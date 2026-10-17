using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Taps;
using Lifestone.Net.Types;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// A network session that answers like the bundled ACE with the Lifestone server mod (steps ST13 to ST15): every server
/// message is a body written by the shared <see cref="AceWireWriters"/> and goes, on the next <see cref="Pump"/>, to the
/// raw tap and then through net's <see cref="InboundDispatcher"/> into the session's reducer (the live decode path), with
/// the network session's own events in net's order. The client's messages go to the tap with plausible bodies. Everything
/// is Lifestone-authored: stand-in guids and names, the Training Academy's cell, no world-database data.
/// </summary>
/// <remarks>
/// <para><b>Script.</b> <see cref="Login"/>: the handshake's phases, the data check (F7E5, the client's F7E6, F7EA), F7E1
/// and F658, then character select. <see cref="EnterWorld"/>: F7DF, the client's F657, F746, the player's hidden
/// CreateObject in the Academy, its PlayerDescription, three stand-in objects and one UpdatePosition. LoginComplete: a
/// SetState that unhides the player. Talk: the speech echo, or, for an <c>@lifestone</c> request, the mod's reply
/// (status and where; anything else is a not-supported error). LogOff: after <see cref="LogOffDelaySeconds"/> the server's
/// F653 and F658, then character select and <c>LoggedOff</c>. Quit and Dispose: Closing, then Closed.</para>
/// <para>Time comes from the given provider only; a reply due later waits for a pump at or after its time.</para>
/// </remarks>
internal sealed class ScriptedAceSession : IAcSession
{
    /// <summary>The first character's guid (the one entered by default).</summary>
    public const uint Amy = 0x50000001;

    /// <summary>The second character (pending deletion).</summary>
    public const uint Bo = 0x50000002;

    /// <summary>The first stand-in object's guid.</summary>
    public const uint FirstObject = 0x80000101;

    /// <summary>The Training Academy's spawn cell (ST12's fix).</summary>
    public const uint AcademyCell = 0x860201AD;

    /// <summary>The account string the list echoes (a stand-in).</summary>
    public const string AccountString = "lifestone-test-account";

    private readonly IAcSessionSink _sink;
    private readonly IRawMessageTap? _tap;
    private readonly TimeProvider _time;
    private readonly List<(long Due, byte[]? Body, SessionEvent Event, Action? Client)> _queue = [];
    private readonly Capture _capture = new();
    private uint _clientSequence;
    private uint _serverEventSequence;
    private uint _fragment;
    private uint _actionSequence;
    private ushort _stateStamp = 1;
    private ushort _teleportStamp = 1;
    private uint _nextGuid = 0x50000010;
    private AcePosition _where = new(AcademyCell, 61.5f, -21.5f, 0.005f);
    private ushort _positionStamp = 2;
    private ushort _forceStamp = 1;
    private bool _teleporting;
    private bool _placed;
    private bool _portalPending;

    /// <summary>The guid of the exit portal the server creates when <see cref="ExitPortalAt"/> is set.</summary>
    public const uint PortalGuid = 0x80000300;

    /// <summary>The PlayerDescription the server sends for the entered character (g7: tests choose the vitals and the character options).</summary>
    public Func<string, AcePlayerDescription> Description { get; set; } = AcePlayerDescription.NewCharacter;

    /// <summary>Where an "Exit Portal" stands in the Academy, or null for none (then Use only answers UseDone).</summary>
    public AcePosition? ExitPortalAt { get; set; }

    /// <summary>
    /// How near, in metres between centres, the player must be for a Use of the portal to teleport it. A Use from farther sends the
    /// player a MoveToObject (as ACE does after an out-of-range Use) and the portal teleports it when a position it sends is in reach.
    /// </summary>
    public float PortalReachMetres { get; set; } = 2f;

    /// <summary>
    /// The quest the portal needs the character to hold, or null for none. To play ACE's quest-gated portal: once the player is in reach, a
    /// character that does not hold the quest in <see cref="Quests"/> gets a WeenieError (0x0474, as the live server sends for the Academy's
    /// "Exit to Holtburg" to a character without the Sentry's quest) and the use done, and no teleport; one that holds it is teleported.
    /// </summary>
    public string? PortalQuest { get; set; }

    /// <summary>The character's quest registry as the scripted mod keeps it (quest name to completions); <c>@lifestone quest</c> changes it.</summary>
    public Dictionary<string, int> Quests { get; } = new(StringComparer.Ordinal);

    /// <summary>The most completions a stamp leaves a quest with (ACE's world row of <c>SentryTaskComplete</c>: one solve).</summary>
    public int QuestMaxCompletions { get; set; } = 1;

    /// <summary>The <c>@lifestone quest</c> requests the scripted mod received, as "has Name", "stamp Name" and "erase Name", in order.</summary>
    public List<string> QuestRequests { get; } = [];

    /// <summary>True to answer every <c>@lifestone quest</c> request with an error (the mod refusing), and change nothing.</summary>
    public bool QuestVerbFails { get; set; }

    /// <summary>True to answer <c>quest stamp</c> and <c>quest erase</c> with an error (the mod refusing, busy or not in the world) and change nothing; <c>quest has</c> still answers.</summary>
    public bool QuestWritesFail { get; set; }

    /// <summary>True to answer <c>quest stamp</c> and <c>quest erase</c> as a success that changed nothing (a reply whose <c>held</c> is what it was before), as a mod that did not do what it said would.</summary>
    public bool QuestWritesDoNothing { get; set; }

    /// <summary>
    /// How the portal's own emote set leaves the character (ACE's weenie 29338 runs it after the teleport, <c>Portal.ActOnUse</c> to <c>EmoteManager.OnPortal</c>): it erases
    /// <see cref="PortalQuest"/> from <see cref="Quests"/>, moves the sanctuary to the destination and enables recalls. False plays a portal that leaves the registry alone.
    /// </summary>
    public bool PortalRunsEmoteSet { get; set; } = true;

    /// <summary>How many times the portal's emote set erased the portal's quest from the registry (a quest the character held).</summary>
    public int PortalQuestErasures { get; private set; }

    /// <summary>The character's sanctuary as the scripted server keeps it: the Academy start until a portal's emote set moves it to the destination.</summary>
    public AcePosition Sanctuary { get; private set; } = new(AcademyCell, 12.32f, -28.48f, 0f);

    /// <summary>The character's RecallsDisabled flag: true at creation (ACE's PlayerFactory), false once a portal's emote set has run.</summary>
    public bool RecallsDisabled { get; private set; } = true;

    /// <summary>
    /// With <see cref="PortalQuest"/>: how far east of the player <c>where</c> puts it once the portal has refused, for the next
    /// <see cref="RefusedWhereLaggedAnswers"/> answers (a server whose view of the walker is behind it, <c>TODO(st16-refused-where)</c>).
    /// </summary>
    public float RefusedWhereOffsetMetres { get; set; }

    /// <summary>With <see cref="RefusedWhereOffsetMetres"/>: how many <c>where</c> answers after the refusal are offset (then the server has caught up).</summary>
    public int RefusedWhereLaggedAnswers { get; set; } = int.MaxValue;

    /// <summary>How many <c>where</c> answers after the refusal were offset by <see cref="RefusedWhereOffsetMetres"/>.</summary>
    public int RefusedWhereLagged { get; private set; }

    /// <summary>Where the portal's teleport puts the player.</summary>
    public AcePosition PortalDestination { get; set; } = new(0xA9B40019, 84f, 7.1f, 94f);

    /// <summary>The Use messages for the portal the server received.</summary>
    public int PortalUses { get; private set; }

    /// <summary>The MoveToObject movements the server sent the player.</summary>
    public int MoveTosSent { get; private set; }

    /// <summary>The portal teleports the server made.</summary>
    public int PortalTeleports { get; private set; }

    /// <summary>
    /// True to announce the portal's teleport twice in a row (F751 and the new position, again with the next teleport sequence), as the live ACE sometimes does
    /// when the Use's MoveTo and the contact with the portal both activate it: the two share one portal-space visit.
    /// </summary>
    public bool PortalTeleportsTwice { get; set; }

    /// <summary>How many times the portal refused for <see cref="PortalQuest"/>.</summary>
    public int PortalRefusals { get; private set; }

    /// <summary>A scripted server for <paramref name="sink"/> on <paramref name="time"/>.</summary>
    public ScriptedAceSession(IAcSessionSink sink, IRawMessageTap? tap, TimeProvider time)
    {
        _sink = sink;
        _tap = tap;
        _time = time;
    }

    /// <summary>Every guid the client asked to enter, in order.</summary>
    public List<uint> EnterRequests { get; } = [];

    /// <summary>The account's characters as the server lists them.</summary>
    public List<AceCharacter> Characters { get; } = [new(Amy, "Amy"), new(Bo, "Bo", 1), new(0x50000003, "Cy")];

    /// <summary>The access level the mod reports.</summary>
    public string Access { get; set; } = "Player";

    /// <summary>The verbs the mod's status reports (the whole list by default; remove one to play a stale mod).</summary>
    public IReadOnlyList<string> StatusVerbs { get; set; } = ToolsWire.Verbs;

    /// <summary>False to answer every tool request with ACE's "Unknown command: lifestone" (no mod).</summary>
    public bool ModLoaded { get; set; } = true;

    /// <summary>How long ACE takes to answer a logoff, in seconds.</summary>
    public double LogOffDelaySeconds { get; set; } = 0.5;

    /// <summary>How far east of the player's position the mod's <c>where</c> reply puts it, in metres (0: where it is).</summary>
    public float WhereOffsetMetres { get; set; }

    /// <summary>
    /// Force-position updates of the player sent after the stand-in objects on entering the world, each with the next
    /// force-position sequence and the player's own position (server corrections that do not move it).
    /// </summary>
    public int ForcePositionsOnEnter { get; set; }

    /// <summary>
    /// True to play ACE for a walk (ST16): the server tracks the player's position, answers <c>place loc</c> with the
    /// reply, an F751 and the destination's position, ends the teleport at LoginComplete, echoes every position the client
    /// sends as an F748 UpdatePosition, and reports the tracked position in <c>where</c>.
    /// </summary>
    public bool AcceptsMovement { get; set; }

    /// <summary>With <see cref="AcceptsMovement"/>: where the server has the player now.</summary>
    public AcePosition PlayerAt => _where;

    /// <summary>With <see cref="AcceptsMovement"/>: how far east of the player's position <c>where</c> puts it once the player was placed by <c>place loc</c> (a server that disagrees about the walk).</summary>
    public float WalkWhereOffsetMetres { get; set; }

    /// <summary>With <see cref="AcceptsMovement"/>: after this many echoes the server sends one force-position correction (0: never).</summary>
    public int CorrectAfterEchoes { get; set; }

    /// <summary>With <see cref="AcceptsMovement"/>: the position messages the server received while the player was teleporting (ignored, as ACE does).</summary>
    public int IgnoredWhileTeleporting { get; private set; }

    /// <summary>With <see cref="AcceptsMovement"/>: the echoes sent so far.</summary>
    public int Echoes { get; private set; }

    /// <summary>With <see cref="AcceptsMovement"/>: the position messages (MoveToState, AutonomousPosition, Jump) received from the player.</summary>
    public int PositionMessages { get; private set; }

    /// <summary>The credentials <see cref="Login"/> received (their password is never read).</summary>
    public List<AcCredentials> Credentials { get; } = [];

    /// <summary>What the client sent, in order: the opcode and, for a GameAction, its type.</summary>
    public List<(uint Opcode, uint Action)> Sent { get; } = [];

    /// <summary>The texts talked.</summary>
    public List<string> Talked { get; } = [];

    /// <summary>Pumps so far.</summary>
    public int Pumps { get; private set; }

    /// <summary>True after <see cref="Dispose"/>.</summary>
    public bool Disposed { get; private set; }

    /// <summary>Times <see cref="Quit"/> was called.</summary>
    public int Quits { get; private set; }

    /// <inheritdoc/>
    public SessionPhase Phase { get; private set; } = SessionPhase.Idle;

    /// <inheritdoc/>
    public SessionFailure? Failure { get; private set; }

    /// <inheritdoc/>
    public CharacterListMsg? CharacterList => _capture.List;

    /// <inheritdoc/>
    public uint? PlayerGuid { get; private set; }

    /// <inheritdoc/>
    public LinkStats Link => default;

    /// <summary>Makes the session fail at the next pump (a stopped server).</summary>
    public void FailNow(SessionFailureKind kind = SessionFailureKind.ConnectTimeout, string message = "the server stopped answering") =>
        Event(SessionEvent.Failed(Now, new SessionFailure(kind, Phase, message)));

    /// <inheritdoc/>
    public void Login(AcCredentials credentials)
    {
        Check();
        if (Phase != SessionPhase.Idle)
        {
            throw new InvalidOperationException("the session is not idle");
        }

        Credentials.Add(credentials);
        Phase = SessionPhase.Connecting;
        PhaseChange(SessionPhase.Idle, SessionPhase.Connecting);
        PhaseChange(SessionPhase.Connecting, SessionPhase.Handshaking);
        Event(SessionEvent.ConnectRequestReceived(Now, SessionPhase.Handshaking, 1000.0));
        PhaseChange(SessionPhase.Handshaking, SessionPhase.DataCheck);
        Message(AceWireWriters.DddInterrogation());
        Event(SessionEvent.DataCheckStarted(Now, SessionPhase.DataCheck));
        Later(() => Outbound(MessageOpcodes.DddInterrogationResponse, 0, w => w.Write(2072u)));
        Message(AceWireWriters.DddEnd());
        Event(SessionEvent.DataCheckComplete(Now, SessionPhase.DataCheck));
        Message(AceWireWriters.ServerName("Lifestone Test"));
        Message(AceWireWriters.CharacterList(Characters, AccountString));
        PhaseChange(SessionPhase.DataCheck, SessionPhase.CharacterSelect);
    }

    /// <inheritdoc/>
    public PumpResult Pump(TimeSpan budget)
    {
        Check();
        Pumps++;
        var now = Now;
        var messages = 0;
        while (_queue.Count > 0 && _queue[0].Due <= now)
        {
            var (_, body, e, client) = _queue[0];
            _queue.RemoveAt(0);
            if (client is not null)
            {
                client();
                continue;
            }

            if (body is null)
            {
                Apply(e);
                _sink.OnSessionEvent(e with { Timestamp = now });
                continue;
            }

            var fragment = ++_fragment;
            _tap?.OnMessage(MessageDirection.Inbound, now, fragment, MessageQueue.Weenie, body);
            var context = new InboundContext(fragment, MessageQueue.Weenie, now);
            InboundDispatcher.Dispatch(context, body, _capture);
            InboundDispatcher.Dispatch(context, body, _sink);
            messages++;
        }

        return new PumpResult(messages, messages, 0, false);
    }

    /// <inheritdoc/>
    public ValueTask<bool> WaitForInboundAsync(TimeSpan max, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_queue.Count > 0 && _queue[0].Due <= Now);

    /// <inheritdoc/>
    public OutboundTicket CreateCharacter(in CharacterCreateRequest request)
    {
        Check();
        request.Validate();
        var name = request.Name;
        var ticket = Outbound(MessageOpcodes.CharacterCreate, 0, w => Write16(w, name));
        var guid = _nextGuid++;
        Characters.Add(new AceCharacter(guid, name));
        Message(AceWireWriters.CharacterCreateResponse(1, guid, name));
        return ticket;
    }

    /// <inheritdoc/>
    public OutboundTicket DeleteCharacter(int listIndex)
    {
        Check();
        return Outbound(MessageOpcodes.CharacterDelete, 0, w => w.Write(listIndex));
    }

    /// <inheritdoc/>
    public OutboundTicket RestoreCharacter(uint guid)
    {
        Check();
        return Outbound(MessageOpcodes.CharacterRestore, 0, w => w.Write(guid));
    }

    /// <inheritdoc/>
    public OutboundTicket EnterWorld(uint guid)
    {
        Check();
        if (Phase != SessionPhase.CharacterSelect)
        {
            throw new InvalidOperationException("not at character select");
        }

        var name = Characters.First(c => c.Guid == guid).Name;
        EnterRequests.Add(guid);
        var ticket = Outbound(MessageOpcodes.CharacterEnterWorldRequest, 0, w => w.Write(guid));
        Phase = SessionPhase.EnteringWorld;
        PhaseChange(SessionPhase.CharacterSelect, SessionPhase.EnteringWorld);
        Message(AceWireWriters.EnterWorldServerReady());
        Later(() => Outbound(MessageOpcodes.CharacterEnterWorld, 0, w =>
        {
            w.Write(guid);
            Write16(w, AccountString);
        }));
        Message(AceWireWriters.PlayerCreate(guid));
        PhaseChange(SessionPhase.EnteringWorld, SessionPhase.InWorld);
        PlayerGuid = guid;
        Phase = SessionPhase.InWorld;
        _stateStamp = 1;
        _teleportStamp = 1;
        Message(AceWireWriters.ObjectCreate(new AceCreateObject
        {
            Guid = guid,
            PhysicsState = (uint)(PhysicsState.Hidden | PhysicsState.IgnoreCollisions | PhysicsState.Gravity | PhysicsState.EdgeSlide),
            Position = new AcePosition(AcademyCell, 61.5f, -21.5f, 0.005f),
            Setup = 0x02000001,
            MotionTable = 0x09000001,
            Sequences = Stamps(),
            Name = name,
            WeenieClassId = 1,
            Icon = 0x06001036,
            ItemType = 0x10,
            ObjectDescriptionFlags = 0x8,
        }));
        Message(AceWireWriters.PlayerDescription(guid, ++_serverEventSequence, Description(name)));
        for (var i = 0u; i < 3; i++)
        {
            Message(AceWireWriters.ObjectCreate(new AceCreateObject
            {
                Guid = FirstObject + i,
                PhysicsState = (uint)(PhysicsState.Gravity | PhysicsState.ReportCollisions),
                Position = new AcePosition(AcademyCell, 55f + (2 * i), -20f, 0.005f),
                Setup = 0x02000001,
                Sequences = Stamps(),
                Name = "Stand-in Object " + i,
                WeenieClassId = 100 + i,
                Icon = 0x06001036,
            }));
        }

        Message(AceWireWriters.UpdatePosition(FirstObject, new AcePosition(AcademyCell, 56f, -19f, 0.005f), true, new AceUpdatePositionSequences(1, 2, 1, 1)));
        if (ExitPortalAt is { } portal)
        {
            Message(AceWireWriters.ObjectCreate(new AceCreateObject
            {
                Guid = PortalGuid,
                PhysicsState = (uint)(PhysicsState.Ethereal | PhysicsState.ReportCollisions),
                Position = portal,
                Setup = 0x02000001,
                Sequences = Stamps(),
                Name = "Exit Portal",
                WeenieClassId = 600,
                Icon = 0x06001036,
                ObjectDescriptionFlags = 0x40000,
            }));
        }
        for (var i = 0; i < ForcePositionsOnEnter; i++)
        {
            var stamp = (ushort)(2 + i);
            Message(AceWireWriters.UpdatePosition(guid, new AcePosition(AcademyCell, 61.5f, -21.5f, 0.005f), true, new AceUpdatePositionSequences(1, stamp, 1, stamp)));
        }

        _actionSequence = 0;
        return ticket;
    }

    /// <inheritdoc/>
    public OutboundTicket SendLoginComplete()
    {
        var ticket = Action(GameActionTypes.LoginComplete, _ => { });
        _teleporting = false;
        _stateStamp++;
        Message(AceWireWriters.SetState(PlayerGuid!.Value, (uint)(PhysicsState.Gravity | PhysicsState.EdgeSlide), 1, _stateStamp));
        return ticket;
    }

    /// <inheritdoc/>
    public OutboundTicket SendMoveToState(in RawMotionStateWire state, in AcPosition position, in ObjSeqs sequences, bool contact, bool standingLongJump)
    {
        var ticket = Action(GameActionTypes.MoveToState, w => w.Write(0u));
        Accept(position, contact);
        return ticket;
    }

    /// <inheritdoc/>
    public OutboundTicket SendAutonomousPosition(in AcPosition position, in ObjSeqs sequences, bool contact)
    {
        var cell = position.CellId;
        var ticket = Action(GameActionTypes.AutonomousPosition, w => w.Write(cell));
        Accept(position, contact);
        return ticket;
    }

    /// <inheritdoc/>
    public OutboundTicket SendJump(float extent, Vector3 velocity, in AcPosition position, in ObjSeqs sequences)
    {
        var ticket = Action(GameActionTypes.Jump, w => w.Write(extent));
        Accept(position, contact: false);
        return ticket;
    }

    /// <inheritdoc/>
    public OutboundTicket SendUse(uint guid)
    {
        var ticket = Action(GameActionTypes.Use, w => w.Write(guid));
        if (guid == PortalGuid && ExitPortalAt is { } portal && AcceptsMovement)
        {
            PortalUses++;
            if (DistanceTo(_where, portal) <= PortalReachMetres)
            {
                TeleportThroughPortal();
                return ticket;
            }

            _portalPending = true;
            MoveTosSent++;
            Message(AceWireWriters.UpdateMotion(PlayerGuid!.Value, 1, 2, 2, false, new AceMovementData
            {
                MovementType = 6,
                TargetGuid = PortalGuid,
                OriginCell = portal.Cell,
                OriginX = portal.X,
                OriginY = portal.Y,
                OriginZ = portal.Z,
                ParameterFlags = 0xE03, // CanWalk | CanRun | MoveTowards | UseSpheres | SetHoldKey
                DistanceToObject = 0.5f,
                FailDistance = float.MaxValue,
                WalkRunThreshold = 15f,
                RunRate = 1.8f,
            }));
            return ticket;
        }

        Message(AceWireWriters.UseDone(PlayerGuid!.Value, ++_serverEventSequence));
        return ticket;
    }

    /// <inheritdoc/>
    public OutboundTicket SendQueryHealth(uint guid)
    {
        var ticket = Action(GameActionTypes.QueryHealth, w => w.Write(guid));
        Message(AceWireWriters.UpdateHealth(PlayerGuid!.Value, ++_serverEventSequence, guid, 0.5f));
        return ticket;
    }

    /// <summary>The stat actions sent, as (GameAction type, id, amount); the scripted server does not answer them (the stat trackers are tested with <see cref="FakeAcSession"/> and the stand-in).</summary>
    public List<(uint Type, uint Id, uint Amount)> StatSends { get; } = [];

    /// <inheritdoc/>
    public OutboundTicket SendRaiseAttribute(uint attribute, uint xp) => StatAction(GameActionTypes.RaiseAttribute, attribute, xp);

    /// <inheritdoc/>
    public OutboundTicket SendRaiseVital(uint vital, uint xp) => StatAction(GameActionTypes.RaiseVital, vital, xp);

    /// <inheritdoc/>
    public OutboundTicket SendRaiseSkill(uint skill, uint xp) => StatAction(GameActionTypes.RaiseSkill, skill, xp);

    /// <inheritdoc/>
    public OutboundTicket SendTrainSkill(uint skill, int credits) => StatAction(GameActionTypes.TrainSkill, skill, unchecked((uint)credits));

    /// <summary>Every magic, combat-mode and inventory action sent (M3 1.2 s4a): the game action type and its fields in wire order; no behaviour answers them.</summary>
    public List<(uint Type, uint First, uint Second, uint Third)> WorldActionSends { get; } = [];

    /// <inheritdoc/>
    public OutboundTicket SendCastUntargetedSpell(uint spellId) => WorldAction(GameActionTypes.CastUntargetedSpell, spellId, 0, 0, 1);

    /// <inheritdoc/>
    public OutboundTicket SendCastTargetedSpell(uint target, uint spellId) => WorldAction(GameActionTypes.CastTargetedSpell, target, spellId, 0, 2);

    /// <inheritdoc/>
    public OutboundTicket SendChangeCombatMode(uint mode) => WorldAction(GameActionTypes.ChangeCombatMode, mode, 0, 0, 1);

    /// <inheritdoc/>
    public OutboundTicket SendSuicide()
    {
        WorldActionSends.Add((GameActionTypes.Suicide, 0, 0, 0));
        return Action(GameActionTypes.Suicide, _ => { });
    }

    /// <inheritdoc/>
    public OutboundTicket SendAddSpellFavorite(uint spellId, uint index, uint bar) => WorldAction(GameActionTypes.AddSpellFavorite, spellId, index, bar, 3);

    /// <inheritdoc/>
    public OutboundTicket SendGetAndWieldItem(uint item, uint equipMask) => WorldAction(GameActionTypes.GetAndWieldItem, item, equipMask, 0, 2);

    /// <inheritdoc/>
    public OutboundTicket SendPutItemInContainer(uint item, uint container, uint placement) => WorldAction(GameActionTypes.PutItemInContainer, item, container, placement, 3);

    /// <inheritdoc/>
    public OutboundTicket SendStackableMerge(uint mergeFrom, uint mergeTo, int amount) => WorldAction(GameActionTypes.StackableMerge, mergeFrom, mergeTo, unchecked((uint)amount), 3);

    private OutboundTicket WorldAction(uint type, uint first, uint second, uint third, int fields)
    {
        WorldActionSends.Add((type, first, second, third));
        return Action(type, w =>
        {
            w.Write(first);
            if (fields > 1)
            {
                w.Write(second);
            }

            if (fields > 2)
            {
                w.Write(third);
            }
        });
    }

    private OutboundTicket StatAction(uint type, uint id, uint amount)
    {
        StatSends.Add((type, id, amount));
        var ticket = Action(type, w =>
        {
            w.Write(id);
            w.Write(amount);
        });
        StatHook?.Invoke(type, id, amount);
        return ticket;
    }

    /// <summary>Called after each stat action is accepted (type, id, amount); the hook answers by <see cref="Inject"/>ing the private updates (<see cref="ScriptedMage"/>). Null: no answer.</summary>
    public Action<uint, uint, uint>? StatHook { get; set; }

    /// <summary>Answers a mod request the scripted server does not do itself (<c>xp</c>, <c>spell learn</c>): returns the reply text, or null for the default refusal (<see cref="ScriptedMage"/>).</summary>
    public Func<ToolRequest, string?>? ToolHook { get; set; }

    /// <inheritdoc/>
    public OutboundTicket SendTalk(ReadOnlySpan<char> text)
    {
        var said = text.ToString();
        var ticket = Action(GameActionTypes.Talk, w => Write16(w, said));
        Talked.Add(said);
        const string prefix = "@" + ToolsWire.Command + " ";
        if (!said.StartsWith(prefix, StringComparison.Ordinal))
        {
            var name = Characters.First(c => c.Guid == PlayerGuid).Name;
            Message(AceWireWriters.HearSpeech(said, name, PlayerGuid!.Value, 2));
            return ticket;
        }

        if (!ModLoaded)
        {
            Message(AceWireWriters.ServerMessage(ToolsWire.UnknownCommandText, ToolsWire.UnknownCommandChatType));
            return ticket;
        }

        if (!ToolsWire.TryParseRequest(said[prefix.Length..], out var request, out var error))
        {
            Message(AceWireWriters.ServerMessage(ToolsWire.Reply(ToolReply.Failure(null, ToolVerbs.Status, error), error.Message), ToolsWire.ReplyChatType));
            return ticket;
        }

        string reply;
        switch (request)
        {
            case StatusRequest:
                var status = new StatusData(1, "0.1.0", new string('a', 40), new string('b', 64), "127.0.0.1", 9000, false, Access, "lifestone-test", "Amy", "Open", StatusVerbs);
                reply = ToolsWire.Reply(ToolReply.Success(request.Id, ToolVerbs.Status, status), status.Describe());
                break;
            case WhereRequest:
                var eastOf = _placed ? WalkWhereOffsetMetres : WhereOffsetMetres;
                if (PortalRefusals > 0 && RefusedWhereOffsetMetres != 0f && RefusedWhereLagged < RefusedWhereLaggedAnswers)
                {
                    RefusedWhereLagged++;
                    eastOf += RefusedWhereOffsetMetres;
                }

                var where = new WhereData(ToolsWire.FormatCell(_where.Cell), _where.X + eastOf, _where.Y, _where.Z, _where.Qw, _where.Qx, _where.Qy, _where.Qz, "0x8602FFFF", ToolsWire.FormatCell(_where.Cell), _teleporting, _forceStamp, _teleportStamp);
                reply = ToolsWire.Reply(ToolReply.Success(request.Id, ToolVerbs.Where, where), where.Describe());
                break;
            case QuestRequest quest:
                reply = AnswerQuest(quest);
                break;
            case PlaceLocRequest place when AcceptsMovement:
                var at = place.At;
                var placed = new PlaceData("loc", null, ToolsWire.FormatCell(at.Cell), at.X, at.Y, at.Z, at.Qw, at.Qx, at.Qy, at.Qz);
                _where = new AcePosition(at.Cell, at.X, at.Y, at.Z, at.Qw, at.Qx, at.Qy, at.Qz);
                _teleporting = true;
                _placed = true;
                _teleportStamp++;
                _positionStamp++;
                reply = ToolsWire.Reply(ToolReply.Success(request.Id, ToolVerbs.PlaceLoc, placed), placed.Describe());
                Message(AceWireWriters.ServerMessage(reply, ToolsWire.ReplyChatType));
                Message(AceWireWriters.PlayerTeleport(_teleportStamp));
                Message(AceWireWriters.UpdatePosition(PlayerGuid!.Value, _where, true, new AceUpdatePositionSequences(1, _positionStamp, _teleportStamp, _forceStamp)));
                return ticket;
            default:
                if (ToolHook?.Invoke(request) is { } custom)
                {
                    reply = custom;
                    break;
                }

                var unsupported = new ToolError(ToolErrors.All[0], "the scripted server does not do that");
                reply = ToolsWire.Reply(ToolReply.Failure(request.Id, ToolVerbs.Status, unsupported), unsupported.Message);
                break;
        }

        Message(AceWireWriters.ServerMessage(reply, ToolsWire.ReplyChatType));
        return ticket;
    }

    /// <inheritdoc/>
    public OutboundTicket SendTell(ReadOnlySpan<char> target, ReadOnlySpan<char> text)
    {
        var message = text.ToString();
        return Action(GameActionTypes.Tell, w => Write16(w, message));
    }

    /// <inheritdoc/>
    public void LogOff()
    {
        Check();
        if (Phase != SessionPhase.InWorld)
        {
            throw new InvalidOperationException("not in the world");
        }

        Outbound(MessageOpcodes.CharacterLogOff, 0, w => w.Write(PlayerGuid!.Value));
        Phase = SessionPhase.LoggingOff;
        PhaseChange(SessionPhase.InWorld, SessionPhase.LoggingOff);
        var due = Now + Ticks(LogOffDelaySeconds);
        Message(AceWireWriters.CharacterLogOff(), due);
        Message(AceWireWriters.CharacterList(Characters, AccountString), due);
        Event(SessionEvent.PhaseChanged(due, SessionPhase.LoggingOff, SessionPhase.CharacterSelect), due);
        Event(SessionEvent.LoggedOff(due, SessionPhase.CharacterSelect), due);
    }

    /// <inheritdoc/>
    public void Quit()
    {
        Check();
        Quits++;
        if (Phase is SessionPhase.Closed or SessionPhase.Failed)
        {
            return;
        }

        if (Phase == SessionPhase.InWorld)
        {
            Outbound(MessageOpcodes.CharacterLogOff, 0, w => w.Write(PlayerGuid!.Value));
        }

        var from = Phase;
        Phase = SessionPhase.Closed;
        PlayerGuid = null;
        PhaseChange(from, SessionPhase.Closing);
        PhaseChange(SessionPhase.Closing, SessionPhase.Closed);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Disposed = true;
        _queue.Clear();
    }

    /// <summary>ACE takes a position the client sends (ignored while it has the player teleporting) and echoes it to the player.</summary>
    private void Accept(in AcPosition position, bool contact)
    {
        if (!AcceptsMovement)
        {
            return;
        }

        PositionMessages++;
        if (_teleporting)
        {
            IgnoredWhileTeleporting++;
            return;
        }

        var q = position.Rotation;
        _where = new AcePosition(position.CellId, position.Origin.X, position.Origin.Y, position.Origin.Z, q.W, q.X, q.Y, q.Z);
        _positionStamp++;
        Echoes++;
        Message(AceWireWriters.UpdatePosition(PlayerGuid!.Value, _where, contact, new AceUpdatePositionSequences(1, _positionStamp, _teleportStamp, _forceStamp)));
        if (_portalPending && ExitPortalAt is { } portal && DistanceTo(_where, portal) <= PortalReachMetres)
        {
            TeleportThroughPortal();
            return;
        }

        if (CorrectAfterEchoes > 0 && Echoes == CorrectAfterEchoes)
        {
            _forceStamp++;
            _positionStamp++;
            Message(AceWireWriters.UpdatePosition(PlayerGuid!.Value, _where, contact, new AceUpdatePositionSequences(1, _positionStamp, _teleportStamp, _forceStamp)));
        }
    }

    /// <summary>The mod's <c>quest</c> verb: ACE's QuestManager on the character's registry (a stamp adds a completion up to <see cref="QuestMaxCompletions"/>, an erase removes the row).</summary>
    private string AnswerQuest(QuestRequest quest)
    {
        var word = quest.Action.ToString().ToLowerInvariant();
        QuestRequests.Add(word + " " + quest.Name);
        if (QuestVerbFails)
        {
            var refused = new ToolError(ToolErrors.NotInWorld, "the scripted mod refuses the quest verb");
            return ToolsWire.Reply(ToolReply.Failure(quest.Id, ToolVerbs.Quest, refused), refused.Message);
        }

        if (QuestWritesFail && quest.Action != QuestAction.Has)
        {
            var refused = new ToolError(ToolErrors.Busy, "the scripted mod refuses to change the registry");
            return ToolsWire.Reply(ToolReply.Failure(quest.Id, ToolVerbs.Quest, refused), refused.Message);
        }

        var heldBefore = Quests.TryGetValue(quest.Name, out var before);
        switch (quest.Action)
        {
            case QuestAction.Stamp or QuestAction.Erase when QuestWritesDoNothing:
                break;
            case QuestAction.Stamp:
                Quests[quest.Name] = Math.Min(before + 1, Math.Max(QuestMaxCompletions, before));
                break;
            case QuestAction.Erase:
                Quests.Remove(quest.Name);
                break;
        }

        var held = Quests.TryGetValue(quest.Name, out var after);
        var data = new QuestData(word, quest.Name, heldBefore, before, held, after);
        return ToolsWire.Reply(ToolReply.Success(quest.Id, ToolVerbs.Quest, data), data.Describe());
    }

    /// <summary>The portal's teleport (F751, the player's new position, the use done), as ACE sends it.</summary>
    private void TeleportThroughPortal()
    {
        _portalPending = false;
        if (PortalQuest is { } needed && !Quests.ContainsKey(needed))
        {
            PortalRefusals++;
            Message(AceWireWriters.WeenieError(PlayerGuid!.Value, ++_serverEventSequence, 0x0474));
            Message(AceWireWriters.UseDone(PlayerGuid!.Value, ++_serverEventSequence));
            return;
        }

        PortalTeleports++;
        if (PortalRunsEmoteSet)
        {
            if (PortalQuest is { } taken && Quests.Remove(taken))
            {
                PortalQuestErasures++;
            }

            (Sanctuary, RecallsDisabled) = (PortalDestination, false);
        }

        _teleporting = true;
        _placed = true;
        _teleportStamp++;
        _positionStamp++;
        _where = PortalDestination;
        Message(AceWireWriters.PlayerTeleport(_teleportStamp));
        Message(AceWireWriters.UpdatePosition(PlayerGuid!.Value, _where, true, new AceUpdatePositionSequences(1, _positionStamp, _teleportStamp, _forceStamp)));
        if (PortalTeleportsTwice)
        {
            _teleportStamp++;
            _positionStamp++;
            Message(AceWireWriters.PlayerTeleport(_teleportStamp));
            Message(AceWireWriters.UpdatePosition(PlayerGuid!.Value, _where, true, new AceUpdatePositionSequences(1, _positionStamp, _teleportStamp, _forceStamp)));
        }

        Message(AceWireWriters.UseDone(PlayerGuid!.Value, ++_serverEventSequence));
    }

    private static float DistanceTo(in AcePosition a, in AcePosition b) =>
        (a.Cell >> 16) != (b.Cell >> 16) ? float.MaxValue : MathF.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private long Now => _time.GetTimestamp();

    private static IReadOnlyList<ushort> Stamps() => [1, 1, 1, 1, 1, 1, 1, 1, 1];

    private static void Write16(BinaryWriter w, string text) => AceWireWriters.WriteString16L(w, text);

    private long Ticks(double seconds) => (long)(seconds * _time.TimestampFrequency);

    private void Check() => ObjectDisposedException.ThrowIf(Disposed, this);

    private void Apply(in SessionEvent e)
    {
        if (e.Kind == SessionEventKind.Failed)
        {
            Phase = SessionPhase.Failed;
            Failure = e.Failure;
        }
        else if (e.Kind == SessionEventKind.PhaseChanged)
        {
            Phase = e.Phase;
        }
    }

    private void PhaseChange(SessionPhase from, SessionPhase to) => Event(SessionEvent.PhaseChanged(Now, from, to));

    private void Event(SessionEvent e, long? due = null) => _queue.Add((due ?? Now, null, e, null));

    /// <summary>Queues one server message (a body from <see cref="AceWireWriters"/>) for the next <see cref="Pump"/>, as if it had just arrived.</summary>
    /// <param name="body">The message body.</param>
    public void Inject(byte[] body) => Message(body);

    private void Message(byte[] body, long? due = null) => _queue.Add((due ?? Now, body, default, null));

    /// <summary>A client message net sends by itself when the message before it arrives (F7E6 after F7E5, F657 after F7DF).</summary>
    private void Later(Action send) => _queue.Add((Now, null, default, send));

    private OutboundTicket Action(uint type, Action<BinaryWriter> payload)
    {
        Check();
        if (Phase != SessionPhase.InWorld)
        {
            throw new InvalidOperationException("not in the world");
        }

        return Outbound(MessageOpcodes.GameAction, type, w =>
        {
            w.Write(++_actionSequence);
            w.Write(type);
            payload(w);
        });
    }

    private OutboundTicket Outbound(uint opcode, uint action, Action<BinaryWriter> payload)
    {
        Sent.Add((opcode, action));
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            w.Write(opcode);
            payload(w);
        }

        var body = stream.ToArray();
        _tap?.OnMessage(MessageDirection.Outbound, Now, ++_clientSequence, opcode == MessageOpcodes.GameAction ? MessageQueue.Weenie : MessageQueue.Login, body);
        return new OutboundTicket(_clientSequence, Now);
    }

    /// <summary>Keeps the last character list the client was sent.</summary>
    private sealed class Capture : IInboundMessageHandler
    {
        public CharacterListMsg? List { get; private set; }

        public void OnCharacterList(in InboundContext context, CharacterListMsg message) => List = message;
    }
}

/// <summary>Reads the body of an outbound GameAction (its type at offset 8), for assertions.</summary>
internal static class GameActionBodies
{
    /// <summary>The action type of an F7B1 body, or 0.</summary>
    public static uint TypeOf(ReadOnlySpan<byte> body) =>
        body.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(body) == MessageOpcodes.GameAction ? BinaryPrimitives.ReadUInt32LittleEndian(body[8..]) : 0;
}

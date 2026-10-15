using System.Collections.Immutable;
using System.Numerics;
using Lifestone.Client.Session.State;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>One teleport of a <see cref="StandInVisit"/>.</summary>
/// <param name="After">Seconds after the visit's unhide (session clock).</param>
/// <param name="Cell">The destination cell.</param>
/// <param name="Origin">The destination's local position.</param>
public readonly record struct StandInTeleport(double After, uint Cell, Vector3 Origin);

/// <summary>One object the server places beside the player in a <see cref="StandInVisit"/>.</summary>
/// <param name="Guid">The object id (a server object's: 0x80000000 and up).</param>
/// <param name="Name">The object's name.</param>
/// <param name="Offset">Where it stands, from the player's creation position (AC axes, metres).</param>
/// <param name="Setup">Its Setup (the human's by default: the base body, a placeholder look).</param>
/// <param name="MotionTable">Its motion table (the human's), or 0 for none.</param>
/// <param name="PortalTo">Where a Use of this object takes the player (a stand-in portal; answered only when the host opted in with <see cref="StandInSessions.AnswerWorld"/>), or null for an object that is not a portal.</param>
/// <param name="DescriptionFlags">Object description bits the object's weenie carries besides the portal bit a portal gets (g3b: 0x10 makes an attackable creature, a gold radar blip).</param>
/// <param name="BlipColor">The server's radar blip colour byte (1 to 10), or 0 to send none.</param>
/// <param name="UnhideAfter">Null for an object created in plain sight. Otherwise the server creates it Hidden (with the human's script table, as a real server does for an object it materialises: the Hidden script's bubbles and
/// the UnHide script's fade) and clears the bit this many seconds after the player's unhide (D63: the materialise fade of an object, drawn by the same path as the player's).</param>
public readonly record struct StandInObject(uint Guid, string Name, Vector3 Offset, uint Setup = StandInVisit.HumanSetup, uint MotionTable = StandInVisit.HumanMotionTable, StandInTeleport? PortalTo = null, uint DescriptionFlags = 0, byte BlipColor = 0, double? UnhideAfter = null);

/// <summary>A line someone nearby says in a scripted visit (a HearSpeech, text type 2).</summary>
/// <param name="After">Seconds after the player is unhidden.</param>
/// <param name="Speaker">The speaker's name.</param>
/// <param name="SpeakerGuid">The speaker's object id.</param>
/// <param name="Text">What is said.</param>
public readonly record struct StandInSpeech(double After, string Speaker, uint SpeakerGuid, string Text);

/// <summary>
/// A scripted visit of the world, as the server would send it, for the play-mode checks (<c>--play-checks</c>) that run without a server: the player's
/// F746 and CreateObject (hidden, in the cell named), the unhide (a SetState without Hidden, which a real server sends after LoginComplete), teleports
/// (F751 then the new position with a newer teleport sequence) and the logoff (the phases back to the character list and F658). Everything goes through
/// the session's own reducer, so the events are the ones a live session publishes; nothing here is retail's or the dats'. The player is the
/// first stand-in character (<see cref="StandInSessions.FirstCharacterGuid"/>) on the human Setup and motion table, with no description of its own (the
/// base body: a placeholder look).
/// </summary>
public sealed record StandInVisit
{
    /// <summary>The human Setup (the walker's collision is built from it).</summary>
    public const uint HumanSetup = 0x02000001;

    /// <summary>The human motion table.</summary>
    public const uint HumanMotionTable = 0x09000001;

    /// <summary>
    /// The human's PhysicsScriptTable (PeTable), which ACE puts in a player's PhysicsDesc: its Hidden and UnHide scripts are the bubbles round a character that is
    /// arriving (docs/avatars.md section 10.2).
    /// </summary>
    public const uint HumanScriptTable = 0x34000004;

    /// <summary>The cell the player is created in: Holtburg's town drop point (an outdoor cell).</summary>
    public uint Cell { get; init; } = 0xA9B40019;

    /// <summary>The player's local position in <see cref="Cell"/>.</summary>
    public Vector3 Origin { get; init; } = new(84f, 7.1f, 94f);

    /// <summary>
    /// The objects the server creates right after the player, standing near it where it is created: creatures on the base body with a placeholder look. None by
    /// default.
    /// </summary>
    public ImmutableArray<StandInObject> Objects { get; init; } = [];

    /// <summary>Seconds after the character list when the player enters the world.</summary>
    public double EnterAfter { get; init; } = 0.5;

    /// <summary>
    /// True when the visit does not start by itself: the host calls <see cref="StandInServer.Release"/> (the checks release it when the retail character list is on the screen, so the server
    /// answers as fast as a live one does, about 0.1 s after the list shows). <see cref="EnterAfter"/> is then the seconds from the release.
    /// </summary>
    public bool HoldEntry { get; init; }

    /// <summary>Seconds after the player's creation when the server unhides it (a real server does after LoginComplete, once portal space has played).</summary>
    public double UnhideAfter { get; init; } = 9.0;

    /// <summary>What others say after the unhide (speech the chat windows show). None by default.</summary>
    public ImmutableArray<StandInSpeech> Speech { get; init; } = [];

    /// <summary>The teleports, in order.</summary>
    public ImmutableArray<StandInTeleport> Teleports { get; init; } = [];

    /// <summary>
    /// The player's PlayerDescription, sent as the player is created (what a server sends at login): attributes, vitals and the character's option bits. Lifestone-authored
    /// numbers (placeholders, not any character's); null sends none (the qualities stay unknown).
    /// </summary>
    public StandInPlayerStats? Stats { get; init; } = new();

    /// <summary>The vital updates the server sends after the unhide, in time order. None by default.</summary>
    public ImmutableArray<StandInVitalUpdate> VitalUpdates { get; init; } = [];

    /// <summary>Seconds after the last teleport (or the unhide) when the logoff starts; null: the player stays.</summary>
    public double? LogOffAfter { get; init; }

    /// <summary>Seconds a logoff takes before the character list is back.</summary>
    public double LogOffDelay { get; init; } = 0.5;
}

/// <summary>A PrivateUpdateAttribute2ndLevel the stand-in server sends: <paramref name="After"/> seconds after the unhide, the vital whose current value has id <paramref name="CurrentId"/> (2 health, 4 stamina, 6 mana) becomes <paramref name="Value"/>.</summary>
/// <param name="After">Seconds after the unhide.</param>
/// <param name="CurrentId">The secondary attribute id of the vital's current value.</param>
/// <param name="Value">The new current value.</param>
public readonly record struct StandInVitalUpdate(double After, uint CurrentId, uint Value);

/// <summary>
/// The stand-in player's description (<see cref="StandInVisit.Stats"/>): a Lifestone-authored placeholder, not a character's. Health 5 ranks over half of Endurance 120 is 65; stamina
/// is Endurance, 120; mana is Self 100. The option bits are ACE's default for a new character (0x50C4A54A: bit 21, side-by-side vitals, is clear, so the stacked window shows).
/// </summary>
public sealed record StandInPlayerStats
{
    /// <summary>ACE's default CharacterOptions1 (its <c>CharacterOptions1</c> default for a new character; the PlayerDescription codec's wire writers use the same).</summary>
    public const uint AceDefaultOptions1 = 0x50C4A54A;

    /// <summary>The character's option bits.</summary>
    public uint Options1 { get; init; } = AceDefaultOptions1;

    /// <summary>Strength.</summary>
    public uint Strength { get; init; } = 100;

    /// <summary>Endurance (health and stamina).</summary>
    public uint Endurance { get; init; } = 120;

    /// <summary>Coordination.</summary>
    public uint Coordination { get; init; } = 90;

    /// <summary>Quickness.</summary>
    public uint Quickness { get; init; } = 80;

    /// <summary>Focus.</summary>
    public uint Focus { get; init; } = 70;

    /// <summary>Self (mana).</summary>
    public uint Self { get; init; } = 100;

    /// <summary>Health ranks raised.</summary>
    public uint HealthRanks { get; init; } = 5;

    /// <summary>Current health.</summary>
    public uint Health { get; init; } = 52;

    /// <summary>Current stamina.</summary>
    public uint Stamina { get; init; } = 120;

    /// <summary>Current mana.</summary>
    public uint Mana { get; init; } = 75;

    /// <summary>The description's enchantment registry (what a buffed character's PlayerDescription carries; none by default).</summary>
    public ImmutableArray<EnchantmentDesc> Enchantments { get; init; } = [];

    /// <summary>
    /// A registry shaped like a buffed live character's (138 enchantments with layered duplicates of the same spell, as the live gate's PlayerDescription): float and integer modifiers of
    /// items, a four-layer Endurance buff, and a five-layer stamina-maximum buff whose top layer (+20) is the only one that counts. Lifestone-authored numbers, not any character's.
    /// </summary>
    public static ImmutableArray<EnchantmentDesc> LiveShapedEnchantments()
    {
        var builder = ImmutableArray.CreateBuilder<EnchantmentDesc>(138);
        EnchantmentDesc Make(ushort spell, ushort layer, ushort category, uint power, uint type, uint key, float value, EnchantmentBucket bucket) =>
            new(spell, layer, category, 0, power, 0.0, -1.0, 0x50000001, 0f, 0f, 0.0, type, key, value, 0, bucket);
        for (ushort spell = 0; spell < 25; spell++)
        {
            for (ushort layer = 1; layer <= 4; layer++)
            {
                builder.Add(Make((ushort)(4459 + spell), layer, (ushort)(100 + spell), 50u + layer, 0x2005008, 60u + spell, 0.32f, EnchantmentBucket.Multiplicative)); // multiplicative float of a property key
            }
        }
        for (ushort layer = 1; layer <= 4; layer++)
        {
            builder.Add(Make(1344, layer, 11, 10u * layer, 0x2009001, 2, 5f * layer, EnchantmentBucket.Additive)); // additive Endurance (attribute 2); the top layer (+20) applies
        }
        for (ushort layer = 1; layer <= 5; layer++)
        {
            builder.Add(Make(1360, layer, 12, 10u * layer, 0x2009002, 3, 4f * layer, EnchantmentBucket.Additive)); // additive maximum stamina (secondary attribute 3); the top layer (+20) applies
        }
        for (ushort layer = 1; layer <= 29; layer++)
        {
            builder.Add(Make((ushort)(2000 + layer), 1, (ushort)(200 + layer), 20u, 0x2008004, 40u, 1f, EnchantmentBucket.Additive)); // additive integer of a property key (not a vital)
        }
        return builder.ToImmutable();
    }
}

public static partial class StandInSessions
{
    /// <summary>
    /// Schedules <paramref name="visit"/> on an offline session whose character list the stand-in has already fed (<see cref="CharacterSelect"/>): the
    /// session must be one <c>GameSession.Offline</c> made (its reducer holds the schedule). The visit begins when the list is in; the first stand-in
    /// character enters.
    /// </summary>
    public static void PlayerVisits(IInboundMessageHandler handler, StandInVisit visit)
    {
        ArgumentNullException.ThrowIfNull(visit);
        if (handler is not GameStateReducer { StandIn: { } schedule } reducer)
        {
            throw new ArgumentException("a stand-in visit needs an offline session's reducer (the handler GameSession.Offline passes)", nameof(handler));
        }

        var guid = FirstCharacterGuid;
        var name = reducer.State.Characters.Entries.Count > 0 ? reducer.State.Characters.Entries[0].Name : DefaultCharacters[0].Name;
        if (visit.HoldEntry)
        {
            schedule.HeldEntry = after => ScheduleVisit(schedule, reducer, visit, guid, name, reducer.Clock.Now + after);
            return;
        }

        ScheduleVisit(schedule, reducer, visit, guid, name, reducer.Clock.Now + visit.EnterAfter);
    }

    /// <summary>Lets a held visit (<see cref="StandInVisit.HoldEntry"/>) begin: the player enters <paramref name="after"/> seconds from now (session clock). False when nothing is held.</summary>
    internal static bool ReleaseEntry(IInboundMessageHandler handler, double after)
    {
        if (handler is not GameStateReducer { StandIn: { HeldEntry: { } held } schedule })
        {
            return false;
        }

        schedule.HeldEntry = null;
        held(after);
        return true;
    }

    private static void ScheduleVisit(StandInSchedule schedule, GameStateReducer reducer, StandInVisit visit, uint guid, string name, double enterAt)
    {
        schedule.At(enterAt, sink => Enter(sink, guid, name, visit));
        var unhideAt = enterAt + visit.UnhideAfter;
        schedule.At(unhideAt, sink => Unhide(sink, guid));
        foreach (var obj in visit.Objects)
        {
            if (obj.UnhideAfter is { } objectWait)
            {
                var guidToUnhide = obj.Guid;
                schedule.At(unhideAt + objectWait, sink => UnhideObject(sink, guidToUnhide));
            }
        }

        foreach (var line in visit.Speech)
        {
            schedule.At(unhideAt + line.After, sink => sink.OnSpeech(new InboundContext(0, MessageQueue.SmartBox, sink is GameStateReducer r ? r.Clock.Time.GetTimestamp() : 0), new SpeechMsg(line.Text, line.Speaker, line.SpeakerGuid, 2, null)));
        }

        foreach (var update in visit.VitalUpdates)
        {
            var sequence = (byte)(1 + visit.VitalUpdates.IndexOf(update));
            schedule.At(unhideAt + update.After, sink => sink.OnPrivateUpdate(WorldContext(sink), new PrivateUpdateMsg(PrivateUpdateKind.VitalCurrent, sequence, update.CurrentId, update.Value, default, default, default)));
        }

        ushort teleport = 1;
        var at = unhideAt;
        foreach (var jump in visit.Teleports)
        {
            at += jump.After;
            var sequence = ++teleport;
            schedule.At(at, sink => SendTeleport(sink, guid, jump, sequence));
        }

        if (visit.LogOffAfter is { } wait)
        {
            schedule.At(at + wait, sink => BeginLogOff(sink, visit.LogOffDelay, schedule, reducer));
        }
    }

    /// <summary>
    /// Lets the offline session answer the world's actions the way a server would for <paramref name="visit"/>: a server-mod <c>place</c> request, a Use of
    /// one of the visit's portal objects (<see cref="StandInObject.PortalTo"/>) and a log off, <paramref name="delay"/> later on the session clock (default
    /// <see cref="DefaultWorldDelay"/>); the tools then work on this session. Opt-in: the play-mode checks call it; every other offline session refuses
    /// those actions as before. Calling it again (a reconnect runs the stand-in again) replaces the visit.
    /// </summary>
    /// <param name="handler">The offline session's reducer (the handler <c>GameSession.Offline</c> passes).</param>
    /// <param name="visit">The visit whose portals and log-off delay the answers use.</param>
    /// <param name="delay">How long an answer takes.</param>
    public static void AnswerWorld(IInboundMessageHandler handler, StandInVisit visit, TimeSpan? delay = null)
    {
        ArgumentNullException.ThrowIfNull(visit);
        if (handler is not GameStateReducer { StandIn: not null } reducer)
        {
            throw new ArgumentException("answering the world needs an offline session's reducer (the handler GameSession.Offline passes)", nameof(handler));
        }

        var wait = delay ?? DefaultWorldDelay;
        ArgumentOutOfRangeException.ThrowIfLessThan(wait, TimeSpan.Zero, nameof(delay));
        reducer.LocalResponder ??= new StandInResponder(reducer, DefaultAnswerDelay.TotalSeconds);
        reducer.LocalResponder.World = new StandInWorld(reducer, visit, wait.TotalSeconds);
    }

    /// <summary>
    /// Ends the session <paramref name="after"/> seconds from now (session clock) in failure, as the network session would: a boot (F7DC,
    /// <see cref="SessionFailureKind.Booted"/>) or a server that went quiet (<see cref="SessionFailureKind.Silence"/>) while the player is in the world. The
    /// retail disconnected screen reports it; its button reconnects (D43).
    /// </summary>
    /// <param name="handler">The offline session's reducer.</param>
    /// <param name="after">Seconds from now on the session clock.</param>
    /// <param name="failure">The failure (its message is shown; Lifestone-authored text only).</param>
    public static void FailLater(IInboundMessageHandler handler, double after, SessionFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (handler is not GameStateReducer { StandIn: { } schedule } reducer)
        {
            throw new ArgumentException("a stand-in failure needs an offline session's reducer (the handler GameSession.Offline passes)", nameof(handler));
        }

        schedule.At(reducer.Clock.Now + after, sink => sink.OnSessionEvent(SessionEvent.Failed(Now(sink), failure)));
    }

    /// <summary>
    /// The server boots the player <paramref name="after"/> seconds from now (session clock): F7DC AccountBoot with <paramref name="reason"/>, then the session
    /// fails with <see cref="SessionFailureKind.Booted"/>, as the network session does (the play flow says "the server booted you", the disconnected screen has
    /// the reason).
    /// </summary>
    /// <param name="handler">The offline session's reducer.</param>
    /// <param name="after">Seconds from now on the session clock.</param>
    /// <param name="reason">The boot's reason text (Lifestone-authored), or null.</param>
    public static void BootLater(IInboundMessageHandler handler, double after, string? reason)
    {
        if (handler is not GameStateReducer { StandIn: { } schedule } reducer)
        {
            throw new ArgumentException("a stand-in boot needs an offline session's reducer (the handler GameSession.Offline passes)", nameof(handler));
        }

        schedule.At(reducer.Clock.Now + after, sink =>
        {
            sink.OnAccountBoot(Context(sink), new AccountBootMsg(reason));
            sink.OnSessionEvent(SessionEvent.Failed(Now(sink), new SessionFailure(SessionFailureKind.Booted, SessionPhase.InWorld, reason ?? "the stand-in server booted the session")));
        });
    }

    /// <summary>
    /// The stand-in server's levers for a host that does not name the network layer (the game): <see cref="StandInServer.Boot"/> and
    /// <see cref="StandInServer.GoQuiet"/>.
    /// </summary>
    /// <param name="handler">The offline session's reducer (the handler <c>GameSession.Offline</c> passes).</param>
    public static StandInServer Server(IInboundMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new StandInServer(handler);
    }

    /// <summary>How long a world answer takes when the host does not say.</summary>
    public static readonly TimeSpan DefaultWorldDelay = TimeSpan.FromSeconds(0.3);

    private static InboundContext WorldContext(IAcSessionSink sink) => new(0, MessageQueue.SmartBox, Now(sink));

    private static void Enter(IAcSessionSink sink, uint guid, string name, StandInVisit visit)
    {
        sink.OnSessionEvent(SessionEvent.PhaseChanged(Now(sink), SessionPhase.CharacterSelect, SessionPhase.EnteringWorld));
        sink.OnEnterWorldServerReady(Context(sink));
        sink.OnPlayerCreate(WorldContext(sink), new PlayerCreateMsg(guid));
        if (visit.Stats is { } stats)
        {
            sink.OnPlayerDescription(WorldContext(sink), new GameEventHeader(guid, 1, 0x0013), Description(stats));
        }

        sink.OnSessionEvent(SessionEvent.PhaseChanged(Now(sink), SessionPhase.EnteringWorld, SessionPhase.InWorld));
        var physics = new PhysicsDesc
        {
            Flags = PhysicsDescFlags.CSetup | PhysicsDescFlags.MTable | PhysicsDescFlags.PeTable | PhysicsDescFlags.Position,
            State = PhysicsState.Hidden | PhysicsState.IgnoreCollisions | PhysicsState.Gravity | PhysicsState.EdgeSlide,
            Position = new AcPosition(visit.Cell, visit.Origin, Quaternion.Identity),
            SetupId = StandInVisit.HumanSetup,
            MotionTableId = StandInVisit.HumanMotionTable,
            PhysicsEffectTableId = StandInVisit.HumanScriptTable,
            Timestamps = Stamps(1, 1),
        };
        var weenie = new WeenieDesc { Header = WeenieHeaderFlags.None, Name = name, WeenieClassId = 1, IconId = 0x06000001, ItemType = 0x10, ObjectDescriptionFlags = 0x8 };
        sink.OnCreateObject(WorldContext(sink), new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isUpdate: false);
        foreach (var obj in visit.Objects)
        {
            var hiddenAtCreation = obj.UnhideAfter is not null;
            var objectPhysics = new PhysicsDesc
            {
                Flags = PhysicsDescFlags.CSetup | PhysicsDescFlags.Position | (obj.MotionTable != 0 ? PhysicsDescFlags.MTable : PhysicsDescFlags.None) | (hiddenAtCreation ? PhysicsDescFlags.PeTable : PhysicsDescFlags.None),
                State = hiddenAtCreation
                    ? PhysicsState.Hidden | PhysicsState.IgnoreCollisions | PhysicsState.Gravity | PhysicsState.EdgeSlide
                    : PhysicsState.Gravity | PhysicsState.ReportCollisions | PhysicsState.EdgeSlide,
                PhysicsEffectTableId = hiddenAtCreation ? StandInVisit.HumanScriptTable : null,
                Position = new AcPosition(visit.Cell, visit.Origin + obj.Offset, Quaternion.Identity),
                SetupId = obj.Setup,
                MotionTableId = obj.MotionTable != 0 ? obj.MotionTable : null,
                Timestamps = Stamps(1, 1),
            };
            // g3b: every stand-in object is shown on the radar (RadarBehavior 4, show always; a placeholder: the stand-in's weenie carries no data); a portal has the portal bit, and a server blip colour is sent when the object names one.
            var objectWeenie = new WeenieDesc
            {
                Header = WeenieHeaderFlags.RadarBehavior | (obj.BlipColor != 0 ? WeenieHeaderFlags.RadarBlipColor : WeenieHeaderFlags.None),
                Name = obj.Name,
                WeenieClassId = 2,
                IconId = 0x06000001,
                ItemType = 0x10,
                ObjectDescriptionFlags = obj.DescriptionFlags | (obj.PortalTo is not null ? 0x40000u : 0u),
                RadarBehavior = 4,
                RadarBlipColor = obj.BlipColor != 0 ? obj.BlipColor : null,
            };
            sink.OnCreateObject(WorldContext(sink), new CreateObjectMsg(obj.Guid, ModelData.Empty, objectPhysics, objectWeenie), isUpdate: false);
        }
    }

    private static PlayerDescriptionMsg Description(StandInPlayerStats s) => new()
    {
 Options1 = s.Options1,
        Enchantments = s.Enchantments,
        Attributes =
        [
            new AttributeDesc(1, 0, s.Strength, 0),
            new AttributeDesc(2, 0, s.Endurance, 0),
            new AttributeDesc(3, 0, s.Quickness, 0),
            new AttributeDesc(4, 0, s.Coordination, 0),
            new AttributeDesc(5, 0, s.Focus, 0),
            new AttributeDesc(6, 0, s.Self, 0),
        ],
        Vitals =
        [
            new VitalDesc(1, s.HealthRanks, 0, 0, s.Health),
            new VitalDesc(3, 0, 0, 0, s.Stamina),
            new VitalDesc(5, 0, 0, 0, s.Mana),
        ],
    };

    private static void UnhideObject(IAcSessionSink sink, uint guid) =>
        sink.OnSetState(WorldContext(sink), new SetStateMsg(guid, PhysicsState.Gravity | PhysicsState.ReportCollisions | PhysicsState.EdgeSlide, 1, 2));

    private static void Unhide(IAcSessionSink sink, uint guid) =>
        sink.OnSetState(WorldContext(sink), new SetStateMsg(guid, PhysicsState.Gravity | PhysicsState.EdgeSlide, 1, 2));

    /// <summary>The server's teleport of the player: F751 with <paramref name="sequence"/>, then the new position (a newer teleport sequence).</summary>
    internal static void SendTeleport(IAcSessionSink sink, uint guid, StandInTeleport jump, ushort sequence)
    {
        sink.OnPlayerTeleport(WorldContext(sink), new PlayerTeleportMsg(sequence));
        var position = new AcPosition(jump.Cell, jump.Origin, Quaternion.Identity);
        sink.OnUpdatePosition(
            WorldContext(sink),
            new UpdatePositionMsg(guid, PositionFlags.Grounded, position, null, null, 1, sequence, sequence, 1));
    }

    /// <summary>
    /// The server's log off: the phase LoggingOff, then F653 and the character list after <paramref name="delay"/> seconds, without the
    /// characters in <paramref name="purged"/> (a stand-in purge removed them).
    /// </summary>
    internal static void BeginLogOff(IAcSessionSink sink, double delay, StandInSchedule schedule, GameStateReducer reducer, IReadOnlySet<uint>? purged = null)
    {
        sink.OnSessionEvent(SessionEvent.PhaseChanged(Now(sink), SessionPhase.InWorld, SessionPhase.LoggingOff));
        schedule.At(reducer.Clock.Now + delay, done =>
        {
            done.OnCharacterLogOff(WorldContext(done));
            done.OnCharacterList(Context(done), ListOf(null, purged));
            done.OnSessionEvent(SessionEvent.PhaseChanged(Now(done), SessionPhase.LoggingOff, SessionPhase.CharacterSelect));
        });
    }

    private static PhysicsTimestamps Stamps(ushort all, ushort position) => new(position, all, all, all, all, all, all, all, all);
}

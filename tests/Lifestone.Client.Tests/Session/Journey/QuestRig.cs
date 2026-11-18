using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Client.World;
using Lifestone.Data.World;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// A stand-in ACE for the quest station (M6 item 5 Q3), in <see cref="RungRig"/>'s shape: the mage at Holtburg's lifestone, NPCs as objects whose Use and Give the test scripts (<see cref="OnUse"/>, <see cref="OnGive"/>) with the server's own
/// answers: a Tell, the system lines (the give line, the reward line, the kill counter), the experience property (Int64 1) and a 0x0274 question. The travel seam moves the body to the request's point at once.
/// </summary>
internal sealed class QuestRig
{
    /// <summary>Holtburg's lifestone cell, where the body starts.</summary>
    public const uint HomeCell = 0xA9B40019;

    private readonly List<(double Due, Action What)> _later = [];
    private int _seen;
    private uint _event = 500;
    private byte _int64;
    private ushort _positionStamp = 2;
    private ushort _teleportStamp = 1;
    private ushort _stateStamp = 2;

    public QuestRig(int level = 10)
    {
        R = new WorldActionRig();
        Caster = new ScriptedCaster(R, 426, 275);
        R.PrivateInt(PlayerQualities.LevelProperty, level);
        Put(HomeCell, new Vector3(84, 7.7f, 94));
        _seen = R.H.Net.Sends.Count;
        Travel = new MovingTravel(this);
    }

    public WorldActionRig R { get; }

    public ScriptedCaster Caster { get; }

    public MovingTravel Travel { get; }

    public double Now => R.H.R.Clock.Now;

    /// <summary>The experience property the rig last sent.</summary>
    public long Experience { get; private set; }

    /// <summary>Called a moment after the player's Use of an NPC (its guid).</summary>
    public Dictionary<uint, Action> OnUse { get; } = [];

    /// <summary>Called a moment after the player's Give to an NPC (the NPC's guid; the item's guid in).</summary>
    public Dictionary<uint, Action<uint>> OnGive { get; } = [];

    /// <summary>Everything the server was asked: <c>use:&lt;guid&gt;</c>, <c>give:&lt;item&gt;@&lt;npc&gt;</c>, <c>confirm:&lt;type&gt;:&lt;yes|no&gt;</c>, <c>recipe:&lt;source&gt;@&lt;target&gt;</c>, <c>pick-up:&lt;item&gt;</c>.</summary>
    public List<string> Events { get; } = [];

    /// <summary>M6 8 A: the combat mode changes and the recipe uses, in the order sent (the peace mode comes first).</summary>
    public List<string> Acts { get; } = [];

    /// <summary>M6 8 A: called a moment after a recipe's UseWithTarget (the source and the target's guids).</summary>
    public Action<uint, uint>? OnRecipe { get; set; }

    /// <summary>M6 8 A: called a moment after the answer to a question (its type, and true for Yes).</summary>
    public Action<uint, bool>? OnConfirm { get; set; }

    /// <summary>M6 8 A: a Use of a container: runs in place of the plain Use's UseDone 0, which comes after the 0196 (ContainerTrackers).</summary>
    public Dictionary<uint, Action> OnOpen { get; } = [];

    /// <summary>M6 8 B2a: the items whose pick-up the server refuses on the item's quest timer (ACE's line, no item moved).</summary>
    public HashSet<uint> RefusedPickUps { get; } = [];

    /// <summary>M6 8 A: true: the rig does not answer a combat-mode change (the server leaves the mode as it is).</summary>
    public bool ModeIgnored { get; set; }

    /// <summary>
    /// M6 14 the quest kill: the scripted caster answers her own vitals casts (<see cref="ScriptedCaster.Pump"/>: Revitalize Self and Stamina to Mana Self cast at herself, its regeneration); every cast at another and every untargeted
    /// cast but the press stays this rig's (RungRig's rule). Set once, before the run.
    /// </summary>
    public bool CasterVitals
    {
        get => _casterVitals;
        set
        {
            _casterVitals = value;
            Caster.CastHook = spell => !Lifestone.Client.Session.Vitals.VitalsMeans.StaminaToManaSelf.Contains(spell);
            Caster.TargetedCastHook = (_, target) => target != WorldActionRig.Player;
        }
    }

    private bool _casterVitals;

    public List<string> Lines(string needle) => [.. R.H.Log.Lines.Where(l => l.Contains(needle, StringComparison.Ordinal))];

    /// <summary>M6 16 Q8: the quest steps' kill runs its own old loop (<see cref="JourneyFacts.QuestOwnKillLoop"/>, the skirmish's fallback) in the contexts this rig makes.</summary>
    public bool OwnKillLoop { get; set; }

    /// <summary>A context over the rig: no grounds hold Holtburg, the store in memory.</summary>
    public JourneyContext Context(MemoryCheckpointStore? store = null, IReadOnlyList<HuntingGround>? grounds = null)
    {
        var context = new JourneyContext { Character = "Winnie", Account = "test-tools", Log = R.H.Log, Travel = Travel, Spells = Caster.Table, Grounds = grounds ?? [], QuestStore = store };
        context.Facts.QuestOwnKillLoop = OwnKillLoop;
        return context;
    }

    public IGameSessionView Session => new View(R, this);

    /// <summary>M6 16 E2b: the live positions the rig's views answer with, or null: the session draws no bodies and the server's reports answer.</summary>
    public LivePositions? Live { get; set; }

    /// <summary>M6 16 E2b: the session draws <paramref name="bodies"/> from here on (play mode's avatar set, scripted): a creature stands where its body is drawn.</summary>
    public void Draw(ILiveBodies bodies) => Live = new LivePositions(bodies, R.H.R.Clock);

    /// <summary>M6 16 E2b: the server reports <paramref name="guid"/> <paramref name="metres"/> east of her (its last report; its drawn body is the test's own).</summary>
    public void Reported(uint guid, float metres)
    {
        var here = R.H.State.Self.Position!.Value;
        Assert.True(R.H.State.Objects.TryGet(guid, out var o));
        o.Position = new CellFrame(here.CellId, here.Frame with { Origin = here.Origin + new Vector3(metres, 0, 0) });
    }

    /// <summary>An NPC (or any world object) at a place.</summary>
    public void Npc(uint guid, string name, uint wcid, uint cell, Vector3 at, bool creature = false)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), new AcPosition(cell, at, Quaternion.Identity), name: name);
        R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { Name = name, WeenieClassId = wcid, ItemType = creature ? 0x10u : 0u } }, isUpdate: false);
    }

    /// <summary>An item in the main pack.</summary>
    public void PackItem(uint guid, uint wcid, string name)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { Name = name, Header = WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, WeenieClassId = wcid } }, isUpdate: false);
        R.Server((p, seq) => AceM3Writers.PutObjInContainer(p, seq, guid, WorldActionRig.Player, 0, 0));
    }

    /// <summary>M6 8 A: an item of a chest: its CreateObject, held by the chest.</summary>
    public void ChestItem(uint guid, uint wcid, string name, uint chest)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { Name = name, Header = WeenieHeaderFlags.Container, ContainerId = chest, WeenieClassId = wcid } }, isUpdate: false);
    }

    /// <summary>M6 8 A: GameEvent 0x0196 for the chest, then the UseDone 0 (capture C2's order).</summary>
    public void ChestOpens(uint chest, params uint[] entries)
    {
        R.H.R.Reducer.OnViewContents(R.H.R.Context, new GameEventHeader(WorldActionRig.Player, ++_event, GameEventTypes.ViewContents), new ViewContentsEvent(chest, [.. entries.Select(e => new ViewContentsEntry(e, 0))]));
        R.UseDone();
    }

    /// <summary>M6 8 A: the locked chest's answer: its sound 0x94 and the UseDone 0, no 0196 (@95ef426d WorldObjects/Chest.cs:105-112).</summary>
    public void ChestLocked(uint chest)
    {
        R.Raw(AceWireWriters.Sound(chest, 0x94));
        R.UseDone();
    }

    /// <summary>M6 8 A: the item removed from the pack (ACE's 0x0024).</summary>
    public void Removed(uint item) => R.Raw(AceM3Writers.InventoryRemoveObject(item));

    /// <summary>The NPC's Tell.</summary>
    public void Tell(uint npc, string name, string text) => R.Raw(AceWireWriters.TellEvent(WorldActionRig.Player, ++_event, text, name, npc, WorldActionRig.Player, 0));

    /// <summary>A system chat line (ACE's GameMessageSystemChat).</summary>
    public void System(string text) => R.Raw(AceWireWriters.ServerMessage(text, 0));

    /// <summary>ACE's quest award: the experience property up by <paramref name="amount"/> and the reward line (Player_Xp.cs:106-114 @95ef426d).</summary>
    public void Award(long amount)
    {
        Experience += amount;
        R.Raw(AceWireWriters.PrivateUpdatePropertyInt64(++_int64, 1, Experience));
        System(string.Create(CultureInfo.InvariantCulture, $"You've earned {amount:N0} experience."));
    }

    /// <summary>The accepted give: the item leaves the pack into the NPC (the 0x0024 and the 0x0022) and the give line.</summary>
    public void Taken(uint item, uint npc, string npcName, string itemName)
    {
        R.Raw(AceM3Writers.InventoryRemoveObject(item));
        R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, item, npc, 0, 0));
        System($"You give {npcName} {itemName}.");
    }

    /// <summary>A refused give: the WeenieErrorWithString naming the NPC and the failed save of the item.</summary>
    public void Refused(uint item, uint code, string npcName)
    {
        R.Server((p, s) => AceWireWriters.WeenieErrorWithString(p, s, code, npcName));
        R.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, item, 0));
    }

    /// <summary>A 0x0274 question.</summary>
    public void Question(uint type, uint context, string text) =>
        R.H.R.Reducer.OnConfirmationRequest(R.H.R.Context, new GameEventHeader(WorldActionRig.Player, ++_event, GameEventTypes.CharacterConfirmationRequest), new ConfirmationRequestEvent(type, context, text));

    /// <summary>M6 8 A: GameEvent 0x0276: the server closes the question.</summary>
    public void QuestionDone(uint type, uint context) =>
        R.H.R.Reducer.OnConfirmationDone(R.H.R.Context, new GameEventHeader(WorldActionRig.Player, ++_event, GameEventTypes.CharacterConfirmationDone), new ConfirmationDoneEvent(type, context));

    /// <summary>M6 8 G: called with each targeted cast the server is asked for (spell id, target guid).</summary>
    public Action<uint, uint>? CastAt { get; set; }

    /// <summary>M6 8 G: the War Magic bolts in the book and a wand in the hand, as a fight-back needs.</summary>
    public void Armed(params ushort[] spells)
    {
        foreach (var spell in spells)
        {
            R.Server((p, seq) => AceM3Writers.MagicUpdateSpell(p, seq, spell));
        }

        var wand = WieldedSetups.Wand;
        WieldedSetups.Create(R.H.R, wand);
        R.H.R.Reducer.OnWieldObject(R.H.R.Context, new GameEventHeader(WorldActionRig.Player, ++_event, GameEventTypes.WieldObject), new WieldObjectEvent(wand.Guid, (uint)wand.Slot));
    }

    /// <summary>M6 8 G: an attackable creature <paramref name="metres"/> east of the player that attacks it (a defender notice naming it).</summary>
    public void Attacker(uint guid, string name, float metres, uint wcid = 236)
    {
        Creature(guid, name, metres, wcid);
        R.Server((p, seq) => AceM3Writers.DefenderNotification(p, seq, name, 4, 0.1, 3, 4, false, 0));
    }

    /// <summary>M6 8 G: an attackable creature that has not attacked.</summary>
    public void Creature(uint guid, string name, float metres, uint wcid = 236)
    {
        var here = R.H.State.Self.Position!.Value;
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), new AcPosition(here.CellId, here.Origin + new Vector3(metres, 0, 0), Quaternion.Identity), name: name);
        R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { Name = name, WeenieClassId = wcid, ItemType = HostileReach.CreatureItemType, ObjectDescriptionFlags = HostileReach.AttackableFlag } }, isUpdate: false);
    }

    /// <summary>M6 8 L: the server says her mana is <paramref name="current"/> (the vital's key 6, sent once with a sequence past the seeded one).</summary>
    public void SetMana(uint current) => R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(200, 6, current));

    /// <summary>M6 8 G: the creature is gone (dead: the server deletes it from the client's view).</summary>
    public void Kill(uint guid) => R.H.R.Reducer.OnDeleteObject(R.H.R.Context, new DeleteObjectMsg(guid, 1));

    /// <summary>M6 8 G: a Magic-typed line the server writes to the player (a creature's spell).</summary>
    public void MagicLine(string text) => R.Raw(AceWireWriters.ServerMessage(text, (int)FightTally.MagicChatType));

    /// <summary>M6 8 G: the server moved the body (a respawn after a death, a portal's touch): F751, then the body in its new place and present again a second later.</summary>
    public void Teleport(uint cell, Vector3 at)
    {
        R.H.R.Reducer.OnPlayerTeleport(R.H.R.Context, new PlayerTeleportMsg(++_teleportStamp));
        Put(cell, at);
        After(1.0, () => R.H.Unhide(++_stateStamp));
    }

    /// <summary>Runs <paramref name="what"/> <paramref name="seconds"/> from now.</summary>
    public void After(double seconds, Action what) => _later.Add((Now + seconds, what));

    public void Put(uint cell, Vector3 at) =>
        R.H.R.Reducer.OnUpdatePosition(R.H.R.Context, new UpdatePositionMsg(WorldActionRig.Player, PositionFlags.Grounded, new AcPosition(cell, at, Quaternion.Identity), null, null, 1, ++_positionStamp, _teleportStamp, 1));

    /// <summary>Steps a run over the rig until it ends.</summary>
    /// <param name="pump">M6 16 E5b: a second server's side pumped after the rig's own each step (a scripted shop's: the buys and the sales this rig does not answer), or null.</param>
    public (IStationRun Run, ScenarioStatus Status) Run(IStationRun run, double maxSeconds = 600, double dt = 0.25, Action? pump = null)
    {
        var end = Now + maxSeconds;
        var status = ScenarioStatus.Running;
        while (status == ScenarioStatus.Running && Now < end)
        {
            R.Tick(dt);
            Pump();
            pump?.Invoke();
            R.Tick();
            status = run.Step(new View(R, this), Now);
        }

        Assert.NotEqual(ScenarioStatus.Running, status);
        return (run, status);
    }

    /// <summary>A quest step (the loader's record, built by hand).</summary>
    public static QuestStep Step(int index, QuestStepKind kind, string what, QuestPlace? place = null, QuestThing? target = null, QuestThing? item = null, QuestExpect? expect = null, int? count = null, string? ground = null, bool? answer = null, QuestThing? failItem = null, string? failReply = null) =>
        new() { Index = index, Kind = kind, What = what, Place = place, Target = target, Item = item, Expect = expect ?? new QuestExpect(null, null, null), Source = "test", Todo = null, Count = count, Ground = ground, Answer = answer, FailItem = failItem, FailReply = failReply };

    /// <summary>A quest (the loader's record, built by hand).</summary>
    public static Quest Quest(string key, QuestDone done, int? timer, params QuestStep[] steps) =>
        new() { Key = key, Name = key, Giver = null, Start = new QuestPlace(HomeCell, null, "Holtburg"), Steps = steps, Done = done, TimerSeconds = timer, Sources = ["test"] };

    private void Pump()
    {
        if (_casterVitals)
        {
            Caster.Pump();
        }

        var sends = R.H.Net.Sends;
        while (_seen < sends.Count)
        {
            var send = sends[_seen++];
            if (_casterVitals && ((send.Kind == FakeSendKind.CastTargetedSpell && send.Guid == WorldActionRig.Player) || (send.Kind == FakeSendKind.CastUntargetedSpell && Lifestone.Client.Session.Vitals.VitalsMeans.StaminaToManaSelf.Contains((uint)send.Guid))))
            {
                Events.Add("self:" + (send.Kind == FakeSendKind.CastTargetedSpell ? (uint)send.Index : (uint)send.Guid).ToString(CultureInfo.InvariantCulture));
                continue; // the scripted caster answers it
            }

            switch (send.Kind)
            {
                case FakeSendKind.ChangeCombatMode:
                    Acts.Add("mode:" + send.Amount.ToString(CultureInfo.InvariantCulture));
                    if (!ModeIgnored)
                    {
                        R.CombatModeIs((int)send.Amount);
                    }

                    break;
                case FakeSendKind.CastTargetedSpell:
                    Events.Add($"cast:{(uint)send.Index}@{send.Guid:X8}");
                    CastAt?.Invoke((uint)send.Index, send.Guid);
                    After(1.0, () => R.UseDone());
                    break;
                case FakeSendKind.UseWithTarget:
                    var recipeSource = send.Guid;
                    var recipeTarget = uint.Parse(send.Target!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    Events.Add($"recipe:{recipeSource:X8}@{recipeTarget:X8}");
                    Acts.Add($"recipe:{recipeSource:X8}@{recipeTarget:X8}");
                    if (OnRecipe is { } recipe)
                    {
                        After(0.5, () => recipe(recipeSource, recipeTarget));
                    }

                    break;
                case FakeSendKind.PutItemInContainer:
                    var taken = send.Guid;
                    Events.Add($"pick-up:{taken:X8}");
                    if (RefusedPickUps.Contains(taken))
                    {
                        After(0.25, () => System("You have solved this quest too recently!"));
                        break;
                    }

                    After(0.25, () =>
                    {
                        R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, taken, WorldActionRig.Player, 0, 0));
                    });
                    break;
                case FakeSendKind.NoLongerViewingContents:
                    var closing = send.Guid;
                    Events.Add($"close:{closing:X8}");
                    After(0.25, () => R.H.R.Reducer.OnCloseGroundContainer(R.H.R.Context, new GameEventHeader(WorldActionRig.Player, ++_event, GameEventTypes.CloseGroundContainer), new CloseGroundContainerEvent(closing)));
                    break;
                case FakeSendKind.Use:
                    Events.Add("use:" + send.Guid.ToString("X8", CultureInfo.InvariantCulture));
                    if (OnOpen.TryGetValue(send.Guid, out var opening))
                    {
                        After(0.25, opening);
                        break;
                    }

                    R.UseDone();
                    if (OnUse.TryGetValue(send.Guid, out var use))
                    {
                        After(0.5, use);
                    }

                    break;
                case FakeSendKind.GiveObject:
                    var npc = uint.Parse(send.Target!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    var item = send.Guid;
                    Events.Add($"give:{item:X8}@{npc:X8}");
                    if (OnGive.TryGetValue(npc, out var give))
                    {
                        After(0.5, () => give(item));
                    }

                    break;
                case FakeSendKind.ConfirmationResponse:
                    var answeredType = (uint)send.Amount;
                    var answeredYes = send.Index != 0;
                    Events.Add($"confirm:{answeredType}:{(answeredYes ? "yes" : "no")}");
                    if (OnConfirm is { } confirm)
                    {
                        After(0.5, () => confirm(answeredType, answeredYes));
                    }

                    break;
            }
        }

        for (var i = 0; i < _later.Count; i++)
        {
            if (_later[i].Due <= Now)
            {
                var due = _later[i].What;
                _later.RemoveAt(i--);
                due();
            }
        }
    }

    private sealed class View(WorldActionRig rig, QuestRig owner) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;

        public LivePositions Live => owner.Live ?? LivePositions.ReportsOnly; // read at each ask: a test draws bodies in the middle of a step
    }

    /// <summary>The travel seam: each request moves the body to its point (an object goal: one metre beside the object) and arrives.</summary>
    public sealed class MovingTravel(QuestRig rig) : IJourneyTravel
    {
        public List<TravelRequest> Requests { get; } = [];

        /// <summary>M6 8 G: the steps a request keeps running before it arrives (<see cref="int.MaxValue"/>: until the station stops it); called once at the request, so a test may set an attacker on the body there; null: it arrives at once.</summary>
        public Func<TravelRequest, int>? RunningSteps { get; set; }

        /// <summary>M6 8 G: a failure for a request the seam refuses (the walk ends Failed there, the body unmoved), or null to walk it.</summary>
        public Func<TravelRequest, string?>? Refuse { get; set; }

        public bool Busy => false;

        public ITravelLeg GoTo(IGameSessionView session, TravelRequest request, double now)
        {
            Requests.Add(request);
            return new Leg(rig, request, RunningSteps?.Invoke(request) ?? 0, Refuse?.Invoke(request));
        }

        private sealed class Leg(QuestRig rig, TravelRequest request, int running, string? refused) : ITravelLeg
        {
            private int _steps;

            public TravelMeans Means => TravelMeans.Run;

            public string? Failure => refused;

            public string Fields => "route_m=1.0";

            public TravelStatus Step(IGameSessionView session, double now)
            {
                if (refused is not null)
                {
                    return TravelStatus.Failed;
                }

                if (_steps++ < running)
                {
                    return TravelStatus.Running;
                }

                if (request.ObjectGuid != 0 && session.State.Objects.TryGet(request.ObjectGuid, out var o) && o.Position is { } op)
                {
                    rig.Put(op.CellId, op.Origin + new Vector3(1, 0, 0));
                }
                else
                {
                    rig.Put(request.Destination.Location.Position.CellId, request.Destination.Location.Position.Local);
                }

                return TravelStatus.Arrived;
            }

            public void Pause()
            {
            }

            public void Resume(double now)
            {
            }

            public void Dispose()
            {
            }
        }
    }
}

/// <summary>A fake ground fight (the quest station's sub-run seam): each step it runs <see cref="Tick"/> (the test's server lines) until it says done; its verdict carries <c>until=met</c> or <c>until=not-met</c>.</summary>
internal sealed class FakeFight(Func<int, bool> tick) : IStationRun
{
    private int _steps;
    private bool _done;

    public StationSpec? Spec { get; init; }

    public bool Met { get; set; }

    /// <summary>True: the verdict says <c>until=refused</c> (the server refused the until's item, M6 5 F1).</summary>
    public bool Refused { get; init; }

    /// <summary>M6 7 K: the fight's failure when it ends (it then ends Failed).</summary>
    public string? FailWith { get; init; }

    public string Name => "den-fight";

    public string Stage => _done ? "done" : "fight";

    public string? Failure => _done ? FailWith : null;

    public double? WhereMaxMetres => null;

    public long Unmatched => 0;

    public bool Ok => _done && FailWith is null;

    public Func<int, bool> Tick => tick;

    public string VerdictFields() => $"kills={_steps} until={(Met ? "met" : Refused ? "refused" : "not-met")}";

    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        if (_done)
        {
            return ScenarioStatus.Succeeded;
        }

        _done = tick(_steps++);
        return !_done ? ScenarioStatus.Running : FailWith is null ? ScenarioStatus.Succeeded : ScenarioStatus.Failed;
    }
}

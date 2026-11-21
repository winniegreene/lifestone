using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Journey;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// The scripted-ACE rig of the autopilot's tests (M5 item 4 a3: one copy, used by <see cref="AutopilotTests"/> and <see cref="AutopilotResumeTests"/> through <c>using static</c>). The world is the logistics station's scripted one
/// (<see cref="LogisticsStationTests.Trip"/>: the ground's home vendors scripted from their <c>sells</c>, the pack held from the ground's own stock lines and purse, the scripted experience server); the logistics station is the real catalog's, the buff,
/// the travels, the fight (a kill every two seconds of the session clock, ended only by the external stop) and the death are scripted stations that move the player as theirs do. <see cref="Factory.Wedge"/> makes the specs it names never end.
/// </summary>
internal static class AutopilotRig
{
    public const string Den = "black-spawn-den-aluvian";
    public const string Temple = "tusker-temple-fort-tethana";
    public const double KillSeconds = 2;
    public static readonly string[] CycleNames = ["n", "state", "why", .. AutopilotTally.FieldNames, "casters", "s"]; // M6 2 e: casters= is a state read between the tally and s=, not a tally field
    public static readonly string[] DoneNames = ["ok", "cycles", "stop", .. AutopilotTally.FieldNames, "s"];

    /// <summary>A scripted station run: <see cref="Body"/> is stepped until it returns a status.</summary>
    internal sealed class ScriptedRun(string name, Func<int, ScenarioStatus?> body, Func<string> fields) : IStationRun
    {
        private int _steps;
        private ScenarioStatus _status = ScenarioStatus.Running;

        public string Name => name;

        public string Stage => "scripted";

        public string? Failure { get; set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public bool Ok => _status == ScenarioStatus.Succeeded && !VerdictFails;

        /// <summary>M6 7 U: the run ends Succeeded and its verdict is false (the autopilot's <c>why=verdict</c>).</summary>
        public bool VerdictFails { get; init; }

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            if (_status == ScenarioStatus.Running && body(_steps++) is { } status)
            {
                _status = status;
            }

            return _status;
        }

        public string VerdictFields() => fields();
    }

    /// <summary>The scripted den fight: a kill every <see cref="KillSeconds"/> of the session clock, ended only by the external stop (as the real one, open-ended).</summary>
    internal sealed class ScriptedFight(Action<int>? onKill) : IStationRun, IStoppableFight
    {
        private double _start = double.NaN;
        private bool _ended;

        public string Name => "den-fight";

        public string Stage => "fight";

        public string? Failure => null;

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public bool Ok => _ended;

        public int Kills { get; private set; }

        public FightStopReason? StopReason { get; private set; }

        public void RequestStop(FightStopReason reason) => StopReason ??= reason;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            var clock = session.Clock.Now;
            if (double.IsNaN(_start))
            {
                _start = clock;
            }

            if (StopReason is not null)
            {
                _ended = true;
                return ScenarioStatus.Succeeded;
            }

            while (clock - _start >= (Kills + 1) * KillSeconds)
            {
                Kills++;
                onKill?.Invoke(Kills);
            }

            return ScenarioStatus.Running;
        }

        public string VerdictFields() => string.Create(CultureInfo.InvariantCulture, $"kills={Kills} xp_gained={Kills * 1000} mana_wait_max_s=1.5 mana_tries={Kills} mana_landed={Kills} storeys_visited=2 basement_entries=0 fizzles={Kills / 2}{(StopReason is { } s ? " stop=" + s.Name : string.Empty)}");
    }

    /// <summary>A station of a fixed run maker.</summary>
    internal sealed class Station(string name, StationNeeds needs, Func<JourneyContext, IStationRun> start) : IStation
    {
        public string Name => name;

        public StationNeeds Needs => needs;

        public IStationRun Start(JourneyContext context) => start(context);
    }

    /// <summary>The test's factory: the real catalog's logistics station (its own needs left out: the scripted world has no vitals), the rest scripted; every spec it is asked for is recorded.</summary>
    internal sealed class Factory(LogisticsStationTests.Trip trip, Action<int>? onKill, StationNeeds? deathNeeds = null) : IStationFactory
    {
        private readonly IStationFactory _real = JourneyStations.Create(JourneyPlans.Mage, [], null);

        public List<StationSpec> Made { get; } = [];

        public Action? OnDeathStep { get; set; }

        /// <summary>Says which of the specs it is asked for never end (the watchdogs' tests).</summary>
        public Func<StationSpec, bool>? Wedge { get; set; }

        /// <summary>M5 7 D141: makes the run of the specs it names (a station that acts on the session before it ends); null, or a null answer: the factory's own.</summary>
        public Func<StationSpec, IStationRun?>? Script { get; set; }

        /// <summary>Says which of the specs it is asked for fail, and with what failure text (M5 4 F1, 4-R6): the run ends <see cref="ScenarioStatus.Failed"/> at its first step (a death station dies first, so the autopilot's tally sees the death).</summary>
        public Func<StationSpec, string?>? FailWith { get; set; }

        /// <summary>The first scripted death station hangs from this step on (it has died and stood at the lifestone; M5 4 F1): a drop, a watchdog trip or an abandon then cuts it with the corpse not recovered.</summary>
        public int? HangDeathAt { get; set; }

        /// <summary>The needs of the fight station made (M5 4 F1, 4-R7: a peace need the scripted ACE never answers holds the station in the peace phase).</summary>
        public StationNeeds? FightNeeds { get; set; }

        /// <summary>M5 7 F14: the scripted recall home is refused with 0x049E until a scripted bind station has tied (a mage bound to no lifestone); false: the recall always lands.</summary>
        public bool RefuseRecallUntilBound { get; set; }

        /// <summary>M6 7 AV: the first scripted recall home is refused for a missing component (0x0400), as the real station is: it sets the journey's fact (<see cref="JourneyFacts.LifestoneRecallUnavailable"/>) and fails.</summary>
        public bool RefuseRecallComponents { get; set; }

        private bool RecallRefusedOnce { get; set; }

        /// <summary>M5 7 F14: the scripted bind station succeeds but ties nothing and leaves the mage where it stands (the recall is refused again after it).</summary>
        public bool BindTiesNothing { get; set; }

        /// <summary>M5 7 audit: the scripted den travel is refused untied (ACE's 0x04A3), as the real station is: it sets the journey's fact untied and fails.</summary>
        public bool RefusePortalRecall { get; set; }

        /// <summary>True once a scripted bind station tied the lifestone.</summary>
        public bool Bound { get; private set; }

        /// <summary>M6 item 14: how many scripted bind stations bound (each is a walk to the lifestone a start paid for).</summary>
        public int BindStations { get; private set; }

        /// <summary>M6 item 14: the scripted bind station records the lifestone it used in the journey's facts, as the real one does at the server's "attuned your spirit" (false: it says bound=yes and records no point).</summary>
        public bool BindRecordsPoint { get; set; } = true;

        /// <summary>M6 item 14: the lifestone the scripted bind station says it used (null: the ground's home lifestone).</summary>
        public (uint Cell, Vector3 At)? BindAt { get; set; }

        /// <summary>M6 item 14: the scripted bind station's <c>tied=</c> field (<c>yes</c>, <c>skipped</c>, <c>no</c>).</summary>
        public string BindTied { get; set; } = "yes";

        /// <summary>M5 7 F23: wraps each scripted fight made (a test gives it a buff refresh of its own), or null.</summary>
        public Func<ScriptedFight, IStationRun>? WrapFight { get; set; }

        /// <summary>How many scripted death stations were made.</summary>
        public int DeathStationsMade { get; private set; }

        /// <summary>What the unscripted death station found in <see cref="JourneyFacts.ObservedDeath"/> when it started: its victim notices and whether the tally was running (null: no such station started).</summary>
        public (int VictimNotices, bool Running, int Deaths)? ObservedAtStart { get; private set; }

        /// <summary>The ends the scripted stations were given to the fight's kills, per fight made (the fights of the run, in order).</summary>
        public List<ScriptedFight> Fights { get; } = [];

        public IStation Create(StationSpec spec)
        {
            Made.Add(spec);
            var g = trip.Ground;
            var arrival = g.ArrivalDestination().Location.Position;
            if (Script?.Invoke(spec) is { } scripted)
            {
                return new Station(spec.Name, StationNeeds.None, _ => scripted);
            }

            if (Wedge?.Invoke(spec) == true)
            {
                return new Station(spec.Name, StationNeeds.None, _ => new ScriptedRun(spec.Name, _ => null, () => string.Empty));
            }

            if (FailWith?.Invoke(spec) is { } failure)
            {
                var dies = spec.Kind == StationKinds.Death;
                return new Station(spec.Name, StationNeeds.None, _ => new ScriptedRun(spec.Name, step => dies && step == 0 ? Died() : ScenarioStatus.Failed, () => dies ? "deaths=1 corpse_recovered=no" : string.Empty) { Failure = failure });
            }

            if (spec.Kind == StationKinds.Death && spec.NeedsOverride is not null)
            {
                // a death the autopilot did not script: entered after the respawn with the autopilot's own tally offered; the mage stands at the lifestone, the handler's trip brings it back to the ground
                return new Station(spec.Name, spec.NeedsOverride, c => new ScriptedRun(
                    spec.Name,
                    step =>
                    {
                        if (step == 0)
                        {
                            ObservedAtStart = c.Facts.ObservedDeath is { } seen ? (seen.VictimNotices, seen.Running, seen.Deaths) : null;
                            c.Facts.ObservedDeath = null; // taken, as the real station does
                            OnDeathStep?.Invoke();
                        }

                        return step switch
                        {
                            0 => Stand(g.Home.LifestoneCell, g.Home.LifestonePoint, done: false),
                            _ => Stand(arrival.CellId, arrival.Local),
                        };
                    },
                    () => "deaths=1 corpse_recovered=yes route=recall rebuffed=yes rebuffed_pretrip=no relines=2"));
            }

            var hangs = spec.Kind == StationKinds.Death && ++DeathStationsMade == 1;
            return spec.Kind switch
            {
                StationKinds.Buff => new Station(spec.Name, StationNeeds.None, _ => new ScriptedRun(spec.Name, _ => ScenarioStatus.Succeeded, () => "buffs=24/24")),
                StationKinds.DenTravel => new Station(spec.Name, StationNeeds.None, c => RefusePortalRecall
                    ? new ScriptedRun(spec.Name, _ => { c.Facts.SetPortalTie(g.Key, PortalTie.Untied); return ScenarioStatus.Failed; }, () => "den=none portal_tied=no") { Failure = "den-travel: Primary Portal Recall refused: YouMustLinkToPortalToRecall 0x04A3; travel:ground:untied" }
                    : new ScriptedRun(spec.Name, _ => Stand(arrival.CellId, arrival.Local), () => "route=recall")),
                StationKinds.RecallHome when RefuseRecallComponents && !RecallRefusedOnce => new Station(spec.Name, StationNeeds.None, c => new ScriptedRun(spec.Name, _ => { RecallRefusedOnce = true; c.Facts.LifestoneRecallUnavailable = true; return ScenarioStatus.Failed; }, () => "recalls=0 recall_why=recall:cast") { Failure = "recall-home: recall:cast: ServerError (code 0x400) YouDontHaveAllTheComponents 0x0400: the server refused the cast" }),
                StationKinds.RecallHome => new Station(spec.Name, StationNeeds.None, _ => RefuseRecallUntilBound && !Bound
                    ? new ScriptedRun(spec.Name, _ => ScenarioStatus.Failed, () => "recalls=0") { Failure = "recall-home: recall: cast: ACE refused: YouMustLinkToLifestoneToRecall 0x049E (the character is bound to no lifestone)" }
                    : new ScriptedRun(spec.Name, _ => Stand(g.Home.LifestoneCell, g.Home.LifestonePoint), () => "recalls=1")),
                StationKinds.Bind => new Station(spec.Name, StationNeeds.None, c => new ScriptedRun(
                    spec.Name,
                    _ =>
                    {
                        if (BindTiesNothing)
                        {
                            return ScenarioStatus.Succeeded;
                        }

                        Bound = true;
                        BindStations++;
                        if (BindRecordsPoint)
                        {
                            // M6 item 14: as the real station does at "attuned your spirit": the lifestone the Use was confirmed at (JourneyFacts.BoundAt), which the autopilot keeps as the bind's proof
                            c.Facts.Bound = true;
                            c.Facts.BoundAt = new Lifestone.Data.World.CellFrame(BindAt?.Cell ?? g.Home.LifestoneCell, new Lifestone.Data.Animation.AcFrame(BindAt?.At ?? g.Home.LifestonePoint, Quaternion.Identity));
                        }

                        return Stand(g.Home.LifestoneCell, g.Home.LifestonePoint);
                    },
                    () => "bound=yes tied=" + BindTied)),
                StationKinds.DenFight => new Station(spec.Name, FightNeeds ?? StationNeeds.None, _ => WrapFight is { } wrap ? wrap(Made_Fight(new ScriptedFight(onKill))) : Made_Fight(new ScriptedFight(onKill))),
                StationKinds.Death => new Station(spec.Name, deathNeeds ?? new StationNeeds(StationWhere.Den), _ => new ScriptedRun(
                    spec.Name,
                    step =>
                    {
                        OnDeathStep?.Invoke();
                        if (HangDeathAt is { } hang && step >= hang && hangs)
                        {
                            return null;
                        }

                        return step switch
                        {
                            0 => Died(),
                            1 => Stand(g.Home.LifestoneCell, g.Home.LifestonePoint, done: false),
                            _ => Stand(arrival.CellId, arrival.Local),
                        };
                    },
                    () => "deaths=1 corpse_recovered=yes route=recall rebuffed=yes")),
                StationKinds.Logistics => Relaxed(_real.Create(spec)),
                _ => throw new NotSupportedException("the autopilot asked for kind " + spec.Kind),
            };
        }

        private ScriptedFight Made_Fight(ScriptedFight fight)
        {
            Fights.Add(fight);
            return fight;
        }

        internal ScenarioStatus? Died()
        {
            trip.Vendor.H.R.Bus.Publish(new PlayerDied("You have been slain by a scripted blow!", 0, DeathNoticeSource.VictimNotification)); // the station's own death: not an unscripted one
            return null;
        }

        private ScenarioStatus? Stand(uint cell, Vector3 at, bool done = true)
        {
            trip.Vendor.StandAt(cell, at);
            return done ? ScenarioStatus.Succeeded : null;
        }

        private static Station Relaxed(IStation real) => new(real.Name, StationNeeds.None, real.Start);
    }

    internal sealed class ApWorld
    {
        private static byte _levelSequence = 150;

        private readonly Func<StockLine, int>? _held;
        private LogisticsStationTests.XpServer? _xp;
        private (uint Cell, Vector3 At)? _savedAt;

        /// <summary>The pack a login delivers after a <see cref="LeaveWorld"/> (null: the world's own <c>held</c>): a test that cuts a trip after its purchases says the pack is full again.</summary>
        public Func<StockLine, int>? HeldAfterRelogin { get; set; }

        public ApWorld(string key, ITestOutputHelper output, Func<StockLine, int>? held = null, bool atHome = true, Action<ApWorld, int>? onKill = null, StationNeeds? deathNeeds = null, int sidePacks = 0)
        {
            _held = held;
            Trip = new LogisticsStationTests.Trip(key, held: held, inGround: !atHome, sidePacks: sidePacks).WithOutput(output);
            Factory = new Factory(Trip, onKill is null ? null : k => onKill(this, k), deathNeeds);
            Output = output;
        }

        public LogisticsStationTests.Trip Trip { get; }

        public Factory Factory { get; }

        public ITestOutputHelper Output { get; }

        public HuntingGround Ground => Trip.Ground;

        public double Now => Trip.Vendor.H.R.Clock.Now;

        public IEnumerable<string> Lines => Trip.Lines;

        public SessionPhase Phase => Trip.Vendor.H.State.Phase;

        /// <summary>M5 7 audit: sets the journey's facts of each context made (a bind or a portal tie already proven this session), or null.</summary>
        public Action<JourneyContext>? Prime { get; set; }

        /// <summary>M6 6 MB: the character's quest timers every context made shares (as the host's file-backed ones do across a hand-back), or null: each context keeps its own in memory.</summary>
        public QuestTimers? Quests { get; set; }

        /// <summary>A journey context of this world (<see cref="LogisticsStationTests.Trip.Context"/>, with <see cref="Quests"/> when set), primed.</summary>
        public JourneyContext Context()
        {
            var c = Trip.Context();
            if (Quests is { } quests)
            {
                c = new JourneyContext { Character = c.Character, Account = c.Account, Log = c.Log, Travel = c.Travel, Spells = c.Spells, Dungeon = c.Dungeon, Quests = quests };
            }

            if (KeeperMaker is not null || Spells is not null)
            {
                var spells = Spells ?? c.Spells;
                c = new JourneyContext { Character = c.Character, Account = c.Account, Log = c.Log, Travel = c.Travel, Spells = spells, Dungeon = c.Dungeon, Quests = c.Quests, Keeper = KeeperMaker?.Invoke(c) };
            }

            Prime?.Invoke(c);
            return c;
        }

        public AutopilotScenario Autopilot(AutopilotOptions options, IAutopilotCheckpointStore? store = null, IAutopilotSession? host = null, AutopilotSeed? seed = null) => new(options, Factory, Context(), store, host, seed) { MinStintSeconds = MinStintSeconds, MinSaleSeconds = MinSaleSeconds };

        /// <summary>M6 14 T: the minimum fight stint of the autopilots this world makes (<see cref="TripStint"/>). 0 here: the scripted world's fights last seconds, and each test is about the trigger it names; the stint's own tests set it (<c>AutopilotTripStintTests</c>).</summary>
        public double MinStintSeconds { get; set; }

        /// <summary>M6 safe load: the sale's floor of the autopilots this world makes (<see cref="TripStint.SaleSeconds"/>). 0 here, as the stint: each test is about the trigger it names; the floor's own tests set it.</summary>
        public double MinSaleSeconds { get; set; }

        /// <summary>M6 7 BD: the context's spell table (the scripted world has an empty one by default).</summary>
        public IReadOnlyDictionary<uint, Lifestone.Data.Magic.SpellInfo>? Spells { get; set; }

        /// <summary>M6 7 BD: makes the context's vitals keeper (the scripted world has none by default: the phases go straight to the peace).</summary>
        public Func<JourneyContext, Lifestone.Client.Session.Vitals.VitalsKeeper>? KeeperMaker { get; set; }

        /// <summary>M5 7 audit: the pack holds the ground's kit lines (each to its count) and the mage's four foci, as the build leaves them.</summary>
        public void OwnKit()
        {
            foreach (var line in Ground.Supplies.Kit)
            {
                for (var i = 0; i < line.Count; i++)
                {
                    Trip.Own(line.Name, line.Wcid, VendorRig.MiscType, 1);
                }
            }

            foreach (var focus in GearKit.MageFociWcids)
            {
                Trip.Own("Foci " + focus.ToString(CultureInfo.InvariantCulture), focus, VendorRig.MiscType, 1);
            }
        }

        public void Drop() => Trip.Vendor.H.Phase(Phase, SessionPhase.Failed);

        /// <summary>
        /// What <c>GameSession.Reconnect</c> does to the state (M5 4 F1, 4-R14): the reducer's own <c>Reset</c> (the world is left as on a log off: every object deleted, the player not in the world with no guid, position or qualities, the phase idle). The
        /// position is remembered: the server puts the character where it was.
        /// </summary>
        public void LeaveWorld()
        {
            var at = Trip.Vendor.State.Self.Position;
            _savedAt = at is { } p ? (p.CellId, p.Origin) : _savedAt; // a second reconnect try meets the world already left: the position stays the one before the first reset
            Trip.Vendor.H.R.Reducer.Reset();
        }

        /// <summary>
        /// The login that follows a <see cref="LeaveWorld"/> (M5 4 F1, 4-R14): the player's own object again, the description (the experience and the stats, the pack with the ground's stock lines and the purse) and the coins, the vendors in their places, and the
        /// character where it stood. What the reset forgot is delivered the way a login delivers it, so the classification, the logistics counters (Unread until the description) and the buff cycle's seed are read from a reloaded state.
        /// </summary>
        public void Relogin()
        {
            var vendor = Trip.Vendor;
            var h = vendor.H;
            h.R.EnterWorld();
            h.Unhide();
            vendor.R.Describe([], []);
            vendor.Coins(2_000_000);
            var available = Trip.Xp.Available;
            _xp = new LogisticsStationTests.XpServer(h, 6000UL, available);
            foreach (var v in Ground.Vendors)
            {
                vendor.PlaceAt(v.Guid, v.Name, v.Cell, v.Point, 3f, v.Wcid);
            }

            foreach (var line in Ground.Supplies.Stock)
            {
                var count = (HeldAfterRelogin ?? _held)?.Invoke(line) ?? line.Keep;
                if (count > 0)
                {
                    Trip.Own(line.Name, line.Wcid, VendorRig.MiscType, 1, (ushort)count);
                }
            }

            foreach (var note in Ground.Supplies.Purse.Notes)
            {
                for (var i = 0; i < note.Count; i++)
                {
                    Trip.Own(note.Name, note.Wcid, VendorStock.PromissoryNoteType, (uint)note.Value);
                }
            }

            if (_savedAt is { } saved)
            {
                vendor.StandAt(saved.Cell, saved.At);
            }
        }

        /// <summary>M5 7: sets the held stack of the stock line of weenie <paramref name="wcid"/> (the first item of it in the pack) to <paramref name="count"/>, as a SetStackSize does.</summary>
        public void SetHeld(uint wcid, int count)
        {
            var state = Trip.Vendor.State;
            var entry = state.Self.Inventory.Pack.First(e => state.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == wcid);
            Trip.Vendor.Stack(entry.Guid, (uint)count, (uint)count);
        }

        public void Level(int level) =>
            Trip.Vendor.H.R.Reducer.OnPrivateUpdate(Trip.Vendor.H.R.Context, new PrivateUpdateMsg(PrivateUpdateKind.PropertyInt, ++_levelSequence, PlayerQualities.LevelProperty, level, default, default, default));

        public ScenarioStatus Run(AutopilotScenario autopilot, Func<AutopilotScenario, bool>? until = null, int frames = 40000, Action<AutopilotScenario>? perFrame = null)
        {
            var status = ScenarioStatus.Running;
            for (var i = 0; i < frames && status == ScenarioStatus.Running && until?.Invoke(autopilot) != true; i++)
            {
                Trip.Vendor.R.Tick(0.25);
                Trip.Shops.Pump();
                (_xp ?? Trip.Xp).Pump();
                Trip.Vendor.R.Tick();
                perFrame?.Invoke(autopilot);
                status = autopilot.Step(Trip.View, Now);
            }

            Output.WriteLine(string.Join(Environment.NewLine, Lines.Where(l => l.StartsWith("autopilot", StringComparison.Ordinal) || l.StartsWith("checkpoint", StringComparison.Ordinal) || l.StartsWith("reconnect", StringComparison.Ordinal) || l.StartsWith("watchdog", StringComparison.Ordinal))));
            return status;
        }
    }

    /// <summary>The scripted ACE's wields (Player_Inventory.cs 297-306: 02DA Wielder 0, 02CE 0, then 0023) and combat modes (the private int 40 update), each answered once.</summary>
    public static void AnswerWieldsAndModes(ApWorld w)
    {
        var rig = w.Trip.Vendor.R;
        foreach (var send in w.Trip.Vendor.H.Net.Of(FakeSendKind.GetAndWieldItem).Skip(Answered(w, FakeSendKind.GetAndWieldItem)))
        {
            var item = (uint)send.Guid;
            var sequence = NextSequence(w); // this world's own (a counter shared by the tests in parallel could jump an object's 8-bit stamp past 127)
            rig.Raw(AceM3Writers.PublicUpdateInstanceId(sequence, item, 3, 0));
            rig.Raw(AceM3Writers.PublicUpdatePropertyInt(sequence, item, 10, 0));
            rig.Server((p, s) => AceM3Writers.WieldObject(p, s, item, (int)send.Amount));
        }

        foreach (var send in w.Trip.Vendor.H.Net.Of(FakeSendKind.ChangeCombatMode).Skip(Answered(w, FakeSendKind.ChangeCombatMode)))
        {
            rig.CombatModeIs((int)send.Amount);
        }

        Mark(w);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ApWorld, Dictionary<FakeSendKind, int>> Seen = new();

    private static int Answered(ApWorld w, FakeSendKind kind) => Seen.GetOrCreateValue(w).GetValueOrDefault(kind);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ApWorld, System.Runtime.CompilerServices.StrongBox<byte>> Sequences = new();

    private static byte NextSequence(ApWorld w)
    {
        var box = Sequences.GetValue(w, _ => new System.Runtime.CompilerServices.StrongBox<byte>(60));
        return ++box.Value;
    }

    private static void Mark(ApWorld w)
    {
        var seen = Seen.GetOrCreateValue(w);
        seen[FakeSendKind.GetAndWieldItem] = w.Trip.Vendor.H.Net.Of(FakeSendKind.GetAndWieldItem).Count;
        seen[FakeSendKind.ChangeCombatMode] = w.Trip.Vendor.H.Net.Of(FakeSendKind.ChangeCombatMode).Count;
    }

    public static string Field(string line, string name) =>
        line.Split(' ').First(p => p.StartsWith(name + "=", StringComparison.Ordinal))[(name.Length + 1)..];

    public static int Int(string line, string name) => int.Parse(Field(line, name), CultureInfo.InvariantCulture);

    public static string[] Names(string line, string prefix) => [.. line[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p[..p.IndexOf('=', StringComparison.Ordinal)])];

    public static string[] Kinds(ApWorld w, int from = 0) => [.. w.Factory.Made.Skip(from).Select(s => s.Name)];
}

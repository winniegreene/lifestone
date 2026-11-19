using System.Collections.Immutable;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Tests.Session.Grounds;
using System.Globalization;
using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Options;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Tools;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Cells;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The fight scenario's loop rules (M3 3.1 lane fa, <see cref="FightScenario"/>) over a scripted ACE on the real read model, reducer and Action API: a target in range dies after two hits and the scenario ends with
/// <c>kills=1</c>; a cast with no projectile is a miss and three in a row move to the next waypoint (a scripted <see cref="IFightWalk"/>); an out-of-range answer walks at once; a Virindi (23), a drudge (1610) and
/// a tusker in the chest corner (cell 0x010502D6) are never targeted; a fizzle drops one level once; the seconds limit and the player's death end the run loudly; the stages are logged once each; and the loop allocates
/// nothing while it waits. Nothing here needs the dats or a server.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed partial class FightScenarioTests
{
    private const uint Den = 0x01050375;

    /// <summary>F5's report-only fields after the tour's (a walk with the ground's tour).</summary>
    private const string Purpose = @" walks_by_purpose=seen:\d+,presumed:\d+,due:\d+,return:\d+ holds=\d+ hold_s=\d+ slots_seen_empty=\d+ walk_loops=\d+";

    /// <summary>F5's fields for a walk without the ground's tour.</summary>
    private const string NoPurpose = " walks_by_purpose=n/a holds=n/a hold_s=n/a slots_seen_empty=n/a walk_loops=n/a";

    /// <summary>M5 3 m2: the last fields of the verdict with looting off (the default).</summary>
    private const string LootOff = " loot=off looted=0 kept=0 loot_value=0";
    private const uint HoltburgCell = 0xA9B40019;
    private const uint TuskerSlave = 1628;

    private const uint TuskerGuard = 1629;
    private const uint DeadChestCorner = 0x010502D6;
    private const float PlayerX = 100;
    private const float PlayerY = 20;

    /// <summary>The session view over the scripted rig.</summary>
    private sealed class View(WorldActionRig rig) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;

        /// <summary>M6 14 (<c>m6-live-position</c>): where a creature stands now; the server's report alone unless a test draws bodies (<see cref="Rig.Draw"/>).</summary>
        public LivePositions Live { get; set; } = LivePositions.ReportsOnly;
    }

    /// <summary>A walk that arrives after <see cref="TicksPerLeg"/> ticks, or fails on demand.</summary>
    private sealed class FakeWalk : IFightWalk
    {
        private int _ticks;
        private bool _walking;

        public int TicksPerLeg { get; set; } = 4;

        public int Prepared { get; private set; }

        public int Legs { get; private set; }

        public bool FailNext { get; set; }

        /// <summary>When set, <see cref="Tick"/> returns Failed with this reason on every call (the walker left the floor, 3.1-R4), whatever the scenario is doing.</summary>
        public string? LeaveFloor { get; set; }

        public string? Failure { get; private set; }

        /// <summary>The walk's tour (g6), or null.</summary>
        public IGroundTour? Tour { get; set; }

        /// <summary>The failed legs the walk reports it recovered from (2-R4), or null for a walk that does not count them.</summary>
        public int? Recovered { get; set; }

        int? IFightWalk.LegsRecovered => Recovered;

        /// <summary>When set, <see cref="HasClearLine"/> is false for a target in this cell until a leg has arrived (the wall between the arrival and that plaza).</summary>
        public uint BlockedCell { get; set; }

        public int LineQueries { get; private set; }

        /// <summary>M5 7 F27: when set, no line is clear (a corpse behind a wall).</summary>
        public bool BlockAll { get; set; }

        public bool HasClearLine(in CellFrame from, in CellFrame to)
        {
            LineQueries++;
            return !BlockAll && (BlockedCell == 0 || to.CellId != BlockedCell || ClearFromCells.Contains(from.CellId));
        }

        /// <summary>M5 7 P7s (D148): a line from a spot in one of these cells is clear whatever <see cref="BlockedCell"/> says (the spot round the corner).</summary>
        public HashSet<uint> ClearFromCells { get; } = [];

        /// <summary>M5 7 P7s (D148): when set, <see cref="BlockedCell"/> stays after a leg arrives (the step did not help).</summary>
        public bool KeepBlockedCell { get; set; }

        /// <summary>M5 7 P7s (D148): the cells <see cref="CellsOnLine"/> answers (null: the walk cannot trace, as the interface's default).</summary>
        public List<uint>? TraceCells { get; set; }

        /// <summary>M5 7 P7s (D148): when set, a spot leg's arrival makes these the traced cells (the mage walked round the no-go cell).</summary>
        public List<uint>? TraceCellsAfterSpotLeg { get; set; }

        public int Traces { get; private set; }

        public LineTrace CellsOnLine(in CellFrame from, in CellFrame to, List<uint> cells)
        {
            cells.Clear();
            if (TraceCells is null)
            {
                return LineTrace.NotTraced;
            }

            Traces++;
            cells.AddRange(TraceCells);
            return LineTrace.Traced;
        }

        /// <summary>M5 7 P7s (D148): the floor's room middles the walk offers for a step.</summary>
        public List<CellFrame> Floor { get; } = [];

        public IReadOnlyList<CellFrame> FloorSpots => Floor;

        public void Prepare(IGameSessionView session, double now) => Prepared++;

        public bool BeginLeg(IGameSessionView session, double now, out string description)
        {
            description = "fake leg " + (Legs + 1);
            if (FailNext)
            {
                Failure = "the fake walk is cut";
                return false;
            }

            Legs++;
            _walking = true;
            _ticks = 0;
            _stuckLeg = StuckLegs > 0;
            if (_stuckLeg)
            {
                StuckLegs--;
            }

            return true;
        }

        /// <summary>M5 7 P7b (D139 g): the stuck rungs a stuck leg reports once it has walked <see cref="StuckAfterTicks"/> ticks.</summary>
        public int StuckRungs { get; set; } = FightScenario.WalkStuckRungsGiveUp;

        /// <summary>M5 7 P7b (D139 g): how many of the next tour legs stick (they report <see cref="StuckRungs"/>).</summary>
        public int StuckLegs { get; set; }

        public int StuckAfterTicks { get; set; } = 2;

        /// <summary>The goal cell a leg reports.</summary>
        public uint GoalCell { get; set; } = 0x01050320;

        /// <summary>M5 7 P7b (D139 j): the spots legs were begun to, in order.</summary>
        public List<CellFrame> Spots { get; } = [];

        public int Abandons { get; private set; }

        /// <summary>M5 7 P7i: the stuck spots the fight patched, in order.</summary>
        public List<(CellFrame At, double Until)> Patches { get; } = [];

        public bool AvoidSpot(in CellFrame at, double until, out string description)
        {
            Patches.Add((at, until));
            description = "the fake walk's patch";
            return true;
        }

        private bool _stuckLeg;
        private bool _spotLeg;

        public int LegStuckRungs => _walking && _stuckLeg && _ticks >= StuckAfterTicks ? StuckRungs : 0;

        public uint LegGoalCell => _walking ? (_spotLeg ? Spots[^1].CellId : GoalCell) : 0;

        public bool BeginLegTo(IGameSessionView session, double now, in CellFrame goal, out string description)
        {
            Spots.Add(goal);
            Legs++;
            _walking = true;
            _ticks = 0;
            _stuckLeg = false;
            _spotLeg = true;
            description = "fake spot leg " + Legs;
            return true;
        }

        /// <summary>M6 14 airborne: the ticks a jump stays in flight after <see cref="TakeOff"/>; its landing check runs in the tick that ends the flight, as the runner's does.</summary>
        public int FlightTicks { get; set; } = 6;

        private int _flightLeft;

        /// <summary>M6 14 airborne: the leg's jump leaves the ground now (a leg must be walking).</summary>
        public void TakeOff() => _flightLeft = FlightTicks;

        /// <summary>M6 14 airborne: the leg's jump is in the air (from <see cref="TakeOff"/> to its landing check).</summary>
        public bool InFlight => _walking && _flightLeft > 0;

        bool IFightWalk.JumpInFlight => InFlight;

        /// <summary>M6 14 airborne: what the landing check does besides being logged (the test's body stands again).</summary>
        public Action? Landed { get; set; }

        /// <summary>M6 14 airborne: when set, the walk's ticks in flight, its landing check and its abandons, in order.</summary>
        public List<string>? Events { get; set; }

        public FightWalkStatus Tick(IGameSessionView session, double now)
        {
            if (LeaveFloor is not null)
            {
                Failure = LeaveFloor;
                return FightWalkStatus.Failed;
            }

            if (!_walking)
            {
                return FightWalkStatus.Idle;
            }

            if (_flightLeft > 0)
            {
                if (--_flightLeft > 0)
                {
                    Events?.Add("tick in flight");
                    return FightWalkStatus.Walking;
                }

                Events?.Add("landing check");
                Landed?.Invoke();
                return FightWalkStatus.Walking; // the leg goes on from the far side
            }

            if (++_ticks < TicksPerLeg)
            {
                return FightWalkStatus.Walking;
            }

            _walking = false;
            if (_spotLeg && TraceCellsAfterSpotLeg is { } after)
            {
                TraceCells = after;
            }

            _spotLeg = false;
            if (!KeepBlockedCell)
            {
                BlockedCell = 0;
            }

            return FightWalkStatus.Arrived;
        }

        public void Abandon()
        {
            Events?.Add(InFlight ? "abandon in flight" : "abandon");
            _walking = false;
            _spotLeg = false;
            _flightLeft = 0;
            Abandons++;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>A tour that records what the fight feeds it (g6) and offers nothing.</summary>
    private sealed class RecordingTour : IGroundTour
    {
        public List<(uint Wcid, uint Cell)> Kills { get; } = [];

        public List<(uint Wcid, uint Cell)> Creates { get; } = [];

        public List<uint> KillGuids { get; } = [];

        public List<uint> CreateGuids { get; } = [];

        /// <summary>M5 7 P7b (D139 g): the cells a give-up rested and the creatures it avoided.</summary>
        public List<uint> Failed { get; } = [];

        public List<uint> Avoided { get; } = [];

        public TourPurpose Purpose { get; set; }

        public uint PurposeGuid { get; set; }

        public string PurposeNote => PurposeGuid != 0 ? $"a living Tusker Slave 0x{PurposeGuid:X8}" : string.Empty;

        public void LegFailed(uint cell, double now) => Failed.Add(cell);

        public void Avoid(uint guid, double now) => Avoided.Add(guid);

        public int Waypoints => 1;

        public uint? Next(uint from, double now) => null;

        public void Killed(uint wcid, uint cell, double now, uint guid = 0)
        {
            Kills.Add((wcid, cell));
            KillGuids.Add(guid);
        }

        public void Created(uint wcid, uint cell, double now, uint guid = 0)
        {
            Creates.Add((wcid, cell));
            CreateGuids.Add(guid);
        }
    }

    private enum Shot
    {
        /// <summary>The cast hits (a damage line) or kills when it is the creature's last hit.</summary>
        Normal,

        /// <summary>UseDone 0 with no projectile.</summary>
        NoProjectile,

        /// <summary>The projectile exists but no damage line follows.</summary>
        Dud,

        /// <summary>The projectile exists, stops (zero velocity) 0.2 s later short of the target and no damage line follows: a wall.</summary>
        Wall,

        /// <summary>A tusker that ran at the mage: the bolt stops 0.2 s later where the target now stands, and "X resists your spell" arrives, with no damage line (4.2 R2).</summary>
        Resist,

        /// <summary>The target ran at the mage and the bolt stops beside it with no resist line and no damage line: still not a wall (4.2 R2, the distance rule alone).</summary>
        RanIn,

        /// <summary>WeenieError 0x0402 then UseDone.</summary>
        Fizzle,

        /// <summary>UseDone 0x0550.</summary>
        OutOfRange,

        /// <summary>M6 7 K: UseDone 0x0406 (ACE's MagicTargetOutOfRange).</summary>
        MagicOutOfRange,

        /// <summary>The creature dies (its object is deleted) with no 0x01AD.</summary>
        SilentKill,

        /// <summary>M5 7 F28: the target died during the wind-up (its object is deleted) and the release is answered UseDone 0x042C (TargetNotAcquired).</summary>
        TargetDied,

        /// <summary>M5 7 F28: UseDone 0x042C for a target that is alive.</summary>
        NotAcquired,

        /// <summary>M5 7 D124: the target died during the wind-up (its object is deleted) and the release is answered WeenieError 0x042C, then UseDone 0 (ACE's DoCastSpell and FinishCast).</summary>
        TargetDiedAtRelease,

        /// <summary>M6 14 airborne: UseDone 0x04EB, ACE's YouCantDoThatWhileInTheAir (<c>Player_Magic.cs</c>:105-109 @95ef426d): nothing cast, nothing charged.</summary>
        InTheAir,
    }

    private sealed class Creature(uint guid, uint wcid, string name, int hitsToKill)
    {
        public uint Guid { get; } = guid;

        public uint Wcid { get; } = wcid;

        public string Name { get; } = name;

        public int HitsToKill { get; } = hitsToKill;

        public int Hits { get; set; }

        public bool Dead { get; set; }

        public uint Cell { get; set; }

        public float Y { get; set; }

        /// <summary>M6 14: the creature's health in points, when a test gives it one (<see cref="Rig.Add"/>'s <c>health</c>): the life spells take points, a bolt's hit still counts toward <see cref="HitsToKill"/>.</summary>
        public int Health { get; set; }

        /// <summary>M6 14: its maximum health in points (0: the creature is counted in hits alone).</summary>
        public int MaxHealth { get; init; }
    }

    /// <summary>The scripted ACE and the session over it.</summary>
    private sealed class Rig
    {
        private readonly Dictionary<uint, Creature> _creatures = [];
        private readonly Queue<Shot> _script = new();
        private readonly Queue<(uint Spell, uint Target, double Due)> _pending = new();
        private readonly byte[] _sequence = new byte[16];
        private int _seen;
        private uint _nextProjectile = 0x80009000;
        private double _lastNow = double.NaN;
        private double _manaAcc;
        private readonly Queue<(uint Guid, double Due)> _late = new();
        private readonly Queue<(uint Guid, double Due)> _stops = new();
        private readonly Queue<(uint Spell, uint Target, Shot Shot, double Due)> _lateHits = new();
        private ushort _teleports = 2;
        private readonly Dictionary<uint, Creature> _corpses = [];

        public Rig(uint mana = 400, uint warRanks = 330, IReadOnlyCollection<uint>? known = null, FightOptions? fight = null, BuffOptions? buff = null, float range = 30f, int characterLevel = 0, bool openers = false, bool life = false, uint? lifeRanks = null)
        {
            R = new WorldActionRig();
            Mana = mana;
            Table = BuildTable(range, openers, life);
            R.Spells([.. Table.Values]);
            Fight = fight ?? new FightOptions();
            Buff = buff ?? new BuffOptions();
            View = new View(R);
            Describe(warRanks, known ?? Table.Keys.ToArray(), characterLevel, lifeRanks);
            R.CombatModeIs(CombatModes.NonCombat);
            var h = R.H.R;
            h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(2));
            h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(ActionHarness.Amy, 1, 2, 2, 1, cell: Den, x: PlayerX));
        }

        public WorldActionRig R { get; }

        public IGameSessionView View { get; }

        /// <summary>M6 14 (<c>m6-live-position</c>): the session draws <paramref name="bodies"/> from here on, as play mode's avatar set does (<c>GameSession.UseBodies</c>).</summary>
        public void Draw(ILiveBodies bodies) => ((View)View).Live = new LivePositions(bodies, R.H.R.Clock, R.H.Log);

        public Dictionary<uint, SpellInfo> Table { get; }

        public FightOptions Fight { get; }

        public BuffOptions Buff { get; }

        public uint Mana { get; set; }

        /// <summary>When above zero, a killed creature stays in the world this many seconds after the 0x01AD (ACE's death animation): then the Dead motion and the deletion arrive together.</summary>
        public double DeathDelay { get; set; }

        /// <summary>When above zero, the projectile's hit (the damage line, or the kill) comes this many seconds after its CreateObject: the flight (ACE sends the UseDone at the launch, the hit when the bolt lands).</summary>
        public double HitDelay { get; set; }

        /// <summary>M5 7 D124 (S1b): with <see cref="HitDelay"/>, the next bolt's answer is followed by a damage line of an earlier projectile on another creature (once).</summary>
        public bool StaleDamageLineOnce { get; set; }

        /// <summary>When set, the 0x01AD text is this instead of "You have killed &lt;name&gt;!" (a kill that does not name the target).</summary>
        public string? KillText { get; set; }

        /// <summary>When set, a "place loc" tool request (the den's arrival through its Places row) is answered with a teleport to this cell (the den's arrival is 0x01050375). A "place poi" (the way out of the den, Holtburg) is always answered with <see cref="HoltburgCell"/>.</summary>
        public uint? ArrivalCell { get; set; }

        /// <summary>The place tool requests, in order.</summary>
        public List<string> Places { get; } = [];

        public double ManaRegenPerSecond { get; set; } = 5;

        /// <summary>M5 3 m2: a kill leaves a corpse named for the creature where it lay (its guid is the creature's plus <see cref="CorpseOffset"/>).</summary>
        public bool Corpses { get; set; }

        /// <summary>M5 7 P7b (D139 i): when a target dies, the creature named here for it dies too (the ring's or a stray bolt's splash): its corpse, its own 0x01AD and its deletion follow the target's.</summary>
        public Dictionary<uint, Creature> Splash { get; } = [];

        /// <summary>M5 7 P7b (D139 j): the kill's 0x01AD comes with the target's health at 0 (ACE pushes the selected target's health), so the fight settles the kill before the body is gone.</summary>
        public bool HealthZeroAtKill { get; set; }

        /// <summary>M5 7 P7b (D139 j): with <see cref="DeathDelay"/>, the corpse appears with the deletion, as ACE creates it after the death animation (round 13: about 1.3 s after the notice).</summary>
        public bool CorpseAtDeletion { get; set; }

        /// <summary>The corpse's guid is its creature's plus this.</summary>
        public const uint CorpseOffset = 0x100;

        /// <summary>When set, the server answers the open of a corpse with a UseDone error and no contents.</summary>
        public bool RefuseOpen { get; set; }

        /// <summary>M5 7 F27: ACE's MoveTo brings the player to a corpse this many seconds after the Use (0: at once; the open answers then).</summary>
        public double MoveToSeconds { get; set; }

        /// <summary>M5 7 D123: the next this-many corpse opens are answered "You're too busy!" (UseDone 0x001D, no ViewContents), as ACE answers a Use while the last pick-up's animation runs (Player_Use.cs:199-203 @95ef426d).</summary>
        public int BusyOpens { get; set; }

        /// <summary>M5 7 F27: when set, ACE's MoveTo fails this many seconds after the Use (its 15 s timeout, Player_Move.cs:16 @95ef426d): a UseDone error and no contents.</summary>
        public double? MoveToFailsAfter { get; set; }

        private readonly Queue<(uint Corpse, double Due, bool Fail)> _opens = new();

        /// <summary>
        /// M6 sprint: when above 0, a pick-up lands this many seconds after its send (ACE's pick-up motion, about 1.35 s live); a pick-up whose corpse is no longer the open one by then is refused with no move
        /// (ACE: "Source item not found!", run 4 04:02:22 UTC). 0: at once.
        /// </summary>
        public double PickSeconds { get; set; }

        /// <summary>M6 sprint: the pick-ups refused because their corpse was closed before they landed.</summary>
        public List<uint> PicksLost { get; } = [];

        private readonly Queue<(uint Item, uint Corpse, double Due)> _picks = new();
        private readonly Queue<double> _keyAnswers = new();

        /// <summary>M6 14: the movement controller a test bound with <see cref="BindKeys"/> (its presses and releases), or null: with none bound no key can be pressed.</summary>
        public WalkKeys? Keys { get; private set; }

        /// <summary>M6 14: the seconds after a key press ACE takes to answer the Use whose walk the key ended (its chain's next look and the way back).</summary>
        public double KeyAnswerSeconds { get; set; }

        /// <summary>M6 14: when set, a key press ends nothing on the scripted ACE: the walk goes on to its own end (the cut's late path).</summary>
        public bool KeysIgnored { get; set; }

        /// <summary>M6 14: binds a movement controller that records the keys; a press while ACE's walk to a corpse is under way ends that walk as ACE ends it on a MoveToState: the Use is answered with a UseDone 0 and no contents.</summary>
        public WalkKeys BindKeys()
        {
            Keys = new WalkKeys(this);
            View.Actions.Move.Bind(Keys, false);
            return Keys;
        }

        /// <summary>M6 14: true while the scripted ACE walks the player to a corpse (a Use sent, its open not yet answered).</summary>
        public bool Walking => _opens.Count > 0;

        private void KeyPressed()
        {
            if (KeysIgnored)
            {
                return;
            }

            while (_opens.Count > 0)
            {
                _opens.Dequeue(); // StopExistingMoveToChains: the chain's next look answers the Use unsuccessful (Player_Move.cs:99-107, Player_Use.cs:223-241 @95ef426d)
                _keyAnswers.Enqueue(Now + KeyAnswerSeconds);
            }
        }

        /// <summary>M6 14: the rig's movement controller: it records every press and release and tells the scripted ACE of a press.</summary>
        public sealed class WalkKeys(Rig rig) : IPlayerMovementIntents
        {
            /// <summary>The commands pressed (true) and released (false), in order.</summary>
            public List<(uint Command, bool Start)> Commands { get; } = [];

            public CellFrame Position => rig.View.State.Self.Position ?? default;

            /// <summary>M6 14 airborne: the body is off the ground (a jump or a fall) while a test says so.</summary>
            public bool InTheAir { get; set; }

            public bool OnGround => !InTheAir;

            public bool? ServerMoving => rig.Walking;

            public string? MotionText => rig.Walking ? "the rig's ACE walks her" : "the rig's body stands";

            public string? LastServerMoveToEnd { get; private set; }

            public void HandleCommand(uint command, bool start, float speed = 1f)
            {
                Commands.Add((command, start));
                if (start && rig.Walking && !rig.KeysIgnored)
                {
                    LastServerMoveToEnd = "cancelled (0x0036 ActionCancelled)";
                }

                if (start)
                {
                    rig.KeyPressed();
                }
            }

            public void SetHoldRun(bool on)
            {
            }

            public void SetHoldSidestep(bool on)
            {
            }

            public void ToggleAutoRun()
            {
            }

            public void CommenceJump(double now)
            {
            }

            public void DoJump(double now)
            {
            }
        }

        /// <summary>What a corpse holds: the item's guid, wcid, item type, Value in its description (null: none sent), burden and name.</summary>
        public List<(uint Guid, uint Wcid, uint Type, uint? Value, ushort Burden, string Name)> CorpseItems { get; } = [];

        /// <summary>What the corpse of one creature holds instead of <see cref="CorpseItems"/>, by the creature's guid.</summary>
        public Dictionary<uint, List<(uint Guid, uint Wcid, uint Type, uint? Value, ushort Burden, string Name)>> ItemsOfCreature { get; } = [];

        /// <summary>The Value an Identify of an item answers with (PropertyInt 19), by guid.</summary>
        public Dictionary<uint, int> AppraisedValues { get; } = [];

        /// <summary>M6 14 keep every great cantrip: the spell book an Identify of an item answers with (its cantrips), by guid.</summary>
        public Dictionary<uint, uint[]> AppraisedSpells { get; } = [];

        /// <summary>M6 14 keep every great cantrip: the drops the player sent, in order; a drop the rig takes puts the item on the ground (019A) and lowers the burden by its own.</summary>
        public List<uint> Dropped { get; } = [];

        /// <summary>M6 14 combine: the scripted ACE's recipes, source weenie to result weenie (a pair of the source makes one result, both consumed: the Atlan wand's 790, 792, 791).</summary>
        public Dictionary<uint, uint> Recipes { get; } = [];

        /// <summary>M6 14 combine: the recipe uses sent (source, target), in order.</summary>
        public List<(uint Source, uint Target)> Combined { get; } = [];

        /// <summary>M6 14 combine2: the next this-many recipe uses are answered "You're too busy!" (UseDone 0x001D, nothing used), as ACE answers one while a pick-up's return animation runs.</summary>
        public int BusyRecipes { get; set; }

        private uint _nextMade = 0x8C0F0001;

        /// <summary>M6 14 keep every great cantrip: the next this-many drops are answered "You're too busy!" (WeenieError 0x001D and 0x00A0, Player_Inventory.cs:1373-1377 @95ef426d).</summary>
        public int BusyDrops { get; set; }

        /// <summary>M6 14 keep every great cantrip: the drops ACE refuses outright (0x00A0 with ActionCancelled, the item stays held), by item guid.</summary>
        public HashSet<uint> RefuseDrops { get; } = [];

        /// <summary>M6 1 f: the creature profile an appraisal of a creature answers with (ACE sends its health and maximum health on any roll); none when a test does not give one.</summary>
        public Dictionary<uint, AceCreatureProfile> CreatureProfiles { get; } = [];

        /// <summary>The corpse opens, pick-ups, closes and identifies the player sent, in order.</summary>
        public List<uint> Opened { get; } = [];

        public List<uint> Picked { get; } = [];

        /// <summary>M6 5 F1: the pick-ups the server refuses with the 20 h line ("You have solved this quest too recently!", ACE's QuestManager) instead of moving the item, by item guid.</summary>
        public HashSet<uint> RefusePicks { get; } = [];

        public List<uint> Closed { get; } = [];

        public List<uint> Identified { get; } = [];

        public double CastSeconds { get; set; } = 0.5;

        public uint ManaPerCast { get; set; } = 20;

        public List<int> ModesSent { get; } = [];

        public List<uint> CastTargets { get; } = [];

        /// <summary>M6 7 K: the spells the scripted ACE answered, in order.</summary>
        public List<uint> Answered { get; } = [];

        /// <summary>M6 7 K: the spells ACE refuses, each with its UseDone code.</summary>
        public Dictionary<uint, uint> RefusedBy { get; } = [];

        public List<uint> CastSpells { get; } = [];

        public List<uint> HealthQueries { get; } = [];

        public double Now => R.H.R.Clock.Now;

        public RecordingLogSink Log => R.H.Log;

        public GearKit Kit { get; set; } = GearKit.Of([GearItemOf(GearKit.Wand.Wcid, GearKit.Wand.Name, GearKind.Wand, GearKit.Wand), .. GearKit.Armour.Select(a => GearItemOf(a.Wcid, a.Name, GearKind.Armour, a))]);

        public void Script(params Shot[] shots)
        {
            foreach (var shot in shots)
            {
                _script.Enqueue(shot);
            }
        }

        public Creature Add(uint guid, uint wcid, uint cell, float y, string name = "Tusker Slave", int hitsToKill = 2, float x = PlayerX, int health = 0)
        {
            var creature = new Creature(guid, wcid, name, hitsToKill) { Cell = cell, Y = y, Health = health, MaxHealth = health };
            _creatures[guid] = creature;
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(cell, x, y, 30), name: name);
            R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = wcid } }, isUpdate: false);
            return creature;
        }

        /// <summary>The server moves creature <paramref name="guid"/> to <paramref name="y"/> in <paramref name="cell"/> (once per creature: the position stamp is fixed).</summary>
        public void Move(uint guid, uint cell, float y) =>
            R.H.R.Reducer.OnUpdatePosition(R.H.R.Context, new UpdatePositionMsg(guid, PositionFlags.Grounded, ReducerHarness.Position(cell, PlayerX, y, 30), null, null, 1, 50, 1, 1));

        public FightScenario Scenario(IFightWalk? walk, FightStages stages = FightStages.Mode) =>
            new(Kit, Table, Buff, Fight, walk, R.H.Log, endSession: false, stages: stages);

        /// <summary>One frame: the clock advances, the Action API sends, the scripted ACE answers, the Action API completes, then the scenario steps.</summary>
        public ScenarioStatus Frame(FightScenario scenario, double dt = 0.25)
        {
            R.Tick(dt);
            Pump();
            R.Tick();
            return scenario.Step(View, Now);
        }

        public ScenarioStatus Run(FightScenario scenario, double maxSeconds = 600, double dt = 0.25)
        {
            var end = Now + maxSeconds;
            var status = ScenarioStatus.Running;
            while (status == ScenarioStatus.Running && Now < end)
            {
                status = Frame(scenario, dt);
            }

            return status;
        }

        public void Pump()
        {
            var now = Now;
            Regenerate(now);
            var sends = R.H.Net.Sends;
            while (_seen < sends.Count)
            {
                var send = sends[_seen++];
                switch (send.Kind)
                {
                    case FakeSendKind.ChangeCombatMode:
                        ModesSent.Add((int)send.Amount);
                        R.CombatModeIs((int)send.Amount);
                        break;
                    case FakeSendKind.CastTargetedSpell when send.Guid == WorldActionRig.Player && VitalsMeans.HealSelf.Contains((uint)send.Index):
                    case FakeSendKind.CastUntargetedSpell when VitalsMeans.HealSelf.Contains((uint)send.Index):
                        CastSpells.Add((uint)send.Index); // M6 14: Heal Self, the one spell at herself this rig answers
                        _pending.Enqueue(((uint)send.Index, 0, now + CastSeconds));
                        break;
                    case FakeSendKind.CastTargetedSpell:
                        CastTargets.Add(send.Guid);
                        CastSpells.Add((uint)send.Index);
                        _pending.Enqueue(((uint)send.Index, send.Guid, now + CastSeconds));
                        break;
                    case FakeSendKind.Talk when send.Text is { } text && text.Contains("place", StringComparison.Ordinal):
                        Places.Add(text);
                        if (text.Contains("poi", StringComparison.Ordinal))
                        {
                            Arrive(text, HoltburgCell);
                        }
                        else if (ArrivalCell is { } arrival)
                        {
                            Arrive(text, arrival);
                        }

                        break;
                    case FakeSendKind.Use when _corpses.ContainsKey(send.Guid):
                        Opened.Add(send.Guid);
                        if (BusyOpens > 0)
                        {
                            BusyOpens--;
                            R.UseDone(0x001D);
                        }
                        else if (MoveToFailsAfter is { } failAfter)
                        {
                            _opens.Enqueue((send.Guid, now + failAfter, true));
                        }
                        else if (MoveToSeconds > 0)
                        {
                            _opens.Enqueue((send.Guid, now + MoveToSeconds, false));
                        }
                        else
                        {
                            OpenCorpse(send.Guid);
                        }

                        break;
                    case FakeSendKind.PutItemInContainer:
                        Picked.Add(send.Guid);
                        if (RefusePicks.Contains(send.Guid))
                        {
                            R.Raw(AceWireWriters.ServerMessage("You have solved this quest too recently!", 0));
                            break;
                        }

                        if (PickSeconds > 0)
                        {
                            _picks.Enqueue((send.Guid, R.H.State.Containers.ViewedGuid, now + PickSeconds));
                            break;
                        }

                        R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, send.Guid, WorldActionRig.Player, 0, 0));
                        break;
                    case FakeSendKind.NoLongerViewingContents:
                        Closed.Add(send.Guid);
                        R.H.R.Reducer.OnCloseGroundContainer(R.H.R.Context, new GameEventHeader(WorldActionRig.Player, 900 + (uint)Closed.Count, GameEventTypes.CloseGroundContainer), new CloseGroundContainerEvent(send.Guid));
                        break;
                    case FakeSendKind.Identify:
                        Identified.Add(send.Guid);
                        var answered = new Dictionary<uint, int> { [AppraisalIds.Value] = AppraisedValues.GetValueOrDefault(send.Guid), [AppraisalIds.Burden] = 5 };
                        R.Server((p, s) => AceVendorWriters.IdentifyResponse(p, s, send.Guid, new AceAppraisal { Success = true, Ints = answered, SpellBook = AppraisedSpells.GetValueOrDefault(send.Guid) ?? [], Creature = CreatureProfiles.GetValueOrDefault(send.Guid) })); // M6 1 f: a creature's profile when a test gives one
                        break;
                    case FakeSendKind.DropItem:
                        Dropped.Add(send.Guid); // M6 14 keep every great cantrip: the room's drops
                        if (BusyDrops > 0)
                        {
                            BusyDrops--;
                            R.Server((p, s) => AceWireWriters.WeenieError(p, s, 0x001D));
                            R.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, send.Guid, 0));
                            break;
                        }

                        if (RefuseDrops.Contains(send.Guid))
                        {
                            R.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, send.Guid, 0x0036));
                            break;
                        }

                        var weight = R.H.State.Objects.TryGet(send.Guid, out var lying) ? lying.Weenie.Burden ?? 0 : 0;
                        var burdenLeft = (R.H.State.Self.Inventory.Burden ?? 0) - weight;
                        R.Server((p, s) => AceM3Writers.PutObjectIn3D(p, s, send.Guid));
                        R.PrivateInt(PlayerInventory.EncumbranceProperty, burdenLeft);
                        break;
                    case FakeSendKind.UseWithTarget:
                        var recipeTarget = uint.Parse(send.Target!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        Combined.Add((send.Guid, recipeTarget));
                        if (BusyRecipes > 0)
                        {
                            BusyRecipes--;
                            R.UseDone(0x001D);
                            break;
                        }

                        if (R.H.State.Objects.TryGet(send.Guid, out var recipeSource) && Recipes.TryGetValue(recipeSource.Weenie.WeenieClassId, out var made))
                        {
                            R.Raw(AceM3Writers.InventoryRemoveObject(send.Guid)); // both consumed (the recipe's destroy chances 1)
                            R.Raw(AceM3Writers.InventoryRemoveObject(recipeTarget));
                            var guid = _nextMade++;
                            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: "Made " + made);
                            R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container, WeenieClassId = made, ContainerId = WorldActionRig.Player, ItemType = 0x80 } }, isUpdate: false);
                            R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, guid, WorldActionRig.Player, 0, 0));
                            R.UseDone();
                        }

                        break;
                    case FakeSendKind.QueryHealth:
                        HealthQueries.Add(send.Guid);
                        var c = _creatures[send.Guid];
                        R.Server((p, s) => AceWireWriters.UpdateHealth(p, s, send.Guid, c.MaxHealth > 0 ? (float)c.Health / c.MaxHealth : Math.Max(0f, 1f - ((float)c.Hits / c.HitsToKill))));
                        break;
                }
            }

            while (_keyAnswers.Count > 0 && _keyAnswers.Peek() <= now)
            {
                _keyAnswers.Dequeue();
                R.UseDone(); // M6 14: the Use whose walk a key ended: a UseDone 0 and no contents
            }

            while (_picks.Count > 0 && _picks.Peek().Due <= now)
            {
                var pick = _picks.Dequeue();
                if (R.H.State.Containers.ViewedGuid == pick.Corpse)
                {
                    R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, pick.Item, WorldActionRig.Player, 0, 0));
                }
                else
                {
                    PicksLost.Add(pick.Item);
                    R.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, pick.Item, 0));
                }
            }

            while (_opens.Count > 0 && _opens.Peek().Due <= now)
            {
                var open = _opens.Dequeue();
                if (open.Fail)
                {
                    R.UseDone(); // M6 sprint: as ACE answers a failed walk-to-use, SendUseDoneEvent() with no error (Player_Use.cs:160-166 @95ef426d; run 5's log: "UseDone 0x00000000 with no 0196 ViewContents")
                }
                else
                {
                    OpenCorpse(open.Corpse);
                }
            }

            while (_pending.Count > 0 && _pending.Peek().Due <= now)
            {
                var cast = _pending.Dequeue();
                Answer(cast.Spell, cast.Target);
            }

            while (_stops.Count > 0 && _stops.Peek().Due <= now)
            {
                R.H.R.Reducer.OnVectorUpdate(R.H.R.Context, new VectorUpdateMsg(_stops.Dequeue().Guid, Vector3.Zero, Vector3.Zero, 1, 100));
            }

            while (_lateHits.Count > 0 && _lateHits.Peek().Due <= now)
            {
                var hit = _lateHits.Dequeue();
                Land(hit.Spell, _creatures[hit.Target], hit.Shot);
            }

            while (_late.Count > 0 && _late.Peek().Due <= now)
            {
                var gone = _late.Dequeue().Guid;
                R.Raw(AceWireWriters.UpdateMotion(gone, 1, 2, 2, false, new AceMovementData { Forward = 0x0011 })); // MotionCommand.Dead
                Assert.Equal((ushort?)0x0011, R.H.State.Objects.TryGet(gone, out var body) ? body.Motion?.Forward : null);
                if (Corpses && CorpseAtDeletion)
                {
                    LeaveCorpse(_creatures[gone]);
                }

                R.Raw(AceWireWriters.ObjectDelete(gone, 1));
            }
        }

        /// <summary>The creature's corpse appears where it lay (a corpse description flag, the creature's name); M5 7 P7b: also the server sending a corpse again once the player is back in view of it.</summary>
        public void LeaveCorpse(Creature creature)
        {
            var guid = creature.Guid + CorpseOffset;
            _corpses[guid] = creature;
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(creature.Cell, PlayerX, creature.Y, 30), name: "Corpse of " + creature.Name);
            R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { ObjectDescriptionFlags = WorldObjectTable.CorpseFlag } }, isUpdate: false);
        }

        /// <summary>The open of a corpse: ViewContents naming its items, their CreateObjects and the UseDone; or, when refused, a UseDone error alone.</summary>
        private void OpenCorpse(uint corpse)
        {
            if (RefuseOpen)
            {
                R.UseDone(0x0036);
                return;
            }

            var held = ItemsOfCreature.TryGetValue(_corpses[corpse].Guid, out var own) ? own : CorpseItems;
            R.Server((p, s) => AceM3Writers.ViewContents(p, s, corpse, [.. held.Select(i => (i.Guid, 0u))]));
            foreach (var i in held)
            {
                var item = LootRulesTests.Item(i.Type, i.Value, i.Burden, i.Guid, i.Name, i.Wcid);
                R.H.R.Reducer.OnCreateObject(R.H.R.Context, item.Created with { Weenie = item.Created.Weenie with { Header = item.Created.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = corpse } }, isUpdate: false);
            }

            R.UseDone();
        }

        /// <summary>The server answers the place tool: the reply, then the teleport and the position in <paramref name="cell"/>.</summary>
        private void Arrive(string request, uint cell)
        {
            var id = request[(request.LastIndexOf('#') + 1)..];
            var data = new PlaceData("loc", null, ToolsWire.FormatCell(cell), PlayerX, PlayerY, 0f, 1f, 0f, 0f, 0f);
            R.H.ServerMessage(ToolsWire.Reply(ToolReply.Success(id, ToolVerbs.PlaceLoc, data), data.Describe()));
            _teleports++;
            var h = R.H.R;
            h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(_teleports));
            h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(ActionHarness.Amy, 1, _teleports, _teleports, 1, cell: cell, x: PlayerX));
            R.Raw(AceWireWriters.SetState(WorldActionRig.Player, (uint)PhysicsState.Gravity, 1, (ushort)(40 + _teleports))); // a state without Hidden ends the arrival
        }

        public void Die() => R.Server((p, s) => AceM3Writers.VictimNotification(p, s, "You have been killed by Tusker Slave."));

        private static GearItem GearItemOf(uint wcid, string name, GearKind kind, GearWeenie w) =>
            new(wcid, name, kind, w.WeenieType, w.ItemType, w.ValidLocations, 1, null, w.Burden);

        /// <summary>M6 14 (<c>m6-life-through</c>): the range of the rig's life spells (Drain Health Other and Harm Other), in metres.</summary>
        public const float LifeRange = 20f;

        /// <summary>M6 14: the mage's health as the scripted ACE holds it (a drain's gain raises it); set by <see cref="SetHealth"/>, else read from the description at the first life cast.</summary>
        public uint? Health { get; private set; }

        /// <summary>M6 14: what the next Harm Other casts take, in order; with none queued a landing takes the level's mean, rounded down (ACE rolls it between the row's two ends).</summary>
        public Queue<int> HarmAmounts { get; } = new();

        /// <summary>M6 14: the health one Heal Self restores on the scripted ACE.</summary>
        public uint HealPerCast { get; set; } = 40;

        /// <summary>M6 14: when false the scripted ACE sends no UpdateHealth with a life spell's landing (a creature that is not her selected target): the chat line alone tells what it took.</summary>
        public bool PushHealth { get; set; } = true;

        /// <summary>M6 14: the server sets the mage's health.</summary>
        public void SetHealth(uint health)
        {
            Health = health;
            var key = MageTemplate.MaxHealth + 1; // the current's secondary attribute: 2 health
            _sequence[key] = (byte)(_sequence[key] + 1);
            R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(_sequence[key], key, health));
        }

        private static Dictionary<uint, SpellInfo> BuildTable(float range, bool openers, bool life = false)
        {
            var table = new Dictionary<uint, SpellInfo>();
            if (life)
            {
                // M6 14 (m6-life-through): Drain Health Other I to VII (a Transfer) and Harm Other I to VII (a Boost), neither a projectile; powers and costs as the SpellTable's (LifeThroughRuleTests and FightDrainTests pin the dats').
                var drains = ArenaProfile.ExtraNamed("Drain Health Other")!.Spells;
                for (var i = 0; i < DrainPicker.Ids.Count; i++)
                {
                    table[drains[i].Id] = WorldActionRig.Spell(drains[i].Id, SpellType.Transfer, SpellFlags.Resistable) with { Name = drains[i].Name, School = MagicSchool.LifeMagic, Power = drains[i].Power, BaseMana = i < 6 ? 10u * (uint)(i + 1) : 60u, BaseRangeConstant = LifeRange, BaseRangeMod = 0f };
                }

                string[] numerals = ["I", "II", "III", "IV", "V", "VI"];
                for (var i = 0; i < VitalsMeans.HarmOther.Count; i++)
                {
                    var id = VitalsMeans.HarmOther[i];
                    table[id] = WorldActionRig.Spell(id, SpellType.Boost, SpellFlags.Resistable) with { Name = i < 6 ? "Harm Other " + numerals[i] : "Heart Rend", School = MagicSchool.LifeMagic, Power = i == 0 ? 1u : 25u + (50u * (uint)i), BaseMana = 10u * (uint)(i + 1), BaseRangeConstant = LifeRange, BaseRangeMod = 0f };
                }

                for (var i = 0; i < VitalsMeans.HealSelf.Count; i++)
                {
                    var id = VitalsMeans.HealSelf[i];
                    table[id] = WorldActionRig.Spell(id, SpellType.Boost, SpellFlags.Beneficial | SpellFlags.SelfTargeted) with { Name = i < 6 ? "Heal Self " + numerals[i] : "Adja's Intervention", School = MagicSchool.LifeMagic, Power = i == 0 ? 1u : 50u * (uint)i, BaseMana = 10u * (uint)(i + 1) };
                }
            }

            if (openers)
            {
                // M5 7 F8: the opener line (Fire Vulnerability Other I-VIII, Life Magic enchantments) is in the table and the book only when a test asks for it, so the other tests' Slaves keep their bolts alone.
                foreach (var spell in ArenaProfile.ExtraNamed("Fire Vulnerability Other")!.Spells)
                {
                    table[spell.Id] = WorldActionRig.Spell(spell.Id, SpellType.Enchantment, SpellFlags.None) with { Name = spell.Name, School = MagicSchool.LifeMagic, Power = spell.Power, BaseMana = 40 };
                }
            }

            foreach (var line in ArenaProfile.Lines.Where(l => l.Role == ProfileRole.WarBolt))
            {
                foreach (var spell in line.Spells)
                {
                    table[spell.Id] = WorldActionRig.Spell(spell.Id, SpellType.Projectile, SpellFlags.Projectile) with
                    {
                        Name = spell.Name,
                        School = MagicSchool.WarMagic,
                        Power = spell.Power,
                        BaseMana = 20 + (uint)spell.Position,
                        BaseRangeConstant = range,
                        BaseRangeMod = 0f,
                    };
                }
            }

            return table;
        }

        private void Describe(uint warRanks, IReadOnlyCollection<uint> known, int characterLevel, uint? lifeRanks = null)
        {
            var attributes = ImmutableArray.CreateBuilder<AttributeDesc>();
            foreach (var id in new[] { MageTemplate.Strength, MageTemplate.Endurance, MageTemplate.Quickness, MageTemplate.Coordination, MageTemplate.Focus, MageTemplate.Self })
            {
                attributes.Add(new AttributeDesc(id, (uint)(MageTemplate.TargetOf(MageStatKind.Attribute, id)?.Ranks ?? 0), MageTemplate.CreationValue(id), 0));
            }

            var skills = ImmutableArray.CreateBuilder<SkillDesc>();
            foreach (var id in new[] { MageTemplate.CreatureEnchantment, MageTemplate.ItemEnchantment, MageTemplate.LifeMagic, MageTemplate.WarMagic, MageTemplate.ManaConversion })
            {
                var t = MageTemplate.TargetOf(MageStatKind.Skill, id)!;
                var ranks = id == MageTemplate.WarMagic ? warRanks : id == MageTemplate.LifeMagic && lifeRanks is { } given ? given : (uint)t.Ranks;
                skills.Add(new SkillDesc(id, (ushort)ranks, 1, t.Specialized ? 3u : 2u, 0, t.Specialized ? 10u : 0u, 0, 0));
            }

            var vitals = ImmutableArray.CreateBuilder<VitalDesc>();
            vitals.Add(new VitalDesc(MageTemplate.MaxHealth, 100, 0, 0, 200));
            vitals.Add(new VitalDesc(MageTemplate.MaxStamina, (uint)MageTemplate.TargetOf(MageStatKind.Vital, MageTemplate.MaxStamina)!.Ranks, 0, 0, 200));
            vitals.Add(new VitalDesc(MageTemplate.MaxMana, (uint)MageTemplate.TargetOf(MageStatKind.Vital, MageTemplate.MaxMana)!.Ranks, 0, 0, Mana));
            R.H.Describe(new PlayerDescriptionMsg
            {
                Attributes = attributes.ToImmutable(),
                Skills = skills.ToImmutable(),
                IntProperties = characterLevel > 0 ? ImmutableDictionary<uint, int>.Empty.Add(25, characterLevel) : ImmutableDictionary<uint, int>.Empty, // PropertyInt.Level (g6's plan cap); 0: unknown
                Vitals = vitals.ToImmutable(),
                Spells = known.ToImmutableDictionary(id => id, _ => 2f),
                Equipped = [new EquippedDesc(WieldedSetups.Wand.Guid, (uint)EquipMask.Held, 0)], // M4 3.1 g: the mage holds its wand, as the combat-mode guard needs for the magic mode
            });
            WieldedSetups.Create(R.H.R, WieldedSetups.Wand);
        }

        private void Regenerate(double now)
        {
            if (!double.IsNaN(_lastNow) && now > _lastNow)
            {
                _manaAcc += (now - _lastNow) * ManaRegenPerSecond;
                if (_manaAcc >= 1 && R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out _, out var max))
                {
                    var whole = (uint)_manaAcc;
                    _manaAcc -= whole;
                    if (Mana < max)
                    {
                        Mana = Math.Min(max, Mana + whole);
                        SendMana();
                    }
                }
            }

            _lastNow = now;
        }

        private void SendMana()
        {
            var key = MageTemplate.MaxMana + 1; // the current's secondary attribute: 6 mana (0x02E9)
            _sequence[key] = (byte)(_sequence[key] + 1);
            R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(_sequence[key], key, Mana));
        }

        private void Answer(uint spellId, uint target)
        {
            Answered.Add(spellId);
            if (RefusedBy.TryGetValue(spellId, out var refusal))
            {
                R.UseDone(refusal); // M6 7 K: ACE refuses this spell every time
                return;
            }

            if (target == 0)
            {
                // M6 14: Heal Self: ACE's line of a boost on oneself, with the vital in lower case (WorldObject_Magic.cs:526, :554 @95ef426d), and the UseDone.
                R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var heldNow, out var healthMax);
                var before = Health ?? heldNow;
                var restored = Math.Min(HealPerCast, healthMax > before ? healthMax - before : 0);
                Mana = Mana >= ManaPerCast ? Mana - ManaPerCast : 0;
                SendMana();
                SetHealth(before + restored);
                R.Raw(AceWireWriters.ServerMessage($"You cast {Table[spellId].Name} and restore {restored} points of your health.", 7));
                R.UseDone();
                return;
            }

            var shot = _script.Count > 0 ? _script.Dequeue() : Shot.Normal;
            switch (shot)
            {
                case Shot.OutOfRange:
                    R.UseDone(0x0550);
                    return;
                case Shot.MagicOutOfRange:
                    R.UseDone(0x0406);
                    return;
                case Shot.NotAcquired:
                    R.UseDone(0x042C);
                    return;
                case Shot.InTheAir:
                    R.UseDone(0x04EB);
                    return;
                case Shot.TargetDied:
                    _creatures[target].Dead = true;
                    R.Raw(AceWireWriters.ObjectDelete(target, 1));
                    R.UseDone(0x042C);
                    return;
                case Shot.TargetDiedAtRelease:
                    _creatures[target].Dead = true;
                    R.Raw(AceWireWriters.ObjectDelete(target, 1));
                    R.WeenieError(0x042C);
                    R.UseDone();
                    return;
                case Shot.Fizzle:
                    Mana = Mana >= 5 ? Mana - 5 : 0;
                    SendMana();
                    R.WeenieError(0x0402);
                    R.UseDone();
                    return;
                case Shot.NoProjectile:
                    R.UseDone();
                    return;
            }

            Mana = Mana >= ManaPerCast ? Mana - ManaPerCast : 0;
            SendMana();
            if (DrainPicker.RowOf(spellId) is not null || HarmOtherRows.RowOf(spellId) is not null)
            {
                AnswerLife(spellId, _creatures[target], shot); // M6 14: no projectile is made for a life spell
                return;
            }

            var boltId = Projectile();
            var creature = _creatures[target];
            if (ArenaProfile.ExtraNamed("Fire Vulnerability Other")!.Spells.Any(o => o.Id == spellId))
            {
                // The opener is no projectile: a resist is ACE's line alone, a landing is "You cast <spell> on <name>" (WorldObject_Magic.cs 192 and 409-421), and the UseDone is code 0 either way.
                R.Raw(AceWireWriters.ServerMessage(shot == Shot.Resist ? $"{creature.Name} resists your spell" : $"You cast {Table[spellId].Name} on {creature.Name}", 7));
                R.UseDone();
                return;
            }

            if (shot is Shot.Resist or Shot.RanIn)
            {
                // The bolt flew 0.2 s at 20 m/s (4 m) and met the target, which had run to 6 m from the bolt's origin.
                var at = ReducerHarness.Position(R.H.R.State.Objects.TryGet(target, out var t) && t.Position is { } p ? p.CellId : Den, PlayerX, PlayerY + 6, 30);
                R.H.R.Reducer.OnUpdatePosition(R.H.R.Context, new UpdatePositionMsg(target, PositionFlags.Grounded, at, null, null, 1, 50, 1, 1));
                _stops.Enqueue((boltId, Now + 0.2));
                if (shot == Shot.Resist)
                {
                    R.Raw(AceWireWriters.ServerMessage($"{creature.Name} resists your spell", 7));
                }

                R.UseDone();
                return;
            }

            if (shot == Shot.Wall)
            {
                _stops.Enqueue((boltId, Now + 0.2));
                R.UseDone();
                return;
            }

            if (shot == Shot.Dud)
            {
                R.UseDone();
                return;
            }

            if (HitDelay > 0)
            {
                if (StaleDamageLineOnce)
                {
                    // M5 7 D124 (S1b): an earlier projectile's damage line arrives while this bolt flies; round 12's hit wait took such a line for its own and picked again at once.
                    StaleDamageLineOnce = false;
                    R.Raw(AceWireWriters.ServerMessage("You burn Drudge Ravener for 10 points with Flame Bolt VI.", 7));
                }

                _lateHits.Enqueue((spellId, target, shot, Now + HitDelay)); // the bolt is in flight: the UseDone ends the cast now, the hit lands later
            }
            else
            {
                Land(spellId, creature, shot);
            }

            R.UseDone();
        }

        /// <summary>
        /// M6 14 (<c>m6-life-through</c>): ACE's answer to a Drain Health Other or a Harm Other, neither of which has a projectile or asks a line (<c>Player_Magic.cs:481-506</c>, <c>:1098-1135</c> @95ef426d). A resist is the
        /// line alone. A drain takes ACE's share of the creature's current health, scaled to what the mage is missing (<see cref="BuffStatMods.Transfer"/>), and writes "You gain N points of health due to casting ..."
        /// (<c>WorldObject_Magic.cs:853</c>); Harm Other takes its amount, cut to the health left, and writes "With &lt;spell&gt; you drain N points of health from &lt;name&gt;." (<c>:548</c>), the killing one too, and the
        /// kill follows (<c>:600</c>). ACE pushes the selected target's new health before either line (<see cref="PushHealth"/>). The UseDone is code 0 every time.
        /// </summary>
        private void AnswerLife(uint spellId, Creature creature, Shot shot)
        {
            var name = Table[spellId].Name;
            if (shot == Shot.Resist)
            {
                R.Raw(AceWireWriters.ServerMessage($"{creature.Name} resists your spell", 7));
                R.UseDone();
                return;
            }

            R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var held, out var healthMax);
            var mine = Health ?? held;
            if (DrainPicker.RowOf(spellId) is { } drain)
            {
                BuffStatMods.Transfer(in drain, (uint)Math.Max(0, creature.Health), healthMax > mine ? healthMax - mine : 0, out var taken, out var given);
                creature.Health -= (int)taken;
                PushHealthOf(creature);
                SetHealth(mine + given);
                R.Raw(AceWireWriters.ServerMessage($"You gain {given} points of health due to casting {name} on {creature.Name}", 7));
                R.UseDone();
                return;
            }

            var took = Math.Min(HarmAmounts.Count > 0 ? HarmAmounts.Dequeue() : (int)HarmOtherRows.RowOf(spellId)!.Value.MeanHarm, Math.Max(0, creature.Health));
            creature.Health -= took;
            PushHealthOf(creature);
            R.Raw(AceWireWriters.ServerMessage($"With {name} you drain {took} points of health from {creature.Name}.", 7));
            if (creature.Health <= 0)
            {
                creature.Hits = creature.HitsToKill - 1;
                Land(spellId, creature, Shot.Normal); // the kill: the notice and the deletion, as a bolt's
            }

            R.UseDone();
        }

        private void PushHealthOf(Creature creature)
        {
            if (PushHealth && creature.MaxHealth > 0)
            {
                var (guid, fraction) = (creature.Guid, Math.Max(0f, (float)creature.Health / creature.MaxHealth));
                R.Server((p, s) => AceWireWriters.UpdateHealth(p, s, guid, fraction));
            }
        }

        /// <summary>The bolt lands: the creature's hit, and the kill (0x01AD, the deletion) or the damage line.</summary>
        private void Land(uint spellId, Creature creature, Shot shot)
        {
            var target = creature.Guid;
            creature.Hits++;
            if (shot == Shot.SilentKill || creature.Hits >= creature.HitsToKill)
            {
                creature.Dead = true;
                if (Corpses && !(CorpseAtDeletion && DeathDelay > 0))
                {
                    LeaveCorpse(creature);
                }

                if (shot != Shot.SilentKill)
                {
                    R.Server((p, s) => AceM3Writers.KillerNotification(p, s, KillText ?? "You have killed " + creature.Name + "!"));
                    if (HealthZeroAtKill)
                    {
                        R.Server((p, s) => AceWireWriters.UpdateHealth(p, s, target, 0f));
                    }
                }

                if (Splash.Remove(target, out var neighbour) && !neighbour.Dead)
                {
                    // M5 7 P7b (D139 i): the ring's splash: the neighbour's corpse, its own 0x01AD (ACE sends it to the killer alone, Creature_Death.cs:58-77) and its deletion.
                    neighbour.Dead = true;
                    if (Corpses)
                    {
                        LeaveCorpse(neighbour);
                    }

                    R.Server((p, s) => AceM3Writers.KillerNotification(p, s, "You bring " + neighbour.Name + " to a fiery end!"));
                    R.Raw(AceWireWriters.ObjectDelete(neighbour.Guid, 1));
                }

                if (DeathDelay > 0 && shot != Shot.SilentKill)
                {
                    _late.Enqueue((target, Now + DeathDelay));
                }
                else
                {
                    R.Raw(AceWireWriters.ObjectDelete(target, 1));
                }
            }
            else
            {
                R.Raw(AceWireWriters.ServerMessage($"You scorch {creature.Name} for 40 points with {Table[spellId].Name}.", 7));
            }
        }

        private uint Projectile()
        {
            var id = _nextProjectile++;
            R.H.R.Reducer.OnCreateObject(
                R.H.R.Context,
                ReducerHarness.Create(id, ReducerHarness.Stamps(), ReducerHarness.Position(R.H.R.State.Self.Position?.CellId ?? Den, PlayerX, PlayerY, 30), state: PhysicsState.Missile | PhysicsState.Gravity, velocity: new Vector3(0, 20, 0)),
                isUpdate: false);
            return id;
        }
    }

    private static int FlameBoltIndexFor(Rig r)
    {
        var self = r.R.H.State.Self;
        return FightScenario.ChooseLevel(self.Qualities, self.Spellbook, r.Buff.CastChance, out _, out _);
    }

    [Fact]
    public void A_tusker_in_range_dies_after_two_hits_and_the_scenario_ends_with_one_kill()
    {
        var r = new Rig();
        var tusker = r.Add(0x80001001, TuskerSlave, 0x01050303, 40); // 20 m from the player, in range 30
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Null(scenario.Failure);
        Assert.Equal("done", scenario.Stage);
        Assert.True(tusker.Dead);
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(1, scenario.Tally.TargetKills); // the notice names "Tusker Slave"
        Assert.Equal(2, scenario.Tally.Casts);
        Assert.Equal(1, scenario.Tally.DamageLinesDealt); // the killing blow has no damage line (SpellProjectile.cs 857-861)
        Assert.Equal(40, scenario.Tally.DamageDealt);
        Assert.Equal(0, scenario.Tally.Misses);
        Assert.Equal(0, scenario.Walks);
        Assert.Equal(2, scenario.Projectiles);
        Assert.Equal([0x80001001u, 0x80001001u], r.CastTargets);
        Assert.Equal([0x80001001u, 0x80001001u], r.HealthQueries); // M5 7 F20: one when the target is picked (selected, so ACE pushes its health), one after the hit that did not kill
        Assert.Equal([CombatModes.Magic, CombatModes.NonCombat], r.ModesSent);
        Assert.Equal(CombatModes.NonCombat, r.R.H.State.Self.Qualities.CombatMode);

        var index = FlameBoltIndexFor(r);
        Assert.True(index >= 0);
        Assert.Equal(ArenaProfile.LineNamed("Flame Bolt")!.Spells[index].Id, scenario.SpellId);
        Assert.All(r.CastSpells, id => Assert.Equal(scenario.SpellId, id));
        Assert.Contains("kills=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains("spells_cast=2 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(30f, scenario.RangeMetres);
    }

    [Fact]
    public void An_arena_here_run_skips_the_place_and_buff_stages_and_the_floor_rule_and_takes_its_level_from_the_arena_skill()
    {
        // M3 3.1 fd: the offline stand-in's arena. A tusker in the chest corner (off the floor, excluded from the den's rule) is a target; the stages the host did not ask for do not run even with FightStages.All.
        var options = new FightOptions { ArenaHere = true, ArenaSkill = 250 };
        var r = new Rig(warRanks: 0, fight: options);
        var tusker = r.Add(0x80001001, TuskerSlave, DeadChestCorner, 40);
        var scenario = r.Scenario(null, FightStages.All);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Null(scenario.Failure);
        Assert.True(tusker.Dead);
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(0, scenario.Walks);
        var stages = r.Log.Lines.Where(l => l.Contains("stage ", StringComparison.Ordinal) && l.Contains("[fight]", StringComparison.Ordinal)).Select(l => l[(l.IndexOf("stage ", StringComparison.Ordinal) + 6)..].Trim()).ToArray();
        Assert.Equal(["buffs", "place", "mode-magic", "fight", "mode-peace"], stages); // no leave-den: the host put the player here and keeps it here
        Assert.Empty(r.Places);
        Assert.Null(scenario.Gear);
        var index = FightScenario.ChooseLevel(options.ArenaSkill, null, r.Buff.CastChance, out _); // the arena-here run reads no spellbook
        Assert.True(index >= 0);
        Assert.Equal(index, FightScenario.ChooseLevel(options.ArenaSkill, r.R.H.State.Self.Spellbook, r.Buff.CastChance, out _)); // the rig knows every level
        Assert.Equal(ArenaProfile.LineNamed("Flame Bolt")!.Spells[index].Id, scenario.SpellId);
        Assert.Equal(30f, scenario.RangeMetres);
    }

    [Fact]
    public void The_level_from_a_known_skill_needs_the_spellbook_and_a_castable_chance()
    {
        var r = new Rig();
        var book = r.R.H.State.Self.Spellbook;

        Assert.Equal(-1, FightScenario.ChooseLevel(0, book, r.Buff.CastChance, out var why0));
        Assert.Contains("chance", why0, StringComparison.Ordinal);
        var low = FightScenario.ChooseLevel(100, book, r.Buff.CastChance, out _);
        var high = FightScenario.ChooseLevel(500, book, r.Buff.CastChance, out _);
        Assert.True(high >= low);
        Assert.Equal(FightScenario.Levels - 1, high);
    }

    [Fact]
    public void Each_stage_is_logged_once_in_order()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var scenario = r.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        var stages = r.Log.Lines.Where(l => l.Contains("stage ", StringComparison.Ordinal) && l.Contains("[fight]", StringComparison.Ordinal)).Select(l => l[(l.IndexOf("stage ", StringComparison.Ordinal) + 6)..].Trim()).ToArray();
        Assert.Equal(["buffs", "place", "mode-magic", "fight", "mode-peace", "leave-den"], stages);
    }

    [Fact]
    public void A_finished_fight_leaves_the_den_before_it_ends()
    {
        // The next run (login-death after login-bsd in the smoke, GearLiveTests after FightLiveTests in the loopback order) must not log BSD Mage in among the tuskers: after mode-peace the run places the player at Holtburg,
        // settles, and only then ends (done here; the log off in the headless run).
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Null(scenario.Failure);
        Assert.Equal("done", scenario.Stage);
        var place = Assert.Single(r.Places);
        Assert.Contains("poi", place, StringComparison.Ordinal);
        Assert.Contains("Holtburg", place, StringComparison.Ordinal);
        Assert.NotEqual(0x0105u, r.R.H.State.Self.Position!.Value.CellId >> 16);
        Assert.Contains(r.Log.Lines, l => l.Contains("leaving the den before the log off", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("left the den: the player is present in cell 0xA9B40019", StringComparison.Ordinal));
        Assert.Equal([CombatModes.Magic, CombatModes.NonCombat], r.ModesSent);
        var kinds = r.R.H.Net.Kinds();
        Assert.True(kinds.LastIndexOf(FakeSendKind.ChangeCombatMode) < kinds.LastIndexOf(FakeSendKind.Talk), "the leave goes out after the return to non-combat");
    }

    [Fact]
    public void A_failed_fight_leaves_the_den_and_only_then_ends_failed()
    {
        // endSession: false is the game's leg (FightLegRunner), which logs off as soon as the scenario returns Failed; a Failed returned in the den left BSD Mage on the arrival floor for the next login. The failure is set at
        // once; the run stays Running until the leave is over, and sends no log off of its own.
        var r = new Rig(fight: new FightOptions { Seconds = 20 });
        r.Add(0x80001001, 23, 0x01050303, 25, "Virindi"); // nothing to fight: the seconds limit fails the run in the den
        var scenario = r.Scenario(new FakeWalk());

        var status = ScenarioStatus.Running;
        var failedAt = double.NaN;
        for (var i = 0; i < 2000 && status == ScenarioStatus.Running; i++)
        {
            status = r.Frame(scenario);
            if (double.IsNaN(failedAt) && scenario.Failure is not null)
            {
                failedAt = r.Now;
                Assert.Equal(ScenarioStatus.Running, status); // not Failed in the den: the leave runs first
            }
        }

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("20 s passed with kills=0 of 1", scenario.Failure, StringComparison.Ordinal);
        Assert.InRange(r.Now - failedAt, GroundExit.SettleSeconds, GroundExit.SettleSeconds + 1); // the rig lands the player at once; then the settle
        var place = Assert.Single(r.Places);
        Assert.Contains("poi", place, StringComparison.Ordinal);
        Assert.NotEqual(0x0105u, r.R.H.State.Self.Position!.Value.CellId >> 16);
        Assert.Empty(r.R.H.Net.Of(FakeSendKind.LogOff)); // the host logs off, not the scenario
    }

    [Fact]
    public void A_cast_with_no_projectile_is_a_miss_and_three_in_a_row_across_two_targets_move_to_the_next_waypoint()
    {
        // M6 8 L: two on one target now drop it (FightScenarioTests.WallStay.cs), so the three in a row need a second target; the walk rule itself is unchanged.
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Add(0x80001002, TuskerSlave, 0x01050303, 42);
        r.Script(Shot.NoProjectile, Shot.NoProjectile, Shot.NoProjectile); // two on the first drop it, the third is on the second
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(3, scenario.Tally.Misses);
        Assert.Equal(1, walk.Legs);
        Assert.True(scenario.Tally.Kills >= 1);
        var lines = r.Log.Lines.Where(l => l.Contains("[fight]", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, lines.Count(l => l.Contains("miss: the cast was not confirmed", StringComparison.Ordinal)));
        Assert.Contains(lines, l => l.Contains("walking: 3 misses in a row; fake leg 1", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("the leg is done", StringComparison.Ordinal));
    }

    [Fact]
    public void A_blocked_target_is_not_cast_at_the_loop_walks_at_once_with_no_miss()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var walk = new FakeWalk { BlockedCell = 0x01050303 };
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal((1, 0, 1), (scenario.Tally.BlockedPicks, scenario.Tally.Misses, walk.Legs));
        Assert.Equal(2, scenario.Tally.Casts); // the two hits of the kill, after the leg: none before it
        Assert.Contains(r.Log.Lines, l => l.Contains("walking: no creature of the ground has a clear line", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("blocked: no clear line to 0x80001001", StringComparison.Ordinal));
    }

    [Fact]
    public void The_next_nearest_tusker_with_a_clear_line_is_cast_at_and_the_blocked_nearer_one_is_skipped()
    {
        var r = new Rig();
        var near = r.Add(0x80001001, TuskerSlave, 0x01050303, 30);
        var far = r.Add(0x80001002, TuskerSlave, 0x01050310, 40);
        var walk = new FakeWalk { BlockedCell = 0x01050303 };
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(0, walk.Legs);
        Assert.All(r.CastTargets, t => Assert.Equal(far.Guid, t));
        Assert.False(near.Dead);
        Assert.True(scenario.Tally.BlockedPicks >= 1);
    }

    [Fact]
    public void Fight_ignore_line_restores_the_nearest_pick()
    {
        var r = new Rig(fight: new FightOptions { IgnoreLine = true });
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var walk = new FakeWalk { BlockedCell = 0x01050303 };
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal((0, 0, 0), (scenario.Tally.BlockedPicks, walk.LineQueries, walk.Legs));
    }

    [Fact]
    public void A_bolt_that_stops_short_of_the_target_with_no_damage_line_is_a_wall_hit_and_walks_on_the_first_one()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Script(Shot.Wall);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal((1, 1, 1), (scenario.Tally.WallHits, scenario.Tally.Misses, walk.Legs));
        Assert.Contains(r.Log.Lines, l => l.Contains("walking: a bolt hit a wall", StringComparison.Ordinal));
    }

    [Fact]
    public void A_resisted_bolt_at_a_target_that_ran_at_the_mage_is_a_miss_not_a_wall_hit() => BoltBesideTarget(Shot.Resist, 1);

    [Fact]
    public void A_bolt_that_stops_beside_a_target_that_ran_at_the_mage_with_no_resist_line_is_a_miss_not_a_wall_hit() => BoltBesideTarget(Shot.RanIn, 0);

    private static void BoltBesideTarget(Shot shot, int resists)
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Script(shot, Shot.Normal, Shot.Normal);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal((0, 1, 0, resists), (scenario.Tally.WallHits, scenario.Tally.Misses, walk.Legs, scenario.Tally.Resists));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("a bolt hit a wall", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(20.0, 20.0, 0.2, true)] // the first-cast wall: 16 m short
    [InlineData(6.0, 20.0, 0.2, false)] // the target ran in: 2 m short, inside the radius and the margin
    [InlineData(20.0, 20.0, 1.0, false)] // flew all the way
    [InlineData(20.0, 20.0, 0.75, true)] // 5 m short: a wall, however the share is read
    public void The_wall_rule_is_the_stop_distance_from_the_target(double toTarget, double speed, double seconds, bool wall) =>
        Assert.Equal(wall, FightScenario.StoppedShortOfTarget(toTarget, speed, seconds));

    [Fact]
    public void The_fight_ignore_line_option_parses()
    {
        Assert.False(new FightOptions().IgnoreLine);
        Assert.True(FightOptions.FromArguments([new CommandLineArgument("--fight-ignore-line", "--fight-ignore-line", string.Empty, false)]).IgnoreLine);
    }

    [Fact]
    public void One_miss_and_hits_do_not_walk()
    {
        // M6 8 L: was two misses and a hit; two with no bolt on one target now drop it.
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Script(Shot.NoProjectile);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal((1, 0, 0), (scenario.Tally.Misses, walk.Legs, scenario.Walks));
    }

    [Fact]
    public void A_confirmed_projectile_with_no_damage_line_in_its_flight_and_the_margin_is_a_miss()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Script(Shot.Dud);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(1, scenario.Tally.Misses);
        Assert.Equal(3, scenario.Tally.Casts);
        Assert.Contains(r.Log.Lines, l => l.Contains("no damage line or kill came within 1.7 s", StringComparison.Ordinal));
    }

    [Fact]
    public void A_target_sixty_metres_away_whose_damage_line_arrives_after_three_seconds_is_a_hit_of_the_first_cast_not_a_miss_and_a_hit_of_the_second()
    {
        // The rig's bolt flies at 20 m/s (its CreateObject velocity): 60 m is 3 s of flight, so the hit wait is 3.7 s and the line at 3.5 s is cast 1's (package 2, finding 6: at the old fixed 3 s it was a miss, then cast 2's hit).
        var r = new Rig(range: 75f) { HitDelay = 3.5 };
        r.Add(0x80001001, TuskerSlave, 0x01050303, PlayerY + 60);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(0, scenario.Tally.Misses);
        Assert.Equal(2, scenario.Tally.Casts);
        Assert.Equal(1, scenario.Tally.DamageLinesDealt);
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(2, r.Log.Lines.Count(l => l.Contains("the target 0x80001001 60.0 m from it: hit wait 3.7 s", StringComparison.Ordinal)));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("no damage line or kill came within", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(20.0, 20f, 1.7)] // a close target
    [InlineData(2.4, 15f, 0.86)] // r3c's 2.4 m: 0.86 s, it was 3
    [InlineData(75.0, 15f, 5.7)] // the cast range at a live Flame Bolt's speed
    [InlineData(75.0, 0.5f, 10.0)] // not a speed to divide by: the cap
    [InlineData(1000.0, 15f, 10.0)] // past the cap
    public void The_hit_wait_is_the_flight_time_plus_the_measured_margin_at_most_ten_M6_7_AA(double distance, float speed, double wait)
    {
        Assert.Equal(wait, FightScenario.HitWaitFor(distance, speed), 6);
        Assert.Equal((3.0, 0.7, 10.0, 1f), (FightScenario.HitWaitSeconds, Math.Round(FightScenario.HitWaitMarginSeconds, 6), FightScenario.HitWaitMaxSeconds, FightScenario.MinProjectileSpeed));
        Assert.True(FightScenario.HitWaitMarginSeconds > FightScenario.WallGraceSeconds, "a bolt stopped at the end of its flight still gets its wall verdict");
    }

    [Fact]
    public void An_out_of_range_answer_walks_at_once()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Script(Shot.OutOfRange);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(1, scenario.Tally.Misses);
        Assert.Equal(1, walk.Legs);
        Assert.Contains(r.Log.Lines, l => l.Contains("MissileOutOfRange (0x0550)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_target_that_died_during_the_wind_up_is_not_a_miss_and_the_next_tusker_is_picked_at_once()
    {
        var r = new Rig();
        var dying = r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var next = r.Add(0x80001002, TuskerSlave, 0x01050303, 48);
        r.Script(Shot.TargetDied);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(dying.Dead);
        Assert.True(next.Dead);
        Assert.Equal(1, scenario.Tally.TargetDiedMidCast); // M5 7 F28
        Assert.Equal(0, scenario.Tally.Misses);
        Assert.Equal(0, walk.Legs);
        Assert.Equal(0, scenario.Walks);
        Assert.Equal([0x80001001u, 0x80001002u, 0x80001002u], r.CastTargets); // no retry at the dead guid
        Assert.Contains("target_died_mid_cast=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(r.Log.Lines, l => l.Contains("died during the wind-up", StringComparison.Ordinal));
    }

    [Fact]
    public void A_0x042C_for_a_target_still_shown_here_is_a_target_died_not_a_miss_and_not_out_of_range()
    {
        // M5 7 D124 (was F28's "a miss as before"): ACE sends 0x042C only for a target gone from its landblock (Player_Magic.cs:137-140, 173-179, 197-203, 751-757 @95ef426d), so the server's word stands over the table's lag.
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Add(0x80001002, TuskerSlave, 0x01050303, 48);
        r.Script(Shot.NotAcquired);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(1, scenario.Tally.TargetDiedMidCast);
        Assert.Equal(0, scenario.Tally.Misses);
        Assert.Equal(0, walk.Legs);
        Assert.Equal([0x80001001u, 0x80001002u, 0x80001002u], r.CastTargets); // the next tusker at once, no walk, no recast at the unacquired guid
        Assert.Contains("target_died_mid_cast=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(r.Log.Lines, l => l.Contains("not acquired at the release", StringComparison.Ordinal));
    }

    [Fact]
    public void The_releases_0x042C_WeenieError_then_UseDone_0_is_a_target_died_not_a_cast_without_a_projectile()
    {
        // M5 7 D124: round 12's 31 casts: the bolt's kill landed during the next wind-up, ACE's DoCastSpell sent WeenieError 0x042C (Player_Magic.cs:751-757 @95ef426d) and FinishCast's UseDone 0 (935-985).
        var r = new Rig();
        var dying = r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var next = r.Add(0x80001002, TuskerSlave, 0x01050303, 48);
        r.Script(Shot.TargetDiedAtRelease);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(dying.Dead);
        Assert.True(next.Dead);
        Assert.Equal(1, scenario.Tally.TargetDiedMidCast);
        Assert.Equal(0, scenario.Tally.Misses);
        Assert.Equal(0, walk.Legs);
        Assert.Equal([0x80001001u, 0x80001002u, 0x80001002u], r.CastTargets);
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("without a projectile", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("died during the wind-up", StringComparison.Ordinal));
    }

    [Fact]
    public void No_target_in_range_walks_and_a_creature_beyond_the_range_is_not_cast_at()
    {
        var r = new Rig();
        var far = r.Add(0x80001001, TuskerSlave, 0x01050303, 80); // 60 m, range 30
        var walk = new FakeWalk { TicksPerLeg = 2 };
        var scenario = r.Scenario(walk);

        for (var i = 0; i < 40 && walk.Legs == 0; i++)
        {
            Assert.Equal(ScenarioStatus.Running, r.Frame(scenario));
        }

        Assert.Equal(1, walk.Legs);
        Assert.Empty(r.CastTargets);
        Assert.False(far.Dead);

        // After the leg the tusker has come within range (the test moves it): the loop casts again.
        r.R.H.R.Reducer.OnUpdatePosition(r.R.H.R.Context, ReducerHarness.Move(far.Guid, 1, 2, 1, 1, cell: 0x01050303, x: PlayerX));
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));
        Assert.Equal(1, scenario.Tally.Kills);
    }

    [Fact]
    public void A_virindi_a_drudge_and_a_tusker_in_the_chest_corner_are_never_targeted()
    {
        var r = new Rig();
        r.Add(0x80001001, 23, 0x01050303, 25, "Virindi"); // 5 m, on the floor but not a tusker
        r.Add(0x80001002, 1610, 0x01050303, 26, "Drudge Ravener");
        r.Add(0x80001003, 237, 0x01050303, 27, "Virindi Master");
        r.Add(0x80001004, TuskerSlave, DeadChestCorner, 28); // a tusker off the floor (the chest corner is excluded)
        r.Add(0x80001005, 1629, 0x0105010D, 29, "Tusker Guard"); // the north wing's cell (no-go in the profile)
        var good = r.Add(0x80001006, 1629, 0x010502E1, 45, "Tusker Guard"); // 25 m, the only legal target
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.NotEmpty(r.CastTargets);
        Assert.All(r.CastTargets, t => Assert.Equal(good.Guid, t));
        Assert.True(good.Dead);
        Assert.Equal(1, scenario.Tally.Kills);
    }

    [Fact]
    public void The_seconds_limit_ends_the_fight_loudly_with_the_counts()
    {
        var r = new Rig(fight: new FightOptions { Seconds = 20 });
        r.Add(0x80001001, 23, 0x01050303, 25, "Virindi"); // nothing to fight: the loop keeps walking
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Failed, r.Run(scenario, 120));

        Assert.NotNull(scenario.Failure);
        Assert.Contains("20 s passed with kills=0 of 1", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("walks=", scenario.Failure, StringComparison.Ordinal);
        Assert.True(walk.Legs >= 2, "the loop walked leg after leg: " + walk.Legs);
        Assert.Empty(r.CastTargets);
        Assert.Contains(r.Log.Lines, l => l.Contains("ERROR", StringComparison.Ordinal) && l.Contains("20 s passed", StringComparison.Ordinal));
    }

    [Fact]
    public void The_players_death_ends_the_fight_loudly()
    {
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40, hitsToKill: 5);
        var scenario = r.Scenario(new FakeWalk());
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 80 && scenario.Tally.Casts == 0; i++)
        {
            status = r.Frame(scenario);
        }

        Assert.Equal(ScenarioStatus.Running, status);
        r.Die();
        status = r.Run(scenario, 10);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("the player died", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains(r.Log.Lines, l => l.Contains("ERROR", StringComparison.Ordinal) && l.Contains("the player died", StringComparison.Ordinal));
    }

    [Fact]
    public void A_fizzle_is_retried_at_the_same_level_twice_then_drops_one_level_once()
    {
        // M5 1 K3 (D14): was "a fizzle drops one level once". Now the first try and two more are at the chosen level, the fourth is one level down, and a fizzle there is a miss with no further drop.
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40, hitsToKill: 3);
        r.Script(Shot.Fizzle, Shot.Fizzle, Shot.Fizzle, Shot.Fizzle);
        var scenario = r.Scenario(new FakeWalk());
        var first = FlameBoltIndexFor(r);
        var spells = ArenaProfile.LineNamed("Flame Bolt")!.Spells;

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal([spells[first].Id, spells[first].Id, spells[first].Id], r.CastSpells.Take(3));
        Assert.Equal(spells[first - 1].Id, r.CastSpells[3]); // one level down after three fizzles at the level
        Assert.All(r.CastSpells.Skip(3), id => Assert.Equal(spells[first - 1].Id, id)); // and no further drop for the fourth fizzle
        Assert.Equal(4, scenario.Tally.Misses);
        Assert.Equal(1, r.Log.Lines.Count(l => l.Contains("one level down", StringComparison.Ordinal)));
    }

    [Fact]
    public void Two_fizzles_are_retried_at_the_same_level_and_the_third_try_lands_with_no_drop()
    {
        // M5 1 K3 (D14): the sequence's first half, with the casts counted (a try sent is a cast) and each fizzle a miss.
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40, hitsToKill: 3);
        r.Script(Shot.Fizzle, Shot.Fizzle);
        var scenario = r.Scenario(new FakeWalk());
        var first = FlameBoltIndexFor(r);
        var spells = ArenaProfile.LineNamed("Flame Bolt")!.Spells;

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.All(r.CastSpells, id => Assert.Equal(spells[first].Id, id));
        Assert.Equal(2, scenario.Tally.Misses);
        Assert.Equal(r.CastSpells.Count, scenario.Tally.Casts);
        Assert.Equal(0, r.Log.Lines.Count(l => l.Contains("one level down", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_fizzle_drops_to_the_next_lower_level_the_spellbook_knows_and_the_range_follows_it()
    {
        // A book that knows the chosen level and the one two below it, not the one between (package 2, finding 16): the drop skips the unknown level and takes the lower level's own range.
        var all = new Rig();
        var first = FlameBoltIndexFor(all);
        Assert.True(first >= 2, "the default rig must choose level III or higher for this test");
        var spells = ArenaProfile.LineNamed("Flame Bolt")!.Spells;
        var r = new Rig(known: [.. all.Table.Keys.Where(id => id != spells[first - 1].Id)]);
        r.Table[spells[first - 2].Id] = r.Table[spells[first - 2].Id] with { BaseRangeConstant = 20f }; // the lower level reaches less far
        r.Add(0x80001001, TuskerSlave, 0x01050303, PlayerY + 15, hitsToKill: 2);
        r.Script(Shot.Fizzle, Shot.Fizzle, Shot.Fizzle); // D14: three tries at the chosen level before the drop
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal([spells[first].Id, spells[first].Id, spells[first].Id], r.CastSpells.Take(3));
        Assert.Equal(spells[first - 2].Id, r.CastSpells[3]); // not the unknown level between
        Assert.All(r.CastSpells.Skip(3), id => Assert.Equal(spells[first - 2].Id, id));
        Assert.Equal(20f, scenario.RangeMetres);
        Assert.Contains(r.Log.Lines, l => l.Contains("one level down, to " + spells[first - 2].Name + " (" + spells[first - 2].Id.ToString(CultureInfo.InvariantCulture) + "), the next lower level the spellbook knows; range 20 m", StringComparison.Ordinal));
        Assert.Contains(" level=" + new[] { "I", "II", "III", "IV", "V", "VI", "VII" }[first - 2] + " range_m=20 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_level_is_the_highest_known_one_whose_chance_reaches_the_threshold()
    {
        // The highest level a skill reaches at 0.9 by the buff cycle's code, then only the levels the spellbook holds.
        var all = new Rig();
        var allIndex = FlameBoltIndexFor(all);
        var spells = ArenaProfile.LineNamed("Flame Bolt")!.Spells;
        Assert.InRange(allIndex, 0, FightScenario.Levels - 1);
        Assert.True(GearCasts.TryCurrentSkill(all.R.H.State.Self.Qualities, MageTemplate.WarMagic, out var skill, out _));
        Assert.True(GearCasts.Chance(skill, spells[allIndex].Power) >= 0.9);
        if (allIndex < FightScenario.Levels - 1)
        {
            Assert.True(GearCasts.Chance(skill, spells[allIndex + 1].Power) < 0.9);
        }

        var onlyLow = new Rig(known: [spells[0].Id, spells[1].Id]);
        Assert.Equal(1, FlameBoltIndexFor(onlyLow));

        var none = new Rig(known: []);
        Assert.Equal(-1, FlameBoltIndexFor(none));
        none.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var scenario = none.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Failed, none.Run(scenario));
        Assert.Contains("no Flame Bolt level to cast", scenario.Failure, StringComparison.Ordinal);
        Assert.Empty(none.CastTargets);
    }

    [Fact]
    public void The_range_is_aces_with_the_skills_init_plus_ranks_and_capped()
    {
        var info = WorldActionRig.Spell(1, SpellType.Projectile, SpellFlags.Projectile) with { BaseRangeConstant = 5f, BaseRangeMod = 0.02f };
        Assert.Equal(13f, FightScenario.CastRange(info, 400), 3);
        var wide = info with { BaseRangeConstant = 50f, BaseRangeMod = 0.1f };
        Assert.Equal(FightScenario.MaxRange, FightScenario.CastRange(wide, 400));
        Assert.Equal(75f, FightScenario.MaxRange);
    }

    [Fact]
    public void Two_kills_are_wanted_and_two_tuskers_are_cast_at_nearest_first()
    {
        var r = new Rig(fight: new FightOptions { Kills = 2 });
        var near = r.Add(0x80001001, TuskerSlave, 0x01050303, 35);
        var far = r.Add(0x80001002, TuskerSlave, 0x01050303, 45);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(near.Dead && far.Dead);
        Assert.Equal(2, scenario.Tally.Kills);
        Assert.Equal([near.Guid, near.Guid, far.Guid, far.Guid], r.CastTargets);
    }

    [Fact]
    public void A_first_hit_that_kills_leaves_damage_dealt_at_zero_so_the_fight_goes_on_to_the_next_tusker_for_a_damage_line()
    {
        // 3.1-R1: ACE writes no damage line for the killing blow, the judge wants damage_dealt > 0, so one kill is not the end while no line has been seen.
        var r = new Rig(fight: new FightOptions { Kills = 1 });
        var first = r.Add(0x80001001, TuskerSlave, 0x01050303, 35, hitsToKill: 1);
        var second = r.Add(0x80001002, TuskerSlave, 0x01050303, 45, hitsToKill: 2);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(first.Dead);
        Assert.Equal(1, scenario.Tally.Kills); // the wanted kill was made by the first cast, with no damage line: the loop went on, to the second tusker's first hit, and stopped there
        Assert.True(scenario.Tally.DamageDealt > 0);
        Assert.Equal(40, scenario.Tally.DamageDealt);
        Assert.Equal([first.Guid, second.Guid], r.CastTargets);
        Assert.Contains(r.Log.Lines, l => l.Contains("damage_dealt=0", StringComparison.Ordinal) && l.Contains("fighting on until a damage line", StringComparison.Ordinal));
        Assert.Equal(1, r.Log.Lines.Count(l => l.Contains("fighting on until a damage line", StringComparison.Ordinal))); // logged once
    }

    [Fact]
    public void A_one_shot_kill_with_no_other_tusker_fails_at_the_seconds_limit_naming_the_missing_damage()
    {
        var r = new Rig(fight: new FightOptions { Kills = 1, Seconds = 40 });
        r.Add(0x80001001, TuskerSlave, 0x01050303, 35, hitsToKill: 1);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Failed, r.Run(scenario, 120));

        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(0, scenario.Tally.DamageDealt);
        Assert.Contains("kills=1 of 1 and damage_dealt=0", scenario.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dying_tusker_is_not_picked_again_while_its_dead_motion_and_deletion_are_still_coming()
    {
        // 3.1-R2: ACE sends the 0x01AD, then the Dead motion a moment later, and keeps the body for its death animation. Two seconds of that, and the loop must go to the other tusker at once.
        var r = new Rig(fight: new FightOptions { Kills = 2 }) { DeathDelay = 2.0 };
        var near = r.Add(0x80001001, TuskerSlave, 0x01050303, 35);
        var far = r.Add(0x80001002, TuskerSlave, 0x01050303, 45);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(near.Dead && far.Dead);
        Assert.Equal(2, scenario.Tally.Kills);
        Assert.Equal([near.Guid, near.Guid, far.Guid, far.Guid], r.CastTargets); // no cast at the dying near tusker after its kill
        Assert.Equal(0, scenario.Tally.Misses);
        Assert.Equal(0, scenario.Walks);
    }

    [Fact]
    public void A_creature_whose_motion_is_dead_or_that_the_caller_excluded_is_never_nearest()
    {
        var r = new Rig();
        var table = r.R.H.State.Objects;
        r.Add(0x80001001, TuskerSlave, 0x01050303, 35);
        r.Add(0x80001002, TuskerSlave, 0x01050303, 45);
        var self = r.R.H.State.Self.Position!.Value;

        Assert.True(table.NearestCreature(DenGround.Profile.TargetWcids(), in self, 30f, null, [0x80001001u], out var guid));
        Assert.Equal(0x80001002u, guid);

        r.R.Raw(AceWireWriters.UpdateMotion(0x80001001, 1, 2, 2, false, new AceMovementData { Forward = 0x0011 }));
        Assert.True(table.NearestCreature(DenGround.Profile.TargetWcids(), in self, 30f, null, out guid));
        Assert.Equal(0x80001002u, guid);
    }

    [Fact]
    public void A_kill_that_does_not_name_the_target_counts_in_kills_but_not_in_target_kills()
    {
        // 3.1-R7: report-only; the judged kills stays the total.
        var r = new Rig { KillText = "You have killed Somebody Else!" };
        r.Add(0x80001001, TuskerSlave, 0x01050303, 35);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(0, scenario.Tally.TargetKills);
        Assert.False(scenario.Tally.LastKillNamedTarget);
        Assert.Contains(" kills=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(" target_kills=0 ", scenario.VerdictFields(), StringComparison.Ordinal); // M5 1 F1: the mana and rate fields now end the line
    }

    [Fact]
    public void A_walker_that_left_the_floor_fails_the_fight_at_once_in_any_phase_with_the_walks_reason()
    {
        // 3.1-R4: the walk reports Failed from its floor check every frame; the scenario reads it in the Cast and Hit phases too, not only while a leg is walking.
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);
        Assert.Equal(ScenarioStatus.Running, r.Frame(scenario));
        while (scenario.Stage != "fight")
        {
            Assert.Equal(ScenarioStatus.Running, r.Frame(scenario));
        }

        walk.LeaveFloor = "the walker left the den's floor: it is in cell 0x01050341";
        var status = r.Frame(scenario);
        Assert.Contains("the walker left the den's floor", scenario.Failure, StringComparison.Ordinal); // the failure is set in that frame
        Assert.Equal(ScenarioStatus.Running, status); // and the run leaves the den before it ends Failed
        Assert.Equal(ScenarioStatus.Failed, r.Run(scenario, 30));
        Assert.Single(r.Places);
        Assert.Equal(0, scenario.Walks);
    }

    [Fact]
    public void A_settle_off_the_floor_is_a_loud_failure_and_a_settle_on_it_goes_on()
    {
        // 3.1-R4 / D12: the arrival cell comes from the server's position. 0x01050341 is the exit portal's room.
        var off = new Rig { ArrivalCell = 0x01050341 };
        off.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var offScenario = off.Scenario(new FakeWalk(), FightStages.Place | FightStages.Mode);
        var offStatus = off.Run(offScenario, 60);
        Assert.True(offStatus == ScenarioStatus.Failed, string.Join(" | ", off.Log.Lines));
        Assert.Contains("settle: the player settled in cell 0x01050341", offScenario.Failure, StringComparison.Ordinal);

        var on = new Rig { ArrivalCell = 0x01050375 };
        on.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var onScenario = on.Scenario(new FakeWalk(), FightStages.Place | FightStages.Mode);
        Assert.Equal(ScenarioStatus.Succeeded, on.Run(onScenario, 120));
        Assert.Contains(on.Log.Lines, l => l.Contains("settled in cell 0x01050375; floor cell yes", StringComparison.Ordinal));
    }

    [Fact]
    public void The_fights_teleport_goes_to_the_grounds_arrival_M5_2_g2b()
    {
        // The place is a location request built from the profile's arrival (GroundRules.ArrivalDestination), not the Places row: the first place of the run names the arrival's cell.
        var r = new Rig { ArrivalCell = 0x01050375 };
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var scenario = r.Scenario(new FakeWalk(), FightStages.Place | FightStages.Mode);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario, 120));
        var place = r.Places[0];
        Assert.Contains("place loc", place, StringComparison.Ordinal);
        Assert.Contains(DenGround.Profile.Arrival.Cell.ToString("X8", CultureInfo.InvariantCulture), place, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_target_that_dies_without_a_kill_notice_is_left_after_two_seconds_and_not_counted()
    {
        var r = new Rig(fight: new FightOptions { Seconds = 40 });
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Script(Shot.SilentKill);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Failed, r.Run(scenario, 120));

        Assert.Equal(0, scenario.Tally.Kills);
        Assert.Contains(r.Log.Lines, l => l.Contains("no kill notice came within 2 s", StringComparison.Ordinal));
    }

    [Fact]
    public void A_walk_that_cannot_be_planned_fails_the_fight_loudly_and_a_missing_walker_is_a_named_todo()
    {
        var r = new Rig();
        r.Add(0x80001001, 23, 0x01050303, 25, "Virindi");
        var scenario = r.Scenario(new FakeWalk { FailNext = true });
        Assert.Equal(ScenarioStatus.Failed, r.Run(scenario));
        Assert.Contains("no walk could be planned", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("the fake walk is cut", scenario.Failure, StringComparison.Ordinal);

        var none = new Rig();
        none.Add(0x80001001, 23, 0x01050303, 25, "Virindi");
        var withoutWalker = none.Scenario(null);
        Assert.Equal(ScenarioStatus.Failed, none.Run(withoutWalker));
        Assert.Contains("TODO(m3-fight-no-walker)", withoutWalker.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cast_that_needs_more_mana_waits_for_regeneration_and_logs_it_once()
    {
        var r = new Rig(mana: 5) { ManaRegenPerSecond = 2 };
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Contains(r.Log.Lines, l => l.Contains("waiting for mana", StringComparison.Ordinal));
        Assert.Equal(1, scenario.Tally.Kills);
    }

    [Fact]
    public void The_loop_allocates_nothing_while_it_waits_for_mana_for_a_hit_or_for_a_leg()
    {
        // Waiting for mana.
        var mana = new Rig(mana: 1) { ManaRegenPerSecond = 0 };
        mana.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var waiting = mana.Scenario(new FakeWalk());
        for (var i = 0; i < 40; i++)
        {
            Assert.Equal(ScenarioStatus.Running, mana.Frame(waiting));
        }

        Assert.Empty(mana.CastTargets);
        Assert.Equal(0, Allocated(mana, waiting));

        // Waiting for a hit that never comes (a projectile with no damage line): the three-second window.
        var hit = new Rig { CastSeconds = 0.1 };
        hit.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        hit.Script(Shot.Dud);
        var hitting = hit.Scenario(new FakeWalk());
        for (var i = 0; i < 60 && hit.CastTargets.Count == 0; i++)
        {
            hit.Frame(hitting);
        }

        hit.Frame(hitting, 0.25);
        hit.Frame(hitting, 0.25);
        Assert.Equal(0, Allocated(hit, hitting, 0.01, 100)); // one second of the 3 s window

        // Walking a leg.
        var walk = new Rig();
        walk.Add(0x80001001, 23, 0x01050303, 25, "Virindi");
        var legs = new FakeWalk { TicksPerLeg = int.MaxValue };
        var walking = walk.Scenario(legs);
        for (var i = 0; i < 40 && legs.Legs == 0; i++)
        {
            walk.Frame(walking);
        }

        Assert.Equal(1, legs.Legs);
        Assert.Equal(0, Allocated(walk, walking));
    }

    private static long Allocated(Rig rig, FightScenario scenario, double dt = 0.01, int frames = 300)
    {
        scenario.Step(rig.View, rig.Now);
        long allocated = 0;
        for (var i = 0; i < frames; i++)
        {
            rig.R.H.R.Time.Advance(TimeSpan.FromSeconds(dt));
            var before = GC.GetAllocatedBytesForCurrentThread();
            scenario.Step(rig.View, rig.Now);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        return allocated;
    }

    [Fact]
    public void The_target_rule_the_loop_filters_by_keeps_the_three_named_creatures_out()
    {
        // The rule the filter applies is the ground's (GroundRules.IsTarget over the profile's roles and walkable cells): pinned here at the three cells the brief names, so a change to the profile cannot silently let them in.
        var den = DenGround.Profile;
        Assert.False(den.IsTarget(23, 0x01050303));
        Assert.False(den.IsTarget(1610, 0x01050303));
        Assert.False(den.IsTarget(TuskerSlave, DeadChestCorner));
        Assert.True(den.IsTarget(TuskerSlave, 0x01050303));
        Assert.True(den.IsTarget(1629, 0x010502E1));
    }

    [Fact]
    public void A_creature_in_a_no_go_cell_is_never_a_target_even_when_its_wcid_is_one_M5_2_g2b()
    {
        // The profile's role for a wcid decides the creature, its cell decides where it counts: a tusker (role target) standing in each no-go cell is no target, in a walkable cell it is. An unlisted wcid is none anywhere.
        var den = DenGround.Profile;
        Assert.NotEmpty(den.NoGo.Cells);
        Assert.All(den.NoGo.Cells, cell => Assert.False(den.IsTarget(TuskerSlave, cell), $"0x{cell:X8}"));
        Assert.All(den.Walkable.Cells.Take(40), cell => Assert.True(den.IsTarget(TuskerSlave, cell), $"0x{cell:X8}"));
        Assert.False(den.IsTarget(3955, 0x01050303));
        Assert.Equal([1628u, 1629u], den.TargetWcids());
    }

    [Fact]
    public void An_unknown_fight_ground_fails_the_parse_naming_the_known_keys_and_keeps_the_default_M5_2_g2b()
    {
        var bad = FightOptions.FromArguments(Lifestone.Client.Options.CommandLineArgument.ParseAll(["--fight-ground=no-such-ground"]));
        var error = Assert.Single(bad.Errors);
        Assert.Contains("--fight-ground", error, StringComparison.Ordinal);
        Assert.Contains("'no-such-ground'", error, StringComparison.Ordinal);
        Assert.Contains(FightOptions.DefaultGround, error, StringComparison.Ordinal);
        Assert.Equal(FightOptions.DefaultGround, bad.Ground);

        var outdoor = FightOptions.FromArguments(Lifestone.Client.Options.CommandLineArgument.ParseAll(["--fight-ground=low-b-belt"]));
        Assert.Empty(outdoor.Errors); // M6 0 a5: an outdoor ground with cells is a fight ground (its outdoor tour)

        var good = FightOptions.FromArguments(Lifestone.Client.Options.CommandLineArgument.ParseAll(["--fight-ground=" + FightOptions.DefaultGround]));
        Assert.Empty(good.Errors);
        Assert.Same(GroundCatalog.Get(FightOptions.DefaultGround), good.GroundProfile);
        Assert.Throws<KeyNotFoundException>(() => new FightOptions { Ground = "no-such-ground" }.GroundProfile);
    }

    [Fact]
    public void The_fight_options_parse_and_the_defaults_are_one_kill_and_five_minutes()
    {
        Assert.Equal((1, 300.0), (new FightOptions().Kills, new FightOptions().Seconds));
        var parsed = FightOptions.FromArguments(Lifestone.Client.Options.CommandLineArgument.ParseAll(["--fight-kills=4", "--fight-seconds=90"]));
        Assert.Empty(parsed.Errors);
        Assert.Equal((4, 90.0), (parsed.Kills, parsed.Seconds));
        var bad = FightOptions.FromArguments(Lifestone.Client.Options.CommandLineArgument.ParseAll(["--fight-kills=-1", "--fight-seconds=abc"])); // M5 1 F1: 0 is now no kill limit
        Assert.Equal(2, bad.Errors.Count);
        Assert.Equal((1, 300.0), (bad.Kills, bad.Seconds));
    }

    [Fact]
    public void The_fight_feeds_the_tour_each_kill_with_the_targets_wcid_and_cell_and_each_creature_created_during_the_fight()
    {
        // g6 item 3: the pick is unchanged (the nearest target with a clear line); the tour hears the kill and the creates.
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40); // 20 m: the nearest
        var tour = new RecordingTour();
        var scenario = r.Scenario(new FakeWalk { Tour = tour });
        for (var i = 0; i < 400 && scenario.Stage != "fight"; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal("fight", scenario.Stage);
        r.Add(0x80001002, TuskerGuard, 0x01050320, 45); // created during the fight, 25 m away
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal([(TuskerSlave, 0x01050303u)], tour.Kills);
        Assert.Equal([0x80001001u], tour.KillGuids); // 2-R1: the guid goes with the kill, so the schedule pairs it by its spawn
        Assert.Contains((TuskerGuard, 0x01050320u), tour.Creates);
        Assert.Contains(0x80001002u, tour.CreateGuids);
        Assert.Equal([0x80001001u, 0x80001001u], r.CastTargets);
    }

    [Fact]
    public void The_tour_fields_follow_the_mana_fields_on_the_verdict_line_and_the_bsd_fight_line()
    {
        // g6 item 5 (D44, report-only): storeys_visited= basement_entries= returns= slots_standing=, after the mana fields' last, dances_landed=.
        var r = new Rig(fight: new FightOptions { StoreysMin = 2 });
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var tour = new GroundTour(DenGround.Profile, (_, _) => double.PositiveInfinity, r.Log); // M5 7 P7o: no other storey is reachable, so the one kill ends the fight as before (named: storeys unmet)
        var scenario = r.Scenario(new FakeWalk { Tour = tour, Recovered = 2 });
        Assert.Equal(2, tour.StoreysMin); // --fight-storeys-min reaches the walk's tour

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(1, tour.Schedule.Paired); // the Slave in 0x0303 paired with one of that cell's two slots
        var standing = tour.Schedule.HuntedCount - 1;
        Assert.Matches(@" dances_landed=\d+ storeys_visited=1 basement_entries=0 returns=0 slots_standing=" + standing + " legs_recovered=2 kills_unpaired=0 slots_expired=0" + Purpose + LootOff + "$", scenario.VerdictFields());
        Assert.Matches($"^storeys_visited=1 basement_entries=0 returns=0 slots_standing={standing} legs_recovered=2 kills_unpaired=0 slots_expired=0" + Purpose + "$", scenario.TourFields());
        Assert.Equal((1, 0), (scenario.StoreysVisited, scenario.BasementEntries));
        var t = scenario.Tally;
        var line = FightLegVerdict.Compose(scenario.Failure, new FightLegMeasures(t.Kills, t.DamageDealt, t.Casts, t.Misses, t.XpGained, t.DamageTaken, t.HitsTaken, t.Evades, null, null, null, null, scenario.FightSeconds, ManaFields: scenario.ManaFields(), TourFields: scenario.TourFields())).Line;
        Assert.Matches(@" dances_landed=\d+ storeys_visited=1 basement_entries=0 returns=0 slots_standing=\d+ legs_recovered=2 kills_unpaired=0 slots_expired=0" + Purpose + "$", line); // the leg line is the leg runner's: the loot fields end the scenario's verdict only (For the lead: FightLegVerdict)

        var bare = new Rig();
        bare.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var plain = bare.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, bare.Run(plain));
        Assert.EndsWith(" storeys_visited=1 basement_entries=0 returns=n/a slots_standing=n/a legs_recovered=n/a kills_unpaired=n/a slots_expired=n/a" + NoPurpose + LootOff, plain.VerdictFields(), StringComparison.Ordinal); // a walk without the ground's tour
    }

    [Fact]
    public void Recovered_legs_unpaired_kills_and_slots_expired_unseen_appear_on_the_verdict_line_and_the_bsd_fight_line()
    {
        // 2-R4 (report-only): a Slave dying in a cell that holds no slot pairs with none (kills_unpaired=1); the walk reports 3 legs recovered; a slot long dead with no create seen is taken as standing (slots_expired=1).
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, Den, 40); // the arrival's own cell: the den's profile has no slot there
        var tour = new GroundTour(DenGround.Profile, (_, _) => 0, r.Log);
        Assert.True(tour.Schedule.Killed(TuskerSlave, 0x01050303, 0) >= 0); // a slot dead from the start of the run, never seen again
        var scenario = r.Scenario(new FakeWalk { Tour = tour, Recovered = 3 });

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));
        Assert.Equal(1, tour.Schedule.Expire(5000)); // as the tour does before a choice, long past due (TODO(ground-respawn-unseen))

        const string tail = @" storeys_visited=1 basement_entries=0 returns=0 slots_standing=\d+ legs_recovered=3 kills_unpaired=1 slots_expired=1" + Purpose + "$";
        Assert.Matches(@" dances_landed=\d+" + tail[..^1] + LootOff + "$", scenario.VerdictFields());
        var t = scenario.Tally;
        var line = FightLegVerdict.Compose(scenario.Failure, new FightLegMeasures(t.Kills, t.DamageDealt, t.Casts, t.Misses, t.XpGained, t.DamageTaken, t.HitsTaken, t.Evades, null, null, null, null, scenario.FightSeconds, ManaFields: scenario.ManaFields(), TourFields: scenario.TourFields())).Line;
        Assert.Matches(@"^bsd-fight done .* dances_landed=\d+" + tail, line);
    }

    [Fact]
    public void While_the_tour_wants_to_leave_the_storey_the_pick_takes_a_target_off_it_so_the_second_kill_is_taken_below()
    {
        // 2-R2 on the den's profile: --fight-storeys-min 2 and two kills; two Slaves stand on the arrival floor (20 m and 22 m) and a Guard on z -6 (28 m). After the floor's first kill the pick must not take the floor's second Slave.
        var r = new Rig(fight: new FightOptions { StoreysMin = 2, Kills = 2 });
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Add(0x80001002, TuskerSlave, 0x01050303, 42);
        r.Add(0x80001003, TuskerGuard, 0x01050280, 48);
        var tour = new GroundTour(DenGround.Profile, (_, _) => 0, r.Log);
        var scenario = r.Scenario(new FakeWalk { Tour = tour });

        for (var i = 0; i < 4000 && scenario.Tally.Kills < 2 && r.Frame(scenario) == ScenarioStatus.Running; i++)
        {
        }

        Assert.Equal([0x80001001u, 0x80001001u, 0x80001003u, 0x80001003u], r.CastTargets); // the floor's nearest, then the Guard on z -6, never the floor's other Slave
        Assert.Equal(2, tour.StoreysVisited);
        Assert.Equal(2, tour.Schedule.Paired);

        // M5 7 P7o: the Guard died on z -6 to a bolt cast from the arrival's floor; the body stood on one storey (storeys_visited=1, the judged field), so the quota does not win
        for (var i = 0; i < 40; i++)
        {
            Assert.Equal(ScenarioStatus.Running, r.Frame(scenario));
        }

        Assert.Equal(1, scenario.StoreysVisited);
        Assert.Contains(r.Log.Lines, l => l.Contains(FightScenario.StoreysHoldLine + ": kills=2 of 2 with 1 of 2 storeys visited by the body (tour 2)", StringComparison.Ordinal));
        Assert.Equal(1, tour.BodyStoreysVisited);
    }

    [Fact]
    public void With_no_target_off_the_storey_in_range_the_tour_that_wants_to_leave_walks_instead_of_taking_the_floors_next_target()
    {
        // 2-R2: the pick prefers targets off the storey and, with none in range, the fight walks (the tour offers the next storey) rather than kill the floor's second Slave and never leave.
        var r = new Rig(fight: new FightOptions { StoreysMin = 2, Kills = 2 });
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Add(0x80001002, TuskerSlave, 0x01050303, 42);
        var tour = new GroundTour(DenGround.Profile, (_, _) => 0, r.Log);
        var walk = new FakeWalk { Tour = tour };
        var scenario = r.Scenario(walk);

        for (var i = 0; i < 4000 && walk.Legs == 0 && r.Frame(scenario) == ScenarioStatus.Running; i++)
        {
        }

        Assert.Equal(1, walk.Legs);
        Assert.Equal([0x80001001u, 0x80001001u], r.CastTargets); // one kill, then the walk
        Assert.Contains(r.Log.Lines, l => l.Contains("walking: the tour leaves this storey", StringComparison.Ordinal));
    }

    [Fact]
    public void A_fight_that_starts_with_creatures_already_in_their_slot_cells_pairs_every_kill_of_them_by_its_spawn_wherever_they_die()
    {
        // F4 (the live finding: kills=3, kills_unpaired=2): the creatures stand in the den before the fight exists, so no create of the fight's reaches the schedule; the fight's start seeds it. Two Slaves in the cell of
        // the den's two Slave slots and a Guard in a Guard slot's cell; the second Slave runs to the arrival's room, which holds no slot, and dies there.
        var r = new Rig(fight: new FightOptions { Kills = 3 });
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Add(0x80001002, TuskerSlave, 0x01050303, 42);
        r.Add(0x80001003, TuskerGuard, 0x01050280, 48);
        var tour = new GroundTour(DenGround.Profile, (_, _) => 0, r.Log);
        var scenario = r.Scenario(new FakeWalk { Tour = tour });
        Assert.Equal(0, tour.Schedule.Seeded); // the tour was made before any session: the fight's start seeds it

        for (var i = 0; i < 400 && scenario.Stage != "fight"; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal("fight", scenario.Stage);
        Assert.Equal(3, tour.Schedule.Seeded);
        r.Move(0x80001002, Den, 42);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(3, scenario.Tally.TargetKills);
        Assert.Equal((3, 3, 0, 0), (tour.Schedule.Paired, tour.Schedule.PairedBySpawn, tour.Schedule.PairedByCell, tour.Schedule.Unpaired));
        Assert.Matches(@" legs_recovered=n/a kills_unpaired=0 slots_expired=0" + Purpose + LootOff + "$", scenario.VerdictFields());
        Assert.Contains(r.Log.Lines, l => l.Contains("paired by its spawn in 0x01050303; it died away from the slot's cell", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("pairs with no standing slot", StringComparison.Ordinal));
    }

    [Fact]
    public void A_creature_standing_away_from_every_slot_cell_when_the_fight_starts_falls_back_to_the_cell_it_died_in_named_and_is_unpaired_where_that_holds_no_slot()
    {
        // F4: two Slaves stand in the arrival's room, which holds no slot, so the seed remembers neither. One dies there (unpaired, named: no slot of that cell); the other runs into the cell of the den's Slave slots and dies there
        // (paired by the cell it died in, named: its guid was not seen created in a slot's cell). The behaviour before the seed, kept for what the seed cannot know.
        var r = new Rig(fight: new FightOptions { Kills = 2 });
        r.Add(0x80001001, TuskerSlave, Den, 40);
        r.Add(0x80001002, TuskerSlave, Den, 42);
        var tour = new GroundTour(DenGround.Profile, (_, _) => 0, r.Log);
        var scenario = r.Scenario(new FakeWalk { Tour = tour });
        for (var i = 0; i < 400 && scenario.Stage != "fight"; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal("fight", scenario.Stage);
        Assert.Equal(0, tour.Schedule.Seeded);
        r.Move(0x80001002, 0x01050303, 42);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal((1, 0, 1, 1), (tour.Schedule.Paired, tour.Schedule.PairedBySpawn, tour.Schedule.PairedByCell, tour.Schedule.Unpaired));
        Assert.Contains(r.Log.Lines, l => l.Contains("paired by the cell it died in: its guid was not seen created in a slot's cell", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("pairs with no standing slot", StringComparison.Ordinal));
        Assert.Matches(@" kills_unpaired=1 slots_expired=0" + Purpose + LootOff + "$", scenario.VerdictFields());
    }

    [Fact]
    public void A_tour_made_with_the_object_table_seeds_the_living_creatures_standing_in_a_slots_cell_and_no_others()
    {
        // F4: the tour (or schedule) made with a session. 1001 stands in a Slave slot's cell: remembered. 1002 is a Slave in the arrival's room (no slot), 1003 a Slave in a slot's cell that is already dead (the Dead motion),
        // 1004 a Guard in a Slave slot's cell (no Guard slot there): none is remembered.
        var r = new Rig();
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        r.Add(0x80001002, TuskerSlave, Den, 42);
        r.Add(0x80001003, TuskerSlave, 0x01050303, 44);
        r.R.Raw(AceWireWriters.UpdateMotion(0x80001003, 1, 2, 2, false, new AceMovementData { Forward = 0x0011 })); // MotionCommand.Dead
        r.Add(0x80001004, TuskerGuard, 0x01050303, 46);

        var tour = new GroundTour(DenGround.Profile, (_, _) => 0, r.Log, r.R.H.State.Objects, r.Now);

        Assert.Equal(1, tour.Schedule.Seeded);
        Assert.True(tour.Schedule.SlotOfGuid(0x80001001) >= 0);
        Assert.Equal((-1, -1, -1), (tour.Schedule.SlotOfGuid(0x80001002), tour.Schedule.SlotOfGuid(0x80001003), tour.Schedule.SlotOfGuid(0x80001004)));
        Assert.Contains(r.Log.Lines, l => l.Contains("seeded from the creatures already known: 3 of a slot's wcid, 1 standing in a slot's cell", StringComparison.Ordinal));
    }

    [Fact]
    public void The_review_frames_wait_for_another_storey_only_with_storeys_min_two_and_the_scenario_knows_when_the_body_is_off_the_arrivals_storey()
    {
        // F3: the fight leg's plan is told to wait (FightShotPlan.WaitsForOtherStorey) when --fight-storeys-min is 2 or more and a tour knows the arrival's storey; OffArrivalStorey follows the body's cell.
        var r = new Rig(fight: new FightOptions { StoreysMin = 2, Kills = 2 });
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var tour = new GroundTour(DenGround.Profile, (_, _) => 0, r.Log);
        var scenario = r.Scenario(new FakeWalk { Tour = tour });
        Assert.True(scenario.ReviewFramesWaitForOtherStorey);
        Assert.False(scenario.OffArrivalStorey); // not seen on a storey yet

        for (var i = 0; i < 40; i++)
        {
            r.Frame(scenario);
        }

        Assert.False(scenario.OffArrivalStorey); // on the arrival floor
        var h = r.R.H.R;
        h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(ActionHarness.Amy, 1, 3, 2, 1, cell: 0x01050280, x: PlayerX));
        for (var i = 0; i < 40; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(scenario.OffArrivalStorey); // on the z -6 storey

        Assert.False(new Rig(fight: new FightOptions { Kills = 2 }).Scenario(new FakeWalk { Tour = tour }).ReviewFramesWaitForOtherStorey); // the default 1: nothing changes
        Assert.False(r.Scenario(new FakeWalk()).ReviewFramesWaitForOtherStorey); // no tour, no arrival storey: the frames are not starved
    }

    [Theory]
    [InlineData(80, 7, 6, "the ground's plan caps Flame Bolt at VI for level 80")]
    [InlineData(99, 7, 6, "the ground's plan caps Flame Bolt at VI for level 99")]
    [InlineData(100, 7, 7, "the ground's plan caps Flame Bolt at VII for level 100")]
    [InlineData(127, 7, 7, "the ground's plan caps Flame Bolt at VII for level 127")]
    [InlineData(127, 5, 5, "--fight-max-level caps Flame Bolt at V (the ground's plan allows VII for level 127)")]
    [InlineData(80, 4, 4, "--fight-max-level caps Flame Bolt at IV (the ground's plan allows VI for level 80)")]
    public void The_grounds_spell_plan_caps_the_level_and_the_max_level_caps_it_lower(int characterLevel, int maxLevel, int cap, string note)
    {
        // g6 item 4 (D41, m5-level80-vi): the den's plan is Flame Bolt VI below level 100, VII from 100.
        Assert.Equal(cap, FightScenario.PlanCap(DenGround.Profile, characterLevel, maxLevel, out var why));
        Assert.Equal(note, why);
    }

    [Fact]
    public void A_level_80_mage_casts_VI_and_BSD_Mage_at_127_keeps_VII_logged_when_chosen()
    {
        var uncapped = FlameBoltIndexFor(new Rig());
        Assert.True(uncapped >= 6); // the rig's War Magic reaches VII

        var r80 = new Rig(characterLevel: 80);
        r80.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var at80 = r80.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, r80.Run(at80));
        Assert.Equal("VI", at80.Level);
        Assert.Contains(r80.Log.Lines, l => l.Contains("the ground's plan caps Flame Bolt at VI for level 80", StringComparison.Ordinal));
        Assert.All(r80.CastSpells, id => Assert.Equal(ArenaProfile.LineNamed("Flame Bolt")!.Spells[5].Id, id));

        var r127 = new Rig(characterLevel: 127);
        r127.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var at127 = r127.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, r127.Run(at127));
        Assert.Equal("VII", at127.Level);

        var capped = new Rig(characterLevel: 127, fight: new FightOptions { MaxLevel = 5 });
        capped.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var at5 = capped.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, capped.Run(at5));
        Assert.Equal("V", at5.Level);
        Assert.Contains(capped.Log.Lines, l => l.Contains("--fight-max-level caps Flame Bolt at V", StringComparison.Ordinal));
    }

    [Fact]
    public void The_fight_storeys_min_option_parses_one_to_sixteen()
    {
        Assert.Equal(1, new FightOptions().StoreysMin);
        var two = FightOptions.FromArguments(Lifestone.Client.Options.CommandLineArgument.ParseAll(["--fight-storeys-min=2"]));
        Assert.Empty(two.Errors);
        Assert.Equal(2, two.StoreysMin);
        Assert.Contains(" storeys_min=2", two.ToString(), StringComparison.Ordinal);
        foreach (var bad in new[] { "0", "17", "two" })
        {
            var parsed = FightOptions.FromArguments(Lifestone.Client.Options.CommandLineArgument.ParseAll(["--fight-storeys-min=" + bad]));
            Assert.Single(parsed.Errors);
            Assert.Equal(1, parsed.StoreysMin);
        }
    }

    private static GearItem GearItemOf(uint wcid, string name, GearKind kind, GearWeenie w) =>
        new(wcid, name, kind, w.WeenieType, w.ItemType, w.ValidLocations, 1, null, w.Burden);

    // ------------------------------------------------------------------------------------------------ M5 item 3 m2: the loot step (--fight-loot, D38)

    private const uint Pyreal = 0x80007001;
    private const uint Gem = 0x80007002;
    private const uint Sword = 0x80007003;
    private const uint Orb = 0x80007004;

    /// <summary>The corpse's items: pyreals, a gem (60 pyreals per burden), a heavy sword (under the floor) and an orb whose description sent no Value (an appraisal says 800).</summary>
    private static void StockCorpse(Rig r)
    {
        r.CorpseItems.Add((Pyreal, 273, 0x40, 30, 0, "Pyreal"));
        r.CorpseItems.Add((Gem, 2001, 0x800, 600, 10, "Gem"));
        r.CorpseItems.Add((Sword, 2002, 0x1, 20, 200, "Rusty Sword"));
        r.CorpseItems.Add((Orb, 2003, 0x80, null, 4, "Mystery Orb"));
        r.AppraisedValues[Orb] = 800;
    }

    [Fact]
    public void With_looting_off_the_loot_step_never_runs_and_the_fields_read_loot_off()
    {
        var r = new Rig { Corpses = true };
        StockCorpse(r);
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal("loot=off looted=0 kept=0 loot_value=0", scenario.LootFields());
        Assert.EndsWith(LootOff, scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.False(scenario.LootBusy);
        Assert.Equal(0, scenario.LootCorpsesLeft);
        Assert.Empty(r.Opened);
        Assert.Empty(r.Picked);
        Assert.Empty(r.Closed);
        Assert.Empty(r.Identified);
        Assert.Empty(r.R.H.Net.Of(FakeSendKind.Use));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("loot:", StringComparison.Ordinal) || l.Contains("purpose=loot", StringComparison.Ordinal));
    }

    /// <summary>The loot rule of F28's rig numbers (a floor of 10, the profile's later payout floor and weight cap off): the stand-in corpses hold small things, which the F29 den values (60, 4,000, 150) would leave.</summary>
    private static readonly MageLootThresholds RigLoot = new() { MinValuePerBurden = 10, MinPayout = 1, MaxItemBurden = 100_000 };

    [Fact]
    public void With_looting_on_a_corpse_ten_metres_off_is_opened_through_aces_move_to_keeps_by_the_rule_and_goes_on()
    {
        var r = new Rig(fight: new FightOptions { Kills = 2, Loot = true, LootThresholds = RigLoot, LootAggroMetres = 0 }) { Corpses = true, MoveToSeconds = 3 }; // the second tusker stands beside the first corpse: the hostile rule is tested alone below
        StockCorpse(r);
        var first = r.Add(0x80001001, TuskerSlave, Den, 30); // 10 m from the player at y 20
        var second = r.Add(0x80001002, TuskerSlave, Den, 34);
        r.ItemsOfCreature[second.Guid] = [(0x80007011, 2011, 0x800, 300, 5, "Opal")];
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Null(scenario.Failure);
        Assert.True(first.Dead && second.Dead);
        Assert.Equal(2, scenario.Tally.Kills); // the last kill ended nothing until its corpse was looted: both corpses are done
        var firstCorpse = first.Guid + Rig.CorpseOffset;
        var secondCorpse = second.Guid + Rig.CorpseOffset;
        Assert.Equal([firstCorpse, secondCorpse], r.Opened); // the Use alone: ACE's MoveTo brought the mage (the rig answers the open 3 s after it)
        Assert.Equal([firstCorpse, secondCorpse], r.Closed);
        Assert.Equal([Orb], r.Identified); // only the item with no Value in its description is appraised
        Assert.Equal([Pyreal, Gem, Orb, 0x80007011u], r.Picked); // the corpse's order; the sword is under the value-per-burden floor
        Assert.Equal(2, r.Log.Lines.Count(l => l.Contains("ACE's own MoveTo walks the mage", StringComparison.Ordinal) && l.Contains("los=yes", StringComparison.Ordinal)));
        Assert.All(r.Places, p => Assert.Contains("poi", p, StringComparison.Ordinal)); // never a place for a corpse: a cheat-free mage walks
        Assert.Contains(r.Log.Lines, l => l.Contains("plan for 0x80001101", StringComparison.Ordinal) && l.Contains("keep 3 of 4", StringComparison.Ordinal));
        Assert.Equal(0, scenario.LootCorpsesLeft);
        Assert.False(scenario.LootBusy);
        Assert.Matches(@"^loot=on looted=2 kept=4 loot_value=1730 loot_opened=2 loot_skipped_unreached=0 loot_skipped_hostile=0 loot_walk_s=[3-9]", scenario.LootFields()); // 30 + 600 + 800, then 300; 3 s of walking each
        Assert.Contains(" loot=on looted=2 kept=4 loot_value=1730 loot_opened=2 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(r.CastTargets, t => t == second.Guid); // the fight went back to the tour: the second tusker was hunted after the first corpse
    }

    [Fact]
    public void A_corpse_with_a_living_tusker_beside_it_is_left_by_name_and_counted_and_the_fight_goes_first()
    {
        var r = new Rig(fight: new FightOptions { Kills = 1, Loot = true }) { Corpses = true };
        StockCorpse(r);
        r.Add(0x80001001, TuskerSlave, Den, 30);
        r.Add(0x80001002, TuskerSlave, Den, 34, "Tusker Slave", 2, 103); // 5 m from the corpse, never cast at in this run (one kill)
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Empty(r.Opened);
        Assert.Equal(1, scenario.LootCorpsesLeft);
        Assert.Contains(r.Log.Lines, l => l.Contains("is put off: Tusker Slave 0x80001002 stands", StringComparison.Ordinal) && l.Contains("--fight-loot-aggro-m", StringComparison.Ordinal) && l.Contains("the fight goes first", StringComparison.Ordinal));
        Assert.Equal("loot=on looted=0 kept=0 loot_value=0 loot_opened=0 loot_skipped_unreached=0 loot_skipped_hostile=1 loot_walk_s=0 loot_plan_kept=0 loot_plan_left=0 loot_plan_value=0 corpses_opened=0 loot_left_aggro=1 loot_left_decayed=0 worth_a_look=0 stack_merges=0 loot_busy_waits=0 loot_left_busy=0", scenario.LootFields());
    }

    [Fact]
    public void A_move_to_that_ends_without_the_open_skips_the_corpse_named_once_and_the_fight_resumes()
    {
        var r = new Rig(fight: new FightOptions { Kills = 2, Loot = true, LootAggroMetres = 0 }) { Corpses = true, MoveToFailsAfter = 15 }; // ACE's own 15 s MoveTo timeout fails the chain (Player_Move.cs:16)
        StockCorpse(r);
        r.Add(0x80001001, TuskerSlave, Den, 30);
        var second = r.Add(0x80001002, TuskerSlave, Den, 34);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(second.Dead); // the fight resumed after the first corpse
        Assert.Equal(2, scenario.LootCorpsesLeft);
        Assert.Equal(1, r.Log.Lines.Count(l => l.Contains("loot: 'Corpse of Tusker Slave' 0x80001101 not reached:", StringComparison.Ordinal)));
        Assert.Empty(r.Picked);
        Assert.Matches(@"^loot=on looted=0 kept=0 loot_value=0 loot_opened=0 loot_skipped_unreached=2 loot_skipped_hostile=0 loot_walk_s=\d", scenario.LootFields());
    }

    [Fact]
    public void A_move_to_that_runs_past_the_wait_skips_the_corpse_named_with_the_seconds()
    {
        var r = new Rig(fight: new FightOptions { Kills = 1, Loot = true, LootWalkSeconds = 6 }) { Corpses = true, MoveToSeconds = 30 };
        StockCorpse(r);
        r.Add(0x80001001, TuskerSlave, Den, 30);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(1, scenario.LootCorpsesLeft);
        Assert.Contains(r.Log.Lines, l => l.Contains("not reached: ACE's move-to brought no open within 6 s", StringComparison.Ordinal));
        Assert.Empty(r.Picked);
    }

    [Fact]
    public void A_corpse_beyond_the_loot_radius_is_left_and_never_walked_to()
    {
        var r = new Rig(fight: new FightOptions { Kills = 1, Loot = true }) { Corpses = true };
        StockCorpse(r);
        r.Add(0x80001001, TuskerSlave, Den, 38); // 18 m: in cast range (30 m), beyond the 15 m loot radius
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Empty(r.Opened);
        Assert.Equal(1, scenario.LootCorpsesLeft);
        Assert.Contains(r.Log.Lines, l => l.Contains("beyond the loot radius (15 m, --fight-loot-radius-m)", StringComparison.Ordinal));
        Assert.Matches(@"loot_skipped_unreached=1 loot_skipped_hostile=0", scenario.LootFields());
    }

    [Fact]
    public void A_corpse_out_of_sight_is_left_by_name_and_not_walked_to()
    {
        var r = new Rig(fight: new FightOptions { Kills = 1, Loot = true }) { Corpses = true };
        StockCorpse(r);
        var first = r.Add(0x80001001, TuskerSlave, Den, 30);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);
        for (var i = 0; i < 4000 && !first.Dead; i++)
        {
            Assert.Equal(ScenarioStatus.Running, r.Frame(scenario));
        }

        walk.BlockAll = true; // the corpse lies behind a wall
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Empty(r.Opened);
        Assert.Contains(r.Log.Lines, l => l.Contains("no clear line of sight", StringComparison.Ordinal) && l.Contains("los=no", StringComparison.Ordinal));
        Assert.Matches(@"loot_skipped_unreached=1 loot_skipped_hostile=0", scenario.LootFields());
    }

    /// <summary>M5 4 fix F2 (4-R3): the loot step's corpse open is an Automation send, so the kill switch withdraws it when queued: the corpse is never opened and nothing is taken.</summary>
    [Fact]
    public void M5_4_f2_the_kill_switch_withdraws_a_queued_corpse_open_and_the_loot_step_takes_nothing()
    {
        var r = new Rig(fight: new FightOptions { Kills = 1, Loot = true }) { Corpses = true };
        StockCorpse(r);
        r.Add(0x80001001, TuskerSlave, Den, 30);
        var scenario = r.Scenario(new FakeWalk());
        var api = r.R.Api;
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running && !(scenario.LootBusy && api.QueuedCount > 0); i++)
        {
            status = r.Frame(scenario, 0.25);
        }

        Assert.Equal(ScenarioStatus.Running, status);
        Assert.Empty(r.Opened); // queued, not sent
        var log = r.View.ActionLog;
        Assert.DoesNotContain(Enumerable.Range(0, log.Count).Select(i => log[i]), e => e.Kind == ActionKind.OpenContainer);

        api.KillSwitch = true; // the person throws it
        Assert.Equal(0, api.QueuedCount);
        var withdrawn = Assert.Single(Enumerable.Range(0, log.Count).Select(i => log[i]), e => e.Kind == ActionKind.OpenContainer);
        Assert.Equal((ActionSource.Automation, ActionOutcomeKind.Cancelled), (withdrawn.Source, withdrawn.Outcome.Kind));

        for (var i = 0; i < 4000 && status == ScenarioStatus.Running; i++)
        {
            status = r.Frame(scenario, 0.25);
        }

        Assert.Empty(r.Opened);
        Assert.Empty(r.Picked);
        Assert.Empty(r.Identified);
        Assert.Equal(ScenarioStatus.Failed, status); // the fight winds down naming the switch and abandons the loot step where it stood
        Assert.Contains(r.Log.Lines, l => l.Contains("Blocked why=kill-switch", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("loot: abandoned at the fight", StringComparison.Ordinal));
    }

    [Fact]
    public void A_corpse_within_reach_is_opened_with_no_walk()
    {
        var r = new Rig(fight: new FightOptions { Loot = true, LootThresholds = RigLoot }) { Corpses = true };
        StockCorpse(r);
        r.Add(0x80001001, TuskerSlave, Den, 21); // 1 m from the player at y 20

        var scenario = r.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("purpose=loot", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("within reach", StringComparison.Ordinal) && l.Contains("no walk", StringComparison.Ordinal));
        Assert.Equal([0x80001101u], r.Opened);
        Assert.StartsWith("loot=on looted=1 kept=3 loot_value=1430 loot_opened=1 ", scenario.LootFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_open_is_named_and_the_fight_goes_on()
    {
        var r = new Rig(fight: new FightOptions { Kills = 2, Loot = true, LootAggroMetres = 0 }) { Corpses = true, RefuseOpen = true };
        StockCorpse(r);
        r.Add(0x80001001, TuskerSlave, Den, 30);
        var second = r.Add(0x80001002, TuskerSlave, Den, 34);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(2, scenario.Tally.Kills);
        Assert.True(second.Dead); // the fight went on to the next tusker after the refused corpse
        Assert.Equal(2, r.Opened.Count);
        Assert.Empty(r.Picked);
        Assert.Empty(r.Closed);
        Assert.Equal(2, scenario.LootCorpsesLeft);
        Assert.Contains(r.Log.Lines, l => l.Contains("0x80001101 not reached: the open was not confirmed", StringComparison.Ordinal)); // out of reach, so ACE's walk was asked for: the refusal reads as not reached
        Assert.StartsWith("loot=on looted=0 kept=0 loot_value=0 loot_opened=0 loot_skipped_unreached=2 ", scenario.LootFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_corpse_that_never_appears_is_named_and_the_fight_goes_on()
    {
        var r = new Rig(fight: new FightOptions { Kills = 1, Loot = true });
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40);
        var scenario = r.Scenario(new FakeWalk()); // no corpses in the world

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(1, scenario.LootCorpsesLeft);
        Assert.Contains(r.Log.Lines, l => l.Contains("appeared within 5 s", StringComparison.Ordinal));
        Assert.Empty(r.Opened);
        Assert.Equal("loot=on looted=0 kept=0 loot_value=0 loot_opened=0 loot_skipped_unreached=0 loot_skipped_hostile=0 loot_walk_s=0 loot_plan_kept=0 loot_plan_left=0 loot_plan_value=0 corpses_opened=0 loot_left_aggro=0 loot_left_decayed=0 worth_a_look=0 stack_merges=0 loot_busy_waits=0 loot_left_busy=0", scenario.LootFields());
    }

}

/// <summary>
/// The den's z 0 storey from the dats (M3 3.1 lane fa; since M5 item 2 g2b read through the den's profile): the profile's z 0 walkable cells are the arrival's connected floor (cells at z -1 to 0.5 a standing human walks to from the arrival through the doorways
/// <see cref="RoomGraph.IsPassable"/> accepts, less the exit portal's room and the north wing), never a cell below z -1 nor a no-go one; every spawn cell of the old tour is on it and reachable from the
/// arrival; and why the den needs a lean (the profile's doorway lean) (the stock planner reaches one spawn cell of twelve). Skipped without the dats.
/// </summary>
public sealed class DenGroundFloorDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The z 0 storey's walkable cells: today's 158-cell floor (HuntingGroundTests pins them as literals).</summary>
    private static ushort[] FloorLowWords() => DenGround.Profile.Storeys[0].Cells.Cells.Where(c => !DenGround.Profile.IsNoGo(c)).Select(c => (ushort)(c & 0xFFFF)).Order().ToArray();

    private LandblockCellSet All() => LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), new LandblockCoord(1, 5));

    [RequiresDatsFact]
    public void The_floor_set_is_the_arrivals_connected_floor_read_from_the_cell_graph()
    {
        var cells = All();
        var planner = new RoomGraph(cells, DenGround.Profile.Walk.DoorwayLean);
        var seen = new HashSet<uint> { Den() };
        var queue = new Queue<uint>([Den()]);
        while (queue.Count > 0)
        {
            var cell = cells.GetCell(queue.Dequeue())!;
            foreach (var portal in cell.Portals)
            {
                var other = cells.GetCell(portal.OtherCellId);
                if (portal.LeadsOutside || other is null || seen.Contains(other.Id) || DenGround.Profile.IsNoGo(other.Id)
                    || other.Frame.Origin.Z < -1f || other.Frame.Origin.Z > 0.5f || !planner.IsPassable(cell, portal))
                {
                    continue;
                }

                seen.Add(other.Id);
                queue.Enqueue(other.Id);
            }
        }

        var expected = seen.Select(c => (ushort)(c & 0xFFFF)).Order().ToArray();
        var pinned = FloorLowWords();
        output.WriteLine($"the floor: {pinned.Length} cells; recomputed {expected.Length}");
        Assert.Equal(expected, pinned);
        Assert.Equal(158, pinned.Length);
    }

    [RequiresDatsFact]
    public void No_floor_cell_is_below_z_minus_1_or_excluded_and_the_exit_portals_room_is_out()
    {
        var cells = All();
        foreach (var low in FloorLowWords())
        {
            var cell = cells.GetCell(0x01050000u | low);
            Assert.NotNull(cell);
            Assert.InRange(cell.Frame.Origin.Z, -1f, 0.5f);
            Assert.False(DenGround.Profile.IsNoGo(0x01050000u | low), $"0x{low:X4}");
            Assert.True(DenGround.Profile.IsWalkable(0x01050000u | low), $"0x{low:X4}");
        }

        foreach (var out_ in new uint[] { 0x0341, 0x0342, 0x0343, 0x035C })
        {
            Assert.DoesNotContain((ushort)out_, FloorLowWords());
            Assert.False(DenGround.Profile.IsWalkable(0x01050000u | out_));
        }
    }

    [RequiresDatsFact]
    public void Every_tour_waypoint_is_a_floor_cell_and_reachable_from_the_arrival_over_the_floor()
    {
        var floor = GroundRooms.FloorOf(All(), DenGround.Profile);
        Assert.Equal(DenGround.Profile.Walkable.Count, floor.Count);
        var planner = new RoomGraph(floor, DenGround.Profile.Walk.DoorwayLean);
        Assert.Equal(12, DenGround.SpawnLowWords.Count);
        Assert.All(DenGround.SpawnLowWords, low => Assert.Contains(low, FloorLowWords())); // the old tour is twelve cells of the z 0 floor
        foreach (var low in DenGround.SpawnLowWords)
        {
            var cellId = 0x01050000u | low;
            var route = planner.PlanScripted("test", DenGround.Arrival, cellId, DenGround.Profile.Walk.StopWithinMetres, DenGround.Profile.Walk.StopDwellSeconds);
            Assert.NotNull(route);
            Assert.True(route.Goals.Count >= 1);
            Assert.Equal(cellId, route.Goals[^1].Target.CellId);
            var path = planner.FindPath(DenGround.Arrival.CellId, cellId)!;
            Assert.All(path, c => Assert.True(DenGround.Profile.IsWalkable(c), $"0x{c:X8} on the way to 0x{cellId:X8}"));
            output.WriteLine($"0x{low:X4}: {path.Count - 1} doorways, {route.Goals.Count} goals");
        }
    }

    [RequiresDatsFact]
    public void The_stock_planner_reaches_every_spawn_cell_through_the_dens_leaning_doorways_as_floor_crossings()
    {
        var floor = GroundRooms.FloorOf(All(), DenGround.Profile);
        var stock = new CellRoutePlanner(floor);
        var reached = DenGround.SpawnLowWords.Count(low => stock.FindPath(DenGround.Arrival.CellId, 0x01050000u | low) is not null);
        output.WriteLine($"CellRoutePlanner reaches {reached} of 12 spawn cells; the lean-0.3 RoomGraph reaches 12 (the doorways' normals have a vertical part of 0.196, the stock fit test wants under 0.1, and the floor crossings join them)");
        Assert.Equal(12, reached); // M6 4 W4: 1 (0x0383 only) by the fit test alone (m3-fight-planner-lean: closed, the lean is the planner's parameter); the leaning doorways' floors meet, so they are floor crossings (CellRoutePlanner.FloorCrossingOf) at any lean
    }

    [RequiresDatsFact]
    public void Flame_Bolt_levels_one_to_seven_are_the_dats_ids_in_the_war_school()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var line = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!;
        uint[] ids = [27, 81, 82, 83, 84, 85, 2128];
        Assert.Equal(ids, line.Spells.Take(FightScenario.Levels).Select(s => s.Id));
        string[] numerals = ["I", "II", "III", "IV", "V", "VI"];
        for (var i = 0; i < ids.Length; i++)
        {
            var spell = spells[ids[i]];
            Assert.Equal(MagicSchool.WarMagic, spell.School);
            Assert.Equal(SpellType.Projectile, spell.MetaSpellType);
            Assert.Equal(line.Spells[i].Power, spell.Power);
            Assert.Equal(i < 6 ? "Flame Bolt " + numerals[i] : "Ilservian's Flame", spell.Name);
            output.WriteLine($"{spell.Name} ({spell.Id}): power {spell.Power}, base mana {spell.BaseMana}, range {spell.BaseRangeConstant} + {spell.BaseRangeMod} * skill");
        }
    }

    private static uint Den() => DenGround.Profile.Arrival.Cell;
}

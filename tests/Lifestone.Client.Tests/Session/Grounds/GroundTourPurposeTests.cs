using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Movement.Wayfinding;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using static Lifestone.Client.Tests.Session.Grounds.TwoStoreyGround;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// F5 (D47, "he should always move with purpose"; round 2's pacing): every waypoint the tour offers carries a named purpose (seen, presumed, due, return) or the tour holds; the schedule learns from what the player sees (a slot
/// in view with no creature of its own is seen empty, a create makes it standing again); a cell arrived at is not offered again until one of its slots is due or a create is seen in it; a repeating cycle of offers is caught
/// and held; the run speed is the runner's own walking time; the leg's report is per leg. Over the synthetic <see cref="TwoStoreyGround"/>, and as a theory over both dungeon profiles (the per-ground rule, D46).
/// </summary>
public sealed class GroundTourPurposeTests
{
    /// <summary>The dungeon profiles in the catalog, for the per-ground theories.</summary>
    public static TheoryData<string> Dungeons => new() { "black-spawn-den-aluvian", "tusker-temple-fort-tethana" };

    [Fact]
    public void Two_neighbouring_phantom_slots_are_not_walked_between_again_and_again()
    {
        // Round 2's pacing, reproduced first against the code before F5 (it offered T(1), T(2), T(1), T(2), ...: 6 legs, 2 cells): T(1) and T(2) stand by the schedule (no kill was told) but hold no creature, and each leg
        // arrives and asks again from there. Now a cell arrived at is looked at: the tour goes on to the low storey with purpose, then holds.
        var log = new RecordingLogSink();
        var tour = new GroundTour(Build(), Metres, log);
        var from = Arrival;
        var offered = new List<uint>();
        for (var leg = 0; leg < 8; leg++)
        {
            if (tour.Next(from, leg * 10.0) is not { } cell || cell == from)
            {
                Assert.Equal(TourPurpose.Hold, tour.Purpose);
                break; // a hold: the player's own cell, no leg
            }

            Assert.Equal(TourPurpose.Presumed, tour.Purpose);
            offered.Add(cell);
            tour.LegArrived(cell, (leg * 10.0) + 5);
            from = cell;
        }

        Assert.Equal(offered.Count, offered.Distinct().Count()); // no cell offered twice with no kill and no create between
        Assert.Equal([T(1), T(2), L(1), L(3), L(5)], offered);
        Assert.Equal(1, tour.Holds);
        Assert.Contains(log.Lines, l => l.Contains("tour: holding in 0x0A0A0205", StringComparison.Ordinal) && l.Contains("no hunted slot is dead", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Dungeons))]
    public void Seen_empty_neighbours_are_not_offered_and_the_tour_moves_on_with_purpose(string key)
    {
        var ground = GroundCatalog.Get(key);
        var (a, b) = Neighbours(ground);
        var log = new RecordingLogSink();
        var tour = new GroundTour(ground, StandIn(ground), log, visible: c => c == a ? [b] : c == b ? [a] : null);
        var empty = new WorldObjectTable();

        // Before F5 the player in A was offered B and in B was offered A, for good. Now both are in view with no creature: after the settle they are seen empty.
        tour.Look(a, empty, 0);
        Assert.Equal(0, tour.Schedule.SeenEmpty); // not before the settle (ACE sends the creates a moment after the cells come into view)
        tour.Look(a, empty, RespawnSchedule.SeenEmptySettleSeconds + 0.1);
        var inAOrB = SlotsIn(tour.Schedule, a, b);
        Assert.Equal(inAOrB.Count, tour.Schedule.SeenEmpty);
        Assert.All(inAOrB, i => Assert.True(tour.Schedule.IsSeenEmpty(i) && !tour.Schedule.IsStanding(i)));
        Assert.Equal(inAOrB.Count, log.Lines.Count(l => l.Contains("is seen empty at", StringComparison.Ordinal))); // once per slot

        tour.Look(a, empty, 10);
        Assert.Equal(inAOrB.Count, tour.Schedule.SeenEmpty); // learned once

        var next = tour.Next(a, 11);
        Assert.NotEqual(b, next);
        Assert.NotEqual(a, next);
        Assert.Equal(TourPurpose.Presumed, tour.Purpose); // walking to look at a slot not yet seen
        Assert.Contains("stands by the schedule and is not yet seen", tour.PurposeNote, StringComparison.Ordinal);

        // A seen-empty slot is due at the latest at the moment seen plus its generator's delay and one tick (the phase unknown).
        var g = ground.Respawn.Generators.Single(x => x.Guid == tour.Schedule.Slot(inAOrB[0]).Generator);
        Assert.Equal(RespawnSchedule.SeenEmptySettleSeconds + 0.1 + g.DelaySeconds + g.TickSeconds, tour.Schedule.DueAt(inAOrB[0]), 6);
    }

    [Theory]
    [MemberData(nameof(Dungeons))]
    public void With_nothing_standing_the_tour_walks_once_to_the_earliest_due_slot_then_holds_there_naming_the_slot_and_its_due_time(string key)
    {
        var ground = GroundCatalog.Get(key);
        var (a, b) = Neighbours(ground);
        var log = new RecordingLogSink();
        var tour = new GroundTour(ground, StandIn(ground), log);
        var first = SlotsIn(tour.Schedule, a)[0];
        tour.Killed(tour.Schedule.Slot(first).Wcid, a, 0); // A's slot dies first: it is the earliest due
        for (var i = 0; i < tour.Schedule.Count; i++)
        {
            if (tour.Schedule.IsHunted(i) && tour.Schedule.IsStanding(i))
            {
                tour.Killed(tour.Schedule.Slot(i).Wcid, tour.Schedule.Slot(i).Cell, 1);
            }
        }

        Assert.Equal(0, tour.Schedule.StandingHunted);
        var due = tour.Schedule.DueAt(first);
        Assert.Equal(a, tour.Next(b, 10)); // nothing seen, nothing presumed, nothing due within the walk and the margin: one walk to where the earliest due slot is in view
        Assert.Equal(TourPurpose.Due, tour.Purpose);
        tour.LegArrived(a, 12);

        Assert.Equal(a, tour.Next(a, 13)); // there: the hold, no leg
        Assert.Equal(TourPurpose.Hold, tour.Purpose);
        var named = string.Create(CultureInfo.InvariantCulture, $"slot {first} (wcid {tour.Schedule.Slot(first).Wcid} in 0x{a:X8}) is due at {due:0} s");
        Assert.Contains(named, tour.PurposeNote, StringComparison.Ordinal);
        Assert.Contains(log.Lines, l => l.Contains($"tour: holding in 0x{a:X8}", StringComparison.Ordinal) && l.Contains(named, StringComparison.Ordinal));
        Assert.Equal(a, tour.Next(a, 14));
        Assert.Equal(1, tour.Holds); // one hold, logged once
        Assert.Equal(1, log.Lines.Count(l => l.Contains("tour: holding in", StringComparison.Ordinal)));
        Assert.Equal(7, tour.HoldSeconds(20), 6);
        Assert.Equal((1, 0, 0), (tour.WalksBy(TourPurpose.Due), tour.WalksBy(TourPurpose.Presumed), tour.WalksBy(TourPurpose.Seen)));
    }

    [Theory]
    [MemberData(nameof(Dungeons))]
    public void A_seen_target_outranks_a_presumed_slot(string key)
    {
        var ground = GroundCatalog.Get(key);
        var (a, b) = Neighbours(ground);
        var far = FarthestHuntedCell(ground, a);
        var tour = new GroundTour(ground, StandIn(ground), NullLogSink.Instance);
        var wcid = ground.Respawn.Slots.First(s => s.Cell == far && ground.RoleOf(s.Wcid) == GroundRole.Target).Wcid;
        var table = Table(Creature(0x80000F01, wcid, far, "Tusker"));
        tour.Look(a, table, 0);

        Assert.Equal(far, tour.Next(a, 1)); // B is presumed and near, the living target far: the target first
        Assert.Equal(TourPurpose.Seen, tour.Purpose);
        Assert.Contains("a living Tusker 0x80000F01 in 0x", tour.PurposeNote, StringComparison.Ordinal);
        Assert.NotEqual(b, far);
    }

    [Fact]
    public void A_create_after_seen_empty_makes_the_slot_standing_and_its_looked_at_cell_offerable_again()
    {
        var log = new RecordingLogSink();
        var tour = new GroundTour(Build(), Metres, log);
        Assert.Equal(T(1), tour.Next(Arrival, 0));
        tour.LegArrived(T(1), 2);
        tour.Look(T(1), new WorldObjectTable(), 2);
        tour.Look(T(1), new WorldObjectTable(), 5);
        Assert.True(tour.Schedule.IsSeenEmpty(0));
        Assert.Equal(T(2), tour.Next(T(1), 6));
        tour.LegArrived(T(2), 7);

        // A Slave is created in T(1): its slot stands again at once (it died before it was seen, at an unknown time), and T(1) is offered again.
        tour.Created(Slave, T(1), 30, 0x80000F02);
        Assert.True(tour.Schedule.IsStanding(0));
        Assert.False(tour.Schedule.IsSeenEmpty(0));
        Assert.Equal(0, tour.Schedule.SlotOfGuid(0x80000F02));
        Assert.Contains(log.Lines, l => l.Contains("stands again at 30.0 s", StringComparison.Ordinal) && l.Contains("after it was seen empty", StringComparison.Ordinal));
        Assert.Equal(T(1), tour.Next(T(2), 31));
        Assert.Equal(TourPurpose.Presumed, tour.Purpose);
    }

    [Fact]
    public void The_loop_guard_holds_on_a_repeating_cycle_with_no_kill_and_no_create_and_counts_it()
    {
        // A tour fed no arrival (a walk that does not report its legs) could still cycle: A, B, A, B is caught, logged once by name, and held.
        var log = new RecordingLogSink();
        var tour = new GroundTour(Build(), Metres, log);
        Assert.Equal(T(2), tour.Next(T(1), 0));
        Assert.Equal(T(1), tour.Next(T(2), 1));
        Assert.Equal(T(2), tour.Next(T(1), 2));
        Assert.Equal(T(2), tour.Next(T(2), 3)); // the fourth would close T(2), T(1), T(2), T(1): a hold where it stands
        Assert.Equal(TourPurpose.Hold, tour.Purpose);
        Assert.Equal(1, tour.WalkLoops);
        Assert.Single(log.Lines, l => l.Contains("tour: a walk loop: 0x0A0A0102, 0x0A0A0101, 0x0A0A0102, 0x0A0A0101 offered again with no kill and no create between them", StringComparison.Ordinal));
        Assert.Equal(T(2), tour.Next(T(2), 4)); // still held
        Assert.Equal(1, tour.WalkLoops);

        tour.Killed(Slave, T(2), 5); // a kill ends the hold
        Assert.NotEqual(TourPurpose.Hold, PurposeOf(tour, T(2), 6));
        Assert.Matches(@"^walks_by_purpose=seen:0,presumed:\d+,due:0,return:0 holds=1 hold_s=2 slots_seen_empty=0 walk_loops=1$", tour.PurposeFields(6));
    }

    [Fact]
    public void The_speed_sample_is_the_runners_walking_time_not_the_offer_to_the_arrival()
    {
        var tour = new GroundTour(Build(), Metres, NullLogSink.Instance);
        Assert.Equal(L(3), tour.Next(L(5), 0)); // 20 m
        tour.LegWalked(L(3), 30, 4); // the leg took 30 s from the offer, 4 s of it walking (a fight or a server MoveTo inside it)
        Assert.Equal(5, tour.RunMetresPerSecond, 6);

        var old = new GroundTour(Build(), Metres, NullLogSink.Instance);
        Assert.Equal(L(3), old.Next(L(5), 0));
        old.LegArrived(L(3), 30); // a walk that gives no walking time: the offer to the arrival, named in the log
        Assert.Equal(20.0 / 30, old.RunMetresPerSecond, 6);
    }

    [Fact]
    public void The_runners_legs_report_each_leg_alone_and_time_the_walk_without_a_pause_inside_it()
    {
        var rig = new IndoorRig(IndoorRoomKit.Ring(), 0f, IndoorRoomKit.RingSouthWest, new Vector2(93f, 99f), heading: 0f);
        var tour = new TimedTour(IndoorRoomKit.RingNorthEast, IndoorRoomKit.RingSouthEast);
        var log = new RecordingLogSink();
        var legs = new RunnerLegs(DenGround.Profile with { Walk = new WalkModel(0f, 1f, 0.0, 600) }, tour, rig.Graph, log, RingCentre);

        Assert.True(legs.Begin(rig.Runner, rig.Indoor.Position, rig.Body.Now, rig.Body.Now, out _), legs.Failure);
        var start = rig.Body.Now;
        var status = FightWalkStatus.Walking;
        var paused = 0;
        for (var i = 0; i < 6000 && status == FightWalkStatus.Walking; i++)
        {
            if (i == 20)
            {
                rig.Runner.Pause(); // a pause inside the leg (the cast's turn, a station's wait): 60 frames, 2 s
            }

            if (i is >= 20 and < 80)
            {
                paused++;
            }

            if (i == 80)
            {
                rig.Runner.Resume(rig.Body.Now);
            }

            status = Frame(rig, legs);
        }

        Assert.Equal(FightWalkStatus.Arrived, status);
        var elapsed = rig.Body.Now - start;
        var walked = Assert.Single(tour.Walked);
        Assert.True(walked <= elapsed - (paused * S5Rig.Dt) + (2 * S5Rig.Dt), $"walk_s {walked:0.00} over {elapsed:0.00} s with {paused * S5Rig.Dt:0.00} s paused");
        Assert.True(walked > 0.5, $"walk_s {walked:0.00}");

        Assert.True(legs.Begin(rig.Runner, rig.Indoor.Position, rig.Body.Now, rig.Body.Now, out _), legs.Failure);
        status = FightWalkStatus.Walking;
        for (var i = 0; i < 6000 && status == FightWalkStatus.Walking; i++)
        {
            status = Frame(rig, legs);
        }

        Assert.Equal(FightWalkStatus.Arrived, status);
        var lines = log.Lines.Where(l => l.Contains(" arrived at 0x", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, lines.Count);
        double Field(string line, string name) => double.Parse(Regex.Match(line, $@" {name}=([0-9.]+)").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.Equal(rig.Runner.RouteMetres, Field(lines[0], "route_m") + Field(lines[1], "route_m"), 0.11); // each leg's own metres: the two add up to the runner's life
        Assert.Equal(rig.Runner.Plans, Field(lines[0], "plans") + Field(lines[1], "plans"));
        Assert.Equal(rig.Runner.Turns, Field(lines[0], "turns") + Field(lines[1], "turns"));
        Assert.True(Field(lines[1], "route_m") < rig.Runner.RouteMetres - 1, lines[1]);
        Assert.Contains(" walk_s=", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_hold_makes_no_leg_and_rechecks_after_a_moment()
    {
        var rig = new IndoorRig(IndoorRoomKit.Ring(), 0f, IndoorRoomKit.RingSouthWest, new Vector2(93f, 99f), heading: 0f);
        var tour = new TimedTour(IndoorRoomKit.RingSouthWest) { HoldAll = true };
        var legs = new RunnerLegs(DenGround.Profile, tour, rig.Graph, NullLogSink.Instance, RingCentre);
        Assert.True(legs.Begin(rig.Runner, rig.Indoor.Position, rig.Body.Now, 100, out var description), legs.Failure);
        Assert.True(legs.Holding);
        Assert.StartsWith("hold in 0x", description, StringComparison.Ordinal);
        Assert.Equal(0, legs.Legs);
        Assert.Equal(FightWalkStatus.Walking, legs.Step(rig.Indoor.Position, rig.Body.Now, 100.5));
        Assert.Equal(FightWalkStatus.Arrived, legs.Step(rig.Indoor.Position, rig.Body.Now, 100 + RunnerLegs.HoldRecheckSeconds));
        Assert.Equal(NavRunState.Idle, rig.Runner.State); // the runner was never started
    }

    /// <summary>A living creature of weenie class <paramref name="wcid"/> standing in <paramref name="cell"/>.</summary>
    internal static WorldObject Creature(uint guid, uint wcid, uint cell, string name = "creature")
    {
        var weenie = new WeenieDesc { Header = WeenieHeaderFlags.None, Name = name, WeenieClassId = wcid, IconId = 0x06000001, ItemType = 0x10, ObjectDescriptionFlags = 0x10 };
        var physics = new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.None, Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1) };
        return new WorldObject(new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isSelf: false, createdAt: 0)
        {
            Position = new CellFrame(cell, new AcFrame(Vector3.Zero, Quaternion.Identity)),
        };
    }

    /// <summary>An object table holding <paramref name="objects"/>.</summary>
    internal static WorldObjectTable Table(params WorldObject[] objects)
    {
        var table = new WorldObjectTable();
        foreach (var o in objects)
        {
            table.Set(o);
        }

        return table;
    }

    /// <summary>The walk stand-in for a profile (these tests read no dats): 100 m a storey plus half a metre a cell number.</summary>
    private static Func<uint, uint, double> StandIn(HuntingGround ground) =>
        (a, b) => (100.0 * Math.Abs(ground.StoreyOf(a) - ground.StoreyOf(b))) + (Math.Abs((int)(a & 0xFFFF) - (int)(b & 0xFFFF)) * 0.5);

    /// <summary>Two cells of hunted slots on the arrival's storey, nearest each other by cell number (the ping-pong pair).</summary>
    private static (uint A, uint B) Neighbours(HuntingGround ground)
    {
        var storey = ground.StoreyOf(ground.Arrival.Cell);
        var cells = ground.Respawn.Slots.Where(s => ground.IsTarget(s.Wcid, s.Cell) && ground.StoreyOf(s.Cell) == storey).Select(s => s.Cell).Distinct().Order().ToList();
        Assert.True(cells.Count >= 2, $"{ground.Key}: two hunted cells on the arrival's storey");
        var best = Enumerable.Range(0, cells.Count - 1).MinBy(i => cells[i + 1] - cells[i]);
        return (cells[best], cells[best + 1]);
    }

    private static uint FarthestHuntedCell(HuntingGround ground, uint from) =>
        ground.Respawn.Slots.Where(s => ground.IsTarget(s.Wcid, s.Cell)).Select(s => s.Cell).MaxBy(c => StandIn(ground)(from, c));

    private static List<int> SlotsIn(RespawnSchedule schedule, params uint[] cells) =>
        [.. Enumerable.Range(0, schedule.Count).Where(i => schedule.IsHunted(i) && cells.Contains(schedule.Slot(i).Cell))];

    private static Client.Session.Grounds.TourPurpose PurposeOf(GroundTour tour, uint from, double now)
    {
        tour.Next(from, now);
        return tour.Purpose;
    }

    /// <summary>The middle of a ring room (the kit's rooms have no floor polygons, so the room graph has no frame for them).</summary>
    private static CellFrame? RingCentre(uint cell)
    {
        var at = cell switch
        {
            IndoorRoomKit.RingSouthWest => new Vector3(96f, 96f, 0f),
            IndoorRoomKit.RingSouthEast => new Vector3(106f, 96f, 0f),
            IndoorRoomKit.RingNorthWest => new Vector3(96f, 106f, 0f),
            IndoorRoomKit.RingNorthEast => new Vector3(106f, 106f, 0f),
            _ => (Vector3?)null,
        };
        return at is { } o ? new CellFrame(cell, new AcFrame(o, Quaternion.Identity)) : null;
    }

    private static FightWalkStatus Frame(IndoorRig rig, RunnerLegs legs)
    {
        rig.Body.Advance(S5Rig.Dt);
        rig.Indoor.Sync();
        var status = legs.Step(rig.Indoor.Position, rig.Body.Now, rig.Body.Now);
        rig.H.Tick(S5Rig.Dt);
        return status;
    }

    /// <summary>A tour over a list of cells that records the walking seconds each arrived leg reports; <see cref="HoldAll"/> makes every offer a hold.</summary>
    private sealed class TimedTour(params uint[] cells) : IGroundTour
    {
        private int _next;

        public List<double> Walked { get; } = [];

        public bool HoldAll { get; init; }

        public int Waypoints => cells.Length;

        public Client.Session.Grounds.TourPurpose Purpose { get; private set; }

        public string PurposeNote => "the test's list";

        public uint? Next(uint from, double now)
        {
            if (HoldAll)
            {
                Purpose = TourPurpose.Hold;
                return from;
            }

            Purpose = TourPurpose.Presumed;
            return cells[_next++ % cells.Length];
        }

        public void LegWalked(uint cell, double now, double walkSeconds) => Walked.Add(walkSeconds);
    }
}

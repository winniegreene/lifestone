using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Net.Messages;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// M6 14 pits (<c>TODO(m6-abode-pits)</c>, the owner's go of 2026-11-13): the Tusker Abode's and the Tusker Holding's pit 0x...02A9 (one 712-cell layout) crossed by a running jump, on the dats with the client's own physics
/// (an <see cref="OfflineWalker"/>: retail's leave-ground velocity, jump speed, gravity and edge slide). The profile's jump is the dats' pit; the climber's buffed running jump lands on the far floor both ways and her bare one falls
/// in; the need is the shortest flight that lands (the runner's own release, held at the brink); the room graph takes the jump only within the body's flight; a runner walks from the arrival over the pit to a far-half slot and back;
/// a jump that falls short ends the run by name; the far half's slots are reached and the drafted tour holds them. Exhaustive tier, skipped without the dats.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class PitJumpDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const double Fps = 60.0;

    /// <summary>Qwinnie at level 47 with the buff bot's level VII set: Run 103 and Jump 178 (<see cref="Her_buffs_reach_the_weenie_s_run_and_jump"/>).</summary>
    private const int BuffedRun = 103;

    private const int BuffedJump = 178;

    /// <summary>The same character bare: Run 23 and Jump 98.</summary>
    private const int BareRun = 23;

    private const int BareJump = 98;

    public static TheoryData<string> Grounds() => new() { "tusker-abode", "tusker-holding" };

    public static TheoryData<string, bool> GroundsBothWays() => new() { { "tusker-abode", true }, { "tusker-abode", false }, { "tusker-holding", true }, { "tusker-holding", false } };

    private LandblockCellSet CellsOf(HuntingGround ground)
    {
        var landblock = Assert.Single(ground.Landblocks);
        return LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), new LandblockCoord((byte)(landblock >> 8), (byte)(landblock & 0xFF)));
    }

    /// <summary>The ground's one jump, eastward (from the arrival's half, 0x...02B9) or westward.</summary>
    private static DirectedJump JumpOf(HuntingGround ground, bool westward)
    {
        var crossing = Assert.Single(ground.Jumps);
        return crossing.Directions.Single(d => (d.From.Cell & 0xFFFF) == (westward ? 0x2B9u : 0x2A0u));
    }

    /// <summary>
    /// The profile's jump is the dats' pit: the pit cell has no floor polygon; the near rooms' doorways onto it face each other across it at the brinks (sill z 0), so the gap brink to brink is 10 m; the run-ups, brinks and landings
    /// stand on their rooms' floors; the doorway onto the pit is still no walk, and the far half is reached only with the jump.
    /// </summary>
    [RequiresDatsTheory]
    [MemberData(nameof(Grounds))]
    public void The_profile_s_jump_is_the_dats_pit(string key)
    {
        var ground = GroundCatalog.Get(key);
        var all = CellsOf(ground);
        var crossing = Assert.Single(ground.Jumps);
        Assert.Empty(all.GetStructure(all.GetCell(crossing.PitCell)!).Floors);
        var planner = new CellRoutePlanner(all, ground.Walk.DoorwayLean);
        var doorways = new List<Doorway>();
        foreach (var side in new[] { crossing.A, crossing.B })
        {
            var cell = all.GetCell(side.Cell)!;
            var portal = cell.Portals.Single(p => p.OtherCellId == crossing.PitCell);
            Assert.False(planner.IsWalkable(cell, portal)); // a walk stops at the brink
            var doorway = planner.DoorwayOf(cell, portal)!.Value;
            doorways.Add(doorway);
            Assert.Equal(side.Brink.Z, doorway.Sill, 2);
            Assert.True(Vector2.Distance(new Vector2(side.Brink.X, side.Brink.Y), new Vector2(doorway.Centre.X, doorway.Centre.Y)) < 0.01f, $"{key}: the brink of 0x{side.Cell:X8} is its doorway's centre");
            foreach (var (name, point) in new[] { ("run-up", side.RunUp), ("landing", side.Landing), ("brink", side.Brink - (new Vector3(doorway.Outward.X, doorway.Outward.Y, 0f) * 0.25f)) })
            {
                Assert.True(planner.RoomFloorUnder(side.Cell, point + new Vector3(0f, 0f, 0.5f)) is { } z && MathF.Abs(z - point.Z) < 0.05f, $"{key}: the {name} of 0x{side.Cell:X8} stands on its floor");
            }
        }

        Assert.True(Vector3.Dot(doorways[0].Outward, doorways[1].Outward) < -0.99f, "the two doorways face each other across the pit");
        var gap = MathF.Abs(Vector3.Dot(doorways[1].Centre - doorways[0].Centre, doorways[0].Outward));
        Assert.Equal(crossing.GapMetres, gap, 2);
        Assert.Equal(10f, gap, 2);

        var walls = new RoomGraph(GroundRooms.FloorOf(all, ground), ground.Walk.DoorwayLean);
        Assert.Null(walls.FindPath(crossing.A.Cell, crossing.B.Cell)); // without the jump no walk joins the halves
        var withJump = GroundRooms.GraphOf(all, ground);
        Assert.Equal(new[] { crossing.A.Cell, crossing.B.Cell }, withJump.FindPath(crossing.A.Cell, crossing.B.Cell)!);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{key}: {crossing.Name}: gap {gap:0.00} m, need {crossing.NeedMetres:0.00} m"));
    }

    /// <summary>
    /// Qwinnie at level 47 (the shard's rows, read-only, 2026-11-13): Strength 75, Quickness 15, Coordination 105, Run and Jump trained with 8 ranks each, and the buff bot's level VII set on her (Might of the 5 Mules, Boon of
    /// Refinement, Ogfoot: +40 Strength, Coordination and Quickness; Saladur's Boon and Jahannan's Boon: +40 Run and Jump; their stat_Mod_Type 0x9001 and 0x9010). The weenie counts them now (<c>TODO(state-enchantments)</c> closed
    /// for Run and Jump): Run 103 and Jump 178, a full-bar running jump of 11.54 m; bare, Run 23 and Jump 98, 5.88 m.
    /// </summary>
    [RequiresDatsFact]
    public void Her_buffs_reach_the_weenie_s_run_and_jump()
    {
        var q = Qwinnie(buffed: false);
        var weenie = QualitiesWeenie.FromSkillTable(q, SkillTableData.Load(fixture.Dats));
        Assert.Equal((BareRun, BareJump), (weenie.RunSkill, weenie.JumpSkill));
        var bare = JumpReach.FlightMetres(weenie)!.Value;

        q = Qwinnie(buffed: true);
        weenie = QualitiesWeenie.FromSkillTable(q, SkillTableData.Load(fixture.Dats));
        Assert.Equal((BuffedRun, BuffedJump), (weenie.RunSkill, weenie.JumpSkill));
        var buffed = JumpReach.FlightMetres(weenie)!.Value;
        Assert.True(weenie.TryGetRunRate(out var rate));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"bare: Run {BareRun} Jump {BareJump} flight {bare:0.00} m; buffed: Run {BuffedRun} Jump {BuffedJump} run rate {rate:0.0000} ({rate * 4f:0.00} m/s) flight {buffed:0.00} m"));
        Assert.Equal(5.88f, bare, 2);
        Assert.Equal(11.54f, buffed, 2);
        Assert.Equal(JumpReach.FlightMetres(new OfflinePlayerWeenie(BuffedRun, BuffedJump))!.Value, buffed, 4);
    }

    /// <summary>
    /// The climber's buffed running jump, the runner's own procedure (line up at the run-up, run with the bar filling, held at the brink by the edge slide, let go with a full bar), lands on the far floor both ways on both grounds,
    /// with no fall: the jump is logged with its take-off and landing.
    /// </summary>
    [RequiresDatsTheory]
    [MemberData(nameof(GroundsBothWays))]
    public void The_buffed_climber_s_running_jump_lands_on_the_far_floor(string key, bool westward)
    {
        var ground = GroundCatalog.Get(key);
        var jump = JumpOf(ground, westward);
        var result = JumpAcross(ground, jump, BuffedRun, BuffedJump, claimedReach: null);
        output.WriteLine(result.Line);
        Assert.Equal(NavRunState.Arrived, result.State);
        Assert.Equal((1, 1), (result.Taken, result.Landed));
        Assert.Equal(jump.To.Cell, result.Cell);
        Assert.InRange(result.LowestZ, -0.1f, 0.1f);
        Assert.Contains(result.Log, l => l.Contains("jump: take-off", StringComparison.Ordinal) && l.Contains(jump.Name, StringComparison.Ordinal));
        Assert.Contains(result.Log, l => l.Contains("jump: landed on the far floor", StringComparison.Ordinal) && l.Contains("outcome=landed", StringComparison.Ordinal));
        Assert.True(result.Flight >= jump.Crossing.GapMetres, $"flight {result.Flight:0.00} m");
    }

    /// <summary>
    /// The need (the profile's <c>need_m</c>) against the physics: the runner's jump with Run 103 and a range of Jump skills (the body claiming a long flight so the runner jumps whatever the need) lands on the far floor from every
    /// level-ground flight at or over the need, and the shorter flights that catch the brink or fall in end the run by name (jump-short, jump-fell). The table is printed for the profile's source.
    /// </summary>
    [RequiresDatsFact]
    public void The_need_is_a_flight_that_lands_and_shorter_jumps_fail_by_name()
    {
        var ground = GroundCatalog.Get("tusker-abode");
        var jump = JumpOf(ground, westward: true);
        float? shortestLanded = null;
        float? longestFailed = null;
        foreach (var skill in new[] { 98, 120, 130, 135, 140, 145, 150, 160, 178 })
        {
            var flight = JumpReach.FlightMetres(new OfflinePlayerWeenie(BuffedRun, skill))!.Value;
            var result = JumpAcross(ground, jump, BuffedRun, skill, claimedReach: 20f);
            var outcome = result.State == NavRunState.Arrived ? "landed" : result.Why!.Split(':')[0];
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Jump {skill}: level-ground flight {flight:0.00} m -> {outcome}, body 0x{result.Cell:X8} z {result.Z:0.00}, measured flight {result.Flight:0.00} m"));
            if (result.State == NavRunState.Arrived)
            {
                shortestLanded = MathF.Min(shortestLanded ?? float.MaxValue, flight);
                Assert.Equal(jump.To.Cell, result.Cell);
            }
            else
            {
                longestFailed = MathF.Max(longestFailed ?? float.MinValue, flight);
                Assert.Equal(NavRunState.Failed, result.State);
                Assert.Matches("^(jump-short|jump-fell):", result.Why!);
                Assert.Contains(result.Log, l => l.Contains("jump: jump-", StringComparison.Ordinal));
            }

            if (flight >= jump.NeedMetres)
            {
                Assert.Equal(NavRunState.Arrived, result.State);
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"shortest landed {shortestLanded:0.00} m, longest failed {longestFailed:0.00} m, need {jump.NeedMetres:0.00} m"));
        Assert.True(longestFailed < jump.NeedMetres && shortestLanded <= jump.NeedMetres);
    }

    /// <summary>
    /// A jump that falls short is a named outcome with its place: the bare climber (Run 23, Jump 98) made to jump (a body claiming a long flight) hits the far wall and falls to the pit's floor beside the bridge, z -18, in
    /// 0x...0110: the run fails <c>jump-fell</c>, names the cell and the height and the way out (Lifestone Recall: the pit's floor is in no storey), and the jump's lines are logged.
    /// </summary>
    [RequiresDatsTheory]
    [MemberData(nameof(Grounds))]
    public void A_short_jump_falls_into_the_pit_and_ends_the_run_by_name(string key)
    {
        var ground = GroundCatalog.Get(key);
        var jump = JumpOf(ground, westward: true);
        var result = JumpAcross(ground, jump, BareRun, BareJump, claimedReach: 20f);
        output.WriteLine(result.Line);
        Assert.Equal(NavRunState.Failed, result.State);
        Assert.StartsWith("jump-fell:", result.Why, StringComparison.Ordinal);
        Assert.Contains("Lifestone Recall", result.Why, StringComparison.Ordinal);
        Assert.InRange(result.Z, -18.1f, -17.9f);
        Assert.Equal(((uint)ground.Landblocks[0] << 16) | 0x110, result.Cell);
        Assert.False(ground.IsWalkable(result.Cell));
        Assert.Equal((1, 0), (result.Taken, result.Landed));
        Assert.Contains(result.Log, l => l.Contains("jump: jump-fell", StringComparison.Ordinal) && l.Contains("take_off=", StringComparison.Ordinal) && l.Contains("landing=", StringComparison.Ordinal));
    }

    /// <summary>
    /// M6 14 airborne: the runner says its jump is in flight (<see cref="NavRunner.JumpInFlight"/>, what the fight holds every walk-cancelling rule on) from the take-off until its landing check ran: every frame the released body is off
    /// the ground is a frame in flight, the flight lasts over a second at 60 frames a second, and it holds no more once the run has ended, landed on the far floor or fallen into the pit.
    /// </summary>
    [RequiresDatsTheory]
    [MemberData(nameof(Grounds))]
    public void The_jump_is_in_flight_from_the_take_off_to_its_landing_check_and_not_after(string key)
    {
        var ground = GroundCatalog.Get(key);
        var jump = JumpOf(ground, westward: true);
        var landed = JumpAcross(ground, jump, BuffedRun, BuffedJump, claimedReach: null);
        output.WriteLine($"landed: {landed.InFlightFrames} frames in flight, {landed.InFlightOffGround} of them off the ground; {landed.Line}");
        Assert.Equal(NavRunState.Arrived, landed.State);
        Assert.True(landed.InFlightFrames >= 60, $"{landed.InFlightFrames} frames in flight");
        Assert.True(landed.InFlightOffGround * 2 >= landed.InFlightFrames, $"{landed.InFlightOffGround} of {landed.InFlightFrames} frames off the ground");
        Assert.Equal(0, landed.OffGroundOutOfFlight);
        Assert.False(landed.InFlightAtEnd);

        var fell = JumpAcross(ground, jump, BareRun, BareJump, claimedReach: 20f);
        output.WriteLine($"fell: {fell.InFlightFrames} frames in flight; {fell.Line}");
        Assert.StartsWith("jump-fell:", fell.Why, StringComparison.Ordinal);
        Assert.True(fell.InFlightFrames > 0);
        Assert.Equal(0, fell.OffGroundOutOfFlight);
        Assert.False(fell.InFlightAtEnd);
    }

    /// <summary>
    /// M6 14 pit-hold (live 2026-11-13 14:53:47 UTC: the fight's sight rule dropped the walk 0.8 s into the run for the brink, and the body stopped on the brink in the pit's cell): the run is committed from the charge
    /// (<see cref="NavRunner.JumpCommitted"/>, which the fight walk reports as its jump under way) until the landing check, the run on the near floor first and the flight after; not before the charge, and not once the run ended.
    /// </summary>
    [RequiresDatsTheory]
    [MemberData(nameof(Grounds))]
    public void The_run_for_the_brink_is_committed_from_the_charge_to_the_landing_check(string key)
    {
        var ground = GroundCatalog.Get(key);
        var jump = JumpOf(ground, westward: true);
        var landed = JumpAcross(ground, jump, BuffedRun, BuffedJump, claimedReach: null);
        output.WriteLine($"landed: {landed.RunFrames} frames committed on the ground before the flight, {landed.InFlightFrames} in flight; {landed.Line}");
        Assert.Equal(NavRunState.Arrived, landed.State);
        Assert.True(landed.RunFrames >= 30, $"{landed.RunFrames} frames of the run committed"); // the bar fills in a second
        Assert.True(landed.InFlightFrames >= 60, $"{landed.InFlightFrames} frames in flight");
        Assert.False(landed.CommittedAtEnd);

        var refused = JumpAcross(ground, jump, BuffedRun, BuffedJump, claimedReach: 20f, emptyBar: true);
        Assert.StartsWith("jump-bar:", refused.Why, StringComparison.Ordinal);
        Assert.True(refused.RunFrames > 0);
        Assert.False(refused.CommittedAtEnd); // a refused charge ends the commitment by name
    }

    /// <summary>
    /// M6 14 pit-hold, the live case (2026-11-13 14:53:51 to 15:04:39 UTC, 0x5C4502A9 [125.1 -60.2 0.0], 438 holds on "its cell is not offerable now"): a body on the pit's west lip, in the floorless pit cell, holds nowhere and
    /// asks the tour nothing there: the fight walk steps back onto the near room's floor (its plan starts from the room whose brink the body stands at), and from there the tour's walk goes on to a walkable waypoint. A body below the
    /// lip (on the pit's floor) ends the walk by name, with no hold.
    /// </summary>
    [RequiresDatsTheory]
    [MemberData(nameof(Grounds))]
    public void On_the_pit_s_lip_the_walk_steps_back_onto_the_near_floor_and_holds_nowhere(string key)
    {
        var ground = GroundCatalog.Get(key);
        var pit = Assert.Single(ground.Jumps);
        var west = JumpOf(ground, westward: false).From; // 0x...02A0, the brink at x 125
        var lip = new CellFrame(pit.PitCell, new AcFrame(new Vector3(125.15f, -60.2f, 0.005f), Quaternion.Identity));
        var (world, walker) = BodyIn(ground, lip, BuffedRun, BuffedJump);
        output.WriteLine($"placed: {walker.Position}");
        Assert.Equal(pit.PitCell, walker.Position.CellId); // the edge slide's place: in the pit's cell, on the lip
        Assert.InRange(walker.Position.Origin.Z, -0.1f, 0.1f);

        var graph = GroundRooms.GraphOf(CellsOf(ground), ground);
        var log = new RecordingLogSink();
        var tour = GroundTour.For(ground, graph, log);
        var h = new ActionHarness(offline: true);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new RoomPathSource(graph), confirmer, null, new NavRunnerOptions { Name = "ground-walk" }, log);
        var legs = new RunnerLegs(ground, tour, graph, log);
        var now = 0.0;

        Assert.True(legs.Begin(runner, walker.Position, now, now, out var description), legs.Failure);
        output.WriteLine(description);
        Assert.False(legs.Holding);
        Assert.StartsWith("the step back off the pit's lip: ", description, StringComparison.Ordinal);
        Assert.Equal(west.Cell, legs.GoalCell);
        Assert.Contains(log.Lines, l => l.Contains($"walk: the player stands on the lip of {pit.Name}", StringComparison.Ordinal) && l.Contains("m6-pit-hold", StringComparison.Ordinal));
        var status = Walk(legs, h, walker, ref now, 15);
        output.WriteLine($"step back: {status}, body {walker.Position}");
        Assert.Equal(FightWalkStatus.Arrived, status);
        Assert.Equal(west.Cell, walker.Position.CellId);

        Assert.True(legs.Begin(runner, walker.Position, now, now, out description), legs.Failure);
        output.WriteLine(description);
        Assert.False(legs.Holding);
        Assert.True(ground.IsWalkable(legs.GoalCell), $"the tour's waypoint 0x{legs.GoalCell:X8}");
        Assert.DoesNotContain(log.Lines, l => l.Contains("tour: holding in", StringComparison.Ordinal) || l.Contains("not offerable now", StringComparison.Ordinal));
        legs.Abandon();

        var fallen = new CellFrame((pit.PitCell & 0xFFFF0000u) | 0x110, new AcFrame(new Vector3(131f, -60f, -18f), Quaternion.Identity)); // the pit's floor beside the bridge, where a short jump lands (jump-fell)
        Assert.False(legs.Begin(runner, fallen, now, now, out _));
        Assert.False(legs.Holding);
        Assert.Contains("a cell of no storey", legs.Failure, StringComparison.Ordinal);
        Assert.Contains("no hold is kept there", legs.Failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// M6 14 pit-hold: the tour's walks follow the body's reach (<see cref="GroundTour.RoomMetres"/> over <see cref="RoomGraph.JumpReachMetres"/>): a slot of the far half is a walk while she can make the jump, no walk once she
    /// cannot (a buff run out, a load past about 107%), and a walk again when she can; the walks are not remembered across the change.
    /// </summary>
    [RequiresDatsTheory]
    [MemberData(nameof(Grounds))]
    public void The_tour_s_walks_follow_the_body_s_reach(string key)
    {
        var ground = GroundCatalog.Get(key);
        var graph = GroundRooms.GraphOf(CellsOf(ground), ground);
        var far = JumpOf(ground, westward: true).To.Cell;
        var metres = GroundTour.RoomMetres(graph);
        var before = CellRoutePlanner.PitLog;
        CellRoutePlanner.PitLog = new RecordingLogSink();
        try
        {
            graph.JumpReachMetres = JumpReach.FlightMetres(BuffedRun, BuffedJump, 0.99f);
            var over = metres(ground.Arrival.Cell, far);
            Assert.True(double.IsFinite(over), $"{over} m");
            graph.JumpReachMetres = JumpReach.FlightMetres(BuffedRun, BuffedJump, 1.25f); // 8.79 m: past the shelf the jump is gone
            Assert.True(double.IsPositiveInfinity(metres(ground.Arrival.Cell, far)));
            graph.JumpReachMetres = JumpReach.FlightMetres(new OfflinePlayerWeenie(BuffedRun, BuffedJump));
            Assert.Equal(over, metres(ground.Arrival.Cell, far));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{key}: arrival to 0x{far:X8}: {over:0} m with the jump, none without"));
        }
        finally
        {
            CellRoutePlanner.PitLog = before;
            CellRoutePlanner.ResetPitLog();
        }
    }

    /// <summary>Steps <paramref name="legs"/> on the walker until the leg is no longer walking or <paramref name="seconds"/> pass.</summary>
    private static FightWalkStatus Walk(RunnerLegs legs, ActionHarness h, OfflineWalker walker, ref double now, double seconds)
    {
        var status = FightWalkStatus.Walking;
        var end = now + seconds;
        while (status == FightWalkStatus.Walking && now < end)
        {
            now += 1.0 / Fps;
            walker.Controller.Tick(now);
            status = legs.Step(walker.Position, now, now);
            h.Tick(1.0 / Fps);
        }

        return status;
    }

    /// <summary>Check before the release: a jump bar that never fills (a refused charge) is no jump: the run fails <c>jump-bar</c> and the body stands at the brink, on the floor, with nothing released.</summary>
    [RequiresDatsFact]
    public void A_jump_bar_that_never_fills_is_not_released()
    {
        var ground = GroundCatalog.Get("tusker-abode");
        var jump = JumpOf(ground, westward: true);
        var result = JumpAcross(ground, jump, BuffedRun, BuffedJump, claimedReach: 20f, emptyBar: true);
        output.WriteLine(result.Line);
        Assert.Equal(NavRunState.Failed, result.State);
        Assert.StartsWith("jump-bar:", result.Why, StringComparison.Ordinal);
        Assert.Equal((0, 0), (result.Taken, result.Landed));
        Assert.Contains(result.Cell, new[] { jump.From.Cell, jump.Crossing.PitCell }); // held at the brink by the edge slide: its centre may stand over the pit's plane
        Assert.InRange(result.LowestZ, -0.1f, 0.1f);
    }

    /// <summary>The room graph takes the jump only within the planning body's flight: the bare climber's 5.88 m plans no walk to the far half (and the line says why); her buffed 11.54 m, or no body, plans the jump.</summary>
    [RequiresDatsTheory]
    [MemberData(nameof(Grounds))]
    public void The_room_graph_takes_the_jump_only_within_the_body_s_flight(string key)
    {
        var ground = GroundCatalog.Get(key);
        var graph = GroundRooms.GraphOf(CellsOf(ground), ground);
        var jump = JumpOf(ground, westward: true);
        var log = new RecordingLogSink();
        var before = CellRoutePlanner.PitLog;
        CellRoutePlanner.ResetPitLog();
        CellRoutePlanner.PitLog = log;
        try
        {
            graph.JumpReachMetres = JumpReach.FlightMetres(new OfflinePlayerWeenie(BareRun, BareJump));
            Assert.Null(graph.FindPath(ground.Arrival.Cell, jump.To.Cell));
            Assert.Null(graph.Plan(ground.Arrival.Cell, ground.Arrival.Origin, jump.To.Cell, jump.To.Landing));
            Assert.Contains(log.Lines, l => l.Contains(jump.Name, StringComparison.Ordinal) && l.Contains("needs a flight of 10.75 m", StringComparison.Ordinal) && l.Contains("5.88 m", StringComparison.Ordinal));

            var buffed = new OfflinePlayerWeenie(BuffedRun, BuffedJump);
            graph.JumpReachMetres = JumpReach.FlightMetres(buffed);
            graph.PlateSpeedMetresPerSecond = buffed.TryGetRunRate(out var rate) ? Data.Motion.MotionInterpreter.RunAnimSpeed * rate : null; // M6 14 holding plates: the Holding's row of plates in 0x5B480373 is jumped too
            var route = graph.Plan(ground.Arrival.Cell, ground.Arrival.Origin, jump.To.Cell, jump.To.Landing)!;
            var hop = Assert.Single(route.Jumps, j => !j.Jump.Crossing.Standing);
            Assert.Equal(jump, hop.Jump);
            Assert.Equal(jump.TakeOff, route.Waypoints[hop.TakeOff]);
            Assert.Equal(jump.Landing, route.Waypoints[hop.TakeOff + 1]);
            Assert.Equal(jump.RunUp, route.Waypoints[hop.TakeOff - 1]);
            var path = RoomPathSource.PlanRooms(graph, new CellFrame(ground.Arrival.Cell, new AcFrame(ground.Arrival.Origin, Quaternion.Identity)), NavGoal.ToPoint(new NavPoint(jump.To.Cell & 0xFFFF0000u, jump.To.Landing), jump.To.Cell));
            Assert.Equal(jump, Assert.Single(path.Waypoints, w => w.Jump is { Crossing.Standing: false }).Jump);

            graph.JumpReachMetres = null;
            Assert.NotNull(graph.FindPath(ground.Arrival.Cell, jump.To.Cell));
        }
        finally
        {
            CellRoutePlanner.PitLog = before;
            CellRoutePlanner.ResetPitLog();
        }
    }

    /// <summary>
    /// End to end on the profile's room graph (the fight's own, <see cref="GroundRooms.GraphOf"/>): a body with the climber's buffed skills runs from the arrival over the pit to the far half's farthest hunted slot, and back from
    /// there to the arrival, one jump each way, every waypoint confirmed by the physics but the flight.
    /// </summary>
    [RequiresDatsTheory]
    [MemberData(nameof(Grounds))]
    public void A_runner_walks_from_the_arrival_over_the_pit_to_the_far_half_and_back(string key)
    {
        var ground = GroundCatalog.Get(key);
        var all = CellsOf(ground);
        var graph = GroundRooms.GraphOf(all, ground);
        var jump = JumpOf(ground, westward: true);
        var west = new RoomGraph(GroundRooms.FloorOf(all, ground), ground.Walk.DoorwayLean);
        var farHalf = ground.Respawn.Slots.Where(s => ground.RoleOf(s.Wcid) == GroundRole.Target && ground.IsWalkable(s.Cell) && west.FindPath(jump.To.Cell, s.Cell) is not null).Select(s => s.Cell).Distinct().ToList();
        var far = farHalf.OrderByDescending(c => west.FindPath(jump.To.Cell, c)!.Count).ThenBy(c => c).First();

        var (world, walker) = BodyIn(ground, new CellFrame(ground.Arrival.Cell, new AcFrame(ground.Arrival.Origin, Quaternion.Identity)), BuffedRun, BuffedJump);
        var h = new ActionHarness(offline: true); // one harness and one clock for both runs: the jump bar is timed on the controller's clock
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        var now = 0.0;
        var there = Run(h, graph, world, walker, far, key + "-over", ref now, out var log, out var runner);
        output.WriteLine(there);
        Assert.Contains(" Arrived ", there, StringComparison.Ordinal);
        Assert.Equal(far, walker.Position.CellId);
        Assert.Equal(runner.JumpsTaken, runner.JumpsLanded); // M6 14 holding plates: the Holding's walk also jumps its row of plates in 0x5B480373
        Assert.Single(log, l => l.Contains("jump: take-off", StringComparison.Ordinal) && l.Contains(jump.Crossing.Name + " ", StringComparison.Ordinal));
        Assert.Contains(log, l => l.Contains("jump: take-off", StringComparison.Ordinal));
        Assert.Contains(log, l => l.Contains("replanned", StringComparison.Ordinal) && l.Contains("why=jumped", StringComparison.Ordinal));

        var back = Run(h, graph, world, walker, ground.Arrival.Cell, key + "-back", ref now, out log, out runner);
        output.WriteLine(back);
        foreach (var l in log.Where(l => l.Contains("jump", StringComparison.Ordinal)))
        {
            output.WriteLine("  " + l);
        }

        Assert.Contains(" Arrived ", back, StringComparison.Ordinal);
        Assert.Equal(ground.Arrival.Cell, walker.Position.CellId);
        Assert.Equal(runner.JumpsTaken, runner.JumpsLanded);
        Assert.Single(log, l => l.Contains("jump: take-off", StringComparison.Ordinal) && l.Contains(jump.Crossing.Name + " ", StringComparison.Ordinal));
    }

    /// <summary>
    /// The far half's hunted slots are the profile's now: every target slot of the two halves stands in a walkable cell the arrival reaches over the room graph with the jump, and the tour holds each one's cell; the drafted tour
    /// (by the walk) crosses the pit twice a round, once each way. The lower level's slots stay out (<c>TODO(m6-abode-lower-level)</c>), counted.
    /// </summary>
    [RequiresDatsTheory]
    [InlineData("tusker-abode", 37, 25, 59)]
    [InlineData("tusker-holding", 54, 48, 81)]
    public void The_far_half_s_slots_are_reached_and_the_tour_holds_them(string key, int east, int west, int lower)
    {
        var ground = GroundCatalog.Get(key);
        var all = CellsOf(ground);
        var graph = GroundRooms.GraphOf(all, ground);
        var walls = new RoomGraph(GroundRooms.FloorOf(all, ground), ground.Walk.DoorwayLean);
        var targets = ground.Respawn.Slots.Where(s => ground.RoleOf(s.Wcid) == GroundRole.Target).ToList();
        var hunted = targets.Where(s => ground.IsWalkable(s.Cell)).ToList();
        var eastHalf = hunted.Count(s => walls.FindPath(ground.Arrival.Cell, s.Cell) is not null);
        output.WriteLine($"{key}: {targets.Count} target slots: {eastHalf} east, {hunted.Count - eastHalf} west (by the jump), {targets.Count - hunted.Count} on the lower level or in the pit; the tour {ground.Tour.Count} cells");
        Assert.Equal((east, west), (eastHalf, hunted.Count - eastHalf));
        Assert.Equal(lower, targets.Count(s => !ground.IsWalkable(s.Cell) && s.Cell != Assert.Single(ground.Jumps).PitCell));
        Assert.All(hunted, s => Assert.NotNull(graph.FindPath(ground.Arrival.Cell, s.Cell)));
        Assert.All(hunted.Select(s => s.Cell).Distinct(), c => Assert.Contains(c, ground.Tour));
        Assert.Equal(GroundDraft.FirstTour(all, ground.Storeys, ground.Arrival.Cell, hunted.Select(s => s.Cell), GroundTour.RoomMetres(graph)), ground.Tour);
        var westCells = ground.Tour.Select(c => walls.FindPath(ground.Arrival.Cell, c) is null).ToList();
        var crossings = westCells.Zip(westCells.Skip(1).Append(westCells[0])).Count(p => p.First != p.Second);
        Assert.Equal(2, crossings);
    }

    /// <summary>A walker of the given skills standing at <paramref name="at"/> in the ground's landblock.</summary>
    private (PhysicsWorld World, OfflineWalker Walker) BodyIn(HuntingGround ground, CellFrame at, int run, int jump)
    {
        var landblock = ground.Landblocks[0];
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(fixture.Dats, new PhysicsGeometryLibrary(fixture.Dats)).Build(new LandblockCoord((byte)(landblock >> 8), (byte)(landblock & 0xFF))));
        var walker = OfflineWalker.Create(fixture.Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(run, jump));
        world.UseTime(0.0);
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(at, sliding: true));
        return (world, walker);
    }

    /// <param name="InFlightFrames">M6 14 airborne: the frames <see cref="NavRunner.JumpInFlight"/> held.</param>
    /// <param name="InFlightOffGround">Of those, the frames the body was off the ground.</param>
    /// <param name="OffGroundOutOfFlight">The frames the body was off the ground after the release while <see cref="NavRunner.JumpInFlight"/> did not hold, before the landing check ran.</param>
    /// <param name="InFlightAtEnd">Whether it still held when the run ended.</param>
    /// <param name="RunFrames">M6 14 pit-hold: the frames <see cref="NavRunner.JumpCommitted"/> held before the flight (the run for the brink with the jump key down).</param>
    /// <param name="CommittedAtEnd">Whether <see cref="NavRunner.JumpCommitted"/> still held when the run ended.</param>
    private sealed record JumpResult(NavRunState State, string? Why, uint Cell, float Z, float LowestZ, float Flight, int Taken, int Landed, IReadOnlyList<string> Log, string Line, int InFlightFrames = 0, int InFlightOffGround = 0, int OffGroundOutOfFlight = 0, bool InFlightAtEnd = false, int RunFrames = 0, bool CommittedAtEnd = false);

    /// <summary>A runner over the ground's graph from the jump's run-up to the far room's middle (the body's own flight, or <paramref name="claimedReach"/>: a body that says it flies that far, so the runner jumps).</summary>
    private JumpResult JumpAcross(HuntingGround ground, DirectedJump jump, int run, int skill, float? claimedReach, bool emptyBar = false)
    {
        var graph = GroundRooms.GraphOf(CellsOf(ground), ground);
        var (world, walker) = BodyIn(ground, jump.RunUp, run, skill);
        var h = new ActionHarness(offline: true);
        var log = new RecordingLogSink();
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, null, "pit-jump");
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        INavBody body = new ControllerNavBody(walker.Controller);
        if (claimedReach is { } claimed)
        {
            body = new ClaimedReach(body, claimed, emptyBar);
        }

        var room = graph.RoomFrame(jump.To.Cell)!.Value;
        using var runner = new NavRunner(h.Api.Move, body, new RoomPathSource(graph), confirmer, audit, new NavRunnerOptions { Name = "pit-jump" }, log);
        var now = 0.0;
        runner.Start(NavGoal.ToPoint(new NavPoint(jump.To.Cell & 0xFFFF0000u, room.Origin), jump.To.Cell), now);
        var lowest = float.MaxValue;
        var (inFlight, inFlightOffGround, offGroundOutOfFlight, runFrames) = (0, 0, 0, 0);
        while (!runner.IsDone && now < 60)
        {
            now += 1.0 / Fps;
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / Fps);
            lowest = MathF.Min(lowest, walker.Position.Origin.Z);
            runFrames += runner.JumpCommitted && !runner.JumpInFlight ? 1 : 0;
            if (runner.JumpInFlight)
            {
                inFlight++;
                inFlightOffGround += walker.Controller.OnGround ? 0 : 1;
            }
            else if (runner.JumpsTaken > runner.JumpsLanded && !runner.IsDone && !walker.Controller.OnGround)
            {
                offGroundOutOfFlight++; // released, not landed, in the air: the flight is not held
            }
        }

        var at = walker.Position;
        var takeOff = log.Lines.FirstOrDefault(l => l.Contains("jump: take-off", StringComparison.Ordinal));
        var flight = runner.LastJump is { } last && last.Contains("flight ", StringComparison.Ordinal)
            ? float.Parse(last[(last.IndexOf("flight ", StringComparison.Ordinal) + 7)..].Split(' ')[0], CultureInfo.InvariantCulture)
            : 0f;
        var why = runner.FailureReason is { } reason ? (reason.IndexOf("jump-", StringComparison.Ordinal) is var i and >= 0 ? reason[i..] : reason) : null;
        var line = string.Create(CultureInfo.InvariantCulture, $"{jump.Name} Run {run} Jump {skill}: {runner.State} in {now:0.0} s, body 0x{at.CellId:X8} ({at.Origin.X:0.00}, {at.Origin.Y:0.00}, {at.Origin.Z:0.00}), lowest z {lowest:0.00}; {runner.LastJump}{(why is null ? string.Empty : " -- " + why)}");
        return new JumpResult(runner.State, why, at.CellId, at.Origin.Z, lowest, flight, runner.JumpsTaken, runner.JumpsLanded, log.Lines, line + (takeOff is null ? string.Empty : " | " + takeOff), inFlight, inFlightOffGround, offGroundOutOfFlight, runner.JumpInFlight, runFrames, runner.JumpCommitted);
    }

    /// <summary>
    /// A <see cref="NavRunner"/> over <paramref name="graph"/> from the walker's place to the middle of <paramref name="goalCell"/> (240 s at most), on the harness <paramref name="h"/> bound to the walker and its clock from
    /// <paramref name="now"/> on (one clock for the controller and the keys: the jump bar is timed on it): its line.
    /// </summary>
    private static string Run(ActionHarness h, RoomGraph graph, PhysicsWorld world, OfflineWalker walker, uint goalCell, string name, ref double now, out IReadOnlyList<string> log, out NavRunner runner)
    {
        var sink = new RecordingLogSink();
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, null, name);
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var room = graph.RoomFrame(goalCell)!.Value;
        runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new RoomPathSource(graph), confirmer, audit, new NavRunnerOptions { Name = name }, sink);
        var start = walker.Position.CellId;
        var began = now;
        runner.Start(NavGoal.ToPoint(new NavPoint(goalCell & 0xFFFF0000u, room.Origin), goalCell), now);
        while (!runner.IsDone && now - began < 240)
        {
            now += 1.0 / Fps;
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / Fps);
        }

        log = sink.Lines;
        var line = string.Create(CultureInfo.InvariantCulture, $"{name}: 0x{start:X8} -> 0x{goalCell:X8} {runner.State} in {now - began:0.0} s, {runner.RouteMetres:0.0} m, body in 0x{walker.Position.CellId:X8} {runner.VerdictFields()}; {runner.LastJump}");
        runner.Dispose();
        return line + (runner.FailureReason is { } why ? " -- " + why : string.Empty);
    }

    /// <summary>Qwinnie's qualities at level 47 (the shard's rows), with or without the buff bot's five level VII buffs that reach Run and Jump.</summary>
    private static PlayerQualities Qwinnie(bool buffed)
    {
        var q = new PlayerQualities();
        foreach (var (id, start, ranks) in new (uint, uint, uint)[] { (1, 10, 65), (2, 10, 94), (3, 10, 5), (4, 100, 5), (5, 100, 102), (6, 100, 102) })
        {
            q.Attributes[id] = new AttributeDesc(id, ranks, start, 0);
        }

        q.Skills[QualitiesWeenie.RunSkillId] = new SkillDesc(QualitiesWeenie.RunSkillId, 8, 1, SkillRules.TrainedClass, 0, 0, 0, 0);
        q.Skills[QualitiesWeenie.JumpSkillId] = new SkillDesc(QualitiesWeenie.JumpSkillId, 8, 1, SkillRules.TrainedClass, 0, 0, 0, 0);
        q.IsKnown = true;
        if (buffed)
        {
            // ace_world spell rows: Might of the 5 Mules 2086, Boon of Refinement 2058, Ogfoot 2080 (stat_Mod_Type 36865 = 0x9001, keys 1, 4, 3); Saladur's Boon 2300, Jahannan's Boon 2256 (36880 = 0x9010, keys 24, 22); +40 each.
            q.Enchantments = ImmutableArray.Create(
                Buff(2086, 0x9001, 1),
                Buff(2058, 0x9001, 4),
                Buff(2080, 0x9001, 3),
                Buff(2300, 0x9010, QualitiesWeenie.RunSkillId),
                Buff(2256, 0x9010, QualitiesWeenie.JumpSkillId));
        }

        return q;
    }

    private static EnchantmentDesc Buff(ushort spell, uint type, uint key) =>
        new(spell, 1, spell, 0, 400, 0.0, 3600.0, 0x50000001, 0f, 0f, 0.0, type, key, 40f, 0, EnchantmentBucket.Additive);

    /// <summary>
    /// A body that claims a flight of its own choosing (the runner then jumps whatever its physics can do): the test's way to make a short jump; with <paramref name="emptyBar"/> it also says its jump bar never fills (a refused charge).
    /// </summary>
    private sealed class ClaimedReach(INavBody inner, float reach, bool emptyBar = false) : INavBody
    {
        public double? JumpBarLevel => emptyBar ? 0.0 : inner.JumpBarLevel;

        public CellFrame Position => inner.Position;

        public uint CellId => inner.CellId;

        public bool OnGround => inner.OnGround;

        public bool ServerDriving => inner.ServerDriving;

        public string? ServerMoveToEnd => inner.ServerMoveToEnd;

        public double Now => inner.Now;

        public float? JumpReachMetres => reach;
    }
}

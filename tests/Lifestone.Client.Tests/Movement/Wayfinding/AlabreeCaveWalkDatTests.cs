using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// M6 7 Q (the Blood Shreth kill task's live round, landblock 0x0162): armed, one kill, then every walk refused, "no walkable ground" at (50.00, -12.72, 12.00) in 0x016201C6 and (60.00, -39.16, 12.00) in 0x016201DB, and the fight ended.
/// No seam: the x of 50 and 60 are the corridors' centre lines, and a floor-to-ceiling pillar stands on each. The pillars' collision cylinders (Setups 0x02000208, 0x02000209) run from 1.87 m under the origin, which sits at their middle, 13.88 over the floor's 12.00, so
/// the planner's height rule on the origin left them out, the room graph planned through them, and the pre-check was right to refuse: a body stops at one. The rule now reads the shape's height (<see cref="CellRoutePlanner.InStatic"/>), and a crossing's approach stands too.
/// The same rule closes the Tusker Cave's, the Cavern's and the Habitat's refused legs (<see cref="IndoorRunThroughDatTests"/>).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class AlabreeCaveWalkDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Corridor = 0x016201DB, Corner = 0x016201C6, RockRoom = 0x016201CF, RockDoor = 0x016201CE, Start = 0x016201D2;

    private const int FighterRunSkill = 230;

    private static readonly Vector3 LiveStart = new(60.14f, -9.86f, 12.01f);

    private (PhysicsWorld World, LandblockCellSet Cells, RoomGraph Graph, OfflineWalker Walker, HuntingGround Ground) Rig(MovementAudit? audit = null)
    {
        var dats = fixture.Dats;
        var ground = GroundCatalog.Get("cave-of-alabree");
        var coord = new LandblockCoord(0x01, 0x62);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, new PhysicsGeometryLibrary(dats)).Build(coord));
        var cells = LandblockCellSet.Load(new CellStructureLibrary(dats), coord);
        var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(FighterRunSkill), sink: audit);
        world.UseTime(0.0);
        return (world, cells, GroundRooms.GraphOf(cells, ground), walker, ground);
    }

    /// <summary>The physics' side of the rule: a body running down 0x016201DB's centre line stops at the pillar, whose origin is above its head.</summary>
    [RequiresDatsFact]
    public void A_running_body_stops_at_the_pillar_on_the_corridors_centre_line()
    {
        var (_, _, _, walker, _) = Rig();
        Assert.True(walker.Place(new CellFrame(Corridor, RetailFrames.SetHeading(new AcFrame(new Vector3(60f, -36.5f, 12.05f), Quaternion.Identity), 180f))));
        walker.DoMotion(MotionCommands.RunForward);
        for (var now = 1.0 / 30; now <= 2.5; now += 1.0 / 30)
        {
            walker.Controller.Tick(now);
        }

        output.WriteLine(walker.Position.ToString());
        Assert.InRange(walker.Position.Origin.Y, -39.1f, -38.6f); // the cylinder's 0.5 m and the body's 0.48 m from (60, -40)
    }

    /// <summary>The planner's side: the pillars count although their origins are above a standing human, the corridor beside them does not, and a storey below is not in their way.</summary>
    [RequiresDatsFact]
    public void The_pillars_stand_in_the_way_of_the_plan_by_their_cylinders_height_not_their_origin()
    {
        var (_, cells, graph, _, _) = Rig();
        var planner = graph.Planner;
        var corridor = cells.GetCell(Corridor)!;
        Assert.True(planner.InStatic(corridor, new Vector3(60f, -39.16f, 12f)));
        Assert.False(planner.InStatic(corridor, new Vector3(62f, -40f, 12f)));
        Assert.False(planner.InStatic(corridor, new Vector3(60f, -39.16f, 6f)));
        Assert.True(planner.InStatic(cells.GetCell(Corner)!, new Vector3(50f, -12.72f, 12f)));
    }

    /// <summary>The live round's two refused walks pass the pre-check from where the body stood, and go round the pillars.</summary>
    [RequiresDatsTheory]
    [InlineData(0x016201BEu, 42.5f, -12.5f)]
    [InlineData(0x016201E5u, 57.75f, -62.25f)]
    public void The_live_rounds_refused_walks_pass_the_pre_check(uint goalCell, float x, float y)
    {
        var (world, _, graph, walker, _) = Rig();
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var from = new CellFrame(Start, new AcFrame(LiveStart, Quaternion.Identity));
        var path = new RoomPathSource(graph).Plan(from, NavGoal.ToPoint(new NavPoint(goalCell & 0xFFFF0000u, new Vector3(x, y, 12f)), goalCell), new NavScars());
        Assert.Equal(NavPathStatus.Found, path.Status);
        var status = confirmer.ConfirmAll(path.Waypoints, NavGoal.DefaultStopTolerance);
        output.WriteLine($"{status} {confirmer.Failure} over {path.Waypoints.Count} waypoints, {confirmer.ConfirmedMetres:0.0} m, {confirmer.GroundSamples} samples");
        Assert.Equal(RouteConfirmStatus.Confirmed, status);
    }

    /// <summary>A crossing's approach is part of its walk: the rock 0x020000A5 sits on the centre crossing's approach in 0x016201CF, the samples past it are clear, so the crossing moves along the opening.</summary>
    [RequiresDatsFact]
    public void A_rock_on_a_crossings_approach_moves_the_crossing()
    {
        var (_, cells, graph, _, _) = Rig();
        var room = cells.GetCell(RockRoom)!;
        var offsets = graph.Planner.CrossingOffsets(room, room.Portals.Single(p => p.OtherCellId == RockDoor));
        output.WriteLine(string.Join(", ", offsets.Select(o => o.ToString("0.00", CultureInfo.InvariantCulture))));
        Assert.DoesNotContain(0f, offsets);
    }

    /// <summary>A room's middle is a place to stand: the first of 0x0162017E's four equal floors has its centre in a static's ring, so the room frame is the next floor's.</summary>
    [RequiresDatsFact]
    public void Of_equally_large_floors_a_rooms_frame_is_one_no_static_stands_on()
    {
        var (_, cells, graph, _, _) = Rig();
        var frame = graph.RoomFrame(0x0162017E);
        Assert.NotNull(frame);
        Assert.False(graph.Planner.InStatic(cells.GetCell(0x0162017E)!, frame!.Value.Origin));
        Assert.NotEqual(new Vector3(92.25f, -97.75f, 6f), frame.Value.Origin);
    }

    /// <summary>
    /// A leg begun facing away from its aim beside a pillar (the world walk's W8): the tour's leg to 0x0162015C from where the body comes to rest after the run before, (29.37, -132.89) facing 216 degrees, turned in place to 60 degrees off and
    /// ran, and the turn's curve carried it between the pillar 0x02000209 and a pot beside the plan's line round the pillar (two rungs). The runner now turns on in place while that curve meets a static (<c>NavRunner.CurveClear</c>), or, steering predictively, turns in place before a run whose swept track passes the statics.
    /// </summary>
    [RequiresDatsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_leg_begun_facing_away_beside_the_pillar_turns_before_it_runs(bool predictive)
    {
        var h = new ActionHarness(offline: true);
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, null, "alabree-curve");
        var (world, _, graph, walker, _) = Rig(audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        Assert.True(walker.Place(new CellFrame(0x01620133u, RetailFrames.SetHeading(new AcFrame(new Vector3(29.373169f, -132.8932f, 6.005f), Quaternion.Identity), 216f))));
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var goal = graph.RoomFrame(0x0162015Cu)!.Value;
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new RoomPathSource(graph), confirmer, audit, new NavRunnerOptions { Name = "alabree-curve", PredictiveSteering = predictive });
        var now = 0.0;
        runner.Start(NavGoal.ToPoint(new NavPoint(0x01620000u, goal.Origin), 0x0162015Cu), now);
        while (!runner.IsDone && now < 120)
        {
            now += 1.0 / 60;
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / 60);
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{runner.State} rungs={runner.Rungs} replans={runner.Replans} curve_turns={runner.CurveTurns} {runner.FailureReason}"));
        Assert.Equal(NavRunState.Arrived, runner.State);
        Assert.Equal(0, runner.Rungs);
        // The ordinary steering turns on in place while the curve meets a static (CurveTurns); the predictive steering's plans sweep their tracks past the statics and turn in place
        // first (NavRunnerPredictive): either way the turn comes before the run.
        Assert.True(runner.CurveTurns > 0 || runner.InPlacePresses > 0, "the turn came before the run at least once");
    }

    /// <summary>The cave's whole tour, every storey, from the arrival (<see cref="IndoorRunThroughDatTests"/> measures its first twelve cells): every leg arrives on its first plan with no rung.</summary>
    [RequiresDatsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_caves_whole_tour_walks(bool predictive)
    {
        var h = new ActionHarness(offline: true);
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, null, "alabree-tour");
        var (world, _, graph, walker, ground) = Rig(audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(ground.Arrival.Frame, sliding: true));
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var source = new RoomPathSource(graph);
        var now = 0.0;
        var failures = new List<string>();
        var tour = ground.Tour.Distinct().ToArray();
        foreach (var cellId in tour)
        {
            var spot = graph.RoomFrame(cellId);
            Assert.NotNull(spot);
            using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, new NavRunnerOptions { Name = "tour-" + (cellId & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture), PredictiveSteering = predictive });
            runner.Start(NavGoal.ToPoint(new NavPoint(cellId & 0xFFFF0000u, spot!.Value.Origin), cellId), now);
            var end = now + 120;
            while (!runner.IsDone && now < end)
            {
                now += 1.0 / 60;
                walker.Controller.Tick(now);
                runner.Step(now);
                h.Tick(1.0 / 60);
            }

            var line = string.Create(CultureInfo.InvariantCulture, $"0x{cellId & 0xFFFF:X4} {runner.State} {runner.RouteMetres:0.0} m in {runner.RunSeconds:0.0} s plans={runner.Plans} rungs={runner.Rungs} {runner.FailureReason}");
            output.WriteLine(line);
            if (runner.State != NavRunState.Arrived || runner.Rungs != 0 || runner.Replans != 0)
            {
                failures.Add(line);
            }
        }

        Assert.True(tour.Length >= 16, "the tour's 16 hunted cells");
        Assert.Empty(failures);
    }
}

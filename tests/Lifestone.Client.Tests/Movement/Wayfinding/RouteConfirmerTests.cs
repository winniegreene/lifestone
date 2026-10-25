using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Movement.MovementTestKit;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// P5a lane S5, the physics half of the confirmation (<see cref="RouteConfirmer"/>) on <see cref="MovementTestKit"/> worlds, and the
/// acceptance: an <see cref="OfflineWalker"/> bound to an offline session's <c>Move</c> runs a planned route round a stamped box to Arrived.
/// </summary>
public sealed class RouteConfirmerTests(ITestOutputHelper output)
{
    private static uint Lb => Block.LandBlockId & 0xFFFF0000u;

    private static NavWaypoint W(float x, float y, float z = 0f) =>
        new(new CellFrame(CellIds.LandCellAt(Block, x, y)!.Value, new AcFrame(new Vector3(x, y, z), Quaternion.Identity)), false);

    [Fact]
    public void A_flat_path_confirms_at_once_and_in_slices_with_a_ground_probe_a_metre_and_a_placement_every_two()
    {
        var world = WorldOf(Flat(Block));
        using var confirmer = new RouteConfirmer(world, HumanSetup);
        IReadOnlyList<NavWaypoint> path = [W(20f, 20f), W(120f, 20f), W(120f, 70f)];

        var first = confirmer.ConfirmAll(path);
        Assert.True(first == RouteConfirmStatus.Confirmed, confirmer.Failure?.ToString());
        var whole = (confirmer.GroundSamples, confirmer.Placements, confirmer.ConfirmedMetres);
        output.WriteLine($"150 m: {whole.GroundSamples} ground probes, {whole.Placements} placements in {confirmer.Milliseconds:F2} ms ({confirmer.Milliseconds * 1000 / 150:F1} us a metre)");
        Assert.Equal(150, whole.GroundSamples);
        Assert.Equal(75, whole.Placements);
        Assert.Equal(25, confirmer.PlacementNudges); // the 50 m along x = 120, a land cell's edge: TODO(placement-cell-edge)
        Assert.Equal(150f, whole.ConfirmedMetres, 0.01f);
        Assert.True(confirmer.Milliseconds / 150 < 2.0, "a metre costs under 2 ms even under load (target about 30 us)");
        Assert.DoesNotContain(world.Objects, o => o.Cell is not null); // the probe left the world

        confirmer.Begin(path);
        var calls = 0;
        while (confirmer.Continue(0) == RouteConfirmStatus.Pending)
        {
            calls++;
        }

        Assert.Equal(RouteConfirmStatus.Confirmed, confirmer.Status);
        Assert.Equal(whole, (confirmer.GroundSamples, confirmer.Placements, confirmer.ConfirmedMetres));
        Assert.True(calls >= 149, $"{calls} slices"); // one sample a call at a zero budget
    }

    [Fact]
    public void A_steep_walkable_ramp_confirms_by_half_samples_and_a_cliff_is_refused_by_the_ground_probe()
    {
        // 22 m a vertex is 0.92 m a metre (walkable: normal z 0.74): a 1 m sample rises past the probe's window, the half sample does not.
        var ramp = WorldOf(S1TileKit.Ramp(22));
        using (var confirmer = new RouteConfirmer(ramp, HumanSetup))
        {
            var status = confirmer.ConfirmAll([W(30f, 50f, 27.5f), W(60f, 50f, 55f)]);
            output.WriteLine($"ramp: {status} {confirmer.Failure}; steps up {confirmer.LargestSteps.Up:F2} m, {confirmer.GroundSamples} probes");
            Assert.True(status == RouteConfirmStatus.Confirmed, confirmer.Failure?.ToString());
            Assert.True(confirmer.GroundSamples > 45, "half samples were taken");
            Assert.InRange(confirmer.LargestSteps.Up, 0.3f, 0.75f);
        }

        // A cliff: 40 m between two vertices 24 m apart (not walkable) from x = 96.
        var cliff = WorldOf(Data.Physics.World.LandblockCollisionBuilder.FromTerrain(S1TileKit.Terrain(Block, (x, _) => x >= 5 ? 40 : 0), Heights));
        using (var confirmer = new RouteConfirmer(cliff, HumanSetup))
        {
            var status = confirmer.ConfirmAll([W(80f, 50f), W(130f, 50f, 40f)]);
            output.WriteLine($"cliff: {status} {confirmer.Failure}");
            Assert.Equal(RouteConfirmStatus.Failed, status);
            var failure = confirmer.Failure!.Value;
            Assert.Equal("ground", failure.Probe);
            Assert.InRange(failure.ArcMetres, 14f, 20f);
            Assert.Contains("no walkable ground within 0.6 m up", failure.Why, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_sight_test_sees_across_open_ground_and_the_nearest_static_is_named()
    {
        var world = WorldOf(S1TileKit.FlatWith(Block, S1TileKit.Box(Block, 100f, 100f, 0f, new Vector3(4f, 4f, 3f))));
        using var confirmer = new RouteConfirmer(world, HumanSetup);

        Assert.True(confirmer.InSight(At(Block, 20f, 20f, 0f), At(Block, 40f, 30f, 0f), out var tries));
        Assert.Equal(1, tries);
        Assert.False(confirmer.WallBetween(At(Block, 20f, 20f, 0f), At(Block, 22f, 20f, 0f)));
        var nearest = confirmer.NearestStatic(At(Block, 90f, 100f, 0f));
        output.WriteLine(nearest);
        Assert.StartsWith("static 0x0100F001 in cell 0x", nearest, StringComparison.Ordinal);
        Assert.EndsWith("at (100.0, 100.0, 0.0), 10.0 m away", nearest, StringComparison.Ordinal);
    }

    [Fact]
    public void The_offline_walker_runs_a_planned_route_round_a_stamped_box_to_Arrived_without_a_rung()
    {
        var block = S1TileKit.FlatWith(Block, S1TileKit.Box(Block, 96f, 96f, 0f, new Vector3(6f, 6f, 3f)));
        var world = WorldOf(block);
        var map = new NavMap();
        map.Attach(NavTileBuilder.Build(block));
        var planner = new PathPlanner(map);

        var h = new ActionHarness(offline: true);
        var weenie = new TestPlayerWeenie();
        var player = Player(world, At(Block, 96f, 80f, 0f), weenie);
        var now = world.CurrentTime;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, "s5-box");
        var walker = OfflineWalker.FromPhysics(world, player, weenie, NullLogSink.Instance, audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        using var confirmer = new RouteConfirmer(world, HumanSetup);
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new OutdoorPathSource(planner), confirmer, audit, new NavRunnerOptions { Name = "s5-box" });

        var goal = NavGoal.ToPoint(NavPoint.At(Lb, 96f, 112f));
        runner.Start(goal, now);
        var inBox = 0;
        while (!runner.IsDone && now < 60.0 + world.CurrentTime)
        {
            now += 1.0 / 60.0;
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / 60.0);
            var p = walker.Position.Origin;
            if (MathF.Abs(p.X - 96f) < 3f && MathF.Abs(p.Y - 96f) < 3f)
            {
                inBox++;
            }
        }

        output.WriteLine($"{runner.State}: {runner.VerdictFields()}; {runner.Path!.Waypoints.Count} waypoints; keys {runner.KeyChanges} (max {runner.MaxKeysPerSecond}/s), {audit.Summary.MoveToStateSent} MoveToStates; {runner.FailureReason}");
        Assert.Equal(NavRunState.Arrived, runner.State);
        Assert.Equal(0, runner.Stuck);
        Assert.Equal(0, runner.Rungs);
        Assert.Equal(0, audit.Summary.Corrections);
        Assert.Equal(0, inBox);
        Assert.True(runner.Path.Waypoints.Count > 2, "the route bends round the box");
        Assert.True(runner.RouteMetres > 32f, $"{runner.RouteMetres} m");
        Assert.True(walker.Position.GetOffset(NavMap.ToCellFrame(goal.Point)).Length() <= NavGoal.DefaultStopTolerance + 0.3f);
        Assert.InRange(runner.MaxKeysPerSecond, 1, 19);
        Assert.Equal(0, confirmer.Failure is null ? 0 : 1);
    }

    [Fact]
    public void An_object_goals_stop_short_is_the_arc_past_its_first_point_within_the_range()
    {
        // M4 P5b F1-3: a straight 20 m east to the object: the last 5 m are within a 5 m range; a bend that enters the circle mid-segment.
        var target = NavPoint.At(Lb, 40f, 20f);
        Assert.Equal(5f, RouteConfirmer.ArcBeyondRange([W(20f, 20f), W(40f, 20f)], target, 5f), 0.001f);
        Assert.Equal(0f, RouteConfirmer.ArcBeyondRange([W(20f, 20f), W(40f, 20f)], target, 0f));
        Assert.Equal(20f, RouteConfirmer.ArcBeyondRange([W(20f, 20f), W(40f, 20f)], target, 30f), 0.001f); // the start is in range: everything after it
        Assert.Equal(3f + 4f, RouteConfirmer.ArcBeyondRange([W(36f, 0f), W(36f, 20f), W(40f, 20f)], target, 5f), 0.001f); // (36, 17) is 5 m away
        Assert.Equal(0f, RouteConfirmer.ArcBeyondRange([W(20f, 20f), W(40f, 20f)], target, 5f, roomCell: 0xA9B4013Fu)); // no waypoint in the room: nothing trimmed
    }

    [Fact]
    public void An_object_inside_a_static_is_reached_within_its_range_without_a_placement_failure()
    {
        // M4 P5b F1-3: the path ends at the object's own point inside a 6 m box (Renald behind his counter); confirming it to the end refused the walk.
        var block = S1TileKit.FlatWith(Block, S1TileKit.Box(Block, 96f, 96f, 0f, new Vector3(6f, 6f, 3f)));
        var straight = new F1StraightSource();
        foreach (var needSight in new[] { false, true })
        {
            var world = WorldOf(block);
            var h = new ActionHarness(offline: true);
            var weenie = new TestPlayerWeenie();
            var player = Player(world, At(Block, 96f, 70f, 0f), weenie);
            var now = world.CurrentTime;
            using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, "f1-box-object");
            var walker = OfflineWalker.FromPhysics(world, player, weenie, NullLogSink.Instance, audit);
            h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
            using var confirmer = new RouteConfirmer(world, HumanSetup);
            using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), straight, confirmer, audit, new NavRunnerOptions { Name = "f1-box-object" });
            runner.Start(NavGoal.ToObject(0x7F000001, NavPoint.At(Lb, 96f, 96f), 5f, needSight), now);
            var end = now + 60.0;
            while (!runner.IsDone && now < end)
            {
                now += 1.0 / 60.0;
                walker.Controller.Tick(now);
                runner.Step(now);
                h.Tick(1.0 / 60.0);
            }

            output.WriteLine($"sight={needSight}: {runner.State} {runner.VerdictFields()} at {walker.Position.Origin}; {runner.FailureReason}; refused {runner.LastConfirmFailure}");
            Assert.Equal(0, runner.ConfirmFailures);
            Assert.Null(runner.LastConfirmFailure);
            Assert.Equal(0, runner.Rungs);
            Assert.InRange(walker.Position.Origin.Y, 90.5f, 91.6f); // stopped at the range's edge, 5 m short of the object, outside the box (93 to 99)
            // With sight required the arrival test stays: the run's end is the confirmer's own sight verdict there (here the box's top lets it see).
            var seen = confirmer.InSight(walker.Position, At(Block, 96f, 96f, 0f), out _);
            Assert.Equal(!needSight || seen ? NavRunState.Arrived : NavRunState.ArrivedWithoutSight, runner.State);
        }
    }

    /// <summary>A path source that walks straight from the body to the goal's point (the room graph's last step into an object's spot).</summary>
    private sealed class F1StraightSource : INavPathSource
    {
        public NavMap? Map => null;

        public PathPlanner? Planner => null;

        public NavPath Plan(in CellFrame from, NavGoal goal, NavScars scars) =>
            new(NavPathStatus.Found, [new NavWaypoint(from, false), new NavWaypoint(NavMap.ToCellFrame(goal.Point), false)], null, []);
    }
}

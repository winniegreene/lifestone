using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// M4 P5b F1 on the real dats (Exhaustive tier): runs that start indoors and an object goal behind a counter. The live journeys failed
/// <c>no path: start-blocked</c> from the chest room 0xA9B4016A and from Renald's back room 0xA9B40141 (the indoor-outdoor source handed an outdoor
/// goal to the outdoor planner, which refuses a start under a building's stamp), and the walk to Renald (object 0x7A9B4021 within 5 m) was refused
/// at his counter. Here: the chest room's run leaves by its door to an outdoor object (F1-2); the back room's run fails by the name
/// <c>indoor-start-unreachable</c> (F1-2, TODO(room-steps)); and Renald's object goal from the front room 0xA9B4013F is confirmed only to the first
/// point within its range, in the room (F1-3), with the measured evidence that no standable floor of the front room is within 5 m of him.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class F1IndoorStartDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const double Fps = 60.0;
    private const uint ChestRoomCell = 0xA9B4016A;
    private const uint RenaldBackRoomCell = 0xA9B40141;
    private const uint RenaldFrontRoomCell = 0xA9B4013F;
    private const uint RenaldDoorCell = 0xA9B40150;
    private static readonly Vector3 RenaldAt = new(139.23f, 18.33f, 94.01f); // object 0x7A9B4021 in the live run's object table

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void A_run_from_inside_the_chest_room_leaves_by_its_door_to_an_outdoor_object()
    {
        var (world, map, cells) = Holtburg();
        var source = new IndoorOutdoorPathSource(new PathPlanner(map), cells);
        var start = new CellFrame(ChestRoomCell, new AcFrame(new Vector3(75.89f, 37.38f, 94.01f), Quaternion.Identity)); // the live run's start
        var goal = NavGoal.ToObject(0x7A9B4072, new NavPoint(0xA9B40000, new Vector3(99f, 29.5f, 94f)), 3f, needSight: false); // the pathwarden chest

        var (runner, visited, at) = Walk(world, source, start, goal, 60, "f1-chest-room-out");

        Assert.True(runner.State == NavRunState.Arrived, runner.FailureReason);
        Assert.Equal(0, runner.Rungs);
        Assert.Equal(0, runner.ConfirmFailures);
        Assert.Equal(ChestRoomCell, visited[0]);
        Assert.True(LandDefs.IsOutdoorCell(visited[^1]), $"ended in 0x{visited[^1]:X8}");
        Assert.Contains(runner.Path!.Waypoints, w => w.Doorway && w.Indoor); // out through the door's sill
        Assert.True(Vector2.Distance(new Vector2(at.Origin.X, at.Origin.Y), new Vector2(99f, 29.5f)) <= 3.2f);
    }

    [RequiresDatsFact]
    public void A_run_from_Renalds_back_room_fails_by_the_name_indoor_start_unreachable_before_a_metre_is_run()
    {
        var (world, map, cells) = Holtburg();
        var source = new IndoorOutdoorPathSource(new PathPlanner(map), cells);
        var start = new CellFrame(RenaldBackRoomCell, new AcFrame(new Vector3(140.85f, 18.11f, 94.01f), Quaternion.Identity)); // where round 1 left the Brawler
        var goal = NavGoal.ToObject(0x7A9B404F, new NavPoint(0xA9B40000, new Vector3(81.33f, 11.80f, 94f)), 4f, needSight: false); // the lifestone

        var (runner, _, _) = Walk(world, source, start, goal, 60, "f1-back-room-out");

        Assert.Equal(NavRunState.Blocked, runner.State);
        Assert.Contains("no path: indoor-start-unreachable:0xA9B40141", runner.FailureReason, StringComparison.Ordinal);
        Assert.Equal(0f, runner.RouteMetres);
        Assert.Equal(0, runner.Rungs);
        Assert.NotEmpty(source.AvoidedCells); // the physics refused the ways out over the steps, TODO(room-steps)
    }

    [RequiresDatsFact]
    public void Renalds_object_goal_from_the_front_room_is_confirmed_only_to_its_range_and_no_front_room_floor_is_within_5_m_of_him()
    {
        var (world, map, cells) = Holtburg();

        // The evidence: the standable floor of the front room and its door cell nearest to Renald, on a 0.25 m grid.
        var scan = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230));
        world.UseTime(0.0);
        var nearest = float.MaxValue;
        for (var x = 128f; x <= 142f; x += 0.25f)
        {
            for (var y = 10f; y <= 24f; y += 0.25f)
            {
                if (scan.Place(new CellFrame(RenaldFrontRoomCell, new AcFrame(new Vector3(x, y, 94.05f), Quaternion.Identity)))
                    && scan.Position.CellId is RenaldFrontRoomCell or RenaldDoorCell
                    && MathF.Abs(scan.Position.Origin.Z - 94f) < 0.3f)
                {
                    nearest = MathF.Min(nearest, Vector2.Distance(new Vector2(scan.Position.Origin.X, scan.Position.Origin.Y), new Vector2(RenaldAt.X, RenaldAt.Y)));
                }
            }
        }

        output.WriteLine($"the front room's standable floor nearest to Renald: {nearest:F2} m");
        Assert.InRange(nearest, 5.2f, 6.4f); // measured 5.72 m: a 5 m range cannot be met from the front room

        // 5 m: refused at the counter (no point of the room is in range). 6.5 m: confirmed to the range's edge only, no refusal, arrived in the room.
        var renald = new NavPoint(0xA9B40000, RenaldAt);
        var five = Walk(world, new IndoorOutdoorPathSource(new PathPlanner(map), cells), Lifestone(), NavGoal.ToObject(0x7A9B4021, renald, 5f, needSight: false, RenaldFrontRoomCell), 60, "f1-renald-5");
        Assert.Equal(NavRunState.Blocked, five.Runner.State);
        var wider = Walk(world, new IndoorOutdoorPathSource(new PathPlanner(map), cells), Lifestone(), NavGoal.ToObject(0x7A9B4021, renald, 6.5f, needSight: false, RenaldFrontRoomCell), 60, "f1-renald-6.5");
        Assert.True(wider.Runner.State == NavRunState.Arrived, wider.Runner.FailureReason);
        Assert.Equal(0, wider.Runner.ConfirmFailures);
        Assert.Equal(RenaldFrontRoomCell, wider.At.CellId); // the range alone is met from the street through the wall: the goal's room is required
        // The runner measures the range in global metres (a landblock origin of some 30 km in a float: about 4 mm a step), this test in landblock-local
        // ones, so an arrival on the range's edge (the indoor straight run's, 6.50 m) may read a few millimetres over it here.
        Assert.True(Vector2.Distance(new Vector2(wider.At.Origin.X, wider.At.Origin.Y), new Vector2(RenaldAt.X, RenaldAt.Y)) <= 6.5f + 0.01f);

        // With sight required the arrival test stays: over the counter every sight line is blocked (ArrivedWithoutSight, a failed leg for the host).
        var sight = Walk(world, new IndoorOutdoorPathSource(new PathPlanner(map), cells), Lifestone(), NavGoal.ToObject(0x7A9B4021, renald, 6.5f, needSight: true, RenaldFrontRoomCell), 60, "f1-renald-sight");
        Assert.Equal(NavRunState.ArrivedWithoutSight, sight.Runner.State);
    }

    private (PhysicsWorld World, NavMap Map, LandblockCellSet Cells) Holtburg()
    {
        var builder = new LandblockCollisionBuilder(Dats);
        var world = new PhysicsWorld();
        var map = new NavMap();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var block = builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value);
                world.Attach(block);
                if (dx == 0 && dy == 0)
                {
                    map.Attach(NavTileBuilder.Build(block));
                }
            }
        }

        return (world, map, LandblockCellSet.Load(new CellStructureLibrary(Dats), LandblockCoord.Holtburg));
    }

    private static CellFrame Lifestone() => new(0xA9B40019, new AcFrame(new Vector3(81.33f, 11.80f, 94.01f), Quaternion.Identity));

    /// <summary>Runs the explore-mode walker from <paramref name="start"/> to <paramref name="goal"/>: the runner, the cells it stood in, where it ended.</summary>
    private (NavRunner Runner, List<uint> Visited, CellFrame At) Walk(PhysicsWorld world, INavPathSource source, CellFrame start, NavGoal goal, double seconds, string name)
    {
        var h = new ActionHarness(offline: true);
        var now = 0.0;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, name);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230), sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.True(walker.Place(start), $"placed at {start}");
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, new NavRunnerOptions { Name = name });
        runner.Start(goal, now);
        var visited = new List<uint> { walker.Position.CellId };
        while (!runner.IsDone && now < seconds)
        {
            now += 1.0 / Fps;
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / Fps);
            if (walker.Position.CellId != visited[^1])
            {
                visited.Add(walker.Position.CellId);
            }
        }

        output.WriteLine($"{name}: {runner.State} {runner.VerdictFields()} at 0x{walker.Position.CellId:X8} {walker.Position.Origin}; cells {string.Join(" ", visited.Select(c => (c & 0xFFFF).ToString("X3")))}; {runner.FailureReason}");
        runner.Dispose();
        return (runner, visited, walker.Position);
    }
}

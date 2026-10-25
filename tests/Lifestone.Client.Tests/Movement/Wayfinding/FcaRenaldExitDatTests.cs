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
/// M4 P5b fix FC-a2 on the real dats (Exhaustive tier). Gate round 1's headless Brawler ended the vendor station on the end of Renald's counter (ACE's walk-to-use MoveToObject, which the journey's body
/// follows, carried it up: z 94.94 over the front room's floor at 94.00) and the next two legs were stuck there ("stuck after 3 plans and 12 rungs; nearest static 0x02000121 ... 0.5 m away"). From the live
/// point: the old leg is stuck the same way (the reproduction), the step-off (<see cref="RaisedStart"/>) walks the body down to the floor, and the leg from there leaves by the door to the street and arrives
/// at the chest room's chest. A start on the floor is not raised.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class FcaRenaldExitDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const double Fps = 60.0;
    private const uint RenaldFrontRoomCell = 0xA9B4013F;
    private const uint ChestRoomCell = 0xA9B4016A;
    private static readonly Vector3 LiveStuckAt = new(134.76f, 15.40f, 94.94f); // the live start of vendor-7 and cell-8 (serial-headless-journey-melee, gate p5b-r1s)
    private static readonly Vector3 FloorArrival = new(133.35f, 15.65f, 94.01f); // where the walk to Renald at 6.5 m arrives (F1IndoorStartDatTests), on the floor

    private DatSet Dats => fixture.Dats;

    private static NavGoal Chest() => NavGoal.ToObject(0x7A9B401D, new NavPoint(0xA9B40000, new Vector3(75.82f, 38.39f, 94.00f)), 1.5f, needSight: false, ChestRoomCell); // the live cell-8 leg

    private static CellFrame Live() => new(RenaldFrontRoomCell, new AcFrame(LiveStuckAt, Quaternion.Identity));

    [RequiresDatsFact]
    public void From_the_live_point_on_Renalds_counter_the_old_leg_is_stuck_by_the_counter_as_live()
    {
        var (world, map, cells) = Holtburg();
        var (runner, _, at) = Walk(world, new IndoorOutdoorPathSource(new PathPlanner(map), cells), Live(), Chest(), 120, "fca-old", stepOff: false);

        Assert.Equal(NavRunState.Blocked, runner.State);
        Assert.Contains("nearest static 0x02000121 in cell 0xA9B4013F", runner.FailureReason, StringComparison.Ordinal);
        Assert.True(Vector3.Distance(at.Origin, LiveStuckAt) < 0.3f, at.ToString());
    }

    [RequiresDatsFact]
    public void From_the_live_point_the_body_steps_off_the_counter_then_leaves_by_the_door_and_arrives_at_the_chest()
    {
        var (world, map, cells) = Holtburg();
        var floor = RaisedStart.FloorBelow(new IndoorOutdoorPathSource(new PathPlanner(map), cells).Plan(Live(), Chest(), new NavScars()), Live(), out _);
        Assert.NotNull(floor);
        Assert.InRange(floor!.Value, 93.9f, 94.1f);

        var (runner, visited, at) = Walk(world, new IndoorOutdoorPathSource(new PathPlanner(map), cells), Live(), Chest(), 120, "fca-step-off", stepOff: true);

        Assert.True(runner.State == NavRunState.Arrived, runner.FailureReason);
        Assert.Equal(RenaldFrontRoomCell, visited[0]);
        Assert.Contains(visited, LandDefs.IsOutdoorCell); // out of Renald's by the door to the street
        Assert.Equal(ChestRoomCell, at.CellId);
        Assert.True(Vector2.Distance(new Vector2(at.Origin.X, at.Origin.Y), new Vector2(75.82f, 38.39f)) <= 1.5f + 0.2f, at.ToString());
    }

    [RequiresDatsFact]
    public void A_start_on_the_front_rooms_floor_is_not_raised()
    {
        var (_, map, cells) = Holtburg();
        var start = new CellFrame(RenaldFrontRoomCell, new AcFrame(FloorArrival, Quaternion.Identity));
        Assert.Null(RaisedStart.FloorBelow(new IndoorOutdoorPathSource(new PathPlanner(map), cells).Plan(start, Chest(), new NavScars()), start, out _));
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

    /// <summary>The leg's order (JourneyRunLeg): the step-off first when the start is raised (with <paramref name="stepOff"/>), then a runner planned from where the body stands.</summary>
    private (NavRunner Runner, List<uint> Visited, CellFrame At) Walk(PhysicsWorld world, INavPathSource source, CellFrame start, NavGoal goal, double seconds, string name, bool stepOff)
    {
        var h = new ActionHarness(offline: true);
        var now = 0.0;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, name);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230), sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.True(walker.Place(start), $"placed at {start}");
        var visited = new List<uint> { walker.Position.CellId };
        if (stepOff && RaisedStart.FloorBelow(source.Plan(walker.Position, goal, new NavScars()), walker.Position, out var next) is { } floor)
        {
            var off = new RaisedStart(h.Api.Move, () => walker.Position, floor, next, NullLogSink.Instance, name);
            var state = RaisedStartState.Running;
            while (state == RaisedStartState.Running && now < seconds)
            {
                now += 1.0 / Fps;
                h.Tick(1.0 / Fps);
                walker.Controller.Tick(now);
                state = off.Step(now);
            }

            output.WriteLine($"{name}: step-off {state} after {off.Tries} heading(s) at 0x{walker.Position.CellId:X8} {walker.Position.Origin} {off.Failure}");
            Assert.Equal(RaisedStartState.Down, state);
        }

        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, new NavRunnerOptions { Name = name });
        runner.Start(goal, now);
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

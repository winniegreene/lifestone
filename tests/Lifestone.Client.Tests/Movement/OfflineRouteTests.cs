using System.Numerics;
using DatReaderWriter.DBObjs;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Data.World.Data;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement;

/// <summary>
/// The DoD 8 routes offline (m2-plan PH9) on the real dats: the explore-mode walker, driven through its controller by
/// <see cref="RouteDriver"/>, walks three laps of the ring <see cref="RouteLibrary.HoltburgLaps"/> generates around
/// Holtburg's arrival point, and from the Training Academy's spawn through its rooms as <see cref="CellRoutePlanner"/>
/// plans.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class OfflineRouteTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The Training Academy's first starting location (CharGen 0x0E000002, StartingAreas[0].Locations[0]).</summary>
    private const uint AcademySpawnCell = 0x860201AD;

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void Three_laps_around_Holtburg_stay_on_the_ground_and_finish()
    {
        var builder = new LandblockCollisionBuilder(Dats);
        var world = new PhysicsWorld();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                world.Attach(builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value));
            }
        }

        // The town's arrival point from ACE's points of interest (the Holtburg row), beside the lifestone.
        var poi = AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg");
        var centre = new CellFrame(poi.CellId, new AcFrame(poi.Origin, Quaternion.Identity));
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance);
        var route = RouteLibrary.HoltburgLaps(centre, world, walker.Physics.Setup!);
        var start = route.Start!.Value;
        output.WriteLine($"ring radius {centre.GetOffset(start).Length():F1} m around {centre}; start {start}");
        Assert.Equal(RouteLibrary.HoltburgLapsName, route.Name);
        Assert.Equal(3, route.Laps);
        Assert.Equal(RouteLibrary.RingPoints, route.Goals.Count);
        Assert.Equal(start, route.Goals[^1].Target);
        Assert.Same(walker.Physics, Assert.Single(world.Objects)); // the ring test's body is gone

        world.UseTime(0.0);
        Assert.True(walker.Place(start));
        var controller = walker.Controller;
        var driver = new RouteDriver(controller, route);
        var stops = new List<RouteStop>();
        driver.Stopped += stops.Add;
        var now = 0.0;
        var offGround = 0;
        var frames = 0;
        var status = RouteStatus.Running;
        while (status == RouteStatus.Running && now < 600.0)
        {
            now += 1.0 / 60.0;
            controller.Tick(now);
            status = driver.Tick(now);
            frames++;
            if (!controller.OnGround)
            {
                offGround++;
            }
        }

        output.WriteLine($"{status} after {now:F1} s; {offGround} of {frames} frames off the ground; {controller.Stats.MoveToStateSent} movements, {controller.Stats.AutonomousPositionSent} positions; longest gap {controller.Stats.MaxGroundMovingGapSeconds:F3} s");
        Assert.True(status == RouteStatus.Done, driver.FailureReason);
        Assert.Equal(3, driver.CompletedLaps);
        Assert.Equal(3, stops.Count);
        output.WriteLine($"stopped {string.Join(", ", stops.Select(s => $"{s.Position.GetOffset(start).Length():F2} m"))} from the start");
        Assert.All(stops, s => Assert.True(s.Position.GetOffset(start).Length() < 1.0f, $"stopped at {s.Position}"));
        Assert.Equal(0, offGround);
        Assert.True(controller.Stats.MaxGroundMovingGapSeconds <= 1.0 + (1.0 / 60.0) + 1e-9);
    }

    [RequiresDatsFact]
    public void The_walk_from_the_Academy_spawn_follows_the_planned_rooms_and_stops_at_the_target()
    {
        Assert.True(Dats.TryGetPortal<CharGen>(0x0E000002, out var charGen));
        var spawnLocation = charGen!.StartingAreas[0].Locations[0];
        Assert.Equal(AcademySpawnCell, spawnLocation.CellId);
        var spawn = new CellFrame(spawnLocation.CellId, new AcFrame(spawnLocation.Frame.Origin, spawnLocation.Frame.Orientation)).Normalized();
        var coord = LandblockCoord.FromId(AcademySpawnCell);
        var cells = LandblockCellSet.Load(new CellStructureLibrary(Dats), coord);
        var planner = new CellRoutePlanner(cells);

        // The exit portal is a world-database object; offline, the target is the room farthest from the spawn through
        // walkable doorways (its largest floor's centre).
        var target = FarthestRoom(planner, AcademySpawnCell);
        var path = planner.FindPath(AcademySpawnCell, target.CellId)!;
        output.WriteLine($"target 0x{target.CellId:X8} at {target.Frame.Origin}; planned {string.Join(" > ", path.Select(c => $"0x{c:X8}"))}");

        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(Dats).Build(coord));
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance);
        var route = RouteLibrary.AcademyExit(spawn, target, cells, stopWithin: 1.0f);
        Assert.Equal(RouteLibrary.AcademyExitName, route.Name);
        Assert.Equal(spawn, route.Start);
        Assert.InRange(route.Goals.Count - 1, path.Count - 1, (path.Count - 1) * 2);

        world.UseTime(0.0);
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(spawn, sliding: true));
        var controller = walker.Controller;
        var driver = new RouteDriver(controller, route);
        var visited = new List<uint> { controller.Position.CellId };
        var now = 0.0;
        var status = RouteStatus.Running;
        var offGround = 0;
        while (status == RouteStatus.Running && now < 300.0)
        {
            now += 1.0 / 60.0;
            controller.Tick(now);
            status = driver.Tick(now);
            if (controller.Position.CellId != visited[^1])
            {
                visited.Add(controller.Position.CellId);
            }

            if (!controller.OnGround)
            {
                offGround++;
            }
        }

        output.WriteLine($"{status} after {now:F1} s at {controller.Position}; visited {string.Join(" > ", visited.Select(c => $"0x{c:X8}"))}; {offGround} frames off the ground");
        Assert.True(status == RouteStatus.Done, driver.FailureReason);

        // Only the planned rooms, entered in the planned order. Running round a doorway's corner can step back over the
        // portal just crossed for a frame or two (D9 and DA of this route), which is not a wrong room.
        Assert.All(visited, c => Assert.Contains(c, path));
        Assert.Equal(path, visited.Distinct());
        Assert.Equal(0, offGround);
        Assert.True(controller.Position.GetOffset(target).Length() < 1.6f);
    }

    /// <summary>The room with the most walkable doorways between it and <paramref name="from"/>, at its largest floor's centre.</summary>
    private static CellFrame FarthestRoom(CellRoutePlanner planner, uint from)
    {
        var cells = planner.Cells;
        var best = (Cell: from, Depth: 0);
        foreach (var id in cells.Ids)
        {
            if (planner.FindPath(from, id) is { } path && path.Count - 1 > best.Depth)
            {
                best = (id, path.Count - 1);
            }
        }

        var cell = cells.GetCell(best.Cell)!;
        var floor = cells.GetStructure(cell).Floors.OrderByDescending(f => f.Area).First();
        var centre = cell.Frame.LocalToParent(floor.Centroid);
        return new CellFrame(cell.Id, new AcFrame(centre, Quaternion.Identity));
    }
}

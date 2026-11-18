using System.Numerics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Tests.Session.Grounds;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Tests.Shared;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Data.World.Data;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// The room graph on the real dats (P5a lane S3, Exhaustive tier: it reads the dats and skips without them, the pattern of
/// <c>OfflineRouteTests</c>): the Holtburg lifestone to Renald's shop (0xA9B4013F) and Thelnoth's cellar (0xA9B40111) planned door to
/// room by <see cref="IndoorOutdoorRoute"/>, and the Black Spawn Den's tour over <see cref="RoomGraph"/> with lean 0.3 (the cells the
/// den's own planner used to reach, reached).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class RoomGraphDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>
    /// A room of Renald's shop (the vendor approach P5a closes): his front room, the shop goal (docs/wayfinding.md). Until M6 4 W4 this was his back room
    /// 0xA9B40141, which the room graph planned and the physics refused; the sill rule (<see cref="Lifestone.Client.Movement.Routes.CellRoutePlanner.HasFloorsAtSill"/>)
    /// now refuses its threshold 0xA9B40140 (1 m over the floors either side) at the plan, so 0x141 is NoDoorway (<c>NavRunnerDatTests</c>).
    /// </summary>
    private const uint RenaldShopCell = 0xA9B4013F;

    /// <summary>A room of Thelnoth's cellar building.</summary>
    private const uint ThelnothCellarCell = 0xA9B40111;

    private DatSet Dats => fixture.Dats;

    public static TheoryData<uint> Shops => new() { RenaldShopCell, ThelnothCellarCell };

    /// <summary>
    /// The world walk (W8, building exits): Viamont (0x31D6) is built of buildings set wall to wall, and a door of one opens straight into a door of the next with no ground between, so
    /// neither door's lane reaches land and no plan joined the two rooms (the town's largest group of rooms joined on foot was 8). The graph now walks such a pair as a doorway
    /// (<see cref="RoomGraph.PairedDoors"/>), and the body walks it from one room's middle to the other's with no recovery move and no refusal.
    /// </summary>
    [RequiresDatsFact]
    public void Two_buildings_doors_that_open_into_each_other_are_walked_as_a_doorway()
    {
        var coord = new LandblockCoord(0x31, 0xD6);
        var cells = LandblockCellSet.Load(new CellStructureLibrary(Dats), coord);
        var graph = new RoomGraph(cells, RoomGraph.BuildingDoorwayLean);
        var pairs = graph.PairedDoors.OrderBy(p => p.From.CellId).ThenBy(p => p.To.CellId).ToList();
        foreach (var (from, to) in pairs)
        {
            output.WriteLine($"0x{from.CellId:X8} {from.Sill} -> 0x{to.CellId:X8} {to.Sill}");
        }

        Assert.Equal(54, pairs.Count);
        Assert.All(pairs, p => Assert.Contains(pairs, q => q.From == p.To && q.To == p.From));
        var (a, b) = pairs[0];
        var start = graph.RoomFrame(a.CellId)!.Value;
        var goal = graph.RoomFrame(b.CellId)!.Value;
        var route = graph.Plan(a.CellId, start.Origin, b.CellId, goal.Origin);
        Assert.NotNull(route);
        Assert.Equal([a.CellId, b.CellId], route.Cells);

        var geometry = new Lifestone.Data.Physics.Geometry.PhysicsGeometryLibrary(Dats);
        var rig = new OfflineWalkRig(Dats, geometry, new Lifestone.Data.Animation.DatAnimationSource(Dats), new LandblockCollisionBuilder(Dats, geometry).Build, Lifestone.Client.Diagnostics.NullLogSink.Instance);
        var walk = rig.Walk(new OfflineWalkRequest("paired", start, NavGoal.ToPoint(NavMap.FromCellFrame(goal), b.CellId), 60) { PathSource = map => new IndoorOutdoorPathSource(new PathPlanner(map), cells) });
        output.WriteLine($"{walk.State} rungs {walk.Rungs} refused {walk.ConfirmFailures} {walk.Reason}");
        Assert.True(walk.Clean, $"{walk.State} rungs {walk.Rungs} refused {walk.ConfirmFailures}: {walk.Reason} {string.Join("; ", walk.RefusedWhy)}");
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Shops))]
    public void The_lifestone_reaches_a_shop_room_through_its_doorway_lane_and_the_planned_cells(uint shopCell)
    {
        var block = new LandblockCollisionBuilder(Dats).Build(LandblockCoord.Holtburg);
        var tile = NavTileBuilder.Build(block);
        var map = new NavMap();
        map.Attach(tile);
        var planner = new PathPlanner(map);
        var rooms = new RoomGraph(LandblockCellSet.Load(new CellStructureLibrary(Dats), LandblockCoord.Holtburg));
        var poi = AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg"); // beside the lifestone
        var from = NavPoint.At(tile.LandblockId, poi.Origin.X, poi.Origin.Y, poi.Origin.Z);
        var goal = rooms.RoomFrame(shopCell)!.Value.Origin;

        var result = IndoorOutdoorRoute.Plan(planner, rooms, from, shopCell, goal);
        output.WriteLine($"0x{shopCell:X8}: {result.Status}, lane entry 0x{result.Lane?.EntryCellId ?? 0:X8}, outdoor {result.Outdoor?.Route?.ArcLength:F1} m in {result.Outdoor?.Milliseconds:F1} ms, cells {string.Join(" ", result.CellsEntered.Select(c => c.ToString("X4").Substring(4)))}, {result.Inside.Count} inside goals");
        Assert.Equal(IndoorOutdoorStatus.Found, result.Status);
        Assert.Null(result.Reason);

        // The planned cells: from the lane's entry cell through the doorways into the shop room.
        Assert.NotNull(result.Lane);
        Assert.Equal(result.Lane.EntryCellId, result.CellsEntered[0]);
        Assert.Equal(shopCell, result.CellsEntered[^1]);
        Assert.Equal(shopCell, result.Inside[^1].CellId);
        Assert.Equal(goal, result.Inside[^1].Origin);
        Assert.Equal(result.Lane.EntryCellId, result.Exit!.CellId);

        // The outdoor part is clear of blocked nodes: every half metre of the route, and the lane to the sill.
        var route = result.Outdoor!.Route!;
        Assert.Equal(NavStatus.Found, result.Outdoor.Status);
        var samples = 0;
        for (var s = 0f; s <= route.ArcLength; s += 0.5f)
        {
            var point = route.PointAt(s);
            Assert.True(map.TryGetNode(point, out var node), $"no node under {point}");
            Assert.True((node.Flags & NavFlags.Blocked) == 0, $"blocked node under {point} at {s:F1} m: {node.Flags}");
            samples++;
        }

        Assert.True(samples > 20);
        Assert.All(result.LanePoints, p =>
        {
            Assert.True(map.TryGetNode(p, out var node));
            Assert.True((node.Flags & NavFlags.Blocked) == 0, $"lane point {p}");
        });
        Assert.True(route.End.HorizontalDistance(result.LanePoints[0]) < 1.0f, "the outdoor route ends at the lane's outer end");
        Assert.True(map.LineOfSight(route.End, result.LanePoints[0]) || route.End.HorizontalDistance(result.LanePoints[0]) < 0.6f);
    }

    [RequiresDatsFact]
    public void Holtburgs_doorways_the_graph_joins_are_the_tiles_lanes_and_the_shops_are_among_them()
    {
        var block = new LandblockCollisionBuilder(Dats).Build(LandblockCoord.Holtburg);
        var tile = NavTileBuilder.Build(block);
        var cells = LandblockCellSet.Load(new CellStructureLibrary(Dats), LandblockCoord.Holtburg);
        var rooms = new RoomGraph(cells, RoomGraph.BuildingDoorwayLean); // the tile cuts lanes at the buildings' lean (M6 5 w), so the town's graph joins them at the same lean
        var joined = tile.DoorwayLanes.Where(l => rooms.TryExit(l, out _)).ToList();
        output.WriteLine($"{rooms.ExitsToOutside.Count} exits to outside; the tile has {tile.DoorwayLanes.Count} lanes, {joined.Count} joined");
        var upright = new RoomGraph(cells);
        output.WriteLine("lanes at the buildings' lean only: " + string.Join(' ', tile.DoorwayLanes.Where(l => !upright.TryExit(l, out _)).Select(l => $"0x{l.EntryCellId:X8} ({l.Sill.X:0.0}, {l.Sill.Y:0.0}, {l.Sill.Z:0.0})")));
        Assert.NotEmpty(rooms.ExitsToOutside);
        Assert.Equal(tile.DoorwayLanes.Count, joined.Count); // every lane S1 cut is a door the room graph has
        foreach (var lane in tile.DoorwayLanes)
        {
            Assert.True(rooms.TryJoin(tile, lane.EntryCellId, out var found, out var exit));
            Assert.Equal(lane.EntryCellId, exit.CellId);
            Assert.True(found.EntryCellId == lane.EntryCellId);
        }

        // The shops' entry cells (S1: Renald 0x145 and 0x150, Thelnoth 0x113 and 0x115) are exits.
        foreach (var low in new uint[] { 0x145, 0x150, 0x113, 0x115 })
        {
            Assert.Contains(rooms.ExitsToOutside, e => e.CellId == (0xA9B40000u | low));
        }
    }

    [RequiresDatsFact]
    public void The_dens_tour_over_the_room_graph_with_lean_0_3_reaches_every_spawn_cell_the_old_planner_reached()
    {
        var all = LandblockCellSet.Load(new CellStructureLibrary(Dats), new LandblockCoord(1, 5));
        var floor = GroundRooms.FloorOf(all, DenGround.Profile);
        var graph = GroundRooms.GraphOf(all, DenGround.Profile);
        Assert.Equal(0.3f, graph.MaxLean);
        Assert.Equal(DenGround.Profile.Walkable.Count, floor.Count);

        // The old planner (a breadth first search through the doorways the upright rule and the 0.3 lean accept) reached all twelve spawn cells from the arrival.
        var from = DenGround.Arrival;
        foreach (var low in DenGround.SpawnLowWords)
        {
            var cellId = 0x01050000u | low;
            var path = graph.FindPath(from.CellId, cellId);
            Assert.NotNull(path);
            Assert.All(path, c => Assert.True(DenGround.Profile.IsWalkable(c), $"0x{c:X8} on the way to 0x{cellId:X8}"));

            var route = graph.PlanScripted("tour", in from, cellId, DenGround.Profile.Walk.StopWithinMetres, DenGround.Profile.Walk.StopDwellSeconds);
            Assert.NotNull(route);
            Assert.Equal(cellId, route.Goals[^1].Target.CellId);
            Assert.True(route.Goals[^1].Stop);
            var planned = graph.Plan(from.CellId, from.Origin, cellId, graph.RoomFrame(cellId)!.Value.Origin)!;
            Assert.Equal(from.CellId, planned.Cells[0]);
            Assert.Equal(cellId, planned.Cells[^1]);
            Assert.All(planned.Cells, c => Assert.True(DenGround.Profile.IsWalkable(c), $"0x{c:X8} in the plan to 0x{cellId:X8}"));
            Assert.InRange(planned.Cells.Count, 2, path.Count + 3); // the shortest walk in metres is near the fewest doorways
            output.WriteLine($"0x{low:X4}: {path.Count - 1} doorways by the fewest-doorways search, {planned.Cells.Count - 1} by the shortest walk, {route.Goals.Count} goals");

            // The next leg starts where this one ended (the loop's tour walks leg to leg).
            from = new CellFrame(cellId, graph.RoomFrame(cellId)!.Value.Frame);
        }
    }

    [RequiresDatsFact]
    public void Without_the_lean_the_den_is_reached_in_twelve_spawn_cells_of_twelve_by_floor_crossings_and_the_lean_reaches_twelve()
    {
        var floor = GroundRooms.FloorOf(LandblockCellSet.Load(new CellStructureLibrary(Dats), new LandblockCoord(1, 5)), DenGround.Profile);
        var upright = new RoomGraph(floor);
        var leaning = new RoomGraph(floor, DenGround.Profile.Walk.DoorwayLean);
        var arrival = DenGround.Arrival.CellId;
        var plain = DenGround.SpawnLowWords.Count(low => upright.FindPath(arrival, 0x01050000u | low) is not null);
        var lean = DenGround.SpawnLowWords.Count(low => leaning.FindPath(arrival, 0x01050000u | low) is not null);
        output.WriteLine($"lean 0: {plain} of 12 spawn cells; lean 0.3: {lean} of 12");
        Assert.Equal(12, plain); // M6 4 W4: 1 (0x0383 only) by the fit test alone; the plazas' leaning doorways are floor crossings (CellRoutePlanner.FloorCrossingOf: the floors meet on them), so the lean is no longer needed
        Assert.Equal(12, lean);
    }
}

using System.Numerics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// <see cref="RoomGraph"/>, the generalised <see cref="CellRoutePlanner"/> lean and <see cref="IndoorOutdoorRoute"/> (P5a lane S3) on
/// synthetic rooms (<see cref="S3RoomKit"/>, no dats): the lean parameter, the plan through two doorways with the waypoint pairs, the
/// exits to outside, the join with a tile's doorway lane and the composed outdoor-to-room route. The same on the dats is
/// <see cref="RoomGraphDatTests"/>.
/// </summary>
public sealed class RoomGraphTests
{
    private const float Tolerance = 0.02f;

    [Fact]
    public void The_kits_leaning_doorways_have_the_dens_lean()
    {
        var cells = S3RoomKit.Rooms(leaning: true);
        var cell = cells.GetCell(S3RoomKit.A)!;
        var polygon = cells.GetStructure(cell).GetPortal(cell.Portals[0].PolygonId)!;
        Assert.InRange(MathF.Abs(polygon.Plane.Normal.Z), 0.19f, 0.2f);
    }

    [Theory]
    [InlineData(false, 0f, true)]    // an upright doorway: today's rule
    [InlineData(false, 0.3f, true)]  // a lean allowance never refuses an upright doorway
    [InlineData(true, 0f, false)]    // 0 refuses a leaning doorway (the normal's vertical part 0.196 is over 0.1)
    [InlineData(true, 0.15f, false)] // under the doorway's lean
    [InlineData(true, 0.3f, true)]   // 0.3 passes it
    public void The_planner_takes_the_lean_as_a_parameter(bool leaning, float maxLean, bool walkable)
    {
        var planner = new CellRoutePlanner(S3RoomKit.Rooms(leaning), maxLean);
        var cell = planner.Cells.GetCell(S3RoomKit.A)!;
        Assert.Equal(maxLean, planner.MaxLean);
        Assert.Equal(walkable, planner.IsWalkable(cell, cell.Portals[0]));
        Assert.Equal(walkable, planner.IsUpright(cell, cell.Portals[0]));
        Assert.Equal(walkable, planner.FindPath(S3RoomKit.A, S3RoomKit.C) is not null);
        Assert.Equal(walkable, planner.Plan(S3RoomKit.A, S3RoomKit.C) is not null);
    }

    [Fact]
    public void The_default_constructor_keeps_the_upright_rule()
    {
        var planner = new CellRoutePlanner(S3RoomKit.Rooms(leaning: true));
        Assert.Equal(0f, planner.MaxLean);
        Assert.Null(planner.FindPath(S3RoomKit.A, S3RoomKit.B));
        Assert.NotNull(new CellRoutePlanner(S3RoomKit.Rooms()).FindPath(S3RoomKit.A, S3RoomKit.B));
    }

    [Fact]
    public void An_outside_portal_fits_a_human_but_is_never_an_inside_doorway_whatever_the_lean()
    {
        var planner = new CellRoutePlanner(S3RoomKit.Rooms(), 1f);
        var cell = planner.Cells.GetCell(S3RoomKit.A)!;
        Assert.False(planner.IsWalkable(cell, cell.Portals[1])); // leads outside: never an inside doorway
        Assert.True(planner.Fits(cell, cell.Portals[1]));       // but a human fits through it
    }

    [Theory]
    [InlineData(false, 0f)]
    [InlineData(true, 0.3f)]
    public void The_room_graph_plans_through_two_doorways_with_the_waypoint_pairs(bool leaning, float maxLean)
    {
        var graph = new RoomGraph(S3RoomKit.Rooms(leaning), maxLean);
        var route = graph.Plan(S3RoomKit.A, new Vector3(96f, 92f, 0f), S3RoomKit.C, new Vector3(96f, 116f, 0f));
        Assert.NotNull(route);
        Assert.Equal([S3RoomKit.A, S3RoomKit.B, S3RoomKit.C], route.Cells);

        // Two points per doorway: 0.7 m before it in the room being left, 0.5 m past it in the room entered, at the doorway's centre line.
        Assert.Equal(4, route.Waypoints.Count);
        Assert.Equal([S3RoomKit.A, S3RoomKit.B, S3RoomKit.B, S3RoomKit.C], route.Waypoints.Select(w => w.CellId));
        var lean = leaning ? 0.22f : 0f; // the leaning doorway's centre is half the 0.44 m tilt outward of the wall
        Assert.All(route.Waypoints, w => Assert.Equal(96f, w.Origin.X, Tolerance));
        Assert.Equal(101f + lean - CellRoutePlanner.ApproachMetres, route.Waypoints[0].Origin.Y, Tolerance);
        Assert.Equal(101f + lean + CellRoutePlanner.PushIntoCellMetres, route.Waypoints[1].Origin.Y, Tolerance);
        Assert.Equal(111f + lean - CellRoutePlanner.ApproachMetres, route.Waypoints[2].Origin.Y, Tolerance);
        Assert.Equal(111f + lean + CellRoutePlanner.PushIntoCellMetres, route.Waypoints[3].Origin.Y, Tolerance);
    }

    [Fact]
    public void The_room_graph_matches_the_planner_waypoints_and_walks_back()
    {
        var cells = S3RoomKit.Rooms();
        var graph = new RoomGraph(cells);
        var stock = new CellRoutePlanner(cells).Plan(S3RoomKit.A, S3RoomKit.C)!;
        var planned = graph.Plan(S3RoomKit.A, new Vector3(96f, 96f, 0f), S3RoomKit.C, new Vector3(96f, 116f, 0f))!;
        Assert.Equal(stock.Cells, planned.Cells);
        Assert.Equal(stock.Waypoints, planned.Waypoints);

        var back = graph.Plan(S3RoomKit.C, new Vector3(96f, 116f, 0f), S3RoomKit.A, new Vector3(96f, 96f, 0f))!;
        Assert.Equal([S3RoomKit.C, S3RoomKit.B, S3RoomKit.A], back.Cells);
        Assert.Equal(4, back.Waypoints.Count);
        Assert.True(back.Waypoints[0].Origin.Y > back.Waypoints[3].Origin.Y); // walking south
    }

    [Fact]
    public void A_plan_inside_one_room_has_no_waypoints_and_a_missing_or_cut_off_cell_has_none()
    {
        var graph = new RoomGraph(S3RoomKit.Rooms());
        var here = graph.Plan(S3RoomKit.B, new Vector3(96f, 106f, 0f), S3RoomKit.B, new Vector3(97f, 107f, 0f));
        Assert.Equal([S3RoomKit.B], here!.Cells);
        Assert.Empty(here.Waypoints);
        Assert.Null(graph.Plan(S3RoomKit.A, Vector3.Zero, 0x40400999u, Vector3.Zero));
        Assert.Null(graph.Plan(0x40400999u, Vector3.Zero, S3RoomKit.A, Vector3.Zero));

        var leaning = new RoomGraph(S3RoomKit.Rooms(leaning: true));
        Assert.Null(leaning.Plan(S3RoomKit.A, Vector3.Zero, S3RoomKit.C, Vector3.Zero)); // lean 0: no walk
        Assert.Null(leaning.FindPath(S3RoomKit.A, S3RoomKit.B));
        Assert.NotNull(new RoomGraph(S3RoomKit.Rooms(leaning: true), 0.3f).FindPath(S3RoomKit.A, S3RoomKit.B));
    }

    [Fact]
    public void ExitsToOutside_finds_the_outside_portal_with_its_sill_and_outward_normal()
    {
        var graph = new RoomGraph(S3RoomKit.Rooms());
        var exit = Assert.Single(graph.ExitsToOutside);
        Assert.Equal(S3RoomKit.A, exit.CellId);
        Assert.Equal(1, exit.PolygonId);
        Assert.Equal(96f, exit.Sill.X, Tolerance);
        Assert.Equal(91f, exit.Sill.Y, Tolerance);
        Assert.Equal(0f, exit.Sill.Z, Tolerance);
        Assert.Equal(0f, exit.Outward.X, Tolerance);
        Assert.Equal(-1f, exit.Outward.Y, Tolerance);
        Assert.Same(graph.ExitsToOutside, graph.ExitsToOutside); // computed once
    }

    [Fact]
    public void ExitsToOutside_obeys_the_lean_and_lists_none_without_an_outside_portal()
    {
        Assert.Empty(new RoomGraph(S3RoomKit.Rooms(leaning: true)).ExitsToOutside);
        var leaning = Assert.Single(new RoomGraph(S3RoomKit.Rooms(leaning: true), 0.3f).ExitsToOutside);
        Assert.Equal(S3RoomKit.A, leaning.CellId);
        Assert.Equal(-1f, leaning.Outward.Y, Tolerance);
        Assert.Empty(new RoomGraph(S3RoomKit.Rooms(withOutside: false)).ExitsToOutside);
    }

    [Fact]
    public void TryJoin_matches_the_tiles_doorway_lane_by_entry_cell_and_sill()
    {
        var tile = DoorTile(2f);
        var lane = Assert.Single(tile.DoorwayLanes);
        var graph = new RoomGraph(S3RoomKit.Rooms());
        Assert.True(graph.TryJoin(tile, S3RoomKit.A, out var joined, out var exit));
        Assert.Same(lane, joined);
        Assert.Equal(S3RoomKit.A, exit.CellId);
        Assert.True(graph.TryJoin(tile, S3RoomKit.A, out joined));
        Assert.Same(lane, joined);
        Assert.True(graph.TryExit(lane, out var same));
        Assert.Same(exit, same);
        Assert.Equal(lane.EntryCellId, exit.CellId);
        Assert.Equal(lane.Sill.X, exit.Sill.X, Tolerance);
        Assert.Equal(lane.Sill.Y, exit.Sill.Y, Tolerance);
        Assert.True(Vector2.Dot(lane.Outward, exit.Outward) > 0.99f);
    }

    [Fact]
    public void TryJoin_refuses_a_cell_without_an_exit_another_landblock_and_a_door_the_rooms_do_not_have()
    {
        var tile = DoorTile(2f);
        var graph = new RoomGraph(S3RoomKit.Rooms());
        Assert.False(graph.TryJoin(tile, S3RoomKit.B, out var lane)); // B has no outside portal
        Assert.Null(lane);
        Assert.False(graph.TryJoin(new NavTile(0x41400000u), S3RoomKit.A, out _)); // another landblock's tile
        Assert.False(new RoomGraph(S3RoomKit.Rooms(withOutside: false)).TryJoin(tile, S3RoomKit.A, out _)); // the rooms have no such door
        Assert.False(new RoomGraph(S3RoomKit.Rooms(leaning: true)).TryJoin(tile, S3RoomKit.A, out _)); // a human does not fit the leaning one at lean 0
        Assert.Empty(DoorTile(0.5f).DoorwayLanes); // a door too narrow has no lane (S1) and so nothing to join
        Assert.False(graph.TryJoin(DoorTile(0.5f), S3RoomKit.A, out _));
    }

    [Fact]
    public void An_outdoor_point_reaches_the_third_room_through_the_lane_and_two_doorways()
    {
        var tile = DoorTile(2f);
        var map = new NavMap();
        map.Attach(tile);
        var planner = new PathPlanner(map);
        var rooms = new RoomGraph(S3RoomKit.Rooms());
        var goal = new Vector3(96f, 116f, 0f);
        var result = IndoorOutdoorRoute.Plan(planner, rooms, NavPoint.At(tile.LandblockId, 96f, 60f), S3RoomKit.C, goal);

        Assert.Equal(IndoorOutdoorStatus.Found, result.Status);
        Assert.Null(result.Reason);
        Assert.Equal(NavStatus.Found, result.Outdoor!.Status);
        Assert.Same(tile.DoorwayLanes[0], result.Lane);
        Assert.Equal(S3RoomKit.A, result.Exit!.CellId);
        Assert.Equal([S3RoomKit.A, S3RoomKit.B, S3RoomKit.C], result.CellsEntered);

        // The outdoor part ends at the lane's outer end, 2.5 m out from the sill; the lane's points lead in to the sill.
        var lanePoints = result.LanePoints;
        Assert.True(lanePoints.Count >= 3);
        Assert.Equal(lanePoints[0].Local.X, result.Outdoor.Route!.End.Local.X, 0.6f);
        Assert.Equal(lanePoints[0].Local.Y, result.Outdoor.Route.End.Local.Y, 0.6f);
        Assert.Equal(88.5f, lanePoints[0].Local.Y, Tolerance);
        Assert.Equal(91f, lanePoints[^1].Local.Y, Tolerance);
        Assert.True(lanePoints.Zip(lanePoints.Skip(1)).All(p => p.First.Local.Y < p.Second.Local.Y)); // in toward the door
        Assert.All(lanePoints.Take(lanePoints.Count - 1), p => Assert.NotEqual(NavFlags.None, tile.Flags[NavTile.Index((int)p.Local.X, (int)p.Local.Y)] & NavFlags.DoorwayLane));

        // Inside: the lane's outdoor cells, the sill in the entry cell, the doorway pairs, the goal.
        var inside = result.Inside;
        Assert.Equal(lanePoints.Count - 1 + 1 + 4 + 1, inside.Count);
        Assert.True(inside[0].IsOutdoor);
        Assert.Equal(S3RoomKit.A, inside[lanePoints.Count - 1].CellId);
        Assert.Equal(S3RoomKit.C, inside[^1].CellId);
        Assert.Equal(goal, inside[^1].Origin);
    }

    [Fact]
    public void A_goal_the_rooms_cannot_reach_or_a_missing_tile_is_named_not_guessed()
    {
        var tile = DoorTile(2f);
        var map = new NavMap();
        map.Attach(tile);
        var planner = new PathPlanner(map);
        var from = NavPoint.At(tile.LandblockId, 96f, 60f);
        var unreachable = IndoorOutdoorRoute.Plan(planner, new RoomGraph(S3RoomKit.Rooms(leaning: true)), from, S3RoomKit.C, new Vector3(96f, 116f, 0f));
        Assert.Equal(IndoorOutdoorStatus.NoDoorway, unreachable.Status); // lean 0: the rooms' own exit is refused, so no lane joins
        Assert.Equal("no-doorway-lane-reaches-the-goal", unreachable.Reason);
        Assert.Null(unreachable.Lane);

        var leanResult = IndoorOutdoorRoute.Plan(planner, new RoomGraph(S3RoomKit.Rooms(leaning: true), 0.3f), from, S3RoomKit.C, new Vector3(96f, 116f, 0f));
        Assert.Equal(IndoorOutdoorStatus.Found, leanResult.Status); // the leaning door's sill centre is 0.22 m out of the tile's lane: the same door

        var noTile = IndoorOutdoorRoute.Plan(new PathPlanner(new NavMap()), new RoomGraph(S3RoomKit.Rooms()), from, S3RoomKit.C, Vector3.Zero);
        Assert.Equal(IndoorOutdoorStatus.NoTile, noTile.Status);
        Assert.Equal("no-tile", noTile.Reason);
    }

    /// <summary>The kit's tile: the house of <see cref="S1TileKit.HouseWithSouthDoor"/> with a south door of the given width, its lane cut.</summary>
    private static NavTile DoorTile(float doorWidth)
    {
        var (building, cell) = S1TileKit.HouseWithSouthDoor(doorWidth);
        return NavTileBuilder.Build(S1TileKit.Assemble([], [building], [cell]));
    }
}

using System.Numerics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// A portal of a room that leads outside and that a standing human fits through (<see cref="RoomGraph.ExitsToOutside"/>): the join
/// with a tile's <see cref="NavDoorwayLane"/> (<see cref="NavDoorwayLane.EntryCellId"/> is <see cref="CellId"/>).
/// </summary>
/// <param name="CellId">The EnvCell the portal is in (the cell behind the door).</param>
/// <param name="PolygonId">The portal's polygon id in the cell's structure.</param>
/// <param name="Sill">The portal's centre on its sill, landblock-local (the lane's <see cref="NavDoorwayLane.Sill"/> for the same door).</param>
/// <param name="Outward">The unit horizontal direction from the portal out of the building (the lane's <see cref="NavDoorwayLane.Outward"/>).</param>
public sealed record RoomExit(uint CellId, ushort PolygonId, Vector3 Sill, Vector2 Outward);

/// <summary>
/// The indoor half of the wayfinder (P5a, docs/wayfinding.md): one landblock's rooms as a graph whose nodes are cells and whose
/// vertices are the doorway pairs of <see cref="CellRoutePlanner"/> (<see cref="CellRoutePlanner.ApproachMetres"/> 0.7 m before a
/// portal, <see cref="CellRoutePlanner.PushIntoCellMetres"/> 0.5 m after it). One planner serves the Academy, the shops and the
/// Black Spawn Den: the den's lean (<c>maxLean</c> 0.3) is the only difference, and the den's own planner is gone (closes
/// <c>m3-fight-planner-lean</c>).
/// </summary>
/// <remarks>
/// <para><b>Plan.</b> <see cref="Plan"/> is a shortest-walk search (Dijkstra) over the doorway crossings: from the start point to a
/// doorway's approach point, through its pair, on to the next, to the goal point; every crossing also costs
/// <see cref="CrossingMetres"/> so that, of equal walks, the one with fewer doorways wins (the breadth-first rule the den's planner
/// had). It returns a <see cref="CellRoute"/>: the cells and the waypoint pairs; the caller walks to the goal point after the last.</para>
/// <para><b>The join.</b> A portal that leads outside (<see cref="CellPortalData.LeadsOutside"/>) is an <see cref="RoomExit"/>; the
/// tile's doorway lane for the same door (<see cref="NavTileBuilder"/> cuts one per building portal a human fits) is found by its
/// entry cell and its sill (<see cref="TryJoin(NavTile, uint, out NavDoorwayLane)"/>). <see cref="IndoorOutdoorRoute"/> composes
/// the outdoor planner, the lane and this graph.</para>
/// <para><c>TODO(room-steps)</c>: the graph knows no steps. The shop's inner doors rise about a metre from the entry cells' floor (Holtburg 0x13F's
/// portals to 0x140 sit at 95.0, its entries' at 94.0); whether that is stairs the human climbs is for the physics confirmation of the route (the runner) to say.</para>
/// <para>Not retail: Lifestone's own wayfinder; <see cref="CrossingMetres"/> and <see cref="JoinToleranceMetres"/> are its own.</para>
/// </remarks>
public sealed class RoomGraph
{
    /// <summary>What a doorway crossing costs beyond its length, in metres: of two equally long walks the one through fewer doorways wins. Not retail: Lifestone's own.</summary>
    public const float CrossingMetres = 0.5f;

    /// <summary>How far under 1.8 m the fit test probes a lintel by default (the shops' doors are 1.8 m tall). Not retail: Lifestone's own.</summary>
    public const float DefaultHeadSlackMetres = 0.05f;

    /// <summary>How far a lane's sill may lie from an exit's sill (horizontally) for them to be the same door. Not retail: Lifestone's own.</summary>
    public const float JoinToleranceMetres = 0.75f;

    /// <summary>How far inside an exit the indoor walk of <see cref="IndoorOutdoorRoute"/> starts, in metres. Not retail: Lifestone's own.</summary>
    public const float InsideStartMetres = 0.5f;

    private readonly LandblockCellSet _cells;
    private readonly CellRoutePlanner _planner;
    private IReadOnlyList<RoomExit>? _exits;

    /// <summary>A graph over <paramref name="cells"/>: the doorways a standing human fits, those leaning up to <paramref name="maxLean"/> too.</summary>
    /// <param name="cells">The cells walked over (a landblock's, or a subset: a doorway into a cell outside it is not walked).</param>
    /// <param name="maxLean">The largest vertical part of a doorway's normal accepted beyond the upright rule (0: upright only; the den uses 0.3).</param>
    /// <param name="headSlack">How far under the human's 1.8 m the fit test probes the lintel (<see cref="CellRoutePlanner.HeadSlack"/>).</param>
    public RoomGraph(LandblockCellSet cells, float maxLean = 0f, float headSlack = DefaultHeadSlackMetres)
    {
        ArgumentNullException.ThrowIfNull(cells);
        _cells = cells;
        _planner = new CellRoutePlanner(cells, maxLean, headSlack);
    }

    /// <summary>The cells planned over.</summary>
    public LandblockCellSet Cells => _cells;

    /// <summary>The lean accepted (see the constructor).</summary>
    public float MaxLean => _planner.MaxLean;

    /// <summary>The generalised planner under the graph: its doorway test and waypoints.</summary>
    public CellRoutePlanner Planner => _planner;

    /// <summary>The landblock id (0xXXYY0000) of the cells.</summary>
    public uint LandblockId => ((uint)_cells.Coord.X << 24) | ((uint)_cells.Coord.Y << 16);

    /// <summary>True when a standing human can walk through <paramref name="portal"/> of <paramref name="cell"/> (an inside portal; see <see cref="CellRoutePlanner.Fits"/>).</summary>
    public bool IsPassable(EnvCellData cell, CellPortalData portal) => _planner.IsWalkable(cell, portal);

    /// <summary>The cells of the walk with the fewest doorways from <paramref name="from"/> to <paramref name="to"/>, or null.</summary>
    public IReadOnlyList<uint>? FindPath(uint from, uint to) => _planner.FindPath(from, to);

    /// <summary>
    /// The shortest walk from <paramref name="from"/> in <paramref name="fromCell"/> to <paramref name="to"/> in <paramref name="toCell"/>
    /// (landblock-local points), or null when either cell is missing or no walkable doorways join them. The route's waypoints are the
    /// doorway pairs only (two per doorway, the first left out where the room is too thin); the goal point is the caller's last step.
    /// </summary>
    public CellRoute? Plan(uint fromCell, Vector3 from, uint toCell, Vector3 to)
    {
        if (_cells.GetCell(fromCell) is null || _cells.GetCell(toCell) is null)
        {
            return null;
        }

        if (fromCell == toCell)
        {
            return new CellRoute([fromCell], []);
        }

        var nodes = new List<Arrival> { new(fromCell, from, -1, 0f, null, null, IsGoal: false) };
        var best = new Dictionary<(uint Cell, int Via), float> { [(fromCell, -1)] = 0f };
        var queue = new PriorityQueue<int, float>();
        queue.Enqueue(0, 0f);
        var pairs = new Dictionary<(uint Cell, int Portal), (CellFrame? Approach, CellFrame Push)>();
        while (queue.TryDequeue(out var index, out _))
        {
            var node = nodes[index];
            if (node.IsGoal)
            {
                return Build(nodes, index);
            }

            if (node.Cost < best[(node.Cell, node.Via)] - 1e-4f)
            {
                continue; // a stale entry
            }

            if (node.Cell == toCell)
            {
                nodes.Add(new Arrival(node.Cell, to, index, node.Cost + Vector3.Distance(node.Point, to), null, null, IsGoal: true));
                queue.Enqueue(nodes.Count - 1, nodes[^1].Cost);
            }

            var cell = _cells.GetCell(node.Cell)!;
            for (var i = 0; i < cell.Portals.Count; i++)
            {
                var portal = cell.Portals[i];
                if (portal.LeadsOutside || _cells.GetCell(portal.OtherCellId) is null || !_planner.IsWalkable(cell, portal))
                {
                    continue;
                }

                if (!pairs.TryGetValue((cell.Id, i), out var pair))
                {
                    pair = _planner.DoorwayPair(cell, portal);
                    pairs[(cell.Id, i)] = pair;
                }

                var cost = node.Cost + CrossingMetres;
                var at = node.Point;
                if (pair.Approach is { } approach)
                {
                    cost += Vector3.Distance(at, approach.Origin);
                    at = approach.Origin;
                }

                cost += Vector3.Distance(at, pair.Push.Origin);
                var key = (portal.OtherCellId, Via: (int)((cell.Id & 0xFFFF) << 12) + i);
                if (best.TryGetValue(key, out var known) && known <= cost)
                {
                    continue;
                }

                best[key] = cost;
                nodes.Add(new Arrival(portal.OtherCellId, pair.Push.Origin, index, cost, pair.Approach, pair.Push, IsGoal: false, key.Via));
                queue.Enqueue(nodes.Count - 1, cost);
            }
        }

        return null;
    }

    /// <summary>
    /// The walk of <see cref="Plan"/> as a scripted route (the Black Spawn Den's legs): a pass point before and after each doorway
    /// (<see cref="CellRoutePlanner.PushIntoCellMetres"/> tolerance), then a stop at the middle of <paramref name="toCell"/>
    /// (<see cref="RoomFrame"/>) within <paramref name="stopWithin"/>.
    /// </summary>
    /// <param name="name">The route's name.</param>
    /// <param name="from">Where the walk starts.</param>
    /// <param name="toCell">The last cell.</param>
    /// <param name="stopWithin">The arrival radius of the stop, in metres.</param>
    /// <param name="stopDwellSeconds">How long the walker stands at the end, in seconds.</param>
    /// <returns>The route, or null when no walk joins the cells (or the last cell has no floor).</returns>
    public ScriptedRoute? PlanScripted(string name, in CellFrame from, uint toCell, float stopWithin, double stopDwellSeconds)
    {
        if (RoomFrame(toCell) is not { } target || Plan(from.CellId, from.Origin, toCell, target.Origin) is not { } route)
        {
            return null;
        }

        var goals = new List<RouteGoal>(route.Waypoints.Count + 1);
        foreach (var waypoint in route.Waypoints)
        {
            goals.Add(new RouteGoal(waypoint, CellRoutePlanner.PushIntoCellMetres, Stop: false, DwellSeconds: 0));
        }

        goals.Add(new RouteGoal(target, stopWithin, Stop: true, DwellSeconds: stopDwellSeconds));
        return new ScriptedRoute(name, goals) { Start = from };
    }

    /// <summary>The largest floor polygon's centre of the cell, as a frame in it (or null for a missing cell or one without a floor).</summary>
    /// <param name="cellId">The cell.</param>
    public CellFrame? RoomFrame(uint cellId)
    {
        if (_cells.GetCell(cellId) is not { } cell)
        {
            return null;
        }

        var floors = _cells.GetStructure(cell).Floors;
        if (floors.Count == 0)
        {
            return null;
        }

        var best = floors[0];
        foreach (var floor in floors)
        {
            if (floor.Area > best.Area)
            {
                best = floor;
            }
        }

        return new CellFrame(cell.Id, new AcFrame(cell.Frame.LocalToParent(best.Centroid), Quaternion.Identity));
    }

    /// <summary>
    /// The portals that lead outside and that a standing human fits through (<see cref="CellRoutePlanner.Fits"/>), in cell-id order:
    /// the cell, the sill point and the outward direction. Computed once.
    /// </summary>
    public IReadOnlyList<RoomExit> ExitsToOutside => _exits ??= FindExits();

    /// <summary>
    /// Finds the doorway lane of <paramref name="tile"/> that is the same door as one of the exits of <paramref name="entryCellId"/>
    /// (the lane's <see cref="NavDoorwayLane.EntryCellId"/>, its sill within <see cref="JoinToleranceMetres"/> of the exit's, the same
    /// way out). False when the tile is another landblock's, the cell has no exit here, or no lane matches.
    /// </summary>
    public bool TryJoin(NavTile tile, uint entryCellId, out NavDoorwayLane lane) => TryJoin(tile, entryCellId, out lane, out _);

    /// <summary><see cref="TryJoin(NavTile, uint, out NavDoorwayLane)"/> that also names the exit.</summary>
    public bool TryJoin(NavTile tile, uint entryCellId, out NavDoorwayLane lane, out RoomExit exit)
    {
        ArgumentNullException.ThrowIfNull(tile);
        lane = null!;
        exit = null!;
        if (tile.LandblockId != LandblockId)
        {
            return false;
        }

        var bestDistance = float.MaxValue;
        foreach (var candidate in tile.DoorwayLanes)
        {
            if (candidate.EntryCellId == entryCellId && TryExit(candidate, out var found, out var distance) && distance < bestDistance)
            {
                bestDistance = distance;
                (lane, exit) = (candidate, found);
            }
        }

        return lane is not null;
    }

    /// <summary>
    /// The exit of this graph that is the same door as <paramref name="lane"/>: in the lane's entry cell, its sill within
    /// <see cref="JoinToleranceMetres"/> of the lane's, the same way out. False when there is none.
    /// </summary>
    public bool TryExit(NavDoorwayLane lane, out RoomExit exit) => TryExit(lane, out exit, out _);

    private bool TryExit(NavDoorwayLane lane, out RoomExit exit, out float distance)
    {
        ArgumentNullException.ThrowIfNull(lane);
        exit = null!;
        distance = float.MaxValue;
        foreach (var e in ExitsToOutside)
        {
            if (e.CellId != lane.EntryCellId)
            {
                continue;
            }

            var d = Vector2.Distance(new Vector2(lane.Sill.X, lane.Sill.Y), new Vector2(e.Sill.X, e.Sill.Y));
            if (d <= JoinToleranceMetres && Vector2.Dot(lane.Outward, e.Outward) > 0.9f && d < distance)
            {
                (exit, distance) = (e, d);
            }
        }

        return exit is not null;
    }

    private RoomExit[] FindExits()
    {
        var exits = new List<RoomExit>();
        foreach (var id in _cells.Ids)
        {
            var cell = _cells.GetCell(id)!;
            foreach (var portal in cell.Portals)
            {
                if (portal.LeadsOutside && _planner.Fits(cell, portal) && _planner.DoorwayOf(cell, portal) is { } doorway)
                {
                    exits.Add(new RoomExit(id, portal.PolygonId, new Vector3(doorway.Centre.X, doorway.Centre.Y, doorway.Sill), new Vector2(doorway.Outward.X, doorway.Outward.Y)));
                }
            }
        }

        return [.. exits];
    }

    private static CellRoute Build(List<Arrival> nodes, int goal)
    {
        var chain = new List<Arrival>();
        for (var at = nodes[goal].Parent; at >= 0; at = nodes[at].Parent)
        {
            chain.Add(nodes[at]);
        }

        chain.Reverse();
        var cells = new List<uint>(chain.Count);
        var waypoints = new List<CellFrame>();
        foreach (var arrival in chain)
        {
            cells.Add(arrival.Cell);
            if (arrival.Approach is { } approach)
            {
                waypoints.Add(approach);
            }

            if (arrival.Push is { } push)
            {
                waypoints.Add(push);
            }
        }

        return new CellRoute(cells, waypoints);
    }

    /// <summary>One way of being in a cell: the point reached, the cost so far, the arrival before and the doorway pair crossed to get here.</summary>
    private sealed record Arrival(uint Cell, Vector3 Point, int Parent, float Cost, CellFrame? Approach, CellFrame? Push, bool IsGoal, int Via = -1);
}

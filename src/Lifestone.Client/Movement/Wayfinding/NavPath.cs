using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>What a <see cref="INavPathSource"/> plan came to.</summary>
public enum NavPathStatus
{
    /// <summary>A whole path to the goal.</summary>
    Found,

    /// <summary>A path to a horizon goal on the edge of the attached tiles (<see cref="NavStatus.Horizon"/>): not an arrival; plan again once more tiles are attached.</summary>
    Horizon,

    /// <summary>No path; <see cref="NavPath.Reason"/> names why (the planner's constant failure name).</summary>
    Failed,
}

/// <summary>
/// One point of a runner's path: a retail position (its cell, landblock-local origin, no rotation) and whether it is a doorway vertex
/// (a sill or a room graph's doorway pair: the runner walks within 3 m of one and never cuts past it).
/// </summary>
/// <param name="At">The position.</param>
/// <param name="Doorway">True for a doorway vertex.</param>
public readonly record struct NavWaypoint(CellFrame At, bool Doorway)
{
    /// <summary>True when the position is in an EnvCell (indoors: the room graph's, not the lattice's).</summary>
    public bool Indoor => !LandDefs.IsOutdoorCell(At.CellId);

    /// <summary>The position as a lattice point (global metres; an indoor point maps to its landblock's frame).</summary>
    public NavPoint Point => NavMap.FromCellFrame(At);
}

/// <summary>A path for the runner: the waypoints from the start (the body's position at the plan) to the goal, or why there is none.</summary>
/// <param name="Status">What the plan came to.</param>
/// <param name="Waypoints">The waypoints, start first (empty when it failed).</param>
/// <param name="Reason">A constant failure name, or null.</param>
/// <param name="CellsEntered">The indoor cells the path enters, in order (empty for an outdoor path).</param>
public sealed record NavPath(NavPathStatus Status, IReadOnlyList<NavWaypoint> Waypoints, string? Reason, IReadOnlyList<uint> CellsEntered)
{
    /// <summary>A failed plan named <paramref name="reason"/>.</summary>
    public static NavPath Fail(string reason) => new(NavPathStatus.Failed, [], reason, []);

    /// <summary>The waypoints as a <see cref="NavRoute"/> (global metres, arc length) for the runner's pursuit.</summary>
    internal NavRoute ToRoute()
    {
        var xy = new Vector2[Waypoints.Count];
        var z = new float[Waypoints.Count];
        for (var i = 0; i < Waypoints.Count; i++)
        {
            var p = Waypoints[i].Point;
            xy[i] = p.Global;
            z[i] = p.Z;
        }

        return new NavRoute(xy, z, Waypoints.Count);
    }
}

/// <summary>
/// Where the runner's paths come from (<see cref="NavRunner"/>): the outdoor planner (<see cref="OutdoorPathSource"/>), the outdoor
/// planner and a landblock's room graph (<see cref="IndoorOutdoorPathSource"/>), or the room graph alone
/// (<see cref="RoomPathSource"/>, the den). Game thread only, like the planner.
/// </summary>
public interface INavPathSource
{
    /// <summary>The lattice the runner clamps its lead point to and scars (null for a path source with no lattice: the rooms).</summary>
    NavMap? Map { get; }

    /// <summary>Plans from <paramref name="from"/> (the body's position) to <paramref name="goal"/>, avoiding <paramref name="scars"/>.</summary>
    NavPath Plan(in CellFrame from, NavGoal goal, NavScars scars);
}

/// <summary>
/// A path source over a room graph that can leave a cell out (the indoor analogue of a scar): the runner asks it when the physics refused
/// the crossing into that cell (<see cref="RouteConfirmer"/>), and the next plan goes round it. Kept for the source's life.
/// </summary>
public interface INavCellAvoider
{
    /// <summary>The cells left out.</summary>
    IReadOnlyCollection<uint> AvoidedCells { get; }

    /// <summary>Leaves <paramref name="cellId"/> out of the room graph; false when it is not one of the rooms or already left out.</summary>
    bool Avoid(uint cellId);
}

/// <summary>A landblock's rooms that can be rebuilt without some cells (<see cref="INavCellAvoider"/>).</summary>
internal sealed class AvoidableRooms
{
    private readonly LandblockCellSet? _cells;
    private readonly float _maxLean;
    private readonly float _headSlack;
    private readonly HashSet<uint> _avoided = [];

    public AvoidableRooms(RoomGraph rooms)
    {
        Graph = rooms ?? throw new ArgumentNullException(nameof(rooms));
    }

    public AvoidableRooms(LandblockCellSet cells, float maxLean, float headSlack)
    {
        _cells = cells ?? throw new ArgumentNullException(nameof(cells));
        _maxLean = maxLean;
        _headSlack = headSlack;
        Graph = new RoomGraph(cells, maxLean, headSlack);
    }

    public RoomGraph Graph { get; private set; }

    public IReadOnlyCollection<uint> Avoided => _avoided;

    public bool Avoid(uint cellId)
    {
        if (_cells is null || _cells.GetCell(cellId) is null || !_avoided.Add(cellId))
        {
            return false;
        }

        Graph = new RoomGraph(LandblockCellSet.FromCells(_cells.Structures, _cells.Coord, _cells.Cells.Where(c => !_avoided.Contains(c.Id))), _maxLean, _headSlack);
        return true;
    }
}

/// <summary>The outdoor planner as a path source: the route's pulled vertices, none a doorway.</summary>
/// <param name="planner">The planner over the attached tiles.</param>
public sealed class OutdoorPathSource(PathPlanner planner) : INavPathSource
{
    /// <summary>The planner.</summary>
    public PathPlanner Planner { get; } = planner ?? throw new ArgumentNullException(nameof(planner));

    /// <inheritdoc/>
    public NavMap? Map => Planner.Map;

    /// <inheritdoc/>
    public NavPath Plan(in CellFrame from, NavGoal goal, NavScars scars)
    {
        ArgumentNullException.ThrowIfNull(goal);
        var result = Planner.Plan(NavMap.FromCellFrame(from), goal, scars);
        if (result.Status is not (NavStatus.Found or NavStatus.Horizon) || result.Route is null)
        {
            return NavPath.Fail(result.Reason ?? "outdoor-" + result.Status.ToString().ToLowerInvariant());
        }

        return new NavPath(
            result.Status == NavStatus.Found ? NavPathStatus.Found : NavPathStatus.Horizon,
            [.. result.Route.Vertices.Select(v => new NavWaypoint(NavMap.ToCellFrame(v), Doorway: false))],
            null,
            []);
    }
}

/// <summary>
/// The outdoor planner and one landblock's room graph as a path source (<see cref="IndoorOutdoorRoute"/>): an outdoor start reaches a
/// room through a doorway lane (the lane's points, then the sill and the doorway pairs as doorway vertices, then the goal); a start
/// inside the rooms plans over the room graph alone; a goal without a cell of the rooms is an outdoor goal.
/// </summary>
/// <remarks>Made from the landblock's cell set, it can leave cells out (<see cref="INavCellAvoider"/>); made from a room graph, it cannot.</remarks>
public sealed class IndoorOutdoorPathSource : INavPathSource, INavCellAvoider
{
    private readonly OutdoorPathSource _outdoor;
    private readonly AvoidableRooms _rooms;

    /// <summary>A source over <paramref name="planner"/> and <paramref name="rooms"/> (no cell can be left out).</summary>
    /// <param name="planner">The outdoor planner (its map holds the rooms' tile).</param>
    /// <param name="rooms">The landblock's room graph.</param>
    public IndoorOutdoorPathSource(PathPlanner planner, RoomGraph rooms)
    {
        _outdoor = new OutdoorPathSource(planner);
        _rooms = new AvoidableRooms(rooms);
    }

    /// <summary>A source over <paramref name="planner"/> and a room graph of <paramref name="cells"/> that can leave cells out.</summary>
    /// <param name="planner">The outdoor planner (its map holds the rooms' tile).</param>
    /// <param name="cells">The landblock's cells.</param>
    /// <param name="maxLean">The room graph's lean.</param>
    /// <param name="headSlack">The room graph's head slack.</param>
    public IndoorOutdoorPathSource(PathPlanner planner, LandblockCellSet cells, float maxLean = 0f, float headSlack = RoomGraph.DefaultHeadSlackMetres)
    {
        _outdoor = new OutdoorPathSource(planner);
        _rooms = new AvoidableRooms(cells, maxLean, headSlack);
    }

    /// <summary>The room graph (rebuilt without the avoided cells).</summary>
    public RoomGraph Rooms => _rooms.Graph;

    /// <inheritdoc/>
    public IReadOnlyCollection<uint> AvoidedCells => _rooms.Avoided;

    /// <inheritdoc/>
    public bool Avoid(uint cellId) => _rooms.Avoid(cellId);

    /// <inheritdoc/>
    public NavMap? Map => _outdoor.Map;

    /// <inheritdoc/>
    public NavPath Plan(in CellFrame from, NavGoal goal, NavScars scars)
    {
        ArgumentNullException.ThrowIfNull(goal);
        if (goal.CellId is not { } toCell || Rooms.RoomFrame(toCell) is null)
        {
            return _outdoor.Plan(from, goal, scars);
        }

        if (!LandDefs.IsOutdoorCell(from.CellId))
        {
            return RoomPathSource.PlanRooms(Rooms, from, goal);
        }

        var result = IndoorOutdoorRoute.Plan(_outdoor.Planner, Rooms, NavMap.FromCellFrame(from), toCell, goal.Point.Local, scars);
        if (result.Status is not (IndoorOutdoorStatus.Found or IndoorOutdoorStatus.Horizon) || result.Outdoor?.Route is null)
        {
            return NavPath.Fail(result.Reason ?? "indoor-outdoor-" + result.Status.ToString().ToLowerInvariant());
        }

        var points = new List<NavWaypoint>();
        foreach (var v in result.Outdoor.Route.Vertices)
        {
            points.Add(new NavWaypoint(NavMap.ToCellFrame(v), Doorway: false));
        }

        if (result.Status == IndoorOutdoorStatus.Horizon)
        {
            return new NavPath(NavPathStatus.Horizon, points, null, []);
        }

        // Inside: the lane's points (outdoor cells), the sill (the entry cell), the doorway pairs, the goal (IndoorOutdoorRoute.Inside).
        var lane = result.LanePoints.Count - 1;
        for (var i = 0; i < result.Inside.Count; i++)
        {
            points.Add(new NavWaypoint(result.Inside[i], Doorway: i >= lane && i < result.Inside.Count - 1));
        }

        return new NavPath(NavPathStatus.Found, points, null, result.CellsEntered);
    }
}

/// <summary>The room graph alone as a path source (the den, a shop from inside): the start, the doorway pairs, the goal.</summary>
/// <remarks>Made from the landblock's cell set, it can leave cells out (<see cref="INavCellAvoider"/>); made from a room graph, it cannot.</remarks>
public sealed class RoomPathSource : INavPathSource, INavCellAvoider
{
    private readonly AvoidableRooms _rooms;

    /// <summary>A source over <paramref name="rooms"/> (no cell can be left out).</summary>
    public RoomPathSource(RoomGraph rooms)
    {
        _rooms = new AvoidableRooms(rooms);
    }

    /// <summary>A source over a room graph of <paramref name="cells"/> that can leave cells out.</summary>
    public RoomPathSource(LandblockCellSet cells, float maxLean = 0f, float headSlack = RoomGraph.DefaultHeadSlackMetres)
    {
        _rooms = new AvoidableRooms(cells, maxLean, headSlack);
    }

    /// <summary>The room graph (rebuilt without the avoided cells).</summary>
    public RoomGraph Rooms => _rooms.Graph;

    /// <inheritdoc/>
    public NavMap? Map => null;

    /// <inheritdoc/>
    public IReadOnlyCollection<uint> AvoidedCells => _rooms.Avoided;

    /// <inheritdoc/>
    public bool Avoid(uint cellId) => _rooms.Avoid(cellId);

    /// <inheritdoc/>
    public NavPath Plan(in CellFrame from, NavGoal goal, NavScars scars) => PlanRooms(Rooms, from, goal);

    /// <summary>The rooms' path from <paramref name="from"/> to <paramref name="goal"/>'s cell.</summary>
    internal static NavPath PlanRooms(RoomGraph rooms, in CellFrame from, NavGoal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        if (goal.CellId is not { } toCell)
        {
            return NavPath.Fail("rooms-goal-has-no-cell");
        }

        if (rooms.Plan(from.CellId, from.Origin, toCell, goal.Point.Local) is not { } route)
        {
            return NavPath.Fail("rooms-no-path");
        }

        var points = new List<NavWaypoint> { new(new CellFrame(from.CellId, new AcFrame(from.Origin, Quaternion.Identity)), Doorway: false) };
        points.AddRange(route.Waypoints.Select(w => new NavWaypoint(w, Doorway: true)));
        points.Add(new NavWaypoint(new CellFrame(toCell, new AcFrame(goal.Point.Local, Quaternion.Identity)), Doorway: false));
        return new NavPath(NavPathStatus.Found, points, null, route.Cells);
    }
}

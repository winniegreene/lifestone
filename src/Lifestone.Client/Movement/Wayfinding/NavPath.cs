using System.Globalization;
using System.Numerics;
using Lifestone.Client.Movement.Routes;
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
    /// <summary>
    /// M6 14 pits: on a take-off waypoint (a pit's brink), the jump the runner makes here; the next waypoint is the landing, and the step between them is a flight, not a walk (the confirmer does not sample it).
    /// Null for every other waypoint.
    /// </summary>
    public Lifestone.Client.Movement.Routes.DirectedJump? Jump { get; init; }

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
    /// <summary>The landblocks of the world route past the attached tiles, nearest first, when the plan followed one; else empty.</summary>
    public IReadOnlyList<uint> Corridor { get; init; } = [];

    /// <summary>On a failed plan with world routes: why they did not give a way on. Else null.</summary>
    public string? WorldRouteNote { get; init; }

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

    /// <summary>The outdoor planner the source plans with (the runner gives it the run's creature layer, <see cref="PathPlanner.Creatures"/>), or null for the rooms alone.</summary>
    PathPlanner? Planner { get; }

    /// <summary>Plans from <paramref name="from"/> (the body's position) to <paramref name="goal"/>, avoiding <paramref name="scars"/>.</summary>
    NavPath Plan(in CellFrame from, NavGoal goal, NavScars scars);
}

/// <summary>
/// A path source over a landblock's room graph (<see cref="RoomPathSource"/>, <see cref="IndoorOutdoorPathSource"/>): the runner gives the graph the run's
/// creature layer (<see cref="RoomGraph.Creatures"/>, nav-creatures-indoor) and runs straight through its rooms (nav-indoor-steering).
/// </summary>
public interface INavRoomSource
{
    /// <summary>The room graph the source plans the rooms with (rebuilt when a cell is left out, so read it at each plan).</summary>
    RoomGraph Rooms { get; }
}

/// <summary>
/// A path source over a room graph that can leave a cell out (the indoor analogue of a scar): the runner asks it when the physics refused
/// the crossing into that cell (<see cref="RouteConfirmer"/>), and the next plan goes round it. Kept for the source's life, which is one leg: the
/// journey makes a source per leg (<c>JourneyLegPlanner.SourceFor</c> from <c>JourneyRunLeg</c>, the wayfind check per leg too), and a room graph shared
/// across legs (a ground's, <c>RoomPathSource(RoomGraph)</c>) leaves nothing out. Since M5 item 5 fix F3 the confirmer judges the fixed world only, so a
/// refusal is never a server object's (a closed door, an NPC, a creature) and a cell is never left out because something stood in it for a while.
/// TODO(room-steps) stays: the refusals left are the static sills the graph knows no steps for.
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

    public AvoidableRooms(LandblockCellSet cells, float maxLean, float headSlack, CellRoutePlanner.Memo? memo = null)
    {
        _cells = cells ?? throw new ArgumentNullException(nameof(cells));
        _maxLean = maxLean;
        _headSlack = headSlack;
        Graph = new RoomGraph(cells, maxLean, headSlack, memo); // a graph without some cells (Avoid) is other cells: its own memo
    }

    public RoomGraph Graph { get; private set; }

    public IReadOnlyCollection<uint> Avoided => _avoided;

    public bool Avoid(uint cellId)
    {
        if (_cells is null || _cells.GetCell(cellId) is null || !_avoided.Add(cellId))
        {
            return false;
        }

        var old = Graph;
        Graph = new RoomGraph(LandblockCellSet.FromCells(_cells.Structures, _cells.Coord, _cells.Cells.Where(c => !_avoided.Contains(c.Id))), _maxLean, _headSlack)
        {
            Jumps = old.Jumps, // M6 14 pits: the jumps stay
            PlateList = old.PlateList, // M6 14 holding plates: the walls stay, and the jumps a plate fired on stay refused
            PlateSpeedMetresPerSecond = old.PlateSpeedMetresPerSecond,
        };
        foreach (var refused in old.RefusedPlateJumps)
        {
            Graph.RefusePlateJump(refused);
        }

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
        if (IndoorOutdoorRoute.DownTheStairs(Planner.Map, from) is { } down)
        {
            return IndoorOutdoorRoute.AfterStairs(down, Plan(down[^1].At, goal, scars));
        }

        var result = Planner.Plan(NavMap.FromCellFrame(from), goal, scars);
        if (result.Status is not (NavStatus.Found or NavStatus.Horizon) || result.Route is null)
        {
            return NavPath.Fail(result.Reason ?? "outdoor-" + result.Status.ToString().ToLowerInvariant()) with { WorldRouteNote = result.WorldRouteNote };
        }

        return new NavPath(
            result.Status == NavStatus.Found ? NavPathStatus.Found : NavPathStatus.Horizon,
            [.. result.Route.Vertices.Select(v => new NavWaypoint(NavMap.ToCellFrame(v), Doorway: false))],
            null,
            [])
        {
            Corridor = result.Corridor,
        };
    }
}

/// <summary>
/// The outdoor planner and one landblock's room graph as a path source (<see cref="IndoorOutdoorRoute"/>): an outdoor start reaches a
/// room through a doorway lane (the lane's points, then the sill and the doorway pairs as doorway vertices, then the goal); a start
/// inside the rooms plans over the room graph alone; a goal without a cell of the rooms is an outdoor goal. With a second landblock's rooms
/// (the start's, M6 4 W1, cross-landblock-rooms) a start in those rooms walks out through their door, outdoors, and in to the goal's room.
/// </summary>
/// <remarks>
/// Made from the landblock's cell set, it can leave cells out (<see cref="INavCellAvoider"/>); made from a room graph, it cannot. With two landblocks' rooms,
/// <see cref="Rooms"/> is the graph of the rooms the last plan started in (the start's rooms until a plan starts outside them), and each plan gives both graphs the
/// creature layer the runner set on it.
/// </remarks>
public sealed class IndoorOutdoorPathSource : INavPathSource, INavCellAvoider, INavRoomSource
{
    private readonly OutdoorPathSource _outdoor;
    private readonly AvoidableRooms _rooms;
    private readonly AvoidableRooms? _startRooms;
    private bool _inStartRooms;

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
    /// <param name="maxLean">The room graph's lean (the buildings' <see cref="RoomGraph.BuildingDoorwayLean"/> by default, M6 5 w).</param>
    /// <param name="headSlack">The room graph's head slack.</param>
    public IndoorOutdoorPathSource(PathPlanner planner, LandblockCellSet cells, float maxLean = RoomGraph.BuildingDoorwayLean, float headSlack = RoomGraph.DefaultHeadSlackMetres)
    {
        _outdoor = new OutdoorPathSource(planner);
        _rooms = new AvoidableRooms(cells, maxLean, headSlack);
    }

    /// <summary>
    /// M6 4 W1 (cross-landblock-rooms): a source over <paramref name="planner"/>, the goal's rooms <paramref name="cells"/> and the start's rooms
    /// <paramref name="startCells"/> in another landblock: a start in the start's rooms walks out through their door to the lane's outer end, the outdoor planner's route,
    /// then in to the goal's room (<see cref="IndoorOutdoorRoute.Plan"/>; a horizon while the goal's landblock has no tile). Both can leave cells out.
    /// </summary>
    /// <param name="planner">The outdoor planner.</param>
    /// <param name="cells">The goal's landblock's cells.</param>
    /// <param name="startCells">The start's landblock's cells (another landblock).</param>
    /// <param name="maxLean">The room graphs' lean.</param>
    /// <param name="headSlack">The room graphs' head slack.</param>
    public IndoorOutdoorPathSource(PathPlanner planner, LandblockCellSet cells, LandblockCellSet startCells, float maxLean = RoomGraph.BuildingDoorwayLean, float headSlack = RoomGraph.DefaultHeadSlackMetres)
        : this(planner, cells, maxLean, headSlack)
    {
        ArgumentNullException.ThrowIfNull(startCells);
        if (startCells.Coord == cells.Coord)
        {
            throw new ArgumentException("the start's rooms are the goal's landblock: use the one-landblock source", nameof(startCells));
        }

        _startRooms = new AvoidableRooms(startCells, maxLean, headSlack);
        _inStartRooms = true; // the leg starts in them (JourneyLegPlanner.SourceFor)
    }

    /// <summary>
    /// M6 14 hub plan: a source as <see cref="IndoorOutdoorPathSource(PathPlanner, LandblockCellSet, float, float)"/> over the cells, lean and head slack of <paramref name="rooms"/>, whose room graph answers from that memo
    /// (<see cref="CellRoutePlanner.Memo"/>): the work of the legs before it in the same landblock is not done again. It can leave cells out, as that source can (a graph without some cells is other cells, with a memo of its own).
    /// </summary>
    /// <param name="planner">The outdoor planner (its map holds the rooms' tile).</param>
    /// <param name="rooms">The memo of the landblock's cells.</param>
    public IndoorOutdoorPathSource(PathPlanner planner, CellRoutePlanner.Memo rooms)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        _outdoor = new OutdoorPathSource(planner);
        _rooms = new AvoidableRooms(rooms.Cells, rooms.MaxLean, rooms.HeadSlack, rooms);
    }

    /// <summary>
    /// M6 14 hub plan: a source as <see cref="IndoorOutdoorPathSource(PathPlanner, LandblockCellSet, LandblockCellSet, float, float)"/> whose two room graphs answer from <paramref name="rooms"/> and <paramref name="startRooms"/>
    /// (the goal's and the start's landblocks' memos, <see cref="CellRoutePlanner.Memo"/>).
    /// </summary>
    /// <param name="planner">The outdoor planner.</param>
    /// <param name="rooms">The memo of the goal's landblock's cells.</param>
    /// <param name="startRooms">The memo of the start's landblock's cells (another landblock).</param>
    public IndoorOutdoorPathSource(PathPlanner planner, CellRoutePlanner.Memo rooms, CellRoutePlanner.Memo startRooms)
        : this(planner, rooms)
    {
        ArgumentNullException.ThrowIfNull(startRooms);
        if (startRooms.Cells.Coord == rooms.Cells.Coord)
        {
            throw new ArgumentException("the start's rooms are the goal's landblock: use the one-landblock source", nameof(startRooms));
        }

        _startRooms = new AvoidableRooms(startRooms.Cells, startRooms.MaxLean, startRooms.HeadSlack, startRooms);
        _inStartRooms = true; // the leg starts in them (JourneyLegPlanner.SourceFor)
    }

    /// <summary>The room graph (rebuilt without the avoided cells): the goal's landblock's, or with the start's rooms of another landblock, the rooms the last plan started in.</summary>
    public RoomGraph Rooms => _inStartRooms && _startRooms is not null ? _startRooms.Graph : _rooms.Graph;

    /// <summary>The goal's landblock's room graph (<see cref="Rooms"/> unless the source has the start's rooms of another landblock).</summary>
    public RoomGraph GoalRooms => _rooms.Graph;

    /// <summary>The start's rooms of another landblock (M6 4 W1, cross-landblock-rooms), or null.</summary>
    public RoomGraph? StartRooms => _startRooms?.Graph;

    /// <summary>M6 8 M: the named ways out of rooms the room graph cannot leave (the travel file's <c>ways_out</c>); empty: none.</summary>
    public IReadOnlyList<RoomWayOut> WaysOut { get; init; } = [];

    /// <inheritdoc/>
    public IReadOnlyCollection<uint> AvoidedCells => _startRooms is null ? _rooms.Avoided : [.. _startRooms.Avoided, .. _rooms.Avoided];

    /// <inheritdoc/>
    public bool Avoid(uint cellId) =>
        _startRooms is not null && cellId >> 16 == _startRooms.Graph.LandblockId >> 16 ? _startRooms.Avoid(cellId) : _rooms.Avoid(cellId);

    /// <inheritdoc/>
    public NavMap? Map => _outdoor.Map;

    /// <inheritdoc/>
    public PathPlanner Planner => _outdoor.Planner;

    /// <summary>
    /// The failure name of a start inside the rooms from which no room route reaches a door with a lane outside (M4 P5b F1): a room behind a step
    /// the room graph does not know (Renald's back room 0xA9B40141, TODO(room-steps)) once the physics refused its ways out. Followed by
    /// <c>:0x&lt;cell&gt;</c>.
    /// </summary>
    public const string IndoorStartUnreachable = "indoor-start-unreachable";

    /// <summary>
    /// The failure name of a start in a room of another landblock than the source's rooms (M5 7 P7k) when the source was made without that landblock's rooms: since M6 4 W1 the
    /// journey's leg planner gives a walk between rooms of two landblocks both (<see cref="IndoorOutdoorPathSource(PathPlanner, LandblockCellSet, LandblockCellSet, float, float)"/>),
    /// so this names a source made for one landblock and asked from another. Followed by <c>:0x&lt;cell&gt;</c>.
    /// </summary>
    public const string IndoorStartOtherLandblock = "indoor-start-other-landblock";

    /// <inheritdoc/>
    public NavPath Plan(in CellFrame from, NavGoal goal, NavScars scars)
    {
        ArgumentNullException.ThrowIfNull(goal);
        if (_startRooms is not null)
        {
            // Two landblocks' rooms (M6 4 W1): the runner set its creature layer and its kept portals on the graph Rooms named; both take them, then Rooms follows the start.
            var layer = Rooms.Creatures;
            var keep = Rooms.KeepOff;
            _startRooms.Graph.Creatures = layer;
            _rooms.Graph.Creatures = layer;
            _startRooms.Graph.KeepOff = keep;
            _rooms.Graph.KeepOff = keep;
            _inStartRooms = !LandDefs.IsOutdoorCell(from.CellId) && from.CellId >> 16 == _startRooms.Graph.LandblockId >> 16;
        }

        var rooms = _rooms.Graph;
        uint? goalRoom = goal.CellId is { } c && rooms.RoomFrame(c) is not null ? c : null;
        if (goalRoom is not null && IndoorOutdoorRoute.DownTheStairs(_outdoor.Planner.Map, from) is { } down)
        {
            return IndoorOutdoorRoute.AfterStairs(down, Plan(down[^1].At, goal, scars));
        }

        if (!LandDefs.IsOutdoorCell(from.CellId))
        {
            if (_inStartRooms && _startRooms is not null)
            {
                // A start in the start's rooms of another landblock (M6 4 W1, cross-landblock-rooms): out through their door, then outdoors and in to the goal's room.
                return OrWayOut(PlanOut(_startRooms.Graph, from, goal, goalRoom, scars), from, goal, scars);
            }

            // A start indoors (M4 P5b F1: the outdoor planner refused it as start-blocked): the rooms alone when they join the goal's room, else out
            // through a door to the outdoor goal, or out and in again to a room the rooms do not join.
            if (goalRoom is not null && RoomPathSource.PlanRooms(rooms, from, goal) is { Status: NavPathStatus.Found } inside)
            {
                return inside;
            }

            return OrWayOut(PlanOut(rooms, from, goal, goalRoom, scars), from, goal, scars);
        }

        if (goalRoom is not { } toCell)
        {
            return _outdoor.Plan(from, goal, scars);
        }

        // A goal room whose landblock has no tile yet (M6 4 W1, m6-walk-goal-tile): a horizon toward its land point, the rooms once the tile attaches.
        var result = IndoorOutdoorRoute.Plan(_outdoor.Planner, rooms, NavMap.FromCellFrame(from), toCell, goal.Point.Local, scars);
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
            return new NavPath(NavPathStatus.Horizon, points, null, []) { Corridor = result.Outdoor.Corridor };
        }

        // Inside: the lane's points (outdoor cells), the sill (the entry cell), the doorway pairs, the goal (IndoorOutdoorRoute.Inside).
        var lane = result.LanePoints.Count - 1;
        for (var i = 0; i < result.Inside.Count; i++)
        {
            points.Add(new NavWaypoint(result.Inside[i], Doorway: i >= lane && i < result.Inside.Count - 1));
        }

        return new NavPath(NavPathStatus.Found, points, null, result.CellsEntered);
    }

    /// <summary>
    /// M6 8 M: what a start the rooms cannot leave does next (<paramref name="planned"/> failed: no door the rooms reach, or a door whose lane the outdoor planner cannot leave): the travel file's named way out of its room (<see cref="WaysOut"/>), then the plan from that way's end, so every walk from there (a go, the walk home's first leg, a replan) leaves the building first;
    /// a room with no named way keeps the failure, which says so (<c>TODO(m6-room-way-out)</c>). A plan that found a way is <paramref name="planned"/> itself: the walks the room graph plans are unchanged. Not retail: Lifestone's own.
    /// </summary>
    private NavPath OrWayOut(NavPath planned, in CellFrame from, NavGoal goal, NavScars scars)
    {
        if (planned.Status != NavPathStatus.Failed)
        {
            return planned;
        }

        foreach (var way in WaysOut)
        {
            if (way.Rooms.Contains(from.CellId))
            {
                var rest = Plan(way.Way[^1], goal, scars);
                return rest.Status == NavPathStatus.Failed
                    ? rest with { Reason = string.Create(CultureInfo.InvariantCulture, $"way-out-{way.Key}-then:{rest.Reason}") }
                    : IndoorOutdoorRoute.AfterStairs(RoomWayOut.FromBody(way.Way, from, RoomWayOut.OnPointMetres), rest) with { CellsEntered = [.. way.Way.Select(w => w.CellId).Distinct(), .. rest.CellsEntered] };
            }
        }

        return planned.Reason?.StartsWith(IndoorStartUnreachable, StringComparison.Ordinal) == true
            ? planned with { Reason = planned.Reason + string.Create(CultureInfo.InvariantCulture, $"; TODO(m6-room-way-out): the travel file names no way out of 0x{from.CellId:X8}") }
            : planned;
    }

    /// <summary>
    /// From a start inside <paramref name="outRooms"/> out through a door (M4 P5b F1; <see cref="IndoorOutdoorRoute"/> walked backwards): for each doorway lane of the
    /// tile whose door the rooms join and reach from the start, the rooms' doorway pairs to the door, the point inside it, the sill, the lane's points
    /// out to its outer end, then the outdoor planner's route to an outdoor goal, or <see cref="IndoorOutdoorRoute.Plan"/>'s route in again to a room of the goal's
    /// rooms (the same rooms' when they did not join it, or since M6 4 W1 another landblock's: a horizon toward their land point while their tile is missing).
    /// The cheapest whole walk wins (a horizon walk only when no whole one). Not retail: Lifestone's own.
    /// </summary>
    private NavPath PlanOut(RoomGraph outRooms, in CellFrame from, NavGoal goal, uint? goalRoom, NavScars scars)
    {
        var planner = _outdoor.Planner;
        var unreachable = string.Create(CultureInfo.InvariantCulture, $"{IndoorStartUnreachable}:0x{from.CellId:X8}");
        if (outRooms.RoomFrame(from.CellId) is null)
        {
            // M5 7 P7k: a start in a room of another landblock than the rooms' (a room goal there) is a gap a one-landblock source cannot join, named on its own
            // (the leg planner gives such a walk both landblocks' rooms since M6 4 W1), not the unreachable room the journey recovers from at the lifestone.
            return NavPath.Fail(from.CellId >> 16 != outRooms.LandblockId >> 16
                ? string.Create(CultureInfo.InvariantCulture, $"{IndoorStartOtherLandblock}:0x{from.CellId:X8}: the start's room is not in landblock 0x{outRooms.LandblockId >> 16:X4}, the rooms planned (a source of one landblock's rooms)")
                : unreachable + ": the start is not one of the rooms");
        }

        if (!planner.Map.TryGetTile(outRooms.LandblockId, out var tile))
        {
            // The start's own landblock: the journey's settle gate waits for its tile before a leg plans (JourneySettleGate), so only a host without the gate meets this.
            return NavPath.Fail(string.Create(CultureInfo.InvariantCulture, $"no-tile:0x{outRooms.LandblockId:X8}: the start room's landblock has no tile"));
        }

        NavPath? best = null;
        var bestScore = float.MaxValue;
        string? lastOutside = null;
        foreach (var lane in tile.DoorwayLanes)
        {
            if (!outRooms.TryExit(lane, out var exit))
            {
                continue;
            }

            var inner = exit.Sill - new Vector3(exit.Outward.X * RoomGraph.InsideStartMetres, exit.Outward.Y * RoomGraph.InsideStartMetres, 0f);
            if (outRooms.Plan(from.CellId, from.Origin, lane.EntryCellId, inner) is not { } rooms)
            {
                continue;
            }

            var lanePoints = IndoorOutdoorRoute.LanePoints(tile, lane);
            if (lanePoints.Count < 2)
            {
                continue;
            }

            var points = new List<NavWaypoint> { new(new CellFrame(from.CellId, new AcFrame(from.Origin, Quaternion.Identity)), Doorway: false) };
            points.AddRange(rooms.Waypoints.Select(w => new NavWaypoint(w, Doorway: true)));
            points.Add(new NavWaypoint(new CellFrame(lane.EntryCellId, new AcFrame(inner, Quaternion.Identity)), Doorway: true));
            points.Add(new NavWaypoint(new CellFrame(lane.EntryCellId, new AcFrame(exit.Sill, Quaternion.Identity)), Doorway: true));
            for (var i = lanePoints.Count - 2; i >= 0; i--)
            {
                points.Add(new NavWaypoint(NavMap.ToCellFrame(lanePoints[i]), Doorway: false));
            }

            var cells = new List<uint>(rooms.Cells);
            NavPathStatus status;
            if (goalRoom is { } toCell)
            {
                var again = IndoorOutdoorRoute.Plan(planner, _rooms.Graph, lanePoints[0], toCell, goal.Point.Local, scars);
                if (again.Status is not (IndoorOutdoorStatus.Found or IndoorOutdoorStatus.Horizon) || again.Outdoor?.Route is null)
                {
                    lastOutside = again.Reason ?? "indoor-outdoor-" + again.Status.ToString().ToLowerInvariant();
                    continue;
                }

                points.AddRange(again.Outdoor.Route.Vertices.Skip(1).Select(v => new NavWaypoint(NavMap.ToCellFrame(v), Doorway: false)));
                status = again.Status == IndoorOutdoorStatus.Found ? NavPathStatus.Found : NavPathStatus.Horizon;
                if (status == NavPathStatus.Found)
                {
                    var laneIn = again.LanePoints.Count - 1;
                    for (var i = 0; i < again.Inside.Count; i++)
                    {
                        points.Add(new NavWaypoint(again.Inside[i], Doorway: i >= laneIn && i < again.Inside.Count - 1));
                    }

                    cells.AddRange(again.CellsEntered);
                }
            }
            else
            {
                var outside = planner.Plan(lanePoints[0], goal, scars);
                if (outside.Status is not (NavStatus.Found or NavStatus.Horizon) || outside.Route is null)
                {
                    lastOutside = outside.Reason ?? "outdoor-" + outside.Status.ToString().ToLowerInvariant();
                    continue;
                }

                points.AddRange(outside.Route.Vertices.Skip(1).Select(v => new NavWaypoint(NavMap.ToCellFrame(v), Doorway: false)));
                status = outside.Status == NavStatus.Found ? NavPathStatus.Found : NavPathStatus.Horizon;
            }

            var score = status == NavPathStatus.Horizon ? 1e6f : 0f; // a whole route is always better
            for (var i = 1; i < points.Count; i++)
            {
                score += Vector2.Distance(points[i - 1].Point.Global, points[i].Point.Global);
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = new NavPath(status, points, null, cells);
            }
        }

        return best ?? NavPath.Fail(lastOutside ?? unreachable + ": no room route from the start reaches a door with a lane outside");
    }
}

/// <summary>
/// M6 8 M: a way out of a building's rooms the room graph cannot leave (a stair round a floorless shaft, a door whose lane the outdoor tile stamps), from the travel file: <see cref="Rooms"/> are the rooms it serves, <see cref="Way"/> the points in order, its last in a room or outdoor cell the planner joins. The route confirmer (the physics) still checks it, as any plan.
/// </summary>
/// <param name="Key">The way's name in the file and the log.</param>
/// <param name="Rooms">The rooms it starts from.</param>
/// <param name="Way">The points in order.</param>
public sealed record RoomWayOut(string Key, IReadOnlySet<uint> Rooms, IReadOnlyList<CellFrame> Way)
{
    /// <summary>A body nearer than this to a point has passed it, in metres (Lifestone's own: the runner's own arrival tolerance).</summary>
    public const float PassedMetres = 1f;

    /// <summary>A way out keeps a point unless the body stands on it, in metres (Lifestone's own).</summary>
    public const float OnPointMetres = 0.1f;

    /// <summary>A named way as waypoints from where the body stands: the body first, then the way from the point nearest it on, a point within <paramref name="passedMetres"/> being passed (M6 8 B1, the rule of a file's way). So a body stopped on the way goes on from there.</summary>
    /// <param name="way">The points in order.</param>
    /// <param name="from">Where the body stands.</param>
    /// <param name="passedMetres">How near a point counts as passed; a way out keeps its points (<see cref="OnPointMetres"/>: the physics confirms it point to point, so a stair's samples stay the file's).</param>
    public static List<NavWaypoint> FromBody(IReadOnlyList<CellFrame> way, in CellFrame from, float passedMetres = PassedMetres)
    {
        ArgumentNullException.ThrowIfNull(way);
        var nearest = 0;
        var best = float.PositiveInfinity;
        for (var i = 0; i < way.Count; i++)
        {
            var d = Vector3.Distance(way[i].Frame.Origin, from.Frame.Origin);
            if (d < best)
            {
                best = d;
                nearest = i;
            }
        }

        var next = best <= passedMetres ? Math.Min(nearest + 1, way.Count - 1) : nearest;
        var points = new List<NavWaypoint> { new(from, false) };
        for (var i = next; i < way.Count; i++)
        {
            points.Add(new NavWaypoint(way[i], false));
        }

        return points;
    }
}

/// <summary>The room graph alone as a path source (the den, a shop from inside): the start, the doorway pairs, the goal.</summary>
/// <remarks>Made from the landblock's cell set, it can leave cells out (<see cref="INavCellAvoider"/>); made from a room graph, it cannot.</remarks>
public sealed class RoomPathSource : INavPathSource, INavCellAvoider, INavRoomSource
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
    public PathPlanner? Planner => null;

    /// <inheritdoc/>
    public IReadOnlyCollection<uint> AvoidedCells => _rooms.Avoided;

    /// <inheritdoc/>
    public bool Avoid(uint cellId) => _rooms.Avoid(cellId);

    /// <inheritdoc/>
    public NavPath Plan(in CellFrame from, NavGoal goal, NavScars scars) => PlanRooms(Rooms, from, goal);

    /// <summary>The rooms' path from <paramref name="from"/> to <paramref name="goal"/>'s cell (the goal's own creature left out of the graph's creature layer).</summary>
    internal static NavPath PlanRooms(RoomGraph rooms, in CellFrame from, NavGoal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        if (goal.CellId is not { } toCell)
        {
            return NavPath.Fail("rooms-goal-has-no-cell");
        }

        if (rooms.Plan(from.CellId, from.Origin, toCell, goal.Point.Local, goal.Guid ?? 0) is not { } route)
        {
            return NavPath.Fail("rooms-no-path");
        }

        var points = new List<NavWaypoint> { new(new CellFrame(from.CellId, new AcFrame(from.Origin, Quaternion.Identity)), Doorway: false) };
        points.AddRange(route.Waypoints.Select(w => new NavWaypoint(w, Doorway: true)));
        foreach (var jump in route.Jumps)
        {
            points[jump.TakeOff + 1] = points[jump.TakeOff + 1] with { Jump = jump.Jump }; // M6 14 pits: the start point comes first
        }

        points.Add(new NavWaypoint(new CellFrame(toCell, new AcFrame(goal.Point.Local, Quaternion.Identity)), Doorway: false));
        return new NavPath(NavPathStatus.Found, points, null, route.Cells);
    }
}

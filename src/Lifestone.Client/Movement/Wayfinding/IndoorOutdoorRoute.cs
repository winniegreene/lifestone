using System.Numerics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>What <see cref="IndoorOutdoorRoute.Plan"/> came to.</summary>
public enum IndoorOutdoorStatus
{
    /// <summary>A whole route: the outdoor part to a doorway lane's outer end, the lane, the rooms.</summary>
    Found,

    /// <summary>
    /// The outdoor part is a partial route to the edge of the attached tiles (<see cref="NavStatus.Horizon"/>), or (M6 4 W1, <c>m6-walk-goal-tile</c>) the rooms' landblock has
    /// no attached tile and the outdoor part leads toward the rooms' land point (<see cref="IndoorOutdoorRoute.LandPoint"/>); plan again when more tiles are attached.
    /// </summary>
    Horizon,

    /// <summary>No doorway lane of the tile joins a door of the rooms that leads to the goal cell (or the goal cell is not in the rooms).</summary>
    NoDoorway,

    /// <summary>A lane leads to the goal, but the outdoor planner found no route to its outer end (<see cref="IndoorOutdoorResult.Outdoor"/> says why).</summary>
    OutdoorFailed,
}

/// <summary>One composed route from an outdoor point to a room (<see cref="IndoorOutdoorRoute.Plan"/>).</summary>
/// <param name="Status">What the plan came to.</param>
/// <param name="Outdoor">The outdoor planner's result to the lane's outer end (null when no lane was tried).</param>
/// <param name="Lane">The doorway lane the route enters by (null unless a lane was chosen).</param>
/// <param name="Exit">The room graph's side of the same door.</param>
/// <param name="LanePoints">The lane's centre-line nodes from its outer end in to the sill, then the sill itself (outdoor points, landblock-local).</param>
/// <param name="Rooms">The rooms' route from the entry cell to the goal cell (null with no lane).</param>
/// <param name="Inside">Everything after the outdoor route as retail positions: the lane's points (outdoor cells), the sill and the rooms' doorway pairs (EnvCells), the goal.</param>
/// <param name="Reason">A constant failure name (never a guess), or null.</param>
public sealed record IndoorOutdoorResult(
    IndoorOutdoorStatus Status,
    NavPlanResult? Outdoor,
    NavDoorwayLane? Lane,
    RoomExit? Exit,
    IReadOnlyList<NavPoint> LanePoints,
    CellRoute? Rooms,
    IReadOnlyList<CellFrame> Inside,
    string? Reason)
{
    /// <summary>The planned cells entered, in order: the entry cell onward (empty when no rooms were planned).</summary>
    public IReadOnlyList<uint> CellsEntered => Rooms?.Cells ?? [];
}

/// <summary>
/// The composition of the wayfinder's two graphs (P5a, docs/wayfinding.md): from an outdoor point to an indoor cell. The
/// <see cref="PathPlanner"/> plans to the outer end of a doorway lane, the lane's nodes lead in to the sill, and the
/// <see cref="RoomGraph"/> plans from the lane's entry cell to the goal. This is how a route enters Renald's shop (0xA9B40141) and
/// Thelnoth's cellar (0xA9B40111) and closes the "no walk through the door" part of <c>m4-vendor-approach</c>.
/// </summary>
/// <remarks>
/// Of the tile's lanes, those whose entry cell the room graph joins (<see cref="RoomGraph.TryExit(NavDoorwayLane, out RoomExit)"/>)
/// and whose rooms reach the goal are tried; the cheapest whole walk wins (outdoor arc length, plus the lane, plus the rooms' length).
/// Not retail: Lifestone's own.
/// </remarks>
public static class IndoorOutdoorRoute
{
    /// <summary>The spacing of the points along a lane's centre line, in metres. Not retail: Lifestone's own.</summary>
    public const float LanePointMetres = 1f;

    /// <summary>How near a climb's point, across and in height, a raised start counts as on it (<see cref="DownTheStairs"/>), in metres. Not retail: Lifestone's own (half the Refuge's 6 m walk less a body).</summary>
    public const float OnStairsMetres = 2.5f;

    /// <summary>
    /// Plans from <paramref name="from"/> (outdoors) to <paramref name="to"/> (landblock-local) in <paramref name="toCell"/>, a cell of
    /// <paramref name="rooms"/>. When the tile of the rooms' landblock is not attached (the walk starts past the attached tiles), the plan is a
    /// horizon toward the rooms' land point (<see cref="PlanTowardRooms"/>, M6 4 W1), and the runner's horizon replan builds the rooms' route once the tile attaches.
    /// </summary>
    /// <param name="planner">The outdoor planner (its map holds the tiles).</param>
    /// <param name="rooms">The landblock's room graph.</param>
    /// <param name="from">The outdoor start.</param>
    /// <param name="toCell">The goal EnvCell.</param>
    /// <param name="to">The goal point in it, landblock-local.</param>
    /// <param name="scars">The run's scars, passed to the outdoor plan.</param>
    public static IndoorOutdoorResult Plan(PathPlanner planner, RoomGraph rooms, NavPoint from, uint toCell, Vector3 to, NavScars? scars = null)
    {
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(rooms);
        if (!planner.Map.TryGetTile(rooms.LandblockId, out var tile))
        {
            return PlanTowardRooms(planner, rooms, from, toCell, to, scars);
        }

        IndoorOutdoorResult? best = null;
        var bestScore = float.MaxValue;
        NavPlanResult? lastOutdoor = null;
        foreach (var lane in tile.DoorwayLanes)
        {
            if (!rooms.TryExit(lane, out var exit))
            {
                continue;
            }

            var start = exit.Sill - new Vector3(exit.Outward.X * RoomGraph.InsideStartMetres, exit.Outward.Y * RoomGraph.InsideStartMetres, 0f);
            if (rooms.Plan(lane.EntryCellId, start, toCell, to) is not { } inside)
            {
                continue;
            }

            var points = LanePoints(tile, lane);
            if (points.Count < 2)
            {
                continue;
            }

            var outdoor = planner.Plan(from, NavGoal.ToPoint(points[0]), scars);
            lastOutdoor = outdoor;
            if (outdoor.Status is not (NavStatus.Found or NavStatus.Horizon) || outdoor.Route is null)
            {
                continue;
            }

            var frames = new List<CellFrame>();
            for (var i = 0; i < points.Count - 1; i++)
            {
                frames.Add(NavMap.ToCellFrame(points[i]));
            }

            frames.Add(new CellFrame(lane.EntryCellId, new AcFrame(exit.Sill, Quaternion.Identity)));
            frames.AddRange(inside.Waypoints);
            frames.Add(new CellFrame(toCell, new AcFrame(to, Quaternion.Identity)));

            var score = outdoor.Route.ArcLength;
            for (var i = 1; i < frames.Count; i++)
            {
                score += Vector3.Distance(frames[i - 1].Origin, frames[i].Origin);
            }

            if (outdoor.Status == NavStatus.Horizon)
            {
                score += 1e6f; // a whole route is always better
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = new IndoorOutdoorResult(
                    outdoor.Status == NavStatus.Found ? IndoorOutdoorStatus.Found : IndoorOutdoorStatus.Horizon,
                    outdoor, lane, exit, points, inside, frames, null);
            }
        }

        if (best is not null)
        {
            return best;
        }

        return lastOutdoor is null
            ? Failed(IndoorOutdoorStatus.NoDoorway, null, "no-doorway-lane-reaches-the-goal")
            : Failed(IndoorOutdoorStatus.OutdoorFailed, lastOutdoor, lastOutdoor.Reason ?? "outdoor-" + lastOutdoor.Status.ToString().ToLowerInvariant());
    }

    /// <summary>
    /// M6 4 W1 (<c>m6-walk-goal-tile</c>; live, M6 item 1: a walk from Fort Tethana's lifestone in 0x2780 to Zisaha's room in 0x2481 failed <c>no-tile</c> at once): the rooms'
    /// landblock has no attached tile, so no doorway lane is known. Of the rooms' exits (<see cref="RoomGraph.ExitsToOutside"/>, the same doors the tile's lanes are cut for) whose
    /// rooms reach the goal, the one with the shortest straight walk (start to its land point, then the rooms) is planned toward: the outdoor planner's route to its land point
    /// (<see cref="LandPoint"/>, the outer end its lane will have), a horizon route to the attached edge while the point is past it. Always <see cref="IndoorOutdoorStatus.Horizon"/>
    /// (never an arrival): the result carries <see cref="IndoorOutdoorResult.Outdoor"/> and <see cref="IndoorOutdoorResult.Exit"/>, no lane and nothing inside; the runner plans
    /// again once the map has changed (<see cref="NavRunner.HorizonReplanMetres"/>), and with the tile attached the plan is <see cref="Plan"/>'s whole route. No retail planner
    /// exists for this (the player walked it); not retail: Lifestone's own. The physics confirms each route as before.
    /// </summary>
    private static IndoorOutdoorResult PlanTowardRooms(PathPlanner planner, RoomGraph rooms, NavPoint from, uint toCell, Vector3 to, NavScars? scars)
    {
        var candidates = new List<(float Score, RoomExit Exit, NavPoint Land)>();
        foreach (var exit in rooms.ExitsToOutside)
        {
            var start = exit.Sill - new Vector3(exit.Outward.X * RoomGraph.InsideStartMetres, exit.Outward.Y * RoomGraph.InsideStartMetres, 0f);
            if (rooms.Plan(exit.CellId, start, toCell, to) is not { } inside)
            {
                continue;
            }

            var land = LandPoint(rooms.LandblockId, exit);
            var score = Vector2.Distance(from.Global, land.Global) + Vector3.Distance(land.Local, exit.Sill);
            var at = start;
            foreach (var w in inside.Waypoints)
            {
                score += Vector3.Distance(at, w.Origin);
                at = w.Origin;
            }

            candidates.Add((score + Vector3.Distance(at, to), exit, land));
        }

        if (candidates.Count == 0)
        {
            return Failed(IndoorOutdoorStatus.NoDoorway, null, "no-doorway-lane-reaches-the-goal");
        }

        candidates.Sort((a, b) => a.Score.CompareTo(b.Score));
        NavPlanResult? last = null;
        foreach (var (_, exit, land) in candidates)
        {
            var outdoor = planner.Plan(from, NavGoal.ToPoint(land), scars);
            last = outdoor;
            if (outdoor.Status is NavStatus.Found or NavStatus.Horizon && outdoor.Route is not null)
            {
                return new IndoorOutdoorResult(IndoorOutdoorStatus.Horizon, outdoor, null, exit, [], null, [], null);
            }

            if (outdoor.Status == NavStatus.StartBlocked)
            {
                break; // the start, not the exit: every other exit fails the same
            }
        }

        return Failed(IndoorOutdoorStatus.OutdoorFailed, last, last!.Reason ?? "outdoor-" + last.Status.ToString().ToLowerInvariant());
    }

    /// <summary>
    /// An exit's land point: the outer end its doorway lane has (<see cref="NavBuildOptions.LaneLength"/> out from the sill along the outward direction, at the sill's height),
    /// in <paramref name="landblockId"/>'s frame. Known from the rooms alone, without the tile (<see cref="PlanTowardRooms"/>).
    /// </summary>
    /// <param name="landblockId">The rooms' landblock.</param>
    /// <param name="exit">The exit.</param>
    public static NavPoint LandPoint(uint landblockId, RoomExit exit)
    {
        ArgumentNullException.ThrowIfNull(exit);
        var along = exit.Outward * new NavBuildOptions().LaneLength;
        return new NavPoint(landblockId & 0xFFFF0000u, new Vector3(exit.Sill.X + along.X, exit.Sill.Y + along.Y, exit.Sill.Z));
    }

    /// <summary>
    /// The lane's centre line from its outer end in to the sill, a point every <see cref="LanePointMetres"/> (only where the lane's node
    /// under the point is walkable), then the sill itself, as outdoor points: the outer end first. Empty when no centre point is walkable. A raised door whose stairs turn aside
    /// walks its <see cref="NavDoorwayLane.Climb"/> instead (M6 7 A).
    /// </summary>
    public static IReadOnlyList<NavPoint> LanePoints(NavTile tile, NavDoorwayLane lane)
    {
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(lane);
        var points = new List<NavPoint>();
        if (lane.Climb is { } climb)
        {
            points.AddRange(climb.Select(p => new NavPoint(tile.LandblockId, p)));
            points.Add(new NavPoint(tile.LandblockId, lane.Sill));
            return points;
        }

        var members = new HashSet<int>(lane.Nodes);
        var sill = new Vector2(lane.Sill.X, lane.Sill.Y);
        for (var along = new NavBuildOptions().LaneLength; along > 0.2f; along -= LanePointMetres)
        {
            var at = sill + (lane.Outward * along);
            var (x, y) = NavTile.NodeAt(at);
            if (NavTile.InBounds(x, y) && members.Contains(NavTile.Index(x, y)) && tile.IsWalkable(x, y))
            {
                points.Add(new NavPoint(tile.LandblockId, new Vector3(at.X, at.Y, tile.Z[NavTile.Index(x, y)])));
            }
        }

        if (points.Count > 0)
        {
            points.Add(new NavPoint(tile.LandblockId, lane.Sill));
        }

        return points;
    }

    /// <summary>
    /// A start up a raised door's stairs or on its deck (M6 7 A): the walk down the nearest <see cref="NavDoorwayLane.Climb"/>, <paramref name="from"/> first and the lane's outer end last, or null for a start that
    /// is on neither (more than the step-up over the ground under it is not a place the tile knows: the tile is the ground, and a plan from the node below walks into the stairs). A goal back through that door
    /// is walked down and up again: correct, not short, and only a replan or a resume on the stairs meets it.
    /// </summary>
    /// <param name="map">The map holding the start's tile.</param>
    /// <param name="from">The body, outdoors.</param>
    public static IReadOnlyList<NavWaypoint>? DownTheStairs(NavMap map, in CellFrame from)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!LandDefs.IsOutdoorCell(from.CellId) || !map.TryGetTile(from.CellId & 0xFFFF0000u, out var tile) || tile.DoorwayLanes.TrueForAll(static l => l.Climb is null))
        {
            return null;
        }

        var (x, y) = NavTile.NodeAt(new Vector2(from.Origin.X, from.Origin.Y));
        if (!NavTile.InBounds(x, y) || from.Origin.Z - tile.Z[NavTile.Index(x, y)] <= new NavBuildOptions().StepUp)
        {
            return null;
        }

        var body = new Vector2(from.Origin.X, from.Origin.Y);
        IReadOnlyList<Vector3>? best = null;
        var bestIndex = 0;
        var bestDistance = OnStairsMetres;
        foreach (var lane in tile.DoorwayLanes)
        {
            if (lane.Climb is not { } climb)
            {
                continue;
            }

            for (var i = 0; i < climb.Count; i++)
            {
                var d = Vector2.Distance(body, new Vector2(climb[i].X, climb[i].Y));
                if (d < bestDistance && MathF.Abs(climb[i].Z - from.Origin.Z) <= OnStairsMetres)
                {
                    (best, bestIndex, bestDistance) = (climb, i, d);
                }
            }
        }

        if (best is null)
        {
            return null;
        }

        var points = new List<NavWaypoint> { new(new CellFrame(from.CellId, new AcFrame(from.Origin, Quaternion.Identity)), Doorway: false) };
        for (var i = bestIndex; i >= 0; i--)
        {
            points.Add(new NavWaypoint(NavMap.ToCellFrame(new NavPoint(tile.LandblockId, best[i])), Doorway: false));
        }

        return points;
    }

    /// <summary><paramref name="rest"/> planned from the stairs' foot, after the walk <paramref name="down"/> to it (<see cref="DownTheStairs"/>).</summary>
    public static NavPath AfterStairs(IReadOnlyList<NavWaypoint> down, NavPath rest)
    {
        ArgumentNullException.ThrowIfNull(down);
        ArgumentNullException.ThrowIfNull(rest);
        return rest.Status == NavPathStatus.Failed ? rest : rest with { Waypoints = [.. down, .. rest.Waypoints.Skip(1)] };
    }

    private static IndoorOutdoorResult Failed(IndoorOutdoorStatus status, NavPlanResult? outdoor, string reason) =>
        new(status, outdoor, null, null, [], null, [], reason);
}

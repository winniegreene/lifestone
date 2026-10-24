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

    /// <summary>The outdoor part is a partial route to the edge of the attached tiles (<see cref="NavStatus.Horizon"/>); plan again when more tiles are attached.</summary>
    Horizon,

    /// <summary>The rooms' landblock has no attached tile.</summary>
    NoTile,

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

    /// <summary>
    /// Plans from <paramref name="from"/> (outdoors) to <paramref name="to"/> (landblock-local) in <paramref name="toCell"/>, a cell of
    /// <paramref name="rooms"/>. The tile of the rooms' landblock must be attached to the planner's map.
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
            return Failed(IndoorOutdoorStatus.NoTile, null, "no-tile");
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
    /// The lane's centre line from its outer end in to the sill, a point every <see cref="LanePointMetres"/> (only where the lane's node
    /// under the point is walkable), then the sill itself, as outdoor points: the outer end first. Empty when no centre point is walkable.
    /// </summary>
    public static IReadOnlyList<NavPoint> LanePoints(NavTile tile, NavDoorwayLane lane)
    {
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(lane);
        var members = new HashSet<int>(lane.Nodes);
        var sill = new Vector2(lane.Sill.X, lane.Sill.Y);
        var points = new List<NavPoint>();
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

    private static IndoorOutdoorResult Failed(IndoorOutdoorStatus status, NavPlanResult? outdoor, string reason) =>
        new(status, outdoor, null, null, [], null, [], reason);
}

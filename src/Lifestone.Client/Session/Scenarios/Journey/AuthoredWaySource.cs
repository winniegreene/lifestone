using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 8 B1: a path the travel file names point by point (<see cref="TravelRequest.Way"/>), for a walk the room graph cannot plan: a door whose room has a floorless cell on the way (the Crater Pathway's door and stair). The runner follows it as any plan, and the route confirmer (the physics) still
/// checks it before a step is taken: a way the body cannot walk is the runner's own named failure, never a walk by luck. A replan starts from the point ahead of the body, so a body stopped on the way goes on from there.
/// </summary>
/// <param name="way">The points in order, the body's goal the last.</param>
internal sealed class AuthoredWaySource(IReadOnlyList<CellFrame> way) : INavPathSource
{
    /// <inheritdoc />
    public NavMap? Map => null;

    /// <inheritdoc />
    public PathPlanner? Planner => null;

    /// <inheritdoc />
    public NavPath Plan(in CellFrame from, NavGoal goal, NavScars scars)
    {
        var points = RoomWayOut.FromBody(way, from);
        return new NavPath(NavPathStatus.Found, points, null, points.Skip(1).Select(w => w.At.CellId).Distinct().ToList());
    }
}

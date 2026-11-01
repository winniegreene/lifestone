using Lifestone.Client.Interiors;
using Lifestone.Client.Navigation;
using Lifestone.Client.World;

namespace Lifestone.Client.Session.Grounds;

/// <summary>
/// What the loops ask of a hunting ground (M5 item 2 g2b, D46): which creatures are targets and where they count, where a teleport to the ground lands, and the first landblock a recall to it arrives in. Every answer is
/// the profile's; nothing here is one dungeon's.
/// </summary>
public static class GroundRules
{
    /// <summary>The weenie class ids of the ground's creatures whose role is <see cref="GroundRole.Target"/>, in the profile's order. Built per call: the callers keep the list.</summary>
    /// <param name="ground">The ground.</param>
    public static IReadOnlyList<uint> TargetWcids(this HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        return ground.Targets.Where(t => t.Role == GroundRole.Target).Select(t => t.Wcid).ToArray();
    }

    /// <summary>
    /// True when a creature of weenie class <paramref name="wcid"/> in <paramref name="cell"/> is a target of the loop: the ground's role for the wcid is target and the cell is walkable (a storey's cell that is not no-go).
    /// Allocates nothing.
    /// </summary>
    /// <param name="ground">The ground.</param>
    /// <param name="wcid">The creature's weenie class id.</param>
    /// <param name="cell">The creature's full cell id.</param>
    public static bool IsTarget(this HuntingGround ground, uint wcid, uint cell)
    {
        ArgumentNullException.ThrowIfNull(ground);
        return ground.RoleOf(wcid) == GroundRole.Target && ground.IsWalkable(cell);
    }

    /// <summary>The destination that teleports the player to the ground's arrival (the profile's cell, origin and heading), not a Places row.</summary>
    /// <param name="ground">The ground.</param>
    public static TeleportDestination ArrivalDestination(this HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        var arrival = ground.Arrival;
        return new TeleportDestination(
            ground.Name,
            [],
            new WorldLocation(ground.Name, new CellPosition(arrival.Cell, arrival.Origin), true, WorldLocations.HeadingFromQuaternion(arrival.HeadingW, arrival.HeadingZ)),
            $"hunting ground '{ground.Key}': the profile's arrival")
        {
            Interior = InteriorKind.Dungeon,
        };
    }

    /// <summary>The landblock (high word of the cell ids) the ground's arrival is in: where a recall to the ground lands.</summary>
    /// <param name="ground">The ground.</param>
    public static uint ArrivalLandblock(this HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        return ground.Arrival.Cell >> 16;
    }
}

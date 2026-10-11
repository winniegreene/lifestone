using Lifestone.Client.World;

namespace Lifestone.Client.Interiors;

/// <summary>
/// Walk mode inside EnvCells: keeps the eye a fixed height above the cell's walkable floor (docs/interiors.md §8).
/// </summary>
/// <remarks>
/// <para><b>Not retail physics.</b> Retail moves the player by its physics transition (sphere sweeps against the cell
/// BSPs, step-up and step-down by the Setup's heights, <c>CTransition</c>); Lifestone has no collision until M2. This
/// only follows the floor: after the camera's horizontal move, the eye's height becomes the highest walkable floor
/// polygon (<see cref="Lifestone.Data.World.Cells.CellStructure.Floors"/>, normal Z ≥ ACE <c>FloorZ</c>) under it, in the
/// viewer's cell or a neighbour behind one of its portals (stairs and ramps cross cell boundaries), that is no more
/// than <see cref="StepUpHeight"/> above the feet. Where no floor is found the height is kept.</para>
/// </remarks>
public sealed class CellWalker
{
    /// <summary>
    /// Highest floor step climbed at once. Placeholder until M2's physics (retail steps up by the player Setup's
    /// <c>step_up_height</c> inside its collision transition): enough for stair treads, not for walls.
    /// </summary>
    public const float StepUpHeight = 0.6f;

    private readonly InteriorRegistry _registry;

    public CellWalker(InteriorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>
    /// The eye for a walk step: <paramref name="eye"/> (new horizontal position, last eye height) moved onto the floor
    /// of <paramref name="cellId"/> (or a portal neighbour) at <paramref name="eyeHeight"/>; unchanged when no floor is
    /// found or the cell's landblock is not loaded.
    /// </summary>
    public GlobalPosition FollowFloor(GlobalPosition eye, uint cellId, float eyeHeight)
    {
        if (_registry.ForCell(cellId) is not { } block || block.Cells.GetCell(cellId) is not { } cell)
        {
            return eye;
        }

        var local = block.ToLocal(eye);
        var reach = local.Z - eyeHeight + StepUpHeight;
        var floor = CellQueries.FloorBelow(block.Cells, cell, local, reach);
        foreach (var portal in cell.Portals)
        {
            if (!portal.LeadsOutside && block.Cells.GetCell(portal.OtherCellId) is { } other
                && CellQueries.FloorBelow(block.Cells, other, local, reach) is { } z && (floor is null || z > floor))
            {
                floor = z;
            }
        }

        return floor is { } f ? eye with { Z = block.Origin.Z + f + eyeHeight } : eye;
    }
}

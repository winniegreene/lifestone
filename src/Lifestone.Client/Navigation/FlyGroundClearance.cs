using Lifestone.Client.World;
using Lifestone.Data.Terrain;

namespace Lifestone.Client.Navigation;

/// <summary>Which cell the viewer is in, as the interiors' cell tracker sees it (for the fly camera and the position HUD).</summary>
public interface IViewerCell
{
    /// <summary>The EnvCell the viewer is in, or 0 outdoors.</summary>
    uint CellId { get; }

    /// <summary>For an EnvCell: whether retail treats it as seen from outside (a building interior); null outdoors.</summary>
    bool? SeenOutside { get; }

    /// <summary>
    /// The highest point of the viewer's EnvCell (global z of its bounding box), or null outdoors or while the cell's
    /// landblock is not loaded yet.
    /// </summary>
    double? CellTop { get; }

    /// <summary>
    /// True when <paramref name="eye"/> lies in the viewer's EnvCell or in a cell the tracker's portal rules reach from it
    /// (its portal neighbours and its stab list): a step that ends there stays indoors. False outdoors.
    /// </summary>
    bool StaysIndoors(GlobalPosition eye);

    /// <summary>
    /// True when <paramref name="eye"/> lies within <paramref name="margin"/> metres of the bounding box of
    /// <paramref name="landblock"/>'s loaded cells; false when that landblock's interiors are not loaded.
    /// </summary>
    bool NearCells(LandblockCoord landblock, GlobalPosition eye, float margin);
}

/// <summary>
/// Fly mode's ground clearance: outdoors the free camera's eye stays at least <see cref="Clearance"/> above the exact
/// ground, while cells below the ground (dungeons, cellars) and the space around them stay reachable. This is the M1
/// stand-in for a player who cannot go under the terrain, not retail behaviour (retail has no free camera).
/// </summary>
/// <remarks>
/// <para>Terrain is drawn back-face culled, as retail draws it, so an eye under the ground outdoors sees the sky through
/// it; that is what the clamp prevents. The free camera can also pass through cell walls and floors, which retail's
/// physics never allows, and the tracker then reports no cell (outdoors). Clamping on that alone threw the eye out of
/// any dungeon that lies below its landblock's terrain (review finding: from the Underground City, 0x01E901AD, eye z
/// -10.3 under sea-level terrain, the camera ended skimming the sea at z 0.5). So, per fly step (<see cref="Apply"/>):</para>
/// <list type="number">
/// <item>A step that stays in the cells (<see cref="IViewerCell.StaysIndoors"/>: through a dungeon's portals, down a
/// cellar's stairs) is never clamped, and nothing ending at or above ground + <see cref="Clearance"/> is.</item>
/// <item><b>Dungeons.</b> Before the step the viewer is in a cell that is not seen_outside (retail's flag; every cell of
/// a dungeon landblock): leaving it through a wall or a floor keeps the eye where it is, and it then moves freely, under
/// or over the terrain, until it is back in the open air (at or above ground + <see cref="Clearance"/>) more than
/// <see cref="CellsMargin"/> from that landblock's cells (<see cref="FreeAround"/>). So it can fly back in, or on down
/// to a lower level through the space between cells (the Mines of Despair's terrain lies between its levels), and it is
/// never pushed up out of the ground it entered that way.</item>
/// <item><b>Cellars.</b> A town's cells are all seen_outside (all 138 of Holtburg's 0xA9B4, the 15 outside its buildings'
/// stab lists too: cellars, the stone room 0xA9B40183), so a cellar is told by its height: a cell whose top lies below ground + <see cref="Clearance"/> at the eye. Leaving one
/// through a wall or a floor keeps the eye where it is too, until it is back in the open air.</item>
/// <item><b>Building interiors</b> (the other seen_outside cells; their floors lie on the terrain, the Holtburg shop's at
/// z 94.00 on ground at 94.00): a step that leaves the cells below ground + <see cref="Clearance"/> goes no lower than
/// the eye already was, so the camera stops on the floor instead of sinking into the ground.</item>
/// <item>Anything else outdoors ending below ground + <see cref="Clearance"/> is lifted to it: open air, and an explicit
/// placement under the ground (<see cref="Placed"/>, e.g. <c>--interior-eye</c> on an outdoor cell), lifted on the next
/// step.</item>
/// </list>
/// </remarks>
public sealed class FlyGroundClearance
{
    /// <summary>The eye's minimum height above the ground outdoors (m).</summary>
    public const double Clearance = 0.5;

    /// <summary>How far from the bounding box of a dungeon's cells the eye must be, in the open air, to be outdoors again (m).</summary>
    public const float CellsMargin = 20f;

    private LandblockCoord? _freeAround;
    private bool _fromDungeon;

    /// <summary>
    /// The landblock whose dungeon or cellar cells the eye left (or is in), around which it moves freely; null when the
    /// clamp applies everywhere outdoors.
    /// </summary>
    public LandblockCoord? FreeAround => _freeAround;

    /// <summary>The camera was put somewhere new (teleport, start position): it is not "around the cells" it was in.</summary>
    public void Placed() => _freeAround = null;

    /// <summary>The eye height to use after a fly step from <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <param name="from">The eye before the step: where the cell tracker last placed the viewer (<paramref name="cell"/>).</param>
    /// <param name="to">The eye after the step.</param>
    /// <param name="groundFrom">The exact ground height under <paramref name="from"/>, if known.</param>
    /// <param name="groundTo">The exact ground height under <paramref name="to"/>, if known (unknown: no clamp).</param>
    /// <param name="cell">The viewer's cell; null without the interiors (always outdoors).</param>
    public double Apply(GlobalPosition from, GlobalPosition to, double? groundFrom, double? groundTo, IViewerCell? cell)
    {
        var cellId = cell?.CellId ?? 0u;
        var indoors = cell is not null && AcCoordinates.IsEnvCell(cellId);
        if (indoors)
        {
            var dungeon = cell!.SeenOutside == false;
            var cellar = !dungeon && cell.CellTop is { } top && groundFrom is { } above && top < above + Clearance;
            _freeAround = dungeon || cellar ? LandblockCoord.FromId(cellId) : null;
            _fromDungeon = dungeon;
        }

        if (groundTo is not { } ground)
        {
            return to.Z;
        }

        var lowest = ground + Clearance;
        if (to.Z >= lowest)
        {
            // Back in the open air: outdoors again, after a cellar at once, after a dungeon once away from its cells.
            if (!indoors && _freeAround is { } block && (!_fromDungeon || cell?.NearCells(block, to, CellsMargin) != true))
            {
                _freeAround = null;
            }

            return to.Z;
        }

        if (indoors && cell!.StaysIndoors(to))
        {
            return to.Z;
        }

        if (_freeAround is not null)
        {
            return to.Z;
        }

        return indoors ? Math.Max(to.Z, Math.Min(from.Z, lowest)) : lowest;
    }
}

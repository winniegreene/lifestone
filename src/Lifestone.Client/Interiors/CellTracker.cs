using System.Numerics;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Interiors;

/// <summary>How the tracker last decided the viewer's cell (for diagnostics).</summary>
public enum CellTransition
{
    /// <summary>Nothing changed.</summary>
    None,

    /// <summary>Set explicitly (start position, teleport).</summary>
    Placed,

    /// <summary>Moved through a cell portal into a neighbour or a cell of the stab list (retail-like).</summary>
    Portal,

    /// <summary>Entered a building's doorway cell from outdoors (retail-like).</summary>
    Doorway,

    /// <summary>Left through a portal to the outside, or no cell contains the eye any more.</summary>
    Outside,

    /// <summary>Found by a global point search after passing through a wall (free camera only, not retail).</summary>
    Search,
}

/// <summary>
/// Tracks which cell the viewer (the camera) is in: outdoors, or an EnvCell. This decides retail's whole render path
/// (<c>SmartBox::RenderNormalMode</c>: <c>LScape::draw</c> outdoors, <c>DrawInside</c> in an EnvCell), the world
/// ambient (<c>CellManager::ChangePosition</c>) and the streaming focus.
/// </summary>
/// <remarks>
/// <para><b>Retail</b> moves an object between cells during physics (<c>CObjCell::find_cell_list</c>; ACE
/// <c>Physics/Common/ObjCell.cs find_cell_list</c>): the candidates are the current cell plus what its
/// <c>find_transit_cells</c> adds (from an EnvCell: the neighbours behind its portals whose cell BSP the sphere
/// touches, and the outside when the sphere is near an outside portal, ACE <c>EnvCell.find_transit_cells</c>; from
/// outdoors: the doorway cells of the buildings in nearby land cells whose BSP the sphere touches, ACE
/// <c>BuildingObj.find_building_transit_cells</c> → <c>EnvCell.check_building_transit</c>), and the first EnvCell
/// candidate whose BSP contains the centre wins (<c>point_in_cell</c>), else the land cell.</para>
/// <para><b>Lifestone</b> treats the eye as a point and follows the same rules in <see cref="Update"/>: stay while the
/// current cell contains the eye; else take a portal neighbour or a stab-list cell that contains it
/// (<c>CEnvCell::find_visible_child_cell</c> order); leave through an outside portal the eye's path crossed. From
/// outdoors, when the eye's path crosses a building doorway polygon, take the doorway's cell or a cell of its stab list
/// that contains the eye. The free camera can also pass through walls, which retail's physics never allows; only then
/// (no portal rule applies) is every loaded cell searched, and a cell found that way must also enclose the eye
/// (<see cref="CellQueries.IsEnclosed"/>), because a cell BSP is convex and can reach outside a non-convex room.
/// Teleports and start positions set the cell directly (<see cref="Place"/>), as retail's position updates carry the
/// cell id (docs/interiors.md §3.2).</para>
/// </remarks>
public sealed class CellTracker
{
    private readonly InteriorRegistry _registry;
    private GlobalPosition? _previous;
    private bool _lost;

    public CellTracker(InteriorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>The viewer's EnvCell id, or 0 when outdoors.</summary>
    public uint CellId { get; private set; }

    /// <summary>The viewer's EnvCell record (known even before its landblock streams in), or null outdoors.</summary>
    public EnvCellData? Cell { get; private set; }

    public bool IsIndoors => CellId != 0;

    /// <summary>
    /// True when the viewer is in an EnvCell that is not seen_outside: retail draws no landscape, lights with 0.2 white
    /// ambient and releases the landscape (<c>CellManager::ChangePosition</c>).
    /// </summary>
    public bool IsInDungeonCell => Cell is { SeenOutside: false };

    /// <summary>How the last <see cref="Update"/> or <see cref="Place"/> changed the cell.</summary>
    public CellTransition LastTransition { get; private set; }

    /// <summary>Number of cell changes so far.</summary>
    public long Changes { get; private set; }

    /// <summary>
    /// Puts the viewer in a cell (or outdoors when <paramref name="cell"/> is null) at <paramref name="eye"/>, e.g. after
    /// a teleport. The cell record may come straight from the dat (<see cref="EnvCellData.Read"/>) before its landblock
    /// is streamed.
    /// </summary>
    public void Place(EnvCellData? cell, GlobalPosition eye)
    {
        SetCell(cell, CellTransition.Placed);
        _previous = eye;
        _lost = false;
    }

    /// <summary>
    /// A landblock's interiors were unloaded. If the viewer's cell is in it, the viewer jumped away without
    /// <see cref="Place"/> (a teleport that only moved the world and the camera): the viewer cannot be in an unloaded
    /// cell otherwise, because the streaming focus keeps the viewer's landblock. The next <see cref="Update"/> then
    /// starts from outdoors (and searches) instead of keeping the stale cell; a <see cref="Place"/> before it wins.
    /// </summary>
    public void LandblockUnloaded(LandblockCoord coord)
    {
        if (IsIndoors && LandblockCoord.FromId(CellId) == coord)
        {
            _lost = true;
        }
    }

    /// <summary>Follows the viewer to <paramref name="eye"/>; returns true when the cell changed.</summary>
    public bool Update(GlobalPosition eye)
    {
        var previous = _previous ?? eye;
        _previous = eye;
        LastTransition = CellTransition.None;
        var before = CellId;
        if (_lost)
        {
            // Jumped out of an unloaded cell (LandblockUnloaded): outdoors, unless a loaded cell holds the eye.
            _lost = false;
            SetCell(Search(eye), CellTransition.Search);
            if (CellId == 0)
            {
                LastTransition = CellTransition.Outside;
            }

            return CellId != before;
        }

        if (IsIndoors)
        {
            UpdateIndoors(previous, eye);
        }
        else
        {
            UpdateOutdoors(previous, eye);
        }

        return CellId != before;
    }

    private void UpdateIndoors(GlobalPosition previous, GlobalPosition eye)
    {
        var block = _registry.ForCell(CellId);
        if (block is null)
        {
            return; // the cell's landblock is not streamed yet (just teleported): keep the cell
        }

        var cell = block.Cells.GetCell(CellId);
        if (cell is null)
        {
            SetCell(null, CellTransition.Outside);
            return;
        }

        var local = block.ToLocal(eye);
        if (block.Contains(cell, local))
        {
            Cell = cell; // refresh the record (the placed one may have come from a separate read)
            return;
        }

        // CEnvCell::find_transit_cells: neighbours behind the portals; then find_visible_child_cell's stab list.
        foreach (var portal in cell.Portals)
        {
            if (!portal.LeadsOutside && block.Cells.GetCell(portal.OtherCellId) is { } other && block.Contains(other, local))
            {
                SetCell(other, CellTransition.Portal);
                return;
            }
        }

        foreach (var id in cell.VisibleCellIds)
        {
            if (block.Cells.GetCell(id) is { } visible && block.Contains(visible, local))
            {
                SetCell(visible, CellTransition.Portal);
                return;
            }
        }

        // Left through an outside portal (a doorway or window): outdoors, unless a doorway leads straight into another cell.
        var from = block.ToLocal(previous);
        var structure = block.Cells.GetStructure(cell);
        foreach (var portal in cell.Portals)
        {
            if (portal.LeadsOutside && structure.GetPortal(portal.PolygonId) is { } polygon)
            {
                var corners = polygon.Vertices.Select(cell.Frame.LocalToParent).ToArray();
                if (CellQueries.SegmentCrossesPolygon(from, local, corners))
                {
                    SetCell(null, CellTransition.Outside);
                    return;
                }
            }
        }

        // Through a wall (free camera only): search every loaded cell.
        SetCell(Search(eye), CellTransition.Search);
        if (CellId == 0)
        {
            LastTransition = CellTransition.Outside;
        }
    }

    private void UpdateOutdoors(GlobalPosition previous, GlobalPosition eye)
    {
        // check_building_transit: the doorway cells of nearby buildings. Retail adds a doorway's cell when the moving
        // sphere touches its BSP; the free camera must have come through the doorway polygon itself (a doorway cell's
        // BSP can reach outside its walls, and a fast camera can pass the doorway cell in one frame, hence the stab list).
        foreach (var block in _registry.All)
        {
            if (block.Buildings.Count == 0)
            {
                continue;
            }

            var local = block.ToLocal(eye);
            if (!block.BoundsContain(local, 1f))
            {
                continue;
            }

            var from = block.ToLocal(previous);
            foreach (var building in block.Buildings)
            {
                foreach (var doorway in building.Doorways)
                {
                    if (!CellQueries.SegmentCrossesPolygon(from, local, doorway.Vertices))
                    {
                        continue;
                    }

                    if (block.Cells.GetCell(doorway.Portal.OtherCellId) is { } entry && block.Contains(entry, local))
                    {
                        SetCell(entry, CellTransition.Doorway);
                        return;
                    }

                    foreach (var id in doorway.Portal.StabList)
                    {
                        if (block.Cells.GetCell(id) is { } cell && block.Contains(cell, local))
                        {
                            SetCell(cell, CellTransition.Doorway);
                            return;
                        }
                    }
                }
            }
        }

        // Through a wall, or down into a dungeon (free camera only).
        if (Search(eye) is { } found)
        {
            SetCell(found, CellTransition.Search);
        }
    }

    /// <summary>A loaded cell that contains and encloses the eye, or null.</summary>
    public EnvCellData? Search(GlobalPosition eye)
    {
        foreach (var block in _registry.All)
        {
            var local = block.ToLocal(eye);
            if (!block.BoundsContain(local))
            {
                continue;
            }

            foreach (var id in block.Cells.Ids)
            {
                if (block.Cells.GetCell(id) is { } cell && block.Contains(cell, local) && CellQueries.IsEnclosed(block.Cells, cell, local))
                {
                    return cell;
                }
            }
        }

        return null;
    }

    private void SetCell(EnvCellData? cell, CellTransition how)
    {
        var id = cell?.Id ?? 0u;
        if (id != CellId)
        {
            Changes++;
        }

        CellId = id;
        Cell = cell;
        LastTransition = how;
    }
}

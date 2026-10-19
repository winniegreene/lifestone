using System.Numerics;
using Lifestone.Client.Interiors;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Avatars;

/// <summary>
/// Moves a flying server object from cell to cell as its position crosses a cell boundary (M3 4.2 x2, item 3; closes <c>TODO(m3-bolt-cell-tracking)</c>). Retail's
/// <c>CPhysicsObj::UpdateObjectInternal</c> (0x005161B0) runs a transition for a moving object, and <c>CObjCell::find_cell_list</c> puts it in the cell its new position falls in; a server object here has no
/// transition (<c>TODO(physics)</c>, D24), so this asks the same question of the object's centre point, as the camera's <see cref="CellTracker"/> does for the eye: from an EnvCell, stay while the cell holds the
/// point, else the neighbour behind a portal or a cell of its stab list that does (<c>CEnvCell::find_transit_cells</c>, <c>find_visible_child_cell</c>), or the land cell when the step crossed an outside portal;
/// from outdoors, the doorway cell of a building whose doorway polygon the step crossed (<c>find_building_transit_cells</c>). Outdoor cells themselves follow the origin in <c>AvatarBody.SetFrame</c>. A step
/// through a wall keeps the cell (the server stops the bolt there). The sphere retail tests is the centre point here. Allocates nothing once its scratch has grown.
/// </summary>
public sealed class ProjectileCellFollower(InteriorRegistry registry) : IAvatarCellFollower
{
    private Vector3[] _corners = new Vector3[16];

    /// <summary>Cell changes made.</summary>
    public long Moves { get; private set; }

    /// <inheritdoc />
    public uint Follow(uint cell, Vector3 from, Vector3 to)
    {
        var next = Find(cell, from, to);
        if (next != cell)
        {
            Moves++;
        }

        return next;
    }

    private uint Find(uint cell, Vector3 from, Vector3 to)
    {
        var block = registry.ForCell(cell);
        if (block is null)
        {
            return cell;
        }

        if (CellIds.IsEnvCell(cell))
        {
            return FromEnvCell(block, cell, from, to);
        }

        return CellIds.IsLandCell(cell) ? FromOutdoors(block, cell, from, to) : cell;
    }

    private uint FromEnvCell(LandblockInteriors block, uint cellId, Vector3 from, Vector3 to)
    {
        if (block.Cells.GetCell(cellId) is not { } cell)
        {
            return cellId;
        }

        if (block.Contains(cell, to))
        {
            return cellId;
        }

        var portals = cell.Portals;
        for (var i = 0; i < portals.Count; i++)
        {
            if (!portals[i].LeadsOutside && block.Cells.GetCell(portals[i].OtherCellId) is { } other && block.Contains(other, to))
            {
                return other.Id;
            }
        }

        var visible = cell.VisibleCellIds;
        for (var i = 0; i < visible.Count; i++)
        {
            if (block.Cells.GetCell(visible[i]) is { } seen && block.Contains(seen, to))
            {
                return seen.Id;
            }
        }

        var structure = block.Cells.GetStructure(cell);
        for (var i = 0; i < portals.Count; i++)
        {
            if (portals[i].LeadsOutside && structure.GetPortal(portals[i].PolygonId) is { } polygon && CrossesOutsidePortal(cell, polygon, from, to))
            {
                return CellIds.LandCellAt(block.Coord, to.X, to.Y) ?? cellId;
            }
        }

        return cellId;
    }

    private bool CrossesOutsidePortal(EnvCellData cell, PortalPolygon polygon, Vector3 from, Vector3 to)
    {
        var vertices = polygon.Vertices;
        if (_corners.Length < vertices.Length)
        {
            _corners = new Vector3[vertices.Length * 2];
        }

        for (var i = 0; i < vertices.Length; i++)
        {
            _corners[i] = cell.Frame.LocalToParent(vertices[i]);
        }

        return CellQueries.SegmentCrossesPolygon(from, to, _corners.AsSpan(0, vertices.Length));
    }

    private uint FromOutdoors(LandblockInteriors block, uint cellId, Vector3 from, Vector3 to)
    {
        if (block.Buildings.Count == 0 || !block.BoundsContain(to, 1f))
        {
            return cellId;
        }

        for (var b = 0; b < block.Buildings.Count; b++)
        {
            var doorways = block.Buildings[b].Doorways;
            for (var d = 0; d < doorways.Count; d++)
            {
                var doorway = doorways[d];
                if (!CellQueries.SegmentCrossesPolygon(from, to, doorway.Vertices))
                {
                    continue;
                }

                if (block.Cells.GetCell(doorway.Portal.OtherCellId) is { } entry && block.Contains(entry, to))
                {
                    return entry.Id;
                }

                var stab = doorway.Portal.StabList;
                for (var s = 0; s < stab.Count; s++)
                {
                    if (block.Cells.GetCell(stab[s]) is { } inside && block.Contains(inside, to))
                    {
                        return inside.Id;
                    }
                }
            }
        }

        return cellId;
    }
}

/// <summary>Gives a flying server object the cell its new position falls in (<see cref="ProjectileCellFollower"/>); the points are in the landblock space of the cell.</summary>
public interface IAvatarCellFollower
{
    /// <summary>The cell of <paramref name="to"/> after a step from <paramref name="from"/> made in <paramref name="cell"/>; <paramref name="cell"/> itself when it does not change.</summary>
    uint Follow(uint cell, Vector3 from, Vector3 to);
}

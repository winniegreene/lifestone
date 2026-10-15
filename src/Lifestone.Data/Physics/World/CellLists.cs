// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Common/ObjCell.cs (find_cell_list) and
// Source/ACE.Server/Physics/Common/LandCell.cs (add_all_outside_cells, add_cell_block, add_outside_cell,
// check_add_cell_boundary) (AGPL-3.0)
// with changes: checked against the 11.4186 decompile and allocation-free (spans, the transition's cell array). Retail
// fixes (docs/third-party/physics.md): the sphere search marks the outside cells added (ACE does not), a cell whose
// landblock is not attached is still listed with its id (ACE drops it), add_cell_block crosses landblock edges (decision
// table row 6: ACE keeps to one landblock; PhysicsWorld reproduces ACE when asked), and the parts search starts from the
// first part's cell as retail's does (ACE repeats it for every part).

using System.Numerics;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics.World;

/// <summary>
/// The retail cell searches: which cells a set of spheres (<c>CObjCell::find_cell_list</c>) or a static object's parts
/// (<c>CPhysicsObj::find_bbox_cell_list</c>, <c>CLandCell::add_all_outside_cells</c>) reach.
/// </summary>
internal static class CellLists
{
    /// <summary>Retail <c>CObjCell::find_cell_list</c>'s cap on cylinder-spheres (10).</summary>
    private const int MaxCylSpheres = 10;

    /// <summary>
    /// Retail <c>CObjCell::find_cell_list(cell_array, check_cell, path)</c> (11.4186 0x0052B960, 11.6096 0x0052C570): the
    /// cells around the path's check position and its global spheres.
    /// </summary>
    public static void FindCellList(IPhysicsCellSource cells, CellArray cellArray, ref PhysCell? currCell, bool wantCurrCell, SpherePath path) =>
        FindCellList(cells, path.CheckPos, path.GlobalSphere.AsSpan(0, path.NumSphere), cellArray, ref currCell, wantCurrCell, path);

    /// <summary>
    /// Retail <c>CObjCell::find_cell_list(p, num_sphere, sphere, cell_array, curr_cell, path)</c> (11.4186 0x0052B4E0,
    /// 11.6096 0x0052C0F0): starting from <paramref name="position"/>'s cell (its EnvCell, or the outdoor cells under the
    /// spheres), every cell the spheres reach through portals, buildings and cell edges. With
    /// <paramref name="wantCurrCell"/>, <paramref name="currCell"/> becomes the cell that contains the first sphere's
    /// centre, an EnvCell winning over a land cell. With <see cref="CellArray.DoNotLoadCells"/> an indoor search keeps to
    /// the start cell's visible cells.
    /// </summary>
    public static void FindCellList(
        IPhysicsCellSource cells,
        in CellFrame position,
        ReadOnlySpan<PhysSphere> spheres,
        CellArray cellArray,
        ref PhysCell? currCell,
        bool wantCurrCell,
        SpherePath? path)
    {
        cellArray.Reset();
        var cellId = position.CellId;
        var visible = cellId != 0 ? cells.GetCell(cellId) : null;
        if ((cellId & 0xFFFF) >= 0x100)
        {
            if (path is not null)
            {
                path.HitsInteriorCell = true;
            }

            cellArray.Add(cellId, visible);
        }
        else
        {
            AddAllOutsideCells(cells, position, spheres, cellArray);
        }

        if (visible is null || spheres.Length == 0)
        {
            return;
        }

        for (var i = 0; i < cellArray.Count; i++)
        {
            cellArray.CellAt(i)?.FindTransitCells(cells, position, spheres, cellArray, path);
        }

        if (wantCurrCell)
        {
            currCell = null;
            for (var i = 0; i < cellArray.Count; i++)
            {
                var cell = cellArray.CellAt(i);
                if (cell is null)
                {
                    continue;
                }

                var localPoint = spheres[0].Center - LandDefs.GetBlockOffset(position.CellId, cell.Id);
                if (cell.PointInCell(localPoint))
                {
                    currCell = cell;
                    if ((cell.Id & 0xFFFF) >= 0x100)
                    {
                        if (path is not null)
                        {
                            path.HitsInteriorCell = true;
                        }

                        break;
                    }
                }
            }
        }

        if (cellArray.DoNotLoadCells && (position.CellId & 0xFFFF) >= 0x100 && visible is PhysEnvCell start)
        {
            var index = 0;
            while (index < cellArray.Count)
            {
                var id = cellArray.IdAt(index);
                if (id == start.Id || start.IsVisible(id))
                {
                    index++;
                }
                else
                {
                    cellArray.RemoveAt(index);
                }
            }
        }
    }

    /// <summary>
    /// Retail <c>CObjCell::find_cell_list(p, num_cylsphere, cylsphere, cell_array, path)</c> (11.4186 0x0052B9F0, 11.6096
    /// 0x0052C600): the cells a static object's cylinder-spheres reach, each searched as a sphere of its radius at its
    /// bottom centre (not scaled, as in retail); at most 10.
    /// </summary>
    public static void FindCellList(IPhysicsCellSource cells, in CellFrame position, ReadOnlySpan<PhysCylSphere> cylSpheres, CellArray cellArray)
    {
        var count = Math.Min(cylSpheres.Length, MaxCylSpheres);
        Span<PhysSphere> spheres = stackalloc PhysSphere[MaxCylSpheres];
        for (var i = 0; i < count; i++)
        {
            spheres[i] = new PhysSphere(position.Frame.LocalToGlobal(cylSpheres[i].LowPoint), cylSpheres[i].Radius);
        }

        PhysCell? ignored = null;
        FindCellList(cells, position, spheres[..count], cellArray, ref ignored, wantCurrCell: false, path: null);
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::find_bbox_cell_list</c> (11.4186 0x00510FC0, 11.6096 0x00511A90): starting from the object's
    /// cell, every cell its parts' boxes reach (<c>CPartArray::calc_cross_cells_static</c> asks each found cell in turn).
    /// </summary>
    public static void FindBboxCellList(IPhysicsCellSource cells, uint objectCellId, PhysCell? objectCell, ReadOnlySpan<PhysPart> parts, CellArray cellArray)
    {
        cellArray.Reset();
        cellArray.Add(objectCellId, objectCell);
        if (objectCell is null || parts.Length == 0)
        {
            return;
        }

        for (var i = 0; i < cellArray.Count; i++)
        {
            cellArray.CellAt(i)?.FindTransitCells(cells, parts, cellArray);
        }
    }

    /// <summary>
    /// Retail <c>CLandCell::add_all_outside_cells(p, num_sphere, sphere, cell_array)</c> (11.4186 0x00533630, 11.6096
    /// 0x00534370): once per search, the outdoor cell under each sphere and the neighbours its radius reaches into (across
    /// landblock edges); without spheres, the cell under the position.
    /// </summary>
    public static void AddAllOutsideCells(IPhysicsCellSource cells, in CellFrame position, ReadOnlySpan<PhysSphere> spheres, CellArray cellArray)
    {
        if (cellArray.AddedOutside)
        {
            return;
        }

        cellArray.AddedOutside = true;
        if (spheres.Length == 0)
        {
            var cellId = position.CellId;
            var origin = position.Frame.Origin;
            if (LandDefs.AdjustToOutside(ref cellId, ref origin) && LandDefs.TryGetLandCoord(cellId, out var x, out var y))
            {
                AddOutsideCell(cells, cellArray, x, y);
            }

            return;
        }

        foreach (var sphere in spheres)
        {
            var cellId = position.CellId;
            var point = sphere.Center;
            if (!LandDefs.AdjustToOutside(ref cellId, ref point))
            {
                break;
            }

            var cellX = Math.Floor(point.X / LandDefs.CellLength);
            var cellY = Math.Floor(point.Y / LandDefs.CellLength);
            var inCellX = (float)(point.X - (cellX * LandDefs.CellLength));
            var inCellY = (float)(point.Y - (cellY * LandDefs.CellLength));
            var minRadius = sphere.Radius;
            var maxRadius = (float)(LandDefs.CellLength - minRadius);
            if (LandDefs.TryGetLandCoord(cellId, out var x, out var y))
            {
                AddOutsideCell(cells, cellArray, x, y);
                CheckAddCellBoundary(cells, cellArray, inCellX, inCellY, x, y, maxRadius, minRadius);
            }
        }
    }

    /// <summary>
    /// Retail <c>CLandCell::add_all_outside_cells(num_parts, parts, cell_array)</c> (11.4186 0x00533360, 11.6096
    /// 0x005340A0): once per search, the block of outdoor cells around the first part's cell that the parts' boxes (or,
    /// for camera-facing parts, their spheres) cover, across landblock edges (<see cref="AddCellBlock"/>). Nothing when
    /// that cell's landblock is not attached.
    /// </summary>
    public static void AddAllOutsideCells(IPhysicsCellSource cells, ReadOnlySpan<PhysPart> parts, CellArray cellArray)
    {
        if (cellArray.AddedOutside)
        {
            return;
        }

        cellArray.AddedOutside = true;
        if (parts.Length == 0)
        {
            return;
        }

        var first = parts[0];
        var cellId = first.Position.CellId;
        var origin = first.Position.Frame.Origin;
        if (!LandDefs.AdjustToOutside(ref cellId, ref origin))
        {
            cellId = 0;
        }

        var cell = cellId != 0 ? cells.GetCell(cellId) : null;
        if (cell is null || !LandDefs.TryGetLandCoord(cellId, out var x, out var y))
        {
            return;
        }

        var low = (int)(cellId & 0xFFFF);
        var localX = (low - 1) >> 3;
        var localY = (low - 1) & 7;
        int minX = 0, minY = 0, maxX = 0, maxY = 0;
        foreach (var part in parts)
        {
            int partMinX, partMinY, partMaxX, partMaxY;
            if (part.Always2D)
            {
                var sphere = part.Shape.CellSearchSphere
                    ?? throw new InvalidDataException($"TODO(physics): {part.Shape} faces the camera but has neither a physics nor a drawing sphere (retail dereferences it).");
                var partOrigin = part.Position.Frame.Origin;
                partMinX = (int)Math.Floor((partOrigin.X - sphere.Radius) / LandDefs.CellLength);
                partMinY = (int)Math.Floor((partOrigin.Y - sphere.Radius) / LandDefs.CellLength);
                partMaxX = (int)Math.Floor((partOrigin.X + sphere.Radius) / LandDefs.CellLength);
                partMaxY = (int)Math.Floor((partOrigin.Y + sphere.Radius) / LandDefs.CellLength);
            }
            else
            {
                var box = BBox.LocalToGlobal(part.Shape.Bounds, part.Position, cell.Position);
                partMinX = (int)Math.Floor(box.Min.X / LandDefs.CellLength);
                partMinY = (int)Math.Floor(box.Min.Y / LandDefs.CellLength);
                partMaxX = (int)Math.Floor(box.Max.X / LandDefs.CellLength);
                partMaxY = (int)Math.Floor(box.Max.Y / LandDefs.CellLength);
            }

            minX = Math.Min(minX, partMinX - localX);
            minY = Math.Min(minY, partMinY - localY);
            maxX = Math.Max(maxX, partMaxX - localX);
            maxY = Math.Max(maxY, partMaxY - localY);
        }

        AddCellBlock(cells, minX + x, minY + y, x + maxX, y + maxY, cellArray);
    }

    /// <summary>
    /// Retail <c>CLandCell::add_cell_block</c> (11.4186 0x005331D0, 11.6096 0x00533F10): every outdoor cell of the lcoord
    /// rectangle that is on the map, whatever landblock it is in (decision table row 6).
    /// </summary>
    public static void AddCellBlock(IPhysicsCellSource cells, int minX, int minY, int maxX, int maxY, CellArray cellArray)
    {
        for (var i = minX; i <= maxX; i++)
        {
            for (var j = minY; j <= maxY; j++)
            {
                AddOutsideCell(cells, cellArray, i, j);
            }
        }
    }

    /// <summary>
    /// Retail <c>CLandCell::add_outside_cell</c> (11.4186 0x00532EC0, 11.6096 0x00533C00): the outdoor cell at lcoord
    /// (<paramref name="x"/>, <paramref name="y"/>) when it is on the map, with its cell when its landblock is attached.
    /// </summary>
    public static void AddOutsideCell(IPhysicsCellSource cells, CellArray cellArray, int x, int y)
    {
        if (x is < 0 or >= LandDefs.LandCoordsPerSide || y is < 0 or >= LandDefs.LandCoordsPerSide)
        {
            return;
        }

        var id = LandDefs.LandCoordToCellId(x, y);
        cellArray.Add(id, cells.GetCell(id));
    }

    /// <summary>
    /// Retail <c>CLandCell::check_add_cell_boundary</c> (11.4186 0x00533260, 11.6096 0x00533FA0): the neighbours of cell
    /// (<paramref name="x"/>, <paramref name="y"/>) that a sphere at (<paramref name="inCellX"/>, <paramref name="inCellY"/>)
    /// in it reaches: beyond <paramref name="inCellMax"/> or below <paramref name="inCellMin"/> on each axis, and the
    /// diagonal ones.
    /// </summary>
    private static void CheckAddCellBoundary(IPhysicsCellSource cells, CellArray cellArray, float inCellX, float inCellY, int x, int y, float inCellMax, float inCellMin)
    {
        if (inCellX > inCellMax)
        {
            AddOutsideCell(cells, cellArray, x + 1, y);
            if (inCellY > inCellMax)
            {
                AddOutsideCell(cells, cellArray, x + 1, y + 1);
            }

            if (inCellY < inCellMin)
            {
                AddOutsideCell(cells, cellArray, x + 1, y - 1);
            }
        }

        if (inCellX < inCellMin)
        {
            AddOutsideCell(cells, cellArray, x - 1, y);
            if (inCellY > inCellMax)
            {
                AddOutsideCell(cells, cellArray, x - 1, y + 1);
            }

            if (inCellY < inCellMin)
            {
                AddOutsideCell(cells, cellArray, x - 1, y - 1);
            }
        }

        if (inCellY > inCellMax)
        {
            AddOutsideCell(cells, cellArray, x, y + 1);
        }

        if (inCellY < inCellMin)
        {
            AddOutsideCell(cells, cellArray, x, y - 1);
        }
    }
}

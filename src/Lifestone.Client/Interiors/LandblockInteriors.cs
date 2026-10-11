using System.Numerics;
using Lifestone.Client.World;
using Lifestone.Data.Geometry;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Interiors;

/// <summary>
/// Everything the interiors need to know about one streamed landblock's EnvCells: the cells (an
/// <see cref="ICellSource"/> for the portal walk), its buildings' doorway polygons, retail's landblock stab list, and
/// landblock-space bounds for quick point queries. Immutable after construction; built on a worker.
/// </summary>
/// <remarks>
/// Every cell of the landblock is here as data (the viewer's cell is found among them, and a free camera can fly into
/// a dungeon); only <see cref="InteriorCellSets.CellsToDraw"/> of them get meshes (docs/interiors.md §2).
/// </remarks>
public sealed class LandblockInteriors
{
    private readonly Dictionary<uint, BoundingBox> _cellBounds;

    /// <param name="cells">Every EnvCell of the landblock.</param>
    /// <param name="buildings">Buildings with doorways and their doorway polygons.</param>
    /// <param name="buildingCellIds">Retail's landblock stab list (<see cref="InteriorCellSets.BuildingStabCells"/> of
    /// every building of the LandBlockInfo); null computes it from <paramref name="buildings"/>.</param>
    public LandblockInteriors(LandblockCellSet cells, IReadOnlyList<BuildingPortalGeometry> buildings, IReadOnlySet<uint>? buildingCellIds = null)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(buildings);
        Cells = cells;
        Buildings = buildings;
        BuildingCellIds = buildingCellIds ?? InteriorCellSets.BuildingStabCells(buildings.Select(b => b.Building));
        _cellBounds = new Dictionary<uint, BoundingBox>(cells.Count);
        var any = false;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var cell in cells.Cells)
        {
            var box = LandblockBounds(cell, cells.GetStructure(cell));
            _cellBounds.Add(cell.Id, box);
            min = Vector3.Min(min, box.Min);
            max = Vector3.Max(max, box.Max);
            any = true;
        }

        Bounds = any ? new BoundingBox(min, max) : BoundingBox.Empty;
        HasCells = any;
        Origin = AcCoordinates.LandblockOrigin(cells.Coord);
    }

    public LandblockCoord Coord => Cells.Coord;

    /// <summary>The landblock's EnvCells.</summary>
    public LandblockCellSet Cells { get; }

    /// <summary>Buildings that have doorways, with their doorway polygons in landblock space.</summary>
    public IReadOnlyList<BuildingPortalGeometry> Buildings { get; }

    /// <summary>Retail's landblock stab list: the cells its buildings' doorways can show (<c>CLandBlock::init_buildings</c>).</summary>
    public IReadOnlySet<uint> BuildingCellIds { get; }

    /// <summary>Landblock-space bounds of all cells (dungeon cells can lie far outside the 192 m square).</summary>
    public BoundingBox Bounds { get; }

    public bool HasCells { get; }

    /// <summary>The landblock's south-west corner in global metres.</summary>
    public GlobalPosition Origin { get; }

    /// <summary>A global position in this landblock's local frame.</summary>
    public Vector3 ToLocal(GlobalPosition position) =>
        new((float)(position.X - Origin.X), (float)(position.Y - Origin.Y), (float)(position.Z - Origin.Z));

    /// <summary>Landblock-space bounds of a cell (all its struct's vertices), or false when the cell is unknown.</summary>
    public bool TryGetCellBounds(uint cellId, out BoundingBox bounds) => _cellBounds.TryGetValue(cellId, out bounds);

    /// <summary>True when the landblock-local point lies within <see cref="Bounds"/> grown by <paramref name="margin"/>.</summary>
    public bool BoundsContain(Vector3 local, float margin = 0f) => HasCells && Inside(Bounds, local, margin);

    /// <summary>True when the landblock-local point lies within the cell's bounds grown by <paramref name="margin"/>.</summary>
    public bool CellBoundsContain(uint cellId, Vector3 local, float margin = 0f) =>
        _cellBounds.TryGetValue(cellId, out var box) && Inside(box, local, margin);

    /// <summary>Retail <c>CEnvCell::point_in_cell</c> with a bounds pre-check.</summary>
    public bool Contains(EnvCellData cell, Vector3 local) =>
        CellBoundsContain(cell.Id, local, 0.01f) && Cells.Contains(cell, local);

    private static bool Inside(BoundingBox box, Vector3 p, float margin) =>
        p.X >= box.Min.X - margin && p.Y >= box.Min.Y - margin && p.Z >= box.Min.Z - margin
        && p.X <= box.Max.X + margin && p.Y <= box.Max.Y + margin && p.Z <= box.Max.Z + margin;

    private static BoundingBox LandblockBounds(EnvCellData cell, CellStructure structure)
    {
        var b = structure.Bounds;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? b.Min.X : b.Max.X, (i & 2) == 0 ? b.Min.Y : b.Max.Y, (i & 4) == 0 ? b.Min.Z : b.Max.Z);
            var p = cell.Frame.LocalToParent(corner);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        return new BoundingBox(min, max);
    }
}

/// <summary>The landblocks whose interiors are loaded (main thread only).</summary>
public sealed class InteriorRegistry
{
    private readonly Dictionary<LandblockCoord, LandblockInteriors> _blocks = new();

    /// <summary>Incremented on every change (visibility caches key on it).</summary>
    public long Version { get; private set; }

    public int Count => _blocks.Count;

    public IEnumerable<LandblockInteriors> All => _blocks.Values;

    public void Add(LandblockInteriors interiors)
    {
        ArgumentNullException.ThrowIfNull(interiors);
        _blocks[interiors.Coord] = interiors;
        Version++;
    }

    public bool Remove(LandblockCoord coord)
    {
        if (!_blocks.Remove(coord))
        {
            return false;
        }

        Version++;
        return true;
    }

    public LandblockInteriors? Get(LandblockCoord coord) => _blocks.GetValueOrDefault(coord);

    /// <summary>The landblock of a cell id, if loaded.</summary>
    public LandblockInteriors? ForCell(uint cellId) => Get(LandblockCoord.FromId(cellId));

    /// <summary>The cell with a full id, if its landblock is loaded.</summary>
    public EnvCellData? GetCell(uint cellId) => ForCell(cellId)?.Cells.GetCell(cellId);
}

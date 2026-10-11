using System.Numerics;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;

namespace Lifestone.Data.World.Cells;

/// <summary>Source of EnvCells and their structures for the portal walk.</summary>
public interface ICellSource
{
    /// <summary>The cell with this full id, or null when it is not available (not loaded / does not exist).</summary>
    EnvCellData? GetCell(uint cellId);

    /// <summary>The struct of a cell.</summary>
    CellStructure GetStructure(EnvCellData cell);
}

/// <summary>
/// All EnvCells of one landblock with their structures: the unit wave 2 streams, and an <see cref="ICellSource"/>.
/// </summary>
/// <remarks>
/// Every portal, visible-cell and building stab id is landblock-relative (low 16 bits), so a landblock's cells form a
/// closed set: verified for all 3,409 landblocks with cells (every referenced id exists in the same landblock).
/// </remarks>
public sealed class LandblockCellSet : ICellSource
{
    private readonly Dictionary<uint, EnvCellData> _cells;
    private readonly CellStructureLibrary _structures;

    private LandblockCellSet(LandblockCoord coord, Dictionary<uint, EnvCellData> cells, CellStructureLibrary structures)
    {
        Coord = coord;
        _cells = cells;
        _structures = structures;
        Ids = cells.Keys.OrderBy(id => id).ToArray();
    }

    public LandblockCoord Coord { get; }

    /// <summary>EnvCell ids, ascending.</summary>
    public IReadOnlyList<uint> Ids { get; }

    public IReadOnlyCollection<EnvCellData> Cells => _cells.Values;

    public int Count => _cells.Count;

    public CellStructureLibrary Structures => _structures;

    /// <summary>Loads every EnvCell of a landblock (B-tree range, not LandBlockInfo.num_cells).</summary>
    /// <exception cref="InvalidDataException">A cell is malformed.</exception>
    public static LandblockCellSet Load(CellStructureLibrary structures, LandblockCoord coord)
    {
        ArgumentNullException.ThrowIfNull(structures);
        var dats = structures.Dats;
        var cells = new Dictionary<uint, EnvCellData>();
        foreach (var id in CellRecordReader.EnvCellIds(dats, coord))
        {
            var bytes = CellRecordReader.ReadCellBytes(dats, id)
                ?? throw new InvalidDataException($"EnvCell 0x{id:X8} listed in the B-tree but unreadable.");
            cells.Add(id, EnvCellData.Decode(id, bytes));
        }

        return new LandblockCellSet(coord, cells, structures);
    }

    /// <summary>Builds a set from cells decoded elsewhere (all must belong to <paramref name="coord"/>).</summary>
    public static LandblockCellSet FromCells(CellStructureLibrary structures, LandblockCoord coord, IEnumerable<EnvCellData> cells)
    {
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(cells);
        var map = new Dictionary<uint, EnvCellData>();
        foreach (var cell in cells)
        {
            if (LandblockCoord.FromId(cell.Id) != coord)
            {
                throw new ArgumentException($"Cell 0x{cell.Id:X8} is not in landblock {coord}.", nameof(cells));
            }

            map.Add(cell.Id, cell);
        }

        return new LandblockCellSet(coord, map, structures);
    }

    public EnvCellData? GetCell(uint cellId) => _cells.GetValueOrDefault(cellId);

    public CellStructure GetStructure(EnvCellData cell) => _structures.Get(cell);

    /// <summary>
    /// True when a landblock-local point lies inside a cell (retail <c>CEnvCell::point_in_cell</c>: the point is moved
    /// into the cell frame and tested against the cell BSP).
    /// </summary>
    public bool Contains(EnvCellData cell, Vector3 landblockPoint)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return GetStructure(cell).Bsp.Contains(cell.Frame.ParentToLocal(landblockPoint));
    }

    /// <summary>
    /// The cell containing a landblock-local point, or null. Search order follows retail
    /// <c>CEnvCell::find_visible_child_cell</c>: the hint cell, then its visible cells; then (not retail, which has
    /// no global search) every cell of the landblock in id order.
    /// </summary>
    public EnvCellData? FindCell(Vector3 landblockPoint, uint? hintCellId = null)
    {
        if (hintCellId is { } hint && GetCell(hint) is { } hintCell)
        {
            if (Contains(hintCell, landblockPoint))
            {
                return hintCell;
            }

            foreach (var id in hintCell.VisibleCellIds)
            {
                if (GetCell(id) is { } visible && Contains(visible, landblockPoint))
                {
                    return visible;
                }
            }
        }

        foreach (var id in Ids)
        {
            var cell = _cells[id];
            if (Contains(cell, landblockPoint))
            {
                return cell;
            }
        }

        return null;
    }

    /// <summary>
    /// Undirected portal adjacency inside the landblock: cell id to the ids of the cells its portals lead to (outside
    /// portals excluded). Both directions are included even where the data stores only one.
    /// </summary>
    public IReadOnlyDictionary<uint, IReadOnlyList<uint>> PortalAdjacency()
    {
        var adjacency = _cells.Keys.ToDictionary(id => id, _ => new SortedSet<uint>());
        foreach (var cell in _cells.Values)
        {
            foreach (var portal in cell.Portals)
            {
                if (portal.LeadsOutside || !adjacency.TryGetValue(portal.OtherCellId, out var other))
                {
                    continue;
                }

                adjacency[cell.Id].Add(portal.OtherCellId);
                other.Add(cell.Id);
            }
        }

        return adjacency.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<uint>)kv.Value.ToArray());
    }
}

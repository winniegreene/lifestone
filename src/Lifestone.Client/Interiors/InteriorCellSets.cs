using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Interiors;

/// <summary>
/// Which EnvCells of a streamed landblock get meshes (docs/interiors.md §2): retail's landblock stab list, plus the
/// whole landblock where the viewer is in a cell outside it.
/// </summary>
/// <remarks>
/// <para><b>Outdoors</b> retail loads only the cells its buildings can show: <c>CLandBlock::init_buildings</c>
/// (acclient.c:352114) calls <c>CBuildingObj::add_to_stablist</c> (719041) for every building, which merges each
/// doorway's stab list into the landblock's (<c>CBldPortal::add_to_stablist</c>, 362423), and
/// <c>CLandBlock::grab_visible_cells</c> (351601) loads exactly that list while the landblock is in the landscape grid
/// (<c>LScape</c> calls both for every landblock, acclient.c:306602-306603). A town's cellars and the dungeons under or
/// beside it are not loaded.</para>
/// <para><b>Indoors</b> <c>CEnvCell::grab_visible_cells</c> (350172) loads the viewer's cell and its stab list on every
/// cell change (and the landscape's list when the cell is seen_outside). Lifestone streams per landblock, so when the
/// viewer's cell or its stab list leaves the buildings' list it loads every cell of that landblock once: a superset of
/// any cell's stab list there, because every stab id is landblock-local (cells README).</para>
/// </remarks>
public static class InteriorCellSets
{
    /// <summary>
    /// Retail's landblock stab list: the union of the stab lists of every doorway of every building (and each
    /// doorway's own cell, which the cells track measured to be in its stab list for all 16,937 doorways).
    /// </summary>
    public static IReadOnlySet<uint> BuildingStabCells(IEnumerable<BuildingData> buildings)
    {
        ArgumentNullException.ThrowIfNull(buildings);
        var result = new HashSet<uint>();
        foreach (var building in buildings)
        {
            foreach (var portal in building.Portals)
            {
                result.Add(portal.OtherCellId);
                foreach (var id in portal.StabList)
                {
                    result.Add(id);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// True when the viewer's cell needs every cell of <paramref name="cells"/>' landblock: it is a cell of that
    /// landblock and it, or a cell of its stab list, is not in <paramref name="buildingCells"/> (a dungeon cell, a
    /// cellar, a room no building doorway shows).
    /// </summary>
    /// <param name="cells">The landblock's cells (all of them, as data).</param>
    /// <param name="buildingCells">The landblock's <see cref="BuildingStabCells"/>.</param>
    /// <param name="viewerCellId">The viewer's cell (0 outdoors).</param>
    public static bool NeedsAllCells(LandblockCellSet cells, IReadOnlySet<uint> buildingCells, uint viewerCellId)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(buildingCells);
        if (cells.GetCell(viewerCellId) is not { } viewer)
        {
            return false;
        }

        if (!buildingCells.Contains(viewer.Id))
        {
            return true;
        }

        foreach (var id in viewer.VisibleCellIds)
        {
            if (!buildingCells.Contains(id) && cells.GetCell(id) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The cells to mesh, ascending: every cell when <paramref name="all"/>, else the building cells that exist.</summary>
    public static IReadOnlyList<uint> CellsToDraw(LandblockCellSet cells, IReadOnlySet<uint> buildingCells, bool all)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(buildingCells);
        if (all)
        {
            return cells.Ids;
        }

        var result = new List<uint>(buildingCells.Count);
        foreach (var id in cells.Ids)
        {
            if (buildingCells.Contains(id))
            {
                result.Add(id);
            }
        }

        return result;
    }
}

using Lifestone.Client.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Interiors;

/// <summary>
/// Teleport targets for any dungeon landblock, from the cells track's discovery helpers (docs/interiors.md §6).
/// </summary>
/// <remarks>
/// Retail and ACE teleport to positions from the server's world database (portal destinations), which Lifestone does
/// not have offline. For a landblock that <c>CellDiscovery.Summarize</c> classifies as
/// <see cref="LandblockCellKind.Dungeon"/>, <see cref="CellDiscovery.FindDungeonEntry"/> picks a floor point (a
/// heuristic: the lowest cell with a floor in the largest portal-connected group); the curated
/// <see cref="InteriorLocations"/> use real portal drop points instead.
/// </remarks>
public static class DungeonEntries
{
    /// <summary>
    /// A spawn point inside the dungeon of <paramref name="coord"/>, or null when the landblock is not a dungeon (towns,
    /// mixed landblocks and open land keep their outdoor start).
    /// </summary>
    public static WorldLocation? Find(CellStructureLibrary structures, LandblockCoord coord)
    {
        ArgumentNullException.ThrowIfNull(structures);
        var dats = structures.Dats;
        if (CellDiscovery.EnumerateEnvCellIds(dats, coord).Count == 0)
        {
            return null;
        }

        var cells = LandblockCellSet.Load(structures, coord);
        var statics = LandblockStatics.Read(dats, coord);
        var summary = CellDiscovery.Summarize(cells, statics, dats.GetLandblockTerrain(coord), dats.TerrainRegion.LandHeightTable);
        if (summary.Kind != LandblockCellKind.Dungeon || CellDiscovery.FindDungeonEntry(cells, statics) is not { } entry)
        {
            return null;
        }

        return new WorldLocation($"Dungeon 0x{coord.X:X2}{coord.Y:X2}", new CellPosition(entry.CellId, entry.Position), true, 0.0);
    }
}

using System.Numerics;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;

namespace Lifestone.Data.World.Cells;

/// <summary>
/// Finding EnvCells, classifying landblocks (dungeons vs building interiors) and picking spawn points for teleports.
/// </summary>
public static class CellDiscovery
{
    /// <summary>
    /// The EnvCell ids that exist for a landblock, ascending, from a B-tree range walk of client_cell_1.dat
    /// (<c>0xXXYY0100-0xXXYYFFFD</c>). In end-of-retail data they are always contiguous from 0x0100.
    /// Prefer this to <see cref="EnvCellIdsFromNumCells"/>; see <see cref="LandblockStatics.NumCells"/>.
    /// </summary>
    public static IReadOnlyList<uint> EnumerateEnvCellIds(DatSet dats, LandblockCoord coord)
    {
        ArgumentNullException.ThrowIfNull(dats);
        return CellRecordReader.EnvCellIds(dats, coord);
    }

    /// <summary>
    /// The ids ACE <c>Physics/Common/Landblock.cs get_envcells</c> walks: <c>0x0100 .. 0x0100 + num_cells - 1</c>
    /// (ACE also stops at the first missing cell). It misses cells in 38 landblocks and overshoots in none.
    /// </summary>
    public static IReadOnlyList<uint> EnvCellIdsFromNumCells(LandblockCoord coord, uint numCells)
    {
        var count = (int)Math.Min(numCells, (uint)(CellIds.LastEnvCell - CellIds.FirstEnvCell + 1));
        var ids = new uint[count];
        for (var i = 0; i < count; i++)
        {
            ids[i] = CellIds.Make(coord, (ushort)(CellIds.FirstEnvCell + i));
        }

        return ids;
    }

    /// <summary>Every landblock that has a LandBlockInfo (statics, buildings or cells), ascending by id.</summary>
    public static IReadOnlyList<LandblockCoord> LandblocksWithInfo(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        return dats.Read(d => d.Cell.Tree
            .Where(f => (f.Id & 0xFFFFu) == 0xFFFEu)
            .Select(f => LandblockCoord.FromId(f.Id))
            .ToList());
    }

    /// <summary>Loads a landblock's cells, statics and terrain and classifies it.</summary>
    public static LandblockCellSummary Summarize(CellStructureLibrary structures, LandblockCoord coord)
    {
        ArgumentNullException.ThrowIfNull(structures);
        var dats = structures.Dats;
        var cells = LandblockCellSet.Load(structures, coord);
        var statics = LandblockStatics.Read(dats, coord);
        var terrain = dats.GetLandblockTerrain(coord);
        return Summarize(cells, statics, terrain, dats.TerrainRegion.LandHeightTable);
    }

    /// <summary>Classifies a landblock from already loaded data.</summary>
    /// <param name="cells">The landblock's EnvCells.</param>
    /// <param name="statics">Its LandBlockInfo, or null.</param>
    /// <param name="terrain">Its terrain, or null.</param>
    /// <param name="landHeightTable">Region LandHeightTable (height index to metres).</param>
    public static LandblockCellSummary Summarize(
        LandblockCellSet cells,
        LandblockStatics? statics,
        LandblockTerrain? terrain,
        IReadOnlyList<float> landHeightTable)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(landHeightTable);

        var buildingCells = BuildingCellIds(cells, statics);
        var allHeightsZero = terrain is not null && terrain.HeightIndices.All(h => h == 0);
        var minTerrainZ = terrain is null ? 0f : terrain.HeightIndices.Min(h => landHeightTable[h]);
        var underground = 0;
        var seenOutside = 0;
        var minZ = float.PositiveInfinity;
        var maxZ = float.NegativeInfinity;
        foreach (var cell in cells.Cells)
        {
            if (cell.SeenOutside)
            {
                seenOutside++;
            }

            var (lo, hi) = LandblockZRange(cell, cells.GetStructure(cell));
            minZ = MathF.Min(minZ, lo);
            maxZ = MathF.Max(maxZ, hi);
            if (hi < minTerrainZ)
            {
                underground++;
            }
        }

        var buildingCount = statics?.Buildings.Count ?? 0;
        var numCellsField = statics?.NumCells ?? 0;

        // ACE Physics/Common/Landblock.cs IsDungeon: not the NW-island area (BlockCoord in cell units: X < 64, Y > 1976),
        // every terrain height index 0, Info.NumCells > 0 and no buildings.
        var nwIsland = cells.Coord.X * TerrainConstants.CellsPerSide < 64 && cells.Coord.Y * TerrainConstants.CellsPerSide > 1976;
        var aceIsDungeon = !nwIsland && allHeightsZero && numCellsField > 0 && buildingCount == 0;

        var kind = cells.Count == 0
            ? LandblockCellKind.NoCells
            : buildingCount == 0 && seenOutside == 0
                ? LandblockCellKind.Dungeon
                : buildingCells.Count == cells.Count && underground == 0
                    ? LandblockCellKind.BuildingInteriors
                    : LandblockCellKind.Mixed;

        return new LandblockCellSummary(
            cells.Coord,
            kind,
            cells.Count,
            numCellsField,
            buildingCount,
            buildingCells.Count,
            cells.Count - buildingCells.Count,
            seenOutside,
            underground,
            allHeightsZero,
            aceIsDungeon,
            cells.Count == 0 ? 0f : minZ,
            cells.Count == 0 ? 0f : maxZ,
            minTerrainZ);
    }

    /// <summary>
    /// Summaries of every landblock with EnvCells whose kind is <see cref="LandblockCellKind.Dungeon"/> or
    /// <see cref="LandblockCellKind.Mixed"/>, largest dungeon part first. Reads every EnvCell (a few seconds); run it on
    /// a worker thread.
    /// </summary>
    public static IReadOnlyList<LandblockCellSummary> FindDungeons(CellStructureLibrary structures)
    {
        ArgumentNullException.ThrowIfNull(structures);
        var dats = structures.Dats;
        var heightTable = dats.TerrainRegion.LandHeightTable;
        var result = new List<LandblockCellSummary>();
        foreach (var coord in LandblocksWithInfo(dats))
        {
            if (CellRecordReader.EnvCellIds(dats, coord).Count == 0)
            {
                continue;
            }

            var summary = Summarize(LandblockCellSet.Load(structures, coord), LandblockStatics.Read(dats, coord), dats.GetLandblockTerrain(coord), heightTable);
            if (summary.Kind is LandblockCellKind.Dungeon or LandblockCellKind.Mixed)
            {
                result.Add(summary);
            }
        }

        return result
            .OrderByDescending(s => Math.Max(s.DetachedCellCount, s.UndergroundCellCount))
            .ThenBy(s => s.Coord.X)
            .ThenBy(s => s.Coord.Y)
            .ToList();
    }

    /// <summary>
    /// Cells that belong to buildings: everything reachable through the portal graph from a building doorway's entry
    /// cell (retail <c>CBuildingObj</c> / ACE <c>BuildingObj.get_building_cells</c> start from the same entry cells; ACE
    /// then follows VisibleCells, which can include rooms of neighbouring buildings, so portals are used here).
    /// </summary>
    public static IReadOnlySet<uint> BuildingCellIds(LandblockCellSet cells, LandblockStatics? statics)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var result = new HashSet<uint>();
        if (statics is null)
        {
            return result;
        }

        var adjacency = cells.PortalAdjacency();
        var queue = new Queue<uint>();
        foreach (var portal in statics.Buildings.SelectMany(b => b.Portals))
        {
            if (cells.GetCell(portal.OtherCellId) is not null && result.Add(portal.OtherCellId))
            {
                queue.Enqueue(portal.OtherCellId);
            }
        }

        while (queue.Count > 0)
        {
            foreach (var next in adjacency[queue.Dequeue()])
            {
                if (result.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// A spawn point inside a landblock's dungeon part: <b>heuristic, not retail</b> (retail and ACE teleport to positions
    /// from the server's world database, which Lifestone does not have offline).
    /// </summary>
    /// <remarks>
    /// Candidates are the cells not reachable from a building doorway; within the largest portal-connected group of them
    /// the lowest cell id with a walkable floor wins, and the point is the centroid of its largest floor polygon (see
    /// <see cref="FloorPointInCell"/>). Returns null when the landblock has no such cell.
    /// </remarks>
    public static CellEntryPoint? FindDungeonEntry(LandblockCellSet cells, LandblockStatics? statics)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var building = BuildingCellIds(cells, statics);
        var detached = cells.Ids.Where(id => !building.Contains(id)).ToHashSet();
        if (detached.Count == 0)
        {
            return null;
        }

        var adjacency = cells.PortalAdjacency();
        var components = new List<List<uint>>();
        var seen = new HashSet<uint>();
        foreach (var start in detached.OrderBy(id => id))
        {
            if (!seen.Add(start))
            {
                continue;
            }

            var component = new List<uint>();
            var queue = new Queue<uint>([start]);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                component.Add(id);
                foreach (var next in adjacency[id])
                {
                    if (detached.Contains(next) && seen.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            components.Add(component);
        }

        var largest = components.OrderByDescending(c => c.Count).ThenBy(c => c.Min()).First();
        return FirstFloorPoint(cells, largest.OrderBy(id => id), "dungeon: lowest cell with a floor in the largest detached portal group");
    }

    /// <summary>
    /// A spawn point inside a building: <b>heuristic, not retail</b>. The building doorways' entry cells are tried in
    /// doorway order, then the other building cells by id; the first with a walkable floor wins.
    /// </summary>
    public static CellEntryPoint? FindBuildingEntry(LandblockCellSet cells, BuildingData building)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(building);
        var entryCells = building.Portals.Select(p => p.OtherCellId).Distinct().ToList();
        var rest = building.Portals.SelectMany(p => p.StabList).Distinct().Except(entryCells).OrderBy(id => id);
        return FirstFloorPoint(cells, entryCells.Concat(rest), "building: first doorway entry cell with a floor");
    }

    /// <summary>
    /// The centroid of the largest walkable floor polygon of a cell (normal Z at least <see cref="CellStructure.FloorZ"/>
    /// after rotating into landblock space), in landblock-local coordinates, or null when the cell has none whose
    /// centroid lies inside the cell (checked 1 cm above the floor with the cell BSP).
    /// </summary>
    public static CellEntryPoint? FloorPointInCell(LandblockCellSet cells, EnvCellData cell, string reason = "floor of the cell")
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(cell);
        var structure = cells.GetStructure(cell);
        foreach (var floor in structure.Floors.OrderByDescending(f => f.Area).ThenBy(f => f.PolygonId))
        {
            var normal = cell.Frame.RotateToParent(floor.Plane.Normal);
            if (normal.Z < CellStructure.FloorZ)
            {
                continue;
            }

            var point = cell.Frame.LocalToParent(floor.Centroid);
            if (cells.Contains(cell, point + (normal * 0.01f)))
            {
                return new CellEntryPoint(cell.Id, point, normal, reason);
            }
        }

        return null;
    }

    private static CellEntryPoint? FirstFloorPoint(LandblockCellSet cells, IEnumerable<uint> candidates, string reason)
    {
        foreach (var id in candidates)
        {
            if (cells.GetCell(id) is { } cell && FloorPointInCell(cells, cell, reason) is { } point)
            {
                return point;
            }
        }

        return null;
    }

    private static (float Min, float Max) LandblockZRange(EnvCellData cell, CellStructure structure)
    {
        var b = structure.Bounds;
        var min = float.PositiveInfinity;
        var max = float.NegativeInfinity;
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? b.Min.X : b.Max.X, (i & 2) == 0 ? b.Min.Y : b.Max.Y, (i & 4) == 0 ? b.Min.Z : b.Max.Z);
            var z = cell.Frame.LocalToParent(corner).Z;
            min = MathF.Min(min, z);
            max = MathF.Max(max, z);
        }

        return (min, max);
    }
}

/// <summary>How a landblock's EnvCells are used (Lifestone's classification; see <see cref="LandblockCellSummary"/>).</summary>
public enum LandblockCellKind
{
    /// <summary>No EnvCells.</summary>
    NoCells,

    /// <summary>Every cell is reachable from a building doorway and none lies below the terrain.</summary>
    BuildingInteriors,

    /// <summary>No buildings and no cell sees outside: a self-contained indoor world reached only by teleport (every ACE
    /// <c>IsDungeon</c> landblock is one).</summary>
    Dungeon,

    /// <summary>Anything else: buildings plus cells not reachable from their doorways or below the terrain (towers over
    /// dungeons, basements, and portal-less solid "box" cells next to buildings), or cells that see outside without a
    /// building.</summary>
    Mixed,
}

/// <summary>Classification of one landblock's EnvCells.</summary>
/// <param name="Coord">Landblock.</param>
/// <param name="Kind">Lifestone's classification.</param>
/// <param name="EnvCellCount">EnvCells present in the dat.</param>
/// <param name="NumCellsField">LandBlockInfo num_cells as stored (unreliable).</param>
/// <param name="BuildingCount">Buildings in the LandBlockInfo.</param>
/// <param name="BuildingCellCount">Cells reachable through portals from a building doorway.</param>
/// <param name="DetachedCellCount">Cells not reachable from any building doorway.</param>
/// <param name="SeenOutsideCellCount">Cells with the SeenOutside flag.</param>
/// <param name="UndergroundCellCount">Cells whose landblock-space bounds lie entirely below the landblock's lowest
/// terrain vertex.</param>
/// <param name="AllTerrainHeightsZero">Every height index of the LandBlock is 0.</param>
/// <param name="AceIsDungeon">ACE <c>Landblock.IsDungeon</c> (heights all 0, num_cells &gt; 0, no buildings, not the NW island).</param>
/// <param name="MinCellZ">Lowest cell bound, landblock space.</param>
/// <param name="MaxCellZ">Highest cell bound, landblock space.</param>
/// <param name="MinTerrainZ">Lowest terrain vertex height.</param>
public sealed record LandblockCellSummary(
    LandblockCoord Coord,
    LandblockCellKind Kind,
    int EnvCellCount,
    uint NumCellsField,
    int BuildingCount,
    int BuildingCellCount,
    int DetachedCellCount,
    int SeenOutsideCellCount,
    int UndergroundCellCount,
    bool AllTerrainHeightsZero,
    bool AceIsDungeon,
    float MinCellZ,
    float MaxCellZ,
    float MinTerrainZ);

/// <summary>A spawn point on a cell floor, landblock-local (add the eye height yourself).</summary>
/// <param name="CellId">The EnvCell containing the point.</param>
/// <param name="Position">Point on the floor, landblock-local.</param>
/// <param name="FloorNormal">Floor normal, landblock space.</param>
/// <param name="Reason">Which heuristic picked it.</param>
public sealed record CellEntryPoint(uint CellId, Vector3 Position, Vector3 FloorNormal, string Reason);

namespace Lifestone.Data.Terrain;

/// <summary>
/// AC's pseudo-random per-cell diagonal choice for landscape triangles.
/// </summary>
/// <remarks>
/// <para>
/// Retail <c>CLandBlockStruct::ConstructPolygons</c> (mirrored by ACE <c>LandblockStruct.ConstructPolygons</c>)
/// computes, with global cell coordinates gx = landblockX * 8 + cellX and gy = landblockY * 8 + cellY and all
/// arithmetic in wrapping uint32:
/// </para>
/// <code>
/// s = gy * (214614067 * gx + 1813693831) - 1109124029 * gx - 1369149221
/// SWtoNEcut = s * 2.3283064e-10 >= 0.5        // i.e. s / 2^32 >= 0.5
/// </code>
/// <para>
/// 214614067 = 0x0CCAC033, 1813693831 = 0x6C1AC587, 1109124029 = 0x421BE3BD, 1369149221 = 0x519B8F25.
/// The float comparison is equivalent to testing bit 31 of <c>s</c>; docs/research/rendering.md reports
/// 0 mismatches over all 4,161,600 map cells.
/// </para>
/// </remarks>
public static class TerrainSplit
{
    /// <summary>Raw split hash for a global cell coordinate.</summary>
    public static uint Hash(int globalCellX, int globalCellY)
    {
        unchecked
        {
            var gx = (uint)globalCellX;
            var gy = (uint)globalCellY;
            return (gy * ((214614067u * gx) + 1813693831u)) - (1109124029u * gx) - 1369149221u;
        }
    }

    /// <summary>
    /// True when the cell's diagonal runs south-west to north-east (triangles SW-SE-NE and SW-NE-NW);
    /// false when it runs south-east to north-west (triangles SW-SE-NW and NE-NW-SE).
    /// </summary>
    public static bool IsSWtoNE(int globalCellX, int globalCellY) => (Hash(globalCellX, globalCellY) & 0x80000000u) != 0;

    /// <summary>Split direction of cell (cellX, cellY), 0-7 each, inside a landblock.</summary>
    public static bool IsSWtoNE(LandblockCoord landblock, int cellX, int cellY) =>
        IsSWtoNE(landblock.GlobalCellX + cellX, landblock.GlobalCellY + cellY);
}

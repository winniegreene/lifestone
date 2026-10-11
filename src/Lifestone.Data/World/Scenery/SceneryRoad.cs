// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Common/Landblock.cs (OnRoad, GetRoad) (AGPL-3.0)
// with changes: reads Lifestone's LandblockTerrain and compares in double (retail x87) instead of float.

using Lifestone.Data.Terrain;

namespace Lifestone.Data.World.Scenery;

/// <summary>
/// Retail road-exclusion test for scenery (<c>CLandBlock::on_road</c>): a point is "on road" when it lies within
/// <see cref="RoadWidth"/> of a road edge implied by the road bits of its cell's four corner vertices.
/// </summary>
/// <remarks>
/// The 16-case table is ACE <c>Landblock.OnRoad</c>. All 16 cases were verified against the x87 code of
/// <c>CLandBlock::on_road</c> in the user's acclient.exe 11.6096 (0x530D30-0x530F79), including the six diagonal ones
/// (|dx-dy|, |dx+dy-24|, dx+dy, 24+dx-dy, 24-dx+dy, 48-dx-dy), the strict comparisons and the evaluation order; they
/// are identical to ACE. Retail keeps dx in an x87 register (double) and stores dy as float; y - 24 * iy is exactly
/// representable in float, so evaluating dy in double, as we do, gives identical results.
/// </remarks>
public static class SceneryRoad
{
    /// <summary>LandDefs road width (LandDefs::get_vars: 5.0).</summary>
    public const float RoadWidth = 5.0f;

    /// <summary>Cell edge length (LandDefs::get_vars: 24.0).</summary>
    public const float TileLength = 24.0f;

    /// <summary>True when the landblock-local point (x, y), 0 &lt;= x, y &lt; 192, is on a road.</summary>
    public static bool OnRoad(LandblockTerrain terrain, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        // Retail: ix = floor(x / 24.0), iy = floor(y / 24.0) in double.
        var ix = (int)Math.Floor(x / (double)TileLength);
        var iy = (int)Math.Floor(y / (double)TileLength);
        if (ix is < 0 or >= TerrainConstants.CellsPerSide || iy is < 0 or >= TerrainConstants.CellsPerSide)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is outside the landblock.");
        }

        var r0 = terrain.Road(ix, iy) != 0;         // SW
        var r1 = terrain.Road(ix, iy + 1) != 0;     // NW
        var r2 = terrain.Road(ix + 1, iy) != 0;     // SE
        var r3 = terrain.Road(ix + 1, iy + 1) != 0; // NE

        if (!r0 && !r1 && !r2 && !r3)
        {
            return false;
        }

        const double rMin = RoadWidth;
        const double rMax = TileLength - RoadWidth;
        const double tile = TileLength;
        var dx = x - (ix * tile);
        var dy = y - (iy * tile);

        if (r0)
        {
            if (r1)
            {
                if (r2)
                {
                    return r3 || dx < rMin || dy < rMin;
                }

                return r3 ? dx < rMin || dy > rMax : dx < rMin;
            }

            if (r2)
            {
                return r3 ? dx > rMax || dy < rMin : dy < rMin;
            }

            return r3 ? Math.Abs(dx - dy) < rMin : dx + dy < rMin;
        }

        if (r1)
        {
            if (r2)
            {
                return r3 ? dx > rMax || dy > rMax : Math.Abs(dx + dy - tile) < rMin;
            }

            return r3 ? dy > rMax : tile + dx - dy < rMin;
        }

        if (r2)
        {
            return r3 ? dx > rMax : tile - dx + dy < rMin;
        }

        return (tile * 2.0) - dx - dy < rMin; // r3 only
    }
}

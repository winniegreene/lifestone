using System.Numerics;

namespace Lifestone.Data.Terrain.Blending;

/// <summary>
/// <b>Not retail.</b> Lighting normals whose block-edge vertices also sum the neighbouring landblocks' triangles, so the
/// vertex light is continuous across landblock edges.
/// </summary>
/// <remarks>
/// <para>
/// Retail <c>CLandBlockStruct::calc_lighting</c> (acclient.c:353713) sums only the block's own 128 triangles, so a vertex
/// on a block edge gets a different normal in each block that shares it and the Gouraud light steps across the edge
/// (<see cref="TerrainVertexLighting"/>; <c>TerrainVertexLightingTests.Block_edge_normals_differ_between_neighbours_as_in_retail</c>).
/// docs/terrain-shader.md §7 leaves keeping that seam to the lead, because the M1 definition of done asks for no visible
/// seams between landblocks. Since the M1 render-fidelity fix this is the renderer's default (lead decision: a glitch-fix
/// deviation, docs/terrain-renderer.md §8); <c>--retail-normals</c> restores retail's per-block normals.
/// </para>
/// <para>
/// Interior vertices are exactly <see cref="TerrainVertexLighting.ComputeNormals"/> (every triangle touching them is in
/// the block). Each edge vertex sums the unit plane normals (<c>cross(v1 - v0, v2 - v0)</c>, as <c>CPolygon::make_plane</c>)
/// of every triangle that touches it in the up-to-four cells around it, each cell taken from the landblock that owns it
/// (its own heights and its own <see cref="TerrainSplit"/> diagonal), then normalises; a missing neighbour (map edge)
/// contributes nothing.
/// </para>
/// </remarks>
public static class TerrainSeamlessNormals
{
    /// <summary>81 normals (index x*9+y) for <paramref name="terrain"/>, with edge vertices shared across blocks.</summary>
    /// <param name="terrain">The landblock.</param>
    /// <param name="getTerrain">Looks up a landblock's terrain (null when absent); called for the up to eight neighbours.</param>
    /// <param name="region">LandHeightTable and cell size.</param>
    public static Vector3[] Compute(LandblockTerrain terrain, Func<LandblockCoord, LandblockTerrain?> getTerrain, TerrainRegion region)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(getTerrain);
        ArgumentNullException.ThrowIfNull(region);

        var normals = TerrainVertexLighting.ComputeNormals(terrain, region.LandHeightTable, region.SquareLength);
        var cache = new Dictionary<LandblockCoord, LandblockTerrain?> { [terrain.Coord] = terrain };
        LandblockTerrain? Owner(int globalCellX, int globalCellY)
        {
            if (globalCellX < 0 || globalCellY < 0 || globalCellX > 0x7FF || globalCellY > 0x7FF)
            {
                return null;
            }

            var coord = new LandblockCoord((byte)(globalCellX >> 3), (byte)(globalCellY >> 3));
            if (!cache.TryGetValue(coord, out var owner))
            {
                owner = getTerrain(coord);
                cache[coord] = owner;
            }

            return owner;
        }

        const int last = TerrainConstants.VerticesPerSide - 1;
        var size = region.SquareLength;
        for (var x = 0; x <= last; x++)
        {
            for (var y = 0; y <= last; y++)
            {
                if (x is > 0 and < last && y is > 0 and < last)
                {
                    continue;
                }

                var gx = terrain.Coord.GlobalCellX + x; // global vertex = global cell of which it is the SW corner
                var gy = terrain.Coord.GlobalCellY + y;
                var sum = Vector3.Zero;
                for (var cellX = gx - 1; cellX <= gx; cellX++)
                {
                    for (var cellY = gy - 1; cellY <= gy; cellY++)
                    {
                        if (Owner(cellX, cellY) is not { } owner)
                        {
                            continue;
                        }

                        sum += CellContribution(owner, cellX & 7, cellY & 7, gx - cellX, gy - cellY, region, size);
                    }
                }

                var length = sum.Length();
                normals[TerrainConstants.VertexIndex(x, y)] = length < TerrainVertexLighting.MinNormalLength ? Vector3.UnitZ : sum / length;
            }
        }

        return normals;
    }

    /// <summary>Sum of the unit plane normals of the triangles of one cell that use the corner (cornerDx, cornerDy).</summary>
    private static Vector3 CellContribution(LandblockTerrain owner, int cellX, int cellY, int cornerDx, int cornerDy, TerrainRegion region, float size)
    {
        // Corners relative to the cell's SW corner (small numbers, so precision does not depend on the world position).
        Vector3 P(int dx, int dy) => new(dx * size, dy * size, region.LandHeightTable[owner.HeightIndex(cellX + dx, cellY + dy)]);
        var sw = (0, 0);
        var se = (1, 0);
        var ne = (1, 1);
        var nw = (0, 1);
        var corner = (cornerDx, cornerDy);

        ((int, int) A, (int, int) B, (int, int) C)[] triangles = TerrainSplit.IsSWtoNE(owner.Coord, cellX, cellY)
            ? [(sw, se, ne), (sw, ne, nw)]
            : [(sw, se, nw), (ne, nw, se)];

        var sum = Vector3.Zero;
        foreach (var (a, b, c) in triangles)
        {
            if (a != corner && b != corner && c != corner)
            {
                continue;
            }

            sum += TerrainVertexLighting.PlaneNormal(P(a.Item1, a.Item2), P(b.Item1, b.Item2), P(c.Item1, c.Item2));
        }

        return sum;
    }
}

using System.Numerics;
using Lifestone.Data.Terrain;
using Lifestone.Data.Terrain.Blending;
using Lifestone.Data.Tests.Terrain;

namespace Lifestone.Data.Tests.TerrainRender;

/// <summary>The opt-in, non-retail across-blocks normals (<see cref="TerrainSeamlessNormals"/>).</summary>
public sealed class TerrainSeamlessNormalsTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Interior_vertices_keep_the_retail_normals_and_shared_edge_vertices_agree_across_blocks()
    {
        var dats = fixture.Dats;
        var region = dats.TerrainRegion;
        var cache = new Dictionary<LandblockCoord, LandblockTerrain?>();
        LandblockTerrain? Get(LandblockCoord c) => cache.TryGetValue(c, out var t) ? t : cache[c] = dats.GetLandblockTerrain(c);

        var normals = new Dictionary<LandblockCoord, Vector3[]>();
        foreach (var terrain in TerrainTestData.LandblocksAround(dats, LandblockCoord.Holtburg, 1))
        {
            var seamless = TerrainSeamlessNormals.Compute(terrain, Get, region);
            var retail = TerrainVertexLighting.ComputeNormals(terrain, region.LandHeightTable, region.SquareLength);
            for (var x = 1; x < 8; x++)
            {
                for (var y = 1; y < 8; y++)
                {
                    Assert.Equal(retail[TerrainConstants.VertexIndex(x, y)], seamless[TerrainConstants.VertexIndex(x, y)]);
                }
            }

            Assert.All(seamless, n => Assert.Equal(1f, n.Length(), 4));
            normals[terrain.Coord] = seamless;
        }

        // Holtburg's east edge is its east neighbour's west edge: the same global vertex now has one normal.
        var center = normals[LandblockCoord.Holtburg];
        var east = normals[LandblockCoord.Holtburg.Offset(1, 0)!.Value];
        var north = normals[LandblockCoord.Holtburg.Offset(0, 1)!.Value];
        var northEast = normals[LandblockCoord.Holtburg.Offset(1, 1)!.Value];
        for (var i = 0; i <= 8; i++)
        {
            AssertClose(center[TerrainConstants.VertexIndex(8, i)], east[TerrainConstants.VertexIndex(0, i)]);
            AssertClose(center[TerrainConstants.VertexIndex(i, 8)], north[TerrainConstants.VertexIndex(i, 0)]);
        }

        AssertClose(center[TerrainConstants.VertexIndex(8, 8)], northEast[TerrainConstants.VertexIndex(0, 0)]);
    }

    [Fact]
    public void Flat_ground_points_straight_up_and_missing_neighbours_contribute_nothing()
    {
        var region = new TerrainRegion(Enumerable.Repeat(10f, 256).ToArray(), []);
        var flat = new LandblockTerrain(new LandblockCoord(0, 0), new ushort[81], new byte[81]);
        var normals = TerrainSeamlessNormals.Compute(flat, _ => null, region); // map corner, no neighbours at all
        Assert.All(normals, n => Assert.Equal(Vector3.UnitZ, n));
    }

    [Fact]
    public void A_neighbour_slope_tilts_the_shared_edge_vertex()
    {
        // Centre block flat at height 0; its east neighbour rises 1 m per metre eastward. Retail ignores the neighbour (the
        // east-edge normal stays vertical); the seamless normal leans west (away from the rising slope).
        var table = Enumerable.Range(0, 256).Select(h => (float)h).ToArray();
        var region = new TerrainRegion(table, []);
        var center = new LandblockTerrain(new LandblockCoord(0x10, 0x10), new ushort[81], new byte[81]);
        var eastHeights = new byte[81];
        for (var x = 0; x < 9; x++)
        {
            for (var y = 0; y < 9; y++)
            {
                eastHeights[TerrainConstants.VertexIndex(x, y)] = (byte)(x * 24);
            }
        }

        var east = new LandblockTerrain(new LandblockCoord(0x11, 0x10), new ushort[81], eastHeights);
        var normals = TerrainSeamlessNormals.Compute(center, c => c == east.Coord ? east : c == center.Coord ? center : null, region);
        var edge = normals[TerrainConstants.VertexIndex(8, 4)];
        Assert.True(edge.X < -0.1f, $"edge normal {edge} should lean west");
        Assert.Equal(Vector3.UnitZ, TerrainVertexLighting.ComputeNormals(center, table)[TerrainConstants.VertexIndex(8, 4)]);
        Assert.Equal(Vector3.UnitZ, normals[TerrainConstants.VertexIndex(0, 4)]);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-5f, $"{expected} vs {actual}");
}

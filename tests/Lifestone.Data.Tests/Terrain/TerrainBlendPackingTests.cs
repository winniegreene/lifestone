using System.Numerics;
using Lifestone.Data.Terrain;
using Lifestone.Data.Terrain.Blending;

namespace Lifestone.Data.Tests.Terrain;

/// <summary>The suggested CUSTOM0..2 packing and the per-vertex expansion used by the terrain shader.</summary>
public sealed class TerrainBlendPackingTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [Fact]
    public void Packing_round_trips_every_layer_index_and_rotation()
    {
        var merge = new TerrainTexMerge(TerrainAtlasManifest.FromTexMerge(TerrainTestData.DerethShaped()));
        var rng = new Random(99);
        for (var n = 0; n < 20_000; n++)
        {
            var pal = TerrainPalCode.Compose(rng.Next(4), rng.Next(4), rng.Next(4), rng.Next(4), rng.Next(32), rng.Next(32), rng.Next(32), rng.Next(32));
            if (n % 2 == 0)
            {
                pal &= 0xF00FFFFFu;
            }

            AssertRoundTrip(merge.GetCellBlend(pal));
        }
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Holtburg_vertices_carry_flat_cell_data_and_retail_corner_uvs()
    {
        var dats = fixture.Dats;
        var manifest = TerrainAtlasManifest.FromDatSet(dats);
        var merge = new TerrainTexMerge(manifest);
        var terrain = dats.GetLandblockTerrain(LandblockCoord.Holtburg)!;
        var mesh = TerrainMeshBuilder.Build(terrain, dats.TerrainRegion);
        var blend = merge.BuildLandblock(terrain);
        var normals = TerrainVertexLighting.ComputeNormals(terrain, dats.TerrainRegion.LandHeightTable);
        var vertices = TerrainBlendVertices.Build(mesh, blend, normals);

        Assert.Equal(384, vertices.VertexCount);
        Assert.Equal(384 * 12, vertices.Custom.Length);
        for (var i = 0; i < vertices.VertexCount; i++)
        {
            var cell = i / 6;
            var (cx, cy) = (cell / 8, cell % 8);
            var vi = vertices.SourceVertex[i];
            var (x, y) = (vi / 9, vi % 9);

            // SW (0,1), SE (1,1), NE (1,0), NW (0,0).
            Assert.Equal(new Vector2(x - cx, 1 - (y - cy)), vertices.Uvs[i]);
            Assert.Equal(mesh.Positions[vi], vertices.Positions[i]);
            Assert.Equal(normals[vi], vertices.Normals[i]);
            Assert.True(
                vertices.Custom.AsSpan(i * 12, 12).SequenceEqual(TerrainBlendPacking.Pack(blend[cx, cy])),
                $"vertex {i} does not carry cell ({cx},{cy})'s data");
        }

        foreach (var cell in blend.Cells)
        {
            AssertRoundTrip(cell);
        }
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Block_edge_normals_differ_between_neighbours_as_in_retail()
    {
        // calc_lighting only sums the block's own triangles, so a shared edge vertex gets a different normal in each
        // block. This documents the (retail) lighting discontinuity the renderer will show at block edges.
        var dats = fixture.Dats;
        var heights = dats.TerrainRegion.LandHeightTable;
        var west = dats.GetLandblockTerrain(LandblockCoord.Holtburg)!;
        var east = dats.GetLandblockTerrain(LandblockCoord.Holtburg.Offset(1, 0)!.Value)!;
        var nWest = TerrainVertexLighting.ComputeNormals(west, heights);
        var nEast = TerrainVertexLighting.ComputeNormals(east, heights);

        var differing = 0;
        for (var y = 0; y < 9; y++)
        {
            Assert.Equal(west.HeightIndex(8, y), east.HeightIndex(0, y)); // the vertices coincide
            if (Vector3.Distance(nWest[TerrainConstants.VertexIndex(8, y)], nEast[TerrainConstants.VertexIndex(0, y)]) > 1e-4f)
            {
                differing++;
            }
        }

        Assert.True(differing > 0, "expected at least one edge vertex with block-dependent normals");
    }

    private static void AssertRoundTrip(in TerrainCellBlend blend)
    {
        var unpacked = TerrainBlendPacking.Unpack(TerrainBlendPacking.Pack(blend));
        Assert.Equal(blend.BaseTextureLayer, unpacked.BaseLayer);
        Assert.Equal(blend.IsAllRoad, unpacked.IsAllRoad);
        Assert.Equal(blend.IsSingleTexture, unpacked.IsSingleTexture);
        for (var i = 0; i < 3; i++)
        {
            if (i < blend.OverlayCount)
            {
                var o = blend.GetOverlay(i);
                Assert.Equal(o.TextureLayer, unpacked.OverlayLayers[i]);
                Assert.Equal(o.AlphaLayer, unpacked.OverlayAlphas[i]);
                Assert.Equal(o.Rotation, unpacked.OverlayRotations[i]);
            }
            else
            {
                Assert.Equal(-1, unpacked.OverlayLayers[i]);
                Assert.Equal(-1, unpacked.OverlayAlphas[i]);
            }
        }

        Assert.Equal(blend.RoadCount > 0 ? blend.RoadTextureLayer : -1, unpacked.RoadLayer);
        for (var i = 0; i < 2; i++)
        {
            if (i < blend.RoadCount)
            {
                Assert.Equal(blend.GetRoad(i).AlphaLayer, unpacked.RoadAlphas[i]);
                Assert.Equal(blend.GetRoad(i).Rotation, unpacked.RoadRotations[i]);
            }
            else
            {
                Assert.Equal(-1, unpacked.RoadAlphas[i]);
            }
        }
    }
}

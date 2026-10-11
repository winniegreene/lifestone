using System.Numerics;
using Lifestone.Data.Terrain;
using Lifestone.Data.Terrain.Blending;
using Lifestone.Data.Tests.Terrain;

namespace Lifestone.Data.Tests.TerrainRender;

/// <summary>
/// The indexed per-cell render mesh (<see cref="TerrainRenderMesh"/>): geometry identical to <see cref="TerrainMeshBuilder"/>,
/// retail lighting normals, retail corner UVs, and CUSTOM0..2 bytes that decode back to the <see cref="TerrainTexMerge"/>
/// recipe and the <see cref="TerrainAtlasManifest"/> layers.
/// </summary>
public sealed class TerrainRenderMeshTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>Holtburg, desert south-east of Yaraq, snowy mountains, the Holtburg marsh, Hebian-To's coast, Glenden Wood.</summary>
    public static readonly LandblockCoord[] Blocks =
    [
        LandblockCoord.Holtburg,
        new(0x7E, 0x62),
        new(0x7A, 0xC2),
        new(0xA8, 0xB5),
        new(0xE7, 0x4F),
        new(0xA1, 0xA4),
    ];

    [Fact]
    public void Corner_slots_and_uvs_follow_the_retail_land_uvs()
    {
        Assert.Equal(0, TerrainRenderMesh.CornerSlot(0, 0));
        Assert.Equal(1, TerrainRenderMesh.CornerSlot(1, 0));
        Assert.Equal(2, TerrainRenderMesh.CornerSlot(1, 1));
        Assert.Equal(3, TerrainRenderMesh.CornerSlot(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainRenderMesh.CornerSlot(2, 0));

        // SW (0,1), SE (1,1), NE (1,0), NW (0,0): u east, v south.
        Assert.Equal(new Vector2(0, 1), TerrainRenderMesh.CornerUv(0));
        Assert.Equal(new Vector2(1, 1), TerrainRenderMesh.CornerUv(1));
        Assert.Equal(new Vector2(1, 0), TerrainRenderMesh.CornerUv(2));
        Assert.Equal(new Vector2(0, 0), TerrainRenderMesh.CornerUv(3));
    }

    [Fact]
    public void Synthetic_block_packs_every_cell_flat_and_keeps_the_triangle_order()
    {
        var manifest = TerrainAtlasManifest.FromTexMerge(TerrainTestData.DerethShaped());
        var merge = new TerrainTexMerge(manifest);
        var rng = new Random(5);
        var words = new ushort[81];
        var heights = new byte[81];
        for (var i = 0; i < 81; i++)
        {
            words[i] = (ushort)((rng.Next(32) << 2) | (rng.Next(6) == 0 ? rng.Next(1, 4) : 0));
            heights[i] = (byte)rng.Next(0, 60);
        }

        var table = Enumerable.Range(0, 256).Select(h => h * 2f).ToArray();
        var region = new TerrainRegion(table, []);
        var terrain = new LandblockTerrain(new LandblockCoord(0x40, 0x41), words, heights);
        AssertMesh(TerrainRenderMesh.Build(terrain, region, merge), terrain, region, merge, manifest);
    }

    [RequiresDatsFact]
    public void Real_blocks_carry_their_recipes_geometry_and_retail_normals()
    {
        var dats = fixture.Dats;
        var manifest = dats.TerrainAtlas;
        var merge = new TerrainTexMerge(manifest);
        var seen = new HashSet<(int Overlays, int Roads, bool AllRoad)>();
        foreach (var coord in Blocks.Concat(TerrainTestData.LandblocksAround(dats, LandblockCoord.Holtburg, 2).Select(t => t.Coord)).Distinct())
        {
            var terrain = dats.GetLandblockTerrain(coord)!;
            var mesh = TerrainRenderMesh.Build(terrain, dats.TerrainRegion, merge);
            AssertMesh(mesh, terrain, dats.TerrainRegion, merge, manifest);
            foreach (var cell in mesh.Blend.Cells)
            {
                seen.Add((cell.OverlayCount, cell.RoadCount, cell.IsAllRoad));
            }
        }

        // The sample covers plain cells, 1-2 overlays and road layers.
        Assert.Contains(seen, s => s.Overlays == 0 && s.Roads == 0);
        Assert.Contains(seen, s => s.Overlays == 2);
        Assert.Contains(seen, s => s.Roads == 1);
        Assert.Contains(seen, s => s.Roads == 2);
    }

    [RequiresDatsFact]
    public void Explicit_grid_normals_replace_the_retail_ones()
    {
        var dats = fixture.Dats;
        var terrain = dats.GetLandblockTerrain(LandblockCoord.Holtburg)!;
        var merge = new TerrainTexMerge(dats.TerrainAtlas);
        var up = Enumerable.Repeat(Vector3.UnitZ, 81).ToArray();
        var mesh = TerrainRenderMesh.Build(terrain, dats.TerrainRegion, merge, up);
        Assert.All(mesh.Normals, n => Assert.Equal(Vector3.UnitZ, n));
        Assert.Throws<ArgumentException>(() => TerrainRenderMesh.Build(terrain, dats.TerrainRegion, merge, up[..80]));
    }

    private static void AssertMesh(TerrainRenderMesh mesh, LandblockTerrain terrain, TerrainRegion region, TerrainTexMerge merge, TerrainAtlasManifest manifest)
    {
        var reference = TerrainMeshBuilder.Build(terrain, region);
        var normals = TerrainVertexLighting.ComputeNormals(terrain, region.LandHeightTable, region.SquareLength);
        var blend = merge.BuildLandblock(terrain);

        Assert.Equal(TerrainRenderMesh.VertexCount, mesh.Positions.Length);
        Assert.Equal(TerrainRenderMesh.IndexCount, mesh.Indices.Length);
        Assert.Equal(256 * 4, mesh.Custom0.Length);
        Assert.Equal(reference.Positions.Min(p => p.Z), mesh.MinHeight);
        Assert.Equal(reference.Positions.Max(p => p.Z), mesh.MaxHeight);

        for (var v = 0; v < TerrainRenderMesh.VertexCount; v++)
        {
            var cell = v / 4;
            var (cx, cy) = (cell / 8, cell % 8);
            var grid = mesh.SourceVertex[v];
            var (gx, gy) = (grid / 9, grid % 9);
            Assert.Equal(TerrainRenderMesh.CornerSlot(gx - cx, gy - cy), v % 4);
            Assert.Equal(reference.Positions[grid], mesh.Positions[v]);
            Assert.Equal(normals[grid], mesh.Normals[v]);
            Assert.Equal(TerrainRenderMesh.CornerUv(v % 4), mesh.Uvs[v]);

            // The bytes are the reference packing of the cell's recipe, on every corner.
            var expected = TerrainBlendPacking.Pack(blend[cx, cy]);
            Assert.Equal(expected, mesh.PackedBytes(v));

            // ... and decode to the recipe's layers, alpha maps and rotations.
            var packed = TerrainBlendPacking.Unpack(mesh.PackedBytes(v));
            var recipe = blend[cx, cy];
            Assert.Equal(recipe.BaseTextureLayer, packed.BaseLayer);
            Assert.Equal(manifest.LayerForTerrainType(recipe.BaseTerrainType), packed.BaseLayer);
            for (var i = 0; i < 3; i++)
            {
                if (i < recipe.OverlayCount)
                {
                    var o = recipe.GetOverlay(i);
                    Assert.Equal(manifest.LayerForTerrainType(o.TerrainType), packed.OverlayLayers[i]);
                    Assert.Equal(o.AlphaLayer, packed.OverlayAlphas[i]);
                    Assert.Equal(o.Rotation, packed.OverlayRotations[i]);
                }
                else
                {
                    Assert.Equal(-1, packed.OverlayLayers[i]);
                    Assert.Equal(-1, packed.OverlayAlphas[i]);
                }
            }

            for (var i = 0; i < 2; i++)
            {
                if (i < recipe.RoadCount)
                {
                    Assert.Equal(manifest.RoadLayer, packed.RoadLayer);
                    Assert.Equal(recipe.GetRoad(i).AlphaLayer, packed.RoadAlphas[i]);
                    Assert.Equal(recipe.GetRoad(i).Rotation, packed.RoadRotations[i]);
                    Assert.Equal(TerrainAlphaKind.Road, manifest.AlphaLayers[packed.RoadAlphas[i]].Kind);
                }
                else
                {
                    Assert.Equal(-1, packed.RoadAlphas[i]);
                }
            }

            Assert.Equal(recipe.IsAllRoad, packed.IsAllRoad);
            Assert.Equal(recipe.IsSingleTexture, packed.IsSingleTexture);
        }

        // Same triangles, same order, same diagonal as the retail mesh; counter-clockwise from above (AC winding).
        for (var i = 0; i < mesh.Indices.Length; i++)
        {
            Assert.Equal(reference.Indices[i], mesh.SourceVertex[mesh.Indices[i]]);
            Assert.Equal(i / 6, mesh.Indices[i] / 4); // every triangle uses its own cell's vertices
        }

        for (var t = 0; t < mesh.Indices.Length; t += 3)
        {
            var a = mesh.Positions[mesh.Indices[t]];
            var b = mesh.Positions[mesh.Indices[t + 1]];
            var c = mesh.Positions[mesh.Indices[t + 2]];
            Assert.True(Vector3.Cross(b - a, c - a).Z > 0, $"triangle {t / 3} is not counter-clockwise from above");
        }
    }
}

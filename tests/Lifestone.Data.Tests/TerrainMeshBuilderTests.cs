using System.Numerics;
using Lifestone.Data.Terrain;

namespace Lifestone.Data.Tests;

public sealed class TerrainMeshBuilderTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static readonly float[] LinearHeightTable = Enumerable.Range(0, 256).Select(i => i * 2f).ToArray();

    [Fact]
    public void Flat_landblock_has_81_vertices_128_triangles_and_up_normals()
    {
        var terrain = new LandblockTerrain(LandblockCoord.Holtburg, new ushort[81], Enumerable.Repeat((byte)33, 81).ToArray());
        var mesh = TerrainMeshBuilder.Build(terrain, LinearHeightTable);

        Assert.Equal(81, mesh.VertexCount);
        Assert.Equal(128, mesh.TriangleCount);
        Assert.Equal(384, mesh.Indices.Length);
        Assert.All(mesh.Positions, p => Assert.Equal(66f, p.Z));
        Assert.All(mesh.Normals, n => Assert.Equal(Vector3.UnitZ, n));
        Assert.Equal(new Vector3(192f, 192f, 66f), mesh.Positions[80]);
        Assert.Equal(new Vector3(24f, 48f, 66f), mesh.Positions[TerrainConstants.VertexIndex(1, 2)]);
    }

    [Fact]
    public void Triangles_follow_split_direction_and_AC_winding()
    {
        var heights = Enumerable.Range(0, 81).Select(i => (byte)((i * 37) % 60)).ToArray();
        var terrain = new LandblockTerrain(new LandblockCoord(0x12, 0x34), new ushort[81], heights);
        var mesh = TerrainMeshBuilder.Build(terrain, LinearHeightTable);
        AssertWellFormed(mesh);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Holtburg_mesh_from_dats()
    {
        var dats = fixture.Dats;
        var terrain = dats.GetLandblockTerrain(LandblockCoord.Holtburg)!;
        var mesh = TerrainMeshBuilder.Build(terrain, dats.TerrainRegion);

        Assert.Equal(81, mesh.VertexCount);
        Assert.Equal(128, mesh.TriangleCount);
        AssertWellFormed(mesh);

        // local-scout.md: Holtburg heights are 30..96 m and the town plateau (index 33) is 66 m.
        Assert.Equal(30f, mesh.Positions.Min(p => p.Z));
        Assert.Equal(96f, mesh.Positions.Max(p => p.Z));
        Assert.Equal(66f, mesh.Positions[TerrainConstants.VertexIndex(3, 5)].Z);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Holtburg_neighbourhood_shares_edge_heights()
    {
        // Adjacent landblocks duplicate their shared edge vertices; a mismatch would show as cracks.
        var dats = fixture.Dats;
        var center = LandblockCoord.Holtburg;
        var east = dats.GetLandblockTerrain(center.Offset(1, 0)!.Value)!;
        var north = dats.GetLandblockTerrain(center.Offset(0, 1)!.Value)!;
        var middle = dats.GetLandblockTerrain(center)!;
        for (var i = 0; i < 9; i++)
        {
            Assert.Equal(middle.HeightIndex(8, i), east.HeightIndex(0, i));
            Assert.Equal(middle.HeightIndex(i, 8), north.HeightIndex(i, 0));
        }
    }

    private static void AssertWellFormed(TerrainMesh mesh)
    {
        Assert.All(mesh.Indices, i => Assert.InRange(i, 0, mesh.VertexCount - 1));

        for (var cx = 0; cx < 8; cx++)
        {
            for (var cy = 0; cy < 8; cy++)
            {
                var cell = (cx * 8) + cy;
                var t0 = mesh.Indices.AsSpan(cell * 6, 3).ToArray();
                var t1 = mesh.Indices.AsSpan((cell * 6) + 3, 3).ToArray();
                var sw = TerrainConstants.VertexIndex(cx, cy);
                var se = TerrainConstants.VertexIndex(cx + 1, cy);
                var ne = TerrainConstants.VertexIndex(cx + 1, cy + 1);
                var nw = TerrainConstants.VertexIndex(cx, cy + 1);

                var swToNe = TerrainSplit.IsSWtoNE(mesh.Coord, cx, cy);
                Assert.Equal(swToNe, mesh.CellSplitSWtoNE[cell]);

                // The shared edge of the two triangles is the cell diagonal.
                var shared = t0.Intersect(t1).OrderBy(i => i).ToArray();
                var diagonal = (swToNe ? new[] { sw, ne } : new[] { se, nw }).OrderBy(i => i).ToArray();
                Assert.Equal(diagonal, shared);

                foreach (var tri in new[] { t0, t1 })
                {
                    // AC winding: counter-clockwise seen from above, so the face normal points up (+Z).
                    var a = mesh.Positions[tri[0]];
                    var b = mesh.Positions[tri[1]];
                    var c = mesh.Positions[tri[2]];
                    var normal = Vector3.Cross(b - a, c - a);
                    Assert.True(normal.Z > 0f, $"cell ({cx},{cy}) triangle is not CCW from above");

                    // Projected area of each triangle is half a 24 x 24 cell.
                    Assert.Equal(288f, normal.Z / 2f, 3);
                }
            }
        }

        Assert.All(mesh.Normals, n =>
        {
            Assert.Equal(1f, n.Length(), 4);
            Assert.True(n.Z > 0f);
        });
    }
}

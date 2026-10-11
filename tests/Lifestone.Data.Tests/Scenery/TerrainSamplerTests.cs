using System.Numerics;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Data.Tests.Scenery;

/// <summary>The retail height sampler against <see cref="TerrainMeshBuilder"/> and against exact geometry.</summary>
public sealed class TerrainSamplerTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static readonly float[] LinearHeightTable = Enumerable.Range(0, 256).Select(i => i * 2f).ToArray();

    [Fact]
    public void Flat_landblock_has_up_normals_and_constant_height()
    {
        var terrain = new LandblockTerrain(new LandblockCoord(0x40, 0x41), new ushort[81], Enumerable.Repeat((byte)10, 81).ToArray());
        var sampler = new TerrainSampler(terrain, LinearHeightTable);
        Assert.All(sampler.Planes, p =>
        {
            Assert.Equal(Vector3.UnitZ, p.Normal);
            Assert.Equal(-20f, p.D);
        });
        Assert.Equal(20f, sampler.GetHeight(0f, 0f));
        Assert.Equal(20f, sampler.GetHeight(191.99998f, 100.25f));
    }

    [Fact]
    public void Make_plane_of_a_tilted_triangle_is_its_unit_normal()
    {
        // z = x / 2 over a 24 x 24 cell: normal ~ (-1, 0, 2) / sqrt(5).
        var plane = TerrainSampler.MakePlane(new Vector3(0, 0, 0), new Vector3(24, 0, 12), new Vector3(0, 24, 0));
        Assert.Equal(-1 / MathF.Sqrt(5), plane.Normal.X, 6);
        Assert.Equal(0f, plane.Normal.Y);
        Assert.Equal(2 / MathF.Sqrt(5), plane.Normal.Z, 6);
        Assert.Equal(0f, plane.D, 6);
        Assert.True(TerrainSampler.TrySetHeight(plane, 10f, 7f, 0f, out var z));
        Assert.Equal(5f, z, 5);
    }

    [Fact]
    public void Points_on_the_diagonal_take_the_first_triangle_and_out_of_block_points_none()
    {
        var heights = Enumerable.Range(0, 81).Select(i => (byte)((i * 37) % 60)).ToArray();
        var terrain = new LandblockTerrain(new LandblockCoord(0x12, 0x34), new ushort[81], heights);
        var sampler = new TerrainSampler(terrain, LinearHeightTable);
        for (var cx = 0; cx < 8; cx++)
        {
            for (var cy = 0; cy < 8; cy++)
            {
                var first = 2 * ((cx * 8) + cy);
                var swToNe = TerrainSplit.IsSWtoNE(terrain.Coord, cx, cy);

                // The cell centre lies on both diagonals' crossing point: inclusive edges pick triangle 0.
                Assert.Equal(first, sampler.FindTerrainPolygon((cx * 24) + 12f, (cy * 24) + 12f));

                // A point just off the diagonal on the triangle-1 side.
                var (x, y) = swToNe ? ((cx * 24) + 6f, (cy * 24) + 18f) : ((cx * 24) + 18f, (cy * 24) + 18f);
                Assert.Equal(first + 1, sampler.FindTerrainPolygon(x, y));
            }
        }

        Assert.Equal(-1, sampler.FindTerrainPolygon(192f, 10f));
        Assert.Equal(-1, sampler.FindTerrainPolygon(10f, -0.001f));
        Assert.Throws<ArgumentOutOfRangeException>(() => sampler.GetHeight(-1f, 5f));
    }

    [RequiresDatsFact]
    public void Matches_TerrainMeshBuilder_vertices_triangles_and_normals_around_Holtburg()
    {
        var dats = fixture.Dats;
        for (var x = 0xA6; x <= 0xAC; x++)
        {
            for (var y = 0xB1; y <= 0xB7; y++)
            {
                var terrain = dats.GetLandblockTerrain(new LandblockCoord((byte)x, (byte)y))!;
                var mesh = TerrainMeshBuilder.Build(terrain, dats.TerrainRegion);
                var sampler = new TerrainSampler(terrain, dats.TerrainRegion.LandHeightTable);

                Assert.Equal(mesh.Positions, sampler.Vertices);
                Assert.Equal(mesh.Indices, sampler.PolygonVertexIndices);

                for (var t = 0; t < mesh.TriangleCount; t++)
                {
                    var a = mesh.Positions[mesh.Indices[t * 3]];
                    var b = mesh.Positions[mesh.Indices[(t * 3) + 1]];
                    var c = mesh.Positions[mesh.Indices[(t * 3) + 2]];
                    var face = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                    var plane = sampler.Planes[t];
                    Assert.True(Vector3.Distance(face, plane.Normal) < 1e-6f, $"{terrain.Coord} triangle {t}: {face} vs {plane.Normal}");
                    foreach (var v in new[] { a, b, c })
                    {
                        Assert.True(Math.Abs(Vector3.Dot(plane.Normal, v) + plane.D) < 1e-3f, $"{terrain.Coord} triangle {t} vertex {v} off its plane");
                    }
                }
            }
        }
    }

    [RequiresDatsFact]
    public void Heights_equal_barycentric_interpolation_of_the_mesh_triangle_under_the_point()
    {
        var dats = fixture.Dats;
        var random = new Random(97);
        foreach (var coord in new[] { LandblockCoord.Holtburg, new LandblockCoord(0xA9, 0xB3), new LandblockCoord(0x7A, 0x8B), new LandblockCoord(0x26, 0x4C) })
        {
            var terrain = dats.GetLandblockTerrain(coord)!;
            var mesh = TerrainMeshBuilder.Build(terrain, dats.TerrainRegion);
            var sampler = new TerrainSampler(terrain, dats.TerrainRegion.LandHeightTable);

            // Every vertex: the plane height reproduces the LandHeightTable height.
            for (var vx = 0; vx < 8; vx++)
            {
                for (var vy = 0; vy < 8; vy++)
                {
                    var p = mesh.Positions[TerrainConstants.VertexIndex(vx, vy)];
                    Assert.Equal(p.Z, sampler.GetHeight(p.X, p.Y), 3);
                }
            }

            for (var i = 0; i < 2000; i++)
            {
                var x = (float)(random.NextDouble() * 192.0);
                var y = (float)(random.NextDouble() * 192.0);
                var expected = MeshHeight(mesh, x, y);
                Assert.True(Math.Abs(expected - sampler.GetHeight(x, y)) < 2e-3, $"{coord} ({x}, {y}): mesh {expected}, sampler {sampler.GetHeight(x, y)}");
            }
        }
    }

    [RequiresDatsFact]
    public void Exact_arithmetic_puts_a_point_4_microns_off_the_diagonal_in_the_second_triangle()
    {
        // Landblock 0x7A8B, cell (4, 0) is cut SE-NW. (108.66f, 11.34f) lies 3.8e-6 north-east of that diagonal
        // (108.66f + 11.34f = 120.0000038). Retail's x87 (53-bit) cross products are exact and reject triangle 0; ACE's
        // float point_in_poly2D rounds the cross product to 0 and accepts triangle 0, whose slope then differs.
        var terrain = fixture.Dats.GetLandblockTerrain(new LandblockCoord(0x7A, 0x8B))!;
        var sampler = new TerrainSampler(terrain, fixture.Dats.TerrainRegion.LandHeightTable);
        Assert.False(TerrainSplit.IsSWtoNE(terrain.Coord, 4, 0));
        const int firstPolygon = 2 * ((4 * 8) + 0);
        Assert.False(sampler.PointInPolygon2D(firstPolygon, 108.66f, 11.34f));
        Assert.True(sampler.PointInPolygon2D(firstPolygon + 1, 108.66f, 11.34f));
        Assert.Equal(firstPolygon + 1, sampler.FindTerrainPolygon(108.66f, 11.34f));
    }

    [Fact]
    public void Retail_evaluation_order_decides_a_point_a_femtometre_off_a_diagonal()
    {
        // Retail point_in_poly2D evaluates each edge's line function as ((c + n.y * py) + n.x * px) with c = -(n . to)
        // (11.6096 0x538DC2-0x538DE6). With py below 2^-18 the partial sum c + n.y * py rounds. Take cell (1, 0) cut
        // SW-NE and the point (24, 2e-15), which lies 2e-15 north-west of the diagonal, so exactly it is only in
        // triangle 1. For the diagonal edge of triangle 0, c = 576 and n.y * py = 4.8e-14 is under half an ulp of 576,
        // so retail's sum is 0 (on the edge) and it accepts triangle 0. The algebraically equal form
        // n.x * (px - to.x) + n.y * (py - to.y) gives +2^-43 there and picks triangle 1.
        var coord = Enumerable.Range(0, 255 * 255)
            .Select(i => new LandblockCoord((byte)(i / 255), (byte)(i % 255)))
            .First(c => TerrainSplit.IsSWtoNE(c, 1, 0));
        var heights = Enumerable.Range(0, 81).Select(i => (byte)((i * 37) % 60)).ToArray();
        var sampler = new TerrainSampler(new LandblockTerrain(coord, new ushort[81], heights), LinearHeightTable);
        const int firstPolygon = 2 * ((1 * 8) + 0);
        const float py = 2e-15f;

        Assert.True(sampler.PointInPolygon2D(firstPolygon, 24f, py));
        Assert.True(sampler.PointInPolygon2D(firstPolygon + 1, 24f, py));
        Assert.Equal(firstPolygon, sampler.FindTerrainPolygon(24f, py));

        // From 2^-18 on, every term is exact and the point lands in the triangle that contains it.
        const float twoToTheMinus17 = 1f / 131072f;
        Assert.Equal(firstPolygon + 1, sampler.FindTerrainPolygon(24f, twoToTheMinus17));
        Assert.False(sampler.PointInPolygon2D(firstPolygon, 24f, twoToTheMinus17));
    }

    /// <summary>Barycentric height on the mesh triangle containing (x, y), found independently of the sampler.</summary>
    private static double MeshHeight(TerrainMesh mesh, float x, float y)
    {
        var best = double.NaN;
        var bestOutside = double.MaxValue;
        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            var a = mesh.Positions[mesh.Indices[t * 3]];
            var b = mesh.Positions[mesh.Indices[(t * 3) + 1]];
            var c = mesh.Positions[mesh.Indices[(t * 3) + 2]];
            var det = ((double)(b.Y - c.Y) * (a.X - c.X)) + ((double)(c.X - b.X) * (a.Y - c.Y));
            var l1 = (((double)(b.Y - c.Y) * (x - c.X)) + ((double)(c.X - b.X) * (y - c.Y))) / det;
            var l2 = (((double)(c.Y - a.Y) * (x - c.X)) + ((double)(a.X - c.X) * (y - c.Y))) / det;
            var l3 = 1 - l1 - l2;
            var outside = -Math.Min(0, Math.Min(l1, Math.Min(l2, l3)));
            if (outside < bestOutside)
            {
                bestOutside = outside;
                best = (l1 * a.Z) + (l2 * b.Z) + (l3 * c.Z);
            }
        }

        return best;
    }
}

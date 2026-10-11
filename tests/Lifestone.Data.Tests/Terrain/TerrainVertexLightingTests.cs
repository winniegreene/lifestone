using System.Numerics;
using Lifestone.Data.Terrain;
using Lifestone.Data.Terrain.Blending;
using Lifestone.Data.Tests.Terrain.Oracle;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Terrain;

/// <summary>Retail <c>CLandBlockStruct::calc_lighting</c> port: unit cases and real-dat cross-checks.</summary>
public sealed class TerrainVertexLightingTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly float[] LinearHeights = Enumerable.Range(0, 256).Select(i => i * 2f).ToArray();

    // A daytime keyframe from DayGroup 0 (rendering.md §8): DirBright 0.7, heading 90, pitch 20, DirColor FAD797,
    // AmbBright 0.35, AmbColor E6E6FF. sunVec = DirBright * (sinH cosP, cosH cosP, sinP).
    private static readonly TerrainLightingEnvironment Day = new(
        0.7f * new Vector3(MathF.Sin(MathF.PI / 2) * MathF.Cos(20 * MathF.PI / 180), MathF.Cos(MathF.PI / 2) * MathF.Cos(20 * MathF.PI / 180), MathF.Sin(20 * MathF.PI / 180)),
        0xFFFAD797u,
        0.35f,
        0xFFE6E6FFu);

    [Fact]
    public void Flat_landblock_has_up_normals_and_the_documented_light()
    {
        var terrain = Flat(heightIndex: 10);
        var normals = TerrainVertexLighting.ComputeNormals(terrain, LinearHeights);
        Assert.All(normals, n => Assert.Equal(Vector3.UnitZ, n));

        var env = new TerrainLightingEnvironment(new Vector3(0, 0, 0.8f), 0xFFFFFFFFu, 0.3f, 0xFF808080u);
        var light = TerrainVertexLighting.LightVertex(Vector3.UnitZ, env);
        var expected = 0.8 + (128 * (double)TerrainVertexLighting.ColorByteScale * 0.3);
        Assert.Equal(expected, light.R, 6);
        Assert.Equal(expected, light.G, 6);
        Assert.Equal(expected, light.B, 6);
    }

    [Fact]
    public void Light_clamps_above_one_only_and_ignores_back_facing_sun()
    {
        var bright = TerrainVertexLighting.LightVertex(Vector3.UnitZ, new TerrainLightingEnvironment(new Vector3(0, 0, 2f), 0xFFFF0000u, 0.1f, 0xFF000000u));
        Assert.Equal(new TerrainVertexLight(1f, 0f, 0f), bright);

        // Sun below the horizon: only ambient remains.
        var night = TerrainVertexLighting.LightVertex(Vector3.UnitZ, new TerrainLightingEnvironment(new Vector3(0, 0, -1f), 0xFFFFFFFFu, 0.5f, 0xFF00FF00u));
        Assert.Equal(0f, night.R);
        Assert.Equal(255 * (double)TerrainVertexLighting.ColorByteScale * 0.5, night.G, 6);
        Assert.Equal(0f, night.B);
    }

    [Fact]
    public void Vertex_colour_bytes_truncate_like_landPolyDraw()
    {
        var light = new TerrainVertexLight(0.5f, 1f, 0.999f);
        Assert.Equal(127, light.R8); // 127.5 truncates
        Assert.Equal(255, light.G8);
        Assert.Equal(254, light.B8); // 254.745 truncates
        Assert.Equal(0xFF7FFFFEu, light.ToArgb());
    }

    [Fact]
    public void Sky_lighting_is_floored_at_retail_min_ambient()
    {
        // LScape::UseTime: if (ambBright < LScape::min_ambient) ambBright = min_ambient, with min_ambient = 0.2.
        Assert.Equal(0.2f, TerrainVertexLighting.MinAmbientLevel);
        var sun = new Vector3(0.1f, 0.2f, 0.3f);
        var dark = TerrainLightingEnvironment.FromSkyLighting(sun, 0xFF102030u, 0.05f, 0xFF405060u);
        Assert.Equal(new TerrainLightingEnvironment(sun, 0xFF102030u, 0.2f, 0xFF405060u), dark);
        Assert.Equal(0.2f, TerrainLightingEnvironment.FromSkyLighting(sun, 0, 0.2f, 0).AmbientLevel);
        Assert.Equal(0.35f, TerrainLightingEnvironment.FromSkyLighting(sun, 0, 0.35f, 0).AmbientLevel);
        Assert.Equal(1.5f, TerrainLightingEnvironment.FromSkyLighting(sun, 0, 1.5f, 0).AmbientLevel);

        // Below the floor, the night light is the floor's ambient, not the raw AmbBright.
        var night = TerrainVertexLighting.LightVertex(Vector3.UnitZ, TerrainLightingEnvironment.FromSkyLighting(-sun, 0, 0f, 0xFFFFFFFFu));
        Assert.Equal(255 * (double)TerrainVertexLighting.ColorByteScale * 0.2, night.R, 6);
    }

    [RequiresDatsFact]
    public void Dereth_sky_keyframes_sit_above_the_ambient_floor()
    {
        // The floor is retail behaviour but never changes Dereth's keyframes; this records that fact.
        var keyframes = fixture.Dats.Region.SkyInfo.DayGroups.SelectMany(g => g.SkyTime).ToArray();
        Assert.NotEmpty(keyframes);
        var lowest = keyframes.Min(k => k.AmbBright);
        output.WriteLine($"{keyframes.Length} SkyTimeOfDay keyframes; lowest AmbBright {lowest}");
        Assert.True(lowest >= TerrainVertexLighting.MinAmbientLevel, $"lowest AmbBright {lowest}");
        Assert.All(keyframes, k => Assert.Equal(k.AmbBright, TerrainLightingEnvironment.FromSkyLighting(Vector3.Zero, 0, k.AmbBright, 0).AmbientLevel));
    }

    [Fact]
    public void Uniform_slope_gives_the_plane_normal_everywhere()
    {
        // Height index = x, so z = 2x metres every 24 m east: plane normal (-2, 0, 24) normalised.
        var words = new ushort[TerrainConstants.VertexCount];
        var heights = new byte[TerrainConstants.VertexCount];
        for (var x = 0; x < 9; x++)
        {
            for (var y = 0; y < 9; y++)
            {
                heights[TerrainConstants.VertexIndex(x, y)] = (byte)x;
            }
        }

        var normals = TerrainVertexLighting.ComputeNormals(new LandblockTerrain(new LandblockCoord(0x12, 0x34), words, heights), LinearHeights);
        var expected = Vector3.Normalize(new Vector3(-2, 0, 24));
        Assert.All(normals, n => Assert.True(Vector3.Distance(expected, n) < 1e-6f, $"{n} vs {expected}"));
    }

    [RequiresDatsFact]
    public void Holtburg_area_normals_match_TerrainMeshBuilder_and_OpenAC_and_lighting_matches_the_reference()
    {
        var region = fixture.Dats.TerrainRegion;
        var count = 0;
        foreach (var terrain in TerrainTestData.LandblocksAround(fixture.Dats, Lifestone.Data.Terrain.LandblockCoord.Holtburg, 4))
        {
            var normals = TerrainVertexLighting.ComputeNormals(terrain, region.LandHeightTable, region.SquareLength);
            var mesh = TerrainMeshBuilder.Build(terrain, region);
            for (var i = 0; i < normals.Length; i++)
            {
                Assert.True(Vector3.Distance(mesh.Normals[i], normals[i]) < 1e-6f, $"{terrain.Coord} vertex {i}: {normals[i]} vs {mesh.Normals[i]}");
            }

            AssertMatchesOpenAcNormals(terrain, region);
            AssertMatchesReference(terrain, region);
            count++;
        }

        Assert.Equal(81, count);
    }

    [Fact]
    public void OpenAC_split_formula_agrees_with_TerrainSplit_on_a_global_sample()
    {
        // The normals oracle uses its own statement of the CLandBlockStruct::ConstructPolygons split; make sure the two
        // agree everywhere so that a disagreement in the sweeps can only come from the normals themselves.
        var rng = new Random(11);
        for (var n = 0; n < 200_000; n++)
        {
            var coord = new LandblockCoord((byte)rng.Next(256), (byte)rng.Next(256));
            var (cx, cy) = (rng.Next(8), rng.Next(8));
            Assert.Equal(TerrainSplit.IsSWtoNE(coord, cx, cy), OpenAcVertexNormalsOracle.IsSWtoNE(coord.X, (uint)cx, coord.Y, (uint)cy));
        }
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_landblock_lighting_matches_the_double_precision_reference_and_OpenAC_normals()
    {
        var region = fixture.Dats.TerrainRegion;
        var count = 0;
        foreach (var terrain in TerrainTestData.AllLandblocks(fixture.Dats))
        {
            AssertMatchesOpenAcNormals(terrain, region);
            AssertMatchesReference(terrain, region);
            count++;
        }

        Assert.Equal(255 * 255, count);
    }

    private static void AssertMatchesOpenAcNormals(LandblockTerrain terrain, TerrainRegion region)
    {
        var actual = TerrainVertexLighting.ComputeNormals(terrain, region.LandHeightTable, region.SquareLength);
        var expected = OpenAcVertexNormalsOracle.BuildRetailVertexNormals(terrain, region.LandHeightTable);
        for (var i = 0; i < actual.Length; i++)
        {
            // OpenAC sums in a different order and normalises in float: allow a few float ulps.
            if (Vector3.Distance(actual[i], expected[i]) > 1e-5f)
            {
                Assert.Fail($"{terrain.Coord} vertex {i}: {actual[i]} vs OpenAC {expected[i]}");
            }
        }
    }

    private static void AssertMatchesReference(LandblockTerrain terrain, TerrainRegion region)
    {
        var actual = TerrainVertexLighting.Compute(terrain, region, Day);
        var expected = ReferenceLighting(terrain, region.LandHeightTable, Day);
        for (var i = 0; i < actual.Length; i++)
        {
            if (Math.Abs(actual[i].R - expected[i].X) > 1e-5 || Math.Abs(actual[i].G - expected[i].Y) > 1e-5 || Math.Abs(actual[i].B - expected[i].Z) > 1e-5)
            {
                Assert.Fail($"{terrain.Coord} vertex {i}: {actual[i]} vs reference {expected[i]}");
            }
        }
    }

    /// <summary>
    /// Independent double-precision statement of the formula in docs/research/rendering.md §2 (from
    /// CLandBlockStruct::calc_lighting): vertex normal = normalised sum of the unit normals of the block's triangles at
    /// that vertex (straight up below 2e-4), light = min(1, sunColor*max(0, n·sun) + ambColor*ambLevel). The cell split
    /// comes from the OpenAC oracle's formula, not from the product's <see cref="TerrainSplit"/>.
    /// </summary>
    private static (double X, double Y, double Z)[] ReferenceLighting(LandblockTerrain terrain, IReadOnlyList<float> heights, TerrainLightingEnvironment env)
    {
        var p = new (double X, double Y, double Z)[81];
        for (var x = 0; x < 9; x++)
        {
            for (var y = 0; y < 9; y++)
            {
                p[(x * 9) + y] = (x * 24.0, y * 24.0, heights[terrain.HeightIndex(x, y)]);
            }
        }

        var sum = new (double X, double Y, double Z)[81];
        void Add(int a, int b, int c)
        {
            var (ux, uy, uz) = (p[b].X - p[a].X, p[b].Y - p[a].Y, p[b].Z - p[a].Z);
            var (vx, vy, vz) = (p[c].X - p[a].X, p[c].Y - p[a].Y, p[c].Z - p[a].Z);
            var (nx, ny, nz) = ((uy * vz) - (uz * vy), (uz * vx) - (ux * vz), (ux * vy) - (uy * vx));
            var len = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
            foreach (var i in new[] { a, b, c })
            {
                sum[i] = (sum[i].X + (nx / len), sum[i].Y + (ny / len), sum[i].Z + (nz / len));
            }
        }

        for (var cx = 0; cx < 8; cx++)
        {
            for (var cy = 0; cy < 8; cy++)
            {
                int sw = (cx * 9) + cy, se = sw + 9, ne = sw + 10, nw = sw + 1;
                if (OpenAcVertexNormalsOracle.IsSWtoNE(terrain.Coord.X, (uint)cx, terrain.Coord.Y, (uint)cy))
                {
                    Add(sw, se, ne);
                    Add(sw, ne, nw);
                }
                else
                {
                    Add(sw, se, nw);
                    Add(ne, nw, se);
                }
            }
        }

        static double C(uint argb, int shift) => ((argb >> shift) & 0xFF) / 255.0;
        var result = new (double X, double Y, double Z)[81];
        for (var i = 0; i < 81; i++)
        {
            var len = Math.Sqrt((sum[i].X * sum[i].X) + (sum[i].Y * sum[i].Y) + (sum[i].Z * sum[i].Z));
            var n = len < 2e-4 ? (0.0, 0.0, 1.0) : (sum[i].X / len, sum[i].Y / len, sum[i].Z / len);
            var d = Math.Max(0, (n.Item1 * env.Sunlight.X) + (n.Item2 * env.Sunlight.Y) + (n.Item3 * env.Sunlight.Z));
            result[i] = (
                Math.Min(1, (C(env.SunlightColor, 16) * d) + (C(env.AmbientColor, 16) * env.AmbientLevel)),
                Math.Min(1, (C(env.SunlightColor, 8) * d) + (C(env.AmbientColor, 8) * env.AmbientLevel)),
                Math.Min(1, (C(env.SunlightColor, 0) * d) + (C(env.AmbientColor, 0) * env.AmbientLevel)));
        }

        return result;
    }

    private static LandblockTerrain Flat(byte heightIndex)
    {
        var heights = Enumerable.Repeat(heightIndex, TerrainConstants.VertexCount).ToArray();
        return new LandblockTerrain(new LandblockCoord(0x10, 0x20), new ushort[TerrainConstants.VertexCount], heights);
    }
}

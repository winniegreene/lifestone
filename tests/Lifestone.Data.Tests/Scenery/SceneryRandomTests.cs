using System.Numerics;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Data.Tests.Scenery;

/// <summary>
/// The retail scenery hashes, checked against an independent transcription in 64-bit integer arithmetic reduced
/// mod 2^32 (the formulas as printed in docs/research/rendering.md §4).
/// </summary>
public sealed class SceneryRandomTests
{
    private const long Mask = 0xFFFFFFFFL;

    public static TheoryData<uint, uint> Vertices()
    {
        var data = new TheoryData<uint, uint> { { 0, 0 }, { 2039, 2039 }, { 0xA9 * 8, 0xB3 * 8 }, { 0xA9 * 8 + 8, 0xB3 * 8 + 3 } };
        var random = new Random(1234);
        for (var i = 0; i < 40; i++)
        {
            data.Add((uint)random.Next(0, 2048), (uint)random.Next(0, 2048));
        }

        return data;
    }

    [Fact]
    public void Unit_scale_is_the_retail_float_constant_and_exactly_two_to_the_minus_32()
    {
        var retailFloatConstant = (double)2.3283064e-10f;
        Assert.Equal(SceneryRandom.UnitScale, retailFloatConstant);
        Assert.Equal(SceneryRandom.UnitScale, Math.ScaleB(1.0, -32));

        // ACE's double literal is slightly smaller; the difference is what a deviation looks like.
        Assert.True(2.3283064e-10 < SceneryRandom.UnitScale);
    }

    [Theory]
    [MemberData(nameof(Vertices))]
    public void Hashes_match_a_64_bit_transcription(uint gx, uint gy)
    {
        long x = gx, y = gy;
        Assert.Equal((uint)(((y * ((712977289L * x + 1813693831L) & Mask)) - 1109124029L * x + 2139937281L) & Mask), SceneryRandom.SceneHash(gx, gy));
        Assert.Equal((uint)((1813693831L * y - ((x * ((1870387557L * y + 1109124029L) & Mask)) & Mask) - 402451965L) & Mask), SceneryRandom.QuadrantHash(gx, gy));

        var m = (1360117743L * x * y + 1888038839L) & Mask;
        Assert.Equal((uint)m, SceneryRandom.VertexMultiplier(gx, gy));
        for (uint k = 0; k < 12; k++)
        {
            Assert.Equal((uint)((-1109124029L * x + 1813693831L * y - ((m * (23399 + k)) & Mask)) & Mask), SceneryRandom.FrequencyHash(gx, gy, k));
            foreach (var salt in new uint[] { 45773, 72719, 63127, 32593 })
            {
                var expected = (uint)((1813693831L * y - (((k + salt) * m) & Mask) - 1109124029L * x) & Mask);
                Assert.Equal(expected, SceneryRandom.ObjectHash(gx, gy, k, salt));
                Assert.Equal(expected * Math.ScaleB(1.0, -32), SceneryRandom.ObjectNoise(gx, gy, k, salt));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Vertices))]
    public void Frequency_hash_equals_the_retail_running_sum(uint gx, uint gy)
    {
        // CLandBlock::get_land_scenes keeps v17 = 23399 * m and adds m after each ObjectDesc.
        unchecked
        {
            var m = SceneryRandom.VertexMultiplier(gx, gy);
            var running = 23399u * m;
            for (uint k = 0; k < 40; k++)
            {
                Assert.Equal((0u - (1109124029u * gx)) + (1813693831u * gy) - running, SceneryRandom.FrequencyHash(gx, gy, k));
                running += m;
            }
        }
    }

    [Theory]
    [MemberData(nameof(Vertices))]
    public void Scene_index_is_floor_of_hash_times_count_and_always_in_range(uint gx, uint gy)
    {
        Assert.Equal(0, SceneryRandom.PickSceneIndex(gx, gy, 1));
        for (var count = 2; count <= 9; count++)
        {
            var index = SceneryRandom.PickSceneIndex(gx, gy, count);
            Assert.InRange(index, 0, count - 1);
            Assert.Equal((int)((ulong)SceneryRandom.SceneHash(gx, gy) * (ulong)count >> 32), index);
        }
    }

    [Theory]
    [InlineData(0.0, 1f, 2f, 1f, 2f)]
    [InlineData(0.30, 1f, 2f, -2f, 1f)]
    [InlineData(0.55, 1f, 2f, -1f, -2f)]
    [InlineData(0.80, 1f, 2f, 2f, -1f)]
    public void Place_rotates_the_offset_by_the_vertex_quadrant(double quadrantAtLeast, float baseX, float baseY, float expectX, float expectY)
    {
        // Find a vertex whose quadrant noise falls in the wanted quarter, then place a non-displaced object there.
        var (gx, gy) = FindVertex(q => q >= quadrantAtLeast && q < quadrantAtLeast + 0.15);
        var obj = new SceneObjectDesc(0x01000001, new Vector3(baseX, baseY, 0.5f), Quaternion.Identity, 1f, 0f, 0f, 1f, 1f, 0f, 0f, 1f, 0, 0, 0);
        Assert.Equal(new Vector3(expectX, expectY, 0.5f), SceneryGenerator.Place(obj, gx, gy, 0));
    }

    [Fact]
    public void Quadrant_in_double_never_disagrees_with_the_exact_quarter_over_the_whole_vertex_grid()
    {
        // OpenAC computes the quadrant in float (foundation-openac.md §3c deviation 4). In double the comparison with
        // 0.25/0.5/0.75 is exact; count how often float rounding would move a vertex across a quarter boundary.
        var floatDisagreements = 0;
        for (uint gx = 0; gx < 2048; gx++)
        {
            for (uint gy = 0; gy < 2048; gy++)
            {
                var hash = SceneryRandom.QuadrantHash(gx, gy);
                var exactQuarter = (int)(hash >> 30);
                Assert.Equal(exactQuarter, Quarter(SceneryRandom.QuadrantNoise(gx, gy)));
                if (Quarter((float)hash * 2.3283064e-10f) != exactQuarter)
                {
                    floatDisagreements++;
                }
            }
        }

        // Float rounding of hashes within 64 of a 2^30 multiple would flip the quarter; none occur on Dereth's grid.
        Assert.Equal(0, floatDisagreements);

        static int Quarter(double q) => q >= 0.75 ? 3 : q >= 0.5 ? 2 : q >= 0.25 ? 1 : 0;
    }

    [Fact]
    public void Scale_is_min_times_ratio_to_the_noise_and_max_when_equal()
    {
        var fixedScale = new SceneObjectDesc(0x01000001, Vector3.Zero, Quaternion.Identity, 1f, 0f, 0f, 1.3f, 1.3f, 0f, 0f, 1f, 0, 0, 0);
        Assert.Equal(1.3f, SceneryGenerator.Scale(fixedScale, 5, 7, 2));

        var ranged = fixedScale with { MinScale = 0.8f, MaxScale = 1.6f };
        for (uint k = 0; k < 20; k++)
        {
            var noise = SceneryRandom.ObjectNoise(1352, 1435, k, SceneryRandom.ScaleSalt);
            var expected = (float)(Math.Pow((double)1.6f / 0.8f, noise) * 0.8f); // 1.6f is exactly 2 * 0.8f
            var scale = SceneryGenerator.Scale(ranged, 1352, 1435, k);
            Assert.InRange(scale, 0.8f, 1.6f);
            Assert.Equal(expected, scale, 1e-6f);
        }
    }

    private static (uint Gx, uint Gy) FindVertex(Func<double, bool> predicate)
    {
        for (uint gx = 0; gx < 2048; gx++)
        {
            for (uint gy = 0; gy < 64; gy++)
            {
                if (predicate(SceneryRandom.QuadrantNoise(gx, gy)))
                {
                    return (gx, gy);
                }
            }
        }

        throw new InvalidOperationException("no vertex found");
    }
}

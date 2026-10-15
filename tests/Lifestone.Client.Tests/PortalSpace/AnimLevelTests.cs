using Lifestone.Client.PortalSpace;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>
/// Step PS3: <see cref="AnimLevel"/> is retail's table, built as <c>UIGlobals::Init</c> builds it and read as
/// <c>UIGlobals::GetAnimLevel</c> reads it (m2-log-in.md §4.6).
/// </summary>
public sealed class AnimLevelTests
{
    /// <summary>The 100 levels, pinned (they are arithmetic, not dat data).</summary>
    private static readonly ushort[] Pinned =
    [
        0, 0, 1, 3, 5, 7, 10, 14, 18, 22, 28, 33, 39, 46, 53, 60, 68, 76, 85, 94,
        104, 114, 124, 135, 146, 158, 170, 182, 195, 208, 221, 235, 248, 262, 277, 291, 306, 321, 336, 352,
        367, 383, 399, 415, 431, 447, 463, 479, 495, 512, 528, 544, 560, 576, 592, 608, 624, 640, 656, 671,
        687, 702, 717, 732, 746, 761, 775, 788, 802, 815, 828, 841, 853, 865, 877, 888, 899, 909, 919, 929,
        938, 947, 955, 963, 970, 977, 984, 990, 995, 1001, 1005, 1009, 1013, 1016, 1018, 1020, 1022, 1023, 1024, 1024,
    ];

    [Fact]
    public void The_table_matches_an_independent_derivation_in_decimal_arithmetic()
    {
        // The same construction, written separately: retail's two literals, an exact-enough sine (a decimal Taylor
        // series, not Math.Sin), truncation, and integer accumulation.
        var raw = new long[100];
        for (var i = 0; i < 100; i++)
        {
            raw[i] = (long)decimal.Floor(DecimalSin(i * 3.141592m * 0.0101010101010101m) * 1024m);
        }

        var total = raw.Sum();
        Assert.Equal(64_478, total);
        long running = 0;
        for (var i = 0; i < 100; i++)
        {
            running += raw[i];
            Assert.Equal((ushort)raw[i], AnimLevel.RawSine[i]);
            Assert.Equal(running * 1024 / total, AnimLevel.Table[i]);
        }
    }

    [Fact]
    public void The_table_is_pinned()
    {
        Assert.Equal(Pinned, AnimLevel.Table.ToArray());
    }

    [Fact]
    public void The_table_rises_from_0_to_1024_and_never_falls()
    {
        var table = AnimLevel.Table;

        Assert.Equal(AnimLevel.Count, table.Length);
        Assert.Equal(0, table[0]);
        Assert.Equal(AnimLevel.Full, table[99]);
        for (var i = 1; i < table.Length; i++)
        {
            Assert.True(table[i] >= table[i - 1], $"level[{i}] = {table[i]} < level[{i - 1}] = {table[i - 1]}");
        }
    }

    [Fact]
    public void Quarter_half_and_three_quarters_are_the_spec_values()
    {
        Assert.Equal(146, AnimLevel.Level(0.25f)); // 0.143
        Assert.Equal(512, AnimLevel.Level(0.5f)); // exactly 0.5
        Assert.Equal(877, AnimLevel.Level(0.75f)); // 0.856
        Assert.Equal(0.142578125, AnimLevel.Fraction(0.25f));
        Assert.Equal(0.5, AnimLevel.Fraction(0.5f));
        Assert.Equal(0.8564453125, AnimLevel.Fraction(0.75f));
    }

    [Fact]
    public void It_eases_like_half_one_minus_cosine()
    {
        var worst = 0.0;
        for (var k = 0; k <= 1000; k++)
        {
            var t = k / 1000.0;
            var deviation = Math.Abs(AnimLevel.Fraction((float)t) - ((1.0 - Math.Cos(Math.PI * t)) / 2.0));
            worst = Math.Max(worst, deviation);
        }

        Assert.True(worst < 0.02, $"largest deviation from (1 - cos pi t) / 2 is {worst:F4}");
    }

    [Theory]
    [InlineData(-0.5f, 0)]
    [InlineData(0f, 0)]
    [InlineData(1f, 1024)]
    [InlineData(1.5f, 1024)]
    [InlineData(float.PositiveInfinity, 1024)]
    [InlineData(float.NegativeInfinity, 0)]
    [InlineData(float.NaN, 0)]
    public void Percentages_are_clamped_to_0_and_1(float percentage, int level)
    {
        Assert.Equal(level, AnimLevel.Level(percentage));
    }

    [Fact]
    public void The_index_truncates_the_widened_float()
    {
        // GetAnimLevel indexes at the truncated 99 · p, with p the float widened to double: 10/99 as a float is a hair
        // below 10/99, so it reads entry 9, not 10.
        var p = (float)(10 / 99.0);
        Assert.True(p * 99.0 < 10.0);
        Assert.Equal(Pinned[9], AnimLevel.Level(p));
        Assert.Equal(Pinned[10], AnimLevel.Level(BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(p) + 1)));
        Assert.Equal(Pinned[98], AnimLevel.Level(0.99f)); // 98.01
        Assert.Equal(Pinned[97], AnimLevel.Level(0.98f)); // 97.02
    }

    /// <summary>sin(x) by its Taylor series in decimal arithmetic (28 significant digits; x is at most about π).</summary>
    private static decimal DecimalSin(decimal x)
    {
        var term = x;
        var sum = x;
        for (var n = 1; n < 40; n++)
        {
            term = -term * x * x / ((2 * n) * ((2 * n) + 1));
            sum += term;
            if (Math.Abs(term) < 1e-26m)
            {
                break;
            }
        }

        return sum;
    }
}

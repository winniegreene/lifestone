using System.Numerics;
using Lifestone.Data.World.Sky;

namespace Lifestone.Data.Tests.Sky;

/// <summary>
/// <c>SkyDesc::CalcPresentDayGroup</c> (CSkyDesc.c acclient.c:301663, 11.4186; same expression in 11.6096 at
/// acclient.c:295047):
/// <c>floor((double)(unsigned)(1782775218 * (day + daysPerYear * year) - 1967253934) * 2.3283064e-10 * numGroups)</c>.
/// </summary>
public sealed class DayGroupSelectionTests(SkyFixture fixture) : IClassFixture<SkyFixture>
{
    /// <summary>Independent oracle: arbitrary-precision arithmetic, reduced mod 2^32 like the int32 wrap.</summary>
    private static int Oracle(int day, int daysPerYear, int year, int numGroups)
    {
        var seed = (BigInteger)day + ((BigInteger)daysPerYear * year);
        var hash = ((1782775218 * seed) - 1967253934) % (BigInteger.One << 32);
        if (hash < 0)
        {
            hash += BigInteger.One << 32;
        }

        var index = (int)((hash * numGroups) >> 32);
        return index < numGroups ? index : 0;
    }

    /// <summary>The retail floating-point expression, evaluated in double with the float constant 2.3283064e-10f.</summary>
    private static int RetailDouble(int day, int daysPerYear, int year, int numGroups)
    {
        var hash = SkyDescription.DayGroupHash(day, daysPerYear, year);
        var index = (ulong)Math.Floor(hash * (double)2.3283064e-10f * numGroups);
        return index < (ulong)numGroups ? (int)index : 0;
    }

    [Fact]
    public void Constant_is_exactly_two_to_the_minus_32()
    {
        // Hex-Rays prints float literals with ~8 significant digits; the float nearest 2.3283064e-10 is 2^-32 exactly,
        // which makes floor(hash * 2^-32 * n) == (hash * n) >> 32 at 53-bit precision.
        Assert.Equal((double)2.3283064e-10f, Math.ScaleB(1.0, -32));
    }

    [Fact]
    public void Hash_uses_wrapping_int32_arithmetic()
    {
        Assert.Equal(unchecked((uint)-1967253934), SkyDescription.DayGroupHash(0, 360, 0));
        Assert.Equal(unchecked((uint)(1782775218 - 1967253934)), SkyDescription.DayGroupHash(1, 360, 0));
        Assert.Equal(unchecked((uint)((1782775218 * 3600) - 1967253934)), SkyDescription.DayGroupHash(0, 360, 10));
    }

    [Fact]
    [Trait("Tier", "Exhaustive")]
    public void Integer_form_equals_the_oracle_and_the_retail_double_expression()
    {
        for (var numGroups = 1; numGroups <= 33; numGroups++)
        {
            for (var year = 0; year <= 420; year += numGroups == 20 ? 1 : 17)
            {
                for (var day = 0; day < 360; day++)
                {
                    var expected = Oracle(day, 360, year, numGroups);
                    Assert.Equal(expected, SkyDescription.CalcPresentDayGroup(day, 360, year, numGroups));
                    Assert.Equal(expected, RetailDouble(day, 360, year, numGroups));
                }
            }
        }
    }

    [Fact]
    public void Single_group_always_selects_zero()
    {
        for (var day = 0; day < 360; day++)
        {
            Assert.Equal(0, SkyDescription.CalcPresentDayGroup(day, 360, 121, 1));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => SkyDescription.CalcPresentDayGroup(0, 360, 10, 0));
    }

    [RequiresDatsFact]
    public void Day_group_changes_only_at_midnight_and_follows_the_calendar_day()
    {
        var sky = fixture.Sky;
        var c = sky.Calendar;
        for (var day = 0; day < 360; day++)
        {
            var expected = Oracle(day, c.DaysPerYear, 121, sky.Sky.DayGroups.Count);
            var morning = c.ToGameTime(121, day, 0.0);
            Assert.Equal(expected, sky.DayGroupIndexAt(morning));
            Assert.Equal(expected, sky.DayGroupIndexAt(c.ToGameTime(121, day, 0.999)));
            Assert.Equal(expected, sky.Evaluate(morning + 1234.5).DayGroup.Index);
        }
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_day_group_is_reachable_over_ten_years()
    {
        var sky = fixture.Sky;
        var counts = new int[sky.Sky.DayGroups.Count];
        for (var year = 115; year < 125; year++)
        {
            for (var day = 0; day < 360; day++)
            {
                counts[sky.DayGroupIndexAt(sky.Calendar.ToGameTime(year, day, 0.5))]++;
            }
        }

        Assert.All(counts, n => Assert.True(n > 0));
        Assert.Equal(3600, counts.Sum());
    }
}

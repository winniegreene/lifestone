using Lifestone.Client.Audio;
using Lifestone.Client.PortalSpace;

namespace Lifestone.Client.Tests.Audio;

/// <summary>
/// Step PS4: <see cref="MsvcCrtRandom"/> is the Microsoft C runtime's <c>rand()</c>, and its <c>RandDouble</c> is
/// retail's, with both ends reachable.
/// </summary>
public sealed class CrtRandomTests
{
    [Fact]
    public void Seed_1_gives_the_well_known_msvc_sequence()
    {
        var random = new MsvcCrtRandom(1);

        Assert.Equal([41, 18467, 6334, 26500, 19169, 15724, 11478, 29358, 26962, 24464], Draw(random, 10));
    }

    [Fact]
    public void An_unseeded_generator_starts_as_srand_1()
    {
        Assert.Equal(Draw(new MsvcCrtRandom(1), 100), Draw(new MsvcCrtRandom(), 100));
    }

    [Fact]
    public void Values_stay_within_0_and_rand_max()
    {
        var random = new MsvcCrtRandom(12345);
        var min = int.MaxValue;
        var max = int.MinValue;
        for (var i = 0; i < 200_000; i++)
        {
            var value = random.Rand();
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        Assert.Equal(0, min);
        Assert.Equal(MsvcCrtRandom.RandMax, max);
    }

    [Fact]
    public void The_same_seed_gives_the_same_sequence_and_seed_restarts_it()
    {
        var a = new MsvcCrtRandom(0xC0FFEE);
        var b = new MsvcCrtRandom(0xC0FFEE);
        var first = Draw(a, 50);

        Assert.Equal(first, Draw(b, 50));
        Assert.NotEqual(first, Draw(new MsvcCrtRandom(0xC0FFEF), 50));
        a.Seed(0xC0FFEE);
        Assert.Equal(first, Draw(a, 50));
    }

    [Fact]
    public void Rand_double_reaches_both_ends()
    {
        // 0.00003051850947599719 is 1/32767: rand() 32767 scales to exactly 1.
        Assert.Equal(1.0, MsvcCrtRandom.RandMax * MsvcCrtRandom.RandDoubleScale);

        Assert.Equal(0.6, MsvcCrtRandom.RandDoubleFrom(0, 0.6, 1.8));
        Assert.Equal(1.8, MsvcCrtRandom.RandDoubleFrom(MsvcCrtRandom.RandMax, 0.6, 1.8), 1e-15);
        Assert.Equal(0.0, MsvcCrtRandom.RandDoubleFrom(0, 0.0, 360.0));
        Assert.Equal(360.0, MsvcCrtRandom.RandDoubleFrom(MsvcCrtRandom.RandMax, 0.0, 360.0));
        Assert.Equal(180.0054933317057, MsvcCrtRandom.RandDoubleFrom(16384, 0.0, 360.0), 1e-12);
    }

    [Fact]
    public void Rand_double_draws_the_next_rand()
    {
        IPortalRandom roll = new MsvcCrtRandom(1);

        // rand() gives 41, then 18467.
        Assert.Equal(MsvcCrtRandom.RandDoubleFrom(41, 0.6, 1.8), roll.RandDouble(0.6, 1.8));
        Assert.Equal(MsvcCrtRandom.RandDoubleFrom(18467, 0.0, 360.0), roll.RandDouble(0.0, 360.0));
    }

    [Fact]
    public void Rand_double_stays_within_its_bounds()
    {
        var random = new MsvcCrtRandom(7);
        for (var i = 0; i < 100_000; i++)
        {
            var duration = random.RandDouble(0.6, 1.8);
            Assert.InRange(duration, 0.6, 1.8 + 1e-15);
        }
    }

    private static int[] Draw(MsvcCrtRandom random, int count)
    {
        var values = new int[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = random.Rand();
        }

        return values;
    }
}

using Lifestone.Data.Effects;

namespace Lifestone.Data.Tests.Effects;

/// <summary>
/// <see cref="RetailRandom"/> against an independent textbook formulation of L'Ecuyer's combined generator with a
/// 32-entry Bays–Durham shuffle, using 64-bit arithmetic instead of Schrage's trick (constants as in acclient
/// <c>Random::rand</c>: 40014/2147483563, 40692/2147483399, NDIV 67108862, scale 4.656613057391769e-10, cap 0.99999988).
/// </summary>
public sealed class RetailRandomTests
{
    private sealed class TextbookGenerator
    {
        private readonly long[] _table = new long[32];
        private long _a;
        private long _b;
        private long _y;

        public TextbookGenerator(int seed)
        {
            _a = seed == 0 ? 1 : seed;
            _b = _a;
            for (var j = 39; j >= 0; j--)
            {
                _a = Mod(40014 * _a, 2147483563);
                if (j < 32)
                {
                    _table[j] = _a;
                }
            }

            _y = _table[0];
        }

        public double Next()
        {
            _a = Mod(40014 * _a, 2147483563);
            _b = Mod(40692 * _b, 2147483399);
            var j = (int)(_y / 67108862);
            _y = _table[j] - _b;
            _table[j] = _a;
            if (_y < 1)
            {
                _y += 2147483562;
            }

            return Math.Min(_y * 4.656613057391769e-10, 0.99999988);
        }

        // Retail keeps values in (−m, m) and adds m when negative, i.e. the non-negative residue (for positive seeds).
        private static long Mod(long v, long m) => ((v % m) + m) % m;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12345)]
    [InlineData(0)]
    [InlineData(2147483000)]
    public void Matches_the_textbook_generator(int seed)
    {
        var retail = new RetailRandom(seed);
        var oracle = new TextbookGenerator(seed);
        for (var i = 0; i < 10_000; i++)
        {
            var r = retail.Rand();
            Assert.Equal(oracle.Next(), r);
            Assert.InRange(r, 0.0, 0.99999988);
        }
    }

    [Fact]
    public void Roll_dice_maps_into_the_range_and_swaps_reversed_bounds()
    {
        var a = new RetailRandom(7);
        var b = new RetailRandom(7);
        for (var i = 0; i < 1000; i++)
        {
            var x = a.RollDice(-1f, 1f);
            Assert.InRange(x, -1.0, 1.0);
            Assert.Equal(x, b.RollDice(1f, -1f));
        }

        Assert.Equal(3.0, a.RollDice(3f, 3f)); // equal bounds return the bound without drawing
    }
}

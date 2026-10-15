namespace Lifestone.Client.RetailUi.CharacterCreation;

/// <summary>
/// The random numbers character creation draws, in the two forms retail uses: the C runtime's <c>rand()</c> (0-32767,
/// behind <c>RandInt</c> and <c>CharGenState::GetRandomReal</c>) and <c>Random::rand</c> (a double in [0, 1), behind
/// <c>Random::RollDice</c>). Injected so that randomisation is deterministic under test.
/// </summary>
public interface ICharGenRandom
{
    /// <summary>The next C-runtime <c>rand()</c> value, 0-32767.</summary>
    int NextRand15();

    /// <summary>The next <c>Random::rand</c> value, in [0, 0.99999988].</summary>
    double NextUnit();
}

/// <summary>
/// A seeded <see cref="ICharGenRandom"/> over <see cref="Random"/>: the same seed gives the same characters. Retail's
/// generators (the MSVC runtime's and the L'Ecuyer generator of <c>Random::rand</c>, 11.4186 0x0042C4C0, 11.6096
/// 0x0042C720) are not reproduced; only how their values are turned into choices is (<see cref="CharGenDice"/>).
/// </summary>
public sealed class CharGenRandom : ICharGenRandom
{
    /// <summary><c>Random::rand</c>'s largest value (it clamps to 0.99999988).</summary>
    private const double MaxUnit = 0.99999988;

    private readonly Random _random;

    /// <summary>A generator seeded with <paramref name="seed"/>.</summary>
    public CharGenRandom(int seed) => _random = new Random(seed);

    /// <inheritdoc />
    public int NextRand15() => _random.Next(32768);

    /// <inheritdoc />
    public double NextUnit() => Math.Min(_random.NextDouble(), MaxUnit);
}

/// <summary>How retail turns random numbers into character-creation choices.</summary>
public static class CharGenDice
{
    /// <summary><c>CharGenState::GetRandomReal</c> (11.4186 0x00563940, 11.6096 0x005646E0): <c>rand()</c> / 32767, in [0, 1].</summary>
    public static double RandomReal(ICharGenRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return random.NextRand15() * 0.00003051850947599719;
    }

    /// <summary><c>RandInt(range)</c> (11.4186 0x00684400, 11.6096 0x00685400): <c>range × rand() / 32768</c>, in [0, range).</summary>
    public static int RandInt(ICharGenRandom random, int range)
    {
        ArgumentNullException.ThrowIfNull(random);
        return (int)((long)range * random.NextRand15() / 0x8000);
    }

    /// <summary>
    /// <c>RandInt(range, exclude)</c> (11.4186 0x00684420, 11.6096 0x00685420): as <see cref="RandInt(ICharGenRandom, int)"/>,
    /// drawn again while it equals <paramref name="exclude"/>; 0 when <paramref name="range"/> is 1 or less.
    /// </summary>
    public static int RandInt(ICharGenRandom random, int range, int exclude)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (range <= 1)
        {
            return 0;
        }

        int result;
        do
        {
            result = RandInt(random, range);
        }
        while (result == exclude);
        return result;
    }

    /// <summary>
    /// <c>Random::RollDice(int, int)</c> (11.4186 0x0042C5C0, 11.6096 0x0042C820): a whole number from
    /// <paramref name="lower"/> to <paramref name="upper"/> inclusive (the bounds in either order).
    /// </summary>
    public static int RollDice(ICharGenRandom random, int lower, int upper)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (lower == upper)
        {
            return lower;
        }

        var (low, high) = upper < lower ? (upper, lower) : (lower, upper);
        return low + (int)(ulong)(random.NextUnit() * (uint)(high - low + 1));
    }
}

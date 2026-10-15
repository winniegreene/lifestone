using Lifestone.Client.PortalSpace;

namespace Lifestone.Client.Audio;

/// <summary>
/// The C runtime's <c>rand()</c> as retail calls it: an integer from 0 to 32767. Two retail rolls use it: the tunnel
/// roll's <c>RandDouble</c> (<see cref="IPortalRandom"/>) and the sound manager's play-probability roll
/// (<c>SoundManager::PlayProbability</c>, 11.4186 0x005500E0, 11.6096 0x00550CF0).
/// </summary>
public interface ICrtRandom
{
    /// <summary>The next value, 0 to <see cref="MsvcCrtRandom.RandMax"/> inclusive.</summary>
    int Rand();
}

/// <summary>
/// The Microsoft C runtime's <c>rand()</c> (the published linear congruential generator of Microsoft's C runtimes), and
/// retail's <c>RandDouble</c> over it.
/// </summary>
/// <remarks>
/// <para><b>The generator</b> is public, not Turbine code: state = state × 214013 + 2531011 (mod 2³²), and
/// <c>rand()</c> returns bits 16 to 30 of the new state. Its initial state is 1 (as <c>srand(1)</c>).</para>
/// <para><b>RandDouble</b> re-expresses retail <c>RandDouble</c> (11.4186 0x006843D0, 11.6096 0x006853D0) by function
/// name: <c>rand()</c> × 0.00003051850947599719 × (max − min) + min, evaluated in that order. The literal is 1/32767
/// (32767 times it is exactly 1.0 in double), so both ends are reachable: <c>rand()</c> 0 gives min and 32767 gives max.
/// Retail's tunnel roll draws its durations from [0.6, 1.8] s and its angles from [0°, 360°], both inclusive.</para>
/// <para><b>Precision.</b> Retail evaluates on the x87 under whatever precision control the process runs with; Lifestone
/// evaluates in double. The last bits can differ (at 32767, (1.8 − 0.6) + 0.6 is 1.8000000000000003 in double); nothing
/// in portal space depends on them.</para>
/// <para><b>Seeding (a documented deviation).</b> Retail shares one C-runtime state across the whole client, so its
/// sequence depends on every other caller. Lifestone gives each consumer its own instance, as M1 does for effects: a
/// host seeds it from a clock in live play and with a fixed value for captures and tests, so a run is reproducible.</para>
/// <para>Not thread-safe; one instance per consumer, on its thread. Allocation-free.</para>
/// </remarks>
public sealed class MsvcCrtRandom : ICrtRandom, IPortalRandom
{
    /// <summary><c>RAND_MAX</c>: the largest value <see cref="Rand"/> returns.</summary>
    public const int RandMax = 0x7FFF;

    /// <summary>The C runtime's initial state (a program that never calls <c>srand</c> starts here).</summary>
    public const uint DefaultSeed = 1;

    /// <summary>Retail <c>RandDouble</c>'s scale, 1/32767 as a double literal.</summary>
    public const double RandDoubleScale = 0.00003051850947599719;

    private const uint Multiplier = 214013;
    private const uint Increment = 2531011;

    private uint _state;

    /// <summary>Creates a generator in the state <c>srand(<paramref name="seed"/>)</c> leaves.</summary>
    public MsvcCrtRandom(uint seed = DefaultSeed) => _state = seed;

    /// <summary>Restarts the sequence (<c>srand</c>).</summary>
    public void Seed(uint seed) => _state = seed;

    /// <inheritdoc/>
    public int Rand()
    {
        _state = unchecked((_state * Multiplier) + Increment);
        return (int)((_state >> 16) & RandMax);
    }

    /// <summary>Retail <c>RandDouble(min, max)</c> over the next <see cref="Rand"/>: a value in [min, max], both ends included.</summary>
    public double RandDouble(double min, double max) => RandDoubleFrom(Rand(), min, max);

    /// <summary>Retail <c>RandDouble</c>'s arithmetic for a given <c>rand()</c> value <paramref name="rand"/>.</summary>
    public static double RandDoubleFrom(int rand, double min, double max) => (rand * RandDoubleScale * (max - min)) + min;
}

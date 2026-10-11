namespace Lifestone.Data.Effects;

/// <summary>
/// The random source used by the particle and script code. Retail calls one global generator,
/// <c>Random::RollDice(float lower, float upper)</c>, at exactly these places (acclient 11.4186):
/// <list type="number">
/// <item><c>ParticleEmitter::EmitParticle</c>, per particle, in this order: <c>GetRandomLifespan</c>,
/// <c>GetRandomFinalTrans</c>, <c>GetRandomStartTrans</c>, <c>GetRandomFinalScale</c>, <c>GetRandomStartScale</c>
/// (one draw each, range [−1, 1]), <c>GetRandomC</c>, <c>GetRandomB</c>, <c>GetRandomA</c> (one draw each, [0, 1]),
/// <c>GetRandomOffset</c> (three draws in [−1, 1] for z, y, x — in that order — then one in [0, 1] unless the
/// direction is degenerate).</item>
/// <item><c>Particle::Init</c> for <c>Explode</c> particles: two draws in [−π, π] (azimuth, then elevation).</item>
/// <item><c>CPhysicsObj::CallPES</c>: one draw in [0, pause] for the delay of a CallPES hook.</item>
/// </list>
/// Everything else in the effect pipeline is deterministic.
/// </summary>
public interface IEffectRandom
{
    /// <summary>Retail <c>Random::RollDice(float, float)</c>: uniform in [lower, upper) (bounds swapped if reversed).</summary>
    double RollDice(float lower, float upper);
}

/// <summary>
/// A seedable re-implementation of retail's generator (<c>Random::Seed</c>, <c>Random::rand</c>,
/// <c>Random::RollDice</c>; acclient 11.4186 functions at 0x42C460–0x42C600, same code in 11.6096 at 0x42C6B0):
/// L'Ecuyer's two-modulus combined generator with a 32-entry Bays–Durham shuffle (the classic "ran2" algorithm),
/// output scaled by 1/2147483563 and capped at 0.99999988.
/// </summary>
/// <remarks>
/// Retail seeds its single global instance once from <c>time(0)</c> (<c>Random::Seed()</c>) and shares it with all
/// game code, so retail particle randomness is not reproducible. Here each effect host gets its own seeded
/// instance: the sequence of draws per emitter is retail-shaped, and a fixed seed makes a simulation deterministic.
/// </remarks>
public sealed class RetailRandom : IEffectRandom
{
    private const int Im1 = 2147483563;
    private const int Im2 = 2147483399;
    private const int Ia1 = 40014;
    private const int Ia2 = 40692;
    private const int Iq1 = 53668;
    private const int Iq2 = 52774;
    private const int Ir1 = 12211;
    private const int Ir2 = 3791;
    private const int NTab = 32;
    private const int NDiv = 1 + (Im1 - 1) / NTab; // 67108862, as in Random::rand
    private const double Am = 4.656613057391769e-10;
    private const double RnMax = 0.99999988;

    private readonly int[] _iv = new int[NTab];
    private int _seed;
    private int _idum2;
    private int _iy;

    public RetailRandom(int seed) => Seed(seed);

    /// <summary>Retail <c>Random::Seed(int)</c>: 0 is replaced by 1; 40 warm-up steps fill the 32-entry table.</summary>
    public void Seed(int seed)
    {
        var v = seed == 0 ? 1 : seed;
        _idum2 = v;
        for (var j = NTab + 7; j >= 0; j--)
        {
            v = Step(v, Ia1, Iq1, Ir1, Im1);
            if (j < NTab)
            {
                _iv[j] = v;
            }
        }

        _iy = _iv[0];
        _seed = v;
    }

    /// <summary>Retail <c>Random::rand()</c>: a double in (0, 0.99999988].</summary>
    public double Rand()
    {
        _seed = Step(_seed, Ia1, Iq1, Ir1, Im1);
        _idum2 = Step(_idum2, Ia2, Iq2, Ir2, Im2);
        var j = _iy / NDiv;
        _iy = _iv[j] - _idum2;
        _iv[j] = _seed;
        if (_iy < 1)
        {
            _iy += Im1 - 1;
        }

        var result = _iy * Am;
        return result > RnMax ? RnMax : result;
    }

    public double RollDice(float lower, float upper)
    {
        if (lower == upper)
        {
            return lower;
        }

        if (upper < lower)
        {
            (lower, upper) = (upper, lower);
        }

        return Rand() * ((double)upper - lower) + lower;
    }

    /// <summary>Schrage's method, identical to retail's 32-bit expression <c>a·v − m·(v / q)</c> (+m when negative).</summary>
    private static int Step(int v, int a, int q, int r, int m)
    {
        var k = v / q;
        var next = a * (v - k * q) - k * r;
        return next < 0 ? next + m : next;
    }
}

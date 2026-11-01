// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Creature_Magic.cs (CalculateManaUsage lines 14-56, GetManaCost 58-100), WorldObjects/SkillCheck.cs
// (GetSkillChance 9-14) and Entity/SpellProperties.cs (PowerMod, line 76) (AGPL-3.0) [with changes: the random source is a delegate the caller owns (ACE's is ThreadSafeRandom.Next(0.0f, 1.0f),
// a double in [0, 1)); the weapon's ManaConversionMod and the spell's flags are the caller's to apply (it passes the skill it computed and "ignores Mana Conversion" as a flag); and the
// model adds what ACE does not have: the exact distribution of the rolled cost (LandingChance, ExpectedCost) and the least mana worth a try (TryFloor), derived from the same two rolls].
namespace Lifestone.Client.Session.Casting;

/// <summary>
/// ACE's Mana Conversion model (M5 item 1, lane K2): what a cast costs, and the odds that a cast short of its base cost lands.
/// <para><b>The cost</b> (<c>Creature.GetManaCost</c>, <c>Creature_Magic.cs</c> 58-100 @95ef426d). A caster with Mana Conversion trained, for a spell that does not ignore it, rolls twice: against the spell's
/// PowerMod / 2 (integer division; PowerMod is the power, at least 25) and then against PowerMod, with <see cref="SkillChance(uint, uint, float)"/> (factor 0.03) as the success chance. The first roll and ONE luck roll are
/// always drawn; each success lowers the cost to <c>Math.Round(cost * (1.0 - (chance - roll * luck)))</c> (C# <c>Math.Round(double)</c>: to even), and the second roll is drawn only when the cost is still over 1.
/// The cost is at least 1. Untrained, or a spell flagged <c>IgnoresManaConversion</c>, pays the base (<c>CalculateManaUsage</c> 41-45).</para>
/// <para><b>The refusal</b> (<c>Player_Magic.cs</c> 566-585). ACE rolls the cost once per cast attempt, before the spell's words: a cost over the mana held is answered UseDone 0x0401 and NOTHING is charged, so
/// every press is a fresh roll, and <see cref="LandingChance"/> (the chance that the roll fits the mana held) is the chance a press lands. A fizzle costs 5 (<see cref="FizzleMana"/>), not a roll.</para>
/// <para><b>Exact, not sampled.</b> <see cref="LandingChance"/> integrates the three independent uniform draws (first roll, luck, second roll) analytically: for a given luck the cost after the first stage takes
/// each value k over an interval of the first roll whose length is closed-form, and the second stage lands at or under the mana held with a probability that is a minimum of a constant and a power of 1 / luck; the
/// product is a sum of constants, 1 / luck and 1 / luck^2 over at most four luck intervals per k, integrated with logarithms. The only approximation is the model's: the draws are continuous uniforms, where
/// ACE's are doubles (and the scripted caster's are floats, 2^-24 apart); a tie at a half cent of mana is a measure-zero event the model ignores. The floating-point error of the sum is under 1e-12; the tests hold it
/// to the table of m5-plan.md section 8 item 0, to a deterministic quasi-random integration of the cost itself (an independent method) at 2e-3, and to a seeded sample of <see cref="RollCost"/>. It allocates nothing:
/// a call is O(base mana) arithmetic on the stack, so the keeper may ask per decision.</para>
/// </summary>
public static class ManaConversionModel
{
    /// <summary>ACE's default skill-check factor (<c>SkillCheck.GetSkillChance</c>, a float 0.03f).</summary>
    public const float DefaultFactor = 0.03f;

    /// <summary>The least PowerMod: <c>SpellProperties.PowerMod</c> is <c>Math.Max(Power, 25)</c> (line 76).</summary>
    public const uint MinimumPowerMod = 25;

    /// <summary>What a fizzle costs (<c>Player_Magic.cs</c> 571-572, "todo: verify with retail"): five mana, not a roll.</summary>
    public const uint FizzleMana = 5;

    /// <summary>The chance a try must have to be worth sending (D15: 25% a try).</summary>
    public const double DefaultTryChance = 0.25;

    /// <summary>The least mana a try is sent with (D15: never under a fizzle's cost).</summary>
    public const uint DefaultTryMinimum = FizzleMana;

    /// <summary>ACE's <c>SpellProperties.PowerMod</c> (line 76): the power, at least 25.</summary>
    public static uint PowerMod(uint power) => Math.Max(power, MinimumPowerMod);

    /// <summary>
    /// ACE's <c>SkillCheck.GetSkillChance(int, int, float)</c> (<c>SkillCheck.cs</c> 9-14): <c>1 - 1 / (1 + e^(factor * (skill - difficulty)))</c>, clamped to 0..1. The product of the float factor and the
    /// integer difference is a float, as ACE's is.
    /// </summary>
    public static double SkillChance(int skill, int difficulty, float factor = DefaultFactor)
    {
        var chance = 1.0 - (1.0 / (1.0 + Math.Exp(factor * (skill - difficulty))));
        return Math.Min(1.0, Math.Max(0.0, chance));
    }

    /// <summary>ACE's <c>SkillCheck.GetSkillChance(uint, uint, float)</c> (lines 16-19): the unsigned arguments cast to int.</summary>
    public static double SkillChance(uint skill, uint difficulty, float factor = DefaultFactor) => SkillChance((int)skill, (int)difficulty, factor);

    /// <summary>
    /// One roll of the cost: <c>Creature.GetManaCost</c> (<c>Creature_Magic.cs</c> 58-100) behind <c>CalculateManaUsage</c>'s gate (41-45). The draws are ACE's, in ACE's order: the first roll, the luck roll, then the
    /// second roll only when the first stage left the cost over 1; none when the cost is the base. <paramref name="next"/> returns a uniform number in [0, 1) (ACE: <c>ThreadSafeRandom.Next(0.0f, 1.0f)</c>, a
    /// double; a lambda that returns a float converts).
    /// </summary>
    /// <param name="baseMana">The spell's base mana (with the armour lines' ManaMod per worn piece already added, as <c>CalculateManaUsage</c> does).</param>
    /// <param name="power">The spell's power (the difficulty is <see cref="PowerMod"/> of it).</param>
    /// <param name="manaConversion">The caster's Mana Conversion skill as ACE computes it (<c>Math.Round(Current * weapon modifier)</c>); 0 for an untrained caster, who pays the base.</param>
    /// <param name="next">The random source.</param>
    /// <param name="ignoresManaConversion">The spell is flagged <c>IgnoresManaConversion</c>: it pays the base.</param>
    public static uint RollCost(uint baseMana, uint power, uint manaConversion, Func<double> next, bool ignoresManaConversion = false)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (ignoresManaConversion || manaConversion == 0)
        {
            return baseMana;
        }

        var difficulty = PowerMod(power);
        var cost = baseMana;
        var successChance = SkillChance(manaConversion, difficulty / 2);
        var roll = next();
        var luck = next();
        if (roll < successChance)
        {
            cost = (uint)Math.Round(cost * (1.0f - (successChance - (roll * luck))));
        }

        if (cost > 1)
        {
            successChance = SkillChance(manaConversion, difficulty);
            roll = next();
            if (roll < successChance)
            {
                cost = (uint)Math.Round(cost * (1.0f - (successChance - (roll * luck))));
            }
        }

        return Math.Max(cost, 1);
    }

    /// <summary>
    /// The exact chance that a press lands with <paramref name="manaHeld"/> mana: P(rolled cost &lt;= mana held), the two rolls and the luck integrated analytically (class remarks). 1 at or over the base, and
    /// for a cost of at most 1 once any mana is held; 0 with no mana (the cost is at least 1). Untrained or ignoring Mana Conversion: 1 at or over the base, else 0.
    /// </summary>
    /// <param name="baseMana">The base mana.</param>
    /// <param name="power">The spell's power.</param>
    /// <param name="manaConversion">The caster's Mana Conversion skill (0: untrained).</param>
    /// <param name="manaHeld">The mana the caster holds.</param>
    /// <param name="ignoresManaConversion">The spell ignores Mana Conversion.</param>
    public static double LandingChance(uint baseMana, uint power, uint manaConversion, uint manaHeld, bool ignoresManaConversion = false)
    {
        if (ignoresManaConversion || manaConversion == 0)
        {
            return manaHeld >= baseMana ? 1.0 : 0.0; // the base
        }

        if (manaHeld == 0)
        {
            return 0.0; // the cost is at least 1
        }

        if (manaHeld >= baseMana || baseMana <= 1)
        {
            return 1.0; // the cost never exceeds the base, and a base of 1 or less costs 1
        }

        var difficulty = PowerMod(power);
        var s1 = SkillChance(manaConversion, difficulty / 2);
        var s2 = SkillChance(manaConversion, difficulty);
        double b = baseMana;
        double m = manaHeld;

        // The first roll misses (probability 1 - s1): the cost stays at the base, and the luck still reaches the second stage.
        var total = (1.0 - s1) * IntegralOfSecondStage(b, m, s2);

        // The first roll hits: the cost becomes k for the rolls whose lowered value rounds to k; the interval of first rolls, for a luck l, is [lo / l, hi / l) clipped to [0, s1).
        for (uint k = 0; k <= baseMana; k++)
        {
            var hi = ((k + 0.5) / b) - 1.0 + s1;
            if (hi <= 0.0)
            {
                continue; // no first roll rounds this low
            }

            var lo = Math.Max(0.0, ((k - 0.5) / b) - 1.0 + s1);
            if (lo >= s1)
            {
                continue; // beyond the interval of first rolls
            }

            if (k <= manaHeld)
            {
                total += IntegrateOverLuck(s1, lo, hi, constantSecondStage: true, 1.0, 0.0);
                continue;
            }

            var t = ((m + 0.5) / k) - 1.0 + s2;
            if (t <= 0.0 || s2 <= 0.0)
            {
                continue; // from this cost the second stage cannot reach the mana held
            }

            total += IntegrateOverLuck(s1, lo, hi, constantSecondStage: false, s2, t);
        }

        return Math.Min(1.0, Math.Max(0.0, total));
    }

    /// <summary>The mean of the rolled cost: the sum over m of P(cost &gt; m), from <see cref="LandingChance"/>. The base for an untrained caster.</summary>
    /// <param name="baseMana">The base mana.</param>
    /// <param name="power">The spell's power.</param>
    /// <param name="manaConversion">The caster's Mana Conversion skill (0: untrained).</param>
    /// <param name="ignoresManaConversion">The spell ignores Mana Conversion.</param>
    public static double ExpectedCost(uint baseMana, uint power, uint manaConversion, bool ignoresManaConversion = false)
    {
        if (ignoresManaConversion || manaConversion == 0)
        {
            return baseMana;
        }

        if (baseMana <= 1)
        {
            return 1.0;
        }

        var mean = 0.0;
        for (uint held = 0; held < baseMana; held++)
        {
            mean += 1.0 - LandingChance(baseMana, power, manaConversion, held);
        }

        return mean;
    }

    /// <summary>
    /// The try floor (D15): the least mana held at which a press has at least <paramref name="chance"/> of landing, and never under <paramref name="minimum"/>. Binary search over the exact
    /// <see cref="LandingChance"/> (nondecreasing in the mana held). For an untrained caster, or a spell that ignores Mana Conversion, it is the base. It is over the base only when the base is under the minimum;
    /// the caller casts outright at or over the base.
    /// </summary>
    /// <param name="baseMana">The base mana.</param>
    /// <param name="power">The spell's power.</param>
    /// <param name="manaConversion">The caster's Mana Conversion skill (0: untrained).</param>
    /// <param name="chance">The landing chance a try must have (default 0.25).</param>
    /// <param name="minimum">The least mana a try is sent with (default 5, a fizzle's cost).</param>
    /// <param name="ignoresManaConversion">The spell ignores Mana Conversion.</param>
    public static uint TryFloor(uint baseMana, uint power, uint manaConversion, double chance = DefaultTryChance, uint minimum = DefaultTryMinimum, bool ignoresManaConversion = false)
    {
        uint low = 0;
        var high = baseMana; // the chance at the base is 1
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (LandingChance(baseMana, power, manaConversion, mid, ignoresManaConversion) >= chance)
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        return Math.Max(low, minimum);
    }

    /// <summary>
    /// The integral over the luck l in [0, 1] of the second stage's chance to land when the first stage left the cost at <paramref name="k"/> (= the base here), a cost over the mana held:
    /// min(s2, t / l) for t = (m + 0.5) / k - 1 + s2, or 0 when t is not positive.
    /// </summary>
    private static double IntegralOfSecondStage(double k, double m, double s2)
    {
        var t = ((m + 0.5) / k) - 1.0 + s2;
        if (t <= 0.0 || s2 <= 0.0)
        {
            return 0.0;
        }

        // min(s2, t / l) is s2 up to l = t / s2, then t / l.
        var turn = t / s2;
        return turn >= 1.0 ? s2 : t + (t * Math.Log(s2 / t));
    }

    /// <summary>
    /// The integral over the luck l in (0, 1] of (U(l) - V(l)) * Q(l), where U = min(s1, hi / l) and V = min(s1, lo / l) are the first rolls below the interval's top and bottom edge, and Q is the second stage's
    /// chance to land: the constant 1 when <paramref name="constantSecondStage"/>, else min(s2, t / l). Each factor is a constant or a constant over l on each side of its turning point, so on every luck interval
    /// between the turning points the product integrates in closed form.
    /// </summary>
    private static double IntegrateOverLuck(double s1, double lo, double hi, bool constantSecondStage, double s2, double t)
    {
        // The turning points inside (0, 1), sorted by insertion: l = hi / s1 (U leaves s1), lo / s1 (V leaves s1), t / s2 (Q leaves s2).
        Span<double> points = stackalloc double[5];
        var count = 0;
        points[count++] = 0.0;
        var uTurn = hi / s1;
        var vTurn = lo / s1;
        var qTurn = constantSecondStage ? double.PositiveInfinity : t / s2;
        Add(points, ref count, uTurn);
        Add(points, ref count, vTurn);
        Add(points, ref count, qTurn);
        points[count++] = 1.0;

        var sum = 0.0;
        for (var i = 0; i + 1 < count; i++)
        {
            double a = points[i];
            double b = points[i + 1];
            if (b <= a)
            {
                continue;
            }

            var mid = 0.5 * (a + b);
            var uConstant = mid <= uTurn;
            var vConstant = mid <= vTurn;
            var qConstant = constantSecondStage || mid <= qTurn;
            var qValue = constantSecondStage ? 1.0 : (qConstant ? s2 : t);
            sum += Product(uConstant, uConstant ? s1 : hi, qConstant, qValue, a, b);
            if (lo > 0.0)
            {
                sum -= Product(vConstant, vConstant ? s1 : lo, qConstant, qValue, a, b);
            }
        }

        return sum;
    }

    private static void Add(Span<double> points, ref int count, double turn)
    {
        if (!(turn > 0.0 && turn < 1.0))
        {
            return;
        }

        var at = count;
        while (at > 1 && points[at - 1] > turn)
        {
            points[at] = points[at - 1];
            at--;
        }

        points[at] = turn;
        count++;
    }

    /// <summary>The integral over [a, b] of x * q where x is the constant <paramref name="x"/> or <c>x / l</c> and q the constant <paramref name="q"/> or <c>q / l</c>.</summary>
    private static double Product(bool xConstant, double x, bool qConstant, double q, double a, double b)
    {
        if (xConstant && qConstant)
        {
            return x * q * (b - a);
        }

        if (xConstant != qConstant)
        {
            return x * q * Math.Log(b / a); // one factor is a constant, the other a constant over l (a is above 0 here: the first interval has only constants)
        }

        return x * q * ((1.0 / a) - (1.0 / b)); // both over l
    }
}

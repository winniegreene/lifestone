using DatReaderWriter.Enums;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Casting;

/// <summary>
/// ACE's Mana Conversion model (M5 item 1, lane K2): the rolled cost, the exact landing chance, the mean and the try floor (D15). The numbers are m5-plan.md section 8 item 0's: the cost of a cast is two skill checks against
/// PowerMod / 2 and PowerMod (<c>Creature_Magic.cs</c> <c>GetManaCost</c> 58-100 @95ef426d), and a cast whose rolled cost exceeds the mana held is refused with nothing charged (<c>Player_Magic.cs</c> 566-585).
/// </summary>
public sealed class ManaConversionModelTests(ITestOutputHelper output)
{
    // Meditative Trance (Stamina to Mana Self VII): base 70, power 300; Stamina to Mana Self VI: base 60, power 250; Flame Bolt VII: base 35, power 300; VI: base 30, power 250.
    private const uint TranceBase = 70, TrancePower = 300, StamToManaBase = 60, StamToManaPower = 250;
    private const uint FlameVIIBase = 35, FlameVIIPower = 300, FlameVIBase = 30, FlameVIPower = 250;

    /// <summary>The table the lead recomputed for item 0 (lanes.md), to three places: the model must give it to within 0.005 (it gives it to within the table's own rounding).</summary>
    [Theory]
    [InlineData(TranceBase, TrancePower, 311u, 1u, 0.121)]
    [InlineData(TranceBase, TrancePower, 311u, 5u, 0.348)]
    [InlineData(TranceBase, TrancePower, 311u, 10u, 0.527)]
    [InlineData(TranceBase, TrancePower, 311u, 15u, 0.653)]
    [InlineData(TranceBase, TrancePower, 311u, 20u, 0.746)]
    [InlineData(TranceBase, TrancePower, 311u, 35u, 0.909)]
    [InlineData(TranceBase, TrancePower, 258u, 1u, 0.000)]
    [InlineData(TranceBase, TrancePower, 258u, 5u, 0.181)]
    [InlineData(TranceBase, TrancePower, 258u, 10u, 0.368)]
    [InlineData(TranceBase, TrancePower, 258u, 15u, 0.503)]
    [InlineData(TranceBase, TrancePower, 258u, 20u, 0.609)]
    [InlineData(TranceBase, TrancePower, 258u, 35u, 0.818)]
    [InlineData(StamToManaBase, StamToManaPower, 311u, 1u, 0.284)]
    [InlineData(StamToManaBase, StamToManaPower, 311u, 5u, 0.578)]
    [InlineData(StamToManaBase, StamToManaPower, 311u, 10u, 0.749)]
    [InlineData(StamToManaBase, StamToManaPower, 311u, 15u, 0.841)]
    public void The_landing_chance_reproduces_item_0s_table(uint baseMana, uint power, uint skill, uint held, double expected)
    {
        var chance = ManaConversionModel.LandingChance(baseMana, power, skill, held);
        output.WriteLine($"base {baseMana} power {power} skill {skill} mana {held}: {chance:F4} (table {expected:F3})");
        Assert.InRange(chance, expected - 0.005, expected + 0.005);
        Assert.InRange(chance, expected - 0.0007, expected + 0.0007); // the table is rounded to three places: the exact model agrees with it to its rounding
    }

    /// <summary>
    /// The means of m5-plan.md section 8 item 0 (14.16, 20.63, 7.84 at two places; 7.1 and 4.0 "about" for the Flame Bolts), within 0.06, and the exact values the model gives (<c>exact</c>), pinned to 0.005: the model's
    /// 14.187 and 20.664 differ from the table's two-place means by under 0.035, the Flame Bolts' 7.149 and 4.054 round to 7.1 and 4.1.
    /// </summary>
    [Theory]
    [InlineData(TranceBase, TrancePower, 311u, 14.16, 14.187)]
    [InlineData(TranceBase, TrancePower, 258u, 20.63, 20.664)]
    [InlineData(StamToManaBase, StamToManaPower, 311u, 7.84, 7.835)]
    [InlineData(FlameVIIBase, FlameVIIPower, 311u, 7.1, 7.149)]
    [InlineData(FlameVIBase, FlameVIPower, 311u, 4.0, 4.054)]
    public void The_expected_cost_is_the_means_of_item_0(uint baseMana, uint power, uint skill, double expected, double exact)
    {
        var mean = ManaConversionModel.ExpectedCost(baseMana, power, skill);
        output.WriteLine($"base {baseMana} power {power} skill {skill}: mean cost {mean:F3} (item 0: {expected})");
        Assert.InRange(mean, expected - 0.06, expected + 0.06);
        Assert.InRange(mean, exact - 0.005, exact + 0.005);
    }

    [Fact]
    public void The_mean_is_the_sum_of_the_costs_weighted_by_the_distribution()
    {
        // E[cost] = sum over k of k * P(cost = k), with P(cost = k) the difference of two landing chances: the same exact distribution read two ways.
        var mean = 0.0;
        var previous = 0.0;
        for (uint k = 1; k <= TranceBase; k++)
        {
            var upTo = ManaConversionModel.LandingChance(TranceBase, TrancePower, 311, k);
            mean += k * (upTo - previous);
            previous = upTo;
        }

        Assert.Equal(1.0, previous, 12);
        Assert.Equal(ManaConversionModel.ExpectedCost(TranceBase, TrancePower, 311), mean, 9);
    }

    [Theory]
    [InlineData(TranceBase, TrancePower, 311u)]
    [InlineData(TranceBase, TrancePower, 258u)]
    [InlineData(StamToManaBase, StamToManaPower, 311u)]
    [InlineData(FlameVIIBase, FlameVIIPower, 311u)]
    [InlineData(FlameVIBase, FlameVIPower, 158u)]
    [InlineData(80u, 200u, 220u)]
    [InlineData(2u, 25u, 50u)]
    public void The_landing_chance_is_a_distribution_function(uint baseMana, uint power, uint skill)
    {
        Assert.Equal(0.0, ManaConversionModel.LandingChance(baseMana, power, skill, 0));
        var previous = 0.0;
        for (uint held = 1; held <= baseMana; held++)
        {
            var chance = ManaConversionModel.LandingChance(baseMana, power, skill, held);
            Assert.InRange(chance, previous - 1e-12, 1.0);
            previous = chance;
        }

        Assert.Equal(1.0, previous, 12); // at the base the cost never exceeds it
        Assert.Equal(1.0, ManaConversionModel.LandingChance(baseMana, power, skill, baseMana + 50));
    }

    /// <summary>
    /// An independent check of the exact integral: the cost itself (<see cref="ManaConversionModel.RollCost"/>) evaluated on a deterministic Halton sequence of (first roll, luck, second roll), a million points. The error of
    /// such a sum on this step-function integrand is a few 1e-4; the bound asserted is 2e-3.
    /// </summary>
    [Theory]
    [InlineData(TranceBase, TrancePower, 311u)]
    [InlineData(TranceBase, TrancePower, 258u)]
    [InlineData(StamToManaBase, StamToManaPower, 311u)]
    [InlineData(FlameVIIBase, FlameVIIPower, 220u)]
    [InlineData(50u, 150u, 158u)]
    [InlineData(80u, 200u, 100u)]
    public void The_exact_landing_chance_agrees_with_a_deterministic_integration_of_the_cost(uint baseMana, uint power, uint skill)
    {
        const int points = 1 << 20;
        var counts = new int[baseMana + 1]; // counts[c]: points whose cost is c
        var draw = new HaltonDraws();
        Func<double> next = draw.Next;
        var sum = 0L;
        for (var i = 1; i <= points; i++)
        {
            draw.Start(i);
            var cost = ManaConversionModel.RollCost(baseMana, power, skill, next);
            counts[cost]++;
            sum += cost;
        }

        var worst = 0.0;
        var cumulative = 0;
        for (uint held = 0; held <= baseMana; held++)
        {
            cumulative += counts[held];
            var sampled = cumulative / (double)points;
            var exact = ManaConversionModel.LandingChance(baseMana, power, skill, held);
            worst = Math.Max(worst, Math.Abs(sampled - exact));
        }

        var meanGap = Math.Abs((sum / (double)points) - ManaConversionModel.ExpectedCost(baseMana, power, skill));
        output.WriteLine($"base {baseMana} power {power} skill {skill}: the largest gap between the exact chance and the Halton sum is {worst:E2}; the gap of the means {meanGap:E2}");
        Assert.True(worst < 2e-3, $"gap {worst}");
        Assert.True(meanGap < 5e-3, $"mean gap {meanGap}");
    }

    [Theory]
    [InlineData(TranceBase, TrancePower, 311u, 7)]
    [InlineData(TranceBase, TrancePower, 258u, 8)]
    [InlineData(StamToManaBase, StamToManaPower, 311u, 9)]
    [InlineData(FlameVIIBase, FlameVIIPower, 311u, 10)]
    public void A_seeded_sample_of_200000_rolled_costs_matches_the_landing_chance(uint baseMana, uint power, uint skill, int seed)
    {
        const int draws = 200_000;
        var random = new Random(seed);
        Func<double> next = () => random.NextSingle(); // the scripted caster's source (floats), the stand-in's too
        var counts = new int[baseMana + 1];
        for (var i = 0; i < draws; i++)
        {
            counts[ManaConversionModel.RollCost(baseMana, power, skill, next)]++;
        }

        var cumulative = 0;
        for (uint held = 0; held <= baseMana; held++)
        {
            cumulative += counts[held];
            var sampled = cumulative / (double)draws;
            var exact = ManaConversionModel.LandingChance(baseMana, power, skill, held);
            Assert.True(Math.Abs(sampled - exact) < 0.01, $"mana {held}: sampled {sampled:F4}, exact {exact:F4}");
        }
    }

    [Fact]
    public void A_roll_never_costs_under_1_or_over_the_base_and_trained_casters_save_mana()
    {
        var random = new Random(3);
        Func<double> next = () => random.NextDouble();
        var total = 0L;
        for (var i = 0; i < 10_000; i++)
        {
            var cost = ManaConversionModel.RollCost(TranceBase, TrancePower, 311, next);
            Assert.InRange(cost, 1u, TranceBase);
            total += cost;
        }

        Assert.True(total / 10_000.0 < TranceBase / 3.0, "a trained mage at 311 pays under a third of the base on average");
    }

    [Theory]
    [InlineData(0u, false)] // untrained
    [InlineData(311u, true)] // IgnoresManaConversion
    public void Untrained_and_ignoring_spells_pay_the_base_and_draw_nothing(uint skill, bool ignores)
    {
        var drawn = 0;
        Func<double> next = () => { drawn++; return 0.5; };
        Assert.Equal(TranceBase, ManaConversionModel.RollCost(TranceBase, TrancePower, skill, next, ignores));
        Assert.Equal(0, drawn);
        Assert.Equal(0.0, ManaConversionModel.LandingChance(TranceBase, TrancePower, skill, TranceBase - 1, ignores));
        Assert.Equal(1.0, ManaConversionModel.LandingChance(TranceBase, TrancePower, skill, TranceBase, ignores));
        Assert.Equal(1.0, ManaConversionModel.LandingChance(TranceBase, TrancePower, skill, TranceBase + 1, ignores));
        Assert.Equal(0.0, ManaConversionModel.LandingChance(TranceBase, TrancePower, skill, 0, ignores));
        Assert.Equal((double)TranceBase, ManaConversionModel.ExpectedCost(TranceBase, TrancePower, skill, ignores));
        Assert.Equal(TranceBase, ManaConversionModel.TryFloor(TranceBase, TrancePower, skill, 0.25, 5, ignores));
    }

    [Fact]
    public void The_draw_order_is_aces_the_roll_the_luck_then_the_second_roll_only_while_the_cost_is_over_1()
    {
        // Mana Conversion 311 against power 300: the first chance is about 0.992 (against 150), the second about 0.582 (against 300).
        var seen = new List<double>();
        var script = new Queue<double>([0.5, 0.5, 0.1]);
        Func<double> next = () => { var v = script.Dequeue(); seen.Add(v); return v; };
        var first = ManaConversionModel.SkillChance(311u, 150u);
        var second = ManaConversionModel.SkillChance(311u, 300u);
        var afterFirst = (uint)Math.Round(70 * (1.0f - (first - (0.5 * 0.5))));
        var expected = (uint)Math.Round(afterFirst * (1.0f - (second - (0.1 * 0.5))));
        Assert.Equal(expected, ManaConversionModel.RollCost(TranceBase, TrancePower, 311, next));
        Assert.Equal([0.5, 0.5, 0.1], seen);
        Assert.Equal(8u, expected);

        // A cost of 1 or less after the first stage draws no second roll: the base 2 at power 25, whose first chance is 1 and whose roll of 0 and luck of 0 lower the cost to 0.
        seen.Clear();
        script = new Queue<double>([0.0, 0.0]);
        Assert.Equal(1u, ManaConversionModel.RollCost(2, 25, 311, next));
        Assert.Equal([0.0, 0.0], seen);

        // A first roll that misses still draws the luck, and the second stage may still hit (the luck is shared).
        seen.Clear();
        var missChance = ManaConversionModel.SkillChance(100u, 150u); // PowerMod 300 / 2
        script = new Queue<double>([0.999999, 0.4, 0.0]);
        Assert.True(missChance < 0.999999);
        var cost = ManaConversionModel.RollCost(TranceBase, TrancePower, 100, next);
        Assert.Equal([0.999999, 0.4, 0.0], seen);
        var secondChance = ManaConversionModel.SkillChance(100u, 300u);
        Assert.Equal((uint)Math.Round(70 * (1.0f - (secondChance - (0.0 * 0.4)))), cost);
    }

    /// <summary>
    /// A skill so high that both chances are exactly 1, so the lowered cost is the cost times roll times luck; with roll = luck = 0.5 that is the base / 4, and a second roll of 1.0 misses. A half rounds to the even
    /// neighbour (<c>Math.Round(double)</c>), not away from zero: 2.5 is 2, 3.5 is 4, 4.5 is 4, 5.5 is 6.
    /// </summary>
    [Theory]
    [InlineData(10u, 2u)]
    [InlineData(14u, 4u)]
    [InlineData(18u, 4u)]
    [InlineData(22u, 6u)]
    public void The_cost_rounds_to_even_as_Math_Round_does(uint baseMana, uint expected)
    {
        Assert.Equal(1.0, ManaConversionModel.SkillChance(100_000u, 150u));
        Assert.Equal(1.0, ManaConversionModel.SkillChance(100_000u, 300u));
        var script = new Queue<double>([0.5, 0.5, 1.0]);
        Assert.Equal(expected, ManaConversionModel.RollCost(baseMana, 300, 100_000, () => script.Dequeue()));
        Assert.Empty(script);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(311, 150)]
    [InlineData(311, 300)]
    [InlineData(258, 300)]
    [InlineData(50, 400)]
    [InlineData(0, 25)]
    public void The_skill_chance_is_aces_formula(int skill, int difficulty)
    {
        // SkillCheck.cs 9-14: 1 - 1 / (1 + e^(factor * (skill - difficulty))), the factor a float 0.03 (the product is a float), clamped.
        var expected = Math.Min(1.0, Math.Max(0.0, 1.0 - (1.0 / (1.0 + Math.Exp(0.03f * (skill - difficulty))))));
        Assert.Equal(expected, ManaConversionModel.SkillChance(skill, difficulty));
        Assert.Equal(expected, ManaConversionModel.SkillChance((uint)skill, (uint)difficulty));
        Assert.Equal(1.0 - ManaConversionModel.SkillChance(difficulty, skill), ManaConversionModel.SkillChance(skill, difficulty), 12);
    }

    [Fact]
    public void The_skill_chance_has_its_known_points()
    {
        Assert.Equal(0.5, ManaConversionModel.SkillChance(200u, 200u), 15);
        Assert.Equal(0.789, ManaConversionModel.SkillChance(244u, 200u), 3); // 1 / (1 + e^-1.32)
        Assert.Equal(1.0, ManaConversionModel.SkillChance(100_000u, 200u));
        Assert.Equal(0.0, ManaConversionModel.SkillChance(0u, 100_000u));
        Assert.Equal(1.0 / (1.0 + Math.Exp(-0.07f * 10)), ManaConversionModel.SkillChance(210, 200, 0.07f), 12); // the fizzle pre-check's factor
        Assert.Equal(25u, ManaConversionModel.PowerMod(1));
        Assert.Equal(300u, ManaConversionModel.PowerMod(300));
    }

    /// <summary>
    /// D15's floor at the 25% a try: the least mana whose landing chance reaches a quarter (<c>raw</c>, with no minimum) and the floor the keeper uses (never under 5). At Mana Conversion 311 the rolls lower these costs so far that
    /// five mana, a fizzle's cost, is already past a quarter; at 258 the Meditative Trance needs seven.
    /// </summary>
    [Theory]
    [InlineData(FlameVIIBase, FlameVIIPower, 311u, 2u, 5u)]
    [InlineData(FlameVIBase, FlameVIPower, 311u, 1u, 5u)]
    [InlineData(TranceBase, TrancePower, 311u, 3u, 5u)]
    [InlineData(TranceBase, TrancePower, 258u, 7u, 7u)]
    public void The_try_floor_is_the_least_mana_at_which_a_press_lands_a_quarter_of_the_time(uint baseMana, uint power, uint skill, uint raw, uint floor)
    {
        var found = ManaConversionModel.TryFloor(baseMana, power, skill);
        var least = ManaConversionModel.TryFloor(baseMana, power, skill, 0.25, 0);
        output.WriteLine($"base {baseMana} power {power} skill {skill}: floor {found}, with no minimum {least}; chance at {least} {ManaConversionModel.LandingChance(baseMana, power, skill, least):F3}, one under {(least == 0 ? double.NaN : ManaConversionModel.LandingChance(baseMana, power, skill, least - 1)):F3}");
        Assert.Equal(raw, least);
        Assert.Equal(floor, found);
        Assert.True(found >= ManaConversionModel.DefaultTryMinimum, "never under 5");
        Assert.True(ManaConversionModel.LandingChance(baseMana, power, skill, found) >= 0.25);
        Assert.True(ManaConversionModel.LandingChance(baseMana, power, skill, least) >= 0.25);
        if (least > 0)
        {
            Assert.True(ManaConversionModel.LandingChance(baseMana, power, skill, least - 1) < 0.25, "the least such mana");
        }
    }

    [Fact]
    public void The_try_floor_follows_the_chance_and_the_minimum_asked_for()
    {
        var quarter = ManaConversionModel.TryFloor(TranceBase, TrancePower, 311, 0.25);
        var half = ManaConversionModel.TryFloor(TranceBase, TrancePower, 311, 0.5);
        var most = ManaConversionModel.TryFloor(TranceBase, TrancePower, 311, 0.9);
        Assert.True(quarter < half && half < most);
        Assert.True(ManaConversionModel.LandingChance(TranceBase, TrancePower, 311, half) >= 0.5);
        Assert.True(ManaConversionModel.LandingChance(TranceBase, TrancePower, 311, half - 1) < 0.5);
        Assert.Equal(1u, ManaConversionModel.TryFloor(TranceBase, TrancePower, 311, 0.0, 1)); // a chance of 0 is met at once: the minimum
        Assert.Equal(20u, ManaConversionModel.TryFloor(TranceBase, TrancePower, 311, 0.25, 20)); // the minimum lifts the floor
        Assert.Equal(TranceBase, ManaConversionModel.TryFloor(TranceBase, TrancePower, 311, 1.0)); // a sure thing is the base
        Assert.Equal(3u, ManaConversionModel.TryFloor(TranceBase, TrancePower, 311, 0.25, 0)); // with no minimum the floor is the model's own
    }

    [Fact]
    public void Small_bases_are_handled_as_aces_formula_gives_them()
    {
        // ACE: a trained caster's cost is at least 1, even for a base of 0 (Math.Max(manaCost, 1)); a base of 1 or 0 costs 1.
        Assert.Equal(1u, ManaConversionModel.RollCost(1, 100, 311, () => 0.0));
        Assert.Equal(1u, ManaConversionModel.RollCost(0, 100, 311, () => 0.0));
        Assert.Equal(0.0, ManaConversionModel.LandingChance(1, 100, 311, 0));
        Assert.Equal(1.0, ManaConversionModel.LandingChance(1, 100, 311, 1));
        Assert.Equal(1.0, ManaConversionModel.ExpectedCost(1, 100, 311));
        Assert.Equal(1.0, ManaConversionModel.LandingChance(0, 100, 311, 1));
        // A base of 2 costs 1 or 2.
        var one = ManaConversionModel.LandingChance(2, 150, 80, 1);
        Assert.InRange(one, 0.0, 1.0);
        Assert.Equal(1.0 + (1.0 - one), ManaConversionModel.ExpectedCost(2, 150, 80), 12);
        Assert.Equal(30u, ManaConversionModel.TryFloor(30, 25, 0, 0.25, 7)); // untrained: the base, over the minimum 7
        Assert.Equal(7u, ManaConversionModel.TryFloor(3, 25, 0, 0.25, 7)); // a base under the minimum: the minimum
    }

    /// <summary>The three draws of one point of a Halton sequence in bases 2, 3 and 5, handed out in ACE's order.</summary>
    private sealed class HaltonDraws
    {
        private double _a, _b, _c;
        private int _at;

        public void Start(int index)
        {
            _a = Radical(index, 2);
            _b = Radical(index, 3);
            _c = Radical(index, 5);
            _at = 0;
        }

        public double Next() => _at++ switch { 0 => _a, 1 => _b, _ => _c };

        private static double Radical(int index, int radix)
        {
            var result = 0.0;
            var fraction = 1.0 / radix;
            for (var i = index; i > 0; i /= radix)
            {
                result += (i % radix) * fraction;
                fraction /= radix;
            }

            return result;
        }
    }
}

/// <summary>The model's hot path allocates nothing (the keeper asks per decision).</summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class ManaConversionModelAllocationTests
{
    [Fact]
    public void The_landing_chance_the_floor_and_a_roll_allocate_nothing()
    {
        var random = new Random(1);
        Func<double> next = random.NextDouble;
        var sink = 0.0;
        AllocationAssert.Nothing(() =>
        {
            for (uint held = 1; held < 40; held++)
            {
                sink += ManaConversionModel.LandingChance(70, 300, 311, held);
            }

            sink += ManaConversionModel.TryFloor(70, 300, 258);
            sink += ManaConversionModel.ExpectedCost(35, 300, 311);
            sink += ManaConversionModel.RollCost(70, 300, 311, next);
        });
        Assert.True(sink > 0);
    }

    /// <summary>
    /// M6 16 Eperf: the mean of the rolled cost is kept once worked out (each decision of the fight asks it for every candidate; one call is O(base mana squared)). The number is the sum's own, bit for bit, the first time
    /// and every time after, for every spell and skill, past the kept means' bound too; it allocates nothing, asked again or worked out for the first time.
    /// </summary>
    [Fact]
    public void The_expected_cost_is_the_sums_own_number_every_time_it_is_asked_and_allocates_nothing_kept_or_worked_out()
    {
        static double Sum(uint baseMana, uint power, uint skill)
        {
            var mean = 0.0;
            for (uint held = 0; held < baseMana; held++)
            {
                mean += 1.0 - ManaConversionModel.LandingChance(baseMana, power, skill, held);
            }

            return mean;
        }

        (uint Base, uint Power)[] spells = [(2, 1), (10, 25), (20, 50), (35, 300), (70, 300), (100, 400), (150, 250)];
        foreach (var (baseMana, power) in spells)
        {
            for (uint skill = 1; skill <= 421; skill += 60)
            {
                var sum = Sum(baseMana, power, skill);
                Assert.Equal(BitConverter.DoubleToInt64Bits(sum), BitConverter.DoubleToInt64Bits(ManaConversionModel.ExpectedCost(baseMana, power, skill))); // worked out, or kept by an earlier test
                Assert.Equal(BitConverter.DoubleToInt64Bits(sum), BitConverter.DoubleToInt64Bits(ManaConversionModel.ExpectedCost(baseMana, power, skill))); // kept
            }
        }

        // past the bound a mean takes another's place, and the one replaced is worked out again: the same numbers
        for (uint skill = 1; skill <= (2 * ManaConversionModel.ExpectedCostsKept) + 8; skill++)
        {
            ManaConversionModel.ExpectedCost(3, 25, skill);
        }

        Assert.Equal(BitConverter.DoubleToInt64Bits(Sum(70, 300, 311)), BitConverter.DoubleToInt64Bits(ManaConversionModel.ExpectedCost(70, 300, 311)));
        Assert.Equal(BitConverter.DoubleToInt64Bits(Sum(3, 25, 7)), BitConverter.DoubleToInt64Bits(ManaConversionModel.ExpectedCost(3, 25, 7)));

        // numbers too large for a key are worked out each time, and what does not roll is the base
        Assert.Equal(BitConverter.DoubleToInt64Bits(Sum(4, 1u << 22, 300)), BitConverter.DoubleToInt64Bits(ManaConversionModel.ExpectedCost(4, 1u << 22, 300)));
        Assert.Equal(70.0, ManaConversionModel.ExpectedCost(70, 300, 0));
        Assert.Equal(70.0, ManaConversionModel.ExpectedCost(70, 300, 311, ignoresManaConversion: true));

        var sink = ManaConversionModel.ExpectedCost(100, 400, 258);
        var fresh = 5000u;
        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                sink += ManaConversionModel.ExpectedCost(100, 400, 258); // kept
                sink += ManaConversionModel.ExpectedCost(4, 25, fresh++); // never asked before: worked out and kept, with no allocation
            }
        });
        Assert.True(sink > 0);
    }
}

/// <summary>
/// The stand-in's cast costs mana (M5 item 1, lane K2; <c>TODO(m3-standin-cast-mana)</c> closed): the rolled cost (<see cref="ManaConversionModel.RollCost"/>) is charged with the effect, a cost over the mana held is
/// answered UseDone 0x0401 at once with nothing charged (<c>Player_Magic.cs</c> 566-585 @95ef426d), a fizzle keeps its 5. The stand-in's Mana Conversion is its own skill 16 when the description holds it trained, else
/// untrained (the base). No planner: the effect comes 0.3 s after the cast is sent and UseDone a second later.
/// </summary>
public sealed class StandInCastManaTests
{
    private const uint Cheap = 30, Dear = 31, Hopeless = 32, Fizzler = 33, Ignoring = 34;

    private static SpellInfo Spell(uint id, uint baseMana, uint power = 1, SpellFlags extra = SpellFlags.None) =>
        WorldActionRig.Spell(id, SpellType.Enchantment, SpellFlags.SelfTargeted | SpellFlags.Beneficial | extra) with
        {
            Name = "Mana Test " + id,
            Category = 40 + id,
            Power = power,
            BaseMana = baseMana,
            Duration = 1800.0,
        };

    private static readonly SpellInfo[] Spells =
    [
        Spell(Cheap, 30),
        Spell(Dear, 70, 300),
        Spell(Hopeless, 5000),
        Spell(Fizzler, 30, 400), // power over the stand-in's fixed skill plus 50: it fizzles
        Spell(Ignoring, 30, 300, SpellFlags.IgnoresManaConversion),
    ];

    private static CastRig Ready(StandInPlayerStats stats)
    {
        var rig = new CastRig(new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0, Stats = stats }, Spells);
        rig.Learn([.. Spells.Select(s => s.Id)]);
        rig.Mode(CombatMode.Magic);
        rig.Begin();
        return rig;
    }

    /// <summary>A mage-ish description: Self 1000 (so 1000 mana) and Mana Conversion 16 with <paramref name="ranks"/> ranks in the class <paramref name="advancementClass"/>: the skill is (70 + 1000) / 6 = 178 plus the ranks.</summary>
    private static StandInPlayerStats Caster(uint advancementClass, ushort ranks) => new()
    {
        Self = 1000,
        Mana = 1000,
        Skills = [new SkillDesc(StandInWorld.ManaConversionSkillId, ranks, 0, advancementClass, 0, 0, 0, 0.0)],
    };

    private static uint Mana(CastRig rig)
    {
        Assert.True(rig.Q.TryGetVitalLevels(5, out var mana, out _));
        return mana;
    }

    private static ActionHandle CastAndWait(CastRig rig, uint spell, double seconds = 1.6)
    {
        var cast = rig.Session.Actions.Cast(spell, ActionSource.Host);
        rig.Run(seconds);
        return cast;
    }

    [Fact]
    public void An_untrained_stand_in_pays_the_base_and_the_update_comes_with_the_effect()
    {
        using var rig = Ready(new StandInPlayerStats()); // mana 75 of 100, no skill 16
        var cast = CastAndWait(rig, Cheap);

        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
        Assert.Equal(75u - 30u, Mana(rig));
        Assert.Equal(["02E9", "02C2", "01C7"], rig.Tags);
        Assert.Equal((30u, 0), (rig.World.ManaCharged, rig.World.ManaRefusals));
        Assert.Equal(StandInWorld.CastRecoilSeconds, rig.First("01C7") - rig.First("02E9"), 0.03);
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("m3-standin-cast-mana", StringComparison.Ordinal));
    }

    [Fact]
    public void A_cost_over_the_mana_held_is_answered_0401_at_once_and_charges_nothing_and_every_press_is_refused_again()
    {
        using var rig = Ready(new StandInPlayerStats { Mana = 20 });
        var refused = CastAndWait(rig, Cheap, 0.5); // the base is 30: the answer comes with the stand-in's own delay, not after a cast and its recoil

        Assert.True(refused.IsCompleted);
        Assert.Equal((ActionOutcomeKind.ServerError, 0x0401u), (refused.Outcome!.Value.Kind, refused.Outcome.Value.Code));
        Assert.Equal(["01C7"], rig.Tags); // no words, no motion, no mana update, no effect
        Assert.Equal(20u, Mana(rig));
        Assert.Equal((0u, 1), (rig.World.ManaCharged, rig.World.ManaRefusals));

        rig.Begin();
        var again = CastAndWait(rig, Ignoring, 0.5); // ignoring Mana Conversion, base 30: still over 20, and certain, so the client's before-check refuses it (M5 7 D141)
        Assert.Equal((ActionOutcomeKind.Refused, ActionReason.NotEnoughMana), (again.Outcome!.Value.Kind, again.Outcome.Value.Reason));
        Assert.Equal(1, rig.World.ManaRefusals); // nothing reached the stand-in
        Assert.Equal(20u, Mana(rig));
    }

    [Fact]
    public void A_fizzle_costs_5_and_a_player_under_5_is_refused()
    {
        using var rig = Ready(new StandInPlayerStats()); // mana 75
        var fizzle = CastAndWait(rig, Fizzler);

        Assert.Equal((ActionOutcomeKind.ServerError, StandInWorld.FizzleError), (fizzle.Outcome!.Value.Kind, fizzle.Outcome.Value.Code));
        Assert.Equal(75u - ManaConversionModel.FizzleMana, Mana(rig)); // the 5, not the base 30
        Assert.Equal(["02E9", "028A", "01C7"], rig.Tags);

        using var poor = Ready(new StandInPlayerStats { Mana = 4 });
        var refused = CastAndWait(poor, Fizzler, 0.5);
        Assert.Equal(0x0401u, refused.Outcome!.Value.Code);
        Assert.Equal(4u, Mana(poor));
        Assert.Equal(["01C7"], poor.Tags);
    }

    [Fact]
    public void A_cost_that_cannot_be_paid_is_refused_whatever_the_skill()
    {
        using var rig = Ready(Caster(SkillRules.SpecializedClass, 400));
        var cast = CastAndWait(rig, Hopeless, 0.5);
        Assert.Equal(0x0401u, cast.Outcome!.Value.Code);
        Assert.Equal(1000u, Mana(rig));
    }

    [Fact]
    public void A_trained_stand_in_pays_what_Mana_Conversion_rolls_the_models_mean_not_the_base()
    {
        // Skill 16 trained with 133 ranks over (70 + 1000) / 6 = 178: skill 311, the lead's mage. A base 70, power 300 spell costs a mean of 14.19 (the model), not 70.
        const int casts = 40;
        using var rig = Ready(Caster(SkillRules.TrainedClass, 133));
        var costs = new List<uint>();
        for (var i = 0; i < casts; i++)
        {
            var before = Mana(rig);
            var cast = CastAndWait(rig, Dear);
            Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
            costs.Add(before - Mana(rig));
        }

        var mean = costs.Average(c => (double)c);
        var expected = ManaConversionModel.ExpectedCost(70, 300, 311);
        var total = (uint)costs.Sum(c => c);
        Assert.Equal(1000u - total, Mana(rig));
        Assert.Equal(total, rig.World.ManaCharged);
        Assert.All(costs, c => Assert.InRange(c, 1u, 70u));
        Assert.True(costs.Distinct().Count() > 5, "the cost is rolled, not fixed: " + string.Join(",", costs));
        Assert.True(Math.Abs(mean - expected) < 5.0, $"mean {mean:F2} against the model's {expected:F2}");
        Assert.Equal(0, rig.World.ManaRefusals);
    }

    [Fact]
    public void An_untrained_skill_in_the_description_and_a_spell_that_ignores_Mana_Conversion_pay_the_base()
    {
        using var untrained = Ready(Caster(SkillRules.UntrainedClass, 400));
        CastAndWait(untrained, Dear);
        Assert.Equal(1000u - 70u, Mana(untrained));

        using var trained = Ready(Caster(SkillRules.TrainedClass, 133));
        CastAndWait(trained, Ignoring);
        Assert.Equal(1000u - 30u, Mana(trained));
    }

    [Fact]
    public void The_same_description_gives_the_same_costs_on_every_run_the_rolls_are_seeded()
    {
        static List<uint> Costs()
        {
            using var rig = Ready(Caster(SkillRules.TrainedClass, 133));
            var costs = new List<uint>();
            for (var i = 0; i < 6; i++)
            {
                var before = Mana(rig);
                CastAndWait(rig, Dear);
                costs.Add(before - Mana(rig));
            }

            return costs;
        }

        Assert.Equal(Costs(), Costs());
    }
}

using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 stamina dance (<c>docs/session.md</c> section 121; the owner: she used Revitalize and Stamina to Mana inefficiently): the dance's whole gives. What these prove, on run 9's live numbers
/// (<c>cache/m6/sprint/run9/step3-levelup.log</c>, Qwinnie at levels 53 to 58 in the Tusker Quarters):
/// <list type="bullet">
/// <item>a: the upkeep's press toward its target waits for the best-priced level's whole give (01:37:39 UTC: Stamina to Mana Self II at 363 of 375 mana for 12, toward a target whose wanted 379 was over her maximum), and goes once it fits;</item>
/// <item>b: the refill's Revitalize Self waits for the best level's whole roll (01:37:33 UTC: Revitalize Self VI at 309 of 354 stamina restored 46 of its 80 to 160), or goes when the next press needs the stamina;</item>
/// <item>c: a press's stamina is priced at what the refill pays to put it back, so from a half bar the press is the level that converts best, not the quickest; and the ladder's self table is run 9's measure.</item>
/// </list>
/// Rule d (the buff pass) is in <c>BuffCycleTests</c>.
/// </summary>
public sealed class StaminaDanceWholeGivesTests
{
    private static readonly LadderVitalsPolicy Rules = LadderLoader.WarMage.VitalsPolicy;

    // Stamina to Mana Self II to VI as her fight offered them in run 9 ("level 2 of 7" to "level 6 of 7": I not in her offer); base mana BuffStatMods.StaminaToManaBaseMana, power the SpellTable's 50 to 250 by level.
    private static readonly FillLevel[] Presses =
    [
        new(0, 2, 20, 50, Rules.SelfSeconds(2), BuffStatMods.StaminaToMana[1]),
        new(1, 3, 30, 100, Rules.SelfSeconds(3), BuffStatMods.StaminaToMana[2]),
        new(2, 4, 40, 150, Rules.SelfSeconds(4), BuffStatMods.StaminaToMana[3]),
        new(3, 5, 50, 200, Rules.SelfSeconds(5), BuffStatMods.StaminaToMana[4]),
        new(4, 6, 60, 250, Rules.SelfSeconds(6), BuffStatMods.StaminaToMana[5]),
    ];

    // Revitalize Self VI (1182): base mana 70 (BuffStatMods.RevitalizeBaseMana; the live "mana 375 of up to 70"), power 250 (the SpellTable's level VI), its row 80 to 160.
    private static readonly BoostLevel[] Revitalize = [new(0, 6, 70, 250, Rules.SelfSeconds(6), BuffStatMods.BoostOf(1182)!.Value)];

    private const uint Reserve = 79; // the way out's line in the test's line: with a ceiling of 300, the wanted 379 of the live line over her 375

    /// <summary>Run 9 at 01:37:39 UTC (log line 163493): health 197/234, stamina 353/354, mana 363/375, Mana Conversion 252, a kill 21.8 s, one attacker; the caller sets the bars and the lines.</summary>
    private static VitalsPolicyFacts Quarters(uint mana = 363, uint stamina = 353, uint drink = 0, params UpkeepLine[] lines) => new()
    {
        Health = 197,
        HealthMax = 234,
        Stamina = stamina,
        StaminaMax = 354,
        Mana = mana,
        ManaMax = 375,
        ManaConversion = 252,
        HealthRegen = 0.098,
        StaminaRegen = 0.645,
        ManaRegen = 0.43,
        SecondsPerKill = 21.8,
        AttacksPerSecond = 0.67,
        DamagePerSecond = 1.45,
        BurstPerSecond = 1.79,
        BiggestHit = 8,
        BoltLevel = 6,
        BoltBaseMana = 30,
        BoltPower = 250,
        BoltSeconds = Rules.BoltSeconds(6),
        BoltMeanDamage = 130,
        BoltSpread = 70,
        BoltLandChance = 0.95,
        KillHealth = 400,
        TargetHealth = 400,
        FullestHealth = 0,
        FullestStamina = 0, // live: "fullest health 400 stamina 0": no stamina drain's source read
        EmergencyLine = 0,
        Presses = Presses,
        Upkeep = lines,
        UpkeepReserve = Reserve,
        ManaDrinkGive = drink,
        Attackers = 1,
    };

    private static UpkeepLine Bane(double due) => new("Bludgeon Bane", due, 300, ManaConversionModel.ExpectedCost(300, 250, 252), 0);

    [Fact]
    public void A_the_upkeeps_press_toward_a_target_over_her_maximum_waits_for_its_whole_give_and_then_goes()
    {
        // Live: "press level 0 (upkeep: the press toward the target; gives 12 ...)" at 363 of 375; and at 369 for 6, at 372 for 3.
        var near = Quarters(lines: Bane(11.3));
        var plan = VitalsPolicy.Decide(in near, Rules);
        Assert.True(plan.Upkeep.Active);
        Assert.True(plan.Upkeep.Wanted > near.ManaMax, $"wanted {plan.Upkeep.Wanted}");
        Assert.Equal(VitalsMove.Bolt, plan.Choice.Move);
        Assert.False(plan.Choice.Upkeep);
        Assert.Equal(VitalsPolicy.UpkeepPressWaitsWhy, plan.Choice.Why);
        Assert.Contains(VitalsPolicy.UpkeepPressWaitsWhy, plan.Describe(in near), StringComparison.Ordinal); // the log line says why it waited

        foreach (var mana in new uint[] { 340, 369, 372 })
        {
            Assert.Equal(VitalsMove.Bolt, VitalsPolicy.Decide(Quarters(mana: mana, lines: Bane(11.3)), Rules).Choice.Move);
        }

        // Its whole give fits: the press goes, whole. Stamina to Mana Self VI from 353 drains round(176.5) = 176 and gives round(176 x 1.5) = 264 (BuffStatMods.Transfer).
        var low = Quarters(mana: 105, lines: Bane(11.3));
        var go = VitalsPolicy.Decide(in low, Rules);
        Assert.Equal(VitalsMove.Press, go.Choice.Move);
        Assert.True(go.Choice.Upkeep);
        Assert.Equal(VitalsPolicy.UpkeepPressTowardWhy, go.Choice.Why);
        Assert.Equal(4, go.Choice.Index); // VI: the best-priced level at its whole give
        Assert.Equal(264, go.Choice.Give);
        Assert.True(go.Choice.Give <= low.ManaMax - low.Mana);
    }

    [Fact]
    public void A_while_the_press_waits_no_drink_stands_in_for_it_and_with_no_press_the_drink_still_goes()
    {
        var waits = VitalsPolicy.Decide(Quarters(drink: 10, lines: Bane(11.3)), Rules); // a drink of 10 fits the 12 missing whole, the press's 264 does not
        Assert.Equal(VitalsMove.Bolt, waits.Choice.Move);
        Assert.Equal(VitalsPolicy.UpkeepPressWaitsWhy, waits.Choice.Why);

        var dry = VitalsPolicy.Decide(Quarters(stamina: 0, drink: 10, lines: Bane(11.3)), Rules); // no stamina: no press offered, the drink goes as before
        Assert.Equal(VitalsMove.DrinkMana, dry.Choice.Move);
        Assert.True(dry.Choice.Upkeep);
    }

    private static readonly RevitalizeLevel[] Refill =
    [
        new(1180, BuffStatMods.RevitalizeBaseMana[3], 150), // IV: 40 to 80
        new(1181, BuffStatMods.RevitalizeBaseMana[4], 200), // V: 60 to 120
        new(1182, BuffStatMods.RevitalizeBaseMana[5], 250), // VI: 80 to 160
    ];

    [Fact]
    public void B_the_refill_waits_for_the_best_levels_whole_roll_and_goes_when_it_fits_or_a_press_needs_the_stamina()
    {
        // Live 01:37:33 UTC: Revitalize Self VI at 309 of 354 stamina, mana 375, "restore 46": 45 missing of a roll of 80 to 160.
        Assert.Equal(-1, ManaOrder.RevitalizeLevelOf(Refill, 375, 30, 309, 354, 0, out var fit));
        Assert.Equal(RefillFit.Waits, fit);
        Assert.Equal(2, ManaOrder.RevitalizeLevelOf(Refill, 375, 30)); // the old rule casts the highest payable, VI, whatever the stamina

        Assert.Equal(2, ManaOrder.RevitalizeLevelOf(Refill, 375, 30, 354 - 160, 354, 0, out fit)); // VI's top roll fits the 160 missing: it lands whole
        Assert.Equal(RefillFit.WholeRoll, fit);
        Assert.Equal(-1, ManaOrder.RevitalizeLevelOf(Refill, 375, 30, 354 - 159, 354, 0, out fit)); // one short: it waits; IV and V, which would fit, are not taken
        Assert.Equal(RefillFit.Waits, fit);

        // D88's floor for her (the press VI's row, 60 and 70 base) sits under the whole-roll line, so on her bar the exception never decides: under the floor VI's whole roll fits anyway.
        var row = BuffStatMods.TransferOf(1681)!.Value;
        var floor = ManaOrder.StaminaFloorOf(in row, 354, 60, 70);
        Assert.True(floor < 354 - 160, $"floor {floor}");
        Assert.Equal(RefillFit.WholeRoll, ManaOrder.RevitalizeLevelOf(Refill, 375, 30, floor - 1, 354, floor, out fit) >= 0 ? fit : RefillFit.None);

        // A floor over that line (a smaller bar or a dearer press; the buff pass's rig reads 159 of 275): under it the next press needs the stamina now, whole roll or not.
        Assert.Equal(2, ManaOrder.RevitalizeLevelOf(Refill, 375, 30, 250, 354, 260, out fit));
        Assert.Equal(RefillFit.PressNeedsIt, fit);

        Assert.Equal(1, ManaOrder.RevitalizeLevelOf(Refill, 60 + 30, 30, 354 - 120, 354, 0, out fit)); // the mana pays V, not VI, with the bolt in hand: V's own whole roll
        Assert.Equal(RefillFit.WholeRoll, fit);
        Assert.Equal(-1, ManaOrder.RevitalizeLevelOf(Refill, 40, 30, 0, 354, 0, out fit));
        Assert.Equal(RefillFit.None, fit);
    }

    [Fact]
    public void B_a_bar_under_the_best_levels_top_roll_takes_the_highest_level_that_fits_it_and_a_level_with_no_row_goes_as_before()
    {
        // Winnie at level 10: stamina 154 (the Retreat's bars, docs/fight-sim.md): VI's 160 never fits, V's 120 does.
        Assert.Equal(1, ManaOrder.RevitalizeLevelOf(Refill, 375, 30, 154 - 120, 154, 0, out var fit));
        Assert.Equal(RefillFit.WholeRoll, fit);
        Assert.Equal(-1, ManaOrder.RevitalizeLevelOf(Refill, 375, 30, 154 - 119, 154, 0, out fit));
        Assert.Equal(RefillFit.Waits, fit);

        Assert.Equal(2, ManaOrder.RevitalizeLevelOf(Refill, 375, 30, 10, 30, 0, out fit)); // a bar of 30: no level fits it whole: the highest payable, as before
        Assert.Equal(RefillFit.NoneFitsTheBar, fit);

        RevitalizeLevel[] unknown = [new(99999, 70, 250)];
        Assert.Equal(0, ManaOrder.RevitalizeLevelOf(unknown, 375, 30, 300, 354, 0, out fit));
        Assert.Equal(RefillFit.NoneFitsTheBar, fit);
    }

    private static ManaOrderInput Between(uint stamina, bool wholeRoll, uint floor = 0) => new()
    {
        Mana = 375,
        ManaMax = 375,
        Stamina = stamina,
        StaminaMax = 354,
        ManaConversion = 252,
        Need = new ManaNeed(30, 10),
        Profile = ManaProfile.Mage,
        StaminaFullPercent = 90,
        StaminaFloor = floor,
        BestNetPress = true,
        RevitalizeWholeRoll = wholeRoll,
    };

    [Fact]
    public void B_the_order_asks_for_whole_rolls_only_when_its_caller_does()
    {
        DanceLevel[] presses = [new(1681, 60, 250, BuffStatMods.TransferOf(1681)!.Value)];
        var old = ManaOrder.Decide(Between(309, wholeRoll: false), presses, ManaOrderOptions.Default, Refill);
        Assert.Equal((ManaStep.Revitalize, 2), (old.Step, old.DanceIndex)); // today's order: under 90% (319 of 354), the highest payable

        var waits = ManaOrder.Decide(Between(309, wholeRoll: true), presses, ManaOrderOptions.Default, Refill);
        Assert.Equal(ManaStep.Ready, waits.Step); // the bolt: VI's 160 does not fit the 45 missing

        var whole = ManaOrder.Decide(Between(190, wholeRoll: true), presses, ManaOrderOptions.Default, Refill);
        Assert.Equal((ManaStep.Revitalize, 2), (whole.Step, whole.DanceIndex));
        Assert.Contains("whole roll fits", whole.Reason, StringComparison.Ordinal);

        var needed = ManaOrder.Decide(Between(250, wholeRoll: true, floor: 260), presses, ManaOrderOptions.Default, Refill);
        Assert.Equal(ManaStep.Revitalize, needed.Step);
        Assert.Contains("the next press needs it now", needed.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "Timing")] // m5-manaorder-alloc-flake: the zero-byte allocation count of the mana order's tests
    public void B_the_whole_roll_refill_allocates_nothing()
    {
        DanceLevel[] presses = [new(1681, 60, 250, BuffStatMods.TransferOf(1681)!.Value)];
        var input = Between(250, wholeRoll: true, floor: 260);
        ManaOrder.Decide(in input, presses, ManaOrderOptions.Default, Refill);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            ManaOrder.Decide(Between((uint)(150 + (i % 200)), wholeRoll: true, floor: 174), presses, ManaOrderOptions.Default, Refill);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    /// <summary>Run 9 at 20:38:50 UTC (log line of "press level 2 (the press's whole give fits; gives 200"): stamina 336/349, mana 133/369, Mana Conversion 248, a kill 24.1 s, no stamina source.</summary>
    private static VitalsPolicyFacts Armour(uint mana = 133, uint stamina = 336, bool refill = true) => Quarters(mana: mana, stamina: stamina) with
    {
        Health = 229,
        HealthMax = 229,
        StaminaMax = 349,
        ManaMax = 369,
        ManaConversion = 248,
        SecondsPerKill = 24.1,
        AttacksPerSecond = 0.5,
        DamagePerSecond = 1.07,
        BurstPerSecond = 2.6,
        Revitalizes = refill ? Revitalize : ReadOnlyMemory<BoostLevel>.Empty,
    };

    [Fact]
    public void C_a_press_takes_stamina_at_the_price_the_refill_pays_to_put_it_back()
    {
        var f = Armour();
        var budget = VitalsPolicy.Budget(in f, Rules);
        Assert.True(budget.StaminaShort > 0, $"short {budget.StaminaShort}"); // live: "stamina 25.1 regen 15.6 short 9.6"
        var none = VitalsPolicy.Prices(Armour(refill: false), in budget);
        Assert.Equal(0, none.Stamina); // live: "prices ... stamina 0": no source in reach and nothing else priced it

        var prices = VitalsPolicy.Prices(in f, in budget);
        var six = Revitalize[0];
        var restore = six.Row.Boost + (six.Row.Variance / 2.0); // 120: the mean of 80 to 160
        var cost = ManaConversionModel.ExpectedCost(six.BaseMana, six.Power, f.ManaConversion);
        Assert.Equal((six.Seconds + (cost * prices.Mana)) / restore, prices.Stamina, 4); // the fixed point: its seconds and its mana at the mana's price, over its restore
        Assert.True(prices.Mana > none.Mana, $"mana {prices.Mana} over {none.Mana}: the press's price carries its stamina's");
    }

    [Fact]
    public void C_from_a_half_bar_under_the_floor_the_press_is_the_level_that_converts_best_not_the_quickest()
    {
        // Stamina 200: II takes 100 (its cap) for 100 and VI takes 100 for 150 (BuffStatMods.Transfer). Priced at 0 a stamina point, II's seconds win; priced at the refill's, VI's mana a point does.
        var priced = VitalsPolicy.Decide(Armour(mana: 40, stamina: 200), Rules);
        Assert.Equal("under the mana floor", priced.Choice.Why);
        Assert.Equal(4, priced.Choice.Index);

        var free = VitalsPolicy.Decide(Armour(mana: 40, stamina: 200, refill: false), Rules);
        Assert.Equal("under the mana floor", free.Choice.Why);
        Assert.Equal(0, free.Choice.Index);
    }

    [Fact]
    public void C_with_run_9s_self_table_the_full_bar_press_is_VI_and_it_waits_for_its_whole_give()
    {
        // Live 20:38:50: Stamina to Mana Self IV went at 133 of 369 for 200 (the table's 2.94 s made it cheaper a point than VI's 3.78); with run 9's 3.40 and 3.64 VI is the best-priced, and its 252 does not fit the 236 missing.
        Assert.Equal(new[] { 1.74, 2.16, 2.6, 3.4, 3.37, 3.64, 4.07, 4.5 }, Rules.CastSecondsSelf); // the ladder's cast_seconds_self: run 9's fight means for II to VI (war-mage.json sources.vitals_policy)
        var at = VitalsPolicy.Decide(Armour(), Rules);
        Assert.Equal(VitalsMove.Bolt, at.Choice.Move);

        var low = VitalsPolicy.Decide(Armour(mana: 115), Rules); // 254 missing: VI's whole give fits
        Assert.Equal(VitalsMove.Press, low.Choice.Move);
        Assert.Equal("the press's whole give fits", low.Choice.Why);
        Assert.Equal(4, low.Choice.Index);
    }
}

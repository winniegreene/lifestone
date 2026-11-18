using Lifestone.Data.Tests;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>M6 7 AN: the mana loop, the fullness bar and the coin order (pure); the numbers are the spell rows' and the brief's.</summary>
public sealed class RechargeRulesTests
{
    private static LadderVitals V => LadderLoader.WarMage.Vitals;

    private static DebuffSpell DrainI => new(1249, "Drain Stamina Other I", DebuffKind.DrainStaminaOther, 1, DebuffSchool.LifeMagic, 10, 38, 0, 0, Proportion: 0.1, LossPercent: -1, TransferCap: 60);

    [Fact]
    public void The_lines_are_the_ladders_data_and_the_option_default_reads_them()
    {
        Assert.Equal((50, 45, 50, 30, 900d), (V.DrainUnderPercent, V.ManaLoopUnder, V.ManaLoopStaminaPercent, V.StaminaDrainMinGive, V.BuffPassWaitSeconds));
        Assert.Equal(V.DrainUnderPercent, FightOptions.DefaultDrainUnderPercent);
        Assert.Contains("deaths of 43% at 30", LadderLoader.WarMage.Sources["vitals"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(290u, 58)] // a tusker: takes 29, gives 58
    [InlineData(80u, 16)] // an outskirts creature
    [InlineData(150u, 30)] // the least that pays
    [InlineData(900u, 60)] // the cap
    public void A_drain_of_stamina_gives_twice_a_tenth_cut_at_the_cap(uint held, int give) => Assert.Equal(give, ManaLoopRule.StaminaGive(held, DrainI));

    [Theory]
    [InlineData(29u, 57u, 57u, 0, ManaLoopMove.Press)]
    [InlineData(29u, 29u, 57u, 0, ManaLoopMove.Press)] // at least half
    [InlineData(29u, 28u, 57u, 58, ManaLoopMove.DrainStamina)]
    [InlineData(29u, 28u, 57u, 16, ManaLoopMove.Press)] // the outskirts: no drain pays; a press if it nets (the caller reads the net)
    [InlineData(13u, 34u, 111u, 58, ManaLoopMove.Press)] // live, 21:15: a drain here left 3 mana and no press
    [InlineData(20u, 34u, 111u, 58, ManaLoopMove.DrainStamina)] // the drain and the press after it
    [InlineData(29u, 28u, 57u, 30, ManaLoopMove.DrainStamina)]
    [InlineData(45u, 57u, 57u, 58, ManaLoopMove.None)] // at the line the loop rests
    public void The_loop_presses_from_half_a_bar_and_drains_only_where_it_pays(uint mana, uint stamina, uint max, int give, ManaLoopMove move) =>
        Assert.Equal(move, ManaLoopRule.Decide(mana, stamina, max, V, give));

    /// <summary>M6 8 H: Stamina to Mana I and II, their rows and costs (the spell rows 1676, 1677).</summary>
    private static DanceLevel[] Press =>
    [
        new(1676, 10, 1, BuffStatMods.TransferOf(1676)!.Value),
        new(1677, 20, 50, BuffStatMods.TransferOf(1677)!.Value),
    ];

    private static ManaLoopFacts Facts(uint mana, uint manaMax, uint stamina, uint staminaMax, uint bolt, int targetStamina, bool dying = false, int attackers = 0, int owed = 0, uint room = uint.MaxValue, bool drainOnTarget = false)
    {
        var levels = Press;
        var pressNow = ManaLoopRule.PressOption(levels, stamina, mana, manaMax, 0, 0.95);
        var fill = ManaLoopRule.FillOption(levels, stamina, mana, manaMax, 0, 0.95);
        var give = targetStamina > 0 ? ManaLoopRule.StaminaGive((uint)targetStamina, DrainI, Math.Min(room, staminaMax - stamina)) : 0;
        var after = give > 0 && mana >= DrainI.BaseMana ? ManaLoopRule.PressOption(levels, stamina + (uint)give, mana - DrainI.BaseMana, manaMax, 0, 0.95) : ManaLoopPress.None;
        return new ManaLoopFacts { Mana = mana, ManaMax = manaMax, Stamina = stamina, StaminaMax = staminaMax, BoltMana = bolt, PressNow = pressNow, PressFill = fill, PressAfterDrain = after, DrainGive = give, DrainMana = DrainI.BaseMana, TargetDying = dying, Attackers = attackers, BoltsOwed = owed, DrainOnBoltTarget = drainOnTarget };
    }

    [Theory]
    // The Cave, 2026-11-12: mana 246 of 290 (49 bolts at 5), the rest's line 15% of 172 (26). This rig's caster has no Mana Conversion (a press costs 10), so the stamina is one at which the press pays: 40 (it takes 20 and
    // would leave 20, under the line): not pressed.
    [InlineData(246u, 290u, 40u, 172u, 5u, 26u, ManaLoopMove.None)]
    [InlineData(246u, 290u, 50u, 172u, 5u, 26u, ManaLoopMove.None)] // it takes 25 and would leave 25, one under the line
    [InlineData(246u, 290u, 52u, 172u, 5u, 26u, ManaLoopMove.Press)] // it takes 26 and leaves 26: at the line, pressed
    [InlineData(246u, 290u, 40u, 172u, 5u, 0u, ManaLoopMove.Press)] // a ground with no stamina line: as before
    [InlineData(45u, 290u, 40u, 172u, 5u, 26u, ManaLoopMove.Press)] // under ten bolts of mana held: the mana matters more than the rest, as before
    public void M6_14_with_ample_mana_a_press_does_not_take_her_stamina_under_the_rests_line(uint mana, uint manaMax, uint stamina, uint staminaMax, uint bolt, uint keep, ManaLoopMove move)
    {
        var facts = Facts(mana, manaMax, stamina, staminaMax, bolt, 0) with { StaminaKeep = keep };
        var plan = ManaLoopRule.DecideBySum(in facts, V);
        Assert.Equal(move, plan.Move);
        if (move == ManaLoopMove.None)
        {
            Assert.Equal("no press: the mana is ample and the press would take her stamina under the rest's line", plan.Why);
        }
    }

    [Fact]
    public void M6_14_a_press_names_the_stamina_it_takes_cut_to_the_mana_she_is_missing()
    {
        var levels = Press;
        Assert.Equal(26u, ManaLoopRule.PressOption(levels, 52, 246, 290, 0, 0.95).Taken); // Stamina to Mana I takes half
        Assert.Equal(4u, ManaLoopRule.PressOption(levels, 7, 246, 290, 0, 0.95).Taken);
        var cut = ManaLoopRule.PressOption(levels, 123, 195, 201, 0, 0.95); // 6 mana missing: the transfer is cut to it, and so is what it takes
        Assert.True(cut.Taken < 20, $"taken {cut.Taken}");
        Assert.Equal(10u, ManaLoopRule.AmpleBolts);
    }

    [Fact]
    public void A_drain_is_cut_to_the_room_she_has_M6_8_H()
    {
        Assert.Equal(58, ManaLoopRule.StaminaGive(290, DrainI, 100)); // a tusker, room to spare
        Assert.Equal(24, ManaLoopRule.StaminaGive(140, DrainI, 24)); // a Mite, her half bar at level 9
        Assert.Equal(0, ManaLoopRule.StaminaGive(290, DrainI, 0)); // a full stamina bar: the drain is wasted
    }

    [Theory]
    [InlineData(100u, 200u, 55u, 111u, 15u, 280, ManaLoopMove.DrainStamina)] // the Burrow, level 18: half a bar, a tusker gives 56, the pair nets 26 (bolt 15)
    [InlineData(100u, 201u, 61u, 123u, 15u, 290, ManaLoopMove.DrainStamina)] // the Lodge, level 22: the pair nets 30
    [InlineData(100u, 165u, 38u, 77u, 15u, 280, ManaLoopMove.DrainStamina)] // the Burrow, level 16: the pair nets 14, under a bolt (M6 8 H: none); M6 14: under the stamina line (38 of 77, line 61) the drain is valued in stamina, 39 >= 30
    [InlineData(100u, 146u, 25u, 49u, 10u, 140, ManaLoopMove.None)] // the outskirts, level 9, a Mite: the pair nets 2; the press from the half bar nets 1
    [InlineData(100u, 146u, 25u, 49u, 10u, 80, ManaLoopMove.None)] // the outskirts, a Drudge: the drain loses mana
    [InlineData(100u, 146u, 49u, 49u, 10u, 0, ManaLoopMove.Press)] // the outskirts' real lever: her own full bar nets 12
    [InlineData(100u, 131u, 37u, 37u, 10u, 0, ManaLoopMove.Press)] // level 6: a full bar nets 6, more than half a bolt
    [InlineData(100u, 200u, 111u, 111u, 15u, 280, ManaLoopMove.Press)] // a full stamina bar: no drain has room; the press
    [InlineData(15u, 200u, 55u, 111u, 15u, 280, ManaLoopMove.Press)] // the mana cannot pay the drain and the press after it (D95's floor, read from the rows): the press alone
    [InlineData(140u, 146u, 49u, 49u, 10u, 0, ManaLoopMove.None)] // 6 missing: the press would pay its 10 for 6 (the overfill counted)
    [InlineData(146u, 146u, 49u, 49u, 10u, 0, ManaLoopMove.None)] // nothing is missing
    public void The_climbers_loop_acts_by_the_sum_table_M6_8_H(uint mana, uint manaMax, uint stamina, uint staminaMax, uint bolt, int targetStamina, ManaLoopMove move) =>
        Assert.Equal(move, ManaLoopRule.DecideBySum(Facts(mana, manaMax, stamina, staminaMax, bolt, targetStamina), V).Move);

    [Theory]
    [InlineData(true, 0, 0, "the bolt first: the target dies to it")]
    [InlineData(false, 3, 0, "the bolt first: too many are hitting her")]
    [InlineData(false, 0, 1, "the bolt first: bolts between the mana casts")]
    public void The_bolt_comes_first_while_it_can_be_paid_M6_8_H(bool dying, int attackers, int owed, string why)
    {
        // M6 14: the stamina is over the stamina line (100 of 111, line 89), so these are the mana casts' gates; under it the stamina line has its own (M6_14_the_stamina_line_waits_*).
        var plan = ManaLoopRule.DecideBySum(Facts(100, 200, 100, 111, 15, 280, dying, attackers, owed), V);
        Assert.Equal(ManaLoopMove.None, plan.Move);
        Assert.Equal(why, plan.Why);
    }

    [Fact]
    public void A_bolt_she_cannot_pay_lets_the_press_through_even_at_a_dying_target_M6_8_H() =>
        Assert.Equal(ManaLoopMove.Press, ManaLoopRule.DecideBySum(Facts(12, 200, 100, 100, 15, 0, dying: true, attackers: 5, owed: 2), V).Move);

    [Fact]
    public void The_sum_picks_the_press_level_that_nets_most_with_the_overfill_counted_M6_8_H()
    {
        var levels = Press;
        var full = ManaLoopRule.PressOption(levels, 123, 100, 201, 0, 0.95);
        Assert.Equal((1, 42.0), (full.Level, full.Net)); // II: 62 less 20 (I: 45 at its cap, less 10)
        var low = ManaLoopRule.PressOption(levels, 49, 100, 146, 0, 0.95);
        Assert.Equal(0, low.Level); // I: 22 - 10 = 12, II: 24 - 20 = 4
        var cut = ManaLoopRule.PressOption(levels, 123, 195, 201, 0, 0.95);
        Assert.True(cut.Net < 0); // the give cut to the 6 missing, the cost paid whole
    }

    [Fact]
    public void The_bars_are_the_ladders_data_M6_8_H()
    {
        Assert.Equal((0.5, 1.0, 2, 1d, 2), (V.ManaPressMinBolts, V.StaminaDrainMinBolts, V.ManaLoopMaxAttackers, V.ManaLoopBoltFirstHits, V.ManaLoopBoltsBetween));
        Assert.Contains("drain-sums.md", LadderLoader.WarMage.Sources["vitals"], StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ M6 14: the stamina line and the press that fills (the live Abode of 2026-11-13: level 40, stamina 221, mana 332, Flame Bolt V at 5,
    // Tusker Redeemers holding 480 stamina, so one Drain Stamina Other I gives the cap, 60)

    private const int Redeemer = 480;

    [Fact]
    public void M6_14_the_new_lines_are_the_ladders_data_with_their_reasons()
    {
        Assert.Equal((80, 4, 90), (V.StaminaKeepPercent, V.StaminaDrainMaxAttackers, V.ManaPressFillPercent));
        Assert.Equal(30, V.StaminaDrainMinGive); // the stamina line's least give is the existing line
        var why = LadderLoader.WarMage.Sources["vitals"];
        Assert.Contains("stamina_keep_percent 80", why, StringComparison.Ordinal);
        Assert.Contains("stamina_drain_max_attackers 4 (the Abode's live hits under the buff bot's 28 lines", why, StringComparison.Ordinal);
        Assert.Contains("mana_press_fill_percent 90", why, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_14_live_under_the_stamina_line_the_drain_is_the_move_valued_in_stamina_not_the_top_up_press()
    {
        // Live: a press by the sum at mana 323 of 332 and stamina 87 of 221 netted 7.6 (bolt 5): the drain was read (under half) but valued only by the mana of the press after it, which at 9 missing never beat the press alone.
        var plan = ManaLoopRule.DecideBySum(Facts(323, 332, 87, 221, 5, Redeemer, attackers: 1), V);
        Assert.Equal((ManaLoopMove.DrainStamina, "the stamina line", 60d, true), (plan.Move, plan.Why, plan.Net, plan.StaminaLine));
    }

    [Fact]
    public void M6_14_the_stamina_line_drains_on_a_full_mana_bar()
    {
        var plan = ManaLoopRule.DecideBySum(Facts(332, 332, 87, 221, 5, Redeemer), V); // M6 8 H returned "no mana is missing" before any drain was read
        Assert.Equal((ManaLoopMove.DrainStamina, "the stamina line"), (plan.Move, plan.Why));
        Assert.Equal("no mana is missing", ManaLoopRule.DecideBySum(Facts(332, 332, 210, 221, 5, Redeemer), V).Why); // over the line, a full bar rests
    }

    [Fact]
    public void M6_14_over_the_stamina_line_a_press_that_would_overflow_waits_and_one_that_fills_goes()
    {
        var full = ManaLoopRule.DecideBySum(Facts(320, 332, 210, 221, 5, Redeemer), V); // II's give 100 against 12 missing (and I's 45): the live top-up
        Assert.Equal((ManaLoopMove.None, "the press would overflow"), (full.Move, full.Why));

        var fill = Facts(200, 332, 210, 221, 5, Redeemer);
        Assert.Equal((1, 100u), (fill.PressFill.Level, fill.PressFill.Give)); // the level that nets the most whole: II, 100 for 20
        var plan = ManaLoopRule.DecideBySum(fill, V); // 132 missing take the whole 100
        Assert.Equal((ManaLoopMove.Press, "the press fills", 80d, 1), (plan.Move, plan.Why, plan.Net, plan.Level));
    }

    [Theory]
    [InlineData(242u, ManaLoopMove.Press)] // 90 missing of II's give of 100: 90%, it fills
    [InlineData(243u, ManaLoopMove.None)] // 89 missing: under 90% of the give, it would overflow
    public void M6_14_the_fill_line_is_ninety_percent_of_the_give(uint mana, ManaLoopMove move)
    {
        Assert.Equal(move, ManaLoopRule.DecideBySum(Facts(mana, 332, 210, 221, 5, 0), V).Move);
        Assert.True(ManaLoopRule.Fills(100, 90, V));
        Assert.False(ManaLoopRule.Fills(100, 89, V));
        Assert.True(ManaLoopRule.Fills(100, 200, V)); // more missing than the give: the whole give lands
        Assert.False(ManaLoopRule.Fills(0, 100, V)); // no give fills nothing
    }

    [Fact]
    public void M6_14_a_bolt_she_cannot_pay_presses_as_before_overflow_or_not()
    {
        // Flame Bolt III at 15 with no Mana Conversion: 12 mana pays no bolt and no drain with one; the press by the sum goes (I: 40 for 10).
        var plan = ManaLoopRule.DecideBySum(Facts(12, 332, 87, 221, 15, Redeemer), V);
        Assert.Equal((ManaLoopMove.Press, "the press pays: the bolt cannot be paid", 0), (plan.Move, plan.Why, plan.Level));

        // 38 missing of 50 at stamina 210: II's whole 100 would overflow, but under a bolt the fill line does not hold: I nets 38 less 10.
        var small = ManaLoopRule.DecideBySum(Facts(12, 50, 210, 221, 15, 0), V);
        Assert.Equal((ManaLoopMove.Press, 28d, 0), (small.Move, small.Net, small.Level));
    }

    [Theory]
    [InlineData(3, ManaLoopMove.DrainStamina, "the stamina line")] // over mana_loop_max_attackers 2: the stamina line reaches four
    [InlineData(4, ManaLoopMove.DrainStamina, "the stamina line")]
    [InlineData(5, ManaLoopMove.None, "the stamina line waits: too many are hitting her")]
    public void M6_14_the_stamina_line_drains_with_up_to_four_hitting_her(int attackers, ManaLoopMove move, string why)
    {
        var plan = ManaLoopRule.DecideBySum(Facts(323, 332, 87, 221, 5, Redeemer, attackers: attackers), V);
        Assert.Equal((move, why), (plan.Move, plan.Why));
    }

    [Fact]
    public void M6_14_the_stamina_line_waits_for_the_bolts_between_and_never_drains_the_target_dying_to_the_next_bolt()
    {
        Assert.Equal("the stamina line waits: bolts between the mana casts", ManaLoopRule.DecideBySum(Facts(323, 332, 87, 221, 5, Redeemer, owed: 1), V).Why);
        Assert.Equal("the stamina line waits a bolt: its target dies to it", ManaLoopRule.DecideBySum(Facts(323, 332, 87, 221, 5, Redeemer, dying: true, drainOnTarget: true), V).Why);
        Assert.Equal(ManaLoopMove.DrainStamina, ManaLoopRule.DecideBySum(Facts(323, 332, 87, 221, 5, Redeemer, dying: true), V).Move); // another target: the drain goes

        // She must pay the drain and still a bolt: 14 pays the drain's 10 and not a bolt of 5 after it; the mana casts decide (the press, the bolt being payable: it fills).
        var poor = ManaLoopRule.DecideBySum(Facts(14, 332, 87, 221, 5, Redeemer), V);
        Assert.NotEqual(ManaLoopMove.DrainStamina, poor.Move);
        Assert.Equal(ManaLoopMove.DrainStamina, ManaLoopRule.DecideBySum(Facts(15, 332, 87, 221, 5, Redeemer), V).Move);

        // A drain under the least give (a Mite's 140 gives 28) is not the stamina line's: the mana casts decide.
        Assert.NotEqual("the stamina line", ManaLoopRule.DecideBySum(Facts(323, 332, 87, 221, 5, 140), V).Why);
    }

    [Fact]
    public void M6_14_the_fill_is_the_level_that_nets_most_whole_and_names_its_give()
    {
        var levels = Press;
        var fill = ManaLoopRule.FillOption(levels, 210, 320, 332, 0, 0.95);
        Assert.Equal((1, 100u), (fill.Level, fill.Give)); // II: 100 for 20, against I's 45 for 10
        Assert.Equal(-8d, fill.Net); // read at the 12 missing: 12 less 20
        var byNet = ManaLoopRule.PressOption(levels, 210, 320, 332, 0, 0.95);
        Assert.Equal((0, 45u), (byNet.Level, byNet.Give)); // the press by the sum, overfill counted: I nets 2 (the live top-up's pick)
    }

    [Fact]
    public void The_bar_holds_on_either_vital_and_only_where_a_ground_sets_it()
    {
        var lodge = GroundCatalog.Get("tusker-lodge").PullRest;
        Assert.Equal(100, lodge!.FullPercent);
        Assert.False(PullRestRule.UnderFullBar(lodge, 154, 154, 57, 57));
        Assert.True(PullRestRule.UnderFullBar(lodge, 153, 154, 57, 57));
        Assert.True(PullRestRule.UnderFullBar(lodge, 154, 154, 56, 57));
        Assert.False(PullRestRule.UnderFullBar(GroundCatalog.Get("holtburg-outskirts").PullRest, 1, 154, 1, 57)); // M6 7 AR: the Burrow has the bar now, the outskirts do not
        Assert.False(PullRestRule.UnderFullBar(null, 1, 154, 1, 57));
    }

    [Fact]
    public void Under_the_bar_the_plan_goes_to_the_top_of_both_with_the_stamina_in_its_bound()
    {
        var lodge = GroundCatalog.Get("tusker-lodge").PullRest!;
        var input = new PullRestInput(154, 154, 50, 72, lodge, PullRestRule.SeedManaPerSecond, 21, 57);
        var plan = PullRestRule.Decide(in input);

        Assert.True(plan.Wanted);
        Assert.Equal(57u, plan.StaminaTarget);
        Assert.Equal(154u, plan.ManaTarget);
        Assert.Contains("stamina 21/57 under 100%", plan.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_outskirts_keep_todays_line_a_rest_at_stamina_30_percent_to_80_and_no_lower()
    {
        var rule = GroundCatalog.Get("holtburg-outskirts").PullRest!;
        Assert.Equal(VitalsOptions.DefaultStaminaBelowPercent, rule.StaminaPercent);
        Assert.Equal(VitalsOptions.DefaultStaminaUntilPercent, rule.UntilPercent);
        var input = new PullRestInput(100, 154, 72, 72, rule, 0.25, 17, 63);
        Assert.True(PullRestRule.Decide(in input).Wanted);
        input = new PullRestInput(100, 154, 72, 72, rule, 0.25, 19, 63);
        Assert.False(PullRestRule.Decide(in input).Wanted);
    }

    [Fact]
    public void At_or_below_the_line_she_drains_above_it_she_does_not_on_any_ground()
    {
        var under = new FightDrain(new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>(), new RecordingLogSink(), FightOptions.DefaultDrainUnderPercent, FightOptions.DefaultDrainUntilPercent);
        Assert.False(under.Wanted(51, 100, 0));
        Assert.True(under.Wanted(50, 100, 0));
    }
}

/// <summary>M6 7 AN: the fullness bar and the recharge, scripted on the real Lodge profile; the mana loop's press and its pass over a creature that holds too little.</summary>
public sealed class FightRechargeTests
{
    private const uint Lodge = 0x5749010D;
    private const uint MaleTusker = 11;

    private static (FightManaTests.Rig Rig, C2VitalsRig.PostureSpy Spy) Rig(uint mana)
    {
        var r = new FightManaTests.Rig(mana: mana, stamina: uint.MaxValue, knowsDance: false, fight: new FightOptions { Ground = "tusker-lodge", Kills = 1, TargetMargin = 5, StaminaFullPercent = 0, Seconds = 3600 })
        {
            TuskerWcid = MaleTusker,
        };
        (r.TuskerCell, r.PlayerCell) = (Lodge, Lodge);
        r.R.H.R.Reducer.OnUpdatePosition(r.R.H.R.Context, ReducerHarness.Move(ActionHarness.Amy, 1, 3, 2, 1, cell: Lodge, x: 100));
        r.R.H.Unhide(stateStamp: 5);
        r.Caster.ManaRegenPerSecond = 5;
        var spy = new C2VitalsRig.PostureSpy();
        r.R.Api.Move.Bind(spy, false);
        return (r, spy);
    }

    private static uint MaxMana(FightManaTests.Rig r)
    {
        r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out _, out var max);
        return max;
    }

    [Fact]
    public void In_the_Lodge_she_lies_down_and_no_pull_comes_until_stamina_and_mana_are_full()
    {
        var (r, spy) = Rig(mana: 150);
        r.AddTusker(y: 48); // over the 24 m of sight
        var scenario = r.Scenario();
        r.Run(scenario, 5, 0.1, () => spy.Posture != 0);

        Assert.Equal(VitalsKeeper.LieCommand, spy.Posture);
        Assert.Equal("0x8000003D down", spy.Calls[0]); // the walker's stance first
        Assert.Empty(r.BoltAnswers);
        r.Run(scenario, 600, 0.1, () => r.BoltAnswers.Any());

        Assert.Contains(r.BoltAnswers, _ => true);
        Assert.True(r.Mana + 60 >= MaxMana(r), $"the pull came at {r.Mana} of {MaxMana(r)}");
        Assert.Contains(r.Log.Lines, l => l.Contains("rest: stretch: posture lie", StringComparison.Ordinal) && l.Contains("ACE 1.8", StringComparison.Ordinal));
    }

    [Fact]
    public void A_creature_in_sight_under_the_bar_is_a_standing_rest_not_a_pull()
    {
        var (r, spy) = Rig(mana: 150);
        r.AddTusker(y: 30); // 10 m off
        var scenario = r.Scenario();
        r.Run(scenario, 10, 0.1, () => r.BoltAnswers.Any());

        Assert.Empty(r.BoltAnswers);
        Assert.Equal(0u, spy.Posture);
        Assert.Contains(r.Log.Lines, l => l.Contains("a standing rest", StringComparison.Ordinal));
        Assert.Equal(0, scenario.PullRestsRefused);

        r.Run(scenario, 600, 0.1, () => r.BoltAnswers.Any());
        Assert.True(r.Mana + 60 >= MaxMana(r), $"the pull came at {r.Mana} of {MaxMana(r)}");
    }

    [Fact]
    public void An_attacker_is_fought_under_the_bar()
    {
        var (r, _) = Rig(mana: 150);
        r.Caster.ManaRegenPerSecond = 1;
        r.AddTusker(y: 30);
        var scenario = r.Scenario();
        r.Run(scenario, 2, 0.1, () => r.BoltAnswers.Any());
        Assert.Empty(r.BoltAnswers);

        r.R.Server((p, s) => Lifestone.Tests.Shared.Net.AceM3Writers.DefenderNotification(p, s, "Tusker Slave", 4, 0.1, 8, 4, false, 0));
        r.Run(scenario, 10, 0.1, () => r.BoltAnswers.Any());

        Assert.Contains(r.BoltAnswers, _ => true); // M5 D88: the always-fight-back rule stands
    }
}

/// <summary>M6 7 AN: the coin order and the lines' bars on the learn plan.</summary>
public sealed class RechargeLearnPlanTests
{
    private const string Outskirts = "holtburg-outskirts";

    private static ScrivenerCatalog C => ScrivenerCatalog.Default;

    [Theory]
    [InlineData("Regeneration Self", 3, 1.4)]
    [InlineData("Rejuvenation Self", 4, 1.55)]
    [InlineData("Mana Renewal Self", 3, 1.4)]
    public void The_regeneration_lines_are_in_the_catalog_I_to_IV_at_the_buff_bar(string line, int level, double _)
    {
        Assert.Equal(ScrivenerLineKind.Recovery, C.Line(line)!.Kind);
        Assert.Contains(C.LineSpells(line), s => s.Level == level);
        Assert.True(ScrivenerLearnPlan.IsBuffLine(ScrivenerLineKind.Recovery));
    }

    [Fact]
    public void The_sustain_lines_hold_exactly_the_levels_she_buys_and_no_Revitalize_or_higher_press_is_pushed()
    {
        // M6 14 (live 2026-11-12): Heal Self and Drain Health Other reach VI in the catalog: with Life Magic 252 standing she healed with level I and lay down for the rest; the plan buys the level her skill casts at its chance.
        Assert.Equal([1, 2, 3, 4, 5, 6], C.LineSpells("Drain Health Other").Select(s => s.Level));
        Assert.Equal([1], C.LineSpells("Drain Stamina Other").Select(s => s.Level));
        Assert.Equal([1, 2, 3, 4, 5, 6], C.LineSpells("Heal Self").Select(s => s.Level));
        Assert.Equal(ScrivenerLineKind.Revitalize, C.Line("Revitalize Self")!.Kind); // M6 14: listed since the owner's 2026-11-14 direction; the plan pushes it only when a round of the dance nets mana on her state (DancePlanTests: off at level 10)

        var q = new PlayerQualities { IsKnown = true };
        q.Vitals[MageTemplate.MaxStamina] = new Lifestone.Net.Messages.VitalDesc(MageTemplate.MaxStamina, 0, 57, 0, 57);
        var target = ScrivenerLearnPlan.DanceTarget(C.LineSpells("Stamina to Mana Self"), 4, q, out var note);
        Assert.Equal(1, target); // from 57 stamina a press nets +15.6 at I, +8.5 at II, +1.3 at III, -5.8 at IV
        Assert.Contains("level I nets the most", note, StringComparison.Ordinal);
    }

    /// <summary>A mage whose Life Magic reads <paramref name="life"/> (Focus 131 and Self 130 give 65; the rest is ranks), as the plan reads it with no ladder: the current skill.</summary>
    private static PlayerQualities Mage(uint life, bool lifeTrained = true)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Ints[PlayerQualities.LevelProperty] = 29;
        q.Attributes[MageTemplate.Focus] = new Lifestone.Net.Messages.AttributeDesc(MageTemplate.Focus, 31, 100, 0);
        q.Attributes[MageTemplate.Self] = new Lifestone.Net.Messages.AttributeDesc(MageTemplate.Self, 30, 100, 0);
        q.Skills[MageTemplate.LifeMagic] = new Lifestone.Net.Messages.SkillDesc(MageTemplate.LifeMagic, (ushort)(life - 65), 1, lifeTrained ? 2u : 1u, 0, 0u, 0, 0);
        q.Skills[MageTemplate.WarMagic] = new Lifestone.Net.Messages.SkillDesc(MageTemplate.WarMagic, 200, 1, 2u, 0, 0u, 0, 0);
        return q;
    }

    private static LearnNeed HarmNeed(PlayerQualities q, params int[] known)
    {
        var book = new Spellbook { IsKnown = true };
        foreach (var level in known)
        {
            book.Learn(C.LineSpells("Harm Other")[level - 1].SpellId);
        }

        var ground = GroundCatalog.Get(Outskirts);
        var needs = ScrivenerLearnPlan.Needs(ground, ScrivenerLearnPlan.RungOf(LadderLoader.WarMage, Outskirts, 29)!, LadderLoader.WarMage.PlanCastChance, q, book, C);
        return Assert.Single(needs, n => n.Line.Name == "Harm Other"); // one need, never a scroll for every level
    }

    [Fact]
    public void M6_14_Harm_Other_is_bought_at_the_highest_level_her_Life_Magic_casts_at_the_plans_chance()
    {
        Assert.True(GearCasts.TryCurrentSkill(Mage(267), MageTemplate.LifeMagic, out var read, out _));
        Assert.Equal(267u, read);

        var low = HarmNeed(Mage(140)); // 0.95 needs the skill 43 over the power: II (75) at 140
        Assert.Equal((LearnNeedKind.Due, "Harm Other II"), (low.Kind, low.Spell!.Name));

        var four = HarmNeed(Mage(267)); // IV (175); V (225) wants 268
        Assert.Equal((LearnNeedKind.Due, "Harm Other IV", 4), (four.Kind, four.Spell!.Name, four.Target));
        Assert.Contains("sold at Holtburg", four.Why, StringComparison.Ordinal);
        Assert.DoesNotContain("is sold only at", four.Why, StringComparison.Ordinal);

        Assert.Equal(LearnNeedKind.Known, HarmNeed(Mage(267), 4).Kind);
        Assert.Equal((LearnNeedKind.Due, 4), (HarmNeed(Mage(267), 1, 2, 3).Kind, HarmNeed(Mage(267), 1, 2, 3).Target)); // a lower level held does not stand in for the target

        Assert.Equal(LearnNeedKind.Untrained, HarmNeed(Mage(267, lifeTrained: false)).Kind);
        var one = HarmNeed(Mage(100)); // I (power 1); II wants 118
        Assert.Equal((LearnNeedKind.Due, "Harm Other I"), (one.Kind, one.Spell!.Name));
    }

    [Fact]
    public void M6_14_a_level_sold_only_off_home_never_leaves_her_without_Harm_Other_the_homes_highest_is_bought_first()
    {
        // Life Magic 268 casts V at the plan's chance; Holtburg sells I to IV. The home's IV first; with IV in the book the off-home answer stands and the Town Network fetches V as it does any line's.
        var first = HarmNeed(Mage(268));
        Assert.Equal((LearnNeedKind.Due, "Harm Other IV", 4), (first.Kind, first.Spell!.Name, first.Target));
        Assert.EndsWith("; the plan's level V is sold only at Dryreach, Plateau Village, Qalaba'r, Westwatch: the highest level Holtburg sells is bought first", first.Why, StringComparison.Ordinal);

        var then = HarmNeed(Mage(268), 4);
        Assert.Equal((LearnNeedKind.OffHome, "Harm Other V"), (then.Kind, then.Spell!.Name));
        Assert.Equal(["Dryreach", "Plateau Village", "Qalaba'r", "Westwatch"], then.Towns);

        Assert.Equal(LearnNeedKind.Known, HarmNeed(Mage(268), 4, 5).Kind);
        var six = HarmNeed(Mage(318)); // VI (275) at the chance: the Grand Masters' towns; the home's IV first all the same
        Assert.Equal((LearnNeedKind.Due, "Harm Other IV"), (six.Kind, six.Spell!.Name));
        Assert.Contains("the plan's level VI is sold only at ", six.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void After_the_first_scrolls_the_coin_goes_to_the_drains_then_the_regeneration_lines_before_the_item_lines()
    {
        var rung = ScrivenerLearnPlan.RungOf(LadderLoader.WarMage, Outskirts, 15)!;
        var names = ScrivenerLearnPlan.LinesFor(rung, C, GroundCatalog.Get("tusker-lodge")).Select(l => l.Name).ToList();

        Assert.True(names.IndexOf("Bludgeoning Protection Self") < names.IndexOf("Drain Health Other"));
        Assert.True(names.IndexOf("Heal Self") < names.IndexOf("Harm Other")); // M6 14 (m6-life-through): Harm Other with the drains she fights with
        Assert.True(names.IndexOf("Harm Other") < names.IndexOf("Regeneration Self"));
        Assert.True(names.IndexOf("Heal Self") < names.IndexOf("Regeneration Self"));
        Assert.True(names.IndexOf("Rejuvenation Self") < names.IndexOf("Impenetrability"));
        Assert.True(names.IndexOf("Focus Self") < names.IndexOf("Armor Self"));
    }
}

/// <summary>M6 7 AN: every component of the new lines, on the account test's formula, is a stock line of the outskirts and the Lodge, or the level is dropped by name in the fight (LevelFloor).</summary>
public sealed class RechargeComponentTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [Theory]
    [InlineData("holtburg-outskirts")]
    [InlineData("tusker-lodge")]
    public void Every_component_of_the_regeneration_and_sustain_lines_is_stocked(string ground)
    {
        var table = SpellTableData.Load(fixture.Dats).Spells;
        var stock = GroundCatalog.Get(ground).Supplies.Stock.Select(s => s.Wcid).ToHashSet();
        var missing = new SortedSet<string>();
        foreach (var line in new[] { "Regeneration Self", "Rejuvenation Self", "Mana Renewal Self", "Drain Health Other", "Drain Stamina Other", "Heal Self", "Harm Other" }) // M6 14: Harm Other, as the other lines
        {
            foreach (var spell in ScrivenerCatalog.Default.LineSpells(line).Where(s => s.Level <= 4))
            {
                foreach (var component in AccountFormula.Counts(table[spell.SpellId], false, "test").Keys)
                {
                    if (ComponentWeenies.TryGetByComponent(component, out var w) && !stock.Contains(w.Wcid))
                    {
                        missing.Add(FormattableString.Invariant($"{w.Wcid} {w.Name}"));
                    }
                }
            }
        }

        Assert.True(missing.Count == 0, string.Join("; ", missing));
    }
}

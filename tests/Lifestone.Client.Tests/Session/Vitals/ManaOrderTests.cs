using Lifestone.Client.Options;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// The mana order (M5 item 1 lane K5, <see cref="ManaOrder"/>; D15, D27): every branch of <see cref="ManaOrder.Decide"/> for the mage and the fighters, the try floor, the dance's level, the stamina rule, the
/// pre-emptive transfer test F1 uses, the options and their errors, and no allocation. The numbers are item 0's (Mana Conversion 311, Meditative Trance base 70 power 300, Stamina to Mana Self VI base 60 power 250).
/// </summary>
public sealed class ManaOrderTests
{
    private const uint Mc = 311;

    private static readonly DanceLevel Six = new(1681, 60, 250, BuffStatMods.TransferOf(1681)!.Value);

    private static readonly DanceLevel Trance = new(2345, 70, 300, BuffStatMods.TransferOf(2345)!.Value);

    private static readonly DanceLevel[] Book = [Six, Trance];

    private static ManaOrderInput Input(uint mana, uint stamina = 200, ManaNeed? need = null, int potions = 0, ManaProfile profile = ManaProfile.Mage, bool triesSpent = false, int fighterLeft = 2, int fullPercent = 0, int staminaPotions = 0, uint staminaFloor = 0) => new()
    {
        StaminaFullPercent = fullPercent,
        StaminaPotions = staminaPotions,
        StaminaFloor = staminaFloor,
        Mana = mana,
        ManaMax = 426,
        Stamina = stamina,
        StaminaMax = 275,
        ManaConversion = Mc,
        Need = need ?? new ManaNeed(70, 5),
        ManaPotions = potions,
        Profile = profile,
        TriesSpent = triesSpent,
        FighterGatePercent = 80,
        FighterTransfersLeft = fighterLeft,
    };

    private static readonly DanceLevel PressI = new(1676, 10, 1, BuffStatMods.TransferOf(1676)!.Value);

    private static readonly DanceLevel PressII = new(1677, 20, 50, BuffStatMods.TransferOf(1677)!.Value);

    private static readonly DanceLevel PressIII = new(1678, 30, 100, BuffStatMods.TransferOf(1678)!.Value);

    private static ManaOrderInput Climber(uint mana, uint stamina, uint boltMana = 10) => new()
    {
        Mana = mana,
        ManaMax = 133,
        Stamina = stamina,
        StaminaMax = 50,
        Need = new ManaNeed(boltMana, boltMana),
        Profile = ManaProfile.Mage,
        StaminaFullPercent = 90,
        BestNetPress = true,
    };

    [Theory]
    [InlineData(50u, 0, 12.0)] // gives 22 (25 x 0.9, ACE's round to even) for 10; II 5; III -2
    [InlineData(100u, 0, 35.0)]
    [InlineData(120u, 1, 40.0)] // I stops at its cap of 50 drained: 35
    [InlineData(21u, 0, 0.0)] // 10 x 0.9 rounds to 9... the best of three nets nothing or less
    public void The_best_net_level_is_not_the_highest_at_a_low_stamina_M6_7_AD(uint stamina, int level, double net)
    {
        var at = ManaOrder.BestNetLevel([PressI, PressII, PressIII], stamina, 100, 0, ManaOrderOptions.Default.TryChance, out var yield);
        Assert.Equal(level, at);
        if (net > 0)
        {
            Assert.Equal(net, yield, 1);
        }
        else
        {
            Assert.True(yield <= 1, $"net {yield}");
        }
    }

    [Fact]
    public void The_best_net_level_counts_only_a_press_the_mana_can_pay_M6_7_AD()
    {
        Assert.Equal(-1, ManaOrder.BestNetLevel([PressI, PressII], 50, 9, 0, ManaOrderOptions.Default.TryChance, out _)); // r3d's 8 mana: nothing lands
        Assert.Equal(0, ManaOrder.BestNetLevel([PressI, PressII], 150, 19, 0, ManaOrderOptions.Default.TryChance, out _)); // II would net more, but 19 cannot pay it
    }

    [Fact]
    public void A_mage_with_no_Mana_Conversion_presses_I_at_50_stamina_whatever_else_the_book_holds_M6_7_AD()
    {
        var d = ManaOrder.Decide(Climber(12, 50, boltMana: 15), [PressI, PressII, PressIII], ManaOrderOptions.Default);
        Assert.Equal((ManaStep.Dance, 0), (d.Step, d.DanceIndex));

        var keeper = Climber(35, 150, boltMana: 40) with { StaminaMax = 200, BestNetPress = false }; // the callers outside the fight press their highest, as before
        Assert.Equal(2, ManaOrder.Decide(keeper, [PressI, PressII, PressIII], ManaOrderOptions.Default).DanceIndex);
        Assert.Equal(1, ManaOrder.Decide(keeper with { BestNetPress = true }, [PressI, PressII, PressIII], ManaOrderOptions.Default).DanceIndex); // II 55, III 52
    }

    [Theory]
    [InlineData(71u, 0, 22.0)] // live: I gives round(36 x 0.9) = 32 for 10; II gives 36 for 20
    [InlineData(150u, 1, 55.0)] // I stops at its cap of 50 drained (45 for 10); II gives 75 for 20
    public void Pick_by_the_sum_reads_the_real_rows_at_her_stamina_with_no_Mana_Conversion(uint stamina, int level, double net)
    {
        var at = ManaOrder.PressLevelOf([PressI, PressII], stamina, 100, 0, ManaOrderOptions.Default.TryChance, out var yield);

        Assert.Equal((level, net), (at, yield));
        var d = ManaOrder.Decide(new ManaOrderInput { Mana = 25, ManaMax = 200, Stamina = stamina, StaminaMax = 200, Need = new ManaNeed(60, 60), Profile = ManaProfile.Mage, BestNetPress = true }, [PressI, PressII], ManaOrderOptions.Default);
        Assert.Equal((ManaStep.Dance, level), (d.Step, d.DanceIndex));
    }

    [Fact]
    public void Pick_by_the_sum_passes_her_Mana_Conversion_through()
    {
        var levels = new[] { PressII, PressIII };
        Assert.Equal(0, ManaOrder.PressLevelOf(levels, 150, 100, 0, ManaOrderOptions.Default.TryChance, out _)); // untrained: II 55, III 52
        Assert.Equal(1, ManaOrder.PressLevelOf(levels, 150, 100, 311, ManaOrderOptions.Default.TryChance, out _)); // trained, the cost discounted: III's larger give wins
        var input = new ManaOrderInput { Mana = 25, ManaMax = 200, Stamina = 150, StaminaMax = 200, ManaConversion = 311, Need = new ManaNeed(120, 120), Profile = ManaProfile.Mage, BestNetPress = true };
        Assert.Equal(1, ManaOrder.Decide(input, levels, ManaOrderOptions.Default).DanceIndex);
    }

    [Fact]
    public void When_no_level_reaches_the_chance_the_press_still_happens_on_the_best_chance_above_zero()
    {
        Assert.Equal(-1, ManaOrder.BestNetLevel(Book, 200, 1, Mc, 0.5, out _));
        var at = ManaOrder.PressLevelOf(Book, 200, 1, Mc, 0.5, out var net);

        Assert.Equal(ManaOrder.DanceLevelOf(Book, 1, Mc, 0.5), at);
        Assert.True(at >= 0);
        Assert.Equal(ManaOrder.NetYield(in Book[at], 200, Mc, 1), net);
        Assert.Equal(-1, ManaOrder.PressLevelOf(Book, 200, 0, Mc, 0.5, out var none));
        Assert.Equal(0, none);
    }

    [Fact]
    public void Only_the_ladders_character_picks_by_the_sum()
    {
        Assert.True(CharacterPolicy.For(MageTemplate.ClimberName).ManaBySum);
        Assert.False(CharacterPolicy.For(MageTemplate.CharacterName).ManaBySum);
        Assert.False(CharacterPolicy.For(null).ManaBySum);
    }

    [Fact]
    public void A_mage_short_of_the_bolt_and_of_the_press_waits_and_one_with_no_net_does_not_press_M6_7_AD()
    {
        var empty = ManaOrder.Decide(Climber(8, 50), [PressI], ManaOrderOptions.Default); // r3d's end: the bolt costs 10, and so does the press
        Assert.Equal(ManaStep.Wait, empty.Step);
        Assert.Contains("no press can land", empty.Reason, StringComparison.Ordinal);

        var poor = ManaOrder.Decide(Climber(14, 20, boltMana: 15), [PressI], ManaOrderOptions.Default); // gives 9 for 10
        Assert.NotEqual(ManaStep.Dance, poor.Step);
        Assert.Equal(0, poor.NoNetLevel);
    }

    [Fact]
    public void With_a_Mana_Potion_held_a_mage_who_cannot_press_drinks_and_never_waits_M6_8()
    {
        // Live 2026-11-11 18:52 UTC, the climb's third death: mana 0, five tuskers on her, 83 Mana Potions in the pack, and the order waited.
        var empty = ManaOrder.Decide(Climber(0, 50, boltMana: 15) with { ManaPotions = 83 }, [PressI], ManaOrderOptions.Default);
        Assert.Equal(ManaStep.Potion, empty.Step);
        Assert.Contains("no press can land at the mana held: a Mana Potion", empty.Reason, StringComparison.Ordinal);

        var poor = ManaOrder.Decide(Climber(14, 20, boltMana: 15) with { ManaPotions = 1 }, [PressI], ManaOrderOptions.Default); // the press gives 9 for 10
        Assert.Equal(ManaStep.Potion, poor.Step);

        Assert.Equal(ManaStep.Wait, ManaOrder.Decide(Climber(0, 50, boltMana: 15), [PressI], ManaOrderOptions.Default).Step); // none held: the wait as before
    }

    [Theory]
    [InlineData(10u, 13.0, 10u, true)] // the bolt leaves no press: press first
    [InlineData(19u, 13.0, 10u, true)]
    [InlineData(20u, 13.0, 10u, false)] // a press is still paid after the bolt
    [InlineData(9u, 13.0, 10u, false)] // no press lands
    [InlineData(15u, 1.0, 10u, false)] // a second press from half the bar nets 1: not sent
    [InlineData(15u, 5.0, 10u, true)]
    [InlineData(24u, 13.0, 15u, true)] // Flame Bolt III
    [InlineData(15u, 0.0, 5u, false)]
    public void The_press_is_kept_when_the_bolt_would_leave_none_and_it_buys_half_a_bolt_M6_7_AD(uint mana, double net, uint bolt, bool press) =>
        Assert.Equal(press, ManaOrder.KeepPress(mana, bolt, in PressI, net));

    [Fact]
    public void At_or_over_the_ceiling_or_with_no_need_the_order_is_ready()
    {
        Assert.Equal(ManaStep.Ready, ManaOrder.Decide(Input(70), Book, ManaOrderOptions.Default).Step);
        Assert.Equal(ManaStep.Ready, ManaOrder.Decide(Input(0, need: ManaNeed.None), Book, ManaOrderOptions.Default).Step);
        Assert.Equal(ManaStep.Ready, ManaOrder.Decide(Input(70), Book, ManaOrderOptions.Ceiling).Step);
    }

    [Fact]
    public void Cast_mana_tries_0_is_todays_ceiling_order_under_the_ceiling_whatever_the_floor_the_stamina_or_the_potions()
    {
        foreach (var mana in new uint[] { 0, 3, 10, 69 })
        {
            var d = ManaOrder.Decide(Input(mana, potions: 5), Book, ManaOrderOptions.Ceiling);
            Assert.Equal(ManaStep.CeilingOrder, d.Step);
            Assert.Equal(70u, d.Target);
        }

        Assert.False(ManaOrderOptions.Ceiling.On);
        Assert.Equal(70u, ManaOrder.FloorOf(70, 300, 70, Mc, ManaOrderOptions.Ceiling)); // no floor under the ceiling
    }

    [Fact]
    public void At_or_over_the_try_floor_the_real_spell_is_tried_and_once_its_tries_are_spent_the_order_funds_the_ceiling()
    {
        var d = ManaOrder.Decide(Input(10), Book, ManaOrderOptions.Default);
        Assert.Equal(ManaStep.TryReal, d.Step);
        Assert.Equal(5u, d.Target);
        Assert.Equal(ManaStep.TryReal, ManaOrder.Decide(Input(5), Book, ManaOrderOptions.Default).Step);

        var spent = ManaOrder.Decide(Input(10, triesSpent: true), Book, ManaOrderOptions.Default);
        Assert.Equal(ManaStep.Dance, spent.Step);
        Assert.Equal(70u, spent.Target);
    }

    [Fact]
    public void Under_the_floor_the_mage_dances_with_the_highest_level_whose_press_lands_at_the_try_chance()
    {
        var d = ManaOrder.Decide(Input(4), Book, ManaOrderOptions.Default);
        Assert.Equal(ManaStep.Dance, d.Step);
        Assert.Equal(1, d.DanceIndex); // the Trance lands 0.25 or more from 4 mana at 311 (item 0: 0.348 at 5)

        Assert.Equal(0, ManaOrder.DanceLevelOf(Book, 1, Mc)); // at 1 mana the Trance lands 12% a press and VI 28%: VI
        Assert.Equal(0, ManaOrder.Decide(Input(1), Book, ManaOrderOptions.Default).DanceIndex);
        Assert.Equal(0, ManaOrder.DanceLevelOf(Book, 1, Mc, minChance: 0.5)); // none reaches 0.5: the best chance above 0, VI
        Assert.Equal(-1, ManaOrder.DanceLevelOf(Book, 0, Mc)); // no press lands with no mana
        Assert.Equal(-1, ManaOrder.DanceLevelOf(Book, 59, 0)); // untrained: only at the base
        Assert.Equal(0, ManaOrder.DanceLevelOf(Book, 60, 0));
    }

    [Fact]
    public void A_mage_whose_stamina_is_too_low_for_a_transfer_drinks_a_potion_and_with_none_rests_in_peace_for_stamina_to_its_target()
    {
        var potion = ManaOrder.Decide(Input(2, stamina: 4, potions: 1), Book, ManaOrderOptions.Default);
        Assert.Equal(ManaStep.Potion, potion.Step);

        var rest = ManaOrder.Decide(Input(2, stamina: 4), Book, ManaOrderOptions.Default);
        Assert.Equal(ManaStep.RestStamina, rest.Step);
        var row = Book[0].Row; // the dance's level at 2 mana is VI
        Assert.True(ManaOrder.Funds(in row, rest.StaminaTarget, 275, 5, 2, 60));
        Assert.False(ManaOrder.Funds(in row, rest.StaminaTarget - 1, 275, 5, 2, 60));
        Assert.Equal(ManaStep.Dance, ManaOrder.Decide(Input(2, stamina: rest.StaminaTarget), Book, ManaOrderOptions.Default).Step);

        // The stamina enough but no press able to land (no mana at all): the wait, never a potion for the mage.
        Assert.Equal(ManaStep.Wait, ManaOrder.Decide(Input(0, potions: 3), Book, ManaOrderOptions.Default).Step);

        // No Stamina to Mana Self known: a potion, else the wait.
        Assert.Equal(ManaStep.Potion, ManaOrder.Decide(Input(2, potions: 1), [], ManaOrderOptions.Default).Step);
        Assert.Equal(ManaStep.Wait, ManaOrder.Decide(Input(2), [], ManaOrderOptions.Default).Step);
    }

    [Fact]
    public void The_stamina_rule_reaches_the_target_at_the_worst_roll_or_gives_twice_the_base()
    {
        var row = Trance.Row; // 0.875 of the stamina (proportion 0.5, loss -0.75: ACE HandleCastSpell_Transfer)
        Assert.Equal(175u, ManaOrder.Give(in row, 200));
        Assert.True(ManaOrder.Funds(in row, 10, 275, 5)); // 9 for 10: the floor of 5 is reached
        Assert.False(ManaOrder.Funds(in row, 40, 275, 70)); // 35 is short of 70 and of twice the base (140)
        Assert.True(ManaOrder.Funds(in row, 40, 275, 70, mana: 110, baseMana: 70)); // 40 kept at the dearest roll, plus 35
        Assert.True(ManaOrder.Funds(in row, 160, 275, 400, baseMana: 70)); // 140: twice the base, whatever the target
        Assert.False(ManaOrder.Funds(in row, 0, 275, 1));
    }

    [Fact]
    public void The_fighters_keep_D40s_order_a_potion_first_then_the_dance_at_their_stamina_gate_within_the_cap_then_the_wait()
    {
        Assert.Equal(ManaStep.Potion, ManaOrder.Decide(Input(2, stamina: 275, potions: 1, profile: ManaProfile.Fighter), Book, ManaOrderOptions.Default).Step);
        var dance = ManaOrder.Decide(Input(2, stamina: 230, profile: ManaProfile.Fighter), Book, ManaOrderOptions.Default); // 84% of 275
        Assert.Equal(ManaStep.Dance, dance.Step);
        Assert.Equal(0, dance.DanceIndex);
        Assert.Equal(ManaStep.Wait, ManaOrder.Decide(Input(2, stamina: 200, profile: ManaProfile.Fighter), Book, ManaOrderOptions.Default).Step); // 73%: under the gate
        Assert.Equal(ManaStep.Wait, ManaOrder.Decide(Input(2, stamina: 275, profile: ManaProfile.Fighter, fighterLeft: 0), Book, ManaOrderOptions.Default).Step); // the cap spent
        Assert.Equal(ManaStep.TryReal, ManaOrder.Decide(Input(10, profile: ManaProfile.Fighter, potions: 4), Book, ManaOrderOptions.Default).Step); // over the floor: the real spell, no potion yet
    }

    [Fact]
    public void The_try_floor_is_the_models_at_the_chance_never_under_5_and_the_ceiling_for_an_untrained_caster()
    {
        Assert.Equal(5u, ManaOrder.FloorOf(70, 300, 70, 311, ManaOrderOptions.Default)); // the Trance at 311: 3 raw, 5 by the minimum (K2)
        Assert.Equal(7u, ManaOrder.FloorOf(70, 300, 70, 258, ManaOrderOptions.Default));
        Assert.Equal(70u, ManaOrder.FloorOf(70, 300, 70, 0, ManaOrderOptions.Default));
        Assert.Equal(3u, ManaOrder.FloorOf(3, 1, 3, 311, ManaOrderOptions.Default)); // never over the ceiling
        Assert.Equal(new ManaNeed(70, 5), ManaOrder.NeedOf(70, 300, 70, 311, ManaOrderOptions.Default));
    }

    [Fact]
    public void The_preemptive_transfer_test_fires_near_the_stamina_maximum_with_enough_mana_missing()
    {
        Assert.True(ManaOrder.PreemptTransfer(250, 275, 100, 426)); // 91% and 326 missing
        Assert.False(ManaOrder.PreemptTransfer(240, 275, 100, 426)); // 87%
        Assert.False(ManaOrder.PreemptTransfer(275, 275, 350, 426)); // 76 missing, under 100
        Assert.True(ManaOrder.PreemptTransfer(275, 275, 326, 426));
        Assert.False(ManaOrder.PreemptTransfer(0, 0, 0, 426));
    }

    [Fact]
    public void The_options_parse_with_their_defaults_and_a_bad_value_is_a_named_cast_option_error()
    {
        var d = ManaOrderOptions.FromArguments([]);
        Assert.Equal((8, 0.25, 0.25, 40), (d.Tries, d.TryChance, d.TrySpacingSeconds, d.DancePresses));
        Assert.Empty(d.Errors);

        var set = ManaOrderOptions.FromArguments(CommandLineArgument.ParseAll(["--cast-mana-tries=0", "--cast-mana-try-chance=0.5", "--cast-mana-try-spacing-s=1", "--cast-mana-dance-presses=100"]));
        Assert.Equal((0, 0.5, 1.0, 100), (set.Tries, set.TryChance, set.TrySpacingSeconds, set.DancePresses));
        Assert.False(set.On);
        Assert.Empty(set.Errors);

        foreach (var bad in new[] { "--cast-mana-tries=41", "--cast-mana-tries=-1", "--cast-mana-tries=x", "--cast-mana-try-chance=0", "--cast-mana-try-chance=1.5", "--cast-mana-try-spacing-s=0.05", "--cast-mana-try-spacing-s=6", "--cast-mana-dance-presses=0", "--cast-mana-dance-presses=101" })
        {
            var o = ManaOrderOptions.FromArguments(CommandLineArgument.ParseAll([bad]));
            var error = Assert.Single(o.Errors);
            Assert.StartsWith("bad cast option: --cast-mana-", error, StringComparison.Ordinal);
            Assert.Equal(ManaOrderOptions.Default with { Errors = o.Errors }, o); // the option keeps its default
        }

        // The keeper's and the buff cycle's parsers read them, and report the same errors.
        var vitals = VitalsOptions.FromArguments(CommandLineArgument.ParseAll(["--cast-mana-tries=3", "--cast-mana-dance-presses=0"]));
        Assert.Equal(3, vitals.Mana.Tries);
        Assert.Contains(vitals.Errors, e => e.StartsWith("bad cast option: --cast-mana-dance-presses", StringComparison.Ordinal));
        var buff = BuffOptions.FromArguments(CommandLineArgument.ParseAll(["--cast-mana-tries=0"]));
        Assert.False(buff.Mana.On);
        Assert.Empty(buff.Errors);
        Assert.True(VitalsOptions.Default.Mana.On);
        Assert.True(new BuffOptions().Mana.On);
    }

    [Fact]
    [Trait("Tier", "Timing")] // m5-manaorder-alloc-flake: a zero-byte allocation count (7,104 bytes under load three times)
    public void A_decision_allocates_nothing()
    {
        var input = Input(2, stamina: 4);
        ManaOrder.Decide(input, Book, ManaOrderOptions.Default);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            ManaOrder.Decide(input, Book, ManaOrderOptions.Default);
            ManaOrder.Decide(Input((uint)(i % 80)), Book, ManaOrderOptions.Default);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // ---- M5 7 F5 (D88): the owner's order in the fight: the transfer, then Revitalize Self to refill the stamina, then the bolt.

    private static readonly RevitalizeLevel[] Revit = [new(1181, 50, 200), new(1182, 60, 250), new(2083, 70, 300)];

    [Fact]
    public void Mana_empty_with_the_stamina_full_gives_the_transfer_then_the_revitalize_then_the_bolt_M5_7_F5()
    {
        var opt = ManaOrderOptions.Default;
        var empty = ManaOrder.Decide(Input(2, stamina: 275, fullPercent: 90), Book, opt, Revit);
        Assert.Equal(ManaStep.Dance, empty.Step); // the transfer first: the bolt cannot be paid
        var after = ManaOrder.Decide(Input(250, stamina: 120, fullPercent: 90), Book, opt, Revit);
        Assert.Equal(ManaStep.Revitalize, after.Step); // the transfer landed: the stamina is refilled before the bolt
        Assert.Equal(2, after.DanceIndex); // the highest level the mana pays (70) with the bolt's floor (5) in hand
        Assert.Equal(ManaOrder.FullStamina(275, 90), after.StaminaTarget);
        var refilled = ManaOrder.Decide(Input(180, stamina: 255, fullPercent: 90), Book, opt, Revit);
        Assert.Equal(ManaStep.Ready, refilled.Step); // the stamina is back near full and the mana pays the bolt: the bolt
        Assert.Equal(ManaStep.Ready, ManaOrder.Decide(Input(180, stamina: 270, fullPercent: 90), Book, opt, Revit).Step);
    }

    [Fact]
    public void Stamina_low_between_kills_with_enough_mana_revitalizes_first_and_with_too_little_mana_the_bolt_goes_first_M5_7_F5()
    {
        var opt = ManaOrderOptions.Default;
        var rich = ManaOrder.Decide(Input(200, stamina: 100, fullPercent: 90), Book, opt, Revit);
        Assert.Equal(ManaStep.Revitalize, rich.Step); // the mana covers the ceiling and a Revitalize besides
        var level = ManaOrder.Decide(Input(60, stamina: 100, fullPercent: 90), Book, opt, Revit);
        Assert.Equal((ManaStep.Revitalize, 0), (level.Step, level.DanceIndex)); // 60 pays level V (50) with the floor (5) in hand, not VI (60)
        Assert.Equal(ManaStep.TryReal, ManaOrder.Decide(Input(52, stamina: 100, fullPercent: 90), Book, opt, Revit).Step); // 52 pays no level with the bolt's floor in hand: the bolt
        Assert.Equal(ManaStep.Ready, ManaOrder.Decide(Input(200, stamina: 100, fullPercent: 0), Book, opt, Revit).Step); // --fight-stamina-full 0: the order of item 1
        Assert.Equal(ManaStep.CeilingOrder, ManaOrder.Decide(Input(10, stamina: 100, fullPercent: 90), Book, ManaOrderOptions.Ceiling, Revit).Step);
        Assert.Equal(ManaStep.Ready, ManaOrder.Decide(Input(200, stamina: 100, fullPercent: 90, profile: ManaProfile.Fighter), Book, opt, Revit).Step); // the fighters keep their own order
    }

    [Fact]
    public void No_payable_revitalize_falls_back_to_a_stamina_potion_only_then_M5_7_F5()
    {
        var opt = ManaOrderOptions.Default;
        Assert.Equal(ManaStep.StaminaPotion, ManaOrder.Decide(Input(200, stamina: 100, fullPercent: 90, staminaPotions: 2), Book, opt, default).Step); // no level at the chance or in the pack's scarabs
        Assert.Equal(ManaStep.Ready, ManaOrder.Decide(Input(200, stamina: 100, fullPercent: 90), Book, opt, default).Step); // none and no potion: the order goes on
        Assert.Equal(ManaStep.TryReal, ManaOrder.Decide(Input(52, stamina: 100, fullPercent: 90, staminaPotions: 2), Book, opt, Revit).Step); // a level is payable, the mana is short of it: no potion
        Assert.Equal(ManaStep.Ready, ManaOrder.Decide(Input(200, stamina: 270, fullPercent: 90, staminaPotions: 2), Book, opt, default).Step); // the stamina near full: no potion
    }

    [Fact]
    [Trait("Tier", "Timing")] // m5-manaorder-alloc-flake: the same zero-byte allocation count, on the refill path
    public void A_decision_with_the_refill_allocates_nothing_M5_7_F5()
    {
        var rich = Input(200, stamina: 100, fullPercent: 90, staminaPotions: 1, staminaFloor: 40);
        var low = Input(2, stamina: 30, fullPercent: 90, staminaFloor: 40);
        ManaOrder.Decide(rich, Book, ManaOrderOptions.Default, Revit);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            ManaOrder.Decide(rich, Book, ManaOrderOptions.Default, Revit);
            ManaOrder.Decide(low, Book, ManaOrderOptions.Default, Revit);
            ManaOrder.Decide(rich, Book, ManaOrderOptions.Default, default);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void With_stamina_over_its_floor_the_mage_presses_the_transfer_instead_of_drinking_or_resting_M5_7_F5()
    {
        // Stamina 4 of 275 does not fund the target (Funds is false), so item 1's order drank a Mana Potion or rested. D88: stamina there to transfer (at or over its floor) is pressed until a roll lands.
        var d = ManaOrder.Decide(Input(2, stamina: 4, potions: 3, fullPercent: 90, staminaFloor: 3), Book, ManaOrderOptions.Default, Revit);
        Assert.Equal(ManaStep.Dance, d.Step);
        Assert.Equal(ManaStep.Potion, ManaOrder.Decide(Input(2, stamina: 4, potions: 3), Book, ManaOrderOptions.Default).Step); // item 1's order, unchanged with the refill off
        Assert.Equal(ManaStep.Potion, ManaOrder.Decide(Input(2, stamina: 2, potions: 3, fullPercent: 90, staminaFloor: 3), Book, ManaOrderOptions.Default, Revit).Step); // under the floor: as before
    }

    [Fact]
    public void The_floors_keep_one_transfer_press_and_one_revitalize_press_and_the_stamina_that_pays_them_M5_7_F5()
    {
        var mana = ManaOrder.ManaFloorOf(60, 250, 60, 250, Mc);
        Assert.Equal(ManaConversionModel.TryFloor(60, 250, Mc) * 2, mana);
        var row = BuffStatMods.TransferOf(1681)!.Value;
        var stamina = ManaOrder.StaminaFloorOf(in row, 275, 60, 60);
        Assert.True(ManaOrder.Give(in row, stamina) >= 120);
        Assert.True(stamina == 0 || ManaOrder.Give(in row, stamina - 1) < 120);
    }

    [Fact]
    public void A_press_that_nets_no_mana_is_not_sent_and_the_order_rests_while_one_that_nets_some_is_M6_7_C()
    {
        // Stamina to Mana I (row 1676: half the stamina, 10% lost, capped at 50), an untrained caster so the cost is the base: at stamina 20 the press drains 10 and gives 9 for a cost of 10.
        var one = new DanceLevel(1676, 10, 1, BuffStatMods.TransferOf(1676)!.Value);
        DanceLevel[] low = [one];
        ManaOrderInput At(uint stamina) => Input(10, stamina: stamina) with { ManaConversion = 0, Need = new ManaNeed(30, 30) };
        var poor = ManaOrder.Decide(At(20), low, ManaOrderOptions.Default);
        Assert.NotEqual(ManaStep.Dance, poor.Step);
        Assert.Equal(0, poor.NoNetLevel);
        Assert.Equal(-1.0, poor.NetYield);
        Assert.Equal(-1.0, ManaOrder.NetYield(in one, 20, 0));

        var rich = ManaOrder.Decide(At(100), low, ManaOrderOptions.Default);
        Assert.Equal(ManaStep.Dance, rich.Step);
        Assert.Equal(-1, rich.NoNetLevel);
        Assert.Equal(35.0, ManaOrder.NetYield(in one, 100, 0)); // drains 50, gives 45, costs 10

        // The fighters' order passes the same press over at the gate.
        var fighter = ManaOrder.Decide(At(20) with { Profile = ManaProfile.Fighter, StaminaMax = 20 }, low, ManaOrderOptions.Default);
        Assert.Equal(ManaStep.Wait, fighter.Step);
        Assert.Equal(0, fighter.NoNetLevel);
    }
}

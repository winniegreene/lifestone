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
}

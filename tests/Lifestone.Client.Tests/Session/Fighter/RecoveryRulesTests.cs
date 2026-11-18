using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// M4 3.1 s3: the recovery rule (<see cref="RecoveryRules"/>, <see cref="HealChance"/>, <see cref="RecoveryOptions"/>). The chance is ACE's formula (<c>W/Healer.cs</c>, spec 5.7) pinned against the live capture
/// (cache/m4/3-1/capture.md C1: eff 264, 145 missing in combat, 5 of 6 uses failed; 72 missing, every use won); each decision and its boundaries are read from the numbers below.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class RecoveryRulesTests
{
    private const uint Max = 188; // the Holtburg Hunter's health

    private static readonly RecoveryOptions Defaults = RecoveryOptions.Default;

    private static RecoveryInput In(uint health, int kit = 20, int potions = 5, bool active = false, int lost = 0, double waited = 0, bool combat = true, int skill = 190, bool specialised = false, uint max = Max) =>
        new(health, max, active, kit, potions, lost, waited, skill, specialised, combat);

    // ---------------------------------------------------------------------------------------------- the chance (ACE's formula, the capture's numbers)

    [Fact]
    public void The_effective_skill_is_the_healing_plus_the_boost_times_1_1_or_1_5_when_specialised()
    {
        Assert.Equal(264, HealChance.Effective(190, 50, false)); // the capture's number for the Holtburg Hunter
        Assert.Equal(360, HealChance.Effective(190, 50, true));
        Assert.Equal(55, HealChance.Effective(0, 50, false));
    }

    [Fact]
    public void The_difficulty_is_twice_the_missing_health_times_1_1_in_combat_and_1_0_out_of_it()
    {
        Assert.Equal(319, HealChance.Difficulty(145, true)); // the capture: "difficulty 319 in combat stance"
        Assert.Equal(290, HealChance.Difficulty(145, false));
        Assert.Equal(0, HealChance.Difficulty(0, true));
    }

    [Fact]
    public void The_capture_pins_the_chance_145_missing_in_combat_about_a_sixth_and_72_missing_about_96_percent()
    {
        var low = HealChance.Estimate(190, 50, false, 145, true);
        var high = HealChance.Estimate(190, 50, false, 72, true);

        Assert.InRange(low, 0.15, 0.18); // "about 17%": 5 of 6 uses failed at 43 of 188 health
        Assert.InRange(high, 0.95, 0.97); // "every use won" from 116 of 188 up
        Assert.Equal(1.0 / (1.0 + Math.Exp(-0.03 * (264 - 319))), low, 12);
    }

    [Fact]
    public void The_chance_is_one_half_when_the_skill_meets_the_difficulty_and_falls_as_the_missing_health_grows()
    {
        Assert.Equal(0.5, HealChance.Chance(300, 300), 12);
        var last = 1.1;
        for (var missing = 0; missing <= 188; missing += 4)
        {
            var c = HealChance.Estimate(190, 50, false, missing, true);
            Assert.True(c < last, $"missing {missing}");
            last = c;
        }

        Assert.True(HealChance.Estimate(190, 50, false, 100, false) > HealChance.Estimate(190, 50, false, 100, true), "the non-combat difficulty is lower");
        Assert.True(HealChance.Estimate(190, 50, true, 100, true) > HealChance.Estimate(190, 50, false, 100, true), "a specialised skill is stronger");
    }

    // ---------------------------------------------------------------------------------------------- none

    [Theory]
    [InlineData(100u)]
    public void With_the_recovery_off_nothing_is_ever_decided(uint health)
    {
        var off = new RecoveryOptions { Enabled = false };

        Assert.Equal(RecoveryDecision.None, RecoveryRules.Decide(In(health), off));
        Assert.Equal(RecoveryDecision.None, RecoveryRules.Decide(In(health, kit: 0, potions: 0), off));
    }

    [Fact]
    public void A_full_vital_and_an_unknown_maximum_decide_nothing_whatever_the_options()
    {
        var always = new RecoveryOptions { BelowPercent = 100, UntilPercent = 100, Chance = 0 };

        Assert.Equal(RecoveryDecision.None, RecoveryRules.Decide(In(Max), always)); // never a potion or a kit at a full vital (capture C3)
        Assert.Equal(RecoveryDecision.None, RecoveryRules.Decide(In(Max, active: true), always));
        Assert.Equal(RecoveryDecision.None, RecoveryRules.Decide(In(0, max: 0), always));
    }

    [Fact]
    public void The_trigger_is_the_share_under_heal_below_141_of_188_is_exactly_75_percent_and_holds()
    {
        Assert.Equal(RecoveryDecision.None, RecoveryRules.Decide(In(141), Defaults));
        Assert.Equal(RecoveryDecision.Kit, RecoveryRules.Decide(In(140), Defaults)); // 74.5%, 48 missing: the chance is 0.99
        Assert.Equal(RecoveryDecision.None, RecoveryRules.Decide(In(188), Defaults));
    }

    [Fact]
    public void A_started_recovery_goes_on_to_heal_until_and_stops_there_169_is_under_90_percent_and_170_is_not()
    {
        Assert.Equal(RecoveryDecision.None, RecoveryRules.Decide(In(150), Defaults)); // 79.8%: not started
        Assert.Equal(RecoveryDecision.Kit, RecoveryRules.Decide(In(150, active: true), Defaults)); // started: heals on to 90%
        Assert.Equal(RecoveryDecision.Kit, RecoveryRules.Decide(In(169, active: true), Defaults)); // 89.9%
        Assert.Equal(RecoveryDecision.None, RecoveryRules.Decide(In(170, active: true), Defaults)); // 90.4%
    }

    [Fact]
    public void A_started_recovery_whose_items_ran_out_over_the_trigger_is_over_not_a_wait()
    {
        Assert.Equal(RecoveryDecision.None, RecoveryRules.Decide(In(150, kit: 0, potions: 0, active: true), Defaults)); // 79.8% >= 75%: nothing helps and nothing is needed
        Assert.Equal(RecoveryDecision.Wait, RecoveryRules.Decide(In(130, kit: 0, potions: 0, active: true), Defaults)); // 69%: under the trigger
    }

    // ---------------------------------------------------------------------------------------------- kit

    [Fact]
    public void The_kit_is_used_while_its_estimated_chance_holds_and_the_chance_comes_out_with_the_decision()
    {
        var decision = RecoveryRules.Decide(In(100), Defaults, out var chance); // 88 missing: 0.93

        Assert.Equal(RecoveryDecision.Kit, decision);
        Assert.Equal(HealChance.Estimate(190, 50, false, 88, true), chance, 12);
    }

    [Fact]
    public void The_chance_boundary_at_0_7_is_81_of_188_health_a_kit_there_and_a_potion_one_point_lower()
    {
        Assert.True(HealChance.Estimate(190, 50, false, 107, true) >= 0.7); // health 81: 107 missing
        Assert.True(HealChance.Estimate(190, 50, false, 108, true) < 0.7); // health 80

        Assert.Equal(RecoveryDecision.Kit, RecoveryRules.Decide(In(81), Defaults));
        Assert.Equal(RecoveryDecision.Potion, RecoveryRules.Decide(In(80), Defaults));
    }

    [Fact]
    public void A_lower_heal_chance_option_lets_the_kit_work_at_a_lower_health_and_a_higher_one_stops_it_sooner()
    {
        Assert.Equal(RecoveryDecision.Kit, RecoveryRules.Decide(In(80), new RecoveryOptions { Chance = 0.5 }));
        Assert.Equal(RecoveryDecision.Kit, RecoveryRules.Decide(In(60), new RecoveryOptions { Chance = 0 }));
        Assert.Equal(RecoveryDecision.Potion, RecoveryRules.Decide(In(130), new RecoveryOptions { Chance = 0.999 })); // 58 missing: 0.99 < 0.999
    }

    [Fact]
    public void At_43_of_188_the_chance_is_a_sixth_so_a_potion_is_drunk_not_the_kit_as_the_capture_wants()
    {
        var decision = RecoveryRules.Decide(In(43), Defaults, out var chance);

        Assert.Equal(RecoveryDecision.Potion, decision);
        Assert.InRange(chance, 0.15, 0.18);
    }

    [Fact]
    public void Two_lost_rolls_in_a_row_set_the_kit_aside_for_that_health_and_a_potion_follows()
    {
        Assert.Equal(RecoveryDecision.Kit, RecoveryRules.Decide(In(100, lost: 1), Defaults));
        Assert.Equal(RecoveryDecision.Potion, RecoveryRules.Decide(In(100, lost: RecoveryRules.MaxLostRolls), Defaults)); // a lost roll changes nothing: the same chance would lose again
        Assert.Equal(RecoveryDecision.Wait, RecoveryRules.Decide(In(100, potions: 0, lost: 2), Defaults)); // no potion: the regeneration wait, then the kit is weighed again when the health moves
    }

    [Fact]
    public void A_kit_out_of_uses_is_a_potion_and_the_decision_has_no_chance()
    {
        var decision = RecoveryRules.Decide(In(100, kit: 0), Defaults, out var chance);

        Assert.Equal(RecoveryDecision.Potion, decision);
        Assert.True(double.IsNaN(chance), "the kit was not weighed");
    }

    // ---------------------------------------------------------------------------------------------- the last-resort kit (M4 3.1 fix, review R1)

    [Fact]
    public void With_no_potion_a_kit_under_the_wanted_chance_is_the_last_resort_before_the_wait_at_75_of_188_in_combat_3_1_fix_R1()
    {
        // The review's scenario: the hunter's potions are spent and a kill ended at 40% (75 of 188, 113 missing, difficulty 249 in melee mode, effective 264): the chance is about 0.61, under the wanted 0.7.
        var decision = RecoveryRules.Decide(In(75, kit: 20, potions: 0), Defaults, out var chance);

        Assert.Equal(RecoveryDecision.KitLastResort, decision);
        Assert.InRange(chance, 0.60, 0.62);
        Assert.Equal(HealChance.Estimate(190, 50, false, 113, true), chance, 12);
        Assert.Equal(249, HealChance.Difficulty(113, true));
        Assert.True(chance < Defaults.Chance && chance >= Defaults.FloorChance);
        Assert.Equal(0.3, RecoveryRules.KitFloorChance);
        Assert.Equal(RecoveryRules.KitFloorChance, Defaults.FloorChance);
    }

    [Fact]
    public void A_chance_about_a_fifth_is_under_the_floor_so_the_recovery_still_waits_or_fails_3_1_fix_R1()
    {
        // 25% health (47 of 188, 141 missing): the chance is about 0.2, and the share is under the retreat share, so nothing can heal: Fail at once, as before.
        var low = RecoveryRules.Decide(In(47, kit: 20, potions: 0), Defaults, out var lowChance);
        Assert.Equal(RecoveryDecision.Fail, low);
        Assert.InRange(lowChance, 0.19, 0.22);

        // A weaker healer at 40% health (Healing 135: effective 204 against 249, about 0.2) is over the retreat share: the wait stays.
        var weak = RecoveryRules.Decide(In(75, kit: 20, potions: 0, skill: 135), Defaults, out var weakChance);
        Assert.Equal(RecoveryDecision.Wait, weak);
        Assert.InRange(weakChance, 0.19, 0.22);
        Assert.Equal(RecoveryDecision.Wait, RecoveryRules.Decide(In(75, kit: 20, potions: 0, skill: 135, waited: 299.9), Defaults));
        Assert.Equal(RecoveryDecision.Fail, RecoveryRules.Decide(In(75, kit: 20, potions: 0, skill: 135, waited: RecoveryRules.RegenSeconds), Defaults));
    }

    [Fact]
    public void The_floor_option_moves_the_last_resort_and_a_potion_in_the_pack_always_comes_first_3_1_fix_R1()
    {
        Assert.Equal(RecoveryDecision.KitLastResort, RecoveryRules.Decide(In(75, kit: 20, potions: 0), new RecoveryOptions { FloorChance = 0.6 })); // 0.61
        Assert.Equal(RecoveryDecision.Wait, RecoveryRules.Decide(In(75, kit: 20, potions: 0), new RecoveryOptions { FloorChance = 0.65 }));
        Assert.Equal(RecoveryDecision.KitLastResort, RecoveryRules.Decide(In(47, kit: 20, potions: 0), new RecoveryOptions { FloorChance = 0 })); // a floor of 0 uses the kit at any chance, even under the retreat share
        Assert.Equal(RecoveryDecision.Fail, RecoveryRules.Decide(In(47, kit: 20, potions: 0), new RecoveryOptions { FloorChance = 0.25 }));

        Assert.Equal(RecoveryDecision.Potion, RecoveryRules.Decide(In(75, kit: 20, potions: 5), Defaults)); // a potion in the pack: no last resort
        Assert.Equal(RecoveryDecision.Kit, RecoveryRules.Decide(In(100, kit: 20, potions: 0), Defaults)); // 0.93: the wanted chance is met, not a last resort
        Assert.Equal(RecoveryDecision.Wait, RecoveryRules.Decide(In(75, kit: 0, potions: 0), Defaults)); // no kit uses: nothing to be a last resort
    }

    [Fact]
    public void A_kit_set_aside_by_two_lost_rolls_is_not_the_last_resort_either_until_the_health_moves_3_1_fix_R1()
    {
        Assert.Equal(RecoveryDecision.KitLastResort, RecoveryRules.Decide(In(75, kit: 20, potions: 0, lost: 1), Defaults));
        Assert.Equal(RecoveryDecision.Wait, RecoveryRules.Decide(In(75, kit: 20, potions: 0, lost: RecoveryRules.MaxLostRolls), Defaults));
    }

    // ---------------------------------------------------------------------------------------------- potion

    [Fact]
    public void A_potion_needs_at_least_15_points_missing_so_none_is_drunk_for_nothing()
    {
        var below100 = new RecoveryOptions { BelowPercent = 100, UntilPercent = 100 };

        Assert.Equal(RecoveryDecision.Wait, RecoveryRules.Decide(In(174, kit: 0), below100)); // 14 missing: a potion would be spent for 14
        Assert.Equal(RecoveryDecision.Potion, RecoveryRules.Decide(In(173, kit: 0), below100)); // 15 missing
        Assert.Equal(RecoveryDecision.None, RecoveryRules.Decide(In(188, kit: 0), below100));
        Assert.Equal(15, RecoveryRules.PotionMinMissing);
        Assert.Equal(25, RecoveryRules.PotionPoints);
    }

    [Fact]
    public void A_potion_is_the_answer_whenever_the_kit_cannot_be_and_potions_are_held_even_at_low_health()
    {
        Assert.Equal(RecoveryDecision.Potion, RecoveryRules.Decide(In(20, kit: 0), Defaults)); // 10%: under the retreat share but a potion helps
        Assert.Equal(RecoveryDecision.Potion, RecoveryRules.Decide(In(20), Defaults)); // the kit's chance at 168 missing is about 0.04
    }

    // ---------------------------------------------------------------------------------------------- wait and fail

    [Fact]
    public void With_nothing_to_heal_with_the_recovery_waits_for_regeneration_up_to_300_seconds_then_fails_loudly()
    {
        Assert.Equal(RecoveryDecision.Wait, RecoveryRules.Decide(In(100, kit: 0, potions: 0), Defaults));
        Assert.Equal(RecoveryDecision.Wait, RecoveryRules.Decide(In(100, kit: 0, potions: 0, waited: 299.9), Defaults));
        Assert.Equal(RecoveryDecision.Fail, RecoveryRules.Decide(In(100, kit: 0, potions: 0, waited: RecoveryRules.RegenSeconds), Defaults));
        Assert.Equal(300.0, RecoveryRules.RegenSeconds);
    }

    [Fact]
    public void Under_the_retreat_share_with_nothing_that_can_heal_the_recovery_fails_at_once_not_after_a_wait()
    {
        Assert.Equal(RecoveryDecision.Fail, RecoveryRules.Decide(In(56, kit: 0, potions: 0), Defaults)); // 29.8%
        Assert.Equal(RecoveryDecision.Wait, RecoveryRules.Decide(In(57, kit: 0, potions: 0), Defaults)); // 30.3%
        Assert.Equal(RecoveryDecision.Fail, RecoveryRules.Decide(In(30, kit: 20, potions: 0), Defaults)); // a kit at 16% health weighs about 0.07: nothing can heal
    }

    // ---------------------------------------------------------------------------------------------- the rule allocates nothing

    [Fact]
    [Trait("Tier", "Timing")] // m6-alloc-flake-recoveryrules: a zero-byte allocation count; the untagged copy of the shared name (FightPickerTests and ManaOrderTests are already tiered)
    public void A_decision_allocates_nothing()
    {
        var inputs = new[] { In(43), In(100), In(100, kit: 0), In(100, kit: 0, potions: 0), In(188), In(150, active: true), In(75, potions: 0), In(47, potions: 0) };
        var sink = RecoveryDecision.None;
        AllocationAssert.Nothing(() =>
        {
            foreach (var input in inputs)
            {
                sink = RecoveryRules.Decide(in input, Defaults, out _);
            }
        });
        Assert.True(Enum.IsDefined(sink));
    }

    // ---------------------------------------------------------------------------------------------- the Healing skill from the read model

    private static PlayerQualities Hunter(uint skillClass, bool listed = true)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Attributes[FighterTemplate.Coordination] = new AttributeDesc(FighterTemplate.Coordination, 0, 100, 0);
        q.Attributes[FighterTemplate.Focus] = new AttributeDesc(FighterTemplate.Focus, 0, 100, 0);
        if (listed)
        {
            q.Skills[FighterTemplate.Healing] = new SkillDesc(FighterTemplate.Healing, 97, 1, skillClass, 0, 0, 0, 0.0);
        }

        return q;
    }

    [Fact]
    public void The_healing_skill_is_the_formula_over_focus_and_coordination_plus_the_ranks()
    {
        var state = RecoveryRules.ReadHealingSkill(Hunter(SkillRules.TrainedClass), out var value, out var specialised);

        Assert.Equal(HealingSkillState.Read, state);
        Assert.Equal(67 + 97, value); // (100 + 100) / 3 rounded half up, plus 97 ranks
        Assert.False(specialised);
        Assert.Equal(HealingSkillState.Read, RecoveryRules.ReadHealingSkill(Hunter(SkillRules.SpecializedClass), out _, out var spec));
        Assert.True(spec);
        Assert.Equal(21u, RecoveryRules.HealingSkillId);
    }

    [Fact]
    public void An_untrained_or_unlisted_skill_is_named_so_the_driver_can_set_the_kit_aside_or_fall_back_loudly()
    {
        Assert.Equal(HealingSkillState.Untrained, RecoveryRules.ReadHealingSkill(Hunter(SkillRules.UntrainedClass), out var untrained, out _));
        Assert.Equal(0, untrained);
        Assert.Equal(HealingSkillState.Unknown, RecoveryRules.ReadHealingSkill(Hunter(SkillRules.TrainedClass, listed: false), out _, out _));
        Assert.Equal(HealingSkillState.Unknown, RecoveryRules.ReadHealingSkill(new PlayerQualities(), out _, out _));
        var noFocus = Hunter(SkillRules.TrainedClass);
        noFocus.Attributes.Remove(FighterTemplate.Focus);
        Assert.Equal(HealingSkillState.Unknown, RecoveryRules.ReadHealingSkill(noFocus, out _, out _));
        Assert.Equal(264, HealChance.Effective(RecoveryRules.FallbackHealingSkill, RecoveryRules.HandyKitBoost, false));
    }

    // ---------------------------------------------------------------------------------------------- the options

    private static RecoveryOptions Parse(params string[] args) => RecoveryOptions.FromArguments(CommandLineArgument.ParseAll(args));

    [Fact]
    public void The_defaults_are_on_75_90_and_0_7()
    {
        var o = Parse();

        Assert.Empty(o.Errors);
        Assert.Equal((true, 75, 90, 0.7, 0.3), (o.Enabled, o.BelowPercent, o.UntilPercent, o.Chance, o.FloorChance));
        Assert.Equal(0.75, o.Below, 12);
        Assert.Equal(0.9, o.Until, 12);
        Assert.Equal("heal=on heal_below=75% heal_until=90% heal_chance=0.7 heal_floor_chance=0.3", o.ToString());
    }

    [Fact]
    public void The_five_options_parse_and_the_last_one_given_wins()
    {
        var o = Parse("--heal=off", "--heal-below=60", "--heal-until=80", "--heal-chance=0.5", "--heal-floor-chance=0.1", "--heal-below=65");

        Assert.Empty(o.Errors);
        Assert.Equal((false, 65, 80, 0.5, 0.1), (o.Enabled, o.BelowPercent, o.UntilPercent, o.Chance, o.FloorChance));
        Assert.Equal(0.45, Parse("--heal-floor-chance=0.4", "--heal-floor-chance=0.45").FloorChance, 12);
        Assert.Equal(0.0, Parse("--heal-floor-chance=0").FloorChance);
        Assert.Equal(1.0, Parse("--heal-floor-chance=1").FloorChance);
        Assert.True(Parse("--heal").Enabled);
        Assert.True(Parse("--heal=on").Enabled);
    }

    [Theory]
    [InlineData("--heal=maybe")]
    [InlineData("--heal-below=0")]
    [InlineData("--heal-below=101")]
    [InlineData("--heal-below=x")]
    [InlineData("--heal-until=101")]
    [InlineData("--heal-chance=1.5")]
    [InlineData("--heal-chance=-0.1")]
    [InlineData("--heal-chance=often")]
    [InlineData("--heal-floor-chance=1.5")]
    [InlineData("--heal-floor-chance=NaN")]
    public void A_bad_value_is_an_error_line_and_the_default_stays(string argument)
    {
        var o = Parse(argument);

        var error = Assert.Single(o.Errors);
        Assert.StartsWith(argument[..argument.IndexOf('=', StringComparison.Ordinal)] + ":", error, StringComparison.Ordinal);
        Assert.Equal(RecoveryOptions.Default.BelowPercent, o.BelowPercent);
        Assert.Equal(RecoveryOptions.Default.UntilPercent, o.UntilPercent);
        Assert.Equal(RecoveryOptions.Default.Chance, o.Chance);
        Assert.Equal(RecoveryOptions.Default.FloorChance, o.FloorChance);
        Assert.True(o.Enabled);
    }

    [Fact]
    public void A_recovery_that_stops_under_the_share_it_starts_at_is_an_error()
    {
        var o = Parse("--heal-below=95");

        var error = Assert.Single(o.Errors);
        Assert.StartsWith("--heal-until:", error, StringComparison.Ordinal);
        Assert.Empty(Parse("--heal-below=85", "--heal-until=85").Errors);
    }

    [Fact]
    public void The_five_options_are_registered_for_the_main_scene_and_the_headless_host()
    {
        foreach (var name in new[] { "--heal", "--heal-below", "--heal-until", "--heal-chance", "--heal-floor-chance" })
        {
            Assert.True(OptionCatalog.Registry.IsKnown(name), name);
        }

        var arguments = CommandLineArgument.ParseAll(["--heal=off", "--heal-below=70", "--heal-until=95", "--heal-chance=0.6", "--heal-floor-chance=0.2"]);
        Assert.Empty(OptionCatalog.Registry.Problems(arguments, OptionScenes.Main));
        Assert.Empty(OptionCatalog.Registry.Problems(arguments, OptionScenes.Headless));
    }

    [Fact]
    public void The_hunts_options_carry_the_recovery_options_and_name_them()
    {
        var o = HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-kills=3", "--heal-below=60", "--heal-until=70", "--heal-chance=0.8", "--heal-floor-chance=0.4", "--loot=off"]));

        Assert.Empty(o.Errors);
        Assert.Equal((60, 70, 0.8, 0.4), (o.Recovery.BelowPercent, o.Recovery.UntilPercent, o.Recovery.Chance, o.Recovery.FloorChance));
        Assert.False(o.Loot.Enabled);
        Assert.Equal(3, o.Kills);
        Assert.Contains("heal=on heal_below=60% heal_until=70% heal_chance=0.8", o.ToString(), StringComparison.Ordinal);
        Assert.Same(RecoveryOptions.Default, new HuntOptions().Recovery);
    }

    [Fact]
    public void A_bad_heal_option_or_a_crossed_pair_fails_the_hunts_parse_never_a_silent_default()
    {
        Assert.Single(HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--heal-below=abc"])).Errors);
        Assert.Single(HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--heal-floor-chance=2"])).Errors); // 3.1 fix R1
        var crossed = HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--heal-below=95"]));
        Assert.Single(crossed.Errors);
        Assert.StartsWith("--heal-until:", crossed.Errors[0], StringComparison.Ordinal);
    }
}

/// <summary>M4 3.1 s3: the Healing skill's formula the rule reads without a SkillTable is the dats' (Focus + Coordination) / 3.</summary>
public sealed class RecoveryRulesDatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void Healing_is_skill_21_with_the_formula_focus_plus_coordination_over_3_that_the_rule_assumes()
    {
        var skills = SkillTableData.Load(fixture.Dats);

        var healing = skills.Skills.Single(s => s.Id == RecoveryRules.HealingSkillId);
        Assert.Equal("Healing", healing.Name);
        Assert.Equal(3, healing.Formula.Divisor);
        Assert.Equal(1, healing.Formula.Attribute1Multiplier);
        Assert.Equal(1, healing.Formula.Attribute2Multiplier);
        Assert.Equal(0, healing.Formula.AdditiveBonus);
        Assert.Equal(new[] { FighterTemplate.Focus, FighterTemplate.Coordination }.Order(), new[] { healing.Formula.Attribute1, healing.Formula.Attribute2 }.Order());
    }
}

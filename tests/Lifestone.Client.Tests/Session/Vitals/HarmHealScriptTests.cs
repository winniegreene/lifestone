using System.Text.RegularExpressions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// The harm-heal script on the scripted rig (M4 P5a lane C3, <see cref="C3HarmRig"/>): the order of a fighter's leg (swap in once, harm, kit, harm, potion, keeper, swap out once), a fizzle counted and retried, the cap that ends
/// <c>why=harm-cap</c>, a lost kit roll counted, the mage's path (Harm Self then Heal Self VII, no swap) and the rule that no cast ever reaches 0. Every number is the rig's: maxima 200, a kit heals 60, a potion 25, Heal Self 50.
/// </summary>
public sealed partial class HarmHealScriptTests
{
    private static readonly uint[] HarmAndHeal = [.. VitalsMeans.HarmSelf, .. VitalsMeans.HealSelf, .. VitalsMeans.StaminaToManaSelf]; // the fighters and the mage learn Stamina to Mana Self with the profile: the harm phase tops its mana up with it (P5a-R7)

    [GeneratedRegex(@"^harm-heal done ok=(true|false)( why=\S+)? heals=\d+ kits=\d+ potions=\d+ harms=\d+ fizzles=\d+ health_min_pct=(\d+\.\d|n/a) swapped=(yes|no) kit_lost=\d+ busy=\d+ health_end_pct=(\d+\.\d|n/a) s=\d+\.\d kit_points=\d+ potion_points=\d+ potion_stack=(\d+->\d+|n/a) transfers=\d+ resist=\d+ checks=(ok|\d+)$")]
    private static partial Regex VerdictShape();

    private static C2Character Fighter(uint health = 200) => new()
    {
        Caster = false,
        CasterInPack = true,
        KitUses = 4,
        Potions = 5,
        Health = health,
        LifeMagicRanks = 600,
        Spells = HarmAndHeal,
    };

    private static C2Character Mage() => new() { HealingClass = 1, LifeMagicRanks = 600, Spells = HarmAndHeal };

    private static int First(List<string> timeline, Func<string, bool> match, int from = 0)
    {
        for (var i = from; i < timeline.Count; i++)
        {
            if (match(timeline[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsHarm(string step) => step.StartsWith("cast:", StringComparison.Ordinal) && HarmSelfPlan.For(uint.Parse(step[5..], System.Globalization.CultureInfo.InvariantCulture)) is not null;

    [Fact]
    public void With_round_4s_numbers_the_keeper_restores_the_mana_before_round_2_which_reaches_half_and_the_potion_step_runs_P5a_r4()
    {
        // Round 4's Brawler: 231 health, 287 mana; round 1 spent about 250 (Harm Self V costs about 60 live), round 2 began at 37 and ran into the round cap at 54% on Harm Self I against a rise of 1 to 3 a harm.
        var character = Fighter(231) with { MaxHealth = 231, MaxMana = 287, Mana = 287, MaxStamina = 300, Stamina = 300, Spells = [.. VitalsMeans.HarmSelf.Take(5), .. VitalsMeans.HealSelf, .. VitalsMeans.StaminaToManaSelf] };
        var rig = new C3HarmRig(character, FighterTemplate.Melee);
        uint[] liveCosts = [12, 24, 36, 50, 60];
        for (var i = 0; i < liveCosts.Length; i++)
        {
            rig.Inner.Table[VitalsMeans.HarmSelf[i]] = rig.Inner.Table[VitalsMeans.HarmSelf[i]] with { BaseMana = liveCosts[i] };
        }

        rig.Inner.HealthRegen = 0.6; // 0.3 a second in the magic mode: about 3 between harms
        rig.Inner.ManaRegen = 0.5;

        var state = rig.RunToEnd();
        var lines = rig.R.H.Log.Lines.Where(l => l.Contains("harm-heal", StringComparison.Ordinal)).ToList();
        var trace = string.Join(" | ", lines);
        Assert.True(state == HarmHealState.Done, "why=" + rig.Script.Why + " " + trace);

        var rest = lines.FindIndex(l => l.Contains("harm round 2 needs ", StringComparison.Ordinal) && l.Contains("the vitals keeper restores it first", StringComparison.Ordinal));
        var restored = lines.FindIndex(l => l.Contains("the mana for harm round 2 is restored", StringComparison.Ordinal));
        var done2 = lines.FindIndex(l => l.Contains("harm round 2 done at ", StringComparison.Ordinal));
        Assert.True(rest >= 0 && restored > rest && done2 > restored, trace);
        var pct = double.Parse(System.Text.RegularExpressions.Regex.Match(lines[done2], @"\((\d+)%\)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(pct, 40, 50); // round 2 reaches the target (50%), not the cap
        Assert.Equal(1, rig.Script.OwnPotions); // the potion step ran after it
        Assert.True(rig.Script.ManaRests >= 1);
        Assert.True(rig.Inner.LowestHealth > HarmSelfPlan.MarginPoints);
        var t = rig.Timeline;
        var kit = t.IndexOf("kit");
        Assert.True(kit >= 0 && kit < t.IndexOf("potion"), string.Join(' ', t));
    }

    [Fact]
    public void With_round_5s_Archer_numbers_and_the_potions_the_leg_reaches_its_harm_rounds_with_no_transfer_and_no_actions_cap_M4_P5a_C7()
    {
        // Round 5's Archer: 189 health, stamina 174 of 238, mana 135 of 291, Harm Self V's need 250. With the transfers alone it hit the keeper's 40-action cap after 16 transfers and 12 Revitalizes (mana 60 of 291) and
        // failed precondition:mana (actions-cap): the three Revitalizes between two transfers cost 130 mana (199 to 69 in the log). With the kit's Mana Potions the keeper drinks 5 to 250 and the leg harms.
        var character = Fighter(189) with { MaxHealth = 189, MaxMana = 291, Mana = 135, MaxStamina = 238, Stamina = 174, ManaPotions = FighterKit.ArcherManaPotionCount, StaminaPotions = FighterKit.ArcherStaminaPotionCount, Spells = [.. VitalsMeans.HarmSelf.Take(5), .. VitalsMeans.HealSelf, .. VitalsMeans.StaminaToManaSelf, .. VitalsMeans.RevitalizeSelf] };
        var rig = new C3HarmRig(character, FighterTemplate.Archer, vitals: VitalsOptions.Default.ForFighter());
        uint[] liveCosts = [12, 24, 36, 50, 60];
        for (var i = 0; i < liveCosts.Length; i++)
        {
            rig.Inner.Table[VitalsMeans.HarmSelf[i]] = rig.Inner.Table[VitalsMeans.HarmSelf[i]] with { BaseMana = liveCosts[i] };
        }

        rig.Inner.HealthRegen = 0.6;
        rig.Inner.ManaRegen = 0.2; // 1 mana a 5 s tick
        var state = rig.RunToEnd();
        var trace = string.Join(" | ", rig.R.H.Log.Lines.Where(l => l.Contains("harm-heal", StringComparison.Ordinal) || l.Contains("[vitals]", StringComparison.Ordinal)).Take(40));
        Assert.True(state == HarmHealState.Done, "why=" + rig.Script.Why + " " + trace);
        Assert.Equal(0, rig.Inner.Keeper.Transfers);
        Assert.Equal(0, rig.Inner.Keeper.Revitalizes);
        Assert.True(rig.Inner.Keeper.ManaPotions >= 5, trace);
        Assert.Contains(rig.Timeline, IsHarm);
        Assert.DoesNotContain("actions-cap", rig.Script.Why ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mana_rest_that_cannot_reach_the_rounds_need_fails_the_leg_as_precondition_mana_P5a_r4()
    {
        // No mana regeneration and no stamina to transfer: the keeper's rest cap ends it by name.
        var character = Fighter() with { Mana = 20, Stamina = 0 };
        var rig = new C3HarmRig(character, FighterTemplate.Melee, vitals: VitalsOptions.Default with { RestCapSeconds = 60 });
        rig.Inner.ManaRegen = 0;
        rig.Inner.StaminaRegen = 0;

        Assert.Equal(HarmHealState.Failed, rig.RunToEnd());
        Assert.StartsWith("precondition:mana (", rig.Script.Why, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Timeline, IsHarm);
    }

    [Theory]
    [InlineData("Brawler", false)]
    [InlineData("Brawler", true)]
    [InlineData("Archer", false)]
    public void A_fighters_leg_swaps_in_once_harms_kits_harms_potions_restores_and_swaps_out_once(string name, bool worstCase)
    {
        var template = name == "Brawler" ? FighterTemplate.Melee : FighterTemplate.Archer;
        var rig = new C3HarmRig(Fighter(), template);
        rig.Inner.HarmWorstCase = worstCase;

        Assert.Equal(HarmHealState.Done, rig.RunToEnd());

        var t = rig.Timeline;
        var wieldCaster = t.IndexOf("wield:" + GearKit.Wand.Name);
        var unwieldCaster = t.IndexOf("unwield:" + GearKit.Wand.Name);
        var firstHarm = First(t, IsHarm);
        var kit = t.IndexOf("kit");
        var secondHarm = First(t, IsHarm, kit);
        var potion = t.IndexOf("potion");
        Assert.True(wieldCaster >= 0 && wieldCaster < firstHarm, "the caster is wielded before the first harm: " + string.Join(' ', t));
        Assert.True(firstHarm < kit && kit < secondHarm && secondHarm < potion, "harm, kit, harm, potion: " + string.Join(' ', t));
        Assert.True(potion < unwieldCaster, "the caster comes out after the potion and the keeper's pass: " + string.Join(' ', t));
        Assert.Equal(1, t.Count(s => s == "wield:" + GearKit.Wand.Name)); // one swap in for every cast, none per cast
        Assert.Equal(1, t.Count(s => s == "unwield:" + GearKit.Wand.Name)); // and one out
        Assert.Equal(0, t.Count(s => s.StartsWith("wield-refused", StringComparison.Ordinal)));

        // The weapon is back in hand and the character is in peace; the caster is in the pack.
        Assert.NotEqual(0u, rig.LocationOf(C3HarmRig.WeaponGuid));
        Assert.Equal(0u, rig.LocationOf(C2VitalsRig.WandGuid));
        Assert.Equal(CombatModes.NonCombat, rig.R.H.State.Self.Qualities.CombatMode);

        var script = rig.Script;
        Assert.True(script.Swapped);
        Assert.True(script.Harms >= 2, "harms " + script.Harms);
        Assert.True(script.OwnKits >= 1 && script.OwnPotions == 1);
        Assert.True(script.Heals >= 2);
        Assert.True(rig.Health >= 180, "the keeper restores the health to its until share: " + rig.Health);
        Assert.True(rig.Inner.LowestHealth > HarmSelfPlan.MarginPoints, "no cast came near 0: " + rig.Inner.LowestHealth);
        Assert.True(script.HealthMinPercent <= 55, "the health went to about half: " + script.HealthMinPercent);
        Assert.Matches(VerdictShape(), script.VerdictLine());
        Assert.Contains("ok=true", script.VerdictLine(), StringComparison.Ordinal);
        Assert.Contains("swapped=yes", script.VerdictLine(), StringComparison.Ordinal);

        // P5a-R6 (D28): the heal amounts and the stack counts the old Brawler fact checked live are checked in the leg itself.
        Assert.Empty(script.Anomalies);
        Assert.Contains("checks=ok", script.VerdictLine(), StringComparison.Ordinal);
        Assert.True(script.KitPoints > 0 && script.KitPoints <= 200, "the kit's Healed amounts are summed: " + script.KitPoints);
        Assert.Equal(25, script.PotionPoints); // the potion's Drank amount: 1 to 25
        Assert.Equal((5, 4), (script.PotionStackBefore, script.PotionStackAfter)); // the stack went down by one
        Assert.Contains("potion_stack=5->4", script.VerdictLine(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_potion_stack_that_goes_down_by_two_is_recorded_as_a_broken_check_P5a_R6()
    {
        var rig = new C3HarmRig(Fighter(), FighterTemplate.Melee);
        rig.Inner.PotionsPerDrink = 2;

        var state = rig.RunToEnd();
        Assert.True(state == HarmHealState.Done, "why=" + rig.Script.Why + " " + string.Join(" ", rig.Timeline));

        Assert.Contains(rig.Script.Anomalies, a => a.Contains("potion stack went 5 -> 3, not down by one", StringComparison.Ordinal));
        Assert.DoesNotContain("checks=ok", rig.Script.VerdictLine(), StringComparison.Ordinal);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("check broke: potion stack", StringComparison.Ordinal));
    }

    [Fact]
    public void A_mana_that_pays_for_no_wanted_level_is_topped_up_with_Stamina_to_Mana_not_waited_out_P5a_R7()
    {
        // Mana 45 pays for Harm Self I-IV (10 a level in the rig) but the level the plan wants at full health is VII (70): one transfer or more first. The rig's mana regenerates 3 a second here (not ACE's 0.2): the subject is the order, and a
        // second round's low-mana tail (a transfer of level I costs more than it gives, so none is cast) would otherwise end at the cap.
        var rig = new C3HarmRig(Fighter() with { Mana = 45 }, FighterTemplate.Melee);
        rig.Inner.ManaRegen = 3;

        var state = rig.RunToEnd();
        Assert.True(state == HarmHealState.Done, "why=" + rig.Script.Why + " " + string.Join(" ", rig.Timeline) + " | " + string.Join(" | ", rig.R.H.Log.Lines.Where(l => l.Contains("harm-heal", StringComparison.Ordinal))));

        var t = rig.Timeline;
        var firstTransfer = First(t, s => s.StartsWith("cast:", StringComparison.Ordinal) && VitalsMeans.StaminaToManaSelf.Contains(uint.Parse(s[5..], System.Globalization.CultureInfo.InvariantCulture)));
        var firstHarm = First(t, IsHarm);
        Assert.True(firstTransfer >= 0 && firstTransfer < firstHarm, "a transfer before the first harm: " + string.Join(' ', t));
        Assert.True(rig.Script.Transfers >= 1);
        Assert.Null(rig.Script.Why); // Done: the harm phase never waited out its 60 s cap
        Assert.Contains("transfers=", rig.Script.VerdictLine(), StringComparison.Ordinal);
        Assert.Equal(1, t.Count(s => s == "wield:" + GearKit.Wand.Name)); // the transfers ride the same swap
    }

    [Fact]
    public void With_no_Stamina_to_Mana_known_the_harm_phase_casts_what_the_mana_pays_and_says_why_once_P5a_R7()
    {
        var rig = new C3HarmRig(Fighter() with { Mana = 45, Spells = [.. VitalsMeans.HarmSelf, .. VitalsMeans.HealSelf] }, FighterTemplate.Melee);

        var state = rig.RunToEnd();

        Assert.Equal(0, rig.Script.Transfers);
        Assert.DoesNotContain(rig.Inner.Casts, c => VitalsMeans.StaminaToManaSelf.Contains(c.Spell));
        Assert.True(rig.Script.Harms >= 1, "the levels the mana pays were cast: " + state);
        Assert.Equal(1, rig.R.H.Log.Lines.Count(l => l.Contains("no Stamina to Mana Self can be cast", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_harm_that_lands_past_its_table_maximum_logs_TODO_m4_harm_heal_resist_once_and_is_counted_P5a_R11()
    {
        var rig = new C3HarmRig(Fighter(), FighterTemplate.Melee);
        rig.Inner.HarmScale = 1.5; // a drain resistance of 1.5
        rig.Inner.HarmWorstCase = true;

        Assert.Equal(HarmHealState.Done, rig.RunToEnd());

        Assert.True(rig.Script.ResistExceeded >= 2, "counted every time: " + rig.Script.ResistExceeded);
        Assert.Equal(1, rig.R.H.Log.Lines.Count(l => l.Contains("TODO(m4-harm-heal-resist)", StringComparison.Ordinal))); // logged once per leg
        Assert.Contains($"resist={rig.Script.ResistExceeded}", rig.Script.VerdictLine(), StringComparison.Ordinal);
        Assert.True(rig.Inner.LowestHealth > 0);
    }

    [Fact]
    public void A_harm_inside_its_table_range_logs_no_resist_TODO()
    {
        var rig = new C3HarmRig(Fighter(), FighterTemplate.Melee);
        rig.Inner.HarmWorstCase = true;

        Assert.Equal(HarmHealState.Done, rig.RunToEnd());

        Assert.Equal(0, rig.Script.ResistExceeded);
        Assert.DoesNotContain(rig.R.H.Log.Lines, l => l.Contains("TODO(m4-harm-heal-resist)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_fizzle_is_counted_and_retried_inside_the_cap()
    {
        var rig = new C3HarmRig(Fighter(), FighterTemplate.Melee);
        rig.Inner.FizzleNext(2071, 2);

        var state = rig.RunToEnd();
        Assert.True(state == HarmHealState.Done, "why=" + rig.Script.Why + " " + string.Join(" ", rig.Timeline) + " | " + string.Join(" | ", rig.R.H.Log.Lines.Where(l => l.Contains("harm-heal", StringComparison.Ordinal))));

        Assert.Equal(2, rig.Script.Fizzles);
        Assert.Equal([(2071u, true), (2071u, true), (2071u, false)], rig.Inner.Casts.Take(3).ToArray());
        Assert.True(rig.Script.Harms >= 2);
        Assert.Contains("fizzles=2", rig.Script.VerdictLine(), StringComparison.Ordinal);
        Assert.True(rig.Script.Seconds - rig.Script.ManaRestSeconds < 60); // M4 P5a fix r4 (D28): the harm rounds' own time; the mana rest before round 2 is the keeper's, bounded by its rest cap
    }

    [Fact]
    public void A_round_that_keeps_landing_harms_is_not_ended_by_the_cap_measured_from_its_start_P5a_r3()
    {
        // Round 3: the Archer's second harm round landed a harm every few seconds (its mana spent) and the cap from the phase's start ended it at 70%. The cap now runs from the last landed harm; the round bound stays.
        var options = new HarmHealOptions { HarmCapSeconds = 3 };
        var rig = new C3HarmRig(Fighter(), FighterTemplate.Melee, options);

        var state = rig.RunToEnd();
        Assert.True(state == HarmHealState.Done, "why=" + rig.Script.Why + " seconds=" + rig.Script.Seconds);
        Assert.True(rig.Script.Seconds > options.HarmCapSeconds, "the leg outlasts the stall cap: " + rig.Script.Seconds);

        var bounded = new C3HarmRig(Fighter(), FighterTemplate.Melee, new HarmHealOptions { HarmRoundMaxSeconds = rig.Script.Seconds / 20 });
        var boundedState = bounded.RunToEnd();
        Assert.True(boundedState == HarmHealState.Failed && bounded.Script.Why == "harm-cap", "the round bound: " + boundedState + " why=" + bounded.Script.Why + " seconds=" + bounded.Script.Seconds + " of " + rig.Script.Seconds);
    }

    [Fact]
    public void The_cap_ends_the_leg_Failed_with_harm_cap_and_the_caster_still_comes_out()
    {
        var rig = new C3HarmRig(Fighter(), FighterTemplate.Melee);
        foreach (var id in VitalsMeans.HarmSelf)
        {
            rig.Inner.FizzleNext(id, 100_000);
        }

        Assert.Equal(HarmHealState.Failed, rig.RunToEnd());

        var script = rig.Script;
        Assert.Equal("harm-cap", script.Why);
        Assert.True(script.Seconds >= 60 && script.Seconds < 120, "the cap is 60 s: " + script.Seconds);
        Assert.True(script.Fizzles > 0);
        Assert.Equal(0, script.Harms);
        Assert.Contains("ok=false why=harm-cap", script.VerdictLine(), StringComparison.Ordinal);
        Assert.Matches(VerdictShape(), script.VerdictLine());
        Assert.Equal(200u, rig.Health); // nothing was harmed
        Assert.Contains("unwield:" + GearKit.Wand.Name, rig.Timeline); // the swap out still ran: the weapon is back in hand
        Assert.NotEqual(0u, rig.LocationOf(C3HarmRig.WeaponGuid));
        Assert.DoesNotContain(rig.Timeline, s => s is "kit" or "potion");
    }

    [Fact]
    public void A_lost_kit_roll_is_counted_and_the_next_try_heals()
    {
        var rig = new C3HarmRig(Fighter(), FighterTemplate.Melee);
        rig.Inner.KitRolls(false, true);

        Assert.Equal(HarmHealState.Done, rig.RunToEnd());

        Assert.Equal(1, rig.Script.KitLost);
        Assert.True(rig.Script.OwnKits >= 1);
        Assert.Equal(2, rig.Timeline.Take(rig.Timeline.IndexOf("potion")).Count(s => s == "kit"));
        Assert.Contains("kit_lost=1", rig.Script.VerdictLine(), StringComparison.Ordinal);
    }

    [Fact]
    public void Three_lost_kit_rolls_end_the_leg_with_kit_no_rise_after_the_swap_out()
    {
        var rig = new C3HarmRig(Fighter(), FighterTemplate.Melee);
        rig.Inner.KitRolls(false, false, false);

        Assert.Equal(HarmHealState.Failed, rig.RunToEnd());

        Assert.Equal("kit-no-rise", rig.Script.Why);
        Assert.Equal(3, rig.Script.KitLost);
        Assert.Contains("unwield:" + GearKit.Wand.Name, rig.Timeline);
    }

    [Fact]
    public void The_mage_harms_then_casts_Heal_Self_VII_with_no_swap()
    {
        var rig = new C3HarmRig(Mage());

        Assert.Equal(HarmHealState.Done, rig.RunToEnd());

        var t = rig.Timeline;
        Assert.DoesNotContain(t, s => s.StartsWith("wield", StringComparison.Ordinal) || s.StartsWith("unwield", StringComparison.Ordinal));
        Assert.DoesNotContain(t, s => s is "kit" or "potion");
        var firstHarm = First(t, IsHarm);
        var heal = t.IndexOf("cast:" + DeathHealSelfVii);
        Assert.True(firstHarm >= 0 && heal > firstHarm, "harm first, then Heal Self VII: " + string.Join(' ', t));
        Assert.Equal(DeathHealSelfVii, Lifestone.Client.Session.Scenarios.DeathScenario.HealSelfVii);
        Assert.False(rig.Script.Swapped);
        Assert.False(rig.Script.FighterPath);
        Assert.True(rig.Script.OwnHealCasts >= 1);
        Assert.True(rig.Script.Heals >= 1);
        Assert.Equal(0, rig.Script.Kits);
        Assert.Equal(0, rig.Script.Potions);
        Assert.True(rig.Inner.LowestHealth > HarmSelfPlan.MarginPoints);
        Assert.Equal(CombatModes.NonCombat, rig.R.H.State.Self.Qualities.CombatMode);
        Assert.Contains("swapped=no", rig.Script.VerdictLine(), StringComparison.Ordinal);
        Assert.Matches(VerdictShape(), rig.Script.VerdictLine());
    }

    [Fact]
    public void Under_a_vitae_penalty_the_mage_casts_its_best_known_Heal_Self_by_the_named_fallback_instead_of_failing_P5a_r3()
    {
        // Round 3: BSD Mage at 40% vitae read Life Magic 310; it knows Heal Self VII (power 300) and no level reached the 0.9 rule, so the leg failed heal-nospell. Now the best known level is cast, logged once.
        var rig = new C3HarmRig(Mage() with { LifeMagicRanks = 260, Spells = [.. VitalsMeans.HarmSelf, DeathHealSelfVii] });

        var state = rig.RunToEnd();
        Assert.True(state == HarmHealState.Done, "why=" + rig.Script.Why + " " + string.Join(" ", rig.Timeline) + " | " + string.Join(" | ", rig.R.H.Log.Lines.Where(l => l.Contains("harm-heal", StringComparison.Ordinal))));

        Assert.Contains("cast:" + DeathHealSelfVii, rig.Timeline);
        Assert.True(rig.Script.OwnHealCasts >= 1);
        var lines = rig.R.H.Log.Lines.Where(l => l.Contains("heal-fallback", StringComparison.Ordinal)).ToList();
        Assert.Single(lines);
        Assert.Contains("Heal Self: no known level reaches a chance of 0.9", lines[0], StringComparison.Ordinal);
        Assert.Contains("casting the best known level", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_character_that_knows_no_Harm_Self_fails_loudly_without_a_cast()
    {
        var rig = new C3HarmRig(Mage() with { Spells = [.. VitalsMeans.HealSelf] });

        Assert.Equal(HarmHealState.Failed, rig.RunToEnd());

        Assert.Equal("harm-nospell", rig.Script.Why);
        Assert.Empty(rig.Inner.Casts);
    }

    [Fact]
    public void A_health_already_at_the_target_harms_nothing_but_still_proves_the_heals()
    {
        var rig = new C3HarmRig(Fighter(health: 90), FighterTemplate.Melee);

        Assert.Equal(HarmHealState.Done, rig.RunToEnd());

        Assert.True(rig.Script.OwnKits >= 1 && rig.Script.OwnPotions == 1);
        Assert.True(rig.Inner.LowestHealth >= 90);
    }

    [Fact]
    public void A_character_that_knows_only_Harm_Self_I_walks_down_in_small_steps_and_never_near_0()
    {
        var rig = new C3HarmRig(Fighter(health: 140) with { Spells = [8u, .. VitalsMeans.HealSelf] }, FighterTemplate.Melee);
        rig.Inner.HarmWorstCase = true;

        Assert.Equal(HarmHealState.Done, rig.RunToEnd());

        Assert.True(rig.Script.Harms >= 6, "Harm Self I takes 6 at most: " + rig.Script.Harms);
        Assert.All(rig.Inner.Casts, c => Assert.Equal(8u, c.Spell));
        Assert.True(rig.Inner.LowestHealth >= 80, "never under about half: " + rig.Inner.LowestHealth);
    }

    private const uint DeathHealSelfVii = 2073;
}

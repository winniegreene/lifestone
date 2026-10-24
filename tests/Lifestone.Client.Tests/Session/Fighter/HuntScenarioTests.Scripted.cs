using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The hunt rows over the scripted rig (M4 4.1 fix2a, D23): <see cref="HuntScriptRig"/> plays ACE for the fight, the corpse and the heal in the tests alone, and nothing in it is reachable from a play-check or a frame.
/// The rows are the ones M4 4.1 t2 deleted with the stand-in's creature, by the review's R17 order: the recovery rule (3.1 s3), the swap edge cases (3.1 fix2), the corpse and loot rows (2.1, 2.2), the target choice and the approach, the shop (3.2 s4a).
/// The hunter is the scripted fighter, built to the template and kit (a Mace wielded, the Shortbow, 200 Arrows, two Handy Healing Kits and five Potions of Healing in the pack); its health is 188 when full.
/// </summary>
public sealed partial class HuntScenarioTests
{
    /// <summary>The hunt of the rows: the same options <c>Make</c> gives, built over the rig's own log.</summary>
    private static Func<RecordingLogSink, HuntScenario> Hunt(HuntWeapon weapon = HuntWeapon.Melee, int kills = 1, double seconds = 300, bool openCorpse = true, LootOptions? loot = null, RecoveryOptions? recovery = null, bool sell = false, int buyKits = 0, bool appraise = true) =>
        log => new HuntScenario(new HuntOptions { Weapon = weapon, Kills = kills, Seconds = seconds, OpenCorpse = openCorpse, Loot = loot ?? LootOptions.Default, Recovery = recovery ?? RecoveryOptions.Default, Sell = sell, BuyKits = buyKits, AppraiseLoot = appraise }, log, _ => 40f, endSession: false);

    /// <summary>A rig whose cell holds one creature 12 m north on each arrival (the next-cell rows number them).</summary>
    private static HuntScriptRig Belt12(Func<RecordingLogSink, HuntScenario> make, bool numbered = false)
    {
        var rig = HuntScriptRig.Create(make);
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature + (numbered ? (uint)(n - 1) : 0u), Belt, 12);
        return rig;
    }

    /// <summary>The hunter carries neither kit nor potions.</summary>
    private static void NoHealing(HuntScriptRig rig, bool kits = true, bool potions = true)
    {
        if (kits)
        {
            rig.Fighter.DropItems(FighterKit.HealingKitWcid);
        }

        if (potions)
        {
            rig.Fighter.DropItems(FighterKit.PotionWcid);
        }
    }

    /// <summary>A generator that answers the same draw for ever: 0.9999 loses every roll (the chance never reaches it).</summary>
    private sealed class FixedDraw(double draw) : Random
    {
        public override double NextDouble() => draw;
    }

    [Fact]
    public void Three_one_blow_kills_with_no_damage_line_are_counted_and_the_scenario_verdict_judges_them_as_the_damage_P5a_r4()
    {
        // Round 4's HuntLiveTests: kills=3 damage_dealt=0 swings=3 with one_blow_kills=3 in the detail, but the composed verdict read one_blow_kills=0. The rig writes no 01B1 for a lethal blow
        // (ACE Player_Combat.cs 157 @95ef426d): the order is the swing, the 01C0 health of 0, the 01AD kill notice; the scenario counts the kill as one blow and the verdict judges it.
        using var rig = Belt12(Hunt(kills: 3, openCorpse: false), numbered: true);
        rig.Blow = (500, 500);

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, rig.Scenario.Failure + rig.Tail());
        var scenario = rig.Scenario;
        Assert.Equal(3, scenario.Tally.Kills);
        Assert.Equal(0, scenario.Tally.DamageDealt);
        Assert.Equal(3, scenario.OneBlowKills);
        Assert.True(rig.Logged("kills=3 of 3 reached with damage_dealt=0 and one_blow_kills=3"), rig.Tail());
        Assert.True(rig.LineAt("the kill notice came (1 of 3)") < rig.LineAt("the kill notice came (2 of 3)"));
        var verdict = HuntLegVerdict.Compose(scenario.Failure, HuntLegVerdict.FromScenario(scenario, HuntWeapon.Melee, lootEnabled: false, HuntGroundChoice.Belt));
        Assert.EndsWith(" one_blow_kills=3", verdict.Line, StringComparison.Ordinal);
        Assert.DoesNotContain(verdict.Problems, p => p.StartsWith("damage_dealt=", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------------- group 1: the recovery rule (M4 3.1 s3 and fix R1) over the scripted heal

    [Fact]
    public void Under_the_trigger_before_the_first_fight_a_potion_is_drunk_and_the_hunt_goes_on_3_1_s3()
    {
        // 125 of 188 health (66%, under heal-below 75%), no kit, five potions: a potion restores 25 (150, 80%), a second (175, 93%) lifts it over heal-until 90% and the rule stops (a third would be drunk for 13 missing, under the 15 least).
        using var rig = Belt12(Hunt());
        NoHealing(rig, potions: false);
        rig.StartHealth = 125;

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, rig.Scenario.Failure + rig.Tail());
        var scenario = rig.Scenario;
        Assert.Equal(2, scenario.HealTally.Potions);
        Assert.Equal(0, scenario.HealTally.KitUses);
        Assert.Equal(50, scenario.HealTally.Points);
        Assert.Equal(2, scenario.Heals);
        Assert.Equal(2, scenario.PotionsUsed);
        Assert.True(rig.Logged("heal: potion 0x"), "the decision is logged");
        Assert.True(rig.Logged("heal: the potion restored 25 Health"), "the outcome with the amount is logged");
        Assert.True(rig.LineAt("heal: the potion restored") < rig.LineAt("ground: cell"), "the recovery came before the first teleport to the belt");
        Assert.Contains(" potions=2 heal_failed_rolls=0 heal_points=50 heal_wasted=0 ", scenario.DetailFields(), StringComparison.Ordinal);
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Matches(Verdict, scenario.VerdictFields()); // the scenario's verdict fields did not change shape
    }

    [Fact]
    public void With_nothing_to_heal_with_the_hunt_waits_300_seconds_for_regeneration_then_fails_loudly_before_any_fight_3_1_s3()
    {
        // 46% health, no kit and no potion: the rule waits (the scripted ACE does not regenerate), then the 300 s run out: a loud failure, no fight, the run returns (m4-hunt-recovery's rule).
        using var rig = Belt12(Hunt());
        NoHealing(rig);
        rig.StartHealth = 86;
        var started = double.NaN;

        var status = ScenarioStatus.Running;
        for (var t = 0.0; t < 900 && status == ScenarioStatus.Running; t += 0.5)
        {
            status = rig.Run(0.5);
            if (double.IsNaN(started) && rig.Logged("regenerating for up to 300 s"))
            {
                started = rig.Now;
            }
        }

        var scenario = rig.Scenario;
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(double.IsNaN(started), "the wait began");
        Assert.InRange(rig.Now - started, 299.0, 330.0); // the wait ran its 300 s (the return to Holtburg follows within seconds)
        Assert.StartsWith("recovery:", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("still under heal-below 75% after 300 s of regeneration", scenario.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO(m4-hunt-recovery)", scenario.Failure, StringComparison.Ordinal); // the row is closed by this item
        Assert.Equal(0, scenario.Attacks);
        Assert.False(rig.Logged("ground: cell"), "no teleport to the belt");
        Assert.Equal(0, scenario.HealTally.KitUses + scenario.HealTally.Potions);
        Assert.True(scenario.Returned);
    }

    [Fact]
    public void Under_the_retreat_share_with_nothing_to_heal_with_the_recovery_fails_at_once_without_a_wait_3_1_s3()
    {
        using var rig = Belt12(Hunt());
        NoHealing(rig);
        rig.StartHealth = 28; // 15% of 188

        var status = rig.Run(120);

        var scenario = rig.Scenario;
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("under the retreat share 30%, so no regeneration wait", scenario.Failure, StringComparison.Ordinal);
        Assert.False(rig.Logged("regenerating for up to"));
        Assert.Equal(0, scenario.Attacks);
        Assert.True(scenario.Returned);
    }

    [Fact]
    public void A_kit_with_a_low_chance_waits_behind_potions_until_the_health_lifts_it_over_the_chance_option_3_1_s3()
    {
        // The capture's case, in peace before the first fight: 43 of 188 health, the chance 0.31 (0.16 in a combat mode): potions (25 each, no roll) until the kit's chance reaches 0.7 (68 of 188 is 0.67, 93 is 0.90), then the kit heals on to 90%.
        using var rig = Belt12(Hunt());
        rig.StartHealth = 43;

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        var firstPotion = rig.LineAt("heal: potion 0x");
        var firstKit = rig.LineAt("heal: kit 0x");
        Assert.True(firstPotion >= 0 && firstKit > firstPotion, $"potion at {firstPotion}, kit at {firstKit}");
        Assert.True(rig.Log.Lines[firstPotion].Contains("under 0.7", StringComparison.Ordinal), rig.Log.Lines[firstPotion]);
        Assert.True(rig.Log.Lines[firstPotion].Contains("(145 missing)", StringComparison.Ordinal), rig.Log.Lines[firstPotion]);
        Assert.True(scenario.HealTally.Potions >= 1 && scenario.HealTally.KitUses >= 1, scenario.DetailFields());
        Assert.True(scenario.Heals >= 2);
        Assert.Contains("estimated chance 0.", rig.Log.Lines[firstKit], StringComparison.Ordinal);
        Assert.True(rig.Logged("the recovery is over after"), "the recovery ended on its own rule (heal-until)");
        Assert.True(scenario.Tally.Kills >= 1);
    }

    [Fact]
    public void With_the_potions_spent_a_kit_under_the_wanted_chance_is_used_as_the_last_resort_and_the_regeneration_wait_never_starts_3_1_fix_R1()
    {
        // No potion is left (the five were drunk at a full vital, spent for nothing, capture C3), and the health is 60 of 188 (32%): in peace 128 missing, the chance about 0.56, under the wanted 0.7 and over the floor 0.3.
        // Before the fix the hunt waited 300 s for regeneration and failed with the kit unused; now the kit is the last resort, and says so.
        using var rig = Belt12(Hunt());
        NoHealing(rig, kits: false);
        rig.HealRandom = new FixedDraw(0.5); // wins a roll at any chance over 0.5 and is no critical heal
        rig.StartHealth = 60;

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        var first = rig.LineAt("heal: kit 0x");
        Assert.True(first >= 0, "the kit was used" + rig.Tail());
        Assert.Contains("(128 missing)", rig.Log.Lines[first], StringComparison.Ordinal);
        Assert.Contains("under the 0.7 wanted, but no potion can be drunk and the kit is the last resort before the regeneration wait (at least 0.3 needed, --heal-floor-chance)", rig.Log.Lines[first], StringComparison.Ordinal);
        Assert.False(rig.Logged("regenerating for up to"), "no regeneration wait began");
        Assert.Equal(0, scenario.HealTally.Potions);
        Assert.True(scenario.HealTally.KitUses >= 1);
        Assert.True(rig.LineAt("heal: kit 0x") < rig.LineAt("ground: cell"), "the recovery came before the first teleport to the belt");
    }

    [Fact]
    public void Two_lost_rolls_in_a_row_switch_the_recovery_to_a_potion_3_1_s3()
    {
        // 100 of 188 health, every roll lost (the draw is never under the chance): the kit twice, then a potion (the health moves, the count clears), the kit twice, a potion, and so on up to 90%.
        using var rig = Belt12(Hunt());
        rig.HealRandom = new FixedDraw(0.9999);
        rig.StartHealth = 100;

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(6, scenario.HealTally.KitUses);
        Assert.Equal(6, scenario.HealTally.FailedRolls);
        Assert.Equal(3, scenario.HealTally.Potions); // 100 -> 125 -> 150 -> 175 of 188 (93%)
        Assert.Equal(75, scenario.HealTally.Points);
        Assert.Equal(3, scenario.Heals);
        var firstPotion = rig.LineAt("heal: potion 0x");
        Assert.True(firstPotion > rig.LineAt("heal: kit 0x"));
        Assert.Contains("2 rolls lost in a row", rig.Log.Lines[firstPotion], StringComparison.Ordinal);
        Assert.Equal(2, rig.Log.Lines.Take(firstPotion).Count(l => l.Contains("heal: the kit's roll was lost", StringComparison.Ordinal)));
        Assert.Contains("heal_failed_rolls=6 heal_points=75", scenario.DetailFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void Between_two_kills_a_health_share_under_the_trigger_is_healed_before_the_next_creature_is_picked_3_1_s3()
    {
        using var rig = Belt12(Hunt(kills: 2), numbered: true); // one creature per arrival: the second kill is in the next cell
        Assert.True(rig.RunUntil(() => rig.Scenario.Tally.Kills >= 1), rig.Scenario.Failure + rig.Tail());

        rig.SetPlayerHealthLater(0.05, 80); // 43% right after the first kill: the next creature must wait for the recovery
        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail(60));
        Assert.Equal(2, scenario.Tally.Kills);
        var recovering = rig.LineAt("after kill 1 is under heal-below 75%: recovering before the next fight");
        Assert.True(recovering >= 0, "the between-kills recovery started");
        var firstTarget = rig.LineAt("acquire: target");
        var secondTarget = rig.LineAt("acquire: target", firstTarget + 1);
        var firstHeal = rig.LineAt("heal: ", recovering);
        Assert.True(firstTarget < recovering && recovering < firstHeal && firstHeal < secondTarget, $"first target {firstTarget}, recovery {recovering}, first heal {firstHeal}, second target {secondTarget}");
        Assert.True(scenario.Heals >= 1);
        Assert.True(rig.LineAt("the recovery is over after", recovering) < secondTarget, "the recovery ended before the second creature was acquired");
        Assert.True(scenario.Tally.Looted >= 1);
    }

    [Fact]
    public void A_health_above_the_trigger_between_kills_sends_no_heal_3_1_s3()
    {
        using var rig = Belt12(Hunt(kills: 2), numbered: true);
        Assert.True(rig.RunUntil(() => rig.Scenario.Tally.Kills >= 1), rig.Scenario.Failure + rig.Tail());

        rig.SetPlayerHealthLater(0.05, 150); // 80%: over heal-below 75%
        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, rig.Scenario.Failure + rig.Tail());
        Assert.Equal(0, rig.Scenario.HealTally.KitUses + rig.Scenario.HealTally.Potions);
        Assert.False(rig.Logged("recovering before the next fight"));
    }

    [Fact]
    public void A_kit_refused_for_an_untrained_healing_is_set_aside_and_the_potions_carry_the_recovery_3_1_s3()
    {
        using var rig = Belt12(Hunt());
        rig.HealingTrained = false; // ACE: UseDone 0x04FC at once (capture C4)
        rig.StartHealth = 100;

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.True(rig.Logged("heal: the kit is set aside for the run (Healing is untrained, ACE's 0x04FC)"));
        Assert.Equal(0, scenario.HealTally.KitUses); // a refused use is not a use
        Assert.True(scenario.HealTally.Potions >= 1);
        Assert.Equal(1, rig.LineCount("heal: kit 0x")); // the one try; after it the potions alone
    }

    [Fact]
    public void With_heal_off_the_hunt_runs_no_recovery_at_all_and_fights_at_the_low_health_3_1_s3()
    {
        using var rig = Belt12(Hunt(recovery: new RecoveryOptions { Enabled = false }));
        rig.StartHealth = 80;

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.NotEqual(ScenarioStatus.Running, status);
        Assert.Equal(0, scenario.HealTally.KitUses + scenario.HealTally.Potions);
        Assert.False(rig.Logged("heal: kit 0x") || rig.Logged("heal: potion 0x"));
        Assert.True(rig.Logged("recovery: heal=off"));
        Assert.True(scenario.Attacks >= 1, "it went and fought at 43% health");
    }

    [Fact]
    public void The_heal_below_option_moves_the_trigger_3_1_s3()
    {
        // 60% health: under the default 75% (a heal), over --heal-below 50 (none).
        using var rig = Belt12(Hunt(recovery: new RecoveryOptions { BelowPercent = 50, UntilPercent = 70 }));
        rig.StartHealth = 113;

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.NotEqual(ScenarioStatus.Running, status);
        Assert.Equal(0, scenario.HealTally.KitUses + scenario.HealTally.Potions);
        Assert.True(scenario.Attacks >= 1);
        Assert.True(rig.Logged("heal_below=50% heal_until=70%"));
    }

    [Fact]
    public void The_retreat_still_ends_a_fight_at_30_percent_with_a_kit_in_the_pack_because_nothing_heals_during_a_fight_3_1_s3()
    {
        using var rig = HuntScriptRig.Create(Hunt());
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12, health: 1_000_000);
        rig.SetPlayerHealthLater(45, 20);

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("retreat", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(0, scenario.HealTally.KitUses + scenario.HealTally.Potions); // the kit and five potions were in the pack and none was used
        Assert.True(rig.Logged("TODO(m4-hunt-infight-heal)"), "the unbuilt in-fight heal is named in the log");
        Assert.True(scenario.Returned);
        Assert.False(rig.Session.Attack.Active);
    }

    [Fact]
    public void The_archer_retreats_at_50_percent_where_the_fighter_keeps_30_because_two_blows_took_it_to_32_percent_P4_sfix()
    {
        Assert.Equal(0.3f, HuntScenario.RetreatShareFor(HuntWeapon.Melee));
        Assert.Equal(0.5f, HuntScenario.RetreatShareFor(HuntWeapon.Missile));

        // 75 of about 188 health is 40%: over the fighter's line, under the archer's. The proof run's archer stood at 32% after two blows of about 67 and the third killed it.
        using var rig = HuntScriptRig.Create(Hunt(HuntWeapon.Missile));
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12, health: 1_000_000);
        rig.SetPlayerHealthLater(45, 75);

        var status = rig.Run(900);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("retreat: the health fell to", rig.Scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("(under 50%)", rig.Scenario.Failure, StringComparison.Ordinal);
    }
}

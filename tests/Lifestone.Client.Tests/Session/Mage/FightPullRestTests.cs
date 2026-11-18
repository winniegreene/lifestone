using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Vitals;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 AB: the rest before the next pull, scripted on the real Burrow profile (the data's floors: mana 60%, health 70%, until 90%, sit, 24 m): she rests where she stands before the pick and before any walk, an attacker's first hit or a creature in sight stands
/// her up and the fight goes on (M5 D88), the rest is bounded, logged with its before and after, and reaches <c>rest_s=</c>; a ground without the fields never rests. M6 7 AD: a character who knows the dance rests by it: she sits for stamina, stands, presses once, sits again, and with no Mana Conversion the fight presses before the bolt that would leave none.
/// </summary>
public sealed class FightPullRestTests
{
    private const uint Burrow = 0x57480210;
    private const uint FemaleTusker = 236;
    private const float PlayerX = 100;

    /// <summary>M6 7 AR gave the Burrow the owner's full bar; these tests read its floors under the bar's absence, the bar has its own (RechargeRulesTests).</summary>
    private static HuntingGround NoFullBar(string key)
    {
        var ground = GroundCatalog.Get(key);
        return key == "tusker-burrow" ? ground with { PullRest = ground.PullRest! with { FullPercent = 0 } } : ground;
    }

    private static (FightManaTests.Rig Rig, C2VitalsRig.PostureSpy Spy) Rig(uint mana = 50, bool knowsDance = false, string ground = "tusker-burrow", uint manaPotions = 0, bool potionRule = true, int potionStamina = 0, bool knowsHeal = false)
    {
        var rested = NoFullBar(ground);
        rested = rested.PullRest is null ? rested
            : potionRule ? rested with { PullRest = rested.PullRest with { PotionStaminaPercent = potionStamina } } // the rig's stamina is 100: the potion rule's stamina floor is a test's own to ask for
            : rested with { PullRest = rested.PullRest with { Potions = false, PotionManaPercent = 0, PotionStaminaPercent = 0 } };
        var r = new FightManaTests.Rig(mana: mana, stamina: 100, manaPotions: manaPotions, knowsDance: knowsDance, knowsDrain: knowsHeal, fight: new FightOptions { Ground = ground, GroundForTest = rested, Kills = 1, TargetMargin = 5, StaminaFullPercent = 0, Seconds = 3600 })
        {
            TuskerWcid = ground == "tusker-burrow" ? FemaleTusker : 1628,
        };
        if (ground == "tusker-burrow")
        {
            (r.TuskerCell, r.PlayerCell) = (Burrow, Burrow);
            r.R.H.R.Reducer.OnUpdatePosition(r.R.H.R.Context, ReducerHarness.Move(ActionHarness.Amy, 1, 3, 2, 1, cell: Burrow, x: PlayerX));
            r.R.H.Unhide(stateStamp: 5);
        }

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

    private static int Lines(FightManaTests.Rig r, string text) => r.Log.Lines.Count(l => l.Contains(text, StringComparison.Ordinal));

    [Fact]
    public void Under_the_floor_she_sits_before_the_pick_and_rises_at_the_target_then_the_bolt()
    {
        var (r, spy) = Rig(mana: 50);
        r.AddTusker(y: 48); // 28 m off: in the bolt's 30 m, over the 24 m of sight
        var scenario = r.Scenario();
        r.Run(scenario, 5, 0.1, () => spy.Posture != 0);

        Assert.Equal(VitalsKeeper.LieCommand, spy.Posture);
        Assert.Equal((int)CombatMode.NonCombat, r.Caster.ModesSent[^1]);
        Assert.Empty(r.BoltAnswers); // the rest comes before the pick
        Assert.Equal(1, Lines(r, "rest: begins before the next pull: mana "));

        var target = (uint)Math.Ceiling(MaxMana(r) * 0.9);
        r.Run(scenario, 300, 0.1, () => r.BoltAnswers.Any());

        Assert.Contains(r.BoltAnswers, _ => true);
        Assert.Equal(0u, spy.Posture); // the stop key
        Assert.Contains("0x41000003 down", spy.Calls);
        Assert.Equal((int)CombatMode.Magic, r.Caster.ModesSent[^1]);
        var line = Assert.Single(r.Log.Lines, l => l.Contains("rest: mana ", StringComparison.Ordinal));
        Assert.Contains("ended: the vitals are back", line, StringComparison.Ordinal);
        Assert.True(r.Mana + 60 >= target, $"the bolt came after the rest: {r.Mana} of {target}");
        Assert.Equal(1, scenario.PullRests);
        Assert.Equal(0, scenario.PullRestsCut);
        Assert.InRange(scenario.PullRestSeconds, (target - 60) / 5.5, (target - 50) / 4.5 + 5);
        Assert.Contains(" rest_s=", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void M6_14_under_the_health_floor_with_ample_mana_she_heals_herself_before_the_pull_and_does_not_lie_down()
    {
        // Live 2026-11-12, the Cave: 37 of her 46 rests before a pull were for health under 70%, about 39 s each lain down, with the mana near full.
        var (r, spy) = Rig(mana: 100_000, knowsHeal: true);
        var max = MaxMana(r);
        r.Caster.SetHealth(110); // of 170: under the floor (70%, 119) and over the emergency.s line (60%, 102), which is a drain.s
        r.AddTusker(y: 48); // 28 m off: in the bolt's 30 m, over the 24 m of sight
        var scenario = r.Scenario();

        r.Run(scenario, 120, 0.1, () => r.BoltAnswers.Any());

        var said = string.Join(" | ", r.Log.Lines.Where(l => l.Contains("rest", StringComparison.Ordinal) || l.Contains("health", StringComparison.Ordinal)).Select(l => l.Length > 220 ? l[..220] : l));
        Assert.True(Lines(r, "in place of a rest: health 110 of 170 is under 70%") == 1, $"max mana {max}, mana {r.Mana}: {said}");
        Assert.True(scenario.PullHeals >= 1 && scenario.PullHeals <= FightScenario.MaxPullHeals, said);
        Assert.Equal(0, Lines(r, "rest: begins before the next pull: health"));
        Assert.Equal(0, scenario.PullRests);
        Assert.DoesNotContain(VitalsKeeper.LieCommand.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), string.Join(' ', spy.Calls), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(r.BoltAnswers, _ => true); // then the pull
    }

    [Fact]
    public void M6_14_with_the_mana_short_of_the_heal_over_the_pulls_own_the_rest_stands_as_before_and_says_why_once()
    {
        var (r, spy) = Rig(mana: 50, knowsHeal: true);
        r.Caster.SetHealth(110);
        r.AddTusker(y: 48);
        var scenario = r.Scenario();

        r.Run(scenario, 5, 0.1, () => spy.Posture != 0);

        Assert.Equal(VitalsKeeper.LieCommand, spy.Posture);
        Assert.Equal(0, scenario.PullHeals);
        Assert.Equal(1, Lines(r, "rest: no "));
        Assert.Equal(1, Lines(r, " does not pay it ("));
        Assert.Equal(1, Lines(r, "the rest as before (m6-pull-heal)"));
    }

    [Fact]
    public void With_low_mana_she_rests_before_she_walks()
    {
        var (r, spy) = Rig(mana: 50);
        var walk = new FightManaTests.Walk();
        var scenario = r.Scenario(walk);
        r.Run(scenario, 5, 0.1, () => spy.Posture != 0);

        Assert.Equal(0, walk.Legs); // no target in range: the leg waits for the rest
        Assert.Equal(1, scenario.PullRests);
        r.Run(scenario, 300, 0.1, () => walk.Legs > 0);
        Assert.True(walk.Legs > 0, "the walk follows the rest");
    }

    [Fact]
    public void The_first_hit_stands_her_up_and_she_fights_back_no_new_rest_until_the_kill()
    {
        var (r, spy) = Rig(mana: 50);
        r.Caster.ManaRegenPerSecond = 1;
        var attacker = r.AddTusker(y: 48);
        var scenario = r.Scenario();
        r.Run(scenario, 5, 0.1, () => spy.Posture != 0);
        Assert.Empty(r.BoltAnswers);

        r.R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, "Tusker Slave", 4, 0.1, 8, 4, false, 0));
        r.Run(scenario, 10, 0.1, () => r.BoltAnswers.Any());

        Assert.Equal(0u, spy.Posture);
        Assert.Equal(1, scenario.PullRestsCut);
        Assert.Equal(1, Lines(r, "rest: cut after"));
        Assert.Contains(r.Log.Lines, l => l.Contains("ended: an attack came", StringComparison.Ordinal));
        Assert.Contains(r.BoltAnswers, _ => true);
        Assert.Equal(1, scenario.PullRests); // the attacker first: the fight goes on, no loop
        Assert.True(attacker != 0);
    }

    [Fact]
    public void An_evaded_swing_stands_her_up_too()
    {
        var (r, spy) = Rig(mana: 50);
        r.Caster.ManaRegenPerSecond = 1;
        r.AddTusker(y: 48);
        var scenario = r.Scenario();
        r.Run(scenario, 5, 0.1, () => spy.Posture != 0);

        r.R.Server((p, s) => AceM3Writers.EvasionDefenderNotification(p, s, "Tusker Slave"));
        r.Run(scenario, 2, 0.1, () => spy.Posture == 0);

        Assert.Equal(0u, spy.Posture);
        Assert.Equal(1, scenario.PullRestsCut);
    }

    [Fact]
    public void A_creature_coming_into_sight_stands_her_up()
    {
        var (r, spy) = Rig(mana: 50);
        r.Caster.ManaRegenPerSecond = 1;
        var tusker = r.AddTusker(y: 48);
        var scenario = r.Scenario();
        r.Run(scenario, 5, 0.1, () => spy.Posture != 0);

        r.MoveTusker(tusker, y: 30); // 10 m away
        r.Run(scenario, 2, 0.1, () => spy.Posture == 0);

        Assert.Equal(0u, spy.Posture);
        Assert.Equal(1, scenario.PullRestsCut);
        Assert.Equal(1, Lines(r, "is in sight 1"));
    }

    [Fact]
    public void A_creature_in_sight_refuses_the_rest_and_the_fight_goes_on()
    {
        var (r, spy) = Rig(mana: 50);
        r.AddTusker(y: 30); // 10 m away, a clear line
        var scenario = r.Scenario();
        r.Run(scenario, 10, 0.1, () => r.BoltAnswers.Any());

        Assert.Contains(r.BoltAnswers, _ => true);
        Assert.Equal(0, scenario.PullRests);
        Assert.Equal(1, scenario.PullRestsRefused);
        Assert.Equal(1, Lines(r, "rest: refused: mana "));
        Assert.Equal(0u, spy.Posture);
    }

    [Fact]
    public void A_rest_that_gains_nothing_ends_at_its_bound_and_stays_aside()
    {
        var (r, spy) = Rig(mana: 50);
        r.Caster.ManaRegenPerSecond = 0;
        var walk = new FightManaTests.Walk();
        var scenario = r.Scenario(walk);
        r.Run(scenario, 5, 0.1, () => spy.Posture != 0);
        r.Run(scenario, PullRestRule.CapSeconds + 10, 1, () => spy.Posture == 0);

        Assert.Equal(0u, spy.Posture);
        Assert.Equal(1, Lines(r, "rest: the bound of"));
        r.Run(scenario, 20, 0.25, () => walk.Legs > 0);

        Assert.True(walk.Legs > 0, "the fight goes on");
        Assert.Equal(1, scenario.PullRests); // aside: no second rest at once, no loop
    }

    [Fact]
    public void The_fights_stop_during_a_rest_stands_her_up_and_keeps_the_seconds()
    {
        var (r, spy) = Rig(mana: 50);
        r.Caster.ManaRegenPerSecond = 1;
        var scenario = r.Scenario();
        r.Run(scenario, 5, 0.1, () => spy.Posture != 0);
        r.Run(scenario, 10, 0.1);

        scenario.RequestStop(FightStopReason.StopRule);
        r.Run(scenario, 2, 0.1, () => spy.Posture == 0);

        Assert.Equal(0u, spy.Posture);
        Assert.Contains(r.Log.Lines, l => l.Contains("rest: standing up (the stop key) as the fight ends", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("ended: the fight was asked to stop", StringComparison.Ordinal));
        Assert.True(scenario.PullRestSeconds >= 9);
    }

    [Fact]
    public void A_posture_the_controller_cannot_take_rests_standing_in_peace()
    {
        var (r, _) = Rig(mana: 50);
        r.R.Api.Move.Unbind();
        r.AddTusker(y: 48);
        var scenario = r.Scenario();
        r.Run(scenario, 300, 0.1, () => r.BoltAnswers.Any());

        Assert.Equal(1, Lines(r, "rest: the posture lie was not taken"));
        Assert.Equal(1, scenario.PullRests);
        Assert.Contains(r.BoltAnswers, _ => true);
    }

    [Fact]
    public void A_ground_without_the_fields_never_rests_the_den()
    {
        var (r, spy) = Rig(mana: 50, ground: FightOptions.DefaultGround);
        r.AddTusker();
        var scenario = r.Scenario();
        r.Run(scenario, 20, 0.1, () => r.BoltAnswers.Any());

        Assert.Contains(r.BoltAnswers, _ => true);
        Assert.Equal(0, scenario.PullRests);
        Assert.Equal(0u, spy.Posture);
        Assert.Equal(0, Lines(r, "rest: "));
    }

    /// <summary>A rig of a mage with no Mana Conversion, who knows the dance, at level 10 of the Burrow's plan (M6 7 AD).</summary>
    private static (FightManaTests.Rig Rig, C2VitalsRig.PostureSpy Spy) Dancer(uint mana, uint stamina)
    {
        var (r, spy) = Rig(mana: mana, knowsDance: true);
        r.R.H.State.Self.Qualities.Skills.Remove(MageTemplate.ManaConversion);
        r.R.H.State.Self.Qualities.Ints[PlayerQualities.LevelProperty] = 10;
        r.Caster.SetVitals(mana, Math.Min(stamina, MaxStamina(r)));
        r.R.Tick();
        r.Caster.ManaRegenPerSecond = 0.25;
        r.Caster.StaminaRegenPerSecond = 1.5;
        return (r, spy);
    }

    private static uint MaxStamina(FightManaTests.Rig r)
    {
        r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out _, out var max);
        return max;
    }

    [Fact]
    public void A_character_who_knows_the_dance_sits_for_stamina_stands_presses_and_sits_again_to_the_top_M6_7_AD()
    {
        var (r, spy) = Dancer(mana: 50, stamina: 1);
        r.AddTusker(y: 48); // 28 m off: over the 24 m of sight
        var scenario = r.Scenario();
        r.Run(scenario, 5, 0.1, () => spy.Posture != 0);
        Assert.Equal(VitalsKeeper.LieCommand, spy.Posture);
        Assert.Empty(r.DanceAnswers); // the stamina is the input: she sits first

        r.Run(scenario, PullRestRule.CapSeconds, 0.1, () => r.DanceAnswers.Any());
        Assert.True(r.Caster.Stamina >= 0.3 * MaxStamina(r), $"the press came near a full bar: {r.Caster.Stamina} left of {MaxStamina(r)}");
        Assert.Contains((int)CombatMode.Magic, r.Caster.ModesSent);
        Assert.Contains("0x41000003 down", spy.Calls);
        r.Run(scenario, 60, 0.1, () => spy.Posture != 0);
        Assert.Equal(VitalsKeeper.LieCommand, spy.Posture); // and she sits again

        r.Run(scenario, PullRestRule.CapSeconds, 0.1, () => r.BoltAnswers.Any());
        var line = Assert.Single(r.Log.Lines, l => l.Contains("rest: mana ", StringComparison.Ordinal));
        Assert.Contains("dance presses=", line, StringComparison.Ordinal);
        Assert.DoesNotContain("dance presses=0", line, StringComparison.Ordinal);
        Assert.True(scenario.PullRestPressesSent >= 1);
        Assert.True(scenario.PullRestPressNet > 0, $"net {scenario.PullRestPressNet}");
        Assert.Equal(scenario.PullRestPressesSent, Lines(r, "rest: press "));
        Assert.Equal(1, scenario.PullRests);
        Assert.Contains($" rest_presses={scenario.PullRestPressesSent} ", " " + scenario.PullRestFields() + " ", StringComparison.Ordinal);
    }

    [Fact]
    public void With_a_near_full_bar_the_first_press_comes_before_the_first_sit_M6_7_AD()
    {
        var (r, spy) = Dancer(mana: 50, stamina: uint.MaxValue);
        r.AddTusker(y: 48);
        var scenario = r.Scenario();
        r.Run(scenario, 10, 0.1, () => r.DanceAnswers.Any());

        Assert.True(r.DanceAnswers.Count() == 1, string.Join("\n", r.Log.Lines.TakeLast(12)));
        Assert.Equal(0u, spy.Posture);
        Assert.DoesNotContain((int)CombatMode.NonCombat, r.Caster.ModesSent);
    }

    [Fact]
    public void A_press_that_fizzles_is_logged_and_the_rest_goes_on_M6_7_AD()
    {
        var (r, spy) = Dancer(mana: 50, stamina: uint.MaxValue);
        r.Caster.FizzleNext(s => VitalsMeans.StaminaToManaSelf.Contains(s.Id), 2);
        r.AddTusker(y: 48);
        var scenario = r.Scenario();
        r.Run(scenario, 30, 0.1, () => spy.Posture != 0);

        Assert.True(Lines(r, "it did not land, and the rest goes on by the regeneration") == 1, string.Join("\n", r.Log.Lines.TakeLast(12)));
        Assert.Equal(VitalsKeeper.LieCommand, spy.Posture);
        Assert.Equal(0, scenario.PullRestsCut);
        Assert.True(scenario.PullRestPressNet <= 0);
    }

    [Fact]
    public void A_creature_in_sight_stands_her_up_before_the_press_M6_7_AD()
    {
        var (r, spy) = Dancer(mana: 50, stamina: 1);
        var tusker = r.AddTusker(y: 48);
        var scenario = r.Scenario();
        r.Run(scenario, 5, 0.1, () => spy.Posture != 0);

        r.MoveTusker(tusker, 30); // 10 m off
        r.Run(scenario, 5, 0.1, () => scenario.PullRestsCut > 0);

        Assert.Equal(1, scenario.PullRestsCut);
        Assert.Empty(r.DanceAnswers);
        Assert.Equal(1, Lines(r, "she stands and fights back"));
    }

    [Fact]
    public void With_no_Mana_Conversion_she_presses_before_the_bolt_that_would_leave_no_press_M6_7_AD()
    {
        var (r, _) = Dancer(mana: 25, stamina: uint.MaxValue); // the rig's Flame Bolt II costs 20
        r.AddTusker(y: 30); // 10 m off: in sight, so no rest; the fight's own press
        var scenario = r.Scenario();
        r.Run(scenario, 20, 0.1, () => r.DanceAnswers.Any() || r.BoltAnswers.Any());

        Assert.True(Lines(r, "mana loop: press") == 1, string.Join("\n", r.Log.Lines.TakeLast(12))); // M6 7 AN: at 25 mana the loop's press comes first (mana under 30, the stamina full): it covers AD's case
        Assert.Single(r.DanceAnswers);
        Assert.Empty(r.BoltAnswers);
    }

    [Fact]
    public void With_Mana_Potions_held_she_drinks_before_the_pull_and_never_lies_down_M6_8()
    {
        var (r, spy) = Rig(mana: 50, manaPotions: 40);
        r.AddTusker(y: 48);
        var scenario = r.Scenario();
        r.Run(scenario, 300, 0.1, () => r.BoltAnswers.Any());

        Assert.Contains(r.BoltAnswers, _ => true);
        Assert.DoesNotContain(spy.Calls, c => c.Contains("down", StringComparison.Ordinal)); // no posture: the potion is no rest
        Assert.Equal(0, scenario.PullRests);
        Assert.True(scenario.PullRestPotions >= 1, "a potion stood in for the rest");
        Assert.Equal(scenario.PullRestPotions, r.Caster.PotionsDrunk);
        Assert.Equal(scenario.PullRestPotions, Lines(r, "rest: potion: check before: mana "));
        Assert.Equal(scenario.PullRestPotions, Lines(r, "rest: potion: check after: the Mana Potion brought the mana from "));
        Assert.Equal(0, Lines(r, "rest: begins before the next pull"));
        var target = (uint)Math.Ceiling(MaxMana(r) * 0.75); // the owner, 2026-11-11: the potion line, not the full bar
        Assert.True(r.Mana + 60 >= target, $"the bolt came at the potion line: {r.Mana} of {target}");
        Assert.InRange(scenario.PullRestPotions, 1, (int)Math.Ceiling((target - 50) / 25.0)); // to the line and not one more (the rig's own regeneration may save one)
    }

    [Fact]
    public void The_last_potion_drunk_the_rest_takes_over_for_what_is_still_short_M6_8()
    {
        var (r, spy) = Rig(mana: 50, manaPotions: 1);
        r.AddTusker(y: 48);
        var scenario = r.Scenario();
        r.Run(scenario, 30, 0.1, () => spy.Posture != 0);

        Assert.Equal(1, scenario.PullRestPotions);
        Assert.Equal(VitalsKeeper.LieCommand, spy.Posture); // none left: she lies down as before
        Assert.Equal(1, scenario.PullRests);
    }

    [Fact]
    public void A_ground_without_the_potion_flag_rests_with_potions_in_the_pack_M6_8()
    {
        var (r, spy) = Rig(mana: 50, manaPotions: 40, potionRule: false);
        r.AddTusker(y: 48);
        var scenario = r.Scenario();
        r.Run(scenario, 5, 0.1, () => spy.Posture != 0);

        Assert.Equal(VitalsKeeper.LieCommand, spy.Posture);
        Assert.Equal(0, scenario.PullRestPotions);
        Assert.Equal(0, Lines(r, "rest: potion:"));
    }

    [Fact]
    public void A_drink_the_server_is_too_busy_for_is_tried_again_and_the_potions_stay_in_use_M6_8()
    {
        var (r, spy) = Rig(mana: 50, manaPotions: 40);
        r.Caster.BusyDrinks = 2;
        r.AddTusker(y: 48);
        var scenario = r.Scenario();
        r.Run(scenario, 300, 0.1, () => r.BoltAnswers.Any());

        Assert.Equal(2, Lines(r, "rest: potion: check after: the server was too busy for the Mana Potion"));
        Assert.Equal(0, Lines(r, "Mana Potions are set aside"));
        Assert.True(scenario.PullRestPotions >= 1);
        Assert.Equal(0, scenario.PullRests);
        Assert.DoesNotContain(spy.Calls, c => c.Contains("down", StringComparison.Ordinal));
    }

    [Fact]
    public void A_maximum_that_falls_while_she_drinks_ends_the_drinking_at_the_new_maximum_M6_8()
    {
        // Live 2026-11-11 14:59 UTC: the maximum fell from 242 to 217 between two potions (a buff lapsed); the plan's target 242 stood, a potion was drunk at 217 of 217, restored nothing, and the potions were set aside.
        var (r, spy) = Rig(mana: 50, manaPotions: 40);
        r.Caster.ManaRegenPerSecond = 0;
        r.AddTusker(y: 48);
        var scenario = r.Scenario();
        var before = MaxMana(r);
        var target = (uint)Math.Ceiling(before * 0.75); // the potion line, planned on the maximum before the fall
        r.Run(scenario, 60, 0.1, () => r.Caster.PotionsDrunk >= 1);
        Assert.True(r.Mana + 30 < target, $"the first potion left her well under the target: {r.Mana} of {target}");

        r.R.Raw(AceWireWriters.PrivateUpdateVital(1, MageTemplate.MaxMana, 0, 0, 0, r.Mana)); // as a lapsed buff: the maximum falls (the vital's ranks go), the mana stays
        var after = MaxMana(r);
        Assert.True(after > r.Mana && after < target, $"the rig's maximum fell under the plan's target: {before} -> {after}, mana {r.Mana}, target {target}");
        r.Run(scenario, 300, 0.1, () => r.BoltAnswers.Any());

        Assert.Contains(r.BoltAnswers, _ => true);
        Assert.Equal(0, Lines(r, "Mana Potions are set aside"));
        Assert.Equal(0, Lines(r, "restores 0 points"));
        Assert.Equal(0, scenario.PullRests);
        Assert.DoesNotContain(spy.Calls, c => c.Contains("down", StringComparison.Ordinal));
        var line = Math.Ceiling(after * 0.75);
        Assert.Equal(1 + (int)Math.Ceiling((line - 75) / 25), r.Caster.PotionsDrunk); // on to the line of the maximum she has now, not of the one the plan was made on
    }

    [Theory]
    [InlineData(242u, 217u, 217u)] // the live case: the maximum fell under the plan's target
    [InlineData(200u, 242u, 200u)] // the usual case: the target stands
    [InlineData(200u, 0u, 200u)] // the maximum not read: the target stands
    public void The_mana_target_never_stands_above_the_maximum_she_has_now_M6_8(uint planned, uint max, uint expected) =>
        Assert.Equal(expected, new PullRestPlan(true, planned, 0, 60, "test").ManaTargetNow(max));

    [Fact]
    public void At_the_potion_line_or_over_no_potion_is_drunk_and_nothing_waits_for_mana_M6_8()
    {
        // The owner, 2026-11-11: she does not need full mana; at three quarters or over no potion is drunk.
        var (r, spy) = Rig(mana: 50, manaPotions: 40);
        var line = (uint)Math.Ceiling(MaxMana(r) * 0.75);
        r.Caster.SetVitals(line, 100);
        r.R.Tick();
        r.Caster.ManaRegenPerSecond = 0;
        r.AddTusker(y: 48);
        var scenario = r.Scenario();
        r.Run(scenario, 60, 0.1, () => r.BoltAnswers.Any());

        Assert.Contains(r.BoltAnswers, _ => true);
        Assert.Equal(0, r.Caster.PotionsDrunk);
        Assert.Equal(0, scenario.PullRests);
        Assert.Equal(0, Lines(r, "rest: potion:"));
        Assert.DoesNotContain(spy.Calls, c => c.Contains("down", StringComparison.Ordinal));
    }

    [Fact]
    public void With_potions_in_use_the_stamina_floor_stands_in_the_bars_place_M6_8()
    {
        // The Burrow's full bar is 100%; with potions in use she pulls on the potion line and the stamina's own floor.
        var ground = GroundCatalog.Get("tusker-burrow");
        var bar = ground.PullRest!;
        Assert.Equal((100, 75, 15), (bar.FullPercent, bar.PotionManaPercent, bar.PotionStaminaPercent)); // 15: live 2026-11-11, a floor of 50 sent her to rest for stamina a quarter of two cycles; a mage casts on no stamina, and exhaustion is at 0
        var inUse = bar.WithPotionsInUse(true);
        Assert.Equal((0, 75, 15, true), (inUse.FullPercent, inUse.ManaPercent, inUse.StaminaPercent, inUse.ToFloors));
        Assert.Same(bar, bar.WithPotionsInUse(false)); // none held, or set aside: the bar as before
        Assert.False(PullRestRule.UnderFullBar(inUse, 80, 100, 60, 100));
        Assert.True(PullRestRule.UnderFullBar(bar, 80, 100, 60, 100));

        var over = PullRestRule.Decide(new PullRestInput(80, 100, 100, 100, inUse, 0.25, 60, 100));
        Assert.False(over.Wanted); // mana 80% and stamina 60%: the pull goes
        var mana = PullRestRule.Decide(new PullRestInput(40, 100, 100, 100, inUse, 0.25, 60, 100));
        Assert.Equal((true, 75u, 60u), (mana.Wanted, mana.ManaTarget, mana.StaminaTarget)); // to the line, not to the until share
        var stamina = PullRestRule.Decide(new PullRestInput(80, 100, 100, 100, inUse, 0.25, 10, 100));
        Assert.Equal((true, 80u, 15u), (stamina.Wanted, stamina.ManaTarget, stamina.StaminaTarget));
        var health = PullRestRule.Decide(new PullRestInput(80, 100, 50, 100, inUse, 0.25, 60, 100));
        Assert.Equal((true, 80u, 60u, 90u), (health.Wanted, health.ManaTarget, health.StaminaTarget, health.HealthTarget)); // a rest for health waits for no mana and no stamina
    }

    [Theory]
    [InlineData(217u, 242u, true)] // 25 fits in 25
    [InlineData(218u, 242u, false)] // it would overflow by one
    [InlineData(242u, 242u, false)]
    [InlineData(250u, 242u, false)] // a maximum that fell under the mana
    [InlineData(0u, 20u, false)]
    public void A_potion_that_would_overflow_is_not_drunk_M6_8(uint mana, uint max, bool fits) => Assert.Equal(fits, PullRestRule.PotionFits(mana, max, 25));

    [Fact]
    public void With_the_stamina_full_she_presses_Stamina_to_Mana_before_any_potion_M6_8()
    {
        // The owner, 2026-11-11: she still uses Stamina to Mana, and stamina does not sit full, where it regenerates nothing.
        var (r, spy) = Rig(mana: 50, knowsDance: true, manaPotions: 40);
        r.Caster.SetVitals(50, MaxStamina(r));
        r.R.Tick();
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0;
        r.AddTusker(y: 48);
        var scenario = r.Scenario();
        r.Run(scenario, 30, 0.1, () => r.DanceAnswers.Any());

        Assert.Single(r.DanceAnswers);
        Assert.Equal(0, r.Caster.PotionsDrunk); // the press came first
        Assert.Equal(1, Lines(r, "rest: press: check before: stamina "));

        r.Run(scenario, 300, 0.1, () => r.BoltAnswers.Any());
        Assert.Contains(r.BoltAnswers, _ => true);
        Assert.Single(r.DanceAnswers); // the stamina is under its share after the press: the potions take over
        Assert.True(r.Caster.PotionsDrunk >= 1);
        Assert.Equal(0, scenario.PullRests);
        Assert.DoesNotContain(spy.Calls, c => c.Contains("down", StringComparison.Ordinal));
    }
}

using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Options;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>M5 item 7 F15, the fight review's fixes over the scripted caster: the upkeep's health stop (7-F2), the drain before the refill (7-F3), the measured stamina drain (7-F4) and the plan cap on the self spells (7-F6).</summary>
public sealed class FightF15Tests
{
    [Fact]
    public void The_autopilots_health_stop_sits_under_the_drain_line_when_the_drain_rule_is_on_and_at_fifty_when_it_is_off_M5_7_F15_7_F2()
    {
        Assert.Equal(30, new AutopilotOptions().HealthFloorPercent);
        Assert.True(new AutopilotOptions().HealthFloorPercent < FightOptions.DefaultDrainUnderPercent);
        var off = AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-drain-under=0"]));
        Assert.Equal(AutopilotScenario.HealthFloorPercent, off.HealthFloorPercent);
        var low = AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-drain-under=40"]));
        Assert.Equal(24, low.HealthFloorPercent, 6); // 60% of the drain line
        Assert.Equal(30, AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-drain-under=90"])).HealthFloorPercent); // at most 30
        Assert.Equal(30, AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-drain-under=101"])).HealthFloorPercent); // a bad value keeps the default
    }

    [Fact]
    public void Under_the_drain_line_with_a_target_in_reach_the_drain_comes_before_the_stamina_refill_M5_7_F15_7_F3()
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 100, knowsDrain: true) { HitsToKill = 40 };
        r.Caster.HealthPerCast = 40;
        r.Caster.ManaRegenPerSecond = 20;
        r.Caster.StaminaRegenPerSecond = 0;
        r.AddTusker();
        r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        r.Caster.SetHealth(max * 30 / 100);

        var scenario = r.Scenario();
        r.Run(scenario, 30, 0.1, () => r.Caster.Answered.Any());

        var first = r.Caster.Answered.First();
        Assert.Contains(first.Spell, DrainPicker.Ids); // the drain first; before the fix a Revitalize Self (stamina 100 of 275 is under 90%) came first
        Assert.DoesNotContain(r.RevitalizeAnswers, a => a.At <= first.At);
    }

    [Fact]
    public void A_Drain_Stamina_Other_that_yields_under_a_quarter_of_its_expected_gain_is_set_aside_for_that_target_only_M5_7_F15_7_F4()
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 100, knowsStaminaDrain: true);
        var scenario = r.Scenario();
        scenario.NoteStaminaDrainYield(0x80001001, before: 100, expected: 112, after: 100); // ACE wrote "You gain 0 points of stamina": the landing proves the cast, not the yield
        scenario.NoteStaminaDrainYield(0x80001002, before: 100, expected: 112, after: 168); // 68 of 112: a share that still pays
        Assert.True(scenario.StaminaDrainSetAside(0x80001001));
        Assert.False(scenario.StaminaDrainSetAside(0x80001002));
        Assert.False(scenario.StaminaDrainSetAside(0x80001003)); // never measured
        Assert.Contains(r.Log.Lines, l => l.Contains("0x80001001 gave 0 stamina of the 112 expected (0 %)", StringComparison.Ordinal) && l.Contains("set aside for this target: Revitalize Self takes over", StringComparison.Ordinal));
    }

    [Fact]
    public void The_refill_offers_no_level_over_the_plan_cap_whatever_the_pack_pays_for_M5_7_F15_7_F6()
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 100);
        var self = r.R.H.State.Self;
        var uncapped = new FightRefill(r.Caster.Table, r.Log, 90);
        uncapped.Fill(self.Qualities, self.Spellbook, null, 0.5);
        var capped = new FightRefill(r.Caster.Table, r.Log, 90);
        capped.Fill(self.Qualities, self.Spellbook, null, 0.5, 6);
        Assert.Equal(uncapped.Count - 1, capped.Count);
        Assert.Equal(VitalsMeans.RevitalizeSelf[5], capped.Levels[^1].SpellId); // VI at level 80 (D26), not VII
        Assert.Equal(VitalsMeans.RevitalizeSelf[6], uncapped.Levels[^1].SpellId);
    }
}

using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 16 Q6 (docs/automation-engine.md E3, stage 1; <c>--fight-survival-mana</c>): with the option on, the survival layer decides the fight's mana loop and stamina drain and the fight carries it out. Proved over the scripted
/// caster of the fight's mana tests, for each of the loop's three rules (the fixed loop of another character, the climber's loop by the sum, her fills by the vitals policy): the same fight with the option off and on casts
/// the same spells in the same order and writes the same lines but the one that says the layer decides, and with it on the layer was asked. Off (the default) the layer is never asked. The pure answer is
/// <c>SurvivalRuleManaLoopTests</c>'.
/// </summary>
public sealed class FightSurvivalManaTests
{
    private static void TargetHolds(FightManaTests.Rig r, uint guid, uint stamina) =>
        r.R.Server((p, s) => AceVendorWriters.IdentifyResponse(p, s, guid, new AceAppraisal
        {
            Success = true,
            Creature = new AceCreatureProfile(100, 100) { Attributes = [10, 10, 10, 10, 10, 10, stamina, 10, stamina, 10], Buffs = (0, 0) },
        }));

    /// <summary>One fight: <paramref name="name"/> at the bars given, Mana Conversion untrained, a tusker holding <paramref name="tuskerStamina"/>, run for <paramref name="seconds"/>.</summary>
    private static (List<uint> Spells, List<string> Lines, FightScenario Scenario, FightManaTests.Rig Rig) Fight(bool on, string name, uint mana, uint stamina, uint tuskerStamina, bool policy, double seconds)
    {
        var r = new FightManaTests.Rig(mana: mana, stamina: stamina, knowsStaminaDrain: true) { Fight = new FightOptions { Kills = 1, MaxLevel = 1, StaminaFullPercent = 0, Seconds = 3600, ManaPolicy = policy, SurvivalMana = on }, HitsToKill = 6 };
        r.R.H.State.Self.Name = name;
        if (!policy)
        {
            r.R.H.State.Self.Qualities.Skills.Remove(MageTemplate.ManaConversion); // the bases are the costs, and the fixed loop runs (it is for a caster with none)
        }

        r.R.Tick();
        if (!policy)
        {
            r.Caster.ManaRegenPerSecond = 0; // the policy's fights keep the rig's Mana Conversion and regeneration, as FightVitalsPolicyTests' do
            r.Caster.StaminaRegenPerSecond = 0;
        }
        var tusker = r.AddTusker(y: 28);
        TargetHolds(r, tusker, tuskerStamina);
        var scenario = r.Scenario();
        r.Run(scenario, seconds, 0.25);
        var spells = r.Caster.Answered.Select(a => a.Spell).ToList();
        var lines = r.Log.Lines.Where(l => !l.Contains(FightScenario.SurvivalManaToken, StringComparison.Ordinal)).ToList();
        return (spells, lines, scenario, r);
    }

    private static void SameFight(string name, uint mana, uint stamina, uint tuskerStamina, bool policy, double seconds, Func<FightManaTests.Rig, FightScenario, bool> exercised)
    {
        var off = Fight(false, name, mana, stamina, tuskerStamina, policy, seconds);
        var on = Fight(true, name, mana, stamina, tuskerStamina, policy, seconds);

        Assert.True(exercised(off.Rig, off.Scenario), "the fight reaches the loop's decision it is meant to: " + string.Join(" | ", off.Lines.Where(l => l.Contains("mana", StringComparison.Ordinal)).Take(12)));
        Assert.Equal(off.Spells, on.Spells); // every cast, in order: the loop's presses and drains and the bolts between
        Assert.Equal(off.Lines, on.Lines); // every line of the fight but the layer's one
        Assert.Equal(0, off.Scenario.SurvivalManaAsks);
        Assert.True(on.Scenario.SurvivalManaAsks > 0);
        Assert.DoesNotContain(off.Rig.Log.Lines, l => l.Contains(FightScenario.SurvivalManaToken, StringComparison.Ordinal));
        Assert.Single(on.Rig.Log.Lines, l => l.Contains(FightScenario.SurvivalManaToken + "the survival layer decides the fight's mana loop and stamina drain (--fight-survival-mana on", StringComparison.Ordinal));
    }

    [Fact]
    public void The_fixed_loops_press_is_the_layers_and_the_fight_casts_exactly_what_it_did()
    {
        // BSD Mage (not the climber) at 30 mana, under mana_loop_under 45, with 200 stamina: the fixed loop presses.
        SameFight("BSD Mage", mana: 30, stamina: 200, tuskerStamina: 290, policy: false, seconds: 40, (r, s) => s.ManaLoopPresses > 0 && r.Log.Lines.Any(l => l.Contains("mana loop: press: mana 30 of", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_fixed_loops_stamina_drain_and_its_target_are_the_layers_and_the_fight_casts_exactly_what_it_did()
    {
        // 30 mana and 10 stamina, short of the press's half: the tusker's 290 gives 58, at least the 30 the fixed loop asks.
        SameFight("BSD Mage", mana: 30, stamina: 10, tuskerStamina: 290, policy: false, seconds: 40, (r, s) => s.ManaLoopDrains > 0 && r.Log.Lines.Any(l => l.Contains("mana loop: Drain Stamina Other on Tusker Slave", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_climbers_loop_by_the_sum_is_the_layers_through_the_stamina_line_the_bolts_between_and_the_press_that_fills()
    {
        // FightManaLoopBySumTests' second fight: the press would overflow, the stamina line's drain, the bolts owed after it, then the press that fills.
        SameFight(MageTemplate.ClimberName, mana: 400, stamina: 200, tuskerStamina: 290, policy: false, seconds: 60, (r, s) => s.ManaLoopDrains > 0 && s.ManaLoopPresses > 0 && r.Log.Lines.Any(l => l.Contains("mana loop: none: the press would overflow", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_climbers_loop_by_the_sum_takes_nothing_where_no_cast_pays_with_the_layer_as_without()
    {
        SameFight(MageTemplate.ClimberName, mana: 300, stamina: 10, tuskerStamina: 80, policy: false, seconds: 40, (r, s) => s.ManaLoopDrains == 0 && r.Log.Lines.Any(l => l.Contains("mana loop: none: no mana cast pays", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_climbers_fills_by_the_vitals_policy_are_the_layers_and_the_fight_casts_exactly_what_it_did()
    {
        // FightVitalsPolicyTests' press under the mana floor.
        SameFight(MageTemplate.ClimberName, mana: 8, stamina: 200, tuskerStamina: 290, policy: true, seconds: 60, (r, s) => s.ManaLoopPresses > 0 && r.Log.Lines.Any(l => l.Contains("vitals policy: press", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_option_is_off_by_default_and_on_by_its_switch()
    {
        Assert.False(new FightOptions().SurvivalMana);
        Assert.DoesNotContain("survival_mana", new FightOptions().ToString(), StringComparison.Ordinal);
        var on = FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-survival-mana=on"]));
        Assert.Empty(on.Errors);
        Assert.True(on.SurvivalMana);
        Assert.Contains(" survival_mana=on", on.ToString(), StringComparison.Ordinal);
        Assert.Single(FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-survival-mana=maybe"])).Errors);
        Assert.Contains(OptionCatalog.Fight().Options, x => x.Name == "--fight-survival-mana");
    }
}

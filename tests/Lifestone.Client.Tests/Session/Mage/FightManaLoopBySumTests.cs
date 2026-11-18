using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>M6 8 H: the climber's mana loop by the sum, in the fight rig (<see cref="ManaLoopRule.DecideBySum"/>; the pure rule is <c>RechargeRulesTests</c>).</summary>
public sealed class FightManaLoopBySumTests
{
    private static FightManaTests.Rig Climber(uint mana, uint stamina)
    {
        var r = new FightManaTests.Rig(mana: mana, stamina: stamina, knowsStaminaDrain: true) { Fight = new FightOptions { Kills = 1, MaxLevel = 1, StaminaFullPercent = 0, Seconds = 3600, ManaPolicy = false }, HitsToKill = 6 }; // M6 14: the sum rule, which the policy replaced for the climber (FightVitalsPolicyTests)
        r.R.H.State.Self.Name = MageTemplate.ClimberName;
        r.R.H.State.Self.Qualities.Skills.Remove(MageTemplate.ManaConversion); // the climber has none: the bases are the costs
        r.R.Tick();
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0;
        return r;
    }

    private static void TargetHolds(FightManaTests.Rig r, uint guid, uint stamina) =>
        r.R.Server((p, s) => AceVendorWriters.IdentifyResponse(p, s, guid, new AceAppraisal
        {
            Success = true,
            Creature = new AceCreatureProfile(100, 100) { Attributes = [10, 10, 10, 10, 10, 10, stamina, 10, stamina, 10], Buffs = (0, 0) },
        }));

    [Fact]
    public void The_drain_really_casts_when_the_sum_says_so_and_every_amount_is_on_one_line_M6_8_H()
    {
        var r = Climber(mana: 300, stamina: 10);
        var tusker = r.AddTusker(y: 28);
        TargetHolds(r, tusker, 290);
        var scenario = r.Scenario();
        r.Run(scenario, 60, 0.25, () => r.Log.Lines.Any(l => l.Contains("mana loop: drain landed", StringComparison.Ordinal)));

        // M6 14: at stamina 10 of 275 the stamina line decides (it was the pair by the sum before), so the line names it and what she pays.
        Assert.Contains(r.Log.Lines, l => l.Contains("mana loop: drain by the stamina line: Drain Stamina Other I on Tusker Slave", StringComparison.Ordinal) && l.Contains("it holds 290 stamina, gives 58", StringComparison.Ordinal) && l.Contains("pays the drain 10 and a bolt", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("mana loop: drain landed", StringComparison.Ordinal) && l.Contains("it held 290, read to give 58", StringComparison.Ordinal) && l.Contains("stamina 10 -> 50", StringComparison.Ordinal));
        Assert.Equal(1, scenario.ManaLoopDrains);
    }

    [Fact]
    public void M6_14_the_drain_fills_the_stamina_then_the_bolts_between_then_the_press_that_fills_and_says_so()
    {
        // M6 8 H pressed straight after the drain (the pair); M6 14: the drain is the stamina line's, the bolts between follow it, and the press comes once she is over the line and its give fits the mana she is missing.
        // Mana 400 of 426: Stamina to Mana I's give of 45 would overflow the 26 missing, so the first bolt goes while the target's stamina is asked.
        var r = Climber(mana: 400, stamina: 200);
        var tusker = r.AddTusker(y: 28);
        TargetHolds(r, tusker, 290);
        var scenario = r.Scenario();
        r.Run(scenario, 80, 0.25, () => r.Log.Lines.Any(l => l.Contains("mana loop: press landed", StringComparison.Ordinal)));

        var lines = r.Log.Lines.ToList();
        Assert.Contains(lines, l => l.Contains("mana loop: none: the press would overflow: mana 400 of 426 (missing 26)", StringComparison.Ordinal));
        var drain = lines.FindIndex(l => l.Contains("mana loop: drain by the stamina line:", StringComparison.Ordinal) && l.Contains("stamina 200 of 275 under 80%", StringComparison.Ordinal));
        var press = lines.FindIndex(l => l.Contains("mana loop: press by the sum: the press fills:", StringComparison.Ordinal) && l.Contains("stamina 240 of", StringComparison.Ordinal));
        Assert.True(drain >= 0 && press > drain, $"drain at {drain}, press at {press}" + Environment.NewLine + string.Join(Environment.NewLine, lines.Where(l => l.Contains("mana", StringComparison.Ordinal)).Take(40)));
        Assert.Equal(1, scenario.ManaLoopDrains);
        Assert.Equal(3, r.BoltAnswers.Count()); // the first, then mana_loop_bolts_between's two, owed after the drain
        var after = lines.Last(l => l.Contains("mana loop: press landed", StringComparison.Ordinal));
        Assert.Contains("read to net", after, StringComparison.Ordinal);
        Assert.Contains("stamina 240 -> 190", after, StringComparison.Ordinal); // Stamina to Mana I's cap of 50, landed whole
        Assert.True(r.DanceAnswers.Any());
    }

    [Fact]
    public void M6_14_the_stamina_line_drains_on_a_full_mana_bar_in_the_fight()
    {
        var probe = Climber(mana: 1, stamina: 1);
        Assert.True(probe.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out _, out var max));
        var r = Climber(mana: max, stamina: 100);
        r.Caster.ManaRegenPerSecond = 1000; // the bolt's mana is back before the next decision: the bar is full each time
        var tusker = r.AddTusker(y: 28);
        TargetHolds(r, tusker, 290);
        var scenario = r.Scenario();
        r.Run(scenario, 30, 0.25, () => r.Log.Lines.Any(l => l.Contains("mana loop: drain landed", StringComparison.Ordinal)));

        // The first decision asks the target's stamina (none read: the full bar rests, M6 8 H's answer); the next drains on the full bar.
        Assert.Contains(r.Log.Lines, l => l.Contains("mana loop: drain by the stamina line:", StringComparison.Ordinal) && l.Contains($"mana {max} of {max} pays", StringComparison.Ordinal) && l.Contains("stamina 100 of", StringComparison.Ordinal));
        Assert.Equal(1, scenario.ManaLoopDrains);
    }

    [Fact]
    public void A_target_with_too_little_stamina_is_not_drained_and_the_bolts_go_on_M6_8_H()
    {
        var r = Climber(mana: 300, stamina: 10);
        var tusker = r.AddTusker(y: 28);
        TargetHolds(r, tusker, 80); // the outskirts' 16 a cast: the pair loses mana at this bar
        var scenario = r.Scenario();
        r.Run(scenario, 60, 0.25);

        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("mana loop: drain by the sum", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("mana loop: none: no mana cast pays", StringComparison.Ordinal));
        Assert.Equal(0, scenario.ManaLoopDrains);
        Assert.True(r.BoltAnswers.Any());
    }

    [Fact]
    public void The_end_line_reports_the_counts_and_the_mana_at_the_end_M6_8_H()
    {
        var r = Climber(mana: 300, stamina: 10);
        var tusker = r.AddTusker(y: 28);
        TargetHolds(r, tusker, 80);
        var scenario = r.Scenario();
        r.Run(scenario, 60, 0.25);

        var fields = scenario.VerdictFields();
        Assert.Contains(" loop_presses=", fields, StringComparison.Ordinal);
        Assert.Contains(" loop_drains=0", fields, StringComparison.Ordinal);
        Assert.Matches(@" mana_end=\d+ mana_end_pct=\d+ ", fields);
    }

    [Fact]
    public void Another_character_keeps_todays_rule_M6_8_H()
    {
        var r = Climber(mana: 300, stamina: 10);
        r.R.H.State.Self.Name = "BSD Mage";
        var tusker = r.AddTusker(y: 28);
        TargetHolds(r, tusker, 290);
        var scenario = r.Scenario();
        r.Run(scenario, 60, 0.25);

        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("by the sum", StringComparison.Ordinal)); // at 300 mana, over the fixed 45, today's rule does nothing
        Assert.Equal(0, scenario.ManaLoopDrains);
    }
}

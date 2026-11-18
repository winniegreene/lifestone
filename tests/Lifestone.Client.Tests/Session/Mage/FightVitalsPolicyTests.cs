using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 (<c>m6-vitals-policy</c>): the climber's fills by the policy, in the fight rig (<see cref="VitalsPolicy"/>; the pure rule is <c>VitalsPolicyTests</c>). What these prove: the climber's fight asks the policy before a bolt and logs
/// the decision with every number it read; under the mana floor the press it chooses really casts; the sum rule's lines no longer run for her; another character keeps its rule.
/// </summary>
public sealed class FightVitalsPolicyTests
{
    private static FightManaTests.Rig Climber(uint mana, uint stamina)
    {
        var r = new FightManaTests.Rig(mana: mana, stamina: stamina, knowsStaminaDrain: true) { Fight = new FightOptions { Kills = 1, MaxLevel = 1, StaminaFullPercent = 0, Seconds = 3600 }, HitsToKill = 6 };
        r.R.H.State.Self.Name = MageTemplate.ClimberName;
        r.R.Tick();
        return r;
    }

    [Fact]
    public void The_climbers_fight_decides_by_the_policy_and_logs_every_number()
    {
        var r = Climber(mana: 300, stamina: 200);
        r.AddTusker(y: 28);
        var scenario = r.Scenario();
        r.Run(scenario, 60, 0.25, () => r.Log.Lines.Any(l => l.Contains("vitals policy: ", StringComparison.Ordinal) && l.Contains("health ", StringComparison.Ordinal)));

        var line = r.Log.Lines.First(l => l.Contains("vitals policy: ", StringComparison.Ordinal) && l.Contains("a kill ", StringComparison.Ordinal));
        foreach (var part in new[] { "health ", "stamina ", "mana ", "a kill ", "bolts; mana ", "floors mana ", "stamina ready ", "pull mana ", "prices damage ", "next bolt kills ", "regen a second health ", "Mana Conversion " })
        {
            Assert.True(line.Contains(part, StringComparison.Ordinal), $"'{part}' not in: {line}");
        }

        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("by the sum", StringComparison.Ordinal));
    }

    [Fact]
    public void Under_the_mana_floor_the_policys_press_really_casts()
    {
        var r = Climber(mana: 8, stamina: 200);
        r.AddTusker(y: 28);
        var scenario = r.Scenario();
        r.Run(scenario, 80, 0.25, () => r.Log.Lines.Any(l => l.Contains("mana loop: press landed", StringComparison.Ordinal)));

        Assert.True(r.Log.Lines.Any(l => l.Contains("vitals policy: press", StringComparison.Ordinal) && l.Contains("under the mana floor", StringComparison.Ordinal)), string.Join(" | ", r.Log.Lines.Where(l => l.Contains("vitals policy", StringComparison.Ordinal) || l.Contains("mana", StringComparison.Ordinal)).Take(12)));
        Assert.Contains(r.Log.Lines, l => l.Contains("mana loop: press landed", StringComparison.Ordinal));
        Assert.True(scenario.ManaLoopPresses >= 1);
    }

    [Fact]
    public void Another_character_never_asks_the_policy()
    {
        var r = Climber(mana: 300, stamina: 200);
        r.R.H.State.Self.Name = "BSD Mage";
        r.AddTusker(y: 28);
        var scenario = r.Scenario();
        r.Run(scenario, 40, 0.25);

        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("vitals policy", StringComparison.Ordinal));
    }
}

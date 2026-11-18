using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 16 E3.0 (docs/automation-engine.md section 3: a shadow reports "without allocating"): one ask of the survival layer inside the fight and its comparison allocate nothing while the two agree or a difference stands
/// (<c>FightScenario.SurvivalShadow.cs</c>): the board's gather of her attackers, the health picture's read of each, the pricing of the heals and of the mana's sums on the layer's picks, the rule and the comparison. Measured
/// on the calling thread (<see cref="AllocationMeasurementCollection"/>; a window with a GC inside is not judged), over the scripted caster of the fight tests: a fight under way at the way out's line, an attacker on her, her
/// health under full so the heals are priced.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class FightSurvivalShadowAllocationTests
{
    private const string Slave = "Tusker Slave";
    private static readonly LadderSurvival Lines = new(50, 80, 25, 60, 15, 40, 50, 15, 80, 50, 0); // the ladder's survival lines of 2026-11-17

    [Fact]
    public void An_ask_of_the_layer_and_its_comparison_allocate_nothing()
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true) { HitsToKill = 400, TuskerFlags = WorldObjectTable.AttackableFlag };
        r.Caster.HealthPerCast = 18;
        r.Caster.ManaRegenPerSecond = 20;
        var tusker = r.AddTusker();
        var scenario = r.Scenario();
        scenario.Survival = static (in SurvivalInput asked) => SurvivalRule.Decide(in asked, Lines);
        r.Run(scenario, 20, 0.1, () => r.BoltAnswers.Any());
        r.Caster.ManaRegenPerSecond = 0;
        r.Caster.StaminaRegenPerSecond = 0;
        r.Run(scenario, 3, 0.1);
        Hit(r, scenario);
        r.R.H.R.Reducer.OnUpdateHealth(r.R.H.R.Context, r.R.H.Header, new UpdateHealthEvent(tusker, 1f));
        r.R.Tick();
        scenario.ArmWayOut(ManaWayOut.LineOf(50, 10));
        r.Caster.SetVitals(70, 0); // the bolt is refused for the way out: a difference stands at that site
        r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        r.Caster.SetHealth(max * 70 / 100); // hurt, over every heal line: the heals are priced and nobody heals
        r.Run(scenario, 4, 0.1, () => scenario.WayOutRefused);
        Hit(r, scenario);
        r.Run(scenario, 2, 0.1);
        Assert.True(scenario.WayOutRefused);
        Assert.True(scenario.SurvivalShadowOf("way-out").Differ > 0);

        var (view, clock) = (r.View, r.Now);
        Assert.True(scenario.SurvivalShadowProbe(view, clock += 0.001)); // the lists have their capacity
        scenario.SurvivalShadowProbe(view, clock += 2); // and any line of the probe's own site is written
        var before = scenario.SurvivalShadowAgree + scenario.SurvivalShadowDiffer;

        AllocationAssert.Nothing(() => scenario.SurvivalShadowProbe(view, clock += 0.001)); // a new clock value each time: nothing is answered from the last ask

        Assert.True(scenario.SurvivalShadowAgree + scenario.SurvivalShadowDiffer > before);
        var (read, _, input) = scenario.SurvivalShadowLast;
        Assert.True(read && input.Attackers.Count == 1 && input.Attackers[0].Guid == tusker && input.Attackers[0].Health > 0, "the attacker and its health were read at each ask");
        Assert.True(input.HealthPercent < 100 && input.Fight.BoltMana > 0);
    }

    private static void Hit(FightManaTests.Rig r, FightScenario scenario)
    {
        r.R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, Slave, 4, 0.1, 3, 4, false, 0));
        r.Run(scenario, 0.3, 0.1);
    }
}

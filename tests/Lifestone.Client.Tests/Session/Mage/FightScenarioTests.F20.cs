using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 item 7 F20 over the scripted ACE: the fight selects its target (asks its health) when the target changes, and the picker's snapshot and the opener read the live model, not the table's last value.
/// </summary>
public sealed partial class FightScenarioTests
{
    [Fact]
    public void The_fight_selects_its_target_when_it_picks_it_and_the_decision_reads_the_live_model()
    {
        var r = new Rig(openers: true);
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        var scenario = r.Scenario(new FakeWalk());
        scenario.LiveHealth.Updated(TuskerGuid, 0.6f, 0); // the server's value before the first decision
        scenario.LiveHealth.Took(TuskerGuid, 100, 400, 0); // our own spell took 100 of the Slave's 400: 35 % in the model, though the table holds nothing

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(1, LinesWith(r, $"health: the health of 0x{TuskerGuid:X8} is asked of the server (the new target is selected)"));
        Assert.True(LinesWith(r, "(health 35 % modelled") > 0, string.Join(" | ", r.Log.Lines.Where(l => l.Contains("decide: ", StringComparison.Ordinal)))); // the first decision reads the model (the table held no health)
        Assert.Equal(TuskerGuid, scenario.LiveHealth.Selected);
    }
}

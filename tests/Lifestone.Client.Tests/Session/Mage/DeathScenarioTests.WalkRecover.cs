using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session.Grounds;

namespace Lifestone.Client.Tests.Session.Mage;

// m5-fight-walk-recover (M5 item 2 g5): the death station's den walk. The recovery lives in the walk (RunnerLegs, LegRecovery), so the scenario that ticks it goes on through a recovered leg.
public sealed partial class DeathScenarioTests
{
    [Fact]
    public void A_leg_the_walk_recovers_does_not_end_the_death_scenarios_den_walk()
    {
        // The first leg stalls (item 1's round 1 lost the mage journey's death station to one stall 0.70 m from a goal); the walk abandons it and goes on to the next waypoint, which never arrives: the tuskers kill the mage
        // in the middle of it, and the death is judged as it is with a walk that never stalls.
        var rig = new Rig(buffed: false);
        var walk = new ScriptedRecoveringWalk(leg => leg == 1, ticksPerLeg: int.MaxValue);
        var scenario = rig.Scenario(walk, DeathStages.Den | DeathStages.Death);

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(scenario.Failure);
        Assert.Equal(1, walk.Recovery.Recovered);
        Assert.Equal(2, walk.Legs);
        Assert.DoesNotContain(Lines(rig), l => l.Contains("the walk failed", StringComparison.Ordinal));
        Assert.Contains(" died=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_stalled_leg_ends_the_death_scenarios_den_walk_only_when_the_walk_does_not_recover_it()
    {
        var rig = new Rig(buffed: false);
        var walk = new ScriptedRecoveringWalk(leg => leg == 1, recover: false);
        var scenario = rig.Scenario(walk, DeathStages.Den | DeathStages.Death);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));
        Assert.Contains("the walk failed: leg 1: scripted stall", scenario.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Three_stalled_legs_in_a_row_end_the_death_scenarios_den_walk_named()
    {
        var rig = new Rig(buffed: false);
        var walk = new ScriptedRecoveringWalk(_ => true);
        var scenario = rig.Scenario(walk, DeathStages.Den | DeathStages.Death);

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario));
        Assert.Contains("the walk failed: 3 legs failed in a row (leg 1 to 0x0105", scenario.Failure, StringComparison.Ordinal);
    }
}

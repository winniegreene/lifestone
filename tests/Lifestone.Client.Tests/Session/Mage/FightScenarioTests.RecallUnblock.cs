using Lifestone.Client.Session.Scenarios;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 recall unblock (m6-loot-hold; live 2026-11-14 17:27:13 to 17:27:27 UTC: a corpse opened in the fight's last seconds held the Use lane the urgent trip's recall then waited on; the fight-backs after the failed recall
/// looted on): once the fight is asked to stop for a trip or a retreat, or stands in for a travel or recovery state, it opens no corpse. A loot in hand stands down, and the kills are left for the next ground visit.
/// Over the same scripted ACE as the other loot tests.
/// </summary>
public sealed partial class FightScenarioTests
{
    [Fact]
    public void Under_a_stop_for_a_trip_the_fight_opens_no_corpse_and_leaves_its_kill_for_the_next_visit_M6_14()
    {
        // The kill's notice comes with its health at 0 (settled at once); the body and its corpse come 2 s later. Before M6 14 the stop waited for that loot: the corpse was opened under the stop.
        var r = new Rig(fight: LootRetryFight(kills: 3)) { Corpses = true, DeathDelay = 2, CorpseAtDeletion = true, HealthZeroAtKill = true };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        r.Add(SlaveB, TuskerSlave, Den, 32);
        var scenario = r.Scenario(new FakeWalk());
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running && ((IStoppableFight)scenario).Kills == 0; i++)
        {
            status = r.Frame(scenario);
        }

        Assert.Equal(ScenarioStatus.Running, status);
        Assert.Equal(1, ((IStoppableFight)scenario).Kills);
        Assert.Empty(r.Opened); // the corpse is not there yet

        scenario.RequestStop(FightStopReason.LogisticsTrip);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario, 60));

        Assert.Empty(r.Opened);
        Assert.Equal("the fight is asked to stop (logistics-trip): she leaves the ground by a trip, a retreat or the stop's recall", scenario.LootHeld);
        Assert.Equal(1, LinesWith(r, "loot: held: the fight is asked to stop (logistics-trip)"));
        Assert.Equal(1, LinesWith(r, "loot: 1 kill(s) not yet looked at are left under the loot's hold"));
        Assert.Equal(1, LinesWith(r, "the fight was asked to stop (logistics-trip)"));
    }

    [Fact]
    public void A_fight_that_stands_in_for_a_travel_state_opens_no_corpse_and_a_stop_for_the_minutes_still_loots_first_M6_14()
    {
        // The keeper's fight-back on the travel state (17:27:42): the loot is held, so the fight kills and never opens.
        var held = new Rig(fight: LootRetryFight(kills: 2)) { Corpses = true };
        held.Add(SlaveA, TuskerSlave, Den, 30);
        held.Add(SlaveB, TuskerSlave, Den, 32);
        var fightBack = held.Scenario(new FakeWalk());
        ((IStoppableFight)fightBack).HoldLoot("the fight stands in for the travel state (a keeper's fight-back, M6 7 BD)");
        Assert.Equal(ScenarioStatus.Succeeded, held.Run(fightBack));
        Assert.Equal(2, ((IStoppableFight)fightBack).Kills);
        Assert.Empty(held.Opened);
        Assert.Equal(1, LinesWith(held, "loot: held: the fight stands in for the travel state"));

        // A stop that does not leave the ground (the quest's end) keeps the clean stop's rule: the loot in hand finishes first.
        Assert.False(FightScenario.LeavesTheGround(FightStopReason.UntilMet));
        Assert.True(FightScenario.LeavesTheGround(FightStopReason.ForcedTrip));
        Assert.True(FightScenario.LeavesTheGround(Lifestone.Client.Session.Vitals.RetreatRule.StopReason));
    }
}

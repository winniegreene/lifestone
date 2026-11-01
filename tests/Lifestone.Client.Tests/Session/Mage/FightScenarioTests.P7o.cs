using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session.Grounds;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 P7o (the item-end gate's <c>storeys_visited 1 under 2</c>, D44 c2b): D88's self-defence dropped the tour's walk and three attackers on the arrival's storey filled the mage journey's kill quota (3) there. With
/// <c>--fight-storeys-min</c> 2 the quota alone no longer wins the fight while another storey is reachable: the attackers are still fought back, the tour's walk is taken up, and the fight is won at the first kill on the second storey.
/// With no other storey reachable the fight ends as before, named.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint LowerCell = 0x01050280; // the den's z -6 storey

    /// <summary>Three Virindi (avoided by the profile) on the arrival's floor attack one after another and are killed by self-defence (kills 1 to 3).</summary>
    private static void DefendThree(Rig r, FightScenario scenario, Creature[] attackers)
    {
        for (var i = 0; i < 2400 && attackers.Any(a => !a.Dead) && r.Frame(scenario) == ScenarioStatus.Running; i++)
        {
            if (i % 4 == 0 && attackers.Any(a => !a.Dead))
            {
                MeleeHit(r);
            }
        }

        Assert.All(attackers, a => Assert.True(a.Dead));
        Assert.Equal(3, scenario.Tally.Kills);
        Assert.Equal(3, scenario.Defended);
    }

    [Fact]
    public void P7o_three_attackers_killed_on_the_arrival_storey_do_not_win_a_two_storey_fight_and_the_first_kill_on_the_second_storey_does()
    {
        var r = new Rig(fight: new FightOptions { Kills = 3, StoreysMin = 2 });
        Creature[] virindi = [r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi"), r.Add(VirindiGuid + 1, Virindi, FloorCell, 32, "Virindi"), r.Add(VirindiGuid + 2, Virindi, FloorCell, 34, "Virindi")];
        var below = r.Add(TuskerGuid, TuskerSlave, LowerCell, 140); // on the next storey, 120 m away: out of range from the arrival
        var tour = new GroundTour(DenGround.Profile, (_, _) => 0, r.Log);
        var walk = new FakeWalk { Tour = tour };
        var scenario = ToTheFight(r, r.Scenario(walk));

        DefendThree(r, scenario, virindi);
        for (var i = 0; i < 40; i++)
        {
            Assert.Equal(ScenarioStatus.Running, r.Frame(scenario)); // kill 3 does not win: one storey of two
        }

        Assert.Contains(r.Log.Lines, l => l.Contains(FightScenario.StoreysHoldLine + ": kills=3 of 3 with 1 of 2 storeys visited", StringComparison.Ordinal));
        Assert.True(walk.Legs >= 1, "the tour's walk was taken up after the self-defence kills");
        Assert.Contains(r.Log.Lines, l => l.Contains("walking: the tour leaves this storey", StringComparison.Ordinal));
        Assert.False(below.Dead);

        // the walk arrives on the z -6 storey and the tusker is in range there
        var h = r.R.H.R;
        h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(ActionHarness.Amy, 1, 3, 2, 1, cell: LowerCell, x: PlayerX));
        r.Move(TuskerGuid, LowerCell, 40);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(below.Dead);
        Assert.Equal(4, scenario.Tally.Kills);
        Assert.Equal(2, scenario.StoreysVisited);
        Assert.Contains(" storeys_visited=2 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(r.Log.Lines, l => l.Contains("storeys met: 2 of 2 storeys visited and kill 4 made after them: the fight is won", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains(FightScenario.StoreysUnmetLine, StringComparison.Ordinal));
    }

    [Fact]
    public void P7o_a_kill_on_the_second_storey_made_from_range_while_the_body_is_on_the_first_does_not_win_and_the_tour_still_leaves()
    {
        // The win hold reads the body's storeys_visited (the judged field), not the tour's count, which the kill on z -6 raises to 2.
        var r = new Rig(fight: new FightOptions { Kills = 3, StoreysMin = 2 });
        Creature[] virindi = [r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi"), r.Add(VirindiGuid + 1, Virindi, FloorCell, 32, "Virindi")];
        var guard = r.Add(0x80001003, TuskerGuard, LowerCell, 46); // on z -6, 26 m: in range from the arrival's floor
        var tour = new GroundTour(DenGround.Profile, (_, _) => 0, r.Log);
        var walk = new FakeWalk { Tour = tour };
        var scenario = ToTheFight(r, r.Scenario(walk));

        for (var i = 0; i < 2400 && (!guard.Dead || virindi.Any(a => !a.Dead)) && r.Frame(scenario) == ScenarioStatus.Running; i++)
        {
            if (i % 4 == 0 && virindi.Any(a => !a.Dead))
            {
                MeleeHit(r);
            }
        }

        Assert.True(guard.Dead);
        Assert.All(virindi, a => Assert.True(a.Dead));
        Assert.Equal(3, scenario.Tally.Kills);
        Assert.Equal(2, tour.StoreysVisited); // the tour counts the kill's storey
        Assert.Equal(1, scenario.StoreysVisited); // the body stood on one
        var legs = walk.Legs;
        for (var i = 0; i < 60; i++)
        {
            Assert.Equal(ScenarioStatus.Running, r.Frame(scenario)); // not won
        }

        Assert.Contains(r.Log.Lines, l => l.Contains(FightScenario.StoreysHoldLine + ": kills=3 of 3 with 1 of 2 storeys visited by the body (tour 2)", StringComparison.Ordinal));
        Assert.True(walk.Legs > legs, "the tour still leaves the storey: the body has not met the minimum");

        // the walk arrives on z -6 (storeys_visited=2) and the first kill there wins
        var h = r.R.H.R;
        h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(ActionHarness.Amy, 1, 3, 2, 1, cell: LowerCell, x: PlayerX));
        r.Add(TuskerGuid, TuskerSlave, LowerCell, 40);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(4, scenario.Tally.Kills);
        Assert.Contains(" storeys_visited=2 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(r.Log.Lines, l => l.Contains("storeys met: 2 of 2 storeys visited and kill 4 made after them: the fight is won", StringComparison.Ordinal));
    }

    [Fact]
    public void P7o_with_no_other_storey_reachable_the_two_storey_fight_ends_at_its_quota_as_before_by_name()
    {
        var r = new Rig(fight: new FightOptions { Kills = 3, StoreysMin = 2 });
        Creature[] virindi = [r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi"), r.Add(VirindiGuid + 1, Virindi, FloorCell, 32, "Virindi"), r.Add(VirindiGuid + 2, Virindi, FloorCell, 34, "Virindi")];
        var tour = new GroundTour(DenGround.Profile, (_, _) => double.PositiveInfinity, r.Log); // no walk joins any slot: no other storey is reachable
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk { Tour = tour }));

        DefendThree(r, scenario, virindi);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(3, scenario.Tally.Kills);
        Assert.Single(r.Log.Lines, l => l.Contains(FightScenario.StoreysUnmetLine + ": kills=3 of 3 with 1 of 2 storeys visited", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains(FightScenario.StoreysHoldLine, StringComparison.Ordinal));
    }

    [Fact]
    public void P7o_a_one_storey_fight_and_the_autopilots_open_ended_fight_are_not_held_for_storeys()
    {
        var r = new Rig(fight: new FightOptions { Kills = 1 }); // the default storeys-min 1
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        var scenario = r.Scenario(new FakeWalk { Tour = new GroundTour(DenGround.Profile, (_, _) => 0, r.Log) });
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("storeys", StringComparison.Ordinal) && (l.Contains(FightScenario.StoreysHoldLine, StringComparison.Ordinal) || l.Contains(FightScenario.StoreysUnmetLine, StringComparison.Ordinal)));

        // the autopilot's den fight is open-ended: no kill quota, so no win to hold (its storeys-min comes from no plan argument)
        var open = Lifestone.Client.Session.Scenarios.Journey.JourneyStations.DenFightOptions(Lifestone.Client.Session.Autopilot.AutopilotScenario.FightSpec, new FightOptions(), []);
        Assert.True(open.UnlimitedKillCount);
        Assert.Equal(FightOptions.DefaultStoreysMin, open.StoreysMin);
    }
}

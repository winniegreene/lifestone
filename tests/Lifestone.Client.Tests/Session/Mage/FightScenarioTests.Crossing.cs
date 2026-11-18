using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session.Grounds;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 D (<c>m6-gate-den-storey-race</c>): the den fight's crossing to its second storey is cut by attacker after attacker (D88 stands: each is killed), and the seconds budget used to end it first. Now an owed crossing that still progresses outlives the
/// budget, the legs go on from where the body got to, and each end of the overtime is named. Over the same scripted ACE and walk as the other fight tests; the gate's live proof is the journey's <c>storeys_visited&gt;=2</c>.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const double CrossingBudget = 40;

    private static (FightScenario Scenario, FakeWalk Walk, Creature Below) ThreeKillsHeldForTheStoreys(Rig r)
    {
        Creature[] virindi = [r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi"), r.Add(VirindiGuid + 1, Virindi, FloorCell, 32, "Virindi"), r.Add(VirindiGuid + 2, Virindi, FloorCell, 34, "Virindi")];
        var below = r.Add(TuskerGuid, TuskerSlave, LowerCell, 140);
        var walk = new FakeWalk { Tour = new GroundTour(DenGround.Profile, (_, _) => 0, r.Log), TicksPerLeg = 2000 }; // a long leg: it is only ever cut, it never arrives by itself
        var start = r.Now;
        var scenario = ToTheFight(r, r.Scenario(walk));
        DefendThree(r, scenario, virindi);
        Assert.True(scenario.Stage == "fight" && r.Now - start < CrossingBudget, "the quota is met inside the budget: " + (r.Now - start));
        return (scenario, walk, below);
    }

    /// <summary>One more attacker cuts the leg under way and is killed; returns once the next leg of the crossing is under way, or the fight has ended.</summary>
    private static ScenarioStatus OneMoreAttackerCutsTheLeg(Rig r, FightScenario scenario, FakeWalk walk, uint guid)
    {
        var attacker = r.Add(guid, Virindi, FloorCell, 30, "Virindi");
        var legs = walk.Legs;
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 600 && status == ScenarioStatus.Running && (!attacker.Dead || walk.Legs == legs); i++)
        {
            if (!attacker.Dead && i % 4 == 0)
            {
                MeleeHit(r);
            }

            status = r.Frame(scenario);
        }

        return status;
    }

    private static void BodyOnTheLowerStorey(Rig r)
    {
        var h = r.R.H.R;
        h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(ActionHarness.Amy, 1, 3, 2, 1, cell: LowerCell, x: PlayerX));
    }

    [Fact]
    public void M6_7_D_the_crossing_survives_attackers_past_the_seconds_budget_and_the_fight_is_won_on_the_second_storey()
    {
        var r = new Rig(fight: new FightOptions { Kills = 3, StoreysMin = 2, Seconds = CrossingBudget });
        var start = r.Now;
        var (scenario, walk, below) = ThreeKillsHeldForTheStoreys(r);

        var guid = VirindiGuid + 3;
        while (r.Now - start < CrossingBudget + 8)
        {
            Assert.Equal(ScenarioStatus.Running, OneMoreAttackerCutsTheLeg(r, scenario, walk, guid++)); // the budget passes here: each attacker is killed (D88), the crossing is still owed
        }

        Assert.True(scenario.Tally.Kills >= 4, "attackers on the way are killed, the win stays held");
        Assert.Equal(scenario.Tally.Kills, scenario.Defended);
        Assert.Equal(1, LinesWith(r, FightScenario.CrossingOvertimeLine));
        Assert.True(LinesWith(r, "self-defence: the walk is dropped") >= 1, "the walk was cut");
        Assert.True(walk.Legs >= 2, "the tour's walk is taken up again after every cut");
        Assert.Equal(1, scenario.StoreysVisited);

        // the leg gets through: the body stands on z -6, and the first kill there wins
        BodyOnTheLowerStorey(r);
        r.Move(TuskerGuid, LowerCell, 40);
        walk.TicksPerLeg = 2; // the leg arrives; the tusker is the next pick
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(below.Dead);
        Assert.Equal(2, scenario.StoreysVisited);
        Assert.Contains(" storeys_visited=2 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Null(scenario.Failure);
    }

    [Fact]
    public void M6_7_D_the_body_on_the_second_storey_at_the_budget_gets_its_next_kill_past_it()
    {
        // M6 4 round 3: the body crossed at 289.6 s and "the next kill" was cut at 300 s, with storeys_visited=2 and 19 kills
        var r = new Rig(fight: new FightOptions { Kills = 3, StoreysMin = 2, Seconds = CrossingBudget });
        var start = r.Now;
        var (scenario, walk, below) = ThreeKillsHeldForTheStoreys(r);
        BodyOnTheLowerStorey(r);
        while (r.Now - start < CrossingBudget + 12)
        {
            Assert.Equal(ScenarioStatus.Running, r.Frame(scenario)); // the tusker is out of range until the budget has passed
        }

        Assert.Equal(1, LinesWith(r, FightScenario.CrossingOvertimeLine));
        r.Move(TuskerGuid, LowerCell, 40);
        walk.TicksPerLeg = 2; // the leg arrives; the tusker is the next pick
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));
        Assert.True(below.Dead);
        Assert.Equal(4, scenario.Tally.Kills);
    }

    [Fact]
    public void M6_7_D_a_crossing_with_no_kill_and_no_new_cell_fails_by_name_after_the_stall_bound()
    {
        var r = new Rig(fight: new FightOptions { Kills = 3, StoreysMin = 2, Seconds = CrossingBudget });
        var (scenario, _, _) = ThreeKillsHeldForTheStoreys(r);
        var lastKill = r.Now;

        Assert.Equal(ScenarioStatus.Failed, r.Run(scenario)); // the walk never gets anywhere and nothing attacks

        Assert.StartsWith("fight: 40 s passed with kills=3 of 3", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("the owed crossing's overtime ended at", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("no kill and no new cell for 60 s", scenario.Failure, StringComparison.Ordinal);
        Assert.InRange(r.Now, lastKill + FightScenario.CrossingStallSeconds - 2, lastKill + FightScenario.CrossingStallSeconds + 6); // the failure's own leaving of the ground takes a moment
        Assert.Single(r.Log.Lines, l => l.Contains("storeys: the owed crossing's overtime ends at", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_7_D_an_owed_crossing_that_keeps_killing_stops_at_the_hard_bound_by_name()
    {
        var r = new Rig(fight: new FightOptions { Kills = 3, StoreysMin = 2, Seconds = CrossingBudget });
        var start = r.Now;
        var (scenario, walk, _) = ThreeKillsHeldForTheStoreys(r);

        var guid = VirindiGuid + 3;
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 200 && status == ScenarioStatus.Running; i++)
        {
            status = OneMoreAttackerCutsTheLeg(r, scenario, walk, guid++);
        }

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("the bound 80 s (2 x --fight-seconds) passed", scenario.Failure, StringComparison.Ordinal);
        Assert.InRange(r.Now - start, CrossingBudget * FightScenario.CrossingBudgetTimes, (CrossingBudget * FightScenario.CrossingBudgetTimes) + 12);
    }

    [Fact]
    public void M6_7_D_a_fight_under_its_quota_at_the_budget_fails_as_before_with_no_overtime()
    {
        var r = new Rig(fight: new FightOptions { Kills = 3, StoreysMin = 2, Seconds = 20 });
        var tough = r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi", hitsToKill: 1000);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk { Tour = new GroundTour(DenGround.Profile, (_, _) => 0, r.Log) }));
        MeleeHit(r);

        Assert.Equal(ScenarioStatus.Failed, r.Run(scenario));

        Assert.False(tough.Dead);
        Assert.StartsWith("fight: 20 s passed with kills=0 of 3 and damage_dealt=", scenario.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain("overtime", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(0, LinesWith(r, FightScenario.CrossingOvertimeLine));
    }

    [Fact]
    public void M6_7_D_while_the_crossing_is_owed_an_attacker_with_no_shot_is_not_walked_back_to_the_tours_leg_keeps_the_ground()
    {
        var r = new Rig(fight: new FightOptions { Kills = 3, StoreysMin = 2, DefendForgetSeconds = 30 });
        var first = r.Add(TuskerGuid, TuskerSlave, Den, 40); // the kill that makes the tour want to leave
        var walk = new FakeWalk { Tour = new GroundTour(DenGround.Profile, (_, _) => 0, r.Log), BlockedCell = FloorCell, KeepBlockedCell = true, TicksPerLeg = 3, TraceCells = [Den, OtherFloorCell, FloorCell] };
        walk.Floor.Add(SpotAt(OtherFloorCell, 26));
        walk.ClearFromCells.Add(OtherFloorCell);
        var scenario = ToTheFight(r, r.Scenario(walk));
        for (var i = 0; i < 2400 && !first.Dead; i++)
        {
            Assert.Equal(ScenarioStatus.Running, r.Frame(scenario));
        }

        Assert.True(first.Dead);
        r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi"); // behind the wall, no line from here
        for (var i = 0; i < 40; i++)
        {
            if (i % 8 == 0)
            {
                MeleeHit(r);
            }

            Assert.Equal(ScenarioStatus.Running, r.Frame(scenario));
        }

        Assert.Empty(walk.Spots);
        Assert.Equal(1, LinesWith(r, "self-defence: no walk toward Virindi 0x80002001 (no shot from here): the tour owes storey 'z -6'"));
        Assert.Equal(0, LinesWith(r, "walking: purpose=self-defence"));
        Assert.Contains(r.Log.Lines, l => l.Contains("self-defence: no attacker is in range with a clear line", StringComparison.Ordinal)); // the tour's leg
    }
}

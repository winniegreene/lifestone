using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 item 7 F15, the fight review's fixes that run over the scripted ACE: the kill notice that names a living target's name (7-F1), the bound on a hold (7-F5) and the settled opener (7-F6). Each test names the failure it pins.
/// </summary>
public sealed partial class FightScenarioTests
{
    [Fact]
    public void A_kill_notice_naming_the_targets_name_for_a_neighbour_that_died_leaves_the_living_target_in_the_fight_M5_7_F15_7_F1()
    {
        var r = new Rig(fight: new FightOptions { Kills = 10 });
        var near = r.Add(TuskerGuid, TuskerSlave, FloorCell, 30, hitsToKill: 100); // the cast target, alive throughout
        r.Add(SecondTuskerGuid, TuskerSlave, FloorCell, 50, hitsToKill: 100);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));
        for (var i = 0; i < 60 && r.CastTargets.Count == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.NotEmpty(r.CastTargets);
        r.R.Server((p, s) => AceM3Writers.KillerNotification(p, s, "You have killed Tusker Slave!")); // the ring's kill of a neighbour of the same name
        for (var i = 0; i < 24; i++)
        {
            r.Frame(scenario);
        }

        Assert.False(near.Dead);
        Assert.Equal(0, scenario.KilledHeld); // the living target was never marked killed
        Assert.Equal(TuskerGuid, r.CastTargets[^1]); // and is cast at again
        Assert.Equal(1, LinesWith(r, "that creature lives (a neighbour of the same name died); it stays the target"));
    }

    [Fact]
    public void A_kill_notice_for_a_target_that_dies_is_confirmed_by_the_table_and_reaches_the_tour_M5_7_F15_7_F1()
    {
        var r = new Rig(); // the 0x01AD arrives before the table shows the body gone
        var slave = r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        var tour = new RecordingTour();
        var scenario = r.Scenario(new FakeWalk { Tour = tour });
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(slave.Dead);
        Assert.Equal(1, scenario.Tally.TargetKills);
        Assert.Equal([(TuskerSlave, FloorCell)], tour.Kills); // the tour was told once the table showed the body dead
        Assert.Equal(0, LinesWith(r, "that creature lives"));
    }

    [Fact]
    public void An_attacker_that_cannot_be_cast_at_is_held_only_for_the_bound_then_dropped_and_the_targets_resume_M5_7_F15_7_F5()
    {
        var r = new Rig(fight: new FightOptions { DefendHoldSeconds = 5 });
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        r.Add(VirindiGuid, Virindi, NoGoCell, 30, "Virindi", hitsToKill: 100);
        var walk = new FakeWalk { BlockedCell = NoGoCell };
        var scenario = ToTheFight(r, r.Scenario(walk));

        for (var i = 0; i < 80 && scenario.Stage == "fight"; i++) // twenty seconds, a hit every second: the forget bound (20 s) never ends the hold
        {
            if (i % 4 == 0)
            {
                MeleeHit(r);
            }

            r.Frame(scenario);
        }

        Assert.Equal(0, walk.Legs);
        Assert.NotEmpty(r.CastTargets); // before the fix the mage held for as long as the attacker kept hitting and cast nothing
        Assert.All(r.CastTargets, t => Assert.Equal(TuskerGuid, t));
        Assert.Equal(1, LinesWith(r, "self-defence: Virindi 0x80002001 could not be cast at for 5 s"));
    }

    [Fact]
    public void The_defend_hold_bound_parses_zero_to_six_hundred_and_defaults_to_thirty_M5_7_F15_7_F5()
    {
        Assert.Equal(30, new FightOptions().DefendHoldSeconds);
        var ten = FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-defend-hold-s=10"]));
        Assert.Empty(ten.Errors);
        Assert.Equal(10, ten.DefendHoldSeconds);
        Assert.Equal(0, FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-defend-hold-s=0"])).DefendHoldSeconds);
        foreach (var bad in new[] { "-1", "601", "soon" })
        {
            var parsed = FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-defend-hold-s=" + bad]));
            Assert.Single(parsed.Errors);
            Assert.Equal(30, parsed.DefendHoldSeconds);
        }

        Assert.Contains(OptionCatalog.Fight().Options, x => x.Name == "--fight-defend-hold-s");
    }

    [Fact]
    public void A_settled_opener_leaves_the_pick_to_the_war_options_M5_7_F15_7_F6()
    {
        var r = new Rig(openers: true);
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 40, hitsToKill: 6);
        r.Script(Shot.Resist, Shot.Resist, Shot.Resist); // the opener is settled by its three resists
        var scenario = r.Scenario(new FakeWalk());
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        var settled = r.Log.Lines.ToList().FindIndex(l => l.Contains("resisted after 3", StringComparison.Ordinal));
        Assert.True(settled >= 0);
        Assert.DoesNotContain(r.Log.Lines.Skip(settled), l => l.Contains("pick: Fire Vulnerability", StringComparison.Ordinal));
    }
}

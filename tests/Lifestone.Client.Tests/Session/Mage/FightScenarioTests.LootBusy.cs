using Lifestone.Client.Session.Scenarios;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 item 7, D123: "You're too busy!" is a wait, never a give-up. ACE refuses a Use while the player is busy (Player_Use.cs:199-203 @95ef426d), as it is until the last pick-up's animation ends (Player_Inventory.cs:759-787); round 12 left
/// 206 corpses for good on that answer and walked off (06:59:00 UTC: corpses 3.8 m and 2.6 m away refused, then an 84 m walk). The open is sent again after the busy wait, a bounded number of times, each logged; a corpse is left only for a
/// named, counted reason; the fight never walks off while the corpse is in hand. Over the same scripted ACE as the other loot tests.
/// </summary>
public sealed partial class FightScenarioTests
{
    [Fact]
    public void A_busy_answer_to_the_open_is_waited_out_and_the_corpse_is_opened_and_looted()
    {
        var r = new Rig(fight: LootRetryFight(kills: 1)) { Corpses = true, BusyOpens = 2 };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal([CorpseA, CorpseA, CorpseA], r.Opened); // two refused, the third opened
        Assert.Equal([CorpseA], r.Closed);
        Assert.Equal(2, LinesWith(r, "was answered busy (0x001D"));
        Assert.Equal(2, LinesWith(r, "is sent again after a busy refusal"));
        Assert.Equal(1, LinesWith(r, "(opened after 2 busy refusal(s))"));
        Assert.Equal(0, scenario.LootCorpsesLeft);
        Assert.Contains("loot=on looted=1 ", scenario.LootFields(), StringComparison.Ordinal);
        Assert.EndsWith(" corpses_opened=1 loot_left_aggro=0 loot_left_decayed=0 worth_a_look=0 stack_merges=0 loot_busy_waits=2 loot_left_busy=0", scenario.LootFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_open_answered_busy_every_time_is_sent_again_at_most_five_times_then_the_corpse_is_left_for_that_named_reason()
    {
        var r = new Rig(fight: LootRetryFight(kills: 1)) { Corpses = true, BusyOpens = 100 };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(5, FightScenario.LootBusyRetries);
        Assert.Equal(1 + FightScenario.LootBusyRetries, r.Opened.Count); // the first open and five more, never a sixth
        Assert.All(r.Opened, guid => Assert.Equal(CorpseA, guid));
        Assert.Empty(r.Closed);
        Assert.Equal(1, scenario.LootCorpsesLeft);
        Assert.Equal(1, LinesWith(r, "left: busy (0x001D \"You're too busy!\") 6 times in a row"));
        Assert.Contains("loot=on looted=0 ", scenario.LootFields(), StringComparison.Ordinal);
        Assert.EndsWith(" corpses_opened=0 loot_left_aggro=0 loot_left_decayed=0 worth_a_look=0 stack_merges=0 loot_busy_waits=5 loot_left_busy=1", scenario.LootFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_fight_does_not_walk_off_while_its_corpse_answers_busy_and_walks_only_once_the_corpse_is_looted()
    {
        var r = new Rig(fight: LootRetryFight(kills: 2)) { Corpses = true, BusyOpens = 3 };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        r.Add(SlaveB, TuskerSlave, Den, 80); // 60 m away: beyond the bolt's 30 m, so with the first tusker dead the fight has no target in range and walks
        var walk = new FakeWalk { TicksPerLeg = 2 };
        var scenario = r.Scenario(walk);

        for (var i = 0; i < 400 && walk.Legs == 0; i++)
        {
            r.Frame(scenario);
            if (walk.Legs == 0 && r.Opened.Count > 0)
            {
                Assert.True(scenario.LootBusy || r.Closed.Count > 0, "the corpse stays in hand through the busy answers"); // round 12 left it and walked 84 m
            }
        }

        Assert.Equal(1, walk.Legs);
        Assert.Equal([CorpseA, CorpseA, CorpseA, CorpseA], r.Opened); // three busy answers, then the open: all before the first leg
        Assert.Equal([CorpseA], r.Closed);
        Assert.Contains("loot=on looted=1 ", scenario.LootFields(), StringComparison.Ordinal);
        Assert.Equal(0, scenario.LootCorpsesLeft);
    }
}

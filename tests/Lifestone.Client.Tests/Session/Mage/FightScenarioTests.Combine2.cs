using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 combine2 (live 2026-11-17 13:30:46, <c>cache/m6/14/live/ladder61/step3-levelup.log</c>: after the combines she held a bar and a nugget, 12 motes' worth, and the want loot's line read "hunt-until: 0 of 16 Pyreal Mote (6353) held after
/// the corpse"): the per-corpse line reads the until's own count (the fight's <c>UntilUnits</c> over <see cref="QuestCombineChain.Units"/>), as the until check does. A combined 16 ends the hunt; the per-corpse line counts
/// the equivalents.
/// </summary>
public sealed partial class FightScenarioTests
{
    [Fact]
    public void A_combined_sixteen_held_as_two_bars_ends_the_hunt_by_its_until()
    {
        var r = CombineRig(motesHeld: 0);
        HoldStack(r, 0x8C100201, BarWcid, 1);
        HoldStack(r, 0x8C100202, BarWcid, 1);
        r.Add(SlaveA, TuskerSlave, Den, 30, hitsToKill: 1000); // a creature to fight: the until, not the lack of one, ends it
        var scenario = r.Scenario(new FakeWalk());

        var status = r.Run(scenario, 120);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.EndsWith(" until=met", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(1, LinesWith(r, "fight: until met: item 6353 held 16 of 16 (its equivalents along the no-roll combine chain)"));
        Assert.Empty(r.Combined); // a bar has no link above it in the hunt's chain
    }

    [Fact]
    public void The_per_corpse_line_counts_the_equivalents_a_bar_a_nugget_and_a_looted_mote_are_thirteen_of_sixteen()
    {
        var r = CombineRig(motesHeld: 0);
        HoldStack(r, 0x8C100201, BarWcid, 1);
        HoldStack(r, 0x8C100202, NuggetWcid, 1);
        r.Add(SlaveA, TuskerSlave, Den, 30);
        r.CorpseItems.Add((0x8C100101, PyrealMote, 0x80, 10, 1, "Pyreal Mote"));
        var scenario = r.Scenario(new FakeWalk());
        for (var i = 0; i < 600 && LinesWith(r, "held after the corpse") == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(1, LinesWith(r, "hunt-until: 13 of 16 ")); // live: "0 of 16"
        Assert.Equal(1, LinesWith(r, " equivalents held after the corpse 0x"));
        Assert.Equal(ScenarioStatus.Running, r.Frame(scenario)); // 13 of 16: the hunt goes on
    }

    [Fact]
    public void A_combine_answered_busy_is_sent_again_after_the_wait_and_made()
    {
        // Live 13:38:37: a combine 117 ms after the corpse's last pick-up was answered 0x001D (YoureTooBusy) and the combines were set aside for the fight.
        var r = CombineRig(motesHeld: 2);
        r.BusyRecipes = 1;
        var scenario = r.Scenario(new FakeWalk());
        for (var i = 0; i < 200 && scenario.Combines < 1; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(1, scenario.Combines);
        Assert.Equal(2, r.Combined.Count); // the busy one, then the one made
        Assert.Equal(1, LinesWith(r, "the server answered busy (0x001D YoureTooBusy"));
        Assert.Equal(1, LinesWith(r, "sent again in 1 s (busy 1 of 5)"));
        Assert.Equal(0, LinesWith(r, "the combines are set aside"));
        Assert.Equal(1, HeldOf(r, SliverWcid));
    }
}

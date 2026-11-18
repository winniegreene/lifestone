using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 combine (the owner's ruling, 2026-11-17: she can also combine the motes into nuggets and bars to save pack space): in a hunt-until with the quest's no-roll chain she combines every pair she holds at a safe moment (between kills, no
/// attacker on her, no hostile within the awake reach), in peace as ACE requires, each a check, act, check, and the until counts the motes' equivalents. Over the scripted ACE, which answers a recipe as ACE does for 790, 792 and 791 (both
/// consumed, one result).
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint SliverWcid = 6355, NuggetWcid = 6354, BarWcid = 6329;

    private static readonly IReadOnlyList<uint> MoteChain = [PyrealMote, SliverWcid, NuggetWcid, BarWcid];

    private static Rig CombineRig(int motesHeld)
    {
        var until = new FightUntil(PyrealMote, 16, null) { Chain = MoteChain };
        var r = new Rig(fight: LootRetryFight(kills: 1) with { Kills = FightOptions.UnlimitedKills, Want = [PyrealMote], Until = until }) { Corpses = true };
        r.Recipes[PyrealMote] = SliverWcid;
        r.Recipes[SliverWcid] = NuggetWcid;
        r.Recipes[NuggetWcid] = BarWcid;
        for (var i = 0u; i < motesHeld; i++)
        {
            HoldStack(r, 0x8C100001 + i, PyrealMote, 1); // a mote does not stack (MaxStackSize 1): one object each
        }

        return r;
    }

    private static int HeldOf(Rig r, uint wcid) => r.View.State.Self.Inventory.Pack.Count(e => r.View.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == wcid);

    [Fact]
    public void With_three_motes_held_a_fourth_looted_is_combined_up_the_chain_between_kills_and_the_until_counts_four_of_sixteen()
    {
        var r = CombineRig(motesHeld: 3);
        r.Add(SlaveA, TuskerSlave, Den, 30);
        r.CorpseItems.Add((0x8C100101, PyrealMote, 0x80, 10, 1, "Pyreal Mote"));
        var scenario = r.Scenario(new FakeWalk());
        for (var i = 0; i < 600 && scenario.Combines < 3; i++)
        {
            r.Frame(scenario);
        }

        for (var i = 0; i < 20; i++)
        {
            r.Frame(scenario); // back to the fight
        }

        // the four motes make two slivers, and the two slivers are a pair of the next link, so they make a nugget: 4 motes' worth in one slot (the rule: every pair of any no-roll link)
        Assert.Equal(3, scenario.Combines);
        Assert.Equal(3, r.Combined.Count);
        Assert.All(r.Combined, c => Assert.NotEqual(c.Source, c.Target));
        Assert.Equal((0, 0, 1), (HeldOf(r, PyrealMote), HeldOf(r, SliverWcid), HeldOf(r, NuggetWcid)));
        Assert.Equal(3, LinesWith(r, " (a no-roll link of the quest's chain): pack slots freed 1 ("));
        Assert.Equal(2, LinesWith(r, "-> Made 6355 (a no-roll link"));
        Assert.Equal(1, LinesWith(r, "-> Made 6354 (a no-roll link"));
        Assert.True(LinesWith(r, "; 4 of 16 ") >= 3, "each combine's check after counts 4 of 16 equivalents: 12 more wanted");
        Assert.Equal(1, LinesWith(r, "combine: no pair left: "));
        Assert.Equal(ScenarioStatus.Running, r.Frame(scenario)); // 4 of 16: 12 more wanted, the hunt goes on
        Assert.Equal("fight", scenario.Stage);
        var peace = r.ModesSent.IndexOf(CombatModes.NonCombat);
        Assert.True(peace >= 0 && r.ModesSent.Skip(peace).Contains(CombatModes.Magic), "she crafts in peace, then the magic mode again");
    }

    [Fact]
    public void No_combine_is_made_while_an_attacker_is_on_her()
    {
        var r = CombineRig(motesHeld: 2);
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 10, hitsToKill: 1000); // a living hostile within the awake reach the whole time
        var scenario = r.Scenario(new FakeWalk());
        for (var i = 0; i < 200; i++)
        {
            r.Frame(scenario);
        }

        Assert.Empty(r.Combined);
        Assert.Equal(0, scenario.Combines);
        Assert.Equal(2, HeldOf(r, PyrealMote));
        Assert.Equal(0, LinesWith(r, "combine: check before"));
    }
}

using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 hour load over the fight's loot step (the scripted ACE of the keep-cantrips tests): a better find over the line is taken by dropping the lowest value per burden sell loot first, each drop checked, then the find taken (the
/// owner's ruling, 2026-11-16: she is not married to something just because she picked it up); and with the session's cycle load comfortable at the top, the plan is given the hour's line and the find is taken over the base line with no drop.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint SilverPea = 0x8C0E0001;

    /// <summary>A rig whose corpse holds a Silver Pea (value 12,500, burden 50: v/b 250) and whose pack holds <see cref="SellLoot"/>, with the burden <paramref name="overTheLine"/> over the den's load line.</summary>
    private static (Rig Rig, long Line) SwapRig(int overTheLine)
    {
        var r = new Rig(fight: LootRetryFight(kills: 1)) { Corpses = true };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        r.CorpseItems.Add((SilverPea, 9401, 0x800, 12_500, 50, "Silver Pea"));
        foreach (var c in SellLoot)
        {
            var item = LootRulesTests.Item(c.Item3, c.Item4, c.Item5, c.Item1, c.Item6, c.Item2);
            r.R.H.R.Reducer.OnCreateObject(r.R.H.R.Context, item.Created with { Weenie = item.Created.Weenie with { Header = item.Created.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player } }, isUpdate: false);
            r.R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, c.Item1, WorldActionRig.Player, 0, 0));
        }

        var line = (long)LootContext.Of(r.View.State).Capacity * r.Fight.EffectiveLootThresholds.LoadLinePercent / 100;
        r.R.PrivateInt(PlayerInventory.EncumbranceProperty, (int)(line + overTheLine));
        return (r, line);
    }

    [Fact]
    public void A_better_find_over_the_line_is_swapped_in_for_the_lowest_value_per_burden_sell_loot_dropped_and_checked_first()
    {
        var (r, line) = SwapRig(0);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal([SilverEwer], r.Dropped); // at the line, 50 to fit: the 400 of v/b 1 goes; the platter (3) and the diamond (500) stay
        Assert.Contains(SilverPea, r.Picked);
        var inventory = r.View.State.Self.Inventory;
        Assert.True(inventory.Find(SilverPea).Held && inventory.Find(GoldPlatter).Held && inventory.Find(Diamond).Held);
        Assert.False(inventory.Contains(SilverEwer));
        Assert.Equal(1, LinesWith(r, "loot: swap: take 'Silver Pea' 0x8C0E0001 (value 12500, burden 50, v/b 250)"));
        Assert.Equal(1, LinesWith(r, "loot: room: dropped 'Silver Ewer' 0x8C0D0011 value 400 burden 400 v/b 1 (pack rule trade-goods: sell loot, the lowest value per burden she carries, under the find's 250) to swap for the better sell loot 'Silver Pea' 0x8C0E0001 (m6-hour-load)"));
        Assert.Equal(1, LinesWith(r, $"loot: swap made for 'Silver Pea' 0x8C0E0001 (value 12500, burden 50, v/b 250): burden {line} -> {line - 400} of "));
        Assert.Contains(" loot_swaps=1 loot_swaps_short=0", scenario.LootFields(), StringComparison.Ordinal);
        Assert.Equal(0, PackRoomMemory.For(r.View.State).LoadLeftKeepers); // sell loot: no trip is ever called for it
    }

    [Fact]
    public void With_the_cycle_load_comfortable_at_the_top_the_plan_gets_the_hours_line_and_takes_the_find_over_the_base_line_with_no_drop()
    {
        var (r, line) = SwapRig(200);
        var load = CycleLoad.For(r.View.State);
        var capacity = LootContext.Of(r.View.State).Capacity;
        load.Begin(r.Fight.GroundProfile.Key, r.Now - 3600, (int)line, capacity, new CycleLoadSettings(), topPercent: 150); // an hour in: the ramp is at the top
        load.Apply(new ComfortVerdict(ComfortKind.Comfortable, "the test's read"));
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Empty(r.Dropped);
        Assert.Contains(SilverPea, r.Picked);
        Assert.Equal(1, LinesWith(r, "hour load on: 60 min to 150%: line 150% (base 85%, start 85% = ")); // the den's own line is 85%
        Assert.Equal(150, load.HighestLine);
        Assert.Equal("rose:150", load.Field);
    }
}

using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 want room (live 2026-11-17 09:58:23 and 10:01:59, <c>cache/m6/14/live/ladder57/step3-levelup.log</c>: "loot: left Pyreal Mote value 10 burden 1 v/b 10 (floor 1, over the load line)" and "(floor 1, no free slot)": three of the six
/// motes the Atlan wand's hunt found were left while her pack held 65 pieces of sell loot at the hour-load rule's line). An item the fight's quest wants outranks every sell loot line: the plan drops the sell loot she carries with the lowest
/// value per burden first until it fits and a slot is free, logged "to make room for the wanted item"; only when nothing droppable remains is it left, by name, with an ERROR line. The sale already never offers a quest file's item
/// (<c>ResupplyStation.QuestKeepReason</c>), and the pack rules keep it (<c>quest-item</c>); there is no stash path to exempt.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint Mote = 0x8C0E0001;

    private static MageLootItem WantedMote(uint guid = Mote) => new(guid, "Pyreal Mote", 0x80, 10, 1, KeepAside: MageLootRules.QuestWantPrefix + PyrealMote, Wcid: PyrealMote);

    [Fact]
    public void A_wanted_mote_at_a_full_pack_and_at_the_load_line_is_taken_by_dropping_the_lowest_value_per_burden_sell_loot()
    {
        var rules = new MageLootRules(new MageLootThresholds { LoadLinePercent = 100 }, new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>());
        CarriedLoot platter = new(21, "Gold Platter", 3000, 300, "trade-goods"), gem = new(22, "Diamond", 5000, 10, "trade-goods"), ewer = new(23, "Silver Ewer", 4000, 200, "trade-goods");
        List<CarriedLoot> carried = [gem, platter, ewer]; // v/b 500, 10 and 20: every one worth as much a burden as the mote or more (the live case: the swap would drop none)
        MageLootItem[] corpse = [new MageLootItem(31, "Lead Pea", 0x80, 500, 10), WantedMote()];

        var before = rules.Plan("Corpse of Nubilous Golem", "Qwinnie", corpse, burden: 21_750, capacity: 21_750, freeSlots: 0); // what ran live: no sell loot offered
        var plan = rules.Plan("Corpse of Nubilous Golem", "Qwinnie", corpse, burden: 21_750, capacity: 21_750, freeSlots: 0, sellLoot: carried);

        Assert.NotEqual(MageLootVerdict.KeepAside, before.VerdictOf(Mote));
        Assert.Equal(MageLootVerdict.KeepAside, plan.VerdictOf(Mote));
        Assert.Equal(Mote, plan.Kept[0]); // ranked first of all
        var drop = Assert.Single(plan.Drops, d => d.ForGuid == Mote); // one drop frees the slot and the burden: the platter, the lowest value per burden
        Assert.Equal(21u, drop.Loot.Guid);
        Assert.True(drop.Want);
        Assert.Equal("'Gold Platter' 0x00000015 value 3000 burden 300 v/b 10 (pack rule trade-goods: sell loot, the lowest value per burden she carries) to make room for the wanted item 'Pyreal Mote' 0x8C0E0001 (m6-want-room: a quest's want outranks every sell loot line)", drop.Reason);
        Assert.Contains(plan.RoomLines(), l => l.StartsWith("loot: make room for the wanted item 'Pyreal Mote' 0x8C0E0001 (quest: want 6353; value 10, burden 1)", StringComparison.Ordinal));
        Assert.Empty(plan.Shorts);
    }

    /// <summary>A rig whose corpse holds a Pyreal Mote the fight wants and whose pack holds <paramref name="carried"/>, at the den's load line.</summary>
    private static Rig WantRig(params (uint Guid, uint Wcid, uint Type, uint Value, ushort Burden, string Name)[] carried)
    {
        var r = new Rig(fight: LootRetryFight(kills: 1) with { Want = [PyrealMote] }) { Corpses = true };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        r.CorpseItems.Add((Mote, PyrealMote, 0x80, 10, 1, "Pyreal Mote"));
        foreach (var c in carried)
        {
            var item = LootRulesTests.Item(c.Type, c.Value, c.Burden, c.Guid, c.Name, c.Wcid);
            r.R.H.R.Reducer.OnCreateObject(r.R.H.R.Context, item.Created with { Weenie = item.Created.Weenie with { Header = item.Created.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player } }, isUpdate: false);
            r.R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, c.Guid, WorldActionRig.Player, 0, 0));
        }

        var line = (long)LootContext.Of(r.View.State).Capacity * r.Fight.EffectiveLootThresholds.LoadLinePercent / 100;
        r.R.PrivateInt(PlayerInventory.EncumbranceProperty, (int)line); // at the line: the mote's 1 crosses it
        return r;
    }

    [Fact]
    public void A_wanted_mote_over_the_load_line_drops_sell_loot_and_is_taken_and_with_nothing_droppable_it_is_left_with_an_error()
    {
        var r = WantRig(SellLoot);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(r.Scenario(new FakeWalk())));

        Assert.Equal([SilverEwer], r.Dropped); // the lowest value per burden first; one frees the room
        Assert.Contains(Mote, r.Picked);
        Assert.True(r.View.State.Self.Inventory.Find(Mote).Held);
        Assert.Equal(1, LinesWith(r, "loot: room: dropping 'Silver Ewer' 0x8C0D0011 value 400 burden 400 v/b 1 (pack rule trade-goods: sell loot, the lowest value per burden she carries) to make room for the wanted item 'Pyreal Mote' 0x8C0E0001"));
        Assert.Equal(0, LinesWith(r, "left Pyreal Mote"));

        // Nothing droppable: left by name, an error.
        var bare = WantRig();
        Assert.Equal(ScenarioStatus.Succeeded, bare.Run(bare.Scenario(new FakeWalk())));
        Assert.Empty(bare.Dropped);
        Assert.DoesNotContain(Mote, bare.Picked);
        Assert.Equal(1, bare.Log.Lines.Count(l => l.Contains("ERROR", StringComparison.Ordinal) && l.Contains("loot: NO ROOM for the wanted item 'Pyreal Mote' 0x8C0E0001 (quest: want 6353; burden 1): dropping all 0 sell loot item(s) she carries", StringComparison.Ordinal)));
    }
}

using DatReaderWriter.Enums;
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
/// M6 14 keep every great cantrip (the owner, 2026-11-15): the loot step makes room for a great-cantrip piece on the same corpse visit, before the take. Live 10:08:39 (ladder29): Studded Leather Leggings with Major Life Magic Aptitude,
/// burden 726, left at 17,664 of 17,850 over the load line. Here the scripted ACE takes her drops (019A, the burden lowered by the item's), answers one "You're too busy!" or refuses one outright; each drop is checked after, and the
/// piece is taken only when it fits; else it is left loudly and the full-pack trip is due.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint MajorLifeSpell = 2520; // ACE's id: the useful-cantrip judge reads what it raises (Life Magic) from the spell table
    private const uint Leggings = 0x8C0D0001, SilverEwer = 0x8C0D0011, GoldPlatter = 0x8C0D0012, Diamond = 0x8C0D0013;

    /// <summary>
    /// A rig whose corpse holds the leggings (a Major Life Magic Aptitude its appraisal names) and whose pack holds <paramref name="carried"/>, with the burden <paramref name="underTheLine"/> under the den's load line (the 10:08 case: 186).
    /// </summary>
    private static (Rig Rig, long Line) CantripRig(int underTheLine, params (uint Guid, uint Wcid, uint Type, uint Value, ushort Burden, string Name)[] carried)
    {
        var r = new Rig(fight: LootRetryFight(kills: 1)) { Corpses = true };
        r.Table[MajorLifeSpell] = WorldActionRig.Spell(MajorLifeSpell, SpellType.Enchantment) with { Name = "Major Life Magic Aptitude" };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        r.CorpseItems.Add((Leggings, 9301, 0x2, 8753, 726, "Studded Leather Leggings"));
        r.AppraisedSpells[Leggings] = [MajorLifeSpell];
        foreach (var c in carried)
        {
            var item = LootRulesTests.Item(c.Type, c.Value, c.Burden, c.Guid, c.Name, c.Wcid);
            r.R.H.R.Reducer.OnCreateObject(r.R.H.R.Context, item.Created with { Weenie = item.Created.Weenie with { Header = item.Created.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player } }, isUpdate: false);
            r.R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, c.Guid, WorldActionRig.Player, 0, 0));
        }

        var percent = r.Fight.EffectiveLootThresholds.LoadLinePercent;
        Assert.True(percent > 0, "the den's loot has a load line");
        var line = (long)LootContext.Of(r.View.State).Capacity * percent / 100;
        r.R.PrivateInt(PlayerInventory.EncumbranceProperty, (int)(line - underTheLine));
        return (r, line);
    }

    private static readonly (uint, uint, uint, uint, ushort, string)[] SellLoot =
    [
        (GoldPlatter, 9312, 0x80, 900, 300, "Gold Platter"), // v/b 3
        (Diamond, 9313, 0x800, 5000, 10, "Diamond"), // v/b 500
        (SilverEwer, 9311, 0x80, 400, 400, "Silver Ewer"), // v/b 1
    ];

    [Fact]
    public void A_great_cantrip_piece_over_the_load_line_is_taken_after_the_lowest_value_per_burden_sell_loot_is_dropped_and_checked()
    {
        var (r, line) = CantripRig(186, SellLoot);
        Assert.Equal(3, CantripRoom.SellLoot(r.View.State, CantripRoomTests.Book(r.View.State, r.Fight.GroundProfile, r.Fight.EffectiveLootThresholds.KeepAside, new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>())).Count); // the den's home buyers take all three
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal([SilverEwer, GoldPlatter], r.Dropped); // 186 under the line, 726 to fit: the 400 (v/b 1), then the 300 (v/b 3); the diamond stays
        Assert.Contains(Leggings, r.Picked);
        var inventory = r.View.State.Self.Inventory;
        Assert.True(inventory.Find(Leggings).Held);
        Assert.True(inventory.Find(Diamond).Held);
        Assert.False(inventory.Contains(SilverEwer) || inventory.Contains(GoldPlatter));
        Assert.Equal((int)(line - 186 - 700), inventory.Burden); // the rig lowers the burden by each drop's (its pick-ups add none)
        Assert.Equal(1, LinesWith(r, "loot: make room for the great cantrip 'Studded Leather Leggings' 0x8C0D0001 (Major Life Magic Aptitude; value 8753, burden 726)"));
        Assert.Equal(1, LinesWith(r, "loot: room: dropping 'Silver Ewer' 0x8C0D0011 value 400 burden 400 v/b 1 (pack rule trade-goods"));
        Assert.Equal(1, LinesWith(r, $"loot: room: dropped 'Silver Ewer' 0x8C0D0011 value 400 burden 400 v/b 1 (pack rule trade-goods: sell loot, the lowest value per burden she carries) to make room for the great cantrip 'Studded Leather Leggings' 0x8C0D0001: Confirmed: dropped; check after: gone from her pack, burden {line - 186} -> {line - 586} of "));
        Assert.Equal(1, LinesWith(r, "loot: room: dropped 'Gold Platter' 0x8C0D0012"));
        Assert.Equal(1, LinesWith(r, $"loot: room made for the great cantrip 'Studded Leather Leggings' 0x8C0D0001 (Major Life Magic Aptitude; value 8753, burden 726): burden {line - 186} -> {line - 886} of "));
        Assert.Equal(1, LinesWith(r, "loot: the great cantrip 'Studded Leather Leggings' 0x8C0D0001 (Major Life Magic Aptitude) is in her pack (check after the take)"));
        var lines = r.Log.Lines.ToList();
        Assert.True(lines.FindIndex(l => l.Contains("loot: room made for", StringComparison.Ordinal)) < lines.FindIndex(l => l.Contains("loot: taking 'Studded Leather Leggings'", StringComparison.Ordinal)), "the room is made before the take");
        Assert.Contains(" great_cantrips_taken=1 great_cantrips_left=0 loot_room_drops=2 loot_room_dropped_value=1300", scenario.LootFields(), StringComparison.Ordinal);
        Assert.Equal(0, PackRoomMemory.For(r.View.State).LoadLeftKeepers); // kept: no trip is called for it
    }

    [Fact]
    public void A_drop_answered_busy_is_sent_again_after_the_wait_and_the_piece_is_still_taken()
    {
        var (r, _) = CantripRig(186, SellLoot);
        r.BusyDrops = 1;
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal([SilverEwer, SilverEwer, GoldPlatter], r.Dropped);
        Assert.Equal(1, LinesWith(r, "loot: room: the drop of 'Silver Ewer' 0x8C0D0011 was answered busy (0x001D \"You're too busy!\""));
        Assert.True(r.View.State.Self.Inventory.Find(Leggings).Held);
        Assert.Contains(" loot_busy_waits=1 ", scenario.LootFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_drop_refused_outright_ends_the_drops_and_the_piece_that_still_does_not_fit_is_left_loudly_with_the_trip_due()
    {
        var (r, _) = CantripRig(186, SellLoot);
        r.RefuseDrops.Add(SilverEwer);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal([SilverEwer], r.Dropped); // no more drops for the corpse once one is refused
        Assert.DoesNotContain(Leggings, r.Picked); // the check after the drops: it does not fit, so it is not taken over the line
        Assert.Equal(1, LinesWith(r, "loot: room: the drop of 'Silver Ewer' 0x8C0D0011 ended ServerError"));
        Assert.Equal(1, LinesWith(r, "loot: room NOT made for the great cantrip 'Studded Leather Leggings' 0x8C0D0001 (Major Life Magic Aptitude; burden 726): over the load line when read again"));
        Assert.Equal(1, PackRoomMemory.For(r.View.State).LoadLeftKeepers); // the full-pack trip is due at once
        Assert.Contains(" great_cantrips_taken=0 great_cantrips_left=1 loot_room_drops=0 ", scenario.LootFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void When_all_her_sell_loot_cannot_make_room_nothing_is_dropped_the_pack_is_named_and_the_trip_is_due()
    {
        var (r, _) = CantripRig(5, (Diamond, 9313, 0x800, 5000, 10, "Diamond"));
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Empty(r.Dropped);
        Assert.DoesNotContain(Leggings, r.Picked);
        Assert.Equal(1, LinesWith(r, "loot: NO ROOM for the great cantrip 'Studded Leather Leggings' 0x8C0D0001 (Major Life Magic Aptitude; burden 726): dropping all 1 sell loot item(s) she carries (burden 10, value 5000) cannot make room"));
        Assert.Equal(1, LinesWith(r, "loot: the pack holds: burden "));
        Assert.Equal(1, LinesWith(r, "sell loot she may drop 1 item(s) burden 10"));
        Assert.Equal(1, PackRoomMemory.For(r.View.State).LoadLeftKeepers);
        Assert.Contains(" great_cantrips_taken=0 great_cantrips_left=1 loot_room_drops=0 ", scenario.LootFields(), StringComparison.Ordinal);
    }
}

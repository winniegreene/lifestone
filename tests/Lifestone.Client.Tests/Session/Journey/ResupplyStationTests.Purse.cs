using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 14 (m6-purse; live 2026-11-12, a level 39 mage homed at Holtburg): the gems and the jewellery of a full pack are sold to Monyra the Jeweler from every Holtburg profile, and the learn trip by the Town Network reads the
/// whole purse (the coin and the trade notes a buy changes at the scrivener's counter) and the pack before it leaves (<see cref="TownTripPurse"/>).
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const uint GemType = 0x800;
    private const uint ScrivenerMask = 0x42080; // MerchandiseItemTypes of every scrivener (TownTripPurse.ScrivenersTakeNotes): Writable, Service and PromissoryNote

    [Fact]
    public void M6_14_purse_the_Cave_s_trip_sells_the_gems_and_the_jewellery_to_the_Jeweler_and_they_leave_the_pack()
    {
        var home = new Home("tusker-cave", 40_000, notes: false);
        var monyra = home.Ground.VendorOf(716)!;
        var loot = new[]
        {
            home.Own("Ring", 297, JewelryType, 1253),
            home.Own("Bracelet", 295, JewelryType, 980),
            home.Own("Necklace", 622, JewelryType, 1100),
            home.Own("Malachite", 2416, GemType, 441),
            home.Own("Azurite", 2414, GemType, 310),
        };
        var run = home.Start(new ResupplyOptions(true));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run, 800));

        var dump = string.Join(" | ", home.Lines.Where(l => l.Contains("sale", StringComparison.Ordinal)));
        Assert.True(home.Logged("pack items for the loot buyers Monyra the Jeweler (5, mask 0x00040808)"), dump);
        Assert.True(home.Logged("5 of 5 items sold to Monyra the Jeweler"), dump);
        Assert.Equal(5, run.Sold);
        Assert.All(loot, g => Assert.False(home.Vendor.State.Self.Inventory.Contains(g)));
        Assert.DoesNotContain(home.Lines, l => l.Contains("is outside", StringComparison.Ordinal)); // the live line: "Malachite (ItemType 0x800) is outside Shopkeeper Renald the Elder's mask: kept"
        Assert.Contains(home.Travel.Requests, r => r.Destination.Name == monyra.Name);
    }

    [Fact]
    public void M6_14_purse_every_Holtburg_profile_with_vendors_lists_the_Jeweler_as_the_outskirts_row()
    {
        var row = GroundCatalog.Get("holtburg-outskirts").VendorOf(716)!;
        var homed = GroundCatalog.All.Where(g => g.Home.Town == "Holtburg" && g.Vendors.Count > 0).ToList();
        Assert.Equal(16, homed.Count); // M6 14 armory: the Tusker Armory, the sixteenth
        foreach (var ground in homed)
        {
            var here = ground.VendorOf(716);
            Assert.True(here is not null, ground.Key);
            Assert.Equal(row, here! with { Sells = row.Sells }); // the row verbatim: guid, cell, point, stop cell, mask, rate
            Assert.Empty(here.Sells); // a loot buyer only
            Assert.NotNull(ResupplyPlanner.LootBuyer(ground, [JewelryType]));
            Assert.Equal("Monyra the Jeweler", ResupplyPlanner.LootBuyer(ground, [JewelryType, GemType])!.Name);
        }
    }

    [Fact]
    public void M6_14_purse_the_live_purse_pays_four_level_VI_scrolls_with_three_notes_changed_at_the_counter()
    {
        // 40,869 in coin and about 870,000 in notes; four scrolls at 50,000 in Stonehold. The buys as the counter makes them: 9,131 short takes a 10,000, 49,131 short the 100,000, the third is paid in coin, the fourth takes a 250,000.
        TownTripPurse.Note[] notes = [new("Trade Note (250,000)", 250_000, 3), new("Trade Note (100,000)", 100_000, 1), new("Trade Note (10,000)", 10_000, 2)];
        TownTripPurse.Line[] lines = [new("Flame Bolt VI", 50_000), new("Armor Self VI", 50_000), new("Heal Self VI", 50_000), new("Blade Protection Self VI", 50_000)];

        var verdict = TownTripPurse.Judge(40_869, notes, vendorTakesNotes: true, freeMain: 60, freeAll: 90, lines);

        Assert.True(verdict.Goes);
        Assert.Equal(("none", 870_000L, 200_000L, 200_000L, 3), (verdict.Reason, verdict.NotesValue, verdict.Cost, verdict.Pays, verdict.NotesChanged));
        Assert.Equal([0, 1, 2, 3], verdict.Buys);
        Assert.Empty(verdict.Waits);
        Assert.Equal("the coin 40869 and the notes 870000 (6 held) cover the scrolls 200000; 3 note(s) are changed at the counter, the first Trade Note (10,000) needs 2 free slot(s) (1 coin stack(s) of 25000 for its payout and 1 for the split from its stack of 2); 90 are free in her packs (60 in the main pack)", verdict.Why);

        // the coin alone, as the check read it before: refused, and the notes of a vendor that takes none are not counted
        var coinOnly = TownTripPurse.Judge(40_869, notes, vendorTakesNotes: false, freeMain: 60, freeAll: 90, lines);
        Assert.Equal((false, "purse-short"), (coinOnly.Goes, coinOnly.Reason));
        Assert.Contains("the vendor takes no trade note: the notes are not counted", coinOnly.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_14_purse_a_purse_short_of_the_whole_town_buys_the_cheapest_scrolls_and_names_the_ones_that_wait()
    {
        TownTripPurse.Line[] lines = [new("Flame Bolt VI", 50_000), new("Lifestone Recall", 5_000), new("Armor Self VI", 50_000), new("Heal Self V", 10_000)];

        var verdict = TownTripPurse.Judge(40_869, [new("Trade Note (50,000)", 50_000, 1)], true, freeMain: 10, freeAll: 10, lines);

        Assert.True(verdict.Goes);
        Assert.Equal([1, 3, 0], verdict.Buys); // cheapest first: 5,000, 10,000, then one of the two at 50,000 (the plan's first)
        Assert.Equal([2], verdict.Waits);
        Assert.Equal((115_000L, 65_000L, 1), (verdict.Cost, verdict.Pays, verdict.NotesChanged));
        Assert.Contains("cover 65000 of the scrolls 115000: buys Lifestone Recall, Heal Self V, Flame Bolt VI (cheapest first); Armor Self VI wait(s) for the purse", verdict.Why, StringComparison.Ordinal);

        var none = TownTripPurse.Judge(4_999, [], true, 10, 10, lines);
        Assert.Equal((false, "purse-short", 0), (none.Goes, none.Reason, none.Buys.Count));
        Assert.Equal("the coin 4999 and the notes 0 (0 held) do not cover the cheapest of the scrolls (Lifestone Recall at 5000; 115000 in all): no trip to this town; nothing is remembered as refused", none.Why);
    }

    [Fact]
    public void M6_14_purse_a_full_pack_is_named_before_the_trip_the_scroll_s_slot_the_split_s_and_the_payout_s_coin_stacks()
    {
        TownTripPurse.Line[] lines = [new("Flame Bolt VI", 50_000)];
        TownTripPurse.Note[] stack = [new("Trade Note (250,000)", 250_000, 3)];

        // no free slot of the main pack: the scroll cannot be bought and one note cannot be split off its stack, whatever the side packs hold
        var full = TownTripPurse.Judge(40_869, stack, true, freeMain: 0, freeAll: 40, lines);
        Assert.Equal((false, "pack-full"), (full.Goes, full.Reason));
        Assert.Contains("the main pack has no free slot: a scroll bought needs one, and so does a note split off its stack: no trip until the pack has room", full.Why, StringComparison.Ordinal);

        // the coin pays: no note is changed, one free slot is enough
        Assert.True(TownTripPurse.Judge(60_000, stack, true, 1, 1, lines).Goes);

        // a 250,000 note pays out ten coin stacks, which ACE counts against the free slots of all her packs before it takes the note (Player_Commerce.cs 176-191), and the split takes one more
        var payout = TownTripPurse.Judge(40_869, stack, true, freeMain: 4, freeAll: 10, lines);
        Assert.Equal((false, "pack-full", 1), (payout.Goes, payout.Reason, payout.NotesChanged));
        Assert.Contains("the first note to change, Trade Note (250,000) needs 11 free slot(s) (10 coin stack(s) of 25000 for its payout and 1 for the split from its stack of 3), and 10 are free in her packs (4 in the main pack): ACE would refuse the sale", payout.Why, StringComparison.Ordinal);
        Assert.True(TownTripPurse.Judge(40_869, stack, true, freeMain: 4, freeAll: 11, lines).Goes);
        Assert.True(TownTripPurse.Judge(40_869, [new("Trade Note (250,000)", 250_000, 1)], true, freeMain: 4, freeAll: 10, lines).Goes); // a single note needs no split
    }

    [Theory]
    [InlineData(9_131, 2)] // the smallest face that covers it
    [InlineData(10_000, 2)]
    [InlineData(49_131, 1)]
    [InlineData(100_001, 0)]
    [InlineData(900_000, 0)] // none covers it: the largest
    public void M6_14_purse_the_note_changed_is_the_smallest_that_covers_the_shortfall_else_the_largest(long shortfall, int picked)
    {
        Assert.Equal(picked, TownTripPurse.Pick([250_000, 100_000, 10_000], shortfall));
        Assert.Equal(-1, TownTripPurse.Pick([], shortfall));
    }

    [Fact]
    public void M6_14_purse_a_note_is_changed_at_a_scrivener_s_counter_when_the_coin_is_short_of_the_scroll()
    {
        // The counter's side of the learn trip, at the home's scrivener (the Town Network trip buys with the same step, BuyLine): 100 in coin, one Trade Note (10,000), the Flame Bolt II scroll at 250.
        var home = new Home(Outskirts, 100, notes: false, description: Climber(56));
        uint row = 0x80009000;
        foreach (var scrivener in home.Ground.Scriveners)
        {
            home.Vendor.PlaceAt(scrivener.Guid, scrivener.Name, scrivener.Cell, scrivener.Point, 3f, scrivener.Wcid);
            var shop = home.Shops.Add(scrivener.Guid, ScrivenerMask);
            foreach (var ware in scrivener.Sells)
            {
                shop.Rows.Add((-1, ++row, ware.Name, ware.Wcid, WritableType, ware.Price));
            }
        }

        var note = home.Own("Trade Note (10,000)", 2625, VendorStock.PromissoryNoteType, 10_000);
        var uses = new List<uint>();
        var run = home.Start(new ResupplyOptions(Memory: new SaleMemory()));

        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: FlameBoltServer(home, uses)));

        var dump = string.Join(" | ", home.Lines.Where(l => l.Contains("purse", StringComparison.Ordinal) || l.Contains("learn", StringComparison.Ordinal) || l.Contains("refused", StringComparison.Ordinal)));
        Assert.True(home.Logged("purse: changed Trade Note (10,000) to coin at Apprentice Scrivener of Item and War Magic: paid 10000 pyreals (the coin 100 -> 10100)"), dump);
        Assert.Equal((1, 1, 1), (run.NotesSold, run.ScrollsBought, run.Learned));
        Assert.False(home.Vendor.State.Self.Inventory.Contains(note));
        Assert.Equal(100 + 10_000 - 250, home.Vendor.State.Self.Qualities.CoinValue);
        Assert.True(home.Vendor.State.Self.Spellbook.Knows(FlameBoltII));
        Assert.False(home.Logged("purse: short"));
    }

    [Fact]
    public void M6_14_purse_the_town_trip_leaves_when_the_coin_and_the_notes_cover_the_scroll()
    {
        var home = OutskirtsHome(warRanks: 200, coins: 1_000, notes: false); // Flame Bolt V at Dryreach's Master Scrivener: 10,000
        home.Own("Trade Note (10,000)", 2625, VendorStock.PromissoryNoteType, 10_000);
        var memory = new SaleMemory();
        var run = home.Start(new ResupplyOptions(Memory: memory));

        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: home.Shops.Pump));

        var dump = string.Join(" | ", home.Lines.Where(l => l.Contains("learn", StringComparison.Ordinal)));
        Assert.True(home.Logged("learn: town trip: check before: to Dryreach for Flame Bolt V at Master Scrivener of War Magic; hops holtburg-to-network,network-to-dryreach; home by portals:dryreach; the coin 1000 and the notes 10000 (1 held) cover the scrolls 10000; 1 note(s) are changed at the counter, the first Trade Note (10,000) needs 1 free slot(s) (1 coin stack(s) of 25000 for its payout)"), dump);
        Assert.Contains(home.Lines, l => l.Contains("learn_trip_hop: n=1 edge=holtburg-to-network", StringComparison.Ordinal)); // the trip left: the rig holds no portal, so the hop is the named stop, as before the purse rule
        Assert.DoesNotContain(run.LearnRefusals, r => r.EndsWith(":purse-short", StringComparison.Ordinal));
        Assert.DoesNotContain(home.Lines, l => l.Contains("reason=purse-short", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_14_purse_a_full_main_pack_is_named_before_the_town_trip_leaves_and_nothing_is_remembered()
    {
        var home = OutskirtsHome(warRanks: 200, coins: 1_000, notes: false);
        home.Own("Trade Note (10,000)", 2625, VendorStock.PromissoryNoteType, 20_000, stack: 2, maxStack: NoteMax);
        FillPackTo(home, 0);
        var memory = new SaleMemory();
        var run = home.Start(new ResupplyOptions(Memory: memory));

        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: home.Shops.Pump));

        var dump = string.Join(" | ", home.Lines.Where(l => l.Contains("learn", StringComparison.Ordinal)));
        Assert.True(home.Logged("the coin 1000 and the notes 20000 (2 held) cover 10000 of the scrolls 10000, but the main pack has no free slot: a scroll bought needs one, and so does a note split off its stack: no trip until the pack has room"), dump);
        Assert.True(home.Logged("learn_trip: town=Dryreach vendors=20223 spells=84 hops=holtburg-to-network,network-to-dryreach travel=town-network bought=0 learned=0 home=portals:dryreach ok=no reason=pack-full"), dump);
        Assert.True(home.Logged("learn: town trip: recovery: no trip to Dryreach (pack-full)"), dump);
        Assert.DoesNotContain(home.Lines, l => l.Contains("learn_trip_hop:", StringComparison.Ordinal)); // she did not leave
        Assert.Contains("84:pack-full", run.LearnRefusals);
        Assert.False(memory.LearnRefused.ContainsKey(84)); // the next trip checks again
    }

    [Fact]
    public void M6_14_purse_a_first_town_the_purse_cannot_pay_does_not_block_the_next_town_s_scrolls_on_the_same_trip()
    {
        // War Magic 295 plans Flame Bolt VI and Life Magic 250 the level V lines: Stonehold's Grand Masters (50,000 a scroll) come first in the plan, Dryreach's Master Scrivener (10,000) second. The home's scriveners are not placed, so the coin is the trip's.
        var description = Climber(245) with
        {
            Skills = [new Lifestone.Net.Messages.SkillDesc(MageTemplate.WarMagic, 245, 1, 2u, 0, 0u, 0, 0), new Lifestone.Net.Messages.SkillDesc(MageTemplate.LifeMagic, 200, 1, 2u, 0, 0u, 0, 0)],
        };
        var home = new Home(Outskirts, 25_000, notes: false, description: description);
        var memory = new SaleMemory();
        var run = home.Start(new ResupplyOptions(Memory: memory));

        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 900, pump: home.Shops.Pump));

        var dump = string.Join(" | ", home.Lines.Where(l => l.Contains("town trip", StringComparison.Ordinal) || l.Contains("learn_trip", StringComparison.Ordinal)));
        Assert.True(home.Logged("check before: to Stonehold for Flame Bolt VI, Drain Health Other VI, Heal Self VI at Grand Master Scrivener of War Magic, Grand Master Scrivener of Life Magic; hops holtburg-to-network,network-to-stonehold; home by portals:stonehold; the coin 25000 and the notes 0 (0 held) do not cover the cheapest of the scrolls (Flame Bolt VI at 50000; 150000 in all): no trip to this town"), dump);
        Assert.True(home.Logged("learn_trip: town=Stonehold vendors=20224+20220 spells=85+1242+1161 hops=holtburg-to-network,network-to-stonehold travel=town-network bought=0 learned=0 home=portals:stonehold ok=no reason=purse-short"), dump);
        Assert.True(home.Logged("learn: town trip: recovery: no trip to Stonehold (purse-short); the plan's next town is tried, and the next trip checks Stonehold again"), dump);

        // the same trip goes on to Dryreach and buys what the purse pays there, cheapest first; the two it cannot pay wait by name
        Assert.True(home.Logged("check before: to Dryreach for Armor Self V, Drain Health Other V, Heal Self V, Mana Renewal Self V at Master Scrivener of Life Magic; hops holtburg-to-network,network-to-dryreach; home by portals:dryreach; the coin 25000 and the notes 0 (0 held) cover 20000 of the scrolls 40000: buys Armor Self V, Drain Health Other V (cheapest first); Heal Self V, Mana Renewal Self V wait(s) for the purse; the coin pays: no note is changed"), dump);
        Assert.True(home.Logged("learn: town trip: Heal Self V (1160) waits: the purse pays 20000 of Dryreach's 40000 and its scroll costs 10000"), dump);
        Assert.Contains(home.Lines, l => l.Contains("learn_trip_hop: n=1 edge=holtburg-to-network", StringComparison.Ordinal)); // she left for Dryreach (the rig holds no portal: the hop is the named stop)
        Assert.True(home.Logged("learn_trip: town=Dryreach vendors=20219 spells=1311+1241 hops=holtburg-to-network,network-to-dryreach travel=town-network bought=0 learned=0 home=portals:dryreach ok=no reason=hop:"), dump);
        Assert.Equal(["85:purse-short", "1242:purse-short", "1161:purse-short", "1160:purse-short", "216:purse-short"], run.LearnRefusals.Where(r => r.EndsWith(":purse-short", StringComparison.Ordinal)));
        Assert.All(new uint[] { 85, 1242, 1161, 1160, 216 }, id => Assert.False(memory.LearnRefused.ContainsKey(id))); // a purse refusal is not remembered: the next trip checks again
    }
}

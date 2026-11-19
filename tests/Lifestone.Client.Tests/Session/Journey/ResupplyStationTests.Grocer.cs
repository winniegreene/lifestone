using System.Numerics;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 14 (m6-grocer; the owner, 2026-11-13: sell the empty flasks): each potion she drinks leaves an Empty Flask (wcid 7940, ItemType 0x400000 CraftCookingBase, Value 1, MaxStackSize 1: a pack slot each, about 13 an
/// hour), and the supply trip kept every one, "Empty Flask (ItemType 0x400000) is outside Monyra the Jeweler's mask: kept" (live 2026-11-13 04:54 UTC): no Holtburg buyer of the profiles took the type. Fispur Ansel the
/// Grocer (714, mask 0x446220) is now a row of every Holtburg profile that lists Monyra, after Renald, and the sale's drink rule (<c>m6-sale-keeps-drinks</c>) still keeps her potions and tinctures from him.
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const uint GrocerWcid = 714;
    private const uint EmptyFlaskWcid = 7940;
    private const uint CraftCookingBaseType = 0x400000;
    private const uint FoodType = 0x20;

    /// <summary>
    /// The live case: 13 Empty Flasks in the pack, sold to the Grocer, and the drinks she uses kept. The drinks are Misc 0x80 in the world database (377, 379, 27319, 27322), outside his mask; the second case types them Food 0x20,
    /// inside it, and the drink rule keeps them all the same.
    /// </summary>
    [Theory]
    [InlineData(MiscType)]
    [InlineData(FoodType)]
    public void M6_14_grocer_the_live_case_sells_13_Empty_Flasks_to_the_Grocer_and_keeps_the_Mana_Potion_and_the_tinctures(uint drinkType)
    {
        var home = new Home("tusker-cave", 40_000, notes: false);
        var grocer = home.Ground.VendorOf(GrocerWcid)!;
        var flasks = Enumerable.Range(0, 13).Select(_ => home.Own("Empty Flask", EmptyFlaskWcid, CraftCookingBaseType, 1)).ToList();
        var drinks = new[]
        {
            home.Own("Mana Potion", FighterKit.ManaPotionWcid, drinkType, 170),
            home.Own("Potion of Healing", FighterKit.PotionWcid, drinkType, 170),
            home.Own("Health Tincture", HealthDrinks.TinctureWcid, drinkType, 500, stack: 12, maxStack: 100),
            home.Own("Mana Tincture", ManaDrinks.TinctureWcid, drinkType, 500, stack: 30, maxStack: 100),
        };
        var run = home.Start(new ResupplyOptions(true));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run, 800));

        var dump = string.Join(" | ", home.Lines.Where(l => l.Contains("sale", StringComparison.Ordinal) || l.Contains("sold", StringComparison.Ordinal)).Select(l => l.Length > 240 ? l[..240] : l));
        Assert.True(home.Logged("pack items for the loot buyers Fispur Ansel the Grocer (13, mask 0x00446220)"), dump);
        Assert.True(home.Logged("13 of 13 items sold to Fispur Ansel the Grocer"), dump);
        Assert.Equal(13, run.Sold);
        Assert.All(flasks, g => Assert.False(home.Vendor.State.Self.Inventory.Contains(g)));
        Assert.All(drinks, g => Assert.True(home.Vendor.State.Self.Inventory.Contains(g), dump));
        Assert.Contains(home.Lines, l => l.Contains("is a drink she uses (health, mana or stamina): kept, not offered", StringComparison.Ordinal));
        Assert.DoesNotContain(home.Lines, l => l.Contains("is outside", StringComparison.Ordinal)); // the live line: "Empty Flask (ItemType 0x400000) is outside Monyra the Jeweler's mask: kept"

        // The walk to him: his shop, the stop cell that is his own (he is in view here, so the request is his body within his use radius; the request when he is not yet seen is VendorStopConfirmDatTests' walk).
        var request = Assert.Single(home.Travel.Requests, r => r.Destination.Name == grocer.Name);
        Assert.Equal((0xA9B4016Au, grocer.Guid, TravelPurpose.Vendor), (request.Destination.Location.Position.CellId, request.ObjectGuid, request.Purpose));
    }

    [Fact]
    public void M6_14_grocer_every_Holtburg_profile_that_lists_the_Jeweler_lists_the_Grocer_as_the_outskirts_row_after_Renald()
    {
        var row = GroundCatalog.Get("holtburg-outskirts").VendorOf(GrocerWcid)!;
        Assert.Equal(("Fispur Ansel the Grocer", 0x7A9B4024u, 0xA9B4016Au, 0xA9B4016Au, 0x00446220u, 0.9), (row.Name, row.Guid, row.Cell, row.StopCell, row.BuysItemTypes, row.BuyPrice)); // his landblock_instance row, int 74 and float 37
        Assert.InRange(Vector3.Distance(row.Point, new Vector3(78.9714f, 38.5313f, 94.005f)), 0f, 0.001f);
        Assert.Null(row.StopPoint); // his stop is his own cell (the dat tests walk it)
        Assert.Empty(row.Sells); // a loot buyer only

        var homed = GroundCatalog.All.Where(g => g.Home.Town == "Holtburg" && g.VendorOf(716) is not null).ToList();
        Assert.Equal(18, homed.Count); // M6 14 armory: the Tusker Armory, the sixteenth; M6 redoubt: the Holtburg Redoubt, the seventeenth; M6 retreat: the Tusker Retreat, the eighteenth
        foreach (var ground in homed)
        {
            var here = ground.VendorOf(GrocerWcid);
            Assert.True(here is not null, ground.Key);
            Assert.Equal(row, here! with { Sells = row.Sells }); // the row verbatim: guid, cell, point, stop cell, mask, rate
            Assert.Empty(here.Sells);
            var order = ground.Vendors.Select(v => v.Wcid).ToList();
            Assert.True(order.IndexOf(GrocerWcid) > order.IndexOf(718), ground.Key); // after Renald

            var renald = ground.VendorOf(718)!;
            Assert.Equal([GrocerWcid], ground.Vendors.Where(v => v.Buys(CraftCookingBaseType)).Select(v => v.Wcid)); // the Empty Flask's only buyer
            Assert.Equal(0u, here.BuysItemTypes & ~(CraftCookingBaseType | renald.BuysItemTypes)); // every other type he takes, Renald takes
            Assert.Equal(0u, here.BuysItemTypes & MiscType); // the drinks' type is outside his mask
            Assert.Equal("Fispur Ansel the Grocer", ResupplyPlanner.LootBuyer(ground, [CraftCookingBaseType])!.Name);
            Assert.Equal(renald.Name, ResupplyPlanner.LootBuyers(ground, [FoodType])[0].Vendor.Name); // a tie of the counts stays Renald's, as before the row
            Assert.Equal([("Fispur Ansel the Grocer", 13), ("Monyra the Jeweler", 2)], ResupplyPlanner.LootBuyers(ground, [.. Enumerable.Repeat(CraftCookingBaseType, 13), JewelryType, GemType]).Select(b => (b.Vendor.Name, b.Count))); // the flasks to him, the jewel and the gem to her
        }
    }
}

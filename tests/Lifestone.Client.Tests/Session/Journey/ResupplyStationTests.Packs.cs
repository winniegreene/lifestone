using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>M5 7 D121: the sale reads the main pack only: a side pack's items (the kit's gear, the review finds) are never offered, whatever they are worth.</summary>
public sealed partial class ResupplyStationTests
{
    [Fact]
    public void The_sale_offers_the_main_packs_loot_only_and_never_a_side_packs_item()
    {
        var home = new Home(Den, 1000);
        var shirt = home.Own("Shirt", 130, ClothingType, 100);
        var pack = home.Vendor.NextGuid();
        home.Vendor.Arrive(pack, "Pack", 136, 0x200, 65);
        home.Vendor.R.Raw(AceM3Writers.PutObjInContainer(VendorRig.Player, 9_001, pack, VendorRig.Player, 0, 1)); // a side pack (ContainerType 1)
        var robe = home.Vendor.NextGuid();
        home.Vendor.Arrive(robe, "Spare Robe", 9_998, ClothingType, 500);
        home.Vendor.Put(robe, pack);
        var run = home.Start(new ResupplyOptions(true, () => [shirt, robe]));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        Assert.True(home.Logged("(0 lines to buy, 1 items to sell)"));
        Assert.Equal(1, run.Sold);
        Assert.False(home.Vendor.State.Self.Inventory.Contains(shirt));
        Assert.True(home.Vendor.State.Self.Inventory.Contains(robe)); // in the side pack: not loot to sell
    }

    /// <summary>M6 8 D (<c>m5-side-packs-use</c>): the fresh climber holds the starter Sack alone (starterGear.json: the Jump skill's gear), so the layout is two short; a trip that happens buys them at the vendor whose sells lists the Pack, at the profile's price, and logs why.</summary>
    [Theory]
    [InlineData(Den, 1, 2)]
    [InlineData(Den, 0, 3)]
    [InlineData(Temple, 2, 1)]
    public void M6_8_D_a_trip_buys_the_side_packs_the_layout_lacks_at_the_home_vendor_and_logs_the_reason(string key, int held, int lacks)
    {
        var home = new Home(key, 100_000, sidePacks: held);
        var vendor = home.Ground.VendorSelling(136)!;
        var price = home.Ground.PriceOf(136)!.Value;
        var run = home.Start();

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        var buy = Assert.Single(run.BuyList);
        Assert.Equal((136u, vendor, lacks, held), (buy.Line.Wcid, buy.Vendor, buy.Count, buy.Held));
        Assert.True(home.Logged($"resupply: packs: {held} side packs held, the layout wants 3: buys {lacks} Pack at {vendor.Name} for {lacks * price} pyreals (price {price} each"));
        Assert.True(home.Logged($"resupply: bought {lacks} Pack at {vendor.Name} for {lacks * price} pyreals"));
        Assert.Equal((long)lacks * price, run.Spent);
        Assert.True(home.Logged("packs: check after: side packs"));
        Assert.Equal(vendor.Name, Assert.Single(run.Visits).Vendor.Name); // it rides on the trip: one stop, no trip of its own
    }

    [Fact]
    public void M6_8_D_a_layout_with_three_side_packs_buys_none_and_a_purse_that_pays_none_names_the_short()
    {
        var whole = new Home(Den, 100_000);
        var run = whole.Start();
        Assert.Equal(ScenarioStatus.Succeeded, whole.Run(run));
        Assert.Empty(run.BuyList);
        Assert.True(whole.Logged("resupply: packs: 3 side packs held, the layout wants 3: none to buy"));

        var thin = new Home(Den, 50, notes: false, sidePacks: 1);
        var poor = thin.Start();
        Assert.Equal(ScenarioStatus.Succeeded, thin.Run(poor));
        Assert.Empty(poor.BuyList);
        Assert.True(thin.Logged("does not pay one at 88: none bought (TODO(m5-pack-short))"));
    }

    [Fact]
    public void M6_8_D_a_kit_trip_buys_a_pack_line_short_of_its_count_and_a_caller_that_does_not_count_leaves_it_to_the_build()
    {
        var ground = GroundCatalog.Get(Den);
        var line = ground.Supplies.Kit.Single(k => k.Slot == KitSlot.Pack);
        var unsold = new List<KitLine>();

        Assert.Empty(ResupplyPlanner.KitBuyList(ground, [line], unsold)); // not counted: the build grants (M5 7 G3)
        var buys = ResupplyPlanner.KitBuyList(ground, [line], unsold, held: wcid => wcid == line.Wcid ? 1 : 0, containerRoom: 5);
        var buy = Assert.Single(buys);
        Assert.Equal((line.Wcid, line.Count - 1, 1), (buy.Line.Wcid, buy.Count, buy.Held));
        Assert.Empty(ResupplyPlanner.KitBuyList(ground, [line], unsold, held: _ => line.Count, containerRoom: 5)); // held already
        Assert.Empty(ResupplyPlanner.KitBuyList(ground, [line], unsold, held: _ => 0, containerRoom: 0)); // no container slot to put one in
        Assert.Empty(unsold);
    }

    [Theory]
    [InlineData(1, 7, 100_000, 2, "buys 2 Pack")]
    [InlineData(1, 1, 100_000, 1, "buys 1 Pack")]
    [InlineData(1, 0, 100_000, 0, "no container slot is free")]
    [InlineData(1, 7, 100, 1, "buys 1 Pack")]
    [InlineData(1, 7, 87, 0, "does not pay one at 88")]
    [InlineData(3, 7, 100_000, 0, "none to buy")]
    public void M6_8_D_the_pack_buy_is_the_lack_the_container_slots_allow_and_the_purse_pays(int held, int room, long purse, int expected, string why)
    {
        var ground = GroundCatalog.Get(Den);

        var buy = ResupplyPlanner.PackBuy(ground, held, room, purse, out var line);

        Assert.Equal(expected, buy?.Count ?? 0);
        Assert.Contains(why, line, StringComparison.Ordinal);
    }
}

using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Session.Vitals;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 T (the owner, 2026-11-11: carry health tinctures, and Mana Tinctures too): the town stock's data and rule. The three island grounds name the Health Tincture and the Mana Tincture at Ludora the Healer of Dryreach; a line under
/// its trip line buys to its keep inside the trip's budget and the load line, else it is held back; the lines share one budget in the profile's order; a line over its trip line rides a stop made for another; a failed stop is not
/// tried again; and the stop's two portals are the Town Network's.
/// </summary>
public sealed class TownStockTests
{
    private static readonly IReadOnlyList<TownStockLine> Lines = [.. GroundCatalog.Get("tusker-cave").TownStock.Where(l => l.Town == "Dryreach")]; // M6 item 14 (m6-goldscarab): the Cave's two more lines are Uziz's (TownStockScarabTests)

    private static TownStockLine Health => Lines[0];

    private static TownStockLine Mana => Lines[1];

    private static string? NoFailure(uint wcid) => null;

    [Theory]
    [InlineData("tusker-burrow")]
    [InlineData("tusker-lodge")]
    [InlineData("tusker-cave")]
    public void The_island_grounds_buy_Health_and_Mana_Tinctures_at_Ludora_of_Dryreach_and_keep_a_hundred_Mana_Potions_from_home(string key)
    {
        var ground = GroundCatalog.Get(key);
        Assert.Equal(
            [(HealthDrinks.TinctureWcid, "Health Tincture", 12, 6, 775, 50), (ManaDrinks.TinctureWcid, "Mana Tincture", 30, 10, 775, 50)],
            ground.TownStock.Where(l => l.Town == "Dryreach").Select(l => (l.Wcid, l.Name, l.Keep, l.TripUnder, l.Price, l.Burden)));
        var drinks = ground.TownStock.Where(l => l.Town == "Dryreach").ToList();
        Assert.All(drinks, l =>
        {
            Assert.Equal(("Dryreach", "Ludora the Healer", 2229u, 0x7DA75026u, 0xDA75003Cu), (l.Town, l.Vendor.Name, l.Vendor.Wcid, l.Vendor.Guid, l.Vendor.Cell));
            Assert.Equal(775, (int)Math.Max(1, Math.Ceiling((1.55 * 500) - 0.1))); // ACE's Vendor.GetSellCost on her SellPrice 1.55 and the Value 500
            Assert.Null(ground.Supplies.StockOf(l.Wcid)); // no line of the home walk
            Assert.Null(ground.VendorSelling(l.Wcid)); // no home vendor sells it
            Assert.True(SellRules.KeptWeenies.Contains(l.Wcid), "never sold with the loot");
        });
        var potion = ground.Supplies.StockOf(FighterKit.ManaPotionWcid)!;
        Assert.Equal((100, 10, 0), (potion.Keep, potion.TripUnder, potion.StopUnder)); // the fallback: 2,500 mana, one stretch between trips at the Cave's 4,000 an hour
        var burden = drinks.Sum(l => l.Keep * l.Burden) + (potion.Keep * 15) + (ground.Supplies.StockOf(FighterKit.PotionWcid)!.Keep * 15);
        Assert.Equal(4_050, burden); // 30 x 50 + 12 x 50 + 100 x 15 + 30 x 15
        Assert.Equal(4_000, (drinks[1].Keep * 50) + (potion.Keep * 25)); // the mana the two mana drinks hold
        Assert.Equal(32_550, drinks.Sum(l => l.Keep * l.Price)); // one stop from nothing
    }

    [Fact]
    public void A_ground_whose_way_out_does_not_pass_the_Town_Network_names_no_town_stock()
    {
        Assert.DoesNotContain(GroundCatalog.Get("tusker-temple-20-40").TownStock, l => !l.FeeOnly); // its first route is the New Colier Portal on foot (D138: but for the buff bot's fee line, every ground's, BuffBotFeeTests)
        Assert.DoesNotContain(GroundCatalog.Get("holtburg-outskirts").TownStock, l => !l.FeeOnly);
    }

    [Fact]
    public void A_line_under_its_trip_line_buys_to_its_keep_and_a_line_holding_it_buys_nothing()
    {
        var due = TownStock.Decide(Health, new TownStockInput(Held: 2, Budget: 50_000, Burden: 1_000, Capacity: 17_400, LoadLinePercent: 110, Failed: null));
        Assert.Equal((TownStockKind.Due, 10, 7_750L, "due"), (due.Kind, due.Count, due.Cost, due.Token));
        Assert.Contains("2 Health Tincture held, under the trip line 6: buys 10 of the 10 to the keep 12 at Ludora the Healer in Dryreach, 7750 pyreals at 775 each, burden 500", due.Why, StringComparison.Ordinal);

        var holds = TownStock.Decide(Health, new TownStockInput(6, 50_000, 1_000, 17_400, 110, null));
        Assert.Equal((TownStockKind.NotDue, 0, "holds"), (holds.Kind, holds.Count, holds.Token));
    }

    [Fact]
    public void A_poor_purse_holds_the_line_back_and_a_purse_that_reaches_the_trip_line_buys_what_it_pays()
    {
        // A fresh character at the Burrow's door: 6 are needed for the trip line, 4,650 pyreals; the budget pays 3.
        var poor = TownStock.Decide(Health, new TownStockInput(0, 3_000, 1_000, 7_800, 110, null));
        Assert.Equal((TownStockKind.HeldBack, 0, "purse"), (poor.Kind, poor.Count, poor.Token));
        Assert.Contains("the trip's budget 3000 pays 3 at 775 each, fewer than the 6 to the trip line: held back for the purse", poor.Why, StringComparison.Ordinal);

        var some = TownStock.Decide(Health, new TownStockInput(0, 6_000, 1_000, 7_800, 110, null));
        Assert.Equal((TownStockKind.Due, 7, 5_425L), (some.Kind, some.Count, some.Cost)); // 7 of the 12: what the budget pays, over the trip line
    }

    [Fact]
    public void The_load_line_caps_the_buy_and_holds_it_back_when_the_trip_line_is_out_of_reach()
    {
        // Capacity 7,800 at 110%: 8,580. A load of 8,200 leaves room for 7 tinctures of 50; a load of 8,400 for 3.
        var capped = TownStock.Decide(Health, new TownStockInput(0, 50_000, 8_200, 7_800, 110, null));
        Assert.Equal((TownStockKind.Due, 7), (capped.Kind, capped.Count));

        var held = TownStock.Decide(Health, new TownStockInput(0, 50_000, 8_400, 7_800, 110, null));
        Assert.Equal((TownStockKind.HeldBack, "burden"), (held.Kind, held.Token));
        Assert.Contains("leaves room for 3 at burden 50 under the line 110%, fewer than the 6 to the trip line: held back for the load", held.Why, StringComparison.Ordinal);

        var unknown = TownStock.Decide(Health, new TownStockInput(0, 50_000, 8_400, 0, 110, null));
        Assert.Equal((TownStockKind.Due, 12), (unknown.Kind, unknown.Count)); // no capacity read: the load is not judged
    }

    [Fact]
    public void A_stop_that_failed_this_run_is_not_tried_again()
    {
        var failed = TownStock.Decide(Mana, new TownStockInput(0, 50_000, 0, 17_400, 110, "hop:network-to-dryreach: no teleport"));
        Assert.Equal((TownStockKind.Failed, 0, "failed"), (failed.Kind, failed.Count, failed.Token));
        Assert.Contains("failed earlier this run (hop:network-to-dryreach: no teleport): not tried again", failed.Why, StringComparison.Ordinal);

        var facts = new JourneyFacts();
        Assert.Null(facts.TownStockFailure(Mana.Wcid));
        facts.TownStockFailed(Mana.Wcid, "shop: nothing bought");
        facts.TownStockFailed(Mana.Wcid, "a later name");
        Assert.Equal("shop: nothing bought", facts.TownStockFailure(Mana.Wcid)); // the first failure stands
        Assert.Null(facts.TownStockGivenUp(Mana.Wcid)); // one stop failed (two failures in it count once): tried again on the next way out
        facts.TownStockStopBegins();
        facts.TownStockFailed(Mana.Wcid, "hop: the second stop");
        facts.TownStockFailed(Mana.Wcid, "shop: the second stop again");
        Assert.Null(facts.TownStockGivenUp(Mana.Wcid)); // two of JourneyFacts.TownStockTries
        facts.TownStockStopBegins();
        Assert.Null(facts.TownStockGivenUp(Mana.Wcid)); // a stop begun is not a stop failed
        facts.TownStockFailed(Mana.Wcid, "hop: the third stop");
        Assert.Equal(3, JourneyFacts.TownStockTries);
        Assert.Equal("shop: nothing bought; 3 stops failed, the bound 3", facts.TownStockGivenUp(Mana.Wcid)); // given up for the run, by the first failure's name
        Assert.Null(facts.TownStockGivenUp(Mana.Wcid + 1)); // another ware is its own count
        Assert.True(facts.TownStockSayOnce(Mana.Wcid, "purse"));
        Assert.False(facts.TownStockSayOnce(Mana.Wcid, "purse")); // said once a run
        Assert.True(facts.TownStockSayOnce(Health.Wcid, "purse"));
    }

    [Fact]
    public void The_lines_of_one_vendor_share_the_trips_budget_in_the_profiles_order_and_a_line_over_its_trip_line_rides_the_stop()
    {
        // Nothing held, 40,000 to spend: the Health Tinctures first (12, 9,300), then all 30 Mana Tinctures (23,250): 32,550 in all, burden 2,100.
        var full = TownStock.Plan(Lines, [0, 0], 40_000, 2_000, 17_400, 110, NoFailure);
        Assert.Equal([(TownStockKind.Due, 12), (TownStockKind.Due, 30)], full.Select(v => (v.Kind, v.Count)));
        Assert.Equal(32_550, full.Sum(v => v.Cost));

        // 15,000 to spend: the health line takes 9,300; 5,700 are left, 7 Mana Tinctures, under the 10 to its trip line: the mana line is held back and said so.
        var tight = TownStock.Plan(Lines, [0, 0], 15_000, 2_000, 17_400, 110, NoFailure);
        Assert.Equal([(TownStockKind.Due, 12, "due"), (TownStockKind.HeldBack, 0, "purse")], tight.Select(v => (v.Kind, v.Count, v.Token)));

        // The mana line is due (2 held); the health line holds its trip line (8 of 12) and rides: she is at the counter anyway.
        var riding = TownStock.Plan(Lines, [8, 2], 40_000, 2_000, 17_400, 110, NoFailure);
        Assert.Equal([(TownStockKind.Due, 4, "riding"), (TownStockKind.Due, 28, "due")], riding.Select(v => (v.Kind, v.Count, v.Token)));

        // Both hold their trip lines: no stop, so nothing rides.
        var none = TownStock.Plan(Lines, [8, 20], 40_000, 2_000, 17_400, 110, NoFailure);
        Assert.All(none, v => Assert.Equal(TownStockKind.NotDue, v.Kind));

        // A line that failed this run neither calls the stop nor rides it.
        var failed = TownStock.Plan(Lines, [8, 2], 40_000, 2_000, 17_400, 110, w => w == Health.Wcid ? "shop: nothing bought" : null);
        Assert.Equal([(TownStockKind.NotDue, 0), (TownStockKind.Due, 28)], failed.Select(v => (v.Kind, v.Count)));
    }

    [Fact]
    public void The_stop_is_made_from_the_Town_Network_by_its_Dryreach_portal_and_back_by_Dryreaches_own()
    {
        var routes = TravelRoutes.Default;
        var edges = TownStock.EdgesOf(Health, "town-network", routes);
        Assert.NotNull(edges);
        Assert.Equal(("network-to-dryreach", "dryreach-to-network"), (edges.Value.Into.Key, edges.Value.Back.Key));
        Assert.Null(TownStock.EdgesOf(Health, "holtburg", routes)); // no portal of Holtburg leads to Dryreach
        Assert.Null(TownStock.EdgesOf(Health, "facility-hub", routes));
        Assert.Null(TownStock.EdgesOf(Health, "dryreach", routes)); // she is in the town already: no hop
    }
}

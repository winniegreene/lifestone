using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>The owner's rule of 2026-11-09 (D116) in the buy list itself: a scroll waits only for its own price; its components are bought from what the scroll leaves of the purse, in part if that is all it pays; the standing shopping list comes ahead of new scrolls; what a stock line already buys is not bought twice.</summary>
public sealed partial class LogisticsCountersTests
{
    private const uint SilverScarabWare = 688;
    private const uint NoStockLineWare = 2472; // the Wand: sold at Cindrue, on no Burrow stock line (every component of hers has one); the planner reads only the row

    private static ScrollComponent Component(HuntingGround ground, uint wcid, string name, int want, int held) => new(wcid, name, want, held, ground.VendorSelling(wcid), ground.PriceOf(wcid) ?? 0);

    [Fact]
    public void A_scroll_is_kept_on_its_own_price_and_its_components_take_only_what_is_left()
    {
        var ground = Ground(Burrow);
        var counters = new LogisticsCounters(ground);
        var pack = BurrowPack(potions: 2, coin: 1_000);
        counters.Check(pack.State);
        var held = counters.HeldOf(IndexOf(counters, SilverScarabWare));
        var scroll = ScrollBuy(ground, 1308) with { Components = [Component(ground, SilverScarabWare, "Silver Scarab", held + 5, held)] };
        var waiting = new List<ResupplyBuy>();

        var list = ResupplyPlanner.BuyList(ground, counters, 1_000, [scroll], waiting);

        Assert.Empty(waiting);
        var kept = list.Single(b => b.Line.Name == "Armor Self II");
        Assert.True((list.FirstOrDefault(b => b.Line.Wcid == SilverScarabWare)?.Count ?? 0) < 5); // five scarabs cost more than the scroll leaves
        Assert.DoesNotContain("x5", kept.ComponentNote, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shopping_list_comes_ahead_of_a_new_scroll()
    {
        var ground = Ground(Burrow);
        var counters = new LogisticsCounters(ground);
        var pack = BurrowPack(potions: 2, coin: 1_000);
        counters.Check(pack.State);
        var scroll = ScrollBuy(ground, 1308);
        var vendor = ground.VendorSelling(SilverScarabWare)!;
        var price = ground.PriceOf(SilverScarabWare)!.Value;
        var line = new ResupplyBuy(new StockLine(SilverScarabWare, "Silver Scarab", 1_000 / price, 1, 1), vendor, 0, 1_000 / price); // the whole purse's worth
        var waiting = new List<ResupplyBuy>();

        var list = ResupplyPlanner.BuyList(ground, counters, 1_000, [scroll], waiting, [line]);

        Assert.DoesNotContain(list, b => b.Line.Name == "Armor Self II");
        Assert.Equal("Armor Self II", Assert.Single(waiting).Line.Name);
        Assert.Equal(1_000 / price, list.Single(b => b.Line.Wcid == SilverScarabWare).Count);
    }

    [Fact]
    public void A_shopping_line_with_no_scroll_on_the_trip_is_still_bought()
    {
        var ground = Ground(Burrow);
        var counters = new LogisticsCounters(ground);
        var pack = BurrowPack(potions: 2, coin: 20_000);
        counters.Check(pack.State);
        var line = new ResupplyBuy(new StockLine(NoStockLineWare, "Wand", 2, 1, 1), ground.VendorSelling(NoStockLineWare)!, 0, 2);

        var list = ResupplyPlanner.BuyList(ground, counters, 20_000, [], [], [line]);

        Assert.Equal(2, list.Single(b => b.Line.Wcid == NoStockLineWare).Count);
    }

    [Fact]
    public void Components_are_bought_beside_the_scroll_a_stock_line_raised_and_a_ware_with_no_stock_line_added_once()
    {
        var ground = Ground(Burrow);
        var counters = new LogisticsCounters(ground);
        var pack = BurrowPack(potions: 2, coin: 20_000);
        counters.Check(pack.State);
        var held = counters.HeldOf(IndexOf(counters, SilverScarabWare));

        var one = ScrollBuy(ground, 1308) with { Components = [Component(ground, SilverScarabWare, "Silver Scarab", held + 2, held), Component(ground, NoStockLineWare, "Wand", 2, 0)] };
        var two = ScrollBuy(ground, 1019) with { Components = [Component(ground, NoStockLineWare, "Wand", 2, 0)] };
        var waiting = new List<ResupplyBuy>();

        var list = ResupplyPlanner.BuyList(ground, counters, 20_000, [one, two], waiting);

        Assert.Empty(waiting);
        Assert.Equal(2, list.Count(b => b.Line.Name.Contains("Self II", StringComparison.Ordinal)));
        Assert.Equal(2, list.Single(b => b.Line.Wcid == SilverScarabWare).Count);
        Assert.Equal(4, list.Single(b => b.Line.Wcid == NoStockLineWare).Count); // two passes of each of the two spells
        Assert.Equal(2, list.Single(b => b.Line.Name == "Armor Self II").ComponentNote.Split(", ").Length);
    }
}

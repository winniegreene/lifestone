using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 V (the r1h loop: level 10, 14 coin, a Mana Potion trip called the moment the fight began, again and again; one trip had spent 9,907 on 198 items): a trip line the purse cannot pay is no trip and the fight goes on; a stop line stays
/// real; the buy list spends in the order of need within the ground's <c>trip_spend_percent</c>; the rung grounds' lines are rung-scale data.
/// </summary>
public sealed partial class LogisticsCountersTests
{
    private const string Burrow = "tusker-burrow";
    private const uint ManaPotion = 379;
    private const uint IronScarab = 689;

    /// <summary>The Burrow with its Mana Potion line given a trip line again: the profile's line no longer calls a trip (M6 7 lead: the rehearsal's trip the moment she arrived with none), so these tests name the rule on a copy.</summary>
    private static HuntingGround PotionTripGround()
    {
        var ground = Ground(Burrow);
        return ground with { Supplies = ground.Supplies with { Stock = [.. ground.Supplies.Stock.Select(l => l.Wcid == ManaPotion ? l with { TripUnder = 1 } : l)] } };
    }

    /// <summary>The Burrow's pack at its keeps, the potion at <paramref name="potions"/>, the coin at <paramref name="coin"/>.</summary>
    private static Pack BurrowPack(int potions, int coin, params (uint Wcid, int Count)[] changes)
    {
        var ground = PotionTripGround();
        var stacks = Stocked(ground).Select(s => changes.Any(c => c.Wcid == s.Wcid) ? (s.Wcid, (uint)changes.First(c => c.Wcid == s.Wcid).Count) : s.Wcid == ManaPotion ? (s.Wcid, (uint)potions) : s).ToList();
        var pack = new Pack(stacks, burden: 100);
        pack.R.PrivateInt(PlayerQualities.CoinValueProperty, coin);
        return pack;
    }

    /// <summary>M6 14 T: the least purse whose trip share (the ground's <c>trip_spend_percent</c>) pays the wares that lift <paramref name="line"/> from <paramref name="held"/> clear of its trip line (<see cref="LogisticsCounters.ClearOf"/>).</summary>
    private static int PurseThatLifts(HuntingGround ground, StockLine line, int held)
    {
        var cost = (long)ground.PriceOf(line.Wcid)!.Value * (LogisticsCounters.ClearOf(line) - held);
        var percent = Math.Clamp(ground.Supplies.Purse.TripSpendPercent, 1, 100);
        return (int)(((cost * 100) + percent - 1) / percent);
    }

    [Fact]
    public void A_trip_line_the_purse_cannot_pay_is_no_trip_and_the_coin_that_pays_it_makes_it_one()
    {
        var ground = PotionTripGround();
        var counters = new LogisticsCounters(ground);
        var potion = IndexOf(counters, ManaPotion);
        var need = PurseThatLifts(ground, counters.LineAt(potion), held: 0); // M6 14 T: the purse whose trip share buys the line clear of its trip line, not only to it
        Assert.True(need > ground.PriceOf(ManaPotion)!.Value * counters.LineAt(potion).TripUnder);

        var broke = BurrowPack(potions: 0, coin: 14); // r1h: the fight found 14 coin and no potion
        var verdict = counters.Check(broke.State);
        Assert.Equal(LogisticsKind.None, verdict.Kind);
        Assert.True(counters.IsHeldBack(potion));
        Assert.Equal(1UL << potion, counters.HeldBackMask);

        var paid = BurrowPack(potions: 0, coin: need);
        verdict = new LogisticsCounters(ground).Check(paid.State);
        Assert.Equal(LogisticsKind.Trip, verdict.Kind);
        Assert.Equal(LogisticsCause.StockLow, verdict.Cause);
        Assert.Equal(ManaPotion, verdict.Line!.Wcid);

        var short1 = BurrowPack(potions: 0, coin: need - 1);
        Assert.Equal(LogisticsKind.None, new LogisticsCounters(ground).Check(short1.State).Kind);
    }

    [Fact]
    public void A_held_back_line_does_not_hide_another_line_the_purse_can_pay()
    {
        var ground = PotionTripGround();
        var taper = ground.Supplies.Stock.Single(l => l.Wcid == PrismaticTaper);
        var coin = PurseThatLifts(ground, taper, taper.TripUnder - 5); // 5 short of its trip line: the purse that lifts the tapers clear; the potions' lift costs more (M6 14 T)
        Assert.True(coin < PurseThatLifts(ground, ground.Supplies.Stock.Single(l => l.Wcid == ManaPotion), held: 0));
        var pack = BurrowPack(potions: 0, coin: coin, (PrismaticTaper, taper.TripUnder - 5));
        var counters = new LogisticsCounters(ground);

        var verdict = counters.Check(pack.State);

        Assert.Equal(LogisticsKind.Trip, verdict.Kind);
        Assert.Equal(PrismaticTaper, verdict.Line!.Wcid);
        Assert.True(counters.IsHeldBack(IndexOf(counters, ManaPotion)));
    }

    [Fact]
    public void A_stop_line_is_a_stop_whatever_the_purse_holds()
    {
        var ground = PotionTripGround();
        var counters = new LogisticsCounters(ground);
        var pack = BurrowPack(potions: 0, coin: 14, (IronScarab, 0));

        var verdict = counters.Check(pack.State);

        Assert.Equal(LogisticsKind.Stop, verdict.Kind);
        Assert.Equal(IronScarab, verdict.Line!.Wcid);
        Assert.False(counters.IsHeldBack(IndexOf(counters, IronScarab)));
    }

    [Fact]
    public void The_buy_list_spends_the_lines_under_their_trip_lines_first_and_stays_inside_the_trip_budget()
    {
        var ground = PotionTripGround();
        var counters = new LogisticsCounters(ground);
        var pack = BurrowPack(potions: 0, coin: 9_907, (PrismaticTaper, 46), (IronScarab, 0));
        counters.Check(pack.State);
        var percent = ground.Supplies.Purse.TripSpendPercent;
        Assert.InRange(percent, 1, 99);

        var list = ResupplyPlanner.BuyList(ground, counters, 9_907);
        var spent = list.Sum(b => (long)b.Count * ground.PriceOf(b.Line.Wcid)!.Value);

        Assert.True(OutsideCasting(list, ground) <= 9_907L * percent / 100, $"spent {spent}"); // r1h spent all of it; M6 sprint: the casting components' top-up to their keeps comes from the rest of the purse
        Assert.True(spent <= 9_907, $"spent {spent}");
        Assert.True(spent > 0);
        var iron = list.Single(b => b.Line.Wcid == IronScarab);
        var potion = list.SingleOrDefault(b => b.Line.Wcid == ManaPotion);
        Assert.True(iron.Count >= ground.Supplies.Stock.Single(l => l.Wcid == IronScarab).TripUnder); // the component's trip line before the potion's top-up
        Assert.True(potion is null || potion.Count <= ground.Supplies.Stock.Single(l => l.Wcid == ManaPotion).Keep);
        Assert.True(list.Single(b => b.Line.Wcid == PrismaticTaper).Count > 0);
    }

    private static int Lifted(IReadOnlyList<ResupplyBuy> list, LogisticsCounters counters, uint wcid) =>
        counters.HeldOf(IndexOf(counters, wcid)) + (list.SingleOrDefault(b => b.Line.Wcid == wcid)?.Count ?? 0);

    private static string[] Show(IReadOnlyList<ResupplyBuy> list) => [.. list.Select(b => $"{b.Line.Name} {b.Held} +{b.Count}")];

    [Fact]
    public void M6_16_Q10_on_a_short_purse_the_line_the_trip_was_called_for_is_lifted_clear_before_the_cheapest_lift()
    {
        var ground = PotionTripGround();
        var potion = ground.Supplies.Stock.Single(l => l.Wcid == ManaPotion);
        var taper = ground.Supplies.Stock.Single(l => l.Wcid == PrismaticTaper);
        var coin = PurseThatLifts(ground, potion, held: 0) + 600; // the potions' whole lift, which costs more than the tapers', and the tapers' few wares to their trip line
        Assert.True(PurseThatLifts(ground, taper, taper.TripUnder - 5) < coin);
        var counters = new LogisticsCounters(ground);
        counters.Check(BurrowPack(potions: 0, coin: coin, (PrismaticTaper, taper.TripUnder - 5)).State);

        var before = ResupplyPlanner.BuyList(ground, counters, coin);
        var after = ResupplyPlanner.BuyList(ground, counters, coin, called: ManaPotion);

        Assert.True(Lifted(before, counters, ManaPotion) < LogisticsCounters.ClearOf(potion)); // before: the cheapest lift (the tapers) went first and the potions stay on their trip line
        Assert.True(Lifted(before, counters, PrismaticTaper) >= LogisticsCounters.ClearOf(taper));
        Assert.True(Lifted(after, counters, ManaPotion) >= LogisticsCounters.ClearOf(potion)); // after: the line the trip was called for is clear of it
        Assert.True(OutsideCasting(after, ground) <= coin * ground.Supplies.Purse.TripSpendPercent / 100); // inside the same trip share (M6 sprint: the tapers, a casting component, are topped up after it from the rest of the purse)
        Assert.True(after.Sum(b => (long)b.Count * ground.PriceOf(b.Line.Wcid)!.Value) <= coin);
    }

    [Fact]
    public void M6_16_Q10_an_ample_purse_or_no_carried_order_gives_the_list_as_it_was()
    {
        var ground = PotionTripGround();
        var counters = new LogisticsCounters(ground);
        counters.Check(BurrowPack(potions: 0, coin: 1_000_000, (PrismaticTaper, 46), (IronScarab, 0)).State);
        var today = ResupplyPlanner.BuyList(ground, counters, 1_000_000);
        Assert.Equal(Show(today), Show(ResupplyPlanner.BuyList(ground, counters, 1_000_000, called: null)));
        foreach (var wcid in new[] { ManaPotion, PrismaticTaper, IronScarab })
        {
            Assert.Equal(Show(today), Show(ResupplyPlanner.BuyList(ground, counters, 1_000_000, called: wcid))); // the purse covers every line: the same buys in the same order
        }
    }

    /// <summary>One scroll buy at the Burrow's home scrivener of <paramref name="spell"/> (<see cref="ScrivenerCatalog"/>), as the learn step adds it.</summary>
    private static ResupplyBuy ScrollBuy(HuntingGround ground, uint spell)
    {
        var row = ScrivenerCatalog.Default.Spell(spell)!;
        var vendor = row.Sellers.Select(s => ground.VendorOf(s.VendorWcid)).First(v => v is not null)!;
        return new ResupplyBuy(new StockLine(row.ScrollWcid, row.Name, 1, 1, 1), vendor, 0, 1);
    }

    [Fact]
    public void M6_7_X_a_poor_purse_buys_the_protective_scrolls_and_their_components_before_the_stock_top_up()
    {
        // r2b's state after two deaths: 924 coin. The budget is 554: Armor Self II and the protection II (250 each) fit, the components with no stop line follow from the whole purse, and the tapers wait for the next trip.
        var ground = Ground(Burrow);
        var counters = new LogisticsCounters(ground);
        var components = ground.Supplies.Stock.Where(l => l.StopUnder == 0 && l.Wcid != ManaPotion && l.Wcid != Lifestone.Client.Session.Fighter.FighterKit.PotionWcid).Take(8).Select(l => (l.Wcid, 0)).ToArray(); // M6 7 AR: the stock is the Lodge's list; the first lines stand for the rest
        var pack = BurrowPack(potions: 0, coin: 924, [(PrismaticTaper, 47), .. components]);
        counters.Check(pack.State);
        var scrolls = new[] { ScrollBuy(ground, 1308), ScrollBuy(ground, 1019), ScrollBuy(ground, 1104) };
        var waiting = new List<ResupplyBuy>();

        var list = ResupplyPlanner.BuyList(ground, counters, 924, scrolls, waiting);

        Assert.Equal(["Armor Self II", "Bludgeoning Protection Self II"], list.Where(b => b.Line.Name.Contains("Self", StringComparison.Ordinal)).Select(b => b.Line.Name));
        Assert.Equal("Fire Vulnerability Other II", Assert.Single(waiting).Line.Name); // 54 of the budget left: it waits, the plan's order stands
        foreach (var (wcid, _) in components)
        {
            Assert.Contains(list, b => b.Line.Wcid == wcid);
        }

        Assert.InRange(Lifted(list, counters, PrismaticTaper), 47, ground.Supplies.Stock.Single(l => l.Wcid == PrismaticTaper).Keep); // 47 is over its stop line: the share does not top it up; M6 sprint: a casting component is topped up from what the purse has left, at most to its keep
        var spent = list.Sum(b => (long)b.Count * b.Vendor.Sells.First(w => w.Wcid == b.Line.Wcid).Price);
        Assert.True(spent <= 924, $"spent {spent}");
    }

    [Fact]
    public void M6_7_AA_a_protective_scroll_comes_before_the_top_up_of_a_line_above_its_trip_line_when_the_budget_pays_only_one()
    {
        // The shape of r3's trip (132 items, 4,644, mostly tapers), with the budget too small for both: a line over its trip line is the last thing the trip buys.
        var ground = Ground(Burrow);
        var counters = new LogisticsCounters(ground);
        var components = ground.Supplies.Stock.Where(l => l.StopUnder == 0 && l.Wcid != ManaPotion).Select(l => (l.Wcid, 1)).ToArray();
        var taper = ground.Supplies.Stock.First(l => l.Wcid == PrismaticTaper);
        var above = Math.Max(taper.TripUnder, taper.StopUnder) + 1;
        Assert.True(above < taper.Keep, "the line sits between its trip line and its keep");
        var pack = BurrowPack(potions: 2, coin: 700, [(PrismaticTaper, above), .. components]);
        counters.Check(pack.State);
        var scrolls = new[] { ScrollBuy(ground, 1308) };
        var waiting = new List<ResupplyBuy>();

        var list = ResupplyPlanner.BuyList(ground, counters, 700, scrolls, waiting);

        Assert.Contains(list, b => b.Line.Name == "Armor Self II");
        Assert.Empty(waiting);
        var spent = list.Sum(b => (long)b.Count * b.Vendor.Sells.First(w => w.Wcid == b.Line.Wcid).Price);
        var outside = list.Where(b => ground.Supplies.Stock.FirstOrDefault(l => l.Wcid == b.Line.Wcid) is not { } l || !Casting(l)).Sum(b => (long)b.Count * b.Vendor.Sells.First(w => w.Wcid == b.Line.Wcid).Price);
        Assert.True(outside <= 700 * ground.Supplies.Purse.TripSpendPercent / 100, $"spent {outside} outside the casting components"); // M6 sprint: the tapers' top-up is the last thing, from the rest of the purse
        Assert.True(spent <= 700, $"spent {spent}");
    }

    [Fact]
    public void M6_7_X_a_stop_line_is_paid_before_a_scroll_and_a_scroll_too_dear_waits_while_a_cheaper_one_is_bought()
    {
        var ground = Ground(Burrow);
        var counters = new LogisticsCounters(ground);
        var pack = BurrowPack(potions: 2, coin: 2_000, (IronScarab, 0));
        counters.Check(pack.State);
        var scrolls = new[] { ScrollBuy(ground, 1310), ScrollBuy(ground, 1019) }; // Armor Self IV 5,000, the protection II 250: the budget is 1,200
        var waiting = new List<ResupplyBuy>();

        var list = ResupplyPlanner.BuyList(ground, counters, 2_000, scrolls, waiting);

        Assert.Contains(list, b => b.Line.Wcid == IronScarab);
        Assert.Contains(list, b => b.Line.Name == "Bludgeoning Protection Self II");
        Assert.Equal("Armor Self IV", Assert.Single(waiting).Line.Name);
    }

    [Fact]
    public void M6_7_X_with_no_budget_every_scroll_is_on_the_list_after_the_stock_as_before()
    {
        var ground = Ground(Burrow);
        var counters = new LogisticsCounters(ground);
        var pack = BurrowPack(potions: 0, coin: 100);
        counters.Check(pack.State);
        var scrolls = new[] { ScrollBuy(ground, 1310) };
        var waiting = new List<ResupplyBuy>();

        var list = ResupplyPlanner.BuyList(ground, counters, -1, scrolls, waiting);

        Assert.Empty(waiting);
        Assert.Equal("Armor Self IV", list[^1].Line.Name);
        Assert.Contains(list, b => b.Line.Wcid == ManaPotion);
    }

    [Fact]
    public void A_broke_purse_buys_the_components_a_stop_needs_before_the_potion()
    {
        var ground = PotionTripGround();
        var counters = new LogisticsCounters(ground);
        var pack = BurrowPack(potions: 0, coin: 300, (IronScarab, 0));
        counters.Check(pack.State);

        var list = ResupplyPlanner.BuyList(ground, counters, 300);

        Assert.DoesNotContain(list, b => b.Line.Wcid == ManaPotion); // 60% of 300 pays no 230 potion
        Assert.Contains(list, b => b.Line.Wcid == IronScarab);
    }

    [Fact]
    public void Without_a_budget_the_list_is_every_line_to_its_keep_as_before_and_a_den_ground_has_none()
    {
        var burrow = Ground(Burrow);
        var counters = new LogisticsCounters(burrow);
        counters.Check(BurrowPack(potions: 0, coin: 14).State);
        var list = ResupplyPlanner.BuyList(burrow, counters);
        Assert.Equal(burrow.Supplies.Stock.Single(l => l.Wcid == ManaPotion).Keep, list.Single(b => b.Line.Wcid == ManaPotion).Count);

        var den = Ground(Den);
        Assert.Equal(100, den.Supplies.Purse.TripSpendPercent); // BSD Mage's profile is untouched
    }

    [Theory]
    [InlineData("tusker-burrow")]
    [InlineData("tusker-lodge")]
    [InlineData("holtburg-outskirts")]
    public void The_rung_grounds_keep_a_budget_and_a_potion_reserve_a_low_level_purse_can_carry(string key)
    {
        var ground = Ground(key);
        Assert.Equal(60, ground.Supplies.Purse.TripSpendPercent);
        var potion = ground.Supplies.Stock.Single(l => l.Wcid == ManaPotion);
        Assert.True(potion.Keep >= 20 && potion.StopUnder == 0); // M6 8 (the owner, 2026-11-11): a stock to drink instead of resting, a top-up inside the trip budget, never a stop
        Assert.Equal(key == "holtburg-outskirts" ? 2 : 10, potion.TripUnder); // the island grounds: 10 of a stock of 100 or 120 (live 2026-11-11 11:50 UTC: with none left and no trip due she fought three minutes, retreated at the mana line and came back with none, again and again); D115 undid a trip under 1 of a stock of 2, a trip a potion // a trip of its own only where home is a short walk: live 2026-11-11 06:45 UTC she rested at mana 0 with none held, 34 minutes after a start with no trip due; never from the island (D115)
        var fullRestock = ground.Supplies.Stock.Where(l => l.Wcid != ManaPotion).Sum(l => (long)l.Keep * ground.PriceOf(l.Wcid)!.Value); // M6 8: without the Mana Potions: they are a top-up the trip budget cuts to the purse (live 2026-11-11: one full buff pass drank 50, so the tusker grounds keep 120)
        Assert.True(fullRestock <= ground.Supplies.Purse.Coin + 1_000, $"{key}: a full restock {fullRestock} against the profile's coin {ground.Supplies.Purse.Coin}");
    }
}

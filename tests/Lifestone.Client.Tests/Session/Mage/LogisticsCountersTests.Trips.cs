using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 T (the sprint of 2026-11-12, levels 6 to 9: 1,452 s of 3,316 s on three trips to Holtburg; two of them sale trips called 19 s and 13 s into a fight, the first for one item sold at 3,600; ten stock lines on their trip lines
/// after the last visit with 6,082 coin left). The rules over that run's numbers: a line calls a trip only when one trip's share of the purse lifts it clear of its trip line; a visit lifts every line clear, the cheapest first, before
/// it tops any up; the sale counts only what it will offer and is called only when its payout pays a want.
/// </summary>
public sealed partial class LogisticsCountersTests
{
    private const uint SilverScarab = 688;
    private const uint MetalCap = 46;
    private const uint Essence = 48_972;
    private const uint PathwardenRobe = 40_439;
    private const int SprintPurse = 6_082; // the coin at 01:39 UTC, after the third visit
    private const int PurseAfterTheFirstVisit = 2_231; // the coin at 00:49 UTC, when the first sale trip was called

    [Theory]
    [InlineData(4, 2, 2, 3)] // the outskirts' full-formula lines now
    [InlineData(2, 2, 2, 2)] // as they were: the keep on the line, nothing a visit buys lifts it clear
    [InlineData(6, 3, 2, 5)] // Iron Scarab
    [InlineData(3, 1, 0, 2)] // Silver Scarab
    [InlineData(50, 2, 0, 26)] // the outskirts' Mana Potion
    [InlineData(100, 10, 0, 55)] // the Lodge's
    [InlineData(400, 130, 4, 265)] // the Lodge's Prismatic Taper
    public void Clear_of_the_trip_line_is_halfway_from_the_line_to_the_keep(int keep, int tripUnder, int stopUnder, int clear)
    {
        Assert.Equal(clear, LogisticsCounters.ClearOf(new StockLine(1, "ware", keep, tripUnder, stopUnder)));
    }

    [Fact]
    public void No_outskirts_line_with_a_trip_or_a_stop_line_keeps_only_to_that_line()
    {
        var ground = Ground(Outskirts);
        foreach (var line in ground.Supplies.Stock.Where(l => Math.Max(l.TripUnder, l.StopUnder) > 0))
        {
            Assert.True(LogisticsCounters.ClearOf(line) > Math.Max(line.TripUnder, line.StopUnder), $"{line.Name}: keep {line.Keep}, trip_under {line.TripUnder}, stop_under {line.StopUnder}: a visit leaves it on its line");
        }
    }

    [Fact]
    public void A_line_under_its_trip_line_calls_a_trip_only_when_one_trips_share_of_the_purse_lifts_it_clear()
    {
        var ground = Ground(Outskirts);
        var line = ground.Supplies.Stock.Single(l => l.Wcid == SilverScarab);
        Assert.Equal((3, 1, 0), (line.Keep, line.TripUnder, line.StopUnder));
        var price = ground.PriceOf(SilverScarab)!.Value;
        var percent = ground.Supplies.Purse.TripSpendPercent;
        var cost = (long)price * LogisticsCounters.ClearOf(line); // none held: two scarabs
        var purse = (int)(((cost * 100) + percent - 1) / percent);

        LogisticsCounters Counters() => new(ground, learnHolds: new LearnHolds(), componentNeeds: new ComponentNeeds());
        Pack Poor(int coin)
        {
            var pack = new Pack(With(ground, SilverScarab, 0), burden: 100);
            pack.R.PrivateInt(PlayerQualities.CoinValueProperty, coin);
            return pack;
        }

        var counters = Counters();
        var index = IndexOf(counters, SilverScarab);
        Assert.Equal(LogisticsKind.None, counters.Check(Poor(price).State).Kind); // the old rule's coin: one scarab, to the trip line, and a visit that leaves her one use from the next
        Assert.True(counters.IsHeldBack(index));
        Assert.Equal(purse, counters.HeldBackCoin(index));

        Assert.Equal(LogisticsKind.None, Counters().Check(Poor(purse - 1).State).Kind);
        var paid = Counters().Check(Poor(purse).State);
        Assert.Equal((LogisticsKind.Trip, "stock:Silver Scarab"), (paid.Kind, paid.Trigger));
    }

    [Fact]
    public void The_sprints_purse_calls_no_Mana_Potion_trip_that_would_buy_a_handful()
    {
        // 6,082 coin, the potions run under their trip line of 2: the old rule called the trip (230 pays one potion to the line); the trip's share, 3,649, does not lift the line clear (26: 25 potions, 5,750), so it waits by name.
        var ground = Ground(Outskirts);
        var pack = new Pack(With(ground, ManaPotion, 1), burden: 100);
        pack.R.PrivateInt(PlayerQualities.CoinValueProperty, SprintPurse);
        var counters = new LogisticsCounters(ground, learnHolds: new LearnHolds(), componentNeeds: new ComponentNeeds());

        var verdict = counters.Check(pack.State);

        Assert.Equal(LogisticsKind.None, verdict.Kind);
        Assert.True(counters.IsHeldBack(IndexOf(counters, ManaPotion)));
        Assert.Equal(9_584, counters.HeldBackCoin(IndexOf(counters, ManaPotion))); // 5,750 is 60% of 9,584
    }

    /// <summary>The pack at 00:51 UTC: the stock at its keeps but one Mana Potion, the two Metal Caps and the Acid Zombie Essence the first kill dropped (13,988 Value) and 1,110 Value of older loot.</summary>
    private static Pack SprintPack(HuntingGround ground, bool robeWorn, int coin)
    {
        var stacks = With(ground, ManaPotion, 1);
        stacks.AddRange([(MetalCap, 1u), (MetalCap, 1u), (Essence, 1u), (LootWcid, 1u)]);
        var pack = new Pack(stacks, burden: 100, values: new Dictionary<uint, uint> { [MetalCap] = 4_994, [Essence] = 4_000, [LootWcid] = 1_110 }, worn: robeWorn ? [PathwardenRobe] : []);
        pack.R.PrivateInt(PlayerQualities.CoinValueProperty, coin);
        return pack;
    }

    [Fact]
    public void The_sale_counts_only_what_it_will_offer_so_the_sprints_first_sale_trip_is_not_called()
    {
        var ground = Ground(Outskirts);
        Assert.Equal(15_000, ground.Loot!.SaleTripValue);
        var counters = new LogisticsCounters(ground, learnHolds: new LearnHolds(), componentNeeds: new ComponentNeeds());
        var pack = SprintPack(ground, robeWorn: true, coin: PurseAfterTheFirstVisit);

        var verdict = counters.Check(pack.State);

        Assert.True(counters.IsHeldBack(IndexOf(counters, ManaPotion))); // a want the coin cannot pay: the old rule's condition for a sale trip
        Assert.Equal(LogisticsKind.None, verdict.Kind); // the old count was 15,098 against the line of 15,000: a trip of 501 s that sold one item for 3,600
        Assert.Equal(5_110, counters.CarriedSaleValue(pack.State)); // the two caps (9,988) are pieces of the leather set: the sale keeps them while she wears the robe
        Assert.Equal(3_600 + 999, counters.CarriedSalePayout(pack.State)); // the shopkeeper pays 0.9 of the Value
        Assert.False(counters.SaleWaits); // under the sale line: nothing to say
    }

    [Fact]
    public void With_the_swap_done_the_caps_are_loot_again_and_the_sale_that_pays_the_want_is_one_trip()
    {
        var ground = Ground(Outskirts);
        var counters = new LogisticsCounters(ground, learnHolds: new LearnHolds(), componentNeeds: new ComponentNeeds());
        var pack = SprintPack(ground, robeWorn: false, coin: PurseAfterTheFirstVisit);

        var verdict = counters.Check(pack.State);

        Assert.Equal(15_098, counters.CarriedSaleValue(pack.State));
        var payout = (2 * 4_494) + 3_600 + 999;
        Assert.Equal(payout, counters.CarriedSalePayout(pack.State));
        Assert.Equal((LogisticsKind.Trip, LogisticsCause.Sale, "sale"), (verdict.Kind, verdict.Cause, verdict.Trigger)); // 2,231 + 13,587 pays the potions clear of their line (a purse of 9,584)
        Assert.Equal(payout, verdict.SalePayout);
        Assert.Contains("the coin cannot pay what is wanted; its sale (about 13587 pyreals) does", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sale_that_would_not_raise_enough_calls_no_trip_and_waits_by_name()
    {
        var outskirts = Ground(Outskirts);
        var ground = outskirts with { Loot = outskirts.Loot! with { SaleTripValue = 5_000 } }; // a line the loot reaches while its payout is still short of the want
        var counters = new LogisticsCounters(ground, learnHolds: new LearnHolds(), componentNeeds: new ComponentNeeds());
        var pack = SprintPack(ground, robeWorn: true, coin: PurseAfterTheFirstVisit); // the sale would pay 4,599: 6,830 with the coin, under the 9,584 the potions want

        var verdict = counters.Check(pack.State);

        Assert.Equal(LogisticsKind.None, verdict.Kind);
        Assert.True(counters.SaleWaits);
        var note = counters.SaleWaitNote(pack.State);
        Assert.NotNull(note);
        Assert.Contains("sale held back: the loot the sale would offer is worth 5110 Value (the sale line 5000) and would pay about 4599; with the purse 2231 that is 6830, which does not pay Mana Potion (a purse of 9584)", note, StringComparison.Ordinal);

        var richer = SprintPack(ground, robeWorn: true, coin: 9_584 - 4_599); // the coin that, with the sale, pays it
        var due = new LogisticsCounters(ground, learnHolds: new LearnHolds(), componentNeeds: new ComponentNeeds());
        Assert.Equal(LogisticsCause.Sale, due.Check(richer.State).Cause);
        Assert.False(due.SaleWaits);
        Assert.Null(due.SaleWaitNote(richer.State));
    }

    /// <summary>The stock after the sprint's second visit: every line at its keep but the Mana Potion at 13 and the Silver Scarab at 1 (on its trip line).</summary>
    private static (LogisticsCounters Counters, HuntingGround Ground) AfterTheSecondVisit()
    {
        var ground = Ground(Outskirts);
        var stacks = ground.Supplies.Stock.Select(l => (l.Wcid, (uint)(l.Wcid == ManaPotion ? 13 : l.Wcid == SilverScarab ? 1 : l.Keep))).ToList();
        var counters = new LogisticsCounters(ground, learnHolds: new LearnHolds(), componentNeeds: new ComponentNeeds());
        Assert.NotEqual(LogisticsKind.Unread, counters.Check(new Pack(stacks, burden: 100).State).Kind);
        return (counters, ground);
    }

    [Fact]
    public void A_visit_lifts_every_line_clear_of_its_trip_line_the_cheapest_first_before_it_tops_any_up()
    {
        // The third visit bought 27 Mana Potions (6,197) and no Silver Scarab: the potion is before the scarab in the profile and took the budget. With 3,000 to count (a share of 1,800) the scarab's one (338) comes first, then the
        // potions toward their clear count of 26 (six fit the 1,462 left).
        var (counters, ground) = AfterTheSecondVisit();

        var list = ResupplyPlanner.BuyList(ground, counters, 3_000);

        Assert.Equal(1, list.Single(b => b.Line.Wcid == SilverScarab).Count);
        Assert.Equal(6, list.Single(b => b.Line.Wcid == ManaPotion).Count);
        Assert.Equal(2, list.Count);
        Assert.True(list.Sum(b => (long)b.Count * ground.PriceOf(b.Line.Wcid)!.Value) <= 1_800);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(3_000)]
    [InlineData(6_082)]
    [InlineData(14_658)] // the third visit's purse
    [InlineData(40_000)]
    public void No_line_is_topped_up_past_its_clear_count_while_another_the_budget_could_lift_sits_short_of_its_own(int purse)
    {
        var (counters, ground) = AfterTheSecondVisit();

        var list = ResupplyPlanner.BuyList(ground, counters, purse);

        int After(uint wcid) => counters.HeldOf(IndexOf(counters, wcid)) + (list.SingleOrDefault(b => b.Line.Wcid == wcid)?.Count ?? 0);
        var potion = ground.Supplies.Stock.Single(l => l.Wcid == ManaPotion);
        var scarab = ground.Supplies.Stock.Single(l => l.Wcid == SilverScarab);
        if (After(ManaPotion) > LogisticsCounters.ClearOf(potion))
        {
            Assert.True(After(SilverScarab) >= LogisticsCounters.ClearOf(scarab), $"purse {purse}: {After(ManaPotion)} potions and {After(SilverScarab)} Silver Scarab(s)");
        }

        if (purse >= 40_000)
        {
            Assert.Equal(scarab.Keep, After(SilverScarab)); // a purse whose share pays every keep (9,186 here) buys every keep
            Assert.Equal(potion.Keep, After(ManaPotion));
        }
        else if (purse >= 6_082)
        {
            Assert.Equal(LogisticsCounters.ClearOf(scarab), After(SilverScarab)); // the scarab off its trip line from the sprint's purse on; the potions take what is left
        }

        Assert.True(list.Sum(b => (long)b.Count * ground.PriceOf(b.Line.Wcid)!.Value) <= (long)purse * ground.Supplies.Purse.TripSpendPercent / 100);
    }

    [Fact]
    public void A_learn_hold_asked_about_a_purse_she_does_not_hold_yet_releases_nothing()
    {
        var holds = new LearnHolds();
        Assert.True(holds.Hold(82, 5_832, 6)); // Flame Bolt III's hold of the second visit: until the coin reaches 5,832 or the level passes 6

        Assert.True(holds.Standing(82, 2_231, 6));
        Assert.False(holds.Standing(82, 2_231 + 12_639, 6)); // the third visit's sale would release it
        Assert.False(holds.Standing(82, 2_231, 7));
        Assert.False(holds.Standing(81, 0, 6)); // no hold
        Assert.Equal(1, holds.Count); // asked, not released
        Assert.True(holds.Holds(82, 2_231, 6));
    }
}

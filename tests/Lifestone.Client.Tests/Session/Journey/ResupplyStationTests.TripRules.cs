using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 T (the sprint of 2026-11-12: seven scrolls wanted with 2,231 coin, and two sale trips inside nine minutes, the first selling one item for 3,600): a scroll the purse cannot pay calls no trip and is named; a sale is called
/// for it only when its payout, with the coin, pays the scrolls' batch; then the one trip sells and buys.
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const uint SaleLoot = 910_001;

    /// <summary>Counters over the Lodge with its sale line set to <paramref name="saleLine"/> (the profile's 100,000 is a dungeon's; the rule is the same at any line).</summary>
    private static LogisticsCounters SaleCounters(Home home, int saleLine) =>
        new(home.Ground with { Loot = home.Ground.Loot! with { SaleTripValue = saleLine } }, learnHolds: new LearnHolds(), componentNeeds: new ComponentNeeds());

    [Fact]
    public void M6_14_T_scrolls_the_purse_cannot_pay_call_no_trip_and_are_named_once()
    {
        // The Lodge's first due scroll is the press (50), the next Armor Self II (250): one trip's share (60%) pays the two from 500 coin.
        var poor = BurrowHome(100, BurrowClimber());
        var counters = SaleCounters(poor, 300);

        Assert.Equal(LogisticsKind.None, counters.Check(poor.Vendor.State).Kind);
        Assert.False(counters.SaleWaits); // no loot at the sale line
        var note = counters.LearnWaitNote(poor.Vendor.State);
        Assert.NotNull(note);
        Assert.StartsWith("learn held back: ", note, StringComparison.Ordinal);
        Assert.Contains("the first Stamina to Mana Self I, 50) and the purse 100 does not pay the next 2 inside the trip's share (60%)", note, StringComparison.Ordinal);
        Assert.NotEqual(0, counters.LearnWaitKey);
        var again = SaleCounters(poor, 300);
        again.Check(poor.Vendor.State);
        Assert.Equal(counters.LearnWaitKey, again.LearnWaitKey); // the same scrolls waiting: the same key, nothing new to say

        var paid = BurrowHome(500, BurrowClimber());
        var due = SaleCounters(paid, 300);
        Assert.Equal("learn:Stamina to Mana Self I", due.Check(paid.Vendor.State).Trigger);
        Assert.Null(due.LearnWaitNote(paid.Vendor.State));
        Assert.Equal(0, due.LearnWaitKey);
    }

    [Fact]
    public void M6_14_T_a_sale_is_called_for_wanted_scrolls_only_when_its_payout_with_the_coin_pays_their_batch()
    {
        var thin = BurrowHome(100, BurrowClimber());
        thin.Own("Drudge Charm", SaleLoot, MiscType, 300); // at the sale line; the shopkeeper pays 270: 370 with the coin, under the 500 the two scrolls want
        var waits = SaleCounters(thin, 300);

        Assert.Equal(LogisticsKind.None, waits.Check(thin.Vendor.State).Kind);
        Assert.True(waits.SaleWaits);
        Assert.Equal(270, waits.CarriedSalePayout(thin.Vendor.State));
        Assert.Contains("would pay about 270; with the purse 100 that is 370, which does not pay the next scrolls (the first Stamina to Mana Self I, 50): no trip", waits.SaleWaitNote(thin.Vendor.State), StringComparison.Ordinal);

        var paying = BurrowHome(100, BurrowClimber());
        paying.Own("Drudge Charm", SaleLoot, MiscType, 500); // 450 from the sale: 550 with the coin pays the two scrolls inside the share
        var due = SaleCounters(paying, 300);

        var verdict = due.Check(paying.Vendor.State);

        Assert.Equal((LogisticsKind.Trip, LogisticsCause.Sale, "sale"), (verdict.Kind, verdict.Cause, verdict.Trigger)); // one trip: it sells, then buys the scrolls from the purse the sale leaves
        Assert.Equal(450, verdict.SalePayout);
        Assert.False(due.SaleWaits);

        var underTheLine = SaleCounters(paying, 501); // the same loot under the ground's sale line: no trip, whatever it would pay
        Assert.Equal(LogisticsKind.None, underTheLine.Check(paying.Vendor.State).Kind);
        Assert.False(underTheLine.SaleWaits);
    }
}

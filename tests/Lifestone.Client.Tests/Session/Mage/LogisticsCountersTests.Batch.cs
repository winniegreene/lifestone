using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 AT (live, 2026-11-06: four of nine buff lines lapsed 70 minutes for want of a Blue Taper and nothing sent her home; a pack of unsold loot beside 761 coin; five learn trips in 25 minutes, one scroll each): a pass short of a
/// component calls a trip by name, once, at a keep of two passes; a sale trip is called when the loot carried is worth the data line while the coin cannot pay what is wanted.
/// </summary>
public sealed partial class LogisticsCountersTests
{
    private const uint BlueTaper = 1643;
    private const string Outskirts = "holtburg-outskirts";

    private static ComponentNeeds TwoLinesShortOfBlueTaper()
    {
        var needs = new ComponentNeeds();
        Assert.True(needs.Note(BlueTaper, "Blue Taper", "Willpower Self", 1));
        Assert.True(needs.Note(BlueTaper, "Blue Taper", "Focus Self", 1));
        Assert.False(needs.Note(BlueTaper, "Blue Taper", "Focus Self", 1)); // the same line again: no change, no log
        return needs;
    }

    [Fact]
    public void A_pass_short_of_a_component_calls_one_trip_by_its_name_under_one_pass_at_a_keep_of_two_passes_above_it_and_the_stocked_line_calls_none()
    {
        var ground = Ground(Outskirts);
        var needs = TwoLinesShortOfBlueTaper();
        Assert.Equal(2 * ComponentNeeds.PassesKept, needs.WantOf(BlueTaper)); // two lines a pass, two passes

        var quiet = new LogisticsCounters(ground, learnHolds: new LearnHolds(), componentNeeds: new ComponentNeeds()).Check(new Pack(With(ground, BlueTaper, 0), burden: 100).State);
        Assert.Equal(LogisticsKind.None, quiet.Kind); // the profile's own line (trip at none): the live lapse

        var counters = new LogisticsCounters(ground, learnHolds: new LearnHolds(), componentNeeds: needs);
        var due = counters.Check(new Pack(With(ground, BlueTaper, 1), burden: 100).State);

        Assert.Equal((LogisticsKind.Trip, LogisticsCause.StockLow, "stock:Blue Taper"), (due.Kind, due.Cause, due.Trigger));
        Assert.Equal((6, 2), (counters.LineAt(IndexOf(counters, BlueTaper)).Keep, counters.LineAt(IndexOf(counters, BlueTaper)).TripUnder)); // M6 sprint: the trip line one pass (2), the keep two passes above it (2 + 4)
        Assert.Contains("1 held, under its trip line 2", due.Reason, StringComparison.Ordinal);

        Assert.Equal(LogisticsKind.None, counters.Check(new Pack(With(ground, BlueTaper, 6), burden: 100).State).Kind); // bought to the keep: once
        Assert.Equal(LogisticsKind.None, counters.Check(new Pack(With(ground, BlueTaper, 2), burden: 100).State).Kind); // M6 sprint (run 6): two passes burned from the keep call no trip; one pass is still castable
    }

    [Fact]
    public void A_component_trip_the_coin_cannot_pay_is_held_back_by_name_and_buys_in_the_plans_order_when_it_can()
    {
        var ground = Ground(Outskirts);
        var counters = new LogisticsCounters(ground, learnHolds: new LearnHolds(), componentNeeds: TwoLinesShortOfBlueTaper());
        var line = IndexOf(counters, BlueTaper);
        var price = ground.PriceOf(BlueTaper);
        Assert.NotNull(price);

        var poor = new Pack(With(ground, BlueTaper, 0), burden: 100);
        poor.R.PrivateInt(Lifestone.Client.Session.Contracts.PlayerQualities.CoinValueProperty, price!.Value - 1);
        Assert.Equal(LogisticsKind.None, counters.Check(poor.State).Kind);
        Assert.True(counters.IsHeldBack(line));

        var paid = new Pack(With(ground, BlueTaper, 0), burden: 100);
        paid.R.PrivateInt(Lifestone.Client.Session.Contracts.PlayerQualities.CoinValueProperty, PurseThatLifts(ground, counters.LineAt(line), held: 0)); // M6 14 T: the four tapers inside one trip's share of the purse
        Assert.Equal("stock:Blue Taper", new LogisticsCounters(ground, learnHolds: new LearnHolds(), componentNeeds: TwoLinesShortOfBlueTaper()).Check(paid.State).Trigger);
    }

    [Fact]
    public void A_component_no_stock_line_carries_is_named_for_the_station_to_log()
    {
        var ground = Ground(Outskirts);
        var needs = new ComponentNeeds();
        needs.Note(999_999, "Phantom Taper", "Focus Self", 1);
        var counters = new LogisticsCounters(ground, learnHolds: new LearnHolds(), componentNeeds: needs);

        Assert.Equal([999_999u], counters.UnstockedNeeds().ToArray());
        Assert.Empty(new LogisticsCounters(ground, learnHolds: new LearnHolds(), componentNeeds: TwoLinesShortOfBlueTaper()).UnstockedNeeds());
    }

    private const uint LootWcid = 910_001;

    [Theory]
    [InlineData(20_000u, true)]
    [InlineData(15_000u, true)]
    [InlineData(14_999u, false)]
    public void Loot_worth_the_data_line_calls_a_sale_trip_while_a_wanted_line_is_held_back_by_the_coin(uint value, bool trip)
    {
        var ground = Ground(Outskirts);
        Assert.Equal(15_000, ground.Loot!.SaleTripValue); // the default, data
        var counters = new LogisticsCounters(ground, learnHolds: new LearnHolds(), componentNeeds: TwoLinesShortOfBlueTaper());
        var stacks = With(ground, BlueTaper, 0);
        stacks.Add((LootWcid, 1u));
        var pack = new Pack(stacks, burden: 100, values: new Dictionary<uint, uint> { [LootWcid] = value });
        pack.R.PrivateInt(Lifestone.Client.Session.Contracts.PlayerQualities.CoinValueProperty, ground.PriceOf(BlueTaper)!.Value * 4 - 1); // a pyreal short of the four tapers (the live stretch held 761)

        var verdict = counters.Check(pack.State);

        Assert.Equal(trip, verdict.Kind == LogisticsKind.Trip);
        if (trip)
        {
            Assert.Equal((LogisticsCause.Sale, "sale"), (verdict.Cause, verdict.Trigger));
            Assert.Equal((int)value, verdict.Held);
            Assert.Contains("the coin cannot pay what is wanted", verdict.Reason, StringComparison.Ordinal);
        }

        Assert.Equal(value, (uint)counters.CarriedSaleValue(pack.State));
    }

    [Fact]
    public void M6_16_E5c2_what_the_sale_keeps_is_no_sale_value_so_it_calls_no_sale_trip()
    {
        // The state of the theory above with the 20,000 Value on a quest file's item, which the sale keeps (pack rule quest-item). Before this slice the estimate's own list counted it (an item with a Value that is no
        // stock line, focus, note, coin, caster or container): 20,000 against the line of 15,000 called a sale trip that would have sold nothing.
        var ground = Ground(Outskirts);
        var quest = Lifestone.Client.Session.Scenarios.Journey.ResupplyRun.QuestItemWcids.First();
        var counters = new LogisticsCounters(ground, learnHolds: new LearnHolds(), componentNeeds: TwoLinesShortOfBlueTaper());
        var stacks = With(ground, BlueTaper, 0);
        stacks.Add((quest, 1u));
        var pack = new Pack(stacks, burden: 100, values: new Dictionary<uint, uint> { [quest] = 20_000 });
        pack.R.PrivateInt(Lifestone.Client.Session.Contracts.PlayerQualities.CoinValueProperty, ground.PriceOf(BlueTaper)!.Value * 4 - 1);

        var verdict = counters.Check(pack.State);

        Assert.Equal(LogisticsKind.None, verdict.Kind);
        Assert.Equal(0, counters.CarriedSaleValue(pack.State));
        Assert.Equal((0L, 0L), counters.SaleEstimate);
    }

    [Fact]
    public void Loot_alone_calls_no_sale_trip_when_nothing_is_wanted_and_the_stock_trip_outranks_it_when_the_coin_pays()
    {
        var ground = Ground(Outskirts);
        var stacks = With(ground, BlueTaper, 0);
        stacks.Add((LootWcid, 1u));
        var values = new Dictionary<uint, uint> { [LootWcid] = 40_000 };

        var nothingWanted = new Pack(stacks, burden: 100, values: values);
        nothingWanted.R.PrivateInt(Lifestone.Client.Session.Contracts.PlayerQualities.CoinValueProperty, 761);
        Assert.Equal(LogisticsKind.None, new LogisticsCounters(ground, learnHolds: new LearnHolds(), componentNeeds: new ComponentNeeds()).Check(nothingWanted.State).Kind);

        var rich = new Pack(stacks, burden: 100, values: values); // the rig's coin pays any trip
        Assert.Equal(LogisticsCause.StockLow, new LogisticsCounters(ground, learnHolds: new LearnHolds(), componentNeeds: TwoLinesShortOfBlueTaper()).Check(rich.State).Cause);
    }

    [Fact]
    public void The_trip_lines_are_data_with_a_default_and_every_ground_with_a_loot_section_carries_the_sale_line()
    {
        Assert.Equal(GroundPurse.DefaultLearnTripCoin, Ground(Outskirts).Supplies.Purse.LearnTripCoin);
        Assert.Equal(GroundLoot.DefaultSaleTripValue, Ground(Outskirts).Loot!.SaleTripValue);
        foreach (var key in new[] { "tusker-burrow", "tusker-lodge" })
        {
            Assert.Equal(100_000, Ground(key).Loot!.SaleTripValue); // a trip from a dungeon costs about 14 minutes
        }
    }
}

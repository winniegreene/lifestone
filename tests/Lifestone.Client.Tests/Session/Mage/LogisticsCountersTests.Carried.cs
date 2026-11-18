using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 16 E5d: the verdict a trip carries (<see cref="TripOrder"/>, <see cref="TripDesk.Call(Lifestone.Client.Session.Grounds.HuntingGround, LogisticsVerdict, double)"/>). What these prove: the order holds what the
/// trigger decided (the reason, every stock line with its held and wanted counts, the purse it read); the count at home is compared with it and each difference is named, or the verdict is said to stand; the check after
/// says whether the reason was served; a call is taken once, by the station of its ground, and a call the fight outlived is void.
/// </summary>
public sealed partial class LogisticsCountersTests
{
    [Fact]
    public void M6_16_E5d_the_order_holds_what_the_trigger_decided_the_reason_the_lines_and_the_purse()
    {
        var ground = Ground(Den);
        var low = ground.Supplies.Stock.First(l => !ground.IsCarried(l.Wcid) && l.TripUnder > 1);
        var pack = new Pack(With(ground, low.Wcid, low.TripUnder - 1), burden: 100);
        pack.R.PrivateInt(PlayerQualities.CoinValueProperty, 250_000);
        var desk = TripDesk.For(pack.State);
        var verdict = desk.On(ground).Check(pack.State);
        Assert.Equal("stock:" + low.Name, verdict.Trigger);

        var order = desk.Call(ground, verdict, now: 100);

        Assert.Same(order, desk.Called);
        Assert.Equal((ground.Key, verdict, "stock:" + low.Name, verdict.Reason, 100.0), (order.Ground, order.Verdict, order.Trigger, order.Reason, order.CalledAt));
        Assert.Equal((250_000L, 0L, 250_000L), (order.Coin, order.Notes, order.Purse));
        Assert.Null(order.SaleValue); // the trigger made no sale estimate: the den names no sale line
        Assert.Equal(ground.Supplies.Stock.Count, order.Lines.Count);
        var line = order.Named!;
        Assert.Equal((low.Name, low.Wcid, low.TripUnder - 1, low.TripUnder, low.Keep, LogisticsCounters.ClearOf(low)), (line.Name, line.Wcid, line.Held, line.TripUnder, line.Keep, line.Clear));
        Assert.All(order.Lines.Where(l => l.Wcid != low.Wcid), l => Assert.Equal(ground.Supplies.StockOf(l.Wcid)!.Keep, l.Held));
        Assert.True(order.CountersReadIt);
        Assert.Equal(
            $"stock:{low.Name} ({low.Name}: {low.TripUnder - 1} held, under its trip line {low.TripUnder}, wanted {low.Keep}, clear of its line at {LogisticsCounters.ClearOf(low)}; purse 250000: coin 250000, notes 0)",
            order.Said);
    }

    [Fact]
    public void M6_16_E5d_the_count_at_home_is_compared_with_the_order_and_each_difference_is_named()
    {
        var ground = Ground(Den);
        var stock = ground.Supplies.Stock.Where(l => !ground.IsCarried(l.Wcid) && l.TripUnder > 1).ToList();
        var (low, other) = (stock[0], stock[1]);
        var field = new Pack(With(ground, low.Wcid, low.TripUnder - 1), burden: 100);
        field.R.PrivateInt(PlayerQualities.CoinValueProperty, 250_000);
        var desk = TripDesk.For(field.State);
        var counters = desk.On(ground);
        var order = desk.Call(ground, counters.Check(field.State), now: 100);

        // nothing changed on the way home: the verdict stands
        Assert.Equal("trip: the verdict stands at home: " + order.Said, order.AtHome(counters, counters.Check(field.State), field.State));

        // on the way home another line fell under its trip line and the purse fell: both named, with what the count at home says
        List<(uint Wcid, uint Count)> stacks = [.. ground.Supplies.Stock.Select(l => (l.Wcid, (uint)(l.Wcid == low.Wcid ? low.TripUnder - 1 : l.Wcid == other.Wcid ? other.TripUnder - 1 : l.Keep)))];
        var home = new Pack(stacks, burden: 100);
        home.R.PrivateInt(PlayerQualities.CoinValueProperty, 200_000);
        var found = counters.Check(home.State);
        var said = order.AtHome(counters, found, home.State);
        Assert.StartsWith($"trip: the verdict said {order.Said}, at home {found.Trigger} ({found.Reason}; purse 200000: coin 200000, notes 0): ", said, StringComparison.Ordinal);
        Assert.Contains($"{other.Name} {other.Keep} held is now {other.TripUnder - 1}", said, StringComparison.Ordinal);
        Assert.EndsWith("the purse 250000 is now 200000", said, StringComparison.Ordinal);
        Assert.DoesNotContain($"{low.Name} {low.TripUnder - 1} held is now", said, StringComparison.Ordinal); // the named line is as the trigger counted it

        // the reason itself changed: the line the trip was called for is back over its trip line and nothing is due
        var cured = new Pack(Stocked(ground), burden: 100);
        cured.R.PrivateInt(PlayerQualities.CoinValueProperty, 250_000);
        var none = counters.Check(cured.State);
        Assert.Equal(
            $"trip: the verdict said {order.Said}, at the fight's end none (all well; purse 250000: coin 250000, notes 0): the reason stock:{low.Name} is now none; {low.Name} {low.TripUnder - 1} held is now {low.Keep}",
            order.AtHome(counters, none, cured.State, "at the fight's end"));
    }

    [Fact]
    public void M6_16_E5d_the_check_after_says_whether_the_reason_the_trip_was_called_for_was_served()
    {
        var ground = Ground(Den);
        var low = ground.Supplies.Stock.First(l => !ground.IsCarried(l.Wcid) && l.TripUnder > 1);
        var field = new Pack(With(ground, low.Wcid, low.TripUnder - 1), burden: 100);
        var desk = TripDesk.For(field.State);
        var counters = desk.On(ground);
        var order = desk.Call(ground, counters.Check(field.State), now: 100);

        // the visit bought the line back to its keep
        var bought = new Pack(Stocked(ground), burden: 100);
        var after = counters.Check(bought.State);
        Assert.True(order.Served(counters, after));
        Assert.Equal(
            $"trip: check after: the verdict's reason is served: stock:{low.Name} ({low.Name} {low.TripUnder - 1} held when called, {low.Keep} now, its trip line {low.TripUnder}); the counters now say None: all well",
            order.After(counters, after));

        // the visit bought nothing of it (no shop answered): the reason still stands, a warning
        var still = counters.Check(field.State);
        Assert.False(order.Served(counters, still));
        Assert.StartsWith($"WARNING: trip: check after: the verdict's reason still stands after the visit: stock:{low.Name} ({low.Name} {low.TripUnder - 1} held when called, {low.TripUnder - 1} now, its trip line {low.TripUnder}); the counters say Trip: ", order.After(counters, still), StringComparison.Ordinal);

        // a trip no counters call (the buff bot's): nothing at home reads its reason again
        var bot = desk.Call(ground, "buff-bot", "the buff bot's lines are about to lapse in the field", now: 200);
        Assert.False(bot.CountersReadIt);
        Assert.Null(bot.Served(counters, still));
        Assert.Equal("trip: check after: the verdict's reason (buff-bot) is not one the counters read again at home (the schedule's, the hour's, the buff bot's or a lapsed line's): nothing to compare", bot.After(counters, still));
        Assert.StartsWith("buff-bot (the buff bot's lines are about to lapse in the field; purse ", bot.Said, StringComparison.Ordinal);
        Assert.Equal(low.TripUnder - 1, bot.Lines.First(l => l.Wcid == low.Wcid).Held); // the counts as the trigger's last check left them
    }

    [Fact]
    public void M6_16_E5d_a_call_is_taken_once_by_the_station_of_its_ground_and_a_call_the_fight_outlived_is_void()
    {
        var ground = Ground(Den);
        var fort = Ground("tusker-temple-fort-tethana");
        var state = new Pack(Stocked(ground), burden: 100).State;
        var desk = TripDesk.For(state);
        var verdict = desk.On(ground).Check(state);
        Assert.Null(desk.Take(ground)); // no trigger called: a forced trip, a plan's
        Assert.Null(desk.InHand);

        var order = desk.Call(ground, verdict, 1);
        Assert.Same(order, desk.Take(ground));
        Assert.Same(order, desk.InHand); // the town trip reads it
        Assert.Null(desk.Called);
        desk.Close();
        Assert.Null(desk.InHand);
        Assert.Null(desk.Take(ground)); // taken once

        desk.Call(ground, verdict, 2);
        desk.Void(); // the fight went on and asked nothing, or she died
        Assert.Null(desk.Take(ground));

        desk.Call(ground, verdict, 3);
        Assert.Null(desk.Take(fort)); // another ground's station takes nothing, and the call is gone
        Assert.Null(desk.Called);

        desk.Call(ground, verdict, 4);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            desk.Void(); // every frame of a fight that asks nothing
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}

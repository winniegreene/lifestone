using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Magic;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 sprint (the owner's decision of 2026-11-19): run 5's first trip (cache/m6/sprint/run5/step3-levelup.log 14:24:12 UTC, purse 10,009, the Redoubt's share 60%) left 12 herb, powder and talisman lines at their stop line 2
/// of keep 4 and two tapers at 1 of 2, one burn from a trip each, and walked out with 2,051. Once the trip's share is spent, a casting component's line (a stop line, a spell component) is topped up to its keep from the rest of the purse.
/// </summary>
public sealed partial class LogisticsCountersTests
{
    private const string Redoubt = "holtburg-redoubt";

    private static bool Casting(StockLine line) => line.StopUnder > 0 && ComponentWeenies.TryGetByWcid(line.Wcid, out _);

    private static (LogisticsCounters Counters, HuntingGround Ground) EmptyRedoubt(long coin)
    {
        var ground = Ground(Redoubt);
        var counters = new LogisticsCounters(ground);
        var pack = new Pack([.. Stocked(ground).Select(s => (s.Wcid, 0u))], burden: 100); // a fresh climber: every line empty
        pack.R.PrivateInt(PlayerQualities.CoinValueProperty, (int)coin);
        counters.Check(pack.State);
        return (counters, ground);
    }

    /// <summary>The list's spend on everything but the casting components' lines (the scrolls and the other lines), at the ground's prices.</summary>
    private static long OutsideCasting(IReadOnlyList<ResupplyBuy> list, HuntingGround ground) =>
        list.Where(b => ground.Supplies.Stock.FirstOrDefault(l => l.Wcid == b.Line.Wcid) is not { } l || !Casting(l)).Sum(b => (long)b.Count * (ground.PriceOf(b.Line.Wcid) ?? 0));

    private static long CostToKeeps(HuntingGround ground, Func<StockLine, bool> which) =>
        ground.Supplies.Stock.Where(which).Sum(l => (long)(ground.PriceOf(l.Wcid) ?? 0) * l.Keep);

    [Fact]
    public void M6_sprint_the_casting_components_reach_their_keeps_from_the_rest_of_the_purse_when_the_share_is_spent()
    {
        var probe = Ground(Redoubt);
        Assert.Contains(probe.Supplies.Stock, Casting); // the Redoubt carries casting lines (Grey Taper: stop 2, keep 4)
        var all = CostToKeeps(probe, _ => true);
        var coin = all; // the whole purse pays every keep; its 60% share does not
        Assert.True(coin * probe.Supplies.Purse.TripSpendPercent / 100 < all);
        var (counters, ground) = EmptyRedoubt(coin);

        var list = ResupplyPlanner.BuyList(ground, counters, coin);

        foreach (var line in ground.Supplies.Stock.Where(Casting).Where(l => ground.PriceOf(l.Wcid) is > 0))
        {
            Assert.True(list.SingleOrDefault(b => b.Line.Wcid == line.Wcid)?.Count >= line.Keep, $"{line.Name}: to its keep {line.Keep}");
        }

        var spent = list.Sum(b => (long)b.Count * (ground.PriceOf(b.Line.Wcid) ?? 0));
        Assert.True(spent <= coin, $"spent {spent} of {coin}");
    }

    [Fact]
    public void M6_sprint_a_component_with_no_stop_line_is_not_topped_up_past_the_share_and_nor_is_any_other_line()
    {
        var probe = Ground(Redoubt);
        var coin = CostToKeeps(probe, _ => true) / 2; // the share runs out well before the keeps
        var (counters, ground) = EmptyRedoubt(coin);
        var share = coin * ground.Supplies.Purse.TripSpendPercent / 100;

        var list = ResupplyPlanner.BuyList(ground, counters, coin);

        var outside = list.Where(b => ground.Supplies.Stock.FirstOrDefault(l => l.Wcid == b.Line.Wcid) is not { } l || !Casting(l)).Sum(b => (long)b.Count * (ground.PriceOf(b.Line.Wcid) ?? 0));
        Assert.True(outside <= share, $"the lines that are not casting components spent {outside}, over the share {share}");
    }
}

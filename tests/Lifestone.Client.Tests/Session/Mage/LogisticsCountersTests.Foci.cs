using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 pack rules (components): with every focus held the account formula's stock lines (the abode's keep-2 tapers, herb, powder, potion and talisman) are suspended: no stop, no trip, no buy, so the sale may sell what is held. A lost
/// focus brings them back.
/// </summary>
public sealed partial class LogisticsCountersTests
{
    private static Pack AbodeWithoutBlueTaper(bool allFoci)
    {
        var ground = Ground("tusker-abode");
        IReadOnlyList<uint> foci = allFoci ? GearKit.MageFociWcids : [GearKit.MageFociWcids[0], GearKit.MageFociWcids[1], GearKit.MageFociWcids[3]];
        return new Pack(With(ground, BlueTaper, 0), foci: foci);
    }

    [Fact]
    public void With_every_focus_held_an_account_formula_line_at_zero_is_no_stop_and_no_buy()
    {
        var ground = Ground("tusker-abode");
        var pack = AbodeWithoutBlueTaper(allFoci: true);
        var counters = new LogisticsCounters(ground);

        var verdict = counters.Check(pack.State);

        Assert.True(counters.AllFociHeld);
        Assert.NotEqual(LogisticsKind.Stop, verdict.Kind);
        var blue = Enumerable.Range(0, counters.LineCount).Single(i => counters.LineAt(i).Wcid == BlueTaper);
        Assert.True(counters.IsSuspended(blue));
        Assert.DoesNotContain(ResupplyPlanner.BuyList(ground, counters), b => b.Line.Wcid == BlueTaper);
        Assert.All(Enumerable.Range(0, counters.LineCount).Where(counters.IsSuspended), i => Assert.True(FociComponents.IsAccountOnly(counters.LineAt(i).Wcid)));
        Assert.False(counters.IsSuspended(Enumerable.Range(0, counters.LineCount).Single(i => counters.LineAt(i).Wcid == FociComponents.PrismaticTaperWcid)));
    }

    [Fact]
    public void A_lost_focus_brings_the_account_formula_line_and_its_stop_back()
    {
        var pack = AbodeWithoutBlueTaper(allFoci: false); // the Foci of Verdancy lost
        var counters = new LogisticsCounters(Ground("tusker-abode"));

        var verdict = counters.Check(pack.State);

        Assert.False(counters.AllFociHeld);
        Assert.Equal(LogisticsKind.Stop, verdict.Kind);
        Assert.Equal(BlueTaper, verdict.Line!.Wcid);
    }
}

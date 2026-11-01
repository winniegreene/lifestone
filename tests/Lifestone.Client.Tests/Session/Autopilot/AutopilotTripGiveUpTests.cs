using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Session;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M5 7 (TODO(m5-trip-failed-continue)): a trip that fails after its one retry never stops the run while every stock line is at or over its stop line: the trip is given up for the cycle, the named gap is logged with the reason, the next trip is
/// held back, and the mage goes back to the fight. A stock truly under its stop line stops the run by name, as before.
/// </summary>
public sealed class AutopilotTripGiveUpTests(ITestOutputHelper output)
{
    private static void Proven(JourneyContext c, string key)
    {
        c.Facts.LifestoneTied = true;
        c.Facts.SetPortalTie(key, PortalTie.Tied);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_trip_that_fails_twice_with_the_stock_over_its_stop_lines_is_given_up_and_the_run_goes_back_to_the_fight(string key)
    {
        var w = new ApWorld(key, output, atHome: false) { Prime = c => Proven(c, key) };
        var trips = 0;
        w.Factory.FailWith = spec => spec.Kind == StationKinds.Logistics && ++trips <= 2 ? "scripted: the vendor was not in reach" : null;
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 1 });

        var status = w.Run(autopilot, a => w.Lines.Any(l => l.Contains("TODO(m5-trip-failed-continue)", StringComparison.Ordinal)) && a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 });

        Assert.Equal(ScenarioStatus.Running, status);
        Assert.Null(autopilot.StopName);
        Assert.Equal(2, trips); // the trip and its one retry
        Assert.Equal(1, w.Lines.Count(l => l.StartsWith("autopilot recovery after trip:", StringComparison.Ordinal)));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot trip: the trip is given up for this cycle", StringComparison.Ordinal) && l.Contains("the vendor was not in reach", StringComparison.Ordinal));
        autopilot.RequestStop("test");
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_trip_that_fails_twice_with_a_line_under_its_stop_line_stops_the_run_by_name(string key)
    {
        var w = new ApWorld(key, output, held: l => l.Name == "Prismatic Taper" ? l.StopUnder - 1 : l.Keep) { Prime = c => Proven(c, key) };
        w.Factory.FailWith = spec => spec.Kind == StationKinds.Logistics ? "scripted: the vendor was not in reach" : null;
        var autopilot = w.Autopilot(new AutopilotOptions());

        Assert.Equal(ScenarioStatus.Failed, w.Run(autopilot));

        Assert.StartsWith("failed:trip:", autopilot.StopName, StringComparison.Ordinal);
        Assert.DoesNotContain(w.Lines, l => l.Contains("TODO(m5-trip-failed-continue)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("Prismatic Taper", StringComparison.Ordinal) && l.Contains("under their stop line", StringComparison.Ordinal));
    }
}

using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Tests.Session.Journey;
using Xunit;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 the dance before running on (the owner, 2026-11-21; docs/session.md section 123): the autopilot hands a travel leg's dance gate the vitals guard's own race (<c>JourneyFacts.Race</c>), and only while a travel leg can run (the travel
/// and death states, as the fight-back's reserve); before the guard is built, or in any other state, the race is not read, so the leg's lull rule stands alone.
/// </summary>
public sealed class AutopilotDanceRaceTests(ITestOutputHelper output)
{
    [Fact]
    public void The_autopilot_hands_the_dance_gate_the_guards_race_and_none_before_the_guard_or_outside_travel()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var context = w.Context();
        Assert.Null(context.Facts.Race); // a plain journey keeps it off
        var autopilot = new AutopilotScenario(new AutopilotOptions(), w.Factory, context);
        var view = new LogisticsStationTests.TripView(w.Trip.Vendor.R);
        Assert.NotNull(context.Facts.Race);
        Assert.Null(context.Facts.Race!(view, w.Now)); // no guard yet, and not travelling: not read
        Assert.NotEqual(AutopilotState.Travel, autopilot.State);
    }
}

using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 14 T (the sprint of 2026-11-12: fights of 19 s and 14 s, each ended by a trip of over eight minutes): the minimum fight stint (<see cref="TripStint"/>). For its seconds after the run began or a trip ended, the counters' trip
/// waits, named once; a stop line is never held.
/// </summary>
public sealed class AutopilotTripStintTests(ITestOutputHelper output)
{
    private const string HeldLine = "autopilot fight: trip held back by the minimum stint: ";

    [Fact]
    public void The_stint_counts_from_the_runs_start_then_from_each_trips_end_and_names_a_trigger_once_a_stint()
    {
        var stint = new TripStint();
        Assert.Equal(900, TripStint.DefaultSeconds);
        Assert.Equal(TripStint.DefaultSeconds, stint.Seconds);

        Assert.True(stint.Holds(19)); // the sprint's first fight: 19 s
        Assert.False(stint.AfterTrip);
        Assert.Equal(881, stint.Left(19));
        Assert.True(stint.SayOnce("sale"));
        Assert.False(stint.SayOnce("sale")); // once a stint
        Assert.True(stint.SayOnce("learn:Flame Bolt III")); // another trigger is another line
        Assert.Equal(2, stint.Held);
        Assert.True(stint.Holds(899.9));
        Assert.False(stint.Holds(900));

        stint.TripEnded(1_000);
        Assert.True(stint.AfterTrip);
        Assert.True(stint.Holds(1_013)); // the second fight: 13 s after the trip
        Assert.True(stint.SayOnce("sale")); // a new stint names it again
        Assert.False(stint.Holds(1_900));
        Assert.Equal(0, stint.Left(2_000));

        var off = new TripStint(0);
        Assert.False(off.Holds(0));
        Assert.Equal(0, new TripStint(-5).Seconds);
    }

    [Fact]
    public void A_counters_trip_inside_the_stint_waits_named_once_and_is_made_when_the_stint_is_over()
    {
        var ground = GroundCatalog.Get(Den);
        var low = ground.Supplies.Stock.First(l => !ground.IsCarried(l.Wcid));
        var lowered = false;
        var w = new ApWorld(Den, output, atHome: false, onKill: (world, _) => // the first kill uses a line up to one under its trip line, over its stop line
        {
            if (!lowered)
            {
                lowered = true;
                world.SetHeld(low.Wcid, Math.Max(low.StopUnder, low.TripUnder - 1));
            }
        })
        { MinStintSeconds = 300 };
        var autopilot = w.Autopilot(new AutopilotOptions());
        Assert.Equal(300, autopilot.MinStintSeconds);
        w.Run(autopilot, a => lowered);
        var until = w.Now + 60;
        w.Run(autopilot, a => w.Now >= until);

        Assert.Equal(AutopilotState.Fight, autopilot.State); // no trip a minute after the line ran low
        Assert.DoesNotContain(w.Factory.Made, s => s.Kind == StationKinds.Logistics);
        var said = Assert.Single(w.Lines, l => l.StartsWith(HeldLine, StringComparison.Ordinal));
        Assert.Contains($"{low.Name}: {Math.Max(low.StopUnder, low.TripUnder - 1)} held, under its trip line {low.TripUnder} (trigger=stock:{low.Name})", said, StringComparison.Ordinal);
        Assert.Contains("s of the 300 s since the run began are left; no stop line is under, so the fight goes on and the trip is asked then", said, StringComparison.Ordinal);
        Assert.Equal(1, autopilot.StintHeld);

        w.Run(autopilot, a => a.Cycles == 1 && a.State == AutopilotState.Fight); // the stint over: the trip, and back in the ground
        Assert.Contains(w.Factory.Made, s => s.Kind == StationKinds.Logistics);
        Assert.Contains(w.Lines, l => l.Contains("the counters say a trip is due: " + low.Name, StringComparison.Ordinal));
        Assert.Single(w.Lines, l => l.StartsWith(HeldLine, StringComparison.Ordinal)); // said once
        autopilot.RequestStop("test");
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot));
    }

    [Fact]
    public void A_stop_line_is_never_held_by_the_stint()
    {
        var ground = GroundCatalog.Get(Den);
        var stop = ground.Supplies.Stock.First(l => l.StopUnder > 0);
        var lowered = false;
        var w = new ApWorld(Den, output, atHome: false, onKill: (world, _) =>
        {
            if (!lowered)
            {
                lowered = true;
                world.SetHeld(stop.Wcid, stop.StopUnder - 1);
            }
        })
        { MinStintSeconds = TripStint.DefaultSeconds };
        var autopilot = w.Autopilot(new AutopilotOptions());

        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Failed, status); // the den mage's stop line ends the run by name at once, stint or no stint (the ladder's run makes its restocking trip the same way: the stop branch reads no stint)
        Assert.StartsWith("logistics-stop:", autopilot.StopName, StringComparison.Ordinal);
        Assert.DoesNotContain(w.Lines, l => l.StartsWith(HeldLine, StringComparison.Ordinal));
        Assert.Equal(0, autopilot.StintHeld);
    }
}

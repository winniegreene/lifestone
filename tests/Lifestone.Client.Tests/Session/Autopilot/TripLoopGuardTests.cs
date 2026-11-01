using Lifestone.Client.Session.Autopilot;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>M5 7 G1 (edge audit 12a): the guard against a trip that loops.</summary>
public sealed class TripLoopGuardTests
{
    [Fact]
    public void Two_idle_trips_in_a_row_hold_the_trips_back_for_a_stretch_of_kills_then_the_asking_resumes()
    {
        var guard = new TripLoopGuard();

        Assert.False(guard.TripEnded(0, 0, 10)); // the first idle trip: no hold yet
        Assert.False(guard.Holding(10));
        Assert.True(guard.TripEnded(0, 0, 10)); // the second: the named hold
        Assert.Equal(1, guard.Holds);
        Assert.True(guard.Holding(10));
        Assert.True(guard.Holding(10 + TripLoopGuard.HoldKills - 1));
        Assert.False(guard.Holding(10 + TripLoopGuard.HoldKills)); // the run asks again after the kills
    }

    [Fact]
    public void A_trip_that_sold_or_bought_anything_resets_the_count()
    {
        var guard = new TripLoopGuard();

        Assert.False(guard.TripEnded(0, 0, 0));
        Assert.False(guard.TripEnded(3, 0, 5)); // sold: the loop is not one
        Assert.Equal(0, guard.Idle);
        Assert.False(guard.TripEnded(0, 0, 5));
        Assert.False(guard.TripEnded(0, 7, 6)); // bought
        Assert.False(guard.TripEnded(0, 0, 6));
        Assert.Equal(0, guard.Holds);
        Assert.False(guard.Holding(6));
    }

    [Fact]
    public void The_scenario_wires_the_guard_to_the_counters_trip_and_the_trip_end_with_a_named_log_line()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lifestone.Client", "Session", "Autopilot", "AutopilotScenario.cs"));

        Assert.Contains("verdict.IsTrip && !_tripLoop.Holding(", src, StringComparison.Ordinal);
        Assert.Contains("_tripLoop.TripEnded(", src, StringComparison.Ordinal);
        Assert.Contains("autopilot trip-loop:", src, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lifestone.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("no repository root");
    }
}

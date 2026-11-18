using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 16 E5d: the outer loop on the session's trip desk (<see cref="TripDesk"/>) over the scripted ACE of <see cref="AutopilotRig"/>, with the real logistics station and town trip. What these prove: the counters the
/// outer loop checks are the session's one set from its first step, a scenario rebuilt on the same session takes the same set and counts the schedule from its own start, and the audit's and the panel's reads go
/// through the reading twin.
/// </summary>
public sealed class AutopilotTripDeskTests(ITestOutputHelper output)
{
    private const string Den = "black-spawn-den-aluvian";
    private const string Temple = "tusker-temple-fort-tethana";

    /// <summary>A world whose first kill uses <paramref name="low"/> up to one under its trip line: the trigger calls the counters' trip.</summary>
    private ApWorld LowAfterAKill(string key, StockLine low)
    {
        var lowered = false;
        return new ApWorld(key, output, atHome: false, onKill: (world, _) =>
        {
            if (!lowered)
            {
                lowered = true;
                world.SetHeld(low.Wcid, Math.Max(low.StopUnder, low.TripUnder - 1));
            }
        });
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void The_trip_the_counters_call_carries_the_triggers_verdict_through_the_station_to_the_town_trip_which_counts_again_at_home(string key)
    {
        var ground = GroundCatalog.Get(key);
        var low = ground.Supplies.Stock.First(l => !ground.IsCarried(l.Wcid));
        var w = LowAfterAKill(key, low);
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.Cycles == 1 && a.State == AutopilotState.Fight);

        // made once, by the trigger; taken by the logistics station; counted again at home by the town trip; and the visit judged against it
        var said = $"stock:{low.Name} ({low.Name}: {Math.Max(low.StopUnder, low.TripUnder - 1)} held, under its trip line {low.TripUnder}, wanted {low.Keep}, ";
        Assert.Single(w.Lines, l => l.StartsWith("autopilot fight: trip: the verdict the trip carries: " + said, StringComparison.Ordinal));
        Assert.Single(w.Lines, l => l.Contains("logistics: the trip was called by the trigger ", StringComparison.Ordinal));
        Assert.Single(w.Lines, l => l.Contains("trip: the verdict stands at the fight's end: " + said, StringComparison.Ordinal));
        Assert.Single(w.Lines, l => l.Contains("trip: the verdict stands at home: " + said, StringComparison.Ordinal)); // the town trip's own count, compared, never a silent second verdict
        Assert.Single(w.Lines, l => l.Contains($"trip: check after: the verdict's reason is served: stock:{low.Name} ({low.Name} {Math.Max(low.StopUnder, low.TripUnder - 1)} held when called, {low.Keep} now, its trip line {low.TripUnder})", StringComparison.Ordinal));

        var desk = TripDesk.For(w.Trip.Vendor.State);
        Assert.Null(desk.Called); // taken
        Assert.Null(desk.InHand); // and closed with the station
    }

    [Fact]
    public void What_changed_on_the_way_home_is_named_against_the_verdict_the_trip_carries()
    {
        var ground = GroundCatalog.Get(Den);
        var stock = ground.Supplies.Stock.Where(l => !ground.IsCarried(l.Wcid) && l.TripUnder > 1).ToList();
        var (low, other) = (stock[0], stock[1]);
        var w = LowAfterAKill(Den, low);
        var changed = false;
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.Cycles == 1 && a.State == AutopilotState.Fight, perFrame: _ =>
        {
            // on the way home (the station has begun its recall) another line falls under its trip line: the trigger never read it
            if (!changed && TripDesk.For(w.Trip.Vendor.State).InHand is not null && w.Lines.Any(l => l.Contains("walking: purpose=home", StringComparison.Ordinal)))
            {
                changed = true;
                w.SetHeld(other.Wcid, other.TripUnder - 1);
            }
        });

        Assert.True(changed);
        Assert.Single(w.Lines, l => l.Contains("trip: the verdict stands at the fight's end: stock:" + low.Name, StringComparison.Ordinal));
        var home = Assert.Single(w.Lines, l => l.Contains("trip: the verdict said stock:" + low.Name + " (", StringComparison.Ordinal));
        Assert.Contains(", at home stock:", home, StringComparison.Ordinal);
        Assert.Contains($"{other.Name} {other.Keep} held is now {other.TripUnder - 1}", home, StringComparison.Ordinal);
        Assert.DoesNotContain(w.Lines, l => l.Contains("trip: the verdict stands at home", StringComparison.Ordinal));
        Assert.Single(w.Lines, l => l.Contains("trip: check after: the verdict's reason is served: stock:" + low.Name, StringComparison.Ordinal)); // the visit bought both: the buy list is the count at home's
    }

    [Fact]
    public void The_outer_loops_counters_are_the_sessions_one_set_from_its_first_step_and_a_rebuilt_scenario_takes_the_same_set()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var first = w.Autopilot(new AutopilotOptions());
        var stated = first.Counters; // what the job states before the session's read model is known
        w.Run(first, a => a.State == AutopilotState.Fight);

        var desk = TripDesk.For(w.Trip.Vendor.State);
        Assert.Same(stated, first.Counters); // the session had counted nothing on this ground: the job's counters are the session's
        Assert.Same(first.Counters, desk.On(w.Ground));
        Assert.Same(desk.Reading(w.Ground), first.TripReader);
        Assert.NotSame(first.Counters, first.TripReader); // the audit's and the panel's read: a twin, never the set the trigger checks

        first.Counters.MarkTrip(5);
        var second = w.Autopilot(new AutopilotOptions()); // a hand-back: a new scenario on the same session and ground
        Assert.NotSame(first.Counters, second.Counters); // its own statement, before its first step
        w.Run(second, frames: 1);
        Assert.Same(first.Counters, second.Counters); // one set a ground a session
        Assert.Equal(0, second.Counters.CyclesAtTrip); // the schedule counts the scenario's own buff cycles, from its own start, as its own counters did
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_trip_the_trigger_called_is_made_on_its_reason_though_the_count_at_the_fights_end_finds_nothing_due(string key)
    {
        // The named change. The trigger calls the trip for a line one under its trip line; before the fight ends the line is back at its keep (she looted a stack), so the station's own count finds nothing due.
        // Before: the station skipped (skipped=no-trip-due), she went back to the fight and the trigger read the pack again. Now the trip is the trigger's: made on the reason it was called for, the difference named.
        var ground = GroundCatalog.Get(key);
        var low = ground.Supplies.Stock.First(l => !ground.IsCarried(l.Wcid));
        var held = Math.Max(low.StopUnder, low.TripUnder - 1);
        var w = LowAfterAKill(key, low);
        var restored = false;
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.Cycles == 1 && a.State == AutopilotState.Fight, perFrame: _ =>
        {
            if (!restored && TripDesk.For(w.Trip.Vendor.State).Called is not null)
            {
                restored = true;
                w.SetHeld(low.Wcid, low.Keep);
            }
        });

        Assert.True(restored);
        var said = $"stock:{low.Name} ({low.Name}: {held} held, under its trip line {low.TripUnder}, ";
        var named = Assert.Single(w.Lines, l => l.Contains("trip: the verdict said " + said, StringComparison.Ordinal) && l.Contains(", at the fight's end none (all well; ", StringComparison.Ordinal));
        Assert.EndsWith($": the reason stock:{low.Name} is now none; {low.Name} {held} held is now {low.Keep}", named, StringComparison.Ordinal);
        Assert.Single(w.Lines, l => l.Contains($"logistics: the trip is made on the verdict it was called on (stock:{low.Name}): the count at the fight's end finds nothing due, and the trigger's reason is the trip's", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("no-trip-due", StringComparison.Ordinal));
        var ended = Assert.Single(w.Lines, l => l.StartsWith("autopilot trip: station logistics ended Succeeded", StringComparison.Ordinal));
        Assert.Equal("stock:" + low.Name.Replace(' ', '_'), Field(ended, "trigger")); // the trigger's reason is the trip's
        Assert.Single(w.Lines, l => l.Contains("walking: purpose=home", StringComparison.Ordinal)); // one trip, made
        Assert.Single(w.Lines, l => l.Contains($"trip: check after: the verdict's reason is served: stock:{low.Name} ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_station_no_trigger_called_still_goes_by_its_own_count_and_a_pack_it_cannot_count_is_still_no_trip()
    {
        // no order: the plan's station with nothing due skips by name, as before (LogisticsStationTests pins the same on its own rig)
        var w = new ApWorld(Den, output, atHome: false);
        var run = w.Trip.Start(forced: false);
        Assert.Equal(ScenarioStatus.Succeeded, w.Trip.Run(run));
        Assert.Contains(" skipped=no-trip-due", run.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(w.Lines, l => l.Contains("trip: the verdict", StringComparison.Ordinal));
    }
}

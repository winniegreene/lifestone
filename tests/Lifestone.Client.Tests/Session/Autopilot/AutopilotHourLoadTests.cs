using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 14 hour load (the owner, 2026-11-16: runs of about an hour, the pack at 150% when the trip leaves) over the scripted ACE of <see cref="AutopilotTests"/>: on a comfortable ground with no loot line of its own (the Tusker Temple 20-40,
/// burden trip line 150%) the hour's trip ends the cycle at the settings' minutes, forced (the station's counters know nothing of the hour), with the comfort read and the trigger logged and the cycle line marked; on a ground whose burden
/// trip line is under the base line (the Tusker Temple of Fort Tethana, 90%) nothing rises and no hour's trip is called; with the option off nothing is read.
/// </summary>
public sealed class AutopilotHourLoadTests(ITestOutputHelper output)
{
    private const string Temple2040 = "tusker-temple-20-40";

    [Fact]
    public void On_a_comfortable_ground_the_hours_trip_ends_the_cycle_at_its_minutes_as_a_forced_trip_and_the_cycle_line_says_so()
    {
        // the scripted fight has no vitals policy: each kill notes the floor the real fight's policy would work out (ladder40 18:15:00 UTC, the Quarters: "floors mana 105 health 33"); the first comfort read, at the first fight frame,
        // is unread (no floor yet), the second, 60 s in, comfortable; the hour is 90 s
        var w = new ApWorld(Temple2040, output, sidePacks: 3, onKill: (world, _) => CycleLoad.For(world.Trip.Vendor.H.State).NoteHealthFloor(33));
        var autopilot = w.Autopilot(new AutopilotOptions { HourLoad = new CycleLoadSettings(true, 1.5, 150) });
        Assert.Equal(ScenarioStatus.Running, w.Run(autopilot, a => a.Cycles == 1));
        autopilot.RequestStop("test");
        w.Run(autopilot);

        var cycle = autopilot.Lines[0];
        Assert.StartsWith("autopilot cycle n=1 state=trip why=logistics-trip:1 ", cycle, StringComparison.Ordinal);
        Assert.Equal("hour:0", Field(cycle, "hour_load")); // the hour's trip ended it; the scripted fight loots nothing, so the loot was never given a line
        Assert.Contains(w.Lines, l => l.StartsWith("hour load: cycle 1 begins on tusker-temple-20-40: on: 1.5 min to 150%;", StringComparison.Ordinal) && l.Contains("top 150%", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("hour load: comfort read: ground=tusker-temple-20-40 verdict=unread in_force=unread ", StringComparison.Ordinal) && l.Contains("health_floor=unread", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("hour load: comfort read: ground=tusker-temple-20-40 verdict=comfortable in_force=comfortable jumps=0 way_home=recall-home ", StringComparison.Ordinal) && l.Contains("health_floor=33", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot fight: the counters say a trip is due: the hour: ", StringComparison.Ordinal) && l.Contains("(trigger=hour;", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("autopilot state fight -> trip (cycle 1): the hour's trip: the cycle's minutes are fought on a comfortable ground (m6-hour-load)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("trigger=forced", StringComparison.Ordinal)); // the station runs the trip: its own counters know nothing of the hour
        var fought = Int(cycle, "kills") * KillSeconds;
        Assert.InRange(fought, 90 - (2 * KillSeconds), 90 + (2 * KillSeconds)); // the scripted fight kills every 2 s from its first frame: the trip at the minute and a half
    }

    [Fact]
    public void Where_the_burden_trip_line_is_under_the_base_line_nothing_rises_and_with_the_option_off_nothing_is_read()
    {
        var held = new ApWorld(Temple, output, sidePacks: 3);
        var heldPilot = held.Autopilot(new AutopilotOptions { HourLoad = new CycleLoadSettings(true, 0.5, 150), TripAfterKills = 30 });
        held.Run(heldPilot, a => a.Cycles == 1);
        heldPilot.RequestStop("test");
        held.Run(heldPilot);
        Assert.Equal("held", Field(heldPilot.Lines[0], "hour_load"));
        Assert.Contains(held.Lines, l => l.Contains("verdict=uncomfortable", StringComparison.Ordinal) && l.Contains("the top 90% is at or under the base line 100%", StringComparison.Ordinal));
        Assert.DoesNotContain(held.Lines, l => l.Contains("trigger=hour", StringComparison.Ordinal));
        Assert.Single(held.Lines, l => l.StartsWith("hour load: comfort read: ", StringComparison.Ordinal)); // latched: read once a cycle

        var off = new ApWorld(Temple2040, output, sidePacks: 3);
        var offPilot = off.Autopilot(new AutopilotOptions { HourLoad = new CycleLoadSettings(false, 0.5, 150), TripAfterKills = 30 });
        off.Run(offPilot, a => a.Cycles == 1);
        offPilot.RequestStop("test");
        off.Run(offPilot);
        Assert.Equal("off", Field(offPilot.Lines[0], "hour_load"));
        Assert.DoesNotContain(off.Lines, l => l.StartsWith("hour load: comfort read: ", StringComparison.Ordinal) || l.Contains("trigger=hour", StringComparison.Ordinal));
        Assert.True(Int(offPilot.Lines[0], "kills") >= 30); // the forced trip after 30 kills, not the half minute's hour
    }
}

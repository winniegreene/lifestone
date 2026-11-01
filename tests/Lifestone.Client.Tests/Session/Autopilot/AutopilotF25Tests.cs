using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M5 7 F25 (D93, the owner: keep the stance). Soak round 3 changed the combat mode 75 times in 71 minutes: each station's end set the peace and the next set the magic mode at once. On the ground the autopilot's stations hold the magic
/// mode between each other; it leaves it only for a vendor (a station with a Peace need: the trip's shopping), the stop, a log off or a person's hand-over. The changes are counted (<see cref="AutopilotScenario.StanceChanges"/>, the soak
/// line's <c>stance_changes=</c>).
/// </summary>
public sealed class AutopilotF25Tests(ITestOutputHelper output)
{
    private static int Mode(ApWorld w) => w.Trip.View.State.Self.Qualities.CombatMode;

    [Fact]
    public void No_peace_between_two_autopilot_stations_on_the_ground_across_a_fights_end_and_a_buff_refresh()
    {
        var w = new ApWorld(Den, output, atHome: false);
        ArmourBuffLedger.For(w.Trip.View.State).MarkSeeded(); // every wanted line is under its margin in the scripted world: the upkeep comes at the first check
        w.Trip.Vendor.R.CombatModeIs(CombatModes.Magic);
        var autopilot = w.Autopilot(new AutopilotOptions());
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        var sentBefore = w.Trip.Vendor.Sent(FakeSendKind.ChangeCombatMode);
        w.Run(autopilot, a => arrows.Count(x => x.To == AutopilotState.Fight) >= 2); // fight, its end for the buff margin, the upkeep's buff station, the fight again

        Assert.Contains(arrows, a => a.To == AutopilotState.Upkeep);
        Assert.Equal(AutopilotState.Fight, autopilot.State);
        Assert.Equal(0, autopilot.StanceChanges);
        Assert.Equal(sentBefore, w.Trip.Vendor.Sent(FakeSendKind.ChangeCombatMode)); // no mode change was sent between the stations
        Assert.Equal(CombatModes.Magic, Mode(w));
        Assert.All(w.Factory.Made.Where(s => s.Kind is StationKinds.DenFight or StationKinds.Death), s => Assert.True(s.Arguments.ContainsKey(JourneyStations.UnattendedArgument), s.Name)); // its fights hold the magic mode at their end
        Assert.True(AutopilotScenario.BuffSpec.Arguments.ContainsKey(JourneyStations.UnattendedArgument)); // its buff station too
    }

    [Fact]
    public void The_vendor_stations_are_entered_in_peace_and_each_mode_change_is_counted()
    {
        // the vendor: the trip's stations declare a Peace need, which the autopilot's peace phase meets before they start (the scripted rig relaxes the needs, so the declaration is what is pinned here)
        Assert.True(new LogisticsStation().Needs.Peace);
        Assert.True(new ResupplyStation().Needs.Peace);

        var w = new ApWorld(Den, output, atHome: false);
        w.Trip.Vendor.R.CombatModeIs(CombatModes.Magic);
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        Assert.Equal(0, autopilot.StanceChanges);
        w.Trip.Vendor.R.CombatModeIs(CombatModes.NonCombat); // a change the stations did not hold off (as the vendor's peace)
        var until = w.Now + 1;
        w.Run(autopilot, a => w.Now >= until);
        Assert.Equal(1, autopilot.StanceChanges);
        w.Trip.Vendor.R.CombatModeIs(CombatModes.Magic);
        until = w.Now + 1;
        w.Run(autopilot, a => w.Now >= until);
        Assert.Equal(2, autopilot.StanceChanges);
    }

    [Fact]
    public void The_stop_returns_the_stance_to_peace()
    {
        var w = new ApWorld(Den, output, atHome: false);
        w.Trip.Vendor.R.CombatModeIs(CombatModes.Magic);
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        var sentBefore = w.Trip.Vendor.Sent(FakeSendKind.ChangeCombatMode);
        autopilot.RequestStop("test");
        var status = w.Run(autopilot);
        for (var i = 0; i < 20; i++)
        {
            w.Trip.Vendor.R.Tick(0.25); // the peace was asked at the stop: the Action API's lane sends it
        }

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains(w.Lines, l => l.Contains("autopilot stop: the stance returns to peace", StringComparison.Ordinal));
        Assert.Equal(sentBefore + 1, w.Trip.Vendor.Sent(FakeSendKind.ChangeCombatMode));
    }
}

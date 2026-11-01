using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Mage;
using Xunit;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M5 7 P7s (the owner's cheat-free default, D148; <c>m5-8-unsold-component-floor</c>): the long run of 2026-10-30 14:13 UTC stopped after ten minutes by design (<c>logistics-stop:stop:Diamond_Scarab</c>: the ring burned one, leaving 1
/// under its stop line of 2, and no shop sells it). Now the run goes on: one chat line says the ring is put away and the mage fights on with bolts, and a second says it is back once more scarabs turn up. A line a vendor sells keeps
/// today's stop (<c>AutopilotTests.The_counters_stop_ends_the_autopilot_by_name_and_no_trip_is_made</c>). Over the scripted ACE of <see cref="AutopilotRig"/>, with a stand-in spell table (<see cref="LogisticsCountersTests.FloorTable"/>).
/// </summary>
public sealed class AutopilotFloorTests(ITestOutputHelper output)
{
    private const uint DiamondScarab = 7299;

    [Fact]
    public void At_the_diamond_scarabs_floor_the_run_goes_on_with_one_chat_line_and_more_scarabs_bring_the_ring_back_with_another()
    {
        var lowered = false;
        var kills = 0;
        var w = new ApWorld(Den, output, atHome: false, onKill: (world, _) =>
        {
            kills++;
            if (!lowered)
            {
                lowered = true;
                world.SetHeld(DiamondScarab, 1); // the ring burned one: 1 held, under its stop line of 2
            }
        });
        var made = w.Trip.Context();
        var context = new JourneyContext { Character = made.Character, Account = made.Account, Log = made.Log, Travel = made.Travel, Spells = LogisticsCountersTests.FloorTable(), Dungeon = made.Dungeon };
        var autopilot = new AutopilotScenario(new AutopilotOptions(), w.Factory, context);
        var chat = new List<string>();
        autopilot.ChatNotice += chat.Add;

        w.Run(autopilot, a => w.Lines.Any(l => l.Contains("components: Diamond Scarab (wcid 7299) is at its floor", StringComparison.Ordinal)));
        var atFloor = kills;
        w.Run(autopilot, _ => kills >= atFloor + 3); // the fight goes on past the floor
        Assert.Equal(AutopilotState.Fight, autopilot.State);
        Assert.Null(autopilot.StopName);

        w.SetHeld(DiamondScarab, 6); // more turn up
        w.Run(autopilot, a => w.Lines.Any(l => l.Contains("components: Diamond Scarab (wcid 7299) is back over its floor", StringComparison.Ordinal)));
        autopilot.RequestStop("test");
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot));

        Assert.DoesNotContain("logistics-stop", autopilot.StopName ?? string.Empty, StringComparison.Ordinal);
        Assert.Single(chat, l => l == "Autopilot: Diamond Scarab is down to 1 and no shop sells it, so Cassius' Ring of Fire is put away; fighting on with bolts.");
        Assert.Single(chat, l => l == "Autopilot: Diamond Scarab is back to 6, so Cassius' Ring of Fire is cast again.");
        Assert.Single(w.Lines, l => l.Contains("components: Diamond Scarab (wcid 7299) is at its floor: 1 held, under its stop line 2; carried, no shop on the ground's trips sells it: Cassius' Ring of Fire is not cast until more turn up", StringComparison.Ordinal));
        Assert.Single(w.Lines, l => l.Contains("autopilot chat: Autopilot: Diamond Scarab is down to 1", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("the counters say stop", StringComparison.Ordinal));
    }
}

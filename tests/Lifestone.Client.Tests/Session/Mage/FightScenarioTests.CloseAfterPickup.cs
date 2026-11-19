using Lifestone.Client.Session.Scenarios;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 sprint (m6-loot-close-after-pickup; run 4, 2026-11-19 04:02:21-22 UTC): a trip's stop stood the loot down 1 s into a pick-up and closed the corpse at once; ACE, still running the pick-up's motion, then no longer found the item
/// ("Source item not found!", one of ACE's own messages no retail player saw) and a 4,000-value essence stayed in the corpse. The close now waits for the pick-up in flight to land, as long as the server takes. Over the same scripted
/// ACE as the other loot tests, with the pick-up's motion timed.
/// </summary>
public sealed partial class FightScenarioTests
{
    [Fact]
    public void A_stop_asked_while_a_pick_up_is_in_flight_closes_the_corpse_only_after_it_lands_M6_sprint()
    {
        var r = new Rig(fight: LootRetryFight(kills: 1)) { Corpses = true, PickSeconds = 1.35 };
        r.CorpseItems.Add((0x80004001, 49227, 0x80, 4000, 50, "Frost Skeleton Minion Essence (50)")); // run 4's lost essence (wcid 49227, value 4000, burden 50: its loot line)
        r.Add(SlaveA, TuskerSlave, Den, 30);
        var scenario = r.Scenario(new FakeWalk());
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running && r.Picked.Count == 0; i++)
        {
            status = r.Frame(scenario);
        }

        Assert.True(status == ScenarioStatus.Running && r.Picked.Count == 1, $"status {status}, picked {r.Picked.Count}, opened {r.Opened.Count}: " + string.Join(" | ", r.Log.Lines.TakeLast(25)));
        Assert.Empty(r.Closed);

        scenario.RequestStop(FightStopReason.LogisticsTrip);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario, 60));

        Assert.Empty(r.PicksLost); // the pick-up landed: the corpse was still open
        Assert.Equal([CorpseA], r.Closed);
        Assert.Equal(1, LinesWith(r, "waits for the pick-up in flight to land (m6-loot-close-after-pickup)"));
        Assert.Equal(1, LinesWith(r, "the corpse is closed (m6-loot-close-after-pickup)"));
    }
}

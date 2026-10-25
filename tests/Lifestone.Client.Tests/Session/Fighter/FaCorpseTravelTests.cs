using Lifestone.Client.Session;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// M4 P5b fix FA, P5b-R1: in a journey the hunt travels to a corpse beyond <see cref="HuntScenario.CorpseReachMetres"/> (an object goal through the travel seam, <see cref="TravelPurpose.Corpse"/>) before it opens it.
/// Live journey 3a (the headless archer): the corpses were opened from bow range, ACE's walk-to-use timed out (UseDone 0, no ViewContents), <c>corpses_refused=2 looted=0</c>. Over the default
/// <see cref="PlaceTravel"/> the open is sent from where the player stands, as before. The scripted rig's creature is the tests' own script (D23: nothing is spawned in any world).
/// </summary>
public sealed class FaCorpseTravelTests
{
    private const uint Creature = 0x80000FA1;
    private const string BeltName = "Field Ursuin";

    private static HuntOptions Missile => new() { Kills = 1, Seconds = 300, Sell = false, BuyKits = 0, Weapon = HuntWeapon.Missile };

    private static HuntScriptRig FaRig(HuntOptions options, J2aRecordingTravel travel, double north)
    {
        var rig = HuntScriptRig.Create(log => new HuntScenario(options with { Travel = travel }, log, _ => 40f, endSession: false));
        travel.Rig = rig;
        rig.OnBelt = (r, n) =>
        {
            if (n == 1)
            {
                r.PlaceCreature(Creature, BeltName, north);
            }
        };
        return rig;
    }

    [Fact]
    public void An_archers_kill_at_25_m_in_a_journey_travels_to_the_corpse_within_2_m_before_the_open_FA()
    {
        HuntScriptRig? made = null;
        var opensAtRequest = -1;
        var travel = new J2aRecordingTravel
        {
            OnRequest = r =>
            {
                if (r.Purpose == TravelPurpose.Corpse)
                {
                    opensAtRequest = made!.CorpseOpensSent;
                }
            },
        };
        using var rig = FaRig(Missile, travel, north: 25);
        made = rig;

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, rig.Scenario.Failure + rig.Tail());
        Assert.Equal(1, rig.Scenario.Tally.Kills);
        var corpse = Assert.Single(travel.Requests, r => r.Purpose == TravelPurpose.Corpse);
        Assert.NotEqual(0u, corpse.ObjectGuid); // the corpse itself is the object goal
        Assert.NotEqual(Creature, corpse.ObjectGuid);
        Assert.InRange(corpse.Range, 0.5, 2.0);
        Assert.Equal(HuntScenario.CorpseReachMetres, corpse.Range);
        Assert.Equal(0, opensAtRequest); // no open was sent before the travel
        Assert.True(rig.CorpseOpensSent >= 1, rig.Tail()); // and the open came after it
        Assert.True(rig.Logged("travel to it within 2 m before the open"), rig.Tail());
        Assert.True(rig.Logged("corpse: at 0x"), rig.Tail());
        Assert.Contains("corpses_refused=0", rig.Scenario.DetailFields(), StringComparison.Ordinal);
        Assert.Equal(0, rig.J2aPlaces());
        var order = travel.Requests.Select(r => r.Purpose).ToList();
        Assert.True(order.IndexOf(TravelPurpose.Corpse) < order.IndexOf(TravelPurpose.Home), string.Join(",", order));
    }

    [Fact]
    public void Over_the_default_place_travel_the_archers_corpse_is_opened_from_where_the_player_stands_as_before_FA()
    {
        var travel = new J2aRecordingTravel { Inner = new PlaceTravel() };
        using var rig = FaRig(Missile, travel, north: 25);

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, rig.Scenario.Failure + rig.Tail());
        Assert.Equal(1, rig.Scenario.Tally.Kills);
        Assert.DoesNotContain(travel.Requests, r => r.Purpose == TravelPurpose.Corpse);
        Assert.All(travel.LegMeans, m => Assert.Equal(TravelMeans.Place, m)); // today's places only
        Assert.True(rig.CorpseOpensSent >= 1, rig.Tail());
        Assert.True(rig.Logged("s after the kill; opening it"), rig.Tail());
        Assert.False(rig.Logged("before the open"), rig.Tail());
    }

    [Fact]
    public void A_failed_walk_to_the_corpse_is_counted_refused_and_the_hunt_goes_on_FA()
    {
        var travel = new J2aRecordingTravel { FailWhen = r => r.Purpose == TravelPurpose.Corpse };
        using var rig = FaRig(Missile, travel, north: 25);

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, rig.Scenario.Failure + rig.Tail());
        Assert.Equal(0, rig.CorpseOpensSent); // never opened from range
        Assert.Contains("corpses_refused=1", rig.Scenario.DetailFields(), StringComparison.Ordinal);
        Assert.True(rig.Logged("corpse: the travel to 0x"), rig.Tail());
    }
}

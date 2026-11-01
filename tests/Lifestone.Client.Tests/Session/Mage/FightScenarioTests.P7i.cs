using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 P7i (D139 g, the rehearsal of 2026-10-30): legs begun from about the same spot and given up twice in a row send the mage out of the spot first: the floor is patched for the planner, and the mage walks back along its way in
/// (a breadcrumb) or, with none, the fight fails named so the autopilot's recovery recalls.
/// </summary>
public sealed partial class FightScenarioTests
{
    private ushort _playerStamp = 2;

    /// <summary>The server moves the player to <paramref name="x"/> in the den's cell (a new position stamp each time).</summary>
    private void MovePlayer(Rig r, float x) =>
        r.R.H.R.Reducer.OnUpdatePosition(r.R.H.R.Context, ReducerHarness.Move(ActionHarness.Amy, 1, ++_playerStamp, 2, 1, cell: Den, x: x));

    [Fact]
    public void Two_legs_from_the_same_spot_given_up_in_a_row_send_the_mage_back_along_its_way_in_and_patch_the_spot_M5_7_P7i()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 80); // only a walk reaches it
        var tour = new RecordingTour { Purpose = TourPurpose.Seen, PurposeGuid = TuskerGuid };
        var walk = new FakeWalk { Tour = tour, TicksPerLeg = 400, StuckLegs = 3, StuckAfterTicks = 14 };
        var scenario = ToTheFight(r, r.Scenario(walk));

        // Leg 1 walks from x=100 to x=80 (a breadcrumb every 2 m), then sticks there; legs 2 and 3 begin at x=80 and stick too.
        for (var i = 0; i < 200 && walk.Spots.Count == 0; i++)
        {
            if (walk.Legs == 1 && scenario.StuckSpotEscapes == 0)
            {
                var x = 100f - (2f * Math.Min(10, i));
                MovePlayer(r, x);
            }

            r.Frame(scenario);
        }

        Assert.Equal(1, LinesWith(r, "walk: stuck spot: give-up 1 in a row of legs begun near 0x01050375 [100"));
        Assert.Equal(1, LinesWith(r, "walk: stuck spot: give-up 1 in a row of legs begun near 0x01050375 [80"));
        Assert.Equal(1, LinesWith(r, "walk: stuck spot: give-up 2 in a row of legs begun near 0x01050375 [80"));
        Assert.Equal(1, scenario.StuckSpotEscapes);
        Assert.Single(walk.Patches);
        Assert.Equal(80f, walk.Patches[0].At.Origin.X, 1);
        Assert.Equal(FightScenario.StuckSpotAvoidSeconds, walk.Patches[0].Until - r.View.Clock.Now, 1);
        Assert.Single(walk.Spots);
        Assert.Equal(86f, walk.Spots[0].Origin.X, 1); // the newest breadcrumb 6 m or more from the spot
        Assert.Equal(1, LinesWith(r, "walking: purpose=escape (2 legs begun near 0x01050375 [80"));

        walk.TicksPerLeg = 4;
        MovePlayer(r, 86f);
        for (var i = 0; i < 20 && LinesWith(r, "walk: out of the stuck spot") == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(1, LinesWith(r, "walk: out of the stuck spot: 6 m from it at 0x01050375 [86"));
        Assert.Null(scenario.Failure);
    }

    [Fact]
    public void Two_legs_from_the_same_spot_given_up_with_no_way_back_fail_the_fight_named_for_the_autopilots_recall_M5_7_P7i()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 80);
        var tour = new RecordingTour { Purpose = TourPurpose.Seen, PurposeGuid = TuskerGuid };
        var walk = new FakeWalk { Tour = tour, TicksPerLeg = 400, StuckLegs = 2 }; // the mage never moves: no breadcrumb lies away from the spot
        var scenario = ToTheFight(r, r.Scenario(walk));

        for (var i = 0; i < 200 && scenario.Failure is null; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(1, LinesWith(r, "walk: stuck spot: give-up 2 in a row of legs begun near 0x01050375"));
        Assert.Single(walk.Patches);
        Assert.Empty(walk.Spots);
        Assert.StartsWith($"{FightScenario.StuckSpotFailure}: 2 legs begun near 0x01050375", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("no breadcrumb lies 6 m or more from", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("the autopilot's recall takes the mage out (P7i)", scenario.Failure, StringComparison.Ordinal);
    }
}

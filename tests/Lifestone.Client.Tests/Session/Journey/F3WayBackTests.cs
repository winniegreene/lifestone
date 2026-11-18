using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 0 fix slice F3 (closes <c>m6-trip-way-back</c>): the logistics trip's way back to an outdoor ground is a walk to the ground's first cell (a point goal through the travel seam, <see cref="TravelPurpose.Cell"/>), check, act, check; never a
/// place, and an outdoor ground that cannot be walked to is a loud stop by name. The dungeon's way back (Primary Portal Recall) is pinned by <see cref="LogisticsStationTests"/> and unchanged.
/// </summary>
public sealed class F3WayBackTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const string Outskirts = "holtburg-outskirts";

    private JourneyContext Context(LogisticsStationTests.Trip trip, HuntingGround? ground = null) => new()
    {
        Character = "Winnie",
        Account = "test",
        Log = trip.Vendor.H.Log,
        Travel = trip.Travel,
        Spells = new Dictionary<uint, SpellInfo>(),
        Dungeon = ground ?? trip.Ground,
        Dats = fixture.Dats,
    };

    [RequiresDatsFact]
    public void The_trips_way_back_to_an_outdoor_ground_is_a_walk_to_its_first_cell_with_the_purpose_logged_and_no_place()
    {
        var trip = new LogisticsStationTests.Trip(Outskirts).WithOutput(output);
        var first = trip.Ground.Outdoor!.Cells[0];
        trip.Vendor.StandAt(first.Cell, new System.Numerics.Vector3(first.Spot.X + 40, first.Spot.Y, 0)); // in the ground (its landblock), 40 m from the first cell's point
        var run = new LogisticsStation(forced: true).Start(Context(trip));

        trip.Run(run);

        var purposes = trip.Travel.Requests.Select(r => r.Purpose).ToList();
        Assert.Equal(TravelPurpose.Home, purposes[0]);
        Assert.Equal(TravelPurpose.Cell, purposes[^1]); // the way back: a point goal on foot
        Assert.DoesNotContain(TravelPurpose.Ground, purposes); // never the portal's recall or a place
        Assert.Equal(first.Cell, trip.Travel.Requests[^1].Destination.Location.Position.CellId);
        Assert.Contains(" places=0", run.VerdictFields(), StringComparison.Ordinal);
        Assert.True(trip.Logged("check before: the player stands at") && trip.Logged("purpose=ground (logistics trip: on foot from Holtburg to the first cell of " + Outskirts));
        Assert.True(trip.Logged("logistics: check after: at the first cell's point") || trip.Logged("already stands within"), string.Join('\n', trip.Lines.Where(l => l.Contains("logistics", StringComparison.Ordinal))));
        Assert.Equal(0, trip.ModVerbsSent);
    }

    [RequiresDatsFact]
    public void M6_8_E_a_trip_with_the_minutes_rule_due_does_its_errand_at_home_and_makes_no_way_back()
    {
        var trip = new LogisticsStationTests.Trip(Outskirts).WithOutput(output);
        var first = trip.Ground.Outdoor!.Cells[0];
        trip.Vendor.StandAt(first.Cell, new System.Numerics.Vector3(first.Spot.X + 40, first.Spot.Y, 0));
        var context = Context(trip);
        context.Facts.MinutesStopDue = () => true; // m6-minutes-stop-safe-point: the autopilot's minutes rule is due while the trip is at home
        var run = new LogisticsStation(forced: true).Start(context);

        var status = trip.Run(run);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(run.Ok, run.VerdictFields());
        var purposes = trip.Travel.Requests.Select(r => r.Purpose).ToList();
        Assert.Equal(TravelPurpose.Home, purposes[0]); // the errand's way home and its vendor walks were made
        Assert.DoesNotContain(purposes, p => p is TravelPurpose.Ground or TravelPurpose.Cell); // no walk and no recall back
        Assert.True(trip.Logged("the minutes stop rule is due: the errand is done at home, so there is no way back"));
        Assert.False(trip.Logged("purpose=ground"));
    }

    [RequiresDatsFact]
    public void An_outdoor_ground_that_is_not_hunted_on_foot_is_a_loud_stop_by_name_and_the_trip_makes_no_place()
    {
        var trip = new LogisticsStationTests.Trip(Outskirts).WithOutput(output);
        var broken = trip.Ground with { Outdoor = null }; // kind Outdoor with no cells: the profile gives no walk
        var run = new LogisticsStation(forced: true).Start(Context(trip, broken));

        var status = trip.Run(run);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("logistics:way-back:no-walk", run.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain(trip.Travel.Requests, r => r.Purpose is TravelPurpose.Ground or TravelPurpose.Cell);
        Assert.Equal(0, trip.ModVerbsSent);
    }

    [RequiresDatsFact]
    public void The_fight_and_buff_stations_build_for_an_outdoor_ground_with_no_kit_where_they_threw_ground_plan_gap()
    {
        foreach (var kind in new[] { StationKinds.DenFight, StationKinds.Buff })
        {
            var t = new J3bRig { Dats = fixture.Dats };
            var factory = JourneyStations.Create(JourneyPlans.Mage, [$"--fight-ground={Outskirts}"], fixture.Dats);
            var station = factory.Create(new StationSpec("no-kit", kind, new StationNeeds(StationWhere.Here), 280));

            var run = station.Start(t.Context(dungeon: GroundCatalog.Get(Outskirts)));

            run.Step(t.View, t.Now); // the scenario is made on the first step: its constructor built the kit (M6 0 F3: the bare one), and no ground-plan-gap stops the climb at the fight or the buff
            Assert.Equal(GroundKind.Outdoor, GroundCatalog.Get(Outskirts).Kind);
            Assert.True(GearKit.HasNoKit(GroundCatalog.Get(Outskirts)) && !GearKit.HasNoKit(GroundCatalog.Get(FightOptions.DefaultGround)), kind);
        }
    }
}

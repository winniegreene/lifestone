using System.Numerics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 6 C2: the rung change cut at a travel place (<see cref="RungRouteInput.StopAtPlace"/>, the corpse trip of a death on a leg): <see cref="RungRoute.Decide"/> keeps the legs up to and including the first that arrives in the place and drops
/// the rest; null is today's plan; the place that holds a corpse lost on a leg (<see cref="RungRoute.PlaceHolding"/>); the station's <c>stop_at=</c> argument and its check after. Over the real travel file and the catalog's profiles.
/// </summary>
public sealed class RungRouteStopAtTests
{
    private static readonly TravelRoutes Routes = TravelRoutes.Default;

    private static HuntingGround Burrow => GroundCatalog.Get("tusker-burrow");

    private static HuntingGround Holding => GroundCatalog.Get("tusker-holding");

    private static HuntingGround Temple2040 => GroundCatalog.Get("tusker-temple-20-40");

    private static RungRouteInput Input(HuntingGround g, int level, string? stopAt, RungWhere where = RungWhere.Home, string place = "holtburg", bool recall = false, bool portalRecall = false, bool tie = false, PortalTie tied = PortalTie.Unknown) =>
        new(g, RungDirection.Out, where, place, level, recall, portalRecall, tie, tied, false, null, stopAt);

    private static string[] Shape(RungPlan plan) => [.. plan.Legs.Select(l => l.KindName + ":" + l.Label)];

    [Theory]
    [InlineData("tusker-burrow", 12)]
    [InlineData("tusker-holding", 61)]
    [InlineData("tusker-temple-20-40", 25)]
    public void A_null_stop_place_is_todays_plan(string ground, int level)
    {
        var g = GroundCatalog.Get(ground);
        var today = RungRoute.Decide(new RungRouteInput(g, RungDirection.Out, RungWhere.Home, "holtburg", level, false, false, true, PortalTie.Unknown, false), Routes);
        var nullStop = RungRoute.Decide(Input(g, level, null, tie: true), Routes);

        Assert.Equal(Shape(today), Shape(nullStop));
        Assert.Equal((today.RouteKey, today.Tied, today.Gate, today.Stop, today.Why), (nullStop.RouteKey, nullStop.Tied, nullStop.Gate, nullStop.Stop, nullStop.Why));
        Assert.Contains(nullStop.Legs, l => l.Kind == RungLegKind.Enter); // the whole plan: the entry's Use is kept
    }

    [Theory]
    [InlineData("tusker-burrow", 12, "town-network", "by-refuge@town-network", "hop:holtburg-to-network")]
    [InlineData("tusker-burrow", 12, "island", "by-refuge@island", "hop:holtburg-to-network,hop:network-to-refuge")]
    [InlineData("tusker-burrow", 15, "facility-hub", "by-facility-hub@facility-hub", "hop:holtburg-to-network,hop:network-to-facility-hub")] // M6 7 J: the hub's route from 15
    [InlineData("tusker-burrow", 15, "island", "by-facility-hub@island", "hop:holtburg-to-network,hop:network-to-facility-hub,hop:hub-burrow")]
    [InlineData("tusker-holding", 61, "island", "by-facility-hub-barracks@island", "hop:holtburg-to-network,hop:network-to-facility-hub,hop:hub-barracks")]
    [InlineData("tusker-temple-20-40", 25, "new-colier", "by-new-colier@new-colier", "hop:new-colier")]
    [InlineData("tusker-burrow", 12, "holtburg", "here@holtburg", "")]
    public void The_plan_is_cut_after_the_first_leg_into_the_place_and_the_run_the_gate_the_tie_and_the_entry_are_dropped(string ground, int level, string place, string route, string legs)
    {
        var plan = RungRoute.Decide(Input(GroundCatalog.Get(ground), level, place, tie: true), Routes);

        Assert.Null(plan.Stop);
        Assert.Equal(route, plan.RouteKey);
        Assert.Equal(legs.Length == 0 ? [] : legs.Split(','), Shape(plan));
        Assert.DoesNotContain(plan.Legs, l => l.Kind is RungLegKind.Run or RungLegKind.Gate or RungLegKind.Tie or RungLegKind.Enter);
        Assert.NotEqual("yes", plan.Tied); // a tie leg, when planned, went with the entry
        if (plan.Legs.Count > 0)
        {
            Assert.Contains((ushort)plan.Legs[^1].Portal!.DestLandblock, Routes.PlaceOf(place)!.Landblocks);
            Assert.Contains($"stop_at={place}: the plan stops after leg {plan.Legs.Count} ", plan.Why, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void From_elsewhere_the_way_home_comes_first_and_a_recall_landing_in_the_place_is_a_leg_that_arrives()
    {
        var byPortals = RungRoute.Decide(Input(Burrow, 12, "island", RungWhere.Elsewhere, "eastham"), Routes);
        Assert.Null(byPortals.Stop);
        Assert.Equal(["hop:eastham-to-network", "hop:network-to-holtburg", "hop:holtburg-to-network", "hop:network-to-refuge"], Shape(byPortals));

        var recalled = RungRoute.Decide(Input(Burrow, 16, "holtburg", RungWhere.Elsewhere, "eastham", recall: true), Routes);
        Assert.Null(recalled.Stop);
        Assert.Equal(["lifestone-recall:lifestone-recall"], Shape(recalled));
    }

    [Fact]
    public void A_tied_ground_is_not_recalled_to_when_the_trip_stops_in_a_place_on_its_route()
    {
        var whole = RungRoute.Decide(Input(Temple2040, 25, null, portalRecall: true, tie: true, tied: PortalTie.Tied), Routes);
        Assert.Equal(RungRoute.PortalRecallRoute, whole.RouteKey); // today: the recall lands in the ground

        var cut = RungRoute.Decide(Input(Temple2040, 25, "new-colier", portalRecall: true, tie: true, tied: PortalTie.Tied), Routes);
        Assert.Null(cut.Stop);
        Assert.Equal(["hop:new-colier"], Shape(cut));
        Assert.Equal("known", cut.Tied);
    }

    [Fact]
    public void A_place_no_leg_reaches_an_unknown_place_and_a_gate_refusal_are_named_stops_with_no_legs()
    {
        var hub = RungRoute.Decide(Input(Burrow, 12, "facility-hub", tie: true), Routes); // the Refuge route, a level under the hub's gate 15: it never enters the hub
        Assert.Equal("rung-change:stop-at-off-route:facility-hub", hub.Stop);
        Assert.Empty(hub.Legs);
        Assert.Contains("[hop:holtburg-to-network,hop:network-to-refuge,run:via-1,run:via-2,run:entry-front,gate:entry:tusker-burrow,enter:entry:tusker-burrow]", hub.Why, StringComparison.Ordinal);

        var unknown = RungRoute.Decide(Input(Burrow, 12, "atlantis"), Routes);
        Assert.Equal("rung-change:stop-at-unknown:atlantis", unknown.Stop);
        Assert.Empty(unknown.Legs);

        var refused = RungRoute.Decide(Input(Burrow, 9, "island"), Routes);
        Assert.Equal("gate:network-to-refuge:9<10", refused.Stop);
        Assert.Empty(refused.Legs);
    }

    [Theory]
    [InlineData("tusker-burrow", 12, 0xF6820021u, 100f, 100f, "island", "the travel file's place island holds the corpse's landblock 0xF682")]
    [InlineData("tusker-holding", 61, 0xF380003Fu, 180f, 156f, "island", "leg in island, within the leg's 1507 m")] // M6 4 round 5 (cache/m6/4/live/r5-loopback.log): the level-61 run to the Holding died in 0xF380003F, a landblock no place holds
    [InlineData("tusker-temple-20-40", 25, 0xAAB20012u, 100f, 100f, "holtburg", "start of the walk to new-colier leg in holtburg")]
    [InlineData("tusker-burrow", 12, 0xC0C00001u, 12f, 12f, null, "no on-foot leg of route by-refuge reaches it")]
    public void The_place_holding_a_corpse_lost_on_a_leg_is_the_landblocks_place_else_the_start_of_the_on_foot_leg_it_lies_within(string ground, int level, uint cell, float x, float y, string? place, string why)
    {
        var held = RungRoute.PlaceHolding(Routes, GroundCatalog.Get(ground), level, cell, new Vector3(x, y, 0));

        Assert.Equal(place, held.Key);
        Assert.Contains(why, held.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_station_takes_stop_at_from_its_spec_and_ends_in_the_place_checked_after()
    {
        var station = RungChangeStation.FromArguments(new Dictionary<string, string>(StringComparer.Ordinal) { [RungChangeStation.DirectionArgument] = "out", [RungChangeStation.StopAtArgument] = "island" });
        Assert.Equal((RungDirection.Out, "island"), (station.Direction, station.StopAt));
        Assert.Null(RungChangeStation.FromArguments(new Dictionary<string, string>(StringComparer.Ordinal)).StopAt);

        var t = new RungRig(level: 12);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        var (run, status) = t.RunStation(station, t.Context(Burrow));

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.Equal(0xF682u, t.Landblock);
        Assert.StartsWith("ground=tusker-burrow level=12 route=by-refuge@island hops=2 ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.EndsWith(" ok=yes fail=none direction=out stop_at=island", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(2, t.Lines("rung_change_leg: ").Count);
        Assert.Single(t.Lines("rung_change: stop_at=island: check after: the player stands in island"));
    }
}

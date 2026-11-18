using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 7 H: the plans the four owed loopbacks walk, read over the real travel file and quest files before a live round: what <see cref="QuestRun"/> asks of <see cref="TownNetworkTrip.Choose"/> and <see cref="RungRoute.Decide"/>, at the levels and with the books the
/// cases are built with (<c>TrophyLiveTests</c>, <c>KillTaskLiveTests</c>). No session: a plan that does not exist would otherwise be first read at the round.
/// </summary>
public sealed class ThrowawayPathPlanTests
{
    private static TravelRoutes Routes => TravelRoutes.Default;

    private static HuntingGround Burrow => GroundCatalog.Get("tusker-burrow");

    /// <summary>The quest station's town trip for the go step <paramref name="index"/> of <paramref name="questKey"/> from Holtburg (<c>QuestRun.TownTrip</c>'s candidate and input).</summary>
    private static TownTripChoice Trip(string questKey, int index, int level, bool recall)
    {
        var step = QuestCatalog.Get(questKey).Steps[index];
        var place = step.Place!;
        var town = Routes.PlaceOf(RungRoute.Hex(place.Cell >> 16))?.Name ?? place.Name;
        var candidate = new TownTripCandidate(town, step.Target?.Name ?? place.Name, step.Target?.Wcid ?? 0, new ScrivenerPlace(town, 0, place.Cell, place.Cell, place.Point!.Value));
        return TownNetworkTrip.Choose([candidate], new TownTripInput(Routes.StartPlace, level, recall, HomeIsTravelStart: true), Routes, LeverDoors.Default);
    }

    private static string[] Shape(RungPlan plan) => [.. plan.Legs.Select(l => l.KindName + ":" + l.Label)];

    [Fact]
    public void Below_sixteen_the_trophy_go_hops_from_Holtburg_to_the_Refuge_and_its_way_back_is_by_portals()
    {
        var choice = Trip("tusk-female", 1, level: 15, recall: false);
        var trip = Assert.IsType<TownTripPlan>(choice.Chosen);
        Assert.Equal(["holtburg-to-network", "network-to-refuge"], trip.Hops.Select(h => h.Key));
        Assert.Equal(0xF6820033u, trip.DropCell);
        Assert.False(trip.HomeByRecall);
    }

    [Fact]
    public void With_the_recall_the_trophy_go_hops_to_the_Refuge_and_home_is_the_recall()
    {
        var trip = Assert.IsType<TownTripPlan>(Trip("tusk-female", 1, level: 61, recall: true).Chosen);
        Assert.Equal(["holtburg-to-network", "network-to-refuge"], trip.Hops.Select(h => h.Key));
        Assert.True(trip.HomeByRecall);
    }

    /// <summary>The rung change home plans from "elsewhere" on the island only while no ground of the catalog holds its landblock (a ground would take the way home by that ground's exit).</summary>
    [Theory]
    [InlineData(0xF6820033u)]
    [InlineData(0xF682011Cu)]
    public void No_ground_holds_the_Refuge_or_Brighteyess_room(uint cell) => Assert.DoesNotContain(GroundCatalog.All, g => g.InLandblocks(cell));

    /// <summary>Case (a)'s way home: the turn-in's 100,000 experience lifts the Throwaway past 15 with no Lifestone Recall in the book, so the way home by portals must hold over the levels it can land on, and the band the hub's gate admits.</summary>
    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(26)]
    public void The_way_home_from_the_Refuge_without_the_recall_holds_past_the_turn_ins_level_ups(int level)
    {
        var input = new RungRouteInput(Burrow, RungDirection.Home, RungWhere.Elsewhere, "island", level, false, false, false, PortalTie.Unknown, false);
        var plan = RungRoute.Decide(input, Routes);
        Assert.Null(plan.Stop);
        Assert.Equal("refuge", plan.RouteKey);
        Assert.Equal(["hop:refuge-to-network", "hop:network-to-holtburg"], Shape(plan));
    }

    /// <summary>Case (b)'s run: out by the Facility Hub's portals at 61 (the hub route is first where its gate admits it, D70; the entry has no level gate), and from inside the Burrow with the recall in the book the home plan's first leg is no exit hop, so the go is the rung change home.</summary>
    [Fact]
    public void At_sixty_one_the_Burrow_is_reached_by_the_Facility_Hub_and_left_by_the_recall()
    {
        var run = RungRoute.Decide(new RungRouteInput(Burrow, RungDirection.Out, RungWhere.Home, Routes.StartPlace, 61, true, false, false, PortalTie.Unknown, false), Routes);
        Assert.Null(run.Stop);
        Assert.Equal("by-facility-hub", run.RouteKey);
        Assert.Contains(run.Legs, l => l.Kind == RungLegKind.Run); // the 20.9 m run from the hub's drop

        var home = RungRoute.Decide(new RungRouteInput(Burrow, RungDirection.Home, RungWhere.AtGround, "0x5748", 61, true, false, false, PortalTie.Unknown, false), Routes);
        Assert.Equal(RungRoute.LifestoneRecallRoute, home.RouteKey);
        Assert.False(home.Legs[0].Label.StartsWith("exit:", StringComparison.Ordinal));
    }
}

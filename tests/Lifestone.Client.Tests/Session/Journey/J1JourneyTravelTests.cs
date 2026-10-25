using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// The journey's travel seam (M4 P5b J1, lanes.md contract 1) on <see cref="J1JourneyRig"/>: the budgeted place and its loud refusal, the den by Primary
/// Portal Recall when tied and by the budgeted place when not, home by Lifestone Recall (counted), nothing when already there, the runs through the runner.
/// </summary>
public sealed class J1JourneyTravelTests
{
    private static TravelRequest Ground(StationWhere where) => new(JourneyGrounds.For(where, (_, _, _) => 10f)!, TravelPurpose.Ground);

    private static TravelRequest Home() => new(JourneyGrounds.Holtburg, TravelPurpose.Home);

    [Fact]
    public void The_budget_refuses_the_second_place_loudly_with_a_warn_and_no_error()
    {
        var t = new J1JourneyRig();
        t.Context();
        var first = t.Travel!.GoTo(t.View, Ground(StationWhere.Belt), t.Now);
        Assert.Equal(TravelMeans.Place, first.Means);
        Assert.True(t.Travel.Busy);
        Assert.Equal(TravelStatus.Arrived, t.RunLeg(first)); // Arrived for a place = Confirmed only (the caller checks the arrival)
        Assert.Equal(1, t.Facts.Teleports);

        var second = t.Travel.GoTo(t.View, Ground(StationWhere.Forest), t.Now);
        Assert.Equal(TravelStatus.Failed, second.Step(t.View, t.Now));
        Assert.Equal("travel:place:over-budget", second.Failure);
        Assert.Equal(TravelMeans.Place, second.Means);
        Assert.Equal((1, 1, 1), (t.Facts.Teleports, t.Travel.Places, t.Travel.Refused));
        Assert.Single(t.PlacesTo);
        Assert.Contains(t.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Category == "journey" && e.Message.Contains("REFUSED the place to 'the forest 0x9FB50039'", StringComparison.Ordinal) && e.Message.Contains("travel:place:over-budget", StringComparison.Ordinal));
        Assert.DoesNotContain(t.Log.Entries, e => e.Severity >= LogSeverity.Error);
    }

    [Fact]
    public void The_den_goes_by_Primary_Portal_Recall_when_the_portal_is_tied_and_counts_a_recall_not_a_teleport()
    {
        var t = new J1JourneyRig();
        t.Context();
        t.Facts.PortalTied = true;
        var leg = t.Travel!.GoTo(t.View, Ground(StationWhere.Den), t.Now);
        Assert.Equal(TravelMeans.Recall, leg.Means);
        Assert.Equal(TravelStatus.Arrived, t.RunLeg(leg));
        leg.Dispose();

        Assert.Equal([48u], t.Casts);
        Assert.Equal(0x0105u, t.Landblock);
        Assert.Equal((0, 1, true), (t.Facts.Teleports, t.Facts.Recalls, t.Facts.PortalTied));
        Assert.StartsWith("recalls=1 ", leg.Fields, StringComparison.Ordinal);
        Assert.Empty(t.PlacesTo);
        Assert.False(t.Travel.Busy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public void The_den_goes_by_the_budgeted_place_when_the_portal_is_not_known_tied(bool? tied)
    {
        var t = new J1JourneyRig();
        t.Context();
        t.Facts.PortalTied = tied;
        var leg = t.Travel!.GoTo(t.View, Ground(StationWhere.Den), t.Now);
        Assert.Equal(TravelMeans.Place, leg.Means);
        Assert.Equal(TravelStatus.Arrived, t.RunLeg(leg));
        Assert.Equal(["Black Spawn Den"], t.PlacesTo);
        Assert.Equal((1, 0), (t.Facts.Teleports, t.Facts.Recalls));
        Assert.Empty(t.Casts);
    }

    [Fact]
    public void A_refused_portal_recall_marks_the_portal_untied()
    {
        var t = new J1JourneyRig { PortalTied = false };
        t.Context();
        t.Facts.PortalTied = true; // the journey believed it (a tie lost since)
        var leg = t.Travel!.GoTo(t.View, Ground(StationWhere.Den), t.Now);
        Assert.Equal(TravelStatus.Failed, t.RunLeg(leg));
        Assert.StartsWith("travel:recall:cast: ACE refused: YouMustLinkToPortalToRecall 0x04A3", leg.Failure, StringComparison.Ordinal);
        Assert.Equal((false, 0), (t.Facts.PortalTied, t.Facts.Recalls));
    }

    [Fact]
    public void Home_goes_by_Lifestone_Recall_counts_the_recall_and_learns_the_lifestone_tie()
    {
        var t = new J1JourneyRig(J1JourneyRig.BeltCell);
        t.Context();
        var leg = t.Travel!.GoTo(t.View, Home(), t.Now);
        Assert.Equal(TravelMeans.Recall, leg.Means);
        Assert.Equal(TravelStatus.Arrived, t.RunLeg(leg));

        Assert.Equal([1635u], t.Casts);
        Assert.Equal(0xA9B4u, t.Landblock);
        Assert.Equal((0, 1, true), (t.Facts.Teleports, t.Facts.Recalls, t.Facts.LifestoneTied));
    }

    [Fact]
    public void Home_with_no_lifestone_tie_fails_naming_0x049E_and_marks_the_tie_false()
    {
        var t = new J1JourneyRig(J1JourneyRig.BeltCell) { LifestoneTied = false };
        t.Context();
        var leg = t.Travel!.GoTo(t.View, Home(), t.Now);
        Assert.Equal(TravelStatus.Failed, t.RunLeg(leg));
        Assert.StartsWith("travel:recall:cast: ACE refused: YouMustLinkToLifestoneToRecall 0x049E", leg.Failure, StringComparison.Ordinal);
        Assert.Equal((false, 0, 0), (t.Facts.LifestoneTied, t.Facts.Recalls, t.Facts.Teleports));
    }

    [Fact]
    public void A_character_with_no_recall_fails_travel_recall_no_caster()
    {
        var t = new J1JourneyRig(J1JourneyRig.BeltCell);
        t.Context("Holtburg Hunter");
        var leg = t.Travel!.GoTo(t.View, Home(), t.Now);
        Assert.Equal(TravelStatus.Failed, leg.Step(t.View, t.Now));
        Assert.Equal("travel:recall:no-caster", leg.Failure);
    }

    [Fact]
    public void Nothing_travels_when_the_player_already_stands_at_the_destination()
    {
        var t = new J1JourneyRig();
        t.Context();
        var home = t.Travel!.GoTo(t.View, Home(), t.Now);
        Assert.Equal((TravelMeans.None, TravelStatus.Arrived), (home.Means, home.Step(t.View, t.Now)));

        t.TeleportNow(J1JourneyRig.DenCell);
        var den = t.Travel.GoTo(t.View, Ground(StationWhere.Den), t.Now);
        Assert.Equal((TravelMeans.None, TravelStatus.Arrived), (den.Means, den.Step(t.View, t.Now)));
        Assert.Equal((0, 0), (t.Facts.Teleports, t.Facts.Recalls));
        Assert.Empty(t.Casts);
    }

    [Theory]
    [InlineData(TravelPurpose.Seek)]
    [InlineData(TravelPurpose.Cell)]
    [InlineData(TravelPurpose.Vendor)]
    [InlineData(TravelPurpose.Corpse)]
    [InlineData(TravelPurpose.Spot)]
    [InlineData(TravelPurpose.Bind)]
    public void The_run_purposes_go_through_the_runner_and_sum_the_route_or_fail_with_no_runner(TravelPurpose purpose)
    {
        var t = new J1JourneyRig();
        var runner = new J1Runner();
        t.Context(runners: runner);
        var request = new TravelRequest(JourneyGrounds.Holtburg, purpose, ObjectGuid: 0x50000009, Range: 2);
        var leg = t.Travel!.GoTo(t.View, request, t.Now);
        Assert.Equal(TravelMeans.Run, leg.Means);
        Assert.Equal(TravelStatus.Arrived, t.RunLeg(leg));
        Assert.Same(request, Assert.Single(runner.Requests));
        Assert.Equal((1, 120.5, 30.0), (t.Travel.Runs, t.Travel.RouteMetres, t.Travel.RunSeconds));
        Assert.Equal(0, t.Facts.Teleports);

        var bare = new J1JourneyRig();
        bare.Context();
        var none = bare.Travel!.GoTo(bare.View, request, bare.Now);
        Assert.Equal(TravelStatus.Failed, none.Step(bare.View, bare.Now));
        Assert.Equal("travel:run:no-runner", none.Failure);
    }

    [Fact]
    public void An_object_goal_is_none_only_within_its_range_of_the_object_and_never_decided_here_when_it_needs_sight()
    {
        var t = new J1JourneyRig();
        var runner = new J1Runner();
        t.Context(runners: runner);
        t.CreateAt(0x50000007, J1JourneyRig.HoltburgCell, 86); // 2 m from the player
        var near = t.Travel!.GoTo(t.View, new TravelRequest(JourneyGrounds.Holtburg, TravelPurpose.Vendor, 0x50000007, Range: 5), t.Now);
        Assert.Equal((TravelMeans.None, TravelStatus.Arrived), (near.Means, near.Step(t.View, t.Now)));
        Assert.Empty(runner.Requests);

        var tight = t.Travel.GoTo(t.View, new TravelRequest(JourneyGrounds.Holtburg, TravelPurpose.Vendor, 0x50000007, Range: 1), t.Now);
        var sight = t.Travel.GoTo(t.View, new TravelRequest(JourneyGrounds.Holtburg, TravelPurpose.Seek, 0x50000007, Range: 20, NeedSight: true), t.Now);
        Assert.Equal((TravelMeans.Run, TravelMeans.Run), (tight.Means, sight.Means));
        Assert.Equal(2, runner.Requests.Count); // passed unchanged: the host builds the object goal
        Assert.True(runner.Requests[1].NeedSight);
    }

    [Fact]
    public void The_grounds_name_their_cells_and_need_a_terrain_height()
    {
        Assert.Null(JourneyGrounds.For(StationWhere.Belt, null));
        Assert.Null(JourneyGrounds.For(StationWhere.Here, (_, _, _) => 1f));
        var belt = JourneyGrounds.For(StationWhere.Belt, (_, _, _) => 10f)!;
        Assert.Equal(0xA5B00032u, belt.Location.Position.CellId);
        Assert.Equal(10.5f, belt.Location.Position.Local.Z);
        Assert.Equal(0x9FB50039u, JourneyGrounds.For(StationWhere.Forest, (_, _, _) => 1f)!.Location.Position.CellId);
        Assert.Equal(0x01050375u, JourneyGrounds.Den.Location.Position.CellId);
        Assert.Equal(0xA9B40019u, JourneyGrounds.Holtburg.Location.Position.CellId);
    }
}

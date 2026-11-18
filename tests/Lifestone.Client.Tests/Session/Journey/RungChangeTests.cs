using System.Numerics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 4 t2: the rung change. <see cref="RungRoute"/>'s decisions over the real travel file (the Burrow by the Refuge at 10 and by the Facility Hub at 15, the Holding by the Barracks drop at 60: the hub's routes first where their gates admit since M6 7 J; a level-9 climber refused by name, a tied
/// Temple 40+ by Primary Portal Recall, an untied Temple 20-40 tied then used, NoRecall entries never tied, the gem never planned, home by Lifestone Recall at 16 and by portals below), and <see cref="RungChangeStation"/>'s legs with check,
/// act, check over a stand-in ACE (<see cref="RungRig"/>: the portals of the travel file answer a Use with the teleport to their destination). The grounds are the Temple 40+ profile re-keyed with each ground's entry from the world SQL rows the
/// travel file names (a test's stand-in: item 3's spine profiles are not on this branch); their arrival landblocks are the tests' own.
/// </summary>
public sealed partial class RungChangeTests
{
    private static readonly TravelRoutes Routes = TravelRoutes.Default;
    private const int WalkStopsCount = Lifestone.Client.Session.Vitals.WalkStops.StallStops;

    private static HuntingGround Temple40 => GroundCatalog.Get("tusker-temple-fort-tethana");

    /// <summary>A ground of <paramref name="key"/> with <paramref name="entry"/>, arriving in <paramref name="arrival"/>'s landblock; no tie record.</summary>
    private static HuntingGround Ground(string key, GroundEntry? entry, uint arrival, GroundExitSection? exit = null) =>
        Temple40 with { Key = key, Name = key, Landblocks = [(ushort)(arrival >> 16)], Arrival = new GroundArrival(arrival, new Vector3(10, 10, 0), 1, 0), Portal = null, Entry = entry, Exit = exit };

    // The entries: landblock_instance rows of the travel file's runs (guid, cell, origin; the cell by the x/24*8 + y/24 + 1 rule), PropertyInt 86/87 and the bitmask's NoRecall as facts.md section 2 has them.
    private static GroundEntry BurrowEntry => new(22663, 0x7F981000, 0xF9810040, new Vector3(176.323f, 186.044f, 12.5f), 0xF9810040, new Vector3(176.3f, 183.0f, 12.5f), null, null, false);

    private static GroundEntry HoldingEntry => new(22680, 0x7EA7B000, 0xEA7B0001, new Vector3(15.744f, 17.16f, 54.575f), 0xEA7B0001, new Vector3(15.7f, 20.2f, 54.6f), 60, null, false);

    private static GroundEntry Temple2040Entry => new(22698, 0x7A2CA000, 0xA2CA003A, new Vector3(186.042f, 34.587f, 110.391f), 0xA2CA003A, new Vector3(186.0f, 37.6f, 110.4f), 20, 40, true);

    private static HuntingGround Burrow => Ground("tusker-burrow", BurrowEntry, 0x01D40100, new GroundExitSection(GroundExitKind.Portal, 42999, 0x01D40105, new Vector3(5, 5, 0), 0xF9810038, null));

    private static HuntingGround Holding => Ground("tusker-holding", HoldingEntry, 0x01D60100);

    private static HuntingGround Temple2040 => Ground("tusker-temple-20-40", Temple2040Entry, 0x5D4302F9);

    private static RungRouteInput Input(HuntingGround g, int level, RungWhere where = RungWhere.Home, string place = "holtburg", bool recall = false, bool portalRecall = false, bool tie = false, PortalTie tied = PortalTie.Unknown, bool gem = false, RungDirection direction = RungDirection.Out) =>
        new(g, direction, where, place, level, recall, portalRecall, tie, tied, gem);

    private static string[] Shape(RungPlan plan) => [.. plan.Legs.Select(l => l.KindName + ":" + l.Label)];

    /// <summary>The Burrow by the Refuge at 10 and by the Facility Hub from 15 (M6 7 J: its hop out of the hub plans and its route is first).</summary>
    [Fact]
    public void M6_8_I_the_way_home_by_portals_or_on_foot_needs_no_mana_the_way_out_and_the_recall_home_keep_theirs()
    {
        // round 1, 18:53 UTC: a retreat with no mana was stopped by precondition:mana and she stood while a Young Mosswart killed her; nothing the way home casts
        Assert.Equal(0, new RungChangeStation(RungDirection.Home).Needs.MinManaPercent);
        Assert.Equal(StationNeeds.None, new RungChangeStation(RungDirection.Home).Needs);
        Assert.Equal(20, new RungChangeStation(RungDirection.Out).Needs.MinManaPercent); // Primary Portal Recall, the tie and the recharge at the door cast
        Assert.Equal(RecallHomeStation.MinManaPercent, new RecallHomeStation().Needs.MinManaPercent); // a recall needs mana: its precondition stays
        Assert.True(RecallHomeStation.MinManaPercent > 0);

    }

    [Fact]
    public void The_Burrow_goes_by_the_Refuge_at_10_and_by_the_Facility_Hub_at_15()
    {
        var at10 = RungRoute.Decide(Input(Burrow, 10, tie: true), Routes);
        Assert.Null(at10.Stop);
        Assert.Equal("by-refuge", at10.RouteKey);
        Assert.Equal(["hop:holtburg-to-network", "hop:network-to-refuge", "run:via-1", "run:via-2", "run:entry-front", "gate:entry:tusker-burrow", "enter:entry:tusker-burrow"], Shape(at10));
        Assert.Equal(3, at10.Hops);
        Assert.Equal("untieable", at10.Tied); // NoRecall (bitmask 49): Primary Portal Tie known, never planned
        Assert.Equal(0xF682u, at10.Legs[1].Portal!.DestLandblock);
        Assert.Equal(0x01D4u, at10.Legs[^1].Portal!.DestLandblock);

        var at15 = RungRoute.Decide(Input(Burrow, 15), Routes);
        Assert.Equal("by-facility-hub", at15.RouteKey);
        Assert.Equal(["hop:holtburg-to-network", "hop:network-to-facility-hub", "hop:hub-burrow", "run:entry-front", "gate:entry:tusker-burrow", "enter:entry:tusker-burrow"], Shape(at15));
        Assert.Equal("pass", at15.Gate);
    }

    /// <summary>The Holding at 60 by the Barracks drop (M6 7 J: the nearest start, its route first since the hop out of the Facility Hub plans).</summary>
    [Fact]
    public void The_Holding_at_60_goes_by_the_Barracks_drop_and_is_never_tied()
    {
        var plan = RungRoute.Decide(Input(Holding, 60, tie: true), Routes);
        Assert.Equal("by-facility-hub-barracks", plan.RouteKey);
        Assert.Equal(["hop:holtburg-to-network", "hop:network-to-facility-hub", "hop:hub-barracks", "run:entry-front", "gate:entry:tusker-holding", "enter:entry:tusker-holding"], Shape(plan));
        Assert.Equal("untieable", plan.Tied); // D51: the Holding's entry is NoRecall
        Assert.Equal(60, plan.Legs[4].Portal!.MinLevel);

        var at59 = RungRoute.Decide(Input(Holding, 59), Routes);
        Assert.Equal("gate:entry:tusker-holding:59<60", at59.Stop);
        Assert.Equal("refused:60", at59.Gate);
        Assert.Empty(at59.Legs);
    }

    [Fact]
    public void A_level_9_climber_is_refused_by_name_before_any_leg()
    {
        var plan = RungRoute.Decide(Input(Burrow, 9), Routes);
        Assert.Equal("gate:network-to-refuge:9<10", plan.Stop);
        Assert.Equal("refused:10", plan.Gate);
        Assert.Equal(RungRoute.NoRoute, plan.RouteKey);
        Assert.Empty(plan.Legs);
    }

    [Fact]
    public void A_tied_Temple_40_goes_by_Primary_Portal_Recall_from_anywhere()
    {
        var plan = RungRoute.Decide(Input(Temple40, 45, RungWhere.Elsewhere, "island", portalRecall: true, tie: true, tied: PortalTie.Tied), Routes);
        Assert.Equal(RungRoute.PortalRecallRoute, plan.RouteKey);
        var leg = Assert.Single(plan.Legs);
        Assert.Equal(RungLegKind.PortalRecall, leg.Kind);
        Assert.Equal(Temple40.Portal!.RecallSpell, leg.Spell);
        Assert.Equal(Temple40.ArrivalLandblock(), leg.Cell >> 16);
        Assert.Equal("known", plan.Tied);

        // Tied but the spellbook lacks the recall: the route by Fort Tethana, no tie (already tied).
        var noBook = RungRoute.Decide(Input(Temple40, 45, tie: true, tied: PortalTie.Tied), Routes);
        Assert.Equal("by-fort-tethana", noBook.RouteKey);
        Assert.DoesNotContain(noBook.Legs, l => l.Kind == RungLegKind.Tie);
        Assert.Equal("known", noBook.Tied);
    }

    [Fact]
    public void An_untied_Temple_20_40_at_20_ties_then_uses_when_Primary_Portal_Tie_is_known()
    {
        var plan = RungRoute.Decide(Input(Temple2040, 20, tie: true), Routes);
        Assert.Equal("by-new-colier", plan.RouteKey);
        Assert.Equal(["hop:new-colier", "run:entry-front", "gate:entry:tusker-temple-20-40", "tie:entry:tusker-temple-20-40", "enter:entry:tusker-temple-20-40"], Shape(plan));
        Assert.Equal(DenTravelStation.PortalTieSpell, plan.Legs[3].Spell);
        Assert.Equal("yes", plan.Tied);

        var noTie = RungRoute.Decide(Input(Temple2040, 20), Routes);
        Assert.DoesNotContain(noTie.Legs, l => l.Kind == RungLegKind.Tie);
        Assert.Equal("no", noTie.Tied);

        var at41 = RungRoute.Decide(Input(Temple2040, 41, tie: true), Routes);
        Assert.Equal("gate:entry:tusker-temple-20-40:41>40", at41.Stop);
    }

    [Fact]
    public void The_gem_is_never_planned_only_reported_held()
    {
        var plan = RungRoute.Decide(Input(Burrow, 10, gem: true), Routes);
        Assert.Equal("held", plan.Gem);
        Assert.DoesNotContain(plan.Legs, l => l.Label.Contains("gem", StringComparison.Ordinal));
        Assert.Equal("none", RungRoute.Decide(Input(Burrow, 10), Routes).Gem);
    }

    [Fact]
    public void Home_is_Lifestone_Recall_at_16_and_portals_below()
    {
        var at16 = RungRoute.Decide(Input(Burrow, 16, RungWhere.Elsewhere, "island", recall: true, direction: RungDirection.Home), Routes);
        Assert.Equal(RungRoute.LifestoneRecallRoute, at16.RouteKey);
        var leg = Assert.Single(at16.Legs);
        Assert.Equal(1635u, leg.Spell);
        Assert.Equal(0xA9B4u, leg.Cell >> 16);

        var at15 = RungRoute.Decide(Input(Burrow, 15, RungWhere.Elsewhere, "island", direction: RungDirection.Home), Routes);
        Assert.Equal("refuge", at15.RouteKey);
        Assert.Equal(["hop:refuge-to-network", "hop:network-to-holtburg"], Shape(at15));

        // From inside the ground: its exit portal first, then the way home from where the exit drops (the island).
        var inside = RungRoute.Decide(Input(Burrow, 15, RungWhere.AtGround, "0x01D4", direction: RungDirection.Home), Routes);
        Assert.Equal(["hop:exit:tusker-burrow", "hop:refuge-to-network", "hop:network-to-holtburg"], Shape(inside));
        Assert.Equal(0xF981u, inside.Legs[0].Portal!.DestLandblock);

        // Left by recall with no recall in the book: the named stop.
        var stuck = RungRoute.Decide(Input(Holding with { Exit = new GroundExitSection(GroundExitKind.Recall, 0, 0, null, 0, null) }, 60, RungWhere.AtGround, "0x01D6", direction: RungDirection.Home), Routes);
        Assert.StartsWith("TODO(m6-way-home)", stuck.Stop, StringComparison.Ordinal);

        Assert.Empty(RungRoute.Decide(Input(Burrow, 15, direction: RungDirection.Home), Routes).Legs); // at home already
    }

    [Fact]
    public void Elsewhere_goes_home_first_then_the_route_and_a_pending_profile_passes_the_portals_gate()
    {
        var away = RungRoute.Decide(Input(Burrow, 12, RungWhere.Elsewhere, "eastham"), Routes);
        Assert.Equal(["hop:eastham-to-network", "hop:network-to-holtburg", "hop:holtburg-to-network", "hop:network-to-refuge", "run:via-1", "run:via-2", "run:entry-front", "gate:entry:tusker-burrow", "enter:entry:tusker-burrow"], Shape(away));

        // No entry section (a pending profile): the route's run names the entry; its gate is the server's, read at the Use.
        var pending = RungRoute.Decide(Input(Ground("tusker-lodge", null, 0x01D50100), 10), Routes);
        Assert.Equal("by-refuge", pending.RouteKey);
        Assert.Equal(["hop:holtburg-to-network", "hop:network-to-refuge", "enter:entry:tusker-lodge"], Shape(pending));
        Assert.Equal(22688u, pending.Legs[^1].Portal!.Wcid);
        Assert.Equal("untieable", pending.Tied);

        Assert.Empty(RungRoute.Decide(Input(Burrow, 12, RungWhere.AtGround, "0x01D4"), Routes).Legs);
        Assert.Empty(RungRoute.Decide(Input(GroundCatalog.Get(FightOptions.DefaultGround), 12), Routes).Legs); // the den: handed over to den-travel
        Assert.StartsWith("TODO(m6-ground-route)", RungRoute.Decide(Input(Ground("no-such-ground", null, 0x01D70100), 12), Routes).Stop, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ the station over the stand-in ACE

    [Fact]
    public void The_station_hops_by_the_Refuge_and_enters_the_Burrow_with_one_line_a_leg()
    {
        var t = new RungRig(level: 10);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.Equal("ground=tusker-burrow level=10 route=by-refuge hops=3 walked_m=" + Token(run.VerdictFields(), "walked_m") + " gem=none tied=untieable gate=pass ok=yes fail=none direction=out", run.VerdictFields());
        Assert.Equal(7, t.Lines("rung_change_leg: ").Count); // M6 7 Z: the two via points of the run
        Assert.Contains(t.Lines("rung_change_leg: "), l => l.Contains("n=2 kind=hop edge=network-to-refuge from=0x0007 to=0xF682", StringComparison.Ordinal) && l.Contains("ok=yes", StringComparison.Ordinal));
        Assert.Single(t.Lines("rung_change: ground=tusker-burrow"));
        Assert.Equal(0x01D4u, t.Landblock);
        Assert.Equal(0, t.Places);
    }

    [Fact]
    public void A_gate_refusal_stops_before_any_walk()
    {
        var t = new RungRig(level: 9);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("fail=gate:network-to-refuge:9<10", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains("gate=refused:10", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Empty(t.Travel.Requests);
        Assert.DoesNotContain(t.Events, e => e.StartsWith("use:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_use_with_no_teleport_uses_again_once_then_fails_by_name()
    {
        var t = new RungRig(level: 10);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Silent.Add(Routes.Edge("network-to-refuge").Guid);
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("fail=hop:network-to-refuge:no-teleport", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(2, t.Events.Count(e => e == "use:" + Routes.Edge("network-to-refuge").Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Single(t.Lines("hop network-to-refuge: recovery=use-again"));
        Assert.Contains(t.Lines("rung_change_leg: "), l => l.Contains("n=2 kind=hop", StringComparison.Ordinal) && l.Contains("ok=no", StringComparison.Ordinal));
    }

    [Fact]
    public void A_failed_hop_to_the_island_uses_the_gem_only_when_held()
    {
        var t = new RungRig(level: 10, gem: true);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        t.Silent.Add(Routes.Edge("network-to-refuge").Guid);
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains("gem=used", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains("use:" + RungRig.GemGuid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), t.Events);
        Assert.Contains(t.Lines("rung_change_leg: "), l => l.Contains("n=2 kind=hop", StringComparison.Ordinal) && l.Contains("ok=yes recovery=use-again gem=used", StringComparison.Ordinal));
        Assert.Equal(0x01D4u, t.Landblock);
    }

    /// <summary>
    /// M6 4 F2 (review 4-R6): the gem replaces the rest of the hops, not only the one into the island. Holtburg's Network portal gives no teleport (a hop that fails by name, not a gate) while the gem is held: the route's
    /// next place is the island, so the gem is used (<c>gem=used</c>), the Network-to-Refuge hop is skipped (its portal is never used), and the legs go on from the gem's drop (the gate, the enter). The gem's source is
    /// item 5's (Brighteyes's tusk turn-ins): the rig only holds it.
    /// </summary>
    [Fact]
    public void A_failed_first_hop_of_a_route_into_the_island_uses_the_gem_for_the_rest_of_the_hops_M6_4_F2()
    {
        var t = new RungRig(level: 10, gem: true);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        t.Silent.Add(Routes.Edge("holtburg-to-network").Guid);
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.Contains("gem=used", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains("ok=yes", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains("use:" + RungRig.GemGuid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), t.Events);
        Assert.DoesNotContain("use:" + Routes.Edge("network-to-refuge").Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), t.Events); // skipped: the gem is the way to the island
        Assert.Contains(t.Lines("rung_change_leg: "), l => l.Contains("n=1 kind=hop edge=holtburg-to-network", StringComparison.Ordinal) && l.Contains("gem=used", StringComparison.Ordinal));
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("replaces the hops from here to network-to-refuge", StringComparison.Ordinal) && l.Contains("skipped=[network-to-refuge]", StringComparison.Ordinal));
        Assert.Contains(t.Lines("rung_change_leg: "), l => l.Contains("kind=enter", StringComparison.Ordinal) && l.Contains("ok=yes", StringComparison.Ordinal)); // the run from the gem's drop
        Assert.Equal(0x01D4u, t.Landblock);
    }

    /// <summary>
    /// M6 7 J, changed by M6 7 M: a failed hop of the Facility Hub's route with the Aphus Lassel Portal Gem held is still replaced by the gem (the hops to the island, the run on from its drop), and never the Refuge route.
    /// </summary>
    [Fact]
    public void A_failed_hop_of_the_hub_route_takes_the_gem_when_held_and_never_the_Refuge_route()
    {
        var t = new RungRig(level: 15, gem: true);
        t.Portals(Routes, "holtburg-to-network", "network-to-facility-hub", "hub-burrow");
        t.Entry(BurrowEntry, Burrow);
        t.Silent.Add(Routes.Edge("hub-burrow").Guid);
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));

        Assert.DoesNotContain("use:" + Routes.Edge("network-to-refuge").Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), t.Events);
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.Contains("gem=used", run.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain("route_fallback", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0x01D4u, t.Landblock);
    }

    /// <summary>
    /// M6 7 M: no gem, the hub's last hop fails by name with the body in the hub: the station falls back once to the ground's next admitted route (the Refuge's) from where the body stands (the hub's way home reaches the Network, the route joins there: no walk to
    /// Holtburg), names it on the verdict (<c>route_fallback=&lt;from&gt;:&lt;to&gt;:&lt;why&gt;</c>) and in the log, and remembers the failed route for the run (the next plan puts it last).
    /// </summary>
    [Fact]
    public void A_failed_hub_hop_with_no_gem_falls_back_once_to_the_Refuge_route_from_where_the_body_stands_M6_7_M()
    {
        var t = new RungRig(level: 15);
        t.Portals(Routes, "holtburg-to-network", "network-to-facility-hub", "hub-burrow", "facility-hub-to-eastham", "eastham-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        t.Silent.Add(Routes.Edge("hub-burrow").Guid);
        var context = t.Context(Burrow);
        var (run, status) = t.RunStation(new RungChangeStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.True(run.VerdictFields().Contains("route_fallback=by-facility-hub:by-refuge:hop:hub-burrow:no-teleport", StringComparison.Ordinal), run.VerdictFields() + " | " + string.Join(" | ", t.R.H.Log.Lines.Where(l => l.Contains("rung_change", StringComparison.Ordinal) || l.Contains("route_fallback", StringComparison.Ordinal)).Select(l => l.Length > 700 ? l[..700] : l)));
        Assert.Contains("route=by-refuge ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains("ok=yes", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0x01D4u, t.Landblock);
        Assert.Contains("use:" + Routes.Edge("network-to-refuge").Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), t.Events);
        Assert.Equal(1, t.Events.Count(e => e == "use:" + Routes.Edge("holtburg-to-network").Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture))); // joined at the Network: Holtburg's portal is not used again
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("route_fallback: hop hub-burrow of route by-facility-hub failed by name", StringComparison.Ordinal) && l.Contains("check before", StringComparison.Ordinal));
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("route_fallback=by-facility-hub:by-refuge:hop:hub-burrow:no-teleport: check after", StringComparison.Ordinal));
        Assert.Equal(["by-facility-hub"], context.Facts.FailedRoutesOf(Burrow.Key));
    }

    /// <summary>M6 7 M: the second failure of a station run stops by name as before: one fallback a run, and the first failure's name stands.</summary>
    [Fact]
    public void A_second_failed_hop_after_the_fallback_stops_by_name_M6_7_M()
    {
        var t = new RungRig(level: 15);
        t.Portals(Routes, "holtburg-to-network", "network-to-facility-hub", "hub-burrow", "facility-hub-to-eastham", "eastham-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        t.Silent.Add(Routes.Edge("hub-burrow").Guid);
        t.Silent.Add(Routes.Edge("network-to-refuge").Guid);
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("route_fallback=by-facility-hub:by-refuge:hop:hub-burrow:no-teleport", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains("ok=no fail=hop:network-to-refuge:no-teleport", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(t.Lines("route_fallback: hop "));
    }

    /// <summary>M6 7 M: a hop both routes share (Holtburg's portal) is not worth a fallback: the next plan takes the same portal, so the station stops by name and says so.</summary>
    [Fact]
    public void A_failed_hop_both_routes_share_does_not_fall_back_M6_7_M()
    {
        var t = new RungRig(level: 15);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Silent.Add(Routes.Edge("holtburg-to-network").Guid);
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("fail=hop:holtburg-to-network:no-teleport", run.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain("route_fallback=", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("route_fallback: none", StringComparison.Ordinal) && l.Contains("takes holtburg-to-network", StringComparison.Ordinal));
    }

    /// <summary>M6 7 M: a route remembered as failed is planned last by the next rung change of the run (and kept when it is the only one the level admits).</summary>
    [Fact]
    public void A_route_remembered_as_failed_is_planned_last_M6_7_M()
    {
        var first = Routes.FirstAdmitted(Burrow.Key, 15, Burrow.Entry?.MinLevel, Burrow.Entry?.MaxLevel);
        var avoiding = Routes.FirstAdmitted(Burrow.Key, 15, Burrow.Entry?.MinLevel, Burrow.Entry?.MaxLevel, [first.Route!.Key]);
        Assert.Equal("by-facility-hub", first.Route.Key);
        Assert.Equal("by-refuge", avoiding.Route!.Key);
        var both = Routes.FirstAdmitted(Burrow.Key, 15, Burrow.Entry?.MinLevel, Burrow.Entry?.MaxLevel, ["by-facility-hub", "by-refuge"]);
        Assert.Equal("by-facility-hub", both.Route!.Key);
    }

    /// <summary>A gate refusal is not a failure the gem replaces, and a route that does not lead into the island is not either (the rule reads the route's next place).</summary>
    [Fact]
    public void A_failed_first_hop_with_no_gem_held_stays_a_named_failure_M6_4_F2()
    {
        var t = new RungRig(level: 10);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Silent.Add(Routes.Edge("holtburg-to-network").Guid);
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("fail=hop:holtburg-to-network:no-teleport", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains("gem=none", run.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_arrival_in_the_wrong_landblock_is_named()
    {
        var t = new RungRig(level: 10);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.DropOverride[Routes.Edge("network-to-refuge").Guid] = (0xF6810033, new Vector3(145, 50, 58));
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("fail=hop:network-to-refuge:wrong-landblock", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("FAILED", StringComparison.Ordinal) && l.Contains("not in landblock 0xF682", StringComparison.Ordinal));
    }

    [Fact]
    public void An_untied_temple_is_tied_at_its_entry_before_the_use()
    {
        var t = new RungRig(level: 20, spells: [47, 48]);
        t.Portals(Routes, "new-colier");
        t.Entry(Temple2040Entry, Temple2040);
        var context = t.Context(Temple2040);
        context.Facts.SetPortalTie(FightOptions.DefaultGround, PortalTie.Tied);
        var (run, status) = t.RunStation(new RungChangeStation(), context);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains("tied=yes", run.VerdictFields(), StringComparison.Ordinal);
        var tie = t.Events.IndexOf("cast:47@" + Temple2040Entry.Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(tie >= 0 && tie < t.Events.IndexOf("use:" + Temple2040Entry.Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)), string.Join(", ", t.Events));
        Assert.Equal(PortalTie.Tied, context.Facts.PortalTieOf("tusker-temple-20-40"));
        Assert.Equal(PortalTie.Untied, context.Facts.PortalTieOf(FightOptions.DefaultGround)); // the primary tie is one slot: it holds the Temple, so the den is known untied (M6 4 F1, 4-R2)
        Assert.Equal("tusker-temple-20-40", context.Facts.PrimaryPortalSlot);
    }

    [Fact]
    public void The_way_home_below_16_hops_the_Refuge_portal_and_the_hub()
    {
        var t = new RungRig(level: 12, at: (0xF6820033, new Vector3(145.7f, 49.9f, 58f)));
        t.Portals(Routes, "refuge-to-network", "network-to-holtburg");
        var (run, status) = t.RunStation(new RungChangeStation(RungDirection.Home), t.Context(Burrow));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains("route=refuge hops=2", run.VerdictFields(), StringComparison.Ordinal);
        Assert.EndsWith("direction=home", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0xA9B4u, t.Landblock);
        Assert.DoesNotContain(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal));
        Assert.Equal(RungDirection.Home, RungChangeStation.ParseDirection("home"));
        Assert.Throws<ArgumentException>(() => RungChangeStation.ParseDirection("sideways"));
    }

    [Fact]
    public void A_station_started_in_the_Town_Network_has_a_way_home_by_the_Holtburg_portal()
    {
        // M6 4 T3a (closes TODO(m6-way-home) for the hub): the Network is a starting place of a failed hop.
        var home = RungRoute.Decide(Input(Burrow, 12, RungWhere.Elsewhere, "town-network", direction: RungDirection.Home), Routes);
        Assert.Null(home.Stop);
        Assert.Equal("town-network", home.RouteKey);
        Assert.Equal(["hop:network-to-holtburg"], Shape(home));

        var out12 = RungRoute.Decide(Input(Burrow, 12, RungWhere.Elsewhere, "town-network"), Routes);
        Assert.Null(out12.Stop);
        Assert.Equal(["hop:network-to-holtburg", "hop:holtburg-to-network", "hop:network-to-refuge", "run:via-1", "run:via-2", "run:entry-front", "gate:entry:tusker-burrow", "enter:entry:tusker-burrow"], Shape(out12));

        var t = new RungRig(level: 12, at: (0x00070143, new Vector3(70, -60, 0.005f)));
        t.Portals(Routes, "network-to-holtburg");
        var (run, status) = t.RunStation(new RungChangeStation(RungDirection.Home), t.Context(Burrow));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.Contains("route=town-network hops=1", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0xA9B4u, t.Landblock);
    }

    [Fact]
    public void A_station_started_in_the_Facility_Hub_goes_home_by_the_Eastham_portal()
    {
        var home = RungRoute.Decide(Input(Burrow, 12, RungWhere.Elsewhere, "facility-hub", direction: RungDirection.Home), Routes);
        Assert.Null(home.Stop);
        Assert.Equal(["hop:facility-hub-to-eastham", "hop:eastham-to-network", "hop:network-to-holtburg"], Shape(home));
        Assert.Equal(0x8A02u, home.Legs[0].Portal!.FromLandblocks[0]);

        var out15 = RungRoute.Decide(Input(Burrow, 15, RungWhere.Elsewhere, "facility-hub"), Routes);
        Assert.Null(out15.Stop);
        Assert.Equal(["hop:facility-hub-to-eastham", "hop:eastham-to-network", "hop:network-to-holtburg", "hop:holtburg-to-network", "hop:network-to-facility-hub", "hop:hub-burrow", "run:entry-front", "gate:entry:tusker-burrow", "enter:entry:tusker-burrow"], Shape(out15)); // the hub route first, from the route's start (M6 4 F4)

        var t = new RungRig(level: 12, at: (0x8A020118, new Vector3(71.7f, -31.7f, 0.005f)));
        t.Portals(Routes, "facility-hub-to-eastham", "eastham-to-network", "network-to-holtburg");
        var (run, status) = t.RunStation(new RungChangeStation(RungDirection.Home), t.Context(Burrow));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.Contains("route=facility-hub hops=3", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0xA9B4u, t.Landblock);
    }

    [Fact]
    public void The_hops_front_point_is_on_the_side_the_portal_faces_else_on_the_approach_side()
    {
        // The Holtburg portal in the Network (row 0x70007028) faces west (-1, 0): the front is 3 m west of its origin, wherever the body comes from.
        var edge = Routes.Edge("network-to-holtburg");
        var portal = RungPortal.Of(edge, Routes);
        Assert.NotNull(portal.Facing);
        var from = new CellFrame(0x00070143, new AcFrame(new Vector3(70, -60, 0.005f), Quaternion.Identity));
        var (cell, at, basis) = TravelLeg.FrontOf(portal, edge.Cell, from);
        Assert.Equal("facing", basis);
        Assert.Equal(edge.Cell, cell);
        Assert.Equal(edge.Origin.X - 3.0f, at.X, 0.05);
        Assert.Equal(edge.Origin.Y, at.Y, 0.05);

        // With no facing in the file: T2's point toward the body (logged as the approach).
        var noFacing = portal with { Facing = null };
        var (_, approach, basisA) = TravelLeg.FrontOf(noFacing, edge.Cell, from);
        Assert.Equal("approach", basisA);
        var toward = Vector2.Normalize(new Vector2(70 - edge.Origin.X, -60 - edge.Origin.Y));
        Assert.Equal(edge.Origin.X + (toward.X * 3.0f), approach.X, 0.05);
        Assert.Equal(edge.Origin.Y + (toward.Y * 3.0f), approach.Y, 0.05);
        Assert.NotEqual(at, approach);

        // An outdoor portal (Holtburg's, row 0x7A9B4080) clamps and re-cells the point: the front is 3 m along its facing.
        var outdoor = Routes.Edge("holtburg-to-network");
        var outPortal = RungPortal.Of(outdoor, Routes);
        var (_, outAt, outBasis) = TravelLeg.FrontOf(outPortal, outdoor.Cell, new CellFrame(0xA9B40019, new AcFrame(new Vector3(84, 7.7f, 94), Quaternion.Identity)));
        Assert.Equal("facing", outBasis);
        Assert.Equal(3.0, Vector2.Distance(new Vector2(outAt.X, outAt.Y), new Vector2(outdoor.Origin.X, outdoor.Origin.Y)), 0.05);
    }

    [Fact]
    public void The_station_walks_to_the_facing_side_and_logs_it_by_name()
    {
        var t = new RungRig(level: 10);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        var (_, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains(t.Lines("hop holtburg-to-network: front point"), l => l.EndsWith("front=facing", StringComparison.Ordinal));
        // M6 4 F5: the Network hop is walked from a drop that lies past its facing front, so it takes the approach side first (the walker stays before the portal).
        Assert.Contains(t.Lines("hop network-to-refuge: front point"), l => l.Contains("front=approach why=facing-past-portal", StringComparison.Ordinal));
        // The ground's entry (the profile has no rotation) takes it from the route's run row.
        var enter = RungRoute.Decide(Input(Burrow, 10), Routes).Legs[^1].Portal!;
        Assert.NotNull(enter.Facing);
        Assert.Equal(Routes.RoutesFor("tusker-burrow")[0].Run!.EntryFacing!.Value.Forward, enter.Facing);
    }

    /// <summary>
    /// M6 4 w6a (<c>TODO(m6-front-wall)</c>, T3a's note): the front point on the side a portal faces may stand in a wall of a Network room. When the walk to it fails, the hop walks once to T2's point on the
    /// approach side (logged <c>front=approach recovery=front-wall</c>) and goes on; when that walk fails too, the hop fails by name, <c>&lt;key&gt;:front-wall</c>, with both walks' failures.
    /// </summary>
    [Fact]
    public void A_front_point_in_a_wall_falls_back_once_to_the_approach_side_then_fails_by_name()
    {
        var edge = Routes.Edge("holtburg-to-network");
        var t = new RungRig(level: 10);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        var facingWalks = 0;
        t.Travel.Refuse = r => r.Destination.Name == "holtburg-to-network-front" && facingWalks++ == 0 ? "no path: the point is in a wall" : null;
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.Contains("fail=none", run.VerdictFields(), StringComparison.Ordinal);
        var recovery = Assert.Single(t.Lines("recovery=front-wall: the walk to the facing side failed"));
        Assert.Contains("hop holtburg-to-network: front point", recovery, StringComparison.Ordinal);
        Assert.Contains("front=approach", recovery, StringComparison.Ordinal);
        Assert.Contains("no path: the point is in a wall", recovery, StringComparison.Ordinal);
        var walks = t.Travel.Requests.Where(r => r.Destination.Name == "holtburg-to-network-front").Select(r => r.Destination.Location.Position.Local).ToList();
        Assert.Equal(2, walks.Count);
        Assert.Equal(edge.Origin.X + (RungPortal.Of(edge, Routes).Facing!.Value.X * 3f), walks[0].X, 0.05); // the facing side first
        Assert.NotEqual(walks[0], walks[1]);
        Assert.Equal(3.0, Vector2.Distance(new Vector2(walks[1].X, walks[1].Y), new Vector2(edge.Origin.X, edge.Origin.Y)), 0.05); // then 3 m toward the body

        // Both walks refused: the hop fails by name, the recovery spent, no Use sent.
        var w = new RungRig(level: 10);
        w.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        w.Entry(BurrowEntry, Burrow);
        w.Travel.Refuse = r => r.Destination.Name == "holtburg-to-network-front" ? "no path: walled in" : null;
        var (failed, failedStatus) = w.RunStation(new RungChangeStation(), w.Context(Burrow));
        Assert.Equal(ScenarioStatus.Failed, failedStatus);
        Assert.Contains("fail=holtburg-to-network:front-wall", failed.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(2, w.Travel.Requests.Count(r => r.Destination.Name == "holtburg-to-network-front"));
        Assert.DoesNotContain(w.Events, e => e == "use:" + edge.Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// M6 7 AI: the hop's walk carries the other portals, at their clearance, and a walk the runner ended for a portal it could not go round fails the hop by that name: the route's fallback reads it as any hop's. M6 14: the walk to the front
    /// carries its own portal too, as the walk's own (kept by its touch and her stop, never the others' clearance); the seek, which goes inside that reach to use it, carries only the others.
    /// </summary>
    [Fact]
    public void A_hop_s_walk_keeps_clear_of_the_other_portals_and_fails_by_name_when_it_cannot()
    {
        var edge = Routes.Edge("holtburg-to-network");
        var t = new RungRig(level: 10);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        var (_, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        var walk = t.Travel.Requests.First(r => r.Destination.Name == "holtburg-to-network-front");
        Assert.NotNull(walk.KeepOff);
        Assert.DoesNotContain(walk.KeepOff.Portals, p => p.Name == edge.Name && !p.Own);
        Assert.Equal(edge.Name, walk.KeepOff.Own?.Name);
        Assert.Contains(walk.KeepOff.Portals, p => p.Name == Routes.Edge("hub-burrow").Name);
        Assert.All(t.Travel.Requests.Where(r => r.Purpose == TravelPurpose.Seek), r => Assert.Null(Assert.IsType<Lifestone.Client.Movement.Wayfinding.PortalKeepOff>(r.KeepOff).Own));
        var run = t.Travel.Requests.First(r => r.Destination.Name == "entry-front");
        Assert.NotNull(run.KeepOff);

        var w = new RungRig(level: 10);
        w.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        w.Entry(BurrowEntry, Burrow);
        w.Travel.Refuse = r => r.Destination.Name == "holtburg-to-network-front" ? "travel:run:blocked: no path: " + Lifestone.Client.Movement.Wayfinding.NavRunner.PortalKeepOffFailure + ":Neighbour: no way round it" : null;
        var (failed, failedStatus) = w.RunStation(new RungChangeStation(), w.Context(Burrow));
        Assert.Equal(ScenarioStatus.Failed, failedStatus);
        Assert.Contains("fail=holtburg-to-network:portal-keep-off", failed.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(w.Events, e => e == "use:" + edge.Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// M6 4 F5 (the live gate's round 2): a walk to the front the server ends by a teleport (the portal took the body on touch) is checked at once. Into the edge's destination landblock = the hop taken on touch (no Use, no
    /// front-wall recovery) and the station goes on; elsewhere = the named failure <c>hop:&lt;edge&gt;:moved-elsewhere:&lt;landblock&gt;</c>.
    /// </summary>
    [Fact]
    public void A_walk_the_server_ends_by_a_teleport_into_the_destination_is_the_hop_taken_on_touch()
    {
        var edge = Routes.Edge("holtburg-to-network");
        var t = new RungRig(level: 10);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        t.Travel.TeleportOn = r => r.Destination.Name == "holtburg-to-network-front" ? (edge.DestCell, edge.DestOrigin) : null;
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.Contains("fail=none", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("holtburg-to-network", StringComparison.Ordinal) && l.Contains("taken=touch", StringComparison.Ordinal));
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("recovery=front-wall", StringComparison.Ordinal));
        Assert.DoesNotContain(t.Events, e => e == "use:" + edge.Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1, t.Travel.Requests.Count(r => r.Destination.Name == "holtburg-to-network-front"));
        Assert.Contains(t.Events, e => e.StartsWith("use:", StringComparison.Ordinal)); // the next leg (network-to-refuge) went on
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("life check after the move on hop holtburg-to-network: no death evidence", StringComparison.Ordinal)); // M6 4 F6: the death check ran first and found none
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("died:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_walk_the_server_ends_by_a_teleport_elsewhere_fails_by_name()
    {
        var edge = Routes.Edge("holtburg-to-network");
        var t = new RungRig(level: 10);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        t.Travel.TeleportOn = r => r.Destination.Name == "holtburg-to-network-front" ? (0xF6820033u, new Vector3(145.7f, 49.9f, 58f)) : null;
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("fail=hop:holtburg-to-network:moved-elsewhere:F682", run.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("recovery=front-wall", StringComparison.Ordinal));
        Assert.Equal(1, t.Travel.Requests.Count(r => r.Destination.Name == "holtburg-to-network-front"));
        Assert.DoesNotContain(t.Events, e => e == "use:" + edge.Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("life check after the move on hop holtburg-to-network: no death evidence", StringComparison.Ordinal)); // M6 4 F6: not a death, so F5's rule stands
    }

    /// <summary>
    /// M6 4 F6 (the live round 4: the Throwaway was killed by island tuskers on the run from the Refuge to the Burrow's entry front and respawned at the Training Academy 0x860201AD): a run the server ends by a teleport after a death (the
    /// victim notice "You have been killed by ...", the health at 0) is named a death, <c>fail=died:run:respawned=&lt;cell&gt;</c>, never <c>travel:run:moved</c>, with the death cell, the killer and the respawn cell logged.
    /// </summary>
    [Fact]
    public void A_death_during_the_run_is_named_died_run_with_the_respawn_cell_never_travel_run_moved()
    {
        var t = new RungRig(level: 10);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        t.Travel.TeleportOn = r => r.Destination.Name == "entry-front" ? (0x860201ADu, new Vector3(60f, -40f, 0f)) : null;
        t.Travel.DiesOn = r => r.Destination.Name == "entry-front" ? "You have been killed by Tusker Slave!" : null;
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=died:run:respawned=0x860201AD ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain("travel:run:moved", run.VerdictFields(), StringComparison.Ordinal);
        var died = Assert.Single(t.Lines("died on the run leg to entry-front: "));
        Assert.Contains("death_cell=0xF982", died, StringComparison.Ordinal); // M6 7 Z: the last via, where the run to the front began
        Assert.Contains("killer='You have been killed by Tusker Slave!'", died, StringComparison.Ordinal);
        Assert.Contains("evidence=notice+health 0", died, StringComparison.Ordinal);
        Assert.Contains("respawned=0x860201AD", died, StringComparison.Ordinal);
        Assert.Contains(t.Lines("life check before the run leg to entry-front: "), l => l.Contains("alive=yes", StringComparison.Ordinal));
        Assert.Contains(t.Lines("rung_change_leg: "), l => l.Contains("n=5 kind=run edge=entry-front ", StringComparison.Ordinal) && l.Contains("ok=no", StringComparison.Ordinal)); // M6 7 Z: legs 3 and 4 are the via points
        Assert.DoesNotContain(t.Events, e => e == "use:" + BurrowEntry.Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>M6 7 Z: a level under the encounter cells' creatures walks the travel file's via points in turn before the entry's front; a level from below_level on runs straight, and the hub's route has none.</summary>
    [Fact]
    public void The_run_takes_the_via_points_below_their_level_and_runs_straight_from_it()
    {
        var plan = RungRoute.Decide(Input(Burrow, 10), Routes);
        var run = plan.Legs.Where(l => l.Kind == RungLegKind.Run).ToArray();
        Assert.Equal(["via-1", "via-2", "entry-front"], run.Select(l => l.Label));
        var via = Routes.RoutesFor(Burrow.Key).Single(r => r.Key == "by-refuge").Run!.Via!;
        Assert.Equal(via.Points[0].Cell, run[0].Cell);
        Assert.Equal(via.Points[1].Origin, run[1].At);
        Assert.All(run[..2], l => Assert.NotNull(l.Via));
        Assert.Null(run[2].Via);
        Assert.Contains("via points", plan.Why, StringComparison.Ordinal);
        var strong = RungRoute.Decide(Input(Burrow, via.BelowLevel) with { FailedRoutes = ["by-facility-hub"] }, Routes);
        Assert.Equal("by-refuge", strong.RouteKey);
        Assert.Equal(["entry-front"], strong.Legs.Where(l => l.Kind == RungLegKind.Run).Select(l => l.Label));
        var hub = RungRoute.Decide(Input(Burrow, 15), Routes);
        Assert.Equal("by-facility-hub", hub.RouteKey);
        Assert.Equal(["entry-front"], hub.Legs.Where(l => l.Kind == RungLegKind.Run).Select(l => l.Label));
    }

    /// <summary>M6 7 Z: a via is a bend, never a wall: its walk's own failure is skipped by name and the run goes on to the front from where the body stands.</summary>
    [Fact]
    public void A_via_the_walk_cannot_reach_is_skipped_by_name_and_the_run_goes_on()
    {
        var t = new RungRig(level: 10);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        t.Travel.Refuse = r => r.Destination.Name == "via-1" ? "travel:run:no-path: no-path" : null;
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(" ok=yes fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("via_skipped: via-1: the walk failed by name", StringComparison.Ordinal));
        Assert.Contains(t.Lines("rung_change_leg: "), l => l.Contains("edge=via-1 ", StringComparison.Ordinal) && l.Contains("ok=yes via=skipped", StringComparison.Ordinal));
        Assert.Equal(1, t.Travel.Requests.Count(r => r.Destination.Name == "via-2"));
        Assert.Equal(1, t.Travel.Requests.Count(r => r.Destination.Name == "entry-front"));
    }

    /// <summary>M6 7 Z: a death on a via is the leg's named death, not a skip.</summary>
    [Fact]
    public void A_death_on_a_via_is_named_and_never_skipped()
    {
        var t = new RungRig(level: 10);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        t.Travel.TeleportOn = r => r.Destination.Name == "via-2" ? (0x860201ADu, new Vector3(60f, -40f, 0f)) : null;
        t.Travel.DiesOn = r => r.Destination.Name == "via-2" ? "You have been killed by Tusker Slave!" : null;
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=died:run:respawned=0x860201AD ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("via_skipped", StringComparison.Ordinal));
        Assert.Equal(0, t.Travel.Requests.Count(r => r.Destination.Name == "entry-front"));
    }

    /// <summary>M6 7 Z: a body already nearer the front than a via (a fight carried it on, or a corpse trip's way back starts past it) does not turn back to it.</summary>
    [Fact]
    public void A_via_behind_the_body_is_not_walked()
    {
        var t = new RungRig(level: 10);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        t.Travel.Refuse = r =>
        {
            if (r.Destination.Name != "via-1")
            {
                return null;
            }

            t.Put(BurrowEntry.FrontCell, BurrowEntry.Front + new Vector3(0f, -10f, 0f));
            return "travel:run:no-path: no-path";
        };
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Equal(0, t.Travel.Requests.Count(r => r.Destination.Name == "via-2"));
        Assert.Contains(t.Lines("rung_change_leg: "), l => l.Contains("edge=via-2 ", StringComparison.Ordinal) && l.Contains("ok=yes via=behind", StringComparison.Ordinal));
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("via_skipped: via-2: the body stands", StringComparison.Ordinal));
    }

    /// <summary>
    /// M6 7 H (the live round 1: the station after a death's respawn started 0.1 s after the station before it, <c>rung-change:not-present ... (travel TeleportArrived)</c>): a rung change that starts while a teleport settles waits for the arrival, one line before and one after, and plans from where the body then stands.
    /// </summary>
    [Fact]
    public void A_rung_change_started_mid_teleport_waits_for_the_arrival_then_plans_M6_7_H()
    {
        var t = new RungRig(level: 15);
        t.R.H.State.Self.Travel = SelfTravel.TeleportArrived;
        var run = new RungChangeStation(RungDirection.Home).Start(t.Context(Burrow));
        var view = t.SessionView();
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 12 && status == ScenarioStatus.Running; i++)
        {
            t.R.Tick(0.25);
            status = run.Step(view, t.Now);
        }

        Assert.Equal(ScenarioStatus.Running, status); // 3 s in: still waiting, no failure
        Assert.Single(t.Lines("the player is not present yet (travel TeleportArrived): waiting for the arrival, at most 45 s"));
        t.R.H.State.Self.Travel = SelfTravel.Present;
        for (var i = 0; i < 40 && status == ScenarioStatus.Running; i++)
        {
            t.R.Tick(0.25);
            status = run.Step(view, t.Now);
        }

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains(" ok=yes fail=none direction=home", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(t.Lines("the travel is Present"));
    }

    /// <summary>M6 7 H: the wait is bounded: a player who never arrives fails by name at the bound, the state in the text.</summary>
    [Fact]
    public void A_rung_change_whose_player_never_arrives_fails_by_name_at_the_bound_M6_7_H()
    {
        var t = new RungRig(level: 15);
        t.R.H.State.Self.Travel = SelfTravel.TeleportAnnounced;
        var started = t.Now;
        var (run, status) = t.RunStation(new RungChangeStation(RungDirection.Home), t.Context(Burrow));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=rung-change:not-present ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.InRange(t.Now - started, 44.0, 47.0);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("not present in the world 45 s into the station (travel TeleportAnnounced)", StringComparison.Ordinal));
    }

    /// <summary>
    /// M6 7 N (the live round 1c: the run touched the Burrow's portal, the body stood in the ground, the leg read <c>fail=run:entry-front</c>): a run the server ends by a teleport into the entry's destination is the entry taken on touch: the station ends ok, the run's, the gate's and
    /// the enter's leg lines say <c>taken=touch</c>, no Use is sent, the arrival is checked as a Use's and the hop is counted.
    /// </summary>
    [Fact]
    public void A_run_the_entry_portal_ends_by_a_teleport_into_the_ground_is_the_entry_taken_on_touch()
    {
        var t = new RungRig(level: 10);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        t.Travel.TeleportOn = r => r.Destination.Name == "entry-front" ? (0x01D40100u, new Vector3(10, 10, 0)) : null;
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.Contains(" hops=3 ", run.VerdictFields(), StringComparison.Ordinal); // the entry is a portal used, as a Use's hop
        Assert.Contains(" ok=yes fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0x01D4u, t.Landblock);
        var legs = t.Lines("rung_change_leg: ");
        foreach (var kind in new[] { "kind=run edge=entry-front", "kind=gate edge=entry:tusker-burrow", "kind=enter edge=entry:tusker-burrow" })
        {
            var line = Assert.Single(legs, l => l.Contains(kind, StringComparison.Ordinal));
            Assert.Contains(" ok=yes", line, StringComparison.Ordinal);
            Assert.EndsWith("taken=touch", line, StringComparison.Ordinal);
        }

        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("life check after the move on the run leg to entry-front: no death evidence", StringComparison.Ordinal)); // the death check ran first
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("hop:entry:tusker-burrow: check after: present in cell 0x01D40100", StringComparison.Ordinal));
        Assert.DoesNotContain(t.Events, e => e == "use:" + BurrowEntry.Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>M6 7 N: an entry that took the body before the planned tie leaves the trip untied, by name (<c>tied=no</c>), as a tie that does not link; no cast at a portal the body has left.</summary>
    [Fact]
    public void An_entry_taken_on_touch_before_the_tie_goes_on_untied_with_no_cast()
    {
        var t = new RungRig(level: 20, spells: [47, 48]);
        t.Portals(Routes, "new-colier");
        t.Entry(Temple2040Entry, Temple2040);
        t.Travel.TeleportOn = r => r.Destination.Name == "entry-front" ? (0x5D4302F9u, new Vector3(10, 10, 0)) : null;
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Temple2040));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.Contains(" tied=no ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.Lines("rung_change_leg: "), l => l.Contains("kind=tie ", StringComparison.Ordinal) && l.Contains(" ok=yes tied=no taken=touch", StringComparison.Ordinal));
        Assert.DoesNotContain(t.Events, e => e.StartsWith("cast:47", StringComparison.Ordinal));
    }

    /// <summary>M6 7 N: only the ground the run was running to is the touch: a teleport into any other landblock keeps <c>travel:run:moved</c> and the run's failure, with no <c>taken=touch</c>.</summary>
    [Fact]
    public void A_run_the_server_moves_anywhere_but_the_ground_is_no_touch()
    {
        var t = new RungRig(level: 10);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        t.Travel.TeleportOn = r => r.Destination.Name == "entry-front" ? (0x0007015Fu, new Vector3(100, 100, 0)) : null;
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=run:entry-front ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("taken=touch", StringComparison.Ordinal));
        Assert.Contains(t.Lines("rung_change_leg: "), l => l.Contains("kind=run ", StringComparison.Ordinal) && l.Contains("ok=no", StringComparison.Ordinal));
    }

    /// <summary>M6 7 N: a death stays a death, even with the respawn in the landblock the run was running to: the death check comes first.</summary>
    [Fact]
    public void A_death_on_the_run_that_respawns_in_the_ground_stays_a_death()
    {
        var t = new RungRig(level: 10);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        t.Travel.TeleportOn = r => r.Destination.Name == "entry-front" ? (0x01D40100u, new Vector3(10, 10, 0)) : null;
        t.Travel.DiesOn = r => r.Destination.Name == "entry-front" ? "You have been killed by Tusker Slave!" : null;
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=died:run:respawned=0x01D40100 ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("taken=touch", StringComparison.Ordinal));
    }

    /// <summary>M6 7 N: the way out of a ground is a hop (<c>exit:&lt;ground&gt;</c>): its walk to the exit's front has F5's rule: a teleport into the exit's destination is the hop taken on touch, and the way home goes on.</summary>
    [Fact]
    public void The_exit_s_walk_has_the_touch_rule_of_a_hop()
    {
        var t = new RungRig(level: 15, at: (0x01D40105, new Vector3(50, 50, 0)));
        t.Portals(Routes, "refuge-to-network", "network-to-holtburg");
        t.Travel.TeleportOn = r => r.Destination.Name == "exit:tusker-burrow-front" ? (0xF9810038u, new Vector3(170, 170, 12)) : null;
        var (run, status) = t.RunStation(new RungChangeStation(RungDirection.Home), t.Context(Burrow));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.Contains(t.Lines("rung_change_leg: "), l => l.Contains("edge=exit:tusker-burrow ", StringComparison.Ordinal) && l.Contains(" ok=yes taken=yes", StringComparison.Ordinal));
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("hop exit:tusker-burrow: ", StringComparison.Ordinal) && l.Contains("taken=touch", StringComparison.Ordinal));
        Assert.Equal(0xA9B4u, t.Landblock);
    }

    /// <summary>M6 4 F6: a run the server ends by a teleport with no death evidence keeps the walk's own failure (<c>travel:run:moved</c>, the run's <c>fail=run:entry-front</c>).</summary>
    [Fact]
    public void A_run_the_server_ends_by_a_teleport_without_a_death_keeps_the_moved_failure()
    {
        var t = new RungRig(level: 10);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        t.Travel.TeleportOn = r => r.Destination.Name == "entry-front" ? (0xA9B40019u, new Vector3(84, 7.7f, 94)) : null;
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=run:entry-front ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("travel:run:moved", StringComparison.Ordinal));
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("life check after the move on the run leg to entry-front: no death evidence", StringComparison.Ordinal));
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("died on", StringComparison.Ordinal));
    }

    /// <summary>M6 4 F6: a death on a hop's walk to the front is <c>died:hop:respawned=&lt;cell&gt;</c> (not F5's moved-elsewhere), and the held gem never replaces it (a death is no failed hop).</summary>
    [Fact]
    public void A_death_on_a_hop_walk_is_named_died_hop_and_the_gem_does_not_replace_it()
    {
        var t = new RungRig(level: 10, gem: true);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        t.Travel.TeleportOn = r => r.Destination.Name == "network-to-refuge-front" ? (0x860201ADu, new Vector3(60f, -40f, 0f)) : null;
        t.Travel.DiesOn = r => r.Destination.Name == "network-to-refuge-front" ? "You have been killed by a Drudge!" : null;
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=died:hop:respawned=0x860201AD ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(t.Events, e => e == "use:" + RungRig.GemGuid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("taken=touch", StringComparison.Ordinal));
    }

    /// <summary>
    /// M6 4 F6 (M5 D88: always fight back an attacker): a creature heard attacking the body during the run, standing in reach, stops the walk; the leg fights it back with a War Magic bolt (cast at it, its death checked), goes back to peace and plans
    /// the walk again, which arrives; the leg's line says <c>fought_back=1 kills=1</c>.
    /// </summary>
    [Fact]
    public void An_attacker_on_the_run_stops_the_walk_is_fought_back_and_the_walk_is_planned_again()
    {
        const uint tusker = 0x80001944;
        var bolts = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id).ToArray(); // the book's War Magic projectiles (the Flame Bolt line)
        var t = new RungRig(level: 10, spells: bolts);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        var first = true;
        t.Travel.WalkingOn = r =>
        {
            if (r.Destination.Name != "entry-front" || !first)
            {
                return false;
            }

            first = false;
            t.Attacker(tusker, "Tusker Slave", 3f);
            return true;
        };
        t.CastAt = (spell, target) =>
        {
            if (target == tusker)
            {
                t.Kill(tusker);
            }
        };
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(" ok=yes fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("the walk is stopped: 'Tusker Slave' 0x80001944 attacked the player", StringComparison.Ordinal));
        Assert.Contains(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal) && e.EndsWith("@80001944", StringComparison.Ordinal));
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("fight-back: 'Tusker Slave' 0x80001944 is dead", StringComparison.Ordinal));
        Assert.Equal(2, t.Travel.Requests.Count(r => r.Destination.Name == "entry-front")); // planned again after the fight-back
        Assert.Contains(t.Lines("rung_change_leg: "), l => l.Contains("kind=run ", StringComparison.Ordinal) && l.Contains("ok=yes fought_back=1 kills=1", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------ M6 8 G: a hop's walks fight back (m6-hop-walk-fight-back)

    private const uint Reaver = 0x80001950;

    private static RungRig HopRig()
    {
        var t = new RungRig(level: 10, spells: [.. ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id)]);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        t.CastAt = (_, target) => t.Kill(target);
        return t;
    }

    /// <summary>The first request <paramref name="match"/> names keeps running with <paramref name="on"/> done on it (the attacker arrives); it never arrives, so the attacker must stop it.</summary>
    private static void FirstWalkAttacked(RungRig t, Func<TravelRequest, bool> match, Action? on = null)
    {
        var first = true;
        t.Travel.WalkingOn = r =>
        {
            if (!first || !match(r))
            {
                return false;
            }

            first = false;
            (on ?? (() => t.Attacker(Reaver, "Jungle Reaver", 3f)))();
            return true;
        };
    }

    /// <summary>M6 8 G (M5 D88): attacked on the walk to a portal's front she stops, fights it back, plans the same walk again and uses the portal: the hop is not failed.</summary>
    [Fact]
    public void An_attacker_on_a_hops_walk_to_the_front_is_fought_back_and_the_hop_goes_on()
    {
        var t = HopRig();
        FirstWalkAttacked(t, r => r.Destination.Name == "holtburg-to-network-front");
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(" fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.Lines("the walk is stopped: 'Jungle Reaver' 0x80001950 attacked the player"), l => l.Contains("to the front of", StringComparison.Ordinal));
        Assert.Single(t.Lines("fight-back: 'Jungle Reaver' 0x80001950 is dead"));
        Assert.Single(t.Lines("fight-back 1 on the hop leg ended; the walk to holtburg-to-network-front is planned again from 0x"));
        Assert.Equal(2, t.Travel.Requests.Count(r => r.Destination.Name == "holtburg-to-network-front"));
        Assert.Contains("use:" + Routes.Edge("holtburg-to-network").Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), t.Events);
    }

    /// <summary>M6 8 G: attacked in the last steps to the portal (the seek from the front) she fights there and the seek runs again; the Use follows.</summary>
    [Fact]
    public void An_attacker_at_a_portals_front_stops_the_seek_and_the_hop_goes_on()
    {
        var t = HopRig();
        FirstWalkAttacked(t, r => r.Purpose == TravelPurpose.Seek && r.Destination.Name == "holtburg-to-network");
        var (_, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Single(t.Lines("hop holtburg-to-network: the seek is stopped: 'Jungle Reaver'"));
        Assert.Single(t.Lines("hop holtburg-to-network: fight-back 1 at the portal ended; the seek runs again from 0x"));
        Assert.Equal(2, t.Travel.Requests.Count(r => r.Purpose == TravelPurpose.Seek && r.Destination.Name == "holtburg-to-network"));
        Assert.Single(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal) && e.Contains('@', StringComparison.Ordinal));
    }

    /// <summary>M6 8 G: the stop's reason does not outlive the walk: attacked with the front point reached, the next walk has nowhere to go and the hop goes on after one fight-back (not eight and a retreat).</summary>
    [Fact]
    public void A_fight_back_that_ends_on_the_front_point_does_not_read_the_old_stop_again()
    {
        var t = HopRig();
        var first = true;
        t.Travel.WalkingOn = r =>
        {
            if (!first || r.Destination.Name != "holtburg-to-network-front")
            {
                return false;
            }

            first = false;
            t.Put(r.Destination.Location.Position.CellId, r.Destination.Location.Position.Local);
            t.Attacker(Reaver, "Jungle Reaver", 3f);
            return true;
        };
        var (_, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Single(t.Lines("fight-back 1 on the hop leg ended"));
        Assert.Empty(t.Lines("fight-back 2 on the hop leg"));
        Assert.Contains(t.Lines("the player already stands within"), l => l.Contains("rung-change:", StringComparison.Ordinal));
    }

    /// <summary>M6 8 G: a walk the walker refuses after the fight is planned once more, then ends by the walk's own name (here the facing front's recovery runs out too).</summary>
    [Fact]
    public void A_walk_that_cannot_resume_after_the_fight_is_planned_once_more_and_then_ends_by_name()
    {
        var t = HopRig();
        var walks = 0;
        FirstWalkAttacked(t, r => r.Destination.Name == "holtburg-to-network-front");
        t.Travel.Refuse = r => r.Destination.Name == "holtburg-to-network-front" && ++walks > 1 ? "no path: the fight carried her off the way" : null;
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(t.Lines("recovery=replan-after-fight: the walk to holtburg-to-network-front could not resume after fight-back 1 (travel"), l => l.Contains("no path: the fight carried her", StringComparison.Ordinal));
        Assert.Contains("holtburg-to-network:front-wall", run.VerdictFields(), StringComparison.Ordinal); // the walk's own name, the front-wall recovery spent
    }

    /// <summary>M6 8 G: a fight-back that gives up on a hop's walk (the health under the line) retreats by the leg's rule and ends the run by the retreat's name: no other route is planned out for a body that left, and no front-wall recovery walks on.</summary>
    [Fact]
    public void A_retreat_on_a_hops_walk_ends_the_run_by_its_name()
    {
        var (t, ground) = RetreatRig(recall: true, line: 40);
        FirstWalkAttacked(t, r => r.Destination.Name == "holtburg-to-network-front", () =>
        {
            t.Attacker(Reaver, "Jungle Reaver", 3f);
            t.Caster.SetHealth(10);
        });
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(ground));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=retreat:hop:recall ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(t.Lines("retreat on hop: why=health way=recall from=0x"));
        Assert.Empty(t.Lines("route_fallback:"));
        Assert.Empty(t.Lines("recovery=front-wall"));
    }

    /// <summary>M6 8 G: the file's way in to a portal's room and the walk out of the drop's room are guarded walks too.</summary>
    /// <param name="label">The walk that is attacked.</param>
    [Theory]
    [InlineData("walk-in-front")]
    [InlineData("walk-out-1")]
    public void An_attacker_on_a_hops_way_in_or_way_out_is_fought_back_and_the_walk_goes_on(string label)
    {
        var t = HopRig();
        var edge = Routes.Edge("holtburg-to-network");
        var portal = RungPortal.Of(edge, Routes) with
        {
            WalkIn = [new TravelVia(0xA9B40019, new Vector3(110, 30, 94), "the way in")],
            WalkOut = [new TravelVia(edge.DestCell, edge.DestOrigin + new Vector3(20, 0, 0), "the way out")],
        };
        FirstWalkAttacked(t, r => r.Destination.Name == label);
        var (leg, status) = t.RunHop(new PortalHopLeg(t.Context(Burrow), portal, "the test's hop"));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Null(leg.FailureName);
        Assert.Single(t.Lines("fight-back: 'Jungle Reaver' 0x80001950 is dead"));
        Assert.Equal(2, t.Travel.Requests.Count(r => r.Destination.Name == label));
        Assert.Equal(1, t.Travel.Requests.Count(r => r.Destination.Name == (label == "walk-in-front" ? "walk-out-1" : "walk-in-front")));
    }

    // ------------------------------------------------------------------------------------------------ M6 14 run past (D137): an attacker stops the run only when she cannot outpace it

    private const uint Runner = 0x80001960;

    /// <summary>
    /// A run past rig: level 10 to the Burrow by the Refuge, the Flame Bolt line, a bolt kills its target; the creatures' motions run <paramref name="anim"/> m/s at rate 1 (a stand-in for the dats' runs, <see cref="CreaturePaces"/>);
    /// her Run is not trained (rate 1: 4 m/s). The run to the entry's front runs <paramref name="seconds"/> s at 4 m/s.
    /// </summary>
    private static (RungRig Rig, JourneyContext Context) RunPastRig(double anim, double seconds)
    {
        var t = HopRig();
        var context = t.Context(Burrow, new Lifestone.Client.Session.Vitals.CreaturePaces(_ => anim));
        var first = true;
        t.Travel.RunsFor = r =>
        {
            if (r.Destination.Name != "entry-front" || !first)
            {
                return null;
            }

            first = false;
            return seconds;
        };
        return (t, context);
    }

    /// <summary>
    /// D137: an Armored Tusker (its facts: Run 200, its motion 0x0900000C, scale 1.2) whose motion runs 1 m/s here (2.9 m/s in all) hits her as the run begins; she runs at 4 m/s, so it is left behind, named with both speeds, and the run arrives
    /// with no fight-back and no bolt.
    /// </summary>
    [Fact]
    public void A_slower_melee_attacker_on_the_run_is_left_behind_with_no_fight_back()
    {
        var (t, context) = RunPastRig(anim: 1.0, seconds: 12);
        t.Travel.OnRun = (r, ran, _) =>
        {
            if (ran == 0)
            {
                t.Attacker(Runner, "Armored Tusker", 3f, wcid: 11540);
            }
        };
        var (run, status) = t.RunStation(new RungChangeStation(), context);
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(" ok=yes fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        var left = Assert.Single(t.Lines("run past: outrun: 'Armored Tusker' 0x80001960"));
        Assert.True(left.Contains("is left behind: its 2.9 m/s (Run 200: rate 2.375 x its motion 0x0900000C's run 1 m/s x scale 1.2) against her 4.0 m/s (her Run 0: rate 1 x 4 m/s)", StringComparison.Ordinal), left);
        Assert.Empty(t.Lines("fight-back 1 on the run leg"));
        Assert.DoesNotContain(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal) && e.EndsWith("@80001960", StringComparison.Ordinal));
        Assert.Single(t.Travel.Requests, r => r.Destination.Name == "entry-front"); // never planned again
        Assert.Contains(t.Lines("rung_change_leg: "), l => l.Contains("kind=run ", StringComparison.Ordinal) && l.Contains(" ran_past=1", StringComparison.Ordinal));
    }

    /// <summary>
    /// D137, M6 14 caught on the run (the owner's ruling, 2026-11-15: asked whether she was fighting or running; live ladder25 04:08:00 UTC: tuskers of 13.8 to 17.0 m/s at 2 to 4 m were "measured" against her 7.9 m/s run and bit on): the same tusker with a
    /// motion of 3 m/s (8.6 m/s in all) hits her from 3 m as the run begins and runs beside her at 2 m: it keeps pace and stands in melee, so it has caught her and she fights it at once, before any second hit, never measured (its bolt
    /// kills it); the run is planned again and arrives.
    /// </summary>
    [Fact]
    public void A_fast_attacker_in_melee_reach_has_caught_her_and_is_fought_at_once()
    {
        var (t, context) = RunPastRig(anim: 3.0, seconds: 20);
        t.Travel.OnRun = (r, ran, arrived) =>
        {
            var here = t.R.H.State.Self.Position!.Value;
            if (ran == 0)
            {
                t.Attacker(Runner, "Armored Tusker", 3f, wcid: 11540);
                return;
            }

            if (t.R.H.State.Objects.TryGet(Runner, out _))
            {
                t.MoveObject(Runner, here.CellId, here.Origin + new Vector3(2, 0, 0)); // it runs beside her
            }
        };
        var (run, status) = t.RunStation(new RungChangeStation(), context);
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(" fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Empty(t.Lines("run past: measuring:")); // a known pace in melee is never measured
        var caught = Assert.Single(t.Lines("run past: caught: 'Armored Tusker' 0x80001960"));
        Assert.Contains("keeps pace with her and stands within the 5 m of melee: it has caught her and she cannot outrun it, so she fights it at once (its 8.6 m/s (Run 200", caught, StringComparison.Ordinal);
        Assert.Contains("against her 4.0 m/s (her Run 0: rate 1 x 4 m/s)", caught, StringComparison.Ordinal);
        Assert.Contains(t.Lines("the walk is stopped: 'Armored Tusker' 0x80001960 attacked the player"), l => l.Contains("fight back (caught; M6 14 run past, D137)", StringComparison.Ordinal));
        Assert.Empty(t.Lines("run past: keeps-pace:")); // fought before it hit again
        Assert.Single(t.Lines("fight-back: 'Armored Tusker' 0x80001960 is dead"));
        Assert.Equal(2, t.Travel.Requests.Count(r => r.Destination.Name == "entry-front"));
    }

    /// <summary>
    /// M6 14 caught on the run (rule 2; live ladder25 04:07:39 to 04:09:43 UTC: each fight-back ended "no attacker heard within 10 s stands within 40 m" with six tuskers at 2 to 3 m, she sheathed, ran, and the run past stopped her again at
    /// once, walk_s=0.0): an Armored Tusker (8.6 m/s) at 2 m has caught her; a Silver Tusker (9.1 m/s) stands 12 m off, not attacking; a Reedshark Ravager (2.8 m/s) at 3 m is slower than her. The fight-back takes the first; while its bolt
    /// flies the Silver Tusker closes to 2 m and bites. With the first dead the set is spent: the run past judges again there, the Silver Tusker has caught her and the slower Ravager is on her while she stands (on-her), and the same fight-back goes on through both, no peace and no new walk; with them dead the
    /// run past fights none and none is on her, and only then does she sheathe and the walk go on (once).
    /// </summary>
    [Fact]
    public void When_the_attackers_she_stopped_for_are_spent_the_run_past_judges_again_and_the_same_fight_back_goes_on()
    {
        var (t, _) = RunPastRig(anim: 3.0, seconds: 20);
        var context = t.Context(Burrow, new Lifestone.Client.Session.Vitals.CreaturePaces(table => table == 0x0900000C ? 3.0 : 0.5)); // the tuskers' motion 3 m/s, the Ravager's 0.5
        const uint second = Runner + 1, ravager = Runner + 2;
        t.Travel.OnRun = (r, ran, arrived) =>
        {
            if (ran == 0)
            {
                t.Attacker(Runner, "Armored Tusker", 2f, wcid: 11540);
                t.Bystander(second, "Silver Tusker", 12f, wcid: 7184);
                t.Attacker(ravager, "Reedshark Ravager", 3f, wcid: 22746); // slower, but on her while she stands to fight: fought in the same fight-back (on-her)
            }
        };
        t.CastAt = (_, target) =>
        {
            if (target == Runner)
            {
                var here = t.R.H.State.Self.Position!.Value;
                t.MoveObject(second, here.CellId, here.Origin + new Vector3(2, 0, 0)); // the Silver Tusker closes while the first dies, and bites
                t.Hit("Silver Tusker", 4, 0x4);
            }

            t.Kill(target);
        };
        var (run, status) = t.RunStation(new RungChangeStation(), context);
        var trace = string.Join(" || ", t.R.H.Log.Lines.Where(l => l.Contains("fight-back", StringComparison.Ordinal) || l.Contains("run past", StringComparison.Ordinal) || l.Contains("walk", StringComparison.Ordinal)).Take(40));
        Assert.True(status == ScenarioStatus.Succeeded, trace);
        Assert.Contains(" fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.Lines("the walk is stopped: 'Armored Tusker' 0x80001960 attacked the player"), l => l.Contains("fight back (caught; M6 14 run past, D137)", StringComparison.Ordinal));
        Assert.Single(t.Lines("fight-back 1 on the run leg: the walk is stopped;"));
        Assert.Empty(t.Lines("fight-back 2 on the run leg"));
        var again = Assert.Single(t.Lines("fight-back 1 on the run leg: the attackers it was stopped for are spent, but the run past judges again and must fight on: 'Silver Tusker' 0x80001961"));
        Assert.Contains("fight back (caught; M6 14 run past, D137); the fight-back goes on, no peace and no new walk (1 cast(s), 1 kill(s) so far", again, StringComparison.Ordinal);
        Assert.Contains(t.Lines("run past: caught: 'Silver Tusker' 0x80001961 at 2.0 m"), l => l.Contains("its 9.1 m/s (Run 250", StringComparison.Ordinal));
        Assert.Single(t.Lines("fight-back: 'Armored Tusker' 0x80001960 is dead"));
        Assert.Single(t.Lines("fight-back: 'Silver Tusker' 0x80001961 is dead"));
        var done = Assert.Single(t.Lines("fight-back 1 on the run leg: done: "));
        Assert.Contains("no creature heard attacking her within 10 s stands alive within 40 m (the last attack heard on her: '", done, StringComparison.Ordinal);
        Assert.Contains("casts=3 kills=3 ", done, StringComparison.Ordinal);
        Assert.Contains(t.Lines("run past: on-her: 'Reedshark Ravager' 0x80001962 at 3.0 m"), l => l.Contains("attacks her while she stands to fight: it is on her, so she fights it before she runs on, whatever its pace", StringComparison.Ordinal));
        Assert.Single(t.Lines("fight-back: 'Reedshark Ravager' 0x80001962 is dead"));

        // no peace between the two kills: the only mode change after the first bolt is the peace after the second
        var events = t.Events;
        var (first, next) = (events.FindIndex(e => e.EndsWith("@80001960", StringComparison.Ordinal)), events.FindIndex(e => e.EndsWith("@80001961", StringComparison.Ordinal)));
        Assert.True(first >= 0 && first < next, string.Join(" ", events));
        Assert.DoesNotContain(events.Skip(first).Take(next - first), e => e.StartsWith("mode:", StringComparison.Ordinal));
        var ravagerBolt = events.FindIndex(e => e.EndsWith("@80001962", StringComparison.Ordinal));
        Assert.True(ravagerBolt > next, string.Join(" ", events)); // the Ravager is bolted in the same fight-back, after the Silver Tusker
        Assert.DoesNotContain(events.Skip(first).Take(ravagerBolt - first), e => e.StartsWith("mode:", StringComparison.Ordinal)); // no peace through the three
        Assert.Equal(2, t.Travel.Requests.Count(r => r.Destination.Name == "entry-front")); // one walk planned again, after the one fight-back
    }

    /// <summary>
    /// M6 14 caught on the run: the walk dance rig (the dance covers her, her stamina low, so a top-off is due after every fight-back) on a run to the entry's front of <paramref name="seconds"/> s at 4 m/s, a bolt killing its target,
    /// the creatures' paces <paramref name="paces"/> (none: not known).
    /// </summary>
    private static (RungRig Rig, JourneyContext Context) DanceRunRig(double seconds, Lifestone.Client.Session.Vitals.CreaturePaces? paces = null)
    {
        var (t, ground) = DanceRig();
        var context = t.Context(ground, paces);
        var first = true;
        t.Travel.RunsFor = r =>
        {
            if (r.Destination.Name != "entry-front" || !first)
            {
                return null;
            }

            first = false;
            return seconds;
        };
        t.CastAt = (_, target) => t.Kill(target);
        return (t, context);
    }

    /// <summary>
    /// M6 14 caught on the run (rule 3 and the lull; live ladder25 04:10:23 to 04:10:58 UTC: a Reedshark Ravager the run past read at 2.6 m was read at 18.9 m by the fight-back's pick from its stale report, left, and bit her every
    /// second through a "lull" and a top-off), with the hovering Zefir's reach rule (live ladder51, 2026-11-17): her bolts reach 15 m here (the test's spell data: base 15, no metres a point), so a Reedshark Ravager drawn and reported
    /// 18.9 m away that throws at her as the run reaches the door is beyond them, and her book holds no Harm Other: she runs past it at once (no wait), the end line naming it, left. It throws again from 18.9 m and stays left (an attack
    /// from beyond her reach takes nothing back). Then it closes, drawn 2.6 m beside her while its report stays at 18.9 m, and bites: it is no longer left, the lull test finds her under attack (no top-off), and the next fight-back reads
    /// it where it is drawn, 2.6 m, within the bolt's range, and bolts it at once.
    /// </summary>
    [Fact]
    public void A_creature_left_beyond_her_reach_that_bites_her_from_within_it_breaks_the_lull_and_is_bolted_where_it_is_drawn()
    {
        var (t, context) = DanceRunRig(seconds: 2);
        foreach (var id in t.Caster.Table.Where(kv => kv.Value.School == DatReaderWriter.Enums.MagicSchool.WarMagic).Select(kv => kv.Key).ToList())
        {
            t.Caster.Table[id] = t.Caster.Table[id] with { BaseRangeConstant = 15f, BaseRangeMod = 0f };
        }

        const uint ravager = 0x80001970;
        t.Travel.OnRun = (r, ran, arrives) =>
        {
            if (arrives)
            {
                t.Attacker(ravager, "Reedshark Ravager", 18.9f, wcid: 22746);
                t.DrawAt(ravager, 18.9f);
            }
        };
        var (threw, bit) = (false, false);
        var threwAt = double.NaN;
        var (run, status) = t.RunStation(new RungChangeStation(), context, maxSeconds: 1200, each: () =>
        {
            if (!threw && t.Lines("fight-back: 'Reedshark Ravager' 0x80001970: it stands 18.9 m off").Count > 0)
            {
                (threw, threwAt) = (true, t.Now);
                t.Hit("Reedshark Ravager", 3, 0x4); // it throws again from beyond her reach
            }

            if (threw && !bit && t.Now - threwAt >= 0.5 && t.Lines("fight-back 1 on the run leg: done: ").Count > 0)
            {
                bit = true;
                t.DrawAt(ravager, 2.6f); // drawn beside her; the server's last report stays 18.9 m away
                t.Hit("Reedshark Ravager", 5, 0x4);
            }
        });
        var lines = t.R.H.Log.Lines.ToList();
        var trace = string.Join(" || ", lines.Where(l => l.Contains("fight-back", StringComparison.Ordinal) || l.Contains("run past", StringComparison.Ordinal) || l.Contains("walk dance", StringComparison.Ordinal)).Take(30));
        Assert.True(status == ScenarioStatus.Succeeded, trace);
        Assert.Contains(" fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.Lines("run past: at the door of the walk to entry-front: the walk stops: 'Reedshark Ravager' 0x80001970"), l => l.Contains("fight back (at-goal; M6 14 run past, D137)", StringComparison.Ordinal));
        Assert.True(t.Lines("fight-back: target 'Reedshark Ravager' 0x80001970 at 18.9 m").Count == 1, trace);
        var passed = Assert.Single(t.Lines("fight-back: 'Reedshark Ravager' 0x80001970: it stands 18.9 m off (planar, as ACE measures), beyond "));
        Assert.Contains("'s range 15 m, and no life spell answers it (", passed, StringComparison.Ordinal);
        Assert.Contains(": she runs past it and the walk goes on; it is fought again only if it attacks her from within 15 m (M6 14 run past, D137;", passed, StringComparison.Ordinal);
        var done = Assert.Single(t.Lines("fight-back 1 on the run leg: done: "));
        Assert.Contains("done: none of the 1 creature(s) still on her is to be fought now: 'Reedshark Ravager' 0x80001970 at 18.9 m (left: it stands 18.9 m off (planar, as ACE measures), beyond ", done, StringComparison.Ordinal);
        Assert.Contains("not heard attacking from within 15 m since it was left", done, StringComparison.Ordinal);
        Assert.DoesNotContain("no creature heard", done, StringComparison.Ordinal);
        var released = Assert.Single(t.Lines("fight-back: 'Reedshark Ravager' 0x80001970 was left ")); // the throw from 18.9 m took nothing back; the bite from 2.6 m did
        Assert.Contains("now 2.6 m off within her reach 15 m): it is no longer left; an attacker on her is fought", released, StringComparison.Ordinal);
        int At(string needle) => lines.FindIndex(l => l.Contains(needle, StringComparison.Ordinal));
        var underAttack = At("walk dance: due to 426, but she is under attack: 'Reedshark Ravager' 0x80001970 attacked her ");
        Assert.True(underAttack >= 0, trace);
        Assert.Contains("stands 2.6 m away where it is drawn: no lull, no dance", lines[underAttack], StringComparison.Ordinal);
        var target = At("fight-back: target 'Reedshark Ravager' 0x80001970 at 2.6 m");
        var dead = At("fight-back: 'Reedshark Ravager' 0x80001970 is dead");
        Assert.True(At("fight-back 1 on the run leg: done: ") < underAttack && underAttack < target && target < dead, trace);
        var lull = At("walk dance: a lull on the run leg");
        Assert.True(lull < 0 || lull > dead, trace); // no lull while it bit her: the top-off waits until it is dead
        Assert.Equal(1, t.Events.Count(e => e.StartsWith("cast:", StringComparison.Ordinal) && e.EndsWith("@80001970", StringComparison.Ordinal))); // bolted once it was drawn in reach, never while left
        Assert.Contains("use:" + BurrowEntry.Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), t.Events);
    }

    /// <summary>
    /// M6 14 caught on the run (the lull; the owner's ruling, 2026-11-15: she is not in a lull while she is under attack): an Armored Tusker (8.6 m/s) at 2 m has caught her and is fought; a Reedshark Ravager at 8 m, beyond melee reach (so it hits her from range and is passed: a slower one within it is fought while she stands, on-her), is one the run past runs
    /// from, outside the fight-back's set. As the fight-back ends the Ravager bites: the lull test a moment later counts it though the fight-back skipped it, names it and how long ago it bit, and no top-off starts.
    /// </summary>
    [Fact]
    public void The_lull_test_counts_a_bite_a_moment_ago_from_a_creature_the_fight_back_skipped()
    {
        var (t, context) = DanceRunRig(seconds: 20, new Lifestone.Client.Session.Vitals.CreaturePaces(table => table == 0x0900000C ? 3.0 : 0.5));
        const uint ravager = Runner + 2;
        t.Travel.OnRun = (r, ran, arrived) =>
        {
            if (ran == 0)
            {
                t.Attacker(Runner, "Armored Tusker", 2f, wcid: 11540);
                t.Attacker(ravager, "Reedshark Ravager", 8f, wcid: 22746); // beyond melee reach (on-her fights a slower one within it while she stands)
            }
        };
        var bit = false;
        t.RunStation(new RungChangeStation(), context, maxSeconds: 1200, each: () =>
        {
            if (!bit && t.Lines("fight-back 1 on the run leg: done: ").Count > 0)
            {
                bit = true;
                t.Hit("Reedshark Ravager", 5, 0x4);
            }
        });
        var lines = t.R.H.Log.Lines.ToList();
        var trace = string.Join(" || ", lines.Where(l => l.Contains("fight-back", StringComparison.Ordinal) || l.Contains("run past", StringComparison.Ordinal) || l.Contains("walk dance", StringComparison.Ordinal)).Take(30));
        int At(string needle) => lines.FindIndex(l => l.Contains(needle, StringComparison.Ordinal));
        var done = At("fight-back 1 on the run leg: done: ");
        Assert.True(done >= 0, trace);
        Assert.Contains("'Reedshark Ravager' 0x80001962 at ", lines[done], StringComparison.Ordinal);
        Assert.Contains("(the run past: passing)", lines[done], StringComparison.Ordinal);
        var underAttack = At("walk dance: due to 426, but she is under attack: 'Reedshark Ravager' 0x80001962 attacked her ");
        Assert.True(underAttack > done, trace);
        var ago = System.Text.RegularExpressions.Regex.Match(lines[underAttack], @"attacked her (\d+\.\d) s ago and stands");
        Assert.True(ago.Success && double.Parse(ago.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) < 2, lines[underAttack]); // bitten a moment before the test, the line names how long ago
        Assert.Contains("no lull, no dance; the walk goes on and the dance waits for the next lull", lines[underAttack], StringComparison.Ordinal);
        var lull = At("walk dance: a lull on the run leg");
        Assert.True(lull < 0 || lull > underAttack, trace);
        Assert.True(At("walk_dance: revitalize") < 0 || At("walk_dance: revitalize") > underAttack, trace); // no top-off under attack
    }

    /// <summary>
    /// D137: a tusker she would outrun (2.9 m/s against her 4) stands in her way and she does not move for three seconds (the walker found no way round): the way is blocked, so she fights it back, then runs on.
    /// </summary>
    [Fact]
    public void A_slower_attacker_that_blocks_her_way_is_fought_back()
    {
        var t = HopRig();
        var context = t.Context(Burrow, new Lifestone.Client.Session.Vitals.CreaturePaces(_ => 1.0));
        FirstWalkAttacked(t, r => r.Destination.Name == "entry-front", () => t.Attacker(Runner, "Armored Tusker", 2f, wcid: 11540));
        var (_, status) = t.RunStation(new RungChangeStation(), context);
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Single(t.Lines("run past: outrun: 'Armored Tusker' 0x80001960"));
        Assert.Contains(t.Lines("run past: blocked: 'Armored Tusker' 0x80001960"), l => l.Contains("she has moved under 1 m in 3 s: the way is blocked, so she fights it", StringComparison.Ordinal));
        Assert.Single(t.Lines("fight-back: 'Armored Tusker' 0x80001960 is dead"));
        Assert.Equal(2, t.Travel.Requests.Count(r => r.Destination.Name == "entry-front"));
    }

    /// <summary>
    /// D137: an Iron Spined Chittick (its facts: Lightning Spines thrown from 40.8 m; its motion 0.5 m/s here, 1.9 m/s in all) throws at her from 20 m as she runs and again from farther: its measured damage over the seconds left in its
    /// range on her way is a few points against her health over the retreat line, so she runs past it, never stopping.
    /// </summary>
    [Fact]
    public void A_ranged_attacker_she_passes_is_paid_in_health_and_not_fought()
    {
        var (t, context) = RunPastRig(anim: 0.5, seconds: 14);
        t.Travel.OnRun = (r, ran, _) =>
        {
            if (ran == 0)
            {
                t.Attacker(Runner, "Iron Spined Chittick", 20f, wcid: 22506);
            }
            else if (Math.Abs(ran - 3) < 0.01)
            {
                t.Hit("Iron Spined Chittick", 4, 0x40);
            }
        };
        var (run, status) = t.RunStation(new RungChangeStation(), context);
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(" fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        var passing = Assert.Single(t.Lines("run past: passing: 'Iron Spined Chittick' 0x80001960"));
        Assert.Contains("hits her from range: she runs past it; ", passing, StringComparison.Ordinal);
        Assert.Contains("left in its 41 m range", passing, StringComparison.Ordinal);
        Assert.Empty(t.Lines("fight-back 1 on the run leg"));
        Assert.Single(t.Travel.Requests, r => r.Destination.Name == "entry-front");
    }

    /// <summary>
    /// D137: an attacker whose pace is not known reaches her as the run arrives at the entry's front: it is still on her at the door, so she fights it there before the portal (and the run is not planned again).
    /// </summary>
    [Fact]
    public void An_attacker_still_on_her_at_the_door_is_fought_there_before_the_portal()
    {
        var (t, context) = RunPastRig(anim: 1.0, seconds: 4);
        t.Travel.OnRun = (r, ran, arrives) =>
        {
            if (arrives)
            {
                t.Attacker(Runner, "Jungle Reaver", 2f);
            }
        };
        var (_, status) = t.RunStation(new RungChangeStation(), context);
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(t.Lines("run past: at the door of the walk to entry-front: the walk stops: 'Jungle Reaver' 0x80001960"), l => l.Contains("fight back (at-goal; M6 14 run past, D137)", StringComparison.Ordinal));
        Assert.Contains(t.Lines("run past: at-goal: 'Jungle Reaver' 0x80001960"), l => l.Contains("is still on her at the leg's goal: she fights it at the door before the portal", StringComparison.Ordinal));
        Assert.Single(t.Lines("fight-back: 'Jungle Reaver' 0x80001960 is dead"));
        Assert.Contains("use:" + BurrowEntry.Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), t.Events);
    }

    /// <summary>
    /// D137 (the safety lines stand): on the run, with an attacker on her and her mana at the way out's reserve (100 against Lifestone Recall's 50 and Heal Self VI's 70), she leaves by recall before any fight-back (<c>why=mana-line</c>).
    /// </summary>
    [Fact]
    public void On_the_run_past_the_mana_line_recalls_her_before_any_fight_back()
    {
        var (t, ground) = RetreatRig(recall: true, line: 0);
        var recall = Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell;
        t.Caster.Table[recall] = WorldActionRig.Spell(recall, DatReaderWriter.Enums.SpellType.Enchantment) with { Name = "Lifestone Recall", BaseMana = 50 };
        t.Caster.Table[1161] = WorldActionRig.Spell(1161, DatReaderWriter.Enums.SpellType.Boost) with { Name = "Heal Self VI", BaseMana = 70, School = DatReaderWriter.Enums.MagicSchool.LifeMagic };
        t.R.Server((p, seq) => AceM3Writers.MagicUpdateSpell(p, seq, 1161));
        t.Caster.ManaRegenPerSecond = 0;
        FirstRunAttacked(t, () =>
        {
            t.Attacker(0x80001944, "Tusker Slave", 3f);
            t.Caster.SetVitals(100, 275);
        });
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(ground));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=retreat:run:recall ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.Lines("retreat due on the walk: why=mana-line:"), l => l.Contains("mana 100 is at the way out's reserve line=120 (Lifestone Recall 50 + Heal Self 70) with attackers on her on the run past", StringComparison.Ordinal));
        Assert.Empty(t.Lines("fight-back 1 on the run leg"));
        Assert.DoesNotContain(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal) && e.Contains('@', StringComparison.Ordinal));
        Assert.Contains(t.Events, e => e == "cast:" + recall);
    }

    // ------------------------------------------------------------------------------------------------ M6 6 E: the run leg's retreat

    /// <summary>
    /// A rig for the retreat: level 10, the Flame Bolt line in the book (and Lifestone Recall when <paramref name="recall"/>, ACE answering it with the teleport to the ground's home lifestone unless <paramref name="recallLands"/> is false),
    /// the hops to the Refuge and the Burrow's entry; the ground's retreat line is <paramref name="line"/> (the profile's <c>fight.retreat_health_percent</c>).
    /// </summary>
    private static (RungRig Rig, HuntingGround Ground) RetreatRig(bool recall, int line, bool recallLands = true, IReadOnlyList<GroundTarget>? targets = null)
    {
        var bolts = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id);
        var t = new RungRig(level: 10, spells: [.. bolts, .. (recall ? new[] { (ushort)Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell } : [])]);
        var ground = Burrow with { FightRetreatHealthPercent = line, Targets = targets ?? Burrow.Targets };
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, ground);
        if (recall && recallLands)
        {
            t.RecallTo[Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell] = (ground.Home.LifestoneCell, ground.Home.LifestonePoint);
        }

        return (t, ground);
    }

    /// <summary>The run's first walk to the entry front moves the body 30 m on (a run under way) and sets <paramref name="attacked"/> on it; it never arrives, so the attacker stops it. Returns the leg's start (the Refuge's drop).</summary>
    private static Func<(uint Cell, Vector3 At)?> FirstRunAttacked(RungRig t, Action attacked)
    {
        (uint Cell, Vector3 At)? start = null;
        t.Travel.WalkingOn = r =>
        {
            if (r.Destination.Name != "entry-front" || start is not null)
            {
                return false;
            }

            var here = t.R.H.State.Self.Position!.Value;
            start = (here.CellId, here.Origin);
            t.Put(here.CellId, here.Origin + new Vector3(30, 0, 0));
            attacked();
            return true;
        };
        return () => start;
    }

    /// <summary>
    /// M6 6 E: the cap: the leg retreats by Lifestone Recall (the book holds it), arrives home and fails <c>retreat:run:recall</c>, not <c>fight-back:run:cap</c>. M6 14 the crowded passage: the cap judges progress, not a count
    /// (<see cref="Lifestone.Client.Session.Vitals.WalkStops"/>): here the walk never moves her, so the fourth stop, with no ground gained over three, is the cap (each fight-back a kill).
    /// </summary>
    [Fact]
    public void The_fight_back_cap_retreats_by_lifestone_recall_and_names_the_retreat()
    {
        var (t, ground) = RetreatRig(recall: true, line: 0);
        var next = 0x80002000u;
        t.Travel.WalkingOn = r =>
        {
            if (r.Destination.Name != "entry-front")
            {
                return false;
            }

            t.Attacker(++next, "Tusker Slave", 3f);
            return true;
        };
        t.CastAt = (spell, target) => t.Kill(target);
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(ground));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=retreat:run:recall ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(WalkStopsCount + 1, t.Travel.Requests.Count(r => r.Destination.Name == "entry-front")); // three fight-backs, then the stall on the fourth stop
        Assert.Single(t.Lines("walk stops: the walk to "), l => l.Contains("stalled: 0 m gained on the goal over the last 3 stops", StringComparison.Ordinal));
        Assert.Contains(t.Events, e => e == "cast:" + Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell);
        Assert.Equal(ground.Home.LifestoneCell >> 16, t.Landblock);
        Assert.Single(t.Lines("retreat on run: why=cap way=recall from=0x"));
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("fight-back:run:cap", StringComparison.Ordinal) || l.Contains("died on", StringComparison.Ordinal));
    }

    /// <summary>M6 6 E: a fight-back without the mana for its bolt and no Lifestone Recall in the book walks back to the leg's start (the Refuge's drop) and fails <c>retreat:run:walked-back</c>.</summary>
    [Fact]
    public void No_mana_in_a_fight_back_without_lifestone_recall_walks_back_to_the_legs_start()
    {
        var (t, ground) = RetreatRig(recall: false, line: 0);
        var start = FirstRunAttacked(t, () =>
        {
            t.Attacker(0x80001944, "Tusker Slave", 3f);
            t.Caster.SetVitals(0, 275);
        });
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(ground));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=retreat:run:walked-back ", run.VerdictFields(), StringComparison.Ordinal);
        var back = Assert.Single(t.Travel.Requests, r => r.Destination.Name == "the leg's start");
        Assert.Equal(start()!.Value.Cell, back.Destination.Location.Position.CellId);
        Assert.Equal(start()!.Value.At, t.R.H.State.Self.Position!.Value.Origin); // the body stands where the run began
        Assert.Single(t.Lines("retreat on run: why=no-mana way=walk-back from=0x"));
        Assert.DoesNotContain(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal)); // no bolt without its mana
    }

    /// <summary>M6 6 E: Lifestone Recall in the book but the mana under its cost (a no-mana retreat) is no recall: the leg walks back (check before: the recall's cost read from the SpellTable).</summary>
    [Fact]
    public void No_mana_for_lifestone_recall_walks_back_instead()
    {
        var (t, ground) = RetreatRig(recall: true, line: 0);
        var spell = Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell;
        t.Caster.Table[spell] = WorldActionRig.Spell(spell, DatReaderWriter.Enums.SpellType.Enchantment) with { Name = "Lifestone Recall", BaseMana = 50 };
        FirstRunAttacked(t, () =>
        {
            t.Attacker(0x80001944, "Tusker Slave", 3f);
            t.Caster.SetVitals(0, 275);
        });
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(ground));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=retreat:run:walked-back ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.Lines("retreat on run: check before: why=no-mana"), l => l.Contains("lifestone_recall=yes but the mana 0/", StringComparison.Ordinal) && l.Contains("-> way=walk-back", StringComparison.Ordinal));
        Assert.DoesNotContain(t.Events, e => e == "cast:" + spell);
    }

    /// <summary>
    /// M6 6 E: the health under the retreat line during a fight-back retreats at once (before a bolt) by Lifestone Recall: <c>retreat:run:recall</c>, <c>why=health</c>. M6 14 retreat attackers: the line is the attackers' (rule 1), not the
    /// destination's profile number (the Burrow's 40 here): a creature of a weenie the world database does not hold (M6 14 the losing race: every one it holds has its attack in the creature table) gives the general fallback 50%
    /// (rule 2), named.
    /// </summary>
    [Fact]
    public void The_health_under_the_attackers_line_in_a_fight_back_retreats_by_recall()
    {
        var (t, ground) = RetreatRig(recall: true, line: 40);
        FirstRunAttacked(t, () =>
        {
            t.Attacker(0x80001944, "Tusker Slave", 3f, wcid: 999_999);
            t.Caster.SetHealth(10);
        });
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(ground));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=retreat:run:recall ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(t.Lines("retreat on run: why=health way=recall from=0x"));
        Assert.Contains(t.Lines("retreat due on the walk: why=health:"), l => l.Contains("50% (from the attackers present: 'Tusker Slave'", StringComparison.Ordinal) && l.Contains("the general fallback 50% for 1 attacker(s) with no facts", StringComparison.Ordinal));
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("fight.retreat_health_percent", StringComparison.Ordinal)); // the destination's number is not the walk's
        Assert.DoesNotContain(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal) && e.Contains('@', StringComparison.Ordinal)); // no bolt: the recall only
    }

    /// <summary>
    /// M6 6 E, M6 14 retreat attackers: a destination whose line is 0 (D91: the fight on that ground never retreats) does not set the walk's: over the attackers' line (here the general fallback 50% of an attacker with no facts, a
    /// weenie the world database does not hold) the fight-back goes on, kills, and the run arrives.
    /// </summary>
    [Fact]
    public void Over_the_attackers_line_a_fight_back_goes_on_whatever_the_destinations_number()
    {
        var (t, ground) = RetreatRig(recall: true, line: 0);
        FirstRunAttacked(t, () =>
        {
            t.Attacker(0x80001944, "Tusker Slave", 3f, wcid: 999_999);
            t.Caster.SetHealth(170);
        });
        t.CastAt = (spell, target) => t.Kill(target);
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(ground));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(" ok=yes fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Empty(t.Lines("retreat due"));
        Assert.Empty(t.Lines("retreat on run"));
        Assert.Contains(t.Lines("fight line on the walk: retreat 50% from the attackers present: 'Tusker Slave'"), l => l.Contains("% to the general fallback 50% (rule 2)", StringComparison.Ordinal));
    }

    /// <summary>
    /// M6 14 retreat attackers (rule 1): an attacker whose facts a profile records (the Rampager, weenie 10810, a target of the Holding) is read by them wherever it is met, never by the destination's: with no armour read and no protection
    /// on her its loss while she leaves is all her health, the line is the top, and the fight-back at 10 health retreats by recall; the line names it and where its facts are recorded.
    /// </summary>
    [Fact]
    public void An_attackers_own_facts_derive_the_walks_retreat_line_wherever_they_are_recorded()
    {
        var (t, ground) = RetreatRig(recall: true, line: 0);
        FirstRunAttacked(t, () =>
        {
            t.Attacker(0x80001944, "Rampager", 3f, wcid: 10810);
            t.Caster.SetHealth(10);
        });
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(ground));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=retreat:run:recall ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.Lines("retreat due on the walk: why=health:"), l => l.Contains("95% (from the attackers present: 'Rampager'", StringComparison.Ordinal));
        Assert.Contains(t.Lines("fight line on the walk: retreat 95% from the attackers present: 'Rampager' 0x80001944 (tusker-holding:targets: hit 293, damage 0x4"), l => l.Contains("resistance 1 ", StringComparison.Ordinal));
    }

    /// <summary>
    /// M6 14 retreat attackers (rule 3): the hits the travel leg's fight-back measures feed the line's floor. An attacker with no facts gives the general fallback 50%; a hit of 120 on her (the largest seen) lifts the line over it, and at
    /// 120 of 170 health (71%, over the fallback) the fight-back retreats by recall, the line naming the measure.
    /// </summary>
    [Fact]
    public void The_hits_a_fight_back_measures_lift_the_walks_line_over_the_fallback_and_she_leaves()
    {
        var (t, ground) = RetreatRig(recall: true, line: 0);
        FirstRunAttacked(t, () =>
        {
            t.Attacker(0x80001944, "Tusker Slave", 3f);
            t.Hit("Tusker Slave", 120);
            t.Caster.SetHealth(120);
        });
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(ground));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=retreat:run:recall ", run.VerdictFields(), StringComparison.Ordinal);
        var due = Assert.Single(t.Lines("retreat due on the walk: why=health:"));
        Assert.Contains("% (from the attackers present: 'Tusker Slave'", due, StringComparison.Ordinal);
        Assert.Contains("and a largest hit of 120", due, StringComparison.Ordinal);
        Assert.DoesNotContain("the general fallback", due, StringComparison.Ordinal); // the measure, not the fallback, set it
        Assert.DoesNotContain(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal) && e.Contains('@', StringComparison.Ordinal));
    }

    /// <summary>
    /// M6 14 retreat attackers (rule 4; live 2026-11-14 10:59:16 UTC: the retreat came at mana 49, under Lifestone Recall's 50): a fight-back keeps the way out's reserve, Lifestone Recall's 50 plus a heal's 70 (Heal Self VI). At mana 100,
    /// under the reserve and over the recall, she leaves by recall before any bolt (<c>why=mana-line</c>), the reserve is named on the fight-back's line, and it is published for the vitals guard only while the fight-back lasts.
    /// </summary>
    [Fact]
    public void A_fight_back_keeps_the_recall_and_a_heal_in_reserve_and_leaves_while_it_can_pay_them()
    {
        var (t, ground) = RetreatRig(recall: true, line: 0);
        var recall = Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell;
        t.Caster.Table[recall] = WorldActionRig.Spell(recall, DatReaderWriter.Enums.SpellType.Enchantment) with { Name = "Lifestone Recall", BaseMana = 50 };
        t.Caster.Table[1161] = WorldActionRig.Spell(1161, DatReaderWriter.Enums.SpellType.Boost) with { Name = "Heal Self VI", BaseMana = 70, School = DatReaderWriter.Enums.MagicSchool.LifeMagic };
        t.R.Server((p, seq) => AceM3Writers.MagicUpdateSpell(p, seq, 1161));
        t.Caster.ManaRegenPerSecond = 0;
        var context = t.Context(ground);
        FirstRunAttacked(t, () => t.Attacker(0x80001944, "Tusker Slave", 3f));
        var drained = false;
        var (run, status) = t.RunStation(new RungChangeStation(), context, each: () =>
        {
            // M6 14 run past (D137): the run reads the mana line before it stops, so the mana falls once the fight-back (the attacker blocks her) has begun
            if (!drained && t.Lines("fight-back 1 on the run leg: the walk is stopped;").Count > 0)
            {
                drained = true;
                t.Caster.SetVitals(100, 275);
            }
        });
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=retreat:run:recall ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(t.Lines("retreat on run: why=mana-line way=recall from=0x"));
        Assert.Contains(t.Lines("fight-back 1 on the run leg: the walk is stopped;"), l => l.Contains("the way out's reserve line=120 (Lifestone Recall 50 + Heal Self 70)", StringComparison.Ordinal));
        Assert.Contains(t.Lines("retreat due on the walk: why=mana-line:"), l => l.Contains("mana 100 is at the way out's reserve line=120", StringComparison.Ordinal));
        Assert.DoesNotContain(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal) && e.Contains('@', StringComparison.Ordinal)); // no bolt spent the recall's mana
        Assert.Contains(t.Events, e => e == "cast:" + recall);
        Assert.Null(context.Facts.FightBackReserve); // published only while the fight-back lasts
    }

    /// <summary>
    /// M6 14 approach cost (rule 2; live 2026-11-14, the Holding's approach: 3, 6, 20 and 28 casts for 1, 2, 4 and 3 kills, then the mana line at 111): a rung change's run out to a ground with an approach measures its crossing. Each walk
    /// gains 10 m of the 85 m toward the entry and meets an attacker that takes five bolts of 15 mana; after the seed's three kills (15 casts, mana 426 to 201, over the reserve 120) the rest of the run at that rate would take far more than the 81 over
    /// the reserve: she recalls then (<c>why=approach-abandoned</c>), before the mana line forces it, and the crossing is given up for the run by name (the next way out reads it as <c>approach-abandoned:&lt;rung&gt;</c>).
    /// </summary>
    [Fact]
    public void A_crossing_at_five_casts_a_kill_with_the_mana_falling_recalls_before_the_mana_line_and_is_given_up_for_the_run()
    {
        var (t, ground) = RetreatRig(recall: true, line: 0);
        var recall = Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell;
        t.Caster.Table[recall] = WorldActionRig.Spell(recall, DatReaderWriter.Enums.SpellType.Enchantment) with { Name = "Lifestone Recall", BaseMana = 50 };
        t.Caster.Table[1161] = WorldActionRig.Spell(1161, DatReaderWriter.Enums.SpellType.Boost) with { Name = "Heal Self VI", BaseMana = 70, School = DatReaderWriter.Enums.MagicSchool.LifeMagic };
        t.R.Server((p, seq) => AceM3Writers.MagicUpdateSpell(p, seq, 1161));
        t.Caster.ManaRegenPerSecond = 0;
        var approached = ground with { Approach = GroundCatalog.Get("tusker-holding").Approach }; // a ground with an approach: its crossing is measured
        var context = t.Context(approached);
        var (mana, casts, next) = (426u, 0, 0x80003000u);
        t.Travel.WalkingOn = r =>
        {
            if (r.Destination.Name != "entry-front")
            {
                return false;
            }

            StepToward(t, BurrowEntry.FrontCell, BurrowEntry.Front, 10);
            t.Attacker(++next, "Tusker Slave", 3f);
            return true;
        };
        var bolts = new Dictionary<uint, int>();
        t.CastAt = (spell, target) =>
        {
            casts++;
            mana -= 15;
            t.Caster.SetVitals(mana, 275);
            bolts[target] = bolts.GetValueOrDefault(target) + 1;
            if (bolts[target] == 5)
            {
                t.Kill(target);
            }
            else
            {
                t.Attacker(target, "Tusker Slave", 3f); // it lives and attacks again
            }
        };
        var (run, status) = t.RunStation(new RungChangeStation(), context);
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=retreat:run:recall ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(t.Lines("retreat on run: why=approach-abandoned way=recall from=0x"));
        Assert.Equal((15, 201u), (casts, mana)); // three kills at five casts each, then the recall, the mana still over the reserve
        Assert.Empty(t.Lines("why=mana-line"));
        Assert.Single(t.Lines("crossing: tusker-burrow's approach is measured from here: mana 426,"));
        Assert.Contains(t.Lines("crossing: tusker-burrow: not judged yet: 2 of the 3 seed kills"), l => l.Contains("M6 14 approach cost, rule 2", StringComparison.Ordinal));
        var gaveUp = Assert.Single(t.Lines("retreat due on the walk: why=approach-abandoned: she will not reach the portal above the reserve: 3 kills in 15 casts (5 a kill), mana 426 -> 201 (75 a kill"));
        Assert.Contains("the reserve is line=120 (Lifestone Recall 50 + Heal Self 70)", gaveUp, StringComparison.Ordinal);
        Assert.StartsWith("she will not reach the portal above the reserve: 3 kills in 15 casts", context.Facts.ApproachAbandonedOf(approached.Key), StringComparison.Ordinal);
        Assert.Contains(t.Events, e => e == "cast:" + recall);
    }

    /// <summary>
    /// M6 14 walk dance (the owner, 2026-11-15: at her level the stamina dance replaces the potions; live ladder22 01:54:50 UTC: the walk never danced between fights and the Armory's crossing was given up): a rung change's run out to a
    /// ground with an approach, with Revitalize Self VI and Stamina to Mana Self VI in her book and Mana Conversion trained, so the dance covers her. Each of three walks gains 5 m and meets an attacker that takes three bolts of 20 mana;
    /// M6 14 top-off (the owner, 2026-11-15: a top-off before the run resumes): at the first fight-back's end (the lull) she stands and dances to her full bars: Revitalize Self first (her stamina low), then an attacker arrives
    /// in the dance and is fought back first (its kill measured), then the dance resumes with the press and goes on to her maximum mana, the stamina last; the walk then goes on, each lull tops her off again, the run arrives, and nothing
    /// retreats.
    /// </summary>
    [Fact]
    public void On_the_run_out_she_dances_to_the_crossings_line_in_a_lull_and_an_attacker_in_the_dance_is_fought_back_first()
    {
        var (t, ground) = DanceRig();
        var context = t.Context(ground);
        var (walks, next) = (0, 0x80003000u);
        t.Travel.WalkingOn = r =>
        {
            if (r.Destination.Name != "entry-front" || ++walks > 3)
            {
                return false; // the fourth walk arrives
            }

            StepToward(t, BurrowEntry.FrontCell, BurrowEntry.Front, 5);
            t.Attacker(++next, "Tusker Slave", 3f);
            return true;
        };
        BoltsKillInThree(t);
        var interrupted = false;
        var (run, status) = t.RunStation(new RungChangeStation(), context, maxSeconds: 1200, each: () =>
        {
            if (!interrupted && t.Lines("walk_dance revitalize: try 1").Count > 0)
            {
                interrupted = true;
                t.Attacker(0x80004000, "Tusker Slave", 3f); // an attacker arrives in the dance
            }
        });
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(" ok=yes fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        var lull = t.Lines("walk dance: a lull on the run leg to entry-front (no creature heard attacking her within 10 s stands alive within 40 m, whatever list it is on; the last attack heard on her: ")[0];
        Assert.Contains("she stands and dances to 426 (the top-off before the walk goes on: ", lull, StringComparison.Ordinal); // her maximum
        Assert.Contains(t.Lines("walk_dance_check: on the run leg to entry-front: "), l => l.Contains("the top-off: mana to her maximum 426, stamina near her maximum 275", StringComparison.Ordinal));
        Assert.NotEmpty(t.Lines("walk_dance: the round: Revitalize Self VI ("));
        Assert.NotEmpty(t.Lines("walk_dance: revitalize: the stamina last: Revitalize Self VI")); // the stamina ends full too
        Assert.NotEmpty(t.Lines("walk_dance: done: topped off: mana 426 of 426"));
        var lines = t.R.H.Log.Lines.ToList();
        int At(string needle) => lines.FindIndex(l => l.Contains(needle, StringComparison.Ordinal));
        var (revitalize, attack, ended, press, after) = (At("walk_dance: revitalize 1: Revitalize Self VI (landed)"), At("walk_dance: attack 1 of at most 3: 'Tusker Slave' 0x80004000"), At("walk_dance: fight-back 1 ended:"), At("walk_dance: press 1:"), At("walk_dance_check: after:"));
        Assert.True(revitalize >= 0 && revitalize < attack && attack < ended && ended < press && press < after, $"{revitalize} {attack} {ended} {press} {after}: " + string.Join(" || ", lines.Where(l => l.Contains("walk", StringComparison.Ordinal) || l.Contains("crossing", StringComparison.Ordinal)))); // Revitalize, the attack fought back first, then the press
        Assert.Contains(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal) && e.EndsWith("@80004000", StringComparison.Ordinal)); // the dance's attacker was fought back
        Assert.Contains("the walk goes on", lines[after], StringComparison.Ordinal);
        Assert.Contains(t.Events, e => e == "press:" + 1182u.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)); // Revitalize Self VI, at herself
        Assert.Contains(t.Events, e => e == "press:" + 1681u.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)); // Stamina to Mana Self VI
        Assert.NotEmpty(t.Lines("walk dance: ended on the run leg to entry-front"));
        Assert.Empty(t.Lines("walk-dance:")); // no dance stopped by its bounds (a stamina round's net is not judged)
        Assert.Empty(t.Lines("retreat due"));
        Assert.Null(context.Facts.ApproachAbandonedOf(ground.Key));
    }

    /// <summary>
    /// M6 14 walk dance: the same run with Mana Conversion untrained (the dance does not cover her): no dance is asked for and none runs, and the crossing measured at the third kill is given up as before, saying why the dance does not cover
    /// her.
    /// </summary>
    [Fact]
    public void Without_the_dance_covering_her_the_run_out_gives_the_crossing_up_as_before_and_says_why()
    {
        var (t, ground) = DanceRig(manaConversion: false);
        var context = t.Context(ground);
        var next = 0x80003000u;
        t.Travel.WalkingOn = r =>
        {
            if (r.Destination.Name != "entry-front")
            {
                return false;
            }

            StepToward(t, BurrowEntry.FrontCell, BurrowEntry.Front, 10);
            t.Attacker(++next, "Tusker Slave", 3f);
            return true;
        };
        BoltsKillInThree(t);
        var (run, status) = t.RunStation(new RungChangeStation(), context, maxSeconds: 1200);
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=retreat:run:recall ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Empty(t.Lines("walk dance:"));
        Assert.Empty(t.Lines("walk_dance"));
        var gaveUp = Assert.Single(t.Lines("retreat due on the walk: why=approach-abandoned: she will not reach the portal above the reserve: "));
        Assert.Contains("; the stamina dance does not cover her: Mana Conversion is not trained", gaveUp, StringComparison.Ordinal);
    }

    /// <summary>
    /// M6 14 fire dance (live ladder23 02:51:52 UTC: the Ravagers gave her no lull and she recalled on the mana line at 151 with the line at 120): a fight-back that never lets up, its attacker taking four bolts of 20 mana, her mana 139
    /// (one under the line and the bolt) and her stamina 40 at its start. The first bolt is refused for the line; she dances under fire instead of leaving (a cast at a time, each checked after it lands), bolts on, kills it, and the run
    /// goes on: nothing retreats.
    /// </summary>
    [Fact]
    public void Under_fire_with_no_lull_she_dances_and_bolts_on_instead_of_leaving_on_the_mana_line()
    {
        var (t, ground) = DanceRig();
        var context = t.Context(ground);
        FirstRunAttacked(t, () =>
        {
            t.Attacker(0x80001944, "Tusker Slave", 3f);
            t.Caster.SetVitals(139, 40);
        });
        var bolts = 0;
        t.CastAt = (spell, target) =>
        {
            t.Caster.SetVitals(t.Caster.Mana - 20, t.Caster.Stamina);
            if (++bolts == 4)
            {
                t.Kill(target);
            }
            else
            {
                t.Attacker(target, "Tusker Slave", 3f); // it attacks again: no lull
            }
        };
        var (_, status) = t.RunStation(new RungChangeStation(), context, maxSeconds: 1200);
        var lines = t.R.H.Log.Lines.ToList();
        var trace = string.Join(" || ", lines.Where(l => l.Contains("fire dance", StringComparison.Ordinal) || l.Contains("retreat", StringComparison.Ordinal) || l.Contains("fight-back", StringComparison.Ordinal) || l.Contains("walk_dance", StringComparison.Ordinal) || l.Contains("mana: decide", StringComparison.Ordinal)).Take(40));
        Assert.True(status == ScenarioStatus.Succeeded, trace);
        Assert.Empty(t.Lines("retreat due"));
        var refused = lines.FindIndex(l => l.Contains("fire dance: ", StringComparison.Ordinal) && l.Contains("would take the mana 139 under the way out's reserve line=120", StringComparison.Ordinal) && l.Contains("she dances instead of leaving", StringComparison.Ordinal));
        var cast = lines.FindIndex(l => l.Contains("fire dance: revitalize: ", StringComparison.Ordinal) || l.Contains("fire dance: press: ", StringComparison.Ordinal));
        var kill = lines.FindIndex(l => l.Contains("fight-back: 'Tusker Slave' 0x80001944 is dead", StringComparison.Ordinal));
        Assert.True(refused >= 0 && refused < cast && cast < kill, $"{refused} {cast} {kill}: {trace}");
        Assert.Equal(4, bolts); // she bolted on after the dance
    }

    /// <summary>
    /// M6 14 bolt wait (live ladder23 02:50:34.006 landed, 02:50:36.021 the next try): a bolt whose damage line comes at once is followed by the next cast at once (the kill notice's grace), not two seconds later; with no line the old
    /// two-second bound stands.
    /// </summary>
    [Fact]
    public void A_bolt_whose_damage_line_comes_at_once_is_followed_by_the_next_cast_at_once()
    {
        double Gap(bool line)
        {
            var (t, ground) = RetreatRig(recall: false, line: 0);
            FirstRunAttacked(t, () => t.Attacker(0x80001944, "Tusker Slave", 3f));
            var times = new List<double>();
            t.CastAt = (spell, target) =>
            {
                times.Add(t.Now);
                if (line)
                {
                    t.R.Raw(AceWireWriters.ServerMessage("You scorch Tusker Slave for 30 points with Flame Bolt.", (int)FightTally.MagicChatType));
                }

                if (times.Count == 3)
                {
                    t.Kill(target);
                }
                else
                {
                    t.Attacker(target, "Tusker Slave", 3f);
                }
            };
            t.RunStation(new RungChangeStation(), t.Context(ground));
            Assert.True(times.Count >= 3, string.Join(" | ", t.R.H.Log.Lines.TakeLast(10)));
            if (line)
            {
                Assert.True(t.Lines("fight-back: ").Any(l => l.Contains(" resolved at 'Tusker Slave' 0x80001944 (its damage line)", StringComparison.Ordinal)), string.Join(" || ", t.R.H.Log.Lines.Where(l => l.Contains("fight-back", StringComparison.Ordinal) || l.Contains("chat", StringComparison.Ordinal)).Take(25)));
            }

            return times[2] - times[1];
        }

        var (quick, slow) = (Gap(line: true), Gap(line: false));
        Assert.True(slow - quick >= 1.4 && quick < slow, $"with the line {quick:0.00} s between casts, without {slow:0.00} s");
    }

    /// <summary>
    /// M6 14 walk dance rig: the retreat rig (level 10, the Flame Bolt line, Lifestone Recall at 50 and Heal Self VI at 70: the way out's reserve 120) with Revitalize Self VI and Stamina to Mana Self VI in the book, the scripted caster
    /// answering both at herself, no regeneration, her stamina at 40 of 275 (a round starts with Revitalize Self), Mana Conversion trained unless <paramref name="manaConversion"/> is false; the Burrow with the Armory's approach (its crossing
    /// is measured).
    /// </summary>
    private static (RungRig Rig, HuntingGround Ground) DanceRig(bool manaConversion = true)
    {
        var (t, ground) = RetreatRig(recall: true, line: 0);
        var recall = Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell;
        t.Caster.Table[recall] = WorldActionRig.Spell(recall, DatReaderWriter.Enums.SpellType.Enchantment) with { Name = "Lifestone Recall", BaseMana = 50 };
        t.Caster.Table[1161] = WorldActionRig.Spell(1161, DatReaderWriter.Enums.SpellType.Boost) with { Name = "Heal Self VI", BaseMana = 70, School = DatReaderWriter.Enums.MagicSchool.LifeMagic };
        foreach (var spell in new ushort[] { 1161, 1182, 1681 })
        {
            t.R.Server((p, seq) => AceM3Writers.MagicUpdateSpell(p, seq, spell));
        }

        if (!manaConversion)
        {
            var q = t.R.H.State.Self.Qualities;
            var skill = q.Skills[MageTemplate.ManaConversion];
            q.Skills[MageTemplate.ManaConversion] = skill with { AdvancementClass = 1 }; // untrained
        }

        t.CasterVitals = true;
        t.Caster.CastHook = spell => !Lifestone.Client.Session.Vitals.VitalsMeans.StaminaToManaSelf.Contains(spell) && !Lifestone.Client.Session.Vitals.VitalsMeans.RevitalizeSelf.Contains(spell); // Revitalize Self is the scripted caster's too
        t.Caster.ManaRegenPerSecond = 0;
        t.Caster.StaminaRegenPerSecond = 0;
        t.Caster.SetVitals(426, 40);
        return (t, ground with { Approach = GroundCatalog.Get("tusker-armory").Approach });
    }

    /// <summary>The fight-backs' bolts: 20 mana each from the scripted caster's own mana; the third at a target kills it, the others leave it attacking.</summary>
    private static void BoltsKillInThree(RungRig t)
    {
        var bolts = new Dictionary<uint, int>();
        t.CastAt = (spell, target) =>
        {
            t.Caster.SetVitals(t.Caster.Mana - 20, t.Caster.Stamina);
            bolts[target] = bolts.GetValueOrDefault(target) + 1;
            if (bolts[target] == 3)
            {
                t.Kill(target);
            }
            else
            {
                t.Attacker(target, "Tusker Slave", 3f); // it lives and attacks again
            }
        };
    }

    /// <summary>Moves the body <paramref name="metres"/> toward <paramref name="cell"/>'s <paramref name="at"/> on global outdoor coordinates (a walk under way).</summary>
    private static void StepToward(RungRig t, uint cell, Vector3 at, float metres)
    {
        static Vector2 Global(uint c, Vector3 p) => new(((c >> 24) * 192f) + p.X, (((c >> 16) & 0xFF) * 192f) + p.Y);
        var here = t.R.H.State.Self.Position!.Value;
        var from = Global(here.CellId, here.Origin);
        var to = from + (Vector2.Normalize(Global(cell, at) - from) * metres);
        var (lbx, lby) = ((uint)(to.X / 192), (uint)(to.Y / 192));
        var (x, y) = (to.X - (lbx * 192f), to.Y - (lby * 192f));
        t.Put((lbx << 24) | (lby << 16) | (uint)(((int)(x / 24) * 8) + (int)(y / 24) + 1), new Vector3(x, y, here.Origin.Z));
    }

    /// <summary>M6 6 E: a fight-back past its 180 s bound (an attacker the bolts do not kill) retreats too (the same retreat fits; the attacker is left): here on foot, <c>why=timeout</c>.</summary>
    [Fact]
    public void A_fight_back_past_its_bound_retreats_and_leaves_the_attacker()
    {
        var (t, ground) = RetreatRig(recall: false, line: 0);
        FirstRunAttacked(t, () => t.Attacker(0x80001944, "Tusker Slave", 3f));
        t.CastAt = (spell, target) => t.Attacker(target, "Tusker Slave", 3f); // the bolt does not kill it, and it attacks again
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(ground), maxSeconds: 900);
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=retreat:run:walked-back ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(t.Lines("retreat on run: why=timeout way=walk-back from=0x"));
        Assert.Contains(t.Events, e => e.EndsWith("@80001944", StringComparison.Ordinal));
    }

    /// <summary>M6 6 E: a retreat that fails is its own named stop: the walk back refused is <c>retreat-failed:run:walk-back</c>; a Lifestone Recall that no teleport answers is <c>retreat-failed:run:recall:no-teleport</c>.</summary>
    [Fact]
    public void A_retreat_that_fails_is_a_named_stop()
    {
        var (walk, walkGround) = RetreatRig(recall: false, line: 0);
        FirstRunAttacked(walk, () =>
        {
            walk.Attacker(0x80001944, "Tusker Slave", 3f);
            walk.Caster.SetVitals(0, 275);
        });
        walk.Travel.Refuse = r => r.Destination.Name == "the leg's start" ? "travel:run:no-path: the test refuses the walk back" : null;
        var (walkRun, walkStatus) = walk.RunStation(new RungChangeStation(), walk.Context(walkGround));
        Assert.Equal(ScenarioStatus.Failed, walkStatus);
        Assert.Contains(" fail=retreat-failed:run:walk-back ", walkRun.VerdictFields(), StringComparison.Ordinal);
        Assert.Empty(walk.Lines("retreat on run: why="));

        var (recall, recallGround) = RetreatRig(recall: true, line: 40, recallLands: false);
        FirstRunAttacked(recall, () =>
        {
            recall.Attacker(0x80001944, "Tusker Slave", 3f);
            recall.Caster.SetHealth(10);
        });
        var (recallRun, recallStatus) = recall.RunStation(new RungChangeStation(), recall.Context(recallGround));
        Assert.Equal(ScenarioStatus.Failed, recallStatus);
        Assert.Contains(" fail=retreat-failed:run:recall:no-teleport ", recallRun.VerdictFields(), StringComparison.Ordinal);
        Assert.Empty(recall.Lines("retreat on run: why="));
    }

    // ------------------------------------------------------------------------------------------------ M6 6 FXD2 (6-D4): the retreat's recall is checked before and has a way back when refused

    /// <summary>The retreat's recall the way the bolt pick judges a bolt: the recall's table entry is a War Magic spell of one Lead Scarab here, so the pack's War focus makes the foci formula the judge (a Portal school has no focus item to read).</summary>
    private static void RecallNeedsLeadScarab(RungRig t, bool packHoldsIt)
    {
        var spell = Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell;
        var formula = new uint[] { 1, 0, 0, 0, 0, 0, 0, 0 };
        t.Caster.Table[spell] = WorldActionRig.Spell(spell, DatReaderWriter.Enums.SpellType.Enchantment) with { Name = "Lifestone Recall", School = DatReaderWriter.Enums.MagicSchool.WarMagic, BaseMana = 1, Formula = formula };
        var stacks = new List<(uint Wcid, int Stack)> { (Lifestone.Client.Session.Mage.LevelFloor.FocusOf(DatReaderWriter.Enums.MagicSchool.WarMagic), 1) };
        if (packHoldsIt)
        {
            foreach (var (component, n) in Lifestone.Client.Session.Mage.GearKit.FociFormulaCounts(formula))
            {
                Assert.True(Lifestone.Data.Magic.ComponentWeenies.TryGetByComponent(component, out var weenie));
                stacks.Add((weenie.Wcid, n));
            }
        }

        for (var i = 0; i < stacks.Count; i++)
        {
            var guid = 0x80007001u + (uint)i;
            var (wcid, stack) = stacks[i];
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: "Pack item " + wcid);
            t.R.H.R.Reducer.OnCreateObject(t.R.H.R.Context, basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, WeenieClassId = wcid, StackSize = (ushort)stack } }, isUpdate: false);
            t.R.Server((p, seq) => AceM3Writers.PutObjInContainer(p, seq, guid, WorldActionRig.Player, 0, 0));
        }
    }

    private static Func<(uint Cell, Vector3 At)?> HealthRetreat(RungRig t) => FirstRunAttacked(t, () =>
    {
        t.Attacker(0x80001944, "Tusker Slave", 3f);
        t.Caster.SetHealth(10);
    });

    /// <summary>M6 6 FXD2 (6-D4): a pack the recall's components are short of is no recall: the check before names the lack and the way is the walk back; no recall is cast.</summary>
    [Fact]
    public void A_pack_short_of_the_recalls_components_walks_back_and_the_line_names_the_lack_FXD2()
    {
        var (t, ground) = RetreatRig(recall: true, line: 40);
        RecallNeedsLeadScarab(t, packHoldsIt: false);
        HealthRetreat(t);
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(ground));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=retreat:run:walked-back ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.Lines("retreat on run: check before: why=health"), l => l.Contains("the pack is short for it: Lead Scarab", StringComparison.Ordinal) && l.Contains("-> way=walk-back", StringComparison.Ordinal));
        Assert.DoesNotContain(t.Events, e => e == "cast:" + Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell);
    }

    /// <summary>M6 6 FXD2 (6-D4): a pack that holds the components and a tie not known false recall as before (the check names what it read).</summary>
    [Fact]
    public void A_pack_that_holds_the_recalls_components_still_recalls_FXD2()
    {
        var (t, ground) = RetreatRig(recall: true, line: 40);
        RecallNeedsLeadScarab(t, packHoldsIt: true);
        HealthRetreat(t);
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(ground));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=retreat:run:recall ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.Lines("retreat on run: check before: why=health"), l => l.Contains("components=held", StringComparison.Ordinal) && l.Contains("-> way=recall", StringComparison.Ordinal));
    }

    /// <summary>M6 6 FXD2 (6-D4): a tie known false (0x049E) is no recall either: the walk back, the reason on the check-before line.</summary>
    [Fact]
    public void A_tie_known_false_walks_back_and_the_line_says_so_FXD2()
    {
        var (t, ground) = RetreatRig(recall: true, line: 40);
        HealthRetreat(t);
        var context = t.Context(ground);
        context.Facts.LifestoneTied = false;
        var (run, status) = t.RunStation(new RungChangeStation(), context);
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=retreat:run:walked-back ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(t.Lines("retreat on run: check before: why=health"), l => l.Contains("the lifestone tie is known false (0x049E)", StringComparison.Ordinal) && l.Contains("-> way=walk-back", StringComparison.Ordinal));
        Assert.DoesNotContain(t.Events, e => e == "cast:" + Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell);
    }

    /// <summary>M6 6 FXD2 (6-D4): a recall ACE refuses for a component (0x0400) or the tie (0x049E) falls back once to the walk back: <c>way=walk-back after recall refused</c>, the leg fails <c>retreat:run:walked-back</c>, never <c>retreat-failed:...:refused</c>.</summary>
    [Theory]
    [InlineData(0x0400u)]
    [InlineData(0x049Eu)]
    public void A_recall_ACE_refuses_for_a_component_or_the_tie_falls_back_once_to_the_walk_back_FXD2(uint refusal)
    {
        var (t, ground) = RetreatRig(recall: true, line: 40);
        t.RecallRefusal = refusal;
        var start = HealthRetreat(t);
        var context = t.Context(ground);
        var (run, status) = t.RunStation(new RungChangeStation(), context);
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(" fail=retreat:run:walked-back ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(t.Events, e => e == "cast:" + Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell); // the recall is tried once
        Assert.Single(t.Lines("retreat on run: why=health way=walk-back after recall refused from=0x"));
        Assert.Single(t.Travel.Requests, r => r.Destination.Name == "the leg's start");
        Assert.Equal(start()!.Value.At, t.R.H.State.Self.Position!.Value.Origin);
        Assert.Equal(refusal == 0x049Eu ? false : null, context.Facts.LifestoneTied);
    }

    // ------------------------------------------------------------------------------------------------ M6 6 R1F: the fight-back's components and combat mode (live round 1)

    /// <summary>
    /// M6 6 R1F (live round 1: Flame Bolt IV refused 0x0400 four times while a tusker beat the climber down): a bolt ACE refuses for a missing component is dropped for the leg and the next bolt is cast at once; it kills, and the run arrives.
    /// </summary>
    [Fact]
    public void A_bolt_refused_for_a_component_is_dropped_for_the_leg_and_the_next_bolt_kills_R1F()
    {
        var (t, ground) = RetreatRig(recall: false, line: 0);
        FirstRunAttacked(t, () => t.Attacker(0x80001944, "Tusker Slave", 3f));
        uint refused = 0;
        t.CastAt = (spell, target) =>
        {
            if (refused == 0)
            {
                refused = spell;
                t.NoComponents.Add(spell);
            }
            else if (spell != refused)
            {
                t.Kill(target);
            }
        };
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(ground));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(" ok=yes fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        var casts = t.Events.Where(e => e.EndsWith("@80001944", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, casts.Count);
        Assert.Equal($"cast:{refused}@80001944", casts[0]);
        Assert.NotEqual(casts[0], casts[1]); // never the refused bolt again
        Assert.Single(t.Lines("was refused for a missing component (0x0400): dropped for this leg; the next bolt is picked"));
        Assert.Contains(t.Lines("fight-back: cast "), l => l.Contains("was refused for a missing component on this leg", StringComparison.Ordinal)); // the second pick names what it skipped
    }

    /// <summary>M6 6 R1F: every bolt the book holds refused for a component is the fifth retreat why, <c>no-components</c>, at once: each bolt is tried once, the walk back leaves the attacker, and the leg fails <c>retreat:run:walked-back</c>.</summary>
    [Fact]
    public void No_bolt_with_its_components_retreats_at_once_with_why_no_components_R1F()
    {
        var (t, ground) = RetreatRig(recall: false, line: 0);
        FirstRunAttacked(t, () => t.Attacker(0x80001944, "Tusker Slave", 3f));
        t.CastAt = (spell, target) =>
        {
            t.NoComponents.Add(spell);
            t.Attacker(target, "Tusker Slave", 3f); // it attacks on (the rig's attacker is heard for 10 s)
        };
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(ground));
        Assert.True(status == ScenarioStatus.Failed, string.Join(" | ", t.R.H.Log.Lines.Where(l => l.Contains("fight-back", StringComparison.Ordinal) || l.Contains("retreat", StringComparison.Ordinal)).TakeLast(14)));
        Assert.Contains(" fail=retreat:run:walked-back ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(t.Lines("retreat on run: why=no-components way=walk-back from=0x"));
        Assert.Contains(t.Lines("retreat due on the walk: why=no-components: no War Magic bolt can be cast at 'Tusker Slave' 0x80001944: no-components: "), l => l.Contains("was refused for a missing component on this leg", StringComparison.Ordinal));
        var bolts = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => $"cast:{b.Id}@80001944").ToList();
        var casts = t.Events.Where(e => e.EndsWith("@80001944", StringComparison.Ordinal)).ToList();
        Assert.Equal(bolts.Order(StringComparer.Ordinal), casts.Order(StringComparer.Ordinal)); // each bolt of the book once, none twice
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("died on", StringComparison.Ordinal));
    }

    /// <summary>
    /// M6 6 R1F (live round 1: a cast refused "the player is not in the magic combat mode" ended the fight-back): the mode is checked before each cast; out of magic, the leg enters it (check, act, check) and casts; when the mode is not reached
    /// the fight-back is the named stop <c>fight-back:run:no-magic-mode</c>.
    /// </summary>
    [Fact]
    public void The_magic_mode_is_checked_before_each_cast_and_entered_again_else_a_named_stop_R1F()
    {
        var (t, ground) = RetreatRig(recall: false, line: 0);
        FirstRunAttacked(t, () => t.Attacker(0x80001944, "Tusker Slave", 3f));
        var casts = 0;
        t.CastAt = (spell, target) =>
        {
            if (++casts == 1)
            {
                t.R.CombatModeIs(CombatModes.NonCombat); // the mode is lost after the first bolt
            }
            else
            {
                t.Kill(target);
            }
        };
        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(ground));
        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        var first = t.Events.FindIndex(e => e.EndsWith("@80001944", StringComparison.Ordinal));
        var second = t.Events.FindLastIndex(e => e.EndsWith("@80001944", StringComparison.Ordinal));
        Assert.True(second > first && t.Events.IndexOf("mode:8", first) is var magic && magic > first && magic < second, string.Join(" > ", t.Events));
        Assert.Single(t.Lines("fight-back: check before the cast: the combat mode is 1, not magic (8): entering the magic mode"));
        Assert.Single(t.Lines("fight-back: check after the mode change: the combat mode is 8 (magic)"));

        var (stuck, stuckGround) = RetreatRig(recall: false, line: 0);
        FirstRunAttacked(stuck, () => stuck.Attacker(0x80001944, "Tusker Slave", 3f));
        stuck.CastAt = (spell, target) =>
        {
            stuck.R.CombatModeIs(CombatModes.NonCombat);
            stuck.ModeRefused = true; // the mode change is never answered
        };
        var (stuckRun, stuckStatus) = stuck.RunStation(new RungChangeStation(), stuck.Context(stuckGround));
        Assert.Equal(ScenarioStatus.Failed, stuckStatus);
        Assert.Contains(" fail=fight-back:run:no-magic-mode ", stuckRun.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(stuck.Events, e => e.EndsWith("@80001944", StringComparison.Ordinal)); // no cast out of the magic mode
        Assert.Contains(stuck.Events, e => e == "mode-unanswered:8");
    }

    [Fact]
    public void A_facing_front_past_the_portal_from_the_start_uses_the_approach_side_first()
    {
        var edge = Routes.Edge("holtburg-to-network");
        var portal = RungPortal.Of(edge, Routes);
        var facing = portal.Facing!.Value;
        var behind = new CellFrame(edge.Cell, new AcFrame(edge.Origin - new Vector3(facing.X * 20f, facing.Y * 20f, 0), Quaternion.Identity));
        var ahead = new CellFrame(edge.Cell, new AcFrame(edge.Origin + new Vector3(facing.X * 20f, facing.Y * 20f, 0), Quaternion.Identity));
        Assert.True(TravelLeg.FacingPastPortal(portal, edge.Cell, behind));
        Assert.False(TravelLeg.FacingPastPortal(portal, edge.Cell, ahead));
        Assert.False(TravelLeg.FacingPastPortal(portal with { Facing = null }, edge.Cell, behind));
    }

    // ------------------------------------------------------------------------------------------------ M6 4 F1: the journey's own travel, the one slot, the body's ground, the lever doors

    /// <summary>The Burrow stand-in hunted from Holtburg (the den's home): the travel file's start, where the way home by portals ends.</summary>
    private static HuntingGround HoltburgBurrow => Burrow with { Home = GroundCatalog.Get(FightOptions.DefaultGround).Home };

    /// <summary>The Lodge stand-in (route by-refuge, no entry section: its run names the entry), hunted from Holtburg.</summary>
    private static HuntingGround HoltburgLodge => Ground("tusker-lodge", null, 0x01D50100) with { Home = GroundCatalog.Get(FightOptions.DefaultGround).Home };

    private static TeleportDestination Home(HuntingGround g) =>
        new("home-lifestone", [], new WorldLocation("home-lifestone", new CellPosition(g.Home.LifestoneCell, g.Home.LifestonePoint), true, 0), "test: the trip's way home");

    /// <summary>A rig whose book is read (the server sent it) and holds <paramref name="spells"/>: a spell it lacks is absent, not unread.</summary>
    private static RungRig ReadBook(int level, (uint Cell, Vector3 At)? at, params uint[] spells)
    {
        var t = new RungRig(level, at: at);
        var book = t.R.H.State.Self.Spellbook;
        book.IsKnown = true;
        foreach (var spell in spells)
        {
            book.Learn(spell);
        }

        return t;
    }

    [Fact]
    public void A_trip_started_in_a_routed_ground_at_11_without_lifestone_recall_goes_home_and_back_by_the_rung_change()
    {
        // 4-R1: the logistics trip's way home (TravelPurpose.Home) and way back (TravelPurpose.Ground) through the journey's own travel, which asks the autopilot's travel rule first: no recall is cast, no place is made.
        var burrow = HoltburgBurrow;
        var t = ReadBook(11, (burrow.Arrival.Cell, burrow.Arrival.Origin), DenTravelStation.PortalRecallSpell); // a read book without Lifestone Recall (D43: the climber learns it at 16)
        t.Portals(Routes, "refuge-to-network", "network-to-holtburg", "holtburg-to-network", "network-to-refuge");
        t.Exit(burrow, 0x7F981FF0, new Vector3(150, 50, 12));
        t.Entry(BurrowEntry, burrow);
        var (travel, _) = t.Journey(burrow);

        var home = travel.GoTo(t.Session, new TravelRequest(Home(burrow), TravelPurpose.Home), t.Now);
        Assert.Equal(TravelStatus.Arrived, t.RunLeg(home));
        Assert.Equal(0xA9B4u, t.Landblock);
        Assert.Contains(t.Lines("journey travel: the travel rule: direction=home way=rung-change ground=tusker-burrow "), l => l.Contains(" where=atground ", StringComparison.Ordinal));
        Assert.Contains(t.Lines("rung_change: ground=tusker-burrow"), l => l.Contains("route=refuge hops=3", StringComparison.Ordinal) && l.Contains("ok=yes", StringComparison.Ordinal) && l.EndsWith("direction=home", StringComparison.Ordinal));

        var back = travel.GoTo(t.Session, new TravelRequest(burrow.ArrivalDestination(), TravelPurpose.Ground), t.Now);
        Assert.Equal(TravelStatus.Arrived, t.RunLeg(back));
        Assert.Equal(0x01D4u, t.Landblock);
        Assert.Contains(t.Lines("journey travel: the travel rule: direction=out way=rung-change ground=tusker-burrow "), l => l.Contains(" route=by-refuge;", StringComparison.Ordinal));
        Assert.Contains(t.Lines("rung_change: ground=tusker-burrow"), l => l.Contains("route=by-refuge hops=3", StringComparison.Ordinal) && l.Contains("ok=yes", StringComparison.Ordinal));

        Assert.DoesNotContain(t.Events, e => e.StartsWith("cast:", StringComparison.Ordinal)); // never a recall tried (decided before the act, not chosen by a failure)
        Assert.Equal(0, t.Places);
        Assert.Equal(0, travel.Places);
    }

    [Fact]
    public void A_trip_started_in_a_routed_ground_at_20_recalls_home_and_comes_back_by_the_Facility_Hub()
    {
        var burrow = HoltburgBurrow;
        var t = ReadBook(20, (burrow.Arrival.Cell, burrow.Arrival.Origin), Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell, DenTravelStation.PortalRecallSpell);
        t.Portals(Routes, "holtburg-to-network", "network-to-facility-hub", "hub-burrow"); // M6 7 J: the hub's route is first from 15
        t.Entry(BurrowEntry, burrow);
        var (travel, _) = t.Journey(burrow);

        using (var home = travel.GoTo(t.Session, new TravelRequest(Home(burrow), TravelPurpose.Home), t.Now))
        {
            Assert.Equal(TravelMeans.Recall, home.Means); // the book holds Lifestone Recall: the rule's way home is the recall
            Assert.Contains(t.Lines("journey travel: the travel rule: direction=home way=recall-home "), l => l.Contains("lifestone_recall=yes", StringComparison.Ordinal));
        }

        t.Put(0xA9B40019, new Vector3(84, 7.7f, 94)); // home (the recall's landing)
        var back = travel.GoTo(t.Session, new TravelRequest(burrow.ArrivalDestination(), TravelPurpose.Ground), t.Now);
        Assert.Equal(TravelStatus.Arrived, t.RunLeg(back));
        Assert.Equal(0x01D4u, t.Landblock);
        Assert.True(t.Lines("rung_change: ground=tusker-burrow").Any(l => l.Contains("route=by-facility-hub hops=4", StringComparison.Ordinal) && l.Contains("ok=yes", StringComparison.Ordinal)), string.Join(" | ", t.Lines("rung_change")));
        Assert.DoesNotContain(t.Events, e => e == "cast:" + DenTravelStation.PortalRecallSpell); // the island's entry is untieable: no Primary Portal Recall tried
    }

    [Fact]
    public void A_journey_travel_without_a_context_or_for_a_tied_ground_keeps_the_recall()
    {
        var t = ReadBook(45, null, DenTravelStation.PortalRecallSpell);
        var (travel, context) = t.Journey(Temple40);
        context.Facts.SetPrimaryPortalTie(Temple40.Key);
        using var tied = travel.GoTo(t.Session, new TravelRequest(Temple40.ArrivalDestination(), TravelPurpose.Ground), t.Now);
        Assert.Equal(TravelMeans.Recall, tied.Means);
        Assert.Contains(t.Lines("journey travel: the travel rule: direction=out way=den-travel ground=tusker-temple-fort-tethana "), l => l.Contains("known tied", StringComparison.Ordinal));

        var bare = new JourneyTravel("Winnie", new JourneyFacts(), t.R.H.Log, dungeon: HoltburgBurrow); // no context: the recalls as before
        Assert.Null(bare.Context);
        Assert.Null(bare.Decide(t.Session, HoltburgBurrow, RungDirection.Out));
    }

    [Fact]
    public void A_new_tie_holds_the_one_slot_so_another_tieable_ground_is_routed_not_recalled()
    {
        // 4-R2: tie A (the Holding stand-in), then route B (the Temple 20-40 stand-in, tieable): B's tie is not unknown, the slot holds A.
        var facts = new JourneyFacts();
        var before = AutopilotTravelRule.Out(Input(Temple2040 with { Portal = Temple40.Portal }, 20, portalRecall: true, tie: true, tied: facts.TieOf(Temple2040)), bookRead: true, Routes);
        Assert.Equal(TravelWay.DenTravel, before.Way); // unknown, the book holds 48: the recall tried (M5's rule)

        facts.SetPrimaryPortalTie(Holding.Key);
        Assert.Equal("tusker-holding", facts.PrimaryPortalSlot);
        Assert.Equal(PortalTie.Untied, facts.TieOf(Temple2040)); // a tieable entry: the slot holds another
        Assert.Equal(PortalTie.Untied, facts.PortalTieOf(FightOptions.DefaultGround)); // a catalog ground with a tie record: known untied
        Assert.Equal(PortalTie.Unknown, facts.TieOf(Burrow)); // untieable, no tie record: nothing to say

        var after = AutopilotTravelRule.Out(Input(Temple2040 with { Portal = Temple40.Portal }, 20, portalRecall: true, tie: true, tied: facts.TieOf(Temple2040)), bookRead: true, Routes);
        Assert.Equal(TravelWay.RungChange, after.Way);
        var plan = RungRoute.Decide(Input(Temple2040, 20, portalRecall: true, tie: true, tied: facts.TieOf(Temple2040)), Routes);
        Assert.Contains("tie:entry:tusker-temple-20-40", Shape(plan)); // the route's tie leg moves the slot

        facts.SetPrimaryPortalTie(Temple2040.Key);
        Assert.Equal(PortalTie.Tied, facts.TieOf(Temple2040));
        Assert.Equal(PortalTie.Untied, facts.PortalTieOf(Holding.Key));
    }

    [Fact]
    public void A_recall_that_lands_in_another_known_grounds_arrival_is_named_slot_holds_and_moves_the_slot()
    {
        var holding = Holding;
        var temple = Temple2040;
        var t = new RungRig(level: 20, spells: [47, 48]);
        t.RecallTo[48] = (holding.Arrival.Cell, holding.Arrival.Origin); // ACE: the one slot holds the Holding's portal
        var grounds = new JourneyContext { Character = "Winnie", Account = "test", Log = t.R.H.Log, Travel = t.Travel, Spells = t.Caster.Table, Dungeon = temple, Grounds = [holding, temple] };
        grounds.Facts.SetPortalTie(temple.Key, PortalTie.Tied); // a stale memory: the Temple was tied once
        var (run, status) = t.RunStation(new RungChangeStation(), grounds);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("fail=recall:portal:slot-holds:tusker-holding", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal("tusker-holding", grounds.Facts.PrimaryPortalSlot);
        Assert.Equal(PortalTie.Untied, grounds.Facts.TieOf(temple));
    }

    [Fact]
    public void The_way_home_is_planned_from_the_ground_that_holds_the_body()
    {
        // 4-R3: the Lodge's run started in the Burrow at 15 without Lifestone Recall: the Burrow's exit, then the island's way home.
        var lodge = HoltburgLodge;
        var burrow = HoltburgBurrow;
        Assert.Same(burrow, RungRoute.BodyGroundOf(0x01D40100, lodge, [lodge, burrow]));
        Assert.Null(RungRoute.BodyGroundOf(0x01D50100, lodge, [lodge, burrow])); // the run's own ground holds it
        var home = RungRoute.Decide(Input(lodge, 15, RungWhere.Elsewhere, "0x01D4", direction: RungDirection.Home) with { BodyGround = burrow }, Routes);
        Assert.Null(home.Stop);
        Assert.Equal(["hop:exit:tusker-burrow", "hop:refuge-to-network", "hop:network-to-holtburg"], Shape(home));
        Assert.Contains("the body stands in tusker-burrow's landblocks", home.Why, StringComparison.Ordinal);
        Assert.StartsWith("TODO(m6-way-home)", RungRoute.Decide(Input(lodge, 15, RungWhere.Elsewhere, "0x01D4", direction: RungDirection.Home), Routes).Stop, StringComparison.Ordinal); // without the body's ground: the old hole

        // The move-on goes out from there: home first by the Burrow's way, then the Lodge's route.
        var outward = RungRoute.Decide(Input(lodge, 15, RungWhere.Elsewhere, "0x01D4") with { BodyGround = burrow }, Routes);
        Assert.Null(outward.Stop);
        Assert.Equal("hop:exit:tusker-burrow", Shape(outward)[0]);
        Assert.Equal("enter:entry:tusker-lodge", Shape(outward)[^1]);

        // The rule's input finds it from the state and the context's grounds; the autopilot's way home is the rung change.
        var t = ReadBook(15, (burrow.Arrival.Cell, burrow.Arrival.Origin), DenTravelStation.PortalRecallSpell);
        var input = AutopilotTravelRule.Input(t.R.H.State, lodge, new JourneyFacts(), Routes, RungDirection.Home, [lodge, burrow]);
        Assert.Same(burrow, input.BodyGround);
        Assert.Equal(TravelWay.RungChange, AutopilotTravelRule.Home(input, bookRead: true, Routes).Way);

        // The Burrow's run started on the outskirts at 10: the outskirts' walk home.
        var outskirts = GroundCatalog.Get("holtburg-outskirts");
        var cell = ((uint)outskirts.Landblocks[0] << 16) | 0x0001u;
        Assert.Same(outskirts, RungRoute.BodyGroundOf(cell, burrow, GroundCatalog.All));
        var walk = RungRoute.Decide(Input(burrow, 10, RungWhere.Elsewhere, RungRoute.Hex(cell >> 16), direction: RungDirection.Home) with { BodyGround = outskirts }, Routes);
        Assert.Null(walk.Stop);
        Assert.Equal(["walk-home:home-lifestone-point"], Shape(walk));
    }

    [Fact]
    public void A_way_home_whose_first_portal_is_behind_a_lever_door_is_refused_by_name()
    {
        // 4-R8: Fort Tethana's way home starts at its Network portal, behind the lever gate (lever-doors.json), as TownNetworkTrip refuses it.
        var input = Input(Temple40, 45, RungWhere.Elsewhere, "fort-tethana", direction: RungDirection.Home);
        var refused = RungRoute.Decide(input, Routes);
        Assert.StartsWith("TODO(door-lever)", refused.Stop, StringComparison.Ordinal);
        Assert.Contains("lever-door:", refused.Stop, StringComparison.Ordinal);
        Assert.Empty(refused.Legs);
        Assert.Equal(TravelWay.Recall, AutopilotTravelRule.Home(input, bookRead: true, Routes).Way); // no way by portals: the recall as before, its refusal names itself

        var open = RungRoute.Decide(input, Routes, LeverDoors.None); // the doors are data: with none, the way is planned
        Assert.Null(open.Stop);
        Assert.NotEmpty(open.Legs);
    }

    private static string Token(string fields, string key) => fields.Split(' ').First(f => f.StartsWith(key + "=", StringComparison.Ordinal))[(key.Length + 1)..];
}

/// <summary>
/// A stand-in ACE for the rung change (M6 item 4 t2), in <see cref="J3bRig"/>'s shape: the mage (<see cref="ScriptedCaster"/>) at Holtburg's lifestone; the travel file's portals as objects at their rows' places, whose Use teleports to the
/// edge's destination 1 s later (unless <see cref="Silent"/>, or <see cref="DropOverride"/> says elsewhere); Primary Portal Tie 47 at a portal links; the gem's Use teleports to the Refuge's drop. The travel seam moves the body to the request's
/// point (or beside its object) at once, as a run on foot that arrived.
/// </summary>
internal sealed class RungRig
{
    public const uint GemGuid = 0x80000777;

    private readonly List<(double Due, Action What)> _later = [];
    private readonly Dictionary<uint, (uint Cell, Vector3 At)> _drops = [];
    private int _seen;
    private ushort _teleportStamp = 1;
    private ushort _positionStamp = 2;
    private ushort _stateStamp = 2;

    public RungRig(int level, bool gem = false, ushort[]? spells = null, (uint Cell, Vector3 At)? at = null, uint mana = 426, uint stamina = 275, uint manaTinctures = 0, bool withHeal = false)
    {
        R = new WorldActionRig();
        Caster = new ScriptedCaster(R, mana, stamina, known: [], manaTinctures: manaTinctures, withDrain: withHeal); // the book is what the rig's spells teach below, nothing else (M6 15: the scripted caster's own default is the build's whole book)
        foreach (var spell in spells ?? [])
        {
            R.Server((p, seq) => AceM3Writers.MagicUpdateSpell(p, seq, spell));
        }

        R.PrivateInt(PlayerQualities.LevelProperty, level);
        if (gem)
        {
            var basis = ReducerHarness.Create(GemGuid, ReducerHarness.Stamps(), null, name: TravelRoutes.Default.Gems[0].Name);
            R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, WeenieClassId = TravelRoutes.Default.Gems[0].Wcid } }, isUpdate: false);
            R.Server((p, seq) => AceM3Writers.PutObjInContainer(p, seq, GemGuid, WorldActionRig.Player, 0, 0));
        }

        var (cell, point) = at ?? (0xA9B40019u, new Vector3(84, 7.7f, 94));
        Put(cell, point);
        _seen = R.H.Net.Sends.Count;
        Travel = new MovingTravel(this);
    }

    public WorldActionRig R { get; }

    public ScriptedCaster Caster { get; }

    public MovingTravel Travel { get; }

    /// <summary>The portal guids whose Use the server leaves unanswered (no teleport).</summary>
    public HashSet<uint> Silent { get; } = [];

    /// <summary>
    /// M6 14 entry recharge: the scripted caster answers her own vitals (<see cref="ScriptedCaster.Pump"/>: Stamina to Mana Self cast at herself, the mana drinks, the regeneration); every other cast and use stays this rig's. Set once, before
    /// the station runs.
    /// </summary>
    public bool CasterVitals
    {
        get => _casterVitals;
        set
        {
            _casterVitals = value;
            Caster.CastHook = spell => !Lifestone.Client.Session.Vitals.VitalsMeans.StaminaToManaSelf.Contains(spell); // an untargeted cast (a recall) is this rig's
            Caster.TargetedCastHook = (_, target) => target != WorldActionRig.Player; // a bolt or a tie is this rig's
        }
    }

    private bool _casterVitals;

    /// <summary>Where a portal's Use drops instead of its own destination.</summary>
    public Dictionary<uint, (uint Cell, Vector3 At)> DropOverride { get; } = [];

    /// <summary>Everything the server was asked: <c>use:&lt;guid&gt;</c>, <c>cast:&lt;spell&gt;@&lt;guid&gt;</c>, <c>cast:&lt;spell&gt;</c>, <c>mode:&lt;n&gt;</c>, <c>teleport:&lt;cell&gt;</c>.</summary>
    public List<string> Events { get; } = [];

    public double Now => R.H.R.Clock.Now;

    public uint Landblock => (R.H.State.Self.Position?.CellId ?? 0) >> 16;

    public int Places => Enumerable.Range(0, R.H.ActionLog.Count).Count(i => R.H.ActionLog[i].Kind == ActionKind.ToolPlace);

    public List<string> Lines(string needle) => [.. R.H.Log.Lines.Where(l => l.Contains(needle, StringComparison.Ordinal))];

    /// <summary>The travel file's edges as portal objects answering a Use with their destination.</summary>
    public void Portals(TravelRoutes routes, params string[] keys)
    {
        foreach (var key in keys)
        {
            var e = routes.Edge(key);
            Object(e.Guid, e.Name, e.Wcid, e.Cell, e.Origin);
            _drops[e.Guid] = (e.DestCell, e.DestOrigin);
        }
    }

    /// <summary>The ground's entry portal, dropping at the ground's arrival.</summary>
    public void Entry(GroundEntry entry, HuntingGround ground)
    {
        Object(entry.Guid, ground.Name + " entry", entry.Wcid, entry.Cell, entry.Origin);
        _drops[entry.Guid] = (ground.Arrival.Cell, ground.Arrival.Origin);
    }

    public JourneyContext Context(HuntingGround ground, Lifestone.Client.Session.Vitals.CreaturePaces? paces = null) =>
        new() { Character = "Winnie", Account = "test", Log = R.H.Log, Travel = Travel, Spells = Caster.Table, Dungeon = ground, Paces = paces };

    /// <summary>M6 4 F6: called with each targeted cast the server is asked for (spell id, target guid).</summary>
    public Action<uint, uint>? CastAt { get; set; }

    /// <summary>M6 14 the Zefir: true for a targeted spell the server answers with a projectile (created 1 m from her, flying at the target): the cast is confirmed by it, as live; null or false: no projectile (the rig's default).</summary>
    public Func<uint, bool>? Projectile { get; set; }

    private uint _nextProjectile = 0x80009100;

    private void LaunchToward(uint target)
    {
        var here = R.H.State.Self.Position!.Value;
        var aim = R.H.State.Objects.TryGet(target, out var o) && o.Position is { } at ? Vector3.Normalize(at.Origin - here.Origin) : Vector3.UnitX;
        var guid = _nextProjectile++;
        R.H.R.Reducer.OnCreateObject(
            R.H.R.Context,
            ReducerHarness.Create(guid, ReducerHarness.Stamps(), new AcPosition(here.CellId, here.Origin + aim + new Vector3(0, 0, 1), Quaternion.Identity), state: PhysicsState.Missile | PhysicsState.Gravity, velocity: aim * 15f),
            isUpdate: false);
    }

    /// <summary>M6 6 R1F: the targeted spells ACE refuses for a missing component (UseDone 0x0400, as live round 1's Flame Bolt IV); read when the cast is answered, 1 s after it is sent.</summary>
    public HashSet<uint> NoComponents { get; } = [];

    /// <summary>M6 6 R1F: while true, a combat mode change is not answered (the mode stays where it is).</summary>
    public bool ModeRefused { get; set; }

    /// <summary>M6 4 F6: the death ACE sends before the respawn's teleport: the victim notice 0x01AC with <paramref name="text"/> and the health at 0.</summary>
    public void Die(string text)
    {
        R.Server((p, seq) => AceM3Writers.VictimNotification(p, seq, text));
        Caster.SetHealth(0);
    }

    /// <summary>M6 4 F6: an attackable creature <paramref name="metres"/> east of the player that attacks it (a defender notice naming it).</summary>
    public void Attacker(uint guid, string name, float metres, uint wcid = 236)
    {
        var here = R.H.State.Self.Position!.Value;
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), new AcPosition(here.CellId, here.Origin + new Vector3(metres, 0, 0), Quaternion.Identity), name: name);
        R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { Name = name, WeenieClassId = wcid, ItemType = HostileReach.CreatureItemType, ObjectDescriptionFlags = HostileReach.AttackableFlag } }, isUpdate: false);
        R.Server((p, seq) => AceM3Writers.DefenderNotification(p, seq, name, 4, 0.1, 3, 4, false, 0));
    }

    /// <summary>M6 14 run past: the server's position report of <paramref name="guid"/> (a creature running).</summary>
    public void MoveObject(uint guid, uint cell, Vector3 at) =>
        R.H.R.Reducer.OnUpdatePosition(R.H.R.Context, new UpdatePositionMsg(guid, PositionFlags.Grounded, new AcPosition(cell, at, Quaternion.Identity), null, null, 1, ++_positionStamp, 1, 1));

    /// <summary>M6 14 retreat attackers: a hit of <paramref name="damage"/> from <paramref name="name"/> (the server's damage notice; the walk's measured floor reads it).</summary>
    public void Hit(string name, uint damage, uint damageType = 0x20) =>
        R.Server((p, seq) => AceM3Writers.DefenderNotification(p, seq, name, damageType, 0.1, damage, 4, false, 0));

    /// <summary>M6 14 entry recharge: an attackable creature <paramref name="metres"/> east of the player that has not attacked it (no notice).</summary>
    public void Bystander(uint guid, string name, float metres, uint wcid = 236)
    {
        var here = R.H.State.Self.Position!.Value;
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), new AcPosition(here.CellId, here.Origin + new Vector3(metres, 0, 0), Quaternion.Identity), name: name);
        R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { Name = name, WeenieClassId = wcid, ItemType = HostileReach.CreatureItemType, ObjectDescriptionFlags = HostileReach.AttackableFlag } }, isUpdate: false);
    }

    /// <summary>M6 4 F6: the creature is gone (dead: the server deletes it from the client's view).</summary>
    public void Kill(uint guid) => R.H.R.Reducer.OnDeleteObject(R.H.R.Context, new DeleteObjectMsg(guid, 1));

    /// <summary>M6 6 FXD2 (6-D4): the UseDone error ACE answers an untargeted recall with (0x0400, 0x049E), and then no teleport; 0: the recall is answered as before.</summary>
    public uint RecallRefusal { get; set; }

    /// <summary>The untargeted recall spells the server answers with a teleport (spell id to the landing).</summary>
    public Dictionary<uint, (uint Cell, Vector3 At)> RecallTo { get; } = [];

    /// <summary>The session view the rig's stations and legs step over.</summary>
    public IGameSessionView Session => new View(R, Live);

    /// <summary>
    /// M6 4 F1: the journey's own travel over this rig (<see cref="JourneyTravel"/>, its runner the rig's moving seam) and its context (attached: <see cref="JourneyContext.Travel"/>), as a journey's host builds them; the grounds the travel
    /// reads for the body's ground are <paramref name="grounds"/>.
    /// </summary>
    public (JourneyTravel Travel, JourneyContext Context) Journey(HuntingGround ground, params HuntingGround[] grounds)
    {
        var facts = new JourneyFacts();
        var travel = new JourneyTravel("Winnie", facts, R.H.Log, new Runner(Travel), dungeon: ground);
        var context = new JourneyContext { Character = "Winnie", Account = "test", Log = R.H.Log, Travel = travel, Spells = Caster.Table, Dungeon = ground, Facts = facts, Grounds = grounds.Length == 0 ? [ground] : grounds };
        return (travel, context);
    }

    /// <summary>Steps a travel leg over the rig until it ends (or <paramref name="maxSeconds"/>).</summary>
    public TravelStatus RunLeg(ITravelLeg leg, double maxSeconds = 900, double dt = 0.25)
    {
        var end = Now + maxSeconds;
        var status = TravelStatus.Running;
        while (status == TravelStatus.Running && Now < end)
        {
            R.Tick(dt);
            Pump();
            R.Tick();
            status = leg.Step(new View(R, Live), Now);
        }

        Assert.NotEqual(TravelStatus.Running, status);
        return status;
    }

    /// <summary>The ground's exit portal (found by its weenie near its origin, guid <paramref name="guid"/>), dropping at its destination.</summary>
    public void Exit(HuntingGround ground, uint guid, Vector3 drop)
    {
        var exit = ground.Exit!;
        Object(guid, ground.Name + " exit", exit.Wcid, exit.Cell, exit.Origin!.Value);
        _drops[guid] = (exit.Destination, drop);
    }

    /// <summary>The host's runner as the rig's moving seam.</summary>
    private sealed class Runner(MovingTravel travel) : IJourneyRunnerHost
    {
        public ITravelLeg Run(IGameSessionView session, TravelRequest request, double now) => travel.GoTo(session, request, now);
    }

    public IGameSessionView SessionView() => new View(R, Live);

    /// <param name="station">The station.</param>
    /// <param name="context">The journey's context.</param>
    /// <param name="maxSeconds">The bound.</param>
    /// <param name="dt">The step.</param>
    /// <param name="each">M6 14 entry recharge: called after each step (the test's world moves: an attacker arrives).</param>
    public (IStationRun Run, ScenarioStatus Status) RunStation(IStation station, JourneyContext context, double maxSeconds = 600, double dt = 0.25, Action? each = null)
    {
        var run = station.Start(context);
        var end = Now + maxSeconds;
        var status = ScenarioStatus.Running;
        while (status == ScenarioStatus.Running && Now < end)
        {
            R.Tick(dt);
            Pump();
            R.Tick();
            status = run.Step(new View(R, Live), Now);
            each?.Invoke();
        }

        Assert.NotEqual(ScenarioStatus.Running, status);
        return (run, status);
    }

    /// <summary>M6 8 G: steps one portal hop over the rig until it ends.</summary>
    public (PortalHopLeg Leg, ScenarioStatus Status) RunHop(PortalHopLeg leg, double maxSeconds = 600, double dt = 0.25)
    {
        var end = Now + maxSeconds;
        var status = ScenarioStatus.Running;
        while (status == ScenarioStatus.Running && Now < end)
        {
            R.Tick(dt);
            Pump();
            R.Tick();
            status = leg.Step(new View(R, Live), Now);
        }

        Assert.NotEqual(ScenarioStatus.Running, status);
        return (leg, status);
    }

    /// <summary>Moves the player as an ordinary position update (a walk).</summary>
    public void Put(uint cell, Vector3 at) =>
        R.H.R.Reducer.OnUpdatePosition(R.H.R.Context, new UpdatePositionMsg(WorldActionRig.Player, PositionFlags.Grounded, new AcPosition(cell, at, Quaternion.Identity), null, null, 1, ++_positionStamp, _teleportStamp, 1));

    internal void Teleport(uint cell, Vector3 at, double unhideAfter = 1.0)
    {
        Events.Add("teleport:" + cell.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        R.H.R.Reducer.OnPlayerTeleport(R.H.R.Context, new PlayerTeleportMsg(++_teleportStamp));
        Put(cell, at);
        Body = new CellFrame(cell, new AcFrame(at, Quaternion.Identity)); // the host re-places the body where the server put it
        After(unhideAfter, () => R.H.Unhide(++_stateStamp));
    }

    /// <summary>M6 14: where the host's own body stands (<see cref="IJourneyWalks.BodyPosition"/> of <see cref="ContextWithBody"/>'s host), or null: the server's position stands in.</summary>
    public CellFrame? Body { get; set; }

    /// <summary>M6 14: called with each combat mode the client asks for, before it is answered.</summary>
    public Action<int>? OnModeChange { get; set; }

    /// <summary>M6 14: <see cref="Context"/> whose host gives a body of its own (<see cref="IJourneyWalks"/>): the walks' checks read <see cref="Body"/>, the server's position is the rig's last report.</summary>
    public JourneyContext ContextWithBody(HuntingGround ground) =>
        new() { Character = "Winnie", Account = "test", Log = R.H.Log, Travel = Travel, Spells = Caster.Table, Dungeon = ground, Runners = new BodyHost(this) };

    /// <summary>M6 14: the host's runner and its body (the Godot host's player, the headless host's walker).</summary>
    private sealed class BodyHost(RungRig rig) : IJourneyRunnerHost, IJourneyWalks
    {
        public ITravelLeg Run(IGameSessionView session, TravelRequest request, double now) => rig.Travel.GoTo(session, request, now);

        public IFightWalk? FightWalk(JourneyContext context, HuntingGround ground) => null;

        public IHuntFollow? HuntFollow(JourneyContext context) => null;

        public CellFrame? BodyPosition(JourneyContext context) => rig.Body ?? rig.R.H.State.Self.Position;
    }

    private void Object(uint guid, string name, uint wcid, uint cell, Vector3 at)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), new AcPosition(cell, at, Quaternion.Identity), name: name);
        R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis with { Weenie = basis.Weenie with { Name = name, WeenieClassId = wcid } }, isUpdate: false);
    }

    private void Chat(string text) => R.Raw(AceWireWriters.ServerMessage(text, 0));

    internal void After(double seconds, Action what) => _later.Add((Now + seconds, what));

    private void Pump()
    {
        if (_casterVitals)
        {
            Caster.Pump();
        }

        var sends = R.H.Net.Sends;
        while (_seen < sends.Count)
        {
            var send = sends[_seen++];
            if (_casterVitals && ((send.Kind == FakeSendKind.CastTargetedSpell && send.Guid == WorldActionRig.Player) || (send.Kind == FakeSendKind.Use && send.Guid is ScriptedCaster.ManaPotionGuid or ScriptedCaster.ManaTinctureGuid)))
            {
                Events.Add((send.Kind == FakeSendKind.Use ? "drink:" : "press:") + (send.Kind == FakeSendKind.Use ? send.Guid : (uint)send.Index).ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
                continue; // the scripted caster answers it
            }

            switch (send.Kind)
            {
                case FakeSendKind.ChangeCombatMode:
                    Events.Add((ModeRefused ? "mode-unanswered:" : "mode:") + (int)send.Amount);
                    OnModeChange?.Invoke((int)send.Amount);
                    if (!ModeRefused)
                    {
                        R.CombatModeIs((int)send.Amount);
                    }

                    break;
                case FakeSendKind.Use:
                    Events.Add("use:" + send.Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
                    if (send.Guid == GemGuid)
                    {
                        After(2.0, () => Teleport(0xF6820033, new Vector3(145.7f, 49.9f, 58f)));
                    }
                    else if (_drops.TryGetValue(send.Guid, out var drop) && !Silent.Contains(send.Guid))
                    {
                        var to = DropOverride.TryGetValue(send.Guid, out var other) ? other : drop;
                        After(1.0, () => Teleport(to.Cell, to.At));
                    }

                    R.UseDone();
                    break;
                case FakeSendKind.CastTargetedSpell:
                    var spell = (uint)send.Index;
                    var target = send.Guid;
                    Events.Add($"cast:{spell}@{target:X8}");
                    CastAt?.Invoke(spell, target);
                    if (Projectile?.Invoke(spell) == true)
                    {
                        After(0.9, () => LaunchToward(target)); // M6 14 the Zefir: the server's projectile, created by her and flying at the target, before the cast's end
                    }

                    After(1.0, () =>
                    {
                        if (spell == 47 && _drops.ContainsKey(target))
                        {
                            Chat("You have successfully linked with the portal.");
                        }

                        R.UseDone(NoComponents.Contains(spell) ? 0x0400u : 0u);
                    });
                    break;
                case FakeSendKind.CastUntargetedSpell:
                    Events.Add("cast:" + (uint)send.Guid);
                    After(1.0, () => R.UseDone(RecallRefusal));
                    if (RecallRefusal == 0 && RecallTo.TryGetValue((uint)send.Guid, out var landing))
                    {
                        After(2.0, () => Teleport(landing.Cell, landing.At));
                    }

                    break;
            }
        }

        for (var i = 0; i < _later.Count; i++)
        {
            if (_later[i].Due <= Now)
            {
                var due = _later[i].What;
                _later.RemoveAt(i--);
                due();
            }
        }
    }

    /// <summary>
    /// M6 14 caught on the run: the bodies the client draws, by guid (a creature drawn where the server's last report does not have it: a monster following her sends no position); empty: the rig draws none and the server's reports
    /// answer (<see cref="LivePositions.ReportsOnly"/>).
    /// </summary>
    public Dictionary<uint, CellFrame> Drawn { get; } = [];

    /// <summary>M6 14 caught on the run: <paramref name="guid"/> drawn <paramref name="metres"/> east of where the server has her (its report unchanged).</summary>
    public void DrawAt(uint guid, float metres)
    {
        var here = R.H.State.Self.Position!.Value;
        Drawn[guid] = new CellFrame(here.CellId, new AcFrame(here.Origin + new Vector3(metres, 0, 0), Quaternion.Identity));
    }

    private LivePositions? _live;

    /// <summary>The views' live positions: the drawn bodies once any is drawn, else none (the reports answer).</summary>
    private LivePositions? Live => Drawn.Count == 0 ? null : _live ??= new LivePositions(new DrawnBodies(this), R.H.R.Clock);

    private sealed class DrawnBodies(RungRig rig) : ILiveBodies
    {
        public bool TryGetBody(uint guid, out LiveBody body)
        {
            var drawn = rig.Drawn.TryGetValue(guid, out var at);
            body = drawn ? new LiveBody(at, InWorld: true, Hidden: false, ServerPositionPending: false) : default;
            return drawn;
        }
    }

    private sealed class View(WorldActionRig rig, LivePositions? live) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;

        public LivePositions Live => live ?? LivePositions.ReportsOnly;
    }

    /// <summary>The travel seam: each request moves the body to its point (an object goal: one metre beside the object) on the first step, and arrives.</summary>
    public sealed class MovingTravel(RungRig rig) : IJourneyTravel
    {
        public List<TravelRequest> Requests { get; } = [];

        /// <summary>A failure for a request the seam refuses (the walk ends Failed there, the body unmoved), or null to walk it (M6 4 w6a: a front point in a wall).</summary>
        public Func<TravelRequest, string?>? Refuse { get; set; }

        /// <summary>M6 4 F5: a request the server answers by teleporting the body (the touch of a portal on the walk): the landing, or null to walk it. The walk then fails <c>travel:run:moved</c>.</summary>
        public Func<TravelRequest, (uint Cell, Vector3 At)?>? TeleportOn { get; set; }

        /// <summary>M6 4 F6: the death notice of a request whose teleport (<see cref="TeleportOn"/>) is the respawn after a death (<see cref="RungRig.Die"/> before it), or null.</summary>
        public Func<TravelRequest, string?>? DiesOn { get; set; }

        /// <summary>M6 14: for a request that arrives, where the server's last report of her stays (a second of her run behind her body, which stands at the point), or null: the server has her at the point.</summary>
        public Func<TravelRequest, (uint Cell, Vector3 At)?>? EchoAt { get; set; }

        /// <summary>M6 14: for a request that arrives, the teleport the server sends that many seconds after the walk ended (its sweep of her last reports met a portal), or null.</summary>
        public Func<TravelRequest, (uint Cell, Vector3 At, double After)?>? TeleportAfterArrival { get; set; }

        /// <summary>M6 4 F6: true for a request whose walk keeps running (never arrives) until the station stops it; called once at the request (the test may set an attacker on the body there).</summary>
        public Func<TravelRequest, bool>? WalkingOn { get; set; }

        /// <summary>
        /// M6 14 run past: for a request that runs a while, its seconds: the body runs at 4 m/s along x in its cell (away from the landblock's nearer edge), then arrives at the point; null: it arrives at once. Called once at the request.
        /// </summary>
        public Func<TravelRequest, double?>? RunsFor { get; set; }

        /// <summary>M6 14 run past: called on each step of a run (<see cref="RunsFor"/>) with its seconds so far and whether it arrives on this step.</summary>
        public Action<TravelRequest, double, bool>? OnRun { get; set; }

        public bool Busy => false;

        public ITravelLeg GoTo(IGameSessionView session, TravelRequest request, double now)
        {
            Requests.Add(request);
            rig.Events.Add("travel:" + request.Purpose);
            return new Leg(rig, request, Refuse?.Invoke(request), TeleportOn?.Invoke(request), DiesOn?.Invoke(request), WalkingOn?.Invoke(request) == true, RunsFor?.Invoke(request));
        }

        private sealed class Leg(RungRig rig, TravelRequest request, string? refused, (uint Cell, Vector3 At)? teleport, string? death, bool walking, double? runs = null) : ITravelLeg
        {
            private bool _moved;
            private (double At, uint Cell, Vector3 From)? _runStart;

            public TravelMeans Means => TravelMeans.Run;

            public string? Failure => refused ?? (teleport is not null ? "travel:run:moved: the server moved the player during the run" : null);

            public string Fields => "route_m=1.0";

            public TravelStatus Step(IGameSessionView session, double now)
            {
                if (refused is not null)
                {
                    return TravelStatus.Failed;
                }

                if (teleport is { } landing)
                {
                    if (!_moved)
                    {
                        _moved = true;
                        if (death is not null)
                        {
                            rig.Die(death);
                        }

                        rig.Teleport(landing.Cell, landing.At);
                    }

                    return TravelStatus.Failed;
                }

                if (walking)
                {
                    return TravelStatus.Running;
                }

                if (runs is { } seconds && !_moved)
                {
                    var here = session.State.Self.Position!.Value;
                    _runStart ??= (now, here.CellId, here.Origin);
                    var (start, cell, from) = _runStart.Value;
                    var ran = now - start;
                    var arrives = ran >= seconds;
                    var direction = from.X > 96 ? -1f : 1f;
                    _ran = ran;
                    if (!arrives)
                    {
                        rig.Put(cell, from + new Vector3(direction * (float)Math.Min(4.0 * ran, 90), 0, 0));
                        rig.Travel.OnRun?.Invoke(request, ran, false);
                        return TravelStatus.Running;
                    }
                }

                if (!_moved)
                {
                    _moved = true;
                    var (cell, at) = request.ObjectGuid != 0 && session.State.Objects.TryGet(request.ObjectGuid, out var o) && o.Position is { } op
                        ? (op.CellId, op.Origin + new Vector3(1, 0, 0))
                        : (request.Destination.Location.Position.CellId, request.Destination.Location.Position.Local);
                    var echo = rig.Travel.EchoAt?.Invoke(request);
                    rig.Put(echo?.Cell ?? cell, echo?.At ?? at);
                    rig.Body = new CellFrame(cell, new AcFrame(at, Quaternion.Identity));
                    if (rig.Travel.TeleportAfterArrival?.Invoke(request) is { } later)
                    {
                        rig.After(later.After, () => rig.Teleport(later.Cell, later.At));
                    }

                    if (runs is not null)
                    {
                        rig.Travel.OnRun?.Invoke(request, _ran, true); // M6 14 run past: the arrival, with the body at the point
                    }
                }

                return TravelStatus.Arrived;
            }

            private double _ran;

            public void Pause()
            {
            }

            public void Resume(double now)
            {
            }

            public void Dispose()
            {
            }
        }
    }
}

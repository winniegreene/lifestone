using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 item 4 t3b: the autopilot's rung change. Proved: the travel rule (<see cref="AutopilotTravelRule"/>) sends a routed ground (the island's, by the travel file's route), an untieable entry and a known-untied tieable one by
/// <c>rung-change</c>, keeps <c>den-travel</c> for a tied ground, an unknown tie the book can recall, an outdoor ground and a route with no portal, and leaves the named gap only for an untied ground no route reaches (the den); the way home
/// is Lifestone Recall when the book holds it (or is unread), else <c>rung-change</c> direction=home where a way home by portals exists; the checkpoint carries a tie across runs (<c>m6-tie-memory</c>); and over the scripted ACE the Travel state
/// runs the station, checks the arrival before the fight, stops by the station's own <c>fail=</c>, and goes home by portals below Lifestone Recall.
/// </summary>
public sealed class AutopilotRungTravelTests(ITestOutputHelper output)
{
    private static readonly TravelRoutes Routes = TravelRoutes.Default;

    private static HuntingGround Temple40 => GroundCatalog.Get(Temple);

    /// <summary>The Burrow as the travel file's rows give it (item 3's profile is not on m6/4): T2's stand-in pattern, entry 22663 (facts.md section 3), arrival 0x57480210, exit 22664 to 0xF9820039.</summary>
    private static HuntingGround Burrow => Temple40 with
    {
        Key = "tusker-burrow",
        Name = "the Tusker Burrow",
        Landblocks = [0x5748],
        Arrival = new GroundArrival(0x57480210, new Vector3(148.378f, -2.8159f, 0.005f), 1, 0),
        Portal = null,
        Entry = new GroundEntry(22663, 0x7F981000, 0xF9810040, new Vector3(176.323f, 186.044f, 12.5f), 0xF9810040, new Vector3(176.3f, 183.0f, 12.5f), null, null, false),
        Exit = new GroundExitSection(GroundExitKind.Portal, 22664, 0x57480100, new Vector3(119.843f, -134.228f, -29.995f), 0xF9820039, null),
    };

    private static RungRouteInput In(HuntingGround g, int level, PortalTie tie = PortalTie.Unknown, RungWhere where = RungWhere.Home, string place = "holtburg", bool lifestoneRecall = false, bool portalRecall = false, bool portalTie = false) =>
        new(g, RungDirection.Out, where, place, level, lifestoneRecall, portalRecall, portalTie, tie, false);

    // ------------------------------------------------------------------------------------------------ the rule out

    [Fact]
    public void A_tied_ground_keeps_den_travels_recall_and_an_unknown_tie_the_book_recalls_is_tried()
    {
        var tied = AutopilotTravelRule.Out(In(Temple40, 45, PortalTie.Tied, portalRecall: true), bookRead: true, Routes);
        Assert.Equal(TravelWay.DenTravel, tied.Way);
        Assert.Contains("known tied", tied.Reason, StringComparison.Ordinal);

        var tried = AutopilotTravelRule.Out(In(Temple40, 45, portalRecall: true), bookRead: true, Routes);
        Assert.Equal(TravelWay.DenTravel, tried.Way);
        Assert.Contains("den-travel tries the recall", tried.Reason, StringComparison.Ordinal);

        var unread = AutopilotTravelRule.Out(In(Temple40, 45), bookRead: false, Routes); // no absence is guessed from a book not read
        Assert.Equal(TravelWay.DenTravel, unread.Way);

        var den = AutopilotTravelRule.Out(In(GroundCatalog.Get(Den), 60), bookRead: true, Routes); // the den's route is the recall alone: its own station
        Assert.Equal(TravelWay.DenTravel, den.Way);
        Assert.Equal("recall-or-tie", den.Route);

        Assert.Equal(TravelWay.DenTravel, AutopilotTravelRule.Out(In(GroundCatalog.Get("low-b-belt"), 5), bookRead: true, Routes).Way); // outdoor: the walk
    }

    [Fact]
    public void A_routed_or_untied_ground_goes_by_the_rung_change_and_an_untied_ground_no_route_reaches_is_the_gap()
    {
        var burrow = AutopilotTravelRule.Out(In(Burrow, 11), bookRead: true, Routes); // untieable entry, no tie record: the Facility Hub's Burrow portal is 15, so by the Refuge
        Assert.Equal(TravelWay.RungChange, burrow.Way);
        Assert.Equal("by-refuge", burrow.Route);
        Assert.Equal("by-facility-hub", AutopilotTravelRule.Out(In(Burrow, 15), bookRead: true, Routes).Route); // M6 7 J: the hub's Burrow portal admits 15, and its walk is proven (FacilityHubWalkDatTests)

        var untied = AutopilotTravelRule.Out(In(Temple40, 45, PortalTie.Untied, portalRecall: true, portalTie: true), bookRead: true, Routes);
        Assert.Equal(TravelWay.RungChange, untied.Way);
        Assert.Equal("by-fort-tethana", untied.Route);

        var noRecall = AutopilotTravelRule.Out(In(Temple40, 45), bookRead: true, Routes); // unknown, and the book lacks Primary Portal Recall: the route (it ties the entry)
        Assert.Equal(TravelWay.RungChange, noRecall.Way);

        var gated = AutopilotTravelRule.Out(In(Temple40, 30, PortalTie.Untied), bookRead: true, Routes); // the entry's 40: the station names the gate before any walk
        Assert.Equal(TravelWay.RungChange, gated.Way);
        Assert.Contains("its plan stops by name (gate:", gated.Reason, StringComparison.Ordinal);

        var gap = AutopilotTravelRule.Out(In(GroundCatalog.Get(Den), 60, PortalTie.Untied, portalRecall: true), bookRead: true, Routes);
        Assert.Equal(TravelWay.Gap, gap.Way);
        Assert.Contains("TODO(m5-portal-retie-route)", gap.Reason, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ the rule home

    [Fact]
    public void Home_is_lifestone_recall_when_the_book_holds_it_else_the_rung_change_by_portals_from_a_routed_place()
    {
        static RungRouteInput Home(string place, bool recall, RungWhere where = RungWhere.Elsewhere) => In(Burrow, 12, where: where, place: place, lifestoneRecall: recall) with { Direction = RungDirection.Home };

        Assert.Equal(TravelWay.Recall, AutopilotTravelRule.Home(Home("island", recall: true), bookRead: true, Routes).Way);
        Assert.Equal(TravelWay.Recall, AutopilotTravelRule.Home(Home("island", recall: false), bookRead: false, Routes).Way); // unread: the recall as before

        var island = AutopilotTravelRule.Home(Home("island", recall: false), bookRead: true, Routes);
        Assert.Equal(TravelWay.RungChange, island.Way);
        Assert.Contains("lacks Lifestone Recall (D43)", island.Reason, StringComparison.Ordinal);

        Assert.Equal(TravelWay.RungChange, AutopilotTravelRule.Home(Home("facility-hub", recall: false), bookRead: true, Routes).Way);
        var fort = AutopilotTravelRule.Home(Home("fort-tethana", recall: false), bookRead: true, Routes); // M6 4 F1 (4-R8): its way home's first portal is behind the lever gate
        Assert.Equal(TravelWay.Recall, fort.Way);
        Assert.Contains("TODO(door-lever)", fort.Reason, StringComparison.Ordinal);

        var nowhere = AutopilotTravelRule.Home(Home("0x1234", recall: false), bookRead: true, Routes);
        Assert.Equal(TravelWay.Recall, nowhere.Way);
        Assert.Contains("TODO(m6-way-home)", nowhere.Reason, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ the tie memory

    [Fact]
    public void The_checkpoint_carries_a_tie_across_runs_for_the_same_character_only()
    {
        var store = new MemoryCheckpointStore
        {
            Json = new AutopilotCheckpoint { Character = "Winnie", CharacterGuid = 0x50000001, Ground = Temple, Completed = true, Stop = "rung-moved:temple-40", PortalTied = [Temple] }.ToJson(),
        };

        Assert.Equal([Temple], AutopilotCheckpoint.ReadTieMemory(store, "Winnie", 0x50000001, out var note)); // a finished run's file still gives it
        Assert.Contains("tie memory: tusker-temple-fort-tethana tied", note, StringComparison.Ordinal);
        Assert.Empty(AutopilotCheckpoint.ReadTieMemory(store, "Winnie", 0x50000002, out note)); // re-created under the same name
        Assert.Contains("not this character's", note, StringComparison.Ordinal);
        Assert.Empty(AutopilotCheckpoint.ReadTieMemory(store, "BSD Mage", 0x50000001, out _));

        store.Json = new AutopilotCheckpoint { Character = "Winnie", CharacterGuid = 0x50000001 }.ToJson(); // a file written before the field
        Assert.Empty(AutopilotCheckpoint.ReadTieMemory(store, "Winnie", 0x50000001, out note));
        Assert.Null(note);
        store.Json = "{ torn";
        Assert.Empty(AutopilotCheckpoint.ReadTieMemory(store, "Winnie", 0x50000001, out note));
        Assert.StartsWith("tie memory: none (torn", note, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tie_one_run_knew_is_read_into_the_next_runs_facts_so_its_trip_recalls()
    {
        var store = new MemoryCheckpointStore();
        var first = new ApWorld(Temple, output) { Prime = c => { c.Facts.LifestoneTied = true; c.Facts.SetPortalTie(Temple, PortalTie.Tied); } };
        first.OwnKit();
        var one = first.Autopilot(new AutopilotOptions(), store);
        first.Run(one, a => a.State == AutopilotState.Fight);
        one.RequestStop("test");
        first.Run(one);
        Assert.Contains("\"portalTied\": [", store.Json, StringComparison.Ordinal);

        var second = new ApWorld(Temple, output) { Prime = c => c.Facts.LifestoneTied = true };
        second.OwnKit();
        var two = second.Autopilot(new AutopilotOptions(), store);
        second.Run(two, a => a.State == AutopilotState.Fight);

        Assert.Contains(second.Lines, l => l.Contains("checkpoint: tie memory: tusker-temple-fort-tethana tied", StringComparison.Ordinal) && l.EndsWith("carried into the journey's facts: tusker-temple-fort-tethana", StringComparison.Ordinal));
        Assert.Contains(" portal_tie=proven ", two.LastAudit, StringComparison.Ordinal);
        Assert.Equal(["buff", "den-travel", "den-fight"], Kinds(second));
    }

    // ------------------------------------------------------------------------------------------------ the Travel state over the scripted ACE

    /// <summary>Scripts the rung change: out stands at the ground's arrival (or at <paramref name="outTo"/>), home at the home lifestone; <paramref name="fail"/> fails it with that <c>fail=</c>.</summary>
    private static void ScriptRung(ApWorld w, (uint Cell, Vector3 At)? outTo = null, string? fail = null) =>
        w.Factory.Script = spec =>
        {
            if (spec.Kind != StationKinds.RungChange)
            {
                return null;
            }

            var home = spec.Arguments[RungChangeStation.DirectionArgument] == "home";
            if (fail is not null)
            {
                return new ScriptedRun(spec.Name, _ => ScenarioStatus.Failed, () => $"ground={w.Ground.Key} ok=no fail={fail} direction={(home ? "home" : "out")}") { Failure = fail + ": scripted" };
            }

            var arrival = w.Ground.ArrivalDestination().Location.Position;
            var to = home ? (w.Ground.Home.LifestoneCell, w.Ground.Home.LifestonePoint) : outTo ?? (arrival.CellId, arrival.Local);
            return new ScriptedRun(spec.Name, _ => { w.Trip.Vendor.StandAt(to.Item1, to.Item2); return ScenarioStatus.Succeeded; }, () => $"ground={w.Ground.Key} ok=yes fail=none direction={(home ? "home" : "out")}");
        };

    [Fact]
    public void A_routed_ground_known_untied_runs_the_rung_change_and_the_fight_after_the_check()
    {
        var w = new ApWorld(Temple, output) { Prime = c => { c.Facts.LifestoneTied = true; c.Facts.SetPortalTie(Temple, PortalTie.Untied); } };
        w.OwnKit();
        w.Level(45); // past the entry's 40 (the level's raises make the start's forced trip first)
        var book = w.Trip.Vendor.State.Self.Spellbook;
        book.IsKnown = true;
        book.Learn(Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell); // home first from Fort Tethana's lifestone (0x2780 is in no travel place)
        ScriptRung(w);
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight);

        Assert.Equal(["buff", "rung-change", "den-fight"], Kinds(w).SkipWhile(k => k != "buff"));
        Assert.Equal("out", w.Factory.Made.Single(s => s.Kind == StationKinds.RungChange).Arguments[RungChangeStation.DirectionArgument]);
        Assert.StartsWith("autopilot audit: place=home bind=proven portal_tie=route ", autopilot.LastAudit, StringComparison.Ordinal);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit portal_tie: ACE refused a Primary Portal Recall untied (0x04A3)", StringComparison.Ordinal) && l.Contains("fix=none: the rung-change station takes the climber", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot travel: direction=out way=rung-change ground=tusker-temple-fort-tethana ", StringComparison.Ordinal) && l.Contains(" route=by-fort-tethana;", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot travel: check after the rung change: present in tusker-temple-fort-tethana", StringComparison.Ordinal));
    }

    [Fact]
    public void A_den_travel_refused_untied_on_a_routed_ground_turns_to_the_rung_change_not_the_gap()
    {
        var w = new ApWorld(Temple, output) { Prime = c => c.Facts.LifestoneTied = true };
        w.OwnKit();
        w.Factory.RefusePortalRecall = true;
        ScriptRung(w);
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight);

        Assert.Equal(["buff", "den-travel", "rung-change", "den-fight"], Kinds(w));
        Assert.Null(autopilot.StopName);
        Assert.DoesNotContain(w.Lines, l => l.Contains("TODO(m5-portal-retie-route)", StringComparison.Ordinal) && l.Contains("gap:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_rung_change_that_ends_well_away_from_the_ground_stops_by_the_check_after_it()
    {
        var w = new ApWorld(Temple, output) { Prime = c => { c.Facts.LifestoneTied = true; c.Facts.SetPortalTie(Temple, PortalTie.Untied); } };
        w.OwnKit();
        ScriptRung(w, outTo: (w.Ground.Home.LifestoneCell, w.Ground.Home.LifestonePoint));
        var autopilot = w.Autopilot(new AutopilotOptions());

        Assert.Equal(ScenarioStatus.Failed, w.Run(autopilot));
        Assert.Equal(AutopilotScenario.RungNotInGroundStop, autopilot.StopName);
        Assert.DoesNotContain("den-fight", Kinds(w));
    }

    [Fact]
    public void A_rung_change_that_fails_by_its_plans_named_stop_stops_the_run_by_that_name_without_a_recovery()
    {
        var w = new ApWorld(Temple, output) { Prime = c => { c.Facts.LifestoneTied = true; c.Facts.SetPortalTie(Temple, PortalTie.Untied); } };
        w.OwnKit();
        ScriptRung(w, fail: "gate:entry:tusker-temple-fort-tethana:30<40");
        var autopilot = w.Autopilot(new AutopilotOptions());

        Assert.Equal(ScenarioStatus.Failed, w.Run(autopilot));
        Assert.StartsWith("failed:travel:rung-change:gate:entry:tusker-temple-fort-tethana:30", autopilot.StopName, StringComparison.Ordinal);
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("autopilot recovery after", StringComparison.Ordinal));
        Assert.Equal(["buff", "rung-change"], Kinds(w));
    }

    [Fact]
    public void An_outdoor_ground_goes_home_on_foot_even_with_lifestone_recall_in_the_book()
    {
        var outskirts = GroundCatalog.Get("holtburg-outskirts");
        var input = In(outskirts, 17, where: RungWhere.AtGround, place: "0xA8B5", lifestoneRecall: true);
        var home = AutopilotTravelRule.Home(input, bookRead: true, Routes);
        Assert.Equal(TravelWay.RungChange, home.Way);
        Assert.Contains("outdoor ground", home.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_body_in_the_outskirts_goes_home_on_foot_whatever_the_runs_ground_M6_7_AV()
    {
        // live, 2026-11-06: a run on the Lodge, standing in 0xA8B5 with the recall in the book, cast the recall (refused for components) 180 m from town
        var lodge = GroundCatalog.Get("tusker-lodge");
        var outskirts = GroundCatalog.Get("holtburg-outskirts");
        Assert.NotEqual(GroundKind.Outdoor, lodge.Kind);
        var input = In(lodge, 17, where: RungWhere.Elsewhere, place: "0xA8B5", lifestoneRecall: true) with { Direction = RungDirection.Home, BodyGround = outskirts };
        var home = AutopilotTravelRule.Home(input, bookRead: true, Routes);

        Assert.Equal(TravelWay.RungChange, home.Way);
        Assert.Equal("walk-home", home.Route);
        Assert.Contains("on foot", home.Reason, StringComparison.Ordinal);
        Assert.Equal(TravelWay.AtHome, AutopilotTravelRule.Home(input with { BodyGround = null, Where = RungWhere.Home }, bookRead: true, Routes).Way); // at home: nothing to walk, and no recall from home (M6 7 BC)
    }

    [Fact]
    public void A_body_at_home_needs_no_way_home_and_never_recalls_from_it_M6_7_BC()
    {
        foreach (var recall in new[] { true, false })
        {
            foreach (var read in new[] { true, false })
            {
                var home = AutopilotTravelRule.Home(In(Burrow, 21, where: RungWhere.Home, lifestoneRecall: recall) with { Direction = RungDirection.Home }, read, Routes);
                Assert.Equal(TravelWay.AtHome, home.Way);
                Assert.Equal("at-home", home.WayName);
                Assert.Contains("stands at home", home.Reason, StringComparison.Ordinal);
            }
        }

        // from anywhere else the rule is unchanged
        Assert.Equal(TravelWay.Recall, AutopilotTravelRule.Home(In(Burrow, 21, where: RungWhere.Elsewhere, place: "0x1234", lifestoneRecall: true) with { Direction = RungDirection.Home }, bookRead: true, Routes).Way);
    }

    [Fact]
    public void A_recall_refused_for_components_is_not_available_this_run_so_the_rule_and_the_input_leave_it_out_M6_7_AV()
    {
        var facts = new JourneyFacts();
        var state = new Lifestone.Client.Session.Contracts.GameState();
        state.Self.Spellbook.IsKnown = true;
        state.Self.Spellbook.Learn(Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell);
        Assert.True(AutopilotTravelRule.Input(state, Burrow, facts, Routes, RungDirection.Home).KnowsLifestoneRecall);

        facts.LifestoneRecallUnavailable = true;
        var input = AutopilotTravelRule.Input(state, Burrow, facts, Routes, RungDirection.Home);
        Assert.False(input.KnowsLifestoneRecall);
        var home = AutopilotTravelRule.Home(input with { Where = RungWhere.Elsewhere, Place = "island" }, bookRead: true, Routes);
        Assert.Equal(TravelWay.RungChange, home.Way); // by portals, as a book below the recall goes
        Assert.Contains("island", home.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_recall_home_refused_for_components_neither_repeats_nor_stops_the_run_it_goes_by_the_rung_change_M6_7_AV()
    {
        var w = new ApWorld(Temple, output) { Prime = c => { c.Facts.LifestoneTied = true; c.Facts.SetPortalTie(Temple, PortalTie.Tied); } };
        w.OwnKit();
        ScriptRung(w);
        w.Factory.RefuseRecallComponents = true;
        var book = w.Trip.Vendor.State.Self.Spellbook;
        book.IsKnown = true;
        book.Learn(DenTravelStation.PortalRecallSpell);
        book.Learn(Lifestone.Client.Session.Fighter.RecallHome.LifestoneRecallSpell);
        w.Trip.Vendor.StandAt(0xF6820033, new Vector3(150, 60, 20)); // the Refuge's landblock, a routed place
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight);

        Assert.Equal(["recall-home", "rung-change-home"], Kinds(w).Take(2).ToArray());
        Assert.Equal(1, Kinds(w).Count(k => k == "recall-home")); // never repeated
        Assert.Contains(w.Lines, l => l.Contains("recall-unavailable:components", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("autopilot recovery", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("autopilot done", StringComparison.Ordinal) && l.Contains("failed:travel:recall-home", StringComparison.Ordinal));
        Assert.Equal(AutopilotState.Fight, autopilot.State);
    }

    [Fact]
    public void Below_lifestone_recall_a_mage_standing_on_the_island_goes_home_by_the_rung_change()
    {
        var w = new ApWorld(Temple, output) { Prime = c => { c.Facts.LifestoneTied = true; c.Facts.SetPortalTie(Temple, PortalTie.Tied); } };
        w.OwnKit();
        ScriptRung(w);
        var book = w.Trip.Vendor.State.Self.Spellbook;
        book.IsKnown = true;
        book.Learn(DenTravelStation.PortalRecallSpell); // a read book with Primary Portal Recall and without Lifestone Recall (the climber below 16, D43)
        w.Trip.Vendor.StandAt(0xF6820033, new Vector3(150, 60, 20)); // the Refuge's landblock, a routed place
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight);

        Assert.Equal("rung-change-home", Kinds(w)[0]);
        Assert.Equal("home", w.Factory.Made[0].Arguments[RungChangeStation.DirectionArgument]);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot travel: direction=home way=rung-change ", StringComparison.Ordinal) && l.Contains(" place=island ", StringComparison.Ordinal));
        Assert.Equal(["rung-change-home", "buff", "den-travel", "den-fight"], Kinds(w));
    }
}

using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// The travel routes as data (M6 item 4 T1, D50): the file loads from its embedded resource; every ladder rung's ground and alternate has a route; the gates come from the portals' rows (Burrow at 10 by the Refuge and at 15 by the Facility Hub, the Holding at 60,
/// a level-9 climber refused the island); a ground with no route is the named stop <c>TODO(m6-ground-route)</c> and not an exception; ways home exist from the Refuge and from every stop town; a missing or unknown field fails naming the file and the JSON path.
/// The numbers' agreement with the pinned world SQL is <c>TravelRoutesSqlTests</c>.
/// </summary>
public sealed class TravelRoutesTests
{
    private static readonly TravelRoutes Routes = TravelRoutes.Default;

    private static readonly string[] IslandGrounds =
        ["tusker-burrow", "tusker-lodge", "tusker-cave", "tusker-cavern", "tusker-abode", "tusker-habitat", "tusker-quarters", "tusker-barracks", "tusker-holding"];

    private static readonly string[] StopTowns = ["fort-tethana", "stonehold", "plateau", "qalabar", "dryreach"];

    [Fact]
    public void The_islands_exit_landblocks_and_Fort_Tethanas_lifestone_belong_to_their_places_and_a_book_without_Lifestone_Recall_there_has_a_way_home()
    {
        // M6 4 t4b: the Burrow's Surface exit drops at 0xF982, the other exits at 0xFA7F, 0xF07F and 0xEA7B (the Holding); the lifestone 0x2780 and Zisaha's room 0x2481 are Fort Tethana's (the SQL rows: TravelRoutesSqlTests).
        foreach (var lb in new[] { "0xF982", "0xFA7F", "0xF07F", "0xEA7B" })
        {
            Assert.Equal("island", Routes.PlaceOf(lb)?.Key);
            Assert.Equal("refuge", Routes.WayHomeFrom(lb, 10).WayHome!.Key);
        }

        foreach (var lb in new[] { "0x2780", "0x2481" })
        {
            Assert.Equal("fort-tethana", Routes.PlaceOf(lb)?.Key);
            Assert.Equal("fort-tethana-to-network", Routes.WayHomeFrom(lb, 40).WayHome!.Steps[0].Edge.Key);
        }
    }

    [Fact]
    public void Every_place_carries_its_source_and_the_strict_loader_reads_it_M6_4_F2()
    {
        Assert.All(Routes.Places, p => Assert.Contains("TravelRoutesSqlTests", p.Source, StringComparison.Ordinal));
        Assert.Contains("guid 0x7A9B4080", Routes.PlaceOf("holtburg")!.Source, StringComparison.Ordinal); // Holtburg's Network portal stands in 0xA9B4

        // a place's unknown field is still refused by name; the source is the one new allowed field
        var json = System.Text.Json.Nodes.JsonNode.Parse(EmbeddedText())!;
        json["places"]![0]!["why"] = "x";
        var e = Assert.Throws<TravelRoutesException>(() => TravelRoutes.Parse(TravelRoutes.FileName, json.ToJsonString()));
        Assert.Contains("$.places[0].why", e.Message, StringComparison.Ordinal);
    }

    /// <summary>M6 8 M: the ways out load (the Cathedral portal's house among them); a way of one point, a duplicated key, a room that is not an env cell, and a way that ends in a room a way out starts from each fail by path.</summary>
    [Fact]
    public void The_ways_out_load_and_a_bad_one_fails_by_its_path()
    {
        var way = Assert.Single(Routes.WaysOut, w => w.Key == "cathedral-portal-house");
        Assert.Contains(0xD8540102u, way.Rooms);
        Assert.Equal(0xD8540103u, way.Way[^1].CellId);

        static TravelRoutesException Bad(Action<System.Text.Json.Nodes.JsonNode> edit)
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(EmbeddedText())!;
            edit(json);
            return Assert.Throws<TravelRoutesException>(() => TravelRoutes.Parse(TravelRoutes.FileName, json.ToJsonString()));
        }

        Assert.Contains("$.ways_out[0].way", Bad(j => j["ways_out"]![0]!["way"]!.AsArray().RemoveAt(1)).Message, StringComparison.Ordinal);
        Assert.Contains("$.ways_out[0].rooms[0]", Bad(j => j["ways_out"]![0]!["rooms"]![0] = "0xD8540019").Message, StringComparison.Ordinal);
        Assert.Contains("duplicated key", Bad(j => j["ways_out"]![1]!["key"] = "cathedral-portal-house").Message, StringComparison.Ordinal);
        Assert.Contains("ends in a room", Bad(j => j["ways_out"]![0]!["rooms"]!.AsArray().Add("0xD8540103")).Message, StringComparison.Ordinal);
    }

    /// <summary>M6 7 Z: the danger map's tables and the runs' via points: read strictly, a generator at (cell x 24, cell y 24) clamped as ACE's, every disc the awareness, the scatter and the bodies.</summary>
    [Fact]
    public void The_danger_cells_and_the_via_points_load_and_a_bad_one_fails_by_its_path()
    {
        Assert.Equal(8, Routes.DangerCells.Count);
        Assert.Equal(506, Routes.DangerCells.Sum(t => t.Cells.Count));
        Assert.All(Routes.DangerCells, t =>
        {
            Assert.Contains("encounter rows of weenie_Class_Id " + t.Weenie.ToString(System.Globalization.CultureInfo.InvariantCulture), t.Source, StringComparison.Ordinal);
            Assert.InRange(t.MinLevel, 8, t.MaxLevel);
        });
        var low = Routes.DangerCells.Single(t => t.Weenie == 22387);
        Assert.Equal((8, 30), (low.MinLevel, low.MaxLevel));
        Assert.Equal(36.48f, low.ClearMetres, 2);
        Assert.Contains(new TravelDangerCell(0xF882, 6, 1), low.Cells); // on the straight line to the Burrow
        Assert.Equal(new System.Numerics.Vector2((0xF8 * 192f) + 144f, (0x82 * 192f) + 24f), new TravelDangerCell(0xF882, 6, 1).Global);
        Assert.Equal(new System.Numerics.Vector2(0.5f, 168f), new TravelDangerCell(0xF882, 0, 7).Local); // the low clamp; 7 * 24 is inside the block

        foreach (var ground in new[] { "tusker-burrow", "tusker-lodge" })
        {
            var refuge = Routes.RoutesFor(ground).Single(r => r.Key == "by-refuge").Run!.Via!;
            Assert.Equal(low.MaxLevel + 1, refuge.BelowLevel);
            Assert.All(refuge.Points, p => Assert.False(string.IsNullOrWhiteSpace(p.Why)));
            Assert.Null(Routes.RoutesFor(ground).Single(r => r.Key == "by-facility-hub").Run!.Via);
        }

        var json = System.Text.Json.Nodes.JsonNode.Parse(EmbeddedText())!;
        json["danger_cells"]![0]!["cells"]![0] = "0xF47C:9,1";
        var e = Assert.Throws<TravelRoutesException>(() => TravelRoutes.Parse(TravelRoutes.FileName, json.ToJsonString()));
        Assert.Contains("$.danger_cells[0].cells[0]", e.Message, StringComparison.Ordinal);

        json = System.Text.Json.Nodes.JsonNode.Parse(EmbeddedText())!;
        json["grounds"]!["tusker-burrow"]!["routes"]![1]!["run"]!["via"]!["points"] = new System.Text.Json.Nodes.JsonArray();
        e = Assert.Throws<TravelRoutesException>(() => TravelRoutes.Parse(TravelRoutes.FileName, json.ToJsonString()));
        Assert.Contains("via.points", e.Message, StringComparison.Ordinal);
    }

    private static string EmbeddedText()
    {
        using var stream = typeof(TravelRoutes).Assembly.GetManifestResourceStream(TravelRoutes.ResourceName) ?? throw new InvalidOperationException("the travel file is not embedded");
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void The_travel_routes_load_from_the_embedded_file_once()
    {
        Assert.Same(Routes, TravelRoutes.Default);
        Assert.Equal("holtburg", Routes.StartPlace);
        Assert.NotEmpty(Routes.Places);
        Assert.All(Routes.Edges, e => Assert.False(string.IsNullOrWhiteSpace(e.Source), e.Key));
        Assert.Equal(Routes.Edges.Count, Routes.Edges.Select(e => e.Key).Distinct().Count());
        Assert.Equal(["tusker-abode", "tusker-armory", "tusker-barracks", "tusker-burrow", "tusker-cave", "tusker-cavern", "tusker-habitat", "tusker-holding", "tusker-lodge", "tusker-quarters"],
            Routes.GroundKeys.Where(k => k.StartsWith("tusker-", StringComparison.Ordinal) && !k.StartsWith("tusker-temple", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Throws<KeyNotFoundException>(() => Routes.Edge("no-such-edge"));
    }

    [Fact]
    public void The_edges_the_stops_need_are_there_with_their_world_rows()
    {
        var hop = Routes.Edge("holtburg-to-network");
        Assert.Equal((43065u, 0x7A9B4080u, 0xA9B40003u, 0x00070143u, (int?)null, (int?)null, 129), (hop.Wcid, hop.Guid, hop.Cell, hop.DestCell, hop.MinLevel, hop.MaxLevel, hop.Bitmask));
        var refuge = Routes.Edge("network-to-refuge");
        Assert.Equal((43002u, 0x70007079u, 0xF6820033u, 10), (refuge.Wcid, refuge.Guid, refuge.DestCell, refuge.MinLevel));
        var back = Routes.Edge("refuge-to-network");
        Assert.Equal((42852u, 0x7F682018u, 0x00070145u), (back.Wcid, back.Guid, back.DestCell));
        Assert.Equal(10, Routes.Edge("network-to-facility-hub").MinLevel);
        Assert.Equal(0x7ABB1005u, Routes.Edge("new-colier").Guid);
        Assert.Equal(12527u, Routes.Edge("new-colier").Wcid);
        Assert.Equal(new uint[] { 42175, 42176, 42177, 42178, 42179, 42180, 42181, 42182 }, Routes.Edges.Where(e => e.Key.StartsWith("hub-", StringComparison.Ordinal)).Select(e => e.Wcid).OrderBy(w => w));
        Assert.All(Routes.Edges.Where(e => e.Key.StartsWith("hub-", StringComparison.Ordinal)), e => Assert.Equal(49, e.Bitmask)); // NoRecall: untieable
        Assert.Equal(["fort-tethana", "stonehold", "plateau", "qalabar", "dryreach"], StopTowns.Where(t => Routes.Edges.Any(e => e.Key == "network-to-" + t) && Routes.Edges.Any(e => e.Key == t + "-to-network")));
    }

    [Fact]
    public void Every_ladder_rungs_ground_and_alternate_has_a_route()
    {
        var checkedGrounds = new List<string>();
        foreach (var rung in LadderLoader.WarMage.Rungs)
        {
            var grounds = new List<string>();
            if (rung.GroundKey is { } g)
            {
                grounds.Add(g);
            }

            // A pending alternate has no profile and no route yet, by name: Hills Citadel (TODO(m6-hills-citadel)), placed in item 10 for the owner's answer; a pending rung is not skipped (its route is item 4's).
            grounds.AddRange(rung.Alternates.Where(a => !a.Pending).Select(a => a.GroundKey));
            foreach (var ground in grounds)
            {
                // A pending rung is not skipped: the file routes every ground the ladder names, pending or not (a pending profile is item 3's, the route is item 4's).
                Assert.True(Routes.RoutesFor(ground).Count > 0, $"rung '{rung.Key}': ground '{ground}' has no route in the travel routes (TODO(m6-ground-route))");
                checkedGrounds.Add(ground);
            }
        }

        Assert.True(checkedGrounds.Count >= 12, "the ladder names twelve or more grounds and alternates");
    }

    /// <summary>
    /// M6 7 J (D70, closes <c>m6-facility-hub-rooms</c>; M6 4 F4 had put the Refuge first at every level): the Facility Hub's route is first in the file, so it is taken from its gate 15 on, and below it the Refuge's: the hub's walk is proven on the dats (TownNetworkWalkDatTests, FacilityHubWalkDatTests),
    /// and it takes the 609 m island run down to 20.9 m.
    /// </summary>
    [Fact]
    public void Burrow_is_reached_by_the_refuge_from_10_and_by_the_facility_hub_from_15()
    {
        Assert.Equal(TravelOutcome.Refused, Routes.FirstAdmitted("tusker-burrow", 9).Outcome);
        Assert.Equal("by-refuge", Routes.FirstAdmitted("tusker-burrow", 10).Route!.Key); // the Refuge portal's MinLevel 10; the hub drop's gate is 15
        Assert.Equal("by-refuge", Routes.FirstAdmitted("tusker-burrow", 14).Route!.Key);
        Assert.Equal("by-facility-hub", Routes.FirstAdmitted("tusker-burrow", 15).Route!.Key); // first where both admit
        Assert.Equal(["by-facility-hub", "by-refuge"], Routes.RoutesFor("tusker-burrow").Select(r => r.Key));
        Assert.Equal(new TravelGate(10, null), Routes.RoutesFor("tusker-burrow").Single(r => r.Key == "by-refuge").Gate);
        Assert.Equal(new TravelGate(15, null), Routes.RoutesFor("tusker-burrow").Single(r => r.Key == "by-facility-hub").Gate);
        Assert.True(Routes.RoutesFor("tusker-burrow").Single(r => r.Key == "by-facility-hub").Run!.Metres < Routes.RoutesFor("tusker-burrow").Single(r => r.Key == "by-refuge").Run!.Metres);
    }

    /// <summary>M6 7 J: the Holding by the Barracks drop first (gate 40, the nearest start, D51), the Refuge second, with the entry's own gate over both.</summary>
    [Fact]
    public void The_holding_is_the_barracks_drop_first_and_the_refuge_second_with_the_entrys_own_gate()
    {
        var routes = Routes.RoutesFor("tusker-holding");
        Assert.Equal(["by-facility-hub-barracks", "by-refuge"], routes.Select(r => r.Key));
        Assert.Equal(new TravelGate(40, null), routes[0].Gate); // Facility Hub Barracks portal 42176: MinLevel 40
        Assert.Equal(new TravelGate(10, null), routes[1].Gate);
        Assert.InRange(routes[0].Run!.Metres, 1506, 1507); // facts.md section 4: 1,506 m from the Barracks drop
        Assert.InRange(routes[1].Run!.Metres, 2796, 2797); // from Oolutanga's arrival
        Assert.Equal(TravelOutcome.Refused, Routes.FirstAdmitted("tusker-holding", 59, entryMinLevel: 60).Outcome); // the Holding's entry gate is 60, passed in
        Assert.Equal("by-facility-hub-barracks", Routes.FirstAdmitted("tusker-holding", 60, entryMinLevel: 60).Route!.Key);
        Assert.Equal("by-refuge", Routes.FirstAdmitted("tusker-holding", 39).Route!.Key);
        Assert.Equal("by-facility-hub-barracks", Routes.FirstAdmitted("tusker-holding", 40).Route!.Key);
    }

    /// <summary>M6 14 armory: the Armory by the Barracks drop alone (gate 40, the nearest start), a run of 345 m to the entry, whose own gate is 50; its Surface exit drops on the island place's 0xEF7F.</summary>
    [Fact]
    public void The_armory_is_the_barracks_drop_and_a_345_m_run_with_the_entrys_own_gate()
    {
        var route = Assert.Single(Routes.RoutesFor("tusker-armory"));
        Assert.Equal("by-facility-hub-barracks", route.Key);
        Assert.Equal(["holtburg-to-network", "network-to-facility-hub", "hub-barracks"], route.Steps.Select(s => s.Edge.Key));
        Assert.Equal(Routes.RoutesFor("tusker-holding")[0].Steps.Select(s => s.Edge.Key), route.Steps.Select(s => s.Edge.Key)); // the Holding's hops: the drop 0xF07E0040
        Assert.Equal(new TravelGate(40, null), route.Gate);
        Assert.InRange(route.Run!.Metres, 345, 345.2); // survey.md section 3.1: 345 m from the Barracks drop
        Assert.Equal((22657u, 0x7EF7F000u), (route.Run.EntryWcid, route.Run.EntryGuid));
        Assert.Equal(TravelOutcome.Refused, Routes.FirstAdmitted("tusker-armory", 49, entryMinLevel: 50).Outcome);
        Assert.Equal("by-facility-hub-barracks", Routes.FirstAdmitted("tusker-armory", 50, entryMinLevel: 50).Route!.Key);
        Assert.Equal("island", Routes.PlaceOf("0xEF7F")?.Key);
        Assert.Equal("refuge", Routes.WayHomeFrom("0xEF7F", 50).WayHome!.Key);
    }

    [Fact]
    public void The_temple_20_to_40_is_the_new_colier_walk_first_and_the_temple_by_fort_tethana_is_the_towns_hop()
    {
        var temple = Routes.RoutesFor("tusker-temple-20-40");
        Assert.Equal(["by-new-colier", "by-neydisa-castle"], temple.Select(r => r.Key));
        Assert.Equal(["new-colier"], temple[0].Steps.Select(s => s.Edge.Key));
        Assert.InRange(temple[0].Run!.Metres, 733, 735); // facts.md section 3: 734 m from the New Colier drop
        Assert.InRange(temple[1].Run!.Metres, 3449, 3451);
        Assert.Equal("by-new-colier", Routes.FirstAdmitted("tusker-temple-20-40", 25, 20, 40).Route!.Key);
        Assert.Equal(TravelOutcome.Refused, Routes.FirstAdmitted("tusker-temple-20-40", 41, 20, 40).Outcome);

        var fort = Routes.RoutesFor("tusker-temple-fort-tethana").Single();
        Assert.Equal(["holtburg-to-network", "network-to-fort-tethana"], fort.Steps.Select(s => s.Edge.Key));
        Assert.InRange(fort.Run!.Metres, 783, 785);
        Assert.Equal(TravelOutcome.Refused, Routes.FirstAdmitted("tusker-temple-fort-tethana", 39, 40).Outcome);
        Assert.Equal(TravelOutcome.Admitted, Routes.FirstAdmitted("tusker-temple-fort-tethana", 40, 40).Outcome);
    }

    [Fact]
    public void A_level_9_climber_is_refused_the_island_and_a_ground_with_no_route_is_a_named_stop_not_an_exception()
    {
        foreach (var ground in IslandGrounds)
        {
            var choice = Routes.FirstAdmitted(ground, 9);
            Assert.Equal(TravelOutcome.Refused, choice.Outcome);
            Assert.Null(choice.Route);
            Assert.Contains("route-refused", choice.Stop);
        }

        var none = Routes.FirstAdmitted("no-such-ground", 50);
        Assert.Equal(TravelOutcome.NoRoute, none.Outcome);
        Assert.Null(none.Route);
        Assert.StartsWith("TODO(m6-ground-route)", none.Stop);
        Assert.Empty(Routes.RoutesFor("no-such-ground"));
    }

    [Fact]
    public void Every_island_ground_has_a_facility_hub_route_or_the_refuge_and_each_routes_gate_is_its_highest_minimum()
    {
        foreach (var ground in IslandGrounds)
        {
            var routes = Routes.RoutesFor(ground);
            Assert.Contains(routes, r => r.Steps.Any(s => s.Edge.Key == "network-to-refuge"));
            foreach (var r in routes)
            {
                Assert.Equal(r.Steps.Max(s => s.Edge.MinLevel ?? 0), r.Gate.MinLevel);
                Assert.Null(r.Gate.MaxLevel);
                Assert.NotNull(r.Run);
                Assert.True(r.Run!.Metres > 0);
            }
        }
    }

    [Fact]
    public void Ways_home_exist_from_the_refuge_the_island_drops_the_facility_hub_and_every_stop_town()
    {
        var refuge = Routes.WayHomeFrom("refuge", 10).WayHome!;
        Assert.Equal(["refuge-to-network", "network-to-holtburg"], refuge.Steps.Select(s => s.Edge.Key)); // 42852 then 42820
        Assert.Equal(0x7F682018u, Routes.Edge("refuge-to-network").Guid);
        Assert.Equal(13.5, Assert.Single(refuge.FirstWalkHints).WalkMetres, 0.1);
        Assert.Equal("refuge", Routes.WayHomeFrom("0xF682", 10).WayHome!.Key); // by the landblock of the Refuge

        foreach (var drop in Routes.Edges.Where(e => e.Key.StartsWith("hub-", StringComparison.Ordinal)))
        {
            var way = Routes.WayHomeFrom("0x" + (drop.DestCell >> 16).ToString("X4", System.Globalization.CultureInfo.InvariantCulture), 40);
            Assert.Equal(TravelOutcome.Admitted, way.Outcome);
            Assert.Equal("refuge-to-network", way.WayHome!.Steps[0].Edge.Key);
        }

        Assert.Contains(Routes.WaysHome.Single(w => w.Key == "island-drops").FirstWalkHints, h => h.ArrivedBy == "hub-barracks");
        Assert.Equal("facility-hub-to-eastham", Routes.WayHomeFrom("facility-hub", 10).WayHome!.Steps[0].Edge.Key); // the hub holds no portal to the Network

        foreach (var town in StopTowns)
        {
            var way = Routes.WayHomeFrom(town, 1);
            Assert.Equal(TravelOutcome.Admitted, way.Outcome);
            Assert.Equal([town + "-to-network", "network-to-holtburg"], way.WayHome!.Steps.Select(s => s.Edge.Key));
            Assert.Equal("holtburg", way.WayHome.Steps[^1].Edge.To);
            Assert.NotEmpty(way.WayHome.FirstWalkHints);
        }

        var unknown = Routes.WayHomeFrom("nowhere", 50);
        Assert.Equal(TravelOutcome.NoRoute, unknown.Outcome);
        Assert.StartsWith("TODO(m6-way-home)", unknown.Stop);
    }

    [Fact]
    public void Each_routes_steps_follow_from_the_start_place_and_the_walk_hints_are_given()
    {
        foreach (var ground in Routes.GroundKeys)
        {
            foreach (var r in Routes.RoutesFor(ground))
            {
                var at = r.Start;
                foreach (var s in r.Steps)
                {
                    Assert.Equal(at, s.Edge.From);
                    Assert.NotNull(s.WalkMetres);
                    at = s.Edge.To;
                }
            }
        }
    }

    [Fact]
    public void A_missing_field_an_unknown_field_and_a_bad_edge_fail_at_load_naming_the_file_and_the_path()
    {
        var text = TravelRoutes.ResourceText()!;

        var missing = Assert.Throws<TravelRoutesException>(() => TravelRoutes.Parse(TravelRoutes.FileName, text.Replace("\"max_level\": null,", string.Empty, StringComparison.Ordinal)));
        Assert.Equal("$.edges[0].max_level", missing.JsonPath);
        Assert.Contains(TravelRoutes.FileName, missing.Message);

        var unknown = Assert.Throws<TravelRoutesException>(() => TravelRoutes.Parse(TravelRoutes.FileName, text.Replace("\"wcid\": 43065,", "\"wcid\": 43065,\n      \"bogus\": 1,", StringComparison.Ordinal)));
        Assert.Equal("$.edges[0].bogus", unknown.JsonPath);

        var badEdge = Assert.Throws<TravelRoutesException>(() => TravelRoutes.Parse(TravelRoutes.FileName, text.Replace("\"edge\": \"holtburg-to-network\"", "\"edge\": \"no-such-edge\"", StringComparison.Ordinal)));
        Assert.EndsWith(".edge", badEdge.JsonPath);

        var wrongKey = Assert.Throws<TravelRoutesException>(() => TravelRoutes.Parse("other.json", text));
        Assert.Equal("$.key", wrongKey.JsonPath);
        Assert.Equal("other.json", wrongKey.File);

        Assert.Equal("$", Assert.Throws<TravelRoutesException>(() => TravelRoutes.Parse(TravelRoutes.FileName, "{")).JsonPath);
    }

    [Fact]
    public void A_climber_through_a_step_that_does_not_follow_its_place_fails_at_load()
    {
        var text = TravelRoutes.ResourceText()!;
        // The first route of the Burrow starts at holtburg; make its first step a portal that stands in the Town Network.
        var broken = text.Replace("\"edge\": \"holtburg-to-network\"", "\"edge\": \"network-to-refuge\"", StringComparison.Ordinal);
        var e = Assert.Throws<TravelRoutesException>(() => TravelRoutes.Parse(TravelRoutes.FileName, broken));
        Assert.Contains("climber is in", e.Message);
    }

    [Fact]
    public void The_Aphus_Lassel_Portal_Gem_is_in_the_file_with_its_spell_and_destination()
    {
        // M6 4 T3a (closes TODO(m6-gem-in-travel-file)): the gem was a code constant; it is data with its SQL rows.
        var gem = Assert.Single(Routes.Gems);
        Assert.Equal(("aphus-lassel", "Aphus Lassel Portal Gem", 22729u, 2936u, 0xF6820033u, "island"), (gem.Key, gem.Name, gem.Wcid, gem.Spell, gem.DestCell, gem.ReplacesHopsInto));
        Assert.Equal(0xF682u, gem.DestLandblock);
        Assert.Contains("weenie_properties_d_i_d", gem.Source, StringComparison.Ordinal);
        Assert.Same(gem, Routes.GemInto("island"));
        Assert.Null(Routes.GemInto("holtburg"));
        Assert.Contains(Routes.PlaceOf("island")!.Landblocks, l => l == gem.DestLandblock);
        // It drops where the Refuge's Portal from the Town Network does (the replaced hop's own destination).
        Assert.Equal(Routes.Edge("network-to-refuge").DestCell, gem.DestCell);
    }

    [Fact]
    public void A_bad_gem_or_facing_fails_at_load_naming_the_path()
    {
        var text = TravelRoutes.ResourceText()!;
        var place = Assert.Throws<TravelRoutesException>(() => TravelRoutes.Parse(TravelRoutes.FileName, text.Replace("\"replaces_hops_into\": \"island\"", "\"replaces_hops_into\": \"nowhere\"", StringComparison.Ordinal)));
        Assert.Equal("$.gems[0].replaces_hops_into", place.JsonPath);
        var lastDest = text.LastIndexOf("\"dest_cell\": \"0xF6820033\"", StringComparison.Ordinal); // the gem is the last section of the file
        var cell = Assert.Throws<TravelRoutesException>(() => TravelRoutes.Parse(TravelRoutes.FileName, text.Remove(lastDest, "\"dest_cell\": \"0xF6820033\"".Length).Insert(lastDest, "\"dest_cell\": \"0x01D40033\"")));
        Assert.StartsWith("$.gems[0]", cell.JsonPath, StringComparison.Ordinal);

        var noGems = Assert.Throws<TravelRoutesException>(() => TravelRoutes.Parse(TravelRoutes.FileName, text.Replace("\"gems\": [", "\"gems_not\": [", StringComparison.Ordinal)));
        Assert.Equal("$.gems", noGems.JsonPath);

        var firstFacing = text.IndexOf("\"facing\": {", StringComparison.Ordinal);
        var end = text.IndexOf('}', firstFacing) + 1;
        var noFacing = Assert.Throws<TravelRoutesException>(() => TravelRoutes.Parse(TravelRoutes.FileName, text.Remove(firstFacing, end - firstFacing).Insert(firstFacing, "\"facing\": {\"w\": 1, \"x\": 1, \"y\": 0, \"z\": 0}")));
        Assert.Equal("$.edges[0].facing", noFacing.JsonPath); // not a unit quaternion
    }

    [Fact]
    public void Every_portal_edge_and_ground_entry_carries_the_facing_of_its_placement()
    {
        // The identity faces +Y; a quarter turn about Z (w=0.707, z=-0.707) faces +X; the file's own: the Network's Holtburg portal faces west.
        Assert.Equal(new System.Numerics.Vector2(0, 1), new PortalFacing(1, 0, 0, 0).Forward);
        var east = new PortalFacing(0.7071067811865476, 0, 0, -0.7071067811865476).Forward!.Value;
        Assert.Equal((1.0, 0.0), (Math.Round(east.X, 3), Math.Round(east.Y, 3)));
        Assert.Null(new PortalFacing(0.7071067811865476, 0.7071067811865476, 0, 0).Forward); // tipped flat: no horizontal facing

        Assert.All(Routes.Edges, e => Assert.NotNull(e.Facing?.Forward));
        var west = Routes.Edge("network-to-holtburg").Facing!.Value.Forward!.Value;
        Assert.Equal((-1.0, 0.0), (Math.Round(west.X, 3), Math.Round(west.Y, 3)));
        var runs = Routes.GroundKeys.SelectMany(g => Routes.RoutesFor(g)).Where(r => r.Run is not null).Select(r => r.Run!).ToArray();
        Assert.NotEmpty(runs);
        Assert.All(runs, r => Assert.NotNull(r.EntryFacing?.Forward));
        // One entry portal has one facing however many routes lead to it.
        foreach (var byGuid in runs.GroupBy(r => r.EntryGuid))
        {
            Assert.Single(byGuid.Select(r => r.EntryFacing).Distinct());
        }
    }

    [Fact]
    public void The_Town_Network_has_a_way_home_with_a_walk_hint_for_every_portal_that_drops_there()
    {
        // M6 4 T3a (closes TODO(m6-way-home) for the hub as a starting place).
        foreach (var from in new[] { "town-network", "0x0007" })
        {
            var way = Routes.WayHomeFrom(from, 1);
            Assert.Equal(TravelOutcome.Admitted, way.Outcome);
            Assert.Equal("town-network", way.WayHome!.Key);
            Assert.Equal(["network-to-holtburg"], way.WayHome.Steps.Select(s => s.Edge.Key));
        }

        var hinted = Routes.WaysHome.Single(w => w.Key == "town-network").FirstWalkHints.Select(h => h.ArrivedBy).Order(StringComparer.Ordinal);
        Assert.Equal(Routes.Edges.Where(e => e.To == "town-network").Select(e => e.Key).Order(StringComparer.Ordinal), hinted);
        // The Facility Hub's way stays by Eastham (the hub holds no portal into the Network).
        Assert.Equal(["facility-hub-to-eastham", "eastham-to-network", "network-to-holtburg"], Routes.WayHomeFrom("0x8A02", 12).WayHome!.Steps.Select(s => s.Edge.Key));
    }

    /// <summary>M6 8 B1: the chains to Crater Lake Village and to Yinar: the seven edges, the door way in and the walk out of the arrival, and a place with its way home.</summary>
    [Fact]
    public void The_chains_to_crater_lake_and_to_yinar_are_in_the_file_M6_8_B1()
    {
        foreach (var key in new[] { "network-to-glenden", "glenden-to-highland", "highland-to-pathway", "pathway-to-crater-lake", "crater-lake-to-network", "network-to-samsur", "samsur-to-yinar" })
        {
            Assert.Contains(Routes.Edges, e => e.Key == key);
        }

        Assert.Equal(2, Routes.Edge("pathway-to-crater-lake").WalkOut!.Count);
        Assert.Equal(5, Routes.Edge("highland-to-pathway").WalkIn!.Count);
        Assert.Null(Routes.Edge("network-to-glenden").WalkOut);
        Assert.Equal(["crater-lake-to-network", "network-to-holtburg"], Routes.WayHomeFrom("0x8FD2", 80).WayHome!.Steps.Select(s => s.Edge.Key));
        Assert.Contains(Routes.Places, p => p.Key == "yinar" && p.Landblocks.Contains((ushort)0xA66C));
    }

    /// <summary>M6 8 B1: a route that starts in the body's place comes first, from anywhere else the route from the file's start; a walk_in or walk_out field is read strictly.</summary>
    [Fact]
    public void A_body_in_the_village_takes_the_route_that_starts_there_and_a_body_at_home_the_chain_M6_8_B1()
    {
        Assert.Equal("from-the-village", Routes.FirstAdmitted("crater-caves", 80, bodyPlace: "crater-lake").Route!.Key);
        Assert.Equal("by-the-crater-pathway", Routes.FirstAdmitted("crater-caves", 80, bodyPlace: "holtburg").Route!.Key);
        Assert.Equal("by-the-crater-pathway", Routes.FirstAdmitted("crater-caves", 80).Route!.Key);

        var json = System.Text.Json.Nodes.JsonNode.Parse(EmbeddedText())!;
        var pathway = json["edges"]!.AsArray().First(e => (string?)e!["key"] == "pathway-to-crater-lake")!;
        pathway["walk_out"]![0]!["why"] = 3;
        var e = Assert.Throws<TravelRoutesException>(() => TravelRoutes.Parse(TravelRoutes.FileName, json.ToJsonString()));
        Assert.Contains("walk_out[0].why", e.Message, StringComparison.Ordinal);
    }

    /// <summary>M6 8 B1: the fewest hops from Holtburg to a place the two-hop rule cannot reach (the Glenden Wood chain), and a place with no portal way back is a trip only for a book with Lifestone Recall.</summary>
    [Fact]
    public void The_town_trip_finds_the_chain_and_a_place_without_a_way_back_needs_the_recall_M6_8_B1()
    {
        static TownTripCandidate Candidate(string town, uint cell) => new(town, "someone", 1, new ScrivenerPlace(town, 0, cell, cell, System.Numerics.Vector3.Zero));
        var plan = TownNetworkTrip.Judge(Candidate("Crater Lake Village", 0x90D00000), new TownTripInput("holtburg", 80, true, true), Routes, LeverDoors.Default);
        Assert.Equal("holtburg-to-network,network-to-glenden,glenden-to-highland,highland-to-pathway,pathway-to-crater-lake", plan.Plan!.HopKeys);

        var cave = Candidate("Yinar", 0xA66C0100);
        Assert.Equal("holtburg-to-network,network-to-samsur,samsur-to-yinar", TownNetworkTrip.Judge(cave, new TownTripInput("holtburg", 80, true, true), Routes, LeverDoors.Default).Plan!.HopKeys);
        Assert.Equal("no-way-back", TownNetworkTrip.Judge(cave, new TownTripInput("holtburg", 80, false, true), Routes, LeverDoors.Default).Refusal);
    }
}

using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 1 d (D12, D4, D40, D28): the buy-trip on the den (its Pyreal and Platinum Scarabs are carried: no Holtburg vendor sells them) against the scripted shops of <see cref="LogisticsStationTests.Trip"/>. What these prove: the catalog's
/// scarab sellers cite their create-list rows with Zisaha of Fort Tethana first; the due rule reads the plan's level against the first spell level the ground's components burn the line at (the Pyreal Scarab from VI, the Platinum from
/// VII), never a constant, and an unread plan is a named stop; the built character's trip goes by the journey's budgeted teleport (the seam's Ground travel) to Fort Tethana's arrival, meets Zisaha in reach, buys the line to its keep with
/// check, act, check (the pack rose by the count, the coin fell) and recalls home; a short purse stops before the travel and a refusal after one buy, both named and never retried; the climber away from the town is D40's loud stop, logged
/// once a session; the logistics stop on a carried line names the buy-trip; and the line's fields come in order.
/// </summary>
public sealed class BuyTripTests(ITestOutputHelper output)
{
    private const string Den = "black-spawn-den-aluvian";
    private const uint PyrealScarab = 690;
    private const uint PlatinumScarab = 8897;
    private const uint DiamondScarab = 7299;
    private const uint Zisaha = 2314;
    private const uint ZisahaGuid = 0x72481001;
    private const uint MiscType = 0x80;

    /// <summary>M6 16 E5b: Zisaha's MerchandiseItemTypes by the Fort Tethana profile (<c>buys_item_types</c>, from the pinned world SQL v0.9.296): it holds PromissoryNote 0x40000.</summary>
    private const uint ZisahaMask = 0x000CB000;

    private static readonly string[] LineFields = ["scarab", "town", "vendor", "want", "bought", "price", "purse_before", "purse_after", "travel", "ok", "reason"];

    /// <summary>The den's trip world with the Pyreal Scarab at <paramref name="pyreal"/>, every other line at its keep, War Magic trained with <paramref name="warRanks"/> ranks, and Zisaha at her catalog place with her two scarabs.</summary>
    /// <param name="mask">M6 16 E5b: the ItemType mask the scripted shop buys on the wire (0: nothing, as before; <see cref="ZisahaMask"/>: her profile's, which takes a trade note).</param>
    private LogisticsStationTests.Trip Make(int pyreal, int coins = 1_000_000, int warRanks = 300, string? refusal = null, uint mask = 0)
    {
        var trip = new LogisticsStationTests.Trip(Den, coins, held: l => l.Wcid == PyrealScarab ? pyreal : l.Keep).WithOutput(output);
        trip.Vendor.H.R.Reducer.OnPrivateUpdate(trip.Vendor.H.R.Context, new PrivateUpdateMsg(PrivateUpdateKind.Skill, 30, MageTemplate.WarMagic, 0, default, default, new SkillDesc(MageTemplate.WarMagic, (ushort)warRanks, 1, 2u, 0, 0u, 0, 0)));
        var seller = ScrivenerCatalog.Default.ScarabSellers.Single(s => s.Wcid == Zisaha);
        var place = seller.Places.Single();
        trip.Vendor.PlaceAt(place.Guid, seller.Name, place.Cell, place.Point, 3f, seller.Wcid);
        var shop = trip.Shops.Add(place.Guid, mask);
        uint row = 0x80008000;
        foreach (var sale in seller.Sells)
        {
            shop.Rows.Add((-1, ++row, sale.Name, sale.ScarabWcid, MiscType, seller.PriceOf(sale.Value))); // the scripted shop sells at 1.0, so the row's Value is his price
        }

        shop.BuyRefusal = refusal;
        trip.Vendor.R.Tick();
        return trip;
    }

    /// <summary>The trip's journey context; <paramref name="teleports"/> is the plan's teleport budget (a built character's is 1 here, the climber's 0: D40 reads the budget, not the name).</summary>
    private static JourneyContext Context(LogisticsStationTests.Trip trip, string character = "BSD Mage", int teleports = 1) => new()
    {
        Facts = new JourneyFacts { TeleportBudget = teleports },
        Character = character,
        Account = "test",
        Log = trip.Vendor.H.Log,
        Travel = trip.Travel,
        Spells = new Dictionary<uint, SpellInfo>(),
        Dungeon = trip.Ground,
    };

    private static string? Field(string line, string name) =>
        line.Split(' ').FirstOrDefault(t => t.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];

    private static string TripLine(LogisticsStationTests.Trip trip) => Assert.Single(trip.Lines, l => l.Contains(BuyTripStation.LinePrefix + " ", StringComparison.Ordinal));

    [Fact]
    public void The_catalog_lists_the_scarab_sellers_with_their_create_list_rows_and_Zisaha_of_Fort_Tethana_first()
    {
        var c = ScrivenerCatalog.Default;
        var zisaha = c.ScarabSellers[0];
        Assert.Equal((Zisaha, "Zisaha bint Jaidka the Mage", 1.8), (zisaha.Wcid, zisaha.Name, zisaha.SellRate));
        Assert.Equal([(PyrealScarab, 1000, 70986), (PlatinumScarab, 10000, 70987)], zisaha.Sells.Select(s => (s.ScarabWcid, s.Value, s.CreateListId)));
        var place = Assert.Single(zisaha.Places);
        Assert.Equal(("Fort Tethana", ZisahaGuid, 0x24810102u), (place.Town, place.Guid, place.Cell));
        Assert.Equal((1800, 18000), (zisaha.PriceOf(1000), zisaha.PriceOf(10000))); // ACE's GetSellCost at her 1.8, as the Fort Tethana profile's prices
        Assert.Equal("Neydisa", c.ScarabSellers.Single(s => s.Wcid == 5646).Places.Single().Town); // Archmage Philomon
        Assert.Contains(c.ScarabSellersOf(PyrealScarab), s => s.Wcid == 35730);
        Assert.DoesNotContain(c.ScarabSellersOf(PlatinumScarab), s => s.Wcid == 35730); // Janier al-Evv lists the Pyreal Scarab only
        Assert.Empty(c.ScarabSellersOf(DiamondScarab)); // D12: no shop (only Jeeves in the Advocate dungeon)
        Assert.All(c.ScarabSellers.SelectMany(s => s.Sells), s => Assert.True(s.CreateListId > 0));
        Assert.Equal(c.ScarabSellers.Count, c.ScarabSellers.Select(s => s.Wcid).Distinct().Count());
    }

    [Theory]
    [InlineData(5, BuyTripKind.NotDue, "plan-below:Pyreal_Scarab")]
    [InlineData(6, BuyTripKind.Due, "due:Pyreal_Scarab")]
    [InlineData(7, BuyTripKind.Due, "due:Pyreal_Scarab")]
    [InlineData(null, BuyTripKind.Stop, "plan-unread")]
    public void The_due_rule_reads_the_plans_level_against_the_first_level_the_grounds_components_burn_the_line_at(int? plan, BuyTripKind kind, string token)
    {
        var trip = Make(pyreal: 2);
        var counters = new LogisticsCounters(trip.Ground);
        Assert.Equal(LogisticsKind.Stop, counters.Check(trip.Vendor.State).Kind);
        Assert.Equal((6, 7), (BuyTripRule.FirstLevelBurning(trip.Ground, PyrealScarab), BuyTripRule.FirstLevelBurning(trip.Ground, PlatinumScarab))); // the den's components, never a constant
        Assert.Null(BuyTripRule.FirstLevelBurning(trip.Ground, DiamondScarab));

        var d = BuyTripRule.Decide(trip.Ground, counters, plan, "the test's plan", ScrivenerCatalog.Default, GroundCatalog.All);

        output.WriteLine($"{d.Kind} {d.Token}: {d.Why}");
        Assert.Equal((kind, token), (d.Kind, d.Token));
        if (kind == BuyTripKind.Due)
        {
            var keep = trip.Ground.Supplies.StockOf(PyrealScarab)!.Keep;
            Assert.Equal((2, keep - 2, 6, plan!.Value, Zisaha, "Fort Tethana", 1800), (d.Held, d.Want, d.FirstLevel, d.PlanLevel, d.Seller!.Wcid, d.Place!.Town, d.CatalogPrice));
            Assert.Equal(GroundCatalog.All.First(g => g.Home.Town == "Fort Tethana").Home, d.Arrival); // the arrival: the lifestone of the ground whose home is Fort Tethana
        }
    }

    [Fact]
    public void The_Platinum_Scarab_is_due_only_from_the_first_VII_of_the_plan_D4()
    {
        var trip = new LogisticsStationTests.Trip(Den, held: l => l.Wcid == PlatinumScarab ? 1 : l.Keep).WithOutput(output);
        var counters = new LogisticsCounters(trip.Ground);
        counters.Check(trip.Vendor.State);

        Assert.Equal("plan-below:Platinum_Scarab", BuyTripRule.Decide(trip.Ground, counters, 6, "VI", ScrivenerCatalog.Default, GroundCatalog.All).Token);
        var due = BuyTripRule.Decide(trip.Ground, counters, 7, "VII", ScrivenerCatalog.Default, GroundCatalog.All);
        Assert.Equal(("due:Platinum_Scarab", 18000), (due.Token, due.CatalogPrice));
    }

    [Fact]
    public void The_built_characters_trip_teleports_to_Fort_Tethana_buys_to_the_keep_at_Zisaha_with_check_act_check_and_recalls_home()
    {
        var trip = Make(pyreal: 2);
        var keep = trip.Ground.Supplies.StockOf(PyrealScarab)!.Keep;
        var run = new BuyTripStation(memory: new BuyTripMemory()).Start(Context(trip));

        Assert.Equal(ScenarioStatus.Succeeded, trip.Run(run));

        var line = TripLine(trip);
        output.WriteLine(line);
        Assert.Equal(LineFields, line[(line.IndexOf(BuyTripStation.LinePrefix + " ", StringComparison.Ordinal) + BuyTripStation.LinePrefix.Length + 1)..].Split(' ').Select(t => t[..t.IndexOf('=', StringComparison.Ordinal)]));
        var want = keep - 2;
        var paid = want * 1800;
        Assert.EndsWith(string.Create(CultureInfo.InvariantCulture, $"scarab=Pyreal_Scarab town=Fort_Tethana vendor=2314 want={want} bought={want} price=1800 purse_before=1000000 purse_after={1_000_000 - paid} travel=budgeted-teleport ok=yes reason=bought"), line, StringComparison.Ordinal);
        Assert.Equal([TravelPurpose.Ground, TravelPurpose.Vendor, TravelPurpose.Home], trip.Travel.Requests.Select(r => r.Purpose));
        Assert.Equal(trip.Ground.Home.LifestoneCell >> 16, trip.Travel.Requests[2].Destination.Location.Position.CellId >> 16); // the way back: the ground's home
        Assert.Equal(ZisahaGuid, trip.Travel.Requests[1].ObjectGuid);
        Assert.Equal(1, trip.Shops.Buys);
        Assert.Equal(keep, GearInventory.CountOf(trip.Vendor.State, [], PyrealScarab));
        Assert.True(trip.Logged("buy-trip: check before the buy: Zisaha bint Jaidka the Mage in reach and open, Pyreal Scarab listed at 1800"));
        Assert.True(trip.Logged($"buy-trip: check after the buy: the pack holds {keep} Pyreal Scarab (+{want})"));
        Assert.Equal(LogisticsKind.None, new LogisticsCounters(trip.Ground).Check(trip.Vendor.State).Kind); // the stop is gone
    }

    [Fact]
    public void A_short_purse_is_a_named_stop_before_the_travel_and_nothing_is_bought()
    {
        var trip = Make(pyreal: 2, coins: 10_000);
        var run = new BuyTripStation(memory: new BuyTripMemory()).Start(Context(trip));

        Assert.Equal(ScenarioStatus.Failed, trip.Run(run));

        Assert.StartsWith("buy-trip:purse-short: the purse 10000 does not cover", run.Failure, StringComparison.Ordinal);
        Assert.Empty(trip.Travel.Requests);
        Assert.Equal(0, trip.Shops.Buys);
        var line = TripLine(trip);
        Assert.Equal(("0", "no", "purse-short", "none"), (Field(line, "bought"), Field(line, "ok"), Field(line, "reason"), Field(line, "travel")));
    }

    /// <summary>
    /// M6 16 E5b (closes <c>TODO(m6-buy-trip-notes)</c>): the trip's buy is the vendor counter's. The den's trip world holds 510,000 pyreals in trade notes (the profile's purse: one 250,000, one 10,000, five 50,000) beside the coin;
    /// 398 Pyreal Scarabs cost 716,400 at Zisaha's 1,800. Before E5b a coin of 700,000 was the stop <c>buy-trip:purse-short</c> before the travel.
    /// </summary>
    [Fact]
    public void A_trip_the_coin_does_not_cover_is_paid_from_a_trade_note_changed_at_the_archmages_counter_M6_16_E5b()
    {
        var trip = Make(pyreal: 2, coins: 700_000, mask: ZisahaMask);
        var keep = trip.Ground.Supplies.StockOf(PyrealScarab)!.Keep;
        var want = keep - 2;
        var cost = want * 1800;
        Assert.Equal(ZisahaMask, GroundCatalog.All.SelectMany(g => g.Vendors).First(v => v.Wcid == Zisaha).BuysItemTypes); // the fact the trip's check before reads: her counter takes a note
        var run = new BuyTripStation(memory: new BuyTripMemory()).Start(Context(trip));

        Assert.Equal(ScenarioStatus.Succeeded, trip.Run(run));

        Assert.True(trip.Logged($"buy-trip: the coin 700000 is short of {cost}; the trade notes held (510000) cover the rest: a note is changed at Zisaha bint Jaidka the Mage's counter before the buy (the mask 0x000CB000 takes a trade note, by the ground profiles)"));
        Assert.True(trip.Logged($"buy-trip: check before the buy: Zisaha bint Jaidka the Mage in reach and open, Pyreal Scarab listed at 1800, the cost {cost} over the coin 700000 (a trade note is changed first); the pack holds 2"));
        Assert.True(trip.Logged("buy-trip: purse: the coin was short: changed Trade Note (50,000) to coin at Zisaha bint Jaidka the Mage: paid 50000 pyreals (the coin 700000 -> 750000)")); // D99's rule: the smallest note that covers the 16,400 short
        Assert.True(trip.Logged("buy-trip: paid from the purse: 1 trade note(s) changed to coin at Zisaha bint Jaidka the Mage before the buy"));
        Assert.True(trip.Logged($"buy-trip: check after the buy: the pack holds {keep} Pyreal Scarab (+{want}), the coin 750000 -> {750_000 - cost} (paid {cost})"));
        var line = TripLine(trip);
        Assert.EndsWith(string.Create(CultureInfo.InvariantCulture, $"scarab=Pyreal_Scarab town=Fort_Tethana vendor=2314 want={want} bought={want} price=1800 purse_before=700000 purse_after={750_000 - cost} travel=budgeted-teleport ok=yes reason=bought"), line, StringComparison.Ordinal);
        Assert.Equal((1, 1), (trip.Shops.Buys, trip.Shops.Sells));
        Assert.Equal(keep, GearInventory.CountOf(trip.Vendor.State, [], PyrealScarab));
        Assert.Equal([TravelPurpose.Ground, TravelPurpose.Vendor, TravelPurpose.Home], trip.Travel.Requests.Select(r => r.Purpose));
    }

    [Fact]
    public void A_counter_whose_wire_mask_takes_no_note_is_the_stop_purse_short_there_and_nothing_is_bought_M6_16_E5b()
    {
        var trip = Make(pyreal: 2, coins: 700_000); // the scripted shop's wire mask is 0, whatever the profile says: the counter reads the wire
        var run = new BuyTripStation(memory: new BuyTripMemory()).Start(Context(trip));

        Assert.Equal(ScenarioStatus.Failed, trip.Run(run));

        Assert.StartsWith("buy-trip:purse-short: the coin 700000 does not cover 398 Pyreal Scarab at Zisaha bint Jaidka the Mage's wire price (716400); nothing is bought, never retried", run.Failure, StringComparison.Ordinal);
        Assert.Contains("the shop's mask takes no trade note (the 510000 held in notes are not changed here)", run.Failure, StringComparison.Ordinal);
        Assert.Equal((0, 0), (trip.Shops.Buys, trip.Shops.Sells));
        Assert.Equal([TravelPurpose.Ground, TravelPurpose.Vendor], trip.Travel.Requests.Select(r => r.Purpose));
        var line = TripLine(trip);
        Assert.Equal(("0", "no", "purse-short", "700000"), (Field(line, "bought"), Field(line, "ok"), Field(line, "reason"), Field(line, "purse_before")));
    }

    [Fact]
    public void A_refused_buy_is_a_named_stop_never_retried()
    {
        var trip = Make(pyreal: 2, refusal: "You can't buy that right now.");
        var run = new BuyTripStation(memory: new BuyTripMemory()).Start(Context(trip));

        Assert.Equal(ScenarioStatus.Failed, trip.Run(run));

        Assert.StartsWith("buy-trip:refused: Zisaha bint Jaidka the Mage refused", run.Failure, StringComparison.Ordinal);
        Assert.Contains("never retried", run.Failure, StringComparison.Ordinal);
        Assert.Equal(1, trip.Shops.Buys);
        Assert.Equal([TravelPurpose.Ground, TravelPurpose.Vendor], trip.Travel.Requests.Select(r => r.Purpose)); // no way back: the station ends at the stop
        var line = TripLine(trip);
        Assert.Equal(("0", "no", "refused", "1000000"), (Field(line, "bought"), Field(line, "ok"), Field(line, "reason"), Field(line, "purse_before")));
    }

    [Fact]
    public void The_climber_away_from_the_town_goes_by_the_Town_Network_and_a_failed_hop_is_named_then_home_by_portals_M6_4_t4a()
    {
        var trip = Make(pyreal: 2);
        var run = new BuyTripStation(memory: new BuyTripMemory()).Start(Context(trip, MageTemplate.ClimberName, teleports: 0));
        Assert.Equal(ScenarioStatus.Failed, trip.Run(run));
        var dump = string.Join(" | ", trip.Lines.Where(l => l.Contains("buy", StringComparison.Ordinal) || l.Contains("hop", StringComparison.Ordinal)));

        // The rig's book lacks Lifestone Recall: Zisaha's way back by portals starts at Fort Tethana's Network portal, behind its lever gate, so the trip is the catalog's next seller in a travel place. M6 item 14 (m6-goldscarab): that is
        // Archmage Sammam al-Ashiz of Uziz since the travel file holds Uziz (the town stock's stop; his walk from the drop and back is confirmed, TownNetworkWalkDatTests); it was Philomon's at Neydisa, whose walk finds no path (TODO(m6-neydisa-walk)).
        Assert.True(trip.Lines.Any(l => l.Contains("buy-trip: by the Town Network (no teleport budget, D40): chose Archmage Sammam al-Ashiz in Uziz: hops holtburg-to-network,network-to-uziz", StringComparison.Ordinal)), dump);
        Assert.True(trip.Lines.Any(l => l.Contains("Fort Tethana refused lever-door:fort-tethana-gate: the way back's first portal, Fort Tethana's Portal to Town Network 0x7258104B is behind", StringComparison.Ordinal)), dump);
        Assert.True(trip.Lines.Any(l => l.Contains("buy-trip: the seller is Archmage Sammam al-Ashiz (1824) in Uziz (the trip's choice)", StringComparison.Ordinal)), dump);
        Assert.StartsWith("buy-trip:hop:holtburg-to-network:", run.Failure, StringComparison.Ordinal); // this rig has no portal object: the hop fails by name
        Assert.Contains(trip.Lines, l => l.Contains("buy_trip_hop: n=1 edge=holtburg-to-network", StringComparison.Ordinal) && l.EndsWith("ok=no", StringComparison.Ordinal));
        Assert.True(trip.Lines.Any(l => l.Contains("walking: purpose=home (buy-trip: the book lacks Lifestone Recall; the rung change direction=home by portals (portals:uziz) to holtburg)", StringComparison.Ordinal)), dump);
        Assert.DoesNotContain(trip.Travel.Requests, r => r.Purpose == TravelPurpose.Ground); // no teleport for the climber (D40)
        var line = TripLine(trip);
        Assert.Equal(("town-network", "no", "Uziz", "1824"), (Field(line, "travel"), Field(line, "ok"), Field(line, "town"), Field(line, "vendor")));
        Assert.Equal(0, trip.Shops.Buys);
    }

    [Fact]
    public void The_scarab_trip_by_the_Town_Network_is_Zisahas_with_the_recall_and_her_walk_passes_no_lever_door_M6_4_t4a()
    {
        var routes = Lifestone.Client.Session.Travel.TravelRoutes.Default;
        var candidates = ScrivenerCatalog.Default.ScarabSellersOf(PyrealScarab).SelectMany(s => s.Places.Select(p => new TownTripCandidate(p.Town, s.Name, s.Wcid, p))).ToList();
        var recall = TownNetworkTrip.Choose(candidates, new TownTripInput("holtburg", 45, KnowsLifestoneRecall: true, HomeIsTravelStart: true), routes, LeverDoors.Default);

        Assert.Equal(("Fort Tethana", ZisahaGuid), (recall.Chosen!.Candidate.Town, recall.Chosen.Candidate.Vendor.Guid));
        Assert.Equal(["holtburg-to-network", "network-to-fort-tethana"], recall.Chosen.Hops.Select(h => h.Key));
        Assert.Equal((0x2681001Du, "fort-tethana"), (recall.Chosen.DropCell, recall.Chosen.Place.Key)); // her landblock 0x2481 is in no place: the town's name finds it
        Assert.True(recall.Chosen.HomeByRecall);
        Assert.All(new[] { "Sawato", "Linvak Tukal", "Timaru", "Fiun", "Xarabydun" }, t => Assert.Equal("off-network", recall.Verdicts.First(v => v.Candidate.Town == t).Refusal));
        Assert.Null(recall.Verdicts.First(v => v.Candidate.Town == "Uziz").Refusal); // M6 item 14 (m6-goldscarab): Uziz is a travel place now (the town stock's stop), so it is admitted too; Zisaha, first in the catalog, is still the choice

        var noRecall = TownNetworkTrip.Choose(candidates, new TownTripInput("holtburg", 45, KnowsLifestoneRecall: false, HomeIsTravelStart: true), routes, LeverDoors.Default);
        Assert.Equal("lever-door:fort-tethana-gate", noRecall.Verdicts.First(v => v.Candidate.Town == "Fort Tethana").Refusal);
        Assert.Equal(("Uziz", "portals:uziz"), (noRecall.Chosen!.Candidate.Town, noRecall.Chosen.Home)); // M6 item 14 (m6-goldscarab): the catalog's second seller, in a travel place since the town stock's stop; before it Neydisa's Philomon, whose walk finds no path
        Assert.Equal(["holtburg-to-network", "network-to-uziz"], noRecall.Chosen.Hops.Select(h => h.Key));
        Assert.Equal("Fort Tethana", TownNetworkTrip.Choose(candidates, new TownTripInput("holtburg", 45, false, true), routes, LeverDoors.None).Chosen!.Candidate.Town); // the door is what refuses her
    }

    [Fact]
    public void Nothing_is_bought_when_no_carried_line_is_under_its_stop_line()
    {
        var trip = Make(pyreal: 400);
        var run = new BuyTripStation(memory: new BuyTripMemory()).Start(Context(trip));

        Assert.Equal(ScenarioStatus.Succeeded, trip.Run(run));

        Assert.Empty(trip.Travel.Requests);
        var line = TripLine(trip);
        Assert.Equal(("0", "yes", "not-due:none"), (Field(line, "bought"), Field(line, "ok"), Field(line, "reason")));
    }

    [Theory]
    [InlineData("BSD Mage", 1)]
    [InlineData(MageTemplate.ClimberName, 0)]
    [InlineData("BSD Mage", 0)] // the rule is the budget, not the name (M6 1 fix, 1-R13)
    public void The_logistics_stop_on_a_carried_line_names_the_buy_trip_and_stays_a_stop_until_the_autopilot_calls_it_M6_buy_trip_autopilot(string character, int teleports)
    {
        var climber = teleports == 0;
        var trip = Make(pyreal: 2);
        var run = new LogisticsStation(forced: true).Start(Context(trip, character, teleports));

        Assert.Equal(ScenarioStatus.Failed, trip.Run(run));

        Assert.Contains("logistics: stop: Pyreal Scarab: 2 held, under its stop line 3", run.Failure, StringComparison.Ordinal);
        Assert.Contains($"buy-trip due: Pyreal Scarab x{trip.Ground.Supplies.StockOf(PyrealScarab)!.Keep - 2} at Zisaha bint Jaidka the Mage (2314) in Fort Tethana; the station buy-trip makes it (D12)", run.Failure, StringComparison.Ordinal);
        Assert.Equal(climber, run.Failure!.Contains("is the Town Network hop (travel=town-network, D40)", StringComparison.Ordinal));
        Assert.DoesNotContain("m5-platinum-stock", run.Failure, StringComparison.Ordinal);
        Assert.Contains("the autopilot's audit queues the station when the seller's town is reachable (M6 4 t4b, D44), so this stop stands only when it was not or the trip was already tried", run.Failure, StringComparison.Ordinal); // M6 4 F2 (4-R10): the stop is still named; the autopilot's call exists since t4b
        Assert.Empty(trip.Travel.Requests);
    }

    [Fact]
    public void A_character_with_no_teleport_budget_goes_by_the_Town_Network_whatever_its_name_M6_1_fix_1_R13()
    {
        var trip = Make(pyreal: 2);
        var run = new BuyTripStation(memory: new BuyTripMemory()).Start(Context(trip, "BSD Mage", teleports: 0));

        Assert.Equal(ScenarioStatus.Failed, trip.Run(run));

        Assert.StartsWith("buy-trip:hop:", run.Failure, StringComparison.Ordinal); // M6 4 t4a: the hop, not the stop (this rig has no portal object)
        Assert.Equal("town-network", Field(TripLine(trip), "travel"));
        Assert.DoesNotContain(trip.Travel.Requests, r => r.Purpose == TravelPurpose.Ground);
        Assert.False(BuyTripStation.NoTeleportBudget(new JourneyFacts { TeleportBudget = 1 }));
        Assert.True(BuyTripStation.NoTeleportBudget(new JourneyFacts()));
    }

    [Fact]
    public void A_town_is_every_landblock_the_catalog_lists_in_it_plus_the_arrival_not_the_one_landblock_M6_1_fix_1_R13()
    {
        var home = GroundCatalog.All.First(g => g.Home.Town == "Fort Tethana").Home;
        var blocks = BuyTripStation.TownLandblocks("Fort Tethana", home, ScrivenerCatalog.Default);

        Assert.Contains(0x2481u, blocks); // Zisaha's shop
        Assert.Contains(0x2581u, blocks); // the town's scrivener
        Assert.Contains(home.LifestoneCell >> 16, blocks); // the lifestone (0x2780)
        Assert.DoesNotContain(0xA9B3u, blocks); // Holtburg is another town
    }

    [Fact]
    public void The_factory_makes_the_buy_trip_station_with_its_no_back_argument()
    {
        var spec = new StationSpec("buy-trip", StationKinds.BuyTrip, null, 600, new Dictionary<string, string>(StringComparer.Ordinal) { [BuyTripStation.NoBackArgument] = "1" });
        var plan = new JourneyPlan("buy-trip", "Lifestone Throwaway", "test-tools", StationWhere.Here, 1, [spec]);

        var station = JourneyStations.Create(plan, [], null).Create(spec);

        Assert.IsType<BuyTripStation>(station);
        Assert.Equal(BuyTripStation.StationName, station.Name);
        Assert.Contains(StationKinds.BuyTrip, StationKinds.All);
    }
}

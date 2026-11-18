using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 item 4 t4b (D44, closes <c>m6-buy-trip-autopilot</c>): a carried line (the Pyreal Scarab from VI) under its stop line. Proved: the pure call (<see cref="AutopilotBuyTripRule"/>) queues the station when it is due and the seller's town is reachable
/// (he stands in it, the journey has a teleport budget, or the travel file has a Town Network hop into it and a portal from where he stands) and leaves the audit's stop otherwise, with the reason; over the scripted ACE the audit queues the
/// <c>buy-trip</c> station at the trip point and the run goes on to the fight when the stack is full after it (check after), stops by the audit's own name when the station fails, and does not try twice when the stack is still short after it.
/// </summary>
public sealed class AutopilotBuyTripTests(ITestOutputHelper output)
{
    private const uint PyrealScarab = 690;

    private static readonly TravelRoutes Routes = TravelRoutes.Default;

    private static void Proven(JourneyContext c)
    {
        c.Facts.LifestoneTied = true;
        c.Facts.SetPortalTie(Den, PortalTie.Tied);
    }

    private static BuyTripDecision Due(ushort? _ = null)
    {
        var den = GroundCatalog.Get(Den);
        var seller = ScrivenerCatalog.Default.ScarabSellers[0];
        return new BuyTripDecision(BuyTripKind.Due, "due:Pyreal_Scarab", "test", den.Supplies.StockOf(PyrealScarab), 2, 10, 6, 6, seller, seller.Places.Single(), GroundCatalog.All.First(g => g.Home.Town == "Fort Tethana").Home, 1800);
    }

    /// <summary>The Town Network rule's input as the station builds it, from <paramref name="place"/> (a travel place key, or null when the body stands in none).</summary>
    private static TownTripInput Trip(string? place, bool recall = true, bool homeIsStart = true, int level = 60) => new(place, level, recall, homeIsStart);

    private static BuyTripCall Call(BuyTripDecision decision, JourneyFacts facts, ushort? landblock, TownTripInput trip, LeverDoors? doors = null) =>
        AutopilotBuyTripRule.Call(decision, facts, landblock, ScrivenerCatalog.Default, Routes, trip, doors ?? LeverDoors.Default);

    [Fact]
    public void The_call_queues_the_station_when_the_seller_is_in_reach_and_says_why_when_not()
    {
        var inTown = Call(Due(), new JourneyFacts(), 0x2780, Trip("fort-tethana"));
        Assert.Equal((true, "in-town"), (inTown.Queue, inTown.Via));

        var budget = Call(Due(), new JourneyFacts { TeleportBudget = 1 }, 0xA9B4, Trip("holtburg"));
        Assert.Equal((true, "budgeted-teleport"), (budget.Queue, budget.Via));

        var network = Call(Due(), new JourneyFacts(), 0xA9B4, Trip("holtburg")); // Holtburg has a portal to the Town Network, which has the Fort Tethana portal
        Assert.Equal((true, TownNetworkTrip.TravelWord), (network.Queue, network.Via));
        Assert.Contains("from holtburg", network.Reason, StringComparison.Ordinal);
        Assert.Contains("in Fort Tethana", network.Reason, StringComparison.Ordinal);

        var nowhere = Call(Due(), new JourneyFacts(), 0x1234, Trip(null));
        Assert.False(nowhere.Queue);
        Assert.Contains("admits no seller", nowhere.Reason, StringComparison.Ordinal);
        Assert.Contains("refused no-route", nowhere.Reason, StringComparison.Ordinal);

        var notDue = Call(Due() with { Kind = BuyTripKind.NotDue, Why = "plan-below" }, new JourneyFacts { TeleportBudget = 1 }, 0xA9B4, Trip("holtburg"));
        Assert.False(notDue.Queue);
        Assert.Contains("notdue", notDue.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_call_asks_the_stations_own_town_choice_so_it_never_queues_a_trip_the_station_refuses()
    {
        // M6 4 F1 (4-R4): the same inputs as BuyTripStation's run (TownNetworkTrip.Choose: lever doors, the town's own way back, the recall).
        var candidates = ScrivenerCatalog.Default.ScarabSellersOf(PyrealScarab).SelectMany(s => s.Places.Select(p => new TownTripCandidate(p.Town, s.Name, s.Wcid, p))).ToList();
        foreach (var trip in new[] { Trip("holtburg"), Trip("holtburg", recall: false), Trip("holtburg", recall: false, homeIsStart: false), Trip(null) })
        {
            var station = TownNetworkTrip.Choose(candidates, trip, Routes, LeverDoors.Default);
            var call = Call(Due(), new JourneyFacts(), 0xA9B4, trip);
            Assert.Equal(station.Chosen is not null, call.Queue);
            Assert.Contains(station.Why, call.Reason, StringComparison.Ordinal); // every refusal is on the call's line
        }

        // Without the recall, Zisaha's way back by portals starts behind Fort Tethana's lever gate: the call says so, as the station does.
        var noRecall = Call(Due(), new JourneyFacts(), 0xA9B4, Trip("holtburg", recall: false));
        Assert.Contains("lever-door:", noRecall.Reason, StringComparison.Ordinal);

        // A ground whose home is not the travel file's start, without the recall: every seller is refused no-way-home, nothing is queued.
        var away = Call(Due(), new JourneyFacts(), 0xA9B4, Trip("holtburg", recall: false, homeIsStart: false));
        Assert.False(away.Queue);
        Assert.Contains("no-way-home", away.Reason, StringComparison.Ordinal);

        // With no lever door known, Zisaha is admitted again: the doors are the station's, passed in, never a constant.
        Assert.Contains("in Fort Tethana", Call(Due(), new JourneyFacts(), 0xA9B4, Trip("holtburg", recall: false), LeverDoors.None).Reason, StringComparison.Ordinal);
    }

    private ApWorld World(int pyreal)
    {
        var w = new ApWorld(Den, output, held: l => l.Wcid == PyrealScarab ? pyreal : l.Keep) { Prime = c => { Proven(c); c.Facts.TeleportBudget = 1; } };
        var h = w.Trip.Vendor.H;
        h.R.Reducer.OnPrivateUpdate(h.R.Context, new PrivateUpdateMsg(PrivateUpdateKind.Skill, 30, MageTemplate.WarMagic, 0, default, default, new SkillDesc(MageTemplate.WarMagic, 300, 1, 2u, 0, 0u, 0, 0)));
        w.OwnKit();
        return w;
    }

    private static int Under() => GroundCatalog.Get(Den).Supplies.StockOf(PyrealScarab)!.StopUnder - 1;

    [Fact]
    public void A_carried_line_under_its_stop_queues_the_buy_trip_and_the_run_goes_on_when_the_stack_is_full_after_it()
    {
        var w = World(GroundCatalog.Get(Den).Supplies.StockOf(PyrealScarab)!.StopUnder - 1);
        var keep = GroundCatalog.Get(Den).Supplies.StockOf(PyrealScarab)!.Keep;
        w.Factory.Script = spec => spec.Kind != StationKinds.BuyTrip ? null : new ScriptedRun(spec.Name, _ => { w.SetHeld(PyrealScarab, keep); return ScenarioStatus.Succeeded; }, () => $"scarab=Pyreal_Scarab bought={keep}");
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Fight);
        autopilot.RequestStop("test");
        w.Run(autopilot);

        Assert.Equal("buy-trip", Kinds(w)[0]);
        Assert.Equal(1, Kinds(w).Count(k => k == "buy-trip"));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit buy_trip: ", StringComparison.Ordinal) && l.Contains("queued via=budgeted-teleport", StringComparison.Ordinal));
        Assert.DoesNotContain("logistics-stop", autopilot.StopName ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("kit=ok", autopilot.LastAudit, StringComparison.Ordinal); // the audit read the stack again after the station
    }

    [Fact]
    public void A_buy_trip_that_fails_stops_by_the_audits_own_name_and_is_not_made_again()
    {
        var w = World(Under());
        w.Factory.Script = spec => spec.Kind != StationKinds.BuyTrip ? null : new ScriptedRun(spec.Name, _ => ScenarioStatus.Failed, () => "scarab=Pyreal_Scarab bought=0") { Failure = "buy-trip:purse-short" };
        var autopilot = w.Autopilot(new AutopilotOptions());

        Assert.Equal(ScenarioStatus.Failed, w.Run(autopilot));
        Assert.StartsWith("logistics-stop:", autopilot.StopName, StringComparison.Ordinal);
        Assert.Single(Kinds(w), "buy-trip");
    }

    [Fact]
    public void A_buy_trip_that_leaves_the_stack_short_is_not_tried_twice_and_the_audit_stops_by_name()
    {
        var w = World(Under());
        w.Factory.Script = spec => spec.Kind != StationKinds.BuyTrip ? null : new ScriptedRun(spec.Name, _ => ScenarioStatus.Succeeded, () => "scarab=Pyreal_Scarab bought=0");
        var autopilot = w.Autopilot(new AutopilotOptions());

        Assert.Equal(ScenarioStatus.Failed, w.Run(autopilot));
        Assert.StartsWith("logistics-stop:", autopilot.StopName, StringComparison.Ordinal);
        Assert.Single(Kinds(w), "buy-trip");
        Assert.Contains(w.Lines, l => l.Contains("ran once this run and the line is still under its stop line", StringComparison.Ordinal));
    }
}

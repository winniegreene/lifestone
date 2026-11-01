using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Grounds;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M5 3 p1, "the places" (D50, D46, 2-R10, 2-R12): the build's first tie (<see cref="PortalTieLeg"/>: the place to the ground's portal front, the tie spell through the cast policy, the link, the place back home; a refusal named; a ground with no portal record skipped),
/// the tie as a three-state fact per ground (<see cref="JourneyFacts.PortalTieOf"/>: unknown tries the recall, ACE's 0x04A3 sets untied, one ground's tie says nothing of another), the mage plan's budget 0 and the untied travel's named failure
/// (<c>travel:ground:untied</c>, <c>TODO(m6-first-tie-walk)</c>, never a place), and the stations' home read from the journey ground. Both dungeon profiles are exercised: nothing here names the den's cells or Holtburg's.
/// </summary>
public sealed class P1PlacesTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>The keys of the dungeon profiles that carry a portal record.</summary>
    public static TheoryData<string> PortalGrounds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var g in GroundCatalog.All)
            {
                if (g.Kind == GroundKind.Dungeon && g.Portal is not null)
                {
                    data.Add(g.Key);
                }
            }

            return data;
        }
    }

    private static HuntingGround Another(HuntingGround ground) => GroundCatalog.All.First(g => g.Kind == GroundKind.Dungeon && g.Key != ground.Key);

    private static TravelRequest ToArrival(HuntingGround ground) => new(ground.ArrivalDestination(), TravelPurpose.Ground);

    private static TravelRequest ToPortalFront(HuntingGround ground) =>
        new(new Lifestone.Client.Navigation.TeleportDestination("portal front", [], new Lifestone.Client.World.WorldLocation("portal front", new Lifestone.Client.World.CellPosition(ground.Portal!.FrontCell, ground.Portal.Front), true, 0), "test"), TravelPurpose.Ground);

    // ------------------------------------------------------------------------------------------------ the plans

    [Fact]
    public void The_mage_plan_makes_no_place_and_the_fighters_keep_their_budgets()
    {
        Assert.Equal(0, JourneyPlans.Mage.TeleportBudget);
        Assert.Equal(JourneyPlans.MageTeleportBudget, JourneyPlans.Mage.TeleportBudget);
        Assert.Equal(1, JourneyPlans.Melee.TeleportBudget);
        Assert.Equal(1, JourneyPlans.Archer.TeleportBudget);
    }

    // ------------------------------------------------------------------------------------------------ the tie fact, per ground

    [Fact]
    public void The_tie_fact_has_three_states_per_ground_and_unknown_is_the_default()
    {
        var facts = new JourneyFacts();
        Assert.Equal(PortalTie.Unknown, facts.PortalTieOf("a"));
        facts.SetPortalTie("a", PortalTie.Tied);
        facts.SetPortalTie("b", PortalTie.Untied);
        Assert.Equal((PortalTie.Tied, PortalTie.Untied, PortalTie.Unknown), (facts.PortalTieOf("a"), facts.PortalTieOf("b"), facts.PortalTieOf("c")));
        facts.SetPortalTie("a", PortalTie.Unknown);
        Assert.Equal(PortalTie.Unknown, facts.PortalTieOf("a"));
        Assert.Equal(["b"], facts.PortalTies.Keys);
    }

    [Theory]
    [MemberData(nameof(PortalGrounds))]
    public void An_unknown_tie_tries_the_recall_and_a_tied_answer_sets_tied_for_that_ground_only(string key)
    {
        var ground = GroundCatalog.Get(key);
        var t = new J1JourneyRig(); // ACE answers the recall (PortalTied defaults to true)
        t.Context();
        var leg = t.Travel!.GoTo(t.View, ToArrival(ground), t.Now);
        Assert.Equal(TravelMeans.Recall, leg.Means);
        Assert.Contains(t.Log.Entries, e => e.Message.Contains($"by Primary Portal Recall TRIED (the {key} tie is not known", StringComparison.Ordinal));
        leg.Dispose();

        // The rig's recall lands in the den's landblock, so only the den's leg can run to its end; every ground's tie is its own.
        if (key == DenGround.Profile.Key)
        {
            var den = t.Travel.GoTo(t.View, ToArrival(ground), t.Now);
            Assert.Equal(TravelStatus.Arrived, t.RunLeg(den));
            Assert.Equal((PortalTie.Tied, 0), (t.Facts.PortalTieOf(key), t.Facts.Teleports));
            Assert.Equal(PortalTie.Unknown, t.Facts.PortalTieOf(Another(ground).Key));
            Assert.Empty(t.PlacesTo);
        }
    }

    [Theory]
    [MemberData(nameof(PortalGrounds))]
    public void ACEs_0x04A3_answer_to_an_unknown_tie_sets_untied_for_that_ground_only_and_names_the_first_tie_walk(string key)
    {
        var ground = GroundCatalog.Get(key);
        var t = new J1JourneyRig { PortalTied = false };
        t.Context();
        var leg = t.Travel!.GoTo(t.View, ToArrival(ground), t.Now);
        Assert.Equal(TravelMeans.Recall, leg.Means);
        Assert.Equal(TravelStatus.Failed, t.RunLeg(leg));
        Assert.StartsWith("travel:recall:cast: ACE refused: YouMustLinkToPortalToRecall 0x04A3", leg.Failure, StringComparison.Ordinal);

        Assert.Equal(PortalTie.Untied, t.Facts.PortalTieOf(key));
        Assert.Equal(PortalTie.Unknown, t.Facts.PortalTieOf(Another(ground).Key));
        Assert.Contains(t.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("TODO(m6-first-tie-walk)", StringComparison.Ordinal));
        Assert.Empty(t.PlacesTo);
    }

    [Theory]
    [MemberData(nameof(PortalGrounds))]
    public void A_known_untied_ground_fails_the_travel_untied_for_its_arrival_and_its_portal_front_and_makes_no_place_whatever_the_budget(string key)
    {
        var ground = GroundCatalog.Get(key);
        var t = new J1JourneyRig();
        t.Context(budget: 5);
        t.Facts.SetPortalTie(key, PortalTie.Untied);

        foreach (var request in new[] { ToArrival(ground), ToPortalFront(ground) })
        {
            var leg = t.Travel!.GoTo(t.View, request, t.Now);
            Assert.Equal(TravelStatus.Failed, t.RunLeg(leg));
            Assert.Equal("travel:ground:untied", leg.Failure);
            Assert.Equal(JourneyTravel.UntiedFailure, leg.Failure);
        }

        Assert.Empty(t.PlacesTo);
        Assert.Empty(t.Casts);
        Assert.Equal((0, 0, 0), (t.Facts.Teleports, t.Travel!.Places, t.Travel.Refused));
        Assert.Equal(2, t.Log.Entries.Count(e => e.Severity == LogSeverity.Warning && e.Message.Contains("TODO(m6-first-tie-walk)", StringComparison.Ordinal) && e.Message.Contains("travel:ground:untied", StringComparison.Ordinal)));
        Assert.DoesNotContain(t.Log.Entries, e => e.Severity >= LogSeverity.Error);
    }

    [Fact]
    public void A_tie_to_one_ground_says_nothing_of_another_and_the_journeys_own_ground_is_the_one_read()
    {
        var den = DenGround.Profile;
        var other = Another(den);
        var t = new J1JourneyRig();
        t.Context(budget: 5);
        t.Facts.SetPortalTie(den.Key, PortalTie.Tied);
        t.Facts.SetPortalTie(other.Key, PortalTie.Untied);

        var toOther = t.Travel!.GoTo(t.View, ToArrival(other), t.Now);
        Assert.Equal("travel:ground:untied", toOther.Failure); // the den's tie did not recall a journey bound for the other ground
        var toDen = t.Travel.GoTo(t.View, ToArrival(den), t.Now);
        Assert.Equal(TravelMeans.Recall, toDen.Means);
        toDen.Dispose();

        // The hosts' context builder hands the travel the journey's ground (--fight-ground), so a ground's cell is read against it first.
        var context = JourneyScenario.NewContext(JourneyPlans.Mage, new RecordingLogSink(), new Dictionary<uint, SpellInfo>(), dungeon: other);
        Assert.Equal(other.Key, context.Dungeon.Key);
        context.Facts.SetPortalTie(other.Key, PortalTie.Untied);
        Assert.Equal("travel:ground:untied", context.Travel.GoTo(t.View, ToArrival(other), t.Now).Failure);
        Assert.Equal(other.Key, JourneyGrounds.DungeonOf(other.Arrival.Cell, other)!.Key);
        Assert.Equal(den.Key, JourneyGrounds.DungeonOf(den.Arrival.Cell, other)!.Key); // a cell outside the journey's ground falls back to the catalog's search
    }

    // ------------------------------------------------------------------------------------------------ the home, from the ground

    [Fact]
    public void The_bind_and_the_home_walks_read_the_journey_grounds_home_lifestone_and_point_not_a_constant()
    {
        var home = new GroundHome("Elsewhere", 0xA9B40031, new Vector3(51.5f, 33.25f, 90f));
        var ground = DenGround.Profile with { Home = home };
        var t = new J3bRig(lifestoneInView: false);
        var ctx = t.Context(null, ground);
        var (run, status) = t.RunStation(new BindStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        var walk = t.Travel.Requests[0];
        Assert.Equal(("home-lifestone-point", TravelPurpose.Bind, 0u), (walk.Destination.Name, walk.Purpose, walk.ObjectGuid));
        Assert.Equal((home.LifestoneCell, home.LifestonePoint), (walk.Destination.Location.Position.CellId, walk.Destination.Location.Position.Local));
    }

    [Fact]
    public void Home_is_none_in_the_destinations_landblock_and_a_recall_otherwise_with_no_constant_town()
    {
        var belt = new J1JourneyRig(J1JourneyRig.BeltCell);
        belt.Context();
        var elsewhere = new Lifestone.Client.Navigation.TeleportDestination("home", [], new Lifestone.Client.World.WorldLocation("home", new Lifestone.Client.World.CellPosition(J1JourneyRig.BeltCell, new Vector3(10, 10, 90)), true, 0), "test");
        var none = belt.Travel!.GoTo(belt.View, new TravelRequest(elsewhere, TravelPurpose.Home), belt.Now);
        Assert.Equal(TravelMeans.None, none.Means); // the player already stands in the journey's home landblock (the destination's)

        var away = new J1JourneyRig(J1JourneyRig.BeltCell);
        away.Context();
        var recall = away.Travel!.GoTo(away.View, new TravelRequest(JourneyGrounds.Holtburg, TravelPurpose.Home), away.Now);
        Assert.Equal(TravelMeans.Recall, recall.Means);
        recall.Dispose();
    }

    // ------------------------------------------------------------------------------------------------ the build's first tie

    [Fact]
    public void The_build_ties_only_in_the_session_ending_build_with_a_kit_plan_and_skips_a_ground_with_no_portal_record_named()
    {
        var den = GearKit.KitPlan.For(DenGround.Profile, SpellTableData.Load(fixture.Dats).Spells, "test");
        Assert.Equal("tie", MageBuildScenario.TieIntent(den, endSession: true));
        Assert.Equal("none", MageBuildScenario.TieIntent(den, endSession: false)); // a journey's embedded build-verify is play: no tie, no place
        Assert.Equal("none", MageBuildScenario.TieIntent(null, endSession: true));
        var bare = GearKit.KitPlan.For(DenGround.Profile with { Portal = null }, SpellTableData.Load(fixture.Dats).Spells, "test");
        Assert.Equal("skipped", MageBuildScenario.TieIntent(bare, endSession: true));
    }

    [Theory]
    [MemberData(nameof(PortalGrounds))]
    public void The_tie_leg_places_to_the_portal_front_casts_the_ground_tie_spell_checks_the_link_and_places_back_home(string key)
    {
        var ground = GroundCatalog.Get(key);
        var portal = ground.Portal!;
        var t = new J1JourneyRig(J1JourneyRig.BeltCell, peace: false); // in the magic mode already, as a built mage is
        t.CreatePortal(portal.Guid, portal.Wcid, portal.FrontCell);
        var log = new RecordingLogSink();
        var leg = new PortalTieLeg(ground, log);

        var status = t.Run(leg);
        Assert.True(status == ScenarioStatus.Succeeded, leg.Failure + " / " + string.Join(" | ", log.Entries.Select(e => e.Message)));
        Assert.True(leg.Linked, leg.Failure);
        Assert.Equal([portal.FrontCell, ground.Home.LifestoneCell], t.PlacedCells);
        Assert.Empty(t.PlacesTo); // the build's places are its own, not the journey seam's
        Assert.Equal([portal.TieSpell], t.Casts);
        Assert.Contains(log.Entries, e => e.Message.Contains("successfully linked with the portal", StringComparison.Ordinal) || e.Message.Contains("place back to " + ground.Home.Town, StringComparison.Ordinal));
        Assert.StartsWith("ACE's text", leg.Evidence, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(PortalGrounds))]
    public void A_fresh_builds_mage_in_peace_with_its_wand_in_the_pack_wields_it_reaches_the_magic_mode_and_ties(string key)
    {
        // M5 3 gate round 1: the re-created BSD Mage held its granted kit unwielded and the tie failed "the magic mode was not reached" (the guard refuses the magic mode with nothing in hand).
        var ground = GroundCatalog.Get(key);
        var portal = ground.Portal!;
        var t = new J1JourneyRig(J1JourneyRig.BeltCell, peace: true);
        t.Caster.AnswerWields = true;
        var wand = t.Caster.WandToPack();
        Assert.Null(CombatModeGuard.Read(t.R.H.State).Caster);
        t.CreatePortal(portal.Guid, portal.Wcid, portal.FrontCell);
        var log = new RecordingLogSink();
        var leg = new PortalTieLeg(ground, log);

        var status = t.Run(leg);
        Assert.True(status == ScenarioStatus.Succeeded, leg.Failure + " / " + string.Join(" | ", log.Entries.Select(e => e.Message)));
        Assert.True(leg.Linked, leg.Failure);
        Assert.Equal(wand, Assert.Single(t.R.H.Net.Of(FakeSendKind.GetAndWieldItem)).Guid);
        Assert.Contains(log.Entries, e => e.Message.Contains("wielding the pack's caster", StringComparison.Ordinal));
        Assert.Equal([portal.TieSpell], t.Casts);
    }

    [Fact]
    public void A_refused_tie_is_named_and_the_mage_is_still_placed_back_home_and_a_missing_portal_is_named()
    {
        var ground = DenGround.Profile;
        var portal = ground.Portal!;
        var refused = new J1JourneyRig(J1JourneyRig.BeltCell, peace: false) { TieRefusal = 0x04AD };
        refused.CreatePortal(portal.Guid, portal.Wcid, portal.FrontCell);
        var leg = new PortalTieLeg(ground, new RecordingLogSink());

        Assert.Equal(ScenarioStatus.Failed, refused.Run(leg));
        Assert.False(leg.Linked);
        Assert.Contains($"Primary Portal Tie {portal.TieSpell} was refused: YouCannotRecallPortal", leg.Failure, StringComparison.Ordinal);
        Assert.Equal([portal.FrontCell, ground.Home.LifestoneCell], refused.PlacedCells); // the front, then home: a failed tie leaves the mage at home

        var absent = new J1JourneyRig(J1JourneyRig.BeltCell, peace: false);
        var missing = new PortalTieLeg(ground, new RecordingLogSink());
        Assert.Equal(ScenarioStatus.Failed, absent.Run(missing));
        Assert.Contains($"no object of weenie {portal.Wcid} (the portal)", missing.Failure, StringComparison.Ordinal);
        Assert.Equal([portal.FrontCell, ground.Home.LifestoneCell], absent.PlacedCells);

        Assert.Throws<ArgumentException>(() => new PortalTieLeg(ground with { Portal = null }, new RecordingLogSink()));
    }

    // ------------------------------------------------------------------------------------------------ the death station

    [Fact]
    public void The_death_station_hands_the_handler_the_journeys_facts_and_travel()
    {
        var context = JourneyScenario.NewContext(JourneyPlans.Mage, new RecordingLogSink(), SpellTableData.Load(fixture.Dats).Spells);
        var station = JourneyStations.Create(JourneyPlans.Mage, [], null, "acct").Create(JourneyPlans.Mage.Stations.Single(s => s.Name == "death"));
        var run = station.Start(context);
        var rig = new J1JourneyRig(J1JourneyRig.DenCell, peace: false); // standing in the den, so the station's precondition passes and its scenario is made
        run.Step(rig.View, rig.Now);
        var scenario = (DeathScenario?)((IStationInner)run).Scenario;
        Assert.NotNull(scenario);
        var options = (DeathOptions)typeof(DeathScenario).GetField("_options", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(scenario)!;
        Assert.Same(context.Facts, options.Facts);
        Assert.Same(context.Travel, options.Travel);
    }
}

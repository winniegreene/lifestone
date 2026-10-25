using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Fighter;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// The travel stations (M4 P5b J3b) over <see cref="J3bRig"/>: the bind and the lifestone tie (the facts set from ACE's texts, the fighters inside a caster swap, the mage in magic mode), the den travel (a Primary Portal Recall that lands inside the den; the untied path's
/// one Ground request, the tie and the portal on foot), the recall home counting recalls, every refusal named, and no station making the mod's <c>place</c>.
/// </summary>
public sealed class J3bStationTests
{
    private static string Id(uint guid) => guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------------------------------------ bind

    [Fact]
    public void The_mage_binds_then_ties_the_lifestone_and_the_texts_set_the_facts()
    {
        var t = new J3bRig();
        var ctx = t.Context();
        var (run, status) = t.RunStation(new BindStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.True(ctx.Facts.Bound);
        Assert.True(ctx.Facts.LifestoneTied);
        Assert.Null(ctx.Facts.PortalTied);
        Assert.Matches(@"^bound=yes tied=yes swap_s=0\.0 tie_fizzles=0$", run.VerdictFields());

        // On foot to the lifestone as an object goal (the Bind purpose), then the Use, then the tie at it, in peace first and back in peace at the end.
        var request = Assert.Single(t.Travel.Requests);
        Assert.Equal((TravelPurpose.Bind, J3bRig.LifestoneGuid), (request.Purpose, request.ObjectGuid));
        Assert.True(request.Range > 0);
        Assert.Equal(["travel:Bind", "mode:1", "use:" + Id(J3bRig.LifestoneGuid), "mode:8", "cast:2644@" + Id(J3bRig.LifestoneGuid), "mode:1"], t.Events);
        Assert.Equal(CombatModes.NonCombat, t.R.H.State.Self.Qualities.CombatMode);
        Assert.Equal(0, t.Places);
    }

    [Fact]
    public void Bind_walks_to_the_lifestones_known_point_first_when_it_is_not_in_view_and_logs_the_fallback_once_F2()
    {
        var t = new J3bRig(lifestoneInView: false); // the Archer started by Thelnoth's shop: the lifestone appears only after the walk
        var ctx = t.Context();
        var (run, status) = t.RunStation(new BindStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal(2, t.Travel.Requests.Count);
        Assert.Equal((TravelPurpose.Bind, 0u), (t.Travel.Requests[0].Purpose, t.Travel.Requests[0].ObjectGuid)); // the point goal
        Assert.Equal(0xA9B40019u, t.Travel.Requests[0].Destination.Location.Position.CellId);
        Assert.Equal((TravelPurpose.Bind, J3bRig.LifestoneGuid), (t.Travel.Requests[1].Purpose, t.Travel.Requests[1].ObjectGuid)); // then the object goal as before
        Assert.Single(t.R.H.Log.Entries, e => e.Message.Contains("fallback walk to its known point", StringComparison.Ordinal));
        Assert.True(ctx.Facts.Bound);
        Assert.Equal(0, t.Places);
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void A_fighter_casts_the_tie_inside_a_caster_swap_and_ends_with_its_weapon_in_hand(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        var t = new J3bRig(template);
        var ctx = t.Context();
        var (run, status) = t.RunStation(new BindStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.True(ctx.Facts.Bound && ctx.Facts.LifestoneTied == true);
        Assert.Matches(@"^bound=yes tied=yes swap_s=\d+\.\d tie_fizzles=0$", run.VerdictFields());
        Assert.DoesNotContain("swap_s=0.0", run.VerdictFields());

        // The order: the Use, then the swap in (the caster wielded, the magic mode), the cast, the swap out (peace, the weapon back).
        var ops = t.SwapRig!.Ops;
        var cast = t.Events.FindIndex(e => e.StartsWith("cast:2644@", StringComparison.Ordinal));
        Assert.True(ops.IndexOf("wield:The Healer's Heart") >= 0 && cast > t.Events.IndexOf("use:" + Id(J3bRig.LifestoneGuid)), string.Join(" > ", t.Events) + " / " + string.Join(" > ", ops));
        Assert.True(t.Events.IndexOf("mode:8") is var magic && magic >= 0 && magic < cast && t.Events.LastIndexOf("mode:1") > cast);
        Assert.Equal(CombatModes.NonCombat, t.R.H.State.Self.Qualities.CombatMode);
        Assert.NotEqual(0u, t.SwapRig.LocationOf(C1bSwapRig.WeaponGuid));
        Assert.Equal(0u, t.SwapRig.LocationOf(C1bSwapRig.WandGuid));
        Assert.Equal(0, t.Places);
    }

    [Fact]
    public void A_fizzled_tie_is_cast_again_and_counted_but_a_tie_refused_for_its_components_is_named_and_leaves_the_fact_unset()
    {
        var fizzle = new J3bRig { TieFizzles = 1 };
        var ctx = fizzle.Context();
        var (run, status) = fizzle.RunStation(new BindStation(), ctx);
        Assert.True(status == ScenarioStatus.Succeeded, run.Failure + " / " + run.VerdictFields());
        Assert.True(ctx.Facts.LifestoneTied);
        Assert.EndsWith("tie_fizzles=1", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(2, fizzle.Events.Count(e => e.StartsWith("cast:2644@", StringComparison.Ordinal)));

        var refused = new J3bRig { TieRefusal = 0x0400 };
        var refusedCtx = refused.Context();
        var (refusedRun, _) = refused.RunStation(new BindStation(), refusedCtx);
        Assert.False(refusedRun.Ok);
        Assert.True(refusedCtx.Facts.Bound);
        Assert.Null(refusedCtx.Facts.LifestoneTied);
        Assert.StartsWith("bound=yes tied=no ", refusedRun.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(((ScriptLeg)refusedRun).Problems, p => p.Contains("YouDontHaveAllTheComponents 0x0400", StringComparison.Ordinal));
        Assert.Equal(0, refused.Places);
    }

    [Fact]
    public void A_use_that_does_not_attune_the_spirit_is_a_failed_check_and_a_failed_walk_ends_the_station_with_the_seams_reason()
    {
        var silent = new J3bRig { UseSilent = true };
        var ctx = silent.Context();
        var (run, _) = silent.RunStation(new BindStation(), ctx);
        Assert.False(run.Ok);
        Assert.False(ctx.Facts.Bound);
        Assert.StartsWith("bound=no tied=yes ", run.VerdictFields(), StringComparison.Ordinal);

        var stuck = new J3bRig();
        stuck.Travel.FailWith = "travel:run:blocked";
        var (walkRun, walkStatus) = stuck.RunStation(new BindStation(), stuck.Context());
        Assert.Equal(ScenarioStatus.Failed, walkStatus);
        Assert.Contains("travel:run:blocked", walkRun.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain(stuck.Events, e => e.StartsWith("use:", StringComparison.Ordinal)); // nothing was used or cast
        Assert.Equal(0, stuck.Places);
    }

    // ------------------------------------------------------------------------------------------------ den travel

    [Fact]
    public void A_tied_mage_recalls_into_the_den_with_one_cast_one_recall_and_no_place_and_no_walk()
    {
        var t = new J3bRig();
        var ctx = t.Context(f => f.PortalTied = true);
        var (run, status) = t.RunStation(new DenTravelStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal(["cast:48", "teleport:" + Id(J3bRig.DenCell)], t.Events.Where(e => e.StartsWith("cast:", StringComparison.Ordinal) || e.StartsWith("teleport:", StringComparison.Ordinal)));
        Assert.Empty(t.Travel.Requests);
        Assert.Equal(1, ctx.Facts.Recalls);
        Assert.True(ctx.Facts.PortalTied);
        Assert.Equal(0, ctx.Facts.Teleports);
        Assert.Equal(0x0105u, t.R.H.State.Self.Position!.Value.CellId >> 16); // inside the den
        Assert.Equal(SelfTravel.Present, t.R.H.State.Self.Travel);
        Assert.Matches(@"^den=recall portal_tied=yes recalls=1 den_s=\d+\.\d$", run.VerdictFields());
        Assert.Equal(0, t.Places);
    }

    [Fact]
    public void An_untied_mage_is_refused_0x04A3_then_makes_the_one_ground_request_ties_the_portal_and_walks_into_it()
    {
        var t = new J3bRig { PortalTied = false };
        var ctx = t.Context(); // the facts do not know yet (null): the probing recall is cast first
        var (run, status) = t.RunStation(new DenTravelStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        var order = t.Events.Where(e => !e.StartsWith("mode:", StringComparison.Ordinal)).ToList();
        Assert.Equal(["cast:48", "travel:Ground", "cast:47@" + Id(J3bRig.PortalGuid), "travel:Seek", "use:" + Id(J3bRig.PortalGuid), "teleport:" + Id(J3bRig.DenCell)], order);

        // The one budgeted request: Ground to the portal's front; the portal itself is an object goal on foot.
        Assert.Equal(2, t.Travel.Requests.Count);
        var ground = t.Travel.Requests[0];
        Assert.Equal(TravelPurpose.Ground, ground.Purpose);
        Assert.Equal(0u, ground.ObjectGuid);
        Assert.Equal(DenTravelStation.PortalFrontCell, ground.Destination.Location.Position.CellId);
        Assert.Equal(new Vector3(122.40f, 84.43f, 30.01f), ground.Destination.Location.Position.Local);
        var seek = t.Travel.Requests[1];
        Assert.Equal((TravelPurpose.Seek, J3bRig.PortalGuid), (seek.Purpose, seek.ObjectGuid));

        Assert.True(ctx.Facts.PortalTied); // false after the 0x04A3, true after the tie
        Assert.Equal(0, ctx.Facts.Recalls); // the portal walk is no recall
        Assert.Equal(0x0105u, t.R.H.State.Self.Position!.Value.CellId >> 16);
        Assert.Matches(@"^den=portal portal_tied=yes recalls=0 den_s=\d+\.\d$", run.VerdictFields());
        Assert.Equal(0, t.Places);
    }

    [Fact]
    public void When_the_facts_already_say_the_portal_is_untied_no_recall_is_probed()
    {
        var t = new J3bRig { PortalTied = false };
        var ctx = t.Context(f => f.PortalTied = false);
        var (run, status) = t.RunStation(new DenTravelStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.DoesNotContain("cast:48", t.Events);
        Assert.Contains("cast:47@" + Id(J3bRig.PortalGuid), t.Events);
        Assert.True(ctx.Facts.PortalTied);
        Assert.Equal(0, t.Places);
    }

    [Fact]
    public void A_portal_recall_refused_for_any_other_reason_fails_the_station_naming_ACEs_refusal_and_leaves_the_facts_alone()
    {
        var t = new J3bRig { PortalRecallRefusal = 0x04AD };
        var ctx = t.Context();
        var (run, status) = t.RunStation(new DenTravelStation(), ctx);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(run.Ok);
        Assert.Contains("YouCannotRecallPortal 0x04AD", run.Failure, StringComparison.Ordinal);
        Assert.Null(ctx.Facts.PortalTied);
        Assert.Equal(0, ctx.Facts.Recalls);
        Assert.Empty(t.Travel.Requests);
        Assert.Equal(0, t.Places);
    }

    [Fact]
    public void The_hero_den_travel_steps_off_the_lifestone_before_the_portal_recall_and_the_gates_does_not()
    {
        // The M4 hero video: the bind leaves the mage in the lifestone's use range, where P5b J7 filmed the recall's cast through the crystal.
        var t = new J3bRig { RecallIntoLifestone = true };
        var ctx = t.Context();
        var (home, _) = t.RunStation(new RecallHomeStation(), ctx); // the mage in Holtburg, in its lifestone (the rig's walks do not move it)
        Assert.True(home.Ok, home.Failure);
        t.Travel.Requests.Clear();
        t.Events.Clear();

        var (run, status) = t.RunStation(new DenTravelStation(stepOffLifestone: true), ctx);
        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Matches(@"^den=recall portal_tied=yes recalls=1 den_s=\d+\.\d stepped_off=yes$", run.VerdictFields());
        var walk = Assert.Single(t.Travel.Requests);
        Assert.Equal((TravelPurpose.Bind, "holtburg-lifestone-point", 0u), (walk.Purpose, walk.Destination.Name, walk.ObjectGuid));
        Assert.True(t.Events.IndexOf("travel:Bind") < t.Events.IndexOf("cast:48"), string.Join(" > ", t.Events));
        Assert.Single(t.R.H.Log.Lines, l => l.Contains("stepping off it before Primary Portal Recall's cast", StringComparison.Ordinal));

        // The plan: only the hero mage's den-travel steps off; the gate's keeps its fields.
        Assert.Equal("1", JourneyPlans.HeroMage.Stations.Single(s => s.Kind == StationKinds.DenTravel).Arguments[JourneyPlans.StepOffArgument]);
        Assert.False(JourneyPlans.Mage.Stations.Single(s => s.Kind == StationKinds.DenTravel).Arguments.ContainsKey(JourneyPlans.StepOffArgument));
        Assert.False(new DenTravelStation().StepOff);
    }

    [Fact]
    public void A_hero_den_travel_with_no_lifestone_near_walks_nothing_and_says_so()
    {
        var t = new J3bRig(); // the Academy's lifestone stands 8 m from the mage
        var (run, status) = t.RunStation(new DenTravelStation(stepOffLifestone: true), t.Context());

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Matches(@"^den=recall portal_tied=yes recalls=1 den_s=\d+\.\d stepped_off=no$", run.VerdictFields());
        Assert.Empty(t.Travel.Requests);
    }

    // ------------------------------------------------------------------------------------------------ recall home

    [Fact]
    public void The_mage_recalls_home_alone_and_the_recall_is_counted_and_says_the_lifestone_is_tied()
    {
        var t = new J3bRig();
        var ctx = t.Context();
        var (run, status) = t.RunStation(new RecallHomeStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal(1, ctx.Facts.Recalls);
        Assert.True(ctx.Facts.LifestoneTied);
        Assert.Matches(@"^recalls=1 recall_s=\d+\.\d recall_fizzles=0 stepped_off=no$", run.VerdictFields());
        Assert.Equal(0xA9B4u, t.R.H.State.Self.Position!.Value.CellId >> 16);
        Assert.Empty(t.Travel.Requests);
        Assert.Equal(0, t.Places);

        // A second station on the same journey adds a second recall.
        var (again, _) = t.RunStation(new RecallHomeStation(), ctx);
        Assert.True(again.Ok, again.Failure);
        Assert.Equal(2, ctx.Facts.Recalls);
    }

    [Fact]
    public void A_recall_that_arrives_inside_the_lifestone_steps_off_it_to_the_lifestones_known_point_before_the_station_ends()
    {
        // M4 4.2 FS (live gate round 1): ACE recalls to the lifestone's own location, and Lifestone's player does not collide with server objects
        // (TODO(d25-server-object-host)), so a character that logged off there was created inside it on its next login and ACE's placement moved it 2.2 m.
        var t = new J3bRig { RecallIntoLifestone = true };
        var ctx = t.Context();
        var (run, status) = t.RunStation(new RecallHomeStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Matches(@"^recalls=1 recall_s=\d+\.\d recall_fizzles=0 stepped_off=yes$", run.VerdictFields());
        var walk = Assert.Single(t.Travel.Requests);
        Assert.Equal(TravelPurpose.Bind, walk.Purpose);
        Assert.Equal("holtburg-lifestone-point", walk.Destination.Name);
        Assert.Equal(0u, walk.ObjectGuid);
        Assert.Equal(0xA9B40019u, walk.Destination.Location.Position.CellId);
        Assert.Equal(0, t.Places);
        Assert.Single(t.R.H.Log.Lines, l => l.Contains("stepping off it before the station ends", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void A_fighter_recalls_home_through_the_caster_swap_and_ends_in_peace_with_its_weapon(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        var t = new J3bRig(template);
        var ctx = t.Context();
        var (run, status) = t.RunStation(new RecallHomeStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal(1, ctx.Facts.Recalls);
        Assert.Contains("cast:1635", t.Events);
        Assert.Contains("wield:The Healer's Heart", t.SwapRig!.Ops);
        Assert.Equal(CombatModes.NonCombat, t.R.H.State.Self.Qualities.CombatMode);
        Assert.NotEqual(0u, t.SwapRig.LocationOf(C1bSwapRig.WeaponGuid));
        Assert.Equal(0, t.Places);
    }

    [Fact]
    public void A_character_that_is_not_tied_to_a_lifestone_fails_the_recall_named_0x049E_and_the_fact_goes_false()
    {
        var t = new J3bRig { Bound = false };
        var ctx = t.Context(f => f.LifestoneTied = true);
        var (run, status) = t.RunStation(new RecallHomeStation(), ctx);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(run.Ok);
        Assert.Contains("recall:cast", run.Failure, StringComparison.Ordinal);
        Assert.Contains("YouMustLinkToLifestoneToRecall 0x049E", run.Failure, StringComparison.Ordinal);
        Assert.False(ctx.Facts.LifestoneTied);
        Assert.Equal(0, ctx.Facts.Recalls);
        Assert.Contains("recall_why=recall:cast", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0, t.Places);
    }

    [Fact]
    public void The_stations_are_public_sealed_with_plain_constructors_and_the_catalogs_names()
    {
        foreach (var (station, name) in new (IStation, string)[] { (new BindStation(), "bind"), (new DenTravelStation(), "den-travel"), (new RecallHomeStation(), "recall-home"), (new BuildVerifyStation(), "build-verify") })
        {
            Assert.Equal(name, station.Name);
            Assert.True(station.GetType().IsPublic && station.GetType().IsSealed, station.GetType().Name);
        }

        Assert.Equal(StationWhere.Holtburg, new BindStation().Needs.Where);
        Assert.Equal(StationWhere.Holtburg, new BuildVerifyStation().Needs.Where);
    }
}

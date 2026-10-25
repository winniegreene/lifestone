using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// The journey (M4 P5b J1) over <see cref="J1JourneyRig"/> and a scripted station factory: the stages, the station and done lines, a failed station that
/// goes on to the next, the precondition skips, the budget, the recoveries (the pre-journey place, the mage's suicide in the den, a recovery place) and the
/// observer; and the <see cref="ScenarioStation"/> adapter.
/// </summary>
public sealed class J1JourneyScenarioTests
{
    private const string StationLine = @"^journey station=[a-z0-9-]+ ok=(true|false) s=\d+\.\d( \S+=\S+)*$";

    private const string DoneLine =
        @"^journey done ok=(true|false) name=\S+ stations=\d+/\d+ failed=\S+ teleports=\d+ recalls=\d+ budget=\d+ route_m=\d+\.\d run_s=\d+\.\d rungs=\d+ blocked=\d+ sightless=\d+ vitals_passes=\d+ recovery_places=\d+ granted=\d+ recovery_place=[01] recovery=(none|suicide)$";

    private static JourneyPlan J1Plan(string name, params StationSpec[] stations) => new(name, "BSD Mage", "test", StationWhere.Belt, 1, stations);

    private static StationSpec J1Spec(string name, StationNeeds? needs = null, string kind = StationKinds.HarmHeal) => new(name, kind, needs ?? StationNeeds.None, 30);

    [Fact]
    public void A_journey_over_a_scripted_factory_prints_each_station_line_and_the_done_line_with_one_teleport_and_one_recall()
    {
        var t = new J1JourneyRig();
        var factory = new J1Factory();
        var plan = J1Plan(
            "j1",
            J1Spec("build-verify", new(StationWhere.Holtburg), StationKinds.BuildVerify),
            J1Spec("hunt", new(StationWhere.Belt), StationKinds.Hunt),
            J1Spec("harm-heal"),
            J1Spec("vendor", new(StationWhere.Holtburg, Peace: true), StationKinds.Vendor));
        var journey = t.Journey(plan, factory, t.Context());

        Assert.Equal(ScenarioStatus.Succeeded, t.Run(journey));

        Assert.Equal(5, journey.Lines.Count);
        Assert.All(journey.Lines.Take(4), l => Assert.Matches(StationLine, l));
        Assert.Equal("journey station=hunt ok=true s=", journey.Lines[1][..31]);
        Assert.EndsWith(" kills=1", journey.Lines[1], StringComparison.Ordinal);
        Assert.Matches(DoneLine, journey.Lines[4]);
        Assert.StartsWith("journey done ok=true name=j1 stations=4/4 failed=none teleports=1 recalls=1 budget=1 ", journey.Lines[4], StringComparison.Ordinal);
        Assert.Equal(["the belt camp 0xA5B00032"], t.PlacesTo); // the one budgeted teleport
        Assert.Equal([1635u], t.Casts); // home to the vendor by Lifestone Recall
        Assert.Equal(CombatModes.NonCombat, t.R.H.State.Self.Qualities.CombatMode); // the vendor needs peace: restored after the recall's magic mode
        Assert.Equal(["build-verify", "hunt", "harm-heal", "vendor"], factory.Started);
        Assert.Null(journey.Failure);

        // Each stage is logged once, the login skipped in the world.
        var stages = t.Log.Entries.Where(e => e.Message.StartsWith("journey stage ", StringComparison.Ordinal)).Select(e => e.Message["journey stage ".Length..]).ToList();
        Assert.Equal("login skipped=in-world (j1: BSD Mage)", stages[0]);
        Assert.Equal(["known-state", "travel:build-verify", "vitals:build-verify", "station:build-verify", "travel:hunt"], stages.Skip(1).Take(5));
        Assert.Equal("logoff", stages[^1]);
        Assert.Equal(stages.Count, stages.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_failed_station_is_recorded_and_the_journey_recovers_and_goes_on_to_the_next()
    {
        var t = new J1JourneyRig();
        var factory = new J1Factory(new() { ["s2"] = new(Succeed: false, Failure: "boom here", Fields: "kills=0") });
        var journey = t.Journey(J1Plan("j1", J1Spec("s1"), J1Spec("s2"), J1Spec("s3")), factory, t.Context());

        Assert.Equal(ScenarioStatus.Failed, t.Run(journey));

        Assert.Matches(@"^journey station=s2 ok=false s=\d+\.\d kills=0 why=boom_here$", journey.Lines[1]);
        Assert.Matches(@"^journey station=s3 ok=true ", journey.Lines[2]);
        Assert.StartsWith("journey done ok=false name=j1 stations=2/3 failed=s2 teleports=0 ", journey.Lines[3], StringComparison.Ordinal);
        Assert.Equal(["s1", "s2", "s3"], factory.Started);
        Assert.Contains(t.Log.Entries, e => e.Message == "journey stage recover:s2");
        Assert.StartsWith("journey j1 ok=false: failed=s2", journey.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_need_that_cannot_be_restored_skips_the_station_by_name_and_a_skip_after_a_failure_names_its_cause()
    {
        var t = new J1JourneyRig();
        var factory = new J1Factory(new() { ["s3"] = new(Succeed: false) });
        var journey = t.Journey(
            J1Plan("j1", J1Spec("s1"), J1Spec("s2", new(MinManaPercent: 101)), J1Spec("s3"), J1Spec("s4", new(StationWhere.Den))),
            factory,
            t.Context());

        Assert.Equal(ScenarioStatus.Failed, t.Run(journey));

        Assert.Equal("journey station=s2 ok=false s=0.0 skipped=precondition:mana", journey.Lines[1]); // no keeper restores it, nothing caused it
        Assert.Equal("journey station=s4 ok=false s=0.0 skipped=precondition:where caused_by=s3", journey.Lines[3]); // the den is reached only by den-travel
        Assert.StartsWith("journey done ok=false name=j1 stations=1/4 failed=s3 ", journey.Lines[4], StringComparison.Ordinal);
        Assert.Equal(["s1", "s3"], factory.Started);
    }

    [Fact]
    public void The_budget_refuses_a_second_ground_place_and_the_station_is_skipped_naming_the_refusal()
    {
        var t = new J1JourneyRig();
        var journey = t.Journey(J1Plan("j1", J1Spec("hunt", new(StationWhere.Belt)), J1Spec("hunt-2", new(StationWhere.Forest))), new J1Factory(), t.Context());

        Assert.Equal(ScenarioStatus.Failed, t.Run(journey));

        Assert.Equal("journey station=hunt-2 ok=false s=0.0 travel_why=travel:place:over-budget skipped=precondition:where", journey.Lines[1]);
        Assert.StartsWith("journey done ok=false name=j1 stations=1/2 failed=none teleports=1 recalls=0 budget=1 ", journey.Lines[2], StringComparison.Ordinal);
        Assert.Single(t.PlacesTo);
    }

    [Fact]
    public void The_known_state_places_a_player_outside_Holtburg_home_unbudgeted_and_asks_for_peace()
    {
        var t = new J1JourneyRig(J1JourneyRig.BeltCell, peace: false);
        var journey = t.Journey(J1Plan("j1", J1Spec("s1")), new J1Factory(), t.Context());

        Assert.Equal(ScenarioStatus.Succeeded, t.Run(journey));

        Assert.Equal(["Holtburg"], t.PlacesTo);
        Assert.Equal(0xA9B4u, t.Landblock);
        Assert.Equal(CombatModes.NonCombat, t.R.H.State.Self.Qualities.CombatMode);
        Assert.StartsWith("journey done ok=true name=j1 stations=1/1 failed=none teleports=0 ", journey.Lines[^1], StringComparison.Ordinal);
        Assert.EndsWith(" recovery_place=1 recovery=none", journey.Lines[^1], StringComparison.Ordinal);
        Assert.Contains(t.Log.Entries, e => e.Message == "journey known-state peace=yes holtburg=yes recovery_place=1");
    }

    [Fact]
    public void The_mage_whose_way_home_fails_in_the_den_recovers_by_the_suicide_tool()
    {
        var t = new J1JourneyRig { LifestoneTied = false };
        var factory = new J1Factory(new() { ["den-fight"] = new(Succeed: false, OnEnd: _ => t.TeleportNow(J1JourneyRig.DenCell)) });
        var journey = t.Journey(J1Plan(JourneyPlans.Mage.Name, J1Spec("den-fight"), J1Spec("vendor", new(StationWhere.Holtburg))), factory, t.Context());

        Assert.Equal(ScenarioStatus.Failed, t.Run(journey));

        Assert.Single(t.R.H.Net.Of(FakeSendKind.Suicide));
        Assert.Equal(0xA9B4u, t.Landblock);
        Assert.Equal(["den-fight", "vendor"], factory.Started);
        Assert.Matches(@"^journey station=vendor ok=true ", journey.Lines[1]);
        Assert.EndsWith(" recovery_places=0 granted=0 recovery_place=0 recovery=suicide", journey.Lines[^1], StringComparison.Ordinal);
        Assert.False(t.Facts.LifestoneTied);
        Assert.Empty(t.PlacesTo);
    }

    [Fact]
    public void A_fighter_whose_way_home_fails_recovers_by_one_place_outside_the_budget()
    {
        var t = new J1JourneyRig { LifestoneTied = false };
        var factory = new J1Factory(new() { ["hunt"] = new(Succeed: false, OnEnd: _ => t.TeleportNow(J1JourneyRig.BeltCell)) });
        var journey = t.Journey(J1Plan("melee", J1Spec("hunt"), J1Spec("vendor", new(StationWhere.Holtburg))), factory, t.Context());

        Assert.Equal(ScenarioStatus.Failed, t.Run(journey));

        Assert.Equal(["Holtburg"], t.PlacesTo);
        Assert.Equal(["hunt", "vendor"], factory.Started);
        Assert.Contains(" teleports=0 recalls=0 budget=1 ", journey.Lines[^1], StringComparison.Ordinal);
        Assert.EndsWith(" recovery_places=1 granted=0 recovery_place=0 recovery=none", journey.Lines[^1], StringComparison.Ordinal);
        Assert.Empty(t.R.H.Net.Of(FakeSendKind.Suicide));
    }

    [Fact]
    public void The_observer_hears_each_station_start_and_end_and_a_skip_only_ends()
    {
        var t = new J1JourneyRig();
        var observer = new J1Observer();
        var factory = new J1Factory(new() { ["s3"] = new(Succeed: false), ["s1"] = new(Granted: 2) });
        var journey = t.Journey(J1Plan("j1", J1Spec("s1"), J1Spec("s2", new(MinManaPercent: 101)), J1Spec("s3")), factory, t.Context(observer: observer));

        t.Run(journey);

        Assert.Equal(["starting:s1", "ended:s1:true", "ended:s2:false", "starting:s3", "ended:s3:false"], observer.Calls);
        Assert.Contains(" granted=2 ", journey.Lines[^1], StringComparison.Ordinal);
        Assert.Equal(0.5, journey.WhereMaxMetres);
    }

    [Fact]
    public void A_kind_the_factory_does_not_know_is_a_failed_station_naming_it_and_the_journey_goes_on()
    {
        var t = new J1JourneyRig();
        var journey = t.Journey(J1Plan("j1", J1Spec("mystery", kind: "unknown"), J1Spec("s2")), new J1Factory(), t.Context());

        Assert.Equal(ScenarioStatus.Failed, t.Run(journey));

        Assert.StartsWith("journey station=mystery ok=false s=0.0 kind=unknown why=factory:TODO(m4-journey-catalog):_no_station", journey.Lines[0], StringComparison.Ordinal);
        Assert.Matches(@"^journey station=s2 ok=true ", journey.Lines[1]);
    }

    [Fact]
    public void ScenarioStation_asserts_its_needs_on_the_first_step_and_never_creates_the_scenario_on_a_miss()
    {
        var t = new J1JourneyRig();
        var context = t.Context();
        var created = 0;
        var station = new ScenarioStation("den-fight", new(StationWhere.Den), _ => { created++; return new J1Factory().Create(J1Spec("inner")).Start(context); }, s => "inner=" + s.Name, (_, _) => true);
        var run = station.Start(context);

        Assert.Equal(ScenarioStatus.Failed, run.Step(t.View, t.Now));
        Assert.Equal("precondition:where", run.Failure);
        Assert.Equal("why=precondition:where", run.VerdictFields());
        Assert.False(run.Ok);
        Assert.Equal(0, created);
        Assert.Empty(t.PlacesTo);
    }

    [Fact]
    public void ScenarioStation_steps_the_wrapped_scenario_and_reads_its_fields_and_its_verdict_rule_at_the_end()
    {
        var t = new J1JourneyRig();
        var context = t.Context();
        var station = new ScenarioStation(
            "gear",
            new(StationWhere.Holtburg, Peace: true),
            ctx => new J1Factory(new() { ["inner"] = new(Steps: 2, Fields: "worn=7") }).Create(J1Spec("inner")).Start(ctx),
            s => ((IStationRun)s).VerdictFields() + " stage=" + s.Stage,
            (s, status) => status == ScenarioStatus.Succeeded && s.Failure is null);
        var run = station.Start(context);

        Assert.Equal(ScenarioStatus.Running, run.Step(t.View, t.Now));
        Assert.False(run.Ok);
        Assert.Equal(ScenarioStatus.Succeeded, run.Step(t.View, t.Now));
        Assert.True(run.Ok);
        Assert.Equal("worn=7 stage=done", run.VerdictFields());
        Assert.Equal(("gear", StationWhere.Holtburg), (station.Name, station.Needs.Where));
    }

    [Fact]
    public void The_hosts_context_shares_one_facts_object_with_its_travel_and_the_budget_is_the_plans()
    {
        var t = new J1JourneyRig();
        var context = JourneyScenario.NewContext(JourneyPlans.Melee, t.Log, t.Caster.Table);
        Assert.Equal(("Holtburg Brawler", "test-melee", 1), (context.Character, context.Account, context.Facts.TeleportBudget));
        var travel = Assert.IsType<JourneyTravel>(context.Travel);
        Assert.Same(NoJourneyObserver.Instance, context.Observer);

        _ = new JourneyScenario(JourneyPlans.Mage, new J1Factory(), context, endSession: false);
        Assert.Equal(1, context.Facts.TeleportBudget);
        Assert.False(travel.Busy);
    }
}

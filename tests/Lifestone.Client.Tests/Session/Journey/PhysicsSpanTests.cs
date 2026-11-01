using Lifestone.Client.Avatars;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M5 item 5 c1 (report-only): the mage journey's den-fight station line carries <c>creature_jumps= creature_flips= transit_ms_max= place_ms_max= transits_max=</c> over the station's span, read from the host's
/// <see cref="IJourneyWalks.PhysicsCost"/> (the Godot host's avatar set); <c>n/a</c> when the host has none (the headless host). These tests give the seam a scripted cost.
/// </summary>
public sealed class PhysicsSpanTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static AvatarPhysicsCost Cost(int transitions = 0, double transitMs = 0, double placeMs = 0, long jumps = 0, long flips = 0) =>
        new(transitions, transitMs, transitMs, 0, 0, placeMs, placeMs, 0, 0, jumps, flips);

    [Fact]
    public void A_span_with_no_sample_prints_n_a_for_all_five_fields()
    {
        var span = new PhysicsSpan();
        span.Sample(null); // a host with no avatars
        Assert.Equal("creature_jumps=n/a creature_flips=n/a transit_ms_max=n/a place_ms_max=n/a transits_max=n/a", span.Fields());
        Assert.Equal(PhysicsSpan.NotAvailable, span.Fields());
    }

    [Fact]
    public void The_counts_are_the_difference_of_the_totals_and_the_maxima_the_largest_frame_over_the_span()
    {
        var span = new PhysicsSpan();
        span.Sample(Cost(transitions: 1, transitMs: 0.5, placeMs: 0.1, jumps: 10, flips: 4)); // the station's start: totals before it do not count
        span.Sample(Cost(transitions: 6, transitMs: 1.25, placeMs: 0.0, jumps: 10, flips: 5));
        span.Sample(null); // a frame without a set changes nothing
        span.Sample(Cost(transitions: 2, transitMs: 0.75, placeMs: 0.4, jumps: 13, flips: 5));

        Assert.Equal("creature_jumps=3 creature_flips=1 transit_ms_max=1.25 place_ms_max=0.40 transits_max=6", span.Fields());
    }

    [Fact]
    public void A_set_made_again_mid_span_does_not_print_a_negative_count()
    {
        var span = new PhysicsSpan();
        span.Sample(Cost(jumps: 9, flips: 9));
        span.Sample(Cost(jumps: 2, flips: 1)); // the totals started over
        Assert.StartsWith("creature_jumps=0 creature_flips=0 ", span.Fields(), StringComparison.Ordinal);
    }

    /// <summary>A host that runs nothing and reports a scripted physics cost, as the Godot host reports its avatar set's.</summary>
    private sealed class CostHost : IJourneyRunnerHost, IJourneyWalks
    {
        public AvatarPhysicsCost? Now { get; set; }

        public AvatarPhysicsCost? PhysicsCost => Now;

        public ITravelLeg Run(IGameSessionView session, TravelRequest request, double now) => throw new NotSupportedException();

        public IFightWalk? FightWalk(JourneyContext context, Lifestone.Client.Session.Grounds.HuntingGround ground) => null;

        public IHuntFollow? HuntFollow(JourneyContext context) => null;
    }

    private (J3bRig Rig, IStationRun Run) StartDenFight(CostHost? host)
    {
        var t = new J3bRig { RunnersOverride = host, SpellsOverride = SpellTableData.Load(fixture.Dats).Spells };
        var ctx = t.Context();
        // The journey's own den-fight station, from the catalog; the station's needs are lifted to "anywhere" so one step builds the fight over the rig's session.
        var spec = new StationSpec(JourneyPlans.Mage.Stations.First(s => s.Kind == StationKinds.DenFight).Name, StationKinds.DenFight, new StationNeeds(), 215);
        var station = JourneyStations.Create(JourneyPlans.Mage, [], null).Create(spec);
        return (t, station.Start(ctx));
    }

    [RequiresDatsFact]
    public void The_den_fight_station_line_ends_with_the_five_physics_fields_read_from_the_hosts_cost_over_its_span()
    {
        var host = new CostHost { Now = Cost(transitions: 3, transitMs: 0.5, jumps: 7, flips: 2) };
        var (t, run) = StartDenFight(host);

        run.Step(t.View, t.Now); // creates the fight: the span starts here
        host.Now = Cost(transitions: 9, transitMs: 2.5, placeMs: 0.8, jumps: 7, flips: 3);
        t.R.Tick(0.25);
        run.Step(t.View, t.Now);
        host.Now = Cost(transitions: 4, transitMs: 1.0, placeMs: 0.2, jumps: 9, flips: 3);
        t.R.Tick(0.25);
        run.Step(t.View, t.Now);

        var fields = run.VerdictFields();
        Assert.EndsWith(" creature_jumps=2 creature_flips=1 transit_ms_max=2.50 place_ms_max=0.80 transits_max=9", fields, StringComparison.Ordinal);
        Assert.Contains(" kills=", fields, StringComparison.Ordinal); // the fight's own fields are still there, in front
    }

    [RequiresDatsFact]
    public void A_host_with_no_physics_cost_gives_the_den_fight_line_n_a_for_all_five()
    {
        var (t, run) = StartDenFight(new CostHost());

        run.Step(t.View, t.Now);

        Assert.EndsWith(" " + PhysicsSpan.NotAvailable, run.VerdictFields(), StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void A_host_that_is_not_a_walks_host_at_all_gives_n_a_too()
    {
        var (t, run) = StartDenFight(null); // Runners null: a scripted host

        run.Step(t.View, t.Now);

        Assert.EndsWith(" " + PhysicsSpan.NotAvailable, run.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_body_position_reads_the_hunt_follower_and_a_host_without_either_says_nothing()
    {
        var t = new J3bRig { Body = J3bRig.BodyNorthOfArrival(1f) };
        var withFollower = (IJourneyWalks)t.Context().Runners!; // the rig's BodyHost: HuntFollow only, BodyPosition is the interface default
        Assert.Equal((CellFrame?)J3bRig.BodyNorthOfArrival(1f), withFollower.BodyPosition(t.Context()));
        Assert.Null(((IJourneyWalks)new CostHost()).BodyPosition(t.Context())); // no follower, no body view: unknown
        Assert.Null(new CostHost().PhysicsCost);
    }
}

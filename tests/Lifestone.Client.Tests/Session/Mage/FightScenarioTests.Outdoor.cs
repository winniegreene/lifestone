using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 0 a5 (D33, the mage's outdoor fight): the fight on an outdoor ground (the belt's profile, <c>low-b-belt</c>): its tour walks the profile's <c>outdoor.cells</c> in order, each a point goal through the travel seam with
/// the arrival checked; its targets are the profile's <c>outdoor.creatures</c> within the band, and the band follows the character (a creature above level + 5 is skipped, logged; an attacker above it is fought back, D88);
/// the den fight's bolt cap argument reaches <c>PlanCap</c>; the verdict and the kill line carry <c>casts=</c>; a cancelled leg's walking seconds count. Nothing here needs the dats or a server.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const string Belt = "low-b-belt";
    private const uint BeltCell = 0xA5B00032; // the belt profile's first outdoor cell (its arrival)
    private const uint BanderlingScout = 6; // level 8: in a level-5 mage's band (top 10)
    private const uint AdultReedshark = 221; // level 15: in the ground's band 8-20, above a level-5 mage's
    private const uint ScoutGuid = 0x80003001;
    private const uint SharkGuid = 0x80003002;

    private static HuntingGround BeltGround => GroundCatalog.Get(Belt);

    /// <summary>The server puts the player in <paramref name="cell"/> (a later position stamp than the rig's first).</summary>
    private static void PlayerTo(Rig r, uint cell)
    {
        r.R.H.R.Reducer.OnUpdatePosition(r.R.H.R.Context, ReducerHarness.Move(ActionHarness.Amy, 1, 3, 2, 1, cell: cell, x: PlayerX));
        r.R.H.Unhide(stateStamp: 5); // ACE clears Hidden: the player is present
    }

    /// <summary>The outskirts rung's <c>target_margin</c> (ladder/war-mage.json, M6 0 F5): the level up job passes it to the fight; the fight itself holds no margin constant.</summary>
    private const int RungMargin = 5;

    private static Rig BeltRig(int level = 5)
    {
        var r = new Rig(fight: new FightOptions { Ground = Belt, TargetMargin = RungMargin }, characterLevel: level);
        PlayerTo(r, BeltCell);
        return r;
    }

    [Fact]
    public void The_outdoor_tour_offers_the_profiles_cells_in_order_round_and_round_skipping_the_players_own_cell()
    {
        var cells = BeltGround.Outdoor!.Cells.Select(c => c.Cell).ToArray();
        var tour = new OutdoorTour(BeltGround);

        var offers = Enumerable.Range(0, cells.Length).Select(_ => tour.Next(0, 0)!.Value).ToArray();
        Assert.Equal(cells, offers);
        Assert.Equal(cells[1], tour.Next(cells[0], 0)); // round again, the player's own cell left out
        Assert.Equal(cells.Length, tour.Waypoints);
        Assert.Equal(TourPurpose.Order, tour.Purpose);
        Assert.Contains("outdoor cell 2 of 10", tour.PurposeNote, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new OutdoorTour(DenProfile()));
    }

    [Fact]
    public void An_outdoor_grounds_targets_are_its_creatures_within_the_band_on_its_outdoor_cells_and_a_dungeons_are_unchanged()
    {
        var targets = FightTargets.Of(BeltGround);
        Assert.All(targets, t => Assert.InRange(t.Level, 8, 20));
        Assert.DoesNotContain(targets, t => t.Wcid == 182); // the Auroch Yearling, level 4: under the band
        Assert.DoesNotContain(targets, t => t.Wcid == 233); // the Tumerok Warrior, level 30: over it
        Assert.Equal(targets.Count, targets.Select(t => t.Wcid).Distinct().Count());
        Assert.Equal(DenProfile().TargetWcids(), FightTargets.Wcids(DenProfile()));

        var (kept, skipped) = FightTargets.Band(targets, 5, RungMargin);
        Assert.Contains(BanderlingScout, kept);
        Assert.Contains(skipped, t => t.Wcid == AdultReedshark);
        Assert.All(skipped, t => Assert.True(t.Level > 5 + RungMargin));
        Assert.DoesNotContain(FightTargets.Band(targets, 5, 3).Kept, w => targets.Single(t => t.Wcid == w).Level > 5 + 3); // a smaller margin from another rung narrows the pick

        var cells = FightTargets.CellFilter(BeltGround);
        Assert.True(cells(BeltCell));
        Assert.False(cells(0xA5B00105)); // an indoor cell of the same landblock
        Assert.False(cells(0xA9B40019)); // Holtburg: not the ground's
        Assert.True(FightTargets.CellFilter(DenProfile())(0x01050303));
        Assert.Null(FightOptions.GroundError(Belt));
        Assert.Null(FightOptions.GroundError(FightOptions.DefaultGround));
        Assert.Contains(Belt, FightOptions.GroundError("no-such-ground")!, StringComparison.Ordinal);
    }

    [Fact]
    public void On_the_belt_the_pick_skips_a_creature_above_the_characters_band_logged_and_kills_one_in_it_with_casts_on_the_kill_line()
    {
        var r = BeltRig(level: 5);
        var shark = r.Add(SharkGuid, AdultReedshark, BeltCell, 30, "Adult Reedshark"); // 10 m, nearer, level 15
        var scout = r.Add(ScoutGuid, BanderlingScout, BeltCell, 45, "Banderling Scout"); // 25 m, level 8
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(scout.Dead);
        Assert.False(shark.Dead);
        Assert.All(r.CastTargets, t => Assert.Equal(ScoutGuid, t));
        Assert.Contains(scenario.BandSkipped, t => t.Wcid == AdultReedshark);
        Assert.Equal(1, LinesWith(r, "targets: the band follows the character: levels 8-10 of the ground's 8-20 (level 5 + 5)"));
        Assert.Equal(1, LinesWith(r, "targets: skipped Adult Reedshark 0x80003002 (level 15) 10 m away: above the band's top 10"));
        Assert.Single(r.Log.Lines, l => l.Contains("kill 1: casts=", StringComparison.Ordinal) && l.Contains("(You have killed Banderling Scout!)", StringComparison.Ordinal)); // the casts counted when the notice came (the scripted ACE counts the killing cast's answer after it)
        Assert.Contains(" casts=2 walk_s=n/a picks=", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_fight_given_no_target_margin_does_not_band_the_pick_and_logs_the_TODO_once_M6_0_F5()
    {
        var r = new Rig(fight: new FightOptions { Ground = Belt }, characterLevel: 5);
        PlayerTo(r, BeltCell);
        r.Add(SharkGuid, AdultReedshark, BeltCell, 30, "Adult Reedshark");
        var scenario = r.Scenario(new FakeWalk());

        r.Run(scenario);

        Assert.Empty(scenario.BandSkipped);
        Assert.Equal(1, LinesWith(r, "TODO(m6-fight-no-target-margin)"));
        Assert.Equal(0, LinesWith(r, "targets: the band follows the character"));
    }

    [Fact]
    public void On_the_belt_an_attacker_above_the_characters_band_is_fought_back_D88()
    {
        var r = BeltRig(level: 5);
        var shark = r.Add(SharkGuid, AdultReedshark, BeltCell, 30, "Adult Reedshark");
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        MeleeHit(r, "Adult Reedshark");
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(shark.Dead);
        Assert.NotEmpty(r.CastTargets);
        Assert.All(r.CastTargets, t => Assert.Equal(SharkGuid, t));
        Assert.Equal(1, scenario.Defended);
    }

    [Fact]
    public void The_den_fights_max_level_argument_reaches_the_fights_options_and_PlanCap_unless_the_command_line_gave_one()
    {
        var spec = new StationSpec("den-fight", StationKinds.DenFight, null, 0, new Dictionary<string, string>(StringComparer.Ordinal) { [JourneyPlans.MaxLevelArgument] = "2" });

        Assert.Equal(2, JourneyStations.DenFightOptions(spec, new FightOptions(), []).MaxLevel);
        var given = CommandLineArgument.ParseAll(["--fight-max-level=4"]);
        Assert.Equal(4, JourneyStations.DenFightOptions(spec, FightOptions.FromArguments(given), given).MaxLevel);
        Assert.Equal(FightOptions.DefaultMaxLevel, JourneyStations.DenFightOptions(spec with { Args = null }, new FightOptions(), []).MaxLevel);

        Assert.Equal(2, FightScenario.PlanCap(BeltGround, 6, 2, out var note)); // the belt has no plan row: the argument is the cap
        Assert.Contains("no row for level 6", note, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ladder_reads_casts_a_kill_from_the_fight_lines_casts()
    {
        Assert.Equal(3.0, AutopilotScenario.CastsAKill(12, 4));
        Assert.Null(AutopilotScenario.CastsAKill(null, 4)); // no fight line carried casts=
        Assert.Null(AutopilotScenario.CastsAKill(5, 0));
    }

    [Fact]
    public void The_outdoor_walk_makes_each_cell_a_point_goal_through_the_travel_seam_and_checks_the_arrival_on_the_ground()
    {
        var r = BeltRig();
        var cells = BeltGround.Outdoor!.Cells;
        var travel = new ScriptedTravel();
        using var walk = new OutdoorFightWalk(BeltGround, travel, (_, _, _) => 10f, r.Log);

        Assert.True(walk.BeginLeg(r.View, 0, out var first));
        Assert.Equal(FightWalkStatus.Arrived, walk.Tick(r.View, 1));
        Assert.True(walk.BeginLeg(r.View, 2, out _));
        Assert.Equal(FightWalkStatus.Arrived, walk.Tick(r.View, 3));

        Assert.Equal([cells[1].Cell, cells[2].Cell], travel.Requests.Select(q => q.Destination.Location.Position.CellId)); // the player stands in the first cell
        Assert.All(travel.Requests, q => Assert.Equal(TravelPurpose.Cell, q.Purpose));
        Assert.Equal(new System.Numerics.Vector3(cells[1].Spot.X, cells[1].Spot.Y, 10 + OutdoorFightWalk.StandOffMetres), travel.Requests[0].Destination.Location.Position.Local);
        Assert.Contains("outdoor cell 0xA4B0003F by run", first, StringComparison.Ordinal);
        Assert.Equal(2, LinesWith(r, "outdoor walk: check before leg"));
        Assert.Equal(1, LinesWith(r, "leg 1 arrived at outdoor cell 0xA4B0003F by run: check after: the player in 0xA5B00032"));
        Assert.Same(walk.Tour, walk.Tour as OutdoorTour);
    }

    [Fact]
    public void The_outdoor_walk_skips_a_cell_with_no_terrain_height_named_and_fails_after_three_legs_off_the_ground()
    {
        var r = new Rig(fight: new FightOptions { Ground = Belt }); // the player stays in the den: every arrival's check fails
        var cells = BeltGround.Outdoor!.Cells;
        var travel = new ScriptedTravel();
        using var walk = new OutdoorFightWalk(BeltGround, travel, (cell, _, _) => cell == cells[0].Cell ? null : 10f, r.Log);

        Assert.True(walk.BeginLeg(r.View, 0, out _));
        Assert.Equal(cells[1].Cell, travel.Requests[0].Destination.Location.Position.CellId);
        Assert.Equal(1, LinesWith(r, "outdoor walk: cell 0xA5B00032 has no terrain height"));

        Assert.Equal(FightWalkStatus.Walking, walk.Tick(r.View, 1)); // the check after fails: recovered onto the next cell
        Assert.Equal(FightWalkStatus.Walking, walk.Tick(r.View, 2));
        Assert.Equal(FightWalkStatus.Failed, walk.Tick(r.View, 3));
        Assert.Contains("3 legs failed in a row", walk.Failure, StringComparison.Ordinal);
        Assert.Contains("not present on the ground", walk.Failure, StringComparison.Ordinal);
        Assert.Equal(2, walk.LegsRecovered);
    }

    [Fact]
    public void A_cancelled_legs_walking_seconds_are_logged_and_counted_in_the_walks_total_m6_walk_cancel_seconds()
    {
        var r = BeltRig();
        var travel = new ScriptedTravel { Status = TravelStatus.Running };
        using var walk = new OutdoorFightWalk(BeltGround, travel, (_, _, _) => 10f, r.Log);

        Assert.True(walk.BeginLeg(r.View, 0, out _));
        walk.Tick(r.View, 1);
        walk.Tick(r.View, 2.5);
        walk.Abandon();
        Assert.Equal(1, LinesWith(r, "cancelled after walk_s=2.5 (counted in the walk's total 2.5 s: m6-walk-cancel-seconds)"));
        Assert.True(walk.BeginLeg(r.View, 3, out _));
        walk.Tick(r.View, 4);
        Assert.Equal(3.5, walk.WalkSecondsTotal!.Value, 6);

        var pacing = new LegPacing();
        pacing.BeginWalk(0);
        pacing.Walked(true, 2);
        pacing.BeginWalk(2); // the leg before was cancelled: a new leg's clock starts, the total keeps it
        pacing.Walked(true, 3);
        Assert.Equal((1.0, 3.0), (pacing.WalkSeconds, pacing.TotalWalkSeconds));
    }

    private static HuntingGround DenProfile() => GroundCatalog.Get(FightOptions.DefaultGround);

    /// <summary>A travel seam whose legs end as <see cref="Status"/> says, by <see cref="Means"/>, each request recorded.</summary>
    private sealed class ScriptedTravel : IJourneyTravel
    {
        public List<TravelRequest> Requests { get; } = [];

        public TravelStatus Status { get; set; } = TravelStatus.Arrived;

        public TravelMeans Means { get; set; } = TravelMeans.Run;

        public bool Busy => false;

        public ITravelLeg GoTo(IGameSessionView session, TravelRequest request, double now)
        {
            Requests.Add(request);
            return new Leg(this);
        }

        private sealed class Leg(ScriptedTravel travel) : ITravelLeg
        {
            public TravelMeans Means => travel.Means;

            public string? Failure => "travel:run:scripted";

            public string Fields => string.Empty;

            public TravelStatus Step(IGameSessionView session, double now) => travel.Status;

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

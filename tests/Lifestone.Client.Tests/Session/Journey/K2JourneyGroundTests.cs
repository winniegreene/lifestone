using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M5 item 2 k2 (D46, "the journeys on the ground"): the gate's mage tours two storeys of the den with the time its walk needs, the archer's forest hunt has the time its 33 spots need, the journeys' den stations take the journey's ground
/// (<c>--fight-ground</c>) and the den-travel station reads the ground profile's portal, with no den fact left in the journey's code.
/// </summary>
public sealed class K2JourneyGroundTests
{
    private const string DenKey = "black-spawn-den-aluvian";
    private const string TuskerKey = "tusker-temple-fort-tethana";

    private static HuntingGround Den => GroundCatalog.Get(DenKey);

    private static HuntingGround Tusker => GroundCatalog.Get(TuskerKey);

    private static StationSpec Spec(JourneyPlan plan, string kind) => plan.Stations.First(s => s.Kind == kind);

    private static CellFrame At(uint cell) => new(cell, new AcFrame(new Vector3(10f, 10f, 0f), Quaternion.Identity));

    // ------------------------------------------------------------------------------------------------ the mage's den fight (item 1)

    [Fact]
    public void The_mages_den_fight_asks_for_three_kills_and_a_tour_of_two_storeys_with_the_walks_seconds()
    {
        var spec = Spec(JourneyPlans.Mage, StationKinds.DenFight);
        Assert.Equal(("3", "2"), (spec.Arguments[JourneyPlans.KillsArgument], spec.Arguments[JourneyPlans.StoreysMinArgument]));
        Assert.Equal(JourneyPlans.MageDenFightSeconds, spec.ExpectedSeconds);
        Assert.Equal(215, spec.ExpectedSeconds); // 150 - 15 + 33 (about 330 m at about 10 m/s) + 3 x 15, rounded
        Assert.Equal((215 * 3) + 300, JourneyStations.Create(JourneyPlans.Mage, [], null).StationLimitSeconds(spec));
        Assert.Equal(945, JourneyLimits.StationSeconds(spec, new FightOptions()));

        var fight = JourneyStations.DenFightOptions(spec, new FightOptions(), CommandLineArgument.ParseAll([]));
        Assert.Equal((3, 2), (fight.Kills, fight.StoreysMin));
        Assert.True(fight.Kills >= 2, "after one kill on a storey the tour leaves it, so the second storey needs a second kill");
        Assert.False(fight.LeaveOnFailure);
        Assert.False(fight.LeaveAtEnd);
    }

    [Fact]
    public void The_command_line_wins_over_the_plans_kills_and_tour_depth_and_the_other_plans_are_untouched()
    {
        string[] args = ["--fight-kills=5", "--fight-storeys-min=3"];
        var spec = Spec(JourneyPlans.Mage, StationKinds.DenFight);
        var fight = JourneyStations.DenFightOptions(spec, JourneyLimits.FightOf(args), CommandLineArgument.ParseAll(args));
        Assert.Equal((5, 3), (fight.Kills, fight.StoreysMin));

        // The hero mage's fight (the ring of fire, three kills) keeps the one-storey tour of its video.
        var hero = JourneyStations.DenFightOptions(Spec(JourneyPlans.HeroMage, StationKinds.DenFight), new FightOptions(), CommandLineArgument.ParseAll([]));
        Assert.Equal((3, FightOptions.DefaultStoreysMin), (hero.Kills, hero.StoreysMin));
    }

    // ------------------------------------------------------------------------------------------------ the archer's forest hunt (item 4)

    [Fact]
    public void The_archers_forest_hunt_has_the_time_its_33_spots_need_and_the_stations_limit_holds_it()
    {
        Assert.Equal(1200, JourneyStations.ArcherHuntSeconds);
        foreach (var plan in new[] { JourneyPlans.Archer, JourneyPlans.HeroArcher })
        {
            var hunt = Spec(plan, StationKinds.Hunt);
            Assert.Equal(400, hunt.ExpectedSeconds);
            Assert.Equal(JourneyPlans.ArcherHuntExpectedSeconds, hunt.ExpectedSeconds);
            var limit = JourneyStations.Create(plan, [], null).StationLimitSeconds(hunt);
            Assert.Equal(1500, limit);
            Assert.True(limit > JourneyStations.ArcherHuntSeconds, "the hunt ends by its own named reason, not the station's timeout");
        }
    }

    [Fact]
    public void The_whole_run_limits_hold_the_archers_other_stations_and_the_whole_hunt()
    {
        var archer = JourneyPlans.Archer;
        Assert.Equal(800, JourneyLimits.HuntExtraSeconds(archer));
        Assert.Equal(800, JourneyLimits.WholeExtraSeconds(archer, new FightOptions()));
        Assert.Equal(0, JourneyLimits.HuntExtraSeconds(JourneyPlans.Mage));
        Assert.Equal(0, JourneyLimits.HuntExtraSeconds(JourneyPlans.Melee)); // the Brawler's hunt keeps its 300 s
        Assert.Equal(0, JourneyLimits.WholeExtraSeconds(JourneyPlans.Mage, new FightOptions()));

        // The headless limit is the plan's sum + 300 + the extra; the worst case is every other station at its expected seconds and the hunt running out its 1,200 s.
        var worst = (archer.ExpectedSeconds - JourneyPlans.ArcherHuntExpectedSeconds) + JourneyStations.ArcherHuntSeconds;
        Assert.True(archer.ExpectedSeconds + 300 + JourneyLimits.WholeExtraSeconds(archer, new FightOptions()) >= worst + 300);
    }

    // ------------------------------------------------------------------------------------------------ the plan's ground (item 2)

    [Fact]
    public void The_dens_arrival_and_landblocks_are_the_journeys_ground_not_the_default()
    {
        Assert.NotEqual(Den.Arrival.Cell >> 16, Tusker.Arrival.Cell >> 16);
        Assert.Equal(Den.Arrival.Cell, JourneyGrounds.For(StationWhere.Den, null, Den)!.Location.Position.CellId);
        Assert.Equal(Tusker.Arrival.Cell, JourneyGrounds.For(StationWhere.Den, null, Tusker)!.Location.Position.CellId);

        var inDen = At(Den.Arrival.Cell);
        var inTusker = At(Tusker.Arrival.Cell);
        Assert.True(JourneyGrounds.At(StationWhere.Den, inDen, Den));
        Assert.False(JourneyGrounds.At(StationWhere.Den, inDen, Tusker));
        Assert.True(JourneyGrounds.At(StationWhere.Den, inTusker, Tusker));
        Assert.False(JourneyGrounds.At(StationWhere.Den, inTusker, Den));
        Assert.False(JourneyGrounds.At(StationWhere.Den, null, Den));
        Assert.Throws<ArgumentNullException>(() => JourneyGrounds.At(StationWhere.Den, inDen));
    }

    [Fact]
    public void The_context_carries_the_ground_the_hosts_name_and_the_default_is_the_options_default()
    {
        var plain = JourneyScenario.NewContext(JourneyPlans.Mage, NullLogSink.Instance, new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>());
        Assert.Equal(FightOptions.DefaultGround, plain.Dungeon.Key);
        var chosen = JourneyScenario.NewContext(JourneyPlans.Mage, NullLogSink.Instance, new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>(), dungeon: Tusker);
        Assert.Same(Tusker, chosen.Dungeon);
        Assert.Equal(TuskerKey, JourneyLimits.FightOf([$"--fight-ground={TuskerKey}"]).GroundProfile.Key); // what the hosts pass
    }

    [Fact]
    public void A_stations_where_need_is_read_against_the_journeys_ground()
    {
        var rig = new J3bRig();
        var needs = new StationNeeds(StationWhere.Den);
        rig.Teleport(Den.Arrival.Cell);
        Assert.Null(StationPreconditions.Miss(needs, rig.R.H.State, null, Den));
        Assert.Equal("where", StationPreconditions.Miss(needs, rig.R.H.State, null, Tusker));
    }

    // ------------------------------------------------------------------------------------------------ the den travel from the profile (item 3)

    [Fact]
    public void The_den_travel_ties_at_the_front_point_the_grounds_portal_names()
    {
        var portal = Den.Portal! with { Wcid = 7242, Front = new Vector3(100.5f, 60.25f, 12f), FrontCell = 0x1B900015 };
        var ground = Den with { Portal = portal };
        var t = new J3bRig { PortalTied = false };
        var ctx = t.Context(f => f.SetPortalTie(J3bRig.DenKey, PortalTie.Untied), ground);

        var (run, status) = t.RunStation(new DenTravelStation(), ctx);

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        var front = t.Travel.Requests[0];
        Assert.Equal(TravelPurpose.Ground, front.Purpose);
        Assert.Equal((portal.FrontCell, portal.Front), (front.Destination.Location.Position.CellId, front.Destination.Location.Position.Local));
    }

    [Fact]
    public void The_den_travel_falls_back_to_the_portals_own_cell_and_point_when_the_object_has_no_position_not_to_its_front_cell()
    {
        // M5 2 fix F2 (review 2-R16): the Seek to the portal fell back to (FrontCell, Origin); the record has its own Cell. The two cells differ here so that a wrong one cannot pass.
        var portal = Den.Portal! with { Cell = 0x1B900031, Origin = new Vector3(122.404f, 87.43f, 29.937f), FrontCell = 0x1B90002C, Front = new Vector3(122.40f, 84.43f, 30.01f) };
        Assert.NotEqual(portal.Cell, portal.FrontCell);
        var ground = Den with { Portal = portal };

        var unplaced = new J3bRig(portalPlaced: false) { PortalTied = false };
        var (run, status) = unplaced.RunStation(new DenTravelStation(), unplaced.Context(f => f.SetPortalTie(J3bRig.DenKey, PortalTie.Untied), ground));
        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        var seek = unplaced.Travel.Requests[1];
        Assert.Equal((TravelPurpose.Seek, J3bRig.PortalGuid), (seek.Purpose, seek.ObjectGuid));
        Assert.Equal((portal.Cell, portal.Origin), (seek.Destination.Location.Position.CellId, seek.Destination.Location.Position.Local));

        // With the object placed, the walk goes to where the server put it, as before.
        var placed = new J3bRig { PortalTied = false };
        var (placedRun, placedStatus) = placed.RunStation(new DenTravelStation(), placed.Context(f => f.SetPortalTie(J3bRig.DenKey, PortalTie.Untied), ground));
        Assert.True(placedStatus == ScenarioStatus.Succeeded && placedRun.Ok, placedRun.Failure + " / " + placedRun.VerdictFields());
        Assert.Equal(J3bRig.AcademyCell, placed.Travel.Requests[1].Destination.Location.Position.CellId);
    }

    [Fact]
    public void The_den_travel_looks_for_the_portal_by_the_grounds_weenie_class_and_a_ground_without_a_portal_fails_the_station_named()
    {
        var t = new J3bRig { PortalTied = false };
        var other = Den with { Portal = Den.Portal! with { Wcid = 99999 } };
        var (run, status) = t.RunStation(new DenTravelStation(), t.Context(f => f.SetPortalTie(J3bRig.DenKey, PortalTie.Untied), other));
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("no object of weenie 99999", run.Failure, StringComparison.Ordinal);

        var none = new J3bRig { PortalTied = false };
        var (bare, bareStatus) = none.RunStation(new DenTravelStation(), none.Context(f => f.SetPortalTie(J3bRig.DenKey, PortalTie.Untied), Den with { Portal = null }));
        Assert.Equal(ScenarioStatus.Failed, bareStatus);
        Assert.Contains("has no portal record", bare.Failure, StringComparison.Ordinal);
        Assert.Contains("TODO(m6-ground-fields)", bare.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_dungeon_profiles_carry_their_portals_weenie_origin_and_tie_point()
    {
        var den = Den.Portal!;
        Assert.Equal((7242u, new Vector3(122.404f, 87.43f, 29.937f), 0x1B90002Cu, new Vector3(122.40f, 84.43f, 30.01f)), (den.Wcid, den.Origin, den.FrontCell, den.Front));
        var tusker = Tusker.Portal!;
        Assert.Equal((22696u, new Vector3(57.7292f, 64.9681f, 70.005f), 0x2A800013u), (tusker.Wcid, tusker.Origin, tusker.FrontCell));
        Assert.Equal(new Vector3(57.8509f, 67.9656f, 70.01f), tusker.Front);
    }

    [Theory]
    [InlineData(DenKey, 0.008428f, 0.999964f, 7242u, 0.05f)] // the pinned world SQL v0.9.296 row of guid 0x71B90000: the portal faces south; the W0 stand lies 3 m along that facing to within 5 cm
    [InlineData(TuskerKey, 0.999794f, -0.02029f, 22696u, 0.001f)] // the row of guid 0x72A80000: yaw -2.32 degrees, facing north
    public void The_tie_point_is_three_metres_along_the_portals_facing_from_its_row(string key, float w, float z, uint wcid, float tolerance)
    {
        var portal = GroundCatalog.Get(key).Portal!;
        Assert.Equal(wcid, portal.Wcid);
        var forward = Vector3.Transform(Vector3.UnitY, new Quaternion(0f, 0f, z, w));
        var expected = portal.Origin + (3f * new Vector3(forward.X, forward.Y, 0f));
        Assert.InRange(Math.Abs(portal.Front.X - expected.X), 0f, tolerance);
        Assert.InRange(Math.Abs(portal.Front.Y - expected.Y), 0f, tolerance);
        Assert.InRange(Math.Abs(portal.Front.Z - portal.Origin.Z), 0f, 0.1f);
        var outdoorCell = 0x01u + (uint)(Math.Floor(portal.Front.X / 24f) * 8) + (uint)Math.Floor(portal.Front.Y / 24f); // x/24 * 8 + y/24 + 1 with whole divisions
        Assert.Equal(portal.FrontCell & 0xFFFFu, outdoorCell);
    }

    // ------------------------------------------------------------------------------------------------ the sources (item 5)

    [Fact]
    public void No_profile_sources_string_cites_a_name_the_code_no_longer_has()
    {
        string[] removed = ["BlackSpawnDen.", "ArenaProfile.TargetWcids", "DenRooms", "DenWalk"];
        foreach (var ground in GroundCatalog.All)
        {
            foreach (var (section, text) in ground.Sources)
            {
                foreach (var name in removed)
                {
                    Assert.False(text.Contains(name, StringComparison.Ordinal), $"{ground.Key} sources.{section} cites {name}");
                }
            }
        }
    }
}

using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Data.Animation;
using Lifestone.Data.Magic;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 14 holding plates (the owner, 2026-11-14: in the Tusker Holding the climber must avoid the Tusker's Friend pressure plates, and where one blocks the way, she can jump): the Holding's 18 plates are facts of its profile and walls
/// of its walks. Proved: the profile's plates are the world's 18 (the pinned world SQL) and no plate stands in the Quarters, the Abode or the Habitat; the plate is a small sphere whose reach and top the profile holds, and its spells
/// strip her own level VI auras and leave the buff bot's level VII lines; no walked step of any leg of the Holding's tour, planned with the walls and the climber's jumps, comes within a wall; the walls close the Holding past the
/// row in 0x5B480373 and the standing jumps open it; and the Quarters' walk reaches every slot with no jump.
/// </summary>
public sealed class HoldingPlatesTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint PlateWcid = 22719;

    /// <summary>The 18 plates of 0x5B48 (landblock_instance, wcid 22719; the world dump test below checks them).</summary>
    private static readonly Vector3[] Plates =
    [
        new(130.363f, -63.372f, -11.995f), new(130.468f, -60.726f, -11.995f), new(70f, -60f, 0.005f), new(129.457f, -130.503f, 0.005f), new(150f, -120f, 0.005f), new(194.937f, -129.681f, 0.005f),
        new(251.099f, -81.735f, 0.005f), new(248.856f, -81.68f, 0.005f), new(251.186f, -79.247f, 0.033f), new(248.776f, -79.026f, 0.005f), new(248.664f, -76.595f, 0.005f), new(251.181f, -76.894f, 0.005f),
        new(283.285f, -69.608f, 0.005f), new(296.125f, -50f, 0.005f), new(298.918f, -50f, 0.005f), new(301.439f, -50f, 0.005f), new(303.863f, -49.946f, 0.005f), new(340.048f, -70.586f, 0.005f),
    ];

    private static HuntingGround Holding => GroundCatalog.Get("tusker-holding");

    /// <summary>The Holding's room graph planned as the buffed climber (Run 103, Jump 178: PitJumpDatTests' Qwinnie) or the bare one (Run 23, Jump 98).</summary>
    private RoomGraph Graph(bool buffed)
    {
        var graph = GroundRooms.Load(fixture.Dats, Holding);
        var weenie = buffed ? new OfflinePlayerWeenie(103, 178) : new OfflinePlayerWeenie(23, 98);
        Assert.True(weenie.TryGetRunRate(out var rate));
        graph.PlateSpeedMetresPerSecond = MotionInterpreter.RunAnimSpeed * rate;
        graph.JumpReachMetres = JumpReach.FlightMetres(weenie);
        return graph;
    }

    [Fact]
    public void The_Holdings_plates_are_its_profiles_walls_and_the_Quarters_the_Abode_and_the_Habitat_have_none()
    {
        var plates = Assert.IsType<GroundPlates>(Holding.Plates);
        Assert.Equal((PlateWcid, "Tusker's Friend", 0.24f, 0.18f), (plates.Wcid, plates.Name, plates.ShapeMetres, plates.TopMetres));
        Assert.Equal(new uint[] { 1878, 2166 }, plates.Spells.Select(s => s.Id));
        Assert.Equal(18, plates.Points.Count);
        Assert.All(Plates, p => Assert.Contains(plates.Points, q => Vector3.Distance(p, q.Point) < 0.001f));
        Assert.All(plates.Points, p => Assert.Equal(0x5B48u, p.Cell >> 16));
        Assert.Equal(16, plates.Points.Count(p => Holding.IsWalkable(p.Cell))); // two stand on the lower level's bridge 0x5B4801B0
        Assert.Equal(18, Holding.KeptPlates.Count);
        Assert.Empty(Holding.AvoidRegions); // the nine costly boxes are gone: a plate is a wall, not a cost
        foreach (var key in new[] { "tusker-quarters", "tusker-abode", "tusker-habitat" })
        {
            Assert.Null(GroundCatalog.Get(key).Plates);
            Assert.Empty(GroundCatalog.Get(key).AvoidRegions);
        }

        Assert.Contains("TODO(m6-holding-plates) closed", Holding.Sources["no_go"], StringComparison.Ordinal);
        Assert.DoesNotContain("0x5B48039F to 0x5B4803D8, each", Holding.Sources["no_go"], StringComparison.Ordinal); // the earlier note, refuted
    }

    [RequiresPinnedWorldSqlFact]
    public void Every_plate_of_the_world_is_a_profile_plate_and_none_stands_on_the_other_three_grounds()
    {
        using var zip = File.OpenRead(RequiresPinnedWorldSqlFactAttribute.Find()!);
        var sql = WorldSqlDump.ReadZip(zip, new Dictionary<string, Func<SqlRow, bool>>
        {
            ["landblock_instance"] = r => r.L("weenie_Class_Id") == PlateWcid,
            ["spell"] = r => r.L("id") == 1878,
        });
        var rows = sql["landblock_instance"].View().ToList();
        var here = rows.Where(r => (r.L("obj_Cell_Id") >> 16) == 0x5B48).ToList();
        Assert.Equal(18, here.Count);
        foreach (var r in here)
        {
            var plate = Assert.Single(Holding.Plates!.Points, p => p.Guid == (uint)r.L("guid"));
            Assert.Equal((uint)r.L("obj_Cell_Id"), plate.Cell);
            Assert.True(Vector3.Distance(plate.Point, new Vector3((float)r.F("origin_X"), (float)r.F("origin_Y"), (float)r.F("origin_Z"))) < 0.001f, plate.Name);
        }

        foreach (var lb in new long[] { 0x5B4A, 0x5C45, 0x5B47 }) // the Quarters, the Abode, the Habitat
        {
            Assert.DoesNotContain(rows, r => (r.L("obj_Cell_Id") >> 16) == lb);
        }

        var nullify = Assert.Single(sql["spell"].View());
        Assert.Equal((285L, 1L, -1L, 0L), (nullify.L("max_Power"), nullify.L("align"), nullify.L("number"), nullify.L("dispel_School"))); // power 285 or less, positive, all of them, every school
        output.WriteLine(string.Join(", ", here.Select(r => string.Create(CultureInfo.InvariantCulture, $"0x{r.L("guid"):X8} 0x{r.L("obj_Cell_Id"):X8}"))));
    }

    [RequiresDatsFact]
    public void The_plate_is_a_small_sphere_the_profile_holds_and_its_dispel_takes_her_auras_and_leaves_the_bots_lines()
    {
        var plates = Holding.Plates!;
        var setup = PhysicsSetup.Load(fixture.Dats, 0x02000550)!;
        Assert.Empty(setup.CylSpheres);
        Assert.False(setup.HasPhysicsBsp);
        var sphere = Assert.Single(setup.Spheres);
        var player = PhysicsSetup.Load(fixture.Dats, 0x02000001)!;
        var lower = player.Spheres.MinBy(s => s.Center.Z);
        Assert.Equal(0.1f, sphere.Radius, 3);
        Assert.True(new Vector2(sphere.Center.X, sphere.Center.Y).Length() + sphere.Radius <= plates.ShapeMetres, "shape_m holds the sphere seen from above whatever the plate's heading");
        Assert.True(sphere.Center.Z + sphere.Radius - (lower.Center.Z - lower.Radius) <= plates.TopMetres, "top_m: her lower sphere over the plate's");
        Assert.Equal(PlateKeepOff.BodyRadiusMetres, lower.Radius, 3);
        Assert.Equal(PlateJumps.BodyHeightMetres, player.Height, 3);
        Assert.True(player.Spheres.Max(s => s.Center.Z + s.Radius) <= PlateJumps.BodyHeightMetres, "her height is over her upper sphere's top");
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        Assert.All(plates.Spells, s => Assert.Equal(spells[s.Id].Name, s.Name));
        var gift = spells[2166];
        Assert.Equal(("Tusker's Gift", 325u, 240.0), (gift.Name, gift.Power, gift.Duration));
        Assert.True(spells[2052].Power > 285, "the bot's Armor Self VII (Executor's Boon) is over the dispel's power: it stays");
        Assert.True(spells[2152].Power > 285, "the bot's Bludgeoning Protection VII stays");
        Assert.True(spells[1480].Power <= 285 && spells[1605].Power <= 285, "her own Hermetic Link VI and Defender VI auras are stripped");
    }

    [RequiresDatsFact]
    public void No_walked_step_of_the_Holdings_tour_comes_within_a_plates_wall()
    {
        var ground = Holding;
        var graph = Graph(buffed: true);
        var legs = new RunnerLegs(ground, new ProfileTour(ground), graph, new RecordingLogSink(LogSeverity.Info));
        Assert.Equal(0, graph.Patches.Count); // no costly box is seeded
        var stops = new List<uint> { ground.Arrival.Cell };
        stops.AddRange(ground.Tour);
        stops.Add(ground.Tour[0]);
        var at = new CellFrame(ground.Arrival.Cell, new AcFrame(ground.Arrival.Origin, Quaternion.Identity));
        var (moved, jumps) = (0, 0);
        for (var i = 1; i < stops.Count; i++)
        {
            var goal = legs.GoalOutOfAvoid(stops[i], at.Origin);
            Assert.True(goal is not null, $"no goal in 0x{stops[i]:X8}");
            moved += graph.RoomFrame(stops[i])!.Value.Origin == goal!.Value.Origin ? 0 : 1;
            Assert.Null(graph.Plates!.Holding(goal.Value.Origin));
            var route = graph.Plan(at.CellId, at.Origin, stops[i], goal.Value.Origin);
            Assert.True(route is not null, $"no plan from 0x{at.CellId:X8} to 0x{stops[i]:X8}");
            var points = new List<NavWaypoint> { new(at, Doorway: false) };
            points.AddRange(route!.Waypoints.Select(w => new NavWaypoint(w, Doorway: true)));
            foreach (var j in route.Jumps)
            {
                points[j.TakeOff + 1] = points[j.TakeOff + 1] with { Jump = j.Jump };
            }

            points.Add(new NavWaypoint(goal.Value, Doorway: false));
            Assert.True(graph.Plates.FirstMeeting(points, graph.LandblockId) is null, $"0x{at.CellId:X8} to 0x{stops[i]:X8} walks within a plate's wall");
            jumps += route.Jumps.Count(j => j.Jump.Crossing.Standing);
            at = goal.Value;
        }

        output.WriteLine($"{stops.Count - 1} legs, {moved} goals moved off a wall, {jumps} plate jumps in the round");
        Assert.True(moved >= 2, "the goals of 0x5B4802B2 and 0x5B4802D3, whose room frames stand inside a plate's wall, are moved");
        Assert.True(jumps >= 2, "the round crosses the row in 0x5B480373 twice, by a jump each way");
    }

    [RequiresDatsFact]
    public void The_walls_close_the_Holding_past_the_row_in_0x5B480373_and_the_standing_jumps_open_it()
    {
        var ground = Holding;
        var far = (uint)0x5B480372; // north of the row of four plates across 0x5B480373
        var buffed = Graph(buffed: true);
        var jumps = buffed.AllPlateJumps();
        output.WriteLine(string.Join(Environment.NewLine, jumps.OrderBy(j => j.Name, StringComparer.Ordinal).Select(j => string.Create(CultureInfo.InvariantCulture, $"{j.Name}: bar {j.Crossing.Bar:0.00}, need {j.NeedMetres:0.00} m"))));
        Assert.Equal(13, jumps.Count);
        Assert.All(jumps, j => Assert.True(j.Crossing.Standing && j.Crossing.PitCell == 0 && j.Crossing.Directions.Count == 1));
        Assert.All(jumps.Where(j => j.Crossing.Name.Contains("0x5B480373", StringComparison.Ordinal)), j => Assert.Equal(1f, j.Crossing.Bar)); // a 6 m room: a full bar
        Assert.All(jumps.Where(j => j.Crossing.Name.Contains("0x5B480351", StringComparison.Ordinal)), j => Assert.InRange(j.Crossing.Bar, 0.3f, 0.95f)); // a 3.7 m corridor: under its ceiling
        var through = buffed.Plan(ground.Arrival.Cell, ground.Arrival.Origin, far, buffed.RoomFrame(far)!.Value.Origin)!;
        Assert.Contains(through.Jumps, j => j.Jump.Crossing.Name.StartsWith("plates: the 4 plates in 0x5B480373", StringComparison.Ordinal));

        buffed.JumpReachMetres = null; // a body that cannot tell its flight makes no jump: the row is a wall
        Assert.Empty(buffed.AllPlateJumps());
        Assert.Null(buffed.Plan(ground.Arrival.Cell, ground.Arrival.Origin, far, buffed.RoomFrame(far)!.Value.Origin));

        var bare = Graph(buffed: false); // Run 23 and Jump 98: a flight of 5.88 m
        var bareJumps = bare.AllPlateJumps();
        output.WriteLine($"bare: {bareJumps.Count} jumps: {string.Join("; ", bareJumps.Select(j => j.Crossing.Name))}");
        Assert.DoesNotContain(bareJumps, j => j.Crossing.Name.Contains("0x5B480351", StringComparison.Ordinal)); // the six plates need more than her flight
        Assert.All(bareJumps, j => Assert.True(PlateJumps.FlightAt(JumpReach.FlightMetres(23, 98, 0f), PlateJumps.ApexMetres(JumpReach.FlightMetres(23, 98, 0f), 4f * (float)MovementSystem.GetRunRate(0f, 23, 1f)), j.Crossing.Bar) >= j.NeedMetres));
    }

    [RequiresDatsFact]
    public void The_Quarters_walk_reaches_every_slot_with_no_jump_and_no_plate()
    {
        var quarters = GroundCatalog.Get("tusker-quarters");
        var graph = GroundRooms.Load(fixture.Dats, quarters);
        Assert.Empty(quarters.Jumps);
        Assert.Empty(quarters.AvoidRegions);
        Assert.Null(quarters.Plates);
        Assert.Empty(graph.PlateList);
        var slots = quarters.Respawn.Slots.Select(s => s.Cell).Distinct().ToList();
        Assert.Equal(79, quarters.Respawn.Slots.Count);
        Assert.All(slots, c => Assert.True(quarters.IsWalkable(c), $"0x{c:X8} is walkable"));
        Assert.All(slots, c => Assert.True(graph.FindPath(quarters.Arrival.Cell, c) is not null, $"the walk joins the arrival and 0x{c:X8}"));
        Assert.All(slots.Where(c => !quarters.IsNoGo(c)), c => Assert.Contains(c, quarters.Tour));
    }
}

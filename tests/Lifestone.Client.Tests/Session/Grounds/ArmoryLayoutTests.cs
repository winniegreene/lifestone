using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Movement;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 14 armory (the owner, 2026-11-14: try the Tusker Armory as the climber's next ground): the Tusker Armory (landblock 0x5948) as facts of the place, checked as the Barracks' traps were (<see cref="BarracksLayoutTests"/>). Proved: the
/// layout has no pit, no jump and no unreached part (every storey cell and every one of the 67 respawn slots joins the arrival on foot, and the tour holds every slot's cell); its two "Magic trap" objects stand in the profile's two avoid
/// boxes with their reach to spare, and no leg of the tour as the walk plans it comes within that reach; on the pinned world SQL the landblock holds nothing else that acts on contact (no plate, no hotspot) and each trap is the Barracks'
/// unlinked Switch; and a body run (M6 4 R1) over the whole tour on the dats arrives on every leg with its track out of the traps' reach.
/// </summary>
public sealed class ArmoryLayoutTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint TrapWcid = 7561;

    private const double Fps = 60.0;

    /// <summary>The Run skill of the run-through tests (<c>IndoorRunThroughDatTests</c>).</summary>
    private const int RunSkill = 230;

    /// <summary>The two traps of 0x5948, landblock-local (landblock_instance guids 0x75948012 in 0x59480161 and 0x75948019 in 0x5948018C, wcid 7561; the world dump test below checks them).</summary>
    private static readonly Vector3[] Traps = [new(60f, -110f, -5.995f), new(80f, -110f, -5.995f)];

    /// <summary>The reach kept round a trap: its Setup 0x020000ED's radius (<see cref="BarracksLayoutTests"/> reads it from the dats) and her body's radius.</summary>
    private const float Reach = 0.104f + NavCreatures.PlayerRadiusMetres;

    private static HuntingGround Armory => GroundCatalog.Get("tusker-armory");

    private static bool Inside(GroundAvoidRegion r, Vector3 p, float margin) =>
        p.X - margin >= r.Min.X && p.X + margin <= r.Max.X && p.Y - margin >= r.Min.Y && p.Y + margin <= r.Max.Y && p.Z >= r.Min.Z && p.Z <= r.Max.Z;

    [Fact]
    public void The_Armorys_two_traps_are_two_avoid_boxes_with_their_reach_to_spare_and_the_exit_is_the_one_no_go_cell()
    {
        var ground = Armory;
        Assert.Equal(2, ground.AvoidRegions.Count);
        Assert.All(ground.AvoidRegions, r => Assert.Equal((ushort)0x5948, r.Landblock));
        Assert.All(Traps, p => Assert.Contains(ground.AvoidRegions, r => Inside(r, p, Reach)));
        Assert.All(ground.AvoidRegions, r => Assert.Contains(Traps, p => Inside(r, p, Reach))); // each box holds a trap: no box is a guess
        Assert.Empty(ground.Jumps);
        Assert.Null(ground.Plates);
        Assert.Equal([0x59480124u], ground.NoGo.Cells);
        Assert.Equal((22658u, 0x59480124u), (ground.Exit!.Wcid, ground.Exit.Cell));
        Assert.Contains("Switch", ground.Sources["no_go"], StringComparison.Ordinal);
        Assert.Contains("No pressure plate and no hotspot", ground.Sources["no_go"], StringComparison.Ordinal);
    }

    [RequiresPinnedWorldSqlFact]
    public void The_landblock_holds_the_tuskers_two_unlinked_switches_the_exit_and_the_generator_and_nothing_else()
    {
        using var zip = File.OpenRead(RequiresPinnedWorldSqlFactAttribute.Find()!);
        var sql = WorldSqlDump.ReadZip(zip, new Dictionary<string, Func<SqlRow, bool>>
        {
            ["landblock_instance"] = r => (r.L("obj_Cell_Id") >> 16) == 0x5948,
            ["landblock_instance_link"] = r => (r.L("parent_GUID") >> 12) == 0x75948 || (r.L("child_GUID") >> 12) == 0x75948,
            ["weenie"] = r => r.L("class_Id") == TrapWcid,
            ["weenie_properties_int"] = r => r.L("object_Id") == TrapWcid,
            ["weenie_properties_d_i_d"] = r => r.L("object_Id") == TrapWcid,
        });
        var rows = sql["landblock_instance"].View().ToList();
        var kinds = rows.GroupBy(r => (uint)r.L("weenie_Class_Id")).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new Dictionary<uint, int> { [11540] = 62, [22611] = 5, [TrapWcid] = 2, [22658] = 1, [7924] = 1 }, kinds); // no plate (22719), no hotspot: the traps are the only objects that act on her

        var traps = rows.Where(r => r.L("weenie_Class_Id") == TrapWcid).ToList();
        var points = traps.Select(r => new Vector3((float)r.F("origin_X"), (float)r.F("origin_Y"), (float)r.F("origin_Z"))).ToList();
        Assert.All(points, p => Assert.Contains(Armory.AvoidRegions, r => Inside(r, p, Reach)));
        Assert.All(points, p => Assert.Contains(Traps, q => Vector3.Distance(p, q) < 0.01f));
        Assert.Equal(new[] { (0x75948012L, 0x59480161L), (0x75948019L, 0x5948018CL) }, traps.Select(r => (r.L("guid"), r.L("obj_Cell_Id"))).Order());

        // The Barracks' weenie (BarracksLayoutTests): a Switch, not usable, which only a link or a Use activates; no link names either trap here, so on ACE @95ef426d it never fires. The generator's 67 links are its slots.
        Assert.Equal(26L, Assert.Single(sql["weenie"].View()).L("type"));
        var ints = sql["weenie_properties_int"].View().ToDictionary(r => r.L("type"), r => r.L("value"));
        Assert.Equal((1L, 0x1000L), (ints[16], ints[83]));
        var guids = traps.Select(r => r.L("guid")).ToHashSet();
        Assert.DoesNotContain(sql["landblock_instance_link"].View(), r => guids.Contains(r.L("parent_GUID")) || guids.Contains(r.L("child_GUID")));
        Assert.Equal(67, sql["landblock_instance_link"].View().Count(r => r.L("parent_GUID") == 0x75948026));
        Assert.Equal(1877L, sql["weenie_properties_d_i_d"].View().Single(r => r.L("type") == 28).L("value")); // Nullify All Magic Other: every line up to power 999, the bot's VII included
        output.WriteLine(string.Join(", ", points.Select(p => string.Create(CultureInfo.InvariantCulture, $"({p.X:0.#} {p.Y:0.#} {p.Z:0.#})"))));
    }

    [RequiresDatsFact]
    public void The_Armorys_walk_reaches_every_storey_cell_and_slot_with_no_jump()
    {
        var ground = Armory;
        var graph = GroundRooms.Load(fixture.Dats, ground);
        var storeyCells = ground.Storeys.SelectMany(s => s.Cells.Cells).Distinct().ToList();
        var unreached = storeyCells.Where(c => c != ground.Arrival.Cell && !ground.IsNoGo(c) && graph.FindPath(ground.Arrival.Cell, c) is null).ToList();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{storeyCells.Count} storey cells in {ground.Storeys.Count} storeys ({string.Join(", ", ground.Storeys.Select(s => s.Name + ": " + s.Cells.Count))}); {unreached.Count} unreached from the arrival"));
        Assert.Equal(290, storeyCells.Count);
        Assert.Equal(["z 12", "z 6", "z 0", "z -6", "z -12"], ground.Storeys.Select(s => s.Name));
        Assert.True(unreached.Count == 0, "unreached: " + string.Join(' ', unreached.Select(c => c.ToString("X8", CultureInfo.InvariantCulture))));
        Assert.Equal(67, ground.Respawn.Slots.Count);
        var slots = ground.Respawn.Slots.Select(s => s.Cell).Distinct().ToList();
        Assert.All(slots, c => Assert.True(ground.IsWalkable(c), $"0x{c:X8} is walkable"));
        Assert.All(slots, c => Assert.True(graph.FindPath(c, ground.Arrival.Cell) is not null, $"the walk joins 0x{c:X8} and the arrival both ways"));
        Assert.Equal(slots.Order(), ground.Tour.Order());
    }

    [RequiresDatsFact]
    public void No_leg_of_the_Armorys_tour_comes_within_a_traps_reach()
    {
        var ground = Armory;
        var graph = GroundRooms.Load(fixture.Dats, ground);
        var legs = new RunnerLegs(ground, new ProfileTour(ground), graph, new RecordingLogSink(LogSeverity.Info)); // seeds the two boxes
        Assert.Equal(2, graph.Patches.Count);
        var stops = new List<uint> { ground.Arrival.Cell };
        stops.AddRange(ground.Tour);
        stops.Add(ground.Tour[0]);
        var at = new CellFrame(ground.Arrival.Cell, new AcFrame(ground.Arrival.Origin, Quaternion.Identity));
        var near = new List<string>();
        var moved = 0;
        for (var i = 1; i < stops.Count; i++)
        {
            var goal = legs.GoalOutOfAvoid(stops[i], at.Origin);
            Assert.True(goal is not null, $"no goal in 0x{stops[i]:X8}");
            moved += graph.RoomFrame(stops[i])!.Value.Origin == goal!.Value.Origin ? 0 : 1;
            var route = graph.Plan(at.CellId, at.Origin, stops[i], goal.Value.Origin);
            Assert.True(route is not null, $"no plan from 0x{at.CellId:X8} to 0x{stops[i]:X8}");
            var points = new List<Vector3> { at.Origin };
            points.AddRange(route!.Waypoints.Select(w => w.Origin));
            for (var s = 1; s < points.Count; s++)
            {
                foreach (var trap in Traps.Where(t => MathF.Abs(t.Z - points[s].Z) < 3f && Distance(t, points[s - 1], points[s]) < Reach))
                {
                    near.Add(string.Create(CultureInfo.InvariantCulture, $"0x{at.CellId:X8} to 0x{stops[i]:X8} passes {Distance(trap, points[s - 1], points[s]):0.##} m from the trap at ({trap.X:0.#} {trap.Y:0.#})"));
                }
            }

            at = goal.Value;
        }

        output.WriteLine($"{stops.Count - 1} legs, {moved} goals moved out of a box; {near.Count} within a trap's reach");
        Assert.True(near.Count == 0, string.Join("; ", near));
        Assert.Equal(0, moved); // unlike the Barracks' 0x5A4801C2 and 0x5A4801E2, the floor centres of the traps' cells 0x59480161 and 0x5948018C stand off the boxes: no goal is moved, and the plans go round the boxes
    }

    /// <summary>
    /// The body run (M6 4 R1), the Abode's and the Holding's proof: a body (an <see cref="OfflineWalker"/> with the landblock's whole collision, Run 230) runs the tour's legs as the fight's walk plans them, from the arrival through every
    /// tour cell and back to the first, each goal off the avoid boxes (<see cref="RunnerLegs.GoalOutOfAvoid"/>), over the room graph with the two boxes seeded, every segment confirmed by the physics before it is run. Every leg arrives in its
    /// cell with no stuck rung and no jump, and the body's own track (each frame) never comes within a trap's reach.
    /// </summary>
    [RequiresDatsFact]
    public void A_body_runs_every_leg_of_the_tour_and_its_track_keeps_out_of_the_traps_reach()
    {
        var ground = Armory;
        var dats = fixture.Dats;
        var coord = new LandblockCoord(0x59, 0x48);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, new PhysicsGeometryLibrary(dats)).Build(coord));
        var graph = GroundRooms.GraphOf(LandblockCellSet.Load(new CellStructureLibrary(dats), coord), ground);
        var legs = new RunnerLegs(ground, new ProfileTour(ground), graph, NullLogSink.Instance);
        var h = new ActionHarness(offline: true);
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, null, "armory-tour");
        var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(RunSkill), sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(ground.Arrival.Frame, sliding: true));
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var source = new RoomPathSource(graph);
        var stops = ground.Tour.Append(ground.Tour[0]).ToList();
        var now = 0.0;
        var (metres, closest, failures) = (0f, float.MaxValue, new List<string>());
        foreach (var cell in stops)
        {
            var goal = legs.GoalOutOfAvoid(cell, walker.Position.Origin)!.Value;
            using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, new NavRunnerOptions { Name = "armory-" + (cell & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture) });
            runner.Start(NavGoal.ToPoint(new NavPoint(cell & 0xFFFF0000u, goal.Origin), cell), now);
            var end = now + 120;
            while (!runner.IsDone && now < end)
            {
                now += 1.0 / Fps;
                walker.Controller.Tick(now);
                runner.Step(now);
                h.Tick(1.0 / Fps);
                var p = walker.Position.Origin;
                foreach (var trap in Traps.Where(t => MathF.Abs(t.Z - p.Z) < 3f))
                {
                    closest = MathF.Min(closest, Vector2.Distance(new Vector2(trap.X, trap.Y), new Vector2(p.X, p.Y)));
                }
            }

            metres += runner.RouteMetres;
            var line = string.Create(CultureInfo.InvariantCulture, $"0x{cell & 0xFFFF:X4} {runner.State} {runner.RouteMetres:0.0} m, body in 0x{walker.Position.CellId:X8} {runner.VerdictFields()}");
            if (runner.State != NavRunState.Arrived || runner.Rungs != 0 || runner.JumpsTaken != 0 || walker.Position.CellId != cell)
            {
                failures.Add(line + (runner.FailureReason is { } why ? " -- " + why : string.Empty));
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"tusker-armory: {stops.Count} legs, {metres:0} m in {now:0} s of the body's clock, {failures.Count} failed; the track's nearest pass to a trap {closest:0.00} m (reach {Reach:0.###})"));
        failures.ForEach(output.WriteLine);
        Assert.Empty(failures);
        Assert.True(closest >= Reach, string.Create(CultureInfo.InvariantCulture, $"the body came {closest:0.00} m from a trap"));
    }

    private static float Distance(Vector3 p, Vector3 a, Vector3 b)
    {
        var (p2, a2, b2) = (new Vector2(p.X, p.Y), new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
        var d = b2 - a2;
        var t = d.LengthSquared() < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(p2 - a2, d) / d.LengthSquared(), 0f, 1f);
        return Vector2.Distance(p2, a2 + (d * t));
    }
}

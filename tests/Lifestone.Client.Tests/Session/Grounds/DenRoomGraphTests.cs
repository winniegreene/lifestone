using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// Every dungeon profile's room graph on the dats (M5 item 2 g2, D39, D46), as per-ground checks over <see cref="GroundCatalog.All"/>: the graph of a ground is its walkable cells at its doorway
/// lean (<see cref="RoomGraph"/>); every target slot's cell is reached from the arrival without entering a no-go cell, every no-go cell is refused by every plan into it, and the storeys are
/// joined (every walkable cell is reached from the arrival); the profile's storeys, generators, slots, targets, exits and arrival equal the draft's (<see cref="GroundDraft"/> over the dats and the
/// pinned world SQL), no walkable cell is cut off, avoided creatures stand in no-go cells and the tour covers the hunted slots. Then the den's own checks: its literals (the draft's counts, the wing, the
/// 13 cut-off cells, the first-draft tour, the two Surface Portals) and the ramp walks (<c>m5-den-storeys</c>): an <see cref="OfflineWalker"/> built as GroundWalk.Create builds it walks the storeys' ramps down
/// and back up. Skipped without the dats.
/// </summary>
public sealed class DenRoomGraphTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const string DenKey = "black-spawn-den-aluvian";

    /// <summary>The keys of every dungeon profile (D46: a new dungeon's JSON file joins every per-ground theory here with no edit).</summary>
    public static TheoryData<string> Dungeons() => new(GroundCatalog.All.Where(g => g.Kind == GroundKind.Dungeon).Select(g => g.Key));

    /// <summary>A ground's landblock cells and its room graph (its walkable cells at its lean).</summary>
    private (LandblockCellSet All, RoomGraph Graph) GraphOf(HuntingGround ground)
    {
        // TODO(ground-multi-landblock): a dungeon of several landblocks needs a graph per landblock and the crossings between them; no profile has one yet.
        var landblock = Assert.Single(ground.Landblocks);
        var all = LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), new LandblockCoord((byte)(landblock >> 8), (byte)(landblock & 0xFF)));
        return (all, new RoomGraph(LandblockCellSet.FromCells(all.Structures, all.Coord, all.Cells.Where(c => ground.IsWalkable(c.Id))), ground.Walk.DoorwayLean));
    }

    /// <summary>The slots a ground hunts: a target's weenie standing in a walkable cell (a target's slot in a no-go cell is left standing: the no-go set wins).</summary>
    private static RespawnSlot[] Hunted(HuntingGround ground) => ground.Respawn.Slots.Where(s => ground.RoleOf(s.Wcid) == GroundRole.Target && !ground.IsNoGo(s.Cell)).ToArray();

    [RequiresDatsTheory]
    [MemberData(nameof(Dungeons))]
    public void Every_target_slot_s_cell_is_reached_from_the_arrival_without_entering_a_no_go_cell(string key)
    {
        var ground = GroundCatalog.Get(key);
        var (_, graph) = GraphOf(ground);
        var hunted = Hunted(ground);
        Assert.NotEmpty(hunted);
        foreach (var cell in hunted.Select(s => s.Cell).Distinct())
        {
            Assert.True(ground.IsWalkable(cell), $"{key}: the target slot's cell 0x{cell:X8} is in no storey");
            var path = graph.FindPath(ground.Arrival.Cell, cell);
            Assert.True(path is not null, $"{key}: no walk joins the arrival 0x{ground.Arrival.Cell:X8} and the target slot's cell 0x{cell:X8}");
            Assert.All(path, c => Assert.False(ground.IsNoGo(c), $"{key}: the walk to 0x{cell:X8} enters the no-go cell 0x{c:X8}"));
            Assert.True(
                graph.PlanScripted("slot", ground.Arrival.Frame, cell, ground.Walk.StopWithinMetres, ground.Walk.StopDwellSeconds) is not null,
                $"{key}: the room graph plans no leg from the arrival to 0x{cell:X8}");
        }

        output.WriteLine($"{key}: {hunted.Length} target slots in {hunted.Select(s => s.Cell).Distinct().Count()} cells, every one reached from the arrival");
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Dungeons))]
    public void Every_no_go_cell_is_refused_by_every_plan_into_it(string key)
    {
        var ground = GroundCatalog.Get(key);
        var (all, graph) = GraphOf(ground);
        var starts = Hunted(ground).Select(s => s.Cell).Prepend(ground.Arrival.Cell).Concat(ground.Tour).Distinct().ToArray();
        var refused = 0;
        foreach (var cell in ground.NoGo.Cells.Where(c => all.GetCell(c) is not null))
        {
            Assert.Null(graph.Cells.GetCell(cell));
            Assert.Null(graph.PlanScripted("no-go", ground.Arrival.Frame, cell, ground.Walk.StopWithinMetres, ground.Walk.StopDwellSeconds));
            foreach (var from in starts)
            {
                Assert.True(graph.FindPath(from, cell) is null, $"{key}: a walk from 0x{from:X8} enters the no-go cell 0x{cell:X8}");
            }

            refused++;
        }

        Assert.True(refused > 0, $"{key} has no no-go cell of its landblock (a dungeon has at least its exits)");
        output.WriteLine($"{key}: {refused} no-go cells refused from {starts.Length} starts");
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Dungeons))]
    public void The_storeys_are_joined_every_walkable_cell_reached_from_the_arrival(string key)
    {
        var ground = GroundCatalog.Get(key);
        var (_, graph) = GraphOf(ground);
        Assert.True(ground.IsWalkable(ground.Arrival.Cell), $"{key}: the arrival is not walkable");
        var unreached = ground.Walkable.Cells.Where(c => c != ground.Arrival.Cell && graph.FindPath(ground.Arrival.Cell, c) is null).ToArray();
        Assert.True(unreached.Length == 0, $"{key}: walkable cells the arrival does not reach (cut off: GroundDraft.CutOff lists them for the no-go set): {string.Join(", ", unreached.Select(c => $"0x{c:X8}"))}");
        foreach (var storey in ground.Storeys)
        {
            var walkable = storey.Cells.Cells.Count(ground.IsWalkable);
            Assert.True(walkable > 0, $"{key}: storey {storey.Name} has no walkable cell");
            output.WriteLine($"{key}: storey {storey.Name}: {walkable} walkable of {storey.Cells.Count}, every one reached from the arrival");
        }
    }

    /// <summary>
    /// Per ground (D46): the profile's storeys are the dats' draft at its lean, band for band (a profile adds no walkable cell and drops none: the no-go set is the only cut); the no-go set leaves no walkable
    /// cell cut off from the arrival; every avoided creature's slot stands in a no-go cell (the loop never walks to one); and the tour covers every hunted slot's cell. The first draft over the hunted cells is
    /// printed for the person who sets a tour (a profile's tour is hand-set from it; the tour's own logic is item 2 g6's).
    /// </summary>
    [RequiresDatsTheory]
    [MemberData(nameof(Dungeons))]
    public void A_dungeon_s_storeys_are_its_draft_s_the_no_go_set_cuts_nothing_off_and_the_tour_covers_the_hunted_slots(string key)
    {
        var ground = GroundCatalog.Get(key);
        var (all, _) = GraphOf(ground);
        var draft = GroundDraft.Cells(all, ground.Arrival.Cell, ground.Walk.DoorwayLean);
        output.WriteLine(GroundDraft.Summary(draft, new DraftWorld([], [], [], [], null, [])));

        // The storeys are the draft's, band for band.
        Assert.Equal(draft.Storeys.Select(s => (s.Name, s.ZMin, s.ZMax)), ground.Storeys.Select(s => (s.Name, s.ZMin, s.ZMax)));
        Assert.Equal(draft.Storeys.Select(s => s.Cells.Cells), ground.Storeys.Select(s => s.Cells.Cells));

        // Nothing the no-go set cuts off is left walkable (GroundDraft.CutOff names the cells for the no-go set).
        var cutOff = GroundDraft.CutOff(all, ground.Arrival.Cell, ground.Walk.DoorwayLean, draft.Walkable, ground.IsNoGo);
        Assert.True(cutOff.Count == 0, $"{key}: the no-go set cuts off walkable cells, which are no-go too: {string.Join(", ", cutOff.Select(c => $"\"0x{c:X8}\""))}");

        // The loop never walks to an avoided creature: every slot of one stands in a no-go cell.
        var avoided = ground.Respawn.Slots.Where(s => ground.RoleOf(s.Wcid) == GroundRole.Avoid && !ground.IsNoGo(s.Cell)).ToArray();
        Assert.True(avoided.Length == 0, $"{key}: avoided creatures stand in walkable cells: {string.Join(", ", avoided.Select(s => $"wcid {s.Wcid} in 0x{s.Cell:X8}"))}");

        // The tour covers every hunted slot's cell.
        var hunted = Hunted(ground).Select(s => s.Cell).Distinct().ToArray();
        var firstDraft = GroundDraft.FirstTour(all, ground.Storeys, ground.Arrival.Cell, hunted);
        output.WriteLine($"{key}: the first-draft tour over {hunted.Length} hunted cells: {string.Join(", ", firstDraft.Select(c => $"\"0x{c:X8}\""))}");
        var missing = hunted.Where(c => !ground.Tour.Contains(c)).ToArray();
        Assert.True(missing.Length == 0, $"{key}: the tour misses hunted slots' cells: {string.Join(", ", missing.Select(c => $"0x{c:X8}"))}");
    }

    [RequiresDatsFact]
    public void The_den_s_draft_equals_the_profile_s_derived_sections()
    {
        var den = GroundCatalog.Get(DenKey);
        var (all, _) = GraphOf(den);
        var draft = GroundDraft.Cells(all, den.Arrival.Cell, den.Walk.DoorwayLean);

        // The den's literals: the draft's counts, the wing and the 13 cells D39's set cuts off.
        Assert.Equal((653, 567, 35, 51), (draft.Total, draft.Walkable.Count, draft.Floorless.Count, draft.Pockets.Count));

        // D39's wing is the cells at or under z -23 with y over -125 (the dats): every one is no-go.
        var wing = all.Cells.Where(c => c.Frame.Origin.Z <= -23f && c.Frame.Origin.Y > -125f).Select(c => c.Id).ToArray();
        Assert.Equal(52, wing.Length);
        Assert.All(wing, c => Assert.True(den.IsNoGo(c), $"the wing's 0x{c:X8} is no-go"));
        Assert.Equal(
            new uint[] { 0x01050139, 0x0105013A, 0x0105013B, 0x0105013C, 0x0105013D }, // the wing's floorless ramp cells: reached (572), not walkable
            wing.Where(c => draft.Floorless.Contains(c)).Order());

        // Nothing the no-go set cuts off is left walkable (the 13 cut-off cells are in the profile's no-go set).
        Assert.Empty(GroundDraft.CutOff(all, den.Arrival.Cell, den.Walk.DoorwayLean, draft.Walkable, den.IsNoGo));
        Assert.Equal(13, GroundDraft.CutOff(all, den.Arrival.Cell, den.Walk.DoorwayLean, draft.Walkable, c => den.IsNoGo(c) && !IsCutOffLiteral(c)).Count);

        // The tour is the first draft over the hunted slots' cells.
        Assert.Equal(GroundDraft.FirstTour(all, den.Storeys, den.Arrival.Cell, Hunted(den).Select(s => s.Cell)), den.Tour);
    }

    private static bool IsCutOffLiteral(uint cell) => (cell & 0xFFFF) is 0x01DC or 0x0240 or (>= 0x026A and <= 0x026C) or (>= 0x0273 and <= 0x0278) or 0x027B or 0x02D4;

    /// <summary>
    /// Per ground (D46): the pinned world SQL's draft of the ground's landblock (parsed read-only) equals the profile's generators, slots, creatures (the targets list, name and level), exit cells (all no-go)
    /// and arrival (origin and heading). The den's two Surface Portals' cells are its literal.
    /// </summary>
    [RequiresWorldSqlDumpTheory]
    [MemberData(nameof(Dungeons))]
    public void A_dungeon_s_draft_from_the_pinned_world_sql_equals_the_profile_s_generators_slots_targets_exits_and_arrival(string key)
    {
        var ground = GroundCatalog.Get(key);
        var world = PinnedWorldSql.Draft(Assert.Single(ground.Landblocks), ground.Arrival.Cell); // shared with GroundDraftTownTests: two streaming passes over the dump a run
        output.WriteLine(string.Join("\n", world.Notes));
        Assert.Equal(ground.Respawn.Generators, world.Generators.Select(g => new GeneratorModel(g.Guid, g.Cell, g.DelaySeconds!.Value, g.TickSeconds!.Value)));
        Assert.Equal(ground.Respawn.Slots, world.Slots.Select(s => new RespawnSlot(s.Generator, s.Cell, s.Wcid)));
        Assert.Equal(ground.Targets.Select(t => (t.Wcid, t.Name, (int?)t.Level)).Order(), world.Creatures.Select(c => (c.Wcid, c.Name, c.Level)).Order());
        Assert.All(world.ExitCells, c => Assert.True(ground.IsNoGo(c), $"{key}: the exit 0x{c:X8} is no-go"));
        if (key == DenKey)
        {
            Assert.Equal([0x01050279u, 0x01050341u], world.ExitCells);
        }

        Assert.NotNull(world.Arrival);
        Assert.Equal(ground.Arrival.Origin, world.Arrival.Origin);
        Assert.Equal((ground.Arrival.HeadingW, ground.Arrival.HeadingZ), (world.Arrival.HeadingW, world.Arrival.HeadingZ));
        output.WriteLine($"{key}: {world.Generators.Count} generator(s), {world.Slots.Count} slots, {world.Creatures.Count} creature weenies, {world.ExitCells.Count} exit cell(s), the arrival 0x{ground.Arrival.Cell:X8}");
    }

    /// <summary>
    /// <c>m5-den-storeys</c>: the walker (GroundWalk.Create's: the landblock's whole collision, a run skill of 230) walks the den's graph from the arrival through the brief's rungs down to the
    /// south z -24 area and back up to the arrival, each leg ending in its end cell at the stop's z (it descends and climbs the 31-degree ramps). The rungs 0x026C, 0x0240 and 0x01DC are no-go
    /// under D39 (the second exit's corridor and its ramp to the wing, reached only through the wing or the exit 0x0279): the graph refuses them, and the walk goes on from 0x0262 to 0x01D0
    /// down the other ramps. About 130 s of walking on the session clock, a fifth of a second to run: it is in the default run.
    /// </summary>
    [RequiresDatsFact]
    public void The_walker_descends_and_climbs_the_den_s_ramps_over_the_whole_den_s_graph()
    {
        var den = GroundCatalog.Get(DenKey);
        var (_, graph) = GraphOf(den);
        var dats = fixture.Dats;
        var geometry = new PhysicsGeometryLibrary(dats);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, geometry).Build(new LandblockCoord(1, 5)));
        var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230), geometry);
        world.UseTime(0.0);
        Assert.True(walker.Place(den.Arrival.Frame), "placed at the arrival");

        uint[] rungs = [0x02DA, 0x0262, 0x026C, 0x0240, 0x01DC, 0x01D0, 0x0118, 0x0375];
        var now = 0.0;
        var refused = new List<uint>();
        foreach (var low in rungs)
        {
            var cell = 0x01050000u | low;
            var from = walker.Position;
            var route = graph.PlanScripted($"ramp-{low:X4}", in from, cell, den.Walk.StopWithinMetres, den.Walk.StopDwellSeconds);
            if (den.IsNoGo(cell))
            {
                Assert.Null(route);
                refused.Add(low);
                output.WriteLine($"rung 0x{low:X4}: no-go, refused by the graph");
                continue;
            }

            Assert.True(route is not null, $"rung 0x{low:X4}: the graph plans no leg from 0x{from.CellId:X8}");
            var driver = new RouteDriver(walker.Controller, route);
            var start = now;
            var status = RouteStatus.Running;
            var visited = new List<uint> { walker.Position.CellId };
            while (status == RouteStatus.Running && now - start < 240)
            {
                now += 1.0 / 60.0;
                walker.Controller.Tick(now);
                status = driver.Tick(now);
                if (walker.Position.CellId != visited[^1])
                {
                    visited.Add(walker.Position.CellId);
                }
            }

            var at = walker.Position;
            var stop = graph.RoomFrame(cell)!.Value;
            output.WriteLine($"rung 0x{low:X4}: {status} in {now - start:0.0} s at 0x{at.CellId:X8} {at.Frame.Origin} (stop z {stop.Frame.Origin.Z:0.00}); cells {string.Join(" ", visited.Select(c => (c & 0xFFFF).ToString("X4")))} {driver.FailureReason}");
            Assert.True(status == RouteStatus.Done, $"rung 0x{low:X4}: the leg from 0x{from.CellId:X8} ended {status} at 0x{at.CellId:X8} {at.Frame.Origin}: {driver.FailureReason}");
            Assert.Equal(cell, at.CellId);
            Assert.InRange(at.Frame.Origin.Z, stop.Frame.Origin.Z - 0.75f, stop.Frame.Origin.Z + 0.75f);
            Assert.All(visited, c => Assert.False(den.IsNoGo(c), $"rung 0x{low:X4}: the walker entered the no-go cell 0x{c:X8}"));
        }

        Assert.Equal([0x026Cu, 0x0240u, 0x01DCu], refused);
    }
}

/// <summary>A theory that needs the dats and the pinned world SQL dump under server/runtime/downloads of the main checkout (a read-only parse, about a second a case): skipped, with the reason, otherwise.</summary>
internal sealed class RequiresWorldSqlDumpTheoryAttribute : TheoryAttribute
{
    public RequiresWorldSqlDumpTheoryAttribute()
    {
        if (!DatSet.Exists())
        {
            Skip = "needs the retail dats";
        }
        else if (PinnedWorldSql.Zip() is null)
        {
            Skip = "needs the pinned world SQL dump under server/runtime/downloads of the main checkout";
        }
    }
}

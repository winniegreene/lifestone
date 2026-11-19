using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// The island's tusker dungeons walked on the dats from their arrivals (M6 4 W4: <c>m6-cave-doorway-0113-0114</c>, <c>m6-burrow-walk</c>; Exhaustive tier, skipped
/// without the dats). Tusker Cave's rooms join through a ramp that rises under a hole in the floor above (0x5B460113 to 0x5B460114, a level portal) and Tusker
/// Burrow's through corridors whose cells part along slanted planes over a level floor (0x5748020E to 0x5748020F, normal Z 0.857): no doorway by the fit test, so
/// the room graph stopped at 4 of 404 and 4 of 278 cells. <see cref="CellRoutePlanner.FloorCrossingOf"/> walks a portal the two cells' floors meet on. Per dungeon:
/// the cells with a floor the room graph reaches from the arrival at the default lean (and the count by the fit test alone, the rule before W4), every floor
/// crossing the walk uses confirmed by the physics (<see cref="RouteConfirmer"/>: its approach to its push), and one body run (an <see cref="OfflineWalker"/>
/// under a <see cref="NavRunner"/> over a <see cref="RoomPathSource"/>, confirmed before it walks) from the arrival to a far target slot's room.
/// </summary>
/// <remarks>
/// The arrivals are the entry portals' Destination (the pinned world SQL v0.9.296, weenie_properties_position position_Type 2 of 22663 Burrow, 22665 Cave,
/// 22688 Lodge, 22654 Abode, 22692 Quarters, 22680 Holding, 22694 Retreat); the far slots are generator slots (landblock_instance rows with is_Link_Child of the landblock),
/// the slot whose planned walk from the arrival is the longest.
/// </remarks>
[Trait("Tier", "Exhaustive")]
public sealed class IslandDungeonWalkDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The Run skill of the run-through tests (<c>IndoorRunThroughDatTests</c>).</summary>
    private const int RunSkill = 230;

    private const double Fps = 60.0;

    /// <summary>
    /// A dungeon of the island: its landblock, the arrival (cell and point), the far slot's cell, and the cells with a floor of the landblock, those the room graph
    /// reaches from the arrival at the default lean, and those the fit test alone reached (the rule before W4).
    /// </summary>
    public sealed record Island(string Name, ushort Landblock, uint Arrival, Vector3 Origin, uint FarSlot, int Floored, int Reached, int ReachedByFit);

    /// <summary>
    /// The counts are the dats' (measured 2026-11-03). The Abode and the Holding share one layout: of its 560 cells with a floor the walk reaches 166, the upper level's
    /// east half at z 0 (the fit test alone walked on across the pit, 305). The rest lie past the pit 0x2A9 (x 125 to 135, y -65 to -55, no floor polygon, open from z 6
    /// down to the bridge at -12 through 0x235 and 0x1B0): the upper level's west half (139 cells at z 0, beyond 0x2A0) and the lower level (230 cells at -12, one walk
    /// since M6 4 W6b: <see cref="The_Abode_s_lower_level_is_one_walk_over_its_bridge_and_its_pit_crossroads_is_refused"/>). A walking body cannot enter the pit
    /// (<see cref="A_body_walking_into_the_Abode_s_pit_stops_at_its_brink"/>): only a jump crosses it, which no walk does. These graphs have no jump (the room graph's walk alone); the profiles'
    /// jump edge over the pit to the west half is <c>PitJumpDatTests</c>' (M6 14 pits), and the lower level stays one way down (<c>TODO(m6-abode-lower-level)</c>).
    /// </summary>
    private static readonly Dictionary<string, Island> Islands = new()
    {
        ["burrow"] = new("Tusker Burrow", 0x5748, 0x57480210, new(148.378f, -2.8159f, 0.005f), 0x5748010D, 196, 196, 4),
        ["cave"] = new("Tusker Cave", 0x5B46, 0x5B460100, new(70.0456f, -94.1599f, -41.995f), 0x5B460270, 278, 278, 3),
        ["lodge"] = new("Tusker Lodge", 0x5749, 0x57490102, new(50f, -150f, 0.005f), 0x57490286, 386, 386, 3),
        ["abode"] = new("Tusker Abode", 0x5C45, 0x5C450384, new(325.98f, -99.5934f, 0.005f), 0x5C4502B9, 560, 166, 305),
        ["quarters"] = new("Tusker Quarters", 0x5B4A, 0x5B4A0210, new(148.551f, -1.84039f, 0.005f), 0x5B4A010E, 196, 196, 4),
        ["holding"] = new("The Holding", 0x5B48, 0x5B480384, new(325.98f, -99.5934f, 0.005f), 0x5B4802B9, 560, 166, 305),
        ["armory"] = new("Tusker Armory", 0x5948, 0x594801AB, new(40f, -170f, 0.005f), 0x59480128, 290, 290, 290), // M6 14 armory: the far slot a tusk carrier's beside the exit, five storeys down
        // M6 retreat: 22694's Destination; the far slot is the Male Tusker in 0x01E9 (212 m by the room graph's room middles, the south-east corner of z 12). Its 11 other floored cells are pockets no walk reaches at any lean
        // (the z 6 ring round the shaft over 0x0126 and three z 0 ledges over the z -6 rooms: tusker-retreat.json sources.walk).
        ["retreat"] = new("Tusker Retreat", 0x5C49, 0x5C490138, new(89.8429f, -116.371f, 0.005f), 0x5C4901E9, 180, 169, 169),
    };

    /// <summary>The other rooms whose floor crossings are confirmed too: the Black Spawn Den's plazas (leaning doorways) and the Training Academy's hive (from its deepest room).</summary>
    private static readonly Dictionary<string, (ushort Landblock, uint Start)> Others = new()
    {
        ["den"] = (0x0105, 0x01050383),
        ["academy-hive"] = (0x8602, 0x86020100),
    };

    public static TheoryData<string> Keys() => new(Islands.Keys);

    public static TheoryData<string> CrossingKeys() => new(Islands.Keys.Concat(Others.Keys));

    /// <summary>M6 7 U: every ground of the catalog whose exit is a portal in a cell of its own landblock, less the two whose exit stands on the lower level past the Abode's pit (<c>TODO(m6-abode-lower-level)</c>).</summary>
    public static TheoryData<string> WalkedExits() => new(GroundCatalog.All.Where(g => g.Exit is { Kind: GroundExitKind.Portal } && g.InLandblocks(g.Exit.Cell) && g.Key is not ("tusker-abode" or "tusker-holding")).Select(g => g.Key));

    private LandblockCellSet CellsOf(ushort landblock) =>
        LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), new LandblockCoord((byte)(landblock >> 8), (byte)(landblock & 0xFF)));

    private LandblockCellSet CellsOf(Island island) => CellsOf(island.Landblock);

    /// <summary>The cells reached from <paramref name="from"/> over the portals <paramref name="passable"/> accepts (breadth first).</summary>
    private static HashSet<uint> Reach(LandblockCellSet cells, uint from, Func<EnvCellData, CellPortalData, bool> passable)
    {
        var seen = new HashSet<uint> { from };
        var queue = new Queue<uint>([from]);
        while (queue.TryDequeue(out var id))
        {
            var cell = cells.GetCell(id)!;
            foreach (var portal in cell.Portals)
            {
                if (!portal.LeadsOutside && cells.GetCell(portal.OtherCellId) is not null && !seen.Contains(portal.OtherCellId) && passable(cell, portal))
                {
                    seen.Add(portal.OtherCellId);
                    queue.Enqueue(portal.OtherCellId);
                }
            }
        }

        return seen;
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Keys))]
    public void The_room_graph_reaches_the_dungeon_s_floored_cells_from_its_arrival_at_the_default_lean(string key)
    {
        var island = Islands[key];
        var cells = CellsOf(island);
        var graph = new RoomGraph(cells);
        var floored = cells.Cells.Where(c => cells.GetStructure(c).Floors.Count > 0).Select(c => c.Id).ToHashSet();
        var reached = Reach(cells, island.Arrival, graph.IsPassable);
        var atDenLean = Reach(cells, island.Arrival, new RoomGraph(cells, 0.3f).IsPassable).Count(floored.Contains);
        var byFit = Reach(cells, island.Arrival, graph.Planner.Fits).Count(floored.Contains);
        output.WriteLine($"{island.Name}: {reached.Count(floored.Contains)} of {floored.Count} cells with a floor reached from 0x{island.Arrival:X8} at lean 0 ({atDenLean} at lean 0.3), {byFit} by the fit test alone; {cells.Count} cells");
        Assert.Equal(island.Floored, floored.Count);
        Assert.Equal(island.Reached, reached.Count(floored.Contains));
        Assert.Equal(island.Reached, atDenLean);
        Assert.Equal(island.ReachedByFit, byFit);
        Assert.Contains(island.FarSlot, reached);
    }

    [RequiresDatsTheory]
    [MemberData(nameof(CrossingKeys))]
    public void Every_floor_crossing_the_walk_uses_is_confirmed_by_the_physics(string key)
    {
        var (landblock, start) = Islands.TryGetValue(key, out var island) ? (island.Landblock, island.Arrival) : Others[key];
        var cells = CellsOf(landblock);
        var graph = new RoomGraph(cells);
        var (world, walker) = BodyIn(landblock, new CellFrame(start, graph.RoomFrame(start)!.Value.Frame));
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var (confirmed, failures) = (0, new List<string>());
        foreach (var id in Reach(cells, start, graph.IsPassable).Order())
        {
            var cell = cells.GetCell(id)!;
            foreach (var portal in cell.Portals)
            {
                if (!graph.IsPassable(cell, portal) || graph.Planner.IsDoorway(cell, portal) || graph.Planner.FloorCrossingOf(cell, portal) is not { } crossing)
                {
                    continue;
                }

                var (approach, push) = graph.Planner.DoorwayPair(cell, portal);
                var before = approach ?? new CellFrame(cell.Id, new AcFrame(crossing.From, Quaternion.Identity));
                var status = confirmer.ConfirmAll([new NavWaypoint(before, Doorway: true), new NavWaypoint(push, Doorway: true)]);
                if (status == RouteConfirmStatus.Confirmed)
                {
                    confirmed++;
                }
                else
                {
                    failures.Add(string.Create(CultureInfo.InvariantCulture, $"0x{cell.Id:X8} -> 0x{portal.OtherCellId:X8} ({before.Origin.X:0.00}, {before.Origin.Y:0.00}, {before.Origin.Z:0.00}) to ({push.Origin.X:0.00}, {push.Origin.Y:0.00}, {push.Origin.Z:0.00}): {confirmer.Failure}"));
                }
            }
        }

        output.WriteLine($"{key}: {confirmed} floor crossings confirmed, {failures.Count} refused");
        failures.ForEach(output.WriteLine);
        Assert.Empty(failures);
        Assert.True(confirmed > 0 || key is "abode" or "holding" or "armory" or "retreat", key + ": no floor crossing on the walk"); // the Abode's layout joins by upright doorways only, and so does the Armory's (M6 14 armory: its five storeys by ramps of upright doorways) and the Retreat's (M6 retreat: 0 crossings, 0 refused)
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Keys))]
    public void A_body_runs_from_the_arrival_to_a_far_target_slot_s_room(string key)
    {
        var island = Islands[key];
        var graph = new RoomGraph(CellsOf(island));
        var (world, walker) = BodyIn(island.Landblock, new CellFrame(island.Arrival, new AcFrame(island.Origin, Quaternion.Identity)));
        var h = new ActionHarness(offline: true);
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, null, "island-walk");
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var room = graph.RoomFrame(island.FarSlot)!.Value;
        var goal = NavGoal.ToPoint(new NavPoint((uint)island.Landblock << 16, room.Origin), island.FarSlot);
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new RoomPathSource(graph), confirmer, audit, new NavRunnerOptions { Name = "island-" + key });
        var now = 0.0;
        runner.Start(goal, now);
        var end = now + 240;
        while (!runner.IsDone && now < end)
        {
            now += 1.0 / Fps;
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / Fps);
        }

        var line = string.Create(CultureInfo.InvariantCulture, $"{island.Name}: 0x{island.Arrival:X8} -> 0x{island.FarSlot:X8} {runner.State} in {now:0.0} s, {runner.RouteMetres:0.0} m, body in 0x{walker.Position.CellId:X8} {runner.VerdictFields()}");
        output.WriteLine(line + (runner.FailureReason is { } why ? " -- " + why : string.Empty));
        Assert.True(runner.State == NavRunState.Arrived, line);
        Assert.Equal(island.FarSlot, walker.Position.CellId);
    }

    /// <summary>
    /// The Abode's (and the Holding's) lower level, z -12, is one walk once a body is down there (M6 4 W6b): its north part (from 0x1AD) and its south part (to 0x1B3) join
    /// through the corridor 0x1AE to 0x1B2, cells with no floor polygon of their own whose floor is a portal onto the bridge cells 0x100 to 0x118 (the bridge's floor lies on
    /// that portal at -12; <see cref="CellRoutePlanner"/>'s floor below a floor portal). The crossroads 0x1B7 has no physics polygon at all and opens 6 m down onto 0x11E:
    /// its four doorways are refused (before W6b a structure with no physics polygons passed the sill rule as the synthetic rooms do). Counts: 230 cells with a floor from
    /// 0x1AD (152 and 78 apart before), the Surface exit's cell 0x150 among them; a body runs from 0x1AD over the bridge to it.
    /// </summary>
    [RequiresDatsTheory]
    [InlineData("abode")]
    [InlineData("holding")]
    public void The_Abode_s_lower_level_is_one_walk_over_its_bridge_and_its_pit_crossroads_is_refused(string key)
    {
        var island = Islands[key];
        var cells = CellsOf(island);
        var graph = new RoomGraph(cells);
        uint Cell(uint low) => ((uint)island.Landblock << 16) | low;
        var floored = cells.Cells.Where(c => cells.GetStructure(c).Floors.Count > 0).Select(c => c.Id).ToHashSet();
        var lower = Reach(cells, Cell(0x1AD), graph.IsPassable);
        output.WriteLine($"{island.Name}: {lower.Count(floored.Contains)} cells with a floor reached from 0x{Cell(0x1AD):X8}, {lower.Count} cells");
        Assert.Equal(230, lower.Count(floored.Contains));
        Assert.Contains(Cell(0x1B3), lower);
        Assert.Contains(Cell(0x150), lower);
        Assert.DoesNotContain(Cell(0x1B7), lower);
        Assert.DoesNotContain(island.Arrival, lower);

        Assert.Contains(Cell(0x1B0), graph.FindPath(Cell(0x1AD), Cell(0x150))!); // over the bridge: the two parts joined nowhere else
        var (world, walker) = BodyIn(island.Landblock, new CellFrame(Cell(0x1AD), graph.RoomFrame(Cell(0x1AD))!.Value.Frame));
        var (state, line) = Run(graph, world, walker, Cell(0x150), "lower-" + key);
        output.WriteLine(line);
        Assert.Equal(NavRunState.Arrived, state);
        Assert.Equal(Cell(0x150), walker.Position.CellId);
    }

    /// <summary>
    /// M6 4 F2 (review 4-R5): the planner's refusal at the pit edge is a loud stub, not a comment: the graph refusing the doorway 0x2B9 to 0x2A9 logs one <c>TODO(m6-abode-pits)</c> line with both
    /// cells, once a run (a second graph over the same cells adds none), and the Holtburg-style high threshold the same rule spares is not named.
    /// </summary>
    [RequiresDatsTheory]
    [InlineData("abode")]
    [InlineData("holding")]
    public void The_graphs_refusal_at_the_pit_edge_logs_the_abode_pits_stub_once(string key)
    {
        var island = Islands[key];
        var cells = CellsOf(island);
        var from = ((uint)island.Landblock << 16) | 0x2B9;
        var pit = ((uint)island.Landblock << 16) | 0x2A9;
        var cell = cells.GetCell(from)!;
        var portal = cell.Portals.Single(p => p.OtherCellId == pit);
        var log = new Lifestone.Client.Tests.RecordingLogSink();
        var before = CellRoutePlanner.PitLog;
        CellRoutePlanner.ResetPitLog();
        CellRoutePlanner.PitLog = log;
        try
        {
            Assert.False(new RoomGraph(cells).IsPassable(cell, portal));
            Assert.False(new RoomGraph(cells).IsPassable(cell, portal)); // a second graph over the same cells: the run's first line stands
            var mine = log.Lines.Where(l => l.Contains("TODO(m6-abode-pits)", StringComparison.Ordinal)).ToList();
            output.WriteLine(string.Join(" | ", log.Lines));
            Assert.Single(mine);
            Assert.Contains($"0x{from:X8} to 0x{pit:X8}", mine[0], StringComparison.Ordinal);
            Assert.Equal(2, CellRoutePlanner.PitEdgesRefused); // counted once a graph, logged once a run
            Assert.Contains("TODO(m6-abode-pits)", mine[0], StringComparison.Ordinal);
            Assert.Contains("no-path", mine[0], StringComparison.Ordinal);
        }
        finally
        {
            CellRoutePlanner.PitLog = before;
            CellRoutePlanner.ResetPitLog();
        }
    }

    /// <summary>
    /// Why the Abode's far half is not walked: the doorway 0x2B9 to 0x2A9 opens onto the pit, and a walking or running body stops at its brink and
    /// stays at z 0 (retail's edge slide, <c>CTransition::edge_slide</c>, 11.6096 0x0050BEA0: a body that finds no ground ahead does not walk off). Measured on the dats
    /// with the offline walker (its Jump skill a placeholder): a walking jump from the brink lands on the bridge at -12 (0x1B0, impact 16.6 m/s) and a running jump crosses
    /// the 10 m pit to 0x2A0 on the west half; neither is a walk. The runner's jump is <c>PitJumpDatTests</c>' (M6 14 pits): the brink this test finds is its take-off.
    /// </summary>
    [RequiresDatsTheory]
    [InlineData("abode", false)]
    [InlineData("abode", true)]
    [InlineData("holding", true)]
    public void A_body_walking_into_the_Abode_s_pit_stops_at_its_brink(string key, bool run)
    {
        var island = Islands[key];
        var cells = CellsOf(island);
        var start = new CellFrame(((uint)island.Landblock << 16) | 0x2B9, new AcFrame(new Vector3(137f, -60f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f)));
        var cell = cells.GetCell(start.CellId)!;
        Assert.False(new RoomGraph(cells).IsPassable(cell, cell.Portals.Single(p => p.OtherCellId == ((start.CellId & 0xFFFF0000u) | 0x2A9))));

        var (_, walker) = BodyIn(island.Landblock, start);
        var now = 0.0;
        walker.SetHoldRun(run);
        walker.DoMotion(MotionCommands.WalkForward);
        var lowest = float.MaxValue;
        for (var i = 0; i < 3 * Fps; i++)
        {
            now += 1.0 / Fps;
            walker.Tick(now);
            lowest = MathF.Min(lowest, walker.Position.Origin.Z);
        }

        var at = walker.Position.Origin;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{island.Name} run={run}: 0x{walker.Position.CellId:X8} ({at.X:0.00}, {at.Y:0.00}, {at.Z:0.00}), lowest z {lowest:0.00}, on the ground {walker.OnGround}"));
        Assert.True(lowest > -0.1f, "the body fell into the pit");
        Assert.InRange(at.X, 134.5f, 135.5f); // at the brink: the doorway's plane is x 135
        Assert.True(walker.OnGround);
    }

    /// <summary>
    /// Holtburg's inner stair (M6 4 W6b, <c>m6-holtburg-upper-floor</c>'s room half): the ground floor 0xA9B30103 (116.0) opens through a hole in its ceiling into the slab
    /// 0xA9B3010F and on into the upper floor 0xA9B3010A (119.0); the stair is the static Setup 0x020001B4, a ramp the walker climbs (about 1.03 m a metre, its walkable normal
    /// over floor_z). The room graph walks it (<see cref="CellRoutePlanner.StairOf"/>), so from the front room 0xA9B30100 it reaches the upper floor's scriveners' rooms
    /// 0xA9B30107 and 0xA9B30108 (Life and Creature II and up), and a body runs from the stair's room 0xA9B30103 up the stair into each (the front room's own middle stands
    /// in its counters). The walk from the street still needs the
    /// balcony doors' lanes refused (slice W6a's half; the lead's check is <c>HoltburgScrivenerWalkDatTests</c>).
    /// </summary>
    [RequiresDatsTheory]
    [InlineData(0xA9B30107u)]
    [InlineData(0xA9B30108u)]
    public void Holtburg_s_inner_stair_joins_the_ground_floor_to_the_upper_floor_and_a_body_climbs_it(uint goal)
    {
        const uint front = 0xA9B30100;
        var cells = CellsOf(0xA9B3);
        var graph = new RoomGraph(cells);
        var reached = Reach(cells, front, graph.IsPassable);
        Assert.Contains(0xA9B3010Au, reached);
        Assert.Contains(goal, reached);
        var ground = cells.GetCell(0xA9B30103)!;
        var stair = graph.Planner.StairOf(ground, ground.Portals.Single(p => p.OtherCellId == 0xA9B3010F));
        Assert.NotNull(stair);
        var (foot, landing) = (stair!.Value.Foot, stair.Value.Landing);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"stair: foot ({foot.X:0.00}, {foot.Y:0.00}, {foot.Z:0.00}) landing ({landing.X:0.00}, {landing.Y:0.00}, {landing.Z:0.00}), past it in 0x{stair.Value.PastLanding.CellId:X8}, {stair.Value.Length:0.00} m"));
        Assert.True(landing.Z - foot.Z > 2.5f);

        var (world, walker) = BodyIn(0xA9B3, new CellFrame(ground.Id, graph.RoomFrame(ground.Id)!.Value.Frame));
        Assert.Contains(0xA9B3010Fu, graph.FindPath(ground.Id, goal)!);
        var (state, line) = Run(graph, world, walker, goal, "holtburg-stair");
        output.WriteLine(line);
        Assert.Equal(NavRunState.Arrived, state);
        Assert.Equal(goal, walker.Position.CellId);
    }

    /// <summary>
    /// The stair rule elsewhere in the den's town (M6 4 W6b): Archmage Cindrue's own cell 0xA9B4011B, 3 m over her building's entry hall 0xA9B40117, was a step the room graph
    /// refused (<c>TODO(room-steps)</c>, the reason her stop is the hall: <c>ResupplyRouteDatTests</c>); the stair to it is now walked, and a body climbs from the hall to her cell.
    /// </summary>
    [RequiresDatsFact]
    public void Cindrues_stair_from_her_entry_hall_to_her_own_cell_is_walked_and_a_body_climbs_it()
    {
        const uint hall = 0xA9B40117, own = 0xA9B4011B;
        var cells = CellsOf(0xA9B4);
        var graph = new RoomGraph(cells);
        var path = graph.FindPath(hall, own);
        Assert.NotNull(path);
        var hops = path!.Zip(path.Skip(1)).Select(h => (Cell: cells.GetCell(h.First)!, Next: h.Second))
            .Select(h => (h.Cell, Portal: h.Cell.Portals.First(p => p.OtherCellId == h.Next && graph.IsPassable(h.Cell, p)))).ToList();
        var stairs = hops.Where(h => !graph.Planner.IsDoorway(h.Cell, h.Portal) && graph.Planner.FloorCrossingOf(h.Cell, h.Portal) is null && graph.Planner.StairOf(h.Cell, h.Portal) is not null).ToList();
        output.WriteLine($"path {string.Join(" ", path.Select(c => (c & 0xFFFF).ToString("X3")))}; stair hops {string.Join(" ", stairs.Select(h => $"{h.Cell.Id & 0xFFFF:X3}>{h.Portal.OtherCellId & 0xFFFF:X3}"))}");
        Assert.NotEmpty(stairs);

        var (world, walker) = BodyIn(0xA9B4, new CellFrame(hall, graph.RoomFrame(hall)!.Value.Frame));
        var (state, line) = Run(graph, world, walker, own, "cindrue-stair");
        output.WriteLine(line);
        Assert.Equal(NavRunState.Arrived, state);
        Assert.Equal(own, walker.Position.CellId);
    }

    /// <summary>A <see cref="NavRunner"/> over the room graph from the walker's place to the middle of <paramref name="goalCell"/> (240 s at most): its end state and a line.</summary>
    private static (NavRunState State, string Line) Run(RoomGraph graph, PhysicsWorld world, OfflineWalker walker, uint goalCell, string name)
    {
        var h = new ActionHarness(offline: true);
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, null, name);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var room = graph.RoomFrame(goalCell)!.Value;
        var goal = NavGoal.ToPoint(new NavPoint(goalCell & 0xFFFF0000u, room.Origin), goalCell);
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new RoomPathSource(graph), confirmer, audit, new NavRunnerOptions { Name = name });
        var start = walker.Position.CellId;
        var now = 0.0;
        runner.Start(goal, now);
        while (!runner.IsDone && now < 240)
        {
            now += 1.0 / Fps;
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / Fps);
        }

        var line = string.Create(CultureInfo.InvariantCulture, $"{name}: 0x{start:X8} -> 0x{goalCell:X8} {runner.State} in {now:0.0} s, {runner.RouteMetres:0.0} m, body in 0x{walker.Position.CellId:X8} {runner.VerdictFields()}");
        return (runner.State, line + (runner.FailureReason is { } why ? " -- " + why : string.Empty));
    }

    /// <summary>The landblock's collision world and a human walker standing at <paramref name="at"/>.</summary>
    /// <summary>
    /// M6 7 U (r1f-climb, level 10, below Lifestone Recall): the way home from the Burrow, <c>rooms-no-path</c> in 13 ms from 0x57480210 to the exit's front in 0x57480100. It reproduced on this tree (slices A, F, G, J, N and Q in), and two things were
    /// wrong. The room graph: the leg's planner took the ground's fight graph, which leaves the no-go cells out (<see cref="HuntingGround.Walkable"/>), and the exit portal's cell is the profile's no-go cell, on all 11 grounds with a portal exit. A leg with
    /// an end in the exit's cell now plans over the fight graph with that one cell back (<see cref="GroundRooms.LegGraphOf"/>); the fight's graph keeps it out. The front: <c>front=approach</c> took the straight line north, into the alcove's wall, and the
    /// room search left it past the alcove's floor (the physics refused the plan's end): the walk now goes to the portal itself (<c>TravelLeg.FirstFront</c>). Each ground's arrival to its exit, the plan and a body confirmed by the physics.
    /// </summary>
    [RequiresDatsTheory]
    [MemberData(nameof(WalkedExits))]
    public void A_body_runs_from_a_ground_s_arrival_to_its_exit_portal(string key)
    {
        var ground = GroundCatalog.Get(key);
        var exit = ground.Exit!;
        var planner = new JourneyLegPlanner(fixture.Dats, ground: ground);
        var from = new CellFrame(ground.Arrival.Cell, new AcFrame(ground.Arrival.Origin, Quaternion.Identity));
        Assert.True(ground.IsNoGo(exit.Cell), $"{key}: the exit's cell 0x{exit.Cell:X8} is the profile's no-go cell (the case this test exists for)");
        Assert.Null(planner.GroundGraph(ground).Plan(from.CellId, from.Origin, exit.Cell, exit.Origin!.Value)); // the fight's graph keeps it out
        RunToExit(planner, ground, from, exit.Origin.Value);
    }

    /// <summary>M6 7 U: the live start: where the rung change left the body (r1f-climb's 0x57480210 (148.38, -2.03, 0.01)), to the portal the hop now walks to.</summary>
    [RequiresDatsFact]
    public void A_body_runs_from_where_the_rung_change_left_it_to_the_Burrow_s_exit_portal()
    {
        var ground = GroundCatalog.Get("tusker-burrow");
        RunToExit(new JourneyLegPlanner(fixture.Dats, ground: ground), ground, new CellFrame(0x57480210, new AcFrame(new Vector3(148.38f, -2.03f, 0.01f), Quaternion.Identity)), ground.Exit!.Origin!.Value);
    }

    /// <summary>
    /// M6 redoubt, the live start (2026-11-18 22:20:55 UTC, the first way home from the Holtburg Redoubt): where the fight left the body, the bottom room 0x01630101 at (23.90, -80.07, -23.99), to the exit portal by the arrival, up the
    /// storeys and over the entrance hall's walkway (the profile's galleries): the plan and a body confirmed by the physics.
    /// </summary>
    [RequiresDatsFact]
    public void A_body_runs_from_the_Redoubt_s_bottom_room_to_its_exit_portal()
    {
        var ground = GroundCatalog.Get("holtburg-redoubt");
        RunToExit(new JourneyLegPlanner(fixture.Dats, ground: ground), ground, new CellFrame(0x01630101, new AcFrame(new Vector3(23.90f, -80.07f, -23.99f), Quaternion.Identity)), ground.Exit!.Origin!.Value);
    }

    /// <summary>
    /// M6 redoubt (the same live line: <c>no path: portal-keep-off:Holtburg_Redoubt_exit: the goal stands 0.00 m from the walk's own portal, inside its touch and her stop (2.84 m)</c>): a walk whose goal is the portal itself keeps off the
    /// other portals only; a front beside a portal keeps off its own portal too.
    /// </summary>
    [Fact]
    public void The_walk_to_a_portal_walked_to_itself_does_not_keep_off_that_portal()
    {
        var ground = GroundCatalog.Get("holtburg-redoubt");
        var exit = new RungPortal("exit:holtburg-redoubt", "Holtburg Redoubt exit", ground.Exit!.Wcid, 0, ground.Exit.Cell, ground.Exit.Origin!.Value, ground.Exit.Destination >> 16, null, null, ground.Landblocks);
        var (others, own) = (exit.KeepOffTheOthers(), exit.KeepOffForFront());
        Assert.NotNull(own.Own);
        Assert.Null(others.Own);
        Assert.Same(others, TravelLeg.FrontWalkKeep(TravelLeg.FrontBasisPortal, others, own));
        Assert.Same(own, TravelLeg.FrontWalkKeep(TravelLeg.FrontBasisFacing, others, own));
        Assert.Same(own, TravelLeg.FrontWalkKeep(TravelLeg.FrontBasisApproach, others, own));
    }
    /// <summary>M6 7 U: the hop's first point for an indoor portal with no facing is the portal itself (the old approach point stood off the alcove's floor); a portal with a facing keeps it.</summary>
    [Fact]
    public void An_indoor_portal_with_no_facing_is_walked_to_itself_and_a_facing_keeps_the_front()
    {
        var at = new CellFrame(0x57480210, new AcFrame(new Vector3(148.38f, -2.03f, 0.01f), Quaternion.Identity));
        var exit = new RungPortal("exit:tusker-burrow", "Tusker Burrow exit", 22664, 0, 0x57480100, new Vector3(119.843f, -134.228f, -29.995f), 0xF982, null, null, [], null);
        Assert.Equal((0x57480100u, exit.Origin, TravelLeg.FrontBasisPortal), TravelLeg.FirstFront(exit, 0x57480100, at));
        Assert.Equal(TravelLeg.FrontBasisFacing, TravelLeg.FirstFront(exit with { Facing = Vector2.UnitY }, 0x57480100, at).Basis);
    }

    /// <summary>
    /// The Abode's and the Holding's exits lie on the lower level: no walk reaches them from the arrival, by name, with the exit's cell in the graph and the profile's jump over the pit too (M6 14 pits: the jump joins the upper
    /// level's halves; the lower level is one way down, <c>TODO(m6-abode-lower-level)</c>).
    /// </summary>
    [RequiresDatsTheory]
    [InlineData("tusker-abode")]
    [InlineData("tusker-holding")]
    public void The_exits_past_the_Abode_s_pit_stay_refused_by_the_room_graph(string key)
    {
        var ground = GroundCatalog.Get(key);
        var exit = ground.Exit!;
        var goal = NavGoal.ToPoint(new NavPoint(exit.Cell & 0xFFFF0000u, exit.Origin!.Value), exit.Cell);
        var from = new CellFrame(ground.Arrival.Cell, new AcFrame(ground.Arrival.Origin, Quaternion.Identity));
        var path = new JourneyLegPlanner(fixture.Dats, ground: ground).SourceFor(from.CellId, exit.Cell, null).Plan(from, goal, new NavScars());
        Assert.Equal((NavPathStatus.Failed, "rooms-no-path"), (path.Status, path.Reason));
    }

    private void RunToExit(JourneyLegPlanner planner, HuntingGround ground, CellFrame start, Vector3 to)
    {
        var exit = ground.Exit!.Cell;
        var (world, walker) = BodyIn(ground.Landblocks[0], start);
        var h = new ActionHarness(offline: true);
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, null, "exit-walk");
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var goal = NavGoal.ToPoint(new NavPoint(exit & 0xFFFF0000u, to), exit);
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), planner.SourceFor(start.CellId, exit, null, out var why), confirmer, audit, new NavRunnerOptions { Name = "exit-" + ground.Key });
        var now = 0.0;
        runner.Start(goal, now);
        var end = now + 240;
        while (!runner.IsDone && now < end)
        {
            now += 1.0 / Fps;
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / Fps);
        }

        var line = string.Create(CultureInfo.InvariantCulture, $"{ground.Key}: 0x{start.CellId:X8} -> 0x{exit:X8} {runner.State} in {now:0.0} s, {runner.RouteMetres:0.0} m, body in 0x{walker.Position.CellId:X8} ({why})");
        output.WriteLine(line + (runner.FailureReason is { } reason ? " -- " + reason : string.Empty));
        Assert.True(runner.State == NavRunState.Arrived, line + " -- " + runner.FailureReason);
    }

    /// <summary>
    /// M6 7 AJ (<c>m6-body-apart</c>), the replay of gate A's second stick (2026-11-06 13:02 UTC): the Lodge's ramp on the dats, no creature and no corpse in the world. From the arrival, with the arrival's own constraint and no echo after
    /// it (the server had the player on no landblock), the run fades to a stop on the ramp where she stopped live, (50.00, -131.77, 2.02), and from there no direction moves the body: the constraint adds up the distance moved in any direction,
    /// so nothing but a new position from the server frees it. One echo there and the same keys run on, and with echoes all the way the ramp is run to its top.
    /// </summary>
    [RequiresDatsFact]
    public void The_Lodge_ramp_stick_is_the_constraint_with_no_echoes_and_no_body_in_the_way()
    {
        var island = Islands["lodge"];
        var start = new CellFrame(island.Arrival, new AcFrame(island.Origin, Quaternion.Identity));

        var (_, free) = BodyIn(island.Landblock, start);
        var climbed = RunNorth(free, start.CellId, seconds: 8, echoEverySecond: true);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"with echoes: 0x{free.Position.CellId:X8} ({climbed.X:0.00}, {climbed.Y:0.00}, {climbed.Z:0.00})"));
        Assert.True(climbed.Y > -127f, "with echoes the body runs past the place of the stick");

        var (_, held) = BodyIn(island.Landblock, start);
        var stopped = RunNorth(held, start.CellId, seconds: 60, echoEverySecond: false);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"no echoes: 0x{held.Position.CellId:X8} ({stopped.X:0.00}, {stopped.Y:0.00}, {stopped.Z:0.00})"));
        Assert.InRange(stopped.Y, -133f, -131f);
        Assert.InRange(stopped.Z, 1f, 3f);

        held.StopMotion(MotionCommands.WalkForward);
        foreach (var command in new[] { MotionCommands.WalkForward, MotionCommands.WalkBackwards, MotionCommands.SideStepLeft, MotionCommands.SideStepRight })
        {
            var from = held.Position.Origin;
            var now = 100.0;
            held.DoMotion(command);
            for (var i = 0; i < 3 * Fps; i++)
            {
                now += 1.0 / Fps;
                held.Tick(now);
            }

            held.StopMotion(command);
            var moved = Vector3.Distance(from, held.Position.Origin);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"0x{command:X8}: moved {moved:0.000} m"));
            Assert.True(moved < 0.15f, $"0x{command:X8} moved the body {moved} m past the constraint's end");
        }

        var here = held.Position;
        held.Physics.ConstrainTo(here, ServerPositionPolicy.StartConstraintDistance(here.CellId), ServerPositionPolicy.MaxConstraintDistance(here.CellId)); // one echo
        var after = 200.0;
        held.DoMotion(MotionCommands.WalkForward);
        for (var i = 0; i < 2 * Fps; i++)
        {
            after += 1.0 / Fps;
            held.Tick(after);
        }

        Assert.True(Vector3.Distance(here.Origin, held.Position.Origin) > 2f, "one echo frees the body");
    }

    private static Vector3 RunNorth(OfflineWalker walker, uint cell, double seconds, bool echoEverySecond)
    {
        walker.Physics.ConstrainTo(walker.Position, ServerPositionPolicy.StartConstraintDistance(cell), ServerPositionPolicy.MaxConstraintDistance(cell)); // SmartBox::TeleportPlayer's, as PlayerMovementController.Apply does on the arrival
        walker.SetHoldRun(true);
        walker.DoMotion(MotionCommands.WalkForward);
        var now = 0.0;
        for (var i = 1; i <= seconds * Fps; i++)
        {
            now += 1.0 / Fps;
            walker.Tick(now);
            if (echoEverySecond && i % (int)Fps == 0)
            {
                var at = walker.Position;
                walker.Physics.ConstrainTo(at, ServerPositionPolicy.StartConstraintDistance(at.CellId), ServerPositionPolicy.MaxConstraintDistance(at.CellId));
            }
        }

        return walker.Position.Origin;
    }

    private (PhysicsWorld World, OfflineWalker Walker) BodyIn(ushort landblock, CellFrame at)
    {
        var dats = fixture.Dats;
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, new PhysicsGeometryLibrary(dats)).Build(new LandblockCoord((byte)(landblock >> 8), (byte)(landblock & 0xFF))));
        var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(RunSkill));
        world.UseTime(0.0);
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(at, sliding: true));
        return (world, walker);
    }
}

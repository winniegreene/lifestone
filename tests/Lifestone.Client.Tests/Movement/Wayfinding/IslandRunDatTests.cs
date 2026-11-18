using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World.Cells;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// M6 4 w3 on the real dats (Exhaustive tier, skips without them): the island's surface runs of the travel between rungs. Each case starts where a portal drops the body and walks, through the headless streamer
/// (the 3 x 3 plus the route's corridor, <see cref="HeadlessStreamer.FollowRoute"/> twice a second), to the front of the next ground's entry portal, with horizons replanned as the runner does, the physics confirmer
/// on every segment (<see cref="NavRunnerOptions.ConfirmBudgetMilliseconds"/>) and the arrival within <see cref="PortalRangeMetres"/> of the front (the travel stations' portal range). The tiles flag a 24 m land cell Water when
/// any corner is water; the planner costs it and never blocks it (retail <c>CLandBlockStruct::calc_water_depth</c>, 11.6096 0x00532330: the body sinks 0.45 m at a water corner, 0.1 m elsewhere), so a run may wade.
/// Per run the log carries the metres planned, the metres walked in cells the tile flags Water, the water cells crossed, the longest wade, the replans and the plans' milliseconds.
/// </summary>
/// <remarks>
/// The positions are test data from the pinned world SQL v0.9.296 (read-only), each with its row: a drop is the entry's <c>weenie_properties_position</c> row of position type 2 (the portal's Destination), a front is the
/// surface portal's <c>landblock_instance</c> row moved 3 m along its facing (<c>GroundDraft.FrontOf</c>: the +Y axis turned by the row's yaw quaternion). The product's copy lives in the travel file, not here.
/// </remarks>
[Trait("Tier", "Exhaustive")]
public sealed class IslandRunDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const double Fps = 60.0;

    /// <summary>How near the portal the walk ends: the portal exit's stop (<c>PortalExit.DefaultStopWithinMetres</c>), as an object goal's range (<c>NavGoal.ToObject</c>).</summary>
    private const float PortalRangeMetres = 4f;

    /// <summary>Each case: the start cell, its point (landblock-local) and the goal landblock and point.</summary>
    private sealed record Case(string From, uint StartCell, Vector3 Start, string To, uint GoalBlock, Vector2 Goal, double StraightMetres, float Range = PortalRangeMetres)
    {
        public string Name => From + ">" + To + (Range == PortalRangeMetres ? string.Empty : "@" + Range.ToString("0.#", CultureInfo.InvariantCulture));
    }

    // Starts (weenie_properties_position, position_Type 2, the Destination row of each portal):
    //   Refuge arrival: weenie 43002 (Town Network "Oolutanga's Refuge"): 0xF6820033 (145.7, 49.9, 58.0), also spell 2936.
    //   Facility Hub drops: 42177 Burrow 0xF9810038 (155.573, 188.208, 13.0406); 42181 Lodge 0xF97F003E (187.876, 122.937, 46.1508);
    //     42178 Cave 0xF97F0008 (18.4629, 180.623, 17.1861); 42175 Abode 0xF67B000C (26.259, 84.692, -0.095); 42182 Quarters 0xF67C003D (172.539, 112.711, 19.4264);
    //     42176 Barracks 0xF07E0040 (171.613, 188.834, 10.9496); M6 7 J: 42179 Cavern 0xF8800027 (97.682, 147.319, 12.005); 42180 Habitat 0xF77E0017 (57.8188, 165.736, 18.0187).
    //   New Colier Portal 12527 (guid 0x7ABB1005) Destination 0xA6CB001F (82.544, 154.642, 56.892). Holtburg arrival (the Network's way home, weenie 42820): 0xA9B40019 (84.0, 7.1, 94.0).
    // Goals (landblock_instance, bitmask 49 surface entries; the portal's own point, the walk ends within PortalRangeMetres of it as an object goal; the front 3 m along the facing lay inside a rock arch's box stamp at four of them until M6 4 w5):
    //   22663 Burrow 0x7F981000 (176.323, 186.044, 12.5) front (179.24, 186.73); 22688 Lodge 0x7FA7F000 (7.605, 120.782, 45.44) front (10.54, 120.14);
    //   22665 Cave 0x7F97F000 (30.291, 181.197, 19.25) front (32.06, 183.62); 22654 Abode 0x7F67B000 (27.72, 93.305, -0.095) front (25.48, 95.30);
    //   22692 Quarters 0x7F67C000 (179.658, 116.6, 19.878) front (176.81, 115.66); 22680 Holding 0x7EA7B000 (15.744, 17.16, 54.575) front (18.21, 15.44);
    //   M6 7 J: 22661 Barracks 0x7F07F000 (177.07, 15.6767, 12.0); 22667 Cavern 0x7F880000 (103.346, 133.513, 12.0); 22678 Habitat 0x7F77E000 (53.7317, 154.137, 19.27).
    //   22698 Tusker Temple 20-40 0x7A2CA000 (186.042, 34.587, 110.391) front (185.24, 37.48); 12527 New Colier 0x7ABB1005 (68.63, 126.407, 94.53) front (66.07, 127.97).
    // M6 4 w5: the four portals framed by a rock arch (model 0x020007D9, a tilted physics-BSP part whose world box is about 20 x 20 m) and the Hub's Burrow and Cave drops
    // inside the arches used to be named gaps (TODO(m6-island-run-burrow|lodge|cave|quarters): a 4 m object goal ended no-open-node-in-range, the two drops start-blocked).
    // NavTileBuilder now stamps a tilted physics-BSP part by its own physics at walking height, so every run arrives at the journeys' 4 m; the arches' stamps are pinned by
    // The_rock_arch_round_a_portal_is_stamped_by_its_physics_at_walking_height_not_its_world_box.
    private static readonly Case[] Cases =
    [
        new("refuge", 0xF6820033, new(145.7f, 49.9f, 58.0f), "burrow", 0xF9810000, new(176.323f, 186.044f), 609),
        new("refuge", 0xF6820033, new(145.7f, 49.9f, 58.0f), "lodge", 0xFA7F0000, new(7.605f, 120.782f), 807),
        new("refuge", 0xF6820033, new(145.7f, 49.9f, 58.0f), "cave", 0xF97F0000, new(30.291f, 181.197f), 640),
        new("refuge", 0xF6820033, new(145.7f, 49.9f, 58.0f), "abode", 0xF67B0000, new(27.72f, 93.305f), 1306),
        new("refuge", 0xF6820033, new(145.7f, 49.9f, 58.0f), "quarters", 0xF67C0000, new(179.658f, 116.6f), 1086),
        new("refuge", 0xF6820033, new(145.7f, 49.9f, 58.0f), "holding", 0xEA7B0000, new(15.744f, 17.16f), 2796),
        new("hub-burrow", 0xF9810038, new(155.573f, 188.208f, 13.0406f), "burrow", 0xF9810000, new(176.323f, 186.044f), 21),
        new("hub-lodge", 0xF97F003E, new(187.876f, 122.937f, 46.1508f), "lodge", 0xFA7F0000, new(7.605f, 120.782f), 12),
        new("hub-cave", 0xF97F0008, new(18.4629f, 180.623f, 17.1861f), "cave", 0xF97F0000, new(30.291f, 181.197f), 12),
        new("hub-abode", 0xF67B000C, new(26.259f, 84.692f, -0.095f), "abode", 0xF67B0000, new(27.72f, 93.305f), 9),
        new("hub-quarters", 0xF67C003D, new(172.539f, 112.711f, 19.4264f), "quarters", 0xF67C0000, new(179.658f, 116.6f), 8),
        new("hub-barracks", 0xF07E0040, new(171.613f, 188.834f, 10.9496f), "holding", 0xEA7B0000, new(15.744f, 17.16f), 1506),
        new("hub-barracks", 0xF07E0040, new(171.613f, 188.834f, 10.9496f), "barracks", 0xF07F0000, new(177.07f, 15.6767f), 20),
        new("hub-barracks", 0xF07E0040, new(171.613f, 188.834f, 10.9496f), "armory", 0xEF7F0000, new(31.9169f, 92.1597f), 345), // M6 14 armory: 22657 Tusker Armory 0x7EF7F000 (31.9169, 92.1597, 12.005)
        new("hub-cavern", 0xF8800027, new(97.682f, 147.319f, 12.005f), "cavern", 0xF8800000, new(103.346f, 133.513f), 15),
        new("hub-habitat", 0xF77E0017, new(57.8188f, 165.736f, 18.0187f), "habitat", 0xF77E0000, new(53.7317f, 154.137f), 12),
        new("new-colier", 0xA6CB001F, new(82.544f, 154.642f, 56.892f), "temple-20-40", 0xA2CA0000, new(186.042f, 34.587f), 734),
        new("holtburg", 0xA9B40019, new(84.0f, 7.1f, 94.0f), "new-colier", 0xABB10000, new(68.63f, 126.407f), 590),
    ];

    /// <summary>The model of the island's rock arches (a Setup whose part is a tilted physics-BSP GfxObj).</summary>
    private const uint ArchModel = 0x020007D9;

    /// <summary>Each arch point: a portal (the walk's goal, an open node needed within <see cref="PortalRangeMetres"/>) or a Hub drop (the walk's start, an open node needed within the planner's start snap).</summary>
    private sealed record ArchPoint(string Name, uint Block, Vector2 Point, bool IsStart);

    private static readonly ArchPoint[] ArchPoints =
    [
        new("burrow", 0xF9810000, new(176.323f, 186.044f), false),
        new("lodge", 0xFA7F0000, new(7.605f, 120.782f), false),
        new("cave", 0xF97F0000, new(30.291f, 181.197f), false),
        new("quarters", 0xF67C0000, new(179.658f, 116.6f), false),
        new("hub-burrow", 0xF9810000, new(155.573f, 188.208f), true),
        new("hub-cave", 0xF97F0000, new(18.4629f, 180.623f), true),
    ];

    public static TheoryData<string> ArchNames()
    {
        var data = new TheoryData<string>();
        foreach (var a in ArchPoints)
        {
            data.Add(a.Name);
        }

        return data;
    }

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var c in Cases)
        {
            data.Add(c.Name);
        }

        return data;
    }

    private DatSet Dats => fixture.Dats;

    [RequiresDatsTheory]
    [MemberData(nameof(Names))]
    public void The_walk_from_the_drop_to_the_portals_front_plans_confirms_and_arrives_through_the_streamer(string name)
    {
        var c = Cases.Single(x => x.Name == name);
        var world = new PhysicsWorld();
        var map = new NavMap();
        using var streamer = new HeadlessStreamer(Dats, null, world, map, NullLogSink.Instance);
        streamer.EnsureNow(c.StartCell);

        var h = new ActionHarness(offline: true);
        var now = 0.0;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, "island-" + name);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        var start = new CellFrame(c.StartCell, new AcFrame(c.Start, Quaternion.Identity));
        Assert.True(walker.Place(start), $"placed at 0x{c.StartCell:X8} {c.Start}");
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var goal = NavGoal.ToObject(0x7F000000u | c.GoalBlock >> 16, new NavPoint(c.GoalBlock, new Vector3(c.Goal.X, c.Goal.Y, 0f)), c.Range, needSight: false, null);
        var line = Vector2.Distance(NavMap.FromCellFrame(start).Global, goal.Point.Global);
        var source = new TimedSource(new OutdoorPathSource(new PathPlanner(map)));
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, new NavRunnerOptions { Name = "island-" + name, ConfirmBudgetMilliseconds = 2 });
        runner.Start(goal, now);

        var water = new WaterRead(map);
        var nextFollow = 0.0;
        var limit = 200.0 + (line / 2.5);
        var wall = Stopwatch.StartNew();
        var rungs = 0;
        var rungAt = new List<string>();
        while (!runner.IsDone && now < limit)
        {
            now += 1.0 / Fps;
            if (now >= nextFollow)
            {
                nextFollow = now + 0.5;
                streamer.FollowRoute(runner, walker.Position);
            }

            streamer.Pump();
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / Fps);
            water.Sample(walker.Position);
            if (runner.Rungs != rungs)
            {
                rungs = runner.Rungs;
                rungAt.Add($"t={now:F1} 0x{walker.Position.CellId:X8} ({walker.Position.Origin.X:F1},{walker.Position.Origin.Y:F1},{walker.Position.Origin.Z:F2}) water={water.InWater}");
            }

            if (streamer.Busy)
            {
                Thread.Sleep(1);
            }
        }

        var end = NavMap.FromCellFrame(walker.Position).Global;
        var left = Vector2.Distance(end, goal.Point.Global);
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"island {name}: {runner.State} straight={line:F0} m planned={runner.RouteMetres:F0} m walked={water.WalkedMetres:F0} m water_m={water.WaterMetres:F0} water_cells={water.Cells.Count} longest_wade_m={water.LongestWade:F0} replans={runner.Replans} plans={source.Count} plan_ms_total={source.TotalMilliseconds:F0} plan_ms_max={source.MaxMilliseconds:F0} rungs={runner.Rungs} left={left:F2} m sim_s={now:F0} wall_s={wall.Elapsed.TotalSeconds:F1} {runner.FailureReason}"));
        output.WriteLine(streamer.StatsLine());
        foreach (var r in rungAt)
        {
            output.WriteLine("  rung " + r);
        }

        if (runner.State != NavRunState.Arrived)
        {
            output.WriteLine("  attached tiles: " + string.Join(' ', map.AttachedLandblocks.OrderBy(x => x).Select(x => (x >> 16).ToString("X4", CultureInfo.InvariantCulture))));
            output.WriteLine("  last route: " + string.Join(" > ", (runner.Path?.Waypoints ?? []).Select(w => $"0x{w.At.CellId:X8}({w.At.Frame.Origin.X:F0},{w.At.Frame.Origin.Y:F0},{w.At.Frame.Origin.Z:F1})")));
        }

        Assert.Equal(0, streamer.TileFailures);
        Assert.True(runner.State == NavRunState.Arrived, $"{runner.State}: {runner.FailureReason}");
        Assert.Equal(0, runner.Rungs);
        Assert.InRange(left, 0, c.Range + 0.5f);
    }

    /// <summary>World walk W0: the offline rig is this file's loop lifted out, so each route must end the same way through it: arrived, with no recovery rung, within the portal's range.</summary>
    [RequiresDatsTheory]
    [MemberData(nameof(Names))]
    public void The_offline_rig_agrees_with_the_walk_through_the_streamer(string name)
    {
        var c = Cases.Single(x => x.Name == name);
        var geometry = new PhysicsGeometryLibrary(Dats);
        var rig = new OfflineWalkRig(Dats, geometry, new DatAnimationSource(Dats), new LandblockCollisionBuilder(Dats, geometry).Build, NullLogSink.Instance);
        var start = new CellFrame(c.StartCell, new AcFrame(c.Start, Quaternion.Identity));
        var goal = NavGoal.ToObject(0x7F000000u | c.GoalBlock >> 16, new NavPoint(c.GoalBlock, new Vector3(c.Goal.X, c.Goal.Y, 0f)), c.Range, needSight: false, null);
        var line = Vector2.Distance(NavMap.FromCellFrame(start).Global, goal.Point.Global);
        var walk = rig.Walk(new OfflineWalkRequest(name, start, goal, 200.0 + (line / 2.5)));
        output.WriteLine($"rig {name}: {walk.State} walked {walk.WalkedMetres:F0} m, rungs {walk.Rungs}, refused {walk.ConfirmFailures}, left {walk.LeftMetres:F2} m, {walk.SimSeconds:F0} sim s, {walk.WallMilliseconds:F0} ms, {walk.Reason}");
        Assert.Equal(NavRunState.Arrived, walk.State);
        Assert.Equal(0, walk.Rungs);
        Assert.InRange(walk.LeftMetres, 0, c.Range + 0.5f);
    }

    /// <summary>
    /// Retail's rule that the first island plans broke (<c>CLandCell::find_env_collisions</c> 11.6096 0x00533C60: a landblock entirely under water collides with every body but the camera and missiles): the sea landblock 0xEF7D
    /// is all Blocked in its tile, and the row of the coast landblock 0xEF7E beside it is blocked within the body's reach, while a partly flooded landblock only costs (Water, not Blocked).
    /// </summary>
    [RequiresDatsFact]
    public void An_entirely_flooded_landblock_is_a_wall_and_a_partly_flooded_one_only_costs()
    {
        var world = new PhysicsWorld();
        var map = new NavMap();
        using var streamer = new HeadlessStreamer(Dats, null, world, map, NullLogSink.Instance);
        streamer.EnsureNow(0xEF7E0001); // the ring takes 0xEF7D (the sea, south) and 0xEE7E, 0xF07E (coast, west and east)
        Assert.Equal(WaterType.EntirelyWater, world.GetLandblock(new LandblockCoord(0xEF, 0x7D))!.WaterType);
        Assert.Equal(WaterType.PartiallyWater, world.GetLandblock(new LandblockCoord(0xEF, 0x7E))!.WaterType);
        Assert.True(map.TryGetTile(0xEF7D0000, out var sea));
        Assert.All(sea.Flags, f => Assert.Equal(NavFlags.Blocked | NavFlags.Water, f & (NavFlags.Blocked | NavFlags.Water)));
        Assert.True(map.TryGetTile(0xEF7E0000, out var coast));
        for (var x = 0; x < NavTile.Size; x++)
        {
            Assert.NotEqual(NavFlags.None, coast.Flags[NavTile.Index(x, 0)] & NavFlags.Blocked); // the row beside the sea
        }

        var open = 0;
        for (var x = 0; x < NavTile.Size; x++)
        {
            open += (coast.Flags[NavTile.Index(x, 5)] & NavFlags.Blocked) == 0 ? 1 : 0;
        }

        output.WriteLine($"0xEF7E row y=5: {open} of {NavTile.Size} nodes open (the sea's wall is the row y=0 only)");
        Assert.True(open > 0);
    }

    /// <summary>
    /// M6 4 w5, TODO(nav-static-footprint) for a tilted physics-BSP part: the rock arch round each of four portals (model 0x020007D9) is stamped by its own physics at walking
    /// height (<c>NavTileBuilder</c>'s body spheres placed against the part's BSP, retail <c>BSPTREE::find_collisions</c>'s placement test, 11.6096 0x0053B1A0), not by its
    /// tilted world box: the arches still block nodes (their legs), far fewer than the box did, and an open node now lies within the portal's 4 m (a goal) or within the planner's start snap
    /// (a Hub drop). The box count is the old rule recomputed here (the part's world box inflated by 0.58 m over the walking band), for the log and the comparison.
    /// </summary>
    [RequiresDatsTheory]
    [MemberData(nameof(ArchNames))]
    public void The_rock_arch_round_a_portal_is_stamped_by_its_physics_at_walking_height_not_its_world_box(string name)
    {
        var a = ArchPoints.Single(x => x.Name == name);
        var coord = LandblockCoord.FromId(a.Block);
        var world = new PhysicsWorld();
        var map = new NavMap();
        using var streamer = new HeadlessStreamer(Dats, null, world, map, NullLogSink.Instance);
        streamer.EnsureNow(CellIds.LandCellAt(coord, a.Point.X, a.Point.Y)!.Value);
        Assert.True(map.TryGetTile(a.Block, out var tile));

        var nearest = float.MaxValue;
        for (var y = 0; y < NavTile.Size; y++)
        {
            for (var x = 0; x < NavTile.Size; x++)
            {
                if (tile.IsWalkable(x, y))
                {
                    nearest = MathF.Min(nearest, Vector2.Distance(NavTile.NodeCentre(x, y), a.Point));
                }
            }
        }

        const float infl = 0.58f;
        var arches = 0;
        // The arches near the point, this landblock's own and its neighbours' reaching in (the Burrow's stands in 0xF982), in this tile's metres.
        var near = new List<(PhysStaticObject Obj, Vector3 Offset)>();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (coord.Offset(dx, dy) is { } other && world.GetLandblock(other) is { } neighbour)
                {
                    var offset = new Vector3(dx * (float)LandDefs.BlockLength, dy * (float)LandDefs.BlockLength, 0f);
                    near.AddRange(neighbour.StaticObjects
                        .Where(o => o.ModelId == ArchModel && Vector2.Distance(new(o.Position.Frame.Origin.X + offset.X, o.Position.Frame.Origin.Y + offset.Y), a.Point) < 30f)
                        .Select(o => (o, offset)));
                }
            }
        }

        // Each arch is measured in its own landblock's tile (the Burrow's stand over the border in 0xF982): the nodes it owns there by physics, against its world box's.
        foreach (var (obj, offset) in near)
        {
            var home = LandblockCoord.FromId(obj.Position.CellId);
            Assert.True(map.TryGetTile(((uint)home.X << 24) | ((uint)home.Y << 16), out var own), $"the tile of 0x{home.X:X2}{home.Y:X2} is attached");
            var origin = obj.Position.Frame.Origin;
            var owner = own.Obstacles.FindIndex(o => o.Id == ArchModel && Vector3.Distance(o.Origin, origin) < 1e-3f);
            var byPhysics = owner < 0 ? 0 : own.Owner.Count(o => o == owner);
            var byBox = 0;
            foreach (var part in obj.Parts.Where(p => p.Shape.Physics is not null))
            {
                var (lo, hi) = WorldBox(part);
                for (var y = 0; y < NavTile.Size; y++)
                {
                    for (var x = 0; x < NavTile.Size; x++)
                    {
                        var c = NavTile.NodeCentre(x, y);
                        var ground = own.Z[NavTile.Index(x, y)];
                        byBox += c.X >= lo.X - infl && c.X <= hi.X + infl && c.Y >= lo.Y - infl && c.Y <= hi.Y + infl && hi.Z > ground + 0.05f && lo.Z < ground + 2f ? 1 : 0;
                    }
                }

                var q = part.Position.Frame.Orientation;
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  part {part.Shape.Id:X8}: quat ({q.X:F3},{q.Y:F3},{q.Z:F3},{q.W:F3}) world box ({lo.X:F1},{lo.Y:F1},{lo.Z:F1})..({hi.X:F1},{hi.Y:F1},{hi.Z:F1}) polygons={part.Shape.Physics!.Polygons.Count}"));
            }

            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: arch 0x{home.X:X2}{home.Y:X2} at ({origin.X:F1},{origin.Y:F1},{origin.Z:F1}), {Vector2.Distance(new(origin.X + offset.X, origin.Y + offset.Y), a.Point):F1} m away: nodes by physics={byPhysics} by box={byBox}"));
            Assert.InRange(byPhysics, 1, byBox - 1);
            arches++;
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: nearest open node {nearest:F2} m from the {(a.IsStart ? "drop" : "portal")}"));
        Assert.True(arches > 0, "an arch is near");
        Assert.InRange(nearest, 0f, a.IsStart ? PathPlanner.StartSnapNodes * NavTile.NodeMetres : PortalRangeMetres - 1f);
    }

    private static (Vector3 Lo, Vector3 Hi) WorldBox(PhysPart part)
    {
        var bounds = part.Shape.Bounds;
        var frame = part.Position.Frame;
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (var k = 0; k < 8; k++)
        {
            var corner = new Vector3((k & 1) == 0 ? bounds.Min.X : bounds.Max.X, (k & 2) == 0 ? bounds.Min.Y : bounds.Max.Y, (k & 4) == 0 ? bounds.Min.Z : bounds.Max.Z) * part.GfxObjScale;
            var w = frame.Origin + Vector3.Transform(corner, frame.Orientation);
            lo = Vector3.Min(lo, w);
            hi = Vector3.Max(hi, w);
        }

        return (lo, hi);
    }

    /// <summary>Counts the plans and times them, for the log.</summary>
    private sealed class TimedSource(INavPathSource inner) : INavPathSource
    {
        public int Count { get; private set; }

        public double TotalMilliseconds { get; private set; }

        public double MaxMilliseconds { get; private set; }

        public NavMap? Map => inner.Map;

        public PathPlanner? Planner => inner.Planner;

        public NavPath Plan(in CellFrame from, NavGoal goal, NavScars scars)
        {
            var t = Stopwatch.StartNew();
            var path = inner.Plan(from, goal, scars);
            var ms = t.Elapsed.TotalMilliseconds;
            Count++;
            TotalMilliseconds += ms;
            MaxMilliseconds = Math.Max(MaxMilliseconds, ms);
            return path;
        }
    }

    /// <summary>Reads the walk against the tile's Water flag: metres walked on water nodes, the 24 m land cells entered that are water, the longest run of water metres.</summary>
    private sealed class WaterRead(NavMap map)
    {
        private Vector2? _last;
        private double _wade;

        public bool InWater => _wade > 0;

        public double WalkedMetres { get; private set; }

        public double WaterMetres { get; private set; }

        public double LongestWade { get; private set; }

        public HashSet<(int, int)> Cells { get; } = [];

        public void Sample(in CellFrame body)
        {
            var point = NavMap.FromCellFrame(body);
            var here = point.Global;
            var step = _last is { } last ? Vector2.Distance(last, here) : 0f;
            _last = here;
            if (step > 20f || !map.TryGetNode(point, out var node))
            {
                return;
            }

            WalkedMetres += step;
            if ((node.Flags & NavFlags.Water) != 0)
            {
                WaterMetres += step;
                _wade += step;
                LongestWade = Math.Max(LongestWade, _wade);
                Cells.Add((node.Gx / NavMap.LandCellNodes, node.Gy / NavMap.LandCellNodes));
            }
            else
            {
                _wade = 0;
            }
        }
    }
}

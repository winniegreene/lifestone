using System.Numerics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Tests.Session.Grounds;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Data.World.Data;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// P5a lane S5 on the real dats (Exhaustive tier: it reads the dats and skips without them, the pattern of <c>OfflineRouteTests</c>): the
/// explore-mode <see cref="OfflineWalker"/>, bound to an offline session's <c>Move</c> and run by <see cref="NavRunner"/> over confirmed
/// paths, walks from Holtburg's lifestone through the doors into Renald's front room 0xA9B4013F (his room 0xA9B40141, behind the
/// counter, the physics refuses: TODO(room-steps)) and Thelnoth's cellar 0xA9B40111, from the
/// lifestone to the belt cell 0xA5B00032 over the straight line's corridor of tiles (attached here by hand; S6 adds the streamer), and the
/// Black Spawn Den's tour over the room graph with lean 0.3; each without a rung of the stuck ladder, with the verdict fields printed.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class NavRunnerDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint RenaldShopCell = 0xA9B40141;

    /// <summary>The front room of Renald's shop, inside its door (0x150): the shop's floor in front of the counter.</summary>
    private const uint RenaldFrontRoomCell = 0xA9B4013F;
    private const uint ThelnothCellarCell = 0xA9B40111;
    private const uint BeltCell = 0xA5B00032;
    private const double Fps = 60.0;

    /// <summary>The Run skill whose rate is the fighters' buffed one, 2.47 (9.6-9.9 m/s: P5a live round 1), by <c>MovementSystem.GetRunRate</c>: the walks' default.</summary>
    private const int FighterRunSkill = 230;

    /// <summary>The Run skill of the BSD Mage's rate, about 2.15 (P5a research: Run 144 ranks).</summary>
    private const int MageRunSkill = 144;

    private DatSet Dats => fixture.Dats;

    public static TheoryData<uint> Shops => new() { RenaldFrontRoomCell, ThelnothCellarCell };

    [RequiresDatsTheory]
    [MemberData(nameof(Shops))]
    public void The_walker_runs_from_the_lifestone_through_the_door_into_the_shop_room_without_a_rung(uint shopCell)
    {
        var (world, map) = Holtburg();
        var source = new IndoorOutdoorPathSource(new PathPlanner(map), LandblockCellSet.Load(new CellStructureLibrary(Dats), LandblockCoord.Holtburg));
        var goalAt = source.Rooms.RoomFrame(shopCell)!.Value.Origin;
        var goal = NavGoal.ToPoint(new NavPoint(shopCell & 0xFFFF0000u, goalAt), shopCell);

        var (runner, visited, confirm) = Walk(world, source, Lifestone(), goal, 120, "shop-" + shopCell.ToString("X8"));

        var planned = runner.Path!.CellsEntered;
        output.WriteLine($"0x{shopCell:X8}: planned cells {string.Join(" ", planned.Select(c => (c & 0xFFFF).ToString("X3")))}; entered {string.Join(" ", visited.Where(c => !LandDefs.IsOutdoorCell(c)).Select(c => (c & 0xFFFF).ToString("X3")))}");
        output.WriteLine($"cells left out after a refused confirmation: {string.Join(" ", source.AvoidedCells.Select(c => (c & 0xFFFF).ToString("X3")))}");
        output.WriteLine($"room-steps evidence: the confirmed path's largest step up {confirm.Up:F2} m, down {confirm.Down:F2} m");
        Assert.True(runner.State == NavRunState.Arrived, runner.FailureReason);
        Assert.Equal(0, runner.Rungs);
        Assert.Equal(shopCell, visited[^1]);
        var at = 0;
        foreach (var cell in planned)
        {
            var next = visited.IndexOf(cell, at);
            Assert.True(next >= 0, $"planned cell 0x{cell:X8} was not entered");
            at = next;
        }
    }

    [RequiresDatsFact]
    public void The_walker_at_the_mages_run_rate_arrives_in_the_cellar_without_a_rung()
    {
        var (world, map) = Holtburg();
        var source = new IndoorOutdoorPathSource(new PathPlanner(map), LandblockCellSet.Load(new CellStructureLibrary(Dats), LandblockCoord.Holtburg));
        var goal = NavGoal.ToPoint(new NavPoint(ThelnothCellarCell & 0xFFFF0000u, source.Rooms.RoomFrame(ThelnothCellarCell)!.Value.Origin), ThelnothCellarCell);

        var (runner, visited, _) = Walk(world, source, Lifestone(), goal, 120, "shop-mage-rate", MageRunSkill);

        Assert.True(runner.State == NavRunState.Arrived, runner.FailureReason);
        Assert.Equal(0, runner.Rungs);
        Assert.Equal(ThelnothCellarCell, visited[^1]);
    }

    /// <summary>
    /// TODO(room-steps)'s evidence: Renald's room 0x141 (behind the counter) is not walkable on foot. The room graph plans 13F, 140, 141; the
    /// physics refuses the way into 0x140 (its floor and doorway sit 1 m above 13F's: no walkable ground within the step window before it,
    /// no room to stand in it), the room graph leaves 0x140 out and plans by 0x142, which the physics refuses too; the run ends Blocked
    /// before a key is pressed (no rung, no metre run). Unconfirmed, the walker sticks in 13F and climbs all twelve rungs (S5, 2026-10-23).
    /// </summary>
    [RequiresDatsFact]
    public void The_room_behind_Renalds_counter_is_refused_by_the_physics_before_a_key_is_pressed()
    {
        var (world, map) = Holtburg();
        var source = new IndoorOutdoorPathSource(new PathPlanner(map), LandblockCellSet.Load(new CellStructureLibrary(Dats), LandblockCoord.Holtburg));
        var goal = NavGoal.ToPoint(new NavPoint(RenaldShopCell & 0xFFFF0000u, source.Rooms.RoomFrame(RenaldShopCell)!.Value.Origin), RenaldShopCell);

        var (runner, _, _) = Walk(world, source, Lifestone(), goal, 60, "shop-A9B40141");

        Assert.Equal(NavRunState.Blocked, runner.State);
        Assert.Equal([0xA9B40140u, 0xA9B40142u], source.AvoidedCells.Order());
        Assert.Equal(0, runner.Rungs);
        Assert.Equal(0f, runner.RouteMetres);
        Assert.Equal(2, runner.ConfirmFailures); // the third plan found no path
        Assert.Contains("no path: no-doorway-lane-reaches-the-goal", runner.FailureReason, StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void The_walker_runs_from_the_lifestone_to_the_belt_over_the_corridor_of_tiles_without_a_rung()
    {
        var start = Lifestone();
        var from = NavMap.FromCellFrame(start).Global;
        var low = (int)(BeltCell & 0xFFFF) - 1;
        var beltLocal = new Vector2(((low / 8) * 24f) + 12f, ((low % 8) * 24f) + 12f);
        var to = new NavPoint(BeltCell & 0xFFFF0000u, new Vector3(beltLocal, 0f)).Global;

        // The corridor: every landblock the straight line crosses and its four neighbours (S6's streamer replaces this).
        var blocks = new HashSet<LandblockCoord>();
        var length = Vector2.Distance(from, to);
        for (var s = 0f; s <= length + 24f; s += 24f)
        {
            var p = NavPoint.FromGlobal(Vector2.Lerp(from, to, MathF.Min(1f, s / length)), 0f);
            var coord = LandblockCoord.FromId(p.LandblockId);
            foreach (var (dx, dy) in new[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                if (coord.Offset(dx, dy) is { } near)
                {
                    blocks.Add(near);
                }
            }
        }

        var builder = new LandblockCollisionBuilder(Dats);
        var world = new PhysicsWorld();
        var map = new NavMap();
        foreach (var coord in blocks)
        {
            var block = builder.Build(coord);
            world.Attach(block);
            map.Attach(NavTileBuilder.Build(block));
        }

        var goalPoint = new NavPoint(BeltCell & 0xFFFF0000u, new Vector3(beltLocal, 0f));
        Assert.True(map.TryGetNode(goalPoint, out var node));
        goalPoint = goalPoint with { Local = goalPoint.Local with { Z = node.Z } };
        output.WriteLine($"{blocks.Count} landblocks in the corridor; straight line {length:F0} m");

        var (runner, visited, _) = Walk(world, new OutdoorPathSource(new PathPlanner(map)), start, NavGoal.ToPoint(goalPoint), 900, "belt");

        Assert.True(runner.State == NavRunState.Arrived, runner.FailureReason);
        Assert.Equal(0, runner.Rungs);
        Assert.Equal(BeltCell, visited[^1]);
        Assert.True(runner.RouteMetres > length * 0.95f);

        // nav-natural-steering: the straight run turns at the route's corners (P5a's pursuit sent 950 MoveToStates on this walk, 7 a second: docs/wayfinding.md).
        Assert.InRange(runner.Turns, 1, 80);
        Assert.InRange(runner.KeyChangesPerMinute, 0, 70);
    }

    [RequiresDatsFact]
    public void The_walker_runs_the_dens_tour_over_the_room_graph_with_lean_0_3_without_a_rung()
    {
        var coord = new LandblockCoord(1, 5);
        var all = LandblockCellSet.Load(new CellStructureLibrary(Dats), coord);
        var graph = GroundRooms.GraphOf(all, DenGround.Profile);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(Dats).Build(coord));
        var h = new ActionHarness(offline: true);
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, null, "den-tour");
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(FighterRunSkill), sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(DenGround.Arrival, sliding: true));
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var source = new RoomPathSource(graph);
        var now = 0.0;
        var legs = new List<string>();
        foreach (var lowWord in DenGround.SpawnLowWords)
        {
            var cellId = 0x01050000u | lowWord;
            var goal = NavGoal.ToPoint(new NavPoint(0x01050000u, graph.RoomFrame(cellId)!.Value.Origin), cellId);
            using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, new NavRunnerOptions { Name = "den-" + lowWord.ToString("X4") });
            runner.Start(goal, now);
            var end = now + 120;
            while (!runner.IsDone && now < end)
            {
                now += 1.0 / Fps;
                walker.Controller.Tick(now);
                runner.Step(now);
                h.Tick(1.0 / Fps);
            }

            legs.Add($"0x{lowWord:X4} {runner.State} {runner.VerdictFields()}");
            output.WriteLine(legs[^1] + (runner.FailureReason is { } why ? " -- " + why : string.Empty));
            Assert.True(runner.State == NavRunState.Arrived, runner.FailureReason);
            Assert.Equal(0, runner.Rungs);
            Assert.Equal(cellId, walker.Position.CellId);
        }

        Assert.Equal(DenGround.SpawnLowWords.Count, legs.Count);
        Assert.Equal(DenGround.SpawnLowWords.Count, audit.Plans - audit.Replans);
        Assert.Equal(0, audit.LadderRungs);
    }

    /// <summary>
    /// M4 4.2 F1 (DoD 8): the death station's corpse trip in the den, run by the explore-mode walker at the mage's run rate with the physics confirming the path:
    /// from where the fresh-r2 den fight ended (0x0105036F, (100.08, -140.87)) to the corpse's object goal at 2.0 m in 0x01050377 at (110.02, -139.07), the goal
    /// built from the corpse's retail position by <see cref="NavMap.FromCellFrame"/> as the leg planner builds it. Live, the normalised goal planned 201 m and the
    /// probe refused segment 6 three times; here the run arrives within the range in a few metres with no refusal and no rung.
    /// </summary>
    [RequiresDatsFact]
    public void The_walker_reaches_the_mages_corpse_in_the_den_from_the_den_fights_end_in_a_short_confirmed_run()
    {
        var coord = new LandblockCoord(1, 5);
        var graph = GroundRooms.GraphOf(LandblockCellSet.Load(new CellStructureLibrary(Dats), coord), DenGround.Profile);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(Dats).Build(coord));
        var start = new CellFrame(0x0105036Fu, new AcFrame(new Vector3(100.08f, -140.87f, 0f), Quaternion.Identity));
        var corpse = new CellFrame(0x01050377u, new AcFrame(new Vector3(110.02f, -139.07f, 0.01f), Quaternion.Identity));
        var goal = NavGoal.ToObject(0x800012C1, NavMap.FromCellFrame(corpse), (float)DeathScenario.CorpseTravelRangeMetres, needSight: false, corpse.CellId);

        var (runner, visited, _) = Walk(world, new RoomPathSource(graph), start, goal, 60, "den-corpse", MageRunSkill);

        Assert.True(runner.State == NavRunState.Arrived, runner.FailureReason);
        Assert.Equal(0, runner.Rungs);
        Assert.Null(runner.LastConfirmFailure);
        Assert.InRange(runner.RouteMetres, 5f, 30f); // live: a 201 m plan that never ran a metre
        Assert.Equal(0x0105u, visited[^1] >> 16);
        output.WriteLine("cells " + string.Join(" ", visited.Select(c => c.ToString("X8", System.Globalization.CultureInfo.InvariantCulture))));
    }

    /// <summary>Holtburg and its eight neighbours' collision, and Holtburg's tile.</summary>
    private (PhysicsWorld World, NavMap Map) Holtburg()
    {
        var builder = new LandblockCollisionBuilder(Dats);
        var world = new PhysicsWorld();
        var map = new NavMap();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var block = builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value);
                world.Attach(block);
                if (dx == 0 && dy == 0)
                {
                    map.Attach(NavTileBuilder.Build(block));
                }
            }
        }

        return (world, map);
    }

    private static CellFrame Lifestone()
    {
        var poi = AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg"); // beside the lifestone
        return new CellFrame(poi.CellId, new AcFrame(poi.Origin, Quaternion.Identity));
    }

    /// <summary>Runs the explore-mode walker from <paramref name="start"/> to <paramref name="goal"/>; the cells it stood in, in order.</summary>
    private (NavRunner Runner, List<uint> Visited, (float Up, float Down) Steps) Walk(PhysicsWorld world, INavPathSource source, CellFrame start, NavGoal goal, double seconds, string name, int runSkill = FighterRunSkill)
    {
        var h = new ActionHarness(offline: true);
        var now = 0.0;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, name);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(runSkill), sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.True(walker.Place(start), $"placed at {start}");
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, new NavRunnerOptions { Name = name });
        runner.Start(goal, now);
        var visited = new List<uint> { walker.Position.CellId };
        var offGround = 0;
        (float Up, float Down) steps = default;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!runner.IsDone && now < seconds)
        {
            now += 1.0 / Fps;
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / Fps);
            if (walker.Position.CellId != visited[^1])
            {
                visited.Add(walker.Position.CellId);
            }

            if (!walker.Controller.OnGround)
            {
                offGround++;
            }

            if (confirmer.Status == RouteConfirmStatus.Confirmed)
            {
                steps = (MathF.Max(steps.Up, confirmer.LargestSteps.Up), MathF.Min(steps.Down, confirmer.LargestSteps.Down));
            }
        }

        output.WriteLine($"{name}: {runner.State} {runner.VerdictFields()} stuck={runner.Stuck}; {runner.Path?.Waypoints.Count} waypoints, confirm {confirmer.GroundSamples} probes {confirmer.Placements} placements ({confirmer.PlacementNudges} nudged) in {confirmer.Milliseconds:F2} ms for {confirmer.ConfirmedMetres:F0} m; keys {runner.KeyChanges} (max {runner.MaxKeysPerSecond}/s), {audit.Summary.MoveToStateSent} MoveToStates; {offGround} frames off the ground; {timer.Elapsed.TotalSeconds:F1} s wall");
        if (runner.FailureReason is { } why)
        {
            output.WriteLine("  " + why);
        }

        if (runner.LastConfirmFailure is { } refused)
        {
            output.WriteLine("  last refused confirmation: " + refused);
        }

        runner.Dispose();
        return (runner, visited, steps);
    }
}

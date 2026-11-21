using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Tests.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// A dungeon is run through, not walked (M5 item 2 fix F6, the owner's D47 rule that he always moves with purpose; live round 2 watched the BSD Mage walk 52 m of the den's first leg with no creature near).
/// The cause, reproduced here: <see cref="NavRunner"/> walked within <c>WalkNearDoorwayMetres</c> of every route vertex marked Doorway, and on a room graph every cell-to-cell portal is a Doorway vertex, so a
/// leg across a few rooms was a walk. The walk exists for the thin doorway cell outdoors (a running step of 0.26 m or more can cross it between two physics steps: docs/physics.md section 15); indoors the body
/// runs through a doorway it can run straight through and walks near one only where the route turns sharply there or the physics' sweep stops short of it. Per ground over every dungeon profile (D46): the
/// walked share of each leg's metres, and the leg's speed, on the ground's own legs from its arrival (Exhaustive tier: the dats; skipped without them).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class IndoorRunThroughDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The Run skill whose rate is the fighters' buffed one, 2.47 (as <c>IndoorSteeringDatTests</c>).</summary>
    private const int FighterRunSkill = 230;

    private const double Fps = 60.0;

    /// <summary>At most this share of any leg's metres is walked (a corner turned sharply at a doorway, the start, the stop within <see cref="NavRunner.WalkNearStopMetres"/> of the goal).</summary>
    public const float MaxWalkedShare = 0.3f;

    /// <summary>At most this share of all the ground's legs' metres is walked.</summary>
    public const float MaxWalkedShareOfAllLegs = 0.15f;

    /// <summary>
    /// The grounds whose run-through check is a named gap, the TODO to remove when the check passes (M6 3 F1, 3-R4). One now, of the ordinary steering alone: <c>m6-redoubt-walk-0158-0150</c>. Closed: <c>m6-burrow-walk</c>, <c>m6-cave-doorway-0113-0114</c>, <c>m6-quarters-walk</c>, <c>m6-abode-walk</c>, <c>m6-holding-walk</c>,
    /// <c>m6-dungeon-run-share</c> (M6 7 F: <c>NavRunner.WalkNearDungeonDoorwayMetres</c>), and, in M6 7 Q, the four whose legs the physics refused: <c>m6-cave-ground-probe-021E</c>, <c>m6-cavern-ground-probe-0209</c>, <c>m6-habitat-ground-probe-011A</c> and <c>m6-alabree-run-through</c>:
    /// the room graph planned through floor-to-ceiling pillars whose origins stand over a human's head and through rocks on a crossing's approach (<see cref="CellRoutePlanner.InStatic"/>, <see cref="AlabreeCaveWalkDatTests"/>).
    /// </summary>
    private static readonly Dictionary<string, string> WalkGapTodo = new(StringComparer.Ordinal)
    {
        // M6 redoubt: with the ordinary steering the leg 0x01630158 to 0x01630150 (20 m, through the door cell 0x0163015A) arrives on its second fallback rung; the predictive steering, the fight walks', runs every leg on its first plan
        ["holtburg-redoubt" + OrdinarySteering] = "m6-redoubt-walk-0158-0150",
    };

    /// <summary>M6 redoubt: the suffix of a <see cref="WalkGapTodo"/> key that names a gap of the ordinary steering alone (a ground's bare key names a gap of both).</summary>
    private const string OrdinarySteering = "@ordinary";

    /// <summary>The most legs a ground's walk is measured over (its first tour cells, from the arrival).</summary>
    private const int MaxLegs = 12;

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared();
        var t = len2 <= 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + (ab * t));
    }

    public static TheoryData<string> Dungeons() => new(GroundCatalog.All.Where(g => g.Kind == GroundKind.Dungeon).Select(g => g.Key));

    /// <summary>Each dungeon ground with the ordinary steering and with the predictive steering (the fight walks' since 2026-11-16).</summary>
    public static TheoryData<string, bool> DungeonsBothSteerings()
    {
        var data = new TheoryData<string, bool>();
        foreach (var key in GroundCatalog.All.Where(g => g.Kind == GroundKind.Dungeon).Select(g => g.Key))
        {
            data.Add(key, false);
            data.Add(key, true);
        }

        return data;
    }

    [DatsGapTheory]
    [MemberData(nameof(DungeonsBothSteerings))]
    public void A_dungeons_legs_are_run_not_walked_through_their_doorways(string key, bool predictive)
    {
        var ground = GroundCatalog.Get(key);
        var dats = fixture.Dats;
        var landblock = Assert.Single(ground.Landblocks);
        var coord = new LandblockCoord((byte)(landblock >> 8), (byte)(landblock & 0xFF));
        var geometry = new PhysicsGeometryLibrary(dats);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, geometry).Build(coord));
        var graph = GroundRooms.GraphOf(LandblockCellSet.Load(new CellStructureLibrary(dats), coord), ground);
        var h = new ActionHarness(offline: true);
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, null, "run-through");
        var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(FighterRunSkill), sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(ground.Arrival.Frame, sliding: true));
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var source = new RoomPathSource(graph);
        var now = 0.0;
        var (metres, walked, seconds) = (0f, 0f, 0.0);
        var failures = new List<string>();
        var legs = 0;
        var worstMiss = 0f;
        var totalWall = 0;
        // M6 14 holding plates: on a ground with plates a leg's goal is the fight's (FightWalk.GoalOutOfAvoid): off a plate's wall where the room's middle stands in one; a cell whose whole floor a plate's wall holds gets no leg
        var plateGoals = ground.Plates is null ? null : new RunnerLegs(ground, new ProfileTour(ground), graph, NullLogSink.Instance);
        foreach (var cellId in ground.Tour.Where(c => graph.RoomFrame(c) is not null).Distinct().Take(MaxLegs))
        {
            if ((plateGoals is null ? graph.RoomFrame(cellId) : plateGoals.GoalOutOfAvoid(cellId, walker.Position.Origin)) is not { } frame)
            {
                output.WriteLine($"0x{cellId & 0xFFFF:X4} no leg: no floor of the cell stands outside its plate's wall");
                continue;
            }

            var goal = NavGoal.ToPoint(new NavPoint(cellId & 0xFFFF0000u, frame.Origin), cellId);
            using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, new NavRunnerOptions { Name = "leg-" + (cellId & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture), PredictiveSteering = predictive });
            runner.Start(goal, now);
            var end = now + 120;
            var wallFrames = 0;
            var track = new List<Vector2> { NavMap.FromCellFrame(walker.Position).Global };
            while (!runner.IsDone && now < end)
            {
                now += 1.0 / Fps;
                walker.Controller.Tick(now);
                runner.Step(now);
                h.Tick(1.0 / Fps);
                track.Add(NavMap.FromCellFrame(walker.Position).Global);
                wallFrames += walker.Physics.CollidingWithEnvironment && runner.RouteMetres > 1f && runner.JumpsTaken == 0 ? 1 : 0; // the placement's latch (31 frames at the first leg's start, before and after F6) is not a wall hit; nor, M6 14 holding plates, a jump's landing on the floor
            }

            // How near the body's track came to each doorway vertex of the leg: running through a doorway must still go through it (the doorway is 2 m wide, the body 0.96 m).
            var miss = runner.Path!.Waypoints.Where(w => w.Doorway).Select(w => w.Point.Global).Select(d => Enumerable.Range(1, track.Count - 1).Min(i => SegmentDistance(d, track[i - 1], track[i]))).DefaultIfEmpty(0f).Max();
            totalWall += wallFrames;
            worstMiss = Math.Max(worstMiss, miss);

            var share = runner.RouteMetres > 0f ? runner.WalkedMetres / runner.RouteMetres : 0f;
            var speed = runner.RunSeconds > 0 ? runner.RouteMetres / runner.RunSeconds : 0.0;
            var line = string.Create(CultureInfo.InvariantCulture, $"0x{cellId & 0xFFFF:X4} {runner.State} walked_m={runner.WalkedMetres:0.0} of {runner.RouteMetres:0.0} m (share {share:0.00}) speed={speed:0.00} m/s turns={runner.Turns} doorway_miss_m={miss:0.00} wall_frames={wallFrames} {runner.VerdictFields()}");
            output.WriteLine(line + (runner.FailureReason is { } why ? " -- " + why : string.Empty));
            legs++;
            metres += runner.RouteMetres;
            walked += runner.WalkedMetres;
            seconds += runner.RunSeconds;
            // In the goal's cell, or within the stop's tolerance of a goal on a sliver of a cell (the Cave of Alabree's 0x0162015F, 3.1 m of floor beside its room: the stop comes before the seam, which is an arrival).
            var inGoalCell = walker.Position.CellId == cellId || Vector2.Distance(NavMap.FromCellFrame(walker.Position).Global, goal.Point.Global) <= goal.StopTolerance;
            // M6 14 holding plates: a leg over a pressure plate's standing jump (the Tusker Holding's row in 0x5B480373) replans from its landing and stood to jump: it is judged on its arrival and its landings (HoldingPlateJumpDatTests proves the jumps)
            var jumpLeg = runner.JumpsTaken > 0;
            if (runner.State != NavRunState.Arrived || runner.Rungs != 0 || (!jumpLeg && runner.Replans != 0) || runner.JumpsLanded != runner.JumpsTaken || !inGoalCell || (!jumpLeg && runner.RouteMetres > 8f && share > MaxWalkedShare))
            {
                failures.Add(line);
            }
        }

        var all = metres > 0f ? walked / metres : 0f;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{key}: {legs} legs, {walked:0.0} of {metres:0.0} m walked (share {all:0.000}) in {seconds:0.0} s, {(seconds > 0 ? metres / seconds : 0):0.00} m/s, the track passed every doorway vertex within {worstMiss:0.00} m, {totalWall} frames of wall contact once under way"));
        // M6 3 F1 (3-R4): a ground with a named walk gap SKIPS (reported as skipped, with the TODO and the measured numbers) while the check fails, and FAILS "stale skip" once the check passes, so a walk fix removes the skip; a ground with no named gap fails as before.
        var passes = legs > 0 && failures.Count == 0 && totalWall == 0 && all <= MaxWalkedShareOfAllLegs;
        if (WalkGapTodo.TryGetValue(key, out var todo) || (!predictive && WalkGapTodo.TryGetValue(key + OrdinarySteering, out todo)))
        {
            Assert.False(passes, $"stale skip: remove TODO({todo}) ({key}'s run-through check passes: {legs} legs, share {all:0.000}, {totalWall} wall frames)");
            DynamicSkipException.Skip(string.Create(CultureInfo.InvariantCulture, $"TODO({todo}): {key}: {failures.Count} of {legs} legs failed or walked too much, {walked:0.0} of {metres:0.0} m walked (share {all:0.000}), {totalWall} wall frames; this check waits for the walk fix"));
        }

        Assert.True(legs > 0, key + ": no leg measured");
        Assert.Empty(failures);
        Assert.True(totalWall == 0, key + ": the body was in contact with the environment for " + totalWall + " frames of its legs (running through a doorway must not clip its jambs)");
        Assert.True(all <= MaxWalkedShareOfAllLegs, string.Create(CultureInfo.InvariantCulture, $"{key}: {all:0.000} of the legs' metres were walked"));
    }
}

/// <summary>
/// The doorway walk's two sides (m6-dungeon-run-share; docs/wayfinding.md "The runner"): in a dungeon the body walks only the last <see cref="NavRunner.WalkNearDungeonDoorwayMetres"/> before a doorway it turns at; rooms with a way
/// outside are a building's and keep the full <see cref="NavRunner.WalkNearDoorwayMetres"/> (the door stops and sills that passed live).
/// </summary>
public sealed class IndoorDoorwayWalkTests(ITestOutputHelper output)
{
    [Fact]
    public void In_a_dungeon_a_sharp_doorway_costs_half_a_metre_of_walk_not_six()
    {
        var rig = new IndoorRig(IndoorRoomKit.SharpTurn(), 0f, IndoorRoomKit.SharpStart, new Vector2(96f, 88f), heading: 0f);

        var report = rig.Run(IndoorRoomKit.SharpStart + 2, new Vector3(82f, 94.5f, 0f), 40);
        output.WriteLine(report);

        Assert.True(rig.Runner.State == NavRunState.Arrived, rig.Runner.FailureReason);
        Assert.True(rig.Runner.DungeonRooms);
        Assert.Equal(0, rig.Runner.Rungs);
        Assert.Equal(0, rig.Runner.Replans);
        Assert.Equal(0, rig.FramesOutside); // lined up before it runs: the second doorway's turn does not swing it through the room's far wall
        Assert.True(rig.Runner.WalkedMetres <= NavRunner.WalkNearStopMetres + (2f * NavRunner.WalkNearDungeonDoorwayMetres), report); // the stop's 2 m and the last steps before a turn
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Rooms_with_a_way_outside_are_a_building_not_a_dungeon(bool withOutside, bool dungeon)
    {
        var rig = new IndoorRig(S3RoomKit.Rooms(withOutside: withOutside), 0f, S3RoomKit.A, new Vector2(96f, 92f), heading: 0f);

        var report = rig.Run(S3RoomKit.C, new Vector3(96f, 119f, 0f), 30);
        output.WriteLine(report);

        Assert.True(rig.Runner.State == NavRunState.Arrived, rig.Runner.FailureReason);
        Assert.Equal(dungeon, rig.Runner.DungeonRooms);
    }
}

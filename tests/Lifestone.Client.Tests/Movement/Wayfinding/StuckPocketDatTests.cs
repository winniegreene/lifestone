using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session;
using Lifestone.Client.Tests.Session.Grounds;
using Lifestone.Data.Animation;
using Lifestone.Client.World;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// M5 7 P7i (the rehearsal of 2026-10-30): after P7b's give-up the next leg logged its plan and then sent no turn and no forward for 6 s until its first stuck rung, three legs in a row at 0x01050318. The cause was no stale state of
/// the give-up: a leg begun inside an indoor corner's approach (the last <see cref="NavRunner.CornerApproachMetres"/> to it, where D134 presses no new turn key) with the aim past <see cref="NavRunner.TurnInPlaceDegrees"/> held neither
/// key (no forward past that error, no press in the approach) and stood. Now such a body turns in place (<see cref="NavRunner.StandTurns"/>, logged). Synthetic rooms with the scripted body, through the walk stack's own
/// <see cref="RunnerLegs"/>; the live spots on the dats; and the planner's costly patches (<see cref="RoomPatches"/>).
/// </summary>
public sealed class WalkAfterGiveUpTests(ITestOutputHelper output)
{
    private static readonly Vector3 CorridorEndCentre = new(117f, 99f, 0f);

    [Fact]
    public void A_leg_begun_after_four_stuck_rungs_and_the_give_up_with_its_way_behind_the_body_turns_and_runs_within_a_second()
    {
        // The corridor runs north from the start to its bend (96, 99), then east. The body stands 4 m short of the bend, inside the corner's approach.
        var rig = new IndoorRig(IndoorRoomKit.CorridorWithABend(), 0f, IndoorRoomKit.CorridorStart, new Vector2(94f, 95f), heading: 0f);
        var log = new RecordingLogSink(LogSeverity.Info);
        var legs = new RunnerLegs(DenGround.Profile with { Walk = new WalkModel(0f, 1f, 0.0, 600) }, new OneCellTour(IndoorRoomKit.CorridorEnd), rig.Graph, log, CorridorFrame);
        rig.Body.Frozen = true; // something holds the body: the ladder climbs
        Assert.True(legs.Begin(rig.Runner, rig.Indoor.Position, rig.Body.Now, rig.Body.Now, out _), legs.Failure);
        for (var i = 0; i < 6000 && legs.StuckRungs < FightScenario.WalkStuckRungsGiveUp; i++)
        {
            Frame(rig, legs);
        }

        Assert.Equal(FightScenario.WalkStuckRungsGiveUp, legs.StuckRungs);
        legs.Abandon(); // the fight's give-up (FightScenario.WalkStuckGivesUp)
        rig.H.Tick(S5Rig.Dt); // the releases go out
        Assert.Equal(0, rig.Body.TotalDepth); // every key released

        // The new leg: the body freed, facing back the way it came (the way on lies behind it).
        rig.Body.Frozen = false;
        rig.Body.Heading = 180f;
        var turns = rig.Runner.Turns;
        var standTurns = rig.Runner.StandTurns;
        var calls = rig.Body.Calls.Count;
        Assert.True(legs.Begin(rig.Runner, rig.Indoor.Position, rig.Body.Now, rig.Body.Now, out _), legs.Failure);
        var start = rig.Body.Xy;
        double? turnAt = null, forwardAt = null;
        var t0 = rig.Body.Now;
        while (rig.Body.Now - t0 < 2.5)
        {
            Frame(rig, legs);
            turnAt ??= rig.Runner.Turns > turns ? rig.Body.Now - t0 : null;
            forwardAt ??= rig.Body.Holds(MotionCommands.WalkForward) ? rig.Body.Now - t0 : null;
        }

        output.WriteLine(string.Join(" | ", rig.Body.Calls.Skip(calls)));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"turn at {turnAt:0.00} s, forward at {forwardAt:0.00} s, heading {rig.Body.Heading:0}, moved {Vector2.Distance(start, rig.Body.Xy):0.0} m"));
        Assert.NotNull(turnAt);
        Assert.True(turnAt <= 0.25, $"the first turn key went down {turnAt} s into the leg");
        Assert.NotNull(forwardAt);
        Assert.True(forwardAt <= 1.5, $"forward went down {forwardAt} s into the leg");
        Assert.True(Math.Abs(rig.Body.Heading - 180f) > 30f, "the heading did not change");
        Assert.True(Vector2.Distance(start, rig.Body.Xy) > 2f, "the body did not move off the spot");
        Assert.Equal(standTurns + 1, rig.Runner.StandTurns); // the leg's first press is the turn in place
    }

    [Fact]
    public void A_costly_patch_sends_the_room_graphs_plan_round_it_while_another_way_exists_and_expires()
    {
        // The ring: from the south-west room to the north-east one there are two ways, through the north-west or the south-east room.
        var graph = new RoomGraph(IndoorRoomKit.Ring());
        var from = new Vector3(96f, 96f, 0f);
        var to = new Vector3(106f, 106f, 0f);
        var plain = graph.Plan(IndoorRoomKit.RingSouthWest, from, IndoorRoomKit.RingNorthEast, to)!;
        var via = plain.Cells[1];
        var other = via == IndoorRoomKit.RingNorthWest ? IndoorRoomKit.RingSouthEast : IndoorRoomKit.RingNorthWest;
        var centre = via == IndoorRoomKit.RingNorthWest ? new Vector3(96f, 101f, 0f) : new Vector3(101f, 96f, 0f); // the doorway into the room the plain plan goes through

        graph.Patches.Add(centre, 3f, until: 600);
        var round = graph.Plan(IndoorRoomKit.RingSouthWest, from, IndoorRoomKit.RingNorthEast, to)!;
        Assert.Equal(other, round.Cells[1]);

        // The only way to the patched room still plans (a patch is a cost, never a wall).
        Assert.NotNull(graph.Plan(IndoorRoomKit.RingSouthWest, from, via, centre + new Vector3(0f, via == IndoorRoomKit.RingNorthWest ? 5f : 0f, 0f) + new Vector3(via == IndoorRoomKit.RingNorthWest ? 0f : 5f, 0f, 0f)));

        Assert.Equal(0, graph.Patches.Expire(599));
        Assert.Equal(1, graph.Patches.Expire(600));
        Assert.Equal(via, graph.Plan(IndoorRoomKit.RingSouthWest, from, IndoorRoomKit.RingNorthEast, to)!.Cells[1]);
    }

    [Fact]
    public void The_walks_stuck_spot_patch_is_logged_and_expires_at_the_next_leg_after_its_time()
    {
        var rig = new IndoorRig(IndoorRoomKit.CorridorWithABend(), 0f, IndoorRoomKit.CorridorStart, new Vector2(96f, 88f), heading: 0f);
        var log = new RecordingLogSink(LogSeverity.Info);
        var legs = new RunnerLegs(DenGround.Profile with { Walk = new WalkModel(0f, 1f, 0.0, 600) }, new OneCellTour(IndoorRoomKit.CorridorEnd), rig.Graph, log, CorridorFrame);

        Assert.True(legs.AvoidSpot(rig.Indoor.Position, 600, out var patched));
        Assert.Contains("costs the room graph's plans 40 m more until 600 (1 patch(es) in force)", patched, StringComparison.Ordinal);
        Assert.Single(log.Lines, l => l.Contains("walk: stuck spot patched: the floor within 3 m of 0x40400110 [96 88 0]", StringComparison.Ordinal));
        Assert.True(legs.Begin(rig.Runner, rig.Indoor.Position, rig.Body.Now, clock: 100, out _));
        Assert.Equal(1, rig.Graph.Patches.Count);
        legs.Abandon();
        Assert.True(legs.Begin(rig.Runner, rig.Indoor.Position, rig.Body.Now, clock: 601, out _));
        Assert.Equal(0, rig.Graph.Patches.Count);
        Assert.Single(log.Lines, l => l.Contains("walk: 1 stuck spot patch(es) expired (0 in force)", StringComparison.Ordinal));
    }

    private static CellFrame? CorridorFrame(uint cell) => cell == IndoorRoomKit.CorridorEnd ? new CellFrame(cell, new AcFrame(CorridorEndCentre, Quaternion.Identity)) : null;

    private static void Frame(IndoorRig rig, RunnerLegs legs)
    {
        rig.Body.Advance(S5Rig.Dt);
        rig.Indoor.Sync();
        legs.Step(rig.Indoor.Position, rig.Body.Now, rig.Body.Now);
        rig.H.Tick(S5Rig.Dt);
    }

    /// <summary>A tour that always offers one cell.</summary>
    private sealed class OneCellTour(uint cell) : IGroundTour
    {
        public int Waypoints => 1;

        public uint? Next(uint from, double now) => from == cell ? null : cell;
    }
}

/// <summary>
/// M5 7 P7i on the dats (Exhaustive tier, skipped without them): the rehearsal's two spots in the Black Spawn Den, the mage's body placed and facing as the live log has it (0x01050318 at (72.2, -97.6) toward 0x010502A0, and
/// 0x010502FE at (59.4, -91.3) toward 0x01050263, heading w=0.6354 z=-0.7722), the leg to the live goal on the den's room graph with the physics confirming: before the fix neither sent a key for 3 s; now the first turn goes down
/// within a quarter second, the heading changes and the body leaves the spot.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class StuckPocketDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [RequiresDatsTheory]
    [InlineData(0x01050318u, 72.227f, -97.614f, 0x010502A0u)]
    [InlineData(0x010502FEu, 59.42f, -91.342f, 0x01050263u)]
    public void The_rehearsals_stuck_spots_turn_and_leave_at_once(uint cell, float x, float y, uint goalCell)
    {
        var dats = fixture.Dats;
        var coord = new LandblockCoord(1, 5);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, new PhysicsGeometryLibrary(dats)).Build(coord));
        var graph = GroundRooms.GraphOf(LandblockCellSet.Load(new CellStructureLibrary(dats), coord), DenGround.Profile);
        var h = new ActionHarness(offline: true);
        var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(238 + 35));
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        var start = new CellFrame(cell, new AcFrame(new Vector3(x, y, 0.005f), new Quaternion(0f, 0f, -0.7722f, 0.6354f)));
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(start, sliding: true));
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var log = new RecordingLogSink(LogSeverity.Info);
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new RoomPathSource(graph), confirmer, null, new NavRunnerOptions { Name = "pocket" }, log);
        runner.Start(NavGoal.ToPoint(new NavPoint(0x01050000u, graph.RoomFrame(goalCell)!.Value.Origin), goalCell), 0.0);
        var heading = RetailFrames.GetHeading(walker.Position.Frame);
        var now = 0.0;
        double? turnAt = null;
        while (now < 3.0 && !runner.IsDone)
        {
            now += 1.0 / 60;
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / 60);
            turnAt ??= runner.Turns > 0 ? now : null;
        }

        var moved = walker.Position.ToGlobal().DistanceTo(start.ToGlobal());
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"turn at {turnAt:0.00} s; heading {heading:0} -> {RetailFrames.GetHeading(walker.Position.Frame):0}; moved {moved:0.0} m; {runner.VerdictFields()}"));
        Assert.NotNull(turnAt);
        Assert.True(turnAt <= 0.25, $"the first turn went down {turnAt} s into the leg");
        Assert.True(Math.Abs(RetailFrames.GetHeading(walker.Position.Frame) - heading) > 30.0);
        Assert.True(moved > 1.5, $"the body moved {moved:0.0} m in 3 s");
        Assert.Equal(0, runner.Rungs);
        Assert.Single(log.Lines, l => l.Contains("a turn in place: the aim is behind the body and no key would press (P7i)", StringComparison.Ordinal) && l.Contains("why=corner-approach", StringComparison.Ordinal));
    }
}

/// <summary>
/// M5 7 P7i (D144): the stuck ladder's recovery moves are bounded and kept out of avoid regions. On the rehearsal of 2026-10-30 the jump rung held forward until the body landed or 3 s passed and carried the mage about 15 m from
/// 0x010502FE into the pocket at 0x01050318. The scripted body never leaves the ground, as the live body's landing went unseen, so its jump rung runs to the time limit.
/// </summary>
public sealed class RungMoveBoundTests(ITestOutputHelper output)
{
    private static readonly Vector2 Start = new(96f, 60f);

    [Fact]
    public void The_jump_rungs_move_ends_at_its_bound_not_at_the_time_limit()
    {
        var rig = new IndoorRig(IndoorRoomKit.LongHall(), 0f, IndoorRoomKit.Hall, Start, heading: 0f);
        var moved = JumpRung(rig, out _); // a body that runs free during the charge ends the rung before the jump: it was not stuck

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"the jump rung's move carried the body {moved:0.0} m; calls: {string.Join(" | ", rig.Body.Calls)}"));
        Assert.True(moved <= NavRunner.RungMoveMaxMetres + 0.5f, string.Create(CultureInfo.InvariantCulture, $"the rung's move carried the body {moved:0.0} m (bound {NavRunner.RungMoveMaxMetres} m)")); // a frame's step past the bound at most
    }

    [Fact]
    public void A_rungs_move_that_would_end_in_an_avoid_region_moves_nothing()
    {
        var rig = new IndoorRig(IndoorRoomKit.LongHall(), 0f, IndoorRoomKit.Hall, Start, heading: 0f);
        rig.Graph.Patches.AddBox(new Vector3(93f, 62f, -1f), new Vector3(99f, 70f, 1f), "a test pocket"); // 2 to 10 m ahead of the body
        var moved = JumpRung(rig, out var jumped);

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"moved {moved:0.0} m; calls: {string.Join(" | ", rig.Body.Calls)}"));
        Assert.False(jumped); // the jump ahead would have ended in the box
        Assert.Equal(0f, moved);
    }

    /// <summary>Holds the body until the ladder's third rung (the jump) begins, frees it, and returns the farthest the rung's move carried it while the rung lasted; whether a jump went.</summary>
    private static float JumpRung(IndoorRig rig, out bool jumped)
    {
        rig.Runner.Start(NavGoal.ToPoint(new NavPoint(IndoorRoomKit.Block.LandBlockId, new Vector3(96f, 99f, 0f)), IndoorRoomKit.Hall + 1), rig.Body.Now);
        rig.Body.Frozen = true;
        for (var i = 0; i < 6000 && rig.Runner.Rungs < 3; i++)
        {
            rig.Frame();
        }

        Assert.Equal(3, rig.Runner.Rungs);
        rig.Body.Frozen = false;
        var from = rig.Body.Xy;
        var farthest = 0f;
        for (var i = 0; i < 600 && rig.Runner.RungActive; i++)
        {
            rig.Frame();
            farthest = MathF.Max(farthest, Vector2.Distance(from, rig.Body.Xy));
        }

        jumped = rig.Body.Calls.Contains("jump");
        return farthest;
    }
}

/// <summary>M5 7 P7i (D144, the owner): the den profile's seeded avoid region (the rehearsal's pocket) is read and seeded into the walk's room graph once.</summary>
public sealed class SeededAvoidRegionTests
{
    [Fact]
    public void The_dens_pocket_is_a_seeded_avoid_region_of_the_profile_and_the_walk_seeds_it_once()
    {
        var region = Assert.Single(DenGround.Profile.AvoidRegions);
        Assert.Equal((ushort)0x0105, region.Landblock);
        Assert.Equal("the 0x02F9-0x0318 pocket", region.Name);

        var rig = new IndoorRig(IndoorRoomKit.CorridorWithABend(), 0f, IndoorRoomKit.CorridorStart, new Vector2(96f, 88f), heading: 0f);
        var log = new RecordingLogSink(LogSeverity.Info);
        var den = new RoomGraph(rig.Graph.Cells); // the kit's cells stand in for the den's landblock below
        Assert.NotEqual(0x01050000u, den.LandblockId);
        _ = new RunnerLegs(DenGround.Profile, new NoTour(), den, log);
        Assert.Equal(0, den.Patches.Count); // another landblock: nothing seeded

        var graph = new RoomGraph(LandblockCellSet.FromCells(rig.Graph.Cells.Structures, new LandblockCoord(1, 5), []));
        _ = new RunnerLegs(DenGround.Profile, new NoTour(), graph, log);
        _ = new RunnerLegs(DenGround.Profile, new NoTour(), graph, log); // a second walk over the same graph
        Assert.Equal(1, graph.Patches.Count);
        Assert.NotNull(graph.Patches.Contains(new Vector3(72.227f, -97.614f, 0.005f))); // the rehearsal's pocket
        Assert.NotNull(graph.Patches.Contains(new Vector3(62f, -80f, 0f)));
        Assert.Null(graph.Patches.Contains(new Vector3(58.4f, -90.8f, 0f))); // where the rung's jump began
        Assert.Single(log.Lines, l => l.Contains("walk: avoid region seeded: the 0x02F9-0x0318 pocket [61..73 -100..-77] in 0x0105", StringComparison.Ordinal));
    }

    private sealed class NoTour : IGroundTour
    {
        public int Waypoints => 0;

        public uint? Next(uint from, double now) => null;
    }
}

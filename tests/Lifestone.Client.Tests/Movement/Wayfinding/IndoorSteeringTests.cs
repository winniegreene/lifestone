using System.Globalization;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Tests.Session.Grounds;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
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
/// The straight run indoors (nav-indoor-steering, docs/wayfinding.md "The runner"; M5 DoD 5): over a room path the body aims at the farthest route vertex
/// it may run straight to and turns with the outdoor run's rules, so a leg presses a turn key at most once per 4 m of route
/// (<see cref="NavRunner.Turns"/> over <see cref="NavRunner.RouteMetres"/>), passes every doorway vertex within its tolerance and never leaves the
/// route's rooms. On synthetic rooms (a corridor with a bend; plazas joined by doorways leaning as the den's, planned at lean 0.3) with the scripted body.
/// The den's dat legs are <see cref="IndoorSteeringDatTests"/>.
/// </summary>
public sealed class IndoorSteeringTests(ITestOutputHelper output)
{
    /// <summary>The DoD's rate: at most one turn key a 4 m of route.</summary>
    public const float MaxTurnsPerMetre = 0.25f;

    /// <summary>The most of a doorway-crossing leg walked (the start, the stop within 2 m of the goal, the bend): F6.</summary>
    public const float MaxWalkedShare = 0.25f;

    [Fact]
    public void A_corridor_with_a_bend_is_run_straight_between_its_corners()
    {
        var cells = IndoorRoomKit.CorridorWithABend();
        var rig = new IndoorRig(cells, 0f, IndoorRoomKit.CorridorStart, new Vector2(96f, 86f), heading: 0f);
        rig.Body.TurnRate = S5KinematicBody.RunningTurnDegreesPerSecond; // through its doorways at a run now (F6): the turn of a running player, not the walk's 90

        var report = rig.Run(IndoorRoomKit.CorridorEnd, new Vector3(119f, 98f, 0f), 30);
        output.WriteLine(report);

        Assert.True(rig.Runner.State == NavRunState.Arrived, rig.Runner.FailureReason);
        Assert.Equal(0, rig.Runner.Rungs);
        Assert.Equal(0, rig.FramesOutside);
        Assert.True(rig.Runner.Turns <= rig.Runner.RouteMetres * MaxTurnsPerMetre, report);
        Assert.True(rig.DoorwayMissMetres <= CellRoutePlanner.PushIntoCellMetres + 0.1f, report);
    }

    [Fact]
    public void Plazas_joined_by_leaning_doorways_planned_at_lean_0_3_are_run_straight()
    {
        var cells = S3RoomKit.Rooms(leaning: true, withOutside: false);
        var rig = new IndoorRig(cells, 0.3f, S3RoomKit.A, new Vector2(93f, 92f), heading: 0f);

        var report = rig.Run(S3RoomKit.C, new Vector3(99f, 119f, 0f), 30);
        output.WriteLine(report);

        Assert.True(rig.Runner.State == NavRunState.Arrived, rig.Runner.FailureReason);
        Assert.Equal(0, rig.Runner.Rungs);
        Assert.Equal(0, rig.FramesOutside);
        Assert.True(rig.Runner.Turns <= rig.Runner.RouteMetres * MaxTurnsPerMetre, report);
        Assert.True(rig.DoorwayMissMetres <= CellRoutePlanner.PushIntoCellMetres + 0.1f, report);
    }

    /// <summary>M5 2 F6 (the owner's D47): a room graph's doorways are run through; the thin-doorway walk is left to the building's door (docs/wayfinding.md "The runner").</summary>
    [Fact]
    public void Doorways_between_rooms_are_run_through_not_walked_near()
    {
        var cells = IndoorRoomKit.CorridorWithABend();
        var rig = new IndoorRig(cells, 0f, IndoorRoomKit.CorridorStart, new Vector2(96f, 86f), heading: 0f);

        var report = rig.Run(IndoorRoomKit.CorridorEnd, new Vector3(119f, 98f, 0f), 30);
        output.WriteLine(report);

        Assert.True(rig.Runner.State == NavRunState.Arrived, rig.Runner.FailureReason);
        Assert.Contains(rig.Runner.Path!.Waypoints, w => w.Doorway); // the graph has its doorway vertices: the old rule walked within 3 m of each
        var walked = rig.Runner.WalkedMetres / rig.Runner.RouteMetres;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"walked {rig.Runner.WalkedMetres:0.0} of {rig.Runner.RouteMetres:0.0} m"));
        Assert.True(walked <= MaxWalkedShare, string.Create(CultureInfo.InvariantCulture, $"{walked:0.00} of the corridor's metres were walked: {report}"));
    }

    /// <summary>Where the route turns sharply at a doorway vertex (a room 20 m wide and 3 m deep, entered by its south door and left by its west door: a turn of 84 degrees at the first vertex), the body walks near it.</summary>
    [Fact]
    public void A_sharp_turn_at_a_doorway_is_walked_and_the_doorway_after_it_is_run()
    {
        var rig = new IndoorRig(IndoorRoomKit.SharpTurn(), 0f, IndoorRoomKit.SharpStart, new Vector2(96f, 88f), heading: 0f);

        var report = rig.Run(IndoorRoomKit.SharpStart + 2, new Vector3(82f, 94.5f, 0f), 40);
        output.WriteLine(report);

        Assert.True(rig.Runner.State == NavRunState.Arrived, rig.Runner.FailureReason);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"walked {rig.Runner.WalkedMetres:0.0} of {rig.Runner.RouteMetres:0.0} m"));
        Assert.InRange(rig.Runner.WalkedMetres, 3f, 12f); // the walk near the doorway before the turn (and the 2 m before the stop), not the whole 28 m
    }

    [Fact]
    public void A_warm_indoor_runner_allocates_nothing_a_step()
    {
        var rig = new IndoorRig(IndoorRoomKit.LongHall(), 0f, IndoorRoomKit.Hall, new Vector2(96f, 58f), heading: 0f);
        rig.Runner.Start(NavGoal.ToPoint(new NavPoint(IndoorRoomKit.Block.LandBlockId, new Vector3(96f, 99f, 0f)), IndoorRoomKit.Hall + 1), rig.Body.Now);
        rig.RunFor(0.5); // the plan, the pools grown
        var keys = rig.Runner.KeyChanges;

        long allocated = 0;
        for (var i = 0; i < 30; i++)
        {
            rig.Body.Advance(S5Rig.Dt);
            rig.Indoor.Sync();
            long before = GC.GetAllocatedBytesForCurrentThread();
            rig.Runner.Step(rig.Body.Now);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            rig.H.Tick(S5Rig.Dt);
        }

        Assert.Equal(NavRunState.Running, rig.Runner.State);
        Assert.Equal(keys, rig.Runner.KeyChanges); // straight: no key changed
        Assert.Equal(0, allocated);
    }
}

/// <summary>
/// The Black Spawn Den's dat legs (Exhaustive tier: it reads the dats and skips without them): landblock 0x0105 loaded as <c>GroundWalk.Create</c> loads it,
/// the room graph at <see cref="DenGround.Profile.Walk.DoorwayLean"/>, the explore-mode <see cref="OfflineWalker"/> as the body with the physics confirming, the legs
/// between the cells of <see cref="DenGround.SpawnLowWords"/>: every leg arrives without a rung and presses a turn key at most once per 4 m of route.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class IndoorSteeringDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The Run skill whose rate is the fighters' buffed one, 2.47 (as <c>NavRunnerDatTests</c>).</summary>
    private const int FighterRunSkill = 230;

    private const double Fps = 60.0;

    /// <summary>The BSD Mage's buffed Run: base 238 (121 ranks, the soak's "Your base Run skill is now 238!") plus Quickness Self VI's 35 (Run is Quickness alone).</summary>
    private const int MageRunSkill = 238 + 35;

    /// <summary>
    /// D134's gate (the owner: a swinging camera is a stop), set from S5's baseline of the walker before the fix so a halving passes: the reversals (the other side
    /// pressed within <see cref="NavRunner.ReversalCountSeconds"/> of a release) and the flips (within <see cref="NavRunner.FlipCountSeconds"/>: the aim jumping)
    /// a metre of the den walk's route. Baseline, the fighter's Run 230: 137 reversals and 15 flips over 1,736.7 m (0.0789 and 0.0086 a metre); the mage's
    /// Run 273: 139 and 27 over 1,743.0 m (0.0797 and 0.0155); no corner turned in place then. The reversals gated leave out
    /// <see cref="NavRunner.CornerReversals"/> (a turn in place at one corner, then the other way at the next: the den's routes step round its grid, every corner a
    /// deliberate turn); the raw count is written beside them.
    /// </summary>
    public const float MaxReversalsPerMetre = 0.039f;

    /// <summary>
    /// D134's den walk (the owner's observation that he swings in direction when hitting junctions): the twelve spawn cells walked in order, back again and in a stride of five
    /// (36 legs, each starting where and facing as the last ended, as the fight's walks do), at the fighter's Run and the mage's. Every leg arrives in its cell
    /// without a rung; the reversals and the flips a metre stay under <see cref="MaxReversalsPerMetre"/> and the run skill's halved flips. The per-leg and total
    /// counters are written (turns, reversals, flips, in-place presses, forward re-presses, progress backs, swings, rungs).
    /// </summary>
    [RequiresDatsTheory]
    [InlineData(FighterRunSkill, 0.0043f)]
    [InlineData(MageRunSkill, 0.0077f)]
    public void The_dens_walk_turns_without_swinging(int runSkill, float maxFlipsPerMetre)
    {
        var tally = WalkTheDen(runSkill, DenTour(), out var failures);
        var reversalsPerMetre = (tally.Reversals - tally.CornerReversals) / tally.Metres;
        var flipsPerMetre = tally.Flips / tally.Metres;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"gate: reversals outside corner pairs {reversalsPerMetre:0.0000}/m (max {MaxReversalsPerMetre:0.0000}; raw {tally.Reversals / tally.Metres:0.0000}/m), flips {flipsPerMetre:0.0000}/m (max {maxFlipsPerMetre:0.0000})"));
        Assert.Empty(failures);
        Assert.Equal(tally.Swings, tally.SwingLines.Count); // D134 step 4: each swing logged once, with where it happened
        Assert.All(tally.SwingLines, l => Assert.Matches(@"^INFO  \[wayfinding\] swing: .* name=den-\d+-[0-9A-F]{4} at=""0x0105[0-9A-F]{4} \[[-\d.]+ [-\d.]+ [-\d.]+\]"" heading=\d+ progress_m=[\d.]+ route_m=[\d.]+ next=\d+ aim=\d+ aim_at=""0x0105[0-9A-F]{4} \[[-\d.]+ [-\d.]+ [-\d.]+\]"" corner=(none|approach|turning) reversals=\d+ flips=\d+$", l));
        Assert.True(reversalsPerMetre <= MaxReversalsPerMetre, tally.ToString());
        Assert.True(flipsPerMetre <= maxFlipsPerMetre, tally.ToString());
    }

    /// <summary>The den walk's legs: the spawn cells in order, reversed, then in a stride of five.</summary>
    private static List<ushort> DenTour()
    {
        var cells = DenGround.SpawnLowWords;
        var tour = new List<ushort>(cells);
        for (var i = cells.Count - 2; i >= 0; i--)
        {
            tour.Add(cells[i]);
        }

        for (var i = 1; i <= cells.Count; i++)
        {
            tour.Add(cells[(i * 5) % cells.Count]);
        }

        return tour;
    }

    /// <summary>The den walk's totals.</summary>
    private sealed record DenTally(int Legs, float Metres, double Seconds, int Turns, int Reversals, int Flips, int InPlace, int ForwardRepresses, int ProgressBacks, int Swings, int Rungs, int CornerTurns = 0, int CornerReversals = 0)
    {
        public List<string> SwingLines { get; } = [];

        public override string ToString() => string.Create(
            CultureInfo.InvariantCulture,
            $"{Legs} legs, {Metres:0.0} m, {Seconds:0.0} s: turns={Turns} turn_reversals={Reversals} turn_flips={Flips} in_place={InPlace} fwd_repress={ForwardRepresses} progress_backs={ProgressBacks} swings={Swings} corner_turns={CornerTurns} corner_reversals={CornerReversals} rungs={Rungs}; a minute: turns {Turns * 60 / Seconds:0.0} reversals {Reversals * 60 / Seconds:0.0} flips {Flips * 60 / Seconds:0.0}");
    }

    /// <summary>Walks <paramref name="tour"/> (spawn-cell low words) from the den's arrival at <paramref name="runSkill"/>, writing each leg's line and the totals.</summary>
    private DenTally WalkTheDen(int runSkill, IReadOnlyList<ushort> tour, out List<string> failures)
    {
        var dats = fixture.Dats;
        var coord = new LandblockCoord(1, 5);
        var geometry = new PhysicsGeometryLibrary(dats);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, geometry).Build(coord));
        var graph = GroundRooms.GraphOf(LandblockCellSet.Load(new CellStructureLibrary(dats), coord), DenGround.Profile);
        var h = new ActionHarness(offline: true);
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, null, "den-steering");
        var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(runSkill), sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(DenGround.Arrival, sliding: true));
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var source = new RoomPathSource(graph);
        var now = 0.0;
        var t = new DenTally(0, 0f, 0.0, 0, 0, 0, 0, 0, 0, 0, 0);
        failures = [];
        var leg = 0;
        var log = new RecordingLogSink(LogSeverity.Info);
        foreach (var lowWord in tour)
        {
            leg++;
            var cellId = 0x01050000u | lowWord;
            if (walker.Position.CellId == cellId)
            {
                continue;
            }

            var goal = NavGoal.ToPoint(new NavPoint(0x01050000u, graph.RoomFrame(cellId)!.Value.Origin), cellId);
            using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, new NavRunnerOptions { Name = string.Create(CultureInfo.InvariantCulture, $"den-{leg}-{lowWord:X4}") }, log);
            runner.Start(goal, now);
            var end = now + 120;
            while (!runner.IsDone && now < end)
            {
                now += 1.0 / Fps;
                walker.Controller.Tick(now);
                runner.Step(now);
                h.Tick(1.0 / Fps);
            }

            var line = string.Create(CultureInfo.InvariantCulture, $"leg {leg} 0x{lowWord:X4} {runner.State} {runner.SteeringFields()} {runner.VerdictFields()}");
            output.WriteLine(line + (runner.FailureReason is { } why ? " -- " + why : string.Empty));
            t = t with
            {
                Legs = t.Legs + 1,
                Metres = t.Metres + runner.RouteMetres,
                Seconds = t.Seconds + runner.RunSeconds,
                Turns = t.Turns + runner.Turns,
                Reversals = t.Reversals + runner.TurnReversals,
                Flips = t.Flips + runner.TurnFlips,
                InPlace = t.InPlace + runner.InPlacePresses,
                ForwardRepresses = t.ForwardRepresses + runner.ForwardRepresses,
                ProgressBacks = t.ProgressBacks + runner.ProgressBacks,
                Swings = t.Swings + runner.Swings,
                Rungs = t.Rungs + runner.Rungs,
                CornerTurns = t.CornerTurns + runner.CornerTurns,
                CornerReversals = t.CornerReversals + runner.CornerReversals,
            };
            if (runner.State != NavRunState.Arrived || runner.Rungs != 0 || walker.Position.CellId != cellId)
            {
                failures.Add(line);
            }
        }

        t.SwingLines.AddRange(log.Lines.Where(l => l.Contains("swing:", StringComparison.Ordinal)));
        foreach (var swing in t.SwingLines)
        {
            output.WriteLine(swing);
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"run skill {runSkill}: {t}"));
        return t;
    }

    [RequiresDatsFact]
    public void The_dens_legs_press_a_turn_key_at_most_once_per_4_m_of_route()
    {
        var dats = fixture.Dats;
        var coord = new LandblockCoord(1, 5);
        var geometry = new PhysicsGeometryLibrary(dats);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, geometry).Build(coord));
        var graph = GroundRooms.GraphOf(LandblockCellSet.Load(new CellStructureLibrary(dats), coord), DenGround.Profile);
        var h = new ActionHarness(offline: true);
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, null, "den-steering");
        var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(FighterRunSkill), sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(DenGround.Arrival, sliding: true));
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var source = new RoomPathSource(graph);
        var now = 0.0;
        var (turns, metres) = (0, 0f);
        var failures = new List<string>();
        foreach (var lowWord in DenGround.SpawnLowWords)
        {
            var cellId = 0x01050000u | lowWord;
            var goal = NavGoal.ToPoint(new NavPoint(0x01050000u, graph.RoomFrame(cellId)!.Value.Origin), cellId);
            using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, new NavRunnerOptions { Name = "den-" + lowWord.ToString("X4", CultureInfo.InvariantCulture) });
            runner.Start(goal, now);
            var end = now + 120;
            while (!runner.IsDone && now < end)
            {
                now += 1.0 / Fps;
                walker.Controller.Tick(now);
                runner.Step(now);
                h.Tick(1.0 / Fps);
            }

            var rate = runner.RouteMetres > 0f ? runner.Turns / runner.RouteMetres : 0f;
            var line = string.Create(CultureInfo.InvariantCulture, $"0x{lowWord:X4} {runner.State} turns_per_m={rate:0.000} {runner.VerdictFields()}");
            output.WriteLine(line + (runner.FailureReason is { } why ? " -- " + why : string.Empty));
            turns += runner.Turns;
            metres += runner.RouteMetres;
            if (runner.State != NavRunState.Arrived || runner.Rungs != 0 || walker.Position.CellId != cellId || runner.Turns > MathF.Max(1f, runner.RouteMetres * IndoorSteeringTests.MaxTurnsPerMetre)) // one press is allowed any leg (D134: 0x02E1, 3.8 m, one turn)
            {
                failures.Add(line);
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"the den's {DenGround.SpawnLowWords.Count} legs: {turns} turns over {metres:0.0} m, {turns / metres:0.000} a metre (one every {metres / Math.Max(1, turns):0.0} m)"));
        Assert.Empty(failures);
    }
}

/// <summary>Synthetic rooms for the indoor runner's tests: unrotated boxes with 2 m doorways on the sides named, in <see cref="S1TileKit.Block"/>.</summary>
internal static class IndoorRoomKit
{
    /// <summary>The corridor's first cell (the start); the bend is +1, the two legs after it +2 and +3 (<see cref="CorridorEnd"/>).</summary>
    public const uint CorridorStart = 0x40400110u;

    /// <summary>The corridor's last cell.</summary>
    public const uint CorridorEnd = 0x40400113u;

    /// <summary>The sharp-turn rooms' first cell (the start); the wide room is +1, the last is +2 (<see cref="SharpTurn"/>).</summary>
    public const uint SharpStart = 0x40400150u;

    /// <summary>
    /// A room entered by its south door and left by its west door with a turn of 84 degrees at the first doorway vertex: 0x150 (96, 90) 6 x 6 m, north into 0x151 (96, 94.5) 20 x 3 m, west into 0x152 (83, 94.5) 6 x 6 m.
    /// </summary>
    public static LandblockCellSet SharpTurn()
    {
        var library = CellStructureLibrary.FromStructures(
        [
            Box(0x0D00F350u, 3f, 3f, 'N'),
            Box(0x0D00F351u, 10f, 1.5f, 'S', 'W'),
            Box(0x0D00F352u, 3f, 3f, 'E'),
        ]);
        return LandblockCellSet.FromCells(library, Block,
        [
            Cell(SharpStart, 0x0D00F350u, 96f, 90f, [Inside(0, SharpStart + 1)]),
            Cell(SharpStart + 1, 0x0D00F351u, 96f, 94.5f, [Inside(0, SharpStart), Inside(1, SharpStart + 2)]),
            Cell(SharpStart + 2, 0x0D00F352u, 83f, 94.5f, [Inside(0, SharpStart + 1)]),
        ]);
    }

    /// <summary>A hall 6 m wide and 40 m long (<see cref="LongHall"/>); +1 is the room at its north end.</summary>
    public const uint Hall = 0x40400130u;

    /// <summary>The ring's south-west room (<see cref="Ring"/>).</summary>
    public const uint RingSouthWest = 0x40400140u;

    /// <summary>The ring's south-east room.</summary>
    public const uint RingSouthEast = 0x40400141u;

    /// <summary>The ring's north-west room.</summary>
    public const uint RingNorthWest = 0x40400142u;

    /// <summary>The ring's north-east room.</summary>
    public const uint RingNorthEast = 0x40400143u;

    /// <summary>The landblock.</summary>
    public static LandblockCoord Block => S1TileKit.Block;

    /// <summary>
    /// A corridor 6 m wide with a right-angle bend: 0x110 (96, 90) 6 x 12 m north to the bend 0x111 (96, 99) 6 x 6 m, east into 0x112 (105, 99) 12 x 6 m
    /// and 0x113 (117, 99) 12 x 6 m. Doorways 2 m wide, 2.2 m tall, upright.
    /// </summary>
    public static LandblockCellSet CorridorWithABend()
    {
        var library = CellStructureLibrary.FromStructures(
        [
            Box(0x0D00F310u, 3f, 6f, 'N'),
            Box(0x0D00F311u, 3f, 3f, 'S', 'E'),
            Box(0x0D00F312u, 6f, 3f, 'W', 'E'),
            Box(0x0D00F313u, 6f, 3f, 'W'),
        ]);
        return LandblockCellSet.FromCells(library, Block,
        [
            Cell(CorridorStart, 0x0D00F310u, 96f, 90f, [Inside(0, CorridorStart + 1)]),
            Cell(CorridorStart + 1, 0x0D00F311u, 96f, 99f, [Inside(0, CorridorStart), Inside(1, CorridorStart + 2)]),
            Cell(CorridorStart + 2, 0x0D00F312u, 105f, 99f, [Inside(0, CorridorStart + 1), Inside(1, CorridorEnd)]),
            Cell(CorridorEnd, 0x0D00F313u, 117f, 99f, [Inside(0, CorridorStart + 2)]),
        ]);
    }

    /// <summary>A hall 0x130 at (96, 76), 6 x 40 m, its north doorway into a 6 x 6 m room 0x131 at (96, 99).</summary>
    public static LandblockCellSet LongHall()
    {
        var library = CellStructureLibrary.FromStructures([Box(0x0D00F330u, 3f, 20f, 'N'), Box(0x0D00F331u, 3f, 3f, 'S')]);
        return LandblockCellSet.FromCells(library, Block,
        [
            Cell(Hall, 0x0D00F330u, 96f, 76f, [Inside(0, Hall + 1)]),
            Cell(Hall + 1, 0x0D00F331u, 96f, 99f, [Inside(0, Hall)]),
        ]);
    }

    /// <summary>
    /// Four 10 m rooms in a ring: south-west (96, 96), south-east (106, 96), north-west (96, 106), north-east (106, 106), each joined to its two
    /// neighbours by a 2 m doorway, so the north-east room is reached from the south-west one by either of two ways.
    /// </summary>
    public static LandblockCellSet Ring()
    {
        var library = CellStructureLibrary.FromStructures(
        [
            Box(0x0D00F340u, 5f, 5f, 'N', 'E'),
            Box(0x0D00F341u, 5f, 5f, 'W', 'N'),
            Box(0x0D00F342u, 5f, 5f, 'S', 'E'),
            Box(0x0D00F343u, 5f, 5f, 'W', 'S'),
        ]);
        return LandblockCellSet.FromCells(library, Block,
        [
            Cell(RingSouthWest, 0x0D00F340u, 96f, 96f, [Inside(0, RingNorthWest), Inside(1, RingSouthEast)]),
            Cell(RingSouthEast, 0x0D00F341u, 106f, 96f, [Inside(0, RingSouthWest), Inside(1, RingNorthEast)]),
            Cell(RingNorthWest, 0x0D00F342u, 96f, 106f, [Inside(0, RingSouthWest), Inside(1, RingNorthEast)]),
            Cell(RingNorthEast, 0x0D00F343u, 106f, 106f, [Inside(0, RingNorthWest), Inside(1, RingSouthEast)]),
        ]);
    }

    private static EnvCellData Cell(uint id, uint environment, float x, float y, CellPortalData[] portals) =>
        EnvCellData.FromParts(id, environment, 0, new Placement(new Vector3(x, y, 0f), Quaternion.Identity), portals);

    private static CellPortalData Inside(ushort polygon, uint other) => new(1, true, true, polygon, other, 0);

    /// <summary>A box 2 hx by 2 hy by 3 m (cell-local, centred on x and y); polygon i is the doorway on side <paramref name="doors"/>[i] (N, S, E, W), normal outward.</summary>
    private static CellStructure Box(uint environment, float hx, float hy, params char[] doors)
    {
        var positions = new List<Vector3>();
        var polygons = new List<CellPolygon>();
        for (var i = 0; i < doors.Length; i++)
        {
            Vector3[] quad = doors[i] switch
            {
                'N' => [new(1f, hy, 0f), new(-1f, hy, 0f), new(-1f, hy, 2.2f), new(1f, hy, 2.2f)],
                'S' => [new(-1f, -hy, 0f), new(1f, -hy, 0f), new(1f, -hy, 2.2f), new(-1f, -hy, 2.2f)],
                'E' => [new(hx, -1f, 0f), new(hx, 1f, 0f), new(hx, 1f, 2.2f), new(hx, -1f, 2.2f)],
                'W' => [new(-hx, 1f, 0f), new(-hx, -1f, 0f), new(-hx, -1f, 2.2f), new(-hx, 1f, 2.2f)],
                _ => throw new ArgumentOutOfRangeException(nameof(doors)),
            };
            var first = (ushort)positions.Count;
            positions.AddRange(quad);
            polygons.Add(new CellPolygon((ushort)i, [first, (ushort)(first + 1), (ushort)(first + 2), (ushort)(first + 3)], [], [], 0, 0, 0, 0));
        }

        var inward = new[]
        {
            new CellPlane(new Vector3(1, 0, 0), hx), new CellPlane(new Vector3(-1, 0, 0), hx),
            new CellPlane(new Vector3(0, 1, 0), hy), new CellPlane(new Vector3(0, -1, 0), hy),
            new CellPlane(new Vector3(0, 0, 1), 0f), new CellPlane(new Vector3(0, 0, -1), 3f),
        };
        var vertices = positions.ToArray();
        return CellStructure.FromParts(
            environment,
            0,
            vertices,
            vertices.Select(_ => Vector3.UnitY).ToArray(),
            vertices.Select(v => new[] { new Vector2(v.X, v.Y) }).ToArray(),
            [.. polygons],
            [.. polygons.Select(p => p.Id)],
            CellBsp.FromPlanes(inward),
            hasDrawingBsp: true);
    }
}

/// <summary>The scripted body indoors: <see cref="S5KinematicBody"/> (which knows only land cells) with its cell found among the rooms each frame.</summary>
internal sealed class IndoorBody(S5KinematicBody body, LandblockCellSet cells, uint startCell) : INavBody
{
    private CellFrame _position = new(startCell, body.Position.Frame);

    /// <summary>Frames the body stood in no room (the kinematic body walks through walls; the runner must not ask it to).</summary>
    public int FramesOutside { get; private set; }

    public CellFrame Position => _position;

    public uint CellId => _position.CellId;

    public bool OnGround => body.OnGround;

    public bool ServerDriving => body.ServerDriving;

    public double Now => body.Now;

    /// <summary>Finds the body's room after it moved (the last one when it stands in none, counted).</summary>
    public void Sync()
    {
        var frame = body.Position.Frame;
        var cell = _position.CellId;
        if (cells.FindCell(frame.Origin + new Vector3(0f, 0f, 0.5f), cell) is { } found)
        {
            cell = found.Id;
        }
        else
        {
            FramesOutside++;
        }

        _position = new CellFrame(cell, frame);
    }
}

/// <summary>The scripted body over a room graph: the runner over a <see cref="RoomPathSource"/>, a feed the test fills, the doorway vertices' nearest pass.</summary>
internal sealed class IndoorRig : INavCreatureFeed
{
    private readonly List<Vector2> _track = [];

    public IndoorRig(LandblockCellSet cells, float maxLean, uint startCell, Vector2 start, float heading, bool withFeed = false)
    {
        H = new ActionHarness(offline: true);
        Body = new S5KinematicBody(start.X, start.Y, heading);
        Indoor = new IndoorBody(Body, cells, startCell);
        H.Api.Move.Bind(Body, false);
        Graph = new RoomGraph(cells, maxLean);
        Source = new RoomPathSource(Graph);
        Runner = new NavRunner(H.Api.Move, Indoor, Source, creatures: withFeed ? this : null);
    }

    public ActionHarness H { get; }

    public S5KinematicBody Body { get; }

    public IndoorBody Indoor { get; }

    public RoomGraph Graph { get; }

    public RoomPathSource Source { get; }

    public NavRunner Runner { get; }

    public List<NavCreature> Creatures { get; } = [];

    public int FramesOutside => Indoor.FramesOutside;

    /// <summary>The farthest the body's track passed from a doorway vertex of the first plan, in metres.</summary>
    public float DoorwayMissMetres { get; private set; }

    /// <summary>The body's track, global metres.</summary>
    public IReadOnlyList<Vector2> Track => _track;

    public void Fill(Vector2 from, float maxMetres, List<NavCreature> into)
    {
        into.Clear();
        foreach (var c in Creatures)
        {
            if (Vector2.Distance(c.Centre, from) <= maxMetres)
            {
                into.Add(c);
            }
        }
    }

    public void Frame()
    {
        Body.Advance(S5Rig.Dt);
        Indoor.Sync();
        Runner.Step(Body.Now);
        H.Tick(S5Rig.Dt);
        _track.Add(NavMap.FromCellFrame(Indoor.Position).Global);
    }

    public void RunFor(double seconds)
    {
        var end = Body.Now + seconds;
        while (Body.Now < end && !Runner.IsDone)
        {
            Frame();
        }
    }

    /// <summary>Runs to <paramref name="to"/> in <paramref name="cell"/> for at most <paramref name="seconds"/>; a line of the verdict fields.</summary>
    public string Run(uint cell, Vector3 to, double seconds)
    {
        Runner.Start(NavGoal.ToPoint(new NavPoint(cell & 0xFFFF0000u, to), cell), Body.Now);
        Frame();
        var doorways = Runner.Path!.Waypoints.Where(w => w.Doorway).Select(w => w.Point.Global).ToArray();
        RunFor(seconds);
        DoorwayMissMetres = doorways.Length == 0 ? 0f : doorways.Max(d => Enumerable.Range(1, _track.Count - 1).Min(i => SegmentDistance(d, _track[i - 1], _track[i])));
        return string.Create(CultureInfo.InvariantCulture, $"{Runner.State} {Runner.VerdictFields()} outside={FramesOutside} doorway_miss_m={DoorwayMissMetres:0.00} waypoints={Runner.Path?.Waypoints.Count}");
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared();
        var t = len2 <= 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + (ab * t));
    }
}

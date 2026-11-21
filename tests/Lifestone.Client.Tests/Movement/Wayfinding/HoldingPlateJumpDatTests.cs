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
/// M6 14 holding plates (the owner, 2026-11-14: in the Tusker Holding the climber must avoid the Tusker's Friend pressure plates, and where one blocks the way, she can jump), on the dats with the client's own physics (an
/// <see cref="OfflineWalker"/> under the real runner): every standing long jump the room graph derives over the plates lands past their walls on the far floor and no sphere of her body reaches a plate's box on the way (the shape ACE
/// fires on, <see cref="HoldingPlateBoxes"/>); the position she reports last before each Jump message is her take-off, so the server's own flight of the jump starts where hers does; the walk of the Holding's tour with the plates as
/// walls reaches 101 of its 102 upper-level slots, 10 of them on foot from the arrival and 91 over at least one plate jump (the slot in 0x5B4802B2 has no floor outside its plate's wall); a round of the tour keeps her off every box; a
/// plate that fires on a jump ends it by name and takes the jump out of the rest of the run; and the model the climber ran with until 2026-11-20 is held to the box. Exhaustive tier, skipped without the dats.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class HoldingPlateJumpDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const double Fps = 60.0;

    /// <summary>Qwinnie at level 47 with the buff bot's level VII set (PitJumpDatTests): Run 103, Jump 178.</summary>
    private const int BuffedRun = 103;

    private const int BuffedJump = 178;

    private static HuntingGround Holding => GroundCatalog.Get("tusker-holding");

    /// <summary>The box ACE fires a plate on (its Setup's physics part in the dats, the profile's <c>setup</c>; <see cref="HoldingPlateBoxes"/>).</summary>
    private (Vector3 Min, Vector3 Max) PlateBox => HoldingPlateBoxes.Box(fixture.Dats, Holding.Plates!.SetupId);

    /// <summary>Her spheres (Setup 0x02000001 in the dats).</summary>
    private IReadOnlyList<Data.Physics.Primitives.PhysSphere> BodySpheres => Data.Physics.Objects.PhysicsSetup.Load(fixture.Dats, OfflineWalker.HumanSetupId)!.Spheres;

    /// <summary>Her least gap to any plate's box with her feet at <paramref name="feet"/> (landblock-local), and the plate.</summary>
    private Func<Vector3, (float Margin, string Plate)> BoxMargin()
    {
        var (plates, box, body) = (HoldingPlateBoxes.Plates(Holding), PlateBox, BodySpheres);
        return feet => HoldingPlateBoxes.Margin(feet, plates, box, body);
    }

    private LandblockCellSet CellsOf(HuntingGround ground)
    {
        var landblock = Assert.Single(ground.Landblocks);
        return LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), new LandblockCoord((byte)(landblock >> 8), (byte)(landblock & 0xFF)));
    }

    private RoomGraph BuffedGraph(HuntingGround ground)
    {
        var graph = GroundRooms.GraphOf(CellsOf(ground), ground);
        var weenie = new OfflinePlayerWeenie(BuffedRun, BuffedJump);
        Assert.True(weenie.TryGetRunRate(out var rate));
        graph.PlateSpeedMetresPerSecond = MotionInterpreter.RunAnimSpeed * rate;
        graph.JumpReachMetres = JumpReach.FlightMetres(weenie);
        return graph;
    }

    /// <summary>
    /// Every standing jump over the plates, the runner's own procedure (walk to the take-off, stop, turn to the landing, charge the bar standing still, run forward on the standing long jump, release): it lands past the far edge of
    /// the walls on the far floor, her spheres stay clear of every plate's box at every frame of the walk and the flight, and the last position she reported before the Jump message is the take-off (the server flies the jump
    /// from there; its sweep between her reports on the ground keeps out of every plate's touch too).
    /// </summary>
    [RequiresDatsFact]
    public void Every_plate_jump_lands_past_its_plates_and_no_sphere_touches_one()
    {
        var ground = Holding;
        var graph = BuffedGraph(ground);
        var jumps = graph.AllPlateJumps();
        Assert.Equal(11, jumps.Count);
        foreach (var jump in jumps.OrderBy(j => j.Name, StringComparer.Ordinal))
        {
            var result = JumpOver(ground, jump, witness: null);
            output.WriteLine(result.Line);
            Assert.True(result.State == NavRunState.Arrived, result.Line);
            Assert.Equal((1, 1), (result.Taken, result.Landed));
            Assert.True(result.PastFarEdge >= 0f, $"{jump.Name}: {result.PastFarEdge:0.00} m past the far edge");
            Assert.InRange(result.FloorRise, -0.1f, 0.1f);
            Assert.True(result.BoxMargin > 0f, $"{jump.Name}: her sphere came {result.BoxMargin:0.00} m from a plate's box");
            Assert.True(result.ReportToTakeOff <= NavRunner.StandStillMetres, $"{jump.Name}: the last report before the Jump stood {result.ReportToTakeOff:0.00} m from the take-off");
            Assert.True(result.ReportedChordMargin > 0f, $"{jump.Name}: a chord between her reports passed {result.ReportedChordMargin:0.00} m inside a plate's touch");
        }
    }

    /// <summary>
    /// The tour with the plates as walls, on the room graph the fight walks (the buffed climber's speed and flight): every one of the 102 upper-level slots' cells is planned from the arrival, its leg's goal moved off a plate's wall
    /// where its room frame stands in one (0x5B4802D3 and 0x5B480388); 10 slots in 7 cells are reached on foot, 91 in 75 cells over at least one plate jump, and the one slot in 0x5B4802B2 is dropped: its plate stands on the cell's
    /// centre and no floor of the cell lies outside the plate's wall (<c>FightWalk.GoalOffPlate</c>). No walked step of any plan comes within a wall.
    /// </summary>
    [RequiresDatsFact]
    public void The_tour_with_plates_as_walls_reaches_every_slot_and_names_the_jumps()
    {
        var ground = Holding;
        var graph = BuffedGraph(ground);
        var legs = new RunnerLegs(ground, new ProfileTour(ground), graph, new RecordingLogSink(LogSeverity.Info));
        var slots = ground.Respawn.Slots.Where(s => ground.RoleOf(s.Wcid) == GroundRole.Target && ground.IsWalkable(s.Cell)).ToList();
        Assert.Equal(102, slots.Count);
        var (foot, jumped, dropped, moved) = (new List<uint>(), new List<uint>(), new List<uint>(), new List<uint>());
        var named = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var cell in slots.Select(s => s.Cell).Distinct())
        {
            var goal = legs.GoalOutOfAvoid(cell, ground.Arrival.Origin);
            if (goal is { } g && g.Origin != graph.RoomFrame(cell)!.Value.Origin)
            {
                moved.Add(cell);
            }

            if (goal is null || graph.Plan(ground.Arrival.Cell, ground.Arrival.Origin, cell, goal.Value.Origin) is not { } route)
            {
                dropped.Add(cell);
                continue;
            }

            var points = new List<NavWaypoint> { new(new CellFrame(ground.Arrival.Cell, new AcFrame(ground.Arrival.Origin, Quaternion.Identity)), Doorway: false) };
            points.AddRange(route.Waypoints.Select(w => new NavWaypoint(w, Doorway: true)));
            foreach (var j in route.Jumps)
            {
                points[j.TakeOff + 1] = points[j.TakeOff + 1] with { Jump = j.Jump };
            }

            points.Add(new NavWaypoint(goal.Value, Doorway: false));
            Assert.Null(graph.Plates!.FirstMeeting(points, graph.LandblockId));
            var standing = route.Jumps.Where(j => j.Jump.Crossing.Standing).ToList();
            (standing.Count == 0 ? foot : jumped).Add(cell);
            named.UnionWith(standing.Select(j => j.Jump.Crossing.Name));
        }

        int Count(List<uint> cells) => slots.Count(s => cells.Contains(s.Cell));
        output.WriteLine($"{slots.Count} slots: {Count(foot)} on foot ({foot.Count} cells), {Count(jumped)} over a plate jump ({jumped.Count} cells), {Count(dropped)} dropped; goals moved off a wall: {string.Join(" ", moved.Select(c => $"0x{c:X8}"))}");
        output.WriteLine("on foot: " + string.Join(" ", foot.Select(c => $"0x{c:X8}")));
        output.WriteLine("jumps used: " + string.Join("; ", named));
        Assert.Equal(new uint[] { 0x5B4802B2 }, dropped);
        Assert.Equal((10, 91, 1), (Count(foot), Count(jumped), Count(dropped)));
        Assert.Equal(new uint[] { 0x5B4802D3, 0x5B480388 }, moved.Order()); // the room frames inside a wall that a floor of the cell stands past
    }

    /// <summary>
    /// End to end: the body walks one round of the Holding's tour from the arrival on the fight's legs (<see cref="RunnerLegs"/>, the profile's order) with the plates as walls: every leg arrives, every jump it takes lands, her body on
    /// the ground never comes within a plate's touch, no chord between two of her reports on the ground passes within one, and at no frame of the walks and the flights does a sphere of hers reach a plate's box (the shape ACE fires on).
    /// </summary>
    [RequiresDatsFact]
    public void A_round_of_the_tour_keeps_off_every_plate_and_lands_every_jump()
    {
        var graph = BuffedGraph(Holding);
        var round = Round(graph);
        output.WriteLine(round.Line);
        Assert.Empty(round.Failed);
        Assert.True(round.Taken > 0 && round.Taken == round.Landed, $"{round.Taken} taken, {round.Landed} landed");
        Assert.True(round.Nearest > PlateKeepOff.TouchOf(graph.Plates!.Plates[0]), $"the body came {round.Nearest:0.00} m from a plate");
        Assert.True(round.Chord > 0f, $"a chord between her reports passed {round.Chord:0.00} m inside a plate's touch");
        Assert.True(round.Box > 0f, $"her spheres came {-round.Box:0.00} m into {round.BoxPlate}'s box");
        Assert.DoesNotContain(round.Log, l => l.Contains("inside a pressure plate's touch", StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>TODO(maps-plate-setup)</c>'s question on the live code: the model the climber ran with until 2026-11-20 (the profile's <c>shape_m</c> 0.24 and <c>top_m</c> 0.18, typed from Setup 0x02000550, a short frost sword's), against the
    /// box ACE fires the plate on (<see cref="HoldingPlateBoxes"/>). Its walls round each plate at its own heading: how far her spheres stand from the box with her feet on a wall's edge (the plan's line) and at its touch (the line less
    /// her whole stop, the most she carries past it); its 13 standing jumps, each flown by the runner, at every frame; and one round of the tour on its walls. Report and pin.
    /// </summary>
    [RequiresDatsFact]
    public void The_model_until_2026_11_20_against_the_box_ACE_fires_on()
    {
        var ground = Holding;
        var old = ground.KeptPlatesOf(fixture.Dats).Select(p => p with { ShapeMetres = 0.24f, TopMetres = 0.18f }).ToList();
        var graph = BuffedGraph(ground);
        graph.PlateList = old;
        var walls = graph.Plates!;
        var boxMargin = BoxMargin();
        (float Margin, string Plate) Around(float metres)
        {
            var least = (float.PositiveInfinity, string.Empty);
            foreach (var p in walls.Plates.Where(p => ground.IsWalkable(p.Cell)))
            {
                for (var degree = 0; degree < 360; degree++)
                {
                    var a = degree * MathF.PI / 180f;
                    if (boxMargin(p.Point + new Vector3(metres * MathF.Cos(a), metres * MathF.Sin(a), 0f)) is var (m, plate) && m < least.Item1)
                    {
                        least = (m, plate);
                    }
                }
            }

            return least;
        }

        var reach = walls.ReachOf(walls.Plates[0]);
        var touch = PlateKeepOff.TouchOf(walls.Plates[0]);
        var (onWall, wallPlate) = Around(reach);
        var (onTouch, touchPlate) = Around(touch);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"its walls: {reach:0.000} m (touch {touch:0.000} m and her stop {walls.StopMetres:0.000} m); her spheres from the nearest box: {onWall:0.000} m on a wall's edge ({wallPlate}), {onTouch:0.000} m at its touch ({touchPlate})"));

        var jumps = graph.AllPlateJumps();
        var into = new List<string>();
        foreach (var jump in jumps.OrderBy(j => j.Name, StringComparer.Ordinal))
        {
            var result = JumpOver(ground, jump, witness: null, graph);
            output.WriteLine(result.Line);
            if (result.BoxMargin <= 0f)
            {
                into.Add(jump.Crossing.Name);
            }
        }

        output.WriteLine($"{into.Count} of {jumps.Count} jumps reach into a box: {string.Join("; ", into)}");
        var fresh = BuffedGraph(ground);
        fresh.PlateList = old;
        var round = Round(fresh);
        output.WriteLine("its round: " + round.Line);

        // Its walls were thinner than the box's touch at a corner: her spheres on a wall's edge reach into a box, and a round of the tour on them walks into the box of the plate in 0x5B480388. Its 13 jumps clear every box: the
        // box is 0.075 m high, and every take-off and landing stood outside its touch.
        Assert.True(onWall < 0f && onTouch < 0f, $"{onWall:0.000} m on the edge, {onTouch:0.000} m at the touch");
        Assert.Equal(13, jumps.Count);
        Assert.Empty(into);
        Assert.Empty(round.Failed);
        Assert.True(round.Box < 0f, round.Line);
        Assert.Equal("the plate 0x75B480C8 in 0x5B480388", round.BoxPlate);
    }

    /// <param name="Failed">The legs that did not arrive.</param>
    /// <param name="Taken">The jumps taken.</param>
    /// <param name="Landed">The jumps landed.</param>
    /// <param name="Nearest">The nearest her body stood to a plate's origin on the ground (the runner's own watch).</param>
    /// <param name="Chord">The least margin outside a plate's touch of a chord between her reports on the ground.</param>
    /// <param name="Box">The least gap between her spheres and a plate's box at any frame; negative when one reached into it.</param>
    /// <param name="BoxPlate">That plate.</param>
    /// <param name="Log">The run's log.</param>
    /// <param name="Line">The report.</param>
    private sealed record RoundResult(IReadOnlyList<string> Failed, int Taken, int Landed, float Nearest, float Chord, float Box, string BoxPlate, IReadOnlyList<string> Log, string Line);

    /// <summary>One round of the Holding's tour from the arrival on the fight's legs over <paramref name="graph"/>, the body's spheres measured against the plates' boxes at every frame.</summary>
    private RoundResult Round(RoomGraph graph)
    {
        var ground = Holding;
        var sink = new RecordingMovementSink();
        var (world, walker) = BodyIn(ground, new CellFrame(ground.Arrival.Cell, new AcFrame(ground.Arrival.Origin, Quaternion.Identity)), sink);
        var h = new ActionHarness(offline: true);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        var log = new RecordingLogSink(LogSeverity.Info);
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new RoomPathSource(graph), confirmer, null, new NavRunnerOptions { Name = "plates-round" }, log);
        var legs = new RunnerLegs(ground, new ProfileTour(ground), graph, log);
        var now = 0.0;
        sink.Clock = () => now;
        var (arrived, failed, nearest, where) = (0, new List<string>(), float.PositiveInfinity, string.Empty);
        var boxMargin = BoxMargin();
        var (box, boxPlate, boxAt) = (float.PositiveInfinity, string.Empty, string.Empty);
        for (var leg = 0; leg < ground.Tour.Count; leg++)
        {
            if (!legs.Begin(runner, walker.Position, now, now, out var description))
            {
                failed.Add($"leg {leg} not begun: {legs.Failure}");
                break;
            }

            var status = FightWalkStatus.Walking;
            var end = now + 120;
            while (status == FightWalkStatus.Walking && now < end)
            {
                now += 1.0 / Fps;
                walker.Controller.Tick(now);
                status = legs.Step(walker.Position, now, now);
                h.Tick(1.0 / Fps);
                if (boxMargin(walker.Position.Origin) is var (m, plate) && m < box)
                {
                    (box, boxPlate, boxAt) = (m, plate, $"{description}, at {walker.Position}");
                }
            }

            if (runner.PlateNearestMetres < nearest)
            {
                (nearest, where) = (runner.PlateNearestMetres, runner.PlateNearestWhere ?? string.Empty);
            }

            if (status == FightWalkStatus.Arrived && legs.FailedInARow == 0)
            {
                arrived++;
            }
            else
            {
                failed.Add($"{description}: {status} {runner.FailureReason}");
            }
        }

        var chord = ChordMargin(sink.Calls, graph.Plates!.Plates);
        var line = string.Create(CultureInfo.InvariantCulture, $"{arrived} of {ground.Tour.Count} legs arrived in {now:0} s; jumps taken {legs.Runner!.JumpsTaken}, landed {legs.Runner.JumpsLanded}; nearest a plate on the ground {nearest:0.00} m ({where}); the reports' chords {chord:0.00} m outside the touch; her spheres {box:0.00} m from the nearest plate's box ({boxPlate}: {boxAt})");
        line += string.Concat(failed.Select(f => Environment.NewLine + "  failed: " + f));
        return new RoundResult(failed, legs.Runner.JumpsTaken, legs.Runner.JumpsLanded, nearest, chord, box, boxPlate, log.Lines, line);
    }

    /// <summary>
    /// Check after: a plate whose spell reaches her on a jump (a witness that says so at the landing) ends the run <c>jump-plate-fired</c> by name, the jump is refused on the room graph for the rest of the run, and the next plan over the
    /// same corridor offers no jump over that plate: a plan to the far side goes another way (round the plate by the corridor's wall, outside its wall) and no walked step of it comes within a wall.
    /// </summary>
    [RequiresDatsFact]
    public void A_plate_that_fires_on_a_jump_ends_it_by_name_and_refuses_the_jump_for_the_run()
    {
        var ground = Holding;
        var graph = BuffedGraph(ground);
        var jump = graph.AllPlateJumps().Single(j => j.Crossing.Name.Contains("0x5B48026B north", StringComparison.Ordinal));
        var witness = new FiringWitness();
        var result = JumpOver(ground, jump, witness, graph);
        output.WriteLine(result.Line);
        Assert.Equal(NavRunState.Failed, result.State);
        Assert.StartsWith(NavRunner.PlateFiredFailure + ":", result.Why, StringComparison.Ordinal);
        Assert.Contains("Tusker's Gift", result.Why, StringComparison.Ordinal);
        Assert.Equal(1, witness.TookOffs);
        Assert.Contains(jump.Crossing.Name, graph.RefusedPlateJumps);
        Assert.DoesNotContain(graph.AllPlateJumps(), j => j.Crossing.Name == jump.Crossing.Name);
        if (graph.Plan(jump.From.Cell, jump.From.Brink, jump.To.Cell, jump.To.Landing) is { } round)
        {
            // the corridor stays closed by the plate's wall: a plan to the far side goes another way, never over the refused jump nor across a wall
            Assert.DoesNotContain(round.Jumps, j => j.Jump.Crossing.Name == jump.Crossing.Name);
            var points = new List<NavWaypoint> { new(jump.TakeOff, Doorway: false) };
            points.AddRange(round.Waypoints.Select(w => new NavWaypoint(w, Doorway: true)));
            foreach (var j in round.Jumps)
            {
                points[j.TakeOff + 1] = points[j.TakeOff + 1] with { Jump = j.Jump };
            }

            points.Add(new NavWaypoint(jump.Landing, Doorway: false));
            Assert.Null(graph.Plates!.FirstMeeting(points, graph.LandblockId));
            output.WriteLine($"the way round: {round.Cells.Count} rooms, {round.Jumps.Count} jumps ({string.Join("; ", round.Jumps.Select(j => j.Jump.Crossing.Name))})");
        }
    }

    private sealed class FiringWitness : IJumpWitness
    {
        public int TookOffs { get; private set; }

        public void TookOff(DirectedJump jump) => TookOffs++;

        public string? Fired() => TookOffs > 0 ? "the chat line \"Tusker's Friend casts Tusker's Gift on you\"" : null;
    }

    /// <param name="PastFarEdge">How far past the far edge of the walls, along the jump, the body came to rest.</param>
    /// <param name="FloorRise">The body's height over the take-off's at rest.</param>
    /// <param name="BoxMargin">The least gap between her spheres and a plate's box (<see cref="HoldingPlateBoxes"/>) over the walk and the flight; negative when one reached into it.</param>
    /// <param name="ReportToTakeOff">How far the last position she reported before the Jump stood from the take-off.</param>
    /// <param name="ReportedChordMargin">The least margin, outside a plate's touch, of a chord between two of her reports on the ground (the jump's own flight is the server's, from the take-off).</param>
    private sealed record JumpResult(NavRunState State, string? Why, int Taken, int Landed, float PastFarEdge, float FloorRise, float BoxMargin, float ReportToTakeOff, float ReportedChordMargin, string Line);

    /// <summary>A body with the climber's buffed skills, 2.5 m short of the jump's take-off (or at it), runs the jump to its landing under the runner, the path forced through the jump (a walk round may exist in a room).</summary>
    private JumpResult JumpOver(HuntingGround ground, DirectedJump jump, IJumpWitness? witness, RoomGraph? shared = null)
    {
        var graph = shared ?? BuffedGraph(ground);
        var d = jump.Direction;
        var sink = new RecordingMovementSink();
        (PhysicsWorld World, OfflineWalker Walker)? placed = null;
        foreach (var metres in new[] { 2.5f, 2.0f, 1.5f, 1.0f, 0f })
        {
            var back = jump.From.Brink - new Vector3(d.X * metres, d.Y * metres, 0f);
            if (graph.Plates!.Holding(back) is not null)
            {
                continue;
            }

            foreach (var cell in graph.Cells.Cells.Select(c => c.Id).OrderBy(c => c == jump.From.Cell ? 0 : 1))
            {
                if (graph.RoomFloorUnder(cell, back + new Vector3(0f, 0f, 0.5f)) is { } z && MathF.Abs(z - jump.From.Brink.Z) < 0.1f
                    && TryBodyIn(ground, new CellFrame(cell, new AcFrame(back with { Z = z }, Quaternion.Identity)), sink) is { } body)
                {
                    placed = body;
                    break;
                }
            }

            if (placed is not null)
            {
                break;
            }
        }

        Assert.True(placed is not null, $"{jump.Name}: no place on the floor short of the take-off holds the body");
        var (world, walker) = placed!.Value;
        var h = new ActionHarness(offline: true);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        var log = new RecordingLogSink(LogSeverity.Info);
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new ForcedJump(graph, jump), confirmer, null, new NavRunnerOptions { Name = "plate-jump" }, log) { JumpWitness = witness };
        var now = 0.0;
        sink.Clock = () => now;
        runner.Start(NavGoal.ToPoint(new NavPoint(jump.To.Cell & 0xFFFF0000u, jump.To.Landing), jump.To.Cell), now);
        var (margin, nearestPlate) = (float.PositiveInfinity, string.Empty);
        var boxMargin = BoxMargin();
        while (!runner.IsDone && now < 30)
        {
            now += 1.0 / Fps;
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / Fps);
            if (boxMargin(walker.Position.Origin) is var (m, plate) && m < margin)
            {
                (margin, nearestPlate) = (m, plate);
            }
        }

        var at = walker.Position.Origin;
        var block = Vector2.Zero; // landblock-local throughout
        var pastFar = Vector2.Dot(new Vector2(at.X, at.Y) - new Vector2(jump.To.Brink.X, jump.To.Brink.Y) + block, d);
        var jumpCall = sink.Calls.FindIndex(c => c.Kind == RecordingMovementSink.Kind.Jump);
        var lastReport = jumpCall > 0 ? sink.Calls.Take(jumpCall).LastOrDefault(c => c.Kind != RecordingMovementSink.Kind.Jump) : default;
        var toTakeOff = jumpCall > 0 ? Vector3.Distance(lastReport.Position.Origin, sink.Calls[jumpCall].Position.Origin) : float.PositiveInfinity;
        var chord = ChordMargin(sink.Calls, graph.Plates!.Plates);
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{jump.Name}: {runner.State} in {now:0.0} s, body 0x{walker.Position.CellId:X8} ({at.X:0.00}, {at.Y:0.00}, {at.Z:0.00}), {pastFar:0.00} m past the far edge, box margin {margin:0.00} m ({nearestPlate}), last report {toTakeOff:0.00} m from the take-off, chords {chord:0.00} m clear; {runner.LastJump}{(runner.FailureReason is { } why ? " -- " + why : string.Empty)}");
        var reason = runner.FailureReason is { } r ? (r.IndexOf("jump-", StringComparison.Ordinal) is var i and >= 0 ? r[i..] : r) : null;
        if (runner.State != NavRunState.Arrived)
        {
            line += Environment.NewLine + string.Join(Environment.NewLine, log.Lines.TakeLast(12).Select(l => "    " + l));
        }

        return new JumpResult(runner.State, reason, runner.JumpsTaken, runner.JumpsLanded, pastFar, at.Z - jump.From.Brink.Z, margin, toTakeOff, chord, line);
    }

    /// <summary>
    /// The least margin outside a plate's touch (<see cref="PlateKeepOff.TouchOf"/>) of the chords between consecutive positions she reported standing on the ground (the server's sweep, Player_Tick.cs:411-479); a chord across a Jump
    /// message is the server's own flight from the take-off, left out.
    /// </summary>
    private static float ChordMargin(IReadOnlyList<RecordingMovementSink.Sent> calls, IReadOnlyList<KeptPlate> plates)
    {
        var least = float.PositiveInfinity;
        RecordingMovementSink.Sent? last = null;
        foreach (var call in calls)
        {
            if (call.Kind == RecordingMovementSink.Kind.Jump)
            {
                last = null;
                continue;
            }

            if (last is { } a)
            {
                foreach (var p in plates)
                {
                    if (MathF.Abs(p.Point.Z - a.Position.Origin.Z) > PlateKeepOff.StoreyMetres)
                    {
                        continue;
                    }

                    least = MathF.Min(least, Segment(new Vector2(p.Point.X, p.Point.Y), a.Position.Origin, call.Position.Origin) - PlateKeepOff.TouchOf(p));
                }
            }

            last = call.Contact ? call : null;
        }

        return least;
    }

    private static float Segment(Vector2 p, Vector3 a3, Vector3 b3)
    {
        var (a, b) = (new Vector2(a3.X, a3.Y), new Vector2(b3.X, b3.Y));
        var ab = b - a;
        var t = ab.LengthSquared() < 1e-8f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
        return Vector2.Distance(p, a + (ab * t));
    }

    private (PhysicsWorld World, OfflineWalker Walker)? TryBodyIn(HuntingGround ground, CellFrame at, RecordingMovementSink sink)
    {
        var landblock = ground.Landblocks[0];
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(fixture.Dats, new PhysicsGeometryLibrary(fixture.Dats)).Build(new LandblockCoord((byte)(landblock >> 8), (byte)(landblock & 0xFF))));
        var walker = OfflineWalker.Create(fixture.Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(BuffedRun, BuffedJump), sink: sink);
        world.UseTime(0.0);
        return walker.Physics.SetPositionSimple(at, sliding: true) == Data.Physics.Objects.SetPositionError.OK ? (world, walker) : null;
    }

    private (PhysicsWorld World, OfflineWalker Walker) BodyIn(HuntingGround ground, CellFrame at, RecordingMovementSink sink)
    {
        var landblock = ground.Landblocks[0];
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(fixture.Dats, new PhysicsGeometryLibrary(fixture.Dats)).Build(new LandblockCoord((byte)(landblock >> 8), (byte)(landblock & 0xFF))));
        var walker = OfflineWalker.Create(fixture.Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(BuffedRun, BuffedJump), sink: sink);
        world.UseTime(0.0);
        var placed = walker.Physics.SetPositionSimple(at, sliding: true);
        Assert.True(placed == Data.Physics.Objects.SetPositionError.OK, $"placing the body at {at}: {placed}");
        return (world, walker);
    }

    /// <summary>The room graph's path source, its first plan forced through <paramref name="jump"/> (the start, the take-off carrying the jump, the landing); every later plan the graph's own.</summary>
    private sealed class ForcedJump(RoomGraph graph, DirectedJump jump) : INavPathSource, INavRoomSource
    {
        private bool _forced;

        public RoomGraph Rooms => graph;

        public NavMap? Map => null;

        public PathPlanner? Planner => null;

        public NavPath Plan(in CellFrame from, NavGoal goal, NavScars scars)
        {
            if (_forced)
            {
                return RoomPathSource.PlanRooms(graph, from, goal);
            }

            _forced = true;
            var points = new List<NavWaypoint>
            {
                new(new CellFrame(from.CellId, new AcFrame(from.Origin, Quaternion.Identity)), Doorway: false),
                new NavWaypoint(jump.TakeOff, Doorway: false) { Jump = jump },
                new(jump.Landing, Doorway: false),
            };
            return new NavPath(NavPathStatus.Found, points, null, [from.CellId, jump.To.Cell]);
        }
    }
}

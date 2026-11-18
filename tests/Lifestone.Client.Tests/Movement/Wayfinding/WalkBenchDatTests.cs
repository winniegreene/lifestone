using System.Globalization;
using System.Numerics;
using System.Text;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Tests.Session;
using Lifestone.Client.Tests.Session.Journey;
using Lifestone.Data.Animation;
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
/// The walk bench (M6 14, the hub walk, 2026-11-18): a fixed set of offline walks on the retail dats, replayed exactly, so a steering change is judged walk for walk before and after on the same starts and
/// goals. Every walk repeats to the bit for the same build: each set starts from the same place, the clock is the step count, and nothing reads the wall clock.
/// <list type="bullet">
/// <item><c>hubs-ord</c>, <c>hubs-pred</c>: the 40 hub legs (<see cref="HubWalkDatTests"/>: the Town Network's arrival 0x00070143 to and from its eleven portal rooms, the Facility Hub's arrival 0x8A020212 to and from nine of its rooms), each placed fresh at its room's frame, with the ordinary steering (the world walk's) and the predictive (the journeys').</item>
/// <item><c>hops</c>: the hop's front walk as the journeys run it live (the leg's own planner, the portals kept off, predictive steering indoors and out) from each hub's arrival to every portal's front there; <c>facility-hub/hub-quarters</c> is the live crossing, 0x8A020212 to the Tusker Quarters portal's front.</item>
/// <item><c>hops-seek</c>, <c>hops-use</c>: the hop's whole walk, its front and then the seek to within the portal's 2 m, with the hand-over to ACE's walk-to-use off and on (<see cref="NavRunner.UseTarget"/>; the server's walk
/// run on the client's own MoveTo, <see cref="FacilityHubWalkDatTests.Rig.ServerWalk"/>): the same code, the rule's before and after.</item>
/// <item><c>dungeons-ord</c>, <c>dungeons-pred</c>: every dungeon ground's whole tour from its arrival, leg after leg (<see cref="IndoorRunThroughDatTests"/>' grounds and graphs; the Cave of Alabree's is <see cref="AlabreeCaveWalkDatTests"/>' tour), with each steering.</item>
/// </list>
/// The world walk's rooms run on a fixed list of landblocks is the bench's other half, its verdicts compared by the tool's own <c>compare</c> (<c>tools/walk-bench.ps1</c> runs both).
/// A tool, not a check: <c>[Trait("Tier", "Tool")]</c> keeps it out of the default and exhaustive filters, and without <see cref="Variable"/> it skips. From the checkout:
/// <code>
/// $env:LIFESTONE_WALK_BENCH = 'after,before'   # this run's label, then the label to compare with (optional); LIFESTONE_WALK_BENCH_SETS picks sets (default all)
/// dotnet test tests/Lifestone.Client.Tests --disable-build-servers --filter "FullyQualifiedName~WalkBenchDatTests"
/// </code>
/// It writes <c>cache/walk-bench/&lt;label&gt;.tsv</c> (one row a walk) and prints each set's totals, and with a base label the totals before and after and every walk whose verdict or counts changed.
/// </summary>
[Trait("Tier", "Tool")]
public sealed class WalkBenchDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>A walk to trace, by its name in the bench (<c>facility-hub/hub-quarters</c>): its steps and the runner's log go to <c>cache/walk-bench/trace-&lt;name&gt;.txt</c>.</summary>
    public const string TraceVariable = "LIFESTONE_WALK_BENCH_TRACE";

    /// <summary>The bench's switch: <c>label[,base label]</c>.</summary>
    public const string Variable = "LIFESTONE_WALK_BENCH";

    /// <summary>The sets to run, comma-separated (unset: all).</summary>
    public const string SetsVariable = "LIFESTONE_WALK_BENCH_SETS";

    private const double Fps = 60.0;

    /// <summary>The fighters' buffed Run skill the dungeon benches walk at (<see cref="IndoorRunThroughDatTests"/>).</summary>
    private const int FighterRunSkill = 230;

    private static readonly ushort[] TownRooms = [0x0124, 0x0126, 0x012A, 0x012D, 0x013D, 0x0150, 0x015F, 0x0160, 0x016C, 0x0176, 0x0117];

    private static readonly ushort[] FacilityRooms = [0x0118, 0x0119, 0x011A, 0x011E, 0x011F, 0x0121, 0x0122, 0x0123, 0x01F4];

    private static readonly string[] AllSets = ["hubs-ord", "hubs-pred", "hops", "hops-seek", "hops-use", "dungeons-ord", "dungeons-pred"];

    private static readonly string[] Columns = ["set", "walk", "state", "rungs", "refusals", "replans", "route_m", "run_s", "keys", "in_place", "reversals", "false_starts", "swings", "corner_turns", "wall_touches", "object_touches", "wall_frames", "reason"];

    /// <summary>One walk's verdict and counts.</summary>
    internal sealed record Row(string Set, string Walk, string State, int Rungs, int Refusals, int Replans, double RouteM, double RunS, int Keys, int InPlace, int Reversals, int FalseStarts, int Swings, int CornerTurns, int WallTouches, int ObjectTouches, int WallFrames, string Reason)
    {
        public bool Arrived => State == nameof(NavRunState.Arrived);

        public string Tsv() => string.Join('\t', Set, Walk, State, I(Rungs), I(Refusals), I(Replans), F(RouteM), F(RunS), I(Keys), I(InPlace), I(Reversals), I(FalseStarts), I(Swings), I(CornerTurns), I(WallTouches), I(ObjectTouches), I(WallFrames), Reason);

        public static Row Parse(string line)
        {
            var f = line.Split('\t');
            int N(int i) => int.Parse(f[i], CultureInfo.InvariantCulture);
            double D(int i) => double.Parse(f[i], CultureInfo.InvariantCulture);
            return new Row(f[0], f[1], f[2], N(3), N(4), N(5), D(6), D(7), N(8), N(9), N(10), N(11), N(12), N(13), N(14), N(15), N(16), f.Length > 17 ? f[17] : string.Empty);
        }

        public static Row Of(string set, string walk, NavRunner runner, int wallFrames, bool done) => new(
            set, walk, done ? runner.State.ToString() : "OverTime", runner.Rungs, runner.ConfirmFailures, runner.Replans, Math.Round(runner.RouteMetres, 1), Math.Round(runner.RunSeconds, 2), runner.KeyChanges, runner.InPlacePresses,
            runner.TurnReversals, runner.ForwardRepresses, runner.Swings, runner.CornerTurns, runner.WallTouches, runner.ObjectTouches, wallFrames, (runner.FailureReason ?? string.Empty).Replace('\t', ' ').Replace('\n', ' '));

        private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

        private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }

    [WalkBenchFact]
    public void Run_the_walk_bench()
    {
        var spec = Environment.GetEnvironmentVariable(Variable)!.Split(',', StringSplitOptions.TrimEntries);
        var label = spec[0];
        var against = spec.Length > 1 && spec[1].Length > 0 ? spec[1] : null;
        var sets = Environment.GetEnvironmentVariable(SetsVariable) is { Length: > 0 } s ? s.Split(',', StringSplitOptions.TrimEntries) : AllSets;
        var rows = new List<Row>();
        foreach (var set in sets)
        {
            var began = System.Diagnostics.Stopwatch.StartNew();
            var before = rows.Count;
            rows.AddRange(set switch
            {
                "hubs-ord" => Hubs(set, predictive: false),
                "hubs-pred" => Hubs(set, predictive: true),
                "hops" => Hops(set),
                "hops-seek" => Hops(set, seek: true, handOver: false),
                "hops-use" => Hops(set, seek: true, handOver: true),
                "dungeons-ord" => Dungeons(set, predictive: false),
                "dungeons-pred" => Dungeons(set, predictive: true),
                _ => throw new ArgumentException($"{SetsVariable}: no set '{set}' (the sets: {string.Join(", ", AllSets)})"),
            });
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[bench] {set}: {rows.Count - before} walks in {began.Elapsed.TotalSeconds:0} s of wall time"));
        }

        var dir = Path.Combine(RepositoryFiles.Root, "cache", "walk-bench");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, label + ".tsv");
        File.WriteAllLines(file, [string.Join('\t', Columns), .. rows.Select(r => r.Tsv())]);
        output.WriteLine($"[bench] wrote cache/walk-bench/{label}.tsv");

        var baseRows = against is not null && File.Exists(Path.Combine(dir, against + ".tsv"))
            ? File.ReadLines(Path.Combine(dir, against + ".tsv")).Skip(1).Select(Row.Parse).ToList()
            : null;
        output.WriteLine(Report(rows, baseRows, against));
        Assert.NotEmpty(rows);
    }

    /// <summary>The totals of each set (and before and after with a base), then every walk that changed.</summary>
    internal static string Report(IReadOnlyList<Row> rows, IReadOnlyList<Row>? baseRows, string? against)
    {
        var text = new StringBuilder();
        text.AppendLine("set | walks | not arrived | rungs | refusals | replans | run s | keys | keys/min | in place | reversals | false starts | swings | corner turns | wall touches | object touches | wall frames");
        foreach (var set in rows.Select(r => r.Set).Distinct())
        {
            var now = rows.Where(r => r.Set == set).ToList();
            text.AppendLine(Totals(set, now));
            if (baseRows?.Where(r => r.Set == set).ToList() is { Count: > 0 } was)
            {
                text.AppendLine(Totals($"{set} ({against})", was));
            }
        }

        if (baseRows is null)
        {
            return text.ToString();
        }

        var old = baseRows.ToDictionary(r => (r.Set, r.Walk));
        text.AppendLine($"changed walks against {against} (verdict, rungs, refusals or replans first):");
        foreach (var r in rows)
        {
            if (!old.TryGetValue((r.Set, r.Walk), out var o))
            {
                text.AppendLine($"  new {r.Set} {r.Walk}");
                continue;
            }

            if (o.State != r.State || o.Rungs != r.Rungs || o.Refusals != r.Refusals || o.Replans != r.Replans || o.WallFrames != r.WallFrames)
            {
                text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  VERDICT {r.Set} {r.Walk}: {o.State} rungs {o.Rungs} refusals {o.Refusals} replans {o.Replans} wall frames {o.WallFrames} -> {r.State} rungs {r.Rungs} refusals {r.Refusals} replans {r.Replans} wall frames {r.WallFrames} {r.Reason}"));
            }
        }

        foreach (var r in rows)
        {
            if (old.TryGetValue((r.Set, r.Walk), out var o) && o.State == r.State && o.Rungs == r.Rungs && o.Refusals == r.Refusals && o.Replans == r.Replans && o.WallFrames == r.WallFrames
                && (o.Keys != r.Keys || o.Swings != r.Swings || o.InPlace != r.InPlace || o.WallTouches != r.WallTouches || Math.Abs(o.RunS - r.RunS) > 0.05))
            {
                text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  counts {r.Set} {r.Walk}: run {o.RunS:0.0}->{r.RunS:0.0} s keys {o.Keys}->{r.Keys} in place {o.InPlace}->{r.InPlace} reversals {o.Reversals}->{r.Reversals} false starts {o.FalseStarts}->{r.FalseStarts} swings {o.Swings}->{r.Swings} wall {o.WallTouches}->{r.WallTouches} object {o.ObjectTouches}->{r.ObjectTouches}"));
            }
        }

        return text.ToString();
    }

    private static string Totals(string name, IReadOnlyList<Row> rows)
    {
        var seconds = rows.Sum(r => r.RunS);
        var keys = rows.Sum(r => r.Keys);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name} | {rows.Count} | {rows.Count(r => !r.Arrived)} | {rows.Sum(r => r.Rungs)} | {rows.Sum(r => r.Refusals)} | {rows.Sum(r => r.Replans)} | {seconds:0.0} | {keys} | {(seconds > 0 ? keys * 60.0 / seconds : 0):0.0} | {rows.Sum(r => r.InPlace)} | {rows.Sum(r => r.Reversals)} | {rows.Sum(r => r.FalseStarts)} | {rows.Sum(r => r.Swings)} | {rows.Sum(r => r.CornerTurns)} | {rows.Sum(r => r.WallTouches)} | {rows.Sum(r => r.ObjectTouches)} | {rows.Sum(r => r.WallFrames)}");
    }

    private static StepTrace? Trace(string name) =>
        Environment.GetEnvironmentVariable(TraceVariable) == name ? new StepTrace(Path.Combine(RepositoryFiles.Root, "cache", "walk-bench", "trace-" + name.Replace('/', '-') + ".txt")) : null;

    /// <summary>A traced walk: each change of the keys, the steering that held them and where, and the runner's log.</summary>
    private sealed class StepTrace(string file) : IDisposable
    {
        private readonly StreamWriter _out = Open(file);
        private string _last = string.Empty;
        private object? _route;

        public ILogSink Log => new Lines(_out);

        public void Step(NavRunner runner, CellFrame at, double t)
        {
            var st = runner.SteeringState;
            if (st.Route is { } route && !ReferenceEquals(route, _route))
            {
                _route = route;
                var points = runner.Path?.Waypoints;
                _out.WriteLine("  route " + string.Join(" ", Enumerable.Range(0, route.Vertices.Count).Select(i => string.Create(CultureInfo.InvariantCulture, $"{i}:{(points is { } w && i < w.Count ? (w[i].At.CellId & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture) + (w[i].Doorway ? "D" : string.Empty) : string.Empty)}({route.Vertices[i].Global.X:F2},{route.Vertices[i].Global.Y:F2})"))));
            }

            var key = $"turn={st.Turn} fwd={st.Forward} corner={st.Corner} pred={runner.PredictiveHeld} next={st.Next} aim={st.Aim}";
            if (key != _last)
            {
                _last = key;
                _out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"t={t:F2} {key} at {at.CellId & 0xFFFF:X4} ({at.Origin.X:F2},{at.Origin.Y:F2},{at.Origin.Z:F2}) hd={RetailFrames.GetHeading(at.Frame):F0}"));
            }
        }

        public void Note(string line) => _out.WriteLine(line);

        public void Dispose()
        {
            NavRunner.PredictTrace = null;
            _out.Dispose();
        }

        private static StreamWriter Open(string file)
        {
            var o = new StreamWriter(file);
            NavRunner.PredictTrace = line => o.WriteLine("    P " + line);
            return o;
        }

        private sealed class Lines(StreamWriter o) : ILogSink
        {
            public bool IsEnabled(LogSeverity severity) => true;

            public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields)
            {
                var text = new StringBuilder("  LOG ").Append(message);
                foreach (var f in fields)
                {
                    text.Append(' ').Append(f.ToString());
                }

                o.WriteLine(text.ToString());
            }
        }
    }

    /// <summary>The 40 hub legs, each from its room's frame (<see cref="HubWalkDatTests"/>' legs, at 60 steps a second).</summary>
    private IEnumerable<Row> Hubs(string set, bool predictive)
    {
        var dats = fixture.Dats;
        var geometry = new PhysicsGeometryLibrary(dats);
        var structures = new CellStructureLibrary(dats);
        var animations = new DatAnimationSource(dats);
        foreach (var (coord, arrival, rooms) in new[] { (new LandblockCoord(0x00, 0x07), (ushort)0x0143, TownRooms), (new LandblockCoord(0x8A, 0x02), (ushort)0x0212, FacilityRooms) })
        {
            var lb = ((uint)coord.X << 24) | ((uint)coord.Y << 16);
            var collision = new LandblockCollisionBuilder(dats, geometry, structures).Build(coord);
            var graph = new RoomGraph(LandblockCellSet.Load(structures, coord), RoomGraph.BuildingDoorwayLean);
            foreach (var room in rooms)
            {
                foreach (var (fromLow, toLow) in new[] { (arrival, room), (room, arrival) })
                {
                    var world = new PhysicsWorld();
                    world.Attach(collision);
                    var time = new SimulatedTime();
                    using var session = GameSession.Offline(_ => { }, time, NullLogSink.Instance);
                    var now = 0.0;
                    using var audit = new MovementAudit(new NullMovementSink(), session.Events, () => now, "bench");
                    var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(OfflinePlayerWeenie.PlaceholderRunSkill), geometry, animations, sink: audit);
                    session.Actions.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
                    world.UseTime(0.0);
                    var from = lb | fromLow;
                    var to = lb | toLow;
                    var name = string.Create(CultureInfo.InvariantCulture, $"{from:X8}-{toLow:X4}");
                    Assert.True(walker.Place(new CellFrame(from, new AcFrame(graph.RoomFrame(from)!.Value.Origin + new Vector3(0f, 0f, 0.05f), Quaternion.Identity))), name);
                    using var check = new RouteConfirmer(world, walker.Physics.Setup!);
                    using var trace = Trace(set + "/" + name);
                    using var runner = new NavRunner(session.Actions.Move, new ControllerNavBody(walker.Controller), new RoomPathSource(graph), check, audit, new NavRunnerOptions { Name = "bench", ScarClock = time, PredictiveSteering = predictive }, trace?.Log ?? NullLogSink.Instance);
                    runner.Start(NavGoal.ToPoint(new NavPoint(lb, graph.RoomFrame(to)!.Value.Origin), to), now);
                    var wall = 0;
                    while (!runner.IsDone && now < 120)
                    {
                        now += 1.0 / Fps;
                        time.Advance(1.0 / Fps);
                        walker.Controller.Tick(now);
                        runner.Step(now);
                        session.Tick(now);
                        trace?.Step(runner, walker.Position, now);
                        wall += walker.Physics.CollidingWithEnvironment && runner.RouteMetres > 1f ? 1 : 0;
                    }

                    yield return Row.Of(set, name, runner, wall, runner.IsDone);
                }
            }
        }
    }

    /// <summary>The hop's front walk from each hub's arrival to every portal's front there, as the journeys run it live (<see cref="FacilityHubWalkDatTests.Rig"/>, the live runner's options).</summary>
    private IEnumerable<Row> Hops(string set) => Hops(set, seek: false, handOver: false);

    /// <summary>
    /// The hop's whole walk as the journeys run it live: from each hub's arrival to every portal's front, then the seek to within the portal's <see cref="DenTravelStation.PortalRangeMetres"/> (PortalHopLeg). With
    /// <paramref name="handOver"/> both walks carry the portal as their Use target (<see cref="NavRunner.UseTarget"/>) and, when one hands over, ACE's walk-to-use runs from there
    /// (<see cref="FacilityHubWalkDatTests.Rig.ServerWalk"/>) until the body is within the use radius. One row a hop: the walks' and the server's walk's time and counts summed (the server's walk presses no key).
    /// </summary>
    private IEnumerable<Row> Hops(string set, bool seek, bool handOver)
    {
        var routes = TravelRoutes.Default;
        var network = new CellFrame(0x00070143, new AcFrame(new Vector3(70f, -60f, 0.005f), Quaternion.Identity));
        var facility = new CellFrame(0x8A020212, new AcFrame(routes.Edge("network-to-facility-hub").DestOrigin, Quaternion.Identity));
        foreach (var (hub, drop) in new[] { ("facility-hub", facility), ("town-network", network) })
        {
            foreach (var edge in routes.Edges.Where(e => e.From == hub))
            {
                using var rig = new FacilityHubWalkDatTests.Rig(fixture.Dats, drop);
                var portal = RungPortal.Of(edge, routes);
                Row? row = null;
                var name = $"{hub}/{edge.Key}";
                var options = new NavRunnerOptions { Name = name, PredictiveSteering = true, PredictiveOutdoors = true };

                // The hop's own front (PortalHopLeg: HopFront with the front's keep-off, then the room that holds it).
                var keep = portal.KeepOffForFront();
                var cells = LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), LandblockCoord.FromId(portal.Cell));
                var (cell, at, _, _) = TravelLeg.HopFront(portal, portal.Cell, rig.Position, keep, (c, a) => LandDefs.IsOutdoorCell(c) || TravelLeg.FrontRoomIn(cells, c, a) is { Moved: 0f });
                if (!LandDefs.IsOutdoorCell(cell) && TravelLeg.FrontRoomIn(cells, cell, at) is { } room)
                {
                    (cell, at) = (room.Cell, room.At);
                }

                using var trace = Trace(set + "/" + name);
                var use = handOver ? new NavUseTarget(portal.Guid, new CellFrame(portal.Cell, new AcFrame(portal.Origin, Quaternion.Identity)), (float)DenTravelStation.PortalRangeMetres) : null;
                var handed = false;
                rig.Run(rig.PointGoal(cell, at), name, keep, options: options, measured: (runner, wall) => { row = Row.Of(set, name, runner, wall, runner.IsDone); handed = runner.HandedToUse; }, log: trace?.Log, stepped: trace is null ? null : trace.Step, use: use);
                if (seek && row!.Arrived)
                {
                    Row? second = null;
                    rig.Run(rig.SeekGoal(portal), name + " seek", portal.KeepOffTheOthers(), options: options with { Name = name + " seek" }, measured: (runner, wall) => { second = Row.Of(set, name, runner, wall, runner.IsDone); handed = runner.HandedToUse; }, log: trace?.Log, stepped: trace is null ? null : trace.Step, use: use);
                    row = Sum(row, second!);
                    if (handed)
                    {
                        var (seconds, reached, wallFrames) = rig.ServerWalk(use!.At, use.UseRadius);
                        trace?.Note(string.Create(CultureInfo.InvariantCulture, $"the server's walk: {seconds:F2} s, reached={reached}, wall frames {wallFrames}"));
                        row = row with { State = reached ? row.State : "ServerWalkShort", RunS = Math.Round(row.RunS + seconds, 2), WallFrames = row.WallFrames + wallFrames, Reason = reached ? "handed over" : "the server's walk did not reach the use radius" };
                    }
                }

                yield return row!;
            }
        }
    }

    /// <summary>Two walks of one hop as one row: times and counts added, the second's verdict.</summary>
    private static Row Sum(Row a, Row b) => b with
    {
        Rungs = a.Rungs + b.Rungs, Refusals = a.Refusals + b.Refusals, Replans = a.Replans + b.Replans, RouteM = Math.Round(a.RouteM + b.RouteM, 1), RunS = Math.Round(a.RunS + b.RunS, 2), Keys = a.Keys + b.Keys,
        InPlace = a.InPlace + b.InPlace, Reversals = a.Reversals + b.Reversals, FalseStarts = a.FalseStarts + b.FalseStarts, Swings = a.Swings + b.Swings, CornerTurns = a.CornerTurns + b.CornerTurns,
        WallTouches = a.WallTouches + b.WallTouches, ObjectTouches = a.ObjectTouches + b.ObjectTouches, WallFrames = a.WallFrames + b.WallFrames,
    };

    /// <summary>Every dungeon ground's whole tour from its arrival, leg after leg, as <see cref="IndoorRunThroughDatTests"/> walks its first twelve.</summary>
    private IEnumerable<Row> Dungeons(string set, bool predictive)
    {
        var dats = fixture.Dats;
        var geometry = new PhysicsGeometryLibrary(dats);
        foreach (var ground in GroundCatalog.All.Where(g => g.Kind == GroundKind.Dungeon))
        {
            var landblock = ground.Landblocks.Single();
            var coord = new LandblockCoord((byte)(landblock >> 8), (byte)(landblock & 0xFF));
            var world = new PhysicsWorld();
            world.Attach(new LandblockCollisionBuilder(dats, geometry).Build(coord));
            var graph = GroundRooms.GraphOf(LandblockCellSet.Load(new CellStructureLibrary(dats), coord), ground);
            var h = new ActionHarness(offline: true);
            using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, null, "bench");
            var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(FighterRunSkill), sink: audit);
            h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
            world.UseTime(0.0);
            Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(ground.Arrival.Frame, sliding: true));
            using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
            var source = new RoomPathSource(graph);
            var now = 0.0;
            foreach (var cellId in ground.Tour.Where(c => graph.RoomFrame(c) is not null).Distinct())
            {
                var goal = NavGoal.ToPoint(new NavPoint(cellId & 0xFFFF0000u, graph.RoomFrame(cellId)!.Value.Origin), cellId);
                var name = string.Create(CultureInfo.InvariantCulture, $"{ground.Key}/{cellId & 0xFFFF:X4}");
                using var trace = Trace(set + "/" + name);
                using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, new NavRunnerOptions { Name = "bench", PredictiveSteering = predictive }, trace?.Log);
                runner.Start(goal, now);
                var end = now + 120;
                var wall = 0;
                while (!runner.IsDone && now < end)
                {
                    now += 1.0 / Fps;
                    walker.Controller.Tick(now);
                    runner.Step(now);
                    h.Tick(1.0 / Fps);
                    trace?.Step(runner, walker.Position, now);
                    wall += walker.Physics.CollidingWithEnvironment && runner.RouteMetres > 1f && runner.JumpsTaken == 0 ? 1 : 0;
                }

                yield return Row.Of(set, name, runner, wall, runner.IsDone);
            }
        }
    }
}

/// <summary>Skips the walk bench unless <see cref="WalkBenchDatTests.Variable"/> is set and the dats are there; a tool of its own tier.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class WalkBenchFactAttribute : FactAttribute
{
    public WalkBenchFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WalkBenchDatTests.Variable)))
        {
            Skip = $"the walk bench: set {WalkBenchDatTests.Variable}=label[,base label] (a tool, Tier=Tool)";
        }
        else if (!Lifestone.Data.Dat.DatSet.Exists())
        {
            Skip = "the walk bench needs the retail dats";
        }
    }
}

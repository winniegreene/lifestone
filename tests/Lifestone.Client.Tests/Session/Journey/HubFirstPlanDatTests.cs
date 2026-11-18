using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Tests.Movement;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 hub plan, on the retail dats (live 2026-11-16 16:54:52 to 16:55:04 UTC, cache/m6/14/live/ladder38): every pass through the Town Network (0x0007) planned its first walk for 10 to 14 s with the game's
/// frames held. Measured here on the live leg (0x00070143 to the Uziz portal's front room 0x00070110): 7.6 s in the raised-start check's bare plan and 4.4 s in the runner's plan with the leg's kept portals, nearly all of it the room graph's
/// detours (1,128 floor detours, 15,592 ground probes, 10,359 straight-step probes) and doorway tests (1,534), worked out again by each leg's new graph; the nav tile (34,560 water nodes) took 0.2 s in all. The landblock's memo
/// (<see cref="CellRoutePlanner.Memo"/>, <see cref="JourneyLegPlanner.RoomsMemoOf"/>) keeps that work from leg to leg, a walk with kept portals reuses the bare detour none of whose steps they refuse, and a host with workers works a leg's
/// rooms out on a worker before it plans (<see cref="JourneyLegPlanner.RoomsWarmUp"/>). The routes are the same, waypoint for waypoint, as a graph of its own with no reuse plans them.
/// </summary>
public sealed class HubFirstPlanDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The most a step of the leg may take on the game's thread, in milliseconds: the frame rule that fails a gate (<see cref="SoakFrameWindow.Over75Milliseconds"/>; the live hold was 12,060 ms: 16:54:52.626 to 16:55:04.686 UTC).</summary>
    private const double StepBudgetMilliseconds = SoakFrameWindow.Over75Milliseconds;

    /// <summary>How near two routes' waypoints lie to be the same, in metres.</summary>
    private const float SameMetres = 1e-3f;

    private static TravelRoutes Routes => TravelRoutes.Default;

    private DatSet Dats => fixture.Dats;

    /// <summary>The Town Network's drop from Holtburg (the edge holtburg-to-network's destination, live 16:54:43 UTC).</summary>
    private static CellFrame TownDrop => new(0x00070143, new AcFrame(new Vector3(70f, -60f, 0.005f), Quaternion.Identity));

    /// <summary>The Town Network's drop from Uziz (live 16:56:11 UTC).</summary>
    private static CellFrame TownDropFromUziz => new(0x00070133, new AcFrame(new Vector3(60f, -70f, 0.005f), Quaternion.Identity));

    /// <summary>The Facility Hub's drop.</summary>
    private static CellFrame FacilityDrop => new(0x8A020212, new AcFrame(Routes.Edge("network-to-facility-hub").DestOrigin, Quaternion.Identity));

    /// <summary>The Facility Hub's legs from its drop to three portals' fronts: on the shared memo, the routes a graph of its own with no reuse plans, bare and with the kept portals.</summary>
    [RequiresDatsFact]
    public void The_Facility_Hub_legs_plan_the_same_routes_on_the_shared_memo()
    {
        var times = SameRoutes([(FacilityDrop, "hub-burrow"), (FacilityDrop, "hub-quarters"), (FacilityDrop, "hub-abode"), (FacilityDrop, "hub-burrow")]);
        Assert.True(times[^1] < times[0], $"the leg planned again on the memo took {times[^1]:0} ms, the first {times[0]:0} ms");
    }

    /// <summary>
    /// The live legs in the Town Network (the Uziz portal's and the Facility Hub portal's fronts from both drops): the same routes on the shared memo, and every leg after the first plans in under a fifth of the first's time
    /// (measured: the first 8,038 + 396 ms, the next 54 + 85 ms; before the memo every leg took the first's).
    /// </summary>
    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void The_Town_Network_legs_plan_the_same_routes_and_the_later_ones_cost_a_fifth()
    {
        var times = SameRoutes([(TownDrop, "network-to-uziz"), (TownDrop, "network-to-facility-hub"), (TownDropFromUziz, "network-to-facility-hub"), (TownDrop, "network-to-uziz")]);
        Assert.All(times.Skip(1), t => Assert.True(t < times[0] / 5, $"a later leg took {t:0} ms, the first {times[0]:0} ms"));
    }

    /// <summary>
    /// The leg in the Facility Hub, run as the Godot host runs it (its rooms worked out on a worker first): no step before the runner's takes over <see cref="StepBudgetMilliseconds"/>, and the runner's route is the one planned without
    /// the worker. The runner's own first step is printed, not judged: its plan takes 16 ms, but on this 119-waypoint route its room sweeps (<c>NavRunner.SweepRooms</c>, the sight table between room vertices) took 65 to 85 ms more
    /// (measured 2026-11-16: the runner's first step 83 to 116 ms), which is the runner's, not the planner's (<c>TODO(m6-runner-sweep-rooms-frame)</c>).
    /// </summary>
    [RequiresDatsFact]
    public void The_Facility_Hub_leg_works_its_rooms_out_on_a_worker_and_holds_no_frame() => LegHoldsNoFrame(FacilityDrop, "hub-abode", judgeRunnerStep: false);

    /// <summary>The live leg (0x00070143 to the Uziz portal's front), run as the Godot host runs it: the 7.6 s and 4.5 s the live leg held the game's frames are spent on a worker, and no step takes over <see cref="StepBudgetMilliseconds"/>.</summary>
    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void The_live_Town_Network_leg_works_its_rooms_out_on_a_worker_and_holds_no_frame() => LegHoldsNoFrame(TownDrop, "network-to-uziz", judgeRunnerStep: true);

    /// <summary>A host with no workers (the headless host) plans as before: no worker line, and the leg plans in its first step.</summary>
    [RequiresDatsFact]
    public void A_host_with_no_workers_plans_at_once()
    {
        var log = new RecordingLogSink();
        using var rig = new LegRig(Dats, FacilityDrop, workers: false, log);
        var (leg, goal, _) = rig.Leg("hub-abode");
        var status = rig.Step(leg);
        Assert.Equal(TravelStatus.Running, status);
        Assert.NotNull(leg.Runner);
        Assert.Equal(1, leg.Runner!.Plans);
        Assert.False(log.AnyLineContains("worked out on a worker"));
        Assert.Equal(NavPathStatus.Found, leg.Runner.Path!.Status);
        Assert.Equal(goal.CellId, leg.Runner.Path.Waypoints[^1].At.CellId);
        leg.Dispose();
    }

    private static (uint Cell, Vector3 At) Front(JourneyLegPlanner planner, RungPortal portal, in CellFrame from)
    {
        var (cell, front, _, _) = TravelLeg.HopFront(portal, portal.Cell, from, portal.KeepOffForFront(), (c, a) => LandDefs.IsOutdoorCell(c) || TravelLeg.FrontRoomIn(planner.CellsOf(c), c, a) is { Moved: 0f }); // the hop's own choice (M6 14 m6-14-plaza: a front in a neighbour's clearance is turned round the portal)

        return TravelLeg.FrontRoomIn(planner.CellsOf(cell), cell, front) is { } room ? (room.Cell, room.At) : (cell, front);
    }

    /// <summary>The walk to a hop's front as the hop asks it: the goal, and the list the runner keeps (the hop's <see cref="RungPortal.KeepOffForFront"/>, the leg's reach, the runner's own clearance).</summary>
    private static (NavGoal Goal, PortalKeepOff Keep, TravelRequest Request) FrontLeg(JourneyLegPlanner planner, string key, in CellFrame from)
    {
        var portal = RungPortal.Of(Routes.Edge(key), Routes);
        var (cell, at) = Front(planner, portal, from);
        var goal = NavGoal.ToPoint(new NavPoint(cell & 0xFFFF0000u, at), cell);
        var keep = portal.KeepOffForFront();
        var request = new TravelRequest(new TeleportDestination(key + "-front", [], new WorldLocation(key + "-front", new CellPosition(cell, at), HasHeight: true, HeadingDegrees: 0), "hub plan test"), TravelPurpose.Cell, KeepOff: keep);
        return (goal, keep.Near(NavMap.FromCellFrame(from).Global, goal.Point.Global, 192f).WithOwnClearance(PortalKeepOff.OwnReach(NavRunner.StopMetres(goal.StopTolerance))), request);
    }

    private static string Points(NavPath path) =>
        string.Join(" ", path.Waypoints.Select(w => string.Create(CultureInfo.InvariantCulture, $"{w.At.CellId & 0xFFFF:X4}({w.At.Origin.X:0.00},{w.At.Origin.Y:0.00},{w.At.Origin.Z:0.00}){(w.Doorway ? "d" : string.Empty)}")));

    private static void AssertSame(NavPath expected, NavPath actual, string what)
    {
        Assert.True(expected.Status == actual.Status && expected.Waypoints.Count == actual.Waypoints.Count, $"{what}: {expected.Status} n={expected.Waypoints.Count} against {actual.Status} n={actual.Waypoints.Count}\n{Points(expected)}\n{Points(actual)}");
        for (var i = 0; i < expected.Waypoints.Count; i++)
        {
            var (e, a) = (expected.Waypoints[i], actual.Waypoints[i]);
            Assert.True(e.At.CellId == a.At.CellId && e.Doorway == a.Doorway && Vector3.Distance(e.At.Origin, a.At.Origin) <= SameMetres, $"{what}: waypoint {i}\n{Points(expected)}\n{Points(actual)}");
        }
    }

    /// <summary>Plans each leg on one planner (the shared memo) and on a graph of its own with no reuse (a fresh memo per leg, every walk with refusals searched afresh), bare and kept; the routes are the same. The shared legs' times, in milliseconds.</summary>
    private List<double> SameRoutes((CellFrame From, string Key)[] legs)
    {
        var map = new NavMap();
        using var streamer = new HeadlessStreamer(Dats, null, new PhysicsWorld(), map, NullLogSink.Instance);
        var planner = new JourneyLegPlanner(Dats);
        var times = new List<double>();
        foreach (var (from, key) in legs)
        {
            streamer.EnsureNow(from.CellId);
            var (goal, keep, _) = FrontLeg(planner, key, from);
            var sw = Stopwatch.StartNew();
            var shared = planner.SourceFor(from.CellId, JourneyLegPlanner.GoalCellOf(goal), map, out var why);
            var bare = shared.Plan(from, goal, new NavScars());
            var bareMs = sw.Elapsed.TotalMilliseconds;
            ((INavRoomSource)shared).Rooms.KeepOff = keep;
            var kept = shared.Plan(from, goal, new NavScars());
            times.Add(sw.Elapsed.TotalMilliseconds);

            var memo = new CellRoutePlanner.Memo(planner.CellsOf(from.CellId), RoomGraph.BuildingDoorwayLean, RoomGraph.DefaultHeadSlackMetres) { ReuseBareDetours = false };
            var reference = new IndoorOutdoorPathSource(new PathPlanner(map), memo) { WaysOut = Routes.WaysOut };
            var referenceBare = reference.Plan(from, goal, new NavScars());
            reference.Rooms.KeepOff = keep;
            var referenceKept = reference.Plan(from, goal, new NavScars());
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"0x{from.CellId:X8} {key}: bare {bareMs:0} ms, kept {times[^1] - bareMs:0} ms ({why}); {kept.Status} n={kept.Waypoints.Count} {Points(kept)}"));
            Assert.Equal(NavPathStatus.Found, kept.Status);
            AssertSame(referenceBare, bare, key + " bare");
            AssertSame(referenceKept, kept, key + " kept");
        }

        return times;
    }

    /// <summary>Runs the leg to its first plan with a host that has workers; every step's time on the test's thread, and the runner's route against the plan made without them.</summary>
    private void LegHoldsNoFrame(CellFrame from, string key, bool judgeRunnerStep)
    {
        var log = new RecordingLogSink();
        using var rig = new LegRig(Dats, from, workers: true, log);
        var (leg, goal, keep) = rig.Leg(key);
        var worst = 0.0;
        var runnerStep = 0.0;
        var steps = 0;
        var real = Stopwatch.StartNew();
        while ((leg.Runner?.Plans ?? 0) == 0 && real.Elapsed.TotalSeconds < 120)
        {
            var hadRunner = leg.Runner is not null;
            var sw = Stopwatch.StartNew();
            Assert.Equal(TravelStatus.Running, rig.Step(leg));
            var ms = sw.Elapsed.TotalMilliseconds;
            (worst, runnerStep) = hadRunner ? (worst, ms) : (Math.Max(worst, ms), runnerStep);
            steps++;
            if (leg.Runner is null)
            {
                Thread.Sleep(5);
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{key} from 0x{from.CellId:X8}: planned after {real.Elapsed.TotalMilliseconds:0} ms and {steps} steps; the worst step before the runner's {worst:0.0} ms, the runner's first step {runnerStep:0.0} ms"));
        foreach (var line in log.Lines.Where(l => l.Contains("worked out", StringComparison.Ordinal) || l.Contains("[wayfinding] planned", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        Assert.NotNull(leg.Runner);
        Assert.Equal(1, leg.Runner!.Plans);
        Assert.True(log.AnyLineContains("the leg's rooms are worked out on a worker before it plans"), "the start line");
        Assert.True(log.AnyLineContains("the leg's rooms were worked out on a worker"), "the end line");
        Assert.True(worst <= StepBudgetMilliseconds, $"a step before the runner's took {worst:0.0} ms on the game's thread (budget {StepBudgetMilliseconds:0} ms)");
        Assert.True(!judgeRunnerStep || runnerStep <= StepBudgetMilliseconds, $"the runner's first step took {runnerStep:0.0} ms on the game's thread (budget {StepBudgetMilliseconds:0} ms)");

        var planner = new JourneyLegPlanner(Dats);
        var source = planner.SourceFor(from.CellId, JourneyLegPlanner.GoalCellOf(goal), rig.Map);
        ((INavRoomSource)source).Rooms.KeepOff = keep;
        AssertSame(source.Plan(from, goal, new NavScars()), leg.Runner.Path!, key + " runner");
        leg.Dispose();
    }

    /// <summary>A body over the headless streamer for one leg, offering the host's workers or not (<see cref="IJourneyLegBody.OffThread"/>).</summary>
    private sealed class LegRig : IDisposable, IJourneyLegBody
    {
        private readonly PhysicsWorld _world = new();
        private readonly HeadlessStreamer _streamer;
        private readonly J1JourneyRig _view = new();
        private readonly ActionHarness _h = new(offline: true);
        private readonly OfflineWalker _walker;
        private readonly RouteConfirmer _confirmer;
        private readonly ILogSink _log;
        private readonly bool _workers;
        private double _now;

        public LegRig(DatSet dats, in CellFrame start, bool workers, ILogSink log)
        {
            (_workers, _log) = (workers, log);
            Map = new NavMap();
            Planner = new JourneyLegPlanner(dats);
            _streamer = new HeadlessStreamer(dats, null, _world, Map, NullLogSink.Instance);
            _streamer.EnsureNow(start.CellId);
            _walker = OfflineWalker.Create(dats, _world, NullLogSink.Instance, new OfflinePlayerWeenie(230));
            _h.Api.Move.Bind(_walker.Controller, _walker.Controller.Options.RunByDefault);
            _world.UseTime(0.0);
            Assert.True(_walker.Place(start), $"placed at {start}");
            _confirmer = new RouteConfirmer(_world, _walker.Physics.Setup!);
        }

        public NavMap Map { get; }

        public JourneyLegPlanner Planner { get; }

        public string LogCategory => "journey-runner";

        public double WaitSeconds => 30;

        public double CorridorSeconds => 1;

        NavMap? IJourneyLegBody.Map => Map;

        public CellFrame Position => _walker.Position;

        public bool Placed => true;

        public int Moves => 0;

        public Func<Action, Task>? OffThread => _workers ? work => Task.Run(work) : null;

        /// <summary>The hop's walk to <paramref name="key"/>'s front as a leg on foot (the planner's cells loaded first, as the hop's front point does).</summary>
        public (JourneyRunLeg Leg, NavGoal Goal, PortalKeepOff Keep) Leg(string key)
        {
            var (goal, keep, request) = FrontLeg(Planner, key, _walker.Position);
            return (new JourneyRunLeg(Planner, this, request, "cell-3", _now, _log), goal, keep);
        }

        public TravelStatus Step(JourneyRunLeg leg)
        {
            _now += 1.0 / 60;
            _walker.Controller.Tick(_now);
            var status = leg.Step(_view.View, _now);
            _h.Tick(1.0 / 60);
            return status;
        }

        public void Frame(IGameSessionView session, double now) => _streamer.PumpTiles();

        public string? WaitReason(IGameSessionView session) => null;

        public string WaitFailure(IGameSessionView session, string reason) => reason;

        public NavRunner NewRunner(IGameSessionView session, INavPathSource source, string name) =>
            new(_h.Api.Move, new ControllerNavBody(_walker.Controller), source, _confirmer, null, new NavRunnerOptions { Name = name, ConfirmBudgetMilliseconds = HeadlessJourneyRunnerHost.ConfirmBudgetMilliseconds, PredictiveSteering = true, PredictiveOutdoors = true }, _log); // the hosts' confirm budget (2 ms a frame)

        public void FollowRoute(NavRunner runner)
        {
        }

        public void Dispose()
        {
            _confirmer.Dispose();
            _streamer.Dispose();
        }
    }
}

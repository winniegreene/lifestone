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
using Lifestone.Client.Tests.Session;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 5 F1 (review 5-R5): the walks to Brighteyes the Tailor (wcid 22642, landblock_instance guid 0x7F68200D, indoors in cell 0xF682011C at (175.994, 30.9674, 63.605); facts.md section 1), an indoor goal reached from outdoors (item 4's
/// portal-front lesson), planned and confirmed as the quest station's walk is (<c>QuestStepLeg.WalkWithRecovery</c>: <see cref="TravelPurpose.Cell"/> with the NPC's guid within <see cref="QuestStepLeg.UseRange"/>, over the journeys' own path
/// source and the headless streamer's tiles, then the walker's physics): (a) from the Refuge arrival 0xF6820033 (the turn-in from=home, 35.7 m), (b) from the Burrow exit's drop 0xF9820039 (181.6, 2.6, 12.005; the tusker-burrow profile's exit
/// portal 22664 destination), the errand's from=ground leg across the island. A walk no plan reaches is green only when it is pinned in <see cref="Known"/> with its TODO and the cause (one line each: <c>brighteyes-walk name= metres= pass=</c>);
/// the pin fails when the walk reaches, so a fix removes its row. Its own file beside <see cref="TownNetworkWalkDatTests"/>, whose helpers it repeats (that file is the movement slice's). M6 7 A closed both pins (D62):
/// the Refuge's hut doors open on a covered walk whose stairs leave at right angles, and <see cref="LaneStairs"/> walks them, 44.8 m from the arrival and 616.6 m from the Burrow exit's drop.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class BrighteyesWalkDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The horizons a walk may take.</summary>
    private const int MaxHops = 24;

    private const uint BrighteyesGuid = 0x7F68200D;

    private const uint BrighteyesCell = 0xF682011C;

    private static readonly Vector3 BrighteyesAt = new(175.994f, 30.9674f, 63.605f);

    /// <summary>The walks no plan reaches on this base, each with its TODO and the cause: none since M6 7 A.</summary>
    private static readonly Dictionary<string, string> Known = new();

    private DatSet Dats => fixture.Dats;

    private static TravelRoutes Routes => TravelRoutes.Default;

    [RequiresDatsTheory]
    [InlineData("refuge-arrival")]
    [InlineData("burrow-exit")]
    public void Brighteyes_is_reached_from_the_refuge_arrival_and_from_the_burrow_exit(string name)
    {
        var from = name == "refuge-arrival"
            ? Routes.Edges.Where(e => e.DestCell == 0xF6820033).Select(e => new CellFrame(e.DestCell, new AcFrame(e.DestOrigin, Quaternion.Identity))).First()
            : new CellFrame(0xF9820039, new AcFrame(new Vector3(181.6f, 2.6f, 12.005f), Quaternion.Identity));
        using var rig = Rig(from);
        var clock = Stopwatch.StartNew();
        var goal = rig.QuestGoal(BrighteyesGuid, BrighteyesCell, BrighteyesAt, QuestStepLeg.UseRange);
        var walk = Walk(rig, from, goal);
        var line = string.Create(CultureInfo.InvariantCulture, $"brighteyes-walk name={name} from=0x{from.CellId:X8} to=0x{BrighteyesCell:X8} metres={walk.Metres:0.0} plan_ms={clock.Elapsed.TotalMilliseconds:0} pass={(walk.Ok ? "yes" : "no")}");
        output.WriteLine(line);
        if (!walk.Ok)
        {
            output.WriteLine($"the walk failed: {walk.Why}");
            Assert.True(Known.TryGetValue(name, out var todo), line + " " + walk.Why);
            output.WriteLine($"pinned: {todo}");
            return;
        }

        Assert.False(Known.ContainsKey(name), $"{line}: reached, so the pin {Known.GetValueOrDefault(name)} is stale: remove it from Known");
        Assert.Equal(BrighteyesCell >> 16, walk.End.CellId >> 16);
        Assert.True(Vector2.Distance(new Vector2(walk.End.Origin.X, walk.End.Origin.Y), new Vector2(BrighteyesAt.X, BrighteyesAt.Y)) <= QuestStepLeg.UseRange + 0.5, $"the walk ended at {walk.End}");
    }

    /// <summary>
    /// M6 7 A: the runner's own feet up the 1.5 m wide flight, from the Refuge arrival into her room: the journey's source, the walker's physics and no rung, so a body that drifts off the stairs fails here, not at gate A.
    /// </summary>
    [RequiresDatsFact]
    public void The_walker_climbs_the_turned_stairs_to_Brighteyes_with_no_rung()
    {
        var from = Routes.Edges.Where(e => e.DestCell == 0xF6820033).Select(e => new CellFrame(e.DestCell, new AcFrame(e.DestOrigin, Quaternion.Identity))).First();
        using var rig = Rig(from);
        var h = new ActionHarness(offline: true);
        var now = 0.0;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, "brighteyes");
        var walker = OfflineWalker.Create(Dats, rig.World, NullLogSink.Instance, sink: audit);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        rig.World.UseTime(0.0);
        Assert.True(walker.Place(from), $"placed at {from}");
        var goal = rig.QuestGoal(BrighteyesGuid, BrighteyesCell, BrighteyesAt, QuestStepLeg.UseRange);
        rig.Streamer.EnsureNow(from.CellId);
        var source = rig.Planner.SourceFor(from.CellId, JourneyLegPlanner.GoalCellOf(goal), rig.Map);
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, rig.Confirmer, audit, new NavRunnerOptions { Name = "brighteyes", ConfirmBudgetMilliseconds = 2 });
        runner.Start(goal, now);
        var top = float.MinValue;
        while (!runner.IsDone && now < 120.0)
        {
            now += 1.0 / 60.0;
            rig.Streamer.Pump();
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / 60.0);
            top = MathF.Max(top, walker.Position.Origin.Z);
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"brighteyes-run {runner.State} t={now:F1} rungs={runner.Rungs} planned={runner.RouteMetres:F1} top_z={top:F2} end=0x{walker.Position.CellId:X8} {walker.Position.Origin}"));
        Assert.True(runner.State == NavRunState.Arrived, $"{runner.State}: {runner.FailureReason} at 0x{walker.Position.CellId:X8} {walker.Position.Origin}");
        Assert.Equal(0, runner.Rungs);
        Assert.True(walker.Position.CellId == BrighteyesCell || walker.Position.CellId == 0xF682011B, $"the run ended in 0x{walker.Position.CellId:X8}");
    }

    /// <summary>M6 7 A: the way back: from beside her out through the door, down the stairs and to the Refuge arrival, planned and confirmed (the errand's way out of the room).</summary>
    [RequiresDatsFact]
    public void The_walk_back_from_Brighteyes_comes_down_the_turned_stairs()
    {
        var from = new CellFrame(BrighteyesCell, new AcFrame(BrighteyesAt with { Y = BrighteyesAt.Y + 0.8f }, Quaternion.Identity));
        var arrival = Routes.Edges.First(e => e.DestCell == 0xF6820033);
        using var rig = Rig(from);
        var goal = rig.PointGoal(arrival.DestCell, arrival.DestOrigin);
        var walk = Walk(rig, from, goal);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"brighteyes-walk name=back metres={walk.Metres:0.0} pass={(walk.Ok ? "yes" : "no")} {walk.Why}"));
        Assert.True(walk.Ok, walk.Why);
    }

    /// <summary>M6 7 A: a start up on the covered walk (a replan or a resume; the tile is the ground, so a plan from the node below walks into the stairs) goes down the stairs first, to her room and to the ground alike.</summary>
    [RequiresDatsFact]
    public void A_start_on_the_covered_walk_is_walked_down_the_stairs_first()
    {
        var from = new CellFrame(0xF682003A, new AcFrame(new Vector3(178.5f, 35.5f, 63.6f), Quaternion.Identity));
        using var rig = Rig(from);
        var walk = Walk(rig, from, rig.QuestGoal(BrighteyesGuid, BrighteyesCell, BrighteyesAt, QuestStepLeg.UseRange));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"brighteyes-walk name=on-the-walk metres={walk.Metres:0.0} pass={(walk.Ok ? "yes" : "no")} {walk.Why}"));
        Assert.True(walk.Ok, walk.Why);

        var arrival = Routes.Edges.First(e => e.DestCell == 0xF6820033);
        var down = Walk(rig, from, rig.PointGoal(arrival.DestCell, arrival.DestOrigin));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"brighteyes-walk name=on-the-walk-down metres={down.Metres:0.0} pass={(down.Ok ? "yes" : "no")} {down.Why}"));
        Assert.True(down.Ok, down.Why);
    }

    /// <summary>
    /// A walk as the journey's leg plans it and its runner answers a refusal: the plan, and while it is a horizon (confirmed) the streamer's focus moved to its end and the plan again; a plan the
    /// physics refuses is planned again (at most <see cref="NavRunner.MaxConfirmFailures"/> refusals in a row) without the room the refused sample was on its way into (the runner's <c>AvoidCellOf</c>)
    /// or, outdoors, with a scar of <see cref="NavRunner.StuckScarMetres"/> at the refused sample; the last plan whole and confirmed.
    /// </summary>
    private static WalkResult Walk(StreamRig rig, CellFrame from, NavGoal goal)
    {
        rig.Streamer.EnsureNow(from.CellId);
        var source = rig.Planner.SourceFor(from.CellId, JourneyLegPlanner.GoalCellOf(goal), rig.Map);
        var scars = new NavScars();
        var at = from;
        var metres = 0.0;
        var refusals = 0;
        var notes = new List<string>();
        for (var plans = 0; plans < MaxHops; plans++)
        {
            var path = source.Plan(at, goal, scars);
            if (path.Status is not (NavPathStatus.Found or NavPathStatus.Horizon))
            {
                return new WalkResult(false, metres, at, $"{path.Status} {path.Reason}{Notes(notes)}");
            }

            if (rig.Confirm(path, goal) != RouteConfirmStatus.Confirmed)
            {
                var failure = rig.Confirmer.Failure!.Value;
                notes.Add($"refused: {failure}; cells {string.Join(' ', path.CellsEntered.Select(c => c.ToString("X8", CultureInfo.InvariantCulture)))}");
                if (++refusals >= NavRunner.MaxConfirmFailures)
                {
                    return new WalkResult(false, metres, at, $"the physics refused the path {refusals} times{Notes(notes)}");
                }

                if (NavRunner.AvoidCellOf(path.Waypoints, failure, goal) is { } cell && source is INavCellAvoider avoider && avoider.Avoid(cell))
                {
                    notes.Add($"room 0x{cell:X8} left out");
                }
                else if (LandDefs.IsOutdoorCell(failure.At.CellId))
                {
                    scars.Add(NavMap.FromCellFrame(failure.At), NavRunner.StuckScarMetres);
                }

                continue;
            }

            refusals = 0;
            metres += Length(path);
            if (path.Status == NavPathStatus.Found)
            {
                return new WalkResult(true, metres, path.Waypoints[^1].At, null);
            }

            at = path.Waypoints[^1].At;
            rig.Streamer.EnsureNow(at.CellId);
        }

        return new WalkResult(false, metres, at, $"no whole plan in {MaxHops} plans{Notes(notes)}");
    }

    private static string Notes(List<string> notes) => notes.Count == 0 ? string.Empty : " (" + string.Join("; ", notes) + ")";

    private static double Length(NavPath path) => path.Waypoints.Zip(path.Waypoints.Skip(1)).Sum(p => Vector2.Distance(p.First.Point.Global, p.Second.Point.Global));

    private StreamRig Rig(CellFrame start)
    {
        var world = new PhysicsWorld();
        var map = new NavMap();
        var streamer = new HeadlessStreamer(Dats, null, world, map, NullLogSink.Instance);
        streamer.EnsureNow(start.CellId);
        world.UseTime(0.0);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230));
        return new StreamRig(new JourneyLegPlanner(Dats), new J1JourneyRig(), map, streamer, new RouteConfirmer(world, walker.Physics.Setup!), world, walker.Physics.Setup!.Spheres[0]);
    }

    private sealed record WalkResult(bool Ok, double Metres, CellFrame End, string? Why);

    /// <summary>The journeys' leg planner over the headless streamer's tiles and collision, with the walker's confirmer on the human body (as <c>GoalTileDatTests</c>'s).</summary>
    private sealed class StreamRig(JourneyLegPlanner planner, J1JourneyRig view, NavMap map, HeadlessStreamer streamer, RouteConfirmer confirmer, PhysicsWorld world, PhysSphere foot) : IDisposable
    {
        public PhysicsWorld World { get; } = world;

        public PhysSphere Foot { get; } = foot;

        public JourneyLegPlanner Planner { get; } = planner;

        public NavMap Map { get; } = map;

        public HeadlessStreamer Streamer { get; } = streamer;

        public RouteConfirmer Confirmer { get; } = confirmer;

        /// <summary>The hop leg's room of an indoor front point (<c>TravelLeg.FrontRoomIn</c> over the journeys' cells of its landblock).</summary>
        public (uint Cell, Vector3 At) FrontRoom(uint cell, Vector3 at, out bool inNoRoom)
        {
            inNoRoom = false;
            if (LandDefs.IsOutdoorCell(cell))
            {
                return (cell, at);
            }

            var room = TravelLeg.FrontRoomIn(Planner.CellsOf(cell), cell, at);
            inNoRoom = room is null;
            return room is { } r ? (r.Cell, r.At) : (cell, at);
        }

        /// <summary>The hop's walk to its front point (<c>WalkToPoint</c>, <see cref="TravelPurpose.Cell"/>, no range).</summary>
        public NavGoal PointGoal(uint cell, Vector3 point) => Planner.GoalOf(view.View, new TravelRequest(Destination(cell, point), TravelPurpose.Cell));

        /// <summary>The quest station's walk to an NPC (<c>QuestStepLeg.WalkWithRecovery</c>'s request): the cell and point, the NPC's guid, within <paramref name="range"/>.</summary>
        public NavGoal QuestGoal(uint guid, uint cell, Vector3 point, double range) => Planner.GoalOf(view.View, new TravelRequest(Destination(cell, point), TravelPurpose.Cell, guid, range));

        /// <summary>The runner's pre-check of a plan: a horizon to its end, a whole path up to the goal's stop tolerance.</summary>
        public RouteConfirmStatus Confirm(NavPath path, NavGoal goal) =>
            path.Status == NavPathStatus.Failed ? RouteConfirmStatus.Failed
            : Confirmer.ConfirmAll(path.Waypoints, path.Status == NavPathStatus.Horizon ? 0f : goal.StopTolerance);

        /// <inheritdoc/>
        public void Dispose()
        {
            Confirmer.Dispose();
            Streamer.Dispose();
        }

        private static TeleportDestination Destination(uint cell, Vector3 point) =>
            new("hop", [], new WorldLocation("hop", new CellPosition(cell, point), HasHeight: true, HeadingDegrees: 0), "walk test");
    }
}

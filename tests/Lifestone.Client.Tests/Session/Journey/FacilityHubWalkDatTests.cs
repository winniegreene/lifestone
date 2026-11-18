using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Motion;
using Lifestone.Client.Avatars;
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
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 7 J (<c>m6-facility-hub-rooms</c>, D70), on the retail dats: the walk from the Facility Hub's drop 0x8A020212 down to its portals, the walk that takes the island's surface run off the climb from level 15 (the rehearsal's level 30 mage took 86 damage in
/// three hits on it). The hub's drop stands 18 m over its portal hall behind two floorless cells: 0x8A020211 over 0x8A0201C9 and 0x8A0201C1 over 0x8A02015F, each carried by a static object 0x02000623, a block 6 m high whose top is the landing and a ramp off it, 12 m long, solid to the floor.
/// </summary>
/// <remarks>
/// The world rows (the pinned world SQL v0.9.296, <c>landblock_instance</c> of landblock 0x8A02): two doors stand on the walk, both weenie 15451 'doorapartment' (Setup 0x02000C9D; no Locked row, weenie_properties_bool has no type 3, so they open to a Use), guid 0x78A020C7 at
/// (60, -85.245, 6) in 0x8A020214 and guid 0x78A0209C at (60, -64.75, 0) in 0x8A0201C4. The offline body walks through them as if open (the walker has no server objects), so here their crossings are asserted on the plan and the live Use stays the runner's.
/// Two NPCs stand near the ramps' feet and are not in this rig either (<c>TODO(m6-npc-indoor-bodies)</c>): the Steward 42134, guid 0x78A020C9 at (60.85, -67.30, 0) in 0x8A0201C8, and Curmudgeon the Drudge 42131, guid 0x78A020CA at (58.74, -37.34, -6) in 0x8A02015E.
/// </remarks>
[Trait("Tier", "Exhaustive")]
public sealed class FacilityHubWalkDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const double Fps = 60.0;
    private const double WalkSeconds = 120.0;
    private const uint Hub = 0x8A020000;
    private const uint Drop = 0x8A020212;
    private const uint DropDoor = 0x8A020214;
    private const uint UpperLanding = 0x8A020211;
    private const uint UpperRamp = 0x8A0201C9;
    private const uint UpperFoot = 0x8A0201C8;
    private const uint MiddleDoor = 0x8A0201C7;
    private const uint LowerLanding = 0x8A0201C1;
    private const uint LowerRamp = 0x8A02015F;
    private const uint LowerFoot = 0x8A02015E;
    private const uint Hall = 0x8A0201C2;

    /// <summary>The door Setup (weenie_properties_d_i_d object_Id 15451 type 1).</summary>
    private const uint DoorSetup = 0x02000C9D;

    private static TravelRoutes Routes => TravelRoutes.Default;

    private DatSet Dats => fixture.Dats;

    /// <summary>Every edge out of the hub: the eight the island routes use and the way home by Eastham.</summary>
    public static IEnumerable<object[]> HubEdges() => Routes.Edges.Where(e => e.From == "facility-hub").Select(e => new object[] { e.Key });

    /// <summary>The landings' floors are the statics' and each ramp is one run: the doorways onto them are walked, the openings that look over the drop are not, and the hub holds these two runs and no other.</summary>
    [RequiresDatsFact]
    public void The_hubs_landings_stand_on_a_static_and_each_ramp_is_a_run_from_landing_to_foot()
    {
        var cells = LandblockCellSet.Load(new CellStructureLibrary(Dats), LandblockCoord.FromId(Hub));
        var graph = new RoomGraph(cells);
        var planner = graph.Planner;
        foreach (var (door, landing, ramp, foot, height) in new[] { (DropDoor, UpperLanding, UpperRamp, UpperFoot, 6f), (MiddleDoor, LowerLanding, LowerRamp, LowerFoot, 0f) })
        {
            var cell = cells.GetCell(landing)!;
            Assert.Empty(cells.GetStructure(cell).Floors);
            Assert.Contains(cells.GetCell(ramp)!.StaticObjects, o => o.ModelId == 0x02000623);
            var into = cells.GetCell(door)!;
            Assert.True(planner.IsDoorway(into, into.Portals.Single(p => p.OtherCellId == landing)));
            Assert.True(planner.IsDoorway(cell, cell.Portals.Single(p => p.OtherCellId == door)));

            var run = Assert.Single(planner.RunsFrom(landing));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"run 0x{landing:X8} to 0x{run.ToCell:X8}: {run.Length:0.0} m, ({run.Start.Origin.X:0.00}, {run.Start.Origin.Y:0.00}, {run.Start.Origin.Z:0.00}) to ({run.End.Origin.X:0.00}, {run.End.Origin.Y:0.00}, {run.End.Origin.Z:0.00}), {run.Points.Count} points over {string.Join(' ', run.Points.Select(p => (p.CellId & 0xFFFF).ToString("X3", CultureInfo.InvariantCulture)).Distinct())}"));
            Assert.Equal(foot, run.ToCell);
            Assert.InRange(run.Start.Origin.Z, height - 0.1f, height + 0.1f);
            Assert.InRange(run.End.Origin.Z, height - 6.1f, height - 5.9f);
            Assert.InRange(run.Length, 14f, 17f);
            Assert.Contains(run.Points, p => p.CellId == ramp);

            var back = Assert.Single(planner.RunsFrom(foot));
            Assert.Equal((landing, run.End, run.Start), (back.ToCell, back.Start, back.End));
            Assert.Empty(planner.RunsFrom(ramp));
        }

        var over = cells.GetCell(UpperLanding)!;
        Assert.False(planner.IsWalkable(over, over.Portals.Single(p => p.OtherCellId == 0x8A020210)), "the upper landing's far opening looks over the ramp, 3 m over it at its sill");
        Assert.Equal(4, cells.Ids.Sum(id => planner.RunsFrom(id).Count));
    }

    /// <summary>A body runs from the drop to the edge's front point and on to the portal, down both ramps, by the leg's own planner, with no rung of the stuck ladder and no frame against a wall.</summary>
    /// <param name="edgeKey">The hub edge.</param>
    [RequiresDatsTheory]
    [MemberData(nameof(HubEdges))]
    public void The_body_runs_from_the_drop_to_the_portal_with_no_rung_and_no_wall_frame(string edgeKey)
    {
        var edge = Routes.Edge(edgeKey);
        using var rig = new Rig(Dats, new CellFrame(edge.From == "facility-hub" ? Drop : 0, new AcFrame(DropPoint, Quaternion.Identity)));
        var end = rig.RunToPortal(edge, "drop");
        output.WriteLine(end.Line);
        if (edgeKey == "facility-hub-to-blackmire")
        {
            // M6 villa: TODO(villa-blackmire-portal-ground) was pinned here as failing. Merged onto m6/14 (the climber's walker), the body run arrives: the front leg clean (101.5 m, no rung), the seek leg's last 7.9 m with
            // 2 rungs of the stuck ladder and no wall frame. The hop theory's confirmer still finds no ground at the portal (TownNetworkWalkDatTests.Known keeps the TODO), so the Stinging cave's route keeps it too.
            // Pinned as it stands: a change in either number is a finding.
            Assert.True(end.Arrived, end.Line);
            Assert.Equal(2, end.Rungs);
            Assert.Equal(0, end.WallFrames);
            output.WriteLine("arrived with 2 rungs; the hop theory still pins TODO(villa-blackmire-portal-ground)");
            return;
        }

        Assert.True(end.Arrived, end.Line);
        Assert.Equal(0, end.Rungs);
        Assert.Equal(0, end.WallFrames);
    }

    /// <summary>
    /// A body that stands anywhere on a ramp (a stuck replan, a leg that begins there) walks on down to the portal: the room graph starts it in the cell it stands in. A run is an edge from its ends only, so high on a ramp the plan's next point is the doorway's approach on the cell's floor, under the ramp,
    /// and the body takes a rung or two to pass it: pinned at two.
    /// </summary>
    /// <param name="y">How far along the ramp, landblock-local.</param>
    /// <param name="z">Its height there, near enough for the walker's placement.</param>
    /// <param name="cell">The cell a body stands in there.</param>
    [RequiresDatsTheory]
    [InlineData(-80f, 5.8f, UpperRamp)]
    [InlineData(-76f, 3.6f, UpperRamp)]
    [InlineData(-72f, 1.6f, UpperFoot)]
    [InlineData(-50f, -0.2f, LowerRamp)]
    [InlineData(-42f, -4.4f, LowerFoot)]
    public void A_body_on_a_ramp_walks_on_down_to_the_portal(float y, float z, uint cell)
    {
        var edge = Routes.Edge("hub-burrow");
        using var rig = new Rig(Dats, new CellFrame(cell, new AcFrame(new Vector3(60f, y, z), Quaternion.Identity)));
        var end = rig.RunToPortal(edge, "ramp");
        output.WriteLine(end.Line);
        Assert.True(end.Arrived, end.Line);
        Assert.InRange(end.Rungs, 0, 2);
    }

    /// <summary>The runs lead up as well as down: a body in the hall under the drop runs back up the ramp to the drop's room (no route asks it; a walk the graph offers must stand).</summary>
    [RequiresDatsFact]
    public void A_body_runs_back_up_from_the_hall_to_the_drops_room()
    {
        var start = new CellFrame(Hall, new AcFrame(new Vector3(60f, -60f, 0f), Quaternion.Identity));
        using var rig = new Rig(Dats, start);
        var end = rig.Run(rig.PointGoal(Drop, DropPoint with { X = 61f, Y = -87f }), "up");
        output.WriteLine(end.Line);
        Assert.True(end.Arrived, end.Line);
        Assert.Equal(0, end.Rungs);
    }

    /// <summary>
    /// The two doors on the walk (the remarks' rows) are crossings of the plan: the runner stops before each and Uses it. The plan is the hop's own, from the drop to the Burrow portal's front, and the walk from the Network's side crosses them in the order they stand.
    /// </summary>
    [RequiresDatsFact]
    public void The_plan_from_the_drop_crosses_both_doors_and_stops_before_each()
    {
        var edge = Routes.Edge("hub-burrow");
        var from = new CellFrame(Drop, new AcFrame(DropPoint, Quaternion.Identity));
        using var rig = new Rig(Dats, from);
        var goal = rig.FrontGoal(edge, from);
        var source = rig.Planner.SourceFor(from.CellId, JourneyLegPlanner.GoalCellOf(goal), rig.Map);
        var path = source.Plan(from, goal, new NavScars());
        Assert.Equal(NavPathStatus.Found, path.Status);

        var radius = DoorCrossings.SetupExtents(Dats)(DoorSetup) ?? DoorCrossings.UnknownRadiusMetres;
        NavDoor[] doors =
        [
            new(0x78A020C7, new CellFrame(DropDoor, new AcFrame(new Vector3(60f, -85.245f, 6f), Quaternion.Identity)), radius, Open: false, Locked: null),
            new(0x78A0209C, new CellFrame(0x8A0201C4, new AcFrame(new Vector3(60f, -64.75f, 0f), Quaternion.Identity)), radius, Open: false, Locked: null),
        ];
        var crossings = DoorCrossings.Map(doors, source);
        Assert.Equal(2, crossings.Count);
        var steps = DoorCrossings.StepsOf(path, crossings, standOffMetres: 1.5f);
        foreach (var s in steps)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"door 0x{s.Guid:X8} cell 0x{s.Crossing.CellId:X8} cross_arc={s.CrossArc:0.0} stop_arc={s.StopArc:0.0} rule={s.StopRule}"));
        }

        Assert.Equal([0x78A020C7u, 0x78A0209Cu], steps.Select(s => s.Guid));
    }

    /// <summary>The drop's point (the edge's own row, network-to-facility-hub's destination).</summary>
    private static Vector3 DropPoint => Routes.Edge("network-to-facility-hub").DestOrigin;

    /// <summary>How a body's run ended.</summary>
    /// <param name="Arrived">True at the goal.</param>
    /// <param name="Rungs">The stuck ladder's rungs.</param>
    /// <param name="WallFrames">The frames against a wall.</param>
    /// <param name="Line">The run in words.</param>
    /// <param name="Nearest">The nearest the body came to a kept portal (M6 7 AI).</param>
    /// <param name="Reason">Why a run that did not arrive ended.</param>
    /// <param name="OwnNearest">M6 14: the nearest the body came to the walk's own portal (<see cref="NavRunner.OwnNearestMetres"/>).</param>
    internal sealed record RunEnd(bool Arrived, int Rungs, int WallFrames, string Line, float Nearest = float.PositiveInfinity, string? Reason = null, float OwnNearest = float.PositiveInfinity);

    /// <summary>A body over the headless streamer's hub, walked by the runner over the leg's own planner.</summary>
    internal sealed class Rig : IDisposable
    {
        private readonly HeadlessStreamer _streamer;
        private readonly PhysicsWorld _world = new();
        private readonly J1JourneyRig _view = new();
        private readonly ActionHarness _h = new(offline: true);
        private readonly MovementAudit _audit;
        private readonly OfflineWalker _walker;
        private double _now;

        public Rig(DatSet dats, in CellFrame start, int runSkill = 230)
        {
            Map = new NavMap();
            Planner = new JourneyLegPlanner(dats);
            _streamer = new HeadlessStreamer(dats, null, _world, Map, NullLogSink.Instance);
            _streamer.EnsureNow(start.CellId);
            _audit = new MovementAudit(new RecordingMovementSink(), _h.R.Bus, () => _now, "hub");
            _walker = OfflineWalker.Create(dats, _world, NullLogSink.Instance, new OfflinePlayerWeenie(runSkill), sink: _audit);
            _h.Api.Move.Bind(_walker.Controller, _walker.Controller.Options.RunByDefault);
            _world.UseTime(0.0);
            Assert.True(_walker.Place(start), $"placed at {start}");
        }

        public NavMap Map { get; }

        public JourneyLegPlanner Planner { get; }

        public NavGoal PointGoal(uint cell, Vector3 point) => Planner.GoalOf(_view.View, new TravelRequest(Destination(cell, point), TravelPurpose.Cell));

        /// <summary>The hop's goal: its front point in the room that holds it.</summary>
        public NavGoal FrontGoal(TravelEdge edge, in CellFrame from)
        {
            var portal = RungPortal.Of(edge, Routes);
            var (cell, front, _) = TravelLeg.FrontOf(portal, portal.Cell, from);
            if (TravelLeg.FrontRoomIn(Planner.CellsOf(cell), cell, front) is { } room)
            {
                (cell, front) = (room.Cell, room.At);
            }

            return PointGoal(cell, front);
        }

        /// <summary>The hop's two walks: to the front point, then the seek to within the portal's 2 m.</summary>
        public RunEnd RunToPortal(TravelEdge edge, string from)
        {
            var portal = RungPortal.Of(edge, Routes);
            var front = Run(FrontGoal(edge, _walker.Position), $"{edge.Key} from the {from}: front");
            if (!front.Arrived)
            {
                return front;
            }

            var seek = Run(SeekGoal(portal), $"{edge.Key} from the {from}: seek");
            return seek with { Rungs = seek.Rungs + front.Rungs, WallFrames = seek.WallFrames + front.WallFrames, Line = front.Line + "\n" + seek.Line };
        }

        /// <summary>The hop's second walk: to within the portal's 2 m.</summary>
        public NavGoal SeekGoal(RungPortal portal) => Planner.GoalOf(_view.View, new TravelRequest(Destination(portal.Cell, portal.Origin), TravelPurpose.Seek, portal.Guid, DenTravelStation.PortalRangeMetres));

        /// <summary>Where the body stands.</summary>
        public CellFrame Position => _walker.Position;

        /// <summary>Runs one walk to its end.</summary>
        /// <param name="goal">The goal.</param>
        /// <param name="name">The walk's name.</param>
        /// <param name="keep">The portals kept off (null: none).</param>
        /// <param name="way">The path source (null: the leg's own planner).</param>
        /// <param name="options">The runner's options (null: the ordinary steering under <paramref name="name"/>); the walk bench passes the live journeys' (<c>WalkBenchDatTests</c>).</param>
        /// <param name="measured">Called with the runner and its wall frames once the walk ends, before the runner is disposed (null: none).</param>
        /// <param name="log">The runner's log (null: none).</param>
        /// <param name="stepped">Called with the runner, the body's place and the time after each step (null: none): the walk bench's trace.</param>
        /// <param name="use">The object the walk ends by Using (<see cref="NavRunner.UseTarget"/>: the hand-over to the server's walk), or null.</param>
        public RunEnd Run(NavGoal goal, string name, PortalKeepOff? keep = null, INavPathSource? way = null, NavRunnerOptions? options = null, Action<NavRunner, int>? measured = null, ILogSink? log = null, Action<NavRunner, CellFrame, double>? stepped = null, NavUseTarget? use = null)
        {
            using var confirmer = new RouteConfirmer(_world, _walker.Physics.Setup!);
            var source = way ?? Planner.SourceFor(_walker.Position.CellId, JourneyLegPlanner.GoalCellOf(goal), Map);
            var runner = new NavRunner(_h.Api.Move, new ControllerNavBody(_walker.Controller), source, confirmer, _audit, options ?? new NavRunnerOptions { Name = name }, log) { KeepOff = keep, UseTarget = use };
            var began = _now;
            var wall = 0;
            runner.Start(goal, _now);
            while (!runner.IsDone && _now - began < WalkSeconds)
            {
                _now += 1.0 / Fps;
                _walker.Controller.Tick(_now);
                runner.Step(_now);
                _h.Tick(1.0 / Fps);
                wall += _walker.Physics.CollidingWithEnvironment && runner.RouteMetres > 1f ? 1 : 0;
                stepped?.Invoke(runner, _walker.Position, _now - began);
            }

            var line = string.Create(CultureInfo.InvariantCulture, $"{name}: {runner.State} after {_now - began:0.0} s, route_m={runner.RouteMetres:0.0} rungs={runner.Rungs} wall_frames={wall} end 0x{_walker.Position.CellId:X8} ({_walker.Position.Origin.X:0.0}, {_walker.Position.Origin.Y:0.0}, {_walker.Position.Origin.Z:0.0}) {runner.VerdictFields()} {runner.FailureReason}");
            var end = new RunEnd(JourneyLegPlanner.Arrived(runner.State, new TravelRequest(Destination(Drop, DropPoint), TravelPurpose.Cell)), runner.Rungs, wall, line + " nearest: " + runner.KeepOffNearestWhere, runner.KeepOffNearestMetres, runner.FailureReason, runner.OwnNearestMetres);
            measured?.Invoke(runner, wall);
            runner.Dispose();
            return end;
        }

        /// <summary>
        /// The hub walk's bench: what ACE does with a Use of a portal from beyond its use radius (<c>Player_Move.cs</c> <c>CreateMoveToChain</c>, <c>Creature.MoveToPosition</c> and <c>SetWalkRunThreshold</c>): a MoveToPosition to the
        /// portal's location, distance 0, charging (running) when it is half the walk-run threshold away or more, run on the client's own MoveTo until the body is within the use radius of it (the chain's check, cylinder to
        /// point: the portal is not in this world), the MoveTo ends, or ACE's 15 s. Answers the seconds, whether it came within reach, and the frames against a wall.
        /// </summary>
        public (double Seconds, bool Reached, int WallFrames) ServerWalk(in CellFrame target, float useRadius)
        {
            Assert.True(_walker.Physics.Weenie!.TryGetRunRate(out var runRate));
            var distance = Vector2.Distance(NavMap.FromCellFrame(_walker.Position).Global, NavMap.FromCellFrame(target).Global);
            var flags = MovementParameters.DefaultFlags | (distance >= new MovementParameters().WalkRunThreshold / 2f ? MovementParameterFlags.CanCharge : 0);
            var parameters = new MovementParameters() with { Flags = flags, DistanceToObject = 0f };
            var nonCombat = MotionCommandTable.ToWire(MotionCommands.NonCombat);
            Assert.Equal(MovementUnpackResult.Applied, ((IPlayerMovementBody)_walker.Body).ApplyServerMovement(new WireMovement(WireMovementType.MoveToPosition, nonCombat, false, false, default, MoveTo: new WireMoveTo(0, target, parameters, runRate)), [], autonomous: false, out _));
            var began = _now;
            var wall = 0;
            var goal = NavMap.FromCellFrame(target).Global;
            while (_now - began < PlayerMovementController.ServerMoveToLimitSeconds)
            {
                _now += 1.0 / Fps;
                _walker.Controller.Tick(_now);
                _h.Tick(1.0 / Fps);
                wall += _walker.Physics.CollidingWithEnvironment ? 1 : 0;
                if (Vector2.Distance(NavMap.FromCellFrame(_walker.Position).Global, goal) - _walker.Physics.Radius <= useRadius)
                {
                    return (_now - began, true, wall);
                }

                if (!_walker.Physics.IsMovingTo)
                {
                    break;
                }
            }

            return (_now - began, false, wall);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _audit.Dispose();
            _streamer.Dispose();
        }

        private static TeleportDestination Destination(uint cell, Vector3 point) =>
            new("hop", [], new WorldLocation("hop", new CellPosition(cell, point), HasHeight: true, HeadingDegrees: 0), "walk test");
    }
}

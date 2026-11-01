using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Physics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Physics;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Net.Diagnostics;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// M5 item 5 fix F3 (the live gate's round 1): the route planner's probes judge the fixed world only. Since item 5 every server object with collision
/// stands in the player's <see cref="PhysicsWorld"/> (<see cref="ServerObjectPhysicsHost"/>, D70), and the confirmer's ground probe
/// (<see cref="PhysicsWorld.TryGroundZ(in CellFrame, PhysSphere, out float, out Plane)"/>, a step down) met them: a closed door's physics BSP, an NPC's
/// or a creature's cylinder under the foot sphere is not walkable ground, so the probe found none and the route was refused ("the physics refused the path
/// ... ground probe ... no walkable ground"). The probes now take <see cref="PhysicsWorld.TryFixedGroundZ"/> and <see cref="PhysicsObj.FixedWorldOnly"/>;
/// the runner meets the server objects at walk time (the door step, the creature layer, the stuck ladder), and the player's own transitions still meet them.
/// The whole chain runs here on the dats: the walker, the host holding Renald's door closed, the runner with a door feed whose Use turns the door Ethereal in
/// the host after a delay (as ACE's <c>Door.Open</c> sets Ethereal, <c>Door.cs:111-130</c> @95ef426d).
/// </summary>
/// <remarks>
/// Test data, read-only from the pinned world database (ACE-World-Database-v0.9.296), as <see cref="DoorLegTests"/> and <see cref="ServerObjectPhysicsTests"/>:
/// Holtburg's door 0x7A9B4015 (Setup 0x020019FF at (132.56, 17.115, 94), turned 180 degrees), Renald the Elder 0x7A9B4021 at (139.23, 18.33, 94.01),
/// the human Setup 0x02000001 for an NPC, and the Tusker Setup 0x02000964 for a creature. Where the NPC and the creature stand is the test's choice (on the
/// planned path), not the database's.
/// </remarks>
[Trait("Tier", "Exhaustive")]
public sealed class ServerObjectRouteTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const double Fps = 60.0;
    private const uint DoorSetupId = 0x020019FF;
    private const uint HumanSetupId = 0x02000001;
    private const uint RenaldFrontRoomCell = 0xA9B4013F;
    private const uint FrontDoor = 0x7A9B4015;
    private const uint DoorCell = 0xA9B40029;
    private static readonly Vector3 RenaldAt = new(139.23f, 18.33f, 94.01f);
    private static readonly Vector3 FrontDoorAt = new(132.56f, 17.115f, 94f);
    private static readonly Quaternion DoorTurn = new(0f, 0f, -1f, 0f);

    private DatSet Dats => fixture.Dats;

    private static CellFrame LifestoneStart() => new(0xA9B40019, new AcFrame(new Vector3(81.33f, 11.80f, 94.01f), Quaternion.Identity));

    private static NavGoal Renald() => NavGoal.ToObject(0x7A9B4021, new NavPoint(0xA9B40000, RenaldAt), 6.5f, needSight: false, RenaldFrontRoomCell);

    [RequiresDatsFact]
    public void The_confirmer_confirms_the_Renald_route_past_the_closed_door_the_host_holds_while_the_players_probe_meets_the_door()
    {
        var (world, map, cells, host, walker) = Holtburg();
        host.Create(DoorDescription(FrontDoor), new CellFrame(DoorCell, new AcFrame(FrontDoorAt, DoorTurn)));
        Assert.True(host.Hosts(FrontDoor), "the door stands in the world");
        var source = new IndoorOutdoorPathSource(new PathPlanner(map), cells);
        var path = source.Plan(LifestoneStart(), Renald(), new NavScars());
        Assert.Equal(NavPathStatus.Found, path.Status);

        // The cause (round 1's "ground probe at 54.6 m"): the ground probe at the door's sill steps down onto the door's physics BSP, which is no ground.
        var sill = new CellFrame(DoorCell, new AcFrame(new Vector3(132.55f, 17.60f, 94.01f), Quaternion.Identity));
        var foot = walker.Physics.Setup!.Spheres[0];
        Assert.False(world.TryGroundZ(sill, foot, out _, out _), "the world's ground probe (the avatars' clamp) still meets the closed door");
        Assert.True(world.TryFixedGroundZ(sill, foot, out var z, out _), "the planner's probe judges the fixed world: the sill's floor");
        Assert.InRange(z, 93.9f, 94.2f);

        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var stopShort = RouteConfirmer.ArcBeyondRange(path.Waypoints, Renald().Point, Renald().Range, RenaldFrontRoomCell);
        var status = confirmer.ConfirmAll(path.Waypoints, stopShort);
        output.WriteLine($"confirm: {status} {confirmer.ConfirmedMetres:F1} m; {confirmer.Failure}");
        Assert.True(status == RouteConfirmStatus.Confirmed, confirmer.Failure?.ToString());
        Assert.True(confirmer.ConfirmedMetres > 54f, "the confirmed arc passes the door's sill (refused at 52.8 m before F3)");
    }

    [RequiresDatsFact]
    public void The_whole_chain_walks_to_Renald_the_door_held_closed_by_the_host_is_used_turns_ethereal_and_is_walked_through()
    {
        var leg = Run("f3-door", npcAt: null);

        Assert.True(leg.Runner.State == NavRunState.Arrived, leg.Runner.FailureReason);
        Assert.Equal([FrontDoor], leg.Doors.Used);
        Assert.Equal(1, leg.Runner.DoorsOpened);
        Assert.True(leg.DoorEthereal, "the host's door turned Ethereal after the Use");
        Assert.Equal(RenaldFrontRoomCell, leg.At.CellId);
        Assert.Contains(0xA9B40150u, leg.Visited); // through the doorway the door stands in
        Assert.True(Vector2.Distance(NavMap.FromCellFrame(leg.At).Global, new NavPoint(0xA9B40000, RenaldAt).Global) <= 6.5f, $"arrived within the goal's 6.5 m ({leg.At})");
    }

    [RequiresDatsFact]
    public void The_same_walk_with_an_NPC_standing_on_the_street_path_arrives()
    {
        var (_, map, cells, _, _) = Holtburg();
        var path = new IndoorOutdoorPathSource(new PathPlanner(map), cells).Plan(LifestoneStart(), Renald(), new NavScars());
        Assert.Equal(NavPathStatus.Found, path.Status);
        var street = path.ToRoute().PointAt(25f); // on the open street between the lifestone and the shop
        var leg = Run("f3-npc", npcAt: street.Local);

        Assert.True(leg.NpcHosted, "the NPC stands in the world, on the path");
        Assert.True(leg.Runner.State == NavRunState.Arrived, leg.Runner.FailureReason);
        Assert.Equal([FrontDoor], leg.Doors.Used);
        Assert.Equal(RenaldFrontRoomCell, leg.At.CellId);
    }

    [RequiresDatsFact]
    public void A_seek_to_within_20_m_of_a_creature_with_another_creature_standing_on_the_path_is_confirmed_and_walked()
    {
        var (world, map, _, host, walker) = Holtburg();
        var start = new CellFrame(0xA9B40019, new AcFrame(new Vector3(96f, 20f, 94f), Quaternion.Identity));
        var target = ServerObjectPhysicsTests.OnGround(Dats, new Vector3(160f, 60f, 0f));
        var goal = NavGoal.ToObject(0x70000002, new NavPoint(0xA9B40000, target), 20f, needSight: false);
        var source = new OutdoorPathSource(new PathPlanner(map));
        var planned = source.Plan(start, goal, new NavScars());
        Assert.Equal(NavPathStatus.Found, planned.Status);
        var route = planned.ToRoute();
        var onPath = ServerObjectPhysicsTests.OnGround(Dats, route.PointAt(route.ArcLength * 0.4f).Local);
        host.Create(Tusker(0x70000001), ServerObjectPhysicsTests.OutdoorAt(onPath));
        host.Create(Tusker(0x70000002), ServerObjectPhysicsTests.OutdoorAt(target));
        Assert.True(host.Hosts(0x70000001) && host.Hosts(0x70000002), "both tuskers stand in the world");
        Assert.False(PlacesAt(world, walker, onPath, fixedWorldOnly: false), "a placement probe that meets server objects is refused at the tusker (round 1's seek-10: \"placement probe ... no room to stand: Collided\")");
        Assert.True(PlacesAt(world, walker, onPath, fixedWorldOnly: true), "the planner's placement probe judges the fixed world there");

        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        Assert.Equal(RouteConfirmStatus.Confirmed, confirmer.ConfirmAll(planned.Waypoints, RouteConfirmer.ArcBeyondRange(planned.Waypoints, goal.Point, goal.Range)));

        var h = new ActionHarness(offline: true);
        var now = 0.0;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, "f3-seek");
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        Assert.True(walker.Place(start), $"placed at {start}");
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), source, confirmer, audit, new NavRunnerOptions { Name = "f3-seek" });
        runner.Start(goal, now);
        var from = walker.Position;
        while (!runner.IsDone && now < 60)
        {
            now += 1.0 / Fps;
            walker.Controller.Tick(now);
            host.Tick();
            runner.Step(now);
            h.Tick(1.0 / Fps);
        }

        var left = Vector2.Distance(NavMap.FromCellFrame(walker.Position).Global, goal.Point.Global);
        output.WriteLine($"f3-seek: {runner.State} {runner.VerdictFields()} from {from.Origin} to {walker.Position.Origin}, {left:F1} m from the target, the blocker at {onPath}; {runner.FailureReason}");
        Assert.True(runner.State == NavRunState.Arrived, runner.FailureReason);
        Assert.InRange(left, 0f, 21f);
    }

    [RequiresDatsFact]
    public void A_cell_the_physics_refused_on_one_leg_is_left_out_of_that_legs_room_graph_only()
    {
        var (_, map, _, _, _) = Holtburg();
        var planner = new JourneyLegPlanner(Dats);
        var first = Assert.IsType<IndoorOutdoorPathSource>(planner.SourceFor(LifestoneStart().CellId, RenaldFrontRoomCell, map));
        Assert.True(first.Avoid(0xA9B40150), "the first leg leaves the door's cell out (round 1's TODO(room-steps) line)");
        Assert.Contains(0xA9B40150u, first.AvoidedCells);

        var next = Assert.IsType<IndoorOutdoorPathSource>(planner.SourceFor(LifestoneStart().CellId, RenaldFrontRoomCell, map));
        Assert.Empty(next.AvoidedCells);
        Assert.Equal(NavPathStatus.Found, next.Plan(LifestoneStart(), Renald(), new NavScars()).Status);
    }

    /// <summary>A placement of the walker's body at <paramref name="at"/> (the confirmer's pattern): true when it fits within the confirmer's slack.</summary>
    private static bool PlacesAt(PhysicsWorld world, OfflineWalker walker, Vector3 at, bool fixedWorldOnly)
    {
        var probe = new PhysicsObj(world, walker.Physics.Setup!, id: 0, new ServerObjectWeenie(isCreature: true, isPlayer: true)) { FixedWorldOnly = fixedWorldOnly };
        try
        {
            var lifted = ServerObjectPhysicsTests.OutdoorAt(at + new Vector3(0f, 0f, RouteConfirmer.PlacementLiftMetres));
            var result = probe.SetPosition(lifted, SetPositionFlags.Placement);
            var moved = lifted.GetOffset(probe.Position);
            return result == SetPositionError.OK && probe.Cell is not null && new Vector2(moved.X, moved.Y).Length() <= RouteConfirmer.PlacementSlackMetres;
        }
        finally
        {
            probe.Destroy();
        }
    }

    private static ServerObjectDescription Tusker(uint guid) =>
        new(guid, ServerObjectTransitionTests.TuskerSetup, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: true, IsSelf: false, DescriptionFlags: 0);

    private sealed record Leg(NavRunner Runner, HostDoors Doors, List<uint> Visited, CellFrame At, bool DoorEthereal, bool NpcHosted);

    /// <summary>
    /// The journey's vendor leg (JourneyRunLeg's order) over the whole chain: the indoor-outdoor source over Holtburg, the confirmer in the walker's world, the host
    /// holding the shop's front door closed (the side door 0x7A9B4016 is left out: its turn is not in this test's data) (and an NPC when <paramref name="npcAt"/> is given), and a door feed that reads open from the host's Ethereal and whose
    /// Use turns the door Ethereal through the host 0.6 s later (<see cref="HostDoors"/>, standing for <c>SessionDoorFeed</c> and ACE).
    /// </summary>
    private Leg Run(string name, Vector3? npcAt)
    {
        var (world, map, cells, host, walker) = Holtburg();
        host.Create(DoorDescription(FrontDoor), new CellFrame(DoorCell, new AcFrame(FrontDoorAt, DoorTurn)));
        if (npcAt is { } spot)
        {
            host.Create(new ServerObjectDescription(0x7A9B4073, HumanSetupId, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: true, IsSelf: false, DescriptionFlags: 0), ServerObjectPhysicsTests.OutdoorAt(ServerObjectPhysicsTests.OnGround(Dats, spot)));
        }

        var npcHosted = npcAt is not null && host.Hosts(0x7A9B4073);
        var h = new ActionHarness(offline: true);
        var now = 0.0;
        using var audit = new MovementAudit(new RecordingMovementSink(), h.R.Bus, () => now, name);
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        Assert.True(walker.Place(LifestoneStart()), $"placed at {LifestoneStart()}");
        var doors = new HostDoors(host, DoorCrossings.SetupExtents(Dats)(DoorSetupId)!.Value, [FrontDoor]);
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new IndoorOutdoorPathSource(new PathPlanner(map), cells), confirmer, audit, new NavRunnerOptions { Name = name }) { Doors = doors };
        runner.Start(Renald(), now);
        var visited = new List<uint> { walker.Position.CellId };
        var metres = 0f;
        var last = NavMap.FromCellFrame(walker.Position).Global;
        while (!runner.IsDone && now < 120)
        {
            now += 1.0 / Fps;
            doors.Now = now;
            walker.Controller.Tick(now);
            host.Tick();
            walker.ReplaceAfterObjectsEntered(host.Stats);
            runner.Step(now);
            h.Tick(1.0 / Fps);
            var here = NavMap.FromCellFrame(walker.Position).Global;
            metres += Vector2.Distance(last, here);
            last = here;
            if (walker.Position.CellId != visited[^1])
            {
                visited.Add(walker.Position.CellId);
            }
        }

        var ethereal = host.TryGetObject(FrontDoor, out var door) && (door.State & PhysicsState.Ethereal) != 0;
        output.WriteLine($"{name}: {runner.State} after {now:F1} s, {metres:F1} m walked; {runner.VerdictFields()} at 0x{walker.Position.CellId:X8} {walker.Position.Origin}; used {string.Join(" ", doors.Used.Select(g => $"0x{g:X8}"))}; cells {string.Join(" ", visited.Select(c => (c & 0xFFFF).ToString("X3")))}; {runner.FailureReason}");
        runner.Dispose();
        return new Leg(runner, doors, visited, walker.Position, ethereal, npcHosted);
    }

    private static ServerObjectDescription DoorDescription(uint guid) =>
        new(guid, DoorSetupId, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: false, IsSelf: false, DescriptionFlags: 0x1000);

    private (PhysicsWorld World, NavMap Map, LandblockCellSet Cells, ServerObjectPhysicsHost Host, OfflineWalker Walker) Holtburg()
    {
        var world = ServerObjectPhysicsTests.HoltburgWorld(Dats);
        var map = new NavMap();
        map.Attach(NavTileBuilder.Build(new LandblockCollisionBuilder(Dats).Build(LandblockCoord.Holtburg)));
        var cells = LandblockCellSet.Load(new CellStructureLibrary(Dats), LandblockCoord.Holtburg);
        var host = new ServerObjectPhysicsHost(world, Dats, log: NullLogSink.Instance);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230));
        return (world, map, cells, host, walker);
    }
}

/// <summary>
/// A door feed over the server-object physics host (the scripted stand-in for <c>SessionDoorFeed</c> and ACE): a door is open while its physics object is Ethereal
/// (as the session's feed reads the object table's state), and a Use turns it Ethereal through the host <see cref="OpenDelaySeconds"/> later, as ACE's <c>Door.Open</c>
/// sets Ethereal and the server's SetState arrives a round trip after the Use (<c>Door.cs:111-130</c> @95ef426d).
/// </summary>
internal sealed class HostDoors(ServerObjectPhysicsHost host, float radius, IReadOnlyList<uint> guids) : INavDoorFeed
{
    private readonly Dictionary<uint, double> _opensAt = [];

    /// <summary>The scripted clock, in seconds; the test sets it every frame.</summary>
    public double Now { get; set; }

    /// <summary>Seconds from a Use to the door's Ethereal state.</summary>
    public double OpenDelaySeconds { get; set; } = 0.6;

    /// <summary>The doors Used, in order.</summary>
    public List<uint> Used { get; } = [];

    /// <inheritdoc/>
    public void Fill(List<NavDoor> into)
    {
        into.Clear();
        foreach (var guid in guids)
        {
            if (TryGet(guid, out var door))
            {
                into.Add(door);
            }
        }
    }

    /// <inheritdoc/>
    public bool TryGet(uint guid, out NavDoor door)
    {
        door = default;
        if (_opensAt.TryGetValue(guid, out var at) && Now >= at)
        {
            host.SetState(guid, (uint)(PhysicsState.ReportCollisions | PhysicsState.Ethereal));
            _opensAt.Remove(guid);
        }

        if (!host.TryGetObject(guid, out var obj))
        {
            return false;
        }

        door = new NavDoor(guid, obj.Position, radius, (obj.State & PhysicsState.Ethereal) != 0, Locked: false);
        return true;
    }

    /// <inheritdoc/>
    public ActionHandle? Use(uint guid)
    {
        Used.Add(guid);
        _opensAt.TryAdd(guid, Now + OpenDelaySeconds);
        return null;
    }
}

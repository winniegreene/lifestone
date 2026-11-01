using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Physics;
using Lifestone.Client.Tests.Avatars;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Data.World.Scenery;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Physics;

/// <summary>
/// M5 item 5 h1 (D70): the server-object physics host keeps a physics object in the player's world for each server
/// object with collision, so the offline walker (the player's transitions) meets Holtburg's doors, its lifestone and its
/// NPCs as retail's player does (<c>CPhysicsObj::FindObjCollisions</c>, docs/physics.md §10 "Other objects").
/// </summary>
/// <remarks>
/// Test data, all from the pinned world database (<c>server/runtime/downloads/ace-world-v0.9.296</c>, its
/// <c>landblock_instance</c> rows of landblock 0xA9B4 and their weenies' Setup data ids) and read here only:
/// <list type="bullet">
/// <item>Door 0x7A9B4015 (weenie 412 <c>door-aluvian-house</c>, Setup 0x020019FF) at (132.56, 17.115, 94), turned 180
/// degrees (q w=0 z=-1), in the outdoor cell its origin falls in (0xA9B40029).</item>
/// <item>The lifestone 0x7A9B404F (weenie 509, Setup 0x020002EE) at (81.3304, 11.7974, 94.005), as
/// <c>LifestonePlacementTests</c>.</item>
/// <item>Alcott 0x7A9B4073 (weenie 44895, an NPC: Setup 0x02000001, the human), whose Setup stands in for any NPC.</item>
/// </list>
/// </remarks>
public sealed class ServerObjectPhysicsTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint DoorGuid = 0x7A9B4015;
    private const uint DoorSetupId = 0x020019FF;
    private const uint DoorCell = 0xA9B40029;
    private static readonly Vector3 DoorOrigin = new(132.56f, 17.115f, 94f);
    private static readonly Quaternion DoorTurn = new(0f, 0f, -1f, 0f);

    private const uint LifestoneGuid = 0x7A9B404F;
    private const uint LifestoneSetupId = 0x020002EE;
    private static readonly Vector3 LifestoneOrigin = new(81.3304f, 11.7974f, 94.005f);

    private const uint NpcGuid = 0x7A9B4073;
    private const uint HumanSetupId = 0x02000001;

    /// <summary>Where the NPC stands in the NPC tests: on the open row y = 20 east of the lifestone, where
    /// <c>OfflineWalkerTests</c> runs east from (96, 20).</summary>
    private static readonly Vector3 NpcSpot = new(102f, 20f, 0f);

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void Without_the_NPC_the_walker_walks_east_along_its_row()
    {
        var (_, walker, _) = Holtburg();
        var reached = WalkEast(walker);
        output.WriteLine($"without the NPC the walker reached {reached}");
        Assert.True(reached.X > NpcSpot.X + 5f, $"something on the row stops the walker at {reached}: the NPC tests prove nothing");
    }

    [RequiresDatsFact]
    public void Without_the_door_the_walker_walks_through_its_doorway()
    {
        var (world, walker, _) = Holtburg();
        var (start, normal) = DoorApproach(world);
        var reached = WalkToward(walker, start, -normal, seconds: 4.0);
        var past = Vector3.Dot(reached - DoorOrigin, -normal);
        output.WriteLine($"without the door: from {start} to {reached} ({past:F2} m past the door's plane)");
        Assert.True(past > 1f, $"the walker stopped {past:F2} m past the door's plane: the doorway itself blocks, so the door tests prove nothing");
    }

    [RequiresDatsFact]
    public void A_closed_Holtburg_door_stops_the_walker()
    {
        var (world, walker, host) = Holtburg();
        host.Create(Door(PhysicsState.ReportCollisions), DoorAt());
        Assert.True(host.TryGetObject(DoorGuid, out var door));
        Assert.True((door.State & PhysicsState.HasPhysicsBsp) != 0, "the door's Setup has physics BSP parts");
        var (start, normal) = DoorApproach(world);
        var reached = WalkToward(walker, start, -normal, seconds: 4.0);
        var past = Vector3.Dot(reached - DoorOrigin, -normal);
        output.WriteLine($"closed door: from {start} to {reached} ({past:F2} m past the door's plane)");
        Assert.True(past < -0.3f, $"the walker went {past:F2} m past the closed door's plane");
        Assert.True(walker.OnGround);
    }

    [RequiresDatsFact]
    public void The_same_door_ethereal_lets_the_walker_through_once_it_turns()
    {
        var (world, walker, host) = Holtburg();
        host.Create(Door(PhysicsState.ReportCollisions), DoorAt());
        var (start, normal) = DoorApproach(world);
        var now = Settle(walker, start, 0.0);
        Face(walker, -normal);
        walker.DoMotion(MotionCommands.WalkForward);
        now = Tick(walker, now, 3.0);
        var stopped = Vector3.Dot(walker.Position.Frame.Origin - DoorOrigin, -normal);
        Assert.True(stopped < -0.3f, $"the walker went {stopped:F2} m past the closed door");

        // ACE opens a door by setting it Ethereal (the server's SetState); retail passes an ethereal object.
        host.SetState(DoorGuid, (uint)(PhysicsState.ReportCollisions | PhysicsState.Ethereal));
        Assert.True(host.TryGetObject(DoorGuid, out var door));
        Assert.True((door.State & PhysicsState.Ethereal) != 0);

        // docs/physics.md §10, "sliding normal": a walker pressed head-on into an obstacle keeps walking into nothing
        // until it turns, even when the obstacle goes away; turning away and back clears it.
        now = Tick(walker, now, 1.0);
        var pressed = Vector3.Dot(walker.Position.Frame.Origin - DoorOrigin, -normal);
        walker.StopMotion(MotionCommands.WalkForward);
        walker.DoMotion(MotionCommands.TurnRight);
        now = Tick(walker, now, 0.5);
        walker.StopMotion(MotionCommands.TurnRight);
        Face(walker, -normal);
        walker.DoMotion(MotionCommands.WalkForward);
        Tick(walker, now, 3.0);
        var past = Vector3.Dot(walker.Position.Frame.Origin - DoorOrigin, -normal);
        output.WriteLine($"stopped {stopped:F2} m from the door's plane; open and still pressed {pressed:F2} m; after turning {past:F2} m past it");
        Assert.True(past > 1f, $"the walker stayed {past:F2} m from the open door's plane");
    }

    [RequiresDatsFact]
    public void Holtburgs_lifestone_stops_the_walker()
    {
        var (world, walker, host) = Holtburg();
        host.Create(
            new ServerObjectDescription(LifestoneGuid, LifestoneSetupId, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: false, IsSelf: false, DescriptionFlags: 0x4000),
            OutdoorAt(LifestoneOrigin));
        Assert.True(host.TryGetObject(LifestoneGuid, out var lifestone));
        var setup = lifestone.Setup!;
        var shape = setup.HasPhysicsBsp ? "physics BSP" : setup.CylSpheres.Count > 0 ? "cylinder-spheres" : "spheres";
        var radius = setup.CylSpheres.Count > 0 ? setup.CylSpheres[0].Radius : setup.Spheres.Count > 0 ? setup.Spheres.Max(p => p.Radius) : 0f;
        output.WriteLine($"lifestone Setup: {shape}; {setup.Spheres.Count} spheres {string.Join(", ", setup.Spheres)}; {setup.CylSpheres.Count} cylinder-spheres; parts {setup.Parts.Count}");
        var start = LifestoneOrigin + new Vector3(0f, 6f, 0.5f);
        var reached = WalkToward(walker, OutdoorAt(start), -Vector3.UnitY, seconds: 4.0);
        var gap = Vector2.Distance(new Vector2(reached.X, reached.Y), new Vector2(LifestoneOrigin.X, LifestoneOrigin.Y));
        output.WriteLine($"stopped at {reached}, {gap:F2} m from the lifestone's centre (its {shape}: radius {radius:F2} m)");
        Assert.True(gap > radius, $"the walker is {gap:F2} m from the lifestone's centre, inside its {shape}");
        Assert.True(reached.Y > LifestoneOrigin.Y, "the walker walked through the lifestone");
    }

    [RequiresDatsFact]
    public void An_NPCs_cylinder_moves_with_the_NPC()
    {
        var (world, walker, host) = Holtburg();
        var first = NpcSpot;
        var second = NpcSpot + new Vector3(6f, 0f, 0f);
        host.Create(Npc(), OutdoorAt(OnGround(Dats, first)));
        var blocked = WalkEast(walker);
        Assert.True(blocked.X < first.X - 0.5f, $"the walker walked through the NPC at {first}: reached {blocked}");

        // The NPC walks 6 m east (a server move through the feed): its old spot is open, its new one blocks.
        host.Move(NpcGuid, OutdoorAt(OnGround(Dats, second)));
        var stopped = WalkEast(walker);
        output.WriteLine($"blocked at {blocked}; after the move stopped at {stopped}");
        Assert.True(stopped.X > first.X + 1f, $"the NPC's old spot still blocks: reached {stopped}");
        Assert.True(stopped.X < second.X - 0.5f, $"the walker walked through the NPC at its new spot: reached {stopped}");
    }

    [RequiresDatsFact]
    public void Deleting_the_NPC_removes_the_obstacle_and_the_worlds_tables_return_to_their_counts()
    {
        var (world, walker, host) = Holtburg();
        var spot = NpcSpot;
        var objects = world.Objects.Count;
        var lost = world.LostObjects.Count;
        host.Create(Npc(), OutdoorAt(OnGround(Dats, spot)));
        Assert.Equal(objects + 1, world.Objects.Count);
        Assert.True(host.TryGetObject(NpcGuid, out var npc));
        var shadowCell = npc.ShadowCellIdAt(0);
        Assert.True(npc.ShadowCellCount > 0);

        host.Remove(NpcGuid);
        Assert.Equal(objects, world.Objects.Count);
        Assert.Equal(lost, world.LostObjects.Count);
        Assert.Null(world.GetObject(NpcGuid));
        Assert.Equal(0, npc.ShadowCellCount);
        Assert.Equal(0, host.Count);
        Assert.False(host.TryGetObject(NpcGuid, out _));
        var reached = WalkEast(walker);
        output.WriteLine($"after the delete the walker reached {reached} (the NPC's shadow was in 0x{shadowCell:X8})");
        Assert.True(reached.X > spot.X + 1f, $"the deleted NPC still blocks: reached {reached}");

        // A recreate after a delete makes a new object; a second create of the same guid replaces it.
        host.Create(Npc(), OutdoorAt(OnGround(Dats, spot)));
        host.Create(Npc(), OutdoorAt(OnGround(Dats, spot)));
        Assert.Equal(objects + 1, world.Objects.Count);
        Assert.Equal(1, host.Count);
    }

    [RequiresDatsFact]
    public void An_object_in_a_landblock_not_attached_waits_in_the_lost_cell_and_blocks_once_it_is_attached()
    {
        var world = new PhysicsWorld();
        world.UseTime(0.0);
        var host = new ServerObjectPhysicsHost(world, Dats, log: NullLogSink.Instance);
        host.Create(
            new ServerObjectDescription(LifestoneGuid, LifestoneSetupId, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: false, IsSelf: false, DescriptionFlags: 0x4000),
            OutdoorAt(LifestoneOrigin));
        Assert.True(host.TryGetObject(LifestoneGuid, out var lifestone));
        Assert.True(lifestone.PlacementPending);
        Assert.Null(lifestone.Cell);
        Assert.Contains(lifestone, world.LostObjects);
        Assert.Equal(1, host.Stats.MovesPending);

        // PhysicsWorld.Attach places the lost objects of the landblock again (reenter_visibility: a sliding placement).
        var builder = new LandblockCollisionBuilder(Dats);
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                world.Attach(builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value));
            }
        }

        Assert.False(lifestone.PlacementPending);
        Assert.NotNull(lifestone.Cell);
        Assert.Empty(world.LostObjects);
        var shift = Vector3.Distance(lifestone.Position.Frame.Origin, LifestoneOrigin);
        output.WriteLine($"placed on attach at {lifestone.Position} ({shift:F3} m from its server position)");

        // The next server position (here the same one) puts it exactly where the server says.
        host.Move(LifestoneGuid, OutdoorAt(LifestoneOrigin with { X = LifestoneOrigin.X + 0.001f }));
        host.Move(LifestoneGuid, OutdoorAt(LifestoneOrigin));
        Assert.Equal(LifestoneOrigin, lifestone.Position.Frame.Origin);

        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance);
        var reached = WalkToward(walker, OutdoorAt(LifestoneOrigin + new Vector3(0f, 6f, 0.5f)), -Vector3.UnitY, seconds: 4.0);
        Assert.True(reached.Y > LifestoneOrigin.Y, $"the walker walked through the re-placed lifestone: reached {reached}");
    }

    [RequiresDatsFact]
    public void State_hides_and_shows_an_object_and_leaving_the_world_takes_it_out_until_its_next_move()
    {
        var (world, walker, host) = Holtburg();
        var spot = NpcSpot;
        var at = OutdoorAt(OnGround(Dats, spot));
        host.Create(Npc(), at);
        Assert.True(host.TryGetObject(NpcGuid, out var npc));

        // Hidden (set_hidden): IgnoreCollisions on, ReportCollisions off; shown again: both back.
        host.SetState(NpcGuid, (uint)(PhysicsState.ReportCollisions | PhysicsState.Hidden));
        Assert.True((npc.State & PhysicsState.IgnoreCollisions) != 0);
        Assert.True((npc.State & PhysicsState.ReportCollisions) == 0);
        host.SetState(NpcGuid, (uint)PhysicsState.ReportCollisions);
        Assert.True((npc.State & (PhysicsState.IgnoreCollisions | PhysicsState.Hidden)) == 0);
        Assert.True((npc.State & PhysicsState.ReportCollisions) != 0);

        // Picked up, wielded or out of its cell: out of the world, not destroyed.
        host.LeaveWorld(NpcGuid);
        Assert.False(host.IsInWorld(NpcGuid));
        Assert.Null(npc.Cell);
        Assert.Equal(0, npc.ShadowCellCount);
        var through = WalkEast(walker);
        Assert.True(through.X > spot.X + 1f, $"an object out of the world still blocks: reached {through}");

        host.Move(NpcGuid, at);
        Assert.True(host.IsInWorld(NpcGuid));
        var blocked = WalkEast(walker);
        Assert.True(blocked.X < spot.X - 0.5f, $"the object put back does not block: reached {blocked}");
    }

    [RequiresDatsFact]
    public void The_avatar_set_feeds_the_host_its_creates_moves_states_parents_pickups_and_deletes()
    {
        var world = HoltburgWorld(Dats);
        var cylinder = PhysicsSetup.FromParts([], [new PhysCylSphere(Vector3.Zero, 1.8f, 0.4f)]);
        var host = new ServerObjectPhysicsHost(world, id => id == AvatarRig.Sword ? PhysicsSetup.FromParts([]) : cylinder);
        var set = new AvatarSet(AvatarRig.Sources()) { ServerObjects = host };
        var spot = new Vector3(60f, 60f, OnGround(Dats, new Vector3(60f, 60f, 0f)).Z);

        Assert.Equal(AvatarApply.Applied, set.Create(AvatarRig.HumanAt(0x50000001, spot, cell: OutdoorAt(spot).CellId), now: 0));
        Assert.Equal(AvatarApply.Applied, set.Create(AvatarStress.Object(AvatarRig.Lamp) with { Guid = 0x50000002, Position = OutdoorAt(spot + new Vector3(5f, 0f, 0f)) }, now: 0));
        Assert.Equal(AvatarApply.Applied, set.Create(AvatarStress.Object(AvatarRig.Sword) with { Guid = 0x50000003, Position = OutdoorAt(spot + new Vector3(8f, 0f, 0f)) }, now: 0));
        Assert.Equal(AvatarApply.Applied, set.Create(AvatarRig.HumanAt(0x50000009, spot + new Vector3(0f, 10f, 0f), cell: OutdoorAt(spot).CellId) with { IsSelf = true }, now: 0));
        Assert.True(host.IsInWorld(0x50000001));
        Assert.True(host.IsInWorld(0x50000002));
        Assert.False(host.TryGetObject(0x50000003, out _));
        Assert.False(host.TryGetObject(0x50000009, out _));
        Assert.Equal(1, host.Stats.WithoutCollision);
        Assert.Equal(1, host.Stats.Skipped);
        Assert.True(host.TryGetObject(0x50000001, out var human));
        Assert.True(human.Weenie!.IsCreature);

        var moved = OutdoorAt(spot + new Vector3(0f, 3f, 0f));
        Assert.Equal(AvatarApply.Applied, set.Move(0x50000001, moved, teleportAdvanced: true, hasContact: true, placement: null, now: 0));
        Assert.Equal(moved.Frame.Origin, human.Position.Frame.Origin);

        Assert.Equal(AvatarApply.Applied, set.SetState(0x50000002, AvatarStateBits.ReportCollisions | (uint)PhysicsState.Ethereal));
        Assert.True(host.TryGetObject(0x50000002, out var lamp));
        Assert.True((lamp.State & PhysicsState.Ethereal) != 0);

        Assert.Equal(AvatarApply.Applied, set.PickUp(0x50000002));
        Assert.False(host.IsInWorld(0x50000002));
        Assert.Equal(AvatarApply.Applied, set.Move(0x50000002, OutdoorAt(spot + new Vector3(5f, 0f, 0f)), teleportAdvanced: true, hasContact: true, placement: null, now: 0));
        Assert.True(host.IsInWorld(0x50000002));
        Assert.Equal(AvatarApply.Applied, set.SetParent(0x50000002, 0x50000001, location: 1, placement: 0));
        Assert.False(host.IsInWorld(0x50000002));

        var objects = world.Objects.Count;
        Assert.Equal(AvatarApply.Applied, set.Delete(0x50000002));
        Assert.Equal(AvatarApply.Applied, set.Delete(0x50000001));
        Assert.Equal(objects - 2, world.Objects.Count);
        Assert.Equal(0, host.Count);
    }

    /// <summary>
    /// 5-R3 (M5 item 5 F1): a tusker created on the placed player's spot. The create's sliding placement (<c>enter_world</c>) does not meet the
    /// player (retail creates a cell's objects before it places the player), so the tusker stays on the server's spot; the walker's per-frame
    /// call (<see cref="OfflineWalker.ReplaceAfterObjectsEntered"/>, the headless body's and the game controller's) places the walker beside it,
    /// and the walker then walks straight away.
    /// </summary>
    [RequiresDatsFact]
    public void A_tusker_created_on_the_players_spot_ends_beside_it_and_the_walker_walks_straight_away()
    {
        var (_, walker, host) = Holtburg();
        var now = Settle(walker, OutdoorAt(OnGround(Dats, NpcSpot)), 0.0);
        var spot = walker.Position;
        Assert.Equal(0f, walker.ReplaceAfterObjectsEntered(host.Stats));
        host.Create(new ServerObjectDescription(0x70000001, ServerObjectTransitionTests.TuskerSetup, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: true, IsSelf: false, DescriptionFlags: 0), spot);
        Assert.True(host.TryGetObject(0x70000001, out var tusker));
        Assert.Equal(0, host.Stats.EntriesForced);
        Assert.True(Vector3.Distance(spot.Frame.Origin, tusker.Position.Frame.Origin) < 0.05f, $"the tusker's create slid it off the server's spot to {tusker.Position} (the player stopped it)");

        var moved = walker.ReplaceAfterObjectsEntered(host.Stats);
        var apart = Vector2.Distance(new Vector2(walker.Position.Frame.Origin.X, walker.Position.Frame.Origin.Y), new Vector2(spot.Frame.Origin.X, spot.Frame.Origin.Y));
        var touching = (tusker.Setup!.Spheres.Max(s => s.Radius) * tusker.Scale) + 0.48f;
        Assert.Equal(0f, walker.ReplaceAfterObjectsEntered(host.Stats));
        var away = new Vector3(walker.Position.Frame.Origin.X - spot.Frame.Origin.X, walker.Position.Frame.Origin.Y - spot.Frame.Origin.Y, 0f);
        Face(walker, Vector3.Normalize(away));
        var before = walker.Position.Frame.Origin;
        walker.DoMotion(MotionCommands.WalkForward);
        Tick(walker, now, 2.0);
        walker.StopMotion(MotionCommands.WalkForward);
        var walked = Vector3.Distance(walker.Position.Frame.Origin, before);
        output.WriteLine($"the walker was placed {moved:F2} m out of the tusker created on it ({apart:F2} m from its centre, touching {touching:F2} m), then walked {walked:F2} m straight away");
        Assert.True(apart >= touching * 0.95f, $"the walker is {apart:F2} m from the tusker's centre, inside it");
        Assert.True(walked > 1.5f, $"the walker walked only {walked:F2} m straight away from the tusker");
        Assert.True(Vector3.Distance(spot.Frame.Origin, tusker.Position.Frame.Origin) < 0.05f, "the tusker moved");
    }

    /// <summary>
    /// lead-fixes 11b (the game path): Holtburg's lifestone created after the recall's teleport on the placed player frees it through the call the
    /// game's player controller makes each frame (<see cref="OfflineWalker.ReplaceAfterObjectsEntered"/>; l1's test drives the placement itself).
    /// </summary>
    [RequiresDatsFact]
    public void The_lifestone_created_after_a_teleport_frees_the_player_through_the_controllers_call()
    {
        var (_, walker, host) = Holtburg();
        var now = Settle(walker, OutdoorAt(LifestoneOrigin), 0.0);
        Assert.Equal(0f, walker.ReplaceAfterObjectsEntered(host.Stats));
        host.Create(
            new ServerObjectDescription(LifestoneGuid, LifestoneSetupId, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: false, IsSelf: false, DescriptionFlags: 0x4000),
            OutdoorAt(LifestoneOrigin));
        Assert.True(host.TryGetObject(LifestoneGuid, out var stone));
        Assert.True(Vector2.Distance(new Vector2(stone.Position.Frame.Origin.X, stone.Position.Frame.Origin.Y), new Vector2(LifestoneOrigin.X, LifestoneOrigin.Y)) < 0.01f, $"the stone's create slid it off the server's spot to {stone.Position}");

        var moved = walker.ReplaceAfterObjectsEntered(host.Stats);
        var gap = Vector2.Distance(new Vector2(walker.Position.Frame.Origin.X, walker.Position.Frame.Origin.Y), new Vector2(LifestoneOrigin.X, LifestoneOrigin.Y));
        Face(walker, Vector3.UnitY);
        walker.DoMotion(MotionCommands.WalkForward);
        Tick(walker, now, 2.0);
        walker.StopMotion(MotionCommands.WalkForward);
        var away = Vector2.Distance(new Vector2(walker.Position.Frame.Origin.X, walker.Position.Frame.Origin.Y), new Vector2(LifestoneOrigin.X, LifestoneOrigin.Y));
        output.WriteLine($"placed {moved:F2} m out of the stone ({gap:F2} m from its centre); after 2 s north {away:F2} m");
        Assert.InRange(gap, 1.5f, 3.0f);
        Assert.True(away > gap + 2f, $"the player reached only {away:F2} m from the stone's centre");
    }

    /// <summary>
    /// 5-R5: Holtburg's door made before its landblock attaches waits in the lost cell; the world's attach places it again, and the host's tick finds
    /// it in a cell on its doorway (or, had that placement failed, forces it there and logs it), so it stops the walker.
    /// </summary>
    [RequiresDatsFact]
    public void The_door_made_before_its_landblock_attaches_ends_on_its_doorway_and_stops_the_walker()
    {
        var world = new PhysicsWorld();
        world.UseTime(0.0);
        var host = new ServerObjectPhysicsHost(world, Dats, log: NullLogSink.Instance);
        host.Create(Door(PhysicsState.ReportCollisions), DoorAt());
        Assert.True(host.TryGetObject(DoorGuid, out var door));
        Assert.True(door.PlacementPending);
        var builder = new LandblockCollisionBuilder(Dats);
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                world.Attach(builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value));
            }
        }

        host.Tick();
        Assert.NotNull(door.Cell);
        var shift = Vector3.Distance(door.Position.Frame.Origin, DoorOrigin);
        output.WriteLine($"the door on attach: 0x{door.Position.CellId:X8} {door.Position.Frame.Origin} ({shift:F3} m from its server position); forced {host.Stats.ReentriesForced}");
        var sideways = Vector2.Distance(new Vector2(door.Position.Frame.Origin.X, door.Position.Frame.Origin.Y), new Vector2(DoorOrigin.X, DoorOrigin.Y));
        Assert.True(sideways < 0.01f && shift < 0.1f, $"the door was placed {shift:F3} m off its doorway ({sideways:F3} m sideways)");
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance);
        var (start, normal) = DoorApproach(world);
        var reached = WalkToward(walker, start, -normal, seconds: 4.0);
        var past = Vector3.Dot(reached - DoorOrigin, -normal);
        Assert.True(past < -0.3f, $"the walker went {past:F2} m past the door made before its landblock");
    }

    /// <summary>
    /// 5-R3 and 5-R5, the fallbacks: an object whose sliding placement finds no valid position (a tusker at a spot of Holtburg where none fits: the
    /// placement and a bare object's placement there both answer NoValidPosition) is forced in at
    /// the server's position when it is created into an attached landblock, and by the host's tick when its landblock attaches after it; each is
    /// counted and logged once per object, and an object forced in does not repeat.
    /// </summary>
    [RequiresDatsFact]
    public void An_object_no_sliding_placement_fits_is_forced_into_its_cell_counted_and_logged_once()
    {
        var log = new Lines();
        var spot = OutdoorAt(OnGround(Dats, new Vector3(104f, 40f, 0f)));
        var attached = new ServerObjectPhysicsHost(HoltburgWorld(Dats), Dats, log: log);
        attached.Create(new ServerObjectDescription(0x70000010, ServerObjectTransitionTests.TuskerSetup, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: false, IsSelf: false, DescriptionFlags: 0), spot);
        Assert.True(attached.TryGetObject(0x70000010, out var made));
        output.WriteLine($"created: forced {attached.Stats.EntriesForced}, at {made.Position}");
        Assert.Equal(1, attached.Stats.EntriesForced);
        Assert.Equal(spot.Frame.Origin, made.Position.Frame.Origin);
        Assert.NotNull(made.Cell);

        var world = new PhysicsWorld();
        world.UseTime(0.0);
        var waiting = new ServerObjectPhysicsHost(world, Dats, log: log);
        waiting.Create(new ServerObjectDescription(0x70000011, ServerObjectTransitionTests.TuskerSetup, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: false, IsSelf: false, DescriptionFlags: 0), spot);
        Assert.True(waiting.TryGetObject(0x70000011, out var lost));
        Assert.True(lost.PlacementPending);
        var builder = new LandblockCollisionBuilder(Dats);
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                world.Attach(builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value));
            }
        }

        Assert.Null(lost.Cell);
        var entered = waiting.Stats.EnteredWorld;
        waiting.Tick();
        waiting.Tick();
        output.WriteLine($"after the attach: forced {waiting.Stats.ReentriesForced}, at {lost.Position}; log: {string.Join(" | ", log.Entries)}");
        Assert.Equal(1, waiting.Stats.ReentriesForced);
        Assert.Equal(entered + 1, waiting.Stats.EnteredWorld);
        Assert.NotNull(lost.Cell);
        Assert.Equal(spot.Frame.Origin, lost.Position.Frame.Origin);
        Assert.Single(log.Entries, e => e.Contains("TODO(m5-create-forced)", StringComparison.Ordinal));
        Assert.Single(log.Entries, e => e.Contains("TODO(m5-reenter-forced)", StringComparison.Ordinal));
    }

    private sealed class Lines : ILogSink
    {
        public List<string> Entries { get; } = [];

        public bool IsEnabled(LogSeverity severity) => true;

        public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields) => Entries.Add(message);
    }

    private static ServerObjectDescription Door(PhysicsState state) =>
        new(DoorGuid, DoorSetupId, 1f, (uint)state, IsCreature: false, IsSelf: false, DescriptionFlags: 0x1000);

    private static ServerObjectDescription Npc() =>
        new(NpcGuid, HumanSetupId, 1f, (uint)PhysicsState.ReportCollisions, IsCreature: true, IsSelf: false, DescriptionFlags: 0);

    private static CellFrame DoorAt() => new(DoorCell, new AcFrame(DoorOrigin, DoorTurn));

    /// <summary>A position in the outdoor cell of Holtburg that holds <paramref name="origin"/>, facing north.</summary>
    internal static CellFrame OutdoorAt(Vector3 origin) =>
        new(CellIds.LandCellAt(LandblockCoord.Holtburg, origin.X, origin.Y)!.Value, new AcFrame(origin, Quaternion.Identity));

    /// <summary>The terrain under (x, y) in Holtburg (<see cref="TerrainSampler"/>, retail's planes).</summary>
    internal static Vector3 OnGround(DatSet dats, Vector3 at)
    {
        var terrain = new TerrainSampler(dats.GetLandblockTerrain(LandblockCoord.Holtburg)!, dats.TerrainRegion.LandHeightTable);
        return at with { Z = terrain.GetHeight(at.X, at.Y) };
    }

    /// <summary>
    /// Where a walker starts in front of the door and the side it starts on: the door panel's widest physics polygon's
    /// normal (in the world) and a point 3 m out along it from the panel's centre, at the door's floor.
    /// </summary>
    private (CellFrame Start, Vector3 Normal) DoorApproach(PhysicsWorld world)
    {
        var setup = PhysicsSetup.Load(Dats, DoorSetupId, new PhysicsGeometryLibrary(Dats))!;
        var door = new AcFrame(DoorOrigin, DoorTurn);
        PhysPolygon? widest = null;
        AcFrame widestFrame = default;
        var widestArea = 0f;
        foreach (var part in setup.Parts)
        {
            if (part.Shape.Physics is not { } physics)
            {
                continue;
            }

            foreach (var polygon in physics.Polygons)
            {
                var area = Area(polygon);
                if (MathF.Abs(polygon.Plane.Normal.Z) < 0.05f && area > widestArea)
                {
                    widest = polygon;
                    widestFrame = part.RestFrame;
                    widestArea = area;
                }
            }
        }

        Assert.NotNull(widest);
        var centreInDoor = widestFrame.LocalToGlobal(widest!.Vertices.ToArray().Aggregate(Vector3.Zero, (s, v) => s + v) / widest.NumPoints);
        var centre = door.LocalToGlobal(centreInDoor);
        var normal = door.LocalToGlobalVec(widestFrame.LocalToGlobalVec(widest.Plane.Normal));
        normal = Vector3.Normalize(normal with { Z = 0f });
        var start = new Vector3(centre.X, centre.Y, DoorOrigin.Z) + (normal * 3f) + new Vector3(0f, 0f, 0.5f);
        output.WriteLine($"door: {setup.Parts.Count} parts, widest panel polygon {widestArea:F2} m2 centred at {centre}, normal {normal}");
        return (OutdoorAt(start), normal);
    }

    /// <summary>Places the walker at <paramref name="start"/>, faces it along <paramref name="direction"/>, walks for <paramref name="seconds"/>; answers where it is.</summary>
    private static Vector3 WalkToward(OfflineWalker walker, in CellFrame start, Vector3 direction, double seconds)
    {
        walker.StopMotion(MotionCommands.WalkForward);
        var now = Settle(walker, start, walker.World.CurrentTime);
        Face(walker, direction);
        walker.DoMotion(MotionCommands.WalkForward);
        Tick(walker, now, seconds);
        walker.StopMotion(MotionCommands.WalkForward);
        return walker.Position.Frame.Origin;
    }

    /// <summary>The walker from 6 m west of <see cref="NpcSpot"/>, walking east for 5 s (about 12.7 m).</summary>
    private Vector3 WalkEast(OfflineWalker walker) =>
        WalkToward(walker, OutdoorAt(OnGround(Dats, NpcSpot - new Vector3(6f, 0f, 0f))), Vector3.UnitX, seconds: 5.0);

    private static void Face(OfflineWalker walker, Vector3 direction) =>
        walker.Physics.SetHeading((float)((Math.Atan2(direction.X, direction.Y) * 180.0 / Math.PI) + 360.0) % 360f);

    private static double Settle(OfflineWalker walker, in CellFrame at, double now)
    {
        Assert.True(walker.Place(at), $"placement at {at}");
        now = Tick(walker, now, 1.0);
        Assert.True(walker.OnGround, $"not on the ground at {walker.Position}");
        return now;
    }

    private static double Tick(OfflineWalker walker, double now, double seconds)
    {
        var frames = (int)Math.Round(seconds * 60.0);
        for (var i = 0; i < frames; i++)
        {
            now += 1.0 / 60.0;
            walker.Tick(now);
        }

        return now;
    }

    private (PhysicsWorld World, OfflineWalker Walker, ServerObjectPhysicsHost Host) Holtburg()
    {
        var world = HoltburgWorld(Dats);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance);
        var host = new ServerObjectPhysicsHost(world, Dats, log: NullLogSink.Instance);
        return (world, walker, host);
    }

    internal static PhysicsWorld HoltburgWorld(DatSet dats)
    {
        var builder = new LandblockCollisionBuilder(dats);
        var world = new PhysicsWorld();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                world.Attach(builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value));
            }
        }

        world.UseTime(0.0);
        return world;
    }

    private static float Area(PhysPolygon polygon)
    {
        var vertices = polygon.Vertices;
        var sum = Vector3.Zero;
        for (var i = 1; i + 1 < vertices.Length; i++)
        {
            sum += Vector3.Cross(vertices[i] - vertices[0], vertices[i + 1] - vertices[0]);
        }

        return sum.Length() * 0.5f;
    }
}

/// <summary>
/// The feed's per-frame cost (M5 item 5 h1): an <see cref="AvatarSet.Advance"/> of 200 standing objects with collision,
/// each fed its unchanged position by the set and once more directly, with the host's tick, allocates nothing once warm
/// (the pattern of <c>AvatarAllocationTests</c>).
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class ServerObjectPhysicsAllocationTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void A_standing_crowds_feed_updates_allocate_nothing()
    {
        var world = ServerObjectPhysicsTests.HoltburgWorld(fixture.Dats);
        var cylinder = PhysicsSetup.FromParts([], [new PhysCylSphere(Vector3.Zero, 1.8f, 0.4f)]);
        var host = new ServerObjectPhysicsHost(world, _ => cylinder);
        var set = new AvatarSet(AvatarRig.Sources()) { ServerObjects = host };
        var center = ServerObjectPhysicsTests.OutdoorAt(ServerObjectPhysicsTests.OnGround(fixture.Dats, new Vector3(96f, 96f, 0f)));
        foreach (var create in AvatarStress.Generate(200, seed: 2, center, radius: 60f, [AvatarStress.Object(AvatarRig.Lamp)], firstGuid: 0x7E100001))
        {
            Assert.Equal(AvatarApply.Applied, set.Create(create, now: 0));
        }

        Assert.Equal(200, host.Count);
        var viewer = new AvatarViewer(center);
        long bytes = 0;
        var judged = 0;
        for (var frame = 1; frame <= 600; frame++)
        {
            var now = frame / 60.0;
            var window = AllocationWindow.Open();
            set.Advance(now, viewer, long.MaxValue);
            var all = set.All;
            for (var i = 0; i < all.Count; i++)
            {
                host.Move(all[i].Guid, all[i].Body.Position);
            }

            host.Tick();
            var (b, gcInside) = window.Close();
            if (frame <= 120 || gcInside)
            {
                continue;
            }

            bytes += b;
            judged++;
        }

        output.WriteLine($"{judged} judged frames of 200 standing objects fed: {bytes} bytes; host moves {host.Stats.Moves}");
        Assert.True(judged > 400, $"only {judged} frames were judged (GCs inside the others)");
        Assert.Equal(0L, bytes);
        Assert.Equal(200, host.Stats.EnteredWorld);
        Assert.Equal(0, host.Stats.Moves);
    }
}

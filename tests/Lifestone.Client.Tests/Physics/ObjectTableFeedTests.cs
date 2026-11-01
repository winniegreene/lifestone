using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Physics;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.ReducerHarness;
using WireState = Lifestone.Net.Types.PhysicsState;

namespace Lifestone.Client.Tests.Physics;

/// <summary>
/// M5 item 5 h2 (D70): <see cref="ObjectTableFeed"/> drives a server-object physics host from the session's object table and events, so a headless walk
/// meets the server's objects. On the reducer's real event stream and Holtburg's collision: a door created through the session blocks the offline walker,
/// its SetState to Ethereal lets the walker through, its delete removes it, and the player's own object is never fed. The door is
/// <c>ServerObjectPhysicsTests</c>' (Holtburg's 0x7A9B4015, Setup 0x020019FF, from the pinned world database's landblock_instance rows; test data only, D46).
/// </summary>
public sealed class ObjectTableFeedTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint DoorGuid = 0x7A9B4015;
    private const uint DoorSetupId = 0x020019FF;
    private const uint DoorCell = 0xA9B40029;
    private const uint HumanSetupId = 0x02000001;
    private static readonly Vector3 DoorOrigin = new(132.56f, 17.115f, 94f);
    private static readonly Quaternion DoorTurn = new(0f, 0f, -1f, 0f);

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void A_door_created_through_the_session_blocks_the_walker_and_a_delete_lets_it_through()
    {
        var (h, walker, host, _) = Headless();
        var (start, normal) = DoorApproach();
        var open = Walk(walker, start, normal, 4.0);
        Assert.True(open > 1f, $"without the door the walker stopped {open:F2} m past its plane: the test proves nothing");

        CreateDoor(h);
        Assert.Equal(1, host.Count);
        Assert.True(host.TryGetObject(DoorGuid, out _));
        var closed = Walk(walker, start, normal, 4.0);
        output.WriteLine($"closed door: {closed:F2} m past the door's plane");
        Assert.True(closed < -0.3f, $"the walker went {closed:F2} m past the closed door");

        h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(DoorGuid, 1));
        Assert.Equal(0, host.Count);
        var deleted = Walk(walker, start, normal, 4.0);
        output.WriteLine($"deleted door: {deleted:F2} m past the plane");
        Assert.True(deleted > 1f, $"the walker stopped {deleted:F2} m past the plane after the delete");
    }

    [RequiresDatsFact]
    public void A_state_change_to_ethereal_through_the_session_lets_the_walker_through()
    {
        var (h, walker, host, _) = Headless();
        var (start, normal) = DoorApproach();
        CreateDoor(h);
        var closed = Walk(walker, start, normal, 4.0);
        Assert.True(closed < -0.3f, $"the walker went {closed:F2} m past the closed door");

        // ACE opens a door with a SetState that adds Ethereal; the walker is placed again, so no earlier contact is carried (the sliding normal of docs/physics.md section 10).
        h.Reducer.OnSetState(h.Context, new SetStateMsg(DoorGuid, WireState.ReportCollisions | WireState.Ethereal, 1, 2));
        Assert.True(host.TryGetObject(DoorGuid, out var door));
        Assert.True((door.State & Lifestone.Data.Physics.PhysicsState.Ethereal) != 0, "the door's physics object is ethereal");
        var open = Walk(walker, start, normal, 4.0);
        output.WriteLine($"open door: {open:F2} m past the door's plane");
        Assert.True(open > 1f, $"the walker stopped {open:F2} m past the open door's plane");
    }

    [RequiresDatsFact]
    public void Objects_already_in_the_table_when_the_feed_is_made_are_created_at_once()
    {
        var h = new ReducerHarness();
        h.EnterWorld(DoorCell);
        CreateDoor(h);
        var world = ServerObjectPhysicsTests.HoltburgWorld(Dats);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance);
        var host = new ServerObjectPhysicsHost(world, Dats, log: NullLogSink.Instance);
        using var feed = new ObjectTableFeed(host, h.State.Objects, h.Bus, PlayerGuid);
        Assert.Equal(1, host.Count);
        var (start, normal) = DoorApproach();
        Assert.True(Walk(walker, start, normal, 4.0) < -0.3f, "the door that was already there blocks the walker");
    }

    [Fact]
    public void The_players_own_object_is_never_fed_and_the_events_reach_the_host()
    {
        var h = new ReducerHarness();
        var host = new RecordingHost();
        using var feed = new ObjectTableFeed(host, h.State.Objects, h.Bus, PlayerGuid);
        h.EnterWorld(DoorCell); // the player's CreateObject (the human Setup has collision: the host would make a physics object for it)
        Assert.Empty(host.Calls);
        h.Reducer.OnUpdatePosition(h.Context, Move(PlayerGuid, 1, position: 2, teleport: 1, forcePosition: 1, cell: DoorCell, x: 12));
        Assert.Empty(host.Calls);
        h.Reducer.OnSetState(h.Context, new SetStateMsg(PlayerGuid, WireState.Gravity | WireState.EdgeSlide, 1, 2));
        Assert.Empty(host.Calls);

        h.Reducer.OnCreateObject(h.Context, Create(0x80000100, Stamps(instance: 1), Position(DoorCell, x: 20), state: WireState.ReportCollisions, setup: HumanSetupId, name: "Guard", itemType: 0x10), false);
        Assert.Equal(["create 80000100 setup=02000001 creature=True flags=0 at=yes"], host.Calls);
        host.Calls.Clear();
        h.Reducer.OnUpdatePosition(h.Context, Move(0x80000100, 1, position: 2, teleport: 1, forcePosition: 1, cell: DoorCell, x: 30));
        h.Reducer.OnSetState(h.Context, new SetStateMsg(0x80000100, WireState.ReportCollisions | WireState.Ethereal, 1, 2));
        h.Reducer.OnPickupEvent(h.Context, new PickupEventMsg(0x80000100, 1, 3));
        h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(0x80000100, 1));
        Assert.Equal(["move 80000100", $"state 80000100 {(uint)(WireState.ReportCollisions | WireState.Ethereal):X}", "leave 80000100", "remove 80000100"], host.Calls);
    }

    [Fact]
    public void The_feed_carries_the_weenie_descriptions_flags_and_stops_when_disposed()
    {
        var h = new ReducerHarness();
        var host = new RecordingHost();
        var feed = new ObjectTableFeed(host, h.State.Objects, h.Bus, PlayerGuid);
        h.EnterWorld(DoorCell);
        var create = Create(0x80000101, Stamps(instance: 1), Position(DoorCell), setup: HumanSetupId, name: "Player");
        h.Reducer.OnCreateObject(h.Context, create with { Weenie = create.Weenie with { ObjectDescriptionFlags = 0x18 } }, false);
        Assert.Equal(["create 80000101 setup=02000001 creature=False flags=24 at=yes"], host.Calls);
        feed.Dispose();
        h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(0x80000101, 1));
        Assert.Single(host.Calls);
    }

    private static void CreateDoor(ReducerHarness h) =>
        h.Reducer.OnCreateObject(h.Context, Create(DoorGuid, Stamps(instance: 1), new AcPosition(DoorCell, DoorOrigin, DoorTurn), state: WireState.ReportCollisions, setup: DoorSetupId, name: "Door"), false);

    /// <summary>A reducer with the player entered, Holtburg's collision, a walker, a host and the feed over the session's table.</summary>
    private (ReducerHarness H, OfflineWalker Walker, ServerObjectPhysicsHost Host, ObjectTableFeed Feed) Headless()
    {
        var h = new ReducerHarness();
        h.EnterWorld(DoorCell);
        var world = ServerObjectPhysicsTests.HoltburgWorld(Dats);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance);
        var host = new ServerObjectPhysicsHost(world, Dats, log: NullLogSink.Instance);
        var feed = new ObjectTableFeed(host, h.State.Objects, h.Bus, PlayerGuid);
        return (h, walker, host, feed);
    }

    /// <summary>Places the walker at <paramref name="start"/>, faces it against <paramref name="normal"/> (into the door), walks; answers how far past the door's plane it is.</summary>
    private static float Walk(OfflineWalker walker, in CellFrame start, Vector3 normal, double seconds)
    {
        walker.StopMotion(MotionCommands.WalkForward);
        Assert.True(walker.Place(start), $"placement at {start}");
        var now = Tick(walker, walker.World.CurrentTime, 1.0);
        Assert.True(walker.OnGround, $"not on the ground at {walker.Position}");
        var direction = -normal;
        walker.Physics.SetHeading((float)((Math.Atan2(direction.X, direction.Y) * 180.0 / Math.PI) + 360.0) % 360f);
        walker.DoMotion(MotionCommands.WalkForward);
        Tick(walker, now, seconds);
        walker.StopMotion(MotionCommands.WalkForward);
        return Vector3.Dot(walker.Position.Frame.Origin - DoorOrigin, direction);
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

    /// <summary>Where a walker starts in front of the door and the side it starts on: the widest panel polygon's normal in the world and a point 3 m out along it (as <c>ServerObjectPhysicsTests</c>).</summary>
    private (CellFrame Start, Vector3 Normal) DoorApproach()
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
                var sum = Vector3.Zero;
                for (var i = 1; i + 1 < polygon.Vertices.Length; i++)
                {
                    sum += Vector3.Cross(polygon.Vertices[i] - polygon.Vertices[0], polygon.Vertices[i + 1] - polygon.Vertices[0]);
                }

                var area = sum.Length() * 0.5f;
                if (MathF.Abs(polygon.Plane.Normal.Z) < 0.05f && area > widestArea)
                {
                    widest = polygon;
                    widestFrame = part.RestFrame;
                    widestArea = area;
                }
            }
        }

        Assert.NotNull(widest);
        var centre = door.LocalToGlobal(widestFrame.LocalToGlobal(widest!.Vertices.ToArray().Aggregate(Vector3.Zero, (s, v) => s + v) / widest.NumPoints));
        var normal = Vector3.Normalize(door.LocalToGlobalVec(widestFrame.LocalToGlobalVec(widest.Plane.Normal)) with { Z = 0f });
        var start = new Vector3(centre.X, centre.Y, DoorOrigin.Z) + (normal * 3f) + new Vector3(0f, 0f, 0.5f);
        return (ServerObjectPhysicsTests.OutdoorAt(start), normal);
    }

    /// <summary>A host that records its calls (what the feed sent, not what a host does with it).</summary>
    private sealed class RecordingHost : IServerObjectPhysics
    {
        public List<string> Calls { get; } = [];

        public void Create(in ServerObjectDescription description, CellFrame? position) =>
            Calls.Add($"create {description.Guid:X8} setup={description.SetupId:X8} creature={description.IsCreature} flags={description.DescriptionFlags} at={(position is null ? "no" : "yes")}");

        public void Move(uint guid, in CellFrame position) => Calls.Add($"move {guid:X8}");

        public void SetState(uint guid, uint physicsState) => Calls.Add($"state {guid:X8} {physicsState:X}");

        public void LeaveWorld(uint guid) => Calls.Add($"leave {guid:X8}");

        public void Remove(uint guid) => Calls.Add($"remove {guid:X8}");

        public void Tick()
        {
        }
    }
}

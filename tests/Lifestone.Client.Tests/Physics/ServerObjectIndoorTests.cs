using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Physics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Tests.Avatars;
using Lifestone.Client.Tests.Movement;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Physics;

/// <summary>
/// M5 item 5 F1 (review 5-R4): the server-object transitions indoors, on the dats in the Black Spawn Den (landblock 0x0105, the den
/// profile's room graph). A walker (GroundWalk.Create's) first walks the den's graph from the arrival to two of
/// <c>DenRoomGraphTests</c>' ramp rungs (0x02DA, 0x0262), and its path is recorded: the segments the tests use are straight runs of
/// it, so the dats, not a hand-set point, say where a room's portal and a ramp are. Then a tusker (Setup 0x02000964 on its table
/// 0x0900000C) walks a run that crosses a room-to-room portal and ends in the next room's cell; one walks a run down a ramp and ends
/// on the floor below (z within 5 cm of the walker's, with contact: D72's carried contact and the transition's step-down, no
/// gravity); and two tuskers stuck to a player standing in a den room end apart and outside its cylinder. Each scene also runs
/// with <c>--server-object-transitions=off</c> where that proves the scene.
/// </summary>
public sealed class ServerObjectIndoorTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const string DenKey = "black-spawn-den-aluvian";
    private const uint PlayerGuid = ServerObjectTransitionTests.PlayerGuid;
    private const uint HumanTable = 0x09000001;
    private const double Frame = 1.0 / 60.0;

    [RequiresDatsFact]
    public void A_tusker_walked_through_a_room_to_room_portal_ends_in_the_next_rooms_cell()
    {
        var path = DenPath();
        var (from, to, crossing) = Run(path, run => run.From.CellId != run.To.CellId && run.CellChanges == 1, minLength: 3.5f);
        output.WriteLine($"run 0x{from.CellId:X8} {from.Origin} -> 0x{to.CellId:X8} {to.Origin} (crosses at 0x{crossing:X8})");
        var scene = new Scene(fixture.Dats, transitions: true, withPlayerAt: null);
        var tusker = scene.Tusker(0x70000001, from, HeadingTo(from.Origin, to.Origin));
        var cells = scene.WalkUntil(0x70000001, from.Origin, Horizontal(from.Origin, to.Origin), seconds: 8.0);
        output.WriteLine($"the tusker ended at 0x{tusker.Position.CellId:X8} {tusker.Position.Origin}; cells {string.Join(" ", cells.Select(c => (c & 0xFFFF).ToString("X4")))}; host transitions {scene.Host.Stats.Transitions} (blocked {scene.Host.Stats.TransitionsBlocked})");
        Assert.True(Horizontal(tusker.Position.Origin, to.Origin) < 1.0f, $"the tusker stopped {Horizontal(tusker.Position.Origin, to.Origin):F2} m short of the run's end: the portal's run does not fit it");
        Assert.Equal(to.CellId, tusker.Position.CellId);
        Assert.True(scene.Host.TryGetObject(0x70000001, out var obj));
        Assert.Equal(to.CellId, obj.Position.CellId);
        Assert.InRange(tusker.Position.Origin.Z, to.Origin.Z - 0.05f, to.Origin.Z + 0.05f);
    }

    [RequiresDatsFact]
    public void A_tusker_walked_down_a_ramp_ends_on_the_floor_below_with_contact()
    {
        var path = DenPath();
        var (from, to, _) = Run(path, run => run.From.Origin.Z - run.To.Origin.Z > 1.0f && run.FlatEnd, minLength: 3.0f);
        output.WriteLine($"ramp run 0x{from.CellId:X8} {from.Origin} -> 0x{to.CellId:X8} {to.Origin} ({from.Origin.Z - to.Origin.Z:F2} m down)");
        var scene = new Scene(fixture.Dats, transitions: true, withPlayerAt: null);
        var tusker = scene.Tusker(0x70000001, from, HeadingTo(from.Origin, to.Origin));
        scene.WalkUntil(0x70000001, from.Origin, Horizontal(from.Origin, to.Origin), seconds: 8.0);
        Assert.True(scene.Host.TryGetObject(0x70000001, out var obj));
        var contact = (obj.TransientState & TransientState.Contact) != 0;
        output.WriteLine($"the tusker ended at 0x{tusker.Position.CellId:X8} {tusker.Position.Origin} (the walker's floor z {to.Origin.Z:F3}); contact {contact} on walkable {obj.OnWalkable}; body contact {tusker.HasContact}");
        Assert.True(Horizontal(tusker.Position.Origin, to.Origin) < 1.0f, $"the tusker stopped {Horizontal(tusker.Position.Origin, to.Origin):F2} m short of the ramp run's end");
        Assert.Equal(to.CellId, tusker.Position.CellId);
        Assert.InRange(tusker.Position.Origin.Z, to.Origin.Z - 0.05f, to.Origin.Z + 0.05f);
        Assert.True(tusker.HasContact);
        Assert.True(contact, "the host's object has no contact on the floor below the ramp");
    }

    [RequiresDatsFact]
    public void Two_tuskers_stuck_to_a_player_in_a_den_room_end_apart_and_outside_its_cylinder()
    {
        var path = DenPath();

        // The player stands where the walker's path has 6 m of straight floor behind it in one cell; the tuskers start 3.5 m and 5.5 m back along it.
        var (back, stand, _) = Run(path, run => run.CellChanges == 0 && run.Level, minLength: 6f);
        var a = Along(back.Origin, stand.Origin, 3.5f);
        var b = Along(back.Origin, stand.Origin, 5.5f);
        var results = new Dictionary<bool, (float Apart, float Nearest, float Touching, float PlayerTouching)>();
        foreach (var transitions in new[] { true, false })
        {
            var scene = new Scene(fixture.Dats, transitions, withPlayerAt: stand);
            var ta = scene.Tusker(0x70000001, new CellFrame(stand.CellId, new AcFrame(a with { Z = stand.Origin.Z }, Quaternion.Identity)), HeadingTo(a, stand.Origin));
            var tb = scene.Tusker(0x70000002, new CellFrame(stand.CellId, new AcFrame(b with { Z = stand.Origin.Z }, Quaternion.Identity)), HeadingTo(b, stand.Origin));
            scene.Advance(10);
            var swing = WireMovement.Interpreted(MotionCommandTable.ToWire(MotionCommands.NonCombat), default, stickyObjectId: PlayerGuid);
            Assert.Equal(AvatarApply.Applied, scene.Set.SetMovement(0x70000001, isSelf: false, autonomous: false, swing, []));
            Assert.Equal(AvatarApply.Applied, scene.Set.SetMovement(0x70000002, isSelf: false, autonomous: false, swing, []));
            scene.Advance(90);
            var player = scene.PlayerObj!.Position.Origin;
            var touching = scene.Radius(0x70000001) + scene.Radius(0x70000002);
            var playerTouching = scene.Radius(0x70000001) + scene.PlayerRadius;
            results[transitions] = (Horizontal(ta.Position.Origin, tb.Position.Origin), Math.Min(Horizontal(ta.Position.Origin, player), Horizontal(tb.Position.Origin, player)), touching, playerTouching);
            output.WriteLine($"transitions {transitions}: in 0x{stand.CellId:X8}, the tuskers at 0x{ta.Position.CellId:X8} {ta.Position.Origin} and 0x{tb.Position.CellId:X8} {tb.Position.Origin}: {results[transitions].Apart:F2} m apart (touching {touching:F2} m), nearest to the player {results[transitions].Nearest:F2} m (touching {playerTouching:F2} m)");
        }

        var on = results[true];
        var off = results[false];
        Assert.True(off.Apart < off.Touching * 0.8f, $"without transitions the two are {off.Apart:F2} m apart: the scene does not press them together");
        Assert.True(on.Apart >= on.Touching * 0.95f, $"with transitions the two tuskers overlap in the den: {on.Apart:F2} m apart, {on.Touching:F2} m when touching");
        Assert.True(on.Nearest >= on.PlayerTouching * 0.95f, $"a tusker is inside the player's cylinder in the den: {on.Nearest:F2} m");
    }

    private static float Horizontal(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));

    private static float HeadingTo(Vector3 from, Vector3 to) =>
        (float)((Math.Atan2(to.X - from.X, to.Y - from.Y) * 180.0 / Math.PI) + 360.0) % 360f;

    private static Vector3 Along(Vector3 from, Vector3 to, float metres)
    {
        var d = new Vector3(to.X - from.X, to.Y - from.Y, 0f);
        return from + (Vector3.Normalize(d) * (d.Length() - metres));
    }

    /// <summary>
    /// The first straight run of <paramref name="path"/> at least <paramref name="minLength"/> metres long (every recorded point between its ends
    /// within 0.25 m of the line, sideways) that <paramref name="accept"/> takes; the cell of its first cell change.
    /// </summary>
    private static (CellFrame From, CellFrame To, uint Crossing) Run(IReadOnlyList<CellFrame> path, Func<RunInfo, bool> accept, float minLength)
    {
        for (var i = 0; i < path.Count; i++)
        {
            for (var j = i + 1; j < path.Count; j++)
            {
                var length = Horizontal(path[i].Origin, path[j].Origin);
                if (length < minLength)
                {
                    continue;
                }

                var straight = true;
                var changes = 0;
                uint crossing = 0;
                var minZ = float.MaxValue;
                var maxZ = float.MinValue;
                for (var k = i + 1; k <= j && straight; k++)
                {
                    straight = Sideways(path[i].Origin, path[j].Origin, path[k].Origin) <= 0.25f;
                    minZ = Math.Min(minZ, path[k].Origin.Z);
                    maxZ = Math.Max(maxZ, path[k].Origin.Z);
                    if (path[k].CellId != path[k - 1].CellId)
                    {
                        crossing = crossing == 0 ? path[k].CellId : crossing;
                        changes++;
                    }
                }

                // The run's last metre is level (the floor below a ramp, not its slope).
                var flatEnd = true;
                for (var k = j; k > i && Horizontal(path[k].Origin, path[j].Origin) < 1f; k--)
                {
                    flatEnd &= Math.Abs(path[k].Origin.Z - path[j].Origin.Z) < 0.02f;
                }

                if (straight && accept(new RunInfo(path[i], path[j], changes, flatEnd, maxZ - minZ < 0.02f)))
                {
                    return (path[i], path[j], crossing);
                }

                break;
            }
        }

        throw new Xunit.Sdk.XunitException("no straight run of the den walk fits the test (the den's walk or its dats changed)");
    }

    private static float Sideways(Vector3 a, Vector3 b, Vector3 p)
    {
        var d = Vector2.Normalize(new Vector2(b.X - a.X, b.Y - a.Y));
        var v = new Vector2(p.X - a.X, p.Y - a.Y);
        return MathF.Abs((v.X * d.Y) - (v.Y * d.X));
    }

    /// <summary>The walker's recorded path from the den's arrival to the ramp rungs 0x02DA and 0x0262 (<c>DenRoomGraphTests</c>), one position per physics step.</summary>
    private List<CellFrame> DenPath()
    {
        var den = GroundCatalog.Get(DenKey);
        var dats = fixture.Dats;
        var landblock = Assert.Single(den.Landblocks);
        var all = LandblockCellSet.Load(new CellStructureLibrary(dats), new LandblockCoord((byte)(landblock >> 8), (byte)(landblock & 0xFF)));
        var graph = new RoomGraph(LandblockCellSet.FromCells(all.Structures, all.Coord, all.Cells.Where(c => den.IsWalkable(c.Id))), den.Walk.DoorwayLean);
        var geometry = new PhysicsGeometryLibrary(dats);
        var world = DenWorld(dats);
        var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230), geometry);
        Assert.True(walker.Place(den.Arrival.Frame), "placed at the arrival");
        var path = new List<CellFrame>();
        var now = 0.0;
        foreach (var low in new uint[] { 0x02DA, 0x0262 })
        {
            var from = walker.Position;
            var route = graph.PlanScripted($"indoor-{low:X4}", in from, 0x01050000u | low, den.Walk.StopWithinMetres, den.Walk.StopDwellSeconds);
            Assert.NotNull(route);
            var driver = new RouteDriver(walker.Controller, route);
            var start = now;
            var status = RouteStatus.Running;
            while (status == RouteStatus.Running && now - start < 240)
            {
                now += Frame;
                if (walker.Controller.Tick(now) && walker.OnGround)
                {
                    path.Add(walker.Position);
                }

                status = driver.Tick(now);
            }

            Assert.Equal(RouteStatus.Done, status);
        }

        return path;
    }

    private static PhysicsWorld DenWorld(DatSet dats)
    {
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, new PhysicsGeometryLibrary(dats)).Build(new LandblockCoord(1, 5)));
        world.UseTime(0.0);
        return world;
    }

    private readonly record struct RunInfo(CellFrame From, CellFrame To, int CellChanges, bool FlatEnd, bool Level);

    /// <summary>The den's collision, the host over it, an avatar set from the dats feeding it, and optionally the player standing at a spot.</summary>
    private sealed class Scene
    {
        public Scene(DatSet dats, bool transitions, CellFrame? withPlayerAt)
        {
            World = DenWorld(dats);
            Host = new ServerObjectPhysicsHost(World, dats, log: NullLogSink.Instance);
            Set = new AvatarSet(AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats))) { ServerObjects = Host, ServerObjectTransitions = transitions };
            if (withPlayerAt is { } at)
            {
                PlayerObj = MovementTestKit.Player(World, at);
                PlayerRadius = MovementTestKit.HumanSetup.Radius;
                Assert.Equal(AvatarApply.Applied, Set.Create(AvatarStress.Object(0x02000001, HumanTable, isCreature: true) with { Guid = PlayerGuid, IsSelf = true, Position = PlayerObj.Position, HasContact = true }, now: 0));
                Set.PlayerBody = new KinematicPlayerBody(Set.Player!, Set);
            }

            Viewer = new AvatarViewer(withPlayerAt ?? new CellFrame(0x01050100, AcFrame.Identity));
        }

        public PhysicsWorld World { get; }

        public ServerObjectPhysicsHost Host { get; }

        public AvatarSet Set { get; }

        public PhysicsObj? PlayerObj { get; }

        public float PlayerRadius { get; }

        public AvatarViewer Viewer { get; }

        public double Now { get; private set; }

        public AvatarBody Tusker(uint guid, in CellFrame at, float heading)
        {
            var create = AvatarStress.Object(ServerObjectTransitionTests.TuskerSetup, ServerObjectTransitionTests.TuskerTable, isCreature: true) with
            {
                Guid = guid,
                PhysicsState = AvatarStateBits.ReportCollisions | AvatarStateBits.Gravity,
                Position = new CellFrame(at.CellId, new AcFrame(at.Origin, PositionFrames.WithHeading(Quaternion.Identity, heading))),
                HasContact = true,
            };
            Assert.Equal(AvatarApply.Applied, Set.Create(create, Now));
            Assert.True(Host.TryGetObject(guid, out _), "the host keeps no physics object for the tusker");
            return Set.All.Single(x => x.Guid == guid).Body;
        }

        public float Radius(uint guid)
        {
            Assert.True(Host.TryGetObject(guid, out var obj));
            return obj.Setup!.Spheres.Max(s => s.Radius) * obj.Scale;
        }

        /// <summary>Walks <paramref name="guid"/> forward until it is <paramref name="metres"/> from <paramref name="start"/> or <paramref name="seconds"/> pass; then it stops. Answers the cells it was in.</summary>
        public List<uint> WalkUntil(uint guid, Vector3 start, float metres, double seconds)
        {
            Advance(10);
            var body = Set.All.Single(x => x.Guid == guid).Body;
            var cells = new List<uint> { body.Position.CellId };
            Assert.Equal(AvatarApply.Applied, Set.SetMovement(guid, isSelf: false, autonomous: false, AvatarRig.Moving(MotionCommands.WalkForward), []));
            for (var t = 0.0; t < seconds && Horizontal(body.Position.Origin, start) < metres; t += Frame)
            {
                Advance(1);
                if (body.Position.CellId != cells[^1])
                {
                    cells.Add(body.Position.CellId);
                }
            }

            Assert.Equal(AvatarApply.Applied, Set.SetMovement(guid, isSelf: false, autonomous: false, AvatarRig.Moving(MotionCommands.Ready), []));
            Advance(30);
            return cells;
        }

        public void Advance(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                Now += Frame;
                if (PlayerObj is not null && World.UseTime(Now))
                {
                    PlayerObj.UpdateObject();
                }

                Set.Advance(Now, Viewer, long.MaxValue);
            }
        }
    }
}

using System.Numerics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Physics;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Movement.MovementTestKit;

namespace Lifestone.Client.Tests.Movement;

/// <summary>
/// M5 8 npc: <see cref="RouteDriver"/> walks around a solid server object on its leg or goal (the Holtburg ring stalled at goal 7 with the Pumpkin Buffer, wcid 32205, next to the
/// walker, about one run in ten), and still fails named when it cannot. Real offline physics on a flat world: the object is a static server object in the host, a collision shape of
/// about the Pumpkin Buffer's size (radius 0.5 m here; the real one, a sphere of 0.48 m, is read from the dats in the second class).
/// </summary>
public sealed class RouteDriverObstacleTests(ITestOutputHelper output)
{
    private const uint NpcSetupId = 0x020014E0; // the Pumpkin Buffer's Setup id (a stand-in cylinder here)
    private const float NpcRadius = 0.5f;

    [Fact]
    public void M5_8_a_solid_object_on_the_leg_stalls_a_walker_that_does_not_look()
    {
        // The control: the same walk without the obstacle source ends as before (stuck, named), so the objects below are solid and the detour is what finishes the walk.
        var scene = new Scene(npcs: [(60f, 66f)]);
        var driver = new RouteDriver(scene.Controller, scene.RouteNorth());
        var status = scene.Drive(driver, 30.0);
        output.WriteLine($"{status}: {driver.FailureReason}");
        Assert.Equal(RouteStatus.Stuck, status);
        Assert.Contains("no progress toward goal 0", driver.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_14_the_physics_stops_the_walker_at_an_object_that_only_ignores_collisions_and_lets_it_through_an_ethereal_one()
    {
        // The rule RoutePlay.StopsTheBody copies, on the real physics (no obstacle source, so nothing walks around): the Uziz town marker's state, 0x418 (Gravity, IgnoreCollisions,
        // ReportCollisions, not a creature), stalls the walk as a solid object does; the same object made ethereal is walked through and the route finishes.
        const uint marker = (uint)(PhysicsState.Gravity | PhysicsState.IgnoreCollisions | PhysicsState.ReportCollisions);
        Assert.True(RoutePlay.StopsTheBody(marker));
        var blocked = new Scene(npcs: [(60f, 66f)], state: marker, isCreature: false);
        var driver = new RouteDriver(blocked.Controller, blocked.RouteNorth());
        var status = blocked.Drive(driver, 30.0);
        output.WriteLine($"ignores collisions: {status}: {driver.FailureReason}");
        Assert.Equal(RouteStatus.Stuck, status);

        const uint ethereal = (uint)(PhysicsState.Ethereal | PhysicsState.ReportCollisions);
        Assert.False(RoutePlay.StopsTheBody(ethereal));
        var open = new Scene(npcs: [(60f, 66f)], state: ethereal, isCreature: false);
        var through = new RouteDriver(open.Controller, open.RouteNorth());
        var passed = open.Drive(through, 30.0);
        output.WriteLine($"ethereal: {passed}: {through.FailureReason}");
        Assert.Equal(RouteStatus.Done, passed);
    }

    [Fact]
    public void M6_14_an_object_that_only_ignores_collisions_is_walked_around_and_the_route_finishes()
    {
        // With the obstacle source the walker steps round the marker-like object (the live Uziz stall's cure).
        var scene = new Scene(npcs: [(60f, 66f)], state: (uint)(PhysicsState.Gravity | PhysicsState.IgnoreCollisions | PhysicsState.ReportCollisions), isCreature: false);
        var driver = scene.DriverWithObstacles(scene.RouteNorth(), out var notices);
        var status = scene.Drive(driver, 40.0);
        output.WriteLine($"{status} at {scene.Controller.Now:F2} s; {string.Join(" | ", notices)}; {driver.FailureReason}");
        Assert.Equal(RouteStatus.Done, status);
        Assert.Equal(1, driver.DetoursCount);
    }

    [Fact]
    public void M5_8_a_static_object_on_the_leg_is_walked_around_and_the_route_finishes()
    {
        var scene = new Scene(npcs: [(60f, 66f)]);
        var driver = scene.DriverWithObstacles(scene.RouteNorth(), out var notices);
        var status = scene.Drive(driver, 40.0);
        output.WriteLine($"{status} at {scene.Controller.Now:F2} s; {string.Join(" | ", notices)}; {driver.FailureReason}");
        Assert.Equal(RouteStatus.Done, status);
        Assert.Null(driver.FailureReason);
        Assert.Equal(1, driver.DetoursCount);
        var line = Assert.Single(notices);
        Assert.Contains("detour 1 of 2 around Test NPC (wcid 32205", line, StringComparison.Ordinal);
        Assert.Contains("on the leg to the goal", line, StringComparison.Ordinal);
        Assert.Contains("by its", line, StringComparison.Ordinal);
        Assert.True(scene.Controller.Position.GetOffset(At(Block, 60f, 72f, 0f)).Length() <= 0.6f, $"ended at {scene.Controller.Position}");
    }

    [Fact]
    public void M5_8_a_goal_inside_an_objects_cylinder_ends_at_its_edge_and_says_so()
    {
        var scene = new Scene(npcs: [(60f, 66f)]);
        var route = new ScriptedRoute("covered", [new RouteGoal(At(Block, 60f, 66f, 0f), 0.5f, Stop: true, DwellSeconds: 0)]);
        var driver = scene.DriverWithObstacles(route, out var notices);
        var status = scene.Drive(driver, 40.0);
        output.WriteLine($"{status} at {scene.Controller.Now:F2} s; {string.Join(" | ", notices)}; {driver.FailureReason}");
        Assert.Equal(RouteStatus.Done, status);
        var line = Assert.Single(notices);
        Assert.Contains("the goal lies inside the cylinder of Test NPC", line, StringComparison.Ordinal);
        Assert.Contains("arrival accepted at its edge", line, StringComparison.Ordinal);
        var away = scene.Controller.Position.GetOffset(At(Block, 60f, 66f, 0f)).Length();
        Assert.InRange(away, NpcRadius + HumanSetup.Radius - 0.05f, NpcRadius + HumanSetup.Radius + 0.1f + RouteDriver.EdgeToleranceMetres);
    }

    [Fact]
    public void M5_8_a_passage_with_no_free_side_fails_named_after_the_walk_is_judged_stuck()
    {
        // A wall of cylinders across the whole leg: both sides of the first object are probed and refused, so no detour is made and the stall fails the route as before.
        var wall = Enumerable.Range(0, 40).Select(i => (40f + (i * 0.6f), 66f)).ToArray();
        var scene = new Scene(npcs: wall);
        var driver = scene.DriverWithObstacles(scene.RouteNorth(), out var notices);
        var status = scene.Drive(driver, 30.0);
        output.WriteLine($"{status} at {scene.Controller.Now:F2} s; {string.Join(" | ", notices)}; {driver.FailureReason}");
        Assert.Equal(RouteStatus.Stuck, status);
        Assert.Equal(0, driver.DetoursCount);
        Assert.Contains(notices, n => n.Contains("no detour around Test NPC", StringComparison.Ordinal) && n.Contains("no free place on either side", StringComparison.Ordinal));
        Assert.Contains("no progress toward goal 0", driver.FailureReason, StringComparison.Ordinal);
        Assert.Contains("detour given up (no free side beside Test NPC)", driver.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void M5_8_detours_are_bounded_per_goal_and_the_stall_then_fails_with_them_listed()
    {
        // A scripted world where every probe says free but the walker never gets anywhere (the test puts it on each detour point and the leg stays blocked).
        var player = new ScriptedPlayer(At(Block, 60f, 60f, 0f));
        var obstacles = new ScriptedObstacles(new RouteObstacle("Test NPC", 32205, At(Block, 60f, 66f, 0f), NpcRadius));
        var driver = new RouteDriver(player, new ScriptedRoute("bounded", [new RouteGoal(At(Block, 60f, 72f, 0f), 0.5f, Stop: true, DwellSeconds: 0)]))
        {
            Obstacles = obstacles,
        };
        var notices = new List<string>();
        driver.Detoured += notices.Add;
        var now = 0.0;
        var status = RouteStatus.Running;
        while (status == RouteStatus.Running && now < 30.0)
        {
            now += 1.0 / 60.0;
            status = driver.Tick(now);
            if (driver.DetourPoint is { } point)
            {
                // reaches the detour point, a little nearer the object than the point is (touching it), no nearer the goal than a step
                var toward = Vector3.Normalize(At(Block, 60f, 66f, 0f).Frame.Origin - point.Frame.Origin);
                player.Position = new CellFrame(point.CellId, new AcFrame(point.Frame.Origin + (toward * 0.3f), Quaternion.Identity));
            }
        }

        output.WriteLine($"{status} at {now:F2} s; {string.Join(" | ", notices)}; {driver.FailureReason}");
        Assert.Equal(RouteStatus.Stuck, status);
        Assert.Equal(RouteDriver.MaxDetoursPerGoal, driver.DetoursCount);
        Assert.Equal(RouteDriver.MaxDetoursPerGoal, notices.Count);
        Assert.Contains("after 2 detours (around Test NPC", driver.FailureReason, StringComparison.Ordinal);
        Assert.Contains("goal 0", driver.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void M5_8_an_objects_radius_is_its_cylinder_scaled_else_its_sphere_else_none()
    {
        var cylinder = PhysicsSetup.FromParts([new PhysSphere(Vector3.Zero, 0.2f)], [new PhysCylSphere(Vector3.Zero, 2f, 0.4f)]);
        var sphere = PhysicsSetup.FromParts([new PhysSphere(Vector3.Zero, 0.3f)]);
        var nothing = PhysicsSetup.FromParts([]);
        Assert.Equal(0.68f, RouteObstacleShapes.RadiusOf(cylinder, 1.7f)!.Value, 3);
        Assert.Equal(0.3f, RouteObstacleShapes.RadiusOf(sphere, 1f)!.Value, 3);
        Assert.Null(RouteObstacleShapes.RadiusOf(nothing, 1f));
    }

    /// <summary>A flat world with a player, a host with static solid objects, and a route driver's tools.</summary>
    private sealed class Scene
    {
        private readonly List<RouteObstacle> _npcs = [];

        public Scene((float X, float Y)[] npcs, uint state = (uint)PhysicsState.ReportCollisions, bool isCreature = true)
        {
            var (controller, _, world) = FlatController(x: 60f, y: 60f);
            Controller = controller;
            World = world;
            Run(controller, 0.5);
            var setup = PhysicsSetup.FromParts([], [new PhysCylSphere(Vector3.Zero, 2.0f, NpcRadius)], height: 2.0f, radius: NpcRadius, id: NpcSetupId);
            var host = new ServerObjectPhysicsHost(world, id => id == NpcSetupId ? setup : null);
            var guid = 0x800032D3u;
            foreach (var (x, y) in npcs)
            {
                var at = At(Block, x, y, 0f);
                host.Create(new ServerObjectDescription(guid++, NpcSetupId, 1f, state, IsCreature: isCreature, IsSelf: false, DescriptionFlags: null), at);
                _npcs.Add(new RouteObstacle("Test NPC", 32205, at, NpcRadius));
            }
        }

        public PlayerMovementController Controller { get; }

        public PhysicsWorld World { get; }

        public ScriptedRoute RouteNorth() => new("north", [new RouteGoal(At(Block, 60f, 72f, 0f), 0.5f, Stop: true, DwellSeconds: 0)]);

        public RouteDriver DriverWithObstacles(ScriptedRoute route, out List<string> notices)
        {
            var driver = new RouteDriver(Controller, route)
            {
                Obstacles = new WorldRouteObstacles(World, HumanSetup, () => _npcs),
            };
            var list = new List<string>();
            driver.Detoured += list.Add;
            notices = list;
            return driver;
        }

        public RouteStatus Drive(RouteDriver driver, double limit)
        {
            var now = Controller.Now;
            var end = now + limit;
            var status = RouteStatus.Running;
            while (status == RouteStatus.Running && now < end)
            {
                now += 1.0 / 60.0;
                Controller.Tick(now);
                status = driver.Tick(now);
            }

            return status;
        }
    }

    /// <summary>A player that goes nowhere unless the test moves it.</summary>
    private sealed class ScriptedPlayer(CellFrame position) : IPlayerMovementIntents
    {
        public CellFrame Position { get; set; } = position;

        public bool OnGround => true;

        public void HandleCommand(uint command, bool start, float speed = 1f)
        {
        }

        public void SetHoldRun(bool on)
        {
        }

        public void SetHoldSidestep(bool on)
        {
        }

        public void ToggleAutoRun()
        {
        }

        public void CommenceJump(double now)
        {
        }

        public void DoJump(double now)
        {
        }
    }

    /// <summary>One object, and a probe that finds every place free.</summary>
    private sealed class ScriptedObstacles(RouteObstacle obstacle) : IRouteObstacles
    {
        public float BodyRadius => 0.48f;

        public void Near(in CellFrame at, float metres, List<RouteObstacle> into) => into.Add(obstacle);

        public bool TryFindStand(in CellFrame origin, Vector2 offset, out CellFrame standing)
        {
            standing = new CellFrame(origin.CellId, new AcFrame(origin.Frame.Origin + new Vector3(offset.X, offset.Y, 0f), Quaternion.Identity));
            return true;
        }
    }
}

/// <summary>M6 14 (live 2026-11-13, Uziz): the walk's obstacles are the objects the body's own physics stops it at (<see cref="Lifestone.Client.Session.Scenarios.RoutePlay"/>).</summary>
public sealed class RouteObstacleFilterTests
{
    [Fact]
    public void M6_14_an_object_that_only_ignores_collisions_is_walked_around_because_the_body_is_stopped_by_it()
    {
        // The Uziz town marker (wcid 49606) as ACE sends it: Gravity 0x400, IgnoreCollisions 0x10, ReportCollisions 0x8; not Ethereal 0x4.
        Assert.True(RoutePlay.StopsTheBody(0x418));
        Assert.True(RoutePlay.StopsTheBody(0x0)); // a plain solid object
    }

    [Fact]
    public void M6_14_an_ethereal_object_is_not_walked_around_whether_or_not_it_also_ignores_collisions()
    {
        Assert.False(RoutePlay.StopsTheBody((uint)PhysicsState.Ethereal));
        Assert.False(RoutePlay.StopsTheBody((uint)(PhysicsState.Ethereal | PhysicsState.IgnoreCollisions)));
    }
}

/// <summary>M6 14: what the retail dats say about the Uziz town marker that stopped the walk from the Town Network's drop (wcid 49606, Setup 0x020006E7).</summary>
public sealed class UzizMarkerShapeTests(Lifestone.Data.Tests.DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<Lifestone.Data.Tests.DatSetFixture>
{
    [RequiresDatsFact]
    public void M6_14_the_uziz_markers_setup_has_a_collision_shape_the_walker_can_go_round()
    {
        // wcid 49606's Setup (ACE world database v0.9.296: weenie_properties_d_i_d type 1 = 0x020006E7; no DefaultScale row, so 1).
        var setup = PhysicsSetup.Load(fixture.Dats, 0x020006E7);
        Assert.NotNull(setup);
        var radius = RouteObstacleShapes.RadiusOf(setup, 1f);
        output.WriteLine($"cylinders {string.Join(", ", setup.CylSpheres.Select(c => c.Radius))}; spheres {string.Join(", ", setup.Spheres.Select(s => s.Radius))}; physics bsp {setup.HasPhysicsBsp}; radius: {radius}");
        Assert.NotNull(radius);
        Assert.InRange(radius.Value, 0.2f, 2.0f);
    }
}

/// <summary>M5 8 npc: what the retail dats say about the object that stalled the Holtburg ring.</summary>
public sealed class PumpkinBufferShapeTests(Lifestone.Data.Tests.DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<Lifestone.Data.Tests.DatSetFixture>
{
    [RequiresDatsFact]
    public void M5_8_the_pumpkin_buffers_setup_has_a_collision_cylinder_the_walker_can_go_round()
    {
        // wcid 32205's Setup (ACE world database v0.9.296: weenie_properties_d_i_d type 1 = 33559776 = 0x020014E0, DefaultScale 1.7).
        var setup = PhysicsSetup.Load(fixture.Dats, 0x020014E0);
        Assert.NotNull(setup);
        var radius = RouteObstacleShapes.RadiusOf(setup, 1.7f);
        output.WriteLine($"cylinders {string.Join(", ", setup.CylSpheres.Select(c => c.Radius))}; spheres {string.Join(", ", setup.Spheres.Select(s => s.Radius))}; physics bsp {setup.HasPhysicsBsp}; radius at scale 1.7: {radius}");
        Assert.NotNull(radius);
        Assert.InRange(radius.Value, 0.2f, 2.0f);
    }
}

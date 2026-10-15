using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;
using static Lifestone.Data.Tests.Physics.PhysicsTestKit;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// The CTransition machine (11.4186 PORTAL/cphysics/CTransition.c) on synthetic worlds: flat ground, walls, steps,
/// slopes either side of FloorZ, ledges with and without EdgeSlide, and a sphere too fast for one step.
/// </summary>
public sealed class TransitionSyntheticTests
{
    private const float Radius = 0.5f;

    [Fact]
    public void A_placed_sphere_rests_on_flat_ground_with_a_walkable_contact_plane()
    {
        var body = new PhysicsTestBody(WorldOf(Flat(Block)));
        Assert.True(body.Place(At(Block, 100f, 100f, 0.3f)));
        AssertNear(0f, body.Position.Frame.Origin.Z);
        Assert.True(body.HasContact);
        Assert.True(body.OnWalkable);
        Assert.Equal(Vector3.UnitZ, body.ContactPlane.Normal);
        Assert.Equal(LandCellAt(Block, 100f, 100f), body.Position.CellId);
    }

    [Fact]
    public void On_a_cell_corner_the_last_cell_found_that_holds_the_centre_is_the_cell()
    {
        // CObjCell::find_cell_list keeps the last land cell that contains the centre: the cell under the point first,
        // then its west, south-west and south neighbours (CLandCell::check_add_cell_boundary's order).
        var body = new PhysicsTestBody(WorldOf(Flat(Block)));
        Assert.True(body.Place(At(Block, 96f, 96f, 0f)));
        Assert.Equal(LandCellAt(Block, 96f, 95f), body.Position.CellId);
        Assert.Equal([LandCellAt(Block, 96f, 96f), LandCellAt(Block, 95f, 96f), LandCellAt(Block, 95f, 95f), LandCellAt(Block, 96f, 95f)], body.Cells);
    }

    [Fact]
    public void Walking_on_flat_ground_keeps_its_height_and_contact()
    {
        var body = new PhysicsTestBody(WorldOf(Flat(Block)));
        Assert.True(body.Place(At(Block, 90f, 96f, 0f)));
        for (var i = 0; i < 40; i++)
        {
            Assert.True(body.MoveBy(new Vector3(0.25f, 0.05f, 0f)), $"step {i}");
            AssertNear(0f, body.Position.Frame.Origin.Z);
            Assert.True(body.HasContact && body.OnWalkable, $"step {i}");
        }

        AssertNear(100f, body.Position.Frame.Origin.X, 1e-3f);
        AssertNear(98f, body.Position.Frame.Origin.Y, 1e-3f);
        Assert.Equal(LandCellAt(Block, 100f, 98f), body.Position.CellId);
    }

    [Fact]
    public void A_wall_stops_the_sphere_and_it_slides_along_the_wall()
    {
        // A wall 20 m long, 3 m high, 0.2 m thick, its west face at x = 100.
        var wall = Box(Block, 100.1f, 96f, 0f, new Vector3(0.2f, 20f, 3f));
        var body = new PhysicsTestBody(WorldOf(Flat(Block, wall)));
        Assert.True(body.Place(At(Block, 98f, 92f, 0f)));
        for (var i = 0; i < 20; i++)
        {
            body.MoveBy(new Vector3(0.2f, 0.2f, 0f));
        }

        // Stopped short of the wall's face by less than a step (retail does not clip an object in contact to the moment
        // of contact; the refused step slides from where it was), and still moving north along it: the slide direction
        // is cross(collision normal, contact normal) = cross(-X, +Z) = +Y, and the sliding normal then keeps every
        // later step off the wall.
        var origin = body.Position.Frame.Origin;
        Assert.InRange(origin.X, 100f - Radius - 0.2f, 100f - Radius);
        AssertNear(96f, origin.Y, 1e-3f);
        AssertNear(0f, origin.Z);
        Assert.True(body.HasContact);
    }

    [Theory]
    [InlineData(1e-4f, true)]
    [InlineData(-1e-4f, false)]
    public void A_slope_is_climbed_only_when_its_normal_is_at_least_FloorZ(float margin, bool climbs)
    {
        // Flat to x = 96, then a ramp along +X whose plane normal has Z = FloorZ + margin.
        var normalZ = PhysicsGlobals.FloorZ + margin;
        var rise = MathF.Sqrt((1f / (normalZ * normalZ)) - 1f) * 24f;
        var terrain = TerrainOf(Block, (x, _) => (byte)Math.Max(0, x - 4));
        var block = LandblockCollisionBuilder.FromTerrain(terrain, Heights(i => i * rise));
        var body = new PhysicsTestBody(WorldOf(block));
        Assert.True(body.Place(At(Block, 90f, 100f, 0f)));
        for (var i = 0; i < 60; i++)
        {
            body.MoveBy(new Vector3(0.25f, 0f, 0f));
        }

        var origin = body.Position.Frame.Origin;
        if (climbs)
        {
            // Each step's offset is projected onto the slope (CTransition::adjust_offset), so the climb is slower.
            Assert.True(origin.X > 99f, $"stopped at {body.Position}");
            AssertNear((origin.X - 96f) * rise / 24f, origin.Z, 1e-3f);
            AssertNear(normalZ, body.ContactPlane.Normal.Z, 1e-5f);
            Assert.True(body.OnWalkable);
        }
        else
        {
            // An EdgeSlide object stops at the foot of the slope (CTransition::edge_slide's cliff slide).
            Assert.True(origin.X < 96.5f, $"climbed to {body.Position}");
            Assert.True(origin.Z < 0.5f, $"climbed to {body.Position}");
            Assert.True(body.OnWalkable);
        }
    }

    [Theory]
    [InlineData(0.9f, true)]
    [InlineData(1.4f, true)]
    [InlineData(1.6f, false)]
    public void A_step_is_climbed_up_to_one_and_a_half_step_up_heights(float heightInSteps, bool climbs)
    {
        // CTransition::step_up steps down by the step-up height onto the step's top; CPolygon::adjust_sphere_to_plane lets
        // that landing use up to half a step more (a walk interpolation down to -0.5), so a physics BSP step up to 1.5
        // step-up heights is climbed (the 0.4 m step-up of this body: 0.6 m).
        var height = heightInSteps * 0.4f;
        var step = Box(Block, 101f, 100f, 0f, new Vector3(2f, 10f, height));
        var body = new PhysicsTestBody(WorldOf(Flat(Block, step)), stepUpHeight: 0.4f);
        Assert.True(body.Place(At(Block, 98f, 100f, 0f)));
        for (var i = 0; i < 20; i++)
        {
            body.MoveBy(new Vector3(0.2f, 0f, 0f));
        }

        var origin = body.Position.Frame.Origin;
        if (climbs)
        {
            AssertNear(height, origin.Z, 1e-3f);
            Assert.True(origin.X > 100.5f, $"stopped at {body.Position}");
            Assert.True(body.OnWalkable);
        }
        else
        {
            AssertNear(0f, origin.Z, 1e-3f);
            Assert.InRange(origin.X, 100f - Radius - 0.2f, 100f - Radius);
        }
    }

    [Theory]
    [InlineData(0.04f)]
    [InlineData(0.35f)]
    public void Walking_off_a_ledge_lower_than_the_step_down_height_keeps_contact(float drop)
    {
        // Standing on a platform `drop` high, walking east off its edge onto the ground.
        var platform = Box(Block, 95f, 100f, 0f, new Vector3(10f, 10f, drop));
        var body = new PhysicsTestBody(WorldOf(Flat(Block, platform)), stepDownHeight: 0.4f);
        Assert.True(body.Place(At(Block, 98f, 100f, drop)));
        AssertNear(drop, body.Position.Frame.Origin.Z);
        for (var i = 0; i < 20; i++)
        {
            body.MoveBy(new Vector3(0.2f, 0f, 0f));
            Assert.True(body.HasContact, $"lost contact at {body.Position}");
        }

        AssertNear(0f, body.Position.Frame.Origin.Z, 1e-3f);
        AssertNear(102f, body.Position.Frame.Origin.X, 1e-3f);
    }

    [Fact]
    public void An_edge_sliding_object_slides_along_a_high_ledge_and_one_without_EdgeSlide_walks_off()
    {
        var platform = Box(Block, 95f, 100f, 0f, new Vector3(10f, 20f, 2f));
        var sliding = new PhysicsTestBody(WorldOf(Flat(Block, platform)));
        Assert.True(sliding.Place(At(Block, 98f, 96f, 2f)));
        for (var i = 0; i < 20; i++)
        {
            sliding.MoveBy(new Vector3(0.2f, 0.2f, 0f));
        }

        // It stays on the platform, its centre at most half a radius past the edge (SPHEREPATH::check_walkables halves the
        // sphere), and moves on along the edge (SPHEREPATH::precipice_slide).
        var origin = sliding.Position.Frame.Origin;
        AssertNear(2f, origin.Z, 1e-3f);
        Assert.InRange(origin.X, 99f, 100f + (Radius * 0.5f) + 0.2f);
        Assert.True(origin.Y > 99f, $"did not slide along the edge: {sliding.Position}");
        Assert.True(sliding.HasContact);

        var walking = new PhysicsTestBody(WorldOf(Flat(Block, platform)), state: PhysicsState.DefaultForNewObject & ~PhysicsState.EdgeSlide);
        Assert.True(walking.Place(At(Block, 98f, 96f, 2f)));
        for (var i = 0; i < 20; i++)
        {
            walking.MoveBy(new Vector3(0.2f, 0.2f, 0f));
        }

        // Past the edge it has nothing under it: no contact, still at the platform's height (falling is the physics
        // object's gravity, not the transition's).
        Assert.True(walking.Position.Frame.Origin.X > 101f, $"stopped at {walking.Position}");
        Assert.False(walking.HasContact);
        AssertNear(2f, walking.Position.Frame.Origin.Z, 1e-3f);
    }

    [Fact]
    public void A_sphere_moving_50_m_in_one_transition_does_not_tunnel_through_a_1_cm_wall()
    {
        // 50 m/s for a 0.2 s physics step is 10 m: CTransition::calc_num_steps cuts it into steps of at most one radius.
        var wall = Box(Block, 100.005f, 100f, 0f, new Vector3(0.01f, 20f, 3f));
        var body = new PhysicsTestBody(WorldOf(Flat(Block, wall)));
        Assert.True(body.Place(At(Block, 95f, 100f, 0f)));
        body.MoveBy(new Vector3(10f, 0f, 0f));
        var origin = body.Position.Frame.Origin;
        Assert.InRange(origin.X, 100f - Radius - 0.5f, 100f - Radius);
        Assert.Equal(-Vector3.UnitX, body.CollisionNormal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_stationary_fall_failsafe_gives_a_still_falling_object_a_floor_on_its_third_step(bool inContact)
    {
        // CTransition::validate_transition: an object with gravity whose step ends where it began counts frames (carried
        // between transitions by the physics object); on the third it gets a horizontal plane under its sphere, and only
        // an object without contact also records it as an environment collision.
        var transition = new Transition();
        transition.InitObject(new object(), inContact ? ObjectInfoState.Contact : ObjectInfoState.None, PhysicsState.Gravity, 1f, 0f, 0f);
        transition.InitSphere([new PhysSphere(new Vector3(0, 0, 0.5f), 0.5f)], 1f);
        var at = new CellFrame(LandCellAt(Block, 10f, 10f), new AcFrame(new Vector3(10f, 10f, 5f), Quaternion.Identity));
        transition.InitPath(null, at, at);
        var collisions = transition.CollisionInfo;
        for (var frame = 1; frame <= 3; frame++)
        {
            // Each frame is a new transition: the object's flags come from its own state again.
            transition.ObjectInfo.State = inContact ? ObjectInfoState.Contact : ObjectInfoState.None;
            transition.SpherePath.SetCheckPos(transition.SpherePath.CurPos, null);
            Assert.Equal(TransitionState.OK, transition.ValidateTransition(TransitionState.OK, out _));
            Assert.Equal(frame, collisions.FramesStationaryFall);
            Assert.Equal(frame == 3, collisions.ContactPlaneValid);
        }

        Assert.Equal(new Plane(Vector3.UnitZ, -5f), collisions.ContactPlane);
        Assert.Equal(!inContact, collisions.CollidedWithEnvironment);
        Assert.Equal(!inContact, collisions.CollisionNormalValid);
        Assert.True((transition.ObjectInfo.State & ObjectInfoState.OnWalkable) != 0);

        // A step that moves the object resets the count.
        transition.SpherePath.SetCheckPos(transition.SpherePath.CurPos.AddOffset(new Vector3(0.1f, 0, 0)), null);
        transition.ValidateTransition(TransitionState.OK, out _);
        Assert.Equal(0, collisions.FramesStationaryFall);
    }

    [Fact]
    public void Retail_has_no_step_cap_and_the_ace_switch_gives_up_above_1000_steps()
    {
        // A 0.05 m sphere moving 60 m takes 1,200 steps (CTransition::calc_num_steps).
        foreach (var (compat, moves) in new[] { (PhysicsCompat.Retail, true), (PhysicsCompat.Ace, false) })
        {
            var body = new PhysicsTestBody(WorldOf(compat, Flat(Block)), radius: 0.05f);
            Assert.True(body.Place(At(Block, 60f, 100f, 0f)));
            Assert.Equal(moves, body.MoveBy(new Vector3(60f, 0f, 0f)));
            AssertNear(moves ? 120f : 60f, body.Position.Frame.Origin.X, 1e-2f);
        }
    }

    [Fact]
    public void Retail_check_walkable_keeps_its_own_backup_and_the_ace_switch_overwrites_the_paths()
    {
        foreach (var (compat, overwritten) in new[] { (PhysicsCompat.Retail, false), (PhysicsCompat.Ace, true) })
        {
            var transition = new Transition(compat: compat);
            transition.InitObject(new object(), ObjectInfoState.Contact | ObjectInfoState.OnWalkable, PhysicsState.Gravity, 1f, 0.4f, 0.4f);
            transition.InitSphere([new PhysSphere(new Vector3(0, 0, 0.5f), 0.5f)], 1f);
            var start = new CellFrame(LandCellAt(Block, 10f, 10f), new AcFrame(new Vector3(10f, 10f, 0f), Quaternion.Identity));
            transition.InitPath(null, start, start);
            var path = transition.SpherePath;
            path.SetCheckPos(start.AddOffset(new Vector3(1f, 0, 0)), null);

            // A walkable polygon the sphere no longer stands over, so the trial step down runs.
            var far = Polygon(0, new(50, 50, 0), new(51, 50, 0), new(51, 51, 0));
            path.SetWalkable(new PhysSphere(new Vector3(0, 0, 0.5f), 0.5f), far, Vector3.UnitZ, start, 1f);
            var saved = start.AddOffset(new Vector3(0, 7f, 0));
            path.BackupCheckPos = saved;

            transition.CheckWalkable(PhysicsGlobals.FloorZ);
            Assert.Equal(overwritten ? start.AddOffset(new Vector3(1f, 0, 0)) : saved, path.BackupCheckPos);
            Assert.Equal(start.AddOffset(new Vector3(1f, 0, 0)), path.CheckPos);
        }
    }

    [Fact]
    public void The_camera_sweep_stops_at_a_wall_and_runs_free_in_the_open()
    {
        var wall = Box(Block, 100.1f, 100f, 0f, new Vector3(0.2f, 20f, 5f));
        var world = WorldOf(Flat(Block, wall));
        var from = At(Block, 95f, 100f, 1.5f);

        // Retail's viewer moves in steps of exactly its radius (0.3 m) and clips to the first contact
        // (BSPTREE::adjust_to_plane), so it stops just short of the wall, never past it.
        var blocked = world.SweepViewer(from, from.AddOffset(new Vector3(10f, 0, 0)), 0.3f);
        Assert.True(blocked.Blocked);
        Assert.InRange(blocked.Position.Frame.Origin.X, 100f - 0.3f - 0.05f, 100f - 0.3f + PhysicsGlobals.Epsilon);
        AssertNear(1.5f, blocked.Position.Frame.Origin.Z);

        var open = world.SweepViewer(from, from.AddOffset(new Vector3(-10f, 3f, 0)), 0.3f);
        Assert.False(open.Blocked);
        Assert.True(open.Position.GetOffset(from.AddOffset(new Vector3(-10f, 3f, 0))).Length() < 1e-3f);
        Assert.Equal(0, world.Transitions.InUse);
    }

    private static void AssertNear(float expected, float actual, float tolerance = 1e-4f) =>
        Assert.True(MathF.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");
}

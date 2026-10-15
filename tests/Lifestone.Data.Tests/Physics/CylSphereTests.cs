using System.Numerics;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// <see cref="PhysCylSphere"/> against retail <c>CCylSphere</c> (11.4186 PORTAL/cgeometry/CCylSphere.c), with the cases
/// where ACE's port differs.
/// </summary>
public sealed class CylSphereTests
{
    /// <summary>A 1 m tall post of radius 0.5 standing at the origin.</summary>
    private static readonly PhysCylSphere Post = new(Vector3.Zero, 1.0f, 0.5f);

    [Theory]
    [InlineData(0.9f, 0f, 0.5f, true)]     // beside it, within the radius sum
    [InlineData(1.1f, 0f, 0.5f, false)]    // beside it, beyond the radius sum
    [InlineData(0f, 0f, 1.4f, true)]       // resting into the top
    [InlineData(0f, 0f, 1.6f, false)]      // above the top
    [InlineData(0f, 0f, -0.4f, true)]      // under the bottom, overlapping
    public void A_sphere_collides_within_the_radius_sum_and_the_height(float x, float y, float z, bool collides)
    {
        var sphere = new PhysSphere(new Vector3(x, y, z), 0.5f);
        Assert.Equal(collides, Post.CollidesWithSphere(sphere, sphere.Center - Post.LowPoint, 1.0f - PhysicsGlobals.Epsilon));
    }

    [Fact]
    public void Without_perfect_clip_a_hit_records_the_normal_and_is_refused()
    {
        var transition = PathWithOneSphere(current: new Vector3(1.2f, 0, 0.5f), check: new Vector3(0.9f, 0, 0.5f));
        ref readonly var check = ref transition.SpherePath.GlobalSphere[0];
        Assert.Equal(TransitionState.Collided, Post.CollideWithPoint(transition, check, check.Center - Post.LowPoint, 0.9998f, 0));
        Assert.Equal(Vector3.UnitX, transition.CollisionInfo.CollisionNormal);
    }

    [Fact]
    public void Retail_clips_a_sphere_falling_onto_the_top_to_rest_on_it()
    {
        // Above and beside the post, moving down and in: not a certain hit, so retail clips by height first. The time
        // is (radius + height - old displacement z) / movement z = (0.5 + 1 - 2) / -1 = 0.5, and the sphere is moved to
        // rest on the top. ACE divides by the movement's own z (time -2.5, refused) and would subtract the clipped
        // movement (CylSphere.cs:120-151).
        var transition = PathWithOneSphere(current: new Vector3(1.2f, 0, 2.0f), check: new Vector3(0.5f, 0, 1.0f));
        transition.ObjectInfo.State = ObjectInfoState.PerfectClip;
        var path = transition.SpherePath;

        var state = Post.CollideWithPoint(transition, path.GlobalSphere[0], path.GlobalSphere[0].Center - Post.LowPoint, 0.9998f, 0);

        Assert.Equal(TransitionState.Adjusted, state);
        AssertVector(new Vector3(0.85f, 0, 1.5f), path.GlobalSphere[0].Center);
        AssertVector(new Vector3(0.85f, 0, 1.5f), path.CheckPos.Frame.Origin);
        Assert.Equal(Vector3.UnitZ, transition.CollisionInfo.CollisionNormal);
    }

    [Fact]
    public void Stepping_down_onto_the_top_records_its_plane_and_cell()
    {
        // The step-down lowered the sphere 0.5 m into the post (bottom at 0.6); it is raised 0.4 m to rest on the top.
        var transition = PathWithOneSphere(current: new Vector3(0.2f, 0, 1.6f), check: new Vector3(0.2f, 0, 1.1f));
        var path = transition.SpherePath;
        path.StepDownAmt = 0.5f;
        path.WalkInterp = 1.0f;
        path.CheckPos = path.CheckPos with { CellId = 0xA9B40019 };

        var state = Post.StepSphereDown(transition, path.GlobalSphere[0], path.GlobalSphere[0].Center - Post.LowPoint, 0.9998f);

        Assert.Equal(TransitionState.Adjusted, state);
        Assert.Equal(1.5f, path.GlobalSphere[0].Center.Z, 5);
        Assert.Equal(0.2f, path.WalkInterp, 5);
        var collisions = transition.CollisionInfo;
        Assert.True(collisions.ContactPlaneValid);
        Assert.Equal(Vector3.UnitZ, collisions.ContactPlane.Normal);
        Assert.Equal(-1.0f, collisions.ContactPlane.D, 5);
        Assert.True(collisions.ContactPlaneIsWater);              // retail passes 1 for every cylinder top
        Assert.Equal(0xA9B40019u, collisions.ContactPlaneCellId); // ACE leaves the cell unset (CylSphere.cs:418)
    }

    [Fact]
    public void The_second_sphere_slides_with_its_own_displacement()
    {
        // The head sphere clears the post; the lower sphere, moving down from 2.3 to 2.1, overlaps the top. Retail hands
        // the lower sphere's displacement to the slide, so the normal points up (2.1 - 2.3 <= 0); ACE hands over the
        // head's (3.5 - 2.3 > 0) and the normal points down (CylSphere.cs:279).
        var post = new PhysCylSphere(Vector3.Zero, 2.0f, 0.5f);
        var transition = new Transition();
        transition.ObjectInfo.State = ObjectInfoState.Contact;
        var path = transition.SpherePath;
        path.NumSphere = 2;
        path.GlobalSphere[0] = new PhysSphere(new Vector3(0, 0, 3.5f), 0.3f);
        path.GlobalSphere[1] = new PhysSphere(new Vector3(0, 0, 2.1f), 0.3f);
        path.GlobalCurrCenter[0] = new Vector3(0, 0, 3.7f);
        path.GlobalCurrCenter[1] = new Vector3(0, 0, 2.3f);
        transition.CollisionInfo.SetContactPlane(new Plane(Vector3.UnitZ, 0), isWater: false);

        Assert.Equal(TransitionState.Slid, post.IntersectsSphere(transition));
        Assert.Equal(Vector3.UnitZ, transition.CollisionInfo.CollisionNormal);
        Assert.Equal(2.3f, path.GlobalSphere[1].Center.Z, 5);
    }

    [Fact]
    public void Placement_and_the_walkable_check_only_ask_whether_a_sphere_overlaps()
    {
        var transition = PathWithOneSphere(current: new Vector3(3, 0, 0.5f), check: new Vector3(0.9f, 0, 0.5f));
        var path = transition.SpherePath;
        path.InsertType = InsertType.Placement;
        Assert.Equal(TransitionState.Collided, Post.IntersectsSphere(transition));

        path.GlobalSphere[0].Center = new Vector3(1.1f, 0, 0.5f);
        Assert.Equal(TransitionState.OK, Post.IntersectsSphere(transition));

        path.InsertType = InsertType.Transition;
        path.CheckWalkable = true;
        Assert.Equal(TransitionState.OK, Post.IntersectsSphere(transition));
        path.GlobalSphere[0].Center = new Vector3(0.9f, 0, 0.5f);
        Assert.Equal(TransitionState.Collided, Post.IntersectsSphere(transition));
    }

    [Fact]
    public void A_falling_sphere_lands_on_the_cylinder()
    {
        var transition = PathWithOneSphere(current: new Vector3(0, 0, 1.8f), check: new Vector3(0, 0, 1.4f));
        Assert.Equal(TransitionState.Adjusted, Post.IntersectsSphere(transition));
        Assert.True(transition.SpherePath.Collide);
        Assert.Equal(Vector3.UnitZ, transition.SpherePath.StepUpNormal);
        Assert.Equal(PhysicsGlobals.LandingZ, transition.SpherePath.WalkableAllowance);
    }

    [Fact]
    public void A_step_up_with_no_ground_in_reach_becomes_a_slide_along_the_post()
    {
        // On the ground and walking into a step low enough to climb: CTransition::step_up tries to step down onto the
        // top, which finds no ground here (the path is in no cell), so the check position is put back and the sphere
        // slides along the post's side instead (SPHEREPATH::step_up_slide), keeping its move along the tangent.
        var transition = PathWithOneSphere(current: new Vector3(1.2f, 0, 0.5f), check: new Vector3(0.9f, 0.1f, 0.5f));
        transition.ObjectInfo.State = ObjectInfoState.Contact | ObjectInfoState.OnWalkable;
        transition.ObjectInfo.StepUpHeight = 2.0f;
        Assert.Equal(TransitionState.Slid, Post.IntersectsSphere(transition));
        Assert.False(transition.SpherePath.StepUp);
        AssertVector(new Vector3(1.2f, 0.1f, 0.5f), transition.SpherePath.GlobalSphere[0].Center);

        // Too high to climb: the sphere slides along the side at once.
        transition = PathWithOneSphere(current: new Vector3(1.2f, 0, 0.5f), check: new Vector3(0.9f, 0.1f, 0.5f));
        transition.ObjectInfo.State = ObjectInfoState.Contact | ObjectInfoState.OnWalkable;
        transition.ObjectInfo.StepUpHeight = 0.1f;
        transition.CollisionInfo.SetContactPlane(new Plane(Vector3.UnitZ, 0), isWater: false);
        Assert.Equal(TransitionState.Slid, Post.IntersectsSphere(transition));
        AssertVector(new Vector3(1.2f, 0.1f, 0.5f), transition.SpherePath.GlobalSphere[0].Center);
    }

    /// <summary>A transition whose single 0.5 m sphere moves from <paramref name="current"/> to <paramref name="check"/>.</summary>
    private static Transition PathWithOneSphere(Vector3 current, Vector3 check)
    {
        var transition = new Transition();
        var path = transition.SpherePath;
        path.NumSphere = 1;
        path.LocalSphere[0] = new PhysSphere(Vector3.Zero, 0.5f);
        path.GlobalCurrCenter[0] = current;
        path.GlobalSphere[0] = new PhysSphere(check, 0.5f);
        path.CheckPos = path.CheckPos.AddOffset(check);
        return transition;
    }

    private static void AssertVector(Vector3 expected, Vector3 actual, float tolerance = 1e-5f)
    {
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");
    }
}

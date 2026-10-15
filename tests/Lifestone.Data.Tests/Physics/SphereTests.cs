// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server.Tests/Physics/SphereTests.cs (AGPL-3.0)
// with changes: MSTest to xUnit; Lifestone's PhysSphere, Ray and transition state; exact expected values instead of
// ACE's one-sided ones; where ACE departs from retail the retail result is expected and the difference is named (the
// sign of find_time_of_collision, the last branch of slide_sphere); retail-only cases added for collide_with_point and
// find_time_of_collision.

using System.Numerics;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// ACE's sphere tests, one for one, on <see cref="PhysSphere"/>, checked against retail <c>CSphere</c> (11.4186
/// PORTAL/cgeometry/CSphere.c).
/// </summary>
public sealed class SphereTests
{
    private const double Tolerance = 1e-5;

    [Fact]
    public void Sphere_Intersection()
    {
        var sphere = new PhysSphere(Vector3.Zero, 5.0f);
        Assert.True(sphere.Intersects(new PhysSphere(new Vector3(5, 5, 5), 5.0f)));
        Assert.False(sphere.Intersects(new PhysSphere(new Vector3(6, 6, 6), 5.0f)));
        Assert.False(sphere.Intersects(new PhysSphere(new Vector3(10, 0, 0), 5.0f)));   // touching is not intersecting
        Assert.False(sphere.Intersects(new PhysSphere(new Vector3(10, 10, 10), 3.0f)));
        Assert.True(sphere.Intersects(new PhysSphere(new Vector3(1, 1, 1), 1.0f)));
    }

    [Fact]
    public void Sphere_FindTimeOfCollision()
    {
        var movement = new Vector3(100, 100, 100);

        // Moving away: both roots are in the past. Retail returns the larger one, -0.3845...; ACE returns its negation,
        // +0.3845 (Sphere.cs:245-248), which its own test checked only from one side.
        Assert.Equal(-0.38452994616207481, PhysSphere.FindTimeOfCollision(movement, new Vector3(50, 50, 50), 20.0f), Tolerance);
        Assert.Equal(-0.46125741132772069, PhysSphere.FindTimeOfCollision(movement, new Vector3(50, 60, 60), 20.0f), Tolerance);
        Assert.Equal(-1.0, PhysSphere.FindTimeOfCollision(movement, new Vector3(30, 42, 63), 20.0f));
    }

    [Fact]
    public void Retail_find_time_of_collision_is_the_first_contact_when_approaching()
    {
        // From (50, 50, 50) towards the origin by (-100, -100, -100): the centres are 20 apart when 50 - 100t = 20/sqrt(3).
        var time = PhysSphere.FindTimeOfCollision(new Vector3(-100, -100, -100), new Vector3(50, 50, 50), 20.0f);
        Assert.Equal((50.0 - (20.0 / Math.Sqrt(3.0))) / 100.0, time, Tolerance);

        // Already overlapping (within the epsilon) or not moving: no time.
        Assert.Equal(-1.0, PhysSphere.FindTimeOfCollision(new Vector3(1, 0, 0), new Vector3(10, 0, 0), 10.0f));
        Assert.Equal(-1.0, PhysSphere.FindTimeOfCollision(new Vector3(0.01f, 0, 0), new Vector3(50, 0, 0), 10.0f));
    }

    [Fact]
    public void Sphere_IntersectsRay()
    {
        var sphere = new PhysSphere(Vector3.Zero, 5.0f);

        Assert.True(sphere.SphereIntersectsRay(Ray.FromOffset(new Vector3(-10, -10, -10), new Vector3(20, 20, 20)), out var time));
        Assert.Equal(12.320511131409415, time, 1e-4);

        Assert.True(sphere.SphereIntersectsRay(Ray.FromOffset(new Vector3(20, 25, 30), new Vector3(-20, -25, -30)), out time));
        Assert.Equal(38.874800109922887, time, 1e-4);

        // A ray starting inside the sphere is not an intersection.
        Assert.False(sphere.SphereIntersectsRay(Ray.FromOffset(new Vector3(1, 1, 1), new Vector3(-20, -20, -20)), out time));
        Assert.Equal(0.0, time);

        // A sphere behind the start still intersects, with a negative time.
        Assert.True(sphere.SphereIntersectsRay(Ray.FromOffset(new Vector3(50, 50, 50), new Vector3(100, 100, 100)), out time));
        Assert.Equal(-81.602495098501151, time, 1e-4);
    }

    [Fact]
    public void Sphere_CollideWithPoint()
    {
        var sphere = new PhysSphere(Vector3.Zero, 5.0f);
        var transition = new Transition();
        var path = transition.SpherePath;
        path.NumSphere = 1;
        path.GlobalCurrCenter[0] = new Vector3(20, 20, 20);
        var checkPos = new PhysSphere(new Vector3(10, 10, 10), 0);

        // Without PerfectClip the transition is refused, with the normal recorded.
        Assert.Equal(TransitionState.Collided, sphere.CollideWithPoint(transition, checkPos, 10.0f, 0));
        AssertVector(Vector3.Normalize(new Vector3(1, 1, 1)), transition.CollisionInfo.CollisionNormal);

        transition.ObjectInfo.State |= ObjectInfoState.PerfectClip;

        // Moving towards the sphere but stopping short of contact (t > 1).
        Assert.Equal(TransitionState.Collided, sphere.CollideWithPoint(transition, checkPos, 10.0f, 0));

        // Moving away from it (t < 0).
        checkPos.Center = new Vector3(30, 30, 30);
        Assert.Equal(TransitionState.Collided, sphere.CollideWithPoint(transition, checkPos, 10.0f, 0));

        // Starting inside the radius sum.
        path.GlobalCurrCenter[0] = new Vector3(1, 1, 1);
        Assert.Equal(TransitionState.Collided, sphere.CollideWithPoint(transition, checkPos, 10.0f, 0));
    }

    [Fact]
    public void Retail_collide_with_point_clips_the_check_position_to_the_moment_of_contact()
    {
        var sphere = new PhysSphere(Vector3.Zero, 5.0f);
        var transition = new Transition();
        transition.ObjectInfo.State = ObjectInfoState.PerfectClip;
        var path = transition.SpherePath;
        path.NumSphere = 1;
        path.GlobalCurrCenter[0] = new Vector3(20, 20, 20);
        path.GlobalSphere[0] = new PhysSphere(new Vector3(5, 5, 5), 0.5f);
        path.CheckPos = path.CheckPos.AddOffset(new Vector3(5, 5, 5));

        Assert.Equal(TransitionState.Adjusted, sphere.CollideWithPoint(transition, path.GlobalSphere[0], 10.0f, 0));

        // The centre is pulled back to where it is radiusSum + epsilon from the sphere (ACE adds the old displacement
        // instead, which throws the sphere 20 m away).
        var contact = path.GlobalSphere[0].Center;
        Assert.Equal(10.0002, contact.Length(), 1e-4);
        AssertVector(Vector3.Normalize(new Vector3(1, 1, 1)) * contact.Length(), contact, 1e-4f);
        AssertVector(contact, path.CheckPos.Frame.Origin, 1e-5f);
        AssertVector(Vector3.Normalize(new Vector3(1, 1, 1)), transition.CollisionInfo.CollisionNormal, 1e-5f);
        Assert.False(path.CellArrayValid);
    }

    [Fact]
    public void Sphere_IntersectsSphere()
    {
        var sphere = new PhysSphere(Vector3.Zero, 5.0f);
        var sphereCollide = new PhysSphere(new Vector3(1, 1, 1), 5.0f);
        var sphereNonCollide = new PhysSphere(new Vector3(10, 10, 10), 5.0f);

        var transition = new Transition();
        var path = transition.SpherePath;
        path.NumSphere = 1;
        path.InsertType = InsertType.Placement;
        path.GlobalSphere[0] = sphereNonCollide;

        IntersectsSphereInner(sphere, transition, sphereCollide, sphereNonCollide);

        // Walkable paths.
        path.CheckWalkable = true;
        path.InsertType = InsertType.Transition;
        IntersectsSphereInner(sphere, transition, sphereCollide, sphereNonCollide);

        // Clipped paths (still under the walkable check).
        transition.ObjectInfo.State = ObjectInfoState.PathClipped;
        IntersectsSphereInner(sphere, transition, sphereCollide, sphereNonCollide, firstTest: true, secondTest: false);

        // Step up: a free move with a clipped path collides with the first sphere only.
        path.StepUp = true;
        path.CheckWalkable = false;
        IntersectsSphereInner(sphere, transition, sphereCollide, sphereNonCollide, firstTest: true, secondTest: false);

        // A clipped free move ignores the second sphere.
        path.StepUp = false;
        IntersectsSphereInner(sphere, transition, sphereCollide, sphereNonCollide, firstTest: false, secondTest: true, interp: true);
    }

    [Fact]
    public void Sphere_LandOnSphere()
    {
        var sphere = new PhysSphere(Vector3.Zero, 5.0f);
        var transition = new Transition();
        var path = transition.SpherePath;

        path.GlobalCurrCenter[0] = new Vector3(0, 0, -1);
        Assert.Equal(TransitionState.Adjusted, sphere.LandOnSphere(transition));
        Assert.True(path.Collide);
        AssertVector(new Vector3(0, 0, -1), path.StepUpNormal);
        Assert.Equal(PhysicsGlobals.LandingZ, path.WalkableAllowance);
        Assert.Equal(1.0f, path.WalkInterp);

        path.GlobalCurrCenter[0] = new Vector3(0, 0, 0.0001f);
        Assert.Equal(TransitionState.Collided, sphere.LandOnSphere(transition));
        Assert.True(path.Collide);
    }

    [Fact]
    public void Sphere_StepSphereDown()
    {
        var sphere = new PhysSphere(Vector3.Zero, 5.0f);
        var transition = new Transition();
        transition.SpherePath.StepDownAmt = -10.0f;
        transition.SpherePath.WalkInterp = 10.0f;

        var disp = new Vector3(0, 0, -10);
        Assert.Equal(TransitionState.Collided, sphere.StepSphereDown(transition, new PhysSphere(), ref disp, sphere.Radius * 2.0f));
    }

    [Fact]
    public void Sphere_SlideSphere()
    {
        var sphere = new PhysSphere(Vector3.Zero, 5.0f);
        var collisionNormal = new Vector3(0, 0, -1);
        var transition = new Transition();

        Assert.Equal(TransitionState.Slid, sphere.SlideSphere(transition, ref collisionNormal, Vector3.Zero));

        transition.CollisionInfo.LastKnownContactPlaneValid = true;
        transition.CollisionInfo.LastKnownContactPlane = new Plane(new Vector3(0, 0, 1), 0);

        // The normal points into the contact plane and nothing can slide: retail refuses the transition
        // (CSphere::slide_sphere, 11.4186 0x00537440, returns 2); ACE returns OK (Sphere.cs:603).
        Assert.Equal(TransitionState.Collided, sphere.SlideSphere(transition, ref collisionNormal, Vector3.Zero));
    }

    private static void IntersectsSphereInner(PhysSphere sphere, Transition transition, PhysSphere sphereCollide, PhysSphere sphereNonCollide, bool firstTest = true, bool secondTest = true, bool interp = false)
    {
        var path = transition.SpherePath;
        if (firstTest)
        {
            path.NumSphere = 1;
            path.GlobalSphere[0] = sphereNonCollide;
            SetCenter(path);
            Assert.Equal(TransitionState.OK, sphere.IntersectsSphere(transition, isCreature: false));

            path.GlobalSphere[0] = sphereCollide;
            SetCenter(path);
            Assert.Equal(interp ? TransitionState.OK : TransitionState.Collided, sphere.IntersectsSphere(transition, isCreature: false));
        }

        if (secondTest)
        {
            path.GlobalSphere[0] = sphereNonCollide;
            path.GlobalSphere[1] = sphereCollide;
            path.NumSphere = 2;
            SetCenter(path);
            Assert.Equal(interp ? TransitionState.OK : TransitionState.Collided, sphere.IntersectsSphere(transition, isCreature: false));
        }
    }

    /// <summary>ACE's test maps the current centres onto the global spheres.</summary>
    private static void SetCenter(SpherePath path)
    {
        for (var i = 0; i < SpherePath.MaxSpheres; i++)
        {
            path.GlobalCurrCenter[i] = path.GlobalSphere[i].Center;
        }
    }

    private static void AssertVector(Vector3 expected, Vector3 actual, float tolerance = 1e-6f)
    {
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");
    }
}

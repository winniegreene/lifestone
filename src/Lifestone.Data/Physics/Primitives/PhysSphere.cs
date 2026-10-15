// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Sphere.cs (AGPL-3.0)
// with changes: a mutable struct with retail's semantics, checked function by function against the 11.4186 decompile;
// the combat Attack test is left out; the transition state comes from Lifestone.Data.Physics.Transitions. Retail fixes
// (docs/third-party/physics.md, "ACE deviations"): find_time_of_collision returns retail's root (ACE negates it);
// collide_with_point moves the check position by the clipped offset and scales the normal by the epsilon-widened
// radius sum (ACE adds the displacement and uses the plain sum); the last branch of slide_sphere returns Collided
// (ACE returns OK).

using System.Numerics;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics.Primitives;

/// <summary>
/// A collision sphere (retail <c>CSphere</c>: <c>center</c>, <c>radius</c>) and its part of the transition: how a moving
/// sphere is stopped, stepped, slid or landed by this one.
/// </summary>
/// <remarks>
/// <para>A mutable struct, as retail passes <c>CSphere*</c> and writes centres in place; callers hand array elements of
/// the transition's sphere path by reference.</para>
/// <para><b>Arithmetic.</b> Single precision through <see cref="System.Numerics"/>, as ACE; retail's x87 code keeps
/// intermediates in double precision, so results can differ in the last float bits (docs/physics.md §4). Times of
/// impact are doubles, as retail returns them.</para>
/// <para>Retail functions (11.4186 PORTAL/cgeometry/CSphere.c; re-expressed, nothing copied) are cited on each member
/// with their 11.4186 and 11.6096 addresses.</para>
/// </remarks>
public struct PhysSphere : IEquatable<PhysSphere>
{
    /// <summary>The centre.</summary>
    public Vector3 Center;

    /// <summary>The radius.</summary>
    public float Radius;

    /// <summary>A sphere with the given centre and radius.</summary>
    public PhysSphere(Vector3 center, float radius)
    {
        Center = center;
        Radius = radius;
    }

    /// <summary>
    /// Retail <c>CSphere::intersects</c> (11.4186 0x005344A0, 11.6096 0x005351E0; not in the per-module decompile, so
    /// ACE's strict reading): true when the centres are closer than the sum of the radii. Touching spheres do not
    /// intersect.
    /// </summary>
    public readonly bool Intersects(in PhysSphere other)
    {
        var delta = other.Center - Center;
        var radiusSum = Radius + other.Radius;
        return delta.LengthSquared() < radiusSum * radiusSum;
    }

    /// <summary>
    /// Retail <c>CSphere::collides_with_sphere</c> (11.4186 0x005369E0, 11.6096 0x00537720): true when the displacement
    /// between two centres is no longer than <paramref name="radiusSum"/> (touching counts).
    /// </summary>
    public static bool CollidesWithSphere(Vector3 displacement, float radiusSum) =>
        displacement.LengthSquared() <= radiusSum * radiusSum;

    /// <summary>
    /// Retail <c>CSphere::find_time_of_collision</c> (11.4186 0x005368F0, 11.6096 0x00537630): the fraction of
    /// <paramref name="movement"/> at which a sphere displaced by <paramref name="displacement"/> from another first
    /// touches it (the centre distance equals <paramref name="radiusSum"/>), or -1 when they already overlap (within
    /// <see cref="PhysicsGlobals.Epsilon"/>), the movement is shorter than the epsilon, or the path misses.
    /// </summary>
    /// <remarks>
    /// It solves <c>|displacement + t * movement| = radiusSum</c> and returns the smaller root when that root is not
    /// negative, otherwise the larger one, so a sphere moving away gets a negative time. ACE returns the negation of both
    /// roots (Sphere.cs:232-249).
    /// </remarks>
    public static double FindTimeOfCollision(Vector3 movement, Vector3 displacement, float radiusSum)
    {
        double lengthSquared = movement.LengthSquared();
        var similar = -(double)Vector3.Dot(displacement, movement);
        var nonCollide = (double)displacement.LengthSquared() - ((double)radiusSum * radiusSum);
        if (nonCollide < PhysicsGlobals.Epsilon || lengthSquared < PhysicsGlobals.Epsilon)
        {
            return -1.0;
        }

        var discriminant = (similar * similar) - (nonCollide * lengthSquared);
        if (discriminant < 0.0)
        {
            return -1.0;
        }

        var root = Math.Sqrt(discriminant);
        return similar - root < 0.0 ? (root + similar) / lengthSquared : (similar - root) / lengthSquared;
    }

    /// <summary>
    /// Retail <c>CSphere::sphere_intersects_ray</c> (11.4186 0x005377A0, 11.6096 0x005384E0): whether
    /// <paramref name="ray"/> meets the sphere, with the ray parameter of the hit. A ray that starts inside or on the
    /// sphere does not intersect it; a sphere behind the start can still return true, with a negative time.
    /// </summary>
    public readonly bool SphereIntersectsRay(in Ray ray, out double time)
    {
        time = 0.0;
        var delta = ray.Point - Center;
        double directionSquared = ray.Direction.LengthSquared();
        var b = -(double)Vector3.Dot(delta, ray.Direction);
        var c = (double)delta.LengthSquared() - ((double)Radius * Radius);
        if (c <= 0.0 || directionSquared < PhysicsGlobals.Epsilon)
        {
            return false;
        }

        var discriminant = (b * b) - (c * directionSquared);
        if (discriminant < 0.0)
        {
            return false;
        }

        var root = Math.Sqrt(discriminant);
        time = b <= root ? (root + b) / directionSquared : (b - root) / directionSquared;
        return true;
    }

    /// <summary>
    /// Retail <c>CSphere::collide_with_point</c> (11.4186 0x00537230, 11.6096 0x00537F70): the moving sphere
    /// <paramref name="sphereNum"/> hit this one. Without PerfectClip the transition only records the normal and is
    /// refused; with it, the check position is pulled back along the path to the moment of contact.
    /// </summary>
    /// <param name="transition">The transition being tested.</param>
    /// <param name="checkPos">The moving sphere at its check position (an element of the path's global spheres).</param>
    /// <param name="radiusSum">The two radii's sum, less the epsilon.</param>
    /// <param name="sphereNum">Which of the moving object's spheres (0 or 1).</param>
    internal readonly TransitionState CollideWithPoint(Transition transition, in PhysSphere checkPos, float radiusSum, int sphereNum)
    {
        var path = transition.SpherePath;
        var collisions = transition.CollisionInfo;
        var currCenter = path.GlobalCurrCenter[sphereNum];
        if ((transition.ObjectInfo.State & ObjectInfoState.PerfectClip) == 0)
        {
            var normal = currCenter - Center;
            if (!VectorMath.NormalizeCheckSmall(ref normal))
            {
                collisions.SetCollisionNormal(normal);
            }

            return TransitionState.Collided;
        }

        var widened = radiusSum + PhysicsGlobals.Epsilon;
        var oldDisplacement = currCenter - Center;
        var movement = LandDefs.GetBlockOffset(path.CurPos.CellId, path.CheckPos.CellId) + (checkPos.Center - currCenter);
        var time = FindTimeOfCollision(movement, oldDisplacement, widened);
        if (time < PhysicsGlobals.Epsilon || time > 1.0)
        {
            return TransitionState.Collided;
        }

        var offset = (movement * (float)time) - movement;
        var collisionNormal = (offset + checkPos.Center - Center) * (1.0f / widened);
        collisions.SetCollisionNormal(collisionNormal);
        path.AddOffsetToCheckPos(offset, checkPos.Radius);
        return TransitionState.Adjusted;
    }

    /// <summary>
    /// Retail <c>CSphere::slide_sphere(object, path, collisions, disp, radsum, sphere_num)</c> (11.4186 0x00536F40,
    /// 11.6096 0x00537C80): slides the path's sphere <paramref name="sphereNum"/> along this one, across the contact
    /// plane (or the last known one).
    /// </summary>
    /// <param name="transition">The transition being tested.</param>
    /// <param name="displacement">The moving sphere's displacement from this one.</param>
    /// <param name="sphereNum">Which of the moving object's spheres (0 or 1).</param>
    internal readonly TransitionState SlideSphere(Transition transition, Vector3 displacement, int sphereNum)
    {
        var path = transition.SpherePath;
        var collisions = transition.CollisionInfo;
        var globalSphere = path.GlobalSphere[sphereNum];
        var currCenter = path.GlobalCurrCenter[sphereNum];
        var collisionNormal = currCenter - Center;
        if (VectorMath.NormalizeCheckSmall(ref collisionNormal))
        {
            return TransitionState.Collided;
        }

        collisions.SetCollisionNormal(collisionNormal);
        var skidDirection = (collisions.ContactPlaneValid ? collisions.ContactPlane : collisions.LastKnownContactPlane).Normal;
        var direction = Vector3.Cross(collisionNormal, skidDirection);
        var globalOffset = LandDefs.GetBlockOffset(path.CurPos.CellId, path.CheckPos.CellId) + (globalSphere.Center - currCenter);
        var directionSquared = direction.LengthSquared();
        if (directionSquared >= PhysicsGlobals.Epsilon)
        {
            // The along-the-edge part of the move. The decompile loses the comparison that refuses a near-zero slide;
            // the five-argument slide_sphere (0x00537440) makes the same test explicitly on the slide's squared length.
            var slide = direction * Vector3.Dot(globalOffset, direction) * (1.0f / directionSquared);
            if (slide.LengthSquared() < PhysicsGlobals.Epsilon)
            {
                return TransitionState.Collided;
            }

            path.AddOffsetToCheckPos(slide - globalOffset, globalSphere.Radius);
            return TransitionState.Slid;
        }

        if (Vector3.Dot(skidDirection, displacement) < 0.0f)
        {
            return TransitionState.Collided;
        }

        path.AddOffsetToCheckPos(-collisionNormal * Vector3.Dot(globalOffset, collisionNormal), globalSphere.Radius);
        return TransitionState.Slid;
    }

    /// <summary>
    /// Retail <c>CSphere::slide_sphere(path, collisions, collision_normal, curr_pos)</c> (11.4186 0x00537440, 11.6096
    /// 0x00538180), called on the moving sphere: slides it from <paramref name="currPos"/> along the edge between the
    /// obstacle (normal <paramref name="collisionNormal"/>) and the contact plane. A zero normal moves it halfway back.
    /// When nothing can slide, the normal is replaced by the reversed displacement and the transition is refused.
    /// </summary>
    internal readonly TransitionState SlideSphere(Transition transition, ref Vector3 collisionNormal, Vector3 currPos)
    {
        var path = transition.SpherePath;
        var collisions = transition.CollisionInfo;
        if (collisionNormal.X == 0.0f && collisionNormal.Y == 0.0f && collisionNormal.Z == 0.0f)
        {
            path.AddOffsetToCheckPos((currPos - Center) * 0.5f, Radius);
            return TransitionState.Adjusted;
        }

        collisions.SetCollisionNormal(collisionNormal);
        var delta = LandDefs.GetBlockOffset(path.CurPos.CellId, path.CheckPos.CellId) + (Center - currPos);
        var contactNormal = (collisions.ContactPlaneValid ? collisions.ContactPlane : collisions.LastKnownContactPlane).Normal;
        var direction = Vector3.Cross(collisionNormal, contactNormal);
        var directionSquared = direction.LengthSquared();
        if (directionSquared >= PhysicsGlobals.Epsilon)
        {
            var offset = direction * Vector3.Dot(direction, delta) * (1.0f / directionSquared);
            if (offset.LengthSquared() < PhysicsGlobals.Epsilon)
            {
                return TransitionState.Collided;
            }

            path.AddOffsetToCheckPos(offset - delta, Radius);
            return TransitionState.Slid;
        }

        if (Vector3.Dot(collisionNormal, contactNormal) >= 0.0f)
        {
            path.AddOffsetToCheckPos(-collisionNormal * Vector3.Dot(delta, collisionNormal), Radius);
            return TransitionState.Slid;
        }

        collisionNormal = -delta;
        if (!VectorMath.NormalizeCheckSmall(ref collisionNormal))
        {
            collisions.SetCollisionNormal(collisionNormal);
        }

        return TransitionState.Collided;
    }

    /// <summary>
    /// Retail <c>CSphere::slide_sphere(object, path, collisions, check_pos, disp, radsum, sphere_number)</c> (11.4186
    /// 0x00537A10, 11.6096 0x00538750): the normal from this sphere to the moving sphere's current centre, then
    /// <paramref name="checkPos"/>'s own slide (<see cref="SlideSphere(Transition, ref Vector3, Vector3)"/>).
    /// </summary>
    internal readonly TransitionState SlideSphere(Transition transition, in PhysSphere checkPos, int sphereNum)
    {
        var currCenter = transition.SpherePath.GlobalCurrCenter[sphereNum];
        var collisionNormal = currCenter - Center;
        return VectorMath.NormalizeCheckSmall(ref collisionNormal)
            ? TransitionState.Collided
            : checkPos.SlideSphere(transition, ref collisionNormal, currCenter);
    }

    /// <summary>
    /// Retail <c>CSphere::step_sphere_down</c> (11.4186 0x00536D20, 11.6096 0x00537A60): while stepping down, the moving
    /// sphere lands on this one: the check position is lowered to rest on it and the contact plane is the tangent plane
    /// there, unless that plane is too steep (not above the walkable allowance).
    /// </summary>
    /// <param name="transition">The transition being tested.</param>
    /// <param name="checkPos">The moving sphere at its check position.</param>
    /// <param name="displacement">The moving sphere's displacement from this one; scaled to the contact normal on return.</param>
    /// <param name="radiusSum">The two radii's sum, less the epsilon.</param>
    internal readonly TransitionState StepSphereDown(Transition transition, in PhysSphere checkPos, ref Vector3 displacement, float radiusSum)
    {
        var path = transition.SpherePath;
        var collisions = transition.CollisionInfo;
        var horizontalSquared = (displacement.X * displacement.X) + (displacement.Y * displacement.Y);
        if (radiusSum * radiusSum < (displacement.Z * displacement.Z) + horizontalSquared)
        {
            if (path.NumSphere <= 1 || !CollidesWithSphere(path.GlobalSphere[1].Center - Center, radiusSum))
            {
                return TransitionState.OK;
            }
        }

        var stepDown = path.StepDownAmt * path.WalkInterp;
        if (MathF.Abs(stepDown) < PhysicsGlobals.Epsilon)
        {
            return TransitionState.Collided;
        }

        var widened = radiusSum + PhysicsGlobals.Epsilon;
        var scaledStep = (float)((Math.Sqrt(((double)widened * widened) - horizontalSquared) - displacement.Z) / stepDown);
        var timeCheck = (1.0f - scaledStep) * path.WalkInterp;
        if (timeCheck >= path.WalkInterp || timeCheck < -0.1f)
        {
            return TransitionState.Collided;
        }

        var interp = stepDown * scaledStep;
        displacement = new Vector3(displacement.X, displacement.Y, displacement.Z + interp) * (1.0f / widened);
        if (displacement.Z <= path.WalkableAllowance)
        {
            return TransitionState.OK;
        }

        collisions.SetContactPlane(PlaneMath.Through(displacement, Center + (displacement * Radius)), isWater: true);
        collisions.ContactPlaneCellId = path.CheckPos.CellId;
        path.WalkInterp = timeCheck;
        path.AddOffsetToCheckPos(new Vector3(0f, 0f, interp), checkPos.Radius);
        return TransitionState.Adjusted;
    }

    /// <summary>
    /// Retail <c>CSphere::step_sphere_up</c> (11.4186 0x00537900, 11.6096 0x00538640): an object on the ground walked
    /// into this sphere; it steps up onto it when the rise is within its step-up height, and slides otherwise.
    /// </summary>
    internal readonly TransitionState StepSphereUp(Transition transition, Vector3 displacement, float radiusSum)
    {
        var widened = radiusSum + PhysicsGlobals.Epsilon;
        if (transition.ObjectInfo.StepUpHeight < widened - displacement.Z)
        {
            return SlideSphere(transition, displacement, 0);
        }

        var collisionNormal = transition.SpherePath.GlobalCurrCenter[0] - Center;
        return transition.StepUp(collisionNormal) ? TransitionState.OK : transition.SpherePath.StepUpSlide(transition);
    }

    /// <summary>
    /// Retail <c>CSphere::land_on_sphere</c> (11.4186 0x005379A0, 11.6096 0x005386E0): a falling object hit this
    /// sphere: the path records the collision and the landing allowance (<see cref="PhysicsGlobals.LandingZ"/>).
    /// </summary>
    internal readonly TransitionState LandOnSphere(Transition transition)
    {
        var path = transition.SpherePath;
        var collisionNormal = path.GlobalCurrCenter[0] - Center;
        if (VectorMath.NormalizeCheckSmall(ref collisionNormal))
        {
            return TransitionState.Collided;
        }

        path.SetCollide(collisionNormal);
        path.WalkableAllowance = PhysicsGlobals.LandingZ;
        return TransitionState.Adjusted;
    }

    /// <summary>
    /// Retail <c>CSphere::intersects_sphere(transition, is_creature)</c> (11.4186 0x00537A80, 11.6096 0x005387C0): this
    /// sphere (in the check position's landblock space) against the moving object's spheres, by the path's mode:
    /// placement, stepping down, the walkable check, a free move (step up, slide, clip or land) or a collide pass
    /// (rest on the sphere when the contact is walkable).
    /// </summary>
    /// <param name="transition">The transition being tested.</param>
    /// <param name="isCreature">True when this sphere belongs to a creature: creatures are neither stepped down onto
    /// nor rested on.</param>
    internal readonly TransitionState IntersectsSphere(Transition transition, bool isCreature)
    {
        var path = transition.SpherePath;
        ref readonly var globalSphere = ref path.GlobalSphere[0];
        var displacement = globalSphere.Center - Center;
        var radiusSum = Radius + globalSphere.Radius - PhysicsGlobals.Epsilon;

        if (path.ObstructionEthereal || path.InsertType == InsertType.Placement)
        {
            if (CollidesWithSphere(displacement, radiusSum))
            {
                return TransitionState.Collided;
            }

            return path.NumSphere > 1 && CollidesWithSphere(path.GlobalSphere[1].Center - Center, radiusSum)
                ? TransitionState.Collided
                : TransitionState.OK;
        }

        if (path.StepDown)
        {
            return isCreature ? TransitionState.OK : StepSphereDown(transition, globalSphere, ref displacement, radiusSum);
        }

        if (path.CheckWalkable)
        {
            if (CollidesWithSphere(displacement, radiusSum))
            {
                return TransitionState.Collided;
            }

            return path.NumSphere > 1 && CollidesWithSphere(path.GlobalSphere[1].Center - Center, radiusSum)
                ? TransitionState.Collided
                : TransitionState.OK;
        }

        var state = transition.ObjectInfo.State;
        if (!path.Collide)
        {
            if ((state & (ObjectInfoState.Contact | ObjectInfoState.OnWalkable)) != 0)
            {
                if (CollidesWithSphere(displacement, radiusSum))
                {
                    return StepSphereUp(transition, displacement, radiusSum);
                }

                if (path.NumSphere > 1 && CollidesWithSphere(path.GlobalSphere[1].Center - Center, radiusSum))
                {
                    return SlideSphere(transition, path.GlobalSphere[1], 1);
                }
            }
            else if ((state & ObjectInfoState.PathClipped) != 0)
            {
                if (CollidesWithSphere(displacement, radiusSum))
                {
                    return CollideWithPoint(transition, globalSphere, radiusSum, 0);
                }
            }
            else
            {
                if (CollidesWithSphere(displacement, radiusSum))
                {
                    return LandOnSphere(transition);
                }

                if (path.NumSphere > 1 && CollidesWithSphere(path.GlobalSphere[1].Center - Center, radiusSum))
                {
                    return CollideWithPoint(transition, path.GlobalSphere[1], radiusSum, 1);
                }
            }

            return TransitionState.OK;
        }

        if (isCreature)
        {
            return TransitionState.OK;
        }

        if (!CollidesWithSphere(displacement, radiusSum)
            && !(path.NumSphere > 1 && CollidesWithSphere(path.GlobalSphere[1].Center - Center, radiusSum)))
        {
            return TransitionState.OK;
        }

        // Rest on the sphere: the moment along the step's movement where the moving sphere touches this one.
        var movement = path.GlobalCurrCenter[0] - globalSphere.Center - path.GetCurrPosCheckPosBlockOffset();
        var widened = radiusSum + PhysicsGlobals.Epsilon;
        double lengthSquared = movement.LengthSquared();
        var similar = -(double)Vector3.Dot(movement, displacement);
        if (Math.Abs(lengthSquared) < PhysicsGlobals.Epsilon)
        {
            return TransitionState.Collided;
        }

        var root = Math.Sqrt((similar * similar) - ((displacement.LengthSquared() - ((double)widened * widened)) * lengthSquared)) + similar;
        if (root > 1.0)
        {
            root = (similar * 2.0) - root;
        }

        var time = (float)(root / lengthSquared);
        var timeCheck = (1.0f - time) * path.WalkInterp;
        if (!(timeCheck < path.WalkInterp && timeCheck >= -0.1f))
        {
            // Written as retail tests it, so a NaN time (a negative discriminant) is refused.
            return TransitionState.Collided;
        }

        movement *= time;
        var contactNormal = (displacement + movement) / widened;
        if (!path.IsWalkableAllowable(contactNormal.Z))
        {
            return TransitionState.OK;
        }

        var collisions = transition.CollisionInfo;
        collisions.SetContactPlane(PlaneMath.Through(contactNormal, globalSphere.Center - (contactNormal * globalSphere.Radius)), isWater: true);
        collisions.ContactPlaneCellId = path.CheckPos.CellId;
        path.WalkInterp = timeCheck;
        path.AddOffsetToCheckPos(movement, globalSphere.Radius);
        return TransitionState.Adjusted;
    }

    /// <summary>
    /// Retail <c>CSphere::intersects_sphere(p, scale, transition, is_creature)</c> (11.4186 0x00537FD0, 11.6096
    /// 0x00538D10): this sphere, local to an object at <paramref name="position"/> with <paramref name="scale"/>, moved
    /// into the check position's landblock space and tested with <see cref="IntersectsSphere(Transition, bool)"/>.
    /// </summary>
    internal readonly TransitionState IntersectsSphere(in CellFrame position, float scale, Transition transition, bool isCreature)
    {
        var global = new PhysSphere(transition.SpherePath.CheckPos.LocalToGlobal(position, Center * scale), Radius * scale);
        return global.IntersectsSphere(transition, isCreature);
    }

    /// <inheritdoc/>
    public readonly bool Equals(PhysSphere other) => Center == other.Center && Radius == other.Radius;

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj) => obj is PhysSphere other && Equals(other);

    /// <inheritdoc/>
    public override readonly int GetHashCode() => HashCode.Combine(Center, Radius);

    /// <inheritdoc/>
    public override readonly string ToString() => $"Sphere {Center} r={Radius}";
}

// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/CylSphere.cs (AGPL-3.0)
// with changes: a struct with retail's semantics, checked function by function against the 11.4186 decompile; the
// transition state comes from Lifestone.Data.Physics.Transitions. Retail fixes (docs/third-party/physics.md, "ACE
// deviations"): collide_with_point's vertical time of impact uses the displacement's height (ACE uses the movement's)
// and the vertical clip adds the clipped movement (ACE subtracts it); step_sphere_down records the contact plane's cell;
// the second sphere slides with its own displacement in intersects_sphere (ACE passes the first sphere's).

using System.Numerics;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics.Primitives;

/// <summary>
/// A vertical cylinder with a flat top and bottom, the collision shape of creatures and many Setups (retail
/// <c>CCylSphere</c>: <c>low_pt</c>, <c>height</c>, <c>radius</c>), and its part of the transition.
/// </summary>
/// <remarks>
/// Retail functions (11.4186 PORTAL/cgeometry/CCylSphere.c; re-expressed, nothing copied) are cited on each member with
/// their 11.4186 and 11.6096 addresses. Arithmetic as <see cref="PhysSphere"/>.
/// </remarks>
public struct PhysCylSphere : IEquatable<PhysCylSphere>
{
    /// <summary>The centre of the bottom face.</summary>
    public Vector3 LowPoint;

    /// <summary>The height.</summary>
    public float Height;

    /// <summary>The radius.</summary>
    public float Radius;

    /// <summary>A cylinder with the given bottom centre, height and radius.</summary>
    public PhysCylSphere(Vector3 lowPoint, float height, float radius)
    {
        LowPoint = lowPoint;
        Height = height;
        Radius = radius;
    }

    /// <summary>
    /// Retail <c>CCylSphere::collides_with_sphere</c> (11.4186 0x0053A880, 11.6096 0x0053B5E0): a sphere
    /// (<paramref name="checkPos"/>, displaced by <paramref name="displacement"/> from the bottom centre) touches the
    /// cylinder when it is within <paramref name="radiusSum"/> horizontally and its vertical overlap reaches the body.
    /// </summary>
    public readonly bool CollidesWithSphere(in PhysSphere checkPos, Vector3 displacement, float radiusSum) =>
        radiusSum * radiusSum >= (displacement.X * displacement.X) + (displacement.Y * displacement.Y)
        && checkPos.Radius - PhysicsGlobals.Epsilon + (Height * 0.5f) >= MathF.Abs((Height * 0.5f) - displacement.Z);

    /// <summary>
    /// Retail <c>CCylSphere::normal_of_collision</c> (11.4186 0x0053AB50, 11.6096 0x0053B8B0): the collision normal for
    /// the path's sphere <paramref name="sphereNum"/> at its current centre: horizontal when it is outside the radius
    /// (and true when the hit is certain), otherwise straight up or down.
    /// </summary>
    internal readonly bool NormalOfCollision(SpherePath path, in PhysSphere checkPos, Vector3 displacement, float radiusSum, int sphereNum, out Vector3 normal)
    {
        var oldDisplacement = path.GlobalCurrCenter[sphereNum] - LowPoint;
        if (radiusSum * radiusSum < oldDisplacement.LengthSquared2D())
        {
            normal = new Vector3(oldDisplacement.X, oldDisplacement.Y, 0f);
            return checkPos.Radius - PhysicsGlobals.Epsilon + (Height * 0.5f) >= MathF.Abs((Height * 0.5f) - oldDisplacement.Z)
                || MathF.Abs(oldDisplacement.Z - displacement.Z) <= PhysicsGlobals.Epsilon;
        }

        normal = new Vector3(0f, 0f, displacement.Z - oldDisplacement.Z <= 0.0f ? 1.0f : -1.0f);
        return true;
    }

    /// <summary>
    /// Retail <c>CCylSphere::collide_with_point</c> (11.4186 0x0053ACB0, 11.6096 0x0053BA10): the moving sphere hit the
    /// cylinder. Without PerfectClip the normal is recorded and the transition refused; with it, the check position is
    /// pulled back to the moment of contact with the top, bottom or side.
    /// </summary>
    internal readonly TransitionState CollideWithPoint(Transition transition, in PhysSphere checkPos, Vector3 displacement, float radiusSum, int sphereNum)
    {
        var path = transition.SpherePath;
        var collisions = transition.CollisionInfo;
        var definite = NormalOfCollision(path, checkPos, displacement, radiusSum, sphereNum, out var collisionNormal);
        if (VectorMath.NormalizeCheckSmall(ref collisionNormal))
        {
            return TransitionState.Collided;
        }

        if ((transition.ObjectInfo.State & ObjectInfoState.PerfectClip) == 0)
        {
            collisions.SetCollisionNormal(collisionNormal);
            return TransitionState.Collided;
        }

        // Retail measures from the first sphere's current centre whichever sphere hit.
        var currCenter = path.GlobalCurrCenter[0];
        var movement = LandDefs.GetBlockOffset(path.CurPos.CellId, path.CheckPos.CellId) + (checkPos.Center - currCenter);
        var oldDisplacement = currCenter - LowPoint;
        var widened = radiusSum + PhysicsGlobals.Epsilon;
        double time;
        Vector3 scaled;

        if (!definite)
        {
            if (MathF.Abs(movement.Z) < PhysicsGlobals.Epsilon)
            {
                return TransitionState.Collided;
            }

            if (movement.Z > 0.0f)
            {
                collisionNormal = new Vector3(0f, 0f, -1.0f);
                time = -((oldDisplacement.Z + checkPos.Radius) / movement.Z);
            }
            else
            {
                collisionNormal = new Vector3(0f, 0f, 1.0f);
                time = (checkPos.Radius + Height - oldDisplacement.Z) / movement.Z;
            }

            scaled = movement * (float)time;
            if (((scaled.Y + oldDisplacement.Y) * (scaled.Y + oldDisplacement.Y)) + ((scaled.X + oldDisplacement.X) * (scaled.X + oldDisplacement.X))
                >= widened * widened)
            {
                // The top or bottom is missed: clip against the side instead.
                var horizontalSquared = movement.LengthSquared2D();
                if (MathF.Abs(horizontalSquared) < PhysicsGlobals.Epsilon)
                {
                    return TransitionState.Collided;
                }

                if (TrySideTime(movement, oldDisplacement, widened, horizontalSquared, strict: true, out var sideTime))
                {
                    time = sideTime;
                    scaled = movement * (float)sideTime;
                }

                collisionNormal = SideNormal(currCenter + scaled, widened);
            }

            if (!(time >= 0.0 && time <= 1.0))
            {
                return TransitionState.Collided;
            }

            return Clip(path, collisions, checkPos, currCenter + scaled, collisionNormal);
        }

        if (collisionNormal.Z != 0.0f)
        {
            if (MathF.Abs(movement.Z) < PhysicsGlobals.Epsilon)
            {
                return TransitionState.Collided;
            }

            time = movement.Z > 0.0f
                ? -((oldDisplacement.Z + checkPos.Radius) / movement.Z)
                : (checkPos.Radius + Height - oldDisplacement.Z) / movement.Z;
            if (!(time >= 0.0 && time <= 1.0))
            {
                return TransitionState.Collided;
            }

            return Clip(path, collisions, checkPos, currCenter + (movement * (float)time), collisionNormal);
        }

        if (!TrySideTime(movement, oldDisplacement, widened, movement.LengthSquared2D(), strict: false, out time)
            || !(time >= 0.0 && time <= 1.0))
        {
            return TransitionState.Collided;
        }

        scaled = movement * (float)time;
        return Clip(path, collisions, checkPos, currCenter + scaled, SideNormal(currCenter + scaled, widened));
    }

    /// <summary>
    /// Retail <c>CCylSphere::slide_sphere</c> (11.4186 0x0053B2A0, 11.6096 0x0053C000): the collision normal, then
    /// <paramref name="checkPos"/>'s own slide from its current centre.
    /// </summary>
    internal readonly TransitionState SlideSphere(Transition transition, in PhysSphere checkPos, Vector3 displacement, float radiusSum, int sphereNum)
    {
        var path = transition.SpherePath;
        NormalOfCollision(path, checkPos, displacement, radiusSum, sphereNum, out var collisionNormal);
        return VectorMath.NormalizeCheckSmall(ref collisionNormal)
            ? TransitionState.Collided
            : checkPos.SlideSphere(transition, ref collisionNormal, path.GlobalCurrCenter[sphereNum]);
    }

    /// <summary>
    /// Retail <c>CCylSphere::step_sphere_up</c> (11.4186 0x0053B310, 11.6096 0x0053C070): an object on the ground walked
    /// into the cylinder; it steps up onto the top when the rise is within its step-up height, and slides otherwise.
    /// </summary>
    internal readonly TransitionState StepSphereUp(Transition transition, in PhysSphere checkPos, Vector3 displacement, float radiusSum)
    {
        if (transition.ObjectInfo.StepUpHeight < checkPos.Radius + Height - displacement.Z)
        {
            return SlideSphere(transition, checkPos, displacement, radiusSum, 0);
        }

        var path = transition.SpherePath;
        NormalOfCollision(path, checkPos, displacement, radiusSum, 0, out var collisionNormal);
        if (VectorMath.NormalizeCheckSmall(ref collisionNormal))
        {
            return TransitionState.Collided;
        }

        return transition.StepUp(path.LocalSpacePos.Frame.LocalToGlobalVec(collisionNormal))
            ? TransitionState.OK
            : path.StepUpSlide(transition);
    }

    /// <summary>
    /// Retail <c>CCylSphere::land_on_cylinder</c> (11.4186 0x0053B3D0, 11.6096 0x0053C130): a falling object hit the
    /// cylinder: the path records the collision and the landing allowance.
    /// </summary>
    internal readonly TransitionState LandOnCylinder(Transition transition, in PhysSphere checkPos, Vector3 displacement, float radiusSum)
    {
        var path = transition.SpherePath;
        NormalOfCollision(path, checkPos, displacement, radiusSum, 0, out var collisionNormal);
        if (VectorMath.NormalizeCheckSmall(ref collisionNormal))
        {
            return TransitionState.Collided;
        }

        path.SetCollide(collisionNormal);
        path.WalkableAllowance = PhysicsGlobals.LandingZ;
        return TransitionState.Adjusted;
    }

    /// <summary>
    /// Retail <c>CCylSphere::step_sphere_down</c> (11.4186 0x0053A9B0, 11.6096 0x0053B710): while stepping down, the
    /// moving sphere lands on the cylinder's top: the check position is lowered onto it and the contact plane is the
    /// top's horizontal plane.
    /// </summary>
    internal readonly TransitionState StepSphereDown(Transition transition, in PhysSphere checkPos, Vector3 displacement, float radiusSum)
    {
        var path = transition.SpherePath;
        if (!CollidesWithSphere(checkPos, displacement, radiusSum)
            && !(path.NumSphere > 1 && CollidesWithSphere(path.GlobalSphere[1], path.GlobalSphere[1].Center - LowPoint, radiusSum)))
        {
            return TransitionState.OK;
        }

        var stepScale = path.StepDownAmt * path.WalkInterp;
        if (MathF.Abs(stepScale) < PhysicsGlobals.Epsilon)
        {
            return TransitionState.Collided;
        }

        var deltaZ = Height + checkPos.Radius - displacement.Z;
        var interp = (1.0f - (deltaZ / stepScale)) * path.WalkInterp;
        if (interp >= path.WalkInterp || interp < -0.1f)
        {
            return TransitionState.Collided;
        }

        var collisions = transition.CollisionInfo;
        var contactPoint = new Vector3(checkPos.Center.X, checkPos.Center.Y, checkPos.Center.Z + (deltaZ - checkPos.Radius));
        collisions.SetContactPlane(PlaneMath.Through(Vector3.UnitZ, contactPoint), isWater: true);
        collisions.ContactPlaneCellId = path.CheckPos.CellId;
        path.WalkInterp = interp;
        path.AddOffsetToCheckPos(new Vector3(0f, 0f, deltaZ), checkPos.Radius);
        return TransitionState.Adjusted;
    }

    /// <summary>
    /// Retail <c>CCylSphere::intersects_sphere(transition)</c> (11.4186 0x0053B440, 11.6096 0x0053C1A0): the cylinder
    /// (in the check position's landblock space) against the moving object's spheres, by the path's mode: placement,
    /// stepping down, the walkable check, a free move (step up, slide, clip or land) or a collide pass (rest on the top).
    /// </summary>
    internal readonly TransitionState IntersectsSphere(Transition transition)
    {
        var path = transition.SpherePath;
        ref readonly var globalSphere = ref path.GlobalSphere[0];
        var displacement = globalSphere.Center - LowPoint;
        var radiusSum = Radius - PhysicsGlobals.Epsilon + globalSphere.Radius;

        if (path.InsertType == InsertType.Placement || path.ObstructionEthereal)
        {
            if (CollidesWithSphere(globalSphere, displacement, radiusSum))
            {
                return TransitionState.Collided;
            }

            return path.NumSphere > 1 && CollidesWithSphere(path.GlobalSphere[1], path.GlobalSphere[1].Center - LowPoint, radiusSum)
                ? TransitionState.Collided
                : TransitionState.OK;
        }

        if (path.StepDown)
        {
            return StepSphereDown(transition, globalSphere, displacement, radiusSum);
        }

        if (path.CheckWalkable)
        {
            if (CollidesWithSphere(globalSphere, displacement, radiusSum))
            {
                return TransitionState.Collided;
            }

            return path.NumSphere > 1 && CollidesWithSphere(path.GlobalSphere[1], path.GlobalSphere[1].Center - LowPoint, radiusSum)
                ? TransitionState.Collided
                : TransitionState.OK;
        }

        var state = transition.ObjectInfo.State;
        if (!path.Collide)
        {
            if ((state & (ObjectInfoState.Contact | ObjectInfoState.OnWalkable)) != 0)
            {
                if (CollidesWithSphere(globalSphere, displacement, radiusSum))
                {
                    return StepSphereUp(transition, globalSphere, displacement, radiusSum);
                }

                if (path.NumSphere > 1)
                {
                    var secondDisplacement = path.GlobalSphere[1].Center - LowPoint;
                    if (CollidesWithSphere(path.GlobalSphere[1], secondDisplacement, radiusSum))
                    {
                        return SlideSphere(transition, path.GlobalSphere[1], secondDisplacement, radiusSum, 1);
                    }
                }
            }
            else if ((state & ObjectInfoState.PathClipped) != 0)
            {
                if (CollidesWithSphere(globalSphere, displacement, radiusSum))
                {
                    return CollideWithPoint(transition, globalSphere, displacement, radiusSum, 0);
                }
            }
            else
            {
                if (CollidesWithSphere(globalSphere, displacement, radiusSum))
                {
                    return LandOnCylinder(transition, globalSphere, displacement, radiusSum);
                }

                if (path.NumSphere > 1)
                {
                    var secondDisplacement = path.GlobalSphere[1].Center - LowPoint;
                    if (CollidesWithSphere(path.GlobalSphere[1], secondDisplacement, radiusSum))
                    {
                        return CollideWithPoint(transition, path.GlobalSphere[1], secondDisplacement, radiusSum, 1);
                    }
                }
            }

            return TransitionState.OK;
        }

        if (!CollidesWithSphere(globalSphere, displacement, radiusSum)
            && !(path.NumSphere > 1 && CollidesWithSphere(path.GlobalSphere[1], path.GlobalSphere[1].Center - LowPoint, radiusSum)))
        {
            return TransitionState.OK;
        }

        // Rest on the top: the moment along the step's movement where the moving sphere's bottom meets it.
        var movement = path.GlobalCurrCenter[0] - globalSphere.Center - path.GetCurrPosCheckPosBlockOffset();
        if (MathF.Abs(movement.Z) < PhysicsGlobals.Epsilon)
        {
            return TransitionState.Collided;
        }

        var timeCheck = (Height + globalSphere.Radius - displacement.Z) / movement.Z;
        var offset = movement * timeCheck;
        if (radiusSum * radiusSum < ((offset.Y + displacement.Y) * (offset.Y + displacement.Y)) + ((offset.X + displacement.X) * (offset.X + displacement.X)))
        {
            return TransitionState.OK;
        }

        var interp = (1.0f - timeCheck) * path.WalkInterp;
        if (!(interp < path.WalkInterp && interp >= -0.1f))
        {
            return TransitionState.Collided;
        }

        var contactPoint = globalSphere.Center + offset;
        contactPoint.Z -= globalSphere.Radius;
        var collisions = transition.CollisionInfo;
        collisions.SetContactPlane(PlaneMath.Through(Vector3.UnitZ, contactPoint), isWater: true);
        collisions.ContactPlaneCellId = path.CheckPos.CellId;
        path.WalkInterp = interp;
        path.AddOffsetToCheckPos(offset, globalSphere.Radius);
        return TransitionState.Adjusted;
    }

    /// <summary>
    /// Retail <c>CCylSphere::intersects_sphere(p, scale, transition)</c> (11.4186 0x0053B8F0, 11.6096 0x0053C650): caches
    /// the path's spheres in the object's space (<see cref="SpherePath.CacheLocalSpaceSphere"/>), then tests the
    /// cylinder, scaled and moved into the check position's landblock space, with <see cref="IntersectsSphere(Transition)"/>.
    /// </summary>
    internal readonly TransitionState IntersectsSphere(in CellFrame position, float scale, Transition transition)
    {
        var path = transition.SpherePath;
        path.CacheLocalSpaceSphere(position, 1.0f);
        var global = new PhysCylSphere(path.CheckPos.LocalToGlobal(position, LowPoint * scale), Height * scale, Radius * scale);
        return global.IntersectsSphere(transition);
    }

    /// <inheritdoc/>
    public readonly bool Equals(PhysCylSphere other) => LowPoint == other.LowPoint && Height == other.Height && Radius == other.Radius;

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj) => obj is PhysCylSphere other && Equals(other);

    /// <inheritdoc/>
    public override readonly int GetHashCode() => HashCode.Combine(LowPoint, Height, Radius);

    /// <inheritdoc/>
    public override readonly string ToString() => $"CylSphere {LowPoint} h={Height} r={Radius}";

    /// <summary>
    /// The time along <paramref name="movement"/> at which the moving centre, starting <paramref name="oldDisplacement"/>
    /// from the bottom centre, reaches <paramref name="widenedSum"/> from the axis in the horizontal plane: the smaller
    /// root when it is not negative, else the larger. False when the discriminant is negative or the horizontal movement
    /// is too short (at most the epsilon when <paramref name="strict"/>, below it otherwise, as retail's two copies test).
    /// </summary>
    private static bool TrySideTime(Vector3 movement, Vector3 oldDisplacement, float widenedSum, float horizontalSquared, bool strict, out double time)
    {
        time = 0.0;
        var similar = -(double)movement.Dot2D(oldDisplacement);
        var discriminant = (similar * similar) - (((double)oldDisplacement.LengthSquared2D() - ((double)widenedSum * widenedSum)) * horizontalSquared);
        var longEnough = strict ? horizontalSquared > PhysicsGlobals.Epsilon : horizontalSquared >= PhysicsGlobals.Epsilon;
        if (!(discriminant >= 0.0 && longEnough))
        {
            return false;
        }

        var root = Math.Sqrt(discriminant);
        time = similar - root < 0.0 ? (root + similar) / horizontalSquared : (similar - root) / horizontalSquared;
        return true;
    }

    /// <summary>The horizontal normal from the axis to <paramref name="contactCenter"/>, divided by <paramref name="widenedSum"/> as retail does.</summary>
    private readonly Vector3 SideNormal(Vector3 contactCenter, float widenedSum)
    {
        var normal = contactCenter - LowPoint;
        normal.Z = 0f;
        return normal / widenedSum;
    }

    /// <summary>Moves the check sphere's centre to <paramref name="contactCenter"/> and records the normal (retail's shared tail of collide_with_point).</summary>
    private static TransitionState Clip(SpherePath path, CollisionInfo collisions, in PhysSphere checkPos, Vector3 contactCenter, Vector3 collisionNormal)
    {
        var offset = contactCenter - checkPos.Center;
        collisions.SetCollisionNormal(collisionNormal);
        path.AddOffsetToCheckPos(offset, checkPos.Radius);
        return TransitionState.Adjusted;
    }
}

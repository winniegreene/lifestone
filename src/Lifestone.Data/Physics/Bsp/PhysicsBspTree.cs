// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/BSP/BSPTree.cs (the physics queries) (AGPL-3.0)
// with changes: checked function by function against the 11.4186 decompile (CBSPTree.c); spheres are structs on the
// stack and the trees are built from DatReaderWriter by PhysicsBspNode. Retail fixes (docs/third-party/physics.md):
// adjust_to_plane follows retail's search (contact times polygon by polygon, then bisection keeping the last free time;
// ACE inverts the loop's test and the bisection and fails where retail succeeds); placement_insert resets the hit
// polygon and the test radius after each push (ACE keeps both); a path-clipped object only tests its first sphere, as
// retail does.

using System.Numerics;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;
using DatPhysicsBspTree = DatReaderWriter.Types.PhysicsBSPTree;

namespace Lifestone.Data.Physics.Bsp;

/// <summary>
/// A physics BSP (retail <c>BSPTREE</c> with <c>pack_tree_type</c> 1): the tree a GfxObj or a cell struct collides
/// with, over its physics polygons, and the transition's queries against it.
/// </summary>
/// <remarks>
/// Immutable and shared. The queries work in the tree's own space: the transition caches its spheres there first
/// (<see cref="SpherePath.CacheLocalSpaceSphere"/>), and results are turned back into the check position's landblock
/// space with <see cref="SpherePath.LocalSpacePos"/> and the object's scale. Retail functions (11.4186
/// PORTAL/cgeometry/CBSPTree.c; re-expressed, nothing copied) are cited with their 11.4186 and 11.6096 addresses.
/// </remarks>
public sealed class PhysicsBspTree
{
    /// <summary>Retail <c>BSPTREE::adjust_to_plane</c>'s iteration limit (the literal 15).</summary>
    private const int AdjustIterations = 15;

    /// <summary>Retail <c>BSPTREE::placement_insert</c>'s attempt limit (20 passes).</summary>
    private const int PlacementAttempts = 20;

    private PhysicsBspTree(PhysicsBspNode root)
    {
        Root = root;
    }

    /// <summary>The root node (a leaf for the simplest shapes).</summary>
    public PhysicsBspNode Root { get; }

    /// <summary>
    /// The root's bounding sphere (retail <c>BSPTREE::GetSphere</c>, which <c>CGfxObj::Serialize</c> stores as the
    /// GfxObj's <c>physics_sphere</c>).
    /// </summary>
    public PhysSphere Sphere => Root.BoundingSphere;

    /// <summary>
    /// Builds the tree from DatReaderWriter's decode (retail <c>BSPTREE::UnPack</c>, 11.4186 0x00539830, 11.6096
    /// 0x0053A590, which unpacks the root as any child), resolving leaf polygons as positions in
    /// <paramref name="polygons"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">The tree has no root or is malformed (<see cref="PhysicsBspNode"/>).</exception>
    internal static PhysicsBspTree FromDat(DatPhysicsBspTree? tree, IReadOnlyList<PhysPolygon> polygons, string owner)
    {
        var root = tree?.Root ?? throw new InvalidDataException($"{owner}: the physics BSP has no root.");
        return new PhysicsBspTree(PhysicsBspNode.FromDat(root, polygons, owner));
    }

    /// <summary>A tree from a root built in code (synthetic shapes in tests).</summary>
    internal static PhysicsBspTree FromRoot(PhysicsBspNode root) => new(root ?? throw new ArgumentNullException(nameof(root)));

    /// <summary>
    /// Retail <c>BSPTREE::find_collisions</c> (11.4186 0x0053A440, 11.6096 0x0053B1A0): the transition's spheres (cached in
    /// this tree's space) against the tree, by the path's mode: placement and ethereal obstructions (any solid contact
    /// collides), the walkable check, a step down, a pending collision (land on a walkable polygon), an object in contact
    /// (step up, slide the second sphere, or remember a polygon touched from behind), a free object (collide and land) or
    /// a path-clipped one (clip to the contact).
    /// </summary>
    /// <param name="transition">The transition being tested.</param>
    /// <param name="scale">The owning object's scale (1 for a cell).</param>
    internal TransitionState FindCollisions(Transition transition, float scale)
    {
        var path = transition.SpherePath;
        var collisions = transition.CollisionInfo;
        ref readonly var localSphere = ref path.LocalSpaceSphere[0];
        var center = path.LocalSpaceCurrCenter[0];
        var movement = localSphere.Center - center;
        PhysPolygon? hitPoly = null;
        var contactPoint = Vector3.Zero;

        if (path.InsertType == InsertType.Placement || path.ObstructionEthereal)
        {
            var clearCell = !path.BuildingCheck || !path.HitsInteriorCell;
            return Root.SphereIntersectsSolid(localSphere, clearCell)
                || (path.NumSphere > 1 && Root.SphereIntersectsSolid(path.LocalSpaceSphere[1], clearCell))
                ? TransitionState.Collided
                : TransitionState.OK;
        }

        if (path.CheckWalkable)
        {
            return CheckWalkable(path, localSphere);
        }

        if (path.StepDown)
        {
            return StepSphereDown(path, collisions, localSphere, scale);
        }

        if (path.Collide)
        {
            var validPos = localSphere;
            var changed = false;
            Root.FindWalkable(path, ref validPos, ref hitPoly, movement, path.LocalSpaceZ, ref changed);
            if (!changed)
            {
                return TransitionState.OK;
            }

            path.AddOffsetToCheckPos(path.LocalSpacePos.Frame.LocalToGlobalVec(validPos.Center - localSphere.Center) * scale);
            var plane = PlaneMath.LocalToGlobal(hitPoly!.Plane, path.CheckPos, path.LocalSpacePos);
            collisions.SetContactPlane(new Plane(plane.Normal, plane.D * scale), isWater: false);
            collisions.ContactPlaneCellId = path.CheckPos.CellId;
            path.SetWalkable(validPos, hitPoly, path.LocalSpaceZ, path.LocalSpacePos, scale);
            return TransitionState.Adjusted;
        }

        var state = transition.ObjectInfo.State;
        if ((state & ObjectInfoState.Contact) != 0)
        {
            if (Root.SphereIntersectsPoly(localSphere, movement, ref hitPoly, ref contactPoint))
            {
                return StepSphereUp(transition, hitPoly!.Plane.Normal);
            }

            if (path.NumSphere > 1)
            {
                PhysPolygon? secondHit = null;
                if (Root.SphereIntersectsPoly(path.LocalSpaceSphere[1], movement, ref secondHit, ref contactPoint))
                {
                    return SlideSphere(transition, secondHit!.Plane.Normal);
                }

                if (secondHit is not null)
                {
                    path.SetNegPolyHit(false, path.LocalSpacePos.Frame.LocalToGlobalVec(secondHit.Plane.Normal));
                    return TransitionState.OK;
                }

                if (hitPoly is not null)
                {
                    path.SetNegPolyHit(true, path.LocalSpacePos.Frame.LocalToGlobalVec(hitPoly.Plane.Normal));
                    return TransitionState.OK;
                }
            }

            return TransitionState.OK;
        }

        if ((state & ObjectInfoState.PathClipped) == 0)
        {
            if (Root.SphereIntersectsPoly(localSphere, movement, ref hitPoly, ref contactPoint) || hitPoly is not null)
            {
                path.SetCollide(path.LocalSpacePos.Frame.LocalToGlobalVec(hitPoly!.Plane.Normal));
                path.WalkableAllowance = PhysicsGlobals.LandingZ;
                return TransitionState.Adjusted;
            }

            if (path.NumSphere > 1
                && (Root.SphereIntersectsPoly(path.LocalSpaceSphere[1], movement, ref hitPoly, ref contactPoint) || hitPoly is not null))
            {
                collisions.SetCollisionNormal(path.LocalSpacePos.Frame.LocalToGlobalVec(hitPoly!.Plane.Normal));
                return TransitionState.Collided;
            }

            return TransitionState.OK;
        }

        return Root.SphereIntersectsPoly(localSphere, movement, ref hitPoly, ref contactPoint) || hitPoly is not null
            ? CollideWithPt(transition, localSphere, center, hitPoly!, ref contactPoint, scale)
            : TransitionState.OK;
    }

    /// <summary>
    /// Retail <c>BSPTREE::placement_insert</c> (11.4186 0x00539960, 11.6096 0x0053A6C0): an initial placement in this tree.
    /// Each polygon the sphere (or the second one) touches pushes both out along its normal
    /// (<see cref="PhysPolygon.AdjustToPlacementPoly"/>); a centre inside a solid with no polygon touched doubles the test
    /// radius. When nothing is touched any more the check position moves by the push (not scaled, as in retail); after 20
    /// passes the placement is refused.
    /// </summary>
    internal TransitionState PlacementInsert(Transition transition)
    {
        var path = transition.SpherePath;
        var validPos = path.LocalSpaceSphere[0];
        var validPos2 = path.NumSphere > 1 ? path.LocalSpaceSphere[1] : default;
        var radius = validPos.Radius;
        var clearCell = !path.BuildingCheck || !path.HitsInteriorCell;
        PhysPolygon? hitPoly = null;
        for (var i = 0; ; i++)
        {
            var centerSolid = false;
            if (Root.SphereIntersectsSolidPoly(validPos, radius, ref centerSolid, ref hitPoly, clearCell))
            {
                if (hitPoly is null)
                {
                    radius += radius;
                }
                else
                {
                    hitPoly.AdjustToPlacementPoly(ref validPos, ref validPos2, radius, centerSolid, clearCell);
                    hitPoly = null;
                    radius = validPos.Radius;
                }
            }
            else
            {
                if (path.NumSphere <= 1 || !Root.SphereIntersectsSolidPoly(validPos2, radius, ref centerSolid, ref hitPoly, clearCell))
                {
                    if (i == 0)
                    {
                        return TransitionState.OK;
                    }

                    path.AddOffsetToCheckPos(path.LocalSpacePos.Frame.LocalToGlobalVec(validPos.Center - path.LocalSpaceSphere[0].Center));
                    return TransitionState.Adjusted;
                }

                if (hitPoly is null)
                {
                    radius += radius;
                }
                else
                {
                    hitPoly.AdjustToPlacementPoly(ref validPos2, ref validPos, radius, centerSolid, clearCell);
                    hitPoly = null;
                    radius = validPos.Radius;
                }
            }

            if (i >= PlacementAttempts - 1)
            {
                return TransitionState.Collided;
            }
        }
    }

    /// <summary>
    /// Retail <c>BSPTREE::adjust_to_plane</c> (11.4186 0x00539BF0, 11.6096 0x0053A950): moves <paramref name="checkPos"/>
    /// back along its path from <paramref name="currPos"/> to the last moment it touches no polygon. Polygon by polygon
    /// the contact time (<see cref="PhysPolygon.AdjustSphereToPoly"/>) is tried until one leaves the sphere free, then the
    /// interval between that free time and the last blocked one is halved until it is shorter than 0.02; the sphere ends
    /// at the free end. False after 15 blocked contact times.
    /// </summary>
    /// <remarks>
    /// The decompile keeps only the flags of the test that ends the contact-time loop early (the flags of an equality
    /// test). It is read as "the contact time is 1", ACE's constant: <see cref="PhysPolygon.AdjustSphereToPoly"/> returns
    /// exactly 1 when the sphere already touches the polygon's plane at the start, and the search then halves the whole
    /// path. ACE runs its loop body only when the time is 1 and gives up otherwise, which no consistent reading of the
    /// loop allows (docs/physics.md §4).
    /// </remarks>
    internal bool AdjustToPlane(ref PhysSphere checkPos, Vector3 currPos, PhysPolygon hitPoly, ref Vector3 contactPoint)
    {
        var movement = checkPos.Center - currPos;
        var lowerTime = 0.0;
        var upperTime = 1.0;
        var iteration = 0;
        PhysPolygon? polygon = hitPoly;
        while (true)
        {
            var touchTime = polygon!.AdjustSphereToPoly(checkPos, currPos, movement);
            if (touchTime == 1.0)
            {
                break;
            }

            checkPos.Center = currPos + (movement * (float)touchTime);
            if (!Root.SphereIntersectsPoly(checkPos, movement, ref polygon, ref contactPoint))
            {
                lowerTime = touchTime;
                break;
            }

            iteration++;
            upperTime = touchTime;
            if (iteration >= AdjustIterations)
            {
                return false;
            }
        }

        while (iteration < AdjustIterations)
        {
            var middle = (lowerTime + upperTime) * 0.5;
            checkPos.Center = currPos + (movement * (float)middle);
            if (Root.SphereIntersectsPoly(checkPos, movement, ref polygon, ref contactPoint))
            {
                upperTime = middle;
            }
            else
            {
                lowerTime = middle;
            }

            if (upperTime - lowerTime < 0.02)
            {
                break;
            }

            iteration++;
        }

        checkPos.Center = currPos + (movement * (float)lowerTime);
        return true;
    }

    /// <summary>
    /// Retail <c>BSPTREE::collide_with_pt</c> (11.4186 0x0053A090, 11.6096 0x0053ADF0): a path-clipped object hit
    /// <paramref name="hitPoly"/>. Without PerfectClip the normal is recorded and the step refused; with it the sphere is
    /// clipped back to the contact (<see cref="AdjustToPlane"/>) and the check position follows.
    /// </summary>
    internal TransitionState CollideWithPt(Transition transition, in PhysSphere checkPos, Vector3 currPos, PhysPolygon hitPoly, ref Vector3 contactPoint, float scale)
    {
        var path = transition.SpherePath;
        var collisions = transition.CollisionInfo;
        var collisionNormal = path.LocalSpacePos.Frame.LocalToGlobalVec(hitPoly.Plane.Normal);
        if ((transition.ObjectInfo.State & ObjectInfoState.PerfectClip) == 0)
        {
            collisions.SetCollisionNormal(collisionNormal);
            return TransitionState.Collided;
        }

        var validPos = checkPos;
        if (!AdjustToPlane(ref validPos, currPos, hitPoly, ref contactPoint))
        {
            return TransitionState.Collided;
        }

        collisions.SetCollisionNormal(collisionNormal);
        path.AddOffsetToCheckPos(path.LocalSpacePos.Frame.LocalToGlobalVec(validPos.Center - checkPos.Center) * scale);
        return TransitionState.Adjusted;
    }

    /// <summary>
    /// Retail <c>BSPTREE::step_sphere_down</c> (11.4186 0x0053A210, 11.6096 0x0053AF70): the step-down search. The sphere,
    /// lowered by the step, moves back up onto the walkable polygons it touches (<see cref="PhysicsBspNode.FindWalkable"/>
    /// along the step); the check position follows and the last polygon becomes the contact plane and the walkable.
    /// </summary>
    internal TransitionState StepSphereDown(SpherePath path, CollisionInfo collisions, in PhysSphere checkPos, float scale)
    {
        var stepDown = -(path.StepDownAmt * path.WalkInterp);
        var movement = path.LocalSpaceZ * stepDown * (1.0f / scale);
        var validPos = checkPos;
        var changed = false;
        PhysPolygon? polygon = null;
        Root.FindWalkable(path, ref validPos, ref polygon, movement, path.LocalSpaceZ, ref changed);
        if (!changed)
        {
            return TransitionState.OK;
        }

        path.AddOffsetToCheckPos(path.LocalSpacePos.Frame.LocalToGlobalVec(validPos.Center - checkPos.Center) * scale);
        var plane = PlaneMath.LocalToGlobal(polygon!.Plane, path.CheckPos, path.LocalSpacePos);
        collisions.SetContactPlane(new Plane(plane.Normal, plane.D * scale), isWater: false);
        collisions.ContactPlaneCellId = path.CheckPos.CellId;
        path.SetWalkable(validPos, polygon, path.LocalSpaceZ, path.LocalSpacePos, scale);
        return TransitionState.Adjusted;
    }

    /// <summary>
    /// Retail <c>BSPTREE::step_sphere_up</c> (11.4186 0x00539F20, 11.6096 0x0053AC80): an object in contact hit a polygon
    /// (normal in this tree's space); it steps up over it or slides along it.
    /// </summary>
    internal static TransitionState StepSphereUp(Transition transition, Vector3 collisionNormal)
    {
        var path = transition.SpherePath;
        return transition.StepUp(path.LocalSpacePos.Frame.LocalToGlobalVec(collisionNormal))
            ? TransitionState.OK
            : path.StepUpSlide(transition);
    }

    /// <summary>
    /// Retail <c>BSPTREE::slide_sphere</c> (11.4186 0x00539E50, 11.6096 0x0053ABB0): the first sphere slides along a polygon
    /// the second one hit (normal in this tree's space).
    /// </summary>
    internal static TransitionState SlideSphere(Transition transition, Vector3 collisionNormal)
    {
        var path = transition.SpherePath;
        var global = path.LocalSpacePos.Frame.LocalToGlobalVec(collisionNormal);
        return path.GlobalSphere[0].SlideSphere(transition, ref global, path.GlobalCurrCenter[0]);
    }

    /// <summary>
    /// Retail <c>BSPTREE::check_walkable</c> (11.4186 0x00539ED0, 11.6096 0x0053AC30): collided when a walkable polygon
    /// lies under the sphere (<see cref="PhysicsBspNode.HitsWalkable"/>), so the transition's walkable check knows there
    /// is ground.
    /// </summary>
    internal TransitionState CheckWalkable(SpherePath path, in PhysSphere checkPos) =>
        Root.HitsWalkable(path, checkPos, path.LocalSpaceZ) ? TransitionState.Collided : TransitionState.OK;
}

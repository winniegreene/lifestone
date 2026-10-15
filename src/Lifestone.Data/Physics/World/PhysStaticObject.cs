// Partly ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/PhysicsObj.cs (FindObjCollisions),
// Source/ACE.Server/Physics/PartArray.cs (FindObjCollisions), Source/ACE.Server/Physics/PhysicsPart.cs
// (find_obj_collisions) and Source/ACE.Server/Physics/Common/GfxObj.cs (FindObjCollisions) (AGPL-3.0)
// with changes: an immutable static object (no weenie, no motion), checked against the retail functions cited below;
// the collided-object record of dynamic objects (COLLISIONINFO::add_object) waits for PhysicsObj (PH5), since a static
// only marks an environment collision.

using System.Numerics;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics.World;

/// <summary>
/// One part of a static object or building (retail <c>CPhysicsPart</c> as collision sees it): its position, the level-0
/// GfxObj's shape (<c>gfxobj[0]</c>), its scale, and whether its first degrade level is a camera-facing one.
/// </summary>
/// <param name="Position">The part's cell and landblock-local frame (retail <c>CPhysicsPart::pos</c>).</param>
/// <param name="Shape">The level-0 GfxObj's bounds, spheres and physics shape.</param>
/// <param name="GfxObjScale">The part's scale (retail <c>gfxobj_scale</c>: the Setup's default scale times the object's).</param>
/// <param name="Always2D">Retail <c>CPhysicsPart::Always2D</c> (11.4186 0x0050D8A0, 11.6096 0x0050E370): the degrade table's
/// first level has a mode other than 1, so the part's cells are found from its sphere instead of its box.</param>
public sealed record PhysPart(CellFrame Position, GfxObjShape Shape, Vector3 GfxObjScale, bool Always2D)
{
    /// <summary>
    /// Retail <c>CPhysicsPart::find_obj_collisions</c> (11.4186 0x0050D8D0, 11.6096 0x0050E3A0): with a physics shape, the
    /// transition's spheres are cached in the part's space (scaled by its height scale) and tested by
    /// <c>CGfxObj::find_obj_collisions</c> (11.4186 0x00534700, 11.6096 0x00535440): the BSP is only asked when a moving
    /// sphere comes within the epsilon of the physics sphere (the decompile keeps only the test's flags; ACE's reading,
    /// squared distance less the squared radius sum below the epsilon); an initial placement is a placement insert.
    /// </summary>
    internal TransitionState FindObjCollisions(Transition transition)
    {
        var physics = Shape.Physics;
        if (physics is null)
        {
            return TransitionState.OK;
        }

        var path = transition.SpherePath;
        path.CacheLocalSpaceSphere(Position, GfxObjScale.Z);
        var physicsSphere = physics.Bsp.Sphere;
        for (var i = 0; i < path.NumSphere; i++)
        {
            ref readonly var sphere = ref path.LocalSpaceSphere[i];
            var radiusSum = physicsSphere.Radius + sphere.Radius;
            if ((physicsSphere.Center - sphere.Center).LengthSquared() - (radiusSum * radiusSum) < PhysicsGlobals.Epsilon)
            {
                return path.InsertType == InsertType.InitialPlacement
                    ? physics.Bsp.PlacementInsert(transition)
                    : physics.Bsp.FindCollisions(transition, GfxObjScale.Z);
            }
        }

        return TransitionState.OK;
    }
}

/// <summary>
/// A static object of the world as collision sees it (retail <c>CPhysicsObj</c> created by <c>CPhysicsObj::makeObject</c>
/// for a LandBlockInfo static, a scenery object or an EnvCell static): its position, state, scale and collision shapes.
/// </summary>
/// <remarks>
/// <para>Retail tests an object's physics BSP parts when it has any (HasPhysicsBsp), else its cylinder-spheres, else its
/// spheres. A static has no weenie, so the player-killer and missile rules never apply to it.</para>
/// <para>Immutable; shared by every cell it reaches (its shadows).</para>
/// </remarks>
public sealed class PhysStaticObject
{
    private readonly PhysPart[] _parts;
    private readonly PhysCylSphere[] _cylSpheres;
    private readonly PhysSphere[] _spheres;

    internal PhysStaticObject(uint modelId, CellFrame position, PhysicsState state, float scale, PhysPart[] parts, PhysCylSphere[] cylSpheres, PhysSphere[] spheres)
    {
        ModelId = modelId;
        Position = position;
        State = state;
        Scale = scale;
        _parts = parts;
        _cylSpheres = cylSpheres;
        _spheres = spheres;
    }

    /// <summary>The Setup (0x02) or GfxObj (0x01) the object was made from.</summary>
    public uint ModelId { get; }

    /// <summary>The object's cell and landblock-local frame (retail <c>m_position</c>).</summary>
    public CellFrame Position { get; }

    /// <summary>The object's physics state (Static, HasPhysicsBsp when a part's level-0 GfxObj has a physics BSP, and the defaults).</summary>
    public PhysicsState State { get; }

    /// <summary>The object's scale (retail <c>m_scale</c>; scenery has its own, statics 1).</summary>
    public float Scale { get; }

    /// <summary>The parts, posed at rest.</summary>
    public IReadOnlyList<PhysPart> Parts => _parts;

    /// <summary>The Setup's cylinder-spheres in the object's frame (unscaled).</summary>
    public IReadOnlyList<PhysCylSphere> CylSpheres => _cylSpheres;

    /// <summary>The Setup's spheres in the object's frame (unscaled).</summary>
    public IReadOnlyList<PhysSphere> Spheres => _spheres;

    /// <summary>True when the object can stop anything (a physics BSP part, a cylinder-sphere or a sphere).</summary>
    public bool HasCollisionShape =>
        ((State & PhysicsState.HasPhysicsBsp) != 0) || _cylSpheres.Length > 0 || _spheres.Length > 0;

    /// <summary>The parts as a span (the cell searches).</summary>
    internal ReadOnlySpan<PhysPart> PartSpan => _parts;

    /// <inheritdoc/>
    public override string ToString() => $"PhysStaticObject 0x{ModelId:X8} at {Position}";

    /// <summary>
    /// Retail <c>CPhysicsObj::FindObjCollisions</c> (11.4186 0x0050F050, 11.6096 0x0050FB20) for a static object: nothing
    /// for an ethereal object that ignores collisions; an ethereal obstruction (the object, or an ethereal mover against a
    /// non-static) is only placed against and never stepped down onto; then the physics BSP parts
    /// (<c>CPartArray::FindObjCollisions</c>, 11.4186 0x00518180, 11.6096 0x00518CB0), else the cylinder-spheres, else the
    /// spheres, until one stops the transition. A static that stops an object without contact is an environment
    /// collision.
    /// </summary>
    internal TransitionState FindObjCollisions(Transition transition)
    {
        if ((State & PhysicsState.Ethereal) != 0 && (State & PhysicsState.IgnoreCollisions) != 0)
        {
            return TransitionState.OK;
        }

        var path = transition.SpherePath;
        var info = transition.ObjectInfo;
        var ethereal = (State & PhysicsState.Ethereal) != 0 || (info.Ethereal && (State & PhysicsState.Static) == 0);
        if (ethereal && path.StepDown)
        {
            return TransitionState.OK;
        }

        path.ObstructionEthereal = ethereal;

        // OBJECTINFO::missile_ignore for an object without a weenie: only a missile is ignored.
        var missileIgnore = (State & PhysicsState.Missile) != 0;
        var isCreature = (State & PhysicsState.Missile) != 0;
        var state = TransitionState.OK;
        if ((State & PhysicsState.HasPhysicsBsp) != 0 && !missileIgnore)
        {
            foreach (var part in _parts)
            {
                state = part.FindObjCollisions(transition);
                if (state != TransitionState.OK)
                {
                    break;
                }
            }
        }
        else if (_cylSpheres.Length > 0 && !missileIgnore)
        {
            foreach (var cylSphere in _cylSpheres)
            {
                state = cylSphere.IntersectsSphere(Position, Scale, transition);
                if (state != TransitionState.OK)
                {
                    break;
                }
            }
        }
        else if (_spheres.Length > 0 && !missileIgnore)
        {
            foreach (var sphere in _spheres)
            {
                state = sphere.IntersectsSphere(Position, Scale, transition, isCreature);
                if (state != TransitionState.OK)
                {
                    break;
                }
            }
        }

        if (state != TransitionState.OK && !path.StepDown)
        {
            if ((State & PhysicsState.Static) != 0)
            {
                if ((info.State & ObjectInfoState.Contact) == 0)
                {
                    transition.CollisionInfo.CollidedWithEnvironment = true;
                }
            }
            else if (ethereal || (isCreature && (info.State & ObjectInfoState.IgnoreCreatures) != 0))
            {
                state = TransitionState.OK;
                transition.CollisionInfo.CollisionNormalValid = false;
            }
        }

        path.ObstructionEthereal = false;
        return state;
    }
}

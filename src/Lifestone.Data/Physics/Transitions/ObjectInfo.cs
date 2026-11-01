// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/ObjectInfo.cs (AGPL-3.0)
// with changes: filled from explicit arguments instead of a PhysicsObj (PH5 supplies them); the weenie flags (impenetrable,
// player, PK, PK-lite) are passed in the state; kill_velocity records the request for the caller instead of zeroing a
// PhysicsObj's velocity; validate_walkable re-expressed from retail OBJECTINFO::validate_walkable, including its viewer
// branch and its walk-interp limit.

using System.Numerics;
using Lifestone.Data.Physics.Primitives;

namespace Lifestone.Data.Physics.Transitions;

/// <summary>
/// The moving object as one transition sees it (retail <c>OBJECTINFO</c>): its flags, scale and step heights.
/// </summary>
internal sealed class ObjectInfo
{
    /// <summary>
    /// The moving object (retail <c>OBJECTINFO::object</c>), compared by reference: a static that is the moving object
    /// is skipped, and a transition without an object cannot enter any cell (<c>CObjCell::check_entry_restrictions</c>
    /// refuses it, as retail does).
    /// </summary>
    public object? Object;

    /// <summary>The moving object's physics state (retail <c>object-&gt;state</c>): Gravity, Missile, Ethereal and the rest.</summary>
    public PhysicsState ObjectState;

    /// <summary>The object's flags for this transition.</summary>
    public ObjectInfoState State;

    /// <summary>The object's scale.</summary>
    public float Scale = 1.0f;

    /// <summary>How high the object may step up (its Setup's step-up height, scaled).</summary>
    public float StepUpHeight;

    /// <summary>How far the object may step down (its Setup's step-down height, scaled).</summary>
    public float StepDownHeight;

    /// <summary>True when the object is ethereal.</summary>
    public bool Ethereal;

    /// <summary>True when the object steps down (everything but missiles).</summary>
    public bool StepDown;

    /// <summary>The missile's target, when it has one.</summary>
    public uint TargetId;

    /// <summary>
    /// True once the transition asked for the object's velocity to be zeroed (retail <c>OBJECTINFO::kill_velocity</c>
    /// zeroes it at once with <c>CPhysicsObj::set_velocity</c>); the object applies it after the transition.
    /// </summary>
    public bool VelocityKilled;

    /// <summary>
    /// True when the transition meets the fixed world only (terrain, cells, buildings and landblock statics) and passes every moving
    /// object's shadow (<see cref="Objects.PhysicsObj.FindObjCollisions"/> answers OK): Lifestone's route planner's probes, which judge the
    /// world the route is built in while the runner meets doors, creatures and NPCs at walk time (M5 item 5 fix F3). Not retail: retail has
    /// no route planner; every retail transition leaves this false. Set after <see cref="Init"/>, which clears it.
    /// </summary>
    public bool FixedWorldOnly;

    /// <summary>
    /// Retail <c>OBJECTINFO::init</c> (11.4186 0x0050CF30, 11.6096 0x0050DA00): the object, its transition flags, scale and
    /// step heights; ethereal from its state, and step-down for everything but missiles. The weenie's flags
    /// (IsImpenetrable, IsPlayer, IsPK, IsPKLite) arrive in <paramref name="state"/>.
    /// </summary>
    public void Init(object? obj, ObjectInfoState state, PhysicsState objectState, float scale, float stepUpHeight, float stepDownHeight)
    {
        Object = obj;
        State = state;
        ObjectState = objectState;
        Scale = scale;
        StepUpHeight = stepUpHeight;
        StepDownHeight = stepDownHeight;
        Ethereal = (objectState & PhysicsState.Ethereal) != 0;
        StepDown = (objectState & PhysicsState.Missile) == 0;
        VelocityKilled = false;
        FixedWorldOnly = false;
    }

    /// <summary>
    /// Retail <c>OBJECTINFO::is_valid_walkable</c> (11.4186 0x0050CF10, 11.6096 0x0050D9E0) through
    /// <c>CPhysicsObj::is_valid_walkable</c> (0x0050F530, 0x00510000): a normal Z of at least <see cref="PhysicsGlobals.FloorZ"/>.
    /// </summary>
    public static bool IsValidWalkable(Vector3 normal) => normal.Z >= PhysicsGlobals.FloorZ;

    /// <summary>
    /// Retail <c>OBJECTINFO::get_walkable_z</c> (11.4186 0x0050CF20, 11.6096 0x0050D9F0) through
    /// <c>CPhysicsObj::get_walkable_z</c>: <see cref="PhysicsGlobals.FloorZ"/>.
    /// </summary>
    public static float GetWalkableZ() => PhysicsGlobals.FloorZ;

    /// <summary>Retail <c>OBJECTINFO::kill_velocity</c> (11.4186 0x0050CFE0, 11.6096 0x0050DAB0): see <see cref="VelocityKilled"/>.</summary>
    public void KillVelocity() => VelocityKilled = true;

    /// <summary>
    /// Retail <c>OBJECTINFO::validate_walkable</c> (11.4186 0x0050D010, 11.6096 0x0050DAE0): the moving sphere against a
    /// terrain polygon's plane (<paramref name="contactPlane"/>, in the land cell's landblock space, with
    /// <paramref name="checkPos"/> moved there). The low point may sink <paramref name="waterDepth"/> below it.
    /// </summary>
    /// <remarks>
    /// <para><b>Viewer.</b> The camera's sphere is only pushed out: when it is more than the epsilon into the plane and its
    /// path from the current centre crossed the plane within this step, it moves back to the plane along that path and
    /// the transition is adjusted, unless the path began in an EnvCell.</para>
    /// <para><b>Anything else.</b> Touching the plane (within the epsilon) sets the contact plane when it is walkable,
    /// the object is not on walkable ground, or a step down is running. Below the plane, the walkable check refuses the
    /// step; otherwise the same planes become the contact plane and the sphere rises onto it, a step down giving up the
    /// part of its interpolation the rise uses (refused when that leaves the step or goes below -0.1). An object without
    /// contact records the plane as an environment collision.</para>
    /// </remarks>
    public TransitionState ValidateWalkable(in PhysSphere checkPos, in Plane contactPlane, bool isWater, float waterDepth, Transition transition, uint landCellId)
    {
        var path = transition.SpherePath;
        var collisions = transition.CollisionInfo;
        if ((State & ObjectInfoState.IsViewer) != 0)
        {
            var depth = contactPlane.DistanceTo(checkPos.Center) - checkPos.Radius;
            if (depth > -PhysicsGlobals.Epsilon)
            {
                return TransitionState.OK;
            }

            var movement = checkPos.Center - path.GlobalCurrCenter[0];
            var fraction = depth / Vector3.Dot(movement, contactPlane.Normal);
            if ((fraction <= 0.0f || fraction > 1.0f) && path.HasBeginPos && (path.BeginPos.CellId & 0xFFFF) >= 0x100)
            {
                return TransitionState.OK;
            }

            path.AddOffsetToCheckPos(movement * -fraction);
            collisions.SetCollisionNormal(contactPlane.Normal);
            collisions.CollidedWithEnvironment = true;
            return TransitionState.Adjusted;
        }

        var distance = contactPlane.DistanceTo(new Vector3(checkPos.Center.X, checkPos.Center.Y, checkPos.Center.Z - checkPos.Radius)) + waterDepth;
        if (distance >= -PhysicsGlobals.Epsilon)
        {
            if (distance > PhysicsGlobals.Epsilon)
            {
                return TransitionState.OK;
            }

            if (path.StepDown || (State & ObjectInfoState.OnWalkable) == 0 || IsValidWalkable(contactPlane.Normal))
            {
                collisions.SetContactPlane(contactPlane, isWater);
                collisions.ContactPlaneCellId = landCellId;
            }

            if ((State & ObjectInfoState.Contact) == 0 && !path.StepDown)
            {
                collisions.SetCollisionNormal(contactPlane.Normal);
                collisions.CollidedWithEnvironment = true;
            }

            return TransitionState.OK;
        }

        if (path.CheckWalkable)
        {
            return TransitionState.Collided;
        }

        var result = TransitionState.OK;
        var rise = distance / contactPlane.Normal.Z;
        if (path.StepDown || (State & ObjectInfoState.OnWalkable) == 0 || IsValidWalkable(contactPlane.Normal))
        {
            collisions.SetContactPlane(contactPlane, isWater);
            collisions.ContactPlaneCellId = landCellId;
            if (path.StepDown)
            {
                var interp = (1.0f - (-1.0f / (path.StepDownAmt * path.WalkInterp) * rise)) * path.WalkInterp;
                if (interp >= path.WalkInterp || interp < -0.1f)
                {
                    return TransitionState.Collided;
                }

                path.WalkInterp = interp;
            }

            path.AddOffsetToCheckPos(new Vector3(0f, 0f, -rise));
            result = TransitionState.Adjusted;
        }

        if ((State & ObjectInfoState.Contact) == 0 && !path.StepDown)
        {
            collisions.SetCollisionNormal(contactPlane.Normal);
            collisions.CollidedWithEnvironment = true;
        }

        return result;
    }
}

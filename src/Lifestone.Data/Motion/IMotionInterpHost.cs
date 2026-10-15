using System.Numerics;

namespace Lifestone.Data.Motion;

/// <summary>
/// What the motion interpreter asks of an object's weenie (retail <c>CWeenieObject</c>, the virtuals
/// <c>CMotionInterp</c> calls). The player's implementation reads its qualities (run and jump skill, burden, stamina);
/// a server object's answers only <see cref="IsCreature"/> and fails the queries, so the interpreter falls back to the
/// run rate the server sent.
/// </summary>
/// <remarks>
/// The 11.4186 decompile calls these through the weenie's vtable. Slot 11 is <c>IsCreature</c> (it is also the
/// <c>is_creature</c> test of <c>CPhysicsObj::FindObjCollisions</c>), slot 13 <c>InqRunRate</c>, slot 12
/// <c>InqJumpVelocity</c>, slot 15 <c>CanJump</c> and slot 16 <c>JumpStaminaCost</c>, each recognisable from its
/// arguments. Slot 5, used by <c>apply_current_movement</c> and <c>move_to_interpreted_state</c>, follows slot 4
/// (<c>IsPlayer</c> in <c>FindObjCollisions</c>) and is read as <c>IsThePlayer</c>: only the client's own player applies
/// its raw motion state, and every other object's motion comes from the server. ACE reads it as <c>IsCreature</c>.
/// </remarks>
public interface IMotionWeenie
{
    /// <summary>Retail <c>CWeenieObject::IsCreature</c>: a creature's motions are adjusted and need ground contact.</summary>
    bool IsCreature { get; }

    /// <summary>Retail <c>CWeenieObject::IsThePlayer</c>: the client's own player character.</summary>
    bool IsThePlayer { get; }

    /// <summary>Retail <c>InqRunRate</c>: the run rate from the qualities; false when the weenie cannot tell.</summary>
    bool TryGetRunRate(out float rate);

    /// <summary>Retail <c>InqJumpVelocity</c>: the jump's vertical speed for an extent in [0, 1]; false when unknown.</summary>
    bool TryGetJumpVelocity(float extent, out float velocityZ);

    /// <summary>Retail <c>CanJump</c>: false when the weenie cannot jump with its load.</summary>
    bool CanJump(float extent);

    /// <summary>
    /// Retail <c>JumpStaminaCost</c>: the stamina a jump of <paramref name="extent"/> costs; false when the weenie
    /// cannot pay it.
    /// </summary>
    bool TryGetJumpStaminaCost(float extent, out int staminaCost);
}

/// <summary>
/// What the motion interpreter asks of the object it moves (retail <c>CPhysicsObj</c>, as <c>CMotionInterp</c> uses it).
/// The physics track's <c>PhysicsObj</c> implements it for the player; the avatar body (AV7) for server objects.
/// </summary>
/// <remarks>
/// The part-array calls (<c>CPhysicsObj::DoInterpretedMotion</c>, <c>StopInterpretedMotion</c>,
/// <c>StopCompletely_Internal</c>, <c>InitializeMotionTables</c>, <c>CheckForCompletedMotions</c> and
/// <c>RemoveLinkAnimations</c>) go to the <see cref="Animation.MotionPlayer"/> the interpreter is given, not through
/// this interface. The host steps that player itself, and each update quantum, after
/// <see cref="MovementManager.UseTime"/>, calls <see cref="Animation.MotionPlayer.HandleMovement"/> (retail
/// <c>CPhysicsObj::UpdateObjectInternal</c> → <c>CPartArray::HandleMovement</c>), so that queued motions without
/// animations complete.
/// </remarks>
public interface IMotionInterpHost
{
    /// <summary>Retail <c>transient_state</c> Contact (0x1): touching a surface.</summary>
    bool HasContact { get; }

    /// <summary>Retail <c>transient_state</c> OnWalkable (0x2): standing on a walkable surface.</summary>
    bool OnWalkable { get; }

    /// <summary>Retail <c>state</c> Gravity (0x400).</summary>
    bool HasGravity { get; }

    /// <summary>Retail <c>cell != null</c>: the object is in the world. Without a cell, link animations are dropped.</summary>
    bool InCell { get; }

    /// <summary>
    /// Retail <c>CPhysicsObj::movement_is_autonomous</c> (<c>last_move_was_autonomous</c>): the object's last movement
    /// was the client's own. With <see cref="IMotionWeenie.IsThePlayer"/> it selects the raw motion state.
    /// </summary>
    bool MovementIsAutonomous { get; }

    /// <summary>Retail <c>CPhysicsObj::IsFullyConstrained</c>: the position constraint forbids a jump.</summary>
    bool IsFullyConstrained { get; }

    /// <summary>The velocity in world (landblock) axes (retail <c>m_velocityVector</c>).</summary>
    Vector3 Velocity { get; }

    /// <summary>The object's orientation (its position frame's rotation), to take <see cref="Velocity"/> to local axes.</summary>
    Quaternion Orientation { get; }

    /// <summary>The object's weenie, or null for an object without one.</summary>
    IMotionWeenie? Weenie { get; }

    /// <summary>Retail <c>CPhysicsObj::set_local_velocity(v, send_event 1)</c>: sets the velocity from local axes.</summary>
    void SetLocalVelocity(Vector3 localVelocity);

    /// <summary>
    /// Retail <c>CPhysicsObj::set_on_walkable</c>. A jump clears it; when that ends a walkable contact, the host calls
    /// <see cref="MotionInterpreter.LeaveGround"/> (retail <c>MovementManager::LeaveGround</c>), and when it starts one,
    /// <see cref="MotionInterpreter.HitGround"/>.
    /// </summary>
    void SetOnWalkable(bool onWalkable);

    /// <summary>Retail <c>CPhysicsObj::cancel_moveto</c>: cancels a MoveTo (AV14) with error 0x36.</summary>
    void CancelMoveTo();

    /// <summary>Retail <c>CPhysicsObj::unstick_from_object</c>: ends a sticky follow (AV14).</summary>
    void UnstickFromObject();
}

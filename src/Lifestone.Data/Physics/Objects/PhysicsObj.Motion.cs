using System.Numerics;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics.Objects;

/// <summary>
/// The motion a <see cref="PhysicsObj"/> hosts: the avatars track's <see cref="MovementManager"/> (retail
/// <c>MovementManager</c>) with its <see cref="MotionInterpreter"/> (<c>CMotionInterp</c>) and MoveTo manager, the position
/// managers, and the <see cref="IMotionInterpHost"/> and <see cref="IMoveToHost"/> sides they call back into.
/// </summary>
public sealed partial class PhysicsObj
{
    /// <summary>Retail <c>CPhysicsObj::IsFullyConstrained</c> (11.4186 0x0050EC60, 11.6096 0x0050F730).</summary>
    public bool IsFullyConstrained => Positions?.IsFullyConstrained ?? false;

    /// <summary>
    /// Retail <c>CPhysicsObj::IsMovingTo</c> (11.4186 0x0050EB10, 11.6096 0x0050F5E0): a server MoveTo or TurnTo is under
    /// way (<see cref="MovementManager.IsMovingTo"/>).
    /// </summary>
    public bool IsMovingTo => Movement?.IsMovingTo ?? false;

    /// <summary>
    /// Retail <c>CPhysicsObj::IsInterpolating</c> (11.4186 0x0050EB50, 11.6096 0x0050F620): the interpolation manager has
    /// positions queued.
    /// </summary>
    public bool IsInterpolating => Positions?.IsInterpolating ?? false;

    /// <inheritdoc/>
    bool IMotionInterpHost.HasGravity => (State & PhysicsState.Gravity) != 0;

    /// <inheritdoc/>
    bool IMotionInterpHost.InCell => Cell is not null;

    /// <inheritdoc/>
    bool IMotionInterpHost.MovementIsAutonomous => LastMoveWasAutonomous;

    /// <inheritdoc/>
    Quaternion IMotionInterpHost.Orientation => Position.Frame.Orientation;

    /// <inheritdoc/>
    IMotionWeenie? IMotionInterpHost.Weenie => Weenie;

    /// <summary>
    /// Retail <c>CPhysicsObj::get_minterp</c> (11.4186 0x005120C0, 11.6096 0x00512B90): the motion interpreter, made with
    /// its default state when the object has none yet (on an object without a motion table every motion then fails with
    /// <see cref="WeenieError.NoAnimationTable"/>).
    /// </summary>
    public MotionInterpreter GetMinterp() => Motion ?? MakeMovementManager(initMotion: true).Motion;

    /// <summary>
    /// Retail <c>CPhysicsObj::unpack_movement</c> (11.4186 0x00512040, 11.6096 0x00512B10): a server movement for this
    /// object (an UpdateMotion's or a CreateObject's MovementData), through the movement manager
    /// (<see cref="MovementManager.UnpackMovement"/>): a type-0 interpreted state, or a MoveTo or TurnTo (types 6-9) that
    /// the MoveTo manager then carries out in <see cref="UpdateObject"/>. For the player the caller first records the
    /// message's autonomous bit as <see cref="LastMoveWasAutonomous"/> (<c>CPhysics::SetObjectMovement</c>, 11.4186
    /// 0x00509690, 11.6096 0x0050A160).
    /// </summary>
    /// <remarks>
    /// An object without a movement manager gets one, active unless Static, as retail's does; its interpreter enters its
    /// default state once, as retail's is made by <c>MovementManager::get_minterp</c> (11.4186 0x005242A0;
    /// TODO(physics): 11.6096 not in the symbol table) when the unpacking first needs it.
    /// </remarks>
    /// <param name="movement">The movement.</param>
    /// <param name="actions">The type-0 state's queued actions.</param>
    /// <param name="scratch">A state to unpack into, reused by the caller (never the interpreter's own).</param>
    /// <param name="failure">The unknown low word, for <see cref="MovementUnpackResult.UnknownCommand"/>.</param>
    public MovementUnpackResult UnpackMovement(
        in WireMovement movement, ReadOnlySpan<WireMotionItem> actions, InterpretedMotionState scratch, out WireMotionFailure failure)
    {
        if (Movement is not { } manager)
        {
            manager = MakeMovementManager(initMotion: false);
            manager.Motion.EnterDefaultState();
        }

        return manager.UnpackMovement(movement, actions, scratch, out failure);
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::DoMotion</c> (11.4186 0x00510020, 11.6096 0x00510AF0): the client's own raw command (the
    /// last move becomes autonomous), through <c>MovementManager::PerformMovement</c> (0x005240D0, 0x00524CD0: the object
    /// becomes active) and <see cref="MotionInterpreter.PerformMovement"/>.
    /// </summary>
    /// <returns>The interpreter's answer; <see cref="WeenieError.NoAnimationTable"/> without a motion interpreter.</returns>
    public WeenieError DoMotion(uint motion, in MovementParameters parameters)
    {
        LastMoveWasAutonomous = true;
        if (Motion is null)
        {
            return WeenieError.NoAnimationTable;
        }

        SetActive(true);
        return Motion.PerformMovement(MotionMovementType.RawCommand, motion, parameters);
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::StopMotion</c> (11.4186 0x005100D0, 11.6096 0x00510BA0): stops the client's own raw command.
    /// </summary>
    public WeenieError StopMotion(uint motion, in MovementParameters parameters)
    {
        LastMoveWasAutonomous = true;
        if (Motion is null)
        {
            return WeenieError.NoAnimationTable;
        }

        SetActive(true);
        return Motion.PerformMovement(MotionMovementType.StopRawCommand, motion, parameters);
    }

    /// <summary>Retail <c>CPhysicsObj::StopCompletely</c> (11.4186 0x00510180, 11.6096 0x00510C50).</summary>
    public void StopCompletely()
    {
        if (Motion is null)
        {
            return;
        }

        SetActive(true);
        Motion.PerformMovement(MotionMovementType.StopCompletely, 0, new MovementParameters());
    }

    /// <summary>
    /// The jump of retail <c>ClientCombatSystem::DoJump</c> (11.4186 0x0056B110, 11.6096 0x0056BE50, which the symbol table
    /// lacks and docs/research/retail-addresses-m2.md §1.2 settles from the bytes): <c>get_minterp()-&gt;jump(extent)</c> (<see cref="MotionInterpreter.Jump"/>, 11.4186 0x00528780, 11.6096
    /// 0x00529390). Allowed, the object leaves walkable ground and <c>LeaveGround</c> sets its velocity: the state
    /// velocity with the weenie's jump speed (<see cref="IMotionWeenie.TryGetJumpVelocity"/>, which the player's weenie
    /// answers with <see cref="MovementSystem.GetJumpHeight"/>). The jump bar's extent and the Jump message are the player
    /// controller's (PH7).
    /// </summary>
    /// <param name="extent">The jump bar's extent in [0, 1].</param>
    /// <param name="staminaCost">The stamina the jump costs, when allowed.</param>
    public WeenieError Jump(float extent, out int staminaCost) => GetMinterp().Jump(extent, out staminaCost);

    /// <summary>
    /// Retail <c>CPhysicsObj::InterpolateTo</c> (11.4186 0x005104F0, 11.6096 0x00510FC0): makes the position manager and
    /// interpolates towards <paramref name="position"/>; an arrival without keep-heading takes the target's heading.
    /// </summary>
    public InterpolateToResult InterpolateTo(in CellFrame position, bool keepHeading)
    {
        var positions = MakePositionManager();
        var result = positions.InterpolateTo(position, keepHeading, PositionContextNow());
        if (result.SetHeading is { } heading)
        {
            SetHeading(heading);
        }

        return result;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::ConstrainTo</c> (11.4186 0x00510520, 11.6096 0x00510FF0): makes the position manager and
    /// constrains the object to <paramref name="position"/> (the server's word on the player: m2-plan PH7).
    /// </summary>
    public void ConstrainTo(in CellFrame position, float startDistance, float maxDistance) =>
        MakePositionManager().ConstrainTo(position, startDistance, maxDistance, PositionContextNow());

    /// <summary>Retail <c>PositionManager::UnConstrain</c> (11.4186 0x005552B0, 11.6096 0x00555ED0).</summary>
    public void UnConstrain() => Positions?.UnConstrain();

    /// <summary>Retail <c>CPhysicsObj::StopInterpolating</c> (11.4186 0x0050EB40, 11.6096 0x0050F610).</summary>
    public void StopInterpolating() => Positions?.StopInterpolating();

    /// <summary>
    /// Retail <c>CPhysicsObj::teleport_hook</c> (11.4186 0x00514ED0, 11.6096 0x005159D0), at a teleport: the MoveTo is
    /// cancelled with error 0x3C (<see cref="WeenieError.ITeleported"/>), the object unsticks, stops interpolating and is
    /// unconstrained, its target is cleared and the objects that target it are told it teleported
    /// (<see cref="TargetStatus.Teleported"/>), and its collisions end.
    /// </summary>
    public void TeleportHook()
    {
        Movement?.CancelMoveTo(WeenieError.ITeleported);
        Positions?.UnStick();
        Positions?.StopInterpolating();
        Positions?.UnConstrain();
        if (_targets is { } targets)
        {
            targets.ClearTarget();
            targets.NotifyVoyeurOfEvent(TargetStatus.Teleported);
        }

        ReportCollisionEnd(forceEnd: true);
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::cancel_moveto</c> (11.4186 0x005101F0, 11.6096 0x00510CC0): cancels the MoveTo under way
    /// with error 0x36 (<see cref="WeenieError.ActionCancelled"/>). The interpreter calls it for a command that cancels
    /// MoveTos (the player's own keys), the sticky manager when a stick ends.
    /// </summary>
    public void CancelMoveTo() => Movement?.CancelMoveTo(WeenieError.ActionCancelled);

    /// <summary>
    /// Retail <c>CPhysicsObj::unstick_from_object</c> (11.4186 0x0050EAE0, 11.6096 0x0050F5B0): ends a sticky follow
    /// (<see cref="PositionManager.UnStick"/>).
    /// </summary>
    public void UnstickFromObject() => Positions?.UnStick();

    /// <summary>
    /// Retail <c>PositionManager::StickTo</c> through <c>CPhysicsObj::get_position_manager</c> (11.4186 0x00512130, 11.6096
    /// 0x00512C00, which makes the position manager): follows <paramref name="objectId"/>'s cylinder, as a MoveTo with the
    /// Sticky flag ends.
    /// </summary>
    public void StickTo(uint objectId, float radius, float height) => MakePositionManager().StickTo(objectId, radius, height, this);

    /// <summary>
    /// Retail <c>CPhysicsObj::get_sticky_object_id</c> (11.4186 0x0050EAF0, 11.6096 0x0050F5C0): the object stuck to, or 0.
    /// </summary>
    public uint GetStickyObjectId() => Positions?.GetStickyObjectId() ?? 0;

    /// <inheritdoc/>
    /// <remarks>Retail <c>CPhysicsObj::set_on_walkable</c>, which the interpreter's <c>jump</c> calls.</remarks>
    void IMotionInterpHost.SetOnWalkable(bool onWalkable) => SetOnWalkable(onWalkable);

    /// <summary>
    /// Retail <c>CPhysicsObj::MakeMovementManager</c> (11.4186 0x00510270, 11.6096 0x00510D40), with
    /// <c>MovementManager::Create</c> (0x00524050, 0x00524C50) and <c>MovementManager::EnterDefaultState</c> (0x005241C0,
    /// 0x00524DC0): the movement manager with the interpreter over the object's motion table, with
    /// <paramref name="initMotion"/> entered into its default state twice (the manager creates the interpreter and enters
    /// the default state once because it has an object, then once more), and a dynamic object active.
    /// </summary>
    private MovementManager MakeMovementManager(bool initMotion)
    {
        if (Movement is not null)
        {
            return Movement;
        }

        var motion = new MotionInterpreter(MotionPlayer, this);
        Motion = motion;
        Movement = new MovementManager(motion, this);
        if (initMotion)
        {
            motion.EnterDefaultState();
            motion.EnterDefaultState();
        }

        if ((State & PhysicsState.Static) == 0)
        {
            SetActiveFlag();
        }

        return Movement;
    }

    /// <summary>
    /// What the position managers read of the object this update: its position and contact, whether it is the player,
    /// its interpreter, and the object it sticks to (the interpolation manager skips its progress check while stuck).
    /// </summary>
    private PositionContext PositionContextNow() =>
        PositionContext.For(Position, HasContact, IsThePlayer, Motion, Positions?.GetStickyObjectId() ?? 0);

    /// <summary>
    /// Retail <c>CPhysicsObj::MakePositionManager</c> (11.4186 0x00510210, 11.6096 0x00510CE0): the position manager,
    /// made on first use, and a dynamic object active.
    /// </summary>
    private PositionManager MakePositionManager()
    {
        Positions ??= new PositionManager();
        if ((State & PhysicsState.Static) == 0)
        {
            SetActiveFlag();
        }

        return Positions;
    }
}

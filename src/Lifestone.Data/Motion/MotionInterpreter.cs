using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Data.Motion;

/// <summary>A motion the interpreter queued and is waiting on (retail <c>MotionNode</c> in <c>pending_motions</c>).</summary>
/// <param name="ContextId">The context the motion was issued with.</param>
/// <param name="Motion">The motion, as the motion table received it.</param>
/// <param name="JumpErrorCode">Why a jump is refused until the motion completes, or <see cref="WeenieError.None"/>.</param>
public readonly record struct PendingMotion(uint ContextId, uint Motion, WeenieError JumpErrorCode);

/// <summary>The movement types <c>CMotionInterp::PerformMovement</c> accepts (retail <c>MovementStruct::type</c>).</summary>
public enum MotionMovementType
{
    /// <summary>1: <see cref="MotionInterpreter.DoMotion"/>, a raw command.</summary>
    RawCommand = 1,

    /// <summary>2: <see cref="MotionInterpreter.DoInterpretedMotion"/>.</summary>
    InterpretedCommand = 2,

    /// <summary>3: <see cref="MotionInterpreter.StopMotion"/>.</summary>
    StopRawCommand = 3,

    /// <summary>4: <see cref="MotionInterpreter.StopInterpretedMotion"/>.</summary>
    StopInterpretedCommand = 4,

    /// <summary>5: <see cref="MotionInterpreter.StopCompletely"/>.</summary>
    StopCompletely = 5,
}

/// <summary>
/// Retail <c>CMotionInterp</c>, re-expressed from the 11.4186 decompile (PORTAL/canim/CMotionInterp.c; function names
/// and addresses cited, nothing copied) with the 11.6096 jump rules. It turns the motions an object is told to make into
/// motion-table calls on its <see cref="MotionPlayer"/>: the player's raw input (<see cref="DoMotion"/>,
/// <see cref="ApplyRawMovement"/>) and the server's interpreted states for every other object
/// (<see cref="MoveToInterpretedState"/>), with the rules for contact, falling, standing long jumps, actions and jumps.
/// </summary>
/// <remarks>
/// <para><b>Lifecycle.</b> The constructor is <c>CMotionInterp::Create</c>; it touches neither the host nor the parts.
/// Call <see cref="EnterDefaultState"/> once the host is ready, as <c>MovementManager</c> does right after creating
/// one. The interpreter listens to the player's <see cref="MotionTableManager.MotionDone"/> itself (retail
/// <c>CPhysicsObj::MotionDone</c> → <c>MovementManager::MotionDone</c> → <c>CMotionInterp::MotionDone</c>).</para>
/// <para><b>Addresses</b> are 11.4186 / 11.6096 (docs/research/retail-addresses-m2.md). Retail returns error numbers;
/// they are <see cref="WeenieError"/> here.</para>
/// <para><b>Where retail and ACE differ</b> (ACE's MotionInterp.cs is the test oracle; docs/avatars.md §5.4 lists them):
/// slot 5 of the weenie is <see cref="IMotionWeenie.IsThePlayer"/> in <see cref="ApplyCurrentMovement"/> and
/// <see cref="MoveToInterpretedState"/> (ACE: IsCreature); the jump-gating set is 11.6096's (ACE has the 2013 range and
/// Falling for Fallen); <see cref="SetHoldKey"/> handles the run key too; <see cref="JumpIsAllowed"/> checks the oldest
/// pending motion's jump error whenever one is queued (ACE: only with two or more); without a weenie
/// <see cref="GetStateVelocity"/> caps at run rate 1 (ACE: <see cref="MyRunRate"/>).</para>
/// <para>Not thread-safe; the caller's frame loop owns it. No allocation of its own per call once the pending queue has
/// grown; the motions it queues in a motion table allocate there (<see cref="Animation.MotionPlayer.MotionsQueued"/>).</para>
/// </remarks>
public sealed class MotionInterpreter
{
    /// <summary>Retail <c>BACKWARDS_FACTOR</c> (−0.64999998 in <c>adjust_motion</c>): walking backwards is 65% speed.</summary>
    public const float BackwardsFactor = 0.65f;

    /// <summary>Retail walk animation speed, 3.12 m/s at speed 1 (<c>get_state_velocity</c>, <c>adjust_motion</c>).</summary>
    public const float WalkAnimSpeed = 3.12f;

    /// <summary>Retail run animation speed, 4.0 m/s at run rate 1 (<c>get_state_velocity</c>, <c>get_max_speed</c>).</summary>
    public const float RunAnimSpeed = 4f;

    /// <summary>Retail sidestep animation speed, 1.25 m/s at speed 1 (<c>get_state_velocity</c>).</summary>
    public const float SidestepAnimSpeed = 1.25f;

    /// <summary>Retail sidestep factor (<c>adjust_motion</c> scales sidesteps by 0.5 × 3.12 / 1.25).</summary>
    public const float SidestepFactor = 0.5f;

    /// <summary>Retail run turn factor: running turns 1.5 times faster (<c>apply_run_to_command</c>).</summary>
    public const float RunTurnFactor = 1.5f;

    /// <summary>Retail cap on the sidestep speed while running (<c>apply_run_to_command</c>).</summary>
    public const float MaxSidestepAnimRate = 3f;

    /// <summary>Retail's limit on queued actions (<c>DoMotion</c> refuses a seventh).</summary>
    public const int MaxActions = 6;

    /// <summary>Retail <c>F_EPSILON</c> (0.0002) as <c>get_jump_v_z</c> and <c>get_leave_ground_velocity</c> use it.</summary>
    public const float Epsilon = 0.00019999999f;

    // Motion ids the interpreter compares with that MotionCommands does not name (11.6096 values).
    private const uint Fallen = 0x40000008;
    private const uint Crouch = 0x41000012;
    private const uint Sitting = 0x41000013;
    private const uint Sleeping = 0x41000014;
    private const uint Sanctuary = 0x10000057;
    private const uint AiTelegraphCast = 0x1000019B;

    private readonly IMotionParts? _parts;
    private readonly Queue<PendingMotion> _pending = new();

    /// <summary>
    /// Retail <c>CMotionInterp::Create</c> (0x00528C00 / 0x00529810): default raw and interpreted states, speed factor
    /// and run rate 1, not yet initialised. <paramref name="parts"/> is the object's motion-table player, or null for an
    /// object without a motion table (every part-array call then fails with <see cref="WeenieError.NoAnimationTable"/>).
    /// </summary>
    public MotionInterpreter(MotionPlayer? parts, IMotionInterpHost host)
        : this(parts is null ? null : new MotionPlayerParts(parts), host)
    {
        Parts = parts;
        if (parts is not null)
        {
            parts.Manager.MotionDone += (_, success) => MotionDone(success);
        }
    }

    /// <summary>For tests: a recording part array instead of a motion-table player.</summary>
    internal MotionInterpreter(IMotionParts? parts, IMotionInterpHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _parts = parts;
        Host = host;
    }

    /// <summary>The object the interpreter moves.</summary>
    public IMotionInterpHost Host { get; }

    /// <summary>The motion-table player, or null.</summary>
    public MotionPlayer? Parts { get; }

    /// <summary>The motion state as input expresses it (retail <c>raw_state</c>).</summary>
    public RawMotionState Raw { get; } = new();

    /// <summary>The motion state the motion table plays (retail <c>interpreted_state</c>).</summary>
    public InterpretedMotionState Interpreted { get; } = new();

    /// <summary>Retail <c>initted</c>: set by <see cref="EnterDefaultState"/>; until then the movement is not applied.</summary>
    public bool Initted { get; private set; }

    /// <summary>Retail <c>current_speed_factor</c> (1): divides the run speed in <see cref="GetAdjustedMaxSpeed"/>.</summary>
    public float CurrentSpeedFactor { get; set; } = 1f;

    /// <summary>
    /// Retail <c>standing_longjump</c>: a jump charged while standing still. While set, walking, running and
    /// sidestepping are recorded but not played. A server MovementData carries it (flag 0x2).
    /// </summary>
    public bool StandingLongJump { get; set; }

    /// <summary>Retail <c>jump_extent</c>: the extent of the jump in progress (0 on the ground).</summary>
    public float JumpExtent { get; private set; }

    /// <summary>Retail <c>server_action_stamp</c>: the newest server action applied (15 bits).</summary>
    public ushort ServerActionStamp { get; private set; }

    /// <summary>
    /// Retail <c>my_run_rate</c> (1): the run rate when the weenie cannot tell. Running sets it to the forward speed;
    /// a server MoveTo sets it from its run rate.
    /// </summary>
    public float MyRunRate { get; set; } = 1f;

    /// <summary>
    /// Restores retail's <see cref="GetStateVelocity"/> for a negative run rate: a NaN velocity standing, a reversed one
    /// moving (false by default, a glitch fix; docs/avatars.md §7). It also reaches <see cref="GetLeaveGroundVelocity"/>.
    /// </summary>
    public bool RetailNegativeRunRateCap { get; set; }

    /// <summary>Retail <c>motions_pending</c> (0x00527FE0 / 0x00528BF0).</summary>
    public bool MotionsPending => _pending.Count > 0;

    /// <summary>The number of motions waiting to complete.</summary>
    public int PendingMotionCount => _pending.Count;

    /// <summary>The motions waiting to complete, oldest first.</summary>
    public IReadOnlyCollection<PendingMotion> PendingMotions => _pending;

    /// <summary>Retail <c>InqStyle</c> (0x00527B10; TODO(avatars): 11.6096, folded): the interpreted style.</summary>
    public uint InqStyle() => Interpreted.CurrentStyle;

    /// <summary>
    /// Retail <c>enter_default_state</c> (0x00528C80 / 0x00529890): default raw and interpreted states (their actions
    /// dropped), the motion table's default state, a Ready motion queued (the pending queue is not cleared), initialised,
    /// then <see cref="LeaveGround"/>.
    /// </summary>
    public void EnterDefaultState()
    {
        Raw.InitDefaults();
        Interpreted.InitDefaults();
        _parts?.InitializeMotionTables();
        _pending.Enqueue(new PendingMotion(0, MotionCommands.Ready, WeenieError.None));
        Initted = true;
        LeaveGround();
    }

    /// <summary>
    /// Retail <c>PerformMovement</c> (0x00528E80 / 0x00529A90): one of the five movement types, then the part array's
    /// completion check. Any other type fails with <see cref="WeenieError.GeneralMovementFailure"/>.
    /// </summary>
    public WeenieError PerformMovement(MotionMovementType type, uint motion, in MovementParameters parameters)
    {
        WeenieError result;
        switch (type)
        {
            case MotionMovementType.RawCommand:
                result = DoMotion(motion, parameters);
                break;
            case MotionMovementType.InterpretedCommand:
                result = DoInterpretedMotion(motion, parameters);
                break;
            case MotionMovementType.StopRawCommand:
                result = StopMotion(motion, parameters);
                break;
            case MotionMovementType.StopInterpretedCommand:
                result = StopInterpretedMotion(motion, parameters);
                break;
            case MotionMovementType.StopCompletely:
                StopCompletely();
                result = WeenieError.None;
                break;
            default:
                return WeenieError.GeneralMovementFailure;
        }

        _parts?.CheckForCompletedMotions();
        return result;
    }

    /// <summary>
    /// Retail <c>DoMotion</c> (0x00528D20 / 0x00529930): a raw command, as the player's input issues it.
    /// <list type="number">
    /// <item>Cancels a MoveTo when <see cref="MovementParameters.CancelMoveTo"/>; applies the hold key when
    /// <see cref="MovementParameters.SetHoldKey"/>.</item>
    /// <item>Adjusts the motion and speed (<see cref="AdjustMotion"/>).</item>
    /// <item>Outside NonCombat refuses crouch, sitting, sleeping and chat emotes; refuses a seventh action.</item>
    /// <item><see cref="DoInterpretedMotion"/> with the adjusted motion; on success, the raw state records the
    /// unadjusted motion when <see cref="MovementParameters.ModifyRawState"/>.</item>
    /// </list>
    /// </summary>
    public WeenieError DoMotion(uint motion, in MovementParameters parameters)
    {
        if (parameters.CancelMoveTo)
        {
            Host.CancelMoveTo();
        }

        if (parameters.SetHoldKey)
        {
            SetHoldKey(parameters.HoldKeyToApply, parameters.CancelMoveTo);
        }

        var adjustedMotion = motion;
        var speed = parameters.Speed;
        AdjustMotion(ref adjustedMotion, ref speed, parameters.HoldKeyToApply);
        if (Interpreted.CurrentStyle != MotionCommands.NonCombat)
        {
            switch (motion)
            {
                case Crouch:
                    return WeenieError.CantCrouchInCombat;
                case Sitting:
                    return WeenieError.CantSitInCombat;
                case Sleeping:
                    return WeenieError.CantLieDownInCombat;
            }

            if ((motion & MotionCommands.ChatEmoteMask) != 0)
            {
                return WeenieError.CantChatEmoteInCombat;
            }
        }

        if ((motion & MotionCommands.ActionMask) != 0 && Interpreted.NumActions >= MaxActions)
        {
            return WeenieError.TooManyActions;
        }

        var result = DoInterpretedMotion(adjustedMotion, parameters with { Speed = speed });
        if (result == WeenieError.None && parameters.ModifyRawState)
        {
            Raw.ApplyMotion(motion, parameters);
        }

        return result;
    }

    /// <summary>
    /// Retail <c>DoInterpretedMotion</c> (0x00528360 / 0x00528F70): plays an interpreted motion.
    /// <list type="bullet">
    /// <item>When contact allows the motion (<see cref="ContactAllowsMove"/>): during a standing long jump, walking,
    /// running and sidestepping are only recorded; otherwise <c>Dead</c> first drops the link animations, the motion
    /// table plays the motion, and on success the motion is queued with the jump error it imposes
    /// (<see cref="WeenieError.YouCantJumpFromThisPosition"/> when <see cref="MovementParameters.DisableJumpDuringLink"/>,
    /// else <see cref="MotionAllowsJump"/> of the motion or, for a non-action, of the current forward command).</item>
    /// <item>Without contact, an action fails with <see cref="WeenieError.YouCantJumpWhileInTheAir"/>; anything else is
    /// only recorded.</item>
    /// <item>"Recorded" means applied to the interpreted state when
    /// <see cref="MovementParameters.ModifyInterpretedState"/>.</item>
    /// <item>An object outside a cell then drops its link animations.</item>
    /// </list>
    /// </summary>
    public WeenieError DoInterpretedMotion(uint motion, in MovementParameters parameters)
    {
        WeenieError result;
        if (ContactAllowsMove(motion))
        {
            if (StandingLongJump && IsLongJumpLocomotion(motion))
            {
                if (parameters.ModifyInterpretedState)
                {
                    Interpreted.ApplyMotion(motion, parameters);
                }

                result = WeenieError.None;
            }
            else
            {
                if (motion == MotionCommands.Dead)
                {
                    RemoveLinkAnimations();
                }

                result = PartsDo(motion, parameters.Speed);
                if (result == WeenieError.None)
                {
                    WeenieError jumpError;
                    if (parameters.DisableJumpDuringLink)
                    {
                        jumpError = WeenieError.YouCantJumpFromThisPosition;
                    }
                    else
                    {
                        jumpError = MotionAllowsJump(motion);
                        if (jumpError == WeenieError.None && (motion & MotionCommands.ActionMask) == 0)
                        {
                            jumpError = MotionAllowsJump(Interpreted.ForwardCommand);
                        }
                    }

                    AddToQueue(parameters.ContextId, motion, jumpError);
                    if (parameters.ModifyInterpretedState)
                    {
                        Interpreted.ApplyMotion(motion, parameters);
                    }
                }
            }
        }
        else if ((motion & MotionCommands.ActionMask) != 0)
        {
            result = WeenieError.YouCantJumpWhileInTheAir;
        }
        else
        {
            if (parameters.ModifyInterpretedState)
            {
                Interpreted.ApplyMotion(motion, parameters);
            }

            result = WeenieError.None;
        }

        if (!Host.InCell)
        {
            RemoveLinkAnimations();
        }

        return result;
    }

    /// <summary>
    /// Retail <c>StopMotion</c> (0x00528530 / 0x00529140): stops a raw command. Cancels a MoveTo when asked, adjusts
    /// the motion, stops the adjusted one (<see cref="StopInterpretedMotion"/>), and on success removes the unadjusted
    /// one from the raw state when <see cref="MovementParameters.ModifyRawState"/>.
    /// </summary>
    public WeenieError StopMotion(uint motion, in MovementParameters parameters)
    {
        if (parameters.CancelMoveTo)
        {
            Host.CancelMoveTo();
        }

        var adjustedMotion = motion;
        var speed = parameters.Speed;
        AdjustMotion(ref adjustedMotion, ref speed, parameters.HoldKeyToApply);
        var result = StopInterpretedMotion(adjustedMotion, parameters with { Speed = speed });
        if (result == WeenieError.None && parameters.ModifyRawState)
        {
            Raw.RemoveMotion(motion);
        }

        return result;
    }

    /// <summary>
    /// Retail <c>StopInterpretedMotion</c> (0x00528470 / 0x00529080). Without contact, or for walking, running or
    /// sidestepping during a standing long jump, the motion is only removed from the interpreted state (when
    /// <see cref="MovementParameters.ModifyInterpretedState"/>). Otherwise the motion table stops it and, on success,
    /// a Ready motion is queued and the motion removed. An object outside a cell then drops its link animations.
    /// </summary>
    public WeenieError StopInterpretedMotion(uint motion, in MovementParameters parameters)
    {
        WeenieError result;
        if (!ContactAllowsMove(motion) || (StandingLongJump && IsLongJumpLocomotion(motion)))
        {
            if (parameters.ModifyInterpretedState)
            {
                Interpreted.RemoveMotion(motion);
            }

            result = WeenieError.None;
        }
        else
        {
            result = PartsStop(motion, parameters.Speed);
            if (result == WeenieError.None)
            {
                AddToQueue(parameters.ContextId, MotionCommands.Ready, WeenieError.None);
                if (parameters.ModifyInterpretedState)
                {
                    Interpreted.RemoveMotion(motion);
                }
            }
        }

        if (!Host.InCell)
        {
            RemoveLinkAnimations();
        }

        return result;
    }

    /// <summary>
    /// Retail <c>StopCompletely</c> (0x00527E40 / 0x00528A50): cancels a MoveTo, resets both states' forward command to
    /// Ready at speed 1 and clears their sidestep and turn, stops the motion table completely and queues a Ready motion
    /// carrying the old forward command's jump error.
    /// </summary>
    public WeenieError StopCompletely()
    {
        Host.CancelMoveTo();
        var jumpError = MotionAllowsJump(Interpreted.ForwardCommand);
        Raw.ForwardCommand = MotionCommands.Ready;
        Raw.ForwardSpeed = 1f;
        Raw.SideStepCommand = 0;
        Raw.TurnCommand = 0;
        Interpreted.ForwardCommand = MotionCommands.Ready;
        Interpreted.ForwardSpeed = 1f;
        Interpreted.SideStepCommand = 0;
        Interpreted.TurnCommand = 0;
        _parts?.StopCompletelyInternal();
        AddToQueue(0, MotionCommands.Ready, jumpError);
        if (!Host.InCell)
        {
            RemoveLinkAnimations();
        }

        return WeenieError.None;
    }

    /// <summary>
    /// Retail <c>move_to_interpreted_state</c> (0x005289C0 / 0x005295D0): applies a motion state the server sent.
    /// <list type="number">
    /// <item>The raw style follows the new style; a MoveTo is cancelled.</item>
    /// <item>The movement (style, commands, speeds) is copied and applied (<see cref="ApplyCurrentMovement"/>, cancelling
    /// a MoveTo, and forbidding a jump during the links when the old forward command forbade one).</item>
    /// <item>Each action whose stamp is newer than <see cref="ServerActionStamp"/> under the 15-bit rule
    /// (<see cref="Sequence16.IsNewerAction"/>) is played, unless it is the player's own autonomous action, which the
    /// player already played: only for an object that is not the player, or for an action that is not autonomous, is
    /// the stamp stored and the action played (at its speed, with its autonomous bit). The stamp is stored only then.</item>
    /// </list>
    /// Always true here (retail returns false only without a physics object).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="state"/> is <see cref="Interpreted"/> itself.</exception>
    public bool MoveToInterpretedState(InterpretedMotionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (ReferenceEquals(state, Interpreted))
        {
            throw new ArgumentException("pass the server's state, not the interpreter's own", nameof(state));
        }

        Raw.CurrentStyle = state.CurrentStyle;
        Host.CancelMoveTo();
        var disallowJump = MotionAllowsJump(Interpreted.ForwardCommand) != WeenieError.None;
        Interpreted.CopyMovementFrom(state);
        ApplyCurrentMovement(cancelMoveTo: true, disallowJump);

        var parameters = new MovementParameters();
        var weenie = Host.Weenie;
        for (var i = 0; i < state.Actions.Count; i++)
        {
            var action = state.Actions[i];
            if (!Sequence16.IsNewerAction(ServerActionStamp, action.Stamp))
            {
                continue;
            }

            if ((weenie is not null && !weenie.IsThePlayer) || !action.Autonomous)
            {
                ServerActionStamp = (ushort)(action.Stamp & Sequence16.ActionStampMask);
                parameters = parameters
                    .WithFlag(MovementParameterFlags.Autonomous, action.Autonomous) with { Speed = action.Speed };
                DoInterpretedMotion(action.Action, parameters);
            }
        }

        return true;
    }

    /// <summary>
    /// Retail <c>apply_raw_movement</c> (0x005287E0 / 0x005293F0), the player's path: copies the raw style, commands and
    /// speeds into the interpreted state, adjusts each command with its own hold key (<see cref="AdjustMotion"/>), then
    /// <see cref="ApplyInterpretedMovement"/>.
    /// </summary>
    /// <param name="cancelMoveTo">Cancel a MoveTo with each motion.</param>
    /// <param name="disallowJump">Forbid a jump until the queued motions complete (retail's <c>disallow_jump</c>).</param>
    public void ApplyRawMovement(bool cancelMoveTo, bool disallowJump)
    {
        Interpreted.CurrentStyle = Raw.CurrentStyle;
        Interpreted.ForwardCommand = Raw.ForwardCommand;
        Interpreted.ForwardSpeed = Raw.ForwardSpeed;
        Interpreted.SideStepCommand = Raw.SideStepCommand;
        Interpreted.SideStepSpeed = Raw.SideStepSpeed;
        Interpreted.TurnCommand = Raw.TurnCommand;
        Interpreted.TurnSpeed = Raw.TurnSpeed;

        var motion = Interpreted.ForwardCommand;
        var speed = Interpreted.ForwardSpeed;
        AdjustMotion(ref motion, ref speed, Raw.ForwardHoldKey);
        Interpreted.ForwardCommand = motion;
        Interpreted.ForwardSpeed = speed;

        motion = Interpreted.SideStepCommand;
        speed = Interpreted.SideStepSpeed;
        AdjustMotion(ref motion, ref speed, Raw.SideStepHoldKey);
        Interpreted.SideStepCommand = motion;
        Interpreted.SideStepSpeed = speed;

        motion = Interpreted.TurnCommand;
        speed = Interpreted.TurnSpeed;
        AdjustMotion(ref motion, ref speed, Raw.TurnHoldKey);
        Interpreted.TurnCommand = motion;
        Interpreted.TurnSpeed = speed;

        ApplyInterpretedMovement(cancelMoveTo, disallowJump);
    }

    /// <summary>
    /// Retail <c>apply_interpreted_movement</c> (0x00528600 / 0x00529210): plays the interpreted state on the motion
    /// table without changing it.
    /// <list type="number">
    /// <item>Running records the forward speed as <see cref="MyRunRate"/>.</item>
    /// <item>The style.</item>
    /// <item>When contact allows the forward command: in a standing long jump, Ready and a stop of the sidestep;
    /// otherwise the forward command, then the sidestep or a stop of SideStepRight. Without contact: Falling.</item>
    /// <item>The turn, or a stop of TurnRight straight on the motion table (queuing Ready on success).</item>
    /// </list>
    /// Every motion goes with default parameters minus the hold key and the interpreted-state update, plus
    /// <paramref name="cancelMoveTo"/> and <paramref name="disallowJump"/>; the speed carries over from one call to the
    /// next as retail's single parameter block does.
    /// </summary>
    public void ApplyInterpretedMovement(bool cancelMoveTo, bool disallowJump)
    {
        var parameters = new MovementParameters();
        var flags = parameters.Flags & ~(MovementParameterFlags.SetHoldKey | MovementParameterFlags.ModifyInterpretedState
            | MovementParameterFlags.CancelMoveTo | MovementParameterFlags.DisableJumpDuringLink);
        if (cancelMoveTo)
        {
            flags |= MovementParameterFlags.CancelMoveTo;
        }

        if (disallowJump)
        {
            flags |= MovementParameterFlags.DisableJumpDuringLink;
        }

        parameters = parameters with { Flags = flags };
        if (Interpreted.ForwardCommand == MotionCommands.RunForward)
        {
            MyRunRate = Interpreted.ForwardSpeed;
        }

        DoInterpretedMotion(Interpreted.CurrentStyle, parameters);
        if (ContactAllowsMove(Interpreted.ForwardCommand))
        {
            if (StandingLongJump)
            {
                parameters = parameters with { Speed = 1f };
                DoInterpretedMotion(MotionCommands.Ready, parameters);
                StopInterpretedMotion(MotionCommands.SideStepRight, parameters);
            }
            else
            {
                parameters = parameters with { Speed = Interpreted.ForwardSpeed };
                DoInterpretedMotion(Interpreted.ForwardCommand, parameters);
                if (Interpreted.SideStepCommand != 0)
                {
                    parameters = parameters with { Speed = Interpreted.SideStepSpeed };
                    DoInterpretedMotion(Interpreted.SideStepCommand, parameters);
                }
                else
                {
                    StopInterpretedMotion(MotionCommands.SideStepRight, parameters);
                }
            }
        }
        else
        {
            parameters = parameters with { Speed = 1f };
            DoInterpretedMotion(MotionCommands.Falling, parameters);
        }

        if (Interpreted.TurnCommand != 0)
        {
            parameters = parameters with { Speed = Interpreted.TurnSpeed };
            DoInterpretedMotion(Interpreted.TurnCommand, parameters);
        }
        else
        {
            if (PartsStop(MotionCommands.TurnRight, parameters.Speed) == WeenieError.None)
            {
                AddToQueue(parameters.ContextId, MotionCommands.Ready, WeenieError.None);
                if (parameters.ModifyInterpretedState)
                {
                    Interpreted.RemoveMotion(MotionCommands.TurnRight);
                }
            }

            if (!Host.InCell)
            {
                RemoveLinkAnimations();
            }
        }
    }

    /// <summary>
    /// Retail <c>apply_current_movement</c> (0x00528870 / 0x00529480): once initialised, the raw state for the player
    /// (or an object without a weenie) whose movement is its own (<see cref="IMotionInterpHost.MovementIsAutonomous"/>),
    /// else the interpreted state.
    /// </summary>
    public void ApplyCurrentMovement(bool cancelMoveTo, bool disallowJump)
    {
        if (!Initted)
        {
            return;
        }

        var weenie = Host.Weenie;
        if ((weenie is null || weenie.IsThePlayer) && Host.MovementIsAutonomous)
        {
            ApplyRawMovement(cancelMoveTo, disallowJump);
        }
        else
        {
            ApplyInterpretedMovement(cancelMoveTo, disallowJump);
        }
    }

    /// <summary>
    /// Retail <c>ReportExhaustion</c> (0x005288D0 / 0x005294E0): re-applies the current movement after the run rate
    /// changed (out of stamina), allowing a jump and keeping a MoveTo.
    /// </summary>
    public void ReportExhaustion() => ApplyCurrentMovement(cancelMoveTo: false, disallowJump: false);

    /// <summary>
    /// Retail <c>SetWeenieObject</c> (0x00528920 / 0x00529530) after the weenie changed: once initialised, re-applies
    /// the current movement, cancelling a MoveTo.
    /// </summary>
    public void OnWeenieChanged() => ApplyCurrentMovement(cancelMoveTo: true, disallowJump: false);

    /// <summary>
    /// Retail <c>SetHoldKey</c> (0x00528BB0 / 0x005297C0): switches the run key to <see cref="HoldKey.None"/> or
    /// <see cref="HoldKey.Run"/> and re-applies the current movement when it changes. <see cref="HoldKey.Invalid"/> is
    /// ignored.
    /// </summary>
    public void SetHoldKey(HoldKey key, bool cancelMoveTo)
    {
        var current = Raw.CurrentHoldKey;
        if (key == current)
        {
            return;
        }

        if (key == HoldKey.None)
        {
            if (current == HoldKey.Run)
            {
                Raw.CurrentHoldKey = HoldKey.None;
                ApplyCurrentMovement(cancelMoveTo, disallowJump: false);
            }
        }
        else if (key == HoldKey.Run)
        {
            Raw.CurrentHoldKey = HoldKey.Run;
            ApplyCurrentMovement(cancelMoveTo, disallowJump: false);
        }
    }

    /// <summary>
    /// Retail <c>set_hold_run</c> (0x00528B70 / 0x00529780): holds or releases the run key, re-applying the current
    /// movement when that changes it.
    /// </summary>
    public void SetHoldRun(bool run, bool cancelMoveTo)
    {
        if (run != (Raw.CurrentHoldKey == HoldKey.Run))
        {
            Raw.CurrentHoldKey = run ? HoldKey.Run : HoldKey.None;
            ApplyCurrentMovement(cancelMoveTo, disallowJump: false);
        }
    }

    /// <summary>
    /// Retail <c>HitGround</c> (0x00528AC0 / 0x005296D0): landing. For a creature (or an object without a weenie)
    /// with gravity: drop the link animations and re-apply the current movement, so Falling gives way to the forward
    /// command.
    /// </summary>
    public void HitGround()
    {
        if (!IsCreatureOrNone() || !Host.HasGravity)
        {
            return;
        }

        RemoveLinkAnimations();
        ApplyCurrentMovement(cancelMoveTo: false, disallowJump: false);
    }

    /// <summary>
    /// Retail <c>LeaveGround</c> (0x00528B00 / 0x00529710): leaving the ground. For a creature (or an object without a
    /// weenie) with gravity: set the leave-ground velocity (<see cref="GetLeaveGroundVelocity"/>) in local axes, end the
    /// standing long jump, clear the jump extent, drop the link animations and re-apply the current movement (which
    /// plays Falling without contact).
    /// </summary>
    public void LeaveGround()
    {
        if (!IsCreatureOrNone() || !Host.HasGravity)
        {
            return;
        }

        Host.SetLocalVelocity(GetLeaveGroundVelocity());
        StandingLongJump = false;
        JumpExtent = 0f;
        RemoveLinkAnimations();
        ApplyCurrentMovement(cancelMoveTo: false, disallowJump: false);
    }

    /// <summary>
    /// Retail <c>charge_jump</c> (0x005281C0 / 0x00528DD0 predicted, TODO(avatars): 11.6096 confirm from the bytes;
    /// 11.6096 adds Sanctuary): the jump key went down. Refused (<see cref="WeenieError.CantJumpLoadedDown"/>) when the
    /// weenie cannot jump, or (<see cref="WeenieError.YouCantJumpFromThisPosition"/>) from Fallen, crouching, sitting,
    /// sleeping or Sanctuary; otherwise a standing long jump starts when the object stands still on walkable ground.
    /// </summary>
    public WeenieError ChargeJump()
    {
        var refusal = JumpChargeIsAllowed();
        if (refusal != WeenieError.None)
        {
            return refusal;
        }

        if (IsStandingStill())
        {
            StandingLongJump = true;
        }

        return WeenieError.None;
    }

    /// <summary>
    /// Retail <c>jump_charge_is_allowed</c> (0x00527A50; TODO(avatars): 11.6096, unmapped in the symbol table; 11.6096
    /// adds Sanctuary): <see cref="WeenieError.CantJumpLoadedDown"/> when the weenie cannot jump with the
    /// current extent; <see cref="WeenieError.YouCantJumpFromThisPosition"/> when the forward command is Fallen,
    /// Crouch, Sitting, Sleeping or Sanctuary.
    /// </summary>
    /// <remarks>
    /// The interpreted forward command is always a substate, so the Sanctuary comparison doc 13 reports for 11.6096 can
    /// never match here; it is kept as the document states it, pending a read of the bytes.
    /// </remarks>
    public WeenieError JumpChargeIsAllowed()
    {
        var weenie = Host.Weenie;
        if (weenie is not null && !weenie.CanJump(JumpExtent))
        {
            return WeenieError.CantJumpLoadedDown;
        }

        var forward = Interpreted.ForwardCommand;
        return forward == Fallen || forward is > 0x41000011 and <= Sleeping || forward == Sanctuary
            ? WeenieError.YouCantJumpFromThisPosition
            : WeenieError.None;
    }

    /// <summary>
    /// Retail <c>jump_is_allowed</c> (0x005282B0 / 0x00528EC0). In the air (a creature with gravity, without walkable
    /// contact): <see cref="WeenieError.YouCantJumpWhileInTheAir"/>. Otherwise, in order:
    /// <see cref="WeenieError.GeneralMovementFailure"/> when fully constrained; the oldest pending motion's jump error;
    /// <see cref="JumpChargeIsAllowed"/>; <see cref="MotionAllowsJump"/> of the forward command;
    /// <see cref="WeenieError.GeneralMovementFailure"/> when the weenie cannot pay the stamina.
    /// </summary>
    public WeenieError JumpIsAllowed(float extent, out int staminaCost)
    {
        staminaCost = 0;
        var weenie = Host.Weenie;
        if ((weenie is null || weenie.IsCreature) && Host.HasGravity && !(Host.HasContact && Host.OnWalkable))
        {
            return WeenieError.YouCantJumpWhileInTheAir;
        }

        if (Host.IsFullyConstrained)
        {
            return WeenieError.GeneralMovementFailure;
        }

        if (_pending.Count > 0 && _pending.Peek().JumpErrorCode != WeenieError.None)
        {
            return _pending.Peek().JumpErrorCode;
        }

        var result = JumpChargeIsAllowed();
        if (result != WeenieError.None)
        {
            return result;
        }

        result = MotionAllowsJump(Interpreted.ForwardCommand);
        if (result != WeenieError.None)
        {
            return result;
        }

        return weenie is not null && !weenie.TryGetJumpStaminaCost(extent, out staminaCost)
            ? WeenieError.GeneralMovementFailure
            : WeenieError.None;
    }

    /// <summary>
    /// Retail <c>jump</c> (0x00528780 / 0x00529390): cancels a MoveTo and asks <see cref="JumpIsAllowed"/>. Refused, the
    /// standing long jump ends. Allowed, the extent is kept and the object leaves walkable ground
    /// (<see cref="IMotionInterpHost.SetOnWalkable"/>(false), which brings <see cref="LeaveGround"/>).
    /// </summary>
    public WeenieError Jump(float extent, out int staminaCost)
    {
        Host.CancelMoveTo();
        var result = JumpIsAllowed(extent, out staminaCost);
        if (result != WeenieError.None)
        {
            StandingLongJump = false;
        }
        else
        {
            JumpExtent = extent;
            Host.SetOnWalkable(false);
        }

        return result;
    }

    /// <summary>
    /// Retail <c>motion_allows_jump</c> (0x005279E0 / 0x005285E0 predicted, TODO(avatars): 11.6096 confirm from the
    /// bytes), with the 11.6096 set (acclient-AI-RE doc 13 §5): <see cref="WeenieError.YouCantJumpFromThisPosition"/>
    /// for Fallen (0x40000008),
    /// 0x40000016-0x40000018, 0x4000001E-0x40000039, Crouch to Sleeping (0x41000012-0x41000014), MagicPowerUp01-10
    /// (0x1000006F-0x10000078), MagicPowerUp01-10Purple (0x1000012B-0x10000134), Sanctuary (0x10000057) and
    /// AI_TelegraphCast (0x1000019B); <see cref="WeenieError.None"/> for anything else.
    /// </summary>
    /// <remarks>
    /// 11.4186 blocks 0x10000128-0x10000131 instead of the Purple range (the 2013 numbering, three lower) and has neither
    /// Sanctuary nor AI_TelegraphCast. Falling (0x40000015) is not in either set.
    /// </remarks>
    public static WeenieError MotionAllowsJump(uint substate) =>
        substate == Fallen
        || substate is >= 0x40000016 and <= 0x40000018
        || substate is >= 0x4000001E and <= 0x40000039
        || substate is >= Crouch and <= Sleeping
        || substate is >= 0x1000006F and <= 0x10000078
        || substate is >= 0x1000012B and <= 0x10000134
        || substate == Sanctuary
        || substate == AiTelegraphCast
            ? WeenieError.YouCantJumpFromThisPosition
            : WeenieError.None;

    /// <summary>
    /// Retail <c>contact_allows_move</c> (0x00528240 / 0x00528E50): Falling, Dead and turning are always allowed, and so
    /// is anything for a non-creature or an object without gravity; otherwise the object needs walkable contact.
    /// </summary>
    public bool ContactAllowsMove(uint motion)
    {
        if (motion is MotionCommands.Falling or MotionCommands.Dead or MotionCommands.TurnRight or MotionCommands.TurnLeft)
        {
            return true;
        }

        var weenie = Host.Weenie;
        if (weenie is not null && !weenie.IsCreature)
        {
            return true;
        }

        return !Host.HasGravity || (Host.HasContact && Host.OnWalkable);
    }

    /// <summary>
    /// Retail <c>is_standing_still</c> (0x00527FA0 / 0x00528BB0): walkable contact, Ready, no sidestep and no turn.
    /// </summary>
    public bool IsStandingStill() =>
        Host.HasContact && Host.OnWalkable && Interpreted.ForwardCommand == MotionCommands.Ready
        && Interpreted.SideStepCommand == 0 && Interpreted.TurnCommand == 0;

    /// <summary>
    /// Retail <c>adjust_motion</c> (0x00528010 / 0x00528C20), for creatures (and objects without a weenie) only:
    /// TurnLeft becomes TurnRight at −speed, WalkBackwards becomes WalkForward at −0.65 × speed, SideStepLeft becomes
    /// SideStepRight at −speed, and every sidestep is scaled by 0.5 × 3.12 / 1.25 = 1.248. RunForward passes untouched.
    /// Then, when the hold key (<paramref name="holdKey"/>, or the current one when it is
    /// <see cref="HoldKey.Invalid"/>) is <see cref="HoldKey.Run"/>, <see cref="ApplyRunToCommand"/>.
    /// </summary>
    public void AdjustMotion(ref uint motion, ref float speed, HoldKey holdKey)
    {
        var weenie = Host.Weenie;
        if (weenie is not null && !weenie.IsCreature)
        {
            return;
        }

        switch (motion)
        {
            case MotionCommands.RunForward:
                return;
            case MotionCommands.TurnLeft:
                motion = MotionCommands.TurnRight;
                speed = (float)(speed * -1.0);
                break;
            case MotionCommands.WalkBackwards:
                motion = MotionCommands.WalkForward;
                speed = (float)(-(double)BackwardsFactor * speed);
                break;
            case MotionCommands.SideStepRight:
                speed = (float)(SidestepScale * speed);
                break;
            case MotionCommands.SideStepLeft:
                motion = MotionCommands.SideStepRight;
                speed = (float)(SidestepScale * (float)(speed * -1.0));
                break;
        }

        if (holdKey == HoldKey.Invalid)
        {
            holdKey = Raw.CurrentHoldKey;
        }

        if (holdKey == HoldKey.Run)
        {
            ApplyRunToCommand(ref motion, ref speed);
        }
    }

    /// <summary>
    /// Retail <c>apply_run_to_command</c> (0x00527BE0 / 0x005287F0), with the run rate (the weenie's, else
    /// <see cref="MyRunRate"/>; 1 without a weenie): WalkForward at a positive speed becomes RunForward, and the speed
    /// (backwards too) is multiplied by the run rate; TurnRight's speed by 1.5; SideStepRight's by the run rate, capped
    /// at ±3.
    /// </summary>
    public void ApplyRunToCommand(ref uint motion, ref float speed)
    {
        var rate = RunRate();
        switch (motion)
        {
            case MotionCommands.WalkForward:
                if (speed > 0f)
                {
                    motion = MotionCommands.RunForward;
                }

                speed = (float)(rate * speed);
                break;
            case MotionCommands.TurnRight:
                speed = (float)(RunTurnFactor * (double)speed);
                break;
            case MotionCommands.SideStepRight:
                var scaled = rate * speed;
                speed = (float)scaled;
                if (Math.Abs(scaled) > MaxSidestepAnimRate)
                {
                    speed = scaled <= 0.0 ? -MaxSidestepAnimRate : MaxSidestepAnimRate;
                }

                break;
        }
    }

    /// <summary>
    /// Retail <c>get_state_velocity</c> (0x00527D50 / 0x00528960): the velocity, in local axes, that the interpreted
    /// state moves the object at: x = 1.25 × sidestep speed for SideStepRight, y = 3.12 × forward speed walking or
    /// 4 × forward speed running, z = 0; scaled down to 4 × the run rate when longer.
    /// </summary>
    /// <remarks>
    /// A glitch fix (the documented default, docs/avatars.md §7): retail also applies a negative cap (a negative run
    /// rate, which <see cref="MyRunRate"/> takes unchecked from a server MoveTo or a RunForward speed), so a standing
    /// object's velocity is NaN (0 × 1/0) and a moving one's turns round to the cap's length. Lifestone leaves the
    /// velocity uncapped then; <see cref="RetailNegativeRunRateCap"/> restores retail. A cap of 0 is retail's either way
    /// (a zero velocity).
    /// </remarks>
    public Vector3 GetStateVelocity()
    {
        double x = Interpreted.SideStepCommand == MotionCommands.SideStepRight ? SidestepAnimSpeed * (double)Interpreted.SideStepSpeed : 0.0;
        double y = Interpreted.ForwardCommand switch
        {
            MotionCommands.WalkForward => WalkAnimSpeed * (double)Interpreted.ForwardSpeed,
            MotionCommands.RunForward => RunAnimSpeed * (double)Interpreted.ForwardSpeed,
            _ => 0.0,
        };
        var v = new Vector3((float)x, (float)y, 0f);
        var max = RunRate() * RunAnimSpeed;
        var length = Math.Sqrt(((double)v.X * v.X) + ((double)v.Y * v.Y) + ((double)v.Z * v.Z));
        if (length > max && (max >= 0.0 || RetailNegativeRunRateCap))
        {
            var scale = 1.0 / length;
            v = new Vector3((float)(scale * v.X * max), (float)(scale * v.Y * max), (float)(scale * v.Z * max));
        }

        return v;
    }

    /// <summary>
    /// Retail <c>get_jump_v_z</c> (0x00527AA0 / 0x005286B0): 0 below an extent of 0.0002; else the extent capped at 1,
    /// and 10 without a weenie, else the weenie's jump velocity (0 when it cannot tell).
    /// </summary>
    public float GetJumpVelocityZ()
    {
        var extent = JumpExtent;
        if (extent < Epsilon)
        {
            return 0f;
        }

        if (extent > 1f)
        {
            extent = 1f;
        }

        var weenie = Host.Weenie;
        if (weenie is null)
        {
            return 10f;
        }

        return weenie.TryGetJumpVelocity(extent, out var velocityZ) ? velocityZ : 0f;
    }

    /// <summary>
    /// Retail <c>get_leave_ground_velocity</c> (0x005280C0 / 0x00528CD0): <see cref="GetStateVelocity"/> with the jump's
    /// vertical speed (<see cref="GetJumpVelocityZ"/>); when all three are below 0.0002, the object's current velocity
    /// in local axes instead.
    /// </summary>
    public Vector3 GetLeaveGroundVelocity()
    {
        var v = GetStateVelocity();
        v.Z = GetJumpVelocityZ();
        if (MathF.Abs(v.X) < Epsilon && MathF.Abs(v.Y) < Epsilon && MathF.Abs(v.Z) < Epsilon)
        {
            v = Vector3.Transform(Host.Velocity, Quaternion.Conjugate(Host.Orientation));
        }

        return v;
    }

    /// <summary>
    /// Retail <c>get_max_speed</c> (0x00527CB0 / 0x005288C0): 4 × the run rate (the weenie's, else
    /// <see cref="MyRunRate"/>); 4 without a weenie.
    /// </summary>
    public float GetMaxSpeed() => (float)(RunRate() * RunAnimSpeed);

    /// <summary>
    /// Retail <c>get_adjusted_max_speed</c> (0x00527D00 / 0x00528910): like <see cref="GetMaxSpeed"/>, but while running
    /// the rate is the forward speed divided by <see cref="CurrentSpeedFactor"/>.
    /// </summary>
    public float GetAdjustedMaxSpeed()
    {
        var rate = RunRate();
        if (Interpreted.ForwardCommand == MotionCommands.RunForward)
        {
            rate = Interpreted.ForwardSpeed / CurrentSpeedFactor;
        }

        return (float)(rate * RunAnimSpeed);
    }

    /// <summary>
    /// Retail <c>HandleExitWorld</c> (0x00527F30; TODO(avatars): 11.6096): completes every pending motion as
    /// <see cref="MotionDone"/> would (an action leaves both states and ends a sticky follow).
    /// </summary>
    public void HandleExitWorld()
    {
        while (_pending.Count > 0)
        {
            MotionDone(success: false);
        }
    }

    /// <summary>
    /// Retail <c>MotionDone</c> (0x00527EC0 / 0x00528AD0): the oldest pending motion completed (whether it succeeded
    /// does not matter). When it is an action, the object stops sticking and both states drop their oldest action.
    /// Called through <see cref="MotionTableManager.MotionDone"/>.
    /// </summary>
    internal void MotionDone(bool success)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        if ((_pending.Peek().Motion & MotionCommands.ActionMask) != 0)
        {
            Host.UnstickFromObject();
            Interpreted.RemoveAction();
            Raw.RemoveAction();
        }

        _pending.Dequeue();
    }

    /// <summary>The sidestep scale of <c>adjust_motion</c>, computed in double as retail's x87 code does.</summary>
    private const double SidestepScale = (double)WalkAnimSpeed / SidestepAnimSpeed * SidestepFactor;

    private static bool IsLongJumpLocomotion(uint motion) =>
        motion is MotionCommands.WalkForward or MotionCommands.RunForward or MotionCommands.SideStepRight;

    private bool IsCreatureOrNone() => Host.Weenie is not { } weenie || weenie.IsCreature;

    /// <summary>The run rate retail's speed functions use: the weenie's, else <see cref="MyRunRate"/>; 1 without one.</summary>
    private double RunRate()
    {
        var weenie = Host.Weenie;
        if (weenie is null)
        {
            return 1.0;
        }

        return weenie.TryGetRunRate(out var rate) ? rate : MyRunRate;
    }

    /// <summary>Retail <c>add_to_queue</c> (0x00527B80 / 0x00528790).</summary>
    private void AddToQueue(uint contextId, uint motion, WeenieError jumpErrorCode) =>
        _pending.Enqueue(new PendingMotion(contextId, motion, jumpErrorCode));

    private WeenieError PartsDo(uint motion, float speed) => _parts?.DoInterpretedMotion(motion, speed) ?? WeenieError.NoAnimationTable;

    private WeenieError PartsStop(uint motion, float speed) => _parts?.StopInterpretedMotion(motion, speed) ?? WeenieError.NoAnimationTable;

    private void RemoveLinkAnimations() => _parts?.RemoveLinkAnimations();
}

/// <summary>
/// The part-array calls <c>CMotionInterp</c> makes through <c>CPhysicsObj</c> (retail <c>CPartArray</c>). The motion-table
/// player is the product implementation; tests record the calls.
/// </summary>
internal interface IMotionParts
{
    /// <summary><c>CPartArray::DoInterpretedMotion</c>.</summary>
    WeenieError DoInterpretedMotion(uint motion, float speed);

    /// <summary><c>CPartArray::StopInterpretedMotion</c>.</summary>
    WeenieError StopInterpretedMotion(uint motion, float speed);

    /// <summary><c>CPartArray::StopCompletelyInternal</c>.</summary>
    void StopCompletelyInternal();

    /// <summary><c>CPartArray::InitializeMotionTables</c>.</summary>
    void InitializeMotionTables();

    /// <summary><c>CPartArray::CheckForCompletedMotions</c>.</summary>
    void CheckForCompletedMotions();

    /// <summary><c>CPhysicsObj::RemoveLinkAnimations</c>.</summary>
    void RemoveLinkAnimations();
}

/// <summary>The <see cref="MotionPlayer"/> as the interpreter's part array (failures are retail's error 0x43).</summary>
internal sealed class MotionPlayerParts(MotionPlayer player) : IMotionParts
{
    public WeenieError DoInterpretedMotion(uint motion, float speed) =>
        player.DoInterpretedMotion(motion, speed) ? WeenieError.None : WeenieError.NoMtableData;

    public WeenieError StopInterpretedMotion(uint motion, float speed) =>
        player.StopInterpretedMotion(motion, speed) ? WeenieError.None : WeenieError.NoMtableData;

    public void StopCompletelyInternal() => player.StopCompletelyInternal();

    public void InitializeMotionTables() => player.InitializeMotionTables();

    public void CheckForCompletedMotions() => player.CheckForCompletedMotions();

    public void RemoveLinkAnimations() => player.RemoveLinkAnimations();
}

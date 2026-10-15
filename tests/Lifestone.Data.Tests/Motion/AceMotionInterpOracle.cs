// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Animation/MotionInterp.cs, RawMotionState.cs,
// InterpretedMotionState.cs, MovementParameters.cs and MovementSystem.cs (AGPL-3.0) [with changes: test-only oracle;
// reduced to the interpreted and raw motion paths, the speed functions, the jump rules and MovementSystem; the PhysicsObj
// and WeenieObject it calls are a recording stand-in (Body); MotionCommand enum values written as numbers; motion_allows_jump
// is a replaceable delegate (default ACE's) so a test can isolate the other differences].

using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;

namespace Lifestone.Data.Tests.Motion;

/// <summary>
/// An independent oracle for the motion interpreter: ACE's server-side MotionInterp, which follows retail
/// <c>CMotionInterp</c> closely. Tests drive it and <see cref="MotionInterpreter"/> with the same scripts and compare the
/// part-array calls and the resulting states; where ACE differs from retail the tests assert the difference.
/// </summary>
internal sealed class AceMotionInterpOracle
{
    // ACE MotionCommand values (11.6096 numbering).
    private const uint Ready = 0x41000003;
    private const uint WalkForward = 0x45000005;
    private const uint WalkBackwards = 0x45000006;
    private const uint RunForward = 0x44000007;
    private const uint TurnRight = 0x6500000D;
    private const uint TurnLeft = 0x6500000E;
    private const uint SideStepRight = 0x6500000F;
    private const uint SideStepLeft = 0x65000010;
    private const uint Dead = 0x40000011;
    private const uint Falling = 0x40000015;
    private const uint Fallen = 0x40000008;
    private const uint NonCombat = 0x8000003D;
    private const uint Crouch = 0x41000012;
    private const uint Sitting = 0x41000013;
    private const uint Sleeping = 0x41000014;
    private const uint Reload = 0x40000016;
    private const uint Pickup = 0x40000018;
    private const uint AimLevel = 0x4000001E;
    private const uint MagicPray = 0x40000039;
    private const uint MagicPowerUp01 = 0x1000006F;
    private const uint MagicPowerUp10 = 0x10000078;
    private const uint TripleThrustLow = 0x10000128;
    private const uint MagicPowerUp07Purple = 0x10000131;
    private const uint ActionMask = 0x10000000;
    private const uint SubStateMask = 0x40000000;
    private const uint StyleMask = 0x80000000;
    private const uint ChatEmoteMask = 0x02000000;

    public const float BackwardsFactor = 6.4999998e-1f;
    public const float MaxSidestepAnimRate = 3.0f;
    public const float RunAnimSpeed = 4.0f;
    public const float RunTurnFactor = 1.5f;
    public const float SidestepAnimSpeed = 1.25f;
    public const float SidestepFactor = 0.5f;
    public const float WalkAnimSpeed = 3.1199999f;

    public AceMotionInterpOracle(Body body)
    {
        PhysicsObj = body;
        CurrentSpeedFactor = 1.0f;
        MyRunRate = 1.0f;
        PendingMotions = new LinkedList<MotionNode>();
    }

    public Body PhysicsObj { get; }

    public bool Initted;
    public RawMotion RawState = new();
    public InterpMotion InterpretedState = new();
    public float CurrentSpeedFactor;
    public bool StandingLongJump;
    public float JumpExtent;
    public int ServerActionStamp;
    public float MyRunRate;
    public LinkedList<MotionNode> PendingMotions;

    /// <summary>motion_allows_jump; ACE's by default.</summary>
    public Func<uint, WeenieError> MotionAllowsJump { get; set; } = AceMotionAllowsJump;

    private Body? WeenieObj => PhysicsObj.HasWeenie ? PhysicsObj : null;

    /// <summary>Stands in for ACE's PhysicsObj (and its WeenieObject) and records the part-array calls.</summary>
    internal sealed class Body
    {
        public bool Contact = true;
        public bool OnWalkable = true;
        public bool Gravity = true;
        public bool InCell = true;
        public bool Autonomous;
        public bool FullyConstrained;
        public Vector3 Velocity;
        public Quaternion Orientation = Quaternion.Identity;
        public bool HasWeenie = true;
        public bool IsCreature = true;
        public float? RunRate;
        public float? JumpVelocity;
        public bool CanJumpResult = true;
        public bool StaminaOk = true;
        public HashSet<uint> Refuse = [];
        public List<PartsCall> Calls = [];
        public AceMotionInterpOracle? Interp;

        public WeenieError DoInterpretedMotion(uint motion, Params p)
        {
            Calls.Add(new PartsCall("do", motion, p.Speed));
            return Refuse.Contains(motion) ? WeenieError.NoMtableData : WeenieError.None;
        }

        public WeenieError StopInterpretedMotion(uint motion, Params p)
        {
            Calls.Add(new PartsCall("stop", motion, p.Speed));
            return Refuse.Contains(motion) ? WeenieError.NoMtableData : WeenieError.None;
        }

        public void StopCompletely_Internal() => Calls.Add(new PartsCall("stopall", 0, 0));

        public void InitializeMotionTables() => Calls.Add(new PartsCall("init", 0, 0));

        public void RemoveLinkAnimations()
        {
            Calls.Add(new PartsCall("unlink", 0, 0));
            while (Interp!.PendingMotions.Count > 0)
            {
                Interp.MotionDone(false);
            }
        }

        public void cancel_moveto()
        {
        }

        public void unstick_from_object()
        {
        }

        public bool InqRunRate(ref float rate)
        {
            if (RunRate is { } r)
            {
                rate = r;
                return true;
            }

            return false;
        }

        public bool InqJumpVelocity(float extent, out float vz)
        {
            vz = JumpVelocity ?? 0f;
            return JumpVelocity.HasValue;
        }

        public void set_local_velocity(Vector3 v) => Velocity = Vector3.Transform(v, Orientation);
    }

    internal sealed record MotionNode(int ContextID, uint Motion, WeenieError JumpErrorCode);

    internal sealed class Params
    {
        public bool SetHoldKey = true;
        public bool Autonomous;
        public bool ModifyRawState = true;
        public bool ModifyInterpretedState = true;
        public bool CancelMoveTo = true;
        public bool DisableJumpDuringLink;
        public float Speed = 1.0f;
        public int ContextID;
        public HoldKey HoldKeyToApply = HoldKey.Invalid;
        public int ActionStamp;

        public void CopySome(Params mvp)
        {
            SetHoldKey = mvp.SetHoldKey;
            ModifyRawState = mvp.ModifyRawState;
            ModifyInterpretedState = mvp.ModifyInterpretedState;
            CancelMoveTo = mvp.CancelMoveTo;
            Speed = mvp.Speed;
            HoldKeyToApply = mvp.HoldKeyToApply;
        }
    }

    internal sealed record ActionNode(uint Action, float Speed, int Stamp, bool Autonomous);

    internal sealed class RawMotion
    {
        public List<ActionNode> Actions = [];
        public HoldKey CurrentHoldKey = HoldKey.None;
        public uint CurrentStyle = NonCombat;
        public uint ForwardCommand = Ready;
        public HoldKey ForwardHoldKey = HoldKey.Invalid;
        public float ForwardSpeed = 1.0f;
        public uint SideStepCommand;
        public HoldKey SideStepHoldKey = HoldKey.Invalid;
        public float SideStepSpeed = 1.0f;
        public uint TurnCommand;
        public HoldKey TurnHoldKey = HoldKey.Invalid;
        public float TurnSpeed = 1.0f;

        public void ApplyMotion(uint motion, Params movementParams)
        {
            switch (motion)
            {
                case TurnRight:
                case TurnLeft:
                    TurnCommand = motion;
                    TurnHoldKey = movementParams.SetHoldKey ? HoldKey.Invalid : movementParams.HoldKeyToApply;
                    TurnSpeed = movementParams.Speed;
                    break;
                case SideStepRight:
                case SideStepLeft:
                    SideStepCommand = motion;
                    SideStepHoldKey = movementParams.SetHoldKey ? HoldKey.Invalid : movementParams.HoldKeyToApply;
                    SideStepSpeed = movementParams.Speed;
                    break;
                default:
                    if ((motion & SubStateMask) != 0)
                    {
                        if (motion != RunForward)
                        {
                            ForwardCommand = motion;
                            ForwardHoldKey = movementParams.SetHoldKey ? HoldKey.Invalid : movementParams.HoldKeyToApply;
                            ForwardSpeed = movementParams.Speed;
                        }
                    }
                    else if ((motion & StyleMask) != 0)
                    {
                        if (CurrentStyle != motion)
                        {
                            ForwardCommand = Ready;
                            CurrentStyle = motion;
                        }
                    }
                    else if ((motion & ActionMask) != 0)
                    {
                        Actions.Add(new ActionNode(motion, movementParams.Speed, movementParams.ActionStamp, movementParams.Autonomous));
                    }

                    break;
            }
        }

        public uint RemoveAction()
        {
            if (Actions.Count == 0)
            {
                return 0;
            }

            var action = Actions[0];
            Actions.RemoveAt(0);
            return action.Action;
        }

        public void RemoveMotion(uint motion)
        {
            switch (motion)
            {
                case TurnRight:
                case TurnLeft:
                    TurnCommand = 0;
                    break;
                case SideStepRight:
                case SideStepLeft:
                    SideStepCommand = 0;
                    break;
                default:
                    if ((motion & SubStateMask) != 0)
                    {
                        if (ForwardCommand == motion)
                        {
                            ForwardCommand = Ready;
                            ForwardSpeed = 1.0f;
                        }
                    }
                    else if ((motion & StyleMask) != 0)
                    {
                        if (CurrentStyle == motion)
                        {
                            CurrentStyle = NonCombat;
                        }
                    }

                    break;
            }
        }
    }

    internal sealed class InterpMotion
    {
        public uint CurrentStyle = NonCombat;
        public uint ForwardCommand = Ready;
        public float ForwardSpeed = 1.0f;
        public uint SideStepCommand;
        public float SideStepSpeed = 1.0f;
        public uint TurnCommand;
        public float TurnSpeed = 1.0f;
        public List<ActionNode> Actions = [];

        public void ApplyMotion(uint motion, Params movementParams)
        {
            switch (motion)
            {
                case TurnRight:
                    TurnCommand = TurnRight;
                    TurnSpeed = movementParams.Speed;
                    break;
                case SideStepRight:
                    SideStepCommand = SideStepRight;
                    SideStepSpeed = movementParams.Speed;
                    break;
                default:
                    if ((motion & SubStateMask) != 0)
                    {
                        ForwardCommand = motion;
                        ForwardSpeed = movementParams.Speed;
                    }
                    else if ((motion & StyleMask) != 0)
                    {
                        ForwardCommand = Ready;
                        CurrentStyle = motion;
                    }
                    else if ((motion & ActionMask) != 0)
                    {
                        Actions.Add(new ActionNode(motion, movementParams.Speed, movementParams.ActionStamp, movementParams.Autonomous));
                    }

                    break;
            }
        }

        public void copy_movement_from(InterpMotion state)
        {
            CurrentStyle = state.CurrentStyle;
            ForwardCommand = state.ForwardCommand;
            ForwardSpeed = state.ForwardSpeed;
            SideStepCommand = state.SideStepCommand;
            SideStepSpeed = state.SideStepSpeed;
            TurnCommand = state.TurnCommand;
            TurnSpeed = state.TurnSpeed;
        }

        public uint RemoveAction()
        {
            if (Actions.Count == 0)
            {
                return 0;
            }

            var action = Actions[0];
            Actions.RemoveAt(0);
            return action.Action;
        }

        public void RemoveMotion(uint motion)
        {
            switch (motion)
            {
                case TurnRight:
                    TurnCommand = 0;
                    break;
                case SideStepRight:
                    SideStepCommand = 0;
                    break;
                default:
                    if ((motion & SubStateMask) != 0)
                    {
                        if (ForwardCommand == motion)
                        {
                            ForwardCommand = Ready;
                            ForwardSpeed = 1.0f;
                        }
                    }
                    else if ((motion & StyleMask) != 0)
                    {
                        if (CurrentStyle == motion)
                        {
                            CurrentStyle = NonCombat;
                        }
                    }

                    break;
            }
        }
    }

    public WeenieError DoInterpretedMotion(uint motion, Params movementParams)
    {
        var result = WeenieError.None;

        if (contact_allows_move(motion))
        {
            if (StandingLongJump && (motion == WalkForward || motion == RunForward || motion == SideStepRight))
            {
                if (movementParams.ModifyInterpretedState)
                {
                    InterpretedState.ApplyMotion(motion, movementParams);
                }
            }
            else
            {
                if (motion == Dead)
                {
                    PhysicsObj.RemoveLinkAnimations();
                }

                result = PhysicsObj.DoInterpretedMotion(motion, movementParams);

                if (result == WeenieError.None)
                {
                    WeenieError jump_error_code;

                    if (movementParams.DisableJumpDuringLink)
                    {
                        jump_error_code = WeenieError.YouCantJumpFromThisPosition;
                    }
                    else
                    {
                        jump_error_code = MotionAllowsJump(motion);

                        if (jump_error_code == WeenieError.None && (motion & ActionMask) == 0)
                        {
                            jump_error_code = MotionAllowsJump(InterpretedState.ForwardCommand);
                        }
                    }

                    add_to_queue(movementParams.ContextID, motion, jump_error_code);

                    if (movementParams.ModifyInterpretedState)
                    {
                        InterpretedState.ApplyMotion(motion, movementParams);
                    }
                }
            }
        }
        else
        {
            if ((motion & ActionMask) != 0)
            {
                result = WeenieError.YouCantJumpWhileInTheAir;
            }
            else
            {
                if (movementParams.ModifyInterpretedState)
                {
                    InterpretedState.ApplyMotion(motion, movementParams);
                }

                result = WeenieError.None;
            }
        }

        if (!PhysicsObj.InCell)
        {
            PhysicsObj.RemoveLinkAnimations();
        }

        return result;
    }

    public WeenieError DoMotion(uint motion, Params movementParams)
    {
        var currentParams = new Params();
        currentParams.CopySome(movementParams);

        var currentMotion = motion;

        if (movementParams.CancelMoveTo)
        {
            PhysicsObj.cancel_moveto();
        }

        if (movementParams.SetHoldKey)
        {
            SetHoldKey(movementParams.HoldKeyToApply, movementParams.CancelMoveTo);
        }

        adjust_motion(ref currentMotion, ref currentParams.Speed, movementParams.HoldKeyToApply);

        if (InterpretedState.CurrentStyle != NonCombat)
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

            if ((motion & ChatEmoteMask) != 0)
            {
                return WeenieError.CantChatEmoteInCombat;
            }
        }

        if ((motion & ActionMask) != 0)
        {
            if (InterpretedState.Actions.Count >= 6)
            {
                return WeenieError.TooManyActions;
            }
        }

        var result = DoInterpretedMotion(currentMotion, currentParams);

        if (result == WeenieError.None && movementParams.ModifyRawState)
        {
            RawState.ApplyMotion(motion, movementParams);
        }

        return result;
    }

    public void HitGround()
    {
        if (WeenieObj != null && !WeenieObj.IsCreature)
        {
            return;
        }

        if (!PhysicsObj.Gravity)
        {
            return;
        }

        PhysicsObj.RemoveLinkAnimations();
        apply_current_movement(false, true);
    }

    public void LeaveGround()
    {
        if (WeenieObj != null && !WeenieObj.IsCreature)
        {
            return;
        }

        if (!PhysicsObj.Gravity)
        {
            return;
        }

        var velocity = get_leave_ground_velocity();
        PhysicsObj.set_local_velocity(velocity);

        StandingLongJump = false;
        JumpExtent = 0;

        PhysicsObj.RemoveLinkAnimations();
        apply_current_movement(false, true);
    }

    public void MotionDone(bool success)
    {
        var motionData = PendingMotions.First;
        if (motionData != null)
        {
            var pendingMotion = motionData.Value;
            if ((pendingMotion.Motion & ActionMask) != 0)
            {
                PhysicsObj.unstick_from_object();
                InterpretedState.RemoveAction();
                RawState.RemoveAction();
            }

            motionData = PendingMotions.First;
            if (motionData != null)
            {
                PendingMotions.Remove(motionData);
            }
        }
    }

    public void SetHoldKey(HoldKey key, bool cancelMoveTo)
    {
        if (key == RawState.CurrentHoldKey)
        {
            return;
        }

        switch (key)
        {
            case HoldKey.None:
                if (RawState.CurrentHoldKey == HoldKey.Run)
                {
                    RawState.CurrentHoldKey = HoldKey.None;
                    apply_current_movement(cancelMoveTo, true);
                }

                break;
        }
    }

    public WeenieError StopCompletely()
    {
        PhysicsObj.cancel_moveto();

        var jump = MotionAllowsJump(InterpretedState.ForwardCommand);

        RawState.ForwardCommand = Ready;
        RawState.ForwardSpeed = 1.0f;
        RawState.SideStepCommand = 0;
        RawState.TurnCommand = 0;

        InterpretedState.ForwardCommand = Ready;
        InterpretedState.ForwardSpeed = 1.0f;
        InterpretedState.SideStepCommand = 0;
        InterpretedState.TurnCommand = 0;

        PhysicsObj.StopCompletely_Internal();

        add_to_queue(0, Ready, jump);

        if (!PhysicsObj.InCell)
        {
            PhysicsObj.RemoveLinkAnimations();
        }

        return WeenieError.None;
    }

    public WeenieError StopInterpretedMotion(uint motion, Params movementParams)
    {
        var result = WeenieError.None;

        if (contact_allows_move(motion))
        {
            if (StandingLongJump && (motion == WalkForward || motion == RunForward || motion == SideStepRight))
            {
                if (movementParams.ModifyInterpretedState)
                {
                    InterpretedState.RemoveMotion(motion);
                }
            }
            else
            {
                result = PhysicsObj.StopInterpretedMotion(motion, movementParams);

                if (result == WeenieError.None)
                {
                    add_to_queue(movementParams.ContextID, Ready, WeenieError.None);

                    if (movementParams.ModifyInterpretedState)
                    {
                        InterpretedState.RemoveMotion(motion);
                    }
                }
            }
        }
        else
        {
            if (movementParams.ModifyInterpretedState)
            {
                InterpretedState.RemoveMotion(motion);
            }
        }

        if (!PhysicsObj.InCell)
        {
            PhysicsObj.RemoveLinkAnimations();
        }

        return result;
    }

    public WeenieError StopMotion(uint motion, Params movementParams)
    {
        if (movementParams.CancelMoveTo)
        {
            PhysicsObj.cancel_moveto();
        }

        var currentMotion = motion;
        var currentParams = new Params();
        currentParams.CopySome(movementParams);

        adjust_motion(ref currentMotion, ref currentParams.Speed, movementParams.HoldKeyToApply);

        var result = StopInterpretedMotion(currentMotion, currentParams);

        if (result == WeenieError.None && movementParams.ModifyRawState)
        {
            RawState.RemoveMotion(motion);
        }

        return result;
    }

    public void add_to_queue(int contextID, uint motion, WeenieError jumpErrorCode) =>
        PendingMotions.AddLast(new MotionNode(contextID, motion, jumpErrorCode));

    public void adjust_motion(ref uint motion, ref float speed, HoldKey holdKey)
    {
        if (WeenieObj != null && !WeenieObj.IsCreature)
        {
            return;
        }

        switch (motion)
        {
            case RunForward:
                return;

            case WalkBackwards:
                motion = WalkForward;
                speed *= -BackwardsFactor;
                break;

            case TurnLeft:
                motion = TurnRight;
                speed *= -1.0f;
                break;

            case SideStepLeft:
                motion = SideStepRight;
                speed *= -1.0f;
                break;
        }

        if (motion == SideStepRight)
        {
            speed *= SidestepFactor * (WalkAnimSpeed / SidestepAnimSpeed);
        }

        if (holdKey == HoldKey.Invalid)
        {
            holdKey = RawState.CurrentHoldKey;
        }

        if (holdKey == HoldKey.Run)
        {
            apply_run_to_command(ref motion, ref speed);
        }
    }

    public void apply_current_movement(bool cancelMoveTo, bool allowJump)
    {
        if (!Initted)
        {
            return;
        }

        if ((WeenieObj != null && !WeenieObj.IsCreature) || !PhysicsObj.Autonomous)
        {
            apply_interpreted_movement(cancelMoveTo, allowJump);
        }
        else
        {
            apply_raw_movement(cancelMoveTo, allowJump);
        }
    }

    public void apply_interpreted_movement(bool cancelMoveTo, bool allowJump)
    {
        var movementParams = new Params
        {
            SetHoldKey = false,
            ModifyInterpretedState = false,
            CancelMoveTo = cancelMoveTo,
            DisableJumpDuringLink = !allowJump,
        };

        if (InterpretedState.ForwardCommand == RunForward)
        {
            MyRunRate = InterpretedState.ForwardSpeed;
        }

        DoInterpretedMotion(InterpretedState.CurrentStyle, movementParams);

        if (contact_allows_move(InterpretedState.ForwardCommand))
        {
            if (!StandingLongJump)
            {
                movementParams.Speed = InterpretedState.ForwardSpeed;
                DoInterpretedMotion(InterpretedState.ForwardCommand, movementParams);

                if (InterpretedState.SideStepCommand != 0)
                {
                    movementParams.Speed = InterpretedState.SideStepSpeed;
                    DoInterpretedMotion(InterpretedState.SideStepCommand, movementParams);
                }
                else
                {
                    StopInterpretedMotion(SideStepRight, movementParams);
                }
            }
            else
            {
                movementParams.Speed = 1.0f;
                DoInterpretedMotion(Ready, movementParams);
                StopInterpretedMotion(SideStepRight, movementParams);
            }
        }
        else
        {
            movementParams.Speed = 1.0f;
            DoInterpretedMotion(Falling, movementParams);
        }

        if (InterpretedState.TurnCommand != 0)
        {
            movementParams.Speed = InterpretedState.TurnSpeed;
            DoInterpretedMotion(InterpretedState.TurnCommand, movementParams);
        }
        else
        {
            var result = PhysicsObj.StopInterpretedMotion(TurnRight, movementParams);

            if (result == WeenieError.None)
            {
                add_to_queue(movementParams.ContextID, Ready, WeenieError.None);

                if (movementParams.ModifyInterpretedState)
                {
                    InterpretedState.RemoveMotion(TurnRight);
                }
            }

            if (!PhysicsObj.InCell)
            {
                PhysicsObj.RemoveLinkAnimations();
            }
        }
    }

    public void apply_raw_movement(bool cancelMoveTo, bool allowJump)
    {
        InterpretedState.CurrentStyle = RawState.CurrentStyle;
        InterpretedState.ForwardCommand = RawState.ForwardCommand;
        InterpretedState.ForwardSpeed = RawState.ForwardSpeed;
        InterpretedState.SideStepCommand = RawState.SideStepCommand;
        InterpretedState.SideStepSpeed = RawState.SideStepSpeed;
        InterpretedState.TurnCommand = RawState.TurnCommand;
        InterpretedState.TurnSpeed = RawState.TurnSpeed;

        adjust_motion(ref InterpretedState.ForwardCommand, ref InterpretedState.ForwardSpeed, RawState.ForwardHoldKey);
        adjust_motion(ref InterpretedState.SideStepCommand, ref InterpretedState.SideStepSpeed, RawState.SideStepHoldKey);
        adjust_motion(ref InterpretedState.TurnCommand, ref InterpretedState.TurnSpeed, RawState.TurnHoldKey);

        apply_interpreted_movement(cancelMoveTo, allowJump);
    }

    public void apply_run_to_command(ref uint motion, ref float speed)
    {
        var speedMod = 1.0f;

        if (WeenieObj != null)
        {
            var runFactor = 0.0f;
            if (WeenieObj.InqRunRate(ref runFactor))
            {
                speedMod = runFactor;
            }
            else
            {
                speedMod = MyRunRate;
            }
        }

        switch (motion)
        {
            case WalkForward:
                if (speed > 0.0f)
                {
                    motion = RunForward;
                }

                speed *= speedMod;
                break;

            case TurnRight:
                speed *= RunTurnFactor;
                break;

            case SideStepRight:
                speed *= speedMod;

                if (MaxSidestepAnimRate < Math.Abs(speed))
                {
                    if (speed > 0.0f)
                    {
                        speed = MaxSidestepAnimRate * 1.0f;
                    }
                    else
                    {
                        speed = MaxSidestepAnimRate * -1.0f;
                    }
                }

                break;
        }
    }

    public int charge_jump()
    {
        if (WeenieObj != null && !WeenieObj.CanJumpResult)
        {
            return 0x49;
        }

        var forward = InterpretedState.ForwardCommand;

        if (forward == Falling || (forward >= Crouch && forward < Sleeping))
        {
            return 0x48;
        }

        if (PhysicsObj.Contact && PhysicsObj.OnWalkable && forward == Ready && InterpretedState.SideStepCommand == 0 && InterpretedState.TurnCommand == 0)
        {
            StandingLongJump = true;
        }

        return 0;
    }

    public bool contact_allows_move(uint motion)
    {
        if (motion == Dead || motion == Falling || (motion >= TurnRight && motion <= TurnLeft))
        {
            return true;
        }

        if (WeenieObj != null && !WeenieObj.IsCreature)
        {
            return true;
        }

        if (!PhysicsObj.Gravity)
        {
            return true;
        }

        if (!PhysicsObj.Contact)
        {
            return false;
        }

        return PhysicsObj.OnWalkable;
    }

    public void enter_default_state()
    {
        RawState = new RawMotion();
        InterpretedState = new InterpMotion();

        PhysicsObj.InitializeMotionTables();
        PendingMotions = new LinkedList<MotionNode>();

        add_to_queue(0, Ready, 0);

        Initted = true;
        LeaveGround();
    }

    public float get_adjusted_max_speed()
    {
        var rate = 1.0f;

        if (WeenieObj != null)
        {
            if (!WeenieObj.InqRunRate(ref rate))
            {
                rate = MyRunRate;
            }
        }

        if (InterpretedState.ForwardCommand == RunForward)
        {
            rate = InterpretedState.ForwardSpeed / CurrentSpeedFactor;
        }

        return rate * 4.0f;
    }

    public float get_jump_v_z()
    {
        if (JumpExtent < 0.0002f)
        {
            return 0.0f;
        }

        var extent = JumpExtent;

        if (extent > 1.0f)
        {
            extent = 1.0f;
        }

        if (WeenieObj == null)
        {
            return 10.0f;
        }

        return WeenieObj.InqJumpVelocity(extent, out var vz) ? vz : 0.0f;
    }

    public Vector3 get_leave_ground_velocity()
    {
        var velocity = get_state_velocity();
        velocity.Z = get_jump_v_z();

        if (Math.Abs(velocity.X) < 0.0002f && Math.Abs(velocity.Y) < 0.0002f && Math.Abs(velocity.Z) < 0.0002f)
        {
            velocity = Vector3.Transform(PhysicsObj.Velocity, Quaternion.Conjugate(PhysicsObj.Orientation));
        }

        return velocity;
    }

    public float get_max_speed()
    {
        var rate = 1.0f;

        if (WeenieObj != null)
        {
            if (!WeenieObj.InqRunRate(ref rate))
            {
                rate = MyRunRate;
            }
        }

        return RunAnimSpeed * rate;
    }

    public Vector3 get_state_velocity()
    {
        var velocity = Vector3.Zero;

        if (InterpretedState.SideStepCommand == SideStepRight)
        {
            velocity.X = SidestepAnimSpeed * InterpretedState.SideStepSpeed;
        }

        if (InterpretedState.ForwardCommand == WalkForward)
        {
            velocity.Y = WalkAnimSpeed * InterpretedState.ForwardSpeed;
        }
        else if (InterpretedState.ForwardCommand == RunForward)
        {
            velocity.Y = RunAnimSpeed * InterpretedState.ForwardSpeed;
        }

        var rate = MyRunRate;

        WeenieObj?.InqRunRate(ref rate);

        var maxSpeed = RunAnimSpeed * rate;
        if (velocity.Length() > maxSpeed)
        {
            velocity = Vector3.Normalize(velocity);
            velocity *= maxSpeed;
        }

        return velocity;
    }

    public WeenieError jump_charge_is_allowed()
    {
        if (WeenieObj != null && !WeenieObj.CanJumpResult)
        {
            return WeenieError.CantJumpLoadedDown;
        }

        var forward = InterpretedState.ForwardCommand;

        if (forward == Fallen || (forward >= Crouch && forward <= Sleeping))
        {
            return WeenieError.YouCantJumpFromThisPosition;
        }

        return WeenieError.None;
    }

    public WeenieError jump_is_allowed(float extent)
    {
        if (WeenieObj == null || !WeenieObj.IsCreature || !PhysicsObj.Gravity || (PhysicsObj.Contact && PhysicsObj.OnWalkable))
        {
            if (PhysicsObj.FullyConstrained)
            {
                return WeenieError.GeneralMovementFailure;
            }

            if (PendingMotions.Count > 1 && PendingMotions.First!.Value.JumpErrorCode != 0)
            {
                return PendingMotions.First.Value.JumpErrorCode;
            }

            var jumpError = jump_charge_is_allowed();

            if (jumpError == WeenieError.None)
            {
                jumpError = MotionAllowsJump(InterpretedState.ForwardCommand);

                if (jumpError == WeenieError.None && WeenieObj != null && !WeenieObj.StaminaOk)
                {
                    jumpError = WeenieError.GeneralMovementFailure;
                }
            }

            return jumpError;
        }

        return WeenieError.YouCantJumpWhileInTheAir;
    }

    public static WeenieError AceMotionAllowsJump(uint substate)
    {
        if ((substate >= Reload && substate <= Pickup) || (substate >= TripleThrustLow && substate <= MagicPowerUp07Purple)
            || (substate >= MagicPowerUp01 && substate <= MagicPowerUp10) || (substate >= Crouch && substate <= Sleeping)
            || (substate >= AimLevel && substate <= MagicPray) || substate == Falling)
        {
            return WeenieError.YouCantJumpFromThisPosition;
        }

        return WeenieError.None;
    }

    public bool move_to_interpreted_state(InterpMotion state)
    {
        RawState.CurrentStyle = state.CurrentStyle;
        PhysicsObj.cancel_moveto();

        var allowJump = MotionAllowsJump(InterpretedState.ForwardCommand) == WeenieError.None;

        InterpretedState.copy_movement_from(state);
        apply_current_movement(true, allowJump);

        var movementParams = new Params();

        foreach (var action in state.Actions)
        {
            var currentStamp = action.Stamp & 0x7FFF;
            var serverStamp = ServerActionStamp & 0x7FFFF;

            var deltaStamp = Math.Abs(currentStamp - serverStamp);

            var diff = deltaStamp <= 0x3FFF ? serverStamp < currentStamp : currentStamp < serverStamp;

            if (diff)
            {
                if ((WeenieObj != null && WeenieObj.IsCreature) || action.Autonomous)
                {
                    ServerActionStamp = action.Stamp;
                    movementParams.Speed = action.Speed;
                    movementParams.Autonomous = action.Autonomous;
                    DoInterpretedMotion(action.Action, movementParams);
                }
            }
        }

        return true;
    }

    public void set_hold_run(int val, bool cancelMoveTo)
    {
        if ((val == 0) != (RawState.CurrentHoldKey != HoldKey.Run))
        {
            RawState.CurrentHoldKey = val != 0 ? HoldKey.Run : HoldKey.None;
            apply_current_movement(cancelMoveTo, true);
        }
    }

    /// <summary>ACE MovementSystem.GetRunRate.</summary>
    public static double GetRunRate(float burden, int runSkill, float scaling)
    {
        var loadMod = BurdenMod(burden);

        if (runSkill >= 800.0f)
        {
            return 18.0f / 4.0f;
        }

        return ((loadMod * ((float)runSkill / (runSkill + 200) * 11)) + 4) / scaling / 4.0f;
    }

    /// <summary>ACE MovementSystem.GetJumpHeight.</summary>
    public static float GetJumpHeight(float burden, uint jumpSkill, float power, float scaling)
    {
        power = Math.Clamp(power, 0.0f, 1.0f);

        var result = BurdenMod(burden) * ((jumpSkill / (jumpSkill + 1300.0f) * 22.2f) + 0.05f) * power / scaling;

        if (result < 0.35f)
        {
            result = 0.35f;
        }

        return result;
    }

    /// <summary>ACE MovementSystem.JumpStaminaCost.</summary>
    public static int JumpStaminaCost(float power, float burden, bool pk) =>
        pk ? (int)((power + 1.0f) * 100.0f) : (int)Math.Ceiling(((burden + 0.5f) * power * 8.0f) + 2.0f);

    private static float BurdenMod(float burden) => burden < 1.0f ? 1.0f : burden < 2.0f ? 2.0f - burden : 0.0f;
}

using Lifestone.Data.Animation;

namespace Lifestone.Data.Motion;

/// <summary>
/// The behaviour of <see cref="InterpretedMotionState"/>, re-expressed from retail <c>InterpretedMotionState</c>
/// (acclient 11.4186 PORTAL/canim/CMInterpPrim.c; function names and addresses cited, nothing copied). Addresses are
/// 11.4186 / 11.6096.
/// </summary>
public sealed partial class InterpretedMotionState
{
    /// <summary>Retail <c>GetNumActions</c> (0x0051EB00 / 0x0051F610): the number of queued actions.</summary>
    public int NumActions => Actions.Count;

    /// <summary>
    /// Retail <c>copy_movement_from</c> (0x0051E750 / 0x0051F260): copies the style, the three commands and their
    /// speeds from <paramref name="other"/>. The actions are not copied.
    /// </summary>
    public void CopyMovementFrom(InterpretedMotionState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        CurrentStyle = other.CurrentStyle;
        ForwardCommand = other.ForwardCommand;
        ForwardSpeed = other.ForwardSpeed;
        SideStepCommand = other.SideStepCommand;
        SideStepSpeed = other.SideStepSpeed;
        TurnCommand = other.TurnCommand;
        TurnSpeed = other.TurnSpeed;
    }

    /// <summary>
    /// Retail <c>ApplyMotion</c> (0x0051EA40 / 0x0051F550): records a motion the motion table accepted.
    /// <list type="bullet">
    /// <item><see cref="MotionCommands.TurnRight"/> becomes the turn and <see cref="MotionCommands.SideStepRight"/> the
    /// sidestep, each with <see cref="MovementParameters.Speed"/>. The interpreted state only knows the right-hand
    /// commands (the interpreter has already turned a left into a negative right).</item>
    /// <item>Any other command with the substate bit (0x40000000) becomes the forward command with its speed. That
    /// includes <see cref="MotionCommands.TurnLeft"/> and <see cref="MotionCommands.SideStepLeft"/>, which have the bit:
    /// retail's own quirk, reachable only for an object whose motions are not adjusted (a non-creature).</item>
    /// <item>A style (0x80000000) becomes the style, and the forward command returns to
    /// <see cref="MotionCommands.Ready"/>; the forward speed is kept.</item>
    /// <item>An action (0x10000000) is queued with the speed, <see cref="MovementParameters.ActionStamp"/> and the
    /// autonomous bit.</item>
    /// </list>
    /// </summary>
    public void ApplyMotion(uint motion, in MovementParameters parameters)
    {
        if (motion == MotionCommands.TurnRight)
        {
            TurnCommand = MotionCommands.TurnRight;
            TurnSpeed = parameters.Speed;
        }
        else if (motion == MotionCommands.SideStepRight)
        {
            SideStepCommand = MotionCommands.SideStepRight;
            SideStepSpeed = parameters.Speed;
        }
        else if ((motion & MotionCommands.SubStateMask) != 0)
        {
            ForwardCommand = motion;
            ForwardSpeed = parameters.Speed;
        }
        else if ((motion & MotionCommands.StyleMask) == 0)
        {
            if ((motion & MotionCommands.ActionMask) != 0)
            {
                AddAction(motion, parameters.Speed, parameters.ActionStamp, parameters.Autonomous);
            }
        }
        else
        {
            ForwardCommand = MotionCommands.Ready;
            CurrentStyle = motion;
        }
    }

    /// <summary>
    /// Retail <c>RemoveMotion</c> (0x0051E790 / 0x0051F2A0): undoes a motion. <see cref="MotionCommands.TurnRight"/>
    /// clears the turn, <see cref="MotionCommands.SideStepRight"/> the sidestep; the current forward command (a substate)
    /// returns to <see cref="MotionCommands.Ready"/> at speed 1; the current style returns to
    /// <see cref="MotionCommands.NonCombat"/>. Anything else is ignored.
    /// </summary>
    public void RemoveMotion(uint motion)
    {
        if (motion == MotionCommands.TurnRight)
        {
            TurnCommand = 0;
        }
        else if (motion == MotionCommands.SideStepRight)
        {
            SideStepCommand = 0;
        }
        else if ((motion & MotionCommands.SubStateMask) != 0)
        {
            if (motion == ForwardCommand)
            {
                ForwardCommand = MotionCommands.Ready;
                ForwardSpeed = 1f;
            }
        }
        else if ((motion & MotionCommands.StyleMask) != 0 && motion == CurrentStyle)
        {
            CurrentStyle = MotionCommands.NonCombat;
        }
    }

    /// <summary>Retail <c>AddAction</c> (0x0051E9E0 / 0x0051F4F0): queues an action at the tail.</summary>
    public void AddAction(uint action, float speed, ushort stamp, bool autonomous) =>
        Actions.Add(new MotionActionNode(action, speed, stamp, autonomous));

    /// <summary>
    /// Retail <c>RemoveAction</c> (0x0051EAD0 / 0x0051F5E0): removes the oldest action and returns its command, or 0
    /// when none is queued.
    /// </summary>
    public uint RemoveAction()
    {
        if (Actions.Count == 0)
        {
            return 0;
        }

        var action = Actions[0].Action;
        Actions.RemoveAt(0);
        return action;
    }
}

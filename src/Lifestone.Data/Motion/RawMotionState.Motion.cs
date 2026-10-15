using Lifestone.Data.Animation;

namespace Lifestone.Data.Motion;

/// <summary>
/// The behaviour of <see cref="RawMotionState"/>, re-expressed from retail <c>RawMotionState</c> (acclient 11.4186
/// PORTAL/canim/CMInterpPrim.c; function names and addresses cited, nothing copied). Addresses are 11.4186 / 11.6096.
/// </summary>
public sealed partial class RawMotionState
{
    /// <summary>
    /// Retail <c>ApplyMotion</c> (0x0051EB60 / 0x0051F670): records a motion as the player's input expressed it.
    /// <list type="bullet">
    /// <item>A sidestep (right or left) or a turn (right or left) becomes the sidestep or the turn, with
    /// <see cref="MovementParameters.Speed"/> and a hold key: <see cref="HoldKey.Invalid"/> when
    /// <see cref="MovementParameters.SetHoldKey"/> is set (the motion follows <see cref="CurrentHoldKey"/>), else
    /// <see cref="MovementParameters.HoldKeyToApply"/>.</item>
    /// <item>Any other substate becomes the forward command the same way, except
    /// <see cref="MotionCommands.RunForward"/>, which is never stored: running is a walk with the run key held.</item>
    /// <item>An action is queued with the speed, the action stamp and the autonomous bit.</item>
    /// <item>A style different from the current one becomes the style and resets the forward command to
    /// <see cref="MotionCommands.Ready"/>; the current style again changes nothing.</item>
    /// </list>
    /// </summary>
    public void ApplyMotion(uint motion, in MovementParameters parameters)
    {
        var holdKey = parameters.SetHoldKey ? HoldKey.Invalid : parameters.HoldKeyToApply;
        switch (motion)
        {
            case MotionCommands.SideStepRight:
            case MotionCommands.SideStepLeft:
                SideStepCommand = motion;
                SideStepHoldKey = holdKey;
                SideStepSpeed = parameters.Speed;
                return;
            case MotionCommands.TurnRight:
            case MotionCommands.TurnLeft:
                TurnCommand = motion;
                TurnHoldKey = holdKey;
                TurnSpeed = parameters.Speed;
                return;
        }

        if ((motion & MotionCommands.SubStateMask) != 0)
        {
            if (motion != MotionCommands.RunForward)
            {
                ForwardCommand = motion;
                ForwardHoldKey = holdKey;
                ForwardSpeed = parameters.Speed;
            }
        }
        else if ((motion & MotionCommands.StyleMask) == 0)
        {
            if ((motion & MotionCommands.ActionMask) != 0)
            {
                AddAction(motion, parameters.Speed, parameters.ActionStamp, parameters.Autonomous);
            }
        }
        else if (CurrentStyle != motion)
        {
            ForwardCommand = MotionCommands.Ready;
            CurrentStyle = motion;
        }
    }

    /// <summary>
    /// Retail <c>RemoveMotion</c> (0x0051E6E0 / 0x0051F1F0): undoes a motion. A sidestep (either side) clears the
    /// sidestep and a turn (either side) the turn; the current forward command returns to
    /// <see cref="MotionCommands.Ready"/> at speed 1; the current style returns to <see cref="MotionCommands.NonCombat"/>.
    /// The hold keys are left alone.
    /// </summary>
    public void RemoveMotion(uint motion)
    {
        switch (motion)
        {
            case MotionCommands.SideStepRight:
            case MotionCommands.SideStepLeft:
                SideStepCommand = 0;
                return;
            case MotionCommands.TurnRight:
            case MotionCommands.TurnLeft:
                TurnCommand = 0;
                return;
        }

        if ((motion & MotionCommands.SubStateMask) != 0)
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

    /// <summary>Retail <c>AddAction</c> (0x0051E840 / 0x0051F350): queues an action at the tail.</summary>
    public void AddAction(uint action, float speed, ushort stamp, bool autonomous) =>
        Actions.Add(new MotionActionNode(action, speed, stamp, autonomous));

    /// <summary>
    /// Retail <c>RemoveAction</c> (0x0051E8A0 / 0x0051F3B0): removes the oldest action and returns its command, or 0
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

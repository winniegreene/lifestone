using Lifestone.Client.Movement;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Data.Animation;

namespace Lifestone.Client.Play;

/// <summary>
/// The actions of the movement input map (map 4 of the game key map <c>gmDefault</c>, KEYMAP 0x14000000), as retail's command interpreter
/// receives them: each one becomes a call on <see cref="IPlayerMovementIntents"/>. It is the <see cref="IInputActionSink"/> the host registers for
/// <see cref="UiInputRouter.MovementMap"/>, and does for the router's actions what <c>ACCmdInterp::OnAction</c> does (11.4186 0x0058B370, 11.6096 0x0058C1A0).
/// </summary>
/// <remarks>
/// <para>The map (the user's dats; <c>MovementBindingsTests</c> pins it):</para>
/// <list type="table">
/// <item><term>0x29, 0x2A</term><description>Move Forward (W, Up), Move Backward (X, Down): WalkForward and WalkBackwards, a press and its release.</description></item>
/// <item><term>0x2E, 0x2F</term><description>Turn Right (D, Right), Turn Left (A, Left): TurnRight and TurnLeft, a press and its release.</description></item>
/// <item><term>0x2C, 0x2D</term><description>Strafe Right (C, Alt+D, Alt+Right), Strafe Left (Z, Alt+A, Alt+Left): SideStepRight and SideStepLeft. Retail's keymap has no sidestep modifier, so these are commands of their own.</description></item>
/// <item><term>0x2B</term><description>Stop Moving (S): Ready, always as a press.</description></item>
/// <item><term>0x30</term><description>Autorun (Q), a toggle: the first press starts it and the next ends it. The router also ends it when another movement action starts.</description></item>
/// <item><term>0x31</term><description>Jump (Space): the key down is <c>CommenceJump</c> (the jump bar starts), the key up <c>DoJump</c>.</description></item>
/// <item><term>0x32</term><description>Toggle Run/Walk (Left Shift), held: <c>SetHoldRun</c> (docs/physics.md ยง12, "The run key's two paths").</description></item>
/// <item><term>0x10000094-0x10000097</term><description>Ready, Crouch, Sit, Lay Down: the emote actions, always as a press (<c>m_hashEmoteInputActionsToCommands</c>; docs/physics.md ง12). Each is mapped to the command of its name (Ready, Crouch, Sitting, Sleeping): the retail table itself is not read.</description></item>
/// </list>
/// <para>Autorun is idempotent here: a start turns it on and an end turns it off, whatever the controller's state (a teleport or a server move
/// clears autorun without the router knowing), where retail toggles on every delivery.</para>
/// </remarks>
public sealed class MovementBindings : IInputActionSink
{
    /// <summary>Move Forward.</summary>
    public const uint MoveForward = 0x29;

    /// <summary>Move Backward.</summary>
    public const uint MoveBackward = 0x2A;

    /// <summary>Stop Moving (Ready).</summary>
    public const uint StopMoving = 0x2B;

    /// <summary>Strafe Right.</summary>
    public const uint StrafeRight = 0x2C;

    /// <summary>Strafe Left.</summary>
    public const uint StrafeLeft = 0x2D;

    /// <summary>Turn Right.</summary>
    public const uint TurnRight = 0x2E;

    /// <summary>Turn Left.</summary>
    public const uint TurnLeft = 0x2F;

    /// <summary>Autorun.</summary>
    public const uint AutoRun = 0x30;

    /// <summary>Jump.</summary>
    public const uint Jump = 0x31;

    /// <summary>Toggle Run/Walk (the run key).</summary>
    public const uint RunKey = 0x32;

    /// <summary>The first of the emote actions of the movement map (Ready, Crouch, Sit, Lay Down).</summary>
    public const uint FirstEmoteAction = 0x10000094;

    /// <summary>The last of the emote actions of the movement map.</summary>
    public const uint LastEmoteAction = 0x10000097;

    private readonly IPlayerMovementIntents _intents;
    private readonly Func<double> _now;
    private readonly Func<bool>? _autoRun;

    /// <param name="intents">The player's movement.</param>
    /// <param name="now">The host's clock in seconds (the jump bar's time).</param>
    /// <param name="autoRun">Whether autorun is on now (<see cref="PlayerMovementController.AutoRun"/>); null toggles on every delivery, as retail does.</param>
    public MovementBindings(IPlayerMovementIntents intents, Func<double> now, Func<bool>? autoRun = null)
    {
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(now);
        _intents = intents;
        _now = now;
        _autoRun = autoRun;
    }

    /// <summary>The motion command a movement action sends as a key (0 when the action is not a key command).</summary>
    public static uint CommandOf(uint actionId) => actionId switch
    {
        MoveForward => MotionCommands.WalkForward,
        MoveBackward => MotionCommands.WalkBackwards,
        TurnRight => MotionCommands.TurnRight,
        TurnLeft => MotionCommands.TurnLeft,
        StrafeRight => MotionCommands.SideStepRight,
        StrafeLeft => MotionCommands.SideStepLeft,
        _ => 0,
    };

    /// <summary>The motion command of an emote action (0 when the action is not one).</summary>
    public static uint EmoteCommandOf(uint actionId) => actionId switch
    {
        0x10000094 => MotionCommands.Ready,
        0x10000095 => 0x41000012, // Crouch
        0x10000096 => 0x41000013, // Sitting
        0x10000097 => 0x41000014, // Sleeping
        _ => 0,
    };

    /// <summary>True for the actions of the movement map this class handles.</summary>
    public static bool Handles(uint actionId) => actionId is >= MoveForward and <= RunKey || EmoteCommandOf(actionId) != 0;

    /// <summary>
    /// Raised when a person pressed a movement key this class handles (the argument is <see cref="NameOf"/> of its action; a
    /// release does not raise it): the human's key wins over automation (R6), so a runner ends as Interrupted. Raised after the
    /// key reached the intents. Automation never goes through here: it moves the player through <c>MoveIntents</c>.
    /// </summary>
    public event Action<string>? HumanKey;

    /// <summary>The name of a movement action for <see cref="HumanKey"/> ("MoveForward", "Jump", "RunKey", "Emote_..."; the hex id otherwise).</summary>
    public static string NameOf(uint actionId) => actionId switch
    {
        MoveForward => nameof(MoveForward),
        MoveBackward => nameof(MoveBackward),
        StopMoving => nameof(StopMoving),
        StrafeRight => nameof(StrafeRight),
        StrafeLeft => nameof(StrafeLeft),
        TurnRight => nameof(TurnRight),
        TurnLeft => nameof(TurnLeft),
        AutoRun => nameof(AutoRun),
        Jump => nameof(Jump),
        RunKey => nameof(RunKey),
        _ when EmoteCommandOf(actionId) != 0 => "Emote_0x" + actionId.ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
        _ => "0x" + actionId.ToString("X", System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <inheritdoc/>
    public bool OnInputAction(uint inputMapId, uint actionId, RetailActivation activation)
    {
        if (!Dispatch(inputMapId, actionId, activation))
        {
            return false;
        }

        if (activation.Start)
        {
            HumanKey?.Invoke(NameOf(actionId));
        }

        return true;
    }

    private bool Dispatch(uint inputMapId, uint actionId, RetailActivation activation)
    {
        if (inputMapId != UiInputRouter.MovementMap)
        {
            return false;
        }

        var start = activation.Start;
        var command = CommandOf(actionId);
        if (command != 0)
        {
            _intents.HandleCommand(command, start);
            return true;
        }

        switch (actionId)
        {
            case StopMoving:
                if (start)
                {
                    _intents.HandleCommand(MotionCommands.Ready, true);
                }

                return true;
            case AutoRun:
                SetAutoRun(start);
                return true;
            case Jump:
                if (start)
                {
                    _intents.CommenceJump(_now());
                }
                else
                {
                    _intents.DoJump(_now());
                }

                return true;
            case RunKey:
                _intents.SetHoldRun(start);
                return true;
            default:
                if (EmoteCommandOf(actionId) is var emote and not 0)
                {
                    if (start)
                    {
                        _intents.HandleCommand(emote, true);
                    }

                    return true;
                }

                return false;
        }
    }

    private void SetAutoRun(bool start)
    {
        if (_autoRun is null || _autoRun() != start)
        {
            _intents.ToggleAutoRun();
        }
    }
}

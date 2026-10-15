using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>The movement intents of <see cref="MoveIntents"/>, one per member of physics' <c>IPlayerMovementIntents</c>.</summary>
internal enum MoveIntent : byte
{
    /// <summary><c>HandleCommand(command, start, speed)</c>.</summary>
    Command,

    /// <summary><c>SetHoldRun(on)</c>.</summary>
    HoldRun,

    /// <summary><c>SetHoldSidestep(on)</c>.</summary>
    HoldSidestep,

    /// <summary><c>ToggleAutoRun()</c>.</summary>
    ToggleAutoRun,

    /// <summary><c>CommenceJump(now)</c>.</summary>
    ChargeJump,

    /// <summary><c>DoJump(now)</c>.</summary>
    Jump,
}

/// <summary>
/// One <see cref="MoveIntents"/> intent (step ST11): rate-limited like any automation action, then handed to the bound
/// movement controller on the game thread, with the tick's frame time for the jump bar. It completes
/// <see cref="ActionOutcomeKind.Local"/> when the controller took it, or <see cref="ActionOutcomeKind.NotImplemented"/>
/// when no controller is bound by then. The movement messages that follow are the controller's to send.
/// </summary>
internal sealed class MoveIntentTracker(ActionHandle handle, MoveIntents owner, MoveIntent intent, uint command, bool on, float speed)
    : ActionTracker(handle, ActionLane.Move, double.PositiveInfinity)
{
    /// <summary>The intent.</summary>
    internal MoveIntent Intent => intent;

    /// <summary>The motion command, for <see cref="MoveIntent.Command"/>.</summary>
    internal uint MotionCommand => command;

    /// <summary>Start (or on) versus stop (or off).</summary>
    internal bool On => on;

    /// <summary>The speed factor, for <see cref="MoveIntent.Command"/>.</summary>
    internal float Speed => speed;

    /// <inheritdoc/>
    internal override bool NeedsSession => false;

    /// <inheritdoc/>
    internal override string WouldSend => "nothing yet: " + Describe(intent, command, on, speed) + " to the player's movement controller";

    /// <summary>An intent as the action log shows it (<c>MoveIntent Command 0x45000005 start speed=1</c>).</summary>
    internal static string Describe(MoveIntent intent, uint command, bool on, float speed) => intent switch
    {
        MoveIntent.Command => string.Create(
            CultureInfo.InvariantCulture,
            $"MoveIntent Command {ActionText.Hex(command)} {(on ? "start" : "stop")} speed={speed}"),
        MoveIntent.HoldRun or MoveIntent.HoldSidestep => "MoveIntent " + intent + (on ? " on" : " off"),
        _ => "MoveIntent " + intent,
    };

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api) => owner.Apply(this, api.FrameTime);
}

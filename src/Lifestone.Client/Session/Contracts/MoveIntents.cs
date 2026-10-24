using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Actions.Trackers;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The automation path into the player's movement controller (<see cref="IActionApi.Move"/>): a bot moves the player
/// through the same controller the keyboard drives (A0's R2), so its movement obeys the same physics and the same
/// retail send cadence. The methods mirror physics' <see cref="IPlayerMovementIntents"/>; each is audit-logged and
/// rate-limited like any action (20 a second, bursts of 20).
/// </summary>
/// <remarks>
/// <para>The host binds the controller it builds (<see cref="Bind"/>: the play mode, or the headless host once physics
/// lands). With no controller bound, every intent completes at once with <see cref="ActionOutcomeKind.NotImplemented"/>
/// and an ERROR line: never a silent success.</para>
/// <para>An intent waits in its lane like any action and reaches the controller in the session's next tick; it completes
/// <see cref="ActionOutcomeKind.Local"/> when the controller took it. The movement messages that follow go through
/// <see cref="IMovementSink"/> as the controller decides. The kill switch refuses and dry run logs automation intents like
/// any other automation action, and engaging the kill switch releases every command, hold and autorun that automation
/// left on, so the player stops.</para>
/// <para>Intents need no server: an offline session's controller takes them too. Game thread only.</para>
/// <para><b>The run key.</b> The binding carries the player's run-by-default option (<see cref="RunByDefault"/>, bound with
/// the controller). Retail's run key is inverted by it (<c>CommandInterpreter::SetHoldRun</c>, 11.6096 0x006B42B0: the motion's
/// run key is the key XOR the option), so a caller that means "run" or "walk" asks <see cref="SetRun"/> and the key goes
/// down only when it must. <see cref="HoldRun"/> is the raw key.</para>
/// <para><b>The human's key wins (R6).</b> The play host reports every movement key a person presses through
/// <see cref="ReportHumanKey"/>; a runner subscribes to <see cref="HumanKey"/> and gives the player back.</para>
/// </remarks>
public sealed class MoveIntents : IRunByDefaultSource
{
    private readonly ActionApi _api;
    private readonly HashSet<uint> _heldByAutomation = [];
    private IPlayerMovementIntents? _controller;
    private bool _runHeldByAutomation;
    private bool _sidestepHeldByAutomation;
    private bool _autoRunToggledByAutomation;

    /// <summary>Only the session creates one.</summary>
    internal MoveIntents(ActionApi api)
    {
        _api = api;
    }

    /// <summary>True while a movement controller is bound.</summary>
    public bool IsBound => _controller is not null;

    /// <summary>
    /// The bound player's run-by-default option (<see cref="MovementOptions.RunByDefault"/>): true when an unheld run key
    /// runs and a held one walks. False while nothing is bound (read <see cref="IsBound"/> first).
    /// </summary>
    public bool RunByDefault { get; private set; }

    /// <summary>
    /// Raised on the game thread when a person pressed a movement key (the argument names it, e.g. "MoveForward", "Jump",
    /// "RunKey"): the player's keys are the human's first (R6), so a runner ends as Interrupted. Raised by the play host through
    /// <see cref="ReportHumanKey"/>; never by an automation intent, and not by a key release.
    /// </summary>
    public event Action<string>? HumanKey;

    /// <summary>
    /// The host reports a movement key a person pressed (the play host's input path, <c>MovementBindings.HumanKey</c>);
    /// raises <see cref="HumanKey"/>. Not an action: nothing is queued, logged to the audit or rate-limited.
    /// </summary>
    /// <param name="key">The key's name.</param>
    public void ReportHumanKey(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        _api.CheckThread("MoveIntents.ReportHumanKey");
        HumanKey?.Invoke(key);
    }

    /// <summary>
    /// Binds the player's movement controller (physics' <c>PlayerMovementController</c>), or unbinds it with null. Intents
    /// already queued reach whichever controller is bound when their turn comes. Binding the controller that is bound only
    /// updates <paramref name="runByDefault"/>.
    /// </summary>
    /// <param name="controller">The controller, or null.</param>
    /// <param name="runByDefault">The player's run-by-default option (<c>controller.Options.RunByDefault</c>): the run key is inverted by it.</param>
    public void Bind(IPlayerMovementIntents? controller, bool runByDefault)
    {
        _api.CheckThread("MoveIntents.Bind");
        if (ReferenceEquals(controller, _controller))
        {
            RunByDefault = controller is not null && runByDefault;
            return;
        }

        _controller = controller;
        RunByDefault = controller is not null && runByDefault;
        _heldByAutomation.Clear();
        _runHeldByAutomation = false;
        _sidestepHeldByAutomation = false;
        _autoRunToggledByAutomation = false;
        _api.Log.Info(
            SessionLog.Category,
            controller is null ? "movement intents: controller unbound" : "movement intents: controller bound",
            new LogField("run_by_default", RunByDefault));
    }

    /// <summary>Unbinds the controller (<c>Bind(null, false)</c>): every intent then completes <see cref="ActionOutcomeKind.NotImplemented"/>.</summary>
    public void Unbind() => Bind(null, false);

    /// <summary>
    /// Unbinds only when <paramref name="expected"/> is the bound controller (a scenario that bound its own walker releases it
    /// without unbinding a host's controller bound after it). Returns whether it unbound.
    /// </summary>
    public bool Unbind(IPlayerMovementIntents expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (!ReferenceEquals(_controller, expected))
        {
            return false;
        }

        Bind(null, false);
        return true;
    }

    /// <summary>Starts or stops a motion command (forward, backward, turn, sidestep; full 32-bit retail motion ids).</summary>
    /// <param name="motionCommand">The motion command, e.g. WalkForward 0x45000005.</param>
    /// <param name="start">True to start it, false to stop it.</param>
    /// <param name="source">Who asked (normally <see cref="ActionSource.Automation"/>).</param>
    /// <param name="speed">The speed multiplier (finite and above 0).</param>
    /// <returns>The intent's handle.</returns>
    public ActionHandle Command(uint motionCommand, bool start, ActionSource source, float speed = 1f)
    {
        string? problem = motionCommand == 0 ? "no motion command"
            : !float.IsFinite(speed) || speed <= 0 ? "the speed must be finite and above 0"
            : null;
        return Accept(MoveIntent.Command, motionCommand, start, speed, source, problem);
    }

    /// <summary>Holds or releases the run key itself (retail's key, which <see cref="RunByDefault"/> inverts: see <see cref="SetRun"/>).</summary>
    public ActionHandle HoldRun(bool on, ActionSource source) => Accept(MoveIntent.HoldRun, 0, on, 1f, source, null);

    /// <summary>
    /// Asks for running (<paramref name="run"/> true) or walking (false) whatever the player's run-by-default option is: the run
    /// key is held when <paramref name="run"/> differs from <see cref="RunByDefault"/> (<c>HoldRun(run != RunByDefault)</c>).
    /// </summary>
    /// <param name="run">True to run, false to walk.</param>
    /// <param name="source">Who asked (normally <see cref="ActionSource.Automation"/>).</param>
    public ActionHandle SetRun(bool run, ActionSource source) => HoldRun(run != RunByDefault, source);

    /// <summary>Holds or releases the sidestep modifier.</summary>
    public ActionHandle HoldSidestep(bool on, ActionSource source) => Accept(MoveIntent.HoldSidestep, 0, on, 1f, source, null);

    /// <summary>Toggles auto-run.</summary>
    public ActionHandle ToggleAutoRun(ActionSource source) => Accept(MoveIntent.ToggleAutoRun, 0, true, 1f, source, null);

    /// <summary>Starts charging a jump (the jump key pressed).</summary>
    public ActionHandle ChargeJump(ActionSource source) => Accept(MoveIntent.ChargeJump, 0, true, 1f, source, null);

    /// <summary>Jumps with the charge so far (the jump key released).</summary>
    public ActionHandle Jump(ActionSource source) => Accept(MoveIntent.Jump, 0, true, 1f, source, null);

    /// <summary>
    /// Hands <paramref name="tracker"/>'s intent to the bound controller (the Action API calls it when the intent's turn
    /// comes): <see cref="ActionOutcomeKind.Local"/>, or <see cref="ActionOutcomeKind.NotImplemented"/> with no controller.
    /// </summary>
    internal ActionOutcome Apply(MoveIntentTracker tracker, double frameTime)
    {
        var controller = _controller;
        if (controller is null)
        {
            return NotBound();
        }

        var automation = tracker.Handle.Source == ActionSource.Automation;
        switch (tracker.Intent)
        {
            case MoveIntent.Command:
                controller.HandleCommand(tracker.MotionCommand, tracker.On, tracker.Speed);
                if (tracker.On && automation)
                {
                    _heldByAutomation.Add(tracker.MotionCommand);
                }
                else
                {
                    // Stopped, or taken over by a person: no longer automation's to release.
                    _heldByAutomation.Remove(tracker.MotionCommand);
                }

                break;
            case MoveIntent.HoldRun:
                controller.SetHoldRun(tracker.On);
                _runHeldByAutomation = automation && tracker.On;
                break;
            case MoveIntent.HoldSidestep:
                controller.SetHoldSidestep(tracker.On);
                _sidestepHeldByAutomation = automation && tracker.On;
                break;
            case MoveIntent.ToggleAutoRun:
                controller.ToggleAutoRun();
                // Automation's toggles flip it; a person's toggle takes autorun over.
                _autoRunToggledByAutomation = automation && !_autoRunToggledByAutomation;
                break;
            case MoveIntent.ChargeJump:
                controller.CommenceJump(frameTime);
                break;
            default:
                controller.DoJump(frameTime);
                break;
        }

        return new ActionOutcome(ActionOutcomeKind.Local);
    }

    /// <summary>
    /// The kill switch engaged: releases every command automation started and has not stopped, the run and sidestep holds
    /// it set, and the autorun it turned on, so the player stops. Returns how many were released.
    /// </summary>
    internal int ReleaseAutomation()
    {
        var controller = _controller;
        var released = 0;
        if (controller is not null)
        {
            foreach (var command in _heldByAutomation)
            {
                controller.HandleCommand(command, false);
                released++;
            }

            if (_runHeldByAutomation)
            {
                controller.SetHoldRun(false);
                released++;
            }

            if (_sidestepHeldByAutomation)
            {
                controller.SetHoldSidestep(false);
                released++;
            }

            if (_autoRunToggledByAutomation)
            {
                controller.ToggleAutoRun();
                released++;
            }
        }

        _heldByAutomation.Clear();
        _runHeldByAutomation = false;
        _sidestepHeldByAutomation = false;
        _autoRunToggledByAutomation = false;
        return released;
    }

    private static ActionOutcome NotBound() => new(
        ActionOutcomeKind.NotImplemented,
        0,
        "TODO(state): no movement controller is bound (the host binds physics' PlayerMovementController with MoveIntents.Bind)");

    private ActionHandle Accept(MoveIntent intent, uint command, bool on, float speed, ActionSource source, string? problem)
    {
        _api.CheckThread("MoveIntents." + intent);
        var handle = _api.NewHandle(ActionKind.MoveIntent, source, MoveIntentTracker.Describe(intent, command, on, speed));
        var tracker = new MoveIntentTracker(handle, this, intent, command, on, speed);
        var early = problem is not null ? new ActionOutcome(ActionOutcomeKind.Refused, 0, problem)
            : _controller is null ? NotBound()
            : (ActionOutcome?)null;
        return _api.Accept(tracker, early);
    }
}

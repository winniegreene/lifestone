using System.Globalization;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Game.Core;

namespace Lifestone.Game.Sessions;

/// <summary>What <see cref="LegSessionFlow.Step"/> reports.</summary>
internal enum LegFlowStatus
{
    /// <summary>Still working (logging in, entering, arriving, settling, logging off, recovering).</summary>
    Working,

    /// <summary>The character is in the world, present and settled: the leg's own script may run now.</summary>
    Ready,

    /// <summary>The flow is over (the quit was flushed, or the failure could not log off): the leg finishes now.</summary>
    Ended,
}

/// <summary>
/// The login-to-world and back out that the journey check and the autopilot check share (M5 4 h1; extracted from <c>JourneyLegRunner</c>, whose messages it keeps word for word): the character list, the choice of the plan's
/// character, the Enter, the arrival, the settle; then, when the leg's script ends, the log off and the quit; and the fail path (a failure logs off first when the character is in the world, so ACE does not keep it for
/// the next run). The node owns the frame, the script and the verdict; this owns the phases between "no session" and "the script may run" and between "the script ended" and "the quit". One instance a run; the node adds the
/// frame's delta to <see cref="Seconds"/> before it steps.
/// </summary>
/// <param name="label">The leg's word in its lines (<c>journey</c>, <c>autopilot</c>).</param>
/// <param name="category">The log category.</param>
/// <param name="character">The character the leg enters (it must be in the list: the leg creates nobody).</param>
internal sealed class LegSessionFlow(string label, string category, string character)
{
    /// <summary>The seconds the character list may take.</summary>
    public const double ListSeconds = 60.0;

    /// <summary>The seconds the player must stay present before the script starts.</summary>
    public const double SettleSeconds = 3.0;

    /// <summary>The seconds the quit's last packets get before the leg ends.</summary>
    public const double QuitFlushSeconds = 1.5;

    /// <summary>How long the log off after a failure may take, in seconds.</summary>
    public const double RecoverySeconds = 120.0;

    private enum Phase
    {
        Idle,
        Login,
        Entering,
        Arriving,
        Settling,
        Ready,
        LoggingOff,
        Quitting,
    }

    private Phase _phase = Phase.Idle;
    private double _phaseStart;
    private ActionHandle? _pending;
    private bool _recoveryStarted;
    private double _recoveryStart = double.NaN;
    private double _quitAt = double.NaN;
    private bool _endedNow;

    /// <summary>The leg's clock: the node adds each frame's delta before <see cref="Step"/>.</summary>
    public double Seconds { get; set; }

    /// <summary>True once the Enter was confirmed (a failure logs off only then).</summary>
    public bool Entered { get; private set; }

    /// <summary>True once a failure was recorded.</summary>
    public bool Failed { get; private set; }

    /// <summary>The first failure, or null.</summary>
    public string? Failure { get; private set; }

    /// <summary>The phase the first failure happened in (the node's phase name when it passed one).</summary>
    public string FailedAt { get; private set; } = "start";

    /// <summary>The flow's phase as a word (the failure line's <c>phase=</c>).</summary>
    public string PhaseName => _phase.ToString();

    /// <summary>True from the quit on: the node only waits for <see cref="Step"/> to say <see cref="LegFlowStatus.Ended"/>.</summary>
    public bool Quitting => !double.IsNaN(_quitAt);

    /// <summary>Starts the login (the character list is waited for from now).</summary>
    public void Begin()
    {
        Go(Phase.Login);
    }

    /// <summary>Steps the flow once. <see cref="LegFlowStatus.Ready"/> comes once the player has settled; the node then runs its script and, when it ends, calls <see cref="BeginLogOff"/>.</summary>
    /// <param name="session">The session.</param>
    public LegFlowStatus Step(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_endedNow)
        {
            return LegFlowStatus.Ended;
        }

        if (Quitting)
        {
            return Seconds - _quitAt >= QuitFlushSeconds ? LegFlowStatus.Ended : LegFlowStatus.Working;
        }

        if (Failed)
        {
            return StepRecovery(session);
        }

        switch (_phase)
        {
            case Phase.Login:
                Login(session);
                break;
            case Phase.Entering:
                Entering(session);
                break;
            case Phase.Arriving:
                Arriving(session);
                break;
            case Phase.Settling:
                Settling(session);
                break;
            case Phase.LoggingOff:
                LoggingOff(session);
                break;
            case Phase.Ready:
                return LegFlowStatus.Ready;
        }

        if (_endedNow)
        {
            return LegFlowStatus.Ended;
        }

        return _phase == Phase.Ready ? LegFlowStatus.Ready : LegFlowStatus.Working;
    }

    /// <summary>The script ended well: logs off (the quit follows once the log off is confirmed).</summary>
    /// <param name="session">The session.</param>
    public void BeginLogOff(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _pending = session.Actions.LogOff(ActionSource.Host);
        Go(Phase.LoggingOff);
    }

    /// <summary>
    /// Records the first failure and logs off before it is reported when the character is in the world. Returns true when the flow ended at once (nothing to log off): the node finishes now.
    /// </summary>
    /// <param name="session">The session, or null.</param>
    /// <param name="why">The failure.</param>
    /// <param name="phase">The node's own phase name when the failure is not the flow's (a loading or script phase); null for the flow's.</param>
    public bool Fail(IGameSessionView? session, string why, string? phase = null)
    {
        if (Failed)
        {
            return false;
        }

        Failed = true;
        Failure = why;
        FailedAt = phase ?? _phase.ToString();
        Log.Error(category, $"the {label} leg failed", ("phase", FailedAt), ("why", why));
        if (session is not null && Entered && BuffLegPhases.InWorld(session.State) && _phase != Phase.LoggingOff)
        {
            Log.Info(category, label + ": logging off before the run ends as failed");
            _pending = session.Actions.LogOff(ActionSource.Host);
            _recoveryStarted = true;
            _recoveryStart = Seconds;
            return false;
        }

        _endedNow = true;
        return true;
    }

    private LegFlowStatus StepRecovery(IGameSessionView session)
    {
        if (_recoveryStarted && _pending is { Outcome: { } left })
        {
            _pending = null;
            Log.Info(category, label + ": the failed run logged off: " + left.Kind + (left.Detail is null ? string.Empty : ": " + left.Detail));
            _ = session.Actions.Quit(ActionSource.Host);
            _quitAt = Seconds;
            return LegFlowStatus.Working;
        }

        return !_recoveryStarted || Seconds - _recoveryStart > RecoverySeconds ? LegFlowStatus.Ended : LegFlowStatus.Working;
    }

    private void Login(IGameSessionView session)
    {
        var state = session.State;
        if (BuffLegPhases.Failed(state))
        {
            Fail(session, "login: the connection failed (" + (state.FailureMessage ?? "no reason given") + ")");
            return;
        }

        if (!BuffLegPhases.AtCharacterSelect(state))
        {
            if (Seconds - _phaseStart > ListSeconds)
            {
                Fail(session, string.Create(CultureInfo.InvariantCulture, $"login: the character list did not come within {ListSeconds:0} s (phase {state.Phase}, data check {state.DataCheck})"));
            }

            return;
        }

        if (state.DataCheck != DataCheckState.Complete)
        {
            Fail(session, "login: the character list came without a completed data check (" + state.DataCheck + ")");
            return;
        }

        foreach (var c in state.Characters.Entries)
        {
            if (!string.Equals(c.Name, character, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (c.PendingDeletion)
            {
                Fail(session, "choosing: " + character + " is pending deletion (restore it first)");
                return;
            }

            session.Actions.SelectCharacter(c.Guid, ActionSource.Host);
            _pending = session.Actions.EnterWorld(c.Guid, ActionSource.Host);
            Log.Info(category, label + ": entering the existing character " + character);
            Go(Phase.Entering);
            return;
        }

        Fail(session, "choosing: " + character + " is not in the character list (run its build scenario first; the " + label + " creates nobody)");
    }

    private void Entering(IGameSessionView session)
    {
        if (_pending is not { Outcome: { } outcome })
        {
            return;
        }

        _pending = null;
        if (outcome.Kind != ActionOutcomeKind.Confirmed)
        {
            Fail(session, "entering the world: " + Describe(outcome));
            return;
        }

        Entered = true;
        Go(Phase.Arriving);
    }

    private void Arriving(IGameSessionView session)
    {
        var state = session.State;
        if (state.Self.Travel == SelfTravel.Present)
        {
            Go(Phase.Settling);
            return;
        }

        if (BuffLegPhases.Failed(state) || BuffLegPhases.AtCharacterSelect(state))
        {
            Fail(session, "arriving: the session left the world before the player was unhidden (phase " + state.Phase + ")");
            return;
        }

        if (Seconds - _phaseStart > LoginScenario.DefaultArrivalSeconds)
        {
            Fail(session, string.Create(CultureInfo.InvariantCulture, $"arriving: the player was not unhidden within {LoginScenario.DefaultArrivalSeconds:0} s (travel {state.Self.Travel})"));
        }
    }

    private void Settling(IGameSessionView session)
    {
        var state = session.State;
        if (!BuffLegPhases.InWorld(state) || state.Self.Travel != SelfTravel.Present || state.Self.Hidden)
        {
            Fail(session, "settling: the player did not stay in the world (phase " + state.Phase + ", travel " + state.Self.Travel + ")");
            return;
        }

        if (Seconds - _phaseStart >= SettleSeconds)
        {
            Go(Phase.Ready);
        }
    }

    private void LoggingOff(IGameSessionView session)
    {
        if (_pending is not { Outcome: { } outcome })
        {
            return;
        }

        _pending = null;
        if (outcome.Kind != ActionOutcomeKind.Confirmed)
        {
            Fail(null, "logging off: " + Describe(outcome));
            return;
        }

        _ = session.Actions.Quit(ActionSource.Host);
        _quitAt = Seconds;
        _phase = Phase.Quitting;
    }

    private void Go(Phase next)
    {
        _phase = next;
        _phaseStart = Seconds;
    }

    private static string Describe(in ActionOutcome outcome) =>
        outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail);
}

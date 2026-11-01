using Godot;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.RetailChecks;
using Lifestone.Game.Core;

namespace Lifestone.Game.Sessions;

/// <summary>
/// The unattended start and end of the play host's dial (M5 item 7 s1; <c>m5-autopilot-start</c>). <c>--autopilot-start</c> presses the dial once, as the key does, when the character is in the world; when that run ends by its own
/// stop rule or a failure the host logs the character off and quits the game with the exit code the logic decided (<see cref="AutopilotHostLogic.UnattendedEnd"/>). A person's request and an interrupt keep the game open.
/// </summary>
public sealed partial class AutopilotHost
{
    /// <summary>The option that asked for an unattended start (the log line and the logic's token).</summary>
    public const string StartOption = "--autopilot-start";

    /// <summary>The longest the host waits for the log-off to complete before it quits anyway, in seconds (Lifestone's own; the session's quit then sends the disconnect).</summary>
    public const double LogOffWaitSeconds = 30;

    private bool _startByOption;
    private string? _pendingStartedBy;
    private string? _pendingReason;
    private ActionHandle? _exitLogOff;
    private double _exitWaited;
    private bool _exiting;
    private bool _quitCalled;
    private bool _unattendedRun;
    private bool _faultEnd;

    /// <summary>The exit code of an unattended end that a fault (not a stop rule) decided.</summary>
    public const int FaultExitCode = 1;

    /// <summary>True when <c>--autopilot-start</c> is on and has not started the dial yet.</summary>
    public bool StartPending => _startByOption;

    /// <summary>True once the unattended end began (the log-off sent or the quit under way).</summary>
    public bool Exiting => _exiting;

    /// <summary>
    /// The unattended start: once, when the character is in the world and its name is known, the dial is pressed as the key presses it (the host's own rule, BSD Mage only, answers with its one existing line for any other character).
    /// It never starts again by itself.
    /// </summary>
    private void TickUnattendedStart(IGameSessionView session)
    {
        if (!_startByOption || !KeepAcrossLogins.InWorld(session) || session.State.Self.Name is not { Length: > 0 })
        {
            return;
        }

        _startByOption = false;
        Press(StartOption);
    }

    /// <summary>
    /// The soak's stop file (M5 7 F18, <c>--autopilot-stop-file</c>): resolved by the output-path rule (relative to the repository, never under src/ or tests/); a path that cannot be used is reported and nothing is watched.
    /// </summary>
    private void SetupStopFile(AutopilotHostOptions hostOptions)
    {
        if (hostOptions.StopFile is not { } path)
        {
            return;
        }

        if (!Lifestone.Client.Diagnostics.OutputPaths.TryResolve(path, Diagnostics.ScreenshotWriter.RepositoryRoot, out var full, out var problem))
        {
            Log.Error(LogCategory, $"autopilot: --autopilot-stop-file {problem}; no stop file is watched");
            return;
        }

        _stopFile = new StopFileWatch(full, () => System.IO.File.Exists(full));
        var shown = System.IO.Path.GetRelativePath(Diagnostics.ScreenshotWriter.RepositoryRoot, full).Replace('\\', '/');
        Log.Info(LogCategory, $"autopilot: watching for the stop file every {StopFileWatch.PollSeconds:0} s: {shown}");
    }

    /// <summary>Looks for the stop file (every <see cref="StopFileWatch.PollSeconds"/> s); when it exists a running dial is asked to stop (request <c>stop-file</c>), logged once.</summary>
    private void TickStopFile(double delta)
    {
        if (_stopFile is not { } watch || !watch.Tick(delta))
        {
            return;
        }

        // M5 7 G2 (edge audit item 6): the owner's stop file acts in every dial mode that has a run to end: running, interrupted (the person holds the keys: the logic hands the controls back with the stop) and halted (the kill switch is on).
        if (_logic.Mode is AutopilotMode.Running or AutopilotMode.Interrupted or AutopilotMode.Halted)
        {
            Log.Info(LogCategory, "autopilot: the stop file exists (" + _logic.Mode.ToString().ToLowerInvariant() + "): the autopilot is asked to stop (it recalls home first; " + (_unattendedRun ? "then the game logs off and quits" : "the game stays open") + ")");
            _logic.Stop(AutopilotHostLogic.StopFileReason);
        }
    }

    /// <summary>
    /// The unattended end: when the logic decided one, logs off (when in the world), waits for it up to <see cref="LogOffWaitSeconds"/>, then quits the game with the decided exit code. The done line was written before, and the log is flushed
    /// first.
    /// </summary>
    private void TickUnattendedEnd(double delta, IGameSessionView session)
    {
        var exitCode = _faultEnd ? FaultExitCode : _logic.UnattendedEnd?.ExitCode;
        if (_quitCalled || exitCode is not { } code)
        {
            return;
        }

        if (!_exiting)
        {
            _exiting = true;
            if (KeepAcrossLogins.InWorld(session))
            {
                _exitLogOff = session.Actions.LogOff(ActionSource.Host);
                Log.Info(LogCategory, "autopilot: logging off before the game quits");
            }
            else
            {
                Log.Info(LogCategory, $"autopilot: the session is not in the world ({session.State.Phase}); nothing to log off");
            }
        }

        _exitWaited += delta;
        if (_exitLogOff is { IsCompleted: false } && _exitWaited < LogOffWaitSeconds)
        {
            return;
        }

        if (_exitLogOff is { } handle)
        {
            Log.Info(LogCategory, handle.IsCompleted ? $"autopilot: the log-off ended: {handle.Outcome?.Kind}" : $"autopilot: the log-off was not confirmed within {LogOffWaitSeconds:0} s; quitting anyway");
        }

        _quitCalled = true;
        Log.Info(LogCategory, $"autopilot: the game quits with exit code {code}");
        Log.FlushToGodot();
        GetTree().Quit(code);
    }

    /// <summary>
    /// 7-R6: with <c>--autopilot-start</c> (an unattended run), a host fault or a failed kit load is an unattended end with exit code <see cref="FaultExitCode"/>: the game logs off and quits as it does for any other failure, rather than idling for
    /// the rest of the night. Without the option nothing changes (the game keeps running, as before).
    /// </summary>
    private void BeginFaultEnd(string why)
    {
        if (!_unattendedRun || _faultEnd)
        {
            return;
        }

        _faultEnd = true;
        Log.Error(LogCategory, $"autopilot: this run is unattended (--autopilot-start) and {why}; the game logs off and quits with exit code {FaultExitCode}");
    }

    /// <summary>The end of an unattended run whose host faulted (the guard tripped, so the normal tick no longer runs). A failure of the end itself still quits.</summary>
    private void TickFaultEnd(double delta, IGameSessionView session)
    {
        if (!_faultEnd || _quitCalled)
        {
            return;
        }

        try
        {
            TickUnattendedEnd(delta, session);
        }
        catch (Exception e)
        {
            _quitCalled = true;
            Log.Error(LogCategory, $"autopilot: the unattended end after the fault failed ({e.GetType().Name}); the game quits with exit code {FaultExitCode}");
            Log.FlushToGodot();
            GetTree().Quit(FaultExitCode);
        }
    }
}

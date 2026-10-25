using System.Globalization;
using Godot;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.PortalSpace;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// <c>--login-check=throwaway</c> (m2-plan section 6.9; D59, D61, D65; docs/session.md section 28): the live check that a character can be created, used, deleted, restored and purged
/// through the retail screens and the tools panel, on the test account, leaving the account as it was. The script is <see cref="ThrowawayLegScenario"/>, which acts through the Action API
/// and through <see cref="ThrowawayScreens"/> (this runner's clicks on the real screens); this node steps it once per frame after the session host and play mode, and adds what only the
/// engine can see: the frame times from the moment the new character enters the world to the moment it could use the tools, and through the Places teleport.
/// </summary>
/// <remarks>
/// <para>The verdict line is <c>[logincheck] throwaway done ok=&lt;true|false&gt; throwaway=&lt;purged|failed&gt; name_errors=&lt;n&gt; xp=&lt;added|n/a&gt; where_m=&lt;m|n/a&gt; place=&lt;teleported|n/a&gt;
/// deletes=&lt;n&gt; restores=&lt;n&gt; list=&lt;same|different|n/a&gt; leftover=&lt;yes|no&gt; enter_to_walk_max_ms=&lt;x|n/a&gt; teleport_frame_max_ms=&lt;x|n/a&gt;</c>; the process ends with 0 when it held and 1 when it did not.
/// No frame over 50 ms in either window is judged as in the other legs (every frame near the limit is logged with the stage). Nothing in the verdict names a character or the account.</para>
/// </remarks>
public sealed partial class ThrowawayLegRunner : Node
{
    /// <summary>The longest the check may take, in seconds; at the limit the scenario recovers (logs off, leaves the throwaway pending deletion) and the check fails.</summary>
    public const double WholeCheckSeconds = 600.0;

    /// <summary>How long recovery may take after the limit, in seconds.</summary>
    public const double RecoverySeconds = 120.0;

    private const string LogCategory = "logincheck";
    private const double FrameLimitMilliseconds = 50.0;
    private const double NearLimitMilliseconds = 30.0;
    private const double TeleportTailSeconds = 1.0;
    private const double QuitFlushSeconds = 1.5;

    private SessionHost _host = null!;
    private readonly Lifestone.Client.Session.Hosting.LegFaultGuard _guard = new("ThrowawayLegRunner", LogCategory, GodotLogSink.Instance);
    private double _faultAt = double.MaxValue;
    private PlayMode _mode = null!;
    private RetailUiLayer _ui = null!;
    private ThrowawayLegScenario _scenario = null!;
    private double _runSeconds;
    private double _enterMaxMs;
    private long _enterFrames;
    private long _enterSlow;
    private double _teleportMaxMs;
    private long _teleportFrames;
    private long _teleportSlow;
    private double _teleportTailStart = double.NaN;
    private double _doneAt = double.NaN;
    private bool _aborted;
    private bool _finished;

    /// <summary>Adds the check to the scene; it starts with the scene's first frame.</summary>
    public static void Install(Main main, GameServices services, SessionHost host, PlayMode mode, RetailUiLayer? ui)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(mode);
        if (ui is null)
        {
            Log.Error(LogCategory, "the throwaway check clicks the retail screens, and the retail UI is off (--retail-ui=off); it cannot run");
            main.GetTree().Quit(1);
            return;
        }

        // The log off is asked for with retail's key, Shift+Escape, as the other login checks do.
        Func<ActionHandle?>? logOffKey = mode.EndKey is null ? null : () => PressLogOffKey(ui, mode.EndKey);
        var session = host.Session ?? throw new InvalidOperationException("the throwaway check needs the session host's session");
        // M4 D43: on the tools account the purge caller is Lifestone Tools (created through the Action API when absent); on test it is Lifestone Smoke.
        var onTools = string.Equals(host.Options.Account, Lifestone.Client.Session.Credentials.TestAccounts.Tools, StringComparison.Ordinal);
        var dats = onTools ? services.Dats ?? throw new InvalidOperationException("the throwaway check on the tools account needs the dats to create its purge caller") : null;
        var scenario = new ThrowawayLegScenario(
            new ThrowawayScreens(ui, session),
            logOffKey,
            message => Log.Info(LogCategory, "throwaway: " + message),
            caller: onTools ? ThrowawayLegScenario.Tools : ThrowawayLegScenario.Smoke,
            callerIntent: onTools ? () => Lifestone.Client.Session.Scenarios.HeadlessCharacter.Intent(ThrowawayLegScenario.Tools, Lifestone.Client.Session.Scenarios.HeadlessCharacter.RetailSkillClasses(dats!)) : null);
        var runner = new ThrowawayLegRunner
        {
            Name = "ThrowawayLegRunner",
            ProcessPriority = 100, // after the session host (-900), play mode (-800) and the retail UI
            _host = host,
            _mode = mode,
            _ui = ui,
            _scenario = scenario,
        };
        main.AddChild(runner);
        Log.Info(
            LogCategory,
            "login check 'throwaway' installed: the test account creates, uses, deletes, restores and purges one character through the retail screens, then the game quits",
            ("scenario", ThrowawayLegScenario.ScenarioName));
    }

    private static ActionHandle? PressLogOffKey(RetailUiLayer ui, EndSessionKey key)
    {
        var before = key.Requests;
        var input = ui.Manager.Input;
        input.KeyDown(DikScanCodes.LShift);
        input.KeyDown(DikScanCodes.Escape);
        input.KeyUp(DikScanCodes.Escape);
        input.KeyUp(DikScanCodes.LShift);
        return key.Requests == before + 1 ? key.Last : null;
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        try
        {
            StepFrame(delta);
        }
        catch (Exception e) when (Lifestone.Client.Session.Hosting.LegFaultGuard.Guards(e))
        {
            // M5 item 0 c2: reported once; the scenario's own recovery (the abort path of the time limit: log off if in the world) then runs and the verdict is ok=false.
            if (_guard.Catch($"_Process (stage {_scenario.Stage})", e))
            {
                _faultAt = _runSeconds;
                _aborted = true;
                if (_host.Session is { } session)
                {
                    _scenario.Abort(session, _guard.Reason!);
                }
                else
                {
                    Finish(false, _guard.Reason);
                }
            }
            else
            {
                Finish(false, _guard.Reason);
            }
        }
    }

    private void StepFrame(double delta)
    {
        if (_finished)
        {
            return;
        }

        _runSeconds += delta;
        if (_host.Session is not { } session)
        {
            Finish(false, "the session host has no session");
            return;
        }

        if (!_aborted)
        {
            SampleFrame(delta); // M5 0 fix 0-R5: not after a fault or the time limit, so a fault in the sampling cannot repeat and end the run before the abort's log off completes
        }

        if (!double.IsNaN(_doneAt))
        {
            if (_runSeconds - _doneAt >= QuitFlushSeconds)
            {
                Finish(true, null);
            }

            return;
        }

        if (!_aborted && _runSeconds > WholeCheckSeconds)
        {
            _aborted = true;
            _scenario.Abort(session, $"the check took more than {WholeCheckSeconds:0} s");
        }
        else if (_aborted && _runSeconds > Math.Min(WholeCheckSeconds, _faultAt) + RecoverySeconds)
        {
            Finish(false, "recovery after the time limit did not finish");
            return;
        }

        switch (_scenario.Step(session, PortalSpaceSetup.Now()))
        {
            case ScenarioStatus.Failed:
                Finish(false, _scenario.Failure ?? "the scenario failed");
                break;
            case ScenarioStatus.Succeeded:
                _doneAt = _runSeconds;
                break;
        }
    }

    /// <inheritdoc/>
    public override void _EnterTree() => GetTree().ProcessFrame += StepGcWatch;

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        GetTree().ProcessFrame -= StepGcWatch;
        _scenario?.Dispose();
    }

    // M4 P5b (TODO(m4-throwaway-teleport-frame)): the watch is read when the tree starts a frame's processing, before any node's, so its interval is the one the delta measures. Read in this node's own
    // _Process (priority 100, after the player's) it missed a collection that ran between the player's process and this node's in the frame the delta measures: the frame lines said gc_pause_ms=0 for a
    // frame whose player line had a 62 ms pause.
    private void StepGcWatch() => _gcWatch.Step();

    private readonly GcFrameWatch _gcWatch = new();

    /// <summary>
    /// The frame times of the two windows the other legs report: from the moment the new character began entering the world to the end of the settle (<c>enter_to_walk_max_ms</c>),
    /// and from the Places click to a second after the player is present again at Holtburg (<c>teleport_frame_max_ms</c>). The engine's delta is the previous frame's whole time.
    /// </summary>
    private void SampleFrame(double delta)
    {
        var ms = delta * 1000.0;
        var gc = _gcWatch.Last; // the collector's work in the interval the delta measures (M4 P4 g3fix, TODO(m4-throwaway-teleport-frame)); stepped at the frame's start (StepGcWatch)
        var enterWindow = !double.IsNaN(_scenario.EnterAt) && double.IsNaN(_scenario.SettledAt);
        var teleportWindow = !double.IsNaN(_scenario.PlaceAskedAt) && (double.IsNaN(_scenario.PlaceArrivedAt) || InTail());
        if (!double.IsNaN(_scenario.PlaceArrivedAt) && double.IsNaN(_teleportTailStart))
        {
            _teleportTailStart = _runSeconds;
        }

        if (enterWindow)
        {
            _enterFrames++;
            _enterMaxMs = Math.Max(_enterMaxMs, ms);
            _enterSlow += ms > FrameLimitMilliseconds ? 1 : 0;
        }

        if (teleportWindow)
        {
            _teleportFrames++;
            _teleportMaxMs = Math.Max(_teleportMaxMs, ms);
            _teleportSlow += ms > FrameLimitMilliseconds ? 1 : 0;
        }

        if ((enterWindow || teleportWindow) && ms > NearLimitMilliseconds)
        {
            Log.Write(
                ms > FrameLimitMilliseconds ? LogLevel.Warning : LogLevel.Info,
                LogCategory,
                ms > FrameLimitMilliseconds ? "throwaway: a frame over 50 ms" : "throwaway: a frame near the 50 ms limit",
                [
                    ("ms", ms),
                    ("stage", _scenario.Stage),
                    ("window", enterWindow ? "enter-to-walk" : "teleport"),
                    ("tunnel", false),
                    ("process_ms_previous_frame", Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0),
                    ("render_setup_cpu_ms", RenderingServer.GetFrameSetupTimeCpu()),
                    .. gc.Fields(),
                    .. gc.LastCollectionFields(),
                ]);
        }

        bool InTail() => double.IsNaN(_teleportTailStart) || _runSeconds - _teleportTailStart <= TeleportTailSeconds;
    }

    private void Finish(bool ok, string? why)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        var inv = CultureInfo.InvariantCulture;
        var enterMeasured = _enterFrames > 0 && !double.IsNaN(_scenario.SettledAt);
        var teleportMeasured = _teleportFrames > 0;
        var framesHeld = (!enterMeasured || _enterMaxMs <= FrameLimitMilliseconds) && (!teleportMeasured || _teleportMaxMs <= FrameLimitMilliseconds);
        var held = ok && _scenario.Ok && enterMeasured && teleportMeasured && framesHeld;
        if (!held)
        {
            Log.Error(LogCategory, "the throwaway check failed", ("stage", _scenario.Stage), ("why", why ?? _scenario.Failure ?? (framesHeld ? "a verdict field is not as expected" : "a frame was over 50 ms")), ("recovered", _scenario.Recovered));
        }

        var line = string.Create(
            inv,
            $"throwaway done ok={(held ? "true" : "false")} {_scenario.VerdictFields()} enter_to_walk_max_ms={(enterMeasured ? _enterMaxMs.ToString("0.0", inv) : "n/a")} teleport_frame_max_ms={(teleportMeasured ? _teleportMaxMs.ToString("0.0", inv) : "n/a")} slow_frames={_enterSlow + _teleportSlow}");
        Log.Info(LogCategory, line);
        Log.Info(
            LogCategory,
            "throwaway detail",
            ("name_error_dialogs", string.Join(" | ", _scenario.NameErrorTexts)),
            ("enter_frames", _enterFrames),
            ("teleport_frames", _teleportFrames),
            ("seconds", _runSeconds),
            ("ui_modes", _ui.Flow?.Flow.CurrentMode ?? 0u));
        Log.FlushToGodot();
        GetTree().Quit(held ? 0 : 1);
    }
}

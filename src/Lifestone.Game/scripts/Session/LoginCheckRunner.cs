using System.Globalization;
using Godot;
using Lifestone.Client.Audio;
using Lifestone.Client.Avatars;
using Lifestone.Client.Play;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Navigation;
using Lifestone.Client.World;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Tools;
using Lifestone.Data.Physics;
using Lifestone.Data.World;
using Lifestone.Game.Audio;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.PortalSpace;
using Lifestone.Game.Render.Particles;
using Lifestone.Game.RetailUi;
using Lifestone.Game.World.Objects;

namespace Lifestone.Game.Sessions;

/// <summary>
/// <c>--login-check=enter</c> (m2-plan G6 W12; docs/session.md section 21): the whole client path of a first login against the bundled ACE, off-screen,
/// on the test account. The script is <see cref="LoginCheckScenario"/> (data check, character list, select or create, Enter, the arrival in the Training
/// Academy, LoginComplete once, the unhide, a 5 s wait, a log off back to the character list, quit); this node steps it once per frame after the session
/// host and play mode, and adds what only the engine can see: the retail screens the run went through, the player's body, portal space's LoginComplete
/// count and the frame times between Enter and the first moment the player could walk.
/// </summary>
/// <remarks>
/// <para>M3 1.3 s3: the line ends with the report-only <c> sounds_server= sounds_hooks= sounds_ui= ambient=</c> (<see cref="LoginCheckScenario.SoundFields"/>): the counters per leg (<see cref="SoundLegTally"/>, fix B: <c>sounds_server</c> in the Academy arrival, <c>ambient</c> in the Holtburg laps, <c>sounds_hooks</c> and <c>sounds_ui</c> since the Enter;
/// <c>sounds_server</c> and <c>sounds_hooks</c> are voices started, the <c>*_handed</c> fields are calls handed to the sound player), each <c>n/a</c> when the run has no such counter, never 0
/// (M3 2.1 s3: <c>sounds_hooks</c> over the walking legs is judged, at least 1 (<see cref="LoginCheckScenario.JudgeWalkingSounds"/>); the Academy arrival's <c>sounds_server</c> stays a report, D27 judges it in the buff leg,
/// <see cref="BuffLegRunner"/>; <c>sounds_ui</c> is judged in <c>--retail-ui-checks</c> only).</para>
/// <para>M5 0 c1 (<c>m5-object-cull</c>): the line ends with the report-only <c> objects= drawn_in_cell= light_rows=</c> (<see cref="LoginCheckScenario.ObjectFields"/>): the most objects the session's table held,
/// the most avatars drawn in interior cells and the most object light-table rows in use at once, each <c>n/a</c> when the run did not reach the world.</para>
/// <para>g7: the line ends with <c>shell=ok|missing vitals=&lt;h&gt;/&lt;s&gt;/&lt;m&gt; (read from the shell's meters) spew_portal=yes|no vitals_window=stacked|side</c> (<see cref="LoginCheckShell"/>, <see cref="ShellWatch"/>): all four are judged (docs/session.md section 27).</para>
/// <para>The verdict line is <c>[logincheck] done ok=&lt;true|false&gt; enter_to_walk_max_ms=&lt;x&gt; teleports=&lt;n&gt; academy_objects=&lt;n&gt; logoff=&lt;ok|failed&gt; materialised=&lt;true|false|n/a&gt; objects_drawn=&lt;n|n/a&gt;</c>
/// (fields the run did not reach are <c>n/a</c>; <c>academy_objects</c> is the census of the world content the server placed, <see cref="Lifestone.Client.Avatars.WorldContentCensus"/>;
/// <c>materialised</c> says the server created the player hidden, unhid it, its body was drawn and the player's own Hidden-script particles were handed to the renderer (an emitter in view that is not the player does not count: <see cref="Lifestone.Game.World.ServerObjects.ServerObjectParticles.PlayerObjectsLastFrame"/>);
/// <c>fade_ms</c> is the player's materialise fade measured on frames, from the first frame the player is visible after the unhide to the first opaque one (UnHide script 0x3300032F's
/// 0.75 s hook; judged between <see cref="FadeMinMilliseconds"/> and <see cref="FadeMaxMilliseconds"/>, with a fade seen through intermediate alpha and no opaque frame before it; docs/avatars.md section 17);
/// <c>objects_drawn</c> is the most server objects the layer drew at once), then the process ends with 0 when it held and 1 when it did not. A failure also logs its reason as an
/// ERROR. The frame time is judged (DoD 15: no frame over 50 ms between Enter and walking; every frame over it is logged with what it held): the longest frame from
/// the moment the retail character list has been up for a few frames to the end of the wait is <c>enter_to_walk_max_ms</c>. Nothing here names the account, the
/// character or any password in the verdict.</para>
/// </remarks>
public sealed partial class LoginCheckRunner : Node
{
    /// <summary>The longest the <c>enter</c> check may take, in seconds (the scenario's own stage limits are shorter).</summary>
    public const double WholeCheckSeconds = 300.0;

    /// <summary>The longest a check with movement legs may take, in seconds (each leg has its own limits; the Academy exit, a Holtburg lap set and their teleports take a few minutes).</summary>
    public const double WholeLegsCheckSeconds = 900.0;

    /// <summary>The frame rate the Holtburg laps must keep in every one-second window (DoD 15).</summary>
    public const double HoltburgMinimumFps = 60.0;

    /// <summary>How long after the player is present again a server teleport's window of frames stays open, in seconds (the arrival's work is spread over about a second).</summary>
    private const double TeleportWindowTailSeconds = 1.0;

    /// <summary>How long a failed leg may take to put the character back at its arrival position before the check quits anyway, in seconds.</summary>
    private const double RecoverySeconds = 60.0;

    /// <summary>How long the recovery waits for the log off that follows it, in seconds.</summary>
    private const double LogOffRecoverySeconds = 20.0;

    /// <summary>How near the arrival position the recovery must bring the character, in metres.</summary>
    private const float RecoveryToleranceMetres = 3f;

    private const string LogCategory = "logincheck";
    private const double FrameLimitMilliseconds = 50.0;

    /// <summary>A frame over this (and within the limit) is logged with what it held, so the margin of the 50 ms rule shows (DoD 15).</summary>
    private const double NearLimitMilliseconds = 30.0;
    private const double AfterLogOffSeconds = 3.0;
    private const int ShotDelayFrames = 3;

    /// <summary>
    /// The shortest and longest measured fade the verdict accepts, in milliseconds. The hook is 750 ms from the update after the unhide; the measure starts on the first visible frame
    /// (the one before the hook applies) and ends on the first frame at or past the end, so it reads 750 plus one to two frames; a frame can be up to 50 ms (the arrival's rule) at each
    /// end. 650 to 900 ms leaves the fade's own length binding while tolerating a heavy frame at either end.
    /// </summary>
    internal const double FadeMinMilliseconds = 650.0;

    /// <inheritdoc cref="FadeMinMilliseconds"/>
    internal const double FadeMaxMilliseconds = 900.0;
    private const int ListSettleFrames = 3;
    private const double TunnelShotSeconds = 1.0;

    /// <summary>How long after the Academy settle the placed objects may take to all be drawn (their meshes build on workers), in seconds; a mismatch that outlasts it fails the run.</summary>
    private const double CensusWaitSeconds = 8.0;

    /// <summary>How often the drawn census is taken, in seconds.</summary>
    private const double CensusIntervalSeconds = 0.5;

    /// <summary>The arrival's portal-space sequence, shortest, in seconds, from the player's create to the end of the world's expansion (docs/portal-space.md section 3: 1 s expand, 2 s hold, 1 s collapse, 1 s world expand, plus the load).</summary>
    private const double ArrivalSequenceMinimumSeconds = 5.0;

    /// <summary>The arrival tunnel's shortest visible time, in seconds: the expand, the hold and the collapse (1 + 2 + 1); the last second of the sequence shows the world.</summary>
    private const double ArrivalTunnelMinimumSeconds = 4.0;

    /// <summary>Frames the tunnel may show behind the shell's black backdrop (the frame order between the presenter and the layer is one frame).</summary>
    private const int TunnelCoveredFramesAllowed = 1;

    /// <summary>The longest the logoff tunnel may be down between its first showing and the character list's screen, in seconds (the frame between the machine's reset and the screen change).</summary>
    private const double LogOffTunnelGapToleranceSeconds = 0.25;
    private const double ListShotDelaySeconds = 1.0;
    private const string ShotFolder = "docs/screenshots/m2/login";

    private readonly List<uint> _modes = [];
    private GameServices _services = null!;
    private SessionHost _host = null!;
    private readonly Lifestone.Client.Session.Hosting.LegFaultGuard _guard = new("LoginCheckRunner", LogCategory, GodotLogSink.Instance);
    private PlayMode _mode = null!;
    private PortalSpaceHandles? _portal;
    private RetailUiLayer? _ui;
    private LoginCheckScenario _scenario = null!;
    private LoginCheckKind _kind;
    private double _wholeSeconds = WholeCheckSeconds;
    private double _fpsWindowSeconds;
    private int _fpsWindowFrames;
    private double _holtburgMinFps = double.NaN;
    private double _holtburgMaxFrameMs;
    private int _holtburgObjectsDrawnMax;
    private int _holtburgNpcsPlacedMax;
    private int _holtburgNpcsDrawnMax;
    private double _holtburgCensusAt = double.NaN;
    private DrawnCensus? _holtburgCensus;
    private double _academyCensusStart = double.NaN;
    private double _academyCensusAt = double.NaN;
    private DrawnCensus? _academyCensus;
    private bool _academyCensusDone;
    private bool _teleportWindow;
    private double _teleportTailSeconds;
    private double _teleportFrameMaxMs;
    private long _teleportWindowFrames;
    private long _teleportSlowFrames;
    private double _recoveryStart = double.NaN;
    private ActionHandle? _recoveryHandle;
    private string? _recoveryReason;
    private ActionHandle? _recoveryLogOff;
    private double _recoveryLogOffAt;
    private bool _recoveryBack;
    private UiFlow? _watchedFlow;
    private double _runSeconds;
    private double _maxFrameMs;
    private long _slowFrames;
    private FrameParts _lastParts;
    private FrameParts _worstParts;
    private double _worstPartsMs;
    private readonly PipelineCompilationWatch _pipelines = new();
    private readonly GcFrameWatch _gcWatch = new();
    private readonly SoundLegTally _soundLegs = new();
    private int _lastAudioStarts;
    private int _lastAudioArrived;
    private int _lastLivingMeshes;
    private FrameAudioWork _frameAudio;
    private PipelineCompilations _windowCompiled;
    private bool _rigAtPresent;
    private bool _rigGoneAtEnd;
    private double _doneAt = double.NaN;
    private long _movementHeld;
    private bool _finished;
    private long _frame;
    private long _listFrame = -1;
    private readonly List<Shot> _shots = [];
    private int _shotsSaved;
    private int _shotsPending;
    private double _shotReadbackMaxMs;
    private double _tunnelSeconds;
    private double _arrivalTunnelSeconds;
    private double _arrivalSequenceSeconds;
    private int _tunnelCoveredFrames;
    private int _presenterFramesSeen;
    private int _enterCues;
    private int _exitCues;
    private int _humEvents;
    private bool _logOffTunnelSeen;
    private double _logOffTunnelSeconds;
    private double _logOffTunnelGapSeconds;
    private bool _tunnelShot;
    private bool _arrivalShot;
    private bool _listShot;
    private bool _settleShot;
    private bool _fadeSawHidden; // D63: the player's materialise fade as the frames show it
    private double _fadeUnhideAt = double.NaN;
    private double _fadeMs = double.NaN;
    private int _fadeIntermediate;
    private int _fadeTwinFrames; // of those, frames with parts drawn through the blended twin (S4)
    private bool _fadeTwinMissing; // TODO(g5-translucency-twin) was logged: a fading part was drawn solid
    private bool _fadePopped;
    private bool _fadeShot;
    private readonly ShellWatch _shell = new();
    private double _listSeenAt = double.NaN;
    private int _playerPartsMax;
    private int _objectsDrawnMax;
    private int _inCellMax = -1;
    private int _lightRowsMax = -1;
    private int _objectsWaitingMax;
    private int _particleObjectsMax;
    private int _playerParticleObjectsMax;
    private int _particleBatchesMax;
    private long _hiddenCreates;
    private long _unhides;
    private long _adapterApplied;
    private long _adapterFailed;
    private long _adapterUnknown;
    private int _avatarCountMax;
    private long _meshesBuilt;
    private double _meshAttachMaxMs;

    /// <summary>Adds the check to the scene; it starts with the scene's first frame.</summary>
    public static void Install(Main main, GameServices services, SessionHost host, PlayMode mode, PortalSpaceHandles? portal, RetailUiLayer? ui, LoginCheckKind kind = LoginCheckKind.Enter)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(mode);
        if (kind == LoginCheckKind.Throwaway)
        {
            ThrowawayLegRunner.Install(main, services, host, mode, ui); // a separate check with its own script and verdict (never part of 'all')
            return;
        }

        if (kind == LoginCheckKind.Keep)
        {
            KeepLegRunner.Install(main, services, host, mode, ui); // a kind of its own (M5 6 k2): one process, two logins of Lifestone Throwaway, an option and a window kept across them, never part of 'all'
            return;
        }

        if (kind == LoginCheckKind.Buff)
        {
            BuffLegRunner.Install(main, services, host, mode, ui); // a kind of its own (docs/session.md section 30): BSD Mage's buff cycle, never part of 'all'
            return;
        }

        if (kind == LoginCheckKind.BsdFight)
        {
            FightLegRunner.Install(main, services, host, mode, ui); // a kind of its own (M3 3.1): BSD Mage's fight in the Aluvian den, never part of 'all'
            return;
        }

        if (kind == LoginCheckKind.Hunt)
        {
            HuntLegRunner.Install(main, services, host, mode, ui); // a kind of its own (M4 1.2 s4b): Holtburg Hunter's hunt on the hunt grounds, never part of 'all'
            return;
        }

        if (kind == LoginCheckKind.Journey)
        {
            JourneyLegRunner.Install(main, services, host, mode, ui); // a kind of its own (M4 P5b J5): a journey's stations in one login on foot, never part of 'all'
            return;
        }

        if (kind == LoginCheckKind.Autopilot)
        {
            AutopilotLegRunner.Install(main, services, host, mode, ui); // a kind of its own (M5 4 h1): BSD Mage's autopilot until its stop rules, never part of 'all'
            return;
        }

        if (kind == LoginCheckKind.BsdDeath)
        {
            DeathLegRunner.Install(main, services, host, mode, ui); // a kind of its own (M3 3.2): BSD Mage's death and re-gear in the Aluvian den, never part of 'all'
            return;
        }

        var dats = services.Dats ?? throw new InvalidOperationException("the login check needs the dats (Main checks for them first)");
        // The log off is asked for with retail's key, Shift+Escape (End Character Session), through the retail UI's input router: the key's whole path is checked live.
        Func<ActionHandle?>? logOffKey = ui is null || mode.EndKey is null ? null : () => PressLogOffKey(ui, mode.EndKey);
        var legs = new List<RoutePlay>();
        if (kind != LoginCheckKind.Enter)
        {
            // The movement legs drive the game's own player (its walker, collision streaming, teleports and keys' controller) through the scenario host: the scenarios plan
            // their routes in worlds of their own (built here, a few seconds) and walk the player in the game's physics world.
            var scenarioHost = new RoutePlayHost(() => mode.Rig?.Controller.Walker, () => mode.MovementGate?.Held ?? 0);
            mode.ScenarioHost = scenarioHost;
            var log = GodotLogSink.Instance;
            if (kind is LoginCheckKind.AcademyExit or LoginCheckKind.All)
            {
                // The exit portal is quest-gated in ACE's world database: the leg stamps the Sentry's quest through the mod when the test character lacks it, takes the portal and erases it again.
                legs.Add(new AcademyExitScenario(dats, log, portalCell: PortalExit.ExitToHoltburgCell, host: scenarioHost).Play);
            }

            if (kind is LoginCheckKind.HoltburgLaps or LoginCheckKind.All)
            {
                legs.Add(new HoltburgLapsScenario(dats, log, host: scenarioHost, jumps: true).Play);
            }
        }

        var scenario = new LoginCheckScenario(HeadlessCharacter.RetailSkillClasses(dats), host.Options.Character, requestLogOff: logOffKey, legs: legs) { FighterTemplate = FighterTemplate.ForAccount(host.Options.Account) ?? FighterTemplate.Hunter };
        var runner = new LoginCheckRunner
        {
            Name = "LoginCheckRunner",
            ProcessPriority = 100, // after the session host (-900) and play mode (-800): it looks at what they did this frame
            _services = services,
            _host = host,
            _mode = mode,
            _portal = portal,
            _ui = ui,
            _scenario = scenario,
            _kind = kind,
            _wholeSeconds = legs.Count == 0 ? WholeCheckSeconds : WholeLegsCheckSeconds,
        };
        main.AddChild(runner);
        Log.Info(
            LogCategory,
            $"login check '{KindName(kind)}' installed: the test account's whole first login runs off-screen{(legs.Count == 0 ? string.Empty : ", then " + string.Join(" and ", legs.Select(l => l.Name)) + " with the game's own player")}, then the game quits",
            ("scenario", LoginCheckScenario.ScenarioName));
    }

    private static string KindName(LoginCheckKind kind) => kind switch
    {
        LoginCheckKind.AcademyExit => "academy-exit",
        LoginCheckKind.HoltburgLaps => "holtburg-laps",
        LoginCheckKind.All => "all",
        _ => "enter",
    };

    /// <summary>Presses and releases Shift+Escape on the retail UI's router; returns the log off the key asked for, or null when it asked for none.</summary>
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
            // M5 0 fix 0-R5: after the first fault only the fail path runs (StepAfterFault), never the sampling and observer code that may have thrown: a repeat fault there
            // ended the run before the log off or the place-back completed.
            if (_guard.Tripped)
            {
                StepAfterFault(delta);
            }
            else
            {
                StepFrame(delta);
            }
        }
        catch (Exception e) when (Lifestone.Client.Session.Hosting.LegFaultGuard.Guards(e))
        {
            // M5 item 0 c2: reported once; a fault in the world takes the legs' failure path (the character is sent back and logged off), else the check ends as failed now.
            // M5 0 fix 0-R5: a fault outside the playing stage, with the character in the world, logs it off first. A fault inside the fail path itself ends the run.
            if (_guard.Catch($"_Process (stage {_scenario.Stage})", e))
            {
                try
                {
                    StartFailPath(_guard.Reason!);
                }
                catch (Exception failPathFault) when (Lifestone.Client.Session.Hosting.LegFaultGuard.Guards(failPathFault))
                {
                    Log.Error(LogCategory, "the fail path itself threw; ending the check", ("type", failPathFault.GetType().Name), ("message", failPathFault.Message));
                    Finish(false, _guard.Reason);
                }
            }
            else
            {
                Finish(false, _guard.Reason);
            }
        }
    }

    /// <summary>The first fault: a fault in the world while playing sends the character back and logs it off; a fault elsewhere in the world logs it off; otherwise the check ends as failed now.</summary>
    private void StartFailPath(string reason)
    {
        if (_host.Session is not { } session || !double.IsNaN(_recoveryStart) || _recoveryLogOff is not null)
        {
            Finish(false, reason);
        }
        else if (_scenario.Stage == "playing")
        {
            StartRecovery(session, reason);
        }
        else if (double.IsNaN(_doneAt) && session.State.Self.Travel != SelfTravel.NotInWorld)
        {
            _recoveryReason = reason;
            _recoveryBack = true; // no place-back is attempted from this stage: the verdict must not say the character could not be put back
            _recoveryLogOff = session.Actions.LogOff(ActionSource.Host);
            _recoveryLogOffAt = _runSeconds;
            Log.Warn(LogCategory, "a fault outside the playing stage; logging the character off before the verdict", ("stage", _scenario.Stage));
        }
        else
        {
            Finish(false, reason);
        }
    }

    /// <summary>The frames after the first fault: only the recovery steps (the place-back, then the log off) run, with the run's own time limit; nothing samples or observes.</summary>
    private void StepAfterFault(double delta)
    {
        if (_finished)
        {
            return;
        }

        _runSeconds += delta;
        if (_host.Session is not { } session)
        {
            Finish(false, _guard.Reason);
        }
        else if (!double.IsNaN(_recoveryStart))
        {
            StepRecovery(session);
        }
        else if (_recoveryLogOff is not null)
        {
            if (_recoveryLogOff.Outcome is not null || _runSeconds - _recoveryLogOffAt > LogOffRecoverySeconds)
            {
                Finish(false, _recoveryReason);
            }
        }
        else
        {
            Finish(false, _guard.Reason); // the fail path had nothing to run
        }
    }

    private void StepFrame(double delta)
    {
        if (_finished)
        {
            return;
        }

        _runSeconds += delta;
        _frame++;
        // What this frame's steps held, taken now that the session host (-900), play mode (-800) and the retail UI (0) have run; the engine's delta below is the PREVIOUS frame's whole
        // time, so a slow-frame line names the parts of the frame before (TODO(login-burst-frame)).
        var partsBefore = _lastParts;
        _lastParts = TakeParts();
        WatchModes();
        _shell.Step(_ui);
        Sample(delta);
        var compiled = _pipelines.Step();
        var gc = _gcWatch.Step();
        TakeDueShots();
        if (_host.Session is not { } session)
        {
            Finish(false, "the session host has no session");
            return;
        }

        TakeAudioWork();
        StepSounds();
        TrackLegFrames(delta, session, compiled);

        if (!double.IsNaN(_scenario.EnterAt) && double.IsNaN(_scenario.SettledAt) && _listFrame >= 0 && _frame >= _listFrame + ListSettleFrames)
        {
            // Enter to walk: every frame from the one that issued Enter to the one where the player has been present for the whole wait, except the first
            // few after the retail character list appeared: the check presses Enter in the very frame that screen is built (a person presses it later), and
            // building the screen is the retail UI's own cost (G2), not the entering's.
            var ms = delta * 1000.0;
            _maxFrameMs = Math.Max(_maxFrameMs, ms);
            _windowCompiled = _windowCompiled.Plus(compiled);
            if (compiled.Foreground > 0 || compiled.Specialization > 0)
            {
                // A first-use shader or pipeline compile: its frame names the stage, so the variant can be found and warmed at start-up (ServerObjectsWarmUp, docs/session.md
                // section 23). Canvas, mesh, surface and draw compiles can stall a frame (a warning); a specialization compile runs in the background (information).
                Log.Write(
                    compiled.Foreground > 0 ? LogLevel.Warning : LogLevel.Info,
                    LogCategory,
                    "pipeline compilation between Enter and walking",
                    [
                        ("ms", ms),
                        ("stage", _scenario.Stage),
                        ("seconds_since_enter", PortalSpaceSetup.Now() - _scenario.EnterAt),
                        ("tunnel", _portal?.Director.Machine.TunnelVisible ?? false),
                        ("rig", _mode.Rig is not null),
                        ("player_parts_drawn", _mode.Rig?.View.DrawnParts ?? 0),
                        .. compiled.Fields(),
                    ]);
            }

            if (ms > NearLimitMilliseconds)
            {
                var overLimit = ms > FrameLimitMilliseconds;
                if (overLimit)
                {
                    _slowFrames++;
                }

                if (ms >= _worstPartsMs)
                {
                    _worstPartsMs = ms;
                    _worstParts = partsBefore;
                }

                Log.Write(
                    overLimit ? LogLevel.Warning : LogLevel.Info,
                    LogCategory,
                    overLimit ? "frame over 50 ms between Enter and walking (TODO(login-burst-frame): the parts below are the frame's own, the burst frame is still unbounded)" : "frame near the 50 ms limit between Enter and walking (TODO(login-burst-frame))",
                    [
                        ("ms", ms),
                        ("stage", _scenario.Stage),
                        ("seconds_since_enter", PortalSpaceSetup.Now() - _scenario.EnterAt),
                        ("process_ms_previous_frame", Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0),
                        ("render_setup_cpu_ms", RenderingServer.GetFrameSetupTimeCpu()),
                        ("tunnel", _portal?.Director.Machine.TunnelVisible ?? false),
                        ("rig", _mode.Rig is not null),
                        ("adapter_pending", _mode.Adapter?.Pending ?? -1),
                        .. partsBefore.Fields(),
                        .. _frameAudio.Fields(),
                        .. gc.Fields(),
                        ("physics_ms_previous_frame", Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0),
                        .. compiled.Fields(),
                    ]);
            }
        }

        if (!double.IsNaN(_doneAt))
        {
            // The scenario is over: give the retail screens a moment to show the character list again (and take its frame), then judge.
            if (CharacterManagementAfterGame())
            {
                if (double.IsNaN(_listSeenAt))
                {
                    _listSeenAt = _runSeconds;
                }

                if (!_listShot && _runSeconds >= _listSeenAt + ListShotDelaySeconds)
                {
                    // The list's first frames are the screen coming up (black); a second later it is whole.
                    _listShot = true;
                    RequestShot("04-character-list-after-logoff", "the retail character list after the log off");
                }
            }

            if ((_listShot && _shots.Count == 0 && Volatile.Read(ref _shotsPending) == 0) || _runSeconds - _doneAt > AfterLogOffSeconds + 5.0)
            {
                Judge();
            }

            return;
        }

        if (_runSeconds > _wholeSeconds)
        {
            Finish(false, $"the check took more than {_wholeSeconds:0} s (stage {_scenario.Stage}{(_scenario.CurrentLeg is { } running ? ", " + running.Name + ":" + running.Stage : string.Empty)})");
            return;
        }

        if (!double.IsNaN(_recoveryStart))
        {
            StepRecovery(session);
            return;
        }

        if (_mode.MovementGate is { } gate)
        {
            _movementHeld = gate.Held;
        }

        if (_scenario.Stage == "settling" && !_rigAtPresent)
        {
            _rigAtPresent = _mode.Rig is { } rig && rig.Controller.Avatar.Guid == _scenario.EnteredGuid;
        }

        if (!_settleShot && !double.IsNaN(_scenario.SettledAt))
        {
            // g7: the shell over the Academy once the wait is over: the vitals filled from the PlayerDescription, the radar's blips of the objects the server placed, the chat and the spew box.
            _settleShot = true;
            RequestShot("03-shell-after-the-settle", "the in-game shell in the Academy after the wait: vitals, radar blips, chat and spew box");
            Log.Info(LogCategory, "the shell after the settle", ("vitals", _scenario.Vitals), ("vitals_on_screen", _shell.VitalsOnScreen), ("vitals_max", LoginCheckShell.VitalMaximums(session.State.Self.Qualities)), ("vitals_window_chosen", _scenario.VitalsWindow), ("vitals_window_shown", _shell.ShownWindow), ("radar_blips_max", _shell.RadarBlipsMax), ("spew_portal", _shell.SpewPortal), ("shell_frames", _shell.Frames), ("bare_world_frames", _shell.BareWorldFrames));
        }

        if (_scenario.Stage == "settling" && !_arrivalShot && PortalSpaceSetup.Now() - _scenario.PresentAt >= _scenario.SettleSeconds - 1.5)
        {
            _arrivalShot = true;
            RequestShot("02-arrival-among-academy-objects", "the player present in the Academy among the objects the server placed, the shell around the world");
        }

        var status = _scenario.Step(session, PortalSpaceSetup.Now());
        switch (status)
        {
            case ScenarioStatus.Failed:
                if (_scenario.Stage == "playing")
                {
                    StartRecovery(session, _scenario.Failure ?? "a leg failed");
                }
                else
                {
                    Finish(false, _scenario.Failure ?? "the scenario failed");
                }

                break;
            case ScenarioStatus.Succeeded:
                _doneAt = _runSeconds;
                break;
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        _scenario?.Dispose();
        if (_watchedFlow is not null)
        {
            _watchedFlow.ModeChanged -= OnModeChanged;
        }
    }

    /// <summary>
    /// A leg failed in the world: the character is sent back to where it arrived (the check's start) before the verdict, so a failed run leaves the test character
    /// where the next one expects it. Quits anyway after <see cref="RecoverySeconds"/>.
    /// </summary>
    private void StartRecovery(IGameSessionView session, string why)
    {
        _recoveryReason = why;
        if (_scenario.ArrivalPosition is not { } home || session.State.Self.Position is null)
        {
            Finish(false, why);
            return;
        }

        Log.Warn(LogCategory, "a leg failed; sending the character back to where it arrived before the verdict", ("cell", $"0x{home.CellId:X8}"), ("why", why));
        var destination = new TeleportDestination(
            "check start",
            [],
            new WorldLocation("check start", new CellPosition(home.CellId, home.Frame.Origin), HasHeight: true, HeadingDegrees: RetailFrames.GetHeading(home.Frame)),
            "login-check");
        _recoveryStart = _runSeconds;
        _recoveryHandle = session.Tools.Place(destination, ActionSource.Host);
    }

    private void StepRecovery(IGameSessionView session)
    {
        var home = _scenario.ArrivalPosition!.Value;
        var state = session.State;
        var back = state.Self.Travel == SelfTravel.Present && state.Self.Position is { } at && at.CellId == home.CellId && PositionAgreement.DistanceMetres(at, home) <= RecoveryToleranceMetres;
        var placeFailed = _recoveryHandle?.Outcome is { Kind: not ActionOutcomeKind.Confirmed };
        if (_recoveryLogOff is not null)
        {
            // The character is as far back as it will get: it is logged off too, so the next run's Enter is not refused as "character in world".
            if (_recoveryLogOff.Outcome is not null || _runSeconds - _recoveryLogOffAt > LogOffRecoverySeconds)
            {
                Finish(false, _recoveryReason + (_recoveryBack ? string.Empty : "; and the character could not be put back (use '@lifestone place loc' to return it to the Academy start)"));
            }

            return;
        }

        if (back || placeFailed || _runSeconds - _recoveryStart > RecoverySeconds)
        {
            _recoveryBack = back;
            if (back)
            {
                Log.Info(LogCategory, "the character is back where it arrived", ("seconds", _runSeconds - _recoveryStart));
            }
            else
            {
                Log.Error(LogCategory, "the character could not be put back where it arrived; put it back with the server mod's place verbs", ("seconds", _runSeconds - _recoveryStart));
            }

            _recoveryLogOff = session.Actions.LogOff(ActionSource.Host);
            _recoveryLogOffAt = _runSeconds;
        }
    }

    /// <summary>
    /// The audio and particle work that landed in one frame (M3 1.3 fix B, review 1.3-R1 attribution), written on the slow-frame lines beside <see cref="FrameParts"/> so the
    /// arrival spike is attributed to a stage: voices started (<see cref="AudioService.Starts"/>), wave streams that arrived on the main thread
    /// (<see cref="WaveStreams.Arrived"/>) and living-static particle meshes finished (<see cref="ParticleMeshLibrary.MeshCount"/>).
    /// </summary>
    /// <remarks>
    /// <c>TODO(server-particle-meshes-counter)</c>: the mesh count covers the living statics' library only. The server objects' library (<c>ServerObjectParticles</c>, where the
    /// script tables' meshes finish in the CreateObject burst) is private and exposes no counter; <c>particle_meshes_server</c> is always <c>n/a</c> until it does.
    /// </remarks>
    private readonly record struct FrameAudioWork(int VoicesStarted, int StreamsArrived, int LivingMeshesFinished)
    {
        /// <summary>The log fields; the values are of the frame the line's <c>ms</c> measures (read each frame, so the delta since the previous read).</summary>
        public (string, object?)[] Fields() =>
        [
            ("audio_voices_started_frame", VoicesStarted),
            ("audio_streams_arrived_frame", StreamsArrived),
            ("particle_meshes_finished_frame_living_statics", LivingMeshesFinished),
            ("particle_meshes_server", "n/a (TODO(server-particle-meshes-counter))"),
        ];
    }

    /// <summary>Reads the audio and particle counters and keeps the growth since the previous frame (a counter that went back counts as no work).</summary>
    private void TakeAudioWork()
    {
        var starts = AudioService.Current?.Starts ?? 0;
        var arrived = AudioService.Current?.Streams.Arrived ?? 0;
        var meshes = LivingStaticsSystem.Instance?.Particles?.MeshCount ?? 0;
        _frameAudio = new FrameAudioWork(Math.Max(0, starts - _lastAudioStarts), Math.Max(0, arrived - _lastAudioArrived), Math.Max(0, meshes - _lastLivingMeshes));
        (_lastAudioStarts, _lastAudioArrived, _lastLivingMeshes) = (starts, arrived, meshes);
    }

    /// <summary>
    /// Adds this frame's sound counters to the leg the frame belongs to (<see cref="SoundLegTally"/>): the Academy arrival from the Enter to the settle, then each movement leg,
    /// and nothing before the Enter, so what played on the character list is not counted (M3 1.3 fix B, review 1.3-R2).
    /// </summary>
    private void StepSounds()
    {
        var leg = double.IsNaN(_scenario.EnterAt)
            ? null
            : double.IsNaN(_scenario.SettledAt) ? LoginCheckScenario.SoundArrivalLeg : _scenario.CurrentLeg?.Name ?? LoginCheckScenario.SoundBetweenLegs;
        _soundLegs.Step(leg, AudioService.ReadSources(_mode.HookSounds, _ui?.SoundStats, AmbientSoundNode.Current?.Player));
    }

    /// <summary>
    /// What the movement legs add to the frame measurements: the slowest one-second window of the Holtburg laps (DoD 15), and the frames around every server
    /// teleport (from the moment the server has the player teleporting to <see cref="TeleportWindowTailSeconds"/> after it is present again), each over 50 ms logged with what it held.
    /// </summary>
    private void TrackLegFrames(double delta, IGameSessionView session, PipelineCompilations compiled)
    {
        if (_scenario.Legs.Count == 0 || double.IsNaN(_scenario.SettledAt))
        {
            return;
        }

        var self = session.State.Self;
        var leg = _scenario.CurrentLeg;
        if (leg is { Name: RouteLibrary.HoltburgLapsName, Stage: "walking" } && self.Travel == SelfTravel.Present)
        {
            _holtburgMaxFrameMs = Math.Max(_holtburgMaxFrameMs, delta * 1000.0);
            if (_mode.Layer is { Active: true } holtburgLayer)
            {
                _holtburgObjectsDrawnMax = Math.Max(_holtburgObjectsDrawnMax, holtburgLayer.DrawnObjects);
            }

            if (double.IsNaN(_holtburgCensusAt) || _runSeconds >= _holtburgCensusAt + CensusIntervalSeconds)
            {
                _holtburgCensusAt = _runSeconds;
                if (TakeCensus() is { } census)
                {
                    _holtburgCensus = census;
                    _holtburgNpcsPlacedMax = Math.Max(_holtburgNpcsPlacedMax, census.Placed.Npcs);
                    _holtburgNpcsDrawnMax = Math.Max(_holtburgNpcsDrawnMax, census.Drawn.Npcs);
                }
            }

            _fpsWindowSeconds += delta;
            _fpsWindowFrames++;
            if (_fpsWindowSeconds >= 1.0)
            {
                var fps = _fpsWindowFrames / _fpsWindowSeconds;
                _holtburgMinFps = double.IsNaN(_holtburgMinFps) ? fps : Math.Min(_holtburgMinFps, fps);
                _fpsWindowSeconds = 0;
                _fpsWindowFrames = 0;
            }
        }
        else
        {
            _fpsWindowSeconds = 0;
            _fpsWindowFrames = 0;
        }

        if (leg is null)
        {
            _teleportWindow = false;
            return;
        }

        if (self.Travel != SelfTravel.Present)
        {
            _teleportWindow = true;
            _teleportTailSeconds = 0;
        }
        else if (_teleportWindow)
        {
            _teleportTailSeconds += delta;
            if (_teleportTailSeconds >= TeleportWindowTailSeconds)
            {
                _teleportWindow = false;
            }
        }

        if (!_teleportWindow)
        {
            return;
        }

        var ms = delta * 1000.0;
        _teleportWindowFrames++;
        _teleportFrameMaxMs = Math.Max(_teleportFrameMaxMs, ms);
        if (ms > FrameLimitMilliseconds)
        {
            _teleportSlowFrames++;
            Log.Warn(
                LogCategory,
                "frame over 50 ms around a server teleport",
                [
                    ("ms", ms),
                    ("leg", leg.Name),
                    ("leg_stage", leg.Stage),
                    ("travel", self.Travel.ToString()),
                    ("tail_seconds", _teleportTailSeconds),
                    ("tunnel", _portal?.Director.Machine.TunnelVisible ?? false),
                    ("process_ms_previous_frame", Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0),
                    ("render_setup_cpu_ms", RenderingServer.GetFrameSetupTimeCpu()),
                    .. compiled.Fields(),
                ]);
        }
    }

    /// <summary>The review frames the run requests and saves: the tunnel, the arrival, the shell after the settle and the character list after the log off (tools/smoke.ps1 demands the same count).</summary>
    private const int ReviewFrames = 4;

    /// <summary>
    /// What one frame's steps held, in milliseconds: the session's tick and its pump (messages and time), play mode's step (queued work, the adapter's events) and the retail UI's
    /// step (its worst step of the frame). Written on the slow-frame lines so a failing live frame is attributed from the run that failed (<c>TODO(login-burst-frame)</c>, DoD 15).
    /// </summary>
    private readonly record struct FrameParts(double SessionTickMs, int PumpMessages, double PumpMs, double RetailUiMs, double PlayModeMs, double PlayWorkMs, double PlayAdapterMs, int EventsApplied)
    {
        /// <summary>The log fields, named for the line; the values are of the frame before the one the line's <c>ms</c> measures the end of.</summary>
        public (string, object?)[] Fields() =>
        [
            ("session_tick_ms", SessionTickMs),
            ("pump_messages", PumpMessages),
            ("pump_ms", PumpMs),
            ("retail_ui_step_ms", RetailUiMs),
            ("play_mode_step_ms", PlayModeMs),
            ("play_mode_work_ms", PlayWorkMs),
            ("play_mode_adapter_ms", PlayAdapterMs),
            ("play_mode_events_applied", EventsApplied),
        ];
    }

    private FrameParts TakeParts()
    {
        var uiMs = _ui?.WorstStepMilliseconds ?? 0.0;
        _ui?.ResetWorstStep();
        var session = _host.Session;
        return new FrameParts(
            session?.LastTickMs ?? 0.0,
            session?.LastPumpMessages ?? 0,
            session?.LastPumpMilliseconds ?? 0.0,
            uiMs,
            _mode.LastStepMilliseconds,
            _mode.LastWorkMilliseconds,
            _mode.LastAdapterMilliseconds,
            _mode.LastEventsApplied);
    }

    private void WatchModes()
    {
        if (_ui?.Flow?.Flow is not { } flow)
        {
            return;
        }

        if (!ReferenceEquals(flow, _watchedFlow))
        {
            if (_watchedFlow is not null)
            {
                _watchedFlow.ModeChanged -= OnModeChanged;
            }

            _watchedFlow = flow;
            flow.ModeChanged += OnModeChanged;
        }

        // A mode that came and went inside one frame is caught by the event; the current one is sampled for the very first frames.
        if (_modes.Count == 0 || _modes[^1] != flow.CurrentMode)
        {
            _modes.Add(flow.CurrentMode);
        }

        if (_listFrame < 0 && _modes.Contains(UiModes.CharacterManagement))
        {
            _listFrame = _frame;
        }
    }

    private void OnModeChanged(UiModeChanged change)
    {
        if (_listFrame < 0 && change.Mode == UiModes.CharacterManagement)
        {
            _listFrame = _frame;
        }

        if (_modes.Count == 0 || _modes[^1] != change.Mode)
        {
            _modes.Add(change.Mode);
        }
    }

    private bool CharacterManagementAfterGame()
    {
        var game = _modes.IndexOf(UiModes.GamePlay);
        return game >= 0 && _modes.IndexOf(UiModes.CharacterManagement, game) >= 0;
    }

    private void Judge()
    {
        var problems = new List<string>();
        var first = _modes.IndexOf(UiModes.CharacterManagement);
        var game = _modes.IndexOf(UiModes.GamePlay);
        if (_ui is null)
        {
            problems.Add("the retail UI was not running, so the screens the login went through cannot be checked");
        }
        else
        {
            if (!_modes.Contains(UiModes.DataPatch))
            {
                problems.Add("the retail data check screen never showed");
            }

            if (first < 0 || (game >= 0 && first > game))
            {
                problems.Add("the retail character list never showed before the game screen");
            }

            if (game < 0)
            {
                problems.Add("the retail game screen never showed");
            }
            else if (!CharacterManagementAfterGame())
            {
                problems.Add("the retail character list did not come back after the log off");
            }
        }

        if (_ui is not null && !_shell.ShellUp)
        {
            problems.Add($"the in-game shell was not up after Enter or a frame showed the world without it (shell frames {_shell.Frames}, bare-world frames {_shell.BareWorldFrames}, painted {_shell.PaintedShown}, paint timed out {_shell.PaintTimedOut})");
        }

        if (!LoginCheckShell.VitalsFilled(_scenario.Vitals))
        {
            problems.Add($"the PlayerDescription left a vital empty (vitals {_scenario.Vitals})");
        }

        if (_ui is not null && !LoginCheckShell.VitalsFilled(_shell.VitalsOnScreen))
        {
            problems.Add($"the shell's vitals meters and labels were not filled (on screen {_shell.VitalsOnScreen}, session {_scenario.Vitals})");
        }

        if (_ui is not null && !_shell.SpewPortal)
        {
            problems.Add("the spew box never heard portal space's notice");
        }

        if (_ui is not null && _shell.ShownWindow != _scenario.VitalsWindow)
        {
            problems.Add($"the shell showed the {_shell.ShownWindow} vitals window, but the character's options chose {_scenario.VitalsWindow}");
        }

        if (!_rigAtPresent)
        {
            problems.Add("play mode had not built the player's body from its own CreateObject when the server unhid it");
        }

        _rigGoneAtEnd = _mode.Rig is null;
        if (!_rigGoneAtEnd)
        {
            problems.Add("the player's body was still in the world after the log off");
        }

        var sent = _portal?.Director.LoginCompletesSent ?? -1;
        if (_portal is null)
        {
            problems.Add("there was no portal view, so LoginComplete went out at the arrival without portal space (TODO g6-portal-fallback)");
        }
        else if (sent != 1 + _scenario.LegTeleportEpisodes || sent != _scenario.LoginCompletes)
        {
            problems.Add($"portal space sent LoginComplete {sent} times (the session saw {_scenario.LoginCompletes}), not once for the arrival and once for each of the {_scenario.LegTeleportEpisodes} portal-space visits of the legs ({_scenario.LegTeleports} teleports)");
        }
        else if (_scenario.LegTeleports > _scenario.LegTeleportEpisodes)
        {
            Log.Info(LogCategory, "ACE announced a teleport while the previous one was in flight; they shared one portal-space visit and its LoginComplete", ("teleports", _scenario.LegTeleports), ("visits", _scenario.LegTeleportEpisodes));
        }

        if (_scenario.Legs.Count > 0)
        {
            JudgeLegs(problems);
        }

        var census = _scenario.Census;
        var drawable = census is { } c ? c.Npcs + c.Creatures + c.Items + c.Others : 0;
        if (_rigAtPresent)
        {
            if (_hiddenCreates < 1 || _unhides < 1)
            {
                problems.Add($"the player did not materialise: the server created it hidden {_hiddenCreates} time(s) and cleared Hidden {_unhides} time(s) (want 1 and 1)");
            }
            else if (_playerPartsMax < 1)
            {
                problems.Add("the player did not materialise: its body was never drawn after the server cleared Hidden");
            }
            else if (_playerParticleObjectsMax < 1)
            {
                problems.Add("the player did not materialise from bubbles: the player's own Hidden-script particles were never handed to the renderer (other emitters in view do not count)");
            }
            else if (double.IsNaN(_fadeMs) || _fadePopped || _fadeIntermediate < 5)
            {
                problems.Add($"the player did not fade in (D63): fade_ms={(double.IsNaN(_fadeMs) ? "n/a" : _fadeMs.ToString("0", CultureInfo.InvariantCulture))}, {_fadeIntermediate} frame(s) at intermediate alpha, drawn opaque before any fade={_fadePopped}");
            }
            else if (_fadeMs is < FadeMinMilliseconds or > FadeMaxMilliseconds)
            {
                problems.Add($"the player's materialise fade took {_fadeMs:0} ms, not the 750 ms of the UnHide script's hook (accepted {FadeMinMilliseconds:0} to {FadeMaxMilliseconds:0} ms)");
            }
            else if (_fadeTwinFrames < 1 || _fadeTwinFrames < _fadeIntermediate - 2 || _fadeTwinMissing)
            {
                problems.Add($"the player's fade was asked for but the blended twin was not drawn (S4): {_fadeTwinFrames} of {_fadeIntermediate} fading frame(s) drew parts through the twin, missing twin logged={_fadeTwinMissing} (TODO(g5-translucency-twin))");
            }
        }

        if (_adapterFailed > 0)
        {
            problems.Add($"the avatar adapter could not apply {_adapterFailed} object event(s) (a Setup or part that does not load); see the avatars ERROR lines");
        }

        if (drawable > 0 && _objectsDrawnMax < 1)
        {
            problems.Add($"the server placed {drawable} creatures and items in the world and the layer drew none of them (waiting for meshes at most {_objectsWaitingMax})");
        }

        if (!double.IsNaN(_scenario.SettledAt) && _maxFrameMs > FrameLimitMilliseconds)
        {
            problems.Add($"DoD 15: {_slowFrames} frame(s) over {FrameLimitMilliseconds:0} ms between Enter and walking, the longest {_maxFrameMs:0.0} ms (each is logged above with what the frame held)");
        }

        if (_ui is not null && !double.IsNaN(_scenario.SettledAt))
        {
            if (_academyCensus is null)
            {
                problems.Add("DoD 6: the drawn census was never taken (the server-objects layer or the avatar set was not running after the settle)");
            }
            else if (!_academyCensus.Matches)
            {
                problems.Add($"DoD 6: {_academyCensus.Missing.Count} placed object(s) that should be drawn were not, {CensusWaitSeconds:0} s after the settle: {_academyCensus.Describe()}");
            }
        }

        if (_portal is not null && !double.IsNaN(_scenario.SettledAt))
        {
            if (_arrivalSequenceSeconds < ArrivalSequenceMinimumSeconds)
            {
                problems.Add($"DoD 5: portal space lasted {_arrivalSequenceSeconds:0.0} s on the arrival, under the {ArrivalSequenceMinimumSeconds:0} s the timelines last at least");
            }

            if (_arrivalTunnelSeconds < ArrivalTunnelMinimumSeconds)
            {
                problems.Add($"DoD 5: the arrival tunnel was visible {_arrivalTunnelSeconds:0.0} s, under the {ArrivalTunnelMinimumSeconds:0} s of its expand, hold and collapse");
            }

            if (_tunnelCoveredFrames > TunnelCoveredFramesAllowed)
            {
                problems.Add($"DoD 5: the tunnel was shown behind the shell's black backdrop for {_tunnelCoveredFrames} frame(s) (retail shows the tunnel from its first frame)");
            }

            if (PortalSoundsVerdict != "ok")
            {
                problems.Add($"DoD 5: the arrival's portal sounds were enter {_enterCues} (expected 1), exit {_exitCues} (expected 1), hum events {_humEvents} (expected 1 or more)");
            }
        }

        if (_portal is not null && _scenario.LogOffOk && LogOffTunnelVerdict != "held")
        {
            problems.Add($"DoD 5/11: the logoff tunnel was {LogOffTunnelVerdict} (visible {_logOffTunnelSeconds:0.0} s, down {_logOffTunnelGapSeconds:0.00} s before the character list), not held until the list (D28)");
        }

        if (Volatile.Read(ref _shotsSaved) < ReviewFrames)
        {
            problems.Add($"only {Volatile.Read(ref _shotsSaved)} of the {ReviewFrames} review frames were saved");
        }

        Finish(problems.Count == 0, problems.Count == 0 ? null : string.Join("; ", problems));
    }

    private void JudgeLegs(List<string> problems)
    {
        foreach (var leg in _scenario.Legs)
        {
            if (leg.Stage != "done")
            {
                problems.Add($"the {leg.Name} leg ended in stage {leg.Stage}, not done");
            }
        }

        if (_scenario.Corrections is not { } corrections)
        {
            problems.Add("no leg walked, so ACE's view of the moves was not audited");
        }
        else if (corrections != 0)
        {
            problems.Add($"ACE forced the player's position {corrections} time(s) (force-position corrections); see docs/physics.md section 15 (physics-thin-doorway, physics-landing-velocity)");
        }

        if (_scenario.WhereMaxMetres is { } where && where > MovementAuditSummary.WhereToleranceMetres)
        {
            problems.Add($"where-am-I disagreed with the walker by {where:0.00} m (the limit is {MovementAuditSummary.WhereToleranceMetres:0.0} m)");
        }

        if (_scenario.Legs.Any(l => l.Name == RouteLibrary.HoltburgLapsName))
        {
            if (double.IsNaN(_holtburgMinFps))
            {
                problems.Add("the Holtburg laps never gave a whole one-second window to measure the frame rate in");
            }
            else if (_holtburgMinFps < HoltburgMinimumFps)
            {
                problems.Add($"DoD 15: the Holtburg laps ran at {_holtburgMinFps:0.0} fps in their slowest second (the limit is {HoltburgMinimumFps:0} fps)");
            }

            if (_holtburgNpcsDrawnMax == 0 && !double.IsNaN(_holtburgMinFps))
            {
                problems.Add($"DoD 6: no NPC was drawn in Holtburg during the laps (the server placed up to {_holtburgNpcsPlacedMax}; last census: {_holtburgCensus?.Describe() ?? "none taken"})");
            }
        }

        if (_teleportSlowFrames > 0)
        {
            problems.Add($"DoD 15: {_teleportSlowFrames} frame(s) over {FrameLimitMilliseconds:0} ms around the legs' server teleports, the longest {_teleportFrameMaxMs:0.0} ms (each is logged above with what the frame held)");
        }

        JudgeWalkingSounds(problems);
    }

    /// <summary>
    /// The walking legs' sound judge (M3 2.1 s3; DoD 13): the hook voices the legs that walked started, each leg's own tally summed (the arrival's and the frames between legs are not counted),
    /// judged at least 1 by <see cref="LoginCheckScenario.JudgeWalkingSounds"/>. <c>sounds_server</c> of the Academy arrival stays a report (D27).
    /// </summary>
    private void JudgeWalkingSounds(List<string> problems)
    {
        int? hooks = null;
        foreach (var leg in _scenario.Legs)
        {
            if (_soundLegs.Of(leg.Name).Hooks is { } sounded)
            {
                hooks = (hooks ?? 0) + sounded;
            }
        }

        if (LoginCheckScenario.JudgeWalkingSounds(hooks, _scenario.Legs.Count) is { } problem)
        {
            problems.Add(problem);
        }
        else if (_scenario.Legs.Count > 0)
        {
            Log.Info(LogCategory, "walking legs' sounds judged", ("sounds_hooks_walking", hooks ?? -1));
        }
    }

    /// <summary>What only the engine sees, sampled every frame: the tunnel, the player's body, the layer's objects, the particles and the adapter's counters.</summary>
    private void Sample(double delta)
    {
        if (_portal is not null && double.IsNaN(_scenario.SettledAt) && _portal.Director.Machine.State != TeleportAnimState.Off)
        {
            _arrivalSequenceSeconds += delta;
        }

        if (_portal?.Director.Machine.TunnelVisible == true)
        {
            _tunnelSeconds += delta;
            if (double.IsNaN(_scenario.SettledAt))
            {
                _arrivalTunnelSeconds += delta;
                if (_portal.View.Shown && _ui is { BackdropShown: true })
                {
                    _tunnelCoveredFrames++;
                }
            }

            if (!_tunnelShot && _tunnelSeconds >= TunnelShotSeconds)
            {
                _tunnelShot = true;
                RequestShot("01-portal-tunnel", "portal space live: the tunnel showing while the destination loads");
            }
        }

        SamplePortalSounds();
        SampleLogOffTunnel(delta);
        SampleAcademyCensus();
        if (_mode.Adapter is { } adapter)
        {
            var stats = adapter.Stats;
            _hiddenCreates = Math.Max(_hiddenCreates, stats.PlayerCreatedHidden);
            _unhides = Math.Max(_unhides, stats.PlayerUnhides);
            _adapterApplied = Math.Max(_adapterApplied, stats.Applied);
            _adapterFailed = Math.Max(_adapterFailed, stats.Failed);
            _adapterUnknown = Math.Max(_adapterUnknown, stats.Unknown);
        }

        if (_mode.Avatars is { } avatars)
        {
            _avatarCountMax = Math.Max(_avatarCountMax, avatars.Count);
            SampleFade(avatars);
        }

        if (_mode.Layer is { Active: true } layer)
        {
            _objectsDrawnMax = Math.Max(_objectsDrawnMax, layer.DrawnObjects);
            _inCellMax = Math.Max(_inCellMax, layer.DrawnInCell); // M5 0 c1: drawn_in_cell= and light_rows= (report-only)
            _lightRowsMax = Math.Max(_lightRowsMax, layer.LightRowsInUse);
            _objectsWaitingMax = Math.Max(_objectsWaitingMax, layer.Waiting);
            if (layer.Particles is { } particles)
            {
                _particleObjectsMax = Math.Max(_particleObjectsMax, particles.ObjectsLastFrame);
                _playerParticleObjectsMax = Math.Max(_playerParticleObjectsMax, particles.PlayerObjectsLastFrame);
                _particleBatchesMax = Math.Max(_particleBatchesMax, particles.BatchesLastFrame);
            }

            var (outdoor, indoor) = layer.Meshes;
            _meshesBuilt = Math.Max(_meshesBuilt, outdoor.Built + indoor.Built);
            _meshAttachMaxMs = Math.Max(_meshAttachMaxMs, Math.Max(outdoor.MaxAttachMilliseconds, indoor.MaxAttachMilliseconds));
        }

        if (_mode.Rig is { } rig && !double.IsNaN(_scenario.PresentAt) && _scenario.Stage is "settling" or "logging-off")
        {
            _playerPartsMax = Math.Max(_playerPartsMax, rig.View.DrawnParts);
        }
    }

    /// <summary>
    /// D63: the player's materialise fade on frames. Hidden frames first; then, from the first visible frame (the unhide), the translucency must fall through intermediate values to 0, and
    /// <c>fade_ms</c> is the time from that first visible frame to the first opaque one. An opaque frame with no intermediate one before it is the pop-in (or retail's re-hide flash).
    /// The review frame <c>02a-materialising</c> is requested at about half way.
    /// </summary>
    private void SampleFade(Lifestone.Client.Avatars.AvatarSet avatars)
    {
        foreach (var draw in avatars.Frame.Avatars)
        {
            if (!draw.IsPlayer)
            {
                continue;
            }

            if (!draw.Visible)
            {
                if (double.IsNaN(_fadeMs))
                {
                    _fadeSawHidden = true;
                    _fadeUnhideAt = double.NaN;
                }

                return;
            }

            if (!_fadeSawHidden || !double.IsNaN(_fadeMs))
            {
                return;
            }

            var now = PortalSpaceSetup.Now();
            if (double.IsNaN(_fadeUnhideAt))
            {
                _fadeUnhideAt = now;
            }

            if (draw.Translucency >= 1f)
            {
                return;
            }

            if (draw.Translucency > 0f)
            {
                _fadeIntermediate++;
                if (_mode.Rig is { } rig)
                {
                    _fadeTwinFrames += rig.View.TwinParts > 0 ? 1 : 0;
                    _fadeTwinMissing |= rig.View.TwinMissing;
                }

                if (!_fadeShot && draw.Translucency <= 0.6f)
                {
                    _fadeShot = true;
                    RequestShot("02a-materialising", "the player mid-fade (about half way through the 0.75 s materialise) among the objects the server placed");
                }

                return;
            }

            if (_fadeIntermediate == 0)
            {
                _fadePopped = true;
            }

            _fadeMs = (now - _fadeUnhideAt) * 1000.0;
            return;
        }
    }

    /// <summary>
    /// The arrival's portal sounds, from the cues the presenter applied (one frame each; the presenter runs before this node) and the director's hum events, until the settle ends: the enter
    /// whoosh once, the exit whoosh once and the tunnel's hum at least once (DoD 5). Counts only; the sound player's own counters are read in the verdict.
    /// </summary>
    private void SamplePortalSounds()
    {
        if (_portal is not { } portal || !double.IsNaN(_scenario.SettledAt))
        {
            return;
        }

        var presenter = portal.Presenter;
        if (presenter.Frames != _presenterFramesSeen)
        {
            _presenterFramesSeen = presenter.Frames;
            var cues = presenter.LastFrame.Cues;
            if ((cues & PortalCues.PlayEnterPortal) != 0)
            {
                _enterCues++;
            }

            if ((cues & PortalCues.PlayExitPortal) != 0)
            {
                _exitCues++;
            }
        }

        _humEvents = portal.Director.TunnelEvents;
    }

    /// <summary>
    /// D28 (the logoff fix): from the log off until the retail character list shows, the tunnel stays up. Counts the time it is visible after its first showing and the time it is down
    /// before the list's screen; the verdict says <c>held</c> when it showed and was never down longer than the tolerance.
    /// </summary>
    private void SampleLogOffTunnel(double delta)
    {
        if (_scenario.Stage is not ("logging-off" or "quitting") || CharacterManagementAfterGame() || _portal is null)
        {
            return;
        }

        if (_portal.Director.Machine.TunnelVisible)
        {
            _logOffTunnelSeen = true;
            _logOffTunnelSeconds += delta;
        }
        else if (_logOffTunnelSeen)
        {
            _logOffTunnelGapSeconds += delta;
        }
    }

    /// <summary>
    /// The drawn census (dod-6-7-1): from the end of the Academy settle, every <see cref="CensusIntervalSeconds"/> for <see cref="CensusWaitSeconds"/>, the placed objects of every kind against
    /// the guids the layer drew; stops at the first match. The last census taken is the verdict's: a mismatch that outlasts the wait fails the run.
    /// </summary>
    private void SampleAcademyCensus()
    {
        if (_academyCensusDone || double.IsNaN(_scenario.SettledAt))
        {
            return;
        }

        if (double.IsNaN(_academyCensusStart))
        {
            _academyCensusStart = _runSeconds;
        }

        if (!double.IsNaN(_academyCensusAt) && _runSeconds < _academyCensusAt + CensusIntervalSeconds)
        {
            return;
        }

        _academyCensusAt = _runSeconds;
        if (TakeCensus() is { } census)
        {
            _academyCensus = census;
            _academyCensusDone = census.Matches;
        }

        _academyCensusDone |= _runSeconds >= _academyCensusStart + CensusWaitSeconds;
    }

    /// <summary>The placed objects against the layer's drawn guids now, or null when the layer or the avatar set is not running.</summary>
    private DrawnCensus? TakeCensus()
    {
        if (_mode.Layer is not { Active: true } layer || _mode.Avatars is not { } avatars || _host.Session is not { } session)
        {
            return null;
        }

        var rows = new Dictionary<uint, AvatarDraw>();
        foreach (var row in avatars.Frame.Avatars)
        {
            rows[row.Guid] = row;
        }

        var frame = avatars.Frame;
        DrawExpectation Expect(uint guid)
        {
            if (!avatars.TryGet(guid, out _))
            {
                return DrawExpectation.NoAvatar;
            }

            if (!rows.TryGetValue(guid, out var row))
            {
                return DrawExpectation.NoCell;
            }

            if (!row.Visible)
            {
                return DrawExpectation.Hidden;
            }

            if (row.Translucency >= 1f)
            {
                return DrawExpectation.Translucent;
            }

            var any = false;
            foreach (var part in frame.Parts.Slice(row.PartStart, row.PartCount))
            {
                if (part.GfxObjId != 0)
                {
                    any = true;
                    break;
                }
            }

            if (!any)
            {
                return DrawExpectation.NoParts;
            }

            return row.InEnvCell && (_mode.Interiors is null || !_mode.Interiors.IsCellShown(row.CellId)) ? DrawExpectation.CellNotShown : DrawExpectation.Drawable;
        }

        return DrawnCensus.Compare(session.State, layer.DrawnGuids.ToHashSet(), Expect);
    }

    /// <summary>
    /// Writes each leg's movement audit summary to <c>cache/movement/&lt;run&gt;-&lt;leg&gt;.json</c> (gitignored; <see cref="Lifestone.Client.Movement.Routes.MovementAudit.ToJson"/>, which the movement layer leaves to
    /// the host): the run is named by the UTC time. A failure to write is logged, never thrown.
    /// </summary>
    private void WriteMovementAudits()
    {
        var run = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        foreach (var leg in _scenario.Legs)
        {
            if (leg.SummaryJson is not { } json)
            {
                continue;
            }

            try
            {
                var directory = System.IO.Path.Combine(ScreenshotWriter.RepositoryRoot, "cache", "movement");
                System.IO.Directory.CreateDirectory(directory);
                var file = System.IO.Path.Combine(directory, $"{run}-{leg.Name}.json");
                System.IO.File.WriteAllText(file, json);
                Log.Info(LogCategory, "the movement audit summary was written", ("leg", leg.Name), ("file", $"cache/movement/{run}-{leg.Name}.json"));
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                Log.Warn(LogCategory, "the movement audit summary could not be written", ("leg", leg.Name), ("problem", ex.Message));
            }
        }
    }

    private string LogOffTunnelVerdict => !_logOffTunnelSeen ? "never-shown" : _logOffTunnelGapSeconds > LogOffTunnelGapToleranceSeconds ? "dropped" : "held";

    /// <summary>The portal sounds as one verdict word: <c>ok</c> when the enter whoosh played once, the exit whoosh once and the hum at least once during the arrival.</summary>
    private string PortalSoundsVerdict => _enterCues == 1 && _exitCues == 1 && _humEvents >= 1 ? "ok" : "bad";

    private void RequestShot(string name, string note)
    {
        _shots.Add(new Shot(name, note, _frame + ShotDelayFrames));
    }

    /// <summary>Saves the review frames whose delay has passed (the viewport holds the frame rendered before the request, so a few frames pass first).</summary>
    private void TakeDueShots()
    {
        for (var i = _shots.Count - 1; i >= 0; i--)
        {
            var shot = _shots[i];
            if (_frame < shot.DueFrame)
            {
                continue;
            }

            _shots.RemoveAt(i);
            Interlocked.Increment(ref _shotsPending);
            var readbackMs = ScreenshotWriter.SaveInBackground(
                GetViewport(),
                $"{ShotFolder}/{shot.Name}.png",
                LogCategory,
                ok =>
                {
                    if (ok)
                    {
                        Interlocked.Increment(ref _shotsSaved);
                    }

                    Interlocked.Decrement(ref _shotsPending);
                },
                ("what", shot.Note),
                ("ui_mode", _modes.Count == 0 ? "none" : UiModes.NameOf(_modes[^1])),
                ("tunnel_visible", _portal?.Director.Machine.TunnelVisible ?? false),
                ("player_parts_drawn", _mode.Rig?.View.DrawnParts ?? 0),
                ("objects_drawn", _mode.Layer?.DrawnObjects ?? 0),
                ("particle_objects", _mode.Layer?.Particles?.ObjectsLastFrame ?? 0),
                ("player_particle_objects", _mode.Layer?.Particles?.PlayerObjectsLastFrame ?? 0),
                ("travel", _host.Session?.State.Self.Travel.ToString() ?? "none"));
            if (readbackMs < 0)
            {
                Interlocked.Decrement(ref _shotsPending); // the capture failed (logged); completed is not called
            }
            else
            {
                _shotReadbackMaxMs = Math.Max(_shotReadbackMaxMs, readbackMs);
            }
        }
    }

    private readonly record struct Shot(string Name, string Note, long DueFrame);

    private void Finish(bool ok, string? why)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        var scenarioOk = _scenario.Ok;
        var held = ok && scenarioOk;
        if (!held)
        {
            Log.Error(LogCategory, "the login check failed", ("stage", _scenario.Stage), ("why", why ?? _scenario.Failure ?? "unknown"));
        }

        var reachedWalk = !double.IsNaN(_scenario.SettledAt);
        if (reachedWalk && _worstPartsMs > NearLimitMilliseconds)
        {
            // The named open TODO, repeated once in the verdict with the worst frame's parts, so a run that failed (or came near) the 50 ms rule says where the time went.
            Log.Warn(
                LogCategory,
                $"TODO(login-burst-frame): the frame that handles ACE's world-entry burst sits near the 50 ms limit live; the worst frame between Enter and walking took {_worstPartsMs:0.0} ms ({_slowFrames} over 50 ms), its parts are listed",
                [.. _worstParts.Fields()]);
        }

        if (reachedWalk && _windowCompiled.Foreground > 0)
        {
            Log.Warn(
                LogCategory,
                "TODO(arrival-pipelines): first-use pipeline compilations remain between Enter and walking (each is logged above with its stage). Known and cheap: the canvas pipeline at the tunnel's start (15-38 ms, not the notice line or the tunnel screen), the player figure's creature-mode surfaces in the tunnel (about 4 ms), the first object's draw pipelines (about 4 ms)",
                ("compiled", _windowCompiled.Describe()));
        }

        var reachedAcademy = _scenario.AcademyObjects >= 0;
        var legsWalked = _scenario.Legs.Count > 0;
        var exit = !_scenario.Legs.Any(l => l.Name == RouteLibrary.AcademyExitName) ? "n/a" : _scenario.ExitTeleported ? "teleported" : "failed";
        var holtburgRan = _scenario.Legs.Any(l => l.Name == RouteLibrary.HoltburgLapsName);
        var legsText = legsWalked ? string.Join(",", _scenario.Legs.Select(l => l.Name)) : "none";
        var inv = CultureInfo.InvariantCulture;
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"done ok={(held ? "true" : "false")} enter_to_walk_max_ms={(reachedWalk ? _maxFrameMs.ToString("0.0", CultureInfo.InvariantCulture) : "n/a")} holtburg_min_fps={(double.IsNaN(_holtburgMinFps) ? "n/a" : _holtburgMinFps.ToString("0.0", inv))} corrections={(_scenario.Corrections is { } corrections ? corrections.ToString(inv) : "n/a")} where_max_m={(_scenario.WhereMaxMetres is { } whereMax ? whereMax.ToString("0.00", inv) : "n/a")} exit={exit} quest={_scenario.ExitQuestVerdict} legs={legsText} teleports={(_scenario.EnteredGuid != 0 && reachedAcademy ? _scenario.Teleports.ToString(CultureInfo.InvariantCulture) : "n/a")} leg_teleports={(legsWalked ? _scenario.LegTeleports.ToString(inv) : "n/a")} teleport_frame_max_ms={(_teleportWindowFrames > 0 ? _teleportFrameMaxMs.ToString("0.0", inv) : "n/a")} academy_objects={(reachedAcademy ? _scenario.AcademyObjects.ToString(CultureInfo.InvariantCulture) : "n/a")} logoff={(_scenario.LogOffOk ? "ok" : "failed")} materialised={(_rigAtPresent ? (_hiddenCreates >= 1 && _unhides >= 1 && _playerPartsMax >= 1 && _playerParticleObjectsMax >= 1 ? "true" : "false") : "n/a")} fade_ms={(_rigAtPresent && !double.IsNaN(_fadeMs) ? _fadeMs.ToString("0", inv) : "n/a")} objects_drawn={(reachedAcademy ? _objectsDrawnMax.ToString(CultureInfo.InvariantCulture) : "n/a")} academy_undrawn={(_academyCensus is null ? "n/a" : _academyCensus.Missing.Count.ToString(inv))} holtburg_objects_drawn_max={(holtburgRan ? _holtburgObjectsDrawnMax.ToString(inv) : "n/a")} holtburg_npcs={(holtburgRan ? _holtburgNpcsDrawnMax.ToString(inv) + "/" + _holtburgNpcsPlacedMax.ToString(inv) : "n/a")} holtburg_max_frame_ms={(holtburgRan ? _holtburgMaxFrameMs.ToString("0.0", inv) : "n/a")} tunnel_seconds={(_portal is null ? "n/a" : _arrivalTunnelSeconds.ToString("0.0", inv))} portal_seconds={(_portal is null ? "n/a" : _arrivalSequenceSeconds.ToString("0.0", inv))} tunnel_covered_frames={(_portal is null ? "n/a" : _tunnelCoveredFrames.ToString(inv))} logoff_tunnel={(_portal is null ? "n/a" : LogOffTunnelVerdict)} portal_sounds={(_portal is null ? "n/a" : PortalSoundsVerdict)} sound_enter={_enterCues} sound_exit={_exitCues} sound_hum={_humEvents} logoff_by={_scenario.LogOffVia}{LoginCheckShell.Fields(_shell.ShellUp, _ui is not null ? _shell.VitalsOnScreen : _scenario.Vitals, _shell.SpewPortal, _shell.ShownWindow)}{_scenario.ReportFields}{LoginCheckScenario.SoundFields(_soundLegs.Report(LoginCheckScenario.SoundArrivalLeg, RouteLibrary.HoltburgLapsName))}{LoginCheckScenario.ObjectFields(_scenario.ObjectsMax, _inCellMax, _lightRowsMax)}");
        WriteMovementAudits();
        foreach (var leg in _scenario.Legs)
        {
            Log.Info(LogCategory, $"leg {leg.Name}: {leg.SummaryLine ?? "no movement verdict"}", ("stage", leg.Stage), ("unmatched", leg.Unmatched), ("server_movements", leg.ServerMovements), ("run_rate", leg.RunRate));
        }

        Log.Info(LogCategory, line);
        var soundLegs = new List<(string, object?)>();
        foreach (var name in _scenario.Legs.Select(l => l.Name).Prepend(LoginCheckScenario.SoundArrivalLeg).Append(LoginCheckScenario.SoundBetweenLegs))
        {
            var c = _soundLegs.Of(name);
            soundLegs.Add((name, $"server_sounded={SoundSourceCounts.Value(c.Server)} hooks_sounded={SoundSourceCounts.Value(c.Hooks)} ui={SoundSourceCounts.Value(c.Ui)} ambient={SoundSourceCounts.Value(c.Ambient)} server_handed={SoundSourceCounts.Value(c.ServerHanded)} hooks_handed={SoundSourceCounts.Value(c.HooksHanded)} player_footsteps_handed={SoundSourceCounts.Value(c.Footsteps)} player_footsteps_sounded={SoundSourceCounts.Value(c.FootstepsSounded)}"));
        }

        Log.Info(LogCategory, "sound counters per leg (report-only)", soundLegs.ToArray());
        Log.Info(
            LogCategory,
            "detail",
            ("created", _scenario.Created),
            ("login_completes", _scenario.LoginCompletes),
            ("movement_held_until_present", _movementHeld),
            ("arrival_cell", $"0x{_scenario.ArrivalCell:X8}"),
            ("ui_modes", string.Join(">", _modes.Select(UiModes.NameOf))),
            ("shell_frames", _shell.Frames),
            ("shell_bare_world_frames", _shell.BareWorldFrames),
            ("vitals_window_chosen", _scenario.VitalsWindow),
            ("radar_blips_max", _shell.RadarBlipsMax),
            ("seconds", _runSeconds),
            ("enter_to_walk_pipelines", _windowCompiled.Describe()));
        Log.Info(
            LogCategory,
            "world content",
            ("census", _scenario.Census?.Describe() ?? "n/a"),
            ("academy_drawn_census", _academyCensus?.Describe() ?? "n/a"),
            ("holtburg_drawn_census", _holtburgCensus?.Describe() ?? "n/a"),
            ("objects_held", _scenario.ObjectsHeld),
            ("avatars_max", _avatarCountMax),
            ("objects_drawn_max", _objectsDrawnMax),
            ("objects_waiting_max", _objectsWaitingMax),
            ("layer_batches", _mode.Layer?.BatchCount ?? 0),
            ("layer_uploads", _mode.Layer?.Uploads ?? 0),
            ("layer_max_ms", _mode.Layer?.MaxMilliseconds ?? 0.0),
            ("meshes_built", _meshesBuilt),
            ("mesh_attach_max_ms", _meshAttachMaxMs),
            ("player_parts_drawn_max", _playerPartsMax),
            ("player_created_hidden", _hiddenCreates),
            ("player_unhides", _unhides),
            ("fade_ms", _fadeMs),
            ("fade_intermediate_frames", _fadeIntermediate),
            ("fade_twin_frames", _fadeTwinFrames),
            ("fade_twin_missing", _fadeTwinMissing),
            ("fade_popped", _fadePopped),
            ("particle_objects_max", _particleObjectsMax),
            ("player_particle_objects_max", _playerParticleObjectsMax),
            ("particle_batches_max", _particleBatchesMax),
            ("adapter_applied", _adapterApplied),
            ("adapter_failed", _adapterFailed),
            ("adapter_unknown", _adapterUnknown),
            ("frames_saved", Volatile.Read(ref _shotsSaved)),
            ("frame_readback_max_ms", _shotReadbackMaxMs));
        Log.FlushToGodot();
        GetTree().Quit(held ? 0 : 1);
    }
}

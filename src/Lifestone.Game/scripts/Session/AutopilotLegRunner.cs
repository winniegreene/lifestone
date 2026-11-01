using System.Globalization;
using Godot;
using Lifestone.Client.Options;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.PortalSpace;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// <c>--login-check=autopilot</c> (M5 4 h1, D57): the game enters BSD Mage on the test account and runs the <see cref="AutopilotScenario"/> (the buff, the travel, the fight, the logistics trip, the death and the arrows between them over the
/// journey's own stations) in its own session until its stop rules (<c>--autopilot-minutes</c>, <c>--autopilot-level</c>) or a stop request end it, then logs off and quits. A kind of its own, never part of <c>--login-check=all</c> (one line in
/// <see cref="LoginCheckRunner.Install"/>, as the journey's is). The login, the log off and the failure path are <see cref="LegSessionFlow"/> (shared with <see cref="JourneyLegRunner"/>); the parts of the context are
/// <see cref="JourneyLegRunner.BuildParts"/> (the vitals keeper, the station factory, <see cref="GodotJourneyRunnerHost"/> with the player's own body as the walker, the dats, the ground), so the two checks build one way. This node steps
/// the autopilot once a frame after the session host and play mode.
/// </summary>
/// <remarks>
/// <para>Every autopilot line is printed prefixed <c>[logincheck]</c>: <c>autopilot cycle n= ...</c> per cycle and <c>autopilot done ok=yes|no cycles= stop= ...</c>; then <c>autopilot leg done ok= ...</c>. The process ends with 0 only when the
/// done line said <c>ok=yes</c> (the autopilot's own verdict: a stop rule or a request is a success, a failed state is not) and nothing else failed. The stations' own lines print under their categories as in the journey.</para>
/// <para><b>Review frames</b> (<c>--play-screenshot=PATH</c>, report-only): <c>&lt;stem&gt;-autopilot-&lt;state&gt;-c&lt;cycle&gt;.png</c> a moment after the autopilot enters a state, at most one a state in a cycle
/// (<see cref="FrameDelaySeconds"/>), the developer HUD hidden. A frame that cannot be saved is logged by name; nothing here fails the run.</para>
/// <para><b>The dial (M5 4 h3; the controls of M5 7 D145).</b> The run is stepped by the same <see cref="AutopilotHostLogic"/> the play host runs (<see cref="AutopilotHost"/>): /stop and /auto in the chat and the tools panel's Autopilot button are wired to it, /stop
/// stops him where he stands and /auto hands back (the gate's scripted version: <c>--autopilot-test-interrupt-at</c>, <c>--autopilot-test-handback-after</c>, typed into the chat), the checkpoint is a file in the user data
/// (<see cref="UserDataCheckpointStore"/>, closing <c>TODO(m5-autopilot-checkpoint)</c>) and a dropped session reconnects (<see cref="GameSessionAutopilotSession"/>, closing <c>TODO(m5-autopilot-reconnect)</c>).</para>
/// <para><b>Limits.</b> <see cref="WholeSecondsFor"/>: the login, the margin and <c>--autopilot-minutes</c>; with no minutes the run has no time limit (its stop rules or a stop request end it).</para>
/// </remarks>
public sealed partial class AutopilotLegRunner : Node
{
    /// <summary>The seconds the login and the arrival may take before the autopilot starts (the journey check's).</summary>
    public const double LoginSeconds = JourneyLegRunner.LoginSeconds;

    /// <summary>The margin added to <c>--autopilot-minutes</c> for the login and the settle (Lifestone's own).</summary>
    public const double MarginSeconds = JourneyLegRunner.MarginSeconds;

    /// <summary>The seconds a stop at <c>--autopilot-minutes</c> may wait for a safe point: a trip or a death handler in progress finishes first (the logistics station's 600 s and the death station's 280 s, with the fight's clean stop; Lifestone's own).</summary>
    public const double SafePointSeconds = 1200.0;

    /// <summary>The seconds after a state begins before its review frame is taken (the state has drawn something worth seeing; Lifestone's own).</summary>
    public const double FrameDelaySeconds = 2.0;

    /// <summary>The whole check's limit for these options: the login, the margin, the stop rule's minutes and the safe point; <see cref="double.PositiveInfinity"/> when no minutes are given (a level or a stop request ends the run).</summary>
    /// <param name="options">The autopilot's options.</param>
    public static double WholeSecondsFor(AutopilotOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Minutes > 0 ? LoginSeconds + MarginSeconds + (options.Minutes * 60.0) + SafePointSeconds : double.PositiveInfinity;
    }

    private const string LogCategory = "logincheck";

    private enum Phase
    {
        Loading,
        Session, // LegSessionFlow: the login to the world, the log off and quit
        Running,
    }

    private SessionHost _host = null!;
    private readonly Lifestone.Client.Session.Hosting.LegFaultGuard _guard = new("AutopilotLegRunner", LogCategory, GodotLogSink.Instance);
    private PlayMode _mode = null!;
    private DatSet _dats = null!;
    private GameServices _services = null!;
    private JourneyPlan _plan = null!;
    private AutopilotOptions _options = new();
    private IReadOnlyList<string> _arguments = [];
    private string? _optionError;
    private double _wholeSeconds;
    private KitLoads _loads = null!;
    private HuntingGround _ground = null!;
    private AutopilotHostLogic? _logic;
    private AutopilotKit? _kit;
    private RetailUiLayer? _ui;
    private AutopilotHostOptions _hostOptions = new();
    private AutopilotTestHandOff? _handOff;
    private object? _wiredScreen;
    private LegSessionFlow _flow = null!;
    private DeveloperHudHider _hud = null!;
    private Phase _phase = Phase.Loading;
    private double _runSeconds;
    private bool _finished;
    private bool _scenarioEnded;
    private string? _scenarioFailure;
    private int _linesPrinted;
    private int _framesSaved;
    private string? _stem;
    private readonly Queue<(string Name, double Due, string State)> _frames = new();
    private readonly HashSet<string> _framesAsked = [];
    private readonly List<string> _framesNotTaken = [];

    /// <summary>The live session, or null before the session host has one (slice h3's reconnect watches it).</summary>
    public IGameSessionView? Session => _host?.Session;

    /// <summary>The running scenario (a hand-back replaces it), or null before the login ended.</summary>
    public AutopilotScenario? Autopilot => _logic?.Scenario;

    /// <summary>The dial's logic (the same <see cref="AutopilotHostLogic"/> the play host runs: the key, the button, the person's interrupt and the hand-back), or null before the login ended.</summary>
    public AutopilotHostLogic? Logic => _logic;

    /// <summary>The checkpoint file (M5 4 h3: <see cref="UserDataCheckpointStore"/>, <c>user://autopilot/&lt;character&gt;.json</c>, closing <c>TODO(m5-autopilot-checkpoint)</c>).</summary>
    public string CheckpointPath { get; private set; } = string.Empty;

    /// <summary>Adds the check to the scene; it starts with the scene's first frame.</summary>
    public static void Install(Main main, GameServices services, SessionHost host, PlayMode mode, RetailUiLayer? ui)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(mode);
        var dats = services.Dats ?? throw new InvalidOperationException("the autopilot needs the dats (Main checks for them first)");
        var play = services.Options.Play;
        var plan = JourneyPlans.Mage;
        var arguments = OS.GetCmdlineUserArgs();
        var parsedArguments = CommandLineArgument.ParseAll(arguments);
        var options = AutopilotOptions.FromArguments(parsedArguments) with { Buff = Lifestone.Client.Session.Mage.BuffOptions.FromArguments(parsedArguments) }; // 4-R4: the same margin as the re-buff driver's
        var hostOptions = AutopilotHostOptions.FromArguments(parsedArguments);
        var ground = JourneyLimits.FightOf(arguments).GroundProfile; // a bad --fight-ground is the station factory's error and keeps the default
        var errors = new List<string>(play.Errors);
        errors.AddRange(options.Errors);
        errors.AddRange(hostOptions.Errors);
        if (!string.Equals(host.Options.Account, plan.Account, StringComparison.Ordinal) && !(string.IsNullOrWhiteSpace(host.Options.Account) && plan.Account == "test"))
        {
            errors.Add($"--account: the autopilot runs {plan.Character} on the account '{plan.Account}'");
        }

        foreach (var error in errors)
        {
            Log.Error(LogCategory, "autopilot: " + error);
        }

        var runner = new AutopilotLegRunner
        {
            Name = "AutopilotLegRunner",
            ProcessPriority = 100, // after the session host (-900), play mode (-800) and the retail UI: it looks at what they did this frame
            _host = host,
            _mode = mode,
            _dats = dats,
            _services = services,
            _plan = plan,
            _options = options,
            _arguments = arguments,
            _ground = ground,
            _optionError = errors.Count == 0 ? null : "bad autopilot option: " + string.Join("; ", errors),
            _wholeSeconds = WholeSecondsFor(options),
            _stem = play.BuffShotPath,
            CheckpointPath = ProjectSettings.GlobalizePath("user://autopilot/" + AutopilotHostOptions.CheckpointFileName(plan.Character)),
            _ui = ui,
            _hostOptions = hostOptions,
        };
        runner._loads = new KitLoads(dats, plan, ground, () => { });
        runner._loads.Start(); // the spell table and the den's rooms on workers (JourneyLegRunner.StartLoads)
        if (hostOptions.HasHandOff)
        {
            runner._handOff = new AutopilotTestHandOff(hostOptions, line => AutopilotHost.TypeInChat(runner._ui, line), options.TestDeath); // M5 7 D145: /stop and /auto typed into the chat
        }

        runner._flow = new LegSessionFlow("autopilot", LogCategory, plan.Character);
        runner._hud = new DeveloperHudHider(runner);
        main.AddChild(runner);
        Log.Info(
            LogCategory,
            $"login check 'autopilot' installed: the test account enters {plan.Character}, the autopilot runs in the game's session (the player's own body is the runner's walker) until its stop rules or a stop request, then the game logs off and quits",
            ("scenario", AutopilotScenario.ScenarioName),
            ("options", options.ToString()),
            ("hand_off", hostOptions.ToString()),
            ("checkpoint", System.IO.Path.GetFileName(runner.CheckpointPath)), // 4-R11: the file name, never the globalised path (under the user profile without -IsolateUserData)
            ("whole_check_s", double.IsInfinity(runner._wholeSeconds) ? "none" : runner._wholeSeconds.ToString("0", CultureInfo.InvariantCulture)),
            ("review_frames", runner._stem is null ? "not asked for" : "autopilot-<state>-c<cycle> a moment after each state begins"));
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
            // Reported once; the leg ends through its own fail path (log off if logged in, a verdict of ok=false, a non-zero quit).
            if (_guard.Catch($"_Process ({_phase})", e))
            {
                Fail(_host.Session, _guard.Reason!);
            }
            else
            {
                Finish(_guard.Reason);
            }
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        _hud.Restore();
        _logic?.Dispose();
        _kit?.Dispose();
    }

    private void StepFrame(double delta)
    {
        if (_finished)
        {
            return;
        }

        _runSeconds += delta;
        _flow.Seconds = _runSeconds;
        if (_host.Session is not { } session)
        {
            Finish("the session host has no session");
            return;
        }

        if (_flow.Quitting || _flow.Failed)
        {
            if (_flow.Step(session) == LegFlowStatus.Ended)
            {
                Finish(null);
            }

            return;
        }

        if (_optionError is not null)
        {
            Fail(session, _optionError);
            return;
        }

        if (_runSeconds > _wholeSeconds)
        {
            Fail(session, $"the autopilot check took more than {_wholeSeconds:0} s");
            return;
        }

        switch (_phase)
        {
            case Phase.Loading:
                Loading();
                break;
            case Phase.Session:
                switch (_flow.Step(session))
                {
                    case LegFlowStatus.Ready:
                        BeginRun(session);
                        break;
                    case LegFlowStatus.Ended:
                        Finish(null);
                        break;
                }

                break;
            case Phase.Running:
                StepRun(session, delta);
                break;
        }
    }

    private void Loading()
    {
        if (_loads.Poll() is { } failure)
        {
            Fail(null, "the spell table or the den's rooms could not be read: " + failure);
            return;
        }

        if (!_loads.Ready)
        {
            return;
        }

        _flow.Begin();
        _phase = Phase.Session;
    }

    private void BeginRun(IGameSessionView session)
    {
        // M5 4 h3: the same dial as the play host (AutopilotHostLogic over AutopilotKit): the checkpoint file, the reconnect, the person's interrupt and the hand-back (the scripted ones: --autopilot-test-interrupt-at).
        var store = new UserDataCheckpointStore(CheckpointPath);
        _kit = new AutopilotKit(_plan, () => _host.Session, _mode, _services, _dats, _arguments, _host.Options.Account, _options, _ground, _loads, store, OnScenario);
        _logic = new AutopilotHostLogic(_kit.Create, GodotLogSink.Instance, _options, _ground);
        AutopilotHost.WireHost(_logic, () => _kit, () => _ui?.Manager?.Input.Bindings); // 4-R8, 4-R16
        _linesPrinted = 0;
        Log.Info(LogCategory, "autopilot: the player is in the world; the autopilot starts (" + _options + "; " + _hostOptions + "; checkpoint " + store.Describe + ")");
        if (_hostOptions is { InterruptAtSeconds: not null, HandBackAfterSeconds: null })
        {
            Log.Warn(LogCategory, "autopilot: --autopilot-test-interrupt-at without --autopilot-test-handback-after: the controls stay the person's, so the run ends only at the check's time limit");
        }

        _logic.Start(session);
        _phase = Phase.Running;
    }

    /// <summary>M5 7 D145: the leg's /stop: he stops where he stands.</summary>
    private string StopHere(AutopilotHostLogic logic, IGameSessionView session) => logic.StopHere(_host.Session ?? session);

    /// <summary>M5 7 D145: the leg's /auto: a hand-back (the leg's kit is built at its start), or an answer while it runs.</summary>
    private string AutoHere(AutopilotHostLogic logic, IGameSessionView session)
    {
        if (logic.Mode is AutopilotMode.Running or AutopilotMode.Stopping)
        {
            return "Lifestone: the autopilot is already " + logic.Mode.ToString().ToLowerInvariant() + "; nothing else is done.";
        }

        logic.Toggle(_host.Session ?? session, reason: AutopilotHostLogic.ResumeReason);
        return logic.Mode == AutopilotMode.Running ? "Lifestone: the autopilot has the controls again: it checks his state first, then resumes." : "Lifestone: the autopilot did not start (" + logic.Status.LastDecision + ").";
    }

    /// <summary>Called for each scenario the kit makes (the start and every hand-back): the review frames follow its states, the soak line reads it, its chat lines go to the chat.</summary>
    private void OnScenario(AutopilotScenario scenario)
    {
        scenario.StateChanged += OnTransition;
        if (GetTree().Root.FindChild("SoakLine", recursive: true, owned: false) is SoakLine soak)
        {
            soak.BindAutopilot(scenario); // the soak line's kills= deaths= cycles= (m5-soak-tallies)
        }
    }

    private void StepRun(IGameSessionView session, double delta)
    {
        var logic = _logic!;
        // M5 7 D145: /stop and /auto in the chat (and the tools panel's button, the one or the other by the mode) are the dial's here too.
        var controls = new DialControls(() => StopHere(logic, session), () => AutoHere(logic, session));
        AutopilotHost.WireShell(_ui, controls, () => AutopilotHost.ShowChat(_ui, logic.Mode is AutopilotMode.Running or AutopilotMode.Stopping ? StopHere(logic, session) : AutoHere(logic, session)), logic, ref _wiredScreen);
        _handOff?.Tick(delta, logic);
        logic.Tick(session, PortalSpaceSetup.Now());
        PrintNewLines(logic);
        SaveDueFrames();
        if (logic.Mode != AutopilotMode.Done)
        {
            return;
        }

        var autopilot = logic.Scenario!;
        _scenarioEnded = true;
        if (!autopilot.Ok)
        {
            _scenarioFailure = autopilot.Failure ?? ("the autopilot stopped " + (autopilot.StopName ?? "without a reason"));
        }

        _flow.BeginLogOff(session); // also after a failed state: the character leaves the world before the check reports
        _phase = Phase.Session;
    }

    /// <summary>Prints each autopilot line written since the last frame, prefixed by the category (<c>[logincheck] autopilot done ok=yes ...</c>).</summary>
    private void PrintNewLines(AutopilotHostLogic logic)
    {
        var lines = logic.Lines;
        while (_linesPrinted < lines.Count)
        {
            Log.Info(LogCategory, lines[_linesPrinted++]);
        }
    }

    // ------------------------------------------------------------------------------------------------ the review frames

    /// <summary>Asks for the state's frame once a cycle (the first arrival at a state in the cycle); it is taken <see cref="FrameDelaySeconds"/> later.</summary>
    private void OnTransition(AutopilotTransition t)
    {
        if (_stem is null)
        {
            return;
        }

        var state = AutopilotScenario.StateName(t.To);
        var name = string.Create(CultureInfo.InvariantCulture, $"autopilot-{state}-c{t.Cycle}");
        if (_framesAsked.Add(name))
        {
            _frames.Enqueue((name, _runSeconds + FrameDelaySeconds, state));
        }
    }

    private void SaveDueFrames()
    {
        while (_frames.Count > 0 && _frames.Peek().Due <= _runSeconds)
        {
            var (name, _, state) = _frames.Dequeue();
            _hud.Hide();
            var path = JourneyFrameNames.PathFor(_stem!, name);
            var saved = ScreenshotWriter.Save(
                GetViewport(),
                path,
                LogCategory,
                ("frame", name),
                ("state", state),
                ("developer_hud", _hud.Showing() ? "VISIBLE" : "hidden"));
            Log.Info(LogCategory, (saved ? "autopilot: review frame " : "autopilot: the review frame could not be saved: ") + name + ": " + path);
            if (saved)
            {
                _framesSaved++;
            }
            else
            {
                _framesNotTaken.Add(name);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ the way out

    /// <summary>Records the first failure (the flow logs off before it is reported) and finishes at once when there is nothing to log off.</summary>
    private void Fail(IGameSessionView? session, string why)
    {
        if (_flow.Fail(session, why, _phase == Phase.Session ? null : _phase.ToString()))
        {
            Finish(why);
        }
    }

    private void Finish(string? why)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _hud.Restore();
        if (_logic is { } logic)
        {
            PrintNewLines(logic);
        }

        var ok = _flow.Failure is null && why is null && _scenarioEnded && _scenarioFailure is null && Autopilot is { Ok: true };
        var failure = _flow.Failure ?? why ?? _scenarioFailure ?? "none";
        Log.Info(
            LogCategory,
            $"autopilot leg done ok={(ok ? "true" : "false")} seconds={_runSeconds.ToString("0.0", CultureInfo.InvariantCulture)} cycles={Autopilot?.Cycles ?? 0} stop={Autopilot?.StopName ?? "n/a"} frames_saved={_framesSaved} frames_not_taken={(_framesNotTaken.Count == 0 ? "none" : string.Join(',', _framesNotTaken))} failure={failure}");
        if (!ok)
        {
            Log.Error(LogCategory, "the autopilot leg failed", ("phase", _flow.FailedAt), ("why", failure == "none" ? "the autopilot's done line did not say ok=yes" : failure));
        }

        if (_kit?.Runners is { StreamerStats.Length: > 0 } runners)
        {
            Log.Info(LogCategory, "autopilot runner: " + runners.StreamerStats);
        }

        Log.FlushToGodot();
        GetTree().Quit(ok ? 0 : 1);
    }
}

using System.Globalization;
using Godot;
using Lifestone.Client.Options;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Game.Core;
using Lifestone.Game.PortalSpace;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// <c>--login-check=climber-start</c> (M6 onscreen start; owner ruling 2026-11-19: the sprint's whole run, from deleting and re-creating the climber through her start-up to the level-up climb, in one game window, one process, recorded from its first
/// frame). Three parts in one session, in order, each checked before the next:
/// <list type="number">
/// <item><b>The creation</b>: <see cref="ClimberCreateScenario"/> on screen (<see cref="ClimberScreens"/>): the live climber deleted on the character screen, the new one made through character creation's pages to the climber's template, entered, checked
/// and the old copy purged; its verdict line is the headless step's (<c>[climber] verdict fresh= purged= granted= template= level=</c>), then <c>climber start: creation done ok=</c>. With <c>--climber-start-wait-file</c> the run first holds at the
/// character list until that file exists (the recorder's go), so a recording holds the whole creation.</item>
/// <item><b>The start-up journey</b>: the climber's plan (<see cref="JourneyPlans.Climbers"/>: the Training Academy and the Pathwarden quest) in this session, built as the journey check builds it (<see cref="JourneyLegRunner.BuildParts"/>,
/// the buff bot's facts set as the headless host sets them); its lines are printed as the journey check prints them (<c>[logincheck] journey station= ...</c>, <c>journey done ok=</c>, <c>journey climber cheat_free=</c>).</item>
/// <item><b>The gate, then the level up job</b>: <see cref="ClimberStartGate"/> over those lines (<c>climber start: gate level_up=allowed|refused ...</c>); only when it allows does the play host's autopilot get installed here
/// (<see cref="AutopilotHost.Install"/>, with the command line's <c>--autopilot-*</c> options; with <c>--autopilot-start</c> the level up job starts at once and ends the process as any unattended run does).</item>
/// </list>
/// Any failure, a time limit or a refused gate logs off (when in the world) and quits with exit code 1, after <c>climber start: done ok=false stage= why=</c>.
/// </summary>
public sealed partial class ClimberStartRunner : Node
{
    /// <summary>The longest the run holds at the character list for <c>--climber-start-wait-file</c>, in seconds; then it goes on and says so.</summary>
    public const double WaitFileSeconds = 300.0;

    /// <summary>The creation's limit, in seconds from the character list (the throwaway check's whole limit, which also makes a character through the screens).</summary>
    public const double CreationSeconds = ThrowawayLegRunner.WholeCheckSeconds;

    /// <summary>The seconds the player stays present before the journey starts (the journey check's settle).</summary>
    public const double SettleSeconds = LegSessionFlow.SettleSeconds;

    /// <summary>How long the log off of a failed run may take before the game quits anyway, in seconds.</summary>
    public const double LogOffSeconds = AutopilotHost.LogOffWaitSeconds;

    /// <summary>The seconds the quit's last packets get before the process ends.</summary>
    public const double QuitFlushSeconds = LegSessionFlow.QuitFlushSeconds;

    /// <summary>The start of the runner's own lines (climb.ps1 reads them).</summary>
    public const string LinePrefix = "climber start: ";

    private const string LogCategory = ClimberCreateScenario.LogCategory;
    private const string JourneyCategory = "logincheck";

    private enum Phase
    {
        Loading,
        List,
        WaitFile,
        Creating,
        Settling,
        Journey,
        HandedOff,
        Ending,
    }

    private readonly Lifestone.Client.Session.Hosting.LegFaultGuard _guard = new("ClimberStartRunner", LogCategory, GodotLogSink.Instance);
    private Main _main = null!;
    private GameServices _services = null!;
    private SessionHost _host = null!;
    private PlayMode _mode = null!;
    private RetailUiLayer _ui = null!;
    private DatSet _dats = null!;
    private JourneyPlan _plan = null!;
    private IReadOnlyList<string> _arguments = [];
    private HuntingGround _ground = null!;
    private string? _waitFile;
    private string? _optionError;
    private bool _buffBot;
    private Task<(IReadOnlyList<uint> Classes, MageTemplate.ClimberCreationPlan Plan)>? _creationLoad;
    private Task<IReadOnlyDictionary<uint, SpellInfo>>? _spells;
    private Task<RoomGraph>? _den;
    private ClimberCreateScenario? _creation;
    private JourneyScenario? _journey;
    private GodotJourneyRunnerHost? _runners;
    private int _linesPrinted;
    private Phase _phase = Phase.Loading;
    private double _runSeconds;
    private double _phaseStart;
    private double _journeyLimit;
    private string _stage = "loading";
    private bool _waitLogged;
    private ActionHandle? _endLogOff;
    private double _quitAt = double.NaN;
    private bool _finished;

    /// <summary>Adds the run to the scene; it starts with the scene's first frame.</summary>
    public static void Install(Main main, GameServices services, SessionHost host, PlayMode mode, RetailUiLayer? ui)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(mode);
        if (ui is null)
        {
            Log.Error(LogCategory, "the climber start clicks the retail screens, and the retail UI is off (--retail-ui=off); it cannot run");
            main.GetTree().Quit(1);
            return;
        }

        var dats = services.Dats ?? throw new InvalidOperationException("the climber start needs the dats (Main checks for them first)");
        var arguments = OS.GetCmdlineUserArgs();
        var parsed = CommandLineArgument.ParseAll(arguments);
        var account = string.IsNullOrWhiteSpace(host.Options.Account) ? Lifestone.Client.Session.Credentials.TestAccounts.Test : host.Options.Account;
        var plan = JourneyPlans.Climbers.FirstOrDefault(p => string.Equals(p.Account, account, StringComparison.Ordinal));
        var errors = new List<string>(services.Options.Play.Errors);
        var autopilot = AutopilotOptions.FromArguments(parsed);
        errors.AddRange(autopilot.Errors);
        if (plan is null)
        {
            errors.Add("--login-check=climber-start: the account is not a climber's (the climbers' plans name test and qwinnie)");
        }

        string? waitFile = null;
        if (services.Options.Play.ClimberStartWaitFile is { } wait)
        {
            if (Lifestone.Client.Diagnostics.OutputPaths.TryResolve(wait, Diagnostics.ScreenshotWriter.RepositoryRoot, out var full, out var problem))
            {
                waitFile = full;
            }
            else
            {
                errors.Add("--climber-start-wait-file " + problem);
            }
        }

        foreach (var error in errors)
        {
            Log.Error(LogCategory, LinePrefix + error);
        }

        var runner = new ClimberStartRunner
        {
            Name = "ClimberStartRunner",
            ProcessPriority = 100, // after the session host (-900), play mode (-800) and the retail UI: it looks at what they did this frame
            _main = main,
            _services = services,
            _host = host,
            _mode = mode,
            _ui = ui,
            _dats = dats,
            _plan = plan ?? JourneyPlans.Climber,
            _arguments = arguments,
            _ground = JourneyLimits.FightOf(arguments).GroundProfile,
            _waitFile = waitFile,
            _buffBot = autopilot.BuffBot,
            _optionError = errors.Count == 0 ? null : string.Join("; ", errors),
        };
        runner._journeyLimit = JourneyLegRunner.WholeSecondsFor(runner._plan, JourneyLimits.FightOf(arguments));
        runner._creationLoad = Task.Run(() => ((IReadOnlyList<uint>)HeadlessCharacter.RetailSkillClasses(dats), ClimberCreateScenario.PlanFromDats(dats, runner._buffBot)));
        (runner._spells, runner._den) = JourneyLegRunner.StartLoads(dats, runner._plan, runner._ground);
        main.AddChild(runner);
        Log.Info(
            LogCategory,
            LinePrefix + $"installed: {runner._plan.Character} on the account '{runner._plan.Account}' is re-created through the retail screens, her journey '{runner._plan.Name}' runs in this session, and the level up job follows in this window only when the gate allows",
            ("wait_file", waitFile is null ? "none" : "given"),
            ("autopilot_start", AutopilotHostOptions.FromArguments(parsed).StartAtLogin),
            ("journey_limit_s", runner._journeyLimit),
            ("creation_limit_s", CreationSeconds));
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
            // Reported once; the run ends through its own fail path (log off if in the world, then a non-zero quit).
            if (_guard.Catch($"_Process ({_phase}, {_stage})", e))
            {
                Fail(_guard.Reason!);
            }
            else
            {
                Exit(1);
            }
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree() => ReleaseJourney();

    private void StepFrame(double delta)
    {
        if (_finished || _phase == Phase.HandedOff)
        {
            return;
        }

        _runSeconds += delta;
        if (_host.Session is not { } session)
        {
            Fail("the session host has no session");
            Exit(1);
            return;
        }

        if (_phase == Phase.Ending)
        {
            StepEnding(session);
            return;
        }

        if (_optionError is not null)
        {
            Fail("bad option: " + _optionError);
            return;
        }

        switch (_phase)
        {
            case Phase.Loading:
                Loading(session);
                break;
            case Phase.List:
                List(session);
                break;
            case Phase.WaitFile:
                WaitForFile(session);
                break;
            case Phase.Creating:
                Creating(session);
                break;
            case Phase.Settling:
                Settling(session);
                break;
            case Phase.Journey:
                StepJourney(session);
                break;
        }
    }

    // ------------------------------------------------------------------------------------------------ the creation

    private void Loading(IGameSessionView session)
    {
        if (_creationLoad is not { IsCompleted: true } load || _spells is not { IsCompleted: true } || (_den is not null && !_den.IsCompleted))
        {
            return;
        }

        if (load.IsFaulted || _spells.IsFaulted || _den is { IsFaulted: true })
        {
            Fail("the dats could not be read for the creation plan, the spell table or the den's rooms: " + (load.Exception ?? _spells.Exception ?? _den?.Exception)?.GetBaseException().Message);
            return;
        }

        var (classes, plan) = load.Result;
        _creation = new ClimberCreateScenario(classes, plan, GodotLogSink.Instance, climber: _plan.Character, orderLine: MageTemplate.ClimberOrderLine(_buffBot), screens: new ClimberScreens(_ui, session));
        Go(Phase.List, "list");
    }

    /// <summary>The character list, by the game's own login, before anything is clicked: the wait file's hold (when asked for) begins there.</summary>
    private void List(IGameSessionView session)
    {
        var state = session.State;
        if (BuffLegPhases.Failed(state))
        {
            Fail("the login failed (" + (state.FailureMessage ?? "no reason given") + ")");
            return;
        }

        if (!BuffLegPhases.AtCharacterSelect(state))
        {
            if (_runSeconds - _phaseStart > ClimberCreateScenario.ListSeconds)
            {
                Fail(string.Create(CultureInfo.InvariantCulture, $"the character list did not come within {ClimberCreateScenario.ListSeconds:0} s (phase {state.Phase})"));
            }

            return;
        }

        if (_waitFile is null)
        {
            Go(Phase.Creating, "creating");
            return;
        }

        Log.Info(LogCategory, LinePrefix + string.Create(CultureInfo.InvariantCulture, $"holding at the character list until the recorder's go file exists (at most {WaitFileSeconds:0} s)"));
        Go(Phase.WaitFile, "wait-file");
    }

    private void WaitForFile(IGameSessionView session)
    {
        if (System.IO.File.Exists(_waitFile))
        {
            Log.Info(LogCategory, LinePrefix + string.Create(CultureInfo.InvariantCulture, $"the recorder's go file is there after {_runSeconds - _phaseStart:0.0} s; the creation begins"));
            Go(Phase.Creating, "creating");
            return;
        }

        if (_runSeconds - _phaseStart > WaitFileSeconds)
        {
            Log.Warn(LogCategory, LinePrefix + string.Create(CultureInfo.InvariantCulture, $"TODO(m6-onscreen-record-go): no go file after {WaitFileSeconds:0} s; the creation begins anyway (a recording, if any, may miss its start)"));
            Go(Phase.Creating, "creating");
        }
    }

    private void Creating(IGameSessionView session)
    {
        var creation = _creation!;
        if (_runSeconds - _phaseStart > CreationSeconds)
        {
            Fail(string.Create(CultureInfo.InvariantCulture, $"the creation took more than {CreationSeconds:0} s (stage {creation.Stage})"));
            return;
        }

        _stage = creation.Stage;
        switch (creation.Step(session, PortalSpaceSetup.Now()))
        {
            case ScenarioStatus.Failed:
                Log.Info(LogCategory, LinePrefix + "creation done ok=false why=" + (creation.Failure ?? "the creation failed"));
                Fail("the creation failed: " + (creation.Failure ?? "no reason given"));
                break;
            case ScenarioStatus.Succeeded:
                Log.Info(LogCategory, LinePrefix + "creation done ok=true " + creation.VerdictFields());
                Go(Phase.Settling, "settling");
                break;
        }
    }

    private void Settling(IGameSessionView session)
    {
        var state = session.State;
        if (!BuffLegPhases.InWorld(state) || state.Self.Travel != SelfTravel.Present || state.Self.Hidden)
        {
            if (_runSeconds - _phaseStart > LoginScenario.DefaultArrivalSeconds)
            {
                Fail("settling: the climber did not stay in the world (phase " + state.Phase + ", travel " + state.Self.Travel + ")");
            }

            return;
        }

        if (_runSeconds - _phaseStart >= SettleSeconds)
        {
            BeginJourney(session);
        }
    }

    // ------------------------------------------------------------------------------------------------ the journey

    private void BeginJourney(IGameSessionView session)
    {
        var (factory, context, runners) = JourneyLegRunner.BuildParts(_plan, session, _spells!.Result, _arguments, _dats, _host.Options.Account, _mode, _services, _den, _ground, observer: null, windows: null);
        _runners = runners;
        _journey = new JourneyScenario(_plan, factory, context, endSession: false); // the session goes on: the level up job follows in this window
        Log.Info(LogCategory, LinePrefix + $"the journey '{_plan.Name}' begins in this session (buff bot stop: {(context.Facts.BuffBotOn?.Invoke() is true ? "on" : "off")}, word '{context.Facts.BuffBotProfile.Key}')");
        Go(Phase.Journey, "journey");
    }

    private void StepJourney(IGameSessionView session)
    {
        var journey = _journey!;
        if (_runSeconds - _phaseStart > _journeyLimit)
        {
            PrintNewLines(journey);
            Fail(string.Create(CultureInfo.InvariantCulture, $"the journey took more than {_journeyLimit:0} s (stage {journey.Stage})"));
            return;
        }

        _stage = "journey:" + journey.Stage;
        var status = journey.Step(session, PortalSpaceSetup.Now());
        PrintNewLines(journey);
        switch (status)
        {
            case ScenarioStatus.Failed:
                Fail("the journey failed: " + (journey.Failure ?? "no reason given"));
                break;
            case ScenarioStatus.Succeeded:
                Gate(journey);
                break;
        }
    }

    /// <summary>Prints each journey line the scenario wrote since the last frame, as the journey check prints them (<c>[logincheck] journey ...</c>).</summary>
    private void PrintNewLines(JourneyScenario journey)
    {
        var lines = journey.Lines;
        while (_linesPrinted < lines.Count)
        {
            Log.Info(JourneyCategory, lines[_linesPrinted++]);
        }
    }

    // ------------------------------------------------------------------------------------------------ the gate and the level up job

    private void Gate(JourneyScenario journey)
    {
        var creationVerdict = "verdict " + _creation!.VerdictFields();
        var done = journey.Lines.LastOrDefault(l => l.StartsWith("journey done ", StringComparison.Ordinal));
        var climber = journey.Lines.LastOrDefault(l => l.StartsWith("journey climber ", StringComparison.Ordinal));
        var decision = ClimberStartGate.Decide(_creation.Stage == "done", creationVerdict, done, climber);
        Log.Info(LogCategory, decision.Line(creationVerdict, climber));
        ReleaseJourney();
        if (!decision.Allowed)
        {
            Fail("the gate refused the level up job: " + decision.Why);
            return;
        }

        var start = AutopilotHostOptions.FromArguments(CommandLineArgument.ParseAll(_arguments)).StartAtLogin;
        _phase = Phase.HandedOff;
        _stage = "handed-off";
        AutopilotHost.Install(_main, _services, _host, _mode, _ui);
        Log.Info(
            LogCategory,
            LinePrefix + (start
                ? "the play host's autopilot is installed in this window and --autopilot-start starts the level up job now; it ends the process as an unattended run does"
                : "the play host's autopilot is installed in this window; without --autopilot-start the dial stays off (/auto in the chat starts it)"));
    }

    private void ReleaseJourney()
    {
        _journey?.Dispose();
        _journey = null;
        _runners?.Dispose();
        _runners = null;
    }

    // ------------------------------------------------------------------------------------------------ the way out

    /// <summary>The first failure: said once with its stage, then the log off (when in the world) and a quit with exit code 1.</summary>
    private void Fail(string why)
    {
        if (_phase == Phase.Ending || _finished)
        {
            return;
        }

        Log.Error(LogCategory, LinePrefix + "the run failed", ("stage", _stage), ("why", why));
        Log.Info(LogCategory, LinePrefix + "done ok=false stage=" + _stage + " why=" + why);
        ReleaseJourney();
        _phase = Phase.Ending;
        _phaseStart = _runSeconds;
        if (_host.Session is { } session && BuffLegPhases.InWorld(session.State))
        {
            Log.Info(LogCategory, LinePrefix + "logging off before the run ends as failed");
            _endLogOff = session.Actions.LogOff(ActionSource.Host);
        }
    }

    private void StepEnding(IGameSessionView session)
    {
        if (!double.IsNaN(_quitAt))
        {
            if (_runSeconds - _quitAt >= QuitFlushSeconds)
            {
                Exit(1);
            }

            return;
        }

        if (_endLogOff is { IsCompleted: false } && _runSeconds - _phaseStart < LogOffSeconds)
        {
            return;
        }

        if (_endLogOff is { } handle)
        {
            Log.Info(LogCategory, LinePrefix + (handle.IsCompleted ? "the failed run logged off: " + handle.Outcome?.Kind : string.Create(CultureInfo.InvariantCulture, $"the log off was not confirmed within {LogOffSeconds:0} s; quitting anyway")));
        }

        _ = session.Actions.Quit(ActionSource.Host);
        _quitAt = _runSeconds;
    }

    private void Exit(int code)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        Log.FlushToGodot();
        GetTree().Quit(code);
    }

    private void Go(Phase next, string stage)
    {
        _phase = next;
        _stage = stage;
        _phaseStart = _runSeconds;
    }
}

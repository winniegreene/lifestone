using System.Globalization;
using System.Security.Cryptography;
using Godot;
using Lifestone.Client.Options;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Data.Checkout;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Data.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.PortalSpace;
using Lifestone.Game.RetailUi;
using Lifestone.Client.Movement.Wayfinding;

namespace Lifestone.Game.Sessions;

/// <summary>
/// <c>--login-check=journey --journey=mage|melee|archer</c> (M4 P5b J5; docs/session.md section 40): the game enters the plan's character on the plan's test account and runs a <see cref="JourneyScenario"/> in
/// its own session (the stations in one login, on foot between them), then logs off and quits. A kind of its own, never part of <c>--login-check=all</c> (hooked by one line in <see cref="LoginCheckRunner.Install"/>, as
/// <see cref="HuntLegRunner"/> is, which this node is modelled on). The script is the scenario's and the stations' (the catalog is <see cref="JourneyStations"/>); this node walks the character list and the arrival, steps the
/// journey once per frame after the session host and play mode, gives the journey the player's own body as the runner's walker (<see cref="GodotJourneyRunnerHost"/>) and reads what only the engine can see: the review frames.
/// </summary>
/// <remarks>
/// <para>Every journey line is printed prefixed <c>[logincheck]</c> (the category's prefix): <c>journey station=&lt;name&gt; ok= s= ...</c> per station as it ends and the <c>journey done ok= ...</c> line; the process ends with 0
/// only when the journey's verdict held. A failure at any stage logs off before it is reported.</para>
/// <para><b>The review frames</b> (<c>--play-screenshot=PATH</c>, 800x600, the developer HUD hidden): <c>&lt;stem&gt;-station-&lt;name&gt;.png</c> at each station's end (<see cref="JourneyFrameObserver"/>); inside the hunt
/// and vendor stations the retail-UI slots whose triggers are readable from the session (<c>corpse</c>, <c>heal</c>, <c>potion</c>, <c>vendor</c>: <see cref="HuntUiShotPlan"/>), named by station
/// (<c>&lt;stem&gt;-hunt-heal.png</c>); the frame times per station are logged as <c>journey frames station=&lt;name&gt; frame_max_ms= frames=</c> (report-only; <c>frames=contended</c> is the parallel phase's flag, set by smoke).
/// Since M4 4.2 J1 the windows of the retired hunt leg are sampled too (<see cref="JourneyWindowSamplers"/>, through the context): the hunt station's line carries <c>attack_frame_max_ms= loot_frame_max_ms=</c>, the vendor and
/// vendor-facts stations' <c>vendor_frame_max_ms=</c> (smoke judges them; the headless host prints <c>n/a</c>).
/// Since J7 the other slots are taken too (JourneyLegRunner.StationShots.cs): the hunt's melee window frames (<c>c1-combat</c>, <c>c1-powerbar</c>, <c>meter</c>) with the review camera and its side turn, the den fight's <c>bolt</c>, <c>hit</c> and <c>kill</c>,
/// the death's and the buff's <c>mid-cast</c>, and with <c>--journey-shots</c> the showpiece bursts (<c>&lt;stem&gt;-&lt;station&gt;-&lt;event&gt;-NN.png</c>); a slot whose trigger never held is logged by name at the end.</para>
/// <para><b>Limits.</b> <see cref="WholeSecondsFor"/>: the login and the arrival, then one and a half times the plan's expected seconds plus a margin, and (M5 1 fix B) the raise of a long den fight (<see cref="JourneyLimits"/>).</para>
/// </remarks>
public sealed partial class JourneyLegRunner : Node
{
    /// <summary>The seconds the login and the arrival may take before the journey starts.</summary>
    public const double LoginSeconds = 60.0;

    /// <summary>The margin added to the plan's expected seconds (known-state recovery, the station cuts' slack).</summary>
    public const double MarginSeconds = 300.0;

    /// <summary>The whole check's limit for <paramref name="plan"/>: the login, then one and a half times the plan's expected seconds and the margin; with <paramref name="fight"/> raised over its default (<c>--fight-minutes</c>, a long <c>--fight-seconds</c>) the raise is added for each den fight (<see cref="JourneyLimits.WholeExtraSeconds"/>, M5 1 fix B).</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="fight">The fight options of the command line, or null for the defaults.</param>
    public static double WholeSecondsFor(JourneyPlan plan, FightOptions? fight = null) => LoginSeconds + (1.5 * plan.ExpectedSeconds) + MarginSeconds + JourneyLimits.WholeExtraSeconds(plan, fight ?? new FightOptions());

    private const string LogCategory = "logincheck";
    private enum Phase
    {
        Loading,
        Session, // the login to the world, and the log off and quit: LegSessionFlow (M5 4 h1: shared with the autopilot check)
        Journey,
    }

    private SessionHost _host = null!;
    private readonly Lifestone.Client.Session.Hosting.LegFaultGuard _guard = new("JourneyLegRunner", LogCategory, GodotLogSink.Instance);
    private PlayMode _mode = null!;
    private RetailUiLayer? _ui;
    private DatSet _dats = null!;
    private GameServices _services = null!;
    private JourneyPlan? _plan;
    private IReadOnlyList<string> _arguments = [];
    private string? _optionError;
    private double _wholeSeconds;
    private Task<IReadOnlyDictionary<uint, SpellInfo>>? _spells;
    private Task<RoomGraph>? _den;
    private HuntingGround _ground = null!;
    private JourneyScenario? _scenario;
    private GodotJourneyRunnerHost? _runners;
    private JourneyFrameObserver _observer = null!;
    private readonly JourneyWindowSamplers _windows = new(); // M4 4.2 J1: attack_frame_max_ms, loot_frame_max_ms, vendor_frame_max_ms of the running station
    private LegSessionFlow _flow = null!;

    private bool _failed => _flow.Failed; // the fail path's flag lives in the shared flow (M5 4 h1)
    private Phase _phase = Phase.Loading;
    private double _runSeconds;
    private bool _finished;
    private bool _scenarioEnded;
    private bool _skipNextFrame;
    private int _linesPrinted;
    private int _framesSaved;
    private string? _stem;
    private bool _film;
    private readonly List<string> _notTaken = [];
    private DeveloperHudHider _hud = null!;

    // The hunt and vendor stations' retail-UI slots that read from the session (the plan is the hunt leg's, reset at each station).
    private HuntUiShotPlan _uiPlan = new();
    private string? _uiStation;

    /// <summary>Adds the check to the scene; it starts with the scene's first frame.</summary>
    public static void Install(Main main, GameServices services, SessionHost host, PlayMode mode, RetailUiLayer? ui)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(mode);
        var dats = services.Dats ?? throw new InvalidOperationException("the journey needs the dats (Main checks for them first)");
        var play = services.Options.Play;
        var plan = JourneyPlans.ForName(play.Journey);
        var errors = new List<string>(play.Errors);
        var ground = JourneyLimits.FightOf(OS.GetCmdlineUserArgs()).GroundProfile; // a bad --fight-ground is the station factory's error and keeps the default
        if (plan is null)
        {
            errors.Add("--journey: " + (play.Journey is null ? "none given or refused (see the option errors)" : "'" + play.Journey + "' is not a plan"));
        }
        else if (!string.Equals(host.Options.Account, plan.Account, StringComparison.Ordinal) && !(string.IsNullOrWhiteSpace(host.Options.Account) && plan.Account == "test"))
        {
            errors.Add($"--account: the {plan.Name} journey runs on the account '{plan.Account}'");
        }

        foreach (var error in errors)
        {
            Log.Error(LogCategory, "journey: " + error);
        }

        var runner = new JourneyLegRunner
        {
            Name = "JourneyLegRunner",
            ProcessPriority = 100, // after the session host (-900), play mode (-800) and the retail UI: it looks at what they did this frame
            _host = host,
            _mode = mode,
            _ui = ui,
            _dats = dats,
            _services = services,
            _plan = plan,
            _arguments = OS.GetCmdlineUserArgs(),
            _ground = ground,
            _optionError = errors.Count == 0 ? null : "bad journey option: " + string.Join("; ", errors),
            _wholeSeconds = plan is null ? LoginSeconds : WholeSecondsFor(plan, JourneyLimits.FightOf(OS.GetCmdlineUserArgs())),
            _stem = play.BuffShotPath,
            _film = play.JourneyFilm,
        };
        runner._hud = new DeveloperHudHider(runner);
        runner._flow = new LegSessionFlow("journey", LogCategory, plan?.Character ?? string.Empty);
        runner._observer = new JourneyFrameObserver(runner._stem, play.JourneyShots, GodotLogSink.Instance);

        (runner._spells, runner._den) = StartLoads(dats, plan, ground);

        main.AddChild(runner);
        Log.Info(
            LogCategory,
            $"login check 'journey' installed: the test account enters {plan?.Character ?? "(no plan)"}, the {plan?.Name ?? "?"} journey runs in the game's session (the player's own body is the runner's walker), then the game logs off and quits",
            ("scenario", JourneyScenario.ScenarioName),
            ("whole_check_s", runner._wholeSeconds),
            ("review_frames", runner._stem is null ? "not asked for" : "station-<name> at each station's end"),
            ("journey_shots", play.JourneyShots.Enabled),
            ("journey_film", play.JourneyFilm));
    }

    /// <summary>The spell table and (for a plan with a den station) the den.s room graph, read on workers so the game thread does not load them in the first frames (the autopilot check calls this too).</summary>
    internal static (Task<IReadOnlyDictionary<uint, SpellInfo>> Spells, Task<RoomGraph>? Den) StartLoads(DatSet dats, JourneyPlan? plan, HuntingGround ground)
    {
        var spells = Task.Factory.StartNew<IReadOnlyDictionary<uint, SpellInfo>>(() => SpellTableData.Load(dats).Spells, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<RoomGraph>? den = null;
        if (plan is not null && plan.Ground == StationWhere.Den)
        {
            den = Task.Factory.StartNew(() => GroundRooms.Load(dats, ground), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        return (spells, den);
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
            // M5 item 0 c2: reported once; the leg ends through its own fail path (log off if logged in, a verdict of ok=false, a non-zero quit).
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

    private void StepFrame(double delta)
    {
        if (_finished)
        {
            return;
        }

        _runSeconds += delta;
        _delta = delta;
        if (!_failed) // M5 0 fix 0-R5: the observers stop with the fail path, so a fault in them cannot repeat and end the run before the log off completes
        {
            _observer.Frame(delta, _skipNextFrame); // the delta is the previous frame's: the frame after a saved review frame (the read-back stalls it) is left out
            _windows.Frame(delta, _skipNextFrame); // the same rule for the attack, loot and vendor windows
        }

        _skipNextFrame = false;
        if (_host.Session is not { } session)
        {
            Finish("the session host has no session");
            return;
        }

        _flow.Seconds = _runSeconds;
        if (_flow.Quitting || _flow.Failed)
        {
            if (_flow.Step(session) == LegFlowStatus.Ended)
            {
                Finish(null); // the quit was flushed, or the failed run could not log off (the flow keeps the failure)
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
            Fail(session, $"the journey took more than {_wholeSeconds:0} s");
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
                        BeginJourney(session);
                        break;
                    case LegFlowStatus.Ended:
                        Finish(null);
                        break;
                }

                break;
            case Phase.Journey:
                StepJourney(session);
                break;
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        RestoreDeveloperHud();
        _runners?.Dispose();
        _windows.Dispose();
    }

    // ------------------------------------------------------------------------------------------------ the way in

    private void Loading()
    {
        if (_spells is not { IsCompleted: true } || (_den is not null && !_den.IsCompleted))
        {
            return;
        }

        if (_spells.IsFaulted || _den is { IsFaulted: true })
        {
            Fail(null, "the spell table or the den's rooms could not be read: " + (_spells.Exception ?? _den?.Exception)?.GetBaseException().Message);
            return;
        }

        _flow.Begin();
        _phase = Phase.Session;
    }
    // ------------------------------------------------------------------------------------------------ the journey

    /// <summary>The login name of the account the run signs in to (the kit hashes it; it is never stored, logged or printed).</summary>
    internal static string LoginName(string? accountKey)
    {
        if (!ServerRuntimePaths.TryFind(System.Environment.GetEnvironmentVariable(ServerRuntimePaths.EnvironmentVariable), CheckoutLocator.DefaultSearchRoots(), out var runtime, out var problem))
        {
            throw new InvalidOperationException(problem ?? "the server runtime folder was not found");
        }

        var secrets = File.ReadAllBytes(runtime.SecretsFile);
        try
        {
            using var credentials = PlayAccounts.Parse(secrets, AccountChoice.FromOption(accountKey, AccountChoice.Default));
            return credentials.Account;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secrets);
        }
    }

    /// <summary>
    /// The journey's parts for a plan in a session: the vitals keeper, the station factory, the Godot runner host (the player's own body is the walker) and the one context (M5 4 h1: the autopilot check calls this too, so the two checks build one way).
    /// </summary>
    internal static (IStationFactory Factory, JourneyContext Context, GodotJourneyRunnerHost Runners) BuildParts(JourneyPlan plan, IGameSessionView session, IReadOnlyDictionary<uint, SpellInfo> spells, IReadOnlyList<string> arguments, DatSet dats, string? account, PlayMode mode, GameServices services, Task<RoomGraph>? den, HuntingGround ground, IJourneyObserver? observer, IJourneyWindowFrames? windows)
    {
        var log = GodotLogSink.Instance;
        var keeper = JourneyStations.CreateKeeper(plan, session, spells, arguments, log);
        var factory = JourneyStations.Create(plan, arguments, dats, LoginName(account));
        var runners = new GodotJourneyRunnerHost(mode, services, dats, den, ground, log);
        var context = JourneyScenario.NewContext(plan, log, spells, keeper, dats, runners, observer, windowFrames: windows, dungeon: ground);
        return (factory, context, runners);
    }

    private void BeginJourney(IGameSessionView session)
    {
        var plan = _plan!;
        var (factory, context, runners) = BuildParts(plan, session, _spells!.Result, _arguments, _dats, _host.Options.Account, _mode, _services, _den, _ground, _observer, _windows);
        _runners = runners;
        // endSession: false, the node logs off itself when the journey ends (the journey ends at Holtburg's lifestone).
        _scenario = new JourneyScenario(plan, factory, context, endSession: false);
        if (_stem is not null || _film)
        {
            HideDeveloperHud();
        }

        _phase = Phase.Journey;
    }

    private void StepJourney(IGameSessionView session)
    {
        var scenario = _scenario!;
        var before = _observer.Current;
        var status = scenario.Step(session, PortalSpaceSetup.Now());
        _windows.Track(session, scenario.CurrentRun); // after the step: a window's open frame does not count, its settling frame does (the samplers' rule)
        PrintNewLines(scenario);
        StepStationShots(session, scenario);
        StepInStationFrames(session, scenario);
        StepHeroPanels(session, scenario); // --journey-film: the hero video's panels (JourneyLegRunner.HeroPanels.cs)
        SaveRequested(session);
        if (before is not null && _observer.Current is null)
        {
            ReportStationEnd(before);
        }

        switch (status)
        {
            case ScenarioStatus.Failed:
                _scenarioEnded = true;
                Fail(session, scenario.Failure ?? "the journey failed");
                break;
            case ScenarioStatus.Succeeded:
                _scenarioEnded = true;
                _flow.BeginLogOff(session);
                _phase = Phase.Session;
                break;
        }
    }

    /// <summary>Prints each journey line the scenario wrote since the last frame, prefixed by the category (<c>[logincheck] journey station=...</c>).</summary>
    private void PrintNewLines(JourneyScenario scenario)
    {
        var lines = scenario.Lines;
        while (_linesPrinted < lines.Count)
        {
            Log.Info(LogCategory, lines[_linesPrinted++]);
        }
    }

    private void ReportStationEnd(string station)
    {
        if (_stem is null)
        {
            Log.Info(LogCategory, $"journey frames station={station} {_observer.FrameFields(station)}");
            return;
        }

        Log.Info(LogCategory, $"journey frames station={station} {_observer.FrameFields(station)}");
        foreach (var slot in JourneyFrameNames.SlotsOf(station))
        {
            var meleeOnly = station == JourneyStationNames.Hunt && ArcherPlan && HuntUiShotPlan.Names.AsSpan(0, 3).Contains(slot); // the Archer's missile hunt takes no melee window frame
            if (!meleeOnly && !_observer.Requested.Contains(JourneyFrameNames.InStation(station, slot)))
            {
                _notTaken.Add(JourneyFrameNames.InStation(station, slot));
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ the review frames

    /// <summary>Saves the frames the observer asked for (the station-end frames, J7's event frames) and the retail-UI slots this node asked itself for.</summary>
    private void SaveRequested(IGameSessionView session)
    {
        while (_observer.TryDequeue(out var request))
        {
            HideDeveloperHud();
            var saved = ScreenshotWriter.Save(
                GetViewport(),
                request.Path,
                LogCategory,
                ("frame", "journey-" + request.Name),
                ("station", request.Station),
                ("kind", request.Kind.ToString()),
                ("developer_hud", HudShowing() ? "VISIBLE" : "hidden"));
            Log.Info(LogCategory, (saved ? "journey: review frame " : "journey: the review frame could not be saved: ") + request.Name + ": " + request.Path);
            _skipNextFrame = true;
            if (saved)
            {
                _framesSaved++;
            }
        }
    }

    /// <summary>
    /// The hunt and vendor stations' retail-UI slots that read from the session alone (<see cref="HuntUiShotPlan"/>): <c>corpse</c> (the kill's corpse open with its container window drawn), <c>heal</c> and <c>potion</c>
    /// (1 s after a heal or a drink that restored points) and <c>vendor</c> (1 s after the sale vendor's stock opened). Named by station. Nothing here changes what the station does.
    /// </summary>
    private void StepStationShots(IGameSessionView session, JourneyScenario scenario)
    {
        if (_stem is null || _observer.Current is not { } station || scenario.CurrentRun is not IStationInner { Scenario: HuntScenario hunt })
        {
            return;
        }

        if (_uiStation != station)
        {
            _uiStation = station;
            _uiPlan = new HuntUiShotPlan();
        }

        var shell = _ui?.Screens?.Flow.CurrentScreen as GamePlayScreen;
        var container = shell?.Root?.GetChildRecursive(GameContainerPanel.WindowId) as GameContainerPanel;
        var combat = shell?.Root?.GetChildRecursive(GameCombatPanel.WindowId) as GameCombatPanel;
        if (station == JourneyStationNames.Hunt && !ArcherPlan)
        {
            TriggerMeleeShots(session, hunt, combat, shell); // M4 P5b J7: the melee window frames (c1-combat, c1-powerbar, meter) of the old hunt leg
        }

        var tally = hunt.HealTally;
        _uiPlan.ObserveHeals(new HealSnapshot(tally.Heals, tally.Potions, tally.Wasted, tally.Points), _runSeconds);
        if (hunt.CorpseOpen)
        {
            _uiPlan.Trigger(HuntUiShotPlan.Corpse, _runSeconds);
        }

        if (session.Vendor is { } stock && hunt.SaleVendor != 0 && stock.Vendor == hunt.SaleVendor)
        {
            _uiPlan.Trigger(HuntUiShotPlan.Vendor, _runSeconds);
        }

        _uiPlan.Expire(_runSeconds);
        var slot = _uiPlan.Due(_runSeconds, 2);
        if (slot < 0)
        {
            return;
        }

        if (slot is >= 0 and <= 2)
        {
            SaveMeleeShot(session, hunt, station, slot, combat, shell);
            return;
        }

        if (slot == HuntUiShotPlan.Corpse && (container?.DrawState ?? ContainerWindowDraw.Hidden) != ContainerWindowDraw.Drawn)
        {
            return; // waits for the container window itself (Expire gives the slot up by name after the hold)
        }

        if (slot == HuntUiShotPlan.Vendor && session.Vendor is null)
        {
            _uiPlan.Drop(slot);
            return;
        }

        HideDeveloperHud();
        var name = JourneyFrameNames.InStation(station, HuntUiShotPlan.Names[slot]);
        var path = JourneyFrameNames.PathFor(_stem, name);
        var saved = ScreenshotWriter.Save(
            GetViewport(),
            path,
            LogCategory,
            ("frame", "journey-" + name),
            ("station", station),
            ("trigger", HuntUiShotPlan.Triggers[slot]),
            ("target", hunt.TargetName.Length == 0 ? "n/a" : hunt.TargetName),
            ("developer_hud", HudShowing() ? "VISIBLE" : "hidden"));
        Log.Info(LogCategory, (saved ? "journey: review frame " : "journey: the review frame could not be saved: ") + name + " (" + HuntUiShotPlan.Triggers[slot] + "): " + path);
        _skipNextFrame = true;
        if (saved)
        {
            _uiPlan.MarkTaken(slot);
            _framesSaved++;
            _observer.NoteTaken(name);
        }
        else
        {
            _uiPlan.MarkFailed(slot, "the frame could not be saved");
        }
    }

    private bool HudShowing() => _hud.Showing();

    private void HideDeveloperHud() => _hud.Hide();

    private void RestoreDeveloperHud() => _hud.Restore();

    // ------------------------------------------------------------------------------------------------ the way out

    /// <summary>Records the first failure (the flow logs off before it is reported, so ACE does not keep the character in the world for the next run) and finishes at once when there is nothing to log off.</summary>
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
        RestoreDeveloperHud();
        var scenario = _scenario;
        if (scenario is not null)
        {
            PrintNewLines(scenario);
        }

        var ok = _flow.Failure is null && why is null && _scenarioEnded && scenario is not null && scenario.Verdict.Ok(scenario.Context.Facts);
        Log.Info(LogCategory, $"journey leg done ok={(ok ? "true" : "false")} name={_plan?.Name ?? "n/a"} seconds={_runSeconds.ToString("0.0", CultureInfo.InvariantCulture)} frames_saved={_framesSaved} frames_not_taken={(_notTaken.Count == 0 ? "none" : string.Join(',', _notTaken))} failure={_flow.Failure ?? why ?? "none"}");
        if (!ok)
        {
            Log.Error(LogCategory, "the journey leg failed", ("phase", _flow.FailedAt), ("why", _flow.Failure ?? why ?? scenario?.Failure ?? "the journey's verdict did not hold"));
        }

        if (_notTaken.Count > 0)
        {
            Log.Warn(LogCategory, "journey: in-station frames whose trigger never held (report-only): " + string.Join(", ", _notTaken));
        }

        if (_runners is { StreamerStats.Length: > 0 } runners)
        {
            Log.Info(LogCategory, "journey runner: " + runners.StreamerStats);
        }

        Log.FlushToGodot();
        GetTree().Quit(ok ? 0 : 1);
    }
}

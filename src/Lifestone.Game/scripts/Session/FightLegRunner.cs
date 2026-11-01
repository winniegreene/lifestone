using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Godot;
using Lifestone.Client.Audio;
using Lifestone.Client.Avatars;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Checkout;
using Lifestone.Data.Magic;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.PortalSpace;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// <c>--login-check=bsd-fight</c> (M3 3.1 fd; plan section 4.7; docs/session.md section 31): the game enters BSD Mage on the test account and runs <see cref="FightScenario"/> in its own session (the gear, the buff top-up, the
/// teleport to the Aluvian Black Spawn Den, the magic combat mode, Flame Bolt at the tuskers of the arrival's floor until <c>--fight-kills</c> kills or <c>--fight-seconds</c>, non-combat) with the game's own cast and
/// projectile visuals, then logs off and quits. A kind of its own, never part of <c>--login-check=all</c> (hooked by one line in <see cref="LoginCheckRunner.Install"/>, as <see cref="BuffLegRunner"/> is). The script is the scenario's;
/// this node walks the character list and the arrival (the session host has already started the login), steps the scenario once per frame after the session host and play mode, gives it the game's own player as its walker
/// (<see cref="GameFightWalk"/> over a <see cref="RoutePlayHost"/>), and reads what only the engine can see: the worst frames and the lowest fps (<see cref="FightFrameSampler"/>), the projectiles shown, and the three review frames.
/// </summary>
/// <remarks>
/// <para>The verdict is <see cref="FightLegVerdict"/>'s (<c>[logincheck] bsd-fight done ok=...</c>); the process ends with 0 only when it held. A failure at any stage logs off before it is reported (so ACE does not keep the
/// character in the world for the next run). Per frame the node reads counters and allocates nothing; strings are built at stage changes and at the end.</para>
/// <para><b>The loading worker</b> reads the SpellTable, builds the gear kit (the login name is hashed into the kit's component formulas inside the worker and stays in its locals), loads the den's floor graph
/// (<see cref="GroundRooms"/>) and then prepares the methods of the fight's types (<see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/>), so none of that is compiled on the game thread in the den.</para>
/// <para><b>Limits.</b> <see cref="WholeCheckSecondsFor"/>: the login and the arrival 60 s, the gear 120 s, the buff top-up 240 s (a live cycle of 26 casts took 56 s), the teleport and the settle 30 s, the fight's own
/// <c>--fight-seconds</c> (default 300; with <c>--fight-minutes</c> the larger of the two, <c>FightOptions.FailSeconds</c>, M5 1 F1), so 750 s by default; the log off after a failure may take <see cref="RecoverySeconds"/> more.</para>
/// </remarks>
public sealed partial class FightLegRunner : Node
{
    /// <summary>The seconds the login and the arrival may take before the scenario starts.</summary>
    public const double LoginSeconds = 60.0;

    /// <summary>The seconds the gear scenario may take (the wand and the armour on a character that has them: seconds; a first gear: a minute or two).</summary>
    public const double GearSeconds = 120.0;

    /// <summary>The seconds the buff top-up may take (the live cycle of 26 casts took 56 s of session time).</summary>
    public const double TopUpSeconds = 240.0;

    /// <summary>The seconds the teleport to the den and the settle may take.</summary>
    public const double PlaceSeconds = 30.0;

    /// <summary>How long the log off after a failure or the limit may take, in seconds.</summary>
    public const double RecoverySeconds = 120.0;

    /// <summary>The whole check's limit with the default <c>--fight-seconds</c>: 60 + 120 + 240 + 30 + 300 = 750 s.</summary>
    public const double WholeCheckSeconds = LoginSeconds + GearSeconds + TopUpSeconds + PlaceSeconds + FightOptions.DefaultSeconds;

    /// <summary>The whole check's limit for a fight of <paramref name="fightSeconds"/> session seconds: from the first frame, at the limit it fails, logs off and reports.</summary>
    /// <param name="fightSeconds"><c>--fight-seconds</c>.</param>
    public static double WholeCheckSecondsFor(double fightSeconds) => LoginSeconds + GearSeconds + TopUpSeconds + PlaceSeconds + fightSeconds;

    private const string LogCategory = "logincheck";
    private const double ListSeconds = 60.0;
    private const double SettleSeconds = 3.0;
    private const double QuitFlushSeconds = 1.5;

    private enum Phase
    {
        Loading,
        Login,
        Entering,
        Arriving,
        Settling,
        Fight,
        LoggingOff,
        Quitting,
    }

    /// <summary>What one frame's steps held (the session's tick and pump, the retail UI, play mode), kept for the frame that proves slowest.</summary>
    private readonly record struct FrameParts(double SessionTickMs, int PumpMessages, double PumpMs, double RetailUiMs, double PlayModeMs, double PlayWorkMs, double PlayAdapterMs, int EventsApplied);

    private SessionHost _host = null!;
    private readonly Lifestone.Client.Session.Hosting.LegFaultGuard _guard = new("FightLegRunner", LogCategory, GodotLogSink.Instance);
    private PlayMode _mode = null!;
    private RetailUiLayer? _ui;
    private RoutePlayHost _walkHost = null!;
    private Func<uint, float?> _setupRadius = null!;
    private Func<uint, float?> _setupExtent = null!;
    private FightOptions _options = new();
    private HuntingGround _ground = null!;
    private double _wholeSeconds = WholeCheckSeconds;
    private Task<(SpellTableData Table, GearKit Kit, RoomGraph Planner, double BuildMilliseconds, double PlannerMilliseconds)>? _loading;
    private FightScenario? _scenario;
    private string? _optionError;
    private ActionHandle? _pending;
    private Phase _phase = Phase.Loading;
    private double _runSeconds;
    private double _phaseStart;
    private string? _failure;
    private string _failedAt = "start";
    private bool _failed;
    private bool _recoveryStarted;
    private double _recoveryStart = double.NaN;
    private double _quitAt = double.NaN;
    private bool _finished;
    private bool _entered;
    private bool _scenarioEnded;

    // The frames: the sampler, the previous frame's parts (the engine's delta is the previous frame's whole time) and the parts of the slowest fight frame.
    private readonly FightFrameSampler _sampler = new();
    private readonly LightFrameSampler _lights = new(); // M3 4.1 lane c: bolt_light, dyn_lights_max, viewer_light (report-only here)
    private bool _inDenNow;
    private bool _movingNow;
    private System.Numerics.Vector3 _lastPosition;
    private bool _havePosition;
    private AvatarSet? _avatars;
    private long _shownBefore;
    private bool _skipNextFrame;
    private FrameParts _previousParts;
    private FrameParts _worstParts;
    private double _worstFightAt = double.NaN;
    private double _worstFightMs = double.NaN;

    // The review frames (--play-screenshot=PATH without --play-screenshot-after): the bolt in flight, the hit and the kill line.
    private string? _shotPath;
    private readonly FightShotPlan _plan = new();
    private bool _hudHidden;
    private readonly List<(Node Node, bool WasVisible)> _shotHidden = [];

    /// <summary>Adds the check to the scene; it starts with the scene's first frame.</summary>
    public static void Install(Main main, GameServices services, SessionHost host, PlayMode mode, RetailUiLayer? ui)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(mode);
        var dats = services.Dats ?? throw new InvalidOperationException("the fight leg needs the dats (Main checks for them first)");
        var key = host.Options.Account;
        var options = FightOptions.FromArguments(CommandLineArgument.ParseAll(OS.GetCmdlineUserArgs()));
        var ground = options.GroundProfile; // a bad --fight-ground is in options.Errors and keeps the default: the leg fails at its first frame, naming it
        // 3.1-R11: a bad --fight-kills or --fight-seconds fails the leg at its first frame, as the headless host fails its run (no silent default).
        foreach (var error in options.Errors)
        {
            Log.Error(LogCategory, "fight: " + error);
        }

        // The game's player is the walker of the loop's legs: play mode builds the player with this host's sink and the scenario drives its controller (RoutePlay's hosted mode).
        var walkHost = new RoutePlayHost(() => mode.Rig?.Controller.Walker, () => mode.MovementGate?.Held ?? 0);
        mode.ScenarioHost = walkHost;
        var runner = new FightLegRunner
        {
            Name = "FightLegRunner",
            ProcessPriority = 100, // after the session host (-900), play mode (-800) and the retail UI: it looks at what they did this frame
            _host = host,
            _mode = mode,
            _ui = ui,
            _walkHost = walkHost,
            _setupRadius = HostileCreatureFeed.SetupRadii(dats), // each creature's own radius for the walk's creature layer (nav-creature-radius-host)
            _setupExtent = DoorCrossings.SetupExtents(dats), // M5 5 d2: the walk's door feed reads the doors' own sizes
            _options = options,
            _ground = ground,
            _optionError = options.Errors.Count == 0 ? null : "bad fight option: " + string.Join("; ", options.Errors),
            _wholeSeconds = WholeCheckSecondsFor(options.FailSeconds), // M5 1 F1: --fight-minutes raises the fight's own limit
            _shotPath = services.Options.Play.BuffShotPath,
        };

        // The SpellTable, the kit and the den's floor graph are built on a worker: the arrival's frames are never spent on them. The login name the kit's formulas are customised for (hashed, never stored, logged or
        // printed) is read inside that worker and stays in its locals. Then the worker prepares the fight's methods, so the game thread does not compile them in the den.
        runner._loading = Task.Factory.StartNew(
            () =>
            {
                var table = SpellTableData.Load(dats);
                var built = System.Diagnostics.Stopwatch.GetTimestamp();
                var kit = GearKit.Build(table.Spells, LoginName(key), ground: ground); // M5 3 w1 (D46): the kit of the leg's --fight-ground (the vendor robe and wand, the four foci, the ground's stock), not the default alone
                var buildMs = System.Diagnostics.Stopwatch.GetElapsedTime(built).TotalMilliseconds;
                var planning = System.Diagnostics.Stopwatch.GetTimestamp();
                var planner = GroundRooms.Load(dats, ground);
                var plannerMs = System.Diagnostics.Stopwatch.GetElapsedTime(planning).TotalMilliseconds;
                var warm = System.Diagnostics.Stopwatch.GetTimestamp();
                var prepared = PrepareFightMethods();
                Log.Info(LogCategory, "fight: the fight's methods were prepared on the loading worker", ("methods", prepared), ("ms", System.Diagnostics.Stopwatch.GetElapsedTime(warm).TotalMilliseconds));
                return (table, kit, planner, buildMs, plannerMs);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        main.AddChild(runner);
        Log.Info(
            LogCategory,
            "login check 'bsd-fight' installed: the test account enters BSD Mage, the fight scenario runs in the game's session (gear, buff top-up, the den, magic mode, Flame Bolt at the tuskers), then the game logs off and quits",
            ("scenario", FightScenario.ScenarioName),
            ("options", options.ToString()),
            ("whole_check_s", runner._wholeSeconds),
            ("review_frames", runner._shotPath is not null));
    }

    /// <summary>
    /// <see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/> over the methods of the scenario's namespaces (the fight, the buff cycle the top-up runs, the mage's profile and the den, the walk) and of this leg's own
    /// types, on the loading worker: a method the game thread would otherwise compile in the den's first frames. No code is run. Returns the methods prepared.
    /// </summary>
    private static int PrepareFightMethods()
    {
        var prepared = 0;
        foreach (var type in typeof(FightScenario).Assembly.GetTypes())
        {
            if (type.Namespace is not ("Lifestone.Client.Session.Scenarios" or "Lifestone.Client.Session.Mage"))
            {
                continue;
            }

            prepared += PrepareType(type);
        }

        return prepared + PrepareType(typeof(GameFightWalk)) + PrepareType(typeof(FightLegRunner)) + PrepareType(typeof(Lifestone.Client.Movement.Routes.RouteDriver));
    }

    private static int PrepareType(Type type)
    {
        if (type.ContainsGenericParameters)
        {
            return 0;
        }

        var prepared = 0;
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var method in type.GetMethods(all))
        {
            if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() is null)
            {
                continue;
            }

            try
            {
                RuntimeHelpers.PrepareMethod(method.MethodHandle);
                prepared++;
            }
            catch (Exception)
            {
                // A method that cannot be prepared is compiled when it is first called, as it would have been.
            }
        }

        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            prepared += PrepareType(nested);
        }

        return prepared;
    }

    /// <summary>The login name of the account the run signs in to (the kit hashes it; it is never stored, logged or printed).</summary>
    private static string LoginName(string? accountKey)
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
        if (_host.Session is not { } session)
        {
            Finish("the session host has no session");
            return;
        }

        if (!double.IsNaN(_quitAt))
        {
            if (_runSeconds - _quitAt >= QuitFlushSeconds)
            {
                Finish(null);
            }

            return;
        }

        if (_failed)
        {
            StepRecovery(session);
            return;
        }

        if (_optionError is not null)
        {
            Fail(session, _optionError);
            return;
        }

        if (_runSeconds > _wholeSeconds)
        {
            Fail(session, $"the leg took more than {_wholeSeconds:0} s");
            return;
        }

        if (_scenario is not null)
        {
            SampleFrame(delta, session);
        }

        switch (_phase)
        {
            case Phase.Loading:
                Loading();
                break;
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
            case Phase.Fight:
                StepScenario(session);
                break;
            case Phase.LoggingOff:
                LoggingOff(session);
                break;
        }

        _previousParts = TakeParts();
    }

    /// <inheritdoc/>
    public override void _ExitTree() => RestoreDeveloperHud();

    // ------------------------------------------------------------------------------------------------ the way in

    private void Loading()
    {
        if (_loading is not { IsCompleted: true })
        {
            return;
        }

        if (_loading.IsFaulted)
        {
            Fail(null, "the spell table, the account or the den's floor could not be read: " + _loading.Exception?.GetBaseException().Message);
            return;
        }

        Go(Phase.Login);
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
            if (_runSeconds - _phaseStart > ListSeconds)
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
            if (!string.Equals(c.Name, MageTemplate.CharacterName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (c.PendingDeletion)
            {
                Fail(session, "choosing: " + MageTemplate.CharacterName + " is pending deletion (restore it first)");
                return;
            }

            session.Actions.SelectCharacter(c.Guid, ActionSource.Host);
            _pending = session.Actions.EnterWorld(c.Guid, ActionSource.Host);
            Log.Info(LogCategory, "fight: entering the existing character " + MageTemplate.CharacterName);
            Go(Phase.Entering);
            return;
        }

        Fail(session, "choosing: " + MageTemplate.CharacterName + " is not in the character list (run --scenario=mage-build first; the fight leg creates nobody)");
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

        _entered = true;
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

        if (_runSeconds - _phaseStart > LoginScenario.DefaultArrivalSeconds)
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

        if (_runSeconds - _phaseStart >= SettleSeconds)
        {
            BeginScenario(session);
        }
    }

    // ------------------------------------------------------------------------------------------------ the fight scenario

    private void BeginScenario(IGameSessionView session)
    {
        var (table, kit, planner, buildMilliseconds, plannerMilliseconds) = _loading!.Result;
        Log.Info(LogCategory, "fight: the gear kit and the den's floor were built on the loading worker, off the game thread", ("build_ms", buildMilliseconds), ("planner_ms", plannerMilliseconds), ("floor_cells", planner.Cells.Cells.Count));
        var walk = new GameFightWalk(_walkHost, planner, _ground, GodotLogSink.Instance, _setupRadius, GroundTour.For(_ground, planner, GodotLogSink.Instance), _setupExtent);
        _scenario = new FightScenario(kit, table.Spells, new BuffOptions(), _options, walk, GodotLogSink.Instance, LoginScenario.DefaultArrivalSeconds, endSession: false, FightStages.All);
        _avatars = _mode.Avatars;
        if (_avatars is { } avatars)
        {
            _shownBefore = avatars.Stats.ProjectilesShown;
            _plan.Begin(avatars.Stats.ProjectilesCreated, avatars.Stats.ProjectileExplodes, _scenario.Tally.DamageLinesDealt, 0);
            if (_scenario.ReviewFramesWaitForOtherStorey)
            {
                _plan.WaitsForOtherStorey = true; // F3: --fight-storeys-min 2 or more
                Log.Info(LogCategory, "fight: the review frames wait for a storey other than the arrival's (--fight-storeys-min): first, bolt, hit and kill are taken on a storey other than the arrival's; the viewer frame is unchanged");
            }
        }
        else
        {
            Log.Warn(LogCategory, "fight: the avatar set is not running, so the projectile and window counters read 0");
        }

        Go(Phase.Fight);
    }

    private void StepScenario(IGameSessionView session)
    {
        var scenario = _scenario!;
        StepShot(scenario);
        if (_plan.Owed)
        {
            return; // the scenario is held (at most FightShotPlan.HoldSeconds per frame owed) while a review frame is taken, so the bolt, the hit or the kill line is still on screen
        }

        switch (scenario.Step(session, PortalSpaceSetup.Now()))
        {
            case ScenarioStatus.Failed:
                Fail(session, scenario.Failure ?? "the scenario failed");
                break;
            case ScenarioStatus.Succeeded:
                _scenarioEnded = true;
                _pending = session.Actions.LogOff(ActionSource.Host);
                Go(Phase.LoggingOff);
                break;
        }
    }

    /// <summary>One frame of the sampler: the windows and the target, read as flags; allocates nothing. The worst fight frame keeps the previous frame's parts.</summary>
    private void SampleFrame(double delta, IGameSessionView session)
    {
        var scenario = _scenario!;
        var self = session.State.Self;
        var inDen = self.Travel == SelfTravel.Present && self.Position is { } at && _ground.InLandblocks(at.CellId);
        var fighting = scenario.Fighting;
        var avatars = _avatars;
        var before = _sampler.FightFrameMaxMs;
        _sampler.Step(delta, avatars?.CastWindowOpen == true, avatars?.ProjectileWindowOpen == true, fighting && scenario.Target != 0, fighting, inDen, _skipNextFrame);
        _skipNextFrame = false;
        _inDenNow = inDen;
        var here = self.Position is { } p ? p.Frame.Origin : default;
        _movingNow = !_havePosition || (here - _lastPosition).LengthSquared() > 0.0025f; // more than 5 cm since the last frame
        (_lastPosition, _havePosition) = (here, self.Position is not null);
        if (avatars is not null)
        {
            _lights.Step(avatars);
        }

        if (_sampler.FightFrameMaxMs is { } now && now != before)
        {
            (_worstFightAt, _worstFightMs, _worstParts) = (_runSeconds, now, _previousParts);
        }
    }

    // ------------------------------------------------------------------------------------------------ the review frames

    /// <summary>
    /// <c>--play-screenshot=PATH</c> without <c>--play-screenshot-after</c> (M3 3.1 fd): three frames with the developer HUD hidden, <c>&lt;stem&gt;-bolt.png</c> (the first projectile created after the first damage line, plus 0.15 s),
    /// <c>-hit.png</c> (the Explode of a projectile whose damage line comes within 1 s, plus 0.1 s) and <c>-kill.png</c> (the 0x01AD kill notice plus 0.5 s, the chat line on screen), each logged with its trigger and its path. A trigger that never comes
    /// is logged by name at the end and its frame is not taken.
    /// </summary>
    private void StepShot(FightScenario scenario)
    {
        if (_shotPath is null || _avatars is not { } avatars)
        {
            return;
        }

        if (!_hudHidden && scenario.Fighting)
        {
            HideDeveloperHud();
        }
        else if (_hudHidden && _plan.Owed)
        {
            // Something drew the HUD again while a frame is owed (the arrival's own UI): hide it again, once per frame at most, so the saved frame has none.
            HideDeveloperHud();
        }

        var stats = avatars.Stats;
        var due = _plan.Step(_runSeconds, stats.ProjectilesCreated, stats.ProjectileExplodes, scenario.Tally.DamageLinesDealt, scenario.Tally.Kills, avatars.ProjectileWindowOpen, _inDenNow, scenario.Walks >= 1, _movingNow, scenario.OffArrivalStorey);
        if (due < 0)
        {
            return;
        }

        var path = FightShotPlan.ShotPath(_shotPath, due);
        var saved = ScreenshotWriter.Save(GetViewport(), path, LogCategory, ("frame", "bsd-fight-" + FightShotPlan.Names[due]), ("after_trigger_s", FightShotPlan.DelaysSeconds[due]), ("trigger", FightShotPlan.Triggers[due]), ("cast_window_open", avatars.CastWindowOpen), ("projectile_window_open", avatars.ProjectileWindowOpen), ("dyn_lights", avatars.DynamicLightFrame.Count), ("viewer_light", _lights.ViewerLight ? "on" : "off"), ("developer_hud", HudShowing() ? "VISIBLE" : "hidden"));
        _skipNextFrame = true;
        Log.Info(LogCategory, (saved ? "fight: review frame " : "fight: the review frame could not be saved: ") + FightShotPlan.Names[due] + " at its trigger (" + FightShotPlan.Triggers[due] + ") + " + FightShotPlan.DelaysSeconds[due].ToString("0.00", CultureInfo.InvariantCulture) + " s: " + path);
        if (saved)
        {
            _plan.MarkTaken(due);
        }
        else
        {
            _plan.MarkFailed(due, "the frame could not be saved");
        }
    }

    /// <summary>True when any of the developer HUD's nodes is drawn now (read from the nodes, not assumed).</summary>
    private bool HudShowing()
    {
        foreach (var (node, _) in _shotHidden)
        {
            if (IsInstanceValid(node) && node is CanvasItem { Visible: true } item && item.IsVisibleInTree())
            {
                return true;
            }

            if (IsInstanceValid(node) && node is CanvasLayer { Visible: true })
            {
                return true;
            }
        }

        return false;
    }

    private void HideDeveloperHud()
    {
        _hudHidden = true;
        foreach (var name in PlayMode.PlayShotDeveloperHudNames)
        {
            switch (GetTree().Root.FindChild(name, recursive: true, owned: false))
            {
                case CanvasLayer layer:
                    NoteHidden(layer, layer.Visible);
                    layer.Visible = false;
                    break;
                case CanvasItem item:
                    NoteHidden(item, item.Visible);
                    item.Visible = false;
                    break;
            }
        }
    }

    // The first time a node is hidden its visibility is kept for the restore; hiding it again (the check on an owed frame) must not overwrite that with false.
    private void NoteHidden(Node node, bool wasVisible)
    {
        foreach (var (known, _) in _shotHidden)
        {
            if (ReferenceEquals(known, node))
            {
                return;
            }
        }

        _shotHidden.Add((node, wasVisible));
    }

    private void RestoreDeveloperHud()
    {
        foreach (var (node, wasVisible) in _shotHidden)
        {
            if (!IsInstanceValid(node))
            {
                continue;
            }

            switch (node)
            {
                case CanvasLayer layer:
                    layer.Visible = wasVisible;
                    break;
                case CanvasItem item:
                    item.Visible = wasVisible;
                    break;
            }
        }

        _shotHidden.Clear();
        _hudHidden = false;
    }

    // ------------------------------------------------------------------------------------------------ the way out

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
        _quitAt = _runSeconds;
        _phase = Phase.Quitting;
    }

    /// <summary>Records the first failure and logs off before it is reported, so ACE does not keep the character in the world for the next run.</summary>
    private void Fail(IGameSessionView? session, string why)
    {
        if (_failed)
        {
            return;
        }

        _failed = true;
        _failure = why;
        _failedAt = _phase.ToString();
        Log.Error(LogCategory, "the bsd-fight leg failed", ("phase", _failedAt), ("why", why));
        _scenario?.Dispose();
        if (session is not null && _entered && BuffLegPhases.InWorld(session.State) && _phase != Phase.LoggingOff)
        {
            Log.Info(LogCategory, "fight: logging off before the run ends as failed");
            _pending = session.Actions.LogOff(ActionSource.Host);
            _recoveryStarted = true;
            _recoveryStart = _runSeconds;
            return;
        }

        Finish(why);
    }

    private void StepRecovery(IGameSessionView session)
    {
        if (_recoveryStarted && _pending is { Outcome: { } left })
        {
            _pending = null;
            Log.Info(LogCategory, "fight: the failed run logged off: " + left.Kind + (left.Detail is null ? string.Empty : ": " + left.Detail));
            _ = session.Actions.Quit(ActionSource.Host);
            _quitAt = _runSeconds;
            return;
        }

        if (!_recoveryStarted || _runSeconds - _recoveryStart > RecoverySeconds)
        {
            Finish(_failure);
        }
    }

    private void Go(Phase next)
    {
        _phase = next;
        _phaseStart = _runSeconds;
    }

    private static string Describe(in ActionOutcome outcome) =>
        outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail);

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

    private void Finish(string? why)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        RestoreDeveloperHud();
        var scenario = _scenario;
        var tally = scenario?.Tally;
        var stats = _avatars?.Stats;
        _lights.Finish();
        var sampled = _avatars is not null && _lights.Frames > 0;
        var measures = new FightLegMeasures(
            tally?.Kills ?? 0,
            tally?.DamageDealt ?? 0,
            tally?.Casts ?? 0,
            tally?.Misses ?? 0,
            tally?.XpGained ?? 0,
            tally?.DamageTaken ?? 0,
            tally?.HitsTaken ?? 0,
            tally?.Evades ?? 0,
            stats is null ? null : (int)Math.Max(0, stats.ProjectilesShown - _shownBefore),
            _sampler.CastFrameMaxMs,
            _sampler.FightFrameMaxMs,
            _sampler.BsdMinFps,
            scenario is null || double.IsNaN(scenario.FightSeconds) ? null : scenario.FightSeconds,
            sampled && _lights.FlightsJudged > 0 ? _lights.BoltLight : null, // n/a: no flight long enough to judge is not a dark bolt (M3 4.2)
            sampled ? _lights.DynamicLightsMax : null,
            sampled ? _lights.ViewerLight : null,
            tally?.BlockedPicks ?? 0,
            tally?.WallHits ?? 0,
            scenario?.ManaFields(),
            scenario?.TourFields(),
            scenario?.LootFields()); // M5 3 c2a: loot= looted= kept= loot_value= on the bsd-fight done line (report-only)
        var outcome = FightLegVerdict.Compose(_failure ?? why ?? scenario?.Failure ?? (_scenarioEnded ? null : "the fight did not finish"), measures);
        if (!outcome.Ok)
        {
            Log.Error(LogCategory, "the bsd-fight leg failed", ("phase", _failedAt), ("why", string.Join("; ", outcome.Problems)));
        }

        Log.Info(LogCategory, outcome.Line);
        var parts = _worstParts;
        Log.Info(
            LogCategory,
            "bsd-fight detail",
            ("seconds", _runSeconds),
            ("level", scenario?.Level ?? "n/a"),
            ("range_m", scenario?.RangeMetres ?? 0f),
            ("walks", scenario?.Walks ?? 0),
            ("projectiles_created", stats is null ? 0L : stats.ProjectilesCreated),
            ("projectile_explodes", stats?.ProjectileExplodes ?? 0L),
            ("projectile_windows", stats?.ProjectileWindows ?? 0L),
            ("cast_windows", stats?.CastWindows ?? 0L),
            ("frames", _sampler.Frames),
            ("light_flights_judged", _lights.FlightsJudged),
            ("light_flights_lit", _lights.FlightsLit),
            ("light_short_flights", _lights.ShortFlights),
            ("light_state_off_flights", _lights.StateOffFlights),
            ("light_flights_alive_lit", _lights.FlightsText()),
            ("light_untracked", _lights.Untracked),
            ("den_frames", _sampler.DenFrames),
            ("fps_windows", _sampler.Windows),
            ("worst_fight_frame_at_s", _worstFightAt),
            ("worst_fight_frame_ms", _worstFightMs),
            ("worst_session_tick_ms", parts.SessionTickMs),
            ("worst_pump_messages", parts.PumpMessages),
            ("worst_pump_ms", parts.PumpMs),
            ("worst_retail_ui_step_ms", parts.RetailUiMs),
            ("worst_play_mode_step_ms", parts.PlayModeMs),
            ("worst_play_mode_work_ms", parts.PlayWorkMs),
            ("worst_play_mode_adapter_ms", parts.PlayAdapterMs),
            ("worst_play_mode_events_applied", parts.EventsApplied),
            ("review_frames", _shotPath is null ? "not asked for" : _plan.Note()),
            ("scenario_verdict", scenario?.VerdictFields() ?? "n/a"));
        if (_shotPath is not null)
        {
            Log.Info(LogCategory, "fight: review frames: " + _plan.Note());
        }

        Log.FlushToGodot();
        GetTree().Quit(outcome.Ok ? 0 : 1);
    }
}

using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Godot;
using Lifestone.Client.Avatars;
using Lifestone.Client.Options;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Mage;
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
/// <c>--login-check=bsd-death</c> (M3 3.2 dd; plan section 4.8, D33 to D36; docs/session.md section 32): the game enters BSD Mage on the test account and runs <see cref="DeathScenario"/> in its own session (the gear and the
/// spell bars, the bind at Holtburg's lifestone after a walk to its use radius, the teleport to the Aluvian Black Spawn Den, a suicide there when any enchantment but the vitae is active, the tuskers' death of the unbuffed
/// mage, the respawn at the lifestone and the re-gear), then logs off and quits. A kind of its own, never part of <c>--login-check=all</c> (hooked by one line in <see cref="LoginCheckRunner.Install"/>, as
/// <see cref="FightLegRunner"/> is). The script is the scenario's; this node walks the character list and the arrival (the session host has already started the login), steps the scenario once per frame after the session host
/// and play mode, gives it the game's own player as both its walkers (<see cref="GameFightWalk"/> in the den, <see cref="GameApproachWalk"/> to the lifestone, over one <see cref="RoutePlayHost"/>), and reads what only the
/// engine can see: the worst frames (<see cref="DeathFrameSampler"/>, <see cref="FightFrameSampler"/>) and the four review frames (<see cref="DeathShotPlan"/>).
/// </summary>
/// <remarks>
/// <para>The verdict is <see cref="DeathLegVerdict"/>'s (<c>[logincheck] bsd-death done ok=...</c>); the process ends with 0 only when it held. A failure at any stage logs off before it is reported. Per frame the node reads
/// counters and allocates nothing; strings are built at stage changes and at the end.</para>
/// <para><b>The loading worker</b> reads the SpellTable, builds the gear kit (the login name is hashed into the kit's component formulas inside the worker and stays in its locals), loads the den's floor graph
/// (<see cref="DenPlanner"/>) and prepares the methods of the death loop's types (<see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/>), so none of that is compiled on the game thread in the den. The session's own
/// message handlers (the victim notice, the vitae, the purge, the corpse) are prepared by <c>SessionJitWarmUp</c> at start-up, for every message the dispatcher knows.</para>
/// <para><b>Limits.</b> <see cref="WholeCheckSecondsFor"/>: the login and the arrival 60 s, the gear 120 s, the spell bars 30 s, the bind 90 s (the teleport, the walk, the Use and the attune line), the suicide and its respawn
/// 90 s (the countdown is 15 s), the decoy top-up after that suicide 30 s, the teleport to the den and the settle 30 s, <c>--death-seconds</c> (default 600), the respawn and the vitals 65 s, and the re-gear 120 s; and, since M4 3.1 d2, the stages
/// after the re-gear: the rebuff 240 s, the teleport back to the den 30 s, the walk to the tuskers 60 s, <c>--death-fight-seconds</c> (default 180), the pre-trip heal 120 s, the corpse trip 90 s and the return to Holtburg 60 s: 2,015 s with the defaults; the log off
/// after a failure may take <see cref="RecoverySeconds"/> more.</para>
/// <para><b>The loop it runs (M4 3.1 d2, D20).</b> The kit is <see cref="GearKit.ForDeathLoop"/> (the decoys with <c>--death-decoys on</c>, M3's kit with off); <c>--death-rebuff</c>, <c>--death-fight</c>, <c>--death-fight-kills</c> and <c>--death-fight-seconds</c> are
/// <see cref="DeathOptions"/>'s (a bad value fails the leg at its first frame); the buff cycle's options are <see cref="BuffOptions.FromArguments"/> (the <c>--buff-*</c> options) and the fight's <c>Kills</c> and <c>Seconds</c> are the death options'. The leg line is
/// <see cref="DeathLegVerdict"/>'s (the fields of M4 3.1 included) with <see cref="CombatModeWatch.Field"/> (<c>mode_mismatch=</c>, report-only) after it and before the optional <c>frame_over_50=yes</c>. The review frames add <c>decoys</c> (the inventory page open with the decoys listed, see <see cref="DeathShotPlan.DecoysSlot"/>).</para>
/// </remarks>
public sealed partial class DeathLegRunner : Node
{
    /// <summary>The seconds the login and the arrival may take before the scenario starts.</summary>
    public const double LoginSeconds = 60.0;

    /// <summary>The seconds the gear scenario may take.</summary>
    public const double GearSeconds = 120.0;

    /// <summary>The seconds the spell bars may take (<see cref="DeathScenario.BarsSeconds"/>).</summary>
    public const double BarsSeconds = 30.0;

    /// <summary>The seconds the bind may take: the teleport to Holtburg and the settle, the walk to the lifestone, the Use and the attune line.</summary>
    public const double BindSeconds = 90.0;

    /// <summary>The seconds the suicide and its respawn may take (the countdown is 15 s; the arrival and the vitals follow).</summary>
    public const double SuicideSeconds = 90.0;

    /// <summary>The seconds the teleport to the den and the settle may take.</summary>
    public const double PlaceSeconds = 30.0;

    /// <summary>The seconds the respawn after the tuskers' death may take (the arrival limit and the vitals wait).</summary>
    public const double RespawnSeconds = 65.0;

    /// <summary>The seconds the re-gear may take.</summary>
    public const double RegearSeconds = 120.0;

    /// <summary>The seconds the decoy top-up after the unbuff suicide may take (M4 3.1 d2; <c>DeathScenario.TopUp.cs</c>: the suicide is a death too and drops items, so the stage spawns the missing decoys, about 4 s of item grants live).</summary>
    public const double DecoyTopUpSeconds = 30.0;

    /// <summary>The seconds the rebuff may take (M4 3.1 d2): the death purged every enchantment, so it is the buff cycle's full pass at Holtburg, the fight leg's top-up limit (<c>FightLegRunner.TopUpSeconds</c>; a live cycle of 26 casts took 56 s, and its mana wait is bounded by <c>--buff-mana-wait-s</c>, 180 s).</summary>
    public const double RebuffSeconds = 240.0;

    /// <summary>The seconds the teleport back to the den and the settle may take before the fight (M4 3.1 d2; the fight scenario's place stage).</summary>
    public const double FightPlaceSeconds = 30.0;

    /// <summary>The seconds the walk to the tuskers may take before the fight's first cast (M4 3.1 d2; <c>--death-fight-seconds</c> counts from that first cast).</summary>
    public const double FightWalkSeconds = 60.0;

    /// <summary>The seconds the pre-trip heal may take (M4 3.1 d2): up to <see cref="DeathScenario.PreTripMaxHeals"/> casts of Heal Self at 15 s each (the mode, the cast and the settle) and <see cref="DeathScenario.PreTripRegenSeconds"/> s of regeneration.</summary>
    public const double PreTripHealSeconds = 120.0;

    /// <summary>The seconds the corpse trip may take (M4 3.1 d2: now after the fight; <see cref="DeathScenario.TripSeconds"/>, its own limit, from the teleport to the corpse to the end).</summary>
    public const double TripSeconds = 90.0;

    /// <summary>The seconds the return to Holtburg after the trip may take (M4 3.1 d2: the teleport and the settle).</summary>
    public const double TripReturnSeconds = 60.0;

    /// <summary>How long the log off after a failure or the limit may take, in seconds.</summary>
    public const double RecoverySeconds = 120.0;

    /// <summary>The whole check's limit with the default <c>--death-seconds</c> and <c>--death-fight-seconds</c>: 605 + 600 for M3's loop and 630 + 180 for M4 3.1's stages after the respawn = 2015 s.</summary>
    public const double WholeCheckSeconds = LoginSeconds + GearSeconds + BarsSeconds + BindSeconds + SuicideSeconds + DecoyTopUpSeconds + PlaceSeconds + DeathOptions.DefaultSeconds + RespawnSeconds + RegearSeconds + RebuffSeconds + FightPlaceSeconds + FightWalkSeconds + DeathOptions.DefaultFightSeconds + PreTripHealSeconds + TripSeconds + TripReturnSeconds;

    /// <summary>
    /// The whole check's limit for a wait of <paramref name="deathSeconds"/> session seconds among the tuskers and a fight of <paramref name="fightSeconds"/>: from the first frame, at the limit it fails, logs off and reports. Always the whole loop (a stage that is off only finishes
    /// early; the limit is the ceiling, not a budget).
    /// </summary>
    /// <param name="deathSeconds"><c>--death-seconds</c>.</param>
    /// <param name="fightSeconds"><c>--death-fight-seconds</c>.</param>
    public static double WholeCheckSecondsFor(double deathSeconds, double fightSeconds = DeathOptions.DefaultFightSeconds) => LoginSeconds + GearSeconds + BarsSeconds + BindSeconds + SuicideSeconds + DecoyTopUpSeconds + PlaceSeconds + deathSeconds + RespawnSeconds + RegearSeconds + RebuffSeconds + FightPlaceSeconds + FightWalkSeconds + fightSeconds + PreTripHealSeconds + TripSeconds + TripReturnSeconds;

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
        Death,
        LoggingOff,
        Quitting,
    }

    /// <summary>Where the decoys frame's page is: not opened yet, open (the frame is owed) or done (taken or given up, the page closed again).</summary>
    private enum DecoysPage
    {
        NotYet,
        Open,
        Done,
    }

    /// <summary>What one frame's steps held (the session's tick and pump, the retail UI, play mode), kept for the frame that proves slowest.</summary>
    private readonly record struct FrameParts(double SessionTickMs, int PumpMessages, double PumpMs, double RetailUiMs, double PlayModeMs, double PlayWorkMs, double PlayAdapterMs, int EventsApplied);

    private SessionHost _host = null!;
    private PlayMode _mode = null!;
    private RetailUiLayer? _ui;
    private RoutePlayHost _walkHost = null!;
    private DeathOptions _options = new();
    private BuffOptions _buffOptions = new();
    private FightOptions _fightOptions = new();
    private readonly CombatModeWatch _modeWatch = new(); // M4 3.1 g: mode_mismatch=, report-only, printed last on the leg line
    private double _wholeSeconds = WholeCheckSeconds;
    private Task<(SpellTableData Table, GearKit Kit, DenPlanner Planner, double BuildMilliseconds, double PlannerMilliseconds)>? _loading;
    private DeathScenario? _scenario;
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

    // The frames: the two samplers, the previous frame's parts (the engine's delta is the previous frame's whole time) and the parts of the slowest death and fight frames.
    private readonly DeathFrameSampler _deathSampler = new();
    private readonly FightFrameSampler _fightSampler = new();
    private AvatarSet? _avatars;
    private bool _skipNextFrame;
    private FrameParts _previousParts;
    private FrameParts _worstDeathParts;
    private FrameParts _worstFightParts;
    private double _worstDeathAt = double.NaN;
    private double _worstDeathMs = double.NaN;
    private double _worstFightAt = double.NaN;
    private double _worstFightMs = double.NaN;
    private string _worstDeathStage = "n/a";

    // The stage timings: the scenario's stage and the run time it was entered at, appended at each change.
    private string? _lastStage;
    private readonly StringBuilder _stageTimings = new();

    // The review frames (--play-screenshot=PATH without --play-screenshot-after): the hit, the Dead motion, the corpse and the lifestone.
    private string? _shotPath;
    private readonly DeathShotPlan _plan = new();
    private bool _planArmed;
    private bool _hudHidden;
    private readonly List<(Node Node, bool WasVisible)> _shotHidden = [];

    // The decoys frame (M4 3.1 d2): the inventory page opened once, after the gear stage's decoy check, and closed when the frame is taken or given up.
    private DecoysPage _decoysPage = DecoysPage.NotYet;
    private GamePanelHost? _decoysHost;
    private GameInventoryPage? _decoysInventory;
    private int _decoysHeld;
    private bool _decoysScrolled;

    /// <summary>Adds the check to the scene; it starts with the scene's first frame.</summary>
    public static void Install(Main main, GameServices services, SessionHost host, PlayMode mode, RetailUiLayer? ui)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(mode);
        var dats = services.Dats ?? throw new InvalidOperationException("the death leg needs the dats (Main checks for them first)");
        var key = host.Options.Account;
        var legOptions = DeathLegOptions.FromArguments(CommandLineArgument.ParseAll(OS.GetCmdlineUserArgs()));
        var options = legOptions.Death;
        foreach (var error in legOptions.Errors)
        {
            Log.Error(LogCategory, "death: " + error);
        }

        // The game's player is the walker of both walks (the den's legs and the bind's approach): play mode builds the player with this host's sink and the scenario drives its controller (RoutePlay's hosted mode).
        var walkHost = new RoutePlayHost(() => mode.Rig?.Controller.Walker, () => mode.MovementGate?.Held ?? 0);
        mode.ScenarioHost = walkHost;
        var runner = new DeathLegRunner
        {
            Name = "DeathLegRunner",
            ProcessPriority = 100, // after the session host (-900), play mode (-800) and the retail UI: it looks at what they did this frame
            _host = host,
            _mode = mode,
            _ui = ui,
            _walkHost = walkHost,
            _options = options,
            _buffOptions = legOptions.Buff,
            _fightOptions = legOptions.Fight,
            _optionError = legOptions.Error,
            _wholeSeconds = WholeCheckSecondsFor(options.Seconds, options.FightSeconds),
            _shotPath = services.Options.Play.BuffShotPath,
        };

        // The SpellTable, the kit and the den's floor graph are built on a worker: the arrival's frames are never spent on them. The login name the kit's formulas are customised for (hashed, never stored, logged or
        // printed) is read inside that worker and stays in its locals. Then the worker prepares the death loop's methods, so the game thread does not compile them in the den.
        runner._loading = Task.Factory.StartNew(
            () =>
            {
                var table = SpellTableData.Load(dats);
                var built = System.Diagnostics.Stopwatch.GetTimestamp();
                var kit = GearKit.ForDeathLoop(table.Spells, LoginName(key), options);
                var buildMs = System.Diagnostics.Stopwatch.GetElapsedTime(built).TotalMilliseconds;
                var planning = System.Diagnostics.Stopwatch.GetTimestamp();
                var planner = new DenPlanner(DenPlanner.FloorOf(LandblockCellSet.Load(new CellStructureLibrary(dats), new LandblockCoord(1, 5))));
                var plannerMs = System.Diagnostics.Stopwatch.GetElapsedTime(planning).TotalMilliseconds;
                var warm = System.Diagnostics.Stopwatch.GetTimestamp();
                var prepared = PrepareDeathMethods();
                Log.Info(LogCategory, "death: the death loop's methods were prepared on the loading worker", ("methods", prepared), ("ms", System.Diagnostics.Stopwatch.GetElapsedTime(warm).TotalMilliseconds));
                return (table, kit, planner, buildMs, plannerMs);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        main.AddChild(runner);
        Log.Info(
            LogCategory,
            "login check 'bsd-death' installed: the test account enters BSD Mage, the death scenario runs in the game's session (gear, bind at Holtburg's lifestone, the den, the tuskers' death, the respawn, the re-gear), then the game logs off and quits",
            ("scenario", DeathScenario.ScenarioName),
            ("options", options.ToString()),
            ("whole_check_s", runner._wholeSeconds),
            ("review_frames", runner._shotPath is not null));
    }

    /// <summary>
    /// <see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/> over the methods of the scenario's namespaces (the death loop, the gear and the buff scenarios it reuses, the mage's profile and the den, the samplers
    /// and the plan) and of this leg's own types and the two walks, on the loading worker: a method the game thread would otherwise compile in the den's first frames. No code is run. Returns the methods prepared.
    /// </summary>
    private static int PrepareDeathMethods()
    {
        var prepared = 0;
        foreach (var type in typeof(DeathScenario).Assembly.GetTypes())
        {
            if (type.Namespace is not ("Lifestone.Client.Session.Scenarios" or "Lifestone.Client.Session.Mage"))
            {
                continue;
            }

            prepared += PrepareType(type);
        }

        return prepared + PrepareType(typeof(GameFightWalk)) + PrepareType(typeof(GameApproachWalk)) + PrepareType(typeof(DeathLegRunner)) + PrepareType(typeof(Lifestone.Client.Movement.Routes.RouteDriver));
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
            case Phase.Death:
                StepScenario(session);
                break;
            case Phase.LoggingOff:
                LoggingOff(session);
                break;
        }

        _previousParts = TakeParts();
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        _deathSampler.Dispose();
        _modeWatch.Dispose();
        RestoreDeveloperHud();
    }

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
            Log.Info(LogCategory, "death: entering the existing character " + MageTemplate.CharacterName);
            Go(Phase.Entering);
            return;
        }

        Fail(session, "choosing: " + MageTemplate.CharacterName + " is not in the character list (run --scenario=mage-build first; the death leg creates nobody)");
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

    // ------------------------------------------------------------------------------------------------ the death scenario

    private void BeginScenario(IGameSessionView session)
    {
        var (table, kit, planner, buildMilliseconds, plannerMilliseconds) = _loading!.Result;
        Log.Info(LogCategory, "death: the gear kit and the den's floor were built on the loading worker, off the game thread", ("build_ms", buildMilliseconds), ("planner_ms", plannerMilliseconds), ("floor_cells", planner.Cells.Cells.Count));
        var walk = new GameFightWalk(_walkHost, planner, GodotLogSink.Instance);
        var approach = new GameApproachWalk(_walkHost, GodotLogSink.Instance);
        _scenario = new DeathScenario(kit, table.Spells, _options, walk, GodotLogSink.Instance, LoginScenario.DefaultArrivalSeconds, endSession: false, DeathStages.All, approach, _buffOptions, _fightOptions);
        _avatars = _mode.Avatars;
        _deathSampler.Start(session);
        _modeWatch.Start(session); // M4 3.1 g: counts the moments the combat mode and the wielded weapon disagree, whoever caused them; report-only
        if (_shotPath is not null)
        {
            HideDeveloperHud();
        }

        Go(Phase.Death);
    }

    private void StepScenario(IGameSessionView session)
    {
        var scenario = _scenario!;
        NoteStage(scenario);
        StepShot(session, scenario);
        if (_plan.IsOwed(_runSeconds))
        {
            // The scenario is held (at most DeathShotPlan.HoldSeconds after a trigger) while a review frame is owed, so the state to show is still on screen. M4 2.2 fix R5: the hold must not suspend the corpse trip's guard
            // (the corpse-open frame holds it among the tuskers): the guard alone runs, never a step of the trip.
            switch (scenario.GuardWhileHeld(session))
            {
                case ScenarioStatus.Failed:
                    Fail(session, scenario.Failure ?? "the scenario failed");
                    break;
                case ScenarioStatus.Running:
                    _plan.ReleaseHold("the trip aborted while the frame was owed (the corpse window is closing)");
                    break;
            }

            return;
        }

        switch (scenario.Step(session, PortalSpaceSetup.Now()))
        {
            case ScenarioStatus.Failed:
                Fail(session, scenario.Failure ?? "the scenario failed");
                break;
            case ScenarioStatus.Succeeded:
                _scenarioEnded = true;
                _modeWatch.Dispose(); // the counts stay readable; the log off's inventory changes are not the loop's
                NoteStage(scenario);
                _pending = session.Actions.LogOff(ActionSource.Host);
                Go(Phase.LoggingOff);
                break;
        }
    }

    /// <summary>The scenario's stage and the run time it was entered at, appended at a change (a reference compare each frame; the string is built at the change only).</summary>
    private void NoteStage(DeathScenario scenario)
    {
        var stage = scenario.Stage;
        if (ReferenceEquals(stage, _lastStage) || string.Equals(stage, _lastStage, StringComparison.Ordinal))
        {
            return;
        }

        _lastStage = stage;
        if (_stageTimings.Length > 0)
        {
            _stageTimings.Append(',');
        }

        _stageTimings.Append(stage).Append('@').Append(_runSeconds.ToString("0.0", CultureInfo.InvariantCulture));
    }

    /// <summary>The stages in the den, up to the respawn: the unbuffed mage among the tuskers (the fight sampler's "fighting").</summary>
    private static bool InDenStage(string stage) => stage is "den-settle" or "unbuff" or "unbuff-dying" or "death-mode" or "death-wait";

    /// <summary>One frame of the samplers: the death window and the den stages' worst frame; allocates nothing. The worst frames keep the previous frame's parts.</summary>
    private void SampleFrame(double delta, IGameSessionView session)
    {
        var scenario = _scenario!;
        var self = session.State.Self;
        var inDen = self.Travel == SelfTravel.Present && self.Position is { } at && (at.CellId >> 16) == BlackSpawnDen.Landblock;
        var fighting = InDenStage(scenario.Stage);
        var avatars = _avatars;
        var fightBefore = _fightSampler.FightFrameMaxMs;
        _fightSampler.Step(delta, avatars?.CastWindowOpen == true, avatars?.ProjectileWindowOpen == true, fighting, fighting, inDen, _skipNextFrame);
        if (_fightSampler.FightFrameMaxMs is { } fightNow && fightNow != fightBefore)
        {
            (_worstFightAt, _worstFightMs, _worstFightParts) = (_runSeconds, fightNow, _previousParts);
        }

        if (_deathSampler.Step(delta, _skipNextFrame) && _deathSampler.DeathFrameMaxMs is { } deathNow)
        {
            (_worstDeathAt, _worstDeathMs, _worstDeathParts, _worstDeathStage) = (_runSeconds, deathNow, _previousParts, scenario.Stage);
        }

        _skipNextFrame = false;
    }

    // ------------------------------------------------------------------------------------------------ the review frames

    /// <summary>
    /// <c>--play-screenshot=PATH</c> without <c>--play-screenshot-after</c> (M3 3.2 dd): four frames with the developer HUD hidden, <c>&lt;stem&gt;-hit.png</c> (a 0x01B2 with the health under 100%, plus 0.2 s),
    /// <c>-dead.png</c> (the player's Dead motion plus 1.5 s), <c>-corpse.png</c> (the corpse's F745 plus 0.3 s, when it comes before portal space), <c>-lifestone.png</c> (the death's arrival plus 3.5 s) and, since M4 2.2 s1,
    /// <c>-corpse-open.png</c> (the mage's own corpse open on the trip back for it, 0.5 s after its contents are listed, the scenario held so the container window shows them) and, since M4 3.1 d2,
    /// <c>-decoys.png</c> (the inventory page open with the decoys listed, taken in the gear stage's wake: <see cref="StepDecoysPage"/>), each logged with its trigger and its path. A trigger that never comes, or a corpse frame that falls in portal space, is logged by name at the end and its frame is not taken.
    /// </summary>
    private void StepShot(IGameSessionView session, DeathScenario scenario)
    {
        if (_shotPath is null)
        {
            return;
        }

        var decoysOpen = StepDecoysPage(session, scenario); // M4 3.1 d2: the decoys frame belongs to the gear stage's wake, long before the plan is armed for the tuskers' death
        if (!_planArmed)
        {
            // The frames are the tuskers' death's, not the unbuff suicide's: the plan starts watching when the scenario waits among the tuskers (or a victim notice, which only a tusker's kill sends, came first), with the counters then as its bases.
            if (scenario.Stage is not ("death-mode" or "death-wait") && scenario.Tally.VictimNotices == 0)
            {
                if (!decoysOpen)
                {
                    return;
                }
            }
            else
            {
                _planArmed = true;
                _plan.Begin(scenario.Tally.HitsTaken, _deathSampler.DeadMotions, _deathSampler.Corpses, _deathSampler.Arrivals);
            }
        }

        var self = session.State.Self;
        var healthUnderFull = self.Qualities.TryGetVitalLevels(1, out var current, out var max) && max != 0 && current < max;
        var decoysListed = decoysOpen && DecoysListed();
        var due = _plan.Step(_runSeconds, scenario.Tally.HitsTaken, healthUnderFull, _deathSampler.DeadMotions, _deathSampler.Corpses, _deathSampler.CorpseBeforePortal, _deathSampler.Arrivals, self.Travel != SelfTravel.Present, scenario.CorpseOpen, decoysOpen, decoysListed);
        if (due < 0)
        {
            return;
        }

        var path = DeathShotPlan.ShotPath(_shotPath, due);
        var avatars = _avatars;
        var saved = ScreenshotWriter.Save(GetViewport(), path, LogCategory, ("frame", "bsd-death-" + DeathShotPlan.Names[due]), ("after_trigger_s", DeathShotPlan.DelaysSeconds[due]), ("stage", scenario.Stage), ("cast_window_open", avatars?.CastWindowOpen == true), ("developer_hud", "hidden"));
        _skipNextFrame = true;
        Log.Info(LogCategory, (saved ? "death: review frame " : "death: the review frame could not be saved: ") + DeathShotPlan.Names[due] + " at its trigger + " + DeathShotPlan.DelaysSeconds[due].ToString("0.00", CultureInfo.InvariantCulture) + " s: " + path);
        if (saved)
        {
            _plan.MarkTaken(due);
        }
        else
        {
            _plan.MarkFailed(due, "the frame could not be saved");
        }
    }

    // ------------------------------------------------------------------------------------------------ the decoys frame

    /// <summary>The seconds after the decoy check the runner keeps trying to open the inventory page before it gives the decoys frame up by name (the shell is built by then; this is for a shell that is not).</summary>
    private const double DecoysOpenGiveUpSeconds = 8.0;

    private double _decoysTryStart = double.NaN;
    private uint[] _decoyGuids = [];
    private int _decoysSettleFrames;

    /// <summary>
    /// M4 3.1 d2: the decoys frame's page. Once the scenario's gear stage has made its decoy check (<see cref="DeathScenario.DecoyCheckResult"/>), the runner opens the inventory page as the toolbar button does (the panel host's own toggle, never a session action: the
    /// character is not touched), reads the decoys the main pack holds, and keeps the page open while the plan owes the frame (<see cref="DeathShotPlan.DecoysSlot"/>: the page must list every decoy and the pack list is scrolled to the first). When the frame is taken or
    /// given up the page is closed again, so the later frames are not covered by it. A shell that has no inventory page (or none within <see cref="DecoysOpenGiveUpSeconds"/>) leaves the slot reading <c>trigger never came</c>, said in the log: the frame is never faked.
    /// Returns true while the page is open and the frame owed.
    /// </summary>
    private bool StepDecoysPage(IGameSessionView session, DeathScenario scenario)
    {
        switch (_decoysPage)
        {
            case DecoysPage.Done:
                return false;
            case DecoysPage.Open:
                if (!_plan.Decided(DeathShotPlan.DecoysSlot))
                {
                    return true;
                }

                CloseDecoysPage();
                return false;
            default:
                if (scenario.DecoyCheckResult is not { Made: true })
                {
                    return false;
                }

                if (double.IsNaN(_decoysTryStart))
                {
                    _decoysTryStart = _runSeconds;
                }

                if (OpenDecoysPage(session))
                {
                    _decoysPage = DecoysPage.Open;
                    return true;
                }

                if (_runSeconds - _decoysTryStart > DecoysOpenGiveUpSeconds)
                {
                    _decoysPage = DecoysPage.Done;
                    Log.Warn(LogCategory, "death: the decoys frame is given up: the in-game shell has no inventory page to open within " + DecoysOpenGiveUpSeconds.ToString("0", CultureInfo.InvariantCulture) + " s of the decoy check (the slot reads 'trigger never came')");
                }

                return false;
        }
    }

    private bool OpenDecoysPage(IGameSessionView session)
    {
        if (_ui?.Screens?.Flow.CurrentScreen is not GamePlayScreen { IsBuilt: true, PanelHost: { } host, PanelControllers: { } panels })
        {
            return false;
        }

        GameInventoryPage? page = null;
        foreach (var controller in panels.ControllersOf(GameInventoryPage.Panel))
        {
            if (controller is GameInventoryPage found)
            {
                page = found;
                break;
            }
        }

        if (page is null)
        {
            return false;
        }

        if (host.CurrentPanel != GameInventoryPage.Panel)
        {
            host.Toggle(GameInventoryPage.Panel); // what the toolbar's inventory button does
        }

        if (host.CurrentPanel != GameInventoryPage.Panel)
        {
            return false;
        }

        // The decoys the main pack holds now (the page's pack list shows the main pack): their guids are what the list must show.
        var pack = session.State.Self.Inventory.Pack;
        var held = new List<uint>();
        for (var i = 0; i < pack.Count; i++)
        {
            if (session.State.Objects.TryGet(pack[i].Guid, out var item) && DecoyKit.IsDecoy(item.Weenie.WeenieClassId))
            {
                held.Add(pack[i].Guid);
            }
        }

        _decoysHost = host;
        _decoysInventory = page;
        _decoyGuids = [.. held];
        _decoysHeld = held.Count;
        _decoysScrolled = false;
        _decoysSettleFrames = 0;
        Log.Info(LogCategory, "death: the inventory page is open for the decoys frame (" + _decoysHeld.ToString(CultureInfo.InvariantCulture) + " decoys in the main pack)");
        return true;
    }

    /// <summary>
    /// True when the open page lists every decoy the pack held when it was opened (each has its cell in the pack list), the list has been scrolled to the first of them and three frames have drawn it: the condition of the frame. With no decoy held it is never true
    /// (the plan gives the slot up by name). Allocates nothing.
    /// </summary>
    private bool DecoysListed()
    {
        if (_decoysInventory is not { } page || _decoysHost is not { } host || _decoysHeld == 0 || host.CurrentPanel != GameInventoryPage.Panel)
        {
            return false;
        }

        var list = page.PackList;
        foreach (var guid in _decoyGuids)
        {
            if (list.FindItem(guid) is null)
            {
                return false;
            }
        }

        if (!_decoysScrolled)
        {
            var first = -1;
            for (var i = 0; i < list.CellCount && first < 0; i++)
            {
                if (list.CellAt(i) is { } cell && Array.IndexOf(_decoyGuids, cell.ItemId) >= 0)
                {
                    first = i;
                }
            }

            if (first >= 0)
            {
                list.ScrollToShow(first); // as a player's scroll bar would: the first decoy's row at the top of the list
            }

            _decoysScrolled = true;
            _decoysSettleFrames = 0;
            Log.Info(LogCategory, "death: the inventory page lists the " + _decoysHeld.ToString(CultureInfo.InvariantCulture) + " decoys; the pack list is scrolled to the first (cell " + first.ToString(CultureInfo.InvariantCulture) + ")");
            return false;
        }

        return ++_decoysSettleFrames >= 3;
    }

    private void CloseDecoysPage()
    {
        _decoysPage = DecoysPage.Done;
        if (_decoysHost is { } host && host.CurrentPanel == GameInventoryPage.Panel)
        {
            host.Toggle(GameInventoryPage.Panel); // closes the page, as its button does
        }

        _decoysHost = null;
        _decoysInventory = null;
        Log.Info(LogCategory, "death: the inventory page is closed again; the decoys frame is decided (" + _plan.Note() + ")");
    }

    private void HideDeveloperHud()
    {
        _hudHidden = true;
        _shotHidden.Clear();
        foreach (var name in PlayMode.PlayShotDeveloperHudNames)
        {
            switch (GetTree().Root.FindChild(name, recursive: true, owned: false))
            {
                case CanvasLayer layer:
                    _shotHidden.Add((layer, layer.Visible));
                    layer.Visible = false;
                    break;
                case CanvasItem item:
                    _shotHidden.Add((item, item.Visible));
                    item.Visible = false;
                    break;
            }
        }
    }

    private void RestoreDeveloperHud()
    {
        if (!_hudHidden)
        {
            return;
        }

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
        Log.Error(LogCategory, "the bsd-death leg failed", ("phase", _failedAt), ("why", why));
        if (session is not null)
        {
            _scenario?.FreezeMeasures(session); // in the world, before the log off below: LeaveWorld clears the object table (review R3)
        }

        _scenario?.Dispose();
        _modeWatch.Dispose();
        if (session is not null && _entered && BuffLegPhases.InWorld(session.State) && _phase != Phase.LoggingOff)
        {
            Log.Info(LogCategory, "death: logging off before the run ends as failed");
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
            Log.Info(LogCategory, "death: the failed run logged off: " + left.Kind + (left.Detail is null ? string.Empty : ": " + left.Detail));
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

    /// <summary>
    /// The leg line: the verdict's line (<see cref="DeathLegVerdict"/>, the fields of M4 3.1 included) and the combat-mode watch's field (<c>mode_mismatch=&lt;n&gt;</c>, report-only) after them and before the optional <c>frame_over_50=yes</c>,
    /// the hunt leg's order and <see cref="DeathLegVerdict"/>'s own rule for a new field (M4 3.1 fix, review R21).
    /// </summary>
    /// <param name="verdictLine">The verdict line.</param>
    /// <param name="modeField">The watch's field.</param>
    internal static string LegLine(string verdictLine, string modeField) => DeathLegVerdict.WithField(verdictLine, modeField);

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
        _deathSampler.Dispose();
        _modeWatch.Dispose();
        var scenario = _scenario;
        var failure = _failure ?? why ?? scenario?.Failure ?? (_scenarioEnded ? null : "the death loop did not finish");
        DeathLegOutcome outcome;
        if (scenario is not null)
        {
            // The scenario's own outcome: its measures were frozen in the world when it concluded or failed (before the log off). A re-measure here would read an empty object table: the log off's LeaveWorld cleared it (review R3).
            outcome = DeathLegFinish.WithRunnerFailure(scenario.Outcome(_deathSampler.DeathFrameMaxMs, _fightSampler.FightFrameMaxMs), failure);
        }
        else
        {
            outcome = DeathLegVerdict.Compose(failure, default);
        }

        if (!outcome.Ok)
        {
            Log.Error(LogCategory, "the bsd-death leg failed", ("phase", _failedAt), ("why", string.Join("; ", outcome.Problems)));
        }

        Log.Info(LogCategory, LegLine(outcome.Line, _modeWatch.Field));
        if (_modeWatch.FirstViolation is { } firstViolation)
        {
            Log.Warn(LogCategory, "death: the combat mode and the wielded weapon disagreed (" + _modeWatch.Field + ", report-only): " + firstViolation);
        }

        var parts = _worstDeathParts;
        var fightParts = _worstFightParts;
        Log.Info(
            LogCategory,
            "bsd-death detail",
            ("seconds", _runSeconds),
            ("stage", scenario?.Stage ?? "n/a"),
            ("stage_timings", _stageTimings.Length == 0 ? "n/a" : _stageTimings.ToString()),
            ("unbuff", scenario?.UnbuffNote ?? "n/a"),
            ("bars", scenario?.BarsNote ?? "n/a"),
            ("bound", scenario is { Bound: true } ? "yes" : "no"),
            ("legs", scenario?.Legs ?? 0),
            ("dead_motions", _deathSampler.DeadMotions),
            ("death_portals", _deathSampler.Portals),
            ("death_arrivals", _deathSampler.Arrivals),
            ("corpses_seen", _deathSampler.Corpses),
            ("corpses_stale", _deathSampler.StaleCorpses),
            ("corpse_before_portal", _deathSampler.CorpseBeforePortal ? "yes" : "no"),
            ("death_windows", _deathSampler.Windows),
            ("death_frames", _deathSampler.Frames),
            ("worst_death_frame_at_s", _worstDeathAt),
            ("worst_death_frame_ms", _worstDeathMs),
            ("worst_death_frame_stage", _worstDeathStage),
            ("worst_death_session_tick_ms", parts.SessionTickMs),
            ("worst_death_pump_messages", parts.PumpMessages),
            ("worst_death_pump_ms", parts.PumpMs),
            ("worst_death_retail_ui_step_ms", parts.RetailUiMs),
            ("worst_death_play_mode_step_ms", parts.PlayModeMs),
            ("worst_death_play_mode_work_ms", parts.PlayWorkMs),
            ("worst_death_play_mode_adapter_ms", parts.PlayAdapterMs),
            ("worst_death_play_mode_events_applied", parts.EventsApplied),
            ("fight_frames", _fightSampler.Frames),
            ("worst_fight_frame_at_s", _worstFightAt),
            ("worst_fight_frame_ms", _worstFightMs),
            ("worst_fight_session_tick_ms", fightParts.SessionTickMs),
            ("worst_fight_pump_ms", fightParts.PumpMs),
            ("worst_fight_play_mode_work_ms", fightParts.PlayWorkMs),
            ("mode_mismatch_unchecked", _modeWatch.Unchecked),
            ("review_frames", _shotPath is null ? "not asked for" : _plan.Note()),
            ("scenario_verdict", scenario?.VerdictFields() ?? "n/a"));
        if (_shotPath is not null)
        {
            Log.Info(LogCategory, "death: review frames: " + _plan.Note() + (_deathSampler.Corpses == 0 ? string.Empty : "; the corpse's F745 came " + (_deathSampler.CorpseBeforePortal ? "before" : "after") + " the death's F751"));
        }

        Log.FlushToGodot();
        GetTree().Quit(outcome.Ok ? 0 : 1);
    }
}

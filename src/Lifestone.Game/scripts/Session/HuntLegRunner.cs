using System.Globalization;
using Godot;
using Lifestone.Client.Options;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Tools;
using Lifestone.Data.Dat;
using Lifestone.Data.World;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.PortalSpace;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// <c>--login-check=hunt</c> (M4 1.2 s4b; docs/session.md section 35): the game enters the account's fighter (Holtburg Hunter on the test account; Holtburg Brawler on test-melee and Holtburg Archer on test-archer, M4 P4) and runs <see cref="HuntScenario"/> in its own session (the bind at Holtburg's lifestone,
/// the belt, the weapon swap for <c>--hunt-weapon=missile</c>, the hunt on the grounds of <c>--hunt-ground</c> (<see cref="HuntOptions.GroundRow"/>: the belt, <see cref="HuntGround.LowB"/>, by default) until <c>--hunt-kills</c> kills or <c>--hunt-seconds</c>, the wind-down back to Holtburg), then logs
/// off and quits. A kind of its own, never part of <c>--login-check=all</c> (hooked by one line in <see cref="LoginCheckRunner.Install"/>, as <see cref="FightLegRunner"/> is). The script is the scenario's; this node walks
/// the character list and the arrival (the session host has already started the login), steps the scenario once per frame after the session host and play mode, and reads what only the engine can see: the review frame.
/// </summary>
/// <remarks>
/// <para>The verdict is <see cref="HuntLegVerdict"/>'s (<c>[logincheck] hunt done ok=...</c>); the process ends with 0 only when it held. A failure at any stage logs off before it is reported (so ACE does not keep
/// the character in the world for the next run). Per frame the node reads counters and allocates nothing; strings are built at stage changes and at the end.</para>
/// <para><b>The review frame</b> (<c>--play-screenshot=PATH</c> without <c>--play-screenshot-after</c>, 800x600, the developer HUD hidden): the leg takes the frame its weapon gives, <c>&lt;stem&gt;-swing.png</c> for
/// melee or <c>&lt;stem&gt;-arrow.png</c> for missile (<see cref="HuntShotPlan"/>), logged with the frame name, the trigger, the target's name and distance, the wielded weapon's wcid and the developer HUD's state.
/// The scenario is held for at most <see cref="HuntShotPlan.HoldSeconds"/> while a frame is owed. The ground heights are read on a worker before the first frame of the hunt.</para>
/// <para><b>Limits.</b> <see cref="WholeCheckSecondsFor"/>: the login and the arrival 60 s, the bind, the belt and the swap 300 s, the hunt's own <c>--hunt-seconds</c> (default 300) and the wind-down 120 s: 780 s by
/// default, and <see cref="HuntScenario.VendorTripsSeconds"/> more (420 s: the two shop visits and the way home) when the sale or the purchase is on, which they are by default (1200 s in all); the log off after a failure may take <see cref="RecoverySeconds"/> more.</para>
/// </remarks>
public sealed partial class HuntLegRunner : Node
{
    /// <summary>The seconds the login and the arrival may take before the scenario starts.</summary>
    public const double LoginSeconds = 60.0;

    /// <summary>The seconds the bind, the belt (a potion, a regeneration wait) and the weapon swap may take before the hunt's own clock starts.</summary>
    public const double PrepareSeconds = 300.0;

    /// <summary>The seconds the wind-down (the cancel, the restore, the way back to Holtburg) may take after the hunt.</summary>
    public const double WindDownSeconds = 120.0;

    /// <summary>How long the log off after a failure or the limit may take, in seconds.</summary>
    public const double RecoverySeconds = 120.0;

    /// <summary>The whole check's limit with the default <c>--hunt-seconds</c> and the shop visits on: 60 + 300 + 300 + 120 + 420 = 1200 s.</summary>
    public const double WholeCheckSeconds = LoginSeconds + PrepareSeconds + HuntOptions.DefaultSeconds + WindDownSeconds + HuntScenario.VendorTripsSeconds;

    /// <summary>The whole check's limit for a hunt of <paramref name="huntSeconds"/> session seconds: from the first frame, at the limit it fails, logs off and reports.</summary>
    /// <param name="huntSeconds"><c>--hunt-seconds</c>.</param>
    /// <param name="vendorTrips">True when <c>--sell</c> or <c>--buy-kits</c> is on: the shop visits' bound is added (3.2-R15).</param>
    public static double WholeCheckSecondsFor(double huntSeconds, bool vendorTrips = true) =>
        LoginSeconds + PrepareSeconds + huntSeconds + WindDownSeconds + (vendorTrips ? HuntScenario.VendorTripsSeconds : 0.0);

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
        Hunt,
        LoggingOff,
        Quitting,
    }

    private SessionHost _host = null!;
    private PlayMode _mode = null!;
    private HuntOptions _options = new();
    private FighterTemplate _template = FighterTemplate.Hunter;
    private double _wholeSeconds = WholeCheckSeconds;
    private Task<Dictionary<HuntCell, float?>>? _loading;
    private DatSet? _dats;
    private HuntScenario? _scenario;
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
    private bool _died;
    private readonly List<IDisposable> _subscriptions = [];

    // The review frame (--play-screenshot=PATH without --play-screenshot-after): the plan, the hidden developer HUD and what the triggers saw.
    private string? _shotPath;
    private HuntShotPlan _plan = new(HuntWeapon.Melee);
    private bool _hudHidden;
    private int _hiddenFrames;
    private int _sequenceSeen;
    private uint _arrowGuid;
    private readonly List<(Node Node, bool WasVisible)> _shotHidden = [];

    // M4 1.3 s2: the retail-UI review frames of the melee hunt (HuntUiShotPlan), the camera framed for them, and attack_frame_max_ms (AttackFrameSampler).
    private RetailUiLayer? _ui;
    private readonly HuntUiShotPlan _uiPlan = new();
    private bool _playerBoxRefusalLogged; // M4 3.1 p: the player's missing projection box is named once, at the first frame it is missing
    private readonly AttackFrameSampler _attackFrames = new();
    private readonly VendorFrameSampler _vendorFrames = new(); // M4 3.2 s4a: vendor_frame_max_ms, the slowest frame from a vendor's open being sent to the visit's end, both vendors (report-only, judged at 4.2)
    private readonly LootFrameSampler _lootFrames = new(); // M4 2.2 s0: loot_frame_max_ms, the slowest frame from a corpse's open being sent to its close being settled or abandoned (report-only)
    private readonly CombatModeWatch _modeWatch = new(); // M4 3.1 s4 (lane g's watch): mode_mismatch, the moments the combat mode and the wielded weapon disagreed (report-only in its first gate)
    private ContainerWindowDraw? _corpseWaitLogged; // M4 2.2 s4: the container window's draw state the corpse frame last logged waiting for (logged on a change, so a 2 s wait is a few lines)
    private bool _corpseGiveUpLogged;
    private float _runPower;
    private uint _selectedFor;
    private float _meterAtSelection = float.NaN;
    private bool _cameraFramed;
    private float _sideDegrees;
    private double _sideSetAt = double.NegativeInfinity;
    private float _cameraScaleBefore = 1f;
    private bool _skipNextFrame;

    /// <summary>Adds the check to the scene; it starts with the scene's first frame.</summary>
    public static void Install(Main main, GameServices services, SessionHost host, PlayMode mode, RetailUiLayer? ui)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(mode);
        var dats = services.Dats ?? throw new InvalidOperationException("the hunt leg needs the dats (Main checks for them first)");
        var options = HuntOptions.FromArguments(CommandLineArgument.ParseAll(OS.GetCmdlineUserArgs()));
        // M4 P4 (D31): the fighter is the template of the account the leg runs with (--account; none is the test account's Holtburg Hunter); --hunt-weapon and --hunt-ground still override its defaults,
        // and a weapon the template's kit has none of is an option error. An account with no template fails the leg at its first frame (the value is never shown).
        var template = FighterTemplate.ForAccount(host.Options.Account);
        options = options.ForTemplate(template ?? FighterTemplate.Hunter);
        if (template is null)
        {
            options = options with { Errors = [.. options.Errors, "--account: the account has no fighter template (test, test-melee or test-archer)"] };
        }

        // A bad --hunt-kills, --hunt-seconds, --hunt-weapon or --hunt-ground fails the leg at its first frame, as the headless host fails its run (no silent default).
        foreach (var error in options.Errors)
        {
            Log.Error(LogCategory, "hunt: " + error);
        }

        var runner = new HuntLegRunner
        {
            Name = "HuntLegRunner",
            ProcessPriority = 100, // after the session host (-900), play mode (-800) and the retail UI: it looks at what they did this frame
            _host = host,
            _mode = mode,
            _ui = ui,
            _options = options,
            _template = template ?? FighterTemplate.Hunter,
            _optionError = options.Errors.Count == 0 ? null : "bad hunt option: " + string.Join("; ", options.Errors),
            _wholeSeconds = WholeCheckSecondsFor(options.Seconds, options.Sell || options.BuyKits > 0),
            _shotPath = services.Options.Play.BuffShotPath,
            _plan = new HuntShotPlan(options.Weapon),
            _dats = dats,
        };

        // The terrain height at every usable cell's centre of the chosen ground (--hunt-ground: the belt or the forest) is read on a worker (the dats' landblock terrain), so the game thread does not load it in the hunt's first frames.
        var ground = options.GroundRow;
        runner._loading = Task.Factory.StartNew(
            () =>
            {
                var heights = new Dictionary<HuntCell, float?>();
                foreach (var cell in ground.UsableCells)
                {
                    heights[cell] = HuntGround.TerrainHeight(dats, cell);
                }

                return heights;
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        main.AddChild(runner);
        Log.Info(
            LogCategory,
            "login check 'hunt' installed: the test account enters Holtburg Hunter, the hunt scenario runs in the game's session (bind, belt, the weapon, the grounds, the wind-down), then the game logs off and quits",
            ("scenario", HuntScenario.ScenarioName),
            ("options", options.ToString()),
            ("whole_check_s", runner._wholeSeconds),
            ("review_frame", runner._shotPath is null ? "not asked for" : runner._plan.Name));
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_finished)
        {
            return;
        }

        _runSeconds += delta;
        _attackFrames.Step(delta, _skipNextFrame); // the delta is the previous frame's: the frame after a saved review frame (the read-back stalls it) is left out
        _lootFrames.Step(delta, _skipNextFrame); // the same rule for the loot window (a corpse review frame saved inside it is skipped too)
        _vendorFrames.Step(delta, _skipNextFrame); // and for the vendor window (the vendor review frame is saved inside it)
        _skipNextFrame = false;
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
            case Phase.Hunt:
                StepScenario(session);
                break;
            case Phase.LoggingOff:
                LoggingOff(session);
                break;
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        RestoreDeveloperHud();
        DisposeSubscriptions();
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
            Fail(null, "the grounds' terrain could not be read: " + _loading.Exception?.GetBaseException().Message);
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
            if (!HuntScenario.NameCheck(c.Name, _template))
            {
                continue;
            }

            if (c.PendingDeletion)
            {
                Fail(session, "choosing: " + _template.Name + " is pending deletion (restore it first)");
                return;
            }

            session.Actions.SelectCharacter(c.Guid, ActionSource.Host);
            _pending = session.Actions.EnterWorld(c.Guid, ActionSource.Host);
            Log.Info(LogCategory, "hunt: entering the existing character " + _template.Name);
            Go(Phase.Entering);
            return;
        }

        Fail(session, "choosing: " + _template.Name + " is not in the character list (run --scenario=fighter-build first; the hunt leg creates nobody)");
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

    // ------------------------------------------------------------------------------------------------ the hunt scenario

    private void BeginScenario(IGameSessionView session)
    {
        var heights = _loading!.Result;
        // endSession: false, the leg logs off itself (the scenario's run ends at the return to Holtburg).
        _scenario = new HuntScenario(_options, GodotLogSink.Instance, cell => heights.TryGetValue(cell, out var h) ? h : null, ground: _options.GroundRow, endSession: false, follow: new PlayerBodyFollow(_mode), template: _template)
        {
            SeekHeight = (cellId, x, y) => HuntGround.SeekHeight(_dats!, cellId, x, y), // M4 P4 seek: the dry point at the weapon's distance from a creature the client knows
        };
        _subscriptions.Add(session.Events.Subscribe<PlayerDied>(OnDied));
        _subscriptions.Add(session.Events.Subscribe<AttackRequested>(OnAttackRequested));
        _subscriptions.Add(_attackFrames.Attach(session.Events));
        _modeWatch.Start(session);
        _sequenceSeen = session.Attack.Number;
        if (_shotPath is not null)
        {
            if (_options.Weapon == HuntWeapon.Missile)
            {
                _subscriptions.Add(session.Events.Subscribe<ObjectCreated>(OnObjectCreated));
            }

            HideDeveloperHud();
        }

        Go(Phase.Hunt);
    }

    private void OnDied(in PlayerDied e) => _died = true;

    private void OnAttackRequested(in AttackRequested e) => _runPower = e.Power;

    /// <summary>The first arrow's F745: a CreateObject of the ammunition's weenie class that is not held (in the world, not in a pack or wielded) while no frame is owed.</summary>
    private void OnObjectCreated(in ObjectCreated e)
    {
        if (e.IsSelf || !e.InWorld || !_plan.Waiting || _host.Session is not { } session)
        {
            return;
        }

        if (session.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == _template.Kit.AmmoWcid && o.ContainerId is null or 0 && o.WielderId is null or 0)
        {
            _arrowGuid = e.Guid;
            _plan.Trigger(_runSeconds);
        }
    }

    private void StepScenario(IGameSessionView session)
    {
        var scenario = _scenario!;
        StepShot(session, scenario);
        StepUiShots(session, scenario);
        if (_plan.Owed || _uiPlan.Owed)
        {
            // The scenario is held (at most HuntShotPlan.HoldSeconds; the corpse frame HuntUiShotPlan.CorpseHoldSeconds) while the review frame is taken, so the swing or the arrow is still on screen. M4 2.2 fix2 R15: the hold must not
            // suspend the retreat rule and the hunt's seconds: the guards alone run, never a step of the hunt, and a corpse sequence they abandon ends the corpse frame's hold (the window is closing).
            if (scenario.GuardWhileHeld(session))
            {
                _uiPlan.ReleaseCorpse("the retreat rule or the hunt's time came while the frame was owed (the corpse window is closing)");
            }

            return;
        }

        var status = scenario.Step(session, PortalSpaceSetup.Now());
        _lootFrames.Track(scenario.CorpseWindow); // after the step: the frame that sent the open does not count, the one that settled the close does (LootFrameSampler)
        _vendorFrames.Track(scenario.VendorWindow); // the same for a vendor's open and the visit's end (VendorFrameSampler)
        switch (status)
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

    // ------------------------------------------------------------------------------------------------ the review frame

    /// <summary>
    /// <c>--play-screenshot=PATH</c> without <c>--play-screenshot-after</c> (M4 1.2 s4b): the frame the weapon gives (<see cref="HuntShotPlan"/>). Melee triggers on the first own swing of an attack sequence with the
    /// target within <see cref="HuntShotPlan.SwingRangeMetres"/>; missile on the first arrow's F745 (<see cref="OnObjectCreated"/>). At the frame's moment the condition is read again (the target still in range, the
    /// arrow still in the world); when it no longer holds the trigger is dropped and the next one is waited for. A trigger that never comes is logged by name at the end and its frame is not taken.
    /// </summary>
    private void StepShot(IGameSessionView session, HuntScenario scenario)
    {
        if (_shotPath is null || _plan.Taken)
        {
            return;
        }

        if (_plan.Owed || !_hudHidden)
        {
            HideDeveloperHud(); // the HUD may have been drawn again while a frame is owed: hide it again, so the saved frame has none
        }

        if (_hudHidden)
        {
            _hiddenFrames++;
        }

        if (_plan.Slot == 0 && _plan.Waiting)
        {
            var attack = session.Attack;
            if (attack.Number != _sequenceSeen && attack.Swings >= 1)
            {
                _sequenceSeen = attack.Number;
                if (TargetDistance(session, scenario) is { } near && near <= HuntShotPlan.SwingRangeMetres)
                {
                    _plan.Trigger(_runSeconds);
                }
            }
        }

        if (_plan.Expired(_runSeconds))
        {
            _plan.MarkFailed("held longer than " + HuntShotPlan.HoldSeconds.ToString("0", CultureInfo.InvariantCulture) + " s");
            return;
        }

        if (!_plan.Due(_runSeconds, _hiddenFrames))
        {
            return;
        }

        var distance = TargetDistance(session, scenario);
        var holds = _plan.Slot == 0 ? distance is { } d && d <= HuntShotPlan.SwingRangeMetres : session.State.Objects.TryGet(_arrowGuid, out _);

        // M4 polish pass P2: the missile frame is taken from the framed camera (settled, not pulled in by a tree), as the melee review frames are; the player's box above the combat window is reported (the bow is seen only when the player is).
        var arrowPose = _mode.Rig?.Chase.Pose;
        var arrowPivot = arrowPose is { } ap ? PivotDistance(ap) : float.NaN;
        var arrowCameraFramed = _plan.Slot == 0 || (arrowPose is { } aq && HuntUiShotPlan.CameraFramed(arrowPivot, aq.Blocked, _mode.Rig!.Chase.Model.Scale));
        var arrowPlayerKnown = false;
        var arrowPlayerBox = default(Lifestone.Client.Play.ScreenRect);
        var arrowWindowTop = float.NaN;
        if (_plan.Slot != 0 && holds)
        {
            arrowPlayerKnown = _mode.Picking.TryProjectIncludingPlayer(session.State.Self.Guid, out arrowPlayerBox, out _);
            if ((_ui?.Screens?.Flow.CurrentScreen as GamePlayScreen)?.Root?.GetChildRecursive(GameCombatPanel.WindowId) is GameCombatPanel arrowCombat)
            {
                arrowWindowTop = arrowCombat.ScreenBox.Y0;
            }
        }

        if (!holds || !arrowCameraFramed)
        {
            Log.Info(LogCategory, "hunt: the " + _plan.Name + " frame's condition no longer held at its moment (" + (!holds ? (_plan.Slot == 0 ? "the target is out of 6 m" : "the arrow is gone") : "the framed camera has not settled or was pulled in") + "); waiting for the next trigger", ("camera_pivot_m", arrowPivot), ("camera_blocked", arrowPose?.Blocked));
            _plan.Drop();
            return;
        }

        var path = HuntShotPlan.ShotPath(_shotPath, _plan.Slot);
        var saved = ScreenshotWriter.Save(
            GetViewport(),
            path,
            LogCategory,
            ("frame", "hunt-" + _plan.Name),
            ("after_trigger_s", HuntShotPlan.DelaysSeconds[_plan.Slot]),
            ("trigger", HuntShotPlan.Triggers[_plan.Slot]),
            ("target", scenario.TargetName.Length == 0 ? "n/a" : scenario.TargetName),
            ("target_m", distance is { } m ? m.ToString("0.0", CultureInfo.InvariantCulture) : "n/a"),
            ("weapon_wcid", WieldedWeaponWcid(session)),
            ("camera_scale", _mode.Rig?.Chase.Model.Scale),
            ("camera_pivot_m", arrowPivot),
            ("heading_offset", _sideDegrees),
            ("player_box", arrowPlayerKnown ? arrowPlayerBox.ToString() : "n/a"),
            ("player_above_window", _plan.Slot == 0 ? "n/a" : HuntUiShotPlan.PlayerFramed(in arrowPlayerBox, arrowPlayerKnown, arrowWindowTop).ToString()),
            ("combat_window_top", arrowWindowTop),
            ("developer_hud", HudShowing() ? "VISIBLE" : "hidden"));
        Log.Info(LogCategory, (saved ? "hunt: review frame " : "hunt: the review frame could not be saved: ") + _plan.Name + " at its trigger (" + HuntShotPlan.Triggers[_plan.Slot] + ") + " + HuntShotPlan.DelaysSeconds[_plan.Slot].ToString("0.0", CultureInfo.InvariantCulture) + " s: " + path);
        _skipNextFrame = true;
        if (saved)
        {
            _plan.MarkTaken();
        }
        else
        {
            _plan.MarkFailed("the frame could not be saved");
        }
    }

    // ------------------------------------------------------------------------------------------------ the retail-UI review frames (M4 1.3 s2)

    /// <summary>
    /// M4 3.2 s4a: the <c>vendor</c> frame's trigger: Renald the Elder's 0062 is the open vendor in the read model (the scenario names him in <see cref="HuntScenario.SaleVendor"/> from its look-up to the visit's end). The frame is taken
    /// <see cref="HuntUiShotPlan.VendorDelaySeconds"/> later, the scenario held for it (the sale starts after it); the plan ignores a second trigger (taken once).
    /// </summary>
    private void TriggerVendorShot(IGameSessionView session, HuntScenario scenario)
    {
        if (scenario.SaleVendor != 0 && session.Vendor is { } stock && stock.Vendor == scenario.SaleVendor)
        {
            _uiPlan.Trigger(HuntUiShotPlan.Vendor, _runSeconds);
        }
    }

    /// <summary>The missile hunt's share of the retail-UI frames: the <c>vendor</c> frame only (the melee frames need the combat window and a melee run).</summary>
    private void StepVendorShotOnly(IGameSessionView session, HuntScenario scenario)
    {
        if (_uiPlan.Owed || !_hudHidden)
        {
            HideDeveloperHud();
        }

        TriggerVendorShot(session, scenario);
        _uiPlan.Expire(_runSeconds);
        if (_uiPlan.Due(_runSeconds, _hiddenFrames) == HuntUiShotPlan.Vendor)
        {
            SaveVendorShot(session, scenario, _ui?.Screens?.Flow.CurrentScreen as GamePlayScreen);
        }
    }

    /// <summary>
    /// Saves the <c>vendor</c> frame, whatever shows: Renald's stock must still be the open vendor (else the trigger is dropped and waits for the next). The log line says whether a vendor panel was visible (s4b builds the panel; the frame
    /// is taken without it, and the line says so), the vendor's rows and the pyreals held, so the owner reads what the frame should show.
    /// </summary>
    private void SaveVendorShot(IGameSessionView session, HuntScenario scenario, GamePlayScreen? shell)
    {
        var slot = HuntUiShotPlan.Vendor;
        if (session.Vendor is not { } stock || scenario.SaleVendor == 0 || stock.Vendor != scenario.SaleVendor)
        {
            Log.Info(LogCategory, "hunt: the vendor frame's condition no longer held at its moment (the vendor is no longer open); waiting for the next trigger");
            _uiPlan.Drop(slot);
            return;
        }

        var panel = VendorPanelVisible(shell?.Root);
        var path = HuntUiShotPlan.ShotPath(_shotPath!, slot);
        var saved = ScreenshotWriter.Save(
            GetViewport(),
            path,
            LogCategory,
            ("frame", "hunt-" + HuntUiShotPlan.Names[slot]),
            ("after_trigger_s", HuntUiShotPlan.DelaysSeconds[slot]),
            ("trigger", HuntUiShotPlan.Triggers[slot]),
            ("vendor_panel_visible", panel ?? "no"),
            ("vendor_rows", stock.Entries.Length),
            ("vendor_mask", $"0x{stock.Mask:X8}"),
            ("pyreals", session.State.Self.Qualities.CoinValue),
            ("developer_hud", HudShowing() ? "VISIBLE" : "hidden"));
        Log.Info(LogCategory, (saved ? "hunt: review frame " : "hunt: the review frame could not be saved: ") + HuntUiShotPlan.Names[slot] + " (vendor panel visible: " + (panel ?? "no") + "; the frame is taken whatever shows): " + path);
        _skipNextFrame = true;
        if (saved)
        {
            _uiPlan.MarkTaken(slot);
        }
        else
        {
            _uiPlan.MarkFailed(slot, "the frame could not be saved");
        }
    }

    /// <summary>The class name of the first shown element under <paramref name="root"/> whose class name says vendor (s4b's panel; the runner does not reference it), or null when none is shown.</summary>
    private static string? VendorPanelVisible(Lifestone.Client.RetailUi.Core.UiElement? root)
    {
        if (root is null)
        {
            return null;
        }

        var stack = new Stack<Lifestone.Client.RetailUi.Core.UiElement>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var element = stack.Pop();
            if (element.IsShown && element.GetType().Name.Contains("Vendor", StringComparison.OrdinalIgnoreCase))
            {
                return element.GetType().Name;
            }

            foreach (var child in element.Children)
            {
                stack.Push(child);
            }
        }

        return null;
    }

    /// <summary>
    /// The melee hunt's three retail-UI frames (<see cref="HuntUiShotPlan"/>: <c>c1-combat</c>, <c>c1-powerbar</c>, <c>meter</c>), asked for with <c>--play-screenshot=PATH</c>. The camera is framed for the whole review hunt
    /// (<see cref="FrameCamera"/>, restored at the end) and the hunt's target is selected, as a player's click would, so the toolbar has a health meter to show. A trigger is read from the combat window's meter, the attack sequence and
    /// the selection display; at the frame's moment the conditions are read again (the target within 6 m, the camera settled and not blocked, the window or meter in the state the frame is named for) and a frame whose
    /// condition fails is dropped, as the swing frame is. Nothing here changes what the hunt does.
    /// </summary>
    private void StepUiShots(IGameSessionView session, HuntScenario scenario)
    {
        if (_shotPath is null)
        {
            return;
        }

        if (_options.Weapon != HuntWeapon.Melee)
        {
            FrameCamera(); // M4 polish pass P2: the missile hunt's frames are framed too (the bow seen from its side, the player above the combat window)
            StepVendorShotOnly(session, scenario); // the missile hunt takes no melee frame; the shop's frame is the same (M4 3.2 s4a)
            return;
        }

        FrameCamera();
        if (_uiPlan.Owed)
        {
            HideDeveloperHud();
        }

        var shell = _ui?.Screens?.Flow.CurrentScreen as GamePlayScreen;
        var combat = shell?.Root?.GetChildRecursive(GameCombatPanel.WindowId) as GameCombatPanel;
        var display = shell?.SelectionUi;
        var container = shell?.Root?.GetChildRecursive(GameContainerPanel.WindowId) as GameContainerPanel;
        var meter = combat?.GetChildRecursive(GameCombatPanel.MeterId) as UiMeter;
        var target = scenario.Target;
        var distance = TargetDistance(session, scenario);
        var near = distance is { } d && d <= HuntUiShotPlan.TargetRangeMetres;
        var attack = session.Attack;
        var corpseNear = scenario.CorpseOpen && ObjectDistance(session, scenario.CorpseGuid) is { } corpseM && corpseM <= HuntUiShotPlan.TargetRangeMetres;

        // The hunt's target is selected (a click on it), once per target; the meter's fraction when it showed first is the one a later hit is lower than.
        if (target != 0 && _selectedFor != target)
        {
            _selectedFor = target;
            _meterAtSelection = float.NaN;
            _ = _mode.Selection.Select(target);
        }

        if (display is { HealthShown: true, HealthFraction: { } shown } && display.Selected == target && target != 0 && float.IsNaN(_meterAtSelection) && attack.Swings == 0)
        {
            _meterAtSelection = shown;
        }

        var melee = combat is { IsShown: true } && combat.Mode == CombatModes.Melee;
        if (near && attack.Active && attack.Swings >= 1 && melee)
        {
            _uiPlan.Trigger(0, _runSeconds);
        }

        if (near && attack.Active && meter is not null && melee && HuntUiShotPlan.MidCharge(meter.Position, _runPower))
        {
            _uiPlan.Trigger(1, _runSeconds);
        }

        if (near && attack.Swings >= 1 && display is { HealthShown: true, HealthFraction: { } now } && display.Selected == target && HuntUiShotPlan.MeterLowered(_meterAtSelection, now))
        {
            _uiPlan.Trigger(2, _runSeconds);
        }

        if (corpseNear)
        {
            _uiPlan.Trigger(HuntUiShotPlan.Corpse, _runSeconds); // 2.1 F1: the scenario is held (StepScenario) at most CorpseHoldSeconds, so the close waits for the frame
        }

        // M4 3.1 p: the heal and potion frames. The counters are the hunt driver's HealTally (a finished heal handle is recorded there; the runner never reads the scenario's private state): a kit use whose roll won, or a drink that restored
        // points, triggers its frame once, taken 1 s later with the camera framed as the others are.
        var healTally = scenario.HealTally;
        _uiPlan.ObserveHeals(new HealSnapshot(healTally.Heals, healTally.Potions, healTally.Wasted, healTally.Points), _runSeconds);

        TriggerVendorShot(session, scenario);
        _uiPlan.Expire(_runSeconds);
        if (!_corpseGiveUpLogged && _uiPlan.IsGivenUp(HuntUiShotPlan.Corpse))
        {
            _corpseGiveUpLogged = true;
            Log.Warn(
                LogCategory,
                "hunt: the corpse frame was given up: the container window was not drawn within " + HuntUiShotPlan.CorpseHoldSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s of the corpse being open (q6-corpse-objdesc-live and DoD 13 get no frame from this kill)",
                ("window_draw", container?.DrawState.ToString() ?? "no window"),
                ("cells", container?.ItemRow?.CellCount ?? -1));
        }

        var slot = _uiPlan.Due(_runSeconds, _hiddenFrames);
        if (slot < 0)
        {
            return;
        }

        if (slot == HuntUiShotPlan.Vendor)
        {
            SaveVendorShot(session, scenario, shell);
            return;
        }

        if (slot == HuntUiShotPlan.Corpse && (container?.DrawState ?? ContainerWindowDraw.Hidden) is var drawn && drawn != ContainerWindowDraw.Drawn)
        {
            // M4 2.2 s4 (DoD 13): the frame waits for the container window itself (shown, every entry listed, every icon drawn) inside the scenario's hold; the slot stays owed (not dropped, so the scenario stays held and the loot has
            // not started) until the window is drawn or the hold ends (Expire, above, gives it up by name).
            if (_corpseWaitLogged != drawn)
            {
                _corpseWaitLogged = drawn;
                Log.Info(LogCategory, "hunt: the corpse frame waits for the container window", ("window_draw", drawn.ToString()), ("cells", container?.ItemRow?.CellCount ?? -1), ("title", container?.Title ?? "n/a"));
            }

            return;
        }

        var pose = _mode.Rig?.Chase.Pose;
        var pivotDistance = pose is { } p ? PivotDistance(p) : float.NaN;
        var framed = pose is { } q && HuntUiShotPlan.CameraFramed(pivotDistance, q.Blocked);
        var level = meter?.Position ?? float.NaN;
        var healthNow = display?.HealthFraction ?? float.NaN;
        var subject = slot == HuntUiShotPlan.Corpse ? scenario.CorpseGuid : target;
        var healSlot = HuntUiShotPlan.IsHealSlot(slot); // the heal and potion frames need the framed camera only: the hunt heals between kills, so no target is asked for
        var holds = (healSlot || (slot == HuntUiShotPlan.Corpse ? corpseNear : near)) && framed && slot switch
        {
            0 => melee && attack.Active,
            1 => melee && attack.Active && meter is not null && HuntUiShotPlan.MidCharge(level, _runPower),
            HuntUiShotPlan.Corpse => scenario.CorpseOpen,
            HuntUiShotPlan.Heal or HuntUiShotPlan.Potion => true,
            _ => display is not null && display.Selected == target && HuntUiShotPlan.MeterLowered(_meterAtSelection, healthNow),
        };
        var targetBox = default(Lifestone.Client.Play.ScreenRect);
        var playerBox = default(Lifestone.Client.Play.ScreenRect);
        var overlap = 0f;
        var playerKnown = false;
        if (holds && combat is not null && !healSlot)
        {
            // 1.3-L1: the player, the weapon and the target all in view: the target's box on screen, above the combat window and not mostly behind the player's.
            var projected = _mode.Picking.TryProject(subject, out targetBox, out var targetOnScreen);
            playerKnown = _mode.Picking.TryProjectIncludingPlayer(session.State.Self.Guid, out playerBox, out _);
            if (!playerKnown && !_playerBoxRefusalLogged)
            {
                // M4 3.1 p (m4-hunt-frame-camera): 1.3's live frames printed player_box=n/a on every frame because the layer test refused the player (SelectionProjectionRules); if the box is missing again, the first frame names the input.
                _playerBoxRefusalLogged = true;
                Log.Warn(LogCategory, "hunt: the player's box did not project at the first review frame; the projection rule judges no overlap until it does", ("why", _mode.Picking.WhyNotProjected(session.State.Self.Guid, allowPlayer: true) ?? "no input refused it now (the box projected a moment later)"), ("player_guid", $"0x{session.State.Self.Guid:X8}"), ("frame", HuntUiShotPlan.Names[slot]));
            }

            var visible = projected && HuntUiShotPlan.TargetVisible(in targetBox, targetOnScreen, in playerBox, playerKnown, combat.ScreenBox.Y0, out overlap);
            if (!visible)
            {
                var settled = _runSeconds - _sideSetAt >= HuntUiShotPlan.SideSettleSeconds;
                var before = _sideDegrees;
                if (settled && _mode.Rig is { } sideRig)
                {
                    _sideDegrees = HuntUiShotPlan.NextSideOffset(_sideDegrees);
                    sideRig.Chase.Model.SetHeadingOffset(_sideDegrees);
                    _sideSetAt = _runSeconds;
                }

                Log.Info(
                    LogCategory,
                    "hunt: the " + HuntUiShotPlan.Names[slot] + " frame's target is hidden or off screen at its moment; " + (settled ? "the camera turns to the next side" : "the camera is still settling"),
                    ("target_projected", projected),
                    ("target_box", targetBox.ToString()),
                    ("target_on_screen", targetOnScreen),
                    ("player_box", playerKnown ? playerBox.ToString() : "n/a"),
                    ("overlap", overlap),
                    ("combat_window_top", combat.ScreenBox.Y0),
                    ("heading_offset_before", before),
                    ("heading_offset_now", _sideDegrees));
                _uiPlan.Drop(slot);
                return;
            }
        }

        if (!holds)
        {
            Log.Info(
                LogCategory,
                "hunt: the " + HuntUiShotPlan.Names[slot] + " frame's condition no longer held at its moment; waiting for the next trigger",
                ("target_m", distance is { } m ? m.ToString("0.0", CultureInfo.InvariantCulture) : "n/a"),
                ("camera_framed", framed),
                ("camera_blocked", pose?.Blocked),
                ("camera_pivot_m", pivotDistance),
                ("combat_window_melee", melee),
                ("run_active", attack.Active),
                ("meter", level),
                ("run_power", _runPower));
            _uiPlan.Drop(slot);
            return;
        }

        var path = HuntUiShotPlan.ShotPath(_shotPath, slot);
        var saved = ScreenshotWriter.Save(
            GetViewport(),
            path,
            LogCategory,
            ("frame", "hunt-" + HuntUiShotPlan.Names[slot]),
            ("after_trigger_s", HuntUiShotPlan.DelaysSeconds[slot]),
            ("trigger", HuntUiShotPlan.Triggers[slot]),
            ("target", scenario.TargetName.Length == 0 ? "n/a" : scenario.TargetName),
            ("corpse", slot == HuntUiShotPlan.Corpse ? CorpseText(session, scenario, container) : "n/a"),
            ("combat_window_shown", slot == HuntUiShotPlan.Corpse ? combat is { IsShown: true } : null), // False at the corpse frame: the combat floaty is hidden while the container floaty shows (they share the place 0,400; M4 2.2 fix2 R12)
            ("target_m", distance is { } near2 ? near2.ToString("0.0", CultureInfo.InvariantCulture) : "n/a"),
            ("camera_scale", _mode.Rig?.Chase.Model.Scale),
            ("camera_pivot_m", pivotDistance),
            ("camera_blocked", pose?.Blocked),
            ("player_translucency", pose?.PlayerTranslucency),
            ("combat_meter", level),
            ("target_box", targetBox.ToString()),
            ("player_box", playerKnown ? playerBox.ToString() : "n/a"),
            ("player_overlap", overlap),
            ("heading_offset", _sideDegrees),
            ("run_power", _runPower),
            ("health_meter_at_selection", _meterAtSelection),
            ("health_meter_now", healthNow),
            ("weapon_wcid", WieldedWeaponWcid(session)),
            ("heal_tally", healSlot ? string.Create(CultureInfo.InvariantCulture, $"kit_uses={healTally.KitUses} heals={healTally.Heals} potions={healTally.Potions} wasted={healTally.Wasted} points={healTally.Points}") : "n/a"),
            ("developer_hud", HudShowing() ? "VISIBLE" : "hidden"));
        Log.Info(LogCategory, (saved ? "hunt: review frame " : "hunt: the review frame could not be saved: ") + HuntUiShotPlan.Names[slot] + " (" + HuntUiShotPlan.Triggers[slot] + "): " + path);
        _skipNextFrame = true;
        if (saved)
        {
            _uiPlan.MarkTaken(slot);
        }
        else
        {
            _uiPlan.MarkFailed(slot, "the frame could not be saved");
        }
    }

    /// <summary>
    /// Frames the chase camera for the review hunt (<see cref="HuntUiShotPlan.FramedScale"/>: 4.5 m behind and 1.35 m above the pivot instead of retail's 2.5 and 0.75, so the player, the wielded weapon and the target are in
    /// view; <c>m4-hunt-frame-camera</c>), once, logging the camera. The scale is put back by <see cref="RestoreCamera"/> when the leg ends. A frame is taken only when the viewer has settled there and was not pulled in by a
    /// wall or a tree.
    /// </summary>
    private void FrameCamera()
    {
        if (_cameraFramed || _mode.Rig is not { } rig)
        {
            return;
        }

        _cameraFramed = true;
        _cameraScaleBefore = rig.Chase.Model.Scale;
        rig.Chase.Model.Scale = HuntUiShotPlan.FramedScale;
        if (_options.Weapon == HuntWeapon.Missile)
        {
            rig.Chase.Model.Scale = HuntUiShotPlan.MissileFramedScale; // closer than the melee frames: the archer and his bow larger in the window (P2)
            _sideDegrees = HuntUiShotPlan.MissileSideDegrees; // the bow seen from its side (P2); the melee frames start straight behind and turn only when the target is hidden
            _sideSetAt = _runSeconds;
            rig.Chase.Model.SetHeadingOffset(_sideDegrees);
        }
        else
        {
            rig.Chase.Model.SetDefaultOffsets();
        }

        Log.Info(LogCategory, "hunt: the review camera is framed for the frames", ("scale_before", _cameraScaleBefore), ("scale", rig.Chase.Model.Scale), ("distance_m", HuntUiShotPlan.FramedMetresOf(rig.Chase.Model.Scale)), ("settled_within_m", HuntUiShotPlan.SettledMetres), ("heading_offset", _sideDegrees));
    }

    private void RestoreCamera()
    {
        if (!_cameraFramed || _mode.Rig is not { } rig || !IsInstanceValid(rig.Chase))
        {
            return;
        }

        rig.Chase.Model.Scale = _cameraScaleBefore;
        rig.Chase.Model.SetDefaultOffsets();
        _sideDegrees = 0f;
        _cameraFramed = false;
    }

    private static float PivotDistance(in Lifestone.Client.Camera.CameraPose pose)
    {
        var viewer = DrawInterpolation.InSpaceOf(pose.Viewer, pose.Pivot.CellId);
        return System.Numerics.Vector3.Distance(pose.Pivot.Origin, viewer.Origin);
    }

    /// <summary>The distance in metres from the player to the scenario's target, or null when either has no position.</summary>
    private static double? TargetDistance(IGameSessionView session, HuntScenario scenario) => ObjectDistance(session, scenario.Target);

    /// <summary>The distance in metres from the player to the object, or null when either has no position.</summary>
    private static double? ObjectDistance(IGameSessionView session, uint guid)
    {
        var state = session.State;
        if (guid == 0 || state.Self.Position is not { } mine || !state.Objects.TryGet(guid, out var target) || target.Position is not { } theirs)
        {
            return null;
        }

        return PositionAgreement.DistanceMetres(in mine, in theirs);
    }

    /// <summary>
    /// The corpse, its entries, the container window and its ObjDesc as one log phrase (2.1 F1, 2.2 s4): <c>Corpse of X 0xGUID with N item(s): 'a' wcid 1, ...; window 'Contents of Corpse of X' draw=Drawn cells=N;
    /// objdesc_parts=.. objdesc_palettes=.. objdesc_textures=..</c>. The ObjDesc counts are the CreateObject's (<c>q6-corpse-objdesc-live</c>: the lead sets them beside the picture).
    /// </summary>
    private static string CorpseText(IGameSessionView session, HuntScenario scenario, GameContainerPanel? window)
    {
        var state = session.State;
        var found = state.Objects.TryGet(scenario.CorpseGuid, out var corpse);
        var name = found ? corpse!.Name : "?";
        var windowText = string.Create(CultureInfo.InvariantCulture, $"window '{window?.Title ?? "n/a"}' draw={window?.DrawState.ToString() ?? "no window"} cells={window?.ItemRow?.CellCount ?? -1}");
        var objDesc = found ? HuntUiShotPlan.ObjDescFacts(corpse!.Created.Model, !ReferenceEquals(corpse.Model, corpse.Created.Model)) : "objdesc=n/a";
        var view = state.Containers.Viewed;
        if (view is null || view.Guid != scenario.CorpseGuid)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{name} 0x{scenario.CorpseGuid:X8} (no view); {windowText}; {objDesc}");
        }

        var list = new System.Text.StringBuilder();
        foreach (var entry in view.Entries)
        {
            list.Append(list.Length == 0 ? string.Empty : ", ");
            list.Append(state.Objects.TryGet(entry.Guid, out var item) ? string.Create(CultureInfo.InvariantCulture, $"'{item.Name}' wcid {item.Weenie.WeenieClassId}") : string.Create(CultureInfo.InvariantCulture, $"0x{entry.Guid:X8}"));
        }

        return string.Create(CultureInfo.InvariantCulture, $"{name} 0x{scenario.CorpseGuid:X8} with {view.Entries.Count} item(s): {(list.Length == 0 ? "none" : list.ToString())}; {windowText}; {objDesc}");
    }

    /// <summary>The weenie class of the wielded melee or missile weapon (the item in the weapon slot, not the ammunition), as text; <c>none</c> when nothing is wielded there.</summary>
    private static string WieldedWeaponWcid(IGameSessionView session)
    {
        var state = session.State;
        var equipped = state.Self.Inventory.Equipped;
        for (var i = 0; i < equipped.Count; i++)
        {
            if ((equipped[i].Location & ((uint)EquipMask.MeleeWeapon | (uint)EquipMask.MissileWeapon)) != 0 && state.Objects.TryGet(equipped[i].Guid, out var weapon))
            {
                return weapon.Weenie.WeenieClassId.ToString(CultureInfo.InvariantCulture);
            }
        }

        return "none";
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

    // The first time a node is hidden its visibility is kept for the restore; hiding it again must not overwrite that with false.
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

    private void DisposeSubscriptions()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _modeWatch.Dispose();
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
        _lootFrames.Close(); // the recovery's frames are not the loot window's
        Log.Error(LogCategory, "the hunt leg failed", ("phase", _failedAt), ("why", why));
        if (session is not null && _entered && BuffLegPhases.InWorld(session.State) && _phase != Phase.LoggingOff)
        {
            Log.Info(LogCategory, "hunt: logging off before the run ends as failed");
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
            Log.Info(LogCategory, "hunt: the failed run logged off: " + left.Kind + (left.Detail is null ? string.Empty : ": " + left.Detail));
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

    private void Finish(string? why)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        RestoreDeveloperHud();
        RestoreCamera();
        DisposeSubscriptions();
        var scenario = _scenario;
        scenario?.Tally.Dispose();
        var tally = scenario?.Tally;
        var measures = new HuntLegMeasures(
            tally?.Kills ?? 0,
            tally?.DamageDealt ?? 0,
            tally?.DamageTaken ?? 0,
            tally?.HitsTaken ?? 0,
            tally?.Evades ?? 0,
            tally?.XpGained ?? 0,
            scenario?.Fighter ?? "n/a",
            tally?.Swings ?? 0,
            tally?.Shots ?? 0,
            tally?.Hits ?? 0,
            tally?.Misses ?? 0,
            scenario?.Returned ?? false,
            !_died,
            _options.Weapon,
            _attackFrames.AttackFrameMaxMs,
            scenario?.CorpsesOpened ?? 0,
            scenario?.CorpseItems ?? 0,
            tally?.Looted ?? 0,
            _lootFrames.LootFrameMaxMs,
            tally?.LootSkipped ?? 0,
            scenario?.Heals ?? 0,
            scenario?.KitUses ?? 0,
            scenario?.PotionsUsed ?? 0,
            _options.Loot.Enabled && _options.OpenCorpse, // M4 3.1 s3: looted >= 1 is judged over two or more kills only when the hunt was asked to loot
            _options.Ground,
            _modeWatch.Violations,
            tally?.Sold ?? 0,
            tally?.Bought ?? 0,
            tally?.Pyreals ?? 0,
            tally?.Appraised ?? 0,
            _vendorFrames.VendorFrameMaxMs,
            scenario?.HealthStartShare,
            _options.Recovery.Enabled,
            _options.Recovery.BelowPercent,
            tally?.Blocked ?? 0,
            tally?.Seeks ?? 0,
            scenario?.OneBlowKills ?? 0);
        var outcome = HuntLegVerdict.Compose(_failure ?? why ?? scenario?.Failure ?? (_scenarioEnded ? null : "the hunt did not finish"), measures);
        if (!outcome.Ok)
        {
            Log.Error(LogCategory, "the hunt leg failed", ("phase", _failedAt), ("why", string.Join("; ", outcome.Problems)));
        }

        Log.Info(LogCategory, outcome.Line);
        foreach (var note in outcome.Notes ?? [])
        {
            Log.Warn(LogCategory, note);
        }

        Log.Info(
            LogCategory,
            "hunt detail",
            ("seconds", _runSeconds),
            ("options", _options.ToString()),
            ("scenario_detail", scenario?.DetailFields() ?? "n/a"),
            ("review_frame", _shotPath is null ? "not asked for" : _plan.Note()),
            ("attack_windows", _attackFrames.Windows),
            ("attack_runs", _attackFrames.Runs),
            ("attack_frames", _attackFrames.Frames),
            ("loot_windows", _lootFrames.Windows),
            ("loot_frames", _lootFrames.Frames),
            ("vendor_windows", _vendorFrames.Windows),
            ("vendor_frames", _vendorFrames.Frames),
            ("mode_mismatch_first", _modeWatch.FirstViolation ?? "none"));
        if (_shotPath is not null)
        {
            Log.Info(LogCategory, "hunt: review frame: " + _plan.Note());
            if (_options.Weapon == HuntWeapon.Melee)
            {
                Log.Info(LogCategory, "hunt: retail-UI review frames: " + _uiPlan.Note());
            }
            else
            {
                Log.Info(LogCategory, "hunt: retail-UI review frame: " + _uiPlan.NoteOf(HuntUiShotPlan.Vendor));
            }
        }

        Log.FlushToGodot();
        GetTree().Quit(outcome.Ok ? 0 : 1);
    }

    /// <summary>
    /// The game's <see cref="IHuntFollow"/>: the player controller's walker is the client's own body (<c>AvatarSet.SetMovement</c> gives it ACE's MoveToObject through <c>PhysicsPlayerBody.ApplyServerMovement</c>, and the
    /// controller reports its positions), so there is nothing to make or tick; the hunt only reads where that body stands for its approach distances (the session's own position is the server's word and does not move
    /// from the client's side).
    /// </summary>
    private sealed class PlayerBodyFollow(PlayMode mode) : IHuntFollow
    {
        public string? Failure => null;

        public CellFrame? Position => mode.Rig?.Controller.Walker is { } walker && !walker.Physics.PlacementPending && walker.Physics.Cell is not null ? walker.Position : null;

        public void Prepare(IGameSessionView session, double now)
        {
        }

        public void Tick(IGameSessionView session, double now)
        {
        }

        public void Dispose()
        {
        }
    }
}

using System.Globalization;
using Godot;
using Lifestone.Client.Options;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Chat;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.LifestonePanels;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Autopilot.Commands;
using Lifestone.Client.Session.Autopilot.Steer;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
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
/// The autopilot's dial in play mode (M5 item 4 h3, D20, D59; the spec's DoD 9; the controls of M5 7 D145): a thin Godot shell around <see cref="AutopilotHostLogic"/>, which holds every rule, as <see cref="RebuffDriverHost"/> is around its logic.
/// The node ticks the logic once a frame (<c>ProcessPriority</c> after the session host, play mode and the retail UI: it reads what they did this frame), wires the chat's <b>/auto</b> (<see cref="Auto"/>: the press, with the reason
/// <c>resume</c>) and <b>/stop</b> (<see cref="StopHere"/>) and the tools panel's <b>Autopilot</b> button (the one or the other by the mode), shows the dial's chat lines (<see cref="AutopilotHostLogic.ChatNotice"/>: the movement keys' notice,
/// every stop it made, the scenario's own lines) as local system lines, answers Num Lock with a hint (it no longer toggles the dial), gives the inspector its state (<c>inspector.Autopilot = logic</c>) and, on the first press, reads the spell table
/// and the den's rooms on workers (the same loads as the <c>--login-check=autopilot</c> leg) and builds the journey's parts for each scenario the logic asks for (a start, or a hand-back after a /stop).
/// </summary>
/// <remarks>
/// <para>Installed by <see cref="PlayMode.Install"/> for a normal play session (not for a <c>--login-check</c> leg: <see cref="AutopilotLegRunner"/> runs the same logic inside its own session flow). The checkpoint is a file under the user data
/// (<c>user://autopilot/&lt;character&gt;.json</c>, <see cref="UserDataCheckpointStore"/>; <c>-IsolateUserData</c> isolates it); the reconnect is <see cref="GameSessionAutopilotSession"/>.</para>
/// <para>Only BSD Mage plays (the mage plan): a press on another character is refused by a log line. The panel button is a <b>placeholder</b> for what retail never had, like every Lifestone panel.</para>
/// <para><b>Hand-back and the checkpoint.</b> A hand-back builds a fresh scenario carrying the old one's counts in memory (<see cref="AutopilotCarry"/>); its checkpoint store reads as empty once (<see cref="ColdReadOnceStore"/>), so the file is
/// not counted on top of the carry, and writes through, so the file stays current. A first start reads the file (a restart of the game resumes the totals and the minutes used).</para>
/// </remarks>
public sealed partial class AutopilotHost : Node
{
    private const string LogCategory = "autopilot";

    private SessionHost _host = null!;
    private RetailUiLayer? _ui;
    private AutopilotHostLogic _logic = null!;
    private AutopilotHostOptions _hostOptions = new();
    private AutopilotTestHandOff? _handOff;
    private string? _requiredCharacter;
    private AutopilotOptions? _jobOptions;
    private JourneyPlan? _plan;
    private DatSet? _dats;
    private PlayMode? _mode;
    private GameServices? _services;
    private string[]? _arguments;
    private string? _account;
    private IAutopilotCheckpointStore? _store;
    private AutopilotRungKits? _rungs;
    private AutopilotKit? _kit;
    private KitLoads? _loads;
    private bool _pressPending;
    private object? _wiredScreen;
    private object? _panelHostWired;
    private AutopilotPanelDirector? _panels;
    private bool _loadsNeeded;
    private WindowCloseGate _closeGate = null!;
    private StopFileWatch? _stopFile;
    private ControlDrill? _drill;
    private string? _pressRefusal;
    private CommandRunner? _orders;
    private AutopilotJobSource? _jobs;
    private double _jobsRefreshedAt = double.NegativeInfinity;
    private QuestTimers? _fileTimers;
    private readonly Lifestone.Client.Session.Hosting.LegFaultGuard _guard = new("AutopilotHost", LogCategory, GodotLogSink.Instance);

    /// <summary>The logic the node ticks (the checks read its mode).</summary>
    public AutopilotHostLogic Logic => _logic;

    /// <summary>True while the dial owns the character's buffs (Running or Stopping, and the host not stopped by a fault): the re-buff driver stands down (M5 4 F3, 4-R4).</summary>
    public bool OwnsBuffs => !_guard.Tripped && _logic is { Mode: AutopilotMode.Running or AutopilotMode.Stopping };

    /// <summary>How many times the dial was pressed (the key, the button or the scripted hand-back).</summary>
    public int Presses { get; private set; }

    /// <summary>M6 6 MD2: the automation menu's source (the screen's menu and the inspector read it), or null before the host is set up.</summary>
    public AutopilotJobSource? Jobs => _jobs;

    /// <summary>M6 6 MD2: how often the host refreshes <see cref="Jobs"/> (seconds); a choice from the menu refreshes it at once.</summary>
    public const double JobsRefreshSeconds = 1.0;

    /// <summary>The scripted hand-off of <c>--autopilot-test-interrupt-at</c> (null when not asked for).</summary>
    public AutopilotTestHandOff? HandOff => _handOff;

    /// <summary>Adds the host to the scene for a play session.</summary>
    public static AutopilotHost Install(Main main, GameServices services, SessionHost host, PlayMode mode, RetailUiLayer? ui)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(mode);
        var dats = services.Dats ?? throw new InvalidOperationException("the autopilot needs the dats (Main checks for them first)");
        var arguments = OS.GetCmdlineUserArgs();
        var parsed = CommandLineArgument.ParseAll(arguments);
        var options = AutopilotOptions.FromArguments(parsed) with { Buff = BuffOptions.FromArguments(parsed) }; // 4-R4: the upkeep reads the re-buff driver's own margin (--buff-rebuff-margin)
        var plan = AutopilotHostLogic.PlanFor(options); // M6 0 a6: the level up job plays Winnie on the climber plan (her own checkpoint file); the den job plays BSD Mage on the mage plan, as before
        var levelUp = options.Job == AutopilotJob.LevelUp;
        var hostOptions = AutopilotHostOptions.FromArguments(parsed);
        var ground = JourneyLimits.FightOf(arguments).GroundProfile;
        foreach (var error in options.Errors.Concat(hostOptions.Errors))
        {
            Log.Error(LogCategory, "autopilot: " + error);
        }

        var store = new UserDataCheckpointStore(ProjectSettings.GlobalizePath("user://autopilot/" + AutopilotHostOptions.CheckpointFileName(plan.Character)));
        var node = new AutopilotHost { Name = "AutopilotHost" };
        node._loadsNeeded = true;
        node._kit = null;
        node._loads = null;
        var logic = new AutopilotHostLogic(
            carry =>
            {
                var kit = node._kit ?? throw new InvalidOperationException("the autopilot's parts are not built yet (the first press reads the spell table and the den's rooms)");
                return kit.Create(carry);
            },
            GodotLogSink.Instance,
            options,
            levelUp ? null : ground); // the level up job's ground is the ladder's rung for the character's level, known only once the character is in the world (the first press gives it: AutopilotHostLogic.UseGround)
        WireHost(logic, () => node._kit, () => node._ui?.Manager?.Input.Bindings);
        FightWalkRefusals.Sink ??= GodotLogSink.Instance; // M6 6 B1/MD2: a walk's named refusal (no-planner:<walk>) reaches the run's log
        var questFile = ProjectSettings.GlobalizePath("user://" + QuestTimers.FileName(plan.Character));
        var jobs = AutopilotJobSource.For(logic, options.Job, () => host.Session, () => node.TimersNow(plan.Character, questFile), TimeProvider.System, GodotLogSink.Instance);
        node._steerTimers = () => node.TimersNow(plan.Character, questFile);
        node.Setup(host, ui, logic, hostOptions, AutopilotHostLogic.RequiredCharacter(options), options.Errors.Count == 0 && hostOptions.Errors.Count == 0, jobs);
        (node._jobOptions, node._plan, node._dats, node._mode, node._services, node._arguments, node._account, node._store) = (options, plan, dats, mode, services, arguments, host.Options.Account, store);
        if (levelUp)
        {
            node._rungs = new AutopilotRungKits(logic, options, new RungKitSeam(node), GodotLogSink.Instance); // M6 16 E0b: the order of a rung move is the client's; this node builds the parts
            logic.MoveOn = node._rungs.MoveOnNext; // M6 0 a6: on stop=rung-moved:* the dial restarts once by itself on the next rung
            node._loads = null; // built at the first press, over the rung's ground
        }
        else
        {
            node._loads = node.NewLoads(ground, spells: null);
        }

        main.AddChild(node);
        Log.Info(
            LogCategory,
            "autopilot host installed: the dial is off; /auto and /stop in the chat are its controls (M5 7 D145; Num Lock no longer is), and the tools panel (F7) has an Autopilot button (a placeholder retail never had); the first /auto reads the spell table and the den's rooms on workers",
            ("options", options.ToString()),
            ("hand_off", hostOptions.ToString()),
            ("checkpoint", store.Describe));
        return node;
    }

    /// <summary>
    /// Adds a host over a ready-made logic (the play-checks: no spell table, no den, no required character; a station factory of their own). The key and the button are wired as in a real session.
    /// </summary>
    /// <param name="parent">The node the host is added to.</param>
    /// <param name="host">The session host.</param>
    /// <param name="ui">The retail UI.</param>
    /// <param name="logic">The ready-made dial.</param>
    /// <param name="hostOptions">The host's options.</param>
    /// <param name="jobs">M6 6 MD2: the menu's source; null: one over <paramref name="logic"/>, the session and empty quest timers in memory (the options' default job).</param>
    internal static AutopilotHost InstallWith(Node parent, SessionHost host, RetailUiLayer? ui, AutopilotHostLogic logic, AutopilotHostOptions hostOptions, AutopilotJobSource? jobs = null)
    {
        var node = new AutopilotHost { Name = "AutopilotHost" };
        var memory = new QuestTimers("check");
        node.Setup(host, ui, logic, hostOptions, requiredCharacter: null, enabled: true, jobs ?? AutopilotJobSource.For(logic, new AutopilotOptions().Job, () => host.Session, () => memory, TimeProvider.System, GodotLogSink.Instance));
        parent.AddChild(node);
        return node;
    }

    private void Setup(SessionHost host, RetailUiLayer? ui, AutopilotHostLogic logic, AutopilotHostOptions hostOptions, string? requiredCharacter, bool enabled, AutopilotJobSource jobs)
    {
        ProcessPriority = 60; // after the session host (-900), play mode (-800), the retail UI and the re-buff driver (50): it reads what they did this frame
        _host = host;
        _ui = ui;
        _logic = logic;
        _jobs = jobs;
        logic.MenuInput ??= jobs.BuildInput; // M6 6 FXU (6-M9): the dial judges a quest task (the chat, the menu, the option) by the menu's own input; null while the level is unread
        _hostOptions = hostOptions;
        _requiredCharacter = requiredCharacter;
        _disabledByOptions = !enabled;
        _startByOption = enabled && hostOptions.StartAtLogin;
        _unattendedRun = _startByOption;
        SetupStopFile(hostOptions);
        SetupOrders(logic);
        SetupSteer(hostOptions);
        _closeGate = new WindowCloseGate(() => _logic.Mode, why => _logic.Stop(why), message => Log.Info(LogCategory, message));
        host.CloseDeferral = () => _closeGate.Request() == CloseDecision.Wait; // M5 7 F18: a window close during a run recalls home first (SessionHost._Notification)
        if (hostOptions.HasHandOff)
        {
            _handOff = new AutopilotTestHandOff(hostOptions, line => TypeInChat(_ui, line), logic.TestDeathScripted, () => _ui?.Screens?.Flow.CurrentScreen is GamePlayScreen { Chat.Main: not null });
        }

        if (hostOptions.ControlDrillDirectory is { } drill)
        {
            SetupDrill(drill);
        }
    }

    private bool _disabledByOptions;

    /// <summary>
    /// Gives the dial what only the host knows (M5 4 F3): the person's Reconnect presses (the session's <c>Reconnects</c> less the autopilot's own tries, 4-R8) and whether a movement key is held (4-R16). Shared with the leg.
    /// </summary>
    internal static void WireHost(AutopilotHostLogic logic, Func<AutopilotKit?> kit, Func<RetailKeyBindings?> bindings)
    {
        logic.ForeignReconnects = () => kit()?.ForeignReconnects ?? 0;
        logic.HumanKeyHeld = () => AutopilotTestHandOff.MovementKeyHeld(bindings());
    }

    /// <summary>Called for each scenario the kit makes: the soak line reads the running one; its chat lines (D145) go to the chat as the dial's do.</summary>
    private void OnScenario(AutopilotScenario scenario)
    {
        if (GetTree().Root.FindChild("SoakLine", recursive: true, owned: false) is SoakLine soak)
        {
            soak.BindAutopilot(scenario);
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        using var framePhase = Lifestone.Game.Diagnostics.FramePhases.Autopilot(); // M6 16 Eperf2: this call's share of a long frame
        if (_closeGate.Tick(delta))
        {
            _host.QuitGame("the window was closed during an autopilot run, which has ended or timed out"); // M5 7 F18
            return;
        }

        if (_host.Session is not { } session)
        {
            return;
        }

        if (_guard.Tripped)
        {
            TickFaultEnd(delta, session); // 7-R6: an unattended run whose host faulted still ends (log off, quit, exit code 1); a person's run just keeps going
            return;
        }

        try
        {
            WireShell(_ui, new DialControls(() => StopHere(), Auto, task => RunTask(task), ChatOrder), PressButton, _logic, ref _wiredScreen, new MenuWiring(_jobs, ChooseFromMenu, StopFromMenu));
            RefreshJobs(session, force: false);
            if (_loads is { } loads && _kit is null)
            {
                if (loads.Poll() is { } failure)
                {
                    _pressPending = false;
                    _loads = null;
                    Log.Error(LogCategory, "autopilot: the spell table or the den's rooms could not be read, so the autopilot cannot start: " + failure);
                    BeginFaultEnd("the kit load failed");
                }
                else if (loads.Ready)
                {
                    loads.Build();
                    if (_pressPending)
                    {
                        _pressPending = false;
                        _logic.Toggle(session, _pendingStartedBy, _pendingReason);
                        _pendingStartedBy = null;
                        _pendingReason = null;
                    }
                }
            }

            TickUnattendedStart(session);
            TickStopFile(delta);
            TickSteer(session, delta);
            _handOff?.Tick(delta, _logic);
            _logic.Tick(session, PortalSpaceSetup.Now());
            _orders?.Tick(); // the command registry: after the dial, it reads what the dial did this frame (a safe point, a taken order, an end)
            if (_drill is { Finished: false } drill)
            {
                drill.Tick(_logic, session, session.Clock.Now); // M5 7 D145: the control drill, after the dial (it reads what the dial did this frame)
            }

            TickPanels(session.Clock.Now);
            TickUnattendedEnd(delta, session);
        }
        catch (Exception e) when (Lifestone.Client.Session.Hosting.LegFaultGuard.Guards(e))
        {
            // As the re-buff driver: reported once, the dial is stopped for this run and the game keeps running (the logic's scenario is released).
            _guard.Catch("_Process", e);
            _logic.Dispose(); // D145: it unlocks the movement keys too
            Log.Error(LogCategory, "autopilot: the autopilot is stopped for this run after the fault above (the game keeps running)");
            ShowChat(_ui, "Lifestone: the autopilot stopped after a fault (see the log); the controls are yours.");
            BeginFaultEnd("the host faulted");
        }
    }

    /// <summary>M5 7 G3: the panels a person would keep open by the dial's state (<see cref="AutopilotPanelDirector"/>), over the play screen's panel host; nothing without a game screen.</summary>
    private void TickPanels(double now)
    {
        if (_ui?.Screens?.Flow.CurrentScreen is not GamePlayScreen { PanelHost: { } host })
        {
            return;
        }

        if (!ReferenceEquals(host, _panelHostWired))
        {
            _panelHostWired = host;
            _panels = new AutopilotPanelDirector(new PlayPanelSurface(host), line => Log.Info(LogCategory, line));
        }

        _panels?.Tick(_logic.Mode is AutopilotMode.Running or AutopilotMode.Stopping, _logic.Scenario?.Stage, now);
    }

    /// <summary>The retail UI's panel host as the director's surface: open is the toolbar button's own toggle, close hides the page.</summary>
    private sealed class PlayPanelSurface(GamePanelHost host) : IAutopilotPanelSurface
    {
        public uint Shown => host.IsOpen ? host.CurrentPanel : 0;

        public bool Open(uint panelId) => host.KindOf(panelId) == PanelPageKind.Built && host.Toggle(panelId); // M5 7 lead: never the NotInM3 fallback for a page not wired yet

        public void Close(uint panelId) => host.SetPanelVisibility(panelId, false);
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        CloseSteer();
        _logic?.Dispose();
        _kit?.Dispose();
    }

    /// <summary>
    /// The dial's press (/auto, the unattended start). A press that needs the journey's parts (a start, a hand-back) reads them first (the first press only: two workers), then toggles; a stop needs none.
    /// </summary>
    public void Press()
    {
        _logic.PersonPressed();
        Press(startedBy: null);
    }

    /// <summary>
    /// M5 7 D145, <b>/auto</b> (the chat's, and the tools panel's button while the dial is not running): while it runs or stops it answers and does nothing else; otherwise the press, with the reason <see cref="AutopilotHostLogic.ResumeReason"/>
    /// (a start from off or done, a hand-back after a /stop or a halt; every start runs the full audit). Returns the chat line.
    /// </summary>
    public string Auto()
    {
        var mode = _logic.Mode;
        _logic.PersonPressed();
        Log.Info(LogCategory, $"autopilot: /auto (mode {mode.ToString().ToLowerInvariant()})");
        if (mode == AutopilotMode.Running)
        {
            return "Lifestone: the autopilot is already running; type /stop to take the controls.";
        }

        if (mode == AutopilotMode.Stopping)
        {
            return "Lifestone: the autopilot is stopping (" + _logic.Status.LastDecision + "); nothing else is done.";
        }

        _pressRefusal = null;
        Press(startedBy: null, reason: AutopilotHostLogic.ResumeReason);
        if (_logic.Mode == AutopilotMode.Running)
        {
            return mode is AutopilotMode.Off or AutopilotMode.Done
                ? "Lifestone: the autopilot starts: it checks his state first, then fights. Type /stop to take the controls."
                : "Lifestone: the autopilot has the controls again: it checks his state first, then resumes. Type /stop to take them.";
        }

        if (_pressPending)
        {
            return "Lifestone: the autopilot is reading the spell table and the den's rooms; it starts when they are read.";
        }

        return "Lifestone: the autopilot did not start (" + (_pressRefusal ?? _logic.Status.LastDecision) + ").";
    }

    /// <summary>
    /// M6 6 MD1, <b>/auto &lt;task&gt;</b>: chooses <paramref name="task"/> (<see cref="AutopilotHostLogic.UseTask"/>, by the chat) and then presses as <see cref="Auto"/> does. A refusal is the returned line and nothing starts (check, act, check:
    /// the choice is checked by the dial before, and the press by its own mode after).
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="by">Who chose it (<see cref="AutopilotHostLogic.TaskByChat"/>, or <see cref="AutopilotHostLogic.TaskByMenu"/> for the menu's row).</param>
    public string RunTask(AutopilotTask task, string by = AutopilotHostLogic.TaskByChat)
    {
        Log.Info(LogCategory, $"autopilot: /auto {task.Name} by the {by} (mode {_logic.Mode.ToString().ToLowerInvariant()})");
        var previous = _logic.Task;
        if (_logic.UseTask(task, by) is { } refusal)
        {
            Log.Warn(LogCategory, $"autopilot: /auto {task.Name} by the {by} refused: {refusal}");
            return $"Lifestone: the autopilot did not take the task {task.Name} ({refusal}); nothing was started.";
        }

        var line = Auto();
        // 6-M12, check after: a press that did not start (and is not waiting on the kit's reads) leaves no task chosen behind it: the previous task is restored, and the line says so.
        if (_logic.Mode is not (AutopilotMode.Running or AutopilotMode.Stopping) && !_pressPending && previous != task)
        {
            var restored = _logic.UseTask(previous, by) is null;
            Log.Warn(LogCategory, $"autopilot: /auto {task.Name} by the {by}: the start was refused, so the task {(restored ? "goes back to " + previous.Name : "could not go back to " + previous.Name)}");
            line += restored ? $" The task stays {previous.Name}." : $" The task {task.Name} stays chosen (it could not go back to {previous.Name}).";
        }

        return line;
    }

    /// <summary>M6 6 MD2 (L1): the menu's row: <see cref="RunTask"/> by the menu; the answer goes to the chat too (as <c>/auto</c>'s does) and the source is refreshed at once (the current task moved).</summary>
    private string ChooseFromMenu(AutopilotTask task)
    {
        var line = RunTask(task, AutopilotHostLogic.TaskByMenu);
        ShowChat(_ui, line);
        RefreshJobs(_host?.Session, force: true);
        return line;
    }

    /// <summary>M6 6 MD2 (L1): the menu's stop row: <see cref="StopHere"/> (the <c>/stop</c> path); the answer goes to the chat too.</summary>
    private string StopFromMenu()
    {
        var line = StopHere("the menu's stop row");
        ShowChat(_ui, line);
        RefreshJobs(_host?.Session, force: true);
        return line;
    }

    /// <summary>M6 6 MD2: refreshes the menu's source at most every <see cref="JobsRefreshSeconds"/> (always when <paramref name="force"/>), once the character has a level (before that the rows would be level 0's).</summary>
    private void RefreshJobs(GameSession? session, bool force)
    {
        if (_jobs is null || session is null || AutopilotHostLogic.LevelOf(session) <= 0)
        {
            return;
        }

        var now = session.Clock.Now;
        if (!force && now - _jobsRefreshedAt < JobsRefreshSeconds)
        {
            return;
        }

        _jobsRefreshedAt = now;
        _jobs.Refresh();
    }

    /// <summary>
    /// M6 6 MD2: the character's quest timers for the menu: the live run's (the last scenario's context, <see cref="JourneyContext.QuestTimersInUse"/>), else a read-only load of the character's file (once; never written here). A file that
    /// cannot be read is logged by name and the menu reads empty timers (every quest due), so it never hides the fault.
    /// </summary>
    private QuestTimers TimersNow(string character, string path)
    {
        if (_kit?.Context is { } context)
        {
            return context.QuestTimersInUse;
        }

        if (_fileTimers is null)
        {
            try
            {
                _fileTimers = QuestTimers.Load(new UserDataCheckpointStore(path), character);
                Log.Info(LogCategory, $"autopilot menu: the quest timers of {character} read from {System.IO.Path.GetFileName(path)} (read-only; {_fileTimers.Entries.Count} entr(ies) under guid 0x{_fileTimers.CharacterGuid:X8}; TODO(m6-menu-timers-guid): the menu does not check the guid, a run binds them)");
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException)
            {
                _fileTimers = new QuestTimers(character);
                Log.Error(LogCategory, $"autopilot menu: the quest timers file {System.IO.Path.GetFileName(path)} could not be read ({e.Message}); the menu reads every quest as due until a run loads them (TODO(m6-menu-timers-file))");
            }
        }

        return _fileTimers;
    }

    /// <summary>M5 7 D145, <b>/stop</b> (the chat's, the tools panel's button while it runs, the drill): he stops where he stands (<see cref="AutopilotHostLogic.StopHere"/>). Returns the chat line.</summary>
    /// <param name="via">Who asked, for the log.</param>
    public string StopHere(string via = "/stop") => _orders is { } orders ? orders.StopByPerson(via) : StopHereCore(via); // the command registry: a person's stop empties the order queue first

    /// <summary>The person's stop itself (the command runner's dial calls it after emptying its queue).</summary>
    private string StopHereCore(string via)
    {
        if (_guard.Tripped)
        {
            return "Lifestone: the autopilot is stopped for this run after a fault; the controls are yours.";
        }

        WithdrawSteerPress(); // a person's stop beats an order's press still waiting for the parts
        return _host?.Session is { } session ? _logic.StopHere(session, via) : "Lifestone: there is no session; nothing to stop.";
    }

    /// <summary>The tools panel's Autopilot button (M5 7 D145): /stop while the dial runs or stops, /auto otherwise; the answer goes to the chat.</summary>
    private void PressButton() => ShowChat(_ui, _logic.Mode is AutopilotMode.Running or AutopilotMode.Stopping ? StopHere("the tools panel's button") : Auto());

    /// <summary>The press, or the start <c>--autopilot-start</c> asks for once the character is in the world (<paramref name="startedBy"/>: the option; the same rules as a press).</summary>
    private void Press(string? startedBy, string? reason = null)
    {
        Presses++;
        if (_guard.Tripped || _host?.Session is not { } session)
        {
            _pressRefusal = "the host is stopped or has no session";
            Log.Warn(LogCategory, "autopilot: the dial was pressed but the host is stopped or has no session");
            return;
        }

        if (_disabledByOptions)
        {
            _pressRefusal = "the --autopilot-* options did not parse; see the log";
            Log.Error(LogCategory, "autopilot: the dial was pressed but the --autopilot-* options did not parse (see the error above); it does nothing");
            return;
        }

        var name = session.State.Self.Name;
        var refusal = _jobOptions is { } job
            ? AutopilotHostLogic.StartRefusal(job, name) // M6 0 a6 (D7, D25): the level up job plays Winnie only, the den job BSD Mage only
            : _requiredCharacter is { } required && name is { Length: > 0 } && !string.Equals(name, required, StringComparison.Ordinal) ? $"it plays {required} only; this character is {name}" : null;
        if (refusal is not null)
        {
            _pressRefusal = refusal;
            Log.Warn(LogCategory, "autopilot: " + refusal);
            if (_jobOptions?.Job == AutopilotJob.LevelUp && startedBy is not (null or OrderDesk.StartedBy))
            {
                BeginFaultEnd(refusal); // an unattended level up run that cannot start ends (exit code 1) rather than idling; an order that cannot start is answered, and the game stays
            }

            return;
        }

        if (_jobOptions is { Job: AutopilotJob.LevelUp } && _logic.Mode is AutopilotMode.Off or AutopilotMode.Done && !_pressPending && !PrepareRung(session, startedBy))
        {
            return;
        }

        if (_logic.Mode is AutopilotMode.Running or AutopilotMode.Stopping || !_loadsNeeded || _kit is not null)
        {
            _logic.Toggle(session, startedBy, reason);
            return;
        }

        if (_pressPending)
        {
            Log.Info(LogCategory, "autopilot: still reading the spell table and the den's rooms; the press is already waiting");
            return;
        }

        _pressPending = true;
        _pendingStartedBy = startedBy;
        _pendingReason = reason;
        _loads?.Start();
        Log.Info(LogCategory, "autopilot: reading the spell table and the den's rooms on workers; the autopilot starts when they are read");
    }

    /// <summary>
    /// The level up job's first press, and each press that starts a fresh run (M6 0 a6; since M6 16 E0b the steps are the client's, <see cref="AutopilotRungKits.PrepareRung"/>): the rung's ground for the character's level and the
    /// run's parts over it. A level whose rung has no run refuses by the ladder's own name (logged there); an unattended press that is refused ends the game with exit code 1. Returns true when the press goes on.
    /// </summary>
    private bool PrepareRung(IGameSessionView session, string? startedBy)
    {
        if (_rungs!.PrepareRung(session) is not { } refusal)
        {
            return true;
        }

        _pressRefusal = refusal;
        if (startedBy is not (null or OrderDesk.StartedBy)) // an order's refusal is answered by the order desk, never ends the game
        {
            BeginFaultEnd(_pressRefusal);
        }

        return false;
    }

    /// <summary>
    /// The play host's half of the level up job's kits (M6 16 E0b; <see cref="IAutopilotKitFactory"/>): what needs the game. The kit is the journey's parts over the game's nodes with the player's own body as the walker
    /// (<see cref="AutopilotKit"/>), read on workers (<see cref="KitLoads"/>; the spell table is shared, not read again) and built by the node's frame when they are read; the press that waits on them is the node's.
    /// </summary>
    private sealed class RungKitSeam(AutopilotHost host) : IAutopilotKitFactory
    {
        /// <inheritdoc/>
        public bool Ready => host._kit is not null;

        /// <inheritdoc/>
        public void Prepare(HuntingGround ground, JourneyPlan plan)
        {
            var spells = host._loads?.SpellsTask;
            host._kit?.Dispose();
            host._kit = null;
            host._plan = plan;
            host._loads = host.NewLoads(ground, spells);
            host._loadsNeeded = true;
        }

        /// <inheritdoc/>
        public void PressWhenReady(string? startedBy, string? reason)
        {
            host._pendingStartedBy = startedBy;
            host._pendingReason = reason;
            host._pressPending = true;
            host._loads!.Start();
        }

        /// <inheritdoc/>
        public AutopilotScenario Create(AutopilotCarry carry) => (host._kit ?? throw new InvalidOperationException("the autopilot's parts are not built yet (the first press reads the spell table and the den's rooms)")).Create(carry);
    }

    /// <summary>The loads (and, when read, the kit) over <paramref name="ground"/> with the host's one plan, options and checkpoint; <paramref name="spells"/> is a spell table already being read, or null to read it.</summary>
    private KitLoads NewLoads(HuntingGround ground, Task<IReadOnlyDictionary<uint, SpellInfo>>? spells)
    {
        KitLoads? loads = null;
        var plan = _plan!;
        loads = new KitLoads(_dats!, plan, ground, () => _kit = new AutopilotKit(plan, () => _host.Session, _mode!, _services!, _dats!, _arguments!, _account, _jobOptions!, ground, loads!, _store!, scenario => OnScenario(scenario)), spells);
        return loads;
    }

    /// <summary>
    /// Wires the chat's /stop and /auto to <paramref name="controls"/> (M5 7 D145), the tools panel's Autopilot button to <paramref name="button"/>, the dial's chat lines to the chat window, Num Lock to its hint, and the inspector to
    /// <paramref name="logic"/>, once to each game screen (its hotkeys exist while the screen does; shared with the login-check leg).
    /// </summary>
    /// <param name="ui">The retail UI.</param>
    /// <param name="controls">The chat's controls.</param>
    /// <param name="button">The tools panel's Autopilot button.</param>
    /// <param name="logic">The dial.</param>
    /// <param name="wired">The hotkeys last wired (one wiring per game screen).</param>
    /// <param name="menu">M6 6 MD2: the automation menu's source and delegates (the play host's); null leaves the menu without a source (it says so) and the tools panel's Jobs button still opens it.</param>
    internal static void WireShell(RetailUiLayer? ui, IAutopilotChatControls controls, Action button, AutopilotHostLogic logic, ref object? wired, MenuWiring? menu = null)
    {
        if (ui?.Screens?.Flow.CurrentScreen is not GamePlayScreen { Hotkeys: { } hotkeys } screen || ReferenceEquals(hotkeys, wired))
        {
            return;
        }

        wired = hotkeys;
        hotkeys.AutopilotKeyHint = line => ShowChat(ui, line); // D145: Num Lock no longer toggles the dial; it says so
        // m5-autopilot-chat-missing: screen.Chat is never null once Hotkeys exists, so the guard asks for what typing needs: the main chat window.
        if (screen.Chat is { Main: not null } chat)
        {
            chat.Autopilot = controls;
        }
        else
        {
            Log.Error(LogCategory, "autopilot: the game screen has no main chat, so /stop and /auto cannot be typed; the rest is still wired (TODO(m5-autopilot-chat-missing): Main is built with the screen, so this is a layout fault)");
        }

        logic.ChatNotice += line => ShowChat(ui, line); // the host logic forwards every scenario's notices (P7n)
        if (screen.ToolsUi is { } tools)
        {
            tools.ToggleAutopilot = button;
            tools.Autopilot = logic; // the button's text follows the dial's mode (4-R17); the panel reads the status only
        }

        // m5-autopilot-vitae-panel: retail opens the vitae page on a worse vitae (GameVitaePage); the dial closes it once the death station ends.
        logic.CloseVitaePage = () =>
        {
            if (screen.PanelHost is { } panels && panels.ElementFor(GameVitaePage.Page) is { Visible: true })
            {
                panels.SetPanelVisibility(GameVitaePage.Page, false);
                return true;
            }

            return false;
        };

        if (screen.Inspector is { } inspector)
        {
            inspector.Autopilot = logic;
            inspector.Jobs = menu?.Source; // M6 6 MD2: the inspector's Job line counts the menu's open quests
        }

        // M6 6 MD2 (L1): the automation menu (a Lifestone placeholder window) reads the host's source and acts through the host's delegates (never IActionApi); the tools panel's Jobs button toggles it.
        if (screen.Menu is { } jobsMenu)
        {
            jobsMenu.Source = menu?.Source;
            jobsMenu.Choose = menu?.Choose;
            jobsMenu.Stop = menu?.Stop;
            if (screen.ToolsUi is { } toolsPanel)
            {
                // M6 6 FXU: the menu hangs under the shell's root (its rows take the clicks) just behind the tools panel; the button that opens it puts the tools panel away, so the menu shows whole (F7 brings the panel back, and
                // its Jobs button, in front of the menu, closes it).
                toolsPanel.ToggleJobs = () =>
                {
                    jobsMenu.Toggle();
                    if (jobsMenu.IsOpen && toolsPanel.IsOpen)
                    {
                        toolsPanel.Close();
                    }
                };
            }
        }
        else
        {
            Log.Error(LogCategory, "autopilot: the game screen has no automation menu (TODO(m6-menu-layout): menu.json is not embedded), so the Jobs button has nothing to open");
        }

        Log.Info(LogCategory, "autopilot: /stop and /auto in the chat, the dial's chat lines, the tools panel's Autopilot and Jobs buttons, the automation menu" + (menu is null ? " (no job source)" : string.Empty) + " and the inspector are wired to the dial; Num Lock answers with a hint (D145)");
    }

    /// <summary>M5 7 D145: shows <paramref name="line"/> in the current game screen's chat as a local system line (never sent) and logs it; a screen with no chat logs that it was not shown.</summary>
    internal static void ShowChat(RetailUiLayer? ui, string line)
    {
        if (ui?.Screens?.Flow.CurrentScreen is GamePlayScreen { Chat: { } chat })
        {
            chat.Local(line);
            Log.Info(LogCategory, "autopilot chat shown: " + line);
        }
        else
        {
            Log.Warn(LogCategory, "autopilot chat NOT shown (no game screen with a chat): " + line);
        }
    }

    /// <summary>
    /// M5 7 D145: types <paramref name="line"/> into the main chat's entry and sends it as a person's Enter does (<c>GameChatWindow.ProcessCommand</c>): the scripted hand-off's and the drill's path. False when there is no main chat. Text a
    /// person left in the entry is cleared first (logged), so the typed line is the whole line.
    /// </summary>
    internal static bool TypeInChat(RetailUiLayer? ui, string line)
    {
        if (ui?.Screens?.Flow.CurrentScreen is not GamePlayScreen { Chat.Main: { } main })
        {
            Log.Warn(LogCategory, "autopilot: no main chat to type '" + line + "' into");
            return false;
        }

        if (main.Entry is { Text.Length: > 0 } entry)
        {
            Log.Warn(LogCategory, "autopilot: the chat entry held text; it is cleared before '" + line + "' is typed");
            entry.ClearAllText();
        }

        main.TypeIntoEntry(line);
        var sent = main.ProcessCommand();
        Log.Info(LogCategory, "autopilot: typed '" + line + "' into the main chat (" + (sent ? "sent" : "NOT sent: the entry was empty") + ")");
        return sent;
    }

    /// <summary>The Godot key and its name for Move Forward, from <paramref name="bindings"/>: the first keyboard binding of the movement map with no meta key that has a Godot key.</summary>
    internal static (Key Key, string Name)? MovementKey(RetailKeyBindings? bindings) => AutopilotTestHandOff.MovementKeyOf(bindings);
}

/// <summary>
/// The spell table and the den's rooms, read on workers when the first press asks (<see cref="JourneyLegRunner.StartLoads"/>, as the login-check leg does at its install) and the kit built when they are done.
/// </summary>
internal sealed class KitLoads(DatSet dats, JourneyPlan plan, HuntingGround ground, Action build, Task<IReadOnlyDictionary<uint, SpellInfo>>? spells = null)
{
    private Task<IReadOnlyDictionary<uint, SpellInfo>>? _spells = spells;
    private Task<RoomGraph>? _den;
    private bool _started;

    /// <summary>The spell table's read (null before <see cref="Start"/>), shared with the next rung's loads (M6 0 a6: the table is read once a run).</summary>
    public Task<IReadOnlyDictionary<uint, SpellInfo>>? SpellsTask => _spells;

    /// <summary>The spell table once read.</summary>
    public IReadOnlyDictionary<uint, SpellInfo> Spells => _spells!.Result;

    /// <summary>The den's rooms (null for a plan with no den station).</summary>
    public Task<RoomGraph>? Den => _den;

    /// <summary>Starts the two workers (once).</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        if (_spells is null)
        {
            (_spells, _den) = JourneyLegRunner.StartLoads(dats, plan, ground);
        }
        else if (plan.Ground == StationWhere.Den)
        {
            _den = Task.Factory.StartNew(() => GroundRooms.Load(dats, ground), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default); // a next rung's dungeon: its rooms, the table already read
        }
    }

    /// <summary>True when both are read.</summary>
    public bool Ready => _started && _spells is { IsCompletedSuccessfully: true } && (_den is null || _den.IsCompletedSuccessfully);

    /// <summary>The failure of a worker, or null (while running or when read).</summary>
    public string? Poll() => !_started || _spells is null ? null : _spells.IsFaulted ? _spells.Exception?.GetBaseException().Message ?? "unknown" : _den is { IsFaulted: true } ? _den.Exception?.GetBaseException().Message ?? "unknown" : null;

    /// <summary>Builds the kit.</summary>
    public void Build() => build();
}

/// <summary>
/// Makes the scenarios the dial asks for (a start or a hand-back): the journey's parts for the plan in the session (<see cref="JourneyLegRunner.BuildParts"/>, so the play host, the login check and the journey build one way), the autopilot
/// over them with the checkpoint store and the reconnect, the carry applied. The runner host of the previous scenario is released when the next is made (the logic disposed that scenario when the person took the controls).
/// </summary>
internal sealed class AutopilotKit : IDisposable
{
    private readonly JourneyPlan _plan;
    private readonly Func<GameSession?> _session;
    private readonly PlayMode _mode;
    private readonly GameServices _services;
    private readonly DatSet _dats;
    private readonly IReadOnlyList<string> _arguments;
    private readonly string? _account;
    private readonly AutopilotOptions _options;
    private readonly HuntingGround _ground;
    private readonly KitLoads _loads;
    private readonly IAutopilotCheckpointStore _store;
    private readonly Action<AutopilotScenario>? _onCreated;
    private GodotJourneyRunnerHost? _runners;
    private int _ownReconnects;

    /// <summary>A kit over the loaded spell table and den.</summary>
    public AutopilotKit(JourneyPlan plan, Func<GameSession?> session, PlayMode mode, GameServices services, DatSet dats, IReadOnlyList<string> arguments, string? account, AutopilotOptions options, HuntingGround ground, KitLoads loads, IAutopilotCheckpointStore store, Action<AutopilotScenario>? onCreated)
    {
        (_plan, _session, _mode, _services, _dats, _arguments, _account, _options, _ground, _loads, _store, _onCreated) = (plan, session, mode, services, dats, arguments, account, options, ground, loads, store, onCreated);
    }

    /// <summary>The runner host of the scenario made last (its streamer stats), or null.</summary>
    public GodotJourneyRunnerHost? Runners => _runners;

    /// <summary>M6 6 MD2: the journey context of the scenario made last (its quest timers are the live run's: the menu reads them), or null before the first.</summary>
    public JourneyContext? Context { get; private set; }

    /// <summary>The session's reconnects that were not the autopilot's own tries: the person's Reconnect button (M5 4 F3, 4-R8). Moves only when a person reconnects.</summary>
    public int ForeignReconnects => _session() is { } game ? game.Reconnects - _ownReconnects : 0;

    /// <summary>The scenario for <paramref name="carry"/> (<see cref="AutopilotCarry.None"/> for a start).</summary>
    public AutopilotScenario Create(AutopilotCarry carry)
    {
        ArgumentNullException.ThrowIfNull(carry);
        var game = _session() ?? throw new InvalidOperationException("the autopilot needs the game session");
        _runners?.Dispose();
        var (factory, context, runners) = JourneyLegRunner.BuildParts(_plan, game, _loads.Spells, _arguments, _dats, _account, _mode, _services, _loads.Den, _ground, observer: null, windows: null);
        _runners = runners;
        Context = context;
        var session = new OwnTryCountingSession(new GameSessionAutopilotSession(game, _plan.Character, owner => _mode.Flow.HoldAutoEnter(owner)), () => game.Reconnects, tries => _ownReconnects += tries);
        // M6 16 E0b: the carry is the client's, one copy for every host (AutopilotRungKits.Scenario): a hand-back's seed, totals and cold-read store (4-R9, 4-R5), a move-on's hand-over (M6 item 14).
        var scenario = AutopilotRungKits.Scenario(carry, _options, factory, context, _store, session);
        _onCreated?.Invoke(scenario);
        return scenario;
    }

    /// <inheritdoc/>
    public void Dispose() => _runners?.Dispose();
}

/// <summary>
/// The autopilot's own reconnect tries, counted apart from the person's Reconnect button (M5 4 F3, 4-R8): each try is measured as the change in the session's <c>Reconnects</c> across the call, so the dial can tell when a
/// person's press moved it, and each try is named in the log.
/// </summary>
internal sealed class OwnTryCountingSession(IAutopilotSession inner, Func<int> reconnects, Action<int> counted) : IAutopilotSession
{
    private int _tries;

    /// <inheritdoc/>
    public string? LoginFailure => inner.LoginFailure;

    /// <inheritdoc/>
    public void Reconnect()
    {
        _tries++;
        Log.Info("autopilot", $"autopilot: its own reconnect try {_tries} begins (not the disconnected screen's button); the session's reconnects before it: {reconnects()}");
        var before = reconnects();
        try
        {
            inner.Reconnect();
        }
        finally
        {
            counted(reconnects() - before);
        }
    }

    /// <inheritdoc/>
    public ScenarioStatus StepLogin(double now) => inner.StepLogin(now);
}

/// <summary>
/// The game's <see cref="IAutopilotCheckpointStore"/> (M5 item 4 h3): the checkpoint is a file in the user data (<c>user://autopilot/&lt;character&gt;.json</c>). <see cref="Write"/> writes a temporary file beside it and moves it over the checkpoint in
/// one step (<c>File.Move(temp, path, overwrite: true)</c>); <see cref="SetAside"/> renames the file <c>.aside</c>, then <c>.aside2</c> and so on, and never deletes it. The client does no file I/O (<c>ArchitectureTests</c>); the headless host has its own
/// (<c>AutopilotFileCheckpointStore</c>, the same steps).
/// </summary>
/// <param name="path">The checkpoint file (an absolute path).</param>
public sealed class UserDataCheckpointStore(string path) : IAutopilotCheckpointStore
{
    /// <summary>The checkpoint file.</summary>
    public string FilePath { get; } = path ?? throw new ArgumentNullException(nameof(path));

    /// <inheritdoc/>
    public string Describe => System.IO.Path.GetFileName(FilePath);

    /// <inheritdoc/>
    public string? Read() => System.IO.File.Exists(FilePath) ? System.IO.File.ReadAllText(FilePath) : null;

    /// <inheritdoc/>
    public void Write(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            if (System.IO.Path.GetDirectoryName(FilePath) is { Length: > 0 } folder)
            {
                System.IO.Directory.CreateDirectory(folder);
            }

            var temporary = FilePath + ".tmp";
            System.IO.File.WriteAllText(temporary, json);
            System.IO.File.Move(temporary, FilePath, overwrite: true);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new System.IO.IOException(e.Message, e); // the autopilot names an IOException and tries again at the next state change
        }
    }

    /// <inheritdoc/>
    public string SetAside()
    {
        var target = FilePath + ".aside";
        for (var n = 2; System.IO.File.Exists(target); n++)
        {
            target = FilePath + ".aside" + n.ToString(CultureInfo.InvariantCulture);
        }

        System.IO.File.Move(FilePath, target);
        return target;
    }
}

/// <summary>
/// The gate's scripted hand-off (M5 item 4 h3, D58; <c>--autopilot-test-interrupt-at S</c>, <c>--autopilot-test-handback-after S</c>): the lane cannot have a person at the keys, so this types the person's controls into the main chat's entry and
/// sends them (since M5 7 D145 the controls are /stop and /auto: a movement key does nothing while the autopilot runs, and Num Lock is no longer its key), the path a person's typing takes (the chat window's submit, the client's command table),
/// never a call into the dial's logic. <see cref="Tick"/> counts seconds from the first fight: at the interrupt time /stop is typed; the hand-back time after that, /auto. Each is a log line (<c>test-interrupt: typed /stop in the &lt;state&gt;
/// state</c>, <c>test-handback: typed /auto</c>).
/// </summary>
public sealed class AutopilotTestHandOff
{
    private const string LogCategory = "autopilot";

    private enum Step
    {
        WaitStart,
        WaitFight,
        WaitInterrupt,
        WaitHandBack,
        Done,
    }

    private readonly AutopilotHostOptions _options;
    private readonly Func<string, bool> _type;
    private readonly AutopilotInterruptArm _arm;
    private readonly Func<bool>? _chatReady;
    private readonly ChatWait _wait = new();
    private Step _step = Step.WaitStart;
    private string? _state;
    private double _clock;
    private double _until;

    /// <summary>A scripted hand-off for <paramref name="options"/>.</summary>
    /// <param name="options">The options (<see cref="AutopilotHostOptions.InterruptAtSeconds"/> must be set).</param>
    /// <param name="type">Types a line into the main chat's entry and sends it; false when there is no chat.</param>
    /// <param name="deathScripted">True when the run scripts a death: S then counts from the first fight after its cycle (4-R10).</param>
    /// <param name="chatReady">True while a game screen with a main chat is up (M6 6 MD1, <c>m5-autopilot-test-chat</c>): each typing waits for it, bounded by <see cref="ChatWait"/>; null means always.</param>
    internal AutopilotTestHandOff(AutopilotHostOptions options, Func<string, bool> type, bool deathScripted = false, Func<bool>? chatReady = null)
    {
        _chatReady = chatReady;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _type = type ?? throw new ArgumentNullException(nameof(type));
        _arm = new AutopilotInterruptArm(deathScripted);
        if (options.InterruptAtSeconds is null)
        {
            throw new ArgumentException("the scripted hand-off needs --autopilot-test-interrupt-at", nameof(options));
        }
    }

    /// <summary>True when /stop was typed.</summary>
    public bool Interrupted { get; private set; }

    /// <summary>True when /auto was typed.</summary>
    public bool HandedBack { get; private set; }

    /// <summary>True when nothing more is scripted.</summary>
    public bool Finished => _step == Step.Done;

    /// <summary>
    /// One frame: waits for the autopilot to run, then does the scripted presses at their times.
    /// </summary>
    /// <param name="delta">The frame's seconds.</param>
    /// <param name="logic">The dial (its mode and state).</param>
    public void Tick(double delta, AutopilotHostLogic logic)
    {
        switch (_step)
        {
            case Step.WaitStart:
                if (logic.Mode is AutopilotMode.Running or AutopilotMode.Stopping)
                {
                    _step = Step.WaitFight;
                    Log.Info(LogCategory, $"test-interrupt: the autopilot is running; S = {_options.InterruptAtSeconds!.Value.ToString("0.#", CultureInfo.InvariantCulture)} s counts from the first fight" + (logic.TestDeathScripted ? " after the scripted death's cycle" : string.Empty) + (_options.HandBackAfterSeconds is { } h ? $", then /auto {h.ToString("0.#", CultureInfo.InvariantCulture)} s after /stop" : ", and the controls are not handed back"));
                }

                break;
            case Step.WaitFight:
                if (_arm.Update(logic))
                {
                    _clock = 0;
                    _step = Step.WaitInterrupt;
                    Log.Info(LogCategory, "test-interrupt: the fight is on (state " + logic.Scenario?.Stage + ", cycle " + logic.Scenario?.Cycle + "); /stop is typed in " + _options.InterruptAtSeconds!.Value.ToString("0.#", CultureInfo.InvariantCulture) + " s");
                }

                break;
            case Step.WaitInterrupt:
                _clock += delta;
                _state = logic.Scenario?.Stage;
                if (_clock >= _options.InterruptAtSeconds!.Value && ChatUp(delta))
                {
                    TypeStop();
                }

                break;
            case Step.WaitHandBack:
                _clock += delta;
                if (_clock >= _until && ChatUp(delta))
                {
                    _step = Step.Done;
                    HandedBack = _type("/auto");
                    Log.Info(LogCategory, HandedBack ? "test-handback: typed /auto" : "test-handback: /auto could not be typed (no chat window); the controls stay the person's");
                }

                break;
        }
    }

    // m5-autopilot-test-chat: before typing, a bounded and logged wait for a game screen with a main chat (a reconnect or portal screen is the real case); false while waiting; on the bound the script ends by name.
    private bool ChatUp(double delta)
    {
        var result = _wait.Poll(delta, _chatReady?.Invoke() ?? true, out var line);
        if (line is not null)
        {
            if (result == ChatWaitResult.GaveUp)
            {
                Log.Error(LogCategory, line);
            }
            else
            {
                Log.Info(LogCategory, line);
            }
        }

        if (result == ChatWaitResult.GaveUp)
        {
            _step = Step.Done;
        }

        return result == ChatWaitResult.Ready;
    }

    private void TypeStop()
    {
        if (!_type("/stop"))
        {
            _step = Step.Done;
            Log.Error(LogCategory, "test-interrupt: there is no chat window to type /stop into; nothing was typed (TODO(m5-autopilot-test-chat))");
            return;
        }

        Interrupted = true;
        Log.Info(LogCategory, "test-interrupt: typed /stop in the " + (_state ?? "unknown") + " state");
        if (_options.HandBackAfterSeconds is { } after)
        {
            _until = _clock + after;
            _step = Step.WaitHandBack;
        }
        else
        {
            _step = Step.Done;
        }
    }

    /// <summary>
    /// True while a movement key of the key map's movement input map (forward, back, strafes, turns; keyboard, no meta key) is held (M5 4 F3, 4-R16): the dial defers a hand-back until it is up.
    /// </summary>
    /// <param name="bindings">The key map (null: none held).</param>
    internal static bool MovementKeyHeld(RetailKeyBindings? bindings)
    {
        if (bindings is null)
        {
            return false;
        }

        foreach (var binding in bindings.Bindings)
        {
            if (binding.InputMapId != UiInputRouter.MovementMap || binding.MetaMode != 0 || binding.Control.DeviceIndex != RetailKeyBindings.KeyboardDevice
                || binding.ActionId is not (MovementBindings.MoveForward or MovementBindings.MoveBackward or MovementBindings.StrafeLeft or MovementBindings.StrafeRight or MovementBindings.TurnLeft or MovementBindings.TurnRight))
            {
                continue;
            }

            foreach (var code in GodotKeyCodes.MappedKeycodes)
            {
                if (GodotKeyCodes.ToDik(code) == binding.Control.Semantic && Input.IsPhysicalKeyPressed((Key)code))
                {
                    return true;
                }
            }
        }

        return false;
    }


    /// <summary>
    /// Move Forward's key in <paramref name="bindings"/> (retail's key map, the movement input map): the first keyboard binding with no meta key whose scan code a Godot key reaches (<see cref="GodotKeyCodes"/>), with a name for the log; null when none.
    /// </summary>
    /// <param name="bindings">The key map (null: none).</param>
    internal static (Key Key, string Name)? MovementKeyOf(RetailKeyBindings? bindings)
    {
        if (bindings is null)
        {
            return null;
        }

        foreach (var binding in bindings.Bindings)
        {
            if (binding.InputMapId != UiInputRouter.MovementMap || binding.ActionId != MovementBindings.MoveForward || binding.MetaMode != 0 || binding.Control.DeviceIndex != RetailKeyBindings.KeyboardDevice)
            {
                continue;
            }

            foreach (var code in GodotKeyCodes.MappedKeycodes)
            {
                if (GodotKeyCodes.ToDik(code) == binding.Control.Semantic)
                {
                    return ((Key)code, OS.GetKeycodeString((Key)code) + " (Move Forward)");
                }
            }
        }

        return null;
    }
}

/// <summary>M6 6 MD2 (L1): the automation menu's host wiring: the source the menu and the inspector read, and the two delegates its rows call (each returns the line the menu's status shows).</summary>
/// <param name="Source">The job source.</param>
/// <param name="Choose">A job, quest or errand row: the task by the menu, then the press.</param>
/// <param name="Stop">The stop row: the <c>/stop</c> path.</param>
internal sealed record MenuWiring(IAutopilotJobSource? Source, Func<AutopilotTask, string>? Choose, Func<string>? Stop);

using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Chat;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Indicators;
using Lifestone.Client.RetailUi.LifestonePanels;
using Lifestone.Client.RetailUi.Options;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Radar;
using Lifestone.Client.RetailUi.Selection;
using Lifestone.Client.RetailUi.Toolbar;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>
/// The in-game shell (<c>gmGamePlayUI</c>, UI mode 0x10000008; docs/retail-ui.md §21): the end-of-retail floating-window shell, root 0x10000495 of
/// <c>classic_gameplay</c> (layout enum 0x10000006, D36), made at the full window. Its constructor releases the forced 800x600 display
/// (<c>Device::ForceDisplayResolution</c>, 11.4186 0x0043A750, 11.6096 0x0043A990; <c>gmGamePlayUI::gmGamePlayUI</c>, 11.6096 0x004EACA0) so the shell
/// follows the window, and finds the windows <c>gmGamePlayUI::SetupChildren</c> (11.6096 0x004EAB50) finds by id (<see cref="UiLayoutEnums.Gameplay"/>).
/// </summary>
/// <remarks>
/// <para><b>What it is.</b> A frame for the slices that fill the windows: g2 the spew box and the chat windows, g3 the vitals and the radar, g4 the
/// toolbar and the panel host, g5 the selection, g6 the options and log-off. Each finds its window here (<see cref="SpewBox"/>, <see cref="MainChat"/>,
/// <see cref="FloatingChats"/>, <see cref="StackedVitals"/>, <see cref="Radar"/>, <see cref="Toolbar"/>, <see cref="Panel"/>) and calls
/// <see cref="MarkWindowBuilt"/> when its window has content, which takes it off the placeholder note. Until then a window keeps its retail art from the
/// layout and is a labelled placeholder (<see cref="PlaceholderNote"/>).</para>
/// <para><b>The SmartBox</b> (0x1000049A, <c>gmSmartBoxUI</c>) fills the screen in this shell. Its viewport 0x10000436 is the hole the world shows
/// through, and the hole the portal tunnel is drawn in (the host registers a source for it, G1). The layout hides the viewport; the screen shows it,
/// as <c>gmSmartBoxUI::PostInit</c> (11.6096 0x004D77C0) presumably does (TODO(g4-smartbox-viewport): that function is not read). The SmartBox's
/// window rectangle is <see cref="GameViewBox"/>, which the host publishes as <c>GameViewRect</c> (<c>gmSmartBoxUI::RecvNotice_UpdateGameView</c>,
/// 11.6096 0x004D6F20; D40: the whole window here, the hook stays).</para>
/// <para><b>Painted.</b> The shell is made when the mode changes, and the host paints it over the next frames. <see cref="ShellPainted"/> turns true when
/// it has, and until then the host keeps the world hidden, so no frame shows the world without the shell.</para>
/// <para>It returns to character management when the server sends the character list again (a logoff: <c>LogOffRequested</c> through portal space's
/// logoff sequence, then F658); a lost connection is the screen host's (the disconnected screen).</para>
/// </remarks>
public sealed class GamePlayScreen : IUiScreen, IUiPlaceholderScreen
{
    /// <summary>The root's id in <c>classic_gameplay</c> (<see cref="UiLayoutEnums.GameplayRoot"/>).</summary>
    public const uint RootElementId = 0x10000495;

    /// <summary>The SmartBox's viewport (the world's hole; the portal tunnel is drawn in it).</summary>
    public const uint SmartBoxViewportId = 0x10000436;

    /// <summary>The main chat window (<c>gmMainChatUI</c>, 11.6096 0x004CDD60), from <c>classic_floatychat</c>.</summary>
    public const uint MainChatId = 0x10000601;

    /// <summary>The toolbar window (<c>gmToolbarUI</c>, 11.6096 0x004BE550), from <c>classic_floatytoolbar</c>.</summary>
    public const uint ToolbarId = 0x10000603;

    /// <summary>The panel host window (the floating panel), from <c>classic_floatypanel</c>.</summary>
    public const uint PanelId = 0x100005FF;

    /// <summary>
    /// The spew box window: 450x72 at the SmartBox's top centre, 0x10000046, a child of the SmartBox. It is the element of UI class 0x10000016, which is
    /// <c>gmSpewBoxUI::GetUIElementType</c> (11.6096 0x004D6710 is its <c>PostInit</c>), and holds the list 0x10000049 <c>PostInit</c> looks for (g2 confirmed it).
    /// </summary>
    public const uint SpewBoxId = 0x10000046;

    /// <summary>The windows that are placeholders when the screen is made, in the order the note names them.</summary>
    public static IReadOnlyList<string> InitialPlaceholders { get; } =
    [
        "spew box", "chat", "vitals", "radar", "toolbar", "panels", "indicators", "selection",
    ];

    private readonly UiLoginContext _context;
    private readonly List<string> _placeholders = [.. InitialPlaceholders];
    private readonly IDisposable _subscription;
    private readonly IDisposable _logOffSubscription;
    private bool _disposed;

    /// <summary>
    /// Registers the element classes the shell's layout uses beyond the login screens' (the radar, the options check box, the link indicator). The screen calls it, and so does the warm-up
    /// (<c>ShellWarmUp</c>), so both build the same tree and neither logs a false "no runtime" TODO for a class that is built.
    /// </summary>
    public static void RegisterClasses(UiClassRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(UiClassIds.Radar, static (in UiElementInit init) => new UiRadarElement(init)); // gmRadarUI::Register (11.6096 0x004D97E0)
        registry.Register(0x10000035, static (in UiElementInit init) => new UiOptionCheckbox(init)); // UIOption_Checkbox::Register (11.6096 0x00487000)
        registry.Register(UiClassIds.LinkStatusIndicator, static (in UiElementInit init) => new UiLinkStatusIndicator(init)); // gmUIElement_LinkStatusIndicator::Register (11.6096 0x004E73A0)

        // The windows whose behaviour G4 and G6 build as a controller over the plain window element (the screen's Game* classes find the window by id and fill it), not as a subclass of the
        // retail class: registered so the registry does not report them as classes with no runtime (a false "TODO(retailui-runtime)" line per class in every login; review R7).
        // The classes with no controller (the power bar, the vitae, effects and burden indicators, the chat and config options pages, the map, the inventory and the rest) stay loud.
        foreach (var classId in ControllerClasses)
        {
            registry.Register(classId, static (in UiElementInit init) => new UiElement(init));
        }

        // M3 3.2 s0: the three floaty windows of the combat and spell slices (docs/milestones/m3-plan.md section 4.8) are plain containers until their controllers land: gmCombatUI (0x1000000C), gmPowerbarUI (0x1000000F)
        // and gmSpellcastingUI (0x10000015). They are windows of the gameplay layout, not host pages; layout-combat.txt, layout-powerbar.txt and layout-spellcasting.txt (PanelLayoutDumpTests) show each subtree.
        registry.Register(0x1000000C, static (in UiElementInit init) => new UiElement(init));
        registry.Register(0x1000000F, static (in UiElementInit init) => new UiElement(init));
        registry.Register(0x10000015, static (in UiElementInit init) => new UiElement(init));

        GamePanels.RegisterClasses(registry); // the panel slices' own classes (M3 2.2: GamePanels.A1.cs ... B3.cs)
    }

    /// <summary>The retail classes of the shell's windows that a Lifestone controller drives over a plain window element (<see cref="RegisterClasses"/>).</summary>
    public static IReadOnlyList<uint> ControllerClasses { get; } =
    [
        0x10000014, // gmSmartBoxUI: the world's frame and viewport (SmartBox, GameViewBox, GameSelection's indicator)
        0x10000016, // gmSpewBoxUI: GameChat's spew box
        0x10000040, // gmFloatyChatUI: GameChat's floating chat windows
        0x10000050, // gmFloatyMainChatUI: GameChat's main chat window
        0x1000004D, // gmFloatyVitalsUI: GameVitals' stacked window
        0x10000056, // gmFloatySideVitalsUI: GameVitals' side-by-side window
        0x10000052, // gmFloatyIndicatorsUI: GameIndicators' window (its link indicator is registered above; the other indicators stay loud)
        0x10000051, // gmFloatyToolbarUI: GameToolbar's window
        0x10000007, // gmToolbarUI: GameToolbar's buttons and GameSelection's display
        0x1000004F, // gmFloatyPanelUI: GamePanelHost
        0x1000001A, // gmCharacterInfoUI: GameCharacterInfo's report page
        0x10000027, // gmCharacterSettingsUI: GameOptionsPanel's character page
        0x10000029, // gmGameplayOptionsUI: GameOptionsPanel's gameplay page and the logout
    ];

    /// <summary>
    /// Releases the forced display, makes the shell's root and finds its windows; follows the session's character list. With <see cref="UiLoginContext.StageShell"/> the root is only begun here:
    /// <see cref="Update"/> makes it and wires the windows over the next frames (<see cref="BuildBudgetMilliseconds"/> a frame, <see cref="IsBuilt"/> when done); otherwise it is all made here.
    /// </summary>
    public GamePlayScreen(UiLoginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        var manager = context.Manager;
        _buildStart = System.Diagnostics.Stopwatch.GetTimestamp();
        manager.ForceDisplayResolution(false); // gmGamePlayUI::gmGamePlayUI (11.6096 0x004EACA0)
        RegisterClasses(manager.Registry);
        _subscription = context.Session.Events.Subscribe<CharacterListChanged>(OnCharacterListChanged);
        _logOffSubscription = context.Session.Events.Subscribe<LogOffRequested>(OnLogOffRequested); // M5 6 u1: the window layout is written at log-off
        if (context.StageShell)
        {
            // The root is made over frames (UiStagedRoot, the same tree and order as CreateRootElement), then the windows are wired a controller at a time: the build is the part of the arrival that
            // cost 21 to 29 ms in one frame, and the live gate's 50 ms rule failed on the machine's luck (docs/session.md section 27). The portal notice waits for the spew box (UiNoticeRouter.ExpectWindows).
            _staged = manager.BeginRootElement(UiLayoutEnums.GameplayRoot.LayoutEnum, UiLayoutEnums.GameplayRoot.ElementId);
            if (_staged is not null)
            {
                BeginNoGcWindow();
                if (context.Notices is { } notices)
                {
                    notices.ExpectWindows = true;
                }

                return;
            }
        }
        else
        {
            Root = manager.CreateRootElement(UiLayoutEnums.GameplayRoot.LayoutEnum, UiLayoutEnums.GameplayRoot.ElementId);
        }

        _rootMade = System.Diagnostics.Stopwatch.GetTimestamp();
        var wiring = Wire();
        while (wiring.MoveNext())
        {
        }

        Complete();
    }

    /// <summary>The most a frame spends building the shell when it is staged (a unit of work is one element or one controller, so a frame may run a little over; <see cref="LongestBuildUnitMilliseconds"/>).</summary>
    public const double BuildBudgetMilliseconds = 3.0;

    private readonly long _buildStart;
    private long _rootMade;
    private UiStagedRoot? _staged;
    private int _wiringUnitIndex;
    private int _longestWiringUnitIndex;
    private IEnumerator<bool>? _wiring;
    private bool? _showRequested;
    private double _wiringMilliseconds;
    private double _longestWiringUnit;

    /// <summary>True once the root is made and every window is wired (at once for a screen that is not staged).</summary>
    public bool IsBuilt { get; private set; }

    /// <summary>The frames that built the shell when it is staged (0 when it was built at once).</summary>
    public int BuildFrames { get; private set; }

    /// <summary>Report-only (M4 1.1 s4): the collector's work inside the build's own frames (StepBuild), the total pause in milliseconds and the collections per generation, so a probe reads the shell's GC share.</summary>
    public (double PauseMilliseconds, int Gen0, int Gen1, int Gen2) BuildGc => (_buildGcPause, _buildGc0, _buildGc1, _buildGc2);

    /// <summary>Report-only: the collector's pause in the slowest wiring unit, in milliseconds.</summary>
    public double LongestWiringUnitGcPauseMilliseconds { get; private set; }

    /// <summary>Report-only: what the no-GC window did ("off", "refused", "held" or "ended early"); its sizing is in <see cref="NoGcWindowSizing"/>.</summary>
    public string NoGcWindowReport => _noGc.Report;

    /// <summary>Report-only: the heap count the window was sized for and the bytes it asked for, and "UNDERSIZED" when the cap cut the request (by the per-heap estimate the window may end early on that many heaps; this machine's 32 heaps held 11 MiB under a 512 MiB request, so the estimate is conservative).</summary>
    public string NoGcWindowSizing => $"heaps {_noGc.Heaps} ({_noGc.HeapSource}), requested {_noGc.RequestedBytes / (1024 * 1024)} MiB{(_noGc.Undersized ? $", UNDERSIZED: {_noGc.Heaps} heaps need more than the {NoGcWindow.CapBytes / (1024 * 1024)} MiB cap by the per-heap estimate, the window may end early; the held or ended-early state is the measurement" : string.Empty)}";

    /// <summary>Report-only (M4 P5b, <c>TODO(m4-throwaway-teleport-frame)</c>): the pause of the gen 0 collection that closed a held window and gave gen 0 its budget back, in milliseconds; NaN until it runs, and when none is due.</summary>
    public double NoGcWindowClosingMilliseconds => _noGc.ClosingCollectionMilliseconds;

    /// <summary>
    /// M4 P5b (<c>TODO(m4-throwaway-teleport-frame)</c>): runs the gen 0 collection that a held no-GC window's end left pending, which gives gen 0 its normal budget back. The retail UI layer calls it at the
    /// start of each step, before the step's clock; nothing is pending on most frames.
    /// </summary>
    public static void RunPendingNoGcWindowClose() => NoGcWindow.RunPendingClose();

    private readonly NoGcWindow _noGc = new();
    private long _allocStart;

    // Test seam (internal, set only by the test assembly's module initializer): the window is off under the test host unless a test turns it on.
    internal static bool NoGcWindowEnabled = true;

    private void BeginNoGcWindow()
    {
        _allocStart = GC.GetAllocatedBytesForCurrentThread();
        if (NoGcWindowEnabled)
        {
            _noGc.Begin();
        }
    }

    private void EndNoGcWindow() => _noGc.End();

    // M4 1.1 fix (R2): the first collection after the window ends, measured on the game thread's frames (Update), report-only.
    private bool _postWindowWatching;
    private int _postWindowFrames;
    private int _postGc0;
    private int _postGc1;
    private int _postGc2;
    private TimeSpan _postPause;
    private bool _closeAccounted; // M4 P5b: the window's closing collection has been taken out of the watch's count

    /// <summary>Report-only: the first collection after the no-GC window ended ("pending" until one runs; "gen1 6.2 ms, 3 frames after Complete"; "none in 900 frames" until one runs after that).</summary>
    public string PostWindowGcReport { get; private set; } = "pending";

    private const int PostWindowGcWatchFrames = 900;

    private void WatchPostWindowGc()
    {
        if (!_postWindowWatching)
        {
            return;
        }

        var gc0 = GC.CollectionCount(0);
        var gc1 = GC.CollectionCount(1);
        var gc2 = GC.CollectionCount(2);
        var pause = GC.GetTotalPauseDuration();
        if (!_closeAccounted && (_noGc.ClosePending || !double.IsNaN(_noGc.ClosingCollectionMilliseconds)))
        {
            // M4 P5b: a held window's closing collection runs at the start of a later UI step (NoGcWindow.RunPendingClose); the watch counts from after it, so it reports the next collection, the natural one.
            _closeAccounted = !_noGc.ClosePending;
            _postGc0 = gc0;
            _postGc1 = gc1;
            _postGc2 = gc2;
            _postPause = pause;
            _postWindowFrames = 0;
            return;
        }

        _postWindowFrames++;
        if (gc0 != _postGc0 || gc1 != _postGc1 || gc2 != _postGc2)
        {
            var generation = gc2 != _postGc2 ? 2 : gc1 != _postGc1 ? 1 : 0;
            var milliseconds = (pause - _postPause).TotalMilliseconds; // every thread's pause since the last frame's snapshot: the first collection's, as none ran before it
            var last = GC.GetGCMemoryInfo(GCKind.Any); // the last collection of the frame (M4 P5b): how much gen 0 held when it began and what it promoted
            PostWindowGcReport = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"gen{generation} {milliseconds:0.0} ms, {_postWindowFrames} frames after {(_closeAccounted ? "the closing collection" : "Complete")}");
            _postWindowWatching = false;
            _context.Manager.Services.Log.Info(UiElementManager.Category, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"the in-game shell: first collection after the no-gc window ({NoGcWindowReport}{ClosingText()}): {PostWindowGcReport} (collections gen0/1/2 {gc0 - _postGc0}/{gc1 - _postGc1}/{gc2 - _postGc2}; the last: gen0 {last.GenerationInfo[0].SizeBeforeBytes / 1048576.0:0.0} MiB before, promoted {last.PromotedBytes / 1048576.0:0.0} MiB; m3-ui-step-gc-margin (closed) closes only if this is under the 12 ms margin)"));
            return;
        }

        _postPause = pause;
        if (_postWindowFrames == PostWindowGcWatchFrames)
        {
            // M4 P5b (TODO(m4-post-window-gc-log)): the "none" case is logged too, so a live gate can read it, and the watch goes on until the first collection runs (four counter reads a frame). After a
            // held window that no collection closed, "none" meant gen 0 still had the region's budget, and the collection that ended it was the next arrival's long pause.
            PostWindowGcReport = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"none in {_postWindowFrames} frames");
            _context.Manager.Services.Log.Info(UiElementManager.Category, $"the in-game shell: first collection after the no-gc window ({NoGcWindowReport}{ClosingText()}): {PostWindowGcReport} (still watching)");
        }

        string ClosingText() => double.IsNaN(_noGc.ClosingCollectionMilliseconds)
            ? string.Empty
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $", closed by a gen0 collection of {_noGc.ClosingCollectionMilliseconds:0.0} ms (process cpu {_noGc.ClosingCollectionCpuMilliseconds:0.0} ms)");
    }

    private double _buildGcPause;
    private int _buildGc0;
    private int _buildGc1;
    private int _buildGc2;

    /// <summary>The longest single unit of the staged build (an element, an initialize or a controller), in milliseconds; 0 when it was built at once.</summary>
    public double LongestBuildUnitMilliseconds => Math.Max(_staged?.LongestUnitMilliseconds ?? 0.0, _longestWiringUnit);

    /// <summary>The longest frame of the staged build so far (<see cref="Update"/>'s share), in milliseconds; 0 when it was built at once.</summary>
    public double LongestBuildFrameMilliseconds { get; private set; }

    // gmGamePlayUI::SetupChildren (11.6096 0x004EAB50) and the slices' controllers, one chunk between two yields; the unstaged screen runs them back to back.
    private IEnumerator<bool> Wire()
    {
        var context = _context;
        var manager = context.Manager;
        if (Root is null)
        {
            manager.Services.Log.Error(UiElementManager.Category, $"gmGamePlayUI: root {UiLayoutEnums.GameplayRoot} does not exist in the layouts; the shell shows nothing");
            yield break;
        }

        SmartBox = Find<UiElement>(UiLayoutEnums.Gameplay.SmartBox);
        SmartBoxViewport = Find<UiViewport>(SmartBoxViewportId);
        SpewBox = Find<UiElement>(SpewBoxId);
        Radar = Find<UiElement>(UiLayoutEnums.Gameplay.Radar);
        StackedVitals = Find<UiElement>(UiLayoutEnums.Gameplay.StackedVitals);
        SideBySideVitals = Find<UiElement>(UiLayoutEnums.Gameplay.SideBySideVitals);
        MainChat = Find<UiElement>(MainChatId);
        Toolbar = Find<UiElement>(ToolbarId);
        Panel = Find<UiElement>(PanelId);
        Indicators = Find<UiElement>(GameIndicators.WindowId);
        var chats = new List<UiElement>();
        foreach (var id in UiLayoutEnums.Gameplay.FloatingChats)
        {
            if (Find<UiElement>(id) is { } chat)
            {
                chats.Add(chat);
            }
        }

        FloatingChats = chats;

        // The SmartBox's viewport is the world's hole: the layout hides it, the shell shows it.
        SmartBoxViewport?.SetVisible(true);

        // The root paints an opaque dirt backdrop over the whole window, which retail's SmartBox 3D view (a scene drawn into the viewport's own surface, above it)
        // covers. Lifestone draws the world behind the UI and shows it through the SmartBox's hole, so the backdrop must not be drawn: it would hide the world.
        // Deviation from the layout's art, by design (docs/retail-ui.md section 21).
        Root.ClearImage();

        // With no art the root's surface is transparent and (property 0x51 unset) not erased, so a window that hides, or moves, over it left its old pixels behind: the tools panel stayed painted after F7
        // (docs/retail-ui.md section 21.3, TODO(g4-erase-exe)). The root erases its dirty pieces, as the spew box does; every child is drawn again over the cleared piece, so nothing else changes.
        Root.SetShouldEraseBackground(true);

        // g3a: the layout shows both vitals windows, which overlap; retail shows one by the character's options (docs/retail-ui.md section 21.3).
        manager.LogOnce(
            "gameplay:window-layout",
            LogSeverity.Warning,
            "TODO(g6-window-locks): the windows' places and sizes are kept per character and window size (M5 6 u1, D33; closes TODO(g4-window-layout-save)), but Lifestone has no window lock (retail's UI lock button 0x10000619 on the radar is not built) and writes no <SBOX> line (TODO(g6-layout-smartbox): the SmartBox is the whole window)");
        yield return true;

        // g2: the spew box and the chat windows, registered on the router the game's text notices go to (a notice held for them is shown now).
        Chat = new GameChat(context, SpewBox, MainChat, FloatingChats);
        if (Chat.Spew is not null)
        {
            MarkWindowBuilt("spew box");
        }

        if (Chat.Main is not null)
        {
            MarkWindowBuilt("chat");
        }

        yield return true;

        Options = new GameOptions(context.Session, manager.Services.Log); // g6: the options go to the server (D32); the model and GameOptions' own overlay keep every reader current, so no subscription order matters
        Vitals = new GameVitals(context, StackedVitals, SideBySideVitals);
        Options.Changed += _ => Vitals.Refresh(chooseWindow: true); // PlayerModule's option notice: the vitals window follows SideBySideVitals at once
        if (Vitals.Stacked is not null || Vitals.SideBySide is not null)
        {
            MarkWindowBuilt("vitals");
        }

        yield return true;

        // g3b: the radar's compass tokens, blips and marker (docs/retail-ui.md section 21.4).
        RadarUi = new GameRadar(manager, context.Session, Radar);
        if (Radar is UiRadarElement)
        {
            MarkWindowBuilt("radar");
        }

        yield return true;

        // g4: the panel host (hidden at login, as gmPanelUI::PostInit), the toolbar's buttons and the link indicator (docs/retail-ui.md section 21.5).
        PanelHost = new GamePanelHost(context, Panel);
        if (Panel is not null)
        {
            MarkWindowBuilt("panels");
        }

        yield return true;

        // g6: character information, the options panel and the logout confirmation fill the host's two hooks (docs/retail-ui.md section 21.8).
        Logout = new GameLogout(context);
        Confirmations = new ServerConfirmation(manager, context.Session); // M5 6 u1: the server's Yes/No questions (GameEvent 0x0274) as dialogs
        CharacterInfo = new GameCharacterInfo(context, PanelHost);
        OptionsPanel = new GameOptionsPanel(context, PanelHost, Options, Logout);
        yield return true;

        ToolbarUi = new GameToolbar(context, Toolbar);
        ToolbarUi.Attach(PanelHost); // M5 7 P7r (D148): the panel buttons light from the host's page notices, one at a time
        if (Toolbar is not null)
        {
            MarkWindowBuilt("toolbar");
        }

        yield return true;

        // g5b: the toolbar's selection display (name, health) and the vivid target indicator (docs/retail-ui.md section 21.7).
        SelectionUi = new GameSelection(context, Toolbar, SmartBox, Options);
        ToolbarUi.SelectedSource = () => SelectionUi.Selected;
        // M5 6 u2: the chat's Selected talk focus tells the selection (docs/retail-ui.md section 21.2).
        Chat.SelectedProvider = () => SelectionUi.Selected;
        if (Toolbar is not null)
        {
            MarkWindowBuilt("selection"); // the selection display lives in the toolbar
        }

        yield return true;

        IndicatorsUi = new GameIndicators(manager, context.Session, Indicators);
        if (IndicatorsUi.Link is not null)
        {
            MarkWindowBuilt("indicators");
        }

        yield return true;

        // M3 2.2: the panel slices' controllers (GamePanels), one staged unit each, after the shell's own controllers they read (the toolbar, the indicators, character information).
        PanelControllers = new GamePanels(context, PanelHost, this);
        var panelUnits = PanelControllers.Build();
        while (panelUnits.MoveNext())
        {
            yield return true;
        }

        // g7 lane A: Lifestone's tools panel, a floating window over the shell that F7 toggles (docs/retail-ui.md section 22.1). Its lines go to the shell's chat.
        ToolsUi = new ToolsPanel(context, Root, line => Chat?.Local(line));
        // g7 lane B: the automation inspector, a floating window that Scroll Lock toggles (section 22.2). Like the tools panel its window is made on its first open.
        Inspector = new AutomationInspector(manager, context.Session);
        // M6 6 MC: the automation menu beside it (L1: no key; the tools panel's button opens it, and the play host wires its source and delegates). Made on its first open, as a child of the shell's root like the tools panel
        // (M6 6 FXU: as a root of its own the shell's root took the clicks over its rows).
        Menu = new AutomationMenu(manager, Root);
        Hotkeys = new LifestoneHotkeys(manager.Services.Log) { OpenTools = ToolsUi.Toggle, OpenInspector = Inspector.Toggle };
        Hotkeys.Attach(manager.Input);
    }

    // The root is made (staged or not) and every window wired.
    private void Complete()
    {
        IsBuilt = true;
        EndNoGcWindow();
        _postGc0 = GC.CollectionCount(0);
        _postGc1 = GC.CollectionCount(1);
        _postGc2 = GC.CollectionCount(2);
        _postPause = GC.GetTotalPauseDuration();
        _postWindowWatching = !_disposed;
        if (_context.Notices is { } notices)
        {
            notices.ExpectWindows = false;
        }

        var manager = _context.Manager;
        var rootMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(_buildStart, _rootMade).TotalMilliseconds;
        var wiringMilliseconds = _staged is null ? System.Diagnostics.Stopwatch.GetElapsedTime(_rootMade).TotalMilliseconds : _wiringMilliseconds;
        manager.Services.Log.Info(
            UiElementManager.Category,
            _staged is null
                ? $"the in-game shell was built: root {rootMilliseconds:0.0} ms, wiring {wiringMilliseconds:0.0} ms"
                : $"the in-game shell was built over {BuildFrames} frames (budget {BuildBudgetMilliseconds:0.0} ms a frame): root {rootMilliseconds:0.0} ms, wiring {wiringMilliseconds:0.0} ms, longest frame {LongestBuildFrameMilliseconds:0.0} ms, longest unit {LongestBuildUnitMilliseconds:0.0} ms (staged unit ended in '{_staged?.LongestUnitPhase}'; wiring unit #{_longestWiringUnitIndex} took {_longestWiringUnit:0.0} ms; staged unit gc {_staged?.LongestUnitGcPauseMilliseconds:0.0} ms {_staged?.LongestUnitGcCollections}; wiring unit gc {LongestWiringUnitGcPauseMilliseconds:0.0} ms; build gc {_buildGcPause:0.0} ms gen0/1/2 {_buildGc0}/{_buildGc1}/{_buildGc2}; no-gc window {NoGcWindowReport} (start {_noGc.StartMilliseconds:0.0} ms, {NoGcWindowSizing}, allocated {(GC.GetAllocatedBytesForCurrentThread() - _allocStart) / 1024} KiB{(_noGc.ClosePending ? ", its closing gen0 collection runs at the next UI step's start" : string.Empty)}); post-window gc is measured on the frames after this line and logged when it runs; slowest controller {PanelControllers?.LongestControllerName} {PanelControllers?.LongestControllerMilliseconds:0.0} ms), {System.Diagnostics.Stopwatch.GetElapsedTime(_buildStart).TotalMilliseconds:0} ms from the start");
        if (_placeholders.Count > 0)
        {
            manager.LogOnce(
                "gameplay:shell",
                LogSeverity.Warning,
                $"placeholder: the in-game shell shows retail's art for its windows with no content yet ({PlaceholderNoteText(_placeholders)}); TODO(g4-shell): g2-g6 fill them");
        }
    }

    // One frame of the staged build: units of work (an element, an initialize, a controller) until the budget is spent.
    private void StepBuild()
    {
        var gc0 = GC.CollectionCount(0);
        var gc1 = GC.CollectionCount(1);
        var gc2 = GC.CollectionCount(2);
        var gcPause = GC.GetTotalPauseDuration();
        try
        {
            StepBuildUnits();
        }
        catch
        {
            EndNoGcWindow(); // a build that throws must not leave the process's collector held off (R3)
            throw;
        }
        finally
        {
            _buildGc0 += GC.CollectionCount(0) - gc0;
            _buildGc1 += GC.CollectionCount(1) - gc1;
            _buildGc2 += GC.CollectionCount(2) - gc2;
            _buildGcPause += (GC.GetTotalPauseDuration() - gcPause).TotalMilliseconds;
        }
    }

    private void StepBuildUnits()
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var deadline = start + (long)(BuildBudgetMilliseconds * System.Diagnostics.Stopwatch.Frequency / 1000.0);
        BuildFrames++;
        while (!IsBuilt)
        {
            if (_wiring is null)
            {
                if (_staged is { Done: false })
                {
                    _staged.Step(deadline);
                }

                if (_staged is not { Done: true })
                {
                    break;
                }

                // The root joined the manager in the unit that finished it: the first wiring chunk (the windows, the cleared backdrop, the shown viewport) runs in this same call, so no composite
                // sees the layout's own dirt backdrop.
                Root = _staged.Root;
                _rootMade = System.Diagnostics.Stopwatch.GetTimestamp();
                if (_showRequested is { } shown)
                {
                    Root?.SetVisible(shown);
                }

                _wiring = Wire();
                if (System.Diagnostics.Stopwatch.GetTimestamp() >= deadline)
                {
                    break; // M3 4.2 fixb: the root's last unit spent the frame (7.9 ms seen), so the first wiring unit waits for the next; no composite runs before IsBuilt (RetailUiLayer.Step's builtThisFrame)
                }
            }

            var unit = System.Diagnostics.Stopwatch.GetTimestamp();
            var unitGcPause = GC.GetTotalPauseDuration();
            var more = _wiring.MoveNext();
            var ended = System.Diagnostics.Stopwatch.GetTimestamp();
            var unitMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(unit, ended).TotalMilliseconds;
            _wiringUnitIndex++;
            if (unitMilliseconds > _longestWiringUnit)
            {
                _longestWiringUnit = unitMilliseconds;
                LongestWiringUnitGcPauseMilliseconds = (GC.GetTotalPauseDuration() - unitGcPause).TotalMilliseconds;
                _longestWiringUnitIndex = _wiringUnitIndex; // report-only: the Nth Wire() yield (1-based) was the slowest
            }

            _wiringMilliseconds += unitMilliseconds;
            if (!more)
            {
                LongestBuildFrameMilliseconds = Math.Max(LongestBuildFrameMilliseconds, System.Diagnostics.Stopwatch.GetElapsedTime(start, ended).TotalMilliseconds);
                Complete();
                return;
            }

            if (ended >= deadline)
            {
                break;
            }
        }

        LongestBuildFrameMilliseconds = Math.Max(LongestBuildFrameMilliseconds, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    /// <summary>The shell's root (0x10000495), or null when the layout lacks it.</summary>
    public UiElement? Root { get; private set; }

    /// <summary>The SmartBox (0x1000049A): the world's frame, filling the screen.</summary>
    public UiElement? SmartBox { get; private set; }

    /// <summary>The SmartBox's viewport (0x10000436).</summary>
    public UiViewport? SmartBoxViewport { get; private set; }

    /// <summary>The spew box window (<see cref="SpewBoxId"/>).</summary>
    public UiElement? SpewBox { get; private set; }

    /// <summary>The spew box, the chat windows and the session's lines (g2), or null when the layout lacks the shell's root.</summary>
    public GameChat? Chat { get; private set; }

    /// <summary>The radar window (0x100006D2, <c>m_pSmartBoxRadar</c>).</summary>
    public UiElement? Radar { get; private set; }

    /// <summary>The stacked vitals window (0x100005FA, <c>m_pStackedVitals</c>).</summary>
    public UiElement? StackedVitals { get; private set; }

    /// <summary>The side-by-side vitals window (0x100006D5, <c>m_pSideBySideVitals</c>); shown instead of the stacked ones when the character's options say so (<see cref="Vitals"/>).</summary>
    public UiElement? SideBySideVitals { get; private set; }

    /// <summary>The main chat window (<see cref="MainChatId"/>).</summary>
    public UiElement? MainChat { get; private set; }

    /// <summary>The four floating chat windows (0x10000505, 0x1000050E-0x10000510), hidden until toggled; those the layout has.</summary>
    public IReadOnlyList<UiElement> FloatingChats { get; private set; } = [];

    /// <summary>The toolbar window (<see cref="ToolbarId"/>).</summary>
    public UiElement? Toolbar { get; private set; }

    /// <summary>The panel host window (<see cref="PanelId"/>).</summary>
    public UiElement? Panel { get; private set; }

    /// <summary>The indicators window (<see cref="GameIndicators.WindowId"/>).</summary>
    public UiElement? Indicators { get; private set; }

    /// <summary>The panel host (g4): its pages, hidden at login; null when the layout has no root.</summary>
    public GamePanelHost? PanelHost { get; private set; }

    /// <summary>The panel slices' controllers (M3 2.2): the pages with a controller open as pages, the others answer NotInM3; null when the layout has no root.</summary>
    public GamePanels? PanelControllers { get; private set; }

    /// <summary>The toolbar's buttons (g4); null when the layout has no root.</summary>
    public GameToolbar? ToolbarUi { get; private set; }

    /// <summary>The selection display and the vivid target indicator (g5b); null when the layout has no root.</summary>
    public GameSelection? SelectionUi { get; private set; }

    /// <summary>The indicators (g4): the link status; null when the layout has no root.</summary>
    public GameIndicators? IndicatorsUi { get; private set; }

    /// <summary>The automation inspector (G7 lane B): the action log, the counters and the safety switches, read-only; closed until <see cref="AutomationInspector.Open"/>. Null when the layout has no root.</summary>
    public AutomationInspector? Inspector { get; private set; }

    /// <summary>The automation menu (M6 6 MC, L1): the jobs the autopilot can run now, a placeholder window; closed until <see cref="AutomationMenu.Open"/>. Null when the layout has no root.</summary>
    public AutomationMenu? Menu { get; private set; }

    /// <summary>
    /// The SmartBox's window rectangle in logical UI pixels, or null without a SmartBox: what the host publishes as <c>GameViewRect</c> once scaled
    /// (<c>gmSmartBoxUI::RecvNotice_UpdateGameView</c>, 11.6096 0x004D6F20). The whole window in this shell (D40 as amended).
    /// </summary>
    public UiBox? GameViewBox => SmartBox is { } box ? box.ScreenBox : null;

    /// <summary>True once the host has painted the shell (<see cref="MarkPainted"/>): until then it keeps the world hidden.</summary>
    public bool ShellPainted { get; private set; }

    /// <summary>The frames the host waited for the paint, set by <see cref="MarkPainted"/>.</summary>
    public int PaintFrames { get; private set; }

    /// <summary>The milliseconds the host waited for the paint, set by <see cref="MarkPainted"/>.</summary>
    public double PaintMilliseconds { get; private set; }

    /// <summary>True when the host gave up waiting and showed the world with the shell not fully painted (logged).</summary>
    public bool PaintTimedOut { get; private set; }

    /// <summary>The windows that are still placeholders.</summary>
    public IReadOnlyList<string> Placeholders => _placeholders;

    /// <inheritdoc/>
    public string? PlaceholderNote => _placeholders.Count == 0 ? null : $"placeholder: the in-game shell's {PlaceholderNoteText(_placeholders)} have no content yet (M2 G4); they show retail's art only";

    /// <inheritdoc/>
    public event Action? PlaceholderNoteChanged;

    /// <summary>The host painted the shell: <paramref name="frames"/> frames (<paramref name="milliseconds"/> ms) after it was made; <paramref name="timedOut"/> when it stopped waiting.</summary>
    public void MarkPainted(int frames, double milliseconds, bool timedOut)
    {
        if (ShellPainted)
        {
            return;
        }

        ShellPainted = true;
        PaintFrames = frames;
        PaintMilliseconds = milliseconds;
        PaintTimedOut = timedOut;
        if (timedOut)
        {
            _context.Manager.Services.Log.Warn(UiElementManager.Category, $"TODO(g4-shell-paint): the shell was not fully painted after {frames} frames ({milliseconds:0} ms); the world is shown with it anyway");
        }
        else
        {
            _context.Manager.Services.Log.Info(UiElementManager.Category, $"the in-game shell is painted ({frames} frames, {milliseconds:0} ms after it was made); the world shows behind it");
        }
    }

    /// <summary>A slice gave <paramref name="window"/> (a name of <see cref="InitialPlaceholders"/>) its content: it leaves the placeholder note.</summary>
    public void MarkWindowBuilt(string window)
    {
        if (_placeholders.Remove(window))
        {
            PlaceholderNoteChanged?.Invoke();
        }
    }

    /// <inheritdoc/>
    public void DisplayError(in UiStringRef text) =>
        _context.Manager.LogOnce("gameplay:error", LogSeverity.Info, $"the game screen was queued with an error ({text}) but shows none");

    /// <inheritdoc/>
    public void Update()
    {
        if (!IsBuilt && !_disposed)
        {
            StepBuild();
        }
        else
        {
            WatchPostWindowGc();
        }

        Chat?.Update();
        TryLoadLayout();
        WatchLayoutSize();
        RadarUi?.Update();
        SelectionUi?.Update();
        ToolsUi?.Update();
        PanelControllers?.Step(_context.Manager.Now);
    }

    /// <summary>Lifestone's tools panel (G7 lane A), toggled by F7; null when the layout has no root.</summary>
    public ToolsPanel? ToolsUi { get; private set; }

    /// <summary>The keys that open Lifestone's panels (F7 and Scroll Lock, D41); null when the layout has no root. The inspector's callback is wired by the host.</summary>
    public LifestoneHotkeys? Hotkeys { get; private set; }

    /// <summary>The radar (g3b): its range, compass tokens and blips; null when the layout has no root.</summary>
    public GameRadar? RadarUi { get; private set; }

    /// <summary>The character's options for the session (g6; D42); null when the layout has no root.</summary>
    public GameOptions? Options { get; private set; }

    /// <summary>The logout confirmation (g6).</summary>
    public GameLogout? Logout { get; private set; }

    /// <summary>The server's questions as dialogs (M5 6 u1); null when the layout has no root.</summary>
    public ServerConfirmation? Confirmations { get; private set; }

    /// <summary>The window layout's save and restore (M5 6 u1, D33); null until the shell is wired.</summary>
    public ScreenLayoutKeeper? Layout { get; private set; }

    /// <summary>The name of the layout file this character's windows are kept in at this window size (retail's <c>UI-world-character-w-h.txt</c>), or null until the world and character names are known.</summary>
    public string? LayoutFileName { get; private set; }

    private string _layoutWorld = "world";
    private string _layoutCharacter = string.Empty;
    private (int Width, int Height) _layoutSize;

    /// <summary>What the last read of a layout file found and did (the world entry's, or a size change's); the default value until one was made (M5 6 fix, 6-R6: the keep check reads it).</summary>
    public ScreenLayoutLoad LastLayoutLoad { get; private set; }

    /// <summary>How many layout files this screen read (one at the world entry, one more at each size change).</summary>
    public int LayoutReads { get; private set; }

    /// <summary>True once the saved layout was read and applied (or found to be missing) at the world entry.</summary>
    public bool LayoutLoaded { get; private set; }

    /// <summary>The read-only character information page (g6).</summary>
    public GameCharacterInfo? CharacterInfo { get; private set; }

    /// <summary>The options panel (g6).</summary>
    public GameOptionsPanel? OptionsPanel { get; private set; }

    /// <summary>The vitals (g3a): the stacked and side-by-side windows and the choice between them; null when the layout has no root.</summary>
    public GameVitals? Vitals { get; private set; }

    /// <inheritdoc/>
    public void Show(bool visible)
    {
        _showRequested = visible; // a staged root that is not made yet takes it when it is
        Root?.SetVisible(visible);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        EndNoGcWindow();
        _subscription.Dispose();
        _logOffSubscription.Dispose();
        if (_staged is { Done: false } half)
        {
            // Left before the shell was made (a log off in its first frames): finish the tree so it can be deleted whole (one frame, rare), rather than leave half of it in the runtime.
            half.Step(long.MaxValue);
            Root = half.Root;
        }

        if (_context.Notices is { } notices)
        {
            notices.ExpectWindows = false;
        }

        SaveLayout("the world exit"); // before anything is taken down: the windows are where the player left them
        _context.Manager.DisplayChanging -= OnDisplayChanging;
        Confirmations?.Dispose();
        Options?.Dispose();
        Chat?.Dispose();
        Vitals?.Dispose();
        SelectionUi?.Dispose();
        CharacterInfo?.Dispose();
        OptionsPanel?.Dispose();
        Hotkeys?.Detach(_context.Manager.Input);
        ToolsUi?.Dispose();
        Inspector?.Dispose();
        Menu?.Dispose();
        PanelControllers?.Dispose(); // the panels unsubscribe from the session, which outlives the screen (review 2.2-R2)
        IndicatorsUi?.Dispose();
        if (Root is not null)
        {
            _context.Manager.DeleteElement(Root); // gmGamePlayUI::~gmGamePlayUI (11.6096 0x004EAF30) forces the display again; the next screen does
        }
    }

    // M5 6 u1 (D33): the window layout. Read once at the world entry, as soon as the character's name is known (the file is named by world, character and window size), over the layout's own
    // places; written at log-off and at the world exit. Nothing is written before the read, so a shell that is left in its first frames never replaces a saved layout with the defaults.
    private void TryLoadLayout()
    {
        if (LayoutLoaded || _disposed || !IsBuilt || Root is null)
        {
            return;
        }

        var manager = _context.Manager;
        var state = _context.Session.State;
        var selected = state.Characters.Entries.FirstOrDefault(e => e.Guid == state.Characters.SelectedGuid).Name; // the slot the player entered with: known before the world's description of the body
        if ((state.Self.Name ?? selected) is not { Length: > 0 } character)
        {
            return;
        }

        LayoutLoaded = true;
        var store = manager.Services.ScreenLayouts;
        if (store is null)
        {
            manager.LogOnce(
                "gameplay:layout-store",
                LogSeverity.Warning,
                "TODO(g6-layout-store): the host gave the shell no layout store; the windows' places last for the session only");
            return;
        }

        _layoutWorld = state.WorldName ?? "world";
        _layoutCharacter = character;
        _layoutSize = manager.DisplaySize;
        LayoutFileName = LayoutNameAt(_layoutSize);
        Layout = new ScreenLayoutKeeper(store, manager.Services.Log);
        LastLayoutLoad = Layout.Load(LayoutFileName, Root, ChatMasks());
        LayoutReads++;
        manager.DisplayChanging += OnDisplayChanging;
    }

    // The windows are still where the player left them at the old size (the root is resized after this): write them under the old size's name.
    private void OnDisplayChanging((int Width, int Height) oldSize)
    {
        if (!LayoutLoaded || Layout is null || Root is null || _disposed || oldSize != _layoutSize)
        {
            return;
        }

        var oldName = LayoutNameAt(oldSize);
        if (Layout.Save(oldName, Root, ChatMasks()))
        {
            _context.Manager.Services.Log.Info(UiElementManager.Category, $"the window layout was written at a size change, under {oldName}");
        }
    }

    private string LayoutNameAt((int Width, int Height) size) => ScreenLayoutFile.Name(_layoutWorld, _layoutCharacter, size.Width, size.Height);

    /// <summary>
    /// The window changed size (6-R3). Retail names the file from the game window's size when it saves, and reloads "#auto" for the new size when its global message 14 arrives
    /// (<c>gmGamePlayUI::ListenToGlobalMessage</c>, 11.6096 0x004EC230, which calls <c>LoadScreenLayout</c> 0x004EB580; the message's meaning is read from the 11.4186 code, <c>TODO(m5-6-layout-resize-msg14)</c>:
    /// not seen on the wire). So the places the player left at the old size are written under the old size's name when the display changes, before the root is resized (<c>OnDisplayChanging</c>; Lifestone's addition, so they are not lost), and the new size's file is read.
    /// A file that is missing leaves the windows where they are.
    /// </summary>
    private void WatchLayoutSize()
    {
        if (!LayoutLoaded || Layout is null || Root is null || _disposed)
        {
            return;
        }

        var size = _context.Manager.DisplaySize;
        if (size == _layoutSize)
        {
            return;
        }

        _layoutSize = size; // the old size's file was written by OnDisplayChanging, before the root was resized
        LayoutFileName = LayoutNameAt(size);
        LastLayoutLoad = Layout.Load(LayoutFileName, Root, ChatMasks());
        LayoutReads++;
    }

    private void SaveLayout(string reason)
    {
        if (!LayoutLoaded || Layout is null || LayoutFileName is null || Root is null)
        {
            return;
        }

        // 6-R3: the name is that of the size at the time of the save, not the size at the world entry.
        var name = LayoutNameAt(_context.Manager.DisplaySize);
        LayoutFileName = name;
        if (Layout.Save(name, Root, ChatMasks()))
        {
            _context.Manager.Services.Log.Info(UiElementManager.Category, $"the window layout was written at {reason}");
        }
    }

    private List<ChatMaskBinding> ChatMasks() =>
        Chat is null ? [] : [.. Chat.Windows.Select(static w => new ChatMaskBinding(w.Window.ElementId, () => w.TextTypeMask, mask => w.TextTypeMask = mask))];

    private void OnLogOffRequested(in LogOffRequested e) => SaveLayout("log off");

    // The server sends the character list again when a logoff completes (F658): the screen returns to character management then. A lost
    // connection is the screen host's (the disconnected screen), and a quit ends the application.
    private void OnCharacterListChanged(in CharacterListChanged e)
    {
        if (!_disposed)
        {
            _context.Flow.QueueUIMode(UiModes.CharacterManagement);
        }
    }

    private T? Find<T>(uint elementId)
        where T : UiElement
    {
        var found = Root?.GetChildRecursive(elementId);
        if (found is T typed)
        {
            return typed;
        }

        _context.Manager.LogOnce(
            $"gameplay:{elementId:X8}",
            LogSeverity.Error,
            found is null
                ? $"gmGamePlayUI: element 0x{elementId:X8} is missing from the layout; that window is not found"
                : $"gmGamePlayUI: element 0x{elementId:X8} is a {found.GetType().Name}, not the {typeof(T).Name} the shell expects; that window is not found");
        return null;
    }

    private static string PlaceholderNoteText(List<string> windows) => string.Join(", ", windows);
}

using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Chat;
using Lifestone.Client.RetailUi.Core;
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
        if (context.StageShell)
        {
            // The root is made over frames (UiStagedRoot, the same tree and order as CreateRootElement), then the windows are wired a controller at a time: the build is the part of the arrival that
            // cost 21 to 29 ms in one frame, and the live gate's 50 ms rule failed on the machine's luck (docs/session.md section 27). The portal notice waits for the spew box (UiNoticeRouter.ExpectWindows).
            _staged = manager.BeginRootElement(UiLayoutEnums.GameplayRoot.LayoutEnum, UiLayoutEnums.GameplayRoot.ElementId);
            if (_staged is not null)
            {
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
            "TODO(g4-window-layout-save): the floating windows' places, sizes and locks last for the session only (D56); retail kept them in the character's options on the server (PlayerModule chat-window options, restored by each floaty window's UpdateFromPlayerModule) and in a layout file UI-<world>-<character>-<width>-<height>.txt (gmGamePlayUI::SaveScreenLayout 11.6096 0x004EB9E0, LoadScreenLayout 0x004EB580)");
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

        Options = new GameOptions(context.Session, manager.Services.Log); // g6: the session-local option changes live in the session (R8), so no subscription order matters
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
        CharacterInfo = new GameCharacterInfo(context, PanelHost);
        OptionsPanel = new GameOptionsPanel(context, PanelHost, Options, Logout);
        yield return true;

        ToolbarUi = new GameToolbar(context, Toolbar);
        if (Toolbar is not null)
        {
            MarkWindowBuilt("toolbar");
        }

        yield return true;

        // g5b: the toolbar's selection display (name, health) and the vivid target indicator (docs/retail-ui.md section 21.7).
        SelectionUi = new GameSelection(context, Toolbar, SmartBox, Options);
        ToolbarUi.SelectedSource = () => SelectionUi.Selected;
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
        Hotkeys = new LifestoneHotkeys(manager.Services.Log) { OpenTools = ToolsUi.Toggle, OpenInspector = Inspector.Toggle };
        Hotkeys.Attach(manager.Input);
    }

    // The root is made (staged or not) and every window wired.
    private void Complete()
    {
        IsBuilt = true;
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
                : $"the in-game shell was built over {BuildFrames} frames (budget {BuildBudgetMilliseconds:0.0} ms a frame): root {rootMilliseconds:0.0} ms, wiring {wiringMilliseconds:0.0} ms, longest frame {LongestBuildFrameMilliseconds:0.0} ms, longest unit {LongestBuildUnitMilliseconds:0.0} ms (staged unit ended in '{_staged?.LongestUnitPhase}'; wiring unit #{_longestWiringUnitIndex} took {_longestWiringUnit:0.0} ms; slowest controller {PanelControllers?.LongestControllerName} {PanelControllers?.LongestControllerMilliseconds:0.0} ms), {System.Diagnostics.Stopwatch.GetElapsedTime(_buildStart).TotalMilliseconds:0} ms from the start");
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
            var more = _wiring.MoveNext();
            var ended = System.Diagnostics.Stopwatch.GetTimestamp();
            var unitMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(unit, ended).TotalMilliseconds;
            _wiringUnitIndex++;
            if (unitMilliseconds > _longestWiringUnit)
            {
                _longestWiringUnit = unitMilliseconds;
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

        Chat?.Update();
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
        _subscription.Dispose();
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

        Chat?.Dispose();
        Vitals?.Dispose();
        SelectionUi?.Dispose();
        CharacterInfo?.Dispose();
        OptionsPanel?.Dispose();
        Hotkeys?.Detach(_context.Manager.Input);
        ToolsUi?.Dispose();
        Inspector?.Dispose();
        PanelControllers?.Dispose(); // the panels unsubscribe from the session, which outlives the screen (review 2.2-R2)
        IndicatorsUi?.Dispose();
        if (Root is not null)
        {
            _context.Manager.DeleteElement(Root); // gmGamePlayUI::~gmGamePlayUI (11.6096 0x004EAF30) forces the display again; the next screen does
        }
    }

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

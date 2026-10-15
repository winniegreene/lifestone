using System.Diagnostics;
using System.Globalization;
using Godot;
using Lifestone.Client.Audio;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Dat;
using Lifestone.Data.Ui;
using Lifestone.Game.Core;
using Lifestone.Game.Sessions;
using DataUiFonts = Lifestone.Data.Ui.UiFonts;

namespace Lifestone.Game.RetailUi;

/// <summary>
/// The retail user interface in the main scene: a <see cref="CanvasLayer"/> in slot <see cref="LayerSlot"/> that owns the element
/// runtime (<see cref="UiElementManager"/>, retail's <c>UIElementManager</c>), the software compositor, the tile presentation
/// (<see cref="RetailUiTiles"/>), the input feed, the cursor and the viewport host. It draws what the runtime composites and
/// nothing else: layouts, fonts, strings, cursors and art are retail's, with no Godot theme (docs/retail-ui.md §17).
/// </summary>
/// <remarks>
/// <para><b>A frame</b> (process priority <see cref="FrameProcessPriority"/>, after the world, the portal presenter and the sky, so
/// the UI draws the state of the frame it appears in): the runtime ticks (<c>UIElementManager::UseTime</c>), the UI flow runs its
/// screen, the compositor paints dirty surfaces within its own <see cref="CompositeSliceMilliseconds"/> ms slice (round-robin,
/// whole surfaces: a surface that has started always finishes, so the slice only bounds the surfaces after the first and a login
/// screen's one 800x600 root can overrun it; the slice is separate from the main-thread queue's budget and is not subtracted from it,
/// docs/game-core.md §1.2), and the tiles of the surfaces that finished are uploaded (only the dirty ones). An idle UI allocates nothing.</para>
/// <para><b>Scale.</b> Window pixels per UI pixel is an integer: <see cref="UiScalePolicy"/>'s rule (the largest that leaves at
/// least 800×600 UI pixels) or <c>--ui-scale</c>; a resize of the window picks it again. The surfaces are drawn with nearest
/// filtering, the cursor is enlarged to match, and the mouse is divided by it.</para>
/// <para><b>Contract members</b> (m2-plan §4.13): <see cref="Scale"/>, <see cref="LogicalSize"/>, <see cref="GameViewRect"/> (the
/// whole window for now: in retail's default shell the SmartBox fills the screen, D40 as amended), <see cref="KeyboardCaptured"/>,
/// <see cref="WantsWorldHidden"/>, <see cref="RegisterViewportSource"/> and <see cref="SetSelectionProjector"/> (G5's picking, docs/avatars.md §18).</para>
/// </remarks>
public partial class RetailUiLayer : CanvasLayer, IUiLoginHost, ICg3DViewHost
{
    /// <summary>The canvas layer slot of the retail UI (m2-plan W2-pre(b)): above the portal notice line (8) and the tool panels.</summary>
    public const int LayerSlot = 10;

    /// <summary>The process priority of the layer's frame and tile upload (docs/game-core.md §1.2): after the world (10), the portal presenter (15) and the sky (20).</summary>
    public const int FrameProcessPriority = 25;

    /// <summary>The compositor's slice per frame, in milliseconds (docs/game-core.md §1.2).</summary>
    public const double CompositeSliceMilliseconds = 1.0;

    /// <summary>The hide request the layer holds on <see cref="WorldVisibility"/> while a full-screen screen shows.</summary>
    public const string WorldHideOwner = "retail-ui";

    /// <summary>The hit-test masks of the retail layouts (alpha media of layout 0x21000040; docs/retail-ui.md §3), decoded ahead.</summary>
    private static readonly uint[] MaskImages = [0x06004C9D, 0x06004CA2, 0x06004CB1];

    private readonly Stopwatch _frameClock = Stopwatch.StartNew();
    private readonly double[] _compositeWindow = new double[WindowFrames];
    private GameServices? _services;
    private ColorRect _backdrop = null!;
    private Node2D _pictures = null!;
    private Node2D _roots = null!;
    private RetailUiOptions _options = new();
    private double _fixedNow;
    private bool _wantsWorldHidden;
    private int _shellFrames;
    private UiServices? _uiServices;
    private ShellWarmUp? _warm;
    private bool _warmed;
    private long _shellClock;
    private int _windowFrame;
    private bool _exited;
    private RetailUiOfflineSession? _offline;
    private SessionHost? _live;

    /// <summary>Frames the composite maximum is taken over.</summary>
    public const int WindowFrames = 240;

    /// <summary>The installed layer, or null.</summary>
    public static RetailUiLayer? Current { get; private set; }

    /// <summary>The element runtime.</summary>
    public UiElementManager Manager { get; private set; } = null!;

    /// <summary>The software compositor.</summary>
    public UiCompositor Compositor { get; private set; } = null!;

    /// <summary>The tile presentation.</summary>
    public RetailUiTiles Tiles { get; private set; } = null!;

    /// <summary>The viewports' holes.</summary>
    public RetailViewportHost Viewports { get; private set; } = null!;

    /// <summary>The mouse cursor.</summary>
    public RetailCursor Cursor { get; private set; } = null!;

    /// <summary>The input feed.</summary>
    public RetailUiInput Input { get; private set; } = null!;

    /// <summary>The images (worker decode).</summary>
    public GodotUiImageSource Images { get; private set; } = null!;

    /// <summary>The relay to the world's pointer sink (G5) and the text-entry state.</summary>
    public WorldPointerRelay World { get; } = new();

    /// <summary>The screen host (<c>gmUIFlow</c> with the login screens and the session behind them), or null before it is built.</summary>
    public RetailUiScreenHost? Screens { get; private set; }

    /// <summary>The UI flow (<c>gmUIFlow</c>), or null before it is built.</summary>
    public GmUiFlow? Flow => Screens?.Gm;

    /// <summary>The session the screens read and act on: the live session of <c>--play</c> (the session host ticks it), else the offline stand-in.</summary>
    public IGameSessionView? Session => _live?.Session ?? _offline?.Session;

    /// <summary>The options the layer was built with.</summary>
    public RetailUiOptions Options => _options;

    /// <summary>
    /// Window pixels per UI pixel (always a whole number): the contract's name (m2-plan §4.13). It hides <see cref="CanvasLayer.Scale"/>,
    /// the layer's own transform, which stays at 1; the surfaces' nodes carry the UI scale.
    /// </summary>
    public new int Scale => Manager.Scale.Scale;

    /// <summary>
    /// Where an object selection is on the screen (m2-plan §4.13): set by play mode to the world's picking (<see cref="ISelectionProjector"/>), null without a world (the login screens).
    /// The shell's target indicator and the inspector read it; rectangles are in <see cref="LogicalSize"/> pixels.
    /// </summary>
    public ISelectionProjector? SelectionProjector { get; private set; }

    /// <summary>Gives the layer the world's selection projection (play mode, once the world camera exists); null takes it away.</summary>
    public void SetSelectionProjector(ISelectionProjector? projector) => SelectionProjector = projector;

    /// <summary>The window's size in pixels.</summary>
    public Vector2I WindowSize => new(Manager.Scale.WindowWidth, Manager.Scale.WindowHeight);

    /// <summary>The UI's size in its own pixels: the window divided by <see cref="Scale"/>.</summary>
    public Vector2I LogicalSize => new(Manager.Scale.LogicalWidth, Manager.Scale.LogicalHeight);

    /// <summary>
    /// The rectangle the 3D world is framed in, in window pixels (the SmartBox frame, D40). With the in-game shell up it is the SmartBox's window
    /// rectangle (<c>gmSmartBoxUI::RecvNotice_UpdateGameView</c>, 11.4186 0x004D62C0, 11.6096 0x004D6F20), else the whole window. Retail's default shell lets the SmartBox
    /// fill the screen (m2 §7, decision 11), so both are the whole window and no offset is applied to the world's projection; a SmartBox that did not
    /// fill it would move the rectangle, and the hook feeds <c>WorldLens</c>. An edge of the SmartBox on the UI's edge is the window's edge (the
    /// window is not always a whole number of UI pixels).
    /// </summary>
    public Rect2I GameViewRect
    {
        get
        {
            var window = WindowSize;
            if (Screens?.Flow.CurrentScreen is not GamePlayScreen { GameViewBox: { } box })
            {
                return new Rect2I(Vector2I.Zero, window);
            }

            var logical = LogicalSize;
            var x0 = box.X0 <= 0 ? 0 : box.X0 * Scale;
            var y0 = box.Y0 <= 0 ? 0 : box.Y0 * Scale;
            var x1 = box.X1 >= logical.X - 1 ? window.X : Math.Min(window.X, (box.X1 + 1) * Scale);
            var y1 = box.Y1 >= logical.Y - 1 ? window.Y : Math.Min(window.Y, (box.Y1 + 1) * Scale);
            return new Rect2I(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
        }
    }

    /// <summary>
    /// True while the in-game shell is up and not yet painted (<see cref="GamePlayScreen.ShellPainted"/>): the layer holds the world hidden behind its
    /// backdrop, so no frame shows the world without the shell (docs/retail-ui.md section 21).
    /// </summary>
    public bool ShellHoldsWorld => Screens?.Flow.CurrentScreen is GamePlayScreen { ShellPainted: false };

    /// <summary>True while the shell's warm-up is under way (built or painting on the side; <see cref="ShellWarmUp"/>).</summary>
    public bool ShellWarmUpRunning => _warm is not null;

    /// <summary>The composite slice, in milliseconds, while the shell is painted and the world held hidden (a frame still under the 50 ms limit).</summary>
    public const double ShellPaintSliceMilliseconds = 3.0;

    /// <summary>The most a frame spends uploading tiles while the shell is painted and the world held hidden (the whole screen's first upload was 12 ms in one frame, the one that builds the shell).</summary>
    public const double ShellCopySliceMilliseconds = 3.0;

    /// <summary>The longest the shell is waited for, in frames; the world then shows with it, loudly.</summary>
    public const int ShellPaintFrameLimit = 800;

    /// <summary>True while a text field has the keyboard: movement keys must not reach the world (retail's text mode).</summary>
    public bool KeyboardCaptured => World.TextEntryActive;

    /// <summary>
    /// True while a full-screen screen is up (the display is forced to the login screens' 800×600 and a screen shows): the world
    /// is hidden behind a black backdrop and the layer holds a hide request on <see cref="WorldVisibility"/>.
    /// </summary>
    public bool WantsWorldHidden => _wantsWorldHidden;

    /// <summary>
    /// Asked every frame while the shell holds the world (<see cref="ShellHoldsWorld"/>): true when portal space's tunnel is showing, so the black backdrop gives way and the
    /// tunnel (its own layer, below this one, covering the whole window) shows from its first frame while the shell is built and painted over it (retail shows the tunnel at once;
    /// the world behind it is hidden by portal space and by <see cref="WantsWorldHidden"/>, so no bare world shows). Set by <c>Main</c>.
    /// </summary>
    public Func<bool>? TunnelShowing { get; set; }

    /// <summary>True while the black backdrop is drawn (the world is held hidden and no tunnel shows over it).</summary>
    public bool BackdropShown => _backdrop.Visible;

    /// <summary>Raised when <see cref="KeyboardCaptured"/> changes (the explore camera stops flying while a text field is typed in).</summary>
    public event Action<bool>? KeyboardCapturedChanged;

    /// <summary>The slowest composite of the last <see cref="WindowFrames"/> frames, in milliseconds.</summary>
    public double MaxCompositeMilliseconds { get; private set; }

    /// <summary>The last frame's composite time in milliseconds.</summary>
    public double LastCompositeMilliseconds { get; private set; }

    /// <summary>The last tile copy's time in milliseconds.</summary>
    public double LastCopyMilliseconds { get; private set; }

    /// <summary>Tiles the last copy uploaded.</summary>
    public long LastCopyTiles { get; private set; }

    /// <summary>Frames stepped so far.</summary>
    public long Frames { get; private set; }

    /// <summary>
    /// Builds the layer over the user's dats: services (retail layouts, fonts, strings, the worker-decoded images, the sound
    /// player and clipboard adapters, the viewport host), the runtime at the scale <paramref name="windowSize"/> gives, the key
    /// bindings and the UI flow with the stand-in screens. Nothing is added to the tree.
    /// </summary>
    /// <param name="services">The game services (dats, textures, workers, the queue, the world arbiter).</param>
    /// <param name="options">The parsed options.</param>
    /// <param name="windowSize">The window's size in pixels now.</param>
    /// <param name="sound">Finds the sound player when a sound plays (the audio service is installed later), or null for silence.</param>
    /// <param name="fixedStep">When above 0 the runtime's clock advances by this many seconds per frame, not by real time (scripted captures).</param>
    /// <param name="host">The session host of a <c>--play</c> run, whose live session the screens read and act on (no stand-in is made), or null for the offline stand-in.</param>
    public static RetailUiLayer Create(GameServices services, RetailUiOptions options, Vector2I windowSize, Func<ISoundSink?>? sound, double fixedStep = 0.0, SessionHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var dats = services.Dats ?? throw new InvalidOperationException(services.StartupError ?? "the retail dats are not available");
        var layer = new RetailUiLayer { Name = "RetailUi", Layer = LayerSlot, ProcessPriority = FrameProcessPriority };
        layer._services = services;
        layer._options = options;
        layer._fixedNow = fixedStep > 0 ? 0.0 : double.NaN;
        layer.FixedStep = fixedStep;

        // The backdrop covers the world while a full-screen screen shows, and it stops the mouse, so nothing under it (the explore tool panels, the camera) is clicked through it.
        layer._backdrop = new ColorRect { Name = "Backdrop", Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        layer._backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer._pictures = new Node2D { Name = "ViewportPictures" };
        layer._roots = new Node2D { Name = "Surfaces" };
        layer.AddChild(layer._backdrop);
        layer.AddChild(layer._pictures);
        layer.AddChild(layer._roots);
        layer.Tiles = new RetailUiTiles(layer._roots);
        layer.Viewports = new RetailViewportHost(layer._pictures, layer.Tiles);

        var log = GodotLogSink.Instance;
        var decoder = new DatUiImageSource(services.Textures, log);
        layer.Images = new GodotUiImageSource(decoder, services.Workers, log);
        var layouts = new UiLayouts(dats) { ErrorLog = message => Log.Error("retailui", message) };
        // Lifestone's own layouts (the tools panel, the inspector) are read and registered on a worker now, so the first F7 does not pay the 20 ms on a frame (docs/session.md section 27).
        _ = services.Workers.RunSync(_ =>
        {
            var clock = Stopwatch.StartNew();
            var read = layouts.WarmEmbeddedLayouts();
            Log.Info("retailui", "Lifestone's embedded layouts registered in the background", ("ok", read), ("ms", clock.Elapsed.TotalMilliseconds));
            return read;
        });
        var uiServices = new UiServices
        {
            Descs = UiDescSource.From(layouts),
            Log = log,
            Random = new UiSeededRandom(services.Options.IsAutomated ? 1 : (int)(Stopwatch.GetTimestamp() & 0x7FFFFFFF)),
            Sound = new UiSoundAdapter(sound ?? (() => null), log),
            Fonts = new UiFontsSource(new DataUiFonts(dats), services.Textures, log),
            Strings = new UiStringsSource(new UiStrings(new UiStringTables(dats), message => Log.Warn("retailui", message)), UiLanguageInfo.Load(dats)),
            Clipboard = new DisplayServerClipboard(),
            Images = layer.Images,
            EnumMaps = dats.EnumMaps,
            RetailGlitches = options.Glitches,
            ViewportHost = layer.Viewports,
        };
        var scale = UiScalePolicy.Choose(Math.Max(1, windowSize.X), Math.Max(1, windowSize.Y), options.ScaleOverride);
        layer._uiServices = uiServices;
        layer.Manager = new UiElementManager(uiServices, scale);
        // Retail's bindings with Lifestone's F7 and Scroll Lock keys added here, at start-up: LifestoneHotkeys.Attach adds them when they are missing, and that rebuilds the whole table (1 ms),
        // which the frame that builds the in-game shell must not pay (docs/session.md section 27).
        layer.Manager.Input.Bindings = RetailKeyBindings.Load(dats).With(Lifestone.Client.RetailUi.LifestonePanels.LifestoneHotkeys.Bindings);
        layer.Manager.Input.WorldPointer = layer.World;
        layer.Manager.Input.RetailFocusLoss = (options.Glitches & UiRetailGlitches.FocusLossActions) != 0;
        layer.Compositor = new UiCompositor(layer.Images, log);
        layer.Compositor.Attach(layer.Manager);
        layer.Cursor = new RetailCursor(layer.Images);
        layer.Input = RetailUiInput.Create(layer.Manager.Input, () => layer.Scale);
        layer.AddChild(layer.Input);
        layer.Tiles.SetScale(scale.Scale);
        layer.Viewports.SetScale(scale.Scale);
        layer.Images.Prefetch(MaskImages);
        layer.World.TextEntryChanged += captured => layer.KeyboardCapturedChanged?.Invoke(captured);

        // Play mode (--play): the screens run on the live session the session host ticks. Otherwise they run on the offline stand-in session, whose
        // clock follows the frames in a scripted run (made by SessionHost.CreateStandIn, the only place a session is created).
        var cursors = new UiCursors(dats);
        GameSession session;
        if (host?.Session is { } live)
        {
            layer._live = host;
            session = live;
            if (options.StartMode is not null)
            {
                Log.Warn("retailui", "--retail-ui-start is ignored in play mode: the login flow starts at the data check, as retail's does");
            }
        }
        else
        {
            layer._offline = SessionHost.CreateStandIn(options.StartMode, scripted: fixedStep > 0);
            session = layer._offline.Session;
        }

        layer.Screens = new RetailUiScreenHost(
            layer.Manager,
            session,
            layer,
            cursors.Default,
            options.RetailDisconnect,
            layer._live is null ? options.StartMode : null,
            LoadCharGen(dats, services, layer),
            liveGame: layer._live is not null,
            stageShell: layer._live is not null);

        Log.Info(
            "retailui",
            "retail ui built",
            ("scale", scale.ToString()),
            ("override", options.ScaleOverride?.ToString(CultureInfo.InvariantCulture) ?? "none"),
            ("start", options.StartMode is { } m ? UiModes.NameOf(m) : "default"),
            ("glitches", options.Glitches),
            ("fixed_step", fixedStep));
        return layer;
    }

    /// <summary>
    /// What character creation takes from the application: the CharGen data of the dats and its dice (a fixed seed in a scripted run, so a frame can be
    /// taken again). A failure to read the data is an error line and null, which leaves the mode on its stand-in.
    /// </summary>
    private static CharGenSetup? LoadCharGen(DatSet dats, GameServices services, ICg3DViewHost viewHost)
    {
        var automated = services.Options.IsAutomated;
        try
        {
            var catalog = CharGenCatalog.Load(dats);
            Cg3DViewData? view3D = null;
            try
            {
                view3D = Cg3DViewData.FromDats(dats, services.Textures, services.GfxObjMeshes);
            }
            catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException)
            {
                Log.Error("retailui", "the data of character creation's 3D character could not be read; the appearance and summary pages show no character (placeholder)", ("problem", ex.Message), ("type", ex.GetType().Name));
            }

            return new CharGenSetup(catalog, () => new CharGenRandom(automated ? 1 : (int)(Stopwatch.GetTimestamp() & 0x7FFFFFFF)), View3D: view3D, ViewHost: view3D is null ? null : viewHost);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("retailui", "the CharGen data could not be read; character creation shows its stand-in (placeholder)", ("problem", ex.Message), ("type", ex.GetType().Name));
            return null;
        }
    }

    /// <summary>The application closes (the epilogue, <c>Device::Done</c>): the window's tree quits.</summary>
    void IUiLoginHost.Exit()
    {
        Log.Info("retailui", "the epilogue closes the application");
        GetTree().Quit();
    }

    /// <summary>The disconnected screen's button (D43): the live session logs in again (the session host); the offline session starts again.</summary>
    bool IUiLoginHost.Reconnect()
    {
        if (_live is not null)
        {
            return _live.Reconnect();
        }

        if (_offline is null)
        {
            return false;
        }

        _offline.Reconnect();
        return true;
    }

    /// <summary>
    /// Draws character creation's character (<paramref name="model"/>) in viewport element <paramref name="viewportElementId"/>'s hole: a
    /// <see cref="CharGenViewportBinder"/> is made under the layer and registered as the hole's source; disposing the result takes both away.
    /// </summary>
    IDisposable ICg3DViewHost.Attach(uint viewportElementId, Cg3DViewModel model)
    {
        var services = _services ?? throw new InvalidOperationException("the retail UI layer has no services");
        var binder = new CharGenViewportBinder(services, model, $"0x{viewportElementId:X8}");
        AddChild(binder);
        RegisterViewportSource(viewportElementId, binder);
        return new ViewAttachment(this, viewportElementId, binder);
    }

    private sealed class ViewAttachment(RetailUiLayer layer, uint viewportElementId, CharGenViewportBinder binder) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (GodotObject.IsInstanceValid(layer))
            {
                layer.UnregisterViewportSource(viewportElementId, binder);
            }

            if (GodotObject.IsInstanceValid(binder))
            {
                binder.QueueFree();
            }
        }
    }

    /// <summary>The seconds the runtime's clock advances per frame when it is scripted (0: real time).</summary>
    public double FixedStep { get; private set; }

    /// <summary>
    /// Shows viewport element <paramref name="viewportElementId"/>'s hole as <paramref name="source"/>'s picture (the portal tunnel
    /// in the game screen's viewport 0x10000436, character creation's character): the picture is drawn behind the UI surfaces, in
    /// the hole.
    /// </summary>
    public void RegisterViewportSource(uint viewportElementId, IUiViewportSource source) => Viewports.RegisterSource(viewportElementId, source);

    /// <summary>Takes <paramref name="source"/> away from viewport <paramref name="viewportElementId"/>.</summary>
    public void UnregisterViewportSource(uint viewportElementId, IUiViewportSource source) => Viewports.UnregisterSource(viewportElementId, source);

    /// <summary>
    /// Runs the frame work with the runtime's clock at <paramref name="now"/> seconds: ticks, the flow's screen, the composite
    /// within its slice, the tile upload, the cursor, the viewport pictures and the world arbiter. <see cref="_Process"/> calls it;
    /// scripted runs and checks may call it themselves.
    /// </summary>
    public void Step(double now)
    {
        var stepStart = Stopwatch.GetTimestamp();
        _offline?.Step(now);
        Manager.Update(now);
        var afterUpdate = Stopwatch.GetTimestamp();
        Screens?.Update();
        var afterScreens = Stopwatch.GetTimestamp();
        StepShellWarmUp(now);
        var start = Stopwatch.GetTimestamp();
        // While the in-game shell is made and not yet painted the world is hidden anyway, so the paint gets a larger slice than the usual 1 ms (the shell is a screen of many windows) but a bounded one.
        // The frame that builds the shell does not also paint it or upload it (the build is the heavy part: docs/session.md section 27); the paint starts in the next frame.
        // A staged shell is not painted while its windows are being wired (the half-filled shell would be painted twice) and not in the frame that completes it; before its root exists the frames paint as usual,
        // so the previous screen is erased at once.
        var builtThisFrame = ShellHoldsWorld && _shellFrames == 0 && Screens?.Flow.CurrentScreen is GamePlayScreen { Root: not null };
        var slice = ShellHoldsWorld ? (builtThisFrame ? 0.0 : ShellPaintSliceMilliseconds) : CompositeSliceMilliseconds;
        if (!builtThisFrame)
        {
            Compositor.Composite(start + (long)(slice * Stopwatch.Frequency / 1000.0));
        }

        LastCompositeMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _compositeWindow[_windowFrame] = LastCompositeMilliseconds;
        _windowFrame = (_windowFrame + 1) % WindowFrames;
        if (_windowFrame == 0 || LastCompositeMilliseconds > MaxCompositeMilliseconds)
        {
            MaxCompositeMilliseconds = _compositeWindow.Max();
        }

        Frames++;
        var afterCompositeMark = Stopwatch.GetTimestamp();
        if (Compositor.HasChangesToCopy && !builtThisFrame)
        {
            CopyTiles();
        }

        var afterCopy = Stopwatch.GetTimestamp();
        Cursor.Update(Manager.Cursor, Scale);
        Viewports.Refresh();
        TrackShellPaint();
        FollowWorld();
        var total = Stopwatch.GetElapsedTime(stepStart).TotalMilliseconds;
        WorstStepMilliseconds = Math.Max(WorstStepMilliseconds, total);
        if (total > SlowStepMilliseconds)
        {
            // What a slow retail UI frame held (the live gate's 50 ms rule names the frame, this names the part): the runtime tick and the flow's screen change (the shell's construction is in
            // the screens' part), the shell warm-up, the paint within its slice and the tile upload.
            Log.Info(
                "retailui",
                "a slow retail ui step",
                ("ms", total),
                ("update", Stopwatch.GetElapsedTime(stepStart, afterUpdate).TotalMilliseconds),
                ("screens", Stopwatch.GetElapsedTime(afterUpdate, afterScreens).TotalMilliseconds),
                ("warm_up", Stopwatch.GetElapsedTime(afterScreens, start).TotalMilliseconds),
                ("composite", LastCompositeMilliseconds),
                ("copy", Stopwatch.GetElapsedTime(afterCompositeMark, afterCopy).TotalMilliseconds),
                ("warm_up_running", _warm is not null),
                ("mode", Screens?.Flow.CurrentMode));
        }
    }

    /// <summary>The longest <see cref="Step"/> since the last <see cref="ResetWorstStep"/>, in milliseconds (the checks judge the steps after Enter by it).</summary>
    public double WorstStepMilliseconds { get; private set; }

    /// <summary>Starts <see cref="WorstStepMilliseconds"/> again from zero.</summary>
    public void ResetWorstStep() => WorstStepMilliseconds = 0.0;

    /// <summary>A retail UI step longer than this is logged with its parts.</summary>
    public const double SlowStepMilliseconds = 8.0;

    /// <summary>
    /// Paints everything dirty and uploads it now, outside the 1 ms slice (a scripted capture and the checks, which need
    /// the finished picture in this frame). Images still decoding are waited for, up to <paramref name="timeoutSeconds"/>.
    /// </summary>
    /// <returns>True when nothing is left to paint or upload.</returns>
    public bool Flush(double timeoutSeconds = 0.0)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeoutSeconds * Stopwatch.Frequency);
        while (true)
        {
            Compositor.Composite(long.MaxValue);
            if (!Compositor.HasPendingWork || Stopwatch.GetTimestamp() >= deadline)
            {
                break;
            }

            Thread.Sleep(1);
        }

        Compositor.CopyDirtyTiles(Tiles);
        return !Compositor.HasPendingWork;
    }

    /// <summary>The perf overlay's line: scale, window, surfaces, composite and upload figures.</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"retail ui: scale {Scale} (window {WindowSize.X}x{WindowSize.Y}, ui {LogicalSize.X}x{LogicalSize.Y})   surfaces {Compositor.RootCount}   composite {LastCompositeMilliseconds:0.00} ms (max {MaxCompositeMilliseconds:0.00} of {CompositeSliceMilliseconds:0.0} slice)   " +
        $"upload {LastCopyTiles} tiles {LastCopyMilliseconds:0.00} ms (total {Tiles.TilesUploaded})   images pending {Images.PendingCount}   mode {(Flow is { } flow ? UiModes.NameOf(flow.Flow.CurrentMode) : "none")}");

    /// <inheritdoc/>
    public override void _Ready()
    {
        Current = this;
        GetViewport().SizeChanged += OnWindowResized;
        OnWindowResized();
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        double now;
        if (double.IsNaN(_fixedNow))
        {
            now = _frameClock.Elapsed.TotalSeconds;
        }
        else
        {
            now = _fixedNow;
            _fixedNow += FixedStep;
        }

        Step(now);
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        if (_exited)
        {
            return;
        }

        _exited = true;
        if (GetViewport() is { } viewport)
        {
            viewport.SizeChanged -= OnWindowResized;
        }

        Cursor.Reset();
        Screens?.Dispose();
        _offline?.Dispose();
        _warm = null; // dropped, not deleted: the process is ending
        Tiles.Clear();
        _services?.WorldVisibility.Follow(WorldHideOwner, true);
        if (Current == this)
        {
            Current = null;
        }
    }

    private void OnWindowResized()
    {
        var size = GetViewport().GetVisibleRect().Size;
        var window = new Vector2I(Math.Max(1, (int)size.X), Math.Max(1, (int)size.Y));
        if (window == WindowSize)
        {
            return;
        }

        var scale = UiScalePolicy.Choose(window.X, window.Y, _options.ScaleOverride);
        Manager.SetScale(scale);
        Tiles.SetScale(scale.Scale);
        Viewports.SetScale(scale.Scale);
        Log.Info("retailui", "window resized", ("scale", scale.ToString()));
    }

    private void CopyTiles()
    {
        var before = Tiles.TilesUploaded;
        var start = Stopwatch.GetTimestamp();
        Compositor.CopyDirtyTiles(Tiles, ShellHoldsWorld ? start + (long)(ShellCopySliceMilliseconds * Stopwatch.Frequency / 1000.0) : long.MaxValue);
        LastCopyMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        LastCopyTiles = Tiles.TilesUploaded - before;
    }

    // From the first login screen on (the data check, the character list; the "Entering World" dialog is not a mode) the shell is built and painted once on the side, a few milliseconds a
    // frame, so the real one finds its images decoded and its glyphs made (ShellWarmUp, docs/retail-ui.md section 21.10). Once per run; the scale it was warmed at is the scale it is used at
    // (a resize makes the images' decode, not the layout, again). Starting at the data check, not at the list, is what lets it finish before a person (or the login check) presses Enter.
    private void StepShellWarmUp(double now)
    {
        if (_warmed)
        {
            return;
        }

        var loginScreens = Screens is not null && Screens.Flow.CurrentMode is 0 or UiModes.DataPatch or UiModes.CharacterManagement;
        if (_warm is null)
        {
            if (_uiServices is null || _live is null || !loginScreens) // play mode only: the offline stand-in has no world to enter
            {
                return;
            }

            _warm = new ShellWarmUp(_uiServices, Manager.Scale);
            Log.Info("retailui", "the in-game shell warm-up started", ("mode", Screens?.Flow.CurrentMode));
            return;
        }

        if (!loginScreens)
        {
            // The mode moved on (Enter came before it was done: the real shell is being made or painted): no second compositor in those frames. What was warmed stays cached; the half-built
            // tree is dropped, not deleted (a delete is a frame's work), and the garbage collector has it.
            Log.Info("retailui", "the in-game shell warm-up stopped: the login screens are gone", ("frames", _warm.Frames), ("build_frames", _warm.BuildFrames), ("longest_step_ms", _warm.LongestStepMilliseconds), ("longest_unit_ms", _warm.LongestBuildUnitMilliseconds));
            _warm = null;
            _warmed = true;
            return;
        }

        _warm.Step(now);
        if (_warm.Done)
        {
            Log.Info("retailui", "the in-game shell was painted on the side while the login screens showed", ("frames", _warm.Frames), ("build_frames", _warm.BuildFrames), ("ms", _warm.Milliseconds), ("longest_step_ms", _warm.LongestStepMilliseconds), ("longest_unit_ms", _warm.LongestBuildUnitMilliseconds));
            _warm.Finish();
            _warm = null;
            _warmed = true;
        }
    }

    // The shell is made in the frame the mode changes (retail paints it before it draws), and the 1 ms slice paints it over the next frames, its images
    // decoding on the workers. It counts as painted when nothing is left to paint or copy (and its root shows); the world stays hidden until then.
    private void TrackShellPaint()
    {
        if (Screens?.Flow.CurrentScreen is not GamePlayScreen { ShellPainted: false } shell)
        {
            _shellFrames = 0;
            _shellClock = 0;
            return;
        }

        if (!shell.IsBuilt)
        {
            // The staged shell is still being made (a few milliseconds a frame): the paint, and its frame limit, count from the frame it is complete.
            _shellFrames = 0;
            _shellClock = 0;
            return;
        }

        if (_shellFrames == 0)
        {
            _shellClock = Stopwatch.GetTimestamp();
        }

        _shellFrames++;
        var milliseconds = Stopwatch.GetElapsedTime(_shellClock).TotalMilliseconds;
        // Not in the frame that built it: that frame skips the composite, which is also what makes the compositor pick up the new surfaces, so it would find nothing to paint.
        if (_shellFrames >= 2 && shell.Root is { IsShown: true } && !Compositor.HasPendingWork && !Compositor.HasChangesToCopy)
        {
            shell.MarkPainted(_shellFrames, milliseconds, timedOut: false);
        }
        else if (_shellFrames >= ShellPaintFrameLimit)
        {
            shell.MarkPainted(_shellFrames, milliseconds, timedOut: true);
        }
    }

    private void FollowWorld()
    {
        var wants = ShellHoldsWorld;
        if (!wants && Manager.IsDisplayForced)
        {
            var roots = Manager.Roots;
            for (var i = 0; i < roots.Count; i++)
            {
                if (roots[i].IsShown)
                {
                    wants = true;
                    break;
                }
            }
        }

        // The backdrop stands in for the world, so it gives way to the arrival tunnel while the shell is built (the tunnel is the picture; the world stays hidden either way).
        var backdrop = wants && !(ShellHoldsWorld && TunnelShowing?.Invoke() == true);
        if (_backdrop.Visible != backdrop)
        {
            _backdrop.Visible = backdrop;
        }

        if (wants == _wantsWorldHidden)
        {
            return;
        }

        _wantsWorldHidden = wants;
        _services?.WorldVisibility.Follow(WorldHideOwner, !wants);
    }
}

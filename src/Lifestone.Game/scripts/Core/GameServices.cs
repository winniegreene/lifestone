using System.Diagnostics;
using Godot;
using Lifestone.Client.Audio;
using Lifestone.Client.Scheduling;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Textures;
using Lifestone.Data.World.Cells;
using Lifestone.Data.World.Scenery;
using Lifestone.Data.World.Sky;
using Lifestone.Game.Render;
using Lifestone.Game.World;

namespace Lifestone.Game.Core;

/// <summary>
/// Autoload (<c>project.godot [autoload]</c>): the process-wide services every scene and track shares — the opened dats,
/// the shared loaders and caches, a bounded worker pool for decoding, the main-thread work queue drained each frame
/// within a time budget, the render resources and logging.
/// </summary>
/// <remarks>
/// <para>Access through <see cref="Instance"/>. Everything is created in <see cref="_EnterTree"/>, before any scene
/// node's <c>_Ready</c>. When the dats cannot be opened the services stay up without them: <see cref="Dats"/> is null
/// and <see cref="StartupError"/> says why (the main scene shows it and quits with exit code 1).</para>
/// <para><b>Threading.</b> Data-layer loaders are thread-safe (dat reads lock inside <see cref="DatSet"/>); call them
/// from <see cref="Workers"/>. RenderingServer resources and nodes are created only through <see cref="MainThread"/>
/// (or in a layer's Attach steps, which run on it); the one exception is CPU-side Godot <c>Image</c>s, which texture
/// decoding builds on workers (docs/render-contract.md §0). The queue runs before other nodes each frame (process
/// priority -1000).</para>
/// <para>Product code does no network I/O and writes only under user:// (logs) and to the output paths automated runs
/// are given (relative to the repository root, never inside src/ or tests/: Diagnostics/ScreenshotWriter.cs).</para>
/// </remarks>
public partial class GameServices : Node
{
    private static GameServices? _instance;

    /// <summary>The autoload instance.</summary>
    public static GameServices Instance => _instance ?? throw new InvalidOperationException("GameServices autoload is not loaded.");

    public GameOptions Options { get; private set; } = new();

    /// <summary>The user's dats, or null when they could not be opened (see <see cref="StartupError"/>).</summary>
    public DatSet? Dats { get; private set; }

    /// <summary>Why the services are unusable, or null.</summary>
    public string? StartupError { get; private set; }

    public TextureLoader Textures { get; private set; } = null!;

    public GfxObjMeshCache GfxObjMeshes { get; private set; } = null!;

    public SetupCache Setups { get; private set; } = null!;

    public CellStructureLibrary CellStructures { get; private set; } = null!;

    public CellMeshCache CellMeshes { get; private set; } = null!;

    public SceneryGenerator Scenery { get; private set; } = null!;

    public DerethSky Sky => Dats?.Sky ?? throw new InvalidOperationException(StartupError ?? "dats not open");

    public DerethClock Clock { get; } = new();

    public GroundHeights Ground { get; private set; } = null!;

    public RenderResources Render { get; private set; } = null!;

    public WorkerPool Workers { get; private set; } = null!;

    public MainThreadQueue MainThread { get; private set; } = null!;

    /// <summary>
    /// Whether the 3D world is drawn: the sole writer of the main viewport's <c>Disable3D</c>, with reference-counted
    /// hide requests (the portal tunnel, full-screen UI screens). Created with the services; always present.
    /// </summary>
    public WorldVisibility WorldVisibility { get; private set; } = null!;

    /// <summary>
    /// The sound card (16 voices, UI and Effects buses): set once by the portal track's <c>AudioService</c> when it is
    /// installed (<c>PortalSpaceSetup</c>); null until then, in the check suites and in the spell viewer. Consumers play
    /// nothing while it is null (portal space is silent without a sink, as the headless host's is).
    /// </summary>
    public IAudioOutput? Audio { get; set; }

    /// <summary>Main-thread time of the last drain, in milliseconds.</summary>
    public double LastDrainMs { get; private set; }

    /// <summary>
    /// The garbage collector as the runtime reports it: server or workstation, concurrent or not, latency mode. Server
    /// GC cannot be switched on from inside the process; docs/game-core.md §7 says where it comes from.
    /// </summary>
    public static string GcMode =>
        (System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")
        + (AppContext.TryGetSwitch("System.GC.Concurrent", out var concurrent) && !concurrent ? " non-concurrent " : " concurrent ")
        + System.Runtime.GCSettings.LatencyMode;

    public override void _EnterTree()
    {
        _instance = this;
        ProcessPriority = -1000;
        Options = GameOptions.Parse(OS.GetCmdlineUserArgs(), Lifestone.Client.Options.OptionCatalog.Registry, OS.GetCmdlineArgs()); // infra: scene-aware (OptionCatalog.SceneOf)
        Log.MinimumLevel = Options.LogLevel;
        Log.Initialize();
        Log.Info(
            "core",
            "Lifestone starting",
            ("godot", Engine.GetVersionInfo()["string"].AsString()),
            ("dotnet", System.Environment.Version),
            ("gc", GcMode), // wave-3 perf: server GC comes from tools\godot.ps1 or the exported runtimeconfig
            ("user_dir", OS.GetUserDataDir()),
            ("log", Log.FilePath),
            ("scene", Options.Scene),
            ("automated", Options.IsAutomated));
        foreach (var error in Options.Errors)
        {
            Log.Warn("core", "bad argument", ("problem", error));
        }

        WorldVisibility = new WorldVisibility(GetViewport());
        MainThread = new MainThreadQueue(TimeSpan.FromMilliseconds(Options.MainThreadBudgetMs));
        Workers = new WorkerPool(Options.WorkerThreads);

        // --- infra (M1 wave 3): --help prints every registered option and quits (Main runs nothing); relative paths in
        // arguments are taken from the repository root (Diagnostics/ScreenshotWriter.cs); vsync and the frame cap apply
        // to every scene ---
        if (Options.ShowHelp)
        {
            GD.Print(Lifestone.Client.Options.OptionCatalog.HelpText());
            GetTree().Quit(0);
            return;
        }

        Log.Info("core", "working directory", ("path", Diagnostics.ScreenshotWriter.UseRepositoryRootAsWorkingDirectory() ?? System.Environment.CurrentDirectory));
        FramePacing.Apply(Options.VSync, Options.MaxFps); // wave-3 perf: vsync plus a frame cap when vsync is off or mailbox
        OpenDats();
    }

    public override void _Process(double delta)
    {
        var start = Stopwatch.GetTimestamp();
        MainThread.Drain();
        LastDrainMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Log.FlushToGodot();
    }

    public override void _ExitTree()
    {
        Log.Info("core", "shutting down");
        Workers?.Dispose();

        // Items already running may be inside dat reads or Godot Image calls; let them finish (briefly) before the dats
        // close and the engine tears down. Waiting items were cancelled by Dispose.
        var deadline = Stopwatch.GetTimestamp() + (2 * Stopwatch.Frequency);
        while (Workers is { Running: > 0 } && Stopwatch.GetTimestamp() < deadline)
        {
            Thread.Sleep(5);
        }

        if (Workers is { Running: > 0 } busy)
        {
            Log.Warn("core", "worker items still running at shutdown", ("running", busy.Running));
        }

        MainThread?.Scheduler.Clear();
        Dats?.Dispose();
        Dats = null;
        Log.Shutdown();
        if (_instance == this)
        {
            _instance = null;
        }
    }

    private void OpenDats()
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var dats = DatSet.Open(Options.DatDirectory);
            Dats = dats;
            Textures = new TextureLoader(dats) { OverrideFallback = message => Log.Warn("textures", message) };
            GfxObjMeshes = new GfxObjMeshCache(dats);
            Setups = new SetupCache(dats, GfxObjMeshes);
            CellStructures = new CellStructureLibrary(dats);
            CellMeshes = new CellMeshCache(CellStructures);
            Scenery = SceneryGenerator.FromDats(dats);
            Ground = new GroundHeights(dats);
            Render = new RenderResources(Textures, Options.DxtPolicy, Options.TextureBudgetBytes, Options.MeshBudgetBytes, Options.ExactCutouts)
            {
                DetailEnabled = Options.DetailTextures, // render-fidelity fix: retail's detail textures (--detail)
            };
            Log.Info(
                "core",
                "dats open",
                ("dir", dats.Directory),
                ("iterations", dats.Iterations.CacheKey),
                ("highres", dats.HighRes is not null),
                ("local", (object?)dats.Iterations.Local ?? "absent"), // the language dat's iteration; the retail UI needs it
                ("ms", timer.Elapsed.TotalMilliseconds),
                ("workers", Workers.MaxConcurrency),
                ("budget_ms", Options.MainThreadBudgetMs),
                ("texture_budget_mb", Options.TextureBudgetBytes / (1024 * 1024)),
                ("mesh_budget_mb", Options.MeshBudgetBytes / (1024 * 1024)),
                ("cutouts", Options.ExactCutouts ? "blend" : "opaque"));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            StartupError = $"Cannot open the retail dats: {ex.Message}";
            Log.Error("core", StartupError);
        }
    }
}

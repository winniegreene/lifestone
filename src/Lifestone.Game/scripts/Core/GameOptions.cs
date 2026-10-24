using Godot;
using Lifestone.Client.Options;
using Lifestone.Client.Play;
using Lifestone.Client.Scheduling;
using Lifestone.Client.Session;
using Lifestone.Client.Streaming;
using Lifestone.Client.World;

namespace Lifestone.Game.Core;

/// <summary>How DXT (BC1-3) textures reach the GPU (see docs/render-contract.md §3).</summary>
public enum DxtUploadPolicy
{
    /// <summary>
    /// Default: upload the dat's blocks unchanged (<c>Image.FORMAT_DXT1/3/5</c>) with a mip chain whose lower levels are
    /// box-filtered and re-encoded, as retail's D3DXFilterTexture does. DXT1 with punch-through texels on a blended or
    /// alpha-tested surface is expanded to RGBA8 instead (Godot's FORMAT_DXT1 has no alpha).
    /// </summary>
    PassthroughWhereSafe,

    /// <summary>Decode every DXT texture to RGBA8 on a worker (4-8x the memory; no re-encoding).</summary>
    Expand,
}

/// <summary>
/// The engine-behaviour check suite a run executes (<c>--engine-checks</c>, <c>--interior-engine-checks</c>,
/// <c>--sky-checks</c>, <c>--blend-checks</c>, <c>--portal-space-checks</c>, <c>--retail-ui-checks</c>).
/// </summary>
public enum GameCheckSuite
{
    None,
    Engine,
    InteriorEngine,
    Sky,
    Blend,

    /// <summary>The portal view's engine checks (<c>--portal-space-checks</c>; scripts/PortalSpace/PortalSpaceChecks.cs).</summary>
    PortalSpace,

    /// <summary>The retail UI layer's engine checks (<c>--retail-ui-checks</c>; scripts/RetailUi/RetailUiChecks.cs).</summary>
    RetailUi,

    /// <summary>
    /// The offline player's walk checks (<c>--offline-player-checks</c>; scripts/Player/PlayerWalkChecks.cs). Unlike the engine suites it runs the live explore
    /// world with the player in it, so Main builds everything it builds for explore mode.
    /// </summary>
    OfflinePlayer,

    /// <summary>
    /// Play mode's checks on a scripted stand-in server (<c>--play-checks</c>; scripts/Session/PlayChecks.cs). Like the offline player's suite it runs the live
    /// explore world, with the play mode in it, so Main builds everything it builds for <c>--play</c>.
    /// </summary>
    Play,
}

/// <summary>
/// Command-line options, read from Godot's user arguments (everything after <c>--</c>; with tools\godot.ps1 pass them
/// through <c>-GameArgs:'...'</c> or the <c>-Screenshot</c>/<c>-Frames</c>/<c>-DatDir</c> parameters).
/// </summary>
/// <remarks>
/// <para><b>One registry.</b> Every option of every feature is registered, with its help, in
/// <see cref="OptionCatalog"/> (Lifestone.Client); <c>--help</c> prints it. This record parses the core and world options
/// below; the other features parse their own (SkyOptions, InteriorArguments, SpellViewerOptions,
/// TerrainPreviewOptions). An argument no feature registers is reported once here as unknown, an option only another
/// scene reads (the spell viewer's <c>--showcase</c> in the main scene) as ignored, and a value that does not parse
/// (including a mistyped choice such as <c>--vsync=of</c>) is an error, never a silent fallback. Errors are logged as
/// warnings; an automated run with errors quits with exit code 1 (Main).</para>
/// <para><b>Automated runs</b> are derived from the registry too: <see cref="IsAutomated"/> is true when an argument
/// read in this run's scene (<see cref="Scene"/>) ends the run by itself (<see cref="OptionSpec.EndsRun"/>), whichever
/// feature owns it.</para>
/// <list type="table">
/// <item><term>--dat-dir=DIR</term><description>Retail dat folder (else LIFESTONE_DAT_DIR, else the client folder of the Lifestone checkout: <see cref="Lifestone.Data.Dat.DatSet.ResolveDirectory(string?)"/>).</description></item>
/// <item><term>--start=LOC</term><description>Start location: a name (holtburg, arwic), a landblock (A9B4), a cell (A9B40019), optionally @x,y,z. Default holtburg.</description></item>
/// <item><term>--screenshot=PATH</term><description>Save a PNG once the place is complete (streaming, objects, the interiors' cells), --frames frames have rendered and at least 2 s have passed (the golden viewpoints' settle, for the living statics), then quit.</description></item>
/// <item><term>--frames=N</term><description>Frames to render after the place is complete before the screenshot (default 30); Main also waits at least <c>CaptureReadiness.SettleSeconds</c> (2 s) for the living statics.</description></item>
/// <item><term>--fly-path=FROM:TO:SPEED</term><description>Automated flight (m/s) that logs frame times and hitches, then quits.</description></item>
/// <item><term>--load-radius=N</term><description>Landblock rings to stream (default 8, retail's mid_radius).</description></item>
/// <item><term>--budget-ms=X</term><description>Main-thread work budget per frame (default 4).</description></item>
/// <item><term>--workers=N</term><description>Worker threads for decoding (default min(8, cores - 2); <see cref="WorkerPool.DefaultConcurrency"/>).</description></item>
/// <item><term>--vsync=on|off|mailbox|adaptive</term><description>Vertical sync mode (default on).</description></item>
/// <item><term>--max-fps=N</term><description>Frame cap; 0 = none. Default: the screen's refresh rate with vsync off or mailbox, none otherwise (<see cref="FramePacing"/>).</description></item>
/// <item><term>--max-mip=N</term><description>Highest mip level sampled (default 3: retail's at most 4 levels).</description></item>
/// <item><term>--dxt=passthrough|expand</term><description>DXT upload policy (default passthrough with re-encoded mips).</description></item>
/// <item><term>--time-of-day=F</term><description>Day fraction for the default lighting (default 0.5, Midsong).</description></item>
/// <item><term>--quit-after=S</term><description>Quit after S seconds whatever happens (safety net for automated runs).</description></item>
/// <item><term>--log-level=debug|info|warning|error</term><description>Minimum log level (default info).</description></item>
/// <item><term>--walk</term><description>Start in walk mode at the start location, facing its heading.</description></item>
/// <item><term>--smoke-statics</term><description>Retired with the render-contract smoke layer (objects track, M1 wave 3): reported as a bad argument (in <see cref="Errors"/>) and ignored, so an automated run with it quits with exit code 1; the objects layer draws statics.</description></item>
/// <item><term>--objects=all|off|statics,buildings,scenery</term><description>Landscape objects to create (objects track, docs/objects.md; default all).</description></item>
/// <item><term>--object-radius=N</term><description>Landblock rings around the viewer's that have objects (default 1, the retail 3×3).</description></item>
/// <item><term>--static-effects=on|off</term><description>Living statics: default scripts (torch flames, chimney smoke) and default animations of statics (docs/objects.md §1.7; default on).</description></item>
/// <item><term>--cutouts=opaque|blend</term><description>Alpha-tested blending surfaces: the opaque cut-out shortcut (default) or retail's blend after the test (docs/render-contract.md §2).</description></item>
/// <item><term>--texture-budget-mb=N</term><description>Texture cache budget (default 768).</description></item>
/// <item><term>--mesh-budget-mb=N</term><description>Shared-mesh cache budget (default 256).</description></item>
/// <item><term>--engine-checks</term><description>Verify the Godot behaviours the render path relies on (zero-normal flag, lighting, DXT1 alpha), then quit (0 = all hold).</description></item>
/// <item><term>--retail-normals</term><description>Retail's per-block terrain normals (default: seamless; docs/terrain-renderer.md §8).</description></item>
/// <item><term>--blend-space=byte|linear</term><description>Blend in retail's byte space (default) or Godot's linear light (docs/render-contract.md §2.3).</description></item>
/// <item><term>--detail=on|off</term><description>Retail's building and environment detail textures (default on; docs/render-contract.md §2.4).</description></item>
/// <item><term>--interior-engine-checks, --sky-checks, --blend-checks, --portal-space-checks, --retail-ui-checks</term><description>The interiors', the sky's, the blend-space, the portal view's and the retail UI layer's check suites (<see cref="Checks"/>).</description></item>
/// <item><term>--fov=DEG</term><description>The world camera's vertical field of view (default 60; the spell viewer's 55, the terrain preview's 60); Godot's perspective mode accepts 1 to 179.</description></item>
/// <item><term>--help</term><description>Print every registered option and quit.</description></item>
/// </list>
/// </remarks>
public sealed record GameOptions
{
    public string? DatDirectory { get; init; }

    public WorldLocation Start { get; init; } = WorldLocations.Holtburg;

    public string? ScreenshotPath { get; init; }

    public int ScreenshotFrames { get; init; } = 30;

    public FlyPathSpec? FlyPath { get; init; }

    public int LoadRadius { get; init; } = StreamingSettings.RetailDefaultLoadRadius;

    public double MainThreadBudgetMs { get; init; } = 4.0;

    /// <summary>
    /// <c>--fov</c>: the world camera's vertical field of view in degrees, or null for the scene's own (the main scene's
    /// <see cref="DefaultFov"/>, the spell viewer's 55). The portal zoom does not use it: it is an off-centre frustum
    /// (D31), whose rest state is this field of view.
    /// </summary>
    public double? Fov { get; init; }

    /// <summary>The main scene's vertical field of view: 60° (Main.tscn, FlyCamera).</summary>
    public const double DefaultFov = 60.0;

    public int WorkerThreads { get; init; } = DefaultWorkerThreads;

    /// <summary>
    /// Default worker count: all cores but two (main and render thread), at most 8 (<see cref="WorkerPool.DefaultConcurrency"/>).
    /// The perf audit measured the same streaming throughput and start-up with 8 workers as with 30 (every dat read
    /// serialises on DatSet's lock), and more threads only give each GC more to suspend (lead decision, M1 wave 3).
    /// </summary>
    public static int DefaultWorkerThreads => WorkerPool.DefaultConcurrency;

    /// <summary>Godot vsync mode: enabled (default), disabled, mailbox or adaptive.</summary>
    public DisplayServer.VSyncMode VSync { get; init; } = DisplayServer.VSyncMode.Enabled;

    /// <summary><c>--max-fps</c>: the frame cap, 0 for none; null = <see cref="FramePacing"/>'s default.</summary>
    public int? MaxFps { get; init; }

    public float MaxMipLevel { get; init; } = 3f;

    public DxtUploadPolicy DxtPolicy { get; init; } = DxtUploadPolicy.PassthroughWhereSafe;

    public float TimeOfDay { get; init; } = 0.5f;

    public double? QuitAfterSeconds { get; init; }

    public LogLevel LogLevel { get; init; } = LogLevel.Info;

    // --- objects track (M1): landscape objects (docs/objects.md) ---
    /// <summary>Living statics: statics run their default scripts and animations (<c>--static-effects</c>; docs/objects.md §1.7).</summary>
    public bool StaticEffects { get; init; } = true;

    /// <summary>Which landscape objects the objects layer creates (<c>--objects</c>).</summary>
    public Lifestone.Client.Objects.WorldObjectKinds ObjectKinds { get; init; } = Lifestone.Client.Objects.WorldObjectKinds.All;

    /// <summary>Landblock rings around the viewer's that have objects (<c>--object-radius</c>; 1 = retail).</summary>
    public int ObjectRadius { get; init; } = Lifestone.Client.Objects.ObjectRings.RetailRadius;

    /// <summary>Start in walk mode at the start location (eye height above the ground) instead of the overview.</summary>
    public bool Walk { get; init; }

    /// <summary>Draw alpha-tested blending surfaces blended, as retail, instead of the opaque cut-out shortcut.</summary>
    public bool ExactCutouts { get; init; }

    /// <summary>Texture cache budget in bytes.</summary>
    public long TextureBudgetBytes { get; init; } = 768L * 1024 * 1024;

    /// <summary>Shared-mesh cache budget in bytes.</summary>
    public long MeshBudgetBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>
    /// The check suite this run executes (one per run). Check mode: the suite owns the render globals, so the main scene
    /// installs nothing live that writes them (no sky clock, no streaming layers; Main.cs).
    /// </summary>
    public GameCheckSuite Checks { get; init; }

    /// <summary><c>--help</c>: print <see cref="OptionCatalog.HelpText"/> and quit.</summary>
    public bool ShowHelp { get; init; }

    // --- terrain track (M1 terrain integration): docs/terrain-renderer.md §5 ---

    /// <summary><c>--placeholder-terrain</c>: the vertex-coloured placeholder layer instead of the TexMerge terrain.</summary>
    public bool PlaceholderTerrain { get; init; }

    /// <summary>
    /// Terrain normals without the landblock-edge light step (default; render-fidelity fix, docs/terrain-renderer.md §8:
    /// a glitch-fix deviation from retail's per-block <c>CLandBlockStruct::calc_lighting</c>). <c>--retail-normals</c>
    /// restores retail's per-block normals; <c>--seamless-normals</c> is still accepted.
    /// </summary>
    public bool SeamlessTerrainNormals { get; init; } = true;

    // --- end terrain track ---

    // --- render-fidelity fix (wave 3): docs/render-contract.md §2.3-2.4 ---

    /// <summary><c>--blend-space=byte|linear</c>: where the main view blends (default byte, retail's).</summary>
    public Lifestone.Game.Render.BlendSpace BlendSpace { get; init; } = Lifestone.Game.Render.BlendSpace.Byte;

    /// <summary><c>--detail=on|off</c>: retail's building and environment detail textures (default on, retail's preference default).</summary>
    public bool DetailTextures { get; init; } = true;

    // --- end render-fidelity fix ---

    /// <summary>
    /// The session group (<c>--play</c>, <c>--account</c>, <c>--character</c>, <c>--server</c>, <c>--record-session</c>, <c>--session-budget-ms</c>, <c>--retail-net</c>;
    /// M2 state track), parsed by <see cref="SessionOptions"/>, whose problems are in <see cref="Errors"/>. Only the session host (scripts/Session/SessionHost.cs)
    /// acts on it; no option here takes a password (the host reads secrets.json in the backend runtime folder as bytes).
    /// </summary>
    public SessionOptions Session { get; init; } = new();

    /// <summary>
    /// The play-mode group (<c>--login-check</c>; M2 G6), parsed by <see cref="PlayOptions"/>, whose problems are in <see cref="Errors"/>. A login check implies
    /// <c>--play</c> (Main.cs).
    /// </summary>
    public PlayOptions Play { get; init; } = new();

    /// <summary>Problems found while parsing (reported by the caller; parsing never throws).</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// True when the run is automated and must end by itself: an argument read in this run's scene whose registered role
    /// ends the run (a screenshot, a fly path, golden viewpoints, a tour, a showcase, a check suite; <see cref="OptionCatalog"/>).
    /// </summary>
    public bool IsAutomated { get; init; }

    /// <summary>The scene this run shows (<see cref="OptionCatalog.SceneOf"/>); options of other scenes are reported and ignored.</summary>
    public OptionScenes Scene { get; init; } = OptionScenes.Main;

    /// <summary>
    /// Parses user arguments against <paramref name="registry"/> (which says which arguments are known at all, and in
    /// which scenes), for the scene <paramref name="engineArgs"/> start (Godot's own arguments, <c>OS.GetCmdlineArgs()</c>).
    /// </summary>
    public static GameOptions Parse(IEnumerable<string> args, OptionRegistry registry, IEnumerable<string> engineArgs)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var arguments = CommandLineArgument.ParseAll(args);
        var scene = OptionCatalog.SceneOf(engineArgs, arguments);
        var options = new GameOptions { Scene = scene, IsAutomated = registry.EndsRun(arguments, scene) };

        // Unknown arguments and other scenes' options: reported once here (every scene has this autoload), then skipped.
        var errors = new List<string>(registry.Problems(arguments, scene));
        foreach (var argument in arguments)
        {
            if (!registry.AppliesTo(argument.Name, scene))
            {
                continue;
            }

            var (name, value) = (argument.Name, argument.Value);
            switch (name)
            {
                case "--dat-dir":
                    options = options with { DatDirectory = value };
                    break;
                case "--start":
                    if (WorldLocations.TryParse(value, out var start, out var startError))
                    {
                        options = options with { Start = start };
                    }
                    else
                    {
                        errors.Add($"--start: {startError}");
                    }

                    break;
                case "--screenshot":
                    options = options with { ScreenshotPath = value };
                    break;
                case "--frames":
                    options = options with { ScreenshotFrames = Math.Max(1, OptionValues.Int(argument, errors, options.ScreenshotFrames)) };
                    break;
                case "--fly-path":
                    if (FlyPathSpec.TryParse(value, out var path, out var pathError))
                    {
                        options = options with { FlyPath = path };
                    }
                    else
                    {
                        errors.Add($"--fly-path: {pathError}");
                    }

                    break;
                case "--load-radius":
                    options = options with { LoadRadius = Math.Clamp(OptionValues.Int(argument, errors, options.LoadRadius), 0, 20) };
                    break;
                case "--fov":
                    // A value that does not parse is reported and leaves the scene's own field of view.
                    var fov = OptionValues.Double(argument, errors, double.NaN);
                    if (!double.IsNaN(fov))
                    {
                        options = options with { Fov = Math.Clamp(fov, 1.0, 179.0) };
                    }

                    break;
                case "--budget-ms":
                    options = options with { MainThreadBudgetMs = Math.Clamp(OptionValues.Double(argument, errors, options.MainThreadBudgetMs), 0.0, 100.0) };
                    break;
                case "--workers":
                    options = options with { WorkerThreads = Math.Clamp(OptionValues.Int(argument, errors, options.WorkerThreads), 1, 64) };
                    break;
                case "--vsync":
                    options = options with
                    {
                        VSync = OptionValues.Choice(
                            argument,
                            errors,
                            options.VSync,
                            ("on", DisplayServer.VSyncMode.Enabled),
                            ("off", DisplayServer.VSyncMode.Disabled),
                            ("mailbox", DisplayServer.VSyncMode.Mailbox),
                            ("adaptive", DisplayServer.VSyncMode.Adaptive),
                            ("1", DisplayServer.VSyncMode.Enabled),
                            ("true", DisplayServer.VSyncMode.Enabled),
                            ("0", DisplayServer.VSyncMode.Disabled),
                            ("false", DisplayServer.VSyncMode.Disabled)),
                    };
                    break;
                case "--max-fps":
                    // A malformed or negative value keeps the default cap (reported as a bad argument); only 0 uncaps (FramePacing).
                    var maxFps = OptionValues.Int(argument, errors, int.MinValue);
                    if (maxFps < 0 && maxFps != int.MinValue)
                    {
                        errors.Add($"--max-fps: '{argument.Value}' is negative; keeping the default cap");
                    }

                    options = options with { MaxFps = maxFps < 0 ? options.MaxFps : Math.Min(maxFps, 10000) };
                    break;
                case "--max-mip":
                    options = options with { MaxMipLevel = (float)Math.Clamp(OptionValues.Double(argument, errors, options.MaxMipLevel), 0.0, 16.0) };
                    break;
                case "--dxt":
                    options = options with
                    {
                        DxtPolicy = OptionValues.Choice(argument, errors, options.DxtPolicy, ("passthrough", DxtUploadPolicy.PassthroughWhereSafe), ("expand", DxtUploadPolicy.Expand)),
                    };
                    break;
                case "--time-of-day":
                    options = options with { TimeOfDay = (float)Math.Clamp(OptionValues.Double(argument, errors, options.TimeOfDay), 0.0, 1.0) };
                    break;
                case "--quit-after":
                    options = options with { QuitAfterSeconds = Math.Max(1.0, OptionValues.Double(argument, errors, 600.0)) };
                    break;
                case "--walk":
                    options = options with { Walk = true };
                    break;
                case "--smoke-statics":
                    // Retired with the render-contract smoke layer (objects track, M1 wave 3): reported, never read.
                    _ = OptionValues.Switch(argument, errors, false);
                    errors.Add("--smoke-statics is retired (M1 wave 3) and ignored: the objects layer draws statics and buildings");
                    break;
                // --- objects track (M1) ---
                case "--static-effects":
                    options = options with { StaticEffects = OptionValues.Switch(argument, errors, options.StaticEffects) };
                    break;
                case "--objects":
                    if (TryParseObjectKinds(value, out var kinds))
                    {
                        options = options with { ObjectKinds = kinds };
                    }
                    else
                    {
                        errors.Add($"--objects: '{value}' is not all, off or a list of statics, buildings, scenery");
                    }

                    break;
                case "--object-radius":
                    options = options with { ObjectRadius = Math.Clamp(OptionValues.Int(argument, errors, options.ObjectRadius), 0, 8) };
                    break;
                case "--cutouts":
                    options = options with { ExactCutouts = OptionValues.Choice(argument, errors, options.ExactCutouts, ("opaque", false), ("blend", true)) };
                    break;
                case "--texture-budget-mb":
                    options = options with { TextureBudgetBytes = Math.Clamp(OptionValues.Int(argument, errors, 768), 1, 65536) * 1024L * 1024L };
                    break;
                case "--mesh-budget-mb":
                    options = options with { MeshBudgetBytes = Math.Clamp(OptionValues.Int(argument, errors, 256), 1, 65536) * 1024L * 1024L };
                    break;

                // --- check suites: one per run (check mode, Main.cs) ---
                case "--engine-checks":
                    options = WithCheck(options, GameCheckSuite.Engine, errors);
                    break;
                case "--interior-engine-checks":
                    options = WithCheck(options, GameCheckSuite.InteriorEngine, errors);
                    break;
                case "--sky-checks":
                    options = WithCheck(options, GameCheckSuite.Sky, errors);
                    break;
                case "--blend-checks": // render-fidelity fix (wave 3): Render/BlendChecks.cs
                    options = WithCheck(options, GameCheckSuite.Blend, errors);
                    break;
                case "--portal-space-checks": // M2 portal view: PortalSpace/PortalSpaceChecks.cs
                    options = WithCheck(options, GameCheckSuite.PortalSpace, errors);
                    break;
                case "--retail-ui-checks": // M2 retail UI renderer: RetailUi/RetailUiChecks.cs
                    options = WithCheck(options, GameCheckSuite.RetailUi, errors);
                    break;
                case "--play-checks": // M2 G6: Session/PlayChecks.cs (play mode on a scripted stand-in server)
                    options = WithCheck(options, GameCheckSuite.Play, errors);
                    break;
                case "--offline-player-checks": // M2 G5: Player/PlayerWalkChecks.cs (also read by AvatarsOptions, which implies --offline-player)
                    options = WithCheck(options, GameCheckSuite.OfflinePlayer, errors);
                    break;
                case "--help":
                    options = options with { ShowHelp = true };
                    break;
                case "--scene":
                    // The main scene switches on it (Main.cs); OptionCatalog.SceneOf already did. Only the value is checked.
                    _ = OptionValues.Choice(argument, errors, string.Empty, (OptionCatalog.SpellViewerSceneValue, OptionCatalog.SpellViewerSceneValue));
                    break;

                // --- terrain track ---
                case "--placeholder-terrain":
                    options = options with { PlaceholderTerrain = true };
                    break;
                case "--seamless-normals":
                    options = options with { SeamlessTerrainNormals = true };
                    break;
                case "--retail-normals": // render-fidelity fix: retail's per-block normals (landblock-edge light step)
                    options = options with { SeamlessTerrainNormals = false };
                    break;

                // --- render-fidelity fix (wave 3) ---
                case "--blend-space":
                    options = options with
                    {
                        BlendSpace = OptionValues.Choice(argument, errors, options.BlendSpace, ("byte", Lifestone.Game.Render.BlendSpace.Byte), ("linear", Lifestone.Game.Render.BlendSpace.Linear)),
                    };
                    break;
                case "--detail":
                    // Bare --detail means on; a value that is not on or off is reported and detail keeps its default (on).
                    options = options with { DetailTextures = OptionValues.Switch(argument, errors, options.DetailTextures) };
                    break;

                // --- end terrain track ---
                case "--log-level":
                    options = options with
                    {
                        LogLevel = OptionValues.Choice(
                            argument,
                            errors,
                            options.LogLevel,
                            ("debug", LogLevel.Debug),
                            ("info", LogLevel.Info),
                            ("warning", LogLevel.Warning),
                            ("warn", LogLevel.Warning),
                            ("error", LogLevel.Error)),
                    };
                    break;

                // Every other feature's options (sky, interiors, spell viewer, terrain preview) are parsed by the feature.
                default:
                    break;
            }
        }

        var session = SessionOptions.FromArguments(arguments);
        errors.AddRange(session.Errors);
        var play = PlayOptions.FromArguments(arguments);
        errors.AddRange(play.Errors);
        if (play.PlayFramesDirectory is not null && options.Checks != GameCheckSuite.Play)
        {
            errors.Add("--play-frames saves the play checks' shell frames: it needs --play-checks");
        }

        return options with { Session = session, Play = play, Errors = errors };
    }

    private static GameOptions WithCheck(GameOptions options, GameCheckSuite suite, List<string> errors)
    {
        if (options.Checks != GameCheckSuite.None && options.Checks != suite)
        {
            errors.Add($"one check suite per run: {options.Checks} and {suite} were both asked for (tools\\smoke.ps1 runs them all)");
            return options;
        }

        return options with { Checks = suite };
    }

    // --- objects track (M1) ---
    private static bool TryParseObjectKinds(string text, out Lifestone.Client.Objects.WorldObjectKinds kinds)
    {
        kinds = Lifestone.Client.Objects.WorldObjectKinds.None;
        switch (text)
        {
            case "" or "all" or "on":
                kinds = Lifestone.Client.Objects.WorldObjectKinds.All;
                return true;
            case "off" or "none":
                return true;
        }

        foreach (var item in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (item)
            {
                case "statics":
                    kinds |= Lifestone.Client.Objects.WorldObjectKinds.Statics;
                    break;
                case "buildings":
                    kinds |= Lifestone.Client.Objects.WorldObjectKinds.Buildings;
                    break;
                case "scenery":
                    kinds |= Lifestone.Client.Objects.WorldObjectKinds.Scenery;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }
}

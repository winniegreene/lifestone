namespace Lifestone.Client.Options;

/// <summary>
/// The registry of every Lifestone command-line option (user arguments after <c>--</c>; with tools\godot.ps1 pass them
/// through <c>-GameArgs:'...'</c>). <c>--help</c> prints it.
/// </summary>
/// <remarks>
/// <para>One group per feature. The parsing of each group's values lives with its feature (named in each group's
/// comment); this class is where a feature declares that an option exists and what it does. <b>Adding an option:</b>
/// add its <c>case "--name"</c> to the feature's parser and an <see cref="OptionSpec"/> to the feature's group: here
/// for the M1 groups, and in the group's own partial file (<c>OptionCatalog.&lt;Group&gt;.cs</c>) for the M2 groups,
/// which are already listed in <see cref="Registry"/> so their owners never edit this file. A group with no options yet
/// is left out of <c>--help</c>. <c>OptionCatalogTests</c> fails when a parser's option is missing here or a registered
/// option is parsed nowhere (the parsers of Lifestone.Game, Lifestone.Client and Lifestone.Headless), and the game warns
/// about (and an automated run fails on) any argument no group registers. No option takes a password or other secret
/// (<c>OptionCatalogTests</c>; tools\godot.ps1 echoes the whole command line): secrets come from
/// server\runtime\secrets.json as bytes.</para>
/// <para><b>Scenes.</b> Each group names the scenes that read its options (<see cref="OptionGroup.Scenes"/>; an option
/// another scene also reads says so with <see cref="OptionSpec.AlsoIn"/>). A run's scene comes from
/// <see cref="SceneOf"/>; an option of another scene is reported and ignored, and does not make the run automated
/// (<see cref="OptionRegistry.Problems"/>, <see cref="OptionRegistry.EndsRun"/>).</para>
/// <para>An option name is registered once. Options several scenes share (<c>--screenshot</c>, <c>--frames</c>,
/// <c>--dat-dir</c>, <c>--max-mip</c>, <c>--retail-normals</c>, <c>--day-group</c>) are registered by the first feature
/// below that reads them, and their help says where else they apply. The headless host
/// (<see cref="OptionScenes.Headless"/>) reads the Core options marked so, and the Session and Headless groups.</para>
/// </remarks>
public static partial class OptionCatalog
{
    /// <summary>The option registry of the game (all scenes).</summary>
    public static OptionRegistry Registry { get; } = new(
    [
        Core(),
        World(),
        Rendering(),
        Checks(),
        Sky(),
        Interiors(),
        SpellViewer(),
        TerrainPreview(),
        Cinematic(), // hero video tooling: the cinematic directors (docs/hero-video.md)
        Video(), // screen-capture videos of the main scene (OptionCatalog.Video.cs)

        // M2 groups, one per owning track, each in its own partial file (OptionCatalog.<Group>.cs); empty until the owner
        // adds its options together with their parser.
        Session(),
        Headless(),
        PortalSpace(),
        Audio(),
        RetailUi(),
        Avatars(),
        Play(),
        Buff(), // the buff cycle's four options (M3 2.1 s0b; OptionCatalog.Buff.cs)
    ]);

    /// <summary>The first lines of <c>--help</c>.</summary>
    public const string HelpPreamble =
        "Lifestone command-line options. Pass them after '--' (with tools\\godot.ps1: -GameArgs:'--option=value ...').\n" +
        "Arguments no feature registers are reported as unknown, and options of another scene as ignored; an automated run (marked [ends run]) with a bad argument quits with exit code 1.\n" +
        "Relative paths in any option are taken from the repository root.";

    /// <summary>The help text <c>--help</c> prints.</summary>
    public static string HelpText() => Registry.FormatHelp(HelpPreamble);

    /// <summary>The value of <c>--scene</c> that opens the spell viewer.</summary>
    public const string SpellViewerSceneValue = "spellviewer";

    /// <summary>
    /// The scene a run shows, for <see cref="OptionRegistry.Problems"/> and <see cref="OptionRegistry.EndsRun"/>: the scene
    /// file Godot was started with (an engine argument naming Main.tscn, SpellViewer.tscn or TerrainPreview.tscn; none =
    /// the project's main scene, Main.tscn), switched to the spell viewer by <c>--scene=spellviewer</c> in the main scene
    /// (Main.cs). Any other scene file (or a <c>uid://</c> path) gives <see cref="OptionScenes.All"/>: no option is out of
    /// place there.
    /// </summary>
    /// <param name="engineArgs">Godot's own arguments (<c>OS.GetCmdlineArgs()</c>), which hold the scene path when one is given.</param>
    /// <param name="userArgs">The user arguments (after <c>--</c>).</param>
    public static OptionScenes SceneOf(IEnumerable<string> engineArgs, IEnumerable<CommandLineArgument> userArgs)
    {
        ArgumentNullException.ThrowIfNull(engineArgs);
        ArgumentNullException.ThrowIfNull(userArgs);
        var scene = OptionScenes.Main;
        foreach (var raw in engineArgs)
        {
            var arg = (raw ?? string.Empty).Trim().Trim('"');
            if (arg.StartsWith("uid://", StringComparison.OrdinalIgnoreCase))
            {
                scene = OptionScenes.All;
                continue;
            }

            if (arg.StartsWith('-') || !(arg.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase) || arg.EndsWith(".scn", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            scene = arg[(arg.LastIndexOfAny(['/', '\\']) + 1)..].ToLowerInvariant() switch
            {
                "main.tscn" => OptionScenes.Main,
                "spellviewer.tscn" => OptionScenes.SpellViewer,
                "terrainpreview.tscn" => OptionScenes.TerrainPreview,
                _ => OptionScenes.All,
            };
        }

        return scene == OptionScenes.Main && userArgs.Any(a => a.Name == "--scene" && a.Value == SpellViewerSceneValue) ? OptionScenes.SpellViewer : scene;
    }

    /// <summary>
    /// Settings every scene reads (parsed by Lifestone.Game Core/GameOptions.cs in the GameServices autoload); the headless
    /// host reads the five marked <see cref="OptionScenes.Headless"/>, with its own parser.
    /// </summary>
    public static OptionGroup Core() => new("Core", "every scene",
    [
        new("--help", null, "Print every option and quit (also the headless host's, which lists its own options only).", OptionRole.Help, AlsoIn: OptionScenes.Headless),
        new("--dat-dir", "DIR", "Retail dat folder (else LIFESTONE_DAT_DIR, else the client folder of the Lifestone checkout, next to Lifestone.slnx; also the headless host's).", AlsoIn: OptionScenes.Headless),
        new("--log-level", "debug|info|warning|error", "Minimum log level (default info; also the headless host's).", AlsoIn: OptionScenes.Headless),
        new("--screenshot", "PATH", "Save a PNG once the scene is ready (main scene: the place complete - streaming, objects, the interiors' cells - then --frames frames and at least 2 s, so living statics' flames and smoke show, as in the golden viewpoints), then quit. Relative paths are taken from the repository root; never inside src/ or tests/. Also the spell viewer's and the terrain preview's.", OptionRole.EndsRun),
        new("--frames", "N", "Frames rendered before --screenshot (default 30; the main scene also waits at least 2 s; also the spell viewer's and the terrain preview's)."),
        new("--quit-after", "S", "Quit with exit code 1 after S seconds whatever happens (safety net for automated runs; also the headless host's).", AlsoIn: OptionScenes.Headless),
        new("--vsync", "on|off|mailbox|adaptive", "Vertical sync (default on). With off or mailbox the frame rate is capped (see --max-fps)."),
        new("--max-fps", "N", "Frame-rate cap; 0 = none (uncapped measurements). Default: none with vsync on or adaptive, the screen's refresh rate with vsync off or mailbox; a malformed or negative value keeps the default."),
        new("--workers", "N", "Worker threads for decoding (default min(8, cores - 2), at least 1; also the headless host's).", AlsoIn: OptionScenes.Headless),
        new("--budget-ms", "X", "Main-thread work budget per frame, in ms (default 4)."),
        new("--max-mip", "N", "Highest mip level sampled (default 3: retail's at most 4 levels; also the terrain preview's)."),
        new("--dxt", "passthrough|expand", "DXT upload: the dat's blocks with re-encoded mips (default) or RGBA8 (render-contract.md §3)."),
        new("--cutouts", "opaque|blend", "Alpha-tested blending surfaces: opaque cut-out shortcut (default) or retail's blend after the test."),
        new("--texture-budget-mb", "N", "Texture cache budget in MB (default 768)."),
        new("--mesh-budget-mb", "N", "Shared-mesh cache budget in MB (default 256)."),
        new("--time-of-day", "F", "Day fraction 0-1 (default 0.5, Midsong): of the live day interactively, of the first Sunny day from 10 P.Y. in automated runs."),
    ]);

    /// <summary>The main scene's world (parsed by Core/GameOptions.cs).</summary>
    public static OptionGroup World() => new("World", "main scene",
    [
        new("--start", "LOC", "Start: a name (holtburg, arwic, mines-of-despair), a landblock (A9B4), a cell (A9B40019), optionally @x,y,z. Default holtburg."),
        new("--walk", null, "Start in walk mode at the start location, facing its heading."),
        new("--fly-path", "FROM:TO:SPEED", "Automated flight (m/s) that reports frame times and hitches, then quits (exit 2 when a frame took over 50 ms).", OptionRole.EndsRun),
        new("--load-radius", "N", "Landblock rings to stream (default 8, retail's mid_radius)."),
        new("--placeholder-terrain", null, "The vertex-coloured placeholder terrain instead of the TexMerge terrain."),
        new("--objects", "all|off|statics,buildings,scenery", "Landscape objects to create (default all; docs/objects.md)."),
        new("--object-radius", "N", "Landblock rings around the viewer's that have objects (default 1, the retail 3x3)."),
        new("--static-effects", "on|off", "Living statics: default scripts and animations of statics, such as torch flames, chimney smoke and windmills (default on; objects.md §1.7; also the spell viewer's).", AlsoIn: OptionScenes.SpellViewer),
        new("--smoke-statics", null, "Retired in M1 wave 3 with the render-contract smoke layer (the objects layer draws every static): reported as a bad argument and ignored; an automated run with it quits with exit code 1."),
    ], OptionScenes.Main);

    /// <summary>
    /// Render fidelity (wave 3: docs/render-contract.md §2.3-2.4, terrain-renderer.md §8), read by the main scene and the
    /// spell viewer (parsed by Core/GameOptions.cs; the terrain preview's TerrainPreviewOptions.cs reads the normals too).
    /// Byte-space blending and the detail textures are retail's behaviour, on by default (<c>--blend-space=linear</c> and
    /// <c>--detail=off</c> move away from retail); the seamless terrain normals remove a rendering glitch retail had (lead
    /// fidelity policy), and <c>--retail-normals</c> restores it.
    /// </summary>
    public static OptionGroup Rendering() => new("Rendering", "main scene and spell viewer",
    [
        new("--blend-space", "byte|linear", "Blend in retail's byte space (default) or Godot's linear light (render-contract.md §2.3)."),
        new("--detail", "on|off", "Retail's building and environment detail textures (default on; render-contract.md §2.4)."),
        new("--fov", "DEG", "The world camera's vertical field of view, 1 to 179 (default 60; the spell viewer's 55; the terrain preview's, default 60). The portal zoom is a frustum and ignores it.", AlsoIn: OptionScenes.TerrainPreview),
        new("--retail-normals", null, "Retail's per-block terrain normals, with the light step at landblock edges (default: seamless normals; also the terrain preview's).", AlsoIn: OptionScenes.TerrainPreview),
        new("--seamless-normals", null, "Seamless terrain normals, now the default (kept for old command lines; also the terrain preview's).", AlsoIn: OptionScenes.TerrainPreview),
    ], OptionScenes.Main | OptionScenes.SpellViewer);

    /// <summary>
    /// Engine-behaviour check suites: one per run, nothing live installed (Main.cs check mode); parsed by GameOptions.cs
    /// (and still by their features: Interiors/InteriorArguments.cs, Sky/SkyOptions.cs).
    /// </summary>
    public static OptionGroup Checks() => new("Checks", "main scene; one suite per run; run windowed, tools\\godot.ps1 -Offscreen",
    [
        new("--engine-checks", null, "Verify the Godot behaviours the render path and the streamer rely on, then quit (0 = all hold).", OptionRole.CheckSuite),
        new("--interior-engine-checks", null, "Verify the engine behaviours the cell shaders rely on, then quit (0 = all hold).", OptionRole.CheckSuite),
        new("--sky-checks", null, "Verify byte-space sky blending, sky order and pass layering, then quit (0 = all hold).", OptionRole.CheckSuite),
        new("--blend-checks", null, "Verify byte-space blending, the decode, the detail pass and built-in material colours in both blend spaces, then quit (0 = all hold).", OptionRole.CheckSuite),
        new("--portal-space-checks", null, "Run the portal-space checks and quit.", OptionRole.CheckSuite),
        new("--offline-player-checks", null, "Walk the offline player (the real controller, driven through its intents) through Holtburg: a lap, a wall, stairs up and down into buildings, a standing and a running jump, in the chase camera and first person; verify blocking and sliding, cell tracking, the movement messages and their cadence, the camera never inside geometry and no frame over 50 ms, then quit (0 = all hold). Implies --offline-player and, unlike the other suites, runs the live world.", OptionRole.CheckSuite),
        new("--play-checks", null, "Run play mode on a scripted stand-in server, with no backend (a scripted visit: the character list, an entry, an arrival through portal space, a teleport and a logoff) and verify the whole path in the engine: the world waits for the player, the player is built from its own CreateObject with the session's movement sink, the world streams around it, portal space plays and sends LoginComplete once, the player is placed on the ground, a server teleport moves the world, the physics and the camera, and a logoff takes it all away; then quit (0 = all hold). Implies --play; the session is the stand-in, never a live one.", OptionRole.CheckSuite),
        new("--retail-ui-checks", null, "Verify the retail UI layer in the engine (tiles byte for byte at scales 1 to 3, nearest filtering, no seams, the viewport hole, the cursor, the key table, the fonts, the real screens, input), then quit (0 = all hold).", OptionRole.CheckSuite),
    ], OptionScenes.Main);

    /// <summary>Sky, time and navigation (parsed by Lifestone.Game World/Sky/SkyOptions.cs; the teleport tour by Diagnostics/TeleportTourRunner.cs).</summary>
    public static OptionGroup Sky() => new("Sky, time and navigation", "main scene",
    [
        new("--game-time", "S", "Start at game time S seconds (retail calendar; 0 = Morningthaw 1, 10 P.Y.)."),
        new("--time-speed", "X", "Game seconds per real second: 0 pause, 1 retail (the interactive default)."),
        new("--day-group", "N|auto", "Force a Region day group (weather) instead of retail's daily choice (also the terrain preview's, default 0).", AlsoIn: OptionScenes.TerrainPreview),
        new("--sky", "on|off", "Draw the sky objects (default on; lighting and fog follow the time either way)."),
        new("--sky-anchor", "follow|retail|camera", "Sky object anchor: follow (default: the camera's x and y at retail's height, no jump at landblock edges), retail (the viewer's landblock corner) or camera."),
        new("--retail-sky-anchor", null, "Retail's sky anchor (same as --sky-anchor=retail: the sky jumps at landblock edges)."),
        new("--weather", "on|off", "Retail's weather option (default on)."),
        new("--ui", "on|off|open", "Time and teleport panels (default: shown, hidden in automated runs); open also opens the teleport panel."),
        new("--golden-viewpoints", "DIR", "Render the golden viewpoints to DIR (default docs/screenshots/m1/golden) and quit.", OptionRole.EndsRun, ValueOptional: true),
        new("--golden-only", "ID[,ID]", "Only these golden viewpoints (id or unique fragment)."),
        new("--golden-frames", "N", "Frames rendered after the place is complete before each golden capture (default 45; at least 2 s also pass, so flames and smoke have started)."),
        new("--teleport-tour", "LAPS", "Jump through teleport-panel places LAPS times (default 1), measuring each jump, then quit (Diagnostics/TeleportTourRunner.cs).", OptionRole.EndsRun, ValueOptional: true),
        new("--teleport-stops", "NAME[,NAME]", "The places --teleport-tour visits (default ten towns and indoor places)."),
    ], OptionScenes.Main);

    /// <summary>Interiors (parsed by Lifestone.Game World/Interiors/InteriorArguments.cs).</summary>
    public static OptionGroup Interiors() => new("Interiors", "main scene",
    [
        new("--interiors", "on|off", "EnvCells, portal visibility and indoor lighting (default on; off for A/B measurements)."),
        new("--interior-eye", "CELL@x,y,z", "Put the eye exactly here (landblock-local; an outdoor or EnvCell id as in --start)."),
        new("--interior-look", "H,P", "Face compass heading H and pitch P (degrees)."),
        new("--interior-show-all", null, "Draw every loaded cell (portal visibility off, as F9)."),
        new("--interior-debug-ambient", "X", "Indoor ambient level instead of retail's 0.2 (0 shows the burned-in static lights alone)."),
        new("--interior-tour", "DIR", "The interiors measurement tour; screenshots to DIR (default docs/screenshots/m1), then quit.", OptionRole.EndsRun, ValueOptional: true),
    ], OptionScenes.Main);

    /// <summary>The spell viewer (parsed by Lifestone.Game SpellViewer/SpellViewerOptions.cs).</summary>
    public static OptionGroup SpellViewer() => new("Spell viewer", "--scene=spellviewer",
    [
        new("--scene", "spellviewer", "Open the spell viewer instead of the world (the main scene reads it and switches).", AlsoIn: OptionScenes.Main),
        new("--spell", "ID", "Cast this spell once the scene is ready (and select it)."),
        new("--fizzle", null, "Make --spell fizzle."),
        new("--speed", "X", "Playback speed, 0.25-2 (default 1)."),
        new("--showcase", null, "Cast every showcase spell in turn with a scripted camera, then quit.", OptionRole.EndsRun),
        new("--showcase-only", "I[,I]", "Restrict --showcase to these showcase indices (0-based)."),
        new("--shots", "T1[,T2]", "With --screenshot: seconds after the --spell cast at which to save PATH-<seconds>.png, then quit."),
        new("--fixed-step", null, "Advance the stage by exactly 1/30 s per frame (implied by --shots)."),
        new("--camera", "YAW,PITCH,DIST", "Orbit camera: compass heading, pitch (negative = down), distance in metres."),
        new("--focus", "caster|target|mid", "What the camera orbits (default mid)."),
        new("--no-drudge", null, "Do not draw the Drudge Skulker (it is still simulated as the target)."),
        new("--no-statics", null, "Do not load Holtburg's statics and buildings."),
        new("--no-ui", null, "Hide the panels (captions stay)."),
        new("--seed", "N", "Random seed of the effects (default 1)."),
    ], OptionScenes.SpellViewer);

    /// <summary>The terrain preview scene (parsed by Lifestone.Game World/Terrain/TerrainPreviewOptions.cs).</summary>
    public static OptionGroup TerrainPreview() => new("Terrain preview", "scene res://scenes/TerrainPreview.tscn, as tools\\terrain-golden.ps1 runs it",
    [
        new("--center", "XXYY", "Centre landblock (hex; default A9B4, Holtburg)."),
        new("--size", "N", "N x N landblocks around the centre (odd; default 5)."),
        new("--time", "F", "Day fraction (default 0.5, Midsong)."),
        new("--look-at", "X,Y", "Camera target in metres from the centre landblock's south-west corner (default 96,96)."),
        new("--distance", "M", "Camera distance in metres."),
        new("--pitch", "DEG", "Camera pitch down from horizontal (default 28)."),
        new("--yaw", "DEG", "Compass heading the camera looks towards (default 0, north)."),
        new("--debug-view", "N", "Terrain debug view (0 = retail; see TerrainDebugView)."),
        new("--no-fog", null, "No fog."),
        new("--perf", null, "Log build timings and render statistics."),
        new("--golden", "DIR", "Golden comparison against the CPU reference compositor, then quit (0 within tolerance, 2 outside, 1 error); DIR defaults to docs/screenshots/m1.", OptionRole.EndsRun, ValueOptional: true),
        new("--golden-blocks", "XXYY[,XXYY]", "Landblocks of the golden comparison (default A9B4,7E62,7AC2,1FB1,1EB1)."),
        new("--golden-size", "N", "Golden render size in pixels per landblock edge (default 1000)."),
    ], OptionScenes.TerrainPreview);

    /// <summary>
    /// Hero video tooling (docs/hero-video.md): the cinematic directors of the main scene and the spell viewer (parsed by
    /// Lifestone.Game Cinematic/CinematicOptions.cs). tools\hero-video.ps1 runs them with the engine's --fixed-fps.
    /// </summary>
    public static OptionGroup Cinematic() => new("Cinematic", "main scene and spell viewer; tools\\hero-video.ps1",
    [
        new("--cinematic", "FILE", "Film the shots of a shot list (JSON; relative to the repository root) with a deterministic camera, then quit. Record with --cinematic-capture (or Godot's --write-movie) and the engine's --fixed-fps; the log names each shot's frames.", OptionRole.EndsRun),
        new("--cinematic-capture", "DIR", "Write each recorded shot's frames to DIR/<shot>.rgba (raw RGBA8 at the window's size, with a .json sidecar); run with the engine's --fixed-fps."),
        new("--cinematic-shot", "ID[,ID]", "Only these shots of the list (the main scene films world shots, the spell viewer spell shots)."),
        new("--cinematic-stills", "DIR", "Save PNG stills of each shot to DIR (default docs/screenshots/hero/stills) instead of filming, for framing.", ValueOptional: true),
        new("--cinematic-stills-at", "F[,F]", "Where in each shot the stills are taken, as fractions of its length (default 0,0.5,1)."),
    ], OptionScenes.Main | OptionScenes.SpellViewer);
}

using Godot;
using Lifestone.Client.Play;
using Lifestone.Client.Streaming;
using Lifestone.Client.World;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.Player;
using Lifestone.Game.Render;
using Lifestone.Game.Sessions;
using Lifestone.Game.World;

namespace Lifestone.Game;

/// <summary>
/// The main scene (<c>scenes/Main.tscn</c>): the streamed world (terrain, objects, interiors, sky), the fly camera,
/// the position HUD and the perf overlay; plus the automated modes (<c>--screenshot</c>, <c>--fly-path</c>) and the
/// check mode (<see cref="GameOptions.Checks"/>: one engine-behaviour check suite, nothing live installed).
/// </summary>
/// <remarks>
/// <para>Start view: <c>--start</c> (default Holtburg's town drop point, 0xA9B40019). The camera starts
/// <see cref="StartOverviewHeight"/> m above the spot (its ground when the location has no height) and
/// <see cref="StartOverviewDistance"/> m behind it, looking at it; with <c>--walk</c> it starts in walk mode on the spot
/// at eye height, facing the location's heading.</para>
/// <para>Exit codes: 0 success; 1 failure (dats missing, screenshot not saved, a bad argument in an automated run, a
/// check failed); 2 a fly path with a frame over 50 ms; a run killed by tools\godot.ps1's timeout reports 124.</para>
/// </remarks>
public partial class Main : Node3D
{
    public const double StartOverviewHeight = 45.0;
    public const double StartOverviewDistance = 90.0;
    private const double ScreenshotIdleTimeoutSeconds = 180.0;

    private GameServices _services = null!;
    private WorldRoot _world = null!;
    private FlyCamera _camera = null!;
    private const ulong LensLogIntervalMilliseconds = 500; // cam-fov-resize: at most one INFO line per half second of a window drag
    private Window? _lensWindow;
    private Vector2 _lensSize;
    private ulong _lensLoggedMilliseconds;
    private bool _lensLogPending;
    private PerfOverlay? _perf;
    private CaptureReadiness? _readiness; // navigation fix: screenshot-mode idle check
    private double _runTime;
    private int _framesAfterIdle;
    private double _secondsAfterIdle;
    private bool _done;

    public override void _Ready()
    {
        // --- infra (M1 wave 3): --help (GameServices printed it and quits) and bad arguments in automated runs ---
        if (GameServices.Instance.Options is { ShowHelp: true } || FailOnBadArguments(GameServices.Instance.Options))
        {
            SetProcess(false);
            return;
        }

        // --- M1 spell viewer track: `--scene=spellviewer` opens scenes/SpellViewer.tscn instead of the world. ---
        if (SpellViewer.SpellViewerOptions.RequestedBy(OS.GetCmdlineUserArgs()))
        {
            SetProcess(false);
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, SpellViewer.SpellViewer.ScenePath);
            return;
        }

        // --- end spell viewer ---
        if (Lifestone.Client.Options.CommandLineArgument.ParseAll(OS.GetCmdlineUserArgs()).Any(a => a.Name == "--record-audio"))
        {
            // --record-audio: the audio service starts it in this _Ready (PortalSpace/PortalSpaceSetup.cs). A run that returns before
            // that, has no audio service or was given a refused path says so here, once.
            var recordOptions = Lifestone.Client.Audio.AudioOptions.Parse(OS.GetCmdlineUserArgs());
            Callable.From(() =>
            {
                if (Audio.AudioService.Current is not { IsRecording: true })
                {
                    Log.Error(
                        "audio",
                        "--record-audio: nothing is recorded (a refused path, or a run without the audio service: a check suite, --retail-ui-only or a failed start)",
                        ("path", recordOptions.RecordPath ?? "refused"),
                        ("problems", string.Join("; ", recordOptions.Errors.Where(e => e.StartsWith("--record-audio", StringComparison.Ordinal)))));
                }
            }).CallDeferred();
        }

        _services = GameServices.Instance;
        var options = _services.Options;
        Log.Info(
            "main",
            "renderer",
            ("driver", RenderingServer.GetCurrentRenderingDriverName()),
            ("adapter", RenderingServer.GetVideoAdapterName()),
            ("window", DisplayServer.WindowGetSize()));

        // --- infra (M1 wave 3): check mode. A check suite owns the render globals, so nothing live that writes them (the
        // sky clock, streaming layers) is installed. The engine and interior suites need no world at all (and no dats);
        // the sky suite needs the sky controller, but no streamed layers; the blend suite needs only the environment.
        // They quit with their own exit code. ---
        switch (options.Checks)
        {
            case GameCheckSuite.Engine:
                AddChild(new EngineChecks { Name = "EngineChecks" });
                return;
            case GameCheckSuite.InteriorEngine:
                AddChild(new World.Interiors.InteriorEngineChecks { Name = "InteriorEngineChecks" });
                return;
            case GameCheckSuite.PortalSpace:
                // M2 portal view (W2-pre(a), extended by the portal track): lighting, lens, sky follow and the world arbiter,
                // on the engine alone: no world, no dats.
                AddChild(new PortalSpace.PortalSpaceChecks { Name = "PortalSpaceChecks" });
                return;
            case GameCheckSuite.RetailUi:
                // M2 retail UI renderer (G2 U2): the tile layer, the hole, the cursor, the key table, the fonts and the real screens
                // through the whole layer; the dat-based checks skip without the dats.
                AddChild(new RetailUi.RetailUiChecks { Name = "RetailUiChecks" });
                return;
        }

        // --- end check mode (the sky and blend suites need the environment below) ---
        if (_services.Dats is null)
        {
            ShowFatal(_services.StartupError ?? "The retail dats are not available.");
            return;
        }

        // --- M2 G6 play mode (--play): the live session, signed in to the bundled ACE on 127.0.0.1 (scripts/Session/SessionHost.cs). Without a session
        // (no backend, a refused gate) the run says why and ends; it never falls back to the explore scene it was not asked for. ---
        SessionHost? sessionHost = null;
        var playChecks = options.Checks is GameCheckSuite.Play;
        var loginCheck = options.Play.LoginCheck;
        var play = playChecks || ((options.Session.Play || loginCheck != LoginCheckKind.None) && options.Checks is GameCheckSuite.None);
        if (play)
        {
            if (playChecks)
            {
                PlayChecks.SupplyHumanLooks(_services);
            }

            sessionHost = playChecks ? SessionHost.InstallStandIn(this, _services, PlayChecks.Visit, PlayChecks.ReplayVisit) : SessionHost.Install(this, _services);
            if (sessionHost.Session is null)
            {
                ShowPlayFailure(sessionHost.StartFailure ?? "the session could not be started");
                return;
            }
        }

        // --- M2 retail UI alone: --retail-ui-only builds no world and no sky behind the UI (RetailUi/RetailUiSetup.cs) ---
        if (options.Checks is GameCheckSuite.None && RetailUi.RetailUiSetup.RequestedAlone(_services))
        {
            RetailUi.RetailUiSetup.AttachOnly(this, _services);
            Log.Info("main", "ready (retail ui only)");
            return;
        }

        if (options.Checks is GameCheckSuite.None or GameCheckSuite.Sky)
        {
            World.Sky.SkyTrack.Prefetch(_services); // wave-3 perf: the sky models prepare on workers during the warm-ups below
        }

        var sky = RenderGlobals.ApplyDefaults(_services.Sky, options.TimeOfDay, options.MaxMipLevel);
        Log.Info(
            "main",
            "default lighting",
            ("time_of_day", sky.TimeOfDay),
            ("sun_dir", sky.Lighting.SunDirection),
            ("sun", sky.Lighting.SunDiffuse),
            ("ambient", sky.Lighting.ObjectAmbient),
            ("fog", $"{sky.Fog.Start:0}-{sky.Fog.End:0} m {sky.Fog.Color}"));
        SetUpEnvironment(sky);

        // --- infra (M1 wave 3): check mode. The sky suite: the sky controller over an empty world (no streamed layers).
        // The blend suite (render-fidelity fix, Render/BlendChecks.cs): known quads in front of the camera over the
        // byte-space environment just built, with no world and no sky driver writing the globals. ---
        switch (options.Checks)
        {
            case GameCheckSuite.Sky:
                StartSkyChecks(options);
                return;
            case GameCheckSuite.Blend:
                AddChild(new BlendChecks { Name = "BlendChecks" });
                Log.Info("main", "check mode: blend checks over the environment alone");
                return;
        }

        // --- terrain track: start the TexMerge terrain now, so its atlas decodes on a worker during the warm-up ---
        var terrain = options.PlaceholderTerrain ? null : World.Terrain.TerrainLandblockLayer.Start(
            _services, this, options.SeamlessTerrainNormals ? World.Terrain.TerrainNormalMode.SeamlessAcrossBlocks : World.Terrain.TerrainNormalMode.RetailPerBlock);

        var warm = _services.Render.WarmUp(_services.Dats);
        Log.Info("main", "render path warmed up", ("surfaces", warm.Surfaces), ("variants", warm.Variants), ("enumerate_ms", warm.EnumerateMs), ("warm_ms", warm.WarmMs));

        _world = GetNode<WorldRoot>("WorldRoot");
        _camera = GetNode<FlyCamera>("Player");
        var start = options.FlyPath?.From ?? options.Start;
        var startPosition = start.Global;
        _world.Initialize(_services, new StreamingSettings { LoadRadius = options.LoadRadius, MaxInFlight = Math.Max(4, options.WorkerThreads * 2) }, startPosition);
        _world.RegisterLayer(terrain ?? (ILandblockLayer)new TerrainLayer(_services.Dats, _services.Ground)); // terrain track: placeholder only with --placeholder-terrain
        // --- objects track (M1): statics, buildings and scenery with retail LOD (docs/objects.md); the --smoke-statics layer is retired (wave 3) ---
        if (options.ObjectKinds != Lifestone.Client.Objects.WorldObjectKinds.None)
        {
            Lifestone.Game.World.Objects.ObjectsLayer.Install(_world, _services);
        }
        _camera.Initialize(_world, _services.Ground);
        PlaceAtStart(start);

        // --- interiors track: EnvCells, portal visibility, indoor lighting, indoor focus (docs/interiors.md) ---
        var interiors = Lifestone.Game.World.Interiors.InteriorsSetup.Install(this, _world, _camera, _services, start);

        // --- M2 G5: --offline-player puts your own character in explore mode (Player/OfflinePlayerSetup.cs); nothing of it exists without the option ---
        PlayerController? player = null;
        if (play && OfflinePlayerSetup.Requested())
        {
            Log.Warn("main", "--offline-player is ignored with --play: the player is the one the server creates");
        }
        else if (OfflinePlayerSetup.Requested())
        {
            player = OfflinePlayerSetup.Install(this, _services, _world, interiors, start, _camera);
        }

        GetNode<PositionHud>("Hud/PositionHud").Initialize(_camera);
        _perf = GetNode<PerfOverlay>("Hud/PerfOverlay");
        _perf.Initialize(_world);
        if (Diagnostics.CleanHud.Requested())
        {
            Diagnostics.CleanHud.Attach(this); // --clean-hud: no diagnostic text over the world, for videos
        }

        Diagnostics.VideoOptions.ApplyHideFloaties(); // --video-hide-floaties, for videos
        Diagnostics.VideoOptions.ApplyVideoChat(); // --video-chat, for videos
        if (Diagnostics.VideoTimer.Requested() is { } timerLevel)
        {
            Diagnostics.VideoTimer.Attach(this, timerLevel, options.Play.LoginCheck == LoginCheckKind.ClimberStart); // --video-timer: the clock since her creation, to the level (0:00:00 on the screens when this run creates her)
        }

        if (Diagnostics.VideoSyncMarker.Requested())
        {
            Diagnostics.VideoSyncMarker.Attach(this, _services); // --video-sync-marker: a white frame and a click, for measuring a take's sound-to-picture offset
        }

        // --- M1 sky track: time of day, sky objects, time/teleport panels, golden viewpoints (World/Sky/SkyTrack.cs) ---
        var skyController = World.Sky.SkyTrack.Attach(this, _services, _world, _camera, playMode: play);

        // --- M2 retail UI over the explore scene (canvas layer 10; off unless --retail-ui is given; RetailUi/RetailUiSetup.cs) ---
        var retailUi = RetailUi.RetailUiSetup.AttachExplore(this, _services, _camera);
        if (retailUi is not null)
        {
            player?.Input?.UseRetailUi(retailUi); // with the retail UI on, the player's keys go through its router
        }

        // --- hero video tooling: --cinematic=FILE films a shot list's world shots (Cinematic/CinematicSetup.cs, docs/hero-video.md) ---
        Cinematic.CinematicSetup.Attach(this, _services, _world, _camera, skyController);

        // --- M2 G6 play mode: the explore scene with a server behind it (Session/PlayMode.cs); made before the portal view, which asks it for the destination's cell ---
        PlayMode? playMode = sessionHost is null ? null : PlayMode.Install(this, _services, sessionHost, _world, interiors, _camera, retailUi, autoEnter: loginCheck == LoginCheckKind.None); // a login check enters by its own script

        // --- M4 polish pass P2: play mode draws with retail's field of view (RetailLens), before the portal view reads the camera's rest lens ---
        if (playMode is not null)
        {
            ApplyRetailFov();
        }

        // --- M2 portal view: the portal track's wave-2 stage fills this seam (PortalSpace/PortalSpaceSetup.cs) ---
        var portal = PortalSpace.PortalSpaceSetup.Install(this, _services, _world, _camera, skyController, playMode is null ? null : () => playMode.DestinationCell);
        playMode?.AttachPortal(portal);
        if (retailUi is not null && portal is not null)
        {
            // The in-game shell's SmartBox viewport draws the tunnel (G4 g1): the shell's hole is the whole window, and the world shows through it otherwise.
            retailUi.RegisterViewportSource(PortalSpace.PortalTunnelViewportSource.ViewportId, new PortalSpace.PortalTunnelViewportSource(portal.View));
            // The tunnel shows from its first frame while the shell is built and painted: the black backdrop that holds the world yields to it (M2 W3 DoD 5).
            retailUi.TunnelShowing = () => portal.View.Shown;
            // G4 g6: portal space is busy for its whole sequence, so the retail UI's busy count follows it (ClientUISystem::IncrementBusyCount / DecrementBusyCount).
            portal.Presenter.BusyChanged += busy => { if (busy) { retailUi.Manager.Busy.Increment(); } else { retailUi.Manager.Busy.Decrement(); } };
        }

        // --- M2 G5: --offline-player-checks and --offline-player-frames drive the offline player (Player/PlayerRunnersSetup.cs) ---
        PlayerRunnersSetup.Attach(this, _services, _world, player);

        // --- M2 G6: --play-checks drives play mode on the scripted stand-in session (Session/PlayChecks.cs) ---
        if (playChecks && playMode is not null)
        {
            PlayChecks.Install(this, _services, sessionHost!, playMode, portal, retailUi, _world, interiors, _camera, options.Play.PlayFramesDirectory);
        }

        // --- M2 G6 W12: --login-check=enter drives the whole first login against the live ACE (Session/LoginCheckRunner.cs) ---
        if (loginCheck != LoginCheckKind.None && playMode is not null)
        {
            LoginCheckRunner.Install(this, _services, sessionHost!, playMode, portal, retailUi, loginCheck);
        }

        if (options.FlyPath is { } path)
        {
            var runner = new FlyPathRunner { Name = "FlyPathRunner" };
            AddChild(runner);
            runner.Initialize(path, _camera, _world, options.ScreenshotPath, SaveScreenshot);
        }

        Log.Info("main", "ready", ("start", start), ("camera", _camera.WorldPosition));
    }

    /// <summary>
    /// M4 polish pass P2 and M5 6 v1 (<c>cam-fov-resize</c>, M5 D9): the play camera draws with retail's field of view (<see cref="RetailLens"/>: the 90-degree preference turned into a vertical angle for the window's aspect,
    /// 73 degrees at 800x600) unless <c>--fov</c> says otherwise (<c>--fov=60</c> is the old default). Explore mode keeps its 60. Retail recomputes the angle every frame from the viewport it draws into
    /// (<c>SmartBox::RenderNormalMode</c>, 11.6096 0x00453B40), so the angle follows the window here: it is set now and again whenever the root window changes size (<see cref="OnWindowResizedForLens"/>), at the cost of one
    /// division per resize event and nothing per frame. <c>TODO(cam-fov-resize-tunnel)</c>: the portal tunnel's own lens is built once from the camera's angle at the start (<c>PortalTunnelView.Create</c>) and is not redone on a resize.
    /// </summary>
    private void ApplyRetailFov()
    {
        if (_services.Options.Fov is not null)
        {
            Log.Info("camera", "the world camera keeps the --fov given", ("fov_degrees", _camera.Camera.Fov));
            return;
        }

        var window = GetTree().Root;
        var size = window.GetVisibleRect().Size;
        if (size.X < 2f || size.Y < 2f)
        {
            Log.Warn("camera", "TODO(cam-fov-resize): the window has no size yet; the camera keeps its default field of view until it has one", ("fov_degrees", _camera.Camera.Fov));
        }
        else
        {
            var fov = Lifestone.Client.Camera.RetailLens.VerticalFovDegrees(Lifestone.Client.Camera.RetailLens.DefaultFovDegrees, size.X / size.Y);
            var was = _camera.Camera.Fov;
            _camera.Camera.Fov = fov;
            Log.Info("camera", "the world camera draws with retail's field of view (Render.FieldOfView 90, SmartBox::RenderNormalMode)", ("vertical_fov_degrees", fov), ("window", size.ToString()), ("was", was));
        }

        _lensWindow = window;
        window.SizeChanged += OnWindowResizedForLens;
    }

    /// <summary>
    /// The root window changed size (a drag, a maximise, a display change): the vertical angle is recomputed from the new aspect, as retail does each frame. The camera is set at once (one division, no allocation);
    /// the INFO line is rate-limited to one per <see cref="LensLogIntervalMilliseconds"/>, with the last size of a drag always logged from <see cref="_Process"/>.
    /// </summary>
    private void OnWindowResizedForLens()
    {
        if (_lensWindow is null || _camera is null)
        {
            return;
        }

        var size = _lensWindow.GetVisibleRect().Size;
        if (size.X < 2f || size.Y < 2f)
        {
            return; // a minimised window: the camera keeps its angle until it has a size again
        }

        _camera.Camera.Fov = Lifestone.Client.Camera.RetailLens.VerticalFovDegrees(Lifestone.Client.Camera.RetailLens.DefaultFovDegrees, size.X / size.Y);
        _lensSize = size;
        _lensLogPending = true;
        FlushLensLog(Time.GetTicksMsec());
    }

    private void FlushLensLog(ulong nowMilliseconds)
    {
        if (!_lensLogPending || nowMilliseconds - _lensLoggedMilliseconds < LensLogIntervalMilliseconds)
        {
            return;
        }

        _lensLogPending = false;
        _lensLoggedMilliseconds = nowMilliseconds;
        Log.Info("camera", "the window was resized; the world camera's vertical field of view follows (RetailLens)", ("vertical_fov_degrees", _camera.Camera.Fov), ("window", $"{_lensSize.X:0}x{_lensSize.Y:0}"));
    }

    public override void _ExitTree()
    {
        if (_lensWindow is not null && IsInstanceValid(_lensWindow))
        {
            _lensWindow.SizeChanged -= OnWindowResizedForLens;
        }

        _lensWindow = null;
    }

    public override void _Process(double delta)
    {
        _runTime += delta;
        if (_lensLogPending)
        {
            FlushLensLog(Time.GetTicksMsec());
        }

        var options = _services.Options;
        if (options.QuitAfterSeconds is { } limit && _runTime > limit && !_done)
        {
            _done = true;
            Log.Error("main", "--quit-after reached; quitting", ("seconds", _runTime));
            GetTree().Quit(1);
            return;
        }

        // Screenshot mode (without a fly path, which takes its own): wait until the place is complete, then N frames and
        // at least CaptureReadiness.SettleSeconds (the golden viewpoints' settle: living statics' flames and smoke).
        if (_done || options.ScreenshotPath is not { } path || options.FlyPath is not null || _world is null || !_world.IsInitialized)
        {
            return;
        }

        // Navigation fix (M1 wave 3): "idle" includes the objects ring, the interiors' cells and the main-thread queue
        // (Diagnostics/CaptureReadiness.cs), so dungeon shots are never black; it is re-checked every frame.
        _readiness ??= new CaptureReadiness(_services, _world);
        var complete = _readiness.IsComplete;
        if (complete || _runTime > ScreenshotIdleTimeoutSeconds)
        {
            if (_framesAfterIdle == 0)
            {
                _secondsAfterIdle = 0;
                Log.Info("main", complete ? "streaming idle" : "streaming did not go idle; taking the screenshot anyway",
                    ("seconds", _runTime), ("loaded", _world.Streamer.Planner.LoadedCount), ("state", _readiness.Describe()),
                    ("frames", options.ScreenshotFrames), ("settle_seconds", CaptureReadiness.SettleSeconds));
            }
            else
            {
                _secondsAfterIdle += delta;
            }

            // '>': the frame that found the place complete must be drawn before the capture (the image is the last drawn
            // frame). The settle (M1 final review): the statics' default scripts have made no particles yet 30 frames
            // after the place is complete (a Holtburg Redoubt shot showed bare torch handles), so a screenshot waits as
            // long as a golden viewpoint does.
            if (++_framesAfterIdle > options.ScreenshotFrames && _secondsAfterIdle >= CaptureReadiness.SettleSeconds)
            {
                _done = true;
                GetTree().Quit(SaveScreenshot(path) ? 0 : 1);
            }
        }
        else
        {
            _framesAfterIdle = 0;
        }
    }

    private void PlaceAtStart(WorldLocation start)
    {
        var target = start.Global;
        if (!start.HasHeight && _services.Ground.TryGetHeight(target, out var ground))
        {
            target = target with { Z = ground };
        }

        if (_services.Options.Walk)
        {
            _camera.WalkMode = true;
            var groundZ = _services.Ground.TryGetHeight(target, out var g) ? g : (float)target.Z;
            _camera.PlaceAt(target with { Z = groundZ + FlyCamera.EyeHeight }, start.HeadingDegrees, 0.0);
            return;
        }

        // Stand back from the spot, opposite its facing, and look at it.
        var back = AcCoordinates.DirectionOfHeading(start.HeadingDegrees);
        var eye = new GlobalPosition(
            target.X - (back.X * StartOverviewDistance),
            target.Y - (back.Y * StartOverviewDistance),
            target.Z + StartOverviewHeight);
        if (_services.Ground.TryGetHeight(eye, out var eyeGround) && eye.Z < eyeGround + 5.0)
        {
            eye = eye with { Z = eyeGround + 5.0 };
        }

        var horizontal = Math.Sqrt(((target.X - eye.X) * (target.X - eye.X)) + ((target.Y - eye.Y) * (target.Y - eye.Y)));
        var pitch = -Math.Atan2(eye.Z - target.Z, horizontal) * 180.0 / Math.PI;
        _camera.PlaceAt(eye, AcCoordinates.HeadingOf(target.X - eye.X, target.Y - eye.Y), pitch);
    }

    private void SetUpEnvironment(Lifestone.Data.World.Sky.SkyState sky)
    {
        // Render-fidelity fix: the blend space first, since the background colour depends on it (render-contract §2.3).
        ByteSpaceBlending.Select(_services.Options.BlendSpace);

        // Lifestone shaders are unshaded and do retail lighting and fog themselves; the environment only clears to the
        // fog colour and must not alter colours (linear tonemapper, exposure 1, no glow/ambient/fog/adjustments).
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = RenderGlobals.FogBackground(sky),
            AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 1f,
            GlowEnabled = false,
            FogEnabled = false,
            VolumetricFogEnabled = false,
            SsaoEnabled = false,
            SsrEnabled = false,
            SdfgiEnabled = false,
            AdjustmentEnabled = false,
        };
        var node = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        if (node is null)
        {
            node = new WorldEnvironment { Name = "WorldEnvironment" };
            AddChild(node);
        }

        node.Environment = environment;
        ByteSpaceBlending.Attach(node); // render-fidelity fix: the byte-space decode in the tonemap pass (render-contract §2.3)
    }

    /// <summary>
    /// Play mode could not start (no backend, a refused gate; <see cref="SessionHost.StartFailure"/> names the remedy): the message on the window and, in an
    /// unattended run (an automated one, or one with <c>--quit-after</c>), exit code 2 as the headless host's "the run could not start".
    /// </summary>
    private void ShowPlayFailure(string message)
    {
        Log.Error("main", "play mode cannot start: " + message);
        var label = new Label
        {
            Text = $"Lifestone cannot log in:\n{message}\n\nStart the backend and the game together with tools/play.ps1 (it stops what it started when you quit).",
            Position = new Vector2(24, 24),
        };
        label.AddThemeColorOverride("font_color", Colors.OrangeRed);
        var layer = new CanvasLayer();
        layer.AddChild(label);
        AddChild(layer);
        if (_services.Options.IsAutomated || _services.Options.QuitAfterSeconds is not null)
        {
            _done = true;
            GetTree().Quit(2);
        }
    }

    private void ShowFatal(string message)
    {
        Log.Error("main", message);
        var label = new Label
        {
            Text = $"Lifestone cannot start:\n{message}\n\nPut your own retail client files (client_portal.dat, client_cell_1.dat) " +
                   "in the client folder of your Lifestone checkout, or pass --dat-dir=..., or set LIFESTONE_DAT_DIR.",
            Position = new Vector2(24, 24),
        };
        label.AddThemeColorOverride("font_color", Colors.OrangeRed);
        var layer = new CanvasLayer();
        layer.AddChild(label);
        AddChild(layer);
        if (_services.Options.IsAutomated)
        {
            _done = true;
            GetTree().Quit(1);
        }
    }

    /// <summary>
    /// Saves the viewport as PNG (<see cref="ScreenshotWriter"/>: a relative path is taken from the repository root,
    /// never inside src/ or tests/). Returns false (logged) on failure.
    /// </summary>
    public bool SaveScreenshot(string path) =>
        ScreenshotWriter.Save(GetViewport(), path, "main", ("seconds", _runTime), ("camera", _camera?.WorldPosition), ("heading", _camera?.Heading));

    // --- infra (M1 wave 3) ---

    /// <summary>
    /// An automated run with a bad argument (unknown, or a value that does not parse) quits with exit code 1 instead of
    /// running something else until a timeout; interactive runs only warn (GameServices logged each one).
    /// </summary>
    private bool FailOnBadArguments(GameOptions options)
    {
        if (!options.IsAutomated || options.Errors.Count == 0)
        {
            return false;
        }

        _done = true;
        Log.Error("main", "automated run with bad arguments; quitting (--help lists every option)", ("problems", string.Join("; ", options.Errors)));
        GetTree().Quit(1);
        return true;
    }

    /// <summary>Check mode for <c>--sky-checks</c>: the sky track over an initialised but empty world (no layers stream).</summary>
    private void StartSkyChecks(GameOptions options)
    {
        _world = GetNode<WorldRoot>("WorldRoot");
        _camera = GetNode<FlyCamera>("Player");
        _world.Initialize(_services, new StreamingSettings { LoadRadius = 0, MaxInFlight = 4 }, options.Start.Global);
        _camera.Initialize(_world, _services.Ground);
        PlaceAtStart(options.Start);
        World.Sky.SkyTrack.Attach(this, _services, _world, _camera);
        Log.Info("main", "check mode: sky checks over an empty world", ("start", options.Start));
    }
}

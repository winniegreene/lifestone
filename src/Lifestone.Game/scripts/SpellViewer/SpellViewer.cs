using System.Collections;
using System.Diagnostics;
using System.Globalization;
using Godot;
using Lifestone.Client.Effects;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Scenery;
using Lifestone.Data.World.Sky;
using Lifestone.Game.Core;
using Lifestone.Game.Render;
using Lifestone.Game.Render.Particles;
using Lifestone.Game.World;
using Lifestone.Game.World.Objects;
using Lifestone.Game.World.Terrain;
using AcFrame = Lifestone.Data.Animation.AcFrame;
using AcVector2 = System.Numerics.Vector2;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.SpellViewer;

/// <summary>
/// scenes/SpellViewer.tscn: an Aluvian male casts any spell of the SpellTable at a Drudge Skulker on the real terrain of
/// Holtburg, with the retail gestures, words, particle effects and war projectiles (docs/spell-viewer.md).
/// </summary>
/// <remarks>
/// <para>Everything simulated comes from the engine-agnostic <see cref="SpellStage"/> (Lifestone.Client) over the data
/// layer's <see cref="SpellCastSimulation"/>; this node only draws it: <see cref="ActorView"/>s for the caster, the target
/// and the projectiles, a <see cref="ParticleRenderer"/> for every effect object, the TexMerge terrain
/// (<see cref="TerrainRenderer"/>) of the 5×5 landblocks around 0xA9B4 and, optionally, Holtburg's objects (statics,
/// buildings, scenery and living statics) through the world's objects path (<see cref="StaticObjectsView"/>).</para>
/// <para>Space: the stage is in landblock 0xA9B4's local AC frame (metres from its south-west corner, Z up); the Godot
/// origin is that corner (a landblock-granular floating origin, as the world uses).</para>
/// </remarks>
public partial class SpellViewer : Node3D
{
    /// <summary>The scene file.</summary>
    public const string ScenePath = "res://scenes/SpellViewer.tscn";

    /// <summary>Landblock the stage stands in (Holtburg, <see cref="StageSite.Landblock"/>).</summary>
    public static readonly LandblockCoord Center = StageSite.Landblock;

    /// <summary>
    /// Caster position (0xA9B4 local x, y), facing compass heading <see cref="DefaultHeading"/>: the open ground in the
    /// south of Holtburg, 4 m south of the timbered house (107.5, 36; its south wall lies on y = 25) and 3.8 m north of
    /// the lamp post at (106, 17.2). <see cref="StageSite"/> holds the placement; <c>StageSiteTests</c> checks it keeps
    /// 2 m of clearance from every building and static.
    /// </summary>
    public static readonly AcVector2 DefaultStand = StageSite.CasterStand;

    /// <summary>The caster faces east, the drudge west.</summary>
    public const float DefaultHeading = StageSite.Heading;

    /// <summary>Caster–target distance (the brief asks for 10–15 m; ACE projectile speeds make bolts take ~0.7 s).</summary>
    public const float DefaultDistance = StageSite.Distance;

    /// <summary>Fixed step for deterministic captures (30 fps).</summary>
    public const double FixedStepSeconds = 1.0 / 30.0;

    private readonly List<string> _optionErrors = new();
    private readonly Dictionary<(uint Setup, int Index), ActorView> _projectileViews = new();
    private readonly HashSet<(uint Setup, int Index)> _onStage = new();
    private StaticObjectsView? _statics;
    private readonly List<string> _consumedEvents = new();
    private GameServices _services = null!;
    private DatSet _dats = null!;
    private SpellViewerOptions _options = new();
    private TerrainRenderer? _terrain;
    private SpellStage _stage = null!;
    private ActorMeshLibrary _actorMeshes = null!;
    private ParticleMeshLibrary _particleMeshes = null!;
    private ParticleRenderer _particles = null!;
    private ActorView _casterView = null!;
    private ActorView _targetView = null!;
    private Node3D _projectileRoot = null!;
    private OrbitCamera _camera = null!;
    private SpellViewerHud _hud = null!;
    private Label3D _words = null!;
    private ShowcaseDirector? _director;
    private Cinematic.SpellCinematic? _cinematic; // hero video tooling: --cinematic (docs/hero-video.md)
    private SkyState _sky = null!;
    private Godot.Environment? _environment;
    private double _speed = 1.0;
    private bool _paused;
    private bool _showDrudge = true;
    private bool _showParticles = true;
    private bool _ready;
    private bool _done;
    private int _frames;
    private int _shotIndex;
    private int _eventsSeen;
    private int _exceptions;
    private uint? _lastSpell;
    private bool _lastFizzle;
    private double _maxParticleMs;
    private int _maxParticles;
    private long _maxDrawCalls;
    private readonly Lifestone.Client.Diagnostics.FrameTimeRecorder _particleTimes = new();
    private readonly Lifestone.Client.Diagnostics.FrameTimeRecorder _drawTimes = new();
    private readonly Lifestone.Client.Diagnostics.FrameTimeRecorder _frameTimes = new();
    private double _loadFrameMaxMs;
    private int _loadFrameMaxAt;
    private (double Statics, double ParticleMeshes) _loadFrameMaxParts;
    private double _staticsStepMaxMs;
    private string? _pendingShot;
    private bool _finishAfterShot;
    private bool _initialCastDone;
    private int _framesReady;

    /// <summary>The stage (for the director and tests of the scene).</summary>
    public SpellStage Stage => _stage;

    public OrbitCamera Camera => _camera;

    public SpellViewerHud Hud => _hud;

    /// <summary>Particles drawn last frame.</summary>
    public int ParticlesDrawn => _particles.DrawList.Stats.Drawn;

    /// <summary>True once the statics (if any) have finished attaching.</summary>
    public bool StaticsReady => _statics?.IsReady ?? true;

    /// <summary>True once every particle mesh has been built in the background.</summary>
    public bool ParticlesReady => _particleMeshes.PrewarmPending == 0;

    /// <summary>Exceptions caught in the frame loop so far (the showcase fails when non-zero).</summary>
    public int Exceptions => _exceptions;

    public override void _Ready()
    {
        _services = GameServices.Instance;
        var args = OS.GetCmdlineUserArgs();
        _options = SpellViewerOptions.Parse(args, _optionErrors);
        foreach (var error in _optionErrors)
        {
            Log.Warn("spellviewer", "bad argument", ("problem", error));
        }

        if (_optionErrors.Count > 0 && _options.IsAutomated)
        {
            // An automated run with a mistyped option fails fast (exit 1) instead of running something else until the
            // godot.ps1 timeout.
            Fail("bad arguments: " + string.Join("; ", _optionErrors));
            return;
        }

        Log.Info("spellviewer", "starting", ("driver", RenderingServer.GetCurrentRenderingDriverName()), ("adapter", RenderingServer.GetVideoAdapterName()),
            ("window", DisplayServer.WindowGetSize()), ("options", _options));
        if (_services.Dats is not { } dats)
        {
            Fail(_services.StartupError ?? "The retail dats are not available.");
            return;
        }

        _dats = dats;
        try
        {
            var timer = Stopwatch.StartNew();
            _sky = RenderGlobals.ApplyDefaults(_services.Sky, _services.Options.TimeOfDay, _services.Options.MaxMipLevel);
            SetUpEnvironment(_sky);
            var warm = _services.Render.WarmUp(dats);
            Log.Info("spellviewer", "render path warmed up", ("variants", warm.Variants), ("ms", warm.WarmMs));
            BuildTerrain();
            BuildStage();
            BuildViews();

            // Navigation/UX fix (M1 wave 3): the Region's sky objects behind the stage, as the main scene draws them.
            SpellViewerSky.Create(this, _services, _environment!, _camera, Center, _services.Options.TimeOfDay, () => _stage.Time, _sky.Fog);
            BuildHud();

            // Legal notices (AGPL-3.0 §5(d)): the About panel (F1), its button beside the spell panel when the panels show.
            Lifestone.Game.UI.AboutPanel.Attach(this, withButton: _options.Ui && !_options.Showcase, new Vector2(352, 10), canvasLayer: 11);
            if (_options.Statics)
            {
                StartStatics();
            }

            _speed = _options.Speed;
            _showDrudge = _options.ShowDrudge;
            if (_options.Showcase)
            {
                _director = new ShowcaseDirector(this, _options);
            }

            // Hero video tooling: --cinematic=FILE films the shot list's spell shots (Cinematic/SpellCinematic.cs), null otherwise.
            _cinematic = Cinematic.SpellCinematic.Create(this);

            Log.Info("spellviewer", "ready", ("ms", timer.Elapsed.TotalMilliseconds), ("caster", _stage.CasterFrame.Origin), ("target", _stage.TargetFrame.Origin));
            _ready = true;
        }
        catch (Exception ex)
        {
            Log.Exception("spellviewer", "start-up failed", ex);
            Fail(ex.Message);
        }
    }

    public override void _ExitTree()
    {
        _statics?.Dispose();
        _particleMeshes?.Dispose();
        _actorMeshes?.Dispose();
        _terrain?.Dispose();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space } && _director is null)
        {
            _hud.TogglePause();
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.R } && _director is null)
        {
            Replay();
        }
    }

    public override void _Process(double delta)
    {
        if (!_ready || _done)
        {
            return;
        }

        _frames++;
        var processStart = Stopwatch.GetTimestamp();
        if (_pendingShot is { } shot)
        {
            // The viewport now holds the frame drawn after the previous _Process, i.e. the state the shot was taken for.
            _pendingShot = null;
            var saved = SaveScreenshot(shot);
            if (_finishAfterShot || !saved)
            {
                Finish(saved ? 0 : 1);
                return;
            }
        }

        try
        {
            var phase = Stopwatch.GetTimestamp();
            StepStatics();
            var staticsMs = Stopwatch.GetElapsedTime(phase).TotalMilliseconds;
            phase = Stopwatch.GetTimestamp();
            _particleMeshes.PrewarmStep(2.0);
            var prewarmMs = Stopwatch.GetElapsedTime(phase).TotalMilliseconds;
            var step = _cinematic?.StepSeconds ?? (_options.FixedStep || _director is not null ? FixedStepSeconds : Math.Min(delta, 0.25));
            if (_cinematic is not null)
            {
                _cinematic.Process(step); // hero video tooling: the film's fixed step (1/60 s) and its camera
            }
            else if (_director is not null)
            {
                _director.Process(step);
            }
            else if (!_initialCastDone && _frames >= 2 && ((StaticsReady && ParticlesReady) || !_options.IsAutomated))
            {
                _initialCastDone = true;
                if (_options.Spell is { } spell)
                {
                    Cast(spell, _options.Fizzle);
                }
            }

            var drawStart = Stopwatch.GetTimestamp();
            if (!_paused)
            {
                _stage.Advance(step * _speed);
            }

            DrawStage();
            if (_stage.Simulation is not null)
            {
                // Stage step + actor poses + particle batches, the viewer's per-frame main-thread cost.
                _drawTimes.Add(Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds);
                _particleTimes.Add(_particles.LastUpdateMs);
                _frameTimes.Add(delta * 1000.0);
                _maxDrawCalls = Math.Max(_maxDrawCalls, (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame));
            }

            UpdateTexts();
            if (_stage.Simulation is null)
            {
                // Loading frames (statics attach, particle mesh prewarm) before the first cast: not in the cast timings
                // above, so their slowest main-thread frame is tracked separately.
                var ms = Stopwatch.GetElapsedTime(processStart).TotalMilliseconds;
                if (ms > _loadFrameMaxMs)
                {
                    _loadFrameMaxMs = ms;
                    _loadFrameMaxAt = _frames;
                    _loadFrameMaxParts = (staticsMs, prewarmMs);
                }
            }
        }
        catch (Exception ex)
        {
            _exceptions++;
            Log.Exception("spellviewer", "frame failed", ex);
            if (_options.IsAutomated)
            {
                _done = true;
                GetTree().Quit(1);
                return;
            }
        }

        Automation();
    }

    /// <summary>Casts a spell now (UI, director, --spell).</summary>
    public void Cast(uint spellId, bool fizzle)
    {
        _lastSpell = spellId;
        _lastFizzle = fizzle;
        _eventsSeen = 0;
        _hud.ClearLog();
        if (_stage.Simulation is null)
        {
            LogLoadTimes();
        }

        var timer = Stopwatch.StartNew();
        var sim = _stage.Cast(spellId, fizzle);
        var spell = sim.Visual.Spell;
        Log.Info("spellviewer", "cast", ("spell", spellId), ("name", spell.Name), ("fizzle", fizzle), ("ms", timer.Elapsed.TotalMilliseconds), ("words", sim.Visual.Plan.Words),
            ("projectile", sim.Visual.Projectile?.Kind), ("notes", string.Join("; ", sim.Visual.Notes)));
        foreach (var note in sim.Visual.Notes)
        {
            _hud.AddLog("note: " + note);
        }
    }

    /// <summary>Casts the last spell again.</summary>
    public void Replay()
    {
        if (_lastSpell is { } spell)
        {
            Cast(spell, _lastFizzle);
        }
    }

    /// <summary>
    /// Saves the viewport as PNG (<see cref="Diagnostics.ScreenshotWriter"/>: a relative path is taken from the repository
    /// root, never inside src/ or tests/); false (logged) on failure.
    /// </summary>
    public bool SaveScreenshot(string path) =>
        Diagnostics.ScreenshotWriter.Save(GetViewport(), path, "spellviewer", ("stage_time", _stage.Time), ("particles", ParticlesDrawn));

    /// <summary>Ends an automated run.</summary>
    public void Finish(int exitCode)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        Log.Info("spellviewer", "finished", ("exit", exitCode), ("frames", _frames), ("max_particles", _maxParticles), ("max_particle_update_ms", _maxParticleMs),
            ("exceptions", _exceptions), ("particle_meshes", _particleMeshes.MeshCount), ("particle_materials", _particleMeshes.MaterialCount));
        Log.Info("spellviewer", "timing (ms; frames with a cast on stage)",
            ("frames", _particleTimes.Count),
            ("particles_avg", _particleTimes.Average), ("particles_p99", _particleTimes.Percentile(99)), ("particles_max", _particleTimes.Max),
            ("draw_avg", _drawTimes.Average), ("draw_p99", _drawTimes.Percentile(99)), ("draw_max", _drawTimes.Max),
            ("frame_avg", _frameTimes.Average), ("frame_p99", _frameTimes.Percentile(99)), ("frame_max", _frameTimes.Max),
            ("draw_calls_max", _maxDrawCalls));
        LogLoadTimes();
        GetTree().Quit(exitCode);
    }

    private void LogLoadTimes() =>
        Log.Info("spellviewer", "loading frames (ms; main thread, before the first cast)",
            ("slowest_frame", _loadFrameMaxMs), ("at_frame", _loadFrameMaxAt), ("its_statics", _loadFrameMaxParts.Statics),
            ("its_particle_meshes", _loadFrameMaxParts.ParticleMeshes), ("slowest_statics_step", _staticsStepMaxMs),
            ("slowest_particle_mesh", _particleMeshes.SlowestPrewarmMs), ("particle_mesh", $"0x{_particleMeshes.SlowestPrewarmKey.GfxObjId:X8}"),
            ("particle_meshes_pending", _particleMeshes.PrewarmPending));

    private void Fail(string message)
    {
        Log.Error("spellviewer", message);
        var layer = new CanvasLayer();
        layer.AddChild(new Label { Text = $"Spell viewer cannot start:\n{message}", Position = new Vector2(24, 24) });
        AddChild(layer);
        if (_options.IsAutomated)
        {
            _done = true;
            GetTree().Quit(1);
        }
    }

    private void SetUpEnvironment(SkyState sky)
    {
        // As Main: Lifestone shaders light and fog themselves; the environment clears to the fog colour until
        // SpellViewerSky makes the sky view its background.
        ByteSpaceBlending.Select(_services.Options.BlendSpace); // render-fidelity fix: retail's byte-space blending
        var environment = _environment = new Godot.Environment
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
        var node = new WorldEnvironment { Name = "WorldEnvironment", Environment = environment };
        AddChild(node);
        ByteSpaceBlending.Attach(node); // render-fidelity fix: the byte-space decode in the tonemap pass (render-contract §2.3)
    }

    private void BuildTerrain()
    {
        var timer = Stopwatch.StartNew();
        var atlas = TerrainRenderer.PrepareAtlas(_dats, _services.Textures);
        // Render-fidelity fix: the game's normal mode, so --retail-normals works here too (docs/terrain-renderer.md §8).
        _terrain = new TerrainRenderer(_dats, atlas, _services.Options.SeamlessTerrainNormals ? TerrainNormalMode.SeamlessAcrossBlocks : TerrainNormalMode.RetailPerBlock);
        TerrainRenderer.MaxMipLevel = _services.Options.MaxMipLevel;
        TerrainRenderer.ApplySky(_sky);
        var coords = new List<LandblockCoord>();
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                if (Center.Offset(dx, dy) is { } c)
                {
                    coords.Add(c);
                }
            }
        }

        // Loading phase: the blocks are prepared on the core WorkerPool while the main thread waits (nothing is drawn yet).
        var terrain = _terrain;
        var jobs = coords.Select(c => _services.Workers.RunSync(_ => terrain.PrepareBlock(c))).ToArray();
        Task.WaitAll(jobs);
        var prepared = jobs.Select(j => j.Result).OfType<PreparedTerrainBlock>().ToList();

        var root = new Node3D { Name = "Terrain" };
        AddChild(root);
        foreach (var block in prepared.OrderBy(b => b.Coord.X).ThenBy(b => b.Coord.Y))
        {
            var landblockRoot = new Node3D { Name = $"LB_{block.Coord.X:X2}{block.Coord.Y:X2}", Position = LandblockOrigin(block.Coord) };
            root.AddChild(landblockRoot);
            _terrain.AttachBlock(block, landblockRoot);
            _services.Ground.Register(new TerrainSampler(block.Terrain, _dats.TerrainRegion.LandHeightTable));
        }

        Log.Info("spellviewer", "terrain", ("landblocks", prepared.Count), ("ms", timer.Elapsed.TotalMilliseconds), ("normals", _terrain.NormalMode));
    }

    private void BuildStage()
    {
        var world = AceWorldExtract.Load();
        var resolver = SpellVisualResolver.Create(_dats, world);
        var drudge = world.Creatures.FirstOrDefault(c => c.Name == "Drudge Skulker")
            ?? throw new KeyNotFoundException($"{AceWorldExtract.FileName} has no Drudge Skulker.");
        var (casterFrame, targetFrame) = SpellStage.FacingFrames(DefaultStand, DefaultHeading, DefaultDistance, GroundHeight);
        _stage = new SpellStage(resolver, VisualObject.HumanMale(_dats), VisualObject.FromCreature(drudge), casterFrame, targetFrame, _options.Seed);
        var warm = Stopwatch.StartNew();
        _stage.WarmUp();
        Log.Info("spellviewer", "simulation warmed up", ("ms", warm.Elapsed.TotalMilliseconds));
    }

    private void BuildViews()
    {
        _actorMeshes = new ActorMeshLibrary(_services);
        _particleMeshes = new ParticleMeshLibrary(_dats, _services.GfxObjMeshes, _services.Render, _services.Workers);
        var warmTimer = Stopwatch.StartNew();
        var effects = (Lifestone.Data.Effects.DatEffectSource)_stage.Resolver.Effects;
        var hw = effects.AllEmitterInfoIds().Select(id => effects.GetEmitterInfo(id)?.HwGfxObjId ?? 0).Where(id => id != 0);
        var (particleGfxObjs, particleVariants) = _particleMeshes.WarmUp(hw);
        Log.Info("spellviewer", "particle shaders warmed up", ("gfxobjs", particleGfxObjs), ("variants", particleVariants), ("ms", warmTimer.Elapsed.TotalMilliseconds));
        _casterView = new ActorView { IsPlayer = true };
        _casterView.Initialize(_services, _stage.CasterVisual.SetupId, _actorMeshes);
        AddChild(_casterView);
        _targetView = new ActorView();
        _targetView.Initialize(_services, _stage.TargetVisual.SetupId, _actorMeshes);
        AddChild(_targetView);
        var preload = Stopwatch.StartNew();
        var preloaded = _casterView.PreloadMeshes() + _targetView.PreloadMeshes();
        Log.Info("spellviewer", "actor meshes preloaded", ("gfxobjs", preloaded), ("ms", preload.Elapsed.TotalMilliseconds));
        _projectileRoot = new Node3D { Name = "Projectiles" };
        AddChild(_projectileRoot);
        _particles = new ParticleRenderer { Name = "Particles" };
        AddChild(_particles);
        _particles.Initialize(_particleMeshes);

        // Holtburg's living statics (docs/objects.md §1.7) share the stage's particle meshes.
        LivingStaticsSystem.Install(this, _services, _particleMeshes);

        _words = new Label3D
        {
            Name = "Words",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 48,
            PixelSize = 0.006f,
            OutlineSize = 12,
            // Render-fidelity fix: a built-in material in the main view, so its colour goes through BuiltinColor (render-contract §2.3).
            Modulate = ByteSpaceBlending.BuiltinColor(new Color(1f, 0.95f, 0.75f)),
            OutlineModulate = ByteSpaceBlending.BuiltinColor(new Color(0f, 0f, 0f, 0.85f)), // black: unchanged, but by the same rule
            NoDepthTest = true,
            Visible = false,
        };
        AddChild(_words);

        _camera = new OrbitCamera { Name = "Camera", Current = true, Fov = 55f, Near = 0.05f, Far = 3000f };
        AddChild(_camera);
        // Default: from the south, looking north-north-east: the caster on the left facing the drudge on the right, the
        // thatched and timbered houses behind them.
        var (yaw, pitch, distance) = _options.Camera ?? (DefaultHeading - 75f, -9f, 13f);
        var focus = _options.Focus switch
        {
            "caster" => _stage.CasterFrame.Origin + new AcVector3(0, 0, 1.1f),
            "target" => _stage.TargetFrame.Origin + new AcVector3(0, 0, 0.9f),
            _ => StageFocus,
        };
        _camera.SetHome(yaw, pitch, distance, focus);
        _camera.InputEnabled = !_options.IsAutomated;
        _camera.ShiftRight = _options.Ui && !_options.Showcase ? 0.13f : 0f;
    }

    private void BuildHud()
    {
        _hud = new SpellViewerHud { Name = "Hud" };
        AddChild(_hud);
        _hud.Build(_stage.AllSpells, SpellStage.Showcase, _options, _options.Ui && !_options.Showcase);
        _hud.CastRequested += Cast;
        _hud.ReplayRequested += Replay;
        _hud.SpeedChanged += s => _speed = s;
        _hud.DrudgeToggled += on => _showDrudge = on;
        _hud.StaticsToggled += on =>
        {
            _statics?.SetVisible(on);
            if (on && _statics is null)
            {
                StartStatics();
            }
        };
        _hud.ParticlesToggled += on => _showParticles = on;
        _hud.PauseToggled += on => _paused = on;
        _hud.SetCaption("Spell viewer", "Pick a spell and press Cast");
    }

    private void StartStatics()
    {
        // Holtburg's 3×3 landblocks through the objects path the world uses (statics, buildings and scenery with retail LOD
        // and camera-facing levels, and living statics: the lamp post by the stage burns), docs/objects.md §2.5. Prepared
        // on workers, attached in budgeted steps by StepStatics.
        var roots = new List<(LandblockCoord, Node3D)>();
        foreach (var dy in new[] { -1, 0, 1 })
        {
            foreach (var dx in new[] { -1, 0, 1 })
            {
                if (Center.Offset(dx, dy) is not { } coord)
                {
                    continue;
                }

                var root = new Node3D { Name = $"Objects_{coord.X:X2}{coord.Y:X2}", Position = LandblockOrigin(coord) };
                AddChild(root);
                roots.Add((coord, root));
            }
        }

        _statics = new StaticObjectsView(_services, roots);
    }

    /// <summary>
    /// Advances the objects' preparation and runs their attach steps while the frame has spent less than 3 ms on them (a
    /// step is never split, so one slow step can overrun the budget; the slowest is logged), then their LOD.
    /// </summary>
    private void StepStatics()
    {
        if (_statics is not { } statics)
        {
            return;
        }

        var wasReady = statics.IsReady;
        statics.Process(_camera.GlobalPosition, 3.0);
        _staticsStepMaxMs = statics.SlowestStepMs;
        if (!wasReady && statics.IsReady)
        {
            Log.Info("spellviewer", "statics attached", ("frames", _frames), ("objects", statics.Objects), ("slowest_step_ms", _staticsStepMaxMs));
        }
    }

    private void DrawStage()
    {
        var viewer = _camera.AcPosition;
        _onStage.Clear();
        foreach (var a in _stage.Actors)
        {
            switch (a.Role)
            {
                case StageRole.Caster:
                    _casterView.UpdatePose(a.Actor, viewer, a.Hidden);
                    break;
                case StageRole.Target:
                    _targetView.UpdatePose(a.Actor, viewer, a.Hidden || !_showDrudge);
                    break;
                case StageRole.Projectile:
                    var key = (a.Visual.SetupId, a.Index);
                    if (!_projectileViews.TryGetValue(key, out var view))
                    {
                        view = new ActorView();
                        view.Initialize(_services, a.Visual.SetupId, _actorMeshes);
                        _projectileRoot.AddChild(view);
                        _projectileViews.Add(key, view);
                    }

                    view.Visible = true;
                    view.UpdatePose(a.Actor, viewer, a.Hidden);
                    _onStage.Add(key);
                    break;
            }
        }

        // Views of projectiles that are not on stage any more (a new cast, or fewer projectiles).
        foreach (var (key, view) in _projectileViews)
        {
            if (!_onStage.Contains(key))
            {
                view.Visible = false;
            }
        }

        if (_showParticles)
        {
            _particles.UpdateParticles(_stage.EffectObjects, viewer);
            _maxParticleMs = Math.Max(_maxParticleMs, _particles.LastUpdateMs);
            _maxParticles = Math.Max(_maxParticles, _particles.DrawList.Stats.Drawn);
        }
        else
        {
            _particles.HideAll();
        }
    }

    private void UpdateTexts()
    {
        var sim = _stage.Simulation;
        if (sim is not null)
        {
            var events = sim.Events;
            for (; _eventsSeen < events.Count; _eventsSeen++)
            {
                var e = events[_eventsSeen];
                _hud.AddLog($"{e.Time - _stage.CastTime,6:0.00}s  {ChatLine(e.Text)}");
            }
        }

        // Spell words over the caster's head for 3 s (a viewer aid; retail shows them as chat text).
        if (_stage.SpokenWords is { } spoken && _stage.Time - spoken.Time < 3.0)
        {
            var head = _stage.CasterFrame.Origin + new AcVector3(0, 0, 2.15f);
            _words.Position = AcToGodot.Position(head);
            _words.Text = spoken.Words;
            _words.Visible = true;
        }
        else
        {
            _words.Visible = false;
        }

        if (sim is not null)
        {
            var spell = sim.Visual.Spell;
            _hud.SetCaption(
                _stage.Fizzling ? $"{spell.Name} (fizzle)" : spell.Name,
                $"{SpellViewerHud.SchoolName(spell.School)} · \"{sim.Visual.Plan.Words}\"");
        }

        if (_frames % 6 == 0)
        {
            _hud.SetInfo(InfoText());
        }
    }

    private string InfoText()
    {
        var stats = _particles.DrawList.Stats;
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"FPS {Engine.GetFramesPerSecond():0}   speed {_speed:0.00}×{(_paused ? "   PAUSED" : string.Empty)}"),
            string.Create(CultureInfo.InvariantCulture, $"Particles {stats.Drawn} drawn / {stats.Live} live, {_particles.DrawnBatches} batches, {_particles.LastUpdateMs:0.00} ms"),
            string.Create(CultureInfo.InvariantCulture, $"Draw calls {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)}"),
        };
        if (_stage.Simulation is { } sim)
        {
            var v = sim.Visual;
            var spell = v.Spell;
            var gestures = string.Join(" → ", v.Plan.Steps.Where(s => s.Motion is not (0 or Lifestone.Data.Animation.MotionCommands.RetailInvalid)).Select(s => $"{Lifestone.Data.Animation.MotionCommands.Name(s.Motion)} ×{s.Speed}"));
            lines.Add(string.Empty);
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{spell.Name}  (#{spell.Id}, {SpellViewerHud.SchoolName(spell.School)}, power {spell.Power})"));
            lines.Add($"Words: {v.Plan.Words}");
            lines.Add($"Gestures: {gestures}");
            lines.Add($"Caster effect: {Describe(v.CasterEffect)}");
            lines.Add($"Target effect: {Describe(v.TargetEffect)}");
            if (v.Projectile is { } p)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Projectile: {p.Kind}, Setup 0x{p.Source.SetupId:X8} × {p.Source.NumProjectiles ?? 1}, {p.Speed:0} m/s, intensity {p.ScriptIntensity:0.0}"));
            }

            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"t = {_stage.Time - _stage.CastTime:0.00} s   (effect at {sim.CastStartTime - _stage.CastTime + v.Plan.EffectTime:0.00} s)"));
        }

        return string.Join('\n', lines);
    }

    private static string Describe(ResolvedScript? script) =>
        script is null ? "none" : $"{script.Type} @{script.Intensity:0.00} → 0x{script.ScriptId:X8}";

    /// <summary>The simulation's log as chat-like lines ("says" lines become retail's quoted speech form).</summary>
    private static string ChatLine(string text)
    {
        var says = text.IndexOf(" says \"", StringComparison.Ordinal);
        return says >= 0 ? $"{text[..says]} says, {text[(says + 6)..]}" : text;
    }

    private void Automation()
    {
        if (_cinematic is { Finished: true } cinematic) // hero video tooling
        {
            Finish(cinematic.ExitCode);
            return;
        }

        if (_done || _director is not null)
        {
            if (_director is { Finished: true } director)
            {
                Finish(director.ExitCode);
            }

            return;
        }

        if (_options.ScreenshotPath is not { } path || !StaticsReady || _pendingShot is not null || _finishAfterShot)
        {
            return;
        }

        if (_options.ShotTimes.Count == 0)
        {
            _framesReady++;
            if (_framesReady >= _options.Frames)
            {
                _pendingShot = path;
                _finishAfterShot = true;
            }

            return;
        }

        // Shot k shows the first frame whose stage time is at least ShotTimes[k] after the cast (fixed 1/30 s steps); it
        // is saved at the start of the next frame, once it has been drawn.
        var sinceCast = _stage.Simulation is null ? -1 : _stage.Time - _stage.CastTime;
        if (_shotIndex < _options.ShotTimes.Count && sinceCast >= _options.ShotTimes[_shotIndex] - 1e-9)
        {
            var t = _options.ShotTimes[_shotIndex];
            _pendingShot = $"{path}-{t.ToString("0.00", CultureInfo.InvariantCulture)}.png";
            _shotIndex++;
            _finishAfterShot = _shotIndex >= _options.ShotTimes.Count;
        }
    }

    /// <summary>Exact retail ground height at a 0xA9B4-local (x, y) (any landblock of the 5×5).</summary>
    private float GroundHeight(float x, float y)
    {
        var global = new Lifestone.Client.World.GlobalPosition((Center.X * 192.0) + x, (Center.Y * 192.0) + y, 0);
        return _services.Ground.TryGetHeight(global, out var h) ? h : 0f;
    }

    private static Vector3 LandblockOrigin(LandblockCoord coord) =>
        AcToGodot.Position(new AcVector3((coord.X - Center.X) * TerrainConstants.LandblockSize, (coord.Y - Center.Y) * TerrainConstants.LandblockSize, 0f));

    /// <summary>Midpoint between caster and target, 1.1 m up.</summary>
    public AcVector3 StageFocus => ((_stage.CasterFrame.Origin + _stage.TargetFrame.Origin) * 0.5f) + new AcVector3(0, 0, 1.1f);

    /// <summary>Points the camera (director).</summary>
    public void SetCamera(float yaw, float pitch, float distance, AcVector3 focus)
    {
        _camera.Yaw = yaw;
        _camera.Pitch = pitch;
        _camera.Distance = distance;
        _camera.Focus = focus;
        _camera.Apply();
    }
}

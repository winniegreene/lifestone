using Godot;
using Lifestone.Client.Sky;
using Lifestone.Client.World;
using Lifestone.Data.World.Sky;
using Lifestone.Game.Core;
using Lifestone.Game.Player;
using Lifestone.Game.Render;

namespace Lifestone.Game.World.Sky;

/// <summary>
/// Drives time of day: advances the Dereth clock, evaluates the Region sky every frame, lights and fogs the world through
/// the render contract's global uniforms on retail's cadence, and draws the sky objects.
/// </summary>
/// <remarks>
/// <para>Runs after the camera and the world's rebase (process priority 20), so the sky anchor uses this frame's camera
/// position and floating origin.</para>
/// <para><b>Background.</b> Retail clears every frame to black (<c>SceneTool::BeginScene</c>, acclient.c:122402:
/// <c>Clear(7, 0x00000000, 1.0)</c>), draws the sky, then the world over it. With sky objects the main view's background
/// is the sky view (<see cref="SkyView"/>, black where no sky object covers it); without them (<c>--sky=off</c>) it is
/// the fog colour, as the core set it.</para>
/// <para>The game clock is mirrored into <see cref="GameServices.Clock"/> (as retail's <c>TimeZeroDelta</c> offset), so
/// everything that reads <see cref="DerethClock.Now"/> sees the same time.</para>
/// </remarks>
public partial class SkyController : Node
{
    private GameServices _services = null!;
    private WorldRoot _world = null!;
    private FlyCamera _camera = null!;
    private WorldEnvironment? _environment;
    private long _discontinuities;
    private int _preloadedGroup = -1;
    private int _lastGroup = -1;
    private Func<GlobalPosition, Vector3> _toRender = null!;

    /// <summary>The game clock.</summary>
    public DerethTimeController Time { get; private set; } = null!;

    /// <summary>The sky logic.</summary>
    public SkyDirector Director { get; private set; } = null!;

    /// <summary>The sky object renderer, or null with <c>--sky=off</c>.</summary>
    public SkyRenderer? Renderer { get; private set; }

    public SkyOptions Options { get; private set; } = new();

    /// <summary>The last frame's sky (null before the first frame).</summary>
    public SkyFrame? Frame => Director?.Last;

    /// <summary>The day group shown now.</summary>
    public int DayGroup => Frame?.DayGroup.Index ?? 0;

    /// <summary>True once the sky objects of the current frame are built (always true without sky objects).</summary>
    public bool IsReady => Frame is not null && (Renderer is null || (Renderer.AllReady && Renderer.Models.Pending == 0));

    public void Initialize(GameServices services, WorldRoot world, FlyCamera camera, SkyOptions options, WorldEnvironment? environment)
    {
        _services = services;
        _world = world;
        _camera = camera;
        _environment = environment;
        _toRender = world.ToRender;
        Options = options;
        ProcessPriority = 20;

        var sky = services.Sky;
        Director = new SkyDirector(sky)
        {
            PartResolver = PartsOf,
        };
        Director.Objects.WeatherEnabled = options.Weather;
        if (options.DayGroup is { } group)
        {
            if (group < sky.Sky.DayGroups.Count)
            {
                Director.ForcedDayGroup = group;
            }
            else
            {
                Log.Warn("sky", "--day-group out of range; using retail's day group", ("day_group", group), ("count", sky.Sky.DayGroups.Count));
            }
        }

        // Interactive runs: the live clock at retail speed. Automated runs: paused on the first Sunny (day group 0) day
        // from 10 P.Y. at --time-of-day (default Midsong), whatever the real date (SkyClockStart, docs/sky.md §2).
        var automated = services.Options.IsAutomated || options.GoldenDirectory is not null || options.SkyChecks;
        var start = SkyClockStart.Resolve(automated, options.GameTime, options.TimeOfDay, options.Speed, sky);
        Time = new DerethTimeController(sky.Calendar, LiveClock, start.GameTime, start.Speed);
        if (start.DayFraction is { } fraction)
        {
            Time.SetDayFraction(fraction);
        }

        _discontinuities = Time.Discontinuities;
        if (options.SkyObjects)
        {
            var shaders = new SkyShaders();
            WarmUp(shaders);
            var models = SkyTrack.TakePrefetched() ?? new SkyModels(services); // wave-3 perf: prepared since Main's start
            var loadMs = models.LoadAllNow(SkyDirector.ModelUses(sky.Sky).Select(u => u.Id));
            Log.Info("sky", "sky models loaded", ("models", models.Ready), ("ms", loadMs));
            Renderer = new SkyRenderer();
            Renderer.Initialize(models, shaders, services.Render.Materials);
            AddChild(Renderer);

            // M2: the sky view draws only while the 3D world does (portal space hides it, WorldVisibility); it starts again
            // with the world's first frame.
            _worldVisibility = services.WorldVisibility;
            _worldVisibility.Changed += OnWorldVisibilityChanged;
            Renderer.View.Paused = !_worldVisibility.Visible;
            if (_environment?.Environment is { } mainEnvironment)
            {
                mainEnvironment.BackgroundMode = Godot.Environment.BGMode.Sky;
                mainEnvironment.Sky = Renderer.Background;
            }
        }

        var date = Time.Date;
        Log.Info(
            "sky",
            "time of day started",
            ("date", date.DateString),
            ("time", date.TimeString),
            ("game_time", Time.GameTime),
            ("speed", Time.Speed),
            ("day_group", Director.DayGroupAt(Time.GameTime)),
            ("forced", Director.ForcedDayGroup?.ToString() ?? "no"),
            ("sky_objects", options.SkyObjects),
            ("anchor", options.Anchor));
        Update(0.0);
    }

    public override void _Process(double delta)
    {
        if (Time is not null)
        {
            Update(delta);
        }
    }

    /// <summary>Retail game time of the live server clock now (ACE's clock; docs/sky.md).</summary>
    public static double LiveClock() => DerethClock.FromRealTime(DateTimeOffset.UtcNow);

    private double _lastApplyMs;
    private long _allocFrames;
    private long _allocFramesNonZero;
    private long _allocBytes;
    private long _allocMaxBytes;
    private long _allocSteadyNonZero;
    private long _allocFramesWithGc;
    private long _allocBytesWithGc;

    private WorldVisibility? _worldVisibility;

    private void OnWorldVisibilityChanged(bool visible)
    {
        if (Renderer is not null)
        {
            Renderer.View.Paused = !visible;
        }
    }

    public override void _ExitTree()
    {
        if (_worldVisibility is not null)
        {
            _worldVisibility.Changed -= OnWorldVisibilityChanged;
            _worldVisibility = null;
        }

        if (_allocFrames > 0)
        {
            // docs/sky.md §7: the per-frame sky update should allocate nothing while the clock runs.
            // "Steady" frames: no jump, no change of day group, no sky object created, deleted or re-shaded, no start-up warm-up.
            Log.Info("sky", "per-frame allocations", ("frames", _allocFrames), ("frames_allocating", _allocFramesNonZero), ("steady_frames_allocating", _allocSteadyNonZero),
                ("bytes", _allocBytes), ("max_bytes", _allocMaxBytes), ("frames_with_gc_inside", _allocFramesWithGc), ("bytes_in_gc_frames", _allocBytesWithGc));
        }
    }

    private long _lastUpdateTimestamp;

    private void Update(double delta)
    {
        var debug = Log.MinimumLevel <= LogLevel.Debug;

        // Wave-3 performance pass: "ms" is Godot's process delta, the previous frame's length as the engine measured and
        // smoothed it; at start-up it carries the whole of Main._Ready (about 150 ms, the excess spread over the next
        // deltas), so it is not a frame of that length. "wall_ms" is this controller's own stopwatch since its previous
        // update (docs/sky.md §4.3).
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var wallMs = _lastUpdateTimestamp == 0 ? 0.0 : System.Diagnostics.Stopwatch.GetElapsedTime(_lastUpdateTimestamp, now).TotalMilliseconds;
        _lastUpdateTimestamp = now;
        if (debug && delta > 0.033 && Frame is { } previous)
        {
            // Diagnostics: what the sky did in the frame before a slow one (docs/sky.md §7).
            Log.Debug("sky", "slow frame", ("ms", delta * 1000.0), ("wall_ms", wallMs), ("sky_apply_ms", _lastApplyMs), ("objects_changed", previous.ObjectsChanged),
                ("created", Renderer?.CreatedThisFrame ?? 0), ("shader_swaps", Renderer?.ShaderSwapsThisFrame ?? 0));
        }

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var gcBefore = GC.CollectionCount(0);
        Time.Advance(delta);
        var jumped = Time.Discontinuities != _discontinuities;
        _discontinuities = Time.Discontinuities;
        var frame = Director.Update(Time.GameTime, Time.ClientClock, jumped);
        if (frame.LightingChanged)
        {
            RenderGlobals.ApplyLighting(frame.Lighting);
        }

        if (frame.FogChanged)
        {
            RenderGlobals.ApplyFog(frame.Fog);
            if (Renderer is null && _environment?.Environment is { } environment)
            {
                environment.BackgroundColor = RenderGlobals.FogBackground(frame.Fog); // blend-space aware (render-fidelity fix)
            }
        }

        _services.Clock.TimeZeroDelta = Time.LiveOffset;

        var group = frame.DayGroup.Index;
        var groupChanged = group != _lastGroup;
        _lastGroup = group;
        if (group != _preloadedGroup && Renderer is not null)
        {
            _preloadedGroup = group;
            Renderer.Preload(Director, group);
            if (debug)
            {
                Log.Debug("sky", "day group", ("index", group), ("name", frame.DayGroup.Name));
            }
        }

        var applied = false;
        if (Renderer is not null && _world.IsInitialized)
        {
            var viewer = _camera.WorldPosition;
            var anchor = AnchorFor(Options.Anchor, viewer);
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            Renderer.Apply(Director, _toRender, anchor, viewer, _camera.Camera, (Vector2I)GetViewport().GetVisibleRect().Size);
            var ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _lastApplyMs = ms;
            applied = true;
        }

        // Measured before any debug logging below, which allocates.
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (debug && applied && (_lastApplyMs > 2.0 || frame.ObjectsChanged))
        {
            Log.Debug("sky", "sky objects updated", ("ms", _lastApplyMs), ("objects_changed", frame.ObjectsChanged), ("instances", Renderer!.InstanceCount),
                ("created", Renderer.CreatedThisFrame), ("shader_swaps", Renderer.ShaderSwapsThisFrame), ("time", frame.Date.TimeString), ("day_fraction", frame.TimeOfDay));
        }

        // Frames in which a GC ran (usually triggered by another thread) are kept apart, bytes included, so that the steady
        // count reflects the sky's own code.
        if (GC.CollectionCount(0) != gcBefore)
        {
            _allocFramesWithGc++;
            _allocBytesWithGc += Math.Max(0, allocated);
            if (debug)
            {
                Log.Debug("sky", "gc inside the sky update", ("counted_bytes", allocated), ("objects_changed", frame.ObjectsChanged));
            }
        }
        else if (delta > 0.0) // not the first update (Initialize), which builds everything
        {
            _allocFrames++;
            _allocBytes += allocated;
            if (allocated > 0)
            {
                _allocFramesNonZero++;
                _allocMaxBytes = Math.Max(_allocMaxBytes, allocated);
                var steady = !frame.ObjectsChanged && !jumped && !groupChanged && (Renderer is null || (Renderer.CreatedThisFrame == 0 && Renderer.ShaderSwapsThisFrame == 0 && !Renderer.WarmingThisFrame));
                if (steady)
                {
                    _allocSteadyNonZero++;
                    if (debug)
                    {
                        Log.Debug("sky", "steady frame allocated", ("bytes", allocated), ("day_group", group), ("day_fraction", frame.TimeOfDay));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Where the non-weather sky objects sit for a viewer (docs/sky.md §4.2). Retail: the viewer's landblock corner at
    /// height 0 (<c>GameSky::UseTime</c> builds every frame with origin 0, <c>GameSky::UpdatePosition</c> keeps it,
    /// <c>PrimD3DRender::positionPushInternal</c> places it in the viewer's block). Follow (default): the viewer's x
    /// and y at that same height, so nothing jumps at a landblock edge. Camera: the viewer itself.
    /// </summary>
    public static GlobalPosition AnchorFor(SkyAnchor mode, GlobalPosition viewer) => mode switch
    {
        SkyAnchor.Camera => viewer,
        SkyAnchor.Follow => new GlobalPosition(viewer.X, viewer.Y, 0.0), // retail's height: a landblock's origin is at z = 0
        _ => AcCoordinates.LandblockOrigin(AcCoordinates.ClampedLandblockAt(viewer)),
    };

    /// <summary>Jumps the clock (UI, golden viewpoints).</summary>
    public void SetGameTime(double gameTime) => Time.SetGameTime(gameTime);

    /// <summary>Forces a day group (null = retail's choice).</summary>
    public void SetDayGroup(int? group) => Director.ForcedDayGroup = group;

    /// <summary>
    /// Compiles every shader variant the Region's sky objects can need (all day groups, with and without a translucent
    /// part material) before the first frame, as <c>RenderResources.WarmUp</c> does for world surfaces.
    /// </summary>
    private void WarmUp(SkyShaders shaders)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var uses = new List<(Lifestone.Data.Textures.SurfaceMaterial, Lifestone.Data.Geometry.TextureAddressMode, SkyPass)>();
        var models = 0;
        foreach (var (id, pass) in SkyDirector.ModelUses(Director.Sky.Sky))
        {
            models++;
            foreach (var part in PartsOf(id))
            {
                if (_services.GfxObjMeshes.Get(part) is not { } mesh)
                {
                    continue;
                }

                foreach (var sub in mesh.Mesh.SubMeshes)
                {
                    if (_services.Textures.GetSurface(sub.SurfaceId) is { } surface)
                    {
                        uses.Add((surface, sub.Addressing, pass));
                    }
                }
            }
        }

        var compiled = shaders.WarmUp(SkySurfaceVariant.Reachable(uses));
        Log.Info("sky", "sky shaders warmed up", ("models", models), ("surfaces", uses.Count), ("variants", compiled), ("ms", timer.Elapsed.TotalMilliseconds));
    }

    private IEnumerable<uint> PartsOf(uint id) =>
        (id & 0xFF000000u) == 0x01000000u
            ? [id]
            : _services.Setups.Get(id)?.Parts.Where(p => p.IsGfxObj).Select(p => p.GfxObjId) ?? [];
}

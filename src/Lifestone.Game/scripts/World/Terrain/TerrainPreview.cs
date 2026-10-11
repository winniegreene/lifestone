using System.Collections.Concurrent;
using System.Diagnostics;
using Godot;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Scenery;
using Lifestone.Data.World.Sky;
using Lifestone.Game.Core;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.World.Terrain;

/// <summary>
/// scenes/TerrainPreview.tscn: N × N landblocks of retail-textured terrain around a chosen landblock, lit by
/// <see cref="DerethSky"/> at a fixed time of day, seen from an oblique camera. Also hosts the golden test
/// (<see cref="TerrainGoldenRunner"/>, <c>--golden</c>). Options: <see cref="TerrainPreviewOptions"/>.
/// </summary>
/// <remarks>
/// Interactive keys (when not taking a screenshot): arrows orbit, PageUp/PageDown or the wheel zoom, 0-6 switch
/// <see cref="TerrainDebugView"/>, M toggles the retail mip clamp (3) against Godot's full chain, F toggles fog.
/// Landblocks are placed relative to the centre landblock's south-west corner (a landblock-granular floating origin).
/// </remarks>
public partial class TerrainPreview : Node3D
{
    private TerrainPreviewOptions _options = new();
    private DatSet? _dats;
    private TerrainRenderer? _renderer;
    private SkyState? _sky;
    private Camera3D? _camera;
    private AcVector3 _target;
    private float _distance;
    private float _pitch;
    private float _yaw;
    private int _frames;
    private bool _done;
    private bool _fog = true;
    private readonly List<LandblockCoord> _coords = new();
    private readonly List<double> _gpuMs = new();
    private readonly List<double> _cpuMs = new();

    public override void _Ready()
    {
        _options = TerrainPreviewOptions.Parse(OS.GetCmdlineUserArgs());
        GD.Print($"[terrain-preview] Godot {Engine.GetVersionInfo()["string"]}, renderer '{RenderingServer.GetCurrentRenderingDriverName()}' " +
                 $"on '{RenderingServer.GetVideoAdapterName()}'");
        try
        {
            // infra (M1 wave 3): no process-environment write and no second DatSet. The GameServices autoload (loaded for
            // every scene) opened the dats from the same --dat-dir before this scene's _Ready.
            _dats = GameServices.Instance.Dats ?? throw new InvalidOperationException(GameServices.Instance.StartupError ?? "the retail dats are not open");
            _sky = _dats.Sky.EvaluateAtTimeOfDay(_options.DayGroup, _options.TimeOfDay);
            var atlasTimer = Stopwatch.StartNew();
            var prepared = TerrainRenderer.PrepareAtlas(_dats);
            var atlasWorker = atlasTimer.Elapsed.TotalMilliseconds;
            // The golden test's CPU reference needs the decoded atlas; otherwise the renderer drops it after the upload.
            _renderer = new TerrainRenderer(
                _dats,
                prepared,
                _options.SeamlessNormals ? TerrainNormalMode.SeamlessAcrossBlocks : TerrainNormalMode.RetailPerBlock,
                keepAtlasPixels: _options.GoldenDirectory is not null);
            _renderer.DebugView = _options.DebugView;
            _fog = _options.Fog;

            // Lighting, fog and the mip cap are the render contract's global uniforms (RenderGlobals), as in the game.
            TerrainRenderer.ApplySky(_sky, _fog);
            TerrainRenderer.MaxMipLevel = _options.MaxMip;
            var atlas = _renderer.AtlasSummary;
            GD.Print($"[terrain-perf] atlas: {atlas.ColorLayers} colour layers {atlas.ColorSize}² + " +
                     $"{atlas.AlphaLayers} alpha layers {atlas.AlphaSize}², decode+mips+images {atlas.DecodeMilliseconds:0} ms, " +
                     $"worker total {atlasWorker:0} ms, upload {_renderer.UploadMilliseconds:0.0} ms + material/warm-up {_renderer.MaterialMilliseconds:0.0} ms (main), " +
                     $"VRAM colour {_renderer.Textures.ColorBytes / 1048576.0:0.0} MiB + alpha {_renderer.Textures.AlphaBytes / 1048576.0:0.00} MiB, " +
                     $"CPU atlas copy {(_renderer.AtlasPixels is null ? "dropped" : "kept (golden)")}");

            if (_options.GoldenDirectory is not null)
            {
                AddChild(new TerrainGoldenRunner(_renderer, _sky, _options));
                _done = true;
                return;
            }

            BuildLandscape();
            AddEnvironment();
            AddCamera();
            if (_options.Perf)
            {
                MeasureWarmBuild();
                RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
            }
        }
        catch (Exception ex)
        {
            GD.PushError($"[terrain-preview] failed: {ex}");
            _done = true;
            GetTree().Quit(1);
        }
    }

    public override void _ExitTree()
    {
        _renderer?.Dispose();
        _dats = null; // GameServices' DatSet: the autoload closes it
    }

    public override void _Process(double delta)
    {
        if (_camera is not null)
        {
            HandleKeys((float)delta);
        }

        if (_done)
        {
            return;
        }

        _frames++;
        if (_options.Perf && _frames > 5)
        {
            var rid = GetViewport().GetViewportRid();
            _gpuMs.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid));
            _cpuMs.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid));
        }

        if (_options.Perf && _frames == _options.Frames - 1)
        {
            LogRenderStats();
        }

        if (_options.ScreenshotPath is null || _frames < _options.Frames)
        {
            return;
        }

        _done = true;
        GetTree().Quit(SaveScreenshot(_options.ScreenshotPath) ? 0 : 1);
    }

    private void BuildLandscape()
    {
        var renderer = _renderer!;
        var center = _options.Center;
        var radius = _options.Size / 2;
        var coords = _coords;
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (center.Offset(dx, dy) is { } c)
                {
                    coords.Add(c);
                }
            }
        }

        // Worker threads prepare (dat reads serialise on the DatSet lock); the main thread attaches.
        var wall = Stopwatch.StartNew();
        var prepared = new ConcurrentBag<PreparedTerrainBlock>();
        Parallel.ForEach(coords, c =>
        {
            if (renderer.PrepareBlock(c) is { } block)
            {
                prepared.Add(block);
            }
        });
        var prepareWall = wall.Elapsed.TotalMilliseconds;

        var root = new Node3D { Name = "Terrain" };
        AddChild(root);
        var attachTimes = new List<double>();
        foreach (var block in prepared.OrderBy(b => b.Coord.X).ThenBy(b => b.Coord.Y))
        {
            var t = Stopwatch.StartNew();
            var landblockRoot = new Node3D
            {
                Name = $"Landblock_{block.Coord.X:X2}{block.Coord.Y:X2}",
                Position = AcToGodot.Position(new AcVector3(
                    (block.Coord.X - center.X) * TerrainConstants.LandblockSize,
                    (block.Coord.Y - center.Y) * TerrainConstants.LandblockSize,
                    0f)),
            };
            root.AddChild(landblockRoot);
            renderer.AttachBlock(block, landblockRoot);
            attachTimes.Add(t.Elapsed.TotalMilliseconds);
        }

        var prep = prepared.Select(b => b.PrepareMilliseconds).OrderBy(x => x).ToArray();
        GD.Print($"[terrain-perf] {prepared.Count} landblocks around {center} ({_options.Size}x{_options.Size}): prepare wall {prepareWall:0.0} ms " +
                 $"on {System.Environment.ProcessorCount} threads, per block CPU median {Median(prep):0.00} ms max {prep.LastOrDefault():0.00} ms; " +
                 $"attach (ArrayMesh + upload, main) median {Median(attachTimes.OrderBy(x => x).ToArray()):0.000} ms " +
                 $"max {attachTimes.DefaultIfEmpty().Max():0.000} ms total {attachTimes.Sum():0.0} ms; " +
                 $"recipes cached {renderer.TexMerge.CachedCount}");

        // Camera target: the requested point (default the centre landblock's middle) at terrain height.
        var look = _options.LookAt ?? new System.Numerics.Vector2(96f, 96f);
        _target = new AcVector3(look.X, look.Y, GroundHeight(look.X, look.Y));
        _distance = _options.Distance ?? (_options.Size * TerrainConstants.LandblockSize * 0.45f);
        _pitch = _options.Pitch;
        _yaw = _options.Yaw;
    }

    private float GroundHeight(float x, float y)
    {
        var bx = (int)MathF.Floor(x / TerrainConstants.LandblockSize);
        var by = (int)MathF.Floor(y / TerrainConstants.LandblockSize);
        if (_options.Center.Offset(bx, by) is { } coord && _dats!.GetLandblockTerrain(coord) is { } terrain)
        {
            var sampler = new TerrainSampler(terrain, _dats.TerrainRegion.LandHeightTable);
            return sampler.GetHeight(x - (bx * TerrainConstants.LandblockSize), y - (by * TerrainConstants.LandblockSize));
        }

        return 0f;
    }

    private void AddEnvironment()
    {
        // The render contract: no Godot lighting, Linear tonemap at exposure 1, clear to the fog colour (raw bytes shown as is).
        var fog = _sky!.Fog.Color;
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(fog.R / 255f, fog.G / 255f, fog.B / 255f),
            AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 1f,
        };
        AddChild(new WorldEnvironment { Name = "WorldEnvironment", Environment = environment });
    }

    private void AddCamera()
    {
        _camera = new Camera3D { Name = "Camera", Fov = _options.Fov, Near = 0.5f, Far = 8000f, Current = true };
        AddChild(_camera);
        PlaceCamera();
        GD.Print($"[terrain-preview] camera target AC ({_target.X:0},{_target.Y:0},{_target.Z:0}) distance {_distance:0} pitch {_pitch:0} yaw {_yaw:0}; " +
                 $"sky t={_sky!.TimeOfDay:0.000} sun {_sky.Lighting.SunVector} fog {(_sky.Fog.Enabled ? $"{_sky.Fog.Start:0}-{_sky.Fog.End:0} m" : "off")}");
    }

    private void PlaceCamera()
    {
        // Yaw = compass heading the camera looks towards (0 = north, 90 = east); pitch = degrees below the horizon.
        var yaw = Mathf.DegToRad(_yaw);
        var pitch = Mathf.DegToRad(Math.Clamp(_pitch, 1f, 89.9f));
        var forward = new AcVector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Cos(yaw) * MathF.Cos(pitch), -MathF.Sin(pitch));
        var eye = _target - (forward * _distance);
        _camera!.LookAtFromPosition(AcToGodot.Position(eye), AcToGodot.Position(_target), Vector3.Up);
    }

    private void HandleKeys(float delta)
    {
        var changed = false;
        if (Input.IsKeyPressed(Key.Left)) { _yaw -= 60f * delta; changed = true; }
        if (Input.IsKeyPressed(Key.Right)) { _yaw += 60f * delta; changed = true; }
        if (Input.IsKeyPressed(Key.Up)) { _pitch = Math.Min(89f, _pitch + (40f * delta)); changed = true; }
        if (Input.IsKeyPressed(Key.Down)) { _pitch = Math.Max(2f, _pitch - (40f * delta)); changed = true; }
        if (Input.IsKeyPressed(Key.Pageup)) { _distance = Math.Max(5f, _distance * (1f - delta)); changed = true; }
        if (Input.IsKeyPressed(Key.Pagedown)) { _distance *= 1f + delta; changed = true; }
        if (changed)
        {
            PlaceCamera();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_renderer is null || _camera is null)
        {
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true } wheel)
        {
            if (wheel.ButtonIndex == MouseButton.WheelUp) { _distance = Math.Max(5f, _distance * 0.9f); PlaceCamera(); }
            if (wheel.ButtonIndex == MouseButton.WheelDown) { _distance *= 1.1f; PlaceCamera(); }
        }

        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode is >= Key.Key0 and <= Key.Key6)
            {
                _renderer.DebugView = (TerrainDebugView)(key.Keycode - Key.Key0);
                GD.Print($"[terrain-preview] debug view {_renderer.DebugView}");
            }
            else if (key.Keycode == Key.M)
            {
                TerrainRenderer.MaxMipLevel = TerrainRenderer.MaxMipLevel <= 3f ? 16f : 3f;
                GD.Print($"[terrain-preview] max mip level {TerrainRenderer.MaxMipLevel}");
            }
            else if (key.Keycode == Key.F)
            {
                _fog = !_fog;
                TerrainRenderer.ApplySky(_sky!, _fog);
            }
        }
    }

    /// <summary>
    /// Warm, single-threaded timings (the first pass above includes JIT and dat-lock contention): per landblock the dat
    /// read, the render mesh (recipes, normals, packing) and the Godot conversion on the CPU, then the main-thread
    /// ArrayMesh creation (vertex/index buffer upload).
    /// </summary>
    private void MeasureWarmBuild()
    {
        var renderer = _renderer!;
        var read = new List<double>();
        var prepare = new List<double>();
        var upload = new List<double>();
        foreach (var coord in _coords)
        {
            var t = Stopwatch.StartNew();
            _ = _dats!.GetLandblockTerrain(coord);
            read.Add(t.Elapsed.TotalMilliseconds);
            t.Restart();
            var block = renderer.PrepareBlock(coord);
            prepare.Add(t.Elapsed.TotalMilliseconds);
            if (block is null)
            {
                continue;
            }

            t.Restart();
            var mesh = TerrainBlockMesh.Build(block, renderer.Material);
            upload.Add(t.Elapsed.TotalMilliseconds);
            mesh.Dispose();
        }

        static string Summary(List<double> values)
        {
            var sorted = values.OrderBy(x => x).ToArray();
            return sorted.Length == 0 ? "-" : $"median {Median(sorted):0.000} ms, max {sorted[^1]:0.000} ms";
        }

        GD.Print($"[terrain-perf] warm, single thread, {_coords.Count} landblocks: dat read {Summary(read)}; " +
                 $"PrepareBlock (read + mesh + recipes + normals + packing + conversion) {Summary(prepare)}; " +
                 $"ArrayMesh build+upload (main) {Summary(upload)}");
    }

    private void LogRenderStats()
    {
        static double Mean(List<double> values) => values.Count == 0 ? 0 : values.Average();
        GD.Print($"[terrain-perf] measured viewport render time over {_gpuMs.Count} frames: GPU {Mean(_gpuMs):0.000} ms, " +
                 $"CPU {Mean(_cpuMs):0.000} ms (mean)");
        GD.Print($"[terrain-perf] frame {_frames}: draw calls {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)}, " +
                 $"objects {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame)}, " +
                 $"primitives {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame)}, " +
                 $"texture mem {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed) / 1048576.0:0.0} MiB, " +
                 $"buffer mem {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.BufferMemUsed) / 1048576.0:0.0} MiB, " +
                 $"video mem {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / 1048576.0:0.0} MiB");
    }

    /// <summary>The shared screenshot rule (<see cref="Diagnostics.ScreenshotWriter"/>: repository-relative, never inside src/ or tests/).</summary>
    private bool SaveScreenshot(string path) => Diagnostics.ScreenshotWriter.Save(GetViewport(), path, "terrain-preview", ("frames", _frames));

    private static double Median(double[] sorted) => sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
}

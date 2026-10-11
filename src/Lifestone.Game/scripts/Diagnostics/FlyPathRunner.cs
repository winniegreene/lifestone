using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Godot;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.World;
using Lifestone.Game.Core;
using Lifestone.Game.Player;
using Lifestone.Game.World;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// <c>--fly-path=from:to:speed</c>: flies the camera in a straight line at a fixed speed, records every frame time,
/// and reports the maximum, the 99th percentile and hitches together with streaming throughput, then quits.
/// </summary>
/// <remarks>
/// <para>Phases: <b>settle</b> (camera at the start; wait until streaming is idle, at most 120 s, then 60 more
/// frames so start-up costs are not measured), <b>fly</b> (frame times recorded; the camera keeps
/// <see cref="Altitude"/> m above the exact ground, heading along the path, pitched 12° down), <b>arrive</b> (wait
/// until streaming is idle again, at most 60 s, then take the screenshot if one was asked for) and <b>report</b>.</para>
/// <para>The report goes to the log (<c>[flypath] report ...</c>) and to <c>user://logs/flypath-report.json</c> plus
/// <c>user://logs/flypath-frames.csv</c> (per frame: time, main-thread drain time, landblocks attached so far, GC
/// collections so far per generation, rebases so far, the GC pause inside the frame and the bytes allocated in it). The
/// M1 definition of done asks that no frame of this flight take more than 50 ms; the exit code says whether one did (0 no,
/// 2 yes, 1 error).</para>
/// <para><b>What is measured where</b> (wave-3 performance pass): the flight's figures (slowest main-thread item, GC
/// pauses, allocations) cover the flight only; the settle phase is reported separately as the start-up
/// (<c>[flypath] startup ...</c>: time from process start to the first frame and to streaming idle, its frames, GC and
/// allocations; <c>user://logs/flypath-startup.csv</c> per frame). After arrival the runner forces one full, compacting
/// collection and reports the live managed heap (<c>[flypath] memory ...</c>), so a run also gives the memory plateau.</para>
/// </remarks>
public partial class FlyPathRunner : Node
{
    /// <summary>Height of the flight above the ground under the camera, in metres.</summary>
    public const double Altitude = 60.0;

    /// <summary>The M1 hitch limit.</summary>
    public const double HitchLimitMs = 50.0;

    private const double SettleTimeoutSeconds = 120.0;
    private const double ArriveTimeoutSeconds = 60.0;
    private const int SettleFrames = 60;

    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };

    private readonly FrameTimeRecorder _frames = new();
    private readonly List<FrameSample> _samples = new();
    private readonly List<FrameSample> _startupSamples = new();
    private FlyPathSpec _spec = null!;
    private FlyCamera _camera = null!;
    private WorldRoot _world = null!;
    private Func<string, bool>? _screenshot;
    private string? _screenshotPath;
    private Phase _phase = Phase.Settle;
    private double _phaseTime;
    private int _settledFrames;
    private double _flown;
    private long _lastTimestamp;
    private long _attachedAtStart;
    private long _cancelledAtStart;
    private long _abandonedAtStart;
    private long _overrunsAtStart;
    private double _maxDrainMs;
    private int _rebasesAtStart;
    private int[] _gcAtStart = [0, 0, 0];
    private bool _withinHitchLimit;
    private TimeSpan _pauseAtStart;
    private long _allocatedAtStart;
    private TimeSpan _lastPause;
    private long _lastAllocated;
    private double? _firstFrameSeconds;
    private double? _idleSeconds;
    private StartupStats? _startup;
    private FlyPathReport? _report;

    private enum Phase
    {
        Settle,
        Fly,
        Arrive,
        Done,
    }

    /// <summary>The recorded frame times of the flight.</summary>
    public FrameTimeRecorder Frames => _frames;

    public void Initialize(FlyPathSpec spec, FlyCamera camera, WorldRoot world, string? screenshotPath, Func<string, bool> screenshot)
    {
        _spec = spec;
        _camera = camera;
        _world = world;
        _screenshotPath = screenshotPath;
        _screenshot = screenshot;
        _camera.InputEnabled = false;
        PlaceCamera(0.0);
        Log.Info(
            "flypath",
            "fly path armed",
            ("from", spec.From.Name),
            ("to", spec.To.Name),
            ("length_m", spec.HorizontalLength),
            ("speed", spec.SpeedMetresPerSecond),
            ("seconds", spec.DurationSeconds),
            ("heading", spec.HeadingDegrees));
    }

    public override void _Process(double delta)
    {
        var now = Stopwatch.GetTimestamp();
        var frameMs = _lastTimestamp == 0 ? 0.0 : (now - _lastTimestamp) * 1000.0 / Stopwatch.Frequency;
        _lastTimestamp = now;
        _phaseTime += delta;
        var services = GameServices.Instance;

        // GC pause and allocation since the previous frame (process-wide: workers included).
        var pause = GC.GetTotalPauseDuration();
        var allocated = GC.GetTotalAllocatedBytes(precise: false);
        var pauseMs = (pause - _lastPause).TotalMilliseconds;
        var allocatedKb = (allocated - _lastAllocated) / 1024.0;
        _lastPause = pause;
        _lastAllocated = allocated;
        switch (_phase)
        {
            case Phase.Settle:
                _firstFrameSeconds ??= SecondsSinceProcessStart();
                if (frameMs > 0)
                {
                    _startupSamples.Add(Sample(frameMs, services, pauseMs, allocatedKb, [0, 0, 0], 0, 0));
                }

                if (_world.Streamer.IsIdle)
                {
                    _settledFrames++;
                    if (_idleSeconds is null && services.MainThread.Pending == 0)
                    {
                        _idleSeconds = SecondsSinceProcessStart();
                        _startup = StartupReport(services, pause, allocated);
                    }
                }

                if (_settledFrames >= SettleFrames || _phaseTime > SettleTimeoutSeconds)
                {
                    if (_phaseTime > SettleTimeoutSeconds)
                    {
                        Log.Warn("flypath", "streaming did not settle before the flight", ("seconds", _phaseTime));
                    }

                    _phase = Phase.Fly;
                    _phaseTime = 0;
                    _attachedAtStart = _world.Streamer.Stats.Attached;
                    _cancelledAtStart = _world.Streamer.Stats.Cancelled;
                    _abandonedAtStart = _world.Streamer.Stats.AbandonedMidAttach;
                    _world.Streamer.Stats.ResetPeaks();
                    _overrunsAtStart = services.MainThread.Scheduler.OverrunCount;
                    _rebasesAtStart = _world.Origin.RebaseCount;
                    _gcAtStart = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
                    _pauseAtStart = pause;
                    _allocatedAtStart = allocated;
                    _startup ??= StartupReport(services, pause, allocated);

                    // The flight's slowest item must not be the start-up's (terrain atlas upload, warm-ups).
                    services.MainThread.Scheduler.ResetPeaks();
                    Log.Info("flypath", "flight started", ("loaded", _world.Streamer.Planner.LoadedCount));
                }

                break;

            case Phase.Fly:
                // The first frame of the phase measures the frame that ended the settle phase; skip it.
                if (_phaseTime > delta)
                {
                    _frames.Add(frameMs);
                    _maxDrainMs = Math.Max(_maxDrainMs, services.LastDrainMs);
                    _samples.Add(Sample(frameMs, services, pauseMs, allocatedKb, _gcAtStart, _attachedAtStart, _rebasesAtStart));
                }

                _flown += _spec.SpeedMetresPerSecond * delta;
                PlaceCamera(Math.Min(1.0, _flown / _spec.HorizontalLength));
                if (_flown >= _spec.HorizontalLength)
                {
                    _phase = Phase.Arrive;
                    _phaseTime = 0;
                    Log.Info("flypath", "arrived", ("seconds", _frames.Samples.Sum() / 1000.0));
                    Report(services);
                }

                break;

            case Phase.Arrive:
                if (_world.Streamer.IsIdle || _phaseTime > ArriveTimeoutSeconds)
                {
                    _phase = Phase.Done;
                    WriteMemorySnapshot();
                    var ok = true;
                    if (_screenshotPath is not null && _screenshot is not null)
                    {
                        ok = _screenshot(_screenshotPath);
                    }

                    // 0 = within the DoD hitch limit, 2 = a frame took over 50 ms (tools\smoke.ps1 relies on it), 1 = error.
                    GetTree().Quit(!ok ? 1 : _withinHitchLimit ? 0 : 2);
                }

                break;
        }
    }

    private void PlaceCamera(double t)
    {
        var from = _spec.From.Global;
        var to = _spec.To.Global;
        var p = GlobalPosition.Lerp(from, to, t);
        var ground = GameServices.Instance.Ground.TryGetHeight(p, out var z) ? z : Math.Max(from.Z, to.Z);
        _camera.PlaceAt(p with { Z = ground + Altitude }, _spec.HeadingDegrees, -12.0);
    }

    private static double SecondsSinceProcessStart()
    {
        using var process = Process.GetCurrentProcess();
        return (DateTime.Now - process.StartTime).TotalSeconds;
    }

    private static string GcMode() => GameServices.GcMode;

    private FrameSample Sample(double frameMs, GameServices services, double pauseMs, double allocatedKb, int[] gcBase, long attachedBase, int rebasesBase) =>
        new(
            frameMs,
            services.LastDrainMs,
            _world.Streamer.Stats.Attached - attachedBase,
            GC.CollectionCount(0) - gcBase[0],
            GC.CollectionCount(1) - gcBase[1],
            GC.CollectionCount(2) - gcBase[2],
            _world.Origin.RebaseCount - rebasesBase,
            pauseMs,
            allocatedKb);

    /// <summary>Process start to the first frame and to streaming idle, with what it cost (settle phase).</summary>
    private StartupStats StartupReport(GameServices services, TimeSpan pause, long allocated)
    {
        var frames = _startupSamples;
        var stats = new StartupStats(
            _firstFrameSeconds ?? 0,
            _idleSeconds ?? SecondsSinceProcessStart(),
            frames.Count,
            frames.Count == 0 ? 0 : frames.Max(f => f.FrameMs),
            frames.Count(f => f.FrameMs > 33.4),
            frames.Count(f => f.FrameMs > HitchLimitMs),
            frames.Count == 0 ? 0 : frames.Max(f => f.DrainMs),
            services.MainThread.Scheduler.MaxItemMilliseconds,
            allocated / (1024.0 * 1024.0),
            $"{GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}",
            pause.TotalMilliseconds,
            frames.Count == 0 ? 0 : frames.Max(f => f.GcPauseMs),
            _world.Streamer.Planner.LoadedCount);
        Log.Info(
            "flypath",
            "startup",
            ("first_frame_s", stats.FirstFrameSeconds),
            ("idle_s", stats.IdleSeconds),
            ("frames", stats.Frames),
            ("max_frame_ms", stats.MaxFrameMs),
            ("over_33ms", stats.FramesOver33Ms),
            ("over_50ms", stats.FramesOver50Ms),
            ("max_drain_ms", stats.MaxDrainMs),
            ("slowest_item_ms", stats.SlowestMainThreadItemMs),
            ("allocated_mb", stats.AllocatedMb),
            ("gc", stats.Collections),
            ("gc_pause_ms", stats.GcPauseMs),
            ("max_frame_gc_pause_ms", stats.MaxFrameGcPauseMs),
            ("loaded", stats.Loaded),
            ("gc_mode", GcMode()),
            ("workers", services.Workers.MaxConcurrency));
        return stats;
    }

    /// <summary>
    /// After arrival: heap and process memory at the plateau, then one forced full compacting collection for the live
    /// managed heap. Logged and added to the report files (the flight's figures are already taken).
    /// </summary>
    private void WriteMemorySnapshot()
    {
        const double Mb = 1024.0 * 1024.0;
        var info = GC.GetGCMemoryInfo(GCKind.Any);
        double privateMb, workingSetMb;
        using (var process = Process.GetCurrentProcess())
        {
            privateMb = process.PrivateMemorySize64 / Mb;
            workingSetMb = process.WorkingSet64 / Mb;
        }

        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        var live = GC.GetTotalMemory(forceFullCollection: false);
        var memory = new MemoryStats(
            info.TotalCommittedBytes / Mb,
            info.HeapSizeBytes / Mb,
            info.FragmentedBytes / Mb,
            privateMb,
            workingSetMb,
            live / Mb,
            GC.GetGCMemoryInfo(GCKind.Any).TotalCommittedBytes / Mb,
            Performance.GetMonitor(Performance.Monitor.MemoryStatic) / Mb,
            RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / Mb);
        Log.Info(
            "flypath",
            "memory",
            ("gc_committed_mb", memory.GcCommittedMb),
            ("gc_heap_mb", memory.GcHeapMb),
            ("gc_fragmented_mb", memory.GcFragmentedMb),
            ("private_mb", memory.PrivateMb),
            ("working_set_mb", memory.WorkingSetMb),
            ("live_after_full_gc_mb", memory.LiveAfterFullGcMb),
            ("committed_after_full_gc_mb", memory.CommittedAfterFullGcMb),
            ("godot_static_mb", memory.GodotStaticMb),
            ("video_mb", memory.VideoMb));
        if (_report is not null)
        {
            _report = _report with { Memory = memory };
            WriteReportFiles(_report);
        }
    }

    private void Report(GameServices services)
    {
        var stats = _world.Streamer.Stats;
        var textures = services.Render.Textures.Stats;
        var materials = services.Render.Materials.Stats;
        var meshes = services.Render.Meshes;
        var seconds = _frames.Samples.Sum() / 1000.0;
        var attached = stats.Attached - _attachedAtStart;
        var report = new FlyPathReport(
            _spec.From.Name,
            _spec.To.Name,
            _spec.SpeedMetresPerSecond,
            _spec.HorizontalLength,
            _frames.Count,
            seconds,
            _frames.Average,
            _frames.Percentile(50),
            _frames.Percentile(99),
            _frames.Max,
            _frames.CountAbove(33.4),
            _frames.CountAbove(HitchLimitMs),
            attached,
            seconds > 0 ? attached / seconds : 0,
            stats.Cancelled - _cancelledAtStart,
            stats.AbandonedMidAttach - _abandonedAtStart,
            stats.MaxPrepareMs,
            stats.Attached > 0 ? stats.TotalAttachMs / stats.Attached : 0,
            stats.MaxAttachMs,
            stats.MaxAttachStepMs,
            services.MainThread.Scheduler.OverrunCount - _overrunsAtStart,
            _maxDrainMs,
            services.MainThread.Scheduler.MaxItemMilliseconds,
            _world.Origin.RebaseCount - _rebasesAtStart,
            GC.CollectionCount(0) - _gcAtStart[0],
            GC.CollectionCount(1) - _gcAtStart[1],
            GC.CollectionCount(2) - _gcAtStart[2],
            _world.Streamer.Planner.LoadedCount,
            textures.Count,
            textures.TotalBytes / (1024.0 * 1024.0),
            textures.IdleCount,
            textures.Evictions,
            materials.Count,
            materials.Evictions,
            meshes.Count,
            meshes.Evictions,
            RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed) / (1024.0 * 1024.0),
            DisplayServer.WindowGetVsyncMode().ToString(),
            RenderingServer.GetCurrentRenderingDriverName(),
            RenderingServer.GetVideoAdapterName(),
            _frames.Max <= HitchLimitMs)
        {
            Gc = FlightGc(services),
            Startup = _startup,
            Workers = services.Workers.MaxConcurrency,
            MaxFps = Engine.MaxFps,
            TextureDecodes = services.Render.Textures.SharedPathDecodes,
            TextureDecodesShared = services.Render.Textures.SharedPathHits,
            SlowestMainThreadItemWorkMs = services.MainThread.Scheduler.MaxItemWorkMilliseconds,
        };
        _withinHitchLimit = report.WithinHitchLimit;
        _report = report;

        Log.Info(
            "flypath",
            "report",
            ("frames", report.Frames),
            ("seconds", report.Seconds),
            ("avg_ms", report.AverageMs),
            ("p50_ms", report.P50Ms),
            ("p99_ms", report.P99Ms),
            ("max_ms", report.MaxMs),
            ("over_33ms", report.FramesOver33Ms),
            ("over_50ms", report.FramesOver50Ms),
            ("landblocks_attached", report.LandblocksAttached),
            ("landblocks_per_s", report.LandblocksPerSecond),
            ("cancelled", report.LandblocksCancelled),
            ("abandoned_mid_attach", report.AbandonedMidAttach),
            ("max_prepare_ms", report.MaxPrepareMs),
            ("avg_attach_ms", report.AverageAttachMs),
            ("max_attach_ms", report.MaxAttachMs),
            ("max_attach_step_ms", report.MaxAttachStepMs),
            ("budget_overruns", report.MainThreadOverruns),
            ("max_drain_ms", report.MaxDrainMs),
            ("rebases", report.Rebases),
            ("slowest_item_in_flight_ms", report.SlowestMainThreadItemMs),
            ("slowest_item_work_in_flight_ms", report.SlowestMainThreadItemWorkMs), // without a GC pause inside it
            ("gc", $"{report.Gen0Collections}/{report.Gen1Collections}/{report.Gen2Collections}"),
            ("textures", $"{report.Textures} ({report.TextureMb:0.0} MB, {report.IdleTextures} idle, {report.TextureEvictions} evicted)"),
            ("materials", $"{report.Materials} ({report.MaterialEvictions} evicted)"),
            ("meshes", $"{report.Meshes} ({report.MeshEvictions} evicted)"),
            ("gpu_texture_mb", report.GpuTextureMb),
            ("vsync", report.VSync),
            ("max_fps", report.MaxFps),
            ("workers", report.Workers),
            ("verdict", report.WithinHitchLimit ? "PASS (no frame over 50 ms)" : "FAIL (a frame took over 50 ms)"));
        var gc = report.Gc!;
        Log.Info(
            "flypath",
            "report gc",
            ("mode", gc.Mode),
            ("frames_with_gc", gc.FramesWithGc),
            ("pause_total_ms", gc.PauseTotalMs),
            ("max_frame_pause_ms", gc.MaxFramePauseMs),
            ("max_frame_with_gc_ms", gc.MaxFrameWithGcMs),
            ("max_frame_without_gc_ms", gc.MaxFrameWithoutGcMs),
            ("allocated_mb", gc.AllocatedMb),
            ("overruns_over_1ms", gc.OverrunsOver1Ms),
            ("texture_decodes", report.TextureDecodes),
            ("texture_decodes_shared", report.TextureDecodesShared));
        WriteReportFiles(report);
    }

    /// <summary>GC figures of the flight (fly phase only).</summary>
    private FlightGcStats FlightGc(GameServices services)
    {
        var budgetMs = services.MainThread.Budget.TotalMilliseconds;
        var withGc = _samples.Where(f => f.GcPauseMs > 0).ToList();
        var withoutGc = _samples.Where(f => f.GcPauseMs <= 0).ToList();
        return new FlightGcStats(
            GcMode(),
            withGc.Count,
            (GC.GetTotalPauseDuration() - _pauseAtStart).TotalMilliseconds,
            _samples.Count == 0 ? 0 : _samples.Max(f => f.GcPauseMs),
            withGc.Count == 0 ? 0 : withGc.Max(f => f.FrameMs),
            withoutGc.Count == 0 ? 0 : withoutGc.Max(f => f.FrameMs),
            (GC.GetTotalAllocatedBytes(precise: false) - _allocatedAtStart) / (1024.0 * 1024.0),
            _samples.Count(f => f.DrainMs > budgetMs + 1.0));
    }

    private void WriteReportFiles(FlyPathReport report)
    {
        try
        {
            var directory = ProjectSettings.GlobalizePath("user://logs");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "flypath-report.json"),
                JsonSerializer.Serialize(report, ReportJson));
            File.WriteAllText(Path.Combine(directory, "flypath-frames.csv"), Csv(_samples));
            File.WriteAllText(Path.Combine(directory, "flypath-startup.csv"), Csv(_startupSamples));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("flypath", "could not write the report files", ("error", ex.Message));
        }
    }

    private static string Csv(List<FrameSample> samples)
    {
        var csv = new StringBuilder("frame_ms,drain_ms,attached,gc0,gc1,gc2,rebases,gc_pause_ms,alloc_kb\n");
        foreach (var f in samples)
        {
            csv.Append(string.Create(
                CultureInfo.InvariantCulture,
                $"{f.FrameMs:0.###},{f.DrainMs:0.###},{f.Attached},{f.Gc0},{f.Gc1},{f.Gc2},{f.Rebases},{f.GcPauseMs:0.###},{f.AllocatedKb:0.#}\n"));
        }

        return csv.ToString();
    }

    private readonly record struct FrameSample(
        double FrameMs, double DrainMs, long Attached, int Gc0, int Gc1, int Gc2, int Rebases, double GcPauseMs, double AllocatedKb);

    /// <summary>GC figures of the flight.</summary>
    /// <param name="Mode">Server or workstation, concurrent or not, latency mode.</param>
    /// <param name="FramesWithGc">Frames in which the GC paused the process.</param>
    /// <param name="PauseTotalMs">All GC pauses during the flight.</param>
    /// <param name="MaxFramePauseMs">The most GC pause inside one frame.</param>
    /// <param name="MaxFrameWithGcMs">The slowest frame that had a GC pause in it.</param>
    /// <param name="MaxFrameWithoutGcMs">The slowest frame without one.</param>
    /// <param name="AllocatedMb">Managed bytes allocated by the whole process during the flight.</param>
    /// <param name="OverrunsOver1Ms">Frames whose main-thread drain ended more than 1 ms past the budget.</param>
    public sealed record FlightGcStats(
        string Mode,
        int FramesWithGc,
        double PauseTotalMs,
        double MaxFramePauseMs,
        double MaxFrameWithGcMs,
        double MaxFrameWithoutGcMs,
        double AllocatedMb,
        int OverrunsOver1Ms);

    /// <summary>The settle phase: process start to streaming idle with the main-thread queue empty.</summary>
    public sealed record StartupStats(
        double FirstFrameSeconds,
        double IdleSeconds,
        int Frames,
        double MaxFrameMs,
        int FramesOver33Ms,
        int FramesOver50Ms,
        double MaxDrainMs,
        double SlowestMainThreadItemMs,
        double AllocatedMb,
        string Collections,
        double GcPauseMs,
        double MaxFrameGcPauseMs,
        int Loaded);

    /// <summary>Memory at arrival (the plateau) and the live managed heap after a forced full collection.</summary>
    public sealed record MemoryStats(
        double GcCommittedMb,
        double GcHeapMb,
        double GcFragmentedMb,
        double PrivateMb,
        double WorkingSetMb,
        double LiveAfterFullGcMb,
        double CommittedAfterFullGcMb,
        double GodotStaticMb,
        double VideoMb);

    /// <summary>The fly-path measurement (also written as JSON).</summary>
    public sealed record FlyPathReport(
        string From,
        string To,
        double SpeedMetresPerSecond,
        double LengthMetres,
        int Frames,
        double Seconds,
        double AverageMs,
        double P50Ms,
        double P99Ms,
        double MaxMs,
        int FramesOver33Ms,
        int FramesOver50Ms,
        long LandblocksAttached,
        double LandblocksPerSecond,
        long LandblocksCancelled,
        long AbandonedMidAttach,
        double MaxPrepareMs,
        double AverageAttachMs,
        double MaxAttachMs,
        double MaxAttachStepMs,
        long MainThreadOverruns,
        double MaxDrainMs,
        double SlowestMainThreadItemMs,
        int Rebases,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections,
        int LoadedAtArrival,
        int Textures,
        double TextureMb,
        int IdleTextures,
        long TextureEvictions,
        int Materials,
        long MaterialEvictions,
        int Meshes,
        long MeshEvictions,
        double GpuTextureMb,
        string VSync,
        string Driver,
        string Adapter,
        bool WithinHitchLimit)
    {
        /// <summary>GC figures of the flight.</summary>
        public FlightGcStats? Gc { get; init; }

        /// <summary>The flight's slowest main-thread item without a GC pause that fell inside it (any item, so it can
        /// be another item than <see cref="SlowestMainThreadItemMs"/>'s).</summary>
        public double SlowestMainThreadItemWorkMs { get; init; }

        /// <summary>The settle phase before the flight (start-up when the flight starts the game).</summary>
        public StartupStats? Startup { get; init; }

        /// <summary>Memory at arrival (filled in after the flight).</summary>
        public MemoryStats? Memory { get; init; }

        /// <summary>Worker threads.</summary>
        public int Workers { get; init; }

        /// <summary>Godot's frame cap (0 = none).</summary>
        public int MaxFps { get; init; }

        /// <summary>Texture decodes since start (shared decode path).</summary>
        public long TextureDecodes { get; init; }

        /// <summary>Texture requests since start served by another request's decode.</summary>
        public long TextureDecodesShared { get; init; }
    }
}

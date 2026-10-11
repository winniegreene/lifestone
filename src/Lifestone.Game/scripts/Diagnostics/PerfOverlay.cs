using System.Diagnostics;
using System.Globalization;
using System.Text;
using Godot;
using Lifestone.Client.Diagnostics;
using Lifestone.Game.Core;
using Lifestone.Game.World;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// Top-right performance overlay (F3 toggles): FPS, frame time and its maximum over the last
/// <see cref="WindowFrames"/> frames, draw calls/objects/primitives (RenderingServer), memory, cache sizes, streaming
/// queue depths and main-thread budget overruns.
/// </summary>
public partial class PerfOverlay : Label
{
    public const int WindowFrames = 240;

    private const char NewLine = (char)10; // not AppendLine: on Windows it writes CR LF, which a Label shows as two breaks

    private readonly FrameTimeWindow _frames = new(WindowFrames);
    private WorldRoot? _world;
    private long _lastTimestamp;
    private double _sinceUpdate = double.MaxValue;

    /// <summary>Frame times of the last <see cref="WindowFrames"/> frames.</summary>
    public FrameTimeWindow Frames => _frames;

    public void Initialize(WorldRoot world) => _world = world;

    public override void _Ready()
    {
        AddThemeColorOverride("font_color", new Color(0.85f, 1f, 0.85f));
        AddThemeColorOverride("font_outline_color", Colors.Black);
        AddThemeConstantOverride("outline_size", 4);
        AddThemeFontSizeOverride("font_size", 13);
        HorizontalAlignment = HorizontalAlignment.Right;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F3 })
        {
            Visible = !Visible;
        }
    }

    public override void _Process(double delta)
    {
        var now = Stopwatch.GetTimestamp();
        if (_lastTimestamp != 0)
        {
            _frames.Add((now - _lastTimestamp) * 1000.0 / Stopwatch.Frequency);
        }

        _lastTimestamp = now;
        _sinceUpdate += delta;
        if (!Visible || _sinceUpdate < 0.25)
        {
            return;
        }

        _sinceUpdate = 0;
        Text = Describe();
    }

    private string Describe()
    {
        var sb = new StringBuilder(512);
        var ci = CultureInfo.InvariantCulture;
        sb.Append(string.Create(ci, $"FPS {Engine.GetFramesPerSecond():0}   frame {_frames.Average:0.0} ms   max {_frames.Max:0.0} ms / {WindowFrames} frames")).Append(NewLine);
        sb.Append(string.Create(
            ci,
            $"draw calls {Info(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)}   objects {Info(RenderingServer.RenderingInfo.TotalObjectsInFrame)}   " +
            $"primitives {Info(RenderingServer.RenderingInfo.TotalPrimitivesInFrame)}")).Append(NewLine);
        sb.Append(string.Create(
            ci,
            $"memory static {Mb(OS.GetStaticMemoryUsage())}   managed {Mb((ulong)GC.GetTotalMemory(false))}   " +
            $"video {Mb(Info(RenderingServer.RenderingInfo.VideoMemUsed))} (textures {Mb(Info(RenderingServer.RenderingInfo.TextureMemUsed))})")).Append(NewLine);

        var services = GameServices.Instance;
        if (services.Dats is not null)
        {
            var textures = services.Render.Textures.Stats;
            var materials = services.Render.Materials.Stats;
            var meshes = services.Render.Meshes;
            sb.Append(string.Create(
                ci,
                $"textures {textures.Count} ({Mb((ulong)textures.TotalBytes)} of {Mb((ulong)textures.BudgetBytes)}, {textures.IdleCount} idle, {textures.Evictions} evicted, {services.Render.Textures.BlockUploads} dxt)   " +
                $"materials {materials.Count} ({materials.IdleCount} idle, {materials.Evictions} evicted)")).Append(NewLine);
            sb.Append(string.Create(
                ci,
                $"meshes {meshes.Count} ({Mb((ulong)meshes.TotalBytes)}, {meshes.IdleCount} idle, {meshes.Evictions} evicted)   shaders {services.Render.Shaders.Count}   gfxobj cache {services.GfxObjMeshes.Count}")).Append(NewLine);
        }

        if (_world?.IsInitialized == true)
        {
            var streamer = _world.Streamer;
            var planner = streamer.Planner;
            sb.Append(string.Create(
                ci,
                $"streaming: loaded {planner.LoadedCount} (ring wants {planner.DesiredCount})   preparing {planner.InFlightCount}   waiting {planner.PendingCount}   " +
                $"attach queue {streamer.PendingAttaches}   {streamer.Stats.RecentAttachesPerSecond:0.0} lb/s   origin {_world.Origin.Origin}")).Append(NewLine);
        }

        var main = services.MainThread.Scheduler;
        sb.Append(string.Create(
            ci,
            $"main queue {main.Pending}   budget {services.MainThread.Budget.TotalMilliseconds:0.0} ms   last {services.LastDrainMs:0.00} ms   " +
            $"overruns {main.OverrunCount}   slowest item {main.MaxItemMilliseconds:0.00} ms")).Append(NewLine);
        sb.Append(string.Create(ci, $"workers {services.Workers.MaxConcurrency}: running {services.Workers.Running}   waiting {services.Workers.Waiting}"));
        return sb.ToString();
    }

    private static ulong Info(RenderingServer.RenderingInfo info) => RenderingServer.GetRenderingInfo(info);

    private static string Mb(ulong bytes) => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0} MB");
}

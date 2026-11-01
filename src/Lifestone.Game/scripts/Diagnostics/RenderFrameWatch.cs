using Godot;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// M5 7 D151 (<c>TODO(login-burst-frame)</c>, item 8's <c>m5-8-login-walk-and-frames</c>): where a slow frame's time went when the game's own work, the GC and the pipelines do not explain it. The viewport's
/// measured render time (its CPU draw and its GPU time, Godot's timestamp queries, read for the previous frame), the physics step, and the whole process's CPU time over the frame's interval (every thread:
/// the render thread, the workers and the GC). A long frame whose process CPU is small was spent waiting (the present, the GPU, or the operating system), not working. No allocation per read.
/// </summary>
public sealed class RenderFrameWatch
{
    private Rid _viewport;
    private TimeSpan _cpu = System.Environment.CpuUsage.TotalTime;

    /// <summary>The process CPU time since the previous <see cref="Step"/>, in milliseconds.</summary>
    public double ProcessCpuMilliseconds { get; private set; }

    /// <summary>Turns on the viewport's render-time measurement (a pair of GPU timestamps a frame).</summary>
    public void Measure(Viewport viewport)
    {
        _viewport = viewport.GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(_viewport, true);
    }

    /// <summary>Reads the process CPU total now and stores its difference to the previous read.</summary>
    public void Step()
    {
        var cpu = System.Environment.CpuUsage.TotalTime;
        ProcessCpuMilliseconds = (cpu - _cpu).TotalMilliseconds;
        _cpu = cpu;
    }

    /// <summary>
    /// The log fields <c>render_cpu_ms</c> and <c>render_gpu_ms</c> (the viewport's last measured frame; n/a before <see cref="Measure"/>), <c>physics_ms</c> (Godot's physics process time) and
    /// <c>process_cpu_ms</c> (every thread's CPU over this frame's interval).
    /// </summary>
    public (string Key, object? Value)[] Fields() =>
    [
        ("render_cpu_ms", _viewport.IsValid ? RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewport) : "n/a"),
        ("render_gpu_ms", _viewport.IsValid ? RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewport) : "n/a"),
        ("physics_ms", Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0),
        ("process_cpu_ms", ProcessCpuMilliseconds),
    ];
}

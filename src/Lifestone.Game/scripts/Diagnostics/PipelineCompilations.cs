using Godot;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// The render server's pipeline-compilation counters (<c>RenderingServer.RenderingInfo.PipelineCompilations*</c>, running totals since start-up), read as a
/// value so a frame's compilations are the difference of two reads. A first-use shader or pipeline compile inside a frame shows as one of these counting up
/// while the frame's CPU set-up time (<c>render_setup_cpu_ms</c>) is the whole frame: that is how a slow frame names its cause (DoD 15, no frame over 50 ms).
/// </summary>
/// <remarks>
/// <para>Canvas, Mesh, Surface and Draw compile on the render thread and can stall a frame (mesh: loading a mesh; surface: building the surface cache before the
/// scene renders; draw: while drawing the scene). Specialization compiles in the background to optimise the scene and never stalls a frame, so it is counted
/// but is not part of <see cref="Foreground"/>.</para>
/// </remarks>
public readonly record struct PipelineCompilations(ulong Canvas, ulong Mesh, ulong Surface, ulong Draw, ulong Specialization)
{
    /// <summary>The compilations that can stall a frame: canvas + mesh + surface + draw.</summary>
    public ulong Foreground => Canvas + Mesh + Surface + Draw;

    /// <summary>Reads the counters now (running totals).</summary>
    public static PipelineCompilations Read() => new(
        RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsCanvas),
        RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsMesh),
        RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsSurface),
        RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsDraw),
        RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsSpecialization));

    /// <summary>The compilations that happened after <paramref name="earlier"/> (a counter never counts down; a reset reads as zero).</summary>
    public PipelineCompilations Since(in PipelineCompilations earlier) => new(
        Canvas >= earlier.Canvas ? Canvas - earlier.Canvas : 0,
        Mesh >= earlier.Mesh ? Mesh - earlier.Mesh : 0,
        Surface >= earlier.Surface ? Surface - earlier.Surface : 0,
        Draw >= earlier.Draw ? Draw - earlier.Draw : 0,
        Specialization >= earlier.Specialization ? Specialization - earlier.Specialization : 0);

    /// <summary>The sum of two counts.</summary>
    public PipelineCompilations Plus(in PipelineCompilations other) =>
        new(Canvas + other.Canvas, Mesh + other.Mesh, Surface + other.Surface, Draw + other.Draw, Specialization + other.Specialization);

    /// <summary>Every counter as a log field named <c>pipelines_&lt;kind&gt;</c>.</summary>
    public (string Key, object? Value)[] Fields() =>
    [
        ("pipelines_canvas", Canvas),
        ("pipelines_mesh", Mesh),
        ("pipelines_surface", Surface),
        ("pipelines_draw", Draw),
        ("pipelines_specialization", Specialization),
    ];

    /// <summary>One line of text, <c>canvas=0 mesh=0 surface=0 draw=1 specialization=0</c>.</summary>
    public string Describe() => $"canvas={Canvas} mesh={Mesh} surface={Surface} draw={Draw} specialization={Specialization}";
}

/// <summary>
/// Turns the counters into per-frame differences: call <see cref="Step"/> once per frame (from a node's <c>_Process</c>) and <see cref="Last"/> holds what was
/// compiled since the previous call, which is the previous frame's render plus this frame's work so far (the same window <c>process_ms</c> and
/// <c>render_setup_cpu_ms</c> describe).
/// </summary>
public sealed class PipelineCompilationWatch
{
    private PipelineCompilations _previous = PipelineCompilations.Read();

    /// <summary>The compilations between the last two <see cref="Step"/> calls.</summary>
    public PipelineCompilations Last { get; private set; }

    /// <summary>The compilations from construction to the latest <see cref="Step"/>.</summary>
    public PipelineCompilations Total { get; private set; }

    /// <summary>Reads the counters, stores the difference to the previous read in <see cref="Last"/> and returns it.</summary>
    public PipelineCompilations Step()
    {
        var now = PipelineCompilations.Read();
        Last = now.Since(_previous);
        _previous = now;
        Total = Total.Plus(Last);
        return Last;
    }
}

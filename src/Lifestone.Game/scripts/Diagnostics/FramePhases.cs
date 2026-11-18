using System.Diagnostics;
using Godot;
using Lifestone.Client.Diagnostics;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// The play host's clock on its own per-frame calls (M6 16 Eperf2): each of the host's top-level frame methods opens its phase with one <c>using</c> line, and the long-frame line
/// (<see cref="LongFrameWatch"/>) reads the sums at the frame's boundary. Two clock reads a phase a frame and no allocation; the game thread only. Once <see cref="HookDraw"/> is called the draw is timed between
/// the rendering server's pre-draw and post-draw signals, and the engine's time after it up to the tree's next process signal (its frame delay, then the next frame's input and physics).
/// </summary>
public static class FramePhases
{
    private static long _drawBegan;
    private static long _drawEnded;
    private static SceneTree? _tree;

    /// <summary>The frame's phase sums; the long-frame watch closes them at each frame's boundary.</summary>
    public static FramePhaseTimes Times { get; } = new();

    /// <summary>The main-thread work queue's drain.</summary>
    public static Scope Services() => new(FramePhase.Services);

    /// <summary>The session's tick.</summary>
    public static Scope Session() => new(FramePhase.Session);

    /// <summary>Play mode's step.</summary>
    public static Scope Play() => new(FramePhase.Play);

    /// <summary>The player controller's step.</summary>
    public static Scope Player() => new(FramePhase.Player);

    /// <summary>The autopilot host's tick.</summary>
    public static Scope Autopilot() => new(FramePhase.Autopilot);

    /// <summary>The retail UI layer's step.</summary>
    public static Scope Ui() => new(FramePhase.Ui);

    /// <summary>A world node's step.</summary>
    public static Scope World() => new(FramePhase.World);

    /// <summary>Starts timing the draw and the engine's time after it, on <paramref name="tree"/>'s process signal. Once; <see cref="UnhookDraw"/> ends it.</summary>
    /// <param name="tree">The scene tree.</param>
    public static void HookDraw(SceneTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (_tree is not null)
        {
            return;
        }

        _tree = tree;
        _drawBegan = 0;
        _drawEnded = 0;
        RenderingServer.FramePreDraw += OnPreDraw;
        RenderingServer.FramePostDraw += OnPostDraw;
        tree.ProcessFrame += OnProcessFrame;
    }

    /// <summary>Stops timing the draw.</summary>
    public static void UnhookDraw()
    {
        if (_tree is not { } tree)
        {
            return;
        }

        _tree = null;
        RenderingServer.FramePreDraw -= OnPreDraw;
        RenderingServer.FramePostDraw -= OnPostDraw;
        if (GodotObject.IsInstanceValid(tree))
        {
            tree.ProcessFrame -= OnProcessFrame;
        }
    }

    private static void OnPreDraw() => _drawBegan = Stopwatch.GetTimestamp();

    private static void OnPostDraw()
    {
        var now = Stopwatch.GetTimestamp();
        if (_drawBegan != 0)
        {
            Times.Add(FramePhase.Draw, now - _drawBegan);
            _drawBegan = 0;
        }

        _drawEnded = now;
    }

    // The tree's process signal comes before any node's process call of the frame: what passed since the draw's end is the engine's own (its frame delay, the input, the physics step).
    private static void OnProcessFrame()
    {
        if (_drawEnded != 0)
        {
            Times.Add(FramePhase.AfterDraw, Stopwatch.GetTimestamp() - _drawEnded);
            _drawEnded = 0;
        }
    }

    /// <summary>One phase's open interval: made at the top of a frame method by a <c>using</c> declaration, it adds its time when the method returns, by whichever path.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly FramePhase _phase;
        private readonly long _began;

        /// <summary>Opens <paramref name="phase"/> now.</summary>
        /// <param name="phase">The phase.</param>
        public Scope(FramePhase phase)
        {
            _phase = phase;
            _began = Stopwatch.GetTimestamp();
        }

        /// <summary>Closes the phase: its time is added to the frame's sum.</summary>
        public void Dispose()
        {
            if (_began != 0)
            {
                Times.Add(_phase, Stopwatch.GetTimestamp() - _began);
            }
        }
    }
}

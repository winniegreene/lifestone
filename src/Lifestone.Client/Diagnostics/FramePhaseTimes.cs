namespace Lifestone.Client.Diagnostics;

/// <summary>
/// The top-level parts of a frame the play host times for the long-frame line (M6 16 Eperf2, <see cref="LongFrameLine"/>): each is one or more of the host's own per-frame calls, timed by the host with its own
/// clock. They do not nest, so their sum is at most the frame.
/// </summary>
public enum FramePhase
{
    /// <summary>The main-thread work queue: what the workers handed back for this frame.</summary>
    Services,

    /// <summary>The session's tick: the network pump, the reducers and whatever the session steps.</summary>
    Session,

    /// <summary>Play mode's step: its queued work and the session's object events applied to the scene.</summary>
    Play,

    /// <summary>The player: the streamer's pump, the walker's physics, the avatar set's advance and the view.</summary>
    Player,

    /// <summary>The autopilot host's tick: the dial, the scenario's step, its panels.</summary>
    Autopilot,

    /// <summary>The retail UI layer's step.</summary>
    Ui,

    /// <summary>The world's nodes: the streamer's root, the objects, the cells, the living statics, the interiors, the server objects' layer and the sky.</summary>
    World,

    /// <summary>The draw: from the rendering server's pre-draw signal to its post-draw signal (the scene's update, the viewports, and the present with its wait).</summary>
    Draw,

    /// <summary>From the draw's end to the next frame's process signal: the engine's own frame delay (its frame cap's sleep), then the next frame's input and physics. On a quiet frame this is where the thread sleeps.</summary>
    AfterDraw,
}

/// <summary>
/// The time of each <see cref="FramePhase"/> over one frame, in the host's clock ticks (M6 16 Eperf2). The host adds a phase's ticks as it ends; <see cref="Close"/> at the frame's boundary moves the running sums to the
/// closed frame and starts again from zero. Two arrays made once: nothing is allocated after construction. This class reads no clock.
/// </summary>
public sealed class FramePhaseTimes
{
    /// <summary>The number of phases.</summary>
    public const int Count = 9;

    private readonly long[] _running = new long[Count];
    private readonly long[] _closed = new long[Count];

    /// <summary>Adds <paramref name="ticks"/> to <paramref name="phase"/> of the frame that is running (a negative count adds nothing).</summary>
    /// <param name="phase">The phase.</param>
    /// <param name="ticks">Its ticks.</param>
    public void Add(FramePhase phase, long ticks)
    {
        if (ticks > 0)
        {
            _running[(int)phase] += ticks;
        }
    }

    /// <summary>The frame's boundary: what was added since the last boundary becomes the closed frame, and the running sums start from zero.</summary>
    public void Close()
    {
        for (var i = 0; i < Count; i++)
        {
            _closed[i] = _running[i];
            _running[i] = 0;
        }
    }

    /// <summary>The ticks of <paramref name="phase"/> in the closed frame.</summary>
    /// <param name="phase">The phase.</param>
    public long Closed(FramePhase phase) => _closed[(int)phase];

    /// <summary>The ticks of every phase of the closed frame together.</summary>
    public long ClosedTotal
    {
        get
        {
            var total = 0L;
            for (var i = 0; i < Count; i++)
            {
                total += _closed[i];
            }

            return total;
        }
    }
}

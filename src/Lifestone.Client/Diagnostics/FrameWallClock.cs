using System.Diagnostics;

namespace Lifestone.Client.Diagnostics;

/// <summary>
/// The length of a frame as the wall clock measured it (M6 14 hub plan): the time between two successive process calls of a node, not the engine's delta. The engine's delta did not show a long hold: the first plan in the
/// Town Network held the game's thread 7.6 s and then 4.5 s, and the slow-frame line read 130.9 ms and the soak line of the minute after read frame_max_ms 138.8 ms (live 2026-11-16 16:54:52 to 16:55:48 UTC, cache/m6/14/live/ladder38).
/// Read once a process call with the engine's frame number: a call that does not follow the frame before it (the node did not process in between: hidden, paused, just made) has no wall time to measure, and its length is the
/// engine's delta.
/// </summary>
public sealed class FrameWallClock
{
    private readonly Func<long> _timestamp;
    private readonly double _ticksPerMillisecond;
    private long _lastFrame = long.MinValue;
    private long _lastStamp;

    /// <summary>A clock on <see cref="Stopwatch"/>.</summary>
    public FrameWallClock()
        : this(Stopwatch.GetTimestamp, Stopwatch.Frequency)
    {
    }

    /// <summary>A clock on <paramref name="timestamp"/> ticking <paramref name="frequency"/> times a second (tests).</summary>
    /// <param name="timestamp">The timestamp source.</param>
    /// <param name="frequency">Its ticks a second.</param>
    public FrameWallClock(Func<long> timestamp, long frequency)
    {
        _timestamp = timestamp ?? throw new ArgumentNullException(nameof(timestamp));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frequency);
        _ticksPerMillisecond = frequency / 1000.0;
    }

    /// <summary>
    /// The frame's length in milliseconds: the wall time since the previous call when that call was the frame before this one (<paramref name="frame"/> one more than its), else <paramref name="deltaMilliseconds"/>.
    /// </summary>
    /// <param name="frame">The engine's frame number of this call (Godot's <c>Engine.GetProcessFrames()</c>).</param>
    /// <param name="deltaMilliseconds">The engine's delta of this call, in milliseconds.</param>
    public double Next(long frame, double deltaMilliseconds)
    {
        var stamp = _timestamp();
        var follows = _lastFrame != long.MinValue && frame == _lastFrame + 1;
        var ms = follows ? (stamp - _lastStamp) / _ticksPerMillisecond : deltaMilliseconds;
        (_lastFrame, _lastStamp) = (frame, stamp);
        return ms;
    }
}

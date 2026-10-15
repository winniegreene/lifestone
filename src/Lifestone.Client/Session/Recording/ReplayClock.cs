namespace Lifestone.Client.Session.Recording;

/// <summary>
/// The time source of a replay (ST14): it stands still until the replayer moves it to the next frame's recorded time, so
/// the session clock reads, at each frame, what it read when the frame was recorded. Timestamps are ticks (100 ns); a
/// replay sets times explicitly and creates no timers.
/// </summary>
internal sealed class ReplayClock : TimeProvider
{
    private long _ticks;

    /// <inheritdoc/>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>The time, in seconds since the clock was created.</summary>
    internal double Seconds => _ticks / (double)TimeSpan.TicksPerSecond;

    /// <inheritdoc/>
    public override long GetTimestamp() => _ticks;

    /// <inheritdoc/>
    /// <remarks>Nothing the reducer stores reads it; it is the Unix epoch plus the replay's time.</remarks>
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + TimeSpan.FromTicks(_ticks);

    /// <summary>Moves the clock to <paramref name="seconds"/> (a recorded frame time; earlier times are allowed).</summary>
    internal void MoveTo(double seconds) =>
        _ticks = double.IsFinite(seconds) ? (long)Math.Round(seconds * TimeSpan.TicksPerSecond) : _ticks;
}

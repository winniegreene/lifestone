namespace Lifestone.Client.Session.Autopilot.Steer;

/// <summary>The host's camera for the <c>capture</c> order. The read-back is the game thread's cost; saving is done elsewhere.</summary>
public interface ISteerCamera
{
    /// <summary>Reads the frame back now, on the game thread, and leaves the saving to another thread.</summary>
    /// <param name="name">The screenshot's name (a checked file name, without the extension).</param>
    /// <returns>The shot on its way to disk; null when no frame could be read.</returns>
    SteerShot? Take(string name);
}

/// <summary>One screenshot on its way to disk, and what it cost.</summary>
/// <param name="readbackMilliseconds">The game thread's time reading the frame back.</param>
/// <param name="priorFrameMilliseconds">The whole frame before the capture.</param>
/// <param name="width">The frame's width.</param>
/// <param name="height">The frame's height.</param>
public sealed class SteerShot(double readbackMilliseconds, double priorFrameMilliseconds, int width, int height)
{
    private volatile bool _saved;
    private volatile string? _failure;

    /// <summary>The game thread's time reading the frame back.</summary>
    public double ReadbackMilliseconds { get; } = readbackMilliseconds;

    /// <summary>The whole frame before the capture.</summary>
    public double PriorFrameMilliseconds { get; } = priorFrameMilliseconds;

    /// <summary>The frame's width.</summary>
    public int Width { get; } = width;

    /// <summary>The frame's height.</summary>
    public int Height { get; } = height;

    /// <summary>The whole frame that paid for the read-back; null until the next frame has begun. Game thread only.</summary>
    public double? FrameMilliseconds { get; set; }

    /// <summary>True once the file is on disk.</summary>
    public bool Saved => _saved;

    /// <summary>Why the file could not be saved; null when it was or is still on its way.</summary>
    public string? Failure => _failure;

    /// <summary>The file is on disk.</summary>
    public void MarkSaved() => _saved = true;

    /// <summary>The file could not be saved.</summary>
    /// <param name="why">Why.</param>
    public void MarkFailed(string why) => _failure = why;
}

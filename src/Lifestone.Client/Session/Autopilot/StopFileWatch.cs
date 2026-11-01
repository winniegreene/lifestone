namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// The soak's clean stop from outside (M5 7 F18, <c>--autopilot-stop-file</c>): polls for a file every <see cref="PollSeconds"/> s and says when it exists. Between two polls a tick only adds the frame's seconds (no file system access, no allocation);
/// the file system is reached through the <c>exists</c> function the host supplies (the client keeps its file I/O out). Game thread only.
/// </summary>
public sealed class StopFileWatch
{
    /// <summary>Seconds between two looks for the file (Lifestone's own; the soak runs for hours and a stop may wait a few seconds).</summary>
    public const double PollSeconds = 5;

    private readonly Func<bool> _exists;
    private double _since;
    private bool _seen;

    /// <summary>A watch over <paramref name="exists"/>.</summary>
    /// <param name="path">The file's path, for the log lines (the host resolved it).</param>
    /// <param name="exists">Says whether the file exists now (called at most once per <see cref="PollSeconds"/>).</param>
    public StopFileWatch(string path, Func<bool> exists)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        _exists = exists ?? throw new ArgumentNullException(nameof(exists));
    }

    /// <summary>The file being watched.</summary>
    public string Path { get; }

    /// <summary>How many times the file system was asked.</summary>
    public int Polls { get; private set; }

    /// <summary>True once a poll found the file.</summary>
    public bool Seen => _seen;

    /// <summary>
    /// Counts <paramref name="delta"/> seconds; at each poll time looks for the file. The first poll is <see cref="PollSeconds"/> after the first tick.
    /// </summary>
    /// <param name="delta">Seconds since the last tick.</param>
    /// <returns>True when this tick's poll found the file (every poll finds it again while the file stays, so a stop asked while the dial was off is asked again).</returns>
    public bool Tick(double delta)
    {
        _since += delta;
        if (_since < PollSeconds)
        {
            return false;
        }

        _since = 0;
        Polls++;
        if (!_exists())
        {
            return false;
        }

        _seen = true;
        return true;
    }
}

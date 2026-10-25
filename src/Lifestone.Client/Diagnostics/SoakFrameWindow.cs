namespace Lifestone.Client.Diagnostics;

/// <summary>
/// The frame times of one <c>[soak]</c> interval (M5 item 0 c3): sampled every frame without allocating, read once per interval.
/// The maximum and the over-50 and over-75 ms counts are exact for the whole interval; the 99th percentile is read from the last
/// <see cref="Capacity"/> frames (16,384 by default: a minute at 240 fps with room to spare).
/// </summary>
public sealed class SoakFrameWindow
{
    /// <summary>The frame length that is a failure of the 50 ms frame rule (DoD 15).</summary>
    public const double Over50Milliseconds = 50.0;

    /// <summary>The frame length that fails a gate outright (the 75 ms rule).</summary>
    public const double Over75Milliseconds = 75.0;

    private readonly double[] _ring;
    private readonly double[] _scratch;
    private int _next;
    private int _filled;

    /// <summary>Creates the window with its two buffers; nothing is allocated after this.</summary>
    /// <param name="capacity">Frames the percentile reads (at least 1).</param>
    public SoakFrameWindow(int capacity = 16384)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _ring = new double[capacity];
        _scratch = new double[capacity];
    }

    /// <summary>The frames the percentile reads.</summary>
    public int Capacity => _ring.Length;

    /// <summary>Frames sampled since the last <see cref="Reset"/>.</summary>
    public long Frames { get; private set; }

    /// <summary>The longest frame since the last <see cref="Reset"/>, in milliseconds (0 when none).</summary>
    public double Max { get; private set; }

    /// <summary>Frames longer than 50 ms since the last <see cref="Reset"/>.</summary>
    public int Over50 { get; private set; }

    /// <summary>Frames longer than 75 ms since the last <see cref="Reset"/>.</summary>
    public int Over75 { get; private set; }

    /// <summary>Adds one frame time in milliseconds. Allocation free.</summary>
    /// <param name="milliseconds">The frame time.</param>
    public void Add(double milliseconds)
    {
        _ring[_next] = milliseconds;
        _next = _next + 1 == _ring.Length ? 0 : _next + 1;
        if (_filled < _ring.Length)
        {
            _filled++;
        }

        Frames++;
        if (milliseconds > Max)
        {
            Max = milliseconds;
        }

        if (milliseconds > Over50Milliseconds)
        {
            Over50++;
        }

        if (milliseconds > Over75Milliseconds)
        {
            Over75++;
        }
    }

    /// <summary>
    /// The 99th percentile (nearest rank) of the last <see cref="Capacity"/> frames, in milliseconds; 0 when none. Sorts a copy in a
    /// buffer made at construction: it allocates nothing, but it is for the once-a-minute read, not for a frame.
    /// </summary>
    public double P99()
    {
        if (_filled == 0)
        {
            return 0.0;
        }

        Array.Copy(_ring, _scratch, _filled);
        Array.Sort(_scratch, 0, _filled);
        var rank = (int)Math.Ceiling(0.99 * _filled);
        return _scratch[Math.Clamp(rank, 1, _filled) - 1];
    }

    /// <summary>Starts a new interval.</summary>
    public void Reset()
    {
        _next = 0;
        _filled = 0;
        Frames = 0;
        Max = 0.0;
        Over50 = 0;
        Over75 = 0;
    }
}

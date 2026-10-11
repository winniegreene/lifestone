namespace Lifestone.Client.Diagnostics;

/// <summary>Frame times over a sliding window of the last N frames (for an on-screen overlay).</summary>
public sealed class FrameTimeWindow
{
    private readonly double[] _samples;
    private int _next;
    private int _count;

    public FrameTimeWindow(int capacity = 240)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _samples = new double[capacity];
    }

    public int Capacity => _samples.Length;

    public int Count => _count;

    /// <summary>Adds a frame time in milliseconds, replacing the oldest when full.</summary>
    public void Add(double milliseconds)
    {
        _samples[_next] = milliseconds;
        _next = (_next + 1) % _samples.Length;
        _count = Math.Min(_count + 1, _samples.Length);
    }

    /// <summary>Largest frame time in the window (0 when empty).</summary>
    public double Max
    {
        get
        {
            var max = 0.0;
            for (var i = 0; i < _count; i++)
            {
                max = Math.Max(max, _samples[i]);
            }

            return max;
        }
    }

    /// <summary>Mean frame time in the window (0 when empty).</summary>
    public double Average
    {
        get
        {
            if (_count == 0)
            {
                return 0.0;
            }

            var sum = 0.0;
            for (var i = 0; i < _count; i++)
            {
                sum += _samples[i];
            }

            return sum / _count;
        }
    }
}

/// <summary>Every frame time of a run (e.g. an automated fly path), with summary statistics.</summary>
public sealed class FrameTimeRecorder
{
    private readonly List<double> _samples = new();

    public int Count => _samples.Count;

    public IReadOnlyList<double> Samples => _samples;

    public void Add(double milliseconds) => _samples.Add(milliseconds);

    public double Max => _samples.Count == 0 ? 0.0 : _samples.Max();

    public double Average => _samples.Count == 0 ? 0.0 : _samples.Average();

    /// <summary>
    /// Nearest-rank percentile (<paramref name="percent"/> in (0, 100]): the smallest sample with at least that share of
    /// samples at or below it. 0 when empty.
    /// </summary>
    public double Percentile(double percent)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100.0);
        if (_samples.Count == 0)
        {
            return 0.0;
        }

        var sorted = _samples.ToArray();
        Array.Sort(sorted);
        var rank = (int)Math.Ceiling(percent / 100.0 * sorted.Length);
        return sorted[Math.Clamp(rank, 1, sorted.Length) - 1];
    }

    /// <summary>Frames slower than <paramref name="milliseconds"/>.</summary>
    public int CountAbove(double milliseconds) => _samples.Count(s => s > milliseconds);
}

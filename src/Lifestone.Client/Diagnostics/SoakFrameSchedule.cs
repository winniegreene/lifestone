using System.Globalization;

namespace Lifestone.Client.Diagnostics;

/// <summary>
/// The clock rule of the soak frames (M5 item 7, <c>--soak-frame-s</c>): the first frame is due <c>S</c> seconds after the host arms the schedule (on entering
/// the world, not at start-up), then one every <c>S</c> seconds. A frame is never owed twice: a stall of several periods gives one frame, and the next is a
/// period after it. Engine-free; the host reads the clock and saves the frame.
/// </summary>
public sealed class SoakFrameSchedule
{
    /// <summary>The shortest <c>--soak-frame-s</c> accepted.</summary>
    public const double MinSeconds = 60.0;

    /// <summary>The longest <c>--soak-frame-s</c> accepted: a day.</summary>
    public const double MaxSeconds = 86400.0;

    private readonly double _periodSeconds;
    private double _due;

    /// <summary>Creates a schedule; a period of 0 or less is off (<see cref="TryTake"/> is always false).</summary>
    public SoakFrameSchedule(double periodSeconds) => _periodSeconds = periodSeconds;

    /// <summary>True when the period is positive.</summary>
    public bool Enabled => _periodSeconds > 0;

    /// <summary>True between <see cref="Arm"/> and <see cref="Disarm"/>.</summary>
    public bool Armed { get; private set; }

    /// <summary>How many frames were taken.</summary>
    public int Taken { get; private set; }

    /// <summary>Starts (or restarts) the countdown: the next frame is due one period after <paramref name="nowSeconds"/>.</summary>
    public void Arm(double nowSeconds)
    {
        if (!Enabled)
        {
            return;
        }

        Armed = true;
        _due = nowSeconds + _periodSeconds;
    }

    /// <summary>Stops the countdown.</summary>
    public void Disarm() => Armed = false;

    /// <summary>True (and the number of the frame, from 1) when a frame is due at <paramref name="nowSeconds"/>; the next is due a period later.</summary>
    public bool TryTake(double nowSeconds, out int number)
    {
        number = 0;
        if (!Enabled || !Armed || nowSeconds < _due)
        {
            return false;
        }

        var next = _due + _periodSeconds;
        _due = next > nowSeconds ? next : nowSeconds + _periodSeconds;
        number = ++Taken;
        return true;
    }

    /// <summary>The frame's file name: <c>soak-&lt;UTC yyyyMMddTHHmmssZ&gt;-&lt;n&gt;.png</c>.</summary>
    public static string FileName(DateTime utc, int number) =>
        string.Create(CultureInfo.InvariantCulture, $"soak-{utc.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}-{number}.png");
}

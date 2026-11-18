using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Vitals;

/// <summary>
/// M6 14 the losing race, her side (the lead, 2026-11-15, live ladder27 06:04:07 UTC: at 166 of 206 and rising, her drains having taken her from 111 to 166 in ten seconds, the race read the two rates, 15.7 heal against 16.2 incoming, and
/// recalled her from a fight she was winning): her health's measured change a second over the last <see cref="WindowSeconds"/>, heals, drains and hits all in it, the least-squares slope of the health read every
/// <see cref="SampleSeconds"/>. Rising or level is not losing, whatever the two rates say. Engine-free and allocation-free.
/// </summary>
public sealed class HealthTrend
{
    /// <summary>
    /// The seconds the trend is read over (Lifestone's own: the longest heal cast measured live, Drain Health Other I's 3.9 s on 06:04:00 UTC, with a hit or two either side, so a heal that lands is in the window with the hits it
    /// answers).
    /// </summary>
    public const double WindowSeconds = 6;

    /// <summary>
    /// M6 14 the losing race (live ladder52 2026-11-17 04:54:27.874 UTC: "her health 137 falls 0/s" read as falling, and she recalled with her health level): the least change a second that is a measured rise or fall: one whole point of
    /// health over <see cref="WindowSeconds"/> (her health is whole points; a slope under one point over the window is level).
    /// </summary>
    public const double LeastChangePerSecond = 1.0 / WindowSeconds;

    /// <summary>The least seconds of samples a trend needs (Lifestone's own: under it one hit or one heal is the whole reading).</summary>
    public const double MinSpanSeconds = 2;

    /// <summary>The seconds between two samples (Lifestone's own: five a second is finer than any hit's or heal's spacing, and the ring then covers twice the window).</summary>
    public const double SampleSeconds = 0.2;

    private const int Ring = 64;

    private readonly double[] _at = new double[Ring];
    private readonly double[] _health = new double[Ring];
    private int _next;
    private int _count;
    private double _lastAt = double.NegativeInfinity;

    /// <summary>Reads her health from <paramref name="state"/> at <paramref name="now"/> (at most once every <see cref="SampleSeconds"/>; an unread or zero health, a death, starts it again).</summary>
    /// <param name="state">The game state.</param>
    /// <param name="now">The session clock.</param>
    public void Sample(GameState state, double now)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _) || health == 0)
        {
            _count = 0;
            return;
        }

        Note(now, health);
    }

    /// <summary>Notes one health reading (the sampling throttle applies).</summary>
    /// <param name="now">The session clock.</param>
    /// <param name="health">Her health.</param>
    public void Note(double now, double health)
    {
        if (now - _lastAt < SampleSeconds)
        {
            return;
        }

        _lastAt = now;
        (_at[_next], _health[_next]) = (now, health);
        _next = (_next + 1) % Ring;
        _count = Math.Min(Ring, _count + 1);
    }

    /// <summary>The health a second over the window ending at <paramref name="now"/>, and the seconds it spans; false when the samples span under <see cref="MinSpanSeconds"/> (not measured).</summary>
    /// <param name="now">The session clock.</param>
    /// <param name="perSecond">The slope.</param>
    /// <param name="span">The seconds the samples span.</param>
    public bool TryRead(double now, out double perSecond, out double span)
    {
        (perSecond, span) = (0, 0);
        var (n, sx, sy, sxx, sxy, first, last) = (0, 0.0, 0.0, 0.0, 0.0, double.PositiveInfinity, double.NegativeInfinity);
        for (var i = 0; i < _count; i++)
        {
            var k = (_next - 1 - i + Ring) % Ring;
            var age = now - _at[k];
            if (age > WindowSeconds || age < 0)
            {
                continue;
            }

            var x = _at[k] - now;
            (n, sx, sy, sxx, sxy) = (n + 1, sx + x, sy + _health[k], sxx + (x * x), sxy + (x * _health[k]));
            (first, last) = (Math.Min(first, _at[k]), Math.Max(last, _at[k]));
        }

        span = n > 1 ? last - first : 0;
        var d = (n * sxx) - (sx * sx);
        if (n < 3 || span < MinSpanSeconds || d <= 0)
        {
            return false;
        }

        perSecond = ((n * sxy) - (sx * sy)) / d;
        return true;
    }
}

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M5 7 G1 (edge audit 12a): a guard against a trip that loops. When the free slots or the burden stay past their trip line after a trip that sold and bought nothing, the fight's counters (every second) would call the next trip at once, and the mage would
/// recall, buff and return every few minutes with no kills. Two trips in a row that sold and bought nothing end the asking: trips are held back for <see cref="HoldKills"/> kills, by a named log line, and the run goes on fighting.
/// </summary>
public sealed class TripLoopGuard
{
    /// <summary>The trips in a row that sold and bought nothing at which the guard holds trips back.</summary>
    public const int IdleLimit = 2;

    /// <summary>The kills the fight makes before a trip is asked again.</summary>
    public const int HoldKills = 30;

    private int _idle;
    private long _holdUntilKills = -1;

    /// <summary>The trips in a row so far that sold and bought nothing.</summary>
    public int Idle => _idle;

    /// <summary>The trips the guard held back (each hold is one).</summary>
    public int Holds { get; private set; }

    /// <summary>A trip ended: <paramref name="sold"/> items sold and <paramref name="bought"/> bought. True when this is the second idle trip in a row: the hold starts at <paramref name="killsNow"/>.</summary>
    public bool TripEnded(int sold, int bought, long killsNow)
    {
        if (sold + bought > 0)
        {
            _idle = 0;
            return false;
        }

        if (++_idle < IdleLimit)
        {
            return false;
        }

        _idle = 0;
        _holdUntilKills = killsNow + HoldKills;
        Holds++;
        return true;
    }

    /// <summary>A trip was given up (it failed twice with the stock over its stop lines): the next is held back for <see cref="HoldKills"/> kills from <paramref name="killsNow"/>.</summary>
    public void Hold(long killsNow)
    {
        _idle = 0;
        _holdUntilKills = killsNow + HoldKills;
        Holds++;
    }

    /// <summary>True while a trip is held back: fewer than <see cref="HoldKills"/> kills since the hold began.</summary>
    public bool Holding(long killsNow) => killsNow < _holdUntilKills;
}

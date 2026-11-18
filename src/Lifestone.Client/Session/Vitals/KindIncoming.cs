namespace Lifestone.Client.Session.Vitals;

/// <summary>
/// M6 14 the losing race's per-kind incoming, as one function so the vitals guard's race (<c>AutopilotVitalsGuard.IncomingNow</c>) and the fight's target switch (<see cref="TargetSwitch"/>, M6 14 tactics) read a kind the same way.
/// A kind's hits over the seconds since its first attack in this attack: once its attacks span the whole of <see cref="RecentHits.AverageSeconds"/> (and it is not new: <see cref="NewKindSeconds"/>, <see cref="NewKindHits"/>), its
/// hits over that window are the whole measure; before that, its hits over the window plus the derived rate of the ones of it on her for the part of the window it was not yet heard in, never under that derived rate (the
/// guard's 06:17:07, 04:54:27 and 07:51:27 UTC lessons, said in its notes); with no derived rate read, its hits over its span, the span never under <see cref="NewKindSeconds"/>. Pure.
/// </summary>
public static class KindIncoming
{
    /// <summary>A kind heard under this many seconds ago reads at least its derived rate (the recent hits' window, <see cref="RecentHits.RecentSeconds"/>).</summary>
    public const double NewKindSeconds = RecentHits.RecentSeconds;

    /// <summary>A kind with fewer attacks heard than this reads at least its derived rate (Lifestone's own: two hits are one interval).</summary>
    public const int NewKindHits = 3;

    /// <summary>True when the kind's attacks span the whole of <see cref="RecentHits.AverageSeconds"/> and it is not new: its measure alone is its rate (no derived rate is read).</summary>
    /// <param name="since">The seconds since its first attack in this attack.</param>
    /// <param name="attacks">Its attacks in this attack, hits and attacks that took nothing alike.</param>
    /// <param name="isNew">True while it is new.</param>
    public static bool Whole(double since, int attacks, out bool isNew)
    {
        isNew = since < NewKindSeconds || attacks < NewKindHits;
        return !isNew && since >= RecentHits.AverageSeconds;
    }

    /// <summary>The kind's damage a second on her.</summary>
    /// <param name="damage">Its hits' damage in the last <see cref="RecentHits.AverageSeconds"/> (the dead's share out).</param>
    /// <param name="since">The seconds since its first attack in this attack.</param>
    /// <param name="attacks">Its attacks in this attack.</param>
    /// <param name="derived">The derived rate of the ones of it on her (not read when <see cref="Whole"/>).</param>
    /// <param name="derivedRead">True when the derived rate was read (the kind's facts are known).</param>
    /// <param name="blend">Its hits over the window plus the derived rate for the part it was not heard in (0 when not read).</param>
    public static double Rate(double damage, double since, int attacks, double derived, bool derivedRead, out double blend)
    {
        var window = RecentHits.AverageSeconds;
        var whole = Whole(since, attacks, out _);
        var rate = whole ? damage / window : damage / Math.Max(NewKindSeconds, since);
        blend = 0;
        if (!whole && derivedRead)
        {
            blend = (damage / window) + (derived * (1 - Math.Clamp(since / window, 0, 1)));
            rate = Math.Max(derived, blend);
        }

        return rate;
    }
}

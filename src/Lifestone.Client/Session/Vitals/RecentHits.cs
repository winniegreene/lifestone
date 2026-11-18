namespace Lifestone.Client.Session.Vitals;

/// <summary>What the recent hits say at one moment (<see cref="RecentHits.Read"/>): the one incoming number the fight's emergency, the vitals guard and the retreat rule read.</summary>
/// <param name="PerSecond">The damage a second: the mean recent hit over the attack interval while the hits go on, never under the average over <see cref="RecentHits.AverageSeconds"/>.</param>
/// <param name="BiggestHit">The biggest hit over <see cref="RecentHits.AverageSeconds"/> (0: none).</param>
/// <param name="IntervalSeconds">The seconds between hits: measured from the recent hits; with one, the gap from the hit before it, at most <see cref="RecentHits.SeedAttackSeconds"/>.</param>
/// <param name="SinceLastHit">The seconds since the last hit (infinity: none in the window).</param>
/// <param name="Hits">The hits over <see cref="RecentHits.RecentSeconds"/>.</param>
/// <param name="Live">True while the hits go on: one within <see cref="RecentHits.RecentSeconds"/> and not overdue by <see cref="RecentHits.OverdueIntervals"/> intervals.</param>
public readonly record struct IncomingRead(double PerSecond, double BiggestHit, double IntervalSeconds, double SinceLastHit, int Hits, bool Live)
{
    /// <summary>True when the hits go on and the biggest one seen takes all of <paramref name="health"/>: the case a low mage must act on.</summary>
    /// <param name="health">The health left.</param>
    public bool NextHitKills(double health) => Live && BiggestHit > 0 && health <= BiggestHit;

    /// <summary>True when the mage is expected to live <paramref name="seconds"/> at <paramref name="health"/>: the incoming over that time leaves some, and no killing blow falls due inside it.</summary>
    /// <param name="health">The health left.</param>
    /// <param name="seconds">The seconds to live through (a cast's).</param>
    public bool Survives(double health, double seconds) =>
        !(NextHitKills(health) && seconds >= Math.Max(0, IntervalSeconds - SinceLastHit)) && health > PerSecond * seconds;
}

/// <summary>
/// M6 7 Y (r2b: a tusker just arrived read 1.1 a second on the fight's 20 s mean and 2.5 on the watch's 10 s window, while it hit for 18 and 16 about 1.5 s apart): the hits of the last seconds, the same seam for every reader of the incoming damage.
/// <c>FightRates</c> feeds one from the fight's hit notices, the autopilot's <c>HostileWatch</c> one from the same notices; each answers the same <see cref="Read"/>. Engine-free and allocation-free.
/// </summary>
public sealed class RecentHits
{
    /// <summary>The seconds the attack interval and the mean hit are read over (Lifestone's own: "the last few seconds": about three tusker swings; r2b's gaps were 3.3, 1.5 and 3.1 s).</summary>
    public const double RecentSeconds = 4;

    /// <summary>The seconds of the plain average and of the biggest hit (the watch's window from M5 7 F16).</summary>
    public const double AverageSeconds = 10;

    /// <summary>The attack interval read from one hit (Lifestone's own: r2b's three tusker gaps average 2.6 s and their shortest is 1.5 s).</summary>
    public const double SeedAttackSeconds = 2;

    /// <summary>The shortest attack interval read (Lifestone's own: two hits in one server tick are two attackers, not a 20 ms swing).</summary>
    public const double MinAttackSeconds = 0.5;

    /// <summary>The attack intervals after which a silent attacker no longer counts (Lifestone's own: one swing may miss; two in a row mean it stopped, or it is dead).</summary>
    public const double OverdueIntervals = 2;

    private const int Ring = 64;

    private readonly double[] _at = new double[Ring];
    private readonly double[] _damage = new double[Ring];
    private int _next;

    /// <summary>An empty ring.</summary>
    public RecentHits() => Array.Fill(_at, double.NegativeInfinity);

    /// <summary>A hit of <paramref name="damage"/> at <paramref name="now"/>.</summary>
    /// <param name="now">The session clock.</param>
    /// <param name="damage">The damage taken.</param>
    public void Note(double now, double damage)
    {
        (_at[_next], _damage[_next]) = (now, damage);
        _next = (_next + 1) % Ring;
    }

    /// <summary>The read at <paramref name="now"/>.</summary>
    /// <param name="now">The session clock.</param>
    public IncomingRead Read(double now)
    {
        var (sum, biggest, recent, hits, first, last, before) = (0.0, 0.0, 0.0, 0, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);
        for (var i = 0; i < Ring; i++)
        {
            var age = now - _at[i];
            if (age > AverageSeconds)
            {
                continue;
            }

            sum += _damage[i];
            biggest = Math.Max(biggest, _damage[i]);
            if (age <= RecentSeconds)
            {
                recent += _damage[i];
                hits++;
                first = Math.Min(first, _at[i]);
                last = Math.Max(last, _at[i]);
            }
            else
            {
                before = Math.Max(before, _at[i]);
            }
        }

        var average = sum / AverageSeconds;
        if (hits == 0)
        {
            return new IncomingRead(average, biggest, SeedAttackSeconds, double.PositiveInfinity, 0, false);
        }

        var interval = Math.Max(MinAttackSeconds, hits > 1 ? (last - first) / (hits - 1) : Math.Min(SeedAttackSeconds, last - before));
        var since = now - last;
        var live = since <= OverdueIntervals * interval;
        return new IncomingRead(live ? Math.Max(average, recent / hits / interval) : average, biggest, interval, since, hits, live);
    }
}

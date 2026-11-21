namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 14 T (the sprint of 2026-11-12: two fights of 19 s and 14 s, each ended by a trip of over eight minutes; an earlier run left for a trip 72 ms after it reached the ground): the minimum fight stint. For <see cref="Seconds"/> after
/// the run began or the last trip ended, the fight's counters call no trip: a visit has just bought what the purse pays, so nothing a trip could cure is short yet, and a walk home and back costs more than the wait. A stop line (she is out of a
/// component a cast needs) is not a trip of this kind and is never held: it sends her home at once, as before; so do the buff bot's trip and a forced trip. Each trip held is named once a stint, by its trigger.
/// </summary>
/// <param name="seconds">The stint's length; 0 turns the rule off.</param>
/// <param name="saleSeconds">M6 safe load: the sale's floor (<see cref="SaleSeconds"/>); 0 leaves the sale to the stint alone.</param>
public sealed class TripStint(double seconds = TripStint.DefaultSeconds, double saleSeconds = TripStint.DefaultSaleSeconds)
{
    /// <summary>
    /// M6 safe load (the owner, 2026-11-20: she never goes back to town to sell before 40 minutes have passed; other reasons are not held to it): the sale's floor. A trip whose cure is a sale, the pack's weight or room or the loot's worth
    /// (<see cref="Lifestone.Client.Session.Mage.LogisticsVerdict.IsSale"/>) waits this long after the run began or the last trip ended; the swap of sell loot for a better find goes on meanwhile. Live run 6 (2026-11-19 21:38 to
    /// 2026-11-20 17:48 UTC): 31 of its 34 counted trips were full-pack trips, the twenty from 02:35 to 09:14 UTC 18.6 to 24.8 minutes apart, and no stretch on the ground reached an hour.
    /// </summary>
    public const double DefaultSaleSeconds = 2400;

    /// <summary>The sale's floor in seconds (at least the stint's length).</summary>
    public double SaleSeconds { get; } = Math.Max(Math.Max(0, seconds), saleSeconds);

    /// <summary>
    /// The stint's default length. A trip from the outskirts took 331 s, 501 s and 618 s in that run and about 200 s from a tusker ground with the recalls, so fifteen minutes keeps the trips the rule can hold under a third of the time
    /// in the worst case; the wants that call them (a scroll, a sale, a line over its stop) lose nothing by waiting that long.
    /// </summary>
    public const double DefaultSeconds = 900;

    private readonly HashSet<string> _said = new(StringComparer.Ordinal);
    private double _from;

    /// <summary>The stint's length in seconds.</summary>
    public double Seconds { get; } = Math.Max(0, seconds);

    /// <summary>True once a trip has ended in this run (the stint counts from it, not from the run's start).</summary>
    public bool AfterTrip { get; private set; }

    /// <summary>The trips this rule held back, one a trigger a stint.</summary>
    public int Held { get; private set; }

    /// <summary>A trip ended at <paramref name="now"/>: the next stint begins.</summary>
    /// <param name="now">The autopilot's clock.</param>
    public void TripEnded(double now)
    {
        _from = now;
        AfterTrip = true;
        _said.Clear();
    }

    /// <summary>True while the stint is not over at <paramref name="now"/>: a trip that is not a stop line's waits.</summary>
    /// <param name="now">The autopilot's clock.</param>
    public bool Holds(double now) => now - _from < Seconds;

    /// <summary>M6 safe load: true while the sale's floor is not over at <paramref name="now"/> (<see cref="SaleSeconds"/>): a sale's trip waits.</summary>
    /// <param name="now">The autopilot's clock.</param>
    public bool HoldsSale(double now) => now - _from < SaleSeconds;

    /// <summary>M6 safe load: the seconds of the sale's floor left at <paramref name="now"/>.</summary>
    /// <param name="now">The autopilot's clock.</param>
    public double SaleLeft(double now) => Math.Max(0, SaleSeconds - (now - _from));

    /// <summary>The seconds of the stint left at <paramref name="now"/>.</summary>
    /// <param name="now">The autopilot's clock.</param>
    public double Left(double now) => Math.Max(0, Seconds - (now - _from));

    /// <summary>True the first time in this stint that the trip of <paramref name="trigger"/> is held: the caller logs it then, once.</summary>
    /// <param name="trigger">The counters' trigger (<c>sale</c>, <c>learn:&lt;spell&gt;</c>, <c>stock:&lt;line&gt;</c>).</param>
    public bool SayOnce(string trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (!_said.Add(trigger))
        {
            return false;
        }

        Held++;
        return true;
    }
}

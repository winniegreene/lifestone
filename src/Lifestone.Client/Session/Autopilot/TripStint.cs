namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 14 T (the sprint of 2026-11-12: two fights of 19 s and 14 s, each ended by a trip of over eight minutes; an earlier run left for a trip 72 ms after it reached the ground): the minimum fight stint. For <see cref="Seconds"/> after
/// the run began or the last trip ended, the fight's counters call no trip: a visit has just bought what the purse pays, so nothing a trip could cure is short yet, and a walk home and back costs more than the wait. A stop line (she is out of a
/// component a cast needs) is not a trip of this kind and is never held: it sends her home at once, as before; so do the buff bot's trip and a forced trip. Each trip held is named once a stint, by its trigger.
/// </summary>
/// <param name="seconds">The stint's length; 0 turns the rule off.</param>
public sealed class TripStint(double seconds = TripStint.DefaultSeconds)
{
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

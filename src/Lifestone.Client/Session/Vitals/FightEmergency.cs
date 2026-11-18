using System.Globalization;

namespace Lifestone.Client.Session.Vitals;

/// <summary>
/// M5 7 F16 (D91, the owner): the emergency drain's line and its hysteresis. The fight never retreats on the den; when the mage's health falls under the emergency line it casts Drain Health Other I (one gesture, no wind-up) at the
/// living tusker in reach with the most health left, again at once, until the health is at the safe line. The line is live: high enough that the damage expected during three casts plus a margin cannot reach zero.
/// <c>line = max(floor, incoming damage a second x (cast seconds x 3) + margin)</c>, all in health points, cut to the safe line. Engine-free and allocation-free except the log strings.
/// </summary>
public sealed class FightEmergency
{
    /// <summary>
    /// The damage the mage takes a second before the fight has measured any (Lifestone's own seed from the first soak round, 15 fight legs: 1,433 hits in 2,489 fight seconds is 0.576 a second, 5,614 damage over those hits is
    /// 3.92 a hit, so 2.26 a second; rounded up). The line uses the larger of this and the measured rate, so a fresh fight is not taken as unhurt.
    /// </summary>
    public const double SeedIncomingPerSecond = 2.3;

    /// <summary>The casts of the emergency drain the line must survive (Lifestone's own: the cast in flight, the one after it, and one that fizzles).</summary>
    public const int SurvivedCasts = 3;

    /// <summary>The cast seconds of the emergency drain before one is measured (Lifestone's own: <c>FightDrain.CastSeconds</c>, until a landing is timed).</summary>
    public const double SeedCastSeconds = 1.0;

    /// <summary>The weight of a new cast time in the running mean (Lifestone's own, as <c>FightRates.Weight</c>).</summary>
    public const double CastWeight = 0.2;

    /// <summary>The health points by which the line must move before it is logged again (Lifestone's own: five points of the share).</summary>
    public const double LogMovePoints = 5;

    /// <summary>Consecutive casts that did not land (not a resist) after which the emergency is set aside <see cref="AsideSeconds"/> (Lifestone's own bound).</summary>
    public const int MaxFailsInARow = 5;

    /// <summary>The seconds the emergency is set aside after <see cref="MaxFailsInARow"/> casts that did not land (Lifestone's own).</summary>
    public const double AsideSeconds = 10;

    private bool _active;
    private double _lineLogged = double.NaN;
    private int _casts;
    private int _fails;
    private double _asideUntil = double.NegativeInfinity;

    /// <summary>The emergency of the options.</summary>
    /// <param name="floorPercent"><c>--fight-emergency-floor</c>: 0 is off.</param>
    /// <param name="safePercent"><c>--fight-emergency-safe</c>.</param>
    /// <param name="marginPercent"><c>--fight-emergency-margin</c>.</param>
    public FightEmergency(int floorPercent, int safePercent, int marginPercent)
    {
        (FloorPercent, SafePercent, MarginPercent) = (floorPercent, Math.Max(safePercent, floorPercent), marginPercent);
    }

    /// <summary>The least line, in percent of the maximum health (0: off).</summary>
    public int FloorPercent { get; }

    /// <summary>The safe line, in percent.</summary>
    public int SafePercent { get; }

    /// <summary>The margin, in percent of the maximum health.</summary>
    public int MarginPercent { get; }

    /// <summary>True when the emergency is on.</summary>
    public bool Enabled => FloorPercent > 0;

    /// <summary>The emergency drain's cast seconds: the seed, then the running mean of the landed casts.</summary>
    public double CastSeconds { get; private set; } = SeedCastSeconds;

    /// <summary>The emergency drains that landed.</summary>
    public int Landed { get; private set; }

    /// <summary>The line last worked out, in health points (0 before the first).</summary>
    public double LineHealth { get; private set; }

    /// <summary>True while the emergency holds (started under the line, not yet at the safe line).</summary>
    public bool Active => _active;

    /// <summary>The line, in health points: the floor, or the damage expected during <see cref="SurvivedCasts"/> casts plus the margin, whichever is more, cut to the safe line.</summary>
    /// <param name="incomingPerSecond">The measured damage a second (the larger of it and <see cref="SeedIncomingPerSecond"/> is used).</param>
    /// <param name="castSeconds">One emergency cast's seconds.</param>
    /// <param name="healthMax">The maximum health.</param>
    /// <param name="floorPercent">The floor, in percent.</param>
    /// <param name="marginPercent">The margin, in percent.</param>
    /// <param name="safePercent">The safe line, in percent.</param>
    public static double Line(double incomingPerSecond, double castSeconds, uint healthMax, int floorPercent, int marginPercent, int safePercent)
    {
        var floor = healthMax * floorPercent / 100.0;
        var expected = (Math.Max(incomingPerSecond, SeedIncomingPerSecond) * castSeconds * SurvivedCasts) + (healthMax * marginPercent / 100.0);
        return Math.Min(Math.Max(floor, expected), healthMax * safePercent / 100.0);
    }

    /// <summary>The line now at the measured damage rate, kept in <see cref="LineHealth"/>.</summary>
    /// <param name="incomingPerSecond">The measured damage a second.</param>
    /// <param name="healthMax">The maximum health.</param>
    public double LineFor(double incomingPerSecond, uint healthMax) => LineHealth = Line(incomingPerSecond, CastSeconds, healthMax, FloorPercent, MarginPercent, SafePercent);

    /// <summary>
    /// True while the emergency holds at <paramref name="clock"/>: the health under the line starts it and it holds until the health is at the safe line (the hysteresis), unless it is set aside. No allocation.
    /// </summary>
    /// <param name="health">The health.</param>
    /// <param name="healthMax">The maximum health.</param>
    /// <param name="incomingPerSecond">The measured damage a second.</param>
    /// <param name="clock">The session clock.</param>
    public bool Wanted(uint health, uint healthMax, double incomingPerSecond, double clock)
    {
        if (!Enabled || healthMax == 0)
        {
            return false;
        }

        var line = LineFor(incomingPerSecond, healthMax);
        if (!_active && health < line)
        {
            _active = true;
        }
        else if (_active && (ulong)health * 100 >= (ulong)healthMax * (uint)SafePercent)
        {
            _active = false;
            _fails = 0;
        }

        return _active && clock >= _asideUntil;
    }

    /// <summary>M6 8: true while the emergency holds (the health is under its line) and is set aside after its casts failed: no cast is tried, but a health drink costs no cast and no mana, so the fight still asks for one.</summary>
    /// <param name="clock">The session clock.</param>
    public bool AsideNow(double clock) => Enabled && _active && clock < _asideUntil;

    /// <summary>The log line when the line has moved by more than <see cref="LogMovePoints"/> points of the share since it was last logged (or was never logged), else null.</summary>
    /// <param name="healthMax">The maximum health.</param>
    /// <param name="incomingPerSecond">The measured damage a second.</param>
    public string? LineNote(uint healthMax, double incomingPerSecond)
    {
        if (healthMax == 0)
        {
            return null;
        }

        var points = LineHealth * 100.0 / healthMax;
        if (!double.IsNaN(_lineLogged) && Math.Abs(points - _lineLogged) <= LogMovePoints)
        {
            return null;
        }

        _lineLogged = points;
        return string.Create(CultureInfo.InvariantCulture, $"emergency: the line is {LineHealth:0} of {healthMax} ({points:0}%): floor {FloorPercent}%, incoming {Math.Max(incomingPerSecond, SeedIncomingPerSecond):0.0} a second x {CastSeconds:0.0} s x {SurvivedCasts} + margin {MarginPercent}%, safe {SafePercent}%");
    }

    /// <summary>An emergency cast ended. A landing is counted and its seconds join the cast time's mean; a cast that did not land (not a resist) counts toward <see cref="MaxFailsInARow"/>.</summary>
    /// <param name="landed">True when it landed.</param>
    /// <param name="resisted">True when it was resisted (a resist is luck, not a failure).</param>
    /// <param name="seconds">The cast's seconds from start to end.</param>
    /// <param name="clock">The session clock.</param>
    /// <returns>True when the emergency was set aside by this end.</returns>
    public bool Ended(bool landed, bool resisted, double seconds, double clock)
    {
        if (landed)
        {
            Landed++;
            _fails = 0;
            if (seconds > 0 && seconds < 30)
            {
                CastSeconds = _casts++ == 0 ? seconds : CastSeconds + (CastWeight * (seconds - CastSeconds));
            }

            return false;
        }

        if (resisted)
        {
            return false;
        }

        if (++_fails < MaxFailsInARow)
        {
            return false;
        }

        _fails = 0;
        _asideUntil = clock + AsideSeconds;
        return true;
    }

    /// <summary>
    /// The index of the best candidate among <paramref name="healthFractions"/> and <paramref name="distances"/> (parallel, the first <paramref name="count"/>): the most health left, the nearest among those within a hundredth of it;
    /// -1 for none. Candidates marked in <paramref name="excluded"/> are skipped.
    /// </summary>
    /// <param name="healthFractions">Each candidate's health, 0 to 1.</param>
    /// <param name="distances">Each candidate's distance.</param>
    /// <param name="excluded">Candidates to skip (a blocked line), or empty.</param>
    /// <param name="count">How many candidates.</param>
    public static int Fullest(ReadOnlySpan<float> healthFractions, ReadOnlySpan<float> distances, ReadOnlySpan<bool> excluded, int count)
    {
        var pick = new Threat.FullestPick(Threat.FullestRule.EmergencyDrain); // M6 16 E2d: the one comparison, by this pick's rule (the most, the nearest among those within a hundredth of it)
        for (var i = 0; i < count; i++)
        {
            if (i < excluded.Length && excluded[i])
            {
                continue;
            }

            pick.Offer(i, 0, healthFractions[i], distances[i]);
        }

        return pick.Index;
    }
}

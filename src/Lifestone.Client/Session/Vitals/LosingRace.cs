using System.Globalization;

namespace Lifestone.Client.Session.Vitals;

/// <summary>
/// M6 14 the losing race (live 2026-11-15 04:13:27 UTC, <c>cache/m6/14/live/ladder25</c>: the climber, level 69 and 206 health, died on the Tusker Holding's approach while the travel vitals guard cast Drain Health Other I eight times in a
/// row at about 6 health a second against 10 to 20 coming in, and nothing took her home): the seconds Lifestone Recall needs to take her out of a fight, from ACE and the live casts.
/// </summary>
public static class RecallWayOut
{
    /// <summary>
    /// Lifestone Recall's cast, from the send to the server's end of the cast: the median of the 135 live casts of <c>cache/m6/14/live</c> (the Action API's <c>ms=</c> on <c>CastSpell spell=1635</c>, 2026-11-13 to 2026-11-15 UTC: the least
    /// 2.441 s, the median 2.449 s, the most 3.392 s). A measure, not a guess; the cast's gestures are ACE's (<c>SpellFormula.WindupGestures</c>, the spell's scarabs).
    /// </summary>
    public const double CastSeconds = 2.45;

    /// <summary>
    /// ACE's delay between the recall's effect and the teleport: <c>HandleCastSpell_PortalRecall</c> queues <c>DoPreTeleportHide</c>, <c>AddDelaySeconds(2.0f)</c>, then <c>TeleToPosition</c> (@95ef426d
    /// <c>WorldObjects/WorldObject_Magic.cs:1141-1145</c>); the creatures go on hitting her until she is in portal space. Live the teleport came 1.03 s after the client saw the cast end (13:03:19.77 to 13:03:20.80 UTC on 2026-11-14), so
    /// the cast plus the delay is about a second on the safe side.
    /// </summary>
    public const double AceDelaySeconds = 2.0;

    /// <summary>The seconds from the recall's send to the teleport: <see cref="CastSeconds"/> plus <see cref="AceDelaySeconds"/>.</summary>
    public const double Seconds = CastSeconds + AceDelaySeconds;

    /// <summary>The way out in words, for the lines that use it.</summary>
    public static string Words => string.Create(CultureInfo.InvariantCulture, $"{Seconds:0.##} s (Lifestone Recall's cast {CastSeconds:0.##} s, the live median, and ACE's recall delay {AceDelaySeconds:0.#} s)");
}

/// <summary>What <see cref="LosingRace.Decide"/> reads at one moment: her side and the kill side.</summary>
/// <param name="Health">Her health now.</param>
/// <param name="IncomingPerSecond">The damage a second the hostile watch measures, the dead attackers' share taken out (<see cref="Autopilot.HostileWatch.LivingShare"/>).</param>
/// <param name="BiggestHit">The largest hit measured while the attack lasts (a critical can land in the way out).</param>
/// <param name="HealPerSecond">The health a second the best heal she could cast gives her, measured where it can be (<see cref="MeasuredHeals"/>): its landing chance times its heal over its cast seconds.</param>
/// <param name="NextCastSeconds">The seconds of the heal she would cast next: the way out starts after it.</param>
/// <param name="WayOutSeconds">The seconds the way out needs (<see cref="RecallWayOut.Seconds"/>).</param>
public readonly record struct LosingRaceInput(uint Health, double IncomingPerSecond, double BiggestHit, double HealPerSecond, double NextCastSeconds, double WayOutSeconds)
{
    /// <summary>Her health's measured change a second over the last seconds, heals and drains in it (<see cref="HealthTrend"/>), or null when not measured: the two rates decide then.</summary>
    public double? TrendPerSecond { get; init; }

    /// <summary>
    /// The damage a second measured before the dead attackers' share was taken out: the plain mean of every hit of the last <see cref="RecentHits.AverageSeconds"/> (for the words). The incoming is over it only by the derived rate
    /// of a kind heard under that window (<c>AutopilotVitalsGuard.IncomingNow</c>), and the words say so.
    /// </summary>
    public double IncomingAllPerSecond { get; init; } = double.NaN;

    /// <summary>The attackers on her (alive, heard attacking, standing within reach).</summary>
    public int Attackers { get; init; }

    /// <summary>Their known remaining health together (<see cref="KnownTargetHealth"/>), or null when one's health is not known (the kill side cannot be counted on).</summary>
    public double? AttackersHealth { get; init; }

    /// <summary>Her measured damage a second (<see cref="KillRate"/>), or null when not measured.</summary>
    public double? KillPerSecond { get; init; }

    /// <summary>Why the kill side is not read, or null.</summary>
    public string? KillUnknown { get; init; }

    /// <summary>The incoming kind by kind, in words (the measured, the derived while new, the living), or null.</summary>
    public string? IncomingWhy { get; init; }

    /// <summary>The kill side attacker by attacker, in words (the health left, the bolts' landing share and ACE's resist roll, the seconds), or null.</summary>
    public string? KillDetail { get; init; }
}

/// <summary>What <see cref="LosingRace.Decide"/> answers.</summary>
/// <param name="Losing">Her health is falling (measured; with no trend measured, the two rates say so) and the heal she can sustain is under the incoming: every heal only slows the loss.</param>
/// <param name="Lost">Losing, and she cannot kill what is on her in the time her health gives: she leaves now, while the recall's seconds are still covered.</param>
/// <param name="SecondsLeft">The seconds her health lasts at the incoming rate with no heal, after one largest hit.</param>
/// <param name="SecondsNeeded">The seconds the next heal's cast and the way out need.</param>
/// <param name="Need">The health the way out needs: the incoming over its seconds and one largest hit.</param>
public readonly record struct LosingRaceAnswer(bool Losing, bool Lost, double SecondsLeft, double SecondsNeeded, double Need)
{
    /// <summary>Her health is falling (measured), or, with no trend measured, the heal is under the incoming.</summary>
    public bool Falling { get; init; }

    /// <summary>The heal she can sustain holds the incoming.</summary>
    public bool HealHolds { get; init; }

    /// <summary>The seconds she needs to kill what is on her at her measured damage a second (infinity when not read).</summary>
    public double KillSeconds { get; init; } = double.PositiveInfinity;

    /// <summary>The seconds her health gives at the full incoming once she stops healing, before it reaches the way out's need.</summary>
    public double LiveSeconds { get; init; }

    /// <summary>She kills what is on her before her health reaches the way out's need: she fights on.</summary>
    public bool KillsFirst { get; init; }

    /// <summary>Losing, but she kills first: the guard lets the fight go on rather than heal a race the heal cannot win.</summary>
    public bool FightsOn => Losing && KillsFirst;
}

/// <summary>
/// M6 14 the losing race (the owner's survival layer, 2026-11-09: keeping her alive is one layer above walks and fights; check, act, check; the lead, 2026-11-15: fight the way there and kill everything that attacks, a recall is for a fight
/// she cannot win): pure, from measured and known data, both sides of the fight.
/// <list type="bullet">
/// <item><b>Her side</b>: her health's measured trend (<see cref="LosingRaceInput.TrendPerSecond"/>): rising or level is not losing, whatever the two rates say (live 06:04:07 UTC: 166 of 206 and rising, recalled on 15.7 against 16.2).
/// Falling, the heal she can sustain (<see cref="LosingRaceInput.HealPerSecond"/>) against the incoming of the attackers still alive: a heal that holds it is not losing either. With no trend measured, the rates decide alone.</item>
/// <item><b>The kill side</b>: the attackers' known remaining health over her measured damage a second, against the seconds her health gives at the full incoming once she stops healing before it reaches the way out's need (the incoming
/// over <see cref="LosingRaceInput.WayOutSeconds"/> and a largest hit). Killing first, she fights on (<see cref="LosingRaceAnswer.FightsOn"/>); an attacker whose health is not known, or her damage not measured, is no kill she can count on.</item>
/// <item><b>Lost</b> only when both fail: then she leaves at once, while the recall's seconds are still covered. Healing on gains nothing: with the heal under the incoming, a fight that mixes heals and bolts still needs her bolts' seconds
/// at the full incoming (health − incoming × the bolts' seconds stays over the way out's need only when the bolts' seconds are under the seconds her health gives), so the kill side alone decides, and waiting spends the margin (live
/// 06:17:07 to 06:17:11.9 UTC: 122 to 50 over one resisted drain and the retry's second).</item>
/// </list>
/// The caller hands over to the leave (the survival layer's, or the autopilot's retreat by Lifestone Recall) only while the recall is paid; the guard heals on otherwise, and says so.
/// </summary>
public static class LosingRace
{
    /// <summary>The answer for <paramref name="i"/>.</summary>
    /// <param name="i">The inputs.</param>
    public static LosingRaceAnswer Decide(in LosingRaceInput i)
    {
        var incoming = Math.Max(0, i.IncomingPerSecond);
        var biggest = Math.Max(0, i.BiggestHit);
        var wayOut = Math.Max(0, i.WayOutSeconds);
        var needed = Math.Max(0, i.NextCastSeconds) + wayOut;
        var need = (incoming * wayOut) + biggest;
        if (incoming <= 0)
        {
            return new LosingRaceAnswer(false, false, double.PositiveInfinity, needed, need) { HealHolds = true, LiveSeconds = double.PositiveInfinity, KillsFirst = true };
        }

        var heal = Math.Max(0, i.HealPerSecond);
        var holds = heal >= incoming;
        var falling = i.TrendPerSecond is { } trend ? trend <= -HealthTrend.LeastChangePerSecond : !holds; // level (under one point over the trend's window) is not falling
        var losing = falling && !holds;
        var live = Math.Max(0, i.Health - need) / incoming;
        var kill = i.Attackers > 0 && i.AttackersHealth is { } remaining && i.KillPerSecond is { } rate and > 0 ? remaining / rate : double.PositiveInfinity; // no attacker read is no kill to count on
        var killsFirst = kill <= live;
        var left = Math.Max(0, i.Health - biggest) / incoming;
        return new LosingRaceAnswer(losing, losing && !killsFirst, left, needed, need) { Falling = falling, HealHolds = holds, KillSeconds = kill, LiveSeconds = live, KillsFirst = killsFirst };
    }

    /// <summary>The race's verdict in one word: <c>winning</c>, <c>holding</c>, <c>fights-on</c>, <c>losing</c> or <c>lost</c>.</summary>
    /// <param name="a">The answer.</param>
    public static string Verdict(in LosingRaceAnswer a) => a.Lost ? "lost" : a.FightsOn ? "fights-on" : a.Losing ? "losing" : !a.Falling ? "winning" : "holding";

    /// <summary>The race in words with both sides' numbers, for the guard's and the retreat's lines.</summary>
    /// <param name="i">The inputs.</param>
    /// <param name="a">The answer for them.</param>
    public static string Words(in LosingRaceInput i, in LosingRaceAnswer a)
    {
        var c = CultureInfo.InvariantCulture;
        var trend = i.TrendPerSecond is { } t
            ? t <= -HealthTrend.LeastChangePerSecond || t >= HealthTrend.LeastChangePerSecond
                ? string.Create(c, $"her health {i.Health} {(t < 0 ? "falls" : "rises")} {Math.Abs(t):0.#}/s over the last {HealthTrend.WindowSeconds:0} s")
                : string.Create(c, $"her health {i.Health} holds over the last {HealthTrend.WindowSeconds:0} s ({t:0.##}/s, under a point over the window)")
            : string.Create(c, $"her health {i.Health}, its trend not measured yet");
        var dead = double.IsNaN(i.IncomingAllPerSecond) || Math.Abs(i.IncomingAllPerSecond - i.IncomingPerSecond) < 0.05 ? string.Empty
            : i.IncomingAllPerSecond > i.IncomingPerSecond ? string.Create(c, $" ({i.IncomingAllPerSecond:0.#}/s with the dead attackers' hits)")
            : string.Create(c, $" (her hits measure {i.IncomingAllPerSecond:0.#}/s over the last {RecentHits.AverageSeconds:0} s; the rest is the derived rate of attackers heard under {RecentHits.AverageSeconds:0} s)");
        var heal = string.Create(c, $"the heal she can sustain, {i.HealPerSecond:0.#}/s, {(a.HealHolds ? "holds" : "is under")} the incoming {i.IncomingPerSecond:0.#}/s{dead}{(i.IncomingWhy is { } kinds ? " (" + kinds + ")" : string.Empty)}");
        var kill = i.KillUnknown is { } why
            ? $"the kill side not read ({why})"
            : string.Create(c, $"{i.Attackers} attacker(s) with {i.AttackersHealth:0} health left at her {i.KillPerSecond:0.#} damage a second: killed in {(double.IsPositiveInfinity(a.KillSeconds) ? "never" : a.KillSeconds.ToString("0.0", c) + " s")}{(string.IsNullOrEmpty(i.KillDetail) ? string.Empty : " (" + i.KillDetail + ")")}");
        var live = double.IsPositiveInfinity(a.LiveSeconds) ? "no incoming" : string.Create(c, $"{a.LiveSeconds:0.0} s");
        var left = double.IsPositiveInfinity(a.SecondsLeft) ? "no incoming" : string.Create(c, $"{a.SecondsLeft:0.0} s");
        return string.Create(c, $"{Verdict(in a)}: {trend}; {heal}; {kill}, against the {live} her health gives at the full incoming before the way out's need of {a.Need:0} (the incoming over the way out's {i.WayOutSeconds:0.##} s and a largest hit of {i.BiggestHit:0}); after a largest hit she lasts {left}, against the {a.SecondsNeeded:0.0} s the next heal ({i.NextCastSeconds:0.0} s) and the way out ({RecallWayOut.Words}) need");
    }
}

using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Tools;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>Where the death put the player (<c>respawn=</c> of <see cref="DeathLegVerdict"/>).</summary>
public enum DeathRespawn
{
    /// <summary>No arrival was seen after the death's teleport (the death did not happen, or the leg ended first).</summary>
    None,

    /// <summary>The arrival is within <see cref="DeathLegVerdict.LifestoneRadiusMetres"/> of the lifestone the run bound at, or, when the run did not bind, of an object with <see cref="WorldObjectTable.LifestoneFlag"/>.</summary>
    Lifestone,

    /// <summary>The player arrived away from the bound lifestone (or, when the run did not bind, somewhere with no lifestone near).</summary>
    Elsewhere,
}

/// <summary>
/// What the death leg measured (<c>--login-check=bsd-death</c>, M3 3.2; plan section 4.8, D33 to D36): the death tally's counters (<see cref="DeathTally"/>), the scenario's own (the suicides it sent, the gear after the
/// re-gear) and the game's frame figures. A null measure is one the run did not take (the line reads <c>n/a</c>).
/// </summary>
/// <param name="Died">The deaths by the tuskers: the victim notices (<c>died</c>, judged == 1; a suicide sends none).</param>
/// <param name="Suicides">The suicides the scenario sent and saw complete (<c>suicides</c>, report-only).</param>
/// <param name="Deaths">The NumDeaths delta, suicides included (<c>deaths</c>, report-only).</param>
/// <param name="VitaePercent">The vitae after the death (<c>vitae</c>, report-only: D35, it climbs to ACE's 40% cap).</param>
/// <param name="Respawn">Where the player arrived (<c>respawn</c>, judged == lifestone).</param>
/// <param name="Corpses">The player's corpses seen (<c>corpse</c>, report-only).</param>
/// <param name="Purges">The enchantment purges seen (<c>purged</c>, report-only).</param>
/// <param name="Protected">The blows the lifestone protection met (<c>protected</c>, report-only).</param>
/// <param name="VitalsPercent">The vitals after the arrival as one share (<c>vitals_pct</c>, report-only; ACE sends 75), or null/NaN.</param>
/// <param name="GearRestored">True when the re-gear left the kit wielded again (<c>gear_restored</c>, judged).</param>
/// <param name="Worn">How many of the five pieces are worn after the re-gear (<c>worn</c>, report-only).</param>
/// <param name="HitsTaken">The 0x01B2 notices (<c>hits_taken</c>, judged &gt;= 1: D36).</param>
/// <param name="DamageTaken">The damage taken (<c>damage_taken</c>, judged &gt; 0: D36).</param>
/// <param name="Evades">The 0x01B4 notices (<c>evades</c>, report-only).</param>
/// <param name="HealthMinPercent">The lowest health share seen (<c>health_min_pct</c>, report-only), or null/NaN.</param>
/// <param name="DeathSeconds">How long the unbuffed fight lasted until the death (<c>death_s</c>, report-only), or null.</param>
/// <param name="DeathFrameMaxMs">The slowest frame from the Dead motion, through portal space and the arrival's load, to the first frame after the player is present again (<c>death_frame_max_ms</c>, report-only: a new judge, section 5), or null.</param>
/// <param name="FightFrameMaxMs">The slowest frame of the unbuffed fight (<c>fight_frame_max_ms</c>, report-only here), or null.</param>
/// <param name="CorpsesStale">The corpses of the player that came outside a death's window, the den's older ones (<c>corpses_stale</c>, report-only; <c>corpse</c> counts the death's own only: review R1).</param>
/// <param name="VitaePaid">The experience the vitae stage paid to clear the vitae (<c>vitae_paid</c>, report-only; D37), or null when the stage did not run (<c>n/a</c>); 0 when there was none to pay.</param>
/// <param name="VitaeAfter">The vitae percentage after the vitae stage (<c>vitae_after</c>, report-only), or null when the stage did not run.</param>
/// <param name="Protection">How the lifestone protection ended, <c>dispelled</c>, <c>expired</c> or <c>none</c> (<c>protection</c>, report-only: ACE dispels it on every teleport, so <c>protected</c> stays 0 live; review R2).</param>
public readonly record struct DeathLegMeasures(
    int Died,
    int Suicides,
    int Deaths,
    int VitaePercent,
    DeathRespawn Respawn,
    int Corpses,
    int Purges,
    int Protected,
    double? VitalsPercent,
    bool GearRestored,
    int Worn,
    int HitsTaken,
    long DamageTaken,
    int Evades,
    double? HealthMinPercent,
    double? DeathSeconds,
    double? DeathFrameMaxMs,
    double? FightFrameMaxMs,
    int CorpsesStale = 0,
    string Protection = DeathTally.ProtectionNone,
    long? VitaePaid = null,
    int? VitaeAfter = null);

/// <summary>
/// The death leg's verdict (<c>--login-check=bsd-death</c>, M3 3.2 s0), composed from the scenario's failure (or null) and the measures, so it is built and tested without Godot. The line is
/// <c>bsd-death done ok=&lt;true|false&gt; died= suicides= vitae= respawn=lifestone|elsewhere|none corpse= purged= protected= vitals_pct= gear_restored=yes|no worn= hits_taken= damage_taken= evades= health_min_pct= death_s=
/// death_frame_max_ms= fight_frame_max_ms= deaths= corpses_stale= protection=dispelled|expired|none vitae_paid= vitae_after=[ frame_over_50=yes]</c> (no name, no account), logged under the <c>logincheck</c> category. The fields are additive: a new one goes at the end.
/// </summary>
/// <remarks>
/// <para><b>Judged</b> (<c>ok=</c>; m3-plan section 4.8, D33 to D36): no failure, <c>died == 1</c> (exactly one death by the tuskers: the victim notices; the suicide that purges the buffs sends none), <c>respawn=lifestone</c>
/// (D34), <c>gear_restored=yes</c>, and DoD 9's <c>damage_taken &gt; 0</c> and <c>hits_taken &gt;= 1</c> (D36: judged here, in the unbuffed leg, and report-only in <c>bsd-fight</c>). Everything else is report-only in this gate
/// (a new judge is report-only in its first gate); <c>vitae=</c> is printed, not judged (D35: BSD Mage's vitae climbs to the cap and stays there). Since M3 4.1 lane c <c>death_frame_max_ms</c> is judged as the fight's frames are (section 5): over <see cref="FrameFailMs"/> fails the run, over
/// <see cref="FrameWarnMs"/> the line ends with <c>frame_over_50=yes</c> (the lead judges the worst of three runs). An unmeasured frame (n/a) is not judged.</para>
/// </remarks>
public static class DeathLegVerdict
{
    /// <summary>How near a lifestone object the arrival must be for <c>respawn=lifestone</c> (m3-plan section 4.8: within 15 m).</summary>
    public const float LifestoneRadiusMetres = 15f;

    /// <summary>A death whose slowest frame is over this many milliseconds fails (m3-plan section 5, M3 4.1 lane c: the fight's frame rule, judged for <c>death_frame_max_ms</c>).</summary>
    public const double FrameFailMs = 75.0;

    /// <summary>A death whose slowest frame is over this many milliseconds prints <c>frame_over_50=yes</c> and passes (the lead's worst of three).</summary>
    public const double FrameWarnMs = 50.0;

    /// <summary>Composes the leg's verdict from the scenario's failure and the measures.</summary>
    /// <param name="failure">The scenario's or the runner's failure, or null when there was none.</param>
    /// <param name="measures">What the tally and the game measured.</param>
    /// <param name="judgeDeath">False for a run whose stages did not include the death (<c>--death-stages=vitae</c>): the death's fields are printed and not judged, only a failure fails it.</param>
    public static DeathLegOutcome Compose(string? failure, in DeathLegMeasures measures, bool judgeDeath = true)
    {
        var inv = CultureInfo.InvariantCulture;
        var problems = new List<string>();
        if (!string.IsNullOrEmpty(failure))
        {
            problems.Add("the death loop failed: " + failure);
        }

        if (!judgeDeath)
        {
            return Finish(measures, problems, inv);
        }

        if (measures.Died != 1)
        {
            problems.Add(string.Create(inv, $"died={measures.Died} (exactly 1 death by the tuskers expected)"));
        }

        if (measures.Respawn != DeathRespawn.Lifestone)
        {
            problems.Add("respawn=" + RespawnText(measures.Respawn) + " (lifestone expected: an arrival within " + LifestoneRadiusMetres.ToString("0", inv) + " m of the lifestone the bind used, or of a lifestone object when the run did not bind)");
        }

        if (!measures.GearRestored)
        {
            problems.Add("gear_restored=no (the kit wielded again expected)");
        }

        if (measures.DamageTaken <= 0)
        {
            problems.Add(string.Create(inv, $"damage_taken={measures.DamageTaken} (> 0 expected, D36)"));
        }

        if (measures.HitsTaken < 1)
        {
            problems.Add(string.Create(inv, $"hits_taken={measures.HitsTaken} (>= 1 expected, D36)"));
        }

        if (measures.DeathFrameMaxMs is { } frame && !double.IsNaN(frame) && frame > FrameFailMs)
        {
            problems.Add(string.Create(inv, $"death_frame_max_ms={frame:0.0} (at most {FrameFailMs:0} expected)"));
        }

        return Finish(measures, problems, inv);
    }

    private static DeathLegOutcome Finish(in DeathLegMeasures measures, List<string> problems, CultureInfo inv)
    {
        var ok = problems.Count == 0;
        var line = string.Create(
            inv,
            $"bsd-death done ok={(ok ? "true" : "false")} died={measures.Died} suicides={measures.Suicides} vitae={measures.VitaePercent} respawn={RespawnText(measures.Respawn)} corpse={measures.Corpses} purged={measures.Purges} protected={measures.Protected} vitals_pct={Pct(measures.VitalsPercent)} gear_restored={(measures.GearRestored ? "yes" : "no")} worn={measures.Worn} hits_taken={measures.HitsTaken} damage_taken={measures.DamageTaken} evades={measures.Evades} health_min_pct={Pct(measures.HealthMinPercent)} death_s={Seconds(measures.DeathSeconds)} death_frame_max_ms={Pct(measures.DeathFrameMaxMs)} fight_frame_max_ms={Pct(measures.FightFrameMaxMs)} deaths={measures.Deaths} corpses_stale={measures.CorpsesStale} protection={measures.Protection ?? DeathTally.ProtectionNone} vitae_paid={(measures.VitaePaid is { } paid ? paid.ToString(inv) : "n/a")} vitae_after={(measures.VitaeAfter is { } after ? after.ToString(inv) : "n/a")}{(measures.DeathFrameMaxMs is { } worst && !double.IsNaN(worst) && worst > FrameWarnMs ? " frame_over_50=yes" : string.Empty)}");
        return new DeathLegOutcome(ok, line, problems);
    }

    /// <summary>
    /// Where the player arrived after the death, against any lifestone object: <see cref="DeathRespawn.None"/> when the tally saw no arrival, <see cref="DeathRespawn.Lifestone"/> when an object with
    /// <see cref="WorldObjectTable.LifestoneFlag"/> is within <see cref="LifestoneRadiusMetres"/> of it (the table must hold the objects the arrival created), else <see cref="DeathRespawn.Elsewhere"/>. The rule of a run
    /// that did not bind (the stand-in's death check, a <c>--death-stages</c> run without the bind); a run that bound passes its lifestone to the other overload.
    /// </summary>
    /// <param name="tally">The death tally.</param>
    /// <param name="objects">The session's object table.</param>
    public static DeathRespawn ClassifyRespawn(DeathTally tally, WorldObjectTable objects) => ClassifyRespawn(tally, objects, null);

    /// <summary>
    /// Where the player arrived after the death: <see cref="DeathRespawn.None"/> when the tally saw no arrival; with <paramref name="boundLifestone"/> (the position of the lifestone this run bound at),
    /// <see cref="DeathRespawn.Lifestone"/> when the arrival is within <see cref="LifestoneRadiusMetres"/> of it (3D, ACE's global frame) and <see cref="DeathRespawn.Elsewhere"/> otherwise, another lifestone near the arrival
    /// included (a mage bound elsewhere by an earlier run respawns there, and that must not read as this run's bind holding: M3 3.2 review, package 2 finding 5); without it, the other overload's rule against any lifestone
    /// object in the table. Allocation-free.
    /// </summary>
    /// <param name="tally">The death tally.</param>
    /// <param name="objects">The session's object table.</param>
    /// <param name="boundLifestone">The position of the lifestone the run's bind used, or null when the run did not bind.</param>
    public static DeathRespawn ClassifyRespawn(DeathTally tally, WorldObjectTable objects, CellFrame? boundLifestone)
    {
        ArgumentNullException.ThrowIfNull(tally);
        ArgumentNullException.ThrowIfNull(objects);
        if (!tally.HasArrival)
        {
            return DeathRespawn.None;
        }

        var arrival = new CellFrame(tally.ArrivalCell, tally.ArrivalFrame);
        if (boundLifestone is { } bound)
        {
            return PositionAgreement.DistanceMetres(in arrival, in bound) <= LifestoneRadiusMetres ? DeathRespawn.Lifestone : DeathRespawn.Elsewhere;
        }

        return objects.NearestWithFlags(WorldObjectTable.LifestoneFlag, in arrival, LifestoneRadiusMetres, out _) ? DeathRespawn.Lifestone : DeathRespawn.Elsewhere;
    }

    /// <summary>
    /// The tally's part of the measures: <see cref="DeathLegMeasures.Died"/> is the victim notices, the vitals are the tally's, and the respawn is classified against <paramref name="objects"/>. The scenario's own figures
    /// (the suicides, the gear, the seconds and the frames) are the arguments.
    /// </summary>
    /// <param name="tally">The death tally.</param>
    /// <param name="objects">The session's object table.</param>
    /// <param name="suicides">The suicides the scenario sent and saw complete.</param>
    /// <param name="gearRestored">True when the re-gear left the kit wielded.</param>
    /// <param name="worn">How many of the five pieces are worn.</param>
    /// <param name="deathSeconds">How long the unbuffed fight lasted until the death, or null.</param>
    /// <param name="deathFrameMaxMs">The slowest frame of the death, or null.</param>
    /// <param name="fightFrameMaxMs">The slowest frame of the unbuffed fight, or null.</param>
    /// <param name="boundLifestone">The position of the lifestone the run's bind used, or null when the run did not bind (the respawn is then classified against any lifestone object).</param>
    public static DeathLegMeasures Measure(DeathTally tally, WorldObjectTable objects, int suicides, bool gearRestored, int worn, double? deathSeconds, double? deathFrameMaxMs, double? fightFrameMaxMs, CellFrame? boundLifestone = null)
    {
        ArgumentNullException.ThrowIfNull(tally);
        return new DeathLegMeasures(
            tally.VictimNotices,
            suicides,
            tally.Deaths,
            tally.VitaePercent,
            ClassifyRespawn(tally, objects, boundLifestone),
            tally.CorpsesSeen,
            tally.Purges,
            tally.ProtectedHits,
            tally.VitalsAfterArrivalPercent,
            gearRestored,
            worn,
            tally.HitsTaken,
            tally.DamageTaken,
            tally.Evades,
            tally.HealthMinPercent,
            deathSeconds,
            deathFrameMaxMs,
            fightFrameMaxMs,
            tally.StaleCorpses,
            tally.Protection);
    }

    private static string RespawnText(DeathRespawn respawn) => respawn switch
    {
        DeathRespawn.Lifestone => "lifestone",
        DeathRespawn.Elsewhere => "elsewhere",
        _ => "none",
    };

    private static string Pct(double? value) => value is { } v && !double.IsNaN(v) ? v.ToString("0.0", CultureInfo.InvariantCulture) : "n/a";

    private static string Seconds(double? value) => value is { } v && !double.IsNaN(v) ? Math.Floor(v).ToString("0", CultureInfo.InvariantCulture) : "n/a";
}

/// <summary>The death leg's verdict: whether it held, the line to log (after the <c>[logincheck]</c> category) and, when it did not hold, what did not.</summary>
/// <param name="Ok">True when the judged fields hold.</param>
/// <param name="Line">The verdict line, <c>bsd-death done ok=... </c>.</param>
/// <param name="Problems">What did not hold, one sentence each; empty when <paramref name="Ok"/>.</param>
public sealed record DeathLegOutcome(bool Ok, string Line, IReadOnlyList<string> Problems);

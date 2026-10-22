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

/// <summary>What the corpse trip came to (<c>corpse_looted=</c> of <see cref="DeathLegVerdict"/>, M4 2.2 s1; report-only in its first gate).</summary>
public enum CorpseLoot
{
    /// <summary>No trip was made: the option or the stage was off, or a precondition failed (named in the log).</summary>
    Skipped,

    /// <summary>A trip began and no item came back (an abort before the first pick-up settled, a refused open, an empty corpse, every pick-up refused or timed out).</summary>
    No,

    /// <summary>
    /// M4 2.2 fix R2: some of the corpse's items came back and some did not (<c>0 &lt; items_recovered &lt; entries</c> of the opened view): an abort, a refusal, a timeout or an entry that never resolved cut the recovery short. The regear
    /// spawns what is still missing.
    /// </summary>
    Partial,

    /// <summary>Every entry of the opened view came back: the number recovered equals the number of entries at the open, with no refusal, no timeout and no unresolved skip.</summary>
    Yes,
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
/// <param name="CorpseLooted">What the trip back to the mage's own corpse came to (<c>corpse_looted</c>, report-only in its first gate; M4 2.2 s1).</param>
/// <param name="ItemsRecovered">The items the trip took out of the corpse (<c>items_recovered</c>, report-only).</param>
/// <param name="TripSeconds">The seconds from the trip's teleport to the corpse until it ended (<c>trip_s</c>, report-only), or null when no trip was made.</param>
/// <param name="TripAbort">The reason the trip aborted (<c>trip_abort</c>, report-only), or <see cref="DeathLegVerdict.TripAbortNone"/>.</param>
/// <param name="Protection">How the lifestone protection ended, <c>dispelled</c>, <c>expired</c> or <c>none</c> (<c>protection</c>, report-only: ACE dispels it on every teleport, so <c>protected</c> stays 0 live; review R2).</param>
/// <param name="Rebuffed">M4 3.1 d1: <c>yes</c> when the rebuff stage's buff cycle ended with every profile line up, <c>no</c> when it failed or ended with a line down, <c>skipped</c> when the stage is off or never ran (<c>rebuffed</c>, report-only).</param>
/// <param name="DenKills">M4 3.1 d1: the kills of the fight stage's inner scenario (<c>den_kills</c>, report-only; 0 when the stage did not run).</param>
/// <param name="Decoys">M4 3.1 d1: the decoys the pack held when the wait among the tuskers began (<c>decoys</c>, report-only), or null (<c>n/a</c>: <c>--death-decoys off</c>).</param>
/// <param name="DecoySafe">M4 3.1 d1: the gear stage's <see cref="Lifestone.Client.Session.Mage.DecoyCheck.Field"/> (<c>yes</c> only when the check was made and the needed items cannot drop), or <c>n/a</c> (<c>--death-decoys off</c>, or no gear stage: no check was made) (<c>decoy_safe</c>, report-only).</param>
/// <param name="DecoysDropped">M4 3.1 d1: the decoys the corpse's own listing held (<c>decoys_dropped</c>, report-only), or null (<c>n/a</c>: decoys off, or the trip never read a listing).</param>
/// <param name="GearDropped">M4 3.1 d1: the listed entries the loops need (<see cref="Lifestone.Client.Session.Mage.DeathDropModel.IsNeeded"/>) (<c>gear_dropped</c>, report-only), or null (<c>n/a</c>).</param>
/// <param name="DecoysRecovered">M4 3.1 d1: the listed decoys that are in the pack after the trip (<c>decoys_recovered</c>, report-only), or null (<c>n/a</c>).</param>
/// <param name="PretripHeals">M4 3.1 d1 follow-up F1: the Heal Self casts of the pre-trip recovery (<c>pretrip_heals</c>, report-only; 0 when it did not run).</param>
/// <param name="GearLeft">M4 3.1 fix (review R2): the listed items the loops need that the trip did not take from the corpse (<c>gear_left</c>, report-only in its first gate: a duplicate, since the regear ran before the trip), or null (<c>n/a</c>: no listing was read).</param>
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
    int? VitaeAfter = null,
    CorpseLoot CorpseLooted = CorpseLoot.Skipped,
    int ItemsRecovered = 0,
    double? TripSeconds = null,
    string TripAbort = DeathLegVerdict.TripAbortNone,
    string Rebuffed = DeathLegVerdict.RebuffedSkipped,
    int DenKills = 0,
    int? Decoys = null,
    string DecoySafe = DeathLegVerdict.NotApplicable,
    int? DecoysDropped = null,
    int? GearDropped = null,
    int? DecoysRecovered = null,
    int PretripHeals = 0,
    int? GearLeft = null);

/// <summary>
/// The death leg's verdict (<c>--login-check=bsd-death</c>, M3 3.2 s0), composed from the scenario's failure (or null) and the measures, so it is built and tested without Godot. The line is
/// <c>bsd-death done ok=&lt;true|false&gt; died= suicides= vitae= respawn=lifestone|elsewhere|none corpse= purged= protected= vitals_pct= gear_restored=yes|no worn= hits_taken= damage_taken= evades= health_min_pct= death_s=
/// death_frame_max_ms= fight_frame_max_ms= deaths= corpses_stale= protection=dispelled|expired|none vitae_paid= vitae_after= corpse_looted=yes|partial|no|skipped items_recovered= trip_s= trip_abort= rebuffed=yes|no|skipped den_kills= decoys= decoy_safe=yes|no|n/a
/// decoys_dropped= gear_dropped= decoys_recovered= pretrip_heals= gear_left=[ frame_over_50=yes]</c> (no name, no account), logged under the <c>logincheck</c> category. The fields are additive: a new one goes at the end (before the optional <c>frame_over_50=yes</c>); the game's leg line inserts <c>mode_mismatch=</c> there as well (<c>DeathLegRunner.LegLine</c>, review R21), as the hunt leg's line does.
/// </summary>
/// <remarks>
/// <para><b>Judged</b> (<c>ok=</c>; m3-plan section 4.8, D33 to D36): no failure, <c>died == 1</c> (exactly one death by the tuskers: the victim notices; the suicide that purges the buffs sends none), <c>respawn=lifestone</c>
/// (D34), <c>gear_restored=yes</c>, and DoD 9's <c>damage_taken &gt; 0</c> and <c>hits_taken &gt;= 1</c> (D36: judged here, in the unbuffed leg, and report-only in <c>bsd-fight</c>). Everything else is report-only in this gate
/// (a new judge is report-only in its first gate; M4 3.1 d1's <c>rebuffed= den_kills= decoys= decoy_safe= decoys_dropped= gear_dropped= decoys_recovered= pretrip_heals= gear_left=</c> are report-only; a gear item left on the corpse is a Warn and <c>gear_left=</c>, never a failure (review R2); a death in the rebuff or the fight still fails the run through the failure); <c>vitae=</c> is printed, not judged (D35: BSD Mage's vitae climbs to the cap and stays there). Since M3 4.1 lane c <c>death_frame_max_ms</c> is judged as the fight's frames are (section 5): over <see cref="FrameFailMs"/> fails the run, over
/// <see cref="FrameWarnMs"/> the line ends with <c>frame_over_50=yes</c> (the lead judges the worst of three runs). An unmeasured frame (n/a) is not judged.</para>
/// </remarks>
public static class DeathLegVerdict
{
    /// <summary>The <c>trip_abort=</c> text when the trip did not abort (or was not made).</summary>
    public const string TripAbortNone = "none";

    /// <summary><c>rebuffed=</c> when the rebuff stage's buff cycle ended with every profile line up.</summary>
    public const string RebuffedYes = "yes";

    /// <summary><c>rebuffed=</c> when the rebuff stage failed or ended with a profile line down.</summary>
    public const string RebuffedNo = "no";

    /// <summary><c>rebuffed=</c> when the stage is off or never ran.</summary>
    public const string RebuffedSkipped = "skipped";

    /// <summary>The text of a decoy field that does not apply (<c>--death-decoys off</c>) or was not measured.</summary>
    public const string NotApplicable = "n/a";

    /// <summary>How near a lifestone object the arrival must be for <c>respawn=lifestone</c> (m3-plan section 4.8: within 15 m).</summary>
    public const float LifestoneRadiusMetres = 15f;

    /// <summary>A death whose slowest frame is over this many milliseconds fails (m3-plan section 5, M3 4.1 lane c: the fight's frame rule, judged for <c>death_frame_max_ms</c>).</summary>
    public const double FrameFailMs = 75.0;

    /// <summary>A death whose slowest frame is over this many milliseconds prints <c>frame_over_50=yes</c> and passes (the lead's worst of three).</summary>
    public const double FrameWarnMs = 50.0;

    /// <summary>The flag the line ends with when the death's slowest frame is over <see cref="FrameWarnMs"/>.</summary>
    public const string FrameOver50Flag = " frame_over_50=yes";

    /// <summary>
    /// Appends a field the verdict does not own (the game leg's <c>mode_mismatch=&lt;n&gt;</c>) to <paramref name="verdictLine"/> before the optional <see cref="FrameOver50Flag"/>, the order the class's own rule gives a new field and the hunt leg's
    /// line has (M4 3.1 fix, review R21).
    /// </summary>
    /// <param name="verdictLine">The verdict's line.</param>
    /// <param name="field">The field, <c>key=value</c>.</param>
    public static string WithField(string verdictLine, string field)
    {
        ArgumentNullException.ThrowIfNull(verdictLine);
        ArgumentNullException.ThrowIfNull(field);
        return verdictLine.EndsWith(FrameOver50Flag, StringComparison.Ordinal)
            ? string.Concat(verdictLine.AsSpan(0, verdictLine.Length - FrameOver50Flag.Length), " ", field, FrameOver50Flag)
            : verdictLine + " " + field;
    }

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
            $"bsd-death done ok={(ok ? "true" : "false")} died={measures.Died} suicides={measures.Suicides} vitae={measures.VitaePercent} respawn={RespawnText(measures.Respawn)} corpse={measures.Corpses} purged={measures.Purges} protected={measures.Protected} vitals_pct={Pct(measures.VitalsPercent)} gear_restored={(measures.GearRestored ? "yes" : "no")} worn={measures.Worn} hits_taken={measures.HitsTaken} damage_taken={measures.DamageTaken} evades={measures.Evades} health_min_pct={Pct(measures.HealthMinPercent)} death_s={Seconds(measures.DeathSeconds)} death_frame_max_ms={Pct(measures.DeathFrameMaxMs)} fight_frame_max_ms={Pct(measures.FightFrameMaxMs)} deaths={measures.Deaths} corpses_stale={measures.CorpsesStale} protection={measures.Protection ?? DeathTally.ProtectionNone} vitae_paid={(measures.VitaePaid is { } paid ? paid.ToString(inv) : "n/a")} vitae_after={(measures.VitaeAfter is { } after ? after.ToString(inv) : "n/a")} corpse_looted={CorpseText(measures.CorpseLooted)} items_recovered={measures.ItemsRecovered} trip_s={Pct(measures.TripSeconds)} trip_abort={measures.TripAbort ?? TripAbortNone} rebuffed={measures.Rebuffed ?? RebuffedSkipped} den_kills={measures.DenKills} decoys={Count(measures.Decoys)} decoy_safe={measures.DecoySafe ?? NotApplicable} decoys_dropped={Count(measures.DecoysDropped)} gear_dropped={Count(measures.GearDropped)} decoys_recovered={Count(measures.DecoysRecovered)} pretrip_heals={measures.PretripHeals} gear_left={Count(measures.GearLeft)}{(measures.DeathFrameMaxMs is { } worst && !double.IsNaN(worst) && worst > FrameWarnMs ? FrameOver50Flag : string.Empty)}");
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

    internal static string CorpseText(CorpseLoot loot) => loot switch
    {
        CorpseLoot.Yes => "yes",
        CorpseLoot.Partial => "partial",
        CorpseLoot.No => "no",
        _ => "skipped",
    };

    private static string RespawnText(DeathRespawn respawn) => respawn switch
    {
        DeathRespawn.Lifestone => "lifestone",
        DeathRespawn.Elsewhere => "elsewhere",
        _ => "none",
    };

    private static string Count(int? value) => value is { } v ? v.ToString(CultureInfo.InvariantCulture) : NotApplicable;

    private static string Pct(double? value) => value is { } v && !double.IsNaN(v) ? v.ToString("0.0", CultureInfo.InvariantCulture) : "n/a";

    private static string Seconds(double? value) => value is { } v && !double.IsNaN(v) ? Math.Floor(v).ToString("0", CultureInfo.InvariantCulture) : "n/a";
}

/// <summary>The death leg's verdict: whether it held, the line to log (after the <c>[logincheck]</c> category) and, when it did not hold, what did not.</summary>
/// <param name="Ok">True when the judged fields hold.</param>
/// <param name="Line">The verdict line, <c>bsd-death done ok=... </c>.</param>
/// <param name="Problems">What did not hold, one sentence each; empty when <paramref name="Ok"/>.</param>
public sealed record DeathLegOutcome(bool Ok, string Line, IReadOnlyList<string> Problems);

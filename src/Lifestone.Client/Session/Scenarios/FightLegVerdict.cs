using System.Globalization;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// What the fight leg measured (<c>--login-check=bsd-fight</c>, M3 3.1; plan section 4.7 and section 5, "The fight's frames"): the tally's counters
/// (<see cref="FightTally"/>) and the game's frame and projectile measures. A null measure is one the run did not take (the line reads <c>n/a</c>).
/// </summary>
/// <param name="Kills">The 0x01AD kills (<c>kills</c>, judged &gt;= 1).</param>
/// <param name="DamageDealt">The sum of the player's spell-damage lines (<c>damage_dealt</c>, judged &gt; 0).</param>
/// <param name="SpellsCast">The casts the scenario sent (<c>spells_cast</c>, report-only).</param>
/// <param name="Misses">The casts that ended without a projectile or a damage line (<c>misses</c>, report-only).</param>
/// <param name="XpGained">The total-experience delta (<c>xp_gained</c>, report-only).</param>
/// <param name="DamageTaken">The damage taken (<c>damage_taken</c>, report-only here; the <c>bsd-death</c> leg judges it, D36).</param>
/// <param name="HitsTaken">The 0x01B2 notices (<c>hits_taken</c>, report-only here; the <c>bsd-death</c> leg judges it, D36).</param>
/// <param name="Evades">The 0x01B4 notices (<c>evades</c>, report-only).</param>
/// <param name="Projectiles">The spell projectiles the game showed (<c>AvatarStats.ProjectilesShown</c>; <c>projectiles</c>, report-only), or null.</param>
/// <param name="CastFrameMaxMs">The slowest frame while a cast window was open (<c>cast_frame_max_ms</c>; judged against the 75 ms limit since 3.2, see <see cref="FightLegVerdict"/>), or null.</param>
/// <param name="FightFrameMaxMs">The slowest frame of the whole fight (<c>fight_frame_max_ms</c>; judged against the 75 ms limit since 3.2), or null.</param>
/// <param name="BsdMinFps">The lowest frames per second sampled in the den (<c>bsd_min_fps</c>, report-only), or null.</param>
/// <param name="FightSeconds">How long the fight lasted (<c>fight_s</c>, report-only), or null.</param>
public readonly record struct FightLegMeasures(
    int Kills,
    long DamageDealt,
    int SpellsCast,
    int Misses,
    long XpGained,
    long DamageTaken,
    int HitsTaken,
    int Evades,
    int? Projectiles,
    double? CastFrameMaxMs,
    double? FightFrameMaxMs,
    double? BsdMinFps,
    double? FightSeconds);

/// <summary>
/// The fight leg's verdict (<c>--login-check=bsd-fight</c>, M3 3.1 s0): composed from the scenario's failure (or null) and the measures, so it is built and tested without Godot. The line is
/// <c>bsd-fight done ok=&lt;true|false&gt; kills= damage_dealt= spells_cast= misses= xp_gained= damage_taken= hits_taken= evades= projectiles= cast_frame_max_ms= fight_frame_max_ms= bsd_min_fps= fight_s=[ frame_over_50=yes]</c>
/// (no name, no account), logged under the <c>logincheck</c> category.
/// </summary>
/// <remarks>
/// <para><b>Judged</b> (<c>ok=</c>, m3-plan section 5, "The fight's frames"): no failure, <c>kills &gt;= 1</c> and <c>damage_dealt &gt; 0</c>, and (since M3 3.2 s0) the frame rule: a run whose <c>cast_frame_max_ms</c> or
/// <c>fight_frame_max_ms</c> is over <see cref="FrameFailMs"/> (75 ms) fails; over <see cref="FrameWarnMs"/> (50 ms) the line ends with <c>frame_over_50=yes</c> (the lead's worst of three runs reads it) and the run still passes.
/// An unmeasured frame (n/a) is not judged. <c>damage_taken</c> and <c>hits_taken</c> stay report-only here (D36: the buffed fight meets no blows; the unbuffed <c>bsd-death</c> leg judges them). Every other field is
/// report-only in this gate (a new judge is report-only in its first gate). <c>damage_dealt</c> excludes the killing blow, for which ACE writes no damage line (<see cref="FightTally"/>), so a kill without earlier hits reads <c>damage_dealt=0</c> and fails.</para>
/// </remarks>
public static class FightLegVerdict
{
    /// <summary>A fight whose slowest frame is over this many milliseconds fails (m3-plan section 5: no frame over 75 ms in a cast or a fight).</summary>
    public const double FrameFailMs = 75.0;

    /// <summary>A fight whose slowest frame is over this many milliseconds prints <c>frame_over_50=yes</c> and passes (M2 DoD 15's 50 ms, kept as the lead's worst-of-three signal).</summary>
    public const double FrameWarnMs = 50.0;

    /// <summary>Composes the leg's verdict from the scenario's failure and the measures.</summary>
    /// <param name="failure">The scenario's or the runner's failure, or null when there was none.</param>
    /// <param name="measures">What the tally and the game measured.</param>
    public static FightLegOutcome Compose(string? failure, in FightLegMeasures measures)
    {
        var problems = new List<string>();
        if (!string.IsNullOrEmpty(failure))
        {
            problems.Add("the fight failed: " + failure);
        }

        if (measures.Kills < 1)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"kills={measures.Kills} (>= 1 expected)"));
        }

        if (measures.DamageDealt <= 0)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"damage_dealt={measures.DamageDealt} (> 0 expected)"));
        }

        AddFrameProblem(problems, "cast_frame_max_ms", measures.CastFrameMaxMs);
        AddFrameProblem(problems, "fight_frame_max_ms", measures.FightFrameMaxMs);
        var over50 = IsOver(measures.CastFrameMaxMs, FrameWarnMs) || IsOver(measures.FightFrameMaxMs, FrameWarnMs);

        var ok = problems.Count == 0;
        var inv = CultureInfo.InvariantCulture;
        var line = string.Create(
            inv,
            $"bsd-fight done ok={(ok ? "true" : "false")} kills={measures.Kills} damage_dealt={measures.DamageDealt} spells_cast={measures.SpellsCast} misses={measures.Misses} xp_gained={measures.XpGained} damage_taken={measures.DamageTaken} hits_taken={measures.HitsTaken} evades={measures.Evades} projectiles={(measures.Projectiles is { } p ? p.ToString(inv) : "n/a")} cast_frame_max_ms={Ms(measures.CastFrameMaxMs)} fight_frame_max_ms={Ms(measures.FightFrameMaxMs)} bsd_min_fps={Ms(measures.BsdMinFps)} fight_s={(measures.FightSeconds is { } s && !double.IsNaN(s) ? Math.Floor(s).ToString("0", inv) : "n/a")}{(over50 ? " frame_over_50=yes" : string.Empty)}");
        return new FightLegOutcome(ok, line, problems);
    }

    private static bool IsOver(double? value, double limit) => value is { } v && !double.IsNaN(v) && v > limit;

    private static void AddFrameProblem(List<string> problems, string name, double? value)
    {
        if (IsOver(value, FrameFailMs))
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"{name}={value:0.0} (at most {FrameFailMs:0} expected)"));
        }
    }

    private static string Ms(double? value) => value is { } v && !double.IsNaN(v) ? v.ToString("0.0", CultureInfo.InvariantCulture) : "n/a";
}

/// <summary>The fight leg's verdict: whether it held, the line to log (after the <c>[logincheck]</c> category) and, when it did not hold, what did not.</summary>
/// <param name="Ok">True when the judged fields hold.</param>
/// <param name="Line">The verdict line, <c>bsd-fight done ok=... </c>.</param>
/// <param name="Problems">What did not hold, one sentence each; empty when <paramref name="Ok"/>.</param>
public sealed record FightLegOutcome(bool Ok, string Line, IReadOnlyList<string> Problems);

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
/// <param name="BoltLight">A projectile's light was in the dynamic-light frame for at least 90% of its flight (<c>bolt_light</c>, M3 4.1; <see cref="LightFrameSampler"/>), or null when not sampled.</param>
/// <param name="DynamicLightsMax">The most dynamic lights in one frame (<c>dyn_lights_max</c>, report-only), or null.</param>
/// <param name="ViewerLight">The viewer light was in the last frame (<c>viewer_light=on|off</c>; judged since M3 4.2: off fails), or null.</param>
/// <param name="BlockedPicks">The picks the clear-line rule skipped (<c>blocked_picks</c>, report-only in M3 4.2).</param>
/// <param name="WallHits">The bolts that exploded short of the target with no damage line (<c>wall_hits</c>, report-only in M3 4.2).</param>
/// <param name="ManaFields">The fight's mana and rate fields (<c>FightScenario.ManaFields</c>, M5 1 F1: <c>level= mana_wait_max_s= mana_wait_s= kills_per_hour= kills_per_hour_burst=</c>, the cast tally's fields, <c>mana_presses= dances= dances_landed=</c>; report-only), printed at the end of the line, or null.</param>
/// <param name="TourFields">The tour's fields (<c>FightScenario.TourFields</c>, M5 item 2 g6, D44: <c>storeys_visited= basement_entries= returns= slots_standing=</c>; report-only), printed after <paramref name="ManaFields"/>, or null.</param>
/// <param name="LootFields">The loot step's fields (<c>FightScenario.LootFields</c>, M5 item 3 m2 and c2a: <c>loot=on|off looted= kept= loot_value=</c>; report-only), printed after <paramref name="TourFields"/>, or null.</param>
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
    double? FightSeconds,
    bool? BoltLight = null,
    int? DynamicLightsMax = null,
    bool? ViewerLight = null,
    int BlockedPicks = 0,
    int WallHits = 0,
    string? ManaFields = null,
    string? TourFields = null,
    string? LootFields = null);

/// <summary>
/// The fight leg's verdict (<c>--login-check=bsd-fight</c>, M3 3.1 s0): composed from the scenario's failure (or null) and the measures, so it is built and tested without Godot. The line is
/// <c>bsd-fight done ok=&lt;true|false&gt; kills= damage_dealt= spells_cast= misses= blocked_picks= wall_hits= xp_gained= damage_taken= hits_taken= evades= projectiles= cast_frame_max_ms= fight_frame_max_ms= bsd_min_fps= fight_s= bolt_light=yes|no dyn_lights_max= viewer_light=on|off[ frame_over_50=yes][ level= mana_wait_max_s= ...][ storeys_visited= basement_entries= returns= slots_standing=]</c>
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
    /// <param name="judgeBoltLight">True offline (the stand-in's fight is deterministic): <c>bolt_light=no</c> fails the run. The live leg leaves it report-only in this gate.</param>
    public static FightLegOutcome Compose(string? failure, in FightLegMeasures measures, bool judgeBoltLight = false)
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
        // M3 4.2 P4: judged in every leg (the 4.1 gate was report-only). n/a (no flight of MinFlightFrames frames, or no frames sampled) passes live; offline, the deterministic stand-in must show yes.
        if (measures.BoltLight == false || (judgeBoltLight && measures.BoltLight != true))
        {
            problems.Add("bolt_light=" + (measures.BoltLight is null ? "n/a" : "no") + " (a projectile's light in the frame for at least 90% of its flight expected)");
        }

        if (measures.ViewerLight == false)
        {
            problems.Add("viewer_light=off (the viewer light is on by default, SmartBox::set_viewer 11.6096 0x00454640: on expected)");
        }

        var over50 = IsOver(measures.CastFrameMaxMs, FrameWarnMs) || IsOver(measures.FightFrameMaxMs, FrameWarnMs);

        var ok = problems.Count == 0;
        var inv = CultureInfo.InvariantCulture;
        var line = string.Create(
            inv,
            $"bsd-fight done ok={(ok ? "true" : "false")} kills={measures.Kills} damage_dealt={measures.DamageDealt} spells_cast={measures.SpellsCast} misses={measures.Misses} blocked_picks={measures.BlockedPicks} wall_hits={measures.WallHits} xp_gained={measures.XpGained} damage_taken={measures.DamageTaken} hits_taken={measures.HitsTaken} evades={measures.Evades} projectiles={(measures.Projectiles is { } p ? p.ToString(inv) : "n/a")} cast_frame_max_ms={Ms(measures.CastFrameMaxMs)} fight_frame_max_ms={Ms(measures.FightFrameMaxMs)} bsd_min_fps={Ms(measures.BsdMinFps)} fight_s={(measures.FightSeconds is { } s && !double.IsNaN(s) ? Math.Floor(s).ToString("0", inv) : "n/a")}{LightFields(measures)}{(over50 ? " frame_over_50=yes" : string.Empty)}{(string.IsNullOrEmpty(measures.ManaFields) ? string.Empty : " " + measures.ManaFields)}{(string.IsNullOrEmpty(measures.TourFields) ? string.Empty : " " + measures.TourFields)}{(string.IsNullOrEmpty(measures.LootFields) ? string.Empty : " " + measures.LootFields)}");
        return new FightLegOutcome(ok, line, problems);
    }

    // A run that sampled no lights (the three are null) prints none of the three fields.
    private static string LightFields(in FightLegMeasures m) =>
        m.BoltLight is null && m.DynamicLightsMax is null && m.ViewerLight is null ? string.Empty :
        " bolt_light=" + (m.BoltLight is { } b ? (b ? "yes" : "no") : "n/a")
        + " dyn_lights_max=" + (m.DynamicLightsMax is { } d ? d.ToString(CultureInfo.InvariantCulture) : "n/a")
        + " viewer_light=" + (m.ViewerLight is { } v ? (v ? "on" : "off") : "n/a");

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

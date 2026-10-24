using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// What the game measured around the buff leg (M3 2.1 s3), read as deltas by the runner: the cast window counters of the avatar set (<c>AvatarStats.CastWindows/CastScripts/CastSounds</c>, s2b), the slowest
/// frame while a cast window was open, and the F750 sounds that sounded between the Enter and the end of the scenario.
/// </summary>
/// <param name="CastScripts">The effect scripts the cast windows played (<c>cast_scripts</c>, report-only).</param>
/// <param name="CastSounds">The voices the cast windows started (<c>cast_sounds</c>, report-only).</param>
/// <param name="CastWindows">The cast windows that opened (<c>cast_windows</c>, report-only).</param>
/// <param name="CastFrameMaxMs">The slowest frame while a cast window was open, or null when none opened (<c>cast_frame_max_ms</c>, report-only).</param>
/// <param name="SoundsServer">The voices the server's sounds (F750) started in the leg (<c>sounds_server</c>, judged &gt;= 1: D27), or null when the run has no such counter.</param>
public readonly record struct BuffLegMeasures(long CastScripts, long CastSounds, long CastWindows, double? CastFrameMaxMs, int? SoundsServer);

/// <summary>What the buff leg's scenario left, as the verdict needs it (<see cref="BuffScenario"/>'s accessors).</summary>
/// <param name="ScenarioSucceeded">True when the scenario ended <c>done</c> without a failure.</param>
/// <param name="Failure">The scenario's failure (its stage names it), or null.</param>
/// <param name="GearFields">The gear scenario's verdict fields (<c>gear=wielded worn=n ...</c>; <c>GearScenario.VerdictFields</c>).</param>
/// <param name="Cycle">What the ordered pass left (<c>BuffScenario.CycleResult</c>), or null when it never ended.</param>
/// <param name="Forced">What the forced re-buff pass left (<c>BuffScenario.ForcedResult</c>), or null (always null with the driver on: there is no forced pass).</param>
/// <param name="DriverOn">True when the run used the re-buff driver (<c>--buff-auto on</c>, M4 4.1 lane c): <paramref name="Driver"/> then takes the forced pass's place.</param>
/// <param name="Driver">What the driver's pass left (<c>BuffScenario.DriverResult</c>), or null when it never ended.</param>
/// <param name="RebuffSeconds">The session seconds from the cycle's end to the driver pass's end (<c>BuffScenario.DriverRebuffSeconds</c>), NaN when the pass never ended.</param>
public sealed record BuffLegScenarioResult(bool ScenarioSucceeded, string? Failure, string GearFields, BuffResult? Cycle, BuffResult? Forced, bool DriverOn = false, BuffResult? Driver = null, double RebuffSeconds = double.NaN);

/// <summary>
/// The buff leg's verdict (<c>--login-check=buff</c>, M3 2.1 s3; docs/session.md section 30): composed from the scenario's result and the game's measures, so it is built and tested without Godot. The line is
/// <c>buff done ok=&lt;true|false&gt; gear= worn= cast=ok|fizzled|failed|n/a buffs=&lt;active&gt;/&lt;wanted&gt; rebuffed= forced= min_remaining_s= levels= cycle_s= cast_scripts= cast_sounds= cast_windows=
/// cast_frame_max_ms= sounds_server= fizzles= misses= surpassed= cast_scripts_expected=[ frame_over_50=yes][ driver=on rebuff_lines= rebuff_s=]</c> (no name, no account). <c>cast_scripts_expected</c> is the casts the passes made
/// (cycle plus forced, or plus the driver's), to read against <c>cast_scripts</c> (the effects the cast windows saw: cluster-k3 gap 1); <c>frame_over_50=yes</c> is <c>cast_frame_max_ms</c> over <see cref="FrameWarnMs"/>.
/// <b>With the driver</b> (<c>--buff-auto on</c>, M4 4.1 lane c; DoD 14) there is no forced pass: <c>rebuffed=</c>, <c>buffs=</c>, <c>min_remaining_s=</c> and the rest of the pass fields are the <b>driver's pass</b>,
/// <c>forced=no</c> is printed (judged at 4.2), and the line ends with <c>driver=on rebuff_lines=&lt;lines the pass cast&gt; rebuff_s=&lt;session seconds from the cycle's end to the pass's end&gt;</c>, both report-only.
/// The judgement of the driver run is <c>rebuffed=yes</c> from the driver's pass, as the forced run's is from the forced pass.
/// </summary>
/// <remarks>
/// <para><b>Judged</b> (<c>ok=</c>): what the loopback test asserts (<c>gear=wielded</c>, every wanted line active, <c>rebuffed=yes</c>, with the scenario ended without a failure) and <c>sounds_server&gt;=1</c> (D27, the owner's:
/// the leg's second gate; ACE sends no F750 at login, so the leg re-seats the wand to have one). <c>cast_scripts</c>, <c>cast_sounds</c>, <c>cast_windows</c>, <c>cast_frame_max_ms</c> and the rest are report-only in
/// this gate.</para>
/// <para><b><c>cast=</c></b> (D20): <c>failed</c> when a pass failed or a line was missed for any reason but a fizzle; <c>fizzled</c> when a line was missed by fizzles (the retry one level down that recovers
/// is not a miss: it reads <c>ok</c>); <c>n/a</c> when nothing was cast (no pass ended, or no line was cast); else <c>ok</c>.</para>
/// </remarks>
public static class BuffLegVerdict
{
    /// <summary>A cast window whose slowest frame is over this many milliseconds prints <c>frame_over_50=yes</c> and passes (the fight leg's rule, report-only here: a new judge; the lead's worst of three).</summary>
    public const double FrameWarnMs = 50.0;

    // TODO(m3-buff-cast-frame-judge): the fight leg fails a window over 75 ms (FightLegVerdict.FrameFailMs); the buff leg's cast window is report-only in M3 4.2 (a new judge reports first). M4 judges it.

    /// <summary>The miss tokens (<c>Short:why</c>) whose why is a fizzle: <c>fizzled</c> (the retry failed too) and <c>upgrade-fizzled</c> (an upgrade is cast once).</summary>
    private static readonly string[] FizzleWhys = ["fizzled", "upgrade-fizzled"];

    /// <summary>The <c>cast=</c> value (D20; see the class remarks) over the two passes' results.</summary>
    /// <param name="cycle">The ordered pass's result, or null.</param>
    /// <param name="forced">The forced pass's result, or null.</param>
    public static string CastOf(BuffResult? cycle, BuffResult? forced)
    {
        if (cycle is null && forced is null)
        {
            return "n/a";
        }

        var fizzled = false;
        foreach (var result in new[] { cycle, forced })
        {
            if (result is null)
            {
                continue;
            }

            if (result.Failure is not null)
            {
                return "failed";
            }

            foreach (var miss in result.Misses)
            {
                if (IsFizzle(miss))
                {
                    fizzled = true;
                }
                else
                {
                    return "failed";
                }
            }
        }

        if (fizzled)
        {
            return "fizzled";
        }

        return (cycle?.Cast ?? 0) + (forced?.Cast ?? 0) > 0 ? "ok" : "n/a";
    }

    /// <summary>True when a miss token (<c>Short:why</c>) is a fizzle.</summary>
    public static bool IsFizzle(string miss)
    {
        ArgumentNullException.ThrowIfNull(miss);
        var why = miss[(miss.IndexOf(':', StringComparison.Ordinal) + 1)..];
        return Array.IndexOf(FizzleWhys, why) >= 0;
    }

    /// <summary>Composes the leg's verdict from the scenario's result and the game's measures.</summary>
    /// <param name="scenario">What the scenario left.</param>
    /// <param name="measures">What the game measured.</param>
    public static BuffLegOutcome Compose(BuffLegScenarioResult scenario, in BuffLegMeasures measures)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var best = scenario.DriverOn ? scenario.Driver : scenario.Forced ?? scenario.Cycle;
        var problems = new List<string>();
        var gear = Token(scenario.GearFields, "gear");
        var worn = Token(scenario.GearFields, "worn");
        if (!scenario.ScenarioSucceeded)
        {
            problems.Add("the scenario did not succeed: " + (scenario.Failure ?? "no reason given"));
        }

        if (gear != "wielded")
        {
            problems.Add("gear=" + gear + " (wielded expected)");
        }

        var active = best?.Active ?? 0;
        var wanted = best?.Wanted ?? 0;
        if (best is null || wanted == 0 || active < wanted)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"buffs={active}/{wanted} (every wanted line active expected)"));
        }

        if (best?.Rebuffed != true)
        {
            problems.Add(scenario.DriverOn ? "rebuffed=no (the driver's pass was to recast a line while it was still active)" : "rebuffed=no (a recast while still active expected)");
        }

        if (measures.SoundsServer is not { } server)
        {
            problems.Add("sounds_server=n/a: the run has no server-sound counter (D27 judges >= 1)");
        }
        else if (server < 1)
        {
            problems.Add("sounds_server=0: no server sound (F750) sounded in the leg (D27 judges >= 1)");
        }

        var cast = CastOf(scenario.Cycle, scenario.DriverOn ? scenario.Driver : scenario.Forced);
        var later = scenario.DriverOn ? scenario.Driver : scenario.Forced;
        var forcedLanded = scenario.Forced is { Forced: true, Cast: >= 1, RecastWhileActive: >= 1 };
        var misses = new List<string>();
        if (scenario.Cycle is not null)
        {
            misses.AddRange(scenario.Cycle.Misses);
        }

        if (later is not null)
        {
            foreach (var miss in later.Misses)
            {
                if (!misses.Contains(miss))
                {
                    misses.Add(miss);
                }
            }
        }

        var inv = CultureInfo.InvariantCulture;
        var min = best is null || double.IsNaN(best.MinRemainingSeconds) ? "n/a" : Math.Floor(best.MinRemainingSeconds).ToString("0", inv);
        var ok = problems.Count == 0;
        var line = string.Create(
            inv,
            $"buff done ok={(ok ? "true" : "false")} gear={gear} worn={worn} cast={cast} buffs={active}/{wanted} rebuffed={(best?.Rebuffed == true ? "yes" : "no")} forced={(forcedLanded ? "yes" : "no")} min_remaining_s={min} levels={(best is null ? "n/a" : best.Levels)} cycle_s={Math.Floor(scenario.Cycle?.CycleSeconds ?? 0).ToString("0", inv)} cast_scripts={measures.CastScripts} cast_sounds={measures.CastSounds} cast_windows={measures.CastWindows} cast_frame_max_ms={(measures.CastFrameMaxMs is { } ms ? ms.ToString("0.0", inv) : "n/a")} sounds_server={(measures.SoundsServer is { } s ? s.ToString(inv) : "n/a")} fizzles={best?.Fizzles ?? 0} misses={(misses.Count == 0 ? "none" : string.Join(';', misses))} surpassed={(scenario.Cycle?.Surpassed ?? 0) + (later?.Surpassed ?? 0)} cast_scripts_expected={(scenario.Cycle?.Cast ?? 0) + (later?.Cast ?? 0)}{(measures.CastFrameMaxMs is { } castMs && !double.IsNaN(castMs) && castMs > FrameWarnMs ? " frame_over_50=yes" : string.Empty)}{DriverFields(scenario)}");
        return new BuffLegOutcome(ok, cast, line, problems);
    }

    /// <summary>The trailing <c>driver=on rebuff_lines= rebuff_s=</c> fields of a driver run, or nothing.</summary>
    private static string DriverFields(BuffLegScenarioResult scenario)
    {
        if (!scenario.DriverOn)
        {
            return string.Empty;
        }

        var seconds = double.IsNaN(scenario.RebuffSeconds) ? "n/a" : Math.Floor(scenario.RebuffSeconds).ToString("0", CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $" driver=on rebuff_lines={scenario.Driver?.Cast ?? 0} rebuff_s={seconds}");
    }

    /// <summary>The <c>name=value</c> value of a fields string (the gear scenario's), or <c>?</c>.</summary>
    private static string Token(string fields, string name)
    {
        var key = name + "=";
        var at = fields.StartsWith(key, StringComparison.Ordinal) ? 0 : fields.IndexOf(" " + key, StringComparison.Ordinal);
        if (at < 0)
        {
            return "?";
        }

        at = at == 0 ? key.Length : at + 1 + key.Length;
        var end = fields.IndexOf(' ', at);
        return end < 0 ? fields[at..] : fields[at..end];
    }
}

/// <summary>The buff leg's verdict: whether it held, its <c>cast=</c> value, the line to log (after the <c>[logincheck]</c> category) and, when it did not hold, what did not.</summary>
/// <param name="Ok">True when the judged fields hold.</param>
/// <param name="Cast">The <c>cast=</c> value.</param>
/// <param name="Line">The verdict line, <c>buff done ok=... </c>.</param>
/// <param name="Problems">What did not hold, one sentence each; empty when <paramref name="Ok"/>.</param>
public sealed record BuffLegOutcome(bool Ok, string Cast, string Line, IReadOnlyList<string> Problems);

/// <summary>
/// The session phases the buff leg's runner asks about (M3 2.1 s3), answered here because the game project names no <c>Lifestone.Net</c> type (the layering test): the runner calls these instead of comparing
/// <see cref="GameState.Phase"/> itself.
/// </summary>
public static class BuffLegPhases
{
    /// <summary>True when the session failed (a timeout, a boot, a data mismatch, a lost server).</summary>
    public static bool Failed(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Phase == SessionPhase.Failed;
    }

    /// <summary>True when the session is at the character list.</summary>
    public static bool AtCharacterSelect(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Phase == SessionPhase.CharacterSelect;
    }

    /// <summary>True when the session is in the world.</summary>
    public static bool InWorld(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Phase == SessionPhase.InWorld;
    }
}

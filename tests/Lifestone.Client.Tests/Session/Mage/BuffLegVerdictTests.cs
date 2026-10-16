using System.Text.RegularExpressions;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The buff leg's verdict (M3 2.1 s3, <see cref="BuffLegVerdict"/>) over scripted scenario results, so it is tested without Godot or a server: the line's field order, what <c>ok=</c> judges (the gear, the 24 lines,
/// the re-buff, the scenario's own success and <c>sounds_server&gt;=1</c>, D27), what stays report-only (the cast counters and the frame figure), and the <c>cast=</c> rule (D20): <c>ok</c>, <c>fizzled</c>,
/// <c>failed</c>, <c>n/a</c>.
/// </summary>
public sealed class BuffLegVerdictTests
{
    private const string Gear = "gear=wielded worn=8 kit=40/40 gear_steps=0 topup=0 cast=n/a";

    private static readonly Regex Line = new(
        @"^buff done ok=(true|false) gear=(wielded|failed|\?) worn=(\d+|\?) cast=(ok|fizzled|failed|n/a) buffs=\d+/\d+ rebuffed=(yes|no) forced=(yes|no) min_remaining_s=(\d+|n/a) levels=\S+ cycle_s=\d+ cast_scripts=\d+ cast_sounds=\d+ cast_windows=\d+ cast_frame_max_ms=(\d+\.\d|n/a) sounds_server=(\d+|n/a) fizzles=\d+ misses=\S+$",
        RegexOptions.CultureInvariant);

    private static BuffResult Cycle(int cast = 30, int fizzles = 0, string[]? misses = null, string? failure = null) => new()
    {
        Wanted = 24,
        Active = 24,
        Levels = "Focus:VII,Will:VII",
        MinRemainingSeconds = 1799.4,
        Fizzles = fizzles,
        Misses = misses ?? [],
        Cast = cast,
        CycleSeconds = 1324.7,
        Failure = failure,
    };

    private static BuffResult Forced(int cast = 1, bool rebuffed = true, string[]? misses = null) => new()
    {
        Wanted = 24,
        Active = 24,
        Levels = "Focus:VII,Will:VII",
        RecastWhileActive = rebuffed ? 1 : 0,
        Forced = true,
        Pass = "rebuff",
        MinRemainingSeconds = 1204.2,
        Cast = cast,
        Misses = misses ?? [],
    };

    private static BuffLegScenarioResult Ran(BuffResult? cycle = null, BuffResult? forced = null, string gear = Gear, bool succeeded = true, string? failure = null) =>
        new(succeeded, failure, gear, cycle ?? Cycle(), forced ?? Forced());

    private static BuffLegMeasures Measures(int? server = 3, long scripts = 12, long sounds = 20, long windows = 31, double? frame = 21.4) => new(scripts, sounds, windows, frame, server);

    [Fact]
    public void A_clean_run_holds_and_the_line_has_the_fields_in_order()
    {
        var outcome = BuffLegVerdict.Compose(Ran(), Measures());

        Assert.True(outcome.Ok, string.Join("; ", outcome.Problems));
        Assert.Empty(outcome.Problems);
        Assert.Equal("ok", outcome.Cast);
        Assert.Matches(Line, outcome.Line);
        Assert.StartsWith("buff done ok=true gear=wielded worn=8 cast=ok buffs=24/24 rebuffed=yes forced=yes min_remaining_s=1204 levels=Focus:VII,Will:VII cycle_s=1324 ", outcome.Line, StringComparison.Ordinal);
        Assert.EndsWith(" cast_scripts=12 cast_sounds=20 cast_windows=31 cast_frame_max_ms=21.4 sounds_server=3 fizzles=0 misses=none", outcome.Line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(null)]
    public void No_server_sound_fails_the_leg_and_says_so(int? server)
    {
        var outcome = BuffLegVerdict.Compose(Ran(), Measures(server: server));

        Assert.False(outcome.Ok);
        Assert.Contains("buff done ok=false", outcome.Line, StringComparison.Ordinal);
        Assert.Contains(" sounds_server=" + (server is null ? "n/a" : "0"), outcome.Line, StringComparison.Ordinal);
        Assert.Single(outcome.Problems);
        Assert.Contains("sounds_server", outcome.Problems[0], StringComparison.Ordinal);
        Assert.Contains("D27", outcome.Problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void The_cast_counters_and_the_frame_figure_are_report_only()
    {
        var quiet = BuffLegVerdict.Compose(Ran(), Measures(scripts: 0, sounds: 0, windows: 0, frame: null));
        var slow = BuffLegVerdict.Compose(Ran(), Measures(frame: 480.0));

        Assert.True(quiet.Ok, string.Join("; ", quiet.Problems));
        Assert.Contains(" cast_scripts=0 cast_sounds=0 cast_windows=0 cast_frame_max_ms=n/a ", quiet.Line, StringComparison.Ordinal);
        Assert.True(slow.Ok);
        Assert.Contains(" cast_frame_max_ms=480.0 ", slow.Line, StringComparison.Ordinal);
        Assert.Matches(Line, quiet.Line);
        Assert.Matches(Line, slow.Line);
    }

    [Fact]
    public void The_loopback_tests_facts_are_judged()
    {
        var geared = BuffLegVerdict.Compose(Ran(gear: "gear=failed worn=5 kit=40/40"), Measures());
        var short24 = BuffLegVerdict.Compose(Ran(forced: new BuffResult { Wanted = 24, Active = 22, Levels = "x", RecastWhileActive = 1, Forced = true, Cast = 1 }), Measures());
        var notRebuffed = BuffLegVerdict.Compose(Ran(forced: Forced(cast: 0, rebuffed: false)), Measures());
        var noPass = BuffLegVerdict.Compose(new BuffLegScenarioResult(true, null, Gear, null, null), Measures());
        // Review 2.1-R5: a forced pass that cast a line but recast none while it was active (an expired line) is not a re-buff: rebuffed=no forced=no, and the leg fails.
        var castOnly = BuffLegVerdict.Compose(Ran(forced: Forced(cast: 1, rebuffed: false)), Measures());

        Assert.False(geared.Ok);
        Assert.Contains("gear=failed", geared.Problems[0], StringComparison.Ordinal);
        Assert.False(short24.Ok);
        Assert.Contains("buffs=22/24", short24.Line, StringComparison.Ordinal);
        Assert.False(notRebuffed.Ok);
        Assert.Contains("rebuffed=no forced=no", notRebuffed.Line, StringComparison.Ordinal);
        Assert.False(castOnly.Ok);
        Assert.Contains("rebuffed=no forced=no", castOnly.Line, StringComparison.Ordinal);
        Assert.False(noPass.Ok);
        Assert.Matches(Line, noPass.Line);
    }

    [Fact]
    public void A_scenario_that_failed_fails_the_leg_with_its_reason_whatever_the_counters_say()
    {
        var outcome = BuffLegVerdict.Compose(Ran(succeeded: false, failure: "cycle: the mana wait ran out"), Measures());

        Assert.False(outcome.Ok);
        Assert.Contains("the mana wait ran out", outcome.Problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_that_never_reached_the_world_reports_n_a_and_failed_gear()
    {
        var outcome = BuffLegVerdict.Compose(new BuffLegScenarioResult(false, "choosing: BSD Mage is not in the character list (run --scenario=mage-build first)", "gear=failed worn=0", null, null), new BuffLegMeasures(0, 0, 0, null, null));

        Assert.False(outcome.Ok);
        Assert.Equal("n/a", outcome.Cast);
        Assert.Matches(Line, outcome.Line);
        Assert.Contains("gear=failed worn=0 cast=n/a buffs=0/0", outcome.Line, StringComparison.Ordinal);
        Assert.Contains("mage-build", outcome.Problems[0], StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------- the cast= rule (D20)

    [Fact]
    public void Cast_is_ok_when_every_cast_was_confirmed()
    {
        Assert.Equal("ok", BuffLegVerdict.CastOf(Cycle(), Forced()));
    }

    [Fact]
    public void Cast_is_ok_when_a_fizzle_was_recovered_by_its_retry()
    {
        // The cycle counts the fizzle (fizzles=1) and retries one level down; the line landed, so there is no miss.
        Assert.Equal("ok", BuffLegVerdict.CastOf(Cycle(fizzles: 2), Forced()));
    }

    [Theory]
    [InlineData("Focus:fizzled")]
    [InlineData("Will:upgrade-fizzled")]
    public void Cast_is_fizzled_when_a_line_was_missed_by_fizzles(string miss)
    {
        Assert.Equal("fizzled", BuffLegVerdict.CastOf(Cycle(fizzles: 2, misses: [miss]), Forced()));
        Assert.Equal("fizzled", BuffLegVerdict.CastOf(Cycle(), Forced(misses: [miss])));
    }

    [Theory]
    [InlineData("Focus:no-spell")]
    [InlineData("Focus:no-level")]
    [InlineData("Imp:no-worn-piece")]
    [InlineData("Bane:error-0x0402")]
    public void Cast_is_failed_for_any_other_miss(string miss)
    {
        Assert.Equal("failed", BuffLegVerdict.CastOf(Cycle(misses: [miss]), Forced()));
    }

    [Fact]
    public void Cast_is_failed_when_a_pass_failed_and_a_fizzle_miss_does_not_hide_it()
    {
        Assert.Equal("failed", BuffLegVerdict.CastOf(Cycle(failure: "the mana wait ran out"), null));
        Assert.Equal("failed", BuffLegVerdict.CastOf(Cycle(misses: ["Focus:fizzled", "Will:no-spell"]), Forced()));
    }

    [Fact]
    public void Cast_is_n_a_when_nothing_was_cast()
    {
        Assert.Equal("n/a", BuffLegVerdict.CastOf(null, null));
        Assert.Equal("n/a", BuffLegVerdict.CastOf(Cycle(cast: 0), Forced(cast: 0)));
    }

    [Fact]
    public void A_second_run_that_only_recast_one_line_in_the_forced_pass_reads_ok()
    {
        Assert.Equal("ok", BuffLegVerdict.CastOf(Cycle(cast: 0), Forced(cast: 1)));
    }

    [Theory]
    [InlineData("Focus:fizzled", true)]
    [InlineData("Focus:upgrade-fizzled", true)]
    [InlineData("Focus:no-spell", false)]
    [InlineData("fizzled", true)]
    [InlineData("Focus:error-0x0402", false)]
    public void A_miss_token_is_a_fizzle_by_its_why(string miss, bool fizzle)
    {
        Assert.Equal(fizzle, BuffLegVerdict.IsFizzle(miss));
    }
}

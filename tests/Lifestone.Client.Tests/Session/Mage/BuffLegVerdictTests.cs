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
        @"^buff done ok=(true|false) gear=(wielded|failed|\?) worn=(\d+|\?) cast=(ok|fizzled|failed|n/a) buffs=\d+/\d+ rebuffed=(yes|no) forced=(yes|no) min_remaining_s=(\d+|n/a) levels=\S+ cycle_s=\d+ cast_scripts=\d+ cast_sounds=\d+ cast_windows=\d+ cast_frame_max_ms=(\d+\.\d|n/a) sounds_server=(\d+|n/a) fizzles=\d+ misses=\S+ surpassed=\d+ cast_scripts_expected=\d+( frame_over_50=yes)?( driver=on rebuff_lines=\d+ rebuff_s=(\d+|n/a))?$",
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
    public void A_clean_run_holds_and_the_golden_line_has_the_fields_in_order()
    {
        // The golden line (P1b lane B), forced-pass shape: the whole line is compared once, with a cast window over 50 ms for the flag.
        var outcome = BuffLegVerdict.Compose(Ran(), Measures(frame: 52.04));

        Assert.True(outcome.Ok, string.Join("; ", outcome.Problems));
        Assert.Empty(outcome.Problems);
        Assert.Equal("ok", outcome.Cast);
        Assert.Matches(Line, outcome.Line);
        Assert.Equal(
            "buff done ok=true gear=wielded worn=8 cast=ok buffs=24/24 rebuffed=yes forced=yes min_remaining_s=1204 levels=Focus:VII,Will:VII cycle_s=1324 cast_scripts=12 cast_sounds=20 cast_windows=31 cast_frame_max_ms=52.0 sounds_server=3 fizzles=0 misses=none surpassed=0 cast_scripts_expected=31 frame_over_50=yes",
            outcome.Line);
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
        Assert.True(slow.Ok);
        Assert.Matches(Line, quiet.Line);
        Assert.Matches(Line, slow.Line);
    }

    [Theory]
    [InlineData(50.0, false)]
    [InlineData(50.1, true)]
    [InlineData(480.0, true)]
    public void A_cast_window_over_50_ms_prints_the_flag_and_still_passes(double frame, bool over50)
    {
        var outcome = BuffLegVerdict.Compose(Ran(), Measures(frame: frame));

        Assert.True(outcome.Ok, string.Join("; ", outcome.Problems));
        Assert.Matches(Line, outcome.Line);
        Assert.Equal(over50, outcome.Line.EndsWith(" frame_over_50=yes", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unmeasured_cast_window_is_not_flagged()
    {
        Assert.DoesNotContain("frame_over_50", BuffLegVerdict.Compose(Ran(), Measures(frame: null)).Line, StringComparison.Ordinal);
        Assert.DoesNotContain("frame_over_50", BuffLegVerdict.Compose(Ran(), Measures(frame: double.NaN)).Line, StringComparison.Ordinal);
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
        Assert.Contains(" cast_scripts_expected=0", noPass.Line, StringComparison.Ordinal);
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
    public void Cast_is_ok_when_every_cast_was_confirmed_or_a_fizzle_was_recovered_by_its_retry()
    {
        Assert.Equal("ok", BuffLegVerdict.CastOf(Cycle(), Forced()));
        // The cycle counts the fizzle (fizzles=1) and retries one level down; the line landed, so there is no miss.
        Assert.Equal("ok", BuffLegVerdict.CastOf(Cycle(fizzles: 2), Forced()));
    }

    [Theory]
    [InlineData("Focus:fizzled")]
    public void Cast_is_fizzled_when_a_line_was_missed_by_fizzles(string miss)
    {
        Assert.Equal("fizzled", BuffLegVerdict.CastOf(Cycle(fizzles: 2, misses: [miss]), Forced()));
        Assert.Equal("fizzled", BuffLegVerdict.CastOf(Cycle(), Forced(misses: [miss])));
    }

    [Theory]
    [InlineData("Focus:no-spell")]
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

    // ---------------------------------------------------------------------------------------------- the driver run (M4 4.1 lane c, DoD 14)

    private static BuffResult Driver(int cast = 3, bool rebuffed = true, string[]? misses = null) => new()
    {
        Wanted = 24,
        Active = 24,
        Levels = "Focus:VII,Will:VII",
        RecastWhileActive = rebuffed ? cast : 0,
        Pass = "rebuff",
        MinRemainingSeconds = 1795.2,
        Cast = cast,
        Misses = misses ?? [],
    };

    private static BuffLegScenarioResult Driven(BuffResult? driver = null, double seconds = 33.4) =>
        new(true, null, Gear, Cycle(), null, DriverOn: true, Driver: driver ?? Driver(), RebuffSeconds: seconds);

    [Fact]
    public void A_driver_run_golden_line_reads_rebuffed_from_the_drivers_pass_and_ends_with_the_driver_fields()
    {
        var outcome = BuffLegVerdict.Compose(Driven(), Measures());

        Assert.True(outcome.Ok, string.Join("; ", outcome.Problems));
        Assert.Matches(Line, outcome.Line);
        Assert.Equal(
            "buff done ok=true gear=wielded worn=8 cast=ok buffs=24/24 rebuffed=yes forced=no min_remaining_s=1795 levels=Focus:VII,Will:VII cycle_s=1324 cast_scripts=12 cast_sounds=20 cast_windows=31 cast_frame_max_ms=21.4 sounds_server=3 fizzles=0 misses=none surpassed=0 cast_scripts_expected=33 driver=on rebuff_lines=3 rebuff_s=33",
            outcome.Line);
    }

    [Fact]
    public void A_driver_run_whose_pass_recast_nothing_active_or_never_ended_fails_the_leg()
    {
        var cast = BuffLegVerdict.Compose(Driven(Driver(rebuffed: false)), Measures());
        Assert.False(cast.Ok);
        Assert.Contains(" rebuffed=no forced=no ", cast.Line, StringComparison.Ordinal);
        Assert.Contains("driver's pass", cast.Problems[0], StringComparison.Ordinal);

        var never = BuffLegVerdict.Compose(new BuffLegScenarioResult(false, "driver: never", Gear, Cycle(), null, DriverOn: true), Measures());
        Assert.False(never.Ok);
        Assert.EndsWith(" driver=on rebuff_lines=0 rebuff_s=n/a", never.Line, StringComparison.Ordinal);
        Assert.Matches(Line, never.Line);
    }

    [Fact]
    public void A_run_without_the_driver_is_unchanged_and_a_forced_pass_does_not_count_for_a_driver_run()
    {
        var plain = BuffLegVerdict.Compose(Ran(), Measures());
        Assert.DoesNotContain("driver=", plain.Line, StringComparison.Ordinal);
        Assert.Contains(" rebuffed=yes forced=yes ", plain.Line, StringComparison.Ordinal);

        // A driver run judges the driver's pass even if a forced result were handed in.
        var mixed = BuffLegVerdict.Compose(new BuffLegScenarioResult(true, null, Gear, Cycle(), Forced(), DriverOn: true, Driver: Driver(rebuffed: false)), Measures());
        Assert.False(mixed.Ok);
        Assert.Contains(" forced=yes ", mixed.Line, StringComparison.Ordinal); // printed from the forced result when one exists; the judgement is the driver's
    }
}

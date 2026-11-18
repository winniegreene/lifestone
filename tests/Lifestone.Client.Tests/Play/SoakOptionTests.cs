using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Play;

namespace Lifestone.Client.Tests.Play;

/// <summary>M5 item 0 c3: <c>--soak-line-s</c> and <c>--log-copies</c> parse, default as the plan says, report a bad value by name (and keep the default) and are registered with help.</summary>
public sealed class SoakOptionTests
{
    [Fact]
    public void The_defaults_are_a_line_a_minute_and_the_console_copy()
    {
        var none = PlayOptions.Parse([]);

        Assert.Equal(60.0, none.SoakLineSeconds);
        Assert.Equal(LogCopies.Console, none.LogCopies);
        Assert.Empty(none.Errors);
    }

    [Theory]
    [InlineData("--soak-line-s=0", 0.0)]
    [InlineData("--soak-line-s=60", 60.0)]
    [InlineData("--soak-line-s=5", 5.0)]
    [InlineData("--soak-line-s=0.5", 0.5)]
    [InlineData("--soak-line-s=86400", 86400.0)]
    public void A_good_interval_parses_and_zero_turns_the_line_off(string argument, double expected)
    {
        var o = PlayOptions.Parse([argument]);

        Assert.Empty(o.Errors);
        Assert.Equal(expected, o.SoakLineSeconds);
    }

    [Theory]
    [InlineData("--soak-line-s=abc", "--soak-line-s: 'abc' is not a number")]
    [InlineData("--soak-line-s=", "--soak-line-s: '' is not a number")]
    [InlineData("--soak-line-s=-1", "--soak-line-s: '-1' is not 0 to 86400 (0 = no line)")]
    [InlineData("--soak-line-s=86401", "--soak-line-s: '86401' is not 0 to 86400 (0 = no line)")]
    [InlineData("--soak-line-s=NaN", "--soak-line-s: 'NaN' is not a number")]
    public void A_bad_interval_is_reported_by_name_and_the_default_is_kept(string argument, string expected)
    {
        var o = PlayOptions.Parse([argument]);

        Assert.Equal(expected, Assert.Single(o.Errors));
        Assert.Equal(60.0, o.SoakLineSeconds);
    }

    [Theory]
    [InlineData("--log-copies=console", LogCopies.Console)]
    [InlineData("--log-copies=file", LogCopies.File)]
    [InlineData("--log-copies=all", LogCopies.All)]
    public void The_three_log_copies_parse(string argument, LogCopies expected)
    {
        var o = PlayOptions.Parse([argument]);

        Assert.Empty(o.Errors);
        Assert.Equal(expected, o.LogCopies);
    }

    [Theory]
    [InlineData("--log-copies=none")]
    [InlineData("--log-copies=")]
    [InlineData("--log-copies=godot")]
    public void A_bad_log_copies_value_is_reported_and_the_default_is_kept(string argument)
    {
        var o = PlayOptions.Parse(["--log-copies=file", argument]);

        var error = Assert.Single(o.Errors);
        Assert.StartsWith("--log-copies:", error, StringComparison.Ordinal);
        Assert.Equal(LogCopies.File, o.LogCopies); // the earlier good value stands: a bad one changes nothing
    }

    [Fact]
    public void Both_options_are_registered_with_help_in_the_play_group()
    {
        var names = OptionCatalog.Play().Options.ToDictionary(o => o.Name, o => o, StringComparer.Ordinal);

        Assert.Contains("--soak-line-s", names.Keys);
        Assert.Contains("--log-copies", names.Keys);
        Assert.Contains("console|file|all", names["--log-copies"].Value ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("m5-soak-tallies (closed)", names["--soak-line-s"].Help, StringComparison.Ordinal);
        Assert.Contains("lifestone.log", names["--log-copies"].Help, StringComparison.Ordinal);
    }

    [Fact]
    public void The_log_keep_vitals_and_objects_options_default_parse_and_report_a_bad_value_by_name_M5_7_F17()
    {
        var none = PlayOptions.Parse([]);
        Assert.Equal(1, none.LogKeep);
        Assert.Equal(0.0, none.LogVitalsSeconds);
        Assert.Equal(60.0, none.LogObjectsMetres);

        var soak = PlayOptions.Parse(["--log-keep=0", "--log-vitals-s=10", "--log-objects-m=45.5"]);
        Assert.Empty(soak.Errors);
        Assert.Equal(0, soak.LogKeep);
        Assert.Equal(10.0, soak.LogVitalsSeconds);
        Assert.Equal(45.5, soak.LogObjectsMetres);
        Assert.Equal(0.0, PlayOptions.Parse(["--log-objects-m=0"]).LogObjectsMetres);

        var bad = PlayOptions.Parse(["--log-keep=-1", "--log-vitals-s=abc", "--log-objects-m=20000"]);
        Assert.Equal(1, bad.LogKeep);
        Assert.Equal(0.0, bad.LogVitalsSeconds);
        Assert.Equal(60.0, bad.LogObjectsMetres);
        Assert.Contains(bad.Errors, e => e.StartsWith("--log-keep: '-1' is not 0 to", StringComparison.Ordinal));
        Assert.Contains(bad.Errors, e => e.StartsWith("--log-vitals-s: 'abc' is not a number", StringComparison.Ordinal));
        Assert.Contains(bad.Errors, e => e.StartsWith("--log-objects-m: '20000' is not 0 to", StringComparison.Ordinal));
    }
}

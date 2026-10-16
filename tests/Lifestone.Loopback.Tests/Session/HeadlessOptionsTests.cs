using System.Net;
using Lifestone.Client.Diagnostics;
using Lifestone.Headless;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// The headless host's command line (ST5): its own group, the Session group and the five shared Core options parse;
/// anything else is a problem reported without its value, and a bad command line exits 1 before anything runs. Plain
/// unit tests (no backend); they live here because this project is the one that references the headless host.
/// </summary>
public sealed class HeadlessOptionsTests
{
    [Fact]
    public void Defaults_are_the_login_scenario()
    {
        var o = HeadlessOptions.Parse([]);
        Assert.Empty(o.Errors);
        Assert.False(o.Help);
        Assert.Equal(HeadlessScenario.Login, o.Scenario);
        Assert.Equal(HeadlessOptions.DefaultLaps, o.Laps);
        Assert.Null(o.CreateCharacter);
        Assert.Null(o.SummaryPath);
        Assert.Null(o.DatDirectory);
        Assert.Equal(LogSeverity.Info, o.LogLevel);
        Assert.Null(o.QuitAfter);
        Assert.Null(o.Workers);
        Assert.Null(o.Session.Account);
    }

    [Fact]
    public void Every_headless_option_parses()
    {
        var o = HeadlessOptions.Parse(
        [
            "--scenario=holtburg-laps", "--laps=5", "--create-character=Lifestone Smoke", "--summary=cache/headless/run.json",
            "--dat-dir=D:/dats", "--log-level=debug", "--quit-after=90", "--workers=2", "--account=test", "--server=127.0.0.1:9000",
            "--record-session", "--play",
        ]);
        Assert.Empty(o.Errors);
        Assert.Equal(HeadlessScenario.HoltburgLaps, o.Scenario);
        Assert.Equal(5, o.Laps);
        Assert.Equal("Lifestone Smoke", o.CreateCharacter);
        Assert.Equal("cache/headless/run.json", o.SummaryPath);
        Assert.Equal("D:/dats", o.DatDirectory);
        Assert.Equal(LogSeverity.Debug, o.LogLevel);
        Assert.Equal(90, o.QuitAfter);
        Assert.Equal(2, o.Workers);
        Assert.Equal("test", o.Session.Account);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 9000), o.Session.Server);
        Assert.True(o.Session.RecordSession);
        Assert.Equal(HeadlessScenario.AcademyExit, HeadlessOptions.Parse(["--scenario=academy-exit"]).Scenario);
        Assert.Equal(HeadlessScenario.MageBuild, HeadlessOptions.Parse(["--scenario=mage-build"]).Scenario);
        Assert.Equal(HeadlessScenario.Gear, HeadlessOptions.Parse(["--scenario=gear"]).Scenario);
        Assert.Equal(HeadlessScenario.Buff, HeadlessOptions.Parse(["--scenario=buff"]).Scenario);
    }

    [Theory]
    [InlineData("--scenario=walk", "--scenario: 'walk' is not one of login, holtburg-laps, academy-exit, mage-build, gear, buff")]
    [InlineData("--laps=0", "--laps: '0' is not 1 to 1000")]
    [InlineData("--laps=many", "--laps: 'many' is not an integer")]
    [InlineData("--quit-after=-5", "--quit-after: '-5' is not a positive number of seconds")]
    [InlineData("--workers=0", "--workers: '0' is not at least 1")]
    [InlineData("--log-level=loud", "--log-level: 'loud' is not one of debug, info, warning, error")]
    [InlineData("--summary=", "--summary: needs a file path (--name=VALUE)")]
    [InlineData("--vsync=off", "'--vsync=(value hidden)' is an option of the Core (every scene), not read in this run's headless host; ignored")]
    [InlineData("--pasword=hunter2", "unknown argument '--pasword=(value hidden)' (--help lists every option)")]
    [InlineData("--server=localhost:9000", "--server: 'localhost' is not an IP literal in canonical form (host names are never resolved); use 127.0.0.1")]
    public void Problems_are_reported_one_per_line(string argument, string expected)
    {
        var o = HeadlessOptions.Parse([argument]);
        Assert.Equal(expected, Assert.Single(o.Errors));
    }

    [Fact]
    public void Mage_build_refuses_any_account_but_the_default_test_account_loudly_and_exits_1_before_anything_runs()
    {
        // 1.1-R5: BSD Mage is built permanently, on accounts.test only.
        var other = HeadlessOptions.Parse(["--scenario=mage-build", "--account=somebody"]);
        var error = Assert.Single(other.Errors);
        Assert.Equal("--account: --scenario=mage-build runs on the default test account 'test' only; the value is not shown", error);
        Assert.DoesNotContain("somebody", error, StringComparison.Ordinal);
        Assert.Empty(HeadlessOptions.Parse(["--scenario=mage-build"]).Errors);
        Assert.Empty(HeadlessOptions.Parse(["--scenario=mage-build", "--account=test"]).Errors);
        Assert.Empty(HeadlessOptions.Parse(["--scenario=login", "--account=somebody"]).Errors); // the other scenarios keep their own rules

        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(Program.BadArgumentsExitCode, Program.Run(["--scenario=mage-build", "--account=somebody"], output, errors));
        Assert.Contains("mage-build runs on the default test account", errors.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Gear_runs_on_the_default_test_account_only_like_mage_build_and_exits_1_before_anything_runs()
    {
        // 1.4 s1: the gear scenario enters the permanent BSD Mage, so it has the same account rule.
        var error = Assert.Single(HeadlessOptions.Parse(["--scenario=gear", "--account=somebody"]).Errors);
        Assert.Equal("--account: --scenario=gear runs on the default test account 'test' only; the value is not shown", error);
        Assert.DoesNotContain("somebody", error, StringComparison.Ordinal);
        Assert.Empty(HeadlessOptions.Parse(["--scenario=gear"]).Errors);
        Assert.Empty(HeadlessOptions.Parse(["--scenario=gear", "--account=test"]).Errors);

        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(Program.BadArgumentsExitCode, Program.Run(["--scenario=gear", "--account=somebody"], output, errors));
        Assert.Contains("gear runs on the default test account", errors.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Buff_runs_on_the_default_test_account_only_takes_the_buff_options_and_has_a_cycle_long_default_limit()
    {
        // 2.1 s1: the buff scenario enters the permanent BSD Mage like the gear scenario, so it has the same account rule.
        var error = Assert.Single(HeadlessOptions.Parse(["--scenario=buff", "--account=somebody"]).Errors);
        Assert.Equal("--account: --scenario=buff runs on the default test account 'test' only; the value is not shown", error);
        Assert.DoesNotContain("somebody", error, StringComparison.Ordinal);
        Assert.Empty(HeadlessOptions.Parse(["--scenario=buff", "--account=test"]).Errors);

        // The --buff-* options reach the scenario's cycle (and a bad one is reported, not repeated).
        var o = HeadlessOptions.Parse(["--scenario=buff", "--buff-cast-chance=0.95", "--buff-rebuff-margin-s=120", "--buff-transfer-tries=3", "--buff-mana-wait-s=60"]);
        Assert.Empty(o.Errors);
        Assert.Equal((0.95, 120.0, 3, 60.0), (o.Buff.CastChance, o.Buff.RebuffMarginSeconds, o.Buff.TransferTries, o.Buff.ManaWaitSeconds));
        Assert.Single(HeadlessOptions.Parse(["--scenario=buff", "--buff-cast-chance=0"]).Errors);

        // A cycle is about 1,300 s of session time at the live rate (s0b): the default limit leaves room for it and the log off.
        Assert.Equal(3000, Program.BuffQuitAfterSeconds);
        Assert.True(Program.BuffQuitAfterSeconds > 1300 + Program.GearQuitAfterSeconds);

        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(Program.BadArgumentsExitCode, Program.Run(["--scenario=buff", "--account=somebody"], output, errors));
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Names_given_to_the_headless_host_are_never_repeated_in_errors()
    {
        var o = HeadlessOptions.Parse(["--create-character=hunter2\u00e9", "--account=hunter2 x"]);
        Assert.Equal(2, o.Errors.Count);
        Assert.All(o.Errors, e => Assert.DoesNotContain("hunter2", e, StringComparison.Ordinal));
    }

    [Fact]
    public void A_bad_command_line_exits_1_before_anything_runs()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(Program.BadArgumentsExitCode, Program.Run(["--laps=0", "--pasword=hunter2"], output, error));
        Assert.Equal(
            ["headless: unknown argument '--pasword=(value hidden)' (--help lists every option)", "headless: --laps: '0' is not 1 to 1000"],
            error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Empty(output.ToString());
        Assert.DoesNotContain("hunter2", error.ToString(), StringComparison.Ordinal);
    }

    public static TheoryData<string> NonLoopbackServers => [
        new IPEndPoint(new IPAddress([192, 0, 2, 10]), 9000).ToString(),
        new IPEndPoint(new IPAddress(0), 9000).ToString(),
        new IPEndPoint(new IPAddress(new byte[16]), 9000).ToString(),
        new IPEndPoint(IPAddress.Broadcast, 9000).ToString(),
        new IPEndPoint(new IPAddress([198, 51, 100, 7]).MapToIPv6(), 9000).ToString(),
        "10.0.0.5:9000",
        "localhost:9000",
        "example.test:9000",
    ];

    [Theory]
    [MemberData(nameof(NonLoopbackServers))]
    public void The_headless_host_refuses_a_non_loopback_server_before_anything_runs(string server)
    {
        // S8's headless row at the command line: --server is refused while parsing, so no session, socket or file is
        // ever made for it. ST15's HeadlessRunner adds the runner's own row (its gate and its socket-factory spy).
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(Program.BadArgumentsExitCode, Program.Run(["--server=" + server], output, error));
        var line = Assert.Single(error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.StartsWith("headless: --server: ", line, StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void The_help_lists_the_session_and_headless_groups()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, Program.Run(["--help", "--laps=0"], output, error));
        var help = output.ToString();
        foreach (var option in new[] { "  --scenario=", "  --laps=N", "  --create-character=NAME", "  --summary=PATH", "  --play", "  --account=NAME", "  --server=IP:PORT", "  --record-session[=DIR]" })
        {
            Assert.Contains(option, help, StringComparison.Ordinal);
        }
    }
}

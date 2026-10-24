using System.Net;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Scenarios;
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
        Assert.Equal(HeadlessScenario.FighterBuild, HeadlessOptions.Parse(["--scenario=fighter-build"]).Scenario);
        Assert.Equal(HeadlessScenario.Gear, HeadlessOptions.Parse(["--scenario=gear"]).Scenario);
        Assert.Equal(HeadlessScenario.Buff, HeadlessOptions.Parse(["--scenario=buff"]).Scenario);
    }

    [Theory]
    [InlineData("--scenario=walk", "--scenario: 'walk' is not one of login, holtburg-laps, academy-exit, mage-build, fighter-build, gear, buff, fight, death-and-regear, hunt, wayfind")]
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

    /// <summary>1.1-R5, 1.1 R9, 1.2 s4a, 1.4 s1, 2.1 s1 and the death and fight scenarios: each enters a permanent test character (BSD Mage, Holtburg Hunter) and so runs on accounts.test only.</summary>
    [Theory]
    [InlineData("mage-build")]
    [InlineData("gear")]
    [InlineData("fighter-build")]
    [InlineData("hunt")]
    [InlineData("buff")]
    [InlineData("death-and-regear")]
    [InlineData("fight")]
    public void A_scenario_that_enters_a_permanent_test_character_refuses_any_account_but_the_default_test_account_loudly_and_exits_1_before_anything_runs(string scenario)
    {
        var error = Assert.Single(HeadlessOptions.Parse(["--scenario=" + scenario, "--account=somebody"]).Errors);
        // M4 P4 (D31): fighter-build and hunt also run on the accounts of the other fighter templates (HeadlessFighterOptionsTests); the rule still names no value it rejects.
        // M4 P5a C4: buff also runs on the accounts of the fighters with a caster (the Brawler's and the Archer's), so it names them.
        var runsOn = scenario is "fighter-build" or "hunt" ? "the account of a fighter template (test, test-melee, test-archer)"
            : scenario == "buff" ? "the default test account 'test' or the account of a fighter with a caster (test-melee, test-archer)"
            : "the default test account 'test'";
        Assert.Equal($"--account: --scenario={scenario} runs on {runsOn} only; the value is not shown", error);
        Assert.DoesNotContain("somebody", error, StringComparison.Ordinal);
        Assert.Empty(HeadlessOptions.Parse(["--scenario=" + scenario]).Errors);
        Assert.Empty(HeadlessOptions.Parse(["--scenario=" + scenario, "--account=test"]).Errors);
        Assert.Empty(HeadlessOptions.Parse(["--scenario=login", "--account=somebody"]).Errors); // the other scenarios keep their own rules

        using var output = new StringWriter();
        using var errors = new StringWriter();
        Assert.Equal(Program.BadArgumentsExitCode, Program.Run(["--scenario=" + scenario, "--account=somebody"], output, errors));
        Assert.Contains(scenario + " runs on the ", errors.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    /// <summary>P5a S6: the wayfind scenario runs on the account of a fighter template, needs --to, takes the wayfinder's options only with itself, and has a limit sized from the route.</summary>
    [Fact]
    public void Wayfind_runs_on_a_fighter_account_needs_a_destination_and_has_a_route_sized_limit()
    {
        var o = HeadlessOptions.Parse(["--scenario=wayfind", "--to=0xA5B00032", "--wayfind-back"]);
        Assert.Empty(o.Errors);
        Assert.Equal(HeadlessScenario.Wayfind, o.Scenario);
        Assert.Equal(new WayfindPlace(0xA5B00032, null), o.Wayfind.To);
        Assert.True(o.Wayfind.Back);
        Assert.Equal("Holtburg Hunter", o.Fighter!.Name); // the default account is the Hunter's
        Assert.Equal("Holtburg Brawler", HeadlessOptions.Parse(["--scenario=wayfind", "--to=0xA5B00032", "--account=test-melee"]).Fighter!.Name);
        Assert.Equal("Holtburg Archer", HeadlessOptions.Parse(["--scenario=wayfind", "--to=0x9FB50039", "--account=test-archer"]).Fighter!.Name);

        Assert.Equal("--to: --scenario=wayfind needs a destination (--to=0xA5B00032 or --to=CELL@x,y,z)", Assert.Single(HeadlessOptions.Parse(["--scenario=wayfind"]).Errors));
        Assert.Equal("--account: --scenario=wayfind runs on the account of a fighter template (test, test-melee, test-archer) only; the value is not shown", Assert.Single(HeadlessOptions.Parse(["--scenario=wayfind", "--to=0xA5B00032", "--account=somebody"]).Errors));
        Assert.Equal("--to, --from and --wayfind-back: only --scenario=wayfind takes them", Assert.Single(HeadlessOptions.Parse(["--scenario=login", "--to=0xA5B00032"]).Errors));
        Assert.Contains("--to: needs a cell id", Assert.Single(HeadlessOptions.Parse(["--scenario=wayfind", "--to=north"]).Errors), StringComparison.Ordinal);
        Assert.DoesNotContain("north", string.Concat(HeadlessOptions.Parse(["--scenario=wayfind", "--to=north"]).Errors), StringComparison.Ordinal);

        // The default limit is the scenario's, sized from the route; --quit-after overrides it (Program.RunSession).
        Assert.InRange(WayfindScenario.QuitAfterSeconds(o.Wayfind), 1000, 1700);
    }

    [Fact]
    public void Hunt_runs_on_the_default_test_account_only_takes_the_hunt_options_and_has_a_hunt_long_default_limit()
    {
        // The account rule (the hunt enters the permanent Holtburg Hunter) is the theory above's.
        var defaults = HeadlessOptions.Parse(["--scenario=hunt"]);
        Assert.Empty(defaults.Errors);
        Assert.Equal(HeadlessScenario.Hunt, defaults.Scenario);
        Assert.Equal((1, 300.0, HuntWeapon.Melee), (defaults.Hunt.Kills, defaults.Hunt.Seconds, defaults.Hunt.Weapon));
        var set = HeadlessOptions.Parse(["--scenario=hunt", "--hunt-kills=3", "--hunt-seconds=120", "--hunt-weapon=missile"]);
        Assert.Empty(set.Errors);
        Assert.Equal((3, 120.0, HuntWeapon.Missile), (set.Hunt.Kills, set.Hunt.Seconds, set.Hunt.Weapon));
        Assert.Equal(Program.HuntQuitAfterOverheadSeconds + 120, Program.HuntQuitAfterSeconds(set.Hunt.Seconds));
        Assert.NotEmpty(HeadlessOptions.Parse(["--scenario=hunt", "--hunt-kills=0"]).Errors);
        Assert.NotEmpty(HeadlessOptions.Parse(["--scenario=hunt", "--hunt-weapon=spear"]).Errors);
    }

    [Fact]
    public void Buff_runs_on_the_default_test_account_only_takes_the_buff_options_and_has_a_cycle_long_default_limit()
    {
        // The account rule (the buff scenario enters the permanent BSD Mage) is the theory above's.
        // The --buff-* options reach the scenario's cycle (and a bad one is reported, not repeated).
        var o = HeadlessOptions.Parse(["--scenario=buff", "--buff-cast-chance=0.95", "--buff-rebuff-margin-s=120", "--buff-transfer-tries=3", "--buff-mana-wait-s=60"]);
        Assert.Empty(o.Errors);
        Assert.Equal((0.95, 120.0, 3, 60.0), (o.Buff.CastChance, o.Buff.RebuffMarginSeconds, o.Buff.TransferTries, o.Buff.ManaWaitSeconds));
        Assert.Single(HeadlessOptions.Parse(["--scenario=buff", "--buff-cast-chance=0"]).Errors);

        // A cycle is about 1,300 s of session time at the live rate (s0b): the default limit leaves room for it and the log off.
        Assert.Equal(3000, Program.BuffQuitAfterSeconds);
        Assert.True(Program.BuffQuitAfterSeconds > 1300 + Program.GearQuitAfterSeconds);
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

    /// <summary>One literal and one name stand for all: which addresses are loopback is LoopbackPolicyTests'; these rows prove the command line refuses before anything runs.</summary>
    public static TheoryData<string> NonLoopbackServers => ["10.0.0.5:9000", "localhost:9000"];

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

}

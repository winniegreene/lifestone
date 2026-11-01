using Lifestone.Client.Options;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>M5 4 h3: the hosts' own autopilot options (the scripted hand-off, the checkpoint folder), the checkpoint file's name, and the session adapter the hosts give the autopilot's reconnect.</summary>
public sealed class AutopilotHostOptionsTests
{
    private static AutopilotHostOptions Parse(params string[] args) => AutopilotHostOptions.FromArguments(CommandLineArgument.ParseAll(args));

    [Fact]
    public void M5_7_D145_the_control_drill_option_parses_a_folder_is_registered_and_an_empty_one_is_an_error()
    {
        var o = Parse("--autopilot-control-drill=cache/soak/x/control-drill");
        Assert.Empty(o.Errors);
        Assert.Equal("cache/soak/x/control-drill", o.ControlDrillDirectory);
        Assert.Contains("control_drill=on", o.ToString(), StringComparison.Ordinal);
        Assert.Null(Parse().ControlDrillDirectory);
        Assert.StartsWith("--autopilot-control-drill:", Assert.Single(Parse("--autopilot-control-drill=").Errors), StringComparison.Ordinal);
        var option = Assert.Single(OptionCatalog.Autopilot().Options, x => x.Name == "--autopilot-control-drill");
        Assert.Equal("DIR", option.Value);
        Assert.Contains("control drill: step=", option.Help, StringComparison.Ordinal);
        Assert.Empty(AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-control-drill=cache/x"])).Errors); // the loop's own parser ignores it
    }

    [Fact]
    public void M5_7_F18_the_stop_file_option_parses_a_path_and_an_empty_one_is_an_error()
    {
        var o = Parse("--autopilot-stop-file=cache/soak/x/stop");
        Assert.Empty(o.Errors);
        Assert.Equal("cache/soak/x/stop", o.StopFile);
        Assert.Null(Parse().StopFile);
        Assert.Single(Parse("--autopilot-stop-file=").Errors);
    }

    [Fact]
    public void The_scripted_hand_off_and_the_checkpoint_folder_parse()
    {
        var o = Parse("--autopilot-test-interrupt-at=45", "--autopilot-test-handback-after=12.5", "--autopilot-checkpoint-dir=cache/run/autopilot");
        Assert.Empty(o.Errors);
        Assert.Equal((45.0, 12.5, "cache/run/autopilot", true), (o.InterruptAtSeconds, o.HandBackAfterSeconds, o.CheckpointDirectory, o.HasHandOff));
        Assert.Contains("test_interrupt_at=45 s", o.ToString(), StringComparison.Ordinal);
        Assert.False(Parse().HasHandOff);
        Assert.Empty(Parse("--autopilot-test-interrupt-at=0").Errors); // at once is allowed
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("--autopilot-start", true)]
    [InlineData("--autopilot-start=on", true)]
    [InlineData("--autopilot-start=true", true)]
    [InlineData("--autopilot-start=off", false)]
    public void M5_7_s1_autopilot_start_is_a_switch_that_defaults_to_off(string argument, bool expected)
    {
        var o = argument.Length == 0 ? Parse() : Parse(argument);
        Assert.Empty(o.Errors);
        Assert.Equal(expected, o.StartAtLogin);
        Assert.Contains(expected ? "start=on" : "start=off", o.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void M5_7_s1_autopilot_start_with_a_bad_value_is_a_named_error_and_is_registered_and_listed_in_the_help()
    {
        var error = Assert.Single(Parse("--autopilot-start=maybe").Errors);
        Assert.StartsWith("--autopilot-start:", error, StringComparison.Ordinal);
        var option = Assert.Single(OptionCatalog.Autopilot().Options, x => x.Name == "--autopilot-start");
        Assert.Contains("exit code", option.Help, StringComparison.Ordinal);
        Assert.Contains("--autopilot-start", OptionCatalog.HelpText(), StringComparison.Ordinal);
        Assert.Empty(AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-start", "--autopilot-minutes=5"])).Errors); // the loop's own parser ignores it
    }

    [Theory]
    [InlineData("--autopilot-test-interrupt-at=-1")]
    [InlineData("--autopilot-test-interrupt-at=abc")]
    [InlineData("--autopilot-test-interrupt-at=100000")]
    [InlineData("--autopilot-test-handback-after=5")] // a hand-back with no interrupt before it
    [InlineData("--autopilot-checkpoint-dir=")]
    public void A_bad_value_or_a_hand_back_with_no_interrupt_is_a_named_error(string argument)
    {
        var error = Assert.Single(Parse(argument).Errors);
        Assert.StartsWith("--autopilot-", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BSD Mage", "bsd-mage.json")]
    [InlineData("  Lifestone  Smoke!", "lifestone-smoke.json")]
    [InlineData("a/b\\c..d", "a-b-c-d.json")]
    [InlineData("", "character.json")]
    [InlineData("???", "character.json")]
    public void The_checkpoint_file_is_one_safe_name_a_character(string character, string file)
    {
        Assert.Equal(file, AutopilotHostOptions.CheckpointFileName(character));
    }

    [Fact]
    public void The_session_adapter_begins_a_login_per_try_and_names_a_step_before_the_first_try()
    {
        var session = GameSession.Offline(_ => { }, new ManualTimeProvider(), new RecordingLogSink());
        var adapter = new GameSessionAutopilotSession(session, "BSD Mage");
        Assert.Equal(ScenarioStatus.Failed, adapter.StepLogin(0));
        Assert.Contains("before Reconnect", adapter.LoginFailure, StringComparison.Ordinal);

        adapter.Reconnect();
        Assert.Equal((1, 1, null), (adapter.Tries, session.Reconnects, adapter.LoginFailure));
        Assert.Equal(ScenarioStatus.Running, adapter.StepLogin(0)); // the login is sent; nothing answers an offline session with no stand-in
        adapter.Reconnect();
        Assert.Equal((2, 2), (adapter.Tries, session.Reconnects));
        Assert.Throws<ArgumentException>(() => new GameSessionAutopilotSession(session, " "));
    }
}

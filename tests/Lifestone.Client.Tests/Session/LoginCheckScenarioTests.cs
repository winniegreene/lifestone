using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The login check's script (<see cref="LoginCheckScenario"/>, G6 W12) against <see cref="ScriptedAceSession"/>, through the headless runner's loop
/// (the session ticks, portal space plays the arrival with a timed tunnel and sends LoginComplete): the whole first-login path to the character list
/// and quit, a character created when missing, the failures that name their stage, and a host that has already started the login.
/// </summary>
public sealed class LoginCheckScenarioTests
{
    private static readonly uint[] SkillClasses = [.. Enumerable.Repeat(1u, 55)];

    [Fact]
    public void The_whole_first_login_path_holds_and_the_session_ends_at_the_list_and_quits()
    {
        using var run = new Run(new LoginCheckScenario(SkillClasses, "Amy"));
        var result = run.Finish();
        var scenario = run.Scenario;

        Assert.True(result.Ok, result.Failure);
        Assert.True(scenario.Ok);
        Assert.Equal("done", scenario.Stage);
        Assert.Equal(ScriptedAceSession.Amy, scenario.EnteredGuid);
        Assert.False(scenario.Created);
        Assert.Equal(1, scenario.LoginCompletes);
        Assert.Equal(1, result.LoginCompletes);
        Assert.Equal(0, scenario.Teleports);
        Assert.Equal(ScriptedAceSession.AcademyCell, scenario.ArrivalCell);
        Assert.Equal(LoginCheckScenario.AcademyLandblock, scenario.ArrivalCell >> 16);
        Assert.True(scenario.AcademyObjects >= 0);
        Assert.NotNull(scenario.Census);
        Assert.Equal(scenario.Census!.Value.Total, scenario.AcademyObjects);
        Assert.True(scenario.ObjectsHeld >= scenario.AcademyObjects, "the census counts placed objects; the session may hold more (a pack)");
        Assert.True(scenario.LogOffOk);
        Assert.True(scenario.SettledAt - scenario.PresentAt >= LoginCheckScenario.DefaultSettleSeconds, "the player must stay present for the whole wait");
        Assert.True(scenario.PresentAt > scenario.EnterAt);
        Assert.Equal(1, run.Servers.Single().Quits);
        Assert.Equal(SessionPhase.CharacterSelect, result.Phases[^1]);
    }

    [Fact]
    public void A_missing_character_is_created_once_then_entered()
    {
        using var run = new Run(new LoginCheckScenario(SkillClasses, "Lifestone Smoke"));
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        Assert.True(run.Scenario.Created);
        Assert.NotEqual(0u, run.Scenario.EnteredGuid);
        Assert.Contains(run.Servers.Single().Characters, c => c.Name == "Lifestone Smoke");
        Assert.Equal(LoginCheckScenario.DefaultCharacter, run.Scenario.Character);
    }

    [Fact]
    public void A_session_the_host_never_logged_in_fails_the_login_stage()
    {
        using var run = new Run(new LoginCheckScenario(SkillClasses, "Amy"), logIn: false);
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Equal("login", run.Scenario.Stage);
        Assert.Contains("the host never started the login", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Empty(run.Servers.Single().Credentials);
    }

    [Fact]
    public void A_character_pending_deletion_fails_the_choosing_stage()
    {
        using var run = new Run(new LoginCheckScenario(SkillClasses, "Bo"));
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.False(run.Scenario.Ok);
        Assert.Equal("choosing", run.Scenario.Stage);
        Assert.Contains("pending deletion", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.False(run.Scenario.LogOffOk);
    }

    [Fact]
    public void A_lost_connection_before_the_list_fails_the_login_stage_and_names_the_reason()
    {
        using var run = new Run(new LoginCheckScenario(SkillClasses, "Amy"), configure: s => s.FailNow(SessionFailureKind.ConnectTimeout, "the server stopped answering"));
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Equal("login", run.Scenario.Stage);
        Assert.Contains("the connection failed", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(-1, run.Scenario.AcademyObjects);
        Assert.True(double.IsNaN(run.Scenario.SettledAt));
    }

    [Fact]
    public void The_shell_fields_read_the_vitals_and_the_chosen_window_from_the_player_description()
    {
        using var run = new Run(new LoginCheckScenario(SkillClasses, "Amy"));
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        Assert.Equal("30/60/40", run.Scenario.Vitals); // the scripted description (NewCharacter): its current health, stamina and mana
        Assert.Equal("stacked", run.Scenario.VitalsWindow); // its character options (0x50C4A54A) leave bit 21 clear
        Assert.Equal(
            " shell=ok vitals=30/60/40 spew_portal=yes vitals_window=stacked",
            LoginCheckShell.Fields(shellUp: true, run.Scenario.Vitals, spewPortal: true, run.Scenario.VitalsWindow));
        Assert.Equal(
            " shell=missing vitals=empty spew_portal=no vitals_window=side",
            LoginCheckShell.Fields(shellUp: false, LoginCheckShell.Empty, spewPortal: false, "side"));
    }

    [Fact]
    public void The_side_by_side_option_chooses_the_side_window()
    {
        using var run = new Run(
            new LoginCheckScenario(SkillClasses, "Amy"),
            server => server.Description = name => AcePlayerDescription.NewCharacter(name) with { CharacterOptions1 = 0x50C4A54A | 0x200000 });
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        Assert.Equal("side", run.Scenario.VitalsWindow);
        Assert.True(LoginCheckShell.VitalsFilled(run.Scenario.Vitals));
    }

    [Fact]
    public void A_description_that_leaves_the_vitals_empty_fails_the_check_at_the_settle()
    {
        using var run = new Run(
            new LoginCheckScenario(SkillClasses, "Amy"),
            server => server.Description = name => AcePlayerDescription.NewCharacter(name) with
            {
                Attributes = [(0, 0, 0), (0, 0, 0), (0, 0, 0), (0, 0, 0), (0, 0, 0), (0, 0, 0)],
                Vitals = [(0, 0, 0, 0), (0, 0, 0, 0), (0, 0, 0, 0)],
            });
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.False(run.Scenario.Ok);
        Assert.Equal(LoginCheckShell.Empty, run.Scenario.Vitals);
        Assert.Contains("a vital empty", run.Scenario.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void The_report_fields_read_the_level_from_the_description_and_the_mage_from_the_character_list()
    {
        using var run = new Run(
            new LoginCheckScenario(SkillClasses, "Amy"),
            server =>
            {
                server.Characters.Add(new AceCharacter(0x50000009, "BSD Mage"));
                server.Description = name => AcePlayerDescription.NewCharacter(name) with { Ints = [(0x19, 126), (0x05, 1234)] };
            });
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        Assert.Equal("126", run.Scenario.LevelField); // PlayerQualities.Level (PropertyInt 25)
        Assert.Equal("present", run.Scenario.MageField);
        Assert.Equal(" level=126 mage_char=present fighter=absent", run.Scenario.ReportFields);

        using var both = new Run(
            new LoginCheckScenario(SkillClasses, "Amy"),
            server => server.Characters.Add(new AceCharacter(0x5000000C, "Holtburg Hunter")));
        Assert.True(both.Finish().Ok);
        Assert.Equal("present", both.Scenario.FighterField);
    }

    [Fact]
    public void The_level_is_a_question_mark_when_the_description_has_none_and_the_mage_is_absent_from_a_list_without_it()
    {
        using var run = new Run(
            new LoginCheckScenario(SkillClasses, "Amy"),
            server => server.Description = name => AcePlayerDescription.NewCharacter(name) with { Ints = [(0x05, 1234)] });
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        Assert.Equal("?", run.Scenario.LevelField);
        Assert.Equal("absent", run.Scenario.MageField); // Amy, Bo and Cy: no mage
        Assert.Equal(" level=? mage_char=absent fighter=absent", run.Scenario.ReportFields);
    }

    [Fact]
    public void The_mage_name_is_compared_case_insensitively_and_a_pending_deletion_is_named()
    {
        using var pending = new Run(
            new LoginCheckScenario(SkillClasses, "Amy"),
            server => server.Characters.Add(new AceCharacter(0x5000000A, "bsd MAGE", 1)));
        Assert.True(pending.Finish().Ok);
        Assert.Equal("pending-delete", pending.Scenario.MageField);

        using var lower = new Run(
            new LoginCheckScenario(SkillClasses, "Amy"),
            server => server.Characters.Add(new AceCharacter(0x5000000B, "bsd mage")));
        Assert.True(lower.Finish().Ok);
        Assert.Equal("present", lower.Scenario.MageField);
    }

    [Fact]
    public void The_report_fields_never_fail_the_check_and_stay_not_applicable_before_they_are_read()
    {
        // A run that fails at the login stage never read the list or the level: both say n/a and the failure is the login's own.
        using var run = new Run(new LoginCheckScenario(SkillClasses, "Amy"), logIn: false);
        Assert.False(run.Finish().Ok);
        Assert.Equal(" level=n/a mage_char=n/a fighter=n/a", run.Scenario.ReportFields);

        // The pure helpers: the read model's 0 is unknown; the list is read by name.
        Assert.Equal("?", LoginCheckScenario.LevelOf(new PlayerQualities()));
        Assert.Equal("absent", LoginCheckScenario.MageOf([]));
        Assert.Equal("present", LoginCheckScenario.MageOf([new CharacterSlot(1, "Amy", 0), new CharacterSlot(2, "BSD Mage", 0)]));
        Assert.Equal("pending-delete", LoginCheckScenario.MageOf([new CharacterSlot(2, "BSD MAGE", 1)]));
        Assert.Equal("absent", LoginCheckScenario.FighterOf([new CharacterSlot(1, "Amy", 0)]));
        Assert.Equal("present", LoginCheckScenario.FighterOf([new CharacterSlot(3, "holtburg HUNTER", 0)]));
        Assert.Equal("pending-delete", LoginCheckScenario.FighterOf([new CharacterSlot(3, "Holtburg Hunter", 1)]));
        Assert.Equal(" level=126 mage_char=present fighter=present", LoginCheckScenario.Fields("126", "present", "present"));
    }

    [Fact]
    public void The_play_frames_option_takes_a_folder_or_the_default_and_the_plan_names_eight_frames()
    {
        Assert.Null(Lifestone.Client.Play.PlayOptions.Parse(["--play-checks"]).PlayFramesDirectory);
        Assert.Equal(Lifestone.Client.Play.PlayOptions.DefaultFramesDirectory, Lifestone.Client.Play.PlayOptions.Parse(["--play-checks", "--play-frames"]).PlayFramesDirectory);
        Assert.Equal("cache/x", Lifestone.Client.Play.PlayOptions.Parse(["--play-frames=cache/x"]).PlayFramesDirectory);
        Assert.Empty(Lifestone.Client.Play.PlayOptions.Parse(["--play-frames"]).Errors);

        Assert.Equal(10, Lifestone.Client.Play.ShellFramesPlan.FrameNames.Count);
        Assert.Equal(Lifestone.Client.Play.ShellFramesPlan.FrameNames.Count, Lifestone.Client.Play.ShellFramesPlan.FrameNames.Distinct().Count());
        Assert.Equal("docs/screenshots/m2/shell/2560x1440", Lifestone.Client.Play.ShellFramesPlan.FolderFor(Lifestone.Client.Play.PlayOptions.DefaultFramesDirectory, 2560, 1440));
        Assert.Equal("cache/x", Lifestone.Client.Play.ShellFramesPlan.FolderFor("cache/x", 2560, 1440));
        Assert.Equal("shell frames done size=2560x1440 scale=2 captured=10 failures=0 frame_time=reported", Lifestone.Client.Play.ShellFramesPlan.Verdict(2560, 1440, 2, 10, 0));
    }

    [Fact]
    public void Options_name_each_check_and_the_bare_form_is_all()
    {
        Assert.Equal(Lifestone.Client.Play.LoginCheckKind.None, Lifestone.Client.Play.PlayOptions.Parse(["--play"]).LoginCheck);
        foreach (var (arg, kind) in new[]
        {
            ("--login-check=enter", Lifestone.Client.Play.LoginCheckKind.Enter),
            ("--login-check=academy-exit", Lifestone.Client.Play.LoginCheckKind.AcademyExit),
            ("--login-check=holtburg-laps", Lifestone.Client.Play.LoginCheckKind.HoltburgLaps),
            ("--login-check=all", Lifestone.Client.Play.LoginCheckKind.All),
            ("--login-check", Lifestone.Client.Play.LoginCheckKind.All),
        })
        {
            var parsed = Lifestone.Client.Play.PlayOptions.Parse([arg]);
            Assert.Equal(kind, parsed.LoginCheck);
            Assert.Empty(parsed.Errors);
        }

        var bogus = Lifestone.Client.Play.PlayOptions.Parse(["--login-check=bogus"]);
        Assert.Equal(Lifestone.Client.Play.LoginCheckKind.None, bogus.LoginCheck);
        Assert.Single(bogus.Errors);
    }

    private sealed class Run : IDisposable
    {
        public Run(LoginCheckScenario scenario, Action<ScriptedAceSession>? configure = null, bool logIn = true)
        {
            Scenario = scenario;
            Runner = HeadlessRunner.StartWith(
                new HeadlessRunOptions { Session = new SessionOptions { Account = "test" } },
                scenario,
                SessionTestData.SecretsV2(),
                SessionTestData.PidsReady(),
                () => SessionTestData.EndOfRetail,
                () => HeadlessTestWorld.Assets,
                _ => World,
                Time,
                Log,
                Recording,
                Sockets.Open,
                (_, sink, tap) =>
                {
                    var server = new ScriptedAceSession(sink, tap, Time);
                    configure?.Invoke(server);
                    Servers.Add(server);
                    return server;
                });
            if (logIn)
            {
                // What the game's session host does at start-up: the login is under way before the scenario's first step.
                Runner.Session.Actions.Login(ActionSource.Host);
            }
        }

        public LoginCheckScenario Scenario { get; }

        public ManualTimeProvider Time { get; } = new();

        public Lifestone.Client.Tests.RecordingLogSink Log { get; } = new();

        public MemoryStream Recording { get; } = new();

        public SocketFactorySpy Sockets { get; } = new();

        public HeadlessTestWorld World { get; } = new();

        public List<ScriptedAceSession> Servers { get; } = [];

        public HeadlessRunner Runner { get; }

        public HeadlessRunResult Finish(double maxSeconds = 300)
        {
            for (var t = 0.0; t < maxSeconds && !Runner.IsDone; t += 0.05)
            {
                Time.Advance(TimeSpan.FromMilliseconds(50));
                Runner.Step(Runner.Session.Clock.Now);
            }

            Assert.True(Runner.IsDone, $"the run did not end within {maxSeconds} s (stage {Scenario.Stage})");
            return Runner.Result;
        }

        public void Dispose()
        {
            Runner.Dispose();
            Scenario.Dispose();
        }
    }
}

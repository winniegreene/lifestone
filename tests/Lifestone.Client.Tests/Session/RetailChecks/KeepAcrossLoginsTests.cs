using Lifestone.Client.RetailUi.Options;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.RetailChecks;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.RetailChecks;

/// <summary>
/// M5 6 k2, <c>--login-check=keep</c> (D32 and D33) over a scripted session: the check's pure judging, and its whole script (login, select, enter, flip one option, move one window, log off, enter again, read both
/// back, put the option back, log off) against the Action API's harness playing ACE (the character list, the world entries, the log off and the descriptions). What the live run adds is ACE's own storage
/// of the option words and the Godot host's file; here "the server" returns the words it was sent, or the original ones, and the "shell" keeps the window or resets it.
/// </summary>
public sealed class KeepAcrossLoginsTests(ITestOutputHelper output)
{
    private const string Name = "Lifestone Throwaway";
    private const uint Options1 = 0x50C4A54A;
    private const uint Options2 = 0x00948700;

    private static readonly KeepWindow Start = new("<RADA>", 10, 20, 100, 80);
    private static readonly KeepWindow Moved = new("<RADA>", 50, 60, 100, 80);

    [Fact]
    public void The_option_is_a_display_option_in_word_two_and_the_bit_helpers_set_and_clear_it_without_touching_the_other_word()
    {
        var o = KeepAcrossLogins.Option;
        Assert.Equal((KeepAcrossLogins.OptionName, 2, 0x40u, false), (o.Name, o.Word, o.Mask, o.Honoured)); // ACE CharacterOptions2.DisplayTimeStamps; nothing in Lifestone reads it
        Assert.False(KeepAcrossLogins.BitOf(o, Options1, Options2)); // off by default

        var (one, two) = KeepAcrossLogins.WithBit(o, Options1, Options2, on: true);
        Assert.Equal((Options1, Options2 | 0x40), (one, two));
        Assert.True(KeepAcrossLogins.BitOf(o, one, two));
        Assert.Equal((Options1, Options2), KeepAcrossLogins.WithBit(o, one, two, on: false));

        var word1 = PlayerOptionTable.All.First(static x => x.Word == 1);
        var (a, b) = KeepAcrossLogins.WithBit(word1, 0, 0, on: true);
        Assert.Equal((word1.Mask, 0u), (a, b)); // a word-1 option leaves word 2 alone
    }

    [Fact]
    public void The_layout_is_kept_only_when_the_window_is_where_login_1_left_it_and_not_where_it_started()
    {
        var move = new KeepMove(Start, Moved);
        Assert.True(KeepAcrossLogins.LayoutKept(move, Moved));
        Assert.False(KeepAcrossLogins.LayoutKept(move, Start)); // the default place again: not kept
        Assert.False(KeepAcrossLogins.LayoutKept(move, Moved with { X = 51 }));
        Assert.False(KeepAcrossLogins.LayoutKept(move, Moved with { Width = 99 }));
        Assert.False(KeepAcrossLogins.LayoutKept(move, Moved with { Tag = "<VITS>" })); // another window
        Assert.False(KeepAcrossLogins.LayoutKept(move, null)); // the shell has no such window
        Assert.False(KeepAcrossLogins.LayoutKept(new KeepMove(Start, Start), Start)); // a move that moved nothing proves nothing
    }

    [Fact]
    public void The_file_was_read_only_by_a_new_screen_whose_read_found_it_and_applied_a_window()
    {
        var first = new KeepLayoutRead(1, false, 0, 1);
        Assert.True(KeepAcrossLogins.FileWasRead(first, new KeepLayoutRead(2, true, 15, 1)));
        Assert.False(KeepAcrossLogins.FileWasRead(first, new KeepLayoutRead(1, true, 15, 1))); // the same screen: the shell survived the log off
        Assert.False(KeepAcrossLogins.FileWasRead(first, new KeepLayoutRead(2, false, 0, 1))); // no file found
        Assert.False(KeepAcrossLogins.FileWasRead(first, new KeepLayoutRead(2, true, 0, 1))); // found, nothing applied
        Assert.False(KeepAcrossLogins.FileWasRead(first, new KeepLayoutRead(2, true, 15, 0))); // no read at all
    }

    [Fact]
    public void A_window_that_stays_where_it_was_moved_without_the_file_being_read_is_not_kept_and_the_window_is_put_back()
    {
        var survived = Run(serverKeepsOption: true, shellKeepsWindow: true, newScreen: false);
        Assert.Equal("keep options_kept=yes layout_kept=no option=DisplayTimeStamps window=<RADA>", survived.Leg.Verdict); // the place reads right, but no new screen read a file
        Assert.Contains(survived.Leg.Problems, static p => p.Contains("layout file was not read", StringComparison.Ordinal));

        var noFile = Run(serverKeepsOption: true, shellKeepsWindow: true, fileFound: false);
        Assert.Equal("keep options_kept=yes layout_kept=no option=DisplayTimeStamps window=<RADA>", noFile.Leg.Verdict);

        var kept = Run(serverKeepsOption: true, shellKeepsWindow: true);
        Assert.Equal(1, kept.Shell.PutBacks); // the window goes back to where it started before the last log off
        Assert.Equal(Start, kept.Shell.Current);
    }

    [Fact]
    public void The_verdict_line_has_the_four_judged_fields_and_nothing_that_names_a_character()
    {
        Assert.Equal("keep options_kept=yes layout_kept=no option=DisplayTimeStamps window=<RADA>", KeepAcrossLogins.VerdictLine(true, false, KeepAcrossLogins.OptionName, "<RADA>"));
        var shell = new FakeShell(Start);
        var leg = new KeepAcrossLogins(shell, _ => { }, Name);
        Assert.Equal("keep options_kept=no layout_kept=no option=DisplayTimeStamps window=n/a", leg.Verdict); // nothing read yet
        Assert.False(leg.Ok);
        Assert.DoesNotContain("Lifestone", leg.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_logins_keep_the_option_and_the_window_when_the_server_and_the_layout_return_them()
    {
        var run = Run(serverKeepsOption: true, shellKeepsWindow: true);

        Assert.True(run.Leg.Ok, string.Join("; ", run.Leg.Problems) + " " + run.Leg.Failure);
        Assert.Equal("keep options_kept=yes layout_kept=yes option=DisplayTimeStamps window=<RADA>", run.Leg.Verdict);
        Assert.True(run.Leg.FirstLoginDone && run.Leg.SecondLoginRead);
        Assert.Equal(2, run.Harness.Net.OptionBlocks.Count); // the flip, and the put-back
        Assert.Equal(Options2 | 0x40, run.Harness.Net.OptionBlocks[0].Options2);
        Assert.Equal(Options1, run.Harness.Net.OptionBlocks[0].Options1);
        Assert.Equal((Options1, Options2), (run.Harness.Net.OptionBlocks[1].Options1, run.Harness.Net.OptionBlocks[1].Options2)); // back as found
        Assert.Equal(2, run.Harness.Net.Of(FakeSendKind.EnterWorld).Count());
        Assert.Equal(2, run.Harness.Net.Of(FakeSendKind.LogOff).Count());
        Assert.Equal(1, run.Shell.Moves);
    }

    [Fact]
    public void A_server_that_forgets_the_option_and_a_shell_that_forgets_the_window_read_as_no_and_no()
    {
        var run = Run(serverKeepsOption: false, shellKeepsWindow: false);

        Assert.Equal("keep options_kept=no layout_kept=no option=DisplayTimeStamps window=<RADA>", run.Leg.Verdict);
        Assert.False(run.Leg.Ok);
        Assert.True(run.Leg.SecondLoginRead); // the check ran to its end: a report-only judge says no, it does not fail the run
        Assert.Equal(2, run.Leg.Problems.Count);
        Assert.Equal(2, run.Harness.Net.OptionBlocks.Count); // it still put the option back
    }

    [Fact]
    public void A_description_that_never_arrives_in_login_2_fails_the_check_rather_than_reading_the_model_that_applied_the_words_at_once()
    {
        var run = Run(serverKeepsOption: true, shellKeepsWindow: true, describeInLogin2: false);

        Assert.False(run.Leg.SecondLoginRead);
        Assert.NotNull(run.Leg.Failure);
        Assert.True(run.Leg.Failure!.Contains("no PlayerDescription arrived", StringComparison.Ordinal) || run.Leg.Failure.Contains("arriving", StringComparison.Ordinal), run.Leg.Failure);
    }

    [Fact]
    public void A_character_missing_from_the_list_without_an_intent_fails_at_select_and_a_shell_that_never_builds_fails_at_the_shell()
    {
        var noChar = Run(serverKeepsOption: true, shellKeepsWindow: true, listed: false);
        Assert.Contains("cannot be created here", noChar.Leg.Failure, StringComparison.Ordinal);

        var noShell = Run(serverKeepsOption: true, shellKeepsWindow: true, shellReady: false);
        Assert.Contains("in-game shell was not built", noShell.Leg.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void In_the_play_host_the_leg_waits_for_the_hosts_own_login_and_never_asks_for_a_second_one()
    {
        var run = Run(serverKeepsOption: true, shellKeepsWindow: true, hostLogsIn: true);
        Assert.Empty(run.Harness.Net.Of(FakeSendKind.Login));
        Assert.True(run.Leg.Ok, string.Join("; ", run.Leg.Problems) + " " + run.Leg.Failure);
        Assert.Equal("keep options_kept=yes layout_kept=yes option=DisplayTimeStamps window=<RADA>", run.Leg.Verdict);
    }

    [Fact]
    public void The_login_check_option_names_the_keep_leg_and_the_bare_form_stays_all()
    {
        Assert.Equal(Lifestone.Client.Play.LoginCheckKind.Keep, Lifestone.Client.Play.PlayOptions.Parse(["--login-check=keep"]).LoginCheck);
        Assert.Empty(Lifestone.Client.Play.PlayOptions.Parse(["--login-check=keep"]).Errors);
        Assert.Equal(Lifestone.Client.Play.LoginCheckKind.All, Lifestone.Client.Play.PlayOptions.Parse(["--login-check"]).LoginCheck);
    }

    // ------------------------------------------------------------------------------------------------ the rig

    private sealed record Result(KeepAcrossLogins Leg, ActionHarness Harness, FakeShell Shell);

    private Result Run(bool serverKeepsOption, bool shellKeepsWindow, bool describeInLogin2 = true, bool listed = true, bool shellReady = true, bool newScreen = true, bool fileFound = true, bool hostLogsIn = false)
    {
        var h = new ActionHarness();
        var shell = new FakeShell(Start) { Ready = shellReady };
        var leg = new KeepAcrossLogins(shell, line => output.WriteLine(line), Name) { LiveStays = true, HostLogsIn = hostLogsIn };
        var view = new View(h);
        var logins = 0;
        var enters = 0;
        var logoffs = 0;
        var status = Lifestone.Client.Session.Scenarios.ScenarioStatus.Running;
        for (var i = 0; i < 6000 && status == Lifestone.Client.Session.Scenarios.ScenarioStatus.Running; i++)
        {
            h.Tick(0.1);
            status = leg.Step(view, h.R.Clock.Now);
            h.Tick(0); // the action sent by the step goes out
            if (hostLogsIn && i == 5 && h.State.Phase == SessionPhase.Idle)
            {
                h.AtCharacterSelect(new[] { (Name, 0u, ActionHarness.Amy) }); // the play host's own login reaches the list
            }

            if (h.Net.Of(FakeSendKind.Login).Count() > logins && h.State.Phase == SessionPhase.Idle)
            {
                logins++;
                h.AtCharacterSelect(listed ? new[] { (Name, 0u, ActionHarness.Amy) } : new[] { ("Someone Else", 0u, ActionHarness.Bo) });
            }
            else if (h.Net.Of(FakeSendKind.EnterWorld).Count() > enters && h.State.Phase == SessionPhase.CharacterSelect)
            {
                enters++;
                h.Present();
                if (enters == 1 || describeInLogin2)
                {
                    var (o1, o2) = enters == 1 || serverKeepsOption ? (h.Net.OptionBlocks.Count > 0 ? h.Net.OptionBlocks[0].Options1 : Options1, h.Net.OptionBlocks.Count > 0 ? h.Net.OptionBlocks[0].Options2 : Options2) : (Options1, Options2);
                    h.Describe(new PlayerDescriptionMsg { Options1 = o1, Options2 = o2 });
                }

                shell.ScreenId = newScreen ? enters : 1; // a shell that survives the log off keeps its screen
                shell.Found = enters == 1 ? !fileFound : fileFound;
                if (enters == 2)
                {
                    shell.Current = shellKeepsWindow ? Moved : Start;
                }
            }
            else if (h.Net.Of(FakeSendKind.LogOff).Count() > logoffs && h.State.Phase == SessionPhase.InWorld)
            {
                logoffs++;
                h.Phase(SessionPhase.InWorld, SessionPhase.LoggingOff);
                h.R.Reducer.OnCharacterLogOff(h.R.Context);
                h.CharacterList((Name, 0u, ActionHarness.Amy));
                h.Phase(SessionPhase.LoggingOff, SessionPhase.CharacterSelect);
            }
        }

        output.WriteLine($"{status} stage={leg.Stage} {leg.Verdict} {leg.VerdictFields()}");
        return new Result(leg, h, shell);
    }

    private sealed class FakeShell(KeepWindow initial) : IKeepShell
    {
        public KeepWindow Current { get; set; } = initial;

        public bool Ready { get; init; } = true;

        public int Moves { get; private set; }

        public bool ShellReady => Ready;

        public KeepMove? MoveOneWindow()
        {
            Moves++;
            var before = Current;
            Current = Moved;
            return new KeepMove(before, Moved);
        }

        public void PutBack(KeepWindow original)
        {
            PutBacks++;
            Current = original;
        }

        public int PutBacks { get; private set; }

        public int ScreenId { get; set; } = 1;

        public bool Found { get; set; }

        public KeepLayoutRead LayoutRead => new(ScreenId, Found, Found ? 15 : 0, 1);

        public KeepWindow? Read(string tag) => tag == Current.Tag ? Current : null;
    }

    private sealed class View(ActionHarness h) : IGameSessionView
    {
        public GameState State => h.State;

        public IEventBus Events => h.R.Bus;

        public IActionApi Actions => h.Api;

        public PlayerTools Tools => null!;

        public SessionClock Clock => h.R.Clock;

        public ActionLog ActionLog => h.ActionLog;

        public LinkStats Link => default;
    }
}

using System.Reflection;
using Lifestone.Client.RetailUi.Chat;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Tests.RetailUi.Chat;

/// <summary>
/// The entry's '/' commands (docs/retail-ui.md section 21.2): retail's <c>DoCommand</c> parse (the first word, case-blind; the arguments joined with single spaces),
/// the comma rule of <c>DoTell</c>, the reply and retell memories, the emote, AFK, the two recalls, the commands answered by name, the unknown command sent on to the
/// server, and the talk focus rule (<see cref="ChatCommands.Say"/>). No dats: the Action API is a recording proxy.
/// </summary>
public sealed class ChatCommandsTests
{
    private const string Null = "<null>";

    private static (ChatCommands Commands, Recorder Recorder) Make(string? lastTeller = null, bool afk = false)
    {
        var actions = DispatchProxy.Create<IActionApi, Recorder>();
        var recorder = (Recorder)(object)actions;
        return (new ChatCommands(actions, () => lastTeller, () => afk), recorder);
    }

    // ------------------------------------------------------------------ tell

    [Theory]
    [InlineData("/t Lifestone Tools, hello")]
    [InlineData("/tell Lifestone Tools, hello")]
    [InlineData("/TELL Lifestone Tools, hello")]
    [InlineData("/send Lifestone Tools, hello")]
    [InlineData("/whisper Lifestone Tools, hello")]
    [InlineData("/w Lifestone Tools, hello")]
    [InlineData("  /t   Lifestone   Tools ,   hello  ")]
    [InlineData("/t +Lifestone Tools, hello")]
    public void A_tell_names_the_player_up_to_the_comma_and_sends_the_rest_from_the_Ui(string line)
    {
        var (commands, recorder) = Make();
        var result = commands.Run(line);
        var call = Assert.Single(recorder.Calls);
        Assert.Equal("Tell", call.Name);
        Assert.Equal(["Lifestone Tools", "hello"], call.Args);
        Assert.Equal(ActionSource.Ui, call.Source);
        Assert.Equal(ActionKind.Tell, Assert.IsAssignableFrom<ActionHandle>(result.Handle).Kind);
        Assert.Equal("Lifestone Tools", commands.LastTellee);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void A_tell_keeps_the_commas_after_the_first_and_collapses_runs_of_spaces_as_retail_joins_the_words()
    {
        var (commands, recorder) = Make();
        commands.Run("/t Bob, one,  two   three, four");
        Assert.Equal(["Bob", "one, two three, four"], Assert.Single(recorder.Calls).Args);
    }

    [Theory]
    [InlineData("/t Bob hello")]
    [InlineData("/t")]
    [InlineData("/t , hello")]
    [InlineData("/tell ,")]
    public void A_tell_without_a_name_before_a_comma_gets_retails_usage_line_and_sends_nothing(string line)
    {
        var (commands, recorder) = Make();
        var result = commands.Run(line);
        Assert.Empty(recorder.Calls);
        Assert.Null(result.Handle);
        var shown = Assert.Single(result.Lines);
        Assert.Equal(new ChatCommandLine(ChatTextTypes.SpewBox, "Use comma after the name for targeted chat."), shown);
        Assert.Null(commands.LastTellee);
    }

    [Fact]
    public void A_tell_with_no_text_is_handed_on_empty_and_the_action_layer_refuses_it()
    {
        var (commands, recorder) = Make();
        commands.Run("/t Bob,");
        Assert.Equal(["Bob", ""], Assert.Single(recorder.Calls).Args);
    }

    // ------------------------------------------------------------------ reply and retell

    [Theory]
    [InlineData("/r thanks")]
    [InlineData("/reply thanks")]
    [InlineData("/rp thanks")]
    [InlineData("/R thanks")]
    public void A_reply_tells_the_last_teller(string line)
    {
        var (commands, recorder) = Make(lastTeller: "Holtburg Hunter");
        var result = commands.Run(line);
        var call = Assert.Single(recorder.Calls);
        Assert.Equal("Tell", call.Name);
        Assert.Equal(["Holtburg Hunter", "thanks"], call.Args);
        Assert.Equal(ActionSource.Ui, call.Source);
        Assert.NotNull(result.Handle);
    }

    [Fact]
    public void A_reply_with_no_one_to_reply_to_says_so_in_retails_words_and_sends_nothing()
    {
        var (commands, recorder) = Make(lastTeller: null);
        var result = commands.Run("/r thanks");
        Assert.Empty(recorder.Calls);
        Assert.Equal(new ChatCommandLine(ChatTextTypes.SpewBox, "Someone must @tell you first!"), Assert.Single(result.Lines));
    }

    [Fact]
    public void A_reply_with_no_text_asks_for_it_before_looking_for_a_teller()
    {
        var (commands, recorder) = Make(lastTeller: "Holtburg Hunter");
        var result = commands.Run("/r");
        Assert.Empty(recorder.Calls);
        Assert.Equal(new ChatCommandLine(ChatTextTypes.SpewBox, "You must specify the text you wish to say!"), Assert.Single(result.Lines));
    }

    [Fact]
    public void A_retell_goes_to_the_last_player_this_one_told()
    {
        var (commands, recorder) = Make();
        var none = commands.Run("/rt hi again");
        Assert.Empty(recorder.Calls);
        Assert.Equal("You must first provide a name using @tell", Assert.Single(none.Lines).Text);

        commands.Run("/t Bob, hi");
        commands.Run("/retell hi again");
        Assert.Equal(2, recorder.Calls.Count);
        Assert.Equal(["Bob", "hi again"], recorder.Calls[1].Args);
    }

    // ------------------------------------------------------------------ say and emote

    [Theory]
    [InlineData("/e waves hello", "waves hello")]
    [InlineData("/emote waves   hello", "waves hello")]
    [InlineData("/em waves", "waves")]
    [InlineData("/me waves", "waves")]
    [InlineData("/EMOTE waves", "waves")]
    public void An_emote_sends_its_text_from_the_Ui(string line, string text)
    {
        var (commands, recorder) = Make();
        var result = commands.Run(line);
        var call = Assert.Single(recorder.Calls);
        Assert.Equal("Emote", call.Name);
        Assert.Equal([text], call.Args);
        Assert.Equal(ActionSource.Ui, call.Source);
        Assert.Equal(ActionKind.Emote, result.Handle!.Kind);
    }

    [Fact]
    public void An_empty_emote_does_nothing_as_retails_does()
    {
        var (commands, recorder) = Make();
        var result = commands.Run("/e");
        Assert.Empty(recorder.Calls);
        Assert.Empty(result.Lines);
        Assert.Null(result.Handle);
    }

    [Theory]
    [InlineData("/say hello there")]
    [InlineData("/s hello there")]
    public void Say_is_local_speech(string line)
    {
        var (commands, recorder) = Make();
        var result = commands.Run(line);
        var call = Assert.Single(recorder.Calls);
        Assert.Equal("Talk", call.Name);
        Assert.Equal(["hello there"], call.Args);
        Assert.True(result.IsTalk);
    }

    // ------------------------------------------------------------------ afk

    [Theory]
    [InlineData("/afk")]
    [InlineData("/afk on")]
    [InlineData("/AFK ON")]
    public void Afk_turns_the_mode_on_when_it_is_off(string line)
    {
        var (commands, recorder) = Make(afk: false);
        var result = commands.Run(line);
        var call = Assert.Single(recorder.Calls);
        Assert.Equal("SetAfk", call.Name);
        Assert.Equal(["True", Null], call.Args);
        Assert.Equal(ActionSource.Ui, call.Source);
        Assert.NotNull(result.SuccessLine);
    }

    [Theory]
    [InlineData("/afk")]
    [InlineData("/afk on")]
    public void Afk_on_while_away_sends_nothing(string line)
    {
        var (commands, recorder) = Make(afk: true);
        commands.Run(line);
        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public void Afk_off_turns_the_mode_off_only_while_away()
    {
        var (away, awayCalls) = Make(afk: true);
        away.Run("/afk off");
        var call = Assert.Single(awayCalls.Calls);
        Assert.Equal(["False", Null], call.Args);

        var (back, backCalls) = Make(afk: false);
        back.Run("/afk off");
        Assert.Empty(backCalls.Calls);
    }

    [Fact]
    public void Afk_msg_sets_the_message_with_retails_confirmation_line_and_names_the_mode_todo()
    {
        var (commands, recorder) = Make();
        var result = commands.Run("/afk msg  gone   to  lunch ");
        var call = Assert.Single(recorder.Calls);
        Assert.Equal(["True", "gone to lunch"], call.Args);
        Assert.Equal(ActionSource.Ui, call.Source);
        Assert.Equal(new ChatCommandLine(0, "New AFK message set: gone to lunch"), Assert.Single(result.Lines));
        Assert.Equal("chat:afk-message-only", result.TodoKey);
        Assert.Contains("TODO(m5-6-afk-message-only)", result.TodoMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Afk_msg_is_cut_at_retails_limit_and_an_empty_one_uses_the_default_words()
    {
        var (commands, recorder) = Make();
        commands.Run("/afk msg " + new string('x', 300));
        Assert.Equal(ChatCommands.AfkMessageLimit, recorder.Calls[0].Args[1].Length);

        var result = commands.Run("/afk msg");
        Assert.Equal(["True", Null], recorder.Calls[1].Args);
        Assert.Equal("New AFK message set: I am currently away from the keyboard.", Assert.Single(result.Lines).Text);
    }

    [Fact]
    public void Afk_with_another_word_gets_a_usage_line_and_sends_nothing()
    {
        var (commands, recorder) = Make();
        var result = commands.Run("/afk gone");
        Assert.Empty(recorder.Calls);
        Assert.Equal(ChatTextTypes.SpewBox, Assert.Single(result.Lines).Type);
        Assert.Equal("chat:afk-help", result.TodoKey);
    }

    // ------------------------------------------------------------------ recalls

    [Theory]
    [InlineData("/lifestone")]
    [InlineData("/lif")]
    [InlineData("/ls")]
    [InlineData("/LIFESTONE")]
    public void The_lifestone_command_asks_for_the_recall_from_the_Ui(string line)
    {
        var (commands, recorder) = Make();
        var result = commands.Run(line);
        var call = Assert.Single(recorder.Calls);
        Assert.Equal("RecallLifestone", call.Name);
        Assert.Equal(ActionSource.Ui, call.Source);
        Assert.Equal(ActionKind.RecallLifestone, result.Handle!.Kind);
        Assert.Contains("lifestone recall did not happen", result.FailurePrefix, StringComparison.Ordinal);
        Assert.NotNull(result.SuccessLine);
    }

    [Theory]
    [InlineData("/marketplace")]
    [InlineData("/mar")]
    [InlineData("/mp")]
    public void The_marketplace_command_asks_for_the_recall_from_the_Ui(string line)
    {
        var (commands, recorder) = Make();
        var result = commands.Run(line);
        var call = Assert.Single(recorder.Calls);
        Assert.Equal("RecallMarketplace", call.Name);
        Assert.Equal(ActionSource.Ui, call.Source);
        Assert.Equal(ActionKind.RecallMarketplace, result.Handle!.Kind);
    }

    [Theory]
    [InlineData("/lifestone now", "lifestone")]
    [InlineData("/mp now", "marketplace")]
    public void A_recall_given_arguments_points_to_the_help_and_does_not_recall(string line, string word)
    {
        var (commands, recorder) = Make();
        var result = commands.Run(line);
        Assert.Empty(recorder.Calls);
        Assert.Equal(new ChatCommandLine(ChatTextTypes.SpewBox, "Please see @help " + word + " for more information on how to use this command."), Assert.Single(result.Lines));
    }

    // ------------------------------------------------------------------ answered by name, and unknown

    [Theory]
    [InlineData("/general hello", "/general")]
    [InlineData("/trade WTS sword", "/trade")]
    [InlineData("/lfg", "/lfg")]
    [InlineData("/a hello", "/a")]
    [InlineData("/f hello", "/f")]
    [InlineData("/p hello", "/p")]
    [InlineData("/Monarch hello", "/monarch")]
    public void A_channel_command_is_answered_by_name_as_M6s_and_sends_nothing(string line, string name)
    {
        var (commands, recorder) = Make();
        var result = commands.Run(line);
        Assert.Empty(recorder.Calls);
        Assert.Null(result.Handle);
        Assert.Equal("chat:channels", result.TodoKey);
        Assert.Contains("TODO(g4-chat-channels)", result.TodoMessage, StringComparison.Ordinal);
        var shown = Assert.Single(result.Lines);
        Assert.Contains("chat to " + name, shown.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("hello", shown.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/help", "help")]
    [InlineData("/Friends", "friends")]
    [InlineData("/house", "house")]
    public void A_retail_client_command_that_is_not_built_is_answered_by_name_with_a_todo_and_not_sent(string line, string name)
    {
        var (commands, recorder) = Make();
        var result = commands.Run(line);
        Assert.Empty(recorder.Calls);
        Assert.Contains("/" + name, Assert.Single(result.Lines).Text, StringComparison.Ordinal);
        Assert.Equal("chat:cmd:" + name, result.TodoKey);
        Assert.Contains("TODO(g4-chat-commands-rest)", result.TodoMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_outside_retails_table_goes_to_the_server_as_Talk_with_an_at_sign_as_retail_sends_it()
    {
        var (commands, recorder) = Make();
        var result = commands.Run("/acehelp  now");
        var call = Assert.Single(recorder.Calls);
        Assert.Equal("Talk", call.Name);
        Assert.Equal(["@acehelp  now"], call.Args);
        Assert.Equal(ActionSource.Ui, call.Source);
        Assert.True(result.IsTalk);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void A_blank_line_runs_nothing()
    {
        var (commands, recorder) = Make();
        Assert.Same(ChatCommandResult.Nothing, commands.Run("   "));
        Assert.Empty(recorder.Calls);
    }

    [Theory]
    [InlineData("/tell x, y", true)]
    [InlineData("   /tell", true)]
    [InlineData("@help", false)]
    [InlineData("hello /there", false)]
    [InlineData("", false)]
    public void Only_a_line_that_starts_with_a_slash_is_a_command(string line, bool expected) => Assert.Equal(expected, ChatCommands.IsCommand(line));

    // ------------------------------------------------------------------ the talk focus

    [Fact]
    public void The_All_focus_speaks_the_text_locally()
    {
        var (commands, recorder) = Make();
        var result = commands.Say(TalkFocus.All, "hello", "Bob");
        var call = Assert.Single(recorder.Calls);
        Assert.Equal(("Talk", ActionSource.Ui), (call.Name, call.Source));
        Assert.Equal(["hello"], call.Args);
        Assert.True(result.IsTalk);
    }

    [Fact]
    public void The_Selected_focus_tells_the_selected_creature_by_name()
    {
        var (commands, recorder) = Make();
        var result = commands.Say(TalkFocus.Selected, "hello", "Holtburg Hunter");
        var call = Assert.Single(recorder.Calls);
        Assert.Equal("Tell", call.Name);
        Assert.Equal(["Holtburg Hunter", "hello"], call.Args);
        Assert.Equal(ActionSource.Ui, call.Source);
        Assert.False(result.IsTalk);
        Assert.Contains("Holtburg Hunter", result.FailurePrefix, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void The_Selected_focus_with_nothing_selected_says_so_and_sends_nothing(string? selected)
    {
        var (commands, recorder) = Make();
        var result = commands.Say(TalkFocus.Selected, "hello", selected);
        Assert.Empty(recorder.Calls);
        Assert.Null(result.Handle);
        Assert.Contains("select a creature", Assert.Single(result.Lines).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TalkFocus.Monarch)]
    [InlineData(TalkFocus.Fellows)]
    [InlineData(TalkFocus.General)]
    [InlineData(TalkFocus.Allegiance)]
    public void A_channel_focus_is_answered_by_name_as_M6s_and_sends_nothing(TalkFocus focus)
    {
        var (commands, recorder) = Make();
        var result = commands.Say(focus, "hello", "Bob");
        Assert.Empty(recorder.Calls);
        Assert.Equal("chat:channels", result.TodoKey);
        Assert.Contains(focus.ToString(), Assert.Single(result.Lines).Text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the autopilot's controls (M5 7 D145)

    private sealed class Controls : IAutopilotChatControls
    {
        public List<string> Calls { get; } = [];

        public string Stop()
        {
            Calls.Add("stop");
            return "stopped line";
        }

        public string Auto()
        {
            Calls.Add("auto");
            return "auto line";
        }
    }

    [Theory]
    [InlineData("/stop", "stop", "stopped line")]
    [InlineData("/STOP", "stop", "stopped line")]
    [InlineData("  /stop  ", "stop", "stopped line")]
    [InlineData("/stop now please", "stop", "stopped line")]
    [InlineData("/auto", "auto", "auto line")]
    [InlineData("/Auto", "auto", "auto line")]
    [InlineData("/auto,", "auto", "auto line")]
    public void M5_7_D145_stop_and_auto_are_the_clients_own_commands_answered_with_a_local_line_and_never_sent(string line, string call, string answer)
    {
        var (commands, recorder) = Make();
        var controls = new Controls();
        commands.Autopilot = controls;
        var result = commands.Run(line);
        Assert.Empty(recorder.Calls); // never sent: the recording Action API throws on anything but the chat's calls and saw none
        Assert.Null(result.Handle);
        Assert.False(result.IsTalk);
        Assert.True(result.ClientHandled);
        Assert.Equal(new ChatCommandLine(ChatTextTypes.System, answer), Assert.Single(result.Lines));
        Assert.Equal([call], controls.Calls);
        Assert.Null(result.TodoKey);
    }

    [Theory]
    [InlineData("/stop")]
    [InlineData("/auto")]
    public void M5_7_D145_stop_and_auto_without_an_autopilot_say_so_and_still_send_nothing(string line)
    {
        var (commands, recorder) = Make();
        var result = commands.Run(line);
        Assert.Empty(recorder.Calls);
        Assert.Null(result.Handle);
        Assert.Equal(new ChatCommandLine(ChatTextTypes.System, ChatCommands.NoAutopilotLine), Assert.Single(result.Lines));
    }

    [Theory]
    [InlineData("/stop-now")] // ACE's own command: a different name, still the server's
    [InlineData("/stopall")]
    [InlineData("/autorun")]
    public void M5_7_D145_names_that_only_start_like_stop_or_auto_are_not_the_controls_and_go_to_the_server(string line)
    {
        var (commands, recorder) = Make();
        var controls = new Controls();
        commands.Autopilot = controls;
        var result = commands.Run(line);
        Assert.Empty(controls.Calls);
        Assert.True(result.IsTalk);
        Assert.Equal("@" + line[1..], Assert.Single(Assert.Single(recorder.Calls).Args));
    }

    [Fact]
    public void M5_7_D145_neither_control_is_a_name_of_retails_client_table()
    {
        // InitializeCommands (11.6096 0x005827F0): the names the parser builds, the channels and the rest of the table. /stop and /auto are none of them, so Lifestone's two take no retail command's place.
        var table = typeof(ChatCommands).GetFields(BindingFlags.NonPublic | BindingFlags.Static).Where(static f => f.FieldType == typeof(HashSet<string>)).SelectMany(static f => (HashSet<string>)f.GetValue(null)!).ToList();
        Assert.True(table.Count > 50, "the table's sets were read: " + table.Count);
        Assert.DoesNotContain(table, static n => n.Equals(ChatCommands.StopCommand, StringComparison.OrdinalIgnoreCase) || n.Equals(ChatCommands.AutoCommand, StringComparison.OrdinalIgnoreCase));
        string[] built = ["tell", "t", "send", "whisper", "w", "reply", "r", "rp", "retell", "rt", "say", "s", "e", "em", "emote", "me", "afk", "lifestone", "lif", "ls", "marketplace", "mar", "mp"];
        Assert.DoesNotContain(ChatCommands.StopCommand, built);
        Assert.DoesNotContain(ChatCommands.AutoCommand, built);
    }

    // ------------------------------------------------------------------ what comes of an action

    [Fact]
    public void A_failed_action_is_reported_with_its_prefix_and_the_servers_detail()
    {
        Assert.Equal(
            "Lifestone: the lifestone recall did not happen (YoureTooBusy).",
            ChatCommands.FailureLine("the lifestone recall did not happen", new ActionOutcome(ActionOutcomeKind.ServerError, 0x001D, "YoureTooBusy")));
        Assert.Equal("Lifestone: your message was not sent (Refused).", ChatCommands.FailureLine("your message was not sent", new ActionOutcome(ActionOutcomeKind.Refused)));
    }

    /// <summary>An <see cref="IActionApi"/> that records the chat-related calls and answers a handle for each; anything else is not part of the chat.</summary>
    public class Recorder : DispatchProxy
    {
        private long _nextId;

        /// <summary>The calls so far.</summary>
        public List<Call> Calls { get; } = [];

        private static string Text(object? value) => value is null ? "<null>" : value.ToString()!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            var arguments = args ?? [];
            (ActionKind Kind, string[] Args)? recorded = name switch
            {
                nameof(IActionApi.Talk) => (ActionKind.Talk, [Text(arguments[0])]),
                nameof(IActionApi.Tell) => (ActionKind.Tell, [Text(arguments[0]), Text(arguments[1])]),
                nameof(IActionApi.Emote) => (ActionKind.Emote, [Text(arguments[0])]),
                nameof(IActionApi.SetAfk) => (ActionKind.SetAfk, [Text(arguments[0]), Text(arguments[1])]),
                nameof(IActionApi.RecallLifestone) => (ActionKind.RecallLifestone, []),
                nameof(IActionApi.RecallMarketplace) => (ActionKind.RecallMarketplace, []),
                _ => null,
            };
            if (recorded is not { } call)
            {
                throw new NotSupportedException("the chat commands called " + name);
            }

            var source = (ActionSource)arguments[^1]!;
            Calls.Add(new Call(name, call.Args, source));
            return new ActionHandle(++_nextId, call.Kind, source, name, 0);
        }
    }

    /// <summary>One recorded call.</summary>
    /// <param name="Name">The Action API member.</param>
    /// <param name="Args">Its arguments before the source (a bool as text).</param>
    /// <param name="Source">The source it was asked with.</param>
    public sealed record Call(string Name, string[] Args, ActionSource Source);
}

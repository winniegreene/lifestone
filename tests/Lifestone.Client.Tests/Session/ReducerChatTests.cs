using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.State;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// Chat and tool replies (ST8, ST12): every chat source lands in the chat log with its kind; Lifestone tool replies are
/// recognised with the server mod's own ToolsWire, routed and kept out of the chat except their human line; ACE's
/// unknown-command answer reports the mod missing.
/// </summary>
public sealed class ReducerChatTests
{
    private const string StatusReply =
        "Lifestone: mod 0.1.0 (wire 1) on ACE 95ef426d at 127.0.0.1:9000\n#lifestone/1 {\"v\":1,\"id\":\"a1\",\"verb\":\"status\",\"ok\":true,\"data\":{\"wire\":1,\"mod\":\"0.1.0\"}}";

    [Fact]
    public void Every_chat_source_lands_in_the_log_with_its_kind()
    {
        var h = new ReducerHarness();
        var header = new GameEventHeader(ReducerHarness.PlayerGuid, 1, 0);
        h.Reducer.OnSpeech(h.Context, new SpeechMsg("Hello", "Guard", 0x80000001, 2, null));
        h.Reducer.OnSpeech(h.Context, new SpeechMsg("Hear ye", "Crier", 0x80000002, 2, 30f));
        h.Reducer.OnEmote(h.Context, new EmoteMsg(0x80000001, "Guard", "waves", false));
        h.Reducer.OnEmote(h.Context, new EmoteMsg(0x80000001, "Guard", "bows", true));
        h.Reducer.OnTell(h.Context, header, new TellEvent("psst", "Friend", 0x50000009, ReducerHarness.PlayerGuid, 3, 0));
        h.Reducer.OnServerMessage(h.Context, new ServerMessageMsg("Welcome to Asheron's Call", 0));
        h.Reducer.OnPopup(h.Context, header, new PopupEvent("A popup"));
        h.Reducer.OnWeenieError(h.Context, header, new WeenieErrorEvent(0x0402, null));
        h.Reducer.OnTransientString(h.Context, header, new TransientStringEvent("You are too fatigued"));

        Assert.Equal(
            [ChatKind.Speech, ChatKind.RangedSpeech, ChatKind.Emote, ChatKind.SoulEmote, ChatKind.Tell, ChatKind.System, ChatKind.Popup, ChatKind.WeenieError, ChatKind.Transient],
            Enumerable.Range(0, h.State.Chat.Count).Select(i => h.State.Chat[i].Kind));
        Assert.Equal(new ChatLine(0, ChatKind.Tell, 3, "Friend", 0x50000009, "psst"), h.State.Chat[4]);
        Assert.Equal(0x0402u, h.State.Chat[7].ChatType);
        Assert.Equal(9, h.Of<ChatReceived>().Count);
    }

    [Fact]
    public void A_new_world_entry_starts_the_shells_chat_empty_so_character_As_lines_do_not_show_for_B()
    {
        var h = new ReducerHarness();
        h.Phase(SessionPhase.CharacterSelect, SessionPhase.EnteringWorld);
        h.Reducer.OnSpeech(h.Context, new SpeechMsg("A talks", "Alpha", 0x80000001, 2, null));
        h.Reducer.OnServerMessage(h.Context, new ServerMessageMsg("A's system line", 0));
        h.Phase(SessionPhase.EnteringWorld, SessionPhase.InWorld);
        Assert.Equal(["Alpha says, \"A talks\"", "A's system line"], ShellLines(h));

        h.Phase(SessionPhase.InWorld, SessionPhase.LoggingOff);
        h.Reducer.OnServerMessage(h.Context, new ServerMessageMsg("Logging off...", 0));
        h.Phase(SessionPhase.LoggingOff, SessionPhase.CharacterSelect);
        h.Reducer.OnServerMessage(h.Context, new ServerMessageMsg("between", 0));
        h.Phase(SessionPhase.CharacterSelect, SessionPhase.EnteringWorld);
        Assert.Empty(ShellLines(h)); // B's shell opens with an empty chat
        h.Reducer.OnServerMessage(h.Context, new ServerMessageMsg("B's system line", 0));
        Assert.Equal(["B's system line"], ShellLines(h));
        Assert.True(h.State.Chat.Count >= 5); // the log itself keeps the history (the scenarios read it)
    }

    [Fact]
    public void A_session_option_change_is_the_reducers_override_whatever_order_readers_subscribe_in()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        var seen = new List<(QualityChange Change, bool Side)>();
        using var early = h.Bus.Subscribe<QualitiesChanged>((in QualitiesChanged c) => seen.Add((c.Change, h.State.Self.Qualities.SideBySideVitals))); // before any UI object exists
        var header = new GameEventHeader(ReducerHarness.PlayerGuid, 1, GameEventTypes.PlayerDescription);
        h.Reducer.OnPlayerDescription(h.Context, header, new PlayerDescriptionMsg { Options1 = 0x10, Options2 = 0x20 });
        var q = h.State.Self.Qualities;
        Assert.False(q.SideBySideVitals);

        h.Reducer.ApplyLocal(new RecordingNotice(8, PlayerQualities.SideBySideVitalsOption, "1:1")); // IActionApi.SetSessionOption
        Assert.True(q.SideBySideVitals);
        Assert.Equal(0x10u | PlayerQualities.SideBySideVitalsOption, q.CharacterOptions1);
        Assert.Equal(0x20u, q.CharacterOptions2);
        Assert.Contains((QualityChange.Options, true), seen); // the first reader already saw the player's value

        seen.Clear();
        h.Reducer.OnPlayerDescription(h.Context, header, new PlayerDescriptionMsg { Options1 = 0x10, Options2 = 0x20 }); // a later description keeps the change
        Assert.Equal([(QualityChange.Description, true)], seen);
        Assert.True(q.SideBySideVitals);

        h.Reducer.ApplyLocal(new RecordingNotice(8, PlayerQualities.SideBySideVitalsOption, "1:0"));
        Assert.False(q.SideBySideVitals);
        Assert.Throws<InvalidDataException>(() => h.Reducer.ApplyLocal(new RecordingNotice(8, 0, "1:1")));
    }

    private static List<string> ShellLines(ReducerHarness h) =>
        [.. Lifestone.Client.RetailUi.Chat.GameChat.EntryLines(h.State.Chat).Select(l => Lifestone.Client.RetailUi.Chat.ChatLineText.TryFormat(l, ReducerHarness.PlayerGuid, out _, out var text) ? text : "?")];

    [Fact]
    public void Tool_reply_lines_are_parsed_routed_and_hidden_from_chat()
    {
        var h = new ReducerHarness();
        var routed = new List<ToolReplyLine>();
        h.Reducer.ToolReplyRouted = routed.Add;
        h.Reducer.OnServerMessage(h.Context, new ServerMessageMsg(StatusReply, 0));

        var line = h.State.Chat[0];
        Assert.Equal(ChatKind.ToolReply, line.Kind);
        Assert.Equal("Lifestone: mod 0.1.0 (wire 1) on ACE 95ef426d at 127.0.0.1:9000", line.Text);
        Assert.DoesNotContain("#lifestone", line.Text, StringComparison.Ordinal);
        var reply = Assert.Single(h.Of<ToolReplyReceived>());
        Assert.Equal(new ToolReplyReceived("a1", "status", true, null, line.Text), reply);
        var route = Assert.Single(routed);
        Assert.Equal(("a1", "status", true), (route.Reply.Id, route.Reply.Verb, route.Reply.Ok));
        Assert.Equal(line.Text, route.ChatLine);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, route.Reply.Data!.Value.ValueKind);
        Assert.Equal(["ChatReceived", "ToolReplyReceived"], h.Names());
    }

    [Fact]
    public void A_silent_tool_request_s_reply_stays_out_of_the_chat()
    {
        var h = new ReducerHarness();
        h.Reducer.ApplyLocal(new RecordingNotice(SessionNotices.ToolRequestSilent, 0, "a1"));
        h.Reducer.OnServerMessage(h.Context, new ServerMessageMsg(StatusReply, 0));
        Assert.Equal(0, h.State.Chat.Count);
        Assert.Single(h.Of<ToolReplyReceived>());

        // The silence is used up by that reply.
        h.Reducer.OnServerMessage(h.Context, new ServerMessageMsg(StatusReply, 0));
        Assert.Equal(1, h.State.Chat.Count);
    }

    [Fact]
    public void A_failed_tool_reply_carries_its_error_code()
    {
        var h = new ReducerHarness();
        h.Reducer.OnServerMessage(
            h.Context,
            new ServerMessageMsg("Lifestone: no place called Nowhere\n#lifestone/1 {\"v\":1,\"id\":\"b2\",\"verb\":\"place\",\"ok\":false,\"error\":\"not-found\",\"message\":\"no place called Nowhere\",\"extra\":[1,2]}", 0));
        Assert.Equal(new ToolReplyReceived("b2", "place", false, "not-found", "Lifestone: no place called Nowhere"), Assert.Single(h.Of<ToolReplyReceived>()));
    }

    [Theory]
    [InlineData("Lifestone: x\n#lifestone/2 {\"v\":2}", "unsupported wire version: '#lifestone/2'")]
    [InlineData("Lifestone: x\n#lifestone/1 {\"v\":2,\"verb\":\"status\",\"ok\":true}", "wire version 2")]
    [InlineData("Lifestone: x\n#lifestone/1 {not json", "does not parse")]
    [InlineData("Lifestone: x\n#lifestone/1 {\"v\":1,\"ok\":true}", "no verb")]
    [InlineData("Lifestone: x\n#lifestone/1 {\"v\":1,\"verb\":\"xp\",\"ok\":false}", "no error code")]
    [InlineData("Lifestone: x\n#lifestone/1 {\"v\":1,\"verb\":\"xp\"}", "no error code")]
    [InlineData("Someone: x\n#lifestone/1 {\"v\":1,\"verb\":\"xp\",\"ok\":true}", "does not start with")]
    [InlineData("Lifestone: x\n#lifestone/1 {}\nmore", "more than two lines")]
    public void A_reply_that_does_not_parse_is_reported_and_shows_its_human_line(string text, string problem)
    {
        var h = new ReducerHarness();
        h.Reducer.OnServerMessage(h.Context, new ServerMessageMsg(text, 0));
        Assert.Empty(h.Of<ToolReplyReceived>());
        Assert.Equal(text[..text.IndexOf('\n', StringComparison.Ordinal)], h.State.Chat[0].Text);
        Assert.True(h.Log.AnyLineContains(problem), string.Join("\n", h.Log.Lines));
        Assert.False(h.Log.AnyLineContains("#lifestone/1 {"));
    }

    [Fact]
    public void Ordinary_system_chat_and_a_lone_tag_are_not_replies()
    {
        var h = new ReducerHarness();
        h.Reducer.OnServerMessage(h.Context, new ServerMessageMsg("#lifestone/1 {\"v\":1}", 0));
        h.Reducer.OnServerMessage(h.Context, new ServerMessageMsg("Lifestone: hello", 0));
        Assert.Empty(h.Of<ToolReplyReceived>());
        Assert.Equal(2, h.State.Chat.Count);
        Assert.All(Enumerable.Range(0, 2), i => Assert.Equal(ChatKind.System, h.State.Chat[i].Kind));
    }

    [Fact]
    public void Unknown_command_lifestone_reports_the_mod_missing()
    {
        var h = new ReducerHarness();
        h.Reducer.OnServerMessage(h.Context, new ServerMessageMsg("Unknown command: lifestone", 0x0F));
        var missing = Assert.Single(h.Of<ToolsUnavailable>());
        Assert.Equal(ToolAvailability.ModMissing, missing.Availability);
        Assert.Equal(ChatKind.System, h.State.Chat[0].Kind);
        Assert.Equal(0x0Fu, h.State.Chat[0].ChatType);
    }
}

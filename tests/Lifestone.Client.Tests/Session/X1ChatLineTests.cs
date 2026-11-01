using Lifestone.Client.RetailUi.Chat;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M3 4.2 x1: the text of a WeenieError (<c>HandleFailureEvent</c>, 11.6096 0x00572760), chat type 0, and the resist lines of ACE's <c>WorldObject_Magic.cs</c>.
/// </summary>
public sealed class X1ChatLineTests
{
    private static ReducerHarness H() => new();

    private static (int Type, string Text) Shown(ReducerHarness h, uint code, string? argument = null)
    {
        h.Reducer.OnWeenieError(h.Context, new GameEventHeader(ReducerHarness.PlayerGuid, 1, 0), new WeenieErrorEvent(code, argument));
        var line = h.State.Chat[h.State.Chat.Count - 1];
        Assert.True(ChatLineText.TryFormat(line, 1, out var type, out var text));
        return (type, text);
    }

    [Fact]
    public void A_fizzle_prints_its_text_in_the_magic_type()
    {
        Assert.Equal((7, "Your spell fizzled."), Shown(H(), 0x0402));
    }

    [Fact]
    public void The_mana_refusal_prints_its_text_in_the_spew_box_type()
    {
        Assert.Equal((26, "You don't have enough Mana to cast this spell."), Shown(H(), 0x0401));
        Assert.Equal((26, "You don't have all the components for this spell."), Shown(H(), 0x0400));
        Assert.Equal((26, "You're too busy!"), Shown(H(), 0x001D));
    }

    [Fact]
    public void A_code_with_a_string_puts_it_in_the_text()
    {
        var (_, text) = Shown(H(), 0x004E, "Tusker");
        Assert.Equal("You fail to affect Tusker because you cannot affect anyone!", text);
    }

    /// <summary>The line is reduced, and whether the chat window would show it (false: nothing on screen).</summary>
    private static bool OnScreen(ReducerHarness h, uint code)
    {
        h.Reducer.OnWeenieError(h.Context, new GameEventHeader(ReducerHarness.PlayerGuid, 1, 0), new WeenieErrorEvent(code, null));
        return ChatLineText.TryFormat(h.State.Chat[h.State.Chat.Count - 1], 1, out _, out _);
    }

    [Fact]
    public void A_code_the_client_prints_with_no_text_here_prints_its_number_and_is_logged_once_as_a_todo()
    {
        // M5 7 D124 (S1b): HandleFailureEvent (11.6096 0x00572760) has a case for 0x0049 (ACE CantJumpLoadedDown) whose text the table lacks.
        var h = H();
        Assert.Equal((0, "WeenieError 0x0049"), Shown(h, 0x0049));
        Assert.Equal((0, "WeenieError 0x0049"), Shown(h, 0x0049));

        Assert.Equal(1, h.Log.Entries.Count(entry => entry.Message.Contains("TODO(m3-weenie-error-text)", StringComparison.Ordinal)));
        Assert.False(WeenieErrorText.Knows(0x0049));
        Assert.True(WeenieErrorText.IsRetailTextUnknown(0x0049));
        Assert.True(WeenieErrorText.Knows(0x0402));
    }

    [Fact]
    public void A_code_the_client_has_no_case_for_shows_nothing_on_screen_and_is_logged_once()
    {
        // M5 7 D124 (S1b): a code with no case of HandleFailureEvent goes to its shared exit (0x0057621A), which prints nothing.
        var h = H();
        Assert.False(OnScreen(h, 0x7ABC));
        Assert.False(OnScreen(h, 0x7ABC));
        var line = h.State.Chat[h.State.Chat.Count - 1];
        Assert.Equal((ChatKind.WeenieError, 0x7ABCu), (line.Kind, line.ChatType)); // still a chat event for the trackers and the log
        Assert.Equal(1, h.Log.Entries.Count(entry => entry.Message.Contains("prints no line for", StringComparison.Ordinal)));
        Assert.Equal(0, h.Log.Entries.Count(entry => entry.Message.Contains("TODO(m3-weenie-error-text)", StringComparison.Ordinal)));
        foreach (var code in new uint[] { 0x003B, 0x003C, 0x0426, 0x04DC, 0x051D })
        {
            Assert.False(WeenieErrorText.RetailShows(code), $"0x{code:X4}");
        }

        Assert.True(WeenieErrorText.RetailShows(0x0402));
        Assert.True(WeenieErrorText.RetailShows(0x051B));
    }

    [Fact]
    public void The_code_stays_in_the_line_for_the_trackers()
    {
        var h = H();
        Shown(h, 0x0402);
        var line = h.State.Chat[h.State.Chat.Count - 1];
        Assert.Equal((ChatKind.WeenieError, 0x0402u, string.Empty), (line.Kind, line.ChatType, line.Text));
    }

    [Fact]
    public void The_channel_notices_are_retails_join_and_leave_lines_not_error_lines()
    {
        // M5 7 D124: ACE's login sends 0x051D, then 0x051B for General, Trade and LFG (Player_Networking.cs:85-88, 182 @95ef426d); the logoff 0x051C (209).
        var h = H();
        Assert.Equal((0, "You have entered the General channel."), Shown(h, 0x051B, "General"));
        Assert.Equal(ChatKind.ChannelNotice, h.State.Chat[h.State.Chat.Count - 1].Kind);
        Assert.Equal((0, "You have left the Trade channel."), Shown(h, 0x051C, "Trade"));
        Assert.Equal(ChatKind.ChannelNotice, h.State.Chat[h.State.Chat.Count - 1].Kind);
        Assert.False(OnScreen(h, 0x051D)); // M5 7 D124 (S1b): retail's HandleFailureEvent has no case for "Turbine Chat is enabled.": nothing on screen
        var line = h.State.Chat[h.State.Chat.Count - 1];
        Assert.Equal((ChatKind.ChannelNotice, 0x051Du), (line.Kind, line.ChatType)); // the code stays in the line
        Assert.False(WeenieErrorText.IsChannelNotice(0x001D));
        Assert.Equal(ChatKind.WeenieError, LastKindAfter(h, 0x001D)); // a refusal stays a failure line
    }

    [Fact]
    public void TargetNotAcquired_shows_nothing_on_screen_as_retail_and_the_log_keeps_its_name()
    {
        // M5 7 D124 (S1b): neither ACE nor the 11.6096 client holds a text for 0x042C, and HandleFailureEvent (0x00572760) has no case for it.
        var h = H();
        Assert.False(OnScreen(h, 0x042C));
        Assert.Equal("TargetNotAcquired", WeenieErrorText.NameOf(0x042C));
        Assert.False(WeenieErrorText.TryFormat(0x042C, null, out _, out var logged));
        Assert.Equal("WeenieError 0x042C (TargetNotAcquired)", logged); // the log's text
        Assert.Contains(h.Log.Entries, entry => entry.Message.Contains("prints no line for", StringComparison.Ordinal) && entry.Message.Contains("TargetNotAcquired", StringComparison.Ordinal));
    }

    private static ChatKind LastKindAfter(ReducerHarness h, uint code)
    {
        Shown(h, code);
        return h.State.Chat[h.State.Chat.Count - 1].Kind;
    }

    [Fact]
    public void Chat_type_0_is_a_valid_system_type_and_stays_0()
    {
        Assert.True(ChatLineText.TryFormat(new ChatLine(0, ChatKind.System, 0, null, 0, "Tester has been killed."), 1, out var type, out _));
        Assert.Equal(0, type);
        Assert.True(ChatLineText.TryFormat(new ChatLine(0, ChatKind.System, 33, null, 0, "x"), 1, out type, out _));
        Assert.Equal(33, type);
        Assert.True(ChatLineText.TryFormat(new ChatLine(0, ChatKind.System, 34, null, 0, "x"), 1, out type, out _));
        Assert.Equal(ChatTextTypes.System, type);
    }

    [Fact]
    public void A_speech_or_tell_with_no_type_keeps_its_own_default()
    {
        Assert.True(ChatLineText.TryFormat(new ChatLine(0, ChatKind.Speech, 0, "Bob", 7, "hi"), 1, out var type, out _));
        Assert.Equal(ChatTextTypes.Speech, type);
        Assert.True(ChatLineText.TryFormat(new ChatLine(0, ChatKind.Tell, 0, "Bob", 7, "hi"), 1, out type, out _));
        Assert.Equal(ChatTextTypes.Tell, type);
    }

    [Fact]
    public void The_kill_lines_reach_the_chat_in_type_0()
    {
        var h = H();
        h.Reducer.OnKillerNotification(h.Context, new GameEventHeader(ReducerHarness.PlayerGuid, 1, 0), new KillerNotificationEvent("You flatten Tusker's body!"));
        var line = h.State.Chat[h.State.Chat.Count - 1];
        Assert.True(ChatLineText.TryFormat(line, 1, out var type, out _));
        Assert.Equal(0, type);
    }
}

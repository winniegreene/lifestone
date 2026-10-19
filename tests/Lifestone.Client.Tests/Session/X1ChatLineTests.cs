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

    [Fact]
    public void An_unknown_code_prints_its_number_and_is_logged_once()
    {
        var h = H();
        Assert.Equal((0, "WeenieError 0x7ABC"), Shown(h, 0x7ABC));
        Assert.Equal((0, "WeenieError 0x7ABC"), Shown(h, 0x7ABC));

        Assert.Equal(1, h.Log.Entries.Count(entry => entry.Message.Contains("TODO(m3-weenie-error-text)", StringComparison.Ordinal)));
        Assert.False(WeenieErrorText.Knows(0x7ABC));
        Assert.True(WeenieErrorText.Knows(0x0402));
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

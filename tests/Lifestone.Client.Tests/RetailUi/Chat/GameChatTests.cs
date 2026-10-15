using Lifestone.Client.RetailUi.Chat;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.RetailUi.Chat;

/// <summary>
/// The spew box and the chat windows of the in-game shell (docs/retail-ui.md section 21.2): type 26 to the spew box only, identical text replaced and the rows capped,
/// the other types to the chat windows by their masks, the mod's machine channel filtered, the entry sending Talk, and the talk-focus menu answering NotInM2.
/// </summary>
public sealed class GameChatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static GamePlayScreen EnterGame(LoginRig rig)
    {
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        return Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
    }

    private static UiStringRef Lit(string text) => UiStringRef.FromLiteral(text);

    // ------------------------------------------------------------------ no dats

    [Fact]
    public void The_default_masks_are_the_ones_read_from_the_exe()
    {
        Assert.Equal(0xFBFFFFFFUL, ChatTextTypes.DefaultMask(1));
        Assert.Equal(0xFBFFFFFFUL, ChatTextTypes.DefaultMask(8));
        Assert.Equal(0x101CUL, ChatTextTypes.DefaultMask(2));
        Assert.Equal(0x40C00UL, ChatTextTypes.DefaultMask(3));
        Assert.Equal(0x80000UL, ChatTextTypes.DefaultMask(4));
        Assert.Equal(0x78000000UL, ChatTextTypes.DefaultMask(5));
        Assert.Equal(ulong.MaxValue & ~(1UL << 26), ChatTextTypes.DefaultMask(6));
        Assert.False(ChatTextTypes.IsActive(ChatTextTypes.DefaultMask(1), ChatTextTypes.SpewBox));
        Assert.True(ChatTextTypes.IsActive(ChatTextTypes.DefaultMask(1), ChatTextTypes.System));
        Assert.True(ChatTextTypes.IsActive(ChatTextTypes.DefaultMask(2), ChatTextTypes.Speech));
        Assert.False(ChatTextTypes.IsActive(ChatTextTypes.DefaultMask(2), ChatTextTypes.System));
    }

    [Fact]
    public void The_colour_table_pairs_types_and_colours_as_the_exe_does()
    {
        Assert.Equal(new UiRgbaColor(1f, 1f, 1f, 1f), ChatTextTypes.ColorOf(2));
        Assert.Equal(ChatTextTypes.ColorOf(3), ChatTextTypes.ColorOf(31));
        Assert.Equal(new UiRgbaColor(1f, 0f, 0f, 1f), ChatTextTypes.ColorOf(26));
        Assert.Equal(new UiRgbaColor(0.5f, 1f, 0.498f, 1f), ChatTextTypes.ColorOf(0)); // the green default
        Assert.Equal(new UiRgbaColor(0.5f, 1f, 0.498f, 1f), ChatTextTypes.ColorOf(24));
    }

    [Fact]
    public void The_mods_machine_channel_is_never_shown()
    {
        var machine = new ChatLine(0, ChatKind.System, 5, null, 0, "#lifestone/1 {\"v\":1}");
        Assert.False(ChatLineText.TryFormat(machine, 1, out _, out _));
        Assert.False(ChatLineText.TryFormat(machine with { Text = "  #lifestone/1 x" }, 1, out _, out _));
        Assert.True(ChatLineText.TryFormat(machine with { Text = "Lifestone: teleporting" }, 1, out var type, out var text));
        Assert.Equal((ChatTextTypes.System, "Lifestone: teleporting"), (type, text));
    }

    [Fact]
    public void Speech_tells_and_emotes_are_formatted_and_typed()
    {
        Assert.True(ChatLineText.TryFormat(new ChatLine(0, ChatKind.Speech, 2, "Bob", 7, "hi [there]"), 1, out var t, out var s));
        Assert.Equal((2, "Bob says, \"hi [there]\""), (t, s));
        ChatLineText.TryFormat(new ChatLine(0, ChatKind.Speech, 2, "Me", 1, "hi"), 1, out t, out s);
        Assert.Equal("You say, \"hi\"", s);
        ChatLineText.TryFormat(new ChatLine(0, ChatKind.Tell, 3, "Ann", 9, "psst"), 1, out t, out s);
        Assert.Equal((3, "Ann tells you, \"psst\""), (t, s));
        ChatLineText.TryFormat(new ChatLine(0, ChatKind.Emote, 0, "Cy", 4, "waves."), 1, out t, out s);
        Assert.Equal((12, "Cy waves."), (t, s));
        ChatLineText.TryFormat(new ChatLine(0, ChatKind.System, 99, null, 0, "odd"), 1, out t, out _);
        Assert.Equal(ChatTextTypes.System, t); // a type outside 0 to 33 is system text
    }

    // ------------------------------------------------------------------ with dats

    [RequiresLocalDatsFact]
    public void The_spew_box_is_the_class_0x10000016_window_with_its_list_and_the_router_sends_it_type_26_only()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var chat = Assert.IsType<GameChat>(screen.Chat);
        var spew = Assert.IsType<GameSpewBox>(chat.Spew);
        Assert.Equal(0x10000016u, screen.SpewBox!.ClassId);
        Assert.IsType<UiListBox>(screen.SpewBox.GetChildRecursive(GameSpewBox.ListBoxId));
        Assert.Same(spew, rig.Screens.Notices.SpewBox);
        Assert.Same(chat.ChatSink, rig.Screens.Notices.Chat);
        Assert.DoesNotContain("spew box", screen.Placeholders);
        Assert.DoesNotContain("chat", screen.Placeholders);

        rig.Screens.Notices.DisplayStringInfo(26, Lit("In Portal Space - Please Wait..."));
        rig.Screens.Notices.DisplayStringInfo(5, Lit("a system line"));
        rig.Frame();
        Assert.Equal(["In Portal Space - Please Wait..."], spew.Lines);
        Assert.Equal(1, spew.Accepted);
        Assert.All(chat.Windows, w => Assert.DoesNotContain("Portal Space", w.LogText, StringComparison.Ordinal));
        Assert.Contains("a system line", chat.Main!.LogText, StringComparison.Ordinal);
    }

    [RequiresLocalDatsFact]
    public void An_identical_notice_replaces_the_top_row_and_the_rows_are_capped_newest_first()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var spew = EnterGame(rig).Chat!.Spew!;
        var notices = rig.Screens.Notices;
        Assert.True(spew.MaxRows >= 1);

        notices.DisplayStringInfo(26, Lit("same"));
        rig.Frame();
        notices.DisplayStringInfo(26, Lit("  same \n"));
        notices.DisplayStringInfo(26, Lit("same"));
        rig.Frame();
        Assert.Equal(["same"], spew.Lines); // the same text, trimmed, replaces rather than adds

        for (var i = 0; i < spew.MaxRows + 3; i++)
        {
            notices.DisplayStringInfo(26, Lit("line " + i));
        }

        rig.Frame();
        Assert.Equal(spew.MaxRows, spew.Lines.Count);
        Assert.Equal("line " + (spew.MaxRows + 2), spew.Lines[0]);
    }

    [RequiresLocalDatsFact]
    public void Each_chat_window_shows_the_types_its_mask_has_and_a_players_brackets_stay()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var chat = EnterGame(rig).Chat!;
        Assert.Equal(5, chat.Windows.Count);
        Assert.Contains(chat.Main!.WindowId, new uint[] { 1, 8 });
        Assert.Equal(ChatTextTypes.DefaultMask(chat.Main.WindowId), chat.Main.TextTypeMask);

        foreach (var type in new[] { 2, 3, 5, 10, 12, 19, 27 })
        {
            foreach (var window in chat.Windows)
            {
                window.DisplayText(type, $"text of type {type} [x]");
            }
        }

        foreach (var window in chat.Windows)
        {
            foreach (var type in new[] { 2, 3, 5, 10, 12, 19, 27 })
            {
                Assert.Equal(ChatTextTypes.IsActive(window.TextTypeMask, type), window.LogText.Contains($"text of type {type} [x]", StringComparison.Ordinal));
            }
        }

        // Retail's defaults put speech in floating chat 1 and not the fellowship's, so at least two windows differ.
        Assert.Contains(chat.Windows, w => w.WindowId == 2 && w.LogText.Contains("type 2 ", StringComparison.Ordinal));
        Assert.Contains(chat.Windows, w => w.WindowId == 4 && w.LogText.Contains("type 19 ", StringComparison.Ordinal) && !w.LogText.Contains("type 2 ", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void The_session_chat_lines_reach_the_windows_and_the_machine_channel_does_not()
    {
        IInboundMessageHandler? handler = null;
        using var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true);
        var chat = EnterGame(rig).Chat!;
        var context = new InboundContext(0, MessageQueue.SmartBox, 0);
        handler!.OnServerMessage(context, new ServerMessageMsg("A server line.", 5));
        handler.OnServerMessage(context, new ServerMessageMsg("#lifestone/1 {\"v\":1,\"verb\":\"status\"}", 5));
        handler.OnServerMessage(context, new ServerMessageMsg("Cannot log off while in mid-air.", 26));
        rig.Frame();
        Assert.Contains("A server line.", chat.Main!.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("#lifestone", chat.Main.LogText, StringComparison.Ordinal);
        Assert.Contains("Cannot log off while in mid-air.", chat.Spew!.Lines);
        Assert.DoesNotContain("mid-air", chat.Main.LogText, StringComparison.Ordinal);
    }

    [RequiresLocalDatsFact]
    public void The_entry_sends_what_is_typed_as_Talk_by_Enter_or_Send_and_clears()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var chat = EnterGame(rig).Chat!;
        var main = chat.Main!;
        Assert.False(main.ProcessCommand()); // an empty entry sends nothing
        Assert.Equal(0, chat.TalkSent);

        main.TypeIntoEntry("hello there");
        Assert.True(main.ProcessCommand());
        Assert.Equal(1, chat.TalkSent);
        var talk = Assert.IsAssignableFrom<ActionHandle>(chat.LastTalk);
        Assert.Equal(ActionKind.Talk, talk.Kind);
        Assert.Equal(ActionSource.Ui, talk.Source);
        Assert.Equal(string.Empty, main.Entry!.Text);
        Assert.Equal(["hello there"], main.History);

        // The Send button does the same.
        main.TypeIntoEntry("@help");
        var send = Assert.IsAssignableFrom<UiElement>(main.Window.GetChildRecursive(GameChatWindow.SendButtonId));
        send.BroadcastElementMessage(UiMessageIds.ButtonClick);
        Assert.Equal(2, chat.TalkSent);
        Assert.Equal(["hello there", "@help"], main.History);

        // Offline, the action layer refuses the Talk; the chat says so.
        rig.Frame();
        Assert.Contains("was not sent", main.LogText, StringComparison.Ordinal);
    }

    [RequiresLocalDatsFact]
    public void A_slash_command_is_a_named_todo_and_is_not_sent()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var chat = EnterGame(rig).Chat!;
        chat.Main!.TypeIntoEntry("/tell bob hi");
        chat.Main.ProcessCommand();
        Assert.Equal(0, chat.TalkSent);
        Assert.Equal(1, chat.CommandsDeclined);
        Assert.Contains("TODO(g4-chat-commands)", chat.Main.LogText, StringComparison.Ordinal);
        Assert.Contains(rig.Log.Lines, l => l.Contains("TODO(g4-chat-commands)", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("bob", StringComparison.Ordinal)); // what was typed is not written to the log
    }

    [RequiresLocalDatsFact]
    public void The_talk_focus_menu_lists_retails_rows_and_any_focus_but_All_answers_NotInM2()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var chat = EnterGame(rig).Chat!;
        var menu = Assert.IsType<UiMenu>(chat.FocusMenu);
        Assert.Equal(14, menu.ItemCount); // the squelch row and the 13 focuses
        Assert.Equal(TalkFocus.All, chat.Focus);
        var all = menu.GetItem(menu.SelectedIndex)!;
        Assert.True(all.InqProperty(GameChat.TalkFocusProperty, out var value) && value is UiEnumProperty { Value: 1 });
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("TODO(g4-chat-strings)", StringComparison.Ordinal));

        // Choose "Monarch" (row 1) as a player would: the menu broadcasts, the chat answers NotInM2 and goes back to All.
        menu.SetSelectedItem(menu.GetItem(1), true);
        Assert.Equal(1, chat.FocusRefusals);
        Assert.Equal(TalkFocus.All, chat.Focus);
        Assert.Equal(menu.GetItem(menu.SelectedIndex), all);
        Assert.Contains(rig.Log.Lines, l => l.Contains("is not in Lifestone M2", StringComparison.Ordinal) && l.Contains("Monarch", StringComparison.Ordinal));

        // Plain speech still goes.
        chat.Main!.TypeIntoEntry("still local");
        chat.Main.ProcessCommand();
        Assert.Equal(1, chat.TalkSent);
    }

    [RequiresLocalDatsFact]
    public void A_long_log_is_cut_back_at_a_line_break()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var main = EnterGame(rig).Chat!.Main!;
        for (var i = 0; i < 400; i++)
        {
            main.DisplayText(ChatTextTypes.System, $"line {i:000} " + new string('x', 30));
        }

        Assert.InRange(main.Log!.Glyphs.Count, 1, GameChatWindow.LogLimit);
        Assert.StartsWith("line ", main.LogText, StringComparison.Ordinal);
        Assert.EndsWith("line 399 " + new string('x', 30), main.LogText, StringComparison.Ordinal);
    }

    [RequiresLocalDatsFact]
    public void The_shell_unregisters_its_windows_when_it_goes()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        EnterGame(rig);
        Assert.NotNull(rig.Screens.Notices.SpewBox);
        rig.Flow.QueueUIMode(UiModes.CharacterManagement);
        rig.Frame();
        rig.Frame();
        Assert.Null(rig.Screens.Notices.SpewBox);
        Assert.Null(rig.Screens.Notices.Chat);
    }
}

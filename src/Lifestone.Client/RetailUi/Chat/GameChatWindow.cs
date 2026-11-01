using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Chat;

/// <summary>
/// One chat window (<c>ChatInterface</c>, 11.6096 0x004F4A30 for <c>PostInit</c>): the main chat 0x10000601 or a floating chat. It owns the log (the text element
/// 0x10000011 with its scrollbar), the entry (the text element 0x10000016), the "new text" indicator (the button 0x1000048C) and the Send button (0x10000019),
/// shows the lines whose text type its mask has, and hands a typed line to its owner.
/// </summary>
/// <remarks>
/// <para>
/// <b>Children.</b> The 11.4186 decompile of <c>PostInit</c> names them by field and type-confuses three of them; the bytes of 11.6096 settle it: element
/// 0x10000011 is the log, 0x1000048C the indicator, 0x10000016 the entry (docs/research/retail-addresses-m2.md section 1.5).
/// </para>
/// <para>
/// <b>Lines.</b> <c>RecvNotice_DisplayFinalStringInfo</c> (11.4186 0x004F4640, 11.6096 0x004F52A0): a window shows a line when its type mask has the line's
/// text type; it adds a new line before every line but the first, then the line in the colour of its text type, truncates the log to 7,500 glyphs at a
/// line break once it passes 10,000 (<c>TruncateChatLog</c>, 11.4186 0x004F2F50), and keeps the view at the end when it was there, else lights the indicator.
/// The destination override and the prefix of retail's notice are not used (no source of either in M2).
/// </para>
/// <para>
/// <b>Entry.</b> Enter (<c>HandleEnterKey</c>, 11.4186 0x004F52D0) or a click on Send (<c>ListenToElementMessage</c>, 11.4186 0x004F5280-ish: message 1 from
/// 0x10000019) runs <c>ProcessCommand</c> (11.4186 0x004F5100): a line of text goes to the owner, joins a history of 100 lines and clears the entry; an empty one does
/// nothing.
/// </para>
/// <para>
/// <b>Not built.</b> The idle/active opacity fade (defaults 0.5 and 1.0, read from the exe; the runtime has no element opacity: <c>TODO(g4-chat-opacity)</c>), the
/// history keys, the reply keys, the font setting and the Chat options tab that would change a mask (<c>TODO(g6-chat-options)</c>). The mask itself is kept: it starts as retail's default for the window
/// (<see cref="ChatTextTypes.DefaultMask"/>) and the shell saves it with the window layout (<c>LS-CHATMASK</c> lines, <see cref="ScreenLayoutFile"/>) and restores it at the next world entry (M5 6 u1, D33).
/// </para>
/// </remarks>
public sealed class GameChatWindow : IUiNoticeSink, IUiElementListener, IDisposable
{
    /// <summary>The log inside a chat window.</summary>
    public const uint LogId = 0x10000011;

    /// <summary>The entry inside a chat window.</summary>
    public const uint EntryId = 0x10000016;

    /// <summary>The "new text" indicator button.</summary>
    public const uint NewTextIndicatorId = 0x1000048C;

    /// <summary>The Send button.</summary>
    public const uint SendButtonId = 0x10000019;

    /// <summary>Master property 0x1000007E on a window: which chat window it is (1 and 8 the main chat, 2 to 5 the floating chats).</summary>
    public const uint WindowIdProperty = 0x1000007E;

    /// <summary>The log is cut back when it passes this many glyphs (0x2710).</summary>
    public const int LogLimit = 10000;

    /// <summary>The log is cut back to about this many glyphs (0x1D4C).</summary>
    public const int LogKeep = 7500;

    /// <summary>Lines kept in the entry's history.</summary>
    public const int HistoryLimit = 100;

    /// <summary>The idle opacity retail starts a chat window with (the constructor's 0.5, read from the exe).</summary>
    public const float DefaultOpacity = 0.5f;

    /// <summary>The opacity while the entry has the focus (1.0).</summary>
    public const float ActiveOpacity = 1.0f;

    private readonly UiElementManager _manager;
    private readonly Action<GameChatWindow, string> _submit;
    private readonly UiElement? _indicator;
    private readonly List<string> _history = [];

    /// <summary>A chat window over <paramref name="window"/>; <paramref name="submit"/> gets each line the player enters.</summary>
    public GameChatWindow(UiElementManager manager, UiElement window, Action<GameChatWindow, string> submit)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(submit);
        _manager = manager;
        _submit = submit;
        Window = window;
        WindowId = window.InqProperty(WindowIdProperty, out var id) && id is UiEnumProperty e ? e.Value : 0;
        TextTypeMask = ChatTextTypes.DefaultMask(WindowId);
        Log = window.GetChildRecursive(LogId) as UiTextElement;
        Entry = window.GetChildRecursive(EntryId) as UiTextElement;
        _indicator = window.GetChildRecursive(NewTextIndicatorId);
        if (Log is null || Entry is null)
        {
            manager.LogOnce($"chat:{window.ElementId:X8}", LogSeverity.Error, $"ChatInterface: window 0x{window.ElementId:X8} has no log 0x{LogId:X8} or entry 0x{EntryId:X8} text element; it shows nothing");
            return;
        }

        // BuildChatColorLookupTable: the log's colour list, one entry per text type.
        var colors = new List<UiProperty>(ChatTextTypes.Count);
        for (var type = 0; type < ChatTextTypes.Count; type++)
        {
            colors.Add(new UiColorProperty(0, ChatTextTypes.ColorOf(type).ToColor32()));
        }

        Log.SetProperty(new UiArrayProperty(UiPropertyIds.TextFontColors, colors));
        _indicator?.SetVisible(false); // TODO(g4-chat-indicator): retail's initial state of the indicator is not read; hidden until text arrives out of view
        Entry.AcceptInput += OnAccept;
        window.RegisterForElementMessages(this);
    }

    /// <summary>The window.</summary>
    public UiElement Window { get; }

    /// <summary>Which chat window this is (<see cref="WindowIdProperty"/>).</summary>
    public uint WindowId { get; }

    /// <summary>The text types the window shows (<c>m_llTextTypeFilter</c>); the window layout restores it at the world entry (<see cref="ScreenLayoutFile"/>).</summary>
    public ulong TextTypeMask { get; set; }

    /// <summary>The log text element.</summary>
    public UiTextElement? Log { get; }

    /// <summary>The entry text element.</summary>
    public UiTextElement? Entry { get; }

    /// <summary>The lines entered so far, oldest first (at most <see cref="HistoryLimit"/>).</summary>
    public IReadOnlyList<string> History => _history;

    /// <summary>The log's text.</summary>
    public string LogText => Log?.Text ?? string.Empty;

    /// <summary>True while the "new text" indicator shows.</summary>
    public bool IndicatorShown => _indicator?.Visible ?? false;

    /// <inheritdoc/>
    public void DisplayStringInfo(int textType, in UiStringRef text)
    {
        if (Log is null || !ChatTextTypes.IsActive(TextTypeMask, textType))
        {
            return;
        }

        var notice = text;
        Append(textType, () => Log.AppendStringInfoWithFont(notice, 0, textType));
    }

    /// <summary>Shows <paramref name="text"/> as a line of <paramref name="textType"/> if the mask has it; the text is not parsed (a player's brackets stay). True when shown.</summary>
    public bool DisplayText(int textType, string text)
    {
        if (Log is null || !ChatTextTypes.IsActive(TextTypeMask, textType))
        {
            return false;
        }

        Append(textType, () => Log.AppendTextWithFont(text, 0, textType));
        return true;
    }

    /// <summary><c>ChatInterface::ProcessCommand</c>: sends the entry's text to the owner and clears it. True when there was text.</summary>
    public bool ProcessCommand()
    {
        if (Entry is null)
        {
            return false;
        }

        var text = Entry.Text;
        if (text.Length == 0)
        {
            return false;
        }

        _history.Add(text);
        while (_history.Count > HistoryLimit)
        {
            _history.RemoveAt(0);
        }

        Entry.ClearAllText();
        _submit(this, text);
        return true;
    }

    /// <summary>Types <paramref name="text"/> into the entry as a player would (tests, the checks).</summary>
    public void TypeIntoEntry(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Entry?.AppendText(text);
    }

    /// <inheritdoc/>
    public UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.ButtonClick)
        {
            if (message.ElementId == SendButtonId && IsMine(message.Element))
            {
                ProcessCommand();
            }
            else if (message.ElementId == NewTextIndicatorId && IsMine(message.Element))
            {
                Log?.ScrollToEnd();
                _indicator?.SetVisible(false);
            }
        }

        return UiListenResult.Default;
    }

    /// <summary>Stops listening.</summary>
    public void Dispose()
    {
        if (Entry is not null)
        {
            Entry.AcceptInput -= OnAccept;
        }

        Window.UnregisterForElementMessages(this);
    }

    private bool IsMine(UiElement element)
    {
        for (var e = element; e is not null; e = e.Parent)
        {
            if (ReferenceEquals(e, Window))
            {
                return true;
            }
        }

        return false;
    }

    private void OnAccept() => ProcessCommand();

    private void Append(int textType, Action addLine)
    {
        var log = Log!;
        if (log.Glyphs.Count > 0)
        {
            log.AppendTextWithFont("\n", 0, textType);
        }

        var atEnd = log.IsAtVerticalEnd;
        addLine();
        if (log.Glyphs.Count > LogLimit)
        {
            TruncateChatLog(LogKeep);
        }

        if (atEnd)
        {
            log.ScrollToEnd();
            _indicator?.SetVisible(false);
        }
        else
        {
            _indicator?.SetVisible(true);
        }
    }

    // ChatInterface::TruncateChatLog (11.4186 0x004F2F50): drops the oldest glyphs so about newLength remain, cutting after a line break that lies within a tenth of
    // the text past the cut (else exactly at the cut).
    private void TruncateChatLog(int newLength)
    {
        var log = Log!;
        var text = log.Text;
        if (text.Length <= newLength)
        {
            return;
        }

        var cut = text.Length - newLength;
        var line = text.IndexOf('\n', cut);
        if (line >= 0 && line - cut < text.Length / 10)
        {
            cut = line + 1;
        }

        log.DeleteSection(0, cut);
    }
}

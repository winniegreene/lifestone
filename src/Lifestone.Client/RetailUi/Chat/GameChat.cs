using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Chat;

/// <summary>The talk focus of the main chat (<c>gmCCommunicationSystem::SetTalkFocus</c>, 11.6096 0x00589E70): who the entry's lines go to.</summary>
public enum TalkFocus : uint
{
    /// <summary>Everyone in earshot (local speech, <c>Talk</c>): the only focus M2 honours.</summary>
    All = 1,

    /// <summary>The selected creature (a tell).</summary>
    Selected = 2,

    /// <summary>The fellowship.</summary>
    Fellows = 3,

    /// <summary>The patron.</summary>
    Patron = 4,

    /// <summary>The monarch.</summary>
    Monarch = 5,

    /// <summary>The vassals.</summary>
    Vassals = 6,

    /// <summary>The allegiance (also "wants to be in allegiance chat").</summary>
    Allegiance = 7,

    /// <summary>The General channel.</summary>
    General = 8,

    /// <summary>The Trade channel.</summary>
    Trade = 9,

    /// <summary>The LFG channel.</summary>
    Lfg = 10,

    /// <summary>The Roleplay channel.</summary>
    Roleplay = 11,

    /// <summary>The Society channel.</summary>
    Society = 12,

    /// <summary>The Olthoi channel.</summary>
    Olthoi = 13,
}

/// <summary>
/// The in-game chat system of the shell (docs/retail-ui.md section 21.2): the spew box, the main chat and the four floating chats, the talk-focus menu, and the
/// session's chat lines. It is registered on the router the notices go through (<see cref="UiNoticeRouter.SpewBox"/>, <see cref="UiNoticeRouter.Chat"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Lines.</b> Every <see cref="ChatReceived"/> line (and those already in <see cref="GameState.Chat"/> when the shell is made) is formatted
/// (<see cref="ChatLineText"/>) and shown by each window whose mask has its type; type 26 goes to the spew box; the mod's <c>#lifestone/1</c> lines are never shown.
/// </para>
/// <para>
/// <b>Entry.</b> <c>ClientCommunicationSystem::OnChatCommand</c> (11.4186 0x00581320, 11.6096 0x005821A0) parses the line; this slice sends plain text and
/// <c>@</c> server commands as <see cref="IActionApi.Talk"/> (<c>DoSay</c>, 11.6096 0x005820C0, ends in <c>CM_Communication::Event_Talk</c>, 11.6096 0x006A53E0) and
/// answers <c>/</c> commands with a labelled line (<c>TODO(g4-chat-commands)</c>). A line the action layer refuses (offline, not in the world) is reported in the chat.
/// </para>
/// <para>
/// <b>Talk focus.</b> Retail has no channel tabs: the main chat's popup menu 0x10000014 (<c>gmMainChatUI::InitTalkFocusMenu</c>, 11.4186 0x004CDC50, 11.6096
/// 0x004CE8C0) holds one row per focus, each with its value in property 0x1000000B. M2 honours <see cref="TalkFocus.All"/> only; choosing another answers
/// <see cref="NotInM2"/> and keeps All (<c>TODO(g4-chat-focus)</c>; the channels and tells need the gameplay milestone).
/// </para>
/// </remarks>
public sealed class GameChat : IDisposable
{
    /// <summary>The talk-focus popup menu in the main chat.</summary>
    public const uint TalkFocusMenuId = 0x10000014;

    /// <summary>Master property 0x1000000B on a menu row: its <see cref="TalkFocus"/>.</summary>
    public const uint TalkFocusProperty = 0x1000000B;

    private const uint StringTableEnum = 0x10000001;

    private readonly UiLoginContext _context;
    private readonly List<GameChatWindow> _windows = [];
    private readonly FanOut _sink;
    private readonly IDisposable _subscription;
    private readonly List<ActionHandle> _sending = [];
    private readonly UiMenu? _menu;
    private readonly UiElement? _allRow;
    private bool _disposed;

    /// <summary>Makes the chat over the shell's windows (any may be null when the layout lacks it) and registers it on <see cref="UiLoginContext.Notices"/>.</summary>
    public GameChat(UiLoginContext context, UiElement? spewWindow, UiElement? mainChat, IReadOnlyList<UiElement> floatingChats)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        if (spewWindow is not null)
        {
            Spew = new GameSpewBox(context.Manager, spewWindow);
        }

        if (mainChat is not null)
        {
            Main = Add(mainChat);
        }

        foreach (var window in floatingChats)
        {
            Add(window);
        }

        _sink = new FanOut(_windows);
        if (context.Notices is { } router)
        {
            router.SpewBox = Spew;
            router.Chat = _sink;
        }

        if (mainChat?.GetChildRecursive(TalkFocusMenuId) is UiMenu menu)
        {
            _menu = menu;
            _allRow = InitTalkFocusMenu(menu);
            menu.RegisterForElementMessages(new MenuListener(this));
        }

        foreach (var line in EntryLines(context.Session.State.Chat))
        {
            Show(line);
        }

        _subscription = context.Session.Events.Subscribe<ChatReceived>(OnChat);
    }

    /// <summary>The spew box, or null without its window.</summary>
    public GameSpewBox? Spew { get; }

    /// <summary>The main chat, or null without its window.</summary>
    public GameChatWindow? Main { get; }

    /// <summary>Every chat window (the main chat first).</summary>
    public IReadOnlyList<GameChatWindow> Windows => _windows;

    /// <summary>The chat windows as the router's chat sink.</summary>
    public IUiNoticeSink ChatSink => _sink;

    /// <summary>The talk focus (always <see cref="TalkFocus.All"/> in M2).</summary>
    public TalkFocus Focus { get; private set; } = TalkFocus.All;

    /// <summary>The talk-focus menu, or null.</summary>
    public UiMenu? FocusMenu => _menu;

    /// <summary>Lines handed to <see cref="IActionApi.Talk"/> so far.</summary>
    public int TalkSent { get; private set; }

    /// <summary>The last Talk handle, or null.</summary>
    public ActionHandle? LastTalk { get; private set; }

    /// <summary>The talk-focus choices that answered <see cref="NotInM2"/> so far.</summary>
    public int FocusRefusals { get; private set; }

    /// <summary>The chat commands (a line starting with '/') answered with the labelled line so far.</summary>
    public int CommandsDeclined { get; private set; }

    /// <summary>Reports a Talk the action layer refused, once it completes.</summary>
    public void Update()
    {
        for (var i = _sending.Count - 1; i >= 0; i--)
        {
            var handle = _sending[i];
            if (!handle.IsCompleted)
            {
                continue;
            }

            _sending.RemoveAt(i);
            if (handle.Outcome is { Kind: ActionOutcomeKind.Refused or ActionOutcomeKind.ServerError or ActionOutcomeKind.TimedOut } outcome)
            {
                Local($"Lifestone: your message was not sent ({outcome.Detail ?? outcome.Kind.ToString()}).");
            }
        }
    }

    /// <summary>A line of the shell's own, shown in the chat as system text (never sent).</summary>
    public void Local(string text) => Show(ChatTextTypes.System, text);

    /// <summary>
    /// Sends what was typed: plain text and '@' server commands as Talk, a '/' command as a labelled line, anything else the focus does not allow as
    /// <see cref="NotInM2"/>.
    /// </summary>
    public void Submit(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.TrimStart().StartsWith('/'))
        {
            CommandsDeclined++;
            _context.Manager.LogOnce(
                "chat:commands",
                LogSeverity.Warning,
                "TODO(g4-chat-commands): chat commands ('/...') are not built; only plain speech and '@' server commands are sent (what was typed is not logged)");
            Local($"Lifestone: chat commands are not built yet ({text.Trim().Split(' ')[0]}); only speech and @ server commands are sent (TODO(g4-chat-commands)).");
            return;
        }

        if (Focus != TalkFocus.All)
        {
            NotInM2.Show(_context.Manager.Dialogs, _context.Manager.Services.Log, "Chat to " + Focus);
            return;
        }

        TalkSent++;
        var handle = _context.Session.Actions.Talk(text, ActionSource.Ui);
        LastTalk = handle;
        _sending.Add(handle);
    }

    /// <summary>
    /// The main chat's menu choice (<c>gmMainChatUI::HandleSelection</c>, 11.4186 0x004CD540): only <see cref="TalkFocus.All"/> is honoured; another focus
    /// answers <see cref="NotInM2"/> and the menu goes back to All.
    /// </summary>
    public void SelectFocus(TalkFocus focus)
    {
        if (focus == TalkFocus.All)
        {
            Focus = TalkFocus.All;
            return;
        }

        FocusRefusals++;
        NotInM2.Show(_context.Manager.Dialogs, _context.Manager.Services.Log, "Chat to " + focus + " (the channels, tells and allegiance chat)");
        if (_menu is not null && _allRow is not null)
        {
            _menu.SetSelectedItem(_allRow, false);
        }
    }

    /// <summary>Unregisters from the router and stops listening.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscription.Dispose();
        if (_context.Notices is { } router)
        {
            if (ReferenceEquals(router.SpewBox, Spew))
            {
                router.SpewBox = null;
            }

            if (ReferenceEquals(router.Chat, _sink))
            {
                router.Chat = null;
            }
        }

        Spew?.Dispose();
        foreach (var window in _windows)
        {
            window.Dispose();
        }
    }

    private GameChatWindow Add(UiElement window)
    {
        var chat = new GameChatWindow(_context.Manager, window, (_, text) => Submit(text));
        _windows.Add(chat);
        return chat;
    }

    /// <summary>The lines of the current world entry, oldest first (what a new shell replays).</summary>
    internal static IEnumerable<ChatLine> EntryLines(ChatLog log)
    {
        // Only the lines of the current world entry: a new entry's chat starts empty (retail's chat log dies with gmGamePlayUI).
        var fromEntry = Math.Min(log.Count, (int)Math.Max(0, log.Total - log.EntryMark));
        for (var i = log.Count - fromEntry; i < log.Count; i++)
        {
            yield return log[i];
        }
    }

    private void OnChat(in ChatReceived e) => Show(e.Line);

    private void Show(in ChatLine line)
    {
        if (ChatLineText.TryFormat(line, _context.Session.State.Self.Guid, out var type, out var text))
        {
            Show(type, text);
        }
    }

    private void Show(int type, string text)
    {
        if (type == ChatTextTypes.SpewBox)
        {
            Spew?.DisplayStringInfo(type, UiStringRef.FromLiteral(text));
            return;
        }

        foreach (var window in _windows)
        {
            window.DisplayText(type, text);
        }
    }

    // gmMainChatUI::InitTalkFocusMenu: a row for the squelch toggle, then one per focus, in retail's order. Returns the All row.
    private UiElement? InitTalkFocusMenu(UiMenu menu)
    {
        menu.ListBox?.Flush();
        var strings = _context.Manager.Services.Strings;
        (string Token, TalkFocus? Focus)[] rows =
        [
            ("ID_Chat_SquelchSelectedNoSelection", null),
            ("ID_Chat_TellToMonarch", TalkFocus.Monarch),
            ("ID_Chat_TellToSelectedNoSelection", TalkFocus.Selected),
            ("ID_Chat_TellToPatron", TalkFocus.Patron),
            ("ID_Chat_TellToAll", TalkFocus.All),
            ("ID_Chat_TellToVassals", TalkFocus.Vassals),
            ("ID_Chat_TellToFellows", TalkFocus.Fellows),
            ("ID_Chat_TellToAllegiance", TalkFocus.Allegiance),
            ("ID_Chat_TellToGeneral", TalkFocus.General),
            ("ID_Chat_TellToTrade", TalkFocus.Trade),
            ("ID_Chat_TellToLFG", TalkFocus.Lfg),
            ("ID_Chat_TellToRoleplay", TalkFocus.Roleplay),
            ("ID_Chat_TellToSociety", TalkFocus.Society),
            ("ID_Chat_TellToOlthoi", TalkFocus.Olthoi),
        ];
        UiElement? all = null;
        var index = 0;
        foreach (var (token, focus) in rows)
        {
            UiStringRef text;
            if (strings is not null && strings.TryReference(StringTableEnum, token, out var reference))
            {
                text = reference;
            }
            else
            {
                text = UiStringRef.FromLiteral(token);
                _context.Manager.LogOnce($"chat:string:{token}", LogSeverity.Warning, $"TODO(g4-chat-strings): string {token} is not in the game string table; the talk-focus menu shows its name");
            }

            if (menu.InsertTextItem(text, index++) is { } row)
            {
                if (focus is { } f)
                {
                    row.SetProperty(new UiEnumProperty(TalkFocusProperty, (uint)f));
                    if (f == TalkFocus.All)
                    {
                        all = row;
                    }
                }
            }
        }

        if (all is not null)
        {
            menu.SetSelectedItem(all, false);
        }

        return all;
    }

    private sealed class MenuListener(GameChat owner) : IUiElementListener
    {
        public UiListenResult ListenToElementMessage(in UiElementMessage message)
        {
            if (message.MessageId == UiMessageIds.MenuSelection && message.ElementId == TalkFocusMenuId)
            {
                if (message.Item is { } row && row.InqProperty(TalkFocusProperty, out var value) && value is UiEnumProperty focus)
                {
                    owner.SelectFocus((TalkFocus)focus.Value);
                }
                else
                {
                    owner.SelectFocus(TalkFocus.Selected); // the squelch toggle row: not built either
                }
            }

            return UiListenResult.Default;
        }
    }

    private sealed class FanOut(List<GameChatWindow> windows) : IUiNoticeSink
    {
        public void DisplayStringInfo(int textType, in UiStringRef text)
        {
            foreach (var window in windows)
            {
                window.DisplayStringInfo(textType, text);
            }
        }
    }
}

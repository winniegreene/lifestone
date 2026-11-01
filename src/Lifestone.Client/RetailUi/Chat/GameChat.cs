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
/// <b>Entry.</b> <c>ClientCommunicationSystem::OnChatCommand</c> (11.4186 0x00581320, 11.6096 0x005821A0) parses the line. A <c>/</c> line goes to
/// <see cref="ChatCommands"/> (tell, reply, say, emote, AFK, the two recalls; the channels and the rest of retail's table are answered by name:
/// <c>TODO(g4-chat-channels)</c>, <c>TODO(g4-chat-commands-rest)</c>); an <c>@</c> line is sent as <see cref="IActionApi.Talk"/> as it is (<c>TODO(g4-chat-at-commands)</c>);
/// plain text goes by the talk focus (<see cref="ChatCommands.Say"/>). A Talk is <c>DoSay</c> (11.6096 0x005820C0), ending in <c>CM_Communication::Event_Talk</c>
/// (11.6096 0x006A53E0). An action the action layer refuses or the server fails (offline, not in the world, a recall ACE refuses) is reported in the chat.
/// </para>
/// <para>
/// <b>Talk focus.</b> Retail has no channel tabs: the main chat's popup menu 0x10000014 (<c>gmMainChatUI::InitTalkFocusMenu</c>, 11.4186 0x004CDC50, 11.6096
/// 0x004CE8C0) holds one row per focus, each with its value in property 0x1000000B. <see cref="TalkFocus.All"/> is local speech; <see cref="TalkFocus.Selected"/> sends plain
/// text as a tell to the last creature selected (<c>OnChatCommand</c>'s case 2 sends <c>Event_TalkDirect</c> to <c>GetLastSpeakableTarget</c>; the target is kept as
/// <c>gmMainChatUI::RecvNotice_SelectionChanged</c>, 11.6096 0x004CECC0, does: a selected object whose item type is creature, other than the player, becomes the target and
/// stays it). Choosing a channel answers <see cref="NotInM2"/> and keeps the previous focus (<c>TODO(g4-chat-channels)</c>, M6). The text swap of the Selected row
/// (<c>gmMainChatUI::SetSelected</c>, 11.6096 0x004CE3A0) is not built (<c>TODO(g4-chat-focus-row)</c>).
/// </para>
/// </remarks>
public sealed class GameChat : IDisposable
{
    /// <summary>The talk-focus popup menu in the main chat.</summary>
    public const uint TalkFocusMenuId = 0x10000014;

    /// <summary>Master property 0x1000000B on a menu row: its <see cref="TalkFocus"/>.</summary>
    public const uint TalkFocusProperty = 0x1000000B;

    private const uint StringTableEnum = 0x10000001;

    /// <summary>ITEM_TYPE creature: what <c>PublicWeenieDesc::IsTalkable</c> (11.4186) accepts.</summary>
    private const uint CreatureItemType = 0x10;

    private readonly UiLoginContext _context;
    private readonly List<GameChatWindow> _windows = [];
    private readonly FanOut _sink;
    private readonly IDisposable _subscription;
    private readonly List<(ActionHandle Handle, ChatCommandResult Result)> _sending = [];
    private readonly ChatCommands _commands;
    private string? _lastTeller;
    private uint _speakableGuid;
    private string? _speakableName;
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

        _commands = new ChatCommands(context.Session.Actions, () => _lastTeller, () => context.Session.State.Self.Qualities.Afk);
        foreach (var line in EntryLines(context.Session.State.Chat))
        {
            NoteTeller(line);
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

    /// <summary>The talk focus: <see cref="TalkFocus.All"/> or <see cref="TalkFocus.Selected"/> (the channels answer <see cref="NotInM2"/> and are not kept).</summary>
    public TalkFocus Focus { get; private set; } = TalkFocus.All;

    /// <summary>
    /// The selected object's id, 0 for none (<c>ACCWeenieObject::selectedID</c>); the play mode gives its <c>ObjectSelection</c>. Read each <see cref="Update"/> to keep the
    /// Selected focus's target. Null leaves the Selected focus without a target.
    /// </summary>
    public Func<uint>? SelectedProvider { get; set; }

    /// <summary>The Selected focus's target (retail's last speakable target) as its id, 0 for none.</summary>
    public uint SpeakableTarget => _speakableGuid;

    /// <summary>The name of the last player who sent a tell (retail's last teller), or null.</summary>
    public string? LastTeller => _lastTeller;

    /// <summary>M5 7 D145: the autopilot's controls for <c>/stop</c> and <c>/auto</c> (<see cref="ChatCommands.Autopilot"/>; the play host's autopilot wires them), or null for none.</summary>
    public IAutopilotChatControls? Autopilot
    {
        get => _commands.Autopilot;
        set => _commands.Autopilot = value;
    }

    /// <summary>The talk-focus menu, or null.</summary>
    public UiMenu? FocusMenu => _menu;

    /// <summary>Lines handed to <see cref="IActionApi.Talk"/> so far (plain speech, '@' lines, and '/' lines the server is left to answer).</summary>
    public int TalkSent { get; private set; }

    /// <summary>The last Talk handle, or null.</summary>
    public ActionHandle? LastTalk { get; private set; }

    /// <summary>The talk-focus choices (channels) that answered <see cref="NotInM2"/> so far.</summary>
    public int FocusRefusals { get; private set; }

    /// <summary>The '/' commands run so far (each answered by an action or a line).</summary>
    public int CommandsRun { get; private set; }

    /// <summary>The '/' commands answered with a line alone (a channel, a command not built, a usage line) so far.</summary>
    public int CommandsDeclined { get; private set; }

    /// <summary>Follows the selection and reports an action the action layer refused, the server failed or timed out, once it completes (a confirmed recall or AFK change says so).</summary>
    public void Update()
    {
        NoteSelection();
        for (var i = _sending.Count - 1; i >= 0; i--)
        {
            var (handle, result) = _sending[i];
            if (!handle.IsCompleted)
            {
                continue;
            }

            _sending.RemoveAt(i);
            if (handle.Outcome is { Kind: ActionOutcomeKind.Refused or ActionOutcomeKind.ServerError or ActionOutcomeKind.TimedOut } outcome)
            {
                Local(ChatCommands.FailureLine(result.FailurePrefix, outcome));
            }
            else if (handle.Outcome is { Kind: ActionOutcomeKind.Confirmed } && result.SuccessLine is { } done)
            {
                Local(done);
            }
        }
    }

    /// <summary>A line of the shell's own, shown in the chat as system text (never sent).</summary>
    public void Local(string text) => Show(ChatTextTypes.System, text);

    /// <summary>
    /// Sends what was typed: a '/' line through <see cref="ChatCommands"/>, an '@' line as Talk, plain text by the talk focus
    /// (<see cref="ChatCommands.Say"/>: All is Talk, Selected a tell to the selected creature).
    /// </summary>
    public void Submit(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        NoteSelection();
        ChatCommandResult result;
        if (ChatCommands.IsCommand(text))
        {
            CommandsRun++;
            result = _commands.Run(text);
            if (result.Handle is null && result.Lines.Count > 0 && !result.ClientHandled)
            {
                CommandsDeclined++;
            }
        }
        else if (text.TrimStart().StartsWith('@'))
        {
            result = _commands.Say(TalkFocus.All, text, null);
        }
        else
        {
            result = _commands.Say(Focus, text, SpeakableName());
        }

        if (result.TodoKey is { } key && result.TodoMessage is { } message)
        {
            _context.Manager.LogOnce(key, LogSeverity.Warning, message);
        }

        foreach (var line in result.Lines)
        {
            Show(line.Type, line.Text);
        }

        if (result.Handle is { } handle)
        {
            if (result.IsTalk)
            {
                TalkSent++;
                LastTalk = handle;
            }

            _sending.Add((handle, result));
        }
    }

    /// <summary>
    /// The main chat's menu choice (<c>gmMainChatUI::HandleSelection</c>, 11.4186 0x004CD540): <see cref="TalkFocus.All"/> and <see cref="TalkFocus.Selected"/> are
    /// honoured; a channel answers <see cref="NotInM2"/> and the menu goes back to the focus it had (<c>TODO(g4-chat-channels)</c>).
    /// </summary>
    public void SelectFocus(TalkFocus focus)
    {
        if (focus is TalkFocus.All or TalkFocus.Selected)
        {
            Focus = focus;
            return;
        }

        FocusRefusals++;
        NotInM2.Show(_context.Manager.Dialogs, _context.Manager.Services.Log, "Chat to " + focus + " (the channels, tells and allegiance chat; TODO(g4-chat-channels))");
        var back = Focus == TalkFocus.Selected ? GetRow(TalkFocus.Selected) : _allRow;
        if (_menu is not null && back is not null)
        {
            _menu.SetSelectedItem(back, false);
        }
    }

    // The squelch toggle row (gmMainChatUI::ToggleSquelchOnCurrentSpeakableTarget, 11.6096 0x004CDEA0): not built (TODO(g4-chat-squelch)); the menu goes back.
    private void SquelchRowChosen()
    {
        FocusRefusals++;
        NotInM2.Show(_context.Manager.Dialogs, _context.Manager.Services.Log, "Squelch the selected player (TODO(g4-chat-squelch))");
        var back = Focus == TalkFocus.Selected ? GetRow(TalkFocus.Selected) : _allRow;
        if (_menu is not null && back is not null)
        {
            _menu.SetSelectedItem(back, false);
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

    private void OnChat(in ChatReceived e)
    {
        NoteTeller(e.Line);
        Show(e.Line);
    }

    // gmCCommunicationSystem::GetLastTeller: the last player who sent this one a tell.
    private void NoteTeller(in ChatLine line)
    {
        if (line.Kind == ChatKind.Tell && !string.IsNullOrEmpty(line.Sender) && line.SenderGuid != _context.Session.State.Self.Guid)
        {
            _lastTeller = line.Sender;
        }
    }

    // gmMainChatUI::RecvNotice_SelectionChanged: a selected creature (PublicWeenieDesc::IsTalkable is item type 0x10), other than the player, becomes the target and stays it.
    private void NoteSelection()
    {
        var selected = SelectedProvider?.Invoke() ?? 0;
        var state = _context.Session.State;
        if (selected != 0 && selected != state.Self.Guid && state.Objects.TryGet(selected, out var obj) && obj.ItemType == CreatureItemType)
        {
            _speakableGuid = selected;
            _speakableName = obj.Name;
        }
    }

    private string? SpeakableName()
    {
        if (_speakableGuid != 0 && _context.Session.State.Objects.TryGet(_speakableGuid, out var obj) && !string.IsNullOrEmpty(obj.Name))
        {
            return obj.Name;
        }

        return _speakableName;
    }

    private UiElement? GetRow(TalkFocus focus)
    {
        if (_menu is null)
        {
            return null;
        }

        for (var i = 0; i < _menu.ItemCount; i++)
        {
            if (_menu.GetItem(i) is { } row && row.InqProperty(TalkFocusProperty, out var value) && value is UiEnumProperty { Value: var v } && v == (uint)focus)
            {
                return row;
            }
        }

        return null;
    }

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
                    owner.SquelchRowChosen(); // the squelch toggle row: not built
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

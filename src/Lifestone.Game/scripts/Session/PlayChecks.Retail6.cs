using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Chat;
using Lifestone.Client.RetailUi;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;

namespace Lifestone.Game.Sessions;

/// <summary>
/// M5 item 6, slice k1 (docs/session.md section 16, the stand-in's retail-tranche answers): the play-checks of the server's Yes/No question, the options and the window layout across a second world entry,
/// the spell bars (a drag and a number key), the chat commands (<c>/e</c>, <c>/afk msg</c>, <c>/lifestone</c>, a Selected-focus tell) and a key on a locked chest. Every answer is the stand-in's (ACE's, cited in
/// <c>StandInWorld.Retail6.cs</c>), not a server's; the live proof is k2's. The question, the tell, the bar and the chest rows run in the first session; the options, the layout and the bar are read again in the
/// second (the reconnect's description), and the commands run last in it.
/// </summary>
public sealed partial class PlayChecks
{
    /// <summary>The review frame of the server's Yes/No box over the world (the stand-in asks the question; the text is the stand-in's placeholder).</summary>
    public const string ServerQuestionFrameName = "e1-server-question";

    /// <summary>The review frame of the spellcasting window after a spell was dragged onto its bar.</summary>
    public const string SpellcastingEditedFrameName = "c2-spellcasting-edited";

    private (uint One, uint Two)? _r6OptionsApplied;
    private (int X, int Y)? _r6ToolbarMoved;
    private (int X, int Y)? _r6ToolbarHome;
    private GamePlayScreen? _r6FirstShell; // the first entry's screen: the second entry must be a new one that read the file (6-R2, 6-R6)
    private uint _r6SpellOnBar;

    private UiInputRouter? Input6 => _ui?.Manager.Input;

    private async Task Retail6PressAsync(UiElement element)
    {
        var box = element.ScreenBox;
        var (x, y) = ((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
        Input6!.MouseMove(x, y);
        await Frames(2);
        Input6.MouseMove(x, y);
        Input6.MouseButton(UiMouseButton.Left, true);
        Input6.MouseButton(UiMouseButton.Left, false);
        await Frames(2);
    }

    private IEnumerable<ChatLine> ChatLines6(IGameSessionView session) => Enumerable.Range(0, session.State.Chat.Count).Select(i => session.State.Chat[i]);

    private string? Frames6Folder() =>
        _framesDirectory is { } directory ? PanelFramesPlan.FolderFor(ShellFramesPlan.FolderFor(directory, _framesSize.X, _framesSize.Y)) : null;

    /// <summary>
    /// The server's question appears as a Yes/No box over the world; No closes it and sends GameAction 0x0275. The stand-in asks (GE 0x0274, type Yes_No) and records the answer; ACE sends no 0x0276 for an answer.
    /// With <c>--play-frames</c> the box is saved as <see cref="ServerQuestionFrameName"/> before the answer.
    /// </summary>
    private async Task Retail6QuestionChecksAsync(IGameSessionView session)
    {
        const string name = "the-servers-question-is-a-yes-no-box-and-no-closes-it-and-sends-0275";
        var shell = _ui?.Screens?.Flow.CurrentScreen as GamePlayScreen;
        var server = _host.StandIn;
        if (shell?.Confirmations is not { } confirmations || _ui?.Manager is not { } manager || server is null)
        {
            Check(name, false, $"shell={shell is not null} confirmations={shell?.Confirmations is not null} server={server is not null}");
            return;
        }

        var answers = server.QuestionAnswers.Count;
        var context = server.AskQuestion();
        var shown = await WaitFor(() => confirmations.Open == 1 && manager.Dialogs.OpenDialogs.Any(), 10.0);
        var dialog = manager.Dialogs.OpenDialogs.FirstOrDefault();
        var text = (dialog?.GetChildRecursive(UiDialog.TextElement) as UiTextElement)?.Text ?? string.Empty;
        var isYesNo = dialog is UiConfirmationDialog && dialog.GetChildRecursive(UiConfirmationDialog.YesButton) is not null;
        await Frames(30);
        var placeholder = "the question's words are the stand-in's own (ACE's Yes/No questions come from a quest or a vendor)";
        var shot = true;
        if (Frames6Folder() is { } folder)
        {
            HideLightsHud();
            await Frames(2);
            shot = SaveCheckedFrame($"{folder}/{ServerQuestionFrameName}.png", ServerQuestionFrameName, placeholder);
            RestoreLightsHud();
            if (!shot)
            {
                FrameFailed($"{ServerQuestionFrameName}: the frame was not taken");
            }
        }

        if (dialog?.GetChildRecursive(UiConfirmationDialog.NoButton) is { } no)
        {
            await Retail6PressAsync(no);
        }

        var closed = await WaitFor(() => confirmations.Open == 0 && !manager.Dialogs.OpenDialogs.Any(), 5.0);
        var recorded = await WaitFor(() => server.QuestionAnswers.Count == answers + 1, 5.0);
        var last = server.QuestionAnswers.Count > 0 ? server.QuestionAnswers[^1] : default;
        Check(
            name,
            context != 0 && shown && isYesNo && text.Contains(StandInServer.QuestionText, StringComparison.Ordinal) && closed && recorded && last == (7u, context, false)
                && session.State.PendingConfirmations.Count == 0 && confirmations.Answered >= 1 && server.QuestionsClosed == 0,
            $"context={context} shown={shown} yes_no_box={isYesNo} text='{text}' closed={closed} answers_recorded={recorded} last=({last.Type},{last.Context},{last.Accepted}) pending={session.State.PendingConfirmations.Count} closed_by_0276={server.QuestionsClosed}");
    }

    /// <summary>
    /// A window moved is kept: the radar window is moved a little (the shell writes the layout at the world exit and reads it at the next entry; <see cref="Retail6SecondEntryChecksAsync"/> reads it).
    /// </summary>
    private async Task Retail6MoveWindowAsync()
    {
        if (_ui?.Screens?.Flow.CurrentScreen is not GamePlayScreen { Radar: { } toolbar })
        {
            return;
        }

        var box = toolbar.Box;
        _r6ToolbarHome = (box.X0, box.Y0);
        _r6FirstShell = _ui.Screens.Flow.CurrentScreen as GamePlayScreen;
        // 6-R2: the checks run on an empty in-memory layout store, so the first entry reads no file another run saved (and the place the radar moves from is the layout's default every run).
        Log.Info(LogCategory, $"retail6: the first entry read a layout file: {_r6FirstShell?.LastLayoutLoad.Found} (the check suite's store starts empty)");
        var moved = (X: box.X0, Y: box.Y0 > 40 ? box.Y0 - 24 : box.Y0 + 24);
        toolbar.MoveTo(moved.X, moved.Y);
        await Frames(2);
        _r6ToolbarMoved = (toolbar.Box.X0, toolbar.Box.Y0);
        Log.Info(LogCategory, $"retail6: the radar window moved from ({box.X0},{box.Y0}) to ({toolbar.Box.X0},{toolbar.Box.Y0}); the second entry reads it back");
    }

    /// <summary>
    /// A spell dragged from the spellbook onto the spellcasting window's first tab is added to bar 0 (AddSpellFavorite as <see cref="ActionSource.Ui"/>), the stand-in keeps it, and a magic-mode number key casts it
    /// (the key of the cell it landed in). The bar stays after the next description (the second session reads it). With <c>--play-frames</c> the window is saved as <see cref="SpellcastingEditedFrameName"/>.
    /// </summary>
    private async Task Retail6SpellBarChecksAsync(IGameSessionView session)
    {
        const string dragName = "a-spell-dragged-onto-a-bar-is-added-through-the-action-api-and-shows-on-the-bar";
        const string keyName = "a-magic-mode-number-key-casts-the-spell-in-its-cell";
        var shell = _ui?.Screens?.Flow.CurrentScreen as GamePlayScreen;
        var window = shell?.Root?.GetChildRecursive(GameSpellcastingPanel.WindowId) as GameSpellcastingPanel;
        var book = shell?.PanelControllers?.ControllersOf(13).OfType<GameSpellbookPage>().FirstOrDefault();
        if (shell is null || window is null || book is null || Input6 is null)
        {
            Check(dragName, false, $"shell={shell is not null} window={window is not null} spellbook={book is not null}");
            Check(keyName, false, "no spellcasting window");
            return;
        }

        var self = 0u; // the spell chosen below: the first one the spellbook shows that bar 0 does not hold
        var caster = await HoldCasterAsync(session);
        var magic = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
        var inMagic = await WaitFor(() => magic.IsCompleted && session.State.Self.Qualities.CombatMode == CombatModes.Magic, 10.0);
        window.OpenTabByClick(0);
        var opened = await OpenPanelAsync(shell, 13, 12);
        opened &= shell.PanelHost?.ElementFor(13) is UiPanel page && (page.OpenTabId == 0x100002A9 || page.OpenTab(0x100002A9)); // the Spellbook tab (an earlier row may have left the page on Components)
        await Frames(6);
        UiItem? source = null;
        string? missed = null;
        var listBox = book.List.ScreenBox;
        var onBar = window.ShownSpells(0);
        for (var i = 0; i < book.List.CellCount; i++)
        {
            if (book.List.CellAt(i) is { } cell && cell.SpellId != 0 && !onBar.Contains(cell.SpellId) && session.Spell(cell.SpellId) is not null)
            {
                var c = ((cell.ScreenBox.X0 + cell.ScreenBox.X1) / 2, (cell.ScreenBox.Y0 + cell.ScreenBox.Y1) / 2);
                var hit = _ui!.Manager.HitTest(c.Item1, c.Item2);
                missed ??= hit?.GetType().Name ?? "nothing";
                if (listBox.X0 <= c.Item1 && c.Item1 < listBox.X1 && listBox.Y0 <= c.Item2 && c.Item2 < listBox.Y1)
                {
                    source = cell;
                    self = cell.SpellId;
                    break;
                }
            }
        }

        var edits = window.BarEdits.Count;
        var target = window.TabElement(0);
        var dropped = false;
        var hitSource = false;
        var dragNote = string.Empty;
        if (self != 0 && source is not null && target is not null)
        {
            var from = ((source.ScreenBox.X0 + source.ScreenBox.X1) / 2, (source.ScreenBox.Y0 + source.ScreenBox.Y1) / 2);
            var to = ((target.ScreenBox.X0 + target.ScreenBox.X1) / 2, (target.ScreenBox.Y0 + target.ScreenBox.Y1) / 2);
            await Frames(60); // apart from the last gesture: no double click
            var startedBefore = Input6.Drag.Started;
            var droppedBefore = Input6.Drag.Dropped;
            hitSource = ReferenceEquals(_ui!.Manager.HitTest(from.Item1, from.Item2), source);
            Input6.MouseMove(from.Item1, from.Item2);
            await Frames(2);
            Input6.MouseMove(from.Item1, from.Item2);
            Input6.MouseButton(UiMouseButton.Left, true);
            Input6.MouseMove(from.Item1 + 3, from.Item2);
            Input6.MouseMove(from.Item1 + 14, from.Item2 + 6);
            await Frames(2);
            Input6.MouseMove(to.Item1, to.Item2);
            await Frames(2);
            Input6.MouseButton(UiMouseButton.Left, false);
            await Frames(3);
            dropped = Input6.Drag.Started == startedBefore + 1 && Input6.Drag.Dropped == droppedBefore + 1 && Input6.Drag.LastEnd == UiDragEnd.Dropped;
            dragNote = $"started={Input6.Drag.Started - startedBefore} dropped_count={Input6.Drag.Dropped - droppedBefore} last_end={Input6.Drag.LastEnd} hit_source={hitSource} hit_target={ReferenceEquals(_ui!.Manager.HitTest(to.Item1, to.Item2), target)}";
        }

        if (!dropped && Frames6Folder() is { } failFolder)
        {
            SaveCheckedFrame($"{failFolder}/c2-spellbook-drag-not-started.png", "c2-spellbook-drag-not-started"); // a failure's evidence: what the pointer was over
        }

        var added = window.BarEdits.Count == edits + 1 ? window.BarEdits[^1] : null;
        var addDone = added is not null && await WaitFor(() => added.IsCompleted, 10.0);
        await ClosePanelAsync(shell, 13);
        await Frames(4);
        var shownSpells = window.ShownSpells(0);
        var index = shownSpells.IndexOf(self);
        Check(
            dragName,
            inMagic && opened && dropped && addDone && added is { Kind: ActionKind.AddSpellFavorite, Source: ActionSource.Ui } && added.Outcome?.Kind == ActionOutcomeKind.Sent && index >= 0 && window.OpenTab == 0,
            $"spell={self} magic={inMagic} spellbook_opened={opened} source_cell={source is not null} dropped={dropped} summary='{added?.Summary}' outcome={added?.Outcome?.Kind} bar0=[{string.Join(",", shownSpells)}] index={index} first_pointer_hit={missed} {dragNote}");
        _r6SpellOnBar = self;

        if (Frames6Folder() is { } folder)
        {
            HideLightsHud();
            await Frames(6);
            var shot = window.IsShown && SaveCheckedFrame($"{folder}/{SpellcastingEditedFrameName}.png", SpellcastingEditedFrameName);
            RestoreLightsHud();
            if (!shot)
            {
                FrameFailed($"{SpellcastingEditedFrameName}: the window did not show (shown={window.IsShown})");
            }
        }

        // The number key of the cell the spell landed in (keys 1 to 9 are quickslots 0 to 8 of the open tab) casts it.
        var previousSelection = _mode.Selection.Selected;
        _mode.Selection.Select(StatueGuid); // a targeted spell goes at the selected object
        await Frames(3);
        var casts = window.Casts;
        var presses = window.Keys?.Presses ?? -1;
        var keyDown = index is >= 0 and < 9 && Input6.KeyDown(DikScanCodes.D1 + index);
        Input6.KeyUp(DikScanCodes.D1 + index);
        await Frames(4);
        Check(
            keyName,
            inMagic && keyDown && window.Casts == casts + 1 && (window.Keys?.Presses ?? -1) > presses,
            $"key={index + 1} registered={window.Keys?.Registered} key_taken={keyDown} casts={casts}->{window.Casts} presses={presses}->{window.Keys?.Presses}");
        _mode.Selection.Select(previousSelection);
        await Frames(120); // let the cast play out before the mode changes back
        var peace = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        await WaitFor(() => peace.IsCompleted && session.State.Self.Qualities.CombatMode == CombatModes.NonCombat, 10.0);
        await StowAsync(session, caster);
    }

    /// <summary>
    /// A Selected-focus tell: the talk focus is Selected with a creature selected, a plain line is a tell to its name (GameAction 0x005D), which the stand-in takes (and refuses as ACE does for a player not online).
    /// </summary>
    private async Task Retail6TellCheckAsync(IGameSessionView session)
    {
        const string name = "the-selected-focus-sends-a-tell-to-the-selected-creature";
        var shell = _ui?.Screens?.Flow.CurrentScreen as GamePlayScreen;
        var server = _host.StandIn;
        const uint target = 0x80000E20;
        var placed = server?.PlaceNear(new StandInObject(target, "Stand-in Talker", default, MotionTable: 0, DescriptionFlags: 0x8u, BlipColor: 3), default); // a creature-typed person (the server's white blip), the only kind a tell can go to
        var present = placed is not null && await WaitFor(() => session.State.Objects.Contains(target), 10.0);
        if (shell?.Chat is not { } chat || server is null || !present)
        {
            Check(name, false, $"chat={shell?.Chat is not null} server={server is not null} target_present={present}");
            return;
        }

        var previous = _mode.Selection.Selected;
        var tells = server.ChatAnswers.Tells.Count;
        var talksBefore = chat.TalkSent;
        _mode.Selection.Select(target);
        await Frames(3);
        chat.SelectFocus(TalkFocus.Selected);
        chat.Submit("hello from the selected focus");
        var arrived = await WaitFor(() => server.ChatAnswers.Tells.Count == tells + 1, 5.0);
        await Frames(10);
        var tell = arrived ? server.ChatAnswers.Tells[^1] : default;
        var focus = chat.Focus;
        chat.SelectFocus(TalkFocus.All);
        _mode.Selection.Select(previous);
        server.Remove(target);
        Check(
            name,
            arrived && focus == TalkFocus.Selected && tell == ("Stand-in Talker", "hello from the selected focus") && chat.TalkSent == talksBefore,
            $"focus={focus} tells={tells}->{server.ChatAnswers.Tells.Count} target='{tell.Target}' text='{tell.Text}' talks_sent={chat.TalkSent}");
    }

    /// <summary>
    /// The key on a locked chest: the stand-in locks a chest (0x02D2 Locked true), a Use says it is locked, a stand-in key spawned by the tools opens it (UseWithTarget): the chest then reads unlocked and ACE's line
    /// "The Chest ... has been unlocked." is in the chat. The key and the chest are the stand-in's placeholders.
    /// </summary>
    private async Task Retail6ChestChecksAsync(IGameSessionView session)
    {
        const string name = "a-key-unlocks-a-locked-chest-and-it-reads-unlocked";
        const uint chest = 0x80000E10;
        var server = _host.StandIn;
        if (server is null)
        {
            Check(name, false, "no stand-in server");
            return;
        }

        var placed = server.PlaceChest(chest, new System.Numerics.Vector3(1f, 3f, 0f));
        await WaitFor(() => session.State.Objects.Contains(chest), 10.0);
        server.LockChest(chest);
        var locked = await WaitFor(() => session.State.Objects.TryGet(chest, out var o) && o.Locked == true, 10.0);
        Visit.Items = StandInServer.WithKey(Visit.Items); // the earlier rows set the visit's item kinds; the key joins them (the stand-in reads the visit's table when it answers the item request)
        var spawn = session.Tools.SpawnItem(StandInServer.KeyWcid, 1, ActionSource.Host);
        var spawned = await WaitFor(() => spawn.IsCompleted, 10.0);
        var key = spawn.Result?.Guid ?? 0;
        var use = session.Actions.Use(chest, ActionSource.Ui);
        await WaitFor(() => use.IsCompleted, 10.0);
        var lockedLine = ChatLines6(session).Any(static l => l.Text.EndsWith(" is locked", StringComparison.Ordinal));
        var stillLocked = session.State.Objects.TryGet(chest, out var before) && before.Locked == true;
        var unlock = key == 0 ? null : session.Actions.UseWithTarget(key, chest, ActionSource.Ui);
        var unlocked = await WaitFor(() => session.State.Objects.TryGet(chest, out var o) && o.Locked == false, 10.0);
        var line = ChatLines6(session).Any(static l => l.Text.EndsWith(" has been unlocked.\n", StringComparison.Ordinal));
        Check(
            name,
            placed is not null && locked && spawned && key != 0 && lockedLine && stillLocked && unlock is not null && unlocked && line,
            $"placed={placed is not null} locked_after_create={locked} spawn={spawn.Outcome?.Kind}:{spawn.Outcome?.Detail} key=0x{key:X8} locked_line={lockedLine} locked_after_use={stillLocked} unlocked={unlocked} line={line}");
        if (placed is not null)
        {
            server.Remove(chest);
        }
    }

    /// <summary>
    /// The second world entry (the reconnect's description): the option applied in the first is set again, the toolbar is where it was moved to, and the bar holds the spell that was dragged onto it.
    /// </summary>
    private async Task Retail6SecondEntryChecksAsync(IGameSessionView session)
    {
        var shell = _ui?.Screens?.Flow.CurrentScreen as GamePlayScreen;
        var q = session.State.Self.Qualities;
        var applied = _r6OptionsApplied;
        await Frames(2);
        Check(
            "an-option-applied-is-set-again-after-the-next-description",
            applied is { } words && shell is { Options: { } options, SideBySideVitals: { } side } && (q.CharacterOptions1, q.CharacterOptions2) == words && options.SideBySideVitals && side.Visible,
            $"applied={(applied is { } w ? $"{w.One:X8}/{w.Two:X8}" : "none")} now={q.CharacterOptions1:X8}/{q.CharacterOptions2:X8} side_by_side={shell?.Options?.SideBySideVitals} window_visible={shell?.SideBySideVitals?.Visible}");
        var toolbar = shell?.Radar; // the radar window (<RADA>): moving the toolbar moved the shortcut bar the hotbar row drags onto
        Check(
            "a-window-moved-is-in-the-same-place-after-a-second-world-entry",
            _r6ToolbarMoved is { } moved && toolbar is not null && shell!.LayoutLoaded && (toolbar.Box.X0, toolbar.Box.Y0) == moved
                && !ReferenceEquals(shell, _r6FirstShell) && shell.LastLayoutLoad is { Found: true, WindowsApplied: >= 1 },
            $"moved={_r6ToolbarMoved} now=({toolbar?.Box.X0},{toolbar?.Box.Y0}) layout_loaded={shell?.LayoutLoaded} file={shell?.LayoutFileName} new_screen={!ReferenceEquals(shell, _r6FirstShell)} file_found={shell?.LastLayoutLoad.Found} windows_applied={shell?.LastLayoutLoad.WindowsApplied}");
        if (toolbar is not null && _r6ToolbarHome is { } home)
        {
            toolbar.MoveTo(home.X, home.Y); // the rows after this one were written for the window at its home place
            await Frames(2);
        }

        var bars = session.State.Self.Spellbook.SpellBars;
        Check(
            "a-spell-dragged-onto-a-bar-stays-after-the-next-description",
            _r6SpellOnBar != 0 && !bars.IsDefaultOrEmpty && !bars[0].IsDefaultOrEmpty && bars[0].Contains(_r6SpellOnBar),
            $"spell={_r6SpellOnBar} bar0=[{(bars.IsDefaultOrEmpty || bars[0].IsDefaultOrEmpty ? string.Empty : string.Join(",", bars[0]))}]");
    }

    /// <summary>
    /// <c>/e</c>, <c>/afk msg gone</c>, <c>/afk off</c> and <c>/lifestone</c> typed in the chat reach the stand-in and their results show in the chat or the state: the emote line, the AFK state and message, and the
    /// recall (the line, then the teleport that completes it).
    /// </summary>
    private async Task Retail6CommandChecksAsync(IGameSessionView session)
    {
        var shell = _ui?.Screens?.Flow.CurrentScreen as GamePlayScreen;
        var server = _host.StandIn;
        if (shell?.Chat is not { } chat || server is null)
        {
            foreach (var name in new[] { "slash-e-reaches-the-stand-in-and-its-line-shows-in-the-chat", "slash-afk-msg-reaches-the-stand-in-and-the-state-reads-away", "slash-lifestone-reaches-the-stand-in-and-the-recall-completes-on-the-teleport" })
            {
                Check(name, false, $"chat={shell?.Chat is not null} server={server is not null}");
            }

            return;
        }

        chat.Submit("/e waves hello");
        var emoted = await WaitFor(() => server.ChatAnswers.Emotes.Count == 1, 5.0);
        await WaitFor(() => ChatLines6(session).Any(static l => l.Text.Contains("waves hello", StringComparison.Ordinal)), 5.0);
        var emoteLine = ChatLines6(session).Any(static l => l.Text.Contains("waves hello", StringComparison.Ordinal));
        var emoteKind = ChatLines6(session).LastOrDefault(static l => l.Text.Contains("waves hello", StringComparison.Ordinal)).Kind;
        Check(
            "slash-e-reaches-the-stand-in-and-its-line-shows-in-the-chat",
            emoted && server.ChatAnswers.Emotes[0] == "waves hello" && emoteLine,
            $"emotes=[{string.Join("|", server.ChatAnswers.Emotes)}] line_in_chat={emoteLine} kind={emoteKind}");

        chat.Submit("/afk msg gone");
        var away = await WaitFor(() => session.State.Self.Qualities.Afk && server.ChatAnswers.Afk, 5.0);
        var message = server.ChatAnswers.AfkMessage;
        chat.Submit("/afk off");
        var back = await WaitFor(() => !session.State.Self.Qualities.Afk && !server.ChatAnswers.Afk, 5.0);
        Check(
            "slash-afk-msg-reaches-the-stand-in-and-the-state-reads-away",
            away && message == "gone" && back,
            $"away_in_state={away} stand_in_message='{message}' back={back}");

        var stamp = session.State.Self.Stamps.Teleport;
        chat.Submit("/lifestone");
        var recalled = await WaitFor(() => server.ChatAnswers.LifestoneRecalls == 1, 5.0);
        var teleported = await WaitFor(() => session.State.Self.Stamps.Teleport != stamp, 20.0);
        await Frames(10);
        var line = ChatLines6(session).Any(static l => l.Text.EndsWith(" is recalling to the lifestone.", StringComparison.Ordinal));
        Check(
            "slash-lifestone-reaches-the-stand-in-and-the-recall-completes-on-the-teleport",
            recalled && teleported && line,
            $"recalls={server.ChatAnswers.LifestoneRecalls} teleport_stamp={stamp}->{session.State.Self.Stamps.Teleport} recall_line={line}");
        await WaitFor(() => _mode.Rig is not null && _mode.Flow.Stage == Lifestone.Client.Play.PlayStage.InWorld, 30.0);
    }
}

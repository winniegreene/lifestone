using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.LifestonePanels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// M6 6 MD2 (L1, map-menu.md 6.4): the automation menu on screen, offline. The play host (a ready-made dial over idle stations, as <see cref="AutopilotDialAsync"/>'s) wires the game screen's menu, the tools panel's Jobs button and the
/// inspector to its job source; the Jobs button (a click through the shell's input) opens the menu; its rows come from the source (level up, a quest of the catalog with its experience, errand, stop); a click on errand chooses the
/// task and the dial starts it, the answer in the menu's status line and the chat; the inspector's Job line names it; a click on stop stops him where he stands and answers; the Jobs button closes the menu. The menu window is a
/// <b>placeholder</b> (retail had none); its review frame is <c>11-shell-automation-menu</c>.
/// </summary>
public sealed partial class PlayChecks
{
    /// <summary>The level the check's source reads (a ready-made source: the tusk quests of the catalog fit it, with their fixed awards).</summary>
    private const int MenuCheckLevel = 15;

    private async Task AutomationMenuChecksAsync(IGameSessionView session, RetailUiLayer? ui)
    {
        if (ui?.Manager is not { } manager || ui.Screens?.Flow.CurrentScreen is not GamePlayScreen { ToolsUi: { } panel, Menu: { } menu, Inspector: { } inspector, Hotkeys: { } hotkeys, Chat: { Main: { } mainChat } chat } gameScreen)
        {
            Check("the-automation-menu-is-wired-to-the-play-host", false, "the shell has no tools panel, no menu, no inspector or no main chat");
            return;
        }

        var log = GodotLogSink.Instance;
        var options = new AutopilotOptions();
        var logic = new AutopilotHostLogic(
            carry =>
            {
                var scenario = new AutopilotScenario(carry.Remaining(options), new IdleStations(), new JourneyContext { Character = "BSD Mage", Account = "test", Log = log, Travel = new PlaceTravel(), Spells = new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>() });
                carry.Apply(scenario);
                return scenario;
            },
            log,
            options);
        var timers = new QuestTimers("check");
        var source = new AutopilotJobSource(AutopilotJob.LevelUp, () => logic.Task, () => MenuCheckLevel, level => AutopilotJobSource.NextLevelXp(session.XpTable?.CharacterLevelXpList, level), QuestCatalog.All, () => timers, () => session.Clock.Time.GetUtcNow(), [], log);
        menu.Source = null;
        inspector.Jobs = null;
        panel.ToggleJobs = null;
        var host = AutopilotHost.InstallWith(_main, _host, ui, logic, new AutopilotHostOptions(), source);
        await Frames(3);
        Check(
            "the-automation-menu-is-wired-to-the-play-host",
            ReferenceEquals(menu.Source, source) && menu.Choose is not null && menu.Stop is not null && panel.ToggleJobs is not null && ReferenceEquals(inspector.Jobs, source) && ReferenceEquals(host.Jobs, source),
            $"source={ReferenceEquals(menu.Source, source)} choose={menu.Choose is not null} stop={menu.Stop is not null} jobs_button={panel.ToggleJobs is not null} inspector={ReferenceEquals(inspector.Jobs, source)}");

        // A retail panel page the dial's panel director left open (the inventory) is closed first, as a person would, so the menu's frame shows the menu whole.
        if (ui.Screens?.Flow.CurrentScreen is GamePlayScreen { PanelHost: { CurrentPanel: var page and not 0 } panels })
        {
            panels.SetPanelVisibility(page, false);
            await Frames(2);
        }

        // The Jobs button (a click through the shell's input) opens the menu.
        TapKey(manager, LifestoneHotkeys.ToolsKey);
        await Frames(4);
        var jobsButton = panel.Window?.GetChildRecursive(ToolsPanel.JobsButtonId) as UiButton;
        if (jobsButton is not null)
        {
            await ClickAsync(this, manager, jobsButton);
        }

        await Frames(4);
        // M6 6 FXU: the press puts the tools panel away (the menu sits just behind it), so the menu shows whole over the shell.
        Check("the-jobs-button-opens-the-automation-menu", jobsButton is { Text: "Jobs" } && panel.JobsPresses == 1 && menu.IsOpen, $"button={jobsButton?.Text ?? "missing"} presses={panel.JobsPresses} open={menu.IsOpen} tools_open={panel.IsOpen} under_the_shell_root={ReferenceEquals(menu.Window?.Parent, gameScreen.Root)}");

        // The rows are the source's: level up (current), the catalog's quests with their experience, errand, then the refused, then stop.
        var rows = Enumerable.Range(0, menu.RowCount).Select(i => (Entry: menu.RowEntry(i), Text: menu.RowText(i) ?? string.Empty)).ToList();
        var quest = rows.FirstOrDefault(r => r.Entry is { Enabled: true, Task.Kind: AutopilotTaskKind.Quest, Experience: > 0 });
        // M6 6 FXU: the xp comes first on a row (a long quest name is cut, never its xp)
        var withXp = rows.Count(r => r.Entry is { Enabled: true, Task.Kind: AutopilotTaskKind.Quest, Experience: > 0 } && r.Text.StartsWith(AutomationMenu.XpText(r.Entry.Experience) + " | ", StringComparison.Ordinal));
        var questRows = rows.Count(r => r.Entry is { Enabled: true, Task.Kind: AutopilotTaskKind.Quest, Experience: > 0 });
        var errand = rows.FindIndex(r => r.Entry is { Enabled: true, Task.Kind: AutopilotTaskKind.Errand });
        var stop = rows.FindIndex(r => r.Entry is { } e && AutomationMenu.IsStop(e));
        Check(
            "the-menu-lists-level-up-the-quests-errand-and-stop",
            rows.Count == source.Jobs.Jobs.Count + source.Jobs.Refused.Count && rows.Count > 0 && rows[0].Text.StartsWith("> xp ", StringComparison.Ordinal) && rows[0].Text.Contains(" | level up", StringComparison.Ordinal) && quest.Entry is not null
                && quest.Text.StartsWith(AutomationMenu.XpText(quest.Entry.Experience) + " | do a quest: ", StringComparison.Ordinal)
                && errand > 0 && stop == rows.Count - 1
                && menu.SummaryText.StartsWith("Level 15", StringComparison.Ordinal),
            $"rows={rows.Count} quest_rows_with_xp={withXp} first='{(rows.Count > 0 ? rows[0].Text : "none")}' quest='{quest.Text}' errand={errand} stop={stop} summary='{menu.SummaryText}' source='{source.Summary}'");

        // Report-only (a new judge, its first gate): every open quest row with an award shows it on screen (M6 6 FXU: the xp first, the label cut when the row is long).
        if (questRows > 0 && withXp == questRows)
        {
            Log.Info(LogCategory, $"menu-quest-row-shows-its-xp (report-only): pass: {withXp} of {questRows} open quest rows with an award show their xp on screen (e.g. '{quest.Text}')");
        }
        else
        {
            Log.Warn(LogCategory, $"menu-quest-row-shows-its-xp (report-only): FAIL: {withXp} of {questRows} open quest rows with an award show their xp on screen (e.g. '{quest.Text}')");
        }

        await Frames(20);
        SaveReviewFrame("11-shell-automation-menu");

        // A click on errand: the task is chosen by the menu and the dial starts it; the answer is the menu's status line and a chat line.
        var talkBefore = chat.TalkSent;
        var errandAt = errand > 0 ? await ChooseByClickAsync(manager, menu, errand) : "no row";

        var started = await WaitFor(() => logic.Mode == AutopilotMode.Running, 5.0);
        await Frames(4);
        Check(
            "choosing-errand-sets-the-task-and-answers-in-the-status-line",
            started && logic.Task == AutopilotTask.Errand && menu.StatusText.StartsWith("Lifestone: the autopilot starts", StringComparison.Ordinal) && mainChat.LogText.Contains("Lifestone: the autopilot starts", StringComparison.Ordinal)
                && source.Current == AutopilotTask.Errand && chat.TalkSent == talkBefore,
            $"clicked={errandAt} mode={logic.Mode} task={logic.Task.Name} status='{menu.StatusText}' current={source.Current.Name} talk_sent={chat.TalkSent - talkBefore}");

        // The inspector (Scroll Lock) names the job and counts the menu's open quests.
        TapKey(manager, LifestoneHotkeys.InspectorKey);
        await Frames(4);
        var open = source.Jobs.Jobs.Count(e => e.Enabled && e.Task.Kind == AutopilotTaskKind.Quest);
        var text = inspector.AutopilotText;
        Check("the-inspector-shows-the-job-line", inspector.IsOpen && text.Contains($"Job: errand | menu: {open} quest(s) open", StringComparison.Ordinal), $"text='{text.Replace('\n', '|')}'");
        TapKey(manager, LifestoneHotkeys.InspectorKey);
        await Frames(2);

        // A click on stop: the /stop path (he stops where he stands); the answer is the status line.
        var stopAt = stop >= 0 ? await ChooseByClickAsync(manager, menu, stop) : "no row";

        var stopped = await WaitFor(() => logic.Mode == AutopilotMode.Interrupted, 5.0);
        await Frames(2);
        Check(
            "the-menus-stop-row-stops-him-and-answers",
            stopped && logic.Interrupted == 1 && !session.Actions.Move.KeyLock.Locked && menu.StatusText.Length > 0 && !menu.StatusText.StartsWith("Lifestone: the autopilot starts", StringComparison.Ordinal),
            $"clicked={stopAt} mode={logic.Mode} interrupted={logic.Interrupted} keys_locked={session.Actions.Move.KeyLock.Locked} status='{menu.StatusText}'");

        // The Jobs button closes it again.
        TapKey(manager, LifestoneHotkeys.ToolsKey);
        await Frames(4);
        if (jobsButton is not null)
        {
            await ClickAsync(this, manager, jobsButton);
        }

        Check("the-jobs-button-closes-the-automation-menu", !menu.IsOpen && panel.JobsPresses == 2, $"open={menu.IsOpen} presses={panel.JobsPresses}");
        TapKey(manager, LifestoneHotkeys.ToolsKey);
        await Frames(2);

        // Back to the host's own state for the checks after this one (the dial's disposal unlocks the keys, D145), as AutopilotDialAsync leaves it.
        host.Logic.Dispose();
        chat.Autopilot = null;
        hotkeys.AutopilotKeyHint = null;
        panel.ToggleAutopilot = null;
        panel.ToggleJobs = null;
        inspector.Autopilot = null;
        inspector.Jobs = null;
        menu.Source = null;
        menu.Choose = null;
        menu.Stop = null;
        _main.RemoveChild(host);
        host.Free();
        Check("the-menu-check-leaves-the-movement-keys-unlocked", !session.Actions.Move.KeyLock.Locked, $"keys_locked={session.Actions.Move.KeyLock.Locked}");
    }

    /// <summary>
    /// A row chosen by a mouse click through the shell's input (the person's path; M6 6 FXU: the menu hangs under the shell's root, so its rows take the click). Report-only <c>menu-row-click</c> (a new judge, its first gate) says
    /// whether the click chose the row (the menu's status line moved) with what the mouse hit; nothing chooses it another way, so the judged checks after it read the click's own result.
    /// </summary>
    private async Task<string> ChooseByClickAsync(UiElementManager manager, AutomationMenu menu, int index)
    {
        var before = menu.StatusText;
        var rowsBefore = menu.Rebuilds;
        var at = await ClickRowAsync(manager, menu, index);
        if (!string.Equals(menu.StatusText, before, StringComparison.Ordinal))
        {
            Log.Info(LogCategory, $"menu-row-click (report-only): pass: row {index} chosen by a click at {at}");
            return "click " + at;
        }

        Log.Warn(LogCategory, $"menu-row-click (report-only): FAIL: a click on row {index} at {at} chose nothing (rebuilds {rowsBefore}->{menu.Rebuilds})");
        return "click chose nothing: " + at;
    }

    /// <summary>
    /// Clicks row <paramref name="index"/> of the open menu through the shell's input (the person's choice): the list scrolls it into view, the point is the list's own hit test for that row (<see cref="UiListBox.TryGetItemIndexAt"/>,
    /// the one its mouse-down selects by), then a left click there. Returns where it clicked, or why it could not.
    /// </summary>
    private async Task<string> ClickRowAsync(UiElementManager manager, AutomationMenu menu, int index)
    {
        if (menu.Window?.GetChildRecursive(AutomationMenu.ListId) is not UiListBox list)
        {
            return "no list";
        }

        list.ScrollToShow(index);
        await Frames(2);
        var box = list.ScreenBox;
        var x = (box.X1 - box.X0) / 2;
        int? first = null, last = null;
        for (var y = 0; y < box.Y1 - box.Y0; y++)
        {
            if (list.TryGetItemIndexAt(x, y, out var at) && at == index)
            {
                first ??= y;
                last = y;
            }
        }

        if (first is not { } top || last is not { } bottom)
        {
            return $"row {index} not under the list ({box.X0},{box.Y0})-({box.X1},{box.Y1})";
        }

        var (sx, sy) = (box.X0 + x, box.Y0 + ((top + bottom) / 2));
        var chain = new List<string>();
        for (var e = manager.HitTest(sx, sy); e is not null && chain.Count < 6; e = e.Parent)
        {
            chain.Add(e.ElementId.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        }

        manager.Input.MouseMove(sx, sy);
        await Frames(2);
        manager.Input.MouseMove(sx, sy);
        manager.Input.MouseButton(UiMouseButton.Left, true);
        manager.Input.MouseButton(UiMouseButton.Left, false);
        await Frames(2);
        return $"({sx},{sy}) list_mouse_over={list.IsMouseOver} hit=[{string.Join("<", chain)}]";
    }
}

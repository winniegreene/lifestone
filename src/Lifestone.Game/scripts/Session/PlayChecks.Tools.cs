using Godot;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.LifestonePanels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Contracts;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// The play checks of Lifestone's tools panel (M2 G7 lane A; docs/retail-ui.md section 22.1): the panel is opened with F7, searched by typing, a place is picked with a click (its
/// <c>place poi</c> request is answered by the stand-in, a placeholder server, with the teleport and portal space), then Where am I and Add XP are pressed (the stand-in answers those
/// too, with placeholder values), and the panel is closed with F7. Everything goes through the shell's own input router, the panel's buttons and the Action API.
/// </summary>
public sealed partial class PlayChecks
{
    /// <summary>Clicks the middle of <paramref name="element"/> through the shell's input and waits two frames.</summary>
    private static async Task ClickAsync(PlayChecks checks, UiElementManager manager, UiElement element)
    {
        var box = element.ScreenBox;
        var x = (box.X0 + box.X1) / 2;
        var y = (box.Y0 + box.Y1) / 2;
        manager.Input.MouseMove(x, y);
        await checks.Frames(2);
        manager.Input.MouseMove(x, y);
        manager.Input.MouseButton(UiMouseButton.Left, true);
        manager.Input.MouseButton(UiMouseButton.Left, false);
        await checks.Frames(2);
    }

    private static void TapKey(UiElementManager manager, int dik)
    {
        manager.Input.KeyDown(dik);
        manager.Input.KeyUp(dik);
    }

    /// <summary>F7, a typed search, a click on the first row: returns the teleport the panel asked for, or null.</summary>
    private async Task<ToolHandle<PlaceResult>?> ToolsPanelPlaceAsync(RetailUiLayer? ui)
    {
        if (ui?.Manager is not { } manager || ui.Screens?.Flow.CurrentScreen is not GamePlayScreen { ToolsUi: { } panel, Hotkeys: { } hotkeys })
        {
            Check("the-tools-panel-is-in-the-shell-unbuilt-and-its-keys-are-registered", false, "the shell has no tools panel");
            return null;
        }

        Check("the-tools-panel-is-in-the-shell-unbuilt-and-its-keys-are-registered", panel.Window is null && !panel.IsOpen && manager.Input.InputMaps.Any(m => m.InputMapId == LifestoneHotkeys.InputMap), $"window_made_before_the_first_open={panel.Window is not null} open={panel.IsOpen}");

        TapKey(manager, LifestoneHotkeys.ToolsKey);
        await Frames(4);
        Check("f7-opens-the-tools-panel-and-focuses-its-search-field", panel.IsOpen && hotkeys.ToolsPresses == 1 && ReferenceEquals(manager.FocusElement, panel.SearchField), $"open={panel.IsOpen} presses={hotkeys.ToolsPresses} focus={manager.FocusElement?.GetType().Name ?? "none"}");

        // p2: the throwaway leg's driver does the typing and the click (--login-check=throwaway presses the same place the same way).
        var pressPlace = new ThrowawayScreens(ui, ui.Session!).PressPlace("holtburg");
        var pressed = await RunOp(ui.Session!, pressPlace, 15);
        Check(
            "typing-in-the-search-field-lists-holtburg-first",
            pressed && pressPlace.Succeeded && panel.Model.Query == "holtburg" && panel.Rows[0]!.Visible && panel.Rows[0]!.Text == "Holtburg" && pressPlace.Text == "Holtburg" && panel.Model.Results.Count >= 2,
            $"failure={pressPlace.Failure} query='{panel.Model.Query}' first='{panel.Rows[0]!.Text}' results={panel.Model.Results.Count}");
        var place = panel.Model.LastPlace;
        Check("a-click-on-a-place-asks-the-server-to-teleport-through-the-action-api", place is { Source: ActionSource.Ui, Kind: ActionKind.ToolPlace }, $"handle={(place is null ? "none" : place.ToString())}");

        // The panel goes away with F7 (the focused search field does not swallow the key), so the portal tunnel is the only thing over the world.
        TapKey(manager, LifestoneHotkeys.ToolsKey);
        await Frames(2);
        Check("f7-closes-the-tools-panel-again", !panel.IsOpen && hotkeys.ToolsPresses == 2 && !ReferenceEquals(manager.FocusElement, panel.SearchField), $"open={panel.IsOpen} presses={hotkeys.ToolsPresses}");
        return place;
    }

    /// <summary>Where am I and Add XP through the panel, the use trail in the chat and the action log, and the panel's review frame.</summary>
    private async Task ToolsPanelWhereAndXpAsync(IGameSessionView session, RetailUiLayer? ui)
    {
        if (ui?.Manager is not { } manager || ui.Screens?.Flow.CurrentScreen is not GamePlayScreen { ToolsUi: { Window: { } window } panel, Hotkeys: { } hotkeys, Inspector: { } inspector } shell)
        {
            Check("the-tools-panel-answers-where-am-i-and-add-xp", false, "the shell has no tools panel");
            return;
        }

        TapKey(manager, LifestoneHotkeys.ToolsKey);
        await Frames(4);
        Check("f7-opens-the-tools-panel-again", panel.IsOpen, $"open={panel.IsOpen}");

        await ClickAsync(this, manager, window.GetChildRecursive(ToolsPanel.WhereButtonId)!);
        var where = panel.Model.LastWhere;
        var whereDone = where is not null && await WaitFor(() => where.IsCompleted, 10.0);
        await Frames(3);
        var chat = shell.Chat?.Main?.LogText ?? string.Empty;
        Check(
            "where-am-i-compares-the-servers-position-with-the-physics-body-in-the-panel-and-the-chat",
            whereDone && where!.Outcome?.Kind == ActionOutcomeKind.Confirmed && panel.Model.LastAgreement is { Agrees: true, Client: not null } && panel.Model.WhereText.StartsWith("server: 0x", StringComparison.Ordinal)
                && panel.Model.WhereText.Contains("client body", StringComparison.Ordinal) && chat.Contains("agrees within 0.5 m", StringComparison.Ordinal),
            $"outcome={where?.Outcome?.Kind} agreement={panel.Model.LastAgreement?.Agrees} metres={panel.Model.LastAgreement?.Metres:0.00} text='{panel.Model.WhereText.Replace('\n', '|')}' chat_has_agreement={chat.Contains("agrees within 0.5 m", StringComparison.Ordinal)}");

        panel.AmountField!.SetText("2500");
        await ClickAsync(this, manager, window.GetChildRecursive(ToolsPanel.AddXpButtonId)!);
        var xp = panel.Model.LastXp;
        var xpDone = xp is not null && await WaitFor(() => xp.IsCompleted, 10.0);
        await Frames(3);
        chat = shell.Chat?.Main?.LogText ?? string.Empty;
        Check(
            "add-xp-is-confirmed-by-the-server-in-the-chat",
            xpDone && xp!.Outcome?.Kind == ActionOutcomeKind.Confirmed && xp.Result is { Added: 2500 } && chat.Contains("added 2,500 experience", StringComparison.Ordinal) && panel.Model.Status.StartsWith("Added 2,500 experience", StringComparison.Ordinal),
            $"outcome={xp?.Outcome?.Kind} added={xp?.Result?.Added} status='{panel.Model.Status}' chat_has_reply={chat.Contains("added 2,500 experience", StringComparison.Ordinal)}");

        var log = session.ActionLog;
        var uses = Enumerable.Range(0, log.Count).Select(i => log[i]).Where(e => e.Source == ActionSource.Ui && e.Kind is ActionKind.ToolPlace or ActionKind.ToolWhereAmI or ActionKind.ToolAddXp).Select(e => e.Kind).Distinct().ToArray();
        Check("every-use-of-the-panel-is-in-the-action-log-as-a-ui-action", uses.Length == 3, $"kinds=[{string.Join(", ", uses)}]");

        // Scroll Lock toggles lane B's automation inspector (wired in GamePlayScreen): it opens, then Scroll Lock closes it again so the tools frame and the inspector's own checks start clean.
        TapKey(manager, LifestoneHotkeys.InspectorKey);
        await Frames(2);
        Check("scroll-lock-opens-the-inspector", hotkeys.InspectorPresses == 1 && hotkeys.OpenInspector is not null && inspector.IsOpen, $"presses={hotkeys.InspectorPresses} wired={hotkeys.OpenInspector is not null} open={inspector.IsOpen}");
        TapKey(manager, LifestoneHotkeys.InspectorKey);
        await Frames(2);
        Check("scroll-lock-closes-the-inspector-again", hotkeys.InspectorPresses == 2 && !inspector.IsOpen, $"presses={hotkeys.InspectorPresses} open={inspector.IsOpen}");

        await Frames(20);
        SaveReviewFrame("10-shell-tools-panel");
        TapKey(manager, LifestoneHotkeys.ToolsKey);
        await Frames(2);
        Check("f7-closes-the-tools-panel-after-the-uses", !panel.IsOpen && hotkeys.ToolsPresses == 4, $"open={panel.IsOpen} presses={hotkeys.ToolsPresses}");
    }
}

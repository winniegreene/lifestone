using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Toolbar;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Game.Sessions;

// M4 3.2 h2: the hotbar row. The only hook is one call in CheckAllAsync after the heal rows (the replay visit's player holds the kit and the five potions; the heal rows leave its vital full, so the drink here is a wasted one: Confirmed all the same).
// The row drives the REAL input router with mouse and key events (the path Godot's events take) and judges the session's state: the shortcut model, the stack and the action's outcome, never the stand-in's counters.
public sealed partial class PlayChecks
{
    /// <summary>
    /// M4 3.2 h2, <c>hotbar</c>: with the inventory page open, a Potion of Healing and a Handy Healing Kit are dragged from the pack onto the hotbar's slots 1 and 2 (the session's shortcut model has them there, the sends completing <c>Sent</c>); the page is
    /// closed and the review frame <c>hotbar</c> is taken (<see cref="PanelFramesPlan.HotbarFrame"/>, with --play-frames); the key 1 is pressed and drinks the potion (the slot shows the cooldown overlay until the drink completes, the drink is Confirmed and the stack is
    /// one less); the kit's slot is dragged off the bar onto the world view, which removes its shortcut and moves nothing. Offline: it proves the UI runtime's drag and key path and the client's sends against the stand-in, whose list the next description would show;
    /// ACE's acceptance is <c>ShortcutLiveTests</c>'.
    /// </summary>
    private async Task HotbarChecksAsync(IGameSessionView session)
    {
        const string name = "hotbar";
        var shell = _ui?.Screens?.Flow.CurrentScreen as GamePlayScreen;
        var hotbar = shell?.ToolbarUi?.Hotbar;
        var page = shell?.PanelControllers?.ControllersOf(GameInventoryPage.Panel).OfType<GameInventoryPage>().FirstOrDefault();
        if (shell is null || hotbar is null || page is null || _ui?.Manager is not { } manager)
        {
            Check(name, false, $"shell={shell is not null} hotbar={hotbar is not null} inventory_controller={page is not null} ui_manager={_ui?.Manager is not null}");
            return;
        }

        var input = manager.Input;
        var bar = session.State.Self.Shortcuts;
        var inventory = session.State.Self.Inventory;
        var potion = HealPackItem(session, HealPotionWcid);
        var kit = HealPackItem(session, HealKitWcid);
        if (potion == 0 || kit == 0 || bar[0].IsItem || bar[1].IsItem)
        {
            Check(name, false, $"setup: potion_stack={potion != 0} kit={kit != 0} slot1_holds={bar[0].ObjectId:X8} slot2_holds={bar[1].ObjectId:X8} (the replay visit's player holds the kit and the potions: HealItems; its bar starts empty)");
            return;
        }

        var opened = await OpenPanelAsync(shell, GameInventoryPage.Panel, 14);
        page.ShowContents(inventory.Owner != 0 ? inventory.Owner : session.State.Self.Guid);
        var filled = await WaitFor(() => page.PackList.OccupiedCount > 0 && page.PackList.Items.OfType<UiItem>().All(static c => !c.IconPending), 10.0);
        var listBox = page.PackList.ScreenBox;
        UiItem? CellInView(uint item)
        {
            var cell = page.PackList.FindItem(item);
            if (cell is null)
            {
                return null;
            }

            var c = DragCenter(cell);
            return listBox.X0 <= c.X && c.X < listBox.X1 && listBox.Y0 <= c.Y && c.Y < listBox.Y1 && ReferenceEquals(manager.HitTest(c.X, c.Y), cell) ? cell : null;
        }

        var potionCell = CellInView(potion);
        var kitCell = CellInView(kit);
        if (!(opened && filled && potionCell is not null && kitCell is not null))
        {
            Check(name, false, $"setup: page_opened={opened} filled={filled} potion_cell_in_view={potionCell is not null} kit_cell_in_view={kitCell is not null} pack_cells={page.PackList.OccupiedCount} pack_list={listBox.X0},{listBox.Y0}-{listBox.X1},{listBox.Y1}");
            await ClosePanelAsync(shell, GameInventoryPage.Panel);
            return;
        }

        // --- the two drags: the item's cell to the slot's cell ---
        var stackBefore = HealStackOf(session, potion);
        async Task<ActionHandle?> DragOntoAsync(UiItem from, int slot)
        {
            var sendsBefore = hotbar.Sends;
            var start = DragCenter(from);
            var end = DragCenter(hotbar.CellOf(slot)!);
            input.MouseMove(start.X, start.Y);
            input.MouseButton(UiMouseButton.Left, true);
            input.MouseMove(start.X + 14, start.Y - 6);
            input.MouseMove(end.X, end.Y);
            await Frames(2);
            input.MouseButton(UiMouseButton.Left, false);
            await Frames(2);
            var handle = hotbar.Sends == sendsBefore + 1 ? hotbar.LastAction : null;
            if (handle is not null)
            {
                await WaitFor(() => handle.IsCompleted, 10.0);
            }

            return handle;
        }

        var addPotion = await DragOntoAsync(potionCell!, 0);
        var addKit = await DragOntoAsync(kitCell!, 1);
        var onBar = await WaitFor(() => bar[0].ObjectId == potion && bar[1].ObjectId == kit, 5.0);
        var sent = addPotion is { Kind: ActionKind.AddShortcut, Source: ActionSource.Ui } && addKit is { Kind: ActionKind.AddShortcut, Source: ActionSource.Ui }
            && addPotion.Outcome?.Kind == ActionOutcomeKind.Sent && addKit.Outcome?.Kind == ActionOutcomeKind.Sent;
        await ClosePanelAsync(shell, GameInventoryPage.Panel);
        await Frames(4);
        var slots = hotbar.CellOf(0) is { } c0 && hotbar.CellOf(1) is { } c1 && c0.ItemId == potion && c1.ItemId == kit && c0.ShortcutNumber == 0 && c1.ShortcutNumber == 1 && c0.ShortcutNumberImage != c1.ShortcutNumberImage;

        // --- the review frame, with --play-frames: the two items in their slots and the numbers ---
        if (slots && _framesDirectory is not null)
        {
            HideLightsHud(); // the developer HUD would cover the toolbar; the shell's own windows stay
            await Frames(4);
            SaveExtraFrame(PanelFramesPlan.HotbarFrame);
            RestoreLightsHud();
        }

        // --- the key 1: a drink through UseShortcut, the cooldown overlay while it runs ---
        var potionCellNow = hotbar.CellOf(0);
        var usesBefore = hotbar.Uses;
        var pressed = input.KeyDown(DikScanCodes.D1);
        input.KeyUp(DikScanCodes.D1);
        var use = hotbar.LastUse;
        var overlayShown = potionCellNow is { CooldownPhase: UiItem.CooldownSteps } && hotbar.IsBusy(0);
        if (overlayShown && _framesDirectory is not null)
        {
            HideLightsHud();
            await Frames(3);
            SaveExtraFrame(PanelFramesPlan.HotbarCooldownFrame); // review only: slot 1 while the drink runs
            RestoreLightsHud();
        }

        var drank = use is not null && await WaitFor(() => use.IsCompleted, HealWaitSeconds);
        await Frames(2);
        var stackAfter = HealStackOf(session, potion);
        var overlayGone = potionCellNow is { CooldownPhase: 0 } && !hotbar.IsBusy(0);
        var keyOk = pressed && hotbar.Uses == usesBefore + 1 && use is { Kind: ActionKind.Drink, Source: ActionSource.Ui } && drank && use.Outcome?.Kind == ActionOutcomeKind.Confirmed && stackAfter == stackBefore - 1;

        // --- the kit's slot dragged off the bar onto the world view: its shortcut goes, nothing else is sent ---
        var removeStart = hotbar.Sends;
        var kitStart = hotbar.CellOf(1) is { } kc ? DragCenter(kc) : (X: 0, Y: 0);
        input.MouseMove(kitStart.X, kitStart.Y);
        input.MouseButton(UiMouseButton.Left, true);
        input.MouseMove(kitStart.X - 14, kitStart.Y - 40);
        input.MouseMove(120, 120);
        await Frames(2);
        input.MouseButton(UiMouseButton.Left, false);
        await Frames(2);
        var remove = hotbar.Sends == removeStart + 1 ? hotbar.LastAction : null;
        var removeDone = remove is not null && await WaitFor(() => remove.IsCompleted, 10.0);
        var gone = await WaitFor(() => !bar[1].IsItem, 5.0);
        var kitStays = inventory.Contains(kit);
        var removed = remove is { Kind: ActionKind.RemoveShortcut, Source: ActionSource.Ui } && removeDone && gone && kitStays && hotbar.CellOf(1) is { IsEmpty: true };

        Check(
            name,
            opened && filled && sent && onBar && slots && keyOk && overlayShown && overlayGone && removed,
            $"potion=0x{potion:X8} kit=0x{kit:X8} drags_sent={sent} on_the_bar={onBar} slots_drawn_with_numbers={slots} key_taken={pressed} drink={use?.Outcome?.Kind.ToString() ?? "pending"} ({use?.Outcome?.Detail}) overlay_while_busy={overlayShown} overlay_cleared={overlayGone} stack {stackBefore} -> {stackAfter} " +
            $"kit_slot_dragged_off: remove={remove?.Outcome?.Kind.ToString() ?? "none"} slot_empty={gone} kit_still_held={kitStays}");

        // Put the bar back as the rows after this see it: both slots empty (the stand-in's list too).
        for (var slot = 0; slot < 2; slot++)
        {
            if (bar[slot].IsItem)
            {
                var back = session.Actions.RemoveShortcut(slot, ActionSource.Host);
                await WaitFor(() => back.IsCompleted, 10.0);
            }
        }

        Log.Info(LogCategory, "hotbar row done", ("uses", hotbar.Uses), ("sends", hotbar.Sends), ("drag_dropped", manager.Input.Drag.Dropped));
    }
}

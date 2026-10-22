using Lifestone.Game.Core;
using Lifestone.Client.RetailUi;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;

namespace Lifestone.Game.Sessions;

// M4 2.2 s3: the drag row. The only hook is one call in CheckAllAsync after the container rows. The row drives the REAL input router with mouse events (the path Godot's events take: RetailUiInput -> UiInputRouter) and judges the
// session's state (the item sits in the side pack in the inventory model), never the stand-in's counters.
public sealed partial class PlayChecks
{
    /// <summary>
    /// M4 2.2 s3, <c>drag-item-to-side-pack</c>: with the inventory page open, a press on a stand-in item in the pack list, a few pixels of movement and Escape cancel the drag and change nothing (the item is still in the main pack); then a
    /// press, a drag to the first side pack's cell and a release moves the item through the Action API (<see cref="ActionSource.Ui"/>, Confirmed), and the session's inventory model has the item in that side pack. Offline: it proves the
    /// UI runtime's drag and the client's side, and the stand-in's 0022 in the live order, not ACE's acceptance (the live tests do that).
    /// </summary>
    private async Task DragChecksAsync(IGameSessionView session)
    {
        const string name = "drag-item-to-side-pack";
        var shell = _ui?.Screens?.Flow.CurrentScreen as GamePlayScreen;
        var page = shell?.PanelControllers?.ControllersOf(GameInventoryPage.Panel).OfType<GameInventoryPage>().FirstOrDefault();
        if (shell is null || page is null || _ui?.Manager is not { } manager)
        {
            Check(name, false, $"shell={shell is not null} inventory_controller={page is not null} ui_manager={_ui?.Manager is not null}");
            return;
        }

        var input = manager.Input;
        var drag = input.Drag;
        var inventory = session.State.Self.Inventory;
        var opened = await OpenPanelAsync(shell, GameInventoryPage.Panel, 14);
        page.ShowContents(inventory.Owner != 0 ? inventory.Owner : session.State.Self.Guid);
        var filled = await WaitFor(() => page.PackList.OccupiedCount > 0 && page.SidePackList.Items.OfType<UiItem>().Any(static c => !c.IsEmpty) && page.PackList.Items.OfType<UiItem>().All(static c => !c.IconPending), 10.0);

        // The item is the first one the pack list shows in its visible part (the stand-in's pack holds more than the list's 18 visible cells, so a new item's cell may be scrolled out of sight).
        var listBox = page.PackList.ScreenBox;
        var itemCell = page.PackList.Items.OfType<UiItem>().FirstOrDefault(c => !c.IsEmpty && !c.IconPending && listBox.X0 <= DragCenter(c).X && DragCenter(c).X < listBox.X1 && listBox.Y0 <= DragCenter(c).Y && DragCenter(c).Y < listBox.Y1);
        var item = itemCell?.ItemId ?? 0u;
        var packCell = page.SidePackList.Items.OfType<UiItem>().FirstOrDefault(static c => !c.IsEmpty);
        var hitCell = itemCell is not null && ReferenceEquals(manager.HitTest(DragCenter(itemCell).X, DragCenter(itemCell).Y), itemCell);
        var hitPack = packCell is not null && ReferenceEquals(manager.HitTest(DragCenter(packCell).X, DragCenter(packCell).Y), packCell);
        if (!(opened && filled && itemCell is not null && packCell is not null && hitCell && hitPack && inventory.Find(item) is { Kind: InventoryPlaceKind.MainPack }))
        {
            var seen = itemCell is null ? null : manager.HitTest(DragCenter(itemCell).X, DragCenter(itemCell).Y);
            Check(name, false, $"setup: page_opened={opened} filled={filled} item_cell={itemCell is not null} side_pack_cell={packCell is not null} pointer_finds_item_cell={hitCell} pointer_finds_pack_cell={hitPack} item_place={inventory.Find(item).Kind} under_pointer={seen?.GetType().Name}:0x{seen?.ElementId:X8} pack_list={listBox.X0},{listBox.Y0}-{listBox.X1},{listBox.Y1} cells={page.PackList.OccupiedCount}");
            await ClosePanelAsync(shell, GameInventoryPage.Panel);
            return;
        }

        var pack = packCell.ItemId;
        var from = DragCenter(itemCell);
        var to = DragCenter(packCell);

        // --- Escape: the drag begins (a few pixels, the item's icon at the pointer) and ends with nothing sent ---
        var cancelledBefore = drag.Cancelled;
        input.MouseMove(from.X, from.Y);
        input.MouseButton(UiMouseButton.Left, true);
        input.MouseMove(from.X + 3, from.Y); // under the threshold: still a click
        var armed = drag.Phase == UiDragPhase.Pressed && drag.ItemId == item;
        input.MouseMove(from.X + 14, from.Y + 6);
        await Frames(2);
        var carried = drag.Phase == UiDragPhase.Dragging && ReferenceEquals(input.DragElement, itemCell) && drag.Icon is { Visible: true } icon && icon.ItemId == item;
        var escaped = input.KeyDown(DikScanCodes.Escape);
        input.MouseButton(UiMouseButton.Left, false);
        await Frames(2);
        var stayed = inventory.Find(item) is { Kind: InventoryPlaceKind.MainPack };
        var cancelled = drag.Phase == UiDragPhase.Idle && drag.Cancelled == cancelledBefore + 1 && drag.LastEnd == UiDragEnd.Cancelled && drag.Icon is { Visible: false };

        // --- the drag to the side pack: press, move over its cell (the cell shows the accept frame), release ---
        var droppedBefore = drag.Dropped;
        input.MouseMove(from.X, from.Y);
        input.MouseButton(UiMouseButton.Left, true);
        input.MouseMove(from.X + 14, from.Y + 6);
        input.MouseMove(to.X, to.Y);
        await Frames(2);
        var hover = ReferenceEquals(drag.HoverCell, packCell) && packCell.DragRollover == UiDragRollover.Accept && packCell.DragAcceptFrame is { Visible: true };
        input.MouseButton(UiMouseButton.Left, false);
        var move = page.SidePackDrop.LastAction;
        var confirmed = move is not null && await WaitFor(() => move.IsCompleted, 15.0) && move.Outcome?.Kind == ActionOutcomeKind.Confirmed;
        var inPack = await WaitFor(() => inventory.Find(item) is { Kind: InventoryPlaceKind.InSidePack } place && place.Container == pack, 10.0);
        var dropped = drag.Dropped == droppedBefore + 1 && drag.LastEnd == UiDragEnd.Dropped && move is { Source: ActionSource.Ui, Kind: ActionKind.MoveToPack };
        await Frames(2);
        var highlightGone = packCell.DragRollover == UiDragRollover.None && drag.Icon is { Visible: false };
        Check(
            name,
            armed && carried && escaped && stayed && cancelled && hover && dropped && confirmed && inPack && highlightGone,
            $"item=0x{item:X8} pack=0x{pack:X8} armed={armed} phase={drag.Phase} carried={carried} escape_taken={escaped} stayed_in_main_pack_after_escape={stayed} cancelled={cancelled} hover_accept={hover} dropped={dropped} outcome={move?.Outcome?.Kind.ToString() ?? "pending"} ({move?.Outcome?.Detail}) in_side_pack={inPack} place={inventory.Find(item).Kind} highlight_gone={highlightGone}");

        // Put the item back and close the page, so the rows after this see the pack as the earlier rows left it.
        if (inPack)
        {
            var back = session.Actions.MoveToPack(item, 0, 0, ActionSource.Host); // the item was the pack's first cell: back at the first place
            await WaitFor(() => back.IsCompleted, 10.0);
        }

        await ClosePanelAsync(shell, GameInventoryPage.Panel);
        Log.Info(LogCategory, "drag row done", ("item", $"0x{item:X8}"), ("pack", $"0x{pack:X8}"), ("started", drag.Started), ("dropped", drag.Dropped), ("cancelled", drag.Cancelled));
    }

    private static (int X, int Y) DragCenter(UiElement element)
    {
        var box = element.ScreenBox;
        return ((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
    }
}

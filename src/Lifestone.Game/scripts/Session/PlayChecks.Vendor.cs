using System.Numerics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Data.World;
using Lifestone.Game.Diagnostics;

namespace Lifestone.Game.Sessions;

// M4 3.2 s4b: the vendor window rows. The only hook is one call in CheckAllAsync, after the heal rows in the run's second session (the replay visit holds the stand-in's vendors: ReplayVisit.Vendors, StandInVisit). Each row judges the
// session's state (the Action API's outcome, GameState.Vendor, the pack and the purse in the inventory model, the window's lists), never the stand-in's counters. The vendors are the only NPCs the stand-in adds (D23): Thelnoth Cort the
// Healer stands at his real cell, a labelled placeholder (TODO(m4-standin-vendor-model)); no creature is added.
public sealed partial class PlayChecks
{
    /// <summary>Thelnoth Cort the Healer's guid in the stand-in (<c>StandInWorld.ThelnothGuid</c>, internal to the client assembly) and his shop's cell.</summary>
    private const uint VendorThelnothGuid = 0x7A9B4027, VendorThelnothCell = 0xA9B40111;

    /// <summary>The stand-in player's Handy Healing Kit and Leather Bracers (<c>StandInWorld.SellKitGuid</c> and <c>SellBracersGuid</c>, internal to the client assembly).</summary>
    private const uint VendorSellKitGuid = 0x80080001, VendorSellBracersGuid = 0x80080002;

    /// <summary>The weenie class of the Handy Healing Kit.</summary>
    private const uint VendorKitWcid = 628;

    /// <summary>
    /// M4 3.2 s4b, on the replay visit's player (the stand-in's vendors, 10,000 pyreals, a kit and Leather Bracers in the pack): the player is put in Thelnoth's shop (<c>place loc</c>, <c>TODO(m4-vendor-approach)</c>), the Action API opens him and the
    /// window shows (<c>vendor-panel-opens</c>: his name, the stock rows, the selected row's predicted price), the Buy button buys the selected Handy Healing Kit (<c>vendor-panel-buy</c>: Confirmed, the purse down by the predicted price, the
    /// kit in the pack; the review frame <c>vendor-buy-sell</c> is taken with the window open after it) and a kit dragged into the sell list is sold by the Sell button (<c>vendor-panel-sell</c>: Leather Bracers refused with retail's text, the
    /// sale Confirmed, the purse up by the predicted payout, the list and its marks cleared). Offline they prove the client's side and the stand-in's answers in the live order of cache/m4/3-2/capture.md (C1), not ACE's acceptance (the
    /// loop's live rows do that).
    /// </summary>
    private async Task VendorChecksAsync(IGameSessionView session)
    {
        const string opens = "vendor-panel-opens";
        const string buyName = "vendor-panel-buy";
        const string sellName = "vendor-panel-sell";
        var shell = _ui?.Screens?.Flow.CurrentScreen as GamePlayScreen;
        var window = shell?.Root?.GetChildRecursive(GameVendorPanel.WindowId) as GameVendorPanel;
        if (shell is null || window is null || _ui?.Manager is not { } manager)
        {
            Check(opens, false, $"shell={shell is not null} vendor_window={window is not null} ui_manager={_ui?.Manager is not null}");
            return;
        }

        // --- into the shop, then the open ---
        var destination = new TeleportDestination(
            "Thelnoth's shop",
            [],
            new WorldLocation("Thelnoth's shop", new CellPosition(VendorThelnothCell, new Vector3(30.02f, 157.9f, 66.005f)), HasHeight: true, HeadingDegrees: 0),
            "play-check m4 3.2 s4b (TODO(m4-vendor-approach): the walk through the door is not built)");
        var place = session.Tools.Place(destination, ActionSource.Host);
        var arrived = await WaitFor(() => place.IsCompleted && session.State.Self.Travel is SelfTravel.TeleportArrived or SelfTravel.Present && session.State.Self.Position is { } p && p.CellId == VendorThelnothCell, 30.0);
        await WaitFor(() => _mode.Flow.Stage == Lifestone.Client.Play.PlayStage.InWorld, 30.0);
        var tunnelGone = await WaitFor(() => _portal is { Director.Machine.TunnelVisible: false }, 25.0); // the review frame shows the shop, not portal space (not judged: the frame's view only)
        var vendorHere = await WaitFor(() => session.State.Objects.TryGet(VendorThelnothGuid, out _), 10.0);
        var open = session.Actions.OpenVendor(VendorThelnothGuid, ActionSource.Host);
        var openConfirmed = await WaitFor(() => open.IsCompleted, 20.0) && open.Outcome?.Kind == ActionOutcomeKind.Confirmed;
        var drawn = openConfirmed && await WaitFor(() => window.Drawn && (window.ShopRow?.Items.OfType<UiItem>().All(static c => !c.IconPending) ?? false), 15.0);
        var stock = session.State.Vendor;
        var coinsBefore = session.State.Self.Qualities.CoinValue;
        var kitRow = stock?.Entries.FirstOrDefault(static e => e.WeenieClassId == VendorKitWcid);
        var predicted = kitRow is null || stock is null ? 0u : stock.PriceToBuy(kitRow, 1);
        Check(
            opens,
            arrived && vendorHere && openConfirmed && drawn && stock is not null && window.Title == "Thelnoth Cort the Healer (stand-in placeholder)" && window.ShopRow!.OccupiedCount == stock.Entries.Length && kitRow is not null && predicted > 0
                && window.OpenTabId == GameVendorPanel.ItemsTabId && window.Parent?.Visible == true && window.ShopRow.SelectedItemId == stock.Entries[0].Guid,
            $"arrived={arrived} (place {place.Outcome?.Kind}; cell 0x{session.State.Self.Position?.CellId ?? 0:X8} travel {session.State.Self.Travel}; tunnel_gone={tunnelGone}) vendor_object={vendorHere} open_outcome={open.Outcome?.Kind.ToString() ?? "pending"} ({open.Outcome?.Detail}) drawn={drawn} title='{window.Title}' rows={window.ShopRow?.OccupiedCount}/{stock?.Entries.Length} empty_cells={window.ShopRow?.EmptyCount} kit_price={predicted} coins={coinsBefore} tab=0x{window.OpenTabId:X8} cost_line='{window.ItemCostText}'");
        if (!(drawn && kitRow is not null && stock is not null))
        {
            return;
        }

        // --- the Buy button buys the selected Handy Healing Kit ---
        var kitsBefore = KitsInPack(session);
        var cell = window.ShopRow!.FindItem(kitRow.Guid);
        var clickedRow = cell is not null && ClickVendorElement(manager, cell);
        await Frames(3);
        var selected = window.ShopRow.SelectedItemId == kitRow.Guid && window.ItemNameText == "Handy Healing Kit";
        var button = window.GetChildRecursive(GameVendorPanel.BuyNowButtonId);
        var clickedBuy = button is not null && ClickVendorElement(manager, button);
        var buy = window.LastBuy;
        var buyDone = buy is not null && await WaitFor(() => buy.IsCompleted, 20.0);
        var coinsAfterBuy = await WaitFor(() => session.State.Self.Qualities.CoinValue != coinsBefore, 5.0) ? session.State.Self.Qualities.CoinValue : coinsBefore;
        var kitsAfter = await WaitFor(() => KitsInPack(session) == kitsBefore + 1, 5.0) ? kitsBefore + 1 : KitsInPack(session);
        var redrawn = await WaitFor(() => window.Drawn && window.ItemCostText.Contains(coinsAfterBuy.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal), 10.0);
        Check(
            buyName,
            clickedRow && selected && clickedBuy && buyDone && buy!.Outcome?.Kind == ActionOutcomeKind.Confirmed && buy.Source == ActionSource.Ui && coinsBefore - coinsAfterBuy == predicted && kitsAfter == kitsBefore + 1 && redrawn && window.Visible,
            $"clicked_row={clickedRow} selected={selected} clicked_buy={clickedBuy} outcome={buy?.Outcome?.Kind.ToString() ?? "pending"} ({buy?.Outcome?.Detail}) coins {coinsBefore} -> {coinsAfterBuy} (predicted -{predicted}) kits {kitsBefore} -> {kitsAfter} cost_line='{window.ItemCostText}' window_visible={window.Visible}");

        // --- the review frame `vendor-buy-sell` (with --play-frames): the window open after the buy, the first row selected, the purse down. PLACEHOLDER: the stand-in's Thelnoth and shop (TODO(m4-standin-vendor-model)). Review only, never judged. ---
        if (_framesDirectory is not null && redrawn)
        {
            HideLightsHud(); // the developer HUD would cover the window; the shell's own windows stay
            await Frames(4);
            SaveExtraFrame(PanelFramesPlan.VendorBuySellFrame);
            RestoreLightsHud();
        }

        // --- the sell list: Leather Bracers are refused (Thelnoth's mask 0x44080 has no armour), a kit is dropped in and the Sell button sells it ---
        var refused = window.AddToSell(VendorSellBracersGuid);
        var refusalText = window.Status;
        var listed = window.AddToSell(VendorSellKitGuid);
        await Frames(3);
        var sellCoinsBefore = session.State.Self.Qualities.CoinValue;
        var kitCell = window.SellRow?.FindItem(VendorSellKitGuid);
        var marked = kitCell is { HasSellMark: true, SellMarkShown: true };
        var payout = window.SellTotal;
        var sellButton = window.GetChildRecursive(GameVendorPanel.SellAllButtonId);
        var clickedSell = window.OpenTabId == GameVendorPanel.SellTabId && sellButton is not null && ClickVendorElement(manager, sellButton);
        var sale = window.LastSell;
        var saleDone = sale is not null && await WaitFor(() => sale.IsCompleted, 20.0);
        var sellCoinsAfter = await WaitFor(() => session.State.Self.Qualities.CoinValue != sellCoinsBefore, 5.0) ? session.State.Self.Qualities.CoinValue : sellCoinsBefore;
        var cleared = await WaitFor(() => window.SellItems.Count == 0, 5.0);
        Check(
            sellName,
            refused == VendorSellVerdict.WrongType && refusalText == "That item cannot be sold here" && listed == VendorSellVerdict.Ok && marked && payout == 9 && clickedSell && saleDone && sale!.Outcome?.Kind == ActionOutcomeKind.Confirmed
                && sale.Source == ActionSource.Ui && sellCoinsAfter - sellCoinsBefore == payout && cleared && !(kitCell?.SellMarkShown ?? false),
            $"bracers={refused} ('{refusalText}') kit={listed} mark_shown={marked} predicted_payout={payout} clicked_sell={clickedSell} outcome={sale?.Outcome?.Kind.ToString() ?? "pending"} ({sale?.Outcome?.Detail}) coins {sellCoinsBefore} -> {sellCoinsAfter} list_cleared={cleared}");

        // The close button (both lists empty): the window and the floaty go.
        var close = window.GetChildRecursive(GameVendorPanel.CloseButtonId);
        if (close is not null)
        {
            ClickVendorElement(manager, close);
            await Frames(2);
        }
    }

    private static int KitsInPack(IGameSessionView session)
    {
        var n = 0;
        var inventory = session.State.Self.Inventory;
        foreach (var entry in inventory.ItemsIn(inventory.Owner != 0 ? inventory.Owner : session.State.Self.Guid))
        {
            if (session.State.Objects.TryGet(entry.Guid, out var o) && o.Weenie.WeenieClassId == VendorKitWcid)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>A left click at the centre of <paramref name="element"/> with the real router (the pointer finds the element or one of its children first; false when something else covers the centre).</summary>
    private static bool ClickVendorElement(UiElementManager manager, UiElement element)
    {
        var box = element.ScreenBox;
        var x = (box.X0 + box.X1) / 2;
        var y = (box.Y0 + box.Y1) / 2;
        var hit = manager.HitTest(x, y);
        var found = false;
        for (var e = hit; e is not null; e = e.Parent)
        {
            if (ReferenceEquals(e, element))
            {
                found = true;
                break;
            }
        }

        if (!found)
        {
            return false;
        }

        manager.Input.MouseMove(x, y);
        manager.Input.MouseButton(UiMouseButton.Left, true);
        manager.Input.MouseButton(UiMouseButton.Left, false);
        return true;
    }
}

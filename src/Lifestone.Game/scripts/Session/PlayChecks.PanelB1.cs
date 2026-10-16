using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Motion;

namespace Lifestone.Game.Sessions;

/// <summary>
/// Slice b1 (Item widgets and the inventory page 7), docs/milestones/m3-plan.md section 4.6 row "b1". The stand-in grants three items through the item tool (the kit's wand, the first leather piece and 120 pyreals; the stand-in gives every
/// created item the placeholder icon 0x06000001, <c>TODO(m3-standin-weenies)</c>), then the page is opened and judged against what <see cref="PlayerInventory"/> and the world objects hold: the pack list's cells are the pack's items in order, the
/// stack shows its count, the player's cell and the side-pack column are as the model has them, the burden text and meter are the load rule applied to the model's burden, the title is the player's name. The page is closed again.
/// </summary>
public sealed partial class PlayChecks
{
    private async partial Task PanelB1ChecksAsync(GamePlayScreen shell)
    {
        const string packName = "the-inventory-page-shows-the-packs-items-in-order-as-the-inventory-model-holds-them";
        const string stackName = "the-inventory-page-shows-a-stacks-count-the-players-cell-the-side-packs-the-burden-and-the-title";
        const string closeName = "the-inventory-page-closes-and-draws-nothing-while-hidden";
        var session = _host.Session;
        var controller = shell.PanelControllers?.ControllersOf(GameInventoryPage.Panel).OfType<GameInventoryPage>().FirstOrDefault();
        if (session is null || controller is null)
        {
            Check(packName, false, $"session={session is not null} inventory_controller={controller is not null}");
            return;
        }

        var wand = session.Tools.SpawnItem(GearKit.Wand.Wcid, null, ActionSource.Host);
        var armour = session.Tools.SpawnItem(GearKit.Armour[0].Wcid, null, ActionSource.Host);
        var coins = session.Tools.SpawnItem(StandInItemKind.PyrealWcid, 120, ActionSource.Host);
        var granted = await WaitFor(() => wand.IsCompleted && armour.IsCompleted && coins.IsCompleted, 20.0);
        var guids = new[] { wand.Result?.Guid ?? 0u, armour.Result?.Guid ?? 0u, coins.Result?.Guid ?? 0u };
        var objects = session.State.Objects;
        var created = await WaitFor(() => guids.All(g => g != 0 && objects.Contains(g)), 10.0);

        var opened = await OpenPanelAsync(shell, GameInventoryPage.Panel, 14);
        var inventory = session.State.Self.Inventory;
        var filled = await WaitFor(() => controller.PackList.CellCount == controller.PackList.FixedSlots && controller.PackList.Items.OfType<UiItem>().All(static c => !c.IconPending), 10.0);

        // The pack list: the items the model's pack holds that have a world object, in order.
        var expected = new List<uint>();
        foreach (var entry in inventory.Pack)
        {
            if (objects.Contains(entry.Guid))
            {
                expected.Add(entry.Guid);
            }
        }

        var shown = new List<uint>();
        foreach (var cell in controller.PackList.Items.OfType<UiItem>())
        {
            if (!cell.IsEmpty)
            {
                shown.Add(cell.ItemId);
            }
        }

        Check(
            packName,
            granted && created && opened && filled && expected.Count >= 3 && shown.SequenceEqual(expected) && guids.All(expected.Contains),
            $"granted={granted} created={created} opened={opened} cells={controller.PackList.CellCount}/{controller.PackList.FixedSlots} pack_model={inventory.Pack.Count} pack_shown={shown.Count} order_matches={shown.SequenceEqual(expected)} icons_pending={controller.PackList.Items.OfType<UiItem>().Count(static c => c.IconPending)} (the stand-in's icons are the placeholder 0x06000001)");

        // The stack's count, the player's cell, the side-pack column, the title and the burden.
        var coinCell = controller.PackList.FindItem(guids[2]);
        objects.TryGet(guids[2], out var coinObject);
        var stack = coinObject?.Weenie.StackSize ?? 0;
        var self = session.State.Self;
        var q = self.Qualities;
        var raw = q.TryGetAttributeLevels(1, out _, out var strengthRaw) ? strengthRaw : 0u;
        q.TryGetInt(0xE6, out var augmentations);
        var capacity = EncumbranceSystem.EncumbranceCapacity((int)EnchantmentRules.EnchantAttribute(q.Enchantments, 1, raw), augmentations);
        var burden = inventory.Burden ?? 0;
        var level = Math.Clamp(EncumbranceSystem.Load(capacity, burden) / 3.0, 0.0, 1.0);
        var percent = (int)Math.Floor(level * 300.0);
        var sideShown = controller.SidePackList.Items.OfType<UiItem>().Count(static c => !c.IsEmpty);
        var sideExpected = inventory.SidePacks.Count(e => objects.Contains(e.Guid));
        var title = self.Name is { Length: > 0 } name ? "Inventory of " + name : "Inventory";
        Check(
            stackName,
            coinCell is not null && stack == 120 && coinCell.Quantity == stack && coinCell.QuantityText is { Visible: true } text && text.Text == "120"
                && controller.TopContainerList.CellAt(0)?.ItemId == (inventory.Owner != 0 ? inventory.Owner : self.Guid) && sideShown == sideExpected
                && controller.Title == title && controller.ContentsTitle == "Contents of Backpack"
                && controller.BurdenTextNow == percent + "%" && Math.Abs(controller.BurdenLevel - (float)level) < 0.0005f,
            $"stack={stack} cell_quantity={coinCell?.Quantity} player_cell=0x{controller.TopContainerList.CellAt(0)?.ItemId:X8} side_packs={sideShown}/{sideExpected} title='{controller.Title}' contents='{controller.ContentsTitle}' burden={burden} capacity={capacity} burden_text={controller.BurdenTextNow} meter={controller.BurdenLevel:0.####} expected_text={percent}%");

        // Closed: the page does not redraw while hidden.
        await ClosePanelAsync(shell, GameInventoryPage.Panel);
        var refreshes = controller.Refreshes;
        await Frames(6);
        Check(closeName, !shell.PanelHost!.IsOpen && controller.Refreshes == refreshes, $"open={shell.PanelHost.IsOpen} refreshes={refreshes}->{controller.Refreshes}");
    }
}

using Lifestone.Client.Play;
using Lifestone.Client.RetailUi;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Toolbar;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The toolbar's shortcut slots (the hotbar, M4 3.2 h2, <see cref="GameHotbar"/>) over the real gameplay layout: drawn from the model with the dats' number art, ghosted when the object is missing, the number keys and the click,
/// the cooldown overlay and the lists as drag sources and targets. The play-check <c>hotbar</c> proves the same path on the stand-in with its sends.
/// </summary>
public sealed class GameToolbarTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    // ------------------------------------------------------------------ the slots from the model

    [RequiresLocalDatsFact]
    public void The_eighteen_slot_lists_are_the_toolbars_two_rows_and_the_bar_that_shows_is_the_top_row_of_nine()
    {
        using var g = new HotbarRig(fixture);
        for (var slot = 0; slot < GameHotbar.SlotCount; slot++)
        {
            var list = Assert.IsType<UiItemList>(g.Hotbar.ListOf(slot));
            Assert.Equal(1, list.FixedSlots); // one cell each
            Assert.Equal(1, list.CellCount);
            Assert.Equal(slot, g.Hotbar.SlotOfList(list));
        }

        var top = g.Hotbar.ListOf(0)!.ScreenBox;
        var bottom = g.Hotbar.ListOf(9)!.ScreenBox;
        var window = g.Toolbar.Window!.ScreenBox;
        Assert.Equal(top.Y0 + 32, bottom.Y0); // the second row lies one cell below the first
        Assert.True(top.Y1 <= window.Y1, "the top row is inside the toolbar window");
        Assert.True(bottom.Y0 > window.Y1 - 8, "the second row is clipped below the toolbar window");
        for (var slot = 1; slot < GameHotbar.VisibleSlots; slot++)
        {
            Assert.Equal(g.Hotbar.ListOf(slot - 1)!.ScreenBox.X0 + 32, g.Hotbar.ListOf(slot)!.ScreenBox.X0); // the nine sit side by side
        }
    }

    [RequiresLocalDatsFact]
    public void The_slots_show_the_model_the_icon_the_stack_count_and_the_empty_art_and_follow_a_change_of_it()
    {
        using var g = new HotbarRig(fixture, (0, HotbarRig.Kit), (1, HotbarRig.Potion), (3, HotbarRig.Sword));
        var kit = g.Cell(0);
        Assert.Equal(HotbarRig.Kit, kit.ItemId);
        Assert.False(kit.IsEmpty);
        Assert.NotNull(kit.ComposedIcon);
        Assert.Equal(-1, kit.Quantity); // one kit: no count
        var potion = g.Cell(1);
        Assert.Equal(HotbarRig.Potion, potion.ItemId);
        Assert.Equal(5, potion.Quantity);
        Assert.Equal("5", potion.QuantityText!.Text);
        Assert.True(potion.QuantityText.Visible);
        Assert.Equal(HotbarRig.Sword, g.Cell(3).ItemId);
        foreach (var empty in new[] { 2, 4, 5, 6, 7, 8, 9, 17 })
        {
            var cell = g.Cell(empty);
            Assert.True(cell.IsEmpty, $"slot {empty}");
            Assert.Equal(UiItem.EmptyState, cell.IconElement!.State); // the layout's empty-slot art (lane e's check: the icon child's state)
            Assert.Equal(1, g.Hotbar.ListOf(empty)!.CellCount);
        }

        // The model changes (a PlayerDescription again with another bar): the slots follow.
        g.Handler.OnPlayerDescription(
            g.Context,
            new Lifestone.Net.Types.GameEventHeader(HotbarRig.Player, 40, 0x0013),
            new Lifestone.Net.Messages.PlayerDescriptionMsg { Shortcuts = [new Lifestone.Net.Messages.ShortcutDesc(2, HotbarRig.Gem, 0)] });
        g.Frames(2);
        Assert.Equal(HotbarRig.Gem, g.Cell(2).ItemId);
        Assert.True(g.Cell(0).IsEmpty);
        Assert.True(g.Cell(1).IsEmpty);
    }

    [RequiresLocalDatsFact]
    public void A_slot_with_an_item_shows_its_corner_number_from_the_dats_art_a_ghosted_one_the_ghosted_number_and_an_empty_slot_its_numeral_in_the_empty_art()
    {
        using var g = new HotbarRig(fixture, (0, HotbarRig.Kit), (1, HotbarRig.Ground), (2, HotbarRig.Unknown), (3, HotbarRig.Potion));
        var seen = new HashSet<uint>();
        foreach (var slot in new[] { 0, 3 })
        {
            var cell = g.Cell(slot);
            Assert.Equal(slot, cell.ShortcutNumber);
            Assert.NotEqual(0u, cell.ShortcutNumberImage);
            Assert.True(cell.ShortcutNumberElement!.Visible, $"slot {slot} shows its corner number");
            Assert.True(seen.Add(cell.ShortcutNumberImage), $"slot {slot}'s number is its own image");
            Assert.Equal(DigitOf(g, slot, UiItem.ShortcutDigitsProperty), cell.ShortcutNumberImage); // the regular array's entry for the slot
        }

        // The slot with the object on the ground and the slot with an unknown object are missing: ghosted, with the ghosted array's entry.
        foreach (var missing in new[] { 1, 2 })
        {
            var cell = g.Cell(missing);
            Assert.True(g.Bar.IsMissing(missing));
            Assert.True(cell.GhostedFrame!.Visible, $"slot {missing} is ghosted");
            Assert.True(cell.ShortcutNumberElement!.Visible);
            Assert.Equal(DigitOf(g, missing, UiItem.ShortcutDigitsGhostedProperty), cell.ShortcutNumberImage);
            Assert.NotEqual(DigitOf(g, missing, UiItem.ShortcutDigitsProperty), cell.ShortcutNumberImage);
        }

        Assert.False(g.Cell(0).GhostedFrame!.Visible);

        // An empty slot has no corner number (the toolbar's cell has no empty array, so retail's lookup changes nothing): its number is the numeral of its own empty-cell art, one image per list.
        var empties = new HashSet<uint>();
        for (var slot = 4; slot < GameHotbar.VisibleSlots; slot++)
        {
            var cell = g.Cell(slot);
            Assert.True(cell.IsEmpty);
            Assert.False(cell.ShortcutNumberElement!.Visible, $"empty slot {slot} shows no corner number");
            Assert.Equal(0u, cell.ShortcutNumberImage);
            Assert.True(empties.Add(EmptyArtOf(cell)), $"empty slot {slot} has its own empty-cell art");
        }

        // The numbers of slots 10 to 18 are the blank image of the layout's array (the bar shows nine).
        Assert.Equal(DigitOf(g, 9, UiItem.ShortcutDigitsProperty), DigitOf(g, 17, UiItem.ShortcutDigitsProperty));
        Assert.NotEqual(DigitOf(g, 0, UiItem.ShortcutDigitsProperty), DigitOf(g, 9, UiItem.ShortcutDigitsProperty));
        Assert.False(g.Cell(9).ShortcutNumberElement!.Visible);
    }

    private static uint EmptyArtOf(UiItem cell) =>
        cell.IconElement!.Desc.States[UiItem.EmptyState].Media.OfType<Lifestone.Data.Ui.UiImageMedia>().First().File;

    private static uint DigitOf(HotbarRig g, int slot, uint property)
    {
        var element = g.Cell(slot).ShortcutNumberElement!;
        Assert.True(element.InqProperty(property, out var found));
        var array = Assert.IsType<Lifestone.Data.Ui.UiArrayProperty>(found);
        return Assert.IsType<Lifestone.Data.Ui.UiDataIdProperty>(array.Items[slot]).Value;
    }

    [RequiresLocalDatsFact]
    public void A_missing_object_comes_back_unghosted_and_an_item_that_leaves_the_inventory_leaves_the_bar()
    {
        using var g = new HotbarRig(fixture, (0, HotbarRig.Kit), (1, HotbarRig.Potion));
        Assert.False(g.Cell(0).GhostedFrame!.Visible);
        g.Handler.OnInventoryPutObjectIn3D(g.Context, new Lifestone.Net.Types.GameEventHeader(HotbarRig.Player, 90, 0x019A), new Lifestone.Net.Messages.InventoryPutObjectIn3DEvent(HotbarRig.Kit));
        g.Frames(3);
        // #219: the dropped kit's row went with it (the model removes it and the Action API sends one RemoveShortcut), and the slot is empty.
        Assert.True(g.Cell(0).IsEmpty);
        Assert.False(g.Bar[0].IsItem);
        Assert.Equal(HotbarRig.Potion, g.Cell(1).ItemId);
    }

    // ------------------------------------------------------------------ the keys and the click

    [RequiresLocalDatsFact]
    public void The_number_keys_use_the_slots_one_to_nine_through_UseShortcut_and_an_empty_slot_is_refused()
    {
        using var g = new HotbarRig(fixture, (0, HotbarRig.Potion), (1, HotbarRig.Kit));
        var before = g.Hotbar.Uses;
        Assert.True(g.Key(DikScanCodes.D1));
        Assert.Equal(before + 1, g.Hotbar.Uses);
        var drink = g.Hotbar.LastUse!;
        Assert.Equal(ActionKind.Drink, drink.Kind); // slot 1 holds the potion: a drink
        Assert.Equal(ActionSource.Ui, drink.Source);

        Assert.True(g.Key(DikScanCodes.D2));
        Assert.Equal(ActionKind.UseWithTarget, g.Hotbar.LastUse!.Kind); // slot 2 holds the kit: used on the player

        Assert.True(g.Key(DikScanCodes.D5)); // an empty slot: the key is taken and the Action API refuses
        var empty = g.Hotbar.LastUse!;
        Assert.Equal(ActionKind.UseShortcut, empty.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, empty.Outcome?.Kind);
        Assert.Equal(before + 3, g.Hotbar.Uses);
    }

    [RequiresLocalDatsFact]
    public void The_keymap_dat_binds_the_digit_keys_to_the_use_actions_and_alt_with_a_digit_reaches_the_second_row()
    {
        using var g = new HotbarRig(fixture);
        var bindings = g.Input.Bindings!.Bindings;
        for (var key = 0; key < 9; key++)
        {
            var action = GameHotbar.FirstUseAction + (uint)key;
            Assert.Contains(bindings, b => b.InputMapId == GameHotbar.InputMap && b.ActionId == action && b.Control.DeviceIndex == RetailKeyBindings.KeyboardDevice && b.Control.Key >> 16 == DikScanCodes.D1 + key && b.MetaMode == 0);
            Assert.Equal(key, GameHotbar.SlotOfAction(action));
        }

        Assert.Equal(11, GameHotbar.SlotOfAction(GameHotbar.FirstUseAction + 11));
        Assert.Equal(12, GameHotbar.SlotOfAction(GameHotbar.FirstHighUseAction));
        Assert.Equal(17, GameHotbar.SlotOfAction(GameHotbar.FirstHighUseAction + 5));
        Assert.Equal(-1, GameHotbar.SlotOfAction(GameHotbar.FirstUseAction + 12)); // 0x1000004E on are the select variants
        Assert.Equal(-1, GameHotbar.SlotOfAction(GameHotbar.FirstHighUseAction + 6));

        var before = g.Hotbar.Uses;
        g.Input.KeyDown(DikScanCodes.LMenu);
        g.Key(DikScanCodes.D1); // Alt+1 is the keymap's action 0x1000004B: slot 10 (empty here: the refusal names it)
        g.Key(DikScanCodes.D9); // Alt+9 is 0x10000137: slot 18
        g.Input.KeyUp(DikScanCodes.LMenu);
        Assert.Equal(before + 2, g.Hotbar.Uses);
        Assert.Equal("UseShortcut slot=17", g.Hotbar.LastUse!.Summary);
        g.Input.KeyDown(DikScanCodes.LControl);
        g.Key(DikScanCodes.D3); // Ctrl+3 is the same action as 3: slot 3
        g.Input.KeyUp(DikScanCodes.LControl);
        Assert.Equal("UseShortcut slot=2", g.Hotbar.LastUse.Summary);
    }

    [RequiresLocalDatsFact]
    public void A_key_does_nothing_while_the_chat_entry_has_the_focus()
    {
        using var g = new HotbarRig(fixture, (0, HotbarRig.Potion));
        var entry = g.Screen.Chat!.Main!.Entry!;
        g.Rig.Manager.SetFocusElement(entry);
        g.Frames(1);
        var before = g.Hotbar.Uses;
        g.Key(DikScanCodes.D1);
        Assert.Equal(before, g.Hotbar.Uses);
        g.Rig.Manager.SetFocusElement(null);
        g.Frames(1);
        Assert.True(g.Key(DikScanCodes.D1));
        Assert.Equal(before + 1, g.Hotbar.Uses);
    }

    [RequiresLocalDatsFact]
    public void A_left_click_on_a_slot_uses_it_the_same_way_and_an_empty_slot_does_nothing()
    {
        using var g = new HotbarRig(fixture, (0, HotbarRig.Potion), (1, HotbarRig.Kit));
        var before = g.Hotbar.Uses;
        g.Click(g.Cell(1));
        Assert.Equal(before + 1, g.Hotbar.Uses);
        Assert.Equal(ActionKind.UseWithTarget, g.Hotbar.LastUse!.Kind);
        Assert.False(g.Cell(1).SelectedFrameShown, "the click uses the item; it does not select it");

        g.Click(g.Cell(6)); // an empty slot: no cell to use
        Assert.Equal(before + 1, g.Hotbar.Uses);
        Assert.Equal(0, g.Drag.Started);
    }

    // ------------------------------------------------------------------ the cooldown overlay

    [RequiresLocalDatsFact]
    public void The_slot_shows_the_cooldown_overlay_while_its_use_is_not_complete_and_clears_it_at_the_completion()
    {
        using var g = new HotbarRig(fixture, (0, HotbarRig.Potion), (1, HotbarRig.Kit));
        var cell = g.Cell(0);
        Assert.Equal(0, cell.CooldownPhase);
        var last = Assert.IsAssignableFrom<UiElement>(cell.CooldownElement(UiItem.CooldownSteps));
        Assert.False(last.Visible);

        var handle = new ActionHandle(9001, ActionKind.Drink, ActionSource.Ui, "Drink", 0);
        g.Hotbar.TrackBusy(0, handle);
        Assert.True(g.Hotbar.IsBusy(0));
        Assert.Equal(UiItem.CooldownSteps, cell.CooldownPhase);
        Assert.True(last.Visible);
        Assert.False(g.Cell(1).CooldownElement(UiItem.CooldownSteps)!.Visible); // only that slot
        g.Frames(2);
        Assert.True(last.Visible); // a redraw for another reason keeps it

        Assert.True(handle.TryComplete(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "drunk"), 1.0, static (_, _) => { }));
        Assert.False(g.Hotbar.IsBusy(0));
        Assert.Equal(0, cell.CooldownPhase);
        Assert.False(last.Visible);

        // The cell's steps are the dats' ten images (one per step) and the phase shows exactly one.
        cell.SetCooldown(4);
        for (var phase = 1; phase <= UiItem.CooldownSteps; phase++)
        {
            Assert.Equal(phase == 4, cell.CooldownElement(phase)!.Visible);
        }

        cell.SetCooldown(0);
    }

    [RequiresLocalDatsFact]
    public void M6_15_an_automation_drink_shows_on_its_slot_as_a_persons_use_shows_the_overlay_until_the_drink_completes()
    {
        using var g = new HotbarRig(fixture, (0, HotbarRig.Potion), (1, HotbarRig.Kit));
        var cell = g.Cell(0);
        var uses = g.Hotbar.Uses;

        // The Action API's notice of an automation drink that went out as the use of slot 1's item (PlayerRulesGate.DrinkSent): no key, no click.
        var handle = new ActionHandle(9002, ActionKind.Drink, ActionSource.Automation, "Drink", 0);
        g.Rig.Session.ActionApi.PublishShortcutUsed(0, HotbarRig.Potion, handle);
        Assert.True(g.Hotbar.IsBusy(0));
        Assert.Equal(UiItem.CooldownSteps, cell.CooldownPhase);
        Assert.False(g.Hotbar.IsBusy(1)); // only that slot
        Assert.Equal(1, g.Hotbar.AutomationUses);
        Assert.Equal(uses, g.Hotbar.Uses); // the bar asked for no use of its own: the automation's drink is already on its way

        Assert.True(handle.TryComplete(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "drunk"), 1.0, static (_, _) => { }));
        Assert.False(g.Hotbar.IsBusy(0));
        Assert.Equal(0, cell.CooldownPhase);
    }

    [RequiresLocalDatsFact]
    public void A_use_that_is_refused_at_once_shows_no_overlay()
    {
        using var g = new HotbarRig(fixture, (0, HotbarRig.Gem));
        g.Key(DikScanCodes.D1);
        Assert.False(g.Hotbar.IsBusy(0)); // the gem's Use is refused offline: a handle that is complete is not busy
        Assert.Equal(0, g.Cell(0).CooldownPhase);
    }

    // ------------------------------------------------------------------ the lists as drag source and target

    [RequiresLocalDatsFact]
    public void Every_slot_list_is_a_drag_source_and_a_drop_target_and_the_bar_gives_them_up_when_it_ends()
    {
        var g = new HotbarRig(fixture, (0, HotbarRig.Kit));
        for (var slot = 0; slot < GameHotbar.SlotCount; slot++)
        {
            var list = g.Hotbar.ListOf(slot)!;
            Assert.True(list.DragSource, $"slot {slot} is a drag source");
            Assert.True(g.Drag.IsTarget(list), $"slot {slot} takes drops");
            Assert.NotNull(g.Hotbar.DropOf(slot));
        }

        var one = g.Hotbar.ListOf(0)!;
        g.Toolbar.Dispose();
        Assert.False(one.DragSource);
        Assert.False(g.Drag.IsTarget(one));
        g.Toolbar.Dispose(); // twice is fine
        g.Dispose();
    }

    [RequiresLocalDatsFact]
    public void A_slot_takes_a_carried_item_but_not_pyreals_or_an_object_that_is_not_carried_and_says_why()
    {
        using var g = new HotbarRig(fixture, (0, HotbarRig.Kit));
        var drop = g.Hotbar.DropOf(3)!;
        var source = g.Cell(0);
        UiDragPayload From(uint item) => new(item, source, null, false); // the payload of a cell of a list that is no slot list (a pack)
        Assert.True(drop.Accepts(From(HotbarRig.Potion), null));
        Assert.True(drop.Accepts(From(HotbarRig.Sword), null));
        Assert.False(drop.Accepts(From(HotbarRig.Pyreals), null)); // ACCWeenieObject::IsCoinstack
        Assert.False(drop.Accepts(From(HotbarRig.Ground), null)); // on the ground: not carried
        Assert.False(drop.Accepts(From(0), null));
        Assert.Contains("carries", drop.Refusal(From(HotbarRig.Ground), null), StringComparison.Ordinal);
        Assert.Contains("pyreals", drop.Refusal(From(HotbarRig.Pyreals), null), StringComparison.Ordinal);
        // A slot's own drag (from a slot list) is always a slot's to take.
        Assert.True(drop.Accepts(new UiDragPayload(HotbarRig.Kit, source, g.Hotbar.ListOf(0), false), null));
    }

    [RequiresLocalDatsFact]
    public void The_bar_ends_with_the_screen_and_leaves_no_handler_key_map_or_drag_registration_behind()
    {
        var g = new HotbarRig(fixture, (0, HotbarRig.Kit));
        var with = g.Rig.Session.Bus.HandlerCount;
        var list = g.Hotbar.ListOf(0)!;
        Assert.Contains(g.Input.InputMaps, m => m.InputMapId == GameHotbar.InputMap);
        g.Rig.Flow.QueueUIMode(Lifestone.Client.RetailUi.Flow.UiModes.CharacterManagement); // the screen is disposed; the session outlives it
        g.Frames(4);
        Assert.True(g.Rig.Session.Bus.HandlerCount <= with - 4, $"the bar's four subscriptions are gone (before {with}, after {g.Rig.Session.Bus.HandlerCount})");
        Assert.False(list.DragSource);
        Assert.DoesNotContain(g.Input.InputMaps, m => m.InputMapId == GameHotbar.InputMap);
        g.Dispose();
    }

    [Fact]
    public void The_hotbar_frame_has_a_name_of_its_own_that_no_page_window_or_extra_frame_has()
    {
        Assert.Equal("hotbar", PanelFramesPlan.HotbarFrame);
        Assert.Equal("hotbar.png", PanelFramesPlan.HotbarShotFile);
        Assert.Equal("hotbar-cooldown", PanelFramesPlan.HotbarCooldownFrame);
        Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", PanelFramesPlan.HotbarFrame);
        Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", PanelFramesPlan.HotbarCooldownFrame);
        Assert.NotEqual(PanelFramesPlan.HotbarFrame, PanelFramesPlan.HotbarCooldownFrame);
        Assert.DoesNotContain(PanelFramesPlan.HotbarCooldownFrame, PanelFramesPlan.OfflineContentFrames);
        Assert.DoesNotContain(PanelFramesPlan.HotbarFrame, PanelFramesPlan.OfflineContentFrames);
        Assert.DoesNotContain(PanelFramesPlan.HotbarFrame, PanelFramesPlan.PanelShotExtraFrames);
        Assert.DoesNotContain(PanelFramesPlan.AllWindowFrames, f => f.Name == PanelFramesPlan.HotbarFrame);
        Assert.DoesNotContain(Lifestone.Client.RetailUi.Panels.GamePanelHost.Pages.Select(PanelFramesPlan.FileName), f => f == PanelFramesPlan.HotbarShotFile);
    }
}

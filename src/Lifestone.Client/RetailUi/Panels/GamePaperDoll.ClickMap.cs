using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>What a paperdoll slot asks for when an item is dropped on it.</summary>
public enum PaperDollDropRule
{
    /// <summary>The item is wielded at its own valid locations, which must include the slot's (a single-location item is the slot's mask; a coat that covers several slots goes whole, as the double click's wield does).</summary>
    SlotLocations,

    /// <summary>A ring or bracelet: the slot's own side when it is free, else the free side (<see cref="WieldSlotChoice"/>), else the slot dropped on (the server swaps it).</summary>
    FreeSide,
}

/// <summary>One entry of the paperdoll's click map (M4 2.2 s3, closes <c>TODO(m3-paperdoll-clickmap)</c> for the slots): the slot's element, the locations it holds, its side and the drop it accepts.</summary>
/// <param name="ElementId">The slot's item list (<c>gmPaperDollUI::GetLocationInfoFromElementID</c>, 11.6096 0x004A3B60).</param>
/// <param name="Name">The slot's name (tests and logs).</param>
/// <param name="Mask">The equip locations the slot holds.</param>
/// <param name="Side">Retail's <c>UI_SLOT_SIDE</c>: 0 none, 1 left, 2 right.</param>
/// <param name="Armour">True for the nine armour slots (hidden until the slots checkbox is checked).</param>
/// <param name="Rule">What a drop on the slot asks for.</param>
public readonly record struct PaperDollClickEntry(uint ElementId, string Name, EquipMask Mask, int Side, bool Armour, PaperDollDropRule Rule);

public sealed partial class GamePaperDoll
{
    /// <summary>
    /// The click map: for each slot element its <see cref="EquipMask"/> and the drop it accepts (retail: <c>gmPaperDollUI</c> 0x10000024 and its <c>UIElement_ItemList</c> slots; <c>CreateClickMap</c> 0x004A4BC0 is the figure's, whose body-part select and
    /// examine is <see cref="ExamineFigureAtMouse"/>, M4 3.2 s4c). Re-expressed from <see cref="SlotTable"/> and the layout, and since M5 7 (D122) checked against retail's drop code: <c>gmPaperDollUI::HandleDropRelease</c> (11.6096 0x004A50F0) hands a slot's drop to
    /// <c>AcceptDragObject</c> (0x004A3E80: the item's locations against the slot's, a melee weapon also on the shield slot; worn by <c>CPlayerSystem::AutoWear</c> for the clothing, armour and cloak slots, else wielded by <c>AutoWield</c> on the
    /// slot's side) and the figure's to <c>AcceptPaperDollDragObject</c> (0x004A4DE0, <see cref="GamePaperDollFigureDrop"/>). The generic drag (<c>UIElementManager::StartDragandDrop</c> 0x0045E120, <c>UIElement_ItemList::AcceptDragObject</c>
    /// 0x004E4EE0) stays <c>TODO(m4-retail-drag)</c> in <see cref="UiDrag"/>.
    /// </summary>
    public static IReadOnlyList<PaperDollClickEntry> ClickMap => _clickMap ??= BuildClickMap(); // built on first use: static initializers of two partial files run in no fixed order

    private static PaperDollClickEntry[]? _clickMap;

    /// <summary>The click-map entry of slot <paramref name="elementId"/>; false for an element that is no slot.</summary>
    public static bool TryGetClickEntry(uint elementId, out PaperDollClickEntry entry)
    {
        foreach (var candidate in ClickMap)
        {
            if (candidate.ElementId == elementId)
            {
                entry = candidate;
                return true;
            }
        }

        entry = default;
        return false;
    }

    /// <summary>The drop target of slot <paramref name="elementId"/>, or null.</summary>
    public GamePaperDollDrop? DropOf(uint elementId)
    {
        for (var i = 0; i < _drops.Length; i++)
        {
            if (SlotTable[i].ElementId == elementId)
            {
                return _drops[i];
            }
        }

        return null;
    }

    /// <summary>
    /// The figure's hit-test colours (nine <c>RGBAColor_HitTest_*</c> globals, read as bytes) with the equip locations <c>gmPaperDollUI::GetPaperDollItemUnderMouse</c> (11.6096 0x004A4C90; 11.4186 0x004A4920) turns each into:
    /// head 1, chest 0x202, abdomen 0x404, upper arm 0x808, lower arm 0x1010, upper leg 0x2040, lower leg 0x4080, hand 0x20, foot 0x100.
    /// </summary>
    public static readonly (byte R, byte G, byte B, string Part, uint Mask)[] HitTestColors =
    [
        (0, 0, 255, "head", 0x1),
        (0, 255, 0, "chest", 0x202),
        (255, 0, 0, "abdomen", 0x404),
        (0, 255, 255, "upper arm", 0x808),
        (255, 0, 255, "lower arm", 0x1010),
        (255, 255, 0, "upper leg", 0x2040),
        (0, 0, 128, "lower leg", 0x4080),
        (0, 128, 0, "hand", 0x20),
        (128, 0, 0, "foot", 0x100),
    ];

    /// <summary>The enum value <c>CreateClickMap</c> (11.6096 0x004A4BC0) passes to <c>DBObj::GetDIDByEnum</c> (group 7, the UI assets) for the click map's image.</summary>
    public const uint ClickMapEnum = 0x1000000C;

    /// <summary>The equip locations of the body part the colour (<paramref name="r"/>, <paramref name="g"/>, <paramref name="b"/>) stands for, or 0 for a colour that is none of the nine (the mouse is over no body part).</summary>
    public static uint MaskOfColor(byte r, byte g, byte b)
    {
        foreach (var entry in HitTestColors)
        {
            if (entry.R == r && entry.G == g && entry.B == b)
            {
                return entry.Mask;
            }
        }

        return 0;
    }

    /// <summary>
    /// Which worn item a body part stands for (<c>gmPaperDollUI::GetUpperInvObj</c>, 11.6096 0x004A4AC0; 11.4186 0x004A4750, with the priority comparison of <c>InventoryPlacement::DetermineHigherPriority</c>): of the worn items whose location
    /// covers <paramref name="locations"/>, the one with the higher clothing priority; on a tie the later one in the list. When none covers it, the player itself (<paramref name="playerGuid"/>) is the answer, and is examined.
    /// </summary>
    public static uint UpperItemAt(IReadOnlyList<EquippedItem> equipped, uint locations, uint playerGuid)
    {
        if (locations == 0)
        {
            return 0;
        }

        EquippedItem top = default;
        var found = false;
        for (var i = 0; i < equipped.Count; i++)
        {
            var candidate = equipped[i];
            if ((candidate.Location & locations) == 0)
            {
                continue;
            }

            if (found && ClothingRank(top, locations) > ClothingRank(candidate, locations))
            {
                continue; // the item already on top outranks this one
            }

            top = candidate;
            found = true;
        }

        return found ? top.Guid : playerGuid;
    }

    /// <summary>The rank an item has among the worn items over <paramref name="locations"/>: its own clothing priority, or 127 when it has none (priority 0) and the part it covers is one of the armour locations 0x200 to 0x4000; a priority-less item elsewhere ranks 0.</summary>
    private static uint ClothingRank(EquippedItem item, uint locations)
    {
        if (item.Priority != 0)
        {
            return item.Priority;
        }

        var covered = locations & item.Location;
        return covered is >= 0x200 and <= 0x4000 ? 127u : 0u;
    }

    /// <summary>
    /// The right button over the figure's click-mask button (<c>ListenToElementMessage</c> 0x004A5FA0, message 28 with parameter 8): the body part under the mouse (the click-map image, <see cref="MaskOfColor"/>) names the worn item (or the
    /// player), which is selected and examined (<c>ClientUISystem::ExamineObject</c>): <see cref="GameExaminePanel.Open"/>. False when the click map is not ready, the mouse is over no part or there is no examine panel.
    /// </summary>
    private bool ExamineFigureAtMouse() => GameExaminePanel.Of(_context) is { } examine && MaskAtMouse() is { } mask && ExamineMask(mask, examine);

    /// <summary>
    /// The left button over the figure (<c>ListenToElementMessage</c> 0x004A5FA0, message 28 with parameter 7; closes <c>TODO(m4-paperdoll-figure-select)</c>, M5 7 D122): the worn item under the mouse
    /// (<c>GetPaperDollItemUnderMouse</c>), or the player where no item covers the part, becomes the selected object (<c>ACCWeenieObject::SetSelectedObject</c>) through <see cref="SelectRequester"/>. Retail first
    /// runs a pending target mode on the player instead (<c>ClientUISystem::ExecuteTargetModeForItem</c>); the shell has no target mode, so that branch is a named stub (<c>TODO(m5-paperdoll-target-mode)</c>).
    /// False when the click map is not ready, the mouse is over no part or nothing can select.
    /// </summary>
    private bool SelectFigureAtMouse() => SelectRequester is not null && MaskAtMouse() is { } mask && SelectMask(mask);

    /// <summary>Selects what covers <paramref name="mask"/> (a test calls this with a location mask instead of a pixel).</summary>
    internal bool SelectMask(uint mask)
    {
        if (SelectRequester is not { } select)
        {
            return false;
        }

        var session = _context.Session;
        var guid = UpperItemAt(session.State.Self.Inventory.Equipped, mask, session.State.Self.Guid);
        if (guid == 0)
        {
            return false;
        }

        _context.Manager.LogOnce("paperdoll:target-mode", LogSeverity.Warning, "TODO(m5-paperdoll-target-mode): retail's left click on the figure runs a pending target mode on the player first (ClientUISystem::ExecuteTargetModeForItem); the shell has no target mode, so the click selects");
        select(guid);
        return true;
    }

    /// <summary>The body-part mask under the mouse on the figure's click map (<c>CreateClickMap</c> 0x004A4BC0), or null when the map is not ready or the mouse is off it.</summary>
    private uint? MaskAtMouse()
    {
        if (_clickMask is null)
        {
            return null;
        }

        var did = _context.CharGen?.View3D?.UiAssetDid(ClickMapEnum) ?? 0;
        if (did == 0 || _context.Manager.Services.Images is not { } images || images.TryGetImage(did, out var image) != UiImageLookup.Ready || image is null)
        {
            _context.Manager.LogOnce("paperdoll:clickmap", LogSeverity.Warning, "TODO(m4-paperdoll-figure-click): the figure's click map (CreateClickMap 0x004A4BC0, UI asset enum 0x1000000C) is not available; the body-part click selects and examines nothing");
            return null;
        }

        var (mx, my) = _context.Manager.Input.MousePosition;
        var screen = _clickMask.ScreenBox;
        var x = mx - screen.X0;
        var y = my - screen.Y0;
        if (x < 0 || y < 0 || x >= image.Width || y >= image.Height)
        {
            return null;
        }

        var at = (y * image.Width + x) * 4;
        return MaskOfColor(image.Pixels[at], image.Pixels[at + 1], image.Pixels[at + 2]);
    }

    /// <summary>Examines what covers <paramref name="mask"/> (a test calls this with a location mask instead of a pixel).</summary>
    internal bool ExamineMask(uint mask, GameExaminePanel examine)
    {
        var session = _context.Session;
        var guid = UpperItemAt(session.State.Self.Inventory.Equipped, mask, session.State.Self.Guid);
        return guid != 0 && examine.Open(guid);
    }

    private static PaperDollClickEntry[] BuildClickMap()
    {
        const uint pairs = (uint)(EquipMask.WristWearLeft | EquipMask.WristWearRight | EquipMask.FingerWearLeft | EquipMask.FingerWearRight);
        var map = new PaperDollClickEntry[SlotTable.Count];
        for (var i = 0; i < map.Length; i++)
        {
            var slot = SlotTable[i];
            map[i] = new PaperDollClickEntry(slot.ElementId, slot.Name, (EquipMask)slot.Mask, slot.Side, slot.Armour, (slot.Mask & pairs) != 0 ? PaperDollDropRule.FreeSide : PaperDollDropRule.SlotLocations);
        }

        return map;
    }
}

/// <summary>
/// A paperdoll slot as a drop target: an item the player holds, whose valid locations include the slot's, is wielded (<see cref="IActionApi.Wield"/>, <see cref="ActionSource.Ui"/>) at the mask <see cref="GameDrops.WieldMask"/> gives. An item that is not
/// held (in the open container or on the ground) is refused ("pick it up first": <c>TODO(m4-drag-wield-from-container)</c>), as is an item worn at the slot already and, outside peace, one that cannot be held in combat (<see cref="GameInventoryPage.HoldRefused"/>).
/// </summary>
public sealed class GamePaperDollDrop(UiLoginContext context, PaperDollSlot slot) : IUiDropTarget
{
    /// <summary>The slot this target stands for.</summary>
    public PaperDollSlot Slot => slot;

    /// <inheritdoc/>
    public bool HighlightsCell => true;

    /// <summary>The last wield a drop sent, or null.</summary>
    public ActionHandle? LastAction { get; private set; }

    /// <summary>How many drops this slot sent.</summary>
    public int Drops { get; private set; }

    /// <summary>The verdict for <paramref name="item"/> and the mask it would be wielded at (0 unless <see cref="DropVerdict.Ok"/>).</summary>
    public DropVerdict Check(uint item, out uint mask)
    {
        mask = 0;
        var session = context.Session;
        var inventory = session.State.Self.Inventory;
        if (item == 0 || !inventory.Find(item).Held)
        {
            return DropVerdict.NotHeld;
        }

        if (!session.State.Objects.TryGet(item, out var obj))
        {
            return DropVerdict.DoesNotFit;
        }

        var valid = obj.ItemFields().ValidLocations;

        // gmPaperDollUI::AcceptDragObject (0x004A3E80): a melee weapon dropped on the shield slot is wielded there, in the off hand (the left side), and only there.
        var offHand = slot.Mask == (uint)EquipMask.Shield && (valid & (uint)EquipMask.MeleeWeapon) != 0;
        var choice = offHand ? (uint)EquipMask.Shield : GameDrops.WieldMask(inventory, slot.Mask, valid);
        if (choice == 0 || (inventory.TryGetEquipped(item, out var worn) && (worn.Location & slot.Mask) != 0))
        {
            return DropVerdict.DoesNotFit;
        }

        if (GameInventoryPage.HoldRefused(session.State.Self.Qualities.CombatMode, valid, obj.ItemType))
        {
            return DropVerdict.InCombat;
        }

        mask = choice;
        return DropVerdict.Ok;
    }

    /// <inheritdoc/>
    public bool Accepts(in UiDragPayload drag, UiItem? cell) => Check(drag.ItemId, out _) == DropVerdict.Ok;

    /// <inheritdoc/>
    public string? Refusal(in UiDragPayload drag, UiItem? cell)
    {
        var verdict = Check(drag.ItemId, out _);
        if (verdict == DropVerdict.NotHeld)
        {
            // Called once, when the release is refused (UiDrag.Release): the loud stub names itself where the behaviour happens, once per run.
            context.Manager.LogOnce("drag:wield-from-container", LogSeverity.Warning, "TODO(m4-drag-wield-from-container): an item in the open container or on the ground cannot be wielded by dropping it on a paperdoll slot; pick it up first (whether retail wields straight from there is not read)");
        }

        return GameDrops.Describe(verdict);
    }

    /// <inheritdoc/>
    public void Drop(in UiDragPayload drag, UiItem? cell)
    {
        if (Check(drag.ItemId, out var mask) != DropVerdict.Ok)
        {
            return;
        }

        Drops++;
        LastAction = context.Session.Actions.Wield(drag.ItemId, (EquipMask)mask, ActionSource.Ui);
        context.Manager.Services.Log.Info("retailui", string.Create(CultureInfo.InvariantCulture, $"drag: item 0x{drag.ItemId:X8} dropped on the {slot.Name} slot: {LastAction.Summary}"));
    }
}

/// <summary>
/// The figure as a drop target (M5 7 D122; <c>gmPaperDollUI::HandleDropRelease</c> 0x004A50F0 over the click mask 0x100001D6, then
/// <c>AcceptPaperDollDragObject</c> 0x004A4DE0): a carried item that is clothing, armour or a cloak (valid locations within 0x8007FFF) is
/// worn (<c>CPlayerSystem::AutoWear</c>: its locations whole, as a double click wears it, <see cref="WieldSlotChoice"/>); anything else is
/// refused with retail's "You can't put that item there" (notice 0x1A, a chat line through <see cref="Notice"/>).
/// </summary>
public sealed class GamePaperDollFigureDrop(UiLoginContext context) : IUiDropTarget
{
    /// <summary>The wearable locations the figure takes: clothing, armour and the cloak.</summary>
    public const uint WearLocations = 0x8007FFF;

    /// <summary>Retail's refusal (<c>AcceptPaperDollDragObject</c>).</summary>
    public const string CannotPutThere = "You can't put that item there";

    /// <summary>Where the refusal is shown (the shell's chat); null logs it only.</summary>
    public Action<string>? Notice { get; set; }

    /// <summary>The last wear a drop sent, or null.</summary>
    public ActionHandle? LastAction { get; private set; }

    /// <inheritdoc/>
    public bool HighlightsCell => false;

    /// <summary>The verdict for <paramref name="item"/> and the mask it would be worn at (0 unless <see cref="DropVerdict.Ok"/>).</summary>
    public DropVerdict Check(uint item, out uint mask)
    {
        mask = 0;
        var session = context.Session;
        var inventory = session.State.Self.Inventory;
        if (item == 0 || !inventory.Find(item).Held)
        {
            return DropVerdict.NotHeld;
        }

        if (inventory.TryGetEquipped(item, out _))
        {
            return DropVerdict.AlreadyThere; // a worn item let go over the body stays worn, without a word
        }

        if (!session.State.Objects.TryGet(item, out var obj) || (obj.ItemFields().ValidLocations & WearLocations) == 0)
        {
            return DropVerdict.DoesNotFit;
        }

        mask = WieldSlotChoice.Choose(inventory, obj.ItemFields().ValidLocations);
        return DropVerdict.Ok;
    }

    /// <inheritdoc/>
    public bool Accepts(in UiDragPayload drag, UiItem? cell) => Check(drag.ItemId, out _) == DropVerdict.Ok;

    /// <inheritdoc/>
    public string? Refusal(in UiDragPayload drag, UiItem? cell)
    {
        var verdict = Check(drag.ItemId, out _);
        if (verdict == DropVerdict.DoesNotFit && Notice is { } notice)
        {
            notice(CannotPutThere);
        }

        return GameDrops.Describe(verdict);
    }

    /// <inheritdoc/>
    public void Drop(in UiDragPayload drag, UiItem? cell)
    {
        if (Check(drag.ItemId, out var mask) != DropVerdict.Ok)
        {
            return;
        }

        LastAction = context.Session.Actions.Wield(drag.ItemId, (EquipMask)mask, ActionSource.Ui);
        context.Manager.Services.Log.Info("retailui", string.Create(CultureInfo.InvariantCulture, $"drag: item 0x{drag.ItemId:X8} dropped on the figure: {LastAction.Summary}"));
    }
}

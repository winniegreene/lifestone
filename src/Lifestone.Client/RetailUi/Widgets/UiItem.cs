using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Data.Dat;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Widgets;

/// <summary>
/// What a cell needs to draw one item's icon (the fields of the object's CreateObject WeenieDesc that retail's <c>IconData::RenderIcons</c>, 11.6096 0x0058DFB0, reads): the icon, the custom underlay and overlay
/// (DIDs, 0 for none), the item type (the background by type) and the UI effects word (the magical glow), plus the stack size and the names the tooltip uses. The owner builds one per item; the cell keeps nothing of the object.
/// </summary>
/// <param name="IconId">The icon (<c>WeenieDesc.IconId</c>, a RenderSurface DID).</param>
/// <param name="UnderlayId">The custom underlay (<c>IconUnderlayId</c>), or 0.</param>
/// <param name="OverlayId">The custom overlay (<c>IconOverlayId</c>), or 0.</param>
/// <param name="ItemType">The item type word (<c>WeenieDesc.ItemType</c>); its lowest set bit picks the background.</param>
/// <param name="UiEffects">The UI effects word (<c>WeenieDesc.UiEffects</c>; Magical is bit 0); its lowest set bit picks the glow texture. 0 for none.</param>
/// <param name="StackSize">The stack size; a number is drawn for a stack of more than one.</param>
/// <param name="Name">The name for the tooltip, or null.</param>
/// <param name="PluralName">The plural name the tooltip uses for a stack, or null (the name is used).</param>
/// <param name="ItemsCapacity">A container's capacity in items, or 0: the capacity bar shows only for a container with one.</param>
/// <param name="Contained">How many items the container holds now (the capacity bar's level over <paramref name="ItemsCapacity"/>).</param>
/// <param name="Structure">The remaining structure.</param>
/// <param name="MaxStructure">The full structure, or 0 for an object without one (no structure bar).</param>
public readonly record struct UiItemIcon(uint IconId, uint UnderlayId, uint OverlayId, uint ItemType, uint UiEffects, int StackSize = 1, string? Name = null, string? PluralName = null, int ItemsCapacity = 0, int Contained = 0, int Structure = 0, int MaxStructure = 0)
{
    /// <summary>The fields that change the picture (the stack and the names do not).</summary>
    internal (uint, uint, uint, uint, uint) Key => (IconId, UnderlayId, OverlayId, ItemType, UiEffects);
}

/// <summary>
/// The Item (retail's <c>UIElement_UIItem</c>, class 0x10000032; 30 functions): one cell of an item list, a <see cref="UiField"/> whose template (layout enum 0x10000038) holds the icon (0x1000033B), the overlays group
/// (0x1000033C, with the selected frame 0x10000342 and the quantity text 0x100004F5 in it) and the drag-accept frame (0x1000045A). A cell is empty (<see cref="EmptyState"/> 0x1000001C: the layout's empty-slot art), shows
/// an item (<see cref="OccupiedState"/> 0x1000001D) or shows a spell.
/// </summary>
/// <remarks>
/// <para>
/// <b>The icon</b> (<c>UIElement_UIItem::UIItem_SetIcon</c> 11.6096 0x004E2D10, <c>UIItem_Update</c> 0x004E2E60, <c>IconData::RenderIcons</c> 0x0058DFB0 over the object's icon data, 0x0058EC60): an item's picture is composed once
/// into 32 by 32 pixels. First the icon is copied and the custom overlay alpha-blended over it (four-channel alpha); then every pure white pixel of that is replaced by the pixel at the same place of the UI-effects
/// texture (<c>SurfaceWindow::ReplaceColor</c>, 11.4186 0x004415B0: enum group 0x10000005 by the lowest set bit of the effects plus one, entry 33 when the effects are 0 or the entry is missing: this is the magical glow). The cell's
/// picture is the type background (enum group 0x10000004 by the lowest set bit of the item type plus one, entry 33 for type 0) copied, the custom underlay blended over it (three-channel alpha), and that composite blended over
/// (three-channel alpha). The element draws it with a plain copy. A spell is drawn as its icon alone with three-channel alpha (<c>UIItem_SetIcon</c>'s spell branch), and its name goes in the text child 0x10000344 when the
/// template has one. The composition runs when the picture's fields change, never per frame; an image that is still decoding (<see cref="UiImageLookup.Pending"/>) makes the cell try again each frame until it is ready.
/// </para>
/// <para>
/// <b>Stack count.</b> The text 0x100004F5 shows <see cref="UiItemIcon.StackSize"/> for a stack of more than one and is hidden otherwise (<c>UpdateQuantityDisplay</c> 0x004E2A30 shows the cell's quantity when it is not
/// negative). 11.4186's <c>UIItem_Update</c> sets the quantity only for a vendor's lines; Lifestone sets it from the stack size of a pack item, as retail's pack shows (<c>TODO(m3-quantity-source)</c>: confirm in 11.6096's
/// <c>UIItem_Update</c>). The tooltip is "N name" for a stack (<c>UpdateTooltip</c> 0x004E2940).
/// </para>
/// <para>
/// <b>Selected and ghosted.</b> <see cref="SetSelected"/> shows the selected frame (<c>SetSelectedState</c> 0x004E1ED0; a cell that is not selectable ignores a select; see <see cref="SelectedFrameShown"/>); <see cref="SetGhosted"/> shows the ghosted frame (the waiting
/// state, <c>SetWaitingState</c> 0x004E1E40). The sell mark is built (<see cref="SetSellState"/>, M4 3.2 s4b). Not built: the trade mark (<c>TODO(m3-uiitem-extras)</c>).
/// </para>
/// <para>
/// <b>The shortcut number and the cooldown overlay</b> (M4 3.2 h2; the share of <c>TODO(m3-uiitem-extras)</c> the hotbar needed). <see cref="SetShortcutNumber"/> is <c>UIElement_UIItem::SetShortcutNum</c> (11.6096 0x004E2220): the child 0x1000034A shows the image
/// that slot picks from an array property of its own, 0x10000042 for a cell with an item, 0x10000043 for a ghosted one, 0x1000005E for an empty cell; the dats name the art (nine digit images and nine blanks per array), nothing is a placeholder. An empty cell shows no corner number: the toolbar template has no empty array (0x1000005E), so retail's lookup changes nothing, and the number of an empty slot is
/// baked into that slot's empty-cell art (a large numeral in each list's own template). <see cref="SetCooldown"/>
/// is <c>UpdateCooldownDisplay</c> (0x004E2AB0): ten stacked images 0x1000054F to 0x10000558, the one for the phase (a tenth of the cooldown left, 1 to 10) shows. Retail takes the phase from the item's cooldown in the enchantment registry; here the owner sets it
/// (<c>TODO(m4-shortcut-cooldown-source)</c>).
/// </para>
/// <para>
/// <b>Drag.</b> A cell of a list that is a drag source (<see cref="UiItemList.DragSource"/>) is taken on a press and carried by <see cref="UiDrag"/> (M4 2.2 s3). A cell under the dragged item shows the
/// drag-accept frame (<see cref="DragAcceptId"/>, 0x1000045A) when the target takes the item, and the field's own drag-rollover states (9 accept, 10 reject) when the cell's desc has them
/// (<see cref="ShowDragRollover"/>, <see cref="ClearDragRollover"/>; <c>TODO(m4-retail-drag)</c>, address unknown).
/// </para>
/// </remarks>
public class UiItem : UiField
{
    /// <summary>The class id, 0x10000032 (<c>UIElement_UIItem</c>).</summary>
    public const uint RetailClassId = 0x10000032;

    /// <summary>The state of an empty cell (0x1000001C): the empty-slot art.</summary>
    public const uint EmptyState = 0x1000001C;

    /// <summary>The state of a cell that shows an object (0x1000001D).</summary>
    public const uint OccupiedState = 0x1000001D;

    /// <summary>The icon child (<c>m_elem_Icon</c>).</summary>
    public const uint IconId = 0x1000033B;

    /// <summary>The overlays group (<c>m_elem_Icon_Overlays</c>).</summary>
    public const uint OverlaysId = 0x1000033C;

    /// <summary>The selected frame (<c>m_elem_Icon_Selected</c>).</summary>
    public const uint SelectedId = 0x10000342;

    /// <summary>The ghosted frame (<c>m_elem_Icon_Ghosted</c>).</summary>
    public const uint GhostedId = 0x10000349;

    /// <summary>The name text a spell cell shows (<c>m_elem_Text</c>), when the template has one.</summary>
    public const uint NameTextId = 0x10000344;

    /// <summary>The stack count text (<c>m_elem_Icon_Quantity</c>).</summary>
    public const uint QuantityTextId = 0x100004F5;

    /// <summary>The capacity bar of a container (<c>m_elem_Icon_CapacityBar</c>, a meter).</summary>
    public const uint CapacityBarId = 0x10000347;

    /// <summary>The structure bar (<c>m_elem_Icon_StructureBar</c>, a meter).</summary>
    public const uint StructureBarId = 0x10000348;

    /// <summary>The open-container frame (<c>m_elem_Icon_OpenContainer</c>).</summary>
    public const uint OpenContainerId = 0x10000450;

    /// <summary>The shortcut number child (<c>m_elem_Icon_ShortcutNum</c>).</summary>
    public const uint ShortcutNumberId = 0x1000034A;

    /// <summary>The shortcut number's array of images for a cell that shows an item (property 0x10000042 of the child; one entry per slot).</summary>
    public const uint ShortcutDigitsProperty = 0x10000042;

    /// <summary>The same array for a ghosted cell (property 0x10000043).</summary>
    public const uint ShortcutDigitsGhostedProperty = 0x10000043;

    /// <summary>The same array for an empty cell (property 0x1000005E; the toolbar's template does not carry it: an empty slot's number is in its empty-cell art).</summary>
    public const uint ShortcutDigitsEmptyProperty = 0x1000005E;

    /// <summary>The first cooldown step, <c>m_elem_Icon_Cooldown_10</c>; the ten steps are the ids 0x1000054F to 0x10000558 (10 to 100 per cent).</summary>
    public const uint CooldownFirstId = 0x1000054F;

    /// <summary>The number of cooldown steps.</summary>
    public const int CooldownSteps = 10;


    /// <summary>The drag-accept frame (0x1000045A): shown over a cell that takes the item being dragged over it.</summary>
    public const uint DragAcceptId = 0x1000045A;

    /// <summary>
    /// The sell mark (0x10000437; 11.4186 <c>UIElement_UIItem::PostInit</c> finds it as <c>m_elem_Icon_SellState</c>, and <c>UIItem_Update</c> 0x004E2E60's sell-state branch shows it while the object's sell state is not 0: M4 3.2 s4b).
    /// The vendor window sets the state of the items in its sell list (<c>gmVendorUI::VendorItemSetSellState</c> 11.6096 0x004C0FA0 over <c>ACCWeenieObject::SetSellState</c>, which re-draws every cell of the object).
    /// </summary>
    public const uint SellMarkId = 0x10000437;

    private UiElement? _sellMark;
    private bool _sellState;
    private UiElement? _dragAccept;
    private UiElement? _icon;
    private UiElement? _overlays;
    private UiElement? _selected;
    private UiElement? _ghosted;
    private UiElement? _openContainer;
    private UiTextElement? _quantity;
    private UiTextElement? _nameText;
    private UiMeter? _capacityBar;
    private UiMeter? _structureBar;
    private UiElement? _shortcutNumber;
    private readonly UiElement?[] _cooldown = new UiElement?[CooldownSteps];
    private bool _selectable = true;
    private bool _isSelected;
    private bool _frameRegistered;
    private (uint, uint, uint, uint, uint) _composedKey;
    private bool _composed;
    private int _quantityShown = -1;
    private (UiItemIcon Icon, bool Has) _lastIcon;

    /// <summary><c>UIElement_UIItem::UIElement_UIItem</c> (11.6096 0x004E1BD0): an empty cell, selectable.</summary>
    public UiItem(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>The object this cell shows (<c>itemID</c>), or 0.</summary>
    public uint ItemId { get; private set; }

    /// <summary>The spell this cell shows (<c>spellID</c>), or 0.</summary>
    public uint SpellId { get; private set; }

    /// <summary>True when the cell shows neither an object nor a spell.</summary>
    public bool IsEmpty => ItemId == 0 && SpellId == 0;

    /// <summary>True when the cell is selected (<c>selected</c>).</summary>
    public bool IsSelected => _isSelected;

    /// <summary>True when a select can take (<c>m_selectable</c>).</summary>
    public bool Selectable => _selectable;

    /// <summary>The stack count the cell shows, or -1 when it shows none (<c>m_quantity</c>).</summary>
    public int Quantity => _quantityShown;

    /// <summary>True while the picture waits for an image that is still decoding.</summary>
    public bool IconPending { get; private set; }

    /// <summary>The picture the cell composed for its item (32 by 32), or null (a spell or an empty cell, or nothing decoded yet). For tests and the owner's checks.</summary>
    public UiImage? ComposedIcon { get; private set; }

    /// <summary>How many times the picture was composed (the cell composes only when the picture's fields change).</summary>
    public int Compositions { get; private set; }

    /// <summary>The drag-accept frame child, or null when the template has none.</summary>
    public UiElement? DragAcceptFrame => _dragAccept;

    /// <summary>What a drag over this cell says: nothing, accept or reject (<see cref="ShowDragRollover"/>).</summary>
    public UiDragRollover DragRollover { get; private set; }

    /// <summary>The icon the cell shows now (the fields <see cref="ShowItem"/> was given), or false for an empty cell or a spell.</summary>
    public bool TryGetIcon(out UiItemIcon icon)
    {
        icon = _lastIcon.Icon;
        return _lastIcon.Has && ItemId != 0;
    }

    /// <summary>The icon child, or null when the template has none.</summary>
    public UiElement? IconElement => _icon;

    /// <summary>The quantity text child, or null.</summary>
    public UiTextElement? QuantityText => _quantity;

    /// <summary>Raised when the cell is deleted with its tree (the owner of a window of cells ends with it: the hotbar, which the screen does not dispose, M4 3.2 h2).</summary>
    public event Action<UiItem>? Deleting;

    /// <inheritdoc/>
    protected internal override void OnDeleting()
    {
        base.OnDeleting();
        Deleting?.Invoke(this);
    }

    /// <summary>The slot whose number the cell shows (<see cref="SetShortcutNumber"/>), or -1.</summary>
    public int ShortcutNumber { get; private set; } = -1;

    /// <summary>The image the shortcut number shows now (0 for none).</summary>
    public uint ShortcutNumberImage { get; private set; }

    /// <summary>The shortcut number child, or null when the template has none.</summary>
    public UiElement? ShortcutNumberElement => _shortcutNumber;

    /// <summary>The cooldown phase the cell shows (<see cref="SetCooldown"/>): 0 for none, 1 to 10.</summary>
    public int CooldownPhase { get; private set; }

    /// <summary>The cooldown step child for <paramref name="phase"/> (1 to 10), or null.</summary>
    public UiElement? CooldownElement(int phase) => phase is >= 1 and <= CooldownSteps ? _cooldown[phase - 1] : null;

    /// <summary>The capacity bar child, or null.</summary>
    public UiMeter? CapacityBar => _capacityBar;

    /// <summary>The structure bar child, or null.</summary>
    public UiMeter? StructureBar => _structureBar;

    /// <summary>The selected frame child, or null.</summary>
    public UiElement? SelectedFrame => _selected;

    /// <summary>The ghosted frame child, or null.</summary>
    public UiElement? GhostedFrame => _ghosted;

    /// <summary><c>UIElement_UIItem::PostInit</c> (11.6096 0x004E2500): finds the children by id; the cell starts empty.</summary>
    protected internal override void PostInit()
    {
        base.PostInit();
        _icon = GetChildRecursive(IconId);
        _overlays = GetChildRecursive(OverlaysId);
        _selected = GetChildRecursive(SelectedId);
        _ghosted = GetChildRecursive(GhostedId);
        _openContainer = GetChildRecursive(OpenContainerId);
        _dragAccept = GetChildRecursive(DragAcceptId);
        _dragAccept?.SetVisible(false);
        _sellMark = GetChildRecursive(SellMarkId);
        _sellMark?.SetVisible(false);
        _quantity = GetChildRecursive(QuantityTextId) as UiTextElement;
        _nameText = GetChildRecursive(NameTextId) as UiTextElement;
        _capacityBar = GetChildRecursive(CapacityBarId) as UiMeter;
        _structureBar = GetChildRecursive(StructureBarId) as UiMeter;
        _shortcutNumber = GetChildRecursive(ShortcutNumberId);
        for (var i = 0; i < CooldownSteps; i++)
        {
            _cooldown[i] = GetChildRecursive(CooldownFirstId + (uint)i);
        }

        SetCellState(EmptyState);
    }

    /// <summary>
    /// <c>UIElement_UIItem::SetShortcutNum</c> (11.6096 0x004E2220): the cell shows the number of shortcut slot <paramref name="slot"/> (0 to 17), or none for a negative slot. The image is the slot's entry of the child's array property: 0x10000043 when
    /// <paramref name="ghosted"/>, 0x1000005E for an empty cell, else 0x10000042. A lookup that finds no array leaves the child hidden (as retail's changes nothing): the toolbar's cells have none for the empty state, because an empty slot's number is part of its
    /// own empty-cell art.
    /// </summary>
    public void SetShortcutNumber(int slot, bool ghosted)
    {
        if (_shortcutNumber is not { } element)
        {
            return;
        }

        if (slot < 0)
        {
            ShortcutNumber = -1;
            ShortcutNumberImage = 0;
            if (element.Visible)
            {
                element.SetVisible(false);
            }

            return;
        }

        var key = IsEmpty ? ShortcutDigitsEmptyProperty : ghosted ? ShortcutDigitsGhostedProperty : ShortcutDigitsProperty;
        element.InqProperty(key, out var property);
        uint did = 0;
        if (property is UiArrayProperty array && slot < array.Items.Count && array.Items[slot] is UiDataIdProperty id)
        {
            did = id.Value;
        }

        ShortcutNumber = slot;
        ShortcutNumberImage = did;
        if (did != 0)
        {
            element.SetImageByDid(did, UiDrawMode.AlphaBlend);
        }

        element.SetVisible(did != 0);
    }

    /// <summary>
    /// <c>UpdateCooldownDisplay</c> (11.6096 0x004E2AB0): the cooldown image for <paramref name="phase"/> shows (1 to 10, a tenth of the cooldown per step; 10 and above are the last), the others hide; 0 hides all. Retail reads the phase from the item's cooldown in the
    /// enchantment registry (<c>CEnchantmentRegistry::OnCooldown</c> 0x00595230); the owner sets it here.
    /// </summary>
    public void SetCooldown(int phase)
    {
        phase = Math.Clamp(phase, 0, CooldownSteps);
        CooldownPhase = phase;
        for (var i = 0; i < CooldownSteps; i++)
        {
            var show = phase == i + 1;
            if (_cooldown[i] is { } step && step.Visible != show)
            {
                step.SetVisible(show);
            }
        }
    }

    /// <summary>
    /// <c>UIElement_UIItem::Clear_UIItem</c> (11.6096 0x004E1E00) then <c>UIItem_SetState</c> (0x004E2190) with the empty state: the cell shows its empty-slot art, no selection, no count, no tooltip.
    /// </summary>
    public void Clear()
    {
        ItemId = 0;
        SpellId = 0;
        _composed = false;
        ComposedIcon = null;
        IconPending = false;
        _icon?.ClearImage();
        SetCellState(EmptyState);
        ShowQuantity(-1);
        SetTooltip(null);
        SetSellState(false); // an emptied cell carries no sell mark
        UpdateMouseVisibility();
    }

    /// <summary>
    /// <c>Init_UIItem</c> (11.6096 0x004E1DA0) then <c>UIItem_SetState</c> with the occupied state and <c>UIItem_Update</c> (0x004E2E60): the cell shows object <paramref name="itemId"/> with the picture and the stack count
    /// of <paramref name="icon"/>. Showing the same picture again composes nothing.
    /// </summary>
    public void ShowItem(uint itemId, in UiItemIcon icon)
    {
        if (ItemId == itemId && SpellId == 0 && _lastIcon.Has && _lastIcon.Icon == icon && !IconPending)
        {
            return;
        }

        ItemId = itemId;
        SpellId = 0;
        SetCellState(OccupiedState);
        Compose(icon);
        ShowQuantity(icon.StackSize > 1 ? icon.StackSize : -1);
        ShowBars(icon);
        var name = icon.Name;
        if (name is null)
        {
            SetTooltip(null);
        }
        else
        {
            var shown = icon.StackSize > 1 ? string.Create(CultureInfo.InvariantCulture, $"{icon.StackSize} {icon.PluralName ?? name}") : name;
            SetTooltip(UiStringRef.FromLiteral(shown));
        }

        UpdateMouseVisibility();
    }

    /// <summary>
    /// <c>Init_UIItem_Spell_Shortcut</c> (11.6096 0x004E1DD0) and <c>UIItem_SetIcon</c>'s spell branch: the cell shows spell <paramref name="spellId"/> with <paramref name="iconId"/> (a RenderSurface DID the owner took
    /// from the SpellTable) drawn with three-channel alpha, and <paramref name="name"/> in the name text when the template has one.
    /// </summary>
    public void ShowSpell(uint spellId, uint iconId, string? name = null)
    {
        ItemId = 0;
        SpellId = spellId;
        _composed = false;
        ComposedIcon = null;
        SetCellState(OccupiedState);
        if (_icon is { } icon)
        {
            _icon.ClearImage();
            icon.SetBlitMode(UiBlitMode.ThreeChannelAlpha);
            icon.SetImageByDid(iconId, UiDrawMode.Overlay);
        }

        _nameText?.SetText(name ?? string.Empty);
        ShowQuantity(-1);
        ShowBars(default);
        SetTooltip(name is null ? null : UiStringRef.FromLiteral(name));
        UpdateMouseVisibility();
    }

    /// <summary>The cell shows spell <paramref name="spellId"/> with a picture the owner composed itself (a 32 by 32 image); the name is as in <see cref="ShowSpell(uint, uint, string?)"/>.</summary>
    public void ShowSpell(uint spellId, UiImage picture, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ShowSpell(spellId, 0, name);
        _icon?.SetGeneratedImage(picture);
    }

    /// <summary><c>SetSelectedState</c> (11.6096 0x004E1ED0): a deselect always takes, a select only when the cell is selectable; the selected frame follows.</summary>
    public void SetSelected(bool selected)
    {
        if (selected && !_selectable)
        {
            return;
        }

        _isSelected = selected;
        ShowSelectedFrame();
    }

    /// <summary>True when the selected frame's picture shows (the frame is drawn at full strength and the cell is not empty).</summary>
    public bool SelectedFrameShown => _selected is { } frame && frame.Visible && frame.AlphaBlendMod > 0f && !IsEmpty;

    private bool SelectedHoldsCount => _selected is { Children.Count: > 0 };

    /// <summary>
    /// When the template nests the count in the frame, the frame hides by its picture alone (blend 0), not by its visibility: the template nests the stack count text in the selected frame (0x100004F5 under 0x10000342), so hiding the element would hide the count of every
    /// unselected item. <c>TODO(m3-quantity-source)</c>: how retail shows the count with the frame hidden is not read.
    /// </summary>
    private void ShowSelectedFrame()
    {
        if (_selected is not { } frame)
        {
            return;
        }

        var shown = _isSelected && !IsEmpty;
        if (SelectedHoldsCount)
        {
            frame.SetVisible(true);
            frame.SetAlphaBlendMod(shown ? 1f : 0f);
        }
        else
        {
            frame.SetAlphaBlendMod(1f);
            frame.SetVisible(shown); // a template whose frame holds no count hides it plainly
        }
    }

    /// <summary>
    /// A dragged item is over this cell (<see cref="UiDrag"/>): <paramref name="accept"/> shows the drag-accept frame and the field's accept state; a reject shows the reject state only (the states are the cell's desc's; a state the desc
    /// lacks is the base state, which the cell then restores, so the cell never loses its picture).
    /// </summary>
    public void ShowDragRollover(bool accept)
    {
        var rollover = accept ? UiDragRollover.Accept : UiDragRollover.Reject;
        if (DragRollover == rollover)
        {
            return;
        }

        ClearDragRollover();
        DragRollover = rollover;
        if (accept)
        {
            _dragAccept?.SetVisible(true);
        }

        var state = accept ? UiStateIds.DragRolloverAccept : UiStateIds.DragRolloverReject;
        if (Desc.States.ContainsKey(state))
        {
            EnterDragRollover(accept);
        }
    }

    /// <summary>The drag over this cell ended or moved on: the frame hides and the cell goes back to the state it had (its icon and overlay states are applied again).</summary>
    public void ClearDragRollover()
    {
        if (DragRollover == UiDragRollover.None)
        {
            return;
        }

        DragRollover = UiDragRollover.None;
        _dragAccept?.SetVisible(false);
        if (LeaveDragRollover())
        {
            SetCellState(IsEmpty ? EmptyState : OccupiedState); // a rollover state that passes to the children moved their states with it
        }
    }

    /// <summary><c>SetSelectableState</c> (11.6096 0x004E1F10).</summary>
    public void SetSelectable(bool selectable) => _selectable = selectable;

    /// <summary><c>SetWaitingState</c> (11.6096 0x004E1E40): the ghosted frame shows while a request for the item is out.</summary>
    public void SetGhosted(bool ghosted) => _ghosted?.SetVisible(ghosted);

    /// <summary><c>SetOpenContainerState</c> (11.6096 0x004E1EC0): the open-container frame of a container the list shows opened.</summary>
    public void SetOpenContainer(bool open)
    {
        if (_openContainer is { } frame && frame.Visible != open)
        {
            frame.SetVisible(open);
        }
    }

    /// <summary>True when the open-container frame shows.</summary>
    public bool OpenContainerShown => _openContainer?.Visible == true;

    /// <summary>
    /// The sell mark (M4 3.2 s4b): shows the cell's item as one in the vendor's sell list (<c>UIItem_Update</c>'s sell-state branch, 11.4186 0x004E2E60). Does nothing when the state is as it was; a template without the mark element draws none
    /// (<see cref="HasSellMark"/> reads false).
    /// </summary>
    public void SetSellState(bool selling)
    {
        if (_sellState == selling)
        {
            return;
        }

        _sellState = selling;
        _sellMark?.SetVisible(selling);
    }

    /// <summary>The sell state last set (the cell's item is in the vendor's sell list).</summary>
    public bool SellState => _sellState;

    /// <summary>True when the cell's template has the sell mark element.</summary>
    public bool HasSellMark => _sellMark is not null;

    /// <summary>True while the sell mark is drawn.</summary>
    public bool SellMarkShown => _sellMark?.Visible == true;

    /// <inheritdoc/>
    protected override bool GetShouldBeMouseVisible() => base.GetShouldBeMouseVisible() || !IsEmpty;

    /// <summary>Retries a picture that was waiting for an image, each frame, until it is ready.</summary>
    protected internal override void ListenToGlobalMessage(uint messageId, int data)
    {
        base.ListenToGlobalMessage(messageId, data);
        if (messageId == UiGlobalMessageIds.Frame && IconPending)
        {
            RetryPending();
        }
    }

    private void SetCellState(uint state)
    {
        _icon?.SetState(state);
        _nameText?.SetState(state);
        _overlays?.SetState(state);
        if (state == EmptyState)
        {
            _isSelected = false;
            _ghosted?.SetVisible(false);
            _openContainer?.SetVisible(false);
            _nameText?.SetText(string.Empty);
            _capacityBar?.SetVisible(false);
            _structureBar?.SetVisible(false);
        }

        ShowSelectedFrame();
    }

    /// <summary>
    /// <c>UpdateCapacityDisplay</c> (11.6096 0x004E2370) and <c>UpdateStructureDisplay</c> (0x004E2430): a container with a capacity shows how full it is (hidden while empty), an object with a structure shows how much is left
    /// (hidden while full); the other cells hide both bars. The level is meter property 0x69.
    /// </summary>
    private void ShowBars(in UiItemIcon icon)
    {
        if (_capacityBar is { } capacity)
        {
            if (icon.ItemsCapacity > 0 && icon.Contained > 0)
            {
                capacity.SetVisible(true);
                capacity.SetFloatProperty(UiPropertyIds.MeterPosition, Math.Clamp((float)icon.Contained / icon.ItemsCapacity, 0f, 1f));
            }
            else
            {
                capacity.SetVisible(false);
            }
        }

        if (_structureBar is { } structure)
        {
            if (icon.MaxStructure > 0 && icon.Structure != icon.MaxStructure)
            {
                structure.SetVisible(true);
                structure.SetFloatProperty(UiPropertyIds.MeterPosition, Math.Clamp((float)icon.Structure / icon.MaxStructure, 0f, 1f));
            }
            else
            {
                structure.SetVisible(false);
            }
        }
    }

    private void ShowQuantity(int quantity)
    {
        _quantityShown = quantity;
        if (_quantity is not { } text)
        {
            return;
        }

        if (quantity < 0)
        {
            text.SetVisible(false);
            return;
        }

        Manager.LogOnce("uiitem:quantity-source", LogSeverity.Warning, "TODO(m3-quantity-source): UIElement_UIItem draws m_quantity; 11.4186 sets it only for a vendor's lines, Lifestone sets it from a pack item's stack size (confirm in 11.6096 UIItem_Update)");
        text.SetText(quantity.ToString(CultureInfo.InvariantCulture));
        text.SetVisible(true);
    }

    private void Compose(in UiItemIcon icon)
    {
        if (_icon is null)
        {
            return;
        }

        _lastIcon = (icon, true);
        if (_composed && _composedKey == icon.Key)
        {
            return;
        }

        var image = UiItemIcons.Compose(Manager, icon, out var pending);
        if (pending)
        {
            IconPending = true;
            RegisterFrame();
            return;
        }

        IconPending = false;
        _composed = true;
        _composedKey = icon.Key;
        ComposedIcon = image;
        Compositions++;
        _icon.SetBlitMode(UiBlitMode.SrcCopy);
        _icon.SetGeneratedImage(image);
    }

    private void RetryPending()
    {
        if (_lastIcon.Has && ItemId != 0)
        {
            Compose(_lastIcon.Icon);
        }

        if (!IconPending && _frameRegistered)
        {
            _frameRegistered = false;
            Manager.UnregisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        }
    }

    private void RegisterFrame()
    {
        if (!_frameRegistered)
        {
            _frameRegistered = true;
            Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        }
    }
}

/// <summary>What a drag over a <see cref="UiItem"/> says about it.</summary>
public enum UiDragRollover
{
    /// <summary>No drag is over the cell.</summary>
    None,

    /// <summary>The target takes the dragged item.</summary>
    Accept,

    /// <summary>The target refuses the dragged item.</summary>
    Reject,
}

/// <summary>The icon composition of <see cref="UiItem"/> (retail's <c>IconData::RenderIcons</c>, 11.6096 0x0058DFB0; see the cell's remarks).</summary>
internal static class UiItemIcons
{
    private const int Size = 32;
    private const uint TypeBackgrounds = 0x10000004;
    private const uint EffectTextures = 0x10000005;
    private const int NoneEntry = 33;

    /// <summary>The enum entry for a word: its lowest set bit plus one, or 33 for 0 (<c>LowestSetBit</c> answers -1 for 0, so the entry would be 0, which retail replaces with 33).</summary>
    internal static int EntryFor(uint word) => word == 0 ? NoneEntry : BitOperations.TrailingZeroCount(word) + 1;

    /// <summary>
    /// Composes the picture of <paramref name="icon"/>. <paramref name="pending"/> is true when an image is still decoding (nothing is returned and the caller asks again later). A missing image is left out, as retail
    /// leaves out a surface it cannot get, and logged once.
    /// </summary>
    internal static UiImage Compose(UiElementManager manager, in UiItemIcon icon, out bool pending)
    {
        var images = manager.Services.Images;
        pending = false;
        var iconImage = Get(manager, images, icon.IconId, ref pending);
        var overlay = Get(manager, images, icon.OverlayId, ref pending);
        var underlay = Get(manager, images, icon.UnderlayId, ref pending);
        var maps = manager.Services.EnumMaps;
        var background = maps is not null && maps.TryResolve(new EnumGroup(TypeBackgrounds), (uint)EntryFor(icon.ItemType), out var bgDid) ? Get(manager, images, bgDid, ref pending) : null;
        UiImage? effect = null;
        if (maps is not null)
        {
            var entry = icon.UiEffects == 0 ? NoneEntry : EntryFor(icon.UiEffects);
            if (!maps.TryResolve(new EnumGroup(EffectTextures), (uint)entry, out var effectDid))
            {
                maps.TryResolve(new EnumGroup(EffectTextures), NoneEntry, out effectDid);
            }

            effect = Get(manager, images, effectDid, ref pending);
        }

        if (pending)
        {
            return null!;
        }

        // The icon and its custom overlay (the drag icon of RenderIcons), the glow replacing the pure white pixels.
        var front = new UiSurface(Size, Size);
        if (iconImage is not null)
        {
            Blit(iconImage, front, UiBlitMode.SrcCopy);
        }

        if (overlay is not null)
        {
            Blit(overlay, front, UiBlitMode.FourChannelAlpha);
        }

        if (effect is not null)
        {
            ReplaceWhite(front, effect);
        }

        // The cell's picture: the type background, the custom underlay, then the front.
        var back = new UiSurface(Size, Size);
        if (background is not null)
        {
            Blit(background, back, UiBlitMode.SrcCopy);
        }

        if (underlay is not null)
        {
            Blit(underlay, back, UiBlitMode.ThreeChannelAlpha);
        }

        Blit(new UiImage(icon.IconId, Size, Size, UiPixelFormat.Argb, front.Pixels), back, UiBlitMode.ThreeChannelAlpha);
        return new UiImage(icon.IconId, Size, Size, UiPixelFormat.Argb, back.Pixels);
    }

    private static UiImage? Get(UiElementManager manager, IUiImageSource? images, uint did, ref bool pending)
    {
        if (did == 0 || images is null)
        {
            return null;
        }

        switch (images.TryGetImage(did, out var image))
        {
            case UiImageLookup.Ready:
                return image;
            case UiImageLookup.Pending:
                pending = true;
                return null;
            default:
                manager.LogOnce($"uiitem:missing:{did:X8}", LogSeverity.Warning, $"UIElement_UIItem: image 0x{did:X8} is missing; its layer is left out of the icon (retail leaves out a surface it cannot get)");
                return null;
        }
    }

    private static void Blit(UiImage source, UiSurface target, UiBlitMode mode) =>
        UiBlitter.Blit(source, 0, 0, target, 0, 0, Math.Min(Size, source.Width), Math.Min(Size, source.Height), mode, 1f);

    /// <summary><c>SurfaceWindow::ReplaceColor</c> (11.4186 0x004415B0): every pixel of <paramref name="target"/> equal to opaque white takes the pixel at the same place of <paramref name="texture"/> (which must be at least as big).</summary>
    internal static void ReplaceWhite(UiSurface target, UiImage texture)
    {
        if (texture.Width < target.Width || texture.Height < target.Height)
        {
            return;
        }

        var opaque = texture.Format != UiPixelFormat.Argb;
        var dp = target.Pixels;
        var sp = texture.Pixels;
        for (var y = 0; y < target.Height; y++)
        {
            for (var x = 0; x < target.Width; x++)
            {
                var d = ((y * target.Width) + x) * 4;
                if (dp[d] != 255 || dp[d + 1] != 255 || dp[d + 2] != 255 || dp[d + 3] != 255)
                {
                    continue;
                }

                var s = ((y * texture.Width) + x) * 4;
                dp[d] = sp[s];
                dp[d + 1] = sp[s + 1];
                dp[d + 2] = sp[s + 2];
                dp[d + 3] = opaque ? (byte)255 : sp[s + 3];
            }
        }
    }
}

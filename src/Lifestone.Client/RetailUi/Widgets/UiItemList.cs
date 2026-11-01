using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Widgets;

/// <summary>
/// The Item list (retail's <c>UIElement_ItemList</c>, class 0x10000031; 54 functions): a <see cref="UiListBox"/> whose rows are <see cref="UiItem"/> cells made at run time from the list's template element
/// (property 0x1000000E, in the item layout of enum 0x10000038). The pack, the side-pack column, the paperdoll's slots and the spell page all use it; a page fills it with <see cref="AddItem"/> and <see cref="AddSpell"/>, and hears a
/// click and a double click through <see cref="ItemSelected"/> and <see cref="ItemActivated"/>. The scroll bar, the columns and the layout are the list box's.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cells.</b> <c>InitItemList</c> (11.6096 0x004E4A00, called by <c>PostInit</c> 0x004E5930) reads the list's flags and makes the fixed number of empty cells (property 0x10000015; the pack's 102, the side-pack column's 7, a paperdoll
/// slot's 1; -1 for a list that grows). Lifestone makes them when the list is first shown: the first <see cref="InitialCells"/> at once and the rest <see cref="CellsPerFrame"/> a frame, so a page never builds the pack's
/// 102 cells in one unit of its construction. <c>ItemList_AddItem</c> (0x004E4A90) puts an item in the first empty cell (<c>UIItem_GetState</c> equal to the empty state) or, in a list that grows, in a new cell; in a fixed list
/// that is full it answers null. <c>ItemList_Flush</c> (0x004E49F0) empties every cell, clears the open container and the selection.
/// </para>
/// <para>
/// <b>Empty slots of a list that grows</b> (M4 3.2 lane e, <c>UpdateEmptySlots</c> 11.6096 0x004E4390, narrows <c>TODO(m3-itemlist-empty-fill)</c>: the pages that call it show the slots). A list whose cell count is -1 holds the items it is given, and a
/// single-row one (maximum columns 0x5F of -1: the container window's item row, the spellcasting window's bars) shows empty slot boxes after them to the edge of its width, so a corpse's window and a favourites bar read as rows of slots;
/// <see cref="UpdateEmptySlots"/> makes and trims them. A page calls it after it fills or empties the list: the fill is the list's, the page decides when (the spellbook page's list shows no empty slots and does not call it).
/// </para>
/// <para>
/// <b>Mouse.</b> <c>ListenToElementMessage</c> (0x004E59E0) hears the mouse-down message the cell under the mouse sends: a left button selects it (<c>HandleSingleSelection</c> 0x004E3EE0 first when the list shows each item once, flag
/// 0x10000052; then retail sets the selected object, and a container list opens the item in its child list), the double left button uses it (<see cref="ItemActivated"/>: the owner decides, a pack page wields a wearable item and uses any other, M4 2.2 s2), the right button selects it and examines it in every list (<see cref="Examine"/>; M5 7 S6 closes <c>TODO(m4-itemlist-examine)</c>), and while a target mode is on the left button runs it on the item (<see cref="UiTargetMode"/>).
/// Retail's selection is the player's one selected object; here a list keeps its own (<c>TODO(m3-selected-object)</c>) and tells the owner. <b>Drag and drop</b> (M4 2.2 s3, D4): a list whose owner sets
/// <see cref="DragSource"/> lets <see cref="UiDrag"/> carry one of its cells, and an owner registers a list as a drop target with <see cref="UiDrag.RegisterTarget"/>; retail's drag handlers, drop release, drag scrolling and
/// <c>AcceptDragObject</c> have no address in the docs (<c>TODO(m4-retail-drag)</c>), so the behaviour is re-expressed and the drag scrolling is not built. No vendor, salvage or shortcut list behaviour beyond the examination (<c>TODO(m3-itemlist-use)</c> is closed: the page that owns the list uses the item), no
/// empty-slot fill of a column or a grid that grows (<see cref="UpdateEmptySlots"/> keeps retail's rule: a column only loses its trailing empty cells) and no cache of removed cells (nothing removes one).
/// </para>
/// </remarks>
public class UiItemList : UiListBox
{
    /// <summary>The class id, 0x10000031 (<c>UIElement_ItemList</c>).</summary>
    public const uint RetailClassId = 0x10000031;

    /// <summary>The layout enum (group UILAYOUT) of the item cells' layout, 0x10000038.</summary>
    public const uint ItemLayoutEnum = 0x10000038;

    /// <summary>Property 0x1000000E: the element id of the cell template.</summary>
    public const uint TemplateProperty = 0x1000000E;

    /// <summary>Property 0x10000015: the number of cells (-1 for a list that grows).</summary>
    public const uint FixedSlotsProperty = 0x10000015;

    /// <summary>Property 0x10000011: a container list (clicking a cell opens the container it holds).</summary>
    public const uint ContainerListProperty = 0x10000011;

    /// <summary>Property 0x10000052: each item shows once (a select deselects the same item elsewhere in the list).</summary>
    public const uint SingleSelectionProperty = 0x10000052;

    /// <summary>Property 0x10000017: a list that grows keeps one empty cell after its last item (<c>UpdateEmptySlots</c>).</summary>
    public const uint NeedsEmptySlotProperty = 0x10000017;

    /// <summary>The width of a cell before the list has one (<c>UIElement_ItemList</c>'s constructor, 11.6096 0x004E4730: cells 32 by 32).</summary>
    public const int DefaultCellWidth = 32;

    /// <summary>The number of cells made at once when the list is first shown.</summary>
    public const int InitialCells = 24;

    /// <summary>The number of further cells made each frame while the list is shown.</summary>
    public const int CellsPerFrame = 12;

    private int _fixedSlots = -1;
    private bool _containerList;
    private bool _singleSelection;
    private bool _fillStarted;
    private bool _noLayoutLogged;
    private uint _selectedId;
    private uint _openId;

    /// <summary><c>UIElement_ItemList::UIElement_ItemList</c> (11.6096 0x004E4730): empty, cells 32 by 32.</summary>
    public UiItemList(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>A cell was selected by a left click (<c>HandleSingleSelection</c> and the selected object); the cell may be empty only if it was clicked while mouse-visible, which an empty cell is not.</summary>
    public event Action<UiItemList, UiItem>? ItemSelected;

    /// <summary>A cell was double clicked with the left button (retail uses the item; the owner decides: a pack page wields a wearable item and uses any other, a container window picks the item up).</summary>
    public event Action<UiItemList, UiItem>? ItemActivated;

    /// <summary>A right click on a non-empty cell (after it is selected): the list's owner examines the item (M4 3.2 s4c). With no subscriber the list answers the not-built dialog.</summary>
    public event Action<UiItemList, UiItem>? ItemExamined;

    /// <summary>True when a press on one of this list's items may start a drag (set by the page that owns the list; the player's own cell and the container's own cell are not sources).</summary>
    public bool DragSource { get; set; }

    /// <summary>The number of cells the list has (<c>m_listItems.m_num</c>).</summary>
    public int CellCount => Items.Count;

    /// <summary>The number of cells the list is to have (property 0x10000015, or the open container's capacity since <see cref="SetFixedSlots"/>), or -1 for a list that grows.</summary>
    public int FixedSlots => _fixedSlots;

    /// <summary>The number of cells the layout gives the list (property 0x10000015 as <c>PostInit</c> read it: the pack list's 102), or -1 for a list that grows.</summary>
    public int LayoutFixedSlots { get; private set; } = -1;

    /// <summary>
    /// <c>UIElement_ItemList::ItemList_UpdateContainerListSize</c> (11.6096 0x004E4670) over <c>UpdateFixedSlots</c> (11.6096 0x004E45A0): the list's cell count becomes <paramref name="count"/> (the capacity of the container it lists,
    /// M5 7 P7r, D148, <c>m5-8-pack-window-contents</c>). The cells past it are removed from the end (the caller has emptied or refilled them: retail flushes the list first) and the missing cells are made, the first
    /// <see cref="InitialCells"/> at once and the rest over the next frames while the list shows (as <see cref="BeginFill"/>). True when the count changed. A negative count is ignored.
    /// </summary>
    public bool SetFixedSlots(int count)
    {
        if (count < 0 || count == _fixedSlots)
        {
            return false;
        }

        _fixedSlots = count;
        while (CellCount > count && DeleteItem(CellCount - 1))
        {
        }

        if (IsShown)
        {
            BeginFill();
        }

        return true;
    }

    /// <summary>True for a container list (property 0x10000011).</summary>
    public bool IsContainerList => _containerList;

    /// <summary>The object whose cell is selected, or 0.</summary>
    public uint SelectedItemId => _selectedId;

    /// <summary>The container shown open, or 0 (<c>openItemID</c>).</summary>
    public uint OpenItemId => _openId;

    /// <summary>How many cells show an object or a spell.</summary>
    public int OccupiedCount
    {
        get
        {
            var n = 0;
            for (var i = 0; i < Items.Count; i++)
            {
                if (Items[i] is UiItem { IsEmpty: false })
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary><c>UIElement_ItemList::PostInit</c> (11.6096 0x004E5930) over <c>InitItemList</c>: reads the list's flags; the cells wait for the list to be shown.</summary>
    protected internal override void PostInit()
    {
        base.PostInit();
        _containerList = TryGetBoolProperty(ContainerListProperty, out var container) && container;
        _singleSelection = TryGetBoolProperty(SingleSelectionProperty, out var single) && single;
        _fixedSlots = TryGetIntegerProperty(FixedSlotsProperty, out var slots) ? slots : -1;
        LayoutFixedSlots = _fixedSlots;
        Manager.LogOnce("itemlist:todos", LogSeverity.Warning, "UIElement_ItemList is built for display, selection, double click and the minimal drag (M4 2.2 s3, TODO(m4-retail-drag)): TODO(m3-itemlist-empty-fill: only the container window's row and the spellcasting bars call UpdateEmptySlots; the spellbook page's list and the other lists show none) TODO(m3-uiitem-extras: shortcut number, sell and trade marks, cooldown) TODO(m3-selected-object: the list keeps its own selection, retail's is the player's one selected object)");
    }

    /// <summary><c>UIElement_ItemList::OnVisibilityChanged</c> (11.6096 0x004E4990): shown, the list makes its cells (the first few at once, the rest over frames).</summary>
    protected override void OnVisibilityChanged(bool shownNow)
    {
        base.OnVisibilityChanged(shownNow);
        if (shownNow)
        {
            BeginFill();
        }
    }

    /// <summary>
    /// Makes the first cells now and the rest over the next frames (what <c>InitItemList</c> does at once). Does nothing for a list that grows or is full. The list calls it when it, itself, is shown; a page that shows a list by
    /// showing a parent must call it from its <c>OnShown</c> (a parent's visibility change does not announce itself to its descendants).
    /// </summary>
    public void BeginFill()
    {
        if (_fixedSlots > 0 && CellCount < _fixedSlots)
        {
            EnsureCells(Math.Min(_fixedSlots, InitialCells));
            _fillStarted = true;
            RequestFrame();
        }
    }

    /// <summary>
    /// <c>UIElement_ItemList::UpdateEmptySlots</c> (11.6096 0x004E4390; 11.4186 0x004E3700) for a list that grows (the cell count, property 0x10000015, is -1) while the list is visible. A single-row list (maximum columns 0x5F of -1) is
    /// padded with empty cells, <c>ItemList_AddEmptySlot</c> (0x004E4340), until its cells fill its width (whole cells only: the width divided by the cell width), and trailing empty cells beyond the width are removed; with
    /// property 0x10000017 set it also keeps one empty cell after a last cell that holds an item. A column (maximum rows 0x60 of -1) only loses its trailing empty cells. Cells that hold an item or a spell are never touched, and
    /// the list is never padded past its width, so a long list scrolls as before. The cell width is the first cell's (<see cref="DefaultCellWidth"/> before there is one).
    /// </summary>
    public void UpdateEmptySlots()
    {
        if (!Visible || _fixedSlots != -1)
        {
            return;
        }

        var maxColumns = TryGetIntegerProperty(UiPropertyIds.ListBoxMaxColumns, out var columns) ? columns : 0;
        var maxRows = TryGetIntegerProperty(UiPropertyIds.ListBoxMaxRows, out var rows) ? rows : -1;
        if (maxColumns == -1)
        {
            var cellWidth = Items.Count > 0 && Items[0] is UiItem { Box.Width: > 0 } first ? first.Box.Width : DefaultCellWidth;
            var width = Box.Width;
            var total = cellWidth * CellCount;
            if (total < width)
            {
                for (var n = (width - total) / cellWidth; n > 0; n--)
                {
                    if (CreateCell() is null)
                    {
                        return; // no cell template: the list logged it
                    }
                }
            }
            else if (total > width)
            {
                for (var n = (cellWidth - width + total - 1) / cellWidth; n > 0 && TrimEmptyTail(); n--)
                {
                }
            }

            if (TryGetBoolProperty(NeedsEmptySlotProperty, out var needsEmpty) && needsEmpty && Items.Count > 0 && Items[^1] is UiItem { IsEmpty: false })
            {
                CreateCell();
            }
        }
        else if (maxRows == -1)
        {
            for (var n = CellCount; n > 0 && TrimEmptyTail(); n--)
            {
            }
        }
    }

    /// <summary>The number of empty cells the list holds now (a test counts the drawn empty slots).</summary>
    public int EmptyCount => CellCount - OccupiedCount;

    /// <summary>Removes the last cell when it is empty (true), else leaves it (false).</summary>
    private bool TrimEmptyTail() => Items.Count > 0 && Items[^1] is UiItem { IsEmpty: true } last && DeleteItem(last);

    /// <summary>Makes empty cells until the list has <paramref name="count"/> (at most the fixed number). True when it has that many.</summary>
    public bool EnsureCells(int count)
    {
        if (_fixedSlots >= 0)
        {
            count = Math.Min(count, _fixedSlots);
        }

        while (CellCount < count)
        {
            if (CreateCell() is null)
            {
                return false;
            }
        }

        return CellCount >= count;
    }

    /// <summary>
    /// <c>UIElement_ItemList::ItemList_AddItem</c> (11.6096 0x004E4A90): object <paramref name="itemId"/> goes in the first empty cell (a new one in a list that grows); null when a fixed list is full. The cell is selectable unless
    /// the list shows each item once and the item is already in it.
    /// </summary>
    public UiItem? AddItem(uint itemId, in UiItemIcon icon)
    {
        var cell = TakeEmptyCell();
        if (cell is null)
        {
            return null;
        }

        cell.SetSelectable(!_singleSelection || FindItem(itemId) is null);
        cell.ShowItem(itemId, icon);
        if (itemId == _selectedId && cell.Selectable)
        {
            cell.SetSelected(true);
        }

        cell.SetOpenContainer(itemId != 0 && itemId == _openId);
        return cell;
    }

    /// <summary><c>UIElement_ItemList::ItemList_AddSpellShortcut</c> (11.6096 0x004E4D30): spell <paramref name="spell"/> goes in the first empty cell, drawn with its composed picture (see <see cref="UiItem.ShowSpell(in UiSpellIcon, string?)"/>).</summary>
    public UiItem? AddSpell(in UiSpellIcon spell, string? name = null)
    {
        var cell = TakeEmptyCell();
        cell?.ShowSpell(spell, name);
        return cell;
    }

    /// <summary><c>UIElement_ItemList::ItemList_Flush</c> (11.6096 0x004E49F0): every cell is emptied; nothing is selected or open. The cells stay.</summary>
    public void FlushItems()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i] is UiItem cell && !cell.IsEmpty)
            {
                cell.Clear();
            }
        }

        _selectedId = 0;
        _openId = 0;
    }

    /// <summary>The cell at <paramref name="index"/>, or null.</summary>
    public UiItem? CellAt(int index) => GetItem(index) as UiItem;

    /// <summary><c>UIElement_ItemList::ItemList_GetItem</c> (11.6096 0x004E3BC0): the cell that shows object <paramref name="itemId"/>, or null.</summary>
    public UiItem? FindItem(uint itemId)
    {
        if (itemId == 0)
        {
            return null;
        }

        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i] is UiItem cell && cell.ItemId == itemId)
            {
                return cell;
            }
        }

        return null;
    }

    /// <summary>Selects the cell that shows <paramref name="itemId"/> (0 or an item not in the list clears the selection). Does not raise <see cref="ItemSelected"/>.</summary>
    public void SelectItem(uint itemId)
    {
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i] is UiItem { IsSelected: true } cell && cell.ItemId != itemId)
            {
                cell.SetSelected(false);
            }
        }

        var found = FindItem(itemId);
        _selectedId = found is null ? 0 : itemId;
        found?.SetSelected(true);
    }

    /// <summary>Clears the selection.</summary>
    public void ClearSelection() => SelectItem(0);

    /// <summary><c>UIElement_ItemList::UpdateOpenContainerIndicator</c> (11.6096 0x004E3D00): only the cell that shows <paramref name="containerId"/> shows the open-container frame.</summary>
    public void SetOpenItem(uint containerId)
    {
        _openId = containerId;
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i] is UiItem cell)
            {
                cell.SetOpenContainer(containerId != 0 && cell.ItemId == containerId);
            }
        }
    }

    /// <summary>
    /// A right click on <paramref name="cell"/> (<c>UIElement_ItemList::ListenToElementMessage</c> 11.6096 0x004E59E0, mouse action 8): the item is selected and examined (<c>ClientUISystem::ExamineObject</c> 0x005657B0) in every list, a vendor's
    /// included (retail's right click has no vendor or salvage exception, M5 7 S6 D136). The list's owner examines when it listens (<see cref="ItemExamined"/>), else the UI's examiner does (<see cref="UiTargetMode.ExamineObject"/>).
    /// False for an empty cell. Public so a review frame examines as the right click does.
    /// </summary>
    public bool Examine(UiItem cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (cell.IsEmpty || cell.ItemId == 0)
        {
            return false;
        }

        Select(cell);
        if (ItemExamined is { } examined)
        {
            examined(this, cell);
            return true;
        }

        return Manager.TargetMode.ExamineObject(cell.ItemId);
    }

    /// <summary>The mouse-down a cell sent: a left click selects, a double left click activates (<c>UIElement_ItemList::ListenToElementMessage</c> 11.6096 0x004E59E0).</summary>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.MouseDown && GetItemUnderMouse() is UiItem { IsEmpty: false } cell)
        {
            switch (message.Param1)
            {
                case UiMouseActions.Left:
                    if (Manager.TargetMode.Active)
                    {
                        Manager.TargetMode.Execute(cell.ItemId); // UIElement_ItemList::HandleTargetedUseLeftClick (11.6096 0x004E3160): a target mode runs on the item, which is not selected
                        break;
                    }

                    Select(cell);
                    break;
                case UiMouseActions.LeftDouble:
                    Select(cell);
                    ItemActivated?.Invoke(this, cell);
                    break;
                case UiMouseActions.Right:
                    Examine(cell);
                    break;
            }
        }

        return base.ListenToElementMessage(message);
    }

    /// <summary>The frame: the base lays the rows out, then the next batch of fixed cells is made.</summary>
    protected internal override void ListenToGlobalMessage(uint messageId, int data)
    {
        base.ListenToGlobalMessage(messageId, data);
        if (messageId == UiGlobalMessageIds.Frame && _fillStarted)
        {
            if (!IsShown || _fixedSlots < 0 || CellCount >= _fixedSlots)
            {
                _fillStarted = false;
                return;
            }

            EnsureCells(CellCount + CellsPerFrame);
            if (CellCount < _fixedSlots)
            {
                RequestFrame();
            }
            else
            {
                _fillStarted = false;
            }
        }
    }

    private void RequestFrame() => Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);

    private void Select(UiItem cell)
    {
        // HandleSingleSelection (11.6096 0x004E3EE0): the same item in another cell of the list is no longer selectable or selected.
        if (_singleSelection)
        {
            for (var i = 0; i < Items.Count; i++)
            {
                if (Items[i] is UiItem other && !ReferenceEquals(other, cell) && other.ItemId == cell.ItemId)
                {
                    other.SetSelectable(false);
                    other.SetSelected(false);
                }
            }

            cell.SetSelectable(true);
        }

        SelectItem(cell.ItemId);
        ItemSelected?.Invoke(this, cell);
    }

    private UiItem? TakeEmptyCell()
    {
        // The first empty cell (ItemList_AddItem walks the list for the first cell in the empty state).
        while (true)
        {
            for (var i = 0; i < Items.Count; i++)
            {
                if (Items[i] is UiItem { IsEmpty: true } cell)
                {
                    return cell;
                }
            }

            if (_fixedSlots >= 0 && CellCount >= _fixedSlots)
            {
                return null;
            }

            if (CreateCell() is null)
            {
                return null;
            }
        }
    }

    private UiItem? CreateCell()
    {
        if (!TryGetEnumProperty(TemplateProperty, out var templateId) || !Manager.Services.Descs.TryResolveLayoutEnum(ItemLayoutEnum, out var layoutId))
        {
            if (!_noLayoutLogged)
            {
                _noLayoutLogged = true;
                Manager.LogOnce($"itemlist:nolayout:{ElementId:X8}", LogSeverity.Warning, $"UIElement_ItemList 0x{ElementId:X8}: no cell template (property 0x1000000E) or no item layout (enum 0x10000038); the list stays empty (TODO(m3-itemlist-template))");
            }

            return null;
        }

        var row = AddItem(layoutId, templateId);
        if (row is UiItem cell)
        {
            return cell;
        }

        if (row is not null)
        {
            Manager.LogOnce($"itemlist:notanitem:{ElementId:X8}", LogSeverity.Warning, $"UIElement_ItemList 0x{ElementId:X8}: template 0x{templateId:X8} is class 0x{row.Desc.Type:X8}, not UIElement_UIItem; its cell cannot show an item");
            DeleteItem(row);
        }

        return null;
    }
}

using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
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
/// <b>Mouse.</b> <c>ListenToElementMessage</c> (0x004E59E0) hears the mouse-down message the cell under the mouse sends: a left button selects it (<c>HandleSingleSelection</c> 0x004E3EE0 first when the list shows each item once, flag
/// 0x10000052; then retail sets the selected object, and a container list opens the item in its child list), the double left button uses it (a pack: wield, <see cref="ItemActivated"/>), the right button examines it.
/// Retail's selection is the player's one selected object; here a list keeps its own (<c>TODO(m3-selected-object)</c>) and tells the owner. No drag and drop (D13: the runtime has none; the drag handlers, drop release,
/// drag scrolling and <c>AcceptDragObject</c> are not built), no vendor, salvage or shortcut list, no use of a double-clicked item (<c>TODO(m3-itemlist-use)</c>), no examination (<c>TODO(m3-itemlist-examine)</c>), no
/// empty-slot fill of a list that grows (<c>UpdateEmptySlots</c> 0x004E3700, <c>TODO(m3-itemlist-empty-fill)</c>: no M3 page has one) and no cache of removed cells (nothing removes one).
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

    /// <summary>A cell was double clicked with the left button (retail uses the item; the owner decides, a pack wields it).</summary>
    public event Action<UiItemList, UiItem>? ItemActivated;

    /// <summary>The number of cells the list has (<c>m_listItems.m_num</c>).</summary>
    public int CellCount => Items.Count;

    /// <summary>The number of cells the list is to have (property 0x10000015), or -1 for a list that grows.</summary>
    public int FixedSlots => _fixedSlots;

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
        Manager.LogOnce("itemlist:todos", LogSeverity.Warning, "UIElement_ItemList is built for display, selection and double click only (D13): TODO(m3-itemlist-use) TODO(m3-itemlist-examine) TODO(m3-itemlist-empty-fill) TODO(m3-uiitem-extras: shortcut number, sell and trade marks, cooldown, drag icon) TODO(m3-selected-object: the list keeps its own selection, retail's is the player's one selected object)");
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

    /// <summary><c>UIElement_ItemList::ItemList_AddSpellShortcut</c> (11.6096 0x004E4D30): spell <paramref name="spellId"/> goes in the first empty cell, drawn with <paramref name="iconId"/> (see <see cref="UiItem.ShowSpell(uint, uint, string?)"/>).</summary>
    public UiItem? AddSpell(uint spellId, uint iconId, string? name = null)
    {
        var cell = TakeEmptyCell();
        cell?.ShowSpell(spellId, iconId, name);
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

    /// <summary>The mouse-down a cell sent: a left click selects, a double left click activates (<c>UIElement_ItemList::ListenToElementMessage</c> 11.6096 0x004E59E0).</summary>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.MouseDown && GetItemUnderMouse() is UiItem { IsEmpty: false } cell)
        {
            switch (message.Param1)
            {
                case UiMouseActions.Left:
                    Select(cell);
                    break;
                case UiMouseActions.LeftDouble:
                    Select(cell);
                    ItemActivated?.Invoke(this, cell);
                    break;
                case UiMouseActions.Right:
                    Manager.LogOnce("itemlist:examine", LogSeverity.Warning, "TODO(m3-itemlist-examine): a right click on an item examines it in retail (ClientUISystem::ExamineObject); the examination panel is not in M3 (D8), so nothing happens");
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

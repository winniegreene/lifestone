using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Widgets;

/// <summary>
/// The List box (class 5, retail's <c>UIElement_ListBox</c>; character management's character list, chat, menus' lists):
/// rows laid out in a grid on a scrollable paper, built from the templates its property 0x64 lists, with a selection.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout</b> (<c>UIElement_ListBox::UpdateLayout</c>, 11.4186 0x0046E460, 11.6096 0x0046E820), done before the next draw
/// or query whenever the rows or the view change: the column count is the maximum columns (0x5F; at least 1, at most the
/// row count; a negative maximum puts every row in one row), the row count follows; each column is as wide as its widest
/// item and each row as tall as its tallest; a horizontal list (0x5C) fills rows first, otherwise columns first; the paper
/// is the sum, and the items are placed from the scroll offset.
/// </para>
/// <para>
/// <b>Selection</b> (<c>UIElement_ListBox::SetSelectedItem</c>, 11.4186 0x0046D8B0, 11.6096 0x0046DC70): a click
/// selects the row under the mouse when click-select (0x59) is on (the press on the list itself, or one bubbling up from a
/// row, <c>MouseDown</c> 11.4186 0x0046E3A0, 11.6096 0x0046E760, and <c>ListenToElementMessage</c> 11.4186 0x0046E350,
/// 11.6096 0x0046E710); with drag-select (0x5B) the selection then follows the mouse while the button is held. With
/// selected-item-state-change (0x61), the old selection goes to the normal item state (0x5D) and the new to the selected
/// one (0x5E). A change broadcasts <see cref="UiMessageIds.ListBoxSelection"/> (the index, the item in
/// <see cref="UiElementMessage.Item"/>); selecting the selected item again broadcasts
/// <see cref="UiMessageIds.ListBoxReselect"/>.
/// </para>
/// <para>
/// A list box takes the mouse unless it is ghosted (<c>UIElement_ListBox::GetShouldBeMouseVisible</c>, 11.4186 0x0046D590,
/// 11.6096 0x0046B300). Dragging items over the list (drag rollover, 0x5A) needs drag and drop, which M2 does not build.
/// </para>
/// </remarks>
public class UiListBox : UiScrollable
{
    private readonly List<UiElement> _items = [];
    private int[] _itemWidths = [];
    private int[] _itemHeights = [];
    private bool _layoutDirty = true;
    private bool _dragSelecting;
    private bool _frameRegistered;

    /// <summary><c>UIElement_ListBox::UIElement_ListBox</c> (11.4186 0x0046DCB0, 11.6096 0x0046E070): empty, the layout due.</summary>
    public UiListBox(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>The rows, in list order (<c>m_listItems</c>).</summary>
    public IReadOnlyList<UiElement> Items => _items;

    /// <summary>The selected row (<c>m_pSelectedItem</c>), or null.</summary>
    public UiElement? SelectedItem { get; private set; }

    /// <summary>The selected row's index, or -1.</summary>
    public int SelectedIndex => SelectedItem is null ? -1 : _items.IndexOf(SelectedItem);

    /// <summary>The column count of the last layout (<c>m_nCols</c>).</summary>
    public int Columns { get; private set; }

    /// <summary>The row count of the last layout (<c>m_nRows</c>).</summary>
    public int Rows { get; private set; }

    /// <summary>Property 0x5C: rows fill across first.</summary>
    public bool Horizontal => Flag(UiPropertyIds.ListBoxHorizontal);

    /// <summary>Property 0x59: a click selects.</summary>
    public bool ClickSelect => Flag(UiPropertyIds.ListBoxClickSelect);

    /// <summary>Property 0x5B: the selection follows the mouse while the button is held.</summary>
    public bool DragSelect => Flag(UiPropertyIds.ListBoxDragSelect);

    /// <summary>Property 0x61: the selected and unselected rows change state.</summary>
    public bool SelectedItemStateChange => Flag(UiPropertyIds.ListBoxSelectedItemStateChange);

    /// <summary>True when the rows need laying out again (bit 0x200).</summary>
    public bool IsLayoutDirty => _layoutDirty;

    // ---------------------------------------------------------------- Rows

    /// <summary>
    /// <c>UIElement_ListBox::AddItemFromTemplateList</c> (11.4186 0x0046F2B0, 11.6096 0x0046F670): a row from template
    /// <paramref name="templateIndex"/> of the list's template array (property 0x64: structs of a layout 0x63 and an element
    /// 0x62), added at the end or before <paramref name="insertBefore"/>. Null when the template or its element is missing.
    /// </summary>
    public UiElement? AddItemFromTemplateList(int templateIndex, UiElement? insertBefore = null)
    {
        if (!InqProperty(UiPropertyIds.ListBoxEntryTemplates, out var property) || property is not UiArrayProperty templates
            || templateIndex < 0 || templateIndex >= templates.Items.Count)
        {
            return null;
        }

        return AddItemFromTemplate(templates.Items[templateIndex], insertBefore);
    }

    /// <summary>
    /// <c>UIElement_ListBox::AddItemFromTemplateListByID</c> (11.4186 0x0046EF00, 11.6096 0x0046F2C0): a row from the template
    /// whose element is <paramref name="elementId"/>.
    /// </summary>
    public UiElement? AddItemFromTemplateListById(uint elementId, UiElement? insertBefore = null)
    {
        if (!InqProperty(UiPropertyIds.ListBoxEntryTemplates, out var property) || property is not UiArrayProperty templates)
        {
            return null;
        }

        foreach (var template in templates.Items)
        {
            if (TryReadTemplate(template, out _, out var id) && id == elementId)
            {
                return AddItemFromTemplate(template, insertBefore);
            }
        }

        return null;
    }

    /// <summary>
    /// <c>UIElement_ListBox::AddItem</c> (11.4186 0x0046EDC0, 11.6096 0x0046F180): a row created from the top-level element
    /// <paramref name="elementId"/> of layout <paramref name="layoutId"/>, added at the end or before
    /// <paramref name="insertBefore"/>.
    /// </summary>
    public UiElement? AddItem(uint layoutId, uint elementId, UiElement? insertBefore = null)
    {
        var row = Manager.CreateChildElement(this, layoutId, elementId);
        if (row is null)
        {
            return null;
        }

        if (insertBefore is null ? AddItem(row) : InsertItem(row, insertBefore))
        {
            return row;
        }

        Manager.DeleteElement(row);
        return null;
    }

    /// <summary><c>UIElement_ListBox::AddItem</c> (11.4186 0x0046ED70, 11.6096 0x0046F130): <paramref name="item"/> becomes a child and the last row (once).</summary>
    public bool AddItem(UiElement item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.SetParent(this);
        if (!_items.Contains(item))
        {
            _items.Add(item);
            MarkLayoutDirty();
        }

        return true;
    }

    /// <summary>
    /// <c>UIElement_ListBox::InsertItem</c> (11.4186 0x0046E760, 11.6096 0x0046EB20): <paramref name="item"/> becomes a child
    /// and the row at <paramref name="index"/> (the end when past it).
    /// </summary>
    public bool InsertItem(UiElement item, int index)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.SetParent(this);
        if (!_items.Contains(item))
        {
            _items.Insert(Math.Clamp(index, 0, _items.Count), item);
            MarkLayoutDirty();
        }

        return true;
    }

    /// <summary><c>UIElement_ListBox::InsertItem</c> (11.4186 0x0046EC00, 11.6096 0x0046EFC0): before <paramref name="before"/> (the end when it is not a row).</summary>
    public bool InsertItem(UiElement item, UiElement before)
    {
        var index = _items.IndexOf(before);
        return InsertItem(item, index < 0 ? _items.Count : index);
    }

    /// <summary>
    /// <c>UIElement_ListBox::RemoveItem</c> (11.4186 0x0046DAC0, 11.6096 0x0046DE80): the row at <paramref name="index"/>
    /// leaves the list and stops being a child (it is not deleted); a selected row is no longer selected (no message).
    /// </summary>
    public UiElement? RemoveItem(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return null;
        }

        var item = _items[index];
        _items.RemoveAt(index);
        MarkLayoutDirty();
        item.SetParent(null);
        if (ReferenceEquals(item, SelectedItem))
        {
            SelectedItem = null;
        }

        return item;
    }

    /// <summary><c>UIElement_ListBox::DeleteItem</c> (11.4186 0x0046E020, 11.6096 0x0046E3E0): removes and deletes <paramref name="item"/>.</summary>
    public bool DeleteItem(UiElement item)
    {
        var removed = RemoveItem(_items.IndexOf(item));
        if (removed is null)
        {
            return false;
        }

        Manager.DeleteElement(removed);
        return true;
    }

    /// <summary><c>UIElement_ListBox::DeleteItem</c> (11.4186 0x0046E070, 11.6096 0x0046E430) by index.</summary>
    public bool DeleteItem(int index) => index >= 0 && index < _items.Count && DeleteItem(_items[index]);

    /// <summary><c>UIElement_ListBox::Flush</c> (11.4186 0x0046E0A0, 11.6096 0x0046E460): every row is deleted; nothing is selected.</summary>
    public void Flush()
    {
        var items = _items.ToArray();
        _items.Clear();
        SelectedItem = null;
        foreach (var item in items)
        {
            Manager.DeleteElement(item);
        }

        MarkLayoutDirty();
    }

    /// <summary><c>UIElement_ListBox::GetItem</c> (11.4186 0x0046D890, 11.6096 0x0046DC50).</summary>
    public UiElement? GetItem(int index) => index >= 0 && index < _items.Count ? _items[index] : null;

    // ---------------------------------------------------------------- Selection

    /// <summary>See the remarks (<c>UIElement_ListBox::SetSelectedItem</c>). An item that is not a row selects nothing.</summary>
    public void SetSelectedItem(UiElement? item, bool broadcast)
    {
        var old = SelectedItem;
        if (ReferenceEquals(old, item))
        {
            if (broadcast)
            {
                Manager.BroadcastElementMessage(new UiElementMessage(this, UiMessageIds.ListBoxReselect, 0, 0) { Item = old });
            }

            return;
        }

        var index = item is null ? -1 : _items.IndexOf(item);
        SelectedItem = index >= 0 ? item : null;
        if (SelectedItemStateChange)
        {
            if (old is not null)
            {
                old.SetState(TryGetEnumProperty(UiPropertyIds.ListBoxItemNormalState, out var normal) ? normal : 0);
            }

            SelectedItem?.SetState(TryGetEnumProperty(UiPropertyIds.ListBoxItemSelectedState, out var selected) ? selected : 0);
        }

        if (broadcast)
        {
            Manager.BroadcastElementMessage(new UiElementMessage(this, UiMessageIds.ListBoxSelection, unchecked((uint)index), 0) { Item = SelectedItem });
        }
    }

    /// <summary><c>UIElement_ListBox::SetSelectedIndex</c> (11.4186 0x0046EBA0, 11.6096 0x0046EF60): selects and shows row <paramref name="index"/> (none when past the end).</summary>
    public void SetSelectedIndex(int index, bool broadcast)
    {
        if (index < 0 || index >= _items.Count)
        {
            SetSelectedItem(null, broadcast);
            return;
        }

        SetSelectedItem(_items[index], broadcast);
        ScrollToShow(index);
    }

    /// <summary><c>UIElement_ListBox::SetSelectedItemByID</c> (11.4186 0x0046ECE0, 11.6096 0x0046F0A0): the first row with id <paramref name="elementId"/>.</summary>
    public void SetSelectedItemById(uint elementId, bool broadcast)
    {
        var index = _items.FindIndex(i => i.ElementId == elementId);
        SetSelectedIndex(index, broadcast);
    }

    // ---------------------------------------------------------------- Layout

    /// <summary>The rows need laying out again (bit 0x200); the list repaints and lays out before the next frame.</summary>
    public void MarkLayoutDirty()
    {
        _layoutDirty = true;
        MakeRootDirtyHere();
        if (!_frameRegistered)
        {
            _frameRegistered = true;
            Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        }
    }

    /// <inheritdoc/>
    protected internal override void ListenToGlobalMessage(uint messageId, int data)
    {
        base.ListenToGlobalMessage(messageId, data);
        if (messageId == UiGlobalMessageIds.Frame)
        {
            UpdateLayout();
        }
    }

    /// <summary><c>UIElement_ListBox::UpdateLayout</c>: see the remarks. Does nothing when the layout is not due.</summary>
    public void UpdateLayout()
    {
        if (_frameRegistered)
        {
            _frameRegistered = false;
            Manager.UnregisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        }

        if (!_layoutDirty)
        {
            return;
        }

        _layoutDirty = false;
        MakeRootDirtyHere();
        var count = _items.Count;
        var maxColumns = TryGetIntegerProperty(UiPropertyIds.ListBoxMaxColumns, out var max) ? max : 0;
        if (maxColumns >= 0)
        {
            Columns = Math.Min(Math.Max(maxColumns, 1), count);
            Rows = Columns != 0 ? (int)Math.Ceiling(count / (double)Columns) : 0;
        }
        else
        {
            Columns = count;
            Rows = count != 0 ? 1 : 0;
        }

        _itemWidths = new int[Columns];
        _itemHeights = new int[Rows];
        var horizontal = Horizontal;
        int column = 0, row = 0;
        foreach (var item in _items)
        {
            _itemWidths[column] = Math.Max(_itemWidths[column], item.Box.Width);
            _itemHeights[row] = Math.Max(_itemHeights[row], item.Box.Height);
            Advance(horizontal, ref column, ref row);
        }

        ResizeScrollableArea(_itemWidths.Sum(), _itemHeights.Sum());
        var x = -ScrollX;
        var y = -ScrollY;
        column = 0;
        row = 0;
        foreach (var item in _items)
        {
            if (!ReferenceEquals(item.Parent, this))
            {
                continue;
            }

            item.MoveTo(x, y);
            if (horizontal)
            {
                if (column != Columns - 1)
                {
                    x += _itemWidths[column++];
                    continue;
                }

                x = -ScrollX;
                column = 0;
                y += _itemHeights[row++];
            }
            else if (row == Rows - 1)
            {
                y = -ScrollY;
                x += _itemWidths[column++];
                row = 0;
            }
            else
            {
                y += _itemHeights[row++];
            }
        }
    }

    private void Advance(bool horizontal, ref int column, ref int row)
    {
        if (horizontal)
        {
            if (column != Columns - 1)
            {
                column++;
                return;
            }

            column = 0;
            row++;
        }
        else if (row == Rows - 1)
        {
            row = 0;
            column++;
        }
        else
        {
            row++;
        }
    }

    /// <summary>
    /// <c>UIElement_ListBox::InqItemIndexAtPoint</c> (11.4186 0x0046D9A0, 11.6096 0x0046DD60): the row at (x, y) of the
    /// list's own box (the scroll offset added), or false outside the box or the rows.
    /// </summary>
    public bool TryGetItemIndexAt(int x, int y, out int index)
    {
        index = -1;
        if (_items.Count == 0 || x < 0 || x >= Box.Width || y < 0 || y >= Box.Height)
        {
            return false;
        }

        UpdateLayout();
        var px = ScrollX + x;
        var py = ScrollY + y;
        var column = 0;
        var right = 0;
        for (var c = 0; c < _itemWidths.Length; c++)
        {
            right += _itemWidths[c];
            if (px <= right)
            {
                column = c;
                break;
            }
        }

        var row = 0;
        var bottom = 0;
        for (var r = 0; r < _itemHeights.Length; r++)
        {
            bottom += _itemHeights[r];
            if (py <= bottom)
            {
                row = r;
                break;
            }
        }

        var found = Horizontal ? column + (row * Columns) : row + (column * Rows);
        if (found >= _items.Count)
        {
            return false;
        }

        index = found;
        return true;
    }

    /// <summary><c>UIElement_ListBox::GetItemUnderMouse</c> (11.4186 0x0046E220, 11.6096 0x0046E5E0): the row under the mouse, while the mouse is over the list.</summary>
    public UiElement? GetItemUnderMouse()
    {
        if (!IsMouseOver)
        {
            return null;
        }

        var (mx, my) = Manager.Input.MousePosition;
        var screen = ScreenBox;
        return TryGetItemIndexAt(mx - screen.X0, my - screen.Y0, out var index) ? _items[index] : null;
    }

    /// <summary>
    /// <c>UIElement_ListBox::ScrollToShow</c> (11.4186 0x0046E940, 11.6096 0x0046ED00): the view moves so row
    /// <paramref name="index"/> is at its top-left corner (kept on the paper).
    /// </summary>
    public void ScrollToShow(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        UpdateLayout();
        var (column, row) = Horizontal ? (index % Math.Max(Columns, 1), index / Math.Max(Columns, 1)) : (index / Math.Max(Rows, 1), index % Math.Max(Rows, 1));
        if (column >= _itemWidths.Length || row >= _itemHeights.Length)
        {
            return;
        }

        SetScrollableXY(_itemWidths.Take(column).Sum(), _itemHeights.Take(row).Sum());
    }

    /// <summary><c>UIElement_ListBox::AdjustToScrollableXYChange</c> (11.4186 0x0046DC00, 11.6096 0x0046DFC0): the rows move.</summary>
    protected override void AdjustToScrollableXYChange() => MarkLayoutDirty();

    /// <summary>
    /// <c>UIElement_ListBox::InqScrollDelta</c> (11.4186 0x0046DC10, 11.6096 0x0046DFD0): a page is the list's size; a line
    /// the paper over the row (or column) count, at most the size.
    /// </summary>
    protected override int InqScrollDelta(bool horizontal, bool towardStart, bool page)
    {
        var delta = 0;
        if (page)
        {
            delta = horizontal ? Box.Width : Box.Height;
        }
        else
        {
            var lines = horizontal ? Columns : Rows;
            if (lines != 0)
            {
                delta = Math.Min((horizontal ? PaperWidth : PaperHeight) / lines, horizontal ? Box.Width : Box.Height);
            }
        }

        return towardStart ? -delta : delta;
    }

    /// <summary><c>UIElement_ListBox::ResizeTo</c> (11.4186 0x0046D7B0, 11.6096 0x0046DB70): a new size lays the rows out again.</summary>
    public override void ResizeTo(int width, int height)
    {
        var oldWidth = Box.Width;
        var oldHeight = Box.Height;
        base.ResizeTo(width, height);
        if (oldWidth != Box.Width || oldHeight != Box.Height)
        {
            MarkLayoutDirty();
        }
    }

    /// <summary>
    /// <c>UIElement_ListBox::SetState</c> (11.4186 0x0046D510, 11.6096 0x0046D8F0): the state, then the mouse visibility
    /// again (a ghosted list lets the mouse through).
    /// </summary>
    public override bool SetState(uint state)
    {
        var result = base.SetState(state);
        SetMouseVisible(GetShouldBeMouseVisible());
        return result;
    }

    /// <inheritdoc/>
    protected override bool GetShouldBeMouseVisible() => base.GetShouldBeMouseVisible() || State != UiStateIds.Ghosted;

    /// <summary><c>UIElement_ListBox::OnSetAttribute</c> (11.4186 0x0046D6A0, 11.6096 0x0046DA60): the layout properties lay the rows out again.</summary>
    protected internal override void OnSetAttribute(uint id, UiProperty? value)
    {
        base.OnSetAttribute(id, value);
        if (id is UiPropertyIds.ListBoxHorizontal or UiPropertyIds.ListBoxMaxColumns)
        {
            MarkLayoutDirty();
        }
    }

    // ---------------------------------------------------------------- Mouse

    /// <inheritdoc/>
    public override void MouseDown(int x, int y, uint button)
    {
        base.MouseDown(x, y, button);
        if (!IsResizing && !IsMoving && button is UiMouseActions.Left or UiMouseActions.LeftDouble)
        {
            SelectUnderMouse();
        }
    }

    /// <inheritdoc/>
    public override void MouseUp(int x, int y, uint button)
    {
        base.MouseUp(x, y, button);
        _dragSelecting = false;
    }

    /// <inheritdoc/>
    public override void MouseMove(int x, int y)
    {
        base.MouseMove(x, y);
        if (_dragSelecting && GetItemUnderMouse() is { } item && !ReferenceEquals(item, SelectedItem))
        {
            SetSelectedItem(item, broadcast: true);
        }
    }

    /// <inheritdoc/>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.MouseDown && message.Param1 == UiMouseActions.Left)
        {
            SelectUnderMouse();
        }

        return base.ListenToElementMessage(message);
    }

    private void SelectUnderMouse()
    {
        if (!ClickSelect || GetItemUnderMouse() is not { } item)
        {
            return;
        }

        SetSelectedItem(item, broadcast: true);
        if (DragSelect && !_dragSelecting && Manager.Input.IsActionInProgress(UiMouseActions.Left))
        {
            // UIElement_ListBox::StartDragSelect (11.4186 0x0046DB50, 11.6096 0x0046DF10).
            Manager.Input.SetMouseCapture(this);
            _dragSelecting = true;
        }
    }

    private UiElement? AddItemFromTemplate(UiProperty template, UiElement? insertBefore) =>
        TryReadTemplate(template, out var layout, out var element) ? AddItem(layout, element, insertBefore) : null;

    private static bool TryReadTemplate(UiProperty template, out uint layoutId, out uint elementId)
    {
        layoutId = 0;
        elementId = 0;
        if (template is not UiStructProperty entry
            || !entry.Members.TryGetValue(UiPropertyIds.ListBoxEntryTemplateLayout, out var layout) || layout is not UiDataIdProperty l
            || !entry.Members.TryGetValue(UiPropertyIds.ListBoxEntryTemplateElement, out var element) || element is not UiEnumProperty e)
        {
            return false;
        }

        layoutId = l.Value;
        elementId = e.Value;
        return true;
    }

    private bool Flag(uint id) => TryGetBoolProperty(id, out var value) && value;
}

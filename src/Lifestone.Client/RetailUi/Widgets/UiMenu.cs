using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Widgets;

/// <summary>
/// The Menu (class 6, retail's <c>UIElement_Menu</c>; drop-down lists such as the menu dialogs'): a button that opens a
/// popup holding a list box; choosing a row closes the popup and shows the choice in the menu's selection display.
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>UIElement_Menu::Initialize</c> (11.4186 0x0046D440, 11.6096 0x0046D820), <c>MakePopup</c>
/// (11.4186 0x0046D310, 11.6096 0x0046D6F0), <c>Open</c> (11.4186 0x0046CC30, 11.6096 0x0046D010), <c>Close</c> (11.4186
/// 0x0046CD20, 11.6096 0x0046D100), <c>NewSelection</c> (11.4186 0x0046CD60, 11.6096 0x0046D140), <c>InsertTextItem</c>
/// (11.4186 0x0046CF10, 11.6096 0x0046D2F0), <c>InsertItem</c> (11.4186 0x0046CA90, 11.6096 0x0046CE70),
/// <c>SetSelectedItem</c> (11.4186 0x0046D0D0, 11.6096 0x0046D4B0), <c>GetSelectedIndex</c> (11.4186 0x0046CBF0, 11.6096
/// 0x0046CFD0), <c>RecalculatePopupSize</c> (11.4186 0x0046CAF0, 11.6096 0x0046CED0), <c>ListenToElementMessage</c> (11.4186
/// 0x0046D180, 11.6096 0x0046D560), <c>SetVisible</c> (11.4186 0x0046D100, 11.6096 0x0046D4E0) and <c>Deactivate</c>
/// (11.4186 0x0046D130, 11.6096 0x0046D510).
/// </para>
/// <para>
/// The popup is the top-level element property 0x06 names, of the layout 0x07 names (else the menu's own), made a hidden
/// root that activates on show; its list box is the child 0x02 names; property 0x01 picks the first selection. Opening
/// moves the popup under the menu (above with 0x05, centred with 0x03) and highlights the menu
/// (<see cref="UiMessageIds.MenuOpened"/>); closing hides it (<see cref="UiMessageIds.MenuClosed"/>). The popup's top is the
/// menu's last row (retail's inclusive <c>GetScreenY1</c>), so the two overlap by a pixel, as retail. After moving the
/// popup, the 11.4186 decompile of <c>Open</c> makes one virtual call on it with the argument 1, under a mislabelled
/// vtable slot; Lifestone takes it to be the show (TODO(retailui-runtime): confirm the slot in the 11.6096 vtable).
/// </para>
/// </remarks>
public class UiMenu : UiButton
{
    private int _listBoxBorderX;
    private int _listBoxBorderY;

    /// <summary>A menu of <paramref name="init"/>, closed.</summary>
    public UiMenu(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>True while the popup is open (<c>m_open</c>).</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The popup (<c>m_popup</c>), or null.</summary>
    public UiElement? Popup { get; private set; }

    /// <summary>The popup's list box (<c>m_listBox</c>), or null.</summary>
    public UiListBox? ListBox { get; private set; }

    /// <summary><c>UIElement_Menu::GetNumItems</c> (11.4186 0x0046CBD0, 11.6096 0x0046CFB0).</summary>
    public int ItemCount => ListBox?.Items.Count ?? 0;

    /// <summary><c>UIElement_Menu::GetItem</c> (11.4186 0x0046CA50, 11.6096 0x0046CE30).</summary>
    public UiElement? GetItem(int index) => ListBox?.GetItem(index);

    /// <summary><c>UIElement_Menu::GetSelectedIndex</c>: the selected row's index, or -1.</summary>
    public int SelectedIndex => ListBox?.SelectedIndex ?? -1;

    /// <summary>
    /// <c>UIElement_Menu::Initialize</c>: the popup is made first (with its list box and first selection), then the base
    /// initialization; the popup's border around its list box is remembered for <see cref="RecalculatePopupSize"/>.
    /// </summary>
    protected internal override void Initialize()
    {
        MakePopup();
        if (Popup is not null && Popup.GetChildRecursive(PropertyEnum(UiPropertyIds.MenuListbox)) is UiListBox list)
        {
            ListBox = list;
            if (PropertyEnum(UiPropertyIds.MenuDefaultSelectedItem) is var first and not 0)
            {
                list.SetSelectedItemById(first, broadcast: true);
            }
        }

        base.Initialize();
        if (Popup is not null && ListBox is not null)
        {
            _listBoxBorderX = Popup.Box.Width - ListBox.Box.Width;
            _listBoxBorderY = Popup.Box.Height - ListBox.Box.Height;
        }
    }

    /// <summary><c>UIElement_Menu::MakePopup</c>: see the remarks.</summary>
    private void MakePopup()
    {
        if (Popup is not null)
        {
            Manager.DeleteElement(Popup);
            Popup = null;
        }

        var layoutId = Desc.BaseState.Properties.TryGetValue(UiPropertyIds.MenuPopupLayout, out var l) && l is UiDataIdProperty did && did.Value != 0
            ? did.Value
            : Layout.Id;
        var popupId = PropertyEnum(UiPropertyIds.MenuPopup);
        if (layoutId == 0 || popupId == 0)
        {
            return;
        }

        var popup = Manager.CreateRootElementByDataId(layoutId, popupId);
        if (popup is null)
        {
            return;
        }

        popup.SetVisible(false);
        popup.SetBoolProperty(UiPropertyIds.ElementActivatable, true);
        popup.SetBoolProperty(UiPropertyIds.ElementActivateOnShow, true);
        Popup = popup;
        popup.RegisterForElementMessages(AsListener);
    }

    /// <summary><c>UIElement_Menu::Open</c>: see the remarks. Nothing happens without rows or a popup.</summary>
    public void Open()
    {
        if (IsOpen || ListBox is not { Items.Count: > 0 } || Popup is not { } popup)
        {
            return;
        }

        var screen = ScreenBox;
        var origin = Manager.DisplayOrigin;
        var x = screen.X0 - origin.X;
        if (TryGetBoolProperty(UiPropertyIds.MenuOpenCenter, out var center) && center)
        {
            x += (popup.Box.Width - Box.Width) / -2;
        }

        var up = TryGetBoolProperty(UiPropertyIds.MenuOpenUp, out var u) && u;
        var y = up ? screen.Y0 - origin.Y - popup.Box.Height : screen.Y1 - origin.Y;
        popup.MoveTo(x, y);
        popup.SetVisible(true);
        IsOpen = true;
        SetBoolProperty(UiPropertyIds.ButtonHighlighted, true);
        BroadcastElementMessage(UiMessageIds.MenuOpened);
    }

    /// <summary>
    /// <c>UIElement_Menu::Close</c>: see the remarks. The menu counts as closed before its popup hides (retail clears
    /// <c>m_open</c> just after), because hiding the active popup deactivates it, and the menu closes on that: closing
    /// once more from inside would send <see cref="UiMessageIds.MenuClosed"/> twice.
    /// </summary>
    public void Close()
    {
        if (!IsOpen || Popup is null)
        {
            return;
        }

        IsOpen = false;
        Popup.SetVisible(false);
        SetBoolProperty(UiPropertyIds.ButtonHighlighted, false);
        BroadcastElementMessage(UiMessageIds.MenuClosed);
    }

    /// <summary>
    /// <c>UIElement_Menu::InsertTextItem</c>: a text row (the top-level element property 0x09 names, of the layout 0x0A names,
    /// else the menu's own) showing <paramref name="text"/>, inserted at <paramref name="index"/>. Null when it cannot be made.
    /// </summary>
    public UiTextElement? InsertTextItem(in UiStringRef text, int index)
    {
        var layoutId = TryGetDataIdProperty(UiPropertyIds.MenuTextItemLayout, out var did) && did != 0 ? did : Layout.Id;
        var itemId = PropertyEnum(UiPropertyIds.MenuTextItem);
        if (ListBox is null || Manager.CreateDetachedElement(layoutId, itemId) is not UiTextElement row)
        {
            Manager.LogOnce($"menu:item:{ElementId:X8}", LogSeverity.Error, $"{Desc.Describe()}: its text item 0x{itemId:X8} of layout 0x{layoutId:X8} is not a text element; the row is not added (as retail)");
            return null;
        }

        row.SetStringInfo(text);
        return InsertItem(row, index) ? row : null;
    }

    /// <summary><c>UIElement_Menu::InsertItem</c>: <paramref name="item"/> takes the mouse and goes into the list.</summary>
    public bool InsertItem(UiElement item, int index)
    {
        if (ListBox is null)
        {
            return false;
        }

        item.SetMouseVisible(true);
        return ListBox.InsertItem(item, index);
    }

    /// <summary><c>UIElement_Menu::SetSelectedItem</c>: the list selects <paramref name="item"/> and the menu shows it.</summary>
    public void SetSelectedItem(UiElement? item, bool broadcast)
    {
        if (ListBox is null)
        {
            return;
        }

        ListBox.SetSelectedItem(item, broadcast);
        NewSelection(broadcast);
    }

    /// <summary>
    /// <c>UIElement_Menu::NewSelection</c>: the selection display (the child property 0x08 names) shows the selected row's
    /// text (or nothing); with <paramref name="broadcast"/>, <see cref="UiMessageIds.MenuSelection"/> goes out with the row's
    /// id and the row.
    /// </summary>
    private void NewSelection(bool broadcast)
    {
        if (ListBox is null)
        {
            return;
        }

        if (PropertyEnum(UiPropertyIds.MenuSelectionDisplay) is var displayId and not 0 && GetChildRecursive(displayId) is UiTextElement display)
        {
            if (ListBox.SelectedItem is UiTextElement selected)
            {
                display.SetText(selected.Text);
            }
            else
            {
                display.ClearAllText();
            }
        }

        if (broadcast)
        {
            var item = ListBox.SelectedItem;
            Manager.BroadcastElementMessage(new UiElementMessage(this, UiMessageIds.MenuSelection, item?.ElementId ?? 0, 0) { Item = item });
        }
    }

    /// <summary><c>UIElement_Menu::RecalculatePopupSize</c>: a list box that follows its popup on two sides sizes the popup to its paper.</summary>
    private void RecalculatePopupSize()
    {
        if (ListBox is null || Popup is null)
        {
            return;
        }

        var width = Popup.Box.Width;
        var height = Popup.Box.Height;
        var edges = ListBox.Desc.Edges;
        if (edges.Left == UiEdge.OwnSide && edges.Right == UiEdge.OwnSide)
        {
            width = _listBoxBorderX + ListBox.PaperWidth;
        }

        if (edges.Top == UiEdge.OwnSide && edges.Bottom == UiEdge.OwnSide)
        {
            height = _listBoxBorderY + ListBox.PaperHeight;
        }

        Popup.ResizeTo(width, height);
    }

    /// <inheritdoc/>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        var element = message.Element;
        if (Popup is not null && ReferenceEquals(element, Popup))
        {
            if (message.MessageId == UiMessageIds.Deactivated && IsOpen && !IsMouseOverTop)
            {
                Close();
            }

            return UiListenResult.Consumed;
        }

        if (ListBox is not null && ReferenceEquals(element, ListBox))
        {
            switch (message.MessageId)
            {
                case UiMessageIds.ListBoxSelection:
                    NewSelection(broadcast: true);
                    Close();
                    break;
                case UiMessageIds.PaperResized:
                    RecalculatePopupSize();
                    break;
                case UiMessageIds.ListBoxReselect:
                    Close();
                    break;
            }

            return UiListenResult.Consumed;
        }

        if (ListBox is not null && ReferenceEquals(element.Parent, ListBox))
        {
            if (message.MessageId == UiMessageIds.MouseOverTop)
            {
                // The rows roll over by themselves: Normal and rollover, Highlight and its rollover.
                var state = element.State == 0 ? UiStateIds.Normal : element.State;
                state = state switch
                {
                    UiStateIds.NormalRollover when message.Param1 == 0 => UiStateIds.Normal,
                    UiStateIds.Normal when message.Param1 != 0 => UiStateIds.NormalRollover,
                    UiStateIds.HighlightRollover when message.Param1 == 0 => UiStateIds.Highlight,
                    UiStateIds.Highlight when message.Param1 != 0 => UiStateIds.HighlightRollover,
                    _ => state,
                };
                if (state != element.State)
                {
                    element.SetState(state);
                }
            }

            return UiListenResult.Consumed;
        }

        if (ReferenceEquals(element, this))
        {
            if (message.MessageId == UiMessageIds.ButtonClick)
            {
                if (IsOpen)
                {
                    Close();
                }
                else
                {
                    Open();
                }

                return UiListenResult.Consumed;
            }

            if (message.MessageId == UiMessageIds.MouseOverTop)
            {
                // UIElement_Menu::UpdateState: the highlight follows whether the popup is open.
                SetBoolProperty(UiPropertyIds.ButtonHighlighted, IsOpen);
                return UiListenResult.Consumed;
            }
        }

        return base.ListenToElementMessage(message);
    }

    /// <summary>
    /// <c>UIElement_Menu::~UIElement_Menu</c> (11.4186 0x0046CE40, 11.6096 0x0046D220): the menu stops hearing its popup,
    /// closes it when it is open (<see cref="UiMessageIds.MenuClosed"/> goes out, as retail), and deletes it. The popup is
    /// a root of its own, not a child, so nothing else would delete it.
    /// </summary>
    protected internal override void OnDeleting()
    {
        base.OnDeleting();
        if (Popup is not { } popup)
        {
            return;
        }

        popup.UnregisterForElementMessages(AsListener);
        Close();
        Popup = null;
        ListBox = null;
        Manager.DeleteElement(popup);
    }

    /// <inheritdoc/>
    public override void SetVisible(bool visible)
    {
        base.SetVisible(visible);
        if (!IsShown && IsOpen)
        {
            Close();
        }
    }

    /// <inheritdoc/>
    public override bool Deactivate()
    {
        var deactivated = base.Deactivate();
        if (deactivated && IsOpen)
        {
            Close();
        }

        return deactivated;
    }

    private uint PropertyEnum(uint id) => TryGetEnumProperty(id, out var value) ? value : 0;
}

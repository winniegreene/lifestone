using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Widgets;

/// <summary>
/// An element whose content (its "paper") can be larger than itself and scrolls (retail's <c>UIElement_Scrollable</c>,
/// no class id of its own): the base of Text, Button, List box and the scroll bar's users. It keeps the scroll position and
/// the paper's size, drives the horizontal and vertical scroll bars that properties 0x71 and 0x72 name, and turns their
/// messages and the wheel into scrolling.
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>UIElement_Scrollable</c>: the constructor (11.4186 0x00473D90, 11.6096 0x00474150),
/// <c>SetScrollableXY</c> (11.4186 0x004740C0, 11.6096 0x00474480), <c>ResizeScrollableArea</c> (11.4186 0x00474730,
/// 11.6096 0x00474AF0), <c>PostInit</c> (11.4186 0x004744B0, 11.6096 0x00474870), <c>ListenToElementMessage</c> (11.4186
/// 0x00474560, 11.6096 0x00474920), <c>OnSetAttribute</c> (11.4186 0x00474630, 11.6096 0x004749F0),
/// <c>GetScrollbarPointer_</c>, <c>UpdateScrollbarPosition_</c>, <c>UpdateScrollbarSize_</c> and
/// <c>HandleScrollbarMessage_</c> (11.4186 0x00473EC0, 0x00473F20, 0x004741A0, 0x00474370; 11.6096 0x00474280, 0x004742E0,
/// 0x00474560, 0x00474730), <c>MouseDown</c> (11.4186 0x00473E00, 11.6096 0x004741C0) and <c>ResizeTo</c> (11.4186
/// 0x004746C0, 11.6096 0x00474A80).
/// </para>
/// <para>
/// <b>Deviation.</b> Retail's scroll-bar message and percentage anchoring set both scroll coordinates from one bar and so
/// reset the other axis to 0; Lifestone keeps the other axis. No retail element has both scroll bars
/// (WidgetCensusTests), so nothing on screen differs.
/// </para>
/// </remarks>
public class UiScrollable : UiElement
{
    private bool _updatingScrollbars;

    /// <summary>A scrollable of <paramref name="init"/>, at (0, 0) with an empty paper.</summary>
    public UiScrollable(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>The element id of the horizontal scroll bar (property 0x71, <c>m_eHorizonalScrollbarID</c>), or 0.</summary>
    public uint HorizontalScrollbarId { get; private set; }

    /// <summary>The element id of the vertical scroll bar (property 0x72, <c>m_eVerticalScrollbarID</c>), or 0.</summary>
    public uint VerticalScrollbarId { get; private set; }

    /// <summary>The horizontal scroll offset (<c>m_iScrollableX</c>).</summary>
    public int ScrollX { get; private set; }

    /// <summary>The vertical scroll offset (<c>m_iScrollableY</c>).</summary>
    public int ScrollY { get; private set; }

    /// <summary>The paper's width (<c>m_iScrollableWidth</c>).</summary>
    public int PaperWidth { get; private set; }

    /// <summary>The paper's height (<c>m_iScrollableHeight</c>).</summary>
    public int PaperHeight { get; private set; }

    /// <summary>
    /// <c>UIElement_Scrollable::SetScrollableXY</c>: moves the view to (<paramref name="x"/>, <paramref name="y"/>), kept on
    /// the paper unless <paramref name="force"/> is set or the element lets it leave (property 0x73 false). When it moved,
    /// the scroll bars follow and the class adjusts (<see cref="AdjustToScrollableXYChange"/>).
    /// </summary>
    public void SetScrollableXY(int x, int y, bool force = false)
    {
        var keepInBounds = !force && (!TryGetBoolProperty(UiPropertyIds.ScrollableKeepInBounds, out var keep) || keep);
        if (keepInBounds)
        {
            x = Math.Max(Math.Min(x, PaperWidth - Box.Width), 0);
            y = Math.Max(Math.Min(y, PaperHeight - Box.Height), 0);
        }

        if (x == ScrollX && y == ScrollY)
        {
            return;
        }

        ScrollX = x;
        ScrollY = y;
        UpdateScrollbarPosition(horizontal: true);
        UpdateScrollbarPosition(horizontal: false);
        AdjustToScrollableXYChange();
    }

    /// <summary>
    /// <c>UIElement_Scrollable::ResizeScrollableArea</c>: the paper is <paramref name="width"/> × <paramref name="height"/>;
    /// when that changed, the scroll bars follow and <see cref="UiMessageIds.PaperResized"/> goes out with the old size.
    /// </summary>
    public void ResizeScrollableArea(int width, int height)
    {
        if (width == PaperWidth && height == PaperHeight)
        {
            return;
        }

        var oldWidth = PaperWidth;
        var oldHeight = PaperHeight;
        PaperWidth = width;
        PaperHeight = height;
        UpdateScrollbarSize(horizontal: true);
        UpdateScrollbarSize(horizontal: false);
        BroadcastElementMessage(UiMessageIds.PaperResized, unchecked((uint)oldWidth), unchecked((uint)oldHeight));
    }

    /// <summary>Called after the view moved (<c>AdjustToScrollableXYChange</c>): text redraws, a list box lays its rows out again.</summary>
    protected virtual void AdjustToScrollableXYChange()
    {
    }

    /// <summary>
    /// How far one line or one page moves the view (<c>InqScrollDelta</c>): negative toward the start when
    /// <paramref name="towardStart"/>. The classes that scroll override it; this base knows no line size and moves only by
    /// pages (a loud TODO for a line).
    /// </summary>
    protected virtual int InqScrollDelta(bool horizontal, bool towardStart, bool page)
    {
        var delta = 0;
        if (page)
        {
            delta = horizontal ? Box.Width : Box.Height;
        }
        else
        {
            Manager.LogOnce($"scroll:{ClassId:X8}", LogSeverity.Warning,
                $"TODO(retailui-runtime): {UiClassRegistry.NameOf(ClassId)} has no line size (InqScrollDelta); {Desc.Describe()} does not scroll by lines");
        }

        return towardStart ? -delta : delta;
    }

    /// <inheritdoc/>
    protected internal override void OnSetAttribute(uint id, UiProperty? value)
    {
        base.OnSetAttribute(id, value);
        switch (id)
        {
            case UiPropertyIds.ScrollableHorizontalScrollbar:
                // Retail updates the bar before taking the new id, so the old bar (if any) is the one updated.
                var horizontal = value is UiEnumProperty h ? h.Value : 0;
                if (horizontal != 0)
                {
                    UpdateScrollbarSize(horizontal: true);
                }

                HorizontalScrollbarId = horizontal;
                break;
            case UiPropertyIds.ScrollableVerticalScrollbar:
                var vertical = value is UiEnumProperty v ? v.Value : 0;
                if (vertical != 0)
                {
                    UpdateScrollbarSize(horizontal: false);
                }

                VerticalScrollbarId = vertical;
                break;
        }
    }

    /// <summary>
    /// <c>UIElement_Scrollable::PostInit</c>: the element listens to each of its scroll bars (one that names this element
    /// back), and sizes them.
    /// </summary>
    protected internal override void PostInit()
    {
        base.PostInit();
        GetScrollbar(horizontal: true)?.RegisterForElementMessages(AsListener);
        GetScrollbar(horizontal: false)?.RegisterForElementMessages(AsListener);
        UpdateScrollbarSize(horizontal: true);
        UpdateScrollbarSize(horizontal: false);
    }

    /// <summary>
    /// <c>UIElement_Scrollable::GetScrollbarPointer_</c>: the scroll bar the id names, found as a relative
    /// (<see cref="UiElement.FindRelative"/>), when it is a scroll bar that finds this element back by the same rule.
    /// </summary>
    public UiScrollbar? GetScrollbar(bool horizontal)
    {
        var id = horizontal ? HorizontalScrollbarId : VerticalScrollbarId;
        if (id == 0 || FindRelative(id) is not UiScrollbar bar)
        {
            return null;
        }

        return ReferenceEquals(bar.FindRelative(ElementId), this) ? bar : null;
    }

    /// <summary>
    /// <c>UIElement_Scrollable::ListenToElementMessage</c>: its scroll bars' messages move the view; the wheel over the element
    /// (mouse action 5 or 6, not over the bar itself) goes to the vertical bar.
    /// </summary>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        var id = message.ElementId;
        if (id != 0 && (id == HorizontalScrollbarId || id == VerticalScrollbarId))
        {
            var horizontal = id == HorizontalScrollbarId;
            var bar = GetScrollbar(horizontal);
            if (bar is not null && ReferenceEquals(bar, message.Element))
            {
                HandleScrollbarMessage(horizontal, message.MessageId, bar);
                return UiListenResult.Consumed;
            }
        }

        if (message.MessageId == UiMessageIds.MouseDown && message.Param1 is UiMouseActions.WheelUp or UiMouseActions.WheelDown
            && GetScrollbar(horizontal: false) is { IsMouseOver: false } vertical)
        {
            vertical.HandleMouseWheel(message.Param1);
            return UiListenResult.Consumed;
        }

        return base.ListenToElementMessage(message);
    }

    /// <summary>
    /// <c>UIElement_Scrollable::MouseDown</c>: a press on a scrollable activates its window and gives it the keyboard focus
    /// (clicking a text field focuses it).
    /// </summary>
    public override void MouseDown(int x, int y, uint button)
    {
        base.MouseDown(x, y, button);
        var root = GetWindowRoot();
        if (root is { Activatable: true })
        {
            root.Activate();
            TakeFocus();
        }
    }

    /// <summary><c>UIElement_Scrollable::ResizeTo</c>: a size change re-sizes the bars and keeps the view on the paper.</summary>
    public override void ResizeTo(int width, int height)
    {
        var oldWidth = Box.Width;
        var oldHeight = Box.Height;
        base.ResizeTo(width, height);
        if (oldWidth != Box.Width || oldHeight != Box.Height)
        {
            UpdateScrollbarSize(horizontal: true);
            UpdateScrollbarSize(horizontal: false);
            SetScrollableXY(ScrollX, ScrollY);
        }
    }

    /// <summary><c>UIElement_Scrollable::RegisterInputMaps</c>: the element's maps, and the wheel map 0xA at the same priority.</summary>
    protected internal override void RegisterInputMaps(int priority)
    {
        base.RegisterInputMaps(priority);
        Manager.Input.RegisterInputMap(UiInputRouter.ScrollMap, this, priority);
    }

    /// <summary><c>UIElement_Scrollable::UnregisterInputMaps</c>.</summary>
    protected internal override void UnregisterInputMaps()
    {
        base.UnregisterInputMaps();
        Manager.Input.UnregisterInputMap(UiInputRouter.ScrollMap, this);
    }

    /// <summary>
    /// <c>UIElement_Scrollable::UpdateScrollbarPosition_</c>: the bar's position becomes the view's offset over the paper's
    /// slack (0 when there is none), set with updating held off so the bar does not report it back.
    /// </summary>
    private void UpdateScrollbarPosition(bool horizontal)
    {
        var bar = GetScrollbar(horizontal);
        if (bar is null)
        {
            return;
        }

        var offset = horizontal ? ScrollX : ScrollY;
        var slack = horizontal ? PaperWidth - Box.Width : PaperHeight - Box.Height;
        var position = Math.Abs((double)slack) > 0.00019999999 ? (float)(offset / (double)slack) : 0f;
        bar.SetBoolProperty(UiPropertyIds.ScrollbarDisallowUpdating, true);
        bar.SetFloatProperty(UiPropertyIds.ScrollbarPosition, position);
        bar.SetBoolProperty(UiPropertyIds.ScrollbarHorizontal, horizontal);
        bar.SetBoolProperty(UiPropertyIds.ScrollbarDisallowUpdating, false);
        bar.UpdateLayout();
    }

    /// <summary>
    /// <c>UIElement_Scrollable::UpdateScrollbarSize_</c>: the bar's widget is the visible share of the paper (at most 1), the
    /// bar is disabled when the paper fits (the view then goes back to the start); a bar that keeps its percentage on
    /// resize (property 0x74) moves the view to it, else the bar follows the view.
    /// </summary>
    private void UpdateScrollbarSize(bool horizontal)
    {
        var bar = GetScrollbar(horizontal);
        if (bar is null || _updatingScrollbars)
        {
            return;
        }

        _updatingScrollbars = true;
        try
        {
            var paper = horizontal ? PaperWidth : PaperHeight;
            var viewable = horizontal ? Box.Width : Box.Height;
            var disable = false;
            if (paper <= viewable)
            {
                paper = viewable;
                disable = true;
                if (horizontal)
                {
                    ScrollX = 0;
                }
                else
                {
                    ScrollY = 0;
                }
            }

            var offset = horizontal ? ScrollX : ScrollY;
            if (offset > paper)
            {
                SetOffset(horizontal, paper);
            }

            var widgetSize = paper == 0 ? 1f : (float)Math.Min(viewable / (double)paper, 1.0);
            var position = bar.TryGetFloatProperty(UiPropertyIds.ScrollbarPosition, out var p) && !disable ? p : 0f;
            bar.SetBoolProperty(UiPropertyIds.ScrollbarDisallowUpdating, true);
            bar.SetBoolProperty(UiPropertyIds.ScrollbarHorizontal, horizontal);
            bar.SetBoolProperty(UiPropertyIds.ScrollbarDisabled, disable);
            bar.SetFloatProperty(UiPropertyIds.ScrollbarWidgetSize, widgetSize);
            bar.SetBoolProperty(UiPropertyIds.ScrollbarDisallowUpdating, false);
            if (TryGetBoolProperty(UiPropertyIds.ScrollablePreservePercentageOnResize, out var keep) && keep)
            {
                bar.SetFloatProperty(UiPropertyIds.ScrollbarPosition, position);
                var slack = horizontal ? PaperWidth - Box.Width : PaperHeight - Box.Height;
                SetOffset(horizontal, (int)(slack * (double)position));
                AdjustToScrollableXYChange();
            }
            else
            {
                UpdateScrollbarPosition(horizontal);
            }
        }
        finally
        {
            _updatingScrollbars = false;
        }
    }

    /// <summary>
    /// <c>UIElement_Scrollable::HandleScrollbarMessage_</c>: a bar's new position moves the view to the same share of the
    /// paper's slack; its line and page requests move the view by <see cref="InqScrollDelta"/>.
    /// </summary>
    private void HandleScrollbarMessage(bool horizontal, uint messageId, UiScrollbar bar)
    {
        if (messageId == UiMessageIds.ScrollbarPosition)
        {
            var position = bar.TryGetFloatProperty(UiPropertyIds.ScrollbarPosition, out var p) ? p : 0f;
            var slack = horizontal ? PaperWidth - Box.Width : PaperHeight - Box.Height;
            SetOffset(horizontal, (int)(slack * (double)position));
            AdjustToScrollableXYChange();
        }
        else if (messageId is >= UiMessageIds.ScrollLineUp and <= UiMessageIds.ScrollPageDown)
        {
            var page = messageId is UiMessageIds.ScrollPageUp or UiMessageIds.ScrollPageDown;
            var towardStart = messageId is UiMessageIds.ScrollLineUp or UiMessageIds.ScrollPageUp;
            var delta = InqScrollDelta(horizontal, towardStart, page);
            SetScrollableXY(ScrollX + (horizontal ? delta : 0), ScrollY + (horizontal ? 0 : delta));
        }
    }

    private void SetOffset(bool horizontal, int value)
    {
        if (horizontal)
        {
            ScrollX = value;
        }
        else
        {
            ScrollY = value;
        }
    }
}

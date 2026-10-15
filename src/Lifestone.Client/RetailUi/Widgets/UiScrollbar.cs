using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Widgets;

/// <summary>
/// The Scroll bar (class 0xB, retail's <c>UIElement_Scrollbar</c>, a hot Button): a position from 0 to 1 (property 0x86)
/// shown by its widget (child 1) between two buttons; it drives the scrollable that names it (property 0x71 or 0x72) with
/// line and page requests and position changes.
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from the constructor (11.4186 0x0046FFF0, 11.6096 0x004703B0), <c>Initialize</c> (11.4186 0x00470520,
/// 11.6096 0x004708E0), <c>OnSetAttribute</c> (11.4186 0x004714D0, 11.6096 0x00471890), <c>UpdateLayout</c> (11.4186
/// 0x004710D0, 11.6096 0x00471490), <c>UpdateScrollingArea</c> (11.4186 0x00470AA0, 11.6096 0x00470E60),
/// <c>ComputeActiveSize</c> (11.4186 0x004702A0, 11.6096 0x00470660), <c>PositionToWidgetX0Y0</c> (11.4186 0x004709A0,
/// 11.6096 0x00470D60), <c>PointToPosition</c> (11.4186 0x00470890, 11.6096 0x00470C50), <c>ValidatePosition</c> (11.4186
/// 0x00470B60, 11.6096 0x00470F20), <c>SetScrollbarPosition</c> (11.4186 0x00470EC0, 11.6096 0x00471280),
/// <c>ScrollToPoint</c> (11.4186 0x00470F50, 11.6096 0x00471310), <c>ScrollNPixelsFromReset</c> (11.4186 0x00471020,
/// 11.6096 0x004713E0), <c>StartWidgetDrag</c> (11.4186 0x00470FB0, 11.6096 0x00471370), <c>HandlePageClick</c> (11.4186
/// 0x004706D0, 11.6096 0x00470A90), <c>HandleButtonClick</c> (11.4186 0x00470E90, 11.6096 0x00471250),
/// <c>HandleMoveSteps</c> (11.4186 0x00470E30, 11.6096 0x004711F0), <c>HandleMouseWheel</c> (11.4186 0x00471450, 11.6096
/// 0x00471810), <c>SetupDefaultHotClick</c> (11.4186 0x00470150, 11.6096 0x00470510), <c>UpdateButtonID_</c> (11.4186
/// 0x00470680, 11.6096 0x00470A40), <c>StartAnimation</c> (11.4186 0x00470550, 11.6096 0x00470910), <c>Global_Loop</c>
/// (11.4186 0x00470CA0, 11.6096 0x00471060), <c>ListenToElementMessage</c> (11.4186 0x00471870, 11.6096 0x00471C30) and
/// <c>RegisterInputMaps</c> (11.4186 0x004700E0, 11.6096 0x004704A0).
/// </para>
/// <para>
/// <b>Parts.</b> The increment button (0x77) sits at the start (top or left) and asks for a line toward the start; the
/// decrement button (0x78) at the end asks for a line toward the end; both repeat while held (hot buttons, 0.5 s then
/// 0.125 s unless they say otherwise). The widget fills the share of the track property 0x88 gives when proportional
/// (0x82, at least 0x89 pixels); the page areas (children 4 and 6 across, 5 and 7 down) fill the track on each side of it.
/// A press on the bar's track asks for a page toward the press, repeating while held; dragging the widget moves the
/// position (snapping back when the mouse strays more than 100 pixels across the bar); the wheel asks for lines. A
/// disabled bar (0x76) hides its page areas, hides itself with 0x79, and ghosts with 0x7A.
/// </para>
/// <para>
/// <b>Not built:</b> stop locations (0x7D-0x7F, 0x87) are a loud TODO: a bar with them moves like one without.
/// </para>
/// </remarks>
public class UiScrollbar : UiButton
{
    private const uint HorizontalBit = 0x1;
    private const uint ProportionalBit = 0x2;
    private const uint DisabledBit = 0x4;
    private const uint HideDisabledBit = 0x8;
    private const uint SmoothBit = 0x10;
    private const uint DisallowUpdatingBit = 0x20;
    private const uint MoveToTouchedBit = 0x40;
    private const uint GhostDisabledBit = 0x1000000;

    private uint _bits;
    private UiElement? _widget;
    private UiBox _scrollingArea;
    private bool _widgetDragActive;
    private (int X, int Y) _dragStart;
    private float _resetPosition;
    private bool _animating;
    private double _animStartTime;
    private double _animEndTime;
    private float _animStartPosition;
    private float _animEndPosition;

    /// <summary>A scroll bar of <paramref name="init"/>.</summary>
    public UiScrollbar(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>True for a horizontal bar (0x7B).</summary>
    public bool IsHorizontal => (_bits & HorizontalBit) != 0;

    /// <summary>True while the bar is disabled (0x76).</summary>
    public bool IsDisabled => (_bits & DisabledBit) != 0;

    /// <summary>The position (0x86), 0 to 1.</summary>
    public float Position => TryGetFloatProperty(UiPropertyIds.ScrollbarPosition, out var p) ? p : 0f;

    /// <summary>The widget (child 1), or null.</summary>
    public UiElement? Widget => _widget ??= GetChild(1);

    /// <summary>The increment button's id (<c>m_eIncrementButtonID</c>, 0x77), or 0.</summary>
    public uint IncrementButtonId { get; private set; }

    /// <summary>The decrement button's id (<c>m_eDecrementButtonID</c>, 0x78), or 0.</summary>
    public uint DecrementButtonId { get; private set; }

    /// <summary>The track between the buttons (<c>m_rectScrollingArea</c>), in the bar's coordinates, right and bottom exclusive.</summary>
    public UiBox ScrollingArea => _scrollingArea;

    /// <summary>True while the widget is being dragged.</summary>
    public bool IsDragging => _widgetDragActive;

    /// <summary><c>UIElement_Scrollbar::Initialize</c>: a bar that does not move to the touched point repeats its page clicks.</summary>
    protected internal override void Initialize()
    {
        base.Initialize();
        if (!(TryGetBoolProperty(UiPropertyIds.ScrollbarMoveToTouchedPosition, out var touched) && touched))
        {
            SetupDefaultHotClick(this);
        }
    }

    /// <summary><c>UIElement_Scrollbar::SetupDefaultHotClick</c>: the element repeats while held, 0.5 s then every 0.125 s unless it says otherwise.</summary>
    private static void SetupDefaultHotClick(UiElement element)
    {
        element.SetBoolProperty(UiPropertyIds.ButtonHotButton, true);
        if (!element.TryGetFloatProperty(UiPropertyIds.ButtonHotClickFirstInterval, out _))
        {
            element.SetFloatProperty(UiPropertyIds.ButtonHotClickFirstInterval, 0.5f);
        }

        if (!element.TryGetFloatProperty(UiPropertyIds.ButtonHotClickInterval, out _))
        {
            element.SetFloatProperty(UiPropertyIds.ButtonHotClickInterval, 0.125f);
        }
    }

    /// <inheritdoc/>
    protected internal override void OnSetAttribute(uint id, UiProperty? value)
    {
        base.OnSetAttribute(id, value);
        switch (id)
        {
            case UiPropertyIds.ScrollbarHorizontal:
                SetBit(HorizontalBit, GetBool(value));
                UpdateLayout();
                break;
            case UiPropertyIds.ScrollbarSmoothMovement:
                SetBit(SmoothBit, GetBool(value));
                break;
            case UiPropertyIds.ScrollbarProportional:
                SetBit(ProportionalBit, GetBool(value));
                UpdateLayout();
                break;
            case UiPropertyIds.ScrollbarDisabled:
                SetBit(DisabledBit, GetBool(value));
                UpdateLayout();
                break;
            case UiPropertyIds.ScrollbarHideWhenDisabled:
                SetBit(HideDisabledBit, GetBool(value));
                UpdateLayout();
                break;
            case UiPropertyIds.ScrollbarGhostWhenDisabled:
                SetBit(GhostDisabledBit, GetBool(value));
                UpdateLayout();
                break;
            case UiPropertyIds.ScrollbarWidgetSize:
            case UiPropertyIds.ScrollbarMinWidgetSize:
                UpdateLayout();
                break;
            case UiPropertyIds.ScrollbarMoveToTouchedPosition:
                var touched = GetBool(value);
                SetBit(MoveToTouchedBit, touched);
                Widget?.SetMouseVisible(!touched);
                if (touched)
                {
                    SetBoolProperty(UiPropertyIds.ButtonHotButton, false);
                }

                break;
            case UiPropertyIds.ScrollbarStopLocations:
            case UiPropertyIds.ScrollbarNumStopLocations:
            case UiPropertyIds.ScrollbarCurrentStop:
                Manager.LogOnce($"scrollbar:stops:{ElementId:X8}", LogSeverity.Warning,
                    $"TODO(retailui-runtime): {Desc.Describe()} has scroll-bar stop locations (UIElement_Scrollbar::SetScrollbarStop), which are not built; it scrolls freely");
                break;
            case UiPropertyIds.ScrollbarDisallowUpdating:
                SetBit(DisallowUpdatingBit, GetBool(value));
                break;
            case UiPropertyIds.ScrollbarPosition:
                var position = value is UiFloatProperty p ? p.Value : 0f;
                var valid = Math.Clamp(position, 0f, 1f);
                if (Math.Abs(position - valid) <= 0.00019999999f)
                {
                    UpdateLayout();
                }
                else
                {
                    SetFloatProperty(UiPropertyIds.ScrollbarPosition, valid);
                }

                break;
            case UiPropertyIds.ScrollbarGoalPosition:
                if ((_bits & SmoothBit) != 0)
                {
                    StartAnimation();
                }
                else
                {
                    SetFloatProperty(UiPropertyIds.ScrollbarPosition, value is UiFloatProperty goal ? goal.Value : 0f);
                }

                break;
            case UiPropertyIds.ScrollbarIncrementButton:
                IncrementButtonId = value is UiEnumProperty inc ? inc.Value : 0;
                UpdateButton(IncrementButtonId);
                break;
            case UiPropertyIds.ScrollbarDecrementButton:
                DecrementButtonId = value is UiEnumProperty dec ? dec.Value : 0;
                UpdateButton(DecrementButtonId);
                break;
            case UiPropertyIds.ScrollbarPageFirstInterval:
                SetFloatProperty(UiPropertyIds.ButtonHotClickFirstInterval, value is UiFloatProperty first ? first.Value : 0f);
                break;
            case UiPropertyIds.ScrollbarPageInterval:
                SetFloatProperty(UiPropertyIds.ButtonHotClickInterval, value is UiFloatProperty interval ? interval.Value : 0f);
                break;
        }
    }

    /// <summary>
    /// <c>UIElement_Scrollbar::UpdateButtonID_</c>: a button the bar holds repeats while held; one not created yet is set up
    /// on the next frame (<c>Global_Loop</c>).
    /// </summary>
    private void UpdateButton(uint id)
    {
        if (GetChildRecursive(id) is { } button)
        {
            SetupDefaultHotClick(button);
        }
        else if (id != 0)
        {
            _pendingButtons = true;
            Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        }
    }

    private bool _pendingButtons;

    /// <summary><c>UIElement_Scrollbar::GetButtonPointer_</c>: the button the id names, when it is a button that finds this bar back.</summary>
    private UiButton? GetButton(bool increment)
    {
        var id = increment ? IncrementButtonId : DecrementButtonId;
        if (id == 0 || FindRelative(id) is not UiButton button)
        {
            return null;
        }

        return ReferenceEquals(button.FindRelative(ElementId), this) ? button : null;
    }

    /// <summary>
    /// <c>UIElement_Scrollbar::UpdateLayout</c>: the track, the widget's size and place, the page areas, and the bar's own
    /// visibility and ghosting. Held off while property 0x75 is set (the scrollable sets several properties at once).
    /// </summary>
    public void UpdateLayout()
    {
        _widget ??= GetChild(1);
        if ((_bits & DisallowUpdatingBit) != 0)
        {
            return;
        }

        UpdateScrollingArea();
        if (_widget is { } widget)
        {
            var widgetWidth = widget.Box.Width;
            var widgetHeight = widget.Box.Height;
            if ((_bits & ProportionalBit) != 0)
            {
                var share = TryGetFloatProperty(UiPropertyIds.ScrollbarWidgetSize, out var s) ? s : 1f;
                var minimum = TryGetIntegerProperty(UiPropertyIds.ScrollbarMinWidgetSize, out var m) ? m : 0;
                var track = IsHorizontal ? _scrollingArea.X1 - _scrollingArea.X0 : _scrollingArea.Y1 - _scrollingArea.Y0;
                var size = Math.Max((int)((track * (double)share) + 0.5), minimum);
                if (IsHorizontal)
                {
                    widgetWidth = size;
                }
                else
                {
                    widgetHeight = size;
                }

                widget.ResizeTo(widgetWidth, widgetHeight);
            }

            var (wx, wy) = PositionToWidget(Position);
            widget.MoveTo(wx, wy);
            var showPages = !IsDisabled;
            var centreX = wx + (widgetWidth / 2);
            var centreY = wy + (widgetHeight / 2);
            PlacePageArea(4, showPages, 0, 0, centreX, Box.Height);
            PlacePageArea(6, showPages, centreX, 0, Box.Width - centreX, Box.Height);
            PlacePageArea(5, showPages, 0, 0, Box.Width, centreY);
            PlacePageArea(7, showPages, 0, centreY, Box.Width, Box.Height - centreY);
        }

        SetVisible(!(IsDisabled && (_bits & HideDisabledBit) != 0));
        if ((_bits & GhostDisabledBit) != 0)
        {
            SetState(IsDisabled ? UiStateIds.Ghosted : UiStateIds.Normal);
        }
    }

    private void PlacePageArea(uint id, bool show, int x, int y, int width, int height)
    {
        if (GetChild(id) is { } area)
        {
            area.SetVisible(show);
            area.MoveTo(x, y);
            area.ResizeTo(width, height);
        }
    }

    /// <summary>
    /// <c>UIElement_Scrollbar::UpdateScrollingArea</c>: the track is the bar less the increment button (moved to the start)
    /// and the decrement button (moved to the end).
    /// </summary>
    private void UpdateScrollingArea()
    {
        var left = 0;
        var top = 0;
        var right = Box.Width;
        var bottom = Box.Height;
        if (GetButton(increment: true) is { } increment)
        {
            top += increment.Box.Height;
            left += increment.Box.Width;
            increment.MoveTo(0, 0);
        }

        if (GetButton(increment: false) is { } decrement)
        {
            right -= decrement.Box.Width;
            bottom -= decrement.Box.Height;
            decrement.MoveTo(right, bottom);
        }

        _scrollingArea = new UiBox(left, top, right, bottom);
    }

    /// <summary><c>UIElement_Scrollbar::ComputeActiveSize</c>: the track less the widget (unless the stops leave out the ends, 0x7E).</summary>
    private (int Width, int Height) ActiveSize()
    {
        var width = _scrollingArea.X1 - _scrollingArea.X0;
        var height = _scrollingArea.Y1 - _scrollingArea.Y0;
        if (!(TryGetBoolProperty(UiPropertyIds.ScrollbarStopsDontIncludeEndpoints, out var no) && no))
        {
            width -= Widget?.Box.Width ?? 0;
            height -= Widget?.Box.Height ?? 0;
        }

        return (width, height);
    }

    /// <summary><c>UIElement_Scrollbar::PositionToWidgetX0Y0</c>: the widget's corner for <paramref name="position"/>.</summary>
    private (int X, int Y) PositionToWidget(float position)
    {
        var (width, height) = ActiveSize();
        var size = IsHorizontal ? width : height;
        var start = IsHorizontal ? _scrollingArea.X0 : _scrollingArea.Y0;
        var place = start + (int)(size * (double)position);
        if (TryGetBoolProperty(UiPropertyIds.ScrollbarStopsDontIncludeEndpoints, out var no) && no)
        {
            place -= (IsHorizontal ? Widget?.Box.Width ?? 0 : Widget?.Box.Height ?? 0) / 2;
        }

        return IsHorizontal ? (place, 0) : (0, place);
    }

    /// <summary><c>UIElement_Scrollbar::PointToPosition</c>: the position a point of the bar (centring the widget on it) stands for.</summary>
    private float PointToPosition(int x, int y)
    {
        var (maxX, maxY) = ActiveSize();
        maxX--;
        maxY--;
        var px = x - _scrollingArea.X0;
        var py = y - _scrollingArea.Y0;
        if (!(TryGetBoolProperty(UiPropertyIds.ScrollbarStopsDontIncludeEndpoints, out var no) && no))
        {
            px = Math.Clamp(px - ((Widget?.Box.Width ?? 0) / 2), 0, Math.Max(maxX, 0));
            py = Math.Clamp(py - ((Widget?.Box.Height ?? 0) / 2), 0, Math.Max(maxY, 0));
        }

        var max = IsHorizontal ? maxX : maxY;
        return max <= 1 ? 0f : (float)((IsHorizontal ? px : py) / (double)max);
    }

    /// <summary>
    /// <c>UIElement_Scrollbar::SetScrollbarPosition</c>: the position (clamped to 0-1) is set and announced
    /// (<see cref="UiMessageIds.ScrollbarPosition"/>, param1 the position × 1000).
    /// </summary>
    public void SetScrollbarPosition(float position)
    {
        position = Math.Clamp(position, 0f, 1f);
        SetFloatProperty(UiPropertyIds.ScrollbarPosition, position);
        BroadcastElementMessage(UiMessageIds.ScrollbarPosition, (uint)(position * 1000.0), 0xFFFFFFFF);
    }

    /// <summary><c>UIElement_Scrollbar::HandleMouseWheel</c>: the wheel asks for a line (up toward the start).</summary>
    public void HandleMouseWheel(uint mouseAction)
    {
        if (TryGetFloatProperty(UiPropertyIds.ScrollbarPosition, out _))
        {
            BroadcastElementMessage(mouseAction == UiMouseActions.WheelUp ? UiMessageIds.ScrollLineUp : UiMessageIds.ScrollLineDown);
        }
    }

    /// <summary><c>UIElement_Scrollbar::HandleMoveSteps</c> without stops: a line toward the start for a positive count, else toward the end.</summary>
    private void HandleMoveSteps(int steps) =>
        BroadcastElementMessage(steps > 0 ? UiMessageIds.ScrollLineUp : UiMessageIds.ScrollLineDown, unchecked((uint)steps));

    /// <summary><c>UIElement_Scrollbar::StartAnimation</c>: the position moves to the goal (0x85) over the duration (0x84).</summary>
    private void StartAnimation()
    {
        if (!TryGetFloatProperty(UiPropertyIds.ScrollbarSmoothMovementDuration, out var duration) || Math.Abs(duration) <= 0.00019999999f)
        {
            return;
        }

        _animStartPosition = Position;
        _animEndPosition = TryGetFloatProperty(UiPropertyIds.ScrollbarGoalPosition, out var goal) ? goal : _animStartPosition;
        _animStartTime = Manager.Now;
        _animEndTime = duration + _animStartTime;
        if (!_animating)
        {
            _animating = true;
            Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        }
    }

    /// <summary><c>UIElement_Scrollbar::Global_Loop</c>: buttons created late are placed and set up; a smooth movement steps.</summary>
    protected internal override void ListenToGlobalMessage(uint messageId, int data)
    {
        base.ListenToGlobalMessage(messageId, data);
        if (messageId != UiGlobalMessageIds.Frame)
        {
            return;
        }

        if (_pendingButtons)
        {
            _pendingButtons = false;
            if (Parent?.GetChildRecursive(IncrementButtonId) is { } increment)
            {
                increment.SetParent(this);
                increment.MoveTo(0, 0);
                SetupDefaultHotClick(increment);
            }

            if (Parent?.GetChildRecursive(DecrementButtonId) is { } decrement)
            {
                decrement.SetParent(this);
                decrement.MoveTo(Box.Width - decrement.Box.Width, Box.Height - decrement.Box.Height);
                SetupDefaultHotClick(decrement);
            }

            UpdateScrollingArea();
            if (!_animating && !IsHotClicking)
            {
                Manager.UnregisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
            }
        }

        if (_animating)
        {
            var t = (float)((Manager.Now - _animStartTime) / (_animEndTime - _animStartTime));
            if (t >= 1f)
            {
                t = 1f;
                _animating = false;
                if (!IsHotClicking)
                {
                    Manager.UnregisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
                }
            }

            SetFloatProperty(UiPropertyIds.ScrollbarPosition, ((_animEndPosition - _animStartPosition) * t) + _animStartPosition);
        }
    }

    /// <inheritdoc/>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        var element = message.Element;
        var onWidget = ReferenceEquals(element, Widget) || (ReferenceEquals(element, this) && (_bits & MoveToTouchedBit) != 0);
        var leftDown = element.IsMouseDown(UiMouseActions.Left) || element.IsMouseDown(UiMouseActions.LeftDouble);
        var left = message.Param1 is UiMouseActions.Left or UiMouseActions.LeftDouble;
        switch (message.MessageId)
        {
            case UiMessageIds.ButtonHotClick:
                if (message.ElementId != 0 && (message.ElementId == IncrementButtonId || message.ElementId == DecrementButtonId))
                {
                    HandleMoveSteps(message.ElementId != IncrementButtonId ? -1 : 1);
                }
                else if (ReferenceEquals(element, this))
                {
                    HandlePageClick();
                }

                break;
            case UiMessageIds.MouseDown:
                if (onWidget && leftDown)
                {
                    StartWidgetDrag(message);
                }

                break;
            case UiMessageIds.MouseUp:
                if (onWidget && left && _widgetDragActive)
                {
                    _widgetDragActive = false;
                }

                break;
            case UiMessageIds.MouseMove:
                if (onWidget && leftDown)
                {
                    var (x, y) = PointInBar(message);
                    if (Math.Abs(IsHorizontal ? y : x) <= 100)
                    {
                        if ((_bits & MoveToTouchedBit) != 0)
                        {
                            SetScrollbarPosition(PointToPosition(x, y));
                        }
                        else
                        {
                            ScrollPixelsFromReset(x - _dragStart.X, y - _dragStart.Y);
                        }

                        _widgetDragActive = true;
                    }
                    else if (_widgetDragActive)
                    {
                        SetScrollbarPosition(_resetPosition);
                        _widgetDragActive = false;
                    }
                }

                break;
        }

        return base.ListenToElementMessage(message);
    }

    /// <summary><c>UIElement_Scrollbar::StartWidgetDrag</c>: the drag starts from the press (jumping there first when the bar moves to the touched point).</summary>
    private void StartWidgetDrag(in UiElementMessage message)
    {
        var (x, y) = PointInBar(message);
        if ((_bits & MoveToTouchedBit) != 0)
        {
            SetScrollbarPosition(PointToPosition(x, y));
        }

        _dragStart = (x, y);
        _resetPosition = Position;
        _widgetDragActive = true;
    }

    /// <summary><c>UIElement_Scrollbar::ScrollNPixelsFromReset</c>: the drag's distance over the active size, added to where it began.</summary>
    private void ScrollPixelsFromReset(int dx, int dy)
    {
        var (width, height) = ActiveSize();
        var size = IsHorizontal ? width : height;
        SetScrollbarPosition(size != 0 ? (float)(((IsHorizontal ? dx : dy) / (double)size) + _resetPosition) : 0f);
    }

    /// <summary><c>UIElement_Scrollbar::HandlePageClick</c>: a held press on the track before the widget asks for a page toward the start, after it toward the end.</summary>
    private void HandlePageClick()
    {
        if (Widget is not { } widget)
        {
            return;
        }

        var (mx, my) = Manager.Input.MousePosition;
        var screen = ScreenBox;
        var point = IsHorizontal ? mx - screen.X0 : my - screen.Y0;
        var start = IsHorizontal ? widget.Box.X0 : widget.Box.Y0;
        var size = IsHorizontal ? widget.Box.Width : widget.Box.Height;
        var before = point < start;
        if (before || point > start + size)
        {
            BroadcastElementMessage(before ? UiMessageIds.ScrollPageUp : UiMessageIds.ScrollPageDown);
        }
    }

    /// <summary><c>UIElement_Scrollbar::GetScrollbarPositionFromMessage</c>: a mouse message's point in the bar's coordinates.</summary>
    private (int X, int Y) PointInBar(in UiElementMessage message)
    {
        var (x, y) = message.ElementPoint;
        if (ReferenceEquals(message.Element.Parent, this))
        {
            x += message.Element.Box.X0;
            y += message.Element.Box.Y0;
        }

        return (x, y);
    }

    /// <summary><c>UIElement_Scrollbar::ResizeTo</c> (11.4186 0x00471430, 11.6096 0x004717F0): the layout follows the size.</summary>
    public override void ResizeTo(int width, int height)
    {
        base.ResizeTo(width, height);
        UpdateLayout();
    }

    /// <summary><c>UIElement_Scrollbar::RegisterInputMaps</c>: the text's maps, and the wheel map 0xA.</summary>
    protected internal override void RegisterInputMaps(int priority)
    {
        base.RegisterInputMaps(priority);
        Manager.Input.RegisterInputMap(UiInputRouter.ScrollMap, this, priority);
    }

    private void SetBit(uint bit, bool on) => _bits = on ? _bits | bit : _bits & ~bit;
}

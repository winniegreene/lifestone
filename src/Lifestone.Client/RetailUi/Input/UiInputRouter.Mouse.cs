using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Input;

public sealed partial class UiInputRouter
{
    private readonly HashSet<uint> _actionsTriggeringCapture = [];
    private readonly HashSet<UiMouseButton> _worldButtons = [];
    private UiElement[] _oldChain = new UiElement[16];
    private UiElement[] _newChain = new UiElement[16];
    private int _captureCount;
    private bool _mouseHasLeftWindow = true;
    private bool _performMouseHitTest;
    private bool _hoverStarted;
    private double _lastMouseMoveTime;
    private double _tooltipStart;
    private double _tooltipDuration = 10.0;

    /// <summary>Where the mouse is, in logical window pixels (<see cref="UiElementManager.DisplayOrigin"/> included).</summary>
    public (int X, int Y) MousePosition { get; private set; }

    /// <summary>The element that takes every mouse event until its buttons are up (<c>m_pElementWithMouseCapture</c>), or null.</summary>
    public UiElement? ElementWithMouseCapture { get; private set; }

    /// <summary>The mouse-visible element the mouse is in (<c>m_pElementLastEntered</c>), or null.</summary>
    public UiElement? ElementLastEntered { get; private set; }

    /// <summary>The element the hit test found under the mouse (<c>m_pElementLastOver</c>), mouse-visible or not, or null.</summary>
    public UiElement? ElementLastOver { get; private set; }

    /// <summary>The open tooltip (<c>m_pTooltipElement</c>), or null.</summary>
    public UiElement? TooltipElement { get; private set; }

    /// <summary>The element whose tooltip is open (<c>m_pTooltipOwner</c>), or null.</summary>
    public UiElement? TooltipOwner { get; private set; }

    /// <summary>True when tooltips open (the <c>Misc.TooltipEnable</c> preference; on by default).</summary>
    public bool TooltipsEnabled { get; set; } = true;

    /// <summary>
    /// How long the mouse rests before a tooltip opens, in seconds (the <c>Misc.TooltipDelay</c> preference; 0.25 by default,
    /// <c>UIElementManager::UIElementManager</c>); an element's property 0x50 overrides it.
    /// </summary>
    public double TooltipDelaySeconds { get; set; } = 0.25;

    /// <summary>How long a tooltip stays open, in seconds (<c>m_tooltipDuration</c>; 10 by default).</summary>
    public double TooltipDurationSeconds { get; set; } = 10.0;

    /// <summary>
    /// The mouse moved to (<paramref name="x"/>, <paramref name="y"/>) in logical window pixels: window pixels divided by
    /// the UI scale, so a forced, centred display starts at <see cref="UiElementManager.DisplayOrigin"/>
    /// (<c>UIElementManager::MouseMoveHandler</c>, 11.4186 0x0045E710, 11.6096 0x0045E7F0): the element under it is found
    /// again (<see cref="SwitchMouseOver"/>) and the element with the capture, else the one the mouse is in, hears the
    /// move. True when the mouse is over the UI (or the UI has the capture).
    /// </summary>
    public bool MouseMove(int x, int y)
    {
        MousePosition = (x, y);
        _lastMouseMoveTime = _manager.Now;
        _mouseHasLeftWindow = false;
        var hit = _manager.HitTest(x, y);
        SwitchMouseOver(hit);
        var target = ElementWithMouseCapture ?? ElementLastEntered;
        target?.MouseMove(x, y);
        return ElementWithMouseCapture is not null || (hit is not null && !IsWorldHole(hit));
    }

    /// <summary>
    /// <c>UIElementManager::MouseLeaveEvent</c> (11.4186 0x0045C2A0, 11.6096 0x0045C380): the mouse left the window; nothing
    /// is under it.
    /// </summary>
    public void MouseLeave()
    {
        _mouseHasLeftWindow = true;
        SwitchMouseOver(null);
    }

    /// <summary>
    /// A mouse button went down or up at the current mouse position (<see cref="MouseMove"/> first), a double click's
    /// press flagged by <paramref name="doubleClick"/> (the host's, as retail took Windows' double-click time). The button
    /// is looked up in the input maps like a key; the UI's map 3 (<c>UIElementManager::OnAction</c>) turns it into a mouse
    /// action for the element under the mouse. What the UI does not take goes to the world (<see cref="WorldPointer"/>),
    /// as does a click on a viewport hole. True when the UI took it.
    /// </summary>
    public bool MouseButton(UiMouseButton button, bool down, bool doubleClick = false)
    {
        _mouseTaken = false;
        var extra = down && doubleClick ? DoubleClickDown | NearbyDown : 0u;
        FireInputEvent(RetailKeyBindings.Mouse(button), down, extra);
        if (!down && _worldButtons.Remove(button))
        {
            // The press went to the world, so the release does too, after the input maps ended the button's action.
            WorldPointer?.OnWorldPointer(MousePosition.X, MousePosition.Y, button, down: false, doubleClick: false);
            return false;
        }

        if (_mouseTaken)
        {
            return true;
        }

        if (down && (ElementLastOver is null || IsWorldHole(ElementLastOver)))
        {
            _worldButtons.Add(button);
            WorldPointer?.OnWorldPointer(MousePosition.X, MousePosition.Y, button, down: true, doubleClick);
            return false;
        }

        return ElementLastOver is not null;
    }

    /// <summary>
    /// A wheel notch (<paramref name="notches"/> positive is up, toward the start). Each notch is looked up in the input
    /// maps (the wheel map 0xA a focused scrollable registers); one no map takes goes to the element under the mouse as
    /// mouse action 5 or 6 (Lifestone's route: retail's way from the wheel to a scrollable that does not have the focus is
    /// not in the decompile read; TODO(retailui-runtime): confirm in 11.6096), which scrollables turn into scrolling as
    /// <c>UIElement_Scrollable::ListenToElementMessage</c> does. True when the UI took a notch.
    /// </summary>
    public bool MouseWheel(int notches)
    {
        var taken = false;
        var up = notches > 0;
        for (var i = Math.Abs(notches); i > 0; i--)
        {
            var control = RetailKeyBindings.Wheel(up);
            var mapped = FireInputEvent(control, down: true, 0);
            FireInputEvent(control, down: false, 0);
            if (mapped)
            {
                taken = true;
                continue;
            }

            _manager.LogOnce("input:wheel", LogSeverity.Warning,
                "TODO(retailui-runtime): a wheel notch no input map took goes to the element under the mouse as mouse action 5 or 6; this route is Lifestone's (retail's way from the wheel to a scrollable without the focus is not in the decompile read; confirm in 11.6096)");
            var action = up ? UiMouseActions.WheelUp : UiMouseActions.WheelDown;
            _mouseTaken = false;
            MouseDownEvent(action);
            MouseUpEvent(action);
            taken |= _mouseTaken;
        }

        return taken;
    }

    /// <summary>
    /// The mouse moved by (<paramref name="dx"/>, <paramref name="dy"/>) in mouse-look mode (<c>CInputManager::CallMouseLookHandler</c>,
    /// 11.4186 0x00431220, 11.6096 0x004313C0). True when it went to the world: never while the window has no focus
    /// (<see cref="ClientActive"/>; <c>CInputManager_WIN32::Activate</c> leaves mouse look then and enters it again on the
    /// focus's return).
    /// </summary>
    public bool MouseLook(float dx, float dy)
    {
        if (!MouseLookMode || !ClientActive || WorldPointer is null)
        {
            return false;
        }

        WorldPointer.OnMouseLook(dx, dy);
        return true;
    }

    /// <summary>Asks for the mouse to be hit-tested again at the next frame (<c>m_bPerformMouseHitTest</c>): something under it changed.</summary>
    public void RequestMouseHitTest() => _performMouseHitTest = true;

    private bool _mouseTaken;

    /// <summary><c>UIElementManager::OnAction</c> (11.4186 0x0045E6A0, 11.6096 0x0045E780): the UI maps' mouse actions.</summary>
    private bool ManagerOnAction(uint actionId, RetailActivation activation)
    {
        if (activation.Start)
        {
            if (actionId == 4)
            {
                return true;
            }

            if (actionId is > 4 and <= 0xF)
            {
                MouseDownEvent(actionId);
                return true;
            }

            return false;
        }

        switch (actionId)
        {
            case >= 5 and <= 0xC:
                MouseUpEvent(actionId);
                return true;
            case 4 or 0xD or 0xE or 0xF:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// <c>UIElementManager::MouseDownEvent</c> (11.4186 0x0045DB60, 11.6096 0x0045DC40): the element under the mouse is found
    /// again; the element the mouse is in takes the mouse capture (a tap does not); the window under the mouse activates
    /// (or the active one deactivates when the click is outside every window); then the capture (for a tap, the capture or
    /// the element the mouse is in) hears the press, or, when nothing does, the focused element gives up the focus.
    /// </summary>
    private void MouseDownEvent(uint action)
    {
        var (x, y) = MousePosition;
        SwitchMouseOver(_mouseHasLeftWindow ? null : _manager.HitTest(x, y));
        var isTap = UiMouseActions.IsTap(action);
        if (action == UiMouseActions.Left && ElementLastOver is { Dragable: true })
        {
            _manager.LogOnce("input:drag", LogSeverity.Warning, "TODO(retailui-runtime): drag and drop of UI items (UIElementManager::StartDragandDrop) is not built; a dragable element is clicked, not dragged");
        }

        var tapOnCapture = false;
        if (ElementWithMouseCapture is not null)
        {
            if (_actionsTriggeringCapture.Count != 0)
            {
                if (isTap)
                {
                    tapOnCapture = true;
                }
                else
                {
                    _actionsTriggeringCapture.Add(action);
                }
            }
        }
        else
        {
            if (ElementLastEntered is { } entered && !isTap)
            {
                SetMouseCapture(entered);
                _actionsTriggeringCapture.Add(action);
            }

            if (ElementLastOver is { } over)
            {
                var root = over.GetWindowRoot();
                if (!ReferenceEquals(root, _manager.ActiveElement))
                {
                    if (root is { Activatable: true })
                    {
                        root.Activate();
                    }
                    else
                    {
                        _manager.ActiveElement?.Deactivate();
                    }
                }
            }
            else
            {
                _manager.ActiveElement?.Deactivate();
            }
        }

        if (isTap || tapOnCapture)
        {
            var target = ElementWithMouseCapture ?? ElementLastEntered;
            if (target is not null)
            {
                _mouseTaken = true;
                target.MouseTap(x, y, action);
                return;
            }
        }
        else if (ElementWithMouseCapture is { } capture)
        {
            _mouseTaken = !IsWorldHole(capture);
            capture.NotifyMouseDown(x, y, action);
            return;
        }

        _manager.FocusElement?.RelinquishFocus();
    }

    /// <summary>
    /// <c>UIElementManager::MouseUpEvent</c> (11.4186 0x0045DD50, 11.6096 0x0045DE30): the capture (else the element the mouse
    /// is in) hears the release; the capture ends when the last button that made it is up; the element under the mouse is
    /// found again.
    /// </summary>
    private void MouseUpEvent(uint action)
    {
        var (x, y) = MousePosition;
        var target = ElementLastEntered;
        if (ElementWithMouseCapture is { } capture)
        {
            target = capture;
            if (_actionsTriggeringCapture.Count != 0 && _actionsTriggeringCapture.Remove(action) && _actionsTriggeringCapture.Count == 0)
            {
                ReleaseMouseCapture(capture);
            }
        }

        SwitchMouseOver(_mouseHasLeftWindow ? null : _manager.HitTest(x, y));
        if (target is not null)
        {
            _mouseTaken = !IsWorldHole(target);
            target.NotifyMouseUp(x, y, action);
        }
    }

    /// <summary>
    /// <c>UIElementManager::SetMouseCapture</c> (11.4186 0x0045D270, 11.6096 0x0045D350): <paramref name="element"/> takes every
    /// mouse event until <see cref="ReleaseMouseCapture"/> (counted).
    /// </summary>
    public void SetMouseCapture(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!ReferenceEquals(ElementWithMouseCapture, element))
        {
            _captureCount = 0;
            ElementWithMouseCapture = element;
            _actionsTriggeringCapture.Clear();
        }

        _captureCount++;
    }

    /// <summary><c>UIElementManager::ReleaseMouseCapture</c> (11.4186 0x0045D2B0, 11.6096 0x0045D390).</summary>
    public void ReleaseMouseCapture(UiElement element)
    {
        if (!ReferenceEquals(ElementWithMouseCapture, element))
        {
            return;
        }

        _lastMouseMoveTime = _manager.Now;
        _actionsTriggeringCapture.Clear();
        if (--_captureCount == 0)
        {
            ElementWithMouseCapture = null;
        }
    }

    /// <summary>
    /// <c>UIElementManager::SwitchMouseOver</c> (11.4186 0x0045B560, 11.6096 0x0045B640): <paramref name="over"/> is under the
    /// mouse. The element the mouse is in becomes it when it is mouse-visible (or it is the capture; with a capture, no other
    /// element can be entered). When that changes, the elements whose area the mouse left or entered hear it
    /// (<c>HierarchyNotifier&lt;MouseOverNotificationLogic&gt;::CallFunctions</c>, 11.4186 0x0045ADE0, 11.6096 0x0045AEC0),
    /// the hover stops, the old element hears the mouse left it and the new one that it entered, and the cursor is checked.
    /// </summary>
    private void SwitchMouseOver(UiElement? over)
    {
        ElementLastOver = over;
        UiElement? entered = null;
        if (over is not null)
        {
            if (ElementWithMouseCapture is not null ? ReferenceEquals(ElementWithMouseCapture, over) : over.IsMouseVisible)
            {
                entered = over;
            }
        }

        if (ReferenceEquals(entered, ElementLastEntered))
        {
            return;
        }

        CallHierarchyFunctions(ElementLastEntered, entered);
        if (ElementLastEntered is { } old)
        {
            StopHover();
            old.MouseOverTop(false);
        }

        if (entered is { IsMouseVisible: true })
        {
            ElementLastEntered = entered;
            entered.MouseOverTop(true);
        }
        else
        {
            ElementLastEntered = null;
        }

        _manager.Cursor.CheckCursor();
    }

    /// <summary>
    /// <c>HierarchyNotifier&lt;MouseOverNotificationLogic&gt;::CallFunctions</c>: the chains from each leaf up to the top are
    /// compared from the top down; below the shared part, the old chain hears the mouse left and the new one that it
    /// entered. As retail compares, the leaves themselves always hear it, even when one is an ancestor of the other.
    /// </summary>
    private void CallHierarchyFunctions(UiElement? oldLeaf, UiElement? newLeaf)
    {
        var oldCount = Chain(oldLeaf, ref _oldChain);
        var newCount = Chain(newLeaf, ref _newChain);
        var o = oldCount - 1;
        var n = newCount - 1;
        while (o > 0 && n > 0 && ReferenceEquals(_oldChain[o], _newChain[n]))
        {
            o--;
            n--;
        }

        if (oldLeaf is not null)
        {
            for (var k = 0; k <= o; k++)
            {
                _oldChain[k].MouseOver(false);
            }
        }

        if (newLeaf is not null)
        {
            for (var k = 0; k <= n; k++)
            {
                _newChain[k].MouseOver(true);
            }
        }

        Array.Clear(_oldChain, 0, oldCount);
        Array.Clear(_newChain, 0, newCount);
    }

    private static int Chain(UiElement? leaf, ref UiElement[] chain)
    {
        var count = 0;
        for (var e = leaf; e is not null; e = e.Parent)
        {
            if (count == chain.Length)
            {
                Array.Resize(ref chain, count * 2);
            }

            chain[count++] = e;
        }

        return count;
    }

    /// <summary><c>UIElementManager::DoMouseUpdate</c> (11.4186 0x0045CC60, 11.6096 0x0045CDE0): the element under the mouse, found again.</summary>
    private void DoMouseUpdate()
    {
        _performMouseHitTest = false;
        var (x, y) = MousePosition;
        SwitchMouseOver(_mouseHasLeftWindow ? null : _manager.HitTest(x, y));
    }

    // ---------------------------------------------------------------- Hover and tooltips

    /// <summary>
    /// <c>UIElementManager::CheckTooltip</c> (11.4186 0x0045B6E0, 11.6096 0x0045B7C0): once the mouse has rested on the
    /// element it is in for the delay (its property 0x50, else <see cref="TooltipDelaySeconds"/>) with no capture, the
    /// hover starts (<see cref="UiElement.MouseHover"/>); an open tooltip closes when its time is up.
    /// </summary>
    private void CheckTooltip(double now)
    {
        if (_hoverStarted || _mouseHasLeftWindow)
        {
            if (TooltipElement is { } tooltip && _tooltipDuration <= now - _tooltipStart)
            {
                DeleteTooltip(tooltip);
                SwitchMouseOver(null);
            }

            return;
        }

        if (ElementWithMouseCapture is not null || ElementLastEntered is not { } entered)
        {
            return;
        }

        var delay = entered.TryGetFloatProperty(UiPropertyIds.ElementHoverDelay, out var custom) ? custom : TooltipDelaySeconds;
        if (delay + _lastMouseMoveTime < now)
        {
            StartHover();
        }
    }

    /// <summary><c>UIElementManager::StartHover</c> (11.4186 0x00459250, 11.6096 0x00459370).</summary>
    private void StartHover()
    {
        if (!_hoverStarted && ElementLastEntered is { } entered)
        {
            _hoverStarted = entered.MouseHover(MousePosition.X, MousePosition.Y);
        }
    }

    /// <summary><c>UIElementManager::StopHover</c> (11.4186 0x00459280, 11.6096 0x004593A0): the hover ends and the tooltip closes.</summary>
    private void StopHover()
    {
        if (_hoverStarted)
        {
            _hoverStarted = false;
            ElementLastEntered?.MouseUnhover();
        }

        if (TooltipElement is { } tooltip)
        {
            DeleteTooltip(tooltip);
        }
    }

    /// <summary>
    /// <c>UIElementManager::ResetTooltip</c> (11.4186 0x0045C360, 11.6096 0x0045C440): an element's tooltip text changed
    /// while its tooltip is open; it closes and the timer starts over.
    /// </summary>
    internal void OnTooltipTextChanged(UiElement element)
    {
        if (!ReferenceEquals(TooltipOwner, element) || TooltipElement is null)
        {
            return;
        }

        DeleteTooltip(TooltipElement);
        if (_hoverStarted)
        {
            _hoverStarted = false;
            ElementLastEntered?.MouseUnhover();
        }

        CheckTooltip(_manager.Now);
    }

    /// <summary>
    /// <c>UIElement::StartTooltipAtMouse</c> (11.4186 0x00460D70, 11.6096 0x00460E50): opens <paramref name="owner"/>'s
    /// tooltip: its text (<see cref="UiElement.TooltipText"/>, else property 0x49) in the tooltip element property 0x47
    /// names, of the layout property 0x48 names (else the owner's own). A duration other than 0 replaces the default. True
    /// when a tooltip opened.
    /// </summary>
    public bool StartTooltipAtMouse(UiElement owner, double durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(owner);
        UiStringRef? text = owner.TooltipText;
        if (text is null && owner.InqProperty(UiPropertyIds.ElementTooltipEntry, out var entry) && entry is UiStringInfoProperty info)
        {
            text = info.Value;
        }

        if (text is not { } tooltipText || (!tooltipText.IsLiteral && tooltipText.StringId == 0)
            || !owner.TryGetEnumProperty(UiPropertyIds.ElementTooltipId, out var tooltipId))
        {
            return false;
        }

        var layoutId = owner.TryGetDataIdProperty(UiPropertyIds.ElementTooltipLayout, out var did) && did != 0 ? did : owner.Layout.Id;
        if (layoutId == 0)
        {
            return false;
        }

        StartTooltip(tooltipText, owner, tooltipId, layoutId);
        if (TooltipElement is null)
        {
            return false;
        }

        if (durationSeconds != 0)
        {
            // UIElementManager::SetDurationForCurrentTooltip (11.4186 0x004592C0, 11.6096 0x004593E0).
            _tooltipStart = _manager.Now - _tooltipDuration + durationSeconds;
        }

        return true;
    }

    /// <summary>
    /// <c>UIElementManager::StartTooltip</c> (11.4186 0x0045DE90, 11.6096 0x0045DF70, and the placing overload 11.4186
    /// 0x00459700, 11.6096 0x00459770): the old tooltip closes; the tooltip element is created as a root, its text element
    /// (property 0x4A) takes the text and the tooltip grows to fit it, and it opens 32 pixels below and right of the mouse,
    /// kept on the display.
    /// </summary>
    private void StartTooltip(UiStringRef text, UiElement owner, uint tooltipId, uint layoutId)
    {
        if (TooltipElement is { } old)
        {
            DeleteTooltip(old);
        }

        TooltipOwner = owner;
        var tooltip = _manager.CreateRootElementByDataId(layoutId, tooltipId);
        if (tooltip is null)
        {
            return;
        }

        if (!tooltip.TryGetEnumProperty(UiPropertyIds.ElementTooltipTextId, out var textId)
            || tooltip.GetChildRecursive(textId) is not UiTextElement label)
        {
            _manager.DeleteElement(tooltip);
            return;
        }

        label.SetStringInfo(text);
        label.RecalculateGlyphList();
        var width = tooltip.Box.Width + label.PaperWidth - label.Box.Width;
        var height = tooltip.Box.Height + Math.Max(0, label.PaperHeight - label.Box.Height);
        tooltip.ResizeTo(width, height);
        // The tooltip hangs from the root element, whose coordinates are the display's.
        var display = _manager.DisplaySize;
        var origin = _manager.DisplayOrigin;
        var x = Math.Min(Math.Max(MousePosition.X - origin.X + 32, 0), display.Width - tooltip.Box.Width);
        var y = Math.Min(Math.Max(MousePosition.Y - origin.Y + 32, 0), display.Height - tooltip.Box.Height);
        tooltip.MoveTo(x, y);
        _manager.RootElement.BringChildToTop(tooltip);
        TooltipElement = tooltip;
        _tooltipDuration = TooltipDurationSeconds;
        _tooltipStart = _manager.Now;
    }

    private void DeleteTooltip(UiElement tooltip)
    {
        TooltipElement = null;
        _manager.DeleteElement(tooltip);
    }

    private void ForgetMouse(UiElement element)
    {
        if (ReferenceEquals(ElementWithMouseCapture, element))
        {
            ElementWithMouseCapture = null;
            _captureCount = 0;
            _actionsTriggeringCapture.Clear();
        }

        if (ReferenceEquals(ElementLastEntered, element))
        {
            ElementLastEntered = null;
            _hoverStarted = false;
        }

        if (ReferenceEquals(ElementLastOver, element))
        {
            ElementLastOver = null;
        }

        if (ReferenceEquals(TooltipOwner, element))
        {
            TooltipOwner = null;
        }

        if (ReferenceEquals(TooltipElement, element))
        {
            TooltipElement = null;
        }

        _performMouseHitTest = true;
    }

    /// <summary>True for an element that is a hole for the 3D view (a viewport): the world takes the mouse there.</summary>
    private static bool IsWorldHole(UiElement element) => element.ViewportElementId != 0;
}

using Lifestone.Client.RetailUi.Input;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Core;

public partial class UiElement : IInputActionSink, IUiGlobalListener
{
    private readonly HashSet<uint> _mouseDown = [];
    private List<IUiElementListener>? _listeners;
    private IUiElementListener[] _listenerScratch = [];
    private uint _lastSerial;
    private bool _moving;
    private bool _resizing;
    private UiBorder _currentBorder;
    private (int X, int Y) _mouseInitial;
    private UiBox _dragStartBox;

    /// <summary>True while the mouse is over the element itself, not only a descendant (<c>UIRegion</c> flag bit 0; <see cref="MouseOverTop"/>).</summary>
    public bool IsMouseOverTop { get; private set; }

    /// <summary>True while the mouse is over the element or one of its descendants (<c>UIRegion</c> flag bit 4; <see cref="MouseOver"/>).</summary>
    public bool IsMouseOver { get; private set; }

    /// <summary>True while the element's window is the active one (<c>m_nFlags</c> bit 20; <see cref="Activate"/>).</summary>
    public bool IsActivated { get; private set; }

    /// <summary>
    /// True when a double click reaches the element as a double click (<see cref="UiMessageIds.DoubleClick"/>) rather than
    /// a second click (<c>m_nFlags</c> bit 23).
    /// </summary>
    /// <remarks>
    /// The 11.4186 decompile reads the flag (<c>UIElement::MouseUp</c>, <c>UIElement_Button::MouseDown</c>) but no function
    /// in it sets it. Lifestone sets it when a listener registers for double clicks of the element's id
    /// (<see cref="UiElementManager.RegisterForElementMessage"/>), as character management's row template does, or through
    /// <see cref="SetWantsDoubleClicks"/> (TODO(retailui-runtime): find retail's setter in 11.6096).
    /// </remarks>
    public bool WantsDoubleClicks { get; private set; }

    /// <summary>True when the element hears mouse moves even with no button down on it (<c>m_nFlags</c> bit 13; the colour picker).</summary>
    public bool WantsMouseMoves { get; protected set; }

    /// <summary>The descendant that takes the focus when this window root is activated (<c>m_pFocusElement</c>; <see cref="TakeFocus"/>).</summary>
    public UiElement? PendingFocusElement { get; private set; }

    /// <summary>True while the element is being dragged by the mouse (<c>m_nFlags</c> bit 18; <see cref="StartMovement"/>).</summary>
    public bool IsMoving => _moving;

    /// <summary>True while the element is being resized by the mouse (<c>m_nFlags</c> bit 19; <see cref="StartResizing"/>).</summary>
    public bool IsResizing => _resizing;

    /// <summary>The input-map registration priority the element last registered at, or null while not registered.</summary>
    public int? InputMapPriority { get; private set; }

    /// <summary>True when the mouse button <paramref name="mouseAction"/> went down on the element and has not come up (<c>UIRegion::IsMouseDown</c>).</summary>
    public bool IsMouseDown(uint mouseAction) => _mouseDown.Contains(mouseAction);

    /// <summary>Sets <see cref="WantsDoubleClicks"/>.</summary>
    public void SetWantsDoubleClicks(bool wants) => WantsDoubleClicks = wants;

    // ---------------------------------------------------------------- Element messages

    /// <summary>
    /// <c>UIElement::BroadcastElementMessage</c> (11.4186 0x00460330, 11.6096 0x00460410): sends a message about this
    /// element (<see cref="UiElementManager.BroadcastElementMessage(in UiElementMessage)"/>). True when a listener took it.
    /// </summary>
    public bool BroadcastElementMessage(uint messageId, uint param1 = 0, uint param2 = 0) =>
        Manager.BroadcastElementMessage(new UiElementMessage(this, messageId, param1, param2));

    /// <summary>
    /// <c>UIElement::RegisterForElementMessages</c> (11.4186 0x004649A0, 11.6096 0x00464AD0): <paramref name="listener"/>
    /// hears every message this element hears (its own and its descendants').
    /// </summary>
    public void RegisterForElementMessages(IUiElementListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        _listeners ??= [];
        if (!_listeners.Contains(listener))
        {
            _listeners.Add(listener);
        }
    }

    /// <summary>Stops <paramref name="listener"/> hearing this element (<c>UIElement::UnregisterForElementMessages</c>).</summary>
    public void UnregisterForElementMessages(IUiElementListener listener) => _listeners?.Remove(listener);

    /// <summary>
    /// The element as a listener of other elements (retail registers elements themselves as <c>UIListener</c>s: a
    /// scrollable on its scroll bars, a menu on its popup). It hears each message once, like the element itself.
    /// </summary>
    public IUiElementListener AsListener => field ??= new ElementListener(this);

    /// <summary>Delivers a message being broadcast to this element (the manager's dispatch; once per message).</summary>
    internal UiListenResult Receive(in UiElementMessage message)
    {
        _lastSerial = Manager.CurrentMessageSerial;
        return ListenToElementMessage(message);
    }

    /// <summary>
    /// <c>UIElement::ListenToElementMessage</c> (11.4186 0x00462340, 11.6096 0x00462420): what the element itself does
    /// with a message about it or one bubbling up from a descendant; then <see cref="ForwardElementMessage"/>. Classes
    /// override, handle their own, and call the base (or return <see cref="UiListenResult.Consumed"/>).
    /// </summary>
    /// <remarks>
    /// The base reacts to its own messages: activation puts a normal element in <see cref="UiStateIds.NormalActivated"/>
    /// and deactivation takes it back to <see cref="UiStateIds.Normal"/>; gaining the focus puts it in
    /// <see cref="UiStateIds.NormalFocussed"/> and losing it takes it back (to activated when the window is active); its
    /// visibility-toggle action shows or hides it by the toggle type (property 0x58: 1 toggles, 2 shows, 3 hides).
    /// </remarks>
    protected internal virtual UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (ReferenceEquals(message.Element, this))
        {
            switch (message.MessageId)
            {
                case UiMessageIds.Activated:
                    if (State is 0 or UiStateIds.Normal)
                    {
                        SetState(UiStateIds.NormalActivated);
                    }

                    break;
                case UiMessageIds.Deactivated:
                    if (State == UiStateIds.NormalActivated)
                    {
                        SetState(UiStateIds.Normal);
                    }

                    break;
                case UiMessageIds.Focus:
                    if (message.Param1 != 0)
                    {
                        if (State is 0 or UiStateIds.Normal or UiStateIds.NormalActivated)
                        {
                            SetState(UiStateIds.NormalFocussed);
                        }
                    }
                    else if (State == UiStateIds.NormalFocussed)
                    {
                        SetState(IsActivated ? UiStateIds.NormalActivated : UiStateIds.Normal);
                    }

                    break;
                case UiMessageIds.VisibilityToggleAction:
                    var toggle = TryGetEnumProperty(UiPropertyIds.ElementVisibilityToggleToggleType, out var type) ? type : 0;
                    switch (toggle)
                    {
                        case 1:
                            SetVisible(!Visible);
                            break;
                        case 2:
                            SetVisible(true);
                            break;
                        case 3:
                            SetVisible(false);
                            break;
                    }

                    break;
            }
        }

        return ForwardElementMessage(message, UiListenResult.Default);
    }

    /// <summary>
    /// <c>UIElement::ForwardElementMessage</c> (11.4186 0x00462B20, 11.6096 0x00462C00): the element's own listeners hear
    /// the message (each once), then it bubbles to the parent; at the top, the manager's listeners for its element id hear
    /// it when nobody took it. A <see cref="UiListenResult.Consumed"/> anywhere stops it.
    /// </summary>
    protected UiListenResult ForwardElementMessage(in UiElementMessage message, UiListenResult status)
    {
        if (status == UiListenResult.Consumed)
        {
            return status;
        }

        if (_listeners is { Count: > 0 })
        {
            var count = _listeners.Count;
            if (_listenerScratch.Length < count)
            {
                _listenerScratch = new IUiElementListener[count * 2];
            }

            _listeners.CopyTo(_listenerScratch);
            for (var i = 0; i < count; i++)
            {
                var listener = _listenerScratch[i];
                _listenerScratch[i] = null!;
                if (!Manager.MarkHeard(listener))
                {
                    continue;
                }

                var result = listener.ListenToElementMessage(message);
                if (result != UiListenResult.Default)
                {
                    status = result;
                }

                if (status == UiListenResult.Consumed)
                {
                    Array.Clear(_listenerScratch, i + 1, count - i - 1);
                    return status;
                }
            }
        }

        var parent = Parent;
        if (parent is null)
        {
            if (status == UiListenResult.Default && Manager.DefElementMessageHandler(message))
            {
                status = UiListenResult.Handled;
            }

            return status;
        }

        if (parent._lastSerial == Manager.CurrentMessageSerial)
        {
            return status;
        }

        parent._lastSerial = Manager.CurrentMessageSerial;
        var parentResult = parent.ListenToElementMessage(message);
        return parentResult != UiListenResult.Default ? parentResult : status;
    }

    /// <summary>Hears a global message the element registered for (<see cref="UiElementManager.RegisterForGlobalMessage"/>).</summary>
    protected internal virtual void ListenToGlobalMessage(uint messageId, int data)
    {
    }

    /// <inheritdoc/>
    void IUiGlobalListener.ListenToGlobalMessage(uint messageId, int data) => ListenToGlobalMessage(messageId, data);

    /// <summary>An enum property through <see cref="InqProperty"/> (<c>UIElement::GetAttribute_Enum</c>).</summary>
    public bool TryGetEnumProperty(uint id, out uint value)
    {
        if (InqProperty(id, out var property) && property is UiEnumProperty e)
        {
            value = e.Value;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>A float property through <see cref="InqProperty"/> (<c>UIElement::GetAttribute_Float</c>).</summary>
    public bool TryGetFloatProperty(uint id, out float value)
    {
        if (InqProperty(id, out var property) && property is UiFloatProperty f)
        {
            value = f.Value;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>A DID property through <see cref="InqProperty"/> (<c>UIElement::GetAttribute_DataID</c>).</summary>
    public bool TryGetDataIdProperty(uint id, out uint value)
    {
        if (InqProperty(id, out var property) && property is UiDataIdProperty d)
        {
            value = d.Value;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary><c>UIElement::SetAttribute_Bool</c> (11.4186 0x00460740, 11.6096 0x00460820): a bool instance property, applied.</summary>
    public void SetBoolProperty(uint id, bool value) => SetProperty(new UiBoolProperty(id, value));

    /// <summary><c>UIElement::SetAttribute_Float</c>: a float instance property, applied.</summary>
    public void SetFloatProperty(uint id, float value) => SetProperty(new UiFloatProperty(id, value));

    /// <summary><c>UIElement::SetAttribute_Enum</c>: an enum instance property, applied.</summary>
    public void SetEnumProperty(uint id, uint value) => SetProperty(new UiEnumProperty(id, value));

    /// <summary><c>UIElement::SetAttribute_Int</c>: an integer instance property, applied.</summary>
    public void SetIntegerProperty(uint id, int value) => SetProperty(new UiIntegerProperty(id, value));

    // ---------------------------------------------------------------- Mouse

    /// <summary>
    /// <c>UIRegion::NotifyMouseDown</c> (11.4186 0x0069FE20, 11.6096 0x006A0CA0): a mouse button went down while this
    /// element has the mouse. A button already down while the mouse is off the element ends that press instead; otherwise
    /// the element hears the press only while the mouse is over it.
    /// </summary>
    internal void NotifyMouseDown(int x, int y, uint button)
    {
        if (button is not (UiMouseActions.WheelUp or UiMouseActions.WheelDown) && _mouseDown.Contains(button))
        {
            if (!IsMouseOverTop)
            {
                MouseUp(x, y, button);
            }
        }
        else if (IsMouseOverTop)
        {
            MouseDown(x, y, button);
        }
    }

    /// <summary><c>UIRegion::NotifyMouseUp</c> (11.4186 0x0069FE80, 11.6096 0x006A0D00): a button that went down on the element came up.</summary>
    internal void NotifyMouseUp(int x, int y, uint button)
    {
        if (_mouseDown.Contains(button))
        {
            MouseUp(x, y, button);
        }
    }

    /// <summary>
    /// <c>UIElement::MouseDown</c> (11.4186 0x00460F10, 11.6096 0x00460FF0) over <c>UIRegion::MouseDown</c>: the button is
    /// remembered as down and <see cref="UiMessageIds.MouseDown"/> goes out; the context-menu button on an element with a
    /// context menu also sends <see cref="UiMessageIds.ContextMenu"/>. Coordinates are logical window pixels
    /// (<see cref="UiElementManager.DisplayOrigin"/> included), as the host reports the mouse.
    /// </summary>
    public virtual void MouseDown(int x, int y, uint button)
    {
        _mouseDown.Add(button);
        var message = MouseMessage(UiMessageIds.MouseDown, x, y, button);
        Manager.BroadcastElementMessage(message);
        if (HasContextMenu && button == UiMouseActions.Right)
        {
            Manager.BroadcastElementMessage(message with { MessageId = UiMessageIds.ContextMenu });
        }
    }

    /// <summary>
    /// <c>UIElement::MouseUp</c> (11.4186 0x00463940, 11.6096 0x00463A70): <see cref="UiMessageIds.MouseUp"/> goes out; when
    /// the button went down on the element and the mouse is still over it, a click follows: a double click
    /// (<see cref="UiMessageIds.DoubleClick"/>) to an element that wants them, else <see cref="UiMessageIds.Click"/> with
    /// the single button (a double click's 10-12 become 7-9).
    /// </summary>
    public virtual void MouseUp(int x, int y, uint button)
    {
        var wasDown = _mouseDown.Remove(button);
        var message = MouseMessage(UiMessageIds.MouseUp, x, y, button);
        Manager.BroadcastElementMessage(message);
        if (!IsMouseOverTop || !wasDown)
        {
            return;
        }

        var single = button switch
        {
            UiMouseActions.LeftDouble => UiMouseActions.Left,
            UiMouseActions.RightDouble => UiMouseActions.Right,
            UiMouseActions.MiddleDouble => UiMouseActions.Middle,
            _ => 0u,
        };
        if (single != 0 && WantsDoubleClicks)
        {
            Manager.BroadcastElementMessage(message with { MessageId = UiMessageIds.DoubleClick });
        }
        else
        {
            Manager.BroadcastElementMessage(message with { MessageId = UiMessageIds.Click, Param1 = single != 0 ? single : button });
        }
    }

    /// <summary>
    /// <c>UIElement::MouseMove</c> (11.4186 0x00463310, 11.6096 0x004633F0): a window being resized or moved follows the
    /// mouse; <see cref="UiMessageIds.MouseMove"/> goes out while a button is down on the element (or it wants every move).
    /// </summary>
    public virtual void MouseMove(int x, int y)
    {
        if (_resizing)
        {
            MouseResizeElement(x, y);
        }
        else if (_moving)
        {
            MouseMoveElement(x, y);
        }

        if (_mouseDown.Count != 0 || WantsMouseMoves)
        {
            Manager.BroadcastElementMessage(MouseMessage(UiMessageIds.MouseMove, x, y, 0));
        }
    }

    /// <summary><c>UIElement::MouseTap</c> (11.4186 0x00460FE0, 11.6096 0x004610C0): <see cref="UiMessageIds.Tap"/>.</summary>
    public virtual void MouseTap(int x, int y, uint button) =>
        Manager.BroadcastElementMessage(MouseMessage(UiMessageIds.Tap, x, y, button));

    /// <summary>
    /// <c>UIElement::MouseOverTop</c> (11.4186 0x004615D0, 11.6096 0x004616B0): the mouse entered or left the element
    /// itself; <see cref="UiMessageIds.MouseOverTop"/> goes out.
    /// </summary>
    public virtual void MouseOverTop(bool overTop)
    {
        IsMouseOverTop = overTop;
        BroadcastElementMessage(UiMessageIds.MouseOverTop, overTop ? 1u : 0u);
    }

    /// <summary>
    /// <c>UIElement::MouseOver</c> (11.4186 0x004615A0, 11.6096 0x00461680): the mouse entered or left the element's
    /// area (itself or a descendant); <see cref="UiMessageIds.MouseOver"/> goes out.
    /// </summary>
    public virtual void MouseOver(bool over)
    {
        IsMouseOver = over;
        BroadcastElementMessage(UiMessageIds.MouseOver, over ? 1u : 0u);
    }

    /// <summary>
    /// <c>UIElement::MouseHover</c> (11.4186 0x00462520, 11.6096 0x00462600): the mouse rested on the element. With tooltips
    /// on (and enabled), its tooltip opens (<see cref="UiInputRouter.StartTooltip"/>); when the mouse is over it,
    /// <see cref="UiMessageIds.MouseHover"/> goes out. True when a tooltip opened.
    /// </summary>
    public virtual bool MouseHover(int x, int y)
    {
        var started = TooltipsOn && Manager.Input.TooltipsEnabled && Manager.Input.StartTooltipAtMouse(this, 0);
        if (IsMouseOver)
        {
            Manager.BroadcastElementMessage(MouseMessage(UiMessageIds.MouseHover, x, y, 0));
        }

        return started;
    }

    /// <summary>The mouse stopped resting on the element (the manager's <c>StopHover</c>).</summary>
    public virtual void MouseUnhover()
    {
    }

    private UiElementMessage MouseMessage(uint id, int x, int y, uint button)
    {
        var screen = ScreenBox;
        return new UiElementMessage(this, id, button, 0)
        {
            WindowPoint = (x, y),
            ElementPoint = (x - screen.X0, y - screen.Y0),
        };
    }

    // ---------------------------------------------------------------- Keys and input actions

    /// <summary>
    /// <c>UIElement::KeyDown</c> (11.4186 0x004610C0, 11.6096 0x004611A0): a key action reached the focused or active element;
    /// <see cref="UiMessageIds.KeyDown"/> goes out. True when a listener took it.
    /// </summary>
    public virtual bool KeyDown(uint action, float extent) =>
        Manager.BroadcastElementMessage(new UiElementMessage(this, UiMessageIds.KeyDown, action, (uint)(extent * 1000.0)));

    /// <summary><c>UIElement::KeyUp</c> (11.4186 0x00461060, 11.6096 0x00461140): <see cref="UiMessageIds.KeyUp"/>.</summary>
    public virtual bool KeyUp(uint action) =>
        Manager.BroadcastElementMessage(new UiElementMessage(this, UiMessageIds.KeyUp, action, 0));

    /// <summary>
    /// <c>UIElement::OnAction</c> (11.4186 0x0045FBC0, 11.6096 0x0045FCA0): an input action of a map the element registered;
    /// the base offers it to the parent (<see cref="OnChildAction"/>). True when taken.
    /// </summary>
    protected internal virtual bool OnAction(in UiInputAction action) => Parent?.OnChildAction(this, action) ?? false;

    /// <summary><c>UIElement::OnChildAction</c> (11.4186 0x0045FC00, 11.6096 0x0045FCE0): a descendant's action; the base passes it up.</summary>
    protected internal virtual bool OnChildAction(UiElement child, in UiInputAction action) => Parent?.OnChildAction(child, action) ?? false;

    /// <inheritdoc/>
    bool IInputActionSink.OnInputAction(uint inputMapId, uint actionId, RetailActivation activation) =>
        OnAction(new UiInputAction(inputMapId, actionId, activation));

    /// <summary>
    /// <c>UIElement::RegisterInputMaps</c> (11.4186 0x0045FB00, 11.6096 0x0045FBE0): the element's ancestors register at one
    /// less priority each, then the element registers its own input map (property 0x4E) at <paramref name="priority"/>.
    /// The focused element registers at 3000, the active window at 2000.
    /// </summary>
    protected internal virtual void RegisterInputMaps(int priority)
    {
        Parent?.RegisterInputMaps(priority - 1);
        InputMapPriority = priority;
        if (InputMapId != 0)
        {
            Manager.Input.RegisterInputMap(InputMapId, this, priority);
        }
    }

    /// <summary><c>UIElement::UnregisterInputMaps</c> (11.4186 0x0045FB70, 11.6096 0x0045FC50): the reverse, ancestors included.</summary>
    protected internal virtual void UnregisterInputMaps()
    {
        Parent?.UnregisterInputMaps();
        InputMapPriority = null;
        if (InputMapId != 0)
        {
            Manager.Input.UnregisterInputMap(InputMapId, this);
        }
    }

    // ---------------------------------------------------------------- Activation and focus

    /// <summary>
    /// <c>UIElement::GetRootElement</c> (11.4186 0x0045FA50, 11.6096 0x0045FB30): the window this element belongs to: itself
    /// or the nearest ancestor that is a root (<see cref="IsRoot"/>), or null.
    /// </summary>
    public UiElement? GetWindowRoot()
    {
        for (var e = this; e is not null; e = e.Parent)
        {
            if (e.IsRoot)
            {
                return e;
            }
        }

        return null;
    }

    /// <summary>
    /// <c>UIElement::Activate</c> (11.4186 0x00461C80, 11.6096 0x00461D60): a shown window root becomes the active window
    /// (<see cref="UiElementManager.ActivationAlert"/>), registers its input maps (at 0, then at 2000) and, when it was
    /// not active before, sends <see cref="UiMessageIds.Activated"/> and gives the focus to its pending focus element. Another
    /// element activates its window. True when something was activated.
    /// </summary>
    public virtual bool Activate()
    {
        var wasActive = IsActivated;
        if (!IsShown)
        {
            return false;
        }

        if (!IsRoot)
        {
            return GetWindowRoot()?.Activate() ?? false;
        }

        // Retail registers the window's maps at priority 0 here, and again at 2000 in the activation alert; both entries
        // stay (CInputManager::RegisterInputMap refuses only an identical map, callback and priority).
        IsActivated = true;
        RegisterInputMaps(0);
        Manager.ActivationAlert(this, true);
        Manager.RegisterActivatable(this);
        if (!wasActive)
        {
            BroadcastElementMessage(UiMessageIds.Activated);
            if (PendingFocusElement is { } focus)
            {
                Manager.SetFocusElement(focus);
            }
        }

        return true;
    }

    /// <summary>
    /// <c>UIElement::Deactivate</c> (11.4186 0x00461D50, 11.6096 0x00461E30): the active window root stops being active,
    /// unregisters its maps and, when it was active, sends <see cref="UiMessageIds.Deactivated"/> and drops the focus.
    /// </summary>
    public virtual bool Deactivate()
    {
        if (!IsRoot)
        {
            return GetWindowRoot()?.Deactivate() ?? false;
        }

        var wasActive = IsActivated;
        IsActivated = false;
        UnregisterInputMaps();
        Manager.ActivationAlert(this, false);
        if (wasActive)
        {
            BroadcastElementMessage(UiMessageIds.Deactivated);
            if (PendingFocusElement is not null)
            {
                Manager.SetFocusElement(null);
            }
        }

        return true;
    }

    /// <summary>
    /// <c>UIElement::TakeFocus</c> (11.4186 0x004601E0, 11.6096 0x004602C0): the element becomes its window's focus
    /// element, and the keyboard focus at once when the window is active. True when it got the focus now.
    /// </summary>
    public bool TakeFocus()
    {
        var root = GetWindowRoot();
        if (root is null)
        {
            return false;
        }

        root.PendingFocusElement = this;
        if (!root.IsActivated)
        {
            return false;
        }

        Manager.SetFocusElement(this);
        return true;
    }

    /// <summary><c>UIElement::RelinquishFocus</c> (11.4186 0x00461DE0, 11.6096 0x00461EC0): the element gives the focus up.</summary>
    public bool RelinquishFocus()
    {
        var root = GetWindowRoot();
        if (root is null)
        {
            return true;
        }

        root.PendingFocusElement = null;
        if (ReferenceEquals(Manager.FocusElement, this))
        {
            Manager.SetFocusElement(null);
        }

        return true;
    }

    /// <summary><c>UIElement::GetHasFocus</c> (11.4186 0x00460230, 11.6096 0x00460310).</summary>
    public bool HasKeyboardFocus => ReferenceEquals(Manager.FocusElement, this);

    // ---------------------------------------------------------------- Moving and resizing by the mouse

    /// <summary>
    /// <c>UIElement::StartMovement</c> (11.4186 0x0045FDB0, 11.6096 0x0045FE90): the element follows the mouse from
    /// (<paramref name="mouseX"/>, <paramref name="mouseY"/>) until <see cref="StopMovement"/>.
    /// </summary>
    public void StartMovement(int mouseX, int mouseY)
    {
        if (_moving || _resizing)
        {
            return;
        }

        _mouseInitial = (mouseX, mouseY);
        _dragStartBox = Box;
        _moving = true;
    }

    /// <summary><c>UIElement::StopMovement</c> (11.4186 0x0045FE20, 11.6096 0x0045FF00).</summary>
    public void StopMovement() => _moving = false;

    /// <summary>
    /// <c>UIElement::StartResizing</c> (11.4186 0x0045FCA0, 11.6096 0x0045FD80): the element's <paramref name="border"/> follows
    /// the mouse until <see cref="StopResizing"/>.
    /// </summary>
    public void StartResizing(UiBorder border, int mouseX, int mouseY)
    {
        if (_moving || _resizing || border == UiBorder.None)
        {
            return;
        }

        _mouseInitial = (mouseX, mouseY);
        _dragStartBox = Box;
        _currentBorder = border;
        _resizing = true;
    }

    /// <summary><c>UIElement::StopResizing</c> (11.4186 0x0045FD60, 11.6096 0x0045FE40).</summary>
    public void StopResizing() => _resizing = false;

    /// <summary>
    /// <c>UIElement::MouseMoveElement</c> (11.4186 0x0045FE30, 11.6096 0x0045FF10): the element moves with the mouse, snapped
    /// to 10 pixels while Shift is down (vertically only while it is not at the parent's bottom), and kept inside its
    /// parent.
    /// </summary>
    private void MouseMoveElement(int x, int y)
    {
        var newX = x + _dragStartBox.X0 - _mouseInitial.X;
        var newY = y + _dragStartBox.Y0 - _mouseInitial.Y;
        var parentWidth = Parent?.Box.Width ?? Manager.DisplaySize.Width;
        var parentHeight = Parent?.Box.Height ?? Manager.DisplaySize.Height;
        if (Manager.ShiftKeyDown)
        {
            newX -= newX % 10;
            if (newY < parentHeight - Box.Height)
            {
                newY -= newY % 10;
            }
        }

        newX = Math.Min(Math.Max(newX, 0), parentWidth - Box.Width);
        newY = Math.Min(Math.Max(newY, 0), parentHeight - Box.Height);
        MoveTo(newX, newY);
    }

    /// <summary>
    /// <c>UIElement::MouseResizeElement</c> (11.4186 0x00461130, 11.6096 0x00461210): the dragged border follows the mouse,
    /// the size kept within the minimum and maximum properties (0x3C-0x3F), snapped to 10 pixels with Shift, inside the
    /// parent.
    /// </summary>
    /// <remarks>
    /// The 11.4186 decompile of this function is damaged for the top and right borders (the decompiler lost their cases);
    /// Lifestone treats every border the same way as the legible left and bottom ones, and logs a warning the first time
    /// one of those borders is dragged (TODO(retailui-runtime): confirm the top and right borders against 11.6096
    /// 0x00461210).
    /// </remarks>
    private void MouseResizeElement(int x, int y)
    {
        var dx = x - _mouseInitial.X;
        var dy = y - _mouseInitial.Y;
        var left = _dragStartBox.X0;
        var top = _dragStartBox.Y0;
        var right = _dragStartBox.X0 + _dragStartBox.Width;
        var bottom = _dragStartBox.Y0 + _dragStartBox.Height;
        var hasMinWidth = TryGetIntegerProperty(UiPropertyIds.ElementMinWidth, out var minWidth);
        var hasMaxWidth = TryGetIntegerProperty(UiPropertyIds.ElementMaxWidth, out var maxWidth);
        var hasMinHeight = TryGetIntegerProperty(UiPropertyIds.ElementMinHeight, out var minHeight);
        var hasMaxHeight = TryGetIntegerProperty(UiPropertyIds.ElementMaxHeight, out var maxHeight);
        var border = _currentBorder;
        if (border is not (UiBorder.Left or UiBorder.Bottom or UiBorder.BottomLeft))
        {
            Manager.LogOnce("resize:top-right", Diagnostics.LogSeverity.Warning,
                "TODO(retailui-runtime): a window is resized from its top or right border; those cases of UIElement::MouseResizeElement are garbled in the 11.4186 decompile and follow the left and bottom ones (confirm against 11.6096 0x00461210)");
        }

        if (border is UiBorder.TopLeft or UiBorder.Left or UiBorder.BottomLeft)
        {
            left = Clamp(left + dx, right, hasMinWidth, minWidth, hasMaxWidth, maxWidth, fromNear: true);
        }

        if (border is UiBorder.TopRight or UiBorder.Right or UiBorder.BottomRight)
        {
            right = Clamp(right + dx, left, hasMinWidth, minWidth, hasMaxWidth, maxWidth, fromNear: false);
        }

        if (border is UiBorder.TopLeft or UiBorder.Top or UiBorder.TopRight)
        {
            top = Clamp(top + dy, bottom, hasMinHeight, minHeight, hasMaxHeight, maxHeight, fromNear: true);
        }

        if (border is UiBorder.BottomLeft or UiBorder.Bottom or UiBorder.BottomRight)
        {
            bottom = Clamp(bottom + dy, top, hasMinHeight, minHeight, hasMaxHeight, maxHeight, fromNear: false);
        }

        if (Manager.ShiftKeyDown)
        {
            left -= left % 10;
            bottom -= bottom % 10;
        }

        var parentWidth = Parent?.Box.Width ?? Manager.DisplaySize.Width;
        var parentHeight = Parent?.Box.Height ?? Manager.DisplaySize.Height;
        left = Math.Max(left, 0);
        top = Math.Max(top, 0);
        right = Math.Min(right, parentWidth);
        bottom = Math.Min(bottom, parentHeight);
        MoveTo(left, top);
        ResizeTo(right - left, bottom - top);
    }

    private static int Clamp(int edge, int opposite, bool hasMin, int min, bool hasMax, int max, bool fromNear)
    {
        var size = fromNear ? opposite - edge : edge - opposite;
        if (hasMin && size < min)
        {
            size = min;
        }

        if (hasMax && size > max)
        {
            size = max;
        }

        return fromNear ? opposite - size : opposite + size;
    }

    // ---------------------------------------------------------------- Relatives

    /// <summary>
    /// <c>UIElement::FindRelative</c> (11.4186 0x00464560, 11.6096 0x00464690): the element <paramref name="elementId"/>
    /// nearest to this one: a descendant first, then, going up to the window root, each ancestor's other children and
    /// their subtrees, or an ancestor itself with that id. Null when there is none.
    /// </summary>
    public UiElement? FindRelative(uint elementId)
    {
        var found = GetChildRecursive(elementId);
        if (found is not null)
        {
            return found;
        }

        var from = this;
        var parent = Parent;
        while (from.ElementId != elementId)
        {
            if (parent is null)
            {
                return null;
            }

            foreach (var sibling in parent.Children)
            {
                if (sibling.ElementId == elementId)
                {
                    return sibling;
                }

                if (!ReferenceEquals(sibling, from) && sibling.GetChildRecursive(elementId) is { } deep)
                {
                    return deep;
                }
            }

            from = parent;
            parent = parent.IsRoot ? null : parent.Parent;
        }

        return from;
    }

    /// <summary><c>UIElement::CenterAt</c> (11.4186 0x0045FFF0, 11.6096 0x004600D0): moves the element so its centre is at (x, y) of its parent.</summary>
    public void CenterAt(int x, int y) => MoveTo(x - (Box.Width / 2), y - (Box.Height / 2));

    private sealed class ElementListener(UiElement owner) : IUiElementListener
    {
        public UiListenResult ListenToElementMessage(in UiElementMessage message)
        {
            if (owner._lastSerial == owner.Manager.CurrentMessageSerial)
            {
                return UiListenResult.Default;
            }

            owner._lastSerial = owner.Manager.CurrentMessageSerial;
            return owner.ListenToElementMessage(message);
        }
    }

    /// <summary>Clears the element's mouse state when it leaves the tree (the manager's delete path).</summary>
    internal void ResetInputState()
    {
        _mouseDown.Clear();
        IsMouseOverTop = false;
        IsMouseOver = false;
        _moving = false;
        _resizing = false;
    }
}

/// <summary>Which border of a window the mouse resizes (retail's <c>BorderLocation</c>, clockwise from the top-left corner).</summary>
public enum UiBorder
{
    /// <summary>0: none.</summary>
    None = 0,

    /// <summary>1: the top-left corner.</summary>
    TopLeft = 1,

    /// <summary>2: the top.</summary>
    Top = 2,

    /// <summary>3: the top-right corner.</summary>
    TopRight = 3,

    /// <summary>4: the right.</summary>
    Right = 4,

    /// <summary>5: the bottom-right corner.</summary>
    BottomRight = 5,

    /// <summary>6: the bottom.</summary>
    Bottom = 6,

    /// <summary>7: the bottom-left corner.</summary>
    BottomLeft = 7,

    /// <summary>8: the left.</summary>
    Left = 8,
}

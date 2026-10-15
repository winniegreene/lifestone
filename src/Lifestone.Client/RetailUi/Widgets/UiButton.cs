using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Widgets;

/// <summary>
/// The Button (class 1, retail's <c>UIElement_Button</c>, a Text with a label): it takes its look from its state, which
/// follows its properties and the mouse, and announces clicks (<see cref="UiMessageIds.ButtonClick"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>State</b> (<c>UIElement_Button::UpdateState_</c>, 11.4186 0x00471CF0, 11.6096 0x004720B0): ghosted (property 0x0D)
/// is <see cref="UiStateIds.Ghosted"/>; otherwise Normal (1), or Highlight (6) when property 0x0E is set; while pressed, or
/// with rollover (0x13) and the mouse over it, the rollover state (2 or 7); pressed with the mouse over it, the pressed
/// state (3 or 8). A state the desc does not have is not entered.
/// </para>
/// <para>
/// <b>Clicks</b> (<c>UIElement_Button::MouseDown</c>, 11.4186 0x00471FF0, 11.6096 0x004723B0, and <c>MouseUp</c>, 11.4186
/// 0x004720E0, 11.6096 0x004724A0): the left button (or a double click, to a button that does not want double clicks)
/// presses it; releasing it over the button clicks it, unless it is ghosted or a hot button repeated. A ghosted button
/// never clicks on release: <c>MouseUp</c> tests property 0x0C (broadcast when ghosted) only inside its not-ghosted
/// branch, where it changes nothing. Property 0x0C only lets a ghosted hot button send its hot clicks
/// (<c>UIElement_Button::MouseDown</c>). A toggle button (0x0B) flips its highlight on each click. A hot button (0x0F)
/// sends <see cref="UiMessageIds.ButtonHotClick"/> on the press and then every interval (0x10 first, 0x11 after) while it
/// is held over it (<c>UIElement_Button::ListenToGlobalMessage</c>, 11.4186 0x00471C60, 11.6096 0x00472020). A click of a
/// button with an input action (0x12) fires that action (<c>UIElement_Button::HandleButtonClick</c>, 11.4186 0x00471E50,
/// 11.6096 0x00472210) and goes no further; a ghosted button's click goes no further either.
/// </para>
/// <para>
/// <b>Mouse visibility.</b> A button takes the mouse unless it is ghosted. The 11.4186 symbols have no
/// <c>UIElement_Button::GetShouldBeMouseVisible</c>, but ghosting re-evaluates the button's mouse visibility, and the
/// shared function list boxes and colour pickers use, <c>UIElement_ListBox::GetShouldBeMouseVisible</c> (11.4186
/// 0x0046D590, 11.6096 0x0046B300: mouse-visible unless ghosted), is the rule that makes that re-evaluation meaningful;
/// Lifestone gives buttons the same rule (TODO(retailui-runtime): confirm the button's virtual in the 11.6096 vtable).
/// </para>
/// </remarks>
public class UiButton : UiTextElement
{
    private bool _mousePressedOnButton;
    private bool _hotClickingInProgress;
    private double _nextHotClickTime;

    /// <summary><c>UIElement_Button::UIElement_Button</c> (11.4186 0x00471B50, 11.6096 0x00471F10).</summary>
    public UiButton(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>True while the left button is held on the button (<c>mousePressedOnButton</c>).</summary>
    public bool IsPressed => _mousePressedOnButton;

    /// <summary>True while a hot button repeats (<c>hotClickingInProgress</c>).</summary>
    public bool IsHotClicking => _hotClickingInProgress;

    /// <summary>Property 0x0D.</summary>
    public bool Ghosted => TryGetBoolProperty(UiPropertyIds.ButtonGhosted, out var g) && g;

    /// <summary>Property 0x0E.</summary>
    public bool Highlighted => TryGetBoolProperty(UiPropertyIds.ButtonHighlighted, out var h) && h;

    /// <summary>
    /// <c>UIElement_Button::SetState</c> (11.4186 0x00471DA0, 11.6096 0x00472160): on a button with property 0x0B (present,
    /// whatever its value, as retail reads it), entering Highlight sets the highlight and Normal clears it; entering or
    /// leaving Ghosted sets or clears property 0x0D; anything else is an ordinary state change.
    /// </summary>
    public override bool SetState(uint state)
    {
        if (InqProperty(UiPropertyIds.ButtonBooleanButton, out _))
        {
            var highlighted = Highlighted;
            if (state == UiStateIds.Highlight)
            {
                if (!highlighted)
                {
                    SetBoolProperty(UiPropertyIds.ButtonHighlighted, true);
                    return true;
                }
            }
            else if (state == UiStateIds.Normal && highlighted)
            {
                SetBoolProperty(UiPropertyIds.ButtonHighlighted, false);
                return true;
            }
        }

        var ghosting = state == UiStateIds.Ghosted;
        if (ghosting == Ghosted)
        {
            return base.SetState(state);
        }

        SetBoolProperty(UiPropertyIds.ButtonGhosted, ghosting);
        return true;
    }

    /// <summary>A button takes the mouse unless it is ghosted (see the remarks).</summary>
    protected override bool GetShouldBeMouseVisible() => base.GetShouldBeMouseVisible() || State != UiStateIds.Ghosted;

    /// <summary><c>UIElement_Button::PostInit</c> (11.4186 0x00471F30, 11.6096 0x004722F0): the scrollable's, then the state from the properties.</summary>
    protected internal override void PostInit()
    {
        base.PostInit();
        UpdateState();
    }

    /// <summary><c>UIElement_Button::OnSetAttribute</c> (11.4186 0x00471F40, 11.6096 0x00472300).</summary>
    protected internal override void OnSetAttribute(uint id, UiProperty? value)
    {
        base.OnSetAttribute(id, value);
        switch (id)
        {
            case UiPropertyIds.ButtonGhosted:
                UpdateState();
                SetMouseVisible(GetShouldBeMouseVisible());
                break;
            case UiPropertyIds.ButtonHighlighted:
            case UiPropertyIds.ButtonRollover:
                UpdateState();
                break;
            case UiPropertyIds.ButtonHotButton:
                if (_hotClickingInProgress && !GetBool(value))
                {
                    StopHotClicking();
                }

                break;
        }
    }

    /// <summary><c>UIElement_Button::UpdateState_</c>: the state the properties and the mouse call for (see the remarks).</summary>
    protected void UpdateState()
    {
        uint state;
        if (Ghosted)
        {
            state = UiStateIds.Ghosted;
        }
        else
        {
            var highlighted = Highlighted;
            var rollover = TryGetBoolProperty(UiPropertyIds.ButtonRollover, out var r) && r;
            state = highlighted ? UiStateIds.Highlight : UiStateIds.Normal;
            if (_mousePressedOnButton || (rollover && IsMouseOverTop))
            {
                state = highlighted ? UiStateIds.HighlightRollover : UiStateIds.NormalRollover;
            }

            if (_mousePressedOnButton && IsMouseOverTop)
            {
                state = highlighted ? UiStateIds.HighlightPressed : UiStateIds.NormalPressed;
            }
        }

        if (Desc.States.ContainsKey(state))
        {
            SetState(state);
        }
    }

    /// <inheritdoc/>
    public override void MouseDown(int x, int y, uint button)
    {
        base.MouseDown(x, y, button);
        var presses = (!WantsDoubleClicks && button == UiMouseActions.LeftDouble) || button == UiMouseActions.Left;
        if (!presses || IsResizing || IsMoving)
        {
            return;
        }

        _mousePressedOnButton = true;
        UpdateState();
        var broadcastWhenGhosted = TryGetBoolProperty(UiPropertyIds.ButtonBroadcastWhenGhosted, out var b) && b;
        if ((!Ghosted || broadcastWhenGhosted) && TryGetBoolProperty(UiPropertyIds.ButtonHotButton, out var hot) && hot)
        {
            BroadcastElementMessage(UiMessageIds.ButtonHotClick, button);
            _hotClickingInProgress = true;
            Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
            var first = TryGetFloatProperty(UiPropertyIds.ButtonHotClickFirstInterval, out var f) ? f : 0f;
            _nextHotClickTime = first + Manager.Now;
        }
    }

    /// <inheritdoc/>
    public override void MouseUp(int x, int y, uint button)
    {
        base.MouseUp(x, y, button);
        var ghosted = Ghosted;
        var broadcastWhenGhosted = TryGetBoolProperty(UiPropertyIds.ButtonBroadcastWhenGhosted, out var b) && b;
        if (!_mousePressedOnButton || button is not (UiMouseActions.Left or UiMouseActions.LeftDouble))
        {
            return;
        }

        var click = false;
        if (IsMouseOverTop && !ghosted)
        {
            if (TryGetBoolProperty(UiPropertyIds.ButtonBooleanButton, out var toggle) && toggle)
            {
                SetBoolProperty(UiPropertyIds.ButtonHighlighted, !Highlighted);
            }

            click = (!ghosted || broadcastWhenGhosted) && !_hotClickingInProgress;
        }

        _mousePressedOnButton = false;
        if (_hotClickingInProgress)
        {
            StopHotClicking();
        }

        UpdateState();
        if (click)
        {
            BroadcastElementMessage(UiMessageIds.ButtonClick, UiMouseActions.Left);
        }
    }

    /// <summary><c>UIElement_Button::MouseOverTop</c> (11.4186 0x004721F0, 11.6096 0x004725B0): the state follows the mouse.</summary>
    public override void MouseOverTop(bool overTop)
    {
        base.MouseOverTop(overTop);
        UpdateState();
    }

    /// <inheritdoc/>
    protected internal override void ListenToGlobalMessage(uint messageId, int data)
    {
        base.ListenToGlobalMessage(messageId, data);
        if (messageId != UiGlobalMessageIds.Frame || !_hotClickingInProgress)
        {
            return;
        }

        var now = Manager.Now;
        if (!IsMouseOverTop)
        {
            // Off the button the repeat pauses; it resumes from the moment the mouse is back.
            if (now > _nextHotClickTime)
            {
                _nextHotClickTime = now;
            }

            return;
        }

        if (now >= _nextHotClickTime)
        {
            BroadcastElementMessage(UiMessageIds.ButtonHotClick);
            var interval = TryGetFloatProperty(UiPropertyIds.ButtonHotClickInterval, out var i) ? i : 0f;
            _nextHotClickTime += interval;
        }
    }

    /// <summary><c>UIElement_Button::ListenToElementMessage</c> (11.4186 0x00472210, 11.6096 0x004725D0).</summary>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (ReferenceEquals(message.Element, this) && message.MessageId == UiMessageIds.ButtonClick && (Ghosted || HandleButtonClick()))
        {
            return UiListenResult.Consumed;
        }

        return base.ListenToElementMessage(message);
    }

    /// <summary>
    /// <c>UIElement_Button::HandleButtonClick</c>: a button with an input action (property 0x12) other than 1 fires it as
    /// an Impulse start (<see cref="Input.UiInputRouter.FireAction"/>). True when it did.
    /// </summary>
    private bool HandleButtonClick()
    {
        if (!TryGetEnumProperty(UiPropertyIds.ButtonClickAction, out var action) || action == 1)
        {
            return false;
        }

        Manager.Input.FireAction(0, action);
        return true;
    }

    private void StopHotClicking()
    {
        _hotClickingInProgress = false;
        Manager.UnregisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
    }
}

namespace Lifestone.Client.RetailUi.Core;

/// <summary>
/// How a listener took an element message (retail's <c>UIElementMessageListenResult</c>): <see cref="Default"/> lets it go
/// on unchanged, <see cref="Handled"/> marks it handled and lets it go on, <see cref="Consumed"/> stops it.
/// </summary>
public enum UiListenResult
{
    /// <summary>0 (<c>lrDefault</c>): not taken; the message goes on.</summary>
    Default = 0,

    /// <summary>1: taken; the message goes on to the others.</summary>
    Handled = 1,

    /// <summary>2: taken; nobody else hears it.</summary>
    Consumed = 2,
}

/// <summary>
/// Something that hears element messages (retail's <c>UIListener</c>): registered on one element
/// (<see cref="UiElement.RegisterForElementMessages"/>), it hears every message that element hears, its descendants'
/// included as they bubble up; registered with the manager by element id (<see cref="UiElementManager.RegisterForElementMessage"/>),
/// it hears the messages of that id that no ancestor consumed. The screens (G2-G4) are listeners on their roots.
/// </summary>
public interface IUiElementListener
{
    /// <summary>Hears <paramref name="message"/> (<c>UIListener::ListenToElementMessage</c>).</summary>
    UiListenResult ListenToElementMessage(in UiElementMessage message);
}

/// <summary>Something that hears global messages (<c>UIListener::ListenToGlobalMessage</c>; <see cref="UiGlobalMessageIds"/>).</summary>
public interface IUiGlobalListener
{
    /// <summary>Hears global message <paramref name="messageId"/> with its value.</summary>
    void ListenToGlobalMessage(uint messageId, int data);
}

/// <summary>
/// The element message ids the engine sends (<c>UIElement::BroadcastElementMessage</c> callers in 11.4186); the screens
/// switch on these. Message parameters are noted per id.
/// </summary>
public static class UiMessageIds
{
    /// <summary>1: a button was clicked (<c>UIElement_Button::MouseUp</c>; param1 7).</summary>
    public const uint ButtonClick = 1;

    /// <summary>2: a hot button repeats while held (<c>UIElement_Button::ListenToGlobalMessage</c>; param1 the button on the first).</summary>
    public const uint ButtonHotClick = 2;

    /// <summary>4: a list box's selection changed (<c>UIElement_ListBox::SetSelectedItem</c>; param1 the index or 0xFFFFFFFF, <see cref="UiElementMessage.Item"/> the item).</summary>
    public const uint ListBoxSelection = 4;

    /// <summary>7: a menu's selection changed (<c>UIElement_Menu::NewSelection</c>; param1 the item's id, <see cref="UiElementMessage.Item"/> the item).</summary>
    public const uint MenuSelection = 7;

    /// <summary>8: a menu opened (<c>UIElement_Menu::Open</c>).</summary>
    public const uint MenuOpened = 8;

    /// <summary>9: a menu closed (<c>UIElement_Menu::Close</c>).</summary>
    public const uint MenuClosed = 9;

    /// <summary>10: a scroll bar's position changed (param1 unused; read property 0x86).</summary>
    public const uint ScrollbarPosition = 10;

    /// <summary>13: a scroll bar asks its scrollable to move one line toward the start.</summary>
    public const uint ScrollLineUp = 13;

    /// <summary>14: one line toward the end.</summary>
    public const uint ScrollLineDown = 14;

    /// <summary>15: one page toward the start.</summary>
    public const uint ScrollPageUp = 15;

    /// <summary>16: one page toward the end.</summary>
    public const uint ScrollPageDown = 16;

    /// <summary>0x12: a character was typed into a text element (<c>UIElement_Text::CharacterHandler</c>; param1 the character).</summary>
    public const uint CharacterTyped = 0x12;

    /// <summary>0x18: an element's own visibility flag changed (<c>UIElement::SetVisible</c>; param1 1 when shown).</summary>
    public const uint VisibilityChanged = 0x18;

    /// <summary>0x19: a click (<c>UIElement::MouseUp</c>; param1 the button, 7 left, 8 right, 9 middle).</summary>
    public const uint Click = 0x19;

    /// <summary>0x1A: a double click, to an element that asked for them (param1 10, 11 or 12).</summary>
    public const uint DoubleClick = 0x1A;

    /// <summary>0x1B: the mouse entered (param1 1) or left (0) the element itself (<c>UIElement::MouseOverTop</c>).</summary>
    public const uint MouseOverTop = 0x1B;

    /// <summary>0x1C: a mouse button went down on the element (<c>UIElement::MouseDown</c>; param1 the mouse action).</summary>
    public const uint MouseDown = 0x1C;

    /// <summary>0x1D: a mouse button came up (<c>UIElement::MouseUp</c>; param1 the mouse action).</summary>
    public const uint MouseUp = 0x1D;

    /// <summary>0x1E: the mouse moved while a button is down on the element (<c>UIElement::MouseMove</c>).</summary>
    public const uint MouseMove = 0x1E;

    /// <summary>0x1F: the mouse entered (1) or left (0) the element or a descendant (<c>UIElement::MouseOver</c>).</summary>
    public const uint MouseOver = 0x1F;

    /// <summary>0x20: the mouse rested on the element (<c>UIElement::MouseHover</c>).</summary>
    public const uint MouseHover = 0x20;

    /// <summary>0x24: a dialog asks to be laid out again (<c>Dialog::ListenToElementMessage</c>).</summary>
    public const uint DialogRelayout = 0x24;

    /// <summary>0x27: the context-menu button went down on an element with a context menu (<c>UIElement::MouseDown</c>).</summary>
    public const uint ContextMenu = 0x27;

    /// <summary>0x29: the element's window was activated (<c>UIElement::Activate</c>).</summary>
    public const uint Activated = 0x29;

    /// <summary>0x2A: it was deactivated (<c>UIElement::Deactivate</c>).</summary>
    public const uint Deactivated = 0x2A;

    /// <summary>0x2C: a panel's open tab changed (<c>UIElement_Panel::Update</c>).</summary>
    public const uint PanelTabChanged = 0x2C;

    /// <summary>0x2D: a meter's position changed (<c>UIElement_Meter::OnSetAttribute</c>).</summary>
    public const uint MeterPosition = 0x2D;

    /// <summary>0x2E: a meter reached its goal.</summary>
    public const uint MeterAtGoal = 0x2E;

    /// <summary>0x2F: the element gained (param1 1) or lost (0) the keyboard focus (<c>UIElementManager::SetFocusElement</c>).</summary>
    public const uint Focus = 0x2F;

    /// <summary>0x30: a colour picker's selection changed (param1 the pixel index).</summary>
    public const uint ColorPicked = 0x30;

    /// <summary>0x31: the element's visibility-toggle action fired (<c>UIElementManager::DoVisibilityToggleAction</c>; param1 the action).</summary>
    public const uint VisibilityToggleAction = 0x31;

    /// <summary>0x32: a scrollable's paper changed size (<c>UIElement_Scrollable::ResizeScrollableArea</c>; param1/param2 the old width and height).</summary>
    public const uint PaperResized = 0x32;

    /// <summary>0x40: a tap (<c>UIElement::MouseTap</c>).</summary>
    public const uint Tap = 0x40;

    /// <summary>0x41: a key action reached the focused or active element (<c>UIElement::KeyDown</c>; param1 the action, param2 the extent × 1000).</summary>
    public const uint KeyDown = 0x41;

    /// <summary>0x42: a key action ended (<c>UIElement::KeyUp</c>; param1 the action).</summary>
    public const uint KeyUp = 0x42;

    /// <summary>0x43: a list box's selected item was selected again (<c>UIElement_ListBox::SetSelectedItem</c>).</summary>
    public const uint ListBoxReselect = 0x43;

    /// <summary>0x44: text was deleted from a text element (<c>UIElement_Text::DeleteSection</c>).</summary>
    public const uint TextDeleted = 0x44;
}

/// <summary>The global message ids (<c>UIElementManager::BroadcastGlobalMessage</c>).</summary>
public static class UiGlobalMessageIds
{
    /// <summary>1: a key action started that no focused or active element took (data: the action).</summary>
    public const uint ActionStart = 1;

    /// <summary>2: a key action ended that no focused or active element took (data: the action).</summary>
    public const uint ActionEnd = 2;

    /// <summary>3: one frame (<c>UIElementManager::UseTime</c>).</summary>
    public const uint Frame = 3;
}

/// <summary>
/// The mouse actions (retail's input actions 5-15, which the mouse handlers receive as their "button"; engine key map
/// 0x14000002, input map 3): what <see cref="UiElement.MouseDown"/> and its kin get.
/// </summary>
public static class UiMouseActions
{
    /// <summary>5: the wheel turned toward the user's screen top (scroll up).</summary>
    public const uint WheelUp = 5;

    /// <summary>6: the wheel turned down.</summary>
    public const uint WheelDown = 6;

    /// <summary>7: the left button.</summary>
    public const uint Left = 7;

    /// <summary>8: the right button.</summary>
    public const uint Right = 8;

    /// <summary>9: the middle button.</summary>
    public const uint Middle = 9;

    /// <summary>10: a left double click.</summary>
    public const uint LeftDouble = 10;

    /// <summary>11: a right double click.</summary>
    public const uint RightDouble = 11;

    /// <summary>12: a middle double click.</summary>
    public const uint MiddleDouble = 12;

    /// <summary>13-15: a tap of the left, right or middle button (no binding in the retail key maps uses taps).</summary>
    public const uint LeftTap = 13;

    /// <summary>True for a tap action (13-15).</summary>
    public static bool IsTap(uint action) => action is >= 13 and <= 15;
}

/// <summary>
/// The engine's UI state ids (the UISTATE names; DatReaderWriter's <c>UIStateId</c>): the states the widget classes
/// enter by themselves.
/// </summary>
public static class UiStateIds
{
    /// <summary>1: <c>Normal</c>.</summary>
    public const uint Normal = 1;

    /// <summary>2: <c>Normal_rollover</c>.</summary>
    public const uint NormalRollover = 2;

    /// <summary>3: <c>Normal_pressed</c>.</summary>
    public const uint NormalPressed = 3;

    /// <summary>4: <c>Normal_focussed</c>.</summary>
    public const uint NormalFocussed = 4;

    /// <summary>5: <c>Normal_activated</c>.</summary>
    public const uint NormalActivated = 5;

    /// <summary>6: <c>Highlight</c>.</summary>
    public const uint Highlight = 6;

    /// <summary>7: <c>Highlight_rollover</c>.</summary>
    public const uint HighlightRollover = 7;

    /// <summary>8: <c>Highlight_pressed</c>.</summary>
    public const uint HighlightPressed = 8;

    /// <summary>9: <c>Drag_rollover_accept</c>.</summary>
    public const uint DragRolloverAccept = 9;

    /// <summary>10: <c>Drag_rollover_reject</c>.</summary>
    public const uint DragRolloverReject = 10;

    /// <summary>11: <c>Closed</c> (a panel's tab).</summary>
    public const uint Closed = 11;

    /// <summary>12: <c>Open</c>.</summary>
    public const uint Open = 12;

    /// <summary>13: <c>Ghosted</c>.</summary>
    public const uint Ghosted = 13;

    /// <summary>24: <c>Dialog_pending_true</c>.</summary>
    public const uint DialogPendingTrue = 24;

    /// <summary>25: <c>Dialog_pending_false</c>.</summary>
    public const uint DialogPendingFalse = 25;
}

using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Input;

/// <summary>
/// How an input action fired (the fields of retail's <c>InputEvent</c> a handler reads): whether it starts or ends, its
/// extent, how the action map says it behaves, and the activation and meta keys of the control that fired it.
/// </summary>
/// <param name="Start">True when the action starts (a press), false when it ends (<c>m_fStart</c>).</param>
/// <param name="Extent">1 for a press, 0 for a release, or an analog value (<c>m_InputExtent</c>).</param>
/// <param name="Toggle">How the action behaves while held (<c>m_ToggleType</c>; Impulse when the action map does not say).</param>
/// <param name="Activation">The control's activation bits (<c>m_InputKey.m_activation</c>: 1 down, 2 up, 8 double-click down, ...).</param>
/// <param name="MetaMode">The meta keys held (<c>m_InputKey.m_metamode</c>).</param>
/// <param name="RepeatCount">For an auto-repeating action, how many repeats this event stands for (0 for the first press).</param>
public readonly record struct RetailActivation(bool Start, float Extent, RetailToggleType Toggle, uint Activation, uint MetaMode, int RepeatCount = 0);

/// <summary>An input action as the input router hands it to a handler: which map and action fired, and how.</summary>
/// <param name="InputMapId">The input map the binding was found in (<c>m_InputMapID</c>; 0 for an action no key fired).</param>
/// <param name="ActionId">The action (<c>m_InputAction</c>).</param>
/// <param name="Activation">How it fired.</param>
public readonly record struct UiInputAction(uint InputMapId, uint ActionId, RetailActivation Activation);

/// <summary>
/// Something that registers input maps with the router and receives their actions (retail's <c>IInputActionCallback</c>):
/// a UI element, a screen, or the host's player controller (G5's <c>PlayerController</c> registers the movement map 4).
/// </summary>
public interface IInputActionSink
{
    /// <summary>
    /// Action <paramref name="actionId"/> of input map <paramref name="inputMapId"/> fired (<c>IInputActionCallback::OnAction</c>).
    /// Return true to take it; false lets the action go on to the router's handlers.
    /// </summary>
    bool OnInputAction(uint inputMapId, uint actionId, RetailActivation activation);
}

/// <summary>A mouse button as the host reports it.</summary>
public enum UiMouseButton
{
    /// <summary>The left button (DirectInput button 0).</summary>
    Left = 0,

    /// <summary>The right button (button 1).</summary>
    Right = 1,

    /// <summary>The middle button (button 2).</summary>
    Middle = 2,
}

/// <summary>
/// Where the mouse goes when the UI does not take it: the 3D world, through a viewport hole or the screen outside the
/// retail frame (G5's player controller: selection, mouse look). Coordinates are logical window pixels, as the host
/// reported the mouse (<see cref="Core.UiElementManager.DisplayOrigin"/> included).
/// </summary>
public interface IWorldPointerSink
{
    /// <summary>
    /// A mouse button went down or up over the world (<paramref name="doubleClick"/> for a double click's press). Every
    /// press gets one release: when the window loses the focus with the button down, the release comes then
    /// (<see cref="UiInputRouter.SetClientActive"/>), at the last mouse position.
    /// </summary>
    void OnWorldPointer(int x, int y, UiMouseButton button, bool down, bool doubleClick);

    /// <summary>The mouse moved by (<paramref name="dx"/>, <paramref name="dy"/>) while mouse look is on (<c>CInputManager::CallMouseLookHandler</c>).</summary>
    void OnMouseLook(float dx, float dy);

    /// <summary>Set by the router when a text field takes or gives up the keyboard (retail's text mode): movement keys do not reach the world while it is on.</summary>
    bool TextEntryActive { set; }
}

/// <summary>One input map registration (<c>CInputManager::InputMapEntry</c>).</summary>
/// <param name="InputMapId">The input map.</param>
/// <param name="Callback">Who receives its actions.</param>
/// <param name="Priority">Its priority; higher maps are searched first.</param>
public readonly record struct UiInputMapEntry(uint InputMapId, IInputActionSink Callback, int Priority);

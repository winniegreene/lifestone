using Godot;
using Lifestone.Client.RetailUi.Input;

namespace Lifestone.Game.RetailUi;

/// <summary>
/// The retail UI's input feed: Godot's keyboard and mouse events go to the UI's input router (<see cref="UiInputRouter"/>,
/// docs/retail-ui.md §10) the way Windows' messages went to retail's input manager, and an event the UI takes is marked handled,
/// so the explore camera, the tool panels and the F-keys never see it (<c>SetInputAsHandled</c> only when consumed: a key nothing
/// binds stays the host's).
/// </summary>
/// <remarks>
/// <para><b>Keys</b> are Godot's physical key codes (the key's place on the keyboard, not its character) turned into DIK scan
/// codes by <see cref="GodotKeyCodes"/>, with the left or right side of Shift, Ctrl, Alt and the Windows key; the typed
/// character of a key press (<see cref="InputEventKey.Unicode"/>) goes to the focused text field after the key itself, as
/// <c>WM_CHAR</c> follows <c>WM_KEYDOWN</c> (D39: no IME). A repeated press is passed on and ignored by the router, which repeats
/// held actions itself.</para>
/// <para><b>The mouse</b> is in logical window pixels: the window position divided by the UI scale (rounded down), the unit the
/// runtime works in. Wheel notches (one per press event) and double clicks are the engine's. While the mouse is captured
/// (mouse look) only the router's mouse-look path hears it, when the world asked for it.</para>
/// <para><b>Focus</b> (<c>CInputManager_WIN32::Activate</c>): <c>NOTIFICATION_APPLICATION_FOCUS_OUT</c> and
/// <c>NOTIFICATION_APPLICATION_FOCUS_IN</c> call <see cref="UiInputRouter.SetClientActive"/>, since neither Windows nor Godot sends the
/// releases a window misses while it has no focus; the mouse leaving the window calls <see cref="UiInputRouter.MouseLeave"/>.</para>
/// <para><b>Drag and drop</b> (M4 2.2 s3, D4; <see cref="UiDrag"/>, <c>TODO(m4-retail-drag)</c>): the feed needs no drag code of its own, because the router's mouse path is the drag's. The button press, the motion and the release below reach
/// <see cref="UiInputRouter.MouseButton"/> and <see cref="UiInputRouter.MouseMove"/>, which arm, begin, carry and end the drag (a release that ends a drag is taken, so the world never sees it); Escape reaches <see cref="UiInputRouter.KeyDown"/>,
/// which cancels a drag that is on and takes the key; a lost focus cancels it through <see cref="UiInputRouter.SetClientActive"/>. The carried icon is a top-layer element of the UI runtime that follows the pointer (no Godot node), and no
/// event allocates: the logical position is a value tuple and the router works on its own fields. <see cref="DragActive"/> tells a host that an item is being carried.</para>
/// </remarks>
public partial class RetailUiInput : Node
{
    private UiInputRouter _router = null!;
    private Func<int> _scale = null!;
    private bool _lastWheelTaken;

    /// <summary>The router this feeds.</summary>
    public UiInputRouter Router => _router;

    /// <summary>True while an item is being carried by the pointer (the router's <see cref="UiDrag"/> is dragging).</summary>
    public bool DragActive => _router.Drag.Phase == Lifestone.Client.RetailUi.UiDragPhase.Dragging;

    /// <summary>False stops the feed (nothing reaches the router, nothing is handled); the layer sets it while it is hidden.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Events passed to the router so far, and how many it took (the perf line and the checks).</summary>
    public (long Events, long Taken) Counts { get; private set; }

    /// <summary>A feed into <paramref name="router"/> for a UI drawn at <paramref name="scale"/> window pixels per UI pixel.</summary>
    public static RetailUiInput Create(UiInputRouter router, Func<int> scale)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(scale);
        return new RetailUiInput { Name = "RetailUiInput", _router = router, _scale = scale };
    }

    /// <inheritdoc/>
    public override void _Input(InputEvent @event)
    {
        if (Enabled && Handle(@event))
        {
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc/>
    public override void _Notification(int what)
    {
        if (_router is null || !Enabled)
        {
            return;
        }

        if (what == NotificationApplicationFocusOut)
        {
            _router.SetClientActive(false);
        }
        else if (what == NotificationApplicationFocusIn)
        {
            _router.SetClientActive(true);
        }
        else if (what == NotificationWMMouseExit)
        {
            _router.MouseLeave();
        }
    }

    /// <summary>Passes one event to the router; true when the UI took it (the caller marks it handled).</summary>
    public bool Handle(InputEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        bool taken;
        switch (@event)
        {
            case InputEventKey key:
                taken = HandleKey(key);
                break;
            case InputEventMouseMotion motion:
                taken = HandleMotion(motion);
                break;
            case InputEventMouseButton button:
                taken = HandleButton(button);
                break;
            default:
                return false;
        }

        var (events, took) = Counts;
        Counts = (events + 1, took + (taken ? 1 : 0));
        return taken;
    }

    private bool HandleKey(InputEventKey key)
    {
        var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
        var dik = GodotKeyCodes.ToDik((int)code, (UiKeyLocation)(int)key.Location);
        var taken = false;
        if (key.Pressed)
        {
            if (dik != 0)
            {
                taken = _router.KeyDown(dik);
            }

            if (key.Unicode is > 0 and <= 0xFFFF && _router.Character((char)key.Unicode))
            {
                taken = true;
            }
        }
        else if (dik != 0)
        {
            taken = _router.KeyUp(dik);
        }

        return taken;
    }

    private bool HandleMotion(InputEventMouseMotion motion)
    {
        if (Godot.Input.MouseMode == Godot.Input.MouseModeEnum.Captured)
        {
            return _router.MouseLookMode && _router.MouseLook(motion.Relative.X, motion.Relative.Y);
        }

        var (x, y) = Logical(motion.Position);
        return _router.MouseMove(x, y);
    }

    private bool HandleButton(InputEventMouseButton button)
    {
        var (x, y) = Logical(button.Position);
        if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            if (!button.Pressed)
            {
                return _lastWheelTaken; // the release of a notch goes where the press went
            }

            _router.MouseMove(x, y);
            var notches = Math.Max(1, (int)Math.Round(button.Factor));
            _lastWheelTaken = _router.MouseWheel(button.ButtonIndex == MouseButton.WheelUp ? notches : -notches);
            return _lastWheelTaken;
        }

        UiMouseButton? mapped = button.ButtonIndex switch
        {
            MouseButton.Left => UiMouseButton.Left,
            MouseButton.Right => UiMouseButton.Right,
            MouseButton.Middle => UiMouseButton.Middle,
            _ => null,
        };
        if (mapped is not { } ui)
        {
            return false;
        }

        _router.MouseMove(x, y);
        return _router.MouseButton(ui, button.Pressed, button.Pressed && button.DoubleClick);
    }

    private (int X, int Y) Logical(Vector2 position)
    {
        var scale = Math.Max(1, _scale());
        return ((int)MathF.Floor(position.X / scale), (int)MathF.Floor(position.Y / scale));
    }
}

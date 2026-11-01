using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Input;

/// <summary>
/// The input router: retail's input manager (<c>CInputManager</c>, <c>CInputManager_WIN32</c>) and the element manager's
/// input handling (<c>UIElementManager</c>'s mouse, key and tooltip code) in one place. The host feeds it keys (DIK scan
/// codes), typed characters and the mouse in logical window pixels (window pixels divided by the UI scale; a forced,
/// centred display starts at <see cref="UiElementManager.DisplayOrigin"/> in them); it turns keys and buttons into
/// input actions through the key bindings and the registered input maps, and drives the elements.
/// </summary>
/// <remarks>
/// <para>
/// <b>Input maps.</b> Whoever handles actions registers input maps with a priority (<see cref="RegisterInputMap"/>,
/// <c>CInputManager::RegisterInputMap</c>, 11.4186 0x00431B70, 11.6096 0x00431D10): the element manager registers the
/// UI's mouse map 3 at 0 and map 13 at 4000; the UI flow map 14 at 4000; the focused element its maps at 3000
/// (<see cref="UiElementManager.FocusInputMapPriority"/>), a focused editable text also the text maps 7 and 8 and the
/// keyboard stop map 1 ten below; the active window its maps at 2000; the player controller (G5) the movement map 4 at
/// 1000, as <c>CPlayerSystem::RegisterInputMaps</c> does.
/// </para>
/// <para>
/// <b>Precedence</b> follows from that and from <c>CInputManager_WIN32::FireInputEvent</c> (11.4186 0x00688800, 11.6096
/// 0x00689890): a key is looked up in every registered map from the highest priority down, until the keyboard stop map 1
/// (for a key) or the stop map 2 (for anything); the best binding found wins
/// (<see cref="RetailKeyBindings.IsBetterMatch"/>), and its action goes first to the map's registrant, then, when the
/// registrant does not take it, to the handlers: the element manager (the focused element, else the active window,
/// hears <see cref="UiMessageIds.KeyDown"/>; unclaimed, global message 1 and the visibility-toggle actions), then the
/// host's (<see cref="AddActionHandler"/>). So a modal dialog takes every click (it blocks clicks behind it), the mouse
/// capture takes the mouse, a focused text field takes the keyboard (its stop map hides the movement map), element
/// handlers and the active window come next, then the game maps by priority; a key nothing binds is not taken
/// (<see cref="KeyDown"/> returns false), so the host's own keys keep working.
/// </para>
/// <para>
/// <b>Threading.</b> One thread (the host's main thread), like the element manager.
/// </para>
/// </remarks>
public sealed partial class UiInputRouter
{
    /// <summary>Input map 1: the keyboard stop a focused editable text registers (no key looks past it).</summary>
    public const uint KeyboardStopMap = 1;

    /// <summary>Input map 2: a stop for every control.</summary>
    public const uint StopMap = 2;

    /// <summary>Input map 3: the UI's mouse buttons (the element manager, priority 0).</summary>
    public const uint UiMouseMap = 3;

    /// <summary>Input map 4: movement (the player controller).</summary>
    public const uint MovementMap = 4;

    /// <summary>Input map 7: text editing (the focused editable text).</summary>
    public const uint TextEditMap = 7;

    /// <summary>Input map 8: copy, cut and paste (the focused editable or selectable text).</summary>
    public const uint TextClipboardMap = 8;

    /// <summary>Input map 9: accept and escape (Enter and Esc; screens and some text fields).</summary>
    public const uint AcceptEscapeMap = 9;

    /// <summary>Input map 10 (0xA): the wheel (scrollables).</summary>
    public const uint ScrollMap = 10;

    /// <summary>Input map 13: the element manager's own (priority 4000; no binding in the retail maps).</summary>
    public const uint UiManagerMap = 13;

    private const uint Down = 1;
    private const uint Up = 2;
    private const uint DoubleClickDown = 8;
    private const uint DoubleClickUp = 0x10;
    private const uint NearbyDown = 0x20;
    private const uint NearbyUp = 0x40;
    private const uint FiresOnPress = 0xA9;
    private const uint EventFlag = 0x80000000;

    private readonly UiElementManager _manager;
    private readonly List<UiInputMapEntry> _maps = [];
    private readonly Dictionary<uint, ActionState> _actionStates = [];
    private readonly Dictionary<uint, ControlState> _controls = [];
    private readonly List<IInputActionSink> _actionHandlers = [];
    private readonly List<UiElement> _characterHandlers = [];
    private readonly ManagerCallback _managerCallback;
    private UiInputMapEntry[] _mapScratch = new UiInputMapEntry[16];
    private uint[] _repeatScratch = new uint[8];
    private uint _metaMode;
    private bool _processingKeyDown;
    private bool _actionDuringKeyDown;
    private bool _ignoreNextChar;
    private bool _textMode;

    internal UiInputRouter(UiElementManager manager)
    {
        _manager = manager;
        _managerCallback = new ManagerCallback(this);
        Drag = new UiDrag(manager);

        // UIElementManager::Init (11.4186 0x0045EE10, 11.6096 0x0045EEF0) registers the UI's maps.
        RegisterInputMap(UiMouseMap, _managerCallback, 0);
        RegisterInputMap(UiManagerMap, _managerCallback, 4000);
        RetailFocusLoss = (manager.Services.RetailGlitches & UiRetailGlitches.FocusLossActions) != 0;
    }

    /// <summary>
    /// The key bindings keys and buttons resolve through (<see cref="RetailKeyBindings.Load"/>); null until the host sets
    /// them, and then no key or button resolves (the mouse still moves over the UI).
    /// </summary>
    public RetailKeyBindings? Bindings { get; set; }

    /// <summary>Where the mouse goes when the UI does not take it (G5's player controller), or null.</summary>
    public IWorldPointerSink? WorldPointer { get; set; }

    /// <summary>
    /// True while a focused text field takes the keyboard (retail's text mode, <c>CInputManager::SetTextMode</c>, 11.4186
    /// 0x004313F0, 11.6096 0x00431590): typed characters go to it, and the host should not move the camera with keys.
    /// </summary>
    public bool KeyboardCaptured => _textMode;

    /// <summary>The meta keys held (<c>m_metaKeyMode</c>): bits from the bindings' meta keys (Shift 0x80000000, Ctrl 0x40000000, Alt 0x20000000, Windows 0x10000000).</summary>
    public uint MetaMode => _metaMode;

    /// <summary>
    /// True while mouse movement goes to the world as mouse look (<c>CInputManager::SetMouseLookMode</c>). It is what the
    /// host wants (retail's <c>m_fWantMouseLookMode</c>): while the window has no focus (<see cref="ClientActive"/>), mouse
    /// look sends nothing, and it resumes when the focus comes back.
    /// </summary>
    public bool MouseLookMode { get; set; }

    /// <summary>
    /// True while the client window has the focus (<c>CInputManager_WIN32::m_fClientIsActive</c>; true at the start). The
    /// host reports changes with <see cref="SetClientActive"/>.
    /// </summary>
    public bool ClientActive { get; private set; } = true;

    /// <summary>
    /// True to release the pressed keys on a focus loss as retail did, glitch included (<see cref="UiRetailGlitches.FocusLossActions"/>):
    /// the held actions do not end. False by default (Lifestone's fix: they end); set from
    /// <see cref="UiServices.RetailGlitches"/>.
    /// </summary>
    public bool RetailFocusLoss { get; set; }

    /// <summary>
    /// Raised when <see cref="ClientActive"/> changes, after a loss has released the pressed keys
    /// (<c>CInputManager::CallFocusSwitchHandler</c>, 11.4186 0x00431250, 11.6096 0x004313F0). Retail's release also tells the
    /// command interpreter through a virtual of <c>SmartBox::cmdinterp</c>, most likely
    /// <c>CommandInterpreter::LoseKeyboardFocus</c> (11.4186 0x006B31F0, 11.6096 0x006B4130: the keyboard's movement
    /// commands are cleared and the player stops); G5's player controller does that when it hears false.
    /// TODO(retailui-runtime): confirm that slot in the 11.6096 vtable.
    /// </summary>
    public event Action<bool>? ClientActiveChanged;

    /// <summary>
    /// The delay before a held auto-repeating action repeats, in seconds (<c>CInputManager::sm_timeKeyRepeatDelay</c>; retail
    /// takes it from Windows, whose default this is). A host may pass the user's setting.
    /// </summary>
    public double KeyRepeatDelaySeconds { get; set; } = 0.5;

    /// <summary>The interval between repeats, in seconds (<c>sm_timeKeyRepeatSpeed</c>; Windows' default about 30 a second).</summary>
    public double KeyRepeatIntervalSeconds { get; set; } = 1.0 / 30.0;

    /// <summary>The registered input maps, highest priority first.</summary>
    public IReadOnlyList<UiInputMapEntry> InputMaps => _maps;

    /// <summary>The actions in progress (started and not ended), by action id.</summary>
    public IEnumerable<uint> ActiveActions => _actionStates.Keys;

    /// <summary>
    /// <c>CInputManager::IsActionInProgress</c> (11.4186 0x00431950, 11.6096 0x00431AF0): true while action
    /// <paramref name="actionId"/> is held (started and not ended).
    /// </summary>
    public bool IsActionInProgress(uint actionId) => _actionStates.ContainsKey(actionId);

    /// <summary>
    /// The UI item being dragged (retail's <c>UIElementManager</c> drag element), or null: the item cell the left button went down on while <see cref="Drag"/>
    /// carries it (M4 2.2 s3; the M2 stub answered null). <see cref="Widgets.UiField"/>'s drag-rollover reads it.
    /// </summary>
    public UiElement? DragElement => Drag.Element;

    /// <summary>The drag and drop of item cells (M4 2.2 s3): the mouse path below feeds it, panels register their drop targets with it.</summary>
    public UiDrag Drag { get; }

    /// <summary>
    /// <c>CInputManager::RegisterInputMap</c>: <paramref name="callback"/> receives the actions of input map
    /// <paramref name="inputMapId"/>, searched at <paramref name="priority"/> (higher first; a new map goes before older
    /// ones of the same priority, as <c>List&lt;InputMapEntry&gt;::insert_sort</c>, 11.4186 0x00431510, 11.6096 0x004316B0,
    /// inserts). An identical registration (map, callback and priority) is refused (false); the same map and callback at
    /// another priority is a second entry.
    /// </summary>
    public bool RegisterInputMap(uint inputMapId, IInputActionSink callback, int priority)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var entry = new UiInputMapEntry(inputMapId, callback, priority);
        foreach (var existing in _maps)
        {
            if (existing.InputMapId == inputMapId && ReferenceEquals(existing.Callback, callback) && existing.Priority == priority)
            {
                return false;
            }
        }

        var index = 0;
        while (index < _maps.Count && priority < _maps[index].Priority)
        {
            index++;
        }

        _maps.Insert(index, entry);
        return true;
    }

    /// <summary>
    /// <c>CInputManager::UnregisterInputMap</c> (11.4186 0x004316D0, 11.6096 0x00431870): removes every registration of
    /// <paramref name="inputMapId"/> by <paramref name="callback"/>. True when there was one.
    /// </summary>
    public bool UnregisterInputMap(uint inputMapId, IInputActionSink callback)
    {
        var removed = _maps.RemoveAll(e => e.InputMapId == inputMapId && ReferenceEquals(e.Callback, callback));
        return removed != 0;
    }

    /// <summary>
    /// <c>CInputManager::UnregisterCallback</c> (11.4186 0x00431710, 11.6096 0x004318B0): removes every map
    /// <paramref name="callback"/> registered, and forgets it as the receiver of actions in progress.
    /// </summary>
    public void UnregisterCallback(IInputActionSink callback)
    {
        _maps.RemoveAll(e => ReferenceEquals(e.Callback, callback));
        foreach (var state in _actionStates.Values)
        {
            if (ReferenceEquals(state.Callback, callback))
            {
                state.Callback = null;
            }
        }

        if (callback is UiElement element)
        {
            _characterHandlers.Remove(element);
        }
    }

    /// <summary>
    /// <c>CInputManager::RegisterInputHandler</c> with the action mode: <paramref name="handler"/> hears every action no
    /// map registrant took (after the element manager's own handler). Lifestone's hosts use it for actions of maps they
    /// do not own.
    /// </summary>
    public void AddActionHandler(IInputActionSink handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!_actionHandlers.Contains(handler))
        {
            _actionHandlers.Add(handler);
        }
    }

    /// <summary>Removes a handler added with <see cref="AddActionHandler"/>.</summary>
    public void RemoveActionHandler(IInputActionSink handler) => _actionHandlers.Remove(handler);

    /// <summary>
    /// A key went down (<paramref name="dik"/>, a DIK scan code; <see cref="GodotKeyCodes.ToDik"/>). True when the UI or a
    /// registered map took it, or a text field has the keyboard; false lets the host use the key. A repeat of a key
    /// already down is ignored as retail ignores it (held actions repeat by themselves, <see cref="UseTime"/>).
    /// </summary>
    public bool KeyDown(int dik) => KeyEvent(dik, down: true);

    /// <summary>A key came up. True when its press was taken (or a text field has the keyboard).</summary>
    public bool KeyUp(int dik) => KeyEvent(dik, down: false);

    /// <summary>
    /// The client window gained (<paramref name="active"/> true) or lost the focus (<c>CInputManager_WIN32::Activate</c>,
    /// 11.4186 0x00689AA0, 11.6096 0x0068AB30). The host calls it on Godot's <c>NOTIFICATION_APPLICATION_FOCUS_OUT</c> and
    /// <c>NOTIFICATION_APPLICATION_FOCUS_IN</c>: neither Windows nor Godot sends the releases of keys and buttons held when
    /// the focus went, so a loss releases every pressed control here (<see cref="ReleasePressedKeys"/>) before
    /// <see cref="ClientActiveChanged"/> is raised. Nothing happens when the state does not change.
    /// </summary>
    public void SetClientActive(bool active)
    {
        if (active == ClientActive)
        {
            return;
        }

        ClientActive = active;
        if (!active)
        {
            Drag.Cancel("the window lost the focus");
            ReleasePressedKeys();
        }

        ClientActiveChanged?.Invoke(active);
    }

    /// <summary>
    /// <c>CInputManager_WIN32::ReleasePressedKeys</c> (11.4186 0x00689590, 11.6096 0x0068A620): every control held down is
    /// let go. Its meta keys leave the meta mode, and the action its press matched ends with extent 0 to the action's
    /// receiver, with the meta keys and activation of the press (<c>CInputManager::FireActionEvent</c>: a momentary,
    /// auto-repeating or continuous action ends; a toggle stays as it is). Then the controls are forgotten, so the next
    /// press of each starts afresh. A button the world took (<see cref="WorldPointer"/>) is released to the world, a mouse
    /// capture that no button holds any more is dropped, and Shift is up for the text fields.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retail fires each end with input map 0, which the action map does not have, so it counts as an impulse and nothing
    /// ends; <see cref="RetailFocusLoss"/> restores that. Retail also fires a mouse event of its own when mouse look is on;
    /// what it ends is not known (TODO(retailui-runtime): read it in 11.6096), and Lifestone stops mouse look by
    /// <see cref="ClientActive"/> instead, logged the first time it matters.
    /// </para>
    /// <para>
    /// A mouse button held on an element is let go as if the mouse had left the window (<see cref="MouseLeave"/>), so the
    /// release cancels the press: a button does not click because the focus went (Lifestone's; with retail's map-0 end
    /// nothing is released at all).
    /// </para>
    /// </remarks>
    public void ReleasePressedKeys()
    {
        var pressed = new List<(uint Key, ControlState State)>();
        var mouseHeld = false;
        foreach (var (key, state) in _controls)
        {
            if (state.Down)
            {
                pressed.Add((key, state));
                mouseHeld |= new RetailControl(key).DeviceIndex == RetailKeyBindings.MouseDevice;
            }
        }

        if (mouseHeld && !RetailFocusLoss)
        {
            MouseLeave();
        }

        var bindings = Bindings;
        foreach (var (key, state) in pressed)
        {
            _controls.Remove(key);
            var control = new RetailControl(key);
            var metaBits = bindings?.MetaModeOf(control) ?? 0;
            if (metaBits != 0)
            {
                _metaMode &= ~metaBits;
            }

            if (state.Matched && _actionStates.TryGetValue(state.ActionId, out var action))
            {
                var mapId = RetailFocusLoss ? 0 : state.InputMapId;
                FireActionEvent(state.ActionId, mapId, action.Callback, control, state.MetaMode, state.Activation, extent: 0f);
            }
        }

        _controls.Clear();
        _manager.ShiftKeyDown = false;
        if (_worldButtons.Count != 0)
        {
            var buttons = _worldButtons.ToArray();
            _worldButtons.Clear();
            foreach (var button in buttons)
            {
                WorldPointer?.OnWorldPointer(MousePosition.X, MousePosition.Y, button, down: false, doubleClick: false);
            }
        }

        if (!RetailFocusLoss && ElementWithMouseCapture is not null)
        {
            ElementWithMouseCapture = null;
            _captureCount = 0;
            _actionsTriggeringCapture.Clear();
            _performMouseHitTest = true;
        }

        if (MouseLookMode)
        {
            _manager.LogOnce("input:focus-mouse-look", LogSeverity.Warning,
                "TODO(retailui-runtime): on a focus loss in mouse look, retail fires a mouse event of its own (CInputManager_WIN32::ReleasePressedKeys) whose effect is not known; Lifestone pauses mouse look until the focus returns");
        }
    }

    /// <summary>
    /// A character was typed (the host's text input; retail's <c>WM_CHAR</c>). In text mode it goes to the focused text
    /// field (<c>CInputManager::CallCharacterHandler</c>, 11.4186 0x004311B0, 11.6096 0x00431350), except the character of
    /// the key that turned text mode on (<c>CInputManager_WIN32::OnSwitchTextMode</c>, 11.4186 0x006866B0, 11.6096
    /// 0x00687700). True in text mode.
    /// </summary>
    public bool Character(char c)
    {
        if (!_textMode)
        {
            return false;
        }

        if (_ignoreNextChar)
        {
            _ignoreNextChar = false;
            return true;
        }

        foreach (var handler in _characterHandlers.ToArray())
        {
            if (handler is Text.UiTextElement text)
            {
                text.CharacterHandler(c);
            }
        }

        return true;
    }

    /// <summary>
    /// <c>CInputManager::SetTextMode</c>: a focused editable text field turns text mode on when it gains the focus and off
    /// when it loses it. A switch on while a key press is being handled swallows that key's character.
    /// </summary>
    public void SetTextMode(bool on)
    {
        if (on == _textMode)
        {
            return;
        }

        _textMode = on;
        if (on && _actionDuringKeyDown)
        {
            _ignoreNextChar = true;
        }

        if (!on)
        {
            _ignoreNextChar = false;
        }

        if (WorldPointer is { } world)
        {
            world.TextEntryActive = on;
        }
    }

    /// <summary>A focused editable text field receives typed characters (<c>RegisterInputHandler</c> with the character mode).</summary>
    public void RegisterCharacterHandler(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!_characterHandlers.Contains(element))
        {
            _characterHandlers.Add(element);
        }
    }

    /// <summary>The reverse of <see cref="RegisterCharacterHandler"/>.</summary>
    public void UnregisterCharacterHandler(UiElement element) => _characterHandlers.Remove(element);

    /// <summary>
    /// Fires <paramref name="actionId"/> as if a binding of <paramref name="inputMapId"/> had (a button's input action,
    /// <c>UIElement_Button::HandleButtonClick</c>): an Impulse start to the handlers. True when one took it.
    /// </summary>
    public bool FireAction(uint inputMapId, uint actionId)
    {
        var activation = new RetailActivation(true, 1f, RetailToggleType.Impulse, 0, _metaMode);
        return SendActionToListeners(new UiInputAction(inputMapId, actionId, activation), callback: null);
    }

    /// <summary>
    /// Per frame (the element manager's <see cref="UiElementManager.Update"/>): auto-repeating actions repeat
    /// (<c>CInputManager::UseTime</c>, 11.4186 0x00431BC0, 11.6096 0x00431D60), the mouse is hit-tested again when
    /// something under it changed (<c>UIElementManager::DoMouseUpdate</c>), and the tooltip timer runs
    /// (<c>UIElementManager::CheckTooltip</c>).
    /// </summary>
    internal void UseTime(double now)
    {
        RepeatActions(now);
        if (_performMouseHitTest)
        {
            DoMouseUpdate();
        }

        CheckTooltip(now);
    }

    private bool KeyEvent(int dik, bool down)
    {
        if (dik <= 0)
        {
            return false;
        }

        if (down && dik == DikScanCodes.Escape && Drag.Phase == UiDragPhase.Dragging)
        {
            DropDragCapture();
            Drag.Cancel("Escape"); // Escape ends the drag and nothing else: the key is taken
            return true;
        }

        if (down && dik == DikScanCodes.Escape && _metaMode == 0 && !_textMode && _manager.TargetMode.Escape())
        {
            return true; // ClientUISystem::OnAction's escape: a target mode ends and the key is taken; a text field with the keyboard has its own escape first, as retail's focused element does
        }

        var canonical = DikScanCodes.Canonical(dik);
        var control = RetailKeyBindings.Keyboard(canonical);
        var previous = _controls.GetValueOrDefault(control.Key);

        // CInputManager_WIN32::GenerateKeyboardEvent: Windows sends no key-down for SysRq, so its release fires a press first.
        if (!down && canonical == DikScanCodes.SysRq && !previous.Down)
        {
            FireInputEvent(control, down: true, extraActivation: 0);
            previous = _controls.GetValueOrDefault(control.Key);
        }

        if (previous.Down == down)
        {
            // A repeat (or a release of a key never seen down): retail fires nothing.
            return (down && previous.Taken) || _textMode;
        }

        _processingKeyDown = down;
        _actionDuringKeyDown = false;
        bool taken;
        try
        {
            taken = FireInputEvent(control, down, extraActivation: 0);
        }
        finally
        {
            _processingKeyDown = false;
            _actionDuringKeyDown = false;
        }

        if (canonical == DikScanCodes.LShift)
        {
            _manager.ShiftKeyDown = down;
        }

        return taken || (down ? _textMode : previous.Taken || _textMode);
    }

    /// <summary>
    /// <c>CInputManager_WIN32::FireInputEvent</c>: the control's activation (down, up, double click), the meta keys
    /// (looked up with those held when it went down), the end of an action the control started, the best binding in the
    /// registered maps, and the action fired. True when a binding matched (or a stop map ate the key).
    /// </summary>
    private bool FireInputEvent(RetailControl control, bool down, uint extraActivation)
    {
        var previous = _controls.GetValueOrDefault(control.Key);
        var activation = EventFlag | (down ? Down : Up) | extraActivation;
        var mode = down ? _metaMode : previous.MetaMode;
        if (!down && (previous.Activation & DoubleClickDown) != 0)
        {
            activation |= DoubleClickUp;
            if ((previous.Activation & NearbyDown) != 0)
            {
                activation |= NearbyUp;
            }
        }

        var bindings = Bindings;
        var metaBits = bindings?.MetaModeOf(control) ?? 0;
        if (metaBits != 0)
        {
            _metaMode = down ? _metaMode | metaBits : _metaMode & ~metaBits;
        }

        // The release of a control that started a held action ends it, with the map, keys and mode of the press.
        if (!down && previous.Matched && _actionStates.TryGetValue(previous.ActionId, out var started)
            && started.Toggle is RetailToggleType.Momentary or RetailToggleType.Continuous or RetailToggleType.AutoRepeat)
        {
            DeactivateActionKey(previous.ActionId, previous.InputMapId, control, previous.MetaMode, previous.Activation, previous.Callback);
        }

        var found = false;
        var stopped = false;
        RetailBinding best = default;
        UiInputMapEntry bestEntry = default;
        if (bindings is not null)
        {
            var count = _maps.Count;
            if (_mapScratch.Length < count)
            {
                _mapScratch = new UiInputMapEntry[count * 2];
            }

            _maps.CopyTo(_mapScratch);
            for (var i = 0; i < count; i++)
            {
                var entry = _mapScratch[i];
                if (entry.InputMapId == KeyboardStopMap)
                {
                    if (control.DeviceIndex == RetailKeyBindings.KeyboardDevice)
                    {
                        stopped = true;
                        break;
                    }

                    continue;
                }

                if (entry.InputMapId == StopMap)
                {
                    stopped = true;
                    break;
                }

                if (bindings.TryFindBestMatch(entry.InputMapId, control, mode, activation, out var match)
                    && (!found || RetailKeyBindings.IsBetterMatch(mode, activation, best, match)))
                {
                    best = match;
                    bestEntry = entry;
                    found = true;
                }
            }

            Array.Clear(_mapScratch, 0, count);
        }

        var taken = found || stopped;
        if (found && best.ActionId > 1 && best.ActionId is not (2 or 3))
        {
            // Actions 2 and 3 are the mouse axes (pointer movement); retail adds them up and fires nothing.
            var extent = down ? 1f : 0f;
            if ((best.Activation & FiresOnPress) == 0 && extent == 0f)
            {
                extent = 1f;
            }

            FireActionEvent(best.ActionId, bestEntry.InputMapId, bestEntry.Callback, control, mode, activation, extent);
        }

        _controls[control.Key] = new ControlState(down, mode, activation, found ? best.ActionId : 0, found ? bestEntry.InputMapId : 0,
            found ? bestEntry.Callback : null, found, taken);
        return taken;
    }

    /// <summary>
    /// <c>CInputManager::FireActionEvent</c>: by the action's toggle type, a release ends a momentary, auto-repeating or
    /// continuous action; a press toggles a toggle, starts an impulse once, and starts the others (a momentary action
    /// whose binding fires only on release starts and ends at once).
    /// </summary>
    private void FireActionEvent(uint actionId, uint mapId, IInputActionSink? callback, RetailControl control, uint mode, uint activation, float extent)
    {
        var toggle = Bindings?.ToggleOf(mapId, actionId) ?? RetailToggleType.Impulse;
        if (extent == 0f)
        {
            if (toggle is RetailToggleType.Momentary or RetailToggleType.AutoRepeat or RetailToggleType.Continuous)
            {
                DeactivateActionKey(actionId, mapId, control, mode, activation, callback);
            }

            return;
        }

        switch (toggle)
        {
            case RetailToggleType.Toggle:
                ToggleActionKey(actionId, mapId, callback, control, mode, activation, extent);
                break;
            case RetailToggleType.Impulse:
                Send(actionId, mapId, callback, start: true, extent, toggle, activation, mode, 0);
                break;
            case RetailToggleType.Momentary:
                ActivateActionKey(actionId, mapId, callback, toggle, control, mode, activation, extent);
                if ((activation & FiresOnPress) == 0)
                {
                    DeactivateActionKey(actionId, mapId, control, mode, activation, callback);
                }

                break;
            default:
                ActivateActionKey(actionId, mapId, callback, toggle, control, mode, activation, extent);
                break;
        }
    }

    /// <summary>
    /// <c>CInputManager::ActivateActionKey</c> (11.4186 0x00432650, 11.6096 0x004327F0) with <c>ActionState::AddKeyPress</c>
    /// (11.4186 0x00431E00, 11.6096 0x00431FA0): the first control to press an action starts it (moving forward, backward
    /// or stopping first ends autorun, <c>CInputManager::TurnOffRunLock</c>); a further control starts it again only when
    /// it presses harder.
    /// </summary>
    private void ActivateActionKey(uint actionId, uint mapId, IInputActionSink? callback, RetailToggleType toggle, RetailControl control, uint mode, uint activation, float extent)
    {
        if (!_actionStates.TryGetValue(actionId, out var state))
        {
            state = new ActionState(actionId, mapId, callback, toggle, _manager.Now);
            _actionStates[actionId] = state;
            state.Keys.Add((control.Key, extent));
            if (actionId is MoveForward or MoveBackward or StopMoving)
            {
                TurnOffRunLock();
            }

            Send(actionId, mapId, callback, start: true, extent, toggle, activation, mode, 0);
            return;
        }

        var max = 0f;
        var known = false;
        for (var i = 0; i < state.Keys.Count; i++)
        {
            max = Math.Max(max, Math.Abs(state.Keys[i].Extent));
            if (state.Keys[i].Key == control.Key)
            {
                state.Keys[i] = (control.Key, extent);
                known = true;
            }
        }

        if (!known)
        {
            state.Keys.Add((control.Key, extent));
        }

        if (max < Math.Abs(extent))
        {
            Send(actionId, mapId, state.Callback, start: true, extent, state.Toggle, activation, mode, 0);
        }
    }

    /// <summary>
    /// <c>CInputManager::DeactivateActionKey</c> (11.4186 0x00432530, 11.6096 0x004326D0) with <c>ActionState::RemoveKeyPress</c>
    /// (11.4186 0x00431420, 11.6096 0x004315C0): the last control to let go ends the action; when others still hold it more
    /// weakly, it starts again at their extent.
    /// </summary>
    private void DeactivateActionKey(uint actionId, uint mapId, RetailControl control, uint mode, uint activation, IInputActionSink? callback)
    {
        if (!_actionStates.TryGetValue(actionId, out var state))
        {
            return;
        }

        var index = state.Keys.FindIndex(k => k.Key == control.Key);
        if (index < 0)
        {
            return;
        }

        var released = Math.Abs(state.Keys[index].Extent);
        state.Keys.RemoveAt(index);
        if (state.Keys.Count == 0)
        {
            _actionStates.Remove(actionId);
            Send(actionId, state.MapId, state.Callback, start: false, 0f, state.Toggle, activation, mode, 0);
            return;
        }

        var remaining = state.Keys.Max(k => Math.Abs(k.Extent));
        if (remaining < released)
        {
            Send(actionId, state.MapId, state.Callback, start: true, remaining, state.Toggle, activation, mode, 0);
        }
    }

    /// <summary>
    /// <c>CInputManager::ToggleActionKey</c> (11.4186 0x00432480, 11.6096 0x00432620): a toggle action starts on one press
    /// and ends on the next.
    /// </summary>
    private void ToggleActionKey(uint actionId, uint mapId, IInputActionSink? callback, RetailControl control, uint mode, uint activation, float extent)
    {
        if (_actionStates.Remove(actionId, out var state))
        {
            Send(actionId, state.MapId, state.Callback, start: false, extent, RetailToggleType.Toggle, activation, mode, 0);
            return;
        }

        state = new ActionState(actionId, mapId, callback, RetailToggleType.Toggle, _manager.Now);
        state.Keys.Add((control.Key, extent));
        _actionStates[actionId] = state;
        Send(actionId, mapId, callback, start: true, extent, RetailToggleType.Toggle, activation, mode, 0);
    }

    /// <summary><c>CInputManager::TurnOffRunLock</c> (11.4186 0x004325E0, 11.6096 0x00432780): autorun (action 0x30) ends.</summary>
    private void TurnOffRunLock()
    {
        if (_actionStates.Remove(AutoRun, out var state))
        {
            Send(AutoRun, state.MapId, state.Callback, start: false, 0f, state.Toggle, 0, _metaMode, 0);
        }
    }

    private void RepeatActions(double now)
    {
        if (_actionStates.Count == 0)
        {
            return;
        }

        var count = 0;
        foreach (var (id, state) in _actionStates)
        {
            if (state.Toggle is RetailToggleType.AutoRepeat or RetailToggleType.Continuous)
            {
                if (count == _repeatScratch.Length)
                {
                    Array.Resize(ref _repeatScratch, count * 2);
                }

                _repeatScratch[count++] = id;
            }
        }

        for (var i = 0; i < count; i++)
        {
            if (!_actionStates.TryGetValue(_repeatScratch[i], out var state))
            {
                continue;
            }

            if (state.Toggle == RetailToggleType.AutoRepeat)
            {
                var start = state.TimeBegan + KeyRepeatDelaySeconds;
                if (now < start || KeyRepeatIntervalSeconds <= 0)
                {
                    continue;
                }

                var due = (int)((now - start) / KeyRepeatIntervalSeconds) + 1;
                if (due > state.RepeatCount)
                {
                    var delta = due - state.RepeatCount;
                    state.RepeatCount = due;
                    Send(state.ActionId, state.MapId, state.Callback, start: true, state.MaxExtent, state.Toggle, 0, _metaMode, delta);
                }
            }
            else
            {
                state.RepeatCount++;
                Send(state.ActionId, state.MapId, state.Callback, start: true, state.MaxExtent, state.Toggle, 0, _metaMode, state.RepeatCount);
            }
        }
    }

    private void Send(uint actionId, uint mapId, IInputActionSink? callback, bool start, float extent, RetailToggleType toggle, uint activation, uint mode, int repeat)
    {
        if (_processingKeyDown)
        {
            _actionDuringKeyDown = true;
        }

        var action = new UiInputAction(mapId, actionId, new RetailActivation(start, extent, toggle, activation & ~EventFlag, mode, repeat));
        SendActionToListeners(action, callback);
    }

    /// <summary>
    /// <c>CInputManager::SendActionToListeners</c> (11.4186 0x004312A0, 11.6096 0x00431440): the map's registrant first; when
    /// it does not take the action, every handler hears it: the element manager's own (<c>UIElementManager::ActionHandler</c>,
    /// 11.4186 0x0045CC00, 11.6096 0x0045CD80), then the host's. True when someone took it.
    /// </summary>
    private bool SendActionToListeners(in UiInputAction action, IInputActionSink? callback)
    {
        if (callback is not null && callback.OnInputAction(action.InputMapId, action.ActionId, action.Activation))
        {
            return true;
        }

        var taken = ActionHandler(action);
        foreach (var handler in _actionHandlers.ToArray())
        {
            taken |= handler.OnInputAction(action.InputMapId, action.ActionId, action.Activation);
        }

        return taken;
    }

    /// <summary>
    /// <c>UIElementManager::ActionHandler</c> over <c>UIElementManager::KeyPressEvent</c> (11.4186 0x0045C300, 11.6096
    /// 0x0045C3E0): the focused element, else the active window, hears the action as a key
    /// (<see cref="UiElement.KeyDown"/>, <see cref="UiElement.KeyUp"/>); when it does not take it, global message 1 (start)
    /// or 2 (end) goes out; a start also runs the visibility-toggle actions.
    /// </summary>
    private bool ActionHandler(in UiInputAction action)
    {
        var target = _manager.FocusElement ?? _manager.ActiveElement;
        bool taken;
        if (action.Activation.Start)
        {
            taken = target is not null && target.KeyDown(action.ActionId, action.Activation.Extent);
            if (!taken)
            {
                _manager.BroadcastGlobalMessage(UiGlobalMessageIds.ActionStart, unchecked((int)action.ActionId));
            }

            taken |= _manager.DoVisibilityToggleAction(action.ActionId);
        }
        else
        {
            taken = target is not null && target.KeyUp(action.ActionId);
            if (!taken)
            {
                _manager.BroadcastGlobalMessage(UiGlobalMessageIds.ActionEnd, unchecked((int)action.ActionId));
            }
        }

        return taken;
    }

    /// <summary>Forgets a deleted element (its maps, characters, mouse state, tooltip).</summary>
    internal void Forget(UiElement element)
    {
        UnregisterCallback(element);
        _characterHandlers.Remove(element);
        ForgetMouse(element);
    }

    private void Log(LogSeverity severity, string message) => _manager.LogOnce(message, severity, message);

    private const uint MoveForward = 0x29;
    private const uint MoveBackward = 0x2A;
    private const uint StopMoving = 0x2B;
    private const uint AutoRun = 0x30;

    /// <summary>A control's last event (<c>CInputManager_WIN32::RecentControlState</c>).</summary>
    private readonly record struct ControlState(bool Down, uint MetaMode, uint Activation, uint ActionId, uint InputMapId, IInputActionSink? Callback, bool Matched, bool Taken);

    /// <summary>An action in progress (<c>ActionState</c>): the controls holding it and who receives it.</summary>
    private sealed class ActionState(uint actionId, uint mapId, IInputActionSink? callback, RetailToggleType toggle, double timeBegan)
    {
        public uint ActionId { get; } = actionId;

        public uint MapId { get; } = mapId;

        public IInputActionSink? Callback { get; set; } = callback;

        public RetailToggleType Toggle { get; } = toggle;

        public double TimeBegan { get; } = timeBegan;

        public int RepeatCount { get; set; }

        public List<(uint Key, float Extent)> Keys { get; } = [];

        /// <summary><c>ActionState::GetExtent</c>: the strongest holding control's extent.</summary>
        public float MaxExtent
        {
            get
            {
                var best = 0f;
                var bestAbs = -1f;
                foreach (var (_, extent) in Keys)
                {
                    if (Math.Abs(extent) > bestAbs)
                    {
                        bestAbs = Math.Abs(extent);
                        best = extent;
                    }
                }

                return best;
            }
        }
    }

    /// <summary>The element manager as the registrant of the UI maps 3 and 13 (<c>UIElementManager::OnAction</c>).</summary>
    private sealed class ManagerCallback(UiInputRouter router) : IInputActionSink
    {
        public bool OnInputAction(uint inputMapId, uint actionId, RetailActivation activation) => router.ManagerOnAction(actionId, activation);
    }
}

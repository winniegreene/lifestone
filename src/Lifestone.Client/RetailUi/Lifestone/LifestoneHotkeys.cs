using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.LifestonePanels;

/// <summary>
/// The keys that open Lifestone's own panels (owner decision D41): <b>F7</b> toggles the tools panel and <b>Scroll Lock</b> opens the automation inspector; and (M4 4.1 lane c) <b>Pause</b> toggles the auto-buff
/// (the re-buff driver, <c>--buff-auto</c>); and (M5 4 h2, D20) <b>Num Lock</b> was the autopilot's toggle until M5 7 D145: the autopilot is started and stopped only by <c>/auto</c> and <c>/stop</c> in the chat now, so Num Lock
/// stays Lifestone's key (bound here, so it reaches nothing of retail's) but only answers with a chat line saying so (<see cref="AutopilotKeyHint"/>). All are keys retail's
/// default game map (KEYMAP <c>gmDefault</c>, MasterInputMap 0x14000000, <c>gmDefaultMap</c>) does not bind to anything, with or without Shift, Ctrl or Alt (and the engine map,
/// KEYMAP <c>Default</c> 0x14000002, binds none of them either): F7, Scroll Lock, Pause and Num Lock are DirectInput scan codes 0x41, 0x46, 0xC5 and 0x45, and F1-F6 and F8-F12 are all bound in <c>gmDefaultMap</c>
/// (<c>LifestoneHotkeysTests</c> proves it over the dats). They were free in M1 too (F7 was the interiors' debug key, dropped in play mode by D41).
/// </summary>
/// <remarks>
/// <para>The keys are bindings of an input map of Lifestone's own (<see cref="InputMap"/>, outside retail's id range), appended to retail's bindings with <see cref="RetailKeyBindings.With"/>,
/// and registered on the router at a priority above the focused text's (a focused text edit does not swallow them). The sink only fires the callbacks: the tools panel's
/// (<see cref="OpenTools"/>) and the automation inspector's (<see cref="OpenInspector"/>, lane B's <c>AutomationInspector.Toggle</c>; <c>TODO(g7-inspector-unwired)</c> is logged when a host wires nothing to it).
/// Game thread only.</para>
/// </remarks>
public sealed class LifestoneHotkeys : IInputActionSink
{
    /// <summary>Lifestone's input map: not one of retail's (0x10000009 and the small numbers), so nothing of retail's is matched or replaced.</summary>
    public const uint InputMap = 0x7F000001;

    /// <summary>The action of F7: toggle the tools panel.</summary>
    public const uint OpenToolsAction = 0x7F000001;

    /// <summary>The action of Scroll Lock: open the automation inspector.</summary>
    public const uint OpenInspectorAction = 0x7F000002;

    /// <summary>The action of Pause: toggle the auto-buff (the re-buff driver; M4 4.1 lane c).</summary>
    public const uint AutoBuffAction = 0x7F000003;

    /// <summary>The auto-buff key: Pause, DirectInput scan code 0xC5 (named in <c>--buff-auto</c>'s help; a key retail's game map does not bind, <c>LifestoneHotkeysTests</c> proves it over the dats).</summary>
    public const int AutoBuffKey = DikScanCodes.Pause;

    /// <summary>The tools key: F7, DirectInput scan code 0x41.</summary>
    public const int ToolsKey = DikScanCodes.F7;

    /// <summary>The inspector key: Scroll Lock, DirectInput scan code 0x46.</summary>
    public const int InspectorKey = DikScanCodes.Scroll;

    /// <summary>The action of Num Lock: the autopilot's toggle from M5 4 h2 (D20) to M5 7 D145; since D145 it does nothing to the autopilot and only answers with a hint (<see cref="AutopilotKeyHint"/>).</summary>
    public const uint AutopilotAction = 0x7F000004;

    /// <summary>M5 7 D145: the hint a Num Lock press shows in the chat (the play host wires <see cref="AutopilotKeyHint"/> to show it).</summary>
    public const string AutopilotKeyHintLine = "Lifestone: Num Lock no longer starts or stops the autopilot; type /auto or /stop in the chat.";

    /// <summary>
    /// The autopilot key: Num Lock, DirectInput scan code 0x45. The first of the candidates D20 ordered that retail's maps leave unbound (the unbound keyboard keys of 0x14000000 and 0x14000002 are the right Shift, Caps Lock,
    /// F7, Num Lock, Scroll Lock, numpad 7/9/1/3, the 102nd key, Sys Rq, Pause and the Apps key; Insert, Home, End, Page Up and Page Down are all bound). <c>LifestoneHotkeysTests</c> proves it over the dats, in every input map.
    /// </summary>
    public const int AutopilotKey = DikScanCodes.NumLock;

    /// <summary>The priority the hotkeys are registered at: above a focused text's maps (<see cref="UiElementManager.FocusInputMapPriority"/>), so typing in the panel's own field does not eat them.</summary>
    public const int Priority = UiElementManager.FocusInputMapPriority + 500;

    /// <summary>The activation retail's function keys use in <c>gmDefaultMap</c> (3: down and up; the binding fires on the press).</summary>
    private const uint FunctionKeyActivation = 3;

    private readonly ILogSink _log;

    /// <summary>The hotkeys' handler; <paramref name="log"/> gets the named TODO of an unwired key.</summary>
    public LifestoneHotkeys(ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <summary>Lifestone's bindings: F7 to <see cref="OpenToolsAction"/>, Scroll Lock to <see cref="OpenInspectorAction"/>, Pause to <see cref="AutoBuffAction"/>, Num Lock to <see cref="AutopilotAction"/>, no meta keys.</summary>
    public static IReadOnlyList<RetailBinding> Bindings { get; } =
    [
        new(InputMap, OpenToolsAction, RetailKeyBindings.Keyboard(ToolsKey), 0, FunctionKeyActivation),
        new(InputMap, OpenInspectorAction, RetailKeyBindings.Keyboard(InspectorKey), 0, FunctionKeyActivation),
        new(InputMap, AutoBuffAction, RetailKeyBindings.Keyboard(AutoBuffKey), 0, FunctionKeyActivation),
        new(InputMap, AutopilotAction, RetailKeyBindings.Keyboard(AutopilotKey), 0, FunctionKeyActivation),
    ];

    /// <summary>Called when F7 is pressed (the tools panel's toggle).</summary>
    public Action? OpenTools { get; set; }

    /// <summary>Called when Scroll Lock is pressed (the inspector's <c>Open</c>); null until the inspector is wired.</summary>
    public Action? OpenInspector { get; set; }

    /// <summary>Called when Pause is pressed (the auto-buff's toggle, wired by the play host); null until the host wires it.</summary>
    public Action? ToggleAutoBuff { get; set; }

    /// <summary>M5 7 D145: called when Num Lock is pressed, with <see cref="AutopilotKeyHintLine"/> (the play host shows it in the chat); null: the press is only logged. It never reaches the autopilot.</summary>
    public Action<string>? AutopilotKeyHint { get; set; }

    /// <summary>How many times Num Lock was pressed.</summary>
    public int AutopilotPresses { get; private set; }

    /// <summary>How many times Pause was pressed.</summary>
    public int AutoBuffPresses { get; private set; }

    /// <summary>How many times F7 was pressed.</summary>
    public int ToolsPresses { get; private set; }

    /// <summary>How many times Scroll Lock was pressed.</summary>
    public int InspectorPresses { get; private set; }

    /// <summary>
    /// Adds <see cref="Bindings"/> to the router's bindings when they are not there yet (<see cref="RetailKeyBindings.With"/>; none is added while the host has set no bindings) and registers the
    /// keys on <paramref name="router"/>.
    /// </summary>
    public void Attach(UiInputRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if (router.Bindings is not { } current)
        {
            _log.Error("lifestone", "F7, Scroll Lock, Pause and Num Lock are registered but the host has set no key bindings on the router yet, so they do nothing until it does (TODO(g7-hotkeys-unbound))");
        }
        else if (!current.Bindings.Contains(Bindings[0]))
        {
            router.Bindings = current.With(Bindings);
        }

        router.RegisterInputMap(InputMap, this, Priority);
    }

    /// <summary>Takes the keys off <paramref name="router"/>.</summary>
    public void Detach(UiInputRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        router.UnregisterInputMap(InputMap, this);
    }

    /// <inheritdoc/>
    public bool OnInputAction(uint inputMapId, uint actionId, RetailActivation activation)
    {
        if (inputMapId != InputMap)
        {
            return false;
        }

        if (!activation.Start)
        {
            return true; // the key's release
        }

        switch (actionId)
        {
            case OpenToolsAction:
                ToolsPresses++;
                if (OpenTools is { } tools)
                {
                    tools();
                }
                else
                {
                    _log.Warn("lifestone", "F7 (the tools panel) was pressed but nothing is wired to it (TODO(g7-tools-hotkey))");
                }

                return true;
            case OpenInspectorAction:
                InspectorPresses++;
                if (OpenInspector is { } inspector)
                {
                    inspector();
                }
                else
                {
                    _log.Warn("lifestone", "Scroll Lock (the automation inspector) was pressed but the inspector is not connected yet (TODO(g7-inspector-unwired): the host wired nothing to OpenInspector)");
                }

                return true;
            case AutoBuffAction:
                AutoBuffPresses++;
                if (ToggleAutoBuff is { } autoBuff)
                {
                    autoBuff();
                }
                else
                {
                    _log.Warn("lifestone", "Pause (the auto-buff toggle) was pressed but nothing is wired to it (the play host wires ToggleAutoBuff through RebuffDriverHost.WireKey; a host without the rebuff driver, such as a test scene or the login-check leg, has none; closed m4-autobuff-key-unwired, M6 6 L3)");
                }

                return true;
            case AutopilotAction:
                AutopilotPresses++;
                _log.Info("lifestone", "Num Lock was pressed: it does nothing to the autopilot since M5 7 D145 (/auto and /stop in the chat are its controls); " + (AutopilotKeyHint is null ? "no hint is wired" : "the hint is shown in the chat"));
                AutopilotKeyHint?.Invoke(AutopilotKeyHintLine);
                return true;
            default:
                return false;
        }
    }
}

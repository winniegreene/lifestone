using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.LifestonePanels;

/// <summary>
/// The two keys that open Lifestone's own panels (owner decision D41): <b>F7</b> toggles the tools panel and <b>Scroll Lock</b> opens the automation inspector. Both are keys retail's
/// default game map (KEYMAP <c>gmDefault</c>, MasterInputMap 0x14000000, <c>gmDefaultMap</c>) does not bind to anything, with or without Shift, Ctrl or Alt (and the engine map,
/// KEYMAP <c>Default</c> 0x14000002, binds neither either): F7 and Scroll Lock are DirectInput scan codes 0x41 and 0x46, and F1-F6 and F8-F12 are all bound in <c>gmDefaultMap</c>
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

    /// <summary>The tools key: F7, DirectInput scan code 0x41.</summary>
    public const int ToolsKey = DikScanCodes.F7;

    /// <summary>The inspector key: Scroll Lock, DirectInput scan code 0x46.</summary>
    public const int InspectorKey = DikScanCodes.Scroll;

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

    /// <summary>Lifestone's bindings: F7 to <see cref="OpenToolsAction"/>, Scroll Lock to <see cref="OpenInspectorAction"/>, no meta keys.</summary>
    public static IReadOnlyList<RetailBinding> Bindings { get; } =
    [
        new(InputMap, OpenToolsAction, RetailKeyBindings.Keyboard(ToolsKey), 0, FunctionKeyActivation),
        new(InputMap, OpenInspectorAction, RetailKeyBindings.Keyboard(InspectorKey), 0, FunctionKeyActivation),
    ];

    /// <summary>Called when F7 is pressed (the tools panel's toggle).</summary>
    public Action? OpenTools { get; set; }

    /// <summary>Called when Scroll Lock is pressed (the inspector's <c>Open</c>); null until the inspector is wired.</summary>
    public Action? OpenInspector { get; set; }

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
            _log.Error("lifestone", "F7 and Scroll Lock are registered but the host has set no key bindings on the router yet, so they do nothing until it does (TODO(g7-hotkeys-unbound))");
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
            default:
                return false;
        }
    }
}

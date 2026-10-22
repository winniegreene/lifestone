using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;

namespace Lifestone.Client.RetailUi.Panels;

// M4 2.2 s2 (the container window). This file is the slice's alone. gmExternalContainerUI (0x1000000D) is a window of the gameplay layout, not a host page: it lives in gmFloatyEnvPanelUI (0x1000004E, window 0x100005FD),
// over the world view. Retail's classes are elements of their own, so they are registered here as elements (GameContainerPanel, UiFloatyEnvPanel); the controller below only hands the window the session. Neither is an
// IGamePanel (a window that is not a host page has no panel id to register under), and the window ends with its element (UiElement.OnDeleting unsubscribes it), so GamePanels.Dispose has nothing to do for it and the
// HandlerCount test holds.
public sealed partial class GamePanels
{
    // The UI element classes the slice's windows need (UiClassRegistry.Register), called from GamePlayScreen.RegisterClasses after the plain containers.
    private static partial void RegisterD1Classes(UiClassRegistry registry)
    {
        registry.Register(GameContainerPanel.RetailClassId, static (in UiElementInit init) => new GameContainerPanel(init)); // gmExternalContainerUI::Register: address unknown (TODO(m4-retail-container-window))
        registry.Register(GameVendorPanel.RetailClassId, static (in UiElementInit init) => new GameVendorPanel(init)); // gmVendorUI::Register 11.6096 0x004C3BF0 (M4 3.2 s4b)
        registry.Register(UiFloatyEnvPanel.RetailClassId, static (in UiElementInit init) => new UiFloatyEnvPanel(init)); // gmFloatyEnvPanelUI::Register: address unknown
    }

    // One Queue call per controller; each is built in a staged unit of its own (under 8 ms), after the shell's other controllers.
    private partial void QueueD1()
    {
        Queue(static p =>
        {
            if (p.Screen.Root?.GetChildRecursive(GameContainerPanel.WindowId) is GameContainerPanel window)
            {
                window.Attach(p.Context);
            }
            else
            {
                p.Context.Manager.Services.Log.Error(UiElementManager.Category, "gmExternalContainerUI: the gameplay layout has no container window 0x1000005D; there is no container window (TODO(m4-container-window-missing))");
            }

            return null;
        });

        // M4 3.2 s4b: the vendor window (gmVendorUI, window 0x10000062 of the same floaty) follows the vendor model.
        Queue(static p =>
        {
            if (p.Screen.Root?.GetChildRecursive(GameVendorPanel.WindowId) is GameVendorPanel window)
            {
                window.Attach(p.Context);
            }
            else
            {
                p.Context.Manager.Services.Log.Error(UiElementManager.Category, "gmVendorUI: the gameplay layout has no vendor window 0x10000062; there is no vendor window (TODO(m4-vendor-window-missing))");
            }

            return null;
        });
    }
}

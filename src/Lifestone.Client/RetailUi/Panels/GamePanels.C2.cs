using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;

namespace Lifestone.Client.RetailUi.Panels;

// Slice pb (the spellcasting panel), docs/milestones/m3-plan.md section 4.8 row "pb". This file is the slice's alone (M3 3.2 s0's seam).
// gmSpellcastingUI (0x10000015) is a floaty window of the gameplay layout, not a host page. Retail's class is an element of its own, so it is registered here as one (GameSpellcastingPanel) and replaces s0's plain
// container; the controller below only hands it the session, the object selection and the chat. It is not an IGamePanel (a window that is not a host page has no panel id to register under) and ends with its element
// (UiElement.OnDeleting unsubscribes it), so GamePanels.Dispose has nothing to do for it. M5 6 s1 adds the melee and missile key maps (CombatMapKeys) here, after C1 attached the combat window, held by its combat key. Its holding floaty (gmFloatyCombatPanelUI 0x10000054) is pa's UiFloatyCombatPanel and is not registered again here.
public sealed partial class GamePanels
{
    // The UI element classes the slice's window needs (UiClassRegistry.Register), called from GamePlayScreen.RegisterClasses after the plain containers.
    private static partial void RegisterC2Classes(UiClassRegistry registry)
    {
        registry.Register(GameSpellcastingPanel.RetailClassId, static (in UiElementInit init) => new GameSpellcastingPanel(init)); // gmSpellcastingUI::Register (11.6096 0x004C6C20)
    }

    // One Queue call per controller; each is built in a staged unit of its own (under 8 ms), after the shell's other controllers.
    private partial void QueueC2()
    {
        Queue(static p =>
        {
            if (p.Screen.Root?.GetChildRecursive(GameSpellcastingPanel.WindowId) is GameSpellcastingPanel window)
            {
                var screen = p.Screen;
                window.SelectedProvider = () => screen.SelectionUi?.SelectedProvider?.Invoke() ?? 0; // read when a targeted spell is cast; the play mode gives the selection its provider
                window.ChatNotice = screen.Chat is { } chat ? chat.Local : null; // ECM_UI::SendNotice_DisplayStringInfo's line
                window.Attach(p.Context);
            }
            else
            {
                p.Context.Manager.Services.Log.Error(UiElementManager.Category, "gmSpellcastingUI: the gameplay layout has no spellcasting window 0x10000061; there is no spellcasting panel (TODO(m3-spellcast-window-missing))");
            }

            // The melee and missile maps of ClientCombatSystem::RegisterInputMaps (11.6096 0x0056B760; M5 6 s1): the combat window was attached by C1's unit, so its attack side is there; the combat key's registration owns them.
            if (p.Screen.Root?.GetChildRecursive(GameCombatPanel.WindowId) is GameCombatPanel { ToggleKey: { } toggle } combat)
            {
                toggle.ModeMaps = new CombatMapKeys(p.Context.Session, p.Context.Manager, combat);
            }

            return null;
        });
    }
}

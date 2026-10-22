using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;

namespace Lifestone.Client.RetailUi.Panels;

// Slice pa (the combat panel and the power bar), docs/milestones/m3-plan.md section 4.8 row "pa". This file is the slice's alone (M3 3.2 s0's seam).
// gmCombatUI (0x1000000C) and gmPowerbarUI (0x1000000F) are floaty windows of the gameplay layout, not host pages. Retail's classes are elements of their own, so they are registered here as
// elements (GameCombatPanel, GamePowerBar) that replace s0's plain containers; the controllers below only hand them the session. Neither is an IGamePanel: a window that is not a host page has no panel id
// to register under, and each ends with its element (UiElement.OnDeleting unsubscribes it), so GamePanels.Dispose has nothing to do for them.
public sealed partial class GamePanels
{
    // The UI element classes the slice's windows need (UiClassRegistry.Register), called from GamePlayScreen.RegisterClasses after the plain containers.
    private static partial void RegisterC1Classes(UiClassRegistry registry)
    {
        registry.Register(GameCombatPanel.RetailClassId, static (in UiElementInit init) => new GameCombatPanel(init)); // gmCombatUI::Register (11.6096 0x004CD000)
        registry.Register(GamePowerBar.RetailClassId, static (in UiElementInit init) => new GamePowerBar(init)); // gmPowerbarUI::Register (11.6096 0x004DB190)

        // gmFloatyCombatPanelUI (0x10000054, Register 11.6096 0x004D5CE0) holds the combat window and the spellcasting window (pb's): it shows while one of them does. gmFloatyPowerBarUI (0x10000053) is the
        // movable floaty of the advanced combat UI; the gameplay layout has no window of its class, so a plain container only keeps the registry quiet.
        registry.Register(UiFloatyCombatPanel.RetailClassId, static (in UiElementInit init) => new UiFloatyCombatPanel(init));
        registry.Register(0x10000053, static (in UiElementInit init) => new UiElement(init));
    }

    // One Queue call per controller; each is built in a staged unit of its own (under 8 ms), after the shell's other controllers.
    private partial void QueueC1()
    {
        Queue(static p =>
        {
            var root = p.Screen.Root;
            var toolbar = p.Screen.ToolbarUi;
            if (toolbar is not null && p.Screen.Chat is { } chat)
            {
                toolbar.ChatNotice = chat.Local; // GetDefaultCombatMode's "You can't enter combat mode while wielding ..." line
            }

            if (root?.GetChildRecursive(GameCombatPanel.WindowId) is GameCombatPanel combat)
            {
                combat.SelectedProvider = () => p.Screen.SelectionUi?.Selected ?? 0u; // the target of a click on an attack button (M4 1.3)
                combat.ChatNotice = toolbar?.ChatNotice;
                combat.Attach(p.Context, p.Screen.Options, toolbar);
            }
            else
            {
                p.Context.Manager.Services.Log.Error(UiElementManager.Category, "gmCombatUI: the gameplay layout has no combat window 0x1000005C; there is no combat panel (TODO(m3-combat-window-missing))");
            }

            if (root?.GetChildRecursive(GamePowerBar.WindowId) is GamePowerBar powerBar)
            {
                powerBar.Attach(p.Context);
            }
            else
            {
                p.Context.Manager.Services.Log.Error(UiElementManager.Category, "gmPowerbarUI: the gameplay layout has no power bar 0x10000044; there is no power bar (TODO(m3-powerbar-window-missing))");
            }

            return null;
        });
    }
}

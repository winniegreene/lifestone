using Lifestone.Client.RetailUi.Core;

namespace Lifestone.Client.RetailUi.Panels;

// Slice a2 (Skills tab (skill page 11)), docs/milestones/m3-plan.md section 4.6 row "a2". This file is the slice's alone (M3 2.2 s0's seam): fill the two hooks and edit nothing else of GamePanels.
public sealed partial class GamePanels
{
    // The UI element classes the slice's page needs (UiClassRegistry.Register), called from GamePlayScreen.RegisterClasses.
    private static partial void RegisterA2Classes(UiClassRegistry registry)
    {
        // gmSkillUI (0x1000002B) is a plain container here: its behaviour (the list, the footers, the buttons) is GameSkillsTab's, which hears the element's messages, so the class needs no generic
        // stand-in warning (UiClassRegistry.Create logs "TODO(retailui-runtime)" for a retail class without a factory).
        registry.Register(GameSkillsTab.ClassId, static (in UiElementInit init) => new UiElement(init));
    }

    // One Queue call per controller, e.g. Queue(static p => new GameSkillsTab(p)); each is built in a staged unit of its own (under 8 ms), after the shell's other controllers.
    private partial void QueueA2()
    {
        Queue(static p => new GameSkillsTab(p.Context, p.Host));
    }
}

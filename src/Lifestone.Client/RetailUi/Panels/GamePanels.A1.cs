using Lifestone.Client.RetailUi.Core;

namespace Lifestone.Client.RetailUi.Panels;

// Slice a1 (Attributes tab (skill page 11)), docs/milestones/m3-plan.md section 4.6 row "a1". This file is the slice's alone (M3 2.2 s0's seam): fill the two hooks and edit nothing else of GamePanels.
public sealed partial class GamePanels
{
    // The UI element classes the slice's page needs (UiClassRegistry.Register), called from GamePlayScreen.RegisterClasses.
    private static partial void RegisterA1Classes(UiClassRegistry registry)
    {
        // gmAttributeUI::Register (11.6096 0x0049CD10) and gmCharacterTitleUI: the tabs' pages are plain windows that GameAttributesTab drives as a controller (the character titles tab shows the layout's art only).
        registry.Register(GameAttributesTab.AttributeClassId, static (in UiElementInit init) => new UiElement(init));
        registry.Register(GameAttributesTab.TitlesClassId, static (in UiElementInit init) => new UiElement(init));
    }

    // One Queue call per controller, e.g. Queue(static p => new Game...(p)); each is built in a staged unit of its own (under 8 ms), after the shell's other controllers.
    private partial void QueueA1()
    {
        Queue(static p => GameAttributesTab.Create(p));
    }
}

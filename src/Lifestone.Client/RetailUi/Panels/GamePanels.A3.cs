using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Indicators;

namespace Lifestone.Client.RetailUi.Panels;

// Slice a3 (Effects pages 4 and 5, vitae page 15, indicators), docs/milestones/m3-plan.md section 4.6 row "a3". This file is the slice's alone (M3 2.2 s0's seam): fill the two hooks and edit nothing else of GamePanels.
public sealed partial class GamePanels
{
    // The UI element classes the slice's pages and indicators need (UiClassRegistry.Register), called from GamePlayScreen.RegisterClasses.
    private static partial void RegisterA3Classes(UiClassRegistry registry)
    {
        // gmEffectsUI (0x1000001B) and gmVitaeUI (0x10000020) are plain containers here: their behaviour (the list, the info box, the text) is GameEffectsPage's and GameVitaePage's, which hear the element's
        // messages (the class needs no generic stand-in warning).
        registry.Register(GameEffectsPage.ClassId, static (in UiElementInit init) => new UiElement(init));
        registry.Register(GameVitaePage.ClassId, static (in UiElementInit init) => new UiElement(init));

        // The effects (0x10000002), vitae (0x10000006) and burden (0x10000001) indicators are buttons whose state GameIndicators sets
        // (gmUIElement_EffectsIndicator::Register 11.6096 0x004E76C0, gmUIElement_VitaeIndicator::Register 0x004E6C50, gmUIElement_BurdenIndicator::Register 0x004E7950).
        registry.Register(UiStateIndicator.EffectsClassId, static (in UiElementInit init) => new UiStateIndicator(init));
        registry.Register(UiStateIndicator.VitaeClassId, static (in UiElementInit init) => new UiStateIndicator(init));
        registry.Register(UiStateIndicator.BurdenClassId, static (in UiElementInit init) => new UiStateIndicator(init));
    }

    // One Queue call per controller, e.g. Queue(static p => new GameSkillsTab(p)); each is built in a staged unit of its own (under 8 ms), after the shell's other controllers.
    private partial void QueueA3()
    {
        Queue(static p => new GameEffectsPage(p.Context, p.Host, GameEffectsPage.BeneficialPanelId));
        Queue(static p => new GameEffectsPage(p.Context, p.Host, GameEffectsPage.HarmfulPanelId));
        Queue(static p => new GameVitaePage(p.Context, p.Host));
    }
}

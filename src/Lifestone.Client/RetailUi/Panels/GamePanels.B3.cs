using Lifestone.Client.RetailUi.Core;

namespace Lifestone.Client.RetailUi.Panels;

// Slice b3 (Spellbook and components (page 13)), docs/milestones/m3-plan.md section 4.6 row "b3". This file is the slice's alone (M3 2.2 s0's seam).
public sealed partial class GamePanels
{
    // gmSpellbookUI (0x1000002E) and gmSpellComponentUI (0x1000002F) are plain containers here: their behaviour (the item list, the filter buttons, the component rows) is GameSpellbookPage's and GameComponentsPage's,
    // which hear the tab elements' messages, so the classes need no generic stand-in warning (UiClassRegistry.Create logs "TODO(retailui-runtime)" for a retail class without a factory). The page's panel
    // (UIElement_Panel) and the item list (UIElement_ItemList, b1) are registered already.
    private static partial void RegisterB3Classes(UiClassRegistry registry)
    {
        registry.Register(GameSpellbookPage.ClassId, static (in UiElementInit init) => new UiElement(init));
        registry.Register(GameComponentsPage.ClassId, static (in UiElementInit init) => new UiElement(init));
    }

    // The spell page (panel 13): the spellbook tab first (retail's default tab), then the components tab. Each is built in a staged unit of its own; the lists fill when the page first opens.
    private partial void QueueB3()
    {
        Queue(static p => GameSpellbookPage.Create(p.Context, p.Host));
        Queue(static p => GameComponentsPage.Create(p.Context, p.Host));
    }
}

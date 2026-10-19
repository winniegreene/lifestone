using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Widgets;

namespace Lifestone.Client.RetailUi.Panels;

// Slice b1 (Item widgets and the inventory page 7), docs/milestones/m3-plan.md section 4.6 row "b1". This file is the slice's alone (M3 2.2 s0's seam).
public sealed partial class GamePanels
{
    // UIElement_ItemList (0x10000031) and UIElement_UIItem (0x10000032): the pack, the side packs and (b2, b3) the paperdoll's slots and the spell lists use them.
    private static partial void RegisterB1Classes(UiClassRegistry registry)
    {
        registry.Register(UiItemList.RetailClassId, static (in UiElementInit init) => new UiItemList(init));
        registry.Register(UiItem.RetailClassId, static (in UiElementInit init) => new UiItem(init));

        // gmBackpackUI (0x10000022) and gmInventoryUI (0x10000023) are plain containers here (M3 2.2 fix B, 2.2-R13): their behaviour is GameInventoryPage's, which hears the lists' and the layout's messages, so the classes need no
        // generic stand-in warning ("TODO(retailui-runtime): class ... has no Lifestone runtime yet", which UiClassRegistry.Create logs for a retail class without a factory).
        registry.Register(0x10000022, static (in UiElementInit init) => new UiElement(init));
        registry.Register(0x10000023, static (in UiElementInit init) => new UiElement(init));
    }

    // The inventory page (panel 7): one controller, built when the shell is wired; it draws when the page first shows.
    private partial void QueueB1()
    {
        Queue(static p =>
        {
            var page = GameInventoryPage.Create(p.Context, p.Host);
            if (page is not null && p.Screen.Chat is { } chat)
            {
                page.HoldNotice = chat.Local; // AutoWieldIsLegal's "Cannot hold ... while in combat" line
            }

            return page;
        });
    }
}

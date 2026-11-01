using Lifestone.Client.RetailUi.Core;

namespace Lifestone.Client.RetailUi.Panels;

// Slice b2 (the paperdoll of inventory page 7), docs/milestones/m3-plan.md section 4.6 row "b2". This file is the slice's alone (M3 2.2 s0's seam).
public sealed partial class GamePanels
{
    // The paperdoll's slots are UIElement_ItemList (0x10000031) and UIElement_UIItem (0x10000032) cells: slice b1 registers both classes. gmPaperDollUI (0x10000024) is a plain container here (M3 2.2 fix B, 2.2-R13): its
    // behaviour is GamePaperDoll's, which hears the slots' messages, so the class needs no generic stand-in warning (UiClassRegistry.Create logs "TODO(retailui-runtime)" for a retail class without a factory).
    private static partial void RegisterB2Classes(UiClassRegistry registry)
    {
        registry.Register(0x10000024, static (in UiElementInit init) => new UiElement(init));
    }

    // The paperdoll (gmPaperDollUI, inside the inventory page 7): a second controller of that page, built when the shell is wired; it draws when the page first shows.
    private partial void QueueB2()
    {
        Queue(static p =>
        {
            var doll = GamePaperDoll.Create(p.Context, p.Host);
            if (doll is not null)
            {
                doll.SelectRequester = guid => p.Screen.SelectionUi?.SelectRequester?.Invoke(guid); // the figure's left click (M5 7 D122)
                if (p.Screen.Chat is { } chat)
                {
                    doll.FigureDrop.Notice = chat.Local; // "You can't put that item there"
                }
            }

            return doll;
        });
    }
}

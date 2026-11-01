using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The slots' tooltips (M5 item 7 D122, closes <c>TODO(m3-paperdoll-tooltips)</c>): <c>gmPaperDollUI::UpdateItemSlotTooltip</c>
/// (11.6096 0x004A5200), called by <c>SetUIItemIntoLocation</c> (0x004A6300) for each slot it fills or empties, sets the slot
/// cell's tooltip: an empty slot names what it takes ("Drag rings here to wear them"); a filled one names its item, worn or
/// wielded, and how to take it off ("Wand (wielded)" over "Double-click to unwield"). The weapon, ammunition, shield, trinket,
/// cloak and sigil slots wield; the jewellery, clothing and armour slots are worn.
/// </summary>
public sealed partial class GamePaperDoll
{
    /// <summary>Each slot's empty-slot tooltip and whether its item is worn (else wielded), in <see cref="SlotTable"/>'s order.</summary>
    private static readonly (string Empty, bool Worn)[] SlotTips =
    [
        ("Drag necklaces here to wear them", true),
        ("Drag bracelets here to wear them", true),
        ("Drag bracelets here to wear them", true),
        ("Drag rings here to wear them", true),
        ("Drag rings here to wear them", true),
        ("Drag weapons here to wield them", false),
        ("Drag shields here to wield them", false),
        ("Drag missile ammunition here to wield it", false),
        ("Drag clothing items here to wear them", true),
        ("Drag clothing items here to wear them", true),
        ("Drag trinkets here to activate them", false),
        ("Drag cloaks here to activate them", false),
        ("Drag a Blue Aetheria sigil here to activate it", false),
        ("Drag a Yellow Aetheria sigil here to activate it", false),
        ("Drag a Red Aetheria sigil here to activate it", false),
        ("Drag head items here to wear them", true),
        ("Drag chest items here to wear them", true),
        ("Drag abdomen items here to wear them", true),
        ("Drag upper arm items here to wear them", true),
        ("Drag lower arm items here to wear them", true),
        ("Drag glove items here to wear them", true),
        ("Drag upper leg items here to wear them", true),
        ("Drag lower leg items here to wear them", true),
        ("Drag foot coverings here to wear them", true),
    ];

    /// <summary>The tooltip of slot <paramref name="index"/> (of <see cref="SlotTable"/>) showing item <paramref name="name"/>, or empty when it is null.</summary>
    public static string SlotTooltip(int index, string? name)
    {
        var (empty, worn) = SlotTips[index];
        return name is null ? empty : $"{name} ({(worn ? "worn" : "wielded")})\nDouble-click to {(worn ? "take off" : "unwield")}";
    }

    /// <summary><c>UpdateItemSlotTooltip</c>: the slot cell's tooltip for what it shows now.</summary>
    private static void UpdateItemSlotTooltip(UiItem cell, int index, string? name) => cell.SetTooltip(UiStringRef.FromLiteral(SlotTooltip(index, name)));
}

using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// What the spell page (M3 2.2 slice b3) reads from the session as plain values, so the retail UI never names a <c>Lifestone.Net</c> type (the architecture test forbids it): a world object's weenie class and whether it is a
/// spell component, the desired amount of a component, and the retail category a component is listed under. Additive; nothing here changes the types it extends.
/// </summary>
public static class SpellPageData
{
    /// <summary>The ItemType bit of a spell component (<c>ITEM_TYPE_SPELL_COMPONENTS</c> 0x1000): the pack items <c>gmSpellComponentUI</c> counts.</summary>
    public const uint ItemTypeSpellComponent = 0x1000;

    /// <summary>The number of component categories the page lists (<c>gmSpellComponentUI::PostInit</c> fills seven titles).</summary>
    public const int CategoryCount = 7;

    /// <summary>The weenie class id of <paramref name="obj"/> (CreateObject's WeenieDesc), which is the key of a component's row and of its desired amount.</summary>
    public static uint WeenieClassOf(this WorldObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return obj.Weenie.WeenieClassId;
    }

    /// <summary>True when <paramref name="obj"/> is a spell component (its ItemType holds <see cref="ItemTypeSpellComponent"/>).</summary>
    public static bool IsSpellComponent(this WorldObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return (obj.ItemType & ItemTypeSpellComponent) != 0;
    }

    /// <summary>
    /// The amount of the component with weenie class id <paramref name="wcid"/> the character wants to rebuy (the description's desired components: ACE sends the weenie class id and the amount;
    /// <c>PlayerModule::GetDesiredCompLevel</c>, 11.6096), or 0 when the book holds none.
    /// </summary>
    /// <param name="book">The spellbook.</param>
    /// <param name="wcid">The component's weenie class id.</param>
    public static int DesiredComponentAmount(this Spellbook book, uint wcid)
    {
        ArgumentNullException.ThrowIfNull(book);
        var desired = book.DesiredComponents;
        for (var i = 0; i < desired.Length; i++)
        {
            if (desired[i].ComponentId == wcid)
            {
                return desired[i].Amount;
            }
        }

        return 0;
    }

    /// <summary>How many desired-component entries the book holds (a test and the play check read it).</summary>
    public static int DesiredComponentCount(this Spellbook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return book.DesiredComponents.Length;
    }

    /// <summary>
    /// The category a component is listed under, 0 to 6 in retail's order (scarabs, herbs, gems, alchemical, talismans, tapers, peas: <c>gmSpellComponentUI::PostInit</c> 11.4186 0x00489FE0, which fills the seven titles in that
    /// order), for the component with weenie class id <paramref name="wcid"/>. The pinned map (<see cref="ComponentWeenies"/>) gives the component id and the id gives the category (a test pins the ranges against the
    /// SpellComponentTable's types). A weenie the map does not hold is a "pea" (category 6) and <paramref name="known"/> is false: the caller logs the named TODO.
    /// </summary>
    /// <param name="wcid">The weenie class id.</param>
    /// <param name="componentId">The SpellComponentTable id (the sort key within a category), or <see cref="uint.MaxValue"/> when the map lacks the weenie.</param>
    /// <param name="known">True when the pinned map holds the weenie.</param>
    public static int CategoryOf(uint wcid, out uint componentId, out bool known)
    {
        if (!ComponentWeenies.TryGetByWcid(wcid, out var component))
        {
            componentId = uint.MaxValue;
            known = false;
            return CategoryCount - 1;
        }

        componentId = component.ComponentId;
        known = true;
        return CategoryOfComponent(component.ComponentId);
    }

    /// <summary>The category of the SpellComponentTable id <paramref name="componentId"/> (see <see cref="CategoryOf"/>).</summary>
    public static int CategoryOfComponent(uint componentId) => componentId switch
    {
        >= 1 and <= 6 or 110 or 112 or 193 => 0,
        >= 7 and <= 24 => 1,
        >= 25 and <= 36 => 2,
        >= 37 and <= 48 or 111 or 187 => 3, // 111 Chorizite and 187 Moo Juice are potions in the dat
        >= 49 and <= 62 or 190 => 4,
        >= 63 and <= 74 or 188 => 5,
        _ => 6,
    };
}

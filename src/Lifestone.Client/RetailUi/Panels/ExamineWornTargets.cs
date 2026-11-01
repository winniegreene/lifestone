using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>Which worn item <c>--examine-worn-frames</c> inspects (M5 7 D142).</summary>
public enum ExamineWornKind
{
    /// <summary>The wielded caster (ItemType 0x8000): Hermetic Link raises its mana conversion line.</summary>
    Caster,

    /// <summary>The worn robe (a worn armour or clothing piece named "Robe", else the first worn armour or clothing piece): Impenetrability raises its armour level, the banes its protections.</summary>
    Robe,
}

/// <summary>
/// The worn items the examine-worn review frames inspect (M5 7 D142, <c>--examine-worn-frames=DIR</c>): the player's wielded caster and worn robe, read from the world table (an object whose WielderId is the player), and whether an
/// answer's page shows the enchantment colours the frame is for.
/// </summary>
public static class ExamineWornTargets
{
    /// <summary>ItemType Caster (ACE <c>ItemType.Caster</c>).</summary>
    public const uint CasterItemType = 0x8000;

    /// <summary>ItemType Armor | Clothing (ACE <c>ItemType.Armor</c> 0x2, <c>ItemType.Clothing</c> 0x4).</summary>
    public const uint WornItemTypes = 0x2 | 0x4;

    /// <summary>
    /// The worn item of <paramref name="kind"/> among <paramref name="objects"/> that <paramref name="self"/> wields, or null when it wears none: the first caster; for the robe the first armour or clothing piece whose name holds "Robe"
    /// (the Faran Robe with Hood), else the first armour or clothing piece in guid order.
    /// </summary>
    public static WorldObject? Pick(IEnumerable<WorldObject> objects, uint self, ExamineWornKind kind)
    {
        ArgumentNullException.ThrowIfNull(objects);
        var worn = objects.Where(o => self != 0 && o.WielderId == self).OrderBy(static o => o.Guid).ToList();
        if (kind == ExamineWornKind.Caster)
        {
            return worn.FirstOrDefault(static o => (o.ItemType & CasterItemType) != 0);
        }

        var pieces = worn.Where(static o => (o.ItemType & WornItemTypes) != 0).ToList();
        return pieces.FirstOrDefault(static o => o.Name.Contains("Robe", StringComparison.OrdinalIgnoreCase)) ?? pieces.FirstOrDefault();
    }

    /// <summary>True when the page has a line drawn raised or lowered (the frame shows what it is for).</summary>
    public static bool ShowsEnchantment(IReadOnlyList<int> lineColors)
    {
        ArgumentNullException.ThrowIfNull(lineColors);
        return lineColors.Any(static c => c is ExamineColor.Raised or ExamineColor.Lowered);
    }
}

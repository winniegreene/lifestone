using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// The keep-aside rule (M5 7 F30, the owner): the loot the level-80 mage keeps whatever the value, burden and payout floors say, and never sells. Two kinds, from the ground's <see cref="GroundKeepAside"/> block:
/// (a) a caster whose War Magic damage bonus (ACE PropertyFloat <c>ElementalDamageMod</c>, 152; 1.03 is +3%) reaches the block's minimum; (b) a wearable, jewellery or caster that casts a cantrip the block names ("Major Focus").
/// Pure: it reads an item's type and its appraisal (the spells and the float come from an appraisal only, so an item with none cannot match: <see cref="NeedsAppraisal"/> says which items to ask about).
/// </summary>
public sealed class KeepAsideRule
{
    /// <summary>ACE's ItemType Armor (0x2), Clothing (0x4) and Jewelry (0x8): the wearables.</summary>
    public const uint WearableTypes = 0x2 | 0x4 | 0x8;

    /// <summary>ACE's ItemType Caster (0x8000): the wands, staffs, orbs, sceptres and batons.</summary>
    public const uint CasterType = 0x8000;

    private readonly HashSet<string> _names;

    /// <summary>A rule from a profile's block.</summary>
    /// <param name="setting">The block.</param>
    public KeepAsideRule(GroundKeepAside setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        Setting = setting;
        _names = new HashSet<string>(setting.SpellNames(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The block the rule runs.</summary>
    public GroundKeepAside Setting { get; }

    /// <summary>The cantrip spell names the rule keeps for.</summary>
    public IReadOnlyCollection<string> SpellNames => _names;

    /// <summary>True when an item of this type could match (a caster, or a wearable or jewellery piece): only those are appraised for the rule.</summary>
    /// <param name="itemType">The item's ItemType.</param>
    public static bool NeedsAppraisal(uint itemType) => (itemType & (WearableTypes | CasterType)) != 0;

    /// <summary>The war bonus in percent an appraisal reads (the float 1.03 is 3), or null when it has none.</summary>
    /// <param name="appraisal">The appraisal.</param>
    public static double? WarBonusPercent(Appraisal appraisal)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        return appraisal.Float(AppraisalIds.ElementalDamageMod) is { } mod ? Math.Round((mod - 1.0) * 100.0, 4) : null;
    }

    /// <summary>The rule's family names that the spell table does not hold under any tier of the block (a typo in the profile would otherwise keep nothing, silently).</summary>
    /// <param name="spells">The SpellTable's spells.</param>
    public IEnumerable<string> NamesMissingFrom(IReadOnlyDictionary<uint, SpellInfo> spells)
    {
        ArgumentNullException.ThrowIfNull(spells);
        var held = new HashSet<string>(spells.Values.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        return _names.Where(n => !held.Contains(n)).Order(StringComparer.Ordinal);
    }

    /// <summary>
    /// Why the item is kept aside, or null when it is not (or when nothing is known: no appraisal, no match). "War Magic bonus +3%" for a caster over the bonus floor; "Major Focus, Major Willpower" for the cantrips it casts.
    /// </summary>
    /// <param name="itemType">The item's ItemType.</param>
    /// <param name="appraisal">The appraisal of the item, or null (a failed or not-found one matches nothing).</param>
    /// <param name="spellName">The name of a spell id (the SpellTable's), or null for an id it does not know.</param>
    public string? Match(uint itemType, Appraisal? appraisal, Func<uint, string?> spellName)
    {
        ArgumentNullException.ThrowIfNull(spellName);
        if (appraisal is null || appraisal.IsNotFound || !NeedsAppraisal(itemType))
        {
            return null;
        }

        string? bonus = null;
        if ((itemType & CasterType) != 0 && WarBonusPercent(appraisal) is { } percent && percent + 1e-6 >= Setting.MinWarBonusPercent)
        {
            bonus = string.Create(CultureInfo.InvariantCulture, $"War Magic bonus +{percent:0.##}%");
        }

        var cantrips = new List<string>();
        foreach (var id in appraisal.Spells)
        {
            if (spellName(id) is { } name && _names.Contains(name) && !cantrips.Contains(name))
            {
                cantrips.Add(name);
            }
        }

        if (bonus is null)
        {
            return cantrips.Count == 0 ? null : string.Join(", ", cantrips);
        }

        return cantrips.Count == 0 ? bonus : bonus + "; " + string.Join(", ", cantrips);
    }
}

/// <summary>
/// The wands and cantrip pieces the keep-aside rule has taken over the process (M5 7 F30), for the soak line's <c>worth_a_look=</c>; report-only. The guids themselves are <see cref="Lifestone.Client.Session.Scenarios.Journey.SaleMemory.KeptAside"/>, which the sale reads.
/// </summary>
public static class KeepAsideTally
{
    private static long _looks;

    /// <summary>Items taken as worth a look over the process.</summary>
    public static long WorthALook => Interlocked.Read(ref _looks);

    /// <summary>Counts one more item.</summary>
    public static void Add() => Interlocked.Increment(ref _looks);
}

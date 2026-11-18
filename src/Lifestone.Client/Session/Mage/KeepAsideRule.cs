using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// The keep-aside rule (M5 7 F30, the owner): the loot the level-80 mage keeps whatever the value, burden and payout floors say, and never sells. Two kinds, from the ground's <see cref="GroundKeepAside"/> block:
/// (a) a caster whose War Magic damage bonus (ACE PropertyFloat <c>ElementalDamageMod</c>, 152; 1.03 is +3%) reaches the block's minimum; (b) a wearable, jewellery or caster that casts a cantrip the block names ("Major Focus").
/// M6 14 useful cantrips (the owner, 2026-11-16): (b) is now a great cantrip of the block's tiers, any family, that <see cref="CantripJudge"/> calls useful (a basic attribute, or a skill she holds trained or specialised or her plan
/// trains); the block's names are still read and checked against the SpellTable, but no longer decide the keep.
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

    /// <summary>
    /// M6 14 (<c>m6-lore-cantrips</c>; the owner, 2026-11-14: save the pieces with great cantrips, Major and above, even while her Arcane Lore cannot light them, because the lore will rise): the climber's keep-aside wherever a ground's profile has no
    /// <c>keep_aside</c> block. The block of <c>black-spawn-den-aluvian.json</c> (M5 7 F30, the owner's for the level-80 mage; its source names every family): a caster with a War Magic bonus of +3% or more, and any piece casting a Major, Epic or
    /// Legendary War, Life, Creature or Item Enchantment Aptitude, Focus, Willpower, Mana Conversion Prowess, Arcane Prowess, Magic Resistance, Invulnerability or Impregnability. What she keeps is hers, not the ground's: the profiles hold facts
    /// only, so the default is not copied into each; a profile's own block still wins (<see cref="SettingFor"/>). <c>KeepAsideRuleTests</c> pins it to the den's block.
    /// </summary>
    public static GroundKeepAside ClimberDefault { get; } = new(
        3,
        ["Major", "Epic", "Legendary"],
        ["War Magic Aptitude", "Life Magic Aptitude", "Creature Enchantment Aptitude", "Item Enchantment Aptitude", "Focus", "Willpower", "Mana Conversion Prowess", "Arcane Prowess", "Magic Resistance", "Invulnerability", "Impregnability"]);

    /// <summary>M6 14 (<c>m6-lore-cantrips</c>): the keep-aside a ground without a block gives <paramref name="character"/>: <see cref="ClimberDefault"/> for the climber, none for any other.</summary>
    /// <param name="character">The character's name, or null.</param>
    public static GroundKeepAside? DefaultFor(string? character) => CharacterPolicy.For(character).KeepAsideDefault;

    /// <summary>M6 14 (<c>m6-lore-cantrips</c>): the keep-aside in force on <paramref name="ground"/> for <paramref name="character"/>: the profile's own block, else the character's default (<see cref="DefaultFor"/>), else none.</summary>
    /// <param name="ground">The ground.</param>
    /// <param name="character">The character's name, or null.</param>
    public static GroundKeepAside? SettingFor(HuntingGround ground, string? character)
    {
        ArgumentNullException.ThrowIfNull(ground);
        return ground.Loot?.KeepAside ?? DefaultFor(character);
    }

    /// <summary>M6 14 (<c>m6-lore-cantrips</c>): the rule in force on <paramref name="ground"/> for <paramref name="character"/> (<see cref="SettingFor"/>), or null for none.</summary>
    /// <param name="ground">The ground.</param>
    /// <param name="character">The character's name, or null.</param>
    public static KeepAsideRule? For(HuntingGround ground, string? character) => SettingFor(ground, character) is { } setting ? new KeepAsideRule(setting) : null;

    /// <summary>The cantrip spell names the rule keeps for.</summary>
    public IReadOnlyCollection<string> SpellNames => _names;

    /// <summary>
    /// M6 14 keep every great cantrip (the owner's ruling, 2026-11-15: these great cantrips must be kept): the cantrip tiers the owner's rule saves whatever the pack holds: Major and above (ACE's spell names begin with the tier: "Major Focus", "Epic Focus",
    /// "Legendary Focus"). M6 14 useful cantrips (the owner, 2026-11-16): the tier alone keeps nothing: a great cantrip keeps its piece only when <see cref="CantripJudge"/> calls it useful (<see cref="CantripRoom.IsUsefulGreatPiece(GameState, uint, Func{uint, string}, CantripPlan, Action{string}, string)"/>).
    /// </summary>
    public static IReadOnlyList<string> GreatTiers { get; } = ["Major", "Epic", "Legendary"];

    /// <summary>M6 14 keep every great cantrip: true when <paramref name="spellName"/> is a cantrip of a great tier (<see cref="GreatTiers"/>: its name begins with "Major ", "Epic " or "Legendary ").</summary>
    /// <param name="spellName">The spell's name (the SpellTable's).</param>
    public static bool IsGreat(string? spellName)
    {
        if (string.IsNullOrEmpty(spellName))
        {
            return false;
        }

        foreach (var tier in GreatTiers)
        {
            if (spellName.Length > tier.Length && spellName.StartsWith(tier, StringComparison.OrdinalIgnoreCase) && spellName[tier.Length] == ' ')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// M6 14 keep every great cantrip, M6 14 useful cantrips (the owner's ruling, 2026-11-16: keep only the useful cantrips): the great cantrips (<see cref="IsGreat"/>, at the block's tiers) the appraisal casts that <paramref name="judge"/> calls
    /// useful (or cannot read: kept loudly), any family, in the appraisal's order, or empty (no appraisal, not a type the rule reads, or none). The block's cantrip names (<see cref="SpellNames"/>) no longer decide the keep: the judge does,
    /// from what each spell raises. A caster's War Magic bonus alone is no great cantrip: such a wand keeps its present rule.
    /// </summary>
    /// <param name="itemType">The item's ItemType.</param>
    /// <param name="appraisal">The appraisal of the item, or null.</param>
    /// <param name="spellName">The name of a spell id (the SpellTable's), or null for an id it does not know.</param>
    /// <param name="judge">The judge of her cantrips (<see cref="CantripJudge.Of"/>: her skills live); null: <see cref="CantripJudge.Stateless"/>.</param>
    public List<string> GreatCantrips(uint itemType, Appraisal? appraisal, Func<uint, string?> spellName, CantripJudge? judge = null)
    {
        ArgumentNullException.ThrowIfNull(spellName);
        var great = new List<string>();
        if (appraisal is null || appraisal.IsNotFound || !NeedsAppraisal(itemType))
        {
            return great;
        }

        foreach (var verdict in (judge ?? CantripJudge.Stateless).GreatOf(appraisal, spellName, Setting.Tiers))
        {
            if (verdict.Keeps && !great.Contains(verdict.SpellName))
            {
                great.Add(verdict.SpellName);
            }
        }

        return great;
    }

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
    /// M6 2 a (<c>m5-keep-aside-spell-name</c>): every profile's keep-aside names the spell table lacks, as <c>&lt;ground key&gt;: &lt;name&gt;</c>, in the grounds' order: a test over every profile and the SpellTable
    /// (KeepAsideRuleTests) so a typo fails before a run, beside the fight's runtime guard (FightScenario.cs).
    /// </summary>
    /// <param name="grounds">The profiles (<see cref="GroundCatalog.All"/>).</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="checkedGrounds">Filled with the keys of the profiles that have a block.</param>
    public static List<string> ProfileNamesMissing(IEnumerable<HuntingGround> grounds, IReadOnlyDictionary<uint, SpellInfo> spells, List<string>? checkedGrounds = null)
    {
        ArgumentNullException.ThrowIfNull(grounds);
        ArgumentNullException.ThrowIfNull(spells);
        var missing = new List<string>();
        foreach (var ground in grounds)
        {
            if (ground.Loot?.KeepAside is not { } block)
            {
                continue;
            }

            checkedGrounds?.Add(ground.Key);
            missing.AddRange(new KeepAsideRule(block).NamesMissingFrom(spells).Select(n => ground.Key + ": " + n));
        }

        missing.AddRange(new KeepAsideRule(ClimberDefault).NamesMissingFrom(spells).Select(n => "the climber's default: " + n)); // M6 14 (m6-lore-cantrips): her default is checked as a profile's block is
        return missing;
    }

    /// <summary>
    /// Why the item is kept aside, or null when it is not (or when nothing is known: no appraisal, no match). "War Magic bonus +3%" for a caster over the bonus floor; "Major Focus, Major Willpower" for the cantrips it casts.
    /// </summary>
    /// <param name="itemType">The item's ItemType.</param>
    /// <param name="appraisal">The appraisal of the item, or null (a failed or not-found one matches nothing).</param>
    /// <param name="spellName">The name of a spell id (the SpellTable's), or null for an id it does not know.</param>
    /// <param name="judge">M6 14 useful cantrips: the judge of her cantrips (<see cref="CantripJudge.Of"/>: her skills live); null: <see cref="CantripJudge.Stateless"/>. The cantrips named are the useful great ones (<see cref="GreatCantrips"/>).</param>
    public string? Match(uint itemType, Appraisal? appraisal, Func<uint, string?> spellName, CantripJudge? judge = null)
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

        var cantrips = GreatCantrips(itemType, appraisal, spellName, judge); // M6 14 useful cantrips (the owner, 2026-11-16): the judge, not the block's names

        if (bonus is null)
        {
            return cantrips.Count == 0 ? null : string.Join(", ", cantrips);
        }

        return cantrips.Count == 0 ? bonus : bonus + "; " + string.Join(", ", cantrips);
    }
}

/// <summary>
/// The wands and cantrip pieces the keep-aside rule has taken over the process (M5 7 F30), for the soak line's <c>worth_a_look=</c>; report-only. The guids themselves are <see cref="Lifestone.Client.Session.Scenarios.Journey.SaleMemory.KeptAside"/>, which the sale reads.
/// M6 14 (<c>m6-lore-cantrips</c>, the count kept honest): a piece counts once, when it is in her packs after the pick-up (not when a plan named it), and only a match of the keep-aside rule counts (not a scroll the scroll rule keeps, not a
/// quest item: <see cref="MageLootPlan.WorthALookGuids"/>).
/// </summary>
public static class KeepAsideTally
{
    private static long _looks;
    private static readonly HashSet<uint> Counted = [];

    /// <summary>Items taken as worth a look over the process.</summary>
    public static long WorthALook => Interlocked.Read(ref _looks);

    /// <summary>Counts <paramref name="guid"/> once over the process; true when it was not counted before.</summary>
    /// <param name="guid">The piece.</param>
    public static bool Add(uint guid)
    {
        lock (Counted)
        {
            if (!Counted.Add(guid))
            {
                return false;
            }
        }

        Interlocked.Increment(ref _looks);
        return true;
    }
}

using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Mage;

/// <summary>M5 7 D125: the character's numbers a wield or activation requirement is read against.</summary>
public interface IGearSkills
{
    /// <summary>The skill's current value (attributes and enchantments in), or null when it is not known (no formula here, or the read model lacks it).</summary>
    /// <param name="skill">ACE's skill id.</param>
    int? Current(uint skill);

    /// <summary>The skill's base value (no enchantments), or null when not known.</summary>
    /// <param name="skill">ACE's skill id.</param>
    int? Base(uint skill);

    /// <summary>The skill's advancement class (0 unusable, 1 untrained, 2 trained, 3 specialised).</summary>
    /// <param name="skill">ACE's skill id.</param>
    int AdvancementClass(uint skill);

    /// <summary>The character's level.</summary>
    int Level { get; }

    /// <summary>M5 7 S2b: a primary attribute's value (1 Strength to 6 Self), or null when not known.</summary>
    /// <param name="attribute">ACE's attribute id.</param>
    int? Attribute(uint attribute) => null;

    /// <summary>M5 7 S2b: a vital's maximum (1 health, 3 stamina, 5 mana by the maximum's id), or null when not known.</summary>
    /// <param name="vital">The vital's maximum id.</param>
    int? VitalMax(uint vital) => null;

    /// <summary>M5 7 S2b: the character's heritage (PropertyInt 188), 0 when not known.</summary>
    int Heritage => 0;
}

/// <summary>
/// M5 7 D125: the mage's numbers from the read model: the four magic skills by their formula (Focus + Self) / 4, Arcane Lore by Focus / 3 (<see cref="FighterGearCheck.ArcaneLoreFormula"/>) and Mana Conversion by (Focus + Self) / 6
/// (<c>ManaOrder</c>'s), each through <see cref="SkillRules"/>. Any other skill reads null (<c>TODO(m5-self-equip-skill-formula)</c>: a requirement on it is unmet, named).
/// </summary>
/// <param name="q">The player's qualities.</param>
public sealed class QualitiesSkills(PlayerQualities q) : IGearSkills
{
    private static readonly SkillTableFormula Magic = new(0, 1, 1, MageTemplate.MagicSkillDivisor, MageTemplate.Focus, MageTemplate.Self);
    private static readonly SkillTableFormula ManaConversionFormula = new(0, 1, 1, 6, MageTemplate.Focus, MageTemplate.Self);

    /// <inheritdoc/>
    public int Level => q.Level;

    /// <inheritdoc/>
    public int AdvancementClass(uint skill) => q.TryGetSkill(skill, out var s) ? (int)s.AdvancementClass : 0;

    /// <inheritdoc/>
    public int? Current(uint skill) => Formula(skill) is { } f && SkillRules.TryCurrent(q, skill, in f.Formula, f.MinLevel, out var v) ? (int)Math.Min(v, int.MaxValue) : null;

    /// <inheritdoc/>
    public int? Base(uint skill) => Formula(skill) is { } f && SkillRules.TryBase(q, skill, in f.Formula, f.MinLevel, out var v) ? (int)Math.Min(v, int.MaxValue) : null;

    /// <inheritdoc/>
    /// <remarks>The raw value (creation plus ranks): an attribute buff only raises it, so a limit met by the raw value is met by the current one (ACE reads <c>Current</c>, WorldObject_Use.cs:298-304); one met only with the buff is refused here.</remarks>
    public int? Attribute(uint attribute) => q.TryGetAttributeLevels(attribute, out _, out var raw) ? (int)Math.Min(raw, int.MaxValue) : null;

    /// <inheritdoc/>
    public int? VitalMax(uint vital) => q.TryGetVitalLevels(vital, out _, out var max) ? (int)Math.Min(max, int.MaxValue) : null;

    /// <inheritdoc/>
    public int Heritage => q.TryGetInt(SelfEquipJudge.HeritageGroupInt, out var h) ? h : 0;

    private static readonly SkillTableFormula MeleeDefenseFormula = new(0, 1, 1, 3, Quickness, Coordination);
    private static readonly SkillTableFormula MissileDefenseFormula = new(0, 1, 1, 5, Quickness, Coordination);
    private const uint Coordination = 4, Quickness = 3, MissileDefense = 7;

    /// <summary>The formula and the class from which it counts (the defences are usable untrained; the magic skills from trained), as the SkillTable's. The armour loot's ItemSkillLimit names Melee or Missile Defense (LootGenerationFactory_Magic.cs:190-203).</summary>
    internal static (SkillTableFormula Formula, uint MinLevel)? Formula(uint skill) => skill switch
    {
        MageTemplate.CreatureEnchantment or MageTemplate.ItemEnchantment or MageTemplate.LifeMagic or MageTemplate.WarMagic => (Magic, SkillRules.TrainedClass),
        MageTemplate.ArcaneLore => (FighterGearCheck.ArcaneLoreFormula, SkillRules.TrainedClass),
        MageTemplate.ManaConversion => (ManaConversionFormula, SkillRules.TrainedClass),
        MageTemplate.MeleeDefense => (MeleeDefenseFormula, SkillRules.UntrainedClass),
        MissileDefense => (MissileDefenseFormula, SkillRules.UntrainedClass),
        _ => null,
    };
}

/// <summary>An item the self-equip may put on: a caster, a wearable or a piece of footwear the mage holds, with its appraisal.</summary>
/// <param name="Guid">The item.</param>
/// <param name="Name">Its name.</param>
/// <param name="ItemType">Its ItemType.</param>
/// <param name="ValidLocations">Its ValidLocations (the EquipMask bits it may be worn at).</param>
/// <param name="Coverage">Its ClothingPriority (ACE's CoverageMask), 0 for jewellery and casters.</param>
/// <param name="Burden">Its burden.</param>
/// <param name="Appraisal">The appraisal the session holds of it, or null.</param>
public sealed record GearCandidate(uint Guid, string Name, uint ItemType, uint ValidLocations, uint Coverage, int Burden, Appraisal? Appraisal);

/// <summary>A piece the mage wears or wields now.</summary>
/// <param name="Guid">The piece.</param>
/// <param name="Name">Its name.</param>
/// <param name="ItemType">Its ItemType.</param>
/// <param name="Location">The EquipMask it occupies.</param>
/// <param name="Coverage">Its ClothingPriority (CoverageMask), 0 when none.</param>
/// <param name="Appraisal">The appraisal the session holds of it, or null.</param>
public sealed record WornPiece(uint Guid, string Name, uint ItemType, uint Location, uint Coverage, Appraisal? Appraisal);

/// <summary>What the self-equip decided of one item.</summary>
public enum SelfEquipKind
{
    /// <summary>Nothing to do: no gain, or the item is not one the self-equip reads.</summary>
    None,

    /// <summary>Put on in a free slot (<see cref="SelfEquipChoice.Location"/>).</summary>
    Wear,

    /// <summary>Wielded in place of the held caster (<see cref="SelfEquipChoice.Replaces"/>), which goes to the kit pack.</summary>
    Swap,

    /// <summary>Interesting, not worn (a requirement unmet, no free slot, the wrong element): the review pack.</summary>
    Review,
}

/// <summary>The self-equip's decision with the numbers that made it (<see cref="Why"/> is the log's).</summary>
/// <param name="Kind">What to do.</param>
/// <param name="Guid">The item.</param>
/// <param name="Name">Its name.</param>
/// <param name="Location">The EquipMask to wield it at (Wear, Swap).</param>
/// <param name="Replaces">The held caster it replaces (Swap), else 0.</param>
/// <param name="Why">Why, with the numbers.</param>
public sealed record SelfEquipChoice(SelfEquipKind Kind, uint Guid, string Name, uint Location, uint Replaces, string Why)
{
    /// <summary>The decision's verb as the log prints it: wear, swap in, review, no gain from.</summary>
    public string Verb => Kind switch { SelfEquipKind.Wear => "wear", SelfEquipKind.Swap => "swap in", SelfEquipKind.Review => "review", _ => "no gain from" };

    /// <summary>M5 7 D149: true for a wearable upgrade the mage can put on (a Wear in a free slot or a Swap for the held caster), the one kind of heavy find the loot rule keeps aside.</summary>
    public bool IsUpgrade => Kind is SelfEquipKind.Wear or SelfEquipKind.Swap;

    /// <summary>The decision as the log prints it: <c>gear: &lt;wear|swap|review|keep&gt; '&lt;name&gt;' 0x&lt;guid&gt;: &lt;why&gt;</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"gear: {Verb} '{Name}' 0x{Guid:X8}: {Why}");
}

/// <summary>
/// M5 7 D125 (the relay's default for the self-equip; the owner may overrule it): what the mage puts on by itself. Pure over the appraisals, the worn pieces and the character's numbers.
/// </summary>
/// <remarks>
/// <para><b>A better caster.</b> ACE multiplies a war spell's damage by the wielded caster's ElementalDamageMod (PropertyFloat 152) only when the caster's DamageType (PropertyInt 45) is the spell's element
/// (<c>GetCasterElementalDamageModifier</c>, WorldObject_Weapon.cs:382-401 @95ef426d); the spell plan's family gives the element (the pinned world SQL's spell <c>e_Type</c>: Flame Bolt 16 fire, Frost Bolt 8 cold, Lightning Bolt 64
/// electric, Force Bolt 2 pierce). The caster's ManaConversionMod (PropertyFloat 144) multiplies the Mana Conversion skill in the magic combat mode (<c>GetWeaponManaConversionModifier</c>, WorldObject_Weapon.cs:235-260; the cost,
/// Creature_Magic.cs:41-50). A candidate is a clear upgrade when its damage factor on the plan's element is higher; on a tie, when its mana conversion is higher; on a tie, when it casts more of the useful cantrips. A caster of another element
/// with a bonus is kept for the owner (review), not wielded.</para>
/// <para><b>A wearable with a useful cantrip.</b> A wearable, jewellery piece or trinket that casts a cantrip of the useful list (the ground's keep-aside block: the tiers Major, Epic, Legendary of War Magic Aptitude, Focus, Willpower, Mana
/// Conversion Prowess, Arcane Prowess, the defences and the other aptitudes) that no worn piece casts already is worn when a slot is free: for clothing and armour no worn piece's ClothingPriority overlaps its own (ACE's
/// <c>WieldedLocationIsAvailable</c> through <c>GetEquippedClothingArmor</c>, Creature_Equipment.cs:50-58, 93-101, refused with "You must remove your X to wield Y", Player_Inventory.cs:1801-1809); for jewellery and trinkets no worn piece holds
/// the location (a ring or a bracelet takes the free side, <see cref="WieldSlotChoice"/>). Nothing is taken off for a cantrip.</para>
/// <para><b>Requirements</b> (each named in the why): the wield requirements (PropertyInt 158-160, 270-272: Skill against the current skill, RawSkill against the base, Level, Training; <c>CheckWieldRequirement</c>, Player_Inventory.cs:2126-2229)
/// refuse the wield; the activation requirements (ItemDifficulty 109 against the current Arcane Lore, UseRequiresLevel 369, ItemSkillLevelLimit 115; <c>CheckUseRequirements</c>, WorldObject_Use.cs:191-295) leave the spells off
/// (<c>TryActivateSpells</c>, Player_Inventory.cs:313-335), so either makes the item a review find. A requirement this code cannot read is unmet, named.</para>
/// </remarks>
public sealed class SelfEquipJudge
{
    /// <summary>ACE's ItemType Caster.</summary>
    public const uint CasterType = 0x8000;

    /// <summary>ACE's EquipMask Held (the caster's slot).</summary>
    public const uint Held = 0x01000000;

    /// <summary>ACE's EquipMask FootWear.</summary>
    public const uint FootWear = 0x100;

    /// <summary>ACE's CoverageMask Feet.</summary>
    public const uint FeetCoverage = 0x10000;

    /// <summary>ACE's PropertyInt DamageType (a caster's element, W_DamageType).</summary>
    public const uint DamageTypeInt = 45;

    /// <summary>ACE's PropertyFloat ManaConversionMod.</summary>
    public const uint ManaConversionModFloat = 144;

    /// <summary>ACE's PropertyInt UseRequiresLevel.</summary>
    public const uint UseRequiresLevelInt = 369;

    /// <summary>ACE's PropertyInt ItemSkillLevelLimit.</summary>
    public const uint ItemSkillLevelLimitInt = 115;

    /// <summary>ACE's PropertyDataId ItemSkillLimit (the skill <see cref="ItemSkillLevelLimitInt"/> is read against).</summary>
    public const uint ItemSkillLimitDid = 37;

    /// <summary>The war spell families' elements (the pinned world SQL v0.9.296's <c>spell.e_Type</c> of the level VI row): ACE's DamageType bits.</summary>
    public static IReadOnlyDictionary<string, uint> FamilyElements { get; } = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
    {
        ["Flame Bolt"] = 0x10,
        ["Frost Bolt"] = 0x8,
        ["Lightning Bolt"] = 0x40,
        ["Force Bolt"] = 0x2,
    };

    private const uint Wrists = 0x00010000 | 0x00020000;
    private const uint Fingers = 0x00040000 | 0x00080000;
    private const uint JewelleryLocations = 0x00008000 | Wrists | Fingers | 0x04000000;
    private const uint WearableTypes = 0x2 | 0x4 | 0x8;

    private readonly HashSet<string> _useful;
    private readonly Func<uint, string?> _spellName;

    /// <summary>A judge.</summary>
    /// <param name="useful">The useful cantrip names (tier and family: "Major Focus"), the ground's keep-aside block's.</param>
    /// <param name="element">The plan's war element (ACE DamageType bits, <see cref="FamilyElements"/>), 0 when not known.</param>
    /// <param name="spellName">The name of a spell id (the SpellTable's), or null.</param>
    public SelfEquipJudge(IEnumerable<string> useful, uint element, Func<uint, string?> spellName)
    {
        ArgumentNullException.ThrowIfNull(useful);
        ArgumentNullException.ThrowIfNull(spellName);
        _useful = new HashSet<string>(useful, StringComparer.OrdinalIgnoreCase);
        Element = element;
        _spellName = spellName;
    }

    /// <summary>The plan's war element (DamageType bits), 0 when not known.</summary>
    public uint Element { get; }

    /// <summary>The element of a war family, or 0 (logged by the caller as <c>TODO(m5-self-equip-element)</c>).</summary>
    public static uint ElementOf(string? family) => family is not null && FamilyElements.TryGetValue(family, out var e) ? e : 0;

    /// <summary>The useful cantrips <paramref name="appraisal"/> casts, by name, in its order.</summary>
    public List<string> UsefulCantrips(Appraisal? appraisal)
    {
        var list = new List<string>();
        if (appraisal is null || appraisal.IsNotFound)
        {
            return list;
        }

        foreach (var id in appraisal.Spells)
        {
            if (_spellName(id) is { } name && _useful.Contains(name) && !list.Contains(name))
            {
                list.Add(name);
            }
        }

        return list;
    }

    /// <summary>A caster's damage factor on the plan's element: its ElementalDamageMod when its DamageType is the element, else 1.</summary>
    public double WarFactor(Appraisal? appraisal) =>
        appraisal is { IsNotFound: false } a && Element != 0 && a.Int(DamageTypeInt) is { } type && (uint)type == Element ? a.Float(AppraisalIds.ElementalDamageMod) ?? 1.0 : 1.0;

    /// <summary>A caster's ManaConversionMod (0 when none).</summary>
    public static double ManaConversion(Appraisal? appraisal) => appraisal is { IsNotFound: false } a ? a.Float(ManaConversionModFloat) ?? 0.0 : 0.0;

    /// <summary>
    /// The first requirement the character does not meet, or null when every one is met (see the class remarks): the wield requirements, then the activation ones.
    /// </summary>
    /// <param name="appraisal">The item's appraisal.</param>
    /// <param name="skills">The character's numbers.</param>
    /// <param name="met">Filled with the requirements met, for the log.</param>
    public static string? Unmet(Appraisal appraisal, IGearSkills skills, List<string>? met = null)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        ArgumentNullException.ThrowIfNull(skills);
        foreach (var r in appraisal.WieldRequirements)
        {
            var skill = (uint)r.Skill;
            switch (r.Type)
            {
                case 1: // Skill: the current value
                case 2: // RawSkill: the base
                    var value = r.Type == 1 ? skills.Current(skill) : skills.Base(skill);
                    if (value is null)
                    {
                        return string.Create(CultureInfo.InvariantCulture, $"a wield requirement on skill {skill} ({(r.Type == 1 ? "current" : "base")} {r.Difficulty}) this code cannot read (TODO(m5-self-equip-skill-formula))");
                    }

                    if (value < r.Difficulty)
                    {
                        return string.Create(CultureInfo.InvariantCulture, $"wield: skill {skill} {(r.Type == 1 ? "current" : "base")} {value} < {r.Difficulty} ({WieldRefusal(SkillTooLow)})");
                    }

                    met?.Add(string.Create(CultureInfo.InvariantCulture, $"wield: skill {skill} {(r.Type == 1 ? "current" : "base")} {value} >= {r.Difficulty}"));
                    break;
                case 7: // Level
                    if (skills.Level < r.Difficulty)
                    {
                        return string.Create(CultureInfo.InvariantCulture, $"wield: level {skills.Level} < {r.Difficulty} ({WieldRefusal(LevelTooLow)})");
                    }

                    met?.Add(string.Create(CultureInfo.InvariantCulture, $"wield: level {skills.Level} >= {r.Difficulty}"));
                    break;
                case 8: // Training
                    if (skills.AdvancementClass(skill) < r.Difficulty)
                    {
                        return string.Create(CultureInfo.InvariantCulture, $"wield: skill {skill} class {skills.AdvancementClass(skill)} < {r.Difficulty} ({WieldRefusal(SkillTooLow)})");
                    }

                    met?.Add(string.Create(CultureInfo.InvariantCulture, $"wield: skill {skill} class {skills.AdvancementClass(skill)} >= {r.Difficulty}"));
                    break;
                default:
                    return string.Create(CultureInfo.InvariantCulture, $"a wield requirement of type {r.Type} (skill {r.Skill}, {r.Difficulty}) this code does not read (TODO(m5-self-equip-wield-type))");
            }
        }

        // The activation requirements, in ACE's order (CheckUseRequirements, WorldObject_Use.cs:191-324); each refusal names the line ACE sends, which the chat shows as retail's (WeenieErrorText).
        if (appraisal.Int(AppraisalIds.ArcaneLore) is { } difficulty and > 0)
        {
            var lore = skills.Current(MageTemplate.ArcaneLore) ?? 0;
            if (lore < difficulty)
            {
                return string.Create(CultureInfo.InvariantCulture, $"activation: Arcane Lore {lore} < {difficulty} (its spells would stay off; {Retail(TooLowToUseItemMagic, SkillName(MageTemplate.ArcaneLore))})"); // :204-210
            }

            met?.Add(string.Create(CultureInfo.InvariantCulture, $"activation: Arcane Lore {lore} >= {difficulty}"));
        }

        if (appraisal.Int(ItemSkillLevelLimitInt) is { } limit and > 0 && appraisal.DataId(ItemSkillLimitDid) is { } limitSkill)
        {
            var value = skills.Current(limitSkill);
            if (value is null || value < limit)
            {
                return string.Create(CultureInfo.InvariantCulture, $"activation: {SkillName(limitSkill)} {value?.ToString(CultureInfo.InvariantCulture) ?? "unread"} < {limit} (its spells would stay off; {Retail(TooLowToUseItemMagic, SkillName(limitSkill))})"); // :214-229
            }

            met?.Add(string.Create(CultureInfo.InvariantCulture, $"activation: {SkillName(limitSkill)} {value} >= {limit}"));
        }

        if (appraisal.Int(UseRequiresSkillInt) is { } useSkill and > 0)
        {
            var skill = (uint)useSkill;
            if (skills.AdvancementClass(skill) < SkillRules.TrainedClass)
            {
                return string.Create(CultureInfo.InvariantCulture, $"activation: {SkillName(skill)} untrained (its spells would stay off; retail: \"You must have {SkillName(skill)} trained to use that item's magic\")"); // :231-241, a transient string
            }

            if (appraisal.Int(UseRequiresSkillLevelInt) is { } useLevel and > 0 && (skills.Current(skill) ?? 0) < useLevel)
            {
                return string.Create(CultureInfo.InvariantCulture, $"activation: {SkillName(skill)} {skills.Current(skill)?.ToString(CultureInfo.InvariantCulture) ?? "unread"} < {useLevel} (its spells would stay off; {Retail(TooLowToUseItemMagic, SkillName(skill))})"); // :243-248
            }

            met?.Add(string.Create(CultureInfo.InvariantCulture, $"activation: {SkillName(skill)} trained"));
        }

        foreach (var spec in new[] { appraisal.Int(UseRequiresSkillSpecInt) is { } s and > 0 ? (uint)s : 0u, appraisal.DataId(ItemSpecializedOnlyDid) ?? 0u })
        {
            if (spec != 0 && skills.AdvancementClass(spec) < SkillRules.SpecializedClass)
            {
                return string.Create(CultureInfo.InvariantCulture, $"activation: {SkillName(spec)} not specialised (its spells would stay off; {Retail(MustSpecializeToUseItemMagic, SkillName(spec))})"); // :254-286
            }
        }

        if (appraisal.Int(UseRequiresLevelInt) is { } level and > 0)
        {
            if (skills.Level < level)
            {
                return string.Create(CultureInfo.InvariantCulture, $"activation: level {skills.Level} < {level} (its spells would stay off; retail: \"You are not high enough level to use that!\")"); // :289-295, a transient string
            }

            met?.Add(string.Create(CultureInfo.InvariantCulture, $"activation: level {skills.Level} >= {level}"));
        }

        if (appraisal.Int(ItemAttributeLimitInt) is { } attribute and > 0 && appraisal.Int(ItemAttributeLevelLimitInt) is { } attributeLimit)
        {
            var value = skills.Attribute((uint)attribute);
            if (value is null || value < attributeLimit)
            {
                return string.Create(CultureInfo.InvariantCulture, $"activation: attribute {attribute} {value?.ToString(CultureInfo.InvariantCulture) ?? "unread"} < {attributeLimit} (its spells would stay off; {Retail(TooLowToUseItemMagic, AttributeName((uint)attribute))})"); // :298-304
            }
        }

        if (appraisal.Int(ItemAttribute2ndLimitInt) is { } vital and > 0 && appraisal.Int(ItemAttribute2ndLevelLimitInt) is { } vitalLimit)
        {
            var value = skills.VitalMax((uint)vital);
            if (value is null || value < vitalLimit)
            {
                return string.Create(CultureInfo.InvariantCulture, $"activation: vital {vital} {value?.ToString(CultureInfo.InvariantCulture) ?? "unread"} < {vitalLimit} (its spells would stay off; {Retail(TooLowToUseItemMagic, "vital")})"); // :306-312
            }
        }

        if (appraisal.Int(HeritageGroupInt) is { } heritage and > 0 && heritage != skills.Heritage)
        {
            return string.Create(CultureInfo.InvariantCulture, $"activation: heritage {skills.Heritage} is not the item's {heritage} (its spells would stay off; {Retail(MustBeToUseItemMagic, "of heritage " + heritage.ToString(CultureInfo.InvariantCulture))})"); // :314-324
        }

        return null;
    }

    /// <summary>ACE's WeenieError SkillTooLow (WeenieError.cs:526), the wield refusal of a skill, attribute or training requirement (Player_Inventory.cs:2133-2194).</summary>
    public const uint SkillTooLow = 0x0468;

    /// <summary>ACE's WeenieError LevelTooLow (WeenieError.cs:344), the wield refusal of a level requirement (Player_Inventory.cs:2181-2186).</summary>
    public const uint LevelTooLow = 0x0420;

    /// <summary>
    /// How a wield refusal shows: ACE sends it as GameEvent 0x00A0 with the code (Player_Inventory.cs:1655-1661, its comment: "client doesnt show specific wieldError here"), and the 11.6096 client prints a code only when
    /// <c>HandleFailureEvent</c> has a case for it (<see cref="WeenieErrorText.RetailShows"/>): SkillTooLow and LevelTooLow have none, so retail shows no line.
    /// </summary>
    public static string WieldRefusal(uint code) => WeenieErrorText.RetailShows(code)
        ? Retail(code, string.Empty)
        : string.Create(CultureInfo.InvariantCulture, $"ACE refuses the wield with 0x00A0 {WeenieErrorText.NameOf(code) ?? "code"} 0x{code:X4}, which retail prints no line for");

    /// <summary>ACE's WeenieErrorWithString Your_IsTooLowToUseItemMagic (WeenieErrorWithString.cs:119).</summary>
    public const uint TooLowToUseItemMagic = 0x04C9;

    /// <summary>ACE's WeenieErrorWithString YouMustSpecialize_ToUseItemMagic (WeenieErrorWithString.cs:129).</summary>
    public const uint MustSpecializeToUseItemMagic = 0x04CB;

    /// <summary>ACE's WeenieErrorWithString YouMustBe_ToUseItemMagic (WeenieErrorWithString.cs:114).</summary>
    public const uint MustBeToUseItemMagic = 0x04C6;

    /// <summary>ACE's PropertyInt UseRequiresSkill, UseRequiresSkillLevel, UseRequiresSkillSpec (PropertyInt.cs:576-580).</summary>
    public const uint UseRequiresSkillInt = 366, UseRequiresSkillLevelInt = 367, UseRequiresSkillSpecInt = 368;

    /// <summary>ACE's PropertyInt ItemAttributeLimit, ItemAttributeLevelLimit, ItemAttribute2ndLimit, ItemAttribute2ndLevelLimit (PropertyInt.cs:370-376).</summary>
    public const uint ItemAttributeLimitInt = 257, ItemAttributeLevelLimitInt = 258, ItemAttribute2ndLimitInt = 259, ItemAttribute2ndLevelLimitInt = 260;

    /// <summary>ACE's PropertyInt HeritageGroup (PropertyInt.cs:268): an item's heritage limit, the character's heritage.</summary>
    public const uint HeritageGroupInt = 188;

    /// <summary>ACE's PropertyDataId ItemSpecializedOnly (PropertyDataId.cs:60).</summary>
    public const uint ItemSpecializedOnlyDid = 41;

    /// <summary>The retail line ACE sends for a refusal with a string, as the chat shows it (<see cref="WeenieErrorText"/>): <c>retail: "Your Arcane Lore is too low to use that item's magic."</c>.</summary>
    public static string Retail(uint code, string argument)
    {
        WeenieErrorText.TryFormat(code, argument, out _, out var text);
        return "retail: \"" + text + "\"";
    }

    /// <summary>A skill's name as ACE's <c>ToSentence</c> writes it, for the skills the mage's requirements name; <c>skill N</c> otherwise.</summary>
    public static string SkillName(uint skill) => skill switch
    {
        MageTemplate.ArcaneLore => "Arcane Lore",
        MageTemplate.WarMagic => "War Magic",
        MageTemplate.LifeMagic => "Life Magic",
        MageTemplate.CreatureEnchantment => "Creature Enchantment",
        MageTemplate.ItemEnchantment => "Item Enchantment",
        MageTemplate.ManaConversion => "Mana Conversion",
        MageTemplate.MagicDefense => "Magic Defense",
        MageTemplate.MeleeDefense => "Melee Defense",
        7 => "Missile Defense",
        _ => string.Create(CultureInfo.InvariantCulture, $"skill {skill}"),
    };

    private static string AttributeName(uint attribute) => attribute switch
    {
        1 => "Strength",
        2 => "Endurance",
        3 => "Quickness",
        4 => "Coordination",
        5 => "Focus",
        _ => "Self",
    };

    /// <summary>Judges one item the mage holds against what it wears (see the class remarks).</summary>
    /// <param name="candidate">The item.</param>
    /// <param name="worn">The pieces worn and wielded now.</param>
    /// <param name="skills">The character's numbers.</param>
    public SelfEquipChoice Judge(GearCandidate candidate, IReadOnlyList<WornPiece> worn, IGearSkills skills)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(worn);
        ArgumentNullException.ThrowIfNull(skills);
        if (candidate.Appraisal is not { IsNotFound: false } appraisal)
        {
            return new SelfEquipChoice(SelfEquipKind.None, candidate.Guid, candidate.Name, 0, 0, "no appraisal of it is held");
        }

        return (candidate.ItemType & CasterType) != 0 ? JudgeCaster(candidate, appraisal, worn, skills) : (candidate.ItemType & WearableTypes) != 0 ? JudgeWearable(candidate, appraisal, worn, skills) : new SelfEquipChoice(SelfEquipKind.None, candidate.Guid, candidate.Name, 0, 0, "not a caster or a wearable");
    }

    private SelfEquipChoice JudgeCaster(GearCandidate candidate, Appraisal appraisal, IReadOnlyList<WornPiece> worn, IGearSkills skills)
    {
        var held = worn.FirstOrDefault(w => (w.Location & Held) != 0 && (w.ItemType & CasterType) != 0);
        var (factor, mc, cantrips) = (WarFactor(appraisal), ManaConversion(appraisal), UsefulCantrips(appraisal).Count);
        var (heldFactor, heldMc, heldCantrips) = held is null ? (1.0, 0.0, 0) : (WarFactor(held.Appraisal), ManaConversion(held.Appraisal), UsefulCantrips(held.Appraisal).Count);
        var numbers = string.Create(
            CultureInfo.InvariantCulture,
            $"war damage x{factor:0.###} against x{heldFactor:0.###} on the plan's element 0x{Element:X} (its element 0x{appraisal.Int(DamageTypeInt) ?? 0:X}, bonus x{appraisal.Float(AppraisalIds.ElementalDamageMod) ?? 1.0:0.###}), mana conversion +{mc * 100:0.#}% against +{heldMc * 100:0.#}%, useful cantrips {cantrips} against {heldCantrips}{(held is null ? ", no caster held" : $", the held '{held.Name}' 0x{held.Guid:X8}")}");
        var better = Beats(appraisal, held?.Appraisal);
        if (!better)
        {
            // Not an upgrade: nothing to do here (a caster worth a look for its bonus is the keep-aside rule's, which keeps it in the review pack already; a loot wand's small mana conversion alone is not hoarded).
            return new SelfEquipChoice(SelfEquipKind.None, candidate.Guid, candidate.Name, 0, 0, "not better: " + numbers);
        }

        var met = new List<string>();
        if (Unmet(appraisal, skills, met) is { } unmet)
        {
            return new SelfEquipChoice(SelfEquipKind.Review, candidate.Guid, candidate.Name, 0, 0, "better but " + unmet + ": " + numbers);
        }

        return new SelfEquipChoice(held is null ? SelfEquipKind.Wear : SelfEquipKind.Swap, candidate.Guid, candidate.Name, Held, held?.Guid ?? 0, "better: " + numbers + "; " + (met.Count == 0 ? "no requirement" : string.Join(", ", met)));
    }

    /// <summary>True when the caster's element is the plan's (ACE's bonus applies only then, WorldObject_Weapon.cs:382-401).</summary>
    private bool OnElement(Appraisal? a) => a is { IsNotFound: false } && Element != 0 && a.Int(DamageTypeInt) is { } t && (uint)t == Element;

    /// <summary>
    /// M5 7 S2c: true when caster <paramref name="candidate"/> is a clear upgrade over <paramref name="held"/> (null: no caster held): a higher war factor on the plan's element wins; on a tie only a caster OF the plan's element may win, by a
    /// higher mana conversion, then more useful cantrips. A caster of another element never wins a tie (the rehearsal of 2026-10-29 14:07:39 swapped an Electric Sceptre in for the plain Wand on its mana conversion alone).
    /// </summary>
    public bool Beats(Appraisal? candidate, Appraisal? held)
    {
        var (f, hf) = (WarFactor(candidate), WarFactor(held));
        if (f > hf + 1e-6)
        {
            return true;
        }

        if (f < hf - 1e-6 || !OnElement(candidate))
        {
            return false;
        }

        var (mc, hmc) = (ManaConversion(candidate), ManaConversion(held));
        return mc > hmc + 1e-6 || (Math.Abs(mc - hmc) <= 1e-6 && UsefulCantrips(candidate).Count > UsefulCantrips(held).Count);
    }

    /// <summary>
    /// M5 7 S2c: the one caster to wield: the best of every caster the mage holds (the wielded one included) whose requirements it meets, by <see cref="Beats"/>; a Swap (or a Wear with none held) when that is not the wielded one, else null.
    /// <paramref name="why"/> names the pick, the field and the numbers.
    /// </summary>
    /// <param name="held">The casters held in the packs, with their appraisals.</param>
    /// <param name="worn">The pieces worn and wielded now.</param>
    /// <param name="skills">The character's numbers.</param>
    /// <param name="why">The pick's line.</param>
    public SelfEquipChoice? BestCaster(IReadOnlyList<GearCandidate> held, IReadOnlyList<WornPiece> worn, IGearSkills skills, out string why)
    {
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(worn);
        ArgumentNullException.ThrowIfNull(skills);
        var wielded = worn.FirstOrDefault(w => (w.Location & Held) != 0 && (w.ItemType & CasterType) != 0);
        GearCandidate? best = null;
        int eligible = 0, unread = 0, unmet = 0;
        foreach (var c in held)
        {
            if ((c.ItemType & CasterType) == 0)
            {
                continue;
            }

            if (c.Appraisal is not { IsNotFound: false } a)
            {
                unread++; // M5 7 S2e: named, so a pick over unappraised casters is never silent
                continue;
            }

            if (Unmet(a, skills) is not null)
            {
                unmet++;
                continue;
            }

            eligible++;
            if (best is null ? Beats(a, wielded?.Appraisal) : Beats(a, best.Appraisal))
            {
                best = c;
            }
        }

        var heldName = wielded is null ? "no caster" : $"'{wielded.Name}' 0x{wielded.Guid:X8}";
        if (best is null)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"gear: caster pick: keep {heldName}: none of the {eligible} usable caster(s) held beats it on the plan's element 0x{Element:X} ({unread} not appraised, {unmet} short of a requirement)");
            return null;
        }

        var choice = JudgeCaster(best, best.Appraisal!, worn, skills);
        why = string.Create(CultureInfo.InvariantCulture, $"gear: caster pick: '{best.Name}' 0x{best.Guid:X8}, the best of {eligible} usable caster(s) held, over {heldName}");
        return choice.Kind is SelfEquipKind.Swap or SelfEquipKind.Wear ? choice : null;
    }

    private SelfEquipChoice JudgeWearable(GearCandidate candidate, Appraisal appraisal, IReadOnlyList<WornPiece> worn, IGearSkills skills)
    {
        var cantrips = UsefulCantrips(appraisal);
        if (cantrips.Count == 0)
        {
            return new SelfEquipChoice(SelfEquipKind.None, candidate.Guid, candidate.Name, 0, 0, "no useful cantrip");
        }

        var wornCantrips = new HashSet<string>(worn.SelectMany(w => UsefulCantrips(w.Appraisal)), StringComparer.OrdinalIgnoreCase);
        var fresh = cantrips.Where(c => !wornCantrips.Contains(c)).ToList();
        var list = string.Join(", ", cantrips);
        if (fresh.Count == 0)
        {
            return new SelfEquipChoice(SelfEquipKind.Review, candidate.Guid, candidate.Name, 0, 0, $"its cantrips ({list}) are cast by a worn piece already");
        }

        if (FreeLocation(candidate, worn, out var blocker) is not { } location)
        {
            return new SelfEquipChoice(SelfEquipKind.Review, candidate.Guid, candidate.Name, 0, 0, $"no free slot for {list}: {blocker}");
        }

        var met = new List<string>();
        if (Unmet(appraisal, skills, met) is { } unmet)
        {
            return new SelfEquipChoice(SelfEquipKind.Review, candidate.Guid, candidate.Name, 0, 0, $"{list}, but {unmet}");
        }

        return new SelfEquipChoice(SelfEquipKind.Wear, candidate.Guid, candidate.Name, location, 0, string.Create(CultureInfo.InvariantCulture, $"casts {string.Join(", ", fresh)} that no worn piece casts, at the free location 0x{location:X8}; {(met.Count == 0 ? "no requirement" : string.Join(", ", met))}"));
    }

    /// <summary>
    /// The free location for <paramref name="candidate"/>, or null with the worn piece in the way named in <paramref name="blocker"/>: clothing and armour by coverage (no worn ClothingPriority overlaps), jewellery and trinkets by location
    /// (a free side of the rings or bracelets).
    /// </summary>
    public static uint? FreeLocation(GearCandidate candidate, IReadOnlyList<WornPiece> worn, out string blocker)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(worn);
        blocker = string.Empty;
        if (candidate.Coverage != 0)
        {
            foreach (var w in worn)
            {
                if ((w.Coverage & candidate.Coverage) != 0)
                {
                    blocker = string.Create(CultureInfo.InvariantCulture, $"the worn '{w.Name}' 0x{w.Guid:X8} covers 0x{w.Coverage & candidate.Coverage:X} of its coverage 0x{candidate.Coverage:X} (ACE: \"You must remove your {w.Name} to wield {candidate.Name}\")");
                    return null;
                }
            }

            return candidate.ValidLocations;
        }

        var valid = candidate.ValidLocations & JewelleryLocations;
        if (valid == 0)
        {
            blocker = string.Create(CultureInfo.InvariantCulture, $"its locations 0x{candidate.ValidLocations:X8} are no jewellery slot and it has no coverage");
            return null;
        }

        foreach (var pair in new[] { Wrists, Fingers })
        {
            if ((valid & pair) == pair)
            {
                foreach (var side in new[] { pair & 0x000A0000, pair & 0x00050000 })
                {
                    if (!worn.Any(w => (w.Location & side) != 0))
                    {
                        return side; // the right side first, as WieldSlotChoice's preference
                    }
                }

                blocker = "both sides are taken";
                return null;
            }
        }

        var taken = worn.FirstOrDefault(w => (w.Location & valid) != 0);
        if (taken is not null)
        {
            blocker = string.Create(CultureInfo.InvariantCulture, $"the worn '{taken.Name}' 0x{taken.Guid:X8} holds 0x{taken.Location & valid:X8}");
            return null;
        }

        return valid;
    }

    /// <summary>
    /// M5 7 D122: the footwear to wear from <paramref name="owned"/>: the feet must be free of every worn piece's coverage (a robe that covers the feet blocks every shoe, by the coverage rule of the class remarks), and of the pieces whose
    /// requirements the character meets the one with the highest armour level (PropertyInt 28 of its appraisal), then the lightest, then the first. <paramref name="why"/> says what decided, with the numbers.
    /// </summary>
    public static GearCandidate? PickFootwear(IReadOnlyList<GearCandidate> owned, IReadOnlyList<WornPiece> worn, IGearSkills skills, out string why)
    {
        ArgumentNullException.ThrowIfNull(owned);
        ArgumentNullException.ThrowIfNull(worn);
        ArgumentNullException.ThrowIfNull(skills);
        var covering = worn.FirstOrDefault(w => (w.Coverage & FeetCoverage) != 0 || (w.Coverage == 0 && (w.Location & FootWear) != 0));
        if (covering is not null && covering.Coverage == FeetCoverage)
        {
            why = $"the mage wears footwear already: '{covering.Name}' 0x{covering.Guid:X8}";
            return null;
        }

        if (covering is not null)
        {
            // The live doll (2026-10-29): the Faran Robe with Hood is worn at 0x7F01, which holds FootWear 0x100, and its ClothingPriority 0x17F00 includes Feet 0x10000 (the pinned world SQL, weenie 5851).
            why = string.Create(CultureInfo.InvariantCulture, $"feet covered by {covering.Name} (0x{covering.Guid:X8}, location 0x{covering.Location:X8}, ClothingPriority 0x{covering.Coverage:X} against Feet 0x{FeetCoverage:X}): ACE refuses every shoe while it is worn (Creature_Equipment.cs:50-58, 93-101; Player_Inventory.cs:1790-1809); no shoe is worn or bought (D129: the mage keeps the robe, as retail)");
            return null;
        }

        GearCandidate? best = null;
        var notes = new List<string>();
        foreach (var c in owned)
        {
            if ((c.ValidLocations & FootWear) == 0)
            {
                continue;
            }

            if (c.Appraisal is { IsNotFound: false } a && Unmet(a, skills) is { } unmet)
            {
                notes.Add($"'{c.Name}' 0x{c.Guid:X8}: {unmet}");
                continue;
            }

            if (best is null || Better(c, best))
            {
                best = c;
            }
        }

        why = best is null
            ? "none of the pack's footwear fits" + (notes.Count == 0 ? " (the pack holds none)" : ": " + string.Join("; ", notes))
            : string.Create(CultureInfo.InvariantCulture, $"'{best.Name}' 0x{best.Guid:X8}: armour level {ArmorOf(best)}, burden {best.Burden}, the feet are free{(notes.Count == 0 ? string.Empty : "; refused: " + string.Join("; ", notes))}");
        return best;

        static bool Better(GearCandidate a, GearCandidate b) => ArmorOf(a) != ArmorOf(b) ? ArmorOf(a) > ArmorOf(b) : a.Burden != b.Burden ? a.Burden < b.Burden : a.Guid < b.Guid;
    }

    private static int ArmorOf(GearCandidate c) => c.Appraisal?.ArmorLevel ?? 0;

    /// <summary>The pieces the mage wears and wields now, with their coverage and appraisals.</summary>
    public static List<WornPiece> Worn(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var list = new List<WornPiece>();
        foreach (var e in state.Self.Inventory.Equipped)
        {
            if (state.Objects.TryGet(e.Guid, out var o))
            {
                list.Add(new WornPiece(e.Guid, o.Name, o.ItemType, e.Location, e.Priority != 0 ? e.Priority : o.Weenie.Priority ?? 0, state.Appraisals.TryGet(e.Guid, out var a) ? a : null));
            }
        }

        return list;
    }

    /// <summary>One held item as a candidate, with its coverage, burden and appraisal.</summary>
    public static GearCandidate CandidateOf(GameState state, WorldObject item)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(item);
        return CandidateOf(item, state.Appraisals.TryGet(item.Guid, out var a) ? a : null);
    }

    /// <summary>One item (held, or still on a corpse: M5 7 D149) as a candidate, with its coverage, burden and the given appraisal.</summary>
    public static GearCandidate CandidateOf(WorldObject item, Appraisal? appraisal)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new GearCandidate(item.Guid, item.Name, item.ItemType, item.Weenie.ValidLocations ?? 0, item.Weenie.Priority ?? 0, item.Weenie.Burden ?? 0, appraisal);
    }
}

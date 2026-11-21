using System.Globalization;
using Lifestone.Client.Session.Grounds;
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

    /// <summary>
    /// M6 2 a: an armour or clothing piece whose <see cref="GearScore"/> beats the one worn piece in its coverage by <see cref="SelfEquipJudge.ReplaceMargin"/> (<see cref="SelfEquipChoice.Replaces"/>): that piece comes off, this one goes
    /// on at <see cref="SelfEquipChoice.Location"/>. Not an <see cref="SelfEquipChoice.IsUpgrade"/> (the loot's keep-aside of heavy finds stays M5 7 D149's); the equip pass acts on it (slice b).
    /// </summary>
    Replace,
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
    public string Verb => Kind switch { SelfEquipKind.Wear => "wear", SelfEquipKind.Swap => "swap in", SelfEquipKind.Review => "review", SelfEquipKind.Replace => "replace with", _ => "no gain from" };

    /// <summary>M5 7 D149: true for a wearable upgrade the mage can put on (a Wear in a free slot or a Swap for the held caster), the one kind of heavy find the loot rule keeps aside.</summary>
    public bool IsUpgrade => Kind is SelfEquipKind.Wear or SelfEquipKind.Swap;

    /// <summary>M6 14 (<c>m6-armour</c>): every worn piece a <see cref="SelfEquipKind.Replace"/> takes off (the wear pick's: a robe can displace several), the first being <see cref="Replaces"/>; empty for a single one.</summary>
    public IReadOnlyList<uint> Displaced { get; init; } = [];

    /// <summary>
    /// M6 14 (<c>m6-lore-spend</c>): for a <see cref="SelfEquipKind.Review"/> kept because her Arcane Lore alone leaves its spells off: the piece's ItemDifficulty, the lore that lights them (<see cref="SelfEquipJudge.LoreShort"/>); null otherwise.
    /// The wear pick's (<see cref="WearPick.LoreNeed"/>) carries its verdict at that lore in <see cref="LoreKind"/>, <see cref="LoreGain"/> and <see cref="LoreMargin"/>; the plain judge's carries the need alone (the loot's heavy rule reads it).
    /// </summary>
    public int? LoreNeed { get; init; }

    /// <summary>M6 14 (<c>m6-lore-spend</c>): what the wear pick would do with her lore at <see cref="LoreNeed"/> (Wear or Replace); None when not judged so.</summary>
    public SelfEquipKind LoreKind { get; init; }

    /// <summary>M6 14 (<c>m6-lore-spend</c>): the wear pick's gain at that lore, in percent of her hour (0 when not judged so).</summary>
    public double LoreGain { get; init; }

    /// <summary>M6 14 (<c>m6-lore-spend</c>): the margin that gain beat.</summary>
    public double LoreMargin { get; init; }

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
/// Creature_Magic.cs:41-50). M6 14 (<c>m6-caster</c>, the owner's ask of 2026-11-13): a candidate is an upgrade when its caster score (<see cref="GearScore.Caster"/>: the damage on the plan's element, the bolts a mana bar its Mana
/// Conversion gives by the mana model, its defences, in percent of the hour by the ladder's weights) beats the wielded one's by the ladder's swap margin; on an exact tie, when it casts more of the useful cantrips. Only a wield requirement
/// keeps a caster out of the pick; an unmet activation requirement leaves its spells off and its own bonuses in.</para>
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

    /// <summary>ACE's PropertyDataId ItemSkillLimit (the skill <see cref="ItemSkillLevelLimitInt"/> is read against), as the server holds it. It is no assessment property (PropertyDataId.cs:55 @95ef426d), so no appraisal carries it: <see cref="AppraisalItemSkillInt"/> does.</summary>
    public const uint ItemSkillLimitDid = 37;

    /// <summary>
    /// M6 16 E5e4: ACE's PropertyInt AppraisalItemSkill (PropertyInt.cs:250-251 @95ef426d): the skill <see cref="ItemSkillLevelLimitInt"/> is read against, as an appraisal carries it (ACE copies the item's ItemSkillLimit into it for the
    /// appraisal, AppraiseInfo.cs:410-411; retail's examine line reads the same pair, <c>Appraisal_ShowActivationRequirements</c>, 11.6096 0x004B3910).
    /// </summary>
    public const uint AppraisalItemSkillInt = 176;

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

    /// <summary>
    /// M6 2 a: the war elements by the ladder's names (<see cref="Grounds.Ladder.Elements"/>): ACE's DamageType bits (ACE.Entity/Enum/DamageType.cs @95ef426d: Slash 0x1, Pierce 0x2, Bludgeon 0x4, Cold 0x8, Fire 0x10, Acid 0x20,
    /// Electric 0x40).
    /// </summary>
    public static IReadOnlyDictionary<string, uint> ElementBits { get; } = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
    {
        ["slash"] = 0x1,
        ["pierce"] = 0x2,
        ["bludgeon"] = 0x4,
        ["cold"] = 0x8,
        ["fire"] = 0x10,
        ["acid"] = 0x20,
        ["electric"] = 0x40,
    };

    /// <summary>
    /// M6 2 a: an armour piece replaces the worn one in its coverage only when its <see cref="GearScore"/> is above the worn one's times this: a replacement is two server actions with the parts bare between them, and the score rests
    /// on averaged protections while the ground's damage types are a TODO (<c>m6-ground-damage-types</c>), so a gain under a tenth is not taken. A worn piece scored 0 (the Faran Robe with Hood, AL 0) is beaten by any armour.
    /// </summary>
    public const double ReplaceMargin = 1.10;

    private readonly HashSet<string> _useful;
    private readonly Func<uint, string?> _spellName;
    private readonly IReadOnlyList<uint>? _damageTypes;
    private readonly GearScoreNotes _notes;

    /// <summary>A judge.</summary>
    /// <param name="useful">The useful cantrip names (tier and family: "Major Focus"), the ground's keep-aside block's.</param>
    /// <param name="element">The rung's war element (ACE DamageType bits, <see cref="ElementOf(string?, string?, out string)"/>), 0 when not known.</param>
    /// <param name="spellName">The name of a spell id (the SpellTable's), or null.</param>
    /// <param name="damageTypes">M6 2 a: the ground's creatures' damage types (DamageType bits) for the armour score: <see cref="DamageTypesOf"/> of the ground (M6 3 a2), or null for a ground none of whose targets gives them (the profile's <c>damage_types</c>); <c>TODO(m6-ground-damage-types)</c> now means only that case (and a judge built with no ground at all), logged once a run.</param>
    /// <param name="notes">M6 2 a: the run's TODO latch for the scores, or null for one of the judge's own.</param>
    /// <param name="caster">M6 14 (<c>m6-caster</c>): the caster score's weights, bolt and auras (<see cref="CasterTerms.Read"/>), or null for <see cref="CasterTerms.Default"/>.</param>
    public SelfEquipJudge(IEnumerable<string> useful, uint element, Func<uint, string?> spellName, IReadOnlyList<uint>? damageTypes = null, GearScoreNotes? notes = null, CasterTerms? caster = null)
    {
        ArgumentNullException.ThrowIfNull(useful);
        ArgumentNullException.ThrowIfNull(spellName);
        _useful = new HashSet<string>(useful, StringComparer.OrdinalIgnoreCase);
        Element = element;
        _spellName = spellName;
        _damageTypes = damageTypes;
        _notes = notes ?? new GearScoreNotes();
        Caster = caster ?? CasterTerms.Default;
    }

    /// <summary>M6 14 (<c>m6-caster</c>): the caster score's terms this judge reads.</summary>
    public CasterTerms Caster { get; }

    /// <summary>M6 14 (<c>m6-caster</c>): this judge with other caster terms (the element, the useful cantrips, the damage types and the notes kept).</summary>
    /// <param name="caster">The terms.</param>
    public SelfEquipJudge WithCaster(CasterTerms caster) => new(_useful, Element, _spellName, _damageTypes, _notes, caster) { Wear = Wear, HeldBack = HeldBack, LitBy = LitBy, UsefulGreat = UsefulGreat };

    /// <summary>
    /// M6 14 (<c>m6-caster-spend</c>): the casters held back for the run (a swap whose wield did not land, <c>ResupplyStation.SwapBestCaster</c>): the pick (<see cref="BestCaster"/>), the rearm (<see cref="HeldCaster.Rearm"/>) and the spend
    /// (<see cref="ShortCasters"/>) pass them over, named. The session's one memory when the wardrobe builds the judge (<see cref="Quartermaster.Wardrobe.HeldBack"/>, M6 16 E5e2); a judge of no session has its own, empty.
    /// </summary>
    public CasterHeldBack HeldBack { get; init; } = new();

    /// <summary>
    /// M6 14 (<c>m6-armour</c>): the armour and jewellery pick's terms (<see cref="WearScore"/>), or null for the judge of before (a wearable with a useful cantrip worn in a free slot, armour by <see cref="GearScore.Armour"/>'s summed armour):
    /// with terms, every armour, clothing and jewellery piece is judged by its score in percent of her hour against what it displaces.
    /// </summary>
    public WearTerms? Wear { get; init; }

    /// <summary>M6 14 (<c>m6-armour</c>): this judge with the wear pick's terms.</summary>
    /// <param name="wear">The terms.</param>
    public SelfEquipJudge WithWear(WearTerms wear) => new(_useful, Element, _spellName, _damageTypes, _notes, Caster) { Wear = wear, HeldBack = HeldBack, LitBy = LitBy, UsefulGreat = UsefulGreat }; // M6 14 capacity: the lit pieces kept (the stored wear pick lost them before)

    /// <summary>
    /// M6 3 a2 (narrows <c>TODO(m6-ground-damage-types)</c>): the damage types the armour score is read against for <paramref name="ground"/>: the single DamageType bits (slash 0x1 to lightning 0x40, the seven protections) of the union of
    /// its targets' <see cref="GroundTarget.DamageTypes"/> (a creature's body-part attacks, Monster_Melee.cs:590-615 @95ef426d); null when no target gives any, which keeps the loud TODO and the seven-protection mean.
    /// </summary>
    /// <param name="ground">The ground.</param>
    public static IReadOnlyList<uint>? DamageTypesOf(HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        var union = 0u;
        for (var i = 0; i < ground.Targets.Count; i++)
        {
            if (ground.Targets[i].Role == GroundRole.Target)
            {
                union |= ground.Targets[i].DamageTypes;
            }
        }

        var bits = new List<uint>();
        for (var bit = 0x1u; bit <= 0x40u; bit <<= 1)
        {
            if ((union & bit) != 0)
            {
                bits.Add(bit);
            }
        }

        return bits.Count == 0 ? null : bits;
    }

    /// <summary>The rung's war element (DamageType bits), 0 when not known.</summary>
    public uint Element { get; }

    /// <summary>The element of a war family, or 0 (logged by the caller as <c>TODO(m5-self-equip-element)</c>).</summary>
    public static uint ElementOf(string? family) => family is not null && FamilyElements.TryGetValue(family, out var e) ? e : 0;

    /// <summary>
    /// M6 2 a (<c>m5-self-equip-element</c>): the war element from the rung first (<see cref="Grounds.LadderRung.Element"/>, "fire"), else from the spell plan's family (<see cref="FamilyElements"/>), else 0;
    /// <paramref name="source"/> names which, or the TODO when neither does.
    /// </summary>
    /// <param name="rungElement">The ladder rung's element, or null (a quest rung, or no ladder).</param>
    /// <param name="family">The spell plan's war family at the level, or null.</param>
    /// <param name="source">Where the element came from.</param>
    public static uint ElementOf(string? rungElement, string? family, out string source)
    {
        if (rungElement is not null && ElementBits.TryGetValue(rungElement, out var bits))
        {
            source = string.Create(CultureInfo.InvariantCulture, $"the rung's element {rungElement} 0x{bits:X}");
            return bits;
        }

        if (ElementOf(family) is var e and not 0)
        {
            source = string.Create(CultureInfo.InvariantCulture, $"the spell plan's family {family} 0x{e:X}{(rungElement is null ? string.Empty : $" (the rung's '{rungElement}' is no element)")}");
            return e;
        }

        source = $"TODO(m5-self-equip-element): neither the rung ('{rungElement ?? "none"}') nor the plan's family ('{family ?? "none"}') names an element";
        return 0;
    }

    /// <summary>M6 2 a: the score context of this judge for <paramref name="skills"/>.</summary>
    /// <param name="skills">The character's numbers.</param>
    public GearContext Context(IGearSkills skills) => new(skills, Element, _damageTypes, _useful, _spellName) { Notes = _notes, Caster = Caster, LitBy = LitBy, UsefulGreat = UsefulGreat, Load = Wear?.Load, Wear = Wear };

    /// <summary>
    /// M6 14 capacity: the caster pick's context: <see cref="Context(IGearSkills)"/> with the strongest Strength cantrip lit on the pieces she wears other than a caster (<see cref="GearContext.WornStrength"/>), so a caster's Strength
    /// cantrip counts only over it (ACE's top layer).
    /// </summary>
    /// <param name="skills">The character's numbers.</param>
    /// <param name="worn">The pieces worn and wielded now.</param>
    public GearContext Context(IGearSkills skills, IReadOnlyList<WornPiece> worn)
    {
        ArgumentNullException.ThrowIfNull(worn);
        var strength = 0.0;
        var tops = new Dictionary<(uint Type, uint Key), double>();
        foreach (var w in worn)
        {
            if ((w.ItemType & CasterType) == 0 && w.Appraisal is { IsNotFound: false } a && (LitBy?.Invoke(w.Guid) == true || WearScore.SpellsOff(a, skills) is null))
            {
                strength = Math.Max(strength, GearScore.StrengthCantrip(a));
                foreach (var id in a.Spells)
                {
                    if (CantripRows.Of(id) is { } row && (!tops.TryGetValue((row.Mod.Type, row.Mod.Key), out var held) || held < row.Mod.Value))
                    {
                        tops[(row.Mod.Type, row.Mod.Key)] = row.Mod.Value; // M6 14 casterfair: a caster's own cantrip counts only over the worn one of its family
                    }
                }
            }
        }

        return Context(skills) with { WornStrength = strength, WornCantrips = tops };
    }

    /// <summary>M6 14: the worn pieces lit on her now (<see cref="GearContext.LitBy"/>; <see cref="WearScore.LitOn"/> over her registry), or null (not read).</summary>
    public Func<uint, bool>? LitBy { get; init; }

    /// <summary>
    /// M6 16 E5e2 (the owner's standing rule: cantrips first, and a piece with a useful great cantrip is never taken off for a piece without one): true for a held or worn piece that casts a great cantrip useful to her
    /// (<see cref="CantripRoom.IsUsefulGreatPiece(GameState, uint, Func{uint, string}, CantripPlan, Action{string}, string)"/>: Major and above, a basic attribute or a skill she holds or her plan trains, her skills read
    /// live), whatever the keep-aside's names say. Neither the armour rule (the judge without the wear pick's terms) nor the wear pick (<see cref="WearScore.Judge"/>) replaces such a worn piece with a piece that casts none. Null: not read (a judge of no session).
    /// <para>M6 16 E5e5 (the owner's decision of 2026-11-18): this read is the first of two questions, "is this a keeper", and answers it whether or not she can activate the piece. The guards ask the second,
    /// "does its great cantrip count for her on her body" (<see cref="GearContext.GreatOn"/>, <see cref="GreatOnBody"/>): a keeper that can never light for her holds no worn slot against a piece that works, and is
    /// judged on what it does give.</para>
    /// </summary>
    public Func<uint, bool>? UsefulGreat { get; init; }

    /// <summary>The useful cantrips <paramref name="appraisal"/> casts, by name, in its order.</summary>
    public List<string> UsefulCantrips(Appraisal? appraisal) => GearScore.UsefulCantrips(appraisal, _useful, _spellName);

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
    public static string? Unmet(Appraisal appraisal, IGearSkills skills, List<string>? met = null) => WieldUnmet(appraisal, skills, met) ?? ActivationUnmet(appraisal, skills, met);

    /// <summary>
    /// M6 14 (<c>m6-caster</c>): the first wield requirement the character does not meet (ACE refuses the wield, <c>CheckWieldRequirement</c>, Player_Inventory.cs:2126-2229 @95ef426d), or null when every one is met. A caster whose
    /// activation requirement alone is unmet is wielded with its spells off (<see cref="ActivationUnmet"/>), so the caster pick reads this one.
    /// </summary>
    /// <param name="appraisal">The item's appraisal.</param>
    /// <param name="skills">The character's numbers.</param>
    /// <param name="met">Filled with the requirements met, for the log.</param>
    public static string? WieldUnmet(Appraisal appraisal, IGearSkills skills, List<string>? met = null)
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
                case 3: // Attrib: ACE reads the current attribute; the raw one read here is its floor (a buff only raises it), so a limit met here is met there
                case 4: // RawAttrib: the base
                case 5: // SecondaryAttrib: the vital's maximum
                case 6: // RawSecondaryAttrib: the vital's base; the maximum read here has the enchantments in, so a buffed maximum may pass here and be refused at the wield (ACE is the authority there)
                    var stat = r.Type <= 4 ? skills.Attribute(skill) : skills.VitalMax(skill);
                    var what = WieldStatName(r.Type, skill);
                    if (stat is null)
                    {
                        return string.Create(CultureInfo.InvariantCulture, $"a wield requirement on {what} ({r.Difficulty}) the read model does not hold (TODO(m5-self-equip-wield-type))");
                    }

                    if (stat < r.Difficulty)
                    {
                        return string.Create(CultureInfo.InvariantCulture, $"wield: {what} {stat} < {r.Difficulty} ({WieldRefusal(SkillTooLow)})");
                    }

                    met?.Add(string.Create(CultureInfo.InvariantCulture, $"wield: {what} {stat} >= {r.Difficulty}"));
                    break;
                default:
                    return string.Create(CultureInfo.InvariantCulture, $"a wield requirement of type {r.Type} (skill {r.Skill}, {r.Difficulty}) this code does not read (TODO(m5-self-equip-wield-type))");
            }
        }

        return null;
    }

    /// <summary>
    /// M6 14 (<c>m6-lore-spend</c>): the Arcane Lore points <paramref name="appraisal"/>'s ItemDifficulty (PropertyInt 109) asks over her current Arcane Lore, or null when it asks none (no difficulty, met, or her lore unread).
    /// ACE reads the CURRENT Arcane Lore (<c>CheckUseRequirements</c>, WorldObject_Use.cs:203-210 @95ef426d: <c>GetCreatureSkill(Skill.ArcaneLore).Current</c>, every buff on her in) once, at the wield (<c>TryActivateSpells</c>,
    /// Player_Inventory.cs:313-335); spells lit stay lit until the piece runs dry or comes off (ManaConsumersTick, Player_Tick.cs:681-699), whatever the lore does after.
    /// </summary>
    /// <param name="appraisal">The piece's appraisal.</param>
    /// <param name="skills">Her numbers.</param>
    /// <param name="lore">Her current Arcane Lore (0 when unread).</param>
    /// <param name="difficulty">The piece's ItemDifficulty (0 when none).</param>
    public static int? LoreShort(Appraisal appraisal, IGearSkills skills, out int lore, out int difficulty)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        ArgumentNullException.ThrowIfNull(skills);
        difficulty = appraisal.Int(AppraisalIds.ArcaneLore) ?? 0;
        lore = skills.Current(MageTemplate.ArcaneLore) ?? 0;
        return difficulty > 0 && skills.Current(MageTemplate.ArcaneLore) is { } read && read < difficulty ? difficulty - read : null;
    }

    /// <summary>M6 14 (<c>m6-lore-spend</c>): her numbers with her Arcane Lore at <paramref name="difficulty"/> (from <paramref name="lore"/>), everything else as hers: the pick's "with the lore there".</summary>
    /// <param name="skills">Her numbers.</param>
    /// <param name="lore">Her current Arcane Lore.</param>
    /// <param name="difficulty">The lore to read.</param>
    public static IGearSkills WithLore(IGearSkills skills, int lore, int difficulty) => new RaisedSkills(skills, [new CasterSkillShort(MageTemplate.ArcaneLore, lore, difficulty)]);

    /// <summary>
    /// M6 14 (<c>m6-caster</c>): the first activation requirement the character does not meet (<c>CheckUseRequirements</c>, WorldObject_Use.cs:191-324 @95ef426d): the item goes on, its spells stay off
    /// (<c>TryActivateSpells</c>, Player_Inventory.cs:313-335). Null when every one is met.
    /// </summary>
    /// <param name="appraisal">The item's appraisal.</param>
    /// <param name="skills">The character's numbers.</param>
    /// <param name="met">Filled with the requirements met, for the log.</param>
    public static string? ActivationUnmet(Appraisal appraisal, IGearSkills skills, List<string>? met = null)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        ArgumentNullException.ThrowIfNull(skills);

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

        // M6 16 E5e4 (live 2026-11-18 00:00:39 to 01:00:03, ladder67: ten re-wields answered "Your Missile Defense is too low to use that item's magic." or "Your Melee Defense ..."): the limit's skill reaches the client as
        // PropertyInt 176 in the appraisal; the server's own PropertyDataId 37 never does, so read alone it left every skill limit of loot armour (LootGenerationFactory_Magic.cs:177-203) unread and met
        if (appraisal.Int(ItemSkillLevelLimitInt) is { } limit and > 0 && ItemSkillLimitOf(appraisal) is { } limitSkill)
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

    /// <summary>
    /// The skill a piece's ItemSkillLevelLimit is read against, where an appraisal carries it (<see cref="AppraisalItemSkillInt"/>; the server's own <see cref="ItemSkillLimitDid"/> for a record that holds it), or null:
    /// one read for the activation check and for the question whether she can ever meet it (<see cref="GreatOnBody.NeverActivates"/>).
    /// </summary>
    /// <param name="appraisal">The piece's appraisal.</param>
    public static uint? ItemSkillLimitOf(Appraisal appraisal)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        return appraisal.Int(AppraisalItemSkillInt) is { } wire and > 0 ? (uint)wire : appraisal.DataId(ItemSkillLimitDid);
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

    /// <summary>M6 2 a: what a wield requirement of type 3 to 6 names (ACE <c>CheckWieldRequirement</c>, Player_Inventory.cs:2147-2178 @95ef426d), as the log prints it.</summary>
    private static string WieldStatName(int type, uint stat) => type switch
    {
        3 => AttributeName(stat) + " (current, read raw)",
        4 => AttributeName(stat) + " (base)",
        5 => VitalName(stat) + " (maximum)",
        _ => VitalName(stat) + " (base, read as the maximum)",
    };

    private static string VitalName(uint vital) => vital switch
    {
        1 => "Maximum Health",
        3 => "Maximum Stamina",
        5 => "Maximum Mana",
        _ => string.Create(CultureInfo.InvariantCulture, $"vital {vital}"),
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
        var context = Context(skills, worn);
        var score = GearScore.Caster(candidate.Guid, candidate.Name, appraisal, context);
        var heldScore = held?.Appraisal is { IsNotFound: false } ha ? GearScore.Caster(held.Guid, held.Name, ha, context) : null;
        var margin = Caster.Weights.SwapMargin;
        var numbers = string.Create(
            CultureInfo.InvariantCulture,
            $"{GearScore.CasterNumbers(score)} against {(heldScore is not null ? GearScore.CasterNumbers(heldScore) : held is null ? "no caster held" : $"the held '{held.Name}' 0x{held.Guid:X8} (not appraised: 0)")} on the plan's element 0x{Element:X} (its element 0x{appraisal.Int(DamageTypeInt) ?? 0:X}, bonus x{appraisal.Float(AppraisalIds.ElementalDamageMod) ?? 1.0:0.###}), the swap margin {margin:0.##}");
        var better = GearScore.CasterBeats(score, heldScore, held is null ? 0 : margin);
        if (!better)
        {
            // Not an upgrade: nothing to do here (a caster worth a look for its bonus is the keep-aside rule's, which keeps it in the review pack already).
            return new SelfEquipChoice(SelfEquipKind.None, candidate.Guid, candidate.Name, 0, 0, "not better: " + numbers);
        }

        var met = new List<string>();
        if (WieldUnmet(appraisal, skills, met) is { } unmet)
        {
            return new SelfEquipChoice(SelfEquipKind.Review, candidate.Guid, candidate.Name, 0, 0, "better but " + unmet + ": " + numbers);
        }

        var spells = ActivationUnmet(appraisal, skills, met) is { } off ? "; its spells stay off (" + off + "), its own bonuses count" : string.Empty;
        return new SelfEquipChoice(held is null ? SelfEquipKind.Wear : SelfEquipKind.Swap, candidate.Guid, candidate.Name, Held, held?.Guid ?? 0, "better: " + numbers + "; " + (met.Count == 0 ? "no requirement" : string.Join(", ", met)) + spells);
    }

    /// <summary>
    /// M6 14 (<c>m6-caster</c>): true when caster <paramref name="candidate"/> beats <paramref name="held"/> (null: no caster held) by the caster score (<see cref="GearScore.CasterBeats"/>, with the swap margin against a held caster),
    /// wield requirements aside (the callers check them). <paramref name="skills"/> null: the numbers of no character (the mana term then stands in by the skill's percent, named).
    /// </summary>
    /// <param name="candidate">The candidate's appraisal.</param>
    /// <param name="held">The held caster's appraisal, or null.</param>
    /// <param name="skills">The character's numbers, or null.</param>
    public bool Beats(Appraisal? candidate, Appraisal? held, IGearSkills? skills = null)
    {
        if (candidate is not { IsNotFound: false } c)
        {
            return false;
        }

        var context = Context(skills ?? NoSkills.Instance);
        var heldScore = held is { IsNotFound: false } h ? GearScore.Caster(h.Guid, string.Empty, h, context) : null;
        return GearScore.CasterBeats(GearScore.Caster(c.Guid, string.Empty, c, context), heldScore, heldScore is null ? 0 : Caster.Weights.SwapMargin);
    }

    /// <summary>The numbers of no character: a requirement-blind score (<see cref="Beats"/>).</summary>
    private sealed class NoSkills : IGearSkills
    {
        public static readonly NoSkills Instance = new();

        public int Level => 0;

        public int? Current(uint skill) => null;

        public int? Base(uint skill) => null;

        public int AdvancementClass(uint skill) => 0;
    }

    /// <summary>
    /// M5 7 S2c, M6 14 (<c>m6-caster</c>): the one caster to wield: the best by the caster score (<see cref="GearScore.Caster"/>) of every caster the mage holds whose wield requirements it meets (an unmet activation requirement leaves
    /// only the caster's spells off, so it stays in the field), against the wielded one by the ladder's swap margin; a Swap (or a Wear with none held) when that is not the wielded one, else null. <paramref name="why"/> names the pick,
    /// the field with every caster's score, the ones short of a wield requirement and the numbers.
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
        var context = Context(skills, worn);
        var wielded = worn.FirstOrDefault(w => (w.Location & Held) != 0 && (w.ItemType & CasterType) != 0);
        var wieldedScore = wielded?.Appraisal is { IsNotFound: false } wa ? GearScore.Caster(wielded.Guid, wielded.Name, wa, context) : null;
        GearCandidate? best = null;
        GearScoreResult? bestScore = null;
        int eligible = 0, unread = 0, unmet = 0;
        var field = new List<string>();
        var shortOf = new List<string>();
        var heldBack = new List<string>();
        foreach (var c in held)
        {
            if ((c.ItemType & CasterType) == 0)
            {
                continue;
            }

            if (HeldBack.IsHeldBack(c.Guid, out var failed))
            {
                heldBack.Add($"'{c.Name}' 0x{c.Guid:X8}: {failed}"); // M6 14 (m6-caster-spend): a wield that did not land is not sent again this run
                continue;
            }

            if (c.Appraisal is not { IsNotFound: false } a)
            {
                unread++; // M5 7 S2e: named, so a pick over unappraised casters is never silent
                continue;
            }

            if (WieldUnmet(a, skills) is { } refused)
            {
                unmet++;
                var shortfall = ShortfallOf(c, a, skills, wielded, wieldedScore, worn); // M6 14 (m6-caster-spend): scored as if wielded, the gain the spend reads
                shortOf.Add($"'{c.Name}' 0x{c.Guid:X8} {refused}{(shortfall.Other is null && shortfall.Skills.Count > 0 ? $": {shortfall.Numbers}; short {string.Join(", ", shortfall.Skills)}" : string.Empty)}");
                continue;
            }

            eligible++;
            var score = GearScore.Caster(c.Guid, c.Name, a, context);
            field.Add(string.Create(CultureInfo.InvariantCulture, $"'{c.Name}' 0x{c.Guid:X8} {score.CasterScore:0.##}"));
            if (bestScore is null || GearScore.CasterBeats(score, bestScore) || (Math.Abs(score.CasterScore - bestScore.CasterScore) <= 1e-6 && score.Cantrips == bestScore.Cantrips && c.Guid < best!.Guid))
            {
                (best, bestScore) = (c, score);
            }
        }

        var heldName = wielded is null ? "no caster" : string.Create(CultureInfo.InvariantCulture, $"'{wielded.Name}' 0x{wielded.Guid:X8} ({(wieldedScore is null ? "not appraised: 0" : wieldedScore.CasterScore.ToString("0.##", CultureInfo.InvariantCulture))})");
        var tail = string.Create(CultureInfo.InvariantCulture, $"field: {(field.Count == 0 ? "none" : string.Join(", ", field))}; {unread} not appraised, {unmet} short of a wield requirement{(shortOf.Count == 0 ? string.Empty : " (" + string.Join("; ", shortOf) + ")")}{(heldBack.Count == 0 ? string.Empty : $", {heldBack.Count} held back for the run ({string.Join("; ", heldBack)})")}; {Caster}");
        if (best is null || bestScore is null || !GearScore.CasterBeats(bestScore, wieldedScore, wielded is null ? 0 : Caster.Weights.SwapMargin))
        {
            // with the hands empty a caster that scores nothing is the rearm's (HeldCaster.Rearm), as before
            why = string.Create(CultureInfo.InvariantCulture, $"gear: caster pick: keep {heldName}: none of the {eligible} usable caster(s) held beats it by the score's margin {Caster.Weights.SwapMargin:0.##} on the plan's element 0x{Element:X}; {tail}");
            return null;
        }

        var choice = JudgeCaster(best, best.Appraisal!, worn, skills);
        why = string.Create(CultureInfo.InvariantCulture, $"gear: caster pick: '{best.Name}' 0x{best.Guid:X8} ({bestScore.CasterScore:0.##}), the best of {eligible} usable caster(s) held, over {heldName}; {tail}");
        return choice.Kind is SelfEquipKind.Swap or SelfEquipKind.Wear ? choice : null;
    }

    /// <summary>
    /// M6 14 (<c>m6-caster-spend</c>; the owner's ruling, 2026-11-14: she should definitely use the fire wand she has): every caster of <paramref name="held"/> whose wield requirement she does not meet, scored as if wielded: her skills with the
    /// skill-base points it is short added (<see cref="SkillBaseShorts"/>), so its element and damage bonuses on the plan's element, its Mana Conversion and its defences count as the pick counts a usable one's (<see cref="GearScore.Caster"/>),
    /// against the caster she wields. One with another requirement unmet even with those points (a level, a training, a current skill), or held back for the run, is listed with that reason (<see cref="CasterShortfall.Other"/>), so the
    /// spend's line names it. Pure; the experience spend reads it (<see cref="CasterSpend.Decide"/>).
    /// </summary>
    /// <param name="held">The casters held in the packs, with their appraisals.</param>
    /// <param name="worn">The pieces worn and wielded now.</param>
    /// <param name="skills">Her numbers.</param>
    public IReadOnlyList<CasterShortfall> ShortCasters(IReadOnlyList<GearCandidate> held, IReadOnlyList<WornPiece> worn, IGearSkills skills)
    {
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(worn);
        ArgumentNullException.ThrowIfNull(skills);
        var wielded = worn.FirstOrDefault(w => (w.Location & Held) != 0 && (w.ItemType & CasterType) != 0);
        var wieldedScore = wielded?.Appraisal is { IsNotFound: false } wa ? GearScore.Caster(wielded.Guid, wielded.Name, wa, Context(skills, worn)) : null;
        var list = new List<CasterShortfall>();
        foreach (var c in held)
        {
            if ((c.ItemType & CasterType) == 0 || c.Appraisal is not { IsNotFound: false } a || WieldUnmet(a, skills) is null)
            {
                continue;
            }

            var shortfall = ShortfallOf(c, a, skills, wielded, wieldedScore, worn);
            list.Add(HeldBack.IsHeldBack(c.Guid, out var failed) ? shortfall with { Other = "held back for the run (" + failed + ")" } : shortfall);
        }

        return list;
    }

    /// <summary>
    /// M6 14 (<c>m6-caster-spend</c>): the skill-base wield requirements (ACE's RawSkill, type 2, read against the skill's <c>Base</c>: <c>CheckWieldRequirement</c>, Player_Inventory.cs:2133-2146 @95ef426d) of <paramref name="appraisal"/> she is
    /// short of, one a skill (the highest requirement on it); empty when none.
    /// </summary>
    /// <param name="appraisal">The caster's appraisal.</param>
    /// <param name="skills">Her numbers.</param>
    public static IReadOnlyList<CasterSkillShort> SkillBaseShorts(Appraisal appraisal, IGearSkills skills)
    {
        ArgumentNullException.ThrowIfNull(appraisal);
        ArgumentNullException.ThrowIfNull(skills);
        var shorts = new List<CasterSkillShort>();
        foreach (var r in appraisal.WieldRequirements)
        {
            if (r.Type != 2 || skills.Base((uint)r.Skill) is not { } value || value >= r.Difficulty)
            {
                continue;
            }

            var at = shorts.FindIndex(s => s.Skill == (uint)r.Skill);
            if (at < 0)
            {
                shorts.Add(new CasterSkillShort((uint)r.Skill, value, r.Difficulty));
            }
            else if (shorts[at].Need < r.Difficulty)
            {
                shorts[at] = shorts[at] with { Need = r.Difficulty };
            }
        }

        return shorts;
    }

    /// <summary>One caster short of a wield requirement, scored as if wielded (<see cref="ShortCasters"/>).</summary>
    private CasterShortfall ShortfallOf(GearCandidate c, Appraisal a, IGearSkills skills, WornPiece? wielded, GearScoreResult? wieldedScore, IReadOnlyList<WornPiece> worn)
    {
        var shorts = SkillBaseShorts(a, skills);
        IGearSkills raised = shorts.Count == 0 ? skills : new RaisedSkills(skills, shorts);
        var other = WieldUnmet(a, raised);
        var score = GearScore.Caster(c.Guid, c.Name, a, Context(raised, worn));
        var margin = wielded is null ? 0 : Caster.Weights.SwapMargin;
        var heldName = wielded is null ? "no caster held" : string.Create(CultureInfo.InvariantCulture, $"the held '{wielded.Name}' 0x{wielded.Guid:X8}");
        var blocked = other is null ? null : "short of " + other + (shorts.Count == 0 ? string.Empty : " even with the skill-base points bought") + ", which no experience spend gives";
        return new CasterShortfall(c.Guid, c.Name, shorts, blocked, score.CasterScore, heldName, wieldedScore?.CasterScore ?? 0, margin, GearScore.CasterBeats(score, wieldedScore, margin));
    }

    private SelfEquipChoice JudgeWearable(GearCandidate candidate, Appraisal appraisal, IReadOnlyList<WornPiece> worn, IGearSkills skills)
    {
        if (Wear is { } wear && GearScore.SlotOf(candidate.ItemType, candidate.Coverage, candidate.ValidLocations) is GearSlot.Armour or GearSlot.Jewellery)
        {
            return WearScore.Judge(candidate, worn, Context(skills), wear).ToChoice(); // M6 14 (m6-armour): the score in percent of her hour
        }

        var cantrips = UsefulCantrips(appraisal);
        if (cantrips.Count == 0)
        {
            return JudgeArmour(candidate, worn, skills);
        }

        // M6 16 E5e5: a piece whose named cantrips cannot light for her now (she can wield it; an activation requirement is unmet) is a candidate on its armour alone, no better and no worse than a plain piece of
        // its armour; when its armour wins it nothing, it is kept for the review below, as before
        if (WieldUnmet(appraisal, skills) is null && ActivationUnmet(appraisal, skills) is { } dark && JudgeArmour(candidate, worn, skills) is { Kind: SelfEquipKind.Wear or SelfEquipKind.Replace } onArmour)
        {
            return onArmour with { Why = $"{string.Join(", ", cantrips)} would stay off ({dark}): judged on its armour alone: {onArmour.Why}" };
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
            // M6 14 (m6-lore-spend; the owner, 2026-11-14: keep great cantrips while her Arcane Lore cannot light them, the lore will rise): the need named when her Arcane Lore alone stands between it and its spells
            var loreNeed = WieldUnmet(appraisal, skills) is null && LoreShort(appraisal, skills, out var lore, out var difficulty) is not null && ActivationUnmet(appraisal, WithLore(skills, lore, difficulty)) is null ? difficulty : (int?)null;
            return new SelfEquipChoice(SelfEquipKind.Review, candidate.Guid, candidate.Name, 0, 0, $"{list}, but {unmet}{(loreNeed is null ? string.Empty : "; her Arcane Lore alone keeps its spells off: a wear once it reaches " + loreNeed.Value.ToString(CultureInfo.InvariantCulture))}") { LoreNeed = loreNeed };
        }

        return new SelfEquipChoice(SelfEquipKind.Wear, candidate.Guid, candidate.Name, location, 0, string.Create(CultureInfo.InvariantCulture, $"casts {string.Join(", ", fresh)} that no worn piece casts, at the free location 0x{location:X8}; {(met.Count == 0 ? "no requirement" : string.Join(", ", met))}"));
    }

    /// <summary>
    /// M6 2 a: a piece with no useful cantrip, judged as armour by its <see cref="GearScore"/>: worn in a free coverage when it scores above 0; in place of the one worn piece in its coverage when it beats that piece's score by
    /// <see cref="ReplaceMargin"/> and, that piece off, ACE's coverage rule lets it on (<see cref="FreeLocation"/>); else nothing (never the review pack: the M5 den's review and keep-aside stay the cantrip pieces'). A worn piece not
    /// appraised, or one that casts a useful cantrip (by the list's names, or a great one useful to her: <see cref="UsefulGreat"/>, M6 16 E5e2), is never replaced; two worn pieces in the way are not taken off for one.
    /// M6 16 E5e5: a worn keeper whose great cantrip can never light for her (<see cref="GreatOnBodyKind.Never"/>) is the exception: it is replaced when beaten. Either piece is scored on what it gives: with an unmet
    /// activation requirement its spells are off and its armour counts (<see cref="GearScore.Armour"/>), so a worn piece behind a skill limit is never beaten by weaker armour, and a find whose spells cannot light is a
    /// candidate on its armour alone.
    /// </summary>
    private SelfEquipChoice JudgeArmour(GearCandidate candidate, IReadOnlyList<WornPiece> worn, IGearSkills skills)
    {
        if (GearScore.SlotOf(candidate.ItemType, candidate.Coverage, candidate.ValidLocations) != GearSlot.Armour)
        {
            return new SelfEquipChoice(SelfEquipKind.None, candidate.Guid, candidate.Name, 0, 0, "no useful cantrip");
        }

        var context = Context(skills);
        var score = GearScore.Of(candidate, context);
        if (score.Total <= 0)
        {
            return new SelfEquipChoice(SelfEquipKind.None, candidate.Guid, candidate.Name, 0, 0, "no useful cantrip and no armour gain: " + score);
        }

        var met = new List<string>();
        var spellsOff = Unmet(candidate.Appraisal!, skills, met); // the wield requirements are met (the score is 0 when one is not); an unmet activation leaves its spells off and its armour counted (M6 16 E5e5)
        var requirements = (met.Count == 0 ? "no requirement" : string.Join(", ", met)) + (spellsOff is null ? string.Empty : "; its spells stay off (" + spellsOff + "): its armour alone counts");
        var inTheWay = worn.Where(w => (w.Coverage & candidate.Coverage) != 0).ToList();
        if (inTheWay.Count == 0)
        {
            return new SelfEquipChoice(SelfEquipKind.Wear, candidate.Guid, candidate.Name, candidate.ValidLocations, 0, string.Create(CultureInfo.InvariantCulture, $"armour in a free coverage 0x{candidate.Coverage:X}: {score}; {requirements}"));
        }

        if (inTheWay.Count > 1)
        {
            return new SelfEquipChoice(SelfEquipKind.None, candidate.Guid, candidate.Name, 0, 0, $"no useful cantrip; its coverage 0x{candidate.Coverage:X} would take off {inTheWay.Count} worn pieces ({string.Join(", ", inTheWay.Select(w => $"'{w.Name}' 0x{w.Guid:X8}"))}): not replaced; {score}");
        }

        var piece = inTheWay[0];
        if (piece.Appraisal is not { IsNotFound: false })
        {
            return new SelfEquipChoice(SelfEquipKind.None, candidate.Guid, candidate.Name, 0, 0, $"no useful cantrip; the worn '{piece.Name}' 0x{piece.Guid:X8} in its coverage is not appraised: no replacement on a guess; {score}");
        }

        // M6 16 E5e5 (the owner's decision of 2026-11-18: she wears what works): a worn keeper whose great cantrip can never light for her (GreatOnBody) holds its slot neither by the list's names nor by the guard below; it
        // is judged on the armour it gives with its spells off, and taken off it is kept (the keep lists never asked whether she can activate it)
        var onBody = context.GreatOn(piece.Guid, piece.Appraisal);
        var dead = onBody.Kind == GreatOnBodyKind.Never;
        if (!dead && UsefulCantrips(piece.Appraisal) is { Count: > 0 } kept)
        {
            return new SelfEquipChoice(SelfEquipKind.None, candidate.Guid, candidate.Name, 0, 0, $"no useful cantrip; the worn '{piece.Name}' 0x{piece.Guid:X8} casts {string.Join(", ", kept)}: kept; {score}");
        }

        if (onBody.HoldsSlot && context.GreatOn(candidate.Guid, candidate.Appraisal) is { HoldsSlot: false } own)
        {
            // M6 16 E5e2: the names of the keep-aside list are not every great cantrip useful to her (a Major Strength is on no list): the owner's rule is asked of the piece itself
            return new SelfEquipChoice(SelfEquipKind.None, candidate.Guid, candidate.Name, 0, 0, $"no useful cantrip; the worn '{piece.Name}' 0x{piece.Guid:X8} casts a great cantrip useful to her and {GreatOnBody.CastsNone(own)}: never taken off for it; {score}");
        }

        var wornScore = GearScore.Of(piece, context); // M6 16 E5e5: a worn piece whose spells cannot light is scored on the armour it gives, never 0 (GearScore.Armour)
        var deadNote = dead ? string.Create(CultureInfo.InvariantCulture, $"; {GreatOnBody.NoSlot(piece, onBody)}") : string.Empty;
        if (score.Total <= (wornScore.Total * ReplaceMargin) + 1e-6)
        {
            return new SelfEquipChoice(SelfEquipKind.None, candidate.Guid, candidate.Name, 0, 0, string.Create(CultureInfo.InvariantCulture, $"not above the worn '{piece.Name}' 0x{piece.Guid:X8} by x{ReplaceMargin:0.##}: {score} against {wornScore}{deadNote}"));
        }

        if (FreeLocation(candidate, worn.Where(w => w.Guid != piece.Guid).ToList(), out var blocker) is not { } location)
        {
            return new SelfEquipChoice(SelfEquipKind.None, candidate.Guid, candidate.Name, 0, 0, $"better than the worn '{piece.Name}' 0x{piece.Guid:X8}, but with it off {blocker}; {score}");
        }

        // M6 2 f (2-R5): a Replace never leaves the chest bare. The Faran Robe with Hood (AL 0) scores 0, so any one-part piece in its coverage would out-score it and take the whole robe off; the floor would then buy a robe that cannot go on
        // (the piece blocks its head or feet). Refused, not scored over the coverage it removes: the floor's chest rule is a floor, and a score that trades chest cover for armour level is tuning the plan does not ask for.
        var coversChest = (candidate.ValidLocations & GearFloor.ChestArmor) != 0 || (candidate.Coverage & GearFloor.OuterwearChest) != 0;
        if (!coversChest && GearFloor.ChestPiece([.. worn.Where(w => w.Guid != piece.Guid)]) is null && GearFloor.ChestPiece([piece]) is not null)
        {
            return new SelfEquipChoice(SelfEquipKind.None, candidate.Guid, candidate.Name, 0, 0, $"better than the worn '{piece.Name}' 0x{piece.Guid:X8}, but it is the only chest-covering piece worn and this one does not cover the chest: not replaced (it would leave the chest bare); {score}");
        }

        // M6 item 14 (armour instead of the robe): a Replace never bares a part the worn piece armours. The Pathwarden Plate Hauberk covers the chest, the abdomen and the arms (0x3C00); a looted breastplate (the chest alone) that out-scores it
        // would take the whole hauberk off and leave three parts with no armour, and ACE reads a hit's armour by the part struck (Monster_Melee.cs:409-419 @95ef426d). A worn piece of armour level 0 (the Faran Robe with Hood) armours nothing,
        // so its replacement is judged as before.
        var bared = piece.Coverage & GearCoverage.Body & ~candidate.Coverage;
        if (bared != 0 && ArmourBase.Level(piece.Appraisal) is > 0 and var wornLevel)
        {
            return new SelfEquipChoice(SelfEquipKind.None, candidate.Guid, candidate.Name, 0, 0, string.Create(CultureInfo.InvariantCulture, $"better than the worn '{piece.Name}' 0x{piece.Guid:X8} where it covers, but that piece armours 0x{bared:X} more of the body (armour level {wornLevel}) which this one does not cover: not replaced (it would leave those parts bare); {score}"));
        }

        // M6 item 14: an attuned worn piece (the Pathwarden set) is replaced only by more armour, never by a lighter piece of less. The score weighs a burden against the capacity of the base Strength, so the hauberk (2,500) scores next
        // to nothing on a Strength of 10 to 18 and a weaker, lighter coat would out-score it; but an attuned piece cannot be dropped (Player_Inventory.cs:1389-1393 @95ef426d), so taken off it is still carried until Thorolf takes it back,
        // and its own lines (Strength Self II) go with it: no burden is saved and armour is lost.
        var (armourNew, armourWorn) = (score.Part("armour") + score.Part("cantrip armour"), wornScore.Part("armour") + wornScore.Part("cantrip armour"));
        if (GearScore.Attuned(piece.Appraisal) && armourNew < armourWorn)
        {
            return new SelfEquipChoice(SelfEquipKind.None, candidate.Guid, candidate.Name, 0, 0, string.Create(CultureInfo.InvariantCulture, $"lighter than the worn '{piece.Name}' 0x{piece.Guid:X8}, but its armour {armourNew:0.##} is under that piece's {armourWorn:0.##} and that piece is attuned (taken off it is still carried: no burden is saved): not replaced; {score} against {wornScore}"));
        }

        return new SelfEquipChoice(
            SelfEquipKind.Replace,
            candidate.Guid,
            candidate.Name,
            location,
            piece.Guid,
            string.Create(CultureInfo.InvariantCulture, $"above the worn '{piece.Name}' 0x{piece.Guid:X8} by more than x{ReplaceMargin:0.##}: {score} against {wornScore}; ACE lets it on with that piece off (Creature_Equipment.cs:50-58, 93-105); {requirements}{deadNote}"));
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

    private static int ArmorOf(GearCandidate c) => c.Appraisal is { } a ? ArmourBase.Level(a) : 0;

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

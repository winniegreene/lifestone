using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Mage;

/// <summary>M6 2 a: the slot a <see cref="GearScoreResult"/> scores.</summary>
public enum GearSlot
{
    /// <summary>Not a piece the score reads (no caster, no coverage, no jewellery location).</summary>
    Other,

    /// <summary>The held caster.</summary>
    Caster,

    /// <summary>Armour or clothing: a piece with a coverage (ClothingPriority).</summary>
    Armour,

    /// <summary>Jewellery and the trinket: a piece worn at a location, no coverage.</summary>
    Jewellery,
}

/// <summary>One named component of a score, as the log line prints it.</summary>
/// <param name="Name">The component's name.</param>
/// <param name="Value">Its value in the slot's unit.</param>
/// <param name="Note">What decided it, or null.</param>
public readonly record struct GearScorePart(string Name, double Value, string? Note = null)
{
    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Name} {Value:0.##}{(Note is null ? string.Empty : " (" + Note + ")")}");
}

/// <summary>
/// M6 2 a: one piece's score in its slot, with the named components (<see cref="Parts"/>) the log line prints: <c>gear score: &lt;slot&gt; '&lt;name&gt;' 0x&lt;guid&gt; &lt;total&gt; = &lt;parts&gt;</c>.
/// </summary>
/// <param name="Slot">The slot scored.</param>
/// <param name="Guid">The piece.</param>
/// <param name="Name">Its name.</param>
/// <param name="Total">The slot's number: for a caster its score in percent of the hour (<see cref="CasterScore"/>, M6 14: war damage, bolts a mana bar and defences by the ladder's weights; the cantrips break an exact tie); for
/// armour the summed armour over its covered parts for the ground's damage types, less the burden; for jewellery its useful cantrips. 0 when <see cref="Unmet"/> names a requirement.</param>
/// <param name="Parts">The components, in order.</param>
/// <param name="Unmet">The first requirement the character does not meet (<see cref="SelfEquipJudge.Unmet"/>; for a caster, and since M6 16 E5e5 for armour, the wield requirements alone, <see cref="SelfEquipJudge.WieldUnmet"/>: an unmet activation leaves <see cref="SpellsOff"/>), or null.</param>
/// <param name="Attuned">PropertyInt 114 Attuned read raw: true when above 0 (ACE <c>AttunedStatus</c>: an attuned piece cannot be dropped or given to a player, Player_Inventory.cs:1389-1393, 3262-3266 @95ef426d).</param>
/// <param name="Bonded">PropertyInt 33 Bonded read raw: true when above 0 (ACE <c>BondedStatus</c> Bonded 1, Sticky 2: it never drops on death, Player_Death.cs:629-663).</param>
/// <param name="ManaHours">The hours of item mana left at its ManaRate (PropertyFloat 5), or null for a piece that burns none.</param>
public sealed record GearScoreResult(GearSlot Slot, uint Guid, string Name, double Total, IReadOnlyList<GearScorePart> Parts, string? Unmet, bool Attuned, bool Bonded, double? ManaHours)
{
    /// <summary>A caster's ElementalDamageMod counts: its element is the rung's (<see cref="GearSlot.Caster"/> only).</summary>
    public bool OnElement { get; init; }

    /// <summary>
    /// A caster's war bonus in percent, requirements aside: what ACE multiplies her bolt of the judge's element by with it in her hands, against a plain caster she has mastered: its elemental bonus with her Spirit Drinker
    /// aura, its resistance cleaving, and the mastery bonus a caster that is tough to master lacks (M6 16 E5e3; negative for such a caster off its element).
    /// </summary>
    public double WarPercent { get; init; }

    /// <summary>A caster's ManaConversionMod in percent, times her Hermetic Link aura (what multiplies her Mana Conversion in the magic mode).</summary>
    public double ManaConversionPercent { get; init; }

    /// <summary>M6 14 (<c>m6-caster</c>): the percent more bolts a mana bar the caster's Mana Conversion bonus gives (<see cref="Casting.ManaConversionModel.ExpectedCost"/> at her skill and at her skill times the caster's modifier).</summary>
    public double CastsPercent { get; init; }

    /// <summary>M6 14: the caster's melee defence modifier above 1, her Defender aura in, in percent.</summary>
    public double MeleeDefencePercent { get; init; }

    /// <summary>M6 14: the caster's magic defence modifier above 1, in percent.</summary>
    public double MagicDefencePercent { get; init; }

    /// <summary>M6 14 (<c>m6-caster</c>): the caster's score in percent of the hour (<see cref="Grounds.LadderCasterScore"/>'s weights), requirements aside: the comparison's key (<see cref="GearScore.CasterBeats"/>).</summary>
    public double CasterScore { get; init; }

    /// <summary>M6 14: the activation requirement that leaves the caster's spells off (its own bonuses still count), or null. M6 16 E5e5: an armour piece's too (its armour level still counts).</summary>
    public string? SpellsOff { get; init; }

    /// <summary>The useful cantrips counted (a piece out of mana counts none: its spells are off).</summary>
    public int Cantrips { get; init; }

    /// <summary>M6 14 capacity: a caster's Strength cantrip as capacity on her load, in percent of the hour (<see cref="GearScore.CapacityPart"/>), part of <see cref="CasterScore"/>.</summary>
    public double CapacityPercent { get; init; }

    /// <summary>M6 14 casterfair: a caster's own skill cantrips over what she wears, in percent of the hour, part of <see cref="CasterScore"/>.</summary>
    public double SkillCantripPercent { get; init; }

    /// <summary>The value of a component by name, or 0.</summary>
    /// <param name="name">The component's name.</param>
    public double Part(string name) => Parts.FirstOrDefault(p => p.Name == name).Value;

    /// <summary>The slot as the log prints it.</summary>
    public string SlotName => Slot switch { GearSlot.Caster => "caster", GearSlot.Armour => "armour", GearSlot.Jewellery => "jewellery", _ => "other" };

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"gear score: {SlotName} '{Name}' 0x{Guid:X8} {Total:0.##} = {string.Join(", ", Parts)}{(Unmet is null ? string.Empty : "; 0: " + Unmet)}{(Attuned ? "; attuned" : string.Empty)}{(Bonded ? "; bonded" : string.Empty)}");
}

/// <summary>
/// M6 2 a: the TODOs a run's scores hit, each logged once (<c>TODO(m6-ground-damage-types)</c> on a ground none of whose targets gives <c>damage_types</c>); the run owns one, so a test's latch is its own. The score lines name the TODO too.
/// </summary>
/// <param name="sink">Where a first-time note goes (the run's log), or null to keep the names only.</param>
public sealed class GearScoreNotes(Action<string>? sink = null)
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<string> _noted = [];

    /// <summary>The TODO names noted so far, in order.</summary>
    public IReadOnlyList<string> Noted => _noted;

    /// <summary>Notes <paramref name="todo"/> once: the first call writes <paramref name="line"/> to the sink, later ones nothing.</summary>
    /// <param name="todo">The TODO's name.</param>
    /// <param name="line">The line logged the first time.</param>
    /// <returns>True the first time.</returns>
    public bool Once(string todo, string line)
    {
        lock (_seen)
        {
            if (!_seen.Add(todo))
            {
                return false;
            }

            _noted.Add(todo);
        }

        sink?.Invoke(line);
        return true;
    }
}

/// <summary>
/// M6 2 a: what a score is read against: the character (level, base skills, capacity through <see cref="IGearSkills"/>) and the ground (the rung's war element, its creatures' damage types). No ground constant (M6 D26): the
/// damage types are the profile's optional per-target <c>damage_types</c> (M6 item 3; <c>SelfEquipJudge.DamageTypesOf</c> takes their union), so they are optional here and a ground none of whose targets gives them is the loud <c>TODO(m6-ground-damage-types)</c>.
/// </summary>
/// <param name="Skills">The character's numbers (the wield requirements read base War Magic for a caster: ACE <c>CheckWieldRequirement</c> RawSkill, Player_Inventory.cs:2080-2146 @95ef426d).</param>
/// <param name="Element">The rung's war element (ACE DamageType bits, <see cref="SelfEquipJudge.ElementOf(string?, string?, out string)"/>), 0 when not known.</param>
/// <param name="DamageTypes">The ground's creatures' damage types (DamageType bits), or null while the profile has no such field.</param>
/// <param name="Useful">The useful cantrip names (the ground's keep-aside block).</param>
/// <param name="SpellName">The name of a spell id (the SpellTable's), or null.</param>
public sealed record GearContext(IGearSkills Skills, uint Element, IReadOnlyList<uint>? DamageTypes, IReadOnlySet<string> Useful, Func<uint, string?> SpellName)
{
    /// <summary>ACE's encumbrance capacity per point of Strength (<c>GetEncumbranceCapacity</c>, Player_Inventory.cs:46-51 @95ef426d: 150 x Strength plus the augmentation, which a mage on the ladder has none of).</summary>
    public const int CapacityPerStrength = 150;

    /// <summary>ACE's PropertyAttribute Strength.</summary>
    public const uint Strength = 1;

    /// <summary>The burden the character carries besides the piece scored, or null (the free capacity is then the whole capacity).</summary>
    public int? CarriedBurden { get; init; }

    /// <summary>The run's TODO latch.</summary>
    public GearScoreNotes Notes { get; init; } = new();

    /// <summary>M6 14 (<c>m6-caster</c>): the caster score's weights, bolt and auras (<see cref="CasterTerms"/>).</summary>
    public CasterTerms Caster { get; init; } = CasterTerms.Default;

    /// <summary>
    /// M6 14 (live 2026-11-14 17:33, ladder11: after a death her Arcane Lore read 213 without her buffs, the wear pick judged the worn Major Mana Conversion Prowess bracelet's spells off (its difficulty 222), swapped it for an
    /// Armor Self V bracelet and the sale sold it): true when a worn piece's spells are on her now, an enchantment in her registry cast by the piece (<see cref="WearScore.LitOn"/>). ACE checks the lore only when a piece is
    /// wielded (<c>TryActivateSpells</c>, Player_Inventory.cs:313-335 @95ef426d): once lit, the spells stay until the piece comes off or runs dry, whatever her lore. Null: not read (the activation check decides, as before).
    /// </summary>
    public Func<uint, bool>? LitBy { get; init; }

    /// <summary>
    /// M6 16 E5e2 (the owner's standing rule: a piece with a useful great cantrip is never taken off for a piece without one): true for a piece that casts a great cantrip useful to her
    /// (<see cref="SelfEquipJudge.UsefulGreat"/>, the session's read). The wear pick refuses a replace that would take such a worn piece off for a piece that casts none. Null: not read (a judge of no session).
    /// </summary>
    public Func<uint, bool>? UsefulGreat { get; init; }

    /// <summary>
    /// M6 16 E5e5 (the owner's decision of 2026-11-18: she wears what works): the second question, "does this piece's great cantrip count for her on her body" (<see cref="GreatOnBody"/>): a keeper
    /// (<see cref="UsefulGreat"/>) that is lit on her (<see cref="LitBy"/>), that ACE would light at a wield now, or that cannot light yet; a keeper that can never light for her does not hold a slot. With
    /// <see cref="UsefulGreat"/> unread the answer is <see cref="GreatOnBodyKind.None"/>, as a judge of no session read before.
    /// </summary>
    /// <param name="guid">The piece.</param>
    /// <param name="appraisal">Its appraisal, or null.</param>
    public GreatOnBody GreatOn(uint guid, Appraisal? appraisal) => GreatOnBody.Of(UsefulGreat?.Invoke(guid) == true, LitBy?.Invoke(guid) == true, appraisal, Skills);

    /// <summary>
    /// M6 14 capacity (the owner's ruling, 2026-11-17: in her major strength shoes she would have a lot more capacity): her live load the capacity term reads (<see cref="GearScore.CapacityPart"/>), or null (a Strength cantrip
    /// counts no capacity, named).
    /// </summary>
    public CapacityLoad? Load { get; init; }

    /// <summary>
    /// M6 14 capacity: the strongest Strength cantrip lit on the pieces she wears other than the held caster (the caster pick's baseline: ACE applies the top layer of a spell category, <c>GetEnchantments_TopLayer</c>,
    /// EnchantmentManager.cs:123-129 @95ef426d, so a caster's Major Strength adds nothing over a worn Major Strength). 0 when none.
    /// </summary>
    public double WornStrength { get; init; }

    /// <summary>M6 14 casterfair: the strongest cantrip of each family (by its stat's type and key) lit on the pieces she wears other than the caster: a caster's own cantrip counts only over it.</summary>
    public IReadOnlyDictionary<(uint Type, uint Key), double> WornCantrips { get; init; } = new Dictionary<(uint, uint), double>();

    /// <summary>M6 14 casterfair: the wear pick's terms, which weigh a caster's own skill cantrips (<see cref="WearScore.SkillPoints"/>), or null.</summary>
    public WearTerms? Wear { get; init; }

    /// <summary>The character's level.</summary>
    public int Level => Skills.Level;

    /// <summary>The capacity, 150 x Strength (the raw Strength: a buff only raises ACE's <c>Current</c>, so this is the floor), or null while Strength is unread.</summary>
    public int? Capacity => Skills.Attribute(Strength) is { } s ? s * CapacityPerStrength : null;

    /// <summary>A context from the judge's pieces.</summary>
    /// <param name="skills">The character's numbers.</param>
    /// <param name="element">The war element bits.</param>
    /// <param name="damageTypes">The ground's damage types, or null.</param>
    /// <param name="useful">The useful cantrip names.</param>
    /// <param name="spellName">The spell names.</param>
    public static GearContext For(IGearSkills skills, uint element, IReadOnlyList<uint>? damageTypes, IEnumerable<string> useful, Func<uint, string?> spellName) =>
        new(skills, element, damageTypes, new HashSet<string>(useful, StringComparer.OrdinalIgnoreCase), spellName);
}

/// <summary>
/// M6 14 capacity: her live load as the capacity term reads it (<see cref="GearScore.CapacityPart"/>).
/// </summary>
/// <param name="Capacity">Her burden capacity now: ACE's <c>GetEncumbranceCapacity</c>, 150 x her CURRENT Strength (the enchantments in) plus the augmentation (Player_Inventory.cs:46-51 @95ef426d;
/// <c>EncumbranceSystem.EncumbranceCapacity</c>, Physics/Common/EncumbranceSystem.cs:5-20), as the loot reads it (<see cref="Fighter.LootContext.Of"/>).</param>
/// <param name="Carried">The burden she carries now.</param>
/// <param name="LinePercent">The loot's load line in percent of the capacity (<see cref="MageLootThresholds.LoadLinePercent"/>; 0: no line, the whole capacity).</param>
/// <param name="BurdenWeight">Percent of her hour a percent of her capacity is worth (the wear score's <c>burden</c> weight, <see cref="Grounds.LadderWearScore.Burden"/>: the loot a full pack leaves on a corpse).</param>
public sealed record CapacityLoad(int Capacity, int Carried, int LinePercent, double BurdenWeight)
{
    /// <summary>The load line in burden: <see cref="LinePercent"/> of the capacity (the whole capacity when 0).</summary>
    public int Line => LinePercent > 0 ? (int)((long)Capacity * LinePercent / 100) : Capacity;

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"burden {Carried} of {Capacity} (load line {Line})");
}

/// <summary>
/// M6 2 a (the ladder note section 11.2, "one score per slot, from an appraisal, against the ground"): a pure per-slot score of one piece from its appraisal (plus the weenie header's coverage and burden) against a
/// <see cref="GearContext"/>. Retail has no such score (what to wear is the player's choice); the rules it rests on are ACE's, cited where used.
/// </summary>
/// <remarks>
/// <para><b>A caster</b> (M6 14, <c>m6-caster</c>; the owner, 2026-11-13: a caster with real Mana Conversion and defence bonuses no longer ties with a bare Training Wand). 0 when a wield requirement is unmet
/// (<see cref="SelfEquipJudge.WieldUnmet"/>; ACE refuses the wield, Player_Inventory.cs:2126-2229 @95ef426d); an activation requirement leaves only its spells off (<c>TryActivateSpells</c>, Player_Inventory.cs:313-335), so its own bonuses
/// count and its cantrips do not. Its number is a score in percent of her experience an hour, the ladder's weights (<see cref="Grounds.LadderCasterScore"/>) times: the war bonus on the rung's element (ACE multiplies a war spell's
/// damage by the caster's ElementalDamageMod, PropertyFloat 152, plus her Spirit Drinker aura, only when its DamageType, PropertyInt 45, is the spell's element: <c>GetCasterElementalDamageModifier</c>, WorldObject_Weapon.cs:382-401;
/// a bonus off the rung's element is 0), times a quest weapon's resistance cleaving on that element and without the mastery bonus a caster that is tough to master lacks (M6 16 E5e3, where used); the percent more bolts a mana bar its ManaConversionMod (PropertyFloat 144, times her Hermetic Link aura) gives, by the mana model on her own skill and the plan's bolt (ACE rounds her Mana
/// Conversion times <c>GetWeaponManaConversionModifier</c> in the magic mode, Creature_Magic.cs:48, WorldObject_Weapon.cs:235-263; <see cref="Casting.ManaConversionModel.ExpectedCost"/>), whatever the caster's element; its melee
/// defence modifier (PropertyFloat 29, plus her Defender aura: :86-129) and its magic defence modifier (PropertyFloat 150: :159-179) above 1. The useful cantrips break an exact tie. The hours of item mana left are shown; a caster out of
/// mana counts no cantrip.</para>
/// <para><b>Armour and clothing.</b> Per covered body part (ACE <c>BodyPart.GetCoverageMask</c>, Entity/BodyPart.cs:115-140: nine parts, an underwear and an outerwear bit for the trunk and limbs), the part's armour from this piece is
/// <c>(ArmorLevel + its Impenetrability) x its protection vs the damage type</c>, the protection clamped to [-2, 2] (<c>GetArmorMod</c>, Creature_BodyPart.cs:66-100); the ground's damage types are averaged, and without them the
/// piece's seven protections (slash to lightning) are (<c>TODO(m6-ground-damage-types)</c>, logged once a run). A body-armour cantrip (Minor to Legendary Armor) adds to every part, unscaled (<c>GetBodyArmorMod</c>, :42-48). The burden
/// is weighed against the free capacity: the share of it the piece takes off the total, all of it when the piece alone would fill it.</para>
/// <para><b>Jewellery and trinkets.</b> The useful cantrips it casts.</para>
/// </remarks>
public static class GearScore
{
    /// <summary>ACE's PropertyInt Bonded.</summary>
    public const uint BondedInt = 33;

    /// <summary>ACE's PropertyInt Attuned.</summary>
    public const uint AttunedInt = 114;

    /// <summary>ACE's PropertyFloat ManaRate (the item mana a worn piece burns a second, negative; ManaConsumersTick, Player_Tick.cs:624-660 @95ef426d).</summary>
    public const uint ManaRateFloat = 5;

    /// <summary>The body parts (ACE <c>BodyPart</c>) and the coverage bits that cover each (<c>BodyPart.GetCoverageMask</c>, Entity/BodyPart.cs:115-140 @95ef426d).</summary>
    public static IReadOnlyList<(string Part, uint Coverage)> BodyParts { get; } =
    [
        ("head", 0x4000),
        ("chest", 0x400 | 0x8),
        ("abdomen", 0x800 | 0x10),
        ("upper arm", 0x1000 | 0x20),
        ("lower arm", 0x2000 | 0x40),
        ("hand", 0x8000),
        ("upper leg", 0x100 | 0x2),
        ("lower leg", 0x200 | 0x4),
        ("foot", 0x10000),
    ];

    /// <summary>ACE's DamageType bits of the seven protections the stub averages (slash, pierce, bludgeon, cold, fire, acid, electric; nether is not an outdoor creature's on the ladder's grounds).</summary>
    public static IReadOnlyList<uint> StubDamageTypes { get; } = [0x1, 0x2, 0x4, 0x8, 0x10, 0x20, 0x40];

    /// <summary>
    /// The armour cantrips' rows the buff table (<see cref="BuffStatMods"/>) does not carry: the pinned world SQL v0.9.296's <c>spell</c> table, read offline (<c>stat_Mod_Type</c>, <c>stat_Mod_Key</c>, <c>stat_Mod_Val</c>):
    /// 2604 Minor, 2592 Major, 4667 Epic, 6095 Legendary Impenetrability (36868 = 0x9004, key 28, 20/40/60/80); 2578 Minor, 2571 Major, 4911 Epic, 6102 Legendary Armor (41088 = 0xA080, key 0, 20/40/60/80).
    /// </summary>
    private static readonly Dictionary<uint, BuffStatMod> ArmourCantrips = new()
    {
        [2604] = new(2604, 0x9004, 28, 20.0),
        [2592] = new(2592, 0x9004, 28, 40.0),
        [4667] = new(4667, 0x9004, 28, 60.0),
        [6095] = new(6095, 0x9004, 28, 80.0),
        [2578] = new(2578, 0xA080, 0, 20.0),
        [2571] = new(2571, 0xA080, 0, 40.0),
        [4911] = new(4911, 0xA080, 0, 60.0),
        [6102] = new(6102, 0xA080, 0, 80.0),
    };

    private const uint ItemArmourType = 0x9004, BodyArmourType = 0xA080, ArmorLevelKey = 28;
    private const uint CasterType = 0x8000, ArmourOrClothing = 0x2 | 0x4;
    private const uint JewelleryLocations = 0x00008000 | 0x00010000 | 0x00020000 | 0x00040000 | 0x00080000 | 0x04000000;

    /// <summary>The slot an item of this type, coverage and locations is scored in.</summary>
    /// <param name="itemType">Its ItemType.</param>
    /// <param name="coverage">Its ClothingPriority.</param>
    /// <param name="validLocations">Its ValidLocations.</param>
    public static GearSlot SlotOf(uint itemType, uint coverage, uint validLocations) =>
        (itemType & CasterType) != 0 ? GearSlot.Caster
        : (itemType & ArmourOrClothing) != 0 && coverage != 0 ? GearSlot.Armour
        : (validLocations & JewelleryLocations) != 0 && coverage == 0 ? GearSlot.Jewellery
        : GearSlot.Other;

    /// <summary>True when PropertyInt 114 Attuned is above 0.</summary>
    /// <param name="appraisal">The appraisal, or null.</param>
    public static bool Attuned(Appraisal? appraisal) => appraisal?.Int(AttunedInt) is > 0;

    /// <summary>True when PropertyInt 33 Bonded is above 0.</summary>
    /// <param name="appraisal">The appraisal, or null.</param>
    public static bool Bonded(Appraisal? appraisal) => appraisal?.Int(BondedInt) is > 0;

    /// <summary>The ManaRate (PropertyFloat 5) read raw, or null.</summary>
    /// <param name="appraisal">The appraisal, or null.</param>
    public static double? ManaRate(Appraisal? appraisal) => appraisal?.Float(ManaRateFloat);

    /// <summary>The hours of item mana left at its ManaRate, or null for a piece that burns none (no rate, or no mana pool).</summary>
    /// <param name="appraisal">The appraisal, or null.</param>
    public static double? ManaHours(Appraisal? appraisal) =>
        appraisal is { ItemMaxMana: > 0 } a && ManaRate(a) is { } rate && rate != 0 ? (a.ItemMana ?? 0) / Math.Abs(rate) / 3600.0 : null;

    /// <summary>True when the piece has a mana pool and it is empty: its spells are off (ACE's ManaConsumersTick, Player_Tick.cs:681-699).</summary>
    /// <param name="appraisal">The appraisal.</param>
    public static bool OutOfMana(Appraisal appraisal) => appraisal is { ItemMaxMana: > 0, ItemMana: null or <= 0 };

    /// <summary>The useful cantrips <paramref name="appraisal"/> casts, by name, in its order.</summary>
    /// <param name="appraisal">The appraisal, or null.</param>
    /// <param name="useful">The useful names.</param>
    /// <param name="spellName">The spell names.</param>
    public static List<string> UsefulCantrips(Appraisal? appraisal, IReadOnlySet<string> useful, Func<uint, string?> spellName)
    {
        ArgumentNullException.ThrowIfNull(useful);
        ArgumentNullException.ThrowIfNull(spellName);
        var list = new List<string>();
        if (appraisal is null || appraisal.IsNotFound)
        {
            return list;
        }

        foreach (var id in appraisal.Spells)
        {
            if (spellName(id) is { } name && useful.Contains(name) && !list.Contains(name))
            {
                list.Add(name);
            }
        }

        return list;
    }

    /// <summary>The score of one piece in its slot (<see cref="SlotOf"/>); an unappraised piece scores 0, named.</summary>
    /// <param name="candidate">The piece with its coverage, burden and appraisal.</param>
    /// <param name="context">The character and the ground.</param>
    public static GearScoreResult Of(GearCandidate candidate, GearContext context)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(context);
        var slot = SlotOf(candidate.ItemType, candidate.Coverage, candidate.ValidLocations);
        if (candidate.Appraisal is not { IsNotFound: false } a)
        {
            return new GearScoreResult(slot, candidate.Guid, candidate.Name, 0, [new GearScorePart("unread", 0, "no appraisal of it is held")], null, false, false, null);
        }

        return slot switch
        {
            GearSlot.Caster => Caster(candidate.Guid, candidate.Name, a, context),
            GearSlot.Armour => Armour(candidate.Guid, candidate.Name, a, candidate.Coverage, a.Burden ?? candidate.Burden, context),
            GearSlot.Jewellery => Jewellery(candidate.Guid, candidate.Name, a, context),
            _ => new GearScoreResult(slot, candidate.Guid, candidate.Name, 0, [new GearScorePart("none", 0, "not a caster, an armour or a jewellery piece")], null, Attuned(a), Bonded(a), ManaHours(a)),
        };
    }

    /// <summary>The score of a worn piece (its location and coverage as worn).</summary>
    /// <param name="piece">The worn piece.</param>
    /// <param name="context">The character and the ground.</param>
    public static GearScoreResult Of(WornPiece piece, GearContext context)
    {
        ArgumentNullException.ThrowIfNull(piece);
        return Of(new GearCandidate(piece.Guid, piece.Name, piece.ItemType, piece.Location, piece.Coverage, piece.Appraisal?.Burden ?? 0, piece.Appraisal), context);
    }

    /// <summary>A caster's score (see the class remarks).</summary>
    /// <param name="guid">The caster.</param>
    /// <param name="name">Its name.</param>
    /// <param name="a">Its appraisal.</param>
    /// <param name="context">The character and the ground.</param>
    public static GearScoreResult Caster(uint guid, string name, Appraisal a, GearContext context)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(context);
        var terms = context.Caster;
        var weights = terms.Weights;
        var auras = terms.Auras;
        var hours = ManaHours(a);
        var off = OutOfMana(a);
        var wield = SelfEquipJudge.WieldUnmet(a, context.Skills);
        var spellsOff = wield is null ? SelfEquipJudge.ActivationUnmet(a, context.Skills) : null;
        var on = !off && spellsOff is null; // its own spells light when wielded

        // M6 14 casterfair (the owner's ruling, 2026-11-17: the hermetic link is a spell she casts already): every caster on its own numbers. ACE folds the wielder's auras and the item's own enchantments into the appraisal of a wielded
        // caster (AppraiseInfo.cs:415-453 @95ef426d), so that appraisal is unfolded back to the caster's intrinsic numbers (CasterIntrinsic); her auras at her book's levels then apply to every caster alike, and only an aura or a cantrip a
        // caster carries in its own spell book (not an enchantment on it) counts for that caster alone: the stronger of hers and its own aura (one category on her: the top layer, EnchantmentManager.cs:123-129), and its item cantrip on
        // top (an enchantment on the caster: ACE combines the wielder's and the weapon's, ResistMask.cs:127-145, WorldObject_Weapon.cs:123-126, 247-259, 384-395)
        var intrinsic = CasterIntrinsic.Of(guid, a, terms.Fold);
        double OwnAura(uint key, double book) => on ? Math.Max(book, CasterSpells.Top(a.Spells, key, aura: true, key == CasterFold.ManaKey ? 1.0 : 0.0)) : book;
        double OwnCantrip(uint key) => on ? CasterSpells.Top(a.Spells, key, aura: false, key == CasterFold.ManaKey ? 1.0 : 0.0) : (key == CasterFold.ManaKey ? 1.0 : 0.0);
        var elementalAdd = OwnAura(CasterFold.ElementalKey, auras.ElementalAdd) + OwnCantrip(CasterFold.ElementalKey);
        var manaFactor = OwnAura(CasterFold.ManaKey, auras.ManaConversionFactor) * OwnCantrip(CasterFold.ManaKey);
        var defenceAdd = OwnAura(CasterFold.DefenceKey, auras.DefenceAdd) + OwnCantrip(CasterFold.DefenceKey);

        var type = a.Int(SelfEquipJudge.DamageTypeInt);
        var mod = intrinsic.Elemental;
        var factor = WarFactor.Of(a, context.Element, mod, elementalAdd); // M6 16 Q4: the one factor, which the fight's line for a target reads of the caster in her hands (CasterWar)
        var onElement = factor.OnElement;
        var war = onElement ? ((mod ?? 1.0) - 1.0 + elementalAdd) * 100.0 : 0.0;
        var spirit = elementalAdd > 0 ? string.Create(CultureInfo.InvariantCulture, $" + Spirit Drinker {elementalAdd:0.##}") : string.Empty;
        var warNote = context.Element == 0 ? "the rung names no element: no bonus counts"
            : type is null ? "no element (Int 45 absent): ACE adds no bonus and no aura (WorldObject_Weapon.cs:384)"
            : onElement ? string.Create(CultureInfo.InvariantCulture, $"on the rung's element 0x{context.Element:X}, its own x{mod ?? 1.0:0.###}{spirit}")
            : string.Create(CultureInfo.InvariantCulture, $"its element 0x{(uint)type.Value:X} is not the rung's 0x{context.Element:X}: its x{mod ?? 1.0:0.###} counts 0");

        // M6 16 E5e3 (tools/fight-sim.ps1 matchup 1628 with and without --wield "Minor Smoldering Atlan Wand", 2026-11-18: her fire bolt's mean plain hit 169.2 with the piercing sceptre, 381.5 with the wand, x2.25, where the
        // elemental bonus alone is x1.18): the two other things ACE multiplies her bolt by for the caster in her hands. (1) A quest weapon's fixed resistance cleaving: on its ResistanceModifierType the bolt strikes as if
        // the target had a vulnerability of 1 + ResistanceModifier (an absent modifier is ACE's 1: x2), which the target's own vulnerability replaces only when it is the larger (GetWeaponResistanceModifier,
        // WorldObject_Weapon.cs:461-486; Creature.GetResistanceMod, Creature_Properties.cs:132-135 @95ef426d). (2) The mastery bonus: x1.05 on every spell with a caster that is not "tough to master" (the server's
        // universal_masteries, ACE's default; SpellProjectile.cs:735, Player_Skills.cs:646-659, WorldObject_Weapon.cs:1012-1023: read from the long description, as ACE reads it), so a caster that is tough to master
        // casts at 1/1.05 of every other. The war term is the whole factor against a plain caster she has mastered.
        // The weights are as they were, checked against the same runs: the damage weight (0.5) stands for the share of her fight that is casting (live ladder67, the fight of 2026-11-17 23:59 to 2026-11-18 00:50 UTC:
        // 404 casts, 3.38 s a cast by the simulator, of fight_s=3009: 0.45); her Mana Conversion from the sceptre saves 0.13 s of a 10.7 s kill-and-recover cycle (1.2%, where the mana weight gives the two casters'
        // difference 2.6); the sceptre's melee defence is worth nothing there (a Tusker Slave lands 100% with either). The term is linear in the bonus, which overstates a factor this large: by the simulator's casts a
        // kill (2.93 to 1.58) that fight's time holds 26% more kills, where the linear term reads 62. The pick reads the order alone: TODO(m6-caster-damage-linear) for a reader of the size.
        var (cleaves, cleave, tough) = (factor.Cleaves, factor.Cleave, factor.Tough);
        if (cleaves || tough)
        {
            war = (((1.0 + (war / 100.0)) * cleave * (tough ? 1.0 / MasteryBonus : 1.0)) - 1.0) * 100.0;
            warNote += (cleaves ? string.Create(CultureInfo.InvariantCulture, $"; resistance cleaving x{cleave:0.###} on it (a target under a larger vulnerability takes that instead: not read, TODO(m6-caster-cleave-under-vuln))") : string.Empty)
                + (tough ? string.Create(CultureInfo.InvariantCulture, $"; tough to master: no x{MasteryBonus:0.##} mastery bonus, which every other caster has") : string.Empty);
        }
        var own = intrinsic.ManaConversion;
        var effective = own * manaFactor;
        var (casts, castsNote) = CastsGain(effective, context);
        var melee = (intrinsic.Defence - 1.0 + defenceAdd) * 100.0;
        var magic = ((a.Float(WeaponMagicDefenseFloat) ?? 1.0) - 1.0) * 100.0;
        var cantrips = on ? UsefulCantrips(a, context.Useful, context.SpellName).Count : 0;

        // M6 14 capacity: its Strength cantrip, while its spells are on, over the strongest one she wears (ACE's top layer: EnchantmentManager.cs:123-129 @95ef426d), as capacity on her live load
        var strength = on ? Math.Max(0, StrengthCantrip(a) - context.WornStrength) : 0;
        var capacity = CapacityPart(strength, context.Load);

        // M6 14 casterfair: its own skill cantrips (War Magic Aptitude and the like) over the strongest of each she wears, by the wear pick's terms (WearScore.SkillPoints); its built-in buff spells (Focus Self V and the like) are
        // her own lines' to override (the owner, 2026-11-15: item buff spells are overridden by the active ones): not counted
        var skillCantrips = SkillCantrips(a, on, context);
        var score = (weights.Damage * war) + (weights.Mana * casts) + (weights.MeleeDefence * melee) + (weights.MagicDefence * magic) + capacity.Value + skillCantrips.Value;
        var ownCantripHl = OwnCantrip(CasterFold.ManaKey);
        List<GearScorePart> parts =
        [
            new("intrinsic", 0, intrinsic.Note),
            new("war", war, warNote),
            new("mana conversion", effective * 100.0, string.Create(CultureInfo.InvariantCulture, $"its own {own:0.###} x Hermetic Link {manaFactor:0.###}{(ownCantripHl > 1.0 ? string.Create(CultureInfo.InvariantCulture, $" (its own cantrip x{ownCantripHl:0.##} in)") : string.Empty)}")),
            new("bolts a mana bar", casts, castsNote),
            new("melee defence", melee, string.Create(CultureInfo.InvariantCulture, $"its own x{intrinsic.Defence:0.###} + Defender {defenceAdd:0.###}")),
            new("magic defence", magic),
            new("cantrips", cantrips, off ? "out of mana: its spells are off" : spellsOff is not null ? "its spells stay off: " + spellsOff : null),
            skillCantrips,
            new("mana h", hours ?? 0, hours is null ? "burns none" : null),
            capacity,
            new("score", score, string.Create(CultureInfo.InvariantCulture, $"% of the hour: {weights.Damage:0.###} x war + {weights.Mana:0.###} x bolts + {weights.MeleeDefence:0.###} x melee + {weights.MagicDefence:0.###} x magic + capacity + skill cantrips")),
        ];
        return new GearScoreResult(GearSlot.Caster, guid, name, wield is null ? score : 0, parts, wield, Attuned(a), Bonded(a), hours)
        {
            OnElement = onElement,
            WarPercent = war,
            ManaConversionPercent = effective * 100.0,
            CastsPercent = casts,
            MeleeDefencePercent = melee,
            MagicDefencePercent = magic,
            CasterScore = score,
            SpellsOff = spellsOff,
            Cantrips = cantrips,
            CapacityPercent = capacity.Value,
            SkillCantripPercent = skillCantrips.Value,
        };
    }

    /// <summary>
    /// M6 14 casterfair: what a caster's own skill cantrips (<see cref="CantripRows"/>, type 0x9010) add over the strongest of each family she wears (<see cref="GearContext.WornCantrips"/>), by the wear pick's terms of that skill
    /// (<see cref="WearScore.SkillPoints"/>); 0 while its spells are off, and named when no wear pick's terms are held (the cantrips then break a tie alone).
    /// </summary>
    private static GearScorePart SkillCantrips(Appraisal a, bool on, GearContext context)
    {
        if (!on)
        {
            return new("skill cantrips", 0);
        }

        var tops = new Dictionary<uint, CantripRow>();
        foreach (var id in a.Spells)
        {
            if (CantripRows.Of(id) is { Mod.Type: 0x9010 } row && (!tops.TryGetValue(row.Mod.Key, out var held) || held.Mod.Value < row.Mod.Value))
            {
                tops[row.Mod.Key] = row;
            }
        }

        if (tops.Count == 0)
        {
            return new("skill cantrips", 0);
        }

        if (context.Wear is not { } wear)
        {
            return new("skill cantrips", 0, string.Join(", ", tops.Values.Select(r => r.Name)) + ": no wear pick's terms held: they break a tie alone");
        }

        var total = 0.0;
        var notes = new List<string>();
        foreach (var row in tops.Values)
        {
            var worn = context.WornCantrips.TryGetValue((row.Mod.Type, row.Mod.Key), out var w) ? w : 0.0;
            var points = Math.Max(0, row.Mod.Value - worn);
            var part = WearScore.SkillPoints(row.Mod.Key, points, context, wear);
            total += part.Value;
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"{row.Name} +{points:0} over the worn {worn:0} ({part})"));
        }

        return new("skill cantrips", total, string.Join("; ", notes));
    }

    /// <summary>
    /// M6 14 capacity (the owner's ruling, 2026-11-17: in her major strength shoes she would have a lot more capacity): what <paramref name="strengthPoints"/> more Strength (a Strength cantrip's top layer: Minor 5, Major 15, Epic 25,
    /// Legendary 35, the world's spell table <c>stat_Mod_Val</c> of 2583, 2576, 3965, 6107) is worth to her, in percent of her hour. ACE's capacity is 150 x her CURRENT Strength (<c>GetEncumbranceCapacity</c>, Player_Inventory.cs:46-51
    /// @95ef426d: an attribute enchantment raises it), so the gain is 150 a point. It is valued by the wear score's own burden rule read the other way (<see cref="Grounds.LadderWearScore.Burden"/>: a percent of her capacity is the loot a
    /// full pack leaves on a corpse), times how much of the new room her live load would fill: all of it when her load is within the gain of the loot's load line (she is leaving loot now), else the share of the way there her load
    /// stands. Without her load read, 0, named.
    /// </summary>
    /// <param name="strengthPoints">The Strength the change adds (negative: takes away).</param>
    /// <param name="load">Her load, or null.</param>
    public static GearScorePart CapacityPart(double strengthPoints, CapacityLoad? load)
    {
        if (Math.Abs(strengthPoints) < 1e-9)
        {
            return new("capacity", 0);
        }

        var gain = strengthPoints * GearContext.CapacityPerStrength;
        if (load is not { Capacity: > 0 } l)
        {
            return new("capacity", 0, string.Create(CultureInfo.InvariantCulture, $"{strengthPoints:+0;-0} Strength = {gain:+0;-0} capacity (150 a point, Player_Inventory.cs:46-51), but her load is unread: not weighed"));
        }

        var line = l.Line;
        var reach = line - Math.Abs(gain);
        var fill = l.Carried >= reach ? 1.0 : reach > 0 ? Math.Clamp((double)l.Carried / reach, 0, 1) : 1.0;
        var percent = gain / l.Capacity * 100.0;
        var value = l.BurdenWeight * percent * fill;
        var how = fill >= 1.0 ? "her load is within it of the load line: all of it is filled" : string.Create(CultureInfo.InvariantCulture, $"her load is {fill * 100:0.#}% of the way to the line less it");
        return new("capacity", value, string.Create(CultureInfo.InvariantCulture, $"{strengthPoints:+0;-0} Strength = {gain:+0;-0} capacity (150 a point, Player_Inventory.cs:46-51) on {l}: {percent:+0.##;-0.##}% of her capacity x {l.BurdenWeight:0.###}, {how}"));
    }

    /// <summary>The strongest Strength cantrip <paramref name="a"/> casts (<see cref="CantripRows"/>: its spell table value), 0 when none.</summary>
    /// <param name="a">The piece's appraisal.</param>
    public static double StrengthCantrip(Appraisal? a)
    {
        var best = 0.0;
        if (a is null || a.IsNotFound)
        {
            return best;
        }

        foreach (var id in a.Spells)
        {
            if (CantripRows.Of(id) is { Mod: { Type: 0x9001, Key: GearContext.Strength } mod } && mod.Value > best)
            {
                best = mod.Value;
            }
        }

        return best;
    }

    /// <summary>M6 16 E5e3: ACE's mastery bonus on a spell cast with a caster she has mastered (<c>heritageMod</c>, SpellProjectile.cs:735 @95ef426d).</summary>
    public const double MasteryBonus = 1.05;

    /// <summary>M6 16 E5e3: the sentence of a weapon's long description by which ACE reads that it gives no mastery bonus (<c>IsMasterable</c>, WorldObject_Weapon.cs:1012-1023 @95ef426d).</summary>
    public const string ToughToMaster = "This weapon seems tough to master.";

    /// <summary>ACE's PropertyFloat WeaponDefense (the melee defence modifier).</summary>
    public const uint WeaponDefenseFloat = 29;

    /// <summary>ACE's PropertyFloat WeaponMagicDefense.</summary>
    public const uint WeaponMagicDefenseFloat = 150;

    /// <summary>
    /// M6 14 (<c>m6-caster</c>): the percent more bolts a mana bar a caster modifier of <paramref name="modifier"/> (its ManaConversionMod times her Hermetic Link) gives her: the plan's bolt's expected cost
    /// (<see cref="Casting.ManaConversionModel.ExpectedCost"/>) at her current Mana Conversion against at ACE's <c>round(skill x (1 + modifier))</c> (Creature_Magic.cs:48, C#'s Math.Round as ACE's). 0 when untrained (ACE pays the base,
    /// Creature_Magic.cs:41-45). Without a bolt (<see cref="CasterTerms.Bolt"/>) or her skill read, the skill's own percent stands in, named (<c>TODO(m6-caster-bolt-unread)</c>, noted once a run).
    /// </summary>
    private static (double Percent, string Note) CastsGain(double modifier, GearContext context)
    {
        if (modifier <= 0)
        {
            return (0, "no Mana Conversion bonus");
        }

        var skill = context.Skills.Current(MageTemplate.ManaConversion);
        if (context.Skills.AdvancementClass(MageTemplate.ManaConversion) is > 0 and < (int)Contracts.SkillRules.TrainedClass)
        {
            return (0, "her Mana Conversion is untrained: ACE pays the base (Creature_Magic.cs:41-45)");
        }

        if (context.Caster.Bolt is not { } bolt || skill is not > 0)
        {
            const string Todo = "m6-caster-bolt-unread";
            context.Notes.Once(Todo, "gear score: TODO(" + Todo + "): the caster score has no bolt or no Mana Conversion read: a caster's Mana Conversion bonus counts its own percent of the skill, not the mana model's bolts a mana bar");
            return (modifier * 100.0, string.Create(CultureInfo.InvariantCulture, $"TODO({Todo}): {(skill is > 0 ? "no bolt" : "Mana Conversion unread")}: the skill's +{modifier * 100.0:0.#}% stands in"));
        }

        var with = (uint)Math.Round(skill.Value * (1.0 + modifier));
        var before = Casting.ManaConversionModel.ExpectedCost(bolt.BaseMana, bolt.Power, (uint)skill.Value);
        var after = Casting.ManaConversionModel.ExpectedCost(bolt.BaseMana, bolt.Power, with);
        var percent = after > 0 ? ((before / after) - 1.0) * 100.0 : 0;
        return (percent, string.Create(CultureInfo.InvariantCulture, $"{bolt.Name}: {before:0.##} mana a cast at Mana Conversion {skill.Value}, {after:0.##} at {with}"));
    }

    /// <summary>
    /// M6 14 (<c>m6-caster</c>): true when caster <paramref name="candidate"/> beats <paramref name="held"/> (null: none held), requirements aside: its <see cref="GearScoreResult.CasterScore"/> is above the held one's by more than
    /// <paramref name="margin"/> (the ladder's <see cref="Grounds.LadderCasterScore.SwapMargin"/> against the wielded caster; 0 between candidates); on an exact tie, more useful cantrips win. It replaces M5 7 S2c's order (the element's
    /// damage alone, then mana conversion only on the element): ACE reads a caster's Mana Conversion and defence whatever its element (WorldObject_Weapon.cs:86-129, 235-263), so the rehearsal of 2026-10-29's Electric Sceptre with
    /// mana conversion is a real gain over a plain Wand with none.
    /// </summary>
    /// <param name="candidate">The candidate's score.</param>
    /// <param name="held">The held caster's score, or null.</param>
    /// <param name="margin">The score to beat it by.</param>
    public static bool CasterBeats(GearScoreResult candidate, GearScoreResult? held, double margin = 0)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        const double eps = 1e-6;
        var (s, hs) = (candidate.CasterScore, held?.CasterScore ?? 0.0);
        if (s > hs + margin + eps)
        {
            return true;
        }

        return Math.Abs(s - hs) <= eps && candidate.Cantrips > (held?.Cantrips ?? 0);
    }

    /// <summary>M6 14 (<c>m6-caster</c>): a caster score's numbers as a swap's log prints them: the score and its terms.</summary>
    /// <param name="score">The caster's score.</param>
    public static string CasterNumbers(GearScoreResult score)
    {
        ArgumentNullException.ThrowIfNull(score);
        return string.Create(CultureInfo.InvariantCulture, $"'{score.Name}' 0x{score.Guid:X8} score {score.CasterScore:0.##} (war {score.WarPercent:+0.#;-0.#}%, mana conversion +{score.ManaConversionPercent:0.#}% = +{score.CastsPercent:0.#}% bolts a mana bar, melee defence +{score.MeleeDefencePercent:0.#}%, magic defence +{score.MagicDefencePercent:0.#}%, cantrips {score.Cantrips}{(score.CapacityPercent != 0 ? string.Create(CultureInfo.InvariantCulture, $", capacity {score.CapacityPercent:+0.###;-0.###}") : string.Empty)}{(score.SpellsOff is null ? string.Empty : ", spells off")})");
    }

    /// <summary>An armour or clothing piece's score (see the class remarks).</summary>
    /// <param name="guid">The piece.</param>
    /// <param name="name">Its name.</param>
    /// <param name="a">Its appraisal.</param>
    /// <param name="coverage">Its ClothingPriority (from the weenie header: the appraisal does not carry it).</param>
    /// <param name="burden">Its burden.</param>
    /// <param name="context">The character and the ground.</param>
    /// <remarks>
    /// M6 16 E5e5 (a piece is scored on what it does give): an unmet wield requirement scores 0, for she cannot put the piece on (<c>CheckWieldRequirement</c>, Player_Inventory.cs:2126-2229 @95ef426d). An unmet activation
    /// requirement leaves only its spells off (<c>TryActivateSpells</c>, Player_Inventory.cs:313-335): the piece is scored on its armour level alone, with no Impenetrability or body armour of its own and no cantrip
    /// counted, never 0, and the requirement is named in <see cref="GearScoreResult.SpellsOff"/>. A piece lit on her (<see cref="GearContext.LitBy"/>) keeps its spells whatever the requirement reads now. Before, any unmet
    /// requirement scored 0, so the rule of before the wear pick (<see cref="SelfEquipJudge"/>) took a worn piece behind a skill limit off for any armour that scored above 0.
    /// </remarks>
    public static GearScoreResult Armour(uint guid, string name, Appraisal a, uint coverage, int burden, GearContext context)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(context);
        var covered = BodyParts.Where(p => (p.Coverage & coverage) != 0).Select(p => p.Part).ToList();
        var level = a.ArmorLevel ?? 0;
        var unmet = SelfEquipJudge.WieldUnmet(a, context.Skills);
        var spellsOff = unmet is null && context.LitBy?.Invoke(guid) != true ? SelfEquipJudge.ActivationUnmet(a, context.Skills) : null;
        var off = spellsOff is not null || OutOfMana(a);
        double impen = 0, body = 0;
        foreach (var id in a.Spells)
        {
            if (StatOf(id) is not { } row)
            {
                continue;
            }

            if (row.Type == ItemArmourType && row.Key == ArmorLevelKey)
            {
                impen += row.Value;
            }
            else if (row.Type == BodyArmourType)
            {
                body += row.Value;
            }
        }

        if (off)
        {
            (impen, body) = (0, 0);
        }

        var (mod, modNote) = Protection(a, context);
        var perPart = (level + impen) * mod;
        var armour = perPart * covered.Count;
        var cantripArmour = body * BodyParts.Count;
        var gross = armour + cantripArmour;
        var (penalty, burdenNote) = BurdenPenalty(gross, burden, context);
        var hours = ManaHours(a);
        List<GearScorePart> parts =
        [
            new("parts", covered.Count, string.Join("/", covered)),
            new("armour level", level),
            new("impenetrability", impen, spellsOff is not null ? "its spells are off for her: " + spellsOff : off ? "out of mana: its spells are off" : null),
            new("protection", mod, modNote),
            new("armour", armour, string.Create(CultureInfo.InvariantCulture, $"({level} + {impen:0.##}) x {mod:0.###} x {covered.Count}")),
            new("cantrip armour", cantripArmour, body == 0 ? null : string.Create(CultureInfo.InvariantCulture, $"{body:0.##} on every one of {BodyParts.Count} parts")),
            new("burden", penalty == 0 ? 0 : -penalty, burdenNote),
            new("mana h", hours ?? 0, hours is null ? "burns none" : null),
        ];
        var total = unmet is null ? Math.Max(0, gross - penalty) : 0;
        return new GearScoreResult(GearSlot.Armour, guid, name, total, parts, unmet, Attuned(a), Bonded(a), hours) { SpellsOff = spellsOff, Cantrips = off ? 0 : UsefulCantrips(a, context.Useful, context.SpellName).Count };
    }

    /// <summary>A jewellery piece's or trinket's score: its useful cantrips (none when out of mana).</summary>
    /// <param name="guid">The piece.</param>
    /// <param name="name">Its name.</param>
    /// <param name="a">Its appraisal.</param>
    /// <param name="context">The character and the ground.</param>
    public static GearScoreResult Jewellery(uint guid, string name, Appraisal a, GearContext context)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(context);
        var off = OutOfMana(a);
        var names = off ? new List<string>() : UsefulCantrips(a, context.Useful, context.SpellName);
        var unmet = SelfEquipJudge.Unmet(a, context.Skills);
        var hours = ManaHours(a);
        List<GearScorePart> parts =
        [
            new("cantrips", names.Count, off ? "out of mana: its spells are off" : names.Count == 0 ? null : string.Join(", ", names)),
            new("mana h", hours ?? 0, hours is null ? "burns none" : null),
        ];
        return new GearScoreResult(GearSlot.Jewellery, guid, name, unmet is null ? names.Count : 0, parts, unmet, Attuned(a), Bonded(a), hours) { Cantrips = names.Count };
    }

    /// <summary>The armour row of a spell the piece casts: the buff table's, else the armour cantrips' (the SQL rows of <see cref="ArmourCantrips"/>), else null.</summary>
    /// <param name="spell">The spell id.</param>
    public static BuffStatMod? StatOf(uint spell) => BuffStatMods.BySpell.TryGetValue(spell, out var row) ? row : ArmourCantrips.TryGetValue(spell, out var cantrip) ? cantrip : null;

    /// <summary>The piece's protection against the ground: the mean over its damage types, else over the seven (the TODO, noted once).</summary>
    private static (double Mod, string Note) Protection(Appraisal a, GearContext context)
    {
        var types = context.DamageTypes is { Count: > 0 } given ? given : StubDamageTypes;
        var mods = types.Select(t => Math.Clamp(ModVs(a, t, out _), -2.0, 2.0)).ToList();
        var mean = mods.Average();
        _ = ModVs(a, 0x4, out var read);
        if (context.DamageTypes is { Count: > 0 })
        {
            return (mean, string.Create(CultureInfo.InvariantCulture, $"the mean vs the ground's {string.Join("/", types.Select(t => "0x" + t.ToString("X", CultureInfo.InvariantCulture)))} from {read}"));
        }

        const string Todo = "m6-ground-damage-types";
        context.Notes.Once(Todo, "gear score: TODO(" + Todo + "): the ground's targets give no damage_types (the profile's optional per-target field, M6 item 3); armour is scored on the mean of its seven protections, slash to lightning");
        return (mean, "the mean of its seven protections from " + read + ": TODO(" + Todo + ")");
    }

    /// <summary>The protection against one damage type: the appraisal's armour profile, else the PropertyFloat (13-19, 165 ArmorModVs...), else 1 (named).</summary>
    internal static double ModVs(Appraisal a, uint type, out string source)
    {
        if (a.Armor is { } p)
        {
            source = "the armour profile";
            return type switch
            {
                0x1 => p.Slashing,
                0x2 => p.Piercing,
                0x4 => p.Bludgeoning,
                0x8 => p.Cold,
                0x10 => p.Fire,
                0x20 => p.Acid,
                0x40 => p.Lightning,
                0x400 => p.Nether,
                _ => 1.0,
            };
        }

        var id = type switch { 0x1 => 13u, 0x2 => 14u, 0x4 => 15u, 0x8 => 16u, 0x10 => 17u, 0x20 => 18u, 0x40 => 19u, 0x400 => 165u, _ => 0u };
        if (id != 0 && a.Float(id) is { } f)
        {
            source = "the ArmorModVs floats";
            return f;
        }

        source = "nothing: protection 1 assumed, TODO(m6-armour-profile-unread)";
        return 1.0;
    }

    /// <summary>The burden's cost: the share of the free capacity the piece takes, of <paramref name="gross"/>; all of it when the piece fills the free capacity; none while the capacity is unread (named).</summary>
    private static (double Penalty, string Note) BurdenPenalty(double gross, int burden, GearContext context)
    {
        if (context.Capacity is not { } capacity)
        {
            return (0, "capacity unread (Strength unknown): not weighed");
        }

        var free = capacity - (context.CarriedBurden ?? 0);
        if (burden <= 0)
        {
            return (0, string.Create(CultureInfo.InvariantCulture, $"0 of {free} free"));
        }

        if (free <= burden)
        {
            return (gross, string.Create(CultureInfo.InvariantCulture, $"{burden} over the free capacity {free} of {capacity}: worth nothing"));
        }

        var share = (double)burden / free;
        return (gross * share, string.Create(CultureInfo.InvariantCulture, $"{burden} of {free} free of {capacity}: {share * 100:0.#}% off"));
    }
}

/// <summary>
/// M6 14 casterfair: the caster spells by id, with the stat key and value of the world's spell table (read 2026-11-17, read-only): the auras she wears (Aura of ... Self: they land on her, the wielder; BuffStatMods rows: Hermetic Link
/// 1475-1480 x1.1 to x1.6 and the Incantation 4418 x1.8, key 171; Defender 1599, 1601-1605 +0.03 to +0.15 and the Incantation 4400 +0.20, key 169; Spirit Drinker 3253-3258 +0.01 to +0.06, key 170) and the item cantrips (they land
/// on the caster: Minor, Major, Epic, Legendary Hermetic Link 3199, 3200, 6086, 6087 x1.1, x1.2, x1.25, x1.3; Defender 2600, 2588, 4663, 6091 +0.03, +0.05, +0.07, +0.09; Spirit Thirst 3251, 3250, 4670, 6098 +0.01, +0.03, +0.05, +0.07).
/// </summary>
public static class CasterSpells
{
    private static readonly Dictionary<uint, (uint Key, double Value, bool Aura)> Rows = Build();

    /// <summary>The row of a spell id, or null.</summary>
    /// <param name="spell">The spell id.</param>
    public static (uint Key, double Value, bool Aura)? Of(uint spell) => Rows.TryGetValue(spell, out var row) ? row : null;

    /// <summary>The strongest value of <paramref name="key"/> among <paramref name="spells"/> of the kind asked (an aura on her, or an item cantrip), else <paramref name="none"/>.</summary>
    /// <param name="spells">The spell ids.</param>
    /// <param name="key">The stat key.</param>
    /// <param name="aura">True for the auras, false for the item cantrips.</param>
    /// <param name="none">The value with none.</param>
    public static double Top(IEnumerable<uint> spells, uint key, bool aura, double none)
    {
        ArgumentNullException.ThrowIfNull(spells);
        var best = none;
        var found = false;
        foreach (var id in spells)
        {
            if (Of(id) is { } row && row.Key == key && row.Aura == aura && (!found || row.Value > best))
            {
                (best, found) = (row.Value, true);
            }
        }

        return best;
    }

    private static Dictionary<uint, (uint, double, bool)> Build()
    {
        var rows = new Dictionary<uint, (uint, double, bool)>();
        foreach (var id in new uint[] { 1475, 1476, 1477, 1478, 1479, 1480, 4418, 1599, 1601, 1602, 1603, 1604, 1605, 4400, 3253, 3254, 3255, 3256, 3257, 3258 })
        {
            var row = BuffStatMods.BySpell[id];
            rows[id] = (row.Key, row.Value, true);
        }

        void Cantrips(uint key, uint[] ids, double[] values)
        {
            for (var i = 0; i < ids.Length; i++)
            {
                rows[ids[i]] = (key, values[i], false);
            }
        }

        Cantrips(CasterFold.ManaKey, [3199, 3200, 6086, 6087], [1.1, 1.2, 1.25, 1.3]);
        Cantrips(CasterFold.DefenceKey, [2600, 2588, 4663, 6091], [0.03, 0.05, 0.07, 0.09]);
        Cantrips(CasterFold.ElementalKey, [3251, 3250, 4670, 6098], [0.01, 0.03, 0.05, 0.07]);
        return rows;
    }
}

/// <summary>
/// M6 14 casterfair: a caster's own numbers, its appraisal unfolded. ACE's appraisal adds to a caster's WeaponDefense the enchantments on it and, when it is wielded, the wielder's auras; multiplies its ManaConversionMod by the same pair,
/// and adds the pair to its ElementalDamageMod (<c>AppraiseInfo.AddPropertyEnchantments</c>, AppraiseInfo.cs:415-453 @95ef426d; <c>ResistMaskHelper</c>, ResistMask.cs:127-145), marking the last two in the resist highlight (0x1000
/// ManaConversionMod, 0x2000 ElementalDamageMod, ResistMask.cs:23-24). The enchantments on the caster are its masked spell book entries (<see cref="Appraisal.ItemEnchantments"/>); the wielder's are her live auras
/// (<see cref="CasterFold"/>), counted only for the caster she wields.
/// </summary>
/// <param name="Defence">Its own WeaponDefense (1 when absent).</param>
/// <param name="ManaConversion">Its own ManaConversionMod (0 when absent).</param>
/// <param name="Elemental">Its own ElementalDamageMod, or null when absent.</param>
/// <param name="Note">How it was read, for the log.</param>
public sealed record CasterIntrinsic(double Defence, double ManaConversion, double? Elemental, string Note)
{
    private const ushort ManaHighlight = 0x1000, ElementalHighlight = 0x2000;

    /// <summary>The caster's own numbers (see the class remarks).</summary>
    /// <param name="guid">The caster.</param>
    /// <param name="a">Its appraisal.</param>
    /// <param name="fold">Her live auras and the caster she wields.</param>
    public static CasterIntrinsic Of(uint guid, Appraisal a, CasterFold fold)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(fold);
        // her auras are in the appraisal only when it was taken while she wielded it: ACE marks a folded ManaConversionMod or ElementalDamageMod in the resist highlight, so a caster with either is read by its marks (an appraisal
        // taken in her pack before the wield shows none); one with neither carries no mark, and the session appraises a worn piece again every few minutes, so wielded now is read as wielded then
        var highlight = a.ResistEnchantments?.Highlight ?? 0;
        var marked = a.Float(SelfEquipJudge.ManaConversionModFloat) is > 0 || a.Float(AppraisalIds.ElementalDamageMod) is not null;
        var wielded = guid != 0 && guid == fold.WieldedCaster && (!marked || (highlight & (ManaHighlight | ElementalHighlight)) != 0);
        var ench = a.ItemEnchantments.IsDefaultOrEmpty ? [] : a.ItemEnchantments.ToArray();
        double OnIt(uint key, double none)
        {
            var aura = CasterSpells.Top(ench, key, aura: true, none);
            var cantrip = CasterSpells.Top(ench, key, aura: false, none);
            return key == CasterFold.ManaKey ? aura * cantrip : aura + cantrip;
        }

        var defenceAdd = OnIt(CasterFold.DefenceKey, 0) + (wielded ? fold.Defence : 0);
        var manaFactor = OnIt(CasterFold.ManaKey, 1.0) * (wielded ? fold.ManaFactor : 1.0);
        var elementalAdd = OnIt(CasterFold.ElementalKey, 0) + (wielded ? fold.Elemental : 0);
        var appDefence = a.Float(GearScore.WeaponDefenseFloat);
        var appMana = a.Float(SelfEquipJudge.ManaConversionModFloat);
        var appElemental = a.Float(AppraisalIds.ElementalDamageMod);
        var defence = appDefence is { } d ? Math.Max(0, d - defenceAdd) : 1.0;
        var manaFolded = (highlight & ManaHighlight) != 0;
        var mana = appMana is { } m ? (manaFolded && manaFactor > 0 ? m / manaFactor : m) : 0.0;
        var elementalFolded = (highlight & ElementalHighlight) != 0;
        double? elemental = appElemental is { } e ? (elementalFolded ? e - elementalAdd : e) : null;
        var unfolded = (appDefence is { } ad && ad != defence) || (appMana is { } am && am != mana) || (appElemental is { } ae && ae != elemental); // a value the appraisal lacks is no fold
        var note = !unfolded
            ? "its appraisal is its own numbers"
            : string.Create(CultureInfo.InvariantCulture, $"its appraisal (defence x{appDefence ?? 1.0:0.###}, mana conversion {appMana ?? 0:0.###}, elemental x{appElemental ?? 1.0:0.###}) unfolded of {(wielded ? "her auras on her (" + fold + ") and " : string.Empty)}{(ench.Length == 0 ? "no enchantment on it" : ench.Length + " enchantment(s) on it")}: its own defence x{defence:0.###}, mana conversion {mana:0.###}, elemental x{elemental ?? 1.0:0.###} (AppraiseInfo.cs:415-453)");
        if ((manaFolded && manaFactor == 1.0) || (elementalFolded && elementalAdd == 0))
        {
            note += "; TODO(m6-caster-fold-unread): ACE marks a fold whose enchantments are not read now: that value is taken as appraised";
        }

        return new CasterIntrinsic(defence, mana, elemental, note);
    }
}

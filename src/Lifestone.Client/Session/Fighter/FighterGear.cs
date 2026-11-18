using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Fighter;

/// <summary>What a piece of the fighters' magic gear is (M4 P5a C6, owner direction D40).</summary>
public enum GearPieceKind
{
    /// <summary>A body armour piece, worn.</summary>
    Armour,

    /// <summary>The weapon (the Brawler's melee weapon, wielded; the Archer's bow, kept in the pack and wielded by the hunt's swap).</summary>
    Weapon,

    /// <summary>The necklace.</summary>
    Necklace,

    /// <summary>A ring (two are worn, one on each hand).</summary>
    Ring,

    /// <summary>A bracelet (two are worn, one on each wrist).</summary>
    Bracelet,
}

/// <summary>A spell an item carries: ace_world.spell's id and name.</summary>
/// <param name="Id">The spell id.</param>
/// <param name="Name">The spell's name.</param>
public readonly record struct GearSpell(uint Id, string Name);

/// <summary>One wield requirement of an item (ACE's WieldRequirements, WieldSkillType, WieldDifficulty; ACE 95ef426d <c>Player_Inventory.cs:2080-2200</c> <c>CheckWieldRequirements</c>).</summary>
/// <param name="Requirement">ACE's WieldRequirement: 7 = Level (the only kind the picks have).</param>
/// <param name="Type">The skill or attribute id (unused for a Level requirement).</param>
/// <param name="Difficulty">The least value.</param>
public readonly record struct GearRequirement(int Requirement, int Type, int Difficulty);

/// <summary>
/// One item of a fighter's magic gear, pinned from ace_world (M4 P5a C6): the weenie, its slot(s), its Arcane Lore difficulty, its spells, its wield requirements and its mana.
/// </summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">The weenie's name (PropertyString 1).</param>
/// <param name="ClassName">The weenie's class name (the world database's key).</param>
/// <param name="Kind">What it is.</param>
/// <param name="Locations">ValidLocations (PropertyInt 9): the body slots the item takes (all of them at once for armour; either hand or wrist for a ring or bracelet).</param>
/// <param name="ItemType">PropertyInt 1.</param>
/// <param name="WeenieType">The weenie's type (ACE WeenieType).</param>
/// <param name="ItemDifficulty">PropertyInt 109, the Arcane Lore the wielder needs for the spells to activate; 0 when the weenie has none.</param>
/// <param name="Burden">PropertyInt 5.</param>
/// <param name="Value">PropertyInt 19.</param>
/// <param name="ItemMana">PropertyInt 107 ItemCurMana (= ItemMaxMana) of a fresh item.</param>
/// <param name="ManaRate">PropertyFloat 5 ManaRate: the mana the item burns per second while its spells are active (negative).</param>
/// <param name="Spells">The item's spell book.</param>
/// <param name="Requirements">The wield requirements.</param>
public sealed record GearPiece(
    uint Wcid,
    string Name,
    string ClassName,
    GearPieceKind Kind,
    uint Locations,
    uint ItemType,
    uint WeenieType,
    int ItemDifficulty,
    int Burden,
    int Value,
    int ItemMana,
    double ManaRate,
    IReadOnlyList<GearSpell> Spells,
    IReadOnlyList<GearRequirement> Requirements)
{
    /// <summary>The hours the item's mana lasts while its spells are active (ItemMana / -ManaRate; ACE <c>Player_Tick.cs</c> <c>ManaConsumersTick</c>).</summary>
    public double ManaHours => ManaRate < 0 ? ItemMana / -ManaRate / 3600.0 : double.PositiveInfinity;

    /// <summary>True for the Brawler's melee weapon (wielded by the build); false for a bow (kept in the pack).</summary>
    public bool IsMelee => Kind == GearPieceKind.Weapon && (ItemType & FighterKit.ItemTypeMelee) != 0;

    /// <summary>The kit line: the fighter's role; the build wields or wears every line but the bow.</summary>
    public FighterItem ToKitItem() => new(
        new GearItem(Wcid, Name, Kind == GearPieceKind.Weapon ? GearKind.Wand : GearKind.Armour, WeenieType, ItemType, (EquipMask)Locations, 1, null, Burden),
        Kind switch
        {
            GearPieceKind.Armour => FighterRole.Armour,
            GearPieceKind.Weapon => IsMelee ? FighterRole.Weapon : FighterRole.Bow,
            _ => FighterRole.Jewelry,
        },
        Value,
        Kind != GearPieceKind.Weapon || IsMelee,
        Kind == GearPieceKind.Weapon ? (IsMelee ? FighterKit.MaceSkillId : FighterKit.BowSkillId) : null);
}

/// <summary>
/// The fighters' magic gear (M4 P5a C6, the owner's direction D40: give them some magic armor that is a bit random and mismatched but has helpful spells, and the same for their weapons and jewellery): per fighter, the nine body-armour slots covered by
/// mismatched pieces of retail sets, a weapon, a necklace, two rings and two bracelets, every one a real ace_world weenie with helpful spells. Pinned as data; the choice is reproducible.
/// </summary>
/// <remarks>
/// <para><b>The selection rule</b> (the throwaway scripts dump.py, filter.py and select.py of the lead's cache, over read-only SELECTs of ace_world, ACE-World-Database v0.9.296; the candidates are summarised in the untracked cache note c6-gear-candidates.md).
/// Candidates: a weenie with ItemCurMana above 1 (ACE activates an equipped item's spells only then: <c>Player_Inventory.cs:314</c>), whose mana lasts at least 4 hours at its ManaRate, not attuned and not bonded, with no Quest,
/// QuestRestriction or KillQuest string, no heritage restriction, a name that is not a test item, ItemDifficulty (the Arcane Lore the spells need) at most <see cref="ArcaneLoreTarget"/>, wield requirements only of kind Level at most
/// <see cref="Level"/> (no skill or attribute requirement: the fighters' skills move with the creation attributes), and a spell book of which no spell is harmful (no Bane, Other, Feeble, Vulnerability, Curse or the like) and at least one a helpful line
/// (attribute, skill, protection, armour, regeneration, ward, aegis, damage auras). Armour: body-armour locations only; the weapon: the Brawler's a one-handed Heavy Weapons (44) bludgeoning (4) melee weapon, the Archer's a Missile Weapons (47) bow taking Arrows (AmmoType 1);
/// jewelry by its ValidLocations (necklace 0x8000, ring 0xC0000, bracelet 0x30000). Order: sha256 of the seed (<c>lifestone-p5a-c6/melee</c> or <c>lifestone-p5a-c6/archer</c>), a colon and the wcid, lowest first. The armour is the first exact cover of the nine armour slots
/// (head 0x1, hands 0x20, feet 0x100, chest 0x200, abdomen 0x400, upper arms 0x800, lower arms 0x1000, upper legs 0x2000, lower legs 0x4000) in that order by depth-first search, with no two pieces overlapping, no two of one name and at most 6,500 burden; the weapon, the
/// necklace, the two rings and the two bracelets are the first of their order. The Archer's picks exclude the Brawler's.</para>
/// <para><b>Not retail:</b> which items a character wears is Lifestone's own test-character choice; the items, their spells, difficulties and requirements are ace_world's.</para>
/// <para><b>ACE's rule.</b> A wielded item's spell book is activated when its ItemCurMana is above 0 and the wielder's Arcane Lore (current) is at least its ItemDifficulty: <c>Player_Inventory.cs:314</c> (<c>if (item.ItemCurMana &gt; 0) TryActivateSpells</c>), <c>:327</c> (<c>CheckUseRequirements</c>) and
/// <c>WorldObject_Use.cs:205-209</c> (<c>arcaneLore.Current &lt; ItemDifficulty</c> refuses with "Your Arcane Lore is too low"); the item still equips. Creature and Life magic spells go on the wearer, Item magic spells on the item (<c>Creature_Magic.cs:98-134</c> <c>CreateItemSpell</c>). Wield requirements: <c>Player_Inventory.cs:2080-2200</c> (all ACE 95ef426d).
/// The mana burns while the spells are active, every ~5 s (<c>Player_Tick.cs:620-660</c>); at 0 the spells are removed (<c>HandleManaDepleted</c>). The journey's build-verify recharges the items with a retail mana charge when one runs low and wears a piece whose
/// spells were removed again (<see cref="GearRecharge"/>, <see cref="FighterKit.ManaChargeWcid"/>; <c>TODO(m4-gear-mana)</c> closed).</para>
/// </remarks>
public sealed class FighterGearSet
{
    /// <summary>The Arcane Lore the gear is picked for (a fighter at level 55 has at least this: M4 P5a C5).</summary>
    public const int ArcaneLoreTarget = 150;

    /// <summary>The level the gear is picked for (every Level requirement is at most this).</summary>
    public const int Level = 55;

    /// <summary>The nine body-armour slots every set covers: head, hands, feet, chest, abdomen, upper arms, lower arms, upper legs, lower legs (ACE EquipMask).</summary>
    public const uint ArmourSlots = 0x1 | 0x20 | 0x100 | 0x200 | 0x400 | 0x800 | 0x1000 | 0x2000 | 0x4000;

    private FighterGearSet(string key, IReadOnlyList<GearPiece> pieces)
    {
        Key = key;
        Pieces = pieces;
    }

    /// <summary>The Brawler's gear ("melee").</summary>
    public static FighterGearSet Brawler { get; } = new("melee", [
        new(45456, "Staff of All Aspects", "ace45456-staffofallaspects", GearPieceKind.Weapon, 0x100000, 1, 6, 0, 500, 50000, 2400, -0.033, [new(4395, "Aura of Incantation of Blood Drinker Self"), new(4400, "Aura of Incantation of Defender Self"), new(4460, "Incantation of Acid Protection Self"), new(4466, "Incantation of Cold Protection Self"), new(4468, "Incantation of Fire Protection Self"), new(4470, "Incantation of Lightning Protection Self"), new(4661, "Epic Blood Thirst"), new(4712, "Epic Heavy Weapon Aptitude")], []),
        new(30528, "Helm of Leikotha's Tears", "helmrareleikotha", GearPieceKind.Armour, 0x1, 2, 2, 0, 600, 50000, 5000, -0.033, [new(3964, "Epic Focus"), new(4407, "Incantation of Impenetrability"), new(4705, "Epic Mana Conversion Prowess")], []),
        new(30518, "Girth of Leikotha's Tears", "girthrareleikotha", GearPieceKind.Armour, 0x400, 2, 2, 0, 900, 50000, 5000, -0.033, [new(4407, "Incantation of Impenetrability"), new(4704, "Epic Magic Resistance"), new(4911, "Epic Armor")], []),
        new(30367, "Footman's Boots", "bootsrarefootman", GearPieceKind.Armour, 0x100, 2, 2, 0, 200, 50000, 1000, -0.033, [new(4019, "Epic Quickness"), new(4407, "Incantation of Impenetrability"), new(4710, "Epic Sprint")], []),
        new(27912, "Ancient Armored Gauntlets", "gauntletshizkri1", GearPieceKind.Armour, 0x20, 2, 2, 0, 225, 6000, 800, -0.033, [new(3094, "Skin of the Fiazhat"), new(3312, "Lesser Flange Aegis")], [new(7, 1, 40)]),
        new(27128, "Ancient Armored Bracers", "bracerfalatacot1", GearPieceKind.Armour, 0x1000, 2, 2, 0, 250, 6000, 800, -0.033, [new(3092, "Lesser Skin of the Fiazhat"), new(3153, "Lesser Scythe Aegis")], [new(7, 1, 40)]),
        new(2032, "Branith's Shirt", "shirtstuddedleatherbranith", GearPieceKind.Armour, 0xA00, 2, 2, 0, 700, 6000, 500, -0.025, [new(1033, "Cold Protection Self IV"), new(1092, "Fire Protection Self IV")], []),
        new(30531, "Patriarch's Twilight Tights", "leggingsrarepatriarchtwilight", GearPieceKind.Armour, 0x6000, 2, 2, 0, 650, 50000, 3200, -0.033, [new(4227, "Epic Willpower"), new(4407, "Incantation of Impenetrability"), new(4681, "Epic Mana Gain"), new(4682, "Epic Stamina Gain")], []),
        new(28396, "Tears of Jvikti Kheval", "necklacekiviklir1", GearPieceKind.Necklace, 0x8000, 8, 1, 100, 50, 2500, 800, -0.033, [new(3408, "Lesser Gift of the Fiazhat"), new(3436, "Lesser Eyes Beyond the Mist")], [new(7, 1, 40)]),
        new(30364, "Weeping Ring", "ringrareweeping", GearPieceKind.Ring, 0xC0000, 8, 1, 0, 15, 50000, 3000, -0.033, [new(4556, "Incantation of Healing Mastery Self"), new(4582, "Incantation of Life Magic Mastery Self"), new(4700, "Epic Life Magic Aptitude")], []),
        new(30363, "Unchained Prowess Ring", "ringrareunchainedprowess", GearPieceKind.Ring, 0xC0000, 8, 1, 0, 15, 50000, 3000, -0.033, [new(3963, "Epic Coordination"), new(3964, "Epic Focus"), new(4506, "Incantation of Alchemy Mastery Self"), new(4526, "Incantation of Cooking Mastery Self"), new(4552, "Incantation of Fletching Mastery Self"), new(4586, "Incantation of Lockpick Mastery Self")], []),
        new(30366, "Aristocrat's Bracelet", "braceletrarearistocrat", GearPieceKind.Bracelet, 0x30000, 8, 1, 0, 60, 50000, 3000, -0.033, [new(2666, "Essence Glutton"), new(4232, "Epic Leadership"), new(4498, "Incantation of Rejuvenation Self"), new(4538, "Incantation of Finesse Weapon Mastery Self"), new(4578, "Incantation of Leadership Mastery Self"), new(4608, "Incantation of Person Attunement Self"), new(4691, "Epic Finesse Weapon Aptitude")], []),
        new(30356, "Circle of Pure Thought", "braceletrarepurethought", GearPieceKind.Bracelet, 0x30000, 8, 1, 0, 60, 50000, 3000, -0.033, [new(3964, "Epic Focus"), new(4227, "Epic Willpower"), new(4305, "Incantation of Focus Self"), new(4329, "Incantation of Willpower Self"), new(4494, "Incantation of Mana Renewal Self"), new(4498, "Incantation of Rejuvenation Self")], []),
    ]);

    /// <summary>The Archer's gear.</summary>
    public static FighterGearSet Archer { get; } = new("archer", [
        new(30303, "Serpent's Flight", "bowrareserpentsflight", GearPieceKind.Weapon, 0x400000, 256, 3, 0, 800, 50000, 2000, -0.05, [new(3963, "Epic Coordination"), new(4400, "Aura of Incantation of Defender Self"), new(4522, "Incantation of Missile Weapon Mastery Self"), new(4596, "Incantation of Magic Resistance Self"), new(4661, "Epic Blood Thirst"), new(4682, "Epic Stamina Gain"), new(4713, "Epic Missile Weapon Aptitude"), new(5183, "Aura of Incantation of Blood Drinker Self")], []),
        new(30525, "Gauntlets of Leikotha's Tears", "gauntletsrareleikotha", GearPieceKind.Armour, 0x20, 2, 2, 0, 300, 50000, 5000, -0.033, [new(3963, "Epic Coordination"), new(4407, "Incantation of Impenetrability"), new(4694, "Epic Healing Prowess")], []),
        new(30369, "Tracker Boots", "bootsraretracker", GearPieceKind.Armour, 0x100, 2, 2, 0, 200, 50000, 6000, -0.033, [new(4019, "Epic Quickness"), new(4407, "Incantation of Impenetrability"), new(4710, "Epic Sprint")], []),
        new(30523, "Bracers of Leikotha's Tears", "bracersrareleikotha", GearPieceKind.Armour, 0x1000, 2, 2, 0, 480, 50000, 5000, -0.033, [new(3965, "Epic Strength"), new(4407, "Incantation of Impenetrability"), new(4692, "Epic Fealty")], []),
        new(30527, "Valkeer's Helm", "helmrarevalkeer", GearPieceKind.Armour, 0x1, 2, 2, 0, 100, 50000, 6000, -0.033, [new(3964, "Epic Focus"), new(4407, "Incantation of Impenetrability"), new(4705, "Epic Mana Conversion Prowess")], []),
        new(30530, "Dusk Leggings", "leggingsraredusk", GearPieceKind.Armour, 0x6000, 2, 2, 0, 640, 50000, 2000, -0.033, [new(4227, "Epic Willpower"), new(4407, "Incantation of Impenetrability"), new(4681, "Epic Mana Gain"), new(4682, "Epic Stamina Gain")], []),
        new(30517, "Gelidite Girth", "girthraregelidite", GearPieceKind.Armour, 0x400, 2, 2, 0, 900, 50000, 6000, -0.033, [new(4407, "Incantation of Impenetrability"), new(4704, "Epic Magic Resistance"), new(4911, "Epic Armor")], []),
        new(26501, "Ancient Armored Vestment", "shirtfalatacot1", GearPieceKind.Armour, 0xA00, 2, 2, 0, 550, 6000, 800, -0.033, [new(3046, "Lesser Mists of Bur"), new(3092, "Lesser Skin of the Fiazhat")], [new(7, 1, 40)]),
        new(30357, "Golden Snake Choker", "necklaceraregoldensnake", GearPieceKind.Necklace, 0x8000, 8, 1, 0, 100, 50000, 3000, -0.033, [new(4510, "Incantation of Arcane Enlightenment Self"), new(4564, "Incantation of Item Enchantment Mastery Self"), new(4684, "Epic Arcane Prowess"), new(4697, "Epic Item Enchantment Aptitude")], []),
        new(7300, "Dansha-Ki's Ring", "ringdanshaki", GearPieceKind.Ring, 0xC0000, 8, 1, 110, 15, 5200, 881, -0.033, [new(278, "Magic Resistance Self V"), new(1070, "Lightning Protection Self V")], []),
        new(30361, "Loop of Opposing Benedictions", "ringrareopposingbenedictions", GearPieceKind.Ring, 0xC0000, 8, 1, 0, 15, 50000, 3000, -0.033, [new(4466, "Incantation of Cold Protection Self"), new(4468, "Incantation of Fire Protection Self"), new(4675, "Epic Flame Ward"), new(4676, "Epic Frost Ward")], []),
        new(30353, "Dreamseer Bangle", "braceletraredreamseerbangle", GearPieceKind.Bracelet, 0x30000, 8, 1, 0, 60, 50000, 3000, -0.033, [new(2014, "Wizard's Ultimate Intellect"), new(4494, "Incantation of Mana Renewal Self"), new(4602, "Incantation of Mana Conversion Mastery Self"), new(4681, "Epic Mana Gain"), new(4705, "Epic Mana Conversion Prowess")], []),
        new(29093, "Viamontian Guardian Bracelet", "braceletthrungusreward", GearPieceKind.Bracelet, 0x30000, 8, 1, 15, 60, 1000, 400, -0.013, [new(24, "Armor Self I"), new(165, "Regeneration Self I"), new(982, "Sprint Self I")], []),
    ]);

    /// <summary>The template key this set belongs to (<c>melee</c>, <c>archer</c>).</summary>
    public string Key { get; }

    /// <summary>The pieces: the weapon, the armour, the necklace, the rings, the bracelets.</summary>
    public IReadOnlyList<GearPiece> Pieces { get; }

    /// <summary>Every gear weenie class of both sets (the sale rule never sells them).</summary>
    public static IReadOnlyList<uint> AllWcids { get; } = [.. Brawler.Pieces.Concat(Archer.Pieces).Select(p => p.Wcid).Distinct()];

    /// <summary>The set for a template key, or null (the Hunter has none).</summary>
    public static FighterGearSet? For(string key) => key switch { "melee" => Brawler, "archer" => Archer, _ => null };

    /// <summary>The weapon piece.</summary>
    public GearPiece Weapon => Pieces.Single(p => p.Kind == GearPieceKind.Weapon);

    /// <summary>The highest ItemDifficulty of the set.</summary>
    public int MaxItemDifficulty => Pieces.Max(p => p.ItemDifficulty);

    /// <summary>The set's burden.</summary>
    public int Burden => Pieces.Sum(p => p.Burden);

    /// <summary>The piece for <paramref name="wcid"/>, or null.</summary>
    public GearPiece? PieceOf(uint wcid) => Pieces.FirstOrDefault(p => p.Wcid == wcid);

    /// <summary>The kit lines for the set, in the order the build grants and wears them (the weapon, the armour, the jewelry).</summary>
    public IEnumerable<FighterItem> KitLines() => Pieces.Select(p => p.ToKitItem());

    /// <summary>The spells of <paramref name="piece"/> that go on the wearer: the Creature Enchantment and Life Magic ones (the others go on the item itself).</summary>
    /// <param name="piece">The piece.</param>
    /// <param name="schoolOf">The school of a spell id, or null when unknown.</param>
    public static IEnumerable<GearSpell> WearerSpells(GearPiece piece, Func<uint, MagicSchool?> schoolOf)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(schoolOf);
        return piece.Spells.Where(s => schoolOf(s.Id) is MagicSchool.CreatureEnchantment or MagicSchool.LifeMagic);
    }
}

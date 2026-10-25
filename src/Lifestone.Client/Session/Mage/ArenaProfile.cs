using System.Collections.Frozen;
using DatReaderWriter.Enums;

namespace Lifestone.Client.Session.Mage;

/// <summary>What a spell line is for in the BSD profile (docs/milestones/m3-lights-and-sound.md section 5.2).</summary>
public enum ProfileRole
{
    /// <summary>A creature-enchantment buff the mage casts on itself (attributes, the four masteries, mana, the defences).</summary>
    CreatureBuff,

    /// <summary>A life-magic buff the mage casts on itself (armour, protection, regeneration, rejuvenation, mana renewal).</summary>
    LifeBuff,

    /// <summary>An item-enchantment buff cast on the worn armour (Impenetrability, Bludgeon Bane).</summary>
    ItemBuffArmor,

    /// <summary>The wand's lines (Hermetic Link, Defender): on this server they are Auras that target the caster and land as the player's enchantment (ACE's spell 1479 is "Aura of Hermetic Link Self V"), so they are self casts on the caster (the cast form follows the SpellTable's target type, <c>GearCasts.FormOf</c>), not casts on the wielded wand.</summary>
    ItemBuffWand,

    /// <summary>The line the mage restores mana with (Stamina to Mana Self, D24): a Life Magic Transfer spell, cast when the next cast needs more mana than it has. Not a buff: it has no duration and the buff cycle never lists it.</summary>
    ManaTransfer,

    /// <summary>The line the mage restores stamina with after a transfer (Revitalize Self, D24): a Life Magic Boost spell. Not a buff.</summary>
    StaminaRestore,

    /// <summary>The war line the loops fight with (Flame Bolt).</summary>
    WarBolt,

    /// <summary>A war line offered as an option (Flame Streak, Volley, Blast, Arc).</summary>
    WarOption,

    /// <summary>Harm Self (M4 P5a): the Life Magic Boost spell that takes health away from the caster (the heal tests' way of getting hurt). Not a buff; never in the cycle.</summary>
    HarmSelf,

    /// <summary>Heal Self (M4 P5a): the Life Magic Boost spell that gives health back to the caster. Not a buff; never in the cycle.</summary>
    HealSelf,

    /// <summary>A travel spell (M4 P5a, plan D36): Lifestone Recall, Primary Portal Tie or Primary Portal Recall, Item Enchantment spells with no duration. Not buffs.</summary>
    Travel,
}

/// <summary>
/// One spell of a profile line, exactly as the SpellTable (0x0E00000E) holds it (pinned by a dat test).
/// </summary>
/// <param name="Position">1-based position in the line's id list (I-VI, then VII, then VIII). It equals the level numeral except
/// for Flame Volley and Flame Blast (TODO(m3-volley-blast-levels)): Volley's positions 1 and 2 are the dat's "III" and "IV" again
/// (ids 3663 and 3664, powers 125 and 175), and Blast has 7 spells, its position 1 being "I" (power 75) and its position 2 "III".</param>
/// <param name="Id">The spell id.</param>
/// <param name="Name">The dat's own name for the spell (not the spec's).</param>
/// <param name="Power">The spell's <c>Power</c> (ACE's cast-chance difficulty).</param>
/// <param name="DurationSeconds">The enchantment's duration in seconds (0 for a projectile).</param>
public sealed record ProfileSpell(int Position, uint Id, string Name, uint Power, double DurationSeconds);

/// <summary>One spell line of the profile: the spec's name, the dat's name root, the school, the role and the spells.</summary>
/// <param name="Name">The spec's line name.</param>
/// <param name="DatRoot">The dat's name for the line (for example "Mana Conversion Mastery Self" for the spec's "Mana Mastery Self").</param>
/// <param name="School">The spell school (the SpellTable's).</param>
/// <param name="Role">What the line is for.</param>
/// <param name="Spells">The spells in list order: levels I-VI, VII, VIII (Volley and Blast: see <see cref="ProfileSpell.Position"/>).</param>
public sealed record ProfileLine(string Name, string DatRoot, MagicSchool School, ProfileRole Role, IReadOnlyList<ProfileSpell> Spells)
{
    /// <summary>True for the self and item buffs (an enchantment with a duration): the 24 buff lines the cycle casts. The mana lines and the war lines are not buffs.</summary>
    public bool IsBuff => Role is ProfileRole.CreatureBuff or ProfileRole.LifeBuff or ProfileRole.ItemBuffArmor or ProfileRole.ItemBuffWand;

    /// <summary>True for the two mana lines (<see cref="ProfileRole.ManaTransfer"/> and <see cref="ProfileRole.StaminaRestore"/>): spells the cycle casts to keep casting, with no duration.</summary>
    public bool IsManaLine => Role is ProfileRole.ManaTransfer or ProfileRole.StaminaRestore;

    /// <summary>The spell with the highest <c>Power</c> that is at most <paramref name="power"/>, or null (list order breaks ties).</summary>
    public ProfileSpell? HighestAtMostPower(uint power) =>
        Spells.Where(s => s.Power <= power).OrderByDescending(s => s.Power).ThenByDescending(s => s.Position).FirstOrDefault();
}

/// <summary>
/// The BSD mage's profile as data (docs/milestones/m3-lights-and-sound.md section 5.2, plan decisions D10, D12): every spell
/// line (the creature, life and item buffs and the war lines) with, per level, the id, the dat's own name, the school, the
/// <c>Power</c> and the <c>Duration</c> the SpellTable pins (<c>BsdProfileSpellsTests</c>, <c>ArenaProfileTests</c>), the tuskers
/// the loops hunt and the arena. Immutable, no I/O.
/// </summary>
/// <remarks>
/// <para>The spells are plain SpellTable entries (none is in a spell set). The buff cycle of item 2.1 picks a level per line by
/// cast chance (D10), so the mage learns every level of every line (<see cref="SpellsToLearn"/>). Pattern of every buff line:
/// powers 1, 50, 100, 150, 200, 250, 300, 400 and durations 1800 (I-V), 2700 (VI), 3600 (VII), 5400 (VIII) seconds, with the
/// exceptions Mana Renewal and Hermetic Link (level I power 25) and Defender (level I power 1, ids 1599 then 1601-1605,
/// level VIII power 350).</para>
/// <para>The two mana lines (M3 2.1 s0a, D24: "a mage restores mana by Stamina to Mana, then its stamina by Revitalize") follow the
/// buffs: Stamina to Mana Self (1676-1681, VII 2345 "Meditative Trance", VIII 4981) and Revitalize Self (1177-1182, VII 2083
/// "Robustification", VIII 4321), Life Magic, powers 1, 50, 100, 150, 200, 250, 300, 400, no duration. The build scenario learns them
/// with every other line (<see cref="SpellsToLearn"/>, D3); <see cref="BuffStatMods"/> holds what they do.</para>
/// <para>TODO(m3-volley-blast-levels) stays open: Flame Volley holds 8 ids (3663, 3664 repeat the dat's own III and IV before
/// 143-146) and Flame Blast 7 (no level II); the pins keep the dat's oddities.</para>
/// </remarks>
public static class ArenaProfile
{
    /// <summary>The tuskers' weenie class ids (D12): the only loop targets.</summary>
    public static readonly IReadOnlyList<uint> TargetWcids = [1628, 1629];

    /// <summary>The Aluvian den's portal (D12).</summary>
    public const uint ArenaPortalWcid = 7242;

    /// <summary>The cell the Aluvian den's portal lands in (D12).</summary>
    public const uint ArenaCell = 0x01050375;

    /// <summary>The war line the loops fight with.</summary>
    public const string WarLineName = "Flame Bolt";

    /// <summary>Every line, in the spec's order.</summary>
    public static IReadOnlyList<ProfileLine> Lines { get; } =
    [
        L("Strength Self", "Strength Self", MagicSchool.CreatureEnchantment, ProfileRole.CreatureBuff,
            S(2, "Strength Self I", 1, 1800),
            S(1328, "Strength Self II", 50, 1800),
            S(1329, "Strength Self III", 100, 1800),
            S(1330, "Strength Self IV", 150, 1800),
            S(1331, "Strength Self V", 200, 1800),
            S(1332, "Strength Self VI", 250, 2700),
            S(2087, "Might of the Lugians", 300, 3600),
            S(4325, "Incantation of Strength Self", 400, 5400)),
        L("Endurance Self", "Endurance Self", MagicSchool.CreatureEnchantment, ProfileRole.CreatureBuff,
            S(1349, "Endurance Self I", 1, 1800),
            S(1350, "Endurance Self II", 50, 1800),
            S(1351, "Endurance Self III", 100, 1800),
            S(1352, "Endurance Self IV", 150, 1800),
            S(1353, "Endurance Self V", 200, 1800),
            S(1354, "Endurance Self VI", 250, 2700),
            S(2061, "Perseverance", 300, 3600),
            S(4299, "Incantation of Endurance Self", 400, 5400)),
        L("Coordination Self", "Coordination Self", MagicSchool.CreatureEnchantment, ProfileRole.CreatureBuff,
            S(1373, "Coordination Self I", 1, 1800),
            S(1374, "Coordination Self II", 50, 1800),
            S(1375, "Coordination Self III", 100, 1800),
            S(1376, "Coordination Self IV", 150, 1800),
            S(1377, "Coordination Self V", 200, 1800),
            S(1378, "Coordination Self VI", 250, 2700),
            S(2059, "Honed Control", 300, 3600),
            S(4297, "Incantation of Coordination Self", 400, 5400)),
        L("Quickness Self", "Quickness Self", MagicSchool.CreatureEnchantment, ProfileRole.CreatureBuff,
            S(1397, "Quickness Self I", 1, 1800),
            S(1398, "Quickness Self II", 50, 1800),
            S(1399, "Quickness Self III", 100, 1800),
            S(1400, "Quickness Self IV", 150, 1800),
            S(1401, "Quickness Self V", 200, 1800),
            S(1402, "Quickness Self VI", 250, 2700),
            S(2081, "Hastening", 300, 3600),
            S(4319, "Incantation of Quickness Self", 400, 5400)),
        L("Focus Self", "Focus Self", MagicSchool.CreatureEnchantment, ProfileRole.CreatureBuff,
            S(1421, "Focus Self I", 1, 1800),
            S(1422, "Focus Self II", 50, 1800),
            S(1423, "Focus Self III", 100, 1800),
            S(1424, "Focus Self IV", 150, 1800),
            S(1425, "Focus Self V", 200, 1800),
            S(1426, "Focus Self VI", 250, 2700),
            S(2067, "Inner Calm", 300, 3600),
            S(4305, "Incantation of Focus Self", 400, 5400)),
        L("Willpower Self", "Willpower Self", MagicSchool.CreatureEnchantment, ProfileRole.CreatureBuff,
            S(1445, "Willpower Self I", 1, 1800),
            S(1446, "Willpower Self II", 50, 1800),
            S(1447, "Willpower Self III", 100, 1800),
            S(1448, "Willpower Self IV", 150, 1800),
            S(1449, "Willpower Self V", 200, 1800),
            S(1450, "Willpower Self VI", 250, 2700),
            S(2091, "Mind Blossom", 300, 3600),
            S(4329, "Incantation of Willpower Self", 400, 5400)),
        L("War Magic Mastery Self", "War Magic Mastery Self", MagicSchool.CreatureEnchantment, ProfileRole.CreatureBuff,
            S(629, "War Magic Mastery Self I", 1, 1800),
            S(630, "War Magic Mastery Self II", 50, 1800),
            S(631, "War Magic Mastery Self III", 100, 1800),
            S(632, "War Magic Mastery Self IV", 150, 1800),
            S(633, "War Magic Mastery Self V", 200, 1800),
            S(634, "War Magic Mastery Self VI", 250, 2700),
            S(2323, "Hieromancer's Blessing", 300, 3600),
            S(4638, "Incantation of War Magic Mastery Self", 400, 5400)),
        L("Creature Enchantment Mastery Self", "Creature Enchantment Mastery Self", MagicSchool.CreatureEnchantment, ProfileRole.CreatureBuff,
            S(557, "Creature Enchantment Mastery Self I", 1, 1800),
            S(558, "Creature Enchantment Mastery Self II", 50, 1800),
            S(559, "Creature Enchantment Mastery Self III", 100, 1800),
            S(560, "Creature Enchantment Mastery Self IV", 150, 1800),
            S(561, "Creature Enchantment Mastery Self V", 200, 1800),
            S(562, "Creature Enchantment Mastery Self VI", 250, 2700),
            S(2215, "Adja's Blessing", 300, 3600),
            S(4530, "Incantation of Creature Enchantment Mastery Self", 400, 5400)),
        L("Item Enchantment Mastery Self", "Item Enchantment Mastery Self", MagicSchool.CreatureEnchantment, ProfileRole.CreatureBuff,
            S(581, "Item Enchantment Mastery Self I", 1, 1800),
            S(582, "Item Enchantment Mastery Self II", 50, 1800),
            S(583, "Item Enchantment Mastery Self III", 100, 1800),
            S(584, "Item Enchantment Mastery Self IV", 150, 1800),
            S(585, "Item Enchantment Mastery Self V", 200, 1800),
            S(586, "Item Enchantment Mastery Self VI", 250, 2700),
            S(2249, "Celcynd's Blessing", 300, 3600),
            S(4564, "Incantation of Item Enchantment Mastery Self", 400, 5400)),
        L("Life Magic Mastery Self", "Life Magic Mastery Self", MagicSchool.CreatureEnchantment, ProfileRole.CreatureBuff,
            S(605, "Life Magic Mastery Self I", 1, 1800),
            S(606, "Life Magic Mastery Self II", 50, 1800),
            S(607, "Life Magic Mastery Self III", 100, 1800),
            S(608, "Life Magic Mastery Self IV", 150, 1800),
            S(609, "Life Magic Mastery Self V", 200, 1800),
            S(610, "Life Magic Mastery Self VI", 250, 2700),
            S(2267, "Harlune's Blessing", 300, 3600),
            S(4582, "Incantation of Life Magic Mastery Self", 400, 5400)),
        L("Mana Mastery Self", "Mana Conversion Mastery Self", MagicSchool.CreatureEnchantment, ProfileRole.CreatureBuff,
            S(653, "Mana Conversion Mastery Self I", 1, 1800),
            S(654, "Mana Conversion Mastery Self II", 50, 1800),
            S(655, "Mana Conversion Mastery Self III", 100, 1800),
            S(656, "Mana Conversion Mastery Self IV", 150, 1800),
            S(657, "Mana Conversion Mastery Self V", 200, 1800),
            S(658, "Mana Conversion Mastery Self VI", 250, 2700),
            S(2287, "Nuhmudira's Blessing", 300, 3600),
            S(4602, "Incantation of Mana Conversion Mastery Self", 400, 5400)),
        L("Arcane Enlightenment Self", "Arcane Enlightenment Self", MagicSchool.CreatureEnchantment, ProfileRole.CreatureBuff,
            S(678, "Arcane Enlightenment Self I", 1, 1800),
            S(679, "Arcane Enlightenment Self II", 50, 1800),
            S(680, "Arcane Enlightenment Self III", 100, 1800),
            S(681, "Arcane Enlightenment Self IV", 150, 1800),
            S(682, "Arcane Enlightenment Self V", 200, 1800),
            S(683, "Arcane Enlightenment Self VI", 250, 2700),
            S(2195, "Aliester's Blessing", 300, 3600),
            S(4510, "Incantation of Arcane Enlightenment Self", 400, 5400)),
        L("Magic Resistance Self", "Magic Resistance Self", MagicSchool.CreatureEnchantment, ProfileRole.CreatureBuff,
            S(274, "Magic Resistance Self I", 1, 1800),
            S(275, "Magic Resistance Self II", 50, 1800),
            S(276, "Magic Resistance Self III", 100, 1800),
            S(277, "Magic Resistance Self IV", 150, 1800),
            S(278, "Magic Resistance Self V", 200, 1800),
            S(279, "Magic Resistance Self VI", 250, 2700),
            S(2281, "Aura of Resistance", 300, 3600),
            S(4596, "Incantation of Magic Resistance Self", 400, 5400)),
        L("Invulnerability Self", "Invulnerability Self", MagicSchool.CreatureEnchantment, ProfileRole.CreatureBuff,
            S(18, "Invulnerability Self I", 1, 1800),
            S(245, "Invulnerability Self II", 50, 1800),
            S(246, "Invulnerability Self III", 100, 1800),
            S(247, "Invulnerability Self IV", 150, 1800),
            S(248, "Invulnerability Self V", 200, 1800),
            S(249, "Invulnerability Self VI", 250, 2700),
            S(2245, "Aura of Defense", 300, 3600),
            S(4560, "Incantation of Invulnerability Self", 400, 5400)),
        L("Impregnability Self", "Impregnability Self", MagicSchool.CreatureEnchantment, ProfileRole.CreatureBuff,
            S(256, "Impregnability Self I", 1, 1800),
            S(257, "Impregnability Self II", 50, 1800),
            S(258, "Impregnability Self III", 100, 1800),
            S(259, "Impregnability Self IV", 150, 1800),
            S(260, "Impregnability Self V", 200, 1800),
            S(261, "Impregnability Self VI", 250, 2700),
            S(2243, "Aura of Deflection", 300, 3600),
            S(4558, "Incantation of Impregnability Self", 400, 5400)),
        L("Armor Self", "Armor Self", MagicSchool.LifeMagic, ProfileRole.LifeBuff,
            S(24, "Armor Self I", 1, 1800),
            S(1308, "Armor Self II", 50, 1800),
            S(1309, "Armor Self III", 100, 1800),
            S(1310, "Armor Self IV", 150, 1800),
            S(1311, "Armor Self V", 200, 1800),
            S(1312, "Armor Self VI", 250, 2700),
            S(2053, "Executor's Blessing", 300, 3600),
            S(4291, "Incantation of Armor Self", 400, 5400)),
        L("Bludgeon Protection Self", "Bludgeoning Protection Self", MagicSchool.LifeMagic, ProfileRole.LifeBuff,
            S(1018, "Bludgeoning Protection Self I", 1, 1800),
            S(1019, "Bludgeoning Protection Self II", 50, 1800),
            S(1020, "Bludgeoning Protection Self III", 100, 1800),
            S(1021, "Bludgeoning Protection Self IV", 150, 1800),
            S(1022, "Bludgeoning Protection Self V", 200, 1800),
            S(1023, "Bludgeoning Protection Self VI", 250, 2700),
            S(2153, "Blessing of the Mace Turner", 300, 3600),
            S(4464, "Incantation of Bludgeoning Protection Self", 400, 5400)),
        L("Regeneration Self", "Regeneration Self", MagicSchool.LifeMagic, ProfileRole.LifeBuff,
            S(165, "Regeneration Self I", 1, 1800),
            S(166, "Regeneration Self II", 50, 1800),
            S(167, "Regeneration Self III", 100, 1800),
            S(168, "Regeneration Self IV", 150, 1800),
            S(169, "Regeneration Self V", 200, 1800),
            S(170, "Regeneration Self VI", 250, 2700),
            S(2185, "Robustify", 300, 3600),
            S(4496, "Incantation of Regeneration Self", 400, 5400)),
        L("Rejuvenation Self", "Rejuvenation Self", MagicSchool.LifeMagic, ProfileRole.LifeBuff,
            S(54, "Rejuvenation Self I", 1, 1800),
            S(189, "Rejuvenation Self II", 50, 1800),
            S(190, "Rejuvenation Self III", 100, 1800),
            S(191, "Rejuvenation Self IV", 150, 1800),
            S(192, "Rejuvenation Self V", 200, 1800),
            S(193, "Rejuvenation Self VI", 250, 2700),
            S(2187, "Unflinching Persistence", 300, 3600),
            S(4498, "Incantation of Rejuvenation Self", 400, 5400)),
        L("Mana Renewal Self", "Mana Renewal Self", MagicSchool.LifeMagic, ProfileRole.LifeBuff,
            S(212, "Mana Renewal Self I", 25, 1800),
            S(213, "Mana Renewal Self II", 50, 1800),
            S(214, "Mana Renewal Self III", 100, 1800),
            S(215, "Mana Renewal Self IV", 150, 1800),
            S(216, "Mana Renewal Self V", 200, 1800),
            S(217, "Mana Renewal Self VI", 250, 2700),
            S(2183, "Battlemage's Blessing", 300, 3600),
            S(4494, "Incantation of Mana Renewal Self", 400, 5400)),
        L("Impenetrability", "Impenetrability", MagicSchool.ItemEnchantment, ProfileRole.ItemBuffArmor,
            S(51, "Impenetrability I", 1, 1800),
            S(1482, "Impenetrability II", 50, 1800),
            S(1483, "Impenetrability III", 100, 1800),
            S(1484, "Impenetrability IV", 150, 1800),
            S(1485, "Impenetrability V", 200, 1800),
            S(1486, "Impenetrability VI", 250, 2700),
            S(2108, "Brogard's Defiance", 300, 3600),
            S(4407, "Incantation of Impenetrability", 400, 5400)),
        L("Bludgeon Bane", "Bludgeon Bane", MagicSchool.ItemEnchantment, ProfileRole.ItemBuffArmor,
            S(1511, "Bludgeon Bane I", 1, 1800),
            S(1512, "Bludgeon Bane II", 50, 1800),
            S(1513, "Bludgeon Bane III", 100, 1800),
            S(1514, "Bludgeon Bane IV", 150, 1800),
            S(1515, "Bludgeon Bane V", 200, 1800),
            S(1516, "Bludgeon Bane VI", 250, 2700),
            S(2098, "Tusker's Bane", 300, 3600),
            S(4397, "Incantation of Bludgeon Bane", 400, 5400)),
        L("Hermetic Link", "Hermetic Link", MagicSchool.ItemEnchantment, ProfileRole.ItemBuffWand,
            S(1475, "Aura of Hermetic Link Self I", 25, 1800),
            S(1476, "Aura of Hermetic Link Self II", 50, 1800),
            S(1477, "Aura of Hermetic Link Self III", 100, 1800),
            S(1478, "Aura of Hermetic Link Self IV", 150, 1800),
            S(1479, "Aura of Hermetic Link Self V", 200, 1800),
            S(1480, "Aura of Hermetic Link Self VI", 250, 2700),
            S(2117, "Aura of Mystic's Blessing", 300, 3600),
            S(4418, "Aura of Incantation of Hermetic Link Self", 400, 5400)),
        L("Defender", "Defender", MagicSchool.ItemEnchantment, ProfileRole.ItemBuffWand,
            S(1599, "Aura of Defender Self I", 1, 1800),
            S(1601, "Aura of Defender Self II", 50, 1800),
            S(1602, "Aura of Defender Self III", 100, 1800),
            S(1603, "Aura of Defender Self IV", 150, 1800),
            S(1604, "Aura of Defender Self V", 200, 1800),
            S(1605, "Aura of Defender Self VI", 250, 2700),
            S(2101, "Aura of Cragstone's Will", 300, 3600),
            S(4400, "Aura of Incantation of Defender Self", 350, 5400)),
        L("Stamina to Mana Self", "Stamina to Mana Self", MagicSchool.LifeMagic, ProfileRole.ManaTransfer,
            S(1676, "Stamina to Mana Self I", 1, 0),
            S(1677, "Stamina to Mana Self II", 50, 0),
            S(1678, "Stamina to Mana Self III", 100, 0),
            S(1679, "Stamina to Mana Self IV", 150, 0),
            S(1680, "Stamina to Mana Self V", 200, 0),
            S(1681, "Stamina to Mana Self VI", 250, 0),
            S(2345, "Meditative Trance", 300, 0),
            S(4981, "Incantation of Stamina to Mana Self", 400, 0)),
        L("Revitalize Self", "Revitalize Self", MagicSchool.LifeMagic, ProfileRole.StaminaRestore,
            S(1177, "Revitalize Self I", 1, 0),
            S(1178, "Revitalize Self II", 50, 0),
            S(1179, "Revitalize Self III", 100, 0),
            S(1180, "Revitalize Self IV", 150, 0),
            S(1181, "Revitalize Self V", 200, 0),
            S(1182, "Revitalize Self VI", 250, 0),
            S(2083, "Robustification", 300, 0),
            S(4321, "Incantation of Revitalize Self", 400, 0)),
        L("Flame Bolt", "Flame Bolt", MagicSchool.WarMagic, ProfileRole.WarBolt,
            S(27, "Flame Bolt I", 1, 0),
            S(81, "Flame Bolt II", 50, 0),
            S(82, "Flame Bolt III", 100, 0),
            S(83, "Flame Bolt IV", 150, 0),
            S(84, "Flame Bolt V", 200, 0),
            S(85, "Flame Bolt VI", 250, 0),
            S(2128, "Ilservian's Flame", 300, 0),
            S(4439, "Incantation of Flame Bolt", 400, 0)),
        L("Flame Streak", "Flame Streak", MagicSchool.WarMagic, ProfileRole.WarOption,
            S(1796, "Flame Streak I", 1, 0),
            S(1797, "Flame Streak II", 50, 0),
            S(1798, "Flame Streak III", 100, 0),
            S(1799, "Flame Streak IV", 150, 0),
            S(1800, "Flame Streak V", 200, 0),
            S(1801, "Flame Streak VI", 250, 0),
            S(2129, "Sizzling Fury", 300, 0),
            S(4440, "Incantation of Flame Streak", 400, 0)),
        L("Flame Volley", "Flame Volley", MagicSchool.WarMagic, ProfileRole.WarOption,
            S(3663, "Flame Volley III", 125, 0),
            S(3664, "Flame Volley IV", 175, 0),
            S(143, "Flame Volley III", 125, 0),
            S(144, "Flame Volley IV", 175, 0),
            S(145, "Flame Volley V", 225, 0),
            S(146, "Flame Volley VI", 275, 0),
            S(2130, "Infernae", 325, 0),
            S(4441, "Incantation of Flame Volley", 400, 0)),
        L("Flame Blast", "Flame Blast", MagicSchool.WarMagic, ProfileRole.WarOption,
            S(3662, "Flame Blast I", 75, 0),
            S(115, "Flame Blast III", 125, 0),
            S(116, "Flame Blast IV", 175, 0),
            S(117, "Flame Blast V", 225, 0),
            S(118, "Flame Blast VI", 275, 0),
            S(2127, "Silencia's Scorn", 325, 0),
            S(4438, "Incantation of Flame Blast", 400, 0)),
        L("Flame Arc", "Flame Arc", MagicSchool.WarMagic, ProfileRole.WarOption,
            S(2739, "Flame Arc I", 1, 0),
            S(2740, "Flame Arc II", 50, 0),
            S(2741, "Flame Arc III", 100, 0),
            S(2742, "Flame Arc IV", 150, 0),
            S(2743, "Flame Arc V", 200, 0),
            S(2744, "Flame Arc VI", 250, 0),
            S(2745, "Flame Arc VII", 300, 0),
            S(4423, "Incantation of Flame Arc", 400, 0)),    ];

    /// <summary>
    /// The mage's lines that are no buff and no fight line (M4 P5a, owner decisions D33 and D36): Harm Self I-VIII (Life Magic, the heal test's way of getting hurt; the mage's Life Magic casts the whole line at the 0.9 chance rule), and the three
    /// travel spells Lifestone Recall (1635), Primary Portal Tie (47) and Primary Portal Recall (48), Item Enchantment, no duration, and (M4 P5b J3b, W0) Lifestone Tie (2644): the only spell that sets ACE's LinkedLifestone, which Lifestone Recall reads (WorldObject_Magic.cs:966-976 and :1087-1093 at 95ef426d). Ids, names, powers and schools are the SpellTable's (<c>ArenaProfileTests</c> pins them). They are not in <see cref="Lines"/>
    /// (the buff cycle, the gear loops and the spell bars walk those); the build learns them through <see cref="FightExtras"/>.
    /// </summary>
    public static IReadOnlyList<ProfileLine> Extras { get; } =
    [
        L("Harm Self", "Harm Self", MagicSchool.LifeMagic, ProfileRole.HarmSelf,
            S(8, "Harm Self I", 15, 0),
            S(1167, "Harm Self II", 75, 0),
            S(1168, "Harm Self III", 125, 0),
            S(1169, "Harm Self IV", 175, 0),
            S(1170, "Harm Self V", 225, 0),
            S(1171, "Harm Self VI", 275, 0),
            S(2071, "Harm Self VII", 325, 0),
            S(4309, "Incantation of Harm Self", 400, 0)),
        L("Lifestone Recall", "Lifestone Recall", MagicSchool.ItemEnchantment, ProfileRole.Travel,
            S(1635, "Lifestone Recall", 100, 0)),
        L("Primary Portal Tie", "Primary Portal Tie", MagicSchool.ItemEnchantment, ProfileRole.Travel,
            S(47, "Primary Portal Tie", 150, 0)),
        L("Primary Portal Recall", "Primary Portal Recall", MagicSchool.ItemEnchantment, ProfileRole.Travel,
            S(48, "Primary Portal Recall", 100, 0)),
        L("Lifestone Tie", "Lifestone Tie", MagicSchool.ItemEnchantment, ProfileRole.Travel,
            S(2644, "Lifestone Tie", 100, 0)),
        L("Cassius' Ring of Fire", "Cassius' Ring of Fire", MagicSchool.WarMagic, ProfileRole.WarOption,
            S(RingOfFireId, "Cassius' Ring of Fire", 200, 0)),
    ];

    /// <summary>
    /// Cassius' Ring of Fire (M4 P5b J7, the hero video, owner decision D32): a War Magic projectile ring of power 200 (the SpellTable: nine projectiles around the caster, components 110 x2, 19, 67, 34, 46, 63, 58 before the account's taper
    /// randomisation). The build learns it with the other extras; <see cref="GearKit.KitFightSpells"/> adds its components to the kit. It is no line of the buff cycle and no war loop's choice: only the hero journey casts it.
    /// </summary>
    public const uint RingOfFireId = 1785;

    /// <summary>
    /// Fire Vulnerability Other VII ("Inferno's Gift") and Heal Self VII ("Adja's Intervention"): the fight tab's spells that are no profile line (M3 4.2 p1); then, from M4 P5a, every spell of <see cref="Extras"/> (Harm Self I-VII, Lifestone Recall,
    /// Primary Portal Tie, Primary Portal Recall, Lifestone Tie); the build learns them after the lines.
    /// </summary>
    public static IReadOnlyList<uint> FightExtras { get; } = [2170, 2073, .. Extras.SelectMany(l => l.Spells).Select(s => s.Id)];

    /// <summary>The extra line named <paramref name="name"/> ("Harm Self", "Lifestone Recall", "Primary Portal Tie", "Primary Portal Recall", "Lifestone Tie"), or null.</summary>
    public static ProfileLine? ExtraNamed(string name) => Extras.FirstOrDefault(l => l.Name == name);

    /// <summary>The mage's buff profile (<see cref="IBuffProfile"/>): the lines, the cycle's casting order (Focus, Willpower and Creature Enchantment Mastery first) and the spells the build learns.</summary>
    public static IBuffProfile Buffs { get; } = new BuffProfile(MageTemplate.CharacterName, Lines, ["Focus Self", "Willpower Self", "Creature Enchantment Mastery Self"], FightExtras);

    /// <summary>Every spell id of every line: what the mage learns (all levels, D10), in line order then list order.</summary>
    public static IReadOnlyList<uint> SpellsToLearn { get; } = [.. Lines.SelectMany(l => l.Spells).Select(s => s.Id), .. FightExtras];

    /// <summary>The line named <paramref name="name"/> (the spec's name), or null.</summary>
    public static ProfileLine? LineNamed(string name) => LinesByName.GetValueOrDefault(name);

    /// <summary>The lines of one role, in order.</summary>
    public static IEnumerable<ProfileLine> LinesOf(ProfileRole role) => Lines.Where(l => l.Role == role);

    /// <summary>The spell with id <paramref name="id"/>, or null when the profile does not hold it.</summary>
    public static ProfileSpell? Spell(uint id) => SpellsById.GetValueOrDefault(id);

    private static readonly FrozenDictionary<string, ProfileLine> LinesByName = Lines.ToFrozenDictionary(l => l.Name);

    private static readonly FrozenDictionary<uint, ProfileSpell> SpellsById = Lines.SelectMany(l => l.Spells).ToFrozenDictionary(s => s.Id);

    private static ProfileLine L(string name, string datRoot, MagicSchool school, ProfileRole role, params ProfileSpell[] spells) =>
        new(name, datRoot, school, role, [.. spells.Select((s, i) => s with { Position = i + 1 })]);

    private static ProfileSpell S(uint id, string name, uint power, double duration) => new(0, id, name, power, duration);
}

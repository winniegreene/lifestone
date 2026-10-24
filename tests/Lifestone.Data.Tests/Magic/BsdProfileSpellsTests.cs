using DatReaderWriter.Enums;
using Lifestone.Data.Magic;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Magic;

/// <summary>
/// The spells of the BSD mage's profile (docs/milestones/m3-lights-and-sound.md section 5.2): every id of every line,
/// levels I-VI, then VII, then VIII, pinned by the name, <c>Power</c> and <c>Duration</c> the SpellTable (0x0E00000E)
/// gives. The ids are the spec's (ACE's SpellId enum); the names, powers and durations are what the dat holds, so a
/// wrong id fails the "belongs to the named line" check rather than being fixed silently.
/// </summary>
/// <remarks>
/// Differences between the spec's line names and the dat's, pinned here (<see cref="Line.Root"/> is the dat's):
/// "Mana Mastery Self" is "Mana Conversion Mastery Self", "Bludgeon Protection Self" is "Bludgeoning Protection Self",
/// and Hermetic Link and Defender are "Aura of ... Self". Level VII always has its own title (for example "Might of
/// the Lugians"), except Flame Arc, whose VII is "Flame Arc VII". Flame Volley's first two ids (3663, 3664) are named
/// "Flame Volley III" and "IV" in the dat, the same names and Powers as 143 and 144, so the spec's Volley list falls from
/// 175 back to 125 at 143. Flame Blast has no level II (3662 is "Flame Blast I" with Power 75, then 115 is "III"), so
/// those two lines hold 8 and 7 spells whose level numerals do not run I-VI (TODO(m3-volley-blast-levels), reported to
/// the lead).
/// </remarks>
[Trait("Tier", "Exhaustive")]
public sealed class BsdProfileSpellsTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>One spell line: the spec's name, the dat's name root, school and type, and each spell's pinned id, name, power and duration (seconds).</summary>
    private sealed record Line(string Name, string Root, MagicSchool School, SpellType Type, uint[] Ids, string[] Names, uint[] Power, double[] Duration);

    private static readonly Line[] Lines =
    [
        new("Strength Self", "Strength Self", MagicSchool.CreatureEnchantment, SpellType.Enchantment,
            [2, 1328, 1329, 1330, 1331, 1332, 2087, 4325],
            ["Strength Self I", "Strength Self II", "Strength Self III", "Strength Self IV", "Strength Self V", "Strength Self VI", "Might of the Lugians", "Incantation of Strength Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Endurance Self", "Endurance Self", MagicSchool.CreatureEnchantment, SpellType.Enchantment,
            [1349, 1350, 1351, 1352, 1353, 1354, 2061, 4299],
            ["Endurance Self I", "Endurance Self II", "Endurance Self III", "Endurance Self IV", "Endurance Self V", "Endurance Self VI", "Perseverance", "Incantation of Endurance Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Coordination Self", "Coordination Self", MagicSchool.CreatureEnchantment, SpellType.Enchantment,
            [1373, 1374, 1375, 1376, 1377, 1378, 2059, 4297],
            ["Coordination Self I", "Coordination Self II", "Coordination Self III", "Coordination Self IV", "Coordination Self V", "Coordination Self VI", "Honed Control", "Incantation of Coordination Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Quickness Self", "Quickness Self", MagicSchool.CreatureEnchantment, SpellType.Enchantment,
            [1397, 1398, 1399, 1400, 1401, 1402, 2081, 4319],
            ["Quickness Self I", "Quickness Self II", "Quickness Self III", "Quickness Self IV", "Quickness Self V", "Quickness Self VI", "Hastening", "Incantation of Quickness Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Focus Self", "Focus Self", MagicSchool.CreatureEnchantment, SpellType.Enchantment,
            [1421, 1422, 1423, 1424, 1425, 1426, 2067, 4305],
            ["Focus Self I", "Focus Self II", "Focus Self III", "Focus Self IV", "Focus Self V", "Focus Self VI", "Inner Calm", "Incantation of Focus Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Willpower Self", "Willpower Self", MagicSchool.CreatureEnchantment, SpellType.Enchantment,
            [1445, 1446, 1447, 1448, 1449, 1450, 2091, 4329],
            ["Willpower Self I", "Willpower Self II", "Willpower Self III", "Willpower Self IV", "Willpower Self V", "Willpower Self VI", "Mind Blossom", "Incantation of Willpower Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("War Magic Mastery Self", "War Magic Mastery Self", MagicSchool.CreatureEnchantment, SpellType.Enchantment,
            [629, 630, 631, 632, 633, 634, 2323, 4638],
            ["War Magic Mastery Self I", "War Magic Mastery Self II", "War Magic Mastery Self III", "War Magic Mastery Self IV", "War Magic Mastery Self V", "War Magic Mastery Self VI", "Hieromancer's Blessing", "Incantation of War Magic Mastery Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Creature Enchantment Mastery Self", "Creature Enchantment Mastery Self", MagicSchool.CreatureEnchantment, SpellType.Enchantment,
            [557, 558, 559, 560, 561, 562, 2215, 4530],
            ["Creature Enchantment Mastery Self I", "Creature Enchantment Mastery Self II", "Creature Enchantment Mastery Self III", "Creature Enchantment Mastery Self IV", "Creature Enchantment Mastery Self V", "Creature Enchantment Mastery Self VI", "Adja's Blessing", "Incantation of Creature Enchantment Mastery Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Item Enchantment Mastery Self", "Item Enchantment Mastery Self", MagicSchool.CreatureEnchantment, SpellType.Enchantment,
            [581, 582, 583, 584, 585, 586, 2249, 4564],
            ["Item Enchantment Mastery Self I", "Item Enchantment Mastery Self II", "Item Enchantment Mastery Self III", "Item Enchantment Mastery Self IV", "Item Enchantment Mastery Self V", "Item Enchantment Mastery Self VI", "Celcynd's Blessing", "Incantation of Item Enchantment Mastery Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Life Magic Mastery Self", "Life Magic Mastery Self", MagicSchool.CreatureEnchantment, SpellType.Enchantment,
            [605, 606, 607, 608, 609, 610, 2267, 4582],
            ["Life Magic Mastery Self I", "Life Magic Mastery Self II", "Life Magic Mastery Self III", "Life Magic Mastery Self IV", "Life Magic Mastery Self V", "Life Magic Mastery Self VI", "Harlune's Blessing", "Incantation of Life Magic Mastery Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Mana Mastery Self", "Mana Conversion Mastery Self", MagicSchool.CreatureEnchantment, SpellType.Enchantment,
            [653, 654, 655, 656, 657, 658, 2287, 4602],
            ["Mana Conversion Mastery Self I", "Mana Conversion Mastery Self II", "Mana Conversion Mastery Self III", "Mana Conversion Mastery Self IV", "Mana Conversion Mastery Self V", "Mana Conversion Mastery Self VI", "Nuhmudira's Blessing", "Incantation of Mana Conversion Mastery Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Arcane Enlightenment Self", "Arcane Enlightenment Self", MagicSchool.CreatureEnchantment, SpellType.Enchantment,
            [678, 679, 680, 681, 682, 683, 2195, 4510],
            ["Arcane Enlightenment Self I", "Arcane Enlightenment Self II", "Arcane Enlightenment Self III", "Arcane Enlightenment Self IV", "Arcane Enlightenment Self V", "Arcane Enlightenment Self VI", "Aliester's Blessing", "Incantation of Arcane Enlightenment Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Magic Resistance Self", "Magic Resistance Self", MagicSchool.CreatureEnchantment, SpellType.Enchantment,
            [274, 275, 276, 277, 278, 279, 2281, 4596],
            ["Magic Resistance Self I", "Magic Resistance Self II", "Magic Resistance Self III", "Magic Resistance Self IV", "Magic Resistance Self V", "Magic Resistance Self VI", "Aura of Resistance", "Incantation of Magic Resistance Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Invulnerability Self", "Invulnerability Self", MagicSchool.CreatureEnchantment, SpellType.Enchantment,
            [18, 245, 246, 247, 248, 249, 2245, 4560],
            ["Invulnerability Self I", "Invulnerability Self II", "Invulnerability Self III", "Invulnerability Self IV", "Invulnerability Self V", "Invulnerability Self VI", "Aura of Defense", "Incantation of Invulnerability Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Impregnability Self", "Impregnability Self", MagicSchool.CreatureEnchantment, SpellType.Enchantment,
            [256, 257, 258, 259, 260, 261, 2243, 4558],
            ["Impregnability Self I", "Impregnability Self II", "Impregnability Self III", "Impregnability Self IV", "Impregnability Self V", "Impregnability Self VI", "Aura of Deflection", "Incantation of Impregnability Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Armor Self", "Armor Self", MagicSchool.LifeMagic, SpellType.Enchantment,
            [24, 1308, 1309, 1310, 1311, 1312, 2053, 4291],
            ["Armor Self I", "Armor Self II", "Armor Self III", "Armor Self IV", "Armor Self V", "Armor Self VI", "Executor's Blessing", "Incantation of Armor Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Bludgeon Protection Self", "Bludgeoning Protection Self", MagicSchool.LifeMagic, SpellType.Enchantment,
            [1018, 1019, 1020, 1021, 1022, 1023, 2153, 4464],
            ["Bludgeoning Protection Self I", "Bludgeoning Protection Self II", "Bludgeoning Protection Self III", "Bludgeoning Protection Self IV", "Bludgeoning Protection Self V", "Bludgeoning Protection Self VI", "Blessing of the Mace Turner", "Incantation of Bludgeoning Protection Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Regeneration Self", "Regeneration Self", MagicSchool.LifeMagic, SpellType.Enchantment,
            [165, 166, 167, 168, 169, 170, 2185, 4496],
            ["Regeneration Self I", "Regeneration Self II", "Regeneration Self III", "Regeneration Self IV", "Regeneration Self V", "Regeneration Self VI", "Robustify", "Incantation of Regeneration Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Rejuvenation Self", "Rejuvenation Self", MagicSchool.LifeMagic, SpellType.Enchantment,
            [54, 189, 190, 191, 192, 193, 2187, 4498],
            ["Rejuvenation Self I", "Rejuvenation Self II", "Rejuvenation Self III", "Rejuvenation Self IV", "Rejuvenation Self V", "Rejuvenation Self VI", "Unflinching Persistence", "Incantation of Rejuvenation Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Mana Renewal Self", "Mana Renewal Self", MagicSchool.LifeMagic, SpellType.Enchantment,
            [212, 213, 214, 215, 216, 217, 2183, 4494],
            ["Mana Renewal Self I", "Mana Renewal Self II", "Mana Renewal Self III", "Mana Renewal Self IV", "Mana Renewal Self V", "Mana Renewal Self VI", "Battlemage's Blessing", "Incantation of Mana Renewal Self"],
            [25, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Impenetrability", "Impenetrability", MagicSchool.ItemEnchantment, SpellType.Enchantment,
            [51, 1482, 1483, 1484, 1485, 1486, 2108, 4407],
            ["Impenetrability I", "Impenetrability II", "Impenetrability III", "Impenetrability IV", "Impenetrability V", "Impenetrability VI", "Brogard's Defiance", "Incantation of Impenetrability"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Bludgeon Bane", "Bludgeon Bane", MagicSchool.ItemEnchantment, SpellType.Enchantment,
            [1511, 1512, 1513, 1514, 1515, 1516, 2098, 4397],
            ["Bludgeon Bane I", "Bludgeon Bane II", "Bludgeon Bane III", "Bludgeon Bane IV", "Bludgeon Bane V", "Bludgeon Bane VI", "Tusker's Bane", "Incantation of Bludgeon Bane"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Hermetic Link", "Hermetic Link", MagicSchool.ItemEnchantment, SpellType.Enchantment,
            [1475, 1476, 1477, 1478, 1479, 1480, 2117, 4418],
            ["Aura of Hermetic Link Self I", "Aura of Hermetic Link Self II", "Aura of Hermetic Link Self III", "Aura of Hermetic Link Self IV", "Aura of Hermetic Link Self V", "Aura of Hermetic Link Self VI", "Aura of Mystic's Blessing", "Aura of Incantation of Hermetic Link Self"],
            [25, 50, 100, 150, 200, 250, 300, 400],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Defender", "Defender", MagicSchool.ItemEnchantment, SpellType.Enchantment,
            [1599, 1601, 1602, 1603, 1604, 1605, 2101, 4400],
            ["Aura of Defender Self I", "Aura of Defender Self II", "Aura of Defender Self III", "Aura of Defender Self IV", "Aura of Defender Self V", "Aura of Defender Self VI", "Aura of Cragstone's Will", "Aura of Incantation of Defender Self"],
            [1, 50, 100, 150, 200, 250, 300, 350],
            [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400]),
        new("Stamina to Mana Self", "Stamina to Mana Self", MagicSchool.LifeMagic, SpellType.Transfer,
            [1676, 1677, 1678, 1679, 1680, 1681, 2345, 4981],
            ["Stamina to Mana Self I", "Stamina to Mana Self II", "Stamina to Mana Self III", "Stamina to Mana Self IV", "Stamina to Mana Self V", "Stamina to Mana Self VI", "Meditative Trance", "Incantation of Stamina to Mana Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [0, 0, 0, 0, 0, 0, 0, 0]),
        new("Revitalize Self", "Revitalize Self", MagicSchool.LifeMagic, SpellType.Boost,
            [1177, 1178, 1179, 1180, 1181, 1182, 2083, 4321],
            ["Revitalize Self I", "Revitalize Self II", "Revitalize Self III", "Revitalize Self IV", "Revitalize Self V", "Revitalize Self VI", "Robustification", "Incantation of Revitalize Self"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [0, 0, 0, 0, 0, 0, 0, 0]),
        new("Flame Bolt", "Flame Bolt", MagicSchool.WarMagic, SpellType.Projectile,
            [27, 81, 82, 83, 84, 85, 2128, 4439],
            ["Flame Bolt I", "Flame Bolt II", "Flame Bolt III", "Flame Bolt IV", "Flame Bolt V", "Flame Bolt VI", "Ilservian's Flame", "Incantation of Flame Bolt"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [0, 0, 0, 0, 0, 0, 0, 0]),
        new("Flame Streak", "Flame Streak", MagicSchool.WarMagic, SpellType.Projectile,
            [1796, 1797, 1798, 1799, 1800, 1801, 2129, 4440],
            ["Flame Streak I", "Flame Streak II", "Flame Streak III", "Flame Streak IV", "Flame Streak V", "Flame Streak VI", "Sizzling Fury", "Incantation of Flame Streak"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [0, 0, 0, 0, 0, 0, 0, 0]),
        new("Flame Volley", "Flame Volley", MagicSchool.WarMagic, SpellType.Projectile,
            [3663, 3664, 143, 144, 145, 146, 2130, 4441],
            ["Flame Volley III", "Flame Volley IV", "Flame Volley III", "Flame Volley IV", "Flame Volley V", "Flame Volley VI", "Infernae", "Incantation of Flame Volley"],
            [125, 175, 125, 175, 225, 275, 325, 400],
            [0, 0, 0, 0, 0, 0, 0, 0]),
        new("Flame Blast", "Flame Blast", MagicSchool.WarMagic, SpellType.Projectile,
            [3662, 115, 116, 117, 118, 2127, 4438],
            ["Flame Blast I", "Flame Blast III", "Flame Blast IV", "Flame Blast V", "Flame Blast VI", "Silencia's Scorn", "Incantation of Flame Blast"],
            [75, 125, 175, 225, 275, 325, 400],
            [0, 0, 0, 0, 0, 0, 0]),
        new("Flame Arc", "Flame Arc", MagicSchool.WarMagic, SpellType.Projectile,
            [2739, 2740, 2741, 2742, 2743, 2744, 2745, 4423],
            ["Flame Arc I", "Flame Arc II", "Flame Arc III", "Flame Arc IV", "Flame Arc V", "Flame Arc VI", "Flame Arc VII", "Incantation of Flame Arc"],
            [1, 50, 100, 150, 200, 250, 300, 400],
            [0, 0, 0, 0, 0, 0, 0, 0]),
    ];

    [RequiresDatsFact]
    public void Every_spec_id_has_the_name_power_and_duration_the_dat_pins()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var line in Lines)
        {
            Assert.Equal(line.Ids.Length, line.Names.Length);
            Assert.Equal(line.Ids.Length, line.Power.Length);
            Assert.Equal(line.Ids.Length, line.Duration.Length);
            for (var i = 0; i < line.Ids.Length; i++)
            {
                Assert.True(spells.TryGetValue(line.Ids[i], out var spell), $"{line.Name}: id {line.Ids[i]} is not in the SpellTable");
                Assert.True(spell.Name == line.Names[i], $"{line.Name}: id {spell.Id} is '{spell.Name}', pinned '{line.Names[i]}'");
                Assert.True(spell.Power == line.Power[i], $"{spell.Name} ({spell.Id}): Power {spell.Power}, pinned {line.Power[i]}");
                Assert.True(spell.Duration == line.Duration[i], $"{spell.Name} ({spell.Id}): Duration {spell.Duration}, pinned {line.Duration[i]}");
                Assert.True(spell.School == line.School, $"{spell.Name} ({spell.Id}): school {spell.School}, line {line.School}");
                Assert.True(spell.MetaSpellType == line.Type, $"{spell.Name} ({spell.Id}): type {spell.MetaSpellType}, line {line.Type}");
            }
        }

        // 31 lines: 30 of 8 spells and Flame Blast of 7 (no level II in the dat); 24 buffs, the two mana lines (M3 2.1 s0a, D24) and 5 war lines.
        Assert.Equal(31, Lines.Length);
        Assert.Equal(247, Lines.Sum(l => l.Ids.Length));
        Assert.Equal(7, Lines.Single(l => l.Name == "Flame Blast").Ids.Length);
        Assert.Equal(247, Lines.SelectMany(l => l.Ids).Distinct().Count());
    }

    [RequiresDatsFact]
    public void Each_id_belongs_to_the_line_the_spec_names()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var line in Lines)
        {
            // Every spell carries the line's dat root in its name, except level VII, which has its own title (second to
            // last in each line; Flame Arc's VII is "Flame Arc VII", and Flame Blast's title is also second to last).
            var withoutRoot = Enumerable.Range(0, line.Ids.Length).Where(i => !spells[line.Ids[i]].Name.Contains(line.Root, StringComparison.Ordinal)).ToArray();
            var expected = line.Name == "Flame Arc" ? Array.Empty<int>() : [line.Ids.Length - 2];
            Assert.True(expected.SequenceEqual(withoutRoot), $"{line.Name}: spells without the root '{line.Root}' at {string.Join(",", withoutRoot)}");

            // Within a line the power never falls from one level to the next and the duration only grows; the one exception
            // is Flame Volley, whose spec ids 3663 and 3664 repeat the dat's own III and IV (143 and 144) before them (see the remarks).
            for (var i = 1; i < line.Ids.Length; i++)
            {
                Assert.True(line.Power[i] >= line.Power[i - 1] || (line.Name == "Flame Volley" && i == 2), $"{line.Name}: power falls at index {i}");
                Assert.True(line.Duration[i] >= line.Duration[i - 1], $"{line.Name}: duration falls at index {i}");
            }
        }
    }

    [RequiresDatsFact]
    public void Print_the_compact_table_for_the_handoff()
    {
        foreach (var line in Lines)
        {
            output.WriteLine($"{line.Name}: " + string.Join(" ", line.Ids.Select((id, i) => $"{id}:{line.Power[i]}/{line.Duration[i]}")));
        }

        // The shortest-lived buff is the self lines' levels I-V (1800 s); the projectiles and the transfer and boost spells have no duration.
        Assert.Equal(1800, Lines.Where(l => l.Type == SpellType.Enchantment).Min(l => l.Duration.Min()));
        Assert.All(Lines.Where(l => l.Type != SpellType.Enchantment), l => Assert.All(l.Duration, d => Assert.Equal(0, d)));
    }
}

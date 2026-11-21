using System.Collections.Frozen;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// One spell that changes a piece of armour: its armour level (ACE PropertyInt 28) or one of its protections (PropertyFloat 13 ArmorModVsSlash to 19 ArmorModVsElectric).
/// </summary>
/// <param name="Id">The spell id.</param>
/// <param name="Key">The stat: 28 the armour level, 13 to 19 the protection against slash, pierce, bludgeon, cold, fire, acid, electric.</param>
/// <param name="Value">What it adds (a lure or Brittlemail: negative).</param>
/// <param name="Category">The spell's stacking family (ACE <c>SpellCategory</c>): of one family on a piece only the top layer counts.</param>
public readonly record struct ArmourSpellRow(uint Id, uint Key, double Value, uint Category);

/// <summary>
/// Every spell of this server's world that changes a piece's armour level or a protection: the rows <see cref="ArmourBase"/> reads an appraisal's enchantments by.
/// <para>Source, the id, key and value: a read-only SELECT of ace_world on this server, 2026-11-19: <c>SELECT id, name, stat_Mod_Type, stat_Mod_Key, stat_Mod_Val FROM spell WHERE (stat_Mod_Key = 28 AND (stat_Mod_Type &amp; 4) &lt;&gt; 0 AND
/// (stat_Mod_Type &amp; 16) = 0) OR (stat_Mod_Key IN (13,14,15,16,17,18,19,165) AND (stat_Mod_Type &amp; 8) &lt;&gt; 0)</c>: 192 rows, 35 on the armour level (type 36868 = 0x9004), 157 on a protection (type 36872 = 0x9008), none on
/// key 165 (nether). The category: the retail dats' spell table (<c>Lifestone.Data.Magic.SpellTableData</c>, <c>SpellInfo.Category</c>), read for the same ids the same day; <c>ArmourBaseDatTests</c> reads it again whenever
/// the dats are present.</para>
/// </summary>
public static class ArmourSpellRows
{
    /// <summary>The armour level's key (PropertyInt 28 ArmorLevel).</summary>
    public const uint ArmorLevelKey = 28;

    private static readonly ArmourSpellRow[] All =
    [
        new(37, 13, 0.111, 174), // Blade Bane I
        new(1558, 13, 0.25, 174), // Blade Bane II
        new(1559, 13, 0.5, 174), // Blade Bane III
        new(1560, 13, 0.75, 174), // Blade Bane IV
        new(1561, 13, 1, 174), // Blade Bane V
        new(1562, 13, 1.5, 174), // Blade Bane VI
        new(2094, 13, 1.7, 174), // Swordsman's Bane
        new(4393, 13, 2, 174), // Incantation of Blade Bane
        new(4394, 13, -2, 175), // Incantation of Blade Lure
        new(2095, 13, -1.7, 175), // Swordsman Bait
        new(1557, 13, -1.5, 175), // Blade Lure VI
        new(1556, 13, -1, 175), // Blade Lure V
        new(1555, 13, -0.75, 175), // Blade Lure IV
        new(1554, 13, -0.5, 175), // Blade Lure III
        new(1553, 13, -0.25, 175), // Blade Lure II
        new(38, 13, -0.111, 175), // Blade Lure I
        new(2355, 13, 0.25, 178), // Lesser Slashing Durance
        new(2962, 13, 1, 178), // Greater Slashing Durance
        new(2606, 13, 0.1, 395), // Minor Slashing Bane
        new(2594, 13, 0.15, 395), // Major Slashing Bane
        new(4669, 13, 0.2, 395), // Epic Slashing Bane
        new(6097, 13, 0.25, 395), // Legendary Slashing Bane
        new(3686, 13, 5, 503), // Prodigal Blade Bane
        new(1569, 14, 0.111, 172), // Piercing Bane I
        new(1570, 14, 0.25, 172), // Piercing Bane II
        new(1571, 14, 0.5, 172), // Piercing Bane III
        new(1572, 14, 0.75, 172), // Piercing Bane IV
        new(1573, 14, 1, 172), // Piercing Bane V
        new(1574, 14, 1.5, 172), // Piercing Bane VI
        new(2113, 14, 1.7, 172), // Archer's Bane
        new(4412, 14, 2, 172), // Incantation of Piercing Bane
        new(4413, 14, -2, 173), // Incantation of Piercing Lure
        new(2114, 14, -1.7, 173), // Archer Bait
        new(1568, 14, -1.5, 173), // Piercing Lure VI
        new(1567, 14, -1, 173), // Piercing Lure V
        new(1566, 14, -0.75, 173), // Piercing Lure IV
        new(1565, 14, -0.5, 173), // Piercing Lure III
        new(1564, 14, -0.25, 173), // Piercing Lure II
        new(1563, 14, -0.111, 173), // Piercing Lure I
        new(2354, 14, 0.25, 180), // Lesser Piercing Durance
        new(2961, 14, 1, 180), // Greater Piercing Durance
        new(2605, 14, 0.1, 393), // Minor Piercing Bane
        new(2593, 14, 0.15, 393), // Major Piercing Bane
        new(4668, 14, 0.2, 393), // Epic Piercing Bane
        new(6096, 14, 0.25, 393), // Legendary Piercing Bane
        new(3728, 14, 5, 498), // Prodigal Piercing Bane
        new(1511, 15, 0.111, 164), // Bludgeon Bane I
        new(1512, 15, 0.25, 164), // Bludgeon Bane II
        new(1513, 15, 0.5, 164), // Bludgeon Bane III
        new(1514, 15, 0.75, 164), // Bludgeon Bane IV
        new(1515, 15, 1, 164), // Bludgeon Bane V
        new(1516, 15, 1.5, 164), // Bludgeon Bane VI
        new(2098, 15, 1.7, 164), // Tusker's Bane
        new(4397, 15, 2, 164), // Incantation of Bludgeon Bane
        new(4398, 15, -2, 165), // Incantation of Bludgeon Lure
        new(2099, 15, -1.7, 165), // Tusker Bait
        new(1510, 15, -1.5, 165), // Bludgeon Lure VI
        new(1509, 15, -1, 165), // Bludgeon Lure V
        new(1508, 15, -0.75, 165), // Bludgeon Lure IV
        new(1507, 15, -0.5, 165), // Bludgeon Lure III
        new(1506, 15, -0.25, 165), // Bludgeon Lure II
        new(1505, 15, -0.111, 165), // Bludgeon Lure I
        new(2356, 15, 0.25, 176), // Lesser Bludgeoning Durance
        new(2960, 15, 1, 176), // Greater Bludgeoning Durance
        new(2599, 15, 0.1, 383), // Minor Bludgeoning Bane
        new(2587, 15, 0.15, 383), // Major Bludgeoning Bane
        new(4662, 15, 0.2, 383), // Epic Bludgeoning Bane
        new(6090, 15, 0.25, 383), // Legendary Bludgeoning Bane
        new(3689, 15, 5, 464), // Prodigal Bludgeon Bane
        new(1523, 16, 0.111, 166), // Frost Bane I
        new(1524, 16, 0.25, 166), // Frost Bane II
        new(1525, 16, 0.5, 166), // Frost Bane III
        new(1526, 16, 0.75, 166), // Frost Bane IV
        new(1527, 16, 1, 166), // Frost Bane V
        new(1528, 16, 1.5, 166), // Frost Bane VI
        new(2104, 16, 1.7, 166), // Gelidite's Bane
        new(4403, 16, 2, 166), // Incantation of Frost Bane
        new(4404, 16, -2, 167), // Incantation of Frost Lure
        new(2105, 16, -1.7, 167), // Gelidite Bait
        new(1522, 16, -1.5, 167), // Frost Lure VI
        new(1521, 16, -1, 167), // Frost Lure V
        new(1520, 16, -0.75, 167), // Frost Lure IV
        new(1519, 16, -0.5, 167), // Frost Lure III
        new(1518, 16, -0.25, 167), // Frost Lure II
        new(1517, 16, -0.111, 167), // Frost Lure I
        new(2352, 16, 1, 184), // Greater Stasis Durance
        new(2602, 16, 0.1, 387), // Minor Frost Bane
        new(2590, 16, 0.15, 387), // Major Frost Bane
        new(4665, 16, 0.2, 387), // Epic Frost Bane
        new(6093, 16, 0.25, 387), // Legendary Frost Bane
        new(3706, 16, 5, 467), // Prodigal Frost Bane
        new(1547, 17, 0.111, 170), // Flame Bane I
        new(1548, 17, 0.25, 170), // Flame Bane II
        new(1549, 17, 0.5, 170), // Flame Bane III
        new(1550, 17, 0.75, 170), // Flame Bane IV
        new(1551, 17, 1, 170), // Flame Bane V
        new(1552, 17, 1.5, 170), // Flame Bane VI
        new(2102, 17, 1.7, 170), // Inferno's Bane
        new(4401, 17, 2, 170), // Incantation of Flame Bane
        new(4402, 17, -2, 171), // Incantation of Flame Lure
        new(2103, 17, -1.7, 171), // Inferno Bait
        new(1546, 17, -1.5, 171), // Flame Lure VI
        new(1545, 17, -1, 171), // Flame Lure V
        new(1544, 17, -0.75, 171), // Flame Lure IV
        new(1543, 17, -0.5, 171), // Flame Lure III
        new(1542, 17, -0.25, 171), // Flame Lure II
        new(1541, 17, -0.111, 171), // Flame Lure I
        new(2351, 17, 1, 186), // Greater Consumption Durance
        new(2601, 17, 0.1, 385), // Minor Flame Bane
        new(2589, 17, 0.15, 385), // Major Flame Bane
        new(4664, 17, 0.2, 385), // Epic Flame Bane
        new(6092, 17, 0.25, 385), // Legendary Flame Bane
        new(3703, 17, 5, 480), // Prodigal Flame Bane
        new(1493, 18, 0.111, 162), // Acid Bane I
        new(1494, 18, 0.25, 162), // Acid Bane II
        new(1495, 18, 0.5, 162), // Acid Bane III
        new(1496, 18, 0.75, 162), // Acid Bane IV
        new(1497, 18, 1, 162), // Acid Bane V
        new(1498, 18, 1.5, 162), // Acid Bane VI
        new(2092, 18, 1.7, 162), // Olthoi's Bane
        new(4391, 18, 2, 162), // Incantation of Acid Bane
        new(4392, 18, -2, 163), // Incantation of Acid Lure
        new(2093, 18, -1.7, 163), // Olthoi Bait
        new(1504, 18, -1.5, 163), // Acid Lure VI
        new(1503, 18, -1, 163), // Acid Lure V
        new(1502, 18, -0.75, 163), // Acid Lure IV
        new(1501, 18, -0.5, 163), // Acid Lure III
        new(1500, 18, -0.25, 163), // Acid Lure II
        new(1499, 18, -0.111, 163), // Acid Lure I
        new(2350, 18, 1, 188), // Greater Decay Durance
        new(2597, 18, 0.1, 381), // Minor Acid Bane
        new(2585, 18, 0.15, 381), // Major Acid Bane
        new(4660, 18, 0.2, 381), // Epic Acid Bane
        new(6088, 18, 0.25, 381), // Legendary Acid Bane
        new(3679, 18, 5, 449), // Prodigal Acid Bane
        new(1535, 19, 0.111, 168), // Lightning Bane I
        new(1536, 19, 0.25, 168), // Lightning Bane II
        new(1537, 19, 0.5, 168), // Lightning Bane III
        new(1538, 19, 0.75, 168), // Lightning Bane IV
        new(1539, 19, 1, 168), // Lightning Bane V
        new(1540, 19, 1.5, 168), // Lightning Bane VI
        new(2110, 19, 1.7, 168), // Astyrrian's Bane
        new(4409, 19, 2, 168), // Incantation of Lightning Bane
        new(4410, 19, -2, 169), // Incantation of Lightning Lure
        new(2111, 19, -1.7, 169), // Astyrrian Bait
        new(1534, 19, -1.5, 169), // Lightning Lure VI
        new(1533, 19, -1, 169), // Lightning Lure V
        new(1532, 19, -0.75, 169), // Lightning Lure IV
        new(1531, 19, -0.5, 169), // Lightning Lure III
        new(1530, 19, -0.25, 169), // Lightning Lure II
        new(1529, 19, -0.111, 169), // Lightning Lure I
        new(2353, 19, 1, 182), // Greater Stimulation Durance
        new(2607, 19, 0.1, 397), // Minor Storm Bane
        new(2595, 19, 0.15, 397), // Major Storm Bane
        new(4671, 19, 0.2, 397), // Epic Storm Bane
        new(6099, 19, 0.25, 397), // Legendary Storm Bane
        new(3718, 19, 5, 477), // Prodigal Lightning Bane
        new(51, 28, 20, 160), // Impenetrability I
        new(1482, 28, 50, 160), // Impenetrability II
        new(1483, 28, 75, 160), // Impenetrability III
        new(1484, 28, 100, 160), // Impenetrability IV
        new(1485, 28, 150, 160), // Impenetrability V
        new(3092, 28, 180, 160), // Lesser Skin of the Fiazhat
        new(1486, 28, 200, 160), // Impenetrability VI
        new(2930, 28, 200, 160), // Tusker Skin
        new(3093, 28, 205, 160), // Minor Skin of the Fiazhat
        new(2015, 28, 220, 160), // Aerfalle's Ward
        new(2108, 28, 220, 160), // Brogard's Defiance
        new(3094, 28, 225, 160), // Skin of the Fiazhat
        new(3980, 28, 240, 160), // Impenetrability Incantation
        new(4407, 28, 240, 160), // Incantation of Impenetrability
        new(4217, 28, 300, 160), // Gourd Guard
        new(4018, 28, 350, 160), // Permafrost
        new(5009, 28, 400, 160), // Aerfalle's Greater Ward
        new(3827, 28, 500, 160), // Lunnum's Embrace
        new(4062, 28, 500, 160), // Empyrean Aegis
        new(4215, 28, 500, 160), // Shadow Armor
        new(4399, 28, -300, 161), // Incantation of Brittlemail
        new(2100, 28, -220, 161), // Tattercoat
        new(1492, 28, -200, 161), // Brittlemail VI
        new(1491, 28, -150, 161), // Brittlemail V
        new(1490, 28, -100, 161), // Brittlemail IV
        new(1489, 28, -75, 161), // Brittlemail III
        new(1488, 28, -50, 161), // Brittlemail II
        new(1487, 28, -20, 161), // Brittlemail I
        new(2349, 28, 170, 190), // Hieromancer's Ward
        new(2948, 28, 220, 190), // Hieromancer's Great Ward
        new(2604, 28, 20, 391), // Minor Impenetrability
        new(2592, 28, 40, 391), // Major Impenetrability
        new(4667, 28, 60, 391), // Epic Impenetrability
        new(6095, 28, 80, 391), // Legendary Impenetrability
        new(3710, 28, 1000, 458), // Prodigal Impenetrability
    ];

    /// <summary>The rows by spell id.</summary>
    public static FrozenDictionary<uint, ArmourSpellRow> ById { get; } = All.ToFrozenDictionary(r => r.Id);

    /// <summary>The protection's key for one DamageType bit (ACE <c>EnchantmentManager.GetImpenBaneKey</c>: 0x1 slash 13 to 0x40 electric 19), or 0 for a type no row changes.</summary>
    /// <param name="damageType">One DamageType bit.</param>
    public static uint KeyOf(uint damageType) => damageType switch { 0x1 => 13u, 0x2 => 14u, 0x4 => 15u, 0x8 => 16u, 0x10 => 17u, 0x20 => 18u, 0x40 => 19u, _ => 0u };
}

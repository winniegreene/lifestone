using System.Collections.Frozen;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 item 14 (<c>m6-banes</c>, the owner's order of 2026-11-12: before a run through the Direlands she has every armour protection on her armour, not only the tuskers' two; a creature with piercing and electric attacks
/// killed her in 50 seconds that morning): the six armour bane lines the ladder's character casts beside Impenetrability and Bludgeon Bane, which stay lines of <see cref="ArenaProfile.Lines"/>: Piercing Bane, Blade Bane,
/// Lightning Bane, Flame Bane, Frost Bane and Acid Bane, as data. Lines of her profile only (<see cref="BuffProfiles.Climber"/>, each learned by play); BSD Mage's profile, its build and its kit are unchanged.
/// </summary>
/// <remarks>
/// <para><b>The spells.</b> Ids, names, powers and durations are the SpellTable's (0x0E00000E; <c>ArmourBanesDatTests</c> pins them, with the category, the target type, the base mana and the mana mod): each line is Item
/// Enchantment, an Enchantment whose <c>NonComponentTargetType</c> is Vestements (0x6), powers 1, 50, 100, 150, 200, 250, 300, 400 and durations 1800 s (I to V), 2700 (VI), 3600 (VII), 5400 (VIII), base mana 10 to 80
/// and mana mod 2 to 14: Impenetrability's and Bludgeon Bane's own pattern.</para>
/// <para><b>What a bane does</b> (ACE @95ef426d). A piece's armour against a damage type is <c>(ArmorLevel + Impenetrability) x clamp(its protection against the type + the bane, -2, 2)</c>
/// (<c>Creature.GetArmorMod</c>, WorldObjects/Creature_BodyPart.cs:65-100); the bane adds the spell's <c>stat_Mod_Val</c> to the piece's protection against one type (the world's spell rows, read-only SELECT of
/// 2026-11-12: stat_Mod_Type 36872 = 0x9008, an additive float; stat_Mod_Key 13 slash, 14 pierce, 15 bludgeon, 16 cold, 17 fire, 18 acid, 19 electric; stat_Mod_Val 0.111, 0.25, 0.5, 0.75, 1, 1.5, 1.7, 2 at I to
/// VIII, the same for every line; Bludgeon Bane's rows are in <see cref="BuffStatMods"/>, which stays the arena profile's table). It cuts a creature's body-part and weapon hits of that type on the pieces that cover the part hit (<c>GetArmorLayers</c>, WorldObjects/Monster_Melee.cs:409-419); a spell projectile's damage reads the
/// target's resistance and absorb only, never the armour (WorldObjects/SpellProjectile.cs:486-557), so a bane is no answer to a creature's war spells (the Life Magic protections are).</para>
/// <para><b>How a bane reaches the worn pieces.</b> A player casts it on herself: <c>Player.VerifyNonComponentTargetType</c> accepts a creature for a Vestements spell (WorldObjects/Player_Magic.cs:1391-1393),
/// and <c>WorldObject.TryCastItemEnchantment_WithRedirects</c> (WorldObjects/WorldObject_Magic.cs:2096-2122) gives a self-targeted impen or bane to every equipped object that is Clothing or a shield and enchantable
/// (<c>IsEnchantable</c>: ResistMagic under 9999, WorldObject_Weapon.cs:532), one <c>HandleCastSpell</c> a piece; cast on one piece it lands on that piece only (:2101-2105), and on another creature it reaches at most
/// a shield (:2126). One cast pays the base mana plus the mana mod for each such piece (<c>Creature.CalculateManaUsage</c>, WorldObjects/Creature_Magic.cs:22-31), before Mana Conversion. So the buff cycle casts each
/// armour line once, at her own guid, as it always cast Impenetrability and Bludgeon Bane (<see cref="BuffCycle"/>, D23), and keeps each piece's time in the <see cref="ArmourBuffLedger"/>.</para>
/// <para><b>Where they are kept up</b> (<see cref="KeptInField"/>). A full pass casts every armour line her book holds; the pass at home, after the buff bot's stop and before she leaves, is where all eight go on. In
/// the field (a fight's refresh, a pass in the ground) one of these six is wanted only when the ground's targets deal its damage type (the profile's <c>damage_types</c>, <see cref="FieldMask"/>); Impenetrability and
/// Bludgeon Bane are kept as before. The numbers (the live log of 2026-11-12, level 35 to 37, ten enchantable pieces worn): a level IV armour line is 3.05 s a cast and prices 40 + 8 x 10 = 120 mana before
/// Mana Conversion (V: 50 + 10 x 10 = 150); nine casts with her Mana Conversion 206 took 1 to 90 mana, mean 25, of a pool of 323. Her two lines come due together and are recast under the fight's hard floor, under
/// fire, in 6 s. Eight lines due together would be 25 s with no bolt cast and about 200 mana (960 at the worst) every half hour, for six lines that cut nothing a tusker deals: so they are the home pass's, and
/// they are cast again at each trip home (the buff bot's lines bring her home within the hour).</para>
/// </remarks>
public static class ArmourBanes
{
    // Declared first: the lines below are built from them (static initialisers run in the file's order).
    private static readonly uint[] Powers = [1, 50, 100, 150, 200, 250, 300, 400];
    private static readonly double[] Durations = [1800, 1800, 1800, 1800, 1800, 2700, 3600, 5400];

    /// <summary>The six lines, in the order her pass casts them after Impenetrability and Bludgeon Bane: the two other physical types first, then the elements.</summary>
    public static IReadOnlyList<ProfileLine> Lines { get; } =
    [
        Line("Piercing Bane", [(1569, "Piercing Bane I"), (1570, "Piercing Bane II"), (1571, "Piercing Bane III"), (1572, "Piercing Bane IV"), (1573, "Piercing Bane V"), (1574, "Piercing Bane VI"), (2113, "Archer's Bane"), (4412, "Incantation of Piercing Bane")]),
        Line("Blade Bane", [(37, "Blade Bane I"), (1558, "Blade Bane II"), (1559, "Blade Bane III"), (1560, "Blade Bane IV"), (1561, "Blade Bane V"), (1562, "Blade Bane VI"), (2094, "Swordsman's Bane"), (4393, "Incantation of Blade Bane")]),
        Line("Lightning Bane", [(1535, "Lightning Bane I"), (1536, "Lightning Bane II"), (1537, "Lightning Bane III"), (1538, "Lightning Bane IV"), (1539, "Lightning Bane V"), (1540, "Lightning Bane VI"), (2110, "Astyrrian's Bane"), (4409, "Incantation of Lightning Bane")]),
        Line("Flame Bane", [(1547, "Flame Bane I"), (1548, "Flame Bane II"), (1549, "Flame Bane III"), (1550, "Flame Bane IV"), (1551, "Flame Bane V"), (1552, "Flame Bane VI"), (2102, "Inferno's Bane"), (4401, "Incantation of Flame Bane")]),
        Line("Frost Bane", [(1523, "Frost Bane I"), (1524, "Frost Bane II"), (1525, "Frost Bane III"), (1526, "Frost Bane IV"), (1527, "Frost Bane V"), (1528, "Frost Bane VI"), (2104, "Gelidite's Bane"), (4403, "Incantation of Frost Bane")]),
        Line("Acid Bane", [(1493, "Acid Bane I"), (1494, "Acid Bane II"), (1495, "Acid Bane III"), (1496, "Acid Bane IV"), (1497, "Acid Bane V"), (1498, "Acid Bane VI"), (2092, "Olthoi's Bane"), (4391, "Incantation of Acid Bane")]),
    ];

    /// <summary>The six lines' names, in <see cref="Lines"/>' order.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Lines.Select(l => l.Name)];

    /// <summary>The eight item armour lines of her profile, in the order her pass casts them: Impenetrability, Bludgeon Bane, then <see cref="Names"/>.</summary>
    public static IReadOnlyList<string> AllArmourLines { get; } = ["Impenetrability", "Bludgeon Bane", .. Names];

    /// <summary>ACE's <c>DamageType</c> bit each of the six cuts (ACE.Entity/Enum/DamageType.cs:12-18 @95ef426d: Slash 0x1, Pierce 0x2, Bludgeon 0x4, Cold 0x8, Fire 0x10, Acid 0x20, Electric 0x40).</summary>
    private static readonly FrozenDictionary<string, uint> Bits = new Dictionary<string, uint>(StringComparer.Ordinal)
    {
        ["Piercing Bane"] = 0x2,
        ["Blade Bane"] = 0x1,
        ["Lightning Bane"] = 0x40,
        ["Flame Bane"] = 0x10,
        ["Frost Bane"] = 0x8,
        ["Acid Bane"] = 0x20,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The <c>DamageType</c> bit the line named <paramref name="line"/> cuts, or 0 for any line that is not one of the six (Impenetrability and Bludgeon Bane among them: they are kept as before).</summary>
    /// <param name="line">A profile line's name.</param>
    public static uint DamageBitOf(string line) => line is not null && Bits.TryGetValue(line, out var bit) ? bit : 0;

    /// <summary>
    /// Whether the line named <paramref name="line"/> is wanted in the field, where the creatures deal <paramref name="fieldDamageTypes"/>: one of the six only when the mask holds its damage type; every other line always.
    /// </summary>
    /// <param name="line">A profile line's name.</param>
    /// <param name="fieldDamageTypes">The DamageType bits the ground's targets deal (<see cref="FieldMask"/>); 0 when the ground says none.</param>
    public static bool KeptInField(string line, uint fieldDamageTypes) => DamageBitOf(line) is var bit && (bit == 0 || (fieldDamageTypes & bit) != 0);

    /// <summary>
    /// The DamageType bits the targets of <paramref name="ground"/> deal: the OR of its <see cref="GroundRole.Target"/> rows' <see cref="GroundTarget.DamageTypes"/> (the rule of <see cref="ScrivenerLearnPlan.IncomingElements"/>: an
    /// avoided creature is not fought). 0 when no target says (the outskirts: <c>TODO(m6-outskirts-damage-types)</c>): none of the six is then kept in the field.
    /// </summary>
    /// <param name="ground">The ground.</param>
    public static uint FieldMask(HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        var mask = 0u;
        for (var i = 0; i < ground.Targets.Count; i++)
        {
            if (ground.Targets[i].Role == GroundRole.Target)
            {
                mask |= ground.Targets[i].DamageTypes;
            }
        }

        return mask;
    }

    private static ProfileLine Line(string name, (uint Id, string Name)[] spells) =>
        new(name, name, MagicSchool.ItemEnchantment, ProfileRole.ItemBuffArmor, [.. spells.Select((s, i) => new ProfileSpell(i + 1, s.Id, s.Name, Powers[i], Durations[i]))], ByPlay: true);
}

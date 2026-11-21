using System.Globalization;

namespace Lifestone.Client.Session.Mage;

/// <summary>One worn piece as the armor report reads it (M6 item 14): what its appraisal says of it.</summary>
/// <param name="Name">The piece's name.</param>
/// <param name="Coverage">Its ClothingPriority (ACE's <c>CoverageMask</c>): the parts of the body whose hits it takes.</param>
/// <param name="ArmorLevel">Its ArmorLevel (PropertyInt 28).</param>
/// <param name="BludgeonMod">Its ArmorModVsBludgeon (PropertyFloat 15, the appraisal's armour profile).</param>
/// <param name="OwnImpenetrability">The armour level its own spell adds while it has mana (the Pathwarden pieces cast Impenetrability II, +50, on themselves), 0 for none.</param>
public readonly record struct ArmorPiece(string Name, uint Coverage, int ArmorLevel, double BludgeonMod, int OwnImpenetrability);

/// <summary>
/// M6 7 AK: the report-only armor line of a buff pass (a new judge is report-only in its first gate): the levels the pass left and the damage factor a creature's bludgeon hit should meet. ACE, for a player hit by a creature
/// (<c>DamageEvent.DoCalculateDamage</c>, Entity/DamageEvent.cs:300-372 @95ef426d): the swing's height is rolled, then a part of the body at that height (<see cref="HitShares"/>); the armour is every worn piece whose ClothingPriority
/// covers the part (<c>GetArmorLayers</c>, WorldObjects/Monster_Melee.cs:409-419), each <c>(ArmorLevel + Impenetrability) x clamp(ArmorModVsType + bane, -2, 2)</c> (<c>GetArmorMod</c>, :490-530), summed with the body armour of Armor Self
/// (:428-452); the factor is <c>66.67 / (66.67 + armor)</c> (<c>SkillFormula.CalcArmorMod</c>, WorldObjects/SkillFormula.cs:29-37), then the protection's factor (<c>GetResistanceMod</c>, Creature_Properties.cs:100-130). The spell values are the
/// world's stat_Mod_Val rows (levels I to VI a read-only SELECT of the spell table, VII and VIII <see cref="BuffStatMods"/>' magnitudes).
/// </summary>
/// <remarks>
/// M6 item 14 (the owner: armour instead of the robe): the factor is read on the pieces she wears (<see cref="Factor(IReadOnlyList{ArmorPiece}, int, int, int, int)"/>), each part of the body weighed by its share of the hits; the four-level
/// form on the Pathwarden Robe alone stays for the robe's own numbers. <c>TODO(m6-armor-buffs-live)</c>: the live casts on every worn piece are the lead's to prove.
/// </remarks>
public static class ArmorReport
{
    /// <summary>The Pathwarden Robe's armor level (weenie 40439).</summary>
    public const int RobeArmor = 50;

    private static readonly int[] Impenetrability = [0, 20, 50, 75, 100, 150, 200, 220, 240];
    private static readonly double[] Bane = [0, 0.111, 0.25, 0.5, 0.75, 1.0, 1.5, 1.7, 2.0];
    private static readonly int[] ArmorSelf = [0, 20, 50, 75, 100, 150, 200, 225, 250];
    private static readonly double[] Protection = [1, 0.91, 0.8, 0.67, 0.57, 0.5, 0.4, 0.35, 0.32];

    /// <summary>M6 14 (<c>m6-armour</c>): the armour level Armor Self adds at <paramref name="level"/> (0 none, past VIII as VIII).</summary>
    /// <param name="level">The line's level.</param>
    public static int ArmorSelfAt(int level) => ArmorSelf[Level(level, 8)];

    /// <summary>M6 14: the armour level Impenetrability adds to a piece at <paramref name="level"/>.</summary>
    /// <param name="level">The line's level.</param>
    public static int ImpenetrabilityAt(int level) => Impenetrability[Level(level, 8)];

    /// <summary>M6 14: what a bane adds to a piece's protection at <paramref name="level"/>.</summary>
    /// <param name="level">The line's level.</param>
    public static double BaneAt(int level) => Bane[Level(level, 8)];

    /// <summary>M6 14: the damage factor a protection line gives at <paramref name="level"/> (1 with none).</summary>
    /// <param name="level">The line's level.</param>
    public static double ProtectionAt(int level) => Protection[Level(level, 8)];

    /// <summary>ACE's armour constant (<c>SkillFormula.ArmorMod</c>, 200 / 3).</summary>
    public const double ArmorMod = 200.0 / 3.0;

    /// <summary>
    /// The share of a creature's melee hits each part of the body takes, with the part's coverage bits (<see cref="GearScore.BodyParts"/>). ACE rolls the swing's height evenly over high, medium and low for a creature whose combat table has the
    /// three (<c>GetCombatManeuver</c>, WorldObjects/Monster_Melee.cs:196-204), then a part evenly among the height's (<c>BodyParts.GetBodyPart</c>, Entity/BodyPart.cs:28-32, 77-86: high the head, chest and upper arm; medium the chest, abdomen,
    /// upper arm, lower arm, hand and upper leg; low the foot and lower leg). A creature with no high swing shifts the shares; the report is a model (report-only).
    /// </summary>
    public static IReadOnlyList<(string Part, uint Coverage, double Share)> HitShares { get; } = Shares();

    private static List<(string Part, uint Coverage, double Share)> Shares()
    {
        string[][] heights = [["head", "chest", "upper arm"], ["chest", "abdomen", "upper arm", "lower arm", "hand", "upper leg"], ["foot", "lower leg"]];
        var list = new List<(string Part, uint Coverage, double Share)>();
        foreach (var (part, coverage) in GearScore.BodyParts)
        {
            list.Add((part, coverage, heights.Sum(h => h.Contains(part) ? 1.0 / heights.Length / h.Length : 0.0)));
        }

        return list;
    }

    private static int Level(int level, int top) => Math.Clamp(level, 0, top);

    /// <summary>The factor a bludgeon hit on the Pathwarden Robe meets with the four lines at these levels (0: none; a level past VIII reads as VIII).</summary>
    /// <param name="armorSelf">Armor Self's level.</param>
    /// <param name="protection">Bludgeoning Protection Self's level.</param>
    /// <param name="impenetrability">Impenetrability's level on the robe.</param>
    /// <param name="bane">Bludgeon Bane's level on the robe.</param>
    public static double Factor(int armorSelf, int protection, int impenetrability, int bane)
    {
        var armor = ArmorSelf[Level(armorSelf, 8)] + ((RobeArmor + Impenetrability[Level(impenetrability, 8)]) * Math.Min(2, 1 + Bane[Level(bane, 8)]));
        return ArmorMod / (ArmorMod + armor) * Protection[Level(protection, 8)];
    }

    /// <summary>The factor a bludgeon hit on one part of the body meets: the worn pieces of <paramref name="worn"/> that cover <paramref name="coverage"/>, summed, under the four lines.</summary>
    /// <param name="worn">The worn pieces.</param>
    /// <param name="coverage">The part's coverage bits.</param>
    /// <param name="armorSelf">Armor Self's level.</param>
    /// <param name="protection">Bludgeoning Protection Self's level.</param>
    /// <param name="impenetrability">Impenetrability's level on every piece (a piece's own stronger spell counts in its place).</param>
    /// <param name="bane">Bludgeon Bane's level on every piece.</param>
    public static double PartFactor(IReadOnlyList<ArmorPiece> worn, uint coverage, int armorSelf, int protection, int impenetrability, int bane) =>
        PartArmour(worn, coverage, ArmorSelf[Level(armorSelf, 8)], Impenetrability[Level(impenetrability, 8)], Bane[Level(bane, 8)]) * Protection[Level(protection, 8)];

    /// <summary>
    /// M6 14 holding safety: what the armour alone leaves of a bludgeon hit on one part of the body, before any resistance: the body armour <paramref name="bodyArmour"/> (Armor Self's, as her registry holds it) plus each covering piece's
    /// <c>(ArmorLevel + max(impenetrability, its own)) x clamp(BludgeonMod + bane, -2, 2)</c>, through ACE's <c>66.67 / (66.67 + armour)</c> (a negative armour, an Imperil's, raises it: <c>1 - armour / 66.67</c>).
    /// </summary>
    /// <param name="worn">The worn pieces.</param>
    /// <param name="coverage">The part's coverage bits.</param>
    /// <param name="bodyArmour">The body armour the enchantments add.</param>
    /// <param name="impenetrability">The armour level Impenetrability adds to each piece.</param>
    /// <param name="bane">What the bludgeon bane adds to each piece's protection.</param>
    public static double PartArmour(IReadOnlyList<ArmorPiece> worn, uint coverage, double bodyArmour, double impenetrability, double bane)
    {
        ArgumentNullException.ThrowIfNull(worn);
        var armor = bodyArmour;
        foreach (var piece in worn)
        {
            if ((piece.Coverage & coverage) != 0)
            {
                armor += (piece.ArmorLevel + Math.Max(impenetrability, piece.OwnImpenetrability)) * Math.Clamp(piece.BludgeonMod + bane, -2.0, 2.0);
            }
        }

        return armor > 0 ? ArmorMod / (ArmorMod + armor) : armor < 0 ? 1.0 - (armor / ArmorMod) : 1.0;
    }

    /// <summary>M6 14 holding safety: <see cref="PartArmour"/> over the body, each part weighed by its share of the hits (<see cref="HitShares"/>): the armour's factor before her resistance.</summary>
    /// <param name="worn">The worn pieces.</param>
    /// <param name="bodyArmour">The body armour the enchantments add.</param>
    /// <param name="impenetrability">The armour level Impenetrability adds to each piece.</param>
    /// <param name="bane">What the bludgeon bane adds to each piece's protection.</param>
    public static double ArmourFactor(IReadOnlyList<ArmorPiece> worn, double bodyArmour, double impenetrability, double bane)
    {
        ArgumentNullException.ThrowIfNull(worn);
        var sum = 0.0;
        foreach (var (_, coverage, share) in HitShares)
        {
            sum += share * PartArmour(worn, coverage, bodyArmour, impenetrability, bane);
        }

        return sum;
    }

    /// <summary>M6 14 holding safety: true when <paramref name="w"/> is a piece the report reads (armour or clothing that covers a part of the body), the filter of <see cref="Read(IReadOnlyList{WornPiece}, out int)"/>.</summary>
    /// <param name="w">The worn piece.</param>
    public static bool Covers(WornPiece w)
    {
        ArgumentNullException.ThrowIfNull(w);
        var coverage = GearCoverage.OfWorn(w.Coverage, w.Location);
        return GearScore.SlotOf(w.ItemType, coverage, w.Location) == GearSlot.Armour && (coverage & GearCoverage.Body) != 0;
    }

    /// <summary>The factor a creature's bludgeon hit meets on the worn pieces, each part of the body weighed by its share of the hits (<see cref="HitShares"/>); a bare part meets the body armour alone.</summary>
    /// <param name="worn">The worn pieces.</param>
    /// <param name="armorSelf">Armor Self's level.</param>
    /// <param name="protection">Bludgeoning Protection Self's level.</param>
    /// <param name="impenetrability">Impenetrability's level.</param>
    /// <param name="bane">Bludgeon Bane's level.</param>
    public static double Factor(IReadOnlyList<ArmorPiece> worn, int armorSelf, int protection, int impenetrability, int bane) =>
        HitShares.Sum(p => p.Share * PartFactor(worn, p.Coverage, armorSelf, protection, impenetrability, bane));

    /// <summary>
    /// The worn armour and clothing of <paramref name="worn"/> as the report reads it, from each piece's appraisal: its armour level, its bludgeon protection (the armour profile) and its own Impenetrability while it has mana. A covering piece
    /// with no appraisal is counted in <paramref name="unread"/> and left out: the report then says so, it does not guess a level.
    /// </summary>
    /// <param name="worn">The worn pieces (<see cref="SelfEquipJudge.Worn"/>).</param>
    /// <param name="unread">The covering pieces whose appraisal is not held.</param>
    public static List<ArmorPiece> Read(IReadOnlyList<WornPiece> worn, out int unread) => Read(worn, 0x4, out unread);

    /// <summary>
    /// M6 14 retreat attackers (closes <c>TODO(m6-fight-lines-damage-types)</c>): the worn pieces as <see cref="Read(IReadOnlyList{WornPiece}, out int)"/> reads them, each piece's protection against <paramref name="damageType"/> in the
    /// record's protection field (<see cref="ArmorPiece.BludgeonMod"/>, named for its first reader): the appraisal's armour profile, else the ArmorModVs float, else 1 (<see cref="GearScore.ModVs"/>). ACE's armour against a type is the
    /// same sum with that type's protection (<c>Creature.GetArmorMod</c>, WorldObjects/Creature_BodyPart.cs:65-100 @95ef426d), so <see cref="ArmourFactor"/> on these pieces, with that type's bane, is the armour an acid or a lightning hit meets.
    /// </summary>
    /// <param name="worn">The worn pieces.</param>
    /// <param name="damageType">One DamageType bit (0x1 slash to 0x40 electric).</param>
    /// <param name="unread">The covering pieces whose appraisal is not held.</param>
    public static List<ArmorPiece> Read(IReadOnlyList<WornPiece> worn, uint damageType, out int unread)
    {
        ArgumentNullException.ThrowIfNull(worn);
        unread = 0;
        var list = new List<ArmorPiece>();
        foreach (var w in worn)
        {
            var coverage = GearCoverage.OfWorn(w.Coverage, w.Location);
            if (GearScore.SlotOf(w.ItemType, coverage, w.Location) != GearSlot.Armour || (coverage & GearCoverage.Body) == 0)
            {
                continue; // a caster, jewellery, or underwear with no outerwear part
            }

            if (w.Appraisal is not { IsNotFound: false } a)
            {
                unread++;
                continue;
            }

            var own = 0.0;
            if (!GearScore.OutOfMana(a))
            {
                foreach (var id in a.Spells)
                {
                    if (GearScore.StatOf(id) is { Type: 0x9004, Key: 28 } row)
                    {
                        own = Math.Max(own, row.Value);
                    }
                }
            }

            // the piece's own protection and level (ArmourBase): the appraisal holds the bane and the Impenetrability on it, which the factor adds from her lines or the ledger
            var protection = GearScore.ModVs(a, damageType, out _);
            list.Add(new ArmorPiece(w.Name, coverage, ArmourBase.Level(a), protection, (int)own));
        }

        return list;
    }

    /// <summary>The line: <c>armor_buffs=self:p:i:b damage_factor=f</c> (on the Pathwarden Robe alone).</summary>
    /// <param name="armorSelf">Armor Self's level.</param>
    /// <param name="protection">Bludgeoning Protection Self's level.</param>
    /// <param name="impenetrability">Impenetrability's level.</param>
    /// <param name="bane">Bludgeon Bane's level.</param>
    public static string Line(int armorSelf, int protection, int impenetrability, int bane) =>
        string.Create(CultureInfo.InvariantCulture, $"armor_buffs={armorSelf}:{protection}:{impenetrability}:{bane} damage_factor={Factor(armorSelf, protection, impenetrability, bane):0.000}");

    /// <summary>
    /// The line on what she wears (M6 item 14): <c>armor_buffs=self:p:i:b damage_factor=f armor_pieces=n armor_unread=m bare=0x...</c>, the factor the hit-weighed one of <see cref="Factor(IReadOnlyList{ArmorPiece}, int, int, int, int)"/>;
    /// <c>bare</c> names the parts of the body no piece covers (<see cref="GearCoverage.Gaps"/>), so a set worn short is in the line by name. While a covering piece's appraisal is not held, the factor and the bare parts read <c>unread</c>:
    /// the report does not guess a level, and never prints a factor on part of what she wears.
    /// </summary>
    /// <param name="worn">The worn pieces as read (<see cref="Read(IReadOnlyList{WornPiece}, out int)"/>).</param>
    /// <param name="unread">The covering pieces not read.</param>
    /// <param name="armorSelf">Armor Self's level.</param>
    /// <param name="protection">Bludgeoning Protection Self's level.</param>
    /// <param name="impenetrability">Impenetrability's level.</param>
    /// <param name="bane">Bludgeon Bane's level.</param>
    public static string Line(IReadOnlyList<ArmorPiece> worn, int unread, int armorSelf, int protection, int impenetrability, int bane) =>
        string.Create(CultureInfo.InvariantCulture, $"armor_buffs={armorSelf}:{protection}:{impenetrability}:{bane} damage_factor={(unread > 0 ? "unread" : Factor(worn, armorSelf, protection, impenetrability, bane).ToString("0.000", CultureInfo.InvariantCulture))} armor_pieces={worn.Count} armor_unread={unread} bare={(unread > 0 ? "unread" : "0x" + GearCoverage.Gaps(worn.Select(p => p.Coverage)).ToString("X", CultureInfo.InvariantCulture))}");
}

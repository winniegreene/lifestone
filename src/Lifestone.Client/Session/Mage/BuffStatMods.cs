// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/WorldObject_Magic.cs (HandleCastSpell_Transfer, lines 699-835: the transfer arithmetic of BuffStatMods.Transfer) (AGPL-3.0)
// [with changes: the caster is the source and the destination of a self cast, the drain resistance is 1, and the numbers are the ACE world database's spell rows (read-only SELECT results, the table below)].
using System.Collections.Frozen;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// What one buff spell does to the caster, as ACE's spell table holds it (a read-only SELECT of the ACE world database,
/// spell table, read 2026-10-16: <c>stat_Mod_Type</c>, <c>stat_Mod_Key</c>, <c>stat_Mod_Val</c>).
/// </summary>
/// <param name="Id">The spell id.</param>
/// <param name="Type">ACE's <c>EnchantmentTypeFlags</c> (<c>ACE.Entity/Enum/EnchantmentTypeFlags.cs</c>): 0x9001 is an additive attribute,
/// 0x9010 an additive skill, 0x9004 an additive int (the armour value of Impenetrability), 0x9008 an additive float, 0xA080 the body
/// armour value, 0x5008 a multiplicative float (a vital's regeneration rate, Bludgeoning Protection) and the same with the
/// Beneficial flag (0x2000000) for the wand's auras and the Incantation levels of the life lines.</param>
/// <param name="Key">The stat: the attribute id (1-6), the skill id, the vital's regeneration key (3 Health, 4 Stamina, 5 Mana) or the armour
/// or damage key of the table.</param>
/// <param name="Value">The magnitude: points added (additive) or the factor (multiplicative).</param>
public readonly record struct BuffStatMod(uint Id, uint Type, uint Key, double Value)
{
    /// <summary>True when the effect multiplies the stat (<c>EnchantmentTypeFlags.Multiplicative</c>, 0x4000).</summary>
    public bool IsMultiplicative => (Type & 0x4000) != 0;

    /// <summary>True when the effect adds to the stat (<c>EnchantmentTypeFlags.Additive</c>, 0x8000).</summary>
    public bool IsAdditive => (Type & 0x8000) != 0;

    /// <summary>True when the effect is on a skill (<c>EnchantmentTypeFlags.Skill</c>, 0x10).</summary>
    public bool IsSkill => (Type & 0x10) != 0;

    /// <summary>True when the effect is on an attribute (<c>EnchantmentTypeFlags.Attribute</c>, 0x1).</summary>
    public bool IsAttribute => (Type & 0x1) != 0;
}

/// <summary>A transfer spell's row (<c>Stamina to Mana Self</c>): ACE's source and destination vital, proportion, loss and cap.</summary>
/// <param name="Id">The spell id.</param>
/// <param name="Source">The vital drained (ACE <c>PropertyAttribute2nd</c>: 2 Health, 4 Stamina, 6 Mana).</param>
/// <param name="Destination">The vital given.</param>
/// <param name="Proportion">The share of the source's current value drained.</param>
/// <param name="LossPercent">The share of the drained amount lost in the transfer; negative is a gain (-0.5 gives 1.5 times what was drained).</param>
/// <param name="TransferCap">The most drained or given by one cast; 0 is none (the SELECT's <c>transfer_Cap</c>; NULL for level VIII).</param>
public readonly record struct TransferRow(uint Id, uint Source, uint Destination, float Proportion, float LossPercent, int TransferCap);

/// <summary>A boost spell's row (<c>Revitalize Self</c>): the lowest and highest amount one cast restores.</summary>
/// <param name="Id">The spell id.</param>
/// <param name="Boost">The lowest amount (the SELECT's <c>boost</c>).</param>
/// <param name="Variance">The span above it (<c>boost_Variance</c>): ACE rolls an integer in [Boost, Boost + Variance].</param>
public readonly record struct BoostRow(uint Id, int Boost, int Variance)
{
    /// <summary>The highest amount a cast restores (ACE <c>SpellProperties.MaxBoost</c>).</summary>
    public int Max => Boost + Variance;
}

/// <summary>
/// The StatMods of the profile's buff spells and the numbers of the two mana lines, as data (docs/session.md section 29, plan item 2.1
/// s0a, decisions D10 and D24). The effects are not in the dats (the SpellTable holds a spell's cost, power, duration and formula; the
/// server's spell table holds what it does), so they are pinned here from the lead's read-only SELECTs of the ACE world database,
/// spell table, read 2026-10-16.
/// </summary>
/// <remarks>
/// <para><b>What it is for.</b> It is the planner's prediction table: <c>BuffChainDatTests</c> and the cycle's plan read it to say what
/// level a line will reach before it is cast. The cycle itself never trusts it: it reads the buffed skill from the read model
/// (<c>GearCasts.TryCurrentSkill</c>) after each cast.</para>
/// <para><b>The magnitudes</b> (VI / VII / VIII): the attribute and skill lines +35 / +40 / +45; Armor Self 200 / 225 / 250;
/// Impenetrability 200 / 220 / 240; Bludgeon Bane x1.5 / 1.7 / 2.0; Regeneration, Rejuvenation and Mana Renewal x1.85 / 2.15 / 2.45;
/// Hermetic Link x1.6 / 1.7 / 1.8; Defender +0.15 / 0.17 / 0.20; Bludgeoning Protection 0.4 / 0.35 / 0.32.</para>
/// <para><b>The transfer rule</b> is ACE's <c>WorldObject.HandleCastSpell_Transfer</c> (<c>ACE.Server/WorldObjects/WorldObject_Magic.cs</c>,
/// lines 699-835, ACE 95ef426d). For a self cast the source and destination are the caster and the drain resistance is 1:
/// the drained amount is <c>round(current source x Proportion)</c> (line 721; .NET rounding, halves to even), cut to
/// <c>TransferCap</c> when the cap is not 0 (726); the given amount is <c>round(drained x (1 - LossPercent))</c> (732); when that is more
/// than the destination's missing amount (itself cut to the cap, 737-738) both are scaled by missing / given and the drained
/// amount is rounded again (743-746); both are then applied to the vitals, and the chat line is "You cast &lt;spell&gt; on yourself and
/// lose N points of stamina and also gain M points of mana" (835). <see cref="Transfer"/> is that arithmetic.</para>
/// <para><b>The boost rule</b> is <c>HandleCastSpell_Boost</c> (<c>WorldObject_Magic.cs</c> 474-560): a uniform integer in
/// [Boost, Boost + Variance] (<c>SpellProperties.MaxBoost</c>), applied to the vital and cut at the vital's maximum by the vital update.</para>
/// <para>Stamina to Mana Self has eight levels (VII is 2345 "Meditative Trance", VIII 4981 "Incantation of Stamina to Mana Self": loss -1,
/// no cap), and so has Revitalize Self (VII 2083 "Robustification", VIII 4321); the dat holds all of them (<c>BsdProfileSpellsTests</c>).</para>
/// </remarks>
public static class BuffStatMods
{
    /// <summary>The ACE vital id of Stamina (<c>PropertyAttribute2nd.Stamina</c>).</summary>
    public const uint Stamina = 4;

    /// <summary>The ACE vital id of Mana (<c>PropertyAttribute2nd.Mana</c>).</summary>
    public const uint Mana = 6;

    /// <summary>The Stamina to Mana Self spells (1676-1681, 2345, 4981), in level order.</summary>
    public static IReadOnlyList<TransferRow> StaminaToMana { get; } =
    [
        new(1676, Stamina, Mana, 0.5f, 0.1f, 50),
        new(1677, Stamina, Mana, 0.5f, 0.0f, 100),
        new(1678, Stamina, Mana, 0.5f, -0.1f, 150),
        new(1679, Stamina, Mana, 0.5f, -0.2f, 200),
        new(1680, Stamina, Mana, 0.5f, -0.35f, 0),
        new(1681, Stamina, Mana, 0.5f, -0.5f, 0),
        new(2345, Stamina, Mana, 0.5f, -0.75f, 0), // Meditative Trance
        new(4981, Stamina, Mana, 0.5f, -1.0f, 0), // Incantation of Stamina to Mana Self
    ];

    /// <summary>
    /// M6 7 AD: the base mana of <see cref="StaminaToMana"/>'s levels I to VII, in their order (the SpellTable's BaseMana, pinned by <c>The_stamina_to_mana_base_mana_is_the_spell_tables</c>): what a press costs a caster with no Mana Conversion, and so which
    /// level nets the most at a low stamina (<see cref="Vitals.ManaOrder.NetYield"/>).
    /// </summary>
    public static IReadOnlyList<uint> StaminaToManaBaseMana { get; } = [10, 20, 30, 40, 50, 60, 70];

    /// <summary>The Revitalize Self spells (1177-1182, 2083, 4321), in level order.</summary>
    public static IReadOnlyList<BoostRow> Revitalize { get; } =
    [
        new(1177, 15, 20),
        new(1178, 20, 25),
        new(1179, 30, 30),
        new(1180, 40, 40),
        new(1181, 60, 60),
        new(1182, 80, 80),
        new(2083, 100, 100), // Robustification
        new(4321, 125, 125), // Incantation of Revitalize Self
    ];

    /// <summary>
    /// M6 14 (the stamina dance in the potions' place): the base mana of <see cref="Revitalize"/>'s levels I to VI, in their order (the SpellTable's BaseMana, pinned by <c>The_stamina_to_mana_base_mana_is_the_spell_tables</c>). A Life Magic self spell
    /// carries no worn-piece surcharge, so this is its <c>GearCasts.ManaCeiling</c>, from which Mana Conversion rolls the cost (<see cref="Casting.ManaConversionModel.ExpectedCost"/>): what the spell plan weighs a round of the dance on
    /// (<see cref="ScrivenerLearnPlan.RevitalizeTarget"/>) with no SpellTable at hand.
    /// </summary>
    public static IReadOnlyList<uint> RevitalizeBaseMana { get; } = [15, 30, 40, 50, 60, 70];

    /// <summary>The boost row of spell <paramref name="id"/>, or null. Allocation-free (M6 stamina dance: the refill's whole-roll test reads it at every decision; a foreach over the list boxes its enumerator).</summary>
    public static BoostRow? BoostOf(uint id)
    {
        var rows = Revitalize;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Id == id)
            {
                return rows[i];
            }
        }

        return null;
    }

    /// <summary>The transfer row of spell <paramref name="id"/>, or null.</summary>
    public static TransferRow? TransferOf(uint id)
    {
        foreach (var row in StaminaToMana)
        {
            if (row.Id == id)
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// What one cast of the transfer <paramref name="row"/> drains and gives (ACE <c>HandleCastSpell_Transfer</c>, see the type's remarks). M6 14 drain resistance: <paramref name="drainMod"/> is the source's drain resistance, which ACE
    /// applies to the take before the TransferCap (<c>WorldObject_Magic.cs</c> 719-726 @95ef426d: <c>round(current x Proportion x drainMod)</c>; the target's <c>GetResistanceMod</c> for a drain from another, 1 for a self transfer).
    /// </summary>
    /// <param name="row">The spell's row.</param>
    /// <param name="sourceCurrent">The current value of the source vital.</param>
    /// <param name="destinationMissing">The destination vital's maximum minus its current value.</param>
    /// <param name="drained">The amount the source loses.</param>
    /// <param name="given">The amount the destination gains.</param>
    /// <param name="drainMod">The source's drain resistance (<see cref="Vitals.DrainResist"/>; 1, the default: none; 0: it gives nothing).</param>
    public static void Transfer(in TransferRow row, uint sourceCurrent, uint destinationMissing, out uint drained, out uint given, float drainMod = 1.0f)
    {
        var src = (uint)Math.Round(sourceCurrent * row.Proportion * drainMod);
        if (row.TransferCap != 0 && src > row.TransferCap)
        {
            src = (uint)row.TransferCap;
        }

        var dest = (uint)Math.Round(src * (1.0f - row.LossPercent));
        var maxDest = destinationMissing;
        if (row.TransferCap != 0 && maxDest > row.TransferCap)
        {
            maxDest = (uint)row.TransferCap;
        }

        if (dest > maxDest)
        {
            var scalar = (float)maxDest / dest;
            src = (uint)Math.Round(src * scalar);
            dest = maxDest;
        }

        drained = src;
        given = dest;
    }

    private static readonly BuffStatMod[] Rows =
    [
        new(2, 0x9001, 1, 10.0), // Strength Self I
        new(18, 0x9010, 6, 10.0), // Invulnerability Self I
        new(24, 0xA080, 0, 20.0), // Armor Self I
        new(51, 0x9004, 28, 20.0), // Impenetrability I
        new(54, 0x5008, 4, 1.1), // Rejuvenation Self I
        new(165, 0x5008, 3, 1.1), // Regeneration Self I
        new(166, 0x5008, 3, 1.25), // Regeneration Self II
        new(167, 0x5008, 3, 1.4), // Regeneration Self III
        new(168, 0x5008, 3, 1.55), // Regeneration Self IV
        new(169, 0x5008, 3, 1.7), // Regeneration Self V
        new(170, 0x5008, 3, 1.85), // Regeneration Self VI
        new(189, 0x5008, 4, 1.25), // Rejuvenation Self II
        new(190, 0x5008, 4, 1.4), // Rejuvenation Self III
        new(191, 0x5008, 4, 1.55), // Rejuvenation Self IV
        new(192, 0x5008, 4, 1.7), // Rejuvenation Self V
        new(193, 0x5008, 4, 1.85), // Rejuvenation Self VI
        new(212, 0x5008, 5, 1.1), // Mana Renewal Self I
        new(213, 0x5008, 5, 1.25), // Mana Renewal Self II
        new(214, 0x5008, 5, 1.4), // Mana Renewal Self III
        new(215, 0x5008, 5, 1.55), // Mana Renewal Self IV
        new(216, 0x5008, 5, 1.7), // Mana Renewal Self V
        new(217, 0x5008, 5, 1.85), // Mana Renewal Self VI
        new(245, 0x9010, 6, 15.0), // Invulnerability Self II
        new(246, 0x9010, 6, 20.0), // Invulnerability Self III
        new(247, 0x9010, 6, 25.0), // Invulnerability Self IV
        new(248, 0x9010, 6, 30.0), // Invulnerability Self V
        new(249, 0x9010, 6, 35.0), // Invulnerability Self VI
        new(256, 0x9010, 7, 10.0), // Impregnability Self I
        new(257, 0x9010, 7, 15.0), // Impregnability Self II
        new(258, 0x9010, 7, 20.0), // Impregnability Self III
        new(259, 0x9010, 7, 25.0), // Impregnability Self IV
        new(260, 0x9010, 7, 30.0), // Impregnability Self V
        new(261, 0x9010, 7, 35.0), // Impregnability Self VI
        new(274, 0x9010, 15, 10.0), // Magic Resistance Self I
        new(275, 0x9010, 15, 15.0), // Magic Resistance Self II
        new(276, 0x9010, 15, 20.0), // Magic Resistance Self III
        new(277, 0x9010, 15, 25.0), // Magic Resistance Self IV
        new(278, 0x9010, 15, 30.0), // Magic Resistance Self V
        new(279, 0x9010, 15, 35.0), // Magic Resistance Self VI
        new(557, 0x9010, 31, 10.0), // Creature Enchantment Mastery Self I
        new(558, 0x9010, 31, 15.0), // Creature Enchantment Mastery Self II
        new(559, 0x9010, 31, 20.0), // Creature Enchantment Mastery Self III
        new(560, 0x9010, 31, 25.0), // Creature Enchantment Mastery Self IV
        new(561, 0x9010, 31, 30.0), // Creature Enchantment Mastery Self V
        new(562, 0x9010, 31, 35.0), // Creature Enchantment Mastery Self VI
        new(581, 0x9010, 32, 10.0), // Item Enchantment Mastery Self I
        new(582, 0x9010, 32, 15.0), // Item Enchantment Mastery Self II
        new(583, 0x9010, 32, 20.0), // Item Enchantment Mastery Self III
        new(584, 0x9010, 32, 25.0), // Item Enchantment Mastery Self IV
        new(585, 0x9010, 32, 30.0), // Item Enchantment Mastery Self V
        new(586, 0x9010, 32, 35.0), // Item Enchantment Mastery Self VI
        new(605, 0x9010, 33, 10.0), // Life Magic Mastery Self I
        new(606, 0x9010, 33, 15.0), // Life Magic Mastery Self II
        new(607, 0x9010, 33, 20.0), // Life Magic Mastery Self III
        new(608, 0x9010, 33, 25.0), // Life Magic Mastery Self IV
        new(609, 0x9010, 33, 30.0), // Life Magic Mastery Self V
        new(610, 0x9010, 33, 35.0), // Life Magic Mastery Self VI
        new(629, 0x9010, 34, 10.0), // War Magic Mastery Self I
        new(630, 0x9010, 34, 15.0), // War Magic Mastery Self II
        new(631, 0x9010, 34, 20.0), // War Magic Mastery Self III
        new(632, 0x9010, 34, 25.0), // War Magic Mastery Self IV
        new(633, 0x9010, 34, 30.0), // War Magic Mastery Self V
        new(634, 0x9010, 34, 35.0), // War Magic Mastery Self VI
        new(653, 0x9010, 16, 10.0), // Mana Conversion Mastery Self I
        new(654, 0x9010, 16, 15.0), // Mana Conversion Mastery Self II
        new(655, 0x9010, 16, 20.0), // Mana Conversion Mastery Self III
        new(656, 0x9010, 16, 25.0), // Mana Conversion Mastery Self IV
        new(657, 0x9010, 16, 30.0), // Mana Conversion Mastery Self V
        new(658, 0x9010, 16, 35.0), // Mana Conversion Mastery Self VI
        new(678, 0x9010, 14, 10.0), // Arcane Enlightenment Self I
        new(679, 0x9010, 14, 15.0), // Arcane Enlightenment Self II
        new(680, 0x9010, 14, 20.0), // Arcane Enlightenment Self III
        new(681, 0x9010, 14, 25.0), // Arcane Enlightenment Self IV
        new(682, 0x9010, 14, 30.0), // Arcane Enlightenment Self V
        new(683, 0x9010, 14, 35.0), // Arcane Enlightenment Self VI
        new(1018, 0x5008, 66, 0.91), // Bludgeoning Protection Self I
        new(1019, 0x5008, 66, 0.8), // Bludgeoning Protection Self II
        new(1020, 0x5008, 66, 0.67), // Bludgeoning Protection Self III
        new(1021, 0x5008, 66, 0.57), // Bludgeoning Protection Self IV
        new(1022, 0x5008, 66, 0.5), // Bludgeoning Protection Self V
        new(1023, 0x5008, 66, 0.4), // Bludgeoning Protection Self VI
        new(1308, 0xA080, 0, 50.0), // Armor Self II
        new(1309, 0xA080, 0, 75.0), // Armor Self III
        new(1310, 0xA080, 0, 100.0), // Armor Self IV
        new(1311, 0xA080, 0, 150.0), // Armor Self V
        new(1312, 0xA080, 0, 200.0), // Armor Self VI
        new(1328, 0x9001, 1, 15.0), // Strength Self II
        new(1329, 0x9001, 1, 20.0), // Strength Self III
        new(1330, 0x9001, 1, 25.0), // Strength Self IV
        new(1331, 0x9001, 1, 30.0), // Strength Self V
        new(1332, 0x9001, 1, 35.0), // Strength Self VI
        new(1349, 0x9001, 2, 10.0), // Endurance Self I
        new(1350, 0x9001, 2, 15.0), // Endurance Self II
        new(1351, 0x9001, 2, 20.0), // Endurance Self III
        new(1352, 0x9001, 2, 25.0), // Endurance Self IV
        new(1353, 0x9001, 2, 30.0), // Endurance Self V
        new(1354, 0x9001, 2, 35.0), // Endurance Self VI
        new(1373, 0x9001, 4, 10.0), // Coordination Self I
        new(1374, 0x9001, 4, 15.0), // Coordination Self II
        new(1375, 0x9001, 4, 20.0), // Coordination Self III
        new(1376, 0x9001, 4, 25.0), // Coordination Self IV
        new(1377, 0x9001, 4, 30.0), // Coordination Self V
        new(1378, 0x9001, 4, 35.0), // Coordination Self VI
        new(1397, 0x9001, 3, 10.0), // Quickness Self I
        new(1398, 0x9001, 3, 15.0), // Quickness Self II
        new(1399, 0x9001, 3, 20.0), // Quickness Self III
        new(1400, 0x9001, 3, 25.0), // Quickness Self IV
        new(1401, 0x9001, 3, 30.0), // Quickness Self V
        new(1402, 0x9001, 3, 35.0), // Quickness Self VI
        new(1421, 0x9001, 5, 10.0), // Focus Self I
        new(1422, 0x9001, 5, 15.0), // Focus Self II
        new(1423, 0x9001, 5, 20.0), // Focus Self III
        new(1424, 0x9001, 5, 25.0), // Focus Self IV
        new(1425, 0x9001, 5, 30.0), // Focus Self V
        new(1426, 0x9001, 5, 35.0), // Focus Self VI
        new(1445, 0x9001, 6, 10.0), // Willpower Self I
        new(1446, 0x9001, 6, 15.0), // Willpower Self II
        new(1447, 0x9001, 6, 20.0), // Willpower Self III
        new(1448, 0x9001, 6, 25.0), // Willpower Self IV
        new(1449, 0x9001, 6, 30.0), // Willpower Self V
        new(1450, 0x9001, 6, 35.0), // Willpower Self VI
        new(1475, 0x2005008, 171, 1.1), // Aura of Hermetic Link Self I
        new(1476, 0x2005008, 171, 1.2), // Aura of Hermetic Link Self II
        new(1477, 0x2005008, 171, 1.3), // Aura of Hermetic Link Self III
        new(1478, 0x2005008, 171, 1.4), // Aura of Hermetic Link Self IV
        new(1479, 0x2005008, 171, 1.5), // Aura of Hermetic Link Self V
        new(1480, 0x2005008, 171, 1.6), // Aura of Hermetic Link Self VI
        new(1482, 0x9004, 28, 50.0), // Impenetrability II
        new(1483, 0x9004, 28, 75.0), // Impenetrability III
        new(1484, 0x9004, 28, 100.0), // Impenetrability IV
        new(1485, 0x9004, 28, 150.0), // Impenetrability V
        new(1486, 0x9004, 28, 200.0), // Impenetrability VI
        new(1511, 0x9008, 15, 0.111), // Bludgeon Bane I
        new(1512, 0x9008, 15, 0.25), // Bludgeon Bane II
        new(1513, 0x9008, 15, 0.5), // Bludgeon Bane III
        new(1514, 0x9008, 15, 0.75), // Bludgeon Bane IV
        new(1515, 0x9008, 15, 1.0), // Bludgeon Bane V
        new(1516, 0x9008, 15, 1.5), // Bludgeon Bane VI
        new(1599, 0x9008, 169, 0.03), // Aura of Defender Self I
        new(1601, 0x9008, 169, 0.05), // Aura of Defender Self II
        new(1602, 0x9008, 169, 0.075), // Aura of Defender Self III
        new(1603, 0x9008, 169, 0.1), // Aura of Defender Self IV
        new(1604, 0x9008, 169, 0.13), // Aura of Defender Self V
        new(1605, 0x9008, 169, 0.15), // Aura of Defender Self VI
        new(3253, 0x2008008, 170, 0.01), // Aura of Spirit Drinker Self I (M5 7 P7j, D143: WeaponAuraElemental, additive; the pinned world SQL v0.9.296's spell rows)
        new(3254, 0x2008008, 170, 0.02), // Aura of Spirit Drinker Self II
        new(3255, 0x2008008, 170, 0.03), // Aura of Spirit Drinker Self III
        new(3256, 0x2008008, 170, 0.04), // Aura of Spirit Drinker Self IV
        new(3257, 0x2008008, 170, 0.05), // Aura of Spirit Drinker Self V
        new(3258, 0x2008008, 170, 0.06), // Aura of Spirit Drinker Self VI
        new(2053, 0xA080, 0, 225.0), // Executor's Blessing
        new(2059, 0x9001, 4, 40.0), // Honed Control
        new(2061, 0x9001, 2, 40.0), // Perseverance
        new(2067, 0x9001, 5, 40.0), // Inner Calm
        new(2081, 0x9001, 3, 40.0), // Hastening
        new(2087, 0x9001, 1, 40.0), // Might of the Lugians
        new(2091, 0x9001, 6, 40.0), // Mind Blossom
        new(2098, 0x9008, 15, 1.7), // Tusker's Bane
        new(2101, 0x9008, 169, 0.17), // Aura of Cragstone's Will
        new(2108, 0x9004, 28, 220.0), // Brogard's Defiance
        new(2117, 0x5008, 171, 1.7), // Aura of Mystic's Blessing
        new(2153, 0x5008, 66, 0.35), // Blessing of the Mace Turner
        new(2183, 0x5008, 5, 2.15), // Battlemage's Blessing
        new(2185, 0x5008, 3, 2.15), // Robustify
        new(2187, 0x5008, 4, 2.15), // Unflinching Persistence
        new(2195, 0x9010, 14, 40.0), // Aliester's Blessing
        new(2215, 0x9010, 31, 40.0), // Adja's Blessing
        new(2243, 0x9010, 7, 40.0), // Aura of Deflection
        new(2245, 0x9010, 6, 40.0), // Aura of Defense
        new(2249, 0x9010, 32, 40.0), // Celcynd's Blessing
        new(2267, 0x9010, 33, 40.0), // Harlune's Blessing
        new(2281, 0x9010, 15, 40.0), // Aura of Resistance
        new(2287, 0x9010, 16, 40.0), // Nuhmudira's Blessing
        new(2323, 0x9010, 34, 40.0), // Hieromancer's Blessing
        new(4291, 0x200A080, 0, 250.0), // Incantation of Armor Self
        new(4297, 0x9001, 4, 45.0), // Incantation of Coordination Self
        new(4299, 0x9001, 2, 45.0), // Incantation of Endurance Self
        new(4305, 0x9001, 5, 45.0), // Incantation of Focus Self
        new(4319, 0x9001, 3, 45.0), // Incantation of Quickness Self
        new(4325, 0x9001, 1, 45.0), // Incantation of Strength Self
        new(4329, 0x9001, 6, 45.0), // Incantation of Willpower Self
        new(4397, 0x9008, 15, 2.0), // Incantation of Bludgeon Bane
        new(4400, 0x2009008, 169, 0.2), // Aura of Incantation of Defender Self
        new(4407, 0x9004, 28, 240.0), // Incantation of Impenetrability
        new(4418, 0x2005008, 171, 1.8), // Aura of Incantation of Hermetic Link Self
        new(4464, 0x2005008, 66, 0.32), // Incantation of Bludgeoning Protection Self
        new(4494, 0x2005008, 5, 2.45), // Incantation of Mana Renewal Self
        new(4496, 0x2005008, 3, 2.45), // Incantation of Regeneration Self
        new(4498, 0x2005008, 4, 2.45), // Incantation of Rejuvenation Self
        new(4510, 0x9010, 14, 45.0), // Incantation of Arcane Enlightenment Self
        new(4530, 0x9010, 31, 45.0), // Incantation of Creature Enchantment Mastery Self
        new(4558, 0x9010, 7, 45.0), // Incantation of Impregnability Self
        new(4560, 0x9010, 6, 45.0), // Incantation of Invulnerability Self
        new(4564, 0x9010, 32, 45.0), // Incantation of Item Enchantment Mastery Self
        new(4582, 0x9010, 33, 45.0), // Incantation of Life Magic Mastery Self
        new(4596, 0x9010, 15, 45.0), // Incantation of Magic Resistance Self
        new(4602, 0x9010, 16, 45.0), // Incantation of Mana Conversion Mastery Self
        new(4638, 0x9010, 34, 45.0), // Incantation of War Magic Mastery Self
    ];

    /// <summary>The StatMod of every buff spell of <see cref="ArenaProfile"/> (the 24 buff lines, eight levels each), by spell id.</summary>
    public static IReadOnlyDictionary<uint, BuffStatMod> BySpell { get; } = Rows.ToFrozenDictionary(r => r.Id);
}

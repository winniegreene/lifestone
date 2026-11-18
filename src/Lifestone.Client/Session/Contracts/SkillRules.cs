using System.Collections.Immutable;
using Lifestone.Data.CharacterCreation;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The one engine-agnostic rule for a skill's value (M3 2.2 a2): retail's <c>CACQualities::InqSkill</c> (11.6096 0x00593380) over the read model, in the arithmetic ACE's
/// <c>CreatureSkill.Current</c> has (<c>Entity/CreatureSkill.cs</c> 154-195; <c>GearCasts.TryCurrentSkill</c> follows it, and now calls this class). The skills page (<c>gmSkillUI</c>, 11.6096 0x0049B040 and the rest of the class)
/// shows <see cref="TryCurrent"/>, coloured by its difference from <see cref="TryBase"/>, and the cast planner reads the same number.
/// </summary>
/// <remarks>
/// <para><b>Base</b> (<c>InqSkill</c> with <c>raw</c> true): the skill's formula over the raw attributes (start plus ranks; <c>InqSkillBaseLevel</c>, 11.6096 0x00592E60: <c>SkillFormula::Calculate</c>,
/// (w + attr2 x y + attr1 x x) / z rounded half up, and 0 while the advancement class is below the skill's <c>_min_level</c>) plus the skill's start level (<c>_init_level</c>) and its ranks
/// (<c>_level_from_pp</c>).</para>
/// <para><b>Current</b> (<c>InqSkill</c> with <c>raw</c> false): the same over the attributes with their enchantments (<see cref="EnchantmentRules.EnchantAttribute"/>), scaled by the multiplicative skill
/// enchantments and the vitae, then the additive ones added, each truncated to an int (ACE <c>EnchantmentManager.GetSkillMod_Additives</c>, <c>EnchantmentManager.cs</c> 759-765), rounded half away from zero and
/// floored at 0 (retail <c>CEnchantmentRegistry::EnchantSkill</c>, 11.6096 0x00595620, keeps 0 below 0.5). A skill enchantment counts when the skill kind bit (0x10) is set and either it names several stats,
/// has the skill's key, or belongs to the attack or defence group the key is in (<see cref="EnchantmentRules.AffectsKey"/>, which closes <c>TODO(g4-enchantment-affects-skills)</c>); of the enchantments of
/// one spell category only the strongest counts (higher power level, at equal power the later start, on a tie the first of the registry).</para>
/// <para><b>The augmentation bonuses</b> (4.2 x4, closing <c>TODO(m3-skill-aug-bonus)</c>) are ACE's <c>CreatureSkill.GetAugBonus_Base</c> and <c>GetAugBonus_Current</c> (<c>CreatureSkill.cs</c> 154-195 and the
/// two methods below <c>Current</c>), read from the description's int qualities (<see cref="AugBonusBase"/>, <see cref="AugBonusCurrent"/>). Retail folds them into <c>CACQualities::InqSkill</c> (11.6096 0x00592AF0; the
/// 11.6096 table has no separate names for them). The base bonus joins the base and is scaled by the multipliers and the vitae; the current bonus is added after the vitae. BSD Mage has none, so its numbers do not change.</para>
/// <para><b>No allocation.</b> The skill enchantments are walked in place (the strongest-of-a-category test is a scan of the registry, which holds a few dozen entries at most). The one allocation left is the
/// two enchanted attributes' (<see cref="EnchantmentRules.EnchantAttribute"/> builds a short list while the registry holds an enchantment), which is outside this class.</para>
/// </remarks>
public static class SkillRules
{
    /// <summary>The advancement class of a skill the character cannot use at all (retail's <c>SKILL_ADVANCEMENT_CLASS</c> 0, "unusable").</summary>
    public const uint UnusableClass = 0;

    /// <summary>The advancement class of an untrained skill (1).</summary>
    public const uint UntrainedClass = 1;

    /// <summary>The advancement class of a trained skill (2).</summary>
    public const uint TrainedClass = 2;

    /// <summary>The advancement class of a specialised skill (3).</summary>
    public const uint SpecializedClass = 3;

    /// <summary>The skill kind bit of an enchantment's type (ACE <c>EnchantmentTypeFlags.Skill</c>).</summary>
    public const uint SkillKind = 0x10;

    /// <summary>
    /// The base value of a skill (see the class remarks): false when the read model lacks an attribute of its formula (a skill the description lacks counts as untrained with no ranks, as retail's default).
    /// </summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="skillId">The skill id.</param>
    /// <param name="formula">The skill's formula (the SkillTable's).</param>
    /// <param name="minLevel">The skill's <c>_min_level</c>: the formula counts from this advancement class.</param>
    /// <param name="value">The base value.</param>
    public static bool TryBase(PlayerQualities q, uint skillId, in SkillTableFormula formula, uint minLevel, out uint value)
    {
        ArgumentNullException.ThrowIfNull(q);
        value = 0;
        if (!q.IsKnown)
        {
            return false;
        }

        var skill = Record(q, skillId);

        uint part = 0;
        if (skill.AdvancementClass >= minLevel)
        {
            if (!TryRawAttributes(q, in formula, out var a1, out var a2) || !formula.TryCalculate(a1, a2, out part))
            {
                return false;
            }
        }

        value = part + skill.Init + skill.Ranks + AugBonusBase(q, skillId, skill.AdvancementClass);
        return true;
    }

    /// <summary>
    /// The current value of a skill (see the class remarks): false when the read model lacks an attribute of its formula (a skill the description lacks counts as untrained with no ranks, as retail's default).
    /// </summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="skillId">The skill id.</param>
    /// <param name="formula">The skill's formula (the SkillTable's).</param>
    /// <param name="minLevel">The skill's <c>_min_level</c>.</param>
    /// <param name="value">The current value.</param>
    public static bool TryCurrent(PlayerQualities q, uint skillId, in SkillTableFormula formula, uint minLevel, out uint value)
    {
        ArgumentNullException.ThrowIfNull(q);
        return TryCurrentOver(q, skillId, in formula, minLevel, q.Enchantments, out value);
    }

    /// <summary>
    /// M6 14 (<c>m6-lore-spend</c>): <see cref="TryCurrent"/> over <paramref name="enchantments"/> in place of the registry (the formula's attributes and the skill's own lines alike; the vitae
    /// and the augmentations are the character's): the value a chosen set of her enchantments gives, as the Arcane Lore rule reads the lines that stand at a wield (<see cref="Mage.StandingLore"/>).
    /// </summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="skillId">The skill id.</param>
    /// <param name="formula">The skill's formula (the SkillTable's).</param>
    /// <param name="minLevel">The skill's <c>_min_level</c>.</param>
    /// <param name="enchantments">The enchantments counted.</param>
    /// <param name="value">The current value over them.</param>
    public static bool TryCurrentOver(PlayerQualities q, uint skillId, in SkillTableFormula formula, uint minLevel, ImmutableArray<EnchantmentDesc> enchantments, out uint value)
    {
        ArgumentNullException.ThrowIfNull(q);
        value = 0;
        if (!q.IsKnown)
        {
            return false;
        }

        var skill = Record(q, skillId);

        uint part = 0;
        if (skill.AdvancementClass >= minLevel)
        {
            if (!TryRawAttributes(q, in formula, out var raw1, out var raw2))
            {
                return false;
            }

            var a1 = formula.Attribute1 == 0 ? 0 : EnchantmentRules.EnchantAttribute(enchantments, formula.Attribute1, raw1);
            var a2 = formula.Attribute2 == 0 ? 0 : EnchantmentRules.EnchantAttribute(enchantments, formula.Attribute2, raw2);
            if (!formula.TryCalculate(a1, a2, out part))
            {
                return false;
            }
        }

        Modifiers(enchantments, skillId, out var product, out var additive);
        var vitae = q.TryGetVitae(out var v) ? v.Enchantment.StatModValue : (double?)null;
        value = Compose(part, skill.Init + skill.Ranks + AugBonusBase(q, skillId, skill.AdvancementClass), product, vitae, additive + (int)AugBonusCurrent(q, skill.AdvancementClass));
        return true;
    }

    /// <summary>PropertyInt 365 (ACE <c>LumAugAllSkills</c>): added to every skill's base.</summary>
    public const uint LumAugAllSkillsProperty = 365;

    /// <summary>PropertyInt 300, 301, 302 (ACE <c>AugmentationSkilledMelee</c>, <c>Missile</c>, <c>Magic</c>): +10 per level on the skills of that group.</summary>
    public const uint AugSkilledMeleeProperty = 300, AugSkilledMissileProperty = 301, AugSkilledMagicProperty = 302;

    /// <summary>PropertyInt 326 (ACE <c>AugmentationJackOfAllTrades</c>): +5 per level on every skill, after the vitae.</summary>
    public const uint JackOfAllTradesProperty = 326;

    /// <summary>PropertyInt 344 (ACE <c>LumAugSkilledSpec</c>): +2 per level on a specialised skill, after the vitae.</summary>
    public const uint LumAugSkilledSpecProperty = 344;

    /// <summary>PropertyInt 390 (ACE <c>Enlightenment</c>): +1 per level on a trained or specialised skill.</summary>
    public const uint EnlightenmentProperty = 390;

    /// <summary>ACE <c>CreatureSkill.GetAugBonus_Base</c> (<c>CreatureSkill.cs</c> 198-228): the bonuses that join the skill's base.</summary>
    public static uint AugBonusBase(PlayerQualities q, uint skillId, uint advancementClass)
    {
        uint total = Level(q, LumAugAllSkillsProperty);
        if (Level(q, AugSkilledMeleeProperty) > 0 && IsMelee(skillId))
        {
            total += Level(q, AugSkilledMeleeProperty) * 10;
        }
        else if (Level(q, AugSkilledMissileProperty) > 0 && IsMissile(skillId))
        {
            total += Level(q, AugSkilledMissileProperty) * 10;
        }
        else if (Level(q, AugSkilledMagicProperty) > 0 && IsMagic(skillId))
        {
            total += Level(q, AugSkilledMagicProperty) * 10;
        }

        if (advancementClass >= TrainedClass)
        {
            total += Level(q, EnlightenmentProperty);
        }

        return total;
    }

    /// <summary>ACE <c>CreatureSkill.GetAugBonus_Current</c> (<c>CreatureSkill.cs</c> 230-243): the bonuses added after the vitae.</summary>
    public static uint AugBonusCurrent(PlayerQualities q, uint advancementClass)
    {
        var total = Level(q, JackOfAllTradesProperty) * 5;
        if (advancementClass == SpecializedClass)
        {
            total += Level(q, LumAugSkilledSpecProperty) * 2;
        }

        return total;
    }

    private static uint Level(PlayerQualities q, uint property) => q.TryGetInt(property, out var v) && v > 0 ? (uint)v : 0;

    // ACE Player.MeleeSkills, MissileSkills and MagicSkills (Player_Skills.cs 554-590), by Skill id.
    private static bool IsMelee(uint id) => id is 1 or 4 or 5 or 9 or 10 or 11 or 13 or 41 or 44 or 45 or 46 or 49;

    private static bool IsMissile(uint id) => id is 2 or 3 or 8 or 12 or 47;

    private static bool IsMagic(uint id) => id is 31 or 32 or 33 or 34 or 43;

    /// <summary>
    /// The arithmetic of <see cref="TryCurrent"/> over plain numbers: <paramref name="formulaPart"/> plus <paramref name="initPlusRanks"/>, scaled by <paramref name="product"/> (the multiplicative skill
    /// enchantments, 1 for none) and the vitae's factor when there is one, plus <paramref name="additive"/>, rounded half away from zero and floored at 0.
    /// </summary>
    /// <param name="formulaPart">The formula's value over the (enchanted) attributes.</param>
    /// <param name="initPlusRanks">The skill's start level plus its ranks.</param>
    /// <param name="product">The product of the strongest multiplicative skill enchantments.</param>
    /// <param name="vitae">The vitae's factor, or null when the character has none.</param>
    /// <param name="additive">The sum of the additive skill enchantments, each truncated to an int.</param>
    public static uint Compose(uint formulaPart, uint initPlusRanks, double product, double? vitae, int additive)
    {
        double total = formulaPart + initPlusRanks;
        total *= product;
        if (vitae is { } v)
        {
            total *= v;
        }

        total += additive;
        return (uint)Math.Max(0.0, Math.Round(total, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// The registry's effect on skill <paramref name="skillId"/> (retail <c>CEnchantmentRegistry::EnchantSkill</c> culls the multiplicative and the additive list for the skill kind and the key): the product of
    /// the strongest multiplicative enchantments and the sum of the strongest additive ones, each truncated to an int. Allocates nothing.
    /// </summary>
    /// <param name="enchantments">The registry, in its order.</param>
    /// <param name="skillId">The skill id.</param>
    /// <param name="product">The product (1 for none).</param>
    /// <param name="additive">The sum (0 for none).</param>
    public static void Modifiers(ImmutableArray<EnchantmentDesc> enchantments, uint skillId, out double product, out int additive)
    {
        product = 1.0;
        additive = 0;
        if (enchantments.IsDefaultOrEmpty)
        {
            return;
        }

        for (var i = 0; i < enchantments.Length; i++)
        {
            var e = enchantments[i];
            if (!Counts(in e, skillId) || IsBeaten(enchantments, i, skillId))
            {
                continue;
            }

            if (e.Bucket == EnchantmentBucket.Multiplicative)
            {
                product *= e.StatModValue;
            }
            else
            {
                // ACE adds each additive skill enchantment truncated to an int (GetSkillMod_Additives).
                additive += (int)e.StatModValue;
            }
        }
    }

    /// <summary>Whether the enchantment is a candidate for the skill: in the multiplicative or additive list, of the skill kind, and naming the skill (<see cref="EnchantmentRules.AffectsKey"/>).</summary>
    private static bool Counts(in EnchantmentDesc e, uint skillId) =>
        e.Bucket is EnchantmentBucket.Multiplicative or EnchantmentBucket.Additive && (e.StatModType & SkillKind) != 0 && EnchantmentRules.AffectsKey(in e, skillId);

    /// <summary>True when another candidate of the same list and spell category beats the one at <paramref name="at"/> (<c>CEnchantmentRegistry::Duel</c>'s outcome without the list).</summary>
    private static bool IsBeaten(ImmutableArray<EnchantmentDesc> enchantments, int at, uint skillId)
    {
        var e = enchantments[at];
        for (var j = 0; j < enchantments.Length; j++)
        {
            if (j == at)
            {
                continue;
            }

            var f = enchantments[j];
            if (f.Bucket != e.Bucket || f.SpellCategory != e.SpellCategory || !Counts(in f, skillId))
            {
                continue;
            }

            if (f.PowerLevel > e.PowerLevel || (f.PowerLevel == e.PowerLevel && (f.StartTime > e.StartTime || (f.StartTime == e.StartTime && j < at))))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The skill's record, or retail's default for a skill the description lacks (<c>InqSkillBaseLevel</c>, 11.6096 0x00592E60, takes advancement class 1 and no ranks).</summary>
    private static SkillDesc Record(PlayerQualities q, uint skillId) =>
        q.TryGetSkill(skillId, out var skill) ? skill : new SkillDesc(skillId, 0, 0, UntrainedClass, 0, 0, 0, 0.0);

    private static bool TryRawAttributes(PlayerQualities q, in SkillTableFormula formula, out uint attribute1, out uint attribute2)
    {
        attribute1 = 0;
        attribute2 = 0;
        return (formula.Attribute1 == 0 || q.TryGetAttributeLevels(formula.Attribute1, out _, out attribute1))
            && (formula.Attribute2 == 0 || q.TryGetAttributeLevels(formula.Attribute2, out _, out attribute2));
    }

    // ------------------------------------------------------------------ the experience a skill costs

    /// <summary>
    /// Experience to raise a trained or specialised skill by one rank (<c>gmSkillUI::GetCostToRaise</c>, 11.6096 0x0049B8F0): the XpTable's cumulative experience for rank + 1 less the experience spent
    /// (<c>ExperienceSystem::ExperienceToSkillLevel</c>, 11.6096 0x005C93B0); 0 when the skill is not trained or specialised or is at its rank cap (the list's length less one).
    /// </summary>
    /// <param name="xp">The XpTable.</param>
    /// <param name="skill">The skill as the read model has it.</param>
    public static uint CostToRaise(XpTableData xp, in SkillDesc skill) => CostOfRanks(xp, in skill, 1);

    /// <summary>
    /// Experience to raise a trained or specialised skill by ten ranks, or by as many as remain below the cap when fewer (<c>gmSkillUI::GetCostToRaise10</c>, 11.6096 0x0049B130); 0 when it cannot be
    /// raised.
    /// </summary>
    /// <param name="xp">The XpTable.</param>
    /// <param name="skill">The skill as the read model has it.</param>
    public static uint CostToRaise10(XpTableData xp, in SkillDesc skill) => CostOfRanks(xp, in skill, 10);

    /// <summary>
    /// The progress through the current rank (<c>DisplaySelectionFooter_Trained</c>, 11.6096 0x0049BAD0, the footer meter): the experience spent past the XpTable's total for the skill's ranks, over the span of one rank;
    /// 0 at the rank cap, for a skill that is neither trained nor specialised, and for an empty span.
    /// </summary>
    /// <param name="xp">The XpTable.</param>
    /// <param name="skill">The skill as the read model has it.</param>
    public static float RankProgress(XpTableData xp, in SkillDesc skill)
    {
        var list = skill.AdvancementClass switch
        {
            TrainedClass => xp.TrainedSkillXpList,
            SpecializedClass => xp.SpecializedSkillXpList,
            _ => null,
        };
        if (list is null || skill.Ranks + 1 >= list.Count)
        {
            return 0f;
        }

        var floor = list[skill.Ranks];
        var span = list[skill.Ranks + 1] - floor;
        return span == 0 ? 0f : Math.Clamp((float)((double)skill.Experience - floor) / span, 0f, 1f);
    }

    /// <summary>The skill's advancement class in the read model: 0 (unusable) for a skill the description lacks (<c>RebuildSkillList</c>, 11.6096 0x0049C610, starts <c>_sac</c> at 0).</summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="skillId">The skill id.</param>
    public static uint ClassOf(PlayerQualities q, uint skillId) =>
        q.TryGetSkill(skillId, out var skill) ? skill.AdvancementClass : UnusableClass;

    /// <summary>
    /// <c>SkillInfoRegion::GetVitaeModifier</c> (11.6096 0x004F1C30): what the vitae takes off <paramref name="baseValue"/> (zero without a vitae, else negative), rounded half up. The retail UI reads
    /// the vitae through this, not through the wire type.
    /// </summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="baseValue">The skill's base value.</param>
    public static int VitaeModifier(PlayerQualities q, uint baseValue)
    {
        if (!q.TryGetVitae(out var vitae) || vitae.Enchantment.StatModValue >= 1.0f)
        {
            return 0;
        }

        return (int)((long)(((float)baseValue * vitae.Enchantment.StatModValue) + 0.5f) - baseValue);
    }

    /// <summary><see cref="CostToRaise(XpTableData, in SkillDesc)"/> for skill <paramref name="skillId"/> of the read model (0 for a skill it lacks).</summary>
    /// <param name="xp">The XpTable.</param>
    /// <param name="q">The player's qualities.</param>
    /// <param name="skillId">The skill id.</param>
    public static uint CostToRaise(XpTableData xp, PlayerQualities q, uint skillId) => q.TryGetSkill(skillId, out var skill) ? CostOfRanks(xp, in skill, 1) : 0;

    /// <summary><see cref="CostToRaise10(XpTableData, in SkillDesc)"/> for skill <paramref name="skillId"/> of the read model.</summary>
    /// <param name="xp">The XpTable.</param>
    /// <param name="q">The player's qualities.</param>
    /// <param name="skillId">The skill id.</param>
    public static uint CostToRaise10(XpTableData xp, PlayerQualities q, uint skillId) => q.TryGetSkill(skillId, out var skill) ? CostOfRanks(xp, in skill, 10) : 0;

    /// <summary><see cref="RankProgress(XpTableData, in SkillDesc)"/> for skill <paramref name="skillId"/> of the read model.</summary>
    /// <param name="xp">The XpTable.</param>
    /// <param name="q">The player's qualities.</param>
    /// <param name="skillId">The skill id.</param>
    public static float RankProgress(XpTableData xp, PlayerQualities q, uint skillId) => q.TryGetSkill(skillId, out var skill) ? RankProgress(xp, in skill) : 0f;

    private static uint CostOfRanks(XpTableData xp, in SkillDesc skill, int ranks)
    {
        var list = skill.AdvancementClass switch
        {
            TrainedClass => xp.TrainedSkillXpList,
            SpecializedClass => xp.SpecializedSkillXpList,
            _ => null,
        };
        if (list is null || skill.Ranks >= list.Count - 1)
        {
            return 0;
        }

        var target = Math.Min(skill.Ranks + ranks, list.Count - 1);
        var cost = (long)list[target] - skill.Experience;
        return cost > 0 ? (uint)cost : 0;
    }
}

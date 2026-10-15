using System.Collections.Immutable;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// How the enchantment registry changes an attribute or a secondary attribute (a vital's maximum), re-expressed from retail: <c>CEnchantmentRegistry::EnchantAttribute</c> (11.4186 0x00594570,
/// 11.6096 0x005953E0), <c>CEnchantmentRegistry::EnchantAttribute2nd</c> (0x00594670 / 0x005954E0), <c>CEnchantmentRegistry::CullEnchantmentsFromList</c> (0x00594470 / 0x005952E0),
/// <c>CEnchantmentRegistry::Duel</c> (0x005942B0 / 0x00595120), <c>Enchantment::Duel</c> (0x005CACE0 / 0x005CBC10) and <c>Enchantment::Enchant</c> (0x005CAD10 / 0x005CBC40).
/// </summary>
/// <remarks>
/// <para>The rule. Each list (multiplicative, then additive, in the order the registry holds them) is culled for the stat asked: an enchantment counts when its type has the kind's bit
/// (attribute 0x1, secondary attribute 0x2) and either names several stats (0x2000) or has the stat's key. The culled enchantments duel by spell category: for one category only
/// the strongest stays (higher power level; at equal power the one that started later; the first of the list on a tie of both), so the layers of one spell do not stack and two spells of one
/// category do not either. Then the survivors apply in order: an additive one (0x8000) adds its value, a multiplicative one (0x4000) multiplies by it. The result is kept at 1 (base below the
/// floor) or at the floor (attribute 10, secondary attribute 5), then rounded half up.</para>
/// <para><c>TODO(g4-quality-filter)</c>: <c>EnchantAttribute2nd</c> first asks the portal's quality filter (<c>ACQualityFilter::QueryAttribute2nd</c>, table 0x1000000C) whether the secondary attribute may be
/// enchanted; the table is not decoded here and every secondary attribute is taken as enchantable. <c>TODO(g4-enchantment-affects-skills)</c>: the cull's two extra clauses (attack and defence skill
/// groups, <c>Enchantment::AffectsAttackSkills</c> and <c>AffectsDefenseSkills</c>) name skill keys only, so they never hold for an attribute's or a vital's key.</para>
/// </remarks>
public static class EnchantmentRules
{
    /// <summary>Type bit: the enchantment changes a primary attribute.</summary>
    public const uint AttributeKind = 0x1;

    /// <summary>Type bit: the enchantment changes a secondary attribute (a vital's maximum or current value).</summary>
    public const uint SecondaryAttributeKind = 0x2;

    /// <summary>Type bit: the enchantment names several stats, so its key is not compared (the second byte's 0x20 in the cull).</summary>
    public const uint MultipleStats = 0x2000;

    /// <summary>Type bit: the enchantment multiplies (the second byte's 0x40 in <c>Enchantment::Enchant</c>).</summary>
    public const uint Multiplicative = 0x4000;

    /// <summary>Type bit: the enchantment adds (the second byte's 0x80; tested first).</summary>
    public const uint Additive = 0x8000;

    /// <summary>
    /// A primary attribute with the registry applied (<c>CACQualities::InqAttribute</c> with <c>raw</c> false asks <c>EnchantAttribute</c>): <paramref name="id"/> is the attribute (1 Strength ... 6 Self),
    /// <paramref name="raw"/> its start plus ranks.
    /// </summary>
    public static uint EnchantAttribute(ImmutableArray<EnchantmentDesc> enchantments, uint id, uint raw)
    {
        if (enchantments.IsDefaultOrEmpty)
        {
            return raw;
        }

        var value = (float)raw;
        var affecting = new List<EnchantmentDesc>(4);
        Cull(enchantments, EnchantmentBucket.Multiplicative, AttributeKind, id, affecting);
        Cull(enchantments, EnchantmentBucket.Additive, AttributeKind, id, affecting);
        foreach (var enchantment in affecting)
        {
            value = Enchant(enchantment, value);
        }

        var floor = raw < 10 ? 1.0f : 10.0f; // retail keeps a raised or lowered attribute at 1 (base below 10) or at 10
        return (uint)((double)Math.Max(value, floor) + 0.5);
    }

    /// <summary>
    /// A secondary attribute with the registry applied (<c>CACQualities::InqAttribute2nd</c> with <c>raw</c> false asks <c>EnchantAttribute2nd</c>): <paramref name="id"/> is the secondary attribute's id (1
    /// maximum health, 3 maximum stamina, 5 maximum mana), <paramref name="raw"/> the value before enchantments. The vitae applies first, whatever its key.
    /// </summary>
    public static uint EnchantSecondaryAttribute(ImmutableArray<EnchantmentDesc> enchantments, uint id, uint raw)
    {
        if (enchantments.IsDefaultOrEmpty)
        {
            return raw;
        }

        var value = (float)raw;
        foreach (var enchantment in enchantments)
        {
            if (enchantment.Bucket == EnchantmentBucket.Vitae)
            {
                value = Enchant(enchantment, value);
            }
        }

        var affecting = new List<EnchantmentDesc>(4);
        Cull(enchantments, EnchantmentBucket.Multiplicative, SecondaryAttributeKind, id, affecting);
        Cull(enchantments, EnchantmentBucket.Additive, SecondaryAttributeKind, id, affecting);
        foreach (var enchantment in affecting)
        {
            value = Enchant(enchantment, value);
        }

        var floor = raw < 5 ? 1.0f : 5.0f; // retail keeps a secondary attribute at 1 (base below 5) or at 5
        return (uint)((double)Math.Max(value, floor) + 0.5);
    }

    /// <summary><c>Enchantment::Enchant</c>: adds when the type says additive, else multiplies when it says multiplicative, else leaves the value.</summary>
    public static float Enchant(in EnchantmentDesc enchantment, float value)
    {
        if ((enchantment.StatModType & Additive) != 0)
        {
            return enchantment.StatModValue + value;
        }

        return (enchantment.StatModType & Multiplicative) != 0 ? enchantment.StatModValue * value : value;
    }

    /// <summary><c>Enchantment::Duel</c>: true when <paramref name="existing"/> keeps its place against <paramref name="challenger"/> (a higher power level, or an equal one that started later).</summary>
    public static bool Holds(in EnchantmentDesc existing, in EnchantmentDesc challenger) =>
        existing.PowerLevel > challenger.PowerLevel || (existing.PowerLevel == challenger.PowerLevel && challenger.StartTime < existing.StartTime);

    /// <summary><c>CEnchantmentRegistry::CullEnchantmentsFromList</c> over one list of the registry: the enchantments that affect <paramref name="key"/>, duelled into <paramref name="affecting"/>.</summary>
    private static void Cull(ImmutableArray<EnchantmentDesc> enchantments, EnchantmentBucket bucket, uint kind, uint key, List<EnchantmentDesc> affecting)
    {
        foreach (var enchantment in enchantments)
        {
            if (enchantment.Bucket == bucket && (enchantment.StatModType & kind) != 0 && ((enchantment.StatModType & MultipleStats) != 0 || enchantment.StatModKey == key))
            {
                Duel(enchantment, affecting);
            }
        }
    }

    /// <summary><c>CEnchantmentRegistry::Duel</c>: the challenger meets the one enchantment of its spell category in <paramref name="list"/>; it takes the place at the list's tail when it wins or there is none.</summary>
    private static void Duel(in EnchantmentDesc challenger, List<EnchantmentDesc> list)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].SpellCategory != challenger.SpellCategory)
            {
                continue;
            }

            if (Holds(list[i], challenger))
            {
                return;
            }

            list.RemoveAt(i);
            break;
        }

        list.Add(challenger);
    }
}

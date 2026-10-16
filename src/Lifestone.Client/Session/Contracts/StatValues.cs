using System.Collections.Immutable;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The stat pages' reading of the player's qualities (M3 2.2 a1; the retail UI cannot name the wire types, so this keeps them in the contracts layer): an attribute's base and its value with the enchantment
/// registry applied (<c>CACQualities::InqAttribute</c> with <c>raw</c> true and false), and a vital's current value, base maximum, enchanted maximum and the vitae's share of the difference
/// (<c>CACQualities::InqAttribute2nd</c> with <c>raw</c> true and false; <c>Attribute2ndInfoRegion::GetVitaeModifier</c>, 11.6096 0x004F1D90). The registry is read once, when the object is made, as
/// <see cref="PlayerQualities.TryGetVitalLevels"/> applies it (each entry's start time is the session-clock time it arrived), so make one per redraw, not per row.
/// </summary>
public sealed class StatValues
{
    private readonly PlayerQualities _qualities;
    private readonly ImmutableArray<EnchantmentDesc> _effective;

    /// <summary>The reading of <paramref name="qualities"/> as they are now.</summary>
    public StatValues(PlayerQualities qualities)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        _qualities = qualities;
        var active = qualities.ActiveEnchantments;
        if (active.IsDefaultOrEmpty)
        {
            _effective = [];
            return;
        }

        var builder = ImmutableArray.CreateBuilder<EnchantmentDesc>(active.Length);
        foreach (var entry in active)
        {
            builder.Add(entry.Enchantment with { StartTime = entry.StartSeconds });
        }

        _effective = builder.MoveToImmutable();
    }

    /// <summary>The attribute <paramref name="id"/> (1 Strength ... 6 Self): its base (start plus ranks) and its value with the registry applied. False when the attribute is not known.</summary>
    public bool TryAttribute(uint id, out uint baseValue, out uint current)
    {
        current = 0;
        if (!_qualities.TryGetAttributeLevels(id, out _, out baseValue))
        {
            return false;
        }

        current = EnchantmentRules.EnchantAttribute(_effective, id, baseValue);
        return true;
    }

    /// <summary>
    /// The vital named by its maximum's id (1 health, 3 stamina, 5 mana): <paramref name="current"/> value, <paramref name="baseMax"/> (start, ranks and the formula over the base attributes, plus PropertyInt 379
    /// for health), <paramref name="max"/> with the registry applied and <paramref name="vitae"/>, what the vitae takes off the base maximum (zero, or negative). False until the vital is known.
    /// </summary>
    public bool TryVital(uint maxId, out uint current, out uint baseMax, out uint max, out long vitae)
    {
        baseMax = 0;
        vitae = 0;
        if (!_qualities.TryGetVitalLevels(maxId, out current, out max) || !_qualities.TryGetVital(maxId, out var vital))
        {
            return false;
        }

        uint Base(uint id) => _qualities.TryGetAttributeLevels(id, out _, out var raw) ? raw : 0;
        var bonus = maxId switch
        {
            1 => (Base(PlayerQualities.EnduranceAttributeId) + 1) / 2,
            3 => Base(PlayerQualities.EnduranceAttributeId),
            _ => Base(PlayerQualities.SelfAttributeId),
        };
        var gear = maxId == 1 && _qualities.TryGetInt(PlayerQualities.GearMaxHealthProperty, out var g) ? g : 0;
        baseMax = unchecked((uint)(vital.Start + vital.Ranks + bonus + gear));
        if (_qualities.TryGetVitae(out var entry) && entry.Enchantment.StatModValue < 1.0f)
        {
            // GetVitaeModifier: the base maximum through the vitae, less the base maximum.
            var scaled = EnchantmentRules.Enchant(entry.Enchantment, baseMax);
            vitae = (long)(scaled + 0.5) - baseMax;
        }

        return true;
    }

    /// <summary>The most ranks the ten-rank button raises at once (<c>gmAttributeUI::GetCostToRaise10</c>, 11.6096 0x0049CEE0).</summary>
    public const int TenRanks = 10;

    /// <summary>
    /// What raising a stat costs, as <c>gmAttributeUI::GetCostToRaise</c> (11.6096 0x0049CDF0) and <c>GetCostToRaise10</c> (0x0049CEE0) price it: the XpTable's cumulative cost of the target rank (one rank, or
    /// <see cref="TenRanks"/> at most, fewer near the cap) less the experience already spent on the stat. <paramref name="vital"/> picks the vital list (maximum health, stamina, mana: id 1, 3, 5), else the attribute
    /// list. 0 when the rank is capped (an attribute's 190, a vital's 196), the stat is unknown or <paramref name="table"/> is null.
    /// </summary>
    public ulong CostToRaise(bool vital, uint id, bool ten, Lifestone.Data.CharacterCreation.XpTableData? table)
    {
        if (table is null)
        {
            return 0;
        }

        uint ranks;
        uint spent;
        IReadOnlyList<uint> list;
        if (vital)
        {
            if (!_qualities.TryGetVital(id, out var v))
            {
                return 0;
            }

            (ranks, spent, list) = (v.Ranks, v.Experience, table.VitalXpList);
        }
        else
        {
            if (!_qualities.TryGetAttribute(id, out var a))
            {
                return 0;
            }

            (ranks, spent, list) = (a.Ranks, a.Experience, table.AttributeXpList);
        }

        var max = list.Count - 1; // ExperienceSystem::GetMaxAttributeLevel / GetMaxAttribute2ndLevel
        if (ranks >= max)
        {
            return 0;
        }

        var target = list[(int)ranks + (ten ? Math.Min(TenRanks, max - (int)ranks) : 1)];
        return target > spent ? target - spent : 0;
    }
}

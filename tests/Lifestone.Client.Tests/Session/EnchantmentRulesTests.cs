using System.Collections.Immutable;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;
using Xunit;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The enchantment registry's rule for attributes and vitals (docs/retail-ui.md section 21.3; <see cref="EnchantmentRules"/>): only the strongest enchantment of each spell category applies, the
/// multiplicative ones apply before the additive ones, and a vital's maximum is the formula over the enchanted attributes plus the vital's own modifiers.
/// </summary>
public sealed class EnchantmentRulesTests
{
    private const uint AdditiveSecondary = 0x2000000 | EnchantmentRules.Additive | 0x1000 | EnchantmentRules.SecondaryAttributeKind;
    private const uint MultiplicativeSecondary = 0x2000000 | EnchantmentRules.Multiplicative | 0x1000 | EnchantmentRules.SecondaryAttributeKind;
    private const uint AdditiveAttribute = 0x2000000 | EnchantmentRules.Additive | 0x1000 | EnchantmentRules.AttributeKind;

    private static EnchantmentDesc Make(ushort spell, ushort layer, ushort category, uint power, uint type, uint key, float value, EnchantmentBucket bucket, double start = 0) =>
        new(spell, layer, category, 0, power, start, -1.0, 0x50000001, 0f, 0f, 0.0, type, key, value, 0, bucket);

    private static EnchantmentDesc Add(ushort category, uint power, uint type, uint key, float value, ushort layer = 1, double start = 0) =>
        Make((ushort)(category + 100), layer, category, power, type, key, value, EnchantmentBucket.Additive, start);

    private static EnchantmentDesc Multiply(ushort category, uint power, uint type, uint key, float value, ushort layer = 1, double start = 0) =>
        Make((ushort)(category + 100), layer, category, power, type, key, value, EnchantmentBucket.Multiplicative, start);

    private static uint Max(ImmutableArray<EnchantmentDesc> enchantments, uint id, uint raw) => EnchantmentRules.EnchantSecondaryAttribute(enchantments, id, raw);

    [Fact]
    public void An_enchantment_of_another_vital_or_another_kind_does_not_touch_the_vital()
    {
        ImmutableArray<EnchantmentDesc> enchantments =
        [
            Add(7, 100, AdditiveSecondary, 3, 22f), // stamina
            Add(8, 100, AdditiveAttribute, 1, 22f), // an attribute with the same key
        ];
        Assert.Equal(28u, Max(enchantments, 1, 28));
        Assert.Equal(50u, Max(enchantments, 3, 28));
    }

    [Fact]
    public void The_layers_of_one_spell_do_not_stack_only_the_strongest_applies()
    {
        // Four layers of one spell (one category) with rising power: +5, +10, +15, +20; only the top counts.
        ImmutableArray<EnchantmentDesc> enchantments =
        [
            Add(7, 51, AdditiveSecondary, 1, 5f, 1),
            Add(7, 52, AdditiveSecondary, 1, 10f, 2),
            Add(7, 53, AdditiveSecondary, 1, 15f, 3),
            Add(7, 54, AdditiveSecondary, 1, 20f, 4),
        ];
        Assert.Equal(48u, Max(enchantments, 1, 28));
        Assert.Equal(48u, Max([.. enchantments.Reverse()], 1, 28));

        // Identical layered duplicates count once (folded in by the M4 polish fold).
        var one = Add(7, 50, AdditiveSecondary, 1, 10f);
        Assert.Equal(38u, Max([one, one, one, one], 1, 28));
    }

    [Fact]
    public void Two_categories_both_apply()
    {
        ImmutableArray<EnchantmentDesc> enchantments =
        [
            Add(7, 50, AdditiveSecondary, 1, 10f, 1),
            Add(7, 60, AdditiveSecondary, 1, 12f, 2),
            Add(9, 40, AdditiveSecondary, 1, 7f),
        ];
        Assert.Equal(47u, Max(enchantments, 1, 28)); // 28 + 12 (category 7's top) + 7 (category 9)
    }

    [Fact]
    public void At_equal_power_the_one_that_started_later_wins()
    {
        ImmutableArray<EnchantmentDesc> older = [Add(7, 50, AdditiveSecondary, 1, 10f, 1, start: -100), Add(7, 50, AdditiveSecondary, 1, 20f, 2, start: -10)];
        Assert.Equal(48u, Max(older, 1, 28));
        ImmutableArray<EnchantmentDesc> reversed = [older[1], older[0]];
        Assert.Equal(48u, Max(reversed, 1, 28));
    }

    [Fact]
    public void Multiplicative_ones_apply_before_additive_ones()
    {
        // (28 * 1.5) + 10 = 52; the other order would give (28 + 10) * 1.5 = 57.
        ImmutableArray<EnchantmentDesc> enchantments =
        [
            Make(1, 1, 3, 10, AdditiveSecondary, 1, 10f, EnchantmentBucket.Additive),
            Make(2, 1, 4, 10, MultiplicativeSecondary, 1, 1.5f, EnchantmentBucket.Multiplicative),
        ];
        Assert.Equal(52u, Max(enchantments, 1, 28));
    }

    [Fact]
    public void A_multiplicative_enchantment_is_one_per_category_too()
    {
        ImmutableArray<EnchantmentDesc> enchantments = [Multiply(4, 10, MultiplicativeSecondary, 1, 1.5f, 1), Multiply(4, 11, MultiplicativeSecondary, 1, 2f, 2)];
        Assert.Equal(56u, Max(enchantments, 1, 28)); // only the x2 layer
    }

    [Fact]
    public void An_additive_and_a_multiplicative_of_one_category_duel_across_the_lists()
    {
        // The registry duels the culled enchantments of both lists by category: the stronger one stays, whichever list it came from.
        ImmutableArray<EnchantmentDesc> enchantments = [Multiply(4, 10, MultiplicativeSecondary, 1, 2f), Add(4, 20, AdditiveSecondary, 1, 5f)];
        Assert.Equal(33u, Max(enchantments, 1, 28));
    }

    [Fact]
    public void An_enchantment_that_names_several_stats_ignores_the_key()
    {
        Assert.Equal(38u, Max([Add(7, 50, AdditiveSecondary | EnchantmentRules.MultipleStats, 99, 10f)], 5, 28));
    }

    [Fact]
    public void The_result_is_kept_at_1_or_at_5_and_rounded_half_up()
    {
        Assert.Equal(1u, Max([Add(7, 50, AdditiveSecondary, 1, -50f)], 1, 4));
        Assert.Equal(5u, Max([Add(7, 50, AdditiveSecondary, 1, -50f)], 1, 28));
        Assert.Equal(32u, Max([Multiply(7, 50, MultiplicativeSecondary, 1, 1.125f)], 1, 28)); // 28 * 1.125 = 31.5 rounds up
    }

    [Fact]
    public void The_vitae_applies_first_whatever_its_key()
    {
        ImmutableArray<EnchantmentDesc> enchantments =
        [
            Make(666, 1, 666, 0, EnchantmentRules.Multiplicative, 0, 0.5f, EnchantmentBucket.Vitae),
            Add(7, 50, AdditiveSecondary, 1, 10f),
        ];
        Assert.Equal(24u, Max(enchantments, 1, 28)); // 28 * 0.5 + 10
    }

    [Fact]
    public void An_attribute_is_enchanted_and_kept_at_10()
    {
        Assert.Equal(100u, EnchantmentRules.EnchantAttribute([Add(7, 50, AdditiveAttribute, 2, 45f)], 2, 55));
        Assert.Equal(55u, EnchantmentRules.EnchantAttribute([Add(7, 50, AdditiveAttribute, 2, 45f)], 3, 55));
        Assert.Equal(10u, EnchantmentRules.EnchantAttribute([Add(7, 50, AdditiveAttribute, 2, -100f)], 2, 55));
        Assert.Equal(1u, EnchantmentRules.EnchantAttribute([Add(7, 50, AdditiveAttribute, 2, -100f)], 2, 8));
    }

    [Fact]
    public void No_enchantments_leave_the_values_as_they_are()
    {
        Assert.Equal(28u, EnchantmentRules.EnchantSecondaryAttribute([], 1, 28));
        Assert.Equal(55u, EnchantmentRules.EnchantAttribute([], 2, 55));
    }

    // ------------------------------------------------------------------ the quality filter (M3 2.2 a1, TODO(g4-quality-filter))

    // A quality filter file as QualityFilter::UnPack reads it: the file id, the eight base counts (all zero), the three attribute counts, then the id lists.
    private static Lifestone.Data.Dat.QualityFilterData Filter(uint[] attribute2nds)
    {
        var words = new List<uint> { 0x0E010001, 0, 0, 0, 0, 0, 0, 0, 0, 0, (uint)attribute2nds.Length, 0 };
        words.AddRange(attribute2nds);
        var bytes = new byte[words.Count * 4];
        for (var i = 0; i < words.Count; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), words[i]);
        }

        return Lifestone.Data.Dat.QualityFilterData.Decode(bytes, 0x0E010001);
    }

    [Fact]
    public void A_secondary_attribute_the_quality_filter_does_not_keep_is_not_enchanted()
    {
        ImmutableArray<EnchantmentDesc> enchantments = [Add(7, 100, AdditiveSecondary, 1, 22f), Add(8, 100, AdditiveSecondary, 3, 22f)];
        var filter = Filter([1, 5]); // the maximum health and mana only
        Assert.Equal(50u, EnchantmentRules.EnchantSecondaryAttributeFiltered(enchantments, 1, 28, filter));
        Assert.Equal(28u, EnchantmentRules.EnchantSecondaryAttributeFiltered(enchantments, 3, 28, filter)); // EnchantAttribute2nd does nothing: the value stays
        Assert.Equal(50u, EnchantmentRules.EnchantSecondaryAttributeFiltered(enchantments, 3, 28, null)); // no filter installed: every vital counts
        Assert.Equal(50u, Max(enchantments, 3, 28)); // the static filter is not installed in this test
    }

    [Fact]
    public void The_filter_file_must_be_consumed_exactly()
    {
        var bytes = new byte[13 * 4 + 2];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x0E010001);
        Assert.Throws<InvalidDataException>(() => Lifestone.Data.Dat.QualityFilterData.Decode(bytes, 0x0E010001));
        Assert.Throws<InvalidDataException>(() => Lifestone.Data.Dat.QualityFilterData.Decode(bytes.AsSpan(0, 12), 0x0E010001));
        Assert.Throws<InvalidDataException>(() => Lifestone.Data.Dat.QualityFilterData.Decode(bytes.AsSpan(0, 12 * 4), 0x0E010002));
    }

    // ------------------------------------------------------------------ through the qualities

    private static PlayerQualities FreshCharacter(ImmutableArray<EnchantmentDesc> enchantments, uint health = 28, uint stamina = 55, uint mana = 55)
    {
        var q = new PlayerQualities { IsKnown = true, Enchantments = enchantments };
        for (uint id = 1; id <= 6; id++)
        {
            q.Attributes[id] = new AttributeDesc(id, 0, 55, 0);
        }

        q.Vitals[1] = new VitalDesc(1, 0, 0, 0, health);
        q.Vitals[3] = new VitalDesc(3, 0, 0, 0, stamina);
        q.Vitals[5] = new VitalDesc(5, 0, 0, 0, mana);
        return q;
    }

    [Fact]
    public void Stat_values_read_the_base_and_the_enchanted_value_and_the_vitaes_share()
    {
        // Endurance and Self 55 (base), Self +45, and a vitae of 0.5: the health base is (55+1)/2 = 28, mana 55; the mana maximum is (55+45) halved by the vitae.
        ImmutableArray<EnchantmentDesc> enchantments = [Add(8, 100, AdditiveAttribute, 6, 45f), Make(666, 1, 666, 0, EnchantmentRules.Multiplicative, 0, 0.5f, EnchantmentBucket.Vitae)];
        var q = FreshCharacter(enchantments, 50, 100, 100);
        var stats = new StatValues(q);
        Assert.True(stats.TryAttribute(6, out var baseSelf, out var currentSelf));
        Assert.Equal((55u, 100u), (baseSelf, currentSelf));
        Assert.True(stats.TryAttribute(1, out var baseStrength, out var currentStrength));
        Assert.Equal((55u, 55u), (baseStrength, currentStrength));
        Assert.True(stats.TryVital(5, out var cur, out var baseMax, out var max, out var vitae));
        Assert.Equal(100u, cur);
        Assert.Equal(55u, baseMax); // no vital ranks: the formula over the base Self
        Assert.Equal(50u, max); // the vitae halves the enchanted formula (100 -> 50)
        Assert.Equal(-27, vitae); // the vitae alone takes 27.5 off the base 55, rounded half up: 28 - 55
        Assert.False(new StatValues(q).TryAttribute(9, out _, out _));
    }

    [Fact]
    public void A_vital_at_28_base_with_a_plus_22_buff_has_the_maximum_50()
    {
        var q = FreshCharacter([Add(7, 100, AdditiveSecondary, 1, 22f)], health: 50);
        Assert.True(q.TryGetVitalLevels(1, out var current, out var max));
        Assert.Equal((50u, 50u), (current, max));
    }

    [Fact]
    public void Attributes_enchanted_to_100_give_the_live_gates_50_100_100()
    {
        // The live gate's character: raw Endurance and Self of 55, currents 50, 100 and 100. Endurance and Self buffed by +45 give maximums of exactly those.
        ImmutableArray<EnchantmentDesc> enchantments = [Add(7, 100, AdditiveAttribute, 2, 45f), Add(8, 100, AdditiveAttribute, 6, 45f)];
        var q = FreshCharacter(enchantments, 50, 100, 100);
        foreach (var (id, expected) in new[] { (1u, 50u), (3u, 100u), (5u, 100u) })
        {
            Assert.True(q.TryGetVitalLevels(id, out var current, out var max));
            Assert.Equal((expected, expected), (current, max));
        }
    }

    [Fact]
    public void The_maximum_adds_the_gear_health_property_and_the_vitals_ranks_and_start()
    {
        var q = FreshCharacter([]);
        q.Ints[PlayerQualities.GearMaxHealthProperty] = 12;
        q.Vitals[1] = new VitalDesc(1, 3, 2, 0, 28);
        Assert.True(q.TryGetVitalLevels(1, out _, out var health));
        Assert.Equal(28u + 3 + 2 + 12, health);
        Assert.True(q.TryGetVitalLevels(3, out _, out var stamina));
        Assert.Equal(55u, stamina);
    }

    [Fact]
    public void A_stand_in_description_with_the_live_shaped_registry_keeps_it_and_applies_its_top_layers()
    {
        var registry = StandInPlayerStats.LiveShapedEnchantments();
        Assert.Equal(138, registry.Length);
        var q = FreshCharacter(registry);
        // Endurance 55 + the top layer (+20) of its four-layer buff = 75, so health (75 + 1) / 2 = 38 and stamina 75 + the top stamina layer (+20) = 95; mana is untouched.
        Assert.True(q.TryGetVitalLevels(1, out _, out var health));
        Assert.True(q.TryGetVitalLevels(3, out _, out var stamina));
        Assert.True(q.TryGetVitalLevels(5, out _, out var mana));
        Assert.Equal((38u, 95u, 55u), (health, stamina, mana));
    }

    [Fact]
    public void The_description_reducer_keeps_the_registry_and_a_new_description_replaces_it()
    {
        IInboundMessageHandler? handler = null;
        using var session = GameSession.Offline(h => handler = h, new ManualTimeProvider(), NullLogSink.Instance);
        var q = session.State.Self.Qualities;
        var context = new InboundContext(0, MessageQueue.SmartBox, 0);
        handler!.OnPlayerDescription(context, new GameEventHeader(1, 1, 0x0013), new PlayerDescriptionMsg { Enchantments = StandInPlayerStats.LiveShapedEnchantments() });
        Assert.Equal(138, q.Enchantments.Length);
        handler.OnPlayerDescription(context, new GameEventHeader(1, 2, 0x0013), new PlayerDescriptionMsg());
        Assert.Empty(q.Enchantments);
    }
}

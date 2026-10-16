using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages.Magic;

/// <summary>
/// <see cref="MagicCodecs"/>: the magic game events 0x02C1-0x02C8 and 0x0312 against bytes laid out from ACE's writers
/// (<see cref="AceM3Writers"/>). Every layout decodes to its fields; a truncated or over-long payload is refused; a
/// 02C2 carries the whole enchantment, the spell set id included when ACE writes it.
/// </summary>
public sealed class MagicCodecTests
{
    private const uint Player = 0x50000001;

    /// <summary>A real-looking Strength Self VI cast on the player: spell 0x0C8B (hypothetical id), layer 4, 2 hours.</summary>
    private static AceEnchantment Strength(ushort hasSpellSetId = 1, uint spellSetId = 0) =>
        new(SpellId: 0x0C8B, Layer: 4, SpellCategory: 0x0003, HasSpellSetId: hasSpellSetId, PowerLevel: 350, StartTime: -12.5, Duration: 7200.0,
            CasterGuid: Player, DegradeModifier: 0.25f, DegradeLimit: -666f, LastTimeDegraded: 0.0, StatModType: 0x00020101, StatModKey: 1,
            StatModValue: 35f, SpellSetId: spellSetId);

    [Fact]
    public void UpdateSpell_reads_the_spell_and_the_unused_layer()
    {
        var message = AceM3Writers.MagicUpdateSpell(Player, 5, 0x0C8B);

        Assert.Equal(16 + 4, message.Length);
        Assert.True(MagicCodecs.TryParseUpdateSpell(message, out var result));
        Assert.Equal(new MagicUpdateSpellEvent(0x0C8B, 0), result);
    }

    [Fact]
    public void UpdateEnchantment_reads_every_field_of_the_enchantment_and_the_spell_set_id()
    {
        var message = AceM3Writers.MagicUpdateEnchantment(Player, 6, Strength(1, 0x1234));

        Assert.Equal(16 + 64, message.Length);
        Assert.True(MagicCodecs.TryParseUpdateEnchantment(message, out var result));
        var e = result.Enchantment;
        Assert.Equal((ushort)0x0C8B, e.SpellId);
        Assert.Equal((ushort)4, e.Layer);
        Assert.Equal((ushort)3, e.SpellCategory);
        Assert.Equal((ushort)1, e.HasSpellSetId);
        Assert.Equal(350u, e.PowerLevel);
        Assert.Equal(-12.5, e.StartTime);
        Assert.Equal(7200.0, e.Duration);
        Assert.Equal(Player, e.Caster);
        Assert.Equal(0.25f, e.DegradeModifier);
        Assert.Equal(-666f, e.DegradeLimit);
        Assert.Equal(0.0, e.LastTimeDegraded);
        Assert.Equal(0x00020101u, e.StatModType);
        Assert.Equal(1u, e.StatModKey);
        Assert.Equal(35f, e.StatModValue);
        Assert.Equal(0x1234u, e.SpellSetId);
        Assert.Equal(EnchantmentBucket.Additive, e.Bucket);
    }

    [Fact]
    public void UpdateEnchantment_without_a_spell_set_id_is_60_bytes()
    {
        var message = AceM3Writers.MagicUpdateEnchantment(Player, 6, Strength(0));

        Assert.Equal(16 + 60, message.Length);
        Assert.True(MagicCodecs.TryParseUpdateEnchantment(message, out var result));
        Assert.Equal((ushort)0, result.Enchantment.HasSpellSetId);
        Assert.Equal(0u, result.Enchantment.SpellSetId);
    }

    [Theory]
    [InlineData(0x029A, 0x00CC, 0x00000801u, EnchantmentBucket.Vitae)]
    [InlineData(0x0C8B, 0x8000, 0x00000000u, EnchantmentBucket.Cooldown)]
    [InlineData(0x0C8B, 0x0003, 0x00004101u, EnchantmentBucket.Multiplicative)]
    [InlineData(0x0C8B, 0x0003, 0x00000101u, EnchantmentBucket.Additive)]
    public void UpdateEnchantment_derives_the_bucket_the_wire_does_not_carry(int spell, int category, uint statModType, EnchantmentBucket expected)
    {
        var e = Strength() with { SpellId = (ushort)spell, SpellCategory = (ushort)category, StatModType = statModType };

        Assert.True(MagicCodecs.TryParseUpdateEnchantment(AceM3Writers.MagicUpdateEnchantment(Player, 1, e), out var result));
        Assert.Equal(expected, result.Enchantment.Bucket);
    }

    [Fact]
    public void RemoveEnchantment_and_DispelEnchantment_read_the_spell_and_layer()
    {
        Assert.True(MagicCodecs.TryParseRemoveEnchantment(AceM3Writers.MagicRemoveEnchantment(Player, 7, 0x0C8B, 4), out var removed));
        Assert.Equal(new MagicRemoveEnchantmentEvent(0x0C8B, 4), removed);

        Assert.True(MagicCodecs.TryParseDispelEnchantment(AceM3Writers.MagicDispelEnchantment(Player, 8, 0x0C8B, 4), out var dispelled));
        Assert.Equal(new MagicDispelEnchantmentEvent(0x0C8B, 4), dispelled);
    }

    [Fact]
    public void The_list_forms_read_their_counts_and_entries()
    {
        var spells = new (ushort Spell, ushort Layer)[] { (0x0C8B, 4), (0x0123, 1), (0x029A, 0) };

        Assert.True(MagicCodecs.TryParseRemoveMultipleEnchantments(AceM3Writers.MagicRemoveMultipleEnchantments(Player, 9, spells), out var removed));
        Assert.Equal<LayeredSpellDesc>([new LayeredSpellDesc(0x0C8B, 4), new LayeredSpellDesc(0x0123, 1), new LayeredSpellDesc(0x029A, 0)], removed.Spells.ToArray());

        Assert.True(MagicCodecs.TryParseDispelMultipleEnchantments(AceM3Writers.MagicDispelMultipleEnchantments(Player, 10, spells), out var dispelled));
        Assert.Equal<LayeredSpellDesc>(removed.Spells.ToArray(), dispelled.Spells.ToArray());

        Assert.True(MagicCodecs.TryParseDispelMultipleEnchantments(AceM3Writers.MagicDispelMultipleEnchantments(Player, 11, []), out var none));
        Assert.Empty(none.Spells);

        var enchantments = new[] { Strength(1, 7), Strength(0) with { Layer = 5, SpellId = 0x0100 } };
        Assert.True(MagicCodecs.TryParseUpdateMultipleEnchantments(AceM3Writers.MagicUpdateMultipleEnchantments(Player, 12, enchantments), out var updated));
        Assert.Equal(2, updated.Enchantments.Length);
        Assert.Equal((0x0C8B, 4, 7u), (updated.Enchantments[0].SpellId, updated.Enchantments[0].Layer, updated.Enchantments[0].SpellSetId));
        Assert.Equal((0x0100, 5, 0u), (updated.Enchantments[1].SpellId, updated.Enchantments[1].Layer, updated.Enchantments[1].SpellSetId));
    }

    [Fact]
    public void The_purges_have_no_payload()
    {
        Assert.True(MagicCodecs.HasNoPayload(AceM3Writers.MagicPurgeEnchantments(Player, 13)));
        Assert.True(MagicCodecs.HasNoPayload(AceM3Writers.MagicPurgeBadEnchantments(Player, 14)));
        Assert.False(MagicCodecs.HasNoPayload([.. AceM3Writers.MagicPurgeEnchantments(Player, 13), 0]));
    }

    [Fact]
    public void A_truncated_or_over_long_payload_is_refused()
    {
        var update = AceM3Writers.MagicUpdateEnchantment(Player, 1, Strength());
        Assert.False(MagicCodecs.TryParseUpdateEnchantment(update[..^1], out _));
        Assert.False(MagicCodecs.TryParseUpdateEnchantment([.. update, 0], out _));

        var pair = AceM3Writers.MagicRemoveEnchantment(Player, 1, 1, 1);
        Assert.False(MagicCodecs.TryParseRemoveEnchantment(pair[..^1], out _));
        Assert.False(MagicCodecs.TryParseRemoveEnchantment([.. pair, 0], out _));
        Assert.False(MagicCodecs.TryParseUpdateSpell(pair[..^2], out _));

        // A count larger than the bytes behind it never allocates and is refused.
        var list = AceM3Writers.MagicDispelMultipleEnchantments(Player, 1, [(1, 1)]);
        var lying = list.ToArray();
        lying[16] = 0xFF;
        Assert.False(MagicCodecs.TryParseDispelMultipleEnchantments(lying, out _));
        Assert.False(MagicCodecs.TryParseDispelMultipleEnchantments(list[..^1], out _));
        Assert.False(MagicCodecs.TryParseUpdateMultipleEnchantments(AceM3Writers.MagicUpdateMultipleEnchantments(Player, 1, [Strength()])[..^1], out _));
    }
}

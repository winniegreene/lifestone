using System.Collections.Immutable;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// The magic game events (M3 1.2 s0), each the payload of an F7B0 envelope (<see cref="GameEventEnvelope"/>), laid out as
/// ACE's <c>GameEvent/Events/GameEventMagic*.cs</c> writers lay them out (<c>ACE.Server/Network/GameEvent/Events/</c>;
/// the enchantment structure in <c>Network/Structure/Enchantment.cs</c> and <c>LayeredSpell.cs</c>). Every parser takes
/// the whole F7B0 message and accepts it only when every byte of the payload was read. The enchantment itself is read by
/// the PlayerDescription codec's reader (<see cref="PlayerDescriptionCodec.TryReadEnchantment"/>), the same 60 or 64 bytes.
/// </summary>
/// <remarks>
/// <para>The layouts, and the retail dispatcher that reads each (<c>CM_Magic::DispatchUI_*</c>, then the client system's
/// <c>ClientMagicSystem::Handle_Magic__*</c>; both 11.6096, with 11.4186 where the symbol table has it):</para>
/// <list type="bullet">
/// <item>0x02C1 MagicUpdateSpell: u16 spell, u16 layer (ACE <c>GameEventMagicUpdateSpell</c>, <c>Player_Spells.cs:69</c>;
/// <c>CM_Magic::DispatchUI_UpdateSpell</c> 0x006A3E30, 11.4186 0x006A3010; <c>ClientMagicSystem::Handle_Magic__UpdateSpell</c>
/// 0x00568590);</item>
/// <item>0x02C2 MagicUpdateEnchantment: one enchantment (ACE <c>GameEventMagicUpdateEnchantment</c>;
/// <c>CM_Magic::DispatchUI_UpdateEnchantment</c> 0x006A4110, 11.4186 0x006A32F0; <c>Handle_Magic__UpdateEnchantment</c>
/// 0x005686D0);</item>
/// <item>0x02C3 MagicRemoveEnchantment: u16 spell, u16 layer (<c>DispatchUI_RemoveEnchantment</c> 0x006A3DD0, 11.4186
/// 0x006A2FB0; <c>Handle_Magic__RemoveEnchantment</c> 0x005696F0);</item>
/// <item>0x02C4 MagicUpdateMultipleEnchantments: u32 count, then that many enchantments (ACE writes the list form in
/// <c>GameEventMagicUpdateMultipleEnchantments</c> but never sends the event; <c>DispatchUI_UpdateMultipleEnchantments</c>
/// 0x006A47B0, 11.4186 0x006A3990; <c>Handle_Magic__UpdateMultipleEnchantments</c> 0x00568780);</item>
/// <item>0x02C5 MagicRemoveMultipleEnchantments: u32 count, then u16 spell and u16 layer each (ACE never sends it;
/// <c>DispatchUI_RemoveMultipleEnchantments</c> 0x006A4720, 11.4186 0x006A3900; <c>Handle_Magic__RemoveMultipleEnchantments</c>
/// 0x005697D0);</item>
/// <item>0x02C6 MagicPurgeEnchantments: no payload (<c>DispatchUI_PurgeEnchantments</c> 0x006A3DA0, 11.4186 0x006A2F80;
/// <c>Handle_Magic__PurgeEnchantments</c> 0x00568810);</item>
/// <item>0x02C7 MagicDispelEnchantment: u16 spell, u16 layer (<c>DispatchUI_DispelEnchantment</c> 0x006A3D40, 11.4186
/// 0x006A2F20; <c>Handle_Magic__DispelEnchantment</c> 0x005698B0);</item>
/// <item>0x02C8 MagicDispelMultipleEnchantments: u32 count, then u16 spell and u16 layer each (ACE's two overloads write
/// the same pairs; <c>DispatchUI_DispelMultipleEnchantments</c> 0x006A4690, 11.4186 0x006A3870;
/// <c>Handle_Magic__DispelMultipleEnchantments</c> 0x005698C0);</item>
/// <item>0x0312 MagicPurgeBadEnchantments: no payload (<c>DispatchUI_PurgeBadEnchantments</c> 0x006A3D70, 11.4186
/// 0x006A2F50; <c>Handle_Magic__PurgeBadEnchantments</c> 0x005688A0).</item>
/// </list>
/// <para><b>The bucket.</b> The wire does not say which registry list an enchantment belongs to, so the codec derives
/// <see cref="EnchantmentDesc.Bucket"/> from the fields: the vitae spell (ACE's <c>SpellId.Vitae</c>, 0x029A) is
/// <see cref="EnchantmentBucket.Vitae"/>; ACE's cooldown category (<c>EnchantmentManager.SpellCategory_Cooldown</c>,
/// 0x8000) is <see cref="EnchantmentBucket.Cooldown"/>; a stat mod with the multiplicative bit (0x4000, as
/// <c>EnchantmentRules</c> reads it) is <see cref="EnchantmentBucket.Multiplicative"/>; everything else is
/// <see cref="EnchantmentBucket.Additive"/>. TODO(m3-enchant-bucket): lane s1 confirms the rule against retail's
/// <c>CEnchantmentRegistry</c> add path.</para>
/// </remarks>
internal static class MagicCodecs
{
    /// <summary>The vitae spell id (ACE <c>SpellId.Vitae</c>; also the description fixture's vitae).</summary>
    public const ushort VitaeSpellId = 0x029A;

    /// <summary>ACE's cooldown spell category (<c>EnchantmentManager.SpellCategory_Cooldown</c>).</summary>
    public const ushort CooldownCategory = 0x8000;

    /// <summary>The multiplicative bit of a stat mod type (<c>EnchantmentRules.Multiplicative</c>).</summary>
    public const uint MultiplicativeStatMod = 0x4000;

    /// <summary>The bucket a live enchantment goes in (see the remarks).</summary>
    public static EnchantmentBucket BucketOf(in EnchantmentDesc enchantment)
    {
        if (enchantment.SpellId == VitaeSpellId)
        {
            return EnchantmentBucket.Vitae;
        }

        if (enchantment.SpellCategory == CooldownCategory)
        {
            return EnchantmentBucket.Cooldown;
        }

        return (enchantment.StatModType & MultiplicativeStatMod) != 0 ? EnchantmentBucket.Multiplicative : EnchantmentBucket.Additive;
    }

    /// <summary>0x02C1 MagicUpdateSpell.</summary>
    public static bool TryParseUpdateSpell(ReadOnlySpan<byte> message, out MagicUpdateSpellEvent result)
    {
        result = default;
        if (!TryReadPair(message, out var spell, out var layer))
        {
            return false;
        }

        result = new MagicUpdateSpellEvent(spell, layer);
        return true;
    }

    /// <summary>0x02C3 MagicRemoveEnchantment.</summary>
    public static bool TryParseRemoveEnchantment(ReadOnlySpan<byte> message, out MagicRemoveEnchantmentEvent result)
    {
        result = default;
        if (!TryReadPair(message, out var spell, out var layer))
        {
            return false;
        }

        result = new MagicRemoveEnchantmentEvent(spell, layer);
        return true;
    }

    /// <summary>0x02C7 MagicDispelEnchantment.</summary>
    public static bool TryParseDispelEnchantment(ReadOnlySpan<byte> message, out MagicDispelEnchantmentEvent result)
    {
        result = default;
        if (!TryReadPair(message, out var spell, out var layer))
        {
            return false;
        }

        result = new MagicDispelEnchantmentEvent(spell, layer);
        return true;
    }

    /// <summary>0x02C2 MagicUpdateEnchantment.</summary>
    public static bool TryParseUpdateEnchantment(ReadOnlySpan<byte> message, out MagicUpdateEnchantmentEvent result)
    {
        result = default;
        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!PlayerDescriptionCodec.TryReadEnchantment(ref reader, EnchantmentBucket.Additive, out var enchantment) || !reader.IsAtEnd)
        {
            return false;
        }

        result = new MagicUpdateEnchantmentEvent(enchantment with { Bucket = BucketOf(enchantment) });
        return true;
    }

    /// <summary>0x02C4 MagicUpdateMultipleEnchantments.</summary>
    public static bool TryParseUpdateMultipleEnchantments(ReadOnlySpan<byte> message, out MagicUpdateMultipleEnchantmentsEvent result)
    {
        result = default;
        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!reader.TryReadCount(PlayerDescriptionCodec.EnchantmentSize, out var count))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<EnchantmentDesc>(count);
        for (var i = 0; i < count; i++)
        {
            if (!PlayerDescriptionCodec.TryReadEnchantment(ref reader, EnchantmentBucket.Additive, out var enchantment))
            {
                return false;
            }

            builder.Add(enchantment with { Bucket = BucketOf(enchantment) });
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        result = new MagicUpdateMultipleEnchantmentsEvent(builder.MoveToImmutable());
        return true;
    }

    /// <summary>0x02C5 MagicRemoveMultipleEnchantments.</summary>
    public static bool TryParseRemoveMultipleEnchantments(ReadOnlySpan<byte> message, out MagicRemoveMultipleEnchantmentsEvent result)
    {
        result = default;
        if (!TryReadSpellList(message, out var spells))
        {
            return false;
        }

        result = new MagicRemoveMultipleEnchantmentsEvent(spells);
        return true;
    }

    /// <summary>0x02C8 MagicDispelMultipleEnchantments.</summary>
    public static bool TryParseDispelMultipleEnchantments(ReadOnlySpan<byte> message, out MagicDispelMultipleEnchantmentsEvent result)
    {
        result = default;
        if (!TryReadSpellList(message, out var spells))
        {
            return false;
        }

        result = new MagicDispelMultipleEnchantmentsEvent(spells);
        return true;
    }

    /// <summary>True when <paramref name="message"/> (an F7B0 message) has no payload: 0x02C6 and 0x0312.</summary>
    public static bool HasNoPayload(ReadOnlySpan<byte> message) => message.Length == GameEventEnvelope.HeaderSize;

    private static bool TryReadPair(ReadOnlySpan<byte> message, out ushort spell, out ushort layer)
    {
        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        spell = 0;
        layer = 0;
        return reader.TryReadU16(out spell) && reader.TryReadU16(out layer) && reader.IsAtEnd;
    }

    private static bool TryReadSpellList(ReadOnlySpan<byte> message, out ImmutableArray<LayeredSpellDesc> spells)
    {
        spells = [];
        var reader = new WireReader(message, GameEventEnvelope.HeaderSize);
        if (!reader.TryReadCount(2 * sizeof(ushort), out var count))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<LayeredSpellDesc>(count);
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryReadU16(out var spell) || !reader.TryReadU16(out var layer))
            {
                return false;
            }

            builder.Add(new LayeredSpellDesc(spell, layer));
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        spells = builder.MoveToImmutable();
        return true;
    }
}

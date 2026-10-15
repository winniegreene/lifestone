// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/PrivateUpdateVital.cs, PrivateUpdateAttribute.cs,
// PrivateUpdateSkill.cs, PrivateUpdatePropertyInt.cs and PrivateUpdatePropertyInt64.cs (MIT; Copyright (c) 2026 Erik
// Nihlén and OpenAC contributors) with changes: one internal static PrivateUpdateCodec over N2's WireReader that decodes
// all six messages into the N0 struct PrivateUpdateMsg without allocating, keeps the sequence byte (OpenAC's int and
// int64 parsers dropped it) and accepts a message only when it is exactly as long as its layout (OpenAC accepted
// trailing bytes); the skill shares PlayerDescriptionCodec's reader.

using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// The player's own PrivateUpdate messages that M2 decodes (m2-plan §3.3 N10; owner decision D16), as ACE's
/// <c>GameMessagePrivateUpdate*</c> writers lay them out: the opcode, a u8 update sequence, then (unaligned) the id and
/// the value. Retail reads each in <c>CM_Qualities</c>:
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>0x02CD PropertyInt: u32 property, i32 value (<c>DispatchUI_PrivateUpdateInt</c>, 11.4186 0x006AEA90, 11.6096
/// 0x006AF960);</item>
/// <item>0x02CF PropertyInt64: u32 property, i64 value (<c>DispatchUI_PrivateUpdateInt64</c>, 11.4186 0x006AEAD0, 11.6096
/// 0x006AF9A0);</item>
/// <item>0x02DD Skill: u32 skill, then the 28 bytes of <c>Skill::UnPack</c> (<c>DispatchUI_PrivateUpdateSkill</c>, 11.4186
/// 0x006AEB20, 11.6096 0x006AF9F0);</item>
/// <item>0x02E3 Attribute: u32 attribute, ranks, start, experience (<c>DispatchUI_PrivateUpdateAttribute</c>, 11.4186
/// 0x006AE800, 11.6096 0x006AF6D0);</item>
/// <item>0x02E7 Vital (retail's Attribute2nd): u32 vital (1, 3 or 5), ranks, start, experience, current
/// (<c>DispatchUI_PrivateUpdateAttribute2nd</c>, 11.4186 0x006AE8A0, 11.6096 0x006AF770);</item>
/// <item>0x02E9 Attribute2ndLevel: u32 vital (2, 4 or 6, the current-value ids), current
/// (<c>DispatchUI_PrivateUpdateAttribute2ndLevel</c>, 11.4186 0x006AE930, 11.6096 0x006AF800).</item>
/// </list>
/// </remarks>
internal static class PrivateUpdateCodec
{
    /// <summary>True for the six opcodes this codec decodes.</summary>
    public static bool Decodes(uint opcode) => opcode is MessageOpcodes.PrivateUpdatePropertyInt or MessageOpcodes.PrivateUpdatePropertyInt64
        or MessageOpcodes.PrivateUpdateSkill or MessageOpcodes.PrivateUpdateAttribute or MessageOpcodes.PrivateUpdateVital
        or MessageOpcodes.PrivateUpdateAttribute2ndLevel;

    /// <summary>Decodes <paramref name="message"/> (one of the six, by its opcode); false when it is not exactly that layout.</summary>
    public static bool TryParse(ReadOnlySpan<byte> message, out PrivateUpdateMsg update)
    {
        update = default;
        var reader = new WireReader(message);
        if (!reader.TryReadU32(out var opcode) || !Decodes(opcode) || !reader.TryReadU8(out var sequence) || !reader.TryReadU32(out var key))
        {
            return false;
        }

        var kind = (PrivateUpdateKind)opcode;
        switch (kind)
        {
            case PrivateUpdateKind.PropertyInt:
                if (!reader.TryReadI32(out var intValue))
                {
                    return false;
                }

                update = new PrivateUpdateMsg(kind, sequence, key, intValue, default, default, default);
                break;

            case PrivateUpdateKind.PropertyInt64:
                if (!reader.TryReadI64(out var longValue))
                {
                    return false;
                }

                update = new PrivateUpdateMsg(kind, sequence, key, longValue, default, default, default);
                break;

            case PrivateUpdateKind.Skill:
                if (!PlayerDescriptionCodec.TryReadSkill(ref reader, key, out var skill))
                {
                    return false;
                }

                update = new PrivateUpdateMsg(kind, sequence, key, 0, default, default, skill);
                break;

            case PrivateUpdateKind.Attribute:
                if (!reader.TryReadU32(out var ranks) || !reader.TryReadU32(out var start) || !reader.TryReadU32(out var experience))
                {
                    return false;
                }

                update = new PrivateUpdateMsg(kind, sequence, key, 0, new AttributeDesc(key, ranks, start, experience), default, default);
                break;

            case PrivateUpdateKind.Vital:
                if (!reader.TryReadU32(out var vitalRanks) || !reader.TryReadU32(out var vitalStart) || !reader.TryReadU32(out var vitalExperience)
                    || !reader.TryReadU32(out var current))
                {
                    return false;
                }

                update = new PrivateUpdateMsg(kind, sequence, key, 0, default, new VitalDesc(key, vitalRanks, vitalStart, vitalExperience, current), default);
                break;

            default:  // PrivateUpdateKind.VitalCurrent
                if (!reader.TryReadU32(out var level))
                {
                    return false;
                }

                update = new PrivateUpdateMsg(kind, sequence, key, level, default, default, default);
                break;
        }

        if (!reader.IsAtEnd)
        {
            update = default;
            return false;
        }

        return true;
    }

    /// <summary>The malformed reason for each opcode, naming its layout.</summary>
    public static string MalformedReason(uint opcode) => opcode switch
    {
        MessageOpcodes.PrivateUpdatePropertyInt => "02CD PrivateUpdatePropertyInt: not exactly a u8 sequence, a u32 property and an i32",
        MessageOpcodes.PrivateUpdatePropertyInt64 => "02CF PrivateUpdatePropertyInt64: not exactly a u8 sequence, a u32 property and an i64",
        MessageOpcodes.PrivateUpdateSkill => "02DD PrivateUpdateSkill: not exactly a u8 sequence, a u32 skill and its 28 bytes",
        MessageOpcodes.PrivateUpdateAttribute => "02E3 PrivateUpdateAttribute: not exactly a u8 sequence, a u32 attribute and three u32",
        MessageOpcodes.PrivateUpdateVital => "02E7 PrivateUpdateVital: not exactly a u8 sequence, a u32 vital and four u32",
        _ => "02E9 PrivateUpdateAttribute2ndLevel: not exactly a u8 sequence, a u32 vital and a u32",
    };
}

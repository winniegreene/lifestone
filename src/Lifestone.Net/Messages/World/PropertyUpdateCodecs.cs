using Lifestone.Net.Types;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// A public property update (GM 0x02D0 Int64, 0x02D2 Bool, 0x02D4 Float, 0x02D6 String, 0x02D8 DataId; M5 6 w1): one property of an
/// object everyone near it is told about, as ACE's <c>GameMessagePublicUpdateProperty*</c> writes it. <typeparamref name="T"/> is the
/// property's value type: long, bool, double, string or uint (a data id).
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
/// <param name="Sequence">The u8 update sequence ACE stamps per object and property (<c>Sequences.GetNextSequence</c>).</param>
/// <param name="Object">The object's GUID.</param>
/// <param name="Key">The property id (ACE's <c>PropertyInt64</c>, <c>PropertyBool</c>, <c>PropertyFloat</c>, <c>PropertyString</c> or <c>PropertyDataId</c>).</param>
/// <param name="Value">The new value.</param>
public readonly record struct PublicPropertyUpdate<T>(byte Sequence, uint Object, uint Key, T Value);

/// <summary>
/// A private property update (GM 0x02D1 Bool, 0x02D9 InstanceId, 0x02D7 DataId; M5 6 w1): one property of the player's own object, as
/// ACE's <c>GameMessagePrivateUpdatePropertyBool</c>, <c>GameMessagePrivateUpdateInstanceID</c> and <c>GameMessagePrivateUpdateDataID</c>
/// write it. The object is always the player.
/// </summary>
/// <typeparam name="T">The value type: bool, or uint for an instance id or a data id.</typeparam>
/// <param name="Sequence">The u8 update sequence.</param>
/// <param name="Key">The property id.</param>
/// <param name="Value">The new value.</param>
public readonly record struct PrivatePropertyUpdate<T>(byte Sequence, uint Key, T Value);

/// <summary>
/// GM 0x02DB PrivateUpdatePosition (M5 6 w1): one of the player's stored positions (ACE's <c>PositionType</c>; the recordings hold one,
/// LastOutsideDeath) as <c>GameMessagePrivateUpdatePosition</c> writes it.
/// </summary>
/// <param name="Sequence">The u8 update sequence.</param>
/// <param name="PositionType">ACE's <c>PositionType</c> word.</param>
/// <param name="Position">The position (the 32-byte form of <see cref="PositionCodec"/>).</param>
public readonly record struct PrivatePositionUpdate(byte Sequence, uint PositionType, AcPosition Position);

/// <summary>
/// The property-update game messages ACE sends beyond the PrivateUpdate set N10 decodes (M5 6 w1, D31), each laid out as its ACE
/// writer lays it out (@95ef426d, <c>Network/GameMessages/Messages/</c>; ACE's opcodes are in <c>GameMessageOpcode.cs:13-28</c>).
/// Every parser takes the whole message (opcode first, so String16L padding is counted as on the wire) and accepts it only when every
/// byte was read.
/// <list type="bullet">
/// <item>0x02D0 PublicUpdatePropertyInt64: u8 sequence, u32 object, u32 key, i64 value (<c>GameMessagePublicUpdatePropertyInt64.cs:19-22</c>).</item>
/// <item>0x02D2 PublicUpdatePropertyBool: u8 sequence, u32 object, u32 key, u32 value, nonzero true (<c>GameMessagePublicUpdatePropertyBool.cs:20-23</c>).</item>
/// <item>0x02D4 PublicUpdatePropertyFloat: u8 sequence, u32 object, u32 key, f64 value (<c>GameMessagePublicUpdatePropertyFloat.cs:19-22</c>).</item>
/// <item>0x02D6 PublicUpdatePropertyString: u8 sequence, u32 KEY, u32 OBJECT, pad to 4, String16L value. The key comes before the
/// object here, the one public update that does (<c>GameMessagePublicUpdatePropertyString.cs:19-23</c>).</item>
/// <item>0x02D8 PublicUpdatePropertyDataId: u8 sequence, u32 object, u32 key, u32 value (<c>GameMessagePublicUpdateDataID.cs</c>).</item>
/// <item>0x02D1 PrivateUpdatePropertyBool: u8 sequence, u32 key, u32 value (<c>GameMessagePrivateUpdatePropertyBool.cs:11-13</c>).</item>
/// <item>0x02D9 PrivateUpdatePropertyInstanceId: u8 sequence, u32 key, u32 value (<c>GameMessagePrivateUpdateInstanceID.cs:11-13</c>).</item>
/// <item>0x02D7 PrivateUpdatePropertyDataId: u8 sequence, u32 key, u32 value (<c>GameMessagePrivateUpdateDataID.cs</c>).</item>
/// <item>0x02DB PrivateUpdatePosition: u8 sequence, u32 position type, a 32-byte position (<c>GameMessagePrivateUpdatePosition.cs:12-14</c>
/// through <c>Position.Serialize(writer)</c>, <c>Position.cs:361-377</c>, which writes the cell, three f32 and W, X, Y, Z).</item>
/// </list>
/// Retail's handlers are in <c>CM_Qualities</c> (11.6096): <c>DispatchUI_UpdateInt64</c> 0x006B0050, <c>DispatchUI_UpdateBool</c>
/// 0x006AFF00, <c>DispatchUI_UpdateFloat</c> 0x006AFF50, <c>DispatchUI_UpdateInstanceID</c> 0x006AFFB0, <c>DispatchUI_PrivateUpdateBool</c>
/// 0x006AF880, <c>DispatchUI_PrivateUpdateInstanceID</c> 0x006AF920, <c>DispatchUI_PrivateUpdateDataID</c> 0x006B01F0, <c>DispatchUI_UpdateDataID</c>
/// 0x006B02C0 and <c>DispatchUI_PrivateUpdatePosition</c> 0x006B0230; the string update's address is unknown (not in the symbol table). The recordings hold 0x02D9 (2,412) and 0x02DB (1) only; the others are specified
/// from ACE's writers, with tests built the way those writers build them.
/// </summary>
internal static class PropertyUpdateCodecs
{
    /// <summary>0x02D0 PublicUpdatePropertyInt64.</summary>
    public static bool TryParsePublicInt64(ReadOnlySpan<byte> message, out PublicPropertyUpdate<long> update)
    {
        update = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.PublicUpdatePropertyInt64)
            || !reader.TryReadU8(out var sequence)
            || !reader.TryReadU32(out var guid)
            || !reader.TryReadU32(out var key)
            || !reader.TryReadI64(out var value)
            || !reader.IsAtEnd)
        {
            return false;
        }

        update = new PublicPropertyUpdate<long>(sequence, guid, key, value);
        return true;
    }

    /// <summary>0x02D2 PublicUpdatePropertyBool.</summary>
    public static bool TryParsePublicBool(ReadOnlySpan<byte> message, out PublicPropertyUpdate<bool> update)
    {
        update = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.PublicUpdatePropertyBool)
            || !reader.TryReadU8(out var sequence)
            || !reader.TryReadU32(out var guid)
            || !reader.TryReadU32(out var key)
            || !reader.TryReadU32(out var value)
            || !reader.IsAtEnd)
        {
            return false;
        }

        update = new PublicPropertyUpdate<bool>(sequence, guid, key, value != 0);
        return true;
    }

    /// <summary>0x02D4 PublicUpdatePropertyFloat.</summary>
    public static bool TryParsePublicFloat(ReadOnlySpan<byte> message, out PublicPropertyUpdate<double> update)
    {
        update = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.PublicUpdatePropertyFloat)
            || !reader.TryReadU8(out var sequence)
            || !reader.TryReadU32(out var guid)
            || !reader.TryReadU32(out var key)
            || !reader.TryReadF64(out var value)
            || !reader.IsAtEnd)
        {
            return false;
        }

        update = new PublicPropertyUpdate<double>(sequence, guid, key, value);
        return true;
    }

    /// <summary>0x02D6 PublicUpdatePropertyString: the key is read before the object, as ACE writes it.</summary>
    public static bool TryParsePublicString(ReadOnlySpan<byte> message, out PublicPropertyUpdate<string> update)
    {
        update = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.PublicUpdatePropertyString)
            || !reader.TryReadU8(out var sequence)
            || !reader.TryReadU32(out var key)
            || !reader.TryReadU32(out var guid)
            || !reader.TryAlign()
            || !reader.TryReadString16L(out var value)
            || !reader.IsAtEnd)
        {
            return false;
        }

        update = new PublicPropertyUpdate<string>(sequence, guid, key, value);
        return true;
    }

    /// <summary>0x02D8 PublicUpdatePropertyDataId.</summary>
    public static bool TryParsePublicDataId(ReadOnlySpan<byte> message, out PublicPropertyUpdate<uint> update)
    {
        update = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.PublicUpdatePropertyDataId)
            || !reader.TryReadU8(out var sequence)
            || !reader.TryReadU32(out var guid)
            || !reader.TryReadU32(out var key)
            || !reader.TryReadU32(out var value)
            || !reader.IsAtEnd)
        {
            return false;
        }

        update = new PublicPropertyUpdate<uint>(sequence, guid, key, value);
        return true;
    }

    /// <summary>0x02D1 PrivateUpdatePropertyBool.</summary>
    public static bool TryParsePrivateBool(ReadOnlySpan<byte> message, out PrivatePropertyUpdate<bool> update)
    {
        update = default;
        if (!TryParsePrivateWord(message, MessageOpcodes.PrivateUpdatePropertyBool, out var sequence, out var key, out var value))
        {
            return false;
        }

        update = new PrivatePropertyUpdate<bool>(sequence, key, value != 0);
        return true;
    }

    /// <summary>0x02D9 PrivateUpdatePropertyInstanceId.</summary>
    public static bool TryParsePrivateInstanceId(ReadOnlySpan<byte> message, out PrivatePropertyUpdate<uint> update)
    {
        update = default;
        if (!TryParsePrivateWord(message, MessageOpcodes.PrivateUpdatePropertyInstanceId, out var sequence, out var key, out var value))
        {
            return false;
        }

        update = new PrivatePropertyUpdate<uint>(sequence, key, value);
        return true;
    }

    /// <summary>0x02D7 PrivateUpdatePropertyDataId.</summary>
    public static bool TryParsePrivateDataId(ReadOnlySpan<byte> message, out PrivatePropertyUpdate<uint> update)
    {
        update = default;
        if (!TryParsePrivateWord(message, MessageOpcodes.PrivateUpdatePropertyDataId, out var sequence, out var key, out var value))
        {
            return false;
        }

        update = new PrivatePropertyUpdate<uint>(sequence, key, value);
        return true;
    }

    /// <summary>0x02DB PrivateUpdatePosition.</summary>
    public static bool TryParsePrivatePosition(ReadOnlySpan<byte> message, out PrivatePositionUpdate update)
    {
        update = default;
        var reader = new WireReader(message);
        if (!MessageCodec.TryStart(ref reader, MessageOpcodes.PrivateUpdatePosition)
            || !reader.TryReadU8(out var sequence)
            || !reader.TryReadU32(out var positionType)
            || !PositionCodec.TryRead(ref reader, out var position)
            || !reader.IsAtEnd)
        {
            return false;
        }

        update = new PrivatePositionUpdate(sequence, positionType, position);
        return true;
    }

    /// <summary>The message's reason when <paramref name="opcode"/> was malformed.</summary>
    public static string MalformedReason(uint opcode) => opcode switch
    {
        MessageOpcodes.PublicUpdatePropertyInt64 => "02D0 PublicUpdatePropertyInt64: not exactly a u8 sequence, a GUID, a property and an i64 value",
        MessageOpcodes.PrivateUpdatePropertyBool => "02D1 PrivateUpdatePropertyBool: not exactly a u8 sequence, a property and a u32 value",
        MessageOpcodes.PublicUpdatePropertyBool => "02D2 PublicUpdatePropertyBool: not exactly a u8 sequence, a GUID, a property and a u32 value",
        MessageOpcodes.PublicUpdatePropertyFloat => "02D4 PublicUpdatePropertyFloat: not exactly a u8 sequence, a GUID, a property and an f64 value",
        MessageOpcodes.PublicUpdatePropertyString => "02D6 PublicUpdatePropertyString: not exactly a u8 sequence, a property, a GUID, a pad and a String16L",
        MessageOpcodes.PrivateUpdatePropertyDataId => "02D7 PrivateUpdatePropertyDataId: not exactly a u8 sequence, a property and a u32 value",
        MessageOpcodes.PublicUpdatePropertyDataId => "02D8 PublicUpdatePropertyDataId: not exactly a u8 sequence, a GUID, a property and a u32 value",
        MessageOpcodes.PrivateUpdatePropertyInstanceId => "02D9 PrivateUpdatePropertyInstanceId: not exactly a u8 sequence, a property and a u32 value",
        MessageOpcodes.PrivateUpdatePosition => "02DB PrivateUpdatePosition: not exactly a u8 sequence, a u32 position type and a 32-byte position",
        _ => "property update: truncated or trailing bytes",
    };

    /// <summary>A private update of one u32 word: u8 sequence, u32 key, u32 value.</summary>
    private static bool TryParsePrivateWord(ReadOnlySpan<byte> message, uint opcode, out byte sequence, out uint key, out uint value)
    {
        sequence = 0;
        key = 0;
        value = 0;
        var reader = new WireReader(message);
        return MessageCodec.TryStart(ref reader, opcode)
            && reader.TryReadU8(out sequence)
            && reader.TryReadU32(out key)
            && reader.TryReadU32(out value)
            && reader.IsAtEnd;
    }
}

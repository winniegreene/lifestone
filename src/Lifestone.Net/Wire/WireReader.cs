using System.Buffers.Binary;

namespace Lifestone.Net.Wire;

/// <summary>
/// A forward-only reader over one reassembled message (docs/protocol.md §3: the u32 opcode, then the body), for the
/// codecs of N3, N7 and N10. Little-endian throughout.
/// </summary>
/// <remarks>
/// <para><b>It never throws on input.</b> Every read is a <c>Try</c> that returns false, leaves <see cref="Position"/>
/// where it was and sets its value to default when the message is too short, so a truncated or corrupt message becomes
/// a malformed report, never an exception (m2-plan §3.3 N3 fuzzing).</para>
/// <para><b>Counts are checked before anything is allocated.</b> <see cref="TryReadCount"/> refuses a count whose
/// smallest possible encoding would not fit in the bytes left, so a corrupt count cannot ask for a huge array.</para>
/// <para><b>Alignment is measured from the opcode.</b> The span the reader is built over starts at the message's first
/// byte, its u32 opcode, and <see cref="TryAlign"/> pads to a multiple of 4 from there, as ACE's <c>Align</c> and
/// retail's <c>PackObj::ALIGN_PTR</c> do (docs/protocol.md "Conventions"). A codec that starts mid-message passes the
/// whole message and a start offset, never a slice.</para>
/// </remarks>
internal ref struct WireReader
{
    private readonly ReadOnlySpan<byte> _message;
    private int _position;

    /// <summary>A reader over <paramref name="message"/> (from its opcode byte), positioned at <paramref name="start"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> is outside the message (a caller bug,
    /// not bad input).</exception>
    public WireReader(ReadOnlySpan<byte> message, int start = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start, message.Length);
        _message = message;
        _position = start;
    }

    /// <summary>The offset of the next byte, from the start of the message.</summary>
    public readonly int Position => _position;

    /// <summary>The bytes not read yet.</summary>
    public readonly int Remaining => _message.Length - _position;

    /// <summary>True when every byte has been read.</summary>
    public readonly bool IsAtEnd => _position == _message.Length;

    /// <summary>The bytes not read yet, as a view.</summary>
    public readonly ReadOnlySpan<byte> RemainingBytes => _message[_position..];

    /// <summary>Reads a byte.</summary>
    public bool TryReadU8(out byte value)
    {
        if (Remaining < 1)
        {
            value = 0;
            return false;
        }

        value = _message[_position++];
        return true;
    }

    /// <summary>Reads a u16.</summary>
    public bool TryReadU16(out ushort value)
    {
        if (Remaining < sizeof(ushort))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt16LittleEndian(_message[_position..]);
        _position += sizeof(ushort);
        return true;
    }

    /// <summary>Reads an i16.</summary>
    public bool TryReadI16(out short value)
    {
        var ok = TryReadU16(out var raw);
        value = unchecked((short)raw);
        return ok;
    }

    /// <summary>Reads a u32.</summary>
    public bool TryReadU32(out uint value)
    {
        if (Remaining < sizeof(uint))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(_message[_position..]);
        _position += sizeof(uint);
        return true;
    }

    /// <summary>Reads an i32.</summary>
    public bool TryReadI32(out int value)
    {
        var ok = TryReadU32(out var raw);
        value = unchecked((int)raw);
        return ok;
    }

    /// <summary>Reads a u64.</summary>
    public bool TryReadU64(out ulong value)
    {
        if (Remaining < sizeof(ulong))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt64LittleEndian(_message[_position..]);
        _position += sizeof(ulong);
        return true;
    }

    /// <summary>Reads an i64.</summary>
    public bool TryReadI64(out long value)
    {
        var ok = TryReadU64(out var raw);
        value = unchecked((long)raw);
        return ok;
    }

    /// <summary>Reads an f32.</summary>
    public bool TryReadF32(out float value)
    {
        var ok = TryReadU32(out var raw);
        value = BitConverter.UInt32BitsToSingle(raw);
        return ok;
    }

    /// <summary>Reads an f64.</summary>
    public bool TryReadF64(out double value)
    {
        var ok = TryReadU64(out var raw);
        value = BitConverter.UInt64BitsToDouble(raw);
        return ok;
    }

    /// <summary>Reads <paramref name="count"/> raw bytes as a view into the message.</summary>
    public bool TryReadBytes(int count, out ReadOnlySpan<byte> bytes)
    {
        if (count < 0 || Remaining < count)
        {
            bytes = default;
            return false;
        }

        bytes = _message.Slice(_position, count);
        _position += count;
        return true;
    }

    /// <summary>Skips <paramref name="count"/> bytes.</summary>
    public bool TrySkip(int count) => TryReadBytes(count, out _);

    /// <summary>
    /// Reads a String16L's bytes: a u16 byte count, the bytes, then zero to three pad bytes so that the record (count
    /// and bytes) is a multiple of 4 long (ACE <c>ReadString16L</c>/<c>WriteString16L</c>). The pad must be present.
    /// </summary>
    public bool TryReadString16LBytes(out ReadOnlySpan<byte> bytes)
    {
        var start = _position;
        if (!TryReadU16(out var length))
        {
            bytes = default;
            return false;
        }

        var padded = length + Pad(sizeof(ushort) + length);
        if (Remaining < padded)
        {
            _position = start;
            bytes = default;
            return false;
        }

        bytes = _message.Slice(_position, length);
        _position += padded;
        return true;
    }

    /// <summary>Reads a String16L and decodes it as CP1252 (<see cref="Cp1252"/>); an empty string allocates nothing.</summary>
    public bool TryReadString16L(out string value)
    {
        if (!TryReadString16LBytes(out var bytes))
        {
            value = string.Empty;
            return false;
        }

        value = Cp1252.GetString(bytes);
        return true;
    }

    /// <summary>
    /// Reads a PackedDword: one u16 for a value up to 0x7FFF, otherwise two u16s, the first holding the high half with
    /// bit 15 set and the second the low half (ACE <c>WritePackedDword</c>; docs/protocol.md "Conventions").
    /// </summary>
    public bool TryReadPackedDword(out uint value)
    {
        var start = _position;
        if (!TryReadU16(out var first))
        {
            value = 0;
            return false;
        }

        if ((first & 0x8000) == 0)
        {
            value = first;
            return true;
        }

        if (!TryReadU16(out var second))
        {
            _position = start;
            value = 0;
            return false;
        }

        value = ((uint)(first & 0x7FFF) << 16) | second;
        return true;
    }

    /// <summary>
    /// Reads a data id packed for a known dat type (palette 0x04000000, surface texture 0x05000000, GfxObj 0x01000000,
    /// icon 0x06000000) as retail's <c>Unpack_AsDataIDOfKnownType</c> (11.4186 0x006844F0, 11.6096 0x006854F0) reads it:
    /// one u16 when its bit 15 is clear, else two u16s, the first carrying bits 16-29 of the value under bit 15 (its bit
    /// 14 is dropped); the id is the type plus the value. So a packed 0 reads as the bare type id (0x06000000 for an
    /// icon), never 0. ACE's <c>WritePackedDwordOfKnownType</c> strips the type and writes 0 for a missing id, so "none"
    /// arrives as the bare type id, and consumers treat it as none. Lifestone.Data's <c>ObjDesc.TryUnpack</c> reads dat
    /// ObjDescs the same way.
    /// </summary>
    public bool TryReadKnownTypePackedDword(uint knownType, out uint value)
    {
        var start = _position;
        if (!TryReadU16(out var first))
        {
            value = 0;
            return false;
        }

        if ((first & 0x8000) == 0)
        {
            value = unchecked(knownType + first);
            return true;
        }

        if (!TryReadU16(out var second))
        {
            _position = start;
            value = 0;
            return false;
        }

        value = unchecked(knownType + (((uint)(first & 0x3FFF) << 16) | second));
        return true;
    }

    /// <summary>
    /// Reads a count (u32) of elements that each take at least <paramref name="minimumElementSize"/> bytes, refusing a
    /// count that could not fit in the bytes left, so a caller can size an array from it safely.
    /// </summary>
    public bool TryReadCount(int minimumElementSize, out int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumElementSize, 1);
        var start = _position;
        if (!TryReadU32(out var raw) || raw > (uint)(Remaining / minimumElementSize))
        {
            _position = start;
            count = 0;
            return false;
        }

        count = (int)raw;
        return true;
    }

    /// <summary>As <see cref="TryReadCount"/>, for a u16 count.</summary>
    public bool TryReadCount16(int minimumElementSize, out int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumElementSize, 1);
        var start = _position;
        if (!TryReadU16(out var raw) || raw > Remaining / minimumElementSize)
        {
            _position = start;
            count = 0;
            return false;
        }

        count = raw;
        return true;
    }

    /// <summary>
    /// Skips the zero to three pad bytes up to the next multiple of 4, measured from the message's opcode byte. The pad
    /// must be present (ACE's <c>Align</c> writes it even at the end of a message).
    /// </summary>
    public bool TryAlign() => TrySkip(Pad(_position));

    /// <summary>The pad bytes that bring <paramref name="length"/> up to a multiple of 4.</summary>
    private static int Pad(int length) => (4 - (length & 3)) & 3;
}

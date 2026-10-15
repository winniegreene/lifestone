// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Packets/PacketWriter.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Packets; internal.
// N2: strings are CP1252, not ASCII (defect 3), with WriteAsciiString16L for the fields that must be ASCII; the
// String32L prefix byte holds the length instead of 0; the string writers are also static span writers, for
// LoginRequest's pooled buffer. N3: the span String16L writer is internal, for the message codecs; WriteInt32 added.
// N5: Reset, so the session reuses one writer.

using System.Buffers.Binary;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Packets;

internal sealed class PacketWriter
{
    private byte[] _buffer;
    private int _position;

    public PacketWriter(int initialCapacity = 256)
    {
        _buffer = new byte[initialCapacity];
        _position = 0;
    }

    public int Position => _position;

    /// <summary>Forgets what was written, keeping the buffer (N5: the session reuses one writer for its sends).</summary>
    public void Reset() => _position = 0;

    /// <summary>Bytes written so far, as a freshly-allocated array.</summary>
    public byte[] ToArray()
    {
        var copy = new byte[_position];
        Array.Copy(_buffer, copy, _position);
        return copy;
    }

    /// <summary>Bytes written so far, as a span view over the internal buffer.
    /// Do not hold on to this after more writes — the buffer may be reallocated.</summary>
    public ReadOnlySpan<byte> AsSpan() => _buffer.AsSpan(0, _position);

    private void EnsureCapacity(int additional)
    {
        int required = _position + additional;
        if (required <= _buffer.Length) return;
        int newSize = _buffer.Length;
        while (newSize < required) newSize *= 2;
        var bigger = new byte[newSize];
        Array.Copy(_buffer, bigger, _position);
        _buffer = bigger;
    }

    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_position++] = value;
    }

    public void WriteUInt16(ushort value)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_position), value);
        _position += 2;
    }

    public void WriteUInt32(uint value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_position), value);
        _position += 4;
    }

    public void WriteInt32(int value) => WriteUInt32(unchecked((uint)value));

    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        EnsureCapacity(bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_position));
        _position += bytes.Length;
    }

    public void WriteFloat(float value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteSingleLittleEndian(_buffer.AsSpan(_position), value);
        _position += 4;
    }

    public void WriteDouble(double value)
    {
        EnsureCapacity(8);
        BinaryPrimitives.WriteDoubleLittleEndian(_buffer.AsSpan(_position), value);
        _position += 8;
    }

    /// <summary>Pad with zeros so the buffer length is a multiple of 4.</summary>
    public void AlignTo4()
    {
        int padding = (4 - (_position & 3)) & 3;
        EnsureCapacity(padding);
        for (int i = 0; i < padding; i++) _buffer[_position++] = 0;
    }

    public void Pad(int count)
    {
        if (count <= 0) return;
        EnsureCapacity(count);
        for (int i = 0; i < count; i++) _buffer[_position++] = 0;
    }

    /// <summary>
    /// Write a String16L: u16 byte count + CP1252 bytes + pad-to-4 from the
    /// start of the length prefix (defect 3: OpenAC wrote ASCII, which turned
    /// every other character into '?'; ACE writes and reads CP1252).
    /// </summary>
    /// <exception cref="ArgumentException">A character has no CP1252 byte, or
    /// the string is longer than a u16 count can say.</exception>
    public void WriteString16L(string value) =>
        WriteString16L(value.AsSpan(), nameof(value));

    /// <summary>
    /// Write a String16L that other fields follow, which must be ASCII
    /// (<see cref="AsciiGuard"/>): the account, a CharacterCreate name, a Tell
    /// target and its text.
    /// </summary>
    /// <exception cref="ArgumentException">A character is above 0x7F; the
    /// message names <paramref name="field"/>, never the text.</exception>
    public void WriteAsciiString16L(ReadOnlySpan<char> value, string field)
    {
        AsciiGuard.Require(value, field);
        WriteString16L(value, field);
    }

    /// <summary>
    /// Write a CP1252 String16L from a span (N3: the message codecs);
    /// <paramref name="field"/> names it in an exception, never the text.
    /// </summary>
    internal void WriteString16L(ReadOnlySpan<char> value, string field)
    {
        int size = String16LSize(value, field);
        EnsureCapacity(size);
        _position += WriteString16L(_buffer.AsSpan(_position, size), value, field);
    }

    /// <summary>
    /// Write a String32L: u32 (prefix + byte count), the 1-byte prefix
    /// holding the byte count, the CP1252 bytes, then pad-to-4 from the start
    /// of the u32. OpenAC wrote 0 in the prefix; ACE skips the byte, and its own
    /// comment calls it a packed word of the length, so Lifestone writes the
    /// length (docs/protocol.md "Conventions"). The only String32L on the wire
    /// is the LoginRequest password, which <see cref="LoginRequest"/> writes
    /// straight into a pooled buffer with <see cref="WriteString32L(Span{byte}, ReadOnlySpan{char}, string)"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Longer than 255 characters, or a
    /// character has no CP1252 byte.</exception>
    public void WriteString32L(ReadOnlySpan<char> value)
    {
        int size = String32LSize(value, nameof(value));
        EnsureCapacity(size);
        _position += WriteString32L(_buffer.AsSpan(_position, size), value, nameof(value));
    }

    /// <summary>The longest String32L these writers produce: one prefix byte
    /// holds the length.</summary>
    internal const int MaxString32LLength = 255;

    /// <summary>The wire size of <paramref name="value"/> as a String16L.</summary>
    internal static int String16LSize(ReadOnlySpan<char> value, string field)
    {
        int byteCount = Cp1252.GetByteCount(value, field);
        if (byteCount > ushort.MaxValue)
        {
            throw new ArgumentException(
                $"{field} is {byteCount} bytes; a String16L holds at most {ushort.MaxValue}.",
                field);
        }

        return sizeof(ushort) + byteCount + PadTo4(sizeof(ushort) + byteCount);
    }

    /// <summary>Writes <paramref name="value"/> as a String16L at the start of
    /// <paramref name="destination"/> and returns the bytes written.</summary>
    internal static int WriteString16L(Span<byte> destination, ReadOnlySpan<char> value, string field)
    {
        int size = String16LSize(value, field);
        if (destination.Length < size)
            throw new ArgumentException($"destination must be at least {size} bytes", nameof(destination));

        int byteCount = Cp1252.GetBytes(value, destination.Slice(sizeof(ushort)), field);
        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)byteCount);
        destination.Slice(sizeof(ushort) + byteCount, size - sizeof(ushort) - byteCount).Clear();
        return size;
    }

    /// <summary>The wire size of <paramref name="value"/> as a String32L.</summary>
    internal static int String32LSize(ReadOnlySpan<char> value, string field)
    {
        int byteCount = Cp1252.GetByteCount(value, field);
        if (byteCount > MaxString32LLength)
        {
            throw new ArgumentException(
                $"{field} is {byteCount} bytes; a String32L written with a one-byte length prefix holds at most {MaxString32LLength}.",
                field);
        }

        return byteCount == 0
            ? sizeof(uint)
            : sizeof(uint) + 1 + byteCount + PadTo4(sizeof(uint) + 1 + byteCount);
    }

    /// <summary>Writes <paramref name="value"/> as a String32L at the start of
    /// <paramref name="destination"/> and returns the bytes written. An empty
    /// string is a u32 0 with no prefix, which ACE reads as "".</summary>
    internal static int WriteString32L(Span<byte> destination, ReadOnlySpan<char> value, string field)
    {
        int size = String32LSize(value, field);
        if (destination.Length < size)
            throw new ArgumentException($"destination must be at least {size} bytes", nameof(destination));

        if (value.IsEmpty)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, 0);
            return size;
        }

        int byteCount = Cp1252.GetBytes(value, destination.Slice(sizeof(uint) + 1), field);
        BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)(byteCount + 1));  // outer length counts the prefix
        destination[sizeof(uint)] = (byte)byteCount;                                   // the prefix: the length itself
        int written = sizeof(uint) + 1 + byteCount;
        destination.Slice(written, size - written).Clear();
        return size;
    }

    private static int PadTo4(int length) => (4 - (length & 3)) & 3;
}

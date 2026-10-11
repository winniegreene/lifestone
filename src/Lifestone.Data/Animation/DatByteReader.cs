using System.Buffers.Binary;
using System.Numerics;

namespace Lifestone.Data.Animation;

/// <summary>
/// Minimal little-endian reader over one raw dat record (as returned by <c>DatDatabase.TryGetFileBytes</c>).
/// Used by the decoders that must not go through DatReaderWriter (see <see cref="AnimHookDecoder"/> and
/// <c>Lifestone.Data.Magic.SpellTableDecoder</c>). Every read is bounds-checked and throws
/// <see cref="InvalidDataException"/> with the record id and offset, so a layout mistake fails loudly.
/// </summary>
internal sealed class DatByteReader(byte[] data, uint recordId)
{
    private readonly byte[] _data = data;

    public uint RecordId { get; } = recordId;

    public int Position { get; private set; }

    public int Length => _data.Length;

    public int Remaining => _data.Length - Position;

    public byte ReadByte()
    {
        Require(1);
        return _data[Position++];
    }

    public ushort ReadUInt16()
    {
        Require(2);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(Position));
        Position += 2;
        return value;
    }

    public int ReadInt32()
    {
        Require(4);
        var value = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(Position));
        Position += 4;
        return value;
    }

    public uint ReadUInt32()
    {
        Require(4);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(Position));
        Position += 4;
        return value;
    }

    public float ReadSingle()
    {
        Require(4);
        var value = BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(Position));
        Position += 4;
        return value;
    }

    public double ReadDouble()
    {
        Require(8);
        var value = BinaryPrimitives.ReadDoubleLittleEndian(_data.AsSpan(Position));
        Position += 8;
        return value;
    }

    public Vector3 ReadVector3() => new(ReadSingle(), ReadSingle(), ReadSingle());

    /// <summary>Dat <c>Frame</c>: origin, then the quaternion stored W, X, Y, Z (ACE.DatLoader/Entity/Frame.cs).</summary>
    public AcFrame ReadFrame()
    {
        var origin = ReadVector3();
        var w = ReadSingle();
        var x = ReadSingle();
        var y = ReadSingle();
        var z = ReadSingle();
        return new AcFrame(origin, new Quaternion(x, y, z, w));
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        Require(count);
        var span = _data.AsSpan(Position, count);
        Position += count;
        return span;
    }

    /// <summary>Skips to the next 4-byte boundary relative to the start of the record (retail <c>PackObj</c> alignment).</summary>
    public void Align4()
    {
        var pad = (4 - (Position & 3)) & 3;
        Require(pad);
        Position += pad;
    }

    /// <summary>Throws unless the whole record has been consumed.</summary>
    public void RequireEnd(string what)
    {
        if (Remaining != 0)
        {
            throw new InvalidDataException(
                $"{what} 0x{RecordId:X8}: {Remaining} unread byte(s) after offset {Position} of {_data.Length}.");
        }
    }

    private void Require(int count)
    {
        if (count < 0 || Position + count > _data.Length)
        {
            throw new InvalidDataException(
                $"Record 0x{RecordId:X8}: read of {count} byte(s) at offset {Position} runs past the end ({_data.Length}).");
        }
    }
}

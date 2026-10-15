using System.Buffers.Binary;

namespace Lifestone.Data.Audio;

/// <summary>
/// A little-endian reader over one raw audio record (a Wave or a SoundTable, starting with its id). Every read is
/// bounds-checked and throws <see cref="InvalidDataException"/> naming the record and the offset, so a layout mistake
/// fails loudly instead of decoding garbage.
/// </summary>
internal ref struct AudioRecordReader
{
    private readonly ReadOnlySpan<byte> _data;

    public AudioRecordReader(ReadOnlySpan<byte> data, string kind, uint recordId)
    {
        _data = data;
        Kind = kind;
        RecordId = recordId;
        Position = 0;
    }

    /// <summary>The record's kind, for messages (<c>Wave</c>, <c>SoundTable</c>).</summary>
    public string Kind { get; }

    /// <summary>The record's id, for messages.</summary>
    public uint RecordId { get; }

    /// <summary>The offset of the next read.</summary>
    public int Position { get; private set; }

    /// <summary>The bytes not read yet.</summary>
    public readonly int Remaining => _data.Length - Position;

    public uint ReadUInt32()
    {
        Require(4);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_data[Position..]);
        Position += 4;
        return value;
    }

    public float ReadSingle()
    {
        Require(4);
        var value = BinaryPrimitives.ReadSingleLittleEndian(_data[Position..]);
        Position += 4;
        return value;
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        Require(count);
        var bytes = _data.Slice(Position, count);
        Position += count;
        return bytes;
    }

    /// <summary>Reads the record's leading id and throws unless it is <see cref="RecordId"/>.</summary>
    public void ReadId()
    {
        var stored = ReadUInt32();
        if (stored != RecordId)
        {
            throw new InvalidDataException($"{Kind} 0x{RecordId:X8}: the record starts with id 0x{stored:X8}.");
        }
    }

    /// <summary>
    /// Ends the record: at most 3 trailing bytes are tolerated, and only when they are zero (the dat's 4-byte alignment,
    /// retail <c>CSoundTable::UnPack</c>'s final align); anything else throws.
    /// </summary>
    /// <returns>The alignment bytes skipped (0 to 3).</returns>
    public int RequireEnd()
    {
        var remaining = Remaining;
        if (remaining == 0)
        {
            return 0;
        }

        if (remaining > 3)
        {
            throw new InvalidDataException(
                $"{Kind} 0x{RecordId:X8}: {remaining} unread byte(s) after offset {Position} of {_data.Length}.");
        }

        foreach (var b in _data[Position..])
        {
            if (b != 0)
            {
                throw new InvalidDataException(
                    $"{Kind} 0x{RecordId:X8}: {remaining} trailing byte(s) after offset {Position} are not zero padding.");
            }
        }

        Position = _data.Length;
        return remaining;
    }

    /// <summary>Throws unless <paramref name="count"/> more bytes can be read.</summary>
    public readonly void Require(int count)
    {
        if (count < 0 || count > Remaining)
        {
            throw new InvalidDataException(
                $"{Kind} 0x{RecordId:X8}: a read of {count} byte(s) at offset {Position} runs past the end ({_data.Length}).");
        }
    }
}

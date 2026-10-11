using System.Buffers.Binary;
using DatReaderWriter.Enums;

namespace Lifestone.Data.Textures;

/// <summary>
/// The fields of a RenderSurface (0x06) record read straight from its raw dat bytes, so that a texture can be decoded
/// from a pooled buffer without DatReaderWriter building a <c>RenderSurface</c> object (which allocates a new pixel
/// array per record and fills it one byte at a time inside the dat lock).
/// </summary>
/// <remarks>
/// <para>Layout, little-endian, as DatReaderWriter's generated <c>RenderSurface.Unpack</c> (its <c>DBObj</c> header with
/// <c>HasId | HasDataCategory</c>) and ACE's <c>ACE.DatLoader.FileTypes.Texture.Unpack</c> read it:
/// u32 id, u32 data category, i32 width, i32 height, u32 pixel format, i32 byte count, the pixel bytes, then a u32
/// default palette id for <c>PFID_INDEX16</c> and <c>PFID_P8</c> only.</para>
/// <para><c>RenderSurfaceRecordTests</c> reads every RenderSurface of the user's portal and high-res dats both ways and
/// requires identical fields and bytes.</para>
/// </remarks>
/// <param name="Id">The file id the record was read for (DatReaderWriter also reports the requested id).</param>
/// <param name="Width">Width in texels (0 for JPEG; the JPEG header has the size).</param>
/// <param name="Height">Height in texels.</param>
/// <param name="Format">Stored pixel format.</param>
/// <param name="DataOffset">Offset of the pixel bytes in the record.</param>
/// <param name="DataLength">Number of pixel bytes.</param>
/// <param name="DefaultPaletteId">The default palette (INDEX16/P8), else 0.</param>
public readonly record struct RenderSurfaceRecord(uint Id, int Width, int Height, PixelFormat Format, int DataOffset, int DataLength, uint DefaultPaletteId)
{
    /// <summary>Bytes before the pixel data.</summary>
    public const int HeaderSize = 24;

    /// <summary>Reads the fields of a raw RenderSurface record.</summary>
    /// <param name="id">The file id it was read for.</param>
    /// <param name="record">The record's bytes (exactly the file, or longer: trailing bytes are ignored).</param>
    /// <exception cref="InvalidDataException">The record is shorter than its fields say.</exception>
    public static RenderSurfaceRecord Parse(uint id, ReadOnlySpan<byte> record)
    {
        if (record.Length < HeaderSize)
        {
            throw new InvalidDataException($"RenderSurface 0x{id:X8}: record of {record.Length} bytes is shorter than its {HeaderSize}-byte header.");
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(record[8..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(record[12..]);
        var format = (PixelFormat)BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
        var length = BinaryPrimitives.ReadInt32LittleEndian(record[20..]);
        if (length < 0 || length > record.Length - HeaderSize)
        {
            throw new InvalidDataException($"RenderSurface 0x{id:X8}: {length} pixel bytes do not fit in a {record.Length}-byte record.");
        }

        uint palette = 0;
        if (RenderSurfaceDecoder.IsIndexed(format))
        {
            var at = HeaderSize + length;
            if (record.Length - at < 4)
            {
                throw new InvalidDataException($"RenderSurface 0x{id:X8}: {format} record has no default palette id.");
            }

            palette = BinaryPrimitives.ReadUInt32LittleEndian(record[at..]);
        }

        return new RenderSurfaceRecord(id, width, height, format, HeaderSize, length, palette);
    }
}

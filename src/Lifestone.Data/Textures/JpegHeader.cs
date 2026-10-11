using System.Buffers.Binary;

namespace Lifestone.Data.Textures;

/// <summary>Frame header (SOFn) of a JPEG stored in a <c>PFID_CUSTOM_RAW_JPEG</c> RenderSurface.</summary>
/// <param name="Width">Image width from the SOF segment.</param>
/// <param name="Height">Image height from the SOF segment.</param>
/// <param name="Components">Colour components (retail requires 3).</param>
/// <param name="Marker">The SOF marker byte (0xC0 baseline, 0xC2 progressive, ...).</param>
/// <remarks>
/// The dat stores w = h = 0 for these records and a complete JFIF file as the payload. Retail
/// (<c>RenderSurface::CreateFromSourceData</c>) reads the dimensions with the Intel JPEG Library
/// (<c>ijlRead(IJL_JBUFF_READPARAMS)</c>), rejects any image whose <c>JPGChannels != 3</c>, and decodes YCbCr into a
/// BGR D3D <c>R8G8B8</c> surface (<c>DIBColor = IJL_BGR</c>, <c>JPGColor = IJL_YCBCR</c>). That is an ordinary JPEG
/// decode, so no red/blue swap is needed: ACViewer's <c>SwapRedAndBlueChannels</c> only undoes its own GDI+ BGRA to
/// XNA RGBA byte copy.
/// </remarks>
public readonly record struct JpegHeader(int Width, int Height, int Components, byte Marker)
{
    /// <summary>Scans marker segments up to the first SOF0..SOF15 (excluding DHT/JPG/DAC) and reads its header.</summary>
    public static bool TryRead(ReadOnlySpan<byte> jpeg, out JpegHeader header)
    {
        header = default;
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return false;
        }

        var p = 2;
        while (p + 4 <= jpeg.Length)
        {
            if (jpeg[p] != 0xFF)
            {
                return false;
            }

            var marker = jpeg[p + 1];
            if (marker == 0xFF)
            {
                p++; // fill byte
                continue;
            }

            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                p += 2; // standalone markers
                continue;
            }

            if (marker == 0xD9 || marker == 0xDA)
            {
                return false; // EOI or scan data before any frame header
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[(p + 2)..]);
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (p + 2 + 8 > jpeg.Length || length < 8)
                {
                    return false;
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(jpeg[(p + 5)..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(jpeg[(p + 7)..]);
                header = new JpegHeader(width, height, jpeg[p + 9], marker);
                return true;
            }

            p += 2 + length;
        }

        return false;
    }
}

using DatReaderWriter.Enums;

namespace Lifestone.Data.Tests.Textures;

/// <summary>
/// Independent reference decoder: a line-by-line C# transcription of the research scout's Python decoder
/// (scratchpad <c>research/scout/acimage.py</c>, <c>decode_render_surface</c> / <c>decode_dxt</c> / <c>_565</c> /
/// <c>_color_block</c>), kept deliberately naive and separate from the production code so the pixel tests compare two
/// implementations. Output is R,G,B,A, top row first, exactly as the Python returns it.
/// </summary>
/// <remarks>
/// Known, intended differences from <see cref="Lifestone.Data.Textures.RenderSurfaceDecoder"/>: the scout decodes A8 and
/// LSCAPE_ALPHA as opaque grey (v, v, v, 255) where production keeps the value as alpha (255, 255, 255, v); the scout
/// knows nothing about clip maps or X8R8G8B8 palette expansion; and it has no JPEG decoder.
/// </remarks>
internal static class ScoutReferenceDecoder
{
    public static byte[] Decode(PixelFormat format, int w, int h, byte[] px, uint[]? paletteArgb)
    {
        var n = w * h;
        var output = new byte[n * 4];
        switch (format)
        {
            case PixelFormat.PFID_DXT1:
            case PixelFormat.PFID_DXT3:
            case PixelFormat.PFID_DXT5:
                return DecodeDxt(px, w, h, format);

            case PixelFormat.PFID_A8R8G8B8:
            case PixelFormat.PFID_X8R8G8B8:
                for (var i = 0; i < n; i++)
                {
                    byte b = px[4 * i], g = px[(4 * i) + 1], r = px[(4 * i) + 2], a = px[(4 * i) + 3];
                    Put(output, i, r, g, b, format == PixelFormat.PFID_A8R8G8B8 ? a : (byte)255);
                }

                return output;

            case PixelFormat.PFID_R8G8B8:
                for (var i = 0; i < n; i++)
                {
                    byte b = px[3 * i], g = px[(3 * i) + 1], r = px[(3 * i) + 2];
                    Put(output, i, r, g, b, 255);
                }

                return output;

            case PixelFormat.PFID_CUSTOM_LSCAPE_R8G8B8:
                for (var i = 0; i < n; i++)
                {
                    byte r = px[3 * i], g = px[(3 * i) + 1], b = px[(3 * i) + 2];
                    Put(output, i, r, g, b, 255);
                }

                return output;

            case PixelFormat.PFID_A8:
            case PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA:
                for (var i = 0; i < n; i++)
                {
                    var v = px[i];
                    Put(output, i, v, v, v, 255);
                }

                return output;

            case PixelFormat.PFID_R5G6B5:
                for (var i = 0; i < n; i++)
                {
                    var c = px[2 * i] | (px[(2 * i) + 1] << 8);
                    var (r, g, b) = C565(c);
                    Put(output, i, r, g, b, 255);
                }

                return output;

            case PixelFormat.PFID_A4R4G4B4:
                for (var i = 0; i < n; i++)
                {
                    var c = px[2 * i] | (px[(2 * i) + 1] << 8);
                    Put(output, i, (byte)(((c >> 8) & 15) * 17), (byte)(((c >> 4) & 15) * 17), (byte)((c & 15) * 17), (byte)(((c >> 12) & 15) * 17));
                }

                return output;

            case PixelFormat.PFID_P8:
            case PixelFormat.PFID_INDEX16:
            {
                ArgumentNullException.ThrowIfNull(paletteArgb);
                var step = format == PixelFormat.PFID_P8 ? 1 : 2;
                for (var i = 0; i < n; i++)
                {
                    var idx = step == 1 ? px[i] : px[2 * i] | (px[(2 * i) + 1] << 8);
                    var c = idx < paletteArgb.Length ? paletteArgb[idx] : 0xFFFF00FFu;
                    Put(output, i, (byte)((c >> 16) & 255), (byte)((c >> 8) & 255), (byte)(c & 255), (byte)((c >> 24) & 255));
                }

                return output;
            }

            default:
                throw new NotSupportedException(format.ToString());
        }
    }

    private static void Put(byte[] o, int i, byte r, byte g, byte b, byte a)
    {
        o[4 * i] = r;
        o[(4 * i) + 1] = g;
        o[(4 * i) + 2] = b;
        o[(4 * i) + 3] = a;
    }

    // def _565(c)
    private static (byte R, byte G, byte B) C565(int c)
    {
        var r = (c >> 11) & 31;
        var g = (c >> 5) & 63;
        var b = c & 31;
        return ((byte)((r << 3) | (r >> 2)), (byte)((g << 2) | (g >> 4)), (byte)((b << 3) | (b >> 2)));
    }

    // def _color_block(blk, off, out, w, h, bx, by, dxt1)
    private static void ColorBlock(byte[] blk, int off, byte[] output, int w, int h, int bx, int by, bool dxt1)
    {
        var c0 = blk[off] | (blk[off + 1] << 8);
        var c1 = blk[off + 2] | (blk[off + 3] << 8);
        var bits = (uint)(blk[off + 4] | (blk[off + 5] << 8) | (blk[off + 6] << 16) | (blk[off + 7] << 24));
        var p0 = C565(c0);
        var p1 = C565(c1);
        var pal = new (byte R, byte G, byte B, byte A)[4];
        if (c0 > c1 || !dxt1)
        {
            pal[0] = (p0.R, p0.G, p0.B, 255);
            pal[1] = (p1.R, p1.G, p1.B, 255);
            pal[2] = ((byte)(((2 * p0.R) + p1.R) / 3), (byte)(((2 * p0.G) + p1.G) / 3), (byte)(((2 * p0.B) + p1.B) / 3), 255);
            pal[3] = ((byte)((p0.R + (2 * p1.R)) / 3), (byte)((p0.G + (2 * p1.G)) / 3), (byte)((p0.B + (2 * p1.B)) / 3), 255);
        }
        else
        {
            pal[0] = (p0.R, p0.G, p0.B, 255);
            pal[1] = (p1.R, p1.G, p1.B, 255);
            pal[2] = ((byte)((p0.R + p1.R) / 2), (byte)((p0.G + p1.G) / 2), (byte)((p0.B + p1.B) / 2), 255);
            pal[3] = (0, 0, 0, 0);
        }

        for (var j = 0; j < 4; j++)
        {
            var y = by + j;
            if (y >= h)
            {
                break;
            }

            for (var i = 0; i < 4; i++)
            {
                var x = bx + i;
                if (x >= w)
                {
                    continue;
                }

                var idx = (int)((bits >> (2 * ((4 * j) + i))) & 3);
                var o = ((y * w) + x) * 4;
                output[o] = pal[idx].R;
                output[o + 1] = pal[idx].G;
                output[o + 2] = pal[idx].B;
                output[o + 3] = pal[idx].A;
            }
        }
    }

    // def decode_dxt(data, w, h, kind)
    private static byte[] DecodeDxt(byte[] data, int w, int h, PixelFormat kind)
    {
        var output = new byte[w * h * 4];
        int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
        var bsize = kind == PixelFormat.PFID_DXT1 ? 8 : 16;
        var p = 0;
        for (var by = 0; by < bh; by++)
        {
            for (var bx = 0; bx < bw; bx++)
            {
                if (kind == PixelFormat.PFID_DXT1)
                {
                    ColorBlock(data, p, output, w, h, bx * 4, by * 4, true);
                }
                else
                {
                    ColorBlock(data, p + 8, output, w, h, bx * 4, by * 4, false);
                    var alphas = new int[16];
                    if (kind == PixelFormat.PFID_DXT3)
                    {
                        var a64 = BitConverter.ToUInt64(data, p);
                        for (var k = 0; k < 16; k++)
                        {
                            alphas[k] = (int)((a64 >> (4 * k)) & 0xF) * 17;
                        }
                    }
                    else
                    {
                        int a0 = data[p], a1 = data[p + 1];
                        ulong bits = 0;
                        for (var i = 0; i < 6; i++)
                        {
                            bits |= (ulong)data[p + 2 + i] << (8 * i);
                        }

                        var tbl = new List<int> { a0, a1 };
                        if (a0 > a1)
                        {
                            for (var k = 1; k < 7; k++)
                            {
                                tbl.Add((((7 - k) * a0) + (k * a1)) / 7);
                            }
                        }
                        else
                        {
                            for (var k = 1; k < 5; k++)
                            {
                                tbl.Add((((5 - k) * a0) + (k * a1)) / 5);
                            }

                            tbl.Add(0);
                            tbl.Add(255);
                        }

                        for (var k = 0; k < 16; k++)
                        {
                            alphas[k] = tbl[(int)((bits >> (3 * k)) & 7)];
                        }
                    }

                    for (var k = 0; k < 16; k++)
                    {
                        int x = (bx * 4) + (k & 3), y = (by * 4) + (k >> 2);
                        if (x < w && y < h)
                        {
                            output[(((y * w) + x) * 4) + 3] = (byte)alphas[k];
                        }
                    }
                }

                p += bsize;
            }
        }

        return output;
    }
}

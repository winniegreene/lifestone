using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace Lifestone.Client.RetailUi.Rendering;

/// <summary>
/// A baseline JPEG decoder written from ITU-T T.81 (no third-party code): what the six JPEG UI images of the retail dats
/// need (the login screens' backgrounds among them): sequential Huffman coding, 8-bit samples, one or three components,
/// sampling factors 1 and 2, restart intervals, the Adobe colour-transform marker. Deterministic: integer arithmetic
/// throughout, so the software compositor (the production path) and its tests see the same pixels on every machine.
/// </summary>
/// <remarks>
/// <para>
/// Progressive, lossless, arithmetic-coded and 12-bit files are refused (false, with the reason from
/// <see cref="Decode"/>); none of the retail UI images is one. Retail decoded these files with its own decoder,
/// whose rounding is not known, so a pixel may differ by one step from retail's; the art is otherwise the same
/// (docs/retail-ui.md §5).
/// </para>
/// <para>
/// The inverse DCT is the separable 8-point transform with the cosines scaled by 2^13 and rounded; chroma subsampled
/// 2:1 is replicated; YCbCr becomes RGB by the JFIF matrix in 16-bit fixed point, rounded.
/// </para>
/// </remarks>
public sealed class UiJpegDecoder : IUiJpegDecoder
{
    private const int CosBits = 13;

    /// <summary>JPEG's zig-zag order: <c>ZigZag[k]</c> is the natural (row-major) index of the k-th coefficient.</summary>
    private static readonly byte[] ZigZag =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    /// <summary><c>round(C(u)/2 × cos((2x + 1)uπ / 16) × 2^13)</c> for x (row) and u (column), C(0) = 1/√2, else 1.</summary>
    private static readonly int[] Cosines =
    [
        2896, 4017, 3784, 3406, 2896, 2276, 1567, 799,
        2896, 3406, 1567, -799, -2896, -4017, -3784, -2276,
        2896, 2276, -1567, -4017, -2896, 799, 3784, 3406,
        2896, 799, -3784, -2276, 2896, 3406, -1567, -4017,
        2896, -799, -3784, 2276, 2896, -3406, -1567, 4017,
        2896, -2276, -1567, 4017, -2896, -799, 3784, -3406,
        2896, -3406, 1567, 799, -2896, 4017, -3784, 2276,
        2896, -4017, 3784, -3406, 2896, -2276, 1567, -799,
    ];

    /// <summary>The shared instance (the decoder keeps no state).</summary>
    public static UiJpegDecoder Instance { get; } = new();

    /// <inheritdoc/>
    public bool TryDecode(ReadOnlySpan<byte> jpeg, out int width, out int height, [NotNullWhen(true)] out byte[]? rgba) =>
        Decode(jpeg, out width, out height, out rgba, out _);

    /// <summary>Decodes a complete JFIF file; false, with the reason in <paramref name="failure"/>, when it cannot.</summary>
    public static bool Decode(ReadOnlySpan<byte> jpeg, out int width, out int height, [NotNullWhen(true)] out byte[]? rgba, out string? failure)
    {
        width = 0;
        height = 0;
        rgba = null;
        try
        {
            var frame = new Decoder(jpeg).Decode();
            width = frame.Width;
            height = frame.Height;
            rgba = frame.Pixels;
            failure = null;
            return true;
        }
        catch (InvalidDataException ex)
        {
            failure = ex.Message;
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            failure = "the file is cut short";
            return false;
        }
    }

    /// <summary>One 8×8 inverse DCT: dequantized coefficients in natural order to level-shifted, clamped samples.</summary>
    internal static void InverseDct(ReadOnlySpan<int> coefficients, Span<byte> samples, int stride)
    {
        Span<long> rows = stackalloc long[64];

        // Columns first (over u for each output row y is the same work): t[v][x] = sum_u C(u) F[v][u] cos.
        for (var v = 0; v < 8; v++)
        {
            for (var x = 0; x < 8; x++)
            {
                long sum = 0;
                for (var u = 0; u < 8; u++)
                {
                    sum += (long)coefficients[(v * 8) + u] * Cosines[(x * 8) + u];
                }

                rows[(v * 8) + x] = sum;
            }
        }

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                long sum = 0;
                for (var v = 0; v < 8; v++)
                {
                    sum += rows[(v * 8) + x] * Cosines[(y * 8) + v];
                }

                // Two 2^13 scales, rounded, then the +128 level shift.
                var value = (int)((sum + (1L << ((2 * CosBits) - 1))) >> (2 * CosBits)) + 128;
                samples[(y * stride) + x] = (byte)Math.Clamp(value, 0, 255);
            }
        }
    }

    private sealed class Component
    {
        public int Id;
        public int H;
        public int V;
        public int QuantTable;
        public int DcTable;
        public int AcTable;
        public int Predictor;
        public int BlocksPerLine;
        public int BlocksPerColumn;
        public byte[] Samples = [];
    }

    private sealed class Huffman
    {
        public readonly int[] MaxCode = new int[18];
        public readonly int[] ValOffset = new int[17];
        public byte[] Values = [];
        public bool Defined;

        public void Build(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values)
        {
            Values = values.ToArray();
            var code = 0;
            var k = 0;
            for (var length = 1; length <= 16; length++)
            {
                var n = counts[length - 1];
                ValOffset[length] = k - code;
                code += n;
                k += n;
                MaxCode[length] = n != 0 ? code - 1 : -1;
                code <<= 1;
            }

            MaxCode[17] = int.MaxValue;
            Defined = true;
        }
    }

    private readonly record struct Frame(int Width, int Height, byte[] Pixels);

    private ref struct Decoder
    {
        private readonly ReadOnlySpan<byte> _data;
        private readonly int[][] _quant = new int[4][];
        private readonly Huffman[] _dc = [new(), new(), new(), new()];
        private readonly Huffman[] _ac = [new(), new(), new(), new()];
        private Component[] _components = [];
        private int _position;
        private int _width;
        private int _height;
        private int _restartInterval;
        private int _adobeTransform = -1;
        private uint _bits;
        private int _bitCount;
        private bool _sawFrame;

        public Decoder(ReadOnlySpan<byte> data) => _data = data;

        public Frame Decode()
        {
            if (_data.Length < 4 || _data[0] != 0xFF || _data[1] != 0xD8)
            {
                throw new InvalidDataException("not a JPEG file (no SOI marker)");
            }

            _position = 2;
            while (true)
            {
                var marker = NextMarker();
                switch (marker)
                {
                    case 0xD9:
                        throw new InvalidDataException("the file ends before its scan");
                    case 0xC0:
                    case 0xC1:
                        ReadFrame();
                        break;
                    case 0xC2:
                    case 0xC3:
                    case >= 0xC5 and <= 0xC7:
                    case >= 0xC9 and <= 0xCB:
                    case >= 0xCD and <= 0xCF:
                        throw new InvalidDataException($"TODO(retailui-runtime): JPEG process SOF{marker - 0xC0} (progressive, lossless or arithmetic) is not supported");
                    case 0xC4:
                        ReadHuffmanTables();
                        break;
                    case 0xDB:
                        ReadQuantTables();
                        break;
                    case 0xDD:
                        _restartInterval = BinaryPrimitives.ReadUInt16BigEndian(Segment(out _).Slice(0, 2));
                        break;
                    case 0xEE:
                        ReadAdobe();
                        break;
                    case 0xDA:
                        ReadScan();
                        return Output();
                    default:
                        Segment(out _);
                        break;
                }
            }
        }

        private int NextMarker()
        {
            while (_position < _data.Length - 1)
            {
                if (_data[_position] == 0xFF && _data[_position + 1] != 0xFF && _data[_position + 1] != 0x00)
                {
                    var marker = _data[_position + 1];
                    _position += 2;
                    return marker;
                }

                _position++;
            }

            throw new InvalidDataException("the file ends before its scan");
        }

        private ReadOnlySpan<byte> Segment(out int start)
        {
            if (_position + 2 > _data.Length)
            {
                throw new InvalidDataException("a marker segment is cut short");
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(_data.Slice(_position, 2));
            if (length < 2 || _position + length > _data.Length)
            {
                throw new InvalidDataException("a marker segment is cut short");
            }

            start = _position + 2;
            var segment = _data.Slice(start, length - 2);
            _position += length;
            return segment;
        }

        private void ReadFrame()
        {
            var s = Segment(out _);
            if (s[0] != 8)
            {
                throw new InvalidDataException($"TODO(retailui-runtime): {s[0]}-bit JPEG samples are not supported");
            }

            _height = BinaryPrimitives.ReadUInt16BigEndian(s.Slice(1, 2));
            _width = BinaryPrimitives.ReadUInt16BigEndian(s.Slice(3, 2));
            var count = s[5];
            if (_width == 0 || _height == 0 || (count != 1 && count != 3))
            {
                throw new InvalidDataException($"TODO(retailui-runtime): a {_width}x{_height} JPEG with {count} components is not supported");
            }

            _components = new Component[count];
            for (var i = 0; i < count; i++)
            {
                var c = s.Slice(6 + (i * 3), 3);
                var h = c[1] >> 4;
                var v = c[1] & 15;
                if (h is < 1 or > 2 || v is < 1 or > 2 || c[2] > 3)
                {
                    throw new InvalidDataException($"TODO(retailui-runtime): JPEG sampling {h}x{v} is not supported");
                }

                _components[i] = new Component { Id = c[0], H = h, V = v, QuantTable = c[2] };
            }

            _sawFrame = true;
        }

        private void ReadHuffmanTables()
        {
            var s = Segment(out _);
            var i = 0;
            while (i < s.Length)
            {
                var info = s[i];
                var counts = s.Slice(i + 1, 16);
                var total = 0;
                foreach (var n in counts)
                {
                    total += n;
                }

                var values = s.Slice(i + 17, total);
                var table = info & 15;
                if (table > 3)
                {
                    throw new InvalidDataException("a Huffman table id is out of range");
                }

                ((info >> 4) == 0 ? _dc : _ac)[table].Build(counts, values);
                i += 17 + total;
            }
        }

        private void ReadQuantTables()
        {
            var s = Segment(out _);
            var i = 0;
            while (i < s.Length)
            {
                var precision = s[i] >> 4;
                var table = s[i] & 15;
                if (table > 3)
                {
                    throw new InvalidDataException("a quantization table id is out of range");
                }

                var q = new int[64];
                for (var k = 0; k < 64; k++)
                {
                    q[ZigZag[k]] = precision == 0 ? s[i + 1 + k] : BinaryPrimitives.ReadUInt16BigEndian(s.Slice(i + 1 + (k * 2), 2));
                }

                _quant[table] = q;
                i += 1 + (precision == 0 ? 64 : 128);
            }
        }

        private void ReadAdobe()
        {
            var s = Segment(out _);
            if (s.Length >= 12 && s[0] == (byte)'A' && s[1] == (byte)'d' && s[2] == (byte)'o' && s[3] == (byte)'b' && s[4] == (byte)'e')
            {
                _adobeTransform = s[11];
            }
        }

        private void ReadScan()
        {
            if (!_sawFrame)
            {
                throw new InvalidDataException("a scan comes before the frame header");
            }

            var s = Segment(out _);
            var count = s[0];
            if (count != _components.Length)
            {
                throw new InvalidDataException("TODO(retailui-runtime): a JPEG with several scans is not supported");
            }

            for (var i = 0; i < count; i++)
            {
                var id = s[1 + (i * 2)];
                var tables = s[2 + (i * 2)];
                var component = Array.Find(_components, c => c.Id == id) ?? throw new InvalidDataException($"scan component {id} is not in the frame");
                component.DcTable = tables >> 4;
                component.AcTable = tables & 15;
                if (!_dc[component.DcTable].Defined || !_ac[component.AcTable].Defined || _quant[component.QuantTable] is null)
                {
                    throw new InvalidDataException($"scan component {id} names a table the file does not define");
                }
            }

            var maxH = 1;
            var maxV = 1;
            foreach (var c in _components)
            {
                maxH = Math.Max(maxH, c.H);
                maxV = Math.Max(maxV, c.V);
            }

            var mcusX = (_width + (8 * maxH) - 1) / (8 * maxH);
            var mcusY = (_height + (8 * maxV) - 1) / (8 * maxV);
            foreach (var c in _components)
            {
                c.BlocksPerLine = mcusX * c.H;
                c.BlocksPerColumn = mcusY * c.V;
                c.Samples = new byte[c.BlocksPerLine * 8 * c.BlocksPerColumn * 8];
            }

            Span<int> coefficients = stackalloc int[64];
            var mcu = 0;
            var total = mcusX * mcusY;
            var single = _components.Length == 1;
            if (single)
            {
                // A non-interleaved scan walks the component's own blocks, not MCUs.
                var c = _components[0];
                var blocksX = (_width + 7) / 8;
                var blocksY = (_height + 7) / 8;
                total = blocksX * blocksY;
                for (var b = 0; b < total; b++)
                {
                    Restart(ref mcu, b);
                    DecodeBlock(c, coefficients, b % blocksX, b / blocksX);
                }

                return;
            }

            for (var m = 0; m < total; m++)
            {
                Restart(ref mcu, m);
                var mx = m % mcusX;
                var my = m / mcusX;
                foreach (var c in _components)
                {
                    for (var v = 0; v < c.V; v++)
                    {
                        for (var h = 0; h < c.H; h++)
                        {
                            DecodeBlock(c, coefficients, (mx * c.H) + h, (my * c.V) + v);
                        }
                    }
                }
            }
        }

        private void Restart(ref int counter, int unit)
        {
            if (_restartInterval == 0 || unit == 0 || unit % _restartInterval != 0)
            {
                return;
            }

            // Byte-align, expect RSTn, reset the predictors.
            _bitCount = 0;
            _bits = 0;
            while (_position < _data.Length - 1 && !(_data[_position] == 0xFF && _data[_position + 1] >= 0xD0 && _data[_position + 1] <= 0xD7))
            {
                _position++;
            }

            if (_position >= _data.Length - 1)
            {
                throw new InvalidDataException("a restart marker is missing");
            }

            _position += 2;
            counter++;
            foreach (var c in _components)
            {
                c.Predictor = 0;
            }
        }

        private void DecodeBlock(Component c, scoped Span<int> coefficients, int blockX, int blockY)
        {
            coefficients.Clear();
            var quant = _quant[c.QuantTable];
            var size = DecodeHuffman(_dc[c.DcTable]);
            var diff = size == 0 ? 0 : Extend(ReadBits(size), size);
            c.Predictor += diff;
            coefficients[0] = c.Predictor * quant[0];
            for (var k = 1; k < 64;)
            {
                var rs = DecodeHuffman(_ac[c.AcTable]);
                var run = rs >> 4;
                var bits = rs & 15;
                if (bits == 0)
                {
                    if (run != 15)
                    {
                        break;
                    }

                    k += 16;
                    continue;
                }

                k += run;
                if (k > 63)
                {
                    throw new InvalidDataException("a block's coefficients run past 64");
                }

                var natural = ZigZag[k];
                coefficients[natural] = Extend(ReadBits(bits), bits) * quant[natural];
                k++;
            }

            var stride = c.BlocksPerLine * 8;
            InverseDct(coefficients, c.Samples.AsSpan((blockY * 8 * stride) + (blockX * 8)), stride);
        }

        private int DecodeHuffman(Huffman table)
        {
            var code = 0;
            for (var length = 1; length <= 16; length++)
            {
                code = (code << 1) | ReadBit();
                if (code <= table.MaxCode[length])
                {
                    return table.Values[code + table.ValOffset[length]];
                }
            }

            throw new InvalidDataException("a Huffman code is longer than 16 bits");
        }

        private int ReadBits(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                value = (value << 1) | ReadBit();
            }

            return value;
        }

        private int ReadBit()
        {
            if (_bitCount == 0)
            {
                if (_position >= _data.Length)
                {
                    throw new InvalidDataException("the scan's data ends early");
                }

                var b = _data[_position++];
                if (b == 0xFF)
                {
                    var next = _position < _data.Length ? _data[_position] : (byte)0;
                    if (next == 0x00)
                    {
                        _position++;
                    }
                    else
                    {
                        throw new InvalidDataException($"a marker 0xFF{next:X2} interrupts the scan's data");
                    }
                }

                _bits = b;
                _bitCount = 8;
            }

            _bitCount--;
            return (int)((_bits >> _bitCount) & 1);
        }

        private static int Extend(int value, int bits) => value < (1 << (bits - 1)) ? value - (1 << bits) + 1 : value;

        private Frame Output()
        {
            var maxH = 1;
            var maxV = 1;
            foreach (var c in _components)
            {
                maxH = Math.Max(maxH, c.H);
                maxV = Math.Max(maxV, c.V);
            }

            var pixels = new byte[_width * _height * 4];
            var ycc = _components.Length == 3 && _adobeTransform != 0;
            for (var y = 0; y < _height; y++)
            {
                for (var x = 0; x < _width; x++)
                {
                    var i = ((y * _width) + x) * 4;
                    if (_components.Length == 1)
                    {
                        var g = Sample(_components[0], x, y, maxH, maxV);
                        pixels[i] = pixels[i + 1] = pixels[i + 2] = g;
                    }
                    else
                    {
                        int c0 = Sample(_components[0], x, y, maxH, maxV);
                        int c1 = Sample(_components[1], x, y, maxH, maxV);
                        int c2 = Sample(_components[2], x, y, maxH, maxV);
                        if (ycc)
                        {
                            var cb = c1 - 128;
                            var cr = c2 - 128;

                            // JFIF: R = Y + 1.402 Cr, G = Y - 0.344136 Cb - 0.714136 Cr, B = Y + 1.772 Cb (16-bit fixed point, rounded).
                            pixels[i] = Clamp(c0 + (((91881 * cr) + 32768) >> 16));
                            pixels[i + 1] = Clamp(c0 - (((22554 * cb) + (46802 * cr) + 32768) >> 16));
                            pixels[i + 2] = Clamp(c0 + (((116130 * cb) + 32768) >> 16));
                        }
                        else
                        {
                            pixels[i] = (byte)c0;
                            pixels[i + 1] = (byte)c1;
                            pixels[i + 2] = (byte)c2;
                        }
                    }

                    pixels[i + 3] = 255;
                }
            }

            return new Frame(_width, _height, pixels);
        }

        private static byte Sample(Component c, int x, int y, int maxH, int maxV)
        {
            var sx = x * c.H / maxH;
            var sy = y * c.V / maxV;
            return c.Samples[(sy * c.BlocksPerLine * 8) + sx];
        }

        private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
    }
}

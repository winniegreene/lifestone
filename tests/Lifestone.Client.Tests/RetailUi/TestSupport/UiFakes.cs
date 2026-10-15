using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.TestSupport;

/// <summary>Builds synthetic descs and layouts for runtime tests (no dats).</summary>
internal static class UiDescs
{
    /// <summary>A desc of class <paramref name="type"/> at (x, y, w, h).</summary>
    public static UiElementDesc Element(
        uint id, uint type, int x, int y, int width, int height,
        uint zLevel = 0, uint readOrder = 0, UiEdges edges = default,
        IEnumerable<UiProperty>? properties = null, IEnumerable<UiMedia>? media = null,
        IEnumerable<UiStateDesc>? states = null, IEnumerable<UiElementDesc>? children = null,
        uint defaultState = 0, bool passToChildren = false, uint layout = 0x21000001) => new()
    {
        ElementId = id,
        Type = type,
        ReadOrder = readOrder,
        Edges = edges,
        DefaultState = defaultState,
        SourceLayout = layout,
        BaseState = new UiStateDesc
        {
            X = x,
            Y = y,
            Width = width,
            Height = height,
            ZLevel = zLevel,
            PassToChildren = passToChildren,
            Incorporation = UiIncorporation.X | UiIncorporation.Y | UiIncorporation.Width | UiIncorporation.Height | UiIncorporation.ZLevel,
            Properties = Dict(properties ?? [], p => p.Id),
            Media = (media ?? []).ToArray(),
        },
        States = Dict(states ?? [], s => s.StateId),
        Children = Dict(children ?? [], c => c.ElementId),
    };

    /// <summary>A state with properties and media.</summary>
    public static UiStateDesc State(uint id, IEnumerable<UiProperty>? properties = null, IEnumerable<UiMedia>? media = null, bool passToChildren = false) => new()
    {
        StateId = id,
        PassToChildren = passToChildren,
        Properties = Dict(properties ?? [], p => p.Id),
        Media = (media ?? []).ToArray(),
    };

    /// <summary>A layout of the given size holding <paramref name="elements"/> at its top level.</summary>
    public static UiLayout Layout(uint id, int width, int height, params UiElementDesc[] elements) =>
        new(id, width, height, Dict(elements, e => e.ElementId));

    private static IReadOnlyDictionary<uint, T> Dict<T>(IEnumerable<T> items, Func<T, uint> key)
    {
        var ordered = new OrderedDictionary<uint, T>();
        foreach (var item in items)
        {
            ordered.Add(key(item), item);
        }

        return new ReadOnlyDictionary<uint, T>(ordered);
    }
}

/// <summary>An <see cref="IUiDescSource"/> over in-memory layouts; full descs (type not 0) resolve to themselves.</summary>
internal sealed class FakeDescSource(IEnumerable<UiLayout> layouts, IReadOnlyDictionary<uint, uint>? enums = null) : IUiDescSource
{
    private readonly Dictionary<uint, UiLayout> _layouts = layouts.ToDictionary(l => l.Id);

    public bool TryResolveLayoutEnum(uint layoutEnum, out uint layoutId)
    {
        layoutId = 0;
        return enums is not null && enums.TryGetValue(layoutEnum, out layoutId);
    }

    public bool TryGetLayout(uint layoutId, [NotNullWhen(true)] out UiLayout? layout) => _layouts.TryGetValue(layoutId, out layout);

    public UiResolveResult InqFullDesc(UiElementDesc local)
    {
        if (local.Type != 0)
        {
            return new UiResolveResult(local, null);
        }

        return _layouts.TryGetValue(local.BaseLayout, out var layout) && layout.AccessElementDesc(local.BaseElement) is { } baseDesc
            ? new UiResolveResult(UiDescResolver.Incorporate(baseDesc, local), null)
            : new UiResolveResult(null, $"{local.Describe()} does not resolve (fake)");
    }
}

/// <summary>Dice that return queued values, then <see cref="Default"/>.</summary>
internal sealed class QueuedRandom(params double[] values) : IUiRandom
{
    private readonly Queue<double> _values = new(values);

    public double Default { get; set; }

    public List<(double Min, double Max)> Calls { get; } = [];

    public double RollDice(double min, double max)
    {
        Calls.Add((min, max));
        return _values.Count > 0 ? _values.Dequeue() : Default;
    }
}

/// <summary>Records UI sounds.</summary>
internal sealed class RecordingSoundSink : IUiSoundSink
{
    public List<string> Played { get; } = [];

    public void PlayFromCenter(uint soundTableId, uint soundType) => Played.Add($"table 0x{soundTableId:X8} type 0x{soundType:X}");

    public void PlayWaveFromCenter(uint waveId, float volume) => Played.Add($"wave 0x{waveId:X8} volume {volume}");
}

/// <summary>Images from a dictionary; DIDs in <see cref="Pending"/> answer pending.</summary>
internal sealed class FakeImageSource : IUiImageSource
{
    public Dictionary<uint, UiImage> Images { get; } = [];

    public HashSet<uint> Pending { get; } = [];

    public UiImageLookup TryGetImage(uint did, [NotNullWhen(true)] out UiImage? image)
    {
        image = null;
        if (Pending.Contains(did))
        {
            return UiImageLookup.Pending;
        }

        return Images.TryGetValue(did, out image) ? UiImageLookup.Ready : UiImageLookup.Missing;
    }

    /// <summary>A solid image of one colour.</summary>
    public static UiImage Solid(uint did, int width, int height, uint argb, UiPixelFormat format = UiPixelFormat.Argb)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            rgba[(i * 4) + 0] = (byte)(argb >> 16);
            rgba[(i * 4) + 1] = (byte)(argb >> 8);
            rgba[(i * 4) + 2] = (byte)argb;
            rgba[(i * 4) + 3] = format == UiPixelFormat.Argb || format == UiPixelFormat.A8 ? (byte)(argb >> 24) : (byte)255;
        }

        return new UiImage(did, width, height, format, rgba);
    }

    /// <summary>An image whose pixel (x, y) is 0xFF000000 | (x &lt;&lt; 8) | y (so a test can read where a pixel came from).</summary>
    public static UiImage Coordinates(uint did, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = ((y * width) + x) * 4;
                rgba[i] = 0;
                rgba[i + 1] = (byte)x;
                rgba[i + 2] = (byte)y;
                rgba[i + 3] = 255;
            }
        }

        return new UiImage(did, width, height, UiPixelFormat.Argb, rgba);
    }
}

/// <summary>
/// A synthetic font: every printable ASCII character (or the given ones) is a <paramref name="cellWidth"/>×<paramref name="cellHeight"/>
/// glyph whose A8 pixels are fully opaque, with the given offsets; '?' always exists. Its lookup keeps R5's rules
/// (<see cref="IUiFont"/>): the range runs from the lowest character to the highest, a character inside it the font
/// lacks is glyph 0 (the first character given), and one outside it is '?'.
/// </summary>
internal sealed class FakeFont : IUiFont
{
    private readonly Dictionary<char, UiGlyphDesc> _glyphs = [];
    private readonly UiGlyphDesc _glyph0;
    private readonly char _min;
    private readonly char _max;

    public FakeFont(uint did = 0x40000000, int cellWidth = 5, int cellHeight = 8, sbyte before = 1, sbyte after = 1, sbyte vertical = 2, int maxCharHeight = 12, IEnumerable<char>? characters = null, UiImage? background = null, int border = 0)
    {
        Did = did;
        MaxCharHeight = maxCharHeight;
        MaxCharWidth = cellWidth;
        BaselineOffset = 10;
        NumHorizontalBorderPixels = border;
        NumVerticalBorderPixels = border;
        var chars = (characters ?? Enumerable.Range(0x20, 0x5F).Select(c => (char)c)).Append('?').Distinct().ToArray();
        var atlasWidth = chars.Length * (cellWidth + (2 * border)) + border;
        var atlasHeight = cellHeight + (2 * border);
        var rgba = new byte[atlasWidth * atlasHeight * 4];
        for (var i = 0; i < chars.Length; i++)
        {
            var x0 = border + (i * (cellWidth + (2 * border)));
            _glyphs[chars[i]] = new UiGlyphDesc(chars[i], (ushort)x0, (ushort)border, (byte)cellWidth, (byte)cellHeight, before, after, vertical);
            for (var y = 0; y < cellHeight; y++)
            {
                for (var x = 0; x < cellWidth; x++)
                {
                    var p = (((y + border) * atlasWidth) + x0 + x) * 4;
                    rgba[p] = rgba[p + 1] = rgba[p + 2] = 255;
                    rgba[p + 3] = 255;
                }
            }
        }

        Foreground = new UiImage(0x06000001, atlasWidth, atlasHeight, UiPixelFormat.A8, rgba);
        Background = background;
        _glyph0 = _glyphs[chars[0]];
        _min = chars.Min();
        _max = chars.Max();
    }

    public uint Did { get; }

    public int MaxCharHeight { get; }

    public int MaxCharWidth { get; }

    public int BaselineOffset { get; }

    public int NumHorizontalBorderPixels { get; }

    public int NumVerticalBorderPixels { get; }

    public UiImage? Foreground { get; }

    public UiImage? Background { get; }

    public bool ContainsChar(char c) => c >= _min && c <= _max;

    public bool TryGetCharDesc(char c, out UiGlyphDesc desc)
    {
        if (ContainsChar(c))
        {
            desc = _glyphs.TryGetValue(c, out var found) ? found : _glyph0;
            return true;
        }

        return _glyphs.TryGetValue('?', out desc);
    }
}

/// <summary>Fonts by DID and enum.</summary>
internal sealed class FakeFontSource : IUiFontSource
{
    public Dictionary<uint, IUiFont> ByDid { get; } = [];

    public Dictionary<uint, IUiFont> ByEnum { get; } = [];

    public IUiFont? GetFont(uint did) => ByDid.GetValueOrDefault(did);

    public IUiFont? GetFontByEnum(uint fontEnum) => ByEnum.GetValueOrDefault(fontEnum);

    /// <summary>A source whose default font (enum 24) is <paramref name="font"/>, also findable by its DID.</summary>
    public static FakeFontSource With(IUiFont font)
    {
        var source = new FakeFontSource();
        source.ByDid[font.Did] = font;
        source.ByEnum[24] = font;
        return source;
    }
}

/// <summary>Strings from a dictionary.</summary>
internal sealed class FakeStringSource : IUiStringSource
{
    public Dictionary<(uint Table, uint Id), string> Strings { get; } = [];

    public bool TryGetString(in UiStringRef reference, out string text) =>
        Strings.TryGetValue((reference.TableId, reference.StringId), out text!);
}

/// <summary>A clipboard in memory.</summary>
internal sealed class FakeClipboard : IUiClipboard
{
    public string? Text { get; set; }

    public void SetText(string text) => Text = text;

    public string? GetText() => Text;
}

/// <summary>Records what the compositor hands over.</summary>
internal sealed class RecordingTileSink : IUiTileSink
{
    public List<UiRootFrame> Frames { get; } = [];

    public List<(UiTileUpdate Tile, byte[] Pixels)> Tiles { get; } = [];

    public List<int> Removed { get; } = [];

    public void UpdateRoot(in UiRootFrame frame) => Frames.Add(frame);

    public void UploadTile(in UiTileUpdate tile, ReadOnlySpan<byte> rgba) => Tiles.Add((tile, rgba.ToArray()));

    public void RemoveRoot(int rootId) => Removed.Add(rootId);

    public void Clear()
    {
        Frames.Clear();
        Tiles.Clear();
        Removed.Clear();
    }
}

/// <summary>A tile sink that keeps nothing (allocation tests).</summary>
internal sealed class CountingTileSink : IUiTileSink
{
    public int Tiles { get; private set; }

    public int Frames { get; private set; }

    public void UpdateRoot(in UiRootFrame frame) => Frames++;

    public void UploadTile(in UiTileUpdate tile, ReadOnlySpan<byte> rgba) => Tiles++;

    public void RemoveRoot(int rootId)
    {
    }
}

/// <summary>A manual clock for the compositor's deadline.</summary>
internal sealed class ManualTicks
{
    public long Now { get; set; }

    public long Step { get; set; }

    public long Read()
    {
        var now = Now;
        Now += Step;
        return now;
    }
}

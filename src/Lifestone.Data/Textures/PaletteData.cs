using DatReaderWriter.DBObjs;

namespace Lifestone.Data.Textures;

/// <summary>An immutable palette: 32-bit ARGB entries (0xAARRGGBB) as retail's <c>Palette::ARGB</c> holds them.</summary>
/// <remarks>
/// Palette (0x04) records store <c>u32 count</c> then count little-endian ARGB words (bytes B,G,R,A). All 4,521
/// end-of-retail palettes have 2,048 entries and every entry has alpha 255. Retail <c>Palette::InitLoad</c> widens a
/// 256-entry palette to 2,048 by repeating each colour 8 times (entry i becomes entries 8i..8i+7); that rule is
/// applied here too although no end-of-retail palette needs it.
/// </remarks>
public sealed class PaletteData
{
    /// <summary>Colour count of every runtime palette (<c>Palette::makeModifiedPalette</c> allocates 2,048).</summary>
    public const int RetailColorCount = 2048;

    private readonly uint[] _argb;

    /// <summary>Wraps <paramref name="argb"/> without copying; the caller must not modify it afterwards.</summary>
    public PaletteData(uint id, uint[] argb, string? cacheKey = null)
    {
        ArgumentNullException.ThrowIfNull(argb);
        Id = id;
        _argb = argb;
        CacheKey = cacheKey ?? $"pal{id:X8}";
    }

    /// <summary>Palette file id (0x04xxxxxx), or 0 for a palette built from sub-palette swaps.</summary>
    public uint Id { get; }

    /// <summary>
    /// Stable key identifying the colour content: <c>pal0400xxxx</c> for a dat palette, or the
    /// <see cref="PaletteOverride.CacheKey"/> it was built from. Use it to key decoded-texture caches.
    /// </summary>
    public string CacheKey { get; }

    /// <summary>Number of entries (2,048 for every retail palette).</summary>
    public int Count => _argb.Length;

    /// <summary>The ARGB entries.</summary>
    public ReadOnlySpan<uint> Argb => _argb;

    /// <summary>Entry <paramref name="index"/> as 0xAARRGGBB (<c>Palette::get_color32</c>).</summary>
    public uint this[int index] => _argb[index];

    /// <summary>Converts a DatReaderWriter Palette record, applying the retail 256-to-2,048 widening.</summary>
    public static PaletteData FromDat(Palette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var colors = palette.Colors;
        uint[] argb;
        if (colors.Count == 256)
        {
            // Palette::InitLoad: entry i is copied to 8i..8i+7.
            argb = new uint[RetailColorCount];
            for (var i = 0; i < 256; i++)
            {
                Array.Fill(argb, Pack(colors[i]), i * 8, 8);
            }
        }
        else
        {
            argb = new uint[colors.Count];
            for (var i = 0; i < argb.Length; i++)
            {
                argb[i] = Pack(colors[i]);
            }
        }

        return new PaletteData(palette.Id, argb);

        static uint Pack(DatReaderWriter.Types.ColorARGB c) =>
            ((uint)c.Alpha << 24) | ((uint)c.Red << 16) | ((uint)c.Green << 8) | c.Blue;
    }

    /// <summary>Copies the entries (for building a modified palette).</summary>
    internal uint[] CopyArgb() => (uint[])_argb.Clone();
}

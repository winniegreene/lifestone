using System.Globalization;
using System.Text;
using DatReaderWriter.DBObjs;

namespace Lifestone.Data.Textures;

/// <summary>One sub-palette swap: copy entries [Offset, Offset + Length) from palette <see cref="PaletteId"/>.</summary>
/// <param name="PaletteId">Source palette (0x04xxxxxx).</param>
/// <param name="Offset">First palette entry, in colours (not in wire units).</param>
/// <param name="Length">Number of entries, in colours.</param>
/// <remarks>
/// Entries keep their index: entry i of the result comes from entry i of the source palette
/// (<c>Palette::Modify</c>; ACViewer <c>TextureCache.IndexToColor</c> does the same).
/// </remarks>
public readonly record struct SubPalette(uint PaletteId, int Offset, int Length)
{
    /// <summary>Wire / ObjDesc units: offset and length are stored as bytes counting groups of 8 colours.</summary>
    public const int WireUnit = 8;

    /// <summary>
    /// Decodes an ObjDesc sub-palette as retail <c>Subpalette::UnPack</c> does: offset = 8 * byte; length = 8 * byte,
    /// where a length byte of 0 means 256 (2,048 colours). ACE writes the same units
    /// (<c>WorldObject_Networking.SerializeModelData</c> writes <c>(byte)Offset, (byte)Length</c> after
    /// <c>CloSubPaletteRange.Offset / 8</c>, <c>NumColors / 8</c> in <c>Creature_Networking</c>), and ACE.DatLoader
    /// <c>SubPalette.Unpack</c> mirrors retail.
    /// </summary>
    public static SubPalette FromWire(uint paletteId, byte offset, byte length) =>
        new(paletteId, offset * WireUnit, (length == 0 ? 256 : length) * WireUnit);

    /// <summary>
    /// A ClothingTable <c>CloSubPaletteRange</c> already counts colours (ACViewer applies <c>Offset</c>/<c>NumColors</c>
    /// directly; ACE divides by 8 only when it re-encodes them for the wire).
    /// </summary>
    public static SubPalette FromColorRange(uint paletteId, uint offset, uint numColors) =>
        new(paletteId, checked((int)offset), checked((int)numColors));

    /// <summary>True for a whole-palette swap (offset 0, 2,048 colours).</summary>
    public bool IsWholePalette => Offset == 0 && Length == PaletteData.RetailColorCount;

    /// <summary><c>Subpalette::replaces(this = new, change = existing)</c>: same range, or this swaps the whole palette.</summary>
    public bool Replaces(SubPalette existing) => (Offset == existing.Offset && Length == existing.Length) || IsWholePalette;

    /// <summary><c>Subpalette::supercedes(this = existing, change = new)</c>: this is whole-palette and the new one is not.</summary>
    public bool Supercedes(SubPalette incoming) => IsWholePalette && !incoming.IsWholePalette;
}

/// <summary>
/// The palette part of an ObjDesc: a base palette plus an ordered list of sub-palette swaps. This is what the M2
/// server's ObjDesc (<c>ModelData</c>) will drive.
/// </summary>
/// <remarks>
/// <para><b>Retail application</b> (<c>CPartArray::DoObjDescChanges</c> runs AnimPart changes, then texture-map changes,
/// then <c>CPartArray::SetPalette(objdesc.paletteID, objdesc.firstSubpal)</c>):</para>
/// <list type="number">
/// <item><c>Palette::makeModifiedPalette(palID, subs)</c> builds one 2,048-entry palette: a single whole-palette swap
/// simply uses that palette; otherwise copy <see cref="BasePaletteId"/> entirely, then apply every swap in list order
/// (later swaps overwrite earlier ones). See <see cref="TryBuild"/>.</item>
/// <item>The result is given (<c>CPhysicsPart::UsePalette</c> → <c>CSurface::UsePalette</c>) to every part whose
/// base palette equals <see cref="BasePaletteId"/> (<see cref="AppliesToPartBasePalette"/>). A part's base palette is
/// the original palette id of its first surface that has one (<c>CPhysicsPart::DetermineBasePal</c>); a surface's
/// original palette id is its texture's default palette (<c>CSurface::InitEnd</c> overwrites the dat's value).</item>
/// <item>Only palette-shifted (INDEX16/P8) surfaces accept it (<c>CSurface::UsePalette</c> needs handler
/// <c>SH_PALSHIFT</c>); they are re-expanded with the new palette, so the modified palette <b>replaces</b> the
/// texture's default palette rather than patching it.</item>
/// </list>
/// <para>When the swaps arrive in an ObjDesc, retail de-duplicates them as they are added (<c>ObjDesc::UnPack</c> →
/// <c>ObjDesc::AddSubpalette</c>); use <see cref="FromObjDesc"/> for wire data.</para>
/// </remarks>
public sealed class PaletteOverride
{
    /// <summary>Retail keeps at most 255 sub-palettes per ObjDesc (<c>ObjDesc::AddSubpalette</c>).</summary>
    public const int MaxSubPalettes = 255;

    /// <summary>Creates an override that applies <paramref name="subPalettes"/> exactly as given (no de-duplication).</summary>
    public PaletteOverride(uint basePaletteId, IEnumerable<SubPalette> subPalettes)
    {
        ArgumentNullException.ThrowIfNull(subPalettes);
        BasePaletteId = basePaletteId;
        SubPalettes = subPalettes.ToArray();
        CacheKey = BuildCacheKey(basePaletteId, SubPalettes);
    }

    /// <summary>The ObjDesc palette id: the palette the swaps are applied to, and the part filter.</summary>
    public uint BasePaletteId { get; }

    /// <summary>Swaps in application order.</summary>
    public IReadOnlyList<SubPalette> SubPalettes { get; }

    /// <summary>
    /// Stable text key of the colour content this override produces: base palette, then each swap as
    /// <c>+palette@offset x length</c> in colours, e.g. <c>pal04000BEF+04000C2A@192x64</c>.
    /// </summary>
    public string CacheKey { get; }

    /// <summary>
    /// Builds the swap list the way retail <c>ObjDesc::AddSubpalette</c> receives it: an incoming swap is dropped when an
    /// existing whole-palette swap supercedes it; otherwise every existing swap it replaces (same range, or it is a
    /// whole-palette swap) is removed and it is appended. More than 255 swaps are dropped.
    /// </summary>
    public static PaletteOverride FromObjDesc(uint basePaletteId, IEnumerable<SubPalette> subPalettesInWireOrder)
    {
        ArgumentNullException.ThrowIfNull(subPalettesInWireOrder);
        var list = new List<SubPalette>();
        foreach (var incoming in subPalettesInWireOrder)
        {
            if (list.Exists(existing => existing.Supercedes(incoming)))
            {
                continue;
            }

            list.RemoveAll(incoming.Replaces);
            if (list.Count < MaxSubPalettes)
            {
                list.Add(incoming);
            }
        }

        return new PaletteOverride(basePaletteId, list);
    }

    /// <summary><c>CPartArray::SetPalette</c>: the modified palette goes only to parts whose base palette is <see cref="BasePaletteId"/>.</summary>
    public bool AppliesToPartBasePalette(uint partBasePaletteId) => partBasePaletteId == BasePaletteId;

    /// <summary>
    /// Builds the modified palette as <c>Palette::makeModifiedPalette</c> does. Returns false (and retail then leaves
    /// every part's palette unchanged, <c>CPartArray::SetPalette</c> returning 0) when the base id is 0, a palette is
    /// missing, or a swap does not fit in 2,048 entries (<c>Palette::Modify</c>).
    /// </summary>
    /// <param name="resolve">Looks up a dat palette by id; null when absent.</param>
    /// <param name="palette">The modified palette (<see cref="PaletteData.Id"/> 0, <see cref="PaletteData.CacheKey"/> = <see cref="CacheKey"/>).</param>
    /// <param name="failure">Why the build failed, for logging.</param>
    public bool TryBuild(Func<uint, PaletteData?> resolve, out PaletteData? palette, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        palette = null;
        if (BasePaletteId == 0)
        {
            failure = "ObjDesc palette id is 0 (makeModifiedPalette rejects the null id).";
            return false;
        }

        // A lone whole-palette swap means "use that palette" (makeModifiedPalette's shortcut).
        if (SubPalettes.Count == 1 && SubPalettes[0].IsWholePalette)
        {
            var whole = resolve(SubPalettes[0].PaletteId);
            if (whole is null)
            {
                failure = $"Palette 0x{SubPalettes[0].PaletteId:X8} not found.";
                return false;
            }

            palette = new PaletteData(0, whole.CopyArgb(), CacheKey);
            failure = null;
            return true;
        }

        var basePalette = resolve(BasePaletteId);
        if (basePalette is null || basePalette.Count < PaletteData.RetailColorCount)
        {
            failure = basePalette is null
                ? $"Base palette 0x{BasePaletteId:X8} not found."
                : $"Base palette 0x{BasePaletteId:X8} has {basePalette.Count} entries; 2048 are copied.";
            return false;
        }

        var argb = new uint[PaletteData.RetailColorCount];
        basePalette.Argb[..PaletteData.RetailColorCount].CopyTo(argb);
        foreach (var sub in SubPalettes)
        {
            if (sub.Offset < 0 || sub.Length < 0 || sub.Offset + sub.Length > argb.Length)
            {
                failure = $"Sub-palette 0x{sub.PaletteId:X8} range [{sub.Offset}, {sub.Offset + sub.Length}) exceeds {argb.Length} entries.";
                return false;
            }

            var source = resolve(sub.PaletteId);
            if (source is null || source.Count < sub.Offset + sub.Length)
            {
                failure = source is null
                    ? $"Sub-palette 0x{sub.PaletteId:X8} not found."
                    : $"Sub-palette 0x{sub.PaletteId:X8} has only {source.Count} entries.";
                return false;
            }

            source.Argb.Slice(sub.Offset, sub.Length).CopyTo(argb.AsSpan(sub.Offset, sub.Length));
        }

        palette = new PaletteData(0, argb, CacheKey);
        failure = null;
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => CacheKey;

    private static string BuildCacheKey(uint basePaletteId, IReadOnlyList<SubPalette> subs)
    {
        var sb = new StringBuilder("pal").Append(basePaletteId.ToString("X8", CultureInfo.InvariantCulture));
        foreach (var s in subs)
        {
            sb.Append('+').Append(s.PaletteId.ToString("X8", CultureInfo.InvariantCulture))
              .Append('@').Append(s.Offset.ToString(CultureInfo.InvariantCulture))
              .Append('x').Append(s.Length.ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}

/// <summary>PaletteSet (0x0F, DatReaderWriter <see cref="PalSet"/>) lookups.</summary>
public static class PaletteSets
{
    /// <summary>
    /// Retail <c>PalSet::GetPaletteID</c>: for a shade in [0, 1] and n palettes, returns
    /// <c>Palettes[(ulong)((n - 0.000001) * shade)]</c>; returns 0 (the null id) when the set is empty or the shade is
    /// out of range. ACE's server-side ObjDesc composition uses the same formula (<c>PaletteSet.GetPaletteID</c>).
    /// </summary>
    public static uint GetPaletteId(PalSet set, double shade)
    {
        ArgumentNullException.ThrowIfNull(set);
        var n = set.Palettes.Count;
        if (n == 0 || !(shade >= 0.0 && shade <= 1.0)) // as retail, NaN fails the range test too
        {
            return 0;
        }

        return (uint)set.Palettes[(int)(ulong)((n - 0.000001) * shade)];
    }
}

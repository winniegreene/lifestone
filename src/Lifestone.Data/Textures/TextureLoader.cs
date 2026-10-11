using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Lib.IO.DatBTree;
using Lifestone.Data.Dat;

namespace Lifestone.Data.Textures;

/// <summary>A Surface resolved and decoded for drawing.</summary>
/// <param name="Material">The surface's material and render state.</param>
/// <param name="Resolution">How its SurfaceTexture resolved; null for untextured surfaces.</param>
/// <param name="Texture">The decoded texture; null for untextured surfaces (use <see cref="SurfaceMaterial.SolidColorTexel"/>).</param>
/// <param name="Palette">The palette used for INDEX16/P8 textures, else null.</param>
public sealed record DecodedSurface(
    SurfaceMaterial Material,
    SurfaceTextureResolution? Resolution,
    DecodedTexture? Texture,
    PaletteData? Palette);

/// <summary>
/// Reads and decodes textures from a <see cref="DatSet"/>: RenderSurfaces (high-res first), palettes, SurfaceTextures
/// and Surfaces. Safe to call from worker threads.
/// </summary>
/// <remarks>
/// <para>
/// Every read goes through <see cref="DatSet.Read{TResult}"/> (the dat lock) and uses DatReaderWriter's converter
/// overload of <c>TryGet</c>, which does <b>not</b> put the record in DatReaderWriter's per-database file cache:
/// <see cref="DatSet"/> opens the databases with the default <c>FileCachingStrategy.OnDemand</c>, under which
/// <see cref="DatSet.TryGetPortal{T}"/> would keep every RenderSurface it ever returned (600 MB for the portal).
/// The decode paths read RenderSurfaces as raw file bytes into pooled buffers instead (<see cref="RenderSurfaceRecord"/>,
/// <see cref="DecodeRenderSurfacePooled"/>), which allocates no pixel array per record. Decoding happens outside the lock.
/// </para>
/// <para>
/// Small derived records are cached here: SurfaceTexture resolutions and Surface materials without bound (at most
/// 7,221 and 6,152 entries), palettes in a bounded LRU (8 KiB each). Decoded textures are not cached; the caller
/// (the Godot resource layer) owns that.
/// </para>
/// </remarks>
public sealed class TextureLoader
{
    private readonly DatSet _dats;
    private readonly ConcurrentDictionary<uint, SurfaceTextureResolution?> _resolutionsHigh = new();
    private readonly ConcurrentDictionary<uint, SurfaceTextureResolution?> _resolutionsLow = new();
    private readonly ConcurrentDictionary<uint, SurfaceMaterial?> _surfaces = new();
    private readonly LruCache<uint, PaletteData> _palettes;

    /// <summary>Creates a loader over <paramref name="dats"/>.</summary>
    /// <param name="dats">The opened dats (not owned).</param>
    /// <param name="paletteCacheCapacity">Palettes kept in memory (8 KiB each).</param>
    public TextureLoader(DatSet dats, int paletteCacheCapacity = 512)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paletteCacheCapacity);
        _dats = dats;
        _palettes = new LruCache<uint, PaletteData>(paletteCacheCapacity);
    }

    /// <summary>The dats this loader reads.</summary>
    public DatSet Dats => _dats;

    /// <summary>
    /// True when <c>client_highres.dat</c> is open. Without it, two-level SurfaceTextures resolve to level 1 whatever
    /// <see cref="TextureDetail"/> is requested, as retail's <c>Render::ShouldDropHighDetail</c> does.
    /// </summary>
    public bool HighResInstalled => _dats.HighRes is not null;

    /// <summary>
    /// Reads a RenderSurface without caching it, looking in <c>client_highres.dat</c> first, then the portal.
    /// </summary>
    public RenderSurface? ReadRenderSurface(uint renderSurfaceId, out TextureDat source)
    {
        var (surface, dat) = _dats.Read(d =>
        {
            if (TryReadUncached<RenderSurface>(d.HighRes, renderSurfaceId, out var hi))
            {
                return (hi, TextureDat.HighRes);
            }

            return TryReadUncached<RenderSurface>(d.Portal, renderSurfaceId, out var portal)
                ? (portal, TextureDat.Portal)
                : ((RenderSurface?)null, TextureDat.Portal);
        });
        source = dat;
        return surface;
    }

    /// <summary>True when a RenderSurface id exists in <c>client_highres.dat</c> or the portal (no record is read).</summary>
    public bool RenderSurfaceExists(uint renderSurfaceId, out TextureDat source)
    {
        var found = _dats.Read(d =>
            d.HighRes is not null && d.HighRes.Tree.HasFile(renderSurfaceId) ? 1
            : d.Portal.Tree.HasFile(renderSurfaceId) ? 2
            : 0);
        source = found == 1 ? TextureDat.HighRes : TextureDat.Portal;
        return found != 0;
    }

    /// <summary>A dat palette (0x04xxxxxx) as ARGB entries, or null when absent. Cached (LRU).</summary>
    public PaletteData? GetPalette(uint paletteId)
    {
        if (_palettes.TryGet(paletteId, out var cached))
        {
            return cached;
        }

        var palette = _dats.Read(d => TryReadUncached<Palette>(d.Portal, paletteId, out var p) ? p : null);
        if (palette is null)
        {
            return null;
        }

        var data = PaletteData.FromDat(palette);
        _palettes.Add(paletteId, data);
        return data;
    }

    /// <summary>Builds the modified palette of an ObjDesc (see <see cref="PaletteOverride.TryBuild"/>).</summary>
    public bool TryBuildPalette(PaletteOverride paletteOverride, out PaletteData? palette, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(paletteOverride);
        return paletteOverride.TryBuild(GetPalette, out palette, out failure);
    }

    /// <summary>
    /// Resolves a SurfaceTexture to a RenderSurface (see <see cref="SurfaceTextureResolution"/>); null when the
    /// SurfaceTexture record itself does not exist. Cached.
    /// </summary>
    public SurfaceTextureResolution? ResolveSurfaceTexture(uint surfaceTextureId, TextureDetail detail = TextureDetail.High)
    {
        var cache = detail == TextureDetail.High ? _resolutionsHigh : _resolutionsLow;
        return cache.GetOrAdd(surfaceTextureId, id => Resolve(id, detail));
    }

    /// <summary>The material of a Surface (0x08xxxxxx), or null when absent. Cached.</summary>
    public SurfaceMaterial? GetSurface(uint surfaceId) =>
        _surfaces.GetOrAdd(surfaceId, id =>
        {
            var surface = _dats.Read(d => TryReadUncached<Surface>(d.Portal, id, out var s) ? s : null);
            return surface is null ? null : SurfaceMaterial.FromDat(id, surface);
        });

    /// <summary>
    /// Reads and decodes a RenderSurface (high-res first). INDEX16/P8 use <paramref name="palette"/> when given,
    /// else the RenderSurface's default palette.
    /// </summary>
    /// <remarks>The record is read into a pooled buffer (<see cref="DecodeRenderSurfacePooled"/>); only the result's own
    /// array is allocated.</remarks>
    /// <exception cref="KeyNotFoundException">The RenderSurface or its default palette does not exist.</exception>
    public DecodedTexture DecodeRenderSurface(uint renderSurfaceId, TextureDecodeMode mode, bool clipMap = false, PaletteData? palette = null)
    {
        var (buffer, record, _) = ReadRecordPooled(renderSurfaceId)
            ?? throw new KeyNotFoundException($"RenderSurface 0x{renderSurfaceId:X8} is in neither client_highres.dat nor client_portal.dat.");
        try
        {
            return Decode(buffer, record, mode, clipMap, palette, out _);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// The buffer-taking decode: like <see cref="DecodeRenderSurface"/>, but the record is read into a buffer rented from
    /// <see cref="ArrayPool{T}.Shared"/> and the texels stay in pooled memory (a slice of the record for DXT, JPEG and
    /// Alpha8; a rented RGBA8 buffer otherwise). No texel array is allocated. Dispose the result to return its buffer.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The RenderSurface or its default palette does not exist.</exception>
    public PooledTexture DecodeRenderSurfacePooled(uint renderSurfaceId, TextureDecodeMode mode, bool clipMap = false, PaletteData? palette = null)
    {
        var (buffer, record, _) = ReadRecordPooled(renderSurfaceId)
            ?? throw new KeyNotFoundException($"RenderSurface 0x{renderSurfaceId:X8} is in neither client_highres.dat nor client_portal.dat.");
        PaletteData? used;
        try
        {
            used = PaletteFor(record, palette);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }

        // DecodePooled owns the buffer from here on (returns it on failure too).
        return RenderSurfaceDecoder.DecodePooled(
            renderSurfaceId, record.Format, record.Width, record.Height, buffer, record.DataOffset, record.DataLength, mode,
            used is null ? default : used.Argb, clipMap);
    }

    /// <summary>
    /// Resolves and decodes a Surface for drawing: material, SurfaceTexture resolution, texture and palette.
    /// Returns null when the Surface record does not exist.
    /// </summary>
    /// <param name="surfaceId">Surface id (0x08xxxxxx).</param>
    /// <param name="mode">Texture output layout.</param>
    /// <param name="palette">
    /// Palette to use instead of the texture's default (an ObjDesc modified palette, when
    /// <see cref="PaletteOverride.AppliesToPartBasePalette"/> holds for the part). Ignored for non-indexed textures, as
    /// retail <c>CSurface::UsePalette</c> ignores them.
    /// </param>
    /// <param name="surfaceTextureOverride">A replacement SurfaceTexture id (ObjDesc TextureMapChange), or null.</param>
    /// <param name="detail">Source level preference (see <see cref="TextureDetail"/> for retail's default).</param>
    /// <exception cref="InvalidDataException">The SurfaceTexture is absent or none of its RenderSurfaces exists.</exception>
    public DecodedSurface? DecodeSurface(
        uint surfaceId, TextureDecodeMode mode, PaletteData? palette = null, uint? surfaceTextureOverride = null,
        TextureDetail detail = TextureDetail.High)
    {
        var material = GetSurface(surfaceId);
        if (material is null)
        {
            return null;
        }

        if (!material.IsTextured)
        {
            return new DecodedSurface(material, null, null, null);
        }

        var stId = surfaceTextureOverride ?? material.SurfaceTextureId;
        var resolution = ResolveSurfaceTexture(stId, detail)
            ?? throw new InvalidDataException($"Surface 0x{surfaceId:X8}: SurfaceTexture 0x{stId:X8} does not exist.");
        if (!resolution.IsResolved)
        {
            throw new InvalidDataException($"Surface 0x{surfaceId:X8}: {resolution}.");
        }

        var (buffer, record, _) = ReadRecordPooled(resolution.RenderSurfaceId)
            ?? throw new InvalidDataException($"Surface 0x{surfaceId:X8}: RenderSurface 0x{resolution.RenderSurfaceId:X8} vanished.");
        try
        {
            var texture = Decode(buffer, record, mode, material.IsClipMap, palette, out var used);
            return new DecodedSurface(material, resolution, texture, used);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Decodes a record read by <see cref="ReadRecordPooled"/> into arrays of its own (the buffer stays the caller's).</summary>
    private DecodedTexture Decode(byte[] buffer, RenderSurfaceRecord record, TextureDecodeMode mode, bool clipMap, PaletteData? palette, out PaletteData? used)
    {
        used = PaletteFor(record, palette);
        return RenderSurfaceDecoder.Decode(
            record.Id, record.Format, record.Width, record.Height, buffer.AsSpan(record.DataOffset, record.DataLength), mode,
            used is null ? default : used.Argb, clipMap);
    }

    /// <summary>The palette an INDEX16/P8 record decodes with (the override, else its default); null for other formats.</summary>
    private PaletteData? PaletteFor(RenderSurfaceRecord record, PaletteData? palette)
    {
        if (!RenderSurfaceDecoder.IsIndexed(record.Format))
        {
            return null;
        }

        return palette ?? GetPalette(record.DefaultPaletteId)
            ?? throw new KeyNotFoundException($"RenderSurface 0x{record.Id:X8}: default palette 0x{record.DefaultPaletteId:X8} not found.");
    }

    /// <summary>
    /// Reads a RenderSurface record's raw bytes (high-res first, then the portal) under the dat lock into a buffer
    /// rented from <see cref="ArrayPool{T}.Shared"/>, and parses its header (<see cref="RenderSurfaceRecord"/>). Null when
    /// the id is in neither dat. The caller owns (and returns) the buffer.
    /// </summary>
    /// <remarks>
    /// DatReaderWriter's <c>TryGet&lt;RenderSurface&gt;</c> would allocate a new pixel array per record and copy it one
    /// byte at a time while holding the dat lock, which every worker waits on; reading the file bytes into a pooled
    /// buffer costs one block copy and no allocation (wave-3 performance pass, docs/textures.md §8).
    /// </remarks>
    private (byte[] Buffer, RenderSurfaceRecord Record, TextureDat Source)? ReadRecordPooled(uint renderSurfaceId)
    {
        var (buffer, length, source) = _dats.Read(d =>
        {
            if (TryReadPooled(d.HighRes, renderSurfaceId, out var highRes, out var highResLength))
            {
                return (highRes, highResLength, TextureDat.HighRes);
            }

            return TryReadPooled(d.Portal, renderSurfaceId, out var portal, out var portalLength)
                ? (portal, portalLength, TextureDat.Portal)
                : ((byte[]?)null, 0, TextureDat.Portal);
        });
        if (buffer is null)
        {
            return null;
        }

        try
        {
            return (buffer, RenderSurfaceRecord.Parse(renderSurfaceId, buffer.AsSpan(0, length)), source);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    /// <summary>Reads a file's bytes into a pooled buffer. Call under the dat lock.</summary>
    private static bool TryReadPooled(DatDatabase? database, uint id, [NotNullWhen(true)] out byte[]? buffer, out int length)
    {
        buffer = null;
        length = 0;
        if (database is null || !database.Tree.TryGetFile(id, out var entry))
        {
            return false;
        }

        if (entry.Flags.HasFlag(DatBTreeFileFlags.IsCompressed))
        {
            // No RenderSurface in the end-of-retail dats is compressed; DatReaderWriter inflates into a new array here.
            if (!database.TryGetFileBytes(id, out var inflated, autoDecompress: true))
            {
                return false;
            }

            buffer = ArrayPool<byte>.Shared.Rent(inflated.Length);
            inflated.CopyTo(buffer, 0);
            length = inflated.Length;
            return true;
        }

        length = checked((int)entry.Size);
        buffer = ArrayPool<byte>.Shared.Rent(length);
        if (database.TryGetFileBytes(id, buffer.AsSpan(0, length), out var read) && read == length)
        {
            return true;
        }

        ArrayPool<byte>.Shared.Return(buffer);
        buffer = null;
        throw new InvalidDataException($"File 0x{id:X8}: read {read} of its {length} bytes.");
    }

    private SurfaceTextureResolution? Resolve(uint surfaceTextureId, TextureDetail detail)
    {
        var st = _dats.Read(d => TryReadUncached<SurfaceTexture>(d.Portal, surfaceTextureId, out var s) ? s : null);
        if (st is null)
        {
            return null;
        }

        var ids = st.Textures.Select(t => t.DataId).ToArray();
        return SurfaceTextureResolution.Resolve(
            surfaceTextureId, ids, detail, HighResInstalled, id => RenderSurfaceExists(id, out var dat) ? dat : null);
    }

    /// <summary>Reads a record without adding it to DatReaderWriter's file cache. Call under the dat lock.</summary>
    private static bool TryReadUncached<T>(DatDatabase? database, uint id, out T? value)
        where T : class, IDBObj
    {
        if (database is not null && database.TryGet<T, T>(id, static x => x, out var result))
        {
            value = result;
            return true;
        }

        value = null;
        return false;
    }
}

using System.Collections.Concurrent;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Appearance;

/// <summary>
/// <see cref="IAppearanceSource"/> over the user's dats: LOD chains (<see cref="LodChain.Load"/>), GfxObj surface lists,
/// Surface materials and palettes (<see cref="TextureLoader"/>). Thread-safe; chains and surface lists are cached
/// without bound (one entry per GfxObj an object uses).
/// </summary>
/// <param name="textures">The texture loader (its <see cref="TextureLoader.Dats"/> are read for chains and GfxObjs).</param>
/// <param name="detail">
/// The texture level whose palette counts as a surface's original palette (<see cref="TextureLoader.GetOriginalPaletteId"/>).
/// Use the level the renderer draws.
/// </param>
/// <param name="meshes">Optional mesh cache; when given, surface lists come from its meshes instead of a GfxObj read.</param>
public sealed class DatAppearanceSource(TextureLoader textures, TextureDetail detail = TextureDetail.High, GfxObjMeshCache? meshes = null)
    : IAppearanceSource, IAppearanceDats
{
    private readonly TextureLoader _textures = textures ?? throw new ArgumentNullException(nameof(textures));
    private readonly ConcurrentDictionary<uint, LodChain> _chains = new();
    private readonly ConcurrentDictionary<uint, IReadOnlyList<uint>> _surfaces = new();
    private readonly DatAppearanceDats _records = new(textures.Dats);

    /// <summary>The texture level used for original palettes.</summary>
    public TextureDetail Detail { get; } = detail;

    /// <inheritdoc/>
    public LodChain GetChain(uint gfxObjId) => _chains.GetOrAdd(gfxObjId, id => LodChain.Load(_textures.Dats, id));

    /// <inheritdoc/>
    public IReadOnlyList<uint> SurfaceIds(uint gfxObjId) => _surfaces.GetOrAdd(gfxObjId, id =>
    {
        if (meshes is not null)
        {
            return meshes.Get(id)?.SurfaceIds ?? [];
        }

        return _textures.Dats.TryGetPortal<GfxObj>(id, out var gfxObj)
            ? gfxObj.Surfaces.Select(s => s.DataId).ToArray()
            : [];
    });

    /// <inheritdoc/>
    public uint OriginalTextureId(uint surfaceId) => _textures.GetSurface(surfaceId)?.SurfaceTextureId ?? 0;

    /// <inheritdoc/>
    public uint OriginalPaletteId(uint surfaceId) => _textures.GetOriginalPaletteId(surfaceId, Detail);

    /// <inheritdoc/>
    public PaletteData? GetPalette(uint paletteId) => _textures.GetPalette(paletteId);

    /// <inheritdoc/>
    public ClothingTable? GetClothingTable(uint clothingTableId) => _records.GetClothingTable(clothingTableId);

    /// <inheritdoc/>
    public PalSet? GetPalSet(uint palSetId) => _records.GetPalSet(palSetId);
}

/// <summary><see cref="IAppearanceDats"/> over a <see cref="DatSet"/> (portal records, read on each call).</summary>
/// <param name="dats">The opened dats (not owned).</param>
public sealed class DatAppearanceDats(DatSet dats) : IAppearanceDats
{
    private readonly DatSet _dats = dats ?? throw new ArgumentNullException(nameof(dats));

    /// <inheritdoc/>
    public ClothingTable? GetClothingTable(uint clothingTableId) =>
        clothingTableId != 0 && _dats.TryGetPortal<ClothingTable>(clothingTableId, out var table) ? table : null;

    /// <inheritdoc/>
    public PalSet? GetPalSet(uint palSetId) =>
        palSetId != 0 && _dats.TryGetPortal<PalSet>(palSetId, out var set) ? set : null;
}

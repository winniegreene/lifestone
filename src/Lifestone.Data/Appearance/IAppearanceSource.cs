using DatReaderWriter.DBObjs;
using Lifestone.Data.Models;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Appearance;

/// <summary>
/// What <see cref="PartArrayAppearance"/> reads from the dats: part LOD chains, surface lists, each surface's original
/// texture and palette, and palettes. <see cref="DatAppearanceSource"/> is the dat-backed implementation; tests
/// supply their own.
/// </summary>
/// <remarks>Implementations used by several threads must be thread-safe (<see cref="DatAppearanceSource"/> is).</remarks>
public interface IAppearanceSource
{
    /// <summary>
    /// The GfxObjs a part draws when it references <paramref name="gfxObjId"/> (<c>CPhysicsPart::LoadGfxObjArray</c>,
    /// 11.4186 0x0050DCF0; 11.6096 0x0050E7C0). <see cref="LodChain.IsLoadable"/> is false when retail's
    /// <c>CPhysicsPart::SetPart</c> would fail.
    /// </summary>
    LodChain GetChain(uint gfxObjId);

    /// <summary>
    /// The Surface ids (<c>0x08xxxxxx</c>) of GfxObj <paramref name="gfxObjId"/> in index order: the part's surface array
    /// when that GfxObj is its level 0 (<c>CPhysicsPart::SetGfxObjArray</c>). Empty when the GfxObj is missing.
    /// </summary>
    IReadOnlyList<uint> SurfaceIds(uint gfxObjId);

    /// <summary>
    /// The SurfaceTexture a Surface was loaded with (<c>CSurface::GetOriginalTextureMapID</c>, 11.4186 0x005363E0): its
    /// stored texture when it is textured, else 0. 0 for a missing Surface.
    /// </summary>
    uint OriginalTextureId(uint surfaceId);

    /// <summary>
    /// The palette a Surface reports as original (<c>CSurface::GetOriginalPaletteID</c>, 11.4186 0x005363F0): what
    /// <c>CSurface::InitEnd</c> leaves there after loading (see <see cref="TextureLoader.GetOriginalPaletteId"/>).
    /// </summary>
    uint OriginalPaletteId(uint surfaceId);

    /// <summary>
    /// A dat palette (<c>0x04xxxxxx</c>), or null when absent: the base and the sources of modified palettes
    /// (<c>Palette::makeModifiedPalette</c>, <c>Palette::Modify</c>).
    /// </summary>
    PaletteData? GetPalette(uint paletteId);
}

/// <summary>
/// The records <see cref="ClothingObjDesc"/> and <see cref="CharGenAppearance"/> read. <see cref="DatAppearanceDats"/>
/// reads them from a <see cref="Dat.DatSet"/>; tests supply their own.
/// </summary>
public interface IAppearanceDats
{
    /// <summary>A ClothingTable (<c>0x10xxxxxx</c>), or null when absent.</summary>
    ClothingTable? GetClothingTable(uint clothingTableId);

    /// <summary>A PaletteSet (<c>0x0Fxxxxxx</c>), or null when absent.</summary>
    PalSet? GetPalSet(uint palSetId);
}

/// <summary>
/// The outcome of applying appearance changes: whether retail's call would have returned success, and a readable line
/// for each change that failed. Nothing fails silently: every <c>false</c> carries at least one problem.
/// </summary>
/// <param name="AllApplied">Retail's return value: true when every change was applied.</param>
/// <param name="Problems">One line per failed change (empty on success).</param>
public readonly record struct AppearanceResult(bool AllApplied, IReadOnlyList<string> Problems)
{
    /// <summary>Everything applied.</summary>
    public static AppearanceResult Success { get; } = new(true, []);

    /// <summary>One failure.</summary>
    public static AppearanceResult Failure(string problem) => new(false, [problem]);

    /// <summary>A result from collected problems: success when there are none.</summary>
    public static AppearanceResult From(List<string>? problems) =>
        problems is null || problems.Count == 0 ? Success : new AppearanceResult(false, [.. problems]);

    /// <summary>Both results: applied only when both were; the problems in order.</summary>
    public AppearanceResult Combine(AppearanceResult other)
    {
        if (other.AllApplied && other.Problems.Count == 0)
        {
            return this;
        }

        if (AllApplied && Problems.Count == 0)
        {
            return other;
        }

        return new AppearanceResult(AllApplied && other.AllApplied, [.. Problems, .. other.Problems]);
    }

    /// <inheritdoc/>
    public override string ToString() => AllApplied ? "applied" : "failed: " + string.Join("; ", Problems);
}

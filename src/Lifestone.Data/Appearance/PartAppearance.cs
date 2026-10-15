using System.Globalization;
using System.Text;
using Lifestone.Data.Models;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Appearance;

/// <summary>
/// How one part of an object is drawn after its ObjDesc changes: which GfxObjs (per LOD level), which SurfaceTexture each
/// of its surfaces draws, and which palette its palette-shifted surfaces use.
/// </summary>
/// <param name="Index">The Setup part index.</param>
/// <param name="Chain">The part's GfxObjs per LOD level (the Setup's own part, or an ObjDesc part swap).</param>
/// <param name="BasePaletteId">
/// <c>CPhysicsPart::original_palette_id</c> (<c>CPhysicsPart::DetermineBasePal</c>, 11.4186 0x0050DC80; 11.6096
/// 0x0050E750): the original palette of the first level-0 surface that has one, else 0. An ObjDesc palette goes only to
/// parts whose base palette equals its <see cref="ObjDesc.PaletteId"/>.
/// </param>
/// <param name="SurfaceTextureBySurfaceId">
/// Texture swaps: Surface id (<c>0x08xxxxxx</c>) → the SurfaceTexture (<c>0x05xxxxxx</c>) it draws instead of its own.
/// Retail swaps per surface index, but the match is on the surface's original texture, which every index of one Surface
/// shares, so keying by Surface id is exact; and since every LOD level draws with the level-0 surface array
/// (<see cref="LodChain"/>), the map holds for every level. Pass the value to
/// <see cref="TextureLoader.DecodeSurface"/> as its texture override.
/// </param>
/// <param name="Palette">
/// The part's modified palette (<c>CPhysicsPart::shiftPal</c>), or null for the textures' own palettes. Pass it to
/// <see cref="TextureLoader.DecodeSurface"/> for every surface; only INDEX16/P8 textures use it, as only retail's
/// palette-shifted surfaces accept it (<c>CSurface::UsePalette</c>).
/// </param>
/// <param name="VariantKey">
/// "" when the part draws its GfxObjs' own surfaces (no texture swap, no palette), so meshes keyed by GfxObj stay shared
/// with M1's static objects; otherwise <c>|tm:&lt;surface&gt;=&lt;texture&gt;,...|pal:&lt;palette cache key&gt;</c>, to
/// append to the part's mesh and material keys.
/// </param>
public sealed record PartAppearance(
    int Index,
    LodChain Chain,
    uint BasePaletteId,
    IReadOnlyDictionary<uint, uint> SurfaceTextureBySurfaceId,
    PaletteData? Palette,
    string VariantKey)
{
    /// <summary>The texture swaps of no part.</summary>
    internal static IReadOnlyDictionary<uint, uint> NoSwaps { get; } = new Dictionary<uint, uint>();

    /// <summary>The SurfaceTexture surface <paramref name="surfaceId"/> draws instead of its own, or null.</summary>
    public uint? TextureOverrideFor(uint surfaceId) =>
        SurfaceTextureBySurfaceId.TryGetValue(surfaceId, out var texture) ? texture : null;

    /// <summary>Builds <see cref="VariantKey"/> from the swaps and the palette (swaps in Surface id order).</summary>
    internal static string BuildVariantKey(IReadOnlyDictionary<uint, uint> swaps, PaletteData? palette)
    {
        if (swaps.Count == 0 && palette is null)
        {
            return "";
        }

        var sb = new StringBuilder();
        if (swaps.Count > 0)
        {
            sb.Append("|tm:");
            var first = true;
            foreach (var surface in swaps.Keys.Order())
            {
                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                sb.Append(surface.ToString("X8", CultureInfo.InvariantCulture))
                  .Append('=').Append(swaps[surface].ToString("X8", CultureInfo.InvariantCulture));
            }
        }

        if (palette is not null)
        {
            sb.Append("|pal:").Append(palette.CacheKey);
        }

        return sb.ToString();
    }
}

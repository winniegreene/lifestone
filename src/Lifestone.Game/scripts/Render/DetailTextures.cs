using Godot;
using Lifestone.Client.Rendering;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain.Blending;
using Lifestone.Data.Textures;
using Lifestone.Game.Core;

namespace Lifestone.Game.Render;

/// <summary>One of the Region's detail surfaces as the surface shaders sample it.</summary>
/// <param name="Texture">The detail texture (RGBA8 with a box-filtered mip chain; the shaders clamp to retail's 4 levels).</param>
/// <param name="Tiling">Retail <c>Render::*_detail_tiling</c>: detail UV = tiling x base UV.</param>
/// <param name="SurfaceTextureId">The SurfaceTexture (0x05) it comes from (logs).</param>
public sealed record DetailTexture(ImageTexture Texture, float Tiling, uint SurfaceTextureId);

/// <summary>
/// The Region's building and environment detail textures (render-fidelity fix; docs/render-contract.md §2.4,
/// <see cref="SurfaceDetail"/>): retail <c>LScape::SetDetailTexturing</c> builds them from TMTerrainDesc[1] and [2]
/// (<see cref="TerrainAtlasManifest.DetailTextures"/>, which also says which ones retail ever generates) while the
/// "Environment Detail Textures" preference is on, its default.
/// </summary>
/// <remarks>
/// Built once on the main thread before streaming (a 256x256 texture in the end-of-retail dats, shared by both kinds).
/// Retail makes the detail surface from the SurfaceTexture directly (<c>LScape::GenerateDetailSurface</c>:
/// <c>CSurface::UseTextureMap</c> on the ImgTex of that id), so its texture resolves like any other SurfaceTexture
/// (<see cref="TextureLoader.ResolveSurfaceTexture"/>). Not refcounted: it lives as long as the render resources.
/// </remarks>
public sealed class DetailTextures
{
    private readonly Dictionary<SurfaceDetail, DetailTexture> _textures;

    private DetailTextures(Dictionary<SurfaceDetail, DetailTexture> textures) => _textures = textures;

    /// <summary>The detail texture of <paramref name="kind"/>, or null when the Region has none.</summary>
    public DetailTexture? Get(SurfaceDetail kind) => _textures.GetValueOrDefault(kind);

    /// <summary>Whether <paramref name="kind"/> has a texture (thread-safe once built: the dictionary is never written again).</summary>
    public bool Has(SurfaceDetail kind) => _textures.ContainsKey(kind);

    /// <summary>Main thread: decodes and uploads the detail textures retail generates. Failures are logged and leave the kind out.</summary>
    public static DetailTextures Load(DatSet dats, TextureLoader loader)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(loader);
        var textures = new Dictionary<SurfaceDetail, DetailTexture>();
        var uploaded = new Dictionary<uint, ImageTexture>();
        foreach (var entry in TerrainAtlasManifest.FromDatSet(dats).DetailTextures)
        {
            var kind = entry.Kind switch
            {
                TerrainDetailKind.Building => SurfaceDetail.Building,
                TerrainDetailKind.Environment => SurfaceDetail.Environment,
                _ => SurfaceDetail.None,
            };

            // Landscape and object detail are never generated in retail (TerrainDetailRetailUse.Never).
            if (kind == SurfaceDetail.None || entry.RetailUse != TerrainDetailRetailUse.EnvironmentDetailPreference || entry.SurfaceTextureId == 0)
            {
                continue;
            }

            try
            {
                if (!uploaded.TryGetValue(entry.SurfaceTextureId, out var texture))
                {
                    var resolution = loader.ResolveSurfaceTexture(entry.SurfaceTextureId);
                    if (resolution is null || !resolution.IsResolved)
                    {
                        Log.Error("render", "detail texture does not resolve; that detail pass is off", ("kind", kind), ("surface_texture", $"0x{entry.SurfaceTextureId:X8}"), ("resolution", resolution));
                        continue;
                    }

                    var decoded = loader.DecodeRenderSurface(resolution.RenderSurfaceId, TextureDecodeMode.Rgba8);
                    if (decoded.Format != DecodedTextureFormat.Rgba8)
                    {
                        // Only JPEG stays undecoded; no Region detail texture is one (0x06006D58 is A8R8G8B8).
                        Log.Error("render", "TODO(render): detail texture is not RGBA8 after decoding; that detail pass is off", ("kind", kind), ("format", decoded.Format));
                        continue;
                    }

                    using var image = Image.CreateFromData(decoded.Width, decoded.Height, false, Image.Format.Rgba8, decoded.Data);
                    image.GenerateMipmaps();
                    texture = ImageTexture.CreateFromImage(image);
                    texture.ResourceName = $"detail_0x{entry.SurfaceTextureId:X8}";
                    uploaded.Add(entry.SurfaceTextureId, texture);
                }

                textures[kind] = new DetailTexture(texture, entry.Tiling, entry.SurfaceTextureId);
                Log.Info("render", "detail texture", ("kind", kind), ("surface_texture", $"0x{entry.SurfaceTextureId:X8}"), ("tiling", entry.Tiling), ("size", $"{texture.GetWidth()}x{texture.GetHeight()}"));
            }
            catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or NotSupportedException)
            {
                Log.Error("render", "detail texture failed to decode; that detail pass is off", ("kind", kind), ("surface_texture", $"0x{entry.SurfaceTextureId:X8}"), ("error", ex.Message));
            }
        }

        return new DetailTextures(textures);
    }

    /// <summary>A detail set from explicit textures (engine checks).</summary>
    public static DetailTextures FromTextures(IReadOnlyDictionary<SurfaceDetail, DetailTexture> textures) => new(new Dictionary<SurfaceDetail, DetailTexture>(textures));
}

using Lifestone.Data.Textures;

namespace Lifestone.Data.Tests.Textures;

/// <summary>
/// M3 4.2 p6 (q6-texture-override-fallback): an ObjDesc texture change whose new texture cannot load leaves the surface on its own texture (retail's failed change: <c>CSurface::UseTextureMap</c> 11.6096 0x00537020 is not
/// applied), where the loader used to throw and the part's whole mesh was dropped. The fallback is counted and told once per texture.
/// </summary>
public sealed class TextureOverrideFallbackTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint ClipMapSurface = 0x0800000Au;
    private const uint MissingTexture = 0x05FFFFFFu;

    [RequiresDatsFact]
    public void An_override_that_cannot_load_draws_the_surfaces_own_texture_and_says_so_once()
    {
        var told = new List<string>();
        var loader = new TextureLoader(fixture.Dats) { OverrideFallback = told.Add };
        var own = loader.DecodeSurface(ClipMapSurface, TextureDecodeMode.Rgba8);
        var changed = loader.DecodeSurface(ClipMapSurface, TextureDecodeMode.Rgba8, surfaceTextureOverride: MissingTexture);
        Assert.NotNull(changed);
        Assert.Equal(own!.Resolution!.RenderSurfaceId, changed.Resolution!.RenderSurfaceId);
        Assert.Equal(own.Texture!.Data, changed.Texture!.Data);
        Assert.Equal(1, loader.OverrideFallbacks);

        _ = loader.DecodeSurface(ClipMapSurface, TextureDecodeMode.Rgba8, surfaceTextureOverride: MissingTexture);
        Assert.Equal(2, loader.OverrideFallbacks);
        var line = Assert.Single(told);
        Assert.Contains("0x05FFFFFF", line, StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void A_surface_with_no_override_and_a_texture_that_loads_is_unchanged()
    {
        var loader = new TextureLoader(fixture.Dats);
        Assert.NotNull(loader.DecodeSurface(ClipMapSurface, TextureDecodeMode.Rgba8));
        Assert.Equal(0, loader.OverrideFallbacks);
    }
}

namespace Lifestone.Client.Rendering;

/// <summary>
/// Identity of an uploaded texture: retail decodes a RenderSurface per (palette, clip-map) combination, so the cache key
/// is (RenderSurface id, palette key, clip-map flag) as docs/textures.md §7 requires, plus the upload form.
/// </summary>
/// <param name="RenderSurfaceId">The RenderSurface (0x06) drawn.</param>
/// <param name="PaletteKey"><c>"default"</c> for the RenderSurface's own default palette (or none), else <c>PaletteData.CacheKey</c>.</param>
/// <param name="ClipMap">Base1ClipMap surface: palette indices below 8 become transparent.</param>
/// <param name="AlphaCritical">
/// True only for the RGBA8 upload of a DXT1 texture with punch-through texels requested by a blended or alpha-tested
/// surface under the DXT passthrough policy (Godot uploads FORMAT_DXT1 as BC1 without alpha; docs/render-contract.md
/// §3). Opaque surfaces share the block upload of the same texture under the key with this flag false.
/// </param>
public readonly record struct TextureKey(uint RenderSurfaceId, string PaletteKey, bool ClipMap, bool AlphaCritical)
{
    public const string DefaultPalette = "default";

    public override string ToString() => $"0x{RenderSurfaceId:X8}/{PaletteKey}{(ClipMap ? "/clip" : string.Empty)}{(AlphaCritical ? "/alpha" : string.Empty)}";
}

/// <summary>Cache key of a material: the surface, the exact texture (null for untextured surfaces) and the shader variant.</summary>
public readonly record struct MaterialKey(uint SurfaceId, TextureKey? Texture, SurfaceShaderVariant Variant)
{
    public override string ToString() => $"0x{SurfaceId:X8}/{Variant.Name}{(Texture is { } t ? "/" + t : string.Empty)}";
}

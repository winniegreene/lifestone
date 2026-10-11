using Lifestone.Data.Textures;

namespace Lifestone.Data.Terrain.Blending;

/// <summary>Where one atlas layer's pixels came from.</summary>
/// <param name="Layer">Layer index in its array (manifest order).</param>
/// <param name="SurfaceTextureId">SurfaceTexture (0x05) the manifest names.</param>
/// <param name="RenderSurfaceId">RenderSurface (0x06) that was decoded.</param>
/// <param name="Dat">Dat the RenderSurface was read from.</param>
/// <param name="SourceFormat">Pixel format in the dat.</param>
/// <param name="SourceSize">Edge length in the dat, which is the array's size (<see cref="TerrainAtlasBuilder"/> never resizes).</param>
public sealed record TerrainAtlasLayerSource(
    int Layer,
    uint SurfaceTextureId,
    uint RenderSurfaceId,
    TextureDat Dat,
    DatReaderWriter.Enums.PixelFormat SourceFormat,
    int SourceSize);

/// <summary>
/// The decoded terrain texture arrays, ready to upload: one RGBA8 colour array in <see cref="TerrainAtlasManifest.TerrainLayers"/>
/// order and one 8-bit alpha array in <see cref="TerrainAtlasManifest.AlphaLayers"/> order, each layer with its full
/// box-filtered mip chain (<see cref="TerrainMipChain"/>). Plain data, built on a worker thread by
/// <see cref="TerrainAtlasBuilder"/>; the Godot layer only wraps the bytes in <c>Image</c>s and <c>Texture2DArray</c>s.
/// </summary>
public sealed class TerrainAtlasPixels
{
    internal TerrainAtlasPixels(
        TerrainAtlasManifest manifest,
        int colorSize,
        int alphaSize,
        byte[][] colorLayers,
        byte[][] alphaLayers,
        TerrainAtlasLayerSource[] colorSources,
        TerrainAtlasLayerSource[] alphaSources)
    {
        Manifest = manifest;
        ColorSize = colorSize;
        AlphaSize = alphaSize;
        ColorLayers = colorLayers;
        AlphaLayers = alphaLayers;
        ColorSources = colorSources;
        AlphaSources = alphaSources;

        LayerTiling = manifest.TerrainLayers.Select(l => (float)l.TexTiling).ToArray();

        // The shader picks one "merged texture" level of detail per pixel (retail mip-maps the BaseTexSize² merged cell
        // texture) and samples each array at the level with the same texel density: a colour layer repeated `tiling` times
        // per cell has ColorSize * tiling texels per cell, an alpha map AlphaSize texels per cell.
        LayerLodOffset = manifest.TerrainLayers.Select(l => Log2Ratio(manifest.BaseTexSize, (double)colorSize * l.TexTiling)).ToArray();
        AlphaLodOffset = Log2Ratio(manifest.BaseTexSize, alphaSize);
    }

    /// <summary>The manifest the arrays follow.</summary>
    public TerrainAtlasManifest Manifest { get; }

    /// <summary>Edge length of every colour layer (512 in Dereth).</summary>
    public int ColorSize { get; }

    /// <summary>Edge length of every alpha layer (512 in Dereth).</summary>
    public int AlphaSize { get; }

    /// <summary>Mip levels per colour layer (full chain).</summary>
    public int ColorMipLevels => TerrainMipChain.LevelCount(ColorSize);

    /// <summary>Mip levels per alpha layer (full chain).</summary>
    public int AlphaMipLevels => TerrainMipChain.LevelCount(AlphaSize);

    /// <summary>Colour layers: RGBA8 (R, G, B, A bytes), full mip chain, level 0 first, top row first.</summary>
    public IReadOnlyList<byte[]> ColorLayers { get; }

    /// <summary>Alpha layers: one byte per texel (255 = keep what is underneath), full mip chain, level 0 first.</summary>
    public IReadOnlyList<byte[]> AlphaLayers { get; }

    /// <summary>Provenance of each colour layer.</summary>
    public IReadOnlyList<TerrainAtlasLayerSource> ColorSources { get; }

    /// <summary>Provenance of each alpha layer.</summary>
    public IReadOnlyList<TerrainAtlasLayerSource> AlphaSources { get; }

    /// <summary>Repeats per cell of each colour layer (<see cref="TerrainAtlasManifest.TilingOfLayer"/>), for the shader's <c>layer_tiling</c>.</summary>
    public IReadOnlyList<float> LayerTiling { get; }

    /// <summary>
    /// Per colour layer, log2(BaseTexSize / (ColorSize × tiling)): how many levels coarser than the merged texture's level
    /// the layer is sampled (0 in Dereth: 512 × 2 = 1024). Shader uniform <c>layer_lod_offset</c>.
    /// </summary>
    public IReadOnlyList<float> LayerLodOffset { get; }

    /// <summary>log2(BaseTexSize / AlphaSize): retail upscales the alpha maps to the merged size (1 in Dereth). Shader uniform <c>alpha_lod_offset</c>.</summary>
    public float AlphaLodOffset { get; }

    /// <summary>Bytes of the colour array including mips.</summary>
    public long ColorBytes => ColorLayers.Sum(l => (long)l.Length);

    /// <summary>Bytes of the alpha array including mips.</summary>
    public long AlphaBytes => AlphaLayers.Sum(l => (long)l.Length);

    /// <summary>Level 0 of a colour layer as a <see cref="TerrainImage"/> (a copy), for <see cref="TerrainCompositor"/>.</summary>
    public TerrainImage ColorImage(int layer)
    {
        var data = ColorLayers[layer];
        var pixels = new TerrainRgba[ColorSize * ColorSize];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new TerrainRgba(data[i * 4], data[(i * 4) + 1], data[(i * 4) + 2], data[(i * 4) + 3]);
        }

        return new TerrainImage(ColorSize, ColorSize, pixels);
    }

    /// <summary>Level 0 of an alpha layer as a <see cref="TerrainAlphaImage"/> (a copy), for <see cref="TerrainCompositor"/>.</summary>
    public TerrainAlphaImage AlphaImage(int layer) =>
        new(AlphaSize, AlphaSize, AlphaLayers[layer].AsSpan(0, AlphaSize * AlphaSize).ToArray());

    private static float Log2Ratio(double numerator, double denominator) => (float)Math.Log2(numerator / denominator);
}

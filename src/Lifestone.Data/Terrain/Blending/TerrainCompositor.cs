using System.Numerics;

namespace Lifestone.Data.Terrain.Blending;

/// <summary>An 8-bit RGBA texel.</summary>
public readonly record struct TerrainRgba(byte R, byte G, byte B, byte A)
{
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}

/// <summary>
/// Samples a terrain colour layer at texture coordinates (u, v). Coordinates are <b>not</b> wrapped: the sampler repeats
/// (like a GPU REPEAT sampler). v = 0 is the top row of the image, which retail maps to the cell's north edge.
/// </summary>
public delegate TerrainRgba TerrainTexelSampler(int textureLayer, float u, float v);

/// <summary>Samples an alpha layer at (u, v) in [0, 1] (clamp). Returns the 8-bit alpha (255 = keep what is underneath).</summary>
public delegate byte TerrainAlphaSampler(int alphaLayer, float u, float v);

/// <summary>A decoded RGBA image, row-major, row 0 first (the top of the texture).</summary>
public sealed class TerrainImage
{
    public TerrainImage(int width, int height, TerrainRgba[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || pixels.Length != width * height)
        {
            throw new ArgumentException($"Expected {width}x{height} pixels, got {pixels.Length}.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public TerrainRgba[] Pixels { get; }

    public TerrainRgba this[int x, int y] => Pixels[(y * Width) + x];

    /// <summary>Nearest texel with repeat addressing.</summary>
    public TerrainRgba SampleNearestRepeat(float u, float v) =>
        this[Wrap(u, Width), Wrap(v, Height)];

    internal static int Wrap(float t, int size)
    {
        var f = t - MathF.Floor(t);
        var i = (int)(f * size);
        return i >= size ? size - 1 : i;
    }
}

/// <summary>A decoded 8-bit alpha map, row-major, row 0 first.</summary>
public sealed class TerrainAlphaImage
{
    public TerrainAlphaImage(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || pixels.Length != width * height)
        {
            throw new ArgumentException($"Expected {width}x{height} pixels, got {pixels.Length}.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    public byte this[int x, int y] => Pixels[(y * Width) + x];

    /// <summary>Nearest texel with clamp addressing.</summary>
    public byte SampleNearestClamp(float u, float v) => this[Clamp(u, Width), Clamp(v, Height)];

    private static int Clamp(float t, int size)
    {
        var i = (int)MathF.Floor(t * size);
        return i < 0 ? 0 : i >= size ? size - 1 : i;
    }
}

/// <summary>
/// CPU reference for the retail landscape texture merge. The wave-2 terrain shader is golden-tested against this.
/// </summary>
/// <remarks>
/// <para>
/// Retail (<c>TexMerge::FillTempTexBuffer</c>, acclient.c:305909) builds one <c>BaseTexSize</c>² (1024²) texture per
/// palette code on the CPU, then draws the cell's two triangles with it:
/// </para>
/// <list type="number">
/// <item><c>ImgTex::TileCSI</c> (acclient.c:365513): the base texture (512²) is copied <c>tex_tiling</c> × <c>tex_tiling</c>
/// times (2 × 2) into the buffer.</item>
/// <item>For each overlay, then each road layer, <c>ImgTex::MergeTexture</c> (acclient.c:365632) walks the buffer: the
/// alpha map (512²) is pixel-replicated up to the buffer size and read through its rotation; the layer texture is read
/// with wrap-around (texel (x mod 512, y mod 512), so it also repeats twice per cell); per colour byte
/// <c>a' = a + (a &gt; 128 ? 1 : 0)</c>, <c>dst = (a' * dst + (256 - a') * src) &gt;&gt; 8</c>, and texels with a = 255 are
/// left alone. a = 255 keeps what is underneath, a = 0 is 100 % of the new layer. The alpha byte of the buffer is untouched.</item>
/// </list>
/// <para>
/// UVs: retail's land UVs are SW (0,1), SE (1,1), NE (1,0), NW (0,0) (<c>CLandBlock::init</c> fills
/// <c>CLandBlockStruct::land_uvs</c>, acclient.c:351988; <c>ConstructUVs</c> with rotation 0), i.e. u runs east and v runs
/// south, and buffer row 0 is the north edge.
/// </para>
/// <para>
/// Tiling: <c>TileCSI</c> uses the base's <c>tex_tiling</c>; <c>MergeTexture</c> ignores its <c>tiling</c> argument and
/// repeats the layer every <c>texture size</c> buffer texels, i.e. BaseTexSize / 512 = 2 times. The two agree whenever
/// BaseTexSize = TexTiling × texture size, which the tests verify for every terrain texture in the user's dats; this
/// class therefore samples every layer at <c>uv * TexTiling</c> of that layer.
/// </para>
/// </remarks>
public static class TerrainCompositor
{
    /// <summary>
    /// Final colour of a cell at cell UV (u, v) — u east, v south, both in [0, 1] — using retail's sequential integer merge.
    /// With nearest-texel samplers and texel-centre UVs this equals <see cref="ComposeRetailTexture"/> exactly.
    /// </summary>
    public static TerrainRgba Compose(
        in TerrainCellBlend blend,
        TerrainAtlasManifest manifest,
        float u,
        float v,
        TerrainTexelSampler texels,
        TerrainAlphaSampler alphas)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(texels);
        ArgumentNullException.ThrowIfNull(alphas);

        var color = SampleLayer(texels, manifest, blend.BaseTextureLayer, u, v);
        for (var i = 0; i < blend.OverlayCount; i++)
        {
            var o = blend.GetOverlay(i);
            color = MergeAt(color, texels, alphas, manifest, o.TextureLayer, o.AlphaLayer, o.Rotation, u, v);
        }

        for (var i = 0; i < blend.RoadCount; i++)
        {
            var r = blend.GetRoad(i);
            color = MergeAt(color, texels, alphas, manifest, blend.RoadTextureLayer, r.AlphaLayer, r.Rotation, u, v);
        }

        return color;
    }

    /// <summary>
    /// The same merge in floating point with <see cref="RetailWeight"/> and no per-step truncation: the arithmetic the
    /// Godot shader is expected to do (see docs/terrain-shader.md). RGB in [0, 1]; W = base alpha / 255.
    /// </summary>
    public static Vector4 ComposeFloat(
        in TerrainCellBlend blend,
        TerrainAtlasManifest manifest,
        float u,
        float v,
        TerrainTexelSampler texels,
        TerrainAlphaSampler alphas)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(texels);
        ArgumentNullException.ThrowIfNull(alphas);

        var color = ToVector(SampleLayer(texels, manifest, blend.BaseTextureLayer, u, v));
        for (var i = 0; i < blend.OverlayCount; i++)
        {
            var o = blend.GetOverlay(i);
            color = MergeFloatAt(color, texels, alphas, manifest, o.TextureLayer, o.AlphaLayer, o.Rotation, u, v);
        }

        for (var i = 0; i < blend.RoadCount; i++)
        {
            var r = blend.GetRoad(i);
            color = MergeFloatAt(color, texels, alphas, manifest, blend.RoadTextureLayer, r.AlphaLayer, r.Rotation, u, v);
        }

        return color;
    }

    /// <summary>
    /// Alpha-map UV for a rotation, from cell UV (u east, v south): r0 (u, v), r1 (1 - v, u), r2 (1 - u, 1 - v),
    /// r3 (v, 1 - u). Derived from the start offsets and strides of <c>ImgTex::MergeTexture</c>.
    /// </summary>
    public static (float U, float V) RotateAlphaUv(TerrainRotation rotation, float u, float v) => rotation switch
    {
        TerrainRotation.Rot0 => (u, v),
        TerrainRotation.Rot1 => (1f - v, u),
        TerrainRotation.Rot2 => (1f - u, 1f - v),
        TerrainRotation.Rot3 => (v, 1f - u),
        _ => throw new ArgumentOutOfRangeException(nameof(rotation), rotation, null),
    };

    /// <summary>
    /// Retail per-texel merge (<c>ImgTex::MergeTexture</c>): returns <paramref name="dst"/> unchanged when
    /// <paramref name="alpha"/> is 255, else per colour channel <c>(a' * dst + (256 - a') * src) &gt;&gt; 8</c> with
    /// <c>a' = a + (a &gt; 128)</c>. The alpha channel of <paramref name="dst"/> is kept.
    /// </summary>
    public static TerrainRgba Merge(TerrainRgba dst, TerrainRgba src, byte alpha)
    {
        if (alpha == 0xFF)
        {
            return dst;
        }

        var a = alpha > 0x80 ? alpha + 1 : alpha;
        var b = 256 - a;
        return new TerrainRgba(
            (byte)(((a * dst.R) + (b * src.R)) >> 8),
            (byte)(((a * dst.G) + (b * src.G)) >> 8),
            (byte)(((a * dst.B) + (b * src.B)) >> 8),
            dst.A);
    }

    /// <summary>
    /// Fraction of the underlying colour that retail keeps for an alpha byte: (a + (a &gt; 128 ? 1 : 0)) / 256. This is 1
    /// for a = 255, so retail's "skip 255" shortcut changes nothing; it differs from a / 255 by at most 1/256.
    /// </summary>
    public static float RetailWeight(byte alpha) => (alpha + (alpha > 0x80 ? 1 : 0)) / 256f;

    /// <summary>
    /// Builds retail's merged cell texture exactly as <c>FillTempTexBuffer</c> does at full landscape texture detail
    /// (buffer = <see cref="TerrainAtlasManifest.BaseTexSize"/>², integer texel addressing). Row 0 is the north edge.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The texture sizes are not the configuration retail handles (layer size × tiling must fill the buffer; alpha maps must
    /// be square, the same size as the layer textures, a power of two, and smaller than the buffer).
    /// </exception>
    public static TerrainImage ComposeRetailTexture(
        in TerrainCellBlend blend,
        TerrainAtlasManifest manifest,
        Func<int, TerrainImage> textures,
        Func<int, TerrainAlphaImage> alphaMaps)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(alphaMaps);

        var size = checked((int)manifest.BaseTexSize);
        var buffer = new TerrainRgba[size * size];

        // TileCSI: tex_tiling x tex_tiling copies of the base at offsets that are multiples of its size.
        var baseImage = textures(blend.BaseTextureLayer);
        var tiling = checked((int)manifest.TilingOfLayer(blend.BaseTextureLayer));
        if (baseImage.Width * tiling != size || baseImage.Height * tiling != size)
        {
            throw new NotSupportedException(
                $"Base layer {blend.BaseTextureLayer} is {baseImage.Width}x{baseImage.Height} with tiling {tiling}; " +
                $"TileCSI only fills a {size}x{size} buffer when size x tiling matches.");
        }

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                buffer[(y * size) + x] = baseImage[x % baseImage.Width, y % baseImage.Height];
            }
        }

        for (var i = 0; i < blend.OverlayCount; i++)
        {
            var o = blend.GetOverlay(i);
            MergeTexture(buffer, size, textures(o.TextureLayer), alphaMaps(o.AlphaLayer), o.Rotation);
        }

        for (var i = 0; i < blend.RoadCount; i++)
        {
            var r = blend.GetRoad(i);
            MergeTexture(buffer, size, textures(blend.RoadTextureLayer), alphaMaps(r.AlphaLayer), r.Rotation);
        }

        return new TerrainImage(size, size, buffer);
    }

    /// <summary>
    /// The Gouraud-interpolated vertex light at cell UV (u, v) as the D3D pipeline sees it: the three corner lights of the
    /// triangle containing the point (split per <see cref="TerrainSplit"/>, triangles as in
    /// <c>CLandBlockStruct::ConstructPolygons</c>), each as its truncated D3DCOLOR byte / 255, blended barycentrically.
    /// </summary>
    /// <param name="lights">81 vertex lights of the landblock (index x*9+y).</param>
    /// <param name="cellX">Cell x (0-7).</param>
    /// <param name="cellY">Cell y (0-7).</param>
    /// <param name="swToNe">The cell's diagonal (<see cref="TerrainSplit.IsSWtoNE(LandblockCoord, int, int)"/>).</param>
    /// <param name="u">Cell u (east).</param>
    /// <param name="v">Cell v (south; 0 = north edge).</param>
    public static Vector3 InterpolateVertexLight(IReadOnlyList<TerrainVertexLight> lights, int cellX, int cellY, bool swToNe, float u, float v)
    {
        ArgumentNullException.ThrowIfNull(lights);
        var sw = Bytes(lights[TerrainConstants.VertexIndex(cellX, cellY)]);
        var se = Bytes(lights[TerrainConstants.VertexIndex(cellX + 1, cellY)]);
        var ne = Bytes(lights[TerrainConstants.VertexIndex(cellX + 1, cellY + 1)]);
        var nw = Bytes(lights[TerrainConstants.VertexIndex(cellX, cellY + 1)]);
        var east = u;
        var north = 1f - v;
        if (swToNe)
        {
            // (SW, SE, NE) below the SW-NE diagonal, (SW, NE, NW) above it.
            return east >= north
                ? (sw * (1f - east)) + (se * (east - north)) + (ne * north)
                : (sw * (1f - north)) + (ne * east) + (nw * (north - east));
        }

        // (SW, SE, NW) below the SE-NW diagonal, (NE, NW, SE) above it.
        return east + north <= 1f
            ? (sw * (1f - east - north)) + (se * east) + (nw * north)
            : (ne * (east + north - 1f)) + (nw * (1f - east)) + (se * (1f - north));

        static Vector3 Bytes(TerrainVertexLight l) => new(l.R8 / 255f, l.G8 / 255f, l.B8 / 255f);
    }

    /// <summary>Fixed-function MODULATE of a merged texel by a vertex light (RGB in [0, 1], before fog).</summary>
    public static Vector3 Modulate(TerrainRgba texel, Vector3 light) =>
        new(texel.R / 255f * light.X, texel.G / 255f * light.Y, texel.B / 255f * light.Z);

    /// <summary>Samplers over decoded images: nearest texel, repeat for colour, clamp for alpha.</summary>
    public static (TerrainTexelSampler Texels, TerrainAlphaSampler Alphas) NearestSamplers(
        Func<int, TerrainImage> textures,
        Func<int, TerrainAlphaImage> alphaMaps)
    {
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(alphaMaps);
        return ((layer, u, v) => textures(layer).SampleNearestRepeat(u, v),
                (layer, u, v) => alphaMaps(layer).SampleNearestClamp(u, v));
    }

    private static TerrainRgba SampleLayer(TerrainTexelSampler texels, TerrainAtlasManifest manifest, int layer, float u, float v)
    {
        float tiling = manifest.TilingOfLayer(layer);
        return texels(layer, u * tiling, v * tiling);
    }

    private static TerrainRgba MergeAt(
        TerrainRgba color,
        TerrainTexelSampler texels,
        TerrainAlphaSampler alphas,
        TerrainAtlasManifest manifest,
        int textureLayer,
        int alphaLayer,
        TerrainRotation rotation,
        float u,
        float v)
    {
        var (au, av) = RotateAlphaUv(rotation, u, v);
        var alpha = alphas(alphaLayer, au, av);
        return alpha == 0xFF ? color : Merge(color, SampleLayer(texels, manifest, textureLayer, u, v), alpha);
    }

    private static Vector4 MergeFloatAt(
        Vector4 color,
        TerrainTexelSampler texels,
        TerrainAlphaSampler alphas,
        TerrainAtlasManifest manifest,
        int textureLayer,
        int alphaLayer,
        TerrainRotation rotation,
        float u,
        float v)
    {
        var (au, av) = RotateAlphaUv(rotation, u, v);
        var w = RetailWeight(alphas(alphaLayer, au, av));
        var src = ToVector(SampleLayer(texels, manifest, textureLayer, u, v));
        return new Vector4(
            (color.X * w) + (src.X * (1f - w)),
            (color.Y * w) + (src.Y * (1f - w)),
            (color.Z * w) + (src.Z * (1f - w)),
            color.W);
    }

    private static Vector4 ToVector(TerrainRgba c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

    /// <summary>
    /// <c>ImgTex::MergeTexture</c>, scale-up branch (buffer wider than the alpha map), re-expressed with explicit texel
    /// coordinates. Retail addresses the alpha map with the layer texture's width and height, so they must match.
    /// </summary>
    private static void MergeTexture(TerrainRgba[] buffer, int size, TerrainImage layer, TerrainAlphaImage alpha, TerrainRotation rotation)
    {
        var w = layer.Width;
        var h = layer.Height;
        if (alpha.Width != w || alpha.Height != h || w != h || (w & (w - 1)) != 0 || size <= w || size % w != 0)
        {
            throw new NotSupportedException(
                $"MergeTexture with a {w}x{h} layer, a {alpha.Width}x{alpha.Height} alpha map and a {size}x{size} buffer " +
                "is outside the configuration retail supports (square power-of-two maps of equal size, smaller than the buffer).");
        }

        var scale = size / w;
        for (var y = 0; y < size; y++)
        {
            var ay = y / scale;
            for (var x = 0; x < size; x++)
            {
                var ax = x / scale;
                var a = rotation switch
                {
                    TerrainRotation.Rot0 => alpha[ax, ay],
                    TerrainRotation.Rot1 => alpha[w - 1 - ay, ax],
                    TerrainRotation.Rot2 => alpha[w - 1 - ax, h - 1 - ay],
                    _ => alpha[ay, h - 1 - ax],
                };

                if (a != 0xFF)
                {
                    var i = (y * size) + x;
                    buffer[i] = Merge(buffer[i], layer[x & (w - 1), y % h], a);
                }
            }
        }
    }
}

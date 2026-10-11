using System.Buffers;
using System.Collections.Concurrent;
using Godot;
using Lifestone.Client.Caching;
using Lifestone.Client.Rendering;
using Lifestone.Client.Textures;
using Lifestone.Data.Textures;
using Lifestone.Game.Core;

namespace Lifestone.Game.Render;

/// <summary>What the render layer needs to know about a texture without touching its texels again.</summary>
/// <param name="Width">Texels.</param>
/// <param name="Height">Texels.</param>
/// <param name="HasAlpha">Some texel has alpha below 255.</param>
/// <param name="PassingTexelsBlend">Some texel has alpha in [200, 254]: it passes retail's clip-map alpha test and is then
/// blended. A property of the texels alone (the shader variant applies it only to alpha-tested surfaces).</param>
/// <param name="SourceFormat">Dat pixel format, for diagnostics.</param>
/// <param name="Dxt1WithAlpha">A DXT1 texture with punch-through texels (needs RGBA8 where alpha is used).</param>
public readonly record struct TextureInfo(int Width, int Height, bool HasAlpha, bool PassingTexelsBlend, string SourceFormat, bool Dxt1WithAlpha);

/// <summary>
/// A texture decoded on a worker into a CPU-side Godot <see cref="Image"/> (mips included), ready to upload: one
/// <b>lease</b> on an image that several requests may share (<see cref="TextureCache.PrepareShared"/>,
/// <see cref="SharedDecodes{TKey, TValue}"/>). The image is freed when the last lease is disposed:
/// <see cref="TextureCache.TryAcquire(PreparedTexture, out ImageTexture)"/> disposes the lease it uploads (or finds
/// redundant), and <see cref="Dispose"/> drops one that was never used (a stale landblock), so native image buffers do
/// not wait for the garbage collector.
/// </summary>
public sealed class PreparedTexture : IDisposable
{
    private readonly SharedDecodes<DecodeKey, PreparedImage>.Lease _lease;

    /// <summary>A texture with a single owner (no sharing).</summary>
    public PreparedTexture(TextureKey key, Image image, TextureInfo info, long bytes)
        : this(SharedDecodes<DecodeKey, PreparedImage>.Owned(new PreparedImage(key, image, info, bytes), PreparedImage.Free))
    {
    }

    internal PreparedTexture(SharedDecodes<DecodeKey, PreparedImage>.Lease lease) => _lease = lease;

    public TextureKey Key => _lease.Value.Key;

    /// <summary>The decoded image; invalid once <see cref="IsDisposed"/>.</summary>
    public Image Image => _lease.Value.Image;

    public TextureInfo Info => _lease.Value.Info;

    /// <summary>Approximate GPU bytes (all mip levels).</summary>
    public long Bytes => _lease.Value.Bytes;

    /// <summary>True once this lease was disposed (uploaded or dropped) or the shared image was freed.</summary>
    public bool IsDisposed => _lease.IsDisposed;

    /// <summary>Drops this lease (once, from any thread); the image is freed with the last one.</summary>
    public void Dispose() => _lease.Dispose();
}

/// <summary>A decoded image before anyone holds it (the result of <see cref="TextureCache.Prepare"/>'s work).</summary>
internal sealed record PreparedImage(TextureKey Key, Image Image, TextureInfo Info, long Bytes)
{
    /// <summary>Frees the image (the last lease is gone).</summary>
    public static void Free(PreparedImage image) => image.Image.Dispose();
}

/// <summary>
/// What a decode depends on: the RenderSurface, the palette, the clip-map rule, whether the surface uses alpha (DXT1
/// with punch-through texels is expanded only then) and the DXT policy.
/// </summary>
internal readonly record struct DecodeKey(uint RenderSurfaceId, string PaletteKey, bool ClipMap, bool Deferred, DxtUploadPolicy Policy);

/// <summary>
/// Godot textures for AC RenderSurfaces: decoding and mip generation on workers, <see cref="ImageTexture"/> creation on
/// the main thread, reference-counted with least-recently-used eviction by bytes in the shared
/// <see cref="RenderResourceCaches{TTexture, TMaterial, TMesh}"/> (materials hold texture references only while they are in
/// use, so the byte budget really bounds texture memory; evicted textures are disposed at once).
/// </summary>
/// <remarks>
/// <para><b>Upload forms</b> (docs/render-contract.md §3):</para>
/// <list type="bullet">
/// <item>Everything but DXT and JPEG is expanded to RGBA8 by <see cref="TextureLoader"/> (palettes, clip maps, BGR
/// orders as docs/textures.md specifies).</item>
/// <item>DXT under the default <see cref="DxtUploadPolicy.PassthroughWhereSafe"/>: level 0 is the dat's blocks unchanged
/// (<c>Image.FORMAT_DXT1/3/5</c>); levels 1+ are box-filtered from the RGBA8 decode and re-encoded
/// (<see cref="MipChain.BuildBlockChain(ReadOnlySpan{byte}, Span{byte}, int, int, BlockFormat, Span{byte})"/>), as retail's
/// D3DXFilterTexture does. The exception is DXT1 with
/// punch-through texels on a blended or alpha-tested surface: Godot uploads FORMAT_DXT1 as BC1 <i>without</i> alpha
/// (RenderingDevice format <c>BC1_RGB_UNORM_BLOCK</c>, verified by <c>--engine-checks</c>), which turns retail's
/// transparent texels opaque black, so those are expanded to RGBA8. <see cref="DxtUploadPolicy.Expand"/> expands every
/// DXT texture.</item>
/// <item>JPEG: <c>Image.LoadJpgFromBuffer</c> (an ordinary JPEG decode, no channel swap; docs/textures.md §2).</item>
/// </list>
/// <para><b>Mipmaps.</b> Godot's <c>Image.GenerateMipmaps</c> (uncompressed images) and <see cref="MipChain"/> (DXT) build
/// the full chain with a 2x2 box filter on the raw bytes (no sRGB conversion), matching retail's
/// <c>D3DXFilterTexture(D3DX_FILTER_BOX)</c> for the levels retail has.
/// Retail creates at most 4 levels (<c>ImgTex::CreateD3DTexture</c>); the surface shader clamps sampling to level 3
/// (<c>ac_max_mip_level</c>) instead of truncating the chain, which Godot cannot do for an <see cref="ImageTexture"/>.</para>
/// <para><b>Threading.</b> <see cref="Prepare"/> creates Godot <see cref="Image"/>s on worker threads: Image is a plain
/// CPU-side resource that Godot allows off the main thread (no RenderingServer call). The <see cref="ImageTexture"/> is
/// created on the main thread.</para>
/// <para><b>Allocation</b> (wave-3 performance pass; docs/textures.md §8). A decode reads the record and keeps every
/// intermediate (record, RGBA8 expansion, mip chain) in buffers rented from <see cref="ArrayPool{T}.Shared"/>, and hands
/// them to Godot as spans, so it allocates no texel arrays; the DXT path decodes the RGBA8 it needs for the mip levels
/// from the same blocks instead of reading the record a second time. <see cref="PrepareShared"/> also makes concurrent and
/// pending requests for the same texture share one decode: the 3×3 and the prefetch ring prepare together at start-up
/// and after a teleport, and used to decode each shared tree, rock and building texture once per landblock.</para>
/// </remarks>
public sealed class TextureCache
{
    private readonly RenderResourceCaches<ImageTexture, ShaderMaterial, ArrayMesh> _caches;
    private readonly ConcurrentDictionary<TextureKey, TextureInfo> _resident;

    // Concurrent and pending requests for one texture share one decode (wave-3 performance pass).
    private readonly SharedDecodes<DecodeKey, PreparedImage> _decodes = new(PreparedImage.Free);

    /// <summary>Creates the texture side of the shared caches.</summary>
    /// <param name="caches">The shared caches.</param>
    /// <param name="resident">Info of cached textures (worker-readable); the host removes a texture's entry when it frees it.</param>
    public TextureCache(RenderResourceCaches<ImageTexture, ShaderMaterial, ArrayMesh> caches, ConcurrentDictionary<TextureKey, TextureInfo> resident)
    {
        _caches = caches;
        _resident = resident;
    }

    /// <summary>Cache statistics for the perf overlay.</summary>
    public IResourceCacheStats Stats => _caches.Textures;

    /// <summary>Uploads made since start.</summary>
    public long Uploads { get; private set; }

    /// <summary>Uploads that kept DXT blocks (with re-encoded mips) since start.</summary>
    public long BlockUploads { get; private set; }

    /// <summary>Decodes run by <see cref="PrepareShared"/> since start.</summary>
    public long SharedPathDecodes => _decodes.Decodes;

    /// <summary>Requests <see cref="PrepareShared"/> served from another request's decode since start.</summary>
    public long SharedPathHits => _decodes.SharedHits;

    /// <summary>
    /// Worker-safe: the info of a texture that is (very probably) resident, so a worker can skip decoding it. A texture
    /// evicted between this call and the main-thread acquire is decoded again there (logged).
    /// </summary>
    public bool TryGetResidentInfo(TextureKey key, out TextureInfo info) => _resident.TryGetValue(key, out info);

    /// <summary>Main thread: takes a reference to a resident texture.</summary>
    public bool TryAcquire(TextureKey key, out ImageTexture texture) => _caches.TryAcquireTexture(key, out texture!);

    /// <summary>
    /// Main thread: takes a reference to the texture of <paramref name="prepared"/>, uploading it unless another request
    /// already did. The lease is disposed either way. Returns false only when the texture is not resident and
    /// the prepared image was already consumed (the caller then decodes again).
    /// </summary>
    public bool TryAcquire(PreparedTexture prepared, out ImageTexture texture)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (_caches.TryAcquireTexture(prepared.Key, out texture!))
        {
            prepared.Dispose();
            return true;
        }

        if (prepared.IsDisposed)
        {
            return false;
        }

        texture = ImageTexture.CreateFromImage(prepared.Image);
        texture.ResourceName = prepared.Key.ToString();
        Uploads++;
        if (prepared.Image.GetFormat() is Image.Format.Dxt1 or Image.Format.Dxt3 or Image.Format.Dxt5)
        {
            BlockUploads++;
        }

        // The pixels now live in the RenderingServer: drop this lease (the CPU copy goes with the last one).
        prepared.Dispose();
        _resident[prepared.Key] = prepared.Info;
        _caches.AddTexture(prepared.Key, texture, prepared.Bytes);
        return true;
    }

    /// <summary>Main thread: drops a reference.</summary>
    public void Release(TextureKey key) => _caches.ReleaseTexture(key);

    /// <summary>
    /// Worker: <see cref="Prepare"/>, but requests for the same texture share one decode — whether they arrive while it
    /// runs (they wait for it) or later, while some lease still holds the image (they take a lease on it). Each call
    /// returns its own lease; dispose it as before.
    /// </summary>
    /// <exception cref="InvalidDataException">As <see cref="Prepare"/> (every waiter of a failed decode gets the error).</exception>
    public PreparedTexture PrepareShared(TextureLoader loader, SurfaceMaterial material, uint renderSurfaceId, PaletteData? palette, DxtUploadPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(material);
        var key = new DecodeKey(renderSurfaceId, palette?.CacheKey ?? TextureKey.DefaultPalette, material.IsClipMap, material.RenderState.IsDeferred, policy);
        return new PreparedTexture(_decodes.Acquire(key, () => Decode(loader, material, renderSurfaceId, palette, policy)));
    }

    /// <summary>
    /// Worker: decodes a surface's texture and builds its Godot image with mips. Pure CPU work, no RenderingServer; the
    /// result has a single owner (see <see cref="PrepareShared"/> for the shared form).
    /// </summary>
    /// <param name="loader">The shared texture loader.</param>
    /// <param name="material">The Surface (textured).</param>
    /// <param name="renderSurfaceId">The resolved RenderSurface.</param>
    /// <param name="palette">An ObjDesc palette, or null for the texture's default.</param>
    /// <param name="policy">DXT upload policy.</param>
    public static PreparedTexture Prepare(TextureLoader loader, SurfaceMaterial material, uint renderSurfaceId, PaletteData? palette, DxtUploadPolicy policy)
    {
        var image = Decode(loader, material, renderSurfaceId, palette, policy);
        return new PreparedTexture(image.Key, image.Image, image.Info, image.Bytes);
    }

    private static PreparedImage Decode(TextureLoader loader, SurfaceMaterial material, uint renderSurfaceId, PaletteData? palette, DxtUploadPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(material);
        var mode = policy == DxtUploadPolicy.Expand ? TextureDecodeMode.Rgba8 : TextureDecodeMode.Native;
        using var decoded = loader.DecodeRenderSurfacePooled(renderSurfaceId, mode, material.IsClipMap, palette);
        if (BlockFormatOf(decoded.Format) is not { } blockFormat)
        {
            // HasAlpha first: without a texel below 255 none can lie in [200, 254].
            var passing = decoded.HasAlpha && decoded.AnyAlphaInRange(SurfaceMaterial.ClipMapAlphaTestReference, 254);
            var info = new TextureInfo(decoded.Width, decoded.Height, decoded.HasAlpha, passing, decoded.SourceFormat.ToString(), Dxt1WithAlpha: false);
            var (image, bytes) = ToImage(decoded.Format, decoded.Data, decoded.Width, decoded.Height, decoded.SourceId);
            return new PreparedImage(KeyFor(material, renderSurfaceId, palette, policy, dxt1WithAlpha: false), image, info, bytes);
        }

        // The RGBA8 of level 0 feeds the mip levels and the alpha checks, whatever form level 0 takes; it is decoded from
        // the same blocks (BlockCompression, as RenderSurfaceDecoder's RGBA8 mode does), not read again.
        var (w, h) = (decoded.Width, decoded.Height);
        var size = w * h * 4;
        var rgba = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            var level0 = rgba.AsSpan(0, size);
            BlockCompression.DecodeToRgba8(decoded.Format, decoded.Data, w, h, level0);
            var dxt1WithAlpha = decoded.Format == DecodedTextureFormat.Dxt1 && decoded.HasAlpha;
            var passing = TexelAlpha.RgbaAnyInRange(level0, SurfaceMaterial.ClipMapAlphaTestReference, 254);
            var info = new TextureInfo(w, h, decoded.HasAlpha, passing, decoded.SourceFormat.ToString(), dxt1WithAlpha);
            var key = KeyFor(material, renderSurfaceId, palette, policy, dxt1WithAlpha);

            // FORMAT_DXT1 is BC1 without alpha in Godot: punch-through texels would turn opaque black. Retail's D3D9
            // DXT1 makes them transparent, which matters only where alpha is used (blended or alpha-tested surfaces).
            var (image, bytes) = dxt1WithAlpha && material.RenderState.IsDeferred
                ? ToImage(DecodedTextureFormat.Rgba8, level0, w, h, decoded.SourceId)
                : ToBlockImage(decoded.Data, level0, w, h, blockFormat, decoded.SourceId);
            return new PreparedImage(key, image, info, bytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rgba);
        }
    }

    /// <summary>
    /// A DXT texture with a full mip chain: the dat's blocks at level 0, re-encoded box-filtered levels below
    /// (<see cref="MipChain.BuildBlockChain(ReadOnlySpan{byte}, ReadOnlySpan{byte}, int, int, BlockFormat)"/>). Falls back to
    /// the RGBA8 path if the blocks do not cover the image.
    /// </summary>
    public static (Image Image, long Bytes) ToBlockImage(DecodedTexture blocks, DecodedTexture rgba)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(rgba);
        var format = BlockFormatOf(blocks.Format) ?? throw new ArgumentException($"{blocks.Format} is not a block format.", nameof(blocks));
        return ToBlockImage(blocks.Data, (byte[])rgba.Data.Clone(), blocks.Width, blocks.Height, format, blocks.SourceId);
    }

    /// <summary>
    /// The pooled form: <paramref name="level0Rgba"/> is used as scratch (overwritten by the box-filtered levels) and the
    /// chain is built in a pooled buffer that Godot copies.
    /// </summary>
    private static (Image Image, long Bytes) ToBlockImage(ReadOnlySpan<byte> blocks, Span<byte> level0Rgba, int width, int height, BlockFormat format, uint sourceId)
    {
        if (blocks.Length != MipChain.BlockLevelSize(width, height, format))
        {
            Log.Warn("render", "DXT data does not match its size; expanding", ("texture", $"0x{sourceId:X8}"));
            return ToImage(DecodedTextureFormat.Rgba8, level0Rgba, width, height, sourceId);
        }

        var chainSize = MipChain.BlockChainSize(width, height, format);
        var chain = ArrayPool<byte>.Shared.Rent(chainSize);
        try
        {
            MipChain.BuildBlockChain(blocks, level0Rgba, width, height, format, chain);
            var godotFormat = format switch
            {
                BlockFormat.Bc1 => Image.Format.Dxt1,
                BlockFormat.Bc2 => Image.Format.Dxt3,
                _ => Image.Format.Dxt5,
            };
            return (Image.CreateFromData(width, height, true, godotFormat, chain.AsSpan(0, chainSize)), chainSize);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chain);
        }
    }

    private static BlockFormat? BlockFormatOf(DecodedTextureFormat format) => format switch
    {
        DecodedTextureFormat.Dxt1 => BlockFormat.Bc1,
        DecodedTextureFormat.Dxt3 => BlockFormat.Bc2,
        DecodedTextureFormat.Dxt5 => BlockFormat.Bc3,
        _ => null,
    };

    /// <summary>
    /// The cache key of a request. <paramref name="dxt1WithAlpha"/> comes from the decode (or a resident texture's
    /// <see cref="TextureInfo"/>); before either is known, pass false and check the resident info.
    /// </summary>
    public static TextureKey KeyFor(SurfaceMaterial material, uint renderSurfaceId, PaletteData? palette, DxtUploadPolicy policy, bool dxt1WithAlpha)
    {
        ArgumentNullException.ThrowIfNull(material);
        return new TextureKey(
            renderSurfaceId,
            palette?.CacheKey ?? TextureKey.DefaultPalette,
            material.IsClipMap,
            policy == DxtUploadPolicy.PassthroughWhereSafe && material.RenderState.IsDeferred && dxt1WithAlpha);
    }

    /// <summary>Builds a Godot image with a full box-filtered mip chain from RGBA8, alpha or JPEG texels (DXT: <see cref="ToBlockImage(DecodedTexture, DecodedTexture)"/>).</summary>
    public static (Image Image, long Bytes) ToImage(DecodedTexture decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        return ToImage(decoded.Format, decoded.Data, decoded.Width, decoded.Height, decoded.SourceId);
    }

    /// <summary>The span form of <see cref="ToImage(DecodedTexture)"/>: Godot copies the texels, so they may be pooled.</summary>
    private static (Image Image, long Bytes) ToImage(DecodedTextureFormat format, ReadOnlySpan<byte> data, int width, int height, uint sourceId)
    {
        Image image;
        switch (format)
        {
            case DecodedTextureFormat.Rgba8:
                image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, data[..(width * height * 4)]);
                break;
            case DecodedTextureFormat.Alpha8:
            {
                // Alpha-only formats draw as white with alpha (docs/textures.md §1 convention; no Surface uses A8).
                var size = width * height * 4;
                var rgba = ArrayPool<byte>.Shared.Rent(size);
                try
                {
                    for (var i = 0; i < width * height; i++)
                    {
                        rgba[(i * 4) + 0] = 255;
                        rgba[(i * 4) + 1] = 255;
                        rgba[(i * 4) + 2] = 255;
                        rgba[(i * 4) + 3] = data[i];
                    }

                    image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba.AsSpan(0, size));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rgba);
                }

                break;
            }

            case DecodedTextureFormat.Jpeg:
                image = new Image();
                var error = image.LoadJpgFromBuffer(data);
                if (error != Error.Ok)
                {
                    image.Dispose();
                    throw new InvalidDataException($"RenderSurface 0x{sourceId:X8}: JPEG decode failed ({error}).");
                }

                break;
            default:
                throw new NotSupportedException($"TODO(render): no upload path for {format}.");
        }

        var baseBytes = (long)image.GetWidth() * image.GetHeight() * (image.GetFormat() == Image.Format.Rgb8 ? 3 : 4);
        var mipError = image.GenerateMipmaps();
        if (mipError != Error.Ok)
        {
            Log.Warn("render", "mipmap generation failed", ("texture", $"0x{sourceId:X8}"), ("error", mipError));
            return (image, baseBytes);
        }

        return (image, (baseBytes * 4) / 3);
    }
}

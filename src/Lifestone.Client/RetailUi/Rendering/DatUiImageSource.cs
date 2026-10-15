using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using DatReaderWriter.Enums;
using Lifestone.Client.Diagnostics;
using Lifestone.Data.Textures;

namespace Lifestone.Client.RetailUi.Rendering;

/// <summary>
/// Decodes JPEG files to RGBA8 pixels. Six retail UI images are JPEG files (the login screens' backgrounds among them,
/// docs/retail-ui.md §5); <see cref="UiJpegDecoder"/> is Lifestone's.
/// </summary>
public interface IUiJpegDecoder
{
    /// <summary>Decodes a complete JFIF file; false when it cannot.</summary>
    bool TryDecode(ReadOnlySpan<byte> jpeg, out int width, out int height, [NotNullWhen(true)] out byte[]? rgba);
}

/// <summary>
/// UI images straight from the dats: each RenderSurface is decoded once (<see cref="TextureLoader"/>, high-res first, as
/// retail's <c>Graphic</c> loads through <c>DBObj::Get</c>) and kept. Safe from any thread; decoding is synchronous, so
/// it never answers <see cref="UiImageLookup.Pending"/> (a host that decodes on workers wraps it).
/// </summary>
/// <remarks>
/// The census of the retail layouts' images (624 DIDs, UiRuntimeCensusTests): 325 A8R8G8B8, 292 R8G8B8, 6 JPEG, and
/// DID 0 (an image entry that clears the image); the 30 animation frames are R8G8B8. A format the software blitter
/// does not handle itself (16-bit, paletted, DXT) is blitted as the nearest of its formats with a loud TODO: retail
/// sends those through <c>SurfaceWindow::LegacyBlit</c> (11.4186 0x00441C50, 11.6096 0x00441DF0), which is not
/// re-expressed because no UI image uses it.
/// </remarks>
public sealed class DatUiImageSource : IUiImageSource
{
    private readonly TextureLoader _loader;
    private readonly ILogSink _log;
    private readonly IUiJpegDecoder _jpeg;
    private readonly ConcurrentDictionary<uint, UiImage?> _cache = new();
    private readonly Func<uint, UiImage?> _load;

    /// <summary>
    /// A source over <paramref name="loader"/>. JPEG images go through <paramref name="jpeg"/>, by default Lifestone's own
    /// decoder (<see cref="UiJpegDecoder"/>); a host should keep the default, so what it presents is what the tests check.
    /// </summary>
    public DatUiImageSource(TextureLoader loader, ILogSink? log = null, IUiJpegDecoder? jpeg = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
        _log = log ?? NullLogSink.Instance;
        _jpeg = jpeg ?? UiJpegDecoder.Instance;
        _load = Load;
    }

    /// <summary>
    /// The image <paramref name="did"/>, decoded on the first request and kept: <see cref="UiImageLookup.Ready"/> or
    /// <see cref="UiImageLookup.Missing"/>, never pending. Allocation-free once the DID was asked for (the compositor and
    /// the hit test ask on every repaint).
    /// </summary>
    public UiImageLookup TryGetImage(uint did, [NotNullWhen(true)] out UiImage? image)
    {
        if (!_cache.TryGetValue(did, out image))
        {
            image = _cache.GetOrAdd(did, _load);
        }

        return image is null ? UiImageLookup.Missing : UiImageLookup.Ready;
    }

    private UiImage? Load(uint did)
    {
        if (did == 0 || !_loader.RenderSurfaceExists(did, out _))
        {
            return null;
        }

        DecodedTexture texture;
        try
        {
            texture = _loader.DecodeRenderSurface(did, TextureDecodeMode.Rgba8);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
        {
            _log.Warn("retailui", $"image 0x{did:X8} does not decode: {ex.Message}");
            return null;
        }

        if (texture.Format == DecodedTextureFormat.Jpeg)
        {
            string? failure = null;
            var decoded = _jpeg is UiJpegDecoder
                ? UiJpegDecoder.Decode(texture.Data, out var width, out var height, out var rgba, out failure)
                : _jpeg.TryDecode(texture.Data, out width, out height, out rgba);
            if (decoded)
            {
                // Retail decodes JPEG files into an opaque surface.
                return new UiImage(did, width, height, UiPixelFormat.Xrgb, rgba!);
            }

            _log.Warn("retailui", $"image 0x{did:X8} is a JPEG file the decoder refuses ({failure ?? "no reason given"}); retail's default graphic stands in (placeholder)");
            return null;
        }

        return new UiImage(did, texture.Width, texture.Height, Classify(did, texture.SourceFormat), texture.Data);
    }

    private UiPixelFormat Classify(uint did, PixelFormat format)
    {
        switch (format)
        {
            case PixelFormat.PFID_A8R8G8B8:
                return UiPixelFormat.Argb;
            case PixelFormat.PFID_X8R8G8B8:
                return UiPixelFormat.Xrgb;
            case PixelFormat.PFID_R8G8B8:
                return UiPixelFormat.Rgb;
            case PixelFormat.PFID_A8:
            case PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA:
                return UiPixelFormat.A8;
            default:
                _log.Warn("retailui", $"TODO(retailui-runtime): image 0x{did:X8} is {format}; retail blits it through SurfaceWindow::LegacyBlit, which Lifestone has not re-expressed (blitted as A8R8G8B8, placeholder)");
                return UiPixelFormat.Argb;
        }
    }
}

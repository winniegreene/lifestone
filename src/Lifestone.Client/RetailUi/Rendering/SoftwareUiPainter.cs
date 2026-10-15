using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Rendering;

/// <summary>
/// The software painter: retail's drawing primitives on a <see cref="UiSurface"/>, byte for byte. It is both the
/// production path (the host uploads the surfaces) and the oracle the tests check (m2-plan.md §1.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Images</b> follow <c>Graphic::Draw</c> (11.4186 0x00693B20, 11.6096 0x00694A40): an image at least as large as
/// the element, with no tiling offset, is blitted from its top-left (cropped, never stretched); otherwise it tiles from
/// the tiling offset across the dirty part of the element. Each piece goes through <c>Graphic::PutImage</c> (11.4186
/// 0x00693A30, 11.6096 0x00694950) to <see cref="UiBlitter"/>.
/// </para>
/// <para>
/// An image that does not resolve draws retail's default graphic (<see cref="UiDefaultGraphic"/>), logged once per
/// DID; one still being decoded draws nothing and sets <see cref="HadPendingImage"/>, and the compositor
/// (<see cref="UiCompositor"/>) then keeps the painted area dirty, hands none of it over, and paints it again later.
/// </para>
/// </remarks>
public sealed class SoftwareUiPainter : IUiPainter
{
    private readonly IUiImageSource _images;
    private readonly ILogSink _log;
    private readonly HashSet<uint> _reportedMissing = [];

    /// <summary>A painter drawing images from <paramref name="images"/>.</summary>
    public SoftwareUiPainter(IUiImageSource images, ILogSink? log = null)
    {
        ArgumentNullException.ThrowIfNull(images);
        _images = images;
        _log = log ?? NullLogSink.Instance;
    }

    /// <summary>The surface being painted (<see cref="Begin"/>).</summary>
    public UiSurface? Target { get; private set; }

    /// <summary>True when an image was still being decoded during this paint.</summary>
    public bool HadPendingImage { get; private set; }

    /// <summary>DIDs drawn as the default graphic so far (each logged once).</summary>
    public IReadOnlyCollection<uint> MissingImages => _reportedMissing;

    /// <summary>Starts painting onto <paramref name="target"/>.</summary>
    public void Begin(UiSurface target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Target = target;
        HadPendingImage = false;
    }

    /// <summary>
    /// Resolves the image <paramref name="did"/> as <see cref="DrawImage(UiImageRef, float, UiBox, UiBox, ValueTuple{int, int})"/> does:
    /// the image, retail's default graphic for one that does not resolve, or null while it is being decoded.
    /// </summary>
    public UiImage? Resolve(uint did)
    {
        switch (_images.TryGetImage(did, out var image))
        {
            case UiImageLookup.Ready:
                return image;
            case UiImageLookup.Pending:
                HadPendingImage = true;
                return null;
            default:
                if (_reportedMissing.Add(did))
                {
                    _log.Warn("retailui", $"image 0x{did:X8} does not resolve; retail's default graphic stands in (placeholder)");
                }

                return UiDefaultGraphic.Image;
        }
    }

    /// <inheritdoc/>
    public void DrawImage(UiImageRef image, float alphaBlendMod, UiBox bounds, UiBox dirty, (int X, int Y) tiling)
    {
        if (!image.HasImage)
        {
            return;
        }

        var resolved = Resolve(image.Did);
        if (resolved is not null)
        {
            DrawImage(resolved, image.Mode, alphaBlendMod, bounds, dirty, tiling);
        }
    }

    /// <inheritdoc/>
    public void DrawImage(UiImage image, UiBlitMode mode, float alphaBlendMod, UiBox bounds, UiBox dirty, (int X, int Y) tiling)
    {
        ArgumentNullException.ThrowIfNull(image);
        var target = RequireTarget();
        var boundsWidth = bounds.X1 - bounds.X0 + 1;
        var boundsHeight = bounds.Y1 - bounds.Y0 + 1;
        if (boundsWidth <= 0 || boundsHeight <= 0 || dirty.X1 - dirty.X0 + 1 <= 0 || dirty.Y1 - dirty.Y0 + 1 <= 0)
        {
            return;
        }

        var imageWidth = image.Width;
        var imageHeight = image.Height;
        if (imageWidth == 0 || imageHeight == 0)
        {
            return;
        }

        if (tiling.X == 0 && tiling.Y == 0 && boundsWidth <= imageWidth && boundsHeight <= imageHeight)
        {
            // The image covers the element: the dirty part, from the element's top-left in the image.
            PutImage(target, image, mode, alphaBlendMod, dirty.X0 - bounds.X0, dirty.Y0 - bounds.Y0, dirty);
            return;
        }

        if (!UiBoxes.TryIntersect(dirty, bounds, out var area))
        {
            return;
        }

        var startColumn = PositiveModulo(area.X0 + tiling.X - bounds.X0, imageWidth);
        var row = PositiveModulo(area.Y0 + tiling.Y - bounds.Y0, imageHeight);
        for (var y = area.Y0; y <= area.Y1;)
        {
            var rowHeight = Math.Min(imageHeight - row, area.Y1 - y + 1);
            var column = startColumn;
            for (var x = area.X0; x <= area.X1;)
            {
                var columnWidth = Math.Min(imageWidth - column, area.X1 - x + 1);
                PutImage(target, image, mode, alphaBlendMod, column, row, new UiBox(x, y, x + columnWidth - 1, y + rowHeight - 1));
                x += columnWidth;
                column = 0;
            }

            y += rowHeight;
            row = 0;
        }
    }

    /// <inheritdoc/>
    public int DrawCharacter(int x, int y, IUiFont font, char c, uint argb, UiTextFlags flags, uint backgroundArgb, UiBox window) =>
        TextRasterizer.DrawCharacter(RequireTarget(), window, x, y, font, c, argb, flags, backgroundArgb);

    /// <inheritdoc/>
    public void NotColorBits(UiBox box)
    {
        var target = RequireTarget();
        if (!UiBoxes.TryIntersect(box, new UiBox(0, 0, target.Width - 1, target.Height - 1), out var area))
        {
            return;
        }

        var pixels = target.Pixels;
        for (var y = area.Y0; y <= area.Y1; y++)
        {
            var i = ((y * target.Width) + area.X0) * 4;
            for (var x = area.X0; x <= area.X1; x++, i += 4)
            {
                pixels[i] = (byte)~pixels[i];
                pixels[i + 1] = (byte)~pixels[i + 1];
                pixels[i + 2] = (byte)~pixels[i + 2];
            }
        }
    }

    /// <inheritdoc/>
    public void Fill(UiBox box, uint argb)
    {
        var target = RequireTarget();
        if (!UiBoxes.TryIntersect(box, new UiBox(0, 0, target.Width - 1, target.Height - 1), out var area))
        {
            return;
        }

        var r = (byte)(argb >> 16);
        var g = (byte)(argb >> 8);
        var b = (byte)argb;
        var a = (byte)(argb >> 24);
        var pixels = target.Pixels;
        for (var y = area.Y0; y <= area.Y1; y++)
        {
            var i = ((y * target.Width) + area.X0) * 4;
            for (var x = area.X0; x <= area.X1; x++, i += 4)
            {
                pixels[i] = r;
                pixels[i + 1] = g;
                pixels[i + 2] = b;
                pixels[i + 3] = a;
            }
        }
    }

    /// <summary>
    /// <c>Graphic::PutImage</c>: blits the image from (<paramref name="sx"/>, <paramref name="sy"/>) into
    /// <paramref name="dst"/>, both cut to their surfaces; the blit covers the smaller of the two.
    /// </summary>
    private static void PutImage(UiSurface target, UiImage image, UiBlitMode mode, float alphaBlendMod, int sx, int sy, UiBox dst)
    {
        var dx0 = Math.Max(dst.X0, 0);
        var dy0 = Math.Max(dst.Y0, 0);
        var dx1 = Math.Min(dst.X1 + 1, target.Width);
        var dy1 = Math.Min(dst.Y1 + 1, target.Height);
        var sx0 = Math.Max(sx, 0);
        var sy0 = Math.Max(sy, 0);
        var sx1 = Math.Min(sx + (dst.X1 - dst.X0 + 1), image.Width);
        var sy1 = Math.Min(sy + (dst.Y1 - dst.Y0 + 1), image.Height);
        var width = Math.Min(sx1 - sx0, dx1 - dx0);
        var height = Math.Min(sy1 - sy0, dy1 - dy0);
        UiBlitter.Blit(image, sx0, sy0, target, dx0, dy0, width, height, mode, alphaBlendMod);
    }

    private static int PositiveModulo(int value, int modulus)
    {
        if (value == 0)
        {
            return 0;
        }

        var r = value % modulus;
        return r < 0 ? r + modulus : r;
    }

    private UiSurface RequireTarget() =>
        Target ?? throw new InvalidOperationException("SoftwareUiPainter.Begin was not called: there is no surface to paint.");
}

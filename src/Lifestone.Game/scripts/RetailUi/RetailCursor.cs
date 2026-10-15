using Godot;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Game.Core;

namespace Lifestone.Game.RetailUi;

/// <summary>A retail cursor ready for the engine: its pixels (RGBA8, straight alpha), size and hot spot, scaled for the UI scale.</summary>
/// <param name="Did">The cursor image's RenderSurface DID.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="HotspotX">The hot spot's column, inside the image.</param>
/// <param name="HotspotY">The hot spot's row, inside the image.</param>
/// <param name="Factor">How many times the retail image was enlarged (the UI scale, less when the engine's cursor limit stops it).</param>
/// <param name="Rgba">The pixels, <c>Width * Height * 4</c> bytes.</param>
public sealed record PreparedCursor(uint Did, int Width, int Height, int HotspotX, int HotspotY, int Factor, byte[] Rgba);

/// <summary>
/// The mouse cursor the retail UI wants (<see cref="UiCursorController"/>, <c>UIElementManager::CheckCursor</c>), shown through
/// the engine's custom cursor: the retail image enlarged by the UI scale with nearest neighbour (so the pointer grows with the
/// UI and keeps its pixels), hot spot scaled with it, and never larger than the engine's 256 pixels.
/// </summary>
/// <remarks>
/// The cursor is the window's, so it also shows over the world: retail's pointer was always its own. <see cref="Update"/> reads
/// the controller's <see cref="UiCursorController.Version"/> once a frame and acts only when the cursor or the scale changed.
/// An image still decoding is asked again next frame. Main thread only.
/// </remarks>
public sealed class RetailCursor
{
    /// <summary>The engine's largest custom cursor, in pixels per side.</summary>
    public const int MaximumSize = 256;

    private readonly IUiImageSource _images;
    private readonly Dictionary<(uint Did, int Factor, int HotX, int HotY), ImageTexture> _textures = [];
    private int _version = -1;
    private int _scale;
    private bool _reportedMissing;

    /// <summary>A cursor showing images from <paramref name="images"/>.</summary>
    public RetailCursor(IUiImageSource images)
    {
        ArgumentNullException.ThrowIfNull(images);
        _images = images;
    }

    /// <summary>The cursor shown now, or null before the first.</summary>
    public PreparedCursor? Current { get; private set; }

    /// <summary>Cursors handed to the engine so far (checks and the perf line).</summary>
    public int Applied { get; private set; }

    /// <summary>
    /// The retail image enlarged <paramref name="scale"/> times with nearest neighbour, or less when the engine's 256-pixel limit
    /// would be passed; null when the image alone is larger than the limit. The hot spot is scaled with it and kept inside the image.
    /// </summary>
    public static PreparedCursor? Prepare(UiImage image, int hotspotX, int hotspotY, int scale)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfLessThan(scale, 1);
        var largest = Math.Max(image.Width, image.Height);
        if (largest == 0 || largest > MaximumSize)
        {
            return null;
        }

        var factor = Math.Max(1, Math.Min(scale, MaximumSize / largest));
        var width = image.Width * factor;
        var height = image.Height * factor;
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var sourceRow = (y / factor) * image.Width * 4;
            var targetRow = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var source = sourceRow + ((x / factor) * 4);
                var target = targetRow + (x * 4);
                rgba[target] = image.Pixels[source];
                rgba[target + 1] = image.Pixels[source + 1];
                rgba[target + 2] = image.Pixels[source + 2];
                rgba[target + 3] = image.Pixels[source + 3];
            }
        }

        var hotX = Math.Clamp(hotspotX * factor, 0, width - 1);
        var hotY = Math.Clamp(hotspotY * factor, 0, height - 1);
        return new PreparedCursor(image.Did, width, height, hotX, hotY, factor, rgba);
    }

    /// <summary>Once a frame: shows the controller's cursor at <paramref name="scale"/> when it, or the scale, changed.</summary>
    public void Update(UiCursorController controller, int scale)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (controller.Version == _version && scale == _scale)
        {
            return;
        }

        var did = controller.CursorDid;
        if (did == 0)
        {
            _version = controller.Version;
            _scale = scale;
            return;
        }

        switch (_images.TryGetImage(did, out var image))
        {
            case UiImageLookup.Pending:
                return;
            case UiImageLookup.Missing:
                if (!_reportedMissing)
                {
                    _reportedMissing = true;
                    Log.Error("retailui", "the cursor image does not load; the system pointer stays (placeholder)", ("did", $"0x{did:X8}"));
                }

                _version = controller.Version;
                _scale = scale;
                return;
        }

        _version = controller.Version;
        _scale = scale;
        var prepared = Prepare(image!, controller.Hotspot.X, controller.Hotspot.Y, scale);
        if (prepared is null)
        {
            Log.Error("retailui", "the cursor image is larger than the engine allows; the system pointer stays (placeholder)", ("did", $"0x{did:X8}"), ("limit", MaximumSize));
            return;
        }

        Show(prepared);
    }

    /// <summary>Hands <paramref name="cursor"/> to the engine as the arrow pointer.</summary>
    public void Show(PreparedCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        var key = (cursor.Did, cursor.Factor, cursor.HotspotX, cursor.HotspotY);
        if (!_textures.TryGetValue(key, out var texture))
        {
            using var image = Image.CreateFromData(cursor.Width, cursor.Height, false, Image.Format.Rgba8, cursor.Rgba);
            texture = ImageTexture.CreateFromImage(image);
            _textures.Add(key, texture);
        }

        Godot.Input.SetCustomMouseCursor(texture, Godot.Input.CursorShape.Arrow, new Vector2(cursor.HotspotX, cursor.HotspotY));
        Current = cursor;
        Applied++;
    }

    /// <summary>Gives the system pointer back (the layer is going away).</summary>
    public void Reset()
    {
        if (Current is not null)
        {
            Godot.Input.SetCustomMouseCursor(null, Godot.Input.CursorShape.Arrow);
            Current = null;
        }
    }
}

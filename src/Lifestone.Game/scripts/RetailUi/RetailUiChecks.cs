using System.Diagnostics;
using System.Globalization;
using Godot;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Ui;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using DataUiFonts = Lifestone.Data.Ui.UiFonts;

namespace Lifestone.Game.RetailUi;

/// <summary>
/// <c>--retail-ui-checks</c>: verifies, in the running engine, what the retail UI's Godot layer adds to the tested runtime, then
/// quits with 0 when every check holds and 1 otherwise (M2 G2 step U2). Run it off-screen:
/// <c>tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 180 -GameArgs:'--retail-ui-checks'</c>. Each line is
/// <c>[uicheck] PASS|FAIL|SKIPPED &lt;name&gt;</c>; the run ends with <c>all retail-ui checks hold</c>. The checks that need the
/// user's dats are skipped without them; the others need none.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><b>Tiles, byte for byte, at scales 1, 2 and 3</b> (<see cref="RetailUiTiles"/>): a surface of pseudo-random opaque pixels
/// cut into 128-pixel tiles (full and edge tiles) is shown in a <see cref="SubViewport"/> and read back; every window pixel is the
/// surface pixel it should be, with the surface's screen box and scale applied. This is the composite of the tile layer, nearest
/// filtering included (a filtered edge would differ).</item>
/// <item><b>Nearest, no mipmaps, no seams</b>: a one-pixel checkerboard and an all-white surface read back exactly (no blur, no
/// dark line on a tile boundary at any scale); the tile textures hold no mipmaps.</item>
/// <item><b>Straight alpha</b>: a half-transparent colour over black is half the colour (not the colour added).</item>
/// <item><b>Only dirty tiles</b>: uploading one tile changes that tile's texture in place and touches no other.</item>
/// <item><b>The viewport hole</b> (<see cref="RetailViewportHost"/>): a transparent hole in an opaque surface shows the registered
/// source's picture exactly in the hole, the source is told the hole in window pixels, and a hidden hole shows nothing.</item>
/// <item><b>The cursor</b> (<see cref="RetailCursor"/>): the retail image enlarged by the scale with nearest neighbour, the hot spot
/// scaled, never above 256 pixels.</item>
/// <item><b>Every gmDefaultMap key is bound</b>: each keyboard control of the engine and game key maps (and each meta key) has a
/// Godot physical key that maps to its DIK scan code (<see cref="GodotKeyCodes"/>).</item>
/// <item><b>The fonts</b> (the production <see cref="UiFontsSource"/>): signed glyph offsets, glyph 0 for a missing character
/// inside a font's range, '?' outside it, and the Gulim fallback font for a control character.</item>
/// <item><b>The real screens</b> through the whole layer (worker-decoded images, the compositor, the tile layer): the data check
/// at 800×600 (scale 1) and 1600×1200 (scale 2) reads back as the compositor's surface, and the surface equals a synchronous
/// composite of the same screen (the JPEG images are the runtime's decoder's).</item>
/// <item><b>Input</b>: a Godot key reaches a registered input map as its DIK action; a key nothing binds is not taken; a press
/// outside the screens reaches the world in logical pixels at scale 2; a text field's keyboard capture is reported once each way;
/// the focus notifications call <c>SetClientActive</c>; and an idle layer frame allocates nothing.</item>
/// <item><b>Character creation's 3D character</b> (D60): the face view, zoomed in, holds its rest pose; the clothes view, zoomed out, and the
/// summary view loop retail's animation, so their poses change between two times.</item>
/// </list>
/// Frames for review (the real screens as the window shows them) are written under <c>cache/retail-ui-checks/</c> (gitignored; derived
/// from the dats and never committed).
/// </remarks>
public partial class RetailUiChecks : Node
{
    private const string OutputFolder = "cache/retail-ui-checks";

    private readonly List<(string Name, Outcome Outcome, string Detail)> _results = [];

    private enum Outcome
    {
        Pass,
        Fail,
        Skipped,
    }

    /// <inheritdoc/>
    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        var exitCode = 1;
        try
        {
            if (DisplayServer.GetName() == "headless")
            {
                Record("render checks", Outcome.Skipped, "headless: no renderer; run off-screen, not --headless");
            }
            else
            {
                await CheckTilesAsync();
                await CheckViewportHoleAsync();
                CheckCursorPreparation();
                var services = GameServices.Instance;
                if (services.Dats is null)
                {
                    Record("dat-based checks", Outcome.Skipped, "the retail dats are not available");
                }
                else
                {
                    CheckKeyTable(services);
                    CheckFonts(services);
                    await CheckRealScreensAsync(services);
                    // Tests and checks stay at retail's native 800x600 (owner rule 2026-10-14); --all-sizes adds the larger sizes (retail-ui.md section 21.9).
                    await CheckGameShellAsync(services, new Vector2I(800, 600), 1);
                    if (PlayOptions.Parse(OS.GetCmdlineUserArgs()).AllSizes)
                    {
                        await CheckGameShellAsync(services, new Vector2I(1920, 1080), 1);
                        await CheckGameShellAsync(services, new Vector2I(2560, 1440), 2); // g7: the same placement at UI scale 2
                    }

                    await CheckInputAsync(services);
                    await CheckLoginFlowAsync(services);
                    await CheckCharGenAnimationAsync(services);
                }
            }

            exitCode = _results.Any(r => r.Outcome == Outcome.Fail) ? 1 : 0;
        }
        catch (Exception ex)
        {
            Log.Exception("uicheck", "retail-ui checks crashed", ex);
        }

        Log.Info(
            "uicheck",
            exitCode == 0 ? "all retail-ui checks hold" : "RETAIL-UI CHECKS FAILED",
            ("pass", _results.Count(r => r.Outcome == Outcome.Pass)),
            ("fail", _results.Count(r => r.Outcome == Outcome.Fail)),
            ("skipped", _results.Count(r => r.Outcome == Outcome.Skipped)));
        Log.FlushToGodot();
        GetTree().Quit(exitCode);
    }

    private void Record(string name, Outcome outcome, string detail)
    {
        _results.Add((name, outcome, detail));
        var verdict = outcome.ToString().ToUpperInvariant();
        if (outcome == Outcome.Fail)
        {
            Log.Error("uicheck", $"{verdict} {name}", ("detail", detail));
        }
        else
        {
            Log.Info("uicheck", $"{verdict} {name}", ("detail", detail));
        }
    }

    private void Check(string name, bool ok, string detail) => Record(name, ok ? Outcome.Pass : Outcome.Fail, detail);

    private async Task FramesAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // A stage: a black SubViewport the pieces are shown in, read back as RGBA8 bytes

    private sealed class Stage : IDisposable
    {
        public required SubViewport View { get; init; }

        public required Node2D Pictures { get; init; }

        public required Node2D Surfaces { get; init; }

        public void Dispose() => View.QueueFree();
    }

    private Stage NewStage(Vector2I size)
    {
        var view = new SubViewport
        {
            Size = size,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Disable3D = true,
            TransparentBg = false,
        };
        var black = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        black.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var pictures = new Node2D { Name = "Pictures" };
        var surfaces = new Node2D { Name = "Surfaces" };
        view.AddChild(black);
        view.AddChild(pictures);
        view.AddChild(surfaces);
        AddChild(view);
        return new Stage { View = view, Pictures = pictures, Surfaces = surfaces };
    }

    private static byte[] Capture(SubViewport view, out Vector2I size)
    {
        using var image = view.GetTexture().GetImage();
        image.Convert(Image.Format.Rgba8);
        size = new Vector2I(image.GetWidth(), image.GetHeight());
        return image.GetData();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Reference: what the window should show for surfaces placed at a scale

    private sealed record Placed(UiBox ScreenBox, int Width, int Height, byte[] Rgba);

    /// <summary>The window as the tile layer should show it: <paramref name="background"/> (or black), each surface nearest-enlarged by <paramref name="scale"/> and mixed over it.</summary>
    private static byte[] Expected(Vector2I window, int scale, IEnumerable<Placed> surfaces, byte[]? background = null)
    {
        var pixels = new byte[window.X * window.Y * 4];
        if (background is not null)
        {
            background.CopyTo(pixels, 0);
        }
        else
        {
            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }
        }

        foreach (var surface in surfaces)
        {
            var x0 = surface.ScreenBox.X0 * scale;
            var y0 = surface.ScreenBox.Y0 * scale;
            for (var wy = Math.Max(0, y0); wy < Math.Min(window.Y, y0 + (surface.Height * scale)); wy++)
            {
                var sy = (wy - y0) / scale;
                for (var wx = Math.Max(0, x0); wx < Math.Min(window.X, x0 + (surface.Width * scale)); wx++)
                {
                    var s = ((sy * surface.Width) + ((wx - x0) / scale)) * 4;
                    var d = ((wy * window.X) + wx) * 4;
                    var a = surface.Rgba[s + 3];
                    for (var c = 0; c < 3; c++)
                    {
                        pixels[d + c] = a == 255 ? surface.Rgba[s + c] : (byte)(((surface.Rgba[s + c] * a) + (pixels[d + c] * (255 - a)) + 127) / 255);
                    }

                    pixels[d + 3] = 255;
                }
            }
        }

        return pixels;
    }

    /// <summary>Compares two RGBA8 images: pixels off by more than <paramref name="tolerance"/> in a colour channel, and the first.</summary>
    private static (int Off, int Within, string First) Compare(byte[] got, byte[] expected, int tolerance)
    {
        var off = 0;
        var within = 0;
        var first = "none";
        for (var i = 0; i + 3 < expected.Length && i + 3 < got.Length; i += 4)
        {
            var worst = Math.Max(Math.Abs(got[i] - expected[i]), Math.Max(Math.Abs(got[i + 1] - expected[i + 1]), Math.Abs(got[i + 2] - expected[i + 2])));
            if (worst > tolerance)
            {
                if (off++ == 0)
                {
                    first = $"pixel {i / 4} got ({got[i]},{got[i + 1]},{got[i + 2]}) expected ({expected[i]},{expected[i + 1]},{expected[i + 2]})";
                }
            }
            else if (worst > 0)
            {
                within++;
            }
        }

        return (off, within, first);
    }

    private static byte[] Noise(int width, int height, uint seed)
    {
        var rgba = new byte[width * height * 4];
        var state = seed;
        for (var i = 0; i < rgba.Length; i += 4)
        {
            state = (state * 1664525u) + 1013904223u;
            rgba[i] = (byte)(state >> 24);
            rgba[i + 1] = (byte)(state >> 16);
            rgba[i + 2] = (byte)(state >> 8);
            rgba[i + 3] = 255;
        }

        return rgba;
    }

    private static byte[] Solid(int width, int height, byte r, byte g, byte b, byte a)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = r;
            rgba[i + 1] = g;
            rgba[i + 2] = b;
            rgba[i + 3] = a;
        }

        return rgba;
    }

    /// <summary>Hands <paramref name="sink"/> a whole surface as the compositor would: the frame, then every tile.</summary>
    private static void Feed(IUiTileSink sink, int rootId, int order, UiBox screenBox, byte[] surface, int width, int height, bool visible = true, float alpha = 1f)
    {
        var grid = new UiTileGrid(width, height);
        sink.UpdateRoot(new UiRootFrame(rootId, order, 0x10000001u + (uint)rootId, screenBox, grid.TilesX, grid.TilesY, visible, alpha));
        for (var ty = 0; ty < grid.TilesY; ty++)
        {
            for (var tx = 0; tx < grid.TilesX; tx++)
            {
                FeedTile(sink, rootId, grid, tx, ty, surface, width);
            }
        }
    }

    private static void FeedTile(IUiTileSink sink, int rootId, UiTileGrid grid, int tx, int ty, byte[] surface, int width)
    {
        var box = grid.TileBox(tx, ty);
        var tile = new byte[box.Width * box.Height * 4];
        for (var row = 0; row < box.Height; row++)
        {
            Array.Copy(surface, ((((box.Y0 + row) * width) + box.X0) * 4), tile, row * box.Width * 4, box.Width * 4);
        }

        sink.UploadTile(new UiTileUpdate(rootId, tx, ty, box), tile);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 1-4. The tile layer

    private async Task CheckTilesAsync()
    {
        // 300 x 200 pixels: 3 x 2 tiles, the right column 44 wide and the bottom row 72 high.
        const int width = 300;
        const int height = 200;
        var noise = Noise(width, height, 7);
        var checker = new byte[width * height * 4];
        var white = Solid(width, height, 255, 255, 255, 255);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = ((y * width) + x) * 4;
                var on = ((x + y) & 1) == 0;
                checker[i] = checker[i + 1] = checker[i + 2] = (byte)(on ? 255 : 0);
                checker[i + 3] = 255;
            }
        }

        foreach (var scale in new[] { 1, 2, 3 })
        {
            var box = UiBox.FromPositionAndSize(10, 7, width, height);
            var window = new Vector2I((10 + width + 6) * scale, (7 + height + 5) * scale);
            foreach (var (what, surface) in new[] { ("random pixels", noise), ("one-pixel checkerboard", checker), ("all white", white) })
            {
                using var stage = NewStage(window);
                var tiles = new RetailUiTiles(stage.Surfaces);
                tiles.SetScale(scale);
                Feed(tiles, 1, 0, box, surface, width, height);
                await FramesAsync(3);
                var got = Capture(stage.View, out var size);
                var (off, within, first) = Compare(got, Expected(size, scale, [new Placed(box, width, height, surface)]), 0);
                var name = what switch
                {
                    "random pixels" => $"tiles at scale {scale} read back byte for byte ({what}; full and edge tiles)",
                    "one-pixel checkerboard" => $"tiles at scale {scale}: nearest filtering keeps every {what} pixel ({scale}x{scale} blocks, none blurred)",
                    _ => $"tiles at scale {scale}: no seam between tiles ({what} surface shows no dark line at the 128-pixel edges)",
                };
                Check(name, off == 0 && within == 0 && size == window, $"window {size.X}x{size.Y}, {off} pixels differ ({first}), texture {FilterOf(tiles)}");
            }
        }

        // Mipmaps: none.
        {
            using var stage = NewStage(new Vector2I(400, 260));
            var tiles = new RetailUiTiles(stage.Surfaces);
            Feed(tiles, 1, 0, UiBox.FromPositionAndSize(0, 0, width, height), noise, width, height);
            var mips = 0;
            var checkedTiles = 0;
            for (var ty = 0; ty < 2; ty++)
            {
                for (var tx = 0; tx < 3; tx++)
                {
                    using var image = tiles.TileTexture(1, tx, ty)!.GetImage();
                    checkedTiles++;
                    mips += image.HasMipmaps() ? 1 : 0;
                }
            }

            Check("tile textures hold no mipmaps", mips == 0 && checkedTiles == 6, $"{checkedTiles} tile textures, {mips} with mipmaps");
        }

        // Straight alpha: (200, 100, 50) at alpha 128 over black is half the colour, not the colour added.
        {
            using var stage = NewStage(new Vector2I(64, 64));
            var tiles = new RetailUiTiles(stage.Surfaces);
            Feed(tiles, 1, 0, UiBox.FromPositionAndSize(0, 0, 64, 64), Solid(64, 64, 200, 100, 50, 128), 64, 64);
            await FramesAsync(3);
            var got = Capture(stage.View, out var size);
            var (off, _, first) = Compare(got, Expected(size, 1, [new Placed(UiBox.FromPositionAndSize(0, 0, 64, 64), 64, 64, Solid(64, 64, 200, 100, 50, 128))]), 1);
            var centre = ((32 * 64) + 32) * 4;
            Check("tiles keep straight alpha (a half-transparent colour over black is half the colour)", off == 0, $"centre ({got[centre]},{got[centre + 1]},{got[centre + 2]}), expected about (100,50,25); {off} pixels off ({first})");
        }

        // Several surfaces: order, a hidden one and a faded one.
        {
            using var stage = NewStage(new Vector2I(200, 100));
            var tiles = new RetailUiTiles(stage.Surfaces);
            Feed(tiles, 1, 0, UiBox.FromPositionAndSize(0, 0, 100, 100), Solid(100, 100, 255, 0, 0, 255), 100, 100);
            Feed(tiles, 2, 1, UiBox.FromPositionAndSize(50, 0, 100, 100), Solid(100, 100, 0, 0, 255, 255), 100, 100);
            Feed(tiles, 3, 2, UiBox.FromPositionAndSize(150, 0, 50, 50), Solid(50, 50, 0, 255, 0, 255), 50, 50, visible: false);
            await FramesAsync(3);
            var got = Capture(stage.View, out _);
            (int R, int G, int B) At(int x, int y) => (got[((y * 200) + x) * 4], got[(((y * 200) + x) * 4) + 1], got[(((y * 200) + x) * 4) + 2]);
            var front = At(75, 50);
            var back = At(25, 50);
            var hidden = At(175, 25);
            Check(
                "surfaces draw back to front and a hidden surface draws nothing",
                front == (0, 0, 255) && back == (255, 0, 0) && hidden == (0, 0, 0),
                $"overlap {front} wants the front blue, back {back} red, hidden {hidden} black");
        }

        // Only the dirty tile is touched, and its texture is reused.
        {
            using var stage = NewStage(new Vector2I(320, 210));
            var tiles = new RetailUiTiles(stage.Surfaces);
            var grid = new UiTileGrid(width, height);
            Feed(tiles, 1, 0, UiBox.FromPositionAndSize(0, 0, width, height), noise, width, height);
            var before = Enumerable.Range(0, 6).Select(n => tiles.TileTexture(1, n % 3, n / 3)).ToArray();
            var uploadedBefore = tiles.TilesUploaded;
            var changed = (byte[])noise.Clone();
            for (var y = 128; y < 200; y++)
            {
                for (var x = 128; x < 256; x++)
                {
                    changed[((y * width) + x) * 4] ^= 0xFF;
                }
            }

            FeedTile(tiles, 1, grid, 1, 1, changed, width);
            await FramesAsync(3);
            var after = Enumerable.Range(0, 6).Select(n => tiles.TileTexture(1, n % 3, n / 3)).ToArray();
            var same = before.Zip(after, ReferenceEquals).Count(s => s);
            var got = Capture(stage.View, out var size);
            var (off, _, first) = Compare(got, Expected(size, 1, [new Placed(UiBox.FromPositionAndSize(0, 0, width, height), width, height, changed)]), 0);
            Check(
                "one dirty tile uploads alone, into its own texture, and the picture follows",
                tiles.TilesUploaded - uploadedBefore == 1 && same == 6 && off == 0,
                $"{tiles.TilesUploaded - uploadedBefore} tile(s) uploaded, {same} of 6 textures reused, {off} pixels differ ({first})");
        }
    }

    private static string FilterOf(RetailUiTiles tiles) =>
        tiles.NodeOf(1) is { } node ? node.TextureFilter.ToString() : "no node";

    // ---------------------------------------------------------------------------------------------------------------
    // 5. The viewport hole

    private sealed class SolidSource(Color colour) : IUiViewportSource
    {
        public Texture2D? Texture { get; } = ImageTexture.CreateFromImage(Image.CreateFromData(8, 8, false, Image.Format.Rgba8, ColourBytes(colour)));

        public Rect2I LastRect { get; private set; }

        public bool LastVisible { get; private set; }

        public int Calls { get; private set; }

        public void Bound(Rect2I windowRect, bool visible)
        {
            LastRect = windowRect;
            LastVisible = visible;
            Calls++;
        }

        private static byte[] ColourBytes(Color c) => Solid(8, 8, (byte)c.R8, (byte)c.G8, (byte)c.B8, 255);
    }

    private async Task CheckViewportHoleAsync()
    {
        const int width = 120;
        const int height = 80;
        const uint viewportId = 0x10000436;
        var surface = Solid(width, height, 20, 40, 160, 255);
        const int hx0 = 30;
        const int hy0 = 20;
        const int hw = 50;
        const int hh = 30;
        for (var y = hy0; y < hy0 + hh; y++)
        {
            for (var x = hx0; x < hx0 + hw; x++)
            {
                var i = ((y * width) + x) * 4;
                surface[i] = surface[i + 1] = surface[i + 2] = surface[i + 3] = 0; // the hole: transparent
            }
        }

        foreach (var scale in new[] { 1, 2 })
        {
            using var stage = NewStage(new Vector2I(160 * scale, 100 * scale));
            var tiles = new RetailUiTiles(stage.Surfaces);
            tiles.SetScale(scale);
            var host = new RetailViewportHost(stage.Pictures);
            host.SetScale(scale);
            var source = new SolidSource(new Color(1f, 0f, 0f));
            host.RegisterSource(viewportId, source);
            var origin = (X: 15, Y: 10);
            Feed(tiles, 1, 0, UiBox.FromPositionAndSize(origin.X, origin.Y, width, height), surface, width, height);
            host.Bind(viewportId, UiBox.FromPositionAndSize(origin.X + hx0, origin.Y + hy0, hw, hh), visible: true);
            host.Refresh();
            await FramesAsync(3);
            var got = Capture(stage.View, out var size);
            var window = new Vector2I(160 * scale, 100 * scale);
            var background = Solid(window.X, window.Y, 0, 0, 0, 255);
            for (var y = (origin.Y + hy0) * scale; y < (origin.Y + hy0 + hh) * scale; y++)
            {
                for (var x = (origin.X + hx0) * scale; x < (origin.X + hx0 + hw) * scale; x++)
                {
                    background[((y * window.X) + x) * 4] = 255; // the picture: solid red in the hole
                }
            }

            var (off, within, first) = Compare(got, Expected(size, scale, [new Placed(UiBox.FromPositionAndSize(origin.X, origin.Y, width, height), width, height, surface)], background), 0);
            var expectedRect = new Rect2I((origin.X + hx0) * scale, (origin.Y + hy0) * scale, hw * scale, hh * scale);
            Check(
                $"the viewport hole at scale {scale} shows the source's picture exactly in the hole, the UI around it",
                off == 0 && within == 0 && source.LastRect == expectedRect && source.LastVisible && host.TryGetHole(viewportId, out var rect, out var shown) && rect == expectedRect && shown,
                $"{off} pixels differ ({first}); the source was told {source.LastRect} (wanted {expectedRect}), visible {source.LastVisible}");

            host.Bind(viewportId, UiBox.FromPositionAndSize(origin.X + hx0, origin.Y + hy0, hw, hh), visible: false);
            host.Refresh();
            await FramesAsync(3);
            var hiddenPixels = Capture(stage.View, out _);
            var holeCentre = (((origin.Y + hy0 + (hh / 2)) * scale * window.X) + ((origin.X + hx0 + (hw / 2)) * scale)) * 4;
            Check(
                $"a hidden viewport at scale {scale} shows nothing in its hole and tells its source",
                hiddenPixels[holeCentre] == 0 && hiddenPixels[holeCentre + 1] == 0 && hiddenPixels[holeCentre + 2] == 0 && !source.LastVisible,
                $"hole centre ({hiddenPixels[holeCentre]},{hiddenPixels[holeCentre + 1]},{hiddenPixels[holeCentre + 2]}) wants black, source visible {source.LastVisible}");
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 6. The cursor

    private void CheckCursorPreparation()
    {
        // A 32 x 32 image whose every pixel is unique in its colour and alpha.
        var pixels = new byte[32 * 32 * 4];
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                var i = ((y * 32) + x) * 4;
                pixels[i] = (byte)(x * 8);
                pixels[i + 1] = (byte)(y * 8);
                pixels[i + 2] = (byte)((x ^ y) * 8);
                pixels[i + 3] = (byte)(255 - (x + y));
            }
        }

        var image = new UiImage(0x06004D68, 32, 32, UiPixelFormat.Argb, pixels);
        foreach (var (scale, wantedFactor) in new[] { (1, 1), (2, 2), (3, 3), (8, 8), (9, 8), (16, 8) })
        {
            var cursor = RetailCursor.Prepare(image, 3, 5, scale);
            var exact = cursor is not null && cursor.Width == 32 * wantedFactor && cursor.Height == 32 * wantedFactor && cursor.Factor == wantedFactor;
            if (exact)
            {
                for (var y = 0; y < cursor!.Height && exact; y++)
                {
                    for (var x = 0; x < cursor.Width; x++)
                    {
                        var s = (((y / wantedFactor) * 32) + (x / wantedFactor)) * 4;
                        var d = ((y * cursor.Width) + x) * 4;
                        if (cursor.Rgba[d] != pixels[s] || cursor.Rgba[d + 1] != pixels[s + 1] || cursor.Rgba[d + 2] != pixels[s + 2] || cursor.Rgba[d + 3] != pixels[s + 3])
                        {
                            exact = false;
                            break;
                        }
                    }
                }
            }

            Check(
                $"the cursor at scale {scale}: nearest enlargement by {wantedFactor}, hot spot scaled, at most {RetailCursor.MaximumSize} pixels",
                exact && cursor!.HotspotX == 3 * wantedFactor && cursor.HotspotY == 5 * wantedFactor && cursor.Width <= RetailCursor.MaximumSize,
                cursor is null ? "no cursor" : $"{cursor.Width}x{cursor.Height}, factor {cursor.Factor}, hot spot {cursor.HotspotX},{cursor.HotspotY}, pixels exact {exact}");
        }

        var tooBig = new UiImage(0, 300, 20, UiPixelFormat.Argb, new byte[300 * 20 * 4]);
        Check("a cursor image above the engine's 256 pixels is refused, not cut", RetailCursor.Prepare(tooBig, 0, 0, 1) is null, "300x20 gives null");

        // The engine takes it: shown, and remembered as the current cursor.
        var shown = new RetailCursor(new NoImages());
        var prepared = RetailCursor.Prepare(image, 0, 0, 2)!;
        shown.Show(prepared);
        Check("the engine takes the prepared cursor as its arrow pointer", shown.Current == prepared && shown.Applied == 1, $"current {shown.Current?.Width}x{shown.Current?.Height}");
        shown.Reset();
        Check("giving the pointer back clears the cursor", shown.Current is null, "reset");
    }

    private sealed class NoImages : IUiImageSource
    {
        public UiImageLookup TryGetImage(uint did, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out UiImage? image)
        {
            image = null;
            return UiImageLookup.Missing;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 7. Keys

    private void CheckKeyTable(GameServices services)
    {
        var dats = services.Dats!;
        var reachable = new HashSet<int>();
        foreach (var code in GodotKeyCodes.MappedKeycodes)
        {
            foreach (var location in new[] { UiKeyLocation.Unspecified, UiKeyLocation.Left, UiKeyLocation.Right })
            {
                reachable.Add(GodotKeyCodes.ToDik(code, location));
            }
        }

        reachable.Remove(0);
        foreach (var (name, enumValue) in new[] { ("gmDefaultMap (KEYMAP 0x10000001)", RetailKeyMap.DefaultKeyMapEnum), ("the engine map (KEYMAP 1)", RetailKeyBindings.EngineKeyMapEnum) })
        {
            var map = RetailKeyMap.Load(dats, enumValue);
            var keyboard = map.Bindings.Where(b => b.Control.DeviceIndex == RetailKeyBindings.KeyboardDevice).Select(b => b.Control.Semantic)
                .Concat(map.MetaKeys.Where(m => m.Control.DeviceIndex == RetailKeyBindings.KeyboardDevice).Select(m => m.Control.Semantic))
                .Distinct()
                .ToArray();
            var unbound = keyboard.Where(dik => !reachable.Contains(dik)).Order().ToArray();
            Check(
                $"every key of {name} has a Godot physical key",
                unbound.Length == 0,
                $"{keyboard.Length} keyboard controls bound; no Godot key for: {(unbound.Length == 0 ? "none" : string.Join(", ", unbound.Select(d => $"DIK 0x{d:X2}")))}");
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 8. Fonts

    private void CheckFonts(GameServices services)
    {
        var fonts = new UiFontsSource(new DataUiFonts(services.Dats!), services.Textures, GodotLogSink.Instance);

        // Signed glyph offsets: 'A' of font 0x40000010 has an after-offset of -1.
        var f10 = fonts.GetFont(0x40000010);
        var glyphA = default(UiGlyphDesc);
        var found = f10 is not null && f10.TryGetCharDesc('A', out glyphA);
        Check("glyph offsets are signed ('A' of font 0x40000010 has an after-offset of -1)", found && glyphA.HorizontalOffsetAfter == -1, $"after-offset {glyphA.HorizontalOffsetAfter}");

        // Inside the range, a character the font lacks draws as glyph 0; outside the range it falls back to '?'.
        var palatino = fonts.GetFont(0x40000002)!;
        var missingInside = Enumerable.Range(0x20, 0xFFFC - 0x20 + 1).Select(c => (char)c).FirstOrDefault(c => palatino.TryGetCharDesc(c, out var d) && d.Unicode != c);
        _ = palatino.TryGetCharDesc(missingInside, out var inside);
        Check(
            "a character inside the font's range that it lacks draws as glyph 0 (kept: retail's behaviour)",
            missingInside != 0 && palatino.ContainsChar(missingInside) && inside.Unicode == ' ',
            $"U+{(int)missingInside:X4} draws glyph '{(inside.Unicode == ' ' ? "space" : inside.Unicode.ToString())}'");
        var outside = palatino.TryGetCharDesc('\u0001', out var question);
        Check("a character outside the range falls back to '?'", !palatino.ContainsChar('\u0001') && outside && question.Unicode == '?', $"contains {palatino.ContainsChar('\u0001')}, glyph '{question.Unicode}'");

        // The control character's own font is Gulim (FONT enum 22), whose range starts at U+0000.
        var japanese = fonts.GetFontByEnum(DataUiFonts.FallbackEnumJapanese);
        Check("a control character is drawn from the Gulim fallback font (FONT enum 22)", japanese is { Did: 0x40000018 } && japanese.ContainsChar('\u0001'), $"font 0x{japanese?.Did ?? 0:X8}");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 9. The real screens through the whole layer

    private RetailUiLayer MakeLayer(GameServices services, Stage stage, Vector2I window, RetailUiOptions? options = null, Func<Lifestone.Client.Audio.ISoundSink?>? sound = null)
    {
        var layer = RetailUiLayer.Create(services, options ?? new RetailUiOptions { Mode = RetailUiMode.StandIn }, window, sound: sound, fixedStep: 1.0 / 60.0);
        stage.View.AddChild(layer);
        layer.SetProcess(false); // the checks step the layer themselves (after it entered the tree: a script with _Process turns processing on when it is ready)
        return layer;
    }

    private async Task CheckRealScreensAsync(GameServices services)
    {
        foreach (var (scale, window) in new[] { (1, new Vector2I(800, 600)), (2, new Vector2I(1600, 1200)) })
        {
            using var stage = NewStage(window);
            var layer = MakeLayer(services, stage, window);
            await FramesAsync(1);
            for (var i = 0; i < 4; i++)
            {
                layer.Step(i / 60.0);
            }

            var settled = layer.Flush(timeoutSeconds: 20);
            await FramesAsync(3);
            var got = Capture(stage.View, out var size);
            var placed = layer.Manager.Roots
                .Select(root => (Root: root, Surface: layer.Compositor.GetSurface(root)))
                .Where(r => r.Surface is not null && r.Root.IsShown)
                .Select(r => new Placed(r.Root.ScreenBox, r.Surface!.Width, r.Surface.Height, r.Surface.Pixels.AsSpan(0, r.Surface.Width * r.Surface.Height * 4).ToArray()))
                .ToArray();
            var (off, within, first) = Compare(got, Expected(size, layer.Scale, placed), 1);
            Check(
                $"the data check at {window.X}x{window.Y} (scale {scale}) reads back as the compositor's surface",
                settled && layer.Scale == scale && placed.Length > 0 && off == 0 && size == window,
                $"scale {layer.Scale}, {placed.Length} surface(s), {off} pixels off by more than 1 ({first}), {within} within 1; {layer.Images.DecodedCount} images decoded on workers, {layer.Compositor.Painter.MissingImages.Count} missing");
            ScreenshotWriter.Save(stage.View, $"{OutputFolder}/datapatch-{window.X}x{window.Y}.png", "uicheck");

            // The same screen composited synchronously, as the tests do: the surface is the same bytes.
            var reference = ReferenceSurface(services, layer, scale, window, steps: 4);
            var live = layer.Manager.Roots.Count == 1 ? layer.Compositor.GetSurface(layer.Manager.Roots[0]) : null;
            var equal = reference is not null && live is not null && reference.Value.Width == live.Width && reference.Value.Height == live.Height
                && reference.Value.Rgba.AsSpan().SequenceEqual(live.Pixels.AsSpan(0, live.Width * live.Height * 4));
            Check(
                $"the worker-decoded screen at scale {scale} equals a synchronous composite of the same screen (the runtime's JPEG decoder)",
                equal,
                reference is null || live is null ? "a surface is missing" : $"{live.Width}x{live.Height} pixels compared");

            if (scale == 1)
            {
                CheckLayerFacts(layer, window);
            }

            layer.QueueFree();
            await FramesAsync(2);
        }

        // The other login screens composite too, and a mode with no screen says so instead of guessing.
        {
            var window = new Vector2I(800, 600);
            using var stage = NewStage(window);
            var layer = MakeLayer(services, stage, window, new RetailUiOptions { Mode = RetailUiMode.StandIn, StartMode = UiModes.CharacterManagement });
            await FramesAsync(1);
            for (var i = 0; i < 4; i++)
            {
                layer.Step(i / 60.0);
            }

            var settled = layer.Flush(timeoutSeconds: 20);
            await FramesAsync(3);
            ScreenshotWriter.Save(stage.View, $"{OutputFolder}/charmanage-800x600.png", "uicheck");
            Check("character management shows its screen through the layer", settled && layer.Manager.Roots.Count == 1 && layer.Flow!.Flow.CurrentMode == UiModes.CharacterManagement, $"mode {UiModes.NameOf(layer.Flow!.Flow.CurrentMode)}, {layer.Manager.Roots.Count} root(s)");
            layer.QueueFree();
            await FramesAsync(2);
        }

        await CheckUiSoundsAsync(services);
    }

    /// <summary>Records the UI sounds the layer asks the sound player for.</summary>
    private sealed class RecordingSink : Lifestone.Client.Audio.ISoundSink
    {
        public List<(uint Table, uint Type)> Centred { get; } = [];

        public void PlayFromCenter(uint soundTableId, uint soundType) => Centred.Add((soundTableId, soundType));

        public void PlayWave(uint waveId, float priority, float probability, float volume, Lifestone.Client.Audio.SoundOrigin origin)
        {
        }

        public void PlayFromTable(uint tableId, uint soundType, Lifestone.Client.Audio.SoundOrigin origin, float volume)
        {
        }
    }

    /// <summary>A log that keeps its lines.</summary>
    private sealed class LinesLog : ILogSink
    {
        public List<string> Lines { get; } = [];

        public bool IsEnabled(LogSeverity severity) => true;

        public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields) => Lines.Add(message);
    }

    /// <summary>
    /// The UI's sounds reach the sound player (m2 wave 3, dod-3-4-4): a click on a character-management button makes the media machine ask for the sound-table
    /// entry its own layout names, and the call reaches a recording <see cref="Lifestone.Client.Audio.ISoundSink"/> through <see cref="UiSoundAdapter"/>; with no
    /// sound player the adapter drops the sound and logs <c>TODO(ui-sound-sink)</c> once.
    /// </summary>
    private async Task CheckUiSoundsAsync(GameServices services)
    {
        var window = new Vector2I(800, 600);
        using var stage = NewStage(window);
        var recorder = new RecordingSink();
        var layer = MakeLayer(services, stage, window, new RetailUiOptions { Mode = RetailUiMode.StandIn, StartMode = UiModes.CharacterManagement }, () => recorder);
        await FramesAsync(1);
        for (var i = 0; i < 4; i++)
        {
            layer.Step(i / 60.0);
        }

        layer.Flush(timeoutSeconds: 20);
        var screen = layer.Flow!.Flow.CurrentScreen as CharacterManagementScreen;
        var exit = screen?.Root?.GetChildRecursive(CharacterManagementScreen.ExitButtonId);
        if (exit is null)
        {
            Check("a button click reaches the sound player with the layout's sound-table entry", false, "no character management screen or no Exit button");
            layer.QueueFree();
            await FramesAsync(2);
            return;
        }

        var wanted = new HashSet<(uint, uint)>(exit.Desc.States.Values.SelectMany(state => state.Media).Concat(exit.Desc.Media).OfType<UiSoundMedia>().Where(m => m.Sound != 0).Select(m => (m.File, m.Sound)));
        recorder.Centred.Clear();
        ClickElement(layer, exit);
        for (var i = 0; i < 12; i++)
        {
            layer.Step((10 + i) / 60.0);
        }

        var heard = recorder.Centred.Distinct().ToArray();
        Check(
            "a click on a button asks the sound player for the sound-table entry its layout names (not silent, nothing else)",
            wanted.Count > 0 && heard.Length > 0 && heard.All(wanted.Contains),
            $"the layout names {wanted.Count} sound entr(ies) [{string.Join(", ", wanted.Select(w => $"0x{w.Item1:X8}/0x{w.Item2:X}"))}]; the sound player heard [{string.Join(", ", heard.Select(w => $"0x{w.Table:X8}/0x{w.Type:X}"))}]");
        layer.QueueFree();
        await FramesAsync(2);

        var log = new LinesLog();
        var adapter = new UiSoundAdapter(() => null, log);
        adapter.PlayFromCenter(0x20000001, 3);
        adapter.PlayWaveFromCenter(0x0A000001, 1f);
        Check(
            "with no sound player the UI's sounds are dropped and TODO(ui-sound-sink) is logged once",
            adapter.Dropped == 2 && log.Lines.Count(l => l.Contains("TODO(ui-sound-sink)", StringComparison.Ordinal)) == 1,
            $"{adapter.Dropped} dropped, {log.Lines.Count} log line(s)");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 9a. The in-game shell through the whole layer (m2-plan G4 g1, docs/retail-ui.md section 21)

    private async Task CheckGameShellAsync(GameServices services, Vector2I window, int scale)
    {
        // The windows are anchored in the UI's own pixels (the window divided by the scale); ScreenBox is in those pixels, the hole and GameViewRect in window pixels.
        var logical = new Vector2I(window.X / scale, window.Y / scale);
        var size = $"{window.X}x{window.Y}";
        using var stage = NewStage(window);
        var layer = MakeLayer(services, stage, window, new RetailUiOptions { Mode = RetailUiMode.StandIn, StartMode = UiModes.GamePlay });
        var source = new SolidSource(new Color(0f, 0.6f, 0.2f));
        layer.RegisterViewportSource(GamePlayScreen.SmartBoxViewportId, source);
        await FramesAsync(1);

        // The shell is made in the frame the mode changes and painted over the next ones; the world stays hidden until it is (no bare-world frame).
        var bareWorldFrames = 0;
        var heldFrames = 0;
        var steps = 0;
        while (steps < 400 && !(layer.Screens?.Flow.CurrentScreen is GamePlayScreen { ShellPainted: true }))
        {
            layer.Step(steps / 60.0);
            steps++;
            if (layer.Screens?.Flow.CurrentScreen is GamePlayScreen { ShellPainted: false })
            {
                heldFrames++;
                if (!layer.WantsWorldHidden)
                {
                    bareWorldFrames++;
                }
            }
        }

        var settled = layer.Flush(timeoutSeconds: 30);
        layer.Step(steps / 60.0);
        await FramesAsync(3);
        ScreenshotWriter.Save(stage.View, $"{OutputFolder}/gameplay-{size}.png", "uicheck");
        var screen = layer.Screens?.Flow.CurrentScreen as GamePlayScreen;
        Check(
            $"the game mode is the in-game shell at {size}: the gameplay root shows over the world with the display released",
            screen is { Root.IsShown: true } && !layer.Manager.IsDisplayForced && settled && layer.Scale == scale && layer.LogicalSize == logical,
            $"screen {layer.Screens?.Flow.CurrentScreen?.GetType().Name ?? "none"}, forced {layer.Manager.IsDisplayForced}, painted after {screen?.PaintFrames} frame(s), {screen?.PaintMilliseconds:0} ms (timed out {screen?.PaintTimedOut}), roots {layer.Manager.Roots.Count}");
        if (screen is null)
        {
            layer.QueueFree();
            await FramesAsync(2);
            return;
        }

        Check(
            $"no frame shows the world without the shell at {size}: the world stays hidden while the shell paints, then shows",
            bareWorldFrames == 0 && heldFrames >= 1 && !screen.PaintTimedOut && screen.ShellPainted && !layer.WantsWorldHidden && !layer.ShellHoldsWorld,
            $"{heldFrames} held frame(s), {bareWorldFrames} bare-world frame(s), world hidden now {layer.WantsWorldHidden}");

        Rect2I Rect(UiElement? e) => e is null ? default : new Rect2I(e.ScreenBox.X0, e.ScreenBox.Y0, e.ScreenBox.Width, e.ScreenBox.Height);
        var whole = new Rect2I(Vector2I.Zero, window);
        var wholeLogical = new Rect2I(Vector2I.Zero, logical);
        string Span(Rect2I r) => $"({r.Position.X},{r.Position.Y})-({r.End.X - 1},{r.End.Y - 1})";
        var radar = new Rect2I(logical.X - 120, 0, 120, 140);
        var mainChat = new Rect2I(0, logical.Y - 100, 410, 100);
        var toolbar = new Rect2I(logical.X - 310, logical.Y - 100, 310, 100);
        var stacked = new Rect2I((logical.X - 160) / 2, 0, 160, 58);
        var at = scale == 1 ? "" : $" in UI pixels (scale {scale}, window pixels x{scale})";
        Check($"the root and the SmartBox fill the {size} window", Rect(screen.Root) == wholeLogical && Rect(screen.SmartBox) == wholeLogical, $"root {Rect(screen.Root)}, SmartBox {Rect(screen.SmartBox)}");
        Check($"the radar sits at the top right, {Span(radar)}{at}", Rect(screen.Radar) == radar, $"{Rect(screen.Radar)}");
        Check($"the main chat sits at the bottom left, {Span(mainChat)}{at}", Rect(screen.MainChat) == mainChat, $"{Rect(screen.MainChat)}");
        Check($"the toolbar sits at the bottom right, {Span(toolbar)}{at}", Rect(screen.Toolbar) == toolbar, $"{Rect(screen.Toolbar)}");
        Check(
            $"the stacked vitals sit at the top centre, {Span(stacked)}{at}, and the side-by-side vitals are hidden (no description yet: the options choose)",
            Rect(screen.StackedVitals) == stacked && screen.StackedVitals!.IsShown && screen.SideBySideVitals is { IsShown: false },
            $"stacked {Rect(screen.StackedVitals)} shown {screen.StackedVitals?.IsShown}, side by side shown {screen.SideBySideVitals?.IsShown}");

        var rootSurface = layer.Compositor.GetSurface(screen.Root!);
        var rootCentre = rootSurface is null ? -1 : (((rootSurface.Height / 2) * rootSurface.Width) + (rootSurface.Width / 2)) * 4;
        var rootOpaque = 0;
        if (rootSurface is not null)
        {
            var pixels = rootSurface.Pixels.AsSpan(0, rootSurface.Width * rootSurface.Height * 4);
            for (var i = 3; i < pixels.Length; i += 4)
            {
                rootOpaque += pixels[i] > 0 ? 1 : 0;
            }
        }

        Check(
            $"the shell draws nothing over the world at {size} in the SmartBox's view: the root's own backdrop is not painted, so the world shows through",
            rootSurface is not null && rootSurface.Pixels[rootCentre + 3] == 0 && rootOpaque < (rootSurface.Width * rootSurface.Height / 20),
            $"root surface {rootSurface?.Width}x{rootSurface?.Height}, centre alpha {(rootSurface is null ? -1 : rootSurface.Pixels[rootCentre + 3])}, {rootOpaque} opaque pixel(s)");

        var hole = layer.Viewports.TryGetHole(GamePlayScreen.SmartBoxViewportId, out var holeRect, out var holeVisible);
        Check($"the SmartBox viewport hole is the whole {size} window, and shows", hole && holeVisible && holeRect == whole, $"hole {holeRect}, visible {holeVisible}, found {hole}");
        Check($"GameViewRect is the SmartBox's rectangle: the whole {size} window", layer.GameViewRect == whole, $"{layer.GameViewRect}");

        // A source registered for the SmartBox viewport (the portal tunnel in play mode) is told the hole (window pixels) and drawn over all of it (the picture lives on the UI canvas, so its size is in UI pixels: the window divided by the scale).
        var picture = layer.Viewports.PictureOf(GamePlayScreen.SmartBoxViewportId);
        var drawn = picture is { Visible: true, Texture: { } texture } ? picture.Scale * texture.GetSize() : default;
        Check(
            $"a source registered for the SmartBox viewport is told the hole and drawn across all of it at {size}",
            source.LastRect == whole && source.LastVisible && picture is { Visible: true } && Math.Abs(drawn.X - logical.X) < 1f && Math.Abs(drawn.Y - logical.Y) < 1f,
            $"told {source.LastRect} (visible {source.LastVisible}), picture {(picture is null ? "none" : $"visible {picture.Visible}, drawn {drawn}")}");
        Check(
            $"the shell has no placeholder windows left at {size} (g5b built the selection, the last)",
            layer.Screens!.Flow.CurrentScreen is IUiPlaceholderScreen { PlaceholderNote: null } && screen.Placeholders.Count == 0,
            $"note {(screen.PlaceholderNote ?? "none")}");

        layer.QueueFree();
        await FramesAsync(2);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 9b. The login flow through the whole layer (m2-plan G2 U3-U4)

    private async Task CheckLoginFlowAsync(GameServices services)
    {
        var window = new Vector2I(800, 600);
        using var stage = NewStage(window);
        var layer = MakeLayer(services, stage, window);
        await FramesAsync(1);
        var time = 0.0;
        void Run(double seconds)
        {
            var frames = Math.Max(1, (int)Math.Ceiling(seconds * 60.0));
            for (var i = 0; i < frames; i++)
            {
                layer.Step(time);
                time += 1.0 / 60.0;
            }
        }

        Run(0.1);
        var flow = layer.Flow!.Flow;
        var dataCheck = flow.CurrentScreen as DataPatchScreen;
        Check(
            "the flow starts on the data check, with the connection up and the check under way",
            flow.CurrentMode == UiModes.DataPatch && dataCheck is { ConnectLevel: 1f, PatchLevel: 0f } && dataCheck.StatusText.StartsWith("Looking for data", StringComparison.Ordinal),
            $"mode {UiModes.NameOf(flow.CurrentMode)}, connect {dataCheck?.ConnectLevel}, patch {dataCheck?.PatchLevel}, status '{dataCheck?.StatusText}'");
        layer.Flush(timeoutSeconds: 20);
        await FramesAsync(3);
        ScreenshotWriter.Save(stage.View, $"{OutputFolder}/login-datapatch-800x600.png", "uicheck");

        Run(1.0);
        var stillChecking = flow.CurrentMode == UiModes.DataPatch;
        Run(1.5);
        var screen = flow.CurrentScreen as CharacterManagementScreen;
        Check(
            "the data check ends after its two seconds and character management starts (the intro is skipped)",
            stillChecking && screen is not null && flow.CurrentMode == UiModes.CharacterManagement,
            $"mode {UiModes.NameOf(flow.CurrentMode)} at {time:0.00} s, still checking at 1.1 s: {stillChecking}");
        if (screen is null)
        {
            layer.QueueFree();
            return;
        }

        Run(0.2);
        var names = screen.List!.Items.Select(i => ((UiTextElement)i).Text).ToArray();
        Check(
            "character management lists the stand-in characters sorted, the one pending deletion last, the first ready one selected",
            names.SequenceEqual(["Stand-in Alpha", "Stand-in Beta", "Stand-in Gamma"]) && screen.SelectedGuid == StandInGuid(0) && !screen.RestoreButton!.Visible && screen.DeleteButton!.Visible,
            $"rows {string.Join(", ", names)}; selected 0x{screen.SelectedGuid:X8}");
        layer.Flush(timeoutSeconds: 20);
        await FramesAsync(3);
        ScreenshotWriter.Save(stage.View, $"{OutputFolder}/login-charmanage-800x600.png", "uicheck");

        // A character pending deletion ghosts Enter and swaps Delete for Restore.
        var gamma = screen.List.Items.First(i => ((UiTextElement)i).Text == "Stand-in Gamma");
        ClickElement(layer, gamma);
        Run(0.1);
        Check(
            "selecting the character pending deletion ghosts Enter and shows Restore in Delete's place",
            screen.EnterButton!.State == UiStateIds.Ghosted && screen.RestoreButton!.Visible && !screen.DeleteButton!.Visible,
            $"enter state {screen.EnterButton!.State}, restore {screen.RestoreButton!.Visible}, delete {screen.DeleteButton!.Visible}");

        // Restore it: the stand-in answers after its delay and the list is rebuilt.
        ClickElement(layer, screen.RestoreButton);
        Run(0.1);
        var waiting = screen.PleaseWaitDialog != 0;
        Run(1.0);
        var entries = layer.Session!.State.Characters.Entries;
        Check(
            "restore shows Please Wait, then the stand-in answers and the character is ready again",
            waiting && screen.PleaseWaitDialog == 0 && entries.All(e => !e.PendingDeletion) && screen.EnterButton.State == UiStateIds.Normal,
            $"waiting {waiting}, wait dialog now {screen.PleaseWaitDialog}, pending {entries.Count(e => e.PendingDeletion)}");

        // Delete Beta with the typed confirmation.
        ClickElement(layer, screen.List.Items.First(i => ((UiTextElement)i).Text == "Stand-in Beta"));
        Run(0.1);
        ClickElement(layer, screen.DeleteButton);
        Run(0.1);
        var dialog = layer.Manager.Dialogs.Find(screen.DeleteDialog);
        layer.Flush(timeoutSeconds: 20);
        await FramesAsync(3);
        ScreenshotWriter.Save(stage.View, $"{OutputFolder}/login-charmanage-delete-confirm-800x600.png", "uicheck");
        if (dialog?.GetChildRecursive(UiConfirmationTextInputDialog.Field) is UiTextElement field && dialog.GetChildRecursive(UiConfirmationTextInputDialog.DoneButton) is { } done)
        {
            field.SetText("delete");
            ClickElement(layer, done);
        }

        Run(0.2);
        var asked = screen.PleaseWaitDialog != 0;
        layer.Flush(timeoutSeconds: 20);
        await FramesAsync(3);
        ScreenshotWriter.Save(stage.View, $"{OutputFolder}/login-charmanage-please-wait-800x600.png", "uicheck");
        Run(1.0);
        var beta = layer.Session.State.Characters.Entries.First(e => e.Name == "Stand-in Beta");
        Check(
            "delete asks for the typed word, waits, and the character is listed pending deletion, last",
            dialog is not null && asked && beta.PendingDeletion && screen.PleaseWaitDialog == 0
                && screen.List.Items.Select(i => ((UiTextElement)i).Text).SequenceEqual(["Stand-in Alpha", "Stand-in Gamma", "Stand-in Beta"]),
            $"dialog {dialog is not null}, waited {asked}, beta pending {beta.PendingDeletion}");
        layer.Flush(timeoutSeconds: 20);
        await FramesAsync(3);
        ScreenshotWriter.Save(stage.View, $"{OutputFolder}/login-charmanage-after-delete-800x600.png", "uicheck");

        // A lost connection: the disconnected screen with its reason, and its button reconnects (D43) to the data check.
        layer.Screens!.Gm.RecvNoticeServerDied();
        Run(0.2);
        var disconnected = flow.CurrentScreen as DisconnectedScreen;
        layer.Flush(timeoutSeconds: 20);
        await FramesAsync(3);
        ScreenshotWriter.Save(stage.View, $"{OutputFolder}/login-disconnected-800x600.png", "uicheck");
        Check(
            "a lost connection shows the disconnected screen with the lost-connection reason",
            disconnected is not null && disconnected.ReasonText.StartsWith("The connection to the server has been lost", StringComparison.Ordinal),
            $"mode {UiModes.NameOf(flow.CurrentMode)}, reason '{disconnected?.ReasonText}'");
        if (disconnected is not null)
        {
            ClickElement(layer, disconnected.Root!.GetChildRecursive(DisconnectedScreen.ButtonId)!);
            Run(0.2);
            Check(
                "the disconnected button reconnects and the flow starts again at the data check",
                flow.CurrentMode == UiModes.DataPatch && layer.Session!.State.DataCheck == Lifestone.Client.Session.Contracts.DataCheckState.Checking,
                $"mode {UiModes.NameOf(flow.CurrentMode)}, data check {layer.Session!.State.DataCheck}");
        }

        layer.QueueFree();
        await FramesAsync(2);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 9c. Character creation's 3D character: the retail animation (D60)

    /// <summary>
    /// <c>gmCG3DView::StartAnimation</c> loops the character on the clothes half (zoomed out) and the summary page; <c>StopAnimation</c> holds the rest pose
    /// zoomed in on the face (11.6096 0x004EF290, 0x004EF2D0). The pose each view's character is drawn in is compared at two times.
    /// </summary>
    private async Task CheckCharGenAnimationAsync(GameServices services)
    {
        var window = new Vector2I(800, 600);
        using var stage = NewStage(window);
        var layer = MakeLayer(services, stage, window);
        await FramesAsync(1);
        var time = 0.0;
        void Run(double seconds)
        {
            var frames = Math.Max(1, (int)Math.Ceiling(seconds * 60.0));
            for (var i = 0; i < frames; i++)
            {
                layer.Step(time);
                time += 1.0 / 60.0;
            }
        }

        Run(3.2); // the data check's two seconds and character management
        var flow = layer.Flow!.Flow;
        if (flow.CurrentScreen is not CharacterManagementScreen { CreateButton: { } create })
        {
            Check("the chargen animation check reaches character management", false, $"mode {UiModes.NameOf(flow.CurrentMode)}");
            layer.QueueFree();
            return;
        }

        ClickElement(layer, create);
        Run(0.6);
        if (flow.CurrentScreen is not CharGenMainScreen chargen)
        {
            Check("the chargen animation check reaches character creation", false, $"mode {UiModes.NameOf(flow.CurrentMode)}");
            layer.QueueFree();
            return;
        }

        for (var i = 0; i < 3; i++)
        {
            ClickElement(layer, chargen.RightButton!); // heritage, profession, skills: Next opens the appearance page, zoomed in on the face
            Run(0.4);
        }

        Run(1.0);
        var face = chargen.Appearance?.View;
        var faceOk = chargen.Progress == CharGenPageId.Appearance && chargen.Appearance is { IsZoomedIn: true, IsZooming: false } && face?.Preview is not null;
        if (!faceOk)
        {
            Check("a zoomed-in face view holds its rest pose", false, $"page {chargen.Progress}, view {(face is null ? "none" : "made")}");
            layer.QueueFree();
            return;
        }

        var held = face!.Preview!.Draws.ToArray();
        Run(0.5);
        Check(
            "a zoomed-in face view holds its rest pose: its pose is the same half a second later",
            !face.IsAnimating && held.AsSpan().SequenceEqual(face.Preview.Draws.ToArray()),
            $"animating {face.IsAnimating}, rest 0x{face.Preview.Animations.Rest:X8}");

        ClickElement(layer, chargen.Appearance!.ClothesButton!);
        Run(1.4);
        var clothes = chargen.Appearance.View!;
        var before = clothes.Preview!.Draws.ToArray();
        Run(0.2);
        Check(
            "a zoomed-out clothes view loops: its pose changes in a fifth of a second",
            clothes.IsAnimating && !before.AsSpan().SequenceEqual(clothes.Preview.Draws.ToArray()),
            $"animating {clothes.IsAnimating}, frame {clothes.Preview.Sequence.FrameNumber:0.0}");

        ClickElement(layer, chargen.RightButton!);
        Run(0.4);
        ClickElement(layer, chargen.RightButton!);
        Run(0.6);
        var summary = chargen.Summary?.View;
        if (chargen.Progress != CharGenPageId.Summary || summary?.Preview is null)
        {
            Check("the summary view's pose changes", false, $"page {chargen.Progress}, view {(summary is null ? "none" : "made")}");
            layer.QueueFree();
            return;
        }

        var first = summary.Preview.Draws.ToArray();
        var firstFrame = summary.Preview.Sequence.FrameNumber;
        Run(0.5);
        Check(
            "the summary view loops: its pose changes between two times half a second apart",
            summary.IsAnimating && !first.AsSpan().SequenceEqual(summary.Preview.Draws.ToArray()),
            $"animating {summary.IsAnimating}, frame {firstFrame:0.0} then {summary.Preview.Sequence.FrameNumber:0.0}");

        layer.QueueFree();
        await FramesAsync(2);
    }

    private static uint StandInGuid(int index) => Lifestone.Client.Session.StandIn.StandInSessions.FirstCharacterGuid + (uint)index;

    /// <summary>Clicks the middle of <paramref name="element"/> with the mouse, through the layer's input router.</summary>
    private static void ClickElement(RetailUiLayer layer, UiElement element)
    {
        var box = element.ScreenBox;
        layer.Manager.Input.MouseMove((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
        layer.Manager.Input.MouseButton(UiMouseButton.Left, down: true);
        layer.Manager.Input.MouseButton(UiMouseButton.Left, down: false);
    }

    /// <summary>The layer's screen composited with the synchronous image source (what the unit tests do), or null.</summary>
    private static (int Width, int Height, byte[] Rgba)? ReferenceSurface(GameServices services, RetailUiLayer layer, int scale, Vector2I window, int steps)
    {
        var dats = services.Dats!;
        var log = GodotLogSink.Instance;
        var images = new DatUiImageSource(services.Textures, log);
        var live = layer.Manager.Services;
        var manager = new UiElementManager(
            new UiServices
            {
                Descs = live.Descs,
                Log = log,
                Random = new UiSeededRandom(1),
                Fonts = live.Fonts,
                Strings = live.Strings,
                Images = images,
            },
            UiScalePolicy.Choose(window.X, window.Y, scale));
        using var offline = Lifestone.Game.Sessions.SessionHost.CreateStandIn(null, scripted: true);
        using var screens = new RetailUiScreenHost(manager, offline.Session, new NoHost(), new UiCursors(dats).Default);
        var compositor = new UiCompositor(images, log);
        compositor.Attach(manager);
        for (var i = 0; i < steps; i++)
        {
            offline.Step(i / 60.0);
            manager.Update(i / 60.0);
            screens.Update();
            compositor.Composite(long.MaxValue);
        }

        if (manager.Roots.Count != 1 || compositor.GetSurface(manager.Roots[0]) is not { } surface)
        {
            return null;
        }

        return (surface.Width, surface.Height, surface.Pixels.AsSpan(0, surface.Width * surface.Height * 4).ToArray());
    }

    /// <summary>The application of a reference screen: nothing closes or reconnects.</summary>
    private sealed class NoHost : IUiLoginHost
    {
        public void Exit()
        {
        }

        public bool Reconnect() => false;
    }

    /// <summary>What the layer says about itself: its contract members and the cursor and idle behaviour.</summary>
    private void CheckLayerFacts(RetailUiLayer layer, Vector2I window)
    {
        Check(
            "the layer's contract members: scale, logical size, the whole window as the game view, the world hidden behind a forced display",
            layer.Scale == 1 && layer.LogicalSize == window && layer.GameViewRect == new Rect2I(Vector2I.Zero, window) && layer.WantsWorldHidden && layer.Layer == RetailUiLayer.LayerSlot && !layer.KeyboardCaptured,
            $"scale {layer.Scale}, logical {layer.LogicalSize}, game view {layer.GameViewRect}, world hidden {layer.WantsWorldHidden}, layer {layer.Layer}");
        var cursor = layer.Cursor.Current;
        Check(
            "the default pointer (UICURSOR 1) is the engine's cursor",
            cursor is not null && cursor.Did == layer.Manager.Cursor.CursorDid && cursor.Did != 0 && cursor.Width <= RetailCursor.MaximumSize,
            cursor is null ? "no cursor shown" : $"0x{cursor.Did:X8} {cursor.Width}x{cursor.Height}, hot spot {cursor.HotspotX},{cursor.HotspotY}");
        var held = GameServices.Instance.WorldVisibility.Held(RetailUiLayer.WorldHideOwner);
        Check("the layer holds one hide request on the world while a full-screen screen shows", held == 1, $"{held} request(s) held");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 10. Input

    private sealed class RecordingMap(bool takes) : IInputActionSink
    {
        public List<(uint Map, uint Action, bool Start)> Actions { get; } = [];

        public bool OnInputAction(uint inputMapId, uint actionId, RetailActivation activation)
        {
            Actions.Add((inputMapId, actionId, activation.Start));
            return takes;
        }
    }

    private sealed class RecordingWorld : IWorldPointerSink
    {
        public List<string> Events { get; } = [];

        public bool TextEntryActive { get; set; }

        public void OnWorldPointer(int x, int y, UiMouseButton button, bool down, bool doubleClick) => Events.Add($"{button} {(down ? "down" : "up")} {x},{y}");

        public void OnMouseLook(float dx, float dy) => Events.Add($"look {dx},{dy}");
    }

    private async Task CheckInputAsync(GameServices services)
    {
        // Scale 2 in a 1700 x 1300 window: the forced 800x600 display sits at (25, 25) in the 850x650 logical window.
        var window = new Vector2I(1700, 1300);
        using var stage = NewStage(window);
        var layer = MakeLayer(services, stage, window, new RetailUiOptions { Mode = RetailUiMode.StandIn, StartMode = UiModes.CharacterManagement });
        await FramesAsync(1);
        for (var i = 0; i < 4; i++)
        {
            layer.Step(i / 60.0);
        }

        layer.Flush(timeoutSeconds: 20);
        var router = layer.Manager.Input;

        // A key reaches a registered map as its action (W is Move Forward, map 4 action 0x29).
        var movement = new RecordingMap(takes: true);
        router.RegisterInputMap(UiInputRouter.MovementMap, movement, 1000);
        var w = new InputEventKey { PhysicalKeycode = Key.W, Keycode = Key.W, Pressed = true };
        var taken = layer.Input.Handle(w);
        layer.Input.Handle(new InputEventKey { PhysicalKeycode = Key.W, Keycode = Key.W, Pressed = false });
        Check(
            "a Godot key reaches a registered input map as its retail action (W is Move Forward)",
            taken && movement.Actions.Count >= 1 && movement.Actions[0] == (4u, 0x29u, true),
            $"taken {taken}, actions {string.Join(", ", movement.Actions.Select(a => $"{a.Map:X}:{a.Action:X}:{(a.Start ? "start" : "end")}"))}");
        router.UnregisterCallback(movement);

        // A key nothing binds is not taken (the explore camera and the F-keys still see it).
        var f9 = layer.Input.Handle(new InputEventKey { PhysicalKeycode = Key.F9, Keycode = Key.F9, Pressed = true });
        layer.Input.Handle(new InputEventKey { PhysicalKeycode = Key.F9, Keycode = Key.F9, Pressed = false });
        Check("a key no registered map binds is not taken", !f9, $"F9 taken {f9}");

        // The mouse: window pixels divided by the scale, rounded down; a press over no screen goes to the world.
        var world = new RecordingWorld();
        layer.World.Target = world;
        var motion = layer.Input.Handle(new InputEventMouseMotion { Position = new Vector2(11, 13) });
        var down = layer.Input.Handle(new InputEventMouseButton { Position = new Vector2(11, 13), ButtonIndex = MouseButton.Left, Pressed = true });
        var up = layer.Input.Handle(new InputEventMouseButton { Position = new Vector2(11, 13), ButtonIndex = MouseButton.Left, Pressed = false });
        Check(
            "a press outside the screens reaches the world in logical pixels (scale 2: window 11,13 is 5,6)",
            !motion && !down && !up && layer.Scale == 2 && world.Events.SequenceEqual(["Left down 5,6", "Left up 5,6"]),
            $"scale {layer.Scale}, events: {string.Join("; ", world.Events)}");
        (int X, int Y)? over = null;
        for (var y = 0; y < 600 && over is null; y += 4)
        {
            for (var x = 0; x < 800 && over is null; x += 4)
            {
                if (layer.Manager.HitTest(25 + x, 25 + y) is not null)
                {
                    over = (25 + x, 25 + y);
                }
            }
        }

        var inside = over is { } point && layer.Input.Handle(new InputEventMouseMotion { Position = new Vector2((point.X * 2) + 1, (point.Y * 2) + 1) });
        Check("the mouse over an element of a screen is the UI's", inside, $"first element under the mouse at logical {over}, taken {inside}");

        // A text field's keyboard: reported once each way, and the typed character is taken.
        var changes = new List<bool>();
        layer.KeyboardCapturedChanged += changes.Add;
        router.SetTextMode(true);
        router.SetTextMode(true);
        var typed = layer.Input.Handle(new InputEventKey { PhysicalKeycode = Key.A, Keycode = Key.A, Unicode = 'a', Pressed = true });
        var capturedOn = layer.KeyboardCaptured && world.TextEntryActive;
        router.SetTextMode(false);
        Check(
            "a text field's keyboard capture is reported once each way and takes the keys",
            typed && capturedOn && !layer.KeyboardCaptured && changes.SequenceEqual([true, false]),
            $"typed taken {typed}, captured while on {capturedOn}, after {layer.KeyboardCaptured}, changes {string.Join(",", changes)}");

        // The focus notifications.
        layer.Input._Notification((int)Node.NotificationApplicationFocusOut);
        var lost = !router.ClientActive;
        layer.Input._Notification((int)Node.NotificationApplicationFocusIn);
        Check("the focus notifications call SetClientActive", lost && router.ClientActive, $"client active after focus out: {!lost}, after focus in: {router.ClientActive}");

        // An idle layer frame allocates nothing.
        layer.Manager.Input.MouseLeave();
        for (var i = 0; i < 30; i++)
        {
            layer.Step(10 + (i / 60.0));
        }

        layer.Flush(timeoutSeconds: 5);
        await FramesAsync(2);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 120; i++)
        {
            layer.Step(20 + (i / 60.0));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("an idle layer frame allocates nothing (120 frames of character management)", allocated == 0, $"{allocated} bytes over 120 frames");

        layer.QueueFree();
        await FramesAsync(2);
    }
}

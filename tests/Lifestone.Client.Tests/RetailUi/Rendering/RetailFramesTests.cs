using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;
using Lifestone.Data.Ui;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.RetailUi.Rendering;

/// <summary>
/// The first retail screens composited from the user's dats (m2-plan.md R11): the data check, the disconnected screen and
/// character management, as their layouts stand (no screen logic yet: R13-R15 and the screen tracks add it, so these are
/// stand-in frames). Always: they composite completely and deterministically. With <c>LIFESTONE_UI_FRAMES</c> set (a
/// folder, or <c>1</c> for <c>cache/ui-frames</c>) the frames are written as PNG files for review; never into the
/// repository's source trees.
/// </summary>
public sealed class RetailFramesTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const string FramesVariable = "LIFESTONE_UI_FRAMES";

    public static TheoryData<string, uint, uint, int, int> Screens()
    {
        var data = new TheoryData<string, uint, uint, int, int>();
        foreach (var (width, height) in new[] { (800, 600), (1920, 1080) })
        {
            data.Add("datacheck", UiLayoutEnums.DataPatchRoot.LayoutEnum, UiLayoutEnums.DataPatchRoot.ElementId, width, height);
            data.Add("disconnected", UiLayoutEnums.DisconnectedRoot.LayoutEnum, UiLayoutEnums.DisconnectedRoot.ElementId, width, height);
            data.Add("charactermanagement", UiLayoutEnums.CharacterManagementRoot.LayoutEnum, UiLayoutEnums.CharacterManagementRoot.ElementId, width, height);
        }

        return data;
    }

    [RequiresLocalDatsTheory]
    [MemberData(nameof(Screens))]
    public void A_login_screen_composites_completely_and_deterministically(string name, uint layoutEnum, uint elementId, int width, int height)
    {
        var first = Render(layoutEnum, elementId, width, height, out var root, out var missing);
        var second = Render(layoutEnum, elementId, width, height, out _, out _);

        Assert.Equal(first.Pixels, second.Pixels);
        Assert.Equal(800, root.Box.Width);
        Assert.Equal(600, root.Box.Height);
        Assert.Contains(first.Pixels.Where((_, i) => i % 4 == 3), a => a != 0);
        output.WriteLine($"{name} {width}x{height}: root at {root.ScreenBox}; images drawn as the default graphic: {string.Join(", ", missing.Select(d => $"0x{d:X8}"))}");

        var folder = FramesFolder();
        if (folder is null)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        var window = Present(root, first, width, height);
        var file = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{name}-{width}x{height}-standin.png"));
        PngWriter.Write(file, width, height, window);
        output.WriteLine($"wrote {file}");
    }

    private UiSurface Render(uint layoutEnum, uint elementId, int width, int height, out UiElement root, out IReadOnlyCollection<uint> missing)
    {
        var dats = fixture.Dats;
        var log = new RecordingLogSink(LogSeverity.Warning);
        var images = new DatUiImageSource(new TextureLoader(dats), log);
        var manager = new UiElementManager(
            new UiServices
            {
                Descs = UiDescSource.From(new UiLayouts(dats)),
                Fonts = new UiFontsSource(dats, log),
                Strings = new UiStringsSource(new UiStrings(new UiStringTables(dats)), UiLanguageInfo.Load(dats)),
                Images = images,
                Log = log,
            },
            UiScalePolicy.Choose(width, height));
        manager.ForceDisplayResolution(true);
        var compositor = new UiCompositor(images, log);
        compositor.Attach(manager);

        root = manager.CreateRootElement(layoutEnum, elementId)!;
        manager.Update(0);
        Assert.True(compositor.Composite(long.MaxValue));
        missing = compositor.Painter.MissingImages;
        return compositor.GetSurface(root)!;
    }

    /// <summary>The window as the host presents it: black, with the root surface over it at its screen box (straight alpha).</summary>
    private static byte[] Present(UiElement root, UiSurface surface, int width, int height)
    {
        var window = new byte[width * height * 4];
        for (var i = 3; i < window.Length; i += 4)
        {
            window[i] = 255;
        }

        var box = root.ScreenBox;
        for (var y = 0; y < surface.Height; y++)
        {
            for (var x = 0; x < surface.Width; x++)
            {
                var wx = box.X0 + x;
                var wy = box.Y0 + y;
                if (wx < 0 || wy < 0 || wx >= width || wy >= height)
                {
                    continue;
                }

                var s = ((y * surface.Width) + x) * 4;
                var d = ((wy * width) + wx) * 4;
                var a = surface.Pixels[s + 3];
                for (var c = 0; c < 3; c++)
                {
                    window[d + c] = (byte)((surface.Pixels[s + c] * a / 255) + (window[d + c] * (255 - a) / 255));
                }
            }
        }

        return window;
    }

    private static string? FramesFolder()
    {
        var value = Environment.GetEnvironmentVariable(FramesVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var requested = value is "1" or "true" ? Path.Combine("cache", "ui-frames") : value;
        Assert.True(OutputPaths.TryResolve(requested, RepositoryFiles.MainCheckout, out var full, out var problem), problem);
        return full;
    }
}

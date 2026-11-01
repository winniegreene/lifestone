using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;

namespace Lifestone.Client.Tests.RetailUi.TestSupport;

/// <summary>
/// Review frames of a running shell (M5 7 P7r): the rig's whole UI composited from the user's dats by the compositor the game presents with (<see cref="UiCompositor"/>, as <c>RetailUiLayer</c> does), every surface at its screen
/// box over black (the 3D world is not drawn: the frame shows the UI only). Written only when <c>LIFESTONE_UI_FRAMES</c> names a folder (or <c>1</c> for <c>cache/ui-frames</c>), as <c>RetailFramesTests</c> does; never into the
/// repository's source trees. Test support only.
/// </summary>
internal static class UiReviewFrames
{
    private const string FramesVariable = "LIFESTONE_UI_FRAMES";

    /// <summary>Writes <c>&lt;folder&gt;/&lt;name&gt;.png</c> of <paramref name="rig"/>'s UI now and returns its path, or null when no folder is set.</summary>
    public static string? Save(LoginRig rig, DatSetFixture fixture, string name, int width = 800, int height = 600)
    {
        var folder = Folder();
        if (folder is null)
        {
            return null;
        }

        var images = new DatUiImageSource(new TextureLoader(fixture.Dats), rig.Log);
        var compositor = new UiCompositor(images, rig.Log);
        compositor.Attach(rig.Manager);
        for (var i = 0; i < 2000 && (compositor.HasPendingWork || i == 0); i++)
        {
            if (!compositor.Composite(long.MaxValue))
            {
                Thread.Sleep(2);
            }
        }

        var window = new byte[width * height * 4];
        for (var i = 3; i < window.Length; i += 4)
        {
            window[i] = 255;
        }

        var surfaces = new List<IUiRegionView>();
        rig.Manager.CollectSurfaces(surfaces);
        foreach (var view in surfaces)
        {
            if (!view.IsShown || compositor.GetSurface(view) is not { } surface)
            {
                continue;
            }

            var box = view.ScreenBox;
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
                    var a = (int)(surface.Pixels[s + 3] * view.SurfaceAlpha);
                    for (var c = 0; c < 3; c++)
                    {
                        window[d + c] = (byte)((surface.Pixels[s + c] * a / 255) + (window[d + c] * (255 - a) / 255));
                    }
                }
            }
        }

        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, name + ".png");
        PngWriter.Write(file, width, height, window);
        return file;
    }

    private static string? Folder()
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

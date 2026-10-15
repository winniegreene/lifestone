using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Rendering;

/// <summary>
/// The per-frame path allocates nothing (R10, R11): an idle screen's update, composite and tile copy, and a blinking
/// caret's repaint and upload.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class UiAllocationTests
{
    private const uint LayoutId = 0x21000001;

    [Fact]
    public void An_idle_screen_updates_composites_and_copies_tiles_without_allocating()
    {
        var images = new FakeImageSource();
        images.Images[0x06000001] = FakeImageSource.Solid(0x06000001, 16, 16, 0xFF336699);
        var desc = UiDescs.Element(1, UiClassIds.Field, 0, 0, 800, 600, media: [new UiImageMedia(0x06000001, UiDrawMode.Normal)], children:
        [
            UiDescs.Element(2, UiClassIds.Field, 10, 10, 200, 100, media: [new UiImageMedia(0x06000001, UiDrawMode.AlphaBlend)]),
            UiDescs.Element(3, UiClassIds.Text, 10, 200, 200, 20, properties: [new UiStringInfoProperty(UiPropertyIds.TextEntry, UiStringRef.FromLiteral("Idle"))]),
        ]);
        var (manager, compositor, sink) = Scene(images, desc);
        Frame(manager, compositor, sink, 0.0);

        var window = AllocationWindow.Open();
        for (var i = 1; i <= 120; i++)
        {
            Frame(manager, compositor, sink, i / 60.0);
        }

        var (bytes, gc) = window.Close();

        if (!gc)
        {
            Assert.Equal(0, bytes);
        }
    }

    [Fact]
    public void A_blinking_caret_repaints_and_uploads_without_allocating()
    {
        var images = new FakeImageSource();
        var desc = UiDescs.Element(1, UiClassIds.Field, 0, 0, 400, 300, children:
        [
            UiDescs.Element(2, UiClassIds.Text, 10, 10, 200, 20, properties:
            [
                new UiBoolProperty(UiPropertyIds.TextEditable, true),
                new UiStringInfoProperty(UiPropertyIds.TextEntry, UiStringRef.FromLiteral("Name")),
            ]),
        ]);
        var (manager, compositor, sink) = Scene(images, desc);
        var text = (UiTextElement)manager.Roots[0].GetChild(2)!;
        manager.SetFocusElement(text);
        text.MoveCursor(UiTextDirection.TextEnd);

        // Warm up: a few blinks grow every list and buffer once.
        for (var i = 0; i <= 240; i++)
        {
            Frame(manager, compositor, sink, i / 60.0);
        }

        var tilesBefore = sink.Tiles;
        var window = AllocationWindow.Open();
        for (var i = 241; i <= 480; i++)
        {
            Frame(manager, compositor, sink, i / 60.0);
        }

        var (bytes, gc) = window.Close();

        Assert.True(sink.Tiles > tilesBefore, "the caret blinked and its tile was uploaded");
        if (!gc)
        {
            Assert.Equal(0, bytes);
        }
    }

    private static void Frame(UiElementManager manager, UiCompositor compositor, CountingTileSink sink, double now)
    {
        manager.Update(now);
        compositor.Composite(long.MaxValue);
        compositor.CopyDirtyTiles(sink);
    }

    private static (UiElementManager Manager, UiCompositor Compositor, CountingTileSink Sink) Scene(FakeImageSource images, UiElementDesc desc)
    {
        var manager = new UiElementManager(new UiServices
        {
            Descs = new FakeDescSource([UiDescs.Layout(LayoutId, 800, 600, desc)]),
            Fonts = FakeFontSource.With(new FakeFont()),
            Images = images,
        });
        var compositor = new UiCompositor(images);
        compositor.Attach(manager);
        manager.CreateRootElementByDataId(LayoutId, desc.ElementId);
        return (manager, compositor, new CountingTileSink());
    }
}

/// <summary>
/// The dat image source answers the compositor's and the hit test's repeated questions without allocating (no delegate
/// per lookup): a decoded image, and a DID that resolves to nothing.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class DatUiImageSourceAllocationTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint LoginBackground = 0x06004D5F;
    private const uint NoImage = 0x06FFFFFE;

    [RequiresDatsFact]
    public void Asking_again_for_an_image_or_a_missing_one_allocates_nothing()
    {
        var source = new DatUiImageSource(new TextureLoader(fixture.Dats));
        Assert.Equal(UiImageLookup.Ready, source.TryGetImage(LoginBackground, out var first));
        Assert.Equal(UiImageLookup.Missing, source.TryGetImage(NoImage, out _));
        Assert.Equal(UiImageLookup.Missing, source.TryGetImage(0, out _));

        var window = AllocationWindow.Open();
        var same = 0;
        for (var i = 0; i < 1000; i++)
        {
            same += source.TryGetImage(LoginBackground, out var image) == UiImageLookup.Ready && ReferenceEquals(image, first) ? 1 : 0;
            source.TryGetImage(NoImage, out _);
            source.TryGetImage(0, out _);
        }

        var (bytes, gc) = window.Close();

        Assert.Equal(1000, same);
        if (!gc)
        {
            Assert.Equal(0, bytes);
        }
    }
}

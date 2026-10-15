using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Rendering;

/// <summary>
/// The compositor (UIElementManager::DrawRegionWithObject, UIRegion::DrawHere/DrawChildren, UIObject::AddDirtyRect):
/// draw order and clipping, hiding, draw-after-children, erasing, disjoint dirty rectangles, tiles, time slicing.
/// </summary>
public sealed class UiCompositorTests
{
    private const uint LayoutId = 0x21000001;
    private const uint F = UiClassIds.Field;
    private const uint Red = 0xFFFF0000;
    private const uint Green = 0xFF00FF00;
    private const uint Blue = 0xFF0000FF;

    [Fact]
    public void Children_draw_back_to_front_clipped_to_their_parent()
    {
        var scene = Scene(UiDescs.Element(1, F, 0, 0, 100, 100, media: [Image(1)], children:
        [
            UiDescs.Element(2, F, 10, 10, 50, 50, zLevel: 1, media: [Image(2)]),
            UiDescs.Element(3, F, 30, 30, 50, 50, zLevel: 0, media: [Image(3)]),
            UiDescs.Element(4, F, 90, 90, 50, 50, zLevel: 0, readOrder: 1, media: [Image(2)]),
        ]));

        var surface = scene.Composite();

        Assert.Equal(Red, surface.GetArgb(5, 5));
        Assert.Equal(Green, surface.GetArgb(20, 20));
        Assert.Equal(Blue, surface.GetArgb(40, 40));
        Assert.Equal(Blue, surface.GetArgb(70, 70));
        Assert.Equal(Green, surface.GetArgb(99, 99));
        Assert.Equal(100, surface.Width);
    }

    [Fact]
    public void A_hidden_element_draws_nothing_and_its_area_is_repainted_from_what_is_behind()
    {
        var scene = Scene(UiDescs.Element(1, F, 0, 0, 100, 100, media: [Image(1)], children: [UiDescs.Element(2, F, 10, 10, 50, 50, media: [Image(2)])]));
        scene.Composite();

        scene.Root.GetChild(2)!.SetVisible(false);
        var surface = scene.Composite();

        Assert.Equal(Red, surface.GetArgb(20, 20));
    }

    [Fact]
    public void Draw_after_children_paints_the_element_over_its_children()
    {
        var scene = Scene(UiDescs.Element(1, F, 0, 0, 100, 100, children:
        [
            UiDescs.Element(2, F, 0, 0, 50, 50, properties: [new UiBoolProperty(UiPropertyIds.ElementDrawAfterChildren, true)], media: [Image(1)], children:
            [
                UiDescs.Element(3, F, 0, 0, 10, 10, media: [Image(2)]),
            ]),
        ]));

        Assert.Equal(Red, scene.Composite().GetArgb(5, 5));
    }

    [Fact]
    public void An_erasing_element_clears_its_dirty_pieces_before_drawing()
    {
        var scene = Scene(UiDescs.Element(1, F, 0, 0, 100, 100, properties: [new UiBoolProperty(UiPropertyIds.ElementShouldEraseBackground, true)], children:
        [
            UiDescs.Element(2, F, 10, 10, 50, 50, media: [Image(1)]),
        ]));
        scene.Composite();
        Assert.Equal(Red, scene.Surface.GetArgb(20, 20));

        scene.Root.GetChild(2)!.SetVisible(false);

        Assert.Equal(0u, scene.Composite().GetArgb(20, 20));
    }

    [Fact]
    public void Without_erasing_a_hidden_elements_pixels_stay_as_retail_left_them()
    {
        var scene = Scene(UiDescs.Element(1, F, 0, 0, 100, 100, children: [UiDescs.Element(2, F, 10, 10, 50, 50, media: [Image(1)])]));
        scene.Composite();

        scene.Root.GetChild(2)!.SetVisible(false);

        Assert.Equal(Red, scene.Composite().GetArgb(20, 20));
    }

    [Fact]
    public void Overlapping_dirty_rectangles_are_painted_once()
    {
        // A half-transparent image over a transparent surface: painting a pixel twice would blend it twice.
        var scene = Scene(UiDescs.Element(1, F, 0, 0, 100, 100, children: [UiDescs.Element(2, F, 0, 0, 100, 100, media: [new UiImageMedia(5, UiDrawMode.Overlay)])]));
        scene.Images.Images[5] = FakeImageSource.Solid(5, 100, 100, 0x80FFFFFF);
        var once = scene.Composite().GetArgb(15, 15);
        Assert.Equal(0x00808080u, once);

        scene.Compositor.AddDirtyRect(scene.Root, new UiBox(0, 0, 19, 19));
        scene.Compositor.AddDirtyRect(scene.Root, new UiBox(10, 10, 29, 29));
        scene.Compositor.AddDirtyRect(scene.Root, new UiBox(5, 5, 14, 14));
        var surface = scene.Composite();

        // The surface keeps the first paint's alpha (0) and ThreeChannelAlpha blends the colour again where dirty, once.
        Assert.Equal(0x00C0C0C0u, surface.GetArgb(15, 15));
        Assert.Equal(0x00C0C0C0u, surface.GetArgb(5, 5));
        Assert.Equal(0x00808080u, surface.GetArgb(40, 40));
    }

    [Fact]
    public void Only_the_tiles_of_painted_areas_are_handed_over_packed()
    {
        var scene = Scene(UiDescs.Element(1, F, 0, 0, 300, 200, media: [Image(1)]));
        scene.Composite();
        var sink = new RecordingTileSink();
        scene.Compositor.CopyDirtyTiles(sink);

        Assert.Single(sink.Frames);
        Assert.Equal(new UiRootFrame(1, 0, 1, new UiBox(0, 0, 299, 199), 3, 2, true, 1f), sink.Frames[0]);
        Assert.Equal(6, sink.Tiles.Count);
        var edge = sink.Tiles.Single(t => t.Tile.TileX == 2 && t.Tile.TileY == 1);
        Assert.Equal(new UiBox(256, 128, 299, 199), edge.Tile.Box);
        Assert.Equal(44 * 72 * 4, edge.Pixels.Length);

        sink.Clear();
        scene.Compositor.CopyDirtyTiles(sink);
        Assert.Empty(sink.Frames);
        Assert.Empty(sink.Tiles);

        scene.Compositor.AddDirtyRect(scene.Root, new UiBox(130, 10, 140, 20));
        scene.Composite();
        scene.Compositor.CopyDirtyTiles(sink);
        Assert.Equal([(1, 0)], sink.Tiles.Select(t => (t.Tile.TileX, t.Tile.TileY)));
        Assert.Equal(0xFF, sink.Tiles[0].Pixels[0]);
    }

    [Fact]
    public void A_passed_deadline_hands_over_one_tile_a_call_and_the_rest_waits_dirty()
    {
        // The shell's first upload is spread over frames (G4 live-gate fix 3): with the deadline already past, each call sends the root's frame once and one tile.
        var scene = Scene(UiDescs.Element(1, F, 0, 0, 300, 200, media: [Image(1)]));
        scene.Composite();
        var sink = new RecordingTileSink();

        for (var call = 1; call <= 6; call++)
        {
            Assert.True(scene.Compositor.HasChangesToCopy, $"before call {call}");
            scene.Compositor.CopyDirtyTiles(sink, deadline: 0);
            Assert.Equal(call, sink.Tiles.Count);
        }

        Assert.Single(sink.Frames);
        Assert.Equal(6, sink.Tiles.Select(t => (t.Tile.TileX, t.Tile.TileY)).Distinct().Count());
        Assert.False(scene.Compositor.HasChangesToCopy);
    }

    [Fact]
    public void A_moved_or_hidden_root_is_re_announced_without_tiles_and_a_deleted_one_removed()
    {
        var scene = Scene(UiDescs.Element(1, F, 0, 0, 50, 50, media: [Image(1)]));
        scene.Composite();
        var sink = new RecordingTileSink();
        scene.Compositor.CopyDirtyTiles(sink);
        sink.Clear();

        scene.Manager.ForceDisplayResolution(true, 50, 50);
        scene.Root.SetVisible(false);
        scene.Compositor.CopyDirtyTiles(sink);

        Assert.Single(sink.Frames);
        Assert.False(sink.Frames[0].Visible);
        Assert.Equal(new UiBox(375, 275, 424, 324), sink.Frames[0].ScreenBox);

        scene.Manager.DeleteElement(scene.Root);
        scene.Compositor.Composite(long.MaxValue);
        scene.Compositor.CopyDirtyTiles(sink);
        Assert.Equal([1], sink.Removed);
        Assert.Equal(0, scene.Compositor.RootCount);
    }

    [Fact]
    public void HasChangesToCopy_says_whether_CopyDirtyTiles_would_hand_the_sink_anything()
    {
        var scene = Scene(UiDescs.Element(1, F, 0, 0, 300, 200, media: [Image(1)]));
        Assert.False(scene.Compositor.HasChangesToCopy); // the compositor has not met the surface yet (Composite picks it up)
        scene.Composite();
        Assert.True(scene.Compositor.HasChangesToCopy); // met and painted: its frame is unsent and its tiles wait
        var sink = new RecordingTileSink();
        scene.Compositor.CopyDirtyTiles(sink);
        Assert.False(scene.Compositor.HasChangesToCopy);

        scene.Compositor.AddDirtyRect(scene.Root, new UiBox(130, 10, 140, 20));
        Assert.False(scene.Compositor.HasChangesToCopy); // dirty but not painted: nothing finished yet
        scene.Composite();
        Assert.True(scene.Compositor.HasChangesToCopy);
        scene.Compositor.CopyDirtyTiles(sink);
        Assert.False(scene.Compositor.HasChangesToCopy);

        scene.Root.SetVisible(false);
        Assert.True(scene.Compositor.HasChangesToCopy); // the frame changed
        scene.Compositor.CopyDirtyTiles(sink);
        Assert.False(scene.Compositor.HasChangesToCopy);

        scene.Manager.DeleteElement(scene.Root);
        scene.Compositor.Composite(long.MaxValue);
        Assert.True(scene.Compositor.HasChangesToCopy); // a removed surface the sink has to hear of
        scene.Compositor.CopyDirtyTiles(sink);
        Assert.False(scene.Compositor.HasChangesToCopy);
    }

    [Fact]
    public void Compositing_is_time_sliced_by_whole_surfaces_and_deterministic()
    {
        byte[] Run()
        {
            var ticks = new ManualTicks { Step = 10 };
            var scene = Scene(
                [
                    UiDescs.Element(1, F, 0, 0, 40, 40, media: [Image(1)]),
                    UiDescs.Element(2, F, 0, 0, 40, 40, media: [Image(2)]),
                ],
                ticks.Read,
                roots: [1, 2]);

            // The deadline is already past after the first read: one surface per call.
            Assert.False(scene.Compositor.Composite(-1));
            var sink = new RecordingTileSink();
            scene.Compositor.CopyDirtyTiles(sink);
            Assert.Single(sink.Tiles);
            Assert.True(scene.Compositor.Composite(long.MaxValue));
            scene.Compositor.CopyDirtyTiles(sink);
            Assert.Equal(2, sink.Tiles.Count);
            return [.. sink.Tiles.SelectMany(t => t.Pixels)];
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void A_surface_whose_images_are_decoding_waits_whole()
    {
        var scene = Scene(UiDescs.Element(1, F, 0, 0, 40, 40, media: [Image(1)], children: [UiDescs.Element(2, F, 0, 0, 10, 10, media: [Image(2)])]));
        scene.Images.Pending.Add(0x06000002);

        Assert.False(scene.Compositor.Composite(long.MaxValue));
        Assert.Equal(0u, scene.Surface.GetArgb(20, 20));

        scene.Images.Pending.Clear();

        Assert.True(scene.Compositor.Composite(long.MaxValue));
        Assert.Equal(Red, scene.Surface.GetArgb(20, 20));
        Assert.Equal(Green, scene.Surface.GetArgb(5, 5));
    }

    [Fact]
    public void A_paint_that_meets_an_image_still_decoding_hands_nothing_over_and_is_painted_again()
    {
        // The region's Draw resolves an image of its own choosing, which the pre-check (the region's image) cannot see.
        var images = new FakeImageSource();
        images.Images[0x06000002] = FakeImageSource.Solid(0x06000002, 8, 8, Green);
        images.Pending.Add(0x06000002);
        var compositor = new UiCompositor(images);
        var view = new DrawsImageView(0x06000002, new UiBox(0, 0, 19, 19));
        compositor.AddRoot(view);
        var sink = new RecordingTileSink();

        Assert.False(compositor.Composite(long.MaxValue));
        Assert.True(compositor.Painter.HadPendingImage);
        Assert.True(compositor.HasPendingWork);
        compositor.CopyDirtyTiles(sink);
        Assert.Empty(sink.Tiles);

        images.Pending.Clear();

        Assert.True(compositor.Composite(long.MaxValue));
        Assert.Equal(Green, compositor.GetSurface(view)!.GetArgb(5, 5));
        compositor.CopyDirtyTiles(sink);
        Assert.Single(sink.Tiles);
    }

    [Fact]
    public void A_nested_surface_is_composited_on_its_own_and_skipped_by_its_parent()
    {
        var scene = Scene(UiDescs.Element(1, F, 0, 0, 100, 100, media: [Image(1)], children:
        [
            UiDescs.Element(2, F, 10, 10, 20, 20, properties: [new UiEnumProperty(UiPropertyIds.ElementObjectMode, 3)], media: [Image(2)]),
        ]));

        scene.Composite();

        Assert.Equal(2, scene.Compositor.RootCount);
        Assert.Equal(Red, scene.Surface.GetArgb(15, 15));
        var nested = scene.Compositor.GetSurface(scene.Root.GetChild(2)!)!;
        Assert.Equal(Green, nested.GetArgb(0, 0));
    }

    private static UiImageMedia Image(int n) => new((uint)(0x06000000 + n), UiDrawMode.Normal);

    private static TestScene Scene(UiElementDesc desc) => Scene([desc], null, [desc.ElementId]);

    private static TestScene Scene(UiElementDesc[] descs, Func<long>? timestamp, uint[] roots)
    {
        var images = new FakeImageSource();
        images.Images[0x06000001] = FakeImageSource.Solid(0x06000001, 8, 8, Red);
        images.Images[0x06000002] = FakeImageSource.Solid(0x06000002, 8, 8, Green);
        images.Images[0x06000003] = FakeImageSource.Solid(0x06000003, 8, 8, Blue);
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([UiDescs.Layout(LayoutId, 800, 600, descs)]), Images = images });
        var compositor = new UiCompositor(images, timestamp: timestamp);
        compositor.Attach(manager);
        UiElement? first = null;
        foreach (var id in roots)
        {
            var root = manager.CreateRootElementByDataId(LayoutId, id);
            first ??= root;
        }

        return new TestScene(manager, compositor, images, first!);
    }

    /// <summary>A root with no image of its own whose Draw draws image <paramref name="did"/> over its box.</summary>
    private sealed class DrawsImageView(uint did, UiBox box) : IUiRegionView
    {
        public uint ElementId => 1;

        public UiBox Box => box;

        public UiBox ScreenBox => box;

        public bool IsVisible => true;

        public bool IsShown => true;

        public UiImageRef Image => UiImageRef.None;

        public float AlphaBlendMod => 1f;

        public (int X, int Y) TilingOffset => (0, 0);

        public uint AlphaImageDid => 0;

        public bool ShouldEraseBackground => false;

        public bool DrawAfterChildren => false;

        public bool OwnsSurface => true;

        public float SurfaceAlpha => 1f;

        public ReadOnlySpan<IUiRegionView> ChildrenInDrawOrder => [];

        public uint ViewportElementId => 0;

        public void Draw(IUiPainter painter, UiBox self, UiBox clip, ReadOnlySpan<UiBox> dirty)
        {
            foreach (var piece in dirty)
            {
                painter.DrawImage(new UiImageRef(did, UiBlitMode.SrcCopy), 1f, self, piece, (0, 0));
            }
        }
    }

    private sealed record TestScene(UiElementManager Manager, UiCompositor Compositor, FakeImageSource Images, UiElement Root)
    {
        public UiSurface Surface => Compositor.GetSurface(Root)!;

        public UiSurface Composite()
        {
            Assert.True(Compositor.Composite(long.MaxValue));
            return Surface;
        }
    }
}

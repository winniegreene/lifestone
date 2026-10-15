using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Core;

/// <summary>Hit testing (MouseHitTester&lt;MouseOverCriteria&gt;::Recurse): front to back, mouse-visible or click-blocking elements answer.</summary>
public sealed class UiHitTesterTests
{
    private const uint LayoutId = 0x21000001;
    private const uint F = UiClassIds.Field;

    [Fact]
    public void The_frontmost_mouse_visible_element_under_the_point_answers()
    {
        // B (z 1) is behind C (z 0); both cover (20,20).
        var (manager, root) = Create(UiDescs.Element(1, F, 0, 0, 800, 600, children:
        [
            UiDescs.Element(2, F, 10, 10, 100, 100, zLevel: 1),
            UiDescs.Element(3, F, 15, 15, 100, 100, zLevel: 0),
        ]));
        root.GetChild(2)!.SetMouseVisible(true);
        root.GetChild(3)!.SetMouseVisible(true);

        Assert.Same(root.GetChild(3), manager.HitTest(20, 20));
        Assert.Same(root.GetChild(2), manager.HitTest(12, 12));
        Assert.Null(manager.HitTest(500, 500));
    }

    [Fact]
    public void An_element_that_is_not_mouse_visible_lets_the_point_through_unless_it_blocks_clicks()
    {
        var (manager, root) = Create(UiDescs.Element(1, F, 0, 0, 800, 600, children:
        [
            UiDescs.Element(2, F, 10, 10, 100, 100, zLevel: 1),
            UiDescs.Element(3, F, 10, 10, 100, 100, zLevel: 0),
        ]));
        root.GetChild(2)!.SetMouseVisible(true);

        Assert.Same(root.GetChild(2), manager.HitTest(50, 50));

        root.GetChild(3)!.SetProperty(new UiBoolProperty(UiPropertyIds.ElementBlocksClicks, true));

        Assert.Same(root.GetChild(3), manager.HitTest(50, 50));
    }

    [Fact]
    public void A_hidden_element_hides_its_subtree_from_the_mouse()
    {
        var (manager, root) = Create(UiDescs.Element(1, F, 0, 0, 800, 600, children:
        [
            UiDescs.Element(2, F, 10, 10, 100, 100, children: [UiDescs.Element(3, F, 0, 0, 50, 50)]),
        ]));
        root.GetChild(2)!.GetChild(3)!.SetMouseVisible(true);
        Assert.Same(root.GetChild(2)!.GetChild(3), manager.HitTest(20, 20));

        root.GetChild(2)!.SetVisible(false);

        Assert.Null(manager.HitTest(20, 20));
    }

    [Fact]
    public void A_hit_test_mask_lets_clicks_through_where_its_pixels_are_not_black()
    {
        var images = new FakeImageSource();
        var mask = FakeImageSource.Solid(0x06000099, 10, 10, 0xFF000000);
        mask.Pixels[((0 * 10) + 5) * 4] = 255; // (5,0) is red: not clickable
        images.Images[mask.Did] = mask;
        var (manager, root) = Create(
            UiDescs.Element(1, F, 0, 0, 800, 600, children: [UiDescs.Element(2, F, 100, 100, 10, 10, media: [new UiAlphaMedia(0x06000099)])]),
            images);
        root.GetChild(2)!.SetMouseVisible(true);

        Assert.Same(root.GetChild(2), manager.HitTest(104, 100));
        Assert.Null(manager.HitTest(105, 100));
    }

    [Fact]
    public void Hit_testing_honours_a_centred_forced_display()
    {
        var (manager, root) = Create(UiDescs.Element(1, F, 0, 0, 800, 600, children: [UiDescs.Element(2, F, 0, 0, 10, 10)]), scale: UiScalePolicy.Choose(1920, 1080));
        root.GetChild(2)!.SetMouseVisible(true);
        manager.ForceDisplayResolution(true);

        Assert.Same(root.GetChild(2), manager.HitTest(565, 245));
        Assert.Null(manager.HitTest(5, 5));
    }

    private static (UiElementManager Manager, UiElement Root) Create(UiElementDesc desc, FakeImageSource? images = null, UiScale? scale = null)
    {
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([UiDescs.Layout(LayoutId, 800, 600, desc)]), Images = images }, scale);
        return (manager, manager.CreateRootElementByDataId(LayoutId, desc.ElementId)!);
    }
}

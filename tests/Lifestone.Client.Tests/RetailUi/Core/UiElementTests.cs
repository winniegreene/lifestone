using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Core;

/// <summary>
/// The element runtime (m2-plan.md R10): creation, draw order (UIRegion::AddChild with UIElement::CompareZLevel), front
/// moves, states (UIElement::SetState), the base attributes (UIElement::OnSetAttribute), and dirty rectangles.
/// </summary>
public sealed class UiElementTests
{
    private const uint LayoutId = 0x21000001;
    private const uint F = UiClassIds.Field;

    [Fact]
    public void Children_run_from_the_highest_z_level_to_the_lowest_then_by_read_order_whatever_the_creation_order()
    {
        var root = UiDescs.Element(1, F, 0, 0, 800, 600, children:
        [
            UiDescs.Element(0xD, F, 0, 0, 10, 10, zLevel: 0, readOrder: 5),
            UiDescs.Element(0xB, F, 0, 0, 10, 10, zLevel: 1, readOrder: 0),
            UiDescs.Element(0xA, F, 0, 0, 10, 10, zLevel: 2, readOrder: 0),
            UiDescs.Element(0xC, F, 0, 0, 10, 10, zLevel: 1, readOrder: 1),
            UiDescs.Element(0xE, F, 0, 0, 10, 10, zLevel: 2, readOrder: 3),
        ]);
        var element = Create(root);

        Assert.Equal([0xAu, 0xEu, 0xBu, 0xCu, 0xDu], element.Children.Select(c => c.ElementId));
        Assert.Equal([0xAu, 0xEu, 0xBu, 0xCu, 0xDu], ToArray(element.ChildrenInDrawOrder).Select(c => c.ElementId));
    }

    [Fact]
    public void Bring_to_front_moves_a_child_to_the_front_of_its_z_level_only()
    {
        var root = UiDescs.Element(1, F, 0, 0, 800, 600, children:
        [
            UiDescs.Element(0xA, F, 0, 0, 10, 10, zLevel: 2, readOrder: 0),
            UiDescs.Element(0xE, F, 0, 0, 10, 10, zLevel: 2, readOrder: 3),
            UiDescs.Element(0xB, F, 0, 0, 10, 10, zLevel: 1, readOrder: 0),
            UiDescs.Element(0xD, F, 0, 0, 10, 10, zLevel: 0, readOrder: 0),
        ]);
        var element = Create(root);

        element.BringToFront(element.GetChild(0xA)!);
        Assert.Equal([0xEu, 0xAu, 0xBu, 0xDu], element.Children.Select(c => c.ElementId));

        element.BringToFront(element.GetChild(0xA)!);
        Assert.Equal([0xEu, 0xAu, 0xBu, 0xDu], element.Children.Select(c => c.ElementId));

        element.BringToFront(element.GetChild(0xD)!);
        Assert.Equal([0xEu, 0xAu, 0xBu, 0xDu], element.Children.Select(c => c.ElementId));
    }

    [Fact]
    public void Get_child_recursive_finds_a_direct_child_before_searching_subtrees()
    {
        var root = UiDescs.Element(1, F, 0, 0, 800, 600, children:
        [
            UiDescs.Element(2, F, 0, 0, 10, 10, readOrder: 0, children: [UiDescs.Element(9, F, 1, 1, 2, 2)]),
            UiDescs.Element(9, F, 5, 5, 10, 10, readOrder: 1),
        ]);
        var element = Create(root);

        Assert.Equal(5, element.GetChildRecursive(9)!.Box.X0);
        Assert.Equal(1, element.GetChild(2)!.GetChildRecursive(9)!.Box.X0);
        Assert.Null(element.GetChildRecursive(0x77));
    }

    [Fact]
    public void A_root_owns_a_surface_and_hangs_from_the_managers_root_element()
    {
        var manager = Manager(out _, UiDescs.Element(1, F, 0, 0, 800, 600));
        var root = manager.CreateRootElementByDataId(LayoutId, 1)!;

        Assert.True(root.OwnsSurface);
        Assert.True(root.IsRoot);
        Assert.Same(manager.RootElement, root.Parent);
        Assert.True(root.IsShown);
        Assert.Equal([root], manager.Roots);
        Assert.Null(manager.CreateRootElementByDataId(LayoutId, 0x99));
        Assert.Null(manager.CreateRootElementByDataId(0x21000099, 1));
    }

    [Fact]
    public void Entering_a_state_applies_the_property_difference_and_dropped_properties_reset()
    {
        var desc = UiDescs.Element(1, F, 0, 0, 100, 100,
            properties: [new UiFloatProperty(UiPropertyIds.ElementAlphaBlendMod, 0.5f)],
            states:
            [
                UiDescs.State(5, [new UiFloatProperty(UiPropertyIds.ElementAlphaBlendMod, 0.25f), new UiIntegerProperty(UiPropertyIds.ElementImageTileOffsetX, 3), new UiBoolProperty(UiPropertyIds.ElementHide, true)]),
                UiDescs.State(6),
            ]);
        var element = Create(desc);
        Assert.Equal(0.5f, element.AlphaBlendMod);

        element.SetState(5);
        Assert.Equal(5u, element.State);
        Assert.Equal(0.25f, element.AlphaBlendMod);
        Assert.Equal((3, 0), element.TilingOffset);
        Assert.False(element.Visible);

        element.SetState(6);

        // The opacity goes back to the desc's; the tiling offset's handler ignores a reset (no value), as retail; hide
        // resets to shown.
        Assert.Equal(0.5f, element.AlphaBlendMod);
        Assert.Equal((3, 0), element.TilingOffset);
        Assert.True(element.Visible);
    }

    [Fact]
    public void An_unknown_state_is_the_base_state_and_entering_the_current_state_does_nothing()
    {
        var desc = UiDescs.Element(1, F, 0, 0, 100, 100, states: [UiDescs.State(5, [new UiFloatProperty(UiPropertyIds.ElementAlphaBlendMod, 0.25f)])]);
        var element = Create(desc);

        element.SetState(5);
        element.SetState(0x1234);

        Assert.Equal(0u, element.State);
        Assert.Null(element.CurrentStateDesc);
        Assert.Equal(1f, element.AlphaBlendMod);
        Assert.True(element.SetState(0));
    }

    [Fact]
    public void A_state_keeps_the_media_running_unless_it_has_media_of_its_own()
    {
        var desc = UiDescs.Element(1, F, 0, 0, 100, 100,
            media: [new UiImageMedia(0x06000001, UiDrawMode.Normal)],
            states:
            [
                UiDescs.State(5),
                UiDescs.State(6, media: [new UiImageMedia(0x06000002, UiDrawMode.AlphaBlend)]),
            ]);
        var element = Create(desc);
        Assert.Equal(new UiImageRef(0x06000001, UiBlitMode.SrcCopy), element.Image);

        element.SetState(5);
        Assert.Equal(new UiImageRef(0x06000001, UiBlitMode.SrcCopy), element.Image);

        element.SetState(6);
        Assert.Equal(new UiImageRef(0x06000002, UiBlitMode.FourChannelAlpha), element.Image);

        // Back to the base state: its media restart.
        element.SetState(0);
        Assert.Equal(new UiImageRef(0x06000001, UiBlitMode.SrcCopy), element.Image);
    }

    [Fact]
    public void A_state_that_passes_to_children_sets_theirs_first_and_unknown_ones_fall_to_the_base_state()
    {
        var desc = UiDescs.Element(1, F, 0, 0, 100, 100,
            states: [UiDescs.State(5, passToChildren: true)],
            children:
            [
                UiDescs.Element(2, F, 0, 0, 10, 10, readOrder: 0, states: [UiDescs.State(5, [new UiFloatProperty(UiPropertyIds.ElementAlphaBlendMod, 0.75f)])]),
                UiDescs.Element(3, F, 0, 0, 10, 10, readOrder: 1, states: [UiDescs.State(6)]),
            ]);
        var element = Create(desc);
        element.GetChild(3)!.SetState(6);

        element.SetState(5);

        Assert.Equal(5u, element.GetChild(2)!.State);
        Assert.Equal(0.75f, element.GetChild(2)!.AlphaBlendMod);
        Assert.Equal(0u, element.GetChild(3)!.State);
    }

    [Fact]
    public void Initialization_enters_the_default_state_then_applies_every_desc_property()
    {
        // Retail order (UIElement::Initialize): SetState(default), then the desc's properties; so a property both set ends
        // with the desc's value when its handler takes the value it is given.
        var desc = UiDescs.Element(1, F, 0, 0, 100, 100,
            properties: [new UiFloatProperty(UiPropertyIds.ElementAlphaBlendMod, 0.5f)],
            states: [UiDescs.State(5, [new UiFloatProperty(UiPropertyIds.ElementAlphaBlendMod, 0.25f), new UiIntegerProperty(UiPropertyIds.ElementImageTileOffsetY, 7)])],
            defaultState: 5);
        var element = Create(desc);

        Assert.Equal(5u, element.State);
        Assert.Equal(0.5f, element.AlphaBlendMod);
        Assert.Equal((0, 7), element.TilingOffset);
    }

    [Fact]
    public void The_base_attributes_set_their_fields()
    {
        var desc = UiDescs.Element(1, F, 0, 0, 100, 100, properties:
        [
            new UiBoolProperty(UiPropertyIds.ElementBlocksClicks, true),
            new UiBoolProperty(UiPropertyIds.ElementShouldEraseBackground, true),
            new UiBoolProperty(UiPropertyIds.ElementDrawAfterChildren, false),
            new UiStructProperty(UiPropertyIds.ElementImageTileOffset, new Dictionary<uint, UiProperty>
            {
                [UiPropertyIds.ElementImageTileOffsetX] = new UiIntegerProperty(UiPropertyIds.ElementImageTileOffsetX, 4),
                [UiPropertyIds.ElementImageTileOffsetY] = new UiIntegerProperty(UiPropertyIds.ElementImageTileOffsetY, 9),
            }),
            new UiFloatProperty(UiPropertyIds.ElementAlphaBlendMod, 1.5f),
            new UiBoolProperty(UiPropertyIds.ElementContextMenu, true),
        ]);
        var element = Create(desc);

        Assert.True(element.BlocksClicks);
        Assert.True(element.ShouldEraseBackground);
        Assert.True(element.DrawAfterChildren); // retail sets the flag whatever the value
        Assert.Equal((4, 9), element.TilingOffset);
        Assert.Equal(1f, element.AlphaBlendMod); // clamped
        Assert.True(element.IsMouseVisible); // a context menu makes it mouse-visible
    }

    [Fact]
    public void Instance_properties_override_states_and_the_desc()
    {
        var desc = UiDescs.Element(1, F, 0, 0, 100, 100,
            properties: [new UiIntegerProperty(UiPropertyIds.ElementMaxWidth, 50)],
            states: [UiDescs.State(5, [new UiIntegerProperty(UiPropertyIds.ElementMaxWidth, 40)])]);
        var element = Create(desc);
        Assert.True(element.TryGetIntegerProperty(UiPropertyIds.ElementMaxWidth, out var max) && max == 50);

        element.SetState(5);
        Assert.True(element.TryGetIntegerProperty(UiPropertyIds.ElementMaxWidth, out max) && max == 40);

        element.SetProperty(new UiIntegerProperty(UiPropertyIds.ElementMaxWidth, 30));
        Assert.True(element.TryGetIntegerProperty(UiPropertyIds.ElementMaxWidth, out max) && max == 30);

        element.ResizeTo(90, 20);
        Assert.Equal(30, element.Box.Width);
    }

    [Fact]
    public void Dirty_rectangles_are_clipped_to_the_element_and_its_ancestors_and_reach_the_surface_owner()
    {
        var desc = UiDescs.Element(1, F, 0, 0, 200, 100, children:
        [
            UiDescs.Element(2, F, 150, 50, 100, 100, children: [UiDescs.Element(3, F, 10, 10, 30, 30)]),
        ]);
        var manager = Manager(out var sink, desc);
        var root = manager.CreateRootElementByDataId(LayoutId, 1)!;
        sink.Rects.Clear();

        root.GetChild(2)!.GetChild(3)!.MakeRootDirtyHere();
        root.GetChild(2)!.SetImageByDid(0x06000001, UiDrawMode.Normal);

        Assert.Equal(
        [
            (root, new UiBox(160, 60, 189, 89)),
            (root, new UiBox(150, 50, 199, 99)),
        ], sink.Rects);
    }

    [Fact]
    public void Hiding_an_element_dirties_its_area_and_hides_its_subtree()
    {
        var desc = UiDescs.Element(1, F, 0, 0, 200, 100, children: [UiDescs.Element(2, F, 10, 10, 50, 50, children: [UiDescs.Element(3, F, 0, 0, 5, 5)])]);
        var manager = Manager(out var sink, desc);
        var root = manager.CreateRootElementByDataId(LayoutId, 1)!;
        sink.Rects.Clear();
        var child = root.GetChild(2)!;

        child.SetVisible(false);

        Assert.False(child.GetChild(3)!.IsShown);
        Assert.True(child.GetChild(3)!.Visible);
        Assert.Equal([(root, new UiBox(10, 10, 59, 59))], sink.Rects);
    }

    private static UiElement Create(UiElementDesc desc)
    {
        var manager = Manager(out _, desc);
        return manager.CreateRootElementByDataId(LayoutId, desc.ElementId)!;
    }

    private static UiElementManager Manager(out RecordingDirtySink sink, params UiElementDesc[] elements)
    {
        sink = new RecordingDirtySink();
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([UiDescs.Layout(LayoutId, 800, 600, elements)]) })
        {
            DirtySink = sink,
        };
        return manager;
    }

    private static IUiRegionView[] ToArray(ReadOnlySpan<IUiRegionView> views) => views.ToArray();

    private sealed class RecordingDirtySink : IUiDirtySink
    {
        public List<(IUiRegionView Owner, UiBox Rect)> Rects { get; } = [];

        public void AddDirtyRect(IUiRegionView surfaceOwner, UiBox rect) => Rects.Add((surfaceOwner, rect));
    }
}

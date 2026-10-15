using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Core;

/// <summary>
/// What deleting leaves behind and what delivering keeps (the review of R10-R15): a menu's popup root goes with the menu
/// (<c>UIElement_Menu::~UIElement_Menu</c>), deleting twice does nothing, listener marks last one delivery, a nested
/// global broadcast does not cut the outer one short, the guessed resize borders say so, a forced display maps window
/// coordinates, and retail's A8 hit test is behind its glitch flag.
/// </summary>
public sealed class UiLifecycleTests
{
    private const uint F = UiClassIds.Field;
    private const uint PopupLayout = 0x21000003;

    [Fact]
    public void Deleting_a_screen_with_a_menu_deletes_the_menus_popup_root_and_closes_it_first()
    {
        var scene = MenuScene();
        var menu = scene.Get<UiMenu>(0x50);
        var popup = menu.Popup!;
        menu.InsertTextItem(UiStringRef.FromLiteral("One"), 0);
        menu.Open();
        Assert.True(popup.IsShown);
        Assert.Equal(2, scene.Manager.Roots.Count);

        scene.Manager.DeleteElement(scene.Root);

        Assert.True(popup.IsDeleted);
        Assert.Empty(scene.Manager.Roots);
        Assert.Empty(scene.Manager.RootElement.Children);
        Assert.DoesNotContain(scene.Manager.Elements, e => ReferenceEquals(e, popup) || ReferenceEquals(e.GetWindowRoot(), popup));
        Assert.Equal(1, scene.Count(0x50, UiMessageIds.MenuClosed));
        Assert.Null(menu.Popup);
    }

    [Fact]
    public void Deleting_an_element_twice_does_nothing_the_second_time()
    {
        var host = new RecordingViewportHost();
        var scene = new UiScene([UiDescs.Element(0x91, UiClassIds.Viewport, 10, 20, 300, 200)], viewportHost: host);
        var viewport = scene.Get<UiViewport>(0x91);

        scene.Manager.DeleteElement(viewport);
        scene.Manager.DeleteElement(viewport);

        Assert.True(viewport.IsDeleted);
        Assert.Single(host.Binds, b => !b.Visible);
    }

    [Fact]
    public void Listener_marks_last_one_delivery_so_deleted_listeners_are_not_kept()
    {
        var scene = new UiScene([UiDescs.Element(2, F, 0, 0, 10, 10)]);
        var listener = new CountingListener();
        var element = scene.Root.GetChild(2)!;
        element.RegisterForElementMessages(listener);

        element.BroadcastElementMessage(UiMessageIds.ButtonClick);
        element.BroadcastElementMessage(UiMessageIds.ButtonClick);

        Assert.Equal(2, listener.Heard);
        Assert.Equal(0, scene.Manager.HeardMarkCount);
    }

    [Fact]
    public void A_global_broadcast_inside_another_does_not_cut_the_outer_one_short()
    {
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([]) });
        const uint Inner = 0x77;
        var d = new GlobalRecorder();
        var e = new GlobalRecorder();
        var b = new GlobalRecorder();
        var c = new GlobalRecorder();
        var a = new GlobalRecorder(() => manager.BroadcastGlobalMessage(Inner, 5));
        manager.RegisterForGlobalMessage(a, UiGlobalMessageIds.Frame);
        manager.RegisterForGlobalMessage(b, UiGlobalMessageIds.Frame);
        manager.RegisterForGlobalMessage(c, UiGlobalMessageIds.Frame);
        manager.RegisterForGlobalMessage(d, Inner);
        manager.RegisterForGlobalMessage(e, Inner);

        manager.Update(1);

        Assert.Equal([(UiGlobalMessageIds.Frame, 0)], a.Heard);
        Assert.Equal([(UiGlobalMessageIds.Frame, 0)], b.Heard);
        Assert.Equal([(UiGlobalMessageIds.Frame, 0)], c.Heard);
        Assert.Equal([(Inner, 5)], d.Heard);
        Assert.Equal([(Inner, 5)], e.Heard);
    }

    [Fact]
    public void Resizing_from_the_right_border_follows_the_mouse_and_says_the_rule_is_a_guess()
    {
        var scene = new UiScene([UiDescs.Element(2, F, 100, 100, 200, 100)]);
        var window = scene.Root.GetChild(2)!;

        window.StartResizing(UiBorder.Left, 100, 150);
        window.MouseMove(90, 150);
        window.StopResizing();
        Assert.Equal((90, 210), (window.Box.X0, window.Box.Width));
        Assert.DoesNotContain(scene.Log.Lines, l => l.Contains("top or right border", StringComparison.Ordinal));

        window.StartResizing(UiBorder.Right, 300, 150);
        window.MouseMove(320, 150);
        window.StopResizing();

        Assert.Equal((90, 230), (window.Box.X0, window.Box.Width));
        Assert.Single(scene.Log.Lines, l => l.Contains("TODO(retailui-runtime): a window is resized from its top or right border", StringComparison.Ordinal));
    }

    [Fact]
    public void A_forced_display_in_a_1920x1080_window_takes_the_mouse_and_reports_viewports_in_window_coordinates()
    {
        var host = new RecordingViewportHost();
        var scene = new UiScene(
        [
            UiDescs.Element(0x20, UiClassIds.Button, 100, 100, 50, 20),
            UiDescs.Element(0x91, UiClassIds.Viewport, 300, 200, 100, 80),
        ], viewportHost: host);
        scene.Manager.SetScale(UiScalePolicy.Choose(1920, 1080));
        scene.Manager.ForceDisplayResolution(true);
        scene.Frame();
        var button = scene.Get<UiButton>(0x20);

        Assert.Equal((560, 240), scene.Manager.DisplayOrigin);
        Assert.Equal(new UiBox(660, 340, 709, 359), button.ScreenBox);
        scene.Input.MouseMove(665, 345);
        Assert.Same(button, scene.Input.ElementLastEntered);
        scene.Input.MouseMove(105, 105);
        Assert.Null(scene.Input.ElementLastOver);
        Assert.Equal((0x91u, new UiBox(860, 440, 959, 519), true), host.Binds[^1]);
    }

    [Fact]
    public void An_A8_mask_never_tests_true_unless_retails_glitch_is_restored()
    {
        // A 4x2 A8 mask: only pixel (2, 0) has alpha.
        var rgba = new byte[4 * 2 * 4];
        rgba[(2 * 4) + 3] = 255;
        var mask = new UiImage(0x06000005, 4, 2, UiPixelFormat.A8, rgba);

        Assert.All(Enumerable.Range(0, 8), i => Assert.False(mask.PointTestOverlay(i % 4, i / 4)));

        // Retail reads the dword at the pixel's byte and masks off the low (alpha) byte: the next three bytes decide.
        Assert.True(mask.PointTestOverlay(0, 0, retail: true));
        Assert.True(mask.PointTestOverlay(1, 0, retail: true));
        Assert.False(mask.PointTestOverlay(2, 0, retail: true));
        Assert.False(mask.PointTestOverlay(3, 0, retail: true));
        Assert.False(mask.PointTestOverlay(3, 1, retail: true));
        Assert.False(mask.PointTestOverlay(4, 0, retail: true));
    }

    [Fact]
    public void An_element_hit_tests_its_A8_mask_the_retail_way_only_with_the_glitch_flag()
    {
        var rgba = new byte[4 * 1 * 4];
        rgba[(1 * 4) + 3] = 255;
        var images = new FakeImageSource();
        images.Images[0x06000005] = new UiImage(0x06000005, 4, 1, UiPixelFormat.A8, rgba);
        var desc = UiDescs.Element(1, F, 0, 0, 800, 600, children: [UiDescs.Element(2, F, 10, 10, 4, 1)]);
        UiElement Element(UiRetailGlitches glitches)
        {
            var manager = new UiElementManager(new UiServices
            {
                Descs = new FakeDescSource([UiDescs.Layout(UiScene.LayoutId, 800, 600, desc)]),
                Images = images,
                RetailGlitches = glitches,
            });
            var element = manager.CreateRootElementByDataId(UiScene.LayoutId, 1)!.GetChild(2)!;
            element.SetAlphaImageByDid(0x06000005);
            return element;
        }

        // A mask pixel that tests true is not over the element: with the fix every pixel is over it.
        Assert.True(Element(UiRetailGlitches.None).PointIsOverRegion(10, 10));
        Assert.False(Element(UiRetailGlitches.A8PointTest).PointIsOverRegion(10, 10));
        Assert.True(Element(UiRetailGlitches.A8PointTest).PointIsOverRegion(11, 10));
    }

    private static UiScene MenuScene()
    {
        var popup = UiDescs.Element(0x60, F, 0, 0, 100, 60, layout: PopupLayout, children:
        [
            UiDescs.Element(0x61, UiClassIds.ListBox, 0, 0, 100, 60, layout: PopupLayout),
        ]);
        var item = UiDescs.Element(0x62, UiClassIds.Text, 0, 0, 100, 20, layout: PopupLayout);
        var menu = UiDescs.Element(0x50, UiClassIds.Menu, 10, 10, 100, 20, properties:
        [
            new UiDataIdProperty(UiPropertyIds.MenuPopupLayout, PopupLayout),
            new UiDataIdProperty(UiPropertyIds.MenuTextItemLayout, PopupLayout),
            new UiEnumProperty(UiPropertyIds.MenuPopup, 0x60),
            new UiEnumProperty(UiPropertyIds.MenuListbox, 0x61),
            new UiEnumProperty(UiPropertyIds.MenuTextItem, 0x62),
        ]);
        return new UiScene([menu], extraLayouts: [UiDescs.Layout(PopupLayout, 800, 600, popup, item)]);
    }

    private sealed class CountingListener : IUiElementListener
    {
        public int Heard { get; private set; }

        public UiListenResult ListenToElementMessage(in UiElementMessage message)
        {
            Heard++;
            return UiListenResult.Default;
        }
    }

    private sealed class GlobalRecorder(Action? onHear = null) : IUiGlobalListener
    {
        public List<(uint Message, int Data)> Heard { get; } = [];

        public void ListenToGlobalMessage(uint messageId, int data)
        {
            Heard.Add((messageId, data));
            onHear?.Invoke();
        }
    }

    private sealed class RecordingViewportHost : IUiViewportHost
    {
        public List<(uint Id, UiBox Box, bool Visible)> Binds { get; } = [];

        public void Bind(uint viewportElementId, UiBox screenBox, bool visible) => Binds.Add((viewportElementId, screenBox, visible));
    }
}

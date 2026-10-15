using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Widgets;

/// <summary>
/// The engine widgets (m2-plan.md R13): the button's states and clicks, the meter's levels, the panel's tabs, the scroll
/// bar's widget arithmetic and requests, the list box and the menu, the group box, the drag bar, the viewport's binding and
/// the browser stub.
/// </summary>
public sealed class UiWidgetTests
{
    private const uint F = UiClassIds.Field;

    private static readonly UiStateDesc[] ButtonStates =
    [
        UiDescs.State(UiStateIds.Normal), UiDescs.State(UiStateIds.NormalRollover), UiDescs.State(UiStateIds.NormalPressed),
        UiDescs.State(UiStateIds.Highlight), UiDescs.State(UiStateIds.HighlightRollover), UiDescs.State(UiStateIds.HighlightPressed),
        UiDescs.State(UiStateIds.Ghosted),
    ];

    // ---------------------------------------------------------------- Button

    [Fact]
    public void A_rollover_button_s_state_follows_the_mouse_and_its_properties()
    {
        var scene = new UiScene([Button(3, 10, 10, new UiBoolProperty(UiPropertyIds.ButtonRollover, true))]);
        var button = scene.Get<UiButton>(3);
        Assert.Equal(UiStateIds.Normal, button.State);

        scene.Input.MouseMove(20, 20);
        Assert.Equal(UiStateIds.NormalRollover, button.State);
        scene.Input.MouseButton(UiMouseButton.Left, down: true);
        Assert.Equal(UiStateIds.NormalPressed, button.State);
        scene.Input.MouseMove(400, 400);
        Assert.Equal(UiStateIds.NormalRollover, button.State);
        scene.Input.MouseButton(UiMouseButton.Left, down: false);
        Assert.Equal(UiStateIds.Normal, button.State);

        button.SetBoolProperty(UiPropertyIds.ButtonHighlighted, true);
        Assert.Equal(UiStateIds.Highlight, button.State);
        scene.Input.MouseMove(20, 20);
        Assert.Equal(UiStateIds.HighlightRollover, button.State);
        scene.Input.MouseButton(UiMouseButton.Left, down: true);
        Assert.Equal(UiStateIds.HighlightPressed, button.State);
        scene.Input.MouseButton(UiMouseButton.Left, down: false);

        button.SetBoolProperty(UiPropertyIds.ButtonGhosted, true);
        Assert.Equal(UiStateIds.Ghosted, button.State);
        Assert.False(button.IsMouseVisible);
        button.SetState(UiStateIds.Normal);
        Assert.False(button.Ghosted);
        Assert.True(button.IsMouseVisible);
    }

    [Fact]
    public void A_button_without_rollover_stays_normal_under_the_mouse_and_a_ghosted_one_does_not_click()
    {
        // A button ghosted by its desc also defaults to the ghosted state: Initialize's SetState(default) runs the
        // button's SetState, which clears the ghosting for any other state (as retail).
        var ghosted = UiDescs.Element(4, UiClassIds.Button, 100, 10, 50, 20, properties: [new UiBoolProperty(UiPropertyIds.ButtonGhosted, true)], states: ButtonStates, defaultState: UiStateIds.Ghosted);
        var scene = new UiScene([Button(3, 10, 10), ghosted]);

        scene.Input.MouseMove(20, 20);
        Assert.Equal(UiStateIds.Normal, scene.Get<UiButton>(3).State);
        scene.Click(110, 20);

        Assert.Equal(0, scene.Count(4, UiMessageIds.ButtonClick));
        Assert.Equal(UiStateIds.Ghosted, scene.Get<UiButton>(4).State);
    }

    [Fact]
    public void A_ghosted_button_never_clicks_on_release_and_property_0x0C_only_lets_a_hot_one_send_its_hot_click()
    {
        var broadcast = new UiBoolProperty(UiPropertyIds.ButtonBroadcastWhenGhosted, true);
        var scene = new UiScene([Button(3, 10, 10, broadcast), Button(4, 100, 10, broadcast, new UiBoolProperty(UiPropertyIds.ButtonHotButton, true))]);

        foreach (var id in new uint[] { 3, 4 })
        {
            var button = scene.Get<UiButton>(id);
            button.SetBoolProperty(UiPropertyIds.ButtonGhosted, true);

            // A ghosted button is not mouse-visible, so the press comes to it directly, with the mouse over it.
            button.MouseOverTop(true);
            button.MouseDown(5, 5, UiMouseActions.Left);
            button.MouseUp(5, 5, UiMouseActions.Left);
        }

        Assert.Equal((0, 0), (scene.Count(3, UiMessageIds.ButtonClick), scene.Count(4, UiMessageIds.ButtonClick)));
        Assert.Equal((0, 1), (scene.Count(3, UiMessageIds.ButtonHotClick), scene.Count(4, UiMessageIds.ButtonHotClick)));
    }

    [Fact]
    public void A_toggle_button_flips_its_highlight_on_each_click()
    {
        var scene = new UiScene([Button(3, 10, 10, new UiBoolProperty(UiPropertyIds.ButtonBooleanButton, true))]);
        var button = scene.Get<UiButton>(3);

        scene.Click(20, 20);
        Assert.True(button.Highlighted);
        scene.Click(20, 20);
        Assert.False(button.Highlighted);

        Assert.Equal(2, scene.Count(3, UiMessageIds.ButtonClick));
    }

    [Fact]
    public void A_hot_button_repeats_after_the_first_interval_then_at_the_interval_while_held_and_sends_no_click()
    {
        var scene = new UiScene([Button(3, 10, 10,
            new UiBoolProperty(UiPropertyIds.ButtonHotButton, true),
            new UiFloatProperty(UiPropertyIds.ButtonHotClickFirstInterval, 0.5f),
            new UiFloatProperty(UiPropertyIds.ButtonHotClickInterval, 0.1f))]);

        scene.Input.MouseMove(20, 20);
        scene.Input.MouseButton(UiMouseButton.Left, down: true);
        Assert.Equal(1, scene.Count(3, UiMessageIds.ButtonHotClick));
        scene.Frame(0.4);
        Assert.Equal(1, scene.Count(3, UiMessageIds.ButtonHotClick));
        scene.Frame(0.15);
        Assert.Equal(2, scene.Count(3, UiMessageIds.ButtonHotClick));
        scene.Frame(0.1);
        Assert.Equal(3, scene.Count(3, UiMessageIds.ButtonHotClick));
        scene.Input.MouseButton(UiMouseButton.Left, down: false);
        scene.Frame(0.5);

        Assert.Equal(3, scene.Count(3, UiMessageIds.ButtonHotClick));
        Assert.Equal(0, scene.Count(3, UiMessageIds.ButtonClick));
        Assert.False(scene.Get<UiButton>(3).IsHotClicking);
    }

    [Fact]
    public void A_button_with_an_input_action_fires_it_on_click()
    {
        var scene = new UiScene([Button(3, 10, 10, new UiEnumProperty(UiPropertyIds.ButtonClickAction, 0x10000023))]);
        var handler = new RecordingActionSink(takes: false);
        scene.Input.AddActionHandler(handler);

        scene.Click(20, 20);

        Assert.Equal(["0:10000023:start"], handler.Lines.Where(l => l.Contains("10000023", StringComparison.Ordinal)));
    }

    // ---------------------------------------------------------------- Meter

    [Theory]
    [InlineData(1u, 0, 0, 49, 9)]
    [InlineData(3u, 50, 0, 99, 9)]
    [InlineData(2u, 0, 0, 99, 4)]
    [InlineData(4u, 0, 5, 99, 9)]
    public void A_meter_draws_its_fill_child_over_the_filled_part_in_its_direction(uint direction, int x0, int y0, int x1, int y1)
    {
        var scene = new UiScene([Meter(0x10, direction)]);
        var meter = scene.Get<UiMeter>(0x10);
        var fill = Assert.IsAssignableFrom<IUiRegionView>(meter.FillChild);

        meter.SetFloatProperty(UiPropertyIds.MeterPosition, 0.5f);

        var self = new UiBox(0, 0, 99, 9);
        Assert.Equal(new UiBox(x0, y0, x1, y1), meter.ClipChild(fill, self, self));
        Assert.Equal(direction, meter.Direction);
    }

    [Fact]
    public void A_smooth_meter_moves_to_its_goal_over_the_duration_and_announces_it()
    {
        var scene = new UiScene([Meter(0x10, 1,
            new UiBoolProperty(UiPropertyIds.MeterSmoothMovement, true),
            new UiFloatProperty(UiPropertyIds.MeterSmoothMovementDuration, 1f))]);
        var meter = scene.Get<UiMeter>(0x10);

        meter.SetGoal(1f);
        Assert.True(meter.IsAnimating);
        scene.Frame(0.5);
        Assert.InRange(meter.Position, 0.49f, 0.51f);
        scene.Frame(0.6);

        Assert.Equal(1f, meter.Position);
        Assert.False(meter.IsAnimating);
        Assert.True(scene.Count(0x10, UiMessageIds.MeterPosition) >= 2);
        Assert.Equal(1, scene.Count(0x10, UiMessageIds.MeterAtGoal));
    }

    [Fact]
    public void A_frame_meter_shows_the_nearest_frame_of_its_array()
    {
        var frames = new UiArrayProperty(UiPropertyIds.MeterFrameArray, Enumerable.Range(0, 5).Select(i => (UiProperty)new UiDataIdProperty(UiPropertyIds.MeterFrame, 0x06000100u + (uint)i)).ToArray());
        var scene = new UiScene([UiDescs.Element(0x10, UiClassIds.Meter, 0, 0, 100, 10, properties: [new UiBoolProperty(UiPropertyIds.MeterFrameMeter, true), frames])]);
        var meter = scene.Get<UiMeter>(0x10);

        meter.SetFloatProperty(UiPropertyIds.MeterPosition, 0.5f);
        Assert.Equal(2, meter.CurrentFrame);
        meter.SetFloatProperty(UiPropertyIds.MeterPosition, 0.9f);
        Assert.Equal(4, meter.CurrentFrame);
        Assert.Null(meter.FillChild);
    }

    // ---------------------------------------------------------------- Panel

    [Fact]
    public void A_panel_opens_the_flagged_page_and_a_tab_click_opens_its_page_and_closes_the_others()
    {
        var tabStates = new[] { UiDescs.State(UiStateIds.Closed), UiDescs.State(UiStateIds.Open) };
        var table = new UiArrayProperty(UiPropertyIds.PanelPages,
        [
            Page(0x21, 0x31, open: false),
            Page(0x22, 0x32, open: true),
        ]);
        var scene = new UiScene([UiDescs.Element(0x20, UiClassIds.Panel, 0, 0, 300, 200, properties: [table], children:
        [
            UiDescs.Element(0x21, F, 0, 0, 50, 20, states: tabStates),
            UiDescs.Element(0x22, F, 50, 0, 50, 20, states: tabStates),
            UiDescs.Element(0x31, F, 0, 20, 300, 180),
            UiDescs.Element(0x32, F, 0, 20, 300, 180),
        ])]);
        var panel = scene.Get<UiPanel>(0x20);
        Assert.Equal((0x22u, 0x32u), (panel.OpenTabId, panel.OpenPageId));
        Assert.False(scene.Get<UiElement>(0x31).Visible);
        Assert.True(scene.Get<UiElement>(0x32).Visible);
        Assert.Equal(UiStateIds.Open, scene.Get<UiElement>(0x22).State);
        scene.Messages.Clear();

        scene.Click(10, 10);

        Assert.Equal((0x21u, 0x31u), (panel.OpenTabId, panel.OpenPageId));
        Assert.True(scene.Get<UiElement>(0x31).Visible);
        Assert.False(scene.Get<UiElement>(0x32).Visible);
        Assert.Equal(UiStateIds.Open, scene.Get<UiElement>(0x21).State);
        Assert.Equal(UiStateIds.Closed, scene.Get<UiElement>(0x22).State);
        Assert.Equal(1, scene.Count(0x20, UiMessageIds.PanelTabChanged));
    }

    // ---------------------------------------------------------------- Scroll bar

    [Fact]
    public void A_scroll_bar_places_its_widget_on_the_track_between_its_buttons()
    {
        var scene = new UiScene([Scrollbar(0x30, 100, 0)]);
        var bar = scene.Get<UiScrollbar>(0x30);

        Assert.Equal((16, 184), (bar.ScrollingArea.Y0, bar.ScrollingArea.Y1));
        Assert.Equal((0, 184), (scene.Get<UiButton>(3).Box.X0, scene.Get<UiButton>(3).Box.Y0));
        Assert.Equal(16, bar.Widget!.Box.Y0);
        bar.SetScrollbarPosition(0.5f);
        Assert.Equal(16 + 74, bar.Widget.Box.Y0);
        bar.SetScrollbarPosition(1.5f);
        Assert.Equal(1f, bar.Position);
        Assert.Equal(184, bar.Widget.Box.Y0 + bar.Widget.Box.Height);
        Assert.Contains(scene.Messages, m => m.ElementId == 0x30 && m.MessageId == UiMessageIds.ScrollbarPosition && m.Param1 == 500);
    }

    [Fact]
    public void A_proportional_scroll_bar_sizes_its_widget_to_the_visible_share()
    {
        var scene = new UiScene([Scrollbar(0x30, 100, 0, new UiBoolProperty(UiPropertyIds.ScrollbarProportional, true), new UiFloatProperty(UiPropertyIds.ScrollbarWidgetSize, 0.25f))]);

        Assert.Equal(42, scene.Get<UiScrollbar>(0x30).Widget!.Box.Height);
    }

    [Fact]
    public void Its_buttons_ask_for_lines_the_track_for_pages_and_dragging_the_widget_moves_the_position()
    {
        var scene = new UiScene([Scrollbar(0x30, 100, 0)]);
        var bar = scene.Get<UiScrollbar>(0x30);
        bar.SetScrollbarPosition(0.5f);

        scene.Click(108, 5);
        scene.Click(108, 195);
        Assert.Equal(1, scene.Count(0x30, UiMessageIds.ScrollLineUp));
        Assert.Equal(1, scene.Count(0x30, UiMessageIds.ScrollLineDown));

        scene.Click(108, 30);
        scene.Click(108, 170);
        Assert.Equal(1, scene.Count(0x30, UiMessageIds.ScrollPageUp));
        Assert.Equal(1, scene.Count(0x30, UiMessageIds.ScrollPageDown));

        scene.Input.MouseMove(108, 95);
        scene.Input.MouseButton(UiMouseButton.Left, down: true);
        scene.Input.MouseMove(108, 95 + 37);
        Assert.True(bar.IsDragging);
        scene.Input.MouseButton(UiMouseButton.Left, down: false);

        Assert.Equal(0.75f, bar.Position, 3);
        Assert.False(bar.IsDragging);
    }

    // ---------------------------------------------------------------- List box and scrolling

    [Fact]
    public void A_list_box_stacks_rows_from_its_template_scrolls_by_rows_and_selects_the_clicked_row()
    {
        var scene = ListScene(rows: 5);
        var list = scene.Get<UiListBox>(0x40);
        var bar = scene.Get<UiScrollbar>(0x30);

        Assert.Equal(5, list.Items.Count);
        Assert.Equal([0, 20, 40, 60, 80], list.Items.Select(i => i.Box.Y0));
        Assert.Equal((100, 100), (list.PaperWidth, list.PaperHeight));
        Assert.False(bar.IsDisabled);
        Assert.Equal(0.4f, bar.TryGetFloatProperty(UiPropertyIds.ScrollbarWidgetSize, out var share) ? share : 0f, 3);

        scene.Click(108, 195);
        scene.Frame();
        Assert.Equal(20, list.ScrollY);
        Assert.Equal([-20, 0, 20, 40, 60], list.Items.Select(i => i.Box.Y0));

        // A press on the list itself selects twice, as retail: its MouseDown message (heard by its own
        // ListenToElementMessage) and then its MouseDown; the second is a reselect.
        scene.Click(50, 25);
        Assert.Equal(2, list.SelectedIndex);
        var selection = Assert.Single(scene.Messages, m => m.ElementId == 0x40 && m.MessageId == UiMessageIds.ListBoxSelection);
        Assert.Equal(2u, selection.Param1);
        Assert.Same(list.Items[2], selection.Item);
        Assert.Equal(UiStateIds.Highlight, list.Items[2].State);
        Assert.Equal(1, scene.Count(0x40, UiMessageIds.ListBoxReselect));

        scene.Click(50, 5);
        Assert.Equal(1, list.SelectedIndex);
        Assert.Equal(UiStateIds.Normal, list.Items[2].State);
        Assert.Equal(UiStateIds.Highlight, list.Items[1].State);
    }

    [Fact]
    public void The_wheel_over_a_list_scrolls_it_through_its_scroll_bar_and_a_list_that_fits_disables_the_bar()
    {
        var scene = ListScene(rows: 5);
        var list = scene.Get<UiListBox>(0x40);

        scene.Input.MouseMove(50, 10);
        scene.Input.MouseWheel(-2);
        scene.Frame();
        Assert.Equal(40, list.ScrollY);
        scene.Input.MouseWheel(1);
        scene.Frame();
        Assert.Equal(20, list.ScrollY);

        var short_ = ListScene(rows: 2);
        Assert.True(short_.Get<UiScrollbar>(0x30).IsDisabled);
        Assert.Equal(0, short_.Get<UiListBox>(0x40).ScrollY);
    }

    // ---------------------------------------------------------------- Menu

    [Fact]
    public void A_menu_opens_its_popup_under_it_and_choosing_a_row_shows_it_and_closes_the_popup()
    {
        var popup = UiDescs.Element(0x60, F, 0, 0, 100, 60, layout: 0x21000003, children:
        [
            UiDescs.Element(0x61, UiClassIds.ListBox, 0, 0, 100, 60, layout: 0x21000003, properties: [new UiBoolProperty(UiPropertyIds.ListBoxClickSelect, true)]),
        ]);
        var item = UiDescs.Element(0x62, UiClassIds.Text, 0, 0, 100, 20, layout: 0x21000003);
        var menu = UiDescs.Element(0x50, UiClassIds.Menu, 10, 10, 100, 20, properties:
        [
            new UiDataIdProperty(UiPropertyIds.MenuPopupLayout, 0x21000003),
            new UiDataIdProperty(UiPropertyIds.MenuTextItemLayout, 0x21000003),
            new UiEnumProperty(UiPropertyIds.MenuPopup, 0x60),
            new UiEnumProperty(UiPropertyIds.MenuListbox, 0x61),
            new UiEnumProperty(UiPropertyIds.MenuTextItem, 0x62),
            new UiEnumProperty(UiPropertyIds.MenuSelectionDisplay, 0x63),
        ], children: [UiDescs.Element(0x63, UiClassIds.Text, 0, 0, 100, 20)]);
        var scene = new UiScene([menu], extraLayouts: [UiDescs.Layout(0x21000003, 800, 600, popup, item)]);
        var control = scene.Get<UiMenu>(0x50);
        Assert.NotNull(control.ListBox);

        Assert.NotNull(control.InsertTextItem(UiStringRef.FromLiteral("One"), 0));
        Assert.NotNull(control.InsertTextItem(UiStringRef.FromLiteral("Two"), 1));
        scene.Frame();
        scene.Click(20, 20);
        scene.Frame();

        Assert.True(control.IsOpen);
        Assert.True(control.Popup!.IsShown);
        Assert.Equal((10, 29), (control.Popup.Box.X0, control.Popup.Box.Y0));
        Assert.Equal(1, scene.Count(0x50, UiMessageIds.MenuOpened));

        scene.Click(20, 29 + 25);

        Assert.Equal(1, control.SelectedIndex);
        Assert.False(control.IsOpen);
        Assert.False(control.Popup.IsShown);
        Assert.Equal("Two", scene.Get<UiTextElement>(0x63).Text);
        Assert.Contains(scene.Messages, m => m.ElementId == 0x50 && m.MessageId == UiMessageIds.MenuSelection && ReferenceEquals(m.Item, control.GetItem(1)));
    }

    // ---------------------------------------------------------------- Group box, drag bar, viewport, browser

    [Fact]
    public void A_group_box_keeps_one_button_highlighted()
    {
        var scene = new UiScene([UiDescs.Element(0x70, UiClassIds.GroupBox, 0, 0, 300, 50, properties: [new UiEnumProperty(UiPropertyIds.GroupBoxDefaultButton, 0x71)], children:
        [
            Button(0x71, 0, 0), Button(0x72, 100, 0),
        ])]);
        var group = scene.Get<UiGroupBox>(0x70);
        Assert.Equal(0x71u, group.SelectedButtonId);
        Assert.Equal(UiStateIds.Highlight, scene.Get<UiButton>(0x71).State);

        scene.Click(110, 10);

        Assert.Equal(0x72u, group.SelectedButtonId);
        Assert.Equal(UiStateIds.Highlight, scene.Get<UiButton>(0x72).State);
        Assert.Equal(UiStateIds.Normal, scene.Get<UiButton>(0x71).State);
    }

    [Fact]
    public void A_drag_bar_moves_its_window_with_the_mouse_kept_inside_the_parent()
    {
        var scene = new UiScene([UiDescs.Element(0x80, F, 100, 100, 200, 100, children: [UiDescs.Element(0x81, UiClassIds.Dragbar, 0, 0, 200, 16)])]);
        var window = scene.Get<UiElement>(0x80);

        scene.Input.MouseMove(150, 105);
        scene.Input.MouseButton(UiMouseButton.Left, down: true);
        scene.Input.MouseMove(250, 205);
        Assert.Equal((200, 200), (window.Box.X0, window.Box.Y0));
        scene.Input.MouseMove(900, 900);
        Assert.Equal((600, 500), (window.Box.X0, window.Box.Y0));
        scene.Input.MouseButton(UiMouseButton.Left, down: false);
        scene.Input.MouseMove(100, 100);

        Assert.Equal((600, 500), (window.Box.X0, window.Box.Y0));
        Assert.False(window.IsMoving);
    }

    [Fact]
    public void A_viewport_reports_its_place_to_the_host_when_it_changes_and_goes_when_it_is_deleted()
    {
        var host = new RecordingViewportHost();
        var window = UiDescs.Element(0x90, F, 0, 0, 400, 300, children: [UiDescs.Element(0x91, UiClassIds.Viewport, 10, 20, 300, 200)], layout: 0x21000002);
        var scene = new UiScene([], extraLayouts: [UiDescs.Layout(0x21000002, 800, 600, window)], viewportHost: host);
        var root = scene.Manager.CreateRootElementByDataId(0x21000002, 0x90)!;

        scene.Frame();
        root.MoveTo(5, 5);
        scene.Frame();
        scene.Frame();
        scene.Manager.DeleteElement(root);

        Assert.Equal(
        [
            (0x91u, new UiBox(10, 20, 309, 219), true),
            (0x91u, new UiBox(15, 25, 314, 224), true),
            (0x91u, new UiBox(15, 25, 314, 224), false),
        ], host.Binds);
    }

    [Fact]
    public void A_viewport_without_a_host_and_a_browser_are_loud_placeholders()
    {
        var scene = new UiScene([UiDescs.Element(0x91, UiClassIds.Viewport, 0, 0, 300, 200), UiDescs.Element(0x92, UiClassIds.Browser, 0, 0, 300, 200)]);

        Assert.IsType<UiBrowser>(scene.Get<UiElement>(0x92));
        Assert.Contains(scene.Log.Lines, l => l.Contains("no viewport host is wired", StringComparison.Ordinal));
        Assert.Contains(scene.Log.Lines, l => l.Contains("UIElement_Browser", StringComparison.Ordinal) && l.Contains("placeholder", StringComparison.Ordinal));
    }

    private static UiElementDesc Button(uint id, int x, int y, params UiProperty[] properties) =>
        UiDescs.Element(id, UiClassIds.Button, x, y, 50, 20, properties: properties, states: ButtonStates);

    private static UiElementDesc Meter(uint id, uint direction, params UiProperty[] properties) =>
        UiDescs.Element(id, UiClassIds.Meter, 0, 0, 100, 10, properties: [new UiEnumProperty(UiPropertyIds.MeterChildDirection, direction), .. properties], children:
        [
            UiDescs.Element(2, F, 0, 0, 100, 10),
        ]);

    private static UiStructProperty Page(uint tab, uint page, bool open) => new(UiPropertyIds.PanelPageData, new Dictionary<uint, UiProperty>
    {
        [UiPropertyIds.PanelTabElement] = new UiEnumProperty(UiPropertyIds.PanelTabElement, tab),
        [UiPropertyIds.PanelPageElement] = new UiEnumProperty(UiPropertyIds.PanelPageElement, page),
        [UiPropertyIds.PanelPageOpen] = new UiBoolProperty(UiPropertyIds.PanelPageOpen, open),
    });

    /// <summary>A vertical scroll bar 16×200 at (x, y): widget 1 (16×20), increment 2 and decrement 3 (16×16 buttons).</summary>
    private static UiElementDesc Scrollbar(uint id, int x, int y, params UiProperty[] properties) =>
        UiDescs.Element(id, UiClassIds.Scrollbar, x, y, 16, 200, properties:
        [
            new UiEnumProperty(UiPropertyIds.ScrollbarIncrementButton, 2),
            new UiEnumProperty(UiPropertyIds.ScrollbarDecrementButton, 3),
            new UiFloatProperty(UiPropertyIds.ScrollbarPosition, 0f),
            .. properties,
        ], children:
        [
            UiDescs.Element(1, UiClassIds.Button, 0, 0, 16, 20),
            UiDescs.Element(2, UiClassIds.Button, 0, 0, 16, 16),
            UiDescs.Element(3, UiClassIds.Button, 0, 184, 16, 16),
        ]);

    /// <summary>A list box 0x40 (100×40) with its vertical scroll bar 0x30 beside it, and <paramref name="rows"/> rows of 100×20 from its template.</summary>
    private static UiScene ListScene(int rows)
    {
        var rowStates = new[] { UiDescs.State(UiStateIds.Normal), UiDescs.State(UiStateIds.Highlight) };
        var row = UiDescs.Element(0x41, F, 0, 0, 100, 20, states: rowStates, layout: 0x21000004);
        var template = new UiArrayProperty(UiPropertyIds.ListBoxEntryTemplates,
        [
            new UiStructProperty(UiPropertyIds.ListBoxEntryTemplate, new Dictionary<uint, UiProperty>
            {
                [UiPropertyIds.ListBoxEntryTemplateLayout] = new UiDataIdProperty(UiPropertyIds.ListBoxEntryTemplateLayout, 0x21000004),
                [UiPropertyIds.ListBoxEntryTemplateElement] = new UiEnumProperty(UiPropertyIds.ListBoxEntryTemplateElement, 0x41),
            }),
        ]);
        var list = UiDescs.Element(0x40, UiClassIds.ListBox, 0, 0, 100, 40, properties:
        [
            template,
            new UiBoolProperty(UiPropertyIds.ListBoxClickSelect, true),
            new UiBoolProperty(UiPropertyIds.ListBoxSelectedItemStateChange, true),
            new UiEnumProperty(UiPropertyIds.ListBoxItemNormalState, UiStateIds.Normal),
            new UiEnumProperty(UiPropertyIds.ListBoxItemSelectedState, UiStateIds.Highlight),
            new UiEnumProperty(UiPropertyIds.ScrollableVerticalScrollbar, 0x30),
        ]);
        var bar = Scrollbar(0x30, 100, 0);
        var scene = new UiScene([list, bar], extraLayouts: [UiDescs.Layout(0x21000004, 800, 600, row)]);
        var box = scene.Get<UiListBox>(0x40);
        for (var i = 0; i < rows; i++)
        {
            Assert.NotNull(box.AddItemFromTemplateList(0));
        }

        box.UpdateLayout();
        return scene;
    }

    private sealed class RecordingViewportHost : IUiViewportHost
    {
        public List<(uint Id, UiBox Box, bool Visible)> Binds { get; } = [];

        public void Bind(uint viewportElementId, UiBox screenBox, bool visible) => Binds.Add((viewportElementId, screenBox, visible));
    }
}

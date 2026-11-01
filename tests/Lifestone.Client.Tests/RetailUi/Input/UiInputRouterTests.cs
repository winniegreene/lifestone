using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Input;

/// <summary>
/// The input router (m2-plan.md R14): keys through the input maps by priority with their stops, the focus and text mode,
/// the mouse through map 3 to the element under it, the capture, clicks behind a modal blocker, the world fallback, the
/// tooltip timer and the cursor's priority.
/// </summary>
public sealed class UiInputRouterTests
{
    private const uint Button = UiClassIds.Button;
    private const uint F = UiClassIds.Field;

    [Fact]
    public void A_key_nothing_binds_is_not_taken_so_the_host_keeps_it()
    {
        var scene = new UiScene([]);

        Assert.False(scene.Input.KeyDown(DikScanCodes.F12));
        Assert.False(scene.Input.KeyUp(DikScanCodes.F12));
    }

    [Fact]
    public void A_movement_key_starts_its_action_on_press_ends_it_on_release_and_ignores_the_host_repeat()
    {
        var scene = new UiScene([]);
        var movement = new RecordingActionSink();
        scene.Input.RegisterInputMap(UiInputRouter.MovementMap, movement, 1000);

        Assert.True(scene.Input.KeyDown(DikScanCodes.W));
        Assert.True(scene.Input.IsActionInProgress(0x29));
        Assert.True(scene.Input.KeyDown(DikScanCodes.W));
        Assert.True(scene.Input.KeyUp(DikScanCodes.W));

        Assert.Equal(["4:29:start", "4:29:end"], movement.Lines);
        Assert.False(scene.Input.IsActionInProgress(0x29));
    }

    [Fact]
    public void Of_two_maps_binding_the_same_key_the_higher_priority_one_gets_it()
    {
        var scene = new UiScene([]);
        var dialog = new RecordingActionSink();
        var game = new RecordingActionSink();
        scene.Input.RegisterInputMap(UiInputRouter.AcceptEscapeMap, dialog, 2000);
        scene.Input.RegisterInputMap(0x10000009, game, 1000);

        scene.Tap(DikScanCodes.Escape);
        scene.Input.UnregisterInputMap(UiInputRouter.AcceptEscapeMap, dialog);
        scene.Tap(DikScanCodes.Escape);

        Assert.Equal(["9:27:start"], dialog.Lines);
        Assert.Equal(["10000009:27:start"], game.Lines);
    }

    [Fact]
    public void An_action_its_map_declines_goes_to_the_handlers_and_unclaimed_becomes_global_message_1()
    {
        var scene = new UiScene([]);
        var movement = new RecordingActionSink(takes: false);
        var handler = new RecordingActionSink(takes: false);
        var global = new GlobalRecorder();
        scene.Input.RegisterInputMap(UiInputRouter.MovementMap, movement, 1000);
        scene.Input.AddActionHandler(handler);
        scene.Manager.RegisterForGlobalMessage(global, UiGlobalMessageIds.ActionStart);

        scene.Input.KeyDown(DikScanCodes.Space);

        Assert.Equal(["4:31:start"], movement.Lines);
        Assert.Equal(["4:31:start"], handler.Lines);
        Assert.Equal([(UiGlobalMessageIds.ActionStart, 0x31)], global.Heard);
    }

    [Fact]
    public void A_focused_editable_text_takes_the_keyboard_and_hides_movement_until_it_loses_the_focus()
    {
        var scene = new UiScene([UiDescs.Element(2, UiClassIds.Text, 0, 0, 200, 20, properties: [new UiBoolProperty(UiPropertyIds.TextEditable, true)])]);
        var movement = new RecordingActionSink();
        var chat = new RecordingActionSink();
        scene.Input.RegisterInputMap(UiInputRouter.MovementMap, movement, 1000);
        scene.Input.RegisterInputMap(0x1000000A, chat, 1000);
        var text = scene.Get<UiTextElement>(2);
        scene.Root.Activate();

        Assert.True(text.TakeFocus());
        Assert.True(scene.Input.KeyboardCaptured);
        Assert.True(scene.World.TextEntryActive);
        Assert.True(scene.Input.KeyDown(DikScanCodes.W));
        Assert.True(scene.Input.Character('w'));
        scene.Input.KeyUp(DikScanCodes.W);
        scene.Tap(DikScanCodes.Return);

        Assert.Equal("w", text.Text);
        Assert.Empty(movement.Actions);
        Assert.Empty(chat.Actions);

        Assert.True(text.RelinquishFocus());
        Assert.False(scene.Input.KeyboardCaptured);
        Assert.False(scene.World.TextEntryActive);
        Assert.False(scene.Input.Character('x'));
        scene.Tap(DikScanCodes.W);
        scene.Tap(DikScanCodes.Return);

        Assert.Equal(["4:29:start", "4:29:end"], movement.Lines);
        Assert.Equal(["1000000A:10000023:start"], chat.Lines);
    }

    [Fact]
    public void The_focused_text_registers_its_maps_at_3000_with_the_keyboard_stop_ten_below()
    {
        var scene = new UiScene([UiDescs.Element(2, UiClassIds.Text, 0, 0, 200, 20, properties: [new UiBoolProperty(UiPropertyIds.TextEditable, true)])]);
        var text = scene.Get<UiTextElement>(2);
        scene.Root.Activate();

        Assert.True(text.TakeFocus());

        var maps = scene.Input.InputMaps.Where(m => ReferenceEquals(m.Callback, text)).Select(m => (m.InputMapId, m.Priority)).ToArray();
        Assert.Contains((UiInputRouter.KeyboardStopMap, UiElementManager.FocusInputMapPriority - 10), maps);
        Assert.Contains((UiInputRouter.TextEditMap, UiElementManager.FocusInputMapPriority), maps);
        Assert.Contains((UiInputRouter.TextClipboardMap, UiElementManager.FocusInputMapPriority), maps);

        text.RelinquishFocus();
        Assert.DoesNotContain(scene.Input.InputMaps, m => ReferenceEquals(m.Callback, text) && m.Priority >= UiElementManager.FocusInputMapPriority - 10);
    }

    [Fact]
    public void A_click_presses_the_button_under_the_mouse_and_releasing_over_it_clicks()
    {
        var scene = new UiScene([UiDescs.Element(3, Button, 10, 10, 50, 20)]);

        Assert.True(scene.Click(20, 20));

        Assert.Equal(1, scene.Count(3, UiMessageIds.ButtonClick));
        Assert.Equal(1, scene.Count(3, UiMessageIds.Click));
        Assert.Null(scene.Input.ElementWithMouseCapture);
        Assert.False(scene.Input.IsActionInProgress(7));
        Assert.Empty(scene.World.Events);
    }

    [Fact]
    public void The_capture_keeps_the_release_on_the_button_pressed_and_a_release_off_it_does_not_click()
    {
        var scene = new UiScene([UiDescs.Element(3, Button, 10, 10, 50, 20), UiDescs.Element(4, Button, 300, 300, 50, 20)]);
        var button = scene.Get<UiButton>(3);

        scene.Input.MouseMove(20, 20);
        scene.Input.MouseButton(UiMouseButton.Left, down: true);
        Assert.Same(button, scene.Input.ElementWithMouseCapture);
        Assert.True(button.IsPressed);

        scene.Input.MouseMove(310, 310);
        Assert.False(button.IsMouseOverTop);
        Assert.False(scene.Get<UiButton>(4).IsMouseOverTop);
        scene.Input.MouseButton(UiMouseButton.Left, down: false);

        Assert.Equal(1, scene.Count(3, UiMessageIds.MouseUp));
        Assert.Equal(0, scene.Count(3, UiMessageIds.ButtonClick));
        Assert.Equal(0, scene.Count(4, UiMessageIds.MouseUp));
        Assert.False(button.IsPressed);
        Assert.Null(scene.Input.ElementWithMouseCapture);
        Assert.True(scene.Get<UiButton>(4).IsMouseOverTop);
    }

    [Fact]
    public void A_modal_blocker_eats_the_clicks_behind_it_and_the_world_gets_none()
    {
        var scene = new UiScene(
        [
            UiDescs.Element(3, Button, 10, 10, 50, 20, zLevel: 1),
            UiDescs.Element(5, F, 0, 0, 800, 600, zLevel: 0, properties: [new UiBoolProperty(UiPropertyIds.ElementBlocksClicks, true)]),
        ]);

        Assert.True(scene.Click(20, 20));

        Assert.Equal(0, scene.Count(3, UiMessageIds.MouseDown));
        Assert.Empty(scene.World.Events);
    }

    [Fact]
    public void A_click_on_no_element_goes_to_the_world_with_its_release_and_the_button_action_ends()
    {
        var scene = new UiScene([UiDescs.Element(3, Button, 10, 10, 50, 20)]);

        scene.Input.MouseMove(500, 400);
        Assert.False(scene.Input.MouseButton(UiMouseButton.Left, down: true));
        Assert.True(scene.Input.IsActionInProgress(7));
        Assert.False(scene.Input.MouseButton(UiMouseButton.Left, down: false));

        Assert.Equal(["Left down at 500,400", "Left up at 500,400"], scene.World.Events);
        Assert.False(scene.Input.IsActionInProgress(7));
    }

    [Fact]
    public void A_viewport_is_a_hole_the_world_takes_clicks_through_where_a_plain_blocker_is_not()
    {
        var blocks = new UiBoolProperty(UiPropertyIds.ElementBlocksClicks, true);
        var scene = new UiScene(
        [
            UiDescs.Element(6, UiClassIds.Viewport, 0, 0, 400, 600, properties: [blocks]),
            UiDescs.Element(7, F, 400, 0, 400, 600, properties: [blocks]),
        ]);

        scene.Click(100, 100);
        scene.Click(600, 100);

        Assert.Equal(["Left down at 100,100", "Left up at 100,100"], scene.World.Events);
    }

    [Fact]
    public void A_double_click_reaches_an_element_that_wants_them_as_a_double_click_and_others_as_a_click()
    {
        var scene = new UiScene([UiDescs.Element(3, Button, 10, 10, 50, 20), UiDescs.Element(4, Button, 100, 10, 50, 20)]);
        scene.Get<UiButton>(4).SetWantsDoubleClicks(true);

        foreach (var x in new[] { 20, 110 })
        {
            scene.Input.MouseMove(x, 20);
            scene.Input.MouseButton(UiMouseButton.Left, down: true);
            scene.Input.MouseButton(UiMouseButton.Left, down: false);
            scene.Input.MouseButton(UiMouseButton.Left, down: true, doubleClick: true);
            scene.Input.MouseButton(UiMouseButton.Left, down: false);
        }

        Assert.Equal(2, scene.Count(3, UiMessageIds.Click));
        Assert.Equal(0, scene.Count(3, UiMessageIds.DoubleClick));
        Assert.Equal(2, scene.Count(3, UiMessageIds.ButtonClick));
        Assert.Equal(1, scene.Count(4, UiMessageIds.Click));
        Assert.Equal(1, scene.Count(4, UiMessageIds.DoubleClick));
        Assert.Contains(scene.Messages, m => m.ElementId == 3 && m.MessageId == UiMessageIds.MouseDown && m.Param1 == UiMouseActions.LeftDouble);
    }

    [Fact]
    public void A_click_activates_the_window_under_it_and_a_click_outside_every_window_deactivates_it()
    {
        var window = UiDescs.Element(8, F, 0, 0, 200, 200, properties: [new UiBoolProperty(UiPropertyIds.ElementActivatable, true)], children:
        [
            UiDescs.Element(3, Button, 10, 10, 50, 20),
        ]);
        var scene = new UiScene([], extraLayouts: [UiDescs.Layout(0x21000002, 800, 600, window)]);
        var root = scene.Manager.CreateRootElementByDataId(0x21000002, 8)!;

        scene.Click(20, 20);
        Assert.Same(root, scene.Manager.ActiveElement);
        Assert.True(root.IsActivated);
        Assert.Equal(1, scene.Count(8, UiMessageIds.Activated));

        scene.Click(500, 500);
        Assert.Null(scene.Manager.ActiveElement);
        Assert.Equal(1, scene.Count(8, UiMessageIds.Deactivated));
    }

    [Fact]
    public void A_click_on_nothing_takes_the_focus_from_the_text_field()
    {
        var scene = new UiScene([UiDescs.Element(2, UiClassIds.Text, 0, 0, 200, 20, properties: [new UiBoolProperty(UiPropertyIds.TextEditable, true)])]);
        var text = scene.Get<UiTextElement>(2);
        scene.Root.Activate();
        Assert.True(text.TakeFocus());

        scene.Click(500, 500);

        Assert.Null(scene.Manager.FocusElement);
        Assert.False(scene.Input.KeyboardCaptured);
    }

    [Fact]
    public void A_tooltip_opens_after_the_mouse_rests_for_the_delay_and_closes_when_it_moves_off()
    {
        var tooltip = UiDescs.Element(0x50, F, 0, 0, 40, 16, properties: [new UiEnumProperty(UiPropertyIds.ElementTooltipTextId, 0x51)], children:
        [
            UiDescs.Element(0x51, UiClassIds.Text, 0, 0, 40, 16, layout: 0x21000002),
        ], layout: 0x21000002);
        var scene = new UiScene(
        [
            UiDescs.Element(3, Button, 10, 10, 50, 20, properties:
            [
                new UiBoolProperty(UiPropertyIds.ElementTooltipsOn, true),
                new UiEnumProperty(UiPropertyIds.ElementTooltipId, 0x50),
                new UiDataIdProperty(UiPropertyIds.ElementTooltipLayout, 0x21000002),
            ]),
        ], extraLayouts: [UiDescs.Layout(0x21000002, 800, 600, tooltip)]);
        scene.Get<UiButton>(3).SetTooltip(UiStringRef.FromLiteral("Play"));

        scene.Input.MouseMove(20, 20);
        scene.Frame(0.2);
        Assert.Null(scene.Input.TooltipElement);
        scene.Frame(0.1);

        var open = Assert.IsAssignableFrom<UiElement>(scene.Input.TooltipElement);
        Assert.Equal("Play", Assert.IsType<UiTextElement>(open.GetChild(0x51)).Text);
        Assert.Equal((20 + 32, 20 + 32), (open.Box.X0, open.Box.Y0));

        scene.Input.MouseMove(600, 500);
        Assert.Null(scene.Input.TooltipElement);
        Assert.DoesNotContain(open, scene.Manager.Elements);
    }

    // The owner's sighting (M5 item 7): the tooltip was laid out in its text element's design width, so a long description
    // wrapped one word per line and the tooltip grew only downwards. UIElementManager::StartTooltip (11.6096 0x0045DF70)
    // measures the text first (UIElement_Text::InqSizewMargins, 11.6096 0x004698F0: no wider than the text element's
    // maximum width property, else the display) and sizes the tooltip to it.
    private const string LongTooltip = "Self: Affects how much magic a character can use, and how quickly they regain mana. Self also aids resistance to some spells.";

    [Fact]
    public void A_long_tooltip_lays_out_as_wide_as_the_display_allows_not_one_word_per_line()
    {
        var (scene, open) = OpenTooltip(LongTooltip, maxWidth: null);
        var label = Assert.IsType<UiTextElement>(open.GetChild(0x51));
        label.RecalculateGlyphList();

        // 7 pixels a character: 875 pixels of text wrap at the 800-pixel display into two lines, not twenty.
        Assert.Equal(2, label.Glyphs.Lines.Count);
        Assert.True(label.Box.Width > 700, $"text width {label.Box.Width}");
        Assert.True(label.Box.Width <= scene.Manager.DisplaySize.Width);
        Assert.Equal(open.Box.Width, label.Box.Width);
        Assert.True(label.PaperHeight <= label.Box.Height, $"text {label.PaperHeight} high in {label.Box.Height}");
        Assert.True(open.Box.X0 >= 0 && open.Box.X0 + open.Box.Width <= scene.Manager.DisplaySize.Width);
    }

    [Fact]
    public void A_long_tooltip_keeps_to_its_text_element_s_maximum_width()
    {
        var (_, open) = OpenTooltip(LongTooltip, maxWidth: 200);
        var label = Assert.IsType<UiTextElement>(open.GetChild(0x51));
        label.RecalculateGlyphList();

        // Wider than the longest word ("character," is 70 pixels) and no wider than 200: several words a line.
        Assert.InRange(label.Box.Width, 71, 200);
        Assert.InRange(label.Glyphs.Lines.Count, 5, 9);
        Assert.Equal(label.PaperHeight, label.Box.Height);
    }

    [Fact]
    public void A_short_tooltip_shrinks_to_its_text()
    {
        var (_, open) = OpenTooltip("Play", maxWidth: 200);
        var label = Assert.IsType<UiTextElement>(open.GetChild(0x51));

        Assert.Equal(4 * 7, label.Box.Width);
        Assert.Equal(12, label.Box.Height);
    }

    private static (UiScene Scene, UiElement Tooltip) OpenTooltip(string text, int? maxWidth)
    {
        var stretch = new UiEdges(UiEdge.OwnSide, UiEdge.OwnSide, UiEdge.OwnSide, UiEdge.OwnSide);
        UiProperty[] labelProperties = maxWidth is { } max ? [new UiIntegerProperty(UiPropertyIds.ElementMaxWidth, max)] : [];
        var tooltip = UiDescs.Element(0x50, F, 0, 0, 40, 16, properties: [new UiEnumProperty(UiPropertyIds.ElementTooltipTextId, 0x51)], children:
        [
            UiDescs.Element(0x51, UiClassIds.Text, 0, 0, 40, 16, edges: stretch, properties: labelProperties, layout: 0x21000002),
        ], layout: 0x21000002);
        var scene = new UiScene(
        [
            UiDescs.Element(3, Button, 10, 10, 50, 20, properties:
            [
                new UiBoolProperty(UiPropertyIds.ElementTooltipsOn, true),
                new UiEnumProperty(UiPropertyIds.ElementTooltipId, 0x50),
                new UiDataIdProperty(UiPropertyIds.ElementTooltipLayout, 0x21000002),
            ]),
        ], extraLayouts: [UiDescs.Layout(0x21000002, 800, 600, tooltip)]);
        scene.Get<UiButton>(3).SetTooltip(UiStringRef.FromLiteral(text));
        scene.Input.MouseMove(20, 20);
        scene.Frame(0.3);
        return (scene, Assert.IsAssignableFrom<UiElement>(scene.Input.TooltipElement));
    }

    [Fact]
    public void The_cursor_is_the_capture_s_then_the_entered_element_s_then_the_default()
    {
        var cursor = new UiCursorMedia(0x06001234, 3, 4);
        var scene = new UiScene(
        [
            UiDescs.Element(3, Button, 10, 10, 50, 20, media: [cursor]),
            UiDescs.Element(4, Button, 100, 10, 50, 20),
        ]);
        scene.Manager.Cursor.SetDefaultCursor(0x06000001, 0, 0);
        Assert.Equal(0x06000001u, scene.Manager.Cursor.CursorDid);

        scene.Input.MouseMove(20, 20);
        Assert.Equal(0x06001234u, scene.Manager.Cursor.CursorDid);
        Assert.Equal((3, 4), scene.Manager.Cursor.Hotspot);

        scene.Input.MouseButton(UiMouseButton.Left, down: true);
        scene.Input.MouseMove(110, 20);
        Assert.Equal(0x06001234u, scene.Manager.Cursor.CursorDid);

        scene.Input.MouseButton(UiMouseButton.Left, down: false);
        Assert.Equal(0x06000001u, scene.Manager.Cursor.CursorDid);
        Assert.Equal((0, 0), scene.Manager.Cursor.Hotspot);
    }

    [Fact]
    public void Deleting_the_hovered_element_whose_cursor_shows_brings_the_default_pointer_back()
    {
        var cursor = new UiCursorMedia(0x06001234, 3, 4);
        var window = UiDescs.Element(0x90, F, 300, 200, 200, 100, layout: 0x21000002, children:
        [
            UiDescs.Element(0x91, Button, 10, 10, 50, 20, layout: 0x21000002, media: [cursor]),
        ]);
        var scene = new UiScene([], extraLayouts: [UiDescs.Layout(0x21000002, 800, 600, window)]);
        scene.Manager.Cursor.SetDefaultCursor(0x06000001, 0, 0);
        var root = scene.Manager.CreateRootElementByDataId(0x21000002, 0x90)!;
        scene.Input.MouseMove(320, 220);
        Assert.Equal(0x06001234u, scene.Manager.Cursor.CursorDid);

        scene.Manager.DeleteElement(root);

        Assert.Equal(0x06000001u, scene.Manager.Cursor.CursorDid);
        Assert.Equal((0, 0), scene.Manager.Cursor.Hotspot);
        Assert.Null(scene.Manager.Cursor.Source);
    }

    [Fact]
    public void The_wheel_scrolls_through_map_0xA_and_otherwise_reaches_the_element_under_the_mouse()
    {
        var scene = new UiScene([UiDescs.Element(3, Button, 10, 10, 50, 20)]);
        var wheel = new RecordingActionSink();
        scene.Input.RegisterInputMap(UiInputRouter.ScrollMap, wheel, 3000);

        Assert.True(scene.Input.MouseWheel(2));
        Assert.Equal(["A:5:start", "A:5:end", "A:5:start", "A:5:end"], wheel.Lines);
        Assert.DoesNotContain(scene.Log.Lines, l => l.Contains("wheel notch", StringComparison.Ordinal));

        scene.Input.UnregisterInputMap(UiInputRouter.ScrollMap, wheel);
        scene.Input.MouseMove(20, 20);
        Assert.True(scene.Input.MouseWheel(-1));
        Assert.True(scene.Input.MouseWheel(-1));
        Assert.Contains(scene.Messages, m => m.ElementId == 3 && m.MessageId == UiMessageIds.MouseDown && m.Param1 == UiMouseActions.WheelDown);

        // The fallback route is Lifestone's guess: it says so, once.
        Assert.Single(scene.Log.Lines, l => l.Contains("TODO(retailui-runtime): a wheel notch no input map took", StringComparison.Ordinal));
    }

    [Fact]
    public void Mouse_look_goes_to_the_world_only_in_mouse_look_mode()
    {
        var scene = new UiScene([]);

        Assert.False(scene.Input.MouseLook(1, 2));
        scene.Input.MouseLookMode = true;
        Assert.True(scene.Input.MouseLook(1, 2));

        Assert.Equal(["look 1,2"], scene.World.Events);
    }

    [Fact]
    public void A_focus_loss_ends_the_held_actions_and_lets_go_of_shift_and_the_next_press_starts_afresh()
    {
        var scene = new UiScene([]);
        scene.Input.Bindings = FocusBindings();
        var movement = new RecordingActionSink();
        scene.Input.RegisterInputMap(UiInputRouter.MovementMap, movement, 1000);
        var changes = new List<bool>();
        scene.Input.ClientActiveChanged += changes.Add;

        scene.Input.KeyDown(DikScanCodes.W);
        scene.Frame();
        scene.Input.KeyDown(DikScanCodes.LShift);
        Assert.True(scene.Manager.ShiftKeyDown);
        Assert.Equal(TestKeyBindings.ShiftMeta, scene.Input.MetaMode);

        scene.Input.SetClientActive(false);
        scene.Input.SetClientActive(false);

        Assert.False(scene.Input.ClientActive);
        Assert.False(scene.Input.IsActionInProgress(0x29));
        Assert.False(scene.Manager.ShiftKeyDown);
        Assert.Equal(0u, scene.Input.MetaMode);
        Assert.Equal(["4:29:start", "4:29:start", "4:29:end"], movement.Lines);

        // No more repeats; the next press of W starts the action again, with no Shift (Shift+W would be 0x2A).
        scene.Frame();
        scene.Frame();
        scene.Input.SetClientActive(true);
        Assert.True(scene.Input.KeyDown(DikScanCodes.W));

        Assert.Equal(["4:29:start", "4:29:start", "4:29:end", "4:29:start"], movement.Lines);
        Assert.True(scene.Input.IsActionInProgress(0x29));
        Assert.Equal([false, true], changes);
    }

    [Fact]
    public void Retail_focus_loss_ends_no_held_action_so_it_keeps_repeating_until_pressed_and_released_again()
    {
        var scene = new UiScene([]);
        scene.Input.Bindings = FocusBindings();
        Assert.False(scene.Input.RetailFocusLoss);
        scene.Input.RetailFocusLoss = true;
        var movement = new RecordingActionSink();
        scene.Input.RegisterInputMap(UiInputRouter.MovementMap, movement, 1000);

        scene.Input.KeyDown(DikScanCodes.W);
        scene.Input.KeyDown(DikScanCodes.LShift);
        scene.Input.SetClientActive(false);
        scene.Frame();

        Assert.True(scene.Input.IsActionInProgress(0x29));
        Assert.Equal(0u, scene.Input.MetaMode);
        Assert.Equal(["4:29:start", "4:29:start"], movement.Lines);

        scene.Input.SetClientActive(true);
        scene.Input.KeyDown(DikScanCodes.W);
        scene.Input.KeyUp(DikScanCodes.W);
        Assert.False(scene.Input.IsActionInProgress(0x29));
        Assert.Equal("4:29:end", movement.Lines.Last());
    }

    [Fact]
    public void The_hosts_retail_glitch_flag_turns_retail_focus_loss_on()
    {
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([]), RetailGlitches = UiRetailGlitches.FocusLossActions });

        Assert.True(manager.Input.RetailFocusLoss);
    }

    [Fact]
    public void A_focus_loss_cancels_a_press_on_a_button_and_releases_a_press_to_the_world()
    {
        var scene = new UiScene([UiDescs.Element(3, Button, 10, 10, 50, 20)]);
        var button = scene.Get<UiButton>(3);

        scene.Input.MouseMove(20, 20);
        scene.Input.MouseButton(UiMouseButton.Left, down: true);
        Assert.True(button.IsPressed);
        scene.Input.SetClientActive(false);

        Assert.False(button.IsPressed);
        Assert.Null(scene.Input.ElementWithMouseCapture);
        Assert.False(scene.Input.IsActionInProgress(7));
        Assert.Equal(1, scene.Count(3, UiMessageIds.MouseUp));
        Assert.Equal(0, scene.Count(3, UiMessageIds.ButtonClick));

        scene.Input.SetClientActive(true);
        scene.Input.MouseMove(500, 400);
        scene.Input.MouseButton(UiMouseButton.Right, down: true);
        scene.Input.SetClientActive(false);

        Assert.Equal(["Right down at 500,400", "Right up at 500,400"], scene.World.Events);
        Assert.False(scene.Input.IsActionInProgress(8));

        // The release the window missed never comes; a later one is not a second release to the world.
        scene.Input.SetClientActive(true);
        scene.Input.MouseButton(UiMouseButton.Right, down: false);
        Assert.Equal(2, scene.World.Events.Count);
    }

    [Fact]
    public void Mouse_look_pauses_while_the_window_has_no_focus()
    {
        var scene = new UiScene([]);
        scene.Input.MouseLookMode = true;

        scene.Input.SetClientActive(false);
        Assert.False(scene.Input.MouseLook(1, 2));
        scene.Input.SetClientActive(true);
        Assert.True(scene.Input.MouseLook(3, 4));

        Assert.Equal(["look 3,4"], scene.World.Events);
        Assert.Single(scene.Log.Lines, l => l.Contains("TODO(retailui-runtime): on a focus loss in mouse look", StringComparison.Ordinal));
    }

    [Fact]
    public void A_held_auto_repeat_action_repeats_after_the_delay_at_the_interval()
    {
        var scene = new UiScene([]);
        var edit = new RecordingActionSink();
        scene.Input.RegisterInputMap(UiInputRouter.TextEditMap, edit, 3000);

        scene.Input.KeyDown(DikScanCodes.Back);
        scene.Frame(0.4);
        Assert.Single(edit.Actions);
        scene.Frame(0.2);
        scene.Frame(0.04);
        scene.Input.KeyUp(DikScanCodes.Back);

        Assert.True(edit.Actions.Count >= 3, string.Join(" ", edit.Lines));
        Assert.All(edit.Actions.SkipLast(1), a => Assert.True(a.Activation.Start));
        Assert.False(edit.Actions[^1].Activation.Start);
    }

    [Fact]
    public void Right_shift_is_left_shift_to_the_maps()
    {
        Assert.Equal(DikScanCodes.LShift, DikScanCodes.Canonical(DikScanCodes.RShift));
        Assert.Equal(DikScanCodes.W, DikScanCodes.Canonical(DikScanCodes.W));
    }

    /// <summary>W held is a continuous action 0x29 of the movement map; Shift+W is 0x2A; left Shift is the Shift meta key.</summary>
    private static RetailKeyBindings FocusBindings()
    {
        var w = RetailKeyBindings.Keyboard(DikScanCodes.W);
        RetailBinding[] bindings =
        [
            new(UiInputRouter.MovementMap, 0x29, w, 0, 3),
            new(UiInputRouter.MovementMap, 0x2A, w, TestKeyBindings.ShiftMeta, 3),
        ];
        RetailMetaKey[] metas = [new(RetailKeyBindings.Keyboard(DikScanCodes.LShift), TestKeyBindings.ShiftMeta)];
        return new RetailKeyBindings(bindings, metas, (map, _) => map == UiInputRouter.MovementMap ? RetailToggleType.Continuous : RetailToggleType.Invalid);
    }

    private sealed class GlobalRecorder : IUiGlobalListener
    {
        public List<(uint Message, int Data)> Heard { get; } = [];

        public void ListenToGlobalMessage(uint messageId, int data) => Heard.Add((messageId, data));
    }
}

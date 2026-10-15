using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Input;

/// <summary>
/// The router over the user's own key maps (KEYMAP 0x14000002 merged with 0x14000000): movement, chat, Escape, the mouse,
/// and the census facts the router relies on.
/// </summary>
public sealed class RealKeyMapTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresLocalDatsFact]
    public void Keyboard_bindings_all_fire_on_down_and_up_and_no_binding_names_the_stop_or_callback_only_maps()
    {
        var bindings = RetailKeyBindings.Load(fixture.Dats);

        Assert.All(bindings.Bindings.Where(b => b.Control.DeviceIndex == RetailKeyBindings.KeyboardDevice), b => Assert.Equal(3u, b.Activation));
        Assert.DoesNotContain(bindings.Bindings, b => b.InputMapId is UiInputRouter.KeyboardStopMap or UiInputRouter.StopMap or UiInputRouter.UiManagerMap or 14);
    }

    [RequiresLocalDatsFact]
    public void The_retail_mouse_and_wheel_bindings_are_the_ones_the_tests_are_shaped_on()
    {
        var real = RetailKeyBindings.Load(fixture.Dats).Bindings;
        var shaped = TestKeyBindings.RetailShaped().Bindings;

        foreach (var binding in shaped)
        {
            Assert.Contains(real, r => r.InputMapId == binding.InputMapId && r.ActionId == binding.ActionId && r.Control == binding.Control
                && r.MetaMode == binding.MetaMode && r.Activation == binding.Activation);
        }
    }

    [RequiresLocalDatsFact]
    public void Every_keyboard_control_of_the_engine_and_game_key_maps_has_a_godot_physical_key()
    {
        // The in-engine check (--retail-ui-checks) pins the same thing; this one runs without the engine.
        var reachable = GodotKeyCodes.MappedKeycodes
            .SelectMany(code => new[] { UiKeyLocation.Unspecified, UiKeyLocation.Left, UiKeyLocation.Right }.Select(side => GodotKeyCodes.ToDik(code, side)))
            .ToHashSet();
        foreach (var enumValue in new[] { RetailKeyBindings.EngineKeyMapEnum, RetailKeyMap.DefaultKeyMapEnum })
        {
            var map = RetailKeyMap.Load(fixture.Dats, enumValue);
            var controls = map.Bindings.Select(b => b.Control).Concat(map.MetaKeys.Select(m => m.Control)).Where(c => c.DeviceIndex == RetailKeyBindings.KeyboardDevice);
            Assert.All(controls, c => Assert.Contains(c.Semantic, reachable));
        }
    }

    [RequiresLocalDatsTheory]
    [InlineData(DikScanCodes.W, 0x29u)]
    [InlineData(DikScanCodes.Up, 0x29u)]
    [InlineData(DikScanCodes.X, 0x2Au)]
    [InlineData(DikScanCodes.A, 0x2Fu)]
    [InlineData(DikScanCodes.D, 0x2Eu)]
    [InlineData(DikScanCodes.Z, 0x2Du)]
    [InlineData(DikScanCodes.C, 0x2Cu)]
    [InlineData(DikScanCodes.Space, 0x31u)]
    public void A_movement_key_holds_its_action_while_held(int dik, uint action)
    {
        var scene = Scene();
        var movement = new RecordingActionSink();
        scene.Input.RegisterInputMap(UiInputRouter.MovementMap, movement, 1000);

        scene.Tap(dik);

        Assert.Equal([$"4:{action:X}:start", $"4:{action:X}:end"], movement.Lines);
        Assert.All(movement.Actions, a => Assert.Equal(RetailToggleType.Momentary, a.Activation.Toggle));
    }

    [RequiresLocalDatsFact]
    public void Autorun_toggles_on_each_press_stop_is_an_impulse_and_moving_forward_ends_autorun()
    {
        var scene = Scene();
        var movement = new RecordingActionSink();
        scene.Input.RegisterInputMap(UiInputRouter.MovementMap, movement, 1000);

        scene.Tap(DikScanCodes.Q);
        Assert.True(scene.Input.IsActionInProgress(0x30));
        scene.Tap(DikScanCodes.Q);
        scene.Tap(DikScanCodes.S);
        scene.Tap(DikScanCodes.Q);
        scene.Tap(DikScanCodes.W);

        Assert.Equal(["4:30:start", "4:30:end", "4:2B:start", "4:30:start", "4:30:end", "4:29:start", "4:29:end"], movement.Lines);
        Assert.False(scene.Input.IsActionInProgress(0x30));
    }

    [RequiresLocalDatsFact]
    public void Return_opens_chat_through_the_game_map_until_a_dialog_s_map_takes_Return_and_Escape_first()
    {
        var scene = Scene();
        var chat = new RecordingActionSink();
        var dialog = new RecordingActionSink();
        var game = new RecordingActionSink();
        scene.Input.RegisterInputMap(0x1000000A, chat, 1000);
        scene.Input.RegisterInputMap(0x10000009, game, 1000);

        scene.Tap(DikScanCodes.Return);
        scene.Tap(DikScanCodes.Escape);
        scene.Input.RegisterInputMap(UiInputRouter.AcceptEscapeMap, dialog, 2000);
        scene.Tap(DikScanCodes.Return);
        scene.Tap(DikScanCodes.Escape);

        Assert.Equal(["1000000A:10000023:start"], chat.Lines);
        Assert.Equal(["10000009:27:start"], game.Lines);
        Assert.Equal(["9:25:start", "9:27:start"], dialog.Lines);
    }

    [RequiresLocalDatsFact]
    public void A_click_on_a_button_clicks_it_through_the_retail_mouse_map()
    {
        var scene = Scene([UiDescs.Element(3, UiClassIds.Button, 10, 10, 50, 20)]);

        scene.Click(20, 20);

        Assert.Equal(1, scene.Count(3, UiMessageIds.ButtonClick));
    }

    private UiScene Scene(IEnumerable<UiElementDesc>? children = null)
    {
        var scene = new UiScene(children ?? []);
        scene.Input.Bindings = RetailKeyBindings.Load(fixture.Dats);
        return scene;
    }
}

/// <summary>The Godot physical key code to DIK table (plain ints; m2-plan.md R14).</summary>
public sealed class GodotKeyCodesTests
{
    private const int Special = 0x400000;

    [Theory]
    [InlineData(0x57, UiKeyLocation.Unspecified, DikScanCodes.W)]
    [InlineData('w', UiKeyLocation.Unspecified, DikScanCodes.W)]
    [InlineData(0x20, UiKeyLocation.Unspecified, DikScanCodes.Space)]
    [InlineData(0x30, UiKeyLocation.Unspecified, DikScanCodes.D0)]
    [InlineData(0x60, UiKeyLocation.Unspecified, DikScanCodes.Grave)]
    [InlineData(Special | 0x01, UiKeyLocation.Unspecified, DikScanCodes.Escape)]
    [InlineData(Special | 0x05, UiKeyLocation.Unspecified, DikScanCodes.Return)]
    [InlineData(Special | 0x06, UiKeyLocation.Unspecified, DikScanCodes.NumPadEnter)]
    [InlineData(Special | 0x0F, UiKeyLocation.Unspecified, DikScanCodes.Left)]
    [InlineData(Special | 0x10, UiKeyLocation.Unspecified, DikScanCodes.Up)]
    [InlineData(Special | 0x1C, UiKeyLocation.Unspecified, DikScanCodes.F1)]
    [InlineData(Special | 0x27, UiKeyLocation.Unspecified, DikScanCodes.F12)]
    [InlineData(Special | 0x86, UiKeyLocation.Unspecified, DikScanCodes.NumPad0)]
    [InlineData(GodotKeyCodes.Shift, UiKeyLocation.Unspecified, DikScanCodes.LShift)]
    [InlineData(GodotKeyCodes.Shift, UiKeyLocation.Right, DikScanCodes.RShift)]
    [InlineData(GodotKeyCodes.Ctrl, UiKeyLocation.Left, DikScanCodes.LControl)]
    [InlineData(GodotKeyCodes.Ctrl, UiKeyLocation.Right, DikScanCodes.RControl)]
    [InlineData(GodotKeyCodes.Alt, UiKeyLocation.Right, DikScanCodes.RMenu)]
    [InlineData(Special | 0x3E, UiKeyLocation.Unspecified, 0)]
    [InlineData(0, UiKeyLocation.Unspecified, 0)]
    public void A_godot_physical_key_maps_to_its_scan_code(int keycode, UiKeyLocation location, int dik) =>
        Assert.Equal(dik, GodotKeyCodes.ToDik(keycode, location));

    [Fact]
    public void Every_mapped_key_has_a_scan_code_and_only_the_two_print_keys_share_one()
    {
        var mapped = GodotKeyCodes.MappedKeycodes.ToArray();
        var diks = mapped.Select(k => GodotKeyCodes.ToDik(k)).ToArray();

        Assert.All(diks, d => Assert.InRange(d, 1, 0xFF));
        Assert.Equal(mapped.Length, mapped.Distinct().Count());
        var shared = diks.GroupBy(d => d).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        Assert.Equal([DikScanCodes.SysRq], shared);
    }
}

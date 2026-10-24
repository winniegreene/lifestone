using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.LifestonePanels;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.LifestonePanelTests;

/// <summary>
/// The keys that open Lifestone's panels (D41): F7 for the tools panel and Scroll Lock for the inspector. They must be keys retail's default game map (KEYMAP <c>gmDefault</c>, MasterInputMap
/// 0x14000000) does not bind, with any meta keys, and the engine map (0x14000002) must not bind them either.
/// </summary>
public sealed class LifestoneHotkeysTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresLocalDatsFact]
    public void The_two_keys_are_unbound_in_retails_game_map_0x14000000_and_in_the_engine_map()
    {
        var game = RetailKeyMap.Load(fixture.Dats);
        var engine = RetailKeyMap.Load(fixture.Dats, RetailKeyBindings.EngineKeyMapEnum);
        Assert.Equal(0x14000000u, game.Id);
        Assert.Equal("gmDefaultMap", game.Name);
        Assert.Equal(0x14000002u, engine.Id);

        foreach (var map in new[] { game, engine })
        {
            var keyboard = map.Bindings.Where(b => map.DeviceOf(b.Control) == RetailDeviceType.Keyboard).ToArray();
            Assert.NotEmpty(keyboard);
            foreach (var dik in new[] { LifestoneHotkeys.ToolsKey, LifestoneHotkeys.InspectorKey, LifestoneHotkeys.AutoBuffKey })
            {
                // No binding of the key in any input map, whatever meta keys it needs; and it is no meta key either.
                Assert.DoesNotContain(keyboard, b => b.Control.Semantic == dik);
                Assert.DoesNotContain(map.MetaKeys, m => m.Control.Semantic == dik && m.Control.DeviceIndex == RetailKeyBindings.KeyboardDevice);
            }
        }

        // The other function keys are all taken in gmDefaultMap, which is why F7 is the one that was free.
        var bound = game.Bindings.Where(b => game.DeviceOf(b.Control) == RetailDeviceType.Keyboard).Select(b => b.Control.Semantic).ToHashSet();
        foreach (var dik in new[] { DikScanCodes.F1, DikScanCodes.F2, DikScanCodes.F3, DikScanCodes.F4, DikScanCodes.F5, DikScanCodes.F6, DikScanCodes.F8, DikScanCodes.F9, DikScanCodes.F10, DikScanCodes.F11, DikScanCodes.F12 })
        {
            Assert.Contains(dik, bound);
        }

        // Lifestone's own input map is none of retail's.
        Assert.DoesNotContain(game.Bindings.Concat(engine.Bindings), b => b.InputMapId == LifestoneHotkeys.InputMap);
        Assert.DoesNotContain(game.Bindings.Concat(engine.Bindings), b => b.ActionId is LifestoneHotkeys.OpenToolsAction or LifestoneHotkeys.OpenInspectorAction or LifestoneHotkeys.AutoBuffAction);
    }

    [RequiresLocalDatsFact]
    public void Both_keys_are_reachable_from_the_hosts_physical_keys()
    {
        var reachable = GodotKeyCodes.MappedKeycodes.Select(code => GodotKeyCodes.ToDik(code, UiKeyLocation.Unspecified)).ToHashSet();
        Assert.Contains(LifestoneHotkeys.ToolsKey, reachable);
        Assert.Contains(LifestoneHotkeys.InspectorKey, reachable);
        Assert.Contains(LifestoneHotkeys.AutoBuffKey, reachable);
        Assert.Equal(0x41, LifestoneHotkeys.ToolsKey);
        Assert.Equal(0x46, LifestoneHotkeys.InspectorKey);
    }

    [RequiresLocalDatsFact]
    public void Adding_the_bindings_leaves_retails_untouched_and_the_keys_find_only_ours()
    {
        var retail = RetailKeyBindings.Load(fixture.Dats);
        var both = retail.With(LifestoneHotkeys.Bindings);

        Assert.Equal(retail.Bindings.Count + 3, both.Bindings.Count);
        Assert.Equal(retail.Bindings, both.Bindings.Take(retail.Bindings.Count));
        Assert.False(retail.TryFindBestMatch(LifestoneHotkeys.InputMap, RetailKeyBindings.Keyboard(LifestoneHotkeys.ToolsKey), 0, 1, out _));
        Assert.True(both.TryFindBestMatch(LifestoneHotkeys.InputMap, RetailKeyBindings.Keyboard(LifestoneHotkeys.ToolsKey), 0, 1, out var tools));
        Assert.Equal(LifestoneHotkeys.OpenToolsAction, tools.ActionId);
        Assert.True(both.TryFindBestMatch(LifestoneHotkeys.InputMap, RetailKeyBindings.Keyboard(LifestoneHotkeys.InspectorKey), 0, 1, out var inspector));
        Assert.Equal(LifestoneHotkeys.OpenInspectorAction, inspector.ActionId);
        Assert.True(both.TryFindBestMatch(LifestoneHotkeys.InputMap, RetailKeyBindings.Keyboard(LifestoneHotkeys.AutoBuffKey), 0, 1, out var autoBuff));
        Assert.Equal(LifestoneHotkeys.AutoBuffAction, autoBuff.ActionId);
    }

    [Fact]
    public void A_press_calls_its_callback_and_an_unwired_inspector_key_is_a_named_TODO()
    {
        var scene = new UiScene([]);
        var hotkeys = new LifestoneHotkeys(scene.Log);
        var tools = 0;
        hotkeys.OpenTools = () => tools++;
        scene.Input.Bindings = new RetailKeyBindings([], []);
        hotkeys.Attach(scene.Input); // adds the bindings to the host's (here empty) set, and registers the keys

        Assert.True(scene.Input.KeyDown(DikScanCodes.F7));
        Assert.True(scene.Input.KeyUp(DikScanCodes.F7));
        Assert.Equal((1, 1, 0), (tools, hotkeys.ToolsPresses, hotkeys.InspectorPresses));

        // Scroll Lock before the inspector is wired: counted and logged as a named TODO, nothing else.
        scene.Input.KeyDown(DikScanCodes.Scroll);
        scene.Input.KeyUp(DikScanCodes.Scroll);
        Assert.Equal(1, hotkeys.InspectorPresses);
        Assert.Contains(scene.Log.Entries, e => e.Message.Contains("TODO(g7-inspector-unwired)", StringComparison.Ordinal));

        var opened = 0;
        hotkeys.OpenInspector = () => opened++;
        scene.Input.KeyDown(DikScanCodes.Scroll);
        scene.Input.KeyUp(DikScanCodes.Scroll);
        Assert.Equal((1, 2), (opened, hotkeys.InspectorPresses));

        // Pause (the auto-buff) before the host wires it: counted and logged as a named TODO; wired, it calls back.
        scene.Input.KeyDown(DikScanCodes.Pause);
        scene.Input.KeyUp(DikScanCodes.Pause);
        Assert.Equal(1, hotkeys.AutoBuffPresses);
        Assert.Contains(scene.Log.Entries, e => e.Message.Contains("TODO(m4-autobuff-key-unwired)", StringComparison.Ordinal));
        var toggled = 0;
        hotkeys.ToggleAutoBuff = () => toggled++;
        scene.Input.KeyDown(DikScanCodes.Pause);
        scene.Input.KeyUp(DikScanCodes.Pause);
        Assert.Equal((1, 2), (toggled, hotkeys.AutoBuffPresses));

        // A key held down repeats nothing; other keys are none of ours.
        scene.Input.KeyDown(DikScanCodes.F7);
        scene.Input.KeyDown(DikScanCodes.F7);
        scene.Input.KeyUp(DikScanCodes.F7);
        Assert.Equal(2, tools);
        Assert.False(scene.Input.KeyDown(DikScanCodes.F8));
        scene.Input.KeyUp(DikScanCodes.F8);

        hotkeys.Detach(scene.Input);
        Assert.False(scene.Input.KeyDown(DikScanCodes.F7));
        scene.Input.KeyUp(DikScanCodes.F7);
        Assert.Equal(2, tools);
    }
}

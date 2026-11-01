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
            foreach (var dik in new[] { LifestoneHotkeys.ToolsKey, LifestoneHotkeys.InspectorKey, LifestoneHotkeys.AutoBuffKey, LifestoneHotkeys.AutopilotKey })
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
        Assert.DoesNotContain(game.Bindings.Concat(engine.Bindings), b => b.ActionId is LifestoneHotkeys.OpenToolsAction or LifestoneHotkeys.OpenInspectorAction or LifestoneHotkeys.AutoBuffAction or LifestoneHotkeys.AutopilotAction);
    }

    [RequiresLocalDatsFact]
    public void Both_keys_are_reachable_from_the_hosts_physical_keys()
    {
        var reachable = GodotKeyCodes.MappedKeycodes.Select(code => GodotKeyCodes.ToDik(code, UiKeyLocation.Unspecified)).ToHashSet();
        Assert.Contains(LifestoneHotkeys.ToolsKey, reachable);
        Assert.Contains(LifestoneHotkeys.InspectorKey, reachable);
        Assert.Contains(LifestoneHotkeys.AutoBuffKey, reachable);
        Assert.Contains(LifestoneHotkeys.AutopilotKey, reachable);
        Assert.Equal(0x41, LifestoneHotkeys.ToolsKey);
        Assert.Equal(0x46, LifestoneHotkeys.InspectorKey);
        Assert.Equal(0x45, LifestoneHotkeys.AutopilotKey); // Num Lock (M5 4 h2, D20)
    }

    [RequiresLocalDatsFact]
    public void Adding_the_bindings_leaves_retails_untouched_and_the_keys_find_only_ours()
    {
        var retail = RetailKeyBindings.Load(fixture.Dats);
        var both = retail.With(LifestoneHotkeys.Bindings);

        Assert.Equal(retail.Bindings.Count + 4, both.Bindings.Count);
        Assert.Equal(retail.Bindings, both.Bindings.Take(retail.Bindings.Count));
        Assert.False(retail.TryFindBestMatch(LifestoneHotkeys.InputMap, RetailKeyBindings.Keyboard(LifestoneHotkeys.ToolsKey), 0, 1, out _));
        Assert.True(both.TryFindBestMatch(LifestoneHotkeys.InputMap, RetailKeyBindings.Keyboard(LifestoneHotkeys.ToolsKey), 0, 1, out var tools));
        Assert.Equal(LifestoneHotkeys.OpenToolsAction, tools.ActionId);
        Assert.True(both.TryFindBestMatch(LifestoneHotkeys.InputMap, RetailKeyBindings.Keyboard(LifestoneHotkeys.InspectorKey), 0, 1, out var inspector));
        Assert.Equal(LifestoneHotkeys.OpenInspectorAction, inspector.ActionId);
        Assert.True(both.TryFindBestMatch(LifestoneHotkeys.InputMap, RetailKeyBindings.Keyboard(LifestoneHotkeys.AutoBuffKey), 0, 1, out var autoBuff));
        Assert.Equal(LifestoneHotkeys.AutoBuffAction, autoBuff.ActionId);
        Assert.True(both.TryFindBestMatch(LifestoneHotkeys.InputMap, RetailKeyBindings.Keyboard(LifestoneHotkeys.AutopilotKey), 0, 1, out var autopilot));
        Assert.Equal(LifestoneHotkeys.AutopilotAction, autopilot.ActionId);
    }

    /// <summary>
    /// D20 / M5 4 h2: the autopilot's key (Num Lock, DirectInput 0x45) is none of retail's: no binding of it, with any meta keys, in any input map of the key table (not only the two the other proof reads: the
    /// combat, magic and shortcut maps too), it is no meta key, and it is a key the D20 order reached first: of the keys near Pause, Num Lock is the first that every map leaves unbound.
    /// </summary>
    [RequiresLocalDatsFact]
    public void The_autopilot_key_Num_Lock_is_unbound_in_every_retail_input_map_and_the_keys_before_it_in_the_order_are_taken_D20()
    {
        var retail = RetailKeyBindings.Load(fixture.Dats);
        var control = RetailKeyBindings.Keyboard(LifestoneHotkeys.AutopilotKey);
        Assert.DoesNotContain(retail.Bindings, b => b.Control == control);
        Assert.Equal(0x45, LifestoneHotkeys.AutopilotKey);

        foreach (var map in new[] { RetailKeyMap.Load(fixture.Dats), RetailKeyMap.Load(fixture.Dats, RetailKeyBindings.EngineKeyMapEnum) })
        {
            Assert.DoesNotContain(map.Bindings, b => map.DeviceOf(b.Control) == RetailDeviceType.Keyboard && b.Control.Semantic == DikScanCodes.NumLock);
            Assert.DoesNotContain(map.MetaKeys, m => m.Control.Semantic == DikScanCodes.NumLock && m.Control.DeviceIndex == RetailKeyBindings.KeyboardDevice);

            // The keys D20 listed before the numpad's are all taken, so none could be the one: Insert, Home, End, Page Up and Page Down.
            if (map.Id == 0x14000000u)
            {
                var bound = map.Bindings.Where(b => map.DeviceOf(b.Control) == RetailDeviceType.Keyboard).Select(b => b.Control.Semantic).ToHashSet();
                foreach (var dik in new[] { DikScanCodes.Insert, DikScanCodes.Home, DikScanCodes.End, DikScanCodes.Prior, DikScanCodes.Next })
                {
                    Assert.Contains(dik, bound);
                }
            }
        }
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

        // Num Lock (M5 7 D145): no longer the autopilot's toggle. Still Lifestone's key (taken, so nothing of retail's sees it), counted and logged; wired, it only hands the host its hint line for the chat.
        Assert.True(scene.Input.KeyDown(DikScanCodes.NumLock));
        scene.Input.KeyUp(DikScanCodes.NumLock);
        Assert.Equal(1, hotkeys.AutopilotPresses);
        Assert.Contains(scene.Log.Entries, e => e.Message.Contains("Num Lock was pressed: it does nothing to the autopilot", StringComparison.Ordinal));
        var hints = new List<string>();
        hotkeys.AutopilotKeyHint = hints.Add;
        scene.Input.KeyDown(DikScanCodes.NumLock);
        scene.Input.KeyUp(DikScanCodes.NumLock);
        Assert.Equal((LifestoneHotkeys.AutopilotKeyHintLine, 2), (Assert.Single(hints), hotkeys.AutopilotPresses));
        Assert.Contains("/auto", LifestoneHotkeys.AutopilotKeyHintLine, StringComparison.Ordinal);

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

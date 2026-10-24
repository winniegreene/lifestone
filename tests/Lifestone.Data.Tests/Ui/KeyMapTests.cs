using Lifestone.Data.Dat;
using Lifestone.Data.Ui;

namespace Lifestone.Data.Tests.Ui;

/// <summary>
/// Retail's default key map and action map (m2-plan.md R7): the KEYMAP chain, the binding census, and the movement, run,
/// jump, chat-entry and escape bindings pinned by action.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class KeyMapTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    // DirectInput scan codes (DIK_*).
    private const int DikEscape = 0x01;
    private const int DikW = 0x11;
    private const int DikEnter = 0x1C;
    private const int DikLeftShift = 0x2A;
    private const int DikSpace = 0x39;
    private const int DikTab = 0x0F;
    private const int DikUp = 0xC8;

    // Input maps and actions of the default maps.
    private const uint MovementMap = 0x4;
    private const uint GameplayMap = 0x10000009;
    private const uint ChatEntryMap = 0x1000000A;
    private const uint ChatModeMap = 0x1000000D;

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void KEYMAP_gmDefault_resolves_to_0x14000000_named_gmDefaultMap()
    {
        Assert.Equal("KEYMAP", Dats.EnumMaps.GroupName(EnumGroup.KeyMap));
        Assert.Equal("gmDefault", Dats.EnumMaps.NameOf(EnumGroup.KeyMap, RetailKeyMap.DefaultKeyMapEnum));
        Assert.True(Dats.EnumMaps.TryResolve(EnumGroup.KeyMap, RetailKeyMap.DefaultKeyMapEnum, out var did));
        Assert.Equal(0x14000000u, did);

        var map = RetailKeyMap.Load(Dats);
        Assert.Equal((0x14000000u, "gmDefaultMap"), (map.Id, map.Name));
        Assert.Equal([RetailDeviceType.Keyboard, RetailDeviceType.Mouse], map.Devices);

        // The internal enum gmInternal only exists in the map's internal table (EnumIdMaps' fallback).
        Assert.True(Dats.EnumMaps.TryResolve(EnumGroup.KeyMap, 0x10000002, out var internalMap));
        Assert.Equal(0x14000001u, internalMap);
        var error = Assert.Throws<InvalidDataException>(() => RetailKeyMap.Load(Dats, 0x10000FFF));
        Assert.Equal("TODO(retailui-data): KEYMAP enum 0x10000FFF does not resolve to a MasterInputMap.", error.Message);
    }

    [RequiresDatsFact]
    public void The_default_map_census()
    {
        var map = RetailKeyMap.Load(Dats);

        Assert.Equal(14, map.InputMapIds.Count);
        Assert.Equal(9, map.MetaKeys.Count);
        Assert.All(map.Bindings, b => Assert.NotEqual(RetailDeviceType.Invalid, map.DeviceOf(b.Control)));
        Assert.All(map.Bindings, b => Assert.Equal(0u, b.Activation & 0x80000000u));
        Assert.All(map.Bindings, b => Assert.Contains(b.ActivationType, (uint[])[1, 2, 3, 4, 8, 0x10, 0x18, 0x20, 0x40, 0x60, 0x80]));
        Assert.Equal(map.Bindings.Count, map.Bindings.Select(b => (b.InputMapId, b.Control, b.MetaMode, b.Activation)).Distinct().Count());

        // Meta keys: left Shift, both Ctrl, both Alt, both Windows keys; right Shift is none.
        Assert.Equal(0x80000000u, map.MetaModeOf(Key(DikLeftShift)));
        Assert.Equal(0x40000000u, map.MetaModeOf(Key(0x1D)));
        Assert.Equal(0x40000000u, map.MetaModeOf(Key(0x9D)));
        Assert.Equal(0x20000000u, map.MetaModeOf(Key(0x38)));
        Assert.Equal(0x10000000u, map.MetaModeOf(Key(0xDC)));
        Assert.Equal(0u, map.MetaModeOf(Key(0x36)));
    }

    [RequiresLocalDatsFact]
    public void Movement_run_jump_chat_entry_and_escape_are_bound_as_retail_ships_them()
    {
        var map = RetailKeyMap.Load(Dats);
        var actions = RetailActionMap.Load(Dats);
        var strings = new UiStrings(new UiStringTables(Dats));

        string Name(uint inputMap, uint action)
        {
            Assert.True(actions.TryGetAction(inputMap, action, out var a), $"action 0x{action:X} of map 0x{inputMap:X}");
            return strings.GetString(new UiStringRef(a.NameStringId, actions.StringTableId)).Trim();
        }

        // Move Forward: W and Up arrow, momentary.
        Assert.Equal("Move Forward", Name(MovementMap, 0x29));
        Assert.Equal([DikW, DikUp], Keys(map, MovementMap, 0x29));
        Assert.True(actions.TryGetAction(MovementMap, 0x29, out var forward));
        Assert.Equal(RetailToggleType.Momentary, forward.Toggle);

        // Run/walk on left Shift; jump on Space; autorun toggles on Q.
        Assert.Equal("Toggle Run/Walk", Name(MovementMap, 0x32));
        Assert.Equal([DikLeftShift], Keys(map, MovementMap, 0x32));
        Assert.Equal("Jump", Name(MovementMap, 0x31));
        Assert.Equal([DikSpace], Keys(map, MovementMap, 0x31));
        Assert.True(actions.TryGetAction(MovementMap, 0x30, out var autorun));
        Assert.Equal(RetailToggleType.Toggle, autorun.Toggle);

        // Chat entry: Enter begins chat mode; Tab enters and leaves it.
        Assert.Equal("Begin Chat Mode", Name(ChatEntryMap, 0x10000023));
        Assert.Equal([DikEnter], Keys(map, ChatEntryMap, 0x10000023));
        Assert.Equal([DikTab], Keys(map, ChatModeMap, 0x10000024));

        // Escape: action 0x27, which the user cannot rebind; Shift+Escape ends the character session.
        var escape = Assert.Single(map.BindingsFor(0x27, GameplayMap));
        Assert.Equal((DikEscape, 0u), (escape.Control.Semantic, escape.MetaMode));
        Assert.True(actions.TryGetAction(GameplayMap, 0x27, out var escapeAction));
        Assert.False(escapeAction.IsUserBindable);
        var end = Assert.Single(map.BindingsFor(0x10000026, GameplayMap));
        Assert.Equal((DikEscape, 0x80000000u), (end.Control.Semantic, end.MetaMode));
    }

    [RequiresDatsFact]
    public void The_action_map_is_ACTIONMAP_Default_with_its_names_in_ActionDescription()
    {
        var actions = RetailActionMap.Load(Dats);

        Assert.Equal((0x26000000u, 0x23000005u), (actions.Id, actions.StringTableId));
        Assert.Equal(25, actions.Actions.Select(a => a.InputMapId).Distinct().Count()); // 27 input maps, two of them empty
        Assert.All(actions.Actions, a => Assert.InRange((uint)a.Toggle, 1u, 5u));
        Assert.Equal(16, actions.ConflictingInputMaps.Count);
        Assert.Contains(GameplayMap, actions.ConflictingInputMaps[MovementMap]);

        // Every binding of the default key map fires an action its input map defines.
        var map = RetailKeyMap.Load(Dats);
        Assert.All(map.Bindings, b => Assert.True(actions.TryGetAction(b.InputMapId, b.ActionId, out _), $"0x{b.ActionId:X} in 0x{b.InputMapId:X}"));
    }

    private static RetailControl Key(int scanCode) => new((uint)scanCode << 16);

    private static int[] Keys(RetailKeyMap map, uint inputMap, uint action) =>
        [.. map.BindingsFor(action, inputMap).Where(b => b.MetaMode == 0 && map.DeviceOf(b.Control) == RetailDeviceType.Keyboard).Select(b => b.Control.Semantic)];
}

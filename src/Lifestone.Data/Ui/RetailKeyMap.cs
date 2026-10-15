using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;

namespace Lifestone.Data.Ui;

/// <summary>An input device type (retail's <c>DeviceType</c>, as <c>DeviceTypeToString</c> names them).</summary>
public enum RetailDeviceType : byte
{
    /// <summary>0: none.</summary>
    Invalid = 0,

    /// <summary>1: the keyboard.</summary>
    Keyboard = 1,

    /// <summary>2: the mouse.</summary>
    Mouse = 2,

    /// <summary>3: a joystick.</summary>
    Joystick = 3,

    /// <summary>4: a virtual device.</summary>
    Virtual = 4,
}

/// <summary>How an action behaves while its control is held (retail's <c>ToggleType</c> of an ActionMap entry).</summary>
public enum RetailToggleType : uint
{
    /// <summary>0: none.</summary>
    Invalid = 0,

    /// <summary>1: on while held.</summary>
    Momentary = 1,

    /// <summary>2: each press flips it.</summary>
    Toggle = 2,

    /// <summary>3: one event per press.</summary>
    Impulse = 3,

    /// <summary>4: repeats while held.</summary>
    AutoRepeat = 4,

    /// <summary>5: a continuous value (an axis).</summary>
    Continuous = 5,
}

/// <summary>
/// A control of an input device (retail's <c>ControlSpecification::m_dwKey</c>): the device's index in the key map's
/// device list (low byte), a sub-control (second byte: an axis direction or a POV direction), and the control itself
/// (high word: the DirectInput scan code for a keyboard key, the button or axis for a mouse), as
/// <c>ControlSpecification::ToFileNode</c> (11.4186 0x0068AB80, 11.6096 0x0068BB80) writes them out.
/// </summary>
/// <param name="Key">The packed value.</param>
public readonly record struct RetailControl(uint Key)
{
    /// <summary>The device's index in <see cref="RetailKeyMap.Devices"/>.</summary>
    public int DeviceIndex => (int)(Key & 0xFF);

    /// <summary>The sub-control (<c>SubControlIndex</c>: 0 none, 1-2 axis positive/negative, 3-6 POV up/right/down/left).</summary>
    public int SubControl => (int)((Key >> 8) & 0xFF);

    /// <summary>The control: a DirectInput scan code (DIK_*) for a key, a button or axis number for a mouse.</summary>
    public int Semantic => (int)(Key >> 16);

    /// <summary>For logs: <c>device:semantic</c> in hex.</summary>
    public override string ToString() => $"{DeviceIndex}:0x{Semantic:X2}" + (SubControl == 0 ? string.Empty : $"/{SubControl}");
}

/// <summary>One binding of a key map: a control, with meta keys and an activation, that fires an action in an input map.</summary>
/// <param name="InputMapId">The input map (mode) the binding belongs to.</param>
/// <param name="ActionId">The action it fires.</param>
/// <param name="Control">The control.</param>
/// <param name="MetaMode">The meta keys that must be held (<c>m_metamode</c>: bits from <see cref="RetailKeyMap.MetaKeys"/>).</param>
/// <param name="Activation">
/// The activation (<c>m_activation</c>): its low 31 bits are the activation type <see cref="ActivationType"/>; the top
/// bit is a matching flag that <c>QualifiedControl::operator==</c> (11.4186 0x0068A1D0, 11.6096 0x0068B260) tests, which no
/// binding of the retail default map sets.
/// </param>
public readonly record struct RetailBinding(uint InputMapId, uint ActionId, RetailControl Control, uint MetaMode, uint Activation)
{
    /// <summary>
    /// The activation type, as <c>ControlActivationTypeToString</c> (11.4186 0x0068A940, 11.6096 0x0068B940) names it:
    /// 1 Down, 2 Up, 3 Click, 4 Tap, 8 DblClickDown, 0x10 DblClickUp, 0x18 DblClick, 0x20 NearbyDown, 0x40 NearbyUp,
    /// 0x60 MouseDblClick, 0x80 Analog.
    /// </summary>
    public uint ActivationType => Activation & 0x7FFFFFFFu;
}

/// <summary>A meta key: a control that, while held, sets bits of a binding's meta mode.</summary>
/// <param name="Control">The control (for example left Shift).</param>
/// <param name="MetaMode">The meta-mode bits it sets.</param>
public readonly record struct RetailMetaKey(RetailControl Control, uint MetaMode);

/// <summary>
/// A retail key map (MasterInputMap 0x14xxxxxx in <c>client_portal.dat</c>): its devices, meta keys and bindings.
/// </summary>
/// <remarks>
/// <para>
/// Retail's default bindings are KEYMAP enum <see cref="DefaultKeyMapEnum"/> (<c>gmDefault</c>, group 10, map
/// 0x25000013), which resolves to MasterInputMap 0x14000000 named <c>gmDefaultMap</c>. Its layout is
/// <c>CMasterInputMap::Serialize</c> (11.4186 0x0068FCE0, 11.6096 0x00690C30): the name, a GUID, the devices, the meta
/// keys and the input maps; each input map (<c>CInputMap::Serialize</c>, 11.4186 0x0068EE60, 11.6096 0x0068FDB0) is a
/// list of a <c>QualifiedControl</c> (control, meta mode, activation) and an action id. DatReaderWriter reads the same
/// bytes under other names: its <c>QualifiedControl.Key.Key</c>, <c>Key.Modifier</c>, <c>Activation</c> and
/// <c>Unknown</c> are the control, the meta mode, the activation and the action.
/// </para>
/// <para>
/// In <c>gmDefaultMap</c> the keyboard is device 0 and the mouse device 1; the meta keys are left Shift (0x80000000),
/// both Ctrl keys (0x40000000), both Alt keys (0x20000000), both Windows keys (0x10000000) and two mouse buttons; right
/// Shift is no meta key. What each action means, and which input maps are active when, belongs to the input router
/// (R14) and the action map (<see cref="RetailActionMap"/>).
/// </para>
/// </remarks>
public sealed class RetailKeyMap
{
    /// <summary>KEYMAP enum <c>gmDefault</c>: retail's default game bindings.</summary>
    public const uint DefaultKeyMapEnum = 0x10000001;

    private RetailKeyMap(uint id, string name, IReadOnlyList<RetailDeviceType> devices, IReadOnlyList<RetailMetaKey> metaKeys,
        IReadOnlyList<RetailBinding> bindings)
    {
        Id = id;
        Name = name;
        Devices = devices;
        MetaKeys = metaKeys;
        Bindings = bindings;
        InputMapIds = bindings.Select(b => b.InputMapId).Distinct().ToArray();
    }

    /// <summary>The MasterInputMap's DID.</summary>
    public uint Id { get; }

    /// <summary>Its name (<c>gmDefaultMap</c> for the default).</summary>
    public string Name { get; }

    /// <summary>The devices; a control's <see cref="RetailControl.DeviceIndex"/> indexes this list.</summary>
    public IReadOnlyList<RetailDeviceType> Devices { get; }

    /// <summary>The meta keys, in dat order.</summary>
    public IReadOnlyList<RetailMetaKey> MetaKeys { get; }

    /// <summary>Every binding, input map by input map, each in dat order.</summary>
    public IReadOnlyList<RetailBinding> Bindings { get; }

    /// <summary>The input maps that hold bindings, in dat order.</summary>
    public IReadOnlyList<uint> InputMapIds { get; }

    /// <summary>Reads the key map KEYMAP enum <paramref name="keyMapEnum"/> names.</summary>
    /// <exception cref="InvalidDataException">The enum does not resolve, or the MasterInputMap is absent.</exception>
    public static RetailKeyMap Load(DatSet dats, uint keyMapEnum = DefaultKeyMapEnum)
    {
        ArgumentNullException.ThrowIfNull(dats);
        if (!dats.EnumMaps.TryResolve(EnumGroup.KeyMap, keyMapEnum, out var did))
        {
            throw new InvalidDataException($"TODO(retailui-data): KEYMAP enum 0x{keyMapEnum:X8} does not resolve to a MasterInputMap.");
        }

        return dats.TryGetPortal<MasterInputMap>(did, out var map)
            ? FromRecord(map)
            : throw new InvalidDataException($"TODO(retailui-data): MasterInputMap 0x{did:X8} (KEYMAP enum 0x{keyMapEnum:X8}) is not in {DatSet.PortalFileName}.");
    }

    /// <summary>Converts a decoded MasterInputMap.</summary>
    internal static RetailKeyMap FromRecord(MasterInputMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var devices = map.Devices.Select(d => (RetailDeviceType)(byte)d.Type).ToArray();
        var metaKeys = map.MetaKeys.Select(m => new RetailMetaKey(new RetailControl(m.Key), m.Modifier)).ToArray();
        var bindings = map.InputMaps
            .SelectMany(kv => kv.Value.Mappings.Select(q => new RetailBinding(kv.Key, q.Unknown, new RetailControl(q.Key.Key), q.Key.Modifier, q.Activation)))
            .ToArray();
        return new RetailKeyMap(map.Id, map.Name.Value, devices, metaKeys, bindings);
    }

    /// <summary>The type of the device <paramref name="control"/> belongs to (<see cref="RetailDeviceType.Invalid"/> for an unknown index).</summary>
    public RetailDeviceType DeviceOf(RetailControl control) =>
        control.DeviceIndex < Devices.Count ? Devices[control.DeviceIndex] : RetailDeviceType.Invalid;

    /// <summary>
    /// <c>CMasterInputMap::MetaModeFromKey</c> (11.4186 0x0068D850, 11.6096 0x0068E7A0): the meta-mode bits
    /// <paramref name="control"/> sets while held, or 0 when it is no meta key.
    /// </summary>
    public uint MetaModeOf(RetailControl control)
    {
        foreach (var meta in MetaKeys)
        {
            if (meta.Control == control)
            {
                return meta.MetaMode;
            }
        }

        return 0;
    }

    /// <summary>The bindings that fire <paramref name="actionId"/>, optionally only in <paramref name="inputMapId"/>.</summary>
    public IEnumerable<RetailBinding> BindingsFor(uint actionId, uint? inputMapId = null) =>
        Bindings.Where(b => b.ActionId == actionId && (inputMapId is null || b.InputMapId == inputMapId));
}

/// <summary>One action of the action map: how it behaves and how the key-binding screen names it.</summary>
/// <param name="InputMapId">The input map it belongs to.</param>
/// <param name="ActionId">The action.</param>
/// <param name="Toggle">How it behaves while its control is held.</param>
/// <param name="ActionClass">The binding screen's group (<c>m_eActionClass</c>; 0 for actions the user cannot rebind).</param>
/// <param name="NameStringId">The action's name in the action map's string table (<c>m_action_name</c>), or 0.</param>
/// <param name="DescriptionStringId">The action's description in that table, or 0.</param>
public sealed record RetailAction(uint InputMapId, uint ActionId, RetailToggleType Toggle, uint ActionClass, uint NameStringId, uint DescriptionStringId)
{
    /// <summary><c>ActionMap::IsUserBindable</c> (11.4186 0x006854E0, 11.6096 0x00686530): a class and a name.</summary>
    public bool IsUserBindable => ActionClass != 0 && NameStringId != 0;
}

/// <summary>
/// The action map (ActionMap 0x26000000, ACTIONMAP group 8 enum <see cref="DefaultEnum"/>): per input map, each action's
/// toggle type and binding-screen names, plus which input maps' bindings conflict.
/// </summary>
/// <remarks>
/// <c>ActionMap::Serialize</c> (11.4186 0x00685F10, 11.6096 0x00686F60) and <c>ActionMapValue::Serialize</c> (11.4186
/// 0x00684A30, 11.6096 0x00685A80): a magic
/// number, a flag byte, the toggle type, an unused list and the user-binding data (class, name and description string
/// ids). The names live in <see cref="StringTableId"/> (0x23000005, <c>ActionDescription</c>).
/// </remarks>
public sealed class RetailActionMap
{
    /// <summary>ACTIONMAP enum <c>Default</c>.</summary>
    public const uint DefaultEnum = 1;

    private readonly FrozenDictionary<(uint Map, uint Action), RetailAction> _actions;

    private RetailActionMap(uint id, uint stringTableId, IReadOnlyList<RetailAction> actions, IReadOnlyDictionary<uint, IReadOnlyList<uint>> conflicts)
    {
        Id = id;
        StringTableId = stringTableId;
        Actions = actions;
        ConflictingInputMaps = conflicts;
        _actions = actions.ToFrozenDictionary(a => (a.InputMapId, a.ActionId));
    }

    /// <summary>The ActionMap's DID.</summary>
    public uint Id { get; }

    /// <summary>The StringTable the actions' names are in.</summary>
    public uint StringTableId { get; }

    /// <summary>Every action, input map by input map, in dat order.</summary>
    public IReadOnlyList<RetailAction> Actions { get; }

    /// <summary>
    /// For each input map, the input maps whose bindings conflict with its own (<c>ActionMap::GetConflictingInputMaps</c>,
    /// 11.4186 0x00684EE0, 11.6096 0x00685F30).
    /// </summary>
    public IReadOnlyDictionary<uint, IReadOnlyList<uint>> ConflictingInputMaps { get; }

    /// <summary>Reads the action map ACTIONMAP enum <paramref name="actionMapEnum"/> names.</summary>
    /// <exception cref="InvalidDataException">The enum does not resolve, or the ActionMap is absent.</exception>
    public static RetailActionMap Load(DatSet dats, uint actionMapEnum = DefaultEnum)
    {
        ArgumentNullException.ThrowIfNull(dats);
        if (!dats.EnumMaps.TryResolve(new EnumGroup(8), actionMapEnum, out var did))
        {
            throw new InvalidDataException($"TODO(retailui-data): ACTIONMAP enum 0x{actionMapEnum:X8} does not resolve to an ActionMap.");
        }

        if (!dats.TryGetPortal<ActionMap>(did, out var map))
        {
            throw new InvalidDataException($"TODO(retailui-data): ActionMap 0x{did:X8} is not in {DatSet.PortalFileName}.");
        }

        var actions = map.InputMaps
            .SelectMany(m => m.Value.Select(a => new RetailAction(
                m.Key, a.Key, (RetailToggleType)(uint)a.Value.ToggleType, a.Value.UserBinding.ActionClass, a.Value.UserBinding.ActionName,
                a.Value.UserBinding.ActionDescription)))
            .ToArray();
        var conflicts = map.ConflictingMaps.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<uint>)kv.Value.ConflictingInputMaps.ToArray());
        return new RetailActionMap(did, map.StringTableId, actions, conflicts);
    }

    /// <summary>The action <paramref name="actionId"/> of input map <paramref name="inputMapId"/>; false when the map does not define it.</summary>
    public bool TryGetAction(uint inputMapId, uint actionId, [NotNullWhen(true)] out RetailAction? action) =>
        _actions.TryGetValue((inputMapId, actionId), out action);
}

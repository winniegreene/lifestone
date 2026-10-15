using System.Collections.Frozen;
using Lifestone.Data.Dat;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Input;

/// <summary>
/// Retail's key bindings as the input manager searches them: the engine map (KEYMAP <c>Default</c>, 0x14000002, which
/// binds the UI's mouse map 3, the text maps 7 and 8, the dialog keys of map 9 and the wheel map 0xA) merged with the
/// game map (KEYMAP <c>gmDefault</c>, 0x14000000: movement, chat, panels), and the action map's toggle types. Immutable.
/// </summary>
/// <remarks>
/// <para>
/// <b>Merging</b> re-expresses <c>CInputManager_WIN32::AddKeyMap</c> (11.4186 0x006864C0, 11.6096 0x00687510) over
/// <c>CMasterInputMap::Merge</c> (11.4186 0x0068F6F0, 11.6096 0x00690640): the maps' bindings and meta keys are
/// combined, a later map's binding replacing an earlier one with the same map, control, meta keys and activation. The
/// two retail maps list the same devices in the same order (keyboard 0, mouse 1).
/// </para>
/// <para>
/// <b>Matching</b> re-expresses <c>CInputMap::ActionBindingList::FindBestMatch</c> (11.4186 0x0068D460, 11.6096
/// 0x0068E3B0) with <c>QualifiedControl::operator==</c> (11.4186 0x0068A1D0, 11.6096 0x0068B260) and
/// <c>QualifiedControl::IsBetterMatch</c> (11.4186 0x0068A270, 11.6096 0x0068B300): a binding matches a control when
/// its meta keys are all held and its activation shares a bit with the event's; of several, the one with the higher
/// activation wins, then the one that names more of the held meta keys.
/// </para>
/// </remarks>
public sealed class RetailKeyBindings
{
    /// <summary>The keyboard's device index in the retail maps.</summary>
    public const int KeyboardDevice = 0;

    /// <summary>The mouse's device index in the retail maps.</summary>
    public const int MouseDevice = 1;

    /// <summary>KEYMAP enum <c>Default</c>: the engine's bindings (0x14000002).</summary>
    public const uint EngineKeyMapEnum = 1;

    private readonly FrozenDictionary<(uint Map, uint Control), RetailBinding[]> _byControl;
    private readonly FrozenDictionary<uint, uint> _metaModes;
    private readonly Func<uint, uint, RetailToggleType> _toggles;
    private readonly RetailMetaKey[] _metaKeys;

    /// <summary>Bindings from any source (tests, Lifestone's own maps).</summary>
    /// <param name="bindings">The bindings, in priority of replacement: a later binding replaces an earlier identical one.</param>
    /// <param name="metaKeys">The meta keys.</param>
    /// <param name="toggles">The action map's toggle type of (map, action); null makes every action Impulse.</param>
    public RetailKeyBindings(IEnumerable<RetailBinding> bindings, IEnumerable<RetailMetaKey> metaKeys, Func<uint, uint, RetailToggleType>? toggles = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(metaKeys);
        var merged = new List<RetailBinding>();
        foreach (var binding in bindings)
        {
            var same = merged.FindIndex(b => b.InputMapId == binding.InputMapId && b.Control == binding.Control
                && b.MetaMode == binding.MetaMode && b.Activation == binding.Activation);
            if (same >= 0)
            {
                merged[same] = binding;
            }
            else
            {
                merged.Add(binding);
            }
        }

        Bindings = merged;
        _byControl = merged.GroupBy(b => (b.InputMapId, b.Control.Key)).ToFrozenDictionary(g => g.Key, g => g.ToArray());
        _metaKeys = [.. metaKeys];
        var metas = new Dictionary<uint, uint>();
        foreach (var meta in _metaKeys)
        {
            metas[meta.Control.Key] = meta.MetaMode;
        }

        _metaModes = metas.ToFrozenDictionary();
        _toggles = toggles ?? ((_, _) => RetailToggleType.Impulse);
    }

    /// <summary>Every binding after merging.</summary>
    public IReadOnlyList<RetailBinding> Bindings { get; }

    /// <summary>
    /// Retail's bindings from <paramref name="dats"/>: the engine map (KEYMAP 1) then the game map (KEYMAP 0x10000001),
    /// and the action map's toggle types.
    /// </summary>
    public static RetailKeyBindings Load(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        var engine = RetailKeyMap.Load(dats, EngineKeyMapEnum);
        var game = RetailKeyMap.Load(dats);
        var actions = RetailActionMap.Load(dats);
        return new RetailKeyBindings(engine.Bindings.Concat(game.Bindings), engine.MetaKeys.Concat(game.MetaKeys), Toggle);

        RetailToggleType Toggle(uint map, uint action) =>
            actions.TryGetAction(map, action, out var a) ? a.Toggle : RetailToggleType.Invalid;
    }

    /// <summary>
    /// These bindings plus <paramref name="extra"/> (Lifestone's own maps, for example <c>LifestoneHotkeys.Bindings</c>), as a new set: a later binding replaces an earlier one with the same map,
    /// control, meta keys and activation, so extras in input maps of their own leave retail's untouched.
    /// </summary>
    public RetailKeyBindings With(IEnumerable<RetailBinding> extra)
    {
        ArgumentNullException.ThrowIfNull(extra);
        return new RetailKeyBindings(Bindings.Concat(extra), _metaKeys, _toggles);
    }

    /// <summary>The control of keyboard key <paramref name="dik"/> (<c>ControlSpecification</c>: the scan code in the high word).</summary>
    public static RetailControl Keyboard(int dik) => new((uint)(dik << 16) | KeyboardDevice);

    /// <summary>The control of mouse button <paramref name="button"/> (DirectInput's <c>DIMOFS_BUTTON0</c> 0x0C upwards).</summary>
    public static RetailControl Mouse(UiMouseButton button) => new((uint)((0x0C + (int)button) << 16) | MouseDevice);

    /// <summary>The control of a wheel notch: the wheel axis (<c>DIMOFS_Z</c>, 8) in its positive (up) or negative direction.</summary>
    public static RetailControl Wheel(bool up) => new((8u << 16) | ((up ? 1u : 2u) << 8) | MouseDevice);

    /// <summary>
    /// <c>CMasterInputMap::MetaModeFromKey</c> (11.4186 0x0068D850, 11.6096 0x0068E7A0): the meta-mode bits
    /// <paramref name="control"/> sets while held, or 0.
    /// </summary>
    public uint MetaModeOf(RetailControl control) => _metaModes.TryGetValue(control.Key, out var mode) ? mode : 0;

    /// <summary>
    /// <c>ActionMap::GetToggleType</c> as <c>CInputManager::FireActionEvent</c> (11.4186 0x00432720, 11.6096 0x004328C0) uses
    /// it: the action's toggle type, Impulse when the action map does not name one.
    /// </summary>
    public RetailToggleType ToggleOf(uint inputMapId, uint actionId)
    {
        var toggle = _toggles(inputMapId, actionId);
        return toggle == RetailToggleType.Invalid ? RetailToggleType.Impulse : toggle;
    }

    /// <summary>
    /// The best binding of input map <paramref name="inputMapId"/> for <paramref name="control"/> with
    /// <paramref name="metaMode"/> held and event activation <paramref name="activation"/>; false when none matches.
    /// </summary>
    public bool TryFindBestMatch(uint inputMapId, RetailControl control, uint metaMode, uint activation, out RetailBinding best)
    {
        best = default;
        if (!_byControl.TryGetValue((inputMapId, control.Key), out var candidates))
        {
            return false;
        }

        var found = false;
        foreach (var candidate in candidates)
        {
            if (!Matches(candidate, metaMode, activation))
            {
                continue;
            }

            if (!found || IsBetterMatch(metaMode, activation, best, candidate))
            {
                best = candidate;
                found = true;
            }
        }

        return found;
    }

    /// <summary><c>QualifiedControl::operator==</c> for a stored binding against an event: its meta keys held, an activation bit shared.</summary>
    public static bool Matches(RetailBinding binding, uint metaMode, uint activation) =>
        (binding.MetaMode & metaMode) == binding.MetaMode && (binding.Activation & activation) != 0;

    /// <summary>
    /// <c>QualifiedControl::IsBetterMatch</c>: true when <paramref name="candidate"/> beats <paramref name="current"/> for an
    /// event with <paramref name="metaMode"/> and <paramref name="activation"/>: it matches, and its activation is higher,
    /// or equal with more of the held meta keys (numerically more on a tie).
    /// </summary>
    public static bool IsBetterMatch(uint metaMode, uint activation, RetailBinding current, RetailBinding candidate)
    {
        if (current.Control == candidate.Control && current.MetaMode == candidate.MetaMode && current.Activation == candidate.Activation)
        {
            return false;
        }

        var candidateMeta = candidate.MetaMode & metaMode;
        if (candidateMeta != candidate.MetaMode || (candidate.Activation & activation) == 0)
        {
            return false;
        }

        if (candidate.Activation != current.Activation)
        {
            return candidate.Activation > current.Activation;
        }

        var currentMeta = metaMode & current.MetaMode;
        var candidateBits = System.Numerics.BitOperations.PopCount(candidateMeta);
        var currentBits = System.Numerics.BitOperations.PopCount(currentMeta);
        return candidateBits != currentBits ? candidateBits > currentBits : candidateMeta > currentMeta;
    }
}

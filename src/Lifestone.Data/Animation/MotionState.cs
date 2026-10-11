namespace Lifestone.Data.Animation;

/// <summary>A (motion, speed) pair in a <see cref="MotionState"/> list (retail <c>MotionList</c>).</summary>
public readonly record struct MotionListEntry(uint Motion, float SpeedMod);

/// <summary>
/// The motion-table state of one object: current style (stance), substate and its speed, plus the active modifiers
/// and the queued actions. Re-expresses retail <c>MotionState</c> (acclient 11.4186 PORTAL/canim/CMotionPrim.c:
/// <c>add_modifier</c>, <c>add_modifier_no_check</c>, <c>remove_modifier</c>, <c>add_action</c>,
/// <c>remove_action_head</c>, copy constructor). Modifiers are a singly linked list with head insertion; actions are FIFO.
/// </summary>
public sealed class MotionState
{
    private readonly List<MotionListEntry> _modifiers = []; // index 0 = list head
    private readonly List<MotionListEntry> _actions = [];   // index 0 = list head

    public MotionState()
    {
    }

    /// <summary>Retail <c>MotionState::MotionState(const MotionState&amp;)</c>.</summary>
    public MotionState(MotionState other)
    {
        Style = other.Style;
        Substate = other.Substate;
        SubstateMod = other.SubstateMod;
        _modifiers.AddRange(other._modifiers);
        _actions.AddRange(other._actions);
    }

    public uint Style { get; set; }

    public uint Substate { get; set; }

    /// <summary>Speed of the current substate; retail initialises it to 1.0.</summary>
    public float SubstateMod { get; set; } = 1f;

    /// <summary>Active modifiers, head first.</summary>
    public IReadOnlyList<MotionListEntry> Modifiers => _modifiers;

    /// <summary>Queued actions, oldest first.</summary>
    public IReadOnlyList<MotionListEntry> Actions => _actions;

    /// <summary>Retail <c>add_modifier</c>: refuses duplicates and the current substate.</summary>
    public bool AddModifier(uint modifier, float speedMod)
    {
        if (_modifiers.Any(m => m.Motion == modifier) || Substate == modifier)
        {
            return false;
        }

        AddModifierNoCheck(modifier, speedMod);
        return true;
    }

    /// <summary>Retail <c>add_modifier_no_check</c>: inserts at the list head.</summary>
    public void AddModifierNoCheck(uint modifier, float speedMod) => _modifiers.Insert(0, new MotionListEntry(modifier, speedMod));

    /// <summary>Retail <c>remove_modifier(curr, prev)</c>, by list position.</summary>
    public void RemoveModifierAt(int index) => _modifiers.RemoveAt(index);

    public void ClearModifiers() => _modifiers.Clear();

    /// <summary>Retail <c>add_action</c>: appends at the tail.</summary>
    public void AddAction(uint action, float speedMod) => _actions.Add(new MotionListEntry(action, speedMod));

    /// <summary>Retail <c>remove_action_head</c>: returns the removed motion, or 0 when empty.</summary>
    public uint RemoveActionHead()
    {
        if (_actions.Count == 0)
        {
            return 0;
        }

        var motion = _actions[0].Motion;
        _actions.RemoveAt(0);
        return motion;
    }

    public void ClearActions() => _actions.Clear();
}

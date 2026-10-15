using System.Globalization;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Data.Motion;

/// <summary>
/// One queued command of an interpreted motion state as the wire carries it: {u16 command low word, u16 stamp |
/// autonomous &lt;&lt; 15, f32 speed} (docs/protocol.md §5.3; the net track's <c>MotionItem</c> holds the same fields).
/// </summary>
/// <param name="Command">The command's u16 low word, unmapped.</param>
/// <param name="Stamp">The action stamp; only its low 15 bits count.</param>
/// <param name="Autonomous">Bit 15 of the packed stamp word: the client issued the command.</param>
/// <param name="Speed">The speed multiplier.</param>
public readonly record struct WireMotionItem(ushort Command, ushort Stamp, bool Autonomous, float Speed);

/// <summary>
/// An interpreted motion state as the wire carries it (retail <c>InterpretedMotionState::Pack</c> and <c>UnPack</c>):
/// commands as u16 low words, and a null field for each one the flags leave out, which means the default.
/// </summary>
/// <param name="Style">The style low word (flag 0x1); null is <see cref="MotionCommands.NonCombat"/>.</param>
/// <param name="Forward">The forward command low word (flag 0x2); null is <see cref="MotionCommands.Ready"/>.</param>
/// <param name="Sidestep">The sidestep low word (flag 0x8); null is none.</param>
/// <param name="Turn">The turn low word (flag 0x20); null is none.</param>
/// <param name="ForwardSpeed">The forward speed (flag 0x4); null is 1.</param>
/// <param name="SidestepSpeed">The sidestep speed (flag 0x10); null is 1.</param>
/// <param name="TurnSpeed">The turn speed (flag 0x40); null is 1.</param>
public readonly record struct WireInterpretedMotion(
    ushort? Style,
    ushort? Forward,
    ushort? Sidestep,
    ushort? Turn,
    float? ForwardSpeed,
    float? SidestepSpeed,
    float? TurnSpeed);

/// <summary>Which field of a wire motion state held a low word the command table does not know.</summary>
public enum WireMotionField
{
    /// <summary>The interpreted state's style.</summary>
    Style,

    /// <summary>The forward command.</summary>
    Forward,

    /// <summary>The sidestep command.</summary>
    Sidestep,

    /// <summary>The turn command.</summary>
    Turn,

    /// <summary>A queued action (see <see cref="WireMotionFailure.Index"/>).</summary>
    Action,
}

/// <summary>A wire low word that names no 11.6096 motion command, with the field it was found in.</summary>
/// <param name="Field">The field.</param>
/// <param name="Index">The action's index for <see cref="WireMotionField.Action"/>, else 0.</param>
/// <param name="LowWord">The unknown low word.</param>
public readonly record struct WireMotionFailure(WireMotionField Field, int Index, ushort LowWord)
{
    /// <summary>A log line naming the field and the low word.</summary>
    public string Message => Field == WireMotionField.Action
        ? string.Create(CultureInfo.InvariantCulture, $"motion action {Index}: low word 0x{LowWord:X4} names no 11.6096 motion command (the table has {MotionCommandTable.Count})")
        : string.Create(CultureInfo.InvariantCulture, $"motion {Field}: low word 0x{LowWord:X4} names no 11.6096 motion command (the table has {MotionCommandTable.Count})");
}

/// <summary>
/// The wire form of motion states and the full 11.6096 commands: retail <c>InterpretedMotionState::UnPack</c> (11.4186
/// 0x0051F400, 11.6096 0x0051FF10) and <c>Pack</c> (0x0051F1D0 / 0x0051FCE0). The server sends commands as u16 low
/// words; retail rebuilds them through its command-id table, this through <see cref="MotionCommandTable"/>.
/// </summary>
/// <remarks>
/// <para>Retail indexes its table with the low word unchecked, so a word past the table reads whatever follows it. Here
/// an unknown low word fails the conversion, names the field and the word (<see cref="WireMotionFailure"/>) and leaves
/// the target state untouched: nothing is guessed.</para>
/// <para>The stance travels outside the interpreted state, in the MovementData header (docs/protocol.md §5.3).
/// <c>MovementManager::unpack_movement</c> (0x00524440 / 0x00525040) maps it with <see cref="TryCommand"/> and calls
/// <c>DoMotion(stance)</c> only when it differs from the interpreted style, before
/// <see cref="MotionInterpreter.MoveToInterpretedState"/>; that sequence is the avatar set's (AV8).</para>
/// </remarks>
public static class WireMotion
{
    /// <summary>The full command for a wire low word, or false when the 11.6096 table has none.</summary>
    public static bool TryCommand(ushort lowWord, out uint command) => MotionCommandTable.TryFromWire(lowWord, out command);

    /// <summary>
    /// Retail <c>InterpretedMotionState::UnPack</c>: fills <paramref name="into"/> from the wire fields, with the
    /// defaults for the absent ones (style NonCombat, forward Ready, no sidestep or turn, speeds 1), and replaces its
    /// actions with <paramref name="actions"/> (each stamp masked to 15 bits). False, with <paramref name="failure"/>
    /// naming the first unknown low word, when a command is not in the 11.6096 table; <paramref name="into"/> is then
    /// unchanged.
    /// </summary>
    public static bool TryToInterpreted(
        in WireInterpretedMotion wire, ReadOnlySpan<WireMotionItem> actions, InterpretedMotionState into, out WireMotionFailure failure)
    {
        ArgumentNullException.ThrowIfNull(into);
        if (!TryField(wire.Style, MotionCommands.NonCombat, WireMotionField.Style, out var style, out failure)
            || !TryField(wire.Forward, MotionCommands.Ready, WireMotionField.Forward, out var forward, out failure)
            || !TryField(wire.Sidestep, 0, WireMotionField.Sidestep, out var sidestep, out failure)
            || !TryField(wire.Turn, 0, WireMotionField.Turn, out var turn, out failure))
        {
            return false;
        }

        for (var i = 0; i < actions.Length; i++)
        {
            if (!TryCommand(actions[i].Command, out _))
            {
                failure = new WireMotionFailure(WireMotionField.Action, i, actions[i].Command);
                return false;
            }
        }

        into.CurrentStyle = style;
        into.ForwardCommand = forward;
        into.SideStepCommand = sidestep;
        into.TurnCommand = turn;
        into.ForwardSpeed = wire.ForwardSpeed ?? 1f;
        into.SideStepSpeed = wire.SidestepSpeed ?? 1f;
        into.TurnSpeed = wire.TurnSpeed ?? 1f;
        into.Actions.Clear();
        foreach (var action in actions)
        {
            TryCommand(action.Command, out var command);
            into.AddAction(command, action.Speed, (ushort)(action.Stamp & Sequence16.ActionStampMask), action.Autonomous);
        }

        failure = default;
        return true;
    }

    /// <summary>
    /// Retail <c>InterpretedMotionState::Pack</c>'s choice of fields: each field is written only when it differs from
    /// its default (the speeds compared bit for bit with 1.0), and every queued action as {low word, stamp, autonomous,
    /// speed}.
    /// </summary>
    public static WireInterpretedMotion ToWire(InterpretedMotionState state, out WireMotionItem[] actions)
    {
        ArgumentNullException.ThrowIfNull(state);
        actions = new WireMotionItem[state.Actions.Count];
        for (var i = 0; i < actions.Length; i++)
        {
            var a = state.Actions[i];
            actions[i] = new WireMotionItem(MotionCommandTable.ToWire(a.Action), (ushort)(a.Stamp & Sequence16.ActionStampMask), a.Autonomous, a.Speed);
        }

        return new WireInterpretedMotion(
            state.CurrentStyle != MotionCommands.NonCombat ? MotionCommandTable.ToWire(state.CurrentStyle) : null,
            state.ForwardCommand != MotionCommands.Ready ? MotionCommandTable.ToWire(state.ForwardCommand) : null,
            state.SideStepCommand != 0 ? MotionCommandTable.ToWire(state.SideStepCommand) : null,
            state.TurnCommand != 0 ? MotionCommandTable.ToWire(state.TurnCommand) : null,
            IsDefaultSpeed(state.ForwardSpeed) ? null : state.ForwardSpeed,
            IsDefaultSpeed(state.SideStepSpeed) ? null : state.SideStepSpeed,
            IsDefaultSpeed(state.TurnSpeed) ? null : state.TurnSpeed);
    }

    private static bool IsDefaultSpeed(float speed) => BitConverter.SingleToInt32Bits(speed) == BitConverter.SingleToInt32Bits(1f);

    private static bool TryField(ushort? lowWord, uint absent, WireMotionField field, out uint command, out WireMotionFailure failure)
    {
        failure = default;
        if (lowWord is not { } word)
        {
            command = absent;
            return true;
        }

        if (TryCommand(word, out command))
        {
            return true;
        }

        failure = new WireMotionFailure(field, 0, word);
        return false;
    }
}

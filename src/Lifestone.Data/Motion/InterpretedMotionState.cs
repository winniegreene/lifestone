// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Animation/InterpretedMotionState.cs (AGPL-3.0)
// with changes: data and defaults only (the fields, InitDefaults and a Clone); the behaviour (ApplyMotion, RemoveMotion
// and the rest) is added by the motion owner in its own partial files.
using Lifestone.Data.Animation;

namespace Lifestone.Data.Motion;

/// <summary>
/// Retail <c>InterpretedMotionState</c>: the motion state the motion table plays (the stance, and the forward, sidestep
/// and turn commands with their speeds, plus the queued actions), derived from a <see cref="RawMotionState"/> or sent
/// by the server for other objects. Unlike the raw state it has no hold keys: a held run key is already a run command.
/// </summary>
/// <remarks>
/// A partial class: this file holds the data and the defaults only. Defaults follow ACE's <c>InitDefaults</c>
/// (InterpretedMotionState.cs:72-83): style <see cref="MotionCommands.NonCombat"/> (0x8000003D), forward
/// <see cref="MotionCommands.Ready"/> (0x41000003), no sidestep or turn, every speed 1 and no actions.
/// </remarks>
public sealed partial class InterpretedMotionState
{
    /// <summary>A state with the defaults (<see cref="InitDefaults"/>).</summary>
    public InterpretedMotionState()
    {
        InitDefaults();
    }

    /// <summary>The stance (style) command, e.g. <see cref="MotionCommands.NonCombat"/>.</summary>
    public uint CurrentStyle { get; set; }

    /// <summary>The forward (substate) command, e.g. <see cref="MotionCommands.Ready"/> or <see cref="MotionCommands.RunForward"/>.</summary>
    public uint ForwardCommand { get; set; }

    /// <summary>Speed multiplier of the forward command.</summary>
    public float ForwardSpeed { get; set; }

    /// <summary>The sidestep command, or 0.</summary>
    public uint SideStepCommand { get; set; }

    /// <summary>Speed multiplier of the sidestep command.</summary>
    public float SideStepSpeed { get; set; }

    /// <summary>The turn command, or 0.</summary>
    public uint TurnCommand { get; set; }

    /// <summary>Speed multiplier of the turn command.</summary>
    public float TurnSpeed { get; set; }

    /// <summary>Queued actions, oldest first (retail <c>actions</c>).</summary>
    public List<MotionActionNode> Actions { get; } = [];

    /// <summary>
    /// Retail <c>InterpretedMotionState::InitDefaults</c> (ACE InterpretedMotionState.cs:72-83): resets every field to
    /// its default and empties <see cref="Actions"/> (keeping its storage).
    /// </summary>
    public void InitDefaults()
    {
        Actions.Clear();
        CurrentStyle = MotionCommands.NonCombat;
        ForwardCommand = MotionCommands.Ready;
        ForwardSpeed = 1f;
        SideStepCommand = 0;
        SideStepSpeed = 1f;
        TurnCommand = 0;
        TurnSpeed = 1f;
    }

    /// <summary>An independent copy: the same values and a new <see cref="Actions"/> list with the same entries.</summary>
    public InterpretedMotionState Clone()
    {
        var copy = new InterpretedMotionState
        {
            CurrentStyle = CurrentStyle,
            ForwardCommand = ForwardCommand,
            ForwardSpeed = ForwardSpeed,
            SideStepCommand = SideStepCommand,
            SideStepSpeed = SideStepSpeed,
            TurnCommand = TurnCommand,
            TurnSpeed = TurnSpeed,
        };
        copy.Actions.AddRange(Actions);
        return copy;
    }
}

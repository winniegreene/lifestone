// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Animation/RawMotionState.cs (AGPL-3.0)
// with changes: data and defaults only (the fields, InitDefaults and a Clone); the behaviour (ApplyMotion, RemoveMotion
// and the rest) is added by the motion owner in its own partial files.
using Lifestone.Data.Animation;

namespace Lifestone.Data.Motion;

/// <summary>
/// Retail <c>RawMotionState</c>: the motion state as the player's input expresses it (the run key, the stance, and the
/// forward, sidestep and turn commands with their hold keys and speeds, plus the queued actions). It is what a
/// MoveToState message carries; <see cref="InterpretedMotionState"/> is what the motion table plays.
/// </summary>
/// <remarks>
/// A partial class: this file holds the data and the defaults only. Defaults follow ACE's <c>InitDefaults</c>
/// (RawMotionState.cs:100-115): hold key <see cref="HoldKey.None"/>, style <see cref="MotionCommands.NonCombat"/>
/// (0x8000003D), forward <see cref="MotionCommands.Ready"/> (0x41000003), no sidestep or turn, every per-motion hold
/// key <see cref="HoldKey.Invalid"/>, every speed 1 and no actions.
/// </remarks>
public sealed partial class RawMotionState
{
    /// <summary>A state with the defaults (<see cref="InitDefaults"/>).</summary>
    public RawMotionState()
    {
        InitDefaults();
    }

    /// <summary>The hold key in effect (retail <c>current_holdkey</c>).</summary>
    public HoldKey CurrentHoldKey { get; set; }

    /// <summary>The stance (style) command, e.g. <see cref="MotionCommands.NonCombat"/>.</summary>
    public uint CurrentStyle { get; set; }

    /// <summary>The forward (substate) command, e.g. <see cref="MotionCommands.Ready"/> or <see cref="MotionCommands.WalkForward"/>.</summary>
    public uint ForwardCommand { get; set; }

    /// <summary>The hold key the forward command was issued with.</summary>
    public HoldKey ForwardHoldKey { get; set; }

    /// <summary>Speed multiplier of the forward command.</summary>
    public float ForwardSpeed { get; set; }

    /// <summary>The sidestep command, or 0.</summary>
    public uint SideStepCommand { get; set; }

    /// <summary>The hold key the sidestep command was issued with.</summary>
    public HoldKey SideStepHoldKey { get; set; }

    /// <summary>Speed multiplier of the sidestep command.</summary>
    public float SideStepSpeed { get; set; }

    /// <summary>The turn command, or 0.</summary>
    public uint TurnCommand { get; set; }

    /// <summary>The hold key the turn command was issued with.</summary>
    public HoldKey TurnHoldKey { get; set; }

    /// <summary>Speed multiplier of the turn command.</summary>
    public float TurnSpeed { get; set; }

    /// <summary>Queued actions, oldest first (retail <c>actions</c>).</summary>
    public List<MotionActionNode> Actions { get; } = [];

    /// <summary>
    /// Retail <c>RawMotionState::InitDefaults</c> (ACE RawMotionState.cs:100-115): resets every field to its default
    /// and empties <see cref="Actions"/> (keeping its storage).
    /// </summary>
    public void InitDefaults()
    {
        Actions.Clear();
        CurrentHoldKey = HoldKey.None;
        CurrentStyle = MotionCommands.NonCombat;
        ForwardCommand = MotionCommands.Ready;
        ForwardHoldKey = HoldKey.Invalid;
        ForwardSpeed = 1f;
        SideStepCommand = 0;
        SideStepHoldKey = HoldKey.Invalid;
        SideStepSpeed = 1f;
        TurnCommand = 0;
        TurnHoldKey = HoldKey.Invalid;
        TurnSpeed = 1f;
    }

    /// <summary>An independent copy: the same values and a new <see cref="Actions"/> list with the same entries.</summary>
    public RawMotionState Clone()
    {
        var copy = new RawMotionState
        {
            CurrentHoldKey = CurrentHoldKey,
            CurrentStyle = CurrentStyle,
            ForwardCommand = ForwardCommand,
            ForwardHoldKey = ForwardHoldKey,
            ForwardSpeed = ForwardSpeed,
            SideStepCommand = SideStepCommand,
            SideStepHoldKey = SideStepHoldKey,
            SideStepSpeed = SideStepSpeed,
            TurnCommand = TurnCommand,
            TurnHoldKey = TurnHoldKey,
            TurnSpeed = TurnSpeed,
        };
        copy.Actions.AddRange(Actions);
        return copy;
    }
}

// Ported from ACEmulator/ACE@95ef426d Source/ACE.Entity/Enum/HoldKey.cs (AGPL-3.0)
// with changes: data and defaults only; XML docs added.
namespace Lifestone.Data.Motion;

/// <summary>
/// Retail <c>HoldKey</c>: which "hold" modifier a raw motion was issued with. <see cref="Run"/> is the run key, which
/// turns a walk into a run; <see cref="None"/> is no modifier; <see cref="Invalid"/> is no hold key of the motion's own
/// (the per-motion default, and what ACE's <c>RawMotionState.ApplyMotion</c> stores when
/// <c>MovementParameters.SetHoldKey</c> is set).
/// </summary>
public enum HoldKey : uint
{
    /// <summary>No hold key of the motion's own (the per-motion default of <see cref="RawMotionState"/>).</summary>
    Invalid = 0,

    /// <summary>No modifier held (the default <see cref="RawMotionState.CurrentHoldKey"/>).</summary>
    None = 1,

    /// <summary>The run key is held.</summary>
    Run = 2,
}

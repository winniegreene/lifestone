// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Animation/MovementParamFlags.cs (AGPL-3.0)
// with changes: the flag names and values only; the parameters and their defaults are re-expressed from retail
// MovementParameters::MovementParameters, whose defaults differ from ACE's constructor.
namespace Lifestone.Data.Motion;

/// <summary>
/// The bits of retail <c>MovementParameters::bitfield</c>. The motion interpreter reads <see cref="SetHoldKey"/>,
/// <see cref="Autonomous"/>, <see cref="ModifyRawState"/>, <see cref="ModifyInterpretedState"/>,
/// <see cref="CancelMoveTo"/> and <see cref="DisableJumpDuringLink"/>; the rest steer MoveTo (AV14).
/// </summary>
[Flags]
public enum MovementParameterFlags : uint
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>A MoveTo may walk.</summary>
    CanWalk = 0x00001,

    /// <summary>A MoveTo may run.</summary>
    CanRun = 0x00002,

    /// <summary>A MoveTo may sidestep.</summary>
    CanSidestep = 0x00004,

    /// <summary>A MoveTo may walk backwards.</summary>
    CanWalkBackwards = 0x00008,

    /// <summary>A MoveTo may charge.</summary>
    CanCharge = 0x00010,

    /// <summary>A MoveTo fails when it has to walk.</summary>
    FailWalk = 0x00020,

    /// <summary>A MoveTo turns to <see cref="MovementParameters.DesiredHeading"/> on arrival.</summary>
    UseFinalHeading = 0x00040,

    /// <summary>A MoveTo sticks to its target on arrival.</summary>
    Sticky = 0x00080,

    /// <summary>A MoveTo moves away from its target.</summary>
    MoveAway = 0x00100,

    /// <summary>A MoveTo moves towards its target.</summary>
    MoveTowards = 0x00200,

    /// <summary>A MoveTo measures with the objects' spheres.</summary>
    UseSpheres = 0x00400,

    /// <summary><c>CMotionInterp::DoMotion</c> applies <see cref="MovementParameters.HoldKeyToApply"/> first.</summary>
    SetHoldKey = 0x00800,

    /// <summary>The client issued the motion on its own (an action's autonomous bit).</summary>
    Autonomous = 0x01000,

    /// <summary>A successful <c>DoMotion</c>/<c>StopMotion</c> updates the raw motion state.</summary>
    ModifyRawState = 0x02000,

    /// <summary>A successful interpreted motion updates the interpreted motion state.</summary>
    ModifyInterpretedState = 0x04000,

    /// <summary>The motion cancels a MoveTo first.</summary>
    CancelMoveTo = 0x08000,

    /// <summary>A MoveTo stops completely on arrival.</summary>
    StopCompletely = 0x10000,

    /// <summary>The queued motion forbids a jump until it completes (<c>CMotionInterp::DoInterpretedMotion</c>).</summary>
    DisableJumpDuringLink = 0x20000,
}

/// <summary>
/// Retail <c>MovementParameters</c>: how one motion is applied (its speed, hold key, the state it modifies) and, for a
/// MoveTo, how to get there. <c>new MovementParameters()</c> gives retail's defaults
/// (<c>MovementParameters::MovementParameters</c>, 11.4186 0x00524380, 11.6096 0x00524F80):
/// <see cref="DefaultFlags"/>, distance to object 0.6, minimum distance 0, fail distance <see cref="float.MaxValue"/>,
/// heading 0, speed 1, walk/run threshold 15, context 0, hold key <see cref="HoldKey.Invalid"/>, action stamp 0.
/// </summary>
/// <remarks>
/// <para><c>default(MovementParameters)</c> is all zeros (speed 0, no flags), which is not retail's default: always
/// start from <c>new MovementParameters()</c> and use <c>with</c>.</para>
/// <para>ACE's constructor differs from retail in two defaults: it sets <see cref="MovementParameterFlags.CanCharge"/>
/// and uses a walk/run threshold of 1. Retail's default bitfield is 0x1EE0F, without CanCharge.</para>
/// </remarks>
public readonly partial record struct MovementParameters()
{
    /// <summary>
    /// Retail's default bitfield (0x1EE0F): CanWalk, CanRun, CanSidestep, CanWalkBackwards, MoveTowards, UseSpheres,
    /// SetHoldKey, ModifyRawState, ModifyInterpretedState, CancelMoveTo and StopCompletely.
    /// </summary>
    public const MovementParameterFlags DefaultFlags =
        MovementParameterFlags.CanWalk | MovementParameterFlags.CanRun | MovementParameterFlags.CanSidestep
        | MovementParameterFlags.CanWalkBackwards | MovementParameterFlags.MoveTowards | MovementParameterFlags.UseSpheres
        | MovementParameterFlags.SetHoldKey | MovementParameterFlags.ModifyRawState
        | MovementParameterFlags.ModifyInterpretedState | MovementParameterFlags.CancelMoveTo
        | MovementParameterFlags.StopCompletely;

    /// <summary>The flags (retail <c>bitfield</c>).</summary>
    public MovementParameterFlags Flags { get; init; } = DefaultFlags;

    /// <summary>A MoveTo arrives within this distance of its target (retail <c>distance_to_object</c>).</summary>
    public float DistanceToObject { get; init; } = 0.6f;

    /// <summary>The minimum distance of a MoveTo (retail <c>min_distance</c>).</summary>
    public float MinDistance { get; init; }

    /// <summary>A MoveTo fails beyond this distance (retail <c>fail_distance</c>).</summary>
    public float FailDistance { get; init; } = float.MaxValue;

    /// <summary>The heading a turn or a MoveTo aims for, in degrees (retail <c>desired_heading</c>).</summary>
    public float DesiredHeading { get; init; }

    /// <summary>The speed multiplier of the motion (retail <c>speed</c>).</summary>
    public float Speed { get; init; } = 1f;

    /// <summary>Below this distance a MoveTo walks instead of running (retail <c>walk_run_threshhold</c>).</summary>
    public float WalkRunThreshold { get; init; } = 15f;

    /// <summary>The context the motion is queued with (retail <c>context_id</c>).</summary>
    public uint ContextId { get; init; }

    /// <summary>The hold key the motion was issued with (retail <c>hold_key_to_apply</c>).</summary>
    public HoldKey HoldKeyToApply { get; init; } = HoldKey.Invalid;

    /// <summary>The action stamp an action is stored with (retail <c>action_stamp</c>; 15 bits).</summary>
    public ushort ActionStamp { get; init; }

    /// <summary>True when <see cref="MovementParameterFlags.SetHoldKey"/> is set.</summary>
    public bool SetHoldKey => (Flags & MovementParameterFlags.SetHoldKey) != 0;

    /// <summary>True when <see cref="MovementParameterFlags.Autonomous"/> is set.</summary>
    public bool Autonomous => (Flags & MovementParameterFlags.Autonomous) != 0;

    /// <summary>True when <see cref="MovementParameterFlags.ModifyRawState"/> is set.</summary>
    public bool ModifyRawState => (Flags & MovementParameterFlags.ModifyRawState) != 0;

    /// <summary>True when <see cref="MovementParameterFlags.ModifyInterpretedState"/> is set.</summary>
    public bool ModifyInterpretedState => (Flags & MovementParameterFlags.ModifyInterpretedState) != 0;

    /// <summary>True when <see cref="MovementParameterFlags.CancelMoveTo"/> is set.</summary>
    public bool CancelMoveTo => (Flags & MovementParameterFlags.CancelMoveTo) != 0;

    /// <summary>True when <see cref="MovementParameterFlags.DisableJumpDuringLink"/> is set.</summary>
    public bool DisableJumpDuringLink => (Flags & MovementParameterFlags.DisableJumpDuringLink) != 0;

    /// <summary>A copy with <paramref name="flag"/> set or cleared.</summary>
    public MovementParameters WithFlag(MovementParameterFlags flag, bool set) =>
        this with { Flags = set ? Flags | flag : Flags & ~flag };
}

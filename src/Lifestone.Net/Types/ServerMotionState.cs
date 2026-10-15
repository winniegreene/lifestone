using System.Collections.Immutable;

namespace Lifestone.Net.Types;

/// <summary>
/// The body of a server MovementData (docs/protocol.md §5.3), as UpdateMotion (F74C) and a CreateObject PhysicsDesc
/// carry it: the movement type, the motion flags, the current stance, then either the interpreted motion state (type 0)
/// or a MoveTo/TurnTo body (types 6-9).
/// </summary>
/// <remarks>
/// Motion commands here are the wire's u16 low words, kept raw. Rebuild the full 11.6096 commands with
/// <c>Lifestone.Data.Animation.MotionCommandTable</c>, as retail's <c>InterpretedMotionState::UnPack</c> (11.4186
/// 0x0051F400) does through its command-id table. A null field was not flagged on the wire, which means "the default"
/// (retail fills in the defaults on unpack; the consumer does the same).
/// </remarks>
public sealed record ServerMotionState
{
    /// <summary>The movement type: <see cref="Types.MovementType.Interpreted"/> or one of the MoveTo/TurnTo types.</summary>
    public required MovementType MovementType { get; init; }

    /// <summary>The motion flags (StickToObject, StandingLongJump).</summary>
    public MotionFlags MotionFlags { get; init; }

    /// <summary>The current stance's low word (u16).</summary>
    public ushort Stance { get; init; }

    /// <summary>The interpreted state's style low word (flag 0x1).</summary>
    public ushort? Style { get; init; }

    /// <summary>The forward command's low word (flag 0x2).</summary>
    public ushort? Forward { get; init; }

    /// <summary>The sidestep command's low word (flag 0x8).</summary>
    public ushort? Sidestep { get; init; }

    /// <summary>The turn command's low word (flag 0x20).</summary>
    public ushort? Turn { get; init; }

    /// <summary>The forward speed (flag 0x4).</summary>
    public float? ForwardSpeed { get; init; }

    /// <summary>The sidestep speed (flag 0x10).</summary>
    public float? SidestepSpeed { get; init; }

    /// <summary>The turn speed (flag 0x40).</summary>
    public float? TurnSpeed { get; init; }

    /// <summary>The queued commands (actions and emotes), in wire order.</summary>
    public ImmutableArray<MotionItem> Commands { get; init; } = [];

    /// <summary>The object to stick to (after the alignment, when <see cref="MotionFlags.StickToObject"/> is set).</summary>
    public uint? StickyGuid { get; init; }

    /// <summary>The MoveToObject (6) or MoveToPosition (7) body.</summary>
    public MoveToData? MoveTo { get; init; }

    /// <summary>The TurnToObject (8) or TurnToHeading (9) body.</summary>
    public TurnToData? TurnTo { get; init; }
}

/// <summary>The movement type byte of a server MovementData.</summary>
public enum MovementType : byte
{
    /// <summary>0: an interpreted motion state (retail calls the type Invalid; it is the common animation case).</summary>
    Interpreted = 0,

    /// <summary>6: move to an object.</summary>
    MoveToObject = 6,

    /// <summary>7: move to a position.</summary>
    MoveToPosition = 7,

    /// <summary>8: turn to face an object.</summary>
    TurnToObject = 8,

    /// <summary>9: turn to a heading.</summary>
    TurnToHeading = 9,
}

/// <summary>The motion flags byte of a server MovementData.</summary>
[Flags]
public enum MotionFlags : byte
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>A sticky GUID follows the interpreted state.</summary>
    StickToObject = 0x1,

    /// <summary>The object is in a standing long jump.</summary>
    StandingLongJump = 0x2,
}

/// <summary>
/// One command of an interpreted motion state (ACE <c>MotionItem</c>): {u16 command low word, u16 stamp | autonomous
/// &lt;&lt; 15, f32 speed} on the wire.
/// </summary>
/// <param name="Command">The command's low word, raw (map it with <c>MotionCommandTable</c>).</param>
/// <param name="Stamp">The 15-bit action stamp (the packed word masked with 0x7FFF).</param>
/// <param name="Autonomous">Bit 15 of the packed word: the client issued it.</param>
/// <param name="Speed">The speed multiplier.</param>
public readonly record struct MotionItem(ushort Command, ushort Stamp, bool Autonomous, float Speed);

/// <summary>
/// A MoveToObject (6) or MoveToPosition (7) body (ACE <c>Motion/MoveToObject.cs</c>, <c>MoveToPosition.cs</c>): the
/// target (type 6 only), the target's origin, the movement parameters and the run rate.
/// </summary>
/// <param name="TargetGuid">The object to move to (type 6), else null.</param>
/// <param name="OriginCellId">The cell of the target's origin.</param>
/// <param name="Origin">The target's origin in that cell's landblock frame.</param>
/// <param name="Parameters">The movement parameters.</param>
/// <param name="RunRate">The mover's run rate.</param>
public readonly record struct MoveToData(uint? TargetGuid, uint OriginCellId, System.Numerics.Vector3 Origin, MoveToParameters Parameters, float RunRate);

/// <summary>Retail <c>MovementParameters</c> as a MoveTo body packs them (ACE <c>Motion/MoveToParameters.cs</c>).</summary>
/// <param name="Flags">The MovementParams bits (can walk, can run, move towards, ...).</param>
/// <param name="DistanceToObject">Move to within this distance of the target.</param>
/// <param name="MinDistance">The minimum distance to move.</param>
/// <param name="FailDistance">The distance at which the movement fails.</param>
/// <param name="Speed">The speed multiplier.</param>
/// <param name="WalkRunThreshold">Below this distance, walk instead of run.</param>
/// <param name="DesiredHeading">The heading to turn to.</param>
public readonly record struct MoveToParameters(uint Flags, float DistanceToObject, float MinDistance, float FailDistance, float Speed, float WalkRunThreshold, float DesiredHeading);

/// <summary>
/// A TurnToObject (8) or TurnToHeading (9) body (ACE <c>Motion/TurnToObject.cs</c>, <c>TurnToHeading.cs</c>).
/// </summary>
/// <param name="TargetGuid">The object to face (type 8), else null.</param>
/// <param name="ObjectHeading">The heading of the object to face (type 8), else null.</param>
/// <param name="Parameters">The turn parameters.</param>
public readonly record struct TurnToData(uint? TargetGuid, float? ObjectHeading, TurnToParameters Parameters);

/// <summary>Retail turn parameters as a TurnTo body packs them (ACE <c>Motion/TurnToParameters.cs</c>).</summary>
/// <param name="Flags">The MovementParams bits.</param>
/// <param name="Speed">The turn speed.</param>
/// <param name="DesiredHeading">The heading to turn to.</param>
public readonly record struct TurnToParameters(uint Flags, float Speed, float DesiredHeading);

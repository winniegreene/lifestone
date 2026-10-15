using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Data.Motion;

/// <summary>The movement type byte of a server MovementData (retail <c>MovementTypes::Type</c>).</summary>
public enum WireMovementType : byte
{
    /// <summary>0: an interpreted motion state (retail's <c>Invalid</c>, the common animation case).</summary>
    InterpretedMotion = 0,

    /// <summary>6: move to an object.</summary>
    MoveToObject = 6,

    /// <summary>7: move to a position.</summary>
    MoveToPosition = 7,

    /// <summary>8: turn to face an object.</summary>
    TurnToObject = 8,

    /// <summary>9: turn to a heading.</summary>
    TurnToHeading = 9,
}

/// <summary>
/// The body of a MoveToObject (6) or MoveToPosition (7) movement as retail <c>MovementManager::unpack_movement</c> reads
/// it: the target (type 6 only), the target's origin (<c>Position::UnPackOrigin</c>: a cell and an origin, no
/// rotation), the parameters (<c>MovementParameters::UnPackNet</c>) and the mover's run rate.
/// </summary>
/// <param name="TargetId">The object to move to (type 6), or 0.</param>
/// <param name="Origin">The target's position: its cell and origin, with the identity rotation.</param>
/// <param name="Parameters">The parameters, see <see cref="MovementParameters.FromMoveToWire"/>.</param>
/// <param name="RunRate">The mover's run rate, which becomes its interpreter's <see cref="MotionInterpreter.MyRunRate"/>.</param>
public readonly record struct WireMoveTo(uint TargetId, CellFrame Origin, MovementParameters Parameters, float RunRate);

/// <summary>
/// The body of a TurnToObject (8) or TurnToHeading (9) movement as <c>unpack_movement</c> reads it.
/// </summary>
/// <param name="TargetId">The object to face (type 8), or 0.</param>
/// <param name="ObjectHeading">For type 8, the heading to turn to when the object is unknown here; else 0.</param>
/// <param name="Parameters">The parameters, see <see cref="MovementParameters.FromTurnToWire"/>.</param>
public readonly record struct WireTurnTo(uint TargetId, float ObjectHeading, MovementParameters Parameters);

/// <summary>
/// A whole server MovementData (docs/protocol.md §5.3), as UpdateMotion (F74C) and a CreateObject PhysicsDesc carry
/// it, in Lifestone.Data's terms: the type, the two flag bits, the stance's low word, and the body of the type. The
/// queued actions of a type-0 state travel beside it as a span of <see cref="WireMotionItem"/>. The net track's
/// <c>ServerMotionState</c> maps to it field for field (the avatars' <c>AvatarWire</c>).
/// </summary>
/// <param name="Type">The movement type.</param>
/// <param name="Stance">The current stance's low word (the MovementData header).</param>
/// <param name="StickToObject">Flag 0x1: a sticky object id follows the interpreted state.</param>
/// <param name="StandingLongJump">Flag 0x2 (header bit 0x200): the object is in a standing long jump.</param>
/// <param name="State">The interpreted state of type 0.</param>
/// <param name="StickyObjectId">The object to stick to (type 0 with <paramref name="StickToObject"/>), or 0.</param>
/// <param name="MoveTo">The body of types 6 and 7.</param>
/// <param name="TurnTo">The body of types 8 and 9.</param>
public readonly record struct WireMovement(
    WireMovementType Type,
    ushort Stance,
    bool StickToObject,
    bool StandingLongJump,
    WireInterpretedMotion State,
    uint StickyObjectId = 0,
    WireMoveTo? MoveTo = null,
    WireTurnTo? TurnTo = null)
{
    /// <summary>A type-0 movement with <paramref name="state"/> and the stance <paramref name="stance"/>.</summary>
    public static WireMovement Interpreted(ushort stance, in WireInterpretedMotion state, uint stickyObjectId = 0, bool standingLongJump = false) =>
        new(WireMovementType.InterpretedMotion, stance, stickyObjectId != 0, standingLongJump, state, stickyObjectId);
}

/// <summary>What <see cref="MovementManager.UnpackMovement"/> did with a movement.</summary>
public enum MovementUnpackResult
{
    /// <summary>Applied (retail returns 1).</summary>
    Applied,

    /// <summary>
    /// A low word (the stance, a command or an action) names no 11.6096 command; nothing was applied (retail would read
    /// past its command table). The failure is reported.
    /// </summary>
    UnknownCommand,

    /// <summary>A movement type other than 0 and 6-9; nothing was applied (retail returns 0).</summary>
    UnknownType,

    /// <summary>The body the type needs is missing (a malformed mapping); nothing was applied.</summary>
    MissingBody,
}

/// <summary>Converters from the wire's MoveTo and TurnTo parameter blocks.</summary>
public readonly partial record struct MovementParameters
{
    /// <summary>
    /// Retail <c>MovementParameters::UnPackNet</c> for types 6 and 7 (11.4186 0x0052AC50, 11.6096 0x0052B860): the
    /// bitfield, distance to object, minimum distance, fail distance, speed, walk/run threshold and desired heading are
    /// read; every other field keeps the constructor's value (context 0, hold key <see cref="HoldKey.Invalid"/>, action
    /// stamp 0).
    /// </summary>
    public static MovementParameters FromMoveToWire(
        uint flags, float distanceToObject, float minDistance, float failDistance, float speed, float walkRunThreshold, float desiredHeading) =>
        new()
        {
            Flags = (MovementParameterFlags)flags,
            DistanceToObject = distanceToObject,
            MinDistance = minDistance,
            FailDistance = failDistance,
            Speed = speed,
            WalkRunThreshold = walkRunThreshold,
            DesiredHeading = desiredHeading,
        };

    /// <summary>
    /// <c>MovementParameters::UnPackNet</c> for types 8 and 9: the bitfield, speed and desired heading are read; every
    /// other field keeps the constructor's value (distance to object 0.6, fail distance <see cref="float.MaxValue"/>,
    /// walk/run threshold 15).
    /// </summary>
    public static MovementParameters FromTurnToWire(uint flags, float speed, float desiredHeading) =>
        new()
        {
            Flags = (MovementParameterFlags)flags,
            Speed = speed,
            DesiredHeading = desiredHeading,
        };
}

/// <summary>Helpers for MoveTo origins.</summary>
public static class WireMovementPositions
{
    /// <summary>A <c>Position::UnPackOrigin</c> position: the cell and origin, the identity rotation.</summary>
    public static CellFrame Origin(uint cellId, Vector3 origin) => new(cellId, new AcFrame(origin, Quaternion.Identity));
}

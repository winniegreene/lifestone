using Lifestone.Data.World;

namespace Lifestone.Data.Motion;

/// <summary>
/// What the position managers read from their object (retail <c>CPhysicsObj</c>) at one call: its position, whether
/// it touches a surface, whether it is the client's player, the speeds its motion interpreter allows and the object
/// it sticks to. A snapshot the host builds per call, so the managers keep no reference to the object.
/// </summary>
/// <param name="Position">The object's position (retail <c>m_position</c>).</param>
/// <param name="HasContact">Retail <c>transient_state</c> Contact (0x1): interpolation moves only an object in contact.</param>
/// <param name="IsThePlayer">The object is the client's player (retail <c>this == CPhysicsObj::player_object</c>).</param>
/// <param name="MaxSpeed">The interpreter's <see cref="MotionInterpreter.GetMaxSpeed"/>.</param>
/// <param name="AdjustedMaxSpeed">The interpreter's <see cref="MotionInterpreter.GetAdjustedMaxSpeed"/>.</param>
/// <param name="StickyObjectId">The object it sticks to (retail <c>get_sticky_object_id</c>), or 0 (AV14).</param>
public readonly record struct PositionContext(
    CellFrame Position,
    bool HasContact,
    bool IsThePlayer,
    float MaxSpeed,
    float AdjustedMaxSpeed,
    uint StickyObjectId = 0)
{
    /// <summary>
    /// A context with the speeds of <paramref name="motion"/>. Without an interpreter both speeds are
    /// <see cref="MotionInterpreter.RunAnimSpeed"/>: retail's <c>CPhysicsObj::get_minterp</c> creates a default one on
    /// demand, and a default interpreter without a weenie allows 4 m/s.
    /// </summary>
    public static PositionContext For(in CellFrame position, bool hasContact, bool isThePlayer, MotionInterpreter? motion, uint stickyObjectId = 0) =>
        new(
            position,
            hasContact,
            isThePlayer,
            motion?.GetMaxSpeed() ?? MotionInterpreter.RunAnimSpeed,
            motion?.GetAdjustedMaxSpeed() ?? MotionInterpreter.RunAnimSpeed,
            stickyObjectId);
}

/// <summary>
/// A blip the interpolation manager asks for: place the object at <see cref="Position"/> with
/// <c>CPhysicsObj::SetPositionSimple</c> (sliding), then report the result with
/// <see cref="InterpolationManager.CompleteBlip"/>.
/// </summary>
/// <param name="Position">Where to put the object.</param>
public readonly record struct InterpolationBlip(CellFrame Position);

/// <summary>What <see cref="InterpolationManager.InterpolateTo"/> did with a new position.</summary>
public enum InterpolateToOutcome
{
    /// <summary>Queued as a node to move to.</summary>
    Queued,

    /// <summary>The object is already within 0.05 m: interpolation stopped (and the heading may be set).</summary>
    Arrived,

    /// <summary>Beyond the autonomy blip distance: queued, and the next <c>UseTime</c> blips to it.</summary>
    BlipArmed,
}

/// <summary>The result of <see cref="InterpolationManager.InterpolateTo"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="SetHeading">
/// For <see cref="InterpolateToOutcome.Arrived"/> without keep-heading, the heading in degrees the host must give the
/// object (retail <c>CPhysicsObj::set_heading(h, send_event 1)</c>); otherwise null.
/// </param>
public readonly record struct InterpolateToResult(InterpolateToOutcome Outcome, float? SetHeading = null);

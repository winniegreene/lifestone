using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Avatars;

/// <summary>
/// A placeholder player body without collision or gravity (AV10): the test double of the physics track's player object,
/// and a stand-in for offline tools until that object exists. It hosts the player's <see cref="MotionInterpreter"/> and
/// <see cref="MovementManager"/> on the player avatar's part array, steps that part array with retail's object clock,
/// moves by the animation's root motion (scaled by nothing, zeroed off walkable ground) and the position managers, and
/// takes contact from whoever drives it (<see cref="SetContact"/>). It is not retail physics: no transition, no gravity,
/// the velocity a jump sets is recorded but not integrated.
/// </summary>
public sealed class KinematicPlayerBody : IPlayerMovementBody, IMotionInterpHost, IMoveToHost
{
    private readonly PlayerAvatar _avatar;
    private readonly MotionPlayer _parts;
    private readonly InterpretedMotionState _scratch = new();
    private readonly IMotionWeenie? _weenie;
    private ObjectClock _clock;

    /// <param name="avatar">The player's avatar, whose part array the body animates.</param>
    /// <param name="world">The objects target ids resolve to (normally the avatar set).</param>
    /// <param name="weenie">The player's weenie (the state track's qualities), or null for none.</param>
    /// <exception cref="InvalidOperationException">The player's CreateObject named no motion table.</exception>
    public KinematicPlayerBody(PlayerAvatar avatar, ITargetWorld world, IMotionWeenie? weenie = null)
    {
        ArgumentNullException.ThrowIfNull(avatar);
        ArgumentNullException.ThrowIfNull(world);
        _avatar = avatar;
        _parts = avatar.Parts ?? throw new InvalidOperationException($"player 0x{avatar.Guid:X8}: the CreateObject named no motion table");
        _weenie = weenie;
        World = world;
        Position = avatar.Body.Position;
        Time = avatar.Body.Time;
        _clock = new ObjectClock(Time);
        HasContact = true;
        OnWalkable = true;
        MovementIsAutonomous = true;
        Positions = new PositionManager();
        Targets = new TargetManager(this, world);
        Motion = new MotionInterpreter(_parts, this);
        Movement = new MovementManager(Motion, this);
        Motion.EnterDefaultState();
    }

    /// <inheritdoc/>
    public CellFrame Position { get; private set; }

    /// <inheritdoc/>
    public bool HasContact { get; private set; }

    /// <inheritdoc/>
    public bool OnWalkable { get; private set; }

    /// <inheritdoc/>
    public MotionInterpreter Motion { get; }

    /// <inheritdoc/>
    public MovementManager Movement { get; }

    /// <summary>The position managers.</summary>
    public PositionManager Positions { get; }

    /// <inheritdoc/>
    public TargetManager Targets { get; }

    /// <inheritdoc/>
    public ITargetWorld World { get; }

    /// <inheritdoc/>
    public uint Id => _avatar.Guid;

    /// <inheritdoc/>
    public uint TopLevelId => Id;

    /// <inheritdoc/>
    public double Time { get; private set; }

    /// <summary>The measured velocity of the last update.</summary>
    public Vector3 Velocity { get; private set; }

    /// <summary>The velocity the interpreter set (a jump's), recorded and not integrated.</summary>
    public Vector3 PhysicsVelocity { get; private set; }

    /// <inheritdoc/>
    public float Radius => _avatar.Body.Radius;

    /// <inheritdoc/>
    public float Height => _avatar.Body.Height;

    /// <inheritdoc/>
    public float Heading => PositionFrames.Heading(Position.Orientation);

    /// <inheritdoc/>
    public bool IsInterpolating => Positions.IsInterpolating;

    /// <inheritdoc/>
    public bool HasGravity => true;

    /// <inheritdoc/>
    public bool InCell => Position.CellId != 0;

    /// <inheritdoc/>
    public bool MovementIsAutonomous { get; set; }

    /// <inheritdoc/>
    public bool IsFullyConstrained => Positions.IsFullyConstrained;

    /// <inheritdoc/>
    public Quaternion Orientation => Position.Orientation;

    /// <inheritdoc/>
    public IMotionWeenie? Weenie => _weenie;

    Vector3 IMotionInterpHost.Velocity => PhysicsVelocity;

    MotionInterpreter? IMoveToHost.Motion => Motion;

    /// <summary>Places the body (a teleport: nothing in between).</summary>
    public void Place(in CellFrame position)
    {
        Position = position.Normalized();
        Velocity = Vector3.Zero;
    }

    /// <summary>Contact and walkable ground (<see cref="SetOnWalkable"/> on a change): landing or leaving the ground.</summary>
    public void SetContact(bool hasContact)
    {
        HasContact = hasContact;
        SetOnWalkable(hasContact);
    }

    /// <summary>
    /// Steps the body to <paramref name="now"/>: per quantum (retail's object clock), the part array's update (its hooks
    /// to the avatar), the root motion on walkable ground, the position managers, the combined frame (outdoors the cell
    /// follows the origin), then, in retail's order, the targets, the MoveTo, the part array's completion check
    /// (<see cref="MotionPlayer.HandleMovement"/>) and the blips.
    /// </summary>
    public void Advance(double now)
    {
        Time = now;
        if (Position.CellId == 0)
        {
            _clock = new ObjectClock(now);
            return;
        }

        foreach (var (quantum, time) in _clock.Advance(now))
        {
            var update = _parts.Update(quantum);
            _avatar.QueueHooks(update.Hooks);
            var offset = update.RootMotion;
            offset = offset with { Origin = OnWalkable ? offset.Origin : Vector3.Zero };
            var ctx = PositionContext.For(Position, HasContact, isThePlayer: true, Motion, Positions.GetStickyObjectId());
            Positions.AdjustOffset(ref offset, quantum, ctx);
            var old = Position;
            var frame = AcFrame.Combine(Position.Frame, offset);
            var cell = Position.CellId;
            var origin = frame.Origin;
            if (LandDefs.IsOutdoorCell(cell))
            {
                var adjusted = cell;
                var local = origin;
                if (LandDefs.AdjustToOutside(ref adjusted, ref local))
                {
                    cell = adjusted;
                    origin = local;
                }
            }

            Position = new CellFrame(cell, frame with { Origin = origin });
            Velocity = PositionFrames.Offset(old, Position) / (float)quantum;
            Targets.HandleTargetting(time);
            Movement.UseTime();
            _parts.HandleMovement();
            if (Positions.UseTime(ctx) is { } blip)
            {
                Place(blip.Position);
                Positions.Interpolation.CompleteBlip(placed: true);
            }
        }
    }

    /// <inheritdoc/>
    public MovementUnpackResult ApplyServerMovement(in WireMovement movement, ReadOnlySpan<WireMotionItem> actions, bool autonomous, out WireMotionFailure failure)
    {
        MovementIsAutonomous = autonomous;
        return Movement.UnpackMovement(movement, actions, _scratch, out failure);
    }

    /// <inheritdoc/>
    public void SetLocalVelocity(Vector3 localVelocity) => PhysicsVelocity = Vector3.Transform(localVelocity, Position.Orientation);

    /// <inheritdoc/>
    public void SetOnWalkable(bool onWalkable)
    {
        var was = OnWalkable;
        OnWalkable = onWalkable;
        if (was && !onWalkable)
        {
            Movement.LeaveGround();
        }
        else if (!was && onWalkable)
        {
            PhysicsVelocity = Vector3.Zero;
            Movement.HitGround();
        }
    }

    /// <inheritdoc/>
    public void CancelMoveTo() => Movement.CancelMoveTo(WeenieError.ActionCancelled);

    /// <inheritdoc/>
    public void UnstickFromObject() => Positions.UnStick();

    /// <inheritdoc/>
    public void SetHeading(float degrees) =>
        Position = Position with { Frame = Position.Frame with { Orientation = PositionFrames.WithHeading(Position.Orientation, degrees) } };

    /// <inheritdoc/>
    public void StopCompletely() => Motion.PerformMovement(MotionMovementType.StopCompletely, 0, new MovementParameters());

    /// <inheritdoc/>
    public void StickTo(uint objectId, float radius, float height) => Positions.StickTo(objectId, radius, height, this);

    /// <inheritdoc/>
    public void HandleUpdateTarget(in TargetInfo info)
    {
        if (info.ContextId != 0)
        {
            return;
        }

        Movement.HandleUpdateTarget(info);
        Positions.HandleUpdateTarget(info);
    }
}

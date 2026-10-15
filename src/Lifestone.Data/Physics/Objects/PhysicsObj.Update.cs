using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics.Objects;

/// <summary>The stepping of a <see cref="PhysicsObj"/>: <c>update_object</c> and what it calls.</summary>
public sealed partial class PhysicsObj
{
    /// <summary>Beyond this distance from the player an object is not updated (<c>update_object</c>'s literal 96.0).</summary>
    public const float ActiveDistance = 96f;

    /// <summary>
    /// Retail <c>CPhysicsObj::calc_friction</c>'s cosine of 10 degrees (<c>cos(0.1745329251994329)</c>): a sledding object
    /// on a slope steeper than this keeps the low friction.
    /// </summary>
    private static readonly double SleddingSlopeCos = Math.Cos(0.1745329251994329);

    /// <summary>Retail <c>PhysicsTimer::curr_time</c>: the end of the step being taken (collision records are stamped with it).</summary>
    private double _stepTime;

    /// <summary>
    /// Retail <c>CPhysicsObj::update_object</c> (11.4186 0x00515D10, 11.6096 0x00516810): steps the object from
    /// <see cref="UpdateTime"/> to the world's <see cref="PhysicsWorld.CurrentTime"/>.
    /// <list type="bullet">
    /// <item>An object outside a cell, or frozen, becomes inactive and does not move.</item>
    /// <item>With a player in the world, the object measures its distance from it (<see cref="PlayerDistance"/>) and is
    /// inactive beyond <see cref="ActiveDistance"/> (retail also asks that the object maintainer be active, which it always
    /// is in play), else active.</item>
    /// <item>A gap of at most 0.0002 s, or of more than 2 s, is not stepped: the clock jumps to now.</item>
    /// <item>Otherwise steps of at most <see cref="PhysicsCompat.MaxQuantum"/> (retail 0.2 s); after a longer gap is split,
    /// a remainder of at most 1/30 s is carried to the next update (the clock stops at the end of the last step).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The slicing is <see cref="Effects.ObjectClock"/>'s rule, with the largest step taken from the world's
    /// <see cref="PhysicsCompat"/> (decision table row 1).
    /// </remarks>
    public void UpdateObject()
    {
        var now = World.CurrentTime;
        if (Cell is null || (State & PhysicsState.Frozen) != 0)
        {
            TransientState &= ~TransientState.Active;
            return;
        }

        if (World.Player is { } player)
        {
            PlayerVector = player.Position.GetOffset(Position);
            var distance = Math.Sqrt(((double)PlayerVector.X * PlayerVector.X) + ((double)PlayerVector.Y * PlayerVector.Y) + ((double)PlayerVector.Z * PlayerVector.Z));
            PlayerDistance = (float)distance;
            if (distance > ActiveDistance)
            {
                TransientState &= ~TransientState.Active;
            }
            else
            {
                SetActive(true);
            }
        }

        var quantum = now - UpdateTime;
        if (quantum <= Effects.ObjectClock.Epsilon || quantum > PhysicsGlobals.HugeQuantum)
        {
            UpdateTime = now;
            return;
        }

        var maxQuantum = World.Compat.MaxQuantum;
        var time = UpdateTime;
        _updating = true;
        try
        {
            if (quantum > maxQuantum)
            {
                do
                {
                    time += maxQuantum;
                    _stepTime = time;
                    UpdateObjectInternal((float)maxQuantum);
                    quantum -= maxQuantum;
                }
                while (quantum > maxQuantum);

                if (quantum > PhysicsGlobals.MinQuantum)
                {
                    time += quantum;
                    _stepTime = time;
                    UpdateObjectInternal((float)quantum);
                }
            }
            else
            {
                time += quantum;
                _stepTime = time;
                UpdateObjectInternal((float)quantum);
            }
        }
        finally
        {
            _updating = false;
        }

        UpdateTime = time;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::UpdateObjectInternal</c> (11.4186 0x005156B0, 11.6096 0x005161B0): one step of an active
    /// object in a cell.
    /// <list type="number">
    /// <item>A pending return to solid is tried again (CheckEthereal); <see cref="JumpedThisFrame"/> is cleared.</item>
    /// <item>The new frame (<see cref="UpdatePositionInternal"/>: root motion, position managers, velocity).</item>
    /// <item>An object with spheres whose origin did not move (each axis within the epsilon) only turns; one that moved
    /// runs a transition from its
    /// position to the new one (turned to its path with AlignPath, or to its velocity when sledding) and takes the
    /// result (<see cref="SetPositionInternal(Transition)"/>), or only turns when no position is valid. The cached
    /// velocity is the displacement over the quantum. An object without spheres only turns.</item>
    /// <item>In retail's order: the target manager's reports (<c>TargetManager::HandleTargetting</c>, 11.4186 0x0051AA90,
    /// 11.6096 0x0051B5C0: the objects that target this one hear where it is going), the movement manager's MoveTo
    /// (<c>MovementManager::UseTime</c>, 0x005242F0 / 0x00524EF0), the motion table's completion check
    /// (<c>CPartArray::HandleMovement</c>, 0x00517D60 / 0x00518890, <see cref="Animation.MotionPlayer.HandleMovement"/>:
    /// queued motions without animations complete, so the MoveTo's steering and the jump gate stop waiting), and the
    /// position manager's blip check and sticky timeout.</item>
    /// </list>
    /// </summary>
    private void UpdateObjectInternal(float quantum)
    {
        if ((TransientState & TransientState.Active) == 0 || Cell is null)
        {
            return;
        }

        if ((TransientState & TransientState.CheckEthereal) != 0)
        {
            SetEthereal(false);
        }

        JumpedThisFrame = false;
        var newFrame = UpdatePositionInternal(quantum);
        if (Setup is not null && Setup.Spheres.Count > 0)
        {
            if (RetailEqual(newFrame.Origin, Position.Frame.Origin))
            {
                SetFrame(newFrame with { Origin = Position.Frame.Origin });
                CachedVelocity = Vector3.Zero;
            }
            else
            {
                if ((State & PhysicsState.AlignPath) != 0)
                {
                    var path = newFrame.Origin - Position.Frame.Origin;
                    newFrame = RetailHeadings.SetVectorHeading(newFrame, Vector3.Normalize(path));
                }
                else if ((State & PhysicsState.Sledding) != 0 && !RetailIsZero(Velocity))
                {
                    newFrame = RetailHeadings.SetHeading(newFrame, VectorHeading(Velocity));
                }

                var transition = World.Transitions.Rent();
                try
                {
                    if (RunTransition(transition, Position, new CellFrame(Position.CellId, newFrame), admin: false))
                    {
                        CachedVelocity = Position.GetOffset(transition.SpherePath.CurPos) / quantum;
                        SetPositionInternal(transition);
                    }
                    else
                    {
                        SetFrame(newFrame with { Origin = Position.Frame.Origin });
                        CachedVelocity = Vector3.Zero;
                    }
                }
                finally
                {
                    World.Transitions.Return(transition);
                }
            }
        }
        else
        {
            if (Motion is null && OnWalkable)
            {
                TransientState &= ~TransientState.Active;
            }

            SetFrame(newFrame with { Origin = Position.Frame.Origin });
            CachedVelocity = Vector3.Zero;
        }

        // Detection (DetectionManager::CheckDetection) comes first in retail and is not physics.
        _targets?.HandleTargetting(_stepTime);
        Movement?.UseTime();
        MotionPlayer?.HandleMovement();
        UsePositionManagerTime();
    }

    /// <summary>
    /// Retail <c>PositionManager::UseTime</c> (11.4186 0x00555160, 11.6096 0x00555D80) through the avatars track's blip
    /// handshake: a blip the interpolation manager asks for is a sliding <see cref="SetPositionSimple"/>, whose success
    /// is reported back; then the sticky manager's timeout.
    /// </summary>
    private void UsePositionManagerTime()
    {
        if (Positions is null)
        {
            return;
        }

        var ctx = PositionContextNow();
        if (Positions.UseTime(ctx) is { } blip)
        {
            var placed = SetPositionSimple(blip.Position, sliding: true) == SetPositionError.OK;
            Positions.Interpolation.CompleteBlip(placed);
        }
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::UpdatePositionInternal</c> (11.4186 0x00512C30, 11.6096 0x00513730): the frame the object
    /// would reach this step.
    /// <list type="number">
    /// <item>Unless hidden: the motion table's root motion (<c>CPartArray::Update</c>), scaled by the object's scale on
    /// walkable ground and dropped elsewhere (in the air the object moves by its velocity only).</item>
    /// <item>The position managers' adjustment (interpolation, sticky, constraint).</item>
    /// <item>The offset combined onto the position; unless hidden, the velocity, acceleration and spin
    /// (<see cref="UpdatePhysicsInternal"/>).</item>
    /// <item>The animation hooks (<c>process_hooks</c>): animation-done hooks were already fed to the motion table by
    /// <see cref="Animation.MotionPlayer.Update"/>; the others go to <see cref="Events"/>.</item>
    /// </list>
    /// </summary>
    private AcFrame UpdatePositionInternal(float quantum)
    {
        var offset = AcFrame.Identity;
        IReadOnlyList<AnimHook>? hooks = null;
        var hidden = (State & PhysicsState.Hidden) != 0;
        if (!hidden)
        {
            if (MotionPlayer is not null)
            {
                var update = MotionPlayer.Update(quantum);
                offset = update.RootMotion;
                hooks = update.Hooks;
            }

            offset = OnWalkable
                ? offset with { Origin = offset.Origin * Scale }
                : offset with { Origin = offset.Origin * 0.0f };
        }

        if (Positions is not null)
        {
            var ctx = PositionContextNow();
            Positions.AdjustOffset(ref offset, quantum, ctx);
        }

        var newFrame = AcFrame.Combine(Position.Frame, offset);
        if (!hidden)
        {
            UpdatePhysicsInternal(quantum, ref newFrame);
        }

        ProcessHooks(hooks);
        return newFrame;
    }

    /// <summary>Retail <c>CPhysicsObj::process_hooks</c> (11.4186 0x00511550, 11.6096 0x00512020), for the animation hooks.</summary>
    private void ProcessHooks(IReadOnlyList<AnimHook>? hooks)
    {
        if (hooks is null || hooks.Count == 0 || Events is null)
        {
            return;
        }

        for (var i = 0; i < hooks.Count; i++)
        {
            if (hooks[i] is not AnimationDoneHook)
            {
                Events.OnAnimationHooks(this, hooks);
                return;
            }
        }
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::UpdatePhysicsInternal</c> (11.4186 0x00510700, 11.6096 0x005111D0): moves
    /// <paramref name="frame"/> by the velocity and half the acceleration's square term, then adds the acceleration to
    /// the velocity and turns the frame by the spin.
    /// <list type="bullet">
    /// <item>Without velocity, an object without a motion interpreter on walkable ground becomes inactive.</item>
    /// <item>Otherwise the velocity is capped at <see cref="PhysicsGlobals.MaxVelocity"/>, slowed by friction on walkable
    /// ground (<see cref="CalcFriction"/>), and zeroed when its squared length (before friction) is within the epsilon of
    /// <see cref="PhysicsGlobals.SmallVelocity"/>'s square or below.</item>
    /// </list>
    /// </summary>
    private void UpdatePhysicsInternal(float quantum, ref AcFrame frame)
    {
        var velocity = Velocity;
        var magnitude2 = velocity.LengthSquared();
        if (magnitude2 <= 0.0f)
        {
            if (Motion is null && OnWalkable)
            {
                TransientState &= ~TransientState.Active;
            }
        }
        else
        {
            if (magnitude2 > PhysicsGlobals.MaxVelocitySquared)
            {
                Velocity = Vector3.Normalize(Velocity) * PhysicsGlobals.MaxVelocity;
                magnitude2 = PhysicsGlobals.MaxVelocitySquared;
            }

            CalcFriction(quantum, magnitude2);
            if (magnitude2 - PhysicsGlobals.SmallVelocitySquared < PhysicsGlobals.Epsilon)
            {
                Velocity = Vector3.Zero;
            }

            var half = Acceleration * 0.5f;
            var movement = new Vector3(
                (quantum * Velocity.X) + (half.X * quantum * quantum),
                (quantum * Velocity.Y) + (half.Y * quantum * quantum),
                (quantum * Velocity.Z) + (half.Z * quantum * quantum));
            frame = frame with { Origin = frame.Origin + movement };
        }

        Velocity += Acceleration * quantum;
        frame = frame.GRotate(Omega * quantum);
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::calc_acceleration</c> (11.4186 0x00510950, 11.6096 0x00511420): nothing (and no spin) for an
    /// object standing on walkable ground unless it is sledding; else gravity with the Gravity state, or nothing.
    /// </summary>
    private void CalcAcceleration()
    {
        if (HasContact && OnWalkable && (State & PhysicsState.Sledding) == 0)
        {
            Acceleration = Vector3.Zero;
            Omega = Vector3.Zero;
        }
        else
        {
            Acceleration = (State & PhysicsState.Gravity) != 0 ? new Vector3(0f, 0f, PhysicsGlobals.Gravity) : Vector3.Zero;
        }
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::calc_friction</c> (11.4186 0x0050EE70, 11.6096 0x0050F940), on walkable ground only: unless
    /// the velocity leaves the contact plane at 0.25 m/s or more, its part into the plane is removed and the rest decays
    /// by <c>(1 - friction)^quantum</c>. A sledding object uses friction 1 below 1.25 m/s, 0.2 above 2.5 m/s on a slope
    /// steeper than 10 degrees, and its own friction otherwise.
    /// </summary>
    /// <param name="quantum">The step in seconds.</param>
    /// <param name="magnitude2">The velocity's squared length before the step.</param>
    private void CalcFriction(float quantum, float magnitude2)
    {
        if (!OnWalkable)
        {
            return;
        }

        var normal = ContactPlane.Normal;
        var velocity = Velocity;
        double into = (velocity.Z * normal.Z) + (velocity.Y * normal.Y) + (velocity.X * normal.X);
        if (into >= 0.25)
        {
            return;
        }

        velocity = new Vector3(
            (float)(velocity.X - (into * normal.X)),
            (float)(velocity.Y - (into * normal.Y)),
            (float)(velocity.Z - (float)(into * normal.Z)));

        double friction;
        if ((State & PhysicsState.Sledding) != 0)
        {
            if (magnitude2 >= 1.5625)
            {
                friction = magnitude2 < 6.25 || SleddingSlopeCos <= normal.Z ? Friction : 0.2f;
            }
            else
            {
                friction = 1.0;
            }
        }
        else
        {
            friction = Friction;
        }

        var decay = Math.Pow(1.0 - friction, quantum);
        Velocity = new Vector3((float)(decay * velocity.X), (float)(decay * velocity.Y), (float)(decay * velocity.Z));
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::transition</c> (11.4186 0x00512DC0, 11.6096 0x005138C0) without the pool: fills
    /// <paramref name="transition"/> for a move from <paramref name="from"/> to <paramref name="to"/> (the object and its
    /// spheres, the path from its cell, the stationary-fall count carried in the transient state) and finds the valid
    /// position. A velocity the transition killed (<c>OBJECTINFO::kill_velocity</c>) is zeroed at once, as retail does.
    /// </summary>
    private bool RunTransition(Transition transition, in CellFrame from, in CellFrame to, bool admin)
    {
        InitObject(transition, admin);
        transition.InitPath(Cell, from, to);
        if ((TransientState & TransientState.StationaryStuck) != 0)
        {
            transition.CollisionInfo.FramesStationaryFall = 3;
        }
        else if ((TransientState & TransientState.StationaryStop) != 0)
        {
            transition.CollisionInfo.FramesStationaryFall = 2;
        }
        else if ((TransientState & TransientState.StationaryFall) != 0)
        {
            transition.CollisionInfo.FramesStationaryFall = 1;
        }

        var found = transition.FindValidPosition();
        ApplyKilledVelocity(transition);
        return found;
    }

    /// <summary>Retail <c>OBJECTINFO::kill_velocity</c>'s <c>set_velocity(0)</c>, which Lifestone's transition records.</summary>
    private void ApplyKilledVelocity(Transition transition)
    {
        if (transition.ObjectInfo.VelocityKilled)
        {
            SetVelocity(Vector3.Zero);
        }
    }

    /// <summary>Retail <c>AC1Legacy::Vector3::get_heading</c>: the compass heading of a vector (0 north, clockwise), in degrees.</summary>
    private static float VectorHeading(Vector3 v)
    {
        var heading = (450.0 - (Math.Atan2(v.Y, v.X) * (180.0 / Math.PI))) % 360.0;
        return (float)(heading < 0 ? heading + 360.0 : heading);
    }
}

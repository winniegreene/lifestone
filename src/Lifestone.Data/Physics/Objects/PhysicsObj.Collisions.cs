using System.Numerics;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.Physics.World;

namespace Lifestone.Data.Physics.Objects;

/// <summary>Collisions of a <see cref="PhysicsObj"/>: other objects running into it, and what it ran into.</summary>
public sealed partial class PhysicsObj
{
    /// <summary>The physics bits a missile drops at its first collision (<c>report_*_collision</c>: Missile, AlignPath, PathClipped).</summary>
    private const PhysicsState MissileFlightBits = PhysicsState.Missile | PhysicsState.AlignPath | PhysicsState.PathClipped;

    private readonly List<uint> _endedCollisions = [];
    private Dictionary<uint, CollisionRecord>? _collisionTable;
    private bool _updating;

    /// <summary>The objects the object is touching, by id (retail <c>collision_table</c>), with when each was last touched.</summary>
    public int CollisionCount => _collisionTable?.Count ?? 0;

    /// <summary>Retail <c>PhysicsTimer::curr_time</c>: the step's end inside <see cref="UpdateObject"/>, else the world's time.</summary>
    private double CollisionTime => _updating ? _stepTime : World.CurrentTime;

    /// <summary>
    /// Retail <c>CPhysicsObj::FindObjCollisions</c> (11.4186 0x0050F050, 11.6096 0x0050FB20) for this object as an
    /// obstacle in another object's transition.
    /// <list type="bullet">
    /// <item>Nothing for an object that is ethereal and ignores collisions, or for a creature against the camera.</item>
    /// <item>An ethereal obstruction (this object, or an ethereal mover against a non-static) is never stepped down onto.</item>
    /// <item>Two players pass through each other, unless either is impenetrable or both are player killers (or both lite
    /// player killers).</item>
    /// <item>Otherwise the object's physics BSP parts, else its cylinder-spheres, else its spheres (scaled, at its
    /// position), unless the mover is a missile that ignores it (<see cref="MissileIgnore"/>).</item>
    /// <item>A collision (not while stepping down) is recorded: with a static as an environment collision of a mover
    /// without contact; with an ethereal object, or a creature the mover ignores, as an object collision that does not
    /// stop the mover; otherwise as an object collision.</item>
    /// </list>
    /// </summary>
    internal TransitionState FindObjCollisions(Transition transition)
    {
        if ((State & PhysicsState.Ethereal) != 0 && (State & PhysicsState.IgnoreCollisions) != 0)
        {
            return TransitionState.OK;
        }

        var info = transition.ObjectInfo;
        var weenie = Weenie;
        if (weenie is not null && (info.State & ObjectInfoState.IsViewer) != 0 && weenie.IsCreature)
        {
            return TransitionState.OK;
        }

        var path = transition.SpherePath;
        var ethereal = (State & PhysicsState.Ethereal) != 0 || (info.Ethereal && (State & PhysicsState.Static) == 0);
        if (ethereal && path.StepDown)
        {
            return TransitionState.OK;
        }

        path.ObstructionEthereal = ethereal;
        var playersPass = weenie is not null
            && weenie.IsPlayer
            && (info.State & ObjectInfoState.IsPlayer) != 0
            && (info.State & ObjectInfoState.IsImpenetrable) == 0
            && !weenie.IsImpenetrable
            && !((info.State & ObjectInfoState.IsPK) != 0 && weenie.IsPK)
            && !((info.State & ObjectInfoState.IsPKLite) != 0 && weenie.IsPKLite);
        var isCreature = (State & PhysicsState.Missile) != 0 || (weenie?.IsCreature ?? false);
        var state = TransitionState.OK;
        if (!playersPass && !MissileIgnore(info))
        {
            if ((State & PhysicsState.HasPhysicsBsp) != 0)
            {
                foreach (var part in _parts)
                {
                    state = part.FindObjCollisions(transition);
                    if (state != TransitionState.OK)
                    {
                        break;
                    }
                }
            }
            else if (Setup is not null && Setup.CylSpheres.Count > 0)
            {
                foreach (var cylSphere in Setup.CylSphereSpan)
                {
                    state = cylSphere.IntersectsSphere(Position, Scale, transition);
                    if (state != TransitionState.OK)
                    {
                        break;
                    }
                }
            }
            else if (Setup is not null)
            {
                foreach (var sphere in Setup.SphereSpan)
                {
                    state = sphere.IntersectsSphere(Position, Scale, transition, isCreature);
                    if (state != TransitionState.OK)
                    {
                        break;
                    }
                }
            }
        }

        if (state != TransitionState.OK && !path.StepDown)
        {
            var collisions = transition.CollisionInfo;
            if ((State & PhysicsState.Static) != 0)
            {
                if ((info.State & ObjectInfoState.Contact) == 0)
                {
                    collisions.CollidedWithEnvironment = true;
                }
            }
            else if (ethereal || (isCreature && (info.State & ObjectInfoState.IgnoreCreatures) != 0))
            {
                state = TransitionState.OK;
                collisions.CollisionNormalValid = false;
                collisions.AddObject(this, TransitionState.OK);
            }
            else
            {
                collisions.AddObject(this, state);
            }
        }

        path.ObstructionEthereal = false;
        return state;
    }

    /// <summary>
    /// Retail <c>OBJECTINFO::missile_ignore</c> (11.4186 0x0050CEB0, 11.6096 0x0050D980): a missile passes through this
    /// object when this object is a missile too, or when this object is not the missile's target and either is ethereal
    /// with a weenie or is a creature while the missile has a target.
    /// </summary>
    private bool MissileIgnore(ObjectInfo info)
    {
        if ((State & PhysicsState.Missile) != 0)
        {
            return true;
        }

        if ((info.ObjectState & PhysicsState.Missile) == 0 || Id == info.TargetId)
        {
            return false;
        }

        return ((State & PhysicsState.Ethereal) != 0 && Weenie is not null)
            || (info.TargetId != 0 && Weenie is not null && Weenie.IsCreature);
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::handle_all_collisions</c> (11.4186 0x00514780, 11.6096 0x00515280): reports what the
    /// transition ran into and bounces off it.
    /// <list type="number">
    /// <item>Each object collided with is tracked (<see cref="TrackObjectCollision"/>); collisions not touched for a
    /// second end (<see cref="ReportCollisionEnd"/>).</item>
    /// <item>An environment collision is reported once while it lasts; landing on walkable ground counts as one.</item>
    /// <item>Unless the object was and still is on walkable ground (and not sledding), a collision normal bounces the
    /// velocity: an inelastic object stops, any other loses its speed into the normal and gains its elasticity's share
    /// back out of it. The stationary-fall count zeroes the velocity from 2 on and sets the stationary bits (the count
    /// of 3 clears them).</item>
    /// </list>
    /// </summary>
    /// <returns>True when a collision was reported (retail's <c>SetPositionInternal</c> then answers Collided).</returns>
    private bool HandleAllCollisions(CollisionInfo collisions, bool prevHasContact, bool prevOnWalkable)
    {
        var applyBounce = !prevOnWalkable || !OnWalkable || (State & PhysicsState.Sledding) != 0;
        var reported = false;
        for (var i = 0; i < collisions.CollideObjectCount; i++)
        {
            if (TrackObjectCollision(collisions.CollideObjectAt(i), prevHasContact))
            {
                reported = true;
            }
        }

        ReportCollisionEnd(forceEnd: false);
        if (CollidingWithEnvironment)
        {
            CollidingWithEnvironment = collisions.CollidedWithEnvironment;
        }
        else if ((collisions.CollidedWithEnvironment || (!prevOnWalkable && OnWalkable)) && ReportEnvironmentCollision(prevHasContact))
        {
            reported = true;
        }

        var framesStationaryFall = collisions.FramesStationaryFall;
        if (framesStationaryFall <= 1)
        {
            if (applyBounce && collisions.CollisionNormalValid)
            {
                if ((State & PhysicsState.Inelastic) != 0)
                {
                    Velocity = Vector3.Zero;
                }
                else
                {
                    var normal = collisions.CollisionNormal;
                    double into = (Velocity.Z * normal.Z) + (Velocity.Y * normal.Y) + (normal.X * Velocity.X);
                    if (into < 0.0)
                    {
                        var push = -(into * (Elasticity + 1.0));
                        Velocity = new Vector3(
                            (float)((push * normal.X) + Velocity.X),
                            (float)((push * normal.Y) + Velocity.Y),
                            (float)((push * normal.Z) + Velocity.Z));
                    }
                }
            }
        }
        else
        {
            Velocity = Vector3.Zero;
            if (framesStationaryFall == 3)
            {
                TransientState &= ~TransientState.StationaryMask;
                return reported;
            }
        }

        TransientState |= framesStationaryFall switch
        {
            0 => TransientState.None,
            1 => TransientState.StationaryFall,
            2 => TransientState.StationaryStop,
            _ => TransientState.StationaryStuck,
        };
        if (framesStationaryFall == 0)
        {
            TransientState &= ~TransientState.StationaryMask;
        }

        return reported;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::track_object_collision</c> (11.4186 0x00513F10, 11.6096 0x00514A10): a static counts as the
    /// environment; any other object is recorded as touched now, and reported only when it was not already touching.
    /// </summary>
    private bool TrackObjectCollision(PhysicsObj other, bool prevHasContact)
    {
        if ((other.State & PhysicsState.Static) != 0)
        {
            return ReportEnvironmentCollision(prevHasContact);
        }

        _collisionTable ??= new Dictionary<uint, CollisionRecord>(4);
        var existed = _collisionTable.ContainsKey(other.Id);
        _collisionTable[other.Id] = new CollisionRecord(CollisionTime, (other.State & PhysicsState.Ethereal) != 0);
        return !existed && ReportObjectCollision(other, prevHasContact);
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::report_environment_collision</c> (11.4186 0x00512FC0, 11.6096 0x00513AC0): once until it
    /// ends, an object with ReportCollisions and a weenie reports it (<see cref="IPhysicsEvents.OnEnvironmentCollision"/>);
    /// a missile stops being one.
    /// </summary>
    private bool ReportEnvironmentCollision(bool prevHasContact)
    {
        if (CollidingWithEnvironment)
        {
            return false;
        }

        var reported = false;
        if ((State & PhysicsState.ReportCollisions) != 0 && Weenie is not null)
        {
            Events?.OnEnvironmentCollision(this, Velocity, prevHasContact);
            reported = true;
        }

        CollidingWithEnvironment = true;
        if ((State & PhysicsState.Missile) != 0)
        {
            State &= ~MissileFlightBits;
        }

        return reported;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::report_object_collision</c> (11.4186 0x00513060, 11.6096 0x00513B60): an object that
    /// reports as the environment is an environment collision; otherwise both objects that report collisions (and have a
    /// weenie) hear of it, unless the other ignores collisions, and a missile stops being one.
    /// </summary>
    /// <remarks>
    /// Retail builds the other object's profile with the reporter's previous contact as the missile flag and its missile
    /// bit as the contact flag (the arguments of <c>build_collision_profile</c> are swapped there); the report keeps
    /// retail's values. No Lifestone consumer reads them.
    /// </remarks>
    private bool ReportObjectCollision(PhysicsObj other, bool prevHasContact)
    {
        if ((other.State & PhysicsState.ReportCollisionsAsEnvironment) != 0)
        {
            return ReportEnvironmentCollision(prevHasContact);
        }

        var reported = false;
        var wasMissile = (State & PhysicsState.Missile) != 0;
        var velocity = Velocity - other.Velocity;
        if ((other.State & PhysicsState.IgnoreCollisions) == 0)
        {
            if ((State & PhysicsState.ReportCollisions) != 0 && Weenie is not null)
            {
                Events?.OnObjectCollision(
                    this,
                    new ObjectCollision(other, velocity, wasMissile, (other.State & PhysicsState.Missile) != 0, other.HasContact, prevHasContact));
                reported = true;
            }

            if (wasMissile)
            {
                State &= ~MissileFlightBits;
            }
        }

        if ((other.State & PhysicsState.ReportCollisions) != 0 && (State & PhysicsState.IgnoreCollisions) == 0 && other.Weenie is not null)
        {
            other.Events?.OnObjectCollision(
                other,
                new ObjectCollision(this, velocity, (other.State & PhysicsState.Missile) != 0, prevHasContact, wasMissile, other.HasContact));
            reported = true;
        }

        return reported;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::report_collision_end</c> (11.4186 0x00514620, 11.6096 0x00515120): the collisions not
    /// touched for more than a second (an ethereal one's at once), or all of them when <paramref name="forceEnd"/>, end.
    /// </summary>
    private void ReportCollisionEnd(bool forceEnd)
    {
        if (_collisionTable is null || _collisionTable.Count == 0)
        {
            return;
        }

        var now = CollisionTime;
        _endedCollisions.Clear();
        foreach (var (id, record) in _collisionTable)
        {
            if (now - record.TouchedTime > 1.0 || (record.Ethereal && now - record.TouchedTime > 0.0) || forceEnd)
            {
                _endedCollisions.Add(id);
            }
        }

        foreach (var id in _endedCollisions)
        {
            _collisionTable.Remove(id);
        }

        foreach (var id in _endedCollisions)
        {
            ReportObjectCollisionEnd(id);
        }

        _endedCollisions.Clear();
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::report_object_collision_end</c> (11.4186 0x00510A90, 11.6096 0x00511560): both objects that
    /// report collisions hear that it ended (an object that reports as the environment never started one); an object no
    /// longer in the world only to this one.
    /// </summary>
    private void ReportObjectCollisionEnd(uint otherId)
    {
        var other = World.GetObject(otherId);
        if (other is null)
        {
            if ((State & PhysicsState.ReportCollisions) != 0 && Weenie is not null)
            {
                Events?.OnObjectCollisionEnd(this, otherId);
            }

            return;
        }

        if ((other.State & PhysicsState.ReportCollisionsAsEnvironment) != 0)
        {
            return;
        }

        if ((State & PhysicsState.ReportCollisions) != 0 && Weenie is not null)
        {
            Events?.OnObjectCollisionEnd(this, otherId);
        }

        if ((other.State & PhysicsState.ReportCollisions) != 0 && other.Weenie is not null)
        {
            other.Events?.OnObjectCollisionEnd(other, Id);
        }
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::report_collision_start</c> (11.4186 0x00513FD0, 11.6096 0x00514AD0): reports again every
    /// collision still in the table (an object shown again).
    /// </summary>
    private void ReportCollisionStart()
    {
        if (_collisionTable is null)
        {
            return;
        }

        foreach (var id in _collisionTable.Keys)
        {
            if (World.GetObject(id) is { } other)
            {
                ReportObjectCollision(other, HasContact);
            }
        }
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::ethereal_check_for_collisions</c> (11.4186 0x00510A40, 11.6096 0x00511510) through
    /// <c>CObjCell::check_collisions</c> (0x0052B7B0, 0x0052C3C0): whether a moving object in one of this object's shadow
    /// cells overlaps it (statics never answer).
    /// </summary>
    private bool EtherealCheckForCollisions()
    {
        IPhysicsCellSource cells = World;
        for (var i = 0; i < _shadowCount; i++)
        {
            if (_shadowCells[i] is null)
            {
                continue;
            }

            var objects = cells.GetObjectShadows(_shadowIds[i]);
            if (objects is null)
            {
                continue;
            }

            for (var j = 0; j < objects.Count; j++)
            {
                var other = objects[j];
                if (!ReferenceEquals(other, this) && other.CheckCollision(this))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::check_collision</c> (11.4186 0x00512ED0, 11.6096 0x005139D0): whether this (dynamic) object,
    /// placed where it stands, overlaps <paramref name="other"/> (<c>CTransition::check_collisions</c>).
    /// </summary>
    private bool CheckCollision(PhysicsObj other)
    {
        if ((State & PhysicsState.Static) != 0)
        {
            return false;
        }

        var transition = World.Transitions.Rent();
        try
        {
            InitObject(transition, admin: false);
            transition.InitPath(Cell, Position, Position);
            return transition.CheckCollisions(other);
        }
        finally
        {
            World.Transitions.Return(transition);
        }
    }

    /// <summary>One entry of retail's <c>collision_table</c> (<c>CPhysicsObj::CollisionRecord</c>).</summary>
    private readonly record struct CollisionRecord(double TouchedTime, bool Ethereal);
}

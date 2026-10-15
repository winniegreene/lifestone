using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Data.Tests.Motion;

/// <summary>The objects target ids resolve to, for the MoveTo tests.</summary>
internal sealed class TestWorld : ITargetWorld
{
    private readonly Dictionary<uint, ITargetable> _objects = [];

    public void Add(ITargetable obj) => _objects[obj.Id] = obj;

    public void Remove(uint id) => _objects.Remove(id);

    public ITargetable? FindObject(uint objectId) => _objects.GetValueOrDefault(objectId);
}

/// <summary>
/// A test object for the MoveTo, sticky and target managers: a motion interpreter on recording parts whose queued
/// motions have no animations, so they complete in the part array's completion check (as a motion table without links
/// would), and a kinematic body that turns at 90°/s × the interpreted turn speed and moves at the interpreter's state
/// velocity. Each <see cref="Tick"/> is one <c>UpdateObjectInternal</c> in retail's order: move, then the target
/// reports, the MoveTo (<c>MovementManager::UseTime</c>) and the completion check (<c>CPartArray::HandleMovement</c>).
/// A motion the MoveTo queues outside <c>CMotionInterp::PerformMovement</c> is therefore still pending during the rest
/// of its update, as in retail.
/// </summary>
internal sealed class MoverHost : IMoveToHost, IMotionInterpHost, IAceMoveToBody
{
    /// <summary>Degrees per second at turn speed 1.</summary>
    public const float TurnRate = 90f;

    private readonly RecordingParts _parts = new();
    private readonly FakeWeenie _weenie = FakeWeenie.RemoteCreature();
    private Vector3 _physicsVelocity;

    public MoverHost(uint id, TestWorld world, CellFrame position, float radius = 0.4f, float height = 1.8f)
    {
        Id = id;
        World = world;
        Position = position;
        Radius = radius;
        Height = height;
        Motion = new MotionInterpreter(_parts, this);
        _parts.Interpreter = Motion;
        _parts.CompleteOnCheck = true;
        Movement = new MovementManager(Motion, this);
        Positions = new PositionManager();
        Targets = new TargetManager(this, world);
        Motion.EnterDefaultState();
        HandleMovement();
        _parts.Calls.Clear();
        world.Add(this);
    }

    public uint Id { get; }

    public ITargetWorld World { get; }

    public CellFrame Position { get; set; }

    public float Radius { get; }

    public float Height { get; }

    public uint TopLevelId => Id;

    public double Time { get; set; }

    public TargetManager Targets { get; }

    public MotionInterpreter Motion { get; }

    MotionInterpreter? IMoveToHost.Motion => Motion;

    public MovementManager Movement { get; }

    public PositionManager Positions { get; }

    /// <summary>The motion-table calls the interpreter made.</summary>
    public List<PartsCall> Calls => _parts.Calls;

    /// <summary>Every report the object handled.</summary>
    public List<TargetInfo> Reports { get; } = [];

    /// <summary>The measured velocity (retail <c>cached_velocity</c>).</summary>
    public Vector3 Velocity { get; private set; }

    /// <summary>A velocity the test sets: the body drifts at it (for a target that moves on its own).</summary>
    public Vector3 Drift { get; set; }

    public float Heading => PositionFrames.Heading(Position.Orientation);

    public bool HasContact { get; set; } = true;

    public bool OnWalkable { get; set; } = true;

    public bool HasGravity => true;

    public bool InCell => true;

    public bool MovementIsAutonomous => false;

    public bool IsFullyConstrained => false;

    public bool IsInterpolating => Positions.IsInterpolating;

    Vector3 IMotionInterpHost.Velocity => _physicsVelocity;

    public Quaternion Orientation => Position.Orientation;

    public IMotionWeenie? Weenie => _weenie;

    public int StopCompletelyCount { get; private set; }

    /// <summary>When set, ACE's MoveToManager drives the body instead of Lifestone's (the oracle runs).</summary>
    public AceMoveToOracle? Oracle { get; set; }

    /// <summary>The statuses ACE's manager reported to its weenie (its server customisation).</summary>
    public List<WeenieError> OracleCompletions { get; } = [];

    public static CellFrame At(float x, float y, float heading = 0f, uint cell = 0xA9B40019) =>
        new(cell, new AcFrame(new Vector3(x, y, 0), PositionFrames.WithHeading(Quaternion.Identity, heading)));

    /// <summary>One update of <paramref name="dt"/> seconds.</summary>
    public void Tick(double dt)
    {
        Time += dt;
        var old = Position;
        var turn = Motion.Interpreted.TurnCommand == MotionCommands.TurnRight ? Motion.Interpreted.TurnSpeed : 0f;
        var heading = (float)((Heading + (TurnRate * turn * dt) + 720.0) % 360.0);
        var local = Motion.GetStateVelocity();
        var orientation = PositionFrames.WithHeading(Position.Orientation, heading);
        var world = Vector3.Transform(local, orientation) + Drift;
        Position = new CellFrame(Position.CellId, new AcFrame(Position.Origin + (world * (float)dt), orientation));
        Velocity = (Position.Origin - old.Origin) / (float)dt;
        Targets.HandleTargetting(Time);
        if (Oracle is { } oracle)
        {
            oracle.UseTime();
        }
        else
        {
            Movement.UseTime();
        }

        HandleMovement();
    }

    /// <summary>
    /// Retail <c>CPartArray::HandleMovement</c>: the part array's completion check, which completes every queued motion
    /// (none has animations).
    /// </summary>
    public void HandleMovement() => _parts.CheckForCompletedMotions();

    public void SetHeading(float degrees) =>
        Position = Position with { Frame = Position.Frame with { Orientation = PositionFrames.WithHeading(Position.Orientation, degrees) } };

    public void StopCompletely()
    {
        StopCompletelyCount++;
        Motion.PerformMovement(MotionMovementType.StopCompletely, 0, new MovementParameters());
    }

    public void CancelMoveTo() => Movement.CancelMoveTo(WeenieError.ActionCancelled);

    public void UnstickFromObject() => Positions.UnStick();

    public void StickTo(uint objectId, float radius, float height) => Positions.StickTo(objectId, radius, height, this);

    public void SetLocalVelocity(Vector3 localVelocity) => _physicsVelocity = Vector3.Transform(localVelocity, Orientation);

    public void SetOnWalkable(bool onWalkable) => OnWalkable = onWalkable;

    public void HandleUpdateTarget(in TargetInfo info)
    {
        Reports.Add(info);
        if (info.ContextId != 0)
        {
            return;
        }

        if (Oracle is { } oracle)
        {
            oracle.HandleUpdateTarget(info);
        }
        else
        {
            Movement.HandleUpdateTarget(info);
        }

        Positions.HandleUpdateTarget(info);
    }

    // ----- ACE's PhysicsObj, for the oracle -----

    uint IAceMoveToBody.ID => Id;

    double IAceMoveToBody.CurrentTime => Time;

    bool IAceMoveToBody.IsAnimating => Motion.MotionsPending;

    Vector3 IAceMoveToBody.CachedVelocity => Velocity;

    float IAceMoveToBody.GetRadius() => Radius;

    float IAceMoveToBody.GetHeight() => Height;

    float IAceMoveToBody.get_heading() => Heading;

    void IAceMoveToBody.set_heading(float heading, bool sendEvent) => SetHeading(heading);

    void IAceMoveToBody.StopCompletely(bool sendEvent) => StopCompletely();

    bool IAceMoveToBody.IsInterpolating() => IsInterpolating;

    MotionInterpreter? IAceMoveToBody.get_minterp() => Motion;

    void IAceMoveToBody.set_target(uint contextId, uint objectId, float radius, double quantum) => Targets.SetTarget(contextId, objectId, radius, quantum);

    void IAceMoveToBody.clear_target() => Targets.ClearTarget();

    double IAceMoveToBody.get_target_quantum() => Targets.TargetQuantum;

    void IAceMoveToBody.set_target_quantum(double quantum) => Targets.SetTargetQuantum(quantum);

    void IAceMoveToBody.unstick_from_object() => Positions.UnStick();

    void IAceMoveToBody.OnMoveComplete(WeenieError status) => OracleCompletions.Add(status);

    /// <summary>The do/stop calls, as "do TurnRight", "stop WalkForward"…, without the speeds.</summary>
    public string[] Commands() => Calls.Where(c => c.Kind is "do" or "stop").Select(c => $"{c.Kind} {MotionCommandTable.NameOf(c.Motion)}").ToArray();
}

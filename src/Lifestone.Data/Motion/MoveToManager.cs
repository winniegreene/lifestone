using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Data.Motion;

/// <summary>
/// What the MoveTo and sticky managers ask of the object they move (retail <c>CPhysicsObj</c>, as
/// <c>MoveToManager</c> and <c>StickyManager</c> use it). The avatar body implements it for server objects; the physics
/// track's player object for the player (a server MoveTo after an out-of-range Use).
/// </summary>
public interface IMoveToHost : ITargetable
{
    /// <summary>Retail <c>CPhysicsObj::get_heading</c> (0x00511540 / 0x00512010): the compass heading of the object.</summary>
    float Heading { get; }

    /// <summary>Retail <c>CPhysicsObj::set_heading(h, send_event 1)</c> (0x00514160 / 0x00514C60).</summary>
    void SetHeading(float degrees);

    /// <summary>Retail <c>transient_state</c> Contact: a MoveTo advances only in contact.</summary>
    bool HasContact { get; }

    /// <summary>Retail <c>CPhysicsObj::IsInterpolating</c> (0x0050EB50 / 0x0050F620).</summary>
    bool IsInterpolating { get; }

    /// <summary>Retail <c>CPhysicsObj::get_minterp</c>: the object's motion interpreter, or null.</summary>
    MotionInterpreter? Motion { get; }

    /// <summary>Retail <c>CPhysicsObj::StopCompletely(0)</c> (0x00510180 / 0x00510C50): a type-5 movement.</summary>
    void StopCompletely();

    /// <summary>Retail <c>CPhysicsObj::cancel_moveto</c> (0x005101F0 / 0x00510CC0): cancels a MoveTo (error 0x36).</summary>
    void CancelMoveTo();

    /// <summary>Retail <c>CPhysicsObj::unstick_from_object</c> (0x0050EAE0 / 0x0050F5B0).</summary>
    void UnstickFromObject();

    /// <summary>Retail <c>PositionManager::StickTo</c> through <c>CPhysicsObj::get_position_manager</c>.</summary>
    void StickTo(uint objectId, float radius, float height);

    /// <summary>The objects a target id can name (retail <c>CPhysicsObj::GetObjectA</c>).</summary>
    ITargetWorld World { get; }
}

/// <summary>The steps a MoveTo queues (retail <c>MovementNode</c> types).</summary>
public enum MoveToNodeType
{
    /// <summary>7: walk or run to the target (<c>BeginMoveForward</c>, <c>HandleMoveToPosition</c>).</summary>
    MoveToPosition = 7,

    /// <summary>9: turn to a heading (<c>BeginTurnToHeading</c>, <c>HandleTurnToHeading</c>).</summary>
    TurnToHeading = 9,
}

/// <summary>One queued MoveTo step.</summary>
/// <param name="Type">The step.</param>
/// <param name="Heading">The heading of a <see cref="MoveToNodeType.TurnToHeading"/> step, in degrees.</param>
public readonly record struct MoveToNode(MoveToNodeType Type, float Heading);

/// <summary>
/// Retail <c>MoveToManager</c>, re-expressed from the 11.4186 decompile (PORTAL/canim/CMoveToManager.c; names and
/// addresses cited, nothing copied): carries out the server's MoveToObject, MoveToPosition, TurnToObject and
/// TurnToHeading movements (types 6-9) by issuing turn and walk/run commands to the motion interpreter until the object
/// gets there, as a creature chases the player, an NPC turns to face it, or the player walks to a portal it used from
/// too far. Addresses are 11.4186 / 11.6096.
/// </summary>
/// <remarks>
/// <para><b>How it moves.</b> A MoveTo queues steps: turn to the target, move to it, and turn to a final heading when
/// <see cref="MovementParameters.UseFinalHeading"/>. Turning issues TurnRight or TurnLeft until the heading passes the
/// wanted one, then snaps to it. Moving issues WalkForward (with the run key beyond the walk/run threshold, or WalkBackwards
/// to back away) and steers with an auxiliary turn whenever the heading is more than 20° off; it arrives within
/// <see cref="MovementParameters.DistanceToObject"/> (or beyond the minimum distance when moving away) and fails past the
/// fail distance. A MoveToObject first asks the target for reports (<see cref="TargetManager"/>) and starts on the first
/// Ok; later reports retarget it. With <see cref="MovementParameters.Sticky"/>, arriving sticks to the target.</para>
/// <para><b>Time.</b> Retail reads <c>Timer::cur_time</c>; here <see cref="ITargetable.Time"/> of the host.</para>
/// <para><b>Where ACE differs</b> (its MoveToManager.cs is the test oracle; docs/avatars.md lists the differences, each
/// with a test): ACE waits on <c>IsAnimating</c> where retail waits on <c>motions_pending</c>; ACE's
/// <see cref="UseTime"/> runs while a target is set or a movement is under way, retail's except while a target has not
/// reported yet; ACE's <see cref="TurnToHeading"/> does not begin the turn; ACE arrives early within 1 m when the distance
/// grows (a server-tick allowance), snaps the heading when the auxiliary turn stops, and resets the distance to object
/// when it cleans up where retail clears the flags.</para>
/// <para>Not thread-safe. Allocates only when a MoveTo is issued (its step list grows once).</para>
/// </remarks>
public sealed class MoveToManager
{
    /// <summary>The auxiliary turn starts when the heading is more than this many degrees off.</summary>
    public const float SteeringTolerance = 20f;

    /// <summary>Progress below this many metres per second over a second fails the progress check.</summary>
    public const float MinProgressSpeed = 0.25f;

    /// <summary>The radius a voyeur report needs to move by before the target reports again (0.5 m).</summary>
    public const float TargetRadius = 0.5f;

    private readonly IMoveToHost _host;
    private readonly List<MoveToNode> _pending = [];

    /// <summary>Retail <c>MoveToManager::Create</c> (0x00529470 / 0x0052A080) and the constructor (0x005293B0 / 0x00529FC0).</summary>
    public MoveToManager(IMoveToHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        StartingPosition = Origin;
        MovementParams = new MovementParameters();
        InitializeLocalVariables();
    }

    private static CellFrame Origin => new(0, AcFrame.Identity);

    /// <summary>Retail <c>movement_type</c>: 0, or the type under way (6-9).</summary>
    public WireMovementType MovementType { get; private set; }

    /// <summary>Retail <c>sought_position</c>.</summary>
    public CellFrame SoughtPosition { get; private set; }

    /// <summary>Retail <c>current_target_position</c>: the position the distance is measured to.</summary>
    public CellFrame CurrentTargetPosition { get; private set; }

    /// <summary>Retail <c>starting_position</c>: where the MoveTo began (for the fail distance).</summary>
    public CellFrame StartingPosition { get; private set; }

    /// <summary>Retail <c>movement_params</c>.</summary>
    public MovementParameters MovementParams { get; private set; }

    /// <summary>Retail <c>previous_heading</c>.</summary>
    public float PreviousHeading { get; private set; }

    /// <summary>Retail <c>previous_distance</c>.</summary>
    public float PreviousDistance { get; private set; }

    /// <summary>Retail <c>previous_distance_time</c>.</summary>
    public double PreviousDistanceTime { get; private set; }

    /// <summary>Retail <c>original_distance</c>.</summary>
    public float OriginalDistance { get; private set; }

    /// <summary>Retail <c>original_distance_time</c>.</summary>
    public double OriginalDistanceTime { get; private set; }

    /// <summary>Retail <c>fail_progress_count</c>: checks without progress (retail never reads it back).</summary>
    public int FailProgressCount { get; private set; }

    /// <summary>Retail <c>sought_object_id</c>.</summary>
    public uint SoughtObjectId { get; private set; }

    /// <summary>Retail <c>top_level_object_id</c>: the object whose reports are awaited.</summary>
    public uint TopLevelObjectId { get; private set; }

    /// <summary>Retail <c>sought_object_radius</c>.</summary>
    public float SoughtObjectRadius { get; private set; }

    /// <summary>Retail <c>sought_object_height</c>.</summary>
    public float SoughtObjectHeight { get; private set; }

    /// <summary>Retail <c>current_command</c>: the turn or walk the current step issued.</summary>
    public uint CurrentCommand { get; private set; }

    /// <summary>Retail <c>aux_command</c>: the steering turn while walking.</summary>
    public uint AuxCommand { get; private set; }

    /// <summary>Retail <c>moving_away</c>.</summary>
    public bool MovingAway { get; private set; }

    /// <summary>Retail <c>initialized</c>: the target has reported and the steps are queued.</summary>
    public bool Initialized { get; private set; }

    /// <summary>The queued steps, oldest first (retail <c>pending_actions</c>).</summary>
    public IReadOnlyList<MoveToNode> PendingActions => _pending;

    /// <summary>
    /// Why the last MoveTo was cancelled (the error retail passes to <c>CancelMoveTo</c>), or
    /// <see cref="WeenieError.None"/>. Retail drops the value; it is kept for diagnostics only.
    /// </summary>
    public WeenieError LastCancelReason { get; private set; }

    /// <summary>
    /// How many MoveTos <see cref="CancelMoveTo"/> has cancelled (Lifestone's diagnostic, M5 item 5 F6; retail keeps no count): a reader that noted it when a
    /// MoveTo began tells a cancel (with <see cref="LastCancelReason"/>) from a MoveTo that ended by arriving.
    /// </summary>
    public int Cancels { get; private set; }

    /// <summary>Retail <c>is_moving_to</c> (0x00529220 / 0x00529E30): a movement is under way.</summary>
    public bool IsMovingTo => MovementType != 0;

    /// <summary>
    /// Retail <c>PerformMovement</c> (0x0052A900 / 0x0052B510) for type 6 through <c>CPhysicsObj::MoveToObject</c>:
    /// cancels the MoveTo under way (0x36), ends a stick, then <see cref="MoveToObject"/>.
    /// </summary>
    public void PerformMoveToObject(uint objectId, uint topLevelId, float objectRadius, float objectHeight, in MovementParameters parameters)
    {
        CancelMoveTo(WeenieError.ActionCancelled);
        _host.UnstickFromObject();
        MoveToObject(objectId, topLevelId, objectRadius, objectHeight, parameters);
    }

    /// <summary><see cref="PerformMoveToObject"/> for type 7.</summary>
    public void PerformMoveToPosition(in CellFrame position, in MovementParameters parameters)
    {
        CancelMoveTo(WeenieError.ActionCancelled);
        _host.UnstickFromObject();
        MoveToPosition(position, parameters);
    }

    /// <summary><see cref="PerformMoveToObject"/> for type 8.</summary>
    public void PerformTurnToObject(uint objectId, uint topLevelId, in MovementParameters parameters)
    {
        CancelMoveTo(WeenieError.ActionCancelled);
        _host.UnstickFromObject();
        TurnToObject(objectId, topLevelId, parameters);
    }

    /// <summary><see cref="PerformMoveToObject"/> for type 9.</summary>
    public void PerformTurnToHeading(in MovementParameters parameters)
    {
        CancelMoveTo(WeenieError.ActionCancelled);
        _host.UnstickFromObject();
        TurnToHeading(parameters);
    }

    /// <summary>
    /// Retail <c>MoveToObject</c> (0x00529680 / 0x0052A290): stops completely, records the start, the target and its
    /// cylinder and the parameters, and asks the top-level object for reports (radius 0.5, quantum 0); the steps are
    /// queued on its first Ok (<see cref="HandleUpdateTarget"/>). Moving to oneself cleans up and stops instead.
    /// </summary>
    public void MoveToObject(uint objectId, uint topLevelId, float objectRadius, float objectHeight, in MovementParameters parameters)
    {
        _host.StopCompletely();
        StartingPosition = _host.Position;
        SoughtObjectId = objectId;
        SoughtObjectRadius = objectRadius;
        SoughtObjectHeight = objectHeight;
        MovementType = WireMovementType.MoveToObject;
        TopLevelObjectId = topLevelId;
        MovementParams = parameters;
        Initialized = false;
        if (topLevelId != _host.Id)
        {
            _host.Targets.SetTarget(0, TopLevelObjectId, TargetRadius, 0.0);
            return;
        }

        CleanUp();
        _host.StopCompletely();
    }

    /// <summary>
    /// Retail <c>TurnToObject</c> (0x005297D0 / 0x0052A3E0): stops completely when the parameters say so, records the
    /// target and the parameters (and, as retail, the desired heading on the current target's frame, which the first
    /// report replaces), and asks the top-level object for reports. Turning to oneself cleans up and stops instead.
    /// </summary>
    public void TurnToObject(uint objectId, uint topLevelId, in MovementParameters parameters)
    {
        if (parameters.StopCompletelyOnArrival)
        {
            _host.StopCompletely();
        }

        MovementType = WireMovementType.TurnToObject;
        SoughtObjectId = objectId;
        CurrentTargetPosition = CurrentTargetPosition with
        {
            Frame = CurrentTargetPosition.Frame with
            {
                Orientation = PositionFrames.WithHeading(CurrentTargetPosition.Orientation, parameters.DesiredHeading),
            },
        };
        TopLevelObjectId = topLevelId;
        MovementParams = parameters;
        if (topLevelId != _host.Id)
        {
            Initialized = false;
            _host.Targets.SetTarget(0, topLevelId, TargetRadius, 0.0);
            return;
        }

        CleanUp();
        _host.StopCompletely();
    }

    /// <summary>
    /// Retail <c>MoveToPosition</c> (0x0052A240 / 0x0052AE50): stops completely, then queues a turn towards the position
    /// and a move to it when the parameters' command there is not none, and a final turn when
    /// <see cref="MovementParameters.UseFinalHeading"/>; records the position, the start and the parameters (without the
    /// Sticky flag) and begins. The command is chosen with the new parameters but the distance measured with the old
    /// ones' sphere flag, as in retail.
    /// </summary>
    public void MoveToPosition(in CellFrame position, in MovementParameters parameters)
    {
        _host.StopCompletely();
        CurrentTargetPosition = position;
        SoughtObjectRadius = 0f;
        var distance = GetCurrentDistance();
        var heading = RelativeHeading(PositionFrames.HeadingTo(_host.Position, position));
        var movingAway = false;
        parameters.GetCommand(distance, heading, out var command, out _, ref movingAway);
        if (command != 0)
        {
            AddTurnToHeadingNode(PositionFrames.HeadingTo(_host.Position, position));
            AddMoveToPositionNode();
        }

        if (parameters.UseFinalHeading)
        {
            AddTurnToHeadingNode(parameters.DesiredHeading);
        }

        SoughtPosition = position;
        StartingPosition = _host.Position;
        MovementType = WireMovementType.MoveToPosition;
        MovementParams = parameters.WithFlag(MovementParameterFlags.Sticky, false);
        BeginNextNode();
    }

    /// <summary>
    /// Retail <c>TurnToHeading</c> (0x0052A630 / 0x0052B240): stops completely when the parameters say so, records them
    /// (without Sticky) and the heading, queues the turn and begins it.
    /// </summary>
    public void TurnToHeading(in MovementParameters parameters)
    {
        if (parameters.StopCompletelyOnArrival)
        {
            _host.StopCompletely();
        }

        MovementParams = parameters.WithFlag(MovementParameterFlags.Sticky, false);
        SoughtPosition = SoughtPosition with
        {
            Frame = SoughtPosition.Frame with
            {
                Orientation = PositionFrames.WithHeading(SoughtPosition.Orientation, parameters.DesiredHeading),
            },
        };
        MovementType = WireMovementType.TurnToHeading;
        _pending.Add(new MoveToNode(MoveToNodeType.TurnToHeading, parameters.DesiredHeading));
        BeginNextNode();
    }

    /// <summary>
    /// Retail <c>CancelMoveTo</c> (0x00529930 / 0x0052A540): with a movement under way, drops its steps, cleans up and
    /// stops completely.
    /// </summary>
    public void CancelMoveTo(WeenieError reason)
    {
        if (MovementType == 0)
        {
            return;
        }

        LastCancelReason = reason;
        Cancels++;
        _pending.Clear();
        CleanUp();
        _host.StopCompletely();
    }

    /// <summary>Retail <c>HitGround</c> (0x00529D70; TODO(avatars): 11.6096): landing resumes the steps.</summary>
    public void HitGround()
    {
        if (MovementType != 0)
        {
            BeginNextNode();
        }
    }

    /// <summary>
    /// Retail <c>UseTime</c> (0x0052A780; TODO(avatars): 11.6096), run from each object update: in contact, with a step
    /// queued, and unless a target has been asked for and has not reported yet, the step advances
    /// (<see cref="HandleMoveToPosition"/> or <see cref="HandleTurnToHeading"/>).
    /// </summary>
    public void UseTime()
    {
        if (!_host.HasContact || _pending.Count == 0)
        {
            return;
        }

        if (TopLevelObjectId == 0 || MovementType == 0 || Initialized)
        {
            switch (_pending[0].Type)
            {
                case MoveToNodeType.MoveToPosition:
                    HandleMoveToPosition();
                    break;
                case MoveToNodeType.TurnToHeading:
                    HandleTurnToHeading();
                    break;
            }
        }
    }

    /// <summary>
    /// Retail <c>HandleUpdateTarget</c> (0x0052A7D0 / 0x0052B3E0): a report from the awaited object. Once initialised, an
    /// Ok report retargets a MoveToObject (and resets its progress measure); anything else cancels (0x37). Before that,
    /// a target that is the object itself completes at once; an Ok report queues the steps
    /// (<see cref="MoveToObjectInternal"/> or <see cref="TurnToObjectInternal"/>); anything else cancels (0x38).
    /// </summary>
    public void HandleUpdateTarget(in TargetInfo info)
    {
        if (TopLevelObjectId != info.ObjectId)
        {
            return;
        }

        if (Initialized)
        {
            if (info.Status == TargetStatus.Ok)
            {
                if (MovementType == WireMovementType.MoveToObject)
                {
                    SoughtPosition = info.InterpolatedPosition;
                    CurrentTargetPosition = info.TargetPosition;
                    PreviousDistance = float.MaxValue;
                    PreviousDistanceTime = _host.Time;
                    OriginalDistance = float.MaxValue;
                    OriginalDistanceTime = _host.Time;
                }
            }
            else
            {
                CancelMoveTo(WeenieError.ObjectGone);
            }
        }
        else if (TopLevelObjectId == _host.Id)
        {
            SoughtPosition = _host.Position;
            CurrentTargetPosition = _host.Position;
            CleanUpAndCallWeenie();
        }
        else if (info.Status == TargetStatus.Ok)
        {
            if (MovementType == WireMovementType.MoveToObject)
            {
                MoveToObjectInternal(info.TargetPosition, info.InterpolatedPosition);
            }
            else if (MovementType == WireMovementType.TurnToObject)
            {
                TurnToObjectInternal(info.TargetPosition);
            }
        }
        else
        {
            CancelMoveTo(WeenieError.NoObject);
        }
    }

    /// <summary>
    /// Retail <c>MoveToObject_Internal</c> (0x0052A400 / 0x0052B010): the first report of a MoveToObject queues a turn
    /// towards the predicted position and a move to it when needed, and a final turn (relative to that direction) when
    /// <see cref="MovementParameters.UseFinalHeading"/>, then begins.
    /// </summary>
    public void MoveToObjectInternal(in CellFrame targetPosition, in CellFrame interpolatedPosition)
    {
        SoughtPosition = interpolatedPosition;
        CurrentTargetPosition = targetPosition;
        var headingToObject = PositionFrames.HeadingTo(_host.Position, interpolatedPosition);
        var distance = GetCurrentDistance();
        var heading = RelativeHeading(headingToObject);
        var movingAway = false;
        MovementParams.GetCommand(distance, heading, out var command, out _, ref movingAway);
        if (command != 0)
        {
            AddTurnToHeadingNode(headingToObject);
            AddMoveToPositionNode();
        }

        if (MovementParams.UseFinalHeading)
        {
            var final = headingToObject + MovementParams.DesiredHeading;
            if (final >= 360f)
            {
                final -= 360f;
            }

            AddTurnToHeadingNode(final);
        }

        Initialized = true;
        BeginNextNode();
    }

    /// <summary>
    /// Retail <c>TurnToObject_Internal</c> (0x0052A550 / 0x0052B160): the first report of a TurnToObject queues a turn to
    /// the direction of the target plus the sought position's heading (0 unless set), then begins.
    /// </summary>
    public void TurnToObjectInternal(in CellFrame targetPosition)
    {
        CurrentTargetPosition = targetPosition;
        var sought = PositionFrames.Heading(SoughtPosition.Orientation);
        var heading = (float)((PositionFrames.HeadingTo(_host.Position, CurrentTargetPosition) + (double)sought) % 360.0);
        SoughtPosition = SoughtPosition with
        {
            Frame = SoughtPosition.Frame with { Orientation = PositionFrames.WithHeading(SoughtPosition.Orientation, heading) },
        };
        _pending.Add(new MoveToNode(MoveToNodeType.TurnToHeading, heading));
        Initialized = true;
        BeginNextNode();
    }

    /// <summary>
    /// Retail <c>GetCurrentDistance</c> (0x005291B0 / 0x00529DC0): with <see cref="MovementParameters.UseSpheres"/>,
    /// the cylinder distance between the object and the target; otherwise the distance between their positions.
    /// </summary>
    public float GetCurrentDistance() => MovementParams.UseSpheres
        ? PositionFrames.CylinderDistance(_host.Radius, _host.Height, _host.Position, SoughtObjectRadius, SoughtObjectHeight, CurrentTargetPosition)
        : PositionFrames.Distance(_host.Position, CurrentTargetPosition);

    /// <summary>
    /// Retail <c>CheckProgressMade</c> (0x005290F0 / 0x00529D00): more than a second after the last check, the distance
    /// must have closed (or opened, moving away) at <see cref="MinProgressSpeed"/> since then and since the start;
    /// otherwise no progress. A check that passes the first test records the distance.
    /// </summary>
    public bool CheckProgressMade(float currentDistance)
    {
        var now = _host.Time;
        var elapsed = now - PreviousDistanceTime;
        if (elapsed <= 1.0)
        {
            return true;
        }

        var closed = MovingAway ? currentDistance - PreviousDistance : PreviousDistance - currentDistance;
        if (closed / elapsed < MinProgressSpeed)
        {
            return false;
        }

        PreviousDistance = currentDistance;
        PreviousDistanceTime = now;
        var total = MovingAway ? currentDistance - OriginalDistance : OriginalDistance - currentDistance;
        return total / (now - OriginalDistanceTime) >= MinProgressSpeed;
    }

    /// <summary>
    /// Retail <c>heading_greater</c> (0x00528F60 / 0x00529B70): whether heading <paramref name="x"/> has passed
    /// <paramref name="y"/> for a turn in <paramref name="motion"/>'s direction, the shorter way round the circle: for
    /// TurnRight, x is beyond y clockwise; for any other turn, x is at or before y.
    /// </summary>
    public static bool HeadingGreater(float x, float y, uint motion)
    {
        bool lessOrEqual = Math.Abs(x - y) <= 180.0 ? x <= y : y <= x;
        return motion == MotionCommands.TurnRight ? !lessOrEqual : lessOrEqual;
    }

    /// <summary>
    /// Retail <c>heading_diff</c> (0x00528FB0 / 0x00529BC0): <paramref name="x"/> − <paramref name="y"/> in [0, 360),
    /// 0 within 0.0002, and measured the other way round for any turn but TurnRight.
    /// </summary>
    public static float HeadingDiff(float x, float y, uint motion)
    {
        double result = x - y;
        if (Math.Abs(result) < MotionInterpreter.Epsilon)
        {
            result = 0.0;
        }

        if (result < -MotionInterpreter.Epsilon)
        {
            result += 360.0;
        }

        if (result > MotionInterpreter.Epsilon && motion != MotionCommands.TurnRight)
        {
            result = 360.0 - result;
        }

        return (float)result;
    }

    /// <summary>
    /// Retail <c>InitializeLocalVariables</c> (0x00529250 / 0x00529E60): no movement, the parameters' flags and context
    /// cleared (their distances kept), the progress measures reset, no commands, positions and target cleared.
    /// </summary>
    private void InitializeLocalVariables()
    {
        MovementType = 0;
        MovementParams = MovementParams with { Flags = MovementParameterFlags.None, ContextId = 0 };
        PreviousDistance = float.MaxValue;
        PreviousDistanceTime = _host.Time;
        OriginalDistance = float.MaxValue;
        OriginalDistanceTime = _host.Time;
        PreviousHeading = 0f;
        FailProgressCount = 0;
        CurrentCommand = 0;
        AuxCommand = 0;
        MovingAway = false;
        Initialized = false;
        SoughtPosition = Origin;
        CurrentTargetPosition = Origin;
        SoughtObjectId = 0;
        TopLevelObjectId = 0;
        SoughtObjectRadius = 0f;
        SoughtObjectHeight = 0f;
    }

    /// <summary>Retail <c>AddTurnToHeadingNode</c> (0x00529530 / 0x0052A140).</summary>
    private void AddTurnToHeadingNode(float heading) => _pending.Add(new MoveToNode(MoveToNodeType.TurnToHeading, heading));

    /// <summary>Retail <c>AddMoveToPositionNode</c> (0x00529580 / 0x0052A190).</summary>
    private void AddMoveToPositionNode() => _pending.Add(new MoveToNode(MoveToNodeType.MoveToPosition, 0f));

    /// <summary>Retail <c>RemovePendingActionsHead</c> (0x00529380 / 0x00529F90).</summary>
    private void RemovePendingActionsHead()
    {
        if (_pending.Count > 0)
        {
            _pending.RemoveAt(0);
        }
    }

    /// <summary>
    /// Retail <c>CleanUp</c> (0x005295C0 / 0x0052A1D0): stops the current and auxiliary commands (with the hold key in
    /// use, without cancelling a MoveTo), clears the target when one was asked for, and resets the local variables.
    /// </summary>
    private void CleanUp()
    {
        var parameters = StopParameters();
        if (CurrentCommand != 0)
        {
            StopMotion(CurrentCommand, parameters);
        }

        if (AuxCommand != 0)
        {
            StopMotion(AuxCommand, parameters);
        }

        if (TopLevelObjectId != 0 && MovementType != 0)
        {
            _host.Targets.ClearTarget();
        }

        InitializeLocalVariables();
    }

    /// <summary>
    /// Retail <c>CleanUpAndCallWeenie</c> (0x00529650 / 0x0052A260): <see cref="CleanUp"/>, then stop completely (the
    /// 11.4186 body makes no weenie call).
    /// </summary>
    private void CleanUpAndCallWeenie()
    {
        CleanUp();
        _host.StopCompletely();
    }

    /// <summary>
    /// Retail <c>BeginNextNode</c> (0x00529CB0 / 0x0052A8C0): begins the next step; with none left, the MoveTo ends:
    /// cleaned up and stopped, and stuck to the target when <see cref="MovementParameters.Sticky"/>.
    /// </summary>
    private void BeginNextNode()
    {
        if (_pending.Count > 0)
        {
            switch (_pending[0].Type)
            {
                case MoveToNodeType.MoveToPosition:
                    BeginMoveForward();
                    break;
                case MoveToNodeType.TurnToHeading:
                    BeginTurnToHeading();
                    break;
            }

            return;
        }

        if (!MovementParams.Sticky)
        {
            CleanUp();
            _host.StopCompletely();
            return;
        }

        var height = SoughtObjectHeight;
        var target = TopLevelObjectId;
        var radius = SoughtObjectRadius;
        CleanUp();
        _host.StopCompletely();
        _host.StickTo(target, radius, height);
    }

    /// <summary>
    /// Retail <c>BeginMoveForward</c> (0x00529A00 / 0x0052A610): the command at the current distance; none means the step
    /// is done. Otherwise it is issued with the chosen hold key at the parameters' speed (a failure cancels with its
    /// error) and the progress measure starts.
    /// </summary>
    private void BeginMoveForward()
    {
        var distance = GetCurrentDistance();
        var heading = RelativeHeading(PositionFrames.HeadingTo(_host.Position, CurrentTargetPosition));
        var movingAway = MovingAway;
        MovementParams.GetCommand(distance, heading, out var command, out var holdKey, ref movingAway);
        if (command == 0)
        {
            RemovePendingActionsHead();
            BeginNextNode();
            return;
        }

        var parameters = new MovementParameters { HoldKeyToApply = holdKey, Speed = MovementParams.Speed }
            .WithFlag(MovementParameterFlags.CancelMoveTo, false);
        var result = DoMotion(command, parameters);
        if (result != WeenieError.None)
        {
            CancelMoveTo(result);
            return;
        }

        var now = _host.Time;
        CurrentCommand = command;
        MovingAway = movingAway;
        MovementParams = MovementParams with { HoldKeyToApply = holdKey };
        PreviousDistance = distance;
        PreviousDistanceTime = now;
        OriginalDistance = distance;
        OriginalDistanceTime = now;
    }

    /// <summary>
    /// Retail <c>BeginTurnToHeading</c> (0x00529B90 / 0x0052A7A0): waits while motions are pending; then turns right
    /// when the wanted heading is up to 180° clockwise and more than 0.0002 away, left when it is the other way and short
    /// of a full circle, and otherwise the step is done.
    /// </summary>
    private void BeginTurnToHeading()
    {
        if (_pending.Count == 0)
        {
            CancelMoveTo(WeenieError.NoPhysicsObject);
            return;
        }

        if (_host.Motion?.MotionsPending == true)
        {
            return;
        }

        var difference = HeadingDiff(_pending[0].Heading, _host.Heading, MotionCommands.TurnRight);
        uint command;
        if (difference <= 180f)
        {
            if (!(difference > MotionInterpreter.Epsilon))
            {
                RemovePendingActionsHead();
                BeginNextNode();
                return;
            }

            command = MotionCommands.TurnRight;
        }
        else
        {
            if (difference + MotionInterpreter.Epsilon >= 360f)
            {
                RemovePendingActionsHead();
                BeginNextNode();
                return;
            }

            command = MotionCommands.TurnLeft;
        }

        var parameters = new MovementParameters { Speed = MovementParams.Speed, HoldKeyToApply = MovementParams.HoldKeyToApply }
            .WithFlag(MovementParameterFlags.CancelMoveTo, false);
        var result = DoMotion(command, parameters);
        if (result != WeenieError.None)
        {
            CancelMoveTo(result);
            return;
        }

        CurrentCommand = command;
        PreviousHeading = difference;
    }

    /// <summary>
    /// Retail <c>HandleMoveToPosition</c> (0x00529D80 / 0x0052A990), one update of a move step.
    /// <list type="number">
    /// <item>Steering: while motions are pending the auxiliary turn stops; otherwise, when the heading towards the target
    /// (plus 180 moving away) is more than 20° off, TurnRight or TurnLeft (from 180° on) is issued as the auxiliary
    /// command, and within 20° it stops.</item>
    /// <item>Progress: without progress (and neither interpolating nor animating) the failure count rises; with progress,
    /// arriving (within the distance to object, or past the minimum distance moving away) stops both commands and begins
    /// the next step, and going past the fail distance from the start cancels (0x3D).</item>
    /// <item>A MoveToObject moving faster than 0.1 m/s asks the target for predictions as far ahead as it needs to get
    /// there, when that differs from the current one by more than a second.</item>
    /// </list>
    /// </summary>
    private void HandleMoveToPosition()
    {
        var current = _host.Position;
        var parameters = new MovementParameters { Speed = MovementParams.Speed, HoldKeyToApply = MovementParams.HoldKeyToApply }
            .WithFlag(MovementParameterFlags.CancelMoveTo, false);
        if (_host.Motion?.MotionsPending == true)
        {
            if (AuxCommand != 0)
            {
                StopMotion(AuxCommand, parameters);
                AuxCommand = 0;
            }
        }
        else
        {
            var wanted = PositionFrames.HeadingTo(current, CurrentTargetPosition) + MovementParameters.GetDesiredHeading(CurrentCommand, MovingAway);
            if (wanted >= 360f)
            {
                wanted -= 360f;
            }

            var off = RelativeHeading(wanted);
            if (off <= SteeringTolerance || off >= 360f - SteeringTolerance)
            {
                if (AuxCommand != 0)
                {
                    StopMotion(AuxCommand, parameters);
                    AuxCommand = 0;
                }
            }
            else
            {
                var turn = off >= 180f ? MotionCommands.TurnLeft : MotionCommands.TurnRight;
                if (turn != AuxCommand)
                {
                    DoMotion(turn, parameters);
                    AuxCommand = turn;
                }
            }
        }

        var distance = GetCurrentDistance();
        if (!CheckProgressMade(distance))
        {
            if (!_host.IsInterpolating && _host.Motion?.MotionsPending != true)
            {
                FailProgressCount++;
            }
        }
        else
        {
            FailProgressCount = 0;
            var arrived = MovingAway ? distance >= MovementParams.MinDistance : distance <= MovementParams.DistanceToObject;
            if (arrived)
            {
                RemovePendingActionsHead();
                StopMotion(CurrentCommand, parameters);
                CurrentCommand = 0;
                if (AuxCommand != 0)
                {
                    StopMotion(AuxCommand, parameters);
                    AuxCommand = 0;
                }

                BeginNextNode();
            }
            else if (PositionFrames.Distance(StartingPosition, _host.Position) > MovementParams.FailDistance)
            {
                CancelMoveTo(WeenieError.YouChargedTooFar);
            }
        }

        if (TopLevelObjectId != 0 && MovementType != 0)
        {
            var speed = _host.Velocity.Length();
            if (speed > 0.1f)
            {
                var timeToGetThere = distance / (double)speed;
                if (Math.Abs(timeToGetThere - _host.Targets.TargetQuantum) > 1.0)
                {
                    _host.Targets.SetTargetQuantum(timeToGetThere);
                }
            }
        }
    }

    /// <summary>
    /// Retail <c>HandleTurnToHeading</c> (0x0052A0C0 / 0x0052ACD0), one update of a turn step: without a turn under way it
    /// begins one; once the heading passes the wanted one the object snaps to it, the turn stops and the next step begins;
    /// otherwise a turn that did not advance (or went more than half round) counts a failure unless interpolating or
    /// animating.
    /// </summary>
    private void HandleTurnToHeading()
    {
        if (CurrentCommand != MotionCommands.TurnRight && CurrentCommand != MotionCommands.TurnLeft)
        {
            BeginTurnToHeading();
            return;
        }

        var wanted = _pending[0].Heading;
        var heading = _host.Heading;
        if (HeadingGreater(heading, wanted, CurrentCommand))
        {
            FailProgressCount = 0;
            _host.SetHeading(wanted);
            RemovePendingActionsHead();
            var parameters = new MovementParameters { HoldKeyToApply = MovementParams.HoldKeyToApply }
                .WithFlag(MovementParameterFlags.CancelMoveTo, false);
            StopMotion(CurrentCommand, parameters);
            CurrentCommand = 0;
            BeginNextNode();
            return;
        }

        var turned = HeadingDiff(heading, PreviousHeading, CurrentCommand);
        if (turned >= 180f || turned <= MotionInterpreter.Epsilon)
        {
            PreviousHeading = heading;
            if (!_host.IsInterpolating && _host.Motion?.MotionsPending != true)
            {
                FailProgressCount++;
            }
        }
        else
        {
            FailProgressCount = 0;
            PreviousHeading = heading;
        }
    }

    /// <summary>
    /// Retail <c>_DoMotion</c> (0x00529010 / 0x00529C20): adjusts the motion with the parameters' hold key
    /// (<see cref="MotionInterpreter.AdjustMotion"/>) and plays it as an interpreted motion. Without an interpreter:
    /// <see cref="WeenieError.NoMotionInterpreter"/>.
    /// </summary>
    private WeenieError DoMotion(uint motion, MovementParameters parameters)
    {
        if (_host.Motion is not { } interpreter)
        {
            return WeenieError.NoMotionInterpreter;
        }

        var speed = parameters.Speed;
        interpreter.AdjustMotion(ref motion, ref speed, parameters.HoldKeyToApply);
        return interpreter.DoInterpretedMotion(motion, parameters with { Speed = speed });
    }

    /// <summary>Retail <c>_StopMotion</c> (0x00529080 / 0x00529C90), as <see cref="DoMotion"/> for a stop.</summary>
    private WeenieError StopMotion(uint motion, MovementParameters parameters)
    {
        if (_host.Motion is not { } interpreter)
        {
            return WeenieError.NoMotionInterpreter;
        }

        var speed = parameters.Speed;
        interpreter.AdjustMotion(ref motion, ref speed, parameters.HoldKeyToApply);
        return interpreter.StopInterpretedMotion(motion, parameters with { Speed = speed });
    }

    /// <summary>The parameters <see cref="CleanUp"/> stops with: the hold key in use, without cancelling a MoveTo.</summary>
    private MovementParameters StopParameters() =>
        new MovementParameters { HoldKeyToApply = MovementParams.HoldKeyToApply }.WithFlag(MovementParameterFlags.CancelMoveTo, false);

    /// <summary>A heading relative to the object's, in [0, 360), 0 within 0.0002 (the pattern every caller repeats).</summary>
    private float RelativeHeading(float heading)
    {
        double relative = heading - _host.Heading;
        if (Math.Abs(relative) < MotionInterpreter.Epsilon)
        {
            relative = 0.0;
        }

        if (relative < -MotionInterpreter.Epsilon)
        {
            relative += 360.0;
        }

        return (float)relative;
    }
}

// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Managers/MoveToManager.cs, Animation/MovementParameters.cs
// (get_command, towards_and_away, get_desired_heading) and Common/Position.cs (heading, CylinderDistance) (AGPL-3.0)
// [with changes: test-only oracle; the PhysicsObj, WeenieObject and PhysicsTimer it calls are an adapter (IAceMoveToBody);
// MotionCommand enum values written as numbers; Lifestone's MovementParameters, CellFrame and MotionInterpreter stand in
// for ACE's MovementParameters, Position and MotionInterp; the movement-type enum is WireMovementType].

using System.Numerics;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Data.Tests.Motion;

/// <summary>What ACE's MoveToManager asks of its PhysicsObj and WeenieObject (the oracle's adapter).</summary>
internal interface IAceMoveToBody
{
    uint ID { get; }

    CellFrame Position { get; }

    double CurrentTime { get; }

    bool HasContact { get; }

    bool IsAnimating { get; }

    Vector3 CachedVelocity { get; }

    float GetRadius();

    float GetHeight();

    float get_heading();

    void set_heading(float heading, bool sendEvent);

    void StopCompletely(bool sendEvent);

    bool IsInterpolating();

    MotionInterpreter? get_minterp();

    void set_target(uint contextId, uint objectId, float radius, double quantum);

    void clear_target();

    double get_target_quantum();

    void set_target_quantum(double quantum);

    void StickTo(uint objectId, float radius, float height);

    void unstick_from_object();

    void OnMoveComplete(WeenieError status);
}

/// <summary>
/// An independent oracle for <see cref="MoveToManager"/>: ACE's server-side MoveToManager, which follows retail
/// <c>MoveToManager</c> with a few server customisations. The tests drive it and Lifestone's manager over the same
/// kinematic bodies and compare where they end up and what they issued; where ACE differs from retail the tests assert
/// the difference.
/// </summary>
internal sealed class AceMoveToOracle
{
    private const float EPSILON = 0.0002f;
    private const uint WalkForward = 0x45000005;
    private const uint WalkBackwards = 0x45000006;
    private const uint RunForward = 0x44000007;
    private const uint TurnRight = 0x6500000D;
    private const uint TurnLeft = 0x6500000E;

    public WireMovementType MovementType;
    public CellFrame SoughtPosition;
    public CellFrame CurrentTargetPosition;
    public CellFrame StartingPosition;
    public MovementParameters MovementParams;
    public float PreviousHeading;
    public float PreviousDistance;
    public double PreviousDistanceTime;
    public float OriginalDistance;
    public double OriginalDistanceTime;
    public int FailProgressCount;
    public uint SoughtObjectID;
    public uint TopLevelObjectID;
    public float SoughtObjectRadius;
    public float SoughtObjectHeight;
    public uint CurrentCommand;
    public uint AuxCommand;
    public bool MovingAway;
    public bool Initialized;
    public List<(WireMovementType Type, float Heading)> PendingActions = [];
    public IAceMoveToBody PhysicsObj;
    public bool AlwaysTurn = false;

    public AceMoveToOracle(IAceMoveToBody obj)
    {
        PhysicsObj = obj;
        MovementParams = new MovementParameters();
        InitializeLocalVars();
    }

    public void InitializeLocalVars()
    {
        MovementType = 0;

        MovementParams = MovementParams with { DistanceToObject = 0, ContextId = 0 };

        PreviousDistanceTime = PhysicsObj.CurrentTime;
        OriginalDistanceTime = PhysicsObj.CurrentTime;

        PreviousHeading = 0.0f;

        FailProgressCount = 0;
        CurrentCommand = 0;
        AuxCommand = 0;
        MovingAway = false;
        Initialized = false;

        SoughtPosition = new CellFrame(0, Lifestone.Data.Animation.AcFrame.Identity);
        CurrentTargetPosition = new CellFrame(0, Lifestone.Data.Animation.AcFrame.Identity);

        SoughtObjectID = 0;
        TopLevelObjectID = 0;
        SoughtObjectRadius = 0;
        SoughtObjectHeight = 0;
    }

    public WeenieError PerformMovement(WireMovementType type, uint objectId, uint topLevelId, float radius, float height, CellFrame position, MovementParameters movementParams)
    {
        CancelMoveTo(WeenieError.ActionCancelled);
        PhysicsObj.unstick_from_object();
        switch (type)
        {
            case WireMovementType.MoveToObject:
                MoveToObject(objectId, topLevelId, radius, height, movementParams);
                break;
            case WireMovementType.MoveToPosition:
                MoveToPosition(position, movementParams);
                break;
            case WireMovementType.TurnToObject:
                TurnToObject(objectId, topLevelId, movementParams);
                break;
            case WireMovementType.TurnToHeading:
                TurnToHeading(movementParams);
                break;
        }

        return WeenieError.None;
    }

    public void MoveToObject(uint objectID, uint topLevelID, float radius, float height, MovementParameters movementParams)
    {
        PhysicsObj.StopCompletely(false);

        StartingPosition = PhysicsObj.Position;
        SoughtObjectID = objectID;
        SoughtObjectRadius = radius;
        SoughtObjectHeight = height;
        MovementType = WireMovementType.MoveToObject;
        TopLevelObjectID = topLevelID;

        MovementParams = movementParams;

        Initialized = false;
        if (PhysicsObj.ID != topLevelID)
        {
            PhysicsObj.set_target(0, TopLevelObjectID, 0.5f, 0.0f);
            return;
        }

        CleanUp();
        PhysicsObj.StopCompletely(false);
    }

    public void MoveToObject_Internal(CellFrame targetPosition, CellFrame interpolatedPosition)
    {
        SoughtPosition = interpolatedPosition;
        CurrentTargetPosition = targetPosition;

        var iHeading = Heading(PhysicsObj.Position, interpolatedPosition);
        var heading = iHeading - PhysicsObj.get_heading();
        var dist = GetCurrentDistance();

        if (Math.Abs(heading) < EPSILON)
            heading = 0.0f;
        if (heading < -EPSILON)
            heading += 360.0f;

        HoldKey holdKey = HoldKey.Invalid;
        uint motionID = 0;
        bool moveAway = false;
        GetCommand(MovementParams, dist, heading, ref motionID, ref holdKey, ref moveAway);

        if (motionID != 0)
        {
            AddTurnToHeadingNode(iHeading);
            AddMoveToPositionNode();
        }

        if (MovementParams.UseFinalHeading)
        {
            var dHeading = iHeading + MovementParams.DesiredHeading;
            if (dHeading >= 360.0f)
                dHeading -= 360.0f;
            AddTurnToHeadingNode(dHeading);
        }

        Initialized = true;
        BeginNextNode();
    }

    public void MoveToPosition(CellFrame position, MovementParameters movementParams)
    {
        PhysicsObj.StopCompletely(false);

        CurrentTargetPosition = position;
        SoughtObjectRadius = 0.0f;

        var distance = GetCurrentDistance();
        var headingDiff = Heading(PhysicsObj.Position, position) - PhysicsObj.get_heading();

        if (Math.Abs(headingDiff) < EPSILON)
            headingDiff = 0.0f;
        if (headingDiff < -EPSILON)
            headingDiff += 360.0f;

        HoldKey holdKey = HoldKey.Invalid;
        uint command = 0;
        bool moveAway = false;
        GetCommand(movementParams, distance, headingDiff, ref command, ref holdKey, ref moveAway);

        if (command != 0)
        {
            AddTurnToHeadingNode(Heading(PhysicsObj.Position, position));
            AddMoveToPositionNode();
        }

        if (MovementParams.UseFinalHeading)
            AddTurnToHeadingNode(movementParams.DesiredHeading);

        SoughtPosition = position;
        StartingPosition = PhysicsObj.Position;

        MovementType = WireMovementType.MoveToPosition;

        MovementParams = movementParams.WithFlag(MovementParameterFlags.Sticky, false);

        BeginNextNode();
    }

    public void TurnToObject(uint objectID, uint topLevelID, MovementParameters movementParams)
    {
        if (movementParams.StopCompletelyOnArrival)
            PhysicsObj.StopCompletely(false);

        MovementType = WireMovementType.TurnToObject;
        SoughtObjectID = objectID;

        CurrentTargetPosition = WithHeading(CurrentTargetPosition, movementParams.DesiredHeading);

        TopLevelObjectID = topLevelID;
        MovementParams = movementParams;

        if (PhysicsObj.ID != topLevelID)
        {
            Initialized = false;
            PhysicsObj.set_target(0, topLevelID, 0.5f, 0.0f);
            return;
        }

        CleanUp();
        PhysicsObj.StopCompletely(false);
    }

    public void TurnToObject_Internal(CellFrame targetPosition)
    {
        CurrentTargetPosition = targetPosition;

        var targetHeading = Heading(PhysicsObj.Position, CurrentTargetPosition);
        var soughtHeading = PositionFrames.Heading(SoughtPosition.Orientation);
        var heading = (targetHeading + soughtHeading) % 360.0f;
        SoughtPosition = WithHeading(SoughtPosition, heading);

        PendingActions.Add((WireMovementType.TurnToHeading, heading));
        Initialized = true;

        BeginNextNode();
    }

    public void TurnToHeading(MovementParameters movementParams)
    {
        if (movementParams.StopCompletelyOnArrival)
            PhysicsObj.StopCompletely(false);

        MovementParams = movementParams.WithFlag(MovementParameterFlags.Sticky, false);

        SoughtPosition = WithHeading(SoughtPosition, movementParams.DesiredHeading);
        MovementType = WireMovementType.TurnToHeading;

        PendingActions.Add((WireMovementType.TurnToHeading, movementParams.DesiredHeading));
    }

    public void AddMoveToPositionNode() => PendingActions.Add((WireMovementType.MoveToPosition, 0f));

    public void AddTurnToHeadingNode(float heading) => PendingActions.Add((WireMovementType.TurnToHeading, heading));

    public void BeginNextNode()
    {
        if (PendingActions.Count > 0)
        {
            var pendingAction = PendingActions[0];

            switch (pendingAction.Type)
            {
                case WireMovementType.MoveToPosition:
                    BeginMoveForward();
                    break;
                case WireMovementType.TurnToHeading:
                    BeginTurnToHeading();
                    break;
            }
        }
        else
        {
            if (MovementParams.Sticky)
            {
                var soughtObjectRadius = SoughtObjectRadius;
                var soughtObjectHeight = SoughtObjectHeight;
                var topLevelObjectID = TopLevelObjectID;

                // unsets sticky flag
                CleanUpAndCallWeenie(WeenieError.None);

                PhysicsObj.StickTo(topLevelObjectID, soughtObjectRadius, soughtObjectHeight);
            }
            else
                CleanUpAndCallWeenie(WeenieError.None);
        }
    }

    public void BeginMoveForward()
    {
        var dist = GetCurrentDistance();
        var heading = Heading(PhysicsObj.Position, CurrentTargetPosition) - PhysicsObj.get_heading();
        if (Math.Abs(heading) < EPSILON)
            heading = 0.0f;
        if (heading < -EPSILON)
            heading += 360.0f;

        uint motion = 0;
        bool moveAway = false;
        HoldKey holdKey = HoldKey.Invalid;
        GetCommand(MovementParams, dist, heading, ref motion, ref holdKey, ref moveAway);

        if (motion == 0)
        {
            RemovePendingActionsHead();
            BeginNextNode();
            return;
        }

        var movementParams = new MovementParameters { HoldKeyToApply = holdKey, Speed = MovementParams.Speed }
            .WithFlag(MovementParameterFlags.CancelMoveTo, false);

        var result = _DoMotion(motion, movementParams);
        if (result != WeenieError.None)
        {
            CancelMoveTo(result);
            return;
        }

        CurrentCommand = motion;
        MovingAway = moveAway;
        MovementParams = MovementParams with { HoldKeyToApply = holdKey };
        PreviousDistance = dist;
        PreviousDistanceTime = PhysicsObj.CurrentTime;
        OriginalDistance = dist;
        OriginalDistanceTime = PhysicsObj.CurrentTime;
    }

    public void HandleMoveToPosition()
    {
        var curPos = PhysicsObj.Position;

        var movementParams = new MovementParameters { Speed = MovementParams.Speed, HoldKeyToApply = MovementParams.HoldKeyToApply }
            .WithFlag(MovementParameterFlags.CancelMoveTo, false);

        if (!PhysicsObj.IsAnimating)
        {
            var heading = GetDesiredHeading(CurrentCommand, MovingAway) + Heading(curPos, CurrentTargetPosition);
            if (heading >= 360.0f) heading -= 360.0f;

            var diff = heading - PhysicsObj.get_heading();

            if (Math.Abs(diff) < EPSILON) diff = 0.0f;
            if (diff < -EPSILON)
                diff += 360.0f;

            if (diff > 20.0f && diff < 340.0f)
            {
                uint motionID = diff >= 180.0f ? TurnLeft : TurnRight;
                if (motionID != AuxCommand)
                {
                    _DoMotion(motionID, movementParams);
                    AuxCommand = motionID;
                }
            }
            else
            {
                // custom: sync for server ticrate
                if (AuxCommand != 0)
                    PhysicsObj.set_heading(heading, true);

                stop_aux_command(movementParams);
            }
        }
        else
            stop_aux_command(movementParams);

        var dist = GetCurrentDistance();

        if (!CheckProgressMade(dist))
        {
            if (!PhysicsObj.IsInterpolating() && !PhysicsObj.IsAnimating)
                FailProgressCount++;
        }
        else
        {
            // custom for low monster update rate
            var inRange = false;

            if (!MovementParams.UseSpheres)
            {
                if (dist < 1.0f && PreviousDistance < dist)
                    inRange = true;

                PreviousDistance = dist;
                PreviousDistanceTime = PhysicsObj.CurrentTime;
            }

            FailProgressCount = 0;
            if (MovingAway && dist >= MovementParams.MinDistance || !MovingAway && dist <= MovementParams.DistanceToObject || inRange)
            {
                PendingActions.RemoveAt(0);
                _StopMotion(CurrentCommand, movementParams);

                CurrentCommand = 0;
                stop_aux_command(movementParams);

                BeginNextNode();
            }
            else
            {
                if (Distance(StartingPosition, PhysicsObj.Position) > MovementParams.FailDistance)
                    CancelMoveTo(WeenieError.YouChargedTooFar);
            }
        }

        if (TopLevelObjectID != 0 && MovementType != 0)
        {
            var velocity = PhysicsObj.CachedVelocity;
            var velocityLength = velocity.Length();
            if (velocityLength > 0.1f)
            {
                var time = dist / velocityLength;
                if (Math.Abs(time - PhysicsObj.get_target_quantum()) > 1.0f)
                    PhysicsObj.set_target_quantum(time);
            }
        }
    }

    public void BeginTurnToHeading()
    {
        if (PhysicsObj.IsAnimating && !AlwaysTurn) return;

        var pendingAction = PendingActions[0];
        var headingDiff = heading_diff(pendingAction.Heading, PhysicsObj.get_heading(), TurnRight);
        uint motionID = 0;

        if (headingDiff <= 180.0f)
        {
            if (headingDiff > EPSILON)
                motionID = TurnRight;
            else
            {
                RemovePendingActionsHead();
                BeginNextNode();
                return;
            }
        }
        else
        {
            if (headingDiff + EPSILON <= 360.0f)
                motionID = TurnLeft;
            else
            {
                RemovePendingActionsHead();
                BeginNextNode();
                return;
            }
        }

        var movementParams = new MovementParameters { Speed = MovementParams.Speed, HoldKeyToApply = MovementParams.HoldKeyToApply }
            .WithFlag(MovementParameterFlags.CancelMoveTo, false);

        var result = _DoMotion(motionID, movementParams);

        if (result != WeenieError.None)
        {
            CancelMoveTo(result);
            return;
        }

        CurrentCommand = motionID;
        PreviousHeading = headingDiff;
    }

    public void HandleTurnToHeading()
    {
        if (CurrentCommand != TurnRight && CurrentCommand != TurnLeft)
        {
            BeginTurnToHeading();
            return;
        }

        var pendingAction = PendingActions[0];
        var heading = PhysicsObj.get_heading();

        if (heading_greater(heading, pendingAction.Heading, CurrentCommand))
        {
            FailProgressCount = 0;
            PhysicsObj.set_heading(pendingAction.Heading, true);

            RemovePendingActionsHead();

            var movementParams = new MovementParameters { HoldKeyToApply = MovementParams.HoldKeyToApply }
                .WithFlag(MovementParameterFlags.CancelMoveTo, false);

            _StopMotion(CurrentCommand, movementParams);

            CurrentCommand = 0;
            BeginNextNode();
            return;
        }

        var diff = heading_diff(heading, PreviousHeading, CurrentCommand);

        if (diff > EPSILON && diff < 180.0f)
        {
            FailProgressCount = 0;
            PreviousHeading = heading;
        }
        else
        {
            PreviousHeading = heading;

            if (!PhysicsObj.IsInterpolating() && !PhysicsObj.IsAnimating)
                FailProgressCount++;
        }
    }

    public void HandleUpdateTarget(in TargetInfo targetInfo)
    {
        if (TopLevelObjectID != targetInfo.ObjectId)
            return;

        if (Initialized)
        {
            if (targetInfo.Status == TargetStatus.Ok)
            {
                if (MovementType == WireMovementType.MoveToObject)
                {
                    SoughtPosition = targetInfo.InterpolatedPosition;
                    CurrentTargetPosition = targetInfo.TargetPosition;
                    PreviousDistance = float.MaxValue;
                    PreviousDistanceTime = PhysicsObj.CurrentTime;
                    OriginalDistance = float.MaxValue;
                    OriginalDistanceTime = PhysicsObj.CurrentTime;
                }
            }
            else
                CancelMoveTo(WeenieError.ObjectGone);
        }
        else if (TopLevelObjectID == PhysicsObj.ID)
        {
            SoughtPosition = PhysicsObj.Position;
            CurrentTargetPosition = PhysicsObj.Position;
            CleanUpAndCallWeenie(WeenieError.None);
        }
        else if (targetInfo.Status == TargetStatus.Ok)
        {
            if (MovementType == WireMovementType.MoveToObject)
                MoveToObject_Internal(targetInfo.TargetPosition, targetInfo.InterpolatedPosition);
            else if (MovementType == WireMovementType.TurnToObject)
                TurnToObject_Internal(targetInfo.TargetPosition);
        }
        else
            CancelMoveTo(WeenieError.NoObject);
    }

    public bool CheckProgressMade(float currDistance)
    {
        var deltaTime = PhysicsObj.CurrentTime - PreviousDistanceTime;

        if (deltaTime > 1.0f)
        {
            var diffDist = MovingAway ? currDistance - PreviousDistance : PreviousDistance - currDistance;

            if (diffDist / deltaTime < 0.25f)
                return false;

            PreviousDistance = currDistance;
            PreviousDistanceTime = PhysicsObj.CurrentTime;

            var dOrigDist = MovingAway ? currDistance - OriginalDistance : OriginalDistance - currDistance;

            if (dOrigDist / (PhysicsObj.CurrentTime - OriginalDistanceTime) < 0.25f)
                return false;
        }

        return true;
    }

    public void CancelMoveTo(WeenieError retval)
    {
        if (MovementType == 0)
            return;

        PendingActions.Clear();
        CleanUpAndCallWeenie(retval);
    }

    public void CleanUp()
    {
        var movementParams = new MovementParameters { HoldKeyToApply = MovementParams.HoldKeyToApply }
            .WithFlag(MovementParameterFlags.CancelMoveTo, false);

        if (CurrentCommand != 0)
            _StopMotion(CurrentCommand, movementParams);

        if (AuxCommand != 0)
            _StopMotion(AuxCommand, movementParams);

        if (TopLevelObjectID != 0 && MovementType != 0)
            PhysicsObj.clear_target();

        InitializeLocalVars();
    }

    public void CleanUpAndCallWeenie(WeenieError status)
    {
        CleanUp();
        PhysicsObj.StopCompletely(false);

        // server custom
        PhysicsObj.OnMoveComplete(status);
    }

    public float GetCurrentDistance()
    {
        if (!MovementParams.UseSpheres)
            return Distance(PhysicsObj.Position, CurrentTargetPosition);

        return (float)CylinderDistance(PhysicsObj.GetRadius(), PhysicsObj.GetHeight(), PhysicsObj.Position,
            SoughtObjectRadius, SoughtObjectHeight, CurrentTargetPosition);
    }

    public void HitGround()
    {
        if (MovementType != 0)
            BeginNextNode();
    }

    public void RemovePendingActionsHead()
    {
        if (PendingActions.Count > 0)
            PendingActions.RemoveAt(0);
    }

    public void UseTime()
    {
        if (!PhysicsObj.HasContact)
            return;

        if (PendingActions.Count == 0)
            return;

        var pendingAction = PendingActions[0];

        if (TopLevelObjectID != 0 || MovementType != 0 || Initialized)
        {
            switch (pendingAction.Type)
            {
                case WireMovementType.MoveToPosition:
                    HandleMoveToPosition();
                    break;
                case WireMovementType.TurnToHeading:
                    HandleTurnToHeading();
                    break;
            }
        }
    }

    public WeenieError _DoMotion(uint motion, MovementParameters movementParams)
    {
        var minterp = PhysicsObj.get_minterp();
        if (minterp == null)
            return WeenieError.NoMotionInterpreter;

        var speed = movementParams.Speed;
        minterp.AdjustMotion(ref motion, ref speed, movementParams.HoldKeyToApply);

        return minterp.DoInterpretedMotion(motion, movementParams with { Speed = speed });
    }

    public WeenieError _StopMotion(uint motion, MovementParameters movementParams)
    {
        var minterp = PhysicsObj.get_minterp();
        if (minterp == null)
            return WeenieError.NoMotionInterpreter;

        var speed = movementParams.Speed;
        minterp.AdjustMotion(ref motion, ref speed, movementParams.HoldKeyToApply);

        return minterp.StopInterpretedMotion(motion, movementParams with { Speed = speed });
    }

    public static float heading_diff(float h1, float h2, uint motion)
    {
        var result = h1 - h2;

        if (Math.Abs(result) < EPSILON)
            result = 0.0f;
        if (result < -EPSILON)
            result += 360.0f;
        if (result > EPSILON && motion != TurnRight)
            result = 360.0f - result;
        return result;
    }

    public static bool heading_greater(float h1, float h2, uint motion)
    {
        var diff = Math.Abs(h1 - h2);

        float v1, v2;

        if (diff <= 180.0f)
        {
            v1 = h2;
            v2 = h1;
        }
        else
        {
            v1 = h1;
            v2 = h2;
        }

        var result = (v2 > v1) ? true : false;

        if (motion != TurnRight)
            result = !result;

        return result;
    }

    public bool is_moving_to() => MovementType != 0;

    public void stop_aux_command(MovementParameters movementParams)
    {
        if (AuxCommand != 0)
        {
            _StopMotion(AuxCommand, movementParams);
            AuxCommand = 0;
        }
    }

    // ACE MovementParameters.get_command / towards_and_away / get_desired_heading.
    public static void GetCommand(MovementParameters p, float dist, float heading, ref uint motion, ref HoldKey holdKey, ref bool movingAway)
    {
        if (p.MoveTowards || !p.MoveAway)
        {
            if (p.MoveAway)
                TowardsAndAway(p, dist, heading, ref motion, ref movingAway);
            else
            {
                if (dist > p.DistanceToObject)
                {
                    motion = WalkForward;
                    movingAway = false;
                }
                else
                    motion = 0;
            }
        }
        else if (p.MoveAway)
        {
            if (dist < p.MinDistance)
            {
                motion = WalkForward;
                movingAway = true;
            }
            else
                motion = 0;
        }

        if (p.CanRun && (!p.CanWalk || dist - p.DistanceToObject > p.WalkRunThreshold))
            holdKey = HoldKey.Run;
        else
            holdKey = HoldKey.None;
    }

    public static float GetDesiredHeading(uint motion, bool movingAway)
    {
        switch (motion)
        {
            case RunForward:
            case WalkForward:
                return movingAway ? 180.0f : 0.0f;
            case WalkBackwards:
                return movingAway ? 0.0f : 180.0f;
            default:
                return 0.0f;
        }
    }

    public static void TowardsAndAway(MovementParameters p, float dist, float heading, ref uint command, ref bool movingAway)
    {
        if (dist > p.DistanceToObject)
        {
            command = WalkForward;
            movingAway = false;
        }
        else if (dist - p.MinDistance < EPSILON)
        {
            command = WalkBackwards;
            movingAway = true;
        }
        else
            command = 0;
    }

    // ACE Position.heading / Distance / CylinderDistance.
    public static float Heading(in CellFrame from, in CellFrame to)
    {
        var dir = PositionFrames.Offset(from, to);
        dir.Z = 0.0f;
        var length = dir.Length();
        if (length < EPSILON)
            return 0.0f;

        dir /= length;
        return (450.0f - (float)(Math.Atan2(dir.Y, dir.X) * 180.0 / Math.PI)) % 360.0f;
    }

    public static float Distance(in CellFrame a, in CellFrame b) => PositionFrames.Offset(a, b).Length();

    public static double CylinderDistance(float radius, float height, CellFrame pos, float otherRadius, float otherHeight, CellFrame otherPos)
    {
        var offset = PositionFrames.Offset(pos, otherPos);
        var reach = offset.Length() - (radius + otherRadius);

        var diffZ = pos.Origin.Z <= otherPos.Origin.Z ? otherPos.Origin.Z - (pos.Origin.Z + height) :
            pos.Origin.Z - (otherPos.Origin.Z + otherHeight);

        if (diffZ > 0 && reach > 0)
            return Math.Sqrt(diffZ * diffZ + reach * reach);
        else if (diffZ < 0 && reach < 0)
            return -Math.Sqrt(diffZ * diffZ + reach * reach);
        else
            return reach;
    }

    private static CellFrame WithHeading(CellFrame position, float heading) =>
        position with { Frame = position.Frame with { Orientation = PositionFrames.WithHeading(position.Orientation, heading) } };
}

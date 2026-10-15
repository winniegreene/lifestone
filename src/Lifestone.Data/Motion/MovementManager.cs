namespace Lifestone.Data.Motion;

/// <summary>
/// Retail <c>MovementManager</c>, re-expressed from the 11.4186 decompile (PORTAL/canim/CMovementManager.c; names and
/// addresses cited, nothing copied): an object's motion interpreter and its MoveTo manager, and the unpacking of the
/// server's MovementData (<see cref="UnpackMovement"/>). Addresses are 11.4186 / 11.6096.
/// </summary>
/// <remarks>
/// <para>The host creates it with the interpreter it hosts, after <see cref="MotionInterpreter.EnterDefaultState"/>
/// (retail <c>CPhysicsObj::MakeMovementManager</c> with <c>init_motion</c>). The MoveTo manager is made on demand
/// (<c>MakeMoveToManager</c>, 0x00524000 / 0x00524C00).</para>
/// <para>The host routes to it: <see cref="IMotionInterpHost.CancelMoveTo"/> → <see cref="CancelMoveTo"/> (0x36);
/// <see cref="IMotionInterpHost.SetOnWalkable"/> → <see cref="HitGround"/> / <see cref="LeaveGround"/>; each update →
/// <see cref="UseTime"/>; a context-0 target report → <see cref="HandleUpdateTarget"/>.</para>
/// <para>Not thread-safe.</para>
/// </remarks>
public sealed class MovementManager
{
    private readonly IMoveToHost _host;

    /// <summary>Retail <c>MovementManager::Create</c> (0x00524050 / 0x00524C50) with its interpreter.</summary>
    public MovementManager(MotionInterpreter motion, IMoveToHost host)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(host);
        Motion = motion;
        _host = host;
    }

    /// <summary>The motion interpreter (retail <c>motion_interpreter</c>).</summary>
    public MotionInterpreter Motion { get; }

    /// <summary>The MoveTo manager, or null before the first MoveTo.</summary>
    public MoveToManager? MoveTo { get; private set; }

    /// <summary>Retail <c>IsMovingTo</c> (0x00524260 / 0x00524E60).</summary>
    public bool IsMovingTo => MoveTo?.IsMovingTo ?? false;

    /// <summary>Retail <c>MakeMoveToManager</c> (0x00524000 / 0x00524C00).</summary>
    public MoveToManager MakeMoveToManager() => MoveTo ??= new MoveToManager(_host);

    /// <summary>Retail <c>CancelMoveTo</c> (0x005241B0 / 0x00524DB0).</summary>
    public void CancelMoveTo(WeenieError reason) => MoveTo?.CancelMoveTo(reason);

    /// <summary>Retail <c>UseTime</c> (0x005242F0 / 0x00524EF0): the MoveTo manager's update.</summary>
    public void UseTime() => MoveTo?.UseTime();

    /// <summary>Retail <c>HitGround</c> (0x00524300 / 0x00524F00): the interpreter's, then the MoveTo manager's.</summary>
    public void HitGround()
    {
        Motion.HitGround();
        MoveTo?.HitGround();
    }

    /// <summary>
    /// Retail <c>LeaveGround</c> (0x00524320 / 0x00524F20): the interpreter's (the MoveTo manager's call is to a folded
    /// empty function).
    /// </summary>
    public void LeaveGround() => Motion.LeaveGround();

    /// <summary>Retail <c>HandleExitWorld</c> (0x00524350 / 0x00524F50): the interpreter's.</summary>
    public void HandleExitWorld() => Motion.HandleExitWorld();

    /// <summary>Retail <c>HandleUpdateTarget</c> (0x00524790 / 0x00525390): the MoveTo manager's.</summary>
    public void HandleUpdateTarget(in TargetInfo info) => MoveTo?.HandleUpdateTarget(info);

    /// <summary>
    /// Retail <c>CPhysicsObj::MoveToObject</c> (0x00512860 / 0x00513360) through <c>MoveToObject_Internal</c> and
    /// <c>PerformMovement</c> type 6: the target's cylinder and top-level object come from <see cref="IMoveToHost.World"/>;
    /// an unknown target does nothing.
    /// </summary>
    public void MoveToObject(uint objectId, in MovementParameters parameters)
    {
        if (_host.World.FindObject(objectId) is not { } target)
        {
            return;
        }

        MakeMoveToManager().PerformMoveToObject(objectId, target.TopLevelId, target.Radius, target.Height, parameters);
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::TurnToObject</c> (0x00512940 / 0x00513440) through <c>PerformMovement</c> type 8; an unknown
    /// target does nothing.
    /// </summary>
    public void TurnToObject(uint objectId, in MovementParameters parameters)
    {
        if (_host.World.FindObject(objectId) is not { } target)
        {
            return;
        }

        MakeMoveToManager().PerformTurnToObject(objectId, target.TopLevelId, parameters);
    }

    /// <summary>Retail <c>CPhysicsObj::TurnToHeading</c> (0x00512980 / 0x00513480) through <c>PerformMovement</c> type 9.</summary>
    public void TurnToHeading(in MovementParameters parameters) => MakeMoveToManager().PerformTurnToHeading(parameters);

    /// <summary>
    /// Retail <c>unpack_movement</c> (0x00524440 / 0x00525040): applies a server MovementData.
    /// <list type="number">
    /// <item>Cancels a MoveTo and ends a stick; the stance's command is played (<c>DoMotion</c>) when it differs from the
    /// interpreted style.</item>
    /// <item>Type 0: the interpreted state (<see cref="WireMotion.TryToInterpreted"/> into <paramref name="scratch"/>,
    /// then <see cref="MotionInterpreter.MoveToInterpretedState"/>), then the stick to the sticky object, then the
    /// standing-long-jump flag.</item>
    /// <item>Types 6 and 7: the run rate becomes the interpreter's <see cref="MotionInterpreter.MyRunRate"/>; a known
    /// target is moved to (<see cref="MoveToObject"/>), an unknown one or type 7 to the origin given
    /// (<see cref="MoveToManager.MoveToPosition"/>).</item>
    /// <item>Type 8: a known target is turned to; an unknown one gives a TurnToHeading to the heading the message
    /// carries. Type 9: <see cref="MoveToManager.TurnToHeading"/>.</item>
    /// </list>
    /// Retail reads every low word through its table unchecked; here every word is checked first, and an unknown one
    /// applies nothing (<see cref="MovementUnpackResult.UnknownCommand"/> with <paramref name="failure"/>). The manager
    /// allocates nothing for type 0 once <paramref name="scratch"/> has grown; the motions it queues in a motion table
    /// allocate there (<see cref="Animation.MotionPlayer.MotionsQueued"/>).
    /// </summary>
    /// <param name="movement">The movement.</param>
    /// <param name="actions">The type-0 state's queued actions.</param>
    /// <param name="scratch">A state to unpack into (reused by the caller; never the interpreter's own).</param>
    /// <param name="failure">The unknown low word, for <see cref="MovementUnpackResult.UnknownCommand"/>.</param>
    public MovementUnpackResult UnpackMovement(
        in WireMovement movement, ReadOnlySpan<WireMotionItem> actions, InterpretedMotionState scratch, out WireMotionFailure failure)
    {
        ArgumentNullException.ThrowIfNull(scratch);
        failure = default;
        if (!WireMotion.TryCommand(movement.Stance, out var style))
        {
            failure = new WireMotionFailure(WireMotionField.Style, 0, movement.Stance);
            return MovementUnpackResult.UnknownCommand;
        }

        switch (movement.Type)
        {
            case WireMovementType.InterpretedMotion:
                if (!WireMotion.TryToInterpreted(movement.State, actions, scratch, out failure))
                {
                    return MovementUnpackResult.UnknownCommand;
                }

                break;
            case WireMovementType.MoveToObject or WireMovementType.MoveToPosition:
                if (movement.MoveTo is null)
                {
                    return MovementUnpackResult.MissingBody;
                }

                break;
            case WireMovementType.TurnToObject or WireMovementType.TurnToHeading:
                if (movement.TurnTo is null)
                {
                    return MovementUnpackResult.MissingBody;
                }

                break;
            default:
                return MovementUnpackResult.UnknownType;
        }

        _host.CancelMoveTo();
        _host.UnstickFromObject();
        if (Motion.InqStyle() != style)
        {
            Motion.DoMotion(style, new MovementParameters());
        }

        switch (movement.Type)
        {
            case WireMovementType.InterpretedMotion:
                Motion.MoveToInterpretedState(scratch);
                if (movement.StickToObject && movement.StickyObjectId != 0)
                {
                    StickToObject(movement.StickyObjectId);
                }

                Motion.StandingLongJump = movement.StandingLongJump;
                break;
            case WireMovementType.MoveToObject:
            {
                var moveTo = movement.MoveTo!.Value;
                MakeMoveToManager();
                Motion.MyRunRate = moveTo.RunRate;
                if (_host.World.FindObject(moveTo.TargetId) is not null)
                {
                    MoveToObject(moveTo.TargetId, moveTo.Parameters);
                }
                else
                {
                    MoveTo!.MoveToPosition(moveTo.Origin, moveTo.Parameters);
                }

                break;
            }

            case WireMovementType.MoveToPosition:
            {
                var moveTo = movement.MoveTo!.Value;
                MakeMoveToManager();
                Motion.MyRunRate = moveTo.RunRate;
                MoveTo!.MoveToPosition(moveTo.Origin, moveTo.Parameters);
                break;
            }

            case WireMovementType.TurnToObject:
            {
                var turnTo = movement.TurnTo!.Value;
                MakeMoveToManager();
                if (_host.World.FindObject(turnTo.TargetId) is not null)
                {
                    TurnToObject(turnTo.TargetId, turnTo.Parameters);
                }
                else
                {
                    MoveTo!.TurnToHeading(turnTo.Parameters with { DesiredHeading = turnTo.ObjectHeading });
                }

                break;
            }

            case WireMovementType.TurnToHeading:
                MakeMoveToManager().TurnToHeading(movement.TurnTo!.Value.Parameters);
                break;
        }

        return MovementUnpackResult.Applied;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::stick_to_object</c> (0x005127E0 / 0x005132E0): sticks to the top-level object of
    /// <paramref name="objectId"/>, with its cylinder; an unknown object does nothing.
    /// </summary>
    public void StickToObject(uint objectId)
    {
        if (_host.World.FindObject(objectId) is not { } target)
        {
            return;
        }

        var top = target.TopLevelId != target.Id && _host.World.FindObject(target.TopLevelId) is { } parent ? parent : target;
        _host.StickTo(top.Id, top.Radius, top.Height);
    }
}

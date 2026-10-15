using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.World;
using Xunit.Abstractions;
using static Lifestone.Data.Tests.Physics.PhysicsObjTestKit;
using static Lifestone.Data.Tests.Physics.PhysicsTestKit;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// A <see cref="PhysicsObj"/> hosting the avatars track's movement manager, MoveTo and target managers (PH5/PH6 after
/// C-avatars' AV14): a server MoveTo runs in the object's own update, the player's own command and a teleport cancel it,
/// and the objects that target it hear where it goes, that it teleported, and that it left. Synthetic: the flat world and
/// the walking table of <see cref="PhysicsObjTestKit"/> (no turn cycles, so every MoveTo here runs straight ahead). The
/// sticky follow a MoveTo with the Sticky flag ends in (<see cref="PhysicsObj.StickTo"/>, and the sticky id each update
/// passes to the position managers), target reports of another context, interpolation and an object without a motion
/// table pin the rest of the physics-side glue.
/// </summary>
public sealed class PhysicsObjMoveToTests(ITestOutputHelper output)
{
    private const uint WatcherId = 0x50000002;

    private static readonly ushort NonCombat = MotionCommandTable.ToWire(MotionCommands.NonCombat);

    [Fact]
    public void A_server_move_to_position_walks_the_object_there_in_its_own_update_and_stops()
    {
        var world = FlatWorld();
        var human = PhysicsObjMotionTests.Standing(world);
        var target = At(Block, 100f, 108f, 0f);

        Assert.Equal(MovementUnpackResult.Applied, MoveTo(human, target));
        Assert.NotNull(human.Movement);
        Assert.Same(human.Motion, human.Movement!.Motion);
        Assert.True(human.IsMovingTo);
        Assert.Equal(MotionCommands.WalkForward, human.Motion!.Interpreted.ForwardCommand);

        var seconds = RunWhileMovingTo(human, 6.0);
        var left = PositionFrames.Distance(target, human.Position);
        output.WriteLine($"arrived after {seconds:F2} s, {left:F3} m short, at {human.Position}");
        Assert.False(human.IsMovingTo);
        Assert.InRange(left, 0f, 0.6f + 0.15f);
        Assert.Equal(100f, human.Position.Frame.Origin.X, 3);
        Assert.Equal(MotionCommands.Ready, human.Motion.Interpreted.ForwardCommand);
        Assert.True(human.OnWalkable);

        // The stop's link plays out and the queues drain (the part array's completion check runs every update).
        Run(human, 0.5);
        Assert.Equal(0, human.Motion.PendingMotionCount);
        Assert.Equal(0, human.MotionPlayer!.PendingAnimationCount);
    }

    [Fact]
    public void The_players_own_command_cancels_the_server_move_to()
    {
        var world = FlatWorld();
        var human = PhysicsObjMotionTests.Standing(world);
        MoveTo(human, At(Block, 100f, 120f, 0f));
        Run(human, 0.5);
        Assert.True(human.IsMovingTo);

        // CPhysicsObj::DoMotion with the default parameters, whose CancelMoveTo bit reaches cancel_moveto (0x36).
        Assert.Equal(WeenieError.None, human.DoMotion(MotionCommands.WalkForward, new MovementParameters()));
        Assert.False(human.IsMovingTo);
        Assert.Equal(WeenieError.ActionCancelled, human.Movement!.MoveTo!.LastCancelReason);
        Assert.True(human.LastMoveWasAutonomous);
    }

    [Fact]
    public void A_teleport_cancels_the_move_to_and_tells_the_objects_that_target_the_object()
    {
        var world = FlatWorld();
        var human = PhysicsObjMotionTests.Standing(world);
        var watcher = Watcher(world, human);
        Assert.Equal(TargetStatus.Ok, watcher.Targets.Target!.Value.Status);
        MoveTo(human, At(Block, 100f, 120f, 0f));
        Run(human, 0.5);
        Assert.True(human.IsMovingTo);

        human.TeleportHook();

        Assert.False(human.IsMovingTo);
        Assert.Equal(WeenieError.ITeleported, human.Movement!.MoveTo!.LastCancelReason);
        Assert.Equal(TargetStatus.Teleported, watcher.Targets.Target!.Value.Status);
    }

    [Fact]
    public void The_objects_that_target_a_walking_object_hear_where_it_goes_from_its_update()
    {
        var world = FlatWorld();
        var human = PhysicsObjMotionTests.Standing(world);
        var watcher = Watcher(world, human);
        var first = watcher.Targets.Target!.Value.TargetPosition;

        human.DoMotion(MotionCommands.WalkForward, new MovementParameters());
        Run(human, 2.0);

        var report = watcher.Targets.Target!.Value;
        var reported = first.GetOffset(report.TargetPosition);
        output.WriteLine($"last report {reported} m on, velocity {report.Velocity}; the walker is {first.GetOffset(human.Position)} m on");
        Assert.Equal(TargetStatus.Ok, report.Status);
        Assert.True(reported.Y > 4f, "reports follow the walker every half second");
        Assert.True(report.Velocity.Y > 2f, "a report carries the measured velocity (get_velocity), not the physics velocity");
        Assert.Equal(Vector3.Zero, human.Velocity);
        Assert.Equal(human.CachedVelocity, ((ITargetable)human).Velocity);
    }

    [Fact]
    public void Leaving_for_good_completes_the_pending_motions_and_tells_the_objects_that_target_the_object()
    {
        var world = FlatWorld();
        var human = PhysicsObjMotionTests.Standing(world);
        var watcher = Watcher(world, human);
        human.DoMotion(MotionCommands.WalkForward, new MovementParameters());
        Assert.True(human.Motion!.PendingMotionCount > 0);

        human.Destroy();

        Assert.Equal(0, human.MotionPlayer!.PendingAnimationCount);
        Assert.Equal(0, human.Motion.PendingMotionCount);
        Assert.Null(watcher.Targets.Target); // ExitWorld clears the watcher's target
        Assert.Null(world.GetObject(human.Id));
        Assert.Null(human.Cell);
    }

    [Fact]
    public void A_server_move_to_object_walks_to_its_cylinder_once_the_object_reports()
    {
        var world = FlatWorld();
        var human = PhysicsObjMotionTests.Standing(world);
        var shape = PhysicsSetup.FromParts(
            [new PhysSphere(new Vector3(0, 0, 0.5f), 0.5f)], height: 1f, radius: 0.5f, sortingSphere: new PhysSphere(new Vector3(0, 0, 0.5f), 0.5f));
        var target = new PhysicsObj(world, shape, WatcherId, FakePhysicsWeenie.Creature());
        Assert.True(target.EnterWorld(At(Block, 100f, 107f, 0f)));
        Assert.Equal(0.48f, human.Radius);
        Assert.Equal(1.8f, human.Height);

        var parameters = new MovementParameters
        {
            Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.MoveTowards | MovementParameterFlags.UseSpheres,
            DistanceToObject = 0.6f,
        };
        var movement = new WireMovement(
            WireMovementType.MoveToObject, NonCombat, false, false, default, MoveTo: new WireMoveTo(WatcherId, target.Position, parameters, 1f));
        human.LastMoveWasAutonomous = false;
        Assert.Equal(MovementUnpackResult.Applied, human.UnpackMovement(movement, [], new InterpretedMotionState(), out _));

        // The target's voyeur list (found through the world's objects) reported at once, so the steps began.
        Assert.True(target.Targets.HasVoyeur(human.Id));
        Assert.True(human.Movement!.MoveTo!.Initialized);
        RunWhileMovingTo(human, 6.0);

        var gap = PositionFrames.Distance(target.Position, human.Position) - human.Radius - target.Radius;
        output.WriteLine($"stopped {gap:F3} m from the target's cylinder");
        Assert.False(human.IsMovingTo);
        Assert.InRange(gap, 0f, 0.6f + 0.15f);
        Assert.False(target.Targets.HasVoyeur(human.Id)); // the MoveTo's clean-up cleared its target
    }

    [Fact]
    public void A_sticky_move_to_object_ends_stuck_to_its_target_and_follows_it_until_the_stick_times_out()
    {
        var world = FlatWorld();
        var human = StuckToTarget(world, out var target);
        var stuckAt = world.CurrentTime;
        Assert.Equal(target.Id, human.GetStickyObjectId());
        Assert.True(human.Positions!.Sticky!.Initialized, "the target reported at once");
        Assert.True(target.Targets.HasVoyeur(human.Id));
        Assert.InRange(Gap(human, target), 0f, 0.6f + 0.15f);

        // The target walks north at 1.8 m/s: each update's offset becomes the sticky manager's step, so the object keeps
        // StickyGap from its cylinder without walking (its interpreter stopped completely when the MoveTo ended).
        var y = target.Position.Frame.Origin.Y;
        var start = human.Position;
        Run(human, 0.8, perFrame: _ => Assert.Equal(SetPositionError.OK, target.SetPositionSimple(At(Block, 100f, y += 0.03f, 0f), sliding: true)));
        output.WriteLine($"after 0.8 s stuck: {Gap(human, target):F3} m from the target's cylinder, moved {PositionFrames.Distance(start, human.Position):F2} m");
        Assert.Equal(target.Id, human.GetStickyObjectId());
        Assert.InRange(Gap(human, target), StickyManager.StickyGap - 0.05f, StickyManager.StickyGap + 0.1f);
        Assert.True(PositionFrames.Distance(start, human.Position) > 1.2f);
        Assert.Equal(MotionCommands.Ready, human.Motion!.Interpreted.ForwardCommand);

        // One second after StickTo the stick ends (StickyManager::UseTime from the object's update): the target is cleared
        // and the object stays where it is while the target walks on.
        Run(human, 0.4, perFrame: _ => target.SetPositionSimple(At(Block, 100f, y += 0.03f, 0f), sliding: true));
        Assert.True(world.CurrentTime > stuckAt + StickyManager.StickySeconds);
        Assert.Equal(0u, human.GetStickyObjectId());
        Assert.Null(human.Targets.Target);
        Assert.False(target.Targets.HasVoyeur(human.Id));
        var left = human.Position;
        Run(human, 0.5, perFrame: _ => target.SetPositionSimple(At(Block, 100f, y += 0.03f, 0f), sliding: true));
        Assert.InRange(PositionFrames.Distance(left, human.Position), 0f, 0.01f);
    }

    [Fact]
    public void A_stick_ends_with_a_new_server_movement_or_an_unstick_and_ignores_reports_of_another_context()
    {
        var world = FlatWorld();
        var human = StuckToTarget(world, out var target);
        var still = WireMovement.Interpreted(NonCombat, new WireInterpretedMotion(null, null, null, null, null, null, null));

        // A report of context 1 is not the sticky manager's (CPhysicsObj::HandleUpdateTarget passes context 0 only).
        human.HandleUpdateTarget(Report(target, contextId: 1, TargetStatus.ExitWorld));
        Assert.Equal(target.Id, human.GetStickyObjectId());
        human.HandleUpdateTarget(Report(target, contextId: 0, TargetStatus.ExitWorld));
        Assert.Equal(0u, human.GetStickyObjectId());

        // A type-0 movement naming a sticky object sticks to it (MovementManager::unpack_movement, stick_to_object,
        // CPhysicsObj::StickTo); the next one ends it (unpack_movement unsticks first), as does unstick_from_object.
        Assert.Equal(
            MovementUnpackResult.Applied,
            human.UnpackMovement(WireMovement.Interpreted(NonCombat, still.State, target.Id), [], new InterpretedMotionState(), out _));
        Assert.Equal(target.Id, human.GetStickyObjectId());
        Assert.True(human.Positions!.Sticky!.Initialized);
        Assert.Equal(MovementUnpackResult.Applied, human.UnpackMovement(still, [], new InterpretedMotionState(), out _));
        Assert.Equal(0u, human.GetStickyObjectId());
        Assert.False(target.Targets.HasVoyeur(human.Id));

        human.UnpackMovement(WireMovement.Interpreted(NonCombat, still.State, target.Id), [], new InterpretedMotionState(), out _);
        Assert.Equal(target.Id, human.GetStickyObjectId());
        human.UnstickFromObject();
        Assert.Equal(0u, human.GetStickyObjectId());
        Assert.Null(human.Targets.Target);
    }

    [Fact]
    public void While_stuck_the_interpolation_skips_its_progress_check()
    {
        var world = FlatWorld();
        var human = StuckToTarget(world, out var target);
        Assert.False(human.IsInterpolating);
        var stuckAt = human.Position;

        // A node 3 m east: the sticky manager's offset replaces the interpolation's, so the object makes no progress
        // towards it. With the sticky object id in the update's context the progress check is skipped (retail
        // InterpolationManager::adjust_offset); without it the node would fail at the second check and the object blip.
        var node = At(Block, stuckAt.Frame.Origin.X + 3f, stuckAt.Frame.Origin.Y, 0f);
        Assert.Equal(InterpolateToOutcome.Queued, human.InterpolateTo(node, keepHeading: false).Outcome);
        Assert.True(human.IsInterpolating);
        Run(human, 0.5);
        Assert.True(human.IsInterpolating);
        Assert.Equal(0, human.Positions!.Interpolation.NodeFailCounter);
        Assert.Equal(node, Assert.Single(human.Positions.Interpolation.Queue));

        // The object only closed on the target (to StickyGap), never towards the node.
        Assert.InRange(Gap(human, target), StickyManager.StickyGap - 0.02f, StickyManager.StickyGap + 0.02f);
        Assert.InRange(human.Position.Frame.Origin.X, stuckAt.Frame.Origin.X - 0.01f, stuckAt.Frame.Origin.X + 0.01f);

        human.StopInterpolating();
        Assert.False(human.IsInterpolating);
    }

    [Fact]
    public void A_target_report_of_another_context_does_not_reach_the_move_to()
    {
        var world = FlatWorld();
        var human = PhysicsObjMotionTests.Standing(world);
        var target = Target(world, 100f, 107f);
        Assert.Equal(MovementUnpackResult.Applied, MoveToObject(human, target, MovementParameterFlags.None));
        Run(human, 0.3);
        Assert.True(human.IsMovingTo);

        human.HandleUpdateTarget(Report(target, contextId: 1, TargetStatus.ExitWorld));
        Assert.True(human.IsMovingTo);
        Assert.Equal(WeenieError.None, human.Movement!.MoveTo!.LastCancelReason);

        // The same report in context 0 cancels the MoveTo (MoveToManager::HandleUpdateTarget, 0x37).
        human.HandleUpdateTarget(Report(target, contextId: 0, TargetStatus.ExitWorld));
        Assert.False(human.IsMovingTo);
        Assert.Equal(WeenieError.ObjectGone, human.Movement.MoveTo.LastCancelReason);
    }

    [Fact]
    public void Unpacking_a_movement_for_an_object_without_a_motion_table_makes_its_movement_manager_once()
    {
        var world = FlatWorld();
        var post = HumanObj(world, 0x50000003, FakePhysicsWeenie.Creature());
        Assert.True(post.EnterWorld(At(Block, 100f, 100f, 0f)));
        Run(post, 1.0);
        Assert.Null(post.Movement);
        Assert.Null(post.Motion);
        Assert.False(post.IsActive, "a still object without a motion table on walkable ground goes inactive");

        // CPhysicsObj::unpack_movement makes the movement manager (active), whose interpreter enters its default state
        // once (one Ready queued; without a motion table it never completes).
        var still = WireMovement.Interpreted(NonCombat, new WireInterpretedMotion(null, null, null, null, null, null, null));
        Assert.Equal(MovementUnpackResult.Applied, post.UnpackMovement(still, [], new InterpretedMotionState(), out _));
        Assert.NotNull(post.Movement);
        Assert.Same(post.Motion, post.Movement!.Motion);
        Assert.True(post.Motion!.Initted);
        Assert.Equal([new PendingMotion(0, MotionCommands.Ready, WeenieError.None)], post.Motion.PendingMotions);
        Assert.True(post.IsActive);

        // A second movement reuses it.
        var movement = post.Movement;
        Assert.Equal(MovementUnpackResult.Applied, post.UnpackMovement(still, [], new InterpretedMotionState(), out _));
        Assert.Same(movement, post.Movement);
        Assert.Equal(1, post.Motion.PendingMotionCount);
        Assert.Same(post.Motion, post.GetMinterp());
    }

    /// <summary>A type-7 MoveToPosition from the server (not autonomous), walking only, to within 0.6 m.</summary>
    private static MovementUnpackResult MoveTo(PhysicsObj obj, in CellFrame target)
    {
        var parameters = new MovementParameters
        {
            Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.MoveTowards,
            DistanceToObject = 0.6f,
        };
        var movement = new WireMovement(
            WireMovementType.MoveToPosition, NonCombat, false, false, default, MoveTo: new WireMoveTo(0, target, parameters, 1f));
        obj.LastMoveWasAutonomous = false;
        return obj.UnpackMovement(movement, [], new InterpretedMotionState(), out _);
    }

    /// <summary>
    /// A type-6 MoveToObject from the server (not autonomous) to <paramref name="target"/>'s cylinder, walking only, to
    /// within 0.6 m, with <paramref name="extra"/> flags.
    /// </summary>
    private static MovementUnpackResult MoveToObject(PhysicsObj obj, PhysicsObj target, MovementParameterFlags extra)
    {
        var parameters = new MovementParameters
        {
            Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.MoveTowards | MovementParameterFlags.UseSpheres | extra,
            DistanceToObject = 0.6f,
        };
        var movement = new WireMovement(
            WireMovementType.MoveToObject, NonCombat, false, false, default, MoveTo: new WireMoveTo(target.Id, target.Position, parameters, 1f));
        obj.LastMoveWasAutonomous = false;
        return obj.UnpackMovement(movement, [], new InterpretedMotionState(), out _);
    }

    /// <summary>
    /// The standing player after a sticky MoveToObject to a target at (100, 107): the MoveTo has ended in
    /// <see cref="PhysicsObj.StickTo"/>.
    /// </summary>
    private static PhysicsObj StuckToTarget(Lifestone.Data.Physics.World.PhysicsWorld world, out PhysicsObj target)
    {
        var human = PhysicsObjMotionTests.Standing(world);
        target = Target(world, 100f, 107f);
        Assert.Equal(MovementUnpackResult.Applied, MoveToObject(human, target, MovementParameterFlags.Sticky));
        RunWhileMovingTo(human, 6.0);
        Assert.False(human.IsMovingTo);
        Assert.Equal(target.Id, human.GetStickyObjectId());
        return human;
    }

    /// <summary>An object without a motion table at (<paramref name="x"/>, <paramref name="y"/>): one 0.5 m sphere, a 0.5 m cylinder 1 m high.</summary>
    private static PhysicsObj Target(Lifestone.Data.Physics.World.PhysicsWorld world, float x, float y)
    {
        var shape = PhysicsSetup.FromParts(
            [new PhysSphere(new Vector3(0, 0, 0.5f), 0.5f)], height: 1f, radius: 0.5f, sortingSphere: new PhysSphere(new Vector3(0, 0, 0.5f), 0.5f));
        var target = new PhysicsObj(world, shape, WatcherId, FakePhysicsWeenie.Creature());
        Assert.True(target.EnterWorld(At(Block, x, y, 0f)));
        return target;
    }

    /// <summary>A report from <paramref name="target"/> as its target manager would send it.</summary>
    private static TargetInfo Report(PhysicsObj target, uint contextId, TargetStatus status) =>
        new(contextId, target.Id, 0.5f, 0.0, target.Position, target.Position, Vector3.Zero, Vector3.Zero, status, target.World.CurrentTime);

    /// <summary>The gap between the cylinders of <paramref name="obj"/> and <paramref name="target"/>.</summary>
    private static float Gap(PhysicsObj obj, PhysicsObj target) =>
        PositionFrames.CylinderDistanceNoZ(obj.Radius, obj.Position, target.Radius, target.Position);

    /// <summary>A creature standing 5 m east of <paramref name="target"/> that targets it (radius 0.5 m, quantum 0).</summary>
    private static PhysicsObj Watcher(Lifestone.Data.Physics.World.PhysicsWorld world, PhysicsObj target)
    {
        var watcher = new PhysicsObj(world, CreatureSetup, WatcherId, FakePhysicsWeenie.Creature());
        Assert.True(watcher.EnterWorld(At(Block, 105f, 100f, 0f)));
        watcher.Targets.SetTarget(0, target.Id, 0.5f, 0.0);
        Assert.True(target.Targets.HasVoyeur(WatcherId));
        return watcher;
    }

    /// <summary>Steps <paramref name="obj"/> at 60 fps until its MoveTo ends (or <paramref name="limit"/> seconds); answers the time taken.</summary>
    private static double RunWhileMovingTo(PhysicsObj obj, double limit)
    {
        var world = obj.World;
        var start = world.CurrentTime;
        while (obj.IsMovingTo && world.CurrentTime - start < limit)
        {
            if (world.UseTime(world.CurrentTime + (1.0 / 60.0)))
            {
                obj.UpdateObject();
            }
        }

        return world.CurrentTime - start;
    }
}

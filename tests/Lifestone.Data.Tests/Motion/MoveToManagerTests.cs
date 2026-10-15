using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Data.Tests.Motion;

/// <summary>
/// The MoveTo manager (AV14), re-expressed from CMoveToManager.c: the command rules, turning, moving to a position and
/// to a moving object, failing, cancelling and sticking. The mover is <see cref="MoverHost"/>: a motion interpreter on
/// recording parts and a kinematic body.
/// </summary>
public sealed class MoveToManagerTests
{
    private const double Dt = 1.0 / 30.0;

    [Theory]
    [InlineData(10f, 100f, MotionCommands.TurnRight, 270f)]
    [InlineData(100f, 10f, MotionCommands.TurnRight, 90f)]
    [InlineData(100f, 10f, MotionCommands.TurnLeft, 270f)]
    [InlineData(5f, 5f, MotionCommands.TurnRight, 0f)]
    [InlineData(5.0001f, 5f, MotionCommands.TurnLeft, 0f)]
    public void Heading_diff_measures_the_turn_the_other_way_for_left_turns(float x, float y, uint motion, float expected)
    {
        Assert.Equal(expected, MoveToManager.HeadingDiff(x, y, motion), 3);
    }

    [Theory]
    [InlineData(100f, 90f, MotionCommands.TurnRight, true)]
    [InlineData(80f, 90f, MotionCommands.TurnRight, false)]
    [InlineData(5f, 350f, MotionCommands.TurnRight, true)] // past 350 clockwise, across north
    [InlineData(345f, 350f, MotionCommands.TurnLeft, true)]
    [InlineData(355f, 350f, MotionCommands.TurnLeft, false)]
    [InlineData(90f, 90f, MotionCommands.TurnRight, false)]
    [InlineData(90f, 90f, MotionCommands.TurnLeft, true)]
    public void Heading_greater_says_when_a_turn_has_passed_its_heading(float x, float y, uint motion, bool expected)
    {
        Assert.Equal(expected, MoveToManager.HeadingGreater(x, y, motion));
    }

    [Fact]
    public void Get_command_walks_towards_runs_beyond_the_threshold_and_backs_away()
    {
        var towards = new MovementParameters { Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.CanRun | MovementParameterFlags.MoveTowards };
        var away = false;
        towards.GetCommand(10f, 0f, out var command, out var key, ref away);
        Assert.Equal((MotionCommands.WalkForward, HoldKey.None, false), (command, key, away));
        towards.GetCommand(20f, 0f, out command, out key, ref away);
        Assert.Equal((MotionCommands.WalkForward, HoldKey.Run), (command, key)); // 20 − 0.6 > 15
        towards.GetCommand(0.5f, 0f, out command, out key, ref away);
        Assert.Equal(0u, command);

        var both = towards with { Flags = towards.Flags | MovementParameterFlags.MoveAway, MinDistance = 0.3f };
        both.GetCommand(0.2f, 0f, out command, out _, ref away);
        Assert.Equal((MotionCommands.WalkBackwards, true), (command, away));
        both.GetCommand(0.4f, 0f, out command, out _, ref away);
        Assert.Equal(0u, command);

        var awayOnly = new MovementParameters { Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.MoveAway, MinDistance = 5f };
        awayOnly.GetCommand(2f, 0f, out command, out key, ref away);
        Assert.Equal((MotionCommands.WalkForward, HoldKey.None, true), (command, key, away));

        var charge = new MovementParameters { Flags = MovementParameterFlags.CanCharge | MovementParameterFlags.CanWalk | MovementParameterFlags.MoveTowards };
        charge.GetCommand(2f, 0f, out _, out key, ref away);
        Assert.Equal(HoldKey.Run, key);

        Assert.Equal(180f, MovementParameters.GetDesiredHeading(MotionCommands.WalkForward, movingAway: true));
        Assert.Equal(180f, MovementParameters.GetDesiredHeading(MotionCommands.WalkBackwards, movingAway: false));
        Assert.Equal(0f, MovementParameters.GetDesiredHeading(MotionCommands.WalkBackwards, movingAway: true));
    }

    [Theory]
    [InlineData(10f, 100f, MotionCommands.TurnRight, 1f)]
    [InlineData(100f, 10f, MotionCommands.TurnRight, -1f)] // TurnLeft, as adjust_motion hands it to the motion table
    [InlineData(350f, 20f, MotionCommands.TurnRight, 1f)]
    public void Turn_to_heading_turns_the_short_way_and_snaps_to_the_heading(float start, float target, uint motion, float speed)
    {
        var world = new TestWorld();
        var mover = new MoverHost(1, world, MoverHost.At(50, 50, start));
        mover.Movement.TurnToHeading(new MovementParameters { DesiredHeading = target }.WithFlag(MovementParameterFlags.StopCompletely, false));
        Assert.True(mover.Movement.IsMovingTo);
        var first = mover.Calls.First(c => c.Kind == "do" && c.Motion != MotionCommands.NonCombat);
        Assert.Equal(motion, first.Motion);
        Assert.Equal(speed, first.Speed, 4);

        var ticks = Run(mover, () => !mover.Movement.IsMovingTo, 300);
        Assert.Equal(target, mover.Heading, 2);
        Assert.InRange(ticks * Dt, 0.25, 2.0); // 30° to 90° at 90°/s
        Assert.Equal(MotionCommands.Ready, mover.Motion.Interpreted.ForwardCommand);
        Assert.Equal(0u, mover.Motion.Interpreted.TurnCommand);
        Assert.Contains(mover.Calls, c => c.Kind == "stop" && c.Motion == MotionCommands.TurnRight);
    }

    [Theory]
    [InlineData(10f, MotionCommands.WalkForward, 1f)]
    [InlineData(30f, MotionCommands.RunForward, 1.5f)] // beyond the walk/run threshold: run at the MoveTo's run rate
    public void Move_to_position_turns_then_walks_or_runs_and_arrives_within_the_distance(float distance, uint forward, float runRate)
    {
        var world = new TestWorld();
        var mover = new MoverHost(1, world, MoverHost.At(50, 50, heading: 0f));
        var target = MoverHost.At(50 + distance, 50);
        var parameters = new MovementParameters { Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.CanRun | MovementParameterFlags.MoveTowards };
        mover.Motion.MyRunRate = runRate;
        mover.Movement.MakeMoveToManager().PerformMoveToPosition(target, parameters);
        Assert.Equal([new MoveToNode(MoveToNodeType.TurnToHeading, 90f), new MoveToNode(MoveToNodeType.MoveToPosition, 0f)], mover.Movement.MoveTo!.PendingActions);

        var sawForward = false;
        Run(mover, () =>
        {
            sawForward |= mover.Motion.Interpreted.ForwardCommand == forward;
            return !mover.Movement.IsMovingTo;
        }, 2000);
        Assert.True(sawForward);
        var gap = PositionFrames.Distance(target, mover.Position);
        Assert.True(gap <= 0.6f, $"stopped {gap} m short");
        Assert.True(gap > 0.3f, $"overshot to {gap} m");
        Assert.Equal(90f, mover.Heading, 0);
        Assert.Equal(MotionCommands.Ready, mover.Motion.Interpreted.ForwardCommand);
    }

    [Fact]
    public void A_final_heading_is_turned_to_after_arriving()
    {
        var world = new TestWorld();
        var mover = new MoverHost(1, world, MoverHost.At(50, 50, heading: 0f));
        var parameters = new MovementParameters
        {
            Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.MoveTowards | MovementParameterFlags.UseFinalHeading,
            DesiredHeading = 200f,
        };
        mover.Movement.MakeMoveToManager().PerformMoveToPosition(MoverHost.At(50, 56), parameters);

        // Three steps were queued: the turn towards the target, the move and the final turn. The stop completed in
        // PerformMovement's completion check, so the first turn began at once and, the target being straight ahead, was
        // done at once.
        Assert.Equal(
            [new MoveToNode(MoveToNodeType.MoveToPosition, 0f), new MoveToNode(MoveToNodeType.TurnToHeading, 200f)],
            mover.Movement.MoveTo!.PendingActions);
        Run(mover, () => !mover.Movement.IsMovingTo, 2000);
        Assert.Equal(200f, mover.Heading, 2);
    }

    [Fact]
    public void Going_past_the_fail_distance_cancels()
    {
        var world = new TestWorld();
        var mover = new MoverHost(1, world, MoverHost.At(50, 50, heading: 0f));
        var parameters = new MovementParameters { Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.MoveTowards, FailDistance = 4f };
        mover.Movement.MakeMoveToManager().PerformMoveToPosition(MoverHost.At(50, 70), parameters);
        Run(mover, () => !mover.Movement.IsMovingTo, 2000);
        Assert.Equal(WeenieError.YouChargedTooFar, mover.Movement.MoveTo!.LastCancelReason);
        Assert.InRange(PositionFrames.Distance(MoverHost.At(50, 50), mover.Position), 4f, 4.5f);
    }

    [Fact]
    public void Move_to_object_waits_for_the_first_report_then_follows_a_moving_target()
    {
        var world = new TestWorld();
        var mover = new MoverHost(1, world, MoverHost.At(50, 50, heading: 0f));
        var target = new MoverHost(2, world, MoverHost.At(60, 60)) { Drift = new Vector3(1.5f, 0, 0) };
        var parameters = new MovementParameters { Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.MoveTowards | MovementParameterFlags.UseSpheres };
        mover.Movement.MoveToObject(2, parameters);
        var moveTo = mover.Movement.MoveTo!;
        Assert.Equal(WireMovementType.MoveToObject, moveTo.MovementType);
        Assert.True(moveTo.Initialized); // the target joined its voyeurs and reported at once
        Assert.Equal(2u, mover.Targets.Target!.Value.ObjectId);
        Assert.True(target.Targets.HasVoyeur(1));

        Run(mover, () =>
        {
            target.Tick(Dt);
            return !mover.Movement.IsMovingTo;
        }, 3000);
        // Arrival is measured to the target's last report, which lags a moving target: by up to the report radius (0.5 m)
        // plus what it drifts between the reports' half-second checks.
        var gap = PositionFrames.CylinderDistance(mover.Radius, mover.Height, mover.Position, target.Radius, target.Height, target.Position);
        Assert.True(gap <= 0.6f + 0.5f + (1.5f * 0.5f), $"gap {gap}");
        Assert.True(mover.Position.Origin.X > 60f, "the mover chased the target east");
        Assert.True(target.Position.Origin.X > 65f); // it was chasing a moving target
        Assert.Null(mover.Targets.Target); // the MoveTo cleaned up and cleared its target
        Assert.False(target.Targets.HasVoyeur(1));
    }

    [Fact]
    public void A_target_that_never_reports_times_out_and_cancels()
    {
        var world = new TestWorld();
        var mover = new MoverHost(1, world, MoverHost.At(50, 50));
        mover.Movement.MakeMoveToManager().PerformMoveToObject(0x99, 0x99, 0.5f, 1f, new MovementParameters());
        Assert.True(mover.Movement.IsMovingTo);
        Assert.False(mover.Movement.MoveTo!.Initialized);
        Run(mover, () => !mover.Movement.IsMovingTo, (int)(11 / Dt));
        Assert.Equal(WeenieError.NoObject, mover.Movement.MoveTo.LastCancelReason);
        Assert.InRange(mover.Time, 10.0, 11.0);
    }

    [Fact]
    public void A_sticky_move_to_object_sticks_on_arrival()
    {
        var world = new TestWorld();
        var mover = new MoverHost(1, world, MoverHost.At(50, 50, heading: 0f));
        _ = new MoverHost(2, world, MoverHost.At(50, 54));
        var parameters = new MovementParameters
        {
            Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.MoveTowards | MovementParameterFlags.UseSpheres | MovementParameterFlags.Sticky,
        };
        mover.Movement.MoveToObject(2, parameters);
        Run(mover, () => !mover.Movement.IsMovingTo, 2000);
        Assert.Equal(2u, mover.Positions.GetStickyObjectId());
        Assert.True(mover.Positions.Sticky!.Initialized);
    }

    [Fact]
    public void A_new_movement_cancels_the_moveto_under_way()
    {
        var world = new TestWorld();
        var mover = new MoverHost(1, world, MoverHost.At(50, 50, heading: 0f));
        var parameters = new MovementParameters { Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.MoveTowards };
        mover.Movement.MakeMoveToManager().PerformMoveToPosition(MoverHost.At(50, 80), parameters);
        Run(mover, () => mover.Motion.Interpreted.ForwardCommand == MotionCommands.WalkForward, 200);
        var stops = mover.StopCompletelyCount;

        mover.Movement.TurnToHeading(new MovementParameters { DesiredHeading = 180f });
        Assert.Equal(WeenieError.ActionCancelled, mover.Movement.MoveTo!.LastCancelReason);
        Assert.True(mover.StopCompletelyCount > stops);
        Assert.Equal(WireMovementType.TurnToHeading, mover.Movement.MoveTo.MovementType);
    }

    [Fact]
    public void Progress_is_checked_once_a_second_since_the_last_check_and_the_start()
    {
        var world = new TestWorld();
        var mover = new MoverHost(1, world, MoverHost.At(50, 50));
        var moveTo = mover.Movement.MakeMoveToManager();
        mover.Time = 10;
        moveTo.PerformMoveToPosition(MoverHost.At(50, 50.1f), new MovementParameters()); // already there: no steps, the times start now
        Assert.True(moveTo.CheckProgressMade(5f)); // within the first second: always progress
    }

    private static int Run(MoverHost mover, Func<bool> done, int maxTicks)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            if (done())
            {
                return i;
            }

            mover.Tick(Dt);
        }

        Assert.Fail($"not done after {maxTicks} ticks");
        return maxTicks;
    }
}

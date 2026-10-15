using System.Numerics;
using Lifestone.Client.Movement;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Movement.MovementTestKit;
using Kind = Lifestone.Client.Tests.Movement.RecordingMovementSink.Kind;

namespace Lifestone.Client.Tests.Movement;

/// <summary>
/// The cadence matrix (m2-plan PH7): when <see cref="PlayerMovementController"/> sends AutonomousPosition and
/// MoveToState, as retail's <c>CommandInterpreter::ShouldSendPositionEvent</c>, <c>SendPositionEvent</c> and
/// <c>SendMovementEvent</c> decide, on synthetic terrain at 60 frames a second.
/// </summary>
public sealed class PositionEventCadenceTests(ITestOutputHelper output)
{
    private const double Frame = 1.0 / 60.0;

    [Fact]
    public void Standing_still_sends_the_first_position_and_then_nothing()
    {
        var (controller, sink, _) = FlatController();
        Run(controller, 5.0);

        var sent = Assert.Single(sink.Calls);
        Assert.Equal(Kind.AutonomousPosition, sent.Kind);
        Assert.True(sent.Contact);
        Assert.Equal(controller.Position, sent.Position);
        Assert.True(controller.ControlledByServer, "no key was pressed, so the server keeps control");
    }

    [Fact]
    public void Walking_straight_in_one_cell_sends_a_position_every_second()
    {
        // Cell (2, 2) spans 48-72 m on both axes; 5 s of walking north from y = 49 stays in it.
        var (controller, sink, _) = FlatController(x: 60f, y: 49f);
        Run(controller, 0.5);
        sink.Calls.Clear();
        var cell = controller.Position.CellId;

        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        var press = Assert.Single(sink.Calls);
        Assert.Equal(Kind.MoveToState, press.Kind);
        Assert.Equal(MotionCommands.WalkForward, press.Forward);
        Assert.True(press.Contact);
        Run(controller, 5.0);

        Assert.Equal(cell, controller.Position.CellId);
        var times = sink.Calls.Select(c => c.Time).ToArray();
        output.WriteLine(string.Join(", ", times.Select(t => $"{t - press.Time:F4}")));
        Assert.Equal(1, sink.Count(Kind.MoveToState));
        Assert.Equal(4, sink.Count(Kind.AutonomousPosition));
        for (var i = 1; i < times.Length; i++)
        {
            // Strictly more than 1.0 s after the last send, on the first frame after it: at most one frame late.
            Assert.InRange(times[i] - times[i - 1], 1.0, 1.0 + Frame + 1e-9);
        }

        Assert.All(sink.Of(Kind.AutonomousPosition), p => Assert.True(p.Contact));
        Assert.InRange(controller.Stats.MaxGroundMovingGapSeconds, 1.0 - Frame, 1.0 + 1e-9);
    }

    [Fact]
    public void A_cell_change_sends_the_position_at_once()
    {
        var (controller, sink, _) = FlatController(x: 60f, y: 70f);
        Run(controller, 0.5);
        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        sink.Calls.Clear();
        var cell = controller.Position.CellId;
        var crossed = false;
        Run(controller, 1.5, perFrame: _ =>
        {
            if (crossed)
            {
                return;
            }

            crossed = controller.Position.CellId != cell;
            if (crossed)
            {
                // The frame that ran physics into the new cell already sent the position (UseTime runs after it).
                var last = sink.Calls[^1];
                Assert.Equal(Kind.AutonomousPosition, last.Kind);
                Assert.Equal(controller.Position.CellId, last.Position.CellId);
            }
        });

        Assert.True(crossed, "the walk crossed into the next cell north");
        var first = sink.Of(Kind.AutonomousPosition).First();
        Assert.NotEqual(cell, first.Position.CellId);
        Assert.True(first.Time - controller.Stats.LastMoveToStateTime < 1.0, "sent before the interval ran out");
    }

    [Fact]
    public void A_contact_plane_change_in_the_same_cell_sends_the_position_at_once()
    {
        // A saddle in every cell: the two terrain triangles of a cell slope differently, so walking east across the
        // cell's diagonal changes the contact plane without changing the cell.
        var block = LandblockCollisionBuilderSaddle();
        var (controller, sink, _) = FlatController(x: 50f, y: 60f, blocks: block);
        controller.Player.SetHeading(90f);
        Run(controller, 0.5);
        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        sink.Calls.Clear();
        var cell = controller.Position.CellId;
        var plane = controller.Player.ContactPlane;
        var changes = 0;
        Run(controller, 4.0, perFrame: _ =>
        {
            if (controller.Position.CellId != cell || !controller.OnGround)
            {
                return;
            }

            if (!RetailFrames.PlaneEquals(plane, controller.Player.ContactPlane))
            {
                changes++;
                plane = controller.Player.ContactPlane;
                var last = sink.Calls[^1];
                Assert.Equal(Kind.AutonomousPosition, last.Kind);
                Assert.Equal(controller.Now, last.Time);
            }
        });

        output.WriteLine($"{changes} contact plane changes in cell 0x{cell:X8}; {sink.Count(Kind.AutonomousPosition)} positions sent");
        Assert.True(changes >= 1, "the walk crossed a terrain triangle edge");
    }

    [Fact]
    public void Nothing_is_sent_in_the_air_and_the_position_goes_out_on_landing()
    {
        var weenie = new TestPlayerWeenie { JumpVelocity = 6f };
        var (controller, sink, _) = FlatController(x: 60f, y: 50f, weenie: weenie);
        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        var now = Run(controller, 0.5);
        controller.CommenceJump(now);
        now = Run(controller, 1.0);
        controller.DoJump(now);
        Assert.Equal(1, sink.Count(Kind.Jump));
        Assert.Equal(1f, sink.Of(Kind.Jump).Single().Extent);
        sink.Calls.Clear();

        var airborne = 0;
        var landed = false;
        Run(controller, 2.0, perFrame: _ =>
        {
            if (landed)
            {
                return;
            }

            if (!controller.OnGround)
            {
                airborne++;
                Assert.DoesNotContain(sink.Calls, c => c.Kind == Kind.AutonomousPosition);
                return;
            }

            if (airborne > 0)
            {
                landed = true;
                var last = sink.Calls[^1];
                Assert.Equal(Kind.AutonomousPosition, last.Kind);
                Assert.Equal(controller.Now, last.Time);
                Assert.True(last.Contact);
            }
        });

        output.WriteLine($"airborne for {airborne} frames");
        Assert.True(airborne > 60, "a 6 m/s jump is in the air over a second");
        Assert.True(landed);
    }

    [Fact]
    public void Each_key_sends_a_movement_which_restarts_the_timer_but_not_the_last_position()
    {
        var (controller, sink, _) = FlatController(x: 60f, y: 49f);
        Run(controller, 0.5);
        sink.Calls.Clear();

        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        Run(controller, 1.5); // one position at about 1.0 s
        var position = sink.Of(Kind.AutonomousPosition).Single();
        controller.HandleCommand(PlayerMovementController.HoldRunCommand, start: true); // moving: a movement goes out
        var run = sink.Calls[^1];
        Assert.Equal(Kind.MoveToState, run.Kind);
        Assert.Equal(HoldKey.Run, run.HoldKey);
        Assert.Equal(MotionCommands.WalkForward, run.Forward);
        Assert.Equal(MotionCommands.RunForward, controller.Motion.Interpreted.ForwardCommand);

        Run(controller, 1.2);
        var next = sink.Of(Kind.AutonomousPosition).Skip(1).First();
        Assert.InRange(next.Time - run.Time, 1.0, 1.0 + Frame + 1e-9);
        Assert.True(next.Time - position.Time > 1.4, "the movement restarted the timer");

        controller.HandleCommand(MotionCommands.WalkForward, start: false);
        var stop = sink.Calls[^1];
        Assert.Equal(Kind.MoveToState, stop.Kind);
        Assert.Equal(MotionCommands.Ready, stop.Forward);
        controller.HandleCommand(PlayerMovementController.HoldRunCommand, start: false); // standing still: nothing goes out
        Assert.Equal(stop, sink.Calls[^1]);
        Assert.Equal(3, sink.Count(Kind.MoveToState));
    }

    [Fact]
    public void The_run_key_changes_the_motion_but_sends_nothing_and_leaves_the_timer_alone()
    {
        // Retail's keymap delivers the run key through ACCmdInterp::OnAction (action 0x32), which calls SetHoldRun
        // directly: no MoveToState, so the timer keeps running from the last message and the server hears of the run key
        // with the next movement.
        var (controller, sink, _) = FlatController(x: 60f, y: 49f);
        Run(controller, 0.5);
        sink.Calls.Clear();

        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        var walk = Assert.Single(sink.Calls);
        Run(controller, 0.5);
        controller.SetHoldRun(true);
        Assert.Equal(walk, sink.Calls[^1]);
        Assert.Equal(MotionCommands.RunForward, controller.Motion.Interpreted.ForwardCommand);

        Run(controller, 0.7);
        var position = sink.Of(Kind.AutonomousPosition).Single();
        Assert.InRange(position.Time - walk.Time, 1.0, 1.0 + Frame + 1e-9);

        controller.HandleCommand(MotionCommands.TurnRight, start: true);
        var turn = sink.Calls[^1];
        Assert.Equal(Kind.MoveToState, turn.Kind);
        Assert.Equal(HoldKey.Run, turn.HoldKey);
        Assert.Equal(2, sink.Count(Kind.MoveToState));
    }

    [Fact]
    public void A_movement_does_not_replace_the_last_position_so_a_change_still_sends_one()
    {
        // Standing still after a position, turn in place for half a second: the movements restart the timer, but the
        // frame is compared with the last position sent, so the next position goes out a second after the last movement.
        var (controller, sink, _) = FlatController(x: 60f, y: 60f);
        Run(controller, 0.5);
        sink.Calls.Clear();
        controller.HandleCommand(MotionCommands.TurnRight, start: true);
        Run(controller, 0.5);
        controller.HandleCommand(MotionCommands.TurnRight, start: false);
        var release = sink.Calls[^1];
        Run(controller, 2.0);

        var position = Assert.Single(sink.Of(Kind.AutonomousPosition));
        Assert.InRange(position.Time - release.Time, 1.0, 1.0 + Frame + 1e-9);
        Assert.Equal(controller.Position, position.Position);
    }

    /// <summary>A landblock whose every cell is a saddle (vertex heights 0 and 1 m in a checkerboard).</summary>
    private static Data.Physics.World.LandblockCollision LandblockCollisionBuilderSaddle() =>
        Data.Physics.World.LandblockCollisionBuilder.FromTerrain(TerrainOf(Block, (x, y) => (byte)((x + y) % 2)), Heights);
}

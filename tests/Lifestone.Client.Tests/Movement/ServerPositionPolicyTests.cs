using System.Numerics;
using Lifestone.Client.Movement;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Movement.MovementTestKit;
using Kind = Lifestone.Client.Tests.Movement.RecordingMovementSink.Kind;

namespace Lifestone.Client.Tests.Movement;

/// <summary>
/// The player's side of <c>SmartBox::HandleReceivedPosition</c> (m2-plan PH7, <see cref="ServerPositionPolicy"/>): a
/// correction blips the player keeping its heading and answers at once, a teleport arrival places, constrains and stops
/// it, an echo constrains it (retail 10/50 m outdoors, 5/20 m indoors), and the constraint's slow-down of a fast runner
/// at loopback latency is measured (owner decision D23).
/// </summary>
public sealed class ServerPositionPolicyTests(ITestOutputHelper output)
{
    private const uint PlayerGuid = 0x50000001;

    [Fact]
    public void A_correction_blips_the_player_keeping_its_heading_and_sends_its_position_at_once()
    {
        var (controller, sink, _) = FlatController(x: 60f, y: 60f);
        controller.Player.SetHeading(90f);
        Run(controller, 0.5);
        sink.Calls.Clear();

        var target = At(Block, 63f, 64f, 0f); // facing north
        var outcome = controller.Apply(Moved(target, PositionVerdict.ForceBlip), controller.Now);

        Assert.Equal(ServerPositionOutcome.Blipped, outcome);
        Assert.Equal(63f, controller.Position.Frame.Origin.X, 3);
        Assert.Equal(64f, controller.Position.Frame.Origin.Y, 3);
        Assert.Equal(90f, RetailFrames.GetHeading(controller.Position.Frame), 2);
        var sent = Assert.Single(sink.Calls);
        Assert.Equal(Kind.AutonomousPosition, sent.Kind);
        Assert.Equal(controller.Position, sent.Position);
        Assert.True(sent.Contact);
        Assert.Equal(1, controller.Stats.Blips);
        Assert.Equal(5f, controller.Stats.LastBlipDistance, 2);
    }

    [Fact]
    public void A_teleport_arrival_places_constrains_and_stops_the_player_and_ends_autorun()
    {
        var (controller, sink, _) = FlatController(x: 60f, y: 60f);
        var notices = new List<MovementNotice>();
        controller.Notice += notices.Add;
        controller.ToggleAutoRun();
        Run(controller, 0.5);
        Assert.True(controller.AutoRun);
        Assert.True(controller.Player.Velocity == Vector3.Zero, "running moves by root motion, not velocity");
        sink.Calls.Clear();

        var target = At(Block, 150f, 150f, 0f);
        var outcome = controller.Apply(Moved(target, PositionVerdict.SelfTeleport), controller.Now);

        Assert.Equal(ServerPositionOutcome.Teleported, outcome);
        Assert.Equal(150f, controller.Position.Frame.Origin.X, 3);
        Assert.Equal(150f, controller.Position.Frame.Origin.Y, 3);
        Assert.False(controller.AutoRun);
        Assert.Equal(MovementNoticeKind.AutoRunOff, notices[^1].Kind);
        Assert.Equal("AutoRun OFF", notices[^1].Text);
        var sent = Assert.Single(sink.Calls);
        Assert.Equal(Kind.MoveToState, sent.Kind);
        Assert.Equal(MotionCommands.Ready, sent.Forward);
        Assert.Equal(Vector3.Zero, controller.Player.Velocity);
        var constraint = controller.Player.Positions!.Constraint!;
        Assert.True(constraint.IsConstrained);
        Assert.InRange(constraint.Offset, 0f, 0.01f); // the placement rests the lower sphere 5 mm higher than asked
        Assert.Equal(10f, constraint.StartDistance);
        Assert.Equal(50f, constraint.MaxDistance);
        Assert.Equal(1, controller.Stats.Teleports);
    }

    [Fact]
    public void A_teleport_into_a_landblock_without_collision_waits_in_the_lost_cell()
    {
        var (controller, _, world) = FlatController(x: 60f, y: 60f);
        var far = new CellFrame(0x8080000A, new AcFrame(new Vector3(30f, 30f, 10f), Quaternion.Identity));

        Assert.Equal(ServerPositionOutcome.Teleported, controller.Apply(Moved(far, PositionVerdict.SelfTeleport), controller.Now));
        Assert.True(controller.Player.PlacementPending);
        Assert.Contains(controller.Player, world.LostObjects);
        Assert.Null(controller.Player.Cell);
    }

    [Fact]
    public void An_echo_constrains_the_player_from_where_it_is_and_lets_it_move_on()
    {
        var (controller, sink, _) = FlatController(x: 60f, y: 60f);
        Run(controller, 0.5);
        sink.Calls.Clear();
        var before = controller.Position;

        var echo = At(Block, 60f, 58f, 0f);
        Assert.Equal(ServerPositionOutcome.Constrained, controller.Apply(Moved(echo, PositionVerdict.SelfEcho), controller.Now));

        Assert.Equal(before, controller.Position);
        Assert.Empty(sink.Calls);
        var constraint = controller.Player.Positions!.Constraint!;
        Assert.True(constraint.IsConstrained);
        Assert.Equal(2f, constraint.Offset, 3);
        Assert.Equal((10f, 50f), (constraint.StartDistance, constraint.MaxDistance));
        Assert.Equal(1, controller.Stats.Echoes);
    }

    [Fact]
    public void Other_objects_positions_are_ignored()
    {
        var (controller, sink, _) = FlatController(x: 60f, y: 60f);
        Run(controller, 0.5);
        sink.Calls.Clear();
        var before = controller.Position;
        var moved = new ObjectMoved(0x80000123, At(Block, 10f, 10f, 0f), PositionVerdict.Remote, false, true, null, null);

        Assert.Equal(ServerPositionOutcome.Ignored, controller.Apply(moved, controller.Now));
        Assert.Equal(before, controller.Position);
        Assert.Empty(sink.Calls);
        Assert.Null(controller.Player.Positions?.Constraint);
        Assert.Equal(1, controller.Stats.IgnoredPositions);
    }

    [Theory]
    [InlineData(0xA9B40019u, 10f, 50f)]
    [InlineData(0xA9B40001u, 10f, 50f)]
    [InlineData(0xA9B40100u, 5f, 20f)]
    [InlineData(0x860201ADu, 5f, 20f)]
    public void The_constraint_distances_are_retails_for_outdoor_and_indoor_cells(uint cellId, float start, float max)
    {
        Assert.Equal(start, ServerPositionPolicy.StartConstraintDistance(cellId));
        Assert.Equal(max, ServerPositionPolicy.MaxConstraintDistance(cellId));
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(10f, 1f)]
    [InlineData(20f, 0.75f)]
    [InlineData(30f, 0.5f)]
    [InlineData(49.9f, 0.0025f)]
    [InlineData(50f, 0f)]
    [InlineData(80f, 0f)]
    public void The_constraint_factor_falls_from_the_start_to_the_maximum(float accumulated, float factor)
    {
        Assert.Equal(factor, ServerPositionPolicy.ConstraintFactor(accumulated, 10f, 50f), 4);
    }

    /// <summary>
    /// D23's measurement: a player running at the top run rate (4.5) outdoors, every AutonomousPosition echoed back one
    /// frame later (loopback latency), as ACE echoes accepted positions. Retail's constraint accumulates the path walked
    /// since the echo, so between the once-a-second echoes it passes its 10 m start and slows the runner.
    /// </summary>
    [Fact]
    public void Echoes_every_second_slow_a_top_speed_runner_measured_for_D23()
    {
        var weenie = new TestPlayerWeenie { RunRate = 4.5f };
        var blocks = new[] { Flat(Block), Flat(Block.Offset(0, 1)!.Value), Flat(Block.Offset(0, 2)!.Value) };
        var (controller, sink, _) = FlatController(x: 96f, y: 2f, weenie: weenie, blocks: blocks);
        controller.SetHoldRun(true);
        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        Run(controller, 1.0);
        controller.Stats.ResetPeaks();
        var echoed = sink.Calls.Count;
        var start = controller.Position;
        var startTime = controller.Now;
        Run(controller, 10.0, perFrame: now =>
        {
            // Echo each position sent last frame (one frame of loopback latency).
            for (; echoed < sink.Calls.Count; echoed++)
            {
                if (sink.Calls[echoed].Kind == Kind.AutonomousPosition)
                {
                    controller.Apply(Moved(sink.Calls[echoed].Position, PositionVerdict.SelfEcho), now);
                }
            }
        });

        var distance = start.GetOffset(controller.Position);
        var pace = new Vector2(distance.X, distance.Y).Length() / (controller.Now - startTime);
        var factor = pace / (4f * 4.5f);
        output.WriteLine(
            $"pace {pace:F3} m/s against 18 m/s unconstrained ({factor:P1}); min constraint factor {controller.Stats.MinConstraintFactor:F3}; {controller.Stats.Echoes} echoes");

        // Pinned measurement (docs/physics.md §12, D23): with the server echoing every accepted position, retail's rule
        // slows a top-speed runner by about 2.5% on average (17.55 of 18 m/s), down to about 82% just before each echo.
        Assert.InRange(controller.Stats.MinConstraintFactor, 0.78f, 0.86f);
        Assert.InRange(factor, 0.96, 0.99);
    }

    private static ObjectMoved Moved(in CellFrame position, PositionVerdict verdict) =>
        new(PlayerGuid, position, verdict, verdict == PositionVerdict.SelfTeleport, HasContact: true, Velocity: null, Placement: null);
}

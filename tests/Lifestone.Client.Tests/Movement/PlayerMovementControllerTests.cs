using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Tests.Movement;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Movement.MovementTestKit;
using Kind = Lifestone.Client.Tests.Movement.RecordingMovementSink.Kind;

namespace Lifestone.Client.Tests.Movement;

/// <summary>
/// <see cref="PlayerMovementController"/>'s command interpreter (m2-plan PH7): the command lists, the modifiers,
/// autorun, control between the server and the player, and the jump key, on synthetic terrain.
/// </summary>
public sealed class PlayerMovementControllerTests(ITestOutputHelper output)
{
    [Fact]
    public void The_controller_needs_its_worlds_player_with_a_motion_table()
    {
        var world = WorldOf(Flat(Block));
        var player = new Data.Physics.Objects.PhysicsObj(world, HumanSetup, 7, new TestPlayerWeenie(), SyntheticMotion());
        var sink = new RecordingMovementSink();
        Assert.Throws<ArgumentException>(() => new PlayerMovementController(player, sink, MovementOptions.Default));
        world.SetPlayer(player);
        Assert.Throws<ArgumentNullException>(() => new PlayerMovementController(player, null!, MovementOptions.Default));
        Assert.Throws<ArgumentNullException>(() => new PlayerMovementController(player, sink, null!));

        var bare = new Data.Physics.Objects.PhysicsObj(world, HumanSetup, 8);
        world.SetPlayer(bare);
        Assert.Throws<ArgumentException>(() => new PlayerMovementController(bare, sink, MovementOptions.Default));
    }

    [Fact]
    public void The_first_key_takes_control_from_the_server_and_a_release_before_it_is_only_forgotten()
    {
        var (controller, sink, _) = FlatController();
        Assert.True(controller.ControlledByServer);

        controller.HandleCommand(MotionCommands.WalkForward, start: false);
        Assert.True(controller.ControlledByServer);
        Assert.Empty(sink.Calls);

        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        Assert.False(controller.ControlledByServer);
        Assert.Equal(MotionCommands.WalkForward, controller.Motion.Raw.ForwardCommand);
        Assert.Equal(Kind.MoveToState, Assert.Single(sink.Calls).Kind);

        controller.LoseControlToServer();
        Assert.True(controller.ControlledByServer);
        controller.HandleCommand(MotionCommands.WalkForward, start: false);
        Assert.Single(sink.Calls); // forgotten, nothing sent
        Assert.Equal(MotionCommands.WalkForward, controller.Motion.Raw.ForwardCommand);

        // Control comes back on its own once the server's motions are done and a key or autorun is held.
        controller.HandleCommand(MotionCommands.TurnRight, start: true);
        Assert.False(controller.ControlledByServer);
    }

    [Fact]
    public void The_motion_description_says_which_keys_are_held_and_what_the_interpreter_does_with_them()
    {
        var (controller, _, _) = FlatController();
        var idle = controller.DescribeMotion();
        Assert.Contains("keys(substates=0 turns=0)", idle, StringComparison.Ordinal);
        Assert.Contains("in_cell=True", idle, StringComparison.Ordinal);

        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        controller.HandleCommand(MotionCommands.TurnRight, start: true);
        var held = controller.DescribeMotion();
        output.WriteLine(held);
        Assert.Contains("keys(substates=1 turns=1)", held, StringComparison.Ordinal);
        Assert.Contains("server=False", held, StringComparison.Ordinal);
        Assert.Contains($"raw(fwd=0x{MotionCommands.WalkForward:X8}", held, StringComparison.Ordinal);
    }

    [Fact]
    public void Releasing_the_newest_key_falls_back_to_the_one_pressed_before_and_an_older_release_changes_nothing()
    {
        var (controller, sink, _) = FlatController();
        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        controller.HandleCommand(MotionCommands.WalkBackwards, start: true);
        Assert.Equal(MotionCommands.WalkBackwards, controller.Motion.Raw.ForwardCommand);

        controller.HandleCommand(MotionCommands.WalkBackwards, start: false);
        Assert.Equal(MotionCommands.WalkForward, controller.Motion.Raw.ForwardCommand);

        controller.HandleCommand(MotionCommands.WalkBackwards, start: true);
        controller.HandleCommand(MotionCommands.WalkForward, start: false); // not the newest: forgotten
        Assert.Equal(MotionCommands.WalkBackwards, controller.Motion.Raw.ForwardCommand);

        controller.HandleCommand(MotionCommands.WalkBackwards, start: false);
        Assert.Equal(MotionCommands.Ready, controller.Motion.Raw.ForwardCommand);

        // Every key event sent a movement, the forgotten release too (retail's HandleKeyboardCommand).
        Assert.Equal(6, sink.Count(Kind.MoveToState));
        Assert.All(sink.Of(Kind.MoveToState), m => Assert.True(m.Contact));
    }

    [Fact]
    public void The_sidestep_modifier_turns_the_turn_keys_into_sidesteps()
    {
        var (controller, _, _) = FlatController();
        controller.SetHoldSidestep(true);
        Assert.True(controller.HoldSidestep);
        controller.HandleCommand(MotionCommands.TurnRight, start: true);
        Assert.Equal(0u, controller.Motion.Raw.TurnCommand);
        Assert.Equal(MotionCommands.SideStepRight, controller.Motion.Raw.SideStepCommand);

        controller.HandleCommand(MotionCommands.TurnLeft, start: true);
        Assert.Equal(MotionCommands.SideStepLeft, controller.Motion.Raw.SideStepCommand);

        // Letting go of the modifier turns again with the newest turn key.
        controller.SetHoldSidestep(false);
        Assert.Equal(0u, controller.Motion.Interpreted.SideStepCommand);
        Assert.Equal(MotionCommands.TurnLeft, controller.Motion.Raw.TurnCommand);
    }

    [Fact]
    public void Pressing_the_sidestep_modifier_during_a_turn_stops_it_as_retail_does()
    {
        // Retail CommandInterpreter::SetHoldSidestep stops the held turn and re-applies the keys: the turn list's head
        // becomes SideStepRight, and then ApplyCurrentMovement, finding the sidestep list empty, stops SideStepRight
        // again (docs/physics.md §12, a retail behaviour kept).
        var (controller, _, _) = FlatController();
        controller.HandleCommand(MotionCommands.TurnRight, start: true);
        Assert.Equal(MotionCommands.TurnRight, controller.Motion.Raw.TurnCommand);

        controller.SetHoldSidestep(true);
        Assert.Equal(0u, controller.Motion.Raw.TurnCommand);
        Assert.Equal(0u, controller.Motion.Raw.SideStepCommand);
        Assert.Equal(0u, controller.Motion.Interpreted.SideStepCommand);

        // The next press of a turn key sidesteps.
        controller.HandleCommand(MotionCommands.TurnRight, start: true);
        Assert.Equal(MotionCommands.SideStepRight, controller.Motion.Raw.SideStepCommand);
    }

    [Fact]
    public void Turning_right_turns_clockwise_and_sidestepping_moves_sideways()
    {
        var (controller, _, _) = FlatController(x: 60f, y: 60f);
        controller.HandleCommand(MotionCommands.TurnRight, start: true);
        Run(controller, 0.5);
        var heading = RetailFrames.GetHeading(controller.Position.Frame);
        output.WriteLine($"heading after 0.5 s of TurnRight: {heading:F2}");
        Assert.InRange(heading, 40f, 50f); // 90 degrees a second in the synthetic table
        controller.HandleCommand(MotionCommands.TurnRight, start: false);

        var start = controller.Position;
        controller.HandleCommand(MotionCommands.SideStepRight, start: true);
        Run(controller, 1.0);
        var moved = start.GetOffset(controller.Position);
        Assert.True(Vector3.Dot(moved, controller.Position.Frame.LocalToGlobalVec(Vector3.UnitX)) > 0.5f, $"moved {moved}");
    }

    [Fact]
    public void The_run_key_runs_and_the_run_by_default_option_reverses_it()
    {
        var (controller, _, _) = FlatController();
        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        Assert.Equal(MotionCommands.WalkForward, controller.Motion.Interpreted.ForwardCommand);
        controller.SetHoldRun(true);
        Assert.Equal(MotionCommands.RunForward, controller.Motion.Interpreted.ForwardCommand);

        var (walker, _, _) = FlatController(options: new MovementOptions { RunByDefault = true });
        walker.HandleCommand(MotionCommands.WalkForward, start: true);
        Assert.Equal(MotionCommands.RunForward, walker.Motion.Interpreted.ForwardCommand);
        walker.SetHoldRun(true);
        Assert.Equal(MotionCommands.WalkForward, walker.Motion.Interpreted.ForwardCommand);
    }

    [Fact]
    public void The_run_key_released_while_the_server_moves_the_player_stays_released_when_control_comes_back()
    {
        // ACCmdInterp::OnAction 0x32 calls SetHoldRun directly: a release is not dropped while the server is in control.
        var (controller, sink, _) = FlatController();
        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        controller.SetHoldRun(true);
        Assert.Equal(MotionCommands.RunForward, controller.Motion.Interpreted.ForwardCommand);

        controller.LoseControlToServer();
        var sent = sink.Calls.Count;
        controller.SetHoldRun(false);
        Assert.False(controller.HoldRun);
        Assert.Equal(HoldKey.None, controller.Motion.Raw.CurrentHoldKey);
        Assert.True(controller.ControlledByServer);
        Assert.Equal(sent, sink.Calls.Count);

        controller.TakeControlFromServer();
        Assert.False(controller.ControlledByServer);
        Assert.False(controller.HoldRun);
        Assert.Equal(HoldKey.None, controller.Motion.Raw.CurrentHoldKey);
        Assert.Equal(MotionCommands.WalkForward, controller.Motion.Interpreted.ForwardCommand);
    }

    [Fact]
    public void The_run_key_pressed_while_the_server_moves_the_player_leaves_it_in_control_and_sends_nothing()
    {
        var (controller, sink, _) = FlatController();
        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        Run(controller, 0.5);
        controller.LoseControlToServer();
        sink.Calls.Clear();

        controller.SetHoldRun(true);
        Assert.True(controller.ControlledByServer);
        Assert.Empty(sink.Calls);
        Assert.True(controller.HoldRun);
        Assert.Equal(HoldKey.Run, controller.Motion.Raw.CurrentHoldKey);

        // The command path (HandleKeyboardCommand with HoldRun) is still there for other callers: a press takes control
        // back and sends a MoveToState while the player moves.
        controller.HandleCommand(PlayerMovementController.HoldRunCommand, start: true);
        Assert.False(controller.ControlledByServer);
        Assert.True(controller.HoldRun);
        var sent = Assert.Single(sink.Calls);
        Assert.Equal(Kind.MoveToState, sent.Kind);
        Assert.Equal(HoldKey.Run, sent.HoldKey);

        // An inactive interpreter takes no run key.
        controller.Disable();
        controller.SetHoldRun(true);
        Assert.False(controller.HoldRun);
    }

    [Fact]
    public void Autorun_runs_until_a_new_forward_key_and_says_so()
    {
        var (controller, sink, _) = FlatController();
        var notices = new List<MovementNotice>();
        controller.Notice += notices.Add;

        controller.ToggleAutoRun();
        Assert.True(controller.AutoRun);
        Assert.False(controller.ControlledByServer);
        Assert.Equal(new MovementNotice(MovementNoticeKind.AutoRunOn, WeenieError.None, "AutoRun ON"), Assert.Single(notices));
        Assert.Equal(MotionCommands.RunForward, controller.Motion.Interpreted.ForwardCommand);
        Assert.Equal(Kind.MoveToState, sink.Calls[^1].Kind);
        Assert.Equal(HoldKey.Run, controller.Motion.Raw.ForwardHoldKey);

        // A released movement key does not stop autorun; a new forward key does.
        controller.HandleCommand(MotionCommands.TurnRight, start: true);
        controller.HandleCommand(MotionCommands.TurnRight, start: false);
        Assert.True(controller.AutoRun);
        controller.HandleCommand(MotionCommands.WalkBackwards, start: true);
        Assert.False(controller.AutoRun);
        Assert.Equal(MovementNoticeKind.AutoRunOff, notices[^1].Kind);
        Assert.Equal(MotionCommands.WalkBackwards, controller.Motion.Raw.ForwardCommand);
    }

    [Fact]
    public void A_standing_charge_starts_a_standing_long_jump_and_the_release_jumps_with_the_bars_level()
    {
        var weenie = new TestPlayerWeenie { JumpVelocity = 4f };
        var (controller, sink, _) = FlatController(weenie: weenie);
        var now = Run(controller, 0.5);
        sink.Calls.Clear();

        controller.CommenceJump(now);
        Assert.True(controller.Jump.Pending);
        var charge = Assert.Single(sink.Calls);
        Assert.Equal(Kind.MoveToState, charge.Kind);
        Assert.True(charge.StandingLongJump);

        now = Run(controller, 0.5);
        Assert.Equal(0.5f, controller.Jump.GetJumpPowerLevel(now, controller.Motion.Interpreted.CurrentStyle), 3);
        controller.DoJump(now);
        Assert.False(controller.Jump.Pending);
        Assert.False(controller.Motion.StandingLongJump);
        var jump = sink.Calls[^1];
        Assert.Equal(Kind.Jump, jump.Kind);
        Assert.Equal(0.5f, jump.Extent, 3);
        Assert.Equal(2f, jump.Velocity.Z, 2); // the weenie's 4 m/s at extent 0.5, local axes
        Assert.False(controller.OnGround);
        Assert.Equal(1, controller.Stats.JumpSent);
    }

    [Fact]
    public void A_release_at_once_jumps_with_the_minimum_extent()
    {
        var (controller, sink, _) = FlatController();
        var now = Run(controller, 0.5);
        controller.CommenceJump(now);
        controller.DoJump(now);
        var jump = sink.Of(Kind.Jump).Single();
        Assert.Equal(JumpCharge.MinJumpExtent, jump.Extent);
    }

    [Fact]
    public void Refused_jumps_are_reported_as_retail_shows_them()
    {
        var weenie = new TestPlayerWeenie { CanJumpNow = false };
        var (controller, sink, _) = FlatController(weenie: weenie);
        var notices = new List<MovementNotice>();
        controller.Notice += notices.Add;
        var now = Run(controller, 0.5);
        sink.Calls.Clear();

        controller.CommenceJump(now);
        Assert.False(controller.Jump.Pending);
        Assert.Equal(new MovementNotice(MovementNoticeKind.JumpRefused, WeenieError.CantJumpLoadedDown, null), Assert.Single(notices));
        Assert.Empty(sink.Calls);
        controller.DoJump(now + 0.5); // nothing pending: nothing happens
        Assert.Single(notices);

        // In the air the charge is allowed (charge_jump does not look), the jump is not.
        weenie.CanJumpNow = true;
        weenie.JumpVelocity = 5f;
        controller.CommenceJump(now);
        controller.DoJump(now + 0.5);
        Assert.False(controller.OnGround);
        controller.CommenceJump(now + 0.6);
        controller.DoJump(now + 0.7);
        Assert.Equal(new MovementNotice(MovementNoticeKind.JumpRefused, WeenieError.YouCantJumpWhileInTheAir, null), notices[^1]);
        Assert.Equal(1, sink.Count(Kind.Jump));
    }

    [Fact]
    public void Losing_control_to_the_server_ends_autorun_and_drops_a_pending_jump()
    {
        var (controller, _, _) = FlatController();
        controller.ToggleAutoRun();
        var now = Run(controller, 0.2);
        controller.CommenceJump(now);
        Assert.True(controller.Jump.Pending);

        controller.LoseControlToServer();
        Assert.True(controller.ControlledByServer);
        Assert.False(controller.AutoRun);
        Assert.False(controller.Jump.Pending);
    }

    [Fact]
    public void The_previous_step_position_and_times_are_kept_for_drawing_between_steps()
    {
        var (controller, _, _) = FlatController(x: 60f, y: 60f);
        Assert.Equal(controller.Position, controller.PreviousPosition);
        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        var now = controller.Now;
        var steps = 0;
        while (steps < 3)
        {
            now += 1.0 / 60.0;
            if (controller.Tick(now))
            {
                steps++;
            }
        }

        Assert.True(controller.LastStepTime > controller.PreviousStepTime);
        Assert.Equal(controller.Now, controller.LastStepTime);
        var moved = controller.PreviousPosition.GetOffset(controller.Position);
        Assert.InRange(moved.Y, 0.05f, 3.12f * 0.07f); // one step of 1/30 to 1/15 s at 3.12 m/s
    }

    [Fact]
    public void Entering_a_cell_a_housing_barrier_guards_logs_the_unenforced_barrier_once()
    {
        var log = new RecordingLogSink();
        var world = WorldOf(Flat(Block));
        var player = Player(world, At(Block, 60f, 70f, 0f));
        var restricted = CellIds.LandCellAt(Block, 60f, 80f)!.Value;

        // The dats give a land cell its barrier (PhysCell.RestrictionObject's setter is the Data layer's); the test sets
        // one by reflection.
        typeof(PhysCell).GetProperty(nameof(PhysCell.RestrictionObject))!.SetValue(world.GetCell(restricted), 0x7A9B4001u);
        var controller = new PlayerMovementController(player, new RecordingMovementSink(), MovementOptions.Default, log);
        Run(controller, 0.5);
        Assert.Empty(log.Entries);

        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        Run(controller, 3.0);
        Assert.Equal(restricted, controller.Position.CellId); // let in: no house weenie in M2
        var warning = Assert.Single(log.Entries);
        output.WriteLine(warning.Line);
        Assert.Equal(LogSeverity.Warning, warning.Severity);
        Assert.StartsWith("TODO(physics): housing barrier not enforced", warning.Message, StringComparison.Ordinal);
        Assert.Contains(new LogField("cell", $"0x{restricted:X8}"), warning.Fields);
        Assert.True(world.UnenforcedRestrictionEntries > 1, "every step in the cell is counted, but logged once");
    }

    [Fact]
    public void Disabling_stops_the_player_and_takes_no_keys_until_enabled()
    {
        var (controller, sink, _) = FlatController();
        controller.SetHoldRun(true);
        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        Run(controller, 0.5);
        sink.Calls.Clear();

        controller.Disable();
        Assert.False(controller.Enabled);
        Assert.False(controller.HoldRun);
        Assert.Equal(MotionCommands.Ready, controller.Motion.Raw.ForwardCommand);
        Assert.Equal(Kind.MoveToState, Assert.Single(sink.Calls).Kind);

        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        Run(controller, 2.0);
        Assert.Single(sink.Calls); // no keys taken, no positions sent
        Assert.Equal(MotionCommands.Ready, controller.Motion.Raw.ForwardCommand);

        controller.Enable();
        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        Assert.Equal(MotionCommands.WalkForward, controller.Motion.Raw.ForwardCommand);
    }

    [Fact]
    public void Losing_the_keyboard_releases_every_key_and_a_pending_jump()
    {
        var (controller, sink, _) = FlatController();
        controller.SetHoldSidestep(true);
        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        controller.HandleCommand(MotionCommands.TurnLeft, start: true);
        var now = Run(controller, 0.3);
        controller.CommenceJump(now);
        sink.Calls.Clear();

        controller.LoseKeyboardFocus();
        Assert.False(controller.HoldSidestep);
        Assert.False(controller.Jump.Pending);
        Assert.Equal(MotionCommands.Ready, controller.Motion.Raw.ForwardCommand);
        Assert.Equal(0u, controller.Motion.Raw.SideStepCommand);
        Assert.Equal(Kind.MoveToState, Assert.Single(sink.Calls).Kind);
        Assert.True(controller.Enabled);
    }

    [Fact]
    public void The_offline_walker_has_a_controller_with_a_sink_that_sends_nothing()
    {
        var world = WorldOf(Flat(Block));
        var player = Player(world, At(Block, 60f, 60f, 0f));
        var sink = new NullMovementSink();
        var controller = new PlayerMovementController(player, sink, MovementOptions.Default);
        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        Run(controller, 1.5);
        Assert.Equal(1, sink.MoveToStateCount);
        Assert.Equal(2, sink.AutonomousPositionCount);
        Assert.Equal(0, sink.JumpCount);
    }
}

/// <summary>The jump bar alone (<see cref="JumpCharge"/>, retail <c>ClientCombatSystem</c>).</summary>
public sealed class JumpChargeTests
{
    [Theory]
    [InlineData(0.25, 0.25)]
    [InlineData(1.0, 1.0)]
    [InlineData(3.0, 1.0)]
    [InlineData(-1.0, 0.0)]
    public void The_bar_fills_in_one_second(double held, double level)
    {
        var charge = new JumpCharge();
        charge.Start(10.0);
        Assert.Equal(level, charge.GetPowerBarLevel(10.0 + held, MotionCommands.NonCombat), 9);
    }

    [Theory]
    [InlineData(0.2, 0.25)]
    [InlineData(0.8, 1.0)]
    public void The_bar_fills_in_point_eight_seconds_in_the_dual_wield_stance(double held, double level)
    {
        var charge = new JumpCharge();
        charge.Start(0.0);
        Assert.Equal(level, charge.GetPowerBarLevel(held, JumpCharge.DualWieldCombatStyle), 9);
    }

    [Fact]
    public void Below_the_minimum_the_extent_is_the_minimum_and_nothing_pending_is_zero()
    {
        Assert.Equal(0.001f, JumpCharge.MinJumpExtent);
        Assert.Equal(JumpCharge.MinJumpExtent, JumpCharge.ToExtent(0.0));
        Assert.Equal(JumpCharge.MinJumpExtent, JumpCharge.ToExtent(0.0009));
        Assert.Equal(0.25f, JumpCharge.ToExtent(0.25));

        var charge = new JumpCharge();
        Assert.Equal(0f, charge.GetJumpPowerLevel(5.0, MotionCommands.NonCombat));
        Assert.Equal(0.0, charge.GetPowerBarLevel(5.0, MotionCommands.NonCombat));
        charge.Start(5.0);
        Assert.Equal(JumpCharge.MinJumpExtent, charge.GetJumpPowerLevel(5.0, MotionCommands.NonCombat));
        Assert.Equal(0.5f, charge.GetJumpPowerLevel(5.5, MotionCommands.NonCombat), 6);

        charge.Finish();
        Assert.False(charge.Pending);
        Assert.False(charge.Building);
        Assert.Equal(0.0, charge.BuildStartTime);
        Assert.Equal(0f, charge.GetJumpPowerLevel(6.0, MotionCommands.NonCombat));
    }
}

/// <summary>The controller's frame allocates nothing once warm.</summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class PlayerMovementControllerAllocationTests
{
    [Fact]
    public void A_frame_of_running_turning_and_sending_allocates_nothing_once_warm()
    {
        var world = WorldOf(Flat(Block), Flat(Block.Offset(0, 1)!.Value));
        var player = Player(world, At(Block, 60f, 60f, 0f));
        var controller = new PlayerMovementController(player, new NullMovementSink(), MovementOptions.Default);
        controller.SetHoldRun(true);
        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        var now = controller.Now;

        void Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                now += 1.0 / 60.0;
                controller.Tick(now);
            }
        }

        // Run in a tight circle (cells visited once allocate their shadow lists once).
        controller.HandleCommand(MotionCommands.TurnRight, start: true);
        Frames(600);

        var judged = 0;
        for (var attempt = 0; attempt < 10 && judged < 3; attempt++)
        {
            var window = AllocationWindow.Open();
            Frames(90); // includes AutonomousPositions (once a second)
            var (bytes, gcInside) = window.Close();
            if (gcInside)
            {
                continue;
            }

            judged++;
            Assert.Equal(0, bytes);
        }

        Assert.True(judged > 0, "every measured window had a GC inside");
        Assert.True(controller.Stats.AutonomousPositionSent > 3);
    }
}

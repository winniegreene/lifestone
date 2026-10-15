using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Data.Tests.Motion;

/// <summary>
/// <see cref="MotionInterpreter"/> (AV5, retail <c>CMotionInterp</c>) on a recording part array: the motion-table calls
/// each retail rule makes, worked out by hand from the 11.4186 functions (CMotionInterp.c) with the 11.6096 jump set.
/// </summary>
public sealed class MotionInterpreterTests
{
    private const uint Wave = 0x13000087;
    private const uint Fallen = 0x40000008;
    private const uint Crouch = 0x41000012;
    private const uint Sitting = 0x41000013;
    private const uint Sleeping = 0x41000014;
    private const uint Sanctuary = 0x10000057;
    private const uint AiTelegraphCast = 0x1000019B;

    private static readonly string[] IdleApplied = ["do NonCombat @1", "do Ready @1", "stop SideStepRight @1", "stop TurnRight @1"];

    [Fact]
    public void Entering_the_default_state_initialises_leaves_the_ground_and_plays_the_default_motion()
    {
        var (motion, host, parts) = MotionRig.Create(FakeWeenie.RemoteCreature(), enterDefaultState: false);
        Assert.False(motion.Initted);

        motion.EnterDefaultState();

        Assert.True(motion.Initted);
        Assert.Equal(["init", "unlink", .. IdleApplied], parts.Describe());
        Assert.Equal(Vector3.Zero, Assert.Single(host.LocalVelocities)); // LeaveGround: standing still keeps the velocity
        Assert.Equal(
            [MotionCommands.NonCombat, MotionCommands.Ready, MotionCommands.Ready, MotionCommands.Ready],
            motion.PendingMotions.Select(p => p.Motion).ToArray()); // the default Ready was completed by the unlink
        Assert.All(motion.PendingMotions, p => Assert.Equal(WeenieError.None, p.JumpErrorCode));
    }

    [Fact]
    public void Movement_is_not_applied_before_the_default_state()
    {
        var (motion, _, parts) = MotionRig.Create(FakeWeenie.RemoteCreature(), enterDefaultState: false);
        motion.ApplyCurrentMovement(cancelMoveTo: true, disallowJump: false);
        motion.SetHoldRun(true, cancelMoveTo: false);
        Assert.Empty(parts.Calls);
    }

    [Fact]
    public void The_player_walks_runs_and_walks_backwards_through_its_raw_state()
    {
        var (motion, host, parts, _) = MotionRig.Player(runRate: 1.5f);

        Assert.Equal(WeenieError.None, motion.DoMotion(MotionCommands.WalkForward, new MovementParameters()));
        Assert.Equal(["do WalkForward @1"], parts.Describe());
        Assert.Equal((MotionCommands.WalkForward, HoldKey.Invalid, 1f), (motion.Raw.ForwardCommand, motion.Raw.ForwardHoldKey, motion.Raw.ForwardSpeed));
        Assert.Equal((MotionCommands.WalkForward, 1f), (motion.Interpreted.ForwardCommand, motion.Interpreted.ForwardSpeed));
        AssertVector(new Vector3(0, 3.12f, 0), motion.GetStateVelocity());
        Assert.Equal(1, host.CancelMoveToCount);

        // Holding the run key re-applies the raw state: WalkForward becomes RunForward at the run rate.
        parts.Calls.Clear();
        motion.SetHoldRun(true, cancelMoveTo: false);
        Assert.Equal(["do NonCombat @1", "do RunForward @1.5", "stop SideStepRight @1.5", "stop TurnRight @1.5"], parts.Describe());
        Assert.Equal((MotionCommands.RunForward, 1.5f), (motion.Interpreted.ForwardCommand, motion.Interpreted.ForwardSpeed));
        Assert.Equal(MotionCommands.WalkForward, motion.Raw.ForwardCommand); // the raw state never holds RunForward
        Assert.Equal(1.5f, motion.MyRunRate);
        AssertVector(new Vector3(0, 6f, 0), motion.GetStateVelocity());

        // Backwards with the run key: WalkForward at -0.65, then times the run rate (it is not a run: the speed is negative).
        parts.Calls.Clear();
        motion.DoMotion(MotionCommands.WalkBackwards, new MovementParameters());
        Assert.Equal(["do WalkForward @-0.975"], parts.Describe());
        Assert.Equal(MotionCommands.WalkBackwards, motion.Raw.ForwardCommand);
        AssertVector(new Vector3(0, 3.12f * -0.975f, 0), motion.GetStateVelocity());

        parts.Calls.Clear();
        motion.SetHoldRun(false, cancelMoveTo: false);
        Assert.Equal(["do NonCombat @1", "do WalkForward @-0.65", "stop SideStepRight @-0.65", "stop TurnRight @-0.65"], parts.Describe());
        AssertVector(new Vector3(0, 3.12f * -0.65f, 0), motion.GetStateVelocity());
    }

    [Fact]
    public void Turning_left_is_turning_right_backwards_and_faster_when_running()
    {
        var (motion, _, parts, _) = MotionRig.Player(runRate: 2f);

        motion.DoMotion(MotionCommands.TurnLeft, new MovementParameters());
        Assert.Equal(["do TurnRight @-1"], parts.Describe());
        Assert.Equal((MotionCommands.TurnRight, -1f), (motion.Interpreted.TurnCommand, motion.Interpreted.TurnSpeed));
        Assert.Equal(MotionCommands.TurnLeft, motion.Raw.TurnCommand);

        parts.Calls.Clear();
        motion.SetHoldRun(true, cancelMoveTo: false);
        Assert.Equal(["do NonCombat @1", "do Ready @1", "stop SideStepRight @1", "do TurnRight @-1.5"], parts.Describe());

        parts.Calls.Clear();
        motion.StopMotion(MotionCommands.TurnLeft, new MovementParameters());
        Assert.Equal(["stop TurnRight @-1.5"], parts.Describe());
        Assert.Equal(0u, motion.Interpreted.TurnCommand);
        Assert.Equal(0u, motion.Raw.TurnCommand);
    }

    [Theory]
    [InlineData(1f, false, -1.248f)]
    [InlineData(1f, true, -1.248f)]
    [InlineData(2f, true, -2.496f)]
    [InlineData(3f, true, -3f)] // -3.744 clamped to the 3.0 sidestep cap
    public void Sidestepping_scales_by_1_248_and_running_caps_it_at_3(float runRate, bool run, float expected)
    {
        var (motion, _, parts, _) = MotionRig.Player(runRate);
        motion.SetHoldRun(run, cancelMoveTo: false);
        parts.Calls.Clear();

        motion.DoMotion(MotionCommands.SideStepLeft, new MovementParameters());

        var call = Assert.Single(parts.Calls);
        Assert.Equal(("do", MotionCommands.SideStepRight), (call.Kind, call.Motion));
        Assert.Equal(expected, call.Speed, 5);
        AssertVector(new Vector3(1.25f * expected, 0, 0), motion.GetStateVelocity());
    }

    [Fact]
    public void Jumping_leaves_the_ground_falls_and_landing_restores_the_motion()
    {
        var (motion, host, parts, weenie) = MotionRig.Player(runRate: 1f);
        weenie.JumpVelocity = 5f;
        motion.DoMotion(MotionCommands.WalkForward, new MovementParameters());
        parts.Calls.Clear();

        Assert.Equal(WeenieError.None, motion.Jump(0.5f, out var stamina));

        Assert.Equal(0, stamina);
        Assert.Equal([0.5f], weenie.JumpVelocityQueries);
        AssertVector(new Vector3(0, 3.12f, 5f), host.LocalVelocities[^1]); // get_leave_ground_velocity in local axes
        Assert.Equal(0f, motion.JumpExtent); // LeaveGround clears it once the velocity is set
        Assert.False(host.OnWalkable);
        // Off walkable ground the style is only recorded and Falling replaces the forward command.
        Assert.Equal(["unlink", "do Falling @1", "stop TurnRight @1"], parts.Describe());
        Assert.Equal(MotionCommands.WalkForward, motion.Interpreted.ForwardCommand);

        parts.Calls.Clear();
        host.SetOnWalkable(true);
        Assert.Equal(["unlink", "do NonCombat @1", "do WalkForward @1", "stop SideStepRight @1", "stop TurnRight @1"], parts.Describe());
    }

    [Fact]
    public void Without_contact_a_creature_falls_and_its_style_is_only_recorded()
    {
        var (motion, host, parts) = MotionRig.Create(FakeWeenie.RemoteCreature());
        host.HasContact = false;

        motion.MoveToInterpretedState(MotionRig.State(forward: MotionCommands.WalkForward, style: MotionCommands.Magic));

        Assert.Equal(["do Falling @1", "stop TurnRight @1"], parts.Describe());
        Assert.Equal(MotionCommands.WalkForward, motion.Interpreted.ForwardCommand);
        Assert.Equal(MotionCommands.Magic, motion.Interpreted.CurrentStyle);
    }

    [Fact]
    public void An_action_without_contact_fails_as_a_jump_in_the_air()
    {
        var (motion, host, parts) = MotionRig.Create(FakeWeenie.RemoteCreature());
        host.HasContact = false;
        Assert.Equal(WeenieError.YouCantJumpWhileInTheAir, motion.DoInterpretedMotion(Wave, new MovementParameters()));
        Assert.Empty(parts.Calls);
        Assert.Empty(motion.Interpreted.Actions);
    }

    [Fact]
    public void Without_gravity_or_as_a_non_creature_contact_does_not_matter()
    {
        var (motion, host, _) = MotionRig.Create(FakeWeenie.RemoteCreature());
        host.HasContact = false;
        host.OnWalkable = false;
        Assert.False(motion.ContactAllowsMove(MotionCommands.WalkForward));
        Assert.True(motion.ContactAllowsMove(MotionCommands.Falling));
        Assert.True(motion.ContactAllowsMove(MotionCommands.Dead));
        Assert.True(motion.ContactAllowsMove(MotionCommands.TurnLeft));

        host.HasGravity = false;
        Assert.True(motion.ContactAllowsMove(MotionCommands.WalkForward));

        host.HasGravity = true;
        host.Weenie = new FakeWeenie { IsCreature = false };
        Assert.True(motion.ContactAllowsMove(MotionCommands.WalkForward));

        host.Weenie = null;
        Assert.False(motion.ContactAllowsMove(MotionCommands.WalkForward));
    }

    [Fact]
    public void A_standing_long_jump_plays_Ready_and_skips_the_sidestep()
    {
        var (motion, _, parts) = MotionRig.Create(FakeWeenie.RemoteCreature());
        motion.StandingLongJump = true;

        motion.MoveToInterpretedState(MotionRig.State(forward: MotionCommands.WalkForward, sidestep: MotionCommands.SideStepRight));

        Assert.Equal(["do NonCombat @1", "do Ready @1", "stop TurnRight @1"], parts.Describe());
        Assert.Equal(MotionCommands.WalkForward, motion.Interpreted.ForwardCommand);
        Assert.Equal(MotionCommands.SideStepRight, motion.Interpreted.SideStepCommand);

        // A direct walk is recorded but not played.
        parts.Calls.Clear();
        Assert.Equal(WeenieError.None, motion.DoInterpretedMotion(MotionCommands.RunForward, new MovementParameters { Speed = 2f }));
        Assert.Empty(parts.Calls);
        Assert.Equal((MotionCommands.RunForward, 2f), (motion.Interpreted.ForwardCommand, motion.Interpreted.ForwardSpeed));
    }

    [Fact]
    public void Server_actions_apply_once_across_the_15_bit_wrap()
    {
        var (motion, _, parts) = MotionRig.Create(FakeWeenie.RemoteCreature());
        var first = MotionRig.State();
        first.AddAction(Wave, 1.5f, 0x3FFF, false); // half the range from 0: newer
        first.AddAction(Wave, 1.5f, 0x7FFE, false); // half the range from 0x3FFF: newer
        motion.MoveToInterpretedState(first);
        Assert.Equal(2, parts.Calls.Count(c => c.Motion == Wave));
        Assert.Equal((ushort)0x7FFE, motion.ServerActionStamp);

        var next = MotionRig.State();
        foreach (var stamp in new ushort[] { 0x7FFE, 0x7FFF, 0x0000, 0x0001 })
        {
            next.AddAction(Wave, 1f, stamp, false);
        }

        parts.Calls.Clear();
        motion.MoveToInterpretedState(next);
        Assert.Equal(3, parts.Calls.Count(c => c.Motion == Wave));
        Assert.Equal((ushort)0x0001, motion.ServerActionStamp);

        // The 16-bit rule would have dropped every action after the wrap.
        Assert.False(Sequence16.IsNewer(0x7FFF, 0x0000));

        parts.Calls.Clear();
        motion.MoveToInterpretedState(next);
        Assert.DoesNotContain(parts.Calls, c => c.Motion == Wave);
    }

    [Fact]
    public void A_server_action_is_played_and_queued_with_its_speed_and_autonomous_bit()
    {
        var (motion, _, parts) = MotionRig.Create(FakeWeenie.RemoteCreature());
        var state = MotionRig.State();
        state.AddAction(Wave, 1.25f, 7, true);

        motion.MoveToInterpretedState(state);

        Assert.Contains(parts.Calls, c => c.Kind == "do" && c.Motion == Wave && c.Speed == 1.25f);
        // retail stores the action with the parameter block's stamp, which move_to_interpreted_state never sets
        Assert.Equal(new MotionActionNode(Wave, 1.25f, 0, true), Assert.Single(motion.Interpreted.Actions));
    }

    [Fact]
    public void The_player_skips_its_own_autonomous_actions_and_does_not_store_their_stamps()
    {
        var (motion, _, parts, _) = MotionRig.Player();
        var state = MotionRig.State();
        state.AddAction(Wave, 1f, 1, true);
        state.AddAction(MotionCommands.MagicPowerUp01, 1f, 2, false);
        state.AddAction(Wave, 1f, 3, true);

        motion.MoveToInterpretedState(state);

        Assert.Equal([MotionCommands.MagicPowerUp01], parts.Calls.Where(c => (c.Motion & MotionCommands.ActionMask) != 0).Select(c => c.Motion).ToArray());
        Assert.Equal((ushort)2, motion.ServerActionStamp);
    }

    [Theory]
    [InlineData(false, true)] // a non-creature that is not the player: every action
    [InlineData(true, false)] // the player: only the server's own
    public void Every_other_object_applies_autonomous_actions_too(bool isThePlayer, bool expectAutonomousPlayed)
    {
        var (motion, _, parts) = MotionRig.Create(new FakeWeenie { IsCreature = false, IsThePlayer = isThePlayer });
        var state = MotionRig.State();
        state.AddAction(Wave, 1f, 1, true);

        motion.MoveToInterpretedState(state);

        Assert.Equal(expectAutonomousPlayed, parts.Calls.Any(c => c.Motion == Wave));
    }

    [Fact]
    public void An_object_without_a_weenie_applies_only_server_actions_and_its_raw_state_when_autonomous()
    {
        var (motion, _, parts) = MotionRig.Create(weenie: null, autonomous: true);
        var state = MotionRig.State(forward: MotionCommands.WalkForward);
        state.AddAction(Wave, 1f, 1, true);

        motion.MoveToInterpretedState(state);

        Assert.DoesNotContain(parts.Calls, c => c.Motion == Wave);
        Assert.DoesNotContain(parts.Calls, c => c.Motion == MotionCommands.WalkForward); // the raw state (Ready) is applied
        Assert.Equal(MotionCommands.Ready, motion.Interpreted.ForwardCommand);
    }

    [Fact]
    public void A_server_state_re_applies_with_jumps_disallowed_when_the_old_forward_forbade_one()
    {
        var (motion, _, _) = MotionRig.Create(FakeWeenie.RemoteCreature());
        motion.MoveToInterpretedState(MotionRig.State(forward: Sitting));
        motion.HandleExitWorld();

        motion.MoveToInterpretedState(MotionRig.State(forward: MotionCommands.WalkForward));

        // Sitting forbade a jump, so the motions played by the re-application are queued with error 0x48; the stops
        // queue Ready without one.
        var jump = WeenieError.YouCantJumpFromThisPosition;
        Assert.Equal(
            new[]
            {
                new PendingMotion(0, MotionCommands.NonCombat, jump),
                new PendingMotion(0, MotionCommands.WalkForward, jump),
                new PendingMotion(0, MotionCommands.Ready, WeenieError.None),
                new PendingMotion(0, MotionCommands.Ready, WeenieError.None),
            },
            motion.PendingMotions);
    }

    [Theory]
    [InlineData(0x1000012Bu, true)] // MagicPowerUp01Purple: 11.6096's range
    [InlineData(0x10000134u, true)] // MagicPowerUp10Purple
    [InlineData(0x10000128u, false)] // TripleThrustLow: in 11.4186's range, not 11.6096's
    [InlineData(0x10000131u, true)] // MagicPowerUp07Purple: in both
    [InlineData(0x10000135u, false)]
    [InlineData(Sanctuary, true)] // new in 11.6096
    [InlineData(AiTelegraphCast, true)] // new in 11.6096
    [InlineData(Fallen, true)]
    [InlineData(0x40000015u, false)] // Falling
    [InlineData(0x40000016u, true)]
    [InlineData(0x40000018u, true)]
    [InlineData(0x40000019u, false)]
    [InlineData(0x4000001Eu, true)]
    [InlineData(0x40000039u, true)]
    [InlineData(0x4000003Au, false)]
    [InlineData(Crouch, true)]
    [InlineData(Sleeping, true)]
    [InlineData(0x41000011u, false)]
    [InlineData(0x1000006Fu, true)]
    [InlineData(0x10000078u, true)]
    [InlineData(0x10000079u, false)]
    [InlineData(MotionCommands.Ready, false)]
    [InlineData(MotionCommands.WalkForward, false)]
    public void The_11_6096_jump_gating_set(uint substate, bool blocks)
    {
        Assert.Equal(blocks ? WeenieError.YouCantJumpFromThisPosition : WeenieError.None, MotionInterpreter.MotionAllowsJump(substate));
    }

    [Fact]
    public void A_jump_is_refused_in_the_air_when_constrained_without_stamina_or_from_a_blocking_motion()
    {
        var (motion, host, _, weenie) = MotionRig.Player();
        weenie.StaminaCost = 7;
        Assert.Equal(WeenieError.None, motion.JumpIsAllowed(0.5f, out var cost));
        Assert.Equal(7, cost);

        weenie.StaminaCost = null;
        Assert.Equal(WeenieError.GeneralMovementFailure, motion.JumpIsAllowed(0.5f, out _));
        weenie.StaminaCost = 0;

        weenie.CanJumpResult = false;
        Assert.Equal(WeenieError.CantJumpLoadedDown, motion.JumpIsAllowed(0.5f, out _));
        weenie.CanJumpResult = true;

        host.IsFullyConstrained = true;
        Assert.Equal(WeenieError.GeneralMovementFailure, motion.JumpIsAllowed(0.5f, out _));
        host.IsFullyConstrained = false;

        host.OnWalkable = false;
        Assert.Equal(WeenieError.YouCantJumpWhileInTheAir, motion.JumpIsAllowed(0.5f, out _));
        host.Weenie = new FakeWeenie { IsCreature = false };
        Assert.Equal(WeenieError.None, motion.JumpIsAllowed(0.5f, out _)); // a non-creature may jump anywhere
        host.Weenie = weenie;
        host.OnWalkable = true;

        motion.DoMotion(Sitting, new MovementParameters());
        Assert.Equal(WeenieError.YouCantJumpFromThisPosition, motion.JumpIsAllowed(0.5f, out _));
    }

    [Fact]
    public void A_pending_motion_that_forbids_a_jump_blocks_it_until_it_completes()
    {
        var (motion, _, _, _) = MotionRig.Player();
        motion.HandleExitWorld();
        var parameters = new MovementParameters().WithFlag(MovementParameterFlags.DisableJumpDuringLink, true);

        motion.DoInterpretedMotion(MotionCommands.Ready, parameters);

        Assert.Equal(WeenieError.YouCantJumpFromThisPosition, Assert.Single(motion.PendingMotions).JumpErrorCode);
        Assert.Equal(WeenieError.YouCantJumpFromThisPosition, motion.JumpIsAllowed(0.5f, out _)); // ACE needs two pending
        motion.MotionDone(success: true);
        Assert.Equal(WeenieError.None, motion.JumpIsAllowed(0.5f, out _));
    }

    [Fact]
    public void A_refused_jump_ends_the_standing_long_jump()
    {
        var (motion, host, _, _) = MotionRig.Player();
        Assert.Equal(WeenieError.None, motion.ChargeJump());
        Assert.True(motion.StandingLongJump);

        host.IsFullyConstrained = true;
        Assert.Equal(WeenieError.GeneralMovementFailure, motion.Jump(1f, out _));
        Assert.False(motion.StandingLongJump);
        Assert.True(host.OnWalkable);
    }

    [Fact]
    public void Charging_a_jump_starts_a_standing_long_jump_only_when_standing_still()
    {
        var (motion, _, _, weenie) = MotionRig.Player();
        motion.DoMotion(MotionCommands.WalkForward, new MovementParameters());
        Assert.Equal(WeenieError.None, motion.ChargeJump());
        Assert.False(motion.StandingLongJump);

        motion.StopCompletely();
        Assert.True(motion.IsStandingStill());
        Assert.Equal(WeenieError.None, motion.ChargeJump());
        Assert.True(motion.StandingLongJump);

        weenie.CanJumpResult = false;
        Assert.Equal(WeenieError.CantJumpLoadedDown, motion.ChargeJump());
    }

    [Theory]
    [InlineData(Fallen)]
    [InlineData(Crouch)]
    [InlineData(Sitting)]
    [InlineData(Sleeping)]
    public void A_jump_cannot_be_charged_from_the_ground_poses(uint forward)
    {
        var (motion, _, _, _) = MotionRig.Player();
        motion.Interpreted.ForwardCommand = forward;
        Assert.Equal(WeenieError.YouCantJumpFromThisPosition, motion.ChargeJump());
        Assert.False(motion.StandingLongJump);
    }

    [Fact]
    public void Combat_stances_refuse_ground_poses_and_chat_emotes()
    {
        var (motion, _, parts, _) = MotionRig.Player();
        Assert.Equal(WeenieError.None, motion.DoMotion(Wave, new MovementParameters())); // NonCombat: fine
        motion.DoMotion(MotionCommands.Magic, new MovementParameters());
        Assert.Equal(MotionCommands.Magic, motion.Interpreted.CurrentStyle);
        parts.Calls.Clear();

        Assert.Equal(WeenieError.CantCrouchInCombat, motion.DoMotion(Crouch, new MovementParameters()));
        Assert.Equal(WeenieError.CantSitInCombat, motion.DoMotion(Sitting, new MovementParameters()));
        Assert.Equal(WeenieError.CantLieDownInCombat, motion.DoMotion(Sleeping, new MovementParameters()));
        Assert.Equal(WeenieError.CantChatEmoteInCombat, motion.DoMotion(Wave, new MovementParameters()));
        Assert.Empty(parts.Calls);
    }

    [Fact]
    public void A_seventh_action_is_refused()
    {
        var (motion, _, parts, _) = MotionRig.Player();
        for (var i = 0; i < MotionInterpreter.MaxActions; i++)
        {
            Assert.Equal(WeenieError.None, motion.DoMotion(MotionCommands.MagicPowerUp01, new MovementParameters()));
        }

        Assert.Equal(6, motion.Interpreted.NumActions);
        Assert.Equal(6, motion.Raw.Actions.Count);
        Assert.Equal(WeenieError.TooManyActions, motion.DoMotion(MotionCommands.MagicPowerUp01, new MovementParameters()));
        Assert.Equal(6, parts.Calls.Count);
    }

    [Fact]
    public void A_refused_motion_changes_neither_state()
    {
        var (motion, _, parts, _) = MotionRig.Player();
        parts.Refuse.Add(MotionCommands.WalkForward);

        Assert.Equal(WeenieError.NoMtableData, motion.DoMotion(MotionCommands.WalkForward, new MovementParameters()));

        Assert.Equal(MotionCommands.Ready, motion.Interpreted.ForwardCommand);
        Assert.Equal(MotionCommands.Ready, motion.Raw.ForwardCommand);
        Assert.Equal(4, motion.PendingMotionCount);
    }

    [Fact]
    public void Without_a_motion_table_every_part_call_fails()
    {
        var host = new FakeMotionHost();
        var motion = new MotionInterpreter((MotionPlayer?)null, host);
        Assert.Null(motion.Parts);
        Assert.Equal(WeenieError.NoAnimationTable, motion.DoInterpretedMotion(MotionCommands.WalkForward, new MovementParameters()));
        Assert.Equal(WeenieError.NoAnimationTable, motion.StopInterpretedMotion(MotionCommands.WalkForward, new MovementParameters()));
        Assert.Equal(WeenieError.None, motion.StopCompletely());
    }

    [Fact]
    public void Stopping_completely_resets_both_states_and_queues_Ready_with_the_old_jump_error()
    {
        var (motion, host, parts, _) = MotionRig.Player();
        motion.DoMotion(Sitting, new MovementParameters());
        motion.DoMotion(MotionCommands.SideStepRight, new MovementParameters());
        motion.DoMotion(MotionCommands.TurnRight, new MovementParameters());
        parts.Calls.Clear();
        var cancels = host.CancelMoveToCount;

        Assert.Equal(WeenieError.None, motion.StopCompletely());

        Assert.Equal(["stopall"], parts.Describe());
        Assert.Equal(cancels + 1, host.CancelMoveToCount);
        Assert.Equal((MotionCommands.Ready, 1f, 0u, 0u), (motion.Raw.ForwardCommand, motion.Raw.ForwardSpeed, motion.Raw.SideStepCommand, motion.Raw.TurnCommand));
        Assert.Equal((MotionCommands.Ready, 1f, 0u, 0u), (motion.Interpreted.ForwardCommand, motion.Interpreted.ForwardSpeed, motion.Interpreted.SideStepCommand, motion.Interpreted.TurnCommand));
        Assert.Equal(new PendingMotion(0, MotionCommands.Ready, WeenieError.YouCantJumpFromThisPosition), motion.PendingMotions.Last());
    }

    [Fact]
    public void A_completed_action_leaves_both_states_and_ends_a_sticky_follow()
    {
        var (motion, host, _, _) = MotionRig.Player();
        motion.HandleExitWorld();
        motion.DoMotion(MotionCommands.MagicPowerUp01, new MovementParameters());
        motion.DoMotion(MotionCommands.WalkForward, new MovementParameters());

        motion.MotionDone(success: true);

        Assert.Equal(1, host.UnstickCount);
        Assert.Empty(motion.Interpreted.Actions);
        Assert.Empty(motion.Raw.Actions);
        Assert.Equal(MotionCommands.WalkForward, Assert.Single(motion.PendingMotions).Motion);

        motion.MotionDone(success: false);
        Assert.Equal(1, host.UnstickCount);
        Assert.False(motion.MotionsPending);
        motion.MotionDone(success: true); // nothing pending: ignored
    }

    [Fact]
    public void Dead_drops_the_link_animations_first()
    {
        var (motion, _, parts) = MotionRig.Create(FakeWeenie.RemoteCreature());
        motion.DoInterpretedMotion(MotionCommands.Dead, new MovementParameters());
        Assert.Equal(["unlink", "do Dead @1"], parts.Describe());
    }

    [Fact]
    public void Outside_a_cell_every_motion_drops_the_link_animations()
    {
        var (motion, host, parts) = MotionRig.Create(FakeWeenie.RemoteCreature());
        host.InCell = false;

        motion.MoveToInterpretedState(MotionRig.State(forward: MotionCommands.WalkForward));

        Assert.Equal(
            ["do NonCombat @1", "unlink", "do WalkForward @1", "unlink", "stop SideStepRight @1", "unlink", "stop TurnRight @1", "unlink"],
            parts.Describe());
        Assert.False(motion.MotionsPending);
    }

    [Fact]
    public void A_non_creature_is_not_adjusted()
    {
        var (motion, _, parts) = MotionRig.Create(new FakeWeenie { IsCreature = false });
        motion.SetHoldRun(true, cancelMoveTo: false);
        parts.Calls.Clear();

        motion.DoMotion(MotionCommands.TurnLeft, new MovementParameters());

        Assert.Equal(["do TurnLeft @1"], parts.Describe());
        Assert.Equal(MotionCommands.TurnLeft, motion.Interpreted.ForwardCommand); // InterpretedMotionState::ApplyMotion's quirk
    }

    [Fact]
    public void The_hold_key_switches_between_None_and_Run_only()
    {
        var (motion, _, parts, _) = MotionRig.Player(runRate: 2f);
        motion.DoMotion(MotionCommands.WalkForward, new MovementParameters());
        parts.Calls.Clear();

        motion.SetHoldKey(HoldKey.Invalid, cancelMoveTo: false);
        motion.SetHoldKey(HoldKey.None, cancelMoveTo: false);
        Assert.Empty(parts.Calls);

        motion.SetHoldKey(HoldKey.Run, cancelMoveTo: false);
        Assert.Equal(HoldKey.Run, motion.Raw.CurrentHoldKey);
        Assert.Contains("do RunForward @2", parts.Describe());

        parts.Calls.Clear();
        motion.DoMotion(MotionCommands.WalkForward, new MovementParameters { HoldKeyToApply = HoldKey.None });
        Assert.Equal(HoldKey.None, motion.Raw.CurrentHoldKey);
        Assert.Equal("do WalkForward @1", parts.Describe()[^1]);
    }

    [Fact]
    public void Speeds_follow_the_run_rate()
    {
        var (motion, host, _) = MotionRig.Create(weenie: null);
        Assert.Equal(4f, motion.GetMaxSpeed());

        var weenie = FakeWeenie.RemoteCreature();
        host.Weenie = weenie;
        motion.MyRunRate = 2f;
        Assert.Equal(8f, motion.GetMaxSpeed()); // the weenie cannot tell: my_run_rate

        weenie.RunRate = 1.5f;
        Assert.Equal(6f, motion.GetMaxSpeed());
        Assert.Equal(6f, motion.GetAdjustedMaxSpeed());

        motion.Interpreted.ForwardCommand = MotionCommands.RunForward;
        motion.Interpreted.ForwardSpeed = 2.4f;
        motion.CurrentSpeedFactor = 1.2f;
        Assert.Equal(8f, motion.GetAdjustedMaxSpeed(), 5);
    }

    [Fact]
    public void The_state_velocity_is_capped_at_four_times_the_run_rate()
    {
        var (motion, host, _) = MotionRig.Create(weenie: null);
        motion.Interpreted.ForwardCommand = MotionCommands.RunForward;
        motion.Interpreted.ForwardSpeed = 1f;
        motion.Interpreted.SideStepCommand = MotionCommands.SideStepRight;
        motion.Interpreted.SideStepSpeed = 2f;
        motion.MyRunRate = 3f; // ignored without a weenie

        var v = motion.GetStateVelocity();
        Assert.Equal(4f, v.Length(), 4);
        AssertVector(Vector3.Normalize(new Vector3(2.5f, 4f, 0)) * 4f, v);

        host.Weenie = FakeWeenie.RemoteCreature();
        AssertVector(new Vector3(2.5f, 4f, 0), motion.GetStateVelocity()); // cap 12
    }

    [Fact]
    public void The_jump_velocity_needs_an_extent_and_asks_the_weenie_for_at_most_full_power()
    {
        var weenie = FakeWeenie.Player();
        weenie.JumpVelocity = 9f;
        var host = new FakeMotionHost { Weenie = weenie, MovementIsAutonomous = true };
        var motion = new MotionInterpreter(new RecordingParts(), host); // no callback: the extent stays set
        motion.EnterDefaultState();
        Assert.Equal(0f, motion.GetJumpVelocityZ());

        Assert.Equal(WeenieError.None, motion.Jump(1.5f, out _));
        Assert.Equal(1.5f, motion.JumpExtent);
        Assert.Equal(9f, motion.GetJumpVelocityZ());
        Assert.Equal(1f, weenie.JumpVelocityQueries[^1]);

        weenie.JumpVelocity = null;
        Assert.Equal(0f, motion.GetJumpVelocityZ());
        host.Weenie = null;
        Assert.Equal(10f, motion.GetJumpVelocityZ());
    }

    [Fact]
    public void Leaving_the_ground_standing_still_keeps_the_current_velocity_in_local_axes()
    {
        var (motion, host, _) = MotionRig.Create(FakeWeenie.RemoteCreature());
        host.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2); // local +Y points west
        host.Velocity = new Vector3(-2f, 0, 0);

        AssertVector(new Vector3(0, 2f, 0), motion.GetLeaveGroundVelocity());

        motion.LeaveGround();
        AssertVector(new Vector3(-2f, 0, 0), host.Velocity);
    }

    [Fact]
    public void Ground_events_need_a_creature_with_gravity()
    {
        var (motion, host, parts) = MotionRig.Create(new FakeWeenie { IsCreature = false });
        motion.HitGround();
        motion.LeaveGround();
        Assert.Empty(parts.Calls);

        host.Weenie = FakeWeenie.RemoteCreature();
        host.HasGravity = false;
        motion.HitGround();
        motion.LeaveGround();
        Assert.Empty(parts.Calls);
        Assert.Empty(host.LocalVelocities);
    }

    [Fact]
    public void Perform_movement_runs_each_type_then_checks_for_completed_motions()
    {
        var (motion, _, parts, _) = MotionRig.Player();
        var p = new MovementParameters();

        Assert.Equal(WeenieError.None, motion.PerformMovement(MotionMovementType.RawCommand, MotionCommands.WalkForward, p));
        Assert.Equal(WeenieError.None, motion.PerformMovement(MotionMovementType.InterpretedCommand, MotionCommands.TurnRight, p));
        Assert.Equal(WeenieError.None, motion.PerformMovement(MotionMovementType.StopRawCommand, MotionCommands.WalkForward, p));
        Assert.Equal(WeenieError.None, motion.PerformMovement(MotionMovementType.StopInterpretedCommand, MotionCommands.TurnRight, p));
        Assert.Equal(WeenieError.None, motion.PerformMovement(MotionMovementType.StopCompletely, 0, p));
        Assert.Equal(WeenieError.GeneralMovementFailure, motion.PerformMovement((MotionMovementType)9, 0, p));

        Assert.Equal(
            ["do WalkForward @1", "check", "do TurnRight @1", "check", "stop WalkForward @1", "check", "stop TurnRight @1", "check", "stopall", "check"],
            parts.Describe());
    }

    [Fact]
    public void Re_applying_after_exhaustion_or_a_new_weenie_uses_the_current_movement()
    {
        var (motion, _, parts, _) = MotionRig.Player();
        motion.ReportExhaustion();
        Assert.Equal(IdleApplied, parts.Describe());

        parts.Calls.Clear();
        motion.OnWeenieChanged();
        Assert.Equal(IdleApplied, parts.Describe());
        Assert.Equal(MotionCommands.NonCombat, motion.InqStyle());
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(-0.5f)]
    public void A_negative_run_rate_leaves_the_state_velocity_uncapped_unless_retails_cap_is_asked_for(float runRate)
    {
        // A creature whose weenie cannot tell its run rate: the cap is 4 × MyRunRate, here negative (a server MoveTo's
        // run rate, or a RunForward speed, taken unchecked).
        var (motion, _, _) = MotionRig.Create(FakeWeenie.RemoteCreature());
        motion.MyRunRate = runRate;
        Assert.Equal(Vector3.Zero, motion.GetStateVelocity());
        Assert.False(float.IsNaN(motion.GetLeaveGroundVelocity().X));

        motion.MoveToInterpretedState(MotionRig.State(forward: MotionCommands.WalkForward));
        AssertVector(new Vector3(0, 3.12f, 0), motion.GetStateVelocity());

        // Retail's cap: the walk turns round to the cap's length, and standing still is NaN.
        motion.RetailNegativeRunRateCap = true;
        AssertVector(new Vector3(0, 4f * runRate, 0), motion.GetStateVelocity());
        motion.MoveToInterpretedState(MotionRig.State());
        Assert.True(float.IsNaN(motion.GetStateVelocity().Y));

        // A cap of 0 is retail's either way: no velocity.
        motion.RetailNegativeRunRateCap = false;
        motion.MyRunRate = 0f;
        motion.MoveToInterpretedState(MotionRig.State(forward: MotionCommands.WalkForward));
        Assert.Equal(Vector3.Zero, motion.GetStateVelocity());
    }

    [Fact]
    public void The_interpreters_own_state_cannot_be_applied_to_itself()
    {
        var (motion, _, _) = MotionRig.Create(FakeWeenie.RemoteCreature());
        Assert.Throws<ArgumentException>(() => motion.MoveToInterpretedState(motion.Interpreted));
    }

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Z, actual.Z, 4);
    }
}

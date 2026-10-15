using Lifestone.Data.Animation;
using Lifestone.Data.Motion;

namespace Lifestone.Data.Tests.Motion;

/// <summary>
/// The motion states' behaviour (AV4), re-expressed from retail <c>RawMotionState</c> and <c>InterpretedMotionState</c>
/// (CMInterpPrim.c), and retail's <c>MovementParameters</c> defaults.
/// </summary>
public sealed class MotionStateBehaviourTests
{
    private const uint Wave = 0x13000087;

    [Fact]
    public void Movement_parameters_default_to_retails_constructor()
    {
        var p = new MovementParameters();

        Assert.Equal(0x1EE0Fu, (uint)p.Flags);
        Assert.Equal(MovementParameters.DefaultFlags, p.Flags);
        Assert.False(p.Flags.HasFlag(MovementParameterFlags.CanCharge)); // ACE's constructor sets it
        Assert.Equal(0.6f, p.DistanceToObject);
        Assert.Equal(0f, p.MinDistance);
        Assert.Equal(float.MaxValue, p.FailDistance);
        Assert.Equal(0f, p.DesiredHeading);
        Assert.Equal(1f, p.Speed);
        Assert.Equal(15f, p.WalkRunThreshold); // ACE: 1
        Assert.Equal(0u, p.ContextId);
        Assert.Equal(HoldKey.Invalid, p.HoldKeyToApply);
        Assert.Equal((ushort)0, p.ActionStamp);
        Assert.True(p.SetHoldKey && p.ModifyRawState && p.ModifyInterpretedState && p.CancelMoveTo);
        Assert.False(p.Autonomous || p.DisableJumpDuringLink);
    }

    [Fact]
    public void With_flag_sets_and_clears_one_bit()
    {
        var p = new MovementParameters().WithFlag(MovementParameterFlags.Autonomous, true);
        Assert.True(p.Autonomous);
        Assert.Equal(MovementParameters.DefaultFlags | MovementParameterFlags.Autonomous, p.Flags);
        Assert.False(p.WithFlag(MovementParameterFlags.SetHoldKey, false).SetHoldKey);
    }

    [Fact]
    public void Interpreted_apply_motion_records_turn_sidestep_forward_style_and_actions()
    {
        var s = new InterpretedMotionState();
        var p = new MovementParameters { Speed = 1.5f, ActionStamp = 0x123 };

        s.ApplyMotion(MotionCommands.TurnRight, p);
        s.ApplyMotion(MotionCommands.SideStepRight, p with { Speed = -0.5f });
        s.ApplyMotion(MotionCommands.WalkForward, p with { Speed = 2f });
        Assert.Equal((MotionCommands.TurnRight, 1.5f), (s.TurnCommand, s.TurnSpeed));
        Assert.Equal((MotionCommands.SideStepRight, -0.5f), (s.SideStepCommand, s.SideStepSpeed));
        Assert.Equal((MotionCommands.WalkForward, 2f), (s.ForwardCommand, s.ForwardSpeed));

        s.ApplyMotion(MotionCommands.Magic, p);
        Assert.Equal(MotionCommands.Magic, s.CurrentStyle);
        Assert.Equal(MotionCommands.Ready, s.ForwardCommand);
        Assert.Equal(2f, s.ForwardSpeed); // a style keeps the forward speed

        s.ApplyMotion(Wave, p.WithFlag(MovementParameterFlags.Autonomous, true));
        Assert.Equal(new MotionActionNode(Wave, 1.5f, 0x123, true), Assert.Single(s.Actions));
        Assert.Equal(1, s.NumActions);
    }

    [Fact]
    public void Interpreted_apply_motion_treats_left_hand_commands_as_forward_substates()
    {
        // TurnLeft and SideStepLeft carry the substate bit; the interpreter always turns them into negative right-hand
        // commands first, except for an object it does not adjust (a non-creature).
        var s = new InterpretedMotionState();
        s.ApplyMotion(MotionCommands.TurnLeft, new MovementParameters { Speed = 1f });
        Assert.Equal(MotionCommands.TurnLeft, s.ForwardCommand);
        Assert.Equal(0u, s.TurnCommand);
    }

    [Fact]
    public void Interpreted_remove_motion_resets_only_what_is_current()
    {
        var s = MotionRig.State(forward: MotionCommands.WalkForward, forwardSpeed: 2f, sidestep: MotionCommands.SideStepRight, turn: MotionCommands.TurnRight, style: MotionCommands.Magic);

        s.RemoveMotion(MotionCommands.RunForward); // not current
        Assert.Equal(MotionCommands.WalkForward, s.ForwardCommand);
        s.RemoveMotion(MotionCommands.WalkForward);
        Assert.Equal((MotionCommands.Ready, 1f), (s.ForwardCommand, s.ForwardSpeed));
        s.RemoveMotion(MotionCommands.TurnLeft); // only TurnRight clears the turn
        Assert.Equal(MotionCommands.TurnRight, s.TurnCommand);
        s.RemoveMotion(MotionCommands.TurnRight);
        s.RemoveMotion(MotionCommands.SideStepRight);
        Assert.Equal((0u, 0u), (s.TurnCommand, s.SideStepCommand));
        s.RemoveMotion(MotionCommands.SwordCombat); // not current
        Assert.Equal(MotionCommands.Magic, s.CurrentStyle);
        s.RemoveMotion(MotionCommands.Magic);
        Assert.Equal(MotionCommands.NonCombat, s.CurrentStyle);
    }

    [Fact]
    public void Copy_movement_from_copies_everything_but_the_actions()
    {
        var source = MotionRig.State(forward: MotionCommands.RunForward, forwardSpeed: 2.5f, sidestep: MotionCommands.SideStepRight, sidestepSpeed: 0.5f, turn: MotionCommands.TurnRight, turnSpeed: -1f, style: MotionCommands.HandCombat);
        source.AddAction(Wave, 1f, 5, false);
        var target = new InterpretedMotionState();
        target.AddAction(MotionCommands.MagicPowerUp01, 1f, 1, true);

        target.CopyMovementFrom(source);

        Assert.Equal(
            (MotionCommands.HandCombat, MotionCommands.RunForward, 2.5f, MotionCommands.SideStepRight, 0.5f, MotionCommands.TurnRight, -1f),
            (target.CurrentStyle, target.ForwardCommand, target.ForwardSpeed, target.SideStepCommand, target.SideStepSpeed, target.TurnCommand, target.TurnSpeed));
        Assert.Equal(MotionCommands.MagicPowerUp01, Assert.Single(target.Actions).Action);
    }

    [Fact]
    public void Remove_action_takes_the_oldest_and_returns_zero_when_empty()
    {
        var s = new InterpretedMotionState();
        Assert.Equal(0u, s.RemoveAction());
        s.AddAction(Wave, 1f, 1, false);
        s.AddAction(MotionCommands.MagicPowerUp01, 1f, 2, false);
        Assert.Equal(Wave, s.RemoveAction());
        Assert.Equal(MotionCommands.MagicPowerUp01, s.RemoveAction());

        var raw = new RawMotionState();
        Assert.Equal(0u, raw.RemoveAction());
        raw.AddAction(Wave, 1f, 1, true);
        Assert.Equal(Wave, raw.RemoveAction());
    }

    [Fact]
    public void Raw_apply_motion_keeps_both_hands_and_the_hold_key_rule()
    {
        var raw = new RawMotionState();
        var setHoldKey = new MovementParameters { Speed = 0.75f, HoldKeyToApply = HoldKey.Run };
        var ownHoldKey = setHoldKey.WithFlag(MovementParameterFlags.SetHoldKey, false);

        raw.ApplyMotion(MotionCommands.TurnLeft, setHoldKey);
        Assert.Equal((MotionCommands.TurnLeft, HoldKey.Invalid, 0.75f), (raw.TurnCommand, raw.TurnHoldKey, raw.TurnSpeed));
        raw.ApplyMotion(MotionCommands.SideStepLeft, ownHoldKey);
        Assert.Equal((MotionCommands.SideStepLeft, HoldKey.Run, 0.75f), (raw.SideStepCommand, raw.SideStepHoldKey, raw.SideStepSpeed));
        raw.ApplyMotion(MotionCommands.WalkBackwards, ownHoldKey);
        Assert.Equal((MotionCommands.WalkBackwards, HoldKey.Run), (raw.ForwardCommand, raw.ForwardHoldKey));
    }

    [Fact]
    public void Raw_apply_motion_never_stores_RunForward()
    {
        var raw = new RawMotionState { ForwardCommand = MotionCommands.WalkForward, ForwardSpeed = 1f };
        raw.ApplyMotion(MotionCommands.RunForward, new MovementParameters { Speed = 3f });
        Assert.Equal((MotionCommands.WalkForward, 1f), (raw.ForwardCommand, raw.ForwardSpeed));
    }

    [Fact]
    public void Raw_apply_motion_resets_forward_only_when_the_style_changes()
    {
        var raw = new RawMotionState { ForwardCommand = MotionCommands.WalkForward };
        raw.ApplyMotion(MotionCommands.NonCombat, new MovementParameters());
        Assert.Equal(MotionCommands.WalkForward, raw.ForwardCommand); // same style: nothing

        raw.ApplyMotion(MotionCommands.SwordCombat, new MovementParameters());
        Assert.Equal((MotionCommands.SwordCombat, MotionCommands.Ready), (raw.CurrentStyle, raw.ForwardCommand));

        raw.ApplyMotion(Wave, new MovementParameters { Speed = 2f, ActionStamp = 9 });
        Assert.Equal(new MotionActionNode(Wave, 2f, 9, false), Assert.Single(raw.Actions));
    }

    [Fact]
    public void Raw_remove_motion_clears_either_hand()
    {
        var raw = new RawMotionState
        {
            SideStepCommand = MotionCommands.SideStepLeft,
            TurnCommand = MotionCommands.TurnLeft,
            ForwardCommand = MotionCommands.WalkBackwards,
            ForwardSpeed = 2f,
            CurrentStyle = MotionCommands.Magic,
        };

        raw.RemoveMotion(MotionCommands.SideStepRight);
        raw.RemoveMotion(MotionCommands.TurnRight);
        raw.RemoveMotion(MotionCommands.WalkBackwards);
        raw.RemoveMotion(MotionCommands.Magic);

        Assert.Equal((0u, 0u), (raw.SideStepCommand, raw.TurnCommand));
        Assert.Equal((MotionCommands.Ready, 1f), (raw.ForwardCommand, raw.ForwardSpeed));
        Assert.Equal(MotionCommands.NonCombat, raw.CurrentStyle);
    }
}

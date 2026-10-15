using Lifestone.Data.Animation;
using Lifestone.Data.Motion;

namespace Lifestone.Data.Tests.Motion;

/// <summary>
/// The motion-state data ported from ACE (data and defaults only): the defaults equal ACE's <c>InitDefaults</c>
/// (RawMotionState.cs:100-115, InterpretedMotionState.cs:72-83), and copies are independent.
/// </summary>
public sealed class MotionStateDataTests
{
    [Fact]
    public void Hold_keys_have_the_retail_values()
    {
        Assert.Equal(0u, (uint)HoldKey.Invalid);
        Assert.Equal(1u, (uint)HoldKey.None);
        Assert.Equal(2u, (uint)HoldKey.Run);
    }

    [Fact]
    public void Raw_state_defaults_are_NonCombat_Ready_at_speed_1()
    {
        var raw = new RawMotionState();

        Assert.Equal(HoldKey.None, raw.CurrentHoldKey);
        Assert.Equal(0x8000003Du, raw.CurrentStyle);
        Assert.Equal(MotionCommands.NonCombat, raw.CurrentStyle);
        Assert.Equal(0x41000003u, raw.ForwardCommand);
        Assert.Equal(MotionCommands.Ready, raw.ForwardCommand);
        Assert.Equal(HoldKey.Invalid, raw.ForwardHoldKey);
        Assert.Equal(1f, raw.ForwardSpeed);
        Assert.Equal(0u, raw.SideStepCommand);
        Assert.Equal(HoldKey.Invalid, raw.SideStepHoldKey);
        Assert.Equal(1f, raw.SideStepSpeed);
        Assert.Equal(0u, raw.TurnCommand);
        Assert.Equal(HoldKey.Invalid, raw.TurnHoldKey);
        Assert.Equal(1f, raw.TurnSpeed);
        Assert.Empty(raw.Actions);
    }

    [Fact]
    public void Interpreted_state_defaults_are_NonCombat_Ready_at_speed_1()
    {
        var state = new InterpretedMotionState();

        Assert.Equal(0x8000003Du, state.CurrentStyle);
        Assert.Equal(0x41000003u, state.ForwardCommand);
        Assert.Equal(1f, state.ForwardSpeed);
        Assert.Equal(0u, state.SideStepCommand);
        Assert.Equal(1f, state.SideStepSpeed);
        Assert.Equal(0u, state.TurnCommand);
        Assert.Equal(1f, state.TurnSpeed);
        Assert.Empty(state.Actions);
    }

    [Fact]
    public void Init_defaults_resets_a_changed_raw_state_and_keeps_its_action_list()
    {
        var raw = new RawMotionState
        {
            CurrentHoldKey = HoldKey.Run,
            CurrentStyle = MotionCommands.Magic,
            ForwardCommand = MotionCommands.WalkForward,
            ForwardHoldKey = HoldKey.Run,
            ForwardSpeed = 2.5f,
            SideStepCommand = MotionCommands.SideStepRight,
            SideStepHoldKey = HoldKey.None,
            SideStepSpeed = 0.5f,
            TurnCommand = MotionCommands.TurnRight,
            TurnHoldKey = HoldKey.None,
            TurnSpeed = 1.5f,
        };
        var actions = raw.Actions;
        actions.Add(new MotionActionNode(0x13000087, 1f, 7, Autonomous: true));

        raw.InitDefaults();

        Assert.Equal(HoldKey.None, raw.CurrentHoldKey);
        Assert.Equal(MotionCommands.NonCombat, raw.CurrentStyle);
        Assert.Equal(MotionCommands.Ready, raw.ForwardCommand);
        Assert.Equal(HoldKey.Invalid, raw.ForwardHoldKey);
        Assert.Equal(1f, raw.ForwardSpeed);
        Assert.Equal(0u, raw.SideStepCommand);
        Assert.Equal(HoldKey.Invalid, raw.SideStepHoldKey);
        Assert.Equal(1f, raw.SideStepSpeed);
        Assert.Equal(0u, raw.TurnCommand);
        Assert.Equal(HoldKey.Invalid, raw.TurnHoldKey);
        Assert.Equal(1f, raw.TurnSpeed);
        Assert.Empty(raw.Actions);
        Assert.Same(actions, raw.Actions);
    }

    [Fact]
    public void Init_defaults_resets_a_changed_interpreted_state()
    {
        var state = new InterpretedMotionState
        {
            CurrentStyle = MotionCommands.Magic,
            ForwardCommand = MotionCommands.RunForward,
            ForwardSpeed = 3f,
            SideStepCommand = MotionCommands.SideStepRight,
            SideStepSpeed = 2f,
            TurnCommand = MotionCommands.TurnRight,
            TurnSpeed = 2f,
        };
        state.Actions.Add(new MotionActionNode(0x13000087, 1f, 1, Autonomous: false));

        state.InitDefaults();

        Assert.Equal(MotionCommands.NonCombat, state.CurrentStyle);
        Assert.Equal(MotionCommands.Ready, state.ForwardCommand);
        Assert.Equal(1f, state.ForwardSpeed);
        Assert.Equal(0u, state.SideStepCommand);
        Assert.Equal(1f, state.SideStepSpeed);
        Assert.Equal(0u, state.TurnCommand);
        Assert.Equal(1f, state.TurnSpeed);
        Assert.Empty(state.Actions);
    }

    [Fact]
    public void A_raw_clone_is_independent_of_its_source()
    {
        var raw = new RawMotionState { CurrentHoldKey = HoldKey.Run, ForwardCommand = MotionCommands.WalkForward, ForwardSpeed = 2f };
        raw.Actions.Add(new MotionActionNode(0x13000087, 1f, 3, Autonomous: true));

        var copy = raw.Clone();
        Assert.Equal(HoldKey.Run, copy.CurrentHoldKey);
        Assert.Equal(MotionCommands.WalkForward, copy.ForwardCommand);
        Assert.Equal(2f, copy.ForwardSpeed);
        Assert.Equal(raw.Actions, copy.Actions);
        Assert.NotSame(raw.Actions, copy.Actions);

        copy.ForwardCommand = MotionCommands.Ready;
        copy.Actions.Clear();
        Assert.Equal(MotionCommands.WalkForward, raw.ForwardCommand);
        Assert.Single(raw.Actions);
    }

    [Fact]
    public void An_interpreted_clone_is_independent_of_its_source()
    {
        var state = new InterpretedMotionState { ForwardCommand = MotionCommands.RunForward, ForwardSpeed = 2.9f, TurnCommand = MotionCommands.TurnRight };
        state.Actions.Add(new MotionActionNode(0x13000087, 1.25f, 0xFFFF, Autonomous: false));

        var copy = state.Clone();
        Assert.Equal(MotionCommands.RunForward, copy.ForwardCommand);
        Assert.Equal(2.9f, copy.ForwardSpeed);
        Assert.Equal(MotionCommands.TurnRight, copy.TurnCommand);
        Assert.Equal(state.Actions, copy.Actions);

        copy.TurnCommand = 0;
        copy.Actions.Add(new MotionActionNode(0x13000087, 1f, 0, Autonomous: false));
        Assert.Equal(MotionCommands.TurnRight, state.TurnCommand);
        Assert.Single(state.Actions);
    }

    [Fact]
    public void Action_nodes_compare_by_value()
    {
        Assert.Equal(new MotionActionNode(0x13000087, 1f, 9, true), new MotionActionNode(0x13000087, 1f, 9, true));
        Assert.NotEqual(new MotionActionNode(0x13000087, 1f, 9, true), new MotionActionNode(0x13000087, 1f, 10, true));
    }
}

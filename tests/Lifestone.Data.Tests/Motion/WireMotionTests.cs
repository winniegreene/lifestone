using Lifestone.Data.Animation;
using Lifestone.Data.Motion;

namespace Lifestone.Data.Tests.Motion;

/// <summary>
/// The wire form of interpreted motion states (AV4): retail <c>InterpretedMotionState::UnPack</c>'s defaults and the
/// 11.6096 command table.
/// </summary>
public sealed class WireMotionTests
{
    [Fact]
    public void Absent_fields_take_the_retail_defaults()
    {
        var into = MotionRig.State(forward: MotionCommands.RunForward, forwardSpeed: 3f, sidestep: MotionCommands.SideStepRight, turn: MotionCommands.TurnRight, style: MotionCommands.Magic);
        into.AddAction(MotionCommands.MagicPowerUp01, 1f, 3, false);

        Assert.True(WireMotion.TryToInterpreted(default, [], into, out _));

        Assert.Equal(MotionCommands.NonCombat, into.CurrentStyle);
        Assert.Equal(MotionCommands.Ready, into.ForwardCommand);
        Assert.Equal(0u, into.SideStepCommand);
        Assert.Equal(0u, into.TurnCommand);
        Assert.Equal((1f, 1f, 1f), (into.ForwardSpeed, into.SideStepSpeed, into.TurnSpeed));
        Assert.Empty(into.Actions);
    }

    [Fact]
    public void Low_words_map_through_the_11_6096_table()
    {
        var wire = new WireInterpretedMotion(Style: 0x3E, Forward: 0x07, Sidestep: 0x0F, Turn: 0x0D, ForwardSpeed: 2.25f, SidestepSpeed: -0.5f, TurnSpeed: 1.5f);
        var into = new InterpretedMotionState();

        Assert.True(WireMotion.TryToInterpreted(wire, [], into, out _));

        Assert.Equal(MotionCommands.SwordCombat, into.CurrentStyle);
        Assert.Equal(0x44000007u, into.ForwardCommand); // RunForward
        Assert.Equal(MotionCommands.SideStepRight, into.SideStepCommand);
        Assert.Equal(MotionCommands.TurnRight, into.TurnCommand);
        Assert.Equal((2.25f, -0.5f, 1.5f), (into.ForwardSpeed, into.SideStepSpeed, into.TurnSpeed));
    }

    [Theory]
    [InlineData(0x0003, 0x41000003u)] // Ready
    [InlineData(0x0007, 0x44000007u)] // RunForward
    [InlineData(0x0015, 0x40000015u)] // Falling
    [InlineData(0x0087, 0x13000087u)] // Wave
    [InlineData(0x011B, 0x4300011Bu)] // AFKState: past ordinal 0x10F, where the 2013 table is off by three
    [InlineData(0x012B, 0x1000012Bu)] // MagicPowerUp01Purple
    [InlineData(0x019B, 0x1000019Bu)] // AI_TelegraphCast, new in 11.6096
    public void Commands_past_ordinal_0x10F_use_the_11_6096_ids(ushort lowWord, uint expected)
    {
        Assert.True(WireMotion.TryCommand(lowWord, out var command));
        Assert.Equal(expected, command);
    }

    [Fact]
    public void Actions_keep_15_bit_stamps_the_autonomous_bit_and_speed()
    {
        var into = new InterpretedMotionState();
        WireMotionItem[] actions = [new(0x0087, 0x7FFF, true, 1.25f), new(0x006F, 0x8001, false, 2f)];

        Assert.True(WireMotion.TryToInterpreted(new WireInterpretedMotion(null, null, null, null, null, null, null), actions, into, out _));

        Assert.Equal(
            new[] { new MotionActionNode(0x13000087, 1.25f, 0x7FFF, true), new MotionActionNode(MotionCommands.MagicPowerUp01, 2f, 0x0001, false) },
            into.Actions);
    }

    [Theory]
    [InlineData(WireMotionField.Style)]
    [InlineData(WireMotionField.Forward)]
    [InlineData(WireMotionField.Sidestep)]
    [InlineData(WireMotionField.Turn)]
    [InlineData(WireMotionField.Action)]
    public void An_unknown_low_word_fails_loudly_and_changes_nothing(WireMotionField field)
    {
        const ushort Unknown = MotionCommandTable.Count; // 0x19C, one past the table
        var wire = new WireInterpretedMotion(
            field == WireMotionField.Style ? Unknown : null,
            field == WireMotionField.Forward ? Unknown : (ushort)0x05,
            field == WireMotionField.Sidestep ? Unknown : null,
            field == WireMotionField.Turn ? Unknown : null,
            2f,
            null,
            null);
        WireMotionItem[] actions = field == WireMotionField.Action ? [new(0x0087, 1, false, 1f), new(Unknown, 2, false, 1f)] : [];
        var into = MotionRig.State(forward: MotionCommands.RunForward, forwardSpeed: 3f);
        into.AddAction(MotionCommands.MagicPowerUp01, 1f, 3, false);

        Assert.False(WireMotion.TryToInterpreted(wire, actions, into, out var failure));

        Assert.Equal(field, failure.Field);
        Assert.Equal(Unknown, failure.LowWord);
        Assert.Equal(field == WireMotionField.Action ? 1 : 0, failure.Index);
        Assert.Contains("0x019C", failure.Message, StringComparison.Ordinal);
        Assert.Contains("412", failure.Message, StringComparison.Ordinal);
        Assert.Equal((MotionCommands.RunForward, 3f), (into.ForwardCommand, into.ForwardSpeed));
        Assert.Equal(MotionCommands.MagicPowerUp01, Assert.Single(into.Actions).Action);
    }

    [Fact]
    public void To_wire_writes_only_the_fields_that_differ_from_their_defaults()
    {
        Assert.Equal(new WireInterpretedMotion(null, null, null, null, null, null, null), WireMotion.ToWire(new InterpretedMotionState(), out var none));
        Assert.Empty(none);

        var state = MotionRig.State(forward: MotionCommands.WalkForward, forwardSpeed: -0.65f, turn: MotionCommands.TurnRight, turnSpeed: 1f, style: MotionCommands.Magic);
        Assert.Equal(new WireInterpretedMotion(0x49, 0x05, null, 0x0D, -0.65f, null, null), WireMotion.ToWire(state, out _));
    }

    [Fact]
    public void Wire_round_trip_restores_the_state()
    {
        var rng = new Random(20261012);
        uint[] forwards = [MotionCommands.Ready, MotionCommands.WalkForward, MotionCommands.RunForward, MotionCommands.Falling, 0x41000013, MotionCommands.Dead];
        uint[] styles = [MotionCommands.NonCombat, MotionCommands.HandCombat, MotionCommands.SwordCombat, MotionCommands.Magic];
        uint[] actions = [0x13000087, MotionCommands.MagicPowerUp01, 0x1000012B, 0x1000019B];
        for (var i = 0; i < 200; i++)
        {
            var state = MotionRig.State(
                forward: forwards[rng.Next(forwards.Length)],
                forwardSpeed: rng.Next(2) == 0 ? 1f : (float)(rng.NextDouble() * 4 - 2),
                sidestep: rng.Next(2) == 0 ? 0 : MotionCommands.SideStepRight,
                sidestepSpeed: (float)(rng.NextDouble() * 3),
                turn: rng.Next(2) == 0 ? 0 : MotionCommands.TurnRight,
                turnSpeed: rng.Next(2) == 0 ? 1f : -1.5f,
                style: styles[rng.Next(styles.Length)]);
            for (var a = rng.Next(4); a > 0; a--)
            {
                state.AddAction(actions[rng.Next(actions.Length)], (float)rng.NextDouble() * 2, (ushort)rng.Next(0x8000), rng.Next(2) == 0);
            }

            var wire = WireMotion.ToWire(state, out var items);
            var back = new InterpretedMotionState();
            Assert.True(WireMotion.TryToInterpreted(wire, items, back, out _));

            Assert.Equal(
                (state.CurrentStyle, state.ForwardCommand, state.ForwardSpeed, state.SideStepCommand, state.SideStepSpeed, state.TurnCommand, state.TurnSpeed),
                (back.CurrentStyle, back.ForwardCommand, back.ForwardSpeed, back.SideStepCommand, back.SideStepSpeed, back.TurnCommand, back.TurnSpeed));
            Assert.Equal(state.Actions, back.Actions);
        }
    }
}

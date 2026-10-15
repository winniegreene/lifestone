using System.Numerics;
using Lifestone.Client.Movement;
using Lifestone.Client.Play;
using static Lifestone.Client.Tests.Movement.MovementTestKit;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Play;

/// <summary>Play mode's movement gate: nothing reaches the session's channel before the server has the player present, everything does after.</summary>
public sealed class PresenceGatedMovementSinkTests
{
    [Fact]
    public void Nothing_is_forwarded_until_the_player_is_present_and_the_held_messages_are_counted()
    {
        var channel = new CountingSink();
        var present = false;
        var gate = new PresenceGatedMovementSink(channel, () => present);
        var at = default(CellFrame);

        gate.MoveToState(default!, in at, true, false);
        gate.AutonomousPosition(in at, true);
        gate.Jump(1f, Vector3.Zero, in at);
        Assert.Equal(0, channel.Total);
        Assert.Equal(3, gate.Held);

        present = true;
        gate.MoveToState(default!, in at, true, false);
        gate.AutonomousPosition(in at, true);
        gate.Jump(1f, Vector3.Zero, in at);
        Assert.Equal((1, 1, 1), (channel.MoveToStates, channel.Positions, channel.Jumps));
        Assert.Equal(3, gate.Held);
        Assert.Same(channel, gate.Inner);
    }

    [Fact]
    public void A_player_that_stands_still_after_the_gate_opens_sends_its_position_on_the_next_frame()
    {
        // dod-8-9-5: a position dropped by the closed gate used to count as sent, so a player who arrived and stood still told
        // the server nothing until it moved.
        var channel = new CountingSink();
        var present = false;
        var gate = new PresenceGatedMovementSink(channel, () => present);
        var world = WorldOf(Flat(Block));
        var player = Player(world, At(Block, 96f, 96f, 0f));
        var controller = new PlayerMovementController(player, gate, MovementOptions.Default);
        Run(controller, 3.0);
        Assert.Equal(0, channel.Total);
        Assert.Equal(0, gate.Held); // a closed gate is not asked to send positions

        present = true;
        Run(controller, 1.0 / 30.0 * 2);
        Assert.Equal(1, channel.Positions);
        Run(controller, 2.0);
        Assert.Equal(1, channel.Positions); // standing still: the one is enough
    }

    [Fact]
    public void A_gate_needs_a_channel_and_a_presence_function()
    {
        Assert.Throws<ArgumentNullException>(() => new PresenceGatedMovementSink(null!, () => true));
        Assert.Throws<ArgumentNullException>(() => new PresenceGatedMovementSink(new CountingSink(), null!));
    }

    private sealed class CountingSink : IMovementSink
    {
        public int MoveToStates { get; private set; }

        public int Positions { get; private set; }

        public int Jumps { get; private set; }

        public int Total => MoveToStates + Positions + Jumps;

        public void MoveToState(RawMotionState raw, in CellFrame position, bool contact, bool standingLongJump) => MoveToStates++;

        public void AutonomousPosition(in CellFrame position, bool contact) => Positions++;

        public void Jump(float extent, Vector3 velocity, in CellFrame position) => Jumps++;
    }
}

using Lifestone.Client.Tests.Movement;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session;

/// <summary>The game's player as a movement scenario's walker (<see cref="RoutePlayHost"/>, G6 W12): where the player's messages go.</summary>
public sealed class RoutePlayHostTests
{
    private static readonly CellFrame Here = new(0x860201AD, new AcFrame(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity));

    [Fact]
    public void The_players_sink_goes_to_the_gate_until_a_scenario_puts_its_audit_in_front()
    {
        var gate = new RecordingMovementSink();
        var audit = new RecordingMovementSink();
        var host = new RoutePlayHost(() => null, () => 0);
        host.Inner = gate;

        host.Sink.AutonomousPosition(Here, true);
        host.Current = audit;
        host.Sink.AutonomousPosition(Here, true);
        host.Sink.Jump(0.5f, System.Numerics.Vector3.UnitZ, Here);
        host.Current = null;
        host.Sink.MoveToState(new RawMotionState(), Here, true, false);

        Assert.Equal(2, gate.Calls.Count);
        Assert.Equal(2, audit.Calls.Count);
        Assert.Equal(RecordingMovementSink.Kind.Jump, audit.Calls[1].Kind);
    }

    [Fact]
    public void A_sink_without_a_gate_behind_it_fails_loudly()
    {
        var host = new RoutePlayHost(() => null, () => 0);

        var problem = Assert.Throws<InvalidOperationException>(() => host.Sink.AutonomousPosition(Here, true));

        Assert.Contains("TODO(g6-logincheck-sink)", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_scenario_holds_the_movement_back_through_the_host_and_the_gate_reads_it()
    {
        var held = 7L;
        var host = new RoutePlayHost(() => null, () => held);

        Assert.False(host.Holding);
        Assert.Equal(7, host.Held);
        var returning = false;
        host.Hold = () => returning;
        Assert.False(host.Holding);
        returning = true;
        Assert.True(host.Holding);
        Assert.Null(host.Walker);
    }
}

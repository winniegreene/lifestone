using System.Numerics;
using System.Text.Json;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.World;
using static Lifestone.Client.Tests.Movement.MovementTestKit;

namespace Lifestone.Client.Tests.Movement;

/// <summary>
/// <see cref="MovementAudit"/> (m2-plan PH9): it passes every movement message on, counts them and the player's
/// corrections, echoes and teleports from the session's events, records where-am-I checks, and prints the frozen
/// movement verdict line.
/// </summary>
public sealed class MovementAuditTests
{
    [Theory]
    [InlineData(0, 0.031, "movement route=holtburg-laps laps=3 corrections=0 where_max_m=0.03 result=PASS")]
    [InlineData(0, 0.5, "movement route=holtburg-laps laps=3 corrections=0 where_max_m=0.50 result=PASS")]
    [InlineData(0, 0.51, "movement route=holtburg-laps laps=3 corrections=0 where_max_m=0.51 result=FAIL")]
    [InlineData(1, 0.0, "movement route=holtburg-laps laps=3 corrections=1 where_max_m=0.00 result=FAIL")]
    [InlineData(0, null, "movement route=holtburg-laps laps=3 corrections=0 where_max_m=none result=FAIL")]
    public void The_movement_verdict_line_follows_the_text_contract(int corrections, double? whereMax, string expected)
    {
        var summary = new MovementAuditSummary { Route = "holtburg-laps", Laps = 3, Corrections = corrections, WhereMaxMetres = whereMax };
        Assert.Equal(expected, MovementAudit.FormatSummaryLine(summary));
    }

    [Fact]
    public void A_changed_force_position_sequence_between_checks_fails_the_walk()
    {
        var summary = new MovementAuditSummary
        {
            Route = "academy-exit",
            Laps = 1,
            WhereMaxMetres = 0.1,
            ForcePositionSequenceFirst = 4,
            ForcePositionSequenceLast = 5,
        };
        Assert.False(summary.Passed);
        Assert.EndsWith("result=FAIL", MovementAudit.FormatSummaryLine(summary), StringComparison.Ordinal);
    }

    [Fact]
    public void The_audit_passes_every_message_on_and_counts_what_the_session_reports()
    {
        var bus = new FakeEventBus();
        var inner = new RecordingMovementSink();
        var now = 0.0;
        using var audit = new MovementAudit(inner, bus, () => now, "holtburg-laps");
        var at = At(Block, 10f, 10f, 0f);
        var raw = new RawMotionState { ForwardCommand = MotionCommands.WalkForward };

        audit.MoveToState(raw, at, contact: true, standingLongJump: false);
        now = 1.02;
        audit.AutonomousPosition(at, contact: true);
        now = 1.05;
        bus.Publish(new ObjectMoved(0x50000001, at, PositionVerdict.SelfEcho, false, true, null, null));
        now = 1.5;
        audit.Jump(0.5f, new Vector3(0, 0, 3f), at);
        now = 3.0;
        audit.AutonomousPosition(at, contact: true); // after a jump: not an interval
        bus.Publish(new ObjectMoved(0x50000001, at, PositionVerdict.ForceBlip, false, true, null, null));
        bus.Publish(new ObjectMoved(0x50000001, at, PositionVerdict.SelfTeleport, true, true, null, null));
        bus.Publish(new ObjectMoved(0x80000002, at, PositionVerdict.Remote, false, true, null, null));

        Assert.Equal(4, inner.Calls.Count);
        var summary = audit.Summary;
        Assert.Equal(1, summary.MoveToStateSent);
        Assert.Equal(2, summary.AutonomousPositionSent);
        Assert.Equal(1, summary.JumpSent);
        Assert.Equal(1, summary.SelfEchoes);
        Assert.Equal(1, summary.Corrections);
        Assert.Equal(1, summary.Teleports);
        Assert.Equal(1.02, summary.MaxAutonomousPositionIntervalSeconds, 6);
        Assert.Equal(0.03, summary.MaxEchoDelaySeconds!.Value, 6);
        Assert.False(summary.Passed);
    }

    [Fact]
    public void Where_checks_measure_across_landblocks_and_keep_the_force_position_sequences()
    {
        var bus = new FakeEventBus();
        using var audit = new MovementAudit(new NullMovementSink(), bus, route: "holtburg-laps");
        var client = new CellFrame(0xA9B40038, new AcFrame(new Vector3(191.9f, 100f, 50f), Quaternion.Identity));
        var server = new CellFrame(0xAAB40001, new AcFrame(new Vector3(0.2f, 100.3f, 50f), Quaternion.Identity));

        Assert.Equal(0.42426, audit.RecordWhere(client, server, forcePositionSequence: 7), 3);
        audit.RecordWhere(client, client, forcePositionSequence: 7);
        audit.CompleteLap();
        audit.CompleteLap();
        audit.CompleteLap();

        var summary = audit.Summary;
        Assert.Equal(2, summary.WhereChecks);
        Assert.Equal(0.42426, summary.WhereMaxMetres!.Value, 3);
        Assert.Equal((ushort)7, summary.ForcePositionSequenceFirst);
        Assert.Equal((ushort)7, summary.ForcePositionSequenceLast);
        Assert.True(summary.Passed);
        Assert.Equal("movement route=holtburg-laps laps=3 corrections=0 where_max_m=0.42 result=PASS", audit.SummaryLine());

        using var json = JsonDocument.Parse(audit.ToJson());
        Assert.Equal("holtburg-laps", json.RootElement.GetProperty("Route").GetString());
        Assert.True(json.RootElement.GetProperty("Passed").GetBoolean());
    }

    [Fact]
    public void Disposing_stops_following_the_bus()
    {
        var bus = new FakeEventBus();
        var audit = new MovementAudit(new NullMovementSink(), bus);
        audit.Dispose();
        audit.Dispose();
        bus.Publish(new ObjectMoved(0x50000001, At(Block, 1f, 1f, 0f), PositionVerdict.ForceBlip, false, true, null, null));
        Assert.Equal(0, audit.Summary.Corrections);
    }

    /// <summary>An event bus with synchronous publishing for tests.</summary>
    private sealed class FakeEventBus : IEventBus
    {
        private readonly List<(Type Type, Delegate Handler)> _handlers = [];

        public IDisposable Subscribe<T>(BusHandler<T> handler)
            where T : struct, IGameEvent
        {
            var entry = (typeof(T), (Delegate)handler);
            _handlers.Add(entry);
            return new Subscription(() => _handlers.Remove(entry));
        }

        public IDisposable SubscribeRaw(RawMessageHandler handler) => new Subscription(() => { });

        public long Count<T>()
            where T : struct, IGameEvent => 0;

        public void Publish<T>(T e)
            where T : struct, IGameEvent
        {
            foreach (var (type, handler) in _handlers.ToArray())
            {
                if (type == typeof(T))
                {
                    ((BusHandler<T>)handler)(in e);
                }
            }
        }

        private sealed class Subscription(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }
}

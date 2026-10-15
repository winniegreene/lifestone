using Lifestone.Client.Tests;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages;

/// <summary>
/// The world's hot path allocates nothing (m2-plan §3.3 N7): 10,000 F748 UpdatePosition dispatches through
/// <see cref="InboundDispatcher"/>, and the other struct messages (F747, F74E, F74B, F751, F749, F74A, F750, F755, and
/// N10's six PrivateUpdate messages), allocate 0 bytes on this thread. Runs in the non-parallel allocation collection; a window with a GC inside is
/// measured again.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class AllocationTests
{
    private const int Dispatches = 10_000;

    private static readonly InboundContext Context = new(1, MessageQueue.SmartBox, 0);

    [Fact]
    public void Ten_thousand_UpdatePosition_dispatches_allocate_nothing()
    {
        byte[][] bodies =
        [
            AceWireWriters.UpdatePosition(0x50000001, new AcePosition(0xA9B40019, 84f, 7.1f, 94f, 0.996917f, 0f, 0f, -0.0784591f), true, new AceUpdatePositionSequences(1, 7, 0, 0)),
            AceWireWriters.UpdatePosition(0x80000001, new AcePosition(0xA9B40019, 1f, 2f, 3f, 0.5f, 0.5f, 0.5f, 0.5f), false, new AceUpdatePositionSequences(2, 8, 1, 1), (1f, 2f, 3f), 0x65),
        ];
        var handler = new SummingHandler();

        Assert.Equal(0, Measure(bodies, handler));
        Assert.Equal(2 * Dispatches, handler.Positions);
        Assert.NotEqual(0d, handler.Sum);
    }

    [Fact]
    public void The_other_struct_messages_dispatch_without_allocating()
    {
        byte[][] bodies =
        [
            AceWireWriters.ObjectDelete(0x80000001, 3),
            AceWireWriters.VectorUpdate(0x80000001, (1f, 0f, 0f), (0f, 0f, 1f), 1, 2),
            AceWireWriters.SetState(0x50000001, 0x408, 1, 4),
            AceWireWriters.PlayerTeleport(9),
            AceWireWriters.ParentEvent(0x50000001, 0x80000002, 1, 1, 1, 1),
            AceWireWriters.PickupEvent(0x80000002, 1, 2),
            AceWireWriters.Sound(0x50000001, 0x77, 1f),
            AceWireWriters.PlayEffect(0x50000001, 0x8A, 1f),
        ];
        var handler = new SummingHandler();

        Assert.Equal(0, Measure(bodies, handler));
        Assert.Equal(bodies.Length * Dispatches, handler.Others);
    }

    [Fact]
    public void The_private_updates_dispatch_without_allocating()
    {
        byte[][] bodies =
        [
            AceWireWriters.PrivateUpdatePropertyInt(1, 0x05, 1234),
            AceWireWriters.PrivateUpdatePropertyInt64(2, 0x02, 99),
            AceWireWriters.PrivateUpdateSkill(3, new AceSkill(0x06, 12, 2, 1500, 10)),
            AceWireWriters.PrivateUpdateAttribute(4, 0x01, 5, 100, 2500),
            AceWireWriters.PrivateUpdateVital(5, 0x01, 2, 0, 800, 61),
            AceWireWriters.PrivateUpdateAttribute2ndLevel(6, 0x02, 29),
        ];
        var handler = new SummingHandler();

        Assert.Equal(0, Measure(bodies, handler));
        Assert.Equal(bodies.Length * Dispatches, handler.Others);
    }

    /// <summary>Bytes allocated by <see cref="Dispatches"/> rounds over <paramref name="bodies"/>; up to 3 windows, to
    /// step over one that a GC lands in.</summary>
    private static long Measure(byte[][] bodies, SummingHandler handler)
    {
        Run(bodies, handler, 100);  // warm the JIT and the dispatcher's paths
        handler.Reset();
        for (var attempt = 0; ; attempt++)
        {
            var window = AllocationWindow.Open();
            Run(bodies, handler, Dispatches);
            var (bytes, gcInside) = window.Close();
            if (!gcInside || attempt == 2)
            {
                return bytes;
            }

            handler.Reset();
        }
    }

    private static void Run(byte[][] bodies, SummingHandler handler, int rounds)
    {
        for (var i = 0; i < rounds; i++)
        {
            foreach (var body in bodies)
            {
                if (InboundDispatcher.Dispatch(Context, body, handler).Outcome != DispatchOutcome.Decoded)
                {
                    throw new InvalidOperationException("a sample did not decode");
                }
            }
        }
    }

    /// <summary>Reads every field it gets, so nothing is optimised away; allocates nothing itself.</summary>
    private sealed class SummingHandler : IInboundMessageHandler
    {
        public double Sum { get; private set; }

        public int Positions { get; private set; }

        public int Others { get; private set; }

        public void Reset() => (Sum, Positions, Others) = (0, 0, 0);

        public void OnUpdatePosition(in InboundContext context, in UpdatePositionMsg message)
        {
            Positions++;
            Sum += message.Position.Origin.X + message.Position.Rotation.W + message.PositionSeq + (message.Velocity?.Z ?? 0f) + (message.Placement ?? 0u);
        }

        public void OnDeleteObject(in InboundContext context, in DeleteObjectMsg message) => Count(message.InstanceSeq);

        public void OnVectorUpdate(in InboundContext context, in VectorUpdateMsg message) => Count(message.Omega.Z);

        public void OnSetState(in InboundContext context, in SetStateMsg message) => Count((uint)message.State);

        public void OnPlayerTeleport(in InboundContext context, in PlayerTeleportMsg message) => Count(message.TeleportSeq);

        public void OnParentEvent(in InboundContext context, in ParentEventMsg message) => Count(message.Placement);

        public void OnPickupEvent(in InboundContext context, in PickupEventMsg message) => Count(message.PositionSeq);

        public void OnSound(in InboundContext context, in SoundMsg message) => Count(message.Volume);

        public void OnPlayEffect(in InboundContext context, in PlayEffectMsg message) => Count(message.Speed);

        public void OnPrivateUpdate(in InboundContext context, in PrivateUpdateMsg message) =>
            Count(message.Value + message.Sequence + message.Skill.Experience + message.Attribute.Start + message.Vital.Current);

        private void Count(double value)
        {
            Others++;
            Sum += value;
        }
    }
}

using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages.Self;

/// <summary>
/// The six PrivateUpdate messages (m2-plan §3.3 N10) against ACE's writers: the sequence byte, the unaligned id and the
/// value round-trip; each message is exactly as long as its layout, so a trailing byte or a truncation is malformed; and
/// the dispatcher hands every one to <see cref="IInboundMessageHandler.OnPrivateUpdate"/>.
/// </summary>
public sealed class PrivateUpdateCodecTests
{
    public static TheoryData<byte[], PrivateUpdateMsg> Samples => new()
    {
        {
            AceWireWriters.PrivateUpdatePropertyInt(7, 0x05, -1234),
            new PrivateUpdateMsg(PrivateUpdateKind.PropertyInt, 7, 0x05, -1234, default, default, default)
        },
        {
            AceWireWriters.PrivateUpdatePropertyInt64(255, 0x02, 0x1_0000_0002L),
            new PrivateUpdateMsg(PrivateUpdateKind.PropertyInt64, 255, 0x02, 0x1_0000_0002L, default, default, default)
        },
        {
            AceWireWriters.PrivateUpdateSkill(3, new AceSkill(0x06, 12, 2, 1500, 10, 4, 1234.5)),
            new PrivateUpdateMsg(PrivateUpdateKind.Skill, 3, 0x06, 0, default, default, new SkillDesc(0x06, 12, 1, 2, 1500, 10, 4, 1234.5))
        },
        {
            AceWireWriters.PrivateUpdateAttribute(4, 0x01, 5, 100, 2500),
            new PrivateUpdateMsg(PrivateUpdateKind.Attribute, 4, 0x01, 0, new AttributeDesc(0x01, 5, 100, 2500), default, default)
        },
        {
            AceWireWriters.PrivateUpdateVital(5, 0x03, 2, 0, 800, 61),
            new PrivateUpdateMsg(PrivateUpdateKind.Vital, 5, 0x03, 0, default, new VitalDesc(0x03, 2, 0, 800, 61), default)
        },
        {
            AceWireWriters.PrivateUpdateAttribute2ndLevel(6, 0x02, 29),
            new PrivateUpdateMsg(PrivateUpdateKind.VitalCurrent, 6, 0x02, 29, default, default, default)
        },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Every_update_round_trips_and_is_exact(byte[] body, PrivateUpdateMsg expected)
    {
        Assert.True(PrivateUpdateCodec.TryParse(body, out var update));
        Assert.Equal(expected, update);
        Assert.Equal((uint)expected.Kind, BitConverter.ToUInt32(body));

        Assert.False(PrivateUpdateCodec.TryParse([.. body, 0], out _), "a trailing byte was accepted");
        for (var length = 0; length < body.Length; length++)
        {
            Assert.False(PrivateUpdateCodec.TryParse(body[..length], out _), $"a {length}-byte prefix was accepted");
        }
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void The_dispatcher_hands_every_update_to_OnPrivateUpdate(byte[] body, PrivateUpdateMsg expected)
    {
        var handler = new Recorder();
        var result = InboundDispatcher.Dispatch(new InboundContext(1, MessageQueue.Ui, 0), body, handler);
        Assert.Equal(new DispatchResult(DispatchOutcome.Decoded, (uint)expected.Kind), result);
        Assert.Equal(expected, handler.Update);

        var truncated = InboundDispatcher.Dispatch(new InboundContext(2, MessageQueue.Ui, 0), body[..^1], handler);
        Assert.Equal(DispatchOutcome.Malformed, truncated.Outcome);
        Assert.StartsWith($"{(uint)expected.Kind:X4} PrivateUpdate", handler.Malformed);

        // A trailing byte is malformed too (this test took over the dispatcher table's PrivateUpdate rows: polish fold data-net-mod).
        Assert.Equal(DispatchOutcome.Malformed, InboundDispatcher.Dispatch(new InboundContext(3, MessageQueue.Ui, 0), [.. body, 0xAA], handler).Outcome);
    }

    [Fact]
    public void The_lengths_are_ACEs()
    {
        Assert.Equal(13, AceWireWriters.PrivateUpdatePropertyInt(0, 0, 0).Length);
        Assert.Equal(17, AceWireWriters.PrivateUpdatePropertyInt64(0, 0, 0).Length);
        Assert.Equal(37, AceWireWriters.PrivateUpdateSkill(0, default).Length);
        Assert.Equal(21, AceWireWriters.PrivateUpdateAttribute(0, 0, 0, 0, 0).Length);
        Assert.Equal(25, AceWireWriters.PrivateUpdateVital(0, 0, 0, 0, 0, 0).Length);
        Assert.Equal(13, AceWireWriters.PrivateUpdateAttribute2ndLevel(0, 0, 0).Length);
    }

    private sealed class Recorder : IInboundMessageHandler
    {
        public PrivateUpdateMsg Update { get; private set; }

        public string Malformed { get; private set; } = string.Empty;

        public void OnPrivateUpdate(in InboundContext context, in PrivateUpdateMsg message) => Update = message;

        public void OnMalformed(in InboundContext context, uint opcode, int length, string reason) => Malformed = reason;
    }
}

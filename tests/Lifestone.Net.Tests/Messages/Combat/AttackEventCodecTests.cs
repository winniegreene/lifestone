using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages.Combat;

/// <summary>
/// GameEvents 0x01A7 AttackDone {u32 WeenieError} (<c>GameEventAttackDone.cs</c>) and 0x01B8 CombatCommenceAttack (empty,
/// <c>GameEventCombatCommenceAttack.cs</c>) as ACE writes them: the whole message is read, a short or long body is refused
/// without throwing, and the dispatcher routes each to its own callback.
/// </summary>
public sealed class AttackEventCodecTests
{
    private const uint Player = 0x50000001;

    private static readonly InboundContext Context = new(3, MessageQueue.Ui, 99);

    [Theory]
    [InlineData(0u)]
    [InlineData(0x0036u)]
    [InlineData(0x0550u)]
    public void AttackDone_reads_the_weenie_error(uint error)
    {
        var body = AceWireWriters.GameEvent(Player, 5, 0x01A7, BitConverter.GetBytes(error));

        Assert.True(AttackEventCodecs.TryParseAttackDone(body, out var done));
        Assert.Equal(new AttackDoneEvent(error), done);
    }

    [Fact]
    public void AttackDone_matches_the_hand_built_bytes()
    {
        var body = AceWireWriters.GameEvent(Player, 5, 0x01A7, [0x36, 0, 0, 0]);

        Assert.Equal(Convert.FromHexString("B0F70000" + "01000050" + "05000000" + "A7010000" + "36000000"), body);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void AttackDone_with_a_short_or_long_body_is_refused_and_never_throws(int payload)
    {
        var body = AceWireWriters.GameEvent(Player, 5, 0x01A7, new byte[payload]);

        Assert.False(AttackEventCodecs.TryParseAttackDone(body, out var done));
        Assert.Equal(default, done);
        Assert.False(AttackEventCodecs.TryParseAttackDone(body[..8], out _));
    }

    [Fact]
    public void CommenceAttack_is_the_envelope_alone()
    {
        Assert.True(AttackEventCodecs.TryParseCommenceAttack(AceWireWriters.GameEvent(Player, 6, 0x01B8, [])));
        Assert.False(AttackEventCodecs.TryParseCommenceAttack(AceWireWriters.GameEvent(Player, 6, 0x01B8, [0])));
        Assert.False(AttackEventCodecs.TryParseCommenceAttack(AceWireWriters.GameEvent(Player, 6, 0x01B8, [])[..15]));
    }

    [Fact]
    public void The_dispatcher_reaches_each_handler_with_its_record()
    {
        var handler = new Recorder();

        var done = InboundDispatcher.Dispatch(Context, AceWireWriters.GameEvent(Player, 7, 0x01A7, BitConverter.GetBytes(0x36u)), handler);
        var commence = InboundDispatcher.Dispatch(Context, AceWireWriters.GameEvent(Player, 8, 0x01B8, []), handler);

        Assert.Equal(new DispatchResult(DispatchOutcome.Decoded, MessageOpcodes.GameEvent, 0x01A7), done);
        Assert.Equal(new DispatchResult(DispatchOutcome.Decoded, MessageOpcodes.GameEvent, 0x01B8), commence);
        Assert.Equal(["done:54:7", "commence:8"], handler.Calls);
    }

    [Fact]
    public void A_bad_body_is_malformed_with_the_message_named_and_reaches_no_callback()
    {
        var handler = new Recorder();

        var short1 = InboundDispatcher.Dispatch(Context, AceWireWriters.GameEvent(Player, 7, 0x01A7, [0, 0]), handler);
        var long1 = InboundDispatcher.Dispatch(Context, AceWireWriters.GameEvent(Player, 8, 0x01B8, [1]), handler);

        Assert.Equal(DispatchOutcome.Malformed, short1.Outcome);
        Assert.Equal(DispatchOutcome.Malformed, long1.Outcome);
        Assert.Equal(2, handler.Malformed.Count);
        Assert.StartsWith("F7B0 GameEvent 0x01A7 ", handler.Malformed[0], StringComparison.Ordinal);
        Assert.StartsWith("F7B0 GameEvent 0x01B8 ", handler.Malformed[1], StringComparison.Ordinal);
        Assert.Empty(handler.Calls);
    }

    private sealed class Recorder : IInboundMessageHandler
    {
        public List<string> Calls { get; } = [];

        public List<string> Malformed { get; } = [];

        public void OnAttackDone(in InboundContext context, in GameEventHeader header, in AttackDoneEvent message) =>
            Calls.Add($"done:{message.WeenieError}:{header.Sequence}");

        public void OnCommenceAttack(in InboundContext context, in GameEventHeader header, in CommenceAttackEvent message) =>
            Calls.Add($"commence:{header.Sequence}");

        public void OnMalformed(in InboundContext context, uint opcode, int length, string reason) => Malformed.Add(reason);
    }
}

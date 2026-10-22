using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages.Inventory;

/// <summary>
/// GameEvents 0x0196 ViewContents (<c>GameEventViewContents.cs:13-26</c>: u32 container, u32 count, count x {u32 item, u32
/// ContainerType}) and 0x0052 CloseGroundContainer (<c>GameEventCloseGroundContainer.cs:9-13</c>: u32 container) as ACE writes
/// them: the whole message is read, a count beyond the body or a trailing byte is refused without throwing, and the dispatcher
/// routes each to its own callback.
/// </summary>
public sealed class ContainerCodecTests
{
    private const uint Player = 0x50000001;
    private const uint Chest = 0x7A9B4016;

    private static readonly InboundContext Context = new(3, MessageQueue.Ui, 99);

    [Fact]
    public void ViewContents_with_no_entries_is_a_container_and_a_zero_count()
    {
        var body = Message(0x0196, Chest, 0);

        Assert.True(ContainerCodecs.TryParseViewContents(body, out var view));
        Assert.Equal(Chest, view.Container);
        Assert.Empty(view.Entries);
    }

    [Fact]
    public void ViewContents_with_one_entry()
    {
        Assert.True(ContainerCodecs.TryParseViewContents(Message(0x0196, Chest, 1, 0x80000011, 0), out var view));

        Assert.Equal([new ViewContentsEntry(0x80000011, 0)], view.Entries);
    }

    [Fact]
    public void ViewContents_with_three_entries_keeps_the_order_and_the_container_type_including_foci()
    {
        var body = Message(0x0196, Chest, 3, 0x80000011, 0, 0x80000012, 1, 0x80000013, 2);

        Assert.True(ContainerCodecs.TryParseViewContents(body, out var view));
        Assert.Equal(Chest, view.Container);
        Assert.Equal([new ViewContentsEntry(0x80000011, 0), new ViewContentsEntry(0x80000012, 1), new ViewContentsEntry(0x80000013, 2)], view.Entries);
    }

    [Fact]
    public void ViewContents_matches_the_hand_built_bytes_after_the_event_envelope()
    {
        // F7B0 {player, sequence 5, type 0x0196} then ACE's writer: container, count, {item, type}.
        var expected = Convert.FromHexString(
            "B0F70000" + "01000050" + "05000000" + "96010000" + "16409B7A" + "01000000" + "11000080" + "02000000");

        Assert.Equal(expected, AceWireWriters.GameEvent(Player, 5, 0x0196, Payload(Chest, 1, 0x80000011, 2)));
        Assert.True(ContainerCodecs.TryParseViewContents(expected, out var view));
        Assert.Equal([new ViewContentsEntry(0x80000011, 2)], view.Entries);
    }

    [Fact]
    public void ViewContents_truncated_or_overlong_is_refused_and_never_throws()
    {
        var whole = Message(0x0196, Chest, 2, 0x80000011, 0, 0x80000012, 1);

        // Every strict prefix of the whole message is refused (a short envelope, a short container or count, a short entry).
        for (var length = 0; length < whole.Length; length++)
        {
            Assert.False(ContainerCodecs.TryParseViewContents(whole.AsSpan(0, length), out var view));
            Assert.Null(view.Entries);
        }

        // A trailing byte, and a count beyond the body (three claimed, two present; and a count that would overflow a size).
        Assert.False(ContainerCodecs.TryParseViewContents([.. whole, 0], out _));
        Assert.False(ContainerCodecs.TryParseViewContents(Message(0x0196, Chest, 3, 0x80000011, 0, 0x80000012, 1), out _));
        Assert.False(ContainerCodecs.TryParseViewContents(Message(0x0196, Chest, uint.MaxValue), out _));
    }

    [Fact]
    public void CloseGroundContainer_is_one_guid()
    {
        Assert.True(ContainerCodecs.TryParseCloseGroundContainer(Message(0x0052, Chest), out var close));
        Assert.Equal(new CloseGroundContainerEvent(Chest), close);
    }

    [Fact]
    public void CloseGroundContainer_short_or_long_is_refused_and_never_throws()
    {
        var whole = Message(0x0052, Chest);

        for (var length = 0; length < whole.Length; length++)
        {
            Assert.False(ContainerCodecs.TryParseCloseGroundContainer(whole.AsSpan(0, length), out _));
        }

        Assert.False(ContainerCodecs.TryParseCloseGroundContainer([.. whole, 0], out _));
    }

    [Fact]
    public void The_dispatcher_reaches_each_handler_with_its_record()
    {
        var handler = new Recorder();

        var view = InboundDispatcher.Dispatch(Context, Message(0x0196, Chest, 2, 0x80000011, 1, 0x80000012, 2), handler);
        var close = InboundDispatcher.Dispatch(Context, Message(0x0052, Chest), handler);

        Assert.Equal(new DispatchResult(DispatchOutcome.Decoded, MessageOpcodes.GameEvent, 0x0196), view);
        Assert.Equal(new DispatchResult(DispatchOutcome.Decoded, MessageOpcodes.GameEvent, 0x0052), close);
        Assert.Equal(["view:7A9B4016:80000011/1,80000012/2:seq7", "close:7A9B4016:seq7"], handler.Calls);
    }

    [Fact]
    public void A_bad_body_is_malformed_with_the_message_named_and_reaches_no_callback()
    {
        var handler = new Recorder();

        var view = InboundDispatcher.Dispatch(Context, Message(0x0196, Chest, 2, 0x80000011, 1), handler);
        var close = InboundDispatcher.Dispatch(Context, Message(0x0052, Chest, 1), handler);

        Assert.Equal(DispatchOutcome.Malformed, view.Outcome);
        Assert.Equal(DispatchOutcome.Malformed, close.Outcome);
        Assert.Equal(2, handler.Malformed.Count);
        Assert.StartsWith("F7B0 GameEvent 0x0196 ", handler.Malformed[0], StringComparison.Ordinal);
        Assert.StartsWith("F7B0 GameEvent 0x0052 ", handler.Malformed[1], StringComparison.Ordinal);
        Assert.Empty(handler.Calls);
    }

    private static byte[] Message(uint type, params uint[] fields) => AceWireWriters.GameEvent(Player, 7, type, Payload(fields));

    private static byte[] Payload(params uint[] fields) => [.. fields.SelectMany(BitConverter.GetBytes)];

    private sealed class Recorder : IInboundMessageHandler
    {
        public List<string> Calls { get; } = [];

        public List<string> Malformed { get; } = [];

        public void OnViewContents(in InboundContext context, in GameEventHeader header, in ViewContentsEvent message) =>
            Calls.Add($"view:{message.Container:X8}:{string.Join(',', message.Entries.Select(e => $"{e.Guid:X8}/{e.ContainerType}"))}:seq{header.Sequence}");

        public void OnCloseGroundContainer(in InboundContext context, in GameEventHeader header, in CloseGroundContainerEvent message) =>
            Calls.Add($"close:{message.Container:X8}:seq{header.Sequence}");

        public void OnMalformed(in InboundContext context, uint opcode, int length, string reason) => Malformed.Add(reason);
    }
}

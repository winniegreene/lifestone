using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The reducer after M3 1.2 s0: each message no lane applies yet (the admin ones; lane s1 took the magic ones, s2 the inventory ones, s3 the combat and death ones) is taken as "decoded, not
/// yet applied" (<see cref="ReducerCounters.DecodedNotApplied"/>, per message), is never reported as an
/// <see cref="UnknownMessage"/> or a dropped message, and changes no state.
/// </summary>
public sealed class ReducerM3HooksTests
{
    private const uint Player = ReducerHarness.PlayerGuid;

    /// <summary>One ACE body per code: the GameMessage opcode, or the event type with <see cref="MessageOpcodes.GameEvent"/>. A lane that applies a message removes its row here.</summary>
    public static TheoryData<uint, bool, byte[]> Bodies => new()
    {
        // Empty since M3 1.3 s2b applied the last one (AdminEnvirons, 0xEA60): the reducer's "decoded, not yet applied" total reads 0.
    };

    [Fact]
    public void The_whole_set_counts_each_remaining_message_once_and_none_unknown_and_the_set_is_empty()
    {
        var rig = new ReducerHarness();

        foreach (var row in Bodies)
        {
            InboundDispatcher.Dispatch(rig.Context, (byte[])row[2]!, rig.Reducer);
        }

        Assert.Empty(Bodies);
        Assert.Equal(Bodies.Count, rig.Reducer.Counters.DecodedNotApplied);
        Assert.Equal(Bodies.Count, rig.Reducer.Counters.DecodedNotAppliedByMessage.Count);
        Assert.All(rig.Reducer.Counters.DecodedNotAppliedByMessage.Values, count => Assert.Equal(1, count));
        Assert.Empty(rig.Of<UnknownMessage>());
        Assert.Empty(rig.Of<MessageDropped>());
    }

    [Fact]
    public void A_game_event_the_set_does_not_cover_is_still_unknown_and_not_counted()
    {
        var rig = new ReducerHarness();

        InboundDispatcher.Dispatch(rig.Context, AceWireWriters.GameEvent(Player, 1, 0x0314, [0, 0, 0, 0]), rig.Reducer);

        Assert.Equal(0, rig.Reducer.Counters.DecodedNotApplied);
        Assert.Equal(0x0314u, Assert.Single(rig.Of<UnknownMessage>()).EventType);
    }
}

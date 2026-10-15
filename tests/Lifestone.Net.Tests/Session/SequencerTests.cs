using System.Buffers.Binary;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.FakeAce;
using Lifestone.Net.Tests.Support;
using Lifestone.Net.Transport;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// Inbound delivery order (m2-plan §3.3 N5, D18): globally by fragment sequence, with the gap-skip rule, or in arrival
/// order for <c>--retail-net</c>. The <see cref="InboundSequencer"/> alone, then the session on the fake server.
/// </summary>
public sealed class SequencerTests
{
    [Fact]
    public void In_order_messages_go_straight_through_and_later_ones_wait_for_the_gap()
    {
        var sequencer = new InboundSequencer(InboundOrder.FragmentSequence, new ManualTimeProvider());

        Assert.Equal(SequencerVerdict.InOrder, sequencer.Classify(0));
        sequencer.Advance();
        Assert.Equal(SequencerVerdict.Wait, sequencer.Classify(2));
        sequencer.Hold(2, MessageQueue.Ui, 7, [2, 0, 0, 0], owned: null);
        Assert.True(sequencer.HasGap);
        Assert.False(sequencer.HasReady);
        Assert.False(sequencer.TryTakeReady(out _));

        Assert.Equal(SequencerVerdict.InOrder, sequencer.Classify(1));
        sequencer.Advance();
        Assert.False(sequencer.HasGap);
        Assert.True(sequencer.TryTakeReady(out var waiting));
        Assert.Equal((2u, MessageQueue.Ui, 7L), (waiting.FragmentSequence, waiting.Queue, waiting.ReceivedTimestamp));
        Assert.Equal(new byte[] { 2, 0, 0, 0 }, waiting.Body.ToArray());
        InboundSequencer.Release(waiting);
        Assert.Equal(3u, sequencer.Next);
    }

    [Fact]
    public void Old_and_repeated_messages_are_dropped_as_duplicates()
    {
        var sequencer = new InboundSequencer(InboundOrder.FragmentSequence, new ManualTimeProvider());
        sequencer.Advance();
        sequencer.Hold(3, MessageQueue.Ui, 0, [3], owned: null);

        Assert.Equal(SequencerVerdict.Duplicate, sequencer.Classify(0));
        Assert.Equal(SequencerVerdict.Duplicate, sequencer.Classify(3));
        sequencer.Hold(3, MessageQueue.Ui, 0, [3], owned: null);
        sequencer.Hold(0, MessageQueue.Ui, 0, [0], owned: null);
        Assert.Equal(2, sequencer.DuplicatesDropped);
        Assert.Equal(1, sequencer.WaitingCount);
    }

    [Fact]
    public void A_held_reassembled_message_keeps_its_own_array()
    {
        var sequencer = new InboundSequencer(InboundOrder.FragmentSequence, new ManualTimeProvider());
        byte[] owned = [1, 2, 3, 4, 5];
        sequencer.Hold(0, MessageQueue.SmartBox, 0, owned, owned);
        Assert.True(sequencer.TryTakeReady(out var waiting));
        Assert.Same(owned, waiting.Buffer);
        Assert.False(waiting.Pooled);
    }

    [Fact]
    public void A_gap_is_skipped_after_a_reject_once_no_nak_or_partial_message_is_outstanding()
    {
        var time = new ManualTimeProvider();
        var sequencer = new InboundSequencer(InboundOrder.FragmentSequence, time);
        sequencer.Hold(3, MessageQueue.Ui, 0, [3], owned: null);
        sequencer.Hold(4, MessageQueue.Ui, 0, [4], owned: null);

        Assert.False(sequencer.TrySkipGap(naksOutstanding: 0, partialMessages: 0, out _, out _));
        sequencer.OnRejectRetransmit();
        Assert.False(sequencer.TrySkipGap(naksOutstanding: 1, partialMessages: 0, out _, out _));
        Assert.False(sequencer.TrySkipGap(naksOutstanding: 0, partialMessages: 1, out _, out _));
        Assert.True(sequencer.TrySkipGap(naksOutstanding: 0, partialMessages: 0, out var from, out var to));

        Assert.Equal((0u, 2u), (from, to));
        Assert.Equal(1, sequencer.GapSkips);
        Assert.Equal(2, sequencer.ReadyCount);
        Assert.False(sequencer.HasGap);
    }

    [Fact]
    public void A_reject_before_the_gap_opened_does_not_count()
    {
        var sequencer = new InboundSequencer(InboundOrder.FragmentSequence, new ManualTimeProvider());
        sequencer.OnRejectRetransmit();
        sequencer.Hold(1, MessageQueue.Ui, 0, [1], owned: null);
        Assert.False(sequencer.TrySkipGap(0, 0, out _, out _));
    }

    [Fact]
    public void Without_a_reject_a_gap_is_skipped_only_at_five_seconds()
    {
        var time = new ManualTimeProvider();
        var sequencer = new InboundSequencer(InboundOrder.FragmentSequence, time);
        sequencer.Hold(1, MessageQueue.Ui, 0, [1], owned: null);

        time.Advance(TimeSpan.FromSeconds(4));
        Assert.False(sequencer.TrySkipGap(0, 0, out _, out _));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(sequencer.TrySkipGap(0, 0, out _, out _));
        Assert.Equal(1u, sequencer.Next);
    }

    [Fact]
    public void Arrival_order_delivers_as_messages_complete_and_queues_behind_a_spent_budget()
    {
        var sequencer = new InboundSequencer(InboundOrder.Arrival, new ManualTimeProvider());
        Assert.Equal(SequencerVerdict.InOrder, sequencer.Classify(5));
        sequencer.Hold(5, MessageQueue.Ui, 0, [5], owned: null);
        sequencer.Hold(2, MessageQueue.Ui, 0, [2], owned: null);
        Assert.Equal(SequencerVerdict.Wait, sequencer.Classify(9));
        Assert.False(sequencer.HasGap);
        Assert.False(sequencer.TrySkipGap(0, 0, out _, out _));

        Assert.True(sequencer.TryTakeReady(out var first));
        Assert.True(sequencer.TryTakeReady(out var second));
        Assert.Equal((5u, 2u), (first.FragmentSequence, second.FragmentSequence));
    }

    [Fact]
    public void Reordered_and_duplicated_server_packets_are_delivered_once_in_fragment_order()
    {
        var script = SessionRig.DefaultScript();
        AddCharacters(script, 30);
        using var rig = new SessionRig(script, serverToClient: new LossModel(seed: 7, dropPercent: 5, swapPercent: 20, duplicatePercent: 10));
        rig.ToCharacterSelect();
        rig.Session.DeleteCharacter(0);
        Assert.True(rig.RunUntil(() => rig.Sink.CharacterLists.Count == 2, TimeSpan.FromSeconds(5)));

        var sequences = rig.Sink.Dispatched.Select(d => d.Sequence).ToList();
        Assert.Equal(Enumerable.Range(0, sequences.Count).Select(i => (uint)i), sequences);
        Assert.Equal(31, rig.Sink.CharacterLists[^1].Characters.Length);
        Assert.Equal(0, rig.Session.Link.GapSkips);
    }

    [Fact]
    public void A_list_sent_tail_first_is_reassembled_and_delivered_after_the_acknowledgement()
    {
        var script = SessionRig.DefaultScript();
        AddCharacters(script, 30);
        using var rig = new SessionRig(script);
        var tailFirst = false;
        rig.Harness.Network.Drop = (from, to, datagram) =>
        {
            if (from.Port is 9000 or 9001)
            {
                var fragments = ServerDatagram.Decode(datagram, from.Port).Fragments;
                tailFirst |= fragments.Count >= 2 && fragments[1].Count > 1 && fragments[1].Index == fragments[1].Count - 1;
            }

            return false;
        };
        rig.ToCharacterSelect();
        rig.Session.DeleteCharacter(0);
        Assert.True(rig.RunUntil(() => rig.Sink.CharacterLists.Count == 2, TimeSpan.FromSeconds(5)));

        Assert.True(tailFirst, "the fake sent no tail fragment first");
        Assert.Equal(new[] { MessageOpcodes.CharacterDelete, MessageOpcodes.CharacterList }, rig.Sink.Opcodes.TakeLast(2));
        Assert.Equal(1u, rig.Sink.CharacterLists[^1].Characters.Single(c => c.Name == "Character 29").DeleteTimeout);
    }

    [Fact]
    public void A_rejected_packet_leads_to_exactly_one_gap_skip()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        var armed = false;
        rig.Harness.Network.Drop = (from, to, datagram) =>
        {
            if (!armed || from.Port is not (9000 or 9001) || !LoginFailureTests.ServerOpcodes(datagram, from.Port).Contains(MessageOpcodes.ServerMessage))
            {
                return false;
            }

            armed = false;
            rig.ServerSession.ForgetCachedPackets();  // as if it had aged out: the NAK will be refused
            return true;
        };

        armed = true;
        rig.ServerSession.EnqueueSend((AceWireWriters.ServerMessage("lost for good", 0), FakeAceSession.UiQueue));
        rig.Harness.Step();
        rig.ServerSession.EnqueueSend((AceWireWriters.ServerMessage("after the gap", 0), FakeAceSession.UiQueue));
        Assert.True(rig.RunUntil(() => rig.Sink.Opcodes.Contains(MessageOpcodes.ServerMessage), TimeSpan.FromSeconds(3)));

        Assert.Equal(1, rig.Session.Link.GapSkips);
        Assert.Equal(1, rig.Sink.Opcodes.Count(o => o == MessageOpcodes.ServerMessage));
        Assert.True(rig.Server.Counters.S2CRejects >= 1);
        Assert.Single(rig.Sink.OfKind(SessionEventKind.Warning), e => e.Warning == SessionWarning.GapSkipped);
        Assert.Single(rig.Log.At(NetLogLevel.Error), l => l.StartsWith("inbound: gave up on server messages", StringComparison.Ordinal));
        Assert.Equal(SessionPhase.CharacterSelect, rig.Session.Phase);
    }

    [Fact]
    public void Without_a_reject_a_four_second_gap_is_waited_out_not_skipped()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        uint? lostSequence = null;
        var holdUntil = TimeSpan.Zero;
        rig.Harness.Network.Drop = (from, to, datagram) =>
        {
            if (from.Port is not (9000 or 9001))
            {
                return false;
            }

            var sequence = BinaryPrimitives.ReadUInt32LittleEndian(datagram);
            if (lostSequence is null && LoginFailureTests.ServerOpcodes(datagram, from.Port).Contains(MessageOpcodes.ServerMessage))
            {
                lostSequence = sequence;
                holdUntil = rig.Harness.Time.Elapsed + TimeSpan.FromSeconds(4);
            }

            // The packet and every resend of it are lost for 4 s.
            return sequence == lostSequence && rig.Harness.Time.Elapsed < holdUntil;
        };

        rig.ServerSession.EnqueueSend((AceWireWriters.ServerMessage("late", 0), FakeAceSession.UiQueue));
        rig.Harness.Step();
        rig.ServerSession.EnqueueSend((AceWireWriters.ServerMessage("behind", 0), FakeAceSession.UiQueue));
        rig.RunFor(TimeSpan.FromSeconds(3.9));
        Assert.DoesNotContain(MessageOpcodes.ServerMessage, rig.Sink.Opcodes);

        Assert.True(rig.RunUntil(() => rig.Sink.Opcodes.Count(o => o == MessageOpcodes.ServerMessage) == 2, TimeSpan.FromSeconds(2)));
        Assert.Equal(0, rig.Session.Link.GapSkips);
        Assert.True(rig.Server.Counters.S2CRetransmits >= 2, "the server should have been asked more than once");
    }

    private static void AddCharacters(FakeAceScript script, int count)
    {
        var account = script.Accounts[SessionRig.Account];
        for (var i = 0; i < count; i++)
        {
            account.Characters.Add(new FakeCharacter(0x50000100u + (uint)i, "Character " + i) { LastLogin = DateTimeOffset.UnixEpoch.AddDays(i + 1) });
        }
    }
}

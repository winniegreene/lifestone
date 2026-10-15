using System.Buffers.Binary;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.Support;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.FakeAce;

/// <summary>
/// The fake server behaves as ACE does on the points docs/protocol.md §2 and §4 and m2-plan §3.3 N4 name, driven by a
/// hand-built client (<see cref="RawAceClient"/>) in virtual time: the handshake race, the first data sequence, gap
/// handling and NAKs, the 257 gap, the RejectRetransmit sequence quirk, a duplicate costing a CRC error, and two silent
/// seconds after a termination. The N5 session tests then rely on it.
/// </summary>
public sealed class FakeAceConformanceTests
{
    private const string Account = "tester";
    private const string Password = "test-password-1";

    [Fact]
    public void A_connect_response_sent_before_the_database_write_is_ignored_and_a_later_one_connects()
    {
        var harness = Harness(dbWriteDelay: TimeSpan.FromMilliseconds(100));
        using var client = new RawAceClient(harness);

        client.SendLoginRequest(Account, Password);
        harness.Step();
        client.Drain();
        Assert.NotNull(client.ConnectRequestPacket);
        Assert.Equal(0u, client.ConnectRequestPacket!.Sequence);
        Assert.Equal(harness.ServerEndPoint.Port, client.ConnectRequestPacket.SourcePort);

        client.SendConnectResponse();
        harness.Step();
        client.Drain();
        Assert.Equal(1, harness.Server.Counters.ConnectResponsesIgnored);
        Assert.Equal(FakeAceSessionState.AuthLoginRequest, harness.Server.Sessions.Single().State);
        Assert.DoesNotContain(client.Received, d => d.Has(RawAceClient.EncryptedChecksum));

        harness.RunFor(TimeSpan.FromMilliseconds(100));
        client.SendConnectResponse();
        Assert.True(harness.RunUntil(() => Drained(client).Opcodes.Contains(MessageOpcodes.CharacterList), TimeSpan.FromSeconds(1)));

        var session = harness.Server.Sessions.Single();
        Assert.Equal(FakeAceSessionState.AuthConnected, session.State);
        Assert.Equal(harness.Server.WorldEndPoint.Port, client.Received.Last().SourcePort);
    }

    [Fact]
    public void The_handshake_sequences_follow_ace_connect_request_0_then_encrypted_from_2()
    {
        var (_, client) = Connected();

        var encrypted = client.Received.Where(d => d.Has(RawAceClient.EncryptedChecksum)).ToList();
        Assert.Equal(2u, encrypted.First().Sequence);
        Assert.True(encrypted.First().TimeSync.HasValue, "the first encrypted packet carries the time sync");
        Assert.All(client.Received, d => Assert.Equal(FakeAceServer.ServerId, d.Id));
        Assert.All(client.Received, d => Assert.Equal(1, d.Iteration));
        client.AssertServerKeysFollowTheKeystream();

        // One fragment counter from 0, groups in order: the Database queue's F7E5 goes before the UI queue's F658 and
        // F7E1 (one bundle per 5 ms, so the time sync, F7E5 and the UI bundle leave in three packets).
        Assert.Equal(new uint[] { 0, 1, 2 }, client.MessagesBySequence.Keys.Order());
        Assert.Equal(new[] { MessageOpcodes.DddInterrogation, MessageOpcodes.CharacterList, MessageOpcodes.ServerName }, client.Opcodes);
    }

    [Fact]
    public void The_first_data_packet_must_be_2_and_a_1_is_dropped_as_already_seen()
    {
        var (harness, client) = Connected();
        var session = harness.Server.Sessions.Single();

        client.Send(client.BuildData(1, Ddd()));
        harness.Step();
        Assert.Equal(1, harness.Server.Counters.DuplicatesDropped);
        Assert.Empty(session.HandledMessages);

        // The dropped packet spent its key (the check comes first) but not its fragment sequence: packet 2 carries the
        // next key and message 1 again.
        client.FragmentSequence = 0;
        client.Send(client.BuildData(2, Ddd()));
        harness.Step();
        Assert.Equal(MessageOpcodes.DddInterrogationResponse, BinaryPrimitives.ReadUInt32LittleEndian(Assert.Single(session.HandledMessages)));
        Assert.Equal(2u, session.LastReceivedPacketSequence);
    }

    [Fact]
    public void A_gap_of_one_is_buffered_without_a_nak_and_handled_in_order_when_filled()
    {
        var (harness, client) = Connected();
        var session = harness.Server.Sessions.Single();
        var second = client.BuildData(2, CharacterError());
        var third = client.BuildData(3, Ddd());

        client.Send(third);
        harness.RunFor(TimeSpan.FromMilliseconds(50));
        client.Drain();
        Assert.Equal(1, harness.Server.Counters.OutOfOrderBuffered);
        Assert.Equal(0, harness.Server.Counters.S2CRequestsForRetransmit);
        Assert.DoesNotContain(client.Received, d => d.Has(RawAceClient.RequestRetransmit));
        Assert.Empty(session.HandledMessages);

        client.Send(second);
        harness.Step();
        Assert.Equal(3u, session.LastReceivedPacketSequence);
        Assert.Equal(2u, session.LastReceivedFragmentSequence);
    }

    [Fact]
    public void A_gap_of_two_is_naked_cleartext_with_the_current_sequence_at_most_once_a_second()
    {
        var (harness, client) = Connected();
        var packets = Enumerable.Range(2, 4).Select(s => client.BuildData((uint)s, Ddd())).ToArray();  // 2, 3, 4, 5
        var before = client.Received.Count;

        client.Send(packets[2]);  // 4: two ahead of the expected 2
        harness.Step();
        client.Send(packets[3]);  // 5: still missing 2 and 3, but inside the second
        harness.Step();
        client.Drain();

        var naks = client.Received.Skip(before).Where(d => d.Has(RawAceClient.RequestRetransmit)).ToList();
        var nak = Assert.Single(naks);
        Assert.Equal(new uint[] { 2, 3 }, nak.RetransmitRequests);
        Assert.False(nak.Has(RawAceClient.EncryptedChecksum));
        Assert.Equal(harness.Server.Sessions.Single().CurrentPacketSequence, nak.Sequence);
        Assert.Equal(1, harness.Server.Counters.S2CRequestsForRetransmit);

        // A second later the next packet past the gap brings a second NAK. (Resending packet 5 would not: its key is
        // spent, so it would only cost a CRC error.)
        harness.RunFor(TimeSpan.FromSeconds(1.1));
        client.Send(client.BuildData(6, Ddd()));
        harness.Step();
        client.Drain();
        Assert.Equal(2, client.Received.Skip(before).Count(d => d.Has(RawAceClient.RequestRetransmit)));
    }

    [Fact]
    public void A_packet_more_than_256_past_the_gap_terminates_the_session_and_256_does_not()
    {
        var (harness, client) = Connected();
        var session = harness.Server.Sessions.Single();

        // Cleartext ack-only packets reach the sequence check (an encrypted one that far ahead would fail its key first).
        client.Send(client.BuildAckOnly(1 + 2 + 256, 2));  // received − (expected + 1) = 256: NAKed
        harness.Step();
        Assert.Null(session.TerminationReason);
        Assert.Equal(1, harness.Server.Counters.S2CRequestsForRetransmit);

        harness.RunFor(TimeSpan.FromSeconds(1.1));
        client.Send(client.BuildAckOnly(1 + 2 + 257, 2));  // 257: AbnormalSequenceReceived
        harness.Step();
        Assert.Equal(FakeAceTermination.AbnormalSequenceReceived, session.TerminationReason);
    }

    [Fact]
    public void A_reject_retransmit_is_cleartext_and_consumes_a_sequence_but_no_key()
    {
        var (harness, client) = Connected();
        var session = harness.Server.Sessions.Single();
        var current = session.CurrentPacketSequence;

        client.Send(client.BuildNak(1, 9_999));  // never sent: not cached
        harness.Step();
        client.Drain();

        var reject = Assert.Single(client.Received, d => d.Has(RawAceClient.RejectRetransmit));
        Assert.Equal(new uint[] { 9_999 }, reject.RejectedSequences);
        Assert.False(reject.Has(RawAceClient.EncryptedChecksum));
        Assert.Equal(current + 1, reject.Sequence);
        Assert.Equal(1, harness.Server.Counters.S2CRejects);

        // The next encrypted packet takes the sequence after the reject and the key after the last encrypted packet.
        client.Send(client.BuildData(2, Ddd()));
        Assert.True(harness.RunUntil(() => Drained(client).Opcodes.Contains(MessageOpcodes.DddEnd), TimeSpan.FromSeconds(1)));
        var next = client.Received.Where(d => d.Has(RawAceClient.EncryptedChecksum)).Last();
        Assert.Equal(current + 2, next.Sequence);
        client.AssertServerKeysFollowTheKeystream();
    }

    [Fact]
    public void A_cached_packet_is_resent_with_its_key_and_the_retransmission_flag()
    {
        var (harness, client) = Connected();
        var original = client.Received.First(d => d.Has(RawAceClient.EncryptedChecksum));

        client.Send(client.BuildNak(1, original.Sequence));
        harness.Step();
        client.Drain();

        var resent = client.Received.Last();
        Assert.Equal(original.Sequence, resent.Sequence);
        Assert.True(resent.Has(0x1), "Retransmission is set");
        Assert.Equal(original.RecoveredKey, resent.RecoveredKey);
        Assert.Equal(1, harness.Server.Counters.S2CRetransmits);
        Assert.Equal(1, harness.Server.Counters.C2SRequestsForRetransmit);
    }

    [Fact]
    public void A_duplicate_encrypted_packet_counts_one_crc_error_and_is_handled_once()
    {
        var (harness, client) = Connected();
        var session = harness.Server.Sessions.Single();
        var packet = client.BuildData(2, Ddd());

        client.Send(packet);
        harness.Step();
        client.Send(packet);
        harness.Step();

        Assert.Equal(1, harness.Server.Counters.C2SCrcErrors);
        Assert.Equal(0, harness.Server.Counters.DuplicatesDropped);  // the spent key fails before the sequence is looked at
        Assert.Single(session.HandledMessages);

        // The failed search moved the window; the next packet's key is still found.
        client.Send(client.BuildData(3, CharacterError()));
        harness.Step();
        Assert.Equal(3u, session.LastReceivedPacketSequence);
    }

    [Fact]
    public void After_a_termination_the_server_flushes_for_2_seconds_then_goes_silent_and_never_sends_a_disconnect()
    {
        var harness = Harness();
        using var client = new RawAceClient(harness);
        harness.AddParticipant(() => client.Drain());

        client.SendLoginRequest(Account, "wrong-password");
        harness.RunFor(TimeSpan.FromMilliseconds(500));
        var session = Assert.Single(harness.Server.History);
        Assert.Equal(FakeAceTermination.NotAuthorizedPasswordMismatch, session.TerminationReason);

        // The ConnectRequest, then the encrypted boot (sequence 2).
        Assert.Contains(client.Received, d => d.Has(RawAceClient.ConnectRequest));
        Assert.Equal(MessageOpcodes.AccountBoot, Assert.Single(client.Opcodes));

        harness.RunFor(TimeSpan.FromSeconds(1.6));
        Assert.True(session.TerminationComplete);
        Assert.Empty(harness.Server.Sessions);
        var count = client.Received.Count;

        harness.RunFor(TimeSpan.FromSeconds(30));
        Assert.Equal(count, client.Received.Count);
        Assert.DoesNotContain(client.Received, d => d.Has(RawAceClient.Disconnect));

        client.Send(client.BuildAckOnly(2, 2));
        harness.Step();
        Assert.Equal(1, harness.Server.Counters.Unsolicited);
    }

    [Fact]
    public void A_client_disconnect_terminates_without_a_reply()
    {
        var (harness, client) = Connected();
        var session = harness.Server.Sessions.Single();

        client.Send(client.BuildDisconnect());
        harness.RunFor(TimeSpan.FromSeconds(3));

        Assert.Equal(FakeAceTermination.PacketHeaderDisconnect, session.TerminationReason);
        Assert.True(session.TerminationComplete);
    }

    [Fact]
    public void A_login_request_resent_from_the_same_endpoint_while_waiting_for_the_connect_response_is_a_bad_handshake()
    {
        var harness = Harness();
        using var client = new RawAceClient(harness);

        client.SendLoginRequest(Account, Password);
        harness.RunFor(TimeSpan.FromMilliseconds(200));
        client.Drain();
        Assert.Equal(FakeAceSessionState.AuthConnectResponse, harness.Server.Sessions.Single().State);

        client.SendLoginRequest(Account, Password);
        harness.Step();
        Assert.Equal(1, harness.Server.Counters.BadHandshakes);
        Assert.Empty(harness.Server.Sessions);

        client.SendConnectResponse();
        harness.Step();
        Assert.Equal(1, harness.Server.Counters.ConnectResponsesIgnored);
    }

    [Fact]
    public void A_connect_response_with_the_wrong_cookie_is_ignored()
    {
        var harness = Harness();
        using var client = new RawAceClient(harness);
        client.SendLoginRequest(Account, Password);
        harness.RunFor(TimeSpan.FromMilliseconds(200));
        client.Drain();

        client.SendConnectResponse(client.ConnectRequestPacket!.ConnectRequestCookie ^ 1);
        harness.Step();

        Assert.Equal(1, harness.Server.Counters.ConnectResponsesIgnored);
        Assert.Equal(FakeAceSessionState.AuthConnectResponse, harness.Server.Sessions.Single().State);
    }

    [Fact]
    public void Acks_come_every_2_seconds_and_time_syncs_every_20_with_ack_only_packets_reusing_the_sequence()
    {
        var (harness, client) = Connected();
        var nextClientAck = harness.Time.Elapsed;
        harness.AddParticipant(() =>
        {
            client.Drain();
            if (harness.Time.Elapsed >= nextClientAck)
            {
                // A client acks too; without client traffic ACE times the session out after 15 s here.
                client.Send(client.BuildAckOnly(1, client.Received.Max(d => d.Sequence)));
                nextClientAck += TimeSpan.FromSeconds(2);
            }
        });
        var start = client.Received.Count;

        harness.RunFor(TimeSpan.FromSeconds(41));
        Assert.Null(harness.Server.Sessions.Single().TerminationReason);

        var later = client.Received.Skip(start).ToList();
        var acks = later.Where(d => d.Has(RawAceClient.AckSequence)).ToList();
        Assert.InRange(acks.Count, 19, 21);
        Assert.Equal(2, later.Count(d => d.TimeSync.HasValue));
        var ackOnly = 0;
        var lastSequence = client.Received[start - 1].Sequence;
        foreach (var datagram in later)
        {
            if (datagram.Flags == RawAceClient.AckSequence)
            {
                Assert.Equal(lastSequence, datagram.Sequence);  // an ack-only packet reuses the current sequence
                ackOnly++;
            }
            else
            {
                Assert.Equal(lastSequence + 1, datagram.Sequence);
                lastSequence = datagram.Sequence;
            }
        }

        Assert.InRange(ackOnly, 17, 21);
        Assert.All(acks, d => Assert.Equal(1u, d.AckSequence));  // the client sent no data packet yet
    }

    [Fact]
    public void A_data_check_with_matching_iterations_ends_with_F7EA_and_a_mismatch_boots()
    {
        var (harness, client) = Connected();
        client.Send(client.BuildData(2, Ddd()));
        Assert.True(harness.RunUntil(() => Drained(client).Opcodes.Contains(MessageOpcodes.DddEnd), TimeSpan.FromSeconds(1)));

        var (harness2, client2) = Connected();
        client2.Send(client2.BuildData(2, Ddd(new DddIterations(2071, 982, 994, null))));
        Assert.True(harness2.RunUntil(() => Drained(client2).Opcodes.Contains(MessageOpcodes.AccountBoot), TimeSpan.FromSeconds(1)));
        Assert.Equal(FakeAceTermination.DatsPatchingDisabled, harness2.Server.History.Single().TerminationReason);
    }

    [Fact]
    public void The_crypto_oracle_keeps_skipped_keys_and_shifts_its_window_on_a_spent_key()
    {
        const uint seed = 0x0BADF00D;
        var keys = new Lifestone.Net.Crypto.IsaacRandom(BitConverter.GetBytes(seed));
        var k = Enumerable.Range(0, 300).Select(_ => keys.Next()).ToArray();
        Assert.Equal(0x99D25AF6u, k[0]);  // docs/protocol.md §10

        var window = new AceCryptoSystemOracle(seed);
        Assert.True(window.Search(k[2]));  // two lost: both kept as skipped
        window.ConsumeKey(k[2]);
        Assert.Equal(2, window.SkippedKeyCount);
        Assert.True(window.Search(k[0]));
        window.ConsumeKey(k[0]);
        Assert.Equal(1, window.SkippedKeyCount);

        Assert.False(window.Search(k[0]));  // spent: the failed search passes 255 more keys
        Assert.Equal(256, window.SkippedKeyCount);
        Assert.True(window.Search(k[1]));
        Assert.False(window.Search(k[299]));  // the window is full: nothing further is searched
    }

    private static SessionHarness Harness(TimeSpan? dbWriteDelay = null)
    {
        var script = new FakeAceScript { DbWriteDelay = dbWriteDelay ?? TimeSpan.FromMilliseconds(50) };
        script.AddAccount(Account, Password);
        return new SessionHarness(script);
    }

    /// <summary>A client past the handshake in character select, with the first server messages drained.</summary>
    private static (SessionHarness Harness, RawAceClient Client) Connected()
    {
        var harness = Harness();
        var client = new RawAceClient(harness);
        client.SendLoginRequest(Account, Password);
        harness.RunFor(TimeSpan.FromMilliseconds(100));
        client.Drain();
        client.SendConnectResponse();
        Assert.True(harness.RunUntil(() => Drained(client).Opcodes.Count >= 3, TimeSpan.FromSeconds(1)));
        return (harness, client);
    }

    private static RawAceClient Drained(RawAceClient client)
    {
        client.Drain();
        return client;
    }

    private static byte[] Ddd(DddIterations? iterations = null)
    {
        var writer = new PacketWriter();
        DddCodec.WriteInterrogationResponse(writer, iterations ?? new DddIterations(2072, 982, 994, null), serverAllowsHighRes: false);
        return writer.ToArray();
    }

    /// <summary>A message no handler takes in character select (counts as unhandled, still advances the fragment sequence).</summary>
    private static byte[] CharacterError() => AceWireWriters.CharacterError(1);
}

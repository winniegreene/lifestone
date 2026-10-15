using System.Buffers.Binary;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Transport;
using Lifestone.Net.Types;

namespace Lifestone.Net.Session;

/// <summary>
/// The receive path (m2-plan §3.3 N5): every waiting datagram is parsed (borrowed, no copy), admitted by sequence with
/// its ISAAC key (docs/protocol.md §2.5, §2.7), verified, and its optional sections handled (NAK, reject, ack,
/// TimeSync); its fragments are reassembled and put in delivery order by the <see cref="InboundSequencer"/>. An
/// in-order message is dispatched straight from the receive buffer; only one that must wait is copied.
/// </summary>
/// <remarks>
/// The flow follows OpenAC's <c>WorldSession.ProcessDatagram</c> (read for its logic, not copied): ConnectRequest and
/// the other sequence-0 packets are cleartext and checked without a key; every other packet goes through the inbound
/// tracker, which hands out the key of its sequence and parks the keys of a gap; a cleartext RejectRetransmit gives back
/// the key it was parked (ACE spends a sequence but no key on it).
/// </remarks>
internal sealed partial class SessionMachine
{
    private void ProcessDatagram(SessionConnection connection, int length, int sourcePort)
    {
        var memory = _receiveBuffer.AsMemory(0, length);
        if (!PacketCodec.TryParseBorrowed(memory, out var packet, out var headerHash, out var payloadHash, out var error))
        {
            connection.MalformedDatagrams++;
            if (_log.IsEnabled(NetLogLevel.Debug))
            {
                _log.Log(NetLogLevel.Debug, $"dropped a {length}-byte datagram from port {sourcePort}: {error}");
            }

            return;
        }

        var header = packet.Header;
        var encrypted = header.HasFlag(PacketHeaderFlags.EncryptedChecksum);
        if (connection.Transport is not { } transport)
        {
            ProcessBeforeConnectRequest(connection, packet, headerHash, payloadHash, encrypted);
            return;
        }

        if (header.Sequence == 0)
        {
            // ConnectRequest again, or an ack-only packet before the first encrypted one: cleartext, no key.
            if (encrypted)
            {
                connection.MalformedDatagrams++;
                return;
            }

            if (!PacketCodec.VerifyChecksum(header, headerHash, payloadHash, isaacKey: null))
            {
                transport.Stats.ChecksumFailures++;
                return;
            }

            if (header.HasFlag(PacketHeaderFlags.ConnectRequest))
            {
                Arrived();
                if (packet.Optional.ConnectRequestCookie != connection.Cookie)
                {
                    Log(NetLogLevel.Warning, "login: ignored a second ConnectRequest with another cookie on the same connection");
                }

                return;
            }
        }
        else
        {
            var admission = transport.Inbound.Admit(header.Sequence, encrypted);
            if (admission.Drop)
            {
                return;
            }

            if (!PacketCodec.VerifyChecksum(header, headerHash, payloadHash, admission.VerifyKey))
            {
                // N8: undo the admission, so a corrupt or forged datagram moves nothing (InboundSequenceTracker.Reject).
                transport.Stats.ChecksumFailures++;
                transport.Inbound.Reject(header.Sequence, admission);
                return;
            }
        }

        Arrived();
        if (encrypted && !connection.HandshakeConfirmed)
        {
            ConfirmHandshake(connection);
        }

        var optional = packet.Optional;
        if (header.HasFlag(PacketHeaderFlags.RequestRetransmit) && optional.RetransmitRequestCount > 0)
        {
            transport.Outbound.OnRetransmitRequest(optional.RetransmitRequestBytes.Span, optional.RetransmitRequestCount);
        }

        if (header.HasFlag(PacketHeaderFlags.RejectRetransmit))
        {
            transport.Stats.RejectsReceived++;
            if (optional.RejectRetransmitCount > 0)
            {
                transport.Inbound.OnRejectRetransmit(optional.RejectRetransmitBytes.Span, optional.RejectRetransmitCount);
            }

            if (header.Sequence != 0 && !encrypted)
            {
                transport.Inbound.OnCleartextRejectSequence(header.Sequence);
            }

            connection.Sequencer!.OnRejectRetransmit();
        }

        if (header.HasFlag(PacketHeaderFlags.AckSequence))
        {
            transport.Outbound.OnAckSequence(optional.AckSequence);
        }

        if (header.HasFlag(PacketHeaderFlags.TimeSync) && optional.TimeSync > 0)
        {
            Raise(SessionEvent.ServerTimeUpdated(_time.GetTimestamp(), Phase, optional.TimeSync));
        }

        if (packet.FragmentCount > 0)
        {
            ReceiveFragments(connection, packet);
        }
    }

    /// <summary>Before ConnectRequest only a cleartext ConnectRequest is taken; see <see cref="ProcessUnverifiedBoot"/> for the exception.</summary>
    private void ProcessBeforeConnectRequest(SessionConnection connection, in BorrowedPacket packet, uint headerHash, uint payloadHash, bool encrypted)
    {
        var header = packet.Header;
        if (encrypted)
        {
            ProcessUnverifiedBoot(connection, packet);
            return;
        }

        if (!PacketCodec.VerifyChecksum(header, headerHash, payloadHash, isaacKey: null))
        {
            connection.EarlyChecksumFailures++;
            return;
        }

        if (header.HasFlag(PacketHeaderFlags.ConnectRequest) && Phase == SessionPhase.Connecting)
        {
            Arrived();
            AcceptConnectRequest(connection, packet);
        }
    }

    /// <summary>
    /// ACE boots a wrong client version before it sends ConnectRequest (<c>AuthenticationHandler</c>'s version check
    /// comes first), in an encrypted packet of sequence 0 whose key the client was never given. Its checksum cannot be
    /// checked, so only an F7DC AccountBoot from it is believed, with a warning, and the session ends; anything else in
    /// such a packet is dropped. The socket already drops everything that is not from the server's two ports.
    /// </summary>
    private void ProcessUnverifiedBoot(SessionConnection connection, in BorrowedPacket packet)
    {
        if (packet.Header.Sequence != 0 || Phase != SessionPhase.Connecting)
        {
            connection.MalformedDatagrams++;
            return;
        }

        foreach (var fragment in packet.Fragments)
        {
            var body = fragment.Payload.Span;
            if (fragment.Header.Count != 1 || body.Length < MessageCodec.OpcodeSize
                || BinaryPrimitives.ReadUInt32LittleEndian(body) != MessageOpcodes.AccountBoot)
            {
                continue;
            }

            Log(NetLogLevel.Warning, "login: an AccountBoot came before any ConnectRequest; believed without a checksum (ACE boots a wrong client version this way)");
            Arrived();
            Dispatch(new InboundContext(fragment.Header.Sequence, (MessageQueue)fragment.Header.Queue, _time.GetTimestamp()), body);
            return;
        }

        connection.MalformedDatagrams++;
    }

    /// <summary>Reassembles the packet's fragments and offers each complete message to the sequencer.</summary>
    private void ReceiveFragments(SessionConnection connection, in BorrowedPacket packet)
    {
        var assembler = connection.Assembler!;
        var received = _time.GetTimestamp();
        foreach (var fragment in packet.Fragments)
        {
            if (!ReferenceEquals(_connection, connection))
            {
                return;  // a message dispatched from this packet ended or restarted the connection
            }

            var single = fragment.Header.Count == 1 && fragment.Header.Index == 0;
            if (!assembler.TryIngest(fragment, out var message, out var queue))
            {
                continue;
            }

            OfferMessage(connection, fragment.Header.Sequence, (MessageQueue)queue, received, message.Span, single ? null : ArrayOf(message));
        }

        if (ReferenceEquals(_connection, connection))
        {
            DrainReady(connection);
        }
    }

    /// <summary>Dispatches an in-order message now when the budget allows; otherwise the sequencer keeps it.</summary>
    private void OfferMessage(SessionConnection connection, uint fragmentSequence, MessageQueue queue, long received, ReadOnlySpan<byte> body, byte[]? owned)
    {
        var sequencer = connection.Sequencer!;
        if (sequencer.Classify(fragmentSequence) == SequencerVerdict.InOrder && CanDispatch())
        {
            sequencer.Advance();
            Dispatch(new InboundContext(fragmentSequence, queue, received), body);
            if (ReferenceEquals(_connection, connection))
            {
                DrainReady(connection);
            }

            return;
        }

        sequencer.Hold(fragmentSequence, queue, received, body, owned);
    }

    /// <summary>Dispatches the waiting messages that are next in order, while the budget allows.</summary>
    private void DrainReady(SessionConnection connection)
    {
        var sequencer = connection.Sequencer;
        while (sequencer is not null && ReferenceEquals(_connection, connection) && sequencer.HasReady && CanDispatch()
               && sequencer.TryTakeReady(out var waiting))
        {
            try
            {
                Dispatch(new InboundContext(waiting.FragmentSequence, waiting.Queue, waiting.ReceivedTimestamp), waiting.Body);
            }
            finally
            {
                InboundSequencer.Release(waiting);
            }
        }
    }

    /// <summary>The gap-skip rule (<see cref="InboundSequencer"/>): logs each skip as an error, then dispatches what it freed.</summary>
    private void SkipGapIfDue(SessionConnection connection)
    {
        if (connection.Sequencer is not { } sequencer || connection.Transport is not { } transport)
        {
            return;
        }

        if (sequencer.TrySkipGap(transport.Inbound.NakCount, connection.Assembler!.PartialCount, out var from, out var to))
        {
            Raise(SessionEvent.Warned(_time.GetTimestamp(), Phase, SessionWarning.GapSkipped, $"messages {from} to {to} skipped"));
            Log(NetLogLevel.Error, $"inbound: gave up on server messages {from} to {to}; {sequencer.WaitingCount} later message(s) go on without them");
            DrainReady(connection);
        }
    }

    private void Arrived()
    {
        _packetsReceived++;
        _lastArrival = _time.GetTimestamp();
    }

    /// <summary>The array behind a reassembled message (the assembler builds a fresh one per message).</summary>
    private static byte[]? ArrayOf(ReadOnlyMemory<byte> message) =>
        System.Runtime.InteropServices.MemoryMarshal.TryGetArray(message, out var segment) && segment.Offset == 0 && segment.Array is { } array
            && array.Length == message.Length
            ? array
            : null;
}

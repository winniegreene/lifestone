// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Transport/TransportStats.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Transport;
// N2: a SendFailures counter. N8: the TailProbes, AdmissionsRejected and AcksHeld counters.

namespace Lifestone.Net.Transport;

internal sealed class TransportStats
{
    public long PacketsReceived;

    public long PacketsSent;

    /// <summary>Datagrams the socket refused to send (a <c>SocketException</c>),
    /// counted as lost on the wire: a reliable one stays in the sent-packet
    /// store, so the server's NAK recovers it (N2).</summary>
    public long SendFailures;

    /// <summary>Datagrams re-emitted in response to a server NAK, or as a tail probe.</summary>
    public long ResendsSent;

    /// <summary>N8: tail probes sent (a subset of <see cref="ResendsSent"/>).</summary>
    public long TailProbes;

    /// <summary>N8: admissions that advanced the window and were undone because the packet's checksum failed.</summary>
    public long AdmissionsRejected;

    /// <summary>N8: sweeps that held a due ack back because a sent packet was not acked yet.</summary>
    public long AcksHeld;

    public long NakRequestsReceived;

    public long UncachedNakIds;

    /// <summary>Inbound <c>AckSequence</c> values folded into the watermark
    /// (explicit acks plus the NAK <c>ids[0]</c> implicit ack).</summary>
    public long AcksConsumed;

    public long InboundDupsDropped;

    public long InboundSanityDrops;

    public long ChecksumFailures;

    public long KeysParked;

    public long AcksSent;

    public long NaksSent;

    public long NakIdsSent;

    public long RejectWordsReclaimed;

    public long RejectsReceived;

    public int CacheDepth => CacheDepthSource?.Invoke() ?? 0;

    internal Func<int>? CacheDepthSource { get; set; }
}

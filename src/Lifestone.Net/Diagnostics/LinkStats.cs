namespace Lifestone.Net.Diagnostics;

/// <summary>
/// Counters of a session's link, as a snapshot (<see cref="Session.IAcSession.Link"/>): what the perf overlay, the
/// automation inspector and the lossy soak's oracle read. Counts are since the session was created, across retries.
/// </summary>
/// <param name="PacketsSent">Datagrams sent, resends included.</param>
/// <param name="PacketsReceived">Datagrams received from the server and admitted.</param>
/// <param name="Resends">Packets sent again because the server NAKed them, or as tail probes.</param>
/// <param name="NaksSent">NAK requests the client sent for missing server packets.</param>
/// <param name="NaksReceived">NAK requests the server sent for missing client packets.</param>
/// <param name="DuplicatesDropped">Server packets received a second time and dropped.</param>
/// <param name="ChecksumFailures">Server packets whose checksum (Hash32 with the ISAAC key) did not verify.</param>
/// <param name="GapSkips">Fragment-sequence gaps the inbound sequencer gave up waiting for (Lifestone's ordered delivery).</param>
/// <param name="TailProbes">Tail probes sent (Lifestone deviation, off with <c>--retail-net</c>); also counted in
/// <paramref name="Resends"/>.</param>
/// <param name="DatagramsFiltered">Datagrams dropped by the socket for not coming from the server's two ports.</param>
/// <param name="LossPercent">Retail's running packet-loss average, in percent.</param>
/// <param name="LastReceivedTimestamp">The session time source's timestamp when the last server datagram arrived; 0 before any (what retail's link status reads as the time last heard from the server).</param>
public readonly record struct LinkStats(
    long PacketsSent,
    long PacketsReceived,
    long Resends,
    long NaksSent,
    long NaksReceived,
    long DuplicatesDropped,
    long ChecksumFailures,
    long GapSkips,
    long TailProbes,
    long DatagramsFiltered,
    double LossPercent,
    long LastReceivedTimestamp = 0);

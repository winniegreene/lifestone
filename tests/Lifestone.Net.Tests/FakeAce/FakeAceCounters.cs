namespace Lifestone.Net.Tests.FakeAce;

/// <summary>
/// What the fake server counted, for assertions: ACE's <c>NetworkStatistics</c> counters plus the places where ACE only
/// logs (a packet "received again", an unsolicited packet, a ConnectResponse no session matched). Single-threaded, like
/// the fake.
/// </summary>
internal sealed class FakeAceCounters
{
    /// <summary>Client packets that reached a session (ACE <c>C2S_Packets</c>).</summary>
    public long C2SPackets { get; set; }

    /// <summary>Client packets whose checksum or key failed (ACE <c>C2S_CRCErrors</c>); a duplicate encrypted packet lands here.</summary>
    public long C2SCrcErrors { get; set; }

    /// <summary>Client NAKs the server serviced (ACE <c>C2S_RequestsForRetransmit</c>).</summary>
    public long C2SRequestsForRetransmit { get; set; }

    /// <summary>NAKs the server sent (ACE <c>S2C_RequestsForRetransmit</c>).</summary>
    public long S2CRequestsForRetransmit { get; set; }

    /// <summary>Datagrams the server sent (ACE <c>S2C_Packets</c>, plus retransmissions).</summary>
    public long S2CPackets { get; set; }

    /// <summary>Cached packets resent for a client NAK.</summary>
    public long S2CRetransmits { get; set; }

    /// <summary>RejectRetransmit packets sent (each consumes a sequence and no key).</summary>
    public long S2CRejects { get; set; }

    /// <summary>Client packets dropped as already processed ("Packet N received again").</summary>
    public long DuplicatesDropped { get; set; }

    /// <summary>Client packets held because they arrived ahead of the next expected sequence.</summary>
    public long OutOfOrderBuffered { get; set; }

    /// <summary>
    /// Packets ahead of the next expected sequence that ACE dropped because another packet with the same sequence was
    /// already held (<c>outOfOrderPackets.ContainsKey</c>, first one wins); ACE logs nothing for it. A data packet lost
    /// this way after its checksum passed is lost for good: its key is spent, so every resend is a CRC error.
    /// </summary>
    public long OutOfOrderSlotTaken { get; set; }

    /// <summary>Of <see cref="OutOfOrderSlotTaken"/>, data packets dropped behind an ack-only packet of the same sequence.</summary>
    public long DataLostBehindAckOnly { get; set; }

    /// <summary>Datagrams ACE's parse refused.</summary>
    public long Unparsable { get; set; }

    /// <summary>Packets for no session, or from an endpoint other than the session's.</summary>
    public long Unsolicited { get; set; }

    /// <summary>ConnectResponses that matched no session waiting in AuthConnectResponse (the handshake race).</summary>
    public long ConnectResponsesIgnored { get; set; }

    /// <summary>Packets on the world port that were not a ConnectResponse.</summary>
    public long RejectedOnWorldPort { get; set; }

    /// <summary>LoginRequests resent from an endpoint whose session waited in AuthConnectResponse ("Bad handshake").</summary>
    public long BadHandshakes { get; set; }

    /// <summary>Client messages a handler took.</summary>
    public long MessagesHandled { get; set; }

    /// <summary>Client messages ignored because the session was in another state (ACE ignores them silently).</summary>
    public long MessagesIgnoredForState { get; set; }

    /// <summary>Client messages or game actions with no handler.</summary>
    public long UnhandledMessages { get; set; }

    /// <summary>Handlers that threw on a malformed client message (ACE logs and goes on).</summary>
    public long HandlerExceptions { get; set; }

    /// <summary>Sessions terminated, by reason.</summary>
    public Dictionary<FakeAceTermination, int> Terminations { get; } = [];

    /// <summary>Counts one termination.</summary>
    public void CountTermination(FakeAceTermination reason) =>
        Terminations[reason] = Terminations.TryGetValue(reason, out var n) ? n + 1 : 1;
}

/// <summary>Why the fake terminated a session (ACE's <c>SessionTerminationReason</c> values that the fake reproduces).</summary>
internal enum FakeAceTermination
{
    /// <summary>A second LoginRequest after the seeds were sent.</summary>
    BadHandshake,

    /// <summary>The client version was not "1802", or a CharacterCreate did not carry 55 skills.</summary>
    ClientVersionIncorrect,

    /// <summary>The account name was longer than 50 characters.</summary>
    AccountInformationInvalid,

    /// <summary>No password and no GLS ticket.</summary>
    NotAuthorizedNoPasswordOrTicket,

    /// <summary>The account does not exist (and was not auto-created).</summary>
    NotAuthorizedAccountNotFound,

    /// <summary>The password did not match.</summary>
    NotAuthorizedPasswordMismatch,

    /// <summary>A GLS ticket (not implemented by ACE).</summary>
    NotAuthorizedGlsTicket,

    /// <summary>The account was already logged in.</summary>
    AccountInUse,

    /// <summary>A newer login booted this session.</summary>
    AccountLoggedIn,

    /// <summary>The account is banned.</summary>
    AccountBanned,

    /// <summary>The world was closed when the ConnectResponse arrived.</summary>
    WorldClosed,

    /// <summary>The client sent a Disconnect.</summary>
    PacketHeaderDisconnect,

    /// <summary>The client sent NetErrorDisconnect.</summary>
    ClientSentNetworkErrorDisconnect,

    /// <summary>A client packet more than 256 ahead of the next expected one.</summary>
    AbnormalSequenceReceived,

    /// <summary>No valid packet for the timeout (15 s before login completes, 60 s after).</summary>
    NetworkTimeout,

    /// <summary>The client's dats are newer than the server's.</summary>
    DatsNewerThanServer,

    /// <summary>The client's dats are older and patching is off.</summary>
    DatsPatchingDisabled,

    /// <summary>The server shut down.</summary>
    ServerShuttingDown,
}

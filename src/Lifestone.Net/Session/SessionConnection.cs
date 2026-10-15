using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;
using Lifestone.Net.Crypto;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Sockets;
using Lifestone.Net.Transport;

namespace Lifestone.Net.Session;

/// <summary>
/// One login attempt of a <see cref="SessionMachine"/>: its socket (a fresh one per attempt, so a LoginRequest is never
/// sent twice from one endpoint, D18) and, once ConnectRequest arrived, the seeds, the client id, the reliable transport
/// (OpenAC's, N1/N2), the fragment reassembler and the inbound sequencer. Disposing it closes the socket and hands every
/// pooled buffer back.
/// </summary>
/// <remarks>
/// Packet-level sends live here: LoginRequest (cleartext, sequence 0, to the login port), ConnectResponse (cleartext,
/// sequence 1, id 0, to the world port) and Disconnect (cleartext, sequence 0, the client id, to the login port), as
/// retail's <c>ClientNet::SendLoginRequest</c> (11.4186 0x00543F90, 11.6096 0x00544B50) and
/// <c>ClientNet::SendConnectAck</c> (11.4186 0x005440F0, 11.6096 0x00544CB0) send theirs. None of them is a message, so
/// none reaches the raw tap. A send the socket refuses (<see cref="SocketException"/>) counts as a datagram lost on the
/// wire: the handshake resends, the retry policy or a NAK recovers it.
/// </remarks>
internal sealed class SessionConnection : IDisposable
{
    private readonly IDatagramSocket _socket;
    private readonly TimeProvider _time;
    private byte[]? _connectResponse;
    private bool _disposed;

    public SessionConnection(IDatagramSocket socket, TimeProvider time, int attempt)
    {
        _socket = socket;
        _time = time;
        Attempt = attempt;
        OpenedAt = time.GetTimestamp();
    }

    /// <summary>The socket of this attempt.</summary>
    public IDatagramSocket Socket => _socket;

    /// <summary>Which login attempt this is (1 for the first).</summary>
    public int Attempt { get; }

    /// <summary>When the socket was opened.</summary>
    public long OpenedAt { get; }

    /// <summary>When the LoginRequest went out.</summary>
    public long LoginRequestSentAt { get; private set; }

    /// <summary>The reliable transport, from ConnectRequest on; null before.</summary>
    public ReliableTransport? Transport { get; private set; }

    /// <summary>The reassembler of multi-fragment messages, from ConnectRequest on.</summary>
    public FragmentAssembler? Assembler { get; private set; }

    /// <summary>The inbound sequencer, from ConnectRequest on.</summary>
    public InboundSequencer? Sequencer { get; private set; }

    /// <summary>The client id ConnectRequest assigned (the header Id of every later client packet).</summary>
    public ushort ClientId { get; private set; }

    /// <summary>The header iteration of the ConnectRequest, echoed on client packets (ACE ignores it).</summary>
    public ushort Iteration { get; private set; }

    /// <summary>The ConnectRequest cookie.</summary>
    public ulong Cookie { get; private set; }

    /// <summary>When ConnectRequest arrived; null before.</summary>
    public long? ConnectRequestAt { get; private set; }

    /// <summary>When the next ConnectResponse is due (handshaking only).</summary>
    public long NextConnectResponseAt { get; set; }

    /// <summary>ConnectResponses sent.</summary>
    public int ConnectResponsesSent { get; private set; }

    /// <summary>True from the first encrypted server packet on: the server took the ConnectResponse.</summary>
    public bool HandshakeConfirmed { get; set; }

    /// <summary>The GameAction sequence the next action gets (retail <c>Proto_UI::GetNextUICounter</c> counts from 1).</summary>
    public uint NextActionSequence { get; set; } = GameActionEnvelope.FirstSequence;

    /// <summary>Datagrams this connection sent outside the transport (LoginRequest, ConnectResponse, Disconnect).</summary>
    public long RawPacketsSent { get; private set; }

    /// <summary>Datagrams the socket refused outside the transport.</summary>
    public long RawSendFailures { get; private set; }

    /// <summary>Cleartext packets that failed their checksum before the transport existed.</summary>
    public long EarlyChecksumFailures { get; set; }

    /// <summary>Datagrams that did not parse, or that no state of the session expects (an encrypted packet of sequence 0).</summary>
    public long MalformedDatagrams { get; set; }

    /// <summary>
    /// Sends the LoginRequest, written straight into a pooled buffer that is zeroed after the send
    /// (<see cref="LoginRequest.Send"/>); the password never becomes a string.
    /// </summary>
    public void SendLoginRequest(ReadOnlySpan<char> clientVersion, ReadOnlySpan<char> account, ReadOnlySpan<char> password, uint timestamp)
    {
        LoginRequestSentAt = _time.GetTimestamp();
        LoginRequest.Send(SendToLoginPort, ArrayPool<byte>.Shared, clientVersion, account, password, timestamp);
    }

    /// <summary>
    /// Takes a verified ConnectRequest: the client id, the cookie and the two ISAAC seeds (the first keys server
    /// packets, the second the client's; docs/protocol.md §2.5), and builds the transport around them. Retail's
    /// <c>ClientNet::HandleConnectionRequest</c> (11.4186 0x00545AC0, 11.6096 0x00546680). <paramref name="tailProbe"/>
    /// turns on the outbound queue's tail probe (N8; <see cref="AcSessionOptions.TailProbe"/>).
    /// </summary>
    public void AcceptConnectRequest(in BorrowedOptionalHeader optional, ushort iteration, InboundOrder order, bool tailProbe)
    {
        ClientId = (ushort)optional.ConnectRequestClientId;
        Iteration = iteration;
        Cookie = optional.ConnectRequestCookie;
        ConnectRequestAt = _time.GetTimestamp();

        Span<byte> seed = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(seed, optional.ConnectRequestClientSeed);
        var outbound = new IsaacRandom(seed);
        BinaryPrimitives.WriteUInt32LittleEndian(seed, optional.ConnectRequestServerSeed);
        var inbound = new IsaacRandom(seed);

        Assembler = new FragmentAssembler(_time);
        Sequencer = new InboundSequencer(order, _time);
        Transport = new ReliableTransport(
            outbound,
            inbound,
            ClientId,
            Iteration,
            SendToLoginPort,
            new TransportClock(_time),
            ArrayPool<byte>.Shared,
            Assembler);
        Transport.Outbound.TailProbeEnabled = tailProbe;

        Span<byte> cookie = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(cookie, Cookie);
        _connectResponse = PacketCodec.Encode(
            new PacketHeader { Sequence = 1, Flags = PacketHeaderFlags.ConnectResponse, Id = 0 },
            cookie,
            outboundIsaac: null);
    }

    /// <summary>Sends (or resends) the ConnectResponse to the world port, Port + 1.</summary>
    public void SendConnectResponse()
    {
        if (_connectResponse is null)
        {
            throw new InvalidOperationException("no ConnectRequest yet");
        }

        ConnectResponsesSent++;
        SendRaw(_socket.Server.Port + 1, _connectResponse);
    }

    /// <summary>
    /// Sends a cleartext Disconnect with the client id (docs/protocol.md §2.10), best effort; ACE drops the session at
    /// once. Nothing is sent before ConnectRequest: without a client id the packet would name another client's slot.
    /// </summary>
    public void SendDisconnect()
    {
        if (ConnectRequestAt is null || _disposed)
        {
            return;
        }

        SendRaw(_socket.Server.Port, TransportDisconnect.Build(ClientId, Iteration));
    }

    /// <summary>This connection's counters, for <see cref="SessionMachine.Link"/>.</summary>
    public LinkTotals Totals()
    {
        var totals = new LinkTotals
        {
            PacketsSent = RawPacketsSent,
            ChecksumFailures = EarlyChecksumFailures,
            DatagramsFiltered = _disposed ? 0 : _socket.DatagramsFiltered,
        };

        if (Transport is { } transport)
        {
            var stats = transport.Stats;
            totals.PacketsSent += stats.PacketsSent;
            totals.Resends = stats.ResendsSent;
            totals.TailProbes = stats.TailProbes;
            totals.NaksSent = stats.NaksSent;
            totals.NaksReceived = stats.NakRequestsReceived;
            totals.DuplicatesDropped = stats.InboundDupsDropped;
            totals.ChecksumFailures += stats.ChecksumFailures;
        }

        if (Sequencer is { } sequencer)
        {
            totals.GapSkips = sequencer.GapSkips;
        }

        return totals;
    }

    /// <summary>Closes the socket, the transport and the sequencer; idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Sequencer?.Clear();
        Assembler?.DropAll();
        Transport?.Dispose();
        _socket.Dispose();
    }

    private void SendToLoginPort(ReadOnlySpan<byte> datagram) => SendRaw(_socket.Server.Port, datagram);

    private void SendRaw(int port, ReadOnlySpan<byte> datagram)
    {
        try
        {
            _socket.Send(port, datagram);
            RawPacketsSent++;
        }
        catch (SocketException)
        {
            RawSendFailures++;
        }
    }
}

/// <summary>Link counters summed over connections (<see cref="Diagnostics.LinkStats"/> without the derived values).</summary>
internal struct LinkTotals
{
    public long PacketsSent;
    public long Resends;
    public long NaksSent;
    public long NaksReceived;
    public long DuplicatesDropped;
    public long ChecksumFailures;
    public long GapSkips;
    public long TailProbes;
    public long DatagramsFiltered;

    /// <summary>The sum of two totals.</summary>
    public readonly LinkTotals Plus(in LinkTotals other) => new()
    {
        PacketsSent = PacketsSent + other.PacketsSent,
        Resends = Resends + other.Resends,
        NaksSent = NaksSent + other.NaksSent,
        NaksReceived = NaksReceived + other.NaksReceived,
        DuplicatesDropped = DuplicatesDropped + other.DuplicatesDropped,
        ChecksumFailures = ChecksumFailures + other.ChecksumFailures,
        GapSkips = GapSkips + other.GapSkips,
        TailProbes = TailProbes + other.TailProbes,
        DatagramsFiltered = DatagramsFiltered + other.DatagramsFiltered,
    };
}

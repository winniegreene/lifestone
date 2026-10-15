using System.Buffers;
using System.Buffers.Binary;
using Lifestone.Net.Crypto;
using Lifestone.Net.Packets;

namespace Lifestone.Net.Tests.Support;

/// <summary>
/// A hand-driven client for the fake server's conformance tests: it builds packets with exactly the sequence, flags and
/// keys a test asks for (in order, out of order, twice, or never), sends them through the <see cref="InMemoryNetwork"/>,
/// and decodes what comes back, recording the key each encrypted server packet was sealed with. Test credentials only.
/// </summary>
/// <remarks>
/// The packet writer here is its own (docs/protocol.md §2.1 to §2.4); only the checked <see cref="Hash32"/>,
/// <see cref="IsaacRandom"/> and the product's LoginRequest writer are shared, so the fake is tested against the spec,
/// not against the product's transport.
/// </remarks>
internal sealed class RawAceClient : IDisposable
{
    /// <summary>Header flags (docs/protocol.md §2.2).</summary>
    public const uint EncryptedChecksum = 0x2, BlobFragments = 0x4, RequestRetransmit = 0x1000, RejectRetransmit = 0x2000,
        AckSequence = 0x4000, Disconnect = 0x8000, ConnectRequest = 0x40000, ConnectResponse = 0x80000, TimeSync = 0x1000000,
        EchoRequest = 0x2000000, EchoResponse = 0x4000000;

    private IsaacRandom? _clientKeys;
    private readonly Dictionary<uint, byte[]> _messagesBySequence = [];
    private readonly Dictionary<uint, SortedDictionary<ushort, byte[]>> _partial = [];

    /// <summary>A client with a fresh socket on <paramref name="harness"/>'s network.</summary>
    public RawAceClient(SessionHarness harness)
    {
        Socket = harness.Network.OpenClientSocket(harness.ServerEndPoint);
    }

    /// <summary>The client's socket.</summary>
    public InMemoryNetwork.InMemoryClientSocket Socket { get; }

    /// <summary>Every server datagram received, decoded, in arrival order.</summary>
    public List<ServerDatagram> Received { get; } = [];

    /// <summary>The ConnectRequest, once it arrived.</summary>
    public ServerDatagram? ConnectRequestPacket { get; private set; }

    /// <summary>The client id the ConnectRequest assigned (the header Id of later packets).</summary>
    public ushort ClientId { get; private set; }

    /// <summary>The fragment sequence of the last message built (the first is 1); a test may set it back.</summary>
    public uint FragmentSequence { get; set; }

    /// <summary>The whole messages received, by fragment sequence, reassembled.</summary>
    public IReadOnlyDictionary<uint, byte[]> MessagesBySequence => _messagesBySequence;

    /// <summary>The whole messages received, in fragment-sequence order.</summary>
    public IReadOnlyList<byte[]> Messages => _messagesBySequence.OrderBy(p => p.Key).Select(p => p.Value).ToList();

    /// <summary>The opcodes of the whole messages received, in fragment-sequence order.</summary>
    public IReadOnlyList<uint> Opcodes => _messagesBySequence.OrderBy(p => p.Key).Select(p => BinaryPrimitives.ReadUInt32LittleEndian(p.Value)).ToList();

    /// <summary>Sends a LoginRequest (sequence 0, cleartext) to the login port.</summary>
    public void SendLoginRequest(string account, string password, string clientVersion = LoginRequest.RetailClientVersion, uint timestamp = 0) =>
        LoginRequest.Send(d => Socket.Send(Socket.Server.Port, d), ArrayPool<byte>.Shared, clientVersion, account, password, timestamp);

    /// <summary>Sends a ConnectResponse (sequence 1, Id 0, cleartext) with the ConnectRequest's cookie (or <paramref name="cookie"/>) to the world port.</summary>
    public void SendConnectResponse(ulong? cookie = null)
    {
        var value = cookie ?? ConnectRequestPacket?.ConnectRequestCookie ?? throw new InvalidOperationException("no ConnectRequest yet");
        var datagram = Build(1, ConnectResponse, BitConverter.GetBytes(value), [], key: null, id: 0);
        Socket.Send(Socket.Server.Port + 1, datagram);
    }

    /// <summary>
    /// Builds an encrypted data packet with <paramref name="messages"/>, one fragment each, numbered from the next
    /// fragment sequence, sealed with the next client key. Nothing is sent.
    /// </summary>
    public byte[] BuildData(uint sequence, params byte[][] messages)
    {
        var keys = _clientKeys ?? throw new InvalidOperationException("no ConnectRequest yet");
        return Build(sequence, EncryptedChecksum | BlobFragments, [], Fragments(messages), keys.Next(), ClientId);
    }

    /// <summary>Builds a cleartext packet (flags, optional section, messages as fragments) with this client's id.</summary>
    public byte[] BuildCleartext(uint sequence, uint flags, byte[] optional, params byte[][] messages) =>
        Build(sequence, flags | (messages.Length > 0 ? BlobFragments : 0), optional, Fragments(messages), key: null, ClientId);

    /// <summary>A cleartext ack-only packet.</summary>
    public byte[] BuildAckOnly(uint sequence, uint ack) => BuildCleartext(sequence, AckSequence, BitConverter.GetBytes(ack));

    /// <summary>A cleartext NAK for <paramref name="sequences"/>.</summary>
    public byte[] BuildNak(uint sequence, params uint[] sequences)
    {
        var optional = new List<byte>(BitConverter.GetBytes((uint)sequences.Length));
        foreach (var s in sequences)
        {
            optional.AddRange(BitConverter.GetBytes(s));
        }

        return BuildCleartext(sequence, RequestRetransmit, [.. optional]);
    }

    /// <summary>A cleartext Disconnect (sequence 0).</summary>
    public byte[] BuildDisconnect() => BuildCleartext(0, Disconnect, []);

    /// <summary>Sends a datagram to the login port.</summary>
    public void Send(byte[] datagram) => Socket.Send(Socket.Server.Port, datagram);

    /// <summary>Takes every waiting datagram, decodes it, learns the seeds from a ConnectRequest; returns how many.</summary>
    public int Drain()
    {
        var buffer = new byte[2048];
        var count = 0;
        while (Socket.TryReceive(buffer, out var length, out var port))
        {
            var datagram = ServerDatagram.Decode(buffer.AsSpan(0, length), port);
            Received.Add(datagram);
            count++;
            if (datagram.Has(ConnectRequest) && ConnectRequestPacket is null)
            {
                ConnectRequestPacket = datagram;
                ClientId = (ushort)datagram.ConnectRequestClientId;
                _clientKeys = new IsaacRandom(BitConverter.GetBytes(datagram.ConnectRequestClientSeed));
            }

            foreach (var fragment in datagram.Fragments)
            {
                Collect(fragment);
            }
        }

        return count;
    }

    /// <summary>
    /// Checks every encrypted server packet's key against the server's keystream: in sequence order, the n-th distinct
    /// encrypted sequence carries the n-th key, and a retransmission carries its original's key.
    /// </summary>
    public void AssertServerKeysFollowTheKeystream()
    {
        var request = ConnectRequestPacket ?? throw new InvalidOperationException("no ConnectRequest yet");
        var keys = new IsaacRandom(BitConverter.GetBytes(request.ConnectRequestServerSeed));
        var bySequence = Received.Where(d => d.Has(EncryptedChecksum)).GroupBy(d => d.Sequence).OrderBy(g => g.Key);
        foreach (var group in bySequence)
        {
            var expected = keys.Next();
            Assert.All(group, d => Assert.True(d.RecoveredKey == expected,
                $"server packet {d.Sequence} carries key 0x{d.RecoveredKey:X8}; the keystream's next is 0x{expected:X8}"));
        }
    }

    public void Dispose() => Socket.Dispose();

    private List<(uint Sequence, byte[] Data)> Fragments(byte[][] messages) =>
        messages.Select(m => (++FragmentSequence, m)).ToList();

    private void Collect(ServerDatagram.Fragment fragment)
    {
        if (fragment.Count == 1)
        {
            _messagesBySequence.TryAdd(fragment.Sequence, fragment.Data);
            return;
        }

        if (!_partial.TryGetValue(fragment.Sequence, out var parts))
        {
            _partial[fragment.Sequence] = parts = [];
        }

        parts[fragment.Index] = fragment.Data;
        if (parts.Count == fragment.Count)
        {
            _messagesBySequence.TryAdd(fragment.Sequence, parts.Values.SelectMany(p => p).ToArray());
            _partial.Remove(fragment.Sequence);
        }
    }

    private static byte[] Build(uint sequence, uint flags, byte[] optional, List<(uint Sequence, byte[] Data)> fragments, uint? key, ushort id)
    {
        var body = new List<byte>(optional);
        var payloadHash = Hash32.Calculate(optional);
        foreach (var (fragmentSequence, data) in fragments)
        {
            var header = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(header, fragmentSequence);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 0x80000000);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), (ushort)(16 + data.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), 9);
            payloadHash = unchecked(payloadHash + Hash32.Calculate(header) + Hash32.Calculate(data));
            body.AddRange(header);
            body.AddRange(data);
        }

        var datagram = new byte[20 + body.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(datagram, sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(4), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(8), 0xBADD70DD);
        BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(12), id);
        BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(16), (ushort)body.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(18), 1);
        var headerHash = Hash32.Calculate(datagram.AsSpan(0, 20));
        BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(8), unchecked(headerHash + (payloadHash ^ (key ?? 0))));
        body.CopyTo(datagram, 20);
        return datagram;
    }
}

/// <summary>A decoded server datagram (docs/protocol.md §2): header, the optional sections a server sends, fragments, and its key.</summary>
internal sealed class ServerDatagram
{
    public int SourcePort { get; private init; }

    public uint Sequence { get; private init; }

    public uint Flags { get; private init; }

    public ushort Id { get; private init; }

    public ushort Iteration { get; private init; }

    public IReadOnlyList<uint> RetransmitRequests { get; private set; } = [];

    public IReadOnlyList<uint> RejectedSequences { get; private set; } = [];

    public uint AckSequence { get; private set; }

    public ulong ConnectRequestCookie { get; private set; }

    public uint ConnectRequestClientId { get; private set; }

    public uint ConnectRequestServerSeed { get; private set; }

    public uint ConnectRequestClientSeed { get; private set; }

    public double? TimeSync { get; private set; }

    public List<Fragment> Fragments { get; } = [];

    /// <summary>For an encrypted packet, the key it was sealed with; for a cleartext one, 0 (and its checksum was checked).</summary>
    public uint RecoveredKey { get; private set; }

    public bool Has(uint flag) => (Flags & flag) != 0;

    /// <summary>Decodes; throws on anything malformed, since the fake must never send that.</summary>
    public static ServerDatagram Decode(ReadOnlySpan<byte> received, int sourcePort)
    {
        var datagram = received.ToArray();
        var size = BinaryPrimitives.ReadUInt16LittleEndian(datagram.AsSpan(16));
        Assert.Equal(datagram.Length - 20, size);
        var d = new ServerDatagram
        {
            SourcePort = sourcePort,
            Sequence = BinaryPrimitives.ReadUInt32LittleEndian(datagram),
            Flags = BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(4)),
            Id = BinaryPrimitives.ReadUInt16LittleEndian(datagram.AsSpan(12)),
            Iteration = BinaryPrimitives.ReadUInt16LittleEndian(datagram.AsSpan(18)),
        };
        var body = datagram.AsSpan(20);
        var pos = 0;
        uint U32()
        {
            var v = BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(20 + pos));
            pos += 4;
            return v;
        }

        List<uint> List()
        {
            var n = U32();
            var list = new List<uint>();
            for (var i = 0; i < n; i++)
            {
                list.Add(U32());
            }

            return list;
        }

        if (d.Has(RawAceClient.RequestRetransmit)) d.RetransmitRequests = List();
        if (d.Has(RawAceClient.RejectRetransmit)) d.RejectedSequences = List();
        if (d.Has(RawAceClient.AckSequence)) d.AckSequence = U32();
        if (d.Has(RawAceClient.ConnectRequest))
        {
            pos += 8;  // server time
            d.ConnectRequestCookie = U32() | ((ulong)U32() << 32);
            d.ConnectRequestClientId = U32();
            d.ConnectRequestServerSeed = U32();
            d.ConnectRequestClientSeed = U32();
            pos += 4;
        }

        if (d.Has(RawAceClient.TimeSync))
        {
            d.TimeSync = BinaryPrimitives.ReadDoubleLittleEndian(body[pos..]);
            pos += 8;
        }

        if (d.Has(RawAceClient.EchoResponse))
        {
            pos += 8;
        }

        var optionalHash = Hash32.Calculate(body[..pos]);
        var payloadHash = optionalHash;
        if (d.Has(RawAceClient.BlobFragments))
        {
            while (pos < body.Length)
            {
                var header = body.Slice(pos, 16);
                var fragmentSize = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);
                var data = body.Slice(pos + 16, fragmentSize - 16).ToArray();
                d.Fragments.Add(new Fragment(
                    BinaryPrimitives.ReadUInt32LittleEndian(header),
                    BinaryPrimitives.ReadUInt16LittleEndian(header[8..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(header[12..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(header[14..]),
                    data));
                payloadHash = unchecked(payloadHash + Hash32.Calculate(header) + Hash32.Calculate(data));
                pos += fragmentSize;
            }
        }

        Assert.Equal(body.Length, pos);
        Span<byte> hashed = stackalloc byte[20];
        datagram.AsSpan(0, 20).CopyTo(hashed);
        BinaryPrimitives.WriteUInt32LittleEndian(hashed[8..], 0xBADD70DD);
        var headerHash = Hash32.Calculate(hashed);
        var checksum = BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(8));
        if (d.Has(RawAceClient.EncryptedChecksum))
        {
            d.RecoveredKey = unchecked((checksum - headerHash) ^ payloadHash);
        }
        else
        {
            Assert.Equal(unchecked(headerHash + payloadHash), checksum);
        }

        return d;
    }

    /// <summary>One server fragment.</summary>
    public sealed record Fragment(uint Sequence, ushort Count, ushort Index, ushort Queue, byte[] Data);
}

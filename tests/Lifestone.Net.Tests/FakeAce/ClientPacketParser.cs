using System.Buffers.Binary;
using Lifestone.Net.Crypto;

namespace Lifestone.Net.Tests.FakeAce;

/// <summary>
/// The fake server's own parse of a client datagram, shaped like ACE's (<c>ClientPacket.Unpack</c>,
/// <c>PacketHeaderOptional.Unpack</c>, <c>ClientPacketFragment.Unpack</c>, read, not copied) and written from
/// docs/protocol.md §2.1 to §2.4. It is independent of Lifestone.Net's packet code (only the checked
/// <see cref="Hash32"/> is shared), so a product encoding mistake cannot hide behind a matching mistake here.
/// </summary>
/// <remarks>
/// What ACE does and this mirrors: only <c>Size</c> bytes after the header count; the optional sections are read in
/// ACE's order (ServerSwitch, RequestRetransmit, RejectRetransmit, AckSequence, LoginRequest, WorldLoginRequest,
/// ConnectResponse, CICMDCommand, TimeSync, EchoRequest, Flow), and LoginRequest, WorldLoginRequest and ConnectResponse
/// leave the reader where it was; a fragment must be 16 to 464 bytes; a short fragment's data is whatever is left.
/// </remarks>
internal static class ClientPacketParser
{
    /// <summary>Header flags (docs/protocol.md §2.2).</summary>
    public const uint Retransmission = 0x1, EncryptedChecksum = 0x2, BlobFragments = 0x4, ServerSwitch = 0x100,
        RequestRetransmit = 0x1000, RejectRetransmit = 0x2000, AckSequence = 0x4000, Disconnect = 0x8000,
        LoginRequest = 0x10000, WorldLoginRequest = 0x20000, ConnectRequest = 0x40000, ConnectResponse = 0x80000,
        NetErrorDisconnect = 0x200000, CicmdCommand = 0x400000, TimeSync = 0x1000000, EchoRequest = 0x2000000,
        EchoResponse = 0x4000000, Flow = 0x8000000;

    /// <summary>The 20-byte packet header.</summary>
    public const int HeaderSize = 20;

    /// <summary>The 16-byte fragment header.</summary>
    public const int FragmentHeaderSize = 16;

    /// <summary>ACE's largest client fragment, header included.</summary>
    public const int MaxFragmentSize = 464;

    /// <summary>The value the checksum field holds while the header is hashed.</summary>
    public const uint ChecksumPlaceholder = 0xBADD70DD;

    /// <summary>Parses <paramref name="datagram"/>; null when ACE would drop it as unreadable.</summary>
    public static ClientPacket? TryParse(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length < HeaderSize)
        {
            return null;
        }

        var header = datagram[..HeaderSize];
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var size = BinaryPrimitives.ReadUInt16LittleEndian(header[16..]);
        if (size > datagram.Length - HeaderSize)
        {
            return null;
        }

        var body = datagram.Slice(HeaderSize, size);
        var packet = new ClientPacket
        {
            Sequence = BinaryPrimitives.ReadUInt32LittleEndian(header),
            Flags = flags,
            Checksum = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]),
            Id = BinaryPrimitives.ReadUInt16LittleEndian(header[12..]),
            Time = BinaryPrimitives.ReadUInt16LittleEndian(header[14..]),
            Size = size,
            Iteration = BinaryPrimitives.ReadUInt16LittleEndian(header[18..]),
        };

        var pos = 0;
        var optional = new List<byte>();
        bool Take(ReadOnlySpan<byte> source, int count, bool advance, out ReadOnlySpan<byte> bytes)
        {
            if (source.Length - pos < count)
            {
                bytes = default;
                return false;
            }

            bytes = source.Slice(pos, count);
            optional.AddRange(bytes.ToArray());
            if (advance)
            {
                pos += count;
            }

            return true;
        }

        if ((flags & ServerSwitch) != 0 && !Take(body, 8, true, out _))
        {
            return null;
        }

        if ((flags & RequestRetransmit) != 0)
        {
            if (!Take(body, 4, true, out var countBytes))
            {
                return null;
            }

            var count = BinaryPrimitives.ReadUInt32LittleEndian(countBytes);
            var list = new List<uint>();
            for (var i = 0u; i < count; i++)
            {
                if (!Take(body, 4, true, out var sequence))
                {
                    return null;
                }

                list.Add(BinaryPrimitives.ReadUInt32LittleEndian(sequence));
            }

            packet.RetransmitRequests = list;
        }

        if ((flags & RejectRetransmit) != 0)
        {
            if (!Take(body, 4, true, out var countBytes))
            {
                return null;
            }

            var count = BinaryPrimitives.ReadUInt32LittleEndian(countBytes);
            for (var i = 0u; i < count; i++)
            {
                if (!Take(body, 4, true, out _))
                {
                    return null;
                }
            }
        }

        if ((flags & AckSequence) != 0)
        {
            if (!Take(body, 4, true, out var ack))
            {
                return null;
            }

            packet.AckSequence = BinaryPrimitives.ReadUInt32LittleEndian(ack);
        }

        if ((flags & LoginRequest) != 0)
        {
            if (body.Length - pos < 1 || !Take(body, body.Length - pos, false, out var login))
            {
                return null;
            }

            packet.LoginBytes = login.ToArray();
        }

        if ((flags & WorldLoginRequest) != 0 && !Take(body, 8, false, out _))
        {
            return null;
        }

        if ((flags & ConnectResponse) != 0)
        {
            if (!Take(body, 8, false, out var cookie))
            {
                return null;
            }

            packet.ConnectResponseCookie = BinaryPrimitives.ReadUInt64LittleEndian(cookie);
        }

        if ((flags & CicmdCommand) != 0 && !Take(body, 8, true, out _))
        {
            return null;
        }

        if ((flags & TimeSync) != 0 && !Take(body, 8, true, out _))
        {
            return null;
        }

        if ((flags & EchoRequest) != 0)
        {
            if (!Take(body, 4, true, out var echo))
            {
                return null;
            }

            packet.EchoRequestClientTime = BinaryPrimitives.ReadSingleLittleEndian(echo);
        }

        if ((flags & Flow) != 0 && !Take(body, 6, true, out _))
        {
            return null;
        }

        packet.OptionalHash = Hash32.Calculate(optional.ToArray());

        if ((flags & BlobFragments) != 0)
        {
            while (pos < body.Length)
            {
                if (body.Length - pos < FragmentHeaderSize)
                {
                    return null;
                }

                var fragmentHeader = body.Slice(pos, FragmentHeaderSize);
                var fragmentSize = BinaryPrimitives.ReadUInt16LittleEndian(fragmentHeader[10..]);
                if (fragmentSize < FragmentHeaderSize || fragmentSize > MaxFragmentSize)
                {
                    return null;
                }

                pos += FragmentHeaderSize;
                var dataLength = Math.Min(fragmentSize - FragmentHeaderSize, body.Length - pos);
                var data = body.Slice(pos, dataLength).ToArray();
                pos += dataLength;
                packet.Fragments.Add(new ClientFragment(
                    BinaryPrimitives.ReadUInt32LittleEndian(fragmentHeader),
                    BinaryPrimitives.ReadUInt32LittleEndian(fragmentHeader[4..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(fragmentHeader[8..]),
                    fragmentSize,
                    BinaryPrimitives.ReadUInt16LittleEndian(fragmentHeader[12..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(fragmentHeader[14..]),
                    data,
                    Hash32.Calculate(fragmentHeader) + Hash32.Calculate(data)));
            }
        }

        Span<byte> hashed = stackalloc byte[HeaderSize];
        header.CopyTo(hashed);
        BinaryPrimitives.WriteUInt32LittleEndian(hashed[8..], ChecksumPlaceholder);
        packet.HeaderHash = Hash32.Calculate(hashed);
        return packet;
    }

    /// <summary>
    /// ACE's <c>VerifyCRC</c>: an encrypted packet's recovered key must be in the window (and is then spent); a cleartext
    /// packet's checksum must be the plain sum.
    /// </summary>
    public static bool VerifyCrc(ClientPacket packet, AceCryptoSystemOracle window)
    {
        if ((packet.Flags & EncryptedChecksum) != 0)
        {
            var key = (packet.Checksum - packet.HeaderHash) ^ packet.PayloadHash;
            if (window.Search(key))
            {
                window.ConsumeKey(key);
                return true;
            }

            return false;
        }

        return packet.HeaderHash + packet.PayloadHash == packet.Checksum;
    }
}

/// <summary>A client packet as the fake server sees it.</summary>
internal sealed class ClientPacket
{
    public uint Sequence { get; init; }

    public uint Flags { get; init; }

    public uint Checksum { get; init; }

    public ushort Id { get; init; }

    public ushort Time { get; init; }

    public ushort Size { get; init; }

    public ushort Iteration { get; init; }

    public uint AckSequence { get; set; }

    public IReadOnlyList<uint> RetransmitRequests { get; set; } = [];

    public byte[]? LoginBytes { get; set; }

    public ulong? ConnectResponseCookie { get; set; }

    public float? EchoRequestClientTime { get; set; }

    public List<ClientFragment> Fragments { get; } = [];

    public uint HeaderHash { get; set; }

    public uint OptionalHash { get; set; }

    public uint PayloadHash => unchecked(OptionalHash + Fragments.Aggregate(0u, (sum, f) => unchecked(sum + f.Hash)));

    public bool Has(uint flag) => (Flags & flag) != 0;
}

/// <summary>One client fragment: header fields, its data and its hash (header hash plus data hash).</summary>
internal sealed record ClientFragment(uint Sequence, uint Id, ushort Count, ushort Size, ushort Index, ushort Queue, byte[] Data, uint Hash);

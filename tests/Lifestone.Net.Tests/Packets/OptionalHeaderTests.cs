using System.Buffers.Binary;
using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Support;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.Packets;

/// <summary>
/// Defect 4 of docs/protocol.md §8.2, fixed in N2: the borrowed parser reads EchoResponse (8 bytes, in bit order between
/// EchoRequest and Flow) and refuses the sections that have no defined server-to-client body (LogonServerAddr, Referral,
/// NetError, NetErrorDisconnect) instead of skipping or guessing them. Before the fix, an EchoResponse made every
/// fragment after it parse from the wrong offset.
/// </summary>
public sealed class OptionalHeaderTests
{
    public static TheoryData<uint> SectionsWithNoDefinedBody =>
    [
        (uint)PacketHeaderFlags.LogonServerAddr,
        (uint)PacketHeaderFlags.Referral,
        (uint)PacketHeaderFlags.NetError,
        (uint)PacketHeaderFlags.NetErrorDisconnect,
    ];

    [Fact]
    public void An_echo_response_with_time_sync_parses_and_the_fragments_start_after_it()
    {
        // ACE's write order for a server bundle: AckSequence, TimeSync, EchoResponse (NetworkSession.WriteOptionalHeaders).
        var optional = new byte[4 + 8 + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(optional, 7);
        BinaryPrimitives.WriteDoubleLittleEndian(optional.AsSpan(4), 1234.5);
        BinaryPrimitives.WriteSingleLittleEndian(optional.AsSpan(12), 3.25f);
        BinaryPrimitives.WriteSingleLittleEndian(optional.AsSpan(16), 100.5f);
        byte[] message = [0xB1, 0xF7, 0, 0, 0xA1, 0, 0, 0];
        var fragment = Fragment(5, message);

        var datagram = Seal(
            PacketHeaderFlags.AckSequence | PacketHeaderFlags.TimeSync | PacketHeaderFlags.EchoResponse | PacketHeaderFlags.BlobFragments,
            optional,
            fragment);

        Assert.True(PacketCodec.TryParseBorrowed(datagram, out var packet, out var headerHash, out var payloadHash, out var error));
        Assert.Equal(PacketCodec.DecodeError.None, error);
        var header = packet.Header;
        Assert.True(PacketCodec.VerifyChecksum(in header, headerHash, payloadHash, isaacKey: null));
        Assert.Equal(7u, packet.Optional.AckSequence);
        Assert.Equal(1234.5, packet.Optional.TimeSync);
        Assert.Equal(3.25f, packet.Optional.EchoResponseClientTime);
        Assert.Equal(100.5f, packet.Optional.EchoResponseServerDelta);
        Assert.Equal(optional, packet.Optional.RawBytes.ToArray());

        Assert.Equal(1, packet.FragmentCount);
        foreach (var parsed in packet.Fragments)
        {
            Assert.Equal(5u, parsed.Header.Sequence);
            Assert.Equal(message, parsed.Payload.ToArray());
        }
    }

    [Fact]
    public void Echo_response_is_read_between_echo_request_and_flow()
    {
        var optional = new byte[4 + 8 + 6];
        BinaryPrimitives.WriteSingleLittleEndian(optional, 1.5f);
        BinaryPrimitives.WriteSingleLittleEndian(optional.AsSpan(4), 2.5f);
        BinaryPrimitives.WriteSingleLittleEndian(optional.AsSpan(8), 3.5f);
        BinaryPrimitives.WriteUInt32LittleEndian(optional.AsSpan(12), 0x01020304);
        BinaryPrimitives.WriteUInt16LittleEndian(optional.AsSpan(16), 0x0506);

        var datagram = Seal(PacketHeaderFlags.EchoRequest | PacketHeaderFlags.EchoResponse | PacketHeaderFlags.Flow, optional, []);

        Assert.True(PacketCodec.TryParseBorrowed(datagram, out var packet, out _, out _, out _));
        Assert.Equal(1.5f, packet.Optional.EchoRequestClientTime);
        Assert.Equal(2.5f, packet.Optional.EchoResponseClientTime);
        Assert.Equal(3.5f, packet.Optional.EchoResponseServerDelta);
        Assert.Equal(0x01020304u, packet.Optional.FlowBytes);
        Assert.Equal((ushort)0x0506, packet.Optional.FlowInterval);
    }

    [Fact]
    public void A_truncated_echo_response_is_an_invalid_optional_header()
    {
        var datagram = Seal(PacketHeaderFlags.EchoResponse, new byte[4], []);

        Assert.False(PacketCodec.TryParseBorrowed(datagram, out _, out _, out _, out var error));
        Assert.Equal(PacketCodec.DecodeError.InvalidOptionalHeader, error);
    }

    [Theory]
    [MemberData(nameof(SectionsWithNoDefinedBody))]
    public void A_section_with_no_defined_body_refuses_the_packet_without_throwing(uint flag)
    {
        var section = (PacketHeaderFlags)flag;
        foreach (var flags in new[] { section, section | PacketHeaderFlags.AckSequence, section | PacketHeaderFlags.TimeSync | PacketHeaderFlags.BlobFragments })
        {
            var optional = new byte[16];
            var fragment = (flags & PacketHeaderFlags.BlobFragments) != 0 ? Fragment(1, [1, 2, 3, 4]) : [];

            var datagram = Seal(flags, optional, fragment);

            Assert.False(PacketCodec.TryParseBorrowed(datagram, out _, out _, out _, out var error));
            Assert.Equal(PacketCodec.DecodeError.InvalidOptionalHeader, error);
            Assert.Throws<ArgumentException>(() => PacketCodec.Encode(new PacketHeader { Flags = flags }, optional, outboundIsaac: null));
        }
    }

    [Fact]
    public void The_sections_with_no_defined_body_are_exactly_the_four_flags()
    {
        Assert.Equal(
            PacketHeaderFlags.LogonServerAddr | PacketHeaderFlags.Referral | PacketHeaderFlags.NetError | PacketHeaderFlags.NetErrorDisconnect,
            PacketCodec.UndefinedOptionalSections);
    }

    [Fact]
    public void Encode_measures_an_echo_response()
    {
        var body = new byte[4 + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(body, 9);

        var datagram = PacketCodec.Encode(new PacketHeader { Flags = PacketHeaderFlags.AckSequence | PacketHeaderFlags.EchoResponse }, body, outboundIsaac: null);

        Assert.True(PacketCodec.TryParseBorrowed(datagram, out var packet, out var headerHash, out var payloadHash, out _));
        var header = packet.Header;
        Assert.True(PacketCodec.VerifyChecksum(in header, headerHash, payloadHash, isaacKey: null));
        Assert.Equal(9u, packet.Optional.AckSequence);
    }

    [Fact]
    public void The_owning_decoder_like_ACE_does_not_know_an_echo_response()
    {
        // ACE parses client packets and never receives an EchoResponse, so its PacketHeaderOptional.Unpack has no case
        // for it; the test-side owning decoder mirrors that, which is why the product's parser is the one that changed.
        var optional = new byte[8];
        var datagram = Seal(PacketHeaderFlags.EchoResponse, optional, []);

        Assert.True(PacketCodec.TryParseBorrowed(datagram, out _, out _, out _, out _));
        Assert.Equal(PacketCodec.DecodeError.ChecksumMismatch, OwningPacketDecoder.TryDecode(datagram, inboundIsaac: null).Error);
    }

    /// <summary>A cleartext datagram of <paramref name="optional"/> then <paramref name="fragments"/>, sealed with the product's checksum.</summary>
    private static byte[] Seal(PacketHeaderFlags flags, byte[] optional, byte[] fragments)
    {
        var datagram = new byte[PacketHeader.Size + optional.Length + fragments.Length];
        optional.CopyTo(datagram, PacketHeader.Size);
        fragments.CopyTo(datagram, PacketHeader.Size + optional.Length);
        var header = new PacketHeader { Sequence = 3, Flags = flags, Id = 0x000B, Time = 0x0102, Iteration = 1 };
        PacketCodec.FinalizeInPlace(header, datagram, optional.Length + fragments.Length, optional.Length, outboundIsaac: null);
        return datagram;
    }

    private static byte[] Fragment(uint sequence, byte[] message)
    {
        var fragment = new byte[MessageFragmentHeader.Size + message.Length];
        GameMessageFragment.WriteSingleFragment(fragment, sequence, MessageQueue.Ui, message);
        return fragment;
    }
}

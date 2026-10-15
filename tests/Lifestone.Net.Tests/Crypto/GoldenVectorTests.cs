using System.Buffers.Binary;
using Lifestone.Net.Crypto;
using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Support;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.Crypto;

/// <summary>
/// The golden vectors of docs/protocol.md §10, computed from ACE's own algorithms (Hash32, ISAAC, the packet checksum),
/// against the vendored OpenAC transport: the checksum table, the ISAAC keystream across its first re-scramble, the
/// cleartext ack-only packet and an encrypted LoginComplete packet, byte for byte.
/// </summary>
public sealed class GoldenVectorTests
{
    public static TheoryData<string, uint> Hash32Table => new()
    {
        { "", 0x00000000u },
        { "01 02 03 04", 0x04070201u },
        { "AA BB CC DD EE", 0xCBD1BBAAu },
        { "42", 0x42010000u },
        { "42 7F", 0x42810000u },
        { "01 02 03", 0x01050300u },
    };

    [Theory]
    [MemberData(nameof(Hash32Table))]
    public void Hash32_matches_the_protocol_table(string input, uint expected)
    {
        Assert.Equal(expected, Hash32.Calculate(Hex(input)));
    }

    [Fact]
    public void Isaac_outputs_256_to_259_cross_the_first_rescramble()
    {
        var isaac = new IsaacRandom([0x78, 0x56, 0x34, 0x12]);
        for (var i = 1; i <= 255; i++)
        {
            isaac.Next();
        }

        Assert.Equal([0xDF7CA7E9u, 0xA608787Au, 0x2271E865u, 0xF43B889Eu], [isaac.Next(), isaac.Next(), isaac.Next(), isaac.Next()]);
    }

    [Fact]
    public void Isaac_seed_zero_gives_the_protocol_outputs()
    {
        var isaac = new IsaacRandom([0, 0, 0, 0]);
        Assert.Equal([0x182600F3u, 0x300B4A8Du, 0x301B6622u, 0xB08ACD21u], [isaac.Next(), isaac.Next(), isaac.Next(), isaac.Next()]);
    }

    [Fact]
    public void The_cleartext_ack_only_packet_is_the_protocol_vector()
    {
        // seq 2, flags AckSequence, Id 0, Time 0x1234, Iteration 1, ack 5 (protocol.md §10), sealed as the transport's
        // ack scheduler seals it: the whole body is the optional section.
        var datagram = new byte[PacketHeader.Size + sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(PacketHeader.Size), 5);
        var header = new PacketHeader { Sequence = 2, Flags = PacketHeaderFlags.AckSequence, Id = 0, Time = 0x1234, Iteration = 1 };

        var length = PacketCodec.FinalizeInPlace(header, datagram, sizeof(uint), sizeof(uint), outboundIsaac: null);

        Assert.Equal(24, length);
        Assert.Equal(Hex("02 00 00 00 00 40 00 00 E8 B0 2A CD 00 00 34 12 04 00 01 00 05 00 00 00"), datagram);
    }

    [Fact]
    public void The_encrypted_login_complete_packet_is_the_protocol_vector()
    {
        // Client seed 0x0BADF00D, whose first key is 0x99D25AF6; packet seq 2, flags BlobFragments|EncryptedChecksum, Id 1,
        // Time 0x1234, Iteration 1; fragment seq 1, id 0x80000000, count 1, index 0, queue UI; the body is GameAction
        // 0xF7B1, action sequence 0, LoginComplete 0xA1.
        byte[] seed = [0x0D, 0xF0, 0xAD, 0x0B];
        byte[] message = Hex("B1 F7 00 00 00 00 00 00 A1 00 00 00");
        var datagram = new byte[PacketHeader.Size + MessageFragmentHeader.Size + message.Length];
        var fragmentLength = GameMessageFragment.WriteSingleFragment(datagram.AsSpan(PacketHeader.Size), 1, MessageQueue.Ui, message);
        var header = new PacketHeader
        {
            Sequence = 2,
            Flags = PacketHeaderFlags.BlobFragments | PacketHeaderFlags.EncryptedChecksum,
            Id = 1,
            Time = 0x1234,
            Iteration = 1,
        };

        var length = PacketCodec.FinalizeInPlace(header, datagram, fragmentLength, 0, new IsaacRandom(seed), out var key, out _);

        Assert.Equal(0x99D25AF6u, key);
        Assert.Equal(datagram.Length, length);
        Assert.Equal(
            Hex("02 00 00 00 06 00 00 00 A4 13 BA E6 01 00 34 12 1C 00 01 00 "
                + "01 00 00 00 00 00 00 80 01 00 1C 00 00 00 09 00 "
                + "B1 F7 00 00 00 00 00 00 A1 00 00 00"),
            datagram);

        // ACE's VerifyCRC recovers the key: the independent decoder accepts it with a fresh keystream from the same seed.
        var decoded = OwningPacketDecoder.TryDecode(datagram, new IsaacRandom(seed));
        Assert.True(decoded.IsOk, decoded.Error.ToString());
        Assert.Equal(message, Assert.Single(decoded.Packet!.Fragments).Payload);
    }

    private static byte[] Hex(string text) => text.Length == 0 ? [] : Convert.FromHexString(text.Replace(" ", "", StringComparison.Ordinal));
}

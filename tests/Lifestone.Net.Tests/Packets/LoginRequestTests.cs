// Ported from eriknihlen/OpenAC@1ee5a08 tests/AcDream.Core.Net.Tests/Packets/LoginRequestTests.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Tests.Packets; the
// owning decoder is the test-side Support.OwningPacketDecoder (PacketCodec.TryDecode moved there). N2: the bodies come
// from Support.LoginRequestBodies (the product's span writer) and are read by Support.LoginRequestReader, since Build and
// Parse left the product; Build_Then_Parse_EmptyAccountAndPassword became Write_RefusesAnEmptyAccountOrPassword (an
// empty account or password is now an error).

using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Support;

namespace Lifestone.Net.Tests.Packets;

public class LoginRequestTests
{
    [Fact]
    public void Build_Then_Parse_RoundTripsAllFields()
    {
        var bytes = LoginRequestBodies.Build(
            account: "testaccount",
            password: "testpassword",
            timestamp: 0x12345678u);

        var parsed = LoginRequestReader.Read(bytes);

        Assert.Equal(LoginRequest.RetailClientVersion, parsed.ClientVersion);
        Assert.Equal(LoginRequest.NetAuthType.AccountPassword, parsed.NetAuth);
        Assert.Equal(0u, parsed.AuthFlags);
        Assert.Equal(0x12345678u, parsed.Timestamp);
        Assert.Equal("testaccount", parsed.Account);
        Assert.Equal(string.Empty, parsed.LoginAs);
        Assert.Equal("testpassword", parsed.Password);
        Assert.Null(parsed.GlsTicket);
    }

    [Fact]
    public void Write_RefusesAnEmptyAccountOrPassword()
    {
        Assert.Throws<ArgumentException>(() => LoginRequestBodies.Build(account: string.Empty, password: "p", timestamp: 1));
        Assert.Throws<ArgumentException>(() => LoginRequestBodies.Build(account: "u", password: string.Empty, timestamp: 1));
    }

    [Fact]
    public void Build_BodyLength_ReflectsActualRemainingBytes()
    {
        var bytes = LoginRequestBodies.Build("u", "p", 0);
        var parsed = LoginRequestReader.Read(bytes);

        int clientVersionAndLengthPrefix = 8 + 4;
        int expectedBodyLength = bytes.Length - clientVersionAndLengthPrefix;
        Assert.Equal((uint)expectedBodyLength, parsed.BodyLength);
    }

    [Fact]
    public void Build_TotalWireSizeIsMultipleOfFour()
    {
        // Each field pads to a 4-byte boundary, so the total should land on one.
        var bytes = LoginRequestBodies.Build("testaccount", "testpassword", 42);
        Assert.Equal(0, bytes.Length % 4);
    }

    [Fact]
    public void Build_DifferentCredentials_ProduceDifferentBytes()
    {
        var a = LoginRequestBodies.Build("alice", "alicepw", 1);
        var b = LoginRequestBodies.Build("bob",   "bobpw",   1);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void LoginRequest_EmbeddedInPacket_DecodableByPacketCodec()
    {
        var payload = LoginRequestBodies.Build("testaccount", "testpassword", 1);

        var header = new PacketHeader
        {
            Flags = PacketHeaderFlags.LoginRequest,
            DataSize = (ushort)payload.Length,
        };
        uint headerHash = header.CalculateHeaderHash32();
        uint optionalHash = Lifestone.Net.Crypto.Hash32.Calculate(payload);
        header.Checksum = headerHash + optionalHash;

        byte[] datagram = new byte[PacketHeader.Size + payload.Length];
        header.Pack(datagram);
        payload.CopyTo(datagram.AsSpan(PacketHeader.Size));

        var result = OwningPacketDecoder.TryDecode(datagram, inboundIsaac: null);
        Assert.Equal(PacketCodec.DecodeError.None, result.Error);
        Assert.NotNull(result.Packet);
        Assert.Equal(payload, result.Packet!.BodyBytes);

        var parsed = LoginRequestReader.Read(result.Packet.BodyBytes);
        Assert.Equal("testaccount", parsed.Account);
        Assert.Equal("testpassword", parsed.Password);
    }
}

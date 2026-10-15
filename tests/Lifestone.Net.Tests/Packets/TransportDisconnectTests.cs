// Ported from eriknihlen/OpenAC@1ee5a08 tests/AcDream.Core.Net.Tests/Packets/TransportDisconnectTests.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Tests.Packets; the
// owning decoder is the test-side Support.OwningPacketDecoder (PacketCodec.TryDecode moved there).

using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Support;

namespace Lifestone.Net.Tests.Packets;

public sealed class TransportDisconnectTests
{
    [Fact]
    public void Build_MatchesRetailOptionalHeaderShape()
    {
        byte[] datagram = TransportDisconnect.Build(0x1234, 0x0001);

        Assert.Equal(PacketHeader.Size, datagram.Length);

        PacketHeader header = PacketHeader.Unpack(datagram);
        Assert.Equal(0u, header.Sequence);
        Assert.Equal(PacketHeaderFlags.Disconnect, header.Flags);
        Assert.Equal((ushort)0x1234, header.Id);
        Assert.Equal((ushort)0x0001, header.Iteration);
        Assert.Equal((ushort)0, header.DataSize);

        OwningPacketDecoder.PacketDecodeResult decoded = OwningPacketDecoder.TryDecode(
            datagram,
            inboundIsaac: null);
        Assert.True(decoded.IsOk);
    }
}

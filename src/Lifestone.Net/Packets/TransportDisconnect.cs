// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Packets/TransportDisconnect.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Packets; internal.

namespace Lifestone.Net.Packets;

internal static class TransportDisconnect
{
    public static byte[] Build(ushort networkId, ushort iteration)
    {
        var header = new PacketHeader
        {
            Sequence = 0,
            Flags = PacketHeaderFlags.Disconnect,
            Id = networkId,
            Iteration = iteration,
        };

        return PacketCodec.Encode(
            header,
            ReadOnlySpan<byte>.Empty,
            outboundIsaac: null);
    }
}

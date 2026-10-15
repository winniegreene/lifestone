// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/GameMessageFragment.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Packets
// (from Messages); internal; its GameMessageGroup enum is Lifestone.Net.Types.MessageQueue (same values).

using Lifestone.Net.Types;

namespace Lifestone.Net.Packets;

internal static class GameMessageFragment
{
    public const uint OutboundFragmentId = 0x80000000u;

    public static MessageFragment BuildSingleFragment(
        uint fragmentSequence,
        MessageQueue queue,
        ReadOnlySpan<byte> gameMessageBytes)
    {
        if (gameMessageBytes.Length > MessageFragmentHeader.MaxFragmentDataSize)
            throw new ArgumentException(
                $"game message body ({gameMessageBytes.Length} bytes) exceeds single-fragment capacity " +
                $"({MessageFragmentHeader.MaxFragmentDataSize} bytes).",
                nameof(gameMessageBytes));

        var header = new MessageFragmentHeader
        {
            Sequence  = fragmentSequence,
            Id        = OutboundFragmentId,
            Count     = 1,
            TotalSize = (ushort)(MessageFragmentHeader.Size + gameMessageBytes.Length),
            Index     = 0,
            Queue     = (ushort)queue,
        };

        return new MessageFragment(header, gameMessageBytes.ToArray());
    }

    internal static int WriteSingleFragment(
        Span<byte> destination,
        uint fragmentSequence,
        MessageQueue queue,
        ReadOnlySpan<byte> gameMessageBytes)
        => WriteFragment(destination, fragmentSequence, queue, gameMessageBytes, 0, 1);

    internal static int WriteFragment(
        Span<byte> destination,
        uint fragmentSequence,
        MessageQueue queue,
        ReadOnlySpan<byte> gameMessageBytes,
        ushort index,
        ushort count)
    {
        if (gameMessageBytes.Length
            > MessageFragmentHeader.MaxFragmentDataSize)
        {
            throw new ArgumentException(
                $"game message body ({gameMessageBytes.Length} bytes) exceeds single-fragment capacity "
                + $"({MessageFragmentHeader.MaxFragmentDataSize} bytes).",
                nameof(gameMessageBytes));
        }

        int wireSize =
            MessageFragmentHeader.Size + gameMessageBytes.Length;
        if (destination.Length < wireSize)
        {
            throw new ArgumentException(
                $"destination must be at least {wireSize} bytes",
                nameof(destination));
        }

        var header = new MessageFragmentHeader
        {
            Sequence = fragmentSequence,
            Id = OutboundFragmentId,
            Count = count,
            TotalSize = checked((ushort)wireSize),
            Index = index,
            Queue = (ushort)queue,
        };
        header.Pack(destination);
        gameMessageBytes.CopyTo(
            destination.Slice(MessageFragmentHeader.Size));
        return wireSize;
    }

    public static byte[] Serialize(in MessageFragment fragment)
    {
        byte[] buffer = new byte[MessageFragmentHeader.Size + fragment.Payload.Length];
        fragment.Header.Pack(buffer);
        fragment.Payload.CopyTo(buffer.AsSpan(MessageFragmentHeader.Size));
        return buffer;
    }
}

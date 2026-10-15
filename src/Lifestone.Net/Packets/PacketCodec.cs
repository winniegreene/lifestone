// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Packets/PacketCodec.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Packets;
// internal; the owning decoder (TryDecode, PacketDecodeResult, and the Packet and PacketHeaderOptional classes it fills) is
// not in the product: it moved to tests/Lifestone.Net.Tests/Support/OwningPacketDecoder.cs, so Encode measures the
// optional section with the borrowed parser. Defect 4 (N2): the borrowed parser reads EchoResponse (8 bytes, in bit order
// between EchoRequest and Flow) and refuses the sections with no defined body (UndefinedOptionalSections).

using Lifestone.Net.Crypto;

namespace Lifestone.Net.Packets;

internal static class PacketCodec
{
    public enum DecodeError
    {
        None = 0,
        TooShort,                      // buffer shorter than 20-byte header
        HeaderSizeExceedsBuffer,       // header.DataSize + 20 > buffer.Length
        InvalidOptionalHeader,
        InvalidFragment,
        ChecksumMismatch,
    }

    /// <summary>
    /// The header flags whose optional section has no defined server-to-client body: LogonServerAddr (0x200), Referral
    /// (0x800), NetError (0x100000) and NetErrorDisconnect (0x200000). ACE never sends them and parses no body for them
    /// (<c>NetworkSession.ProcessPacket</c> only tests NetErrorDisconnect), so <see cref="TryParseBorrowed"/> refuses a
    /// packet that carries one as <see cref="DecodeError.InvalidOptionalHeader"/> instead of guessing a size (defect 4).
    /// The receive path counts the refusal and logs one line naming the flags.
    /// </summary>
    public const PacketHeaderFlags UndefinedOptionalSections =
        PacketHeaderFlags.LogonServerAddr
        | PacketHeaderFlags.Referral
        | PacketHeaderFlags.NetError
        | PacketHeaderFlags.NetErrorDisconnect;

    internal static bool TryParseBorrowed(
        ReadOnlyMemory<byte> datagram,
        out BorrowedPacket packet,
        out uint headerHash,
        out uint payloadHash,
        out DecodeError error)
    {
        packet = default;
        headerHash = 0;
        payloadHash = 0;

        ReadOnlySpan<byte> wire = datagram.Span;
        if (wire.Length < PacketHeader.Size)
        {
            error = DecodeError.TooShort;
            return false;
        }

        PacketHeader header = PacketHeader.Unpack(wire);
        int bodyLength = header.DataSize;
        if (wire.Length - PacketHeader.Size < bodyLength)
        {
            error = DecodeError.HeaderSizeExceedsBuffer;
            return false;
        }

        ReadOnlyMemory<byte> body = datagram.Slice(
            PacketHeader.Size,
            bodyLength);
        if (!TryParseBorrowedOptional(
                body,
                header.Flags,
                out BorrowedOptionalHeader optional,
                out int optionalConsumed))
        {
            error = DecodeError.InvalidOptionalHeader;
            return false;
        }

        ReadOnlyMemory<byte> fragmentBytes =
            ReadOnlyMemory<byte>.Empty;
        int fragmentCount = 0;
        uint fragmentHash = 0;
        if (header.HasFlag(PacketHeaderFlags.BlobFragments))
        {
            fragmentBytes = body.Slice(optionalConsumed);
            ReadOnlySpan<byte> remaining = fragmentBytes.Span;
            while (!remaining.IsEmpty)
            {
                if (!MessageFragment.TryParseLayout(
                        remaining,
                        out _,
                        out int payloadLength,
                        out int consumed))
                {
                    error = DecodeError.InvalidFragment;
                    return false;
                }

                fragmentHash +=
                    Hash32.Calculate(
                        remaining.Slice(
                            0,
                            MessageFragmentHeader.Size))
                    + Hash32.Calculate(
                        remaining.Slice(
                            MessageFragmentHeader.Size,
                            payloadLength));
                fragmentCount++;
                remaining = remaining.Slice(consumed);
            }
        }

        headerHash = header.CalculateHeaderHash32();
        payloadHash =
            Hash32.Calculate(optional.RawBytes.Span) + fragmentHash;
        packet = new BorrowedPacket(
            header,
            optional,
            body,
            fragmentBytes,
            fragmentCount);
        error = DecodeError.None;
        return true;
    }

    internal static bool VerifyChecksum(
        in PacketHeader header,
        uint headerHash,
        uint payloadHash,
        uint? isaacKey) =>
        isaacKey is uint key
            ? header.Checksum == headerHash + (key ^ payloadHash)
            : header.Checksum == headerHash + payloadHash;

    private static bool TryParseBorrowedOptional(
        ReadOnlyMemory<byte> bodyMemory,
        PacketHeaderFlags flags,
        out BorrowedOptionalHeader optional,
        out int consumed)
    {
        ReadOnlySpan<byte> body = bodyMemory.Span;
        int position = 0;
        uint ackSequence = 0;
        double timeSync = 0;
        float echoRequestClientTime = 0;
        float echoResponseClientTime = 0;
        float echoResponseServerDelta = 0;
        uint flowBytes = 0;
        ushort flowInterval = 0;
        double connectRequestServerTime = 0;
        ulong connectRequestCookie = 0;
        uint connectRequestClientId = 0;
        uint connectRequestServerSeed = 0;
        uint connectRequestClientSeed = 0;
        int retransmitOffset = 0;
        int retransmitCount = 0;
        int rejectOffset = 0;
        int rejectCount = 0;

        // Defect 4 (docs/protocol.md §8.2): these sections have no body ACE ever sends or parses, so their size is
        // unknown. Guessing one would misread every byte after it; the packet is refused instead.
        if ((flags & UndefinedOptionalSections) != 0)
        {
            return Invalid(out optional, out consumed);
        }

        if (HasFlag(flags, PacketHeaderFlags.ServerSwitch)
            && !Take(body, ref position, 8))
        {
            return Invalid(out optional, out consumed);
        }

        if (HasFlag(flags, PacketHeaderFlags.RequestRetransmit))
        {
            if (!Take(body, ref position, 4))
                return Invalid(out optional, out consumed);
            uint count = System.Buffers.Binary.BinaryPrimitives
                .ReadUInt32LittleEndian(
                    body.Slice(position - 4));
            if (count > 1024
                || body.Length - position < (int)count * 4)
            {
                return Invalid(out optional, out consumed);
            }

            retransmitOffset = position;
            retransmitCount = checked((int)count);
            position += retransmitCount * 4;
        }

        if (HasFlag(flags, PacketHeaderFlags.RejectRetransmit))
        {
            if (!Take(body, ref position, 4))
                return Invalid(out optional, out consumed);
            uint count = System.Buffers.Binary.BinaryPrimitives
                .ReadUInt32LittleEndian(
                    body.Slice(position - 4));
            if (count > 1024
                || body.Length - position < (int)count * 4)
            {
                return Invalid(out optional, out consumed);
            }

            rejectOffset = position;
            rejectCount = checked((int)count);
            position += rejectCount * 4;
        }

        if (HasFlag(flags, PacketHeaderFlags.AckSequence))
        {
            if (!Take(body, ref position, 4))
                return Invalid(out optional, out consumed);
            ackSequence =
                System.Buffers.Binary.BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        body.Slice(position - 4));
        }

        if (HasFlag(flags, PacketHeaderFlags.LoginRequest))
        {
            position = body.Length;
            optional = BuildOptional(
                bodyMemory,
                position,
                ackSequence,
                timeSync,
                echoRequestClientTime,
                echoResponseClientTime,
                echoResponseServerDelta,
                flowBytes,
                flowInterval,
                connectRequestServerTime,
                connectRequestCookie,
                connectRequestClientId,
                connectRequestServerSeed,
                connectRequestClientSeed,
                retransmitOffset,
                retransmitCount,
                rejectOffset,
                rejectCount);
            consumed = position;
            return true;
        }

        if (HasFlag(flags, PacketHeaderFlags.WorldLoginRequest)
            && !Take(body, ref position, 8))
        {
            return Invalid(out optional, out consumed);
        }

        if (HasFlag(flags, PacketHeaderFlags.ConnectRequest))
        {
            if (body.Length - position < 32)
                return Invalid(out optional, out consumed);

            connectRequestServerTime =
                BitConverter.Int64BitsToDouble(
                    System.Buffers.Binary.BinaryPrimitives
                        .ReadInt64LittleEndian(
                            body.Slice(position)));
            connectRequestCookie =
                System.Buffers.Binary.BinaryPrimitives
                    .ReadUInt64LittleEndian(
                        body.Slice(position + 8));
            connectRequestClientId =
                System.Buffers.Binary.BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        body.Slice(position + 16));
            connectRequestServerSeed =
                System.Buffers.Binary.BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        body.Slice(position + 20));
            connectRequestClientSeed =
                System.Buffers.Binary.BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        body.Slice(position + 24));
            position += 32;
        }

        if (HasFlag(flags, PacketHeaderFlags.ConnectResponse)
            && !Take(body, ref position, 8))
        {
            return Invalid(out optional, out consumed);
        }

        if (HasFlag(flags, PacketHeaderFlags.CICMDCommand)
            && !Take(body, ref position, 8))
        {
            return Invalid(out optional, out consumed);
        }

        if (HasFlag(flags, PacketHeaderFlags.TimeSync))
        {
            if (!Take(body, ref position, 8))
                return Invalid(out optional, out consumed);
            timeSync = BitConverter.Int64BitsToDouble(
                System.Buffers.Binary.BinaryPrimitives
                    .ReadInt64LittleEndian(
                        body.Slice(position - 8)));
        }

        if (HasFlag(flags, PacketHeaderFlags.EchoRequest))
        {
            if (!Take(body, ref position, 4))
                return Invalid(out optional, out consumed);
            echoRequestClientTime =
                System.Buffers.Binary.BinaryPrimitives
                    .ReadSingleLittleEndian(
                        body.Slice(position - 4));
        }

        // Defect 4: EchoResponse (0x04000000) is 8 bytes, the echoed f32 client time and the f32 server-minus-client
        // time, in bit order between EchoRequest and Flow (ACE NetworkSession.WriteOptionalHeaders writes it last).
        if (HasFlag(flags, PacketHeaderFlags.EchoResponse))
        {
            if (!Take(body, ref position, 8))
                return Invalid(out optional, out consumed);
            echoResponseClientTime =
                System.Buffers.Binary.BinaryPrimitives
                    .ReadSingleLittleEndian(
                        body.Slice(position - 8));
            echoResponseServerDelta =
                System.Buffers.Binary.BinaryPrimitives
                    .ReadSingleLittleEndian(
                        body.Slice(position - 4));
        }

        if (HasFlag(flags, PacketHeaderFlags.Flow))
        {
            if (!Take(body, ref position, 6))
                return Invalid(out optional, out consumed);
            flowBytes =
                System.Buffers.Binary.BinaryPrimitives
                    .ReadUInt32LittleEndian(
                        body.Slice(position - 6));
            flowInterval =
                System.Buffers.Binary.BinaryPrimitives
                    .ReadUInt16LittleEndian(
                        body.Slice(position - 2));
        }

        optional = BuildOptional(
            bodyMemory,
            position,
            ackSequence,
            timeSync,
            echoRequestClientTime,
            echoResponseClientTime,
            echoResponseServerDelta,
            flowBytes,
            flowInterval,
            connectRequestServerTime,
            connectRequestCookie,
            connectRequestClientId,
            connectRequestServerSeed,
            connectRequestClientSeed,
            retransmitOffset,
            retransmitCount,
            rejectOffset,
            rejectCount);
        consumed = position;
        return true;
    }

    private static BorrowedOptionalHeader BuildOptional(
        ReadOnlyMemory<byte> body,
        int consumed,
        uint ackSequence,
        double timeSync,
        float echoRequestClientTime,
        float echoResponseClientTime,
        float echoResponseServerDelta,
        uint flowBytes,
        ushort flowInterval,
        double connectRequestServerTime,
        ulong connectRequestCookie,
        uint connectRequestClientId,
        uint connectRequestServerSeed,
        uint connectRequestClientSeed,
        int retransmitOffset,
        int retransmitCount,
        int rejectOffset,
        int rejectCount) =>
        new(
            ackSequence,
            timeSync,
            echoRequestClientTime,
            echoResponseClientTime,
            echoResponseServerDelta,
            flowBytes,
            flowInterval,
            connectRequestServerTime,
            connectRequestCookie,
            connectRequestClientId,
            connectRequestServerSeed,
            connectRequestClientSeed,
            body.Slice(0, consumed),
            retransmitCount == 0
                ? ReadOnlyMemory<byte>.Empty
                : body.Slice(
                    retransmitOffset,
                    retransmitCount * 4),
            retransmitCount,
            rejectCount == 0
                ? ReadOnlyMemory<byte>.Empty
                : body.Slice(
                    rejectOffset,
                    rejectCount * 4),
            rejectCount);

    private static bool Invalid(
        out BorrowedOptionalHeader optional,
        out int consumed)
    {
        optional = default;
        consumed = 0;
        return false;
    }

    private static bool HasFlag(
        PacketHeaderFlags all,
        PacketHeaderFlags bit) =>
        (all & bit) != 0;

    private static bool Take(
        ReadOnlySpan<byte> body,
        ref int position,
        int count)
    {
        if (body.Length - position < count)
            return false;
        position += count;
        return true;
    }

    public static byte[] Encode(PacketHeader header, ReadOnlySpan<byte> body, IsaacRandom? outboundIsaac)
    {
        byte[] datagram = new byte[PacketHeader.Size + body.Length];
        body.CopyTo(datagram.AsSpan(PacketHeader.Size));
        if (!TryParseBorrowedOptional(
                datagram.AsMemory(PacketHeader.Size),
                header.Flags,
                out _,
                out int optionalLen))
        {
            throw new ArgumentException("body's optional section is malformed", nameof(body));
        }

        FinalizeInPlace(
            header,
            datagram,
            body.Length,
            optionalLen,
            outboundIsaac);
        return datagram;
    }

    internal static int FinalizeInPlace(
        PacketHeader header,
        Span<byte> datagram,
        int bodyLength,
        int optionalLength,
        IsaacRandom? outboundIsaac) =>
        FinalizeInPlace(
            header,
            datagram,
            bodyLength,
            optionalLength,
            outboundIsaac,
            out _,
            out _);

    internal static int FinalizeInPlace(
        PacketHeader header,
        Span<byte> datagram,
        int bodyLength,
        int optionalLength,
        IsaacRandom? outboundIsaac,
        out uint isaacKeyUsed,
        out uint sealedChecksum)
    {
        if ((uint)bodyLength > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bodyLength));
        }

        int datagramLength = checked(PacketHeader.Size + bodyLength);
        if (datagram.Length < datagramLength)
        {
            throw new ArgumentException(
                $"datagram must be at least {datagramLength} bytes",
                nameof(datagram));
        }

        if ((uint)optionalLength > (uint)bodyLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(optionalLength));
        }

        ReadOnlySpan<byte> body = datagram.Slice(
            PacketHeader.Size,
            bodyLength);
        uint payloadHash = CalculatePayloadHash(
            body,
            header.Flags,
            optionalLength);

        header.DataSize = checked((ushort)bodyLength);
        uint headerHash = header.CalculateHeaderHash32();
        if (header.HasFlag(PacketHeaderFlags.EncryptedChecksum))
        {
            if (outboundIsaac is null)
            {
                throw new InvalidOperationException(
                    "EncryptedChecksum flag set but no ISAAC keystream provided");
            }

            uint isaacKey = outboundIsaac.Next();
            isaacKeyUsed = isaacKey;
            sealedChecksum = isaacKey ^ payloadHash;
        }
        else
        {
            isaacKeyUsed = 0;
            sealedChecksum = payloadHash;
        }

        header.Checksum = headerHash + sealedChecksum;
        header.Pack(datagram);
        return datagramLength;
    }

    private static uint CalculatePayloadHash(
        ReadOnlySpan<byte> body,
        PacketHeaderFlags flags,
        int optionalLength)
    {
        uint optionalHash = Hash32.Calculate(
            body.Slice(0, optionalLength));
        if ((flags & PacketHeaderFlags.BlobFragments) == 0)
        {
            if (optionalLength != body.Length)
            {
                throw new ArgumentException(
                    "non-fragment body contains bytes outside the optional section",
                    nameof(body));
            }

            return optionalHash;
        }

        uint fragmentHash = 0;
        ReadOnlySpan<byte> remaining =
            body.Slice(optionalLength);
        while (!remaining.IsEmpty)
        {
            if (!MessageFragment.TryParseLayout(
                    remaining,
                    out _,
                    out int payloadLength,
                    out int consumed))
            {
                throw new ArgumentException(
                    "body contains a malformed fragment",
                    nameof(body));
            }

            fragmentHash +=
                Hash32.Calculate(
                    remaining.Slice(
                        0,
                        MessageFragmentHeader.Size))
                + Hash32.Calculate(
                    remaining.Slice(
                        MessageFragmentHeader.Size,
                        payloadLength));
            remaining = remaining.Slice(consumed);
        }

        return optionalHash + fragmentHash;
    }

    public static uint CalculateFragmentHash32(in MessageFragment frag)
    {
        Span<byte> headerBuf = stackalloc byte[MessageFragmentHeader.Size];
        frag.Header.Pack(headerBuf);
        return Hash32.Calculate(headerBuf) + Hash32.Calculate(frag.Payload);
    }
}

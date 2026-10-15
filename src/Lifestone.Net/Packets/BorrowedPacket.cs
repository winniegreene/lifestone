// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Packets/BorrowedPacket.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Packets;
// BorrowedOptionalHeader carries the EchoResponse fields (defect 4, N2).

namespace Lifestone.Net.Packets;

internal readonly record struct BorrowedPacket(
    PacketHeader Header,
    BorrowedOptionalHeader Optional,
    ReadOnlyMemory<byte> Body,
    ReadOnlyMemory<byte> FragmentBytes,
    int FragmentCount)
{
    public BorrowedFragmentEnumerable Fragments =>
        new(FragmentBytes);
}

internal readonly record struct BorrowedOptionalHeader(
    uint AckSequence,
    double TimeSync,
    float EchoRequestClientTime,
    float EchoResponseClientTime,
    float EchoResponseServerDelta,
    uint FlowBytes,
    ushort FlowInterval,
    double ConnectRequestServerTime,
    ulong ConnectRequestCookie,
    uint ConnectRequestClientId,
    uint ConnectRequestServerSeed,
    uint ConnectRequestClientSeed,
    ReadOnlyMemory<byte> RawBytes,
    ReadOnlyMemory<byte> RetransmitRequestBytes,
    int RetransmitRequestCount,
    ReadOnlyMemory<byte> RejectRetransmitBytes,
    int RejectRetransmitCount);

internal readonly record struct BorrowedMessageFragment(
    MessageFragmentHeader Header,
    ReadOnlyMemory<byte> Payload);

internal readonly struct BorrowedFragmentEnumerable(
    ReadOnlyMemory<byte> encoded)
{
    public Enumerator GetEnumerator() => new(encoded);

    internal struct Enumerator(ReadOnlyMemory<byte> remaining)
    {
        private ReadOnlyMemory<byte> _remaining = remaining;

        public BorrowedMessageFragment Current { get; private set; }

        public bool MoveNext()
        {
            if (_remaining.IsEmpty)
                return false;

            if (!MessageFragment.TryParseBorrowed(
                    _remaining,
                    out BorrowedMessageFragment fragment,
                    out int consumed))
            {
                throw new InvalidOperationException(
                    "validated packet contained an invalid fragment");
            }

            Current = fragment;
            _remaining = _remaining.Slice(consumed);
            return true;
        }
    }
}

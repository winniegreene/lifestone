// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Transport/SentPacketStore.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Transport. N8: a cached
// packet remembers when it was last put on the wire (LastSentAt, MarkSent), for the tail probe.

using System.Buffers;

namespace Lifestone.Net.Transport;

internal sealed class SentPacketStore : IDisposable
{
    internal readonly struct CachedPacket(
        uint sequence,
        byte[] buffer,
        int bodyLength,
        uint sealedChecksum,
        uint isaacKey,
        bool hasFragments,
        long lastSentAt = 0)
    {
        public uint Sequence { get; } = sequence;

        /// <summary>Rented wire buffer: header [0..20), body [20..20+BodyLength).</summary>
        public byte[] Buffer { get; } = buffer;

        public int BodyLength { get; } = bodyLength;

        public uint SealedChecksum { get; } = sealedChecksum;

        public uint IsaacKey { get; } = isaacKey;

        public bool HasFragments { get; } = hasFragments;

        /// <summary>N8: the transport timestamp of the packet's last transmission, first send or resend.</summary>
        public long LastSentAt { get; } = lastSentAt;

        /// <summary>The same packet, last sent at <paramref name="timestamp"/>.</summary>
        public CachedPacket SentAt(long timestamp) =>
            new(Sequence, Buffer, BodyLength, SealedChecksum, IsaacKey, HasFragments, timestamp);
    }

    private readonly ArrayPool<byte> _pool;
    private readonly Queue<CachedPacket> _fifo = new();
    private readonly Dictionary<uint, CachedPacket> _bySequence = new();

    public SentPacketStore(ArrayPool<byte>? pool = null) =>
        _pool = pool ?? ArrayPool<byte>.Shared;

    public int Count => _fifo.Count;

    public void Add(in CachedPacket packet, int optionalLength)
    {
        if (optionalLength != 0)
        {
            throw new InvalidOperationException(
                "reliable packets must not carry optional headers; the sent-packet "
                + "store keeps only the headerless body for retransmission");
        }

        _bySequence.Add(packet.Sequence, packet);
        _fifo.Enqueue(packet);
    }

    public bool Contains(uint sequence) => _bySequence.ContainsKey(sequence);

    public bool TryGet(uint sequence, out CachedPacket packet) =>
        _bySequence.TryGetValue(sequence, out packet);

    /// <summary>N8: records that the packet <paramref name="sequence"/> was put on the wire again at
    /// <paramref name="timestamp"/> (the FIFO's copy is only used to flush, so it keeps its first time).</summary>
    public void MarkSent(uint sequence, long timestamp)
    {
        if (_bySequence.TryGetValue(sequence, out CachedPacket packet))
            _bySequence[sequence] = packet.SentAt(timestamp);
    }

    public void FlushOlderThan(uint watermark)
    {
        while (_fifo.TryPeek(out CachedPacket head)
               && SequenceMath.IsNewer(watermark, head.Sequence))
        {
            _fifo.Dequeue();
            _bySequence.Remove(head.Sequence);
            _pool.Return(head.Buffer);
        }
    }

    /// <summary>Return every rented buffer to the pool.</summary>
    public void Dispose()
    {
        while (_fifo.TryDequeue(out CachedPacket entry))
            _pool.Return(entry.Buffer);
        _bySequence.Clear();
    }
}

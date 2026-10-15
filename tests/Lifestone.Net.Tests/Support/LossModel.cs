using System.Buffers.Binary;

namespace Lifestone.Net.Tests.Support;

/// <summary>
/// A seeded loss model for one direction of the <see cref="InMemoryNetwork"/> (m2-plan §3.3 N4): each datagram is
/// dropped, held back to swap with the next one, or forwarded (and then perhaps duplicated), by fixed percentages from
/// a fixed seed, so a lossy run replays exactly.
/// </summary>
/// <remarks>
/// <para><b>Swaps never span two sequences.</b> A held datagram is released right after the next datagram is handled
/// (forwarded or dropped), or after 20 ms when no other comes, so a reorder moves a packet by one place and never makes
/// ACE NAK a packet that was merely late (ACE NAKs only a gap of two).</para>
/// <para><b>It records duplicate deliveries.</b> For client-to-server traffic it remembers every encrypted packet
/// sequence it forwarded; <see cref="DuplicateEncryptedDeliveries"/> counts the forwards of a sequence already
/// forwarded. Each such delivery spends an already-spent ISAAC key at ACE, so it is the exact number of CRC errors ACE
/// counts (the soak oracle of N8 and N12).</para>
/// <para><b>Thread-safe</b> (N8): a hold timer on a real clock fires on a pool thread while the owner submits, so
/// <see cref="Submit"/> and <see cref="ReleaseHeld"/> run under one lock. The forward callbacks run inside it and must
/// not call back into the model.</para>
/// </remarks>
internal sealed class LossModel
{
    /// <summary>How long a held datagram waits for a successor.</summary>
    public static readonly TimeSpan HoldLimit = TimeSpan.FromMilliseconds(20);

    private readonly object _gate = new();
    private readonly Random _random;
    private readonly HashSet<uint> _forwardedEncrypted = [];
    private Held? _held;

    /// <summary>A model with the given percentages (0 to 100), drawn from <paramref name="seed"/>.</summary>
    public LossModel(int seed, double dropPercent, double swapPercent, double duplicatePercent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dropPercent);
        ArgumentOutOfRangeException.ThrowIfNegative(swapPercent);
        ArgumentOutOfRangeException.ThrowIfNegative(duplicatePercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dropPercent + swapPercent, 100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(duplicatePercent, 100);
        _random = new Random(seed);
        DropPercent = dropPercent;
        SwapPercent = swapPercent;
        DuplicatePercent = duplicatePercent;
    }

    /// <summary>The percentage of datagrams dropped.</summary>
    public double DropPercent { get; }

    /// <summary>The percentage of datagrams held back to swap with the next.</summary>
    public double SwapPercent { get; }

    /// <summary>The percentage of forwarded datagrams forwarded twice.</summary>
    public double DuplicatePercent { get; }

    /// <summary>Datagrams submitted.</summary>
    public long Submitted { get; private set; }

    /// <summary>Datagrams dropped.</summary>
    public long Dropped { get; private set; }

    /// <summary>Datagrams that were held and released after their successor (or the 20 ms limit).</summary>
    public long Swapped { get; private set; }

    /// <summary>Extra copies forwarded.</summary>
    public long Duplicated { get; private set; }

    /// <summary>Forwards of an encrypted client packet whose sequence had already been forwarded (see the remarks).</summary>
    public long DuplicateEncryptedDeliveries { get; private set; }

    /// <summary>True while a datagram is held.</summary>
    public bool IsHolding => _held is not null;

    /// <summary>
    /// Handles one datagram: calls <paramref name="forward"/> zero, one or two times for it, and releases a held
    /// datagram after it. <paramref name="time"/> times the 20 ms limit of a datagram this call holds.
    /// </summary>
    public void Submit(byte[] datagram, Action<byte[]> forward, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(datagram);
        ArgumentNullException.ThrowIfNull(forward);
        ArgumentNullException.ThrowIfNull(time);
        lock (_gate)
        {
            Submitted++;

            if (_held is { } held)
            {
                // The successor of a held datagram is handled now, then the held one follows it: one swap, never two.
                _held = null;
                held.Timer?.Dispose();
                Handle(datagram, forward, allowHold: false, time);
                Swapped++;
                Forward(held.Datagram, held.Forward);
                return;
            }

            Handle(datagram, forward, allowHold: true, time);
        }
    }

    /// <summary>Releases a held datagram now (a test's flush).</summary>
    public void ReleaseHeld()
    {
        lock (_gate)
        {
            if (_held is { } held)
            {
                Release(held);
            }
        }
    }

    /// <summary>The hold timer of <paramref name="held"/>: releases it if it is still the one held (a successor may have
    /// released it, and another may be held now, while a pool-thread timer waited for the lock).</summary>
    private void ReleaseIfHeld(Held held)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_held, held))
            {
                Release(held);
            }
        }
    }

    private void Release(Held held)
    {
        _held = null;
        held.Timer?.Dispose();
        Swapped++;
        Forward(held.Datagram, held.Forward);
    }

    private void Handle(byte[] datagram, Action<byte[]> forward, bool allowHold, TimeProvider time)
    {
        var roll = _random.NextDouble() * 100;
        if (roll < DropPercent)
        {
            Dropped++;
            return;
        }

        if (allowHold && roll < DropPercent + SwapPercent)
        {
            var held = new Held(datagram, forward);
            _held = held;
            held.Timer = time.CreateTimer(state => ReleaseIfHeld((Held)state!), held, HoldLimit, Timeout.InfiniteTimeSpan);
            return;
        }

        Forward(datagram, forward);
        if (_random.NextDouble() * 100 < DuplicatePercent)
        {
            Duplicated++;
            Forward(datagram, forward);
        }
    }

    private void Forward(byte[] datagram, Action<byte[]> forward)
    {
        // Header: sequence at 0, flags at 4; 0x2 is EncryptedChecksum.
        if (datagram.Length >= 20 && (BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(4)) & 0x2) != 0
            && !_forwardedEncrypted.Add(BinaryPrimitives.ReadUInt32LittleEndian(datagram)))
        {
            DuplicateEncryptedDeliveries++;
        }

        forward(datagram);
    }

    private sealed class Held(byte[] datagram, Action<byte[]> forward)
    {
        public byte[] Datagram { get; } = datagram;

        public Action<byte[]> Forward { get; } = forward;

        public ITimer? Timer { get; set; }
    }
}

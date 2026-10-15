// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Transport/InboundSequenceTracker.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Transport. N8: an
// admission records whether it advanced the highest sequence, and Reject undoes the admission of a packet whose checksum
// failed (OpenAC only reparked an encrypted packet's key), so a corrupt or forged datagram cannot move the window.

using System.Buffers.Binary;
using Lifestone.Net.Crypto;

namespace Lifestone.Net.Transport;

/// <remarks>
/// <b>Rejected admissions (N8).</b> A packet is admitted (its key drawn, the gap below it parked) before its checksum can
/// be checked, because the key depends on the sequence. OpenAC kept a failed packet's effects: a datagram whose sequence
/// was corrupt or forged a few thousand ahead moved <see cref="HighestIdReceived"/> there and parked a key for every id
/// in between, which the client then NAKed and the server refused, after which the real packets were dropped as
/// duplicates. <see cref="Reject"/> undoes an admission that advanced the window: the ids it parked and its own key go
/// back to the reclaimed words in draw order, to be handed out again to the same ids, and the highest sequence returns.
/// A failed packet below the highest only gets its parked key back, as before.
/// </remarks>
internal sealed class InboundSequenceTracker
{
    internal const uint AceInitialWatermark = 1;

    internal const uint SanityWindow = 0x7FFF;

    private readonly IsaacRandom _inboundIsaac;
    private readonly TransportStats _stats;

    internal readonly record struct ParkedWord(uint Word, ulong DrawOrder);

    private readonly SortedDictionary<uint, ParkedWord> _nakSet = new();

    private readonly PriorityQueue<uint, ulong> _reclaimedWords = new();

    /// <summary>Scratch for the reject bubble-shift — reused, never
    /// allocated on the steady-state path.</summary>
    private readonly List<uint> _rejectShiftScratch = new();

    /// <summary>Monotonic ordinal stamped on every fresh inbound ISAAC
    /// draw; positions in this sequence ARE server stream positions minus
    /// the reclaimed (never-drawn-server-side) entries.</summary>
    private ulong _drawOrdinal;

    public uint HighestIdReceived { get; private set; }

    public int NakCount => _nakSet.Count;

    public int ReclaimedWordCount => _reclaimedWords.Count;

    public InboundSequenceTracker(
        IsaacRandom inboundIsaac,
        TransportStats stats,
        uint initialWatermark = AceInitialWatermark)
    {
        ArgumentNullException.ThrowIfNull(inboundIsaac);
        ArgumentNullException.ThrowIfNull(stats);
        _inboundIsaac = inboundIsaac;
        _stats = stats;
        HighestIdReceived = initialWatermark;
    }

    /// <summary>The outcome of <see cref="Admit"/>.</summary>
    /// <param name="Drop">True when the packet is dropped unchecked (a duplicate, or beyond the sanity window).</param>
    /// <param name="VerifyKey">The key to verify an encrypted packet with; null for a cleartext one.</param>
    /// <param name="VerifyKeyDrawOrder">The key's draw ordinal.</param>
    /// <param name="Advanced">N8: true when the admission moved <see cref="HighestIdReceived"/> up.</param>
    /// <param name="PreviousHighest">N8: <see cref="HighestIdReceived"/> before the admission.</param>
    public readonly record struct Admission(
        bool Drop,
        uint? VerifyKey,
        ulong VerifyKeyDrawOrder,
        bool Advanced = false,
        uint PreviousHighest = 0)
    {
        public static Admission Dropped => new(true, null, 0);

        public static Admission Process(
            uint? verifyKey,
            ulong verifyKeyDrawOrder = ulong.MaxValue) =>
            new(false, verifyKey, verifyKeyDrawOrder);
    }

    public Admission Admit(uint sequence, bool encrypted)
    {
        if (SequenceMath.IsNewer(
                sequence,
                unchecked(HighestIdReceived + SanityWindow)))
        {
            _stats.InboundSanityDrops++;
            return Admission.Dropped;
        }

        bool newer = SequenceMath.IsNewer(sequence, HighestIdReceived);
        uint previousHighest = HighestIdReceived;

        ParkedWord? parkedKey = null;
        if (encrypted && !newer)
        {
            if (!_nakSet.Remove(sequence, out ParkedWord parked))
            {
                _stats.InboundDupsDropped++;
                return Admission.Dropped;
            }

            parkedKey = parked;
        }

        if (newer)
        {
            uint end = encrypted ? sequence : unchecked(sequence + 1u);
            for (uint id = unchecked(HighestIdReceived + 1u);
                 id != end;
                 id = unchecked(id + 1u))
            {
                if (id != 0)
                    AddNakked(id);
            }

            HighestIdReceived = sequence;
        }

        if (!encrypted)
            return Admission.Process(null) with { Advanced = newer, PreviousHighest = previousHighest };

        ParkedWord own = parkedKey ?? NextWord();
        return Admission.Process(own.Word, own.DrawOrder) with { Advanced = newer, PreviousHighest = previousHighest };
    }

    /// <summary>
    /// N8: undoes <paramref name="admission"/> of the packet <paramref name="sequence"/>, whose checksum failed (see the
    /// remarks): an admission that advanced the window is rolled back, and any other gives back the key it took.
    /// </summary>
    public void Reject(uint sequence, in Admission admission)
    {
        if (admission.Drop)
            return;

        if (!admission.Advanced)
        {
            if (admission.VerifyKey is uint key)
                ReparkKey(sequence, key, admission.VerifyKeyDrawOrder);
            return;
        }

        bool encrypted = admission.VerifyKey.HasValue;
        uint end = encrypted ? sequence : unchecked(sequence + 1u);
        for (uint id = unchecked(admission.PreviousHighest + 1u);
             id != end;
             id = unchecked(id + 1u))
        {
            if (_nakSet.Remove(id, out ParkedWord parked))
                _reclaimedWords.Enqueue(parked.Word, parked.DrawOrder);
        }

        if (admission.VerifyKey is uint own)
            _reclaimedWords.Enqueue(own, admission.VerifyKeyDrawOrder);

        HighestIdReceived = admission.PreviousHighest;
        _stats.AdmissionsRejected++;
    }

    public void ReparkKey(uint sequence, uint key, ulong drawOrder)
    {
        if (_nakSet.ContainsKey(sequence))
            return;

        _nakSet.Add(sequence, new ParkedWord(key, drawOrder));
        _stats.KeysParked++;
    }

    public void OnRejectRetransmit(ReadOnlySpan<byte> idBytes, int count)
    {
        if (count <= 0 || idBytes.Length < count * 4)
            return;

        for (int i = 0; i < count; i++)
        {
            _nakSet.Remove(
                BinaryPrimitives.ReadUInt32LittleEndian(
                    idBytes.Slice(i * 4)));
        }
    }

    public void CopyNakkedSequencesAscending(
        List<uint> destination,
        int maxCount = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        foreach (uint sequence in _nakSet.Keys)
        {
            if (destination.Count >= maxCount)
                break;
            destination.Add(sequence);
        }
    }

    public void OnCleartextRejectSequence(uint sequence)
    {
        if (!_nakSet.Remove(sequence, out ParkedWord reclaimed))
            return;

        // Everything drawn after the mis-park sits one server position
        // ahead. Ascending draw order ⇔ ascending wrap-safe id inside the
        // set, so ordering by draw ordinal is both wrap-proof and exactly
        // "the ids newer than the reject".
        _rejectShiftScratch.Clear();
        foreach (KeyValuePair<uint, ParkedWord> entry in _nakSet)
        {
            if (entry.Value.DrawOrder > reclaimed.DrawOrder)
                _rejectShiftScratch.Add(entry.Key);
        }

        _rejectShiftScratch.Sort(
            (a, b) => _nakSet[a].DrawOrder.CompareTo(_nakSet[b].DrawOrder));

        ParkedWord carry = reclaimed;
        foreach (uint id in _rejectShiftScratch)
        {
            ParkedWord displaced = _nakSet[id];
            _nakSet[id] = carry;
            carry = displaced;
        }

        _reclaimedWords.Enqueue(carry.Word, carry.DrawOrder);
        _stats.RejectWordsReclaimed++;
    }

    private ParkedWord NextWord()
    {
        if (_reclaimedWords.TryDequeue(out uint word, out ulong order))
            return new ParkedWord(word, order);

        return new ParkedWord(_inboundIsaac.Next(), ++_drawOrdinal);
    }

    private void AddNakked(uint sequence)
    {
        if (_nakSet.ContainsKey(sequence))
            return;

        _nakSet.Add(sequence, NextWord());
        _stats.KeysParked++;
    }
}

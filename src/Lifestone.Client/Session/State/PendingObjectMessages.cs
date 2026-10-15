using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.State;

/// <summary>Which SmartBox message a <see cref="ParkedMessage"/> holds.</summary>
internal enum ParkedKind : byte
{
    /// <summary>F745 ObjectCreate, or F7DB UpdateObject when <see cref="ParkedMessage.IsUpdate"/>.</summary>
    Create,

    /// <summary>F747 ObjectDelete.</summary>
    Delete,

    /// <summary>F748 UpdatePosition.</summary>
    Position,

    /// <summary>F74C UpdateMotion.</summary>
    Motion,

    /// <summary>F74E VectorUpdate.</summary>
    Vector,

    /// <summary>F74B SetState.</summary>
    State,

    /// <summary>F749 ParentEvent.</summary>
    Parent,

    /// <summary>F74A PickupEvent.</summary>
    Pickup,

    /// <summary>F625 ObjDescEvent.</summary>
    ObjDesc,

    /// <summary>F750 Sound.</summary>
    Sound,

    /// <summary>F755 PlayEffect.</summary>
    Script,

    /// <summary>F751 PlayerTeleport (held only while there is no player yet).</summary>
    Teleport,
}

/// <summary>
/// A SmartBox message the reducer could not apply yet: its object (or the parent it names) does not exist, or the
/// message belongs to a newer instance than the one held, or no player exists yet. Replayed through the same handler
/// when its object is created (or the player is announced).
/// </summary>
/// <param name="Kind">Which message.</param>
/// <param name="Key">The object whose creation replays it (<see cref="PendingObjectMessages.WaitingForPlayer"/> for a message
/// held until F746 PlayerCreate).</param>
/// <param name="ObjectGuid">The object the message is about (for a ParentEvent, the child).</param>
/// <param name="ParkedAt">When it was first parked, in session-clock seconds (kept when it is parked again).</param>
/// <param name="Context">Where it came from, for the replayed handler call.</param>
/// <param name="Message">The decoded message (net's record; a struct one boxed: parking is the rare path).</param>
/// <param name="IsUpdate">For <see cref="ParkedKind.Create"/>, true for F7DB UpdateObject.</param>
internal readonly record struct ParkedMessage(
    ParkedKind Kind,
    uint Key,
    uint ObjectGuid,
    double ParkedAt,
    InboundContext Context,
    object Message,
    bool IsUpdate)
{
    /// <summary>The message's opcode, for <c>MessageDropped</c>.</summary>
    public uint Opcode => OpcodeOf(Kind, IsUpdate);

    /// <summary>The opcode of a message of <paramref name="kind"/> (<paramref name="isUpdate"/>: F7DB rather than F745).</summary>
    public static uint OpcodeOf(ParkedKind kind, bool isUpdate) => kind switch
    {
        ParkedKind.Create => isUpdate ? MessageOpcodes.UpdateObject : MessageOpcodes.ObjectCreate,
        ParkedKind.Delete => MessageOpcodes.ObjectDelete,
        ParkedKind.Position => MessageOpcodes.UpdatePosition,
        ParkedKind.Motion => MessageOpcodes.UpdateMotion,
        ParkedKind.Vector => MessageOpcodes.VectorUpdate,
        ParkedKind.State => MessageOpcodes.SetState,
        ParkedKind.Parent => MessageOpcodes.ParentEvent,
        ParkedKind.Pickup => MessageOpcodes.PickupEvent,
        ParkedKind.ObjDesc => MessageOpcodes.ObjDescEvent,
        ParkedKind.Sound => MessageOpcodes.Sound,
        ParkedKind.Script => MessageOpcodes.PlayEffect,
        _ => MessageOpcodes.PlayerTeleport,
    };
}

/// <summary>What <see cref="PendingObjectMessages.TryPark"/> did.</summary>
internal enum ParkOutcome : byte
{
    /// <summary>Parked.</summary>
    Parked,

    /// <summary>Refused: the object already has <see cref="PendingObjectMessages.PerObjectLimit"/> messages parked.</summary>
    ObjectFull,

    /// <summary>Refused: <see cref="PendingObjectMessages.TotalLimit"/> messages are parked in all.</summary>
    StoreFull,
}

/// <summary>
/// The parked SmartBox messages: retail's per-object blob queues (<c>CObjectMaint::QueueBlobForObject</c>, 11.4186
/// 0x005092D0, 11.6096 0x00509DA0) and the queue of messages that arrive before the player is known
/// (<c>SmartBox::QueueNetBlob</c>, 0x00453980 / 0x00453A20, behind <c>ACSmartBox::IsReadyToDispatchEvent</c>, 0x00559590 /
/// 0x0055A1D0), in one store kept in arrival order.
/// </summary>
/// <remarks>
/// Retail bounds neither queue. The bounds here are a Lifestone choice (m2-plan ST8): at most
/// <see cref="PerObjectLimit"/> messages per object, <see cref="TotalLimit"/> in all, each kept at most
/// <see cref="ExpirySeconds"/>; a message that does not fit is refused (the reducer reports it as
/// <c>MessageDropped(ParkingFull)</c>), and an expired one is removed (<c>MessageDropped(ParkingExpired)</c>). The
/// messages held for the player are bounded by the total only. Game thread only; allocation-free except for the boxed
/// message the caller hands in.
/// </remarks>
internal sealed class PendingObjectMessages
{
    /// <summary>The most messages parked for one object.</summary>
    internal const int PerObjectLimit = 32;

    /// <summary>The most messages parked in all.</summary>
    internal const int TotalLimit = 4096;

    /// <summary>How long a message stays parked, in session-clock seconds.</summary>
    internal const double ExpirySeconds = 60.0;

    /// <summary>The key of the messages held until F746 PlayerCreate names the player (no object has id 0).</summary>
    internal const uint WaitingForPlayer = 0;

    private readonly List<ParkedMessage> _entries = new(64);
    private readonly Dictionary<uint, int> _counts = new(64);
    private double _oldest = double.PositiveInfinity;

    /// <summary>The number of parked messages.</summary>
    internal int Count => _entries.Count;

    /// <summary>The number of messages parked under <paramref name="key"/>.</summary>
    internal int CountFor(uint key) => _counts.TryGetValue(key, out var n) ? n : 0;

    /// <summary>Parks <paramref name="message"/> at the end of the store, unless a bound refuses it.</summary>
    internal ParkOutcome TryPark(in ParkedMessage message)
    {
        if (_entries.Count >= TotalLimit)
        {
            return ParkOutcome.StoreFull;
        }

        var count = CountFor(message.Key);
        if (message.Key != WaitingForPlayer && count >= PerObjectLimit)
        {
            return ParkOutcome.ObjectFull;
        }

        _entries.Add(message);
        _counts[message.Key] = count + 1;
        _oldest = Math.Min(_oldest, message.ParkedAt);
        return ParkOutcome.Parked;
    }

    /// <summary>
    /// Moves the messages parked under <paramref name="key"/> into <paramref name="into"/>, in arrival order, and returns
    /// how many it moved (retail's <c>ProcessObjectNetBlobs</c> also takes the whole queue before replaying it, so a
    /// message parked again during the replay waits for the next creation).
    /// </summary>
    internal int TakeFor(uint key, List<ParkedMessage> into)
    {
        if (!_counts.Remove(key, out var count))
        {
            return 0;
        }

        var kept = 0;
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (entry.Key == key)
            {
                into.Add(entry);
            }
            else
            {
                _entries[kept++] = entry;
            }
        }

        _entries.RemoveRange(kept, _entries.Count - kept);
        RecomputeOldest();
        return count;
    }

    /// <summary>True when some message has been parked for at least <see cref="ExpirySeconds"/> at <paramref name="now"/>.</summary>
    internal bool AnyExpired(double now) => now - _oldest >= ExpirySeconds;

    /// <summary>
    /// Moves the messages parked for at least <see cref="ExpirySeconds"/> at <paramref name="now"/> into
    /// <paramref name="into"/>, in arrival order, and returns how many it moved. Returns at once when none can have
    /// expired.
    /// </summary>
    internal int TakeExpired(double now, List<ParkedMessage> into)
    {
        if (!AnyExpired(now))
        {
            return 0;
        }

        var kept = 0;
        var moved = 0;
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (now - entry.ParkedAt >= ExpirySeconds)
            {
                into.Add(entry);
                moved++;
                var left = _counts[entry.Key] - 1;
                if (left == 0)
                {
                    _counts.Remove(entry.Key);
                }
                else
                {
                    _counts[entry.Key] = left;
                }
            }
            else
            {
                _entries[kept++] = entry;
            }
        }

        _entries.RemoveRange(kept, _entries.Count - kept);
        RecomputeOldest();
        return moved;
    }

    /// <summary>Forgets every parked message.</summary>
    internal void Clear()
    {
        _entries.Clear();
        _counts.Clear();
        _oldest = double.PositiveInfinity;
    }

    private void RecomputeOldest()
    {
        _oldest = double.PositiveInfinity;
        foreach (var entry in _entries)
        {
            _oldest = Math.Min(_oldest, entry.ParkedAt);
        }
    }
}

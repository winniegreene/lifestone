namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The audit log of completed actions (A0's R6; DoD 10's "every use is written to the action log"): a ring of the last
/// <see cref="Capacity"/> entries, oldest first, which the automation inspector shows. The session also writes one log
/// line per completion. Entries summarise arguments and never hold a secret.
/// </summary>
/// <remarks>
/// Game thread only. Adding allocates nothing. <see cref="Changed"/> tells a watcher (the automation inspector, G7) that
/// there is something new to show, so it never polls; the counters are lifetime counts, which the ring's drops do not
/// reduce.
/// </remarks>
public sealed class ActionLog
{
    /// <summary>How many entries the ring keeps.</summary>
    public const int Capacity = 1024;

    private readonly RingBuffer<ActionLogEntry> _ring = new(Capacity);
    private readonly long[] _byOutcome = new long[Enum.GetValues<ActionOutcomeKind>().Length];

    /// <summary>An empty log. Only the session creates one.</summary>
    internal ActionLog()
    {
    }

    /// <summary>The number of entries kept (at most <see cref="Capacity"/>).</summary>
    public int Count => _ring.Count;

    /// <summary>Every entry ever added, including those the ring has dropped.</summary>
    public long Total => _ring.Total;

    /// <summary>The entry at <paramref name="index"/>, 0 being the oldest kept.</summary>
    /// <param name="index">0 to <see cref="Count"/> - 1.</param>
    public ActionLogEntry this[int index] => _ring[index];

    /// <summary>Counts every entry ever added, including those the ring has dropped, that ended in <paramref name="kind"/>.</summary>
    public long CountOf(ActionOutcomeKind kind) => _byOutcome[(int)kind];

    /// <summary>Counts every entry ever added whose action had to wait for its lane's rate limit (<see cref="ActionLogEntry.RateLimited"/>).</summary>
    public long RateLimited { get; private set; }

    /// <summary>Goes up by one each time <see cref="Changed"/> is raised: an entry was added, or the safety switches changed.</summary>
    public long Version { get; private set; }

    /// <summary>
    /// Raised (on the game thread, with the action's completion) after an entry is added, and when the kill switch or dry run
    /// changes, which the inspector shows next to the log. Handlers must be quick and must not call back into the session: set a flag
    /// and do the work on the next frame.
    /// </summary>
    public event Action? Changed;

    /// <summary>Adds an entry, dropping the oldest when the ring is full.</summary>
    internal void Add(in ActionLogEntry entry)
    {
        _ring.Add(entry);
        _byOutcome[(int)entry.Outcome.Kind]++;
        if (entry.RateLimited)
        {
            RateLimited++;
        }

        Version++;
        Changed?.Invoke();
    }

    /// <summary>Raises <see cref="Changed"/> for a change that is not an entry (the kill switch or dry run).</summary>
    internal void Touch()
    {
        Version++;
        Changed?.Invoke();
    }
}

/// <summary>One completed action in the <see cref="ActionLog"/>.</summary>
/// <param name="Id">The handle's id.</param>
/// <param name="Kind">What it was for.</param>
/// <param name="Source">Who asked.</param>
/// <param name="Summary">What was asked (<see cref="ActionHandle.Summary"/>; never a secret).</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="QueuedAt">When it was accepted, in session-clock seconds.</param>
/// <param name="CompletedAt">When it completed, in session-clock seconds.</param>
/// <param name="RateLimited">True when the action had to wait for its lane's rate limit before it was sent (it was not dropped).</param>
public readonly record struct ActionLogEntry(
    long Id,
    ActionKind Kind,
    ActionSource Source,
    string Summary,
    ActionOutcome Outcome,
    double QueuedAt,
    double CompletedAt,
    bool RateLimited = false)
{
    /// <summary>How long the action took, in milliseconds.</summary>
    public double Milliseconds => (CompletedAt - QueuedAt) * 1000.0;
}

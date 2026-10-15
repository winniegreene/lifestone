namespace Lifestone.Client.Session;

/// <summary>
/// The game thread's <see cref="SynchronizationContext"/> for hosts that have none (the headless host, tests; Godot's
/// main thread already has its own): <see cref="Post"/> queues a callback from any thread, and the host runs the queue on
/// its game thread with <see cref="RunPending"/>, once per loop iteration after <c>GameSession.Tick</c>. Installed on
/// the game thread (<see cref="Install"/>), it makes every <c>await</c> that started there (an action handle's
/// <c>Completion</c>, a delay on the session's <see cref="TimeProvider"/>) resume there, never on a pool thread
/// (docs/session.md §2).
/// </summary>
/// <remarks>
/// <para><b>When continuations run.</b> A continuation whose task completes on the game thread while this context is
/// current runs inline at once (the runtime's rule for a matching context), which is how a handle completed inside
/// <c>Tick</c> resumes its awaiter inside that tick. A continuation whose task completes on another thread is posted and
/// runs in the next <see cref="RunPending"/>, never earlier.</para>
/// <para><b>Order and failures.</b> <see cref="RunPending"/> runs the callbacks queued before it started, oldest first;
/// a callback posted while it runs waits for the next call, so one call always ends. A callback that throws stops the
/// call: the exception propagates to the host (an <c>async void</c> failure is loud, not lost), and the callbacks not
/// yet run stay queued, in order, for the next call.</para>
/// <para><b>Threads.</b> The context belongs to the thread that creates it: <see cref="RunPending"/>,
/// <see cref="Install"/> and <see cref="Send"/> refuse any other thread (<see cref="InvalidOperationException"/>).
/// <see cref="Post"/> and <see cref="PendingCount"/> are thread-safe.</para>
/// </remarks>
public sealed class GameThreadSynchronizationContext : SynchronizationContext
{
    private readonly Lock _gate = new();
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private bool _running;

    /// <summary>A context owned by the calling thread (the game thread).</summary>
    public GameThreadSynchronizationContext()
    {
        OwnerThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>The managed id of the thread that owns the context (the game thread).</summary>
    public int OwnerThreadId { get; }

    /// <summary>True when the calling thread owns the context.</summary>
    public bool IsOwnerThread => Environment.CurrentManagedThreadId == OwnerThreadId;

    /// <summary>Callbacks waiting for <see cref="RunPending"/>.</summary>
    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _queue.Count;
            }
        }
    }

    /// <summary>Queues <paramref name="d"/> for the next <see cref="RunPending"/>. Any thread; never runs it inline.</summary>
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        lock (_gate)
        {
            _queue.Enqueue((d, state));
        }
    }

    /// <summary>
    /// Runs <paramref name="d"/> at once on the game thread. From any other thread it throws instead of blocking: the game
    /// thread may be the one waiting, and nothing in the session layer blocks (use <see cref="Post"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">Called from a thread that does not own the context.</exception>
    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        ThrowIfNotOwner(nameof(Send));
        d(state);
    }

    /// <summary>The context itself: there is one queue per game thread.</summary>
    public override SynchronizationContext CreateCopy() => this;

    /// <summary>
    /// Runs the callbacks queued before this call, oldest first, with this context current; returns how many ran. A
    /// callback that throws ends the call with its exception, and the rest stay queued (see the remarks).
    /// </summary>
    /// <exception cref="InvalidOperationException">Called from another thread, or from inside a callback.</exception>
    public int RunPending()
    {
        ThrowIfNotOwner(nameof(RunPending));
        if (_running)
        {
            throw new InvalidOperationException("RunPending is not re-entrant: a posted callback called it.");
        }

        int due;
        lock (_gate)
        {
            due = _queue.Count;
        }

        if (due == 0)
        {
            return 0;
        }

        var previous = Current;
        SetSynchronizationContext(this);
        _running = true;
        var ran = 0;
        try
        {
            while (ran < due)
            {
                (SendOrPostCallback Callback, object? State) item;
                lock (_gate)
                {
                    item = _queue.Dequeue();
                }

                ran++;
                item.Callback(item.State);
            }
        }
        finally
        {
            _running = false;
            SetSynchronizationContext(previous);
        }

        return ran;
    }

    /// <summary>
    /// Makes this context current on the game thread until the result is disposed, which restores the previous one. The
    /// headless host installs it once around its loop.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called from a thread that does not own the context.</exception>
    public IDisposable Install()
    {
        ThrowIfNotOwner(nameof(Install));
        var previous = Current;
        SetSynchronizationContext(this);
        return new Installation(this, previous);
    }

    private void ThrowIfNotOwner(string member)
    {
        if (!IsOwnerThread)
        {
            throw new InvalidOperationException(
                $"GameThreadSynchronizationContext.{member} must be called on its game thread (managed thread {OwnerThreadId}), " +
                $"not thread {Environment.CurrentManagedThreadId}; other threads may only Post.");
        }
    }

    /// <summary>Restores the context that was current before <see cref="Install"/>; safe to dispose more than once.</summary>
    private sealed class Installation(GameThreadSynchronizationContext owner, SynchronizationContext? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            owner.ThrowIfNotOwner(nameof(Install) + "().Dispose");
            _disposed = true;
            SetSynchronizationContext(previous);
        }
    }
}

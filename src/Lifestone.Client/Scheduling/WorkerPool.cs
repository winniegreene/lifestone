namespace Lifestone.Client.Scheduling;

/// <summary>
/// Runs CPU work (decoding, mesh building) on thread-pool threads with a cap on how many items run at once, so a
/// burst of streaming requests cannot starve the engine's own threads. Waiting items start in FIFO order.
/// </summary>
/// <remarks>
/// <para>
/// The cap is a <see cref="SemaphoreSlim"/>: its asynchronous waiters are released first-in first-out, so work queued
/// nearest-first (as <c>StreamingPlanner</c> does) also starts nearest-first. Work is cancelled cooperatively: a
/// token cancelled while the item waits makes it complete as cancelled without running; once running, the work
/// itself must observe the token.
/// </para>
/// <para>
/// Thread-safe. Disposing stops new work from starting (waiting and later items complete as cancelled) but does not
/// abort running items. The semaphore and token source are left to the finalizer-free GC (neither holds an OS handle
/// unless <c>AvailableWaitHandle</c>/<c>WaitHandle</c> is touched, which this class never does), so items still
/// running after <see cref="Dispose"/> can release their slot safely.
/// </para>
/// </remarks>
public sealed class WorkerPool : IDisposable
{
    private readonly SemaphoreSlim _gate;
    private readonly CancellationTokenSource _shutdown = new();
    private int _waiting;
    private int _running;
    private long _completed;
    private long _faulted;
    private long _cancelled;
    private int _disposed;

    /// <summary>Creates a pool.</summary>
    /// <param name="maxConcurrency">Items allowed to run at once; at least 1.</param>
    public WorkerPool(int maxConcurrency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
        MaxConcurrency = maxConcurrency;
        _gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    /// <summary>
    /// A default cap: all cores but two (one for the engine's main thread, one for its render thread), at least 1 and at
    /// most <see cref="MaxDefaultConcurrency"/>.
    /// </summary>
    /// <remarks>
    /// Wave-3 performance pass: 8 workers stream as fast as 30 (16.7 landblocks/s at 150 m/s, the same start-up), because
    /// every dat read serialises on one lock and the demand is 17-35 landblocks/s; more workers only add threads for each
    /// garbage collection to suspend and more concurrent allocation (docs/game-core.md §2.4). <c>--workers</c> overrides it.
    /// </remarks>
    public static int DefaultConcurrency => Math.Clamp(Environment.ProcessorCount - 2, 1, MaxDefaultConcurrency);

    /// <summary>The largest default (<see cref="DefaultConcurrency"/>); <c>--workers</c> may go higher.</summary>
    public const int MaxDefaultConcurrency = 8;

    public int MaxConcurrency { get; }

    /// <summary>Items waiting for a slot.</summary>
    public int Waiting => Volatile.Read(ref _waiting);

    /// <summary>Items running now.</summary>
    public int Running => Volatile.Read(ref _running);

    /// <summary>Items that finished successfully.</summary>
    public long Completed => Interlocked.Read(ref _completed);

    /// <summary>Items that threw.</summary>
    public long Faulted => Interlocked.Read(ref _faulted);

    /// <summary>Items cancelled before or while running.</summary>
    public long Cancelled => Interlocked.Read(ref _cancelled);

    /// <summary>Runs <paramref name="work"/> on a worker once a slot is free.</summary>
    public async Task<T> Run<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _shutdown.Token);
        var token = linked.Token;
        Interlocked.Increment(ref _waiting);
        try
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _cancelled);
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
        }

        Interlocked.Increment(ref _running);
        try
        {
            token.ThrowIfCancellationRequested();

            // No token on Task.Run: an item that got its slot and passed the check above always starts (otherwise a
            // cancellation racing the thread pool could drop it unrun after it took a slot); the work observes the token.
            var result = await Task.Run(() => work(token), CancellationToken.None).ConfigureAwait(false);
            Interlocked.Increment(ref _completed);
            return result;
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _cancelled);
            throw;
        }
        catch
        {
            Interlocked.Increment(ref _faulted);
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _running);
            _gate.Release();
        }
    }

    /// <summary>Runs synchronous work on a worker once a slot is free.</summary>
    public Task<T> RunSync<T>(Func<CancellationToken, T> work, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Run(ct => Task.FromResult(work(ct)), cancellation);
    }

    /// <summary>Cancels waiting items and stops new ones from starting. Running items finish on their own.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
    }
}

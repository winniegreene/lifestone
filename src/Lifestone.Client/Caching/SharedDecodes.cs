using System.Collections.Concurrent;

namespace Lifestone.Client.Caching;

/// <summary>
/// Shares one expensive, disposable result (a decoded texture) between every request for the same key that arrives
/// while it is being produced or while some lease still holds it (wave-3 performance pass). Thread-safe and
/// engine-agnostic; the engine side frees a value through the callback given to the constructor.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> At start-up and after a teleport the 3×3 and the prefetch ring prepare together on the workers, and
/// each landblock used to decode the tree, rock and building textures it shares with its neighbours again. Here the
/// first request decodes, concurrent requests wait for that decode, and later requests take a lease on the result for as
/// long as some lease holds it.</para>
/// <para><b>Lifetime.</b> Each <see cref="Acquire"/> returns its own <see cref="Lease"/>; the value is freed when the
/// last lease is disposed. A value whose leases are all gone can no longer be leased (a request then decodes again). The
/// table holds finished values only weakly, so a lease that is never disposed lets the garbage collector reclaim its
/// value as it would without sharing (the engine's own finalizer then frees it).</para>
/// <para><b>Failures.</b> When a decode throws, every request waiting for it rethrows the same exception; the next
/// request decodes again.</para>
/// </remarks>
/// <typeparam name="TKey">What the decode depends on.</typeparam>
/// <typeparam name="TValue">The decoded result.</typeparam>
public sealed class SharedDecodes<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    [ThreadStatic]
    private static bool t_decodedHere;

    private readonly ConcurrentDictionary<TKey, Lazy<Entry>> _inFlight = new();
    private readonly ConcurrentDictionary<TKey, WeakReference<Entry>> _held = new();
    private readonly Action<TValue> _free;
    private long _decodes;
    private long _sharedHits;

    /// <summary>Creates an empty table.</summary>
    /// <param name="free">Frees a value once its last lease is disposed (any thread).</param>
    public SharedDecodes(Action<TValue> free) => _free = free ?? throw new ArgumentNullException(nameof(free));

    /// <summary>Decodes run since creation.</summary>
    public long Decodes => Interlocked.Read(ref _decodes);

    /// <summary>Requests served by another request's decode since creation.</summary>
    public long SharedHits => Interlocked.Read(ref _sharedHits);

    /// <summary>
    /// A lease on the value for <paramref name="key"/>: the one some lease holds now, the one being decoded (waits for
    /// it), or a new decode by <paramref name="decode"/> on this thread.
    /// </summary>
    /// <exception cref="Exception">Whatever <paramref name="decode"/> threw (for every request that waited for it).</exception>
    public Lease Acquire(TKey key, Func<TValue> decode)
    {
        ArgumentNullException.ThrowIfNull(decode);
        while (true)
        {
            if (_held.TryGetValue(key, out var weak))
            {
                if (weak.TryGetTarget(out var held) && held.TryAddLease())
                {
                    Interlocked.Increment(ref _sharedHits);
                    return new Lease(held);
                }

                _held.TryRemove(KeyValuePair.Create(key, weak)); // freed or collected
            }

            var lazy = _inFlight.GetOrAdd(key, k => new Lazy<Entry>(
                () =>
                {
                    // Runs on the first thread that asks for the value; the others wait for it.
                    t_decodedHere = true;
                    Interlocked.Increment(ref _decodes);
                    var entry = new Entry(this, k, decode());
                    _held[k] = new WeakReference<Entry>(entry);
                    return entry;
                },
                LazyThreadSafetyMode.ExecutionAndPublication));
            Entry shared;
            t_decodedHere = false;
            try
            {
                shared = lazy.Value;
            }
            finally
            {
                // Done (or failed, which every waiter rethrows): later requests find the value through _held.
                _inFlight.TryRemove(KeyValuePair.Create(key, lazy));
            }

            if (shared.TryAddLease())
            {
                if (!t_decodedHere)
                {
                    Interlocked.Increment(ref _sharedHits);
                }

                return new Lease(shared);
            }

            // Every lease on it was released before this thread took one (rare): decode again.
        }
    }

    /// <summary>A lease on a value that is not shared: <paramref name="free"/> runs when it is disposed.</summary>
    public static Lease Owned(TValue value, Action<TValue> free)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(free);
        var entry = new Entry(null, default!, value, free);
        entry.TryAddLease();
        return new Lease(entry);
    }

    private void Forget(TKey key, Entry entry)
    {
        if (_held.TryGetValue(key, out var weak) && weak.TryGetTarget(out var current) && ReferenceEquals(current, entry))
        {
            _held.TryRemove(KeyValuePair.Create(key, weak));
        }
    }

    /// <summary>One holder's share of a value. Dispose it once (from any thread); the value goes with the last lease.</summary>
    public sealed class Lease : IDisposable
    {
        private readonly Entry _entry;
        private int _disposed;

        internal Lease(Entry entry) => _entry = entry;

        /// <summary>The value; do not use it once <see cref="IsDisposed"/>.</summary>
        public TValue Value => _entry.Value;

        /// <summary>True once this lease was disposed, or the value was freed.</summary>
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0 || _entry.IsFreed;

        /// <summary>Drops this lease (idempotent).</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _entry.Release();
            }
        }
    }

    /// <summary>A value and its lease count (-1 once freed).</summary>
    internal sealed class Entry(SharedDecodes<TKey, TValue>? owner, TKey key, TValue value, Action<TValue>? free = null)
    {
        private int _leases;

        public TValue Value { get; } = value;

        public bool IsFreed => Volatile.Read(ref _leases) < 0;

        public bool TryAddLease()
        {
            while (true)
            {
                var n = Volatile.Read(ref _leases);
                if (n < 0)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _leases, n + 1, n) == n)
                {
                    return true;
                }
            }
        }

        public void Release()
        {
            while (true)
            {
                var n = Volatile.Read(ref _leases);
                if (n <= 0)
                {
                    return;
                }

                var next = n == 1 ? -1 : n - 1;
                if (Interlocked.CompareExchange(ref _leases, next, n) != n)
                {
                    continue;
                }

                if (next < 0)
                {
                    (free ?? owner!._free)(Value);
                    owner?.Forget(key, this);
                }

                return;
            }
        }
    }
}

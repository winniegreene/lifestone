using System.Diagnostics.CodeAnalysis;

namespace Lifestone.Client.Caching;

/// <summary>Read-only statistics of a cache (perf overlay, reports).</summary>
public interface IResourceCacheStats
{
    string Name { get; }

    long BudgetBytes { get; }

    int Count { get; }

    int IdleCount { get; }

    long TotalBytes { get; }

    long IdleBytes { get; }

    long Hits { get; }

    long Misses { get; }

    long Evictions { get; }
}

/// <summary>
/// A reference-counted cache of engine resources (textures, materials, meshes) with least-recently-used eviction by bytes. Not thread-safe: use it from one thread (the engine main thread).
/// </summary>
/// <remarks>
/// <para>Holders <see cref="TryAcquire(TKey, out TValue)"/>/<see cref="Add"/> an entry (reference count + 1) and
/// <see cref="Release"/> it when their landblock detaches. Entries nobody holds stay cached (idle) so a landblock that
/// streams back in reuses them, until the cache's total size exceeds <see cref="BudgetBytes"/>; then idle entries are
/// evicted least recently released first. Entries in use are never evicted, so the total can exceed the budget while
/// they are held (<see cref="OverBudgetBytes"/> reports by how much).</para>
/// <para><b>Callbacks.</b> <c>onIdle</c> runs when an entry's last reference is released, before it can be evicted: an
/// entry that holds references into another cache (a mesh its materials, a material its texture) drops them there, so
/// an idle entry never pins anything and every cache's budget holds on its own (<see cref="Lifestone.Client.Rendering.RenderResourceCaches{TTexture, TMaterial, TMesh}"/>).
/// <c>onEvict</c> runs when an idle entry leaves the cache: free the engine resource there.</para>
/// </remarks>
public sealed class ResourceCache<TKey, TValue> : IResourceCacheStats
    where TKey : notnull
    where TValue : class
{
    private readonly Dictionary<TKey, Entry> _entries = new();
    private readonly LinkedList<TKey> _idle = new();
    private readonly Action<TKey, TValue>? _onEvict;
    private readonly Action<TKey, TValue>? _onIdle;

    /// <summary>Creates a cache.</summary>
    /// <param name="name">Name for statistics.</param>
    /// <param name="budgetBytes">Total size idle entries are trimmed down to.</param>
    /// <param name="onEvict">Called when an idle entry is evicted (free the resource).</param>
    /// <param name="onIdle">Called when an entry's reference count drops to zero (drop what it holds elsewhere).</param>
    public ResourceCache(string name, long budgetBytes, Action<TKey, TValue>? onEvict = null, Action<TKey, TValue>? onIdle = null)
    {
        Name = name;
        BudgetBytes = budgetBytes;
        _onEvict = onEvict;
        _onIdle = onIdle;
    }

    public string Name { get; }

    /// <summary>Total size the cache trims idle entries down to.</summary>
    public long BudgetBytes { get; set; }

    public int Count => _entries.Count;

    public int IdleCount => _idle.Count;

    public long TotalBytes { get; private set; }

    public long IdleBytes { get; private set; }

    public long OverBudgetBytes => Math.Max(0, TotalBytes - BudgetBytes);

    public long Hits { get; private set; }

    public long Misses { get; private set; }

    public long Evictions { get; private set; }

    /// <summary>True when the key is cached (in use or idle). Does not change counts or recency.</summary>
    public bool Contains(TKey key) => _entries.ContainsKey(key);

    /// <summary>Takes a reference to a cached entry.</summary>
    public bool TryAcquire(TKey key, [MaybeNullWhen(false)] out TValue value) => TryAcquire(key, out value, out _);

    /// <summary>Takes a reference to a cached entry and says whether it was idle (so the caller can re-take what <c>onIdle</c> dropped).</summary>
    public bool TryAcquire(TKey key, [MaybeNullWhen(false)] out TValue value, out bool wasIdle)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            wasIdle = entry.RefCount++ == 0;
            if (wasIdle && entry.IdleNode is not null)
            {
                _idle.Remove(entry.IdleNode);
                entry.IdleNode = null;
                IdleBytes -= entry.Bytes;
            }

            Hits++;
            value = entry.Value;
            return true;
        }

        Misses++;
        value = null;
        wasIdle = false;
        return false;
    }

    /// <summary>Adds a new entry with one reference taken. The key must not be cached.</summary>
    public TValue Add(TKey key, TValue value, long bytes)
    {
        _entries.Add(key, new Entry(value, Math.Max(0, bytes)) { RefCount = 1 });
        TotalBytes += Math.Max(0, bytes);
        Trim();
        return value;
    }

    /// <summary>Drops one reference; an entry nobody holds becomes idle (most recently used) and may be evicted.</summary>
    /// <returns>False when the key is not cached or not held.</returns>
    public bool Release(TKey key)
    {
        if (!_entries.TryGetValue(key, out var entry) || entry.RefCount == 0)
        {
            return false;
        }

        if (--entry.RefCount == 0)
        {
            _onIdle?.Invoke(key, entry.Value);

            // onIdle may not re-acquire this entry (it drops references elsewhere); guard anyway.
            if (entry.RefCount == 0 && _entries.ContainsKey(key))
            {
                entry.IdleNode = _idle.AddFirst(key);
                IdleBytes += entry.Bytes;
                Trim();
            }
        }

        return true;
    }

    /// <summary>Evicts idle entries, least recently released first, while the total exceeds the budget.</summary>
    public void Trim()
    {
        while (TotalBytes > BudgetBytes && _idle.Last is { } last)
        {
            Evict(last.Value);
        }
    }

    /// <summary>Evicts every idle entry.</summary>
    public void ClearIdle()
    {
        while (_idle.Last is { } last)
        {
            Evict(last.Value);
        }
    }

    private void Evict(TKey key)
    {
        var entry = _entries[key];
        _idle.Remove(entry.IdleNode!);
        entry.IdleNode = null;
        _entries.Remove(key);
        TotalBytes -= entry.Bytes;
        IdleBytes -= entry.Bytes;
        Evictions++;
        _onEvict?.Invoke(key, entry.Value);
    }

    private sealed class Entry(TValue value, long bytes)
    {
        public TValue Value { get; } = value;

        public long Bytes { get; } = bytes;

        public int RefCount { get; set; }

        public LinkedListNode<TKey>? IdleNode { get; set; }
    }
}

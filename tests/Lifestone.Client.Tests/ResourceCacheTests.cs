using Lifestone.Client.Caching;

namespace Lifestone.Client.Tests;

public sealed class ResourceCacheTests
{
    [Fact]
    public void Held_entries_are_never_evicted_and_idle_ones_go_least_recently_released_first()
    {
        var evicted = new List<string>();
        var cache = new ResourceCache<string, object>("test", budgetBytes: 100, (k, _) => evicted.Add(k));
        cache.Add("a", new object(), 60);
        cache.Add("b", new object(), 60); // 120 bytes held: over budget, nothing evictable
        Assert.Equal(120, cache.TotalBytes);
        Assert.Equal(20, cache.OverBudgetBytes);
        Assert.Empty(evicted);

        Assert.True(cache.Release("a")); // a idle -> evicted at once (still over budget)
        Assert.Equal(new[] { "a" }, evicted);
        Assert.Equal(60, cache.TotalBytes);

        cache.Add("c", new object(), 30);
        cache.Release("b");
        cache.Release("c"); // 90 bytes, both idle, under budget
        Assert.Equal(2, cache.IdleCount);
        Assert.Equal(90, cache.IdleBytes);

        cache.Add("d", new object(), 50); // 140: evict b (released before c) -> 80
        Assert.Equal(new[] { "a", "b" }, evicted);
        Assert.True(cache.Contains("c"));
        Assert.Equal(80, cache.TotalBytes);
    }

    [Fact]
    public void A_trim_since_a_mark_drops_the_idle_entries_unused_since_and_keeps_the_held_and_the_recently_used()
    {
        // M5 7 P7d (D139 row e): the budget is far above the total, so only the mark decides.
        var evicted = new List<string>();
        var cache = new ResourceCache<string, object>("test", budgetBytes: 1_000_000, (k, _) => evicted.Add(k));
        cache.Add("old-idle", new object(), 10);
        cache.Add("old-held", new object(), 20);
        cache.Add("reused", new object(), 40);
        cache.Release("old-idle");
        cache.Release("reused");
        var mark = cache.Mark();

        // After the mark: one idle entry is taken and released again, a new one comes and goes idle, the held one stays held.
        Assert.True(cache.TryAcquire("reused", out _));
        cache.Release("reused");
        cache.Add("new-idle", new object(), 80);
        cache.Release("new-idle");

        var (count, bytes) = cache.TrimUnusedSince(mark);

        Assert.Equal(1, count);
        Assert.Equal(10, bytes);
        Assert.Equal(new[] { "old-idle" }, evicted);
        Assert.True(cache.Contains("old-held")); // held: never evicted, however old
        Assert.True(cache.Contains("reused"));
        Assert.True(cache.Contains("new-idle"));
        Assert.Equal(140, cache.TotalBytes);
        Assert.Equal(120, cache.IdleBytes);

        // A later mark with nothing used since: every idle entry goes, the held one stays.
        var (later, laterBytes) = cache.TrimUnusedSince(cache.Mark());
        Assert.Equal(2, later);
        Assert.Equal(120, laterBytes);
        Assert.Equal(1, cache.Count);
        Assert.True(cache.Contains("old-held"));
    }

    [Fact]
    public void Reacquiring_an_idle_entry_protects_it_and_counts_a_hit()
    {
        var cache = new ResourceCache<int, object>("test", 10);
        var value = new object();
        cache.Add(1, value, 8);
        cache.Release(1);
        Assert.True(cache.TryAcquire(1, out var again));
        Assert.Same(value, again);
        Assert.Equal(0, cache.IdleCount);
        Assert.Equal(1, cache.Hits);
        Assert.False(cache.TryAcquire(2, out _));
        Assert.Equal(1, cache.Misses);

        cache.Add(2, new object(), 8); // 16 > 10 but both are held
        Assert.Equal(2, cache.Count);
        Assert.False(cache.Release(3));
        Assert.True(cache.Release(1));
        Assert.Equal(1, cache.Count); // 1 was evicted
        Assert.Equal(1, cache.Evictions);
        Assert.False(cache.Release(1));
    }

    [Fact]
    public void Reference_counts_stack()
    {
        var cache = new ResourceCache<int, object>("test", 0);
        cache.Add(1, new object(), 1);
        Assert.True(cache.TryAcquire(1, out _));
        Assert.True(cache.Release(1));
        Assert.True(cache.Contains(1));
        Assert.True(cache.Release(1));
        Assert.False(cache.Contains(1));
    }

    [Fact]
    public void OnIdle_runs_when_the_last_reference_goes_and_before_eviction()
    {
        var log = new List<string>();
        var cache = new ResourceCache<string, object>("test", 0, (k, _) => log.Add($"evict {k}"), (k, _) => log.Add($"idle {k}"));
        cache.Add("a", new object(), 1);
        Assert.True(cache.TryAcquire("a", out _, out var wasIdle));
        Assert.False(wasIdle);
        cache.Release("a");
        Assert.Empty(log);
        cache.Release("a");
        Assert.Equal(new[] { "idle a", "evict a" }, log);

        var kept = new ResourceCache<string, object>("test", 10, onIdle: (k, _) => log.Add($"idle {k}"));
        kept.Add("b", new object(), 1);
        kept.Release("b");
        Assert.True(kept.TryAcquire("b", out _, out wasIdle));
        Assert.True(wasIdle);
        Assert.True(kept.TryAcquire("b", out _, out wasIdle));
        Assert.False(wasIdle);
    }
}

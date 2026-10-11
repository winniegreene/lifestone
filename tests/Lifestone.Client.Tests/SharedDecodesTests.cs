using System.Runtime.CompilerServices;
using Lifestone.Client.Caching;

namespace Lifestone.Client.Tests;

/// <summary>The shared-decode table behind <c>TextureCache.PrepareShared</c> (wave-3 performance pass).</summary>
public sealed class SharedDecodesTests
{
    private sealed class Value(int id)
    {
        public int Id { get; } = id;

        public int Frees;
    }

    [Fact]
    public void A_request_while_a_lease_holds_the_value_shares_it()
    {
        var table = new SharedDecodes<string, Value>(v => v.Frees++);
        var decodes = 0;
        using var first = table.Acquire("a", () => new Value(++decodes));
        using var second = table.Acquire("a", () => new Value(++decodes));

        Assert.Same(first.Value, second.Value);
        Assert.Equal(1, decodes);
        Assert.Equal(1, table.Decodes);
        Assert.Equal(1, table.SharedHits);
    }

    [Fact]
    public void The_value_is_freed_once_with_its_last_lease_and_decoded_again_afterwards()
    {
        var table = new SharedDecodes<string, Value>(v => v.Frees++);
        var first = table.Acquire("a", () => new Value(1));
        var second = table.Acquire("a", () => new Value(2));
        var value = first.Value;

        first.Dispose();
        first.Dispose(); // idempotent
        Assert.Equal(0, value.Frees);
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);

        second.Dispose();
        Assert.Equal(1, value.Frees);

        using var third = table.Acquire("a", () => new Value(3));
        Assert.Equal(3, third.Value.Id);
        Assert.Equal(2, table.Decodes);
    }

    [Fact]
    public void Different_keys_decode_separately()
    {
        var table = new SharedDecodes<string, Value>(_ => { });
        using var a = table.Acquire("a", () => new Value(1));
        using var b = table.Acquire("b", () => new Value(2));
        Assert.NotSame(a.Value, b.Value);
        Assert.Equal(2, table.Decodes);
        Assert.Equal(0, table.SharedHits);
    }

    [Fact]
    public async Task Concurrent_requests_wait_for_one_decode()
    {
        var table = new SharedDecodes<int, Value>(v => Interlocked.Increment(ref v.Frees));
        var decodes = 0;
        using var start = new Barrier(16);
        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            start.SignalAndWait();
            return table.Acquire(7, () =>
            {
                Interlocked.Increment(ref decodes);
                Thread.Sleep(50);
                return new Value(7);
            });
        })).ToArray();
        var leases = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(1, decodes);
        Assert.All(leases, l => Assert.Same(leases[0].Value, l.Value));
        Assert.Equal(15, table.SharedHits);
        foreach (var lease in leases)
        {
            lease.Dispose();
        }

        Assert.Equal(1, leases[0].Value.Frees);
    }

    [Fact]
    public void A_failed_decode_reaches_the_caller_and_the_next_request_decodes_again()
    {
        var table = new SharedDecodes<string, Value>(_ => { });
        Assert.Throws<InvalidDataException>(() => table.Acquire("a", () => throw new InvalidDataException("bad")));
        using var lease = table.Acquire("a", () => new Value(2));
        Assert.Equal(2, lease.Value.Id);
    }

    [Fact]
    public void An_owned_lease_frees_its_value_on_dispose()
    {
        var value = new Value(1);
        var lease = SharedDecodes<string, Value>.Owned(value, v => v.Frees++);
        Assert.False(lease.IsDisposed);
        lease.Dispose();
        lease.Dispose();
        Assert.True(lease.IsDisposed);
        Assert.Equal(1, value.Frees);
    }

    [Fact]
    public void A_leaked_lease_does_not_keep_its_value_alive()
    {
        var table = new SharedDecodes<string, Value>(_ => { });
        var weak = LeakOne(table);
        for (var i = 0; i < 3 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weak.IsAlive);
        using var lease = table.Acquire("a", () => new Value(2));
        Assert.Equal(2, lease.Value.Id);
        Assert.Equal(2, table.Decodes);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LeakOne(SharedDecodes<string, Value> table) =>
        new(table.Acquire("a", () => new Value(1)).Value);
}

using Lifestone.Client.Scheduling;

namespace Lifestone.Client.Tests;

public sealed class WorkerPoolTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void The_default_is_cores_minus_two_capped_at_eight() =>
        Assert.Equal(Math.Min(8, Math.Max(1, Environment.ProcessorCount - 2)), WorkerPool.DefaultConcurrency);

    [Fact]
    public async Task Never_runs_more_than_the_cap_at_once()
    {
        using var pool = new WorkerPool(3);
        var running = 0;
        var peak = 0;
        var tasks = Enumerable.Range(0, 40).Select(_ => pool.RunSync(_ =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            Thread.Sleep(5);
            Interlocked.Decrement(ref running);
            return 0;
        })).ToArray();

        await Task.WhenAll(tasks).WaitAsync(Timeout);
        Assert.InRange(peak, 1, 3);
        Assert.Equal(40, pool.Completed);
        Assert.Equal(0, pool.Running);
        Assert.Equal(0, pool.Waiting);
    }

    [Fact]
    public async Task Waiting_items_start_in_fifo_order()
    {
        using var pool = new WorkerPool(1);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<int>();
        var blocker = pool.Run(async _ =>
        {
            await gate.Task;
            return -1;
        });

        var tasks = new List<Task<int>>();
        for (var i = 0; i < 10; i++)
        {
            var n = i;
            tasks.Add(pool.RunSync(_ =>
            {
                lock (order)
                {
                    order.Add(n);
                }

                return n;
            }));
        }

        await WaitUntil(() => pool.Waiting == 10);
        gate.SetResult();
        await Task.WhenAll(tasks).WaitAsync(Timeout);
        await blocker;
        Assert.Equal(Enumerable.Range(0, 10), order);
    }

    [Fact]
    public async Task Cancelling_a_waiting_item_means_it_never_runs()
    {
        using var pool = new WorkerPool(1);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = pool.Run(async _ =>
        {
            await gate.Task;
            return 0;
        });

        using var cts = new CancellationTokenSource();
        var ran = false;
        var victim = pool.RunSync(_ =>
        {
            ran = true;
            return 1;
        }, cts.Token);

        await WaitUntil(() => pool.Waiting == 1);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => victim);
        gate.SetResult();
        await blocker;
        Assert.False(ran);
        Assert.Equal(1, pool.Cancelled);
    }

    [Fact]
    public async Task Exceptions_propagate_and_free_the_slot()
    {
        using var pool = new WorkerPool(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pool.RunSync<int>(_ => throw new InvalidOperationException()));
        Assert.Equal(1, pool.Faulted);
        Assert.Equal(7, await pool.RunSync(_ => 7).WaitAsync(Timeout));
    }

    [Fact]
    public async Task Dispose_cancels_waiting_items()
    {
        var pool = new WorkerPool(1);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = pool.Run(async _ =>
        {
            await gate.Task;
            return 0;
        });
        var waiting = pool.RunSync(_ => 1);
        await WaitUntil(() => pool.Waiting == 1);
        pool.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        gate.SetResult();
        Assert.Equal(0, await blocker);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pool.RunSync(_ => 2));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(1);
        }
    }
}

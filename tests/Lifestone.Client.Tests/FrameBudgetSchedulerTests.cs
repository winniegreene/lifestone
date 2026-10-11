using Lifestone.Client.Scheduling;

namespace Lifestone.Client.Tests;

public sealed class FrameBudgetSchedulerTests
{
    /// <summary>A manual clock in microsecond ticks; work items advance it to simulate their cost.</summary>
    private sealed class FakeClock
    {
        public long Now;

        public void AdvanceMs(double ms) => Now += (long)(ms * 1000);
    }

    private static (FrameBudgetScheduler Scheduler, FakeClock Clock) Create(double budgetMs)
    {
        var clock = new FakeClock();
        return (new FrameBudgetScheduler(TimeSpan.FromMilliseconds(budgetMs), () => clock.Now, 1_000_000), clock);
    }

    [Fact]
    public void Drain_runs_items_until_the_budget_is_spent()
    {
        var (scheduler, clock) = Create(4);
        var ran = 0;
        for (var i = 0; i < 10; i++)
        {
            scheduler.Enqueue(() =>
            {
                ran++;
                clock.AdvanceMs(1);
            });
        }

        var result = scheduler.Drain();
        Assert.Equal(4, result.Executed);
        Assert.Equal(4, ran);
        Assert.False(result.Overrun);
        Assert.Equal(6, result.Remaining);
        Assert.Equal(TimeSpan.FromMilliseconds(4), result.Elapsed);

        Assert.Equal(4, scheduler.Drain().Executed);
        Assert.Equal(2, scheduler.Drain().Executed);
        Assert.Equal(0, scheduler.Drain().Executed);
        Assert.Equal(10, scheduler.CompletedCount);
        Assert.Equal(0, scheduler.OverrunCount);
    }

    [Fact]
    public void A_slow_item_still_runs_and_counts_as_an_overrun()
    {
        var (scheduler, clock) = Create(4);
        scheduler.Enqueue(() => clock.AdvanceMs(10));
        scheduler.Enqueue(() => clock.AdvanceMs(1));

        var result = scheduler.Drain();
        Assert.Equal(1, result.Executed);
        Assert.True(result.Overrun);
        Assert.Equal(1, scheduler.OverrunCount);
        Assert.Equal(10.0, scheduler.MaxItemMilliseconds, 6);
        Assert.Equal(1, scheduler.Pending);
    }

    [Fact]
    public void Items_over_the_slow_threshold_are_reported_with_their_delegate()
    {
        var (scheduler, clock) = Create(100);
        var reported = new List<(double Ms, Delegate Step)>();
        scheduler.SlowItemThreshold = TimeSpan.FromMilliseconds(5);
        scheduler.SlowItem = (ms, _, step) => reported.Add((ms, step));
        Action slow = () => clock.AdvanceMs(8);
        scheduler.Enqueue(() => clock.AdvanceMs(1));
        scheduler.Enqueue(slow);
        scheduler.Drain();

        var item = Assert.Single(reported);
        Assert.Equal(8.0, item.Ms, 6);
        Assert.NotNull(item.Step);
    }

    [Fact]
    public void Reset_peaks_restarts_the_slowest_item()
    {
        var (scheduler, clock) = Create(4);
        scheduler.Enqueue(() => clock.AdvanceMs(10));
        scheduler.Drain();

        Assert.Equal(10.0, scheduler.ResetPeaks(), 6);
        Assert.Equal(0.0, scheduler.MaxItemMilliseconds);
        scheduler.Enqueue(() => clock.AdvanceMs(2));
        scheduler.Drain();
        Assert.Equal(2.0, scheduler.MaxItemMilliseconds, 6);
    }

    [Fact]
    public void The_slowest_items_own_work_leaves_out_a_gc_pause_inside_it()
    {
        var (scheduler, clock) = Create(4);
        scheduler.Enqueue(() =>
        {
            var before = GC.GetTotalPauseDuration();
            GC.Collect(); // a real blocking collection inside the item
            var pause = (GC.GetTotalPauseDuration() - before).TotalMilliseconds;
            clock.AdvanceMs(10 + pause); // the item's clock sees the pause too
        });
        scheduler.Drain();

        // Its own work is the 10 ms (less if another thread's collection also paused it), never the pause.
        Assert.True(scheduler.MaxItemMilliseconds >= 10.0);
        Assert.InRange(scheduler.MaxItemWorkMilliseconds, 0.0, 10.0 + 0.001);

        scheduler.ResetPeaks();
        Assert.Equal(0.0, scheduler.MaxItemWorkMilliseconds);
        scheduler.Enqueue(() => clock.AdvanceMs(3));
        scheduler.Drain();
        Assert.InRange(scheduler.MaxItemWorkMilliseconds, 0.0, 3.0 + 0.001);
    }

    [Fact]
    public void A_zero_budget_still_makes_progress()
    {
        var (scheduler, clock) = Create(0);
        scheduler.Enqueue(() => clock.AdvanceMs(1));
        scheduler.Enqueue(() => clock.AdvanceMs(1));
        Assert.Equal(1, scheduler.Drain().Executed);
        Assert.Equal(1, scheduler.Drain().Executed);
    }

    [Fact]
    public void Lower_priority_values_run_first_and_ties_run_in_order()
    {
        var (scheduler, _) = Create(100);
        var order = new List<string>();
        scheduler.Enqueue(() => order.Add("far-1"), priority: 900);
        scheduler.Enqueue(() => order.Add("near-1"), priority: 10);
        scheduler.Enqueue(() => order.Add("far-2"), priority: 900);
        scheduler.Enqueue(() => order.Add("near-2"), priority: 10);
        scheduler.Enqueue(() => order.Add("now"), priority: -1);
        scheduler.Drain();
        Assert.Equal(new[] { "now", "near-1", "near-2", "far-1", "far-2" }, order);
    }

    [Fact]
    public void Cancelled_items_are_skipped()
    {
        var (scheduler, _) = Create(100);
        using var cts = new CancellationTokenSource();
        var ran = false;
        scheduler.Enqueue(() => ran = true, cancellation: cts.Token);
        cts.Cancel();
        var result = scheduler.Drain();
        Assert.False(ran);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Executed);
    }

    [Fact]
    public void Stepwise_items_continue_next_frame_and_keep_their_place()
    {
        var (scheduler, clock) = Create(2);
        var steps = 0;
        var order = new List<string>();
        scheduler.EnqueueSteps(() =>
        {
            steps++;
            order.Add($"step{steps}");
            clock.AdvanceMs(1);
            return steps == 3;
        });
        scheduler.Enqueue(() => order.Add("later"));

        var first = scheduler.Drain();
        Assert.Equal(2, first.Executed);
        Assert.Equal(0, first.Completed);
        var second = scheduler.Drain();
        Assert.Equal(new[] { "step1", "step2", "step3", "later" }, order);
        Assert.Equal(2, second.Completed);
    }

    [Fact]
    public void Errors_go_to_the_handler_or_propagate()
    {
        var (scheduler, _) = Create(100);
        var errors = new List<Exception>();
        scheduler.Error = errors.Add;
        var after = false;
        scheduler.Enqueue(() => throw new InvalidOperationException("boom"));
        scheduler.Enqueue(() => after = true);
        scheduler.Drain();
        Assert.IsType<InvalidOperationException>(Assert.Single(errors));
        Assert.True(after);

        scheduler.Error = null;
        scheduler.Enqueue(() => throw new InvalidOperationException("loud"));
        Assert.Throws<InvalidOperationException>(() => scheduler.Drain());
    }

    [Fact]
    public async Task Any_thread_may_enqueue_while_the_main_thread_drains()
    {
        var scheduler = new FrameBudgetScheduler(TimeSpan.FromMilliseconds(1));
        var count = 0;
        var producers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                scheduler.Enqueue(() => count++);
            }
        })).ToArray();

        while (!producers.All(p => p.IsCompleted) || scheduler.Pending > 0)
        {
            scheduler.Drain();
            await Task.Yield();
        }

        await Task.WhenAll(producers);
        Assert.Equal(8000, count);
        Assert.Equal(8000, scheduler.CompletedCount);
    }

    [Fact]
    public void Clear_drops_everything()
    {
        var (scheduler, _) = Create(100);
        scheduler.Enqueue(() => { });
        scheduler.Enqueue(() => { });
        Assert.Equal(2, scheduler.Clear());
        Assert.Equal(0, scheduler.Pending);
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Enqueue(() => { }, double.NaN));
    }
}

using System.Diagnostics;

namespace Lifestone.Client.Scheduling;

/// <summary>What one <see cref="FrameBudgetScheduler.Drain"/> call did.</summary>
/// <param name="Executed">Work items (or steps of stepwise items) run.</param>
/// <param name="Completed">Items that finished (stepwise items count once, when their last step returns true).</param>
/// <param name="Skipped">Items dropped because their cancellation token was cancelled.</param>
/// <param name="Elapsed">Wall time spent in the drain.</param>
/// <param name="Overrun">True when the drain took longer than the budget.</param>
/// <param name="Remaining">Items still queued afterwards.</param>
public readonly record struct DrainResult(int Executed, int Completed, int Skipped, TimeSpan Elapsed, bool Overrun, int Remaining);

/// <summary>
/// A priority queue of main-thread work drained once per frame within a time budget. Any thread may enqueue; only one
/// thread (the engine's main thread) may drain.
/// </summary>
/// <remarks>
/// <para>
/// Engines require resources and scene nodes to be created on the main thread, and creating too many in one frame
/// shows as a hitch. Workers prepare data off the main thread and enqueue the cheap final step here; each frame the
/// engine calls <see cref="Drain"/>, which runs items in priority order (lowest value first, then FIFO) until the
/// budget is spent.
/// </para>
/// <list type="bullet">
/// <item><b>Progress.</b> At least one item runs per drain even if the budget is zero, so the queue always moves.</item>
/// <item><b>Overruns.</b> A drain that ends past the budget (an item was slower than what was left) counts as an
/// overrun (<see cref="OverrunCount"/>); the perf overlay reports them.</item>
/// <item><b>Stepwise work.</b> <see cref="EnqueueSteps"/> takes a step function that returns true when finished; an
/// unfinished item keeps its place at the head of its priority and continues in the same drain if budget remains,
/// otherwise next frame. Use it to split big attaches (e.g. create 50 nodes per step).</item>
/// <item><b>Cancellation.</b> Items whose token is cancelled before they run are dropped (<see cref="DrainResult.Skipped"/>).</item>
/// <item><b>Errors.</b> An exception from an item goes to <see cref="Error"/> if set (the item is then dropped and the
/// drain continues), otherwise it propagates out of <see cref="Drain"/>.</item>
/// </list>
/// </remarks>
public sealed class FrameBudgetScheduler
{
    private readonly PriorityQueue<WorkItem, (double Priority, long Sequence)> _queue = new();
    private readonly Lock _lock = new();
    private readonly Func<long> _timestamp;
    private readonly double _ticksPerSecond;
    private long _sequence;
    private long _executed;
    private long _completed;
    private long _overruns;
    private long _drains;
    private double _maxItemMilliseconds;
    private double _maxItemWorkMilliseconds;

    /// <summary>Creates a scheduler.</summary>
    /// <param name="budget">Time per <see cref="Drain"/> (e.g. 4 ms).</param>
    /// <param name="timestamp">Clock in ticks (default <see cref="Stopwatch.GetTimestamp"/>); injectable for tests.</param>
    /// <param name="ticksPerSecond">Ticks per second of <paramref name="timestamp"/> (default <see cref="Stopwatch.Frequency"/>).</param>
    public FrameBudgetScheduler(TimeSpan budget, Func<long>? timestamp = null, long ticksPerSecond = 0)
    {
        Budget = budget;
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
        _ticksPerSecond = ticksPerSecond > 0 ? ticksPerSecond : Stopwatch.Frequency;
    }

    /// <summary>Time budget per drain. Negative values act as zero.</summary>
    public TimeSpan Budget { get; set; }

    /// <summary>Receives exceptions thrown by work items; when null they propagate out of <see cref="Drain"/>.</summary>
    public Action<Exception>? Error { get; set; }

    /// <summary>Items or steps that take longer than this are reported to <see cref="SlowItem"/>; zero (default) = never.</summary>
    public TimeSpan SlowItemThreshold { get; set; }

    /// <summary>
    /// Diagnostics: receives each item or step slower than <see cref="SlowItemThreshold"/> — its milliseconds, the
    /// garbage-collection pause that fell inside it, and the delegate that ran (so the log can say whose work it was).
    /// </summary>
    public Action<double, double, Delegate>? SlowItem { get; set; }

    /// <summary>Items queued now.</summary>
    public int Pending
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }

    /// <summary>Items and steps run since creation.</summary>
    public long ExecutedCount => Interlocked.Read(ref _executed);

    /// <summary>Items finished since creation.</summary>
    public long CompletedCount => Interlocked.Read(ref _completed);

    /// <summary>Drains that ran past the budget.</summary>
    public long OverrunCount => Interlocked.Read(ref _overruns);

    /// <summary>Drains run.</summary>
    public long DrainCount => Interlocked.Read(ref _drains);

    /// <summary>Slowest single item or step since creation or the last <see cref="ResetPeaks"/>, in milliseconds.</summary>
    public double MaxItemMilliseconds => Volatile.Read(ref _maxItemMilliseconds);

    /// <summary>
    /// Slowest single item or step without the garbage-collection pause that fell inside it, since creation or the last
    /// <see cref="ResetPeaks"/>, in milliseconds (wave-3 performance pass: tells an item's own work from a GC that landed
    /// in it; docs/game-core.md §7.3).
    /// </summary>
    public double MaxItemWorkMilliseconds => Volatile.Read(ref _maxItemWorkMilliseconds);

    /// <summary>
    /// Restarts <see cref="MaxItemMilliseconds"/> and <see cref="MaxItemWorkMilliseconds"/> (e.g. when a measured flight
    /// starts, so the start-up's one-off items do not count) and returns the value <see cref="MaxItemMilliseconds"/> had.
    /// </summary>
    public double ResetPeaks()
    {
        Interlocked.Exchange(ref _maxItemWorkMilliseconds, 0.0);
        return Interlocked.Exchange(ref _maxItemMilliseconds, 0.0);
    }

    /// <summary>The last drain's result.</summary>
    public DrainResult LastDrain { get; private set; }

    /// <summary>Queues a one-shot action.</summary>
    /// <param name="work">Runs on the draining thread.</param>
    /// <param name="priority">Lower runs first (e.g. squared distance to the camera).</param>
    /// <param name="cancellation">Drop the item if cancelled before it runs.</param>
    public void Enqueue(Action work, double priority = 0, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        Add(new WorkItem(() =>
        {
            work();
            return true;
        }, cancellation), priority);
    }

    /// <summary>Queues stepwise work: <paramref name="step"/> is called until it returns true.</summary>
    public void EnqueueSteps(Func<bool> step, double priority = 0, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        Add(new WorkItem(step, cancellation), priority);
    }

    /// <summary>Removes every queued item without running it; returns how many there were.</summary>
    public int Clear()
    {
        lock (_lock)
        {
            var count = _queue.Count;
            _queue.Clear();
            return count;
        }
    }

    /// <summary>Runs queued work until the budget is spent or the queue is empty. Call from the main thread only.</summary>
    public DrainResult Drain()
    {
        var start = _timestamp();
        var budgetTicks = Math.Max(0.0, Budget.TotalSeconds) * _ticksPerSecond;
        int executed = 0, completed = 0, skipped = 0;
        while (true)
        {
            if (executed > 0 && _timestamp() - start >= budgetTicks)
            {
                break;
            }

            WorkItem item;
            (double Priority, long Sequence) key;
            lock (_lock)
            {
                if (!_queue.TryDequeue(out item!, out key))
                {
                    break;
                }
            }

            if (item.Cancellation.IsCancellationRequested)
            {
                skipped++;
                continue;
            }

            var itemStart = _timestamp();
            var slowItem = SlowItemThreshold > TimeSpan.Zero ? SlowItem : null;
            var pauseStart = GC.GetTotalPauseDuration(); // a counter read (no allocation): MaxItemWorkMilliseconds needs it
            bool done;
            try
            {
                done = item.Step();
            }
            catch (Exception ex) when (Error is not null)
            {
                Error(ex);
                done = true;
            }
            finally
            {
                executed++;
                var ms = (_timestamp() - itemStart) * 1000.0 / _ticksPerSecond;
                var gcMs = (GC.GetTotalPauseDuration() - pauseStart).TotalMilliseconds;
                if (ms > _maxItemMilliseconds)
                {
                    Volatile.Write(ref _maxItemMilliseconds, ms);
                }

                var workMs = Math.Max(0.0, ms - gcMs);
                if (workMs > _maxItemWorkMilliseconds)
                {
                    Volatile.Write(ref _maxItemWorkMilliseconds, workMs);
                }

                if (slowItem is not null && ms > SlowItemThreshold.TotalMilliseconds)
                {
                    slowItem(ms, gcMs, item.Step);
                }
            }

            if (done)
            {
                completed++;
            }
            else
            {
                lock (_lock)
                {
                    // Same key: the item keeps its place ahead of later work of the same priority.
                    _queue.Enqueue(item, key);
                }
            }
        }

        var elapsedTicks = _timestamp() - start;
        var overrun = elapsedTicks > budgetTicks;
        Interlocked.Add(ref _executed, executed);
        Interlocked.Add(ref _completed, completed);
        Interlocked.Increment(ref _drains);
        if (overrun)
        {
            Interlocked.Increment(ref _overruns);
        }

        var result = new DrainResult(
            executed,
            completed,
            skipped,
            TimeSpan.FromSeconds(elapsedTicks / _ticksPerSecond),
            overrun,
            Pending);
        LastDrain = result;
        return result;
    }

    private void Add(WorkItem item, double priority)
    {
        if (double.IsNaN(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority), "Priority must be a number.");
        }

        lock (_lock)
        {
            _queue.Enqueue(item, (priority, _sequence++));
        }
    }

    private sealed record WorkItem(Func<bool> Step, CancellationToken Cancellation);
}

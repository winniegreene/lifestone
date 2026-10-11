using Lifestone.Client.Scheduling;

namespace Lifestone.Game.Core;

/// <summary>
/// The game's main-thread work queue: workers (and anyone else) enqueue the steps that must run on Godot's main thread
/// (creating textures, meshes, materials and nodes); <see cref="GameServices"/> drains it once per frame within a time
/// budget (default 4 ms, <c>--budget-ms</c>). A thin Godot-side wrapper over the engine-agnostic
/// <see cref="FrameBudgetScheduler"/>.
/// </summary>
/// <remarks>
/// Priorities: lower runs first. Streaming uses the squared distance from the camera to the landblock, so near
/// landblocks attach first; use <see cref="Urgent"/> for work that frees memory or must not wait (detaches).
/// </remarks>
public sealed class MainThreadQueue
{
    /// <summary>Priority for work that should run before any attach (detaches, cancellations).</summary>
    public const double Urgent = double.MinValue;

    private readonly FrameBudgetScheduler _scheduler;

    public MainThreadQueue(TimeSpan budget)
    {
        _scheduler = new FrameBudgetScheduler(budget)
        {
            Error = ex => Log.Exception("main", "main-thread work item failed", ex),

            // Wave-3 performance pass: name the work behind slow items (debug log; measurement runs use --log-level=debug).
            SlowItemThreshold = Log.MinimumLevel <= LogLevel.Debug ? TimeSpan.FromMilliseconds(SlowItemMilliseconds) : TimeSpan.Zero,
            SlowItem = (ms, gcMs, step) =>
            {
                if (++_slowItemsLogged <= MaxSlowItemLogs)
                {
                    Log.Debug("main", "slow main-thread item", ("ms", ms), ("gc_pause_ms", gcMs), ("work_ms", ms - gcMs), ("item", Describe(step)));
                }
            },
        };
    }

    /// <summary>Main-thread items slower than this are logged at debug level (with the GC pause inside them).</summary>
    public const double SlowItemMilliseconds = 6.0;

    private const int MaxSlowItemLogs = 60;
    private int _slowItemsLogged;

    /// <summary>"Type.Method" of a queued delegate, and of the work it wraps (e.g. <see cref="InvokeAsync{T}"/>'s).</summary>
    private static string Describe(Delegate step)
    {
        var name = $"{step.Method.DeclaringType?.Name}.{step.Method.Name}";
        if (step.Target is not { } target)
        {
            return name;
        }

        foreach (var field in target.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
        {
            if (field.GetValue(target) is Delegate inner && !ReferenceEquals(inner, step))
            {
                return $"{name} -> {inner.Method.DeclaringType?.Name}.{inner.Method.Name}";
            }
        }

        return name;
    }

    /// <summary>The underlying scheduler (statistics).</summary>
    public FrameBudgetScheduler Scheduler => _scheduler;

    public TimeSpan Budget
    {
        get => _scheduler.Budget;
        set => _scheduler.Budget = value;
    }

    public int Pending => _scheduler.Pending;

    /// <summary>Queues a one-shot action (any thread).</summary>
    public void Enqueue(Action work, double priority = 0, CancellationToken cancellation = default) =>
        _scheduler.Enqueue(work, priority, cancellation);

    /// <summary>Queues stepwise work, called until it returns true (any thread).</summary>
    public void EnqueueSteps(Func<bool> step, double priority = 0, CancellationToken cancellation = default) =>
        _scheduler.EnqueueSteps(step, priority, cancellation);

    /// <summary>
    /// Runs <paramref name="work"/> on the main thread within the budget and returns its result to the caller (a worker
    /// awaiting a Godot resource). Cancelled before it runs: the task is cancelled.
    /// </summary>
    public Task<T> InvokeAsync<T>(Func<T> work, double priority = 0, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancellation.IsCancellationRequested)
        {
            tcs.SetCanceled(cancellation);
            return tcs.Task;
        }

        var registration = cancellation.Register(() => tcs.TrySetCanceled(cancellation));
        _scheduler.Enqueue(
            () =>
            {
                registration.Dispose();
                if (tcs.Task.IsCompleted)
                {
                    return;
                }

                try
                {
                    tcs.TrySetResult(work());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            },
            priority);
        return tcs.Task;
    }

    /// <summary>Runs queued work within the budget. Main thread only (<see cref="GameServices"/> calls it).</summary>
    public DrainResult Drain() => _scheduler.Drain();
}

using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The game-thread primitives of step ST1: the session clock on an injected provider, the headless host's
/// synchronization context (continuations from other threads run only in <c>RunPending</c>, on the game thread) and the
/// one-game-thread binding the session and the bus enforce.
/// </summary>
public sealed class GameThreadTests
{
    [Fact]
    public void SessionClock_is_monotonic_and_starts_at_zero()
    {
        var time = new ManualTimeProvider();
        time.Advance(TimeSpan.FromHours(3));
        var clock = new SessionClock(time);
        Assert.Equal(0, clock.Now);

        var last = clock.Now;
        foreach (var step in new[] { 0, 1, 15, 1000, 0, 333 })
        {
            time.Advance(TimeSpan.FromMilliseconds(step));
            Assert.True(clock.Now >= last, "the session clock went backwards");
            last = clock.Now;
        }

        Assert.Equal(1.349, clock.Now, 9);
    }

    [Fact]
    public void A_delay_on_the_session_provider_completes_only_when_the_provider_advances()
    {
        // Every session timeout is a TimeProvider timer: a test or a replay moves time, not the wall clock.
        var time = new ManualTimeProvider();
        var delay = Task.Delay(TimeSpan.FromSeconds(30), time);
        time.Advance(TimeSpan.FromSeconds(29.999));
        Assert.False(delay.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(delay.IsCompletedSuccessfully);
    }

    [Fact]
    public void Continuations_posted_to_the_game_thread_context_run_only_in_RunPending()
    {
        var context = new GameThreadSynchronizationContext();
        var source = new TaskCompletionSource();
        var resumedOn = 0;
        Task waiting;
        using (context.Install())
        {
            waiting = ResumeAfter(source.Task, () => resumedOn = Environment.CurrentManagedThreadId);
        }

        // Completed on another thread: the continuation is posted to the game thread's queue, not run there and then.
        RunOnOtherThread(source.SetResult);
        Assert.Equal(0, resumedOn);
        Assert.Equal(1, context.PendingCount);
        Assert.False(waiting.IsCompleted);

        Assert.Equal(1, context.RunPending());
        Assert.Equal(Environment.CurrentManagedThreadId, resumedOn);
        Assert.True(waiting.IsCompletedSuccessfully);
        Assert.Equal(0, context.RunPending());
    }

    [Fact]
    public void A_task_completed_on_the_game_thread_resumes_its_awaiter_inline()
    {
        // The rule handles rely on (docs/session.md §2): a handle completed inside Tick resumes its awaiter in that tick.
        var context = new GameThreadSynchronizationContext();
        var source = new TaskCompletionSource();
        var resumed = false;
        using (context.Install())
        {
            _ = ResumeAfter(source.Task, () => resumed = true);
            source.SetResult();
            Assert.True(resumed);
        }

        Assert.Equal(0, context.PendingCount);
    }

    [Fact]
    public void RunPending_runs_only_what_was_queued_before_it_oldest_first()
    {
        var context = new GameThreadSynchronizationContext();
        var order = new List<string>();
        context.Post(_ => order.Add("a"), null);
        context.Post(_ => { order.Add("b"); context.Post(_ => order.Add("posted by b"), null); }, null);
        context.Post(_ => order.Add("c"), null);

        Assert.Equal(3, context.RunPending());
        Assert.Equal(["a", "b", "c"], order);
        Assert.Equal(1, context.PendingCount);
        Assert.Equal(1, context.RunPending());
        Assert.Equal(["a", "b", "c", "posted by b"], order);
    }

    [Fact]
    public void A_throwing_callback_ends_the_run_and_keeps_the_rest_queued_in_order()
    {
        var context = new GameThreadSynchronizationContext();
        var order = new List<string>();
        context.Post(_ => order.Add("a"), null);
        context.Post(_ => throw new InvalidOperationException("boom"), null);
        context.Post(_ => order.Add("c"), null);
        context.Post(_ => order.Add("d"), null);

        var e = Assert.Throws<InvalidOperationException>(() => context.RunPending());
        Assert.Equal("boom", e.Message);
        Assert.Equal(["a"], order);
        Assert.Equal(2, context.PendingCount);
        Assert.Equal(2, context.RunPending());
        Assert.Equal(["a", "c", "d"], order);
    }

    [Fact]
    public void RunPending_makes_the_context_current_and_restores_the_previous_one()
    {
        var context = new GameThreadSynchronizationContext();
        var before = SynchronizationContext.Current;
        SynchronizationContext? seen = null;
        context.Post(_ => seen = SynchronizationContext.Current, null);
        context.RunPending();
        Assert.Same(context, seen);
        Assert.Same(before, SynchronizationContext.Current);
        Assert.Same(context, context.CreateCopy());
    }

    [Fact]
    public void Only_the_owner_thread_runs_installs_or_sends()
    {
        var context = new GameThreadSynchronizationContext();
        var ran = false;
        context.Send(_ => ran = true, null);
        Assert.True(ran);

        Exception? runPending = null;
        Exception? install = null;
        Exception? send = null;
        var thread = new Thread(() =>
        {
            context.Post(_ => { }, null); // any thread may post
            runPending = Record.Exception(() => context.RunPending());
            install = Record.Exception(() => context.Install());
            send = Record.Exception(() => context.Send(_ => { }, null));
        });
        thread.Start();
        thread.Join();

        Assert.IsType<InvalidOperationException>(runPending);
        Assert.IsType<InvalidOperationException>(install);
        Assert.IsType<InvalidOperationException>(send);
        Assert.Contains("game thread", send!.Message, StringComparison.Ordinal);
        Assert.Equal(1, context.PendingCount);
    }

    [Fact]
    public void RunPending_is_not_reentrant()
    {
        var context = new GameThreadSynchronizationContext();
        Exception? inner = null;
        context.Post(_ => inner = Record.Exception(() => context.RunPending()), null);
        context.RunPending();
        Assert.IsType<InvalidOperationException>(inner);
    }

    [Fact]
    public void The_binding_allows_any_thread_until_bound_then_only_its_own()
    {
        var binding = new GameThreadBinding();
        Assert.False(binding.IsBound);
        Exception? beforeBinding = null;
        RunOnOtherThread(() => beforeBinding = Record.Exception(() => binding.Check("IEventBus.Subscribe")));
        Assert.Null(beforeBinding);

        binding.Bind("GameSession.Tick");
        binding.Bind("GameSession.Tick");
        binding.Check("IEventBus.Subscribe");
        Assert.True(binding.IsBound);
        Assert.Equal(Environment.CurrentManagedThreadId, binding.ThreadId);

        Exception? check = null;
        Exception? bind = null;
        RunOnOtherThread(() =>
        {
            check = Record.Exception(() => binding.Check("IEventBus.Subscribe"));
            bind = Record.Exception(() => binding.Bind("GameSession.Tick"));
        });
        var e = Assert.IsType<InvalidOperationException>(check);
        Assert.StartsWith("IEventBus.Subscribe must be called on the session's game thread", e.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(bind);
    }

    private static async Task ResumeAfter(Task task, Action then)
    {
        await task;
        then();
    }

    private static void RunOnOtherThread(Action action)
    {
        var thread = new Thread(() => action());
        thread.Start();
        thread.Join();
    }
}

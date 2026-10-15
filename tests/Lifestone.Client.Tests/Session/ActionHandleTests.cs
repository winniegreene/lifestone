using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <see cref="ActionHandle"/> and <see cref="ToolHandle{T}"/>: a handle completes once, the first outcome wins, a
/// throwing <see cref="ActionHandle.Completed"/> handler cannot stop the others, and a continuation awaiting the handle on
/// the completing thread runs inline, inside the completing call (the session completes handles inside its tick).
/// </summary>
public sealed class ActionHandleTests
{
    private static readonly Action<ActionHandle, Exception> Rethrow = (_, e) => throw new InvalidOperationException("unexpected handler error", e);

    private static ActionHandle Handle() => new(7, ActionKind.EnterWorld, ActionSource.Ui, "EnterWorld guid=0x50000001", queuedAt: 1.5);

    [Fact]
    public async Task A_handle_moves_from_queued_to_sent_to_completed()
    {
        var handle = Handle();
        Assert.Equal(ActionStatus.Queued, handle.Status);
        Assert.Null(handle.SentAt);
        Assert.False(handle.Completion.IsCompleted);

        handle.MarkSent(2.0);
        Assert.Equal(ActionStatus.Sent, handle.Status);
        Assert.Equal(2.0, handle.SentAt);

        Assert.True(handle.TryComplete(new ActionOutcome(ActionOutcomeKind.Confirmed), 2.5, Rethrow));
        Assert.Equal(ActionStatus.Completed, handle.Status);
        Assert.True(handle.IsCompleted);
        Assert.Equal(2.5, handle.CompletedAt);
        Assert.Equal(new ActionOutcome(ActionOutcomeKind.Confirmed), handle.Outcome);
        Assert.True(handle.Completion.IsCompletedSuccessfully);
        Assert.Equal(new ActionOutcome(ActionOutcomeKind.Confirmed), await handle.Completion);
        Assert.Equal(1.5, handle.QueuedAt);
    }

    [Fact]
    public void The_first_outcome_wins()
    {
        var handle = Handle();
        var raised = 0;
        handle.Completed += _ => raised++;

        Assert.True(handle.TryComplete(new ActionOutcome(ActionOutcomeKind.TimedOut), 3, Rethrow));
        Assert.False(handle.TryComplete(new ActionOutcome(ActionOutcomeKind.Confirmed), 4, Rethrow));
        handle.MarkSent(5);

        Assert.Equal(1, raised);
        Assert.Equal(ActionOutcomeKind.TimedOut, handle.Outcome?.Kind);
        Assert.Equal(3, handle.CompletedAt);
        Assert.Null(handle.SentAt);
        Assert.Equal(ActionStatus.Completed, handle.Status);
    }

    [Fact]
    public void A_throwing_completed_handler_is_reported_and_the_others_still_run()
    {
        var handle = Handle();
        var calls = new List<string>();
        handle.Completed += _ => calls.Add("first");
        handle.Completed += _ => throw new InvalidOperationException("boom");
        handle.Completed += _ => calls.Add("third");
        var errors = new List<Exception>();

        Assert.True(handle.TryComplete(new ActionOutcome(ActionOutcomeKind.Refused, Detail: "wrong phase"), 1, (_, e) => errors.Add(e)));

        Assert.Equal(["first", "third"], calls);
        Assert.Equal("boom", Assert.Single(errors).Message);
        Assert.True(handle.Completion.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_continuation_on_the_completing_thread_runs_inside_the_completing_call(bool withGameThreadContext)
    {
        // Models the game thread: no context (a plain host loop), or a context installed on that thread (Godot's, or the
        // headless host's game-thread context). Either way the awaiting code resumes inline, inside TryComplete.
        string? observed = null;
        var thread = new Thread(() =>
        {
            if (withGameThreadContext)
            {
                SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            }

            var handle = Handle();
            var steps = new List<string>();
            _ = Await(handle, steps);
            steps.Add("before");
            handle.TryComplete(new ActionOutcome(ActionOutcomeKind.Confirmed), 1, Rethrow);
            steps.Add("after");
            observed = string.Join(",", steps);
        });
        thread.Start();
        thread.Join();

        Assert.Equal("before,continued Confirmed,after", observed);

        static async Task Await(ActionHandle handle, List<string> steps)
        {
            var outcome = await handle.Completion;
            steps.Add("continued " + outcome.Kind);
        }
    }

    [Fact]
    public void A_handle_describes_itself_without_secrets()
    {
        var handle = Handle();
        Assert.Equal("#7 EnterWorld (Ui) Queued: EnterWorld guid=0x50000001", handle.ToString());
        handle.TryComplete(new ActionOutcome(ActionOutcomeKind.Confirmed), 2, Rethrow);
        Assert.Equal("#7 EnterWorld (Ui) Confirmed: EnterWorld guid=0x50000001", handle.ToString());
    }

    [Fact]
    public void A_tool_handle_carries_its_result_only_when_it_completes_it()
    {
        var where = new WhereResult(CellFrame.FromWire(0xA9B40019, new(84, 7.1f, 94), 1, 0, 0, 0), Teleporting: false, 0, 2);
        var handle = new ToolHandle<WhereResult>(8, ActionKind.ToolWhereAmI, ActionSource.Ui, "ToolWhereAmI", 0);

        Assert.True(handle.TryComplete(where, new ActionOutcome(ActionOutcomeKind.Confirmed), 0.2, Rethrow));
        Assert.Same(where, handle.Result);

        Assert.False(handle.TryComplete(null, new ActionOutcome(ActionOutcomeKind.TimedOut), 5, Rethrow));
        Assert.Same(where, handle.Result);
        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome?.Kind);
    }
}

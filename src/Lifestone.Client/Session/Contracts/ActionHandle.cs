using System.Globalization;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// One action's handle: what was asked, who asked, where it is, and how it ended. Every <see cref="IActionApi"/>,
/// <see cref="PlayerTools"/> and <see cref="MoveIntents"/> call returns one, already queued (or already completed, when
/// it was refused or is not built yet).
/// </summary>
/// <remarks>
/// <para><b>Completion happens inside <c>GameSession.Tick</c></b>, on the game thread, after the tick's events were
/// dispatched: first <see cref="Outcome"/> and <see cref="Status"/> are set, then <see cref="Completion"/> completes,
/// then <see cref="Completed"/> is raised. The task is completed without forcing continuations onto another thread, so a
/// continuation awaiting it on the game thread (under Godot's synchronization context, or the headless host's
/// game-thread context) runs inline inside that tick, and may issue new actions.</para>
/// <para>A handle completes exactly once; the first outcome wins. Times are <see cref="SessionClock"/> seconds.</para>
/// </remarks>
public class ActionHandle
{
    private readonly TaskCompletionSource<ActionOutcome> _completion = new();

    /// <summary>A queued handle. Only the session creates handles.</summary>
    /// <param name="id">The session-unique id (also the request id of a tool request).</param>
    /// <param name="kind">What it is for.</param>
    /// <param name="source">Who asked.</param>
    /// <param name="summary">What was asked, for the log; never a secret.</param>
    /// <param name="queuedAt">When it was accepted, in session-clock seconds.</param>
    internal ActionHandle(long id, ActionKind kind, ActionSource source, string summary, double queuedAt)
    {
        ArgumentNullException.ThrowIfNull(summary);
        Id = id;
        Kind = kind;
        Source = source;
        Summary = summary;
        QueuedAt = queuedAt;
    }

    /// <summary>Raised once, inside the tick that completed the handle, after <see cref="Completion"/> completed.</summary>
    public event Action<ActionHandle>? Completed;

    /// <summary>The session-unique id (rising from 1).</summary>
    public long Id { get; }

    /// <summary>What the action is for.</summary>
    public ActionKind Kind { get; }

    /// <summary>Who asked.</summary>
    public ActionSource Source { get; }

    /// <summary>
    /// What was asked, as the action log shows it (<c>EnterWorld guid=0x50000001</c>, <c>Talk chars=12</c>). Arguments
    /// are summarised and never hold a secret.
    /// </summary>
    public string Summary { get; }

    /// <summary>Where the action is in its life.</summary>
    public ActionStatus Status { get; private set; }

    /// <summary>When it was accepted, in session-clock seconds.</summary>
    public double QueuedAt { get; }

    /// <summary>When it was sent to the server, or null (not sent yet, or never: refused, local, dry run).</summary>
    public double? SentAt { get; private set; }

    /// <summary>When it completed, or null while it has not.</summary>
    public double? CompletedAt { get; private set; }

    /// <summary>How it ended, or null while it has not.</summary>
    public ActionOutcome? Outcome { get; private set; }

    /// <summary>True once <see cref="Outcome"/> is set.</summary>
    public bool IsCompleted => Status == ActionStatus.Completed;

    /// <summary>Completes with the outcome, inside the tick that completed the handle; never faults or cancels.</summary>
    public Task<ActionOutcome> Completion => _completion.Task;

    /// <summary>For logs: <c>#7 EnterWorld (Ui) Sent: EnterWorld guid=0x50000001</c>.</summary>
    public override string ToString()
    {
        var state = Outcome is { } outcome ? outcome.Kind.ToString() : Status.ToString();
        return string.Create(CultureInfo.InvariantCulture, $"#{Id} {Kind} ({Source}) {state}: {Summary}");
    }

    /// <summary>Marks the handle sent at <paramref name="time"/> (session-clock seconds); ignored once completed.</summary>
    internal void MarkSent(double time)
    {
        if (Status == ActionStatus.Queued)
        {
            Status = ActionStatus.Sent;
            SentAt = time;
        }
    }

    /// <summary>
    /// Completes the handle with <paramref name="outcome"/> at <paramref name="time"/> (session-clock seconds), unless it
    /// already completed. Every <see cref="Completed"/> handler runs even when one throws: each exception goes to
    /// <paramref name="onHandlerError"/> (the session logs it as an ERROR and counts it) and never reaches the caller.
    /// </summary>
    /// <returns>True when this call completed the handle; false when it had already completed.</returns>
    internal bool TryComplete(ActionOutcome outcome, double time, Action<ActionHandle, Exception> onHandlerError)
    {
        ArgumentNullException.ThrowIfNull(onHandlerError);
        if (Status == ActionStatus.Completed)
        {
            return false;
        }

        Outcome = outcome;
        CompletedAt = time;
        Status = ActionStatus.Completed;
        _completion.TrySetResult(outcome);

        if (Completed is { } handlers)
        {
            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<ActionHandle>)handler)(this);
                }
                catch (Exception e)
                {
                    onHandlerError(this, e);
                }
            }
        }

        return true;
    }
}

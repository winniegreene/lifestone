namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The handle of a player-tool request (<see cref="PlayerTools"/>): an <see cref="ActionHandle"/> plus the typed result
/// the server mod's reply carried. <see cref="Result"/> is set when the reply confirms the request, before
/// <see cref="ActionHandle.Completion"/> completes; it stays null for every other outcome, except a reply the mod
/// carried out that the tool then judges a failure (<see cref="PlayerTools.LearnSpells"/> with an unknown id: a server error with the result set).
/// </summary>
/// <typeparam name="T">The result type (<see cref="ToolStatus"/>, <see cref="PlaceResult"/>, <see cref="WhereResult"/>,
/// <see cref="XpResult"/>, <see cref="QuestResult"/>, <see cref="CharPurgeResult"/>, <see cref="SpellLearnResult"/>, <see cref="ItemResult"/>).</typeparam>
public sealed class ToolHandle<T> : ActionHandle
    where T : class
{
    /// <summary>A queued tool handle. Only the session creates handles.</summary>
    internal ToolHandle(long id, ActionKind kind, ActionSource source, string summary, double queuedAt)
        : base(id, kind, source, summary, queuedAt)
    {
    }

    /// <summary>The typed result of a confirmed request, or null.</summary>
    public T? Result { get; private set; }

    /// <summary>
    /// Completes the handle with <paramref name="outcome"/>, setting <see cref="Result"/> to <paramref name="result"/>
    /// first, unless it already completed (see <see cref="ActionHandle.TryComplete"/>).
    /// </summary>
    internal bool TryComplete(T? result, ActionOutcome outcome, double time, Action<ActionHandle, Exception> onHandlerError)
    {
        if (IsCompleted)
        {
            return false;
        }

        Result = result;
        return TryComplete(outcome, time, onHandlerError);
    }
}

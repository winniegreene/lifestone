namespace Lifestone.Client.Session;

/// <summary>
/// The session's one-game-thread rule (docs/session.md §2): unbound until the first <see cref="Bind"/> (the session's
/// first <c>Tick</c>), then every <see cref="Check"/> from another thread throws <see cref="InvalidOperationException"/>.
/// Before binding, any thread may set up the session (subscribe, read), as hosts do before their loop starts. The same
/// rule as the network session's, which binds to the thread that first pumps it.
/// </summary>
/// <remarks>Not a lock: it detects misuse loudly; it does not make anything thread-safe.</remarks>
internal sealed class GameThreadBinding
{
    private int _threadId;

    /// <summary>True once <see cref="Bind"/> has run.</summary>
    internal bool IsBound => Volatile.Read(ref _threadId) != 0;

    /// <summary>The managed id of the bound thread, or 0 before binding.</summary>
    internal int ThreadId => Volatile.Read(ref _threadId);

    /// <summary>Binds to the calling thread on the first call; later calls check that they come from that thread.</summary>
    /// <param name="member">The member being called, for the message (e.g. <c>GameSession.Tick</c>).</param>
    /// <exception cref="InvalidOperationException">Bound to another thread.</exception>
    internal void Bind(string member)
    {
        var current = Environment.CurrentManagedThreadId;
        var bound = Interlocked.CompareExchange(ref _threadId, current, 0);
        if (bound != 0 && bound != current)
        {
            throw WrongThread(member, bound, current);
        }
    }

    /// <summary>Throws when bound to a thread other than the caller's; does nothing before binding.</summary>
    /// <param name="member">The member being called, for the message (e.g. <c>IEventBus.Subscribe</c>).</param>
    /// <exception cref="InvalidOperationException">Bound to another thread.</exception>
    internal void Check(string member)
    {
        var bound = Volatile.Read(ref _threadId);
        var current = Environment.CurrentManagedThreadId;
        if (bound != 0 && bound != current)
        {
            throw WrongThread(member, bound, current);
        }
    }

    private static InvalidOperationException WrongThread(string member, int bound, int current) =>
        new($"{member} must be called on the session's game thread (managed thread {bound}), not thread {current}: " +
            "the session is not thread-safe (docs/session.md §2).");
}

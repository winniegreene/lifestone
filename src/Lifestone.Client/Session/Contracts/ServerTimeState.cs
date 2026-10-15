namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The server's clock (<see cref="GameState.Time"/>): the last server time the session heard (ConnectRequest, then
/// TimeSync) and when, so the server's time at any client moment can be estimated by elapsed time.
/// </summary>
public sealed class ServerTimeState
{
    /// <summary>Nothing heard yet. Only the session creates one.</summary>
    internal ServerTimeState()
    {
    }

    /// <summary>True once a server time arrived.</summary>
    public bool IsKnown { get; private set; }

    /// <summary>The last server time heard: the portal-year clock, in seconds.</summary>
    public double ServerSeconds { get; private set; }

    /// <summary>When it was heard, in <see cref="SessionClock"/> seconds.</summary>
    public double ClientSeconds { get; private set; }

    /// <summary>The server's time at <paramref name="clientSeconds"/> (session-clock seconds), or null while unknown.</summary>
    /// <param name="clientSeconds">A session-clock time, normally <see cref="SessionClock.Now"/>.</param>
    /// <returns>The estimated server time, in seconds.</returns>
    public double? ServerSecondsAt(double clientSeconds) => IsKnown ? ServerSeconds + (clientSeconds - ClientSeconds) : null;

    /// <summary>Records a server time heard at a session-clock time.</summary>
    internal void Update(double serverSeconds, double clientSeconds)
    {
        ServerSeconds = serverSeconds;
        ClientSeconds = clientSeconds;
        IsKnown = true;
    }

    /// <summary>Forgets the server time (a reconnect).</summary>
    internal void Reset()
    {
        IsKnown = false;
        ServerSeconds = 0;
        ClientSeconds = 0;
    }
}

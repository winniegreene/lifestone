using System.Net;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions;

/// <summary>
/// What a live session's Action API acts on: the server it was created for, the current network session, and how to log
/// in with the credentials the session holds. The session (ST13) builds one; an offline session has none, and its Action
/// API refuses every server action.
/// </summary>
/// <remarks>No secret passes through here: <see cref="Login"/> calls back into the session, which owns the credentials.</remarks>
internal sealed class ActionLink
{
    private readonly Func<IAcSession?> _session;
    private readonly Action<IAcSession> _login;

    /// <summary>A link to <paramref name="server"/>.</summary>
    /// <param name="server">The server the session was created for (the loopback gate's checks repeat on it for automation and the tools).</param>
    /// <param name="session">The current network session, or null when there is none (before it is created, after it closed).</param>
    /// <param name="login">Starts a login on the given network session with the session's credentials.</param>
    internal ActionLink(EndPoint server, Func<IAcSession?> session, Action<IAcSession> login)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(login);
        Server = server;
        _session = session;
        _login = login;
    }

    /// <summary>The server the session was created for.</summary>
    internal EndPoint Server { get; }

    /// <summary>The current network session, or null.</summary>
    internal IAcSession? Session => _session();

    /// <summary>Starts a login on <paramref name="session"/> (net's <see cref="IAcSession.Login"/>, with the session's credentials).</summary>
    internal void Login(IAcSession session) => _login(session);
}

/// <summary>
/// The Action API's local-notice codes (<see cref="Recording.RecordingNotice.Code"/>), recorded so a replay can reproduce
/// what the Action API published without a server message. Codes 1 to 15 are the reducer's
/// (<see cref="State.SessionNotices"/>); these start at 16.
/// </summary>
/// <remarks>The values are part of the <c>.lsrec</c> format: never renumber one, only add.</remarks>
internal static class ActionNotices
{
    /// <summary>The first code the Action API owns.</summary>
    internal const ushort First = State.SessionNotices.LastReducerCode + 1;

    /// <summary><see cref="Contracts.LogOffRequested"/> was published (<c>Value</c>: 1 when the player-killer delay applies, else 0).</summary>
    internal const ushort LogOffRequested = First;

    /// <summary>The last code assigned so far.</summary>
    internal const ushort Last = LogOffRequested;

    /// <summary>True when <paramref name="code"/> is one of the Action API's (<see cref="First"/> to <see cref="Last"/>).</summary>
    internal static bool Owns(ushort code) => code is >= First and <= Last;

    /// <summary>
    /// Publishes again what the Action API published live for <paramref name="notice"/> (a replay: the Action API's
    /// <c>ApplyRecordedNotice</c> and the replayer share it).
    /// </summary>
    /// <param name="bus">The session's bus.</param>
    /// <param name="notice">The recorded notice.</param>
    /// <param name="frameTime">The frame time to stamp the event with (the replay's own).</param>
    /// <exception cref="InvalidDataException">The code is not one the Action API knows.</exception>
    internal static void Publish(Bus.EventBus bus, in Recording.RecordingNotice notice, double frameTime)
    {
        switch (notice.Code)
        {
            case LogOffRequested:
                bus.Publish(new Contracts.LogOffRequested(frameTime, notice.Value != 0));
                break;
            default:
                throw new InvalidDataException(
                    $"local notice code {notice.Code} does not belong to the Action API (codes {First}-{Last}; ActionNotices)");
        }
    }
}

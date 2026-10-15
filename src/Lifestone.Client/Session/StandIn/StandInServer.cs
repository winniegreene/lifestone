using Lifestone.Net.Dispatch;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.StandIn;

/// <summary>
/// What the play-mode checks can make the offline stand-in server do from outside its script: boot the player, or go quiet. Both end the session in failure,
/// as the network session does, and the retail disconnected screen reports it (its button reconnects, D43). Made by <see cref="StandInSessions.Server"/>.
/// </summary>
public sealed class StandInServer
{
    private readonly IInboundMessageHandler _handler;

    internal StandInServer(IInboundMessageHandler handler) => _handler = handler;

    /// <summary>The server boots the player <paramref name="after"/> seconds from now (session clock): F7DC with <paramref name="reason"/>, then the failure.</summary>
    public void Boot(double after, string? reason) => StandInSessions.BootLater(_handler, after, reason);

    /// <summary>
    /// Lets a held visit (<see cref="StandInVisit.HoldEntry"/>) begin: the player enters <paramref name="after"/> seconds from now (session clock), as a live server answers Enter about
    /// 0.1 s after it is pressed. False when no visit is held.
    /// </summary>
    public bool Release(double after) => StandInSessions.ReleaseEntry(_handler, after);

    /// <summary>The server stops answering: after <paramref name="after"/> seconds the session fails as a silent server's does (<see cref="SessionFailureKind.Silence"/>).</summary>
    public void GoQuiet(double after, string message) =>
        StandInSessions.FailLater(_handler, after, new SessionFailure(SessionFailureKind.Silence, SessionPhase.InWorld, message));
}

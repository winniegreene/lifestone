namespace Lifestone.Net.Session;

/// <summary>Why a session failed (<see cref="IAcSession.Failure"/>). Never holds credentials or payload bytes.</summary>
/// <param name="Kind">The kind of failure.</param>
/// <param name="Phase">The phase the session was in when it failed.</param>
/// <param name="Message">One sentence for the log and the disconnected screen.</param>
/// <param name="Code">The server's code where there is one (a CharacterError code), else 0.</param>
public sealed record SessionFailure(SessionFailureKind Kind, SessionPhase Phase, string Message, uint Code = 0);

/// <summary>The kinds of <see cref="SessionFailure"/>.</summary>
public enum SessionFailureKind
{
    /// <summary>No ConnectRequest arrived, after every allowed attempt.</summary>
    ConnectTimeout,

    /// <summary>The handshake did not complete within <see cref="SessionTimeouts.Handshake"/>.</summary>
    HandshakeTimeout,

    /// <summary>The data check did not complete within <see cref="SessionTimeouts.DataCheck"/>.</summary>
    DataCheckTimeout,

    /// <summary>The server sent DDD_BeginDDD: the dats do not match the server's.</summary>
    DataCheckMismatch,

    /// <summary>A CharacterError ended the login (after any allowed retries).</summary>
    CharacterError,

    /// <summary>The server booted the session (F7DC).</summary>
    Booted,

    /// <summary>The account is banned (F7C1).</summary>
    AccountBanned,

    /// <summary>EnterWorld did not complete within <see cref="SessionTimeouts.EnterWorld"/>.</summary>
    EnterWorldTimeout,

    /// <summary>Logoff did not complete within <see cref="SessionTimeouts.Logoff"/>.</summary>
    LogoffTimeout,

    /// <summary>Nothing arrived for <see cref="SessionTimeouts.Silence"/> while the session was being pumped.</summary>
    Silence,

    /// <summary>
    /// Reserved for a transport that gives up (an unrecoverable gap, a reject it cannot honour, a server disconnect).
    /// The session never raises it in M2. ACE sends a client no Disconnect. The inbound sequencer skips a lost message
    /// instead of giving up (<see cref="SessionWarning.GapSkipped"/>). A server that stops answering fails with
    /// <see cref="Silence"/>.
    /// </summary>
    TransportTerminated,

    /// <summary>A message the session depends on could not be parsed.</summary>
    ProtocolError,

    /// <summary>
    /// A login attempt's socket could not be opened. A send the system refuses counts as a lost datagram. A socket that
    /// later stops receiving logs an error and the session ends with <see cref="Silence"/>.
    /// </summary>
    SocketError,
}

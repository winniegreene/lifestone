using System.Net;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Sockets;

namespace Lifestone.Net.Session;

/// <summary>
/// Everything <see cref="AcSession.Create"/> needs besides the sink and the tap. Only <see cref="Server"/> and
/// <see cref="Iterations"/> are required; the rest default to Lifestone's behaviour. <c>--retail-net</c> maps to
/// <see cref="SessionTimeouts.Retail"/>, <see cref="LoginRetryPolicy.Retail"/>, <see cref="TailProbe"/> false and
/// <see cref="InboundOrder.Arrival"/> (owner decision D18).
/// </summary>
public sealed record AcSessionOptions
{
    /// <summary>
    /// ACE's login endpoint, an IP literal on loopback (127.0.0.1:9000); the world port is Port + 1. Checked by
    /// <see cref="LoopbackPolicy.Require"/> before anything else happens.
    /// </summary>
    public required IPEndPoint Server { get; init; }

    /// <summary>The dat iterations to report in the data check, from the host's <c>DatSet.Iterations</c>.</summary>
    public required DddIterations Iterations { get; init; }

    /// <summary>The client version string of the LoginRequest; ACE boots anything but "1802".</summary>
    public string ClientVersion { get; init; } = "1802";

    /// <summary>The only clock the session reads: transport timers, handshake, watchdog, reassembly, login timestamp.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>The timeouts.</summary>
    public SessionTimeouts Timeouts { get; init; } = SessionTimeouts.Default;

    /// <summary>The automatic login retries.</summary>
    public LoginRetryPolicy Retry { get; init; } = LoginRetryPolicy.Default;

    /// <summary>
    /// The tail probe (Lifestone deviation, N8): when a server ack arrives that is behind the last packet sent and the
    /// packet right after it went out at least 1 s before, send that one packet again (its key and the Retransmission
    /// flag kept), so a lost last packet is not stranded by ACE's NAK rule (it NAKs only a gap of two). At most one probe
    /// per ack, never above ack + 1; counted in <see cref="LinkStats.TailProbes"/>. With it, acks wait while a sent packet
    /// is unacked: ACE keeps the first packet it gets for a sequence ahead of a gap, so an ack-only packet (which carries
    /// the last sequence sent) could take a lost data packet's place and lose it for good. False turns both off
    /// (<c>--retail-net</c>, owner decision D18): only what the server NAKs is resent, and acks go out every 2 s.
    /// </summary>
    public bool TailProbe { get; init; } = true;

    /// <summary>The inbound message order.</summary>
    public InboundOrder Order { get; init; } = InboundOrder.FragmentSequence;

    /// <summary>
    /// Opens a socket for a (loopback-checked) server endpoint: tests, the in-memory network and the lossy decorator
    /// supply one. Null uses the default loopback UDP socket. Called only after the loopback check, once per login
    /// attempt.
    /// </summary>
    public Func<IPEndPoint, IDatagramSocket>? SocketFactory { get; init; }

    /// <summary>Where the session logs; null logs nothing.</summary>
    public INetLog? Log { get; init; }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> naming the first unusable option. <see cref="Server"/>'s address is the
    /// loopback policy's business; here only its port is checked (1 to 65534, so that the world port exists).
    /// </summary>
    internal void Validate()
    {
        if (Server is null || Server.Port is < 1 or >= IPEndPoint.MaxPort)
        {
            throw new ArgumentException($"the server port must be 1 to {IPEndPoint.MaxPort - 1} (the world port is Port + 1)", nameof(Server));
        }

        if (Iterations.Portal <= 0 || Iterations.Language <= 0 || Iterations.Cell < 0 || Iterations.HighRes is <= 0)
        {
            throw new ArgumentException(
                "the dat iterations must be positive (portal and language required, cell 0 or more, high-res absent or positive)", nameof(Iterations));
        }

        if (string.IsNullOrEmpty(ClientVersion) || ClientVersion.Any(c => c is < ' ' or > '~'))
        {
            throw new ArgumentException("the client version must be a non-empty printable ASCII string", nameof(ClientVersion));
        }

        ArgumentNullException.ThrowIfNull(Time, nameof(Time));
        ArgumentNullException.ThrowIfNull(Timeouts, nameof(Timeouts));
        ArgumentNullException.ThrowIfNull(Retry, nameof(Retry));
        TimeSpan[] spans =
        [
            Timeouts.ConnectRequest, Timeouts.ConnectResponseDelay, Timeouts.ConnectResponseInterval, Timeouts.Handshake,
            Timeouts.DataCheck, Timeouts.CharacterOperation, Timeouts.EnterWorld, Timeouts.Logoff, Timeouts.Silence,
        ];
        if (spans.Any(s => s <= TimeSpan.Zero))
        {
            throw new ArgumentException("every session timeout must be positive", nameof(Timeouts));
        }

        if (Retry.Attempts < 1 || Retry.Delay < TimeSpan.Zero || Retry.RetryableCharacterErrors.IsDefault)
        {
            throw new ArgumentException("the retry policy needs at least one attempt, a delay of 0 or more and a list of codes", nameof(Retry));
        }

        if (Retry.CharacterInWorldWait < TimeSpan.Zero || (Retry.CharacterInWorldWait > TimeSpan.Zero && Retry.CharacterInWorldDelay <= TimeSpan.Zero))
        {
            throw new ArgumentException("the retry policy's wait for a character the server still holds needs a wait of 0 or more and, when it is more than 0, a positive delay", nameof(Retry));
        }
    }
}

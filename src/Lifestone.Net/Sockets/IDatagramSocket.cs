using System.Net;

namespace Lifestone.Net.Sockets;

/// <summary>
/// One UDP socket of a session, bound to a loopback address and able to talk only to the server address it was opened
/// for (ACE's login port <see cref="IPEndPoint.Port"/> and its world port, Port + 1; docs/protocol.md §1). The session
/// gets one from <see cref="Session.AcSessionOptions.SocketFactory"/>, after <see cref="LoopbackPolicy.Require"/> has
/// passed the server endpoint, and opens a fresh one for each login attempt.
/// </summary>
/// <remarks>
/// The default implementation (N2, <c>UdpLoopbackSocket</c>) binds the loopback literal that matches the server
/// (127.0.0.1:0 or [::1]:0) and drops any datagram that does not come from the server's address on Port or Port + 1,
/// counting it in <see cref="DatagramsFiltered"/>. Test doubles (the in-memory network, the seeded lossy decorator)
/// implement the same contract. Single-threaded use from the session's game thread, except
/// <see cref="WaitForDatagramAsync"/>.
/// </remarks>
public interface IDatagramSocket : IDisposable
{
    /// <summary>The server endpoint this socket was opened for (its login port; the world port is Port + 1).</summary>
    IPEndPoint Server { get; }

    /// <summary>The local loopback endpoint the socket is bound to.</summary>
    IPEndPoint LocalEndPoint { get; }

    /// <summary>Datagrams dropped because they came from anywhere but the server's two ports.</summary>
    long DatagramsFiltered { get; }

    /// <summary>
    /// Sends one datagram to the server's address on <paramref name="port"/>, which must be <see cref="Server"/>'s Port
    /// or Port + 1 (<see cref="ArgumentOutOfRangeException"/> otherwise).
    /// </summary>
    void Send(int port, ReadOnlySpan<byte> datagram);

    /// <summary>
    /// Copies the next received datagram into <paramref name="destination"/> without blocking. False when none is
    /// waiting. <paramref name="sourcePort"/> is the server port it came from (Port or Port + 1). A datagram longer than
    /// <paramref name="destination"/> is dropped and counted as filtered.
    /// </summary>
    bool TryReceive(Span<byte> destination, out int length, out int sourcePort);

    /// <summary>
    /// Completes with true as soon as a datagram is waiting, or with false after <paramref name="max"/> (measured on the
    /// session's <see cref="TimeProvider"/>) or on cancellation. For hosts that sleep between pumps (the headless host).
    /// It also completes with false at once when the socket can deliver nothing more (it was disposed during the wait,
    /// or its receive loop ended after a fault); the session then sleeps out the rest of its own wait.
    /// </summary>
    ValueTask<bool> WaitForDatagramAsync(TimeSpan max, CancellationToken cancellationToken);
}

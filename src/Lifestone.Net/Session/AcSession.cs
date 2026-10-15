using Lifestone.Net.Dispatch;
using Lifestone.Net.Sockets;
using Lifestone.Net.Taps;

namespace Lifestone.Net.Session;

/// <summary>
/// Creates sessions. The loopback gate runs first: <see cref="Create"/> refuses a non-loopback server before it
/// validates anything else and long before any socket factory is touched, so nothing is ever contacted.
/// </summary>
public static class AcSession
{
    /// <summary>
    /// Creates an idle session for <paramref name="options"/>, reporting to <paramref name="sink"/> and, when given,
    /// to the raw <paramref name="tap"/>. Opens no socket: that happens on <see cref="IAcSession.Login"/>.
    /// </summary>
    /// <exception cref="LoopbackRequiredException">The server is not a loopback IP endpoint.</exception>
    /// <exception cref="ArgumentException">An option is unusable (a port of 0 or 65535, missing iterations, a non-ASCII
    /// client version, a non-positive timeout, a retry policy without attempts).</exception>
    public static IAcSession Create(AcSessionOptions options, IAcSessionSink sink, IRawMessageTap? tap = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var server = LoopbackPolicy.Require(options.Server);
        ArgumentNullException.ThrowIfNull(sink);
        options.Validate();
        return new SessionMachine(options with { Server = server }, sink, tap);
    }
}

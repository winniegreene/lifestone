using System.Diagnostics.CodeAnalysis;
using System.Net;
using Lifestone.Client.Diagnostics;
using Lifestone.Net.Sockets;

namespace Lifestone.Client.Session;

/// <summary>
/// The session layer's loopback gate (CLAUDE.md "Network"; A0's S8; docs/session.md §10): every runner that could
/// contact a server (session creation, the headless runner, the player tools, automation actions) asks it first. It
/// logs one WARNING naming the runner and the reason, then applies net's <see cref="LoopbackPolicy"/>, the one rule:
/// only loopback IP literals pass (127.0.0.0/8, ::1 and their IPv4-mapped forms); a host name is never resolved, and the
/// wildcard, broadcast, private and public addresses are refused. Nothing here opens a socket or contacts anything.
/// </summary>
/// <remarks>
/// Lifestone's own rule, not retail behaviour. The gate adds the log line and the runner's name; the decision is net's,
/// so the two cannot drift (<c>LoopbackGateTests.Gate_and_net_policy_agree</c>).
/// </remarks>
public static class LoopbackGate
{
    /// <summary>
    /// True when <paramref name="endPoint"/> may be contacted (<see cref="LoopbackPolicy.IsAllowed"/>), with no log line:
    /// for validating input as it is typed.
    /// </summary>
    public static bool IsAllowed([NotNullWhen(true)] EndPoint? endPoint, [NotNullWhen(false)] out string? refusal) =>
        LoopbackPolicy.IsAllowed(endPoint, out refusal);

    /// <summary>
    /// Returns the endpoint to contact (a fresh copy, IPv4-mapped loopback turned into IPv4; use it, not the argument),
    /// or logs one WARNING naming <paramref name="runner"/> and the reason and throws.
    /// </summary>
    /// <param name="endPoint">The endpoint the runner was asked to contact.</param>
    /// <param name="runner">Who asks, for the log line (e.g. <c>SessionBootstrap.CreateLive</c>).</param>
    /// <param name="log">Where the refusal is logged (category <c>session</c>).</param>
    /// <exception cref="LoopbackRequiredException">The endpoint is not a loopback IP endpoint.</exception>
    public static IPEndPoint Require(EndPoint? endPoint, string runner, ILogSink log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runner);
        ArgumentNullException.ThrowIfNull(log);
        try
        {
            return LoopbackPolicy.Require(endPoint);
        }
        catch (LoopbackRequiredException e)
        {
            LogRefusal(runner, e.Refusal, log);
            throw;
        }
    }

    /// <summary>
    /// <see cref="Require"/> without the exception, for runners that refuse by completing a handle (the player tools,
    /// automation actions): true with the endpoint to contact, or false with the reason after logging one WARNING.
    /// </summary>
    /// <param name="endPoint">The endpoint the runner was asked to contact.</param>
    /// <param name="runner">Who asks, for the log line.</param>
    /// <param name="log">Where the refusal is logged (category <c>session</c>).</param>
    /// <param name="allowed">The endpoint to contact when allowed.</param>
    /// <param name="refusal">Why it was refused, one sentence naming the endpoint.</param>
    public static bool TryRequire(
        EndPoint? endPoint,
        string runner,
        ILogSink log,
        [NotNullWhen(true)] out IPEndPoint? allowed,
        [NotNullWhen(false)] out string? refusal)
    {
        try
        {
            allowed = Require(endPoint, runner, log);
            refusal = null;
            return true;
        }
        catch (LoopbackRequiredException e)
        {
            allowed = null;
            refusal = e.Refusal;
            return false;
        }
    }

    private static void LogRefusal(string runner, string refusal, ILogSink log) =>
        log.Warn(SessionLog.Category, $"loopback gate: {runner} refused: {refusal}", new LogField("runner", runner));
}

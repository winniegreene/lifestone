#nullable enable
using System;
using System.Net;

namespace Lifestone.Net.Sockets;

/// <summary>
/// Thrown by <see cref="LoopbackPolicy.Require"/> (and so by <c>Lifestone.Net.Session.AcSession.Create</c>) when an
/// endpoint is not a loopback IP endpoint. Nothing was opened or contacted. The message names the endpoint and the
/// reason.
/// </summary>
public sealed class LoopbackRequiredException : InvalidOperationException
{
    /// <summary>Creates the exception for <paramref name="endPoint"/>, refused for <paramref name="refusal"/>.</summary>
    public LoopbackRequiredException(EndPoint? endPoint, string refusal)
        : base("Refused to contact a non-loopback endpoint: " + refusal + ".")
    {
        EndPoint = endPoint;
        Refusal = refusal;
    }

    /// <summary>The endpoint that was refused (null when none was given).</summary>
    public EndPoint? EndPoint { get; }

    /// <summary>Why it was refused, one sentence (<see cref="LoopbackPolicy.IsAllowed"/>'s refusal).</summary>
    public string Refusal { get; }
}

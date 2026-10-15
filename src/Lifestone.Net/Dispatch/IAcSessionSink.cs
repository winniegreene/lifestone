using Lifestone.Net.Session;

namespace Lifestone.Net.Dispatch;

/// <summary>
/// What a live session reports to: every decoded message (<see cref="IInboundMessageHandler"/>) plus the session's own
/// events (phase changes, retries, boots, failures). The state track's reducer implements it. Called on the session's
/// game thread from <see cref="IAcSession.Pump"/>.
/// </summary>
public interface IAcSessionSink : IInboundMessageHandler
{
    /// <summary>A session event (<see cref="SessionEvent.Kind"/> says which).</summary>
    void OnSessionEvent(in SessionEvent sessionEvent);
}

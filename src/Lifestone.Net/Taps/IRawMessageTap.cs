using Lifestone.Net.Types;

namespace Lifestone.Net.Taps;

/// <summary>
/// The A0 raw tap (docs/automation-vision.md): sees every reassembled game message in both directions, as bytes, for
/// the event bus's raw subscribers, the session recorder and the golden-fixture capture.
/// </summary>
/// <remarks>
/// <para>Only game messages reach it. LoginRequest and ConnectResponse are packet-level sections, not messages, so the
/// password can never reach a tap, a recording or a fixture (tested with a sentinel password).</para>
/// <para>Inbound messages are tapped immediately before dispatch, in delivery order; outbound messages when they are
/// queued, with the fragment sequence the transport assigned (the <see cref="Session.OutboundTicket"/> carries the same
/// number). The body is valid only during the call. Called on the session's game thread.</para>
/// </remarks>
public interface IRawMessageTap
{
    /// <summary>One message: its direction, the session clock's timestamp, its fragment sequence, its queue and its bytes
    /// (opcode included).</summary>
    void OnMessage(MessageDirection direction, long timestamp, uint fragmentSequence, MessageQueue queue, ReadOnlySpan<byte> body);

    /// <summary>
    /// An inbound message took about 5 ms or more to dispatch on the game thread (the DoD 15 burst, <c>TODO(login-burst-frame)</c>): called after its dispatch, only for a slow one.
    /// </summary>
    /// <param name="opcode">The message's opcode.</param>
    /// <param name="eventType">The game event type inside an F7B0 envelope, else 0.</param>
    /// <param name="totalMilliseconds">The whole dispatch: decode, the session's bookkeeping and the sink.</param>
    /// <param name="decodeMilliseconds">The part before the sink's first call (decode and the session's bookkeeping); the sink's own work, with the bus subscribers it runs, is the rest.</param>
    void OnDispatched(uint opcode, uint eventType, double totalMilliseconds, double decodeMilliseconds)
    {
    }
}

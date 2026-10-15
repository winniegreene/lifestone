namespace Lifestone.Net.Session;

/// <summary>
/// Returned by every send of <see cref="IAcSession"/>: the fragment sequence the transport gave the message (the same
/// number the raw tap reports for it) and the session clock's timestamp when it was queued. The state track's action
/// log correlates actions with the wire through it.
/// </summary>
/// <param name="MessageSequence">The outbound fragment sequence (starts at 1, +1 per message).</param>
/// <param name="Timestamp">When the message was queued, in the session clock's timestamp units.</param>
public readonly record struct OutboundTicket(uint MessageSequence, long Timestamp);

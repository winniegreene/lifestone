using Lifestone.Net.Types;

namespace Lifestone.Net.Dispatch;

/// <summary>
/// Where an inbound message came from: its fragment sequence (the server's message id, global and rising by one per
/// message), the queue it arrived on, and when it was received (a <see cref="TimeProvider.GetTimestamp"/> value of the
/// session's clock). A replay reconstructs the same context from the recording.
/// </summary>
/// <param name="FragmentSequence">The message's fragment sequence.</param>
/// <param name="Queue">The fragment queue (message group).</param>
/// <param name="ReceivedTimestamp">The receive time, in the session clock's timestamp units.</param>
public readonly record struct InboundContext(uint FragmentSequence, MessageQueue Queue, long ReceivedTimestamp);

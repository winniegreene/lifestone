namespace Lifestone.Net.Messages;

/// <summary>GameEvent 0x02EB CommunicationTransientString (ACE <c>GameEventCommunicationTransientString</c>): a transient
/// on-screen message.</summary>
/// <param name="Text">The message.</param>
public readonly record struct TransientStringEvent(string Text);

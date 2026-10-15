namespace Lifestone.Net.Messages;

/// <summary>GameEvent 0x01C0 UpdateHealth (ACE <c>GameEventUpdateHealth</c>): the answer to QueryHealth.</summary>
/// <param name="Guid">The queried object.</param>
/// <param name="Health">Its health as a fraction, 0 to 1.</param>
public readonly record struct UpdateHealthEvent(uint Guid, float Health);

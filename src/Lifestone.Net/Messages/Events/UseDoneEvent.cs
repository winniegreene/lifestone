namespace Lifestone.Net.Messages;

/// <summary>GameEvent 0x01C7 UseDone (ACE <c>GameEventUseDone</c>): a Use finished; 0 is success, else a WeenieError.</summary>
/// <param name="Error">The raw WeenieError code (0 = none).</param>
public readonly record struct UseDoneEvent(uint Error);

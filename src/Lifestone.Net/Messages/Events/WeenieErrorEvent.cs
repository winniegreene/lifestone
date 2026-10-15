namespace Lifestone.Net.Messages;

/// <summary>
/// GameEvent 0x028A WeenieError and 0x028B WeenieErrorWithString (ACE <c>GameEventWeenieError</c>,
/// <c>GameEventWeenieErrorWithString</c>): an error code, and for 0x028B a string the client's message is built with.
/// </summary>
/// <param name="Error">The raw WeenieError code.</param>
/// <param name="Text">The string (0x028B only), else null.</param>
public readonly record struct WeenieErrorEvent(uint Error, string? Text);

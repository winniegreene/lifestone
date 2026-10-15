namespace Lifestone.Net.Messages;

/// <summary>F7C1 AccountBanned (ACE <c>GameMessageAccountBanned</c>): the seconds left on the ban and an optional reason.</summary>
/// <param name="SecondsRemaining">The seconds until the ban expires, as the server computed them.</param>
/// <param name="Reason">The reason, or null when the message carries none.</param>
public readonly record struct AccountBannedMsg(uint SecondsRemaining, string? Reason);

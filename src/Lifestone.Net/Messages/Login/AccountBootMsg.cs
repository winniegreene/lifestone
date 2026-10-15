namespace Lifestone.Net.Messages;

/// <summary>F7DC AccountBoot: the server closes the session. The String16L reason is optional (m2-plan §1.6 item 24).</summary>
/// <param name="Reason">The reason, or null when the message carries none.</param>
public readonly record struct AccountBootMsg(string? Reason);

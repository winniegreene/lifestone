namespace Lifestone.Net.Messages;

/// <summary>GameEvent 0x0004 PopupString (ACE <c>GameEventPopupString</c>): a message box, such as the first-login welcome.</summary>
/// <param name="Text">The message.</param>
public readonly record struct PopupEvent(string Text);

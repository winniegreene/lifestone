namespace Lifestone.Client.Session.State;

/// <summary>
/// The local-notice codes (<see cref="Recording.RecordingNotice.Code"/>): changes the session makes to its state without
/// a server message, recorded so a replay reapplies them through the same reducer
/// (<see cref="GameStateReducer.ApplyLocal"/>). Codes 1 to <see cref="LastReducerCode"/> belong to the reducer (ST7, ST8);
/// the Action API (ST9, ST10) takes the codes above. A replay that meets a code nobody knows fails loudly.
/// </summary>
/// <remarks>The values are part of the <c>.lsrec</c> format: never renumber one, only add.</remarks>
internal static class SessionNotices
{
    /// <summary>A character was selected in the list (<c>Value</c>: its guid, 0 for none).</summary>
    internal const ushort CharacterSelected = 1;

    /// <summary>LoginComplete was sent (the player goes from arriving to materialising).</summary>
    internal const ushort LoginCompleteSent = 2;

    /// <summary>The client added a line of its own to the chat (<c>Text</c>: the line, such as "Logging off...").</summary>
    internal const ushort ClientChatLine = 3;

    /// <summary>F656 CharacterCreate was sent (the next F643 answers it).</summary>
    internal const ushort CharacterCreateSent = 4;

    /// <summary>F7D9 CharacterRestore was sent (<c>Value</c>: the character; the next F643 answers it).</summary>
    internal const ushort CharacterRestoreSent = 5;

    /// <summary>A tool request is silent (<c>Text</c>: its request id): its reply line stays out of the chat.</summary>
    internal const ushort ToolRequestSilent = 6;

    /// <summary>
    /// The session was reset for a reconnect (<c>GameSession.Reconnect</c>, ST13): the state goes back to an empty one before
    /// login (<see cref="GameStateReducer.Reset"/>), and the next frames belong to a new network session.
    /// </summary>
    internal const ushort SessionReset = 7;

    /// <summary>
    /// The player changed an option for this session (the options panel; <c>IActionApi.SetSessionOption</c>): <c>Value</c> is the option's bit and <c>Text</c> is <c>word:on</c> (<c>1:1</c>, <c>2:0</c>).
    /// Nothing goes to the server (D42, <c>TODO(g6-options-save)</c>); the reducer owns the override and a replay reapplies it.
    /// </summary>
    internal const ushort SessionOptionSet = 8;

    /// <summary>The last code reserved for the reducer.</summary>
    internal const ushort LastReducerCode = 15;
}

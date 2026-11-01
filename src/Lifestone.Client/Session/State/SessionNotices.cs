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

    /// <summary>
    /// The client composed retail's "&lt;spell&gt; has expired." for a 02C3 or 02C5 (<c>Value</c>: the spell id; <c>Text</c>: the line). The name comes from
    /// the SpellTable, which a recording does not hold, so the line is recorded for the replay (M4 P5b F4, TODO(m4-replay-vitae-digest)): a replay adds the
    /// recorded line and composes none of its own when the recording carries it.
    /// </summary>
    internal const ushort ExpiryLine = 9;

    /// <summary>
    /// The final state's digest parts (<c>Text</c>: <see cref="Recording.GameStateDigest.FormatParts"/>), written by the session just before the trailer
    /// (M4 P5b F4). It changes no state: a replay compares its own parts with it and names the parts that differ.
    /// </summary>
    internal const ushort DigestParts = 10;

    /// <summary>
    /// The player answered an open server question (M5 6 w2; <c>IActionApi.RespondToConfirmation</c>): <c>Value</c> is the type word and <c>Text</c> is
    /// <c>context:answer</c> (<c>17:1</c> yes, <c>17:0</c> no). The question closes locally when the answer is sent; a replay closes it again.
    /// </summary>
    internal const ushort ConfirmationAnswered = 11;

    /// <summary>
    /// The player set both character option words (M5 6 w2, D32; <c>IActionApi.SetCharacterOptions</c>): <c>Value</c> is the first word and <c>Text</c> the second
    /// in hexadecimal (eight digits). The reducer applies them at once, as retail's player module does, and GameAction 0x01A1 takes them to the server.
    /// </summary>
    internal const ushort CharacterOptionsSet = 12;

    /// <summary>The last code reserved for the reducer.</summary>
    internal const ushort LastReducerCode = 15;
}

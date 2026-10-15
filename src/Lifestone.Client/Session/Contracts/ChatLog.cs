namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The chat (<see cref="GameState.Chat"/>): the last <see cref="Capacity"/> lines, oldest first, from speech, emotes,
/// tells, system messages, popups, errors, the tools' reply lines and the client's own notices ("Logging off...").
/// Server tool-reply JSON never appears here. <see cref="ChatReceived"/> follows every line.
/// </summary>
/// <remarks>Game thread only.</remarks>
public sealed class ChatLog
{
    /// <summary>How many lines the log keeps.</summary>
    public const int Capacity = 500;

    private readonly RingBuffer<ChatLine> _ring = new(Capacity);

    /// <summary>An empty log. Only the session creates one.</summary>
    internal ChatLog()
    {
    }

    /// <summary>The number of lines kept (at most <see cref="Capacity"/>).</summary>
    public int Count => _ring.Count;

    /// <summary>Every line ever added, including those the log has dropped.</summary>
    public long Total => _ring.Total;

    /// <summary>
    /// <see cref="Total"/> when the current world entry began (the session entered <c>EnteringWorld</c>): the lines from there
    /// on belong to the current in-game shell, whose chat starts empty as retail's does (the chat log is an element of
    /// <c>gmGamePlayUI</c>, deleted with it). 0 before any entry.
    /// </summary>
    public long EntryMark { get; internal set; }

    /// <summary>The line at <paramref name="index"/>, 0 being the oldest kept.</summary>
    /// <param name="index">0 to <see cref="Count"/> - 1.</param>
    public ChatLine this[int index] => _ring[index];

    /// <summary>Adds a line, dropping the oldest when the log is full.</summary>
    internal void Add(in ChatLine line) => _ring.Add(line);

    /// <summary>Removes every line.</summary>
    internal void Clear() => _ring.Clear();
}

/// <summary>One chat line.</summary>
/// <param name="Time">When it arrived (or was added), in session-clock seconds.</param>
/// <param name="Kind">Where it came from.</param>
/// <param name="ChatType">The server's chat type (ACE's ChatMessageType) when the message carried one; for a
/// <see cref="ChatKind.WeenieError"/> line, the WeenieError code (the retail UI looks its text up when the server sent
/// none); else 0.</param>
/// <param name="Sender">The sender's name, when there is one.</param>
/// <param name="SenderGuid">The sender's object id, or 0.</param>
/// <param name="Text">The text, as the server sent it.</param>
public readonly record struct ChatLine(double Time, ChatKind Kind, uint ChatType, string? Sender, uint SenderGuid, string Text);

/// <summary>Where a <see cref="ChatLine"/> came from.</summary>
public enum ChatKind
{
    /// <summary>0x02BB HearSpeech.</summary>
    Speech,

    /// <summary>0x02BC HearRangedSpeech.</summary>
    RangedSpeech,

    /// <summary>0x01E0 EmoteText.</summary>
    Emote,

    /// <summary>0x01E2 SoulEmote.</summary>
    SoulEmote,

    /// <summary>GameEvent 0x02BD Tell: a tell to the player.</summary>
    Tell,

    /// <summary>F7E0 ServerMessage: system text (including the confirmation of a tell the player sent).</summary>
    System,

    /// <summary>GameEvent 0x0004 PopupString.</summary>
    Popup,

    /// <summary>GameEvent 0x028A WeenieError or 0x028B WeenieErrorWithString (the text, where the server gave one).</summary>
    WeenieError,

    /// <summary>GameEvent 0x02EB CommunicationTransientString.</summary>
    Transient,

    /// <summary>The human line of a Lifestone tool reply.</summary>
    ToolReply,

    /// <summary>A line the client added itself ("Logging off...", "Lifestone: teleporting to Holtburg").</summary>
    Notice,
}

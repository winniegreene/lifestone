using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Diagnostics;

/// <summary>
/// Every chat line the server sends the player, logged once at Info in the category <c>chat</c> (M5 item 7 F17, the soak's full record): the combat text (each hit taken and dealt with its amount and
/// body part, evades, resists, kill notices), the magic lines, tells and system messages, as the session receives them. It listens to the session's <see cref="ChatReceived"/> event, the one the chat
/// panel reads, so it works with no UI. The fields are the line's <c>kind</c> (<see cref="ChatKind"/>), its <c>type</c> (the wire's chat type number), the <c>from</c> name and guid when there is a sender, and the <c>text</c>.
/// Nothing is filtered and nothing is special-cased: no password passes through chat, and a line that looks like a credential is logged like any other. A line is cut to
/// <see cref="MaxTextChars"/> characters and its line ends become spaces, so one chat line is always one log line.
/// </summary>
public sealed class ChatLogTap : IDisposable
{
    /// <summary>The longest text logged for one chat line.</summary>
    public const int MaxTextChars = 400;

    /// <summary>The log category.</summary>
    public const string LogCategory = "chat";

    private readonly ILogSink _log;
    private IDisposable? _subscription;

    /// <summary>Subscribes to <paramref name="events"/> and logs each chat line through <paramref name="log"/>.</summary>
    public ChatLogTap(IEventBus events, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(events);
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _subscription = events.Subscribe<ChatReceived>(OnChat);
    }

    /// <summary>How many lines were logged.</summary>
    public long Lines { get; private set; }

    /// <summary>Unsubscribes.</summary>
    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    /// <summary>The line as it is logged: line ends become spaces and the text is cut to <see cref="MaxTextChars"/>.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var cut = text.Length > MaxTextChars ? text[..MaxTextChars] : text;
        return cut.Contains('\n', StringComparison.Ordinal) || cut.Contains('\r', StringComparison.Ordinal) ? cut.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ') : cut;
    }

    private void OnChat(in ChatReceived e)
    {
        if (!_log.IsEnabled(LogSeverity.Info))
        {
            return;
        }

        var line = e.Line;
        Lines++;
        if (line.Sender is { Length: > 0 } sender)
        {
            _log.Info(
                LogCategory,
                "chat line",
                new LogField("kind", line.Kind),
                new LogField("type", line.ChatType),
                new LogField("from", sender),
                new LogField("text", Clean(line.Text)));
        }
        else if (line.Kind is ChatKind.WeenieError or ChatKind.ChannelNotice)
        {
            // M5 7 D124: a failure line carries only its code (and a string for 0x028B); the record writes the line the chat shows (WeenieErrorText), so no error line is blank: "code=0x051B text=You have entered the General channel."
            WeenieErrorText.TryFormat(line.ChatType, line.Text, out _, out var shown);
            _log.Info(
                LogCategory,
                "chat line",
                new LogField("kind", line.Kind),
                new LogField("type", line.ChatType),
                new LogField("code", "0x" + line.ChatType.ToString("X4", System.Globalization.CultureInfo.InvariantCulture)),
                new LogField("text", Clean(shown)),
                new LogField("on_screen", WeenieErrorText.RetailShows(line.ChatType) ? "yes" : "no")); // M5 7 D124 (S1b): a code the 11.6096 client prints no line for stays in the log only
        }
        else
        {
            _log.Info(LogCategory, "chat line", new LogField("kind", line.Kind), new LogField("type", line.ChatType), new LogField("text", Clean(line.Text)));
        }
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lifestone.ServerTools;

/// <summary>
/// The <c>@lifestone</c> wire contract between Lifestone's ACE server mod and the client (m2-plan §4.12, checkpoint
/// C-mod; DoD 10). This folder (server/mod/Shared) is compiled into the mod and, by a compile link, into
/// Lifestone.Client, so both sides share one copy. It uses the base class library only and does no I/O.
/// </summary>
/// <remarks>
/// <para><b>Request.</b> The client sends GameAction 0x0015 Talk with the text of <see cref="Request"/>, for example
/// <c>@lifestone place poi "Holtburg" #a1</c>. The verbs are in <see cref="Usage"/>; <see cref="ToolRequestParser"/> has
/// the grammar.</para>
/// <para><b>Reply.</b> Exactly one F7E0 ServerMessage (ACE's GameMessageSystemChat, ChatMessageType.Broadcast 0x00), to
/// the requesting session only, whose text is two lines: <c>Lifestone: &lt;sentence&gt;</c>, then <see cref="ReplyPrefix"/>
/// and one line of JSON (<see cref="ToolReply"/>). The whole text is printable ASCII plus the one newline, and at most
/// <see cref="MaxReplyLength"/> characters, since ACE writes it as a String16L in code page 1252.</para>
/// <para><b>Client rules.</b> Only an F7E0 whose second line starts with <see cref="ReplyPrefix"/> is a reply
/// (<see cref="ParseReply"/>); chat shows its first line only. Replies are matched to requests by id. An F7E0
/// "Unknown command: lifestone" (ChatMessageType.Help 0x0F, GameActionTalk.cs:86) means the mod is not loaded
/// (<see cref="IsModMissing"/>). ACE sends nothing at all for a command it refuses for access or world state
/// (GameActionTalk.cs:83-95), which is why the mod's command needs neither.</para>
/// </remarks>
public static class ToolsWire
{
    /// <summary>The command word, after the <c>@</c>.</summary>
    public const string Command = "lifestone";

    /// <summary>The wire version this code reads and writes.</summary>
    public const int Version = 1;

    /// <summary>The start of a reply's second line: the version tag and one space, then the JSON.</summary>
    public const string ReplyPrefix = "#lifestone/1 ";

    /// <summary>The start of every version's reply line (<c>#lifestone/&lt;version&gt; </c>).</summary>
    public const string ReplyTag = "#lifestone/";

    /// <summary>The start of a reply's first line, the one chat shows.</summary>
    public const string ChatPrefix = "Lifestone: ";

    /// <summary>The usage line: every verb and its arguments.</summary>
    public const string Usage = "status | where | net | places | xp <amount> | place poi \"<name>\" | place loc <cell> <x> <y> <z> [<qw> <qx> <qy> <qz>] | quest stamp <name> | quest erase <name> | quest has <name> | char purge \"<name>\"  [#id]";

    /// <summary>
    /// The longest reply text: 65,534 characters. ACE writes it as a String16L, whose length is a u16 (WriteString16L
    /// writes the raw count), but a reader that follows retail's PStringBase unpack takes a length word of 0xFFFF as an
    /// escape for a 32-bit length that follows, so a 65,535-character text would be misread there.
    /// </summary>
    public const int MaxReplyLength = 0xFFFE;

    /// <summary>What ACE answers when no mod has registered the command (GameActionTalk.cs:86).</summary>
    public const string UnknownCommandText = "Unknown command: " + Command;

    /// <summary>The F7E0 chat type of a reply: ACE's ChatMessageType.Broadcast.</summary>
    public const int ReplyChatType = 0x00;

    /// <summary>The F7E0 chat type of ACE's <see cref="UnknownCommandText"/> answer: ChatMessageType.Help.</summary>
    public const int UnknownCommandChatType = 0x0F;

    /// <summary>Every verb form, as <see cref="StatusData.Verbs"/> lists the ones a mod build carries out.</summary>
    public static IReadOnlyList<string> Verbs { get; } =
        [ToolVerbs.Status, ToolVerbs.Where, ToolVerbs.Net, ToolVerbs.Places, ToolVerbs.Xp, ToolVerbs.Place + " " + ToolVerbs.PlacePoi, ToolVerbs.Place + " " + ToolVerbs.PlaceLoc, ToolVerbs.Quest + " " + ToolVerbs.QuestStamp, ToolVerbs.Quest + " " + ToolVerbs.QuestErase, ToolVerbs.Quest + " " + ToolVerbs.QuestHas, ToolVerbs.Char + " " + ToolVerbs.CharPurge];

    /// <summary>
    /// The Talk text for <paramref name="request"/>: <c>@lifestone</c>, a space and <see cref="ToolRequestParser.Format"/>.
    /// Throws <see cref="ArgumentException"/> for a request the mod would refuse as <see cref="ToolErrors.BadArgs"/>.
    /// </summary>
    public static string Request(ToolRequest request) => "@" + Command + " " + ToolRequestParser.Format(request);

    /// <summary>
    /// Reads the text after <c>@lifestone</c> (the mod's raw parameter). On failure <paramref name="error"/> is
    /// <see cref="ToolErrors.BadArgs"/> or <see cref="ToolErrors.UnknownVerb"/>; <see cref="ToolRequestParser.Parse"/>
    /// also recovers the verb and id for the reply.
    /// </summary>
    public static bool TryParseRequest(string? rawTail, [NotNullWhen(true)] out ToolRequest? request, [NotNullWhen(false)] out ToolError? error)
    {
        var parse = ToolRequestParser.Parse(rawTail);
        request = parse.Request;
        error = parse.Error;
        return parse.Ok;
    }

    /// <summary>
    /// The F7E0 text of a reply: <see cref="ChatPrefix"/> and <paramref name="sentence"/> (any character outside
    /// printable ASCII becomes <c>?</c>), a newline, <see cref="ReplyPrefix"/> and the JSON. Throws
    /// <see cref="ArgumentException"/> for a reply of another version or without a verb, or a text longer than
    /// <see cref="MaxReplyLength"/>.
    /// </summary>
    public static string Reply(ToolReply reply, string sentence)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(sentence);
        if (reply.V != Version)
        {
            throw new ArgumentException("a reply must be wire version 1", nameof(reply));
        }

        if (reply.Verb is null)
        {
            throw new ArgumentException("a reply needs a verb", nameof(reply));
        }

        var envelope = new ReplyEnvelope
        {
            V = reply.V,
            Id = reply.Id,
            Verb = reply.Verb,
            Ok = reply.Ok,
            Error = reply.Error,
            Message = reply.Message,
            Data = reply.Data is { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } ? null : reply.Data,
        };
        var json = JsonSerializer.Serialize(envelope, ToolsWireJsonContext.Default.ReplyEnvelope);
        var text = new StringBuilder(ChatPrefix.Length + sentence.Length + ReplyPrefix.Length + json.Length + 1)
            .Append(ChatPrefix);
        foreach (var c in sentence)
        {
            text.Append(c is < ' ' or > '~' ? '?' : c);
        }

        text.Append('\n').Append(ReplyPrefix).Append(json);
        if (text.Length > MaxReplyLength)
        {
            throw new ArgumentException("a reply may be at most 65,534 characters (a String16L length word below its 0xFFFF escape)", nameof(reply));
        }

        return text.ToString();
    }

    /// <summary>
    /// True when <paramref name="serverMessageText"/> (an F7E0's text) is a version-1 reply. <paramref name="chatLine"/>
    /// is then its first line (<c>Lifestone: ...</c>), the only part chat shows. False for anything else, including a
    /// reply this version cannot read: use <see cref="ParseReply"/> to tell those apart from ordinary chat.
    /// </summary>
    public static bool TryParseReply(string? serverMessageText, [NotNullWhen(true)] out ToolReply? reply, [NotNullWhen(true)] out string? chatLine)
    {
        var parse = ParseReply(serverMessageText);
        reply = parse.Reply;
        chatLine = parse.Kind == ToolReplyKind.Reply ? parse.ChatLine : null;
        return parse.Kind == ToolReplyKind.Reply && reply is not null && chatLine is not null;
    }

    /// <summary>
    /// Classifies an F7E0's text: ordinary chat, a version-1 reply, a reply of another version, or a malformed reply
    /// (with the problem in one sentence). Unknown JSON fields are ignored. Never throws.
    /// </summary>
    public static ToolReplyParse ParseReply(string? serverMessageText)
    {
        if (string.IsNullOrEmpty(serverMessageText))
        {
            return NotAReply;
        }

        var newline = serverMessageText.IndexOf('\n', StringComparison.Ordinal);
        if (newline < 0 || !serverMessageText.AsSpan(newline + 1).StartsWith(ReplyTag, StringComparison.Ordinal))
        {
            return NotAReply;
        }

        var chatLine = serverMessageText[..newline];
        var line = serverMessageText[(newline + 1)..];
        if (!line.StartsWith(ReplyPrefix, StringComparison.Ordinal))
        {
            var tagEnd = line.IndexOf(' ', StringComparison.Ordinal);
            var tag = tagEnd < 0 ? line : line[..tagEnd];
            return new ToolReplyParse(ToolReplyKind.UnsupportedVersion, null, chatLine, "unsupported wire version: '" + Printable(tag, 24) + "' (this client reads " + ReplyPrefix.TrimEnd() + ")");
        }

        if (!chatLine.StartsWith(ChatPrefix, StringComparison.Ordinal))
        {
            return Malformed(chatLine, "the first line does not start with '" + ChatPrefix + "'");
        }

        if (line.Contains('\n', StringComparison.Ordinal))
        {
            return Malformed(chatLine, "the reply has more than two lines");
        }

        ReplyEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(line.AsSpan(ReplyPrefix.Length), ToolsWireJsonContext.Default.ReplyEnvelope);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            return Malformed(chatLine, "the JSON does not parse: " + Printable(e.Message, 200));
        }

        if (envelope is null)
        {
            return Malformed(chatLine, "the JSON is null");
        }

        if (envelope.V != Version)
        {
            return new ToolReplyParse(ToolReplyKind.UnsupportedVersion, null, chatLine, "unsupported wire version " + envelope.V.ToString(CultureInfo.InvariantCulture) + " in a " + ReplyPrefix.TrimEnd() + " line");
        }

        if (envelope.Verb is null)
        {
            return Malformed(chatLine, "the JSON has no verb");
        }

        if (!envelope.Ok && string.IsNullOrEmpty(envelope.Error))
        {
            return Malformed(chatLine, "a failed reply has no error code");
        }

        var data = envelope.Data is { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } ? null : envelope.Data;
        var reply = new ToolReply(envelope.V, envelope.Id, envelope.Verb, envelope.Ok, envelope.Error, envelope.Message, data);
        return new ToolReplyParse(ToolReplyKind.Reply, reply, chatLine, null);
    }

    /// <summary>True when <paramref name="serverMessageText"/> is ACE's answer to an unregistered <c>@lifestone</c>: the mod is not loaded.</summary>
    public static bool IsModMissing(string? serverMessageText) => string.Equals(serverMessageText, UnknownCommandText, StringComparison.Ordinal);

    /// <summary>A cell id as the wire writes it: <c>0x</c> and 8 upper-case hexadecimal digits.</summary>
    public static string FormatCell(uint cellId) => "0x" + cellId.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>Reads a cell id: exactly 8 hexadecimal digits, optionally after <c>0x</c> or <c>0X</c>.</summary>
    public static bool TryParseCell(string? text, out uint cellId)
    {
        cellId = 0;
        if (text is null)
        {
            return false;
        }

        var digits = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text.AsSpan(2) : text.AsSpan();
        if (digits.Length != 8)
        {
            return false;
        }

        foreach (var c in digits)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out cellId);
    }

    /// <summary>The first 8 characters of a commit id, as log lines and sentences show it.</summary>
    public static string ShortCommit(string? commit) => string.IsNullOrEmpty(commit) ? "unknown" : commit.Length <= 8 ? commit : commit[..8];

    private static readonly ToolReplyParse NotAReply = new(ToolReplyKind.NotAReply, null, null, null);

    private static ToolReplyParse Malformed(string chatLine, string problem) => new(ToolReplyKind.Malformed, null, chatLine, problem);

    /// <summary>At most <paramref name="max"/> characters of <paramref name="text"/>, with anything outside printable ASCII as <c>?</c>.</summary>
    private static string Printable(string text, int max)
    {
        var length = Math.Min(text.Length, max);
        var result = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            result.Append(text[i] is < ' ' or > '~' ? '?' : text[i]);
        }

        return result.ToString();
    }
}

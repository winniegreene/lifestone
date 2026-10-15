using System.Text;
using ACE.Server.Mods;
using ACE.Server.Network;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>
/// One <c>@lifestone</c> request and its one answer. A verb answers through it exactly once, either while ACE runs the
/// command (status, net, where, place) or later: <c>xp</c> answers from the player's action queue after ACE has added
/// the experience, and <c>places</c> from a background task. Answering logs the request's <see cref="ModLog.Tool"/>
/// line and sends the reply (<see cref="Reply"/>); a second answer is dropped and logged as an error
/// (<see cref="OneAnswer"/>), so the client never sees two replies to one request.
/// </summary>
/// <remarks>
/// A verb that answers later must not let an exception escape its callback unanswered: it catches and calls
/// <see cref="Crashed"/>. One case stays unanswered by design: when the player logs off before ACE runs a queued
/// callback, ACE stops running that queue, and the client's own timeout (m2-plan ST12) reports it.
/// </remarks>
internal sealed class ToolCall
{
    private const int MaxLoggedArgs = 200;

    private readonly OneAnswer gate = new();

    /// <summary>A request from <paramref name="session"/> (null at ACE's console).</summary>
    public ToolCall(Session? session, ToolRequestParse parse, string raw)
    {
        Session = session;
        Parse = parse;
        Raw = raw;
    }

    /// <summary>The requesting session, or null at ACE's console.</summary>
    public Session? Session { get; }

    /// <summary>What the parser made of the request (its verb and id even when it failed).</summary>
    public ToolRequestParse Parse { get; }

    /// <summary>The raw text after the command word, as ACE passed it (logged sanitised, never parsed again).</summary>
    public string Raw { get; }

    /// <summary>True once the request has been answered.</summary>
    public bool Answered => gate.IsTaken;

    /// <summary>Answers with <paramref name="data"/> and its sentence.</summary>
    public void Succeed<T>(ToolRequest request, T data, string sentence, string? message = null)
        where T : class => Answer(ToolReply.Success(request, data, message), sentence);

    /// <summary>Answers with an error (<see cref="ToolErrors"/>); the message is also the chat sentence.</summary>
    public void Fail(string code, string message) => Fail(new ToolError(code, message));

    /// <summary>Answers with <paramref name="error"/>.</summary>
    public void Fail(ToolError error) => Answer(ToolReply.Failure(Parse.Id, Parse.Verb, error), error.Message);

    /// <summary>Logs an exception a verb did not handle and answers <see cref="ToolErrors.Failed"/> unless it already answered.</summary>
    public void Crashed(Exception exception)
    {
        ModManager.Log("Lifestone tool failed: verb=" + VerbForLog + ": " + exception, ModManager.LogLevel.Error);
        if (!Answered)
        {
            Fail(ToolErrors.Failed, "the server mod failed (" + exception.GetType().Name + "); see ACE_Log.txt");
        }
    }

    /// <summary>Logs the request's one <see cref="ModLog.Tool"/> line and sends <paramref name="reply"/>; only the first answer counts.</summary>
    public void Answer(ToolReply reply, string sentence)
    {
        if (!gate.TryTake())
        {
            ModManager.Log("Lifestone tool: a second answer to one request was dropped: verb=" + VerbForLog + " id=" + (Parse.Id ?? "(none)"), ModManager.LogLevel.Error);
            return;
        }

        ModManager.Log(ModLog.Tool + "account=" + Clean(Session is null ? "(console)" : Session.Account, 64)
            + " character=" + Clean(Session?.Player?.Name ?? "(none)", 64)
            + " verb=" + VerbForLog
            + " id=" + (Parse.Id ?? "(none)")
            + " args=" + Clean(Raw, MaxLoggedArgs)
            + " -> " + (reply.Ok ? "ok" : reply.Error));
        Reply.Send(Session, reply, sentence);
    }

    private string VerbForLog => Parse.Verb.Length == 0 ? "(none)" : Parse.Verb;

    /// <summary>At most <paramref name="max"/> characters of <paramref name="text"/> for one log line, anything outside printable ASCII as <c>?</c>.</summary>
    private static string Clean(string? text, int max)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "(empty)";
        }

        var clean = new StringBuilder(Math.Min(text.Length, max) + 3);
        foreach (var c in text.Length > max ? text[..max] : text)
        {
            clean.Append(c is < ' ' or > '~' ? '?' : c);
        }

        return text.Length > max ? clean.Append("...").ToString() : clean.ToString();
    }
}

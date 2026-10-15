using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Client.Session.Tools;
using Lifestone.Net.Session;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// One player-tool request (step ST12): the Talk line <c>@lifestone &lt;verb&gt; ... #&lt;id&gt;</c> written by the mod's
/// ToolsWire, and the reply with the same id (m2-plan §4.12). An <c>ok</c> reply confirms the request with its typed
/// result; an error reply is a <see cref="ActionOutcomeKind.ServerError"/> whose detail is the mod's code and sentence
/// (<c>not-found: ...</c>); <c>Unknown command: lifestone</c> refuses it (the mod is not loaded). The timeout is 5 s.
/// </summary>
/// <remarks>
/// Before sending, a request adds its own chat line (<see cref="SessionNotices.ClientChatLine"/>; DoD 10: every use is
/// written to the chat), or, when silent, marks its id so the reducer keeps the reply's line out of the chat
/// (<see cref="SessionNotices.ToolRequestSilent"/>). Game thread only.
/// </remarks>
/// <typeparam name="T">The result type.</typeparam>
internal sealed class ToolTracker<T> : ActionTracker
    where T : class
{
    private readonly ToolRequest _request;
    private readonly string _text;
    private readonly string? _chatLine;
    private readonly bool _silent;
    private readonly Func<ToolReply, T> _read;
    private readonly Action<T>? _observe;
    private T? _result;

    /// <summary>A request for <paramref name="handle"/>.</summary>
    /// <param name="handle">The typed handle.</param>
    /// <param name="request">The request, with the handle's id as its correlation id.</param>
    /// <param name="text">The Talk text (<see cref="ToolsWire.Request"/>).</param>
    /// <param name="chatLine">The line added to the chat when it is sent, or null.</param>
    /// <param name="silent">True to keep the reply's line out of the chat.</param>
    /// <param name="read">Reads the result from an ok reply (throws <see cref="ToolDataException"/> for bad data).</param>
    /// <param name="observe">Called with the result when an ok reply arrives (the status probe's checks), or null.</param>
    internal ToolTracker(ToolHandle<T> handle, ToolRequest request, string text, string? chatLine, bool silent, Func<ToolReply, T> read, Action<T>? observe = null)
        : base(handle, ActionLane.Tools, ActionPolicy.ToolSeconds)
    {
        _request = request;
        _text = text;
        _chatLine = chatLine;
        _silent = silent;
        _read = read;
        _observe = observe;
    }

    /// <summary>The request's correlation id.</summary>
    internal string? RequestId => _request.Id;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0015 Talk '@lifestone " + _request.Verb + "' (a server-mod request)";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) =>
        api.Tools.Availability switch
        {
            ToolAvailability.Unknown or ToolAvailability.Ready => null,
            var other => PlayerTools.Refusal(other),
        };

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        if (_silent)
        {
            api.ApplyLocal(SessionNotices.ToolRequestSilent, 0, _request.Id);
        }
        else if (_chatLine is not null)
        {
            api.ApplyLocal(SessionNotices.ClientChatLine, 0, _chatLine);
        }

        session!.SendTalk(_text);
        return null;
    }

    /// <inheritdoc/>
    internal override bool OnToolReply(ToolReplyLine reply, ActionApi api)
    {
        var r = reply.Reply;
        if (r.Id is null || !string.Equals(r.Id, _request.Id, StringComparison.Ordinal))
        {
            return false;
        }

        if (!r.Ok)
        {
            var error = r.AsError()!;
            Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, 0, error.Code + ": " + error.Message));
            return true;
        }

        try
        {
            var result = _read(r);
            _result = result;
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, reply.ChatLine));
            _observe?.Invoke(result);
        }
        catch (ToolDataException e)
        {
            api.Log.Warn(SessionLog.Category, "a Lifestone tool reply was not understood: " + e.Message, new LogField("verb", r.Verb));
            Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, 0, "failed: " + e.Message));
        }

        return true;
    }

    /// <inheritdoc/>
    internal override void OnToolsUnavailable(in ToolsUnavailable e) =>
        Resolve(new ActionOutcome(ActionOutcomeKind.Refused, 0, PlayerTools.Refusal(e.Availability)));

    /// <inheritdoc/>
    internal override void OnTravel(in SelfTravelChanged e)
    {
        if (e.Travel == SelfTravel.NotInWorld)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.Refused, 0, PlayerTools.Refusal(ToolAvailability.NotInWorld)));
        }
    }

    /// <inheritdoc/>
    internal override bool CompleteHandle(ActionOutcome outcome, double time, Action<ActionHandle, Exception> onHandlerError) =>
        ((ToolHandle<T>)Handle).TryComplete(outcome.Kind == ActionOutcomeKind.Confirmed ? _result : null, outcome, time, onHandlerError);
}

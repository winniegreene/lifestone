using System.Globalization;
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
/// <remarks>
/// <para><b>The world, before and after (M5 8, <c>m5-8-place-not-in-world</c>, D141, D152; the one seam every player-scope verb shares).</b> BEFORE: a request that acts on the player (every verb but <c>status</c>, <c>net</c> and <c>places</c>)
/// is not sent while the client's own state says the player is not in the world (the availability, or <see cref="SelfTravel.NotInWorld"/>): it waits in its lane, at most <see cref="ActionPolicy.ToolWorldWaitSeconds"/>, then is refused
/// with the named reason as before. ACT: it is sent. AFTER: the reply is the proof as always, except one case: the mod answers <c>not-in-world</c> when ACE's <c>session.Player.CurrentLandblock</c> is null, and the client cannot see that.
/// The tracker then waits (the mod's <c>where</c> every <see cref="ActionPolicy.ToolWorldProbeSeconds"/>, silent, until it answers ok, at most <see cref="ActionPolicy.ToolWorldWaitSeconds"/>) and sends the same request once more.
/// A second <c>not-in-world</c>, or the wait running out, fails with the named reason as before. The retry is the same request, the same handle and the same id: <see cref="PlayerTools.Verbs"/> counted it once when it was
/// accepted and counts it no more, so a journey's <c>granted=0 places=0</c> keeps its meaning (the mod carried out one place, and the counter says one).</para>
/// <para><b>What ACE does (the cause, so the wait is honest about what it can fix).</b> ACE sets the player's <c>CurrentLandblock</c> inside <c>PlayerEnterWorld</c>, synchronously, before the client can send LoginComplete
/// (WorldManager.cs:222, Landblock.cs:862 in <c>AddWorldObjectInternal</c>), so it is not "still loading": <c>GetLandblock</c> loads and <c>Init</c>s a block on the same thread before it returns (LandblockManager.cs:477-514). It becomes null again
/// only when the object is removed from the block (Landblock.cs:969 in <c>RemoveWorldObjectInternal</c>, which <c>Landblock.Unload</c> runs for every object, Landblock.cs:1130-1146). An empty block unloads <c>UnloadInterval</c> (five minutes,
/// Landblock.cs:118) after its last <c>SetActive</c> (Landblock.cs:532-549), and a player keeps a block alive only through its own heartbeat (Player_Tick.cs:111-114, Player_Location.cs:782-793), which runs after the block's unload check
/// (Landblock.cs:516-550 before Landblock.cs:597-604) and not before the player's first heartbeat. A login that lands in the last tick before the five minutes are up (the failed run came about five minutes after the previous logout) is
/// therefore added to a block that unloads in the same tick, and the player is left in no landblock: the unload logs nothing, which fits ACE's quiet log. If that is the cause, nothing the client sends puts the player back (the mod's
/// check, ValidateMovement and the heartbeats all need the block), so the bounded wait ends in the named failure within <see cref="ActionPolicy.ToolWorldWaitSeconds"/> instead of a hang, and only a log off and a new login recovers (see the doc note).</para>
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
    private readonly Func<T, string?>? _verdict;
    private T? _result;
    private IAcSession? _session;
    private double _acceptedAt = double.NaN;
    private bool _worldWaitLogged;
    private double _sentAt = double.NaN;
    private bool _retried;
    private bool _waiting;
    private bool _probeOut;
    private double _waitStart;
    private double _nextProbe;
    private string? _probeId;
    private string _refusal = string.Empty;

    /// <summary>A request for <paramref name="handle"/>.</summary>
    /// <param name="handle">The typed handle.</param>
    /// <param name="request">The request, with the handle's id as its correlation id.</param>
    /// <param name="text">The Talk text (<see cref="ToolsWire.Request"/>).</param>
    /// <param name="chatLine">The line added to the chat when it is sent, or null.</param>
    /// <param name="silent">True to keep the reply's line out of the chat.</param>
    /// <param name="read">Reads the result from an ok reply (throws <see cref="ToolDataException"/> for bad data).</param>
    /// <param name="observe">Called with the result when an ok reply arrives (the status probe's checks), or null.</param>
    /// <param name="verdict">Judges an ok reply's result: null to confirm, or the failure sentence (<c>code: sentence</c>) that ends the request as a server error with the result still set (spell learn with an unknown id), or null for none.</param>
    internal ToolTracker(ToolHandle<T> handle, ToolRequest request, string text, string? chatLine, bool silent, Func<ToolReply, T> read, Action<T>? observe = null, Func<T, string?>? verdict = null)
        : base(handle, ActionLane.Tools, ActionPolicy.ToolSeconds)
    {
        _request = request;
        _text = text;
        _chatLine = chatLine;
        _silent = silent;
        _read = read;
        _observe = observe;
        _verdict = verdict;
    }

    /// <summary>The request's correlation id.</summary>
    internal string? RequestId => _request.Id;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0015 Talk '@lifestone " + _request.Verb + "' (a server-mod request)";

    /// <summary>True for a request that acts on the player (the mod answers it <c>not-in-world</c> outside a landblock): every verb but <c>status</c>, <c>net</c> and <c>places</c>.</summary>
    private bool PlayerScope => _request is not (StatusRequest or NetRequest or PlacesRequest);

    /// <summary>M5 8, the before-check: whether the client's own state says the player is not in the world (the availability, or the self-travel state).</summary>
    private static bool ClientSaysNotInWorld(ActionApi api) =>
        api.Tools.Availability == ToolAvailability.NotInWorld || api.State.Self.Travel == SelfTravel.NotInWorld;

    private static string Secs(double seconds) => seconds.ToString("0.0", CultureInfo.InvariantCulture);

    private bool Holding => PlayerScope && !_retried && !_waiting;

    /// <inheritdoc/>
    internal override void Accepted(ActionApi api) => _acceptedAt = api.Clock.Now;

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api)
    {
        if (Holding && ClientSaysNotInWorld(api) && api.Tools.Availability is ToolAvailability.NotInWorld or ToolAvailability.Unknown or ToolAvailability.Ready)
        {
            // M5 8 BEFORE: a player-scope request waits for the world (bounded) instead of being refused at once; WaitReason holds it.
            var waited = double.IsNaN(_acceptedAt) ? 0 : api.Clock.Now - _acceptedAt;
            return waited < ActionPolicy.ToolWorldWaitSeconds
                ? null
                : Refuse(ActionReason.NotInWorld, PlayerTools.Refusal(ToolAvailability.NotInWorld) + " (waited " + ActionText.Count((long)ActionPolicy.ToolWorldWaitSeconds) + " s for it before sending)");
        }

        return api.Tools.Availability switch
        {
            ToolAvailability.Unknown or ToolAvailability.Ready => null,
            var other => PlayerTools.Refusal(other),
        };
    }

    /// <inheritdoc/>
    internal override string? WaitReason(ActionApi api)
    {
        if (!Holding || !ClientSaysNotInWorld(api))
        {
            return null;
        }

        if (!_worldWaitLogged)
        {
            _worldWaitLogged = true;
            api.Log.Info(
                SessionLog.Category,
                "a player-tool request waits for the world: the client's own state says the player is not in it (nothing is sent until it is, at most " + ActionText.Count((long)ActionPolicy.ToolWorldWaitSeconds) + " s)",
                new LogField("request", _request.Verb + " #" + _request.Id));
        }

        return "the client's own state says the player is not in the world";
    }

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

        _session = session;
        _sentAt = api.Clock.Now;
        if (_worldWaitLogged)
        {
            api.Log.Info(
                SessionLog.Category,
                "the player is in the world: sending the request that waited for it",
                new LogField("request", _request.Verb + " #" + _request.Id),
                new LogField("waited_s", Secs(_sentAt - _acceptedAt)));
        }

        session!.SendTalk(_text);
        return null;
    }

    /// <inheritdoc/>
    internal override bool OnToolReply(ToolReplyLine reply, ActionApi api)
    {
        var r = reply.Reply;
        if (_waiting && _probeId is not null && string.Equals(r.Id, _probeId, StringComparison.Ordinal))
        {
            OnProbeReply(r, api);
            return true;
        }

        if (r.Id is null || !string.Equals(r.Id, _request.Id, StringComparison.Ordinal))
        {
            return false;
        }

        if (!r.Ok)
        {
            var error = r.AsError()!;
            if (Holding && string.Equals(error.Code, ToolErrors.NotInWorld, StringComparison.Ordinal))
            {
                BeginWait(error, api);
                return true;
            }

            if (_retried)
            {
                api.Log.Warn(
                    SessionLog.Category,
                    "the retried player-tool request was refused again: it fails with the named reason (no third send)",
                    new LogField("request", _request.Verb + " #" + _request.Id),
                    new LogField("reason", error.Code));
            }

            Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, 0, error.Code + ": " + error.Message));
            return true;
        }

        if (_retried)
        {
            api.Log.Info(
                SessionLog.Category,
                "the retried player-tool request was carried out by the mod",
                new LogField("request", _request.Verb + " #" + _request.Id));
        }

        try
        {
            var result = _read(r);
            _result = result;
            var failure = _verdict?.Invoke(result);
            Resolve(failure is null ? new ActionOutcome(ActionOutcomeKind.Confirmed, 0, reply.ChatLine) : new ActionOutcome(ActionOutcomeKind.ServerError, 0, failure));
            _observe?.Invoke(result);
        }
        catch (ToolDataException e)
        {
            api.Log.Warn(SessionLog.Category, "a Lifestone tool reply was not understood: " + e.Message, new LogField("verb", r.Verb));
            Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, 0, "failed: " + e.Message));
        }

        return true;
    }

    /// <summary>M5 8 AFTER: the mod said <c>not-in-world</c>: wait for its landblock check to pass (a silent <c>where</c> probe every second, bounded), then send the same request once more.</summary>
    private void BeginWait(ToolError error, ActionApi api)
    {
        var now = api.Clock.Now;
        _waiting = true;
        _waitStart = now;
        _nextProbe = now + ActionPolicy.ToolWorldProbeSeconds;
        _refusal = error.Code + ": " + error.Message;
        _probeId = _request.Id + "-w";

        // The wait is inside this request's timeout: the time already spent, the wait, then the retry's answer.
        TimeoutSeconds = (double.IsNaN(_sentAt) ? 0 : now - _sentAt) + ActionPolicy.ToolWorldWaitSeconds + ActionPolicy.ToolSeconds + ActionPolicy.ToolWorldProbeSeconds;
        api.Log.Warn(
            SessionLog.Category,
            "the mod refused a player-tool request: " + error.Code + " (ACE has no landblock for the player). Waiting for it (the mod's where, every " + ActionText.Count((long)ActionPolicy.ToolWorldProbeSeconds) + " s, at most "
            + ActionText.Count((long)ActionPolicy.ToolWorldWaitSeconds) + " s), then the request is sent once more",
            new LogField("request", _request.Verb + " #" + _request.Id));
    }

    private void OnProbeReply(ToolReply r, ActionApi api)
    {
        _probeOut = false;
        if (!r.Ok && string.Equals(r.AsError()?.Code, ToolErrors.NotInWorld, StringComparison.Ordinal))
        {
            _nextProbe = api.Clock.Now + ActionPolicy.ToolWorldProbeSeconds;
            return; // still no landblock: the next probe, or the bound
        }

        Retry(api, r.Ok ? "the mod's where answered" : "the mod's where answered with " + r.AsError()?.Code);
    }

    private void Retry(ActionApi api, string why)
    {
        var now = api.Clock.Now;
        _waiting = false;
        _retried = true;
        _probeId = null;
        TimeoutSeconds = (double.IsNaN(_sentAt) ? 0 : now - _sentAt) + ActionPolicy.ToolSeconds;
        api.Log.Info(
            SessionLog.Category,
            "sending the player-tool request once more: " + why,
            new LogField("request", _request.Verb + " #" + _request.Id),
            new LogField("waited_s", Secs(now - _waitStart)));
        try
        {
            _session!.SendTalk(_text);
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.Cancelled, 0, "the retry could not be sent: " + e.Message));
        }
    }

    /// <inheritdoc/>
    internal override void Poll(ActionApi api)
    {
        if (!_waiting)
        {
            return;
        }

        var now = api.Clock.Now;
        if (now - _waitStart >= ActionPolicy.ToolWorldWaitSeconds)
        {
            api.Log.Warn(
                SessionLog.Category,
                "the player never got a landblock within the wait: the request fails with the named reason (no retry sent)",
                new LogField("request", _request.Verb + " #" + _request.Id),
                new LogField("waited_s", Secs(now - _waitStart)));
            _waiting = false;
            Resolve(new ActionOutcome(ActionOutcomeKind.ServerError, 0, _refusal));
            return;
        }

        if (!_probeOut && now >= _nextProbe)
        {
            _probeOut = true;
            _nextProbe = now + ActionPolicy.ToolWorldProbeSeconds;
            try
            {
                api.ApplyLocal(SessionNotices.ToolRequestSilent, 0, _probeId!);
                _session!.SendTalk(ToolsWire.Request(new WhereRequest(_probeId!)));
            }
            catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or NotSupportedException or ArgumentException)
            {
                Resolve(new ActionOutcome(ActionOutcomeKind.Cancelled, 0, "the probe could not be sent: " + e.Message));
            }
        }
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
        ((ToolHandle<T>)Handle).TryComplete(_result, outcome, time, onHandlerError);
}

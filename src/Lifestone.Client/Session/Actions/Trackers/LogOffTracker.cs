using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// <see cref="IActionApi.LogOff"/>, the only sender of F653 CharacterLogOff. Re-expresses the start of retail's
/// <c>CPlayerSystem::RequestLogOff</c> (11.4186 0x00562DD0, 11.6096 0x00563B70): "Logging off..." goes to the chat
/// (<c>ClientSystem::AddTextToScroll</c>; here notice <see cref="SessionNotices.ClientChatLine"/>), then the logoff is
/// sent (<c>Proto_UI::LogOffCharacter</c>, 0x00546A20 / 0x005475E0), then <see cref="LogOffRequested"/> is published with
/// the frame time, and portal space plays retail's logoff transition and its 3 s wait from there. The player-killer delay
/// (retail's extra 20 s) is off in M2 (D33). Completes when the server's F653 and F658 have brought the session back to
/// character select.
/// </summary>
internal sealed class LogOffTracker(ActionHandle handle) : ActionTracker(handle, ActionLane.Session, ActionPolicy.LogOffSeconds)
{
    /// <summary>The line retail adds to the chat (without its trailing newline: a chat line is one line).</summary>
    internal const string ChatLine = "Logging off...";

    private bool _listReceived;

    /// <inheritdoc/>
    internal override string WouldSend => "F653 CharacterLogOff";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) =>
        api.State.Phase == SessionPhase.InWorld ? null : "not in the world (phase " + api.State.Phase + ")";

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        api.ApplyLocal(SessionNotices.ClientChatLine, 0, ChatLine);
        session!.LogOff();
        api.PublishLogOffRequested(playerKillerDelay: false);
        return null;
    }

    /// <inheritdoc/>
    internal override void OnCharacterList(in CharacterListChanged e)
    {
        if (e.Reason == CharacterListReason.Received)
        {
            _listReceived = true;
        }
    }

    /// <inheritdoc/>
    internal override void Poll(ActionApi api)
    {
        var state = api.State;
        if (_listReceived && state.Phase == SessionPhase.CharacterSelect && state.Self.Guid == 0)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed));
        }
    }

    /// <inheritdoc/>
    internal override ActionOutcome SessionFailed(SessionFailure? failure) => FailureOutcome(failure);
}

/// <summary>
/// <see cref="IActionApi.Quit"/>: ends the session at once (DoD 11). In the world the network session sends F653 and then
/// a cleartext Disconnect, so ACE drops the session immediately; elsewhere only the Disconnect. ACE never answers a
/// Disconnect, so the outcome is <see cref="ActionOutcomeKind.Sent"/>. A quit goes before everything queued, and every
/// other action still waiting or in flight is cancelled.
/// </summary>
internal sealed class QuitTracker(ActionHandle handle) : ActionTracker(handle, ActionLane.Immediate, double.PositiveInfinity)
{
    /// <inheritdoc/>
    internal override string WouldSend => "F653 CharacterLogOff in the world, then a Disconnect";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => api.State.Phase switch
    {
        SessionPhase.Idle => "there is no connection to end (phase Idle)",
        SessionPhase.Closing or SessionPhase.Closed => "the session is already closing",
        SessionPhase.Failed => "the session already failed",
        _ => null,
    };

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        var inWorld = api.State.Phase == SessionPhase.InWorld;
        session!.Quit();
        return new ActionOutcome(ActionOutcomeKind.Sent, 0, inWorld ? "F653 then Disconnect" : "Disconnect");
    }
}

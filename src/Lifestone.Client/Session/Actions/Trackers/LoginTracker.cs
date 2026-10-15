using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// <see cref="IActionApi.Login"/> (step ST10): starts the network session's login with the credentials the session
/// holds, and completes when the session reached character select with the character list in. The network session
/// retries a lost connect or a transient character error itself (D18); its retries show as
/// <see cref="ConnectionProgress"/>, not as an outcome. A boot, a ban, a data mismatch or a character error after the
/// retries ends the login as a <see cref="ActionOutcomeKind.ServerError"/>; a connect, handshake or data-check timeout as
/// <see cref="ActionOutcomeKind.TimedOut"/> (<see cref="ActionTracker.FailureOutcome"/>).
/// </summary>
internal sealed class LoginTracker(ActionHandle handle) : ActionTracker(handle, ActionLane.Session, ActionPolicy.LoginSeconds)
{
    private bool _listReceived;

    /// <inheritdoc/>
    internal override string WouldSend => "LoginRequest (the network session's login)";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) =>
        api.State.Phase == SessionPhase.Idle
            ? null
            : "the session is not idle (phase " + api.State.Phase + "); after a failure, reconnect first";

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        api.Link!.Login(session!);
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
        if (_listReceived && state.Phase == SessionPhase.CharacterSelect && state.DataCheck == DataCheckState.Complete)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed));
        }
    }

    /// <inheritdoc/>
    internal override ActionOutcome SessionFailed(SessionFailure? failure) => FailureOutcome(failure);
}

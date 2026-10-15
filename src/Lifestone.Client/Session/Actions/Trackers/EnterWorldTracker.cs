using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// <see cref="IActionApi.EnterWorld"/>: F7C8, then F657 when F7DF arrives (the network session does both). Refused for a
/// character pending deletion, as retail ghosts its Enter button. Completes when F746 PlayerCreate has named the
/// character and its own CreateObject has arrived (<see cref="SelfState.Guid"/> and the object table); a character error
/// is a <see cref="ActionOutcomeKind.ServerError"/>.
/// </summary>
internal sealed class EnterWorldTracker(ActionHandle handle, uint guid) : ActionTracker(handle, ActionLane.Session, ActionPolicy.EnterWorldSeconds)
{
    /// <inheritdoc/>
    internal override string WouldSend => "F7C8 CharacterEnterWorldRequest, then F657 CharacterEnterWorld";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api)
    {
        var state = api.State;
        if (state.Phase != SessionPhase.CharacterSelect)
        {
            return "not at the character list (phase " + state.Phase + ")";
        }

        var index = state.Characters.IndexOf(guid);
        if (index < 0)
        {
            return "the character is not in the list";
        }

        return state.Characters.Entries[index].PendingDeletion
            ? "the character is pending deletion (restore it first; retail's Enter is disabled for it)"
            : null;
    }

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        session!.EnterWorld(guid);
        return null;
    }

    /// <inheritdoc/>
    internal override void Poll(ActionApi api)
    {
        var state = api.State;
        if (state.Self.Guid == guid && state.Objects.Contains(guid))
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed));
        }
    }

    /// <inheritdoc/>
    internal override void OnCharacterError(in CharacterErrorReceived e) => Resolve(ActionText.CharacterError(e));

    /// <inheritdoc/>
    internal override ActionOutcome SessionFailed(SessionFailure? failure) => FailureOutcome(failure);
}

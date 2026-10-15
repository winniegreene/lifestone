using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// <see cref="IActionApi.SelectCharacter"/>: local, no message (retail's character list selection is the client's own).
/// It goes through the reducer as notice <see cref="SessionNotices.CharacterSelected"/>, so a replay repeats it.
/// </summary>
internal sealed class SelectCharacterTracker(ActionHandle handle, uint guid) : ActionTracker(handle, ActionLane.Immediate, double.PositiveInfinity)
{
    /// <inheritdoc/>
    internal override bool NeedsSession => false;

    /// <inheritdoc/>
    internal override string WouldSend => "nothing (a local selection)";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) =>
        guid != 0 && api.State.Characters.IndexOf(guid) < 0 ? "the character is not in the list" : null;

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        api.ApplyLocal(SessionNotices.CharacterSelected, guid);
        return new ActionOutcome(ActionOutcomeKind.Local);
    }
}

/// <summary>
/// <see cref="IActionApi.CreateCharacter"/>: F656 with the intent mapped field for field onto net's request
/// (<see cref="CharacterCreateIntent.ToRequest"/>: exactly 55 skills and a printable-ASCII name, checked before
/// sending, since ACE ends the session for a wrong skill count). Completes on F643: <c>Ok</c> confirms, any other result
/// is a <see cref="ActionOutcomeKind.ServerError"/> with the CharGenResult as its code.
/// </summary>
internal sealed class CreateCharacterTracker(ActionHandle handle, CharacterCreateIntent? intent)
    : ActionTracker(handle, ActionLane.Session, ActionPolicy.CreateSeconds)
{
    /// <inheritdoc/>
    internal override string WouldSend => "F656 CharacterCreate";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api)
    {
        var state = api.State;
        if (state.Phase != SessionPhase.CharacterSelect)
        {
            return "not at the character list (phase " + state.Phase + ")";
        }

        var list = state.Characters;
        return list.SlotCount != 0 && list.Entries.Count >= list.SlotCount
            ? string.Create(CultureInfo.InvariantCulture, $"every character slot is taken ({list.Entries.Count} of {list.SlotCount})")
            : null;
    }

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        // The next free slot: ACE appends the character to the account's list.
        var request = intent!.ToRequest((uint)api.State.Characters.Entries.Count);
        session!.CreateCharacter(in request);
        api.ApplyLocal(SessionNotices.CharacterCreateSent);
        return null;
    }

    /// <inheritdoc/>
    internal override void OnCreateResult(in CharacterCreateResult e)
    {
        if (e.Restore)
        {
            return;
        }

        Resolve(e.Result == CharGenResult.Ok
            ? new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "created " + ActionText.Hex(e.Guid))
            : new ActionOutcome(ActionOutcomeKind.ServerError, (uint)e.Result, "the server refused the character: " + e.Result));
    }

    /// <inheritdoc/>
    internal override void OnCharacterError(in CharacterErrorReceived e) => Resolve(ActionText.CharacterError(e));

    /// <inheritdoc/>
    internal override ActionOutcome SessionFailed(SessionFailure? failure) => FailureOutcome(failure);
}

/// <summary>
/// <see cref="IActionApi.DeleteCharacter"/>: F655 names the character by its index in the server's list. ACE does not
/// check the index (an out-of-range one throws on the server), so this finds it in the list, which the reducer keeps in
/// server order, and refuses an unknown character or one already pending deletion. Completes when the F655
/// acknowledgement and the F658 after it have arrived.
/// </summary>
internal sealed class DeleteCharacterTracker(ActionHandle handle, uint guid) : ActionTracker(handle, ActionLane.Session, ActionPolicy.DeleteSeconds)
{
    private bool _acknowledged;

    /// <inheritdoc/>
    internal override string WouldSend => "F655 CharacterDelete";

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

        return state.Characters.Entries[index].PendingDeletion ? "the character is already pending deletion" : null;
    }

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        session!.DeleteCharacter(api.State.Characters.IndexOf(guid));
        return null;
    }

    /// <inheritdoc/>
    internal override void OnCharacterList(in CharacterListChanged e)
    {
        if (e.Reason == CharacterListReason.DeleteAcknowledged)
        {
            _acknowledged = true;
        }
        else if (e.Reason == CharacterListReason.Received && _acknowledged)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed));
        }
    }

    /// <inheritdoc/>
    internal override void OnCharacterError(in CharacterErrorReceived e) => Resolve(ActionText.CharacterError(e));

    /// <inheritdoc/>
    internal override ActionOutcome SessionFailed(SessionFailure? failure) => FailureOutcome(failure);
}

/// <summary>
/// <see cref="IActionApi.RestoreCharacter"/>: F7D9 for a character pending deletion (notice
/// <see cref="SessionNotices.CharacterRestoreSent"/> tells the reducer the next F643 answers it). Completes on that F643:
/// <c>Ok</c> confirms, any other result is a <see cref="ActionOutcomeKind.ServerError"/>.
/// </summary>
internal sealed class RestoreCharacterTracker(ActionHandle handle, uint guid) : ActionTracker(handle, ActionLane.Session, ActionPolicy.RestoreSeconds)
{
    /// <inheritdoc/>
    internal override string WouldSend => "F7D9 CharacterRestore";

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

        return state.Characters.Entries[index].PendingDeletion ? null : "the character is not pending deletion";
    }

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        session!.RestoreCharacter(guid);
        api.ApplyLocal(SessionNotices.CharacterRestoreSent, guid);
        return null;
    }

    /// <inheritdoc/>
    internal override void OnCreateResult(in CharacterCreateResult e)
    {
        if (!e.Restore)
        {
            return;
        }

        Resolve(e.Result == CharGenResult.Ok
            ? new ActionOutcome(ActionOutcomeKind.Confirmed)
            : new ActionOutcome(ActionOutcomeKind.ServerError, (uint)e.Result, "the server refused the restore: " + e.Result));
    }

    /// <inheritdoc/>
    internal override void OnCharacterError(in CharacterErrorReceived e) => Resolve(ActionText.CharacterError(e));

    /// <inheritdoc/>
    internal override ActionOutcome SessionFailed(SessionFailure? failure) => FailureOutcome(failure);
}

using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// <see cref="IActionApi.LoginComplete"/>: GameAction 0x00A1 when portal space ends, as retail's
/// <c>CPlayerSystem::SendLoginCompleteNotification</c> sends it through <c>CM_Character::Event_LoginCompleteNotification</c>
/// (11.4186 0x00562E90 and 0x006A1480; 11.6096 0x00563C30 and 0x006A22A0). Until it arrives ACE ignores every position
/// the client sends; ACE then clears Hidden (<c>Player_Location.OnTeleportComplete</c>), and the tracker completes when
/// the player is <see cref="SelfTravel.Present"/>.
/// </summary>
/// <remarks>
/// <para>Allowed only while the player is <see cref="SelfTravel.Arriving"/> or <see cref="SelfTravel.TeleportArrived"/>,
/// and once per arrival (m2-log-in.md §4.5: exactly once): a second call while the first one's arrival is still
/// materialising is refused and logged as an ERROR, since it means two callers believe they own portal space's exit. Sending moves the player to
/// <see cref="SelfTravel.Materialising"/> through notice <see cref="SessionNotices.LoginCompleteSent"/>.</para>
/// <para>Retail also waits, when the player's contained objects have not all arrived
/// (<c>ACCWeenieObject::AllContainedObjectsExist</c>, 11.4186 0x0058C000, 11.6096 0x0058CE30), and sends from
/// <c>CPlayerSystem::AttemptSendLoginCompleteNotification</c> (0x00562F20 / 0x00563CC0) once they have. M2 keeps no
/// inventory read model, so that wait is not reproduced: TODO(state): the contained-objects wait, with the inventory
/// read model. On ACE the inventory arrives right after PlayerCreate, long before portal space ends.</para>
/// </remarks>
internal sealed class LoginCompleteTracker(ActionHandle handle) : ActionTracker(handle, ActionLane.Immediate, ActionPolicy.LoginCompleteSeconds)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x00A1 LoginComplete";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api)
    {
        var state = api.State;
        if (state.Phase != SessionPhase.InWorld)
        {
            return "not in the world (phase " + state.Phase + ")";
        }

        var travel = state.Self.Travel;
        if (api.LoginCompleteSentThisArrival && travel == SelfTravel.Materialising)
        {
            const string twice = "LoginComplete was already sent for this arrival (m2-log-in.md §4.5: exactly once per arrival)";
            api.Log.Error(SessionLog.Category, "a second LoginComplete for the same arrival was refused: " + twice, new LogField("source", Handle.Source));
            return twice;
        }

        return travel is SelfTravel.Arriving or SelfTravel.TeleportArrived
            ? null
            : "the player is not arriving (travel " + travel + "): LoginComplete ends portal space";
    }

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        session!.SendLoginComplete();
        api.MarkLoginCompleteSent();
        api.ApplyLocal(SessionNotices.LoginCompleteSent);
        return null;
    }

    /// <inheritdoc/>
    internal override void OnTravel(in SelfTravelChanged e)
    {
        switch (e.Travel)
        {
            case SelfTravel.Present:
                Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed));
                break;
            case SelfTravel.NotInWorld:
                Resolve(new ActionOutcome(ActionOutcomeKind.Cancelled, 0, "the player left the world"));
                break;
        }
    }
}

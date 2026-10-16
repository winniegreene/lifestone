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
/// <para><b>The wait (M3 1.2 s5).</b> Retail sends only when the player's weenie exists and every object it contains exists
/// (<c>ACCWeenieObject::AllContainedObjectsExist</c>, 11.4186 0x0058C000, 11.6096 0x0058CE30); otherwise
/// <c>CPlayerSystem::AttemptSendLoginCompleteNotification</c> (0x00562F20 / 0x00563CC0) sends it later, once the player description has
/// arrived and the contents exist. <see cref="WaitReason"/> is that rule: the call (portal space's cue) waits in its lane until the
/// player's object, the description's lists (<see cref="PlayerInventory"/>) and every object they name are in the object table, and goes
/// out in the update after the last of them arrives. A safety cap, <see cref="ActionPolicy.LoginCompleteContentsCapSeconds"/> on the
/// session clock from the call, sends it anyway with a WARNING naming the missing count, so a lost object never strands the player
/// in portal space. A description whose lists could not be decoded (<see cref="PlayerInventory.ListsComplete"/> false) names no objects: only the
/// description's arrival is waited for. Retail's behaviour, so there is no flag. TODO(m3-login-complete-side-pack-contents): the contents of a
/// side pack arrive by ViewContents, not with the description, and are not waited for.</para>
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
    internal override string? WaitReason(ActionApi api)
    {
        var missing = MissingContents(api.State, out var describing);
        if (missing == 0 && !describing)
        {
            return null;
        }

        var waited = api.Clock.Now - Handle.QueuedAt;
        if (waited >= ActionPolicy.LoginCompleteContentsCapSeconds)
        {
            api.Log.Warn(
                SessionLog.Category,
                "LoginComplete is sent after the safety cap without everything retail waits for (CPlayerSystem::SendLoginCompleteNotification): " + Describe(missing, describing),
                new LogField("missing", missing),
                new LogField("description_arrived", !describing),
                new LogField("waited_s", waited),
                new LogField("cap_s", ActionPolicy.LoginCompleteContentsCapSeconds));
            return null;
        }

        if (!_waitLogged)
        {
            _waitLogged = true;
            api.Log.Info(SessionLog.Category, "LoginComplete waits for the player's contents (retail's AllContainedObjectsExist): " + Describe(missing, describing), new LogField("missing", missing));
        }

        return WaitingForContents;
    }

    private const string WaitingForContents = "waiting for the player description and the objects it names";

    private bool _waitLogged;

    private static string Describe(int missing, bool describing) =>
        (describing ? "the player description has not arrived" + (missing > 0 ? " and " : string.Empty) : string.Empty)
        + (missing > 0 ? missing.ToString(System.Globalization.CultureInfo.InvariantCulture) + " object(s) of the player and its contents are not in the object table" : string.Empty);

    /// <summary>
    /// How many of the objects retail's <c>AllContainedObjectsExist</c> needs are not in the object table yet (the player's own counts one), and whether the description's lists have not been applied yet.
    /// Allocation-free: it runs every update while the action waits.
    /// </summary>
    /// <param name="state">The session's state.</param>
    /// <param name="describing">True while the PlayerDescription has not arrived.</param>
    internal static int MissingContents(GameState state, out bool describing)
    {
        var self = state.Self;
        var objects = state.Objects;
        var inventory = self.Inventory;
        var missing = self.Guid == 0 || !objects.TryGet(self.Guid, out _) ? 1 : 0;
        describing = !inventory.IsKnown;
        if (inventory.ListsComplete)
        {
            var pack = inventory.Pack;
            for (var i = 0; i < pack.Count; i++)
            {
                missing += objects.TryGet(pack[i].Guid, out _) ? 0 : 1;
            }

            var sidePacks = inventory.SidePacks;
            for (var i = 0; i < sidePacks.Count; i++)
            {
                missing += objects.TryGet(sidePacks[i].Guid, out _) ? 0 : 1;
            }

            var equipped = inventory.Equipped;
            for (var i = 0; i < equipped.Count; i++)
            {
                missing += objects.TryGet(equipped[i].Guid, out _) ? 0 : 1;
            }
        }

        return missing;
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

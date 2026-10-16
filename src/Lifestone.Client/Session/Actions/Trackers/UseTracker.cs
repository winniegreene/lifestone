using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// <see cref="IActionApi.Use"/>: GameAction 0x0036, as retail's <c>CM_Inventory::Event_UseEvent</c> sends it (11.4186
/// 0x006AC3B0, 11.6096 0x006AD310). ACE answers every use with exactly one GameEvent 0x01C7 UseDone, after walking the
/// player to the object when it is out of reach (ACE <c>Player_Use.HandleActionUseItem</c> and <c>SendUseDoneEvent</c>,
/// ACEmulator/ACE@95ef426d), so a UseDone completes the oldest use in flight: code 0 confirms, any other is a
/// <see cref="ActionOutcomeKind.ServerError"/> with that WeenieError. A teleport the use started (a portal's F751 while
/// the use was in flight) is reported in the outcome's detail.
/// </summary>
internal sealed class UseTracker(ActionHandle handle, uint guid) : ActionTracker(handle, ActionLane.Use, ActionPolicy.UseSeconds)
{
    private bool _teleported;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0036 Use " + ActionText.Hex(guid);

    /// <inheritdoc/>
    internal override bool ConsumesUseDone => true;

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        session!.SendUse(guid);
        return null;
    }

    /// <inheritdoc/>
    internal override void OnTeleport(in PlayerTeleportAnnounced e) => _teleported = true;

    /// <inheritdoc/>
    internal override bool OnUseDone(in UseDone e)
    {
        Resolve(e.WeenieError == 0
            ? new ActionOutcome(ActionOutcomeKind.Confirmed)
            : new ActionOutcome(ActionOutcomeKind.ServerError, e.WeenieError, "the server refused the use (WeenieError " + ActionText.Hex(e.WeenieError) + ")"));
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>A teleport seen before the update completes the use counts too: ACE may send the F751 after the UseDone.</remarks>
    internal override ActionOutcome Finish(ActionOutcome resolved) =>
        _teleported && resolved.Kind == ActionOutcomeKind.Confirmed ? resolved with { Detail = "the use started a teleport (F751)" } : resolved;
}

/// <summary>
/// <see cref="IActionApi.QueryHealth"/>: GameAction 0x01BF, as retail's <c>CM_Combat::Event_QueryHealth</c> sends it
/// (11.4186 0x006A9B40, 11.6096 0x006AAAA0). Completes on GameEvent 0x01C0 UpdateHealth for the object. ACE answers only
/// for a creature on the player's landblock (ACE <c>Player.HandleActionQueryHealth</c>), so any other object times out.
/// Object 0 deselects the health target and has no answer: it completes as <see cref="ActionOutcomeKind.Sent"/>.
/// </summary>
internal sealed class QueryHealthTracker(ActionHandle handle, uint guid) : ActionTracker(handle, ActionLane.Query, ActionPolicy.QueryHealthSeconds)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x01BF QueryHealth " + ActionText.Hex(guid);

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        session!.SendQueryHealth(guid);
        return guid == 0 ? new ActionOutcome(ActionOutcomeKind.Sent, 0, "object 0 deselects; the server does not answer") : null;
    }

    /// <inheritdoc/>
    internal override bool OnHealth(in HealthUpdated e)
    {
        if (e.Guid != guid)
        {
            return false;
        }

        Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed));
        return true;
    }
}

/// <summary>The preconditions the in-world actions share.</summary>
internal static class WorldTrackers
{
    /// <summary>Why an in-world action cannot go now, or null when the player is in the world.</summary>
    internal static string? NotInWorld(ActionApi api)
    {
        var state = api.State;
        if (state.Phase != SessionPhase.InWorld)
        {
            return "not in the world (phase " + state.Phase + ")";
        }

        return state.Self.Guid == 0 ? "not in the world (the player is not known yet)" : null;
    }
}

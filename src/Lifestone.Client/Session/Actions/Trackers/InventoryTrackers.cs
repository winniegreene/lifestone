using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// What Wield, Unwield and MoveToPack share (M3 1.2 s4b): the <see cref="ActionLane.Inventory"/> lane (one request in flight at a time: the server's answers carry no request id),
/// the in-world precondition, and the server's refusal. ACE answers a refusal with GameEvent 0x00A0 (InventoryServerSaveFailed: the item and a WeenieError, which is 0 for most
/// of ACE's refusals), so the detail names the item rather than a reason. A success is the inventory message the reducer applied (<see cref="InventoryChanged"/>): ACE's wield sends
/// 0x02DA and 0x02CE zeros for the item first and the 0x0023 after them, and only the 0x0023 (or 0x0022 for a move) counts (the lane's handoff: 0023 is the authority).
/// </summary>
internal abstract class InventoryTracker(ActionHandle handle, uint item) : ActionTracker(handle, ActionLane.Inventory, ActionPolicy.InventorySeconds)
{
    /// <summary>The item the action moves.</summary>
    protected uint Item { get; } = item;

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api) ?? Precondition(api.State.Self.Inventory.Find(Item), api);

    /// <summary>Why the action cannot go given where the model has the item, or null.</summary>
    protected abstract string? Precondition(InventoryPlace place, ActionApi api);

    /// <inheritdoc/>
    internal override void OnInventoryFailed(in InventoryFailed e)
    {
        if (e.Item != Item)
        {
            return;
        }

        Resolve(new ActionOutcome(
            ActionOutcomeKind.ServerError,
            e.Error,
            "the server refused " + ActionText.Hex(Item) + " (WeenieError " + ActionText.Hex(e.Error) + (e.Error == 0 ? "; ACE gives none for most refusals" : string.Empty) + ")"));
    }
}

/// <summary>
/// <see cref="IActionApi.Wield"/>: GameAction 0x001A, as retail's <c>CM_Inventory::Event_GetAndWieldItem</c> sends it (11.4186 0x006AB9F0, 11.6096 0x006AC950). Refused at once
/// unless the player holds the item (the main pack, a side pack, or another slot when the new location does not overlap the old); confirmed by the 0x0023 that wields it.
/// </summary>
internal sealed class WieldTracker(ActionHandle handle, uint item, EquipMask location) : InventoryTracker(handle, item)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x001A GetAndWieldItem " + ActionText.Hex(Item) + " at " + ActionText.Hex((uint)location);

    /// <inheritdoc/>
    protected override string? Precondition(InventoryPlace place, ActionApi api) => place.Kind switch
    {
        InventoryPlaceKind.None => "the item " + ActionText.Hex(Item) + " is not in the pack or a side pack",
        InventoryPlaceKind.Equipped when (place.Location & (uint)location) != 0 => "the item " + ActionText.Hex(Item) + " is already wielded there",
        _ => null,
    };

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        session!.SendGetAndWieldItem(Item, (uint)location);
        return null;
    }

    /// <inheritdoc/>
    internal override void OnInventory(in InventoryChanged e, ActionApi api)
    {
        if (e.Kind == InventoryChangeKind.Wielded && e.Guid == Item)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "wielded at " + ActionText.Hex(e.Location)));
        }
    }
}

/// <summary>
/// <see cref="IActionApi.Unwield"/>: GameAction 0x0019 into the player's own pack (placement 0), as retail's <c>CM_Inventory::Event_PutItemInContainer</c> sends it (11.4186 0x006ABD20,
/// 11.6096 0x006ACC80). Refused at once unless the model has the item equipped; confirmed by the 0x0022 that moves the equipped item into a container
/// (<see cref="InventoryChangeKind.Unwielded"/>).
/// </summary>
internal sealed class UnwieldTracker(ActionHandle handle, uint item) : InventoryTracker(handle, item)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0019 PutItemInContainer (unwield) " + ActionText.Hex(Item);

    /// <inheritdoc/>
    protected override string? Precondition(InventoryPlace place, ActionApi api) =>
        place.Kind == InventoryPlaceKind.Equipped ? null : "the item " + ActionText.Hex(Item) + " is not wielded or worn";

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        session!.SendPutItemInContainer(Item, api.State.Self.Guid, 0);
        return null;
    }

    /// <inheritdoc/>
    internal override void OnInventory(in InventoryChanged e, ActionApi api)
    {
        if (e.Kind == InventoryChangeKind.Unwielded && e.Guid == Item)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "unwielded into " + ActionText.Hex(e.Container) + " slot " + e.Placement));
        }
    }
}

/// <summary>
/// <see cref="IActionApi.MoveToPack"/>: GameAction 0x0019 {item, container, placement} (container 0 is the player's guid: the main pack) for an item the player holds; an item
/// it does not hold is a <see cref="PickUpTracker"/> (M4 2.1 s2 closed <c>TODO(m3-pickup)</c>: the held-item guard is gone). Confirmed by the 0x0022 that puts it into that container (<see cref="InventoryChangeKind.Moved"/>, or
/// <see cref="InventoryChangeKind.Unwielded"/> for an equipped item); an item put into a container that is not the player's (a chest) is reported by the reducer as
/// <see cref="InventoryChangeKind.Removed"/> with that container, which confirms too.
/// </summary>
internal sealed class MoveToTracker(ActionHandle handle, uint item, uint container, uint placement) : InventoryTracker(handle, item)
{
    private uint _target;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0019 PutItemInContainer " + ActionText.Hex(Item) + " into " + ActionText.Hex(container) + " slot " + placement;

    /// <inheritdoc/>
    protected override string? Precondition(InventoryPlace place, ActionApi api) => null; // M4 2.1 s2: no held-item guard; an item the player does not hold is a pick-up (ActionApi.MoveToPack)

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        _target = container == 0 ? api.State.Self.Guid : container;
        session!.SendPutItemInContainer(Item, _target, placement);
        return null;
    }

    /// <inheritdoc/>
    internal override void OnInventory(in InventoryChanged e, ActionApi api)
    {
        if (e.Guid != Item || e.Container != _target
            || e.Kind is not (InventoryChangeKind.Moved or InventoryChangeKind.Unwielded or InventoryChangeKind.Added or InventoryChangeKind.Removed))
        {
            return;
        }

        Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "moved into " + ActionText.Hex(e.Container) + " slot " + e.Placement));
    }
}

using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// <see cref="IActionApi.MergeStack"/> (M3 1.4 fix A): GameAction 0x0054 {from, to, amount}, as retail's <c>CM_Inventory::Event_StackableMerge</c> sends it (11.4186 0x006ABE70, 11.6096 0x006ACDD0).
/// Refused at once unless the model has the player holding both stacks, of the same weenie class and with a known size, the amount within the source's stack and the sum within the target's maximum.
/// ACE's answer (<c>Player_Inventory.cs</c> <c>HandleActionStackableMerge</c> 2849ff, <c>DoHandleActionStackableMerge</c> 3072ff): a merge inside the pack of a whole source stack sends the source's 0x0024 (3096) and then the
/// target's 0x0197 (3121-3123); ACE's <c>Destroy</c> of the contained source sends no F747 (<c>WorldObject.cs</c> 892 removes only a world object on a landblock). A partial merge sends the source's 0x0197 (3151-3153), then the target's
/// (3159-3161). A refusal is GameEvent 0x00A0 for the source (or, for a
/// stuck or traded target, the target). The answers carry no request id, so the lane holds one at a time and the tracker confirms from the model: the target's stack is its old size plus the amount, and the
/// source is gone (a whole merge) or shrunk by the amount. It allocates nothing while it waits: the checks read the inventory model and compare numbers.
/// </summary>
internal sealed class MergeStackTracker(ActionHandle handle, uint from, uint to, int amount) : InventoryTracker(handle, from)
{
    private uint _sourceBefore;
    private uint _targetExpected;
    private bool _whole;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0054 StackableMerge " + ActionText.Hex(Item) + " into " + ActionText.Hex(to) + " x" + ActionText.Count(amount);

    /// <inheritdoc/>
    protected override string? Precondition(InventoryPlace place, ActionApi api)
    {
        if (Item == to)
        {
            return Refuse(ActionReason.NotSameItem, "cannot merge the stack " + ActionText.Hex(Item) + " into itself");
        }

        if (!place.Held && !InViewedContainer(api.State, Item))
        {
            return Refuse(ActionReason.NotHeld, "the source stack " + ActionText.Hex(Item) + " is not held by the player and not in the container she has open");
        }

        var inventory = api.State.Self.Inventory;
        if (!inventory.Find(to).Held)
        {
            return Refuse(ActionReason.NotHeld, "the target stack " + ActionText.Hex(to) + " is not held by the player");
        }

        if (!api.State.Objects.TryGet(Item, out var source) || !api.State.Objects.TryGet(to, out var target))
        {
            return Refuse(ActionReason.TargetGone, "a stack to merge has no world object yet");
        }

        if (source.Weenie.WeenieClassId != target.Weenie.WeenieClassId)
        {
            return Refuse(ActionReason.NotSameItem, "the stacks " + ActionText.Hex(Item) + " and " + ActionText.Hex(to) + " are different items (ACE: YouCannotMergeDifferentStacks)");
        }

        if (SizeOf(inventory, source) is not { } sourceSize || SizeOf(inventory, target) is not { } targetSize)
        {
            return Refuse(ActionReason.NotStackable, "a stack to merge has no stack size (the item does not stack)");
        }

        if (amount > sourceSize)
        {
            return Refuse(ActionReason.StackShort, "the source stack " + ActionText.Hex(Item) + " holds " + ActionText.Count(sourceSize) + ", not " + ActionText.Count(amount));
        }

        if (target.Weenie.MaxStackSize is { } max && (long)targetSize + amount > max)
        {
            return Refuse(ActionReason.StackFull, "the target stack " + ActionText.Hex(to) + " holds " + ActionText.Count(targetSize) + " of at most " + ActionText.Count(max) + ": " + ActionText.Count(amount) + " more does not fit");
        }

        return null;
    }

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        var inventory = api.State.Self.Inventory;
        _sourceBefore = SizeOf(inventory, api.State.Objects.TryGet(Item, out var source) ? source : null) ?? 0;
        _targetExpected = (SizeOf(inventory, api.State.Objects.TryGet(to, out var target) ? target : null) ?? 0) + (uint)amount;
        _whole = amount == _sourceBefore;
        session!.SendStackableMerge(Item, to, amount);
        return null;
    }

    /// <inheritdoc/>
    internal override void OnInventory(in InventoryChanged e, ActionApi api)
    {
        if ((e.Guid != Item && e.Guid != to) || e.Kind is not (InventoryChangeKind.StackChanged or InventoryChangeKind.Removed))
        {
            return;
        }

        var inventory = api.State.Self.Inventory;
        if (!inventory.TryGetStack(to, out var targetNow, out _))
        {
            return;
        }

        if (e.Guid == to && e.Kind == InventoryChangeKind.StackChanged && targetNow != _targetExpected)
        {
            // M5 7 D141's after-check: ACE sends the target's 0197 last (the source's 0024 or 0197 comes first), so a target that changed to another size is the server's answer, not a step on the way:
            // the counts are not the ones asked (a stale model, or another change to the stack). It fails at once, typed, instead of waiting out the timeout.
            Resolve(new ActionOutcome(
                ActionOutcomeKind.ServerError,
                0,
                "the merge of " + ActionText.Count(amount) + " into " + ActionText.Hex(to) + " left the target at " + ActionText.Count(targetNow) + ", not the " + ActionText.Count(_targetExpected) + " asked (the source was " +
                ActionText.Count(_sourceBefore) + ")",
                ActionReason.ContraryResult));
            return;
        }

        if (targetNow != _targetExpected)
        {
            return;
        }

        if (_whole)
        {
            if (inventory.Contains(Item))
            {
                return;
            }
        }
        else if (inventory.Contains(Item) && (!inventory.TryGetStack(Item, out var sourceNow, out _) || sourceNow != _sourceBefore - (uint)amount))
        {
            return; // a source in the open container is not in the pack's model: the target's size is the answer
        }

        Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "merged " + ActionText.Count(amount) + " into " + ActionText.Hex(to) + ", now " + ActionText.Count(targetNow)));
    }

    /// <inheritdoc/>
    internal override void OnInventoryFailed(in InventoryFailed e)
    {
        if (e.Item != Item && e.Item != to)
        {
            return;
        }

        Resolve(new ActionOutcome(
            ActionOutcomeKind.ServerError,
            e.Error,
            "the server refused the merge of " + ActionText.Hex(Item) + " into " + ActionText.Hex(to) + " (WeenieError " + ActionText.Hex(e.Error) + (e.Error == 0 ? "; ACE gives none for most refusals" : string.Empty) + ")"));
    }

    /// <summary>
    /// M6 sprint (run 5, "Unable to put Pyreal into container" eight times with the main pack at 102 of 102): a source still in the container she has open (a corpse's coin) may merge into a held stack, as retail lets a player drag
    /// it onto the stack. ACE finds the source among <c>SearchLocations.LocationsICanMove</c> (<c>HandleActionStackableMerge</c>, <c>Player_Inventory.cs</c> 2849ff), checks the burden of a source from outside the pack, and for a whole
    /// source sends its 0x0024 and the target's 0x0197 (<c>DoHandleActionStackableMerge</c> 3096-3123 @95ef426d).
    /// </summary>
    private static bool InViewedContainer(GameState state, uint guid)
    {
        if (state.Containers.Viewed is not { } view)
        {
            return false;
        }

        foreach (var entry in view.Entries)
        {
            if (entry.Guid == guid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The stack the model holds for a held stack: the server's last 0x0197, else the weenie description's; null for an item with neither.</summary>
    private static uint? SizeOf(PlayerInventory inventory, WorldObject? item)
    {
        if (item is null)
        {
            return null;
        }

        if (inventory.TryGetStack(item.Guid, out var size, out _))
        {
            return size;
        }

        return item.Weenie.StackSize is { } stack ? stack : null;
    }
}

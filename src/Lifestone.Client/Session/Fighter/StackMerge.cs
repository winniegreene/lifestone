using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Fighter;

/// <summary>
/// Which held stack a new stack joins (M5 7). ACE never merges a stack by itself: a pick-up (<c>Container.cs</c> 499-581 <c>TryAddToInventory</c>, <c>Player_Inventory.cs</c> 1268ff <c>DoHandleActionPutItemInContainer</c>) and a purchase
/// (<c>Player_Commerce.cs</c> 59-102 <c>FinalizeBuyTransaction</c> into <c>Player_Inventory.cs</c> 90 <c>TryCreateInInventoryWithNetworking</c>) both add a new object, so every looted or bought stack takes a pack slot until the client
/// sends GameAction 0x0054 (<see cref="IActionApi.MergeStack"/>; <c>HandleActionStackableMerge</c> <c>Player_Inventory.cs</c> 2849ff). This is the one place that decides it, for the corpse looter and the resupply's buys.
/// The target is the fullest other held stack of the weenie class that has room, in the pack or a side pack and not an equipped one; the amount is the smaller of the source and the room. With <c>mainPackTarget</c> (M5 7 D136, the
/// trade notes and the coin, which D121 keeps in the main pack) only a stack in the main pack itself is a target, so a merge never moves them into a side pack (and a stack ACE put in a side pack when the main pack was full comes home).
/// </summary>
internal static class StackMerge
{
    /// <summary>
    /// The merge of the held stack <paramref name="source"/> into another held stack of its weenie class. False when the source does not stack, is not held, or no other stack has room.
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="source">The stack to merge away.</param>
    /// <param name="scratch">A list to collect the held guids into (cleared first).</param>
    /// <param name="target">The stack it joins.</param>
    /// <param name="amount">How many of the source go: the smaller of its size and the target's room.</param>
    /// <param name="targetSize">The target's size now.</param>
    /// <param name="room">The target's room now.</param>
    /// <param name="mainPackTarget">True to take only a stack in the main pack itself as the target (D121's notes and coin).</param>
    internal static bool TryPlan(GameState state, uint source, List<uint> scratch, out uint target, out int amount, out int targetSize, out int room, bool mainPackTarget = false)
    {
        target = 0;
        amount = 0;
        targetSize = -1;
        room = 0;
        if (!state.Objects.TryGet(source, out var item) || item.Weenie.MaxStackSize is not > 1 || !state.Self.Inventory.Find(source).Held || GearInventory.StackOf(state, item) >= item.Weenie.MaxStackSize)
        {
            return false; // M5 7 D136 follow-up: a full stack is never a source (moving out of it frees no slot)
        }

        var wcid = item.Weenie.WeenieClassId;
        GearInventory.Collect(state, scratch);
        for (var i = 0; i < scratch.Count; i++)
        {
            var other = scratch[i];
            var place = state.Self.Inventory.Find(other).Kind;
            if (other == source || !state.Objects.TryGet(other, out var candidate) || candidate.Weenie.WeenieClassId != wcid || place == InventoryPlaceKind.Equipped || (mainPackTarget && place != InventoryPlaceKind.MainPack))
            {
                continue;
            }

            var max = candidate.Weenie.MaxStackSize ?? item.Weenie.MaxStackSize ?? 0;
            var have = GearInventory.StackOf(state, candidate);
            if (max - have > 0 && have > targetSize)
            {
                target = other;
                targetSize = have;
                room = max - have;
            }
        }

        if (target == 0)
        {
            return false;
        }

        amount = Math.Min(GearInventory.StackOf(state, item), room);
        return amount > 0;
    }

    /// <summary>
    /// M6 sprint (run 5): the held stack a stack still in the open container joins whole, when the pack it would land in has no free slot (a pick-up there is refused, "Unable to put X into container", <c>Player_Inventory.cs</c>
    /// 1318-1322 @95ef426d): the fullest held, unequipped stack of its weenie class whose room takes all of it. False when the item does not stack, its size is unknown, or no held stack has the room.
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="item">The stack in the open container.</param>
    /// <param name="scratch">A list to collect the held guids into (cleared first).</param>
    /// <param name="target">The held stack it joins.</param>
    /// <param name="size">The item's stack size (the amount the merge sends).</param>
    /// <param name="room">The target's room now.</param>
    internal static bool TryPlanWhole(GameState state, WorldObject item, List<uint> scratch, out uint target, out int size, out int room)
    {
        target = 0;
        room = 0;
        size = GearInventory.StackOf(state, item);
        if (item.Weenie.MaxStackSize is not > 1 || size <= 0)
        {
            return false;
        }

        var best = -1;
        GearInventory.Collect(state, scratch);
        foreach (var other in scratch)
        {
            if (other == item.Guid || !state.Objects.TryGet(other, out var candidate) || candidate.Weenie.WeenieClassId != item.Weenie.WeenieClassId || state.Self.Inventory.Find(other).Kind == InventoryPlaceKind.Equipped)
            {
                continue;
            }

            var max = candidate.Weenie.MaxStackSize ?? item.Weenie.MaxStackSize ?? 0;
            var have = GearInventory.StackOf(state, candidate);
            if (max - have >= size && have > best)
            {
                (target, best, room) = (other, have, (int)max - have);
            }
        }

        return target != 0;
    }

    /// <summary>
    /// The next merge among the held, unequipped stacks of weenie class <paramref name="wcid"/>: only partial stacks take part (more than 0, under the MaxStackSize), the smallest is the source and the largest other partial the target
    /// (ties by guid), so coin never leaves a full stack. Every merge either empties the source or fills the target, so each one leaves one partial fewer and the merges end when at most one partial is left: no ping-pong
    /// (M5 7 D136 follow-up, soak round 13: the old plan took a full 25,000 stack as a source into a partial one and back, 36 merges in one trip). <paramref name="skip"/> holds the sources whose merge was refused; they are not tried
    /// again. <paramref name="mainPackTarget"/> as in <see cref="TryPlan"/>: a target must be in the main pack, and a partial in a side pack is a source that comes home whatever its size.
    /// </summary>
    internal static bool TryPlanFor(GameState state, uint wcid, List<uint> scratch, HashSet<uint> skip, out uint source, out uint target, out int amount, bool mainPackTarget = false)
    {
        source = 0;
        target = 0;
        amount = 0;
        GearInventory.Collect(state, scratch);
        var partial = new List<(uint Guid, int Size, int Max, bool Main)>();
        foreach (var guid in scratch)
        {
            var kind = state.Self.Inventory.Find(guid).Kind;
            if (!state.Objects.TryGet(guid, out var o) || o.Weenie.WeenieClassId != wcid || o.Weenie.MaxStackSize is not (> 1 and var max) || kind == InventoryPlaceKind.Equipped)
            {
                continue;
            }

            var size = GearInventory.StackOf(state, o);
            if (size > 0 && size < max)
            {
                partial.Add((guid, size, (int)max, kind == InventoryPlaceKind.MainPack));
            }
        }

        if (partial.Count < 2)
        {
            return false;
        }

        partial.Sort((a, b) => a.Size != b.Size ? a.Size.CompareTo(b.Size) : a.Guid.CompareTo(b.Guid));
        foreach (var from in partial)
        {
            if (skip.Contains(from.Guid))
            {
                continue;
            }

            // the largest other partial that may take it: above the source in (size, guid) order, or any main-pack partial for a source that is away from the main pack
            for (var i = partial.Count - 1; i >= 0; i--)
            {
                var to = partial[i];
                var above = to.Size > from.Size || (to.Size == from.Size && to.Guid > from.Guid);
                if (to.Guid == from.Guid || (mainPackTarget && !to.Main) || !(above || (mainPackTarget && !from.Main)))
                {
                    continue;
                }

                source = from.Guid;
                target = to.Guid;
                amount = Math.Min(from.Size, to.Max - to.Size);
                return amount > 0;
            }
        }

        return false;
    }
}

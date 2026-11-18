using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Quartermaster;

/// <summary>
/// M6 item 16, slice Q10b (docs/automation-engine.md section 2.5): the quartermaster's one read of "how many slots are free". The question was written twice, once by the loot rule and the stations (the main pack) and once
/// by the buy tracker (every pack), and a third time per job by the pack layout; each means something different, so each stays a named call and none changes its answer:
/// <list type="bullet">
/// <item><see cref="MainPackFree"/>: the main pack's capacity less the ordinary items in it; a side pack and what lies in it are not main-pack slots. What the loot plan, the sale's payout stacks, the shelf buys, the
/// wear swaps and the trip's slot line ask.</item>
/// <item><see cref="AllPacksLessFociFree"/>: the main pack's and every side pack's free slots as ACE counts them (<c>Container.GetFreeInventorySlots</c>, <c>Container.cs</c> 214-225 @95ef426d), a foci taking a main pack slot;
/// null when the inventory or a side pack's capacity is unknown. What a buy's slot precondition and the recovery table's room check ask.</item>
/// <item>The layout's per-job room stays with the layout (<see cref="PackLayout.FreeIn"/>): it answers for one job's pack, from the pack the layout assigned.</item>
/// </list>
/// </summary>
public static class PackView
{
    /// <summary>The main pack's free slots now: its capacity (the player weenie's <c>ItemsCapacity</c> when it was sent, else <see cref="GearKit.PackCapacity"/>) less the ordinary items it holds (a side pack and what is in it are not main-pack slots).</summary>
    /// <param name="state">The session's read model.</param>
    public static int MainPackFree(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var capacity = state.Objects.TryGet(state.Self.Guid, out var self) && self.Weenie.ItemsCapacity is > 0 ? (int)self.Weenie.ItemsCapacity.Value : GearKit.PackCapacity;
        return Math.Max(0, capacity - state.Self.Inventory.Pack.Count);
    }

    /// <summary>
    /// The packs' free slots as ACE counts them (<c>Container.GetFreeInventorySlots</c>, <c>Container.cs</c> 214-225 @95ef426d): the main pack's capacity less its items, plus each side pack's capacity less its items;
    /// a foci takes a main pack slot. An upper bound when the model misses an item; null when the inventory is not known or a side pack's capacity is not.
    /// </summary>
    /// <param name="state">The session's read model.</param>
    public static int? AllPacksLessFociFree(GameState state)
    {
        var inventory = state.Self.Inventory;
        if (!inventory.IsKnown)
        {
            return null;
        }

        var capacity = state.Objects.TryGet(state.Self.Guid, out var self) && self.Weenie.ItemsCapacity is > 0 ? self.Weenie.ItemsCapacity.Value : BuyTracker.PlayerPackCapacity;
        var free = capacity - inventory.Pack.Count;
        foreach (var pack in inventory.SidePacks)
        {
            if (pack.ContainerType != 1)
            {
                free--; // a foci sits in a main pack slot
                continue;
            }

            if (!state.Objects.TryGet(pack.Guid, out var side) || side.Weenie.ItemsCapacity is not > 0)
            {
                return null;
            }

            free += side.Weenie.ItemsCapacity.Value - inventory.ItemsIn(pack.Guid).Count;
        }

        return free;
    }
}

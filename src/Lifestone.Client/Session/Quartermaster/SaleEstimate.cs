using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Quartermaster;

/// <summary>
/// M6 16 E5c2 (docs/automation-engine.md E5, <c>TODO(engine-sale-copies)</c>): what a town trip's sale would bring as the pack stands, for the trip trigger (<see cref="Mage.LogisticsCounters.CarriedSaleValue"/>): the items the
/// rule book's verdict sells that a buyer of the ground takes (<see cref="SaleVerdict.WillSell"/>), valued as the estimate always valued them: the Value of each, and its payout at the best rate among the ground's buyers of
/// its type, rounded down an item.
/// <para><b>Before this slice</b> the estimate had a list of its own (an item with a Value that is no stock or kit line, focus, note, coin, caster or container, not noted as kept aside, not unsellable by flag or class, of a type
/// a vendor buys, and no leather-set piece while the swap is to come). It counted as sale value what the sale keeps: gear inside the side packs, rotation gear and the pieces kept for review, a keep-aside match the loot step
/// had not noted, a quest file's item, a kept scroll, the mana stones the kit keeps, the drinks she uses, an attuned piece and one the appraisal calls retained, an item dropped before, a component her book burns; and it left
/// out what the sale sells: a caster her gear rules do not need.</para>
/// <para><b>What it cannot know.</b> The verdict is asked on the pack as it stands. A wearable she has not appraised reads as loot here (<c>gear-loot</c>); the trip appraises it first, and a piece its wear pick then wears or
/// keeps for review is not sold. A drink stack the trip's budget would split is counted only by its whole stacks (<see cref="Scenarios.Journey.PackBudgetRead.SurplusNow"/>).</para>
/// <para><b>The cost.</b> One context of the session (<see cref="SaleDesk.BookOn"/>) and one verdict an item held: it allocates (the context's closures, the gear keeps on the first caster or stone, a row's lookup an item),
/// so the caller asks only when the pack changed, never on a check that reads its cached answer. Measured in the unit rig on the development machine (2026-11-18, <c>GC.GetAllocatedBytesForCurrentThread</c> around one
/// estimate): 30,216 bytes and about 0.45 ms for a pack of 8 with a caster and a mana stone (the gear keeps worked out); 165,944 bytes and 1.4 to 2.3 ms for a pack of 146 plain items: about 1.1 KB an item held.</para>
/// </summary>
public static class SaleEstimate
{
    /// <summary>The Value of what the sale would sell and the pyreals it would pay.</summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="ground">The ground (its home vendors' masks).</param>
    /// <param name="buyers">The ground's buyers (<see cref="HuntingGround.LootBuyers"/>, read once by the caller).</param>
    public static (long Value, long Payout) Of(GameState state, HuntingGround ground, IReadOnlyList<LootBuyer> buyers)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(buyers);
        var book = SaleDesk.For(state).BookOn(ground);
        long total = 0, payout = 0;
        foreach (var (guid, item, inSidePack) in SaleVerdict.Read(state))
        {
            if (!SaleVerdict.WillSell(guid, item, inSidePack, book))
            {
                continue;
            }

            var value = (long)(item.Weenie.Value ?? 0);
            var rate = 0.0;
            for (var b = 0; b < buyers.Count; b++)
            {
                if ((buyers[b].ItemTypes & item.ItemType) != 0 && buyers[b].Rate > rate)
                {
                    rate = buyers[b].Rate;
                }
            }

            total += value;
            payout += (long)Math.Floor(value * rate);
        }

        return (total, payout);
    }
}

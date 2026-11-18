using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quartermaster;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 16 E5b: the town trip at the vendor counter (<see cref="VendorCounter"/>): the buy is the counter's, and what the trip says and counts while the counter works is said here, in the trip's own words: the price beside the
/// profile's, the stock that holds fewer, the note split and changed, the purse that is short, the buy, the merges. Every line and counter is what <c>BuyLine</c>, <c>SellNote</c> and <c>MergeBought</c> wrote before the fold.
/// </summary>
public sealed partial class ResupplyRun
{
    /// <summary>The trip's host at the counter: for a buy, the ground, the vendor and the line; for a merge alone (the notes, the coin), none of them.</summary>
    private sealed class TownTripCounter(ResupplyRun run, HuntingGround? ground, GroundVendor? vendor, ResupplyBuy? buy) : VendorCounterHost("resupply", run.Log, run.Until, run.MainRoom)
    {
        /// <inheritdoc />
        public override void Judged(CheckedBuy.Verdict verdict)
        {
            if (verdict.Entry is not { } entry || buy is null || vendor is null || ground is null || run.S.State.Vendor is not { } stock)
            {
                return;
            }

            var name = buy.Line.Name;
            if (PriceNote(name, vendor.Name, stock.PriceToBuy(entry, 1), ground.PriceOf(buy.Line.Wcid) ?? vendor.Sells.FirstOrDefault(w => w.Wcid == buy.Line.Wcid)?.Price) is { } note) // M6 item 14: a vendor the profile does not list (the leather set's) is read by his own sells
            {
                run.Log(note);
            }

            if (!entry.IsUnlimited && entry.Stack > 0 && buy.Count > entry.Stack)
            {
                run.Log(Text($"resupply: {name}: {vendor.Name} holds {entry.Stack}, fewer than the {buy.Count} to buy: buys {entry.Stack}"));
            }
        }

        /// <inheritdoc />
        public override void NoteSplit(PurseNote note, uint one, uint stack, int size) =>
            run.Log(Text($"purse: split one {note.Name} 0x{one:X8} from its stack 0x{stack:X8} (x{size}) to change it"));

        /// <inheritdoc />
        public override void NoteChanged(PurseNote note, string vendor, long paid, long before, long after)
        {
            run.NotesSold++;
            run.Log(Text($"purse: changed {note.Name} to coin at {vendor}: paid {paid} pyreals (the coin {before} -> {after})"));
        }

        /// <inheritdoc />
        public override void NoteRefused(string kind, string what) => run.Refuse(kind, what);

        /// <inheritdoc />
        public override void PurseShort(string ware, long cost, long coins, int count, int affordable, string noNote)
        {
            run.Short++;
            run.Log(Text($"purse: short by {cost - coins} pyreals for {ware} (the {count} cost {cost}, the coin is {coins} and {noNote}): buys {affordable}"));
        }

        /// <inheritdoc />
        public override void Bought(int count, long paid)
        {
            run.Bought += count;
            run.Spent += paid;
            run.Log(Text($"resupply: bought {count} {buy?.Line.Name} at {vendor?.Name} for {paid} pyreals"));
        }

        /// <inheritdoc />
        public override void Merging(bool bought, string name, uint from, uint to, int amount) =>
            run.Log(Text($"resupply: merging a {(bought ? "bought" : "held")} {name} stack 0x{from:X8} into 0x{to:X8}: x{amount}"));

        /// <inheritdoc />
        public override void Merged(bool confirmed, string name, uint from, uint to, string outcome)
        {
            if (confirmed)
            {
                run.StackMerges++;
                return;
            }

            run.MergesRefused++;
            run.Log(Text($"resupply: the merge of {name} 0x{from:X8} into 0x{to:X8} ended {outcome}; counted, the stack stays, going on"));
        }
    }
}

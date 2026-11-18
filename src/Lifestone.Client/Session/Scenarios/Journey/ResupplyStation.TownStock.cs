using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>M6 14 T: one buy of a stop in another town: the profile's town stock line and the count the rule chose (<c>TownStock.Plan</c>).</summary>
/// <param name="Line">The line.</param>
/// <param name="Count">The count to buy.</param>
public sealed record TownStockOrder(TownStockLine Line, int Count);

/// <summary>
/// M6 14 T (the owner, 2026-11-11: carry health tinctures, and Mana Tinctures too): the shop step of a stop in another town, run by the rung change between its two hops (<c>RungChangeStation.TownStockStops</c>) as
/// <see cref="ResupplyOptions.TownShop"/>: the orders of one vendor. It is the home walk's own machinery and nothing else: the walk to the vendor (<c>Meet</c>), his shop opened, each line bought (<c>BuyLine</c>: the wire's price, a note
/// changed when the coin is short, ACE's answer named). Check, act, check: before, the player stands in the vendor's landblock and the counts held are read; after each buy the count held is read again and logged; a stop that added
/// nothing at all fails the step by name (the caller reads the counts itself, remembers what failed and goes on).
/// </summary>
public sealed partial class ResupplyRun
{
    /// <summary>The count the town stop added to the pack, over all its lines (the counts held after less the counts held before).</summary>
    public int TownBought { get; private set; }

    private IEnumerable<ActionHandle?> TownShopScript(HuntingGround ground, IReadOnlyList<TownStockOrder> orders)
    {
        if (orders.Count == 0)
        {
            yield break;
        }

        var vendor = orders[0].Line.Vendor;
        var town = orders[0].Line.Town;
        Stage = "town-stock:vendor:" + vendor.Name;
        if (S.State.Self.Position is not { } here || here.CellId >> 16 != vendor.Cell >> 16)
        {
            Fail(Text($"town stock: not in {town}: the player stands in 0x{S.State.Self.Position?.CellId ?? 0:X8}, not in {vendor.Name}'s landblock 0x{vendor.Cell >> 16:X4}"));
            yield break;
        }

        Log(Text($"walking: purpose=vendor:{vendor.Name} (the stop in {town}: {string.Join(", ", orders.Select(o => Text($"{o.Count} x {o.Line.Name}")))}; the coin {Coins()})"));
        uint guid = 0;
        foreach (var h in Meet(vendor, g => guid = g)) { yield return h; }

        if (guid == 0)
        {
            Fail(Text($"town stock: {vendor.Name} 0x{vendor.Guid:X8} was not met in reach: {_unreachable ?? Text($"not seen within {NpcSeconds:0} s")}"));
            yield break;
        }

        var open = S.Actions.OpenVendor(guid, ActionSource.Automation);
        yield return open;
        if (open.Outcome is not { Kind: ActionOutcomeKind.Confirmed } || S.State.Vendor is not { } opened || opened.Vendor != guid)
        {
            Fail(Text($"town stock: {vendor.Name}'s shop did not open: {Describe(open)}"));
            yield break;
        }

        Vendors++;
        foreach (var order in orders)
        {
            var line = order.Line;
            var held = GearInventory.CountOf(S.State, [], line.Wcid);
            if (S.State.Vendor is { } stock && stock.Vendor == guid && stock.Entries.FirstOrDefault(e => e.WeenieClassId == line.Wcid) is { } entry && stock.PriceToBuy(entry, 1) is var wire && wire != line.Price)
            {
                Log(Text($"town stock: {line.Name}: {vendor.Name}'s 0062 price is {wire}, the profile says {line.Price}: the wire's price is paid"));
            }

            var spent = Spent;
            foreach (var h in BuyLine(ground, vendor, guid, new ResupplyBuy(new StockLine(line.Wcid, line.Name, line.Keep, line.TripUnder, 0), vendor, held, order.Count))) { yield return h; }

            var after = GearInventory.CountOf(S.State, [], line.Wcid);
            TownBought += Math.Max(0, after - held);
            Log(Text($"town stock: check after: {line.Name} {held} -> {after} held at {vendor.Name} in {town} ({order.Count} asked, {Spent - spent} pyreals paid, the coin {Coins()})"));
        }

        if (TownBought == 0)
        {
            Fail(Text($"town stock: nothing was bought at {vendor.Name}: {(_refusals.Count == 0 ? "no refusal was named" : _refusals[^1])}"));
        }
    }
}

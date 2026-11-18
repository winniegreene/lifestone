using System.Globalization;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Grounds;

/// <summary>
/// D138 (m6-plan.md; the owner, 2026-11-14): the buff bot's fee as a supply line. From <see cref="BuffBotWire.FeeLevel"/> a round at the bot costs one credit, one <see cref="BuffBotWire.FeeName"/> handed to it, so a
/// climber at or over that level carries them whatever her ground: every ground's town stock holds a line of the fee's ware (<see cref="With"/>), bought where it is sold on a way out, and the line asks for the ware from the
/// fee's level whatever her spellbook (<see cref="NeedWhy"/>).
/// </summary>
/// <remarks>
/// <para><b>Its counts, from the stop's own rule.</b> The stop at the bot is made at home whenever a line of the bot's has under <see cref="BuffBotRule.RenewUnderSeconds"/> left of its hour, so at most once each
/// 3600 - 2700 = 900 s: <see cref="StopsPerHour"/> = 4 stops an hour, a scarab each. The trip line is an hour of stops (<see cref="TripUnder"/> = 4) and the keep three hours (<see cref="Keep"/> = 12), so a way out
/// every hour or two keeps her paying.</para>
/// <para><b>Where it is bought.</b> A ground that names the ware already keeps it: a home stock line (the dens carry 100) covers the fee as it is; a town stock line (the Holding's at Uziz) is merged, the larger keep and
/// trip line of the two, and asks from the fee's level as well as for her book. Any other ground gets a line at the catalog's cheapest seller of the ware (<see cref="ScrivenerCatalog.ScarabSellers"/>, ties in the
/// catalog's order, never a home vendor or one in the home town): Archmage Sammam al-Ashiz of Uziz, 15,500 pyreals (SellPrice 1.55 x Value 10,000). Its stop is made only on a way out that passes Uziz's door
/// (<c>RungChangeStation</c>'s town stock); a ground whose way out passes none keeps the line inert, and with no scarab and no credit her own pass covers the bot's lines.</para>
/// </remarks>
public static class BuffBotFee
{
    /// <summary>The ware's burden (weenie 8897, PropertyInt 5, in the pinned world SQL v0.9.296; the Holding's line).</summary>
    public const int Burden = 4;

    /// <summary>The most stops at the bot an hour: one each time a line falls under <see cref="BuffBotRule.RenewUnderSeconds"/> of its hour.</summary>
    public static int StopsPerHour { get; } = (int)Math.Ceiling(3600 / (3600 - BuffBotRule.RenewUnderSeconds));

    /// <summary>The line's trip line: an hour of stops.</summary>
    public static int TripUnder { get; } = StopsPerHour;

    /// <summary>The line's keep: three hours of stops.</summary>
    public static int Keep { get; } = 3 * StopsPerHour;

    /// <summary>The ground's town stock with the fee's line in it (see the class).</summary>
    /// <param name="lines">The profile's town stock.</param>
    /// <param name="supplies">The profile's supplies.</param>
    /// <param name="homeTown">The ground's home town.</param>
    /// <param name="homeVendors">The ground's home vendors.</param>
    /// <param name="catalog">The scrivener catalog (its scarab sellers).</param>
    public static IReadOnlyList<TownStockLine> With(IReadOnlyList<TownStockLine> lines, GroundSupplies supplies, string homeTown, IReadOnlyList<GroundVendor> homeVendors, ScrivenerCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(supplies);
        ArgumentNullException.ThrowIfNull(homeVendors);
        ArgumentNullException.ThrowIfNull(catalog);
        if (supplies.StockOf(BuffBotWire.FeeWcid) is not null)
        {
            return lines; // a home stock line keeps the ware already
        }

        var merged = new List<TownStockLine>(lines.Count + 1);
        var found = false;
        foreach (var line in lines)
        {
            if (line.Wcid == BuffBotWire.FeeWcid)
            {
                found = true;
                merged.Add(line with { Keep = Math.Max(line.Keep, Keep), TripUnder = Math.Max(line.TripUnder, TripUnder), FeeFromLevel = BuffBotWire.FeeLevel, FeeOnly = false });
            }
            else
            {
                merged.Add(line);
            }
        }

        if (!found && LineAt(catalog, homeTown, homeVendors) is { } fee)
        {
            merged.Add(fee);
        }

        return merged;
    }

    /// <summary>The fee's line at the cheapest seller of the ware away from home, or null when the catalog lists none.</summary>
    /// <param name="catalog">The scrivener catalog.</param>
    /// <param name="homeTown">The home town.</param>
    /// <param name="homeVendors">The home vendors.</param>
    public static TownStockLine? LineAt(ScrivenerCatalog catalog, string homeTown, IReadOnlyList<GroundVendor> homeVendors)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(homeVendors);
        TownStockLine? best = null;
        foreach (var seller in catalog.ScarabSellersOf(BuffBotWire.FeeWcid))
        {
            if (homeVendors.Any(v => v.Wcid == seller.Wcid) || seller.SaleOf(BuffBotWire.FeeWcid) is not { } sale)
            {
                continue;
            }

            var price = seller.PriceOf(sale.Value);
            foreach (var place in seller.Places)
            {
                if (string.Equals(place.Town, homeTown, StringComparison.Ordinal) || (best is not null && best.Price <= price))
                {
                    continue;
                }

                var vendor = new GroundVendor(seller.Name, seller.Wcid, place.Guid, place.Cell, place.Point, place.Cell, null, 0, [new VendorWare(BuffBotWire.FeeWcid, BuffBotWire.FeeName, price)]);
                best = new TownStockLine(BuffBotWire.FeeWcid, BuffBotWire.FeeName, Keep, TripUnder, price, Burden, place.Town, vendor) { FeeFromLevel = BuffBotWire.FeeLevel, FeeOnly = true };
            }
        }

        return best;
    }

    /// <summary>
    /// Why the fee does not ask for the ware of <paramref name="line"/> at <paramref name="level"/> (the token and the words), or null when it does or does not decide. A fee line asks from its level, whatever the book;
    /// under it the fee's own line is not needed (<c>under-fee</c>: a round is free), and a merged line falls back to its book's rule.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="level">Her level.</param>
    /// <param name="decided">True when the fee decided (needed, or the fee's own line under its level).</param>
    public static (string Token, string Why)? NeedWhy(TownStockLine line, int level, out bool decided)
    {
        ArgumentNullException.ThrowIfNull(line);
        decided = false;
        if (line.FeeFromLevel is not { } fee)
        {
            return null;
        }

        if (level >= fee)
        {
            decided = true;
            return null;
        }

        if (!line.FeeOnly)
        {
            return null;
        }

        decided = true;
        return ("under-fee", string.Create(CultureInfo.InvariantCulture, $"level {level} is under the buff bot's fee level {fee}: a round is free, so no {line.Name} is carried for it"));
    }
}

using System.Globalization;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 audit (fix 2, the owner's decision of 2026-11-19: town trips once an hour, just in time for the buff bot's rebuff; about 75 minutes of consumables, <see cref="PackBudgetRule.SupplyMinutes"/>): whether a
/// trip the counters call waits for the bot's hourly trip. The bot's trip is the town trip (the forced logistics trip with its stop at the bot), so a trip for a reason it serves waits for it, unless a stock line
/// would run down to its stop line first. Pure: every number is given.
/// <para><b>Waits:</b> a stock line under its trip line (<see cref="LogisticsCause.StockLow"/>) that lasts, at her use since the last visit, until the bot's trip; a scroll or a focus due; a sale; the sale cadence;
/// the main pack's slots. <b>Never waits:</b> a stop line, the burden line, a full pack of keepers, the hour's load trip; a stock line with no visit to measure her use from; a bot trip due now or unread.</para>
/// Live 2026-11-19 (run 4, level 11, the Burrow): a Mana Potion trip at 04:02:21 UTC and a taper trip at 04:25:50, 23 minutes apart, each its own walk home, while the bot's hour ran on.
/// </summary>
public static class TripHourRule
{
    /// <summary>True when the trip waits for the bot's trip; <paramref name="why"/> says why either way.</summary>
    /// <param name="cause">The counters' cause.</param>
    /// <param name="botLeast">The seconds left on the bot's least line (<c>BuffBotRule.MinRemaining</c>); NaN or infinite when unread.</param>
    /// <param name="tripUnder">The seconds left at which the bot's trip is due (<c>BuffBotRule.TripUnderSeconds</c>).</param>
    /// <param name="held">The count held of the stock line (a <see cref="LogisticsCause.StockLow"/> trip), else ignored.</param>
    /// <param name="stopUnder">The line's <c>stop_under</c>.</param>
    /// <param name="used">What she used of it since the last visit.</param>
    /// <param name="minutes">The minutes since the last visit; 0 or less: unmeasured.</param>
    /// <param name="why">The reading, in words.</param>
    public static bool Holds(LogisticsCause cause, double botLeast, double tripUnder, int held, int stopUnder, int used, double minutes, out string why)
    {
        var c = CultureInfo.InvariantCulture;
        if (cause is not (LogisticsCause.StockLow or LogisticsCause.Learn or LogisticsCause.Sale or LogisticsCause.Foci or LogisticsCause.Cadence or LogisticsCause.Slots))
        {
            why = string.Create(c, $"a {cause} trip never waits for the bot's hour");
            return false;
        }

        if (double.IsNaN(botLeast) || double.IsInfinity(botLeast) || botLeast < tripUnder)
        {
            why = "the bot's trip is due now (or its lines are unread): the trip goes, and is the bot's";
            return false;
        }

        var toBot = (botLeast - tripUnder) / 60.0;
        if (cause != LogisticsCause.StockLow)
        {
            why = string.Create(c, $"the bot's trip is due in {toBot:0.0} min and serves it");
            return true;
        }

        if (minutes <= 0)
        {
            why = "no visit to measure her use from: the trip goes as before";
            return false;
        }

        var lasts = used <= 0 ? double.PositiveInfinity : Math.Max(0, held - stopUnder) * minutes / used;
        if (lasts < toBot)
        {
            why = string.Create(c, $"it lasts {lasts:0.0} min to its stop line {stopUnder} at her use ({used} in {minutes:0.0} min), under the {toBot:0.0} min to the bot's trip: the trip goes");
            return false;
        }

        why = double.IsPositiveInfinity(lasts)
            ? string.Create(c, $"{held} held and none used since the last visit; the bot's trip is due in {toBot:0.0} min")
            : string.Create(c, $"it lasts {lasts:0.0} min to its stop line {stopUnder} at her use ({used} in {minutes:0.0} min); the bot's trip is due in {toBot:0.0} min");
        return true;
    }
}

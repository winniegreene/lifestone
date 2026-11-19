using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Mage;
using Xunit;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 audit (fix 2, the owner's decision of 2026-11-19: town trips once an hour, just in time for the bot's rebuff): <see cref="TripHourRule"/>. The numbers are run 4's at level 11 on the Burrow (2026-11-19):
/// at 04:25:50 UTC "Prismatic Taper: 48 held, under its trip line 60", 32 used in the 20.0 minutes since the visit of 04:03 (04:26:03 "pack budget: measured"), stop line 4 (tusker-burrow.json).
/// </summary>
public sealed class TripHourRuleTests
{
    private const double TripUnder = 420; // BuffBotRule.TripUnderSeconds

    [Fact]
    public void A_taper_line_that_lasts_past_the_bots_trip_waits_for_it()
    {
        // 44 tapers over the stop line at 1.6 a minute last 27.5 minutes; with 1,500 s on the bot's least line its trip is due in 18 minutes.
        Assert.True(TripHourRule.Holds(LogisticsCause.StockLow, 1_500, TripUnder, held: 48, stopUnder: 4, used: 32, minutes: 20, out var why));
        Assert.Contains("lasts 27.5 min to its stop line 4", why, StringComparison.Ordinal);
        Assert.Contains("the bot's trip is due in 18.0 min", why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_that_runs_down_before_the_bots_trip_goes_now()
    {
        // The same line with 3,000 s on the bot's least line: its trip is 43 minutes away, past the 27.5 the tapers last.
        Assert.False(TripHourRule.Holds(LogisticsCause.StockLow, 3_000, TripUnder, 48, 4, 32, 20, out var why));
        Assert.Contains("the trip goes", why, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LogisticsCause.Learn)]
    [InlineData(LogisticsCause.Sale)]
    [InlineData(LogisticsCause.Foci)]
    [InlineData(LogisticsCause.Cadence)]
    [InlineData(LogisticsCause.Slots)]
    public void A_scroll_a_focus_a_sale_or_slots_wait_for_the_bots_trip(LogisticsCause cause) =>
        Assert.True(TripHourRule.Holds(cause, 2_000, TripUnder, 0, 0, 0, 0, out _));

    [Theory]
    [InlineData(LogisticsCause.StockStop)]
    [InlineData(LogisticsCause.Burden)]
    [InlineData(LogisticsCause.FullPack)]
    [InlineData(LogisticsCause.Hour)]
    public void A_stop_line_the_burden_a_full_pack_and_the_hours_load_never_wait(LogisticsCause cause) =>
        Assert.False(TripHourRule.Holds(cause, 2_000, TripUnder, 0, 0, 0, 0, out _));

    [Fact]
    public void An_unmeasured_line_and_a_bot_trip_due_now_or_unread_never_wait()
    {
        Assert.False(TripHourRule.Holds(LogisticsCause.StockLow, 2_000, TripUnder, 48, 4, 32, 0, out var unmeasured));
        Assert.Contains("no visit to measure", unmeasured, StringComparison.Ordinal);
        Assert.False(TripHourRule.Holds(LogisticsCause.Sale, 400, TripUnder, 0, 0, 0, 0, out _)); // under the bot's trip line: the trip is the bot's
        Assert.False(TripHourRule.Holds(LogisticsCause.Sale, double.NaN, TripUnder, 0, 0, 0, 0, out _));
    }

    [Fact]
    public void A_line_she_did_not_use_since_the_last_visit_waits()
    {
        Assert.True(TripHourRule.Holds(LogisticsCause.StockLow, 2_000, TripUnder, 8, 0, 0, 15.7, out var why)); // run 2's eight Mana Potions, none drunk
        Assert.Contains("none used since the last visit", why, StringComparison.Ordinal);
    }
}

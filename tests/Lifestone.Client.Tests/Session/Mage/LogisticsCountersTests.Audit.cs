using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The leveling audit of 2026-11-19 (cache/m6/sprint/leveling-audit.md, finding "potion trips"): these tests PIN a decision the audit
/// reports as a fault, so a fix flips their assertions. The owner's standing rule is no potions (the Revitalize then Stamina to Mana
/// loop), yet every tusker ground's stock keeps 100 Mana Potions (trip line 10) and 30 Potions of Healing (trip line 6), at 230 pyreals
/// and 15 burden each (world SELECT on weenies 377 and 379: Value 170, burden 15; Thelnoth's 1.35: 2,525 for 11 in run 2 at 00:52:12 UTC).
/// Live, run 2 at level 11 on the Burrow (2026-11-19 01:09:22 UTC): "logistics: the counters say Trip: Mana Potion: 8 held, under its trip
/// line 10"; she left the ground, bought 5 Mana Potions for 1,148 pyreals and was back in the cycle at 01:17:13 (7 m 51 s).
/// The Burrow's trip line of 10 came back on 2026-11-11 (d667dd10, "on the Burrow and the Lodge a trip is due under ten Mana Potions"),
/// before the owner's no-potions rule of 2026-11-14/15; <see cref="PotionTripGround"/>'s summary still reads "the profile's line no longer
/// calls a trip".
/// </summary>
public sealed partial class LogisticsCountersTests
{
    private const uint PotionOfHealing = 377;
    private const int RunTwoPurseAtTheTrip = 54_009; // "trip: the verdict stands at home: stock:Mana Potion (... purse 54009: coin 54009, notes 0)"

    [Fact]
    public void Audit_the_Burrow_keeps_a_hundred_Mana_Potions_and_thirty_Potions_of_Healing_on_trip_lines()
    {
        var stock = Ground(Burrow).Supplies.Stock;
        var mana = stock.Single(l => l.Wcid == ManaPotion);
        var heal = stock.Single(l => l.Wcid == PotionOfHealing);

        Assert.Equal((100, 10), (mana.Keep, mana.TripUnder)); // a fix: no potion line, or keep 0 / trip 0
        Assert.Equal((30, 6), (heal.Keep, heal.TripUnder));
        Assert.Equal(1_950, (mana.Keep + heal.Keep) * 15); // the burden at the keeps, on a capacity of 7,500 to 7,950 at level 10 (run 4 03:36:49 and 03:47:21 UTC)
    }

    [Fact]
    public void Audit_eight_Mana_Potions_on_the_Burrow_send_her_home_as_run_2_did_at_level_11()
    {
        var ground = Ground(Burrow);
        var pack = new Pack(With(ground, ManaPotion, 8), burden: 100);
        pack.R.PrivateInt(PlayerQualities.CoinValueProperty, RunTwoPurseAtTheTrip);
        var counters = new LogisticsCounters(ground, learnHolds: new LearnHolds(), componentNeeds: new ComponentNeeds());

        var verdict = counters.Check(pack.State);

        Assert.Equal((LogisticsKind.Trip, "stock:Mana Potion"), (verdict.Kind, verdict.Trigger)); // a fix: None (no potion is a reason to leave the ground)
        Assert.Equal(55, LogisticsCounters.ClearOf(counters.LineAt(IndexOf(counters, ManaPotion)))); // the line's clear mark, as the live log read it
    }
}

using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Mage;
using Xunit;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 14 T (the owner, 2026-11-11: carry health drinks): the health drinks' table, the pick (none that would overflow, the biggest the missing health takes), the emergency's call (a drink when the mana pays no heal or the next hit kills, else
/// the heal first), the pack's read, the keeper's use of the same pick, and the stock line the tusker grounds carry.
/// </summary>
public sealed class HealthDrinksTests
{
    private const uint Philtre = 27318, Tonic = 27320, Elixir = 2458, Tincture = HealthDrinks.TinctureWcid, Potion = FighterKit.PotionWcid, Draught = VitalsMeans.HealthDraughtWcid;

    [Fact]
    public void The_table_is_the_world_databases_six_health_drinks_biggest_first()
    {
        // weenie_properties_int type 89 (BoosterEnum) = 2 and type 90 (BoostValue), a SELECT on the world copy, 2026-11-11.
        Assert.Equal([(Philtre, 100u), (Tonic, 85u), (Elixir, 65u), (Tincture, 50u), (Potion, 25u), (Draught, 10u)], HealthDrinks.All.Select(d => (d.Wcid, d.Boost)));
        Assert.Equal(["Health Philtre", "Health Tonic", "Health Elixir", "Health Tincture", "Potion of Healing", "Health Draught"], HealthDrinks.All.Select(d => d.Name));
        Assert.Equal(3, HealthDrinks.IndexOf(Tincture));
        Assert.Equal(-1, HealthDrinks.IndexOf(FighterKit.ManaPotionWcid));
        Assert.Equal(-1, HealthDrinks.IndexOf(27322)); // the Mana Tincture
    }

    [Theory]
    [InlineData(5, 5, 5, 60u, 3)] // 60 missing takes the tincture: the bigger drink
    [InlineData(5, 5, 5, 50u, 3)] // exactly its boost: it fits whole
    [InlineData(5, 5, 5, 49u, 4)] // a tincture would overflow by one: the Potion of Healing
    [InlineData(5, 5, 5, 25u, 4)]
    [InlineData(5, 5, 5, 24u, 5)] // the Health Draught
    [InlineData(5, 5, 5, 10u, 5)]
    [InlineData(5, 5, 5, 9u, -1)] // nothing fits: none is drunk
    [InlineData(5, 0, 0, 49u, -1)] // tinctures alone at 49 missing: none, never an overflow
    [InlineData(5, 0, 3, 30u, 5)] // no potion held: the draught, not the tincture
    [InlineData(0, 0, 0, 100u, -1)] // none held
    public void The_pick_is_the_biggest_drink_held_that_fits_whole_in_the_missing_health(int tinctures, int potions, int draughts, uint missing, int expected)
    {
        Assert.Equal(expected, HealthDrinks.Fitting([0, 0, 0, tinctures, potions, draughts], missing));
    }

    [Fact]
    public void A_bigger_drink_than_a_tincture_is_taken_when_the_missing_health_takes_it()
    {
        Assert.Equal(0, HealthDrinks.Fitting([1, 1, 1, 1, 1, 1], 100));
        Assert.Equal(1, HealthDrinks.Fitting([1, 1, 1, 1, 1, 1], 99));
        Assert.Equal(2, HealthDrinks.Fitting([1, 1, 1, 1, 1, 1], 84));
        Assert.Equal(3, HealthDrinks.Fitting([1, 1, 1, 1, 1, 1], 64));
    }

    [Theory]
    [InlineData(0, false, false, true, HealthDrinkCall.NoneHeld, false)]
    [InlineData(3, false, false, true, HealthDrinkCall.NoneFits, false)] // never an overflow, even when the next hit kills
    [InlineData(3, true, false, false, HealthDrinkCall.NoHealPaid, true)] // the mana is gone: the drink is the heal
    [InlineData(3, true, true, true, HealthDrinkCall.NextHitKills, true)] // a drink neither fizzles nor is resisted
    [InlineData(3, true, false, true, HealthDrinkCall.NextHitKills, true)]
    [InlineData(3, true, true, false, HealthDrinkCall.HealFirst, false)] // the reserve is kept
    public void The_emergency_drinks_when_the_mana_pays_no_heal_or_the_next_hit_kills_and_keeps_the_drinks_otherwise(int held, bool fits, bool healPaid, bool nextHitKills, HealthDrinkCall expected, bool drinks)
    {
        var call = HealthDrinks.Emergency(held, fits, healPaid, nextHitKills);
        Assert.Equal(expected, call);
        Assert.Equal(drinks, HealthDrinks.Drinks(call));
        Assert.False(string.IsNullOrEmpty(HealthDrinks.Words(call)));
    }

    [Fact]
    public void The_packs_read_counts_every_health_drink_and_picks_the_first_stack_of_the_one_that_fits()
    {
        var r = new FightManaTests.Rig(mana: 100, stamina: 100, manaPotions: 4, healthDrinks: [(Tincture, 2), (Potion, 3), (Draught, 4)]);
        var scratch = new List<uint>();

        Assert.True(HealthDrinks.Find(r.R.H.State, scratch, 30, out var guid, out var drink, out var held, out var heldAll));
        Assert.Equal((ScriptedCaster.HealthDrinkGuid + 1, Potion, 3, 9), (guid, drink.Wcid, held, heldAll)); // the Mana Potions are no health drink

        Assert.True(HealthDrinks.Find(r.R.H.State, scratch, 120, out guid, out drink, out held, out heldAll));
        Assert.Equal((ScriptedCaster.HealthDrinkGuid, Tincture, 2, 9), (guid, drink.Wcid, held, heldAll));

        Assert.False(HealthDrinks.Find(r.R.H.State, scratch, 5, out guid, out _, out held, out heldAll));
        Assert.Equal((0u, 0, 9), (guid, held, heldAll)); // held, but every one would overflow
    }

    [Fact]
    public void The_keeper_drinks_Health_Tinctures_as_it_drinks_Potions_of_Healing_and_never_one_that_would_overflow()
    {
        var rig = new C2VitalsRig(new C2Character { Potions = 5, PotionWcid = Tincture, Health = 110 });

        rig.Keeper.NeedsFor(new VitalsNeeds());
        rig.RunToEnd();

        Assert.Equal(["potion"], rig.Used); // 110 -> 160: 40 are missing and a second tincture would overflow by 10, so none is drunk though the until share is 180
        Assert.Equal((160u, 4u), (rig.Health, rig.Potions));
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("(Health Tincture, +50, 5 held)", StringComparison.Ordinal));
    }

    [Fact]
    public void The_keepers_Potion_of_Healing_keeps_the_M4_rule_when_nothing_fits_and_a_Health_Draught_is_taken_first_when_it_does()
    {
        // 185 of 200 with a station's minimum of 95%: 15 are missing, a Potion of Healing would overflow by 10 and M4's rule drinks it all the same (RecoveryRules.PotionMinMissing), as VitalsKeeperTests.A_station_s_minimum_raises_the_health_rule pins.
        var potion = new C2VitalsRig(new C2Character { Potions = 5, Health = 185 });
        Assert.False(potion.Keeper.NeedsFor(new VitalsNeeds(MinHealthPercent: 95)));
        Assert.Equal(VitalsKeeperState.Ready, potion.RunToEnd());
        Assert.Equal(["potion"], potion.Used);
        Assert.Equal(200u, potion.Health);

        // The same health with Health Draughts: one fits whole (10 of the 15 missing), so it is the pick and nothing overflows.
        var draught = new C2VitalsRig(new C2Character { Potions = 5, PotionWcid = Draught, Health = 185 });
        Assert.False(draught.Keeper.NeedsFor(new VitalsNeeds(MinHealthPercent: 95)));
        draught.RunToEnd();
        Assert.Equal("potion", draught.Used[0]);
        Assert.Contains(draught.R.H.Log.Lines, l => l.Contains("(Health Draught, +10, 5 held)", StringComparison.Ordinal));
        Assert.Equal(195u, draught.Health); // 185 -> 195: over the 95% the station asked for, and the last 5 are left to regeneration
    }

    [Fact]
    public void The_keeper_drinks_tinctures_to_the_full_bar_when_each_fits()
    {
        var rig = new C2VitalsRig(new C2Character { Potions = 5, PotionWcid = Tincture, Health = 100 });

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.Equal(["potion", "potion"], rig.Used); // 100 -> 150 -> 200
        Assert.Equal((200u, 3u), (rig.Health, rig.Potions));
    }

    [Theory]
    [InlineData("tusker-burrow")]
    [InlineData("tusker-lodge")]
    [InlineData("tusker-cave")]
    [InlineData("tusker-temple-20-40")]
    public void The_tusker_grounds_from_level_16_carry_Potions_of_Healing_bought_at_Thelnoth_before_the_Mana_Potions(string key)
    {
        var ground = GroundCatalog.Get(key);
        var line = ground.Supplies.StockOf(Potion)!;
        Assert.Equal((30, 6, 0), (line.Keep, line.TripUnder, line.StopUnder)); // two to three stands of 8 to 12 drinks; a trip under half a stand; never a stop
        Assert.Equal((715u, 230), (ground.VendorSelling(Potion)!.Wcid, ground.PriceOf(Potion)!.Value)); // Thelnoth Cort the Healer: max(1, ceil(1.35 x 170 - 0.1))
        Assert.False(ground.IsCarried(Potion));
        var stock = ground.Supplies.Stock.Select(l => l.Wcid).ToList();
        Assert.True(stock.IndexOf(Potion) < stock.IndexOf(FighterKit.ManaPotionWcid), "the health drinks are filled before the Mana Potions when the trip budget is short");
        Assert.Null(ground.Supplies.StockOf(Tincture)); // no vendor of Holtburg sells it: TODO(m6-health-tincture-trip)
        Assert.NotNull(HealthDrinks.All.Cast<HealthDrink?>().FirstOrDefault(d => d!.Value.Wcid == line.Wcid)); // the fight and the keeper drink what the line stocks
    }
}

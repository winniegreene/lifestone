using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Mage;
using Xunit;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 14 T (the owner, 2026-11-11: Mana Tinctures too): the mana drinks' table and the one pick every mana drink goes through (<see cref="VitalsMeans.FindManaPotions"/>): the biggest drink held that fits whole in the missing mana, a
/// Mana Tincture when 50 or more is missing, a Mana Potion when 25 to 49 is, never an assumed 25; and the fight's mana order drinking the tincture first.
/// </summary>
public sealed class ManaDrinksTests
{
    private const uint Tincture = ManaDrinks.TinctureWcid, Potion = FighterKit.ManaPotionWcid, Draught = VitalsMeans.ManaDraughtWcid;

    private static uint MaxMana(FightManaTests.Rig r) => r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out _, out var max) ? max : 0;

    [Fact]
    public void The_table_is_the_world_databases_three_mana_drinks_biggest_first()
    {
        // weenie_properties_int type 89 (BoosterEnum) = 6 and type 90 (BoostValue), a SELECT on the world copy, 2026-11-11.
        Assert.Equal([(Tincture, "Mana Tincture", 50u), (Potion, "Mana Potion", 25u), (Draught, "Mana Draught", 10u)], ManaDrinks.All.Select(d => (d.Wcid, d.Name, d.Boost)));
        Assert.Equal(27322u, Tincture);
        Assert.Equal(-1, ManaDrinks.IndexOf(HealthDrinks.TinctureWcid));
        Assert.Equal(-1, ManaDrinks.IndexOf(27326)); // the Stamina Tincture: left out entirely
    }

    [Theory]
    [InlineData(5, 5, 5, 200u, 0)] // a tincture while 50 or more is missing
    [InlineData(5, 5, 5, 50u, 0)]
    [InlineData(5, 5, 5, 49u, 1)] // a Mana Potion when 25 to 49 is missing
    [InlineData(5, 5, 5, 25u, 1)]
    [InlineData(5, 5, 5, 24u, 2)]
    [InlineData(5, 5, 0, 24u, -1)] // nothing fits
    [InlineData(5, 0, 0, 49u, -1)] // tinctures alone at 49 missing: none, never an overflow
    [InlineData(0, 5, 0, 200u, 1)] // no tincture held: the potion, the fallback
    [InlineData(0, 0, 0, 200u, -1)]
    public void The_pick_is_the_biggest_mana_drink_held_that_fits_whole_in_the_missing_mana(int tinctures, int potions, int draughts, uint missing, int expected)
    {
        Assert.Equal(expected, ManaDrinks.Fitting([tinctures, potions, draughts], missing));
    }

    [Fact]
    public void The_smallest_held_is_the_fallback_of_a_caller_that_drinks_whatever_the_fit()
    {
        Assert.Equal(1, ManaDrinks.Smallest([3, 2, 0]));
        Assert.Equal(0, ManaDrinks.Smallest([3, 0, 0]));
        Assert.Equal(-1, ManaDrinks.Smallest([0, 0, 0]));
    }

    [Fact]
    public void Every_caller_s_finder_is_the_pick_a_tincture_while_50_is_missing_a_potion_from_25_and_the_count_is_every_mana_drink()
    {
        var scratch = new List<uint>();
        var low = new FightManaTests.Rig(mana: 100, stamina: 100, manaPotions: 4, manaTinctures: 3);
        Assert.True(VitalsMeans.FindManaPotions(low.R.H.State, scratch, out var guid, out var count));
        Assert.Equal((ScriptedCaster.ManaTinctureGuid, 4), (guid, count)); // over 300 missing: the tincture; 3 tinctures and the rig's one potion stack, which carries no stack size and counts as one
        Assert.True(ManaDrinks.Fits(low.R.H.State, scratch));

        var near = new FightManaTests.Rig(mana: 396, stamina: 100, manaPotions: 4, manaTinctures: 3); // the rig's mana bar is 426: 30 missing, a tincture would overflow, a potion fits
        Assert.Equal(426u, MaxMana(near));
        Assert.True(ManaDrinks.Find(near.R.H.State, scratch, out guid, out var drink, out var fits, out count));
        Assert.Equal((ScriptedCaster.ManaPotionGuid, Potion, true, 4), (guid, drink.Wcid, fits, count));

        var full = new FightManaTests.Rig(mana: 396, stamina: 100, manaTinctures: 3); // tinctures alone at 30 missing
        Assert.True(ManaDrinks.Find(full.R.H.State, scratch, out guid, out drink, out fits, out _));
        Assert.Equal((ScriptedCaster.ManaTinctureGuid, false), (guid, fits)); // found (the smallest held), and said not to fit: the pull rest, which never overflows, does not drink it
        Assert.False(ManaDrinks.Fits(full.R.H.State, scratch));

        var none = new FightManaTests.Rig(mana: 0, stamina: 100);
        Assert.False(VitalsMeans.FindManaPotions(none.R.H.State, scratch, out guid, out count));
        Assert.Equal((0u, 0), (guid, count));
    }

    [Fact]
    public void The_fights_mana_order_drinks_the_Mana_Tincture_first_and_the_Mana_Potions_after_it()
    {
        var r = new FightManaTests.Rig(mana: 0, stamina: 200, manaPotions: 5, knowsDance: false, manaTinctures: 1) { HitsToKill = 400 };
        r.Caster.ManaRegenPerSecond = 0;
        r.AddTusker(40);
        var scenario = r.Scenario();

        r.Run(scenario, 60, 0.1, () => r.Caster.ManaTincturesDrunk.Count >= 1 && r.Caster.PotionsDrunk >= 1);

        Assert.Equal([50u], r.Caster.ManaTincturesDrunk); // the first mana drink of the fight: +50, not an assumed 25
        Assert.True(r.Caster.PotionsDrunk >= 1, "with the tincture gone the Mana Potions are the drink");
        var lines = r.Log.Lines.ToList();
        var firstTincture = lines.FindIndex(l => l.Contains($"Drink item=0x{ScriptedCaster.ManaTinctureGuid:X8}: restored 50 Mana", StringComparison.Ordinal));
        var firstPotion = lines.FindIndex(l => l.Contains($"Drink item=0x{ScriptedCaster.ManaPotionGuid:X8}: restored 25 Mana", StringComparison.Ordinal));
        Assert.True(firstTincture >= 0 && firstPotion > firstTincture, $"the tincture's drink line {firstTincture} comes before the first potion's {firstPotion}");
    }
}

using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 undrainable fills (2026-11-20, the Tusker Armory at level 50; docs/session.md section 119, "Undrainable sources"): the vitals policy prices every drain at its source's drain resistance, the share of the take ACE lets through
/// (<c>WorldObject_Magic.cs</c> 719 @95ef426d), and a kind that lets no stamina drain through is no source. An Armored Tusker's <c>ResistHealthDrain</c>, <c>ResistStaminaDrain</c> and <c>ResistManaDrain</c> are 0 (world DB
/// weenie_properties_float 125, 72 and 74 of weenies 11540 and 22611); a Tusker Guard's are 1 (1629). Before the fix the simulator played about 4.8 Drain Stamina Other a fight that each gave nothing against six Armored Tuskers
/// (<c>fight-sim matchup --her Qwinnie --pack 11540x6 --trace</c>, her level-50 save). What these prove: a level whose source lets nothing through is never offered; a partial share is priced at that share, as ACE's transfer
/// gives it; the mana floor's stamina drain is not chosen at an undrainable source; the behaviour table gives the Armored Tusker's 0 and the Tusker Guard's 1.
/// </summary>
public sealed class VitalsPolicyUndrainableTests
{
    private static readonly LadderVitalsPolicy Rules = LadderLoader.WarMage.VitalsPolicy;

    // Drain Health Other I (spell row 1237: proportion 0.25, loss -1, cap 60), as VitalsPolicySpendTests uses it.
    private static readonly TransferRow DrainOne = new(1237, 2, 2, 0.25f, -1f, 60);

    [Fact]
    public void A_drain_whose_source_lets_nothing_through_is_never_offered_and_a_partial_share_is_priced_at_that_share()
    {
        FillLevel[] none = [new(0, 1, 10, 38, 1.55, DrainOne, DrainMod: 0)];
        Assert.Equal(-1, VitalsPolicy.BestTransfer(none, 400, uint.MaxValue, 0, 0, 0, health: true).Index);

        FillLevel[] whole = [new(0, 1, 10, 38, 1.55, DrainOne)];
        FillLevel[] half = [new(0, 1, 10, 38, 1.55, DrainOne, DrainMod: 0.5)];
        var full = VitalsPolicy.BestTransfer(whole, 100, uint.MaxValue, 0, 0, 0, health: true);
        var share = VitalsPolicy.BestTransfer(half, 100, uint.MaxValue, 0, 0, 0, health: true);

        BuffStatMods.Transfer(in DrainOne, 100, uint.MaxValue, out var takenFull, out var givenFull);
        BuffStatMods.Transfer(in DrainOne, 100, uint.MaxValue, out var takenHalf, out var givenHalf, 0.5f);
        Assert.Equal((double)givenFull, full.Give, 9);
        Assert.Equal((double)givenHalf, share.Give, 9);
        Assert.Equal((double)takenHalf, share.Taken, 9);
        Assert.True(givenHalf < givenFull, $"half {givenHalf} full {givenFull}");
        Assert.True(share.Price > full.Price, $"half {share.Price} full {full.Price}");
    }

    [Fact]
    public void Under_the_mana_floor_the_stamina_drain_is_not_cast_at_a_source_that_lets_nothing_through()
    {
        // Level 10 at 30 mana (under its floor of 35) and no stamina: no press can give, so the floor reaches for the stamina drain. At a drainable source it is cast; at an Armored Tusker's 0 it never is.
        var drainable = Facts(drainMod: 1);
        Assert.Equal(VitalsMove.DrainStamina, VitalsPolicy.Decide(in drainable, Rules).Choice.Move);

        var undrainable = Facts(drainMod: 0);
        Assert.NotEqual(VitalsMove.DrainStamina, VitalsPolicy.Decide(in undrainable, Rules).Choice.Move);
    }

    [Fact]
    public void The_behaviour_table_gives_an_armored_tuskers_stamina_drain_resistance_as_0_and_a_guards_as_1()
    {
        Assert.Equal(0.0, DrainResist.StaminaOf(11540));
        Assert.Equal(0.0, DrainResist.StaminaOf(22611));
        Assert.Equal(1.0, DrainResist.StaminaOf(1629));
    }

    /// <summary>VitalsPolicySpendTests' level-10 facts (run 5's bars, no Mana Conversion) at 30 mana and no stamina, the stamina drain at <paramref name="drainMod"/>.</summary>
    private static VitalsPolicyFacts Facts(double drainMod) => new()
    {
        Health = 84,
        HealthMax = 84,
        Stamina = 0,
        StaminaMax = 154,
        Mana = 30,
        ManaMax = 161,
        ManaConversion = 0,
        HealthRegen = 0.05,
        StaminaRegen = 0.63,
        ManaRegen = 0.42,
        BiggestHit = 12,
        BoltLevel = 3,
        BoltBaseMana = 15,
        BoltPower = 100,
        BoltSeconds = 2.53,
        BoltMeanDamage = 55,
        BoltSpread = 30,
        BoltLandChance = 0.95,
        KillHealth = 100,
        KillStamina = 300,
        TargetHealth = 100,
        FullestHealth = 100,
        FullestStamina = 300,
        Presses = new FillLevel[] { new(0, 1, 10, 1, 1.48, BuffStatMods.StaminaToMana[0]) },
        StaminaDrains = new FillLevel[] { new(0, 2, 20, 50, 2.09, new TransferRow(1250, 4, 4, 0.1f, -1f, 60), DrainMod: drainMod) },
        HealthDrains = new FillLevel[] { new(0, 1, 10, 38, 1.55, DrainOne) },
    };
}

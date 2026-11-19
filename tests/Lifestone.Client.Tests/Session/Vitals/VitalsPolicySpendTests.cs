using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 heal spend (docs/session.md section 119, "The fight's fills: what pays"; the simulator's evidence in docs/fight-sim.md, "With her heals"): the vitals policy's fills cast only when they pay. What these prove: a health top-up waits for the next
/// kill while another creature stays on her, unless its damage pays for its seconds; a mana top-up that pays goes as before with many on her; the floors still fill at once with many
/// on her; a press pays only over the mana its seconds' incoming costs to heal; a stamina drain nets through the presses it feeds walked down the bar; a level she seldom lands is priced at its expected give and a fizzle's mana; the health
/// top-up waits for the best-priced level to fit; the log line names the attackers and the heal's price. The Abode facts are <see cref="VitalsPolicyTests"/>' (the live numbers of 2026-11-13 at level 40); the level-10 bar facts are run 5's
/// (<c>cache/m6/sprint/run5/step3-levelup.log</c>, 14:51 UTC: health 84, stamina 154, mana 161) with no Mana Conversion.
/// </summary>
public sealed class VitalsPolicySpendTests
{
    private static readonly LadderVitalsPolicy Rules = LadderLoader.WarMage.VitalsPolicy;

    private static readonly FillLevel[] Presses =
    [
        new(0, 1, 10, 1, Rules.SelfSeconds(1), BuffStatMods.StaminaToMana[0]),
        new(1, 2, 20, 50, Rules.SelfSeconds(2), BuffStatMods.StaminaToMana[1]),
        new(2, 3, 30, 100, Rules.SelfSeconds(3), BuffStatMods.StaminaToMana[2]),
        new(3, 4, 40, 150, Rules.SelfSeconds(4), BuffStatMods.StaminaToMana[3]),
        new(4, 5, 50, 200, Rules.SelfSeconds(5), BuffStatMods.StaminaToMana[4]),
    ];

    // Drain Health Other I to IV (spell rows 1237-1240: proportion 0.25, loss -1, -0.6, -0.33, 0, cap 60, 80, 100, 100).
    private static readonly FillLevel[] HealthDrains =
    [
        new(0, 1, 10, 38, Rules.OtherSeconds(1), new TransferRow(1237, 2, 2, 0.25f, -1f, 60)),
        new(1, 2, 20, 88, Rules.OtherSeconds(2), new TransferRow(1238, 2, 2, 0.25f, -0.6f, 80)),
        new(2, 3, 30, 138, Rules.OtherSeconds(3), new TransferRow(1239, 2, 2, 0.25f, -0.33f, 100)),
        new(3, 4, 40, 188, Rules.OtherSeconds(4), new TransferRow(1240, 2, 2, 0.25f, 0f, 100)),
    ];

    private static readonly FillLevel[] StaminaDrains = [new(0, 1, 10, 38, Rules.OtherSeconds(1), new TransferRow(1249, 4, 4, 0.1f, -1f, 60))];

    /// <summary>Level 40 in the Abode (<see cref="VitalsPolicyTests"/>' facts), with the attackers on her.</summary>
    private static VitalsPolicyFacts Abode(uint health = 156, uint stamina = 221, uint mana = 332, double burst = 0, int attackers = 0) => new()
    {
        Health = health,
        HealthMax = 156,
        Stamina = stamina,
        StaminaMax = 221,
        Mana = mana,
        ManaMax = 332,
        ManaConversion = 213,
        HealthRegen = VitalsPolicy.Regen(VitalsPolicy.HealthAtOne, VitalsPolicy.CombatStance, 2.15, 72, 94),
        StaminaRegen = VitalsPolicy.Regen(VitalsPolicy.StaminaAtOne, VitalsPolicy.CombatStance, 2.15),
        ManaRegen = VitalsPolicy.Regen(VitalsPolicy.ManaAtOne, 1, 2.15),
        SecondsPerKill = 34.1 * 60 / 98,
        AttacksPerSecond = 818 / 2046.0,
        DamagePerSecond = 2274 / 2046.0,
        BurstPerSecond = burst,
        BiggestHit = 10,
        BoltLevel = 5,
        BoltBaseMana = 25,
        BoltPower = 200,
        BoltSeconds = Rules.BoltSeconds(5),
        BoltMeanDamage = ((68 + 136) / 2.0) + (68 * (302 - 200) / 1000.0) + (VitalsPolicy.CritChance * 0.5 * 136),
        BoltSpread = 68,
        BoltLandChance = GearCasts.Chance(302, 200) * ManaConversionModel.SkillChance(302u, 171u),
        KillHealth = 250,
        KillStamina = 480,
        TargetHealth = 250,
        FullestHealth = 250,
        FullestStamina = 480,
        EmergencyLine = 94,
        Presses = Presses,
        StaminaDrains = StaminaDrains,
        HealthDrains = HealthDrains,
        Attackers = attackers,
    };

    /// <summary>
    /// Level 10's bars (run 5) with no Mana Conversion, at a Male Tusker (100 health): Stamina to Mana Self I only, Drain Stamina Other at base 20 (II's) giving the most a 63 bar misses; the bolt Force Bolt III (base 15, power 100,
    /// 2.53 s on the server: <c>fight-sim cast</c>), its landed mean 55 and land chance 0.95 as the simulator read them for Winnie (<c>matchup --pack "11x3,22510x2,236x1" --her Winnie --bars ...</c>).
    /// </summary>
    private static VitalsPolicyFacts LevelTen(uint stamina, uint staminaMax = 154, uint mana = 50, double burst = 0) => new()
    {
        Health = 84,
        HealthMax = 84,
        Stamina = stamina,
        StaminaMax = staminaMax,
        Mana = mana,
        ManaMax = 161,
        ManaConversion = 0,
        HealthRegen = 0.05,
        StaminaRegen = 0.63,
        ManaRegen = 0.42,
        BurstPerSecond = burst,
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
        StaminaDrains = new FillLevel[] { new(0, 2, 20, 50, 2.09, new TransferRow(1250, 4, 4, 0.1f, -1f, 60)) },
        HealthDrains = new FillLevel[] { new(0, 1, 10, 38, 1.55, new TransferRow(1237, 2, 2, 0.25f, -1f, 60)) },
    };

    [Fact]
    public void A_health_top_up_waits_for_the_next_kill_while_another_stays_on_her_and_goes_with_one()
    {
        // 86 missing: Drain Health Other III's 82 is the best-priced level and fits (VitalsPolicyTests). With one on her, as before; with three, the next kill takes a share out of the incoming and the same drain after it stands under less.
        var one = VitalsPolicy.Decide(Abode(health: 70, attackers: 1), Rules);
        Assert.Equal(VitalsMove.DrainHealth, one.Choice.Move);
        Assert.Equal(2, one.Choice.Index);

        var three = VitalsPolicy.Decide(Abode(health: 70, attackers: 3), Rules);
        Assert.Equal(VitalsMove.Bolt, three.Choice.Move);
        Assert.Equal(VitalsPolicy.HealthTopUpWaitsWhy, three.Choice.Why);

        var unread = VitalsPolicy.Decide(Abode(health: 70), Rules); // no attackers read: as before
        Assert.Equal(VitalsMove.DrainHealth, unread.Choice.Move);
    }

    [Fact]
    public void A_health_drain_whose_damage_pays_for_its_seconds_does_not_wait()
    {
        // A weak bolt (a mean of 20): a point of damage costs 3.53 / (20 x land) s, so III's 62 taken are worth more bolt seconds than its 3.47 s cast: it costs no kill time and goes with three on her.
        var f = Abode(health: 70, attackers: 3) with { BoltMeanDamage = 20, BoltSpread = 10 };
        var heal = VitalsPolicy.BestTransfer(f.HealthDrains.Span, 250, uint.MaxValue, VitalsPolicy.DamagePrice(in f), 0, f.ManaConversion, health: true);
        Assert.True(heal.KillSeconds <= 0, $"kill seconds {heal.KillSeconds}");

        var plan = VitalsPolicy.Decide(in f, Rules);
        Assert.Equal(VitalsMove.DrainHealth, plan.Choice.Move);
    }

    [Fact]
    public void A_mana_top_up_goes_as_before_with_many_on_her_when_it_pays()
    {
        // Mana 183: the press's whole give fits (VitalsPolicyTests). Three on her or one, the press goes: the mana top-up does not wait for the next kill (a wait while her mana covered the kills was measured and dropped,
        // docs/session.md section 119), it waits only when it does not pay (A_press_pays_only_over_the_mana_its_seconds_incoming_costs_to_heal).
        Assert.Equal(VitalsMove.Press, VitalsPolicy.Decide(Abode(mana: 183, attackers: 3), Rules).Choice.Move);
        Assert.Equal(VitalsMove.Press, VitalsPolicy.Decide(Abode(mana: 183, attackers: 1), Rules).Choice.Move);
    }
    [Fact]
    public void The_floors_fill_at_once_with_many_on_her()
    {
        var mana = VitalsPolicy.Decide(Abode(stamina: 60, mana: 40, attackers: 5), Rules);
        Assert.Equal(VitalsMove.Press, mana.Choice.Move);
        Assert.Equal("under the mana floor", mana.Choice.Why);

        var health = VitalsPolicy.Decide(Abode(health: 30, burst: 6.7, attackers: 5), Rules);
        Assert.Equal(VitalsMove.DrainHealth, health.Choice.Move);
        Assert.Equal("under the health floor", health.Choice.Why);
    }

    [Fact]
    public void A_press_pays_only_over_the_mana_its_seconds_incoming_costs_to_heal()
    {
        // Level 10, no Mana Conversion, under the mana floor (15 + 10 + 10 = 35) at 30 mana, a full bar of 154: Stamina to Mana Self I takes 50 (its cap) and gives 45 for 10.
        var calm = LevelTen(stamina: 154, mana: 30);
        var plan = VitalsPolicy.Decide(in calm, Rules);
        Assert.Equal(VitalsMove.Press, plan.Choice.Move);
        var press = VitalsPolicy.BestTransfer(calm.Presses.Span, 154, uint.MaxValue, 0, plan.Prices.Stamina, 0, health: false, mana: true);
        Assert.Equal(45, press.Give);
        Assert.Equal(10, press.Cost, 9);

        // The incoming at which its 1.48 s cost more health, healed at Drain Health Other I's 10 mana for 50 (25 of a Male Tusker's 100, doubled by its loss of -1), than its net 35: no press pays, and her stamina is full, so no
        // stamina drain is offered: the mana order decides.
        var heal = plan.Prices.HealMana;
        Assert.Equal(10 / 50.0, heal, 9);
        var burst = (35 / (1.48 * heal)) + 1;
        var hot = LevelTen(stamina: 154, mana: 30, burst: burst);
        Assert.False(VitalsPolicy.PressPays(in hot, VitalsPolicy.Prices(in hot, VitalsPolicy.Budget(in hot, Rules)), in press));
        var hotPlan = VitalsPolicy.Decide(in hot, Rules);
        Assert.Equal(VitalsMove.Bolt, hotPlan.Choice.Move);
        Assert.Equal("under the mana floor and no fill pays: the mana order decides", hotPlan.Choice.Why);
    }

    [Fact]
    public void A_stamina_drain_nets_through_the_presses_it_feeds_walked_down_the_bar()
    {
        // Winnie's own bar of 63 at 15 (the simulator's trace at her own bars): a drain of 48 (what she misses) for 20 mana. ACE's transfer (BuffStatMods.Transfer): Stamina to Mana I from 63 takes 32 and gives 29, from 31 takes 16 and
        // gives 14, from 15 takes 8 and gives 7 (under its 10: it stops). So the drain buys 19 + 4 = 23 for 20: 3 mana, where a full bar's rate read 48 x 19 / 32 - 20 = 8.5.
        var f = LevelTen(stamina: 15, staminaMax: 63);
        var prices = VitalsPolicy.Prices(in f, VitalsPolicy.Budget(in f, Rules));
        BuffStatMods.Transfer(BuffStatMods.StaminaToMana[0], 63, uint.MaxValue, out var taken63, out var given63);
        BuffStatMods.Transfer(BuffStatMods.StaminaToMana[0], 31, uint.MaxValue, out var taken31, out var given31);
        Assert.Equal((32u, 29u, 16u, 14u), (taken63, given63, taken31, given31));

        Assert.Equal(3, VitalsPolicy.StaminaDrainNet(in f, in prices, 48, 20, 2.09), 9);
        Assert.True(VitalsPolicy.StaminaDrainNets(in f, 48, 20)); // the full bar's rate, which the upkeep's fill still reads

        // Under fire the 3 is gone: the drain's 2.09 s and the presses' 2.96 s at 4 a second coming in, healed at 10 mana for 50.
        var hot = LevelTen(stamina: 15, staminaMax: 63, burst: 4);
        var hotPrices = VitalsPolicy.Prices(in hot, VitalsPolicy.Budget(in hot, Rules));
        Assert.True(VitalsPolicy.StaminaDrainNet(in hot, in hotPrices, 48, 20, 2.09) < 0);
    }

    [Fact]
    public void A_level_she_seldom_lands_is_priced_at_its_expected_give_and_a_fizzles_mana()
    {
        // Two levels of the same row: A at base 10 and B at base 8, both sure: B is cheaper a point. At a cast chance of 0.14 (Winnie's Drain Health Other IV, the simulator's means line), B gives 0.14 of its give and costs
        // 0.14 x 8 + 0.86 x 5 (a fizzle's 5, Player_Magic.cs 566-572): A is cheaper.
        var row = new TransferRow(1237, 2, 2, 0.25f, -1f, 60);
        FillLevel[] sure = [new(0, 1, 10, 38, 1.55, row), new(1, 1, 8, 38, 1.55, row)];
        FillLevel[] seldom = [new(0, 1, 10, 38, 1.55, row), new(1, 1, 8, 38, 1.55, row, 0.14)];
        Assert.Equal(1, VitalsPolicy.BestTransfer(sure, 100, uint.MaxValue, 0.04, 0, 0, health: true, manaPrice: 0.1).Index);
        var pick = VitalsPolicy.BestTransfer(seldom, 100, uint.MaxValue, 0.04, 0, 0, health: true, manaPrice: 0.1);
        Assert.Equal(0, pick.Index);

        var b = VitalsPolicy.BestTransfer([seldom[1]], 100, uint.MaxValue, 0.04, 0, 0, health: true, manaPrice: 0.1);
        Assert.Equal((0.14 * 8) + (0.86 * ManaConversionModel.FizzleMana), b.Cost, 9);
        Assert.Equal(0.14 * b.Give, b.ExpectedGive, 9);
        Assert.Equal(-1, VitalsPolicy.BestTransfer([new FillLevel(0, 1, 10, 38, 1.55, row, 0)], 100, uint.MaxValue, 0, 0, 0, health: true).Index); // a chance of 0: not offered
    }

    [Fact]
    public void The_health_top_up_waits_for_the_best_priced_level_to_fit()
    {
        // 70 missing: Drain Health Other IV's 62 and I's 60 fit, but III (82) is the best-priced level a point; the drain waits until III fits rather than spend a dearer one now.
        var plan = VitalsPolicy.Decide(Abode(health: 86, attackers: 1), Rules);
        Assert.NotEqual(VitalsMove.DrainHealth, plan.Choice.Move);
        var at = VitalsPolicy.Decide(Abode(health: 74, attackers: 1), Rules); // 82 missing: III fits
        Assert.Equal(VitalsMove.DrainHealth, at.Choice.Move);
        Assert.Equal(2, at.Choice.Index);
    }

    [Fact]
    public void Every_decision_names_the_attackers_and_the_heals_price()
    {
        var f = Abode(health: 70, attackers: 3);
        var line = VitalsPolicy.Decide(in f, Rules).Describe(in f);
        Assert.Contains("attackers 3", line, StringComparison.Ordinal);
        Assert.Contains("heal mana a point", line, StringComparison.Ordinal);
        Assert.Contains(VitalsPolicy.HealthTopUpWaitsWhy, line, StringComparison.Ordinal);
        Assert.Contains("attackers unread", VitalsPolicy.Decide(Abode(), Rules).Describe(Abode()), StringComparison.Ordinal);
    }
}

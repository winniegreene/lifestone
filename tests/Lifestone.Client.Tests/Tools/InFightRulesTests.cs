using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.FightSim;
using FightMoveKind = Lifestone.FightSim.FightMoveKind; // the client's Mage namespace has a FightMoveKind and a PackVerdict of its own
using PackVerdict = Lifestone.FightSim.PackVerdict;

namespace Lifestone.Client.Tests.Tools;

/// <summary>
/// The fight simulator's in-fight policy (docs/fight-sim.md, "With her heals"): which cast she makes next, by the client's own rules called from <c>tools/fight-sim/InFightRules.cs</c> (compiled into this project too: plain C# with
/// no ACE type). Proved on made-up spells and creatures; the fights themselves need the ACE build and run through <c>tools\fight-sim.ps1 matchup</c>. The transfer rows are the pinned world SQL's (<see cref="DrainPicker.Rows"/>,
/// <see cref="BuffStatMods.StaminaToMana"/>); the heal seed is <see cref="MeasuredHeals.SelfSeed"/>.
/// </summary>
public sealed class InFightRulesTests
{
    private static readonly FightSpell Bolt = new(1, "Flame Bolt III", 3, 15, 100, 2.53, 0.99, default);
    private static readonly FightSpell DrainOne = new(1237, "Drain Health Other I", 1, 10, 1, 1.55, 1.0, DrainPicker.RowOf(1237)!.Value);
    private static readonly FightSpell HealOne = new(6, "Heal Self I", 1, 15, 1, 1.48, 1.0, default);
    private static readonly FightSpell PressOne = new(BuffStatMods.StaminaToMana[0].Id, "Stamina to Mana Self I", 1, 10, 1, 1.48, 1.0, BuffStatMods.StaminaToMana[0]);

    private static FoeKind Kind(string name, double health, double threat, double herDps, double drainResist = 1.0) =>
        new(name, health, 300, threat, Bolt, 70, 44, 0.95, herDps, 0.95, drainResist);

    private static FightView View(uint health, uint mana, IReadOnlyList<Foe> foes, uint stamina = 63, IReadOnlyList<FightSpell>? presses = null) => new()
    {
        Health = health,
        HealthMax = 100,
        Stamina = stamina,
        StaminaMax = 63,
        Mana = mana,
        ManaMax = 160,
        HealthRegen = 0.05,
        StaminaRegen = 0.63,
        ManaRegen = 0.42,
        HealSelf = HealOne,
        HealthDrains = [DrainOne],
        Presses = presses ?? [],
        Foes = foes,
    };

    [Fact]
    public void The_kill_order_takes_the_most_damage_removed_for_each_second_of_casting()
    {
        // A hits 2 a second and takes 10 s to kill (0.2 a second of casting); B hits 1 a second and takes 2.5 s (0.4): B first (TargetSwitch's rule, folded).
        var view = View(100, 160, [new Foe(Kind("A", 100, 2, 10)), new Foe(Kind("B", 50, 1, 20))]);

        Assert.Equal(1, InFightRules.ChooseTarget(view));
    }

    [Fact]
    public void The_kill_order_keeps_a_living_target_and_moves_on_when_it_falls()
    {
        var view = View(100, 160, [new Foe(Kind("A", 100, 2, 10)), new Foe(Kind("B", 50, 1, 20))]);
        view.Target = 0;

        Assert.Equal(0, InFightRules.ChooseTarget(view));
        view.Foes[0].Health = 0;
        Assert.Equal(1, InFightRules.ChooseTarget(view));
        view.Foes[1].Health = 0;
        view.Target = 1;
        Assert.Equal(-1, InFightRules.ChooseTarget(view));
    }

    [Fact]
    public void Bolts_only_bolts_whatever_her_bars()
    {
        var view = View(5, 0, [new Foe(Kind("A", 100, 2, 10))]);

        var move = InFightRules.Decide(view, FightMode.BoltsOnly);

        Assert.Equal(FightMoveKind.Bolt, move.Kind);
        Assert.Same(Bolt, move.Spell);
    }

    [Fact]
    public void Under_the_emergency_line_the_drain_on_the_fullest_creature_beats_heal_self_when_it_gives_more_a_second()
    {
        // Health 10 of 100 is under FightEmergency's line (its floor, 60%). Drain Health Other I on a creature at 200: 25% of 200 = 50, its loss -1 doubles it to 100, cut to the cap of 60 (BuffStatMods.Transfer), so 60 at
        // 0.95 in 1.55 s = 36.8 a second; Heal Self I's seed 17.5 in 1.48 s = 11.8 a second (HealPick.ChooseNow). The fullest by share is the creature at 200 of 200, not the one at 150 of 300.
        var view = View(10, 160, [new Foe(Kind("hurt", 300, 2, 10)) { Health = 150 }, new Foe(Kind("full", 200, 2, 10))]);

        var move = InFightRules.Decide(view, FightMode.Heals);

        Assert.Equal(FightMoveKind.DrainHealth, move.Kind);
        Assert.True(move.Emergency);
        Assert.Equal(1, move.Foe);
        Assert.Same(DrainOne, move.Spell);
    }

    [Fact]
    public void Under_the_emergency_line_heal_self_goes_when_no_creature_gives_a_drain_anything()
    {
        // A drain resistance of 0 lets nothing through (an Armored Tusker's ResistHealthDrain): no drain candidate, so Heal Self.
        var view = View(10, 160, [new Foe(Kind("armoured", 400, 2, 10, drainResist: 0))]);

        var move = InFightRules.Decide(view, FightMode.Heals);

        Assert.Equal(FightMoveKind.HealSelf, move.Kind);
        Assert.True(move.Emergency);
        Assert.Same(HealOne, move.Spell);
    }

    [Fact]
    public void The_emergency_holds_up_to_its_safe_line_and_lets_go_there()
    {
        var view = View(10, 160, [new Foe(Kind("full", 200, 2, 10))]);
        Assert.NotNull(InFightRules.Emergency(view));

        view.Health = 80; // over the floor's 60, under the safe line's 85: the hysteresis holds
        Assert.NotNull(InFightRules.Emergency(view));

        view.Health = 85; // at the safe line it ends
        Assert.Null(InFightRules.Emergency(view));
    }

    [Fact]
    public void With_no_bolt_and_no_press_her_mana_lets_her_try_she_is_out_of_mana()
    {
        // Full health (no heal is due); 3 mana is under the bolt's 15 (no Mana Conversion: the base is the cost); no press known.
        var view = View(100, 3, [new Foe(Kind("A", 100, 2, 10))]);

        var move = InFightRules.Decide(view, FightMode.Heals);

        Assert.Equal(FightMoveKind.OutOfMana, move.Kind);
        Assert.Null(move.Spell);
    }

    [Fact]
    public void Under_the_mana_floor_she_presses_before_she_bolts()
    {
        // 12 mana is under the vitals policy's mana floor (the bolt's 15 and the cheapest press's 10): the press goes at once (VitalsPolicy.Decide).
        var view = View(100, 12, [new Foe(Kind("A", 100, 2, 10))], presses: [PressOne]);

        var move = InFightRules.Decide(view, FightMode.Heals);

        Assert.Equal(FightMoveKind.Press, move.Kind);
        Assert.Same(PressOne, move.Spell);
    }

    [Fact]
    public void The_policys_facts_carry_the_creatures_on_her_and_each_fills_landing_chance()
    {
        // M6 heal spend: the fight's attackers (every living creature here), their health together, and each level's chance to land as the client's facts carry it (the cast chance, times a drain's source failing its resist).
        var drainSeldom = DrainOne with { CastChance = 0.5 };
        var view = new FightView
        {
            Health = 100, HealthMax = 100, Stamina = 63, StaminaMax = 63, Mana = 160, ManaMax = 160, HealthDrains = [drainSeldom], Presses = [PressOne with { CastChance = 0.9 }],
            Foes = [new Foe(Kind("A", 100, 2, 10)) { Health = 40 }, new Foe(Kind("B", 50, 1, 20)) { Health = 0 }, new Foe(Kind("C", 80, 1, 20))],
        };
        view.Target = InFightRules.ChooseTarget(view);

        var facts = InFightRules.Facts(view, out _, out _, out var fullest, out _);

        Assert.Equal(2, facts.Attackers);
        Assert.Equal(2, fullest); // C, full, over A at 40%
        Assert.Equal(0.5, facts.HealthDrains.Span[0].Chance, 9); // the cast chance
        Assert.Equal(0.95, facts.HealthDrains.Span[0].Lands, 9); // C's DrainLand (Kind's 0.95): the share past its resist
        Assert.Equal(0.9, facts.Presses.Span[0].Chance, 9);
    }

    [Fact]
    public void A_kind_that_lets_no_drain_through_is_no_source_and_each_drain_is_priced_at_its_sources_share()
    {
        // M6 undrainable fills: an Armored Tusker lets no health or stamina drain through (world DB float 125 and 72 of 11540: 0), so with only them on her the fills have no source; beside a creature that lets half through,
        // that one is the source and both drains are priced at its half (FightScenario.VitalsPolicy.cs TryPolicyFacts: DrainResistOf, DrainResist.StaminaOf; ManaLoop.cs FullestStamina's kind rule).
        var drainStamina = new FightSpell(1250, "Drain Stamina Other II", 2, 20, 50, 2.09, 1.0, new TransferRow(1250, 4, 4, 0.1f, -1f, 60));
        var armored = Kind("Armored Tusker", 435, 2, 10, drainResist: 0) with { StaminaDrainResist = 0 };
        var halfKind = Kind("Half", 400, 1, 10, drainResist: 0.5) with { StaminaDrainResist = 0.5 };
        FightView ViewOf(params Foe[] foes)
        {
            var view = new FightView { Health = 100, HealthMax = 100, Stamina = 63, StaminaMax = 63, Mana = 160, ManaMax = 160, HealthDrains = [DrainOne], StaminaDrain = drainStamina, Foes = foes };
            view.Target = InFightRules.ChooseTarget(view);
            return view;
        }

        var only = ViewOf(new Foe(armored), new Foe(armored));
        var none = InFightRules.Facts(only, out _, out _, out var healthSource, out var staminaSource);
        Assert.Equal((-1, -1), (healthSource, staminaSource));
        Assert.Equal(0u, none.FullestStamina);

        var mixed = ViewOf(new Foe(armored), new Foe(halfKind));
        var facts = InFightRules.Facts(mixed, out _, out _, out healthSource, out staminaSource);
        Assert.Equal((1, 1), (healthSource, staminaSource));
        Assert.Equal(0.5, facts.HealthDrains.Span[0].DrainMod, 9);
        Assert.Equal(0.5, facts.StaminaDrains.Span[0].DrainMod, 9);
    }

    [Fact]
    public void A_kill_is_priced_as_the_war_pick_prices_it()
    {
        var pick = new WarPickSettings();

        Assert.Equal(10 + (4 * pick.CastOverheadSeconds) + (60 * pick.DanceSecondsPerMana), InFightRules.KillPrice(10, 4, 60), 12);
        Assert.True(InFightRules.KillPrice(5.1, 2, 105) > InFightRules.KillPrice(5.3, 2.1, 31)); // a mana-hungry fast kill loses to a cheap one, as the streak did to the bolt on a Female Tusker
    }

    [Fact]
    public void The_verdict_sums_the_fights()
    {
        FightResult[] fights =
        [
            new(FightEnd.Won, 20, 30, 35, 100, 40, 2, 6, 1, 0, 1, 3),
            new(FightEnd.Won, 30, 10, 25, 120, 60, 4, 7, 2, 1, 2, 3),
            new(FightEnd.Died, 15, 0, 0, 80, 70, 1, 3, 0, 0, 0, 1),
            new(FightEnd.OutOfMana, 40, 20, 20, 200, 160, 3, 5, 4, 2, 0, 2),
        ];

        var v = PackVerdict.Of(fights);

        Assert.Equal(4, v.Fights);
        Assert.Equal(0.25, v.Dies);
        Assert.Equal(0.25, v.OutOfMana);
        Assert.Equal(0, v.Stalled);
        Assert.Equal(0.5, v.NotWon);
        Assert.Equal(15, v.LowestMean);
        Assert.Equal(0, v.LowestP10);
        Assert.Equal(2.5, v.HealsMean);
        Assert.Equal(25, v.ClearSeconds); // won fights only
        Assert.Equal(30, v.EndHealthWon);
        Assert.Equal(50, v.ManaNetWon);
        Assert.Equal(0.3, v.EmergencyShare, 12);
        Assert.Equal(40, v.OutOfManaSeconds);
        Assert.Equal(2, v.OutOfManaKills);
    }

    [Fact]
    public void A_mean_pack_is_taken_in_proportion_between_its_two_whole_sizes()
    {
        var three = PackVerdict.Of([new FightResult(FightEnd.Won, 10, 30, 30, 10, 10, 0, 3, 0, 0, 0, 3)]);
        var four = PackVerdict.Of([new FightResult(FightEnd.Died, 10, 0, 0, 10, 10, 0, 3, 0, 0, 0, 2), new FightResult(FightEnd.OutOfMana, 10, 5, 5, 10, 10, 0, 3, 0, 0, 0, 2)]);

        var (dies, outOfMana) = PackVerdict.Between(three, four, 3.25);

        Assert.Equal(0.125, dies, 12);
        Assert.Equal(0.125, outOfMana, 12);
    }
}

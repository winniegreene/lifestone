using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 16 E3.0 (docs/automation-engine.md E3; m6-plan.md D119): the survival layer's answer in a fight, pure (<see cref="SurvivalRule.DecideFight"/>): about to die, heal (her own spell or a drain, and on whom) or leave;
/// cannot finish these attackers on the mana she has, take mana by the best sum (the drain's target named) or leave; else go. Every line is an input: the ladder's (<see cref="LadderSurvival"/>) or the fight's own
/// (<see cref="SurvivalFight"/>). Nothing calls the answer to decide yet: the fight compares it with its own (<c>FightScenarioTests.SurvivalShadow.cs</c>).
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class SurvivalRuleFightTests
{
    private const uint Near = 0x80000739;
    private const uint Far = 0x8000073A;
    private static readonly LadderSurvival Lines = new(50, 80, 25, 60, 15, 40, 50, 15, 80, 50, 0); // the ladder's survival lines of 2026-11-17: wait 50/25/15, back 80/60/40, leave 50/15

    private static SurvivalAttacker Attacker(uint guid, double metres, float? share, uint? stamina, double? health) => new(guid, "Tusker Slave", metres, share, stamina) { Health = health };

    /// <summary>In a fight, every bar full, one attacker of 400 health, a bolt of 70 mana for 200: 140 mana finishes it and she holds 400.</summary>
    private static SurvivalInput Fighting(SurvivalFight? fight = null) => new()
    {
        InFight = true,
        HealthPercent = 100,
        ManaPercent = 100,
        StaminaPercent = 100,
        HostileNear = true,
        Attacked = true,
        MayLeave = true,
        Attackers = new[] { Attacker(Near, 3, 1f, 300, 400) },
        Fight = fight ?? Means(),
    };

    private static SurvivalFight Means() => new() { EmergencyLinePercent = 60, EmergencySafePercent = 85, SelfHealPayable = true, SelfHealPerSecond = 20, Mana = 400, BoltMana = 70, BoltDamage = 200 };

    [Fact]
    public void With_every_bar_over_its_line_and_the_mana_to_finish_them_she_keeps_killing()
    {
        var input = Fighting();
        var decided = SurvivalRule.Decide(in input, Lines);
        Assert.Equal((SurvivalVerdict.Go, SurvivalReason.None, SurvivalRule.FightGoes, 0u), (decided.Verdict, decided.Reason, decided.Why, decided.DrainTarget));
    }

    [Theory]
    [InlineData(45, 60, false, SurvivalRule.DieBothLines)] // under the ladder's wait line 50 and the fight's emergency line 60
    [InlineData(55, 60, false, SurvivalRule.DieFightLine)] // under the fight's emergency line alone
    [InlineData(45, 0, false, SurvivalRule.DieLadderLine)] // the fight has no emergency (off): the ladder's line alone
    [InlineData(45, 40, false, SurvivalRule.DieLadderLine)] // the fight's line is lower
    [InlineData(78, 60, true, SurvivalRule.DieBothLines)] // a heal run stands: on to the ladder's back line 80 and the fight's safe line 85
    [InlineData(82, 60, true, SurvivalRule.DieFightLine)] // over the ladder's back line, under the fight's safe line
    public void About_to_die_is_a_heal_and_the_answer_names_the_line_she_stands_under(double health, double emergency, bool held, string why)
    {
        var input = Fighting(Means() with { EmergencyLinePercent = emergency, EmergencySafePercent = emergency > 0 ? 85 : 0 }) with { HealthPercent = health, Held = held };
        var decided = SurvivalRule.Decide(in input, Lines);
        Assert.Equal((SurvivalVerdict.Heal, SurvivalReason.Heal, why, 0u), (decided.Verdict, decided.Reason, decided.Why, decided.DrainTarget));
    }

    [Theory]
    [InlineData(78, false)] // no run stands: the wait line 50 and the emergency line 60, not the back lines
    [InlineData(86, true)] // a run stands and she is over both back lines
    public void Over_the_lines_of_the_moment_there_is_no_heal(double health, bool held)
    {
        var input = Fighting() with { HealthPercent = health, Held = held };
        Assert.Equal(SurvivalVerdict.Go, SurvivalRule.Decide(in input, Lines).Verdict);
    }

    [Theory]
    [InlineData(true, 20, true, 30, Near)] // the drain gives more a second: the layer's pick is named
    [InlineData(true, 20, true, 20, 0u)] // a tie goes to her own spell
    [InlineData(true, 20, true, 10, 0u)]
    [InlineData(false, 0, true, 5, Near)] // her own spell is not payable: the drain
    [InlineData(true, 20, false, 0, 0u)]
    public void The_heal_is_her_own_spell_or_a_drain_on_the_layers_pick_by_what_each_gives_a_second(bool self, double selfRate, bool drain, double drainRate, uint target)
    {
        var input = Fighting(Means() with { SelfHealPayable = self, SelfHealPerSecond = selfRate, DrainPayable = drain, DrainPerSecond = drainRate, DrainTarget = Near }) with { HealthPercent = 40 };
        var decided = SurvivalRule.Decide(in input, Lines);
        Assert.Equal((SurvivalVerdict.Heal, target), (decided.Verdict, decided.DrainTarget));
    }

    [Fact]
    public void About_to_die_with_no_heal_payable_she_leaves_under_the_ladders_leave_lines_and_only_there()
    {
        var none = Means() with { SelfHealPayable = false, Mana = 5, BoltMana = 0 };
        var input = Fighting(none) with { HealthPercent = 40, ManaPercent = 10 };
        var decided = SurvivalRule.Decide(in input, Lines);
        Assert.Equal((SurvivalVerdict.Leave, SurvivalReason.NoMana, SurvivalRule.DieLeaves), (decided.Verdict, decided.Reason, decided.Why));

        var overHealth = input with { HealthPercent = 55 }; // under the fight's emergency line 60, over the ladder's leave line 50: not the leave of the dying; her mana is its own question, and at 10% with nothing to take it is the mana's leave
        Assert.Equal((SurvivalVerdict.Leave, SurvivalRule.ManaLeaves), (SurvivalRule.Decide(in overHealth, Lines).Verdict, SurvivalRule.Decide(in overHealth, Lines).Why));
        var overBoth = overHealth with { ManaPercent = 30 }; // over the ladder's leave line for mana and its wait line: no leave of either kind
        Assert.Equal((SurvivalVerdict.Go, SurvivalRule.DieGoesOn), (SurvivalRule.Decide(in overBoth, Lines).Verdict, SurvivalRule.Decide(in overBoth, Lines).Why));
        var noLeave = input with { MayLeave = false }; // no way out is armed
        Assert.NotEqual(SurvivalVerdict.Leave, SurvivalRule.Decide(in noLeave, Lines).Verdict);
        var quiet = input with { Attacked = false };
        Assert.NotEqual(SurvivalVerdict.Leave, SurvivalRule.Decide(in quiet, Lines).Verdict);
        var payable = input with { Fight = none with { DrainPayable = true, DrainTarget = Near, DrainPerSecond = 4 } };
        Assert.Equal((SurvivalVerdict.Heal, Near), (SurvivalRule.Decide(in payable, Lines).Verdict, SurvivalRule.Decide(in payable, Lines).DrainTarget));
    }

    [Fact]
    public void About_to_die_with_no_heal_and_no_leave_the_mana_is_asked_and_else_she_goes_on_by_name()
    {
        var none = Means() with { SelfHealPayable = false, Mana = 100, PressNet = 30 }; // 140 finishes them, she holds 100
        var input = Fighting(none) with { HealthPercent = 40, ManaPercent = 25, MayLeave = false };
        Assert.Equal((SurvivalVerdict.TakeMana, SurvivalRule.ManaPress), (SurvivalRule.Decide(in input, Lines).Verdict, SurvivalRule.Decide(in input, Lines).Why));

        var enough = input with { Fight = none with { Mana = 140 } };
        Assert.Equal((SurvivalVerdict.Go, SurvivalRule.DieGoesOn), (SurvivalRule.Decide(in enough, Lines).Verdict, SurvivalRule.Decide(in enough, Lines).Why));
    }

    [Theory]
    [InlineData(139u, 30, 0, 0u, SurvivalVerdict.TakeMana, SurvivalRule.ManaPress, 0u)] // 400 health at 200 a bolt is 2 bolts of 70: 140, one more than she holds
    [InlineData(140u, 30, 0, 0u, SurvivalVerdict.Go, SurvivalRule.FightGoes, 0u)]
    [InlineData(100u, 30, 45, Near, SurvivalVerdict.TakeMana, SurvivalRule.ManaDrain, Near)] // the drain and the press after it net more than the press
    [InlineData(100u, 30, 30, Near, SurvivalVerdict.TakeMana, SurvivalRule.ManaPress, 0u)] // a tie goes to the press
    [InlineData(100u, 0, 12, Near, SurvivalVerdict.TakeMana, SurvivalRule.ManaDrain, Near)]
    [InlineData(100u, 30, 45, 0u, SurvivalVerdict.TakeMana, SurvivalRule.ManaPress, 0u)] // no pick: no drain
    [InlineData(100u, -4, -10, Near, SurvivalVerdict.Go, SurvivalRule.ManaGoesOn, 0u)] // nothing pays, and her mana (25%) is over the ladder's leave line 15
    public void Short_of_the_mana_that_finishes_them_she_takes_mana_by_the_better_sum(uint mana, double press, double pair, uint staminaTarget, SurvivalVerdict verdict, string why, uint target)
    {
        var input = Fighting(Means() with { Mana = mana, PressNet = press, StaminaPairNet = pair, StaminaTarget = staminaTarget }) with { ManaPercent = 25 };
        var decided = SurvivalRule.Decide(in input, Lines);
        Assert.Equal((verdict, why, target), (decided.Verdict, decided.Why, decided.DrainTarget));
    }

    [Fact]
    public void Short_of_mana_with_nothing_to_take_she_leaves_under_the_ladders_leave_line_for_mana()
    {
        var input = Fighting(Means() with { Mana = 20 }) with { ManaPercent = 14 };
        var decided = SurvivalRule.Decide(in input, Lines);
        Assert.Equal((SurvivalVerdict.Leave, SurvivalReason.NoMana, SurvivalRule.ManaLeaves), (decided.Verdict, decided.Reason, decided.Why));

        var noLeave = input with { MayLeave = false };
        Assert.Equal((SurvivalVerdict.Go, SurvivalRule.ManaGoesOn), (SurvivalRule.Decide(in noLeave, Lines).Verdict, SurvivalRule.Decide(in noLeave, Lines).Why));
    }

    [Theory]
    [InlineData(24, SurvivalVerdict.TakeMana, SurvivalRule.ManaLinePress)] // under the ladder's wait line 25
    [InlineData(25, SurvivalVerdict.Go, SurvivalRule.FightGoes)]
    public void Where_the_mana_that_finishes_them_is_not_read_the_ladders_wait_line_stands_in(double manaPercent, SurvivalVerdict verdict, string why)
    {
        var input = Fighting(Means() with { Mana = 30, PressNet = 12 }) with { ManaPercent = manaPercent, Attackers = new[] { Attacker(Near, 3, null, null, null) } }; // its health is held by no source
        var decided = SurvivalRule.Decide(in input, Lines);
        Assert.Equal((verdict, why), (decided.Verdict, decided.Why));

        var drain = input with { Fight = input.Fight with { StaminaTarget = Near, StaminaPairNet = 40 } };
        if (verdict == SurvivalVerdict.TakeMana)
        {
            Assert.Equal((SurvivalRule.ManaLineDrain, Near), (SurvivalRule.Decide(in drain, Lines).Why, SurvivalRule.Decide(in drain, Lines).DrainTarget));
        }
    }

    [Fact]
    public void A_lost_race_the_caller_hands_over_is_a_leave_before_anything_else()
    {
        var input = Fighting() with { HealthPercent = 40, GuardRaceLost = true, MayLeaveRace = true };
        var decided = SurvivalRule.Decide(in input, Lines);
        Assert.Equal((SurvivalVerdict.Leave, SurvivalReason.LosingRace, SurvivalRule.FightRaceLost), (decided.Verdict, decided.Reason, decided.Why));

        var notHere = input with { MayLeaveRace = false };
        Assert.Equal(SurvivalVerdict.Heal, SurvivalRule.Decide(in notHere, Lines).Verdict);
    }

    [Fact]
    public void The_mana_that_finishes_them_is_whole_bolts_of_each_ones_known_health()
    {
        var two = new[] { Attacker(Near, 3, 0.25f, null, 100), Attacker(Far, 9, 1f, null, 250) };
        Assert.Equal((2 + 5) * 20.0, SurvivalRule.ManaToFinish(two, boltDamage: 60, boltMana: 20)); // 100 / 60 is 2 bolts, 250 / 60 is 5
        Assert.True(double.IsNaN(SurvivalRule.ManaToFinish(two, 0, 20))); // no bolt read
        Assert.True(double.IsNaN(SurvivalRule.ManaToFinish(two, 60, 0)));
        Assert.True(double.IsNaN(SurvivalRule.ManaToFinish([], 60, 20)));
        Assert.True(double.IsNaN(SurvivalRule.ManaToFinish(null, 60, 20)));
        Assert.True(double.IsNaN(SurvivalRule.ManaToFinish(new[] { two[0], Attacker(Far, 9, null, null, null) }, 60, 20))); // one unread: not said
    }

    [Fact]
    public void The_layers_drain_pick_is_the_one_fullest_pick_over_the_attackers_of_known_health_in_reach()
    {
        var list = new[]
        {
            Attacker(0x1, 4, 0.50f, null, 200),
            Attacker(0x2, 9, 0.995f, null, 398), // within a hundredth of the fullest: the nearer of the two wins
            Attacker(0x3, 12, 1f, null, 400),
            Attacker(0x4, 2, null, null, null), // unread: passed over, never read as full (D92)
            Attacker(0x5, 31, 1f, null, 400), // out of reach
        };
        Assert.Equal(0x2u, SurvivalRule.FullestHealth(list, reachMetres: 30));
        Assert.Equal(0x1u, SurvivalRule.FullestHealth(list, reachMetres: 5));
        Assert.Equal(0u, SurvivalRule.FullestHealth(list, reachMetres: 1));
        Assert.Equal(0u, SurvivalRule.FullestHealth(null, 30));
    }

    [Fact]
    public void The_layers_stamina_pick_is_the_one_fullest_pick_over_the_attackers_an_appraisal_told()
    {
        var list = new[]
        {
            Attacker(0x1, 4, 1f, 0, 400), // none: never picked
            Attacker(0x2, 9, 1f, 290, 400),
            Attacker(0x3, 3, 1f, 290, 400), // an equal: the first met stays
            Attacker(0x4, 2, 1f, null, 400), // unread
            Attacker(0x5, 31, 1f, 600, 400), // out of reach
        };
        Assert.Equal(0x2u, SurvivalRule.FullestStamina(list, reachMetres: 30));
        Assert.Equal(0u, SurvivalRule.FullestStamina(list, reachMetres: 2.5));
        Assert.Equal(0x5u, SurvivalRule.FullestStamina(list, reachMetres: 40));
    }

    [Fact]
    public void The_answer_in_a_fight_allocates_nothing()
    {
        var heal = Fighting(Means() with { DrainPayable = true, DrainTarget = Near, DrainPerSecond = 30 }) with { HealthPercent = 40 };
        var mana = Fighting(Means() with { Mana = 100, PressNet = 30, StaminaPairNet = 45, StaminaTarget = Near }) with { ManaPercent = 25 };
        var go = Fighting();
        var list = heal.Attackers;
        var seen = 0L;
        AllocationAssert.Nothing(() =>
        {
            seen += (int)SurvivalRule.Decide(in heal, Lines).Verdict + (int)SurvivalRule.Decide(in mana, Lines).Verdict + (int)SurvivalRule.Decide(in go, Lines).Verdict;
            seen += SurvivalRule.FullestHealth(list, 30) + SurvivalRule.FullestStamina(list, 30);
        });
        Assert.True(seen > 0);
    }
}

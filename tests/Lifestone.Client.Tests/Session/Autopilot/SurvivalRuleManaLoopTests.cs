using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 16 Q6 (docs/automation-engine.md E3, stage 1): the survival layer's answer to the fight's mana question, pure (<see cref="SurvivalRule.DecideFightMana"/>). What these prove: on the fight's own reads the layer gives the
/// answer the fight's loop gives (the fixed loop over a grid of bars and gives; the loop by the sum over the states its order names: the stamina line and its three waits, no mana missing, the bolt first, the pair, the
/// press that fills and its overflow, the rest's line, the least worth of a press), the drain's target is the fight's pick, and with the loop handed <see cref="SurvivalRule.DecideFight"/> answers the mana by it and not by
/// E3.0's sum. That the fight carries the answer out cast for cast is <c>FightSurvivalManaTests</c>'.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class SurvivalRuleManaLoopTests
{
    private const uint Source = 0x80000739;
    private static readonly LadderVitals Vitals = LadderLoader.WarMage.Vitals;
    private static readonly LadderSurvival Lines = new(50, 80, 25, 60, 15, 40, 50, 15, 80, 50, 0); // the ladder's survival lines of 2026-11-17

    /// <summary>A climber's read: 300 of 426 mana, 250 of 275 stamina, a 70 mana bolt, a press of Stamina to Mana I that nets 35 (give 45, fills), a drain that would give 58 from the source for 10 mana and the press after it.</summary>
    private static SurvivalManaLoop Sum() => new()
    {
        Rule = SurvivalManaRule.BySum,
        Lines = Vitals,
        Mana = 300,
        ManaMax = 426,
        Stamina = 250,
        StaminaMax = 275,
        BoltMana = 70,
        PressNow = new ManaLoopPress(0, 35, 10, 50, 45),
        PressFill = new ManaLoopPress(0, 35, 10, 50, 45),
        PressAfterDrain = new ManaLoopPress(0, 35, 10, 50, 45),
        DrainGive = 58,
        DrainMana = 10,
        StaminaSource = Source,
    };

    private static ManaLoopFacts FactsOf(in SurvivalManaLoop m) => new()
    {
        Mana = m.Mana,
        ManaMax = m.ManaMax,
        Stamina = m.Stamina,
        StaminaMax = m.StaminaMax,
        BoltMana = m.BoltMana,
        PressNow = m.PressNow,
        PressAfterDrain = m.PressAfterDrain,
        DrainGive = m.DrainGive,
        DrainMana = m.DrainMana,
        TargetDying = m.TargetDying,
        Attackers = m.Attackers,
        BoltsOwed = m.BoltsOwed,
        StaminaKeep = m.StaminaKeep,
        PressFill = m.PressFill,
        DrainOnBoltTarget = m.DrainOnBoltTarget,
    };

    [Fact]
    public void The_fixed_loop_is_the_fights_answer_over_a_grid_of_bars_and_gives_and_the_drain_names_the_fights_pick()
    {
        var seen = new HashSet<ManaLoopMove>();
        foreach (var mana in new uint[] { 0, 15, 19, 20, 44, 45, 60 })
        {
            foreach (var stamina in new uint[] { 0, 50, 137, 138, 200 })
            {
                foreach (var give in new[] { 0, 16, 29, 30, 58 })
                {
                    var loop = new SurvivalManaLoop { Rule = SurvivalManaRule.Fixed, Lines = Vitals, Mana = mana, Stamina = stamina, StaminaMax = 275, DrainGive = give, StaminaSource = Source };
                    var answer = SurvivalRule.DecideFightMana(in loop);
                    var fights = ManaLoopRule.Decide(mana, stamina, 275, Vitals, give);
                    seen.Add(fights);

                    Assert.Equal(fights, answer.Loop.Move);
                    var (verdict, target) = fights switch
                    {
                        ManaLoopMove.Press => (SurvivalVerdict.TakeMana, 0u),
                        ManaLoopMove.DrainStamina => (SurvivalVerdict.TakeMana, Source),
                        _ => (SurvivalVerdict.Go, 0u),
                    };
                    Assert.Equal((verdict, target), (answer.Decision.Verdict, answer.Decision.DrainTarget));
                }
            }
        }

        Assert.Equal(3, seen.Count); // the grid reaches the press, the drain and nothing
    }

    [Fact]
    public void The_loop_by_the_sum_is_the_fights_answer_in_every_state_its_order_names()
    {
        var states = new (string Name, SurvivalManaLoop Loop, ManaLoopMove Move)[]
        {
            ("the stamina line drains", Sum() with { Stamina = 100 }, ManaLoopMove.DrainStamina),
            ("the stamina line waits: too many on her", Sum() with { Stamina = 100, Attackers = Vitals.StaminaDrainMaxAttackers + 1 }, ManaLoopMove.None),
            ("the stamina line waits: bolts owed", Sum() with { Stamina = 100, BoltsOwed = 1 }, ManaLoopMove.None),
            ("the stamina line waits: its target dies to the bolt", Sum() with { Stamina = 100, TargetDying = true, DrainOnBoltTarget = true }, ManaLoopMove.None),
            ("no mana missing", Sum() with { Mana = 426 }, ManaLoopMove.None),
            ("the bolt first: the target dies", Sum() with { TargetDying = true }, ManaLoopMove.None),
            ("the bolt first: too many on her", Sum() with { Attackers = Vitals.ManaLoopMaxAttackers + 1 }, ManaLoopMove.None),
            ("the bolt first: bolts owed", Sum() with { BoltsOwed = 2 }, ManaLoopMove.None),
            ("the press fills", Sum(), ManaLoopMove.Press),
            ("the press would overflow", Sum() with { Mana = 400 }, ManaLoopMove.None),
            ("the rest's line: ample mana", Sum() with { StaminaKeep = 220, BoltMana = 20 }, ManaLoopMove.None),
            ("under a bolt the press pays", Sum() with { Mana = 50, BoltsOwed = 2 }, ManaLoopMove.Press),
            ("the least worth of a press", Sum() with { PressFill = new ManaLoopPress(0, 30, 10, 50, 45) }, ManaLoopMove.None),
            ("the drain and the press after it pay", Sum() with { Mana = 50, Stamina = 120, PressNow = new ManaLoopPress(0, 10, 10, 30, 20), PressAfterDrain = new ManaLoopPress(0, 90, 10, 100, 100) }, ManaLoopMove.DrainStamina),
        };

        foreach (var (name, loop, move) in states)
        {
            var facts = FactsOf(in loop);
            var fights = ManaLoopRule.DecideBySum(in facts, Vitals);
            var answer = SurvivalRule.DecideFightMana(in loop);

            Assert.True(fights.Move == move, $"{name}: the fight's own rule says {fights.Move} ({fights.Why}), the state was meant for {move}");
            Assert.Equal(fights, answer.Loop);
            Assert.Equal(move == ManaLoopMove.None ? SurvivalVerdict.Go : SurvivalVerdict.TakeMana, answer.Decision.Verdict);
            Assert.Equal(move == ManaLoopMove.DrainStamina ? Source : 0u, answer.Decision.DrainTarget);
            Assert.Same(fights.Why, answer.Decision.Why); // the fight's own reason, a fixed word
        }
    }

    [Fact]
    public void With_the_loop_handed_the_layers_fight_answer_takes_mana_by_it_and_not_by_the_sum_of_what_finishes_them()
    {
        // E3.0's answer: one attacker of 400 health, a bolt of 70 for 200 needs 140 and she holds 300: she goes on. The loop presses (the press fills).
        var fight = new SurvivalFight { Mana = 300, BoltMana = 70, BoltDamage = 200, PressNet = 35 };
        var input = new SurvivalInput
        {
            InFight = true,
            HealthPercent = 100,
            ManaPercent = 70,
            StaminaPercent = 91,
            HostileNear = true,
            Attacked = true,
            MayLeave = true,
            Attackers = new[] { new SurvivalAttacker(Source, "Tusker Slave", 3, 1f, 300) { Health = 400 } },
            Fight = fight,
        };
        Assert.Equal(SurvivalVerdict.Go, SurvivalRule.Decide(in input, Lines).Verdict);

        var handed = input with { Fight = fight with { ManaLoop = Sum() } };
        var press = SurvivalRule.Decide(in handed, Lines);
        Assert.Equal((SurvivalVerdict.TakeMana, 0u), (press.Verdict, press.DrainTarget));

        // E3.0 would take mana (the 400 health needs 140, she holds 100, the press pays); the loop holds the bolt first (bolts owed): the layer goes on.
        var shortOf = handed with { ManaPercent = 23, Fight = fight with { Mana = 100, ManaLoop = Sum() with { Mana = 100, BoltsOwed = 1 } } };
        var unhanded = shortOf with { Fight = shortOf.Fight with { ManaLoop = default } };
        Assert.Equal(SurvivalVerdict.TakeMana, SurvivalRule.Decide(in unhanded, Lines).Verdict);
        var held = SurvivalRule.Decide(in shortOf, Lines);
        Assert.Equal(SurvivalVerdict.Go, held.Verdict);
        Assert.Equal(SurvivalRule.ManaGoesOn, held.Why);
    }

    [Fact]
    public void Nothing_handed_is_no_answer_and_an_answer_allocates_nothing()
    {
        var none = SurvivalRule.DecideFightMana(default);
        Assert.Equal((SurvivalVerdict.Go, SurvivalRule.ManaNotRead), (none.Decision.Verdict, none.Decision.Why));

        var sum = Sum() with { Stamina = 100 };
        var fixedLoop = new SurvivalManaLoop { Rule = SurvivalManaRule.Fixed, Lines = Vitals, Mana = 30, Stamina = 200, StaminaMax = 275 };
        _ = SurvivalRule.DecideFightMana(in sum);
        _ = SurvivalRule.DecideFightMana(in fixedLoop);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            _ = SurvivalRule.DecideFightMana(in sum);
            _ = SurvivalRule.DecideFightMana(in fixedLoop);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}

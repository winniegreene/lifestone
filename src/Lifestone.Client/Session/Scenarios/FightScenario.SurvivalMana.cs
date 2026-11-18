using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 16 Q6 (docs/automation-engine.md E3, stage 1 of <c>TODO(engine-survival-flip)</c>; <c>--fight-survival-mana</c>, <see cref="FightOptions.SurvivalMana"/>, default off): the fight's mana loop and stamina drain take their decision
/// from the survival layer (<see cref="SurvivalRule.DecideFightMana"/>) and carry it out as before. Each of the loop's three rules hands the layer the reads it decided by (<see cref="SurvivalManaLoop"/>): the fixed loop of a
/// caster who is not the climber (<see cref="ManaLoopStepOwn"/>), the climber's loop by the sum (<see cref="ManaLoopBySum"/>) and her fills by the vitals policy (<see cref="ManaLoopByPolicy"/>, and the upkeep's ask,
/// <see cref="UpkeepPlan"/>). The reads, the casts, the lines and every guard before a cast stay the fight's; only the decision moves. Allocation-free but for the one line a fight that says the layer decides.
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>The first words of the line that says the layer decides the mana loop.</summary>
    public const string SurvivalManaToken = "survival-mana: ";

    private bool _survivalManaSaid;

    /// <summary>The layer's decisions of the mana loop this fight (a test reads it).</summary>
    internal int SurvivalManaAsks { get; private set; }

    /// <summary>The fixed loop's decision, the layer's: the move, and the drain's target in <paramref name="source"/> (the fight's pick, handed to it).</summary>
    private ManaLoopMove SurvivalLoopFixed(LadderVitals rules, uint mana, uint stamina, uint staminaMax, int give, ref uint source)
    {
        var answer = AskSurvivalMana(new SurvivalManaLoop { Rule = SurvivalManaRule.Fixed, Lines = rules, Mana = mana, Stamina = stamina, StaminaMax = staminaMax, DrainGive = give, StaminaSource = source }, "the fixed loop");
        source = answer.Loop.Move == ManaLoopMove.DrainStamina ? answer.Decision.DrainTarget : source;
        return answer.Loop.Move;
    }

    /// <summary>The climber's loop by the sum, the layer's decision: the plan, and the drain's target in <paramref name="source"/>.</summary>
    private ManaLoopPlan SurvivalLoopBySum(in ManaLoopFacts facts, LadderVitals rules, ref uint source)
    {
        var answer = AskSurvivalMana(
            new SurvivalManaLoop
            {
                Rule = SurvivalManaRule.BySum,
                Lines = rules,
                Mana = facts.Mana,
                ManaMax = facts.ManaMax,
                Stamina = facts.Stamina,
                StaminaMax = facts.StaminaMax,
                BoltMana = facts.BoltMana,
                PressNow = facts.PressNow,
                PressFill = facts.PressFill,
                PressAfterDrain = facts.PressAfterDrain,
                DrainGive = facts.DrainGive,
                DrainMana = facts.DrainMana,
                StaminaSource = source,
                TargetDying = facts.TargetDying,
                DrainOnBoltTarget = facts.DrainOnBoltTarget,
                Attackers = facts.Attackers,
                BoltsOwed = facts.BoltsOwed,
                StaminaKeep = facts.StaminaKeep,
            },
            "the climber's loop by the sum");
        source = answer.Loop.Move == ManaLoopMove.DrainStamina ? answer.Decision.DrainTarget : source;
        return answer.Loop;
    }

    /// <summary>The climber's fills by the vitals policy, the layer's decision (the plan the fight logs and starts; its targets are the reads' own, handed to it).</summary>
    private VitalsPlan SurvivalLoopPolicy(in VitalsPolicyFacts facts, LadderVitalsPolicy rules, uint staminaSource, uint healthSource) =>
        AskSurvivalMana(new SurvivalManaLoop { Rule = SurvivalManaRule.Policy, PolicyLines = rules, PolicyFacts = facts, PolicyStaminaSource = staminaSource, PolicyHealthSource = healthSource }, "the vitals policy").Policy;

    private SurvivalManaAnswer AskSurvivalMana(in SurvivalManaLoop loop, string rule)
    {
        SurvivalManaAsks++;
        if (!_survivalManaSaid)
        {
            _survivalManaSaid = true;
            _log.Info(LogCategory, $"{SurvivalManaToken}the survival layer decides the fight's mana loop and stamina drain (--fight-survival-mana on; first by {rule}): the press, the stamina drain and its target, or nothing; the fight reads and casts as before");
        }

        return SurvivalRule.DecideFightMana(in loop);
    }
}

using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>M6 16 Q6: which of the fight's mana rules the character's loop is (the fight reads it from the character's policy and the ladder; the rule answers by it).</summary>
public enum SurvivalManaRule : byte
{
    /// <summary>No loop input handed: the layer answers the mana question by E3.0's sum (the mana that finishes the attackers).</summary>
    NotRead,

    /// <summary>M6 7 AN: a caster with no Mana Conversion who is not the climber: under <c>mana_loop_under</c>, the press, or the stamina drain that gives at least <c>stamina_drain_min_give</c>.</summary>
    Fixed,

    /// <summary>M6 8 H and M6 14: the climber's loop by the sum (the stamina line, the bolts between, the dying target, the press that fills, the least worth of a press).</summary>
    BySum,

    /// <summary>M6 14 (<c>m6-vitals-policy</c>): the climber's fills by the numbers (her floors, a kill's budget, the upkeep).</summary>
    Policy,
}

/// <summary>
/// M6 16 Q6 (docs/automation-engine.md E3, stage 1 of <c>TODO(engine-survival-flip)</c>): what the fight's mana loop hands the survival layer so that the layer, not the loop, decides the press, the stamina drain with its target,
/// or nothing. Every field is a number or a pick the fight read the way it reads it for its own loop; none is the layer's own. The inputs E3.0's rule lacked, by name: the least worth of a press and the bolts between presses
/// (<see cref="Lines"/>'s <c>mana_press_min_bolts</c> and <c>mana_loop_bolts_between</c>, with <see cref="BoltsOwed"/>), the stamina line (<see cref="Lines"/>'s <c>stamina_keep_percent</c> and the rest's <see cref="StaminaKeep"/>), the dying
/// target (<see cref="TargetDying"/>, <see cref="DrainOnBoltTarget"/>) and the policy's floors (<see cref="PolicyFacts"/>, from which <see cref="VitalsPolicy"/> works them out).
/// </summary>
public readonly record struct SurvivalManaLoop
{
    /// <summary>Which rule the character's loop is; <see cref="SurvivalManaRule.NotRead"/>: none handed.</summary>
    public SurvivalManaRule Rule { get; init; }

    /// <summary>The ladder's vitals lines the loop reads (<see cref="LadderVitals"/>: <c>mana_loop_under</c>, <c>mana_loop_stamina_percent</c>, <c>stamina_drain_min_give</c>, the least worth of a press, the bolts between, the stamina line).</summary>
    public LadderVitals? Lines { get; init; }

    /// <summary>The ladder's vitals policy lines (<see cref="SurvivalManaRule.Policy"/>).</summary>
    public LadderVitalsPolicy? PolicyLines { get; init; }

    /// <summary>The mana held.</summary>
    public uint Mana { get; init; }

    /// <summary>The mana's maximum.</summary>
    public uint ManaMax { get; init; }

    /// <summary>The stamina held.</summary>
    public uint Stamina { get; init; }

    /// <summary>The stamina's maximum.</summary>
    public uint StaminaMax { get; init; }

    /// <summary>The next bolt's mana: the most it costs (the loop's number, not E3.0's expected cost).</summary>
    public uint BoltMana { get; init; }

    /// <summary>The press by the sum now (<c>ManaLoopRule.PressOption</c>).</summary>
    public ManaLoopPress PressNow { get; init; }

    /// <summary>The press that fills (<c>ManaLoopRule.FillOption</c>).</summary>
    public ManaLoopPress PressFill { get; init; }

    /// <summary>The press after the stamina drain, at the stamina it gives and the mana it costs.</summary>
    public ManaLoopPress PressAfterDrain { get; init; }

    /// <summary>The stamina one Drain Stamina Other gives from <see cref="StaminaSource"/> (cut to her room under <see cref="SurvivalManaRule.BySum"/>); 0: none read.</summary>
    public int DrainGive { get; init; }

    /// <summary>The drain's mana (its base); 0: no level castable.</summary>
    public uint DrainMana { get; init; }

    /// <summary>
    /// The creature the stamina drain is priced on: the fight's pick (every creature of the ground in reach with a clear line, by its appraisal or its kind's stamina as read, less the bolt's target dying to the next bolt), not
    /// the layer's <see cref="SurvivalRule.FullestStamina"/> over the board's attackers; 0: none.
    /// </summary>
    public uint StaminaSource { get; init; }

    /// <summary>The bolt's target dies to its next bolt (<c>mana_loop_bolt_first_hits</c> of the bolt's mean hit, from its live health).</summary>
    public bool TargetDying { get; init; }

    /// <summary>The stamina drain's pick is the bolt's own target.</summary>
    public bool DrainOnBoltTarget { get; init; }

    /// <summary>The creatures hitting her now, as the fight counts them (its tally's attackers).</summary>
    public int Attackers { get; init; }

    /// <summary>The bolts still owed since the last mana cast (<c>mana_loop_bolts_between</c>).</summary>
    public int BoltsOwed { get; init; }

    /// <summary>The ground's rest line for stamina before a pull, in points (0: none): a press with ample mana does not take her under it.</summary>
    public uint StaminaKeep { get; init; }

    /// <summary>The policy's facts (<see cref="SurvivalManaRule.Policy"/>): her bars, rates, the bolt, the sources, the presses and drains she may cast, the upkeep; the floors are worked out from them.</summary>
    public VitalsPolicyFacts PolicyFacts { get; init; }

    /// <summary>The policy's stamina source (the fight's pick), or 0.</summary>
    public uint PolicyStaminaSource { get; init; }

    /// <summary>The policy's health source (the fight's fullest drain target), or 0.</summary>
    public uint PolicyHealthSource { get; init; }
}

/// <summary>The layer's answer to the fight's mana question: its decision and, for the fight that carries it out, the plan it decided by (the level, the net and the reason the fight's own lines print).</summary>
/// <param name="Decision">The verdict: <see cref="SurvivalVerdict.TakeMana"/> (a press or a drink: no target; a stamina drain: its target), <see cref="SurvivalVerdict.Heal"/> (the policy's health drain on its target), or <see cref="SurvivalVerdict.Go"/>.</param>
/// <param name="Loop">Under <see cref="SurvivalManaRule.Fixed"/> and <see cref="SurvivalManaRule.BySum"/>: the move, its reason, its net and level.</param>
/// <param name="Policy">Under <see cref="SurvivalManaRule.Policy"/>: the policy's plan (its choice and every number it read, for the fight's line).</param>
public readonly record struct SurvivalManaAnswer(SurvivalDecision Decision, ManaLoopPlan Loop, VitalsPlan Policy);

/// <summary>M6 16 Q6: the layer's answer to the fight's mana question (the press, the stamina drain with its target, or nothing), pure and allocation-free.</summary>
public static partial class SurvivalRule
{
    /// <summary>Why: no loop input.</summary>
    public const string ManaNotRead = "the fight's mana loop handed nothing";

    /// <summary>Why (the fixed loop): under the mana line, the press.</summary>
    public const string ManaFixedPress = "the fixed loop: under the mana line, the press";

    /// <summary>Why (the fixed loop): under the mana line and short of the press's stamina, the stamina drain.</summary>
    public const string ManaFixedDrain = "the fixed loop: under the mana line and short of stamina, the stamina drain";

    /// <summary>Why (the fixed loop): over the mana line.</summary>
    public const string ManaFixedNone = "the fixed loop: no mana cast";

    /// <summary>
    /// The mana question in a fight, by the character's loop (<see cref="SurvivalManaLoop.Rule"/>): <see cref="SurvivalManaRule.Fixed"/>, M6 7 AN's fixed lines; <see cref="SurvivalManaRule.BySum"/>, the climber's order (the stamina line
    /// first, whatever her mana, waiting while bolts are owed, while too many are on her and while its target dies to the next bolt; no mana missing, nothing; the bolt first while it can be paid and the target dies to it, too many
    /// are on her or bolts are owed; the drain and its press by the sum; the press that fills, worth at least its share of a bolt, the rest's stamina line kept); <see cref="SurvivalManaRule.Policy"/>, the policy's plan from her
    /// floors and a kill's budget. Each order is the shared pure rule of <c>Session/Vitals</c> the fight decided by until this stage, so on the same reads the layer gives the fight's answer.
    /// </summary>
    /// <param name="loop">The fight's reads.</param>
    public static SurvivalManaAnswer DecideFightMana(in SurvivalManaLoop loop)
    {
        switch (loop.Rule)
        {
            case SurvivalManaRule.Fixed:
                {
                    var lines = loop.Lines ?? throw new ArgumentException("the fixed loop's lines are missing", nameof(loop));
                    var move = ManaLoopRule.Decide(loop.Mana, loop.Stamina, loop.StaminaMax, lines, loop.DrainGive);
                    var why = move switch { ManaLoopMove.Press => ManaFixedPress, ManaLoopMove.DrainStamina => ManaFixedDrain, _ => ManaFixedNone };
                    return new SurvivalManaAnswer(LoopDecision(move, why, loop.StaminaSource), new ManaLoopPlan(move, why, 0), default);
                }

            case SurvivalManaRule.BySum:
                {
                    var lines = loop.Lines ?? throw new ArgumentException("the loop's lines are missing", nameof(loop));
                    var facts = new ManaLoopFacts
                    {
                        Mana = loop.Mana,
                        ManaMax = loop.ManaMax,
                        Stamina = loop.Stamina,
                        StaminaMax = loop.StaminaMax,
                        BoltMana = loop.BoltMana,
                        PressNow = loop.PressNow,
                        PressAfterDrain = loop.PressAfterDrain,
                        DrainGive = loop.DrainGive,
                        DrainMana = loop.DrainMana,
                        TargetDying = loop.TargetDying,
                        Attackers = loop.Attackers,
                        BoltsOwed = loop.BoltsOwed,
                        StaminaKeep = loop.StaminaKeep,
                        PressFill = loop.PressFill,
                        DrainOnBoltTarget = loop.DrainOnBoltTarget,
                    };
                    var plan = ManaLoopRule.DecideBySum(in facts, lines);
                    return new SurvivalManaAnswer(LoopDecision(plan.Move, plan.Why, loop.StaminaSource), plan, default);
                }

            case SurvivalManaRule.Policy:
                {
                    var lines = loop.PolicyLines ?? throw new ArgumentException("the policy's lines are missing", nameof(loop));
                    var plan = VitalsPolicy.Decide(loop.PolicyFacts, lines);
                    var c = plan.Choice;
                    var decision = c.Move switch
                    {
                        VitalsMove.Press or VitalsMove.DrinkMana => new SurvivalDecision(SurvivalVerdict.TakeMana, SurvivalReason.Mana, c.Why),
                        VitalsMove.DrainStamina => new SurvivalDecision(SurvivalVerdict.TakeMana, SurvivalReason.Mana, c.Why, loop.PolicyStaminaSource),
                        VitalsMove.DrainHealth => new SurvivalDecision(SurvivalVerdict.Heal, SurvivalReason.Heal, c.Why, loop.PolicyHealthSource),
                        _ => new SurvivalDecision(SurvivalVerdict.Go, SurvivalReason.None, c.Why),
                    };
                    return new SurvivalManaAnswer(decision, new ManaLoopPlan(ManaLoopMove.None, c.Why, 0), plan);
                }

            default:
                return new SurvivalManaAnswer(new SurvivalDecision(SurvivalVerdict.Go, SurvivalReason.None, ManaNotRead), new ManaLoopPlan(ManaLoopMove.None, ManaNotRead, 0), default);
        }
    }

    private static SurvivalDecision LoopDecision(ManaLoopMove move, string why, uint source) => move switch
    {
        ManaLoopMove.Press => new SurvivalDecision(SurvivalVerdict.TakeMana, SurvivalReason.Mana, why),
        ManaLoopMove.DrainStamina => new SurvivalDecision(SurvivalVerdict.TakeMana, SurvivalReason.Mana, why, source),
        _ => new SurvivalDecision(SurvivalVerdict.Go, SurvivalReason.None, why),
    };
}

using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 item 1 b: the climber's level-up spend, train then spend. First the credits: <see cref="SkillCreditPlan.Next"/> over the order, each skill it names trained by <see cref="SkillTrainStep"/> (check, act, check) while the credits
/// cover the next; then the experience: <see cref="XpSpendRule"/> (the shares of the character's level, the Strength floor of the ground's line first) called again while a call ends <see cref="XpSpendEnd.RaiseCap"/>, at most
/// <see cref="MaxCalls"/> calls, each call's outcome on one line. A refused or unconfirmed train and a refused raise end the run with <see cref="Failure"/> naming it, never retried; the caller stops by name.
/// </summary>
/// <remarks>The caller runs it at the level up job's cycle start and, M6 7 R, in the fight soon after a level-up: a raise's lane is its own, ACE takes a raise at any time, and the fight's Choose reads the raised skills (<c>AutopilotScenario.LadderMidRunSpend</c>). Its bounds are <see cref="XpSpendRule"/>'s (60 raises and 120 s a call) and the calls' cap.</remarks>
public sealed class LevelUpSpend
{
    /// <summary>The experience calls one spend makes at most (the trip's own cap, <c>LogisticsStation.MaxXpCalls</c>: 180 raises).</summary>
    public const int MaxCalls = 3;

    private readonly XpSpendRule _rule;
    private readonly IReadOnlyList<CreditLine>? _order;
    private readonly ILogSink? _log;
    private readonly SkillTrainStep _train;
    private readonly List<uint> _trained = [];
    private IGameSessionView? _view;
    private Phase _phase;
    private int _call;

    private enum Phase
    {
        Idle,
        Credits,
        Training,
        Spending,
        Done,
    }

    /// <summary>A spend over the XpTable.</summary>
    /// <param name="table">The XpTable.</param>
    /// <param name="order">The credit order (<see cref="SkillCreditPlan.OrderOf"/>), or null when it cannot be read: nothing is trained and the caller has named why.</param>
    /// <param name="burdenTripPercent">The ground's <c>burden_trip_percent</c> (the Strength floor's line), or null for no floor.</param>
    /// <param name="log">The log, or null.</param>
    /// <param name="standingBot">M6 item 14: the Strength floor's standing read (<see cref="XpSpendRule"/>'s <c>standingBot</c>: true while the stop at the buff bot applies), or null for the base Strength alone.</param>
    /// <param name="casterFacts">M6 14 (<c>m6-caster-spend</c>): the casters she holds short of a wield requirement and her rate (<see cref="XpSpendRule"/>'s <c>casterFacts</c>), or null for none read.</param>
    /// <param name="loreFacts">M6 14 (<c>m6-lore-spend</c>): the pieces she holds that wait on her Arcane Lore (<see cref="XpSpendRule"/>'s <c>loreFacts</c>), or null for none read.</param>
    /// <param name="sellLootBurden">M6 14 hour load: the burden of the sell loot she carries, which the Strength floor leaves out (<see cref="XpSpendRule"/>'s <c>sellLootBurden</c>), or null for the measured burden whole.</param>
    public LevelUpSpend(XpTableData table, IReadOnlyList<CreditLine>? order, int? burdenTripPercent, ILogSink? log = null, Func<bool>? standingBot = null, Func<CasterSpendFacts?>? casterFacts = null, Func<LoreSpendFacts?>? loreFacts = null, Func<GameState, long>? sellLootBurden = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        _rule = new XpSpendRule(table, log, followLevel: true, burdenTripPercent: burdenTripPercent, standingBot: standingBot, casterFacts: casterFacts, loreFacts: loreFacts, sellLootBurden: sellLootBurden);
        _order = order;
        _log = log;
        _train = new SkillTrainStep(log);
    }

    /// <summary>True once the spend has ended.</summary>
    public bool Done => _phase == Phase.Done;

    /// <summary>The skills trained this spend, in order.</summary>
    public IReadOnlyList<uint> Trained => _trained;

    /// <summary>The experience the raises spent, over the calls.</summary>
    public long SpentXp { get; private set; }

    /// <summary>The raises confirmed, over the calls.</summary>
    public int Raises { get; private set; }

    /// <summary>The last credit step read (what the plan asks next), or null.</summary>
    public CreditStep? LastStep { get; private set; }

    /// <summary>The named reason the spend could not go on (a refused or unconfirmed train, a refused raise, an unread character), or null.</summary>
    public string? Failure { get; private set; }

    /// <summary>The skill whose train failed, or null.</summary>
    public uint? FailedSkill { get; private set; }

    /// <summary>The spend's one-line summary: <c>trained=&lt;list&gt; spent_xp=&lt;n&gt; raises=&lt;n&gt; credits=&lt;n&gt; next=&lt;step&gt;</c>.</summary>
    public string Fields => string.Create(CultureInfo.InvariantCulture, $"trained={TrainedField(_trained)} spent_xp={SpentXp} raises={Raises} credits={_view?.State.Self.Qualities.AvailableSkillCredits ?? 0} next={LastStep?.ToString() ?? "n/a"}");

    /// <summary>A skill list for a line's field: the names without spaces joined by commas, or <c>none</c>.</summary>
    /// <param name="skills">The skill ids.</param>
    public static string TrainedField(IEnumerable<uint> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        var names = skills.Select(id => MageTemplate.SkillName(id).Replace(" ", string.Empty, StringComparison.Ordinal)).ToArray();
        return names.Length == 0 ? "none" : string.Join(",", names);
    }

    /// <summary>Begins the spend over the session (its Action API).</summary>
    /// <param name="view">The session view.</param>
    public void Begin(IGameSessionView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        _view = view;
        _trained.Clear();
        SpentXp = 0;
        Raises = 0;
        LastStep = null;
        Failure = null;
        FailedSkill = null;
        _call = 0;
        _phase = _order is null ? Phase.Spending : Phase.Credits;
        if (_phase == Phase.Spending)
        {
            BeginCall();
        }
    }

    /// <summary>Advances the spend; true once it is <see cref="Done"/>.</summary>
    public bool Tick()
    {
        if (_view is null)
        {
            return false;
        }

        while (true)
        {
            switch (_phase)
            {
                case Phase.Credits:
                    var q = _view.State.Self.Qualities;
                    if (!q.IsKnown)
                    {
                        return Fail("credits: the character's description has not arrived: the credits and the skills are unknown", null);
                    }

                    var step = SkillCreditPlan.Next(_order!, q.AvailableSkillCredits, id => q.TryGetSkill(id, out var s) && s.AdvancementClass >= SkillRules.TrainedClass);
                    LastStep = step;
                    _log?.Info(XpSpendRule.LogCategory, string.Create(CultureInfo.InvariantCulture, $"credits: level {q.Level} credits={q.AvailableSkillCredits} order={TrainedField(_order!.Select(l => l.Skill))}: {step}"));
                    if (step.Kind != CreditStepKind.Train)
                    {
                        _phase = Phase.Spending;
                        BeginCall();
                        continue;
                    }

                    _train.Begin(_view, step.Skill!.Value, step.Cost);
                    _phase = Phase.Training;
                    continue;
                case Phase.Training:
                    if (!_train.Tick())
                    {
                        return false;
                    }

                    if (_train.End == SkillTrainEnd.Trained)
                    {
                        _trained.Add(_train.Skill);
                        _phase = Phase.Credits;
                        continue;
                    }

                    if (_train.End == SkillTrainEnd.DryRun)
                    {
                        _phase = Phase.Spending; // dry run: nothing was trained; the raises are logged the same way
                        BeginCall();
                        continue;
                    }

                    return Fail(_train.Failure ?? $"train {MageTemplate.SkillName(_train.Skill)} ended {_train.End}", _train.Skill);
                case Phase.Spending:
                    if (!_rule.Tick())
                    {
                        return false;
                    }

                    SpentXp += _rule.SpentXp;
                    Raises += _rule.Raises;
                    _log?.Info(XpSpendRule.LogCategory, string.Create(CultureInfo.InvariantCulture, $"xp: level-up call {_call}: ended {_rule.End} ({_rule.Fields}){(_rule.Skipped > 0 ? $", {_rule.Skipped} skipped: {_rule.FirstSkip}" : string.Empty)}{(_rule.PoolNote is { } note ? ", " + note : string.Empty)}"));
                    if (_rule.Failure is { } failure)
                    {
                        return Fail("experience: " + failure, null);
                    }

                    if (_rule.End == XpSpendEnd.RaiseCap && _call < MaxCalls)
                    {
                        BeginCall();
                        continue;
                    }

                    if (_rule.End == XpSpendEnd.RaiseCap)
                    {
                        _log?.Info(XpSpendRule.LogCategory, string.Create(CultureInfo.InvariantCulture, $"xp: the level-up calls reached their cap of {MaxCalls}: more is left for the next level-up or trip"));
                    }

                    _phase = Phase.Done;
                    _log?.Info(XpSpendRule.LogCategory, "level-up spend: done: " + Fields);
                    return true;
                case Phase.Done:
                    return true;
                default:
                    return false;
            }
        }
    }

    private void BeginCall()
    {
        _call++;
        _rule.Begin(_view!);
    }

    private bool Fail(string why, uint? skill)
    {
        Failure = why;
        FailedSkill = skill;
        _phase = Phase.Done;
        _log?.Warn(XpSpendRule.LogCategory, "level-up spend: stopped: " + why + " (" + Fields + ")");
        return true;
    }
}

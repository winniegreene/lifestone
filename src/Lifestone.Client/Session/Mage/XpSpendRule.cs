using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Mage;

/// <summary>Why an <see cref="XpSpendRule"/> run ended (<see cref="XpSpendRule.End"/>).</summary>
public enum XpSpendEnd
{
    /// <summary>The run has not ended (or has not begun).</summary>
    None,

    /// <summary>The unassigned experience does not cover the next rank of any stat the plan still raises (each such stat is named once and passed over; the pool grows until one is covered). Not a failure: with nothing to cover, <c>spent_xp=0</c>.</summary>
    PoolCoversNothing,

    /// <summary>Every stat the planner raises is at its planned rank for the experience the character has earned: nothing is left to raise.</summary>
    PlanReached,

    /// <summary>The raises of one call reached <see cref="XpSpendRule.MaxRaises"/> (Lifestone's own bound); more may be left, and the caller may call again.</summary>
    RaiseCap,

    /// <summary>The call's time bound (<see cref="XpSpendRule.MaxSeconds"/>, Lifestone's own) passed; no further raise was sent.</summary>
    TimeBound,

    /// <summary>A raise was refused or did not complete (<see cref="XpSpendRule.Failure"/> names it). Nothing is retried.</summary>
    Refused,

    /// <summary>The character's description has not arrived: its stats and experience are unknown (<see cref="XpSpendRule.Failure"/> says so).</summary>
    Unread,

    /// <summary>A raise ran under dry run: it was logged and not sent, so nothing was spent.</summary>
    DryRun,
}

/// <summary>
/// The mage's experience spending (M5 item 3 m4; spec section 2 DoD 6, section 5.5): whenever the unassigned experience covers the next rank of a stat <see cref="MageSpendingPlanner"/>'s order still raises, raise it (the first
/// stat of the order that the pool covers) through ACE's raise actions (<see cref="IActionApi.RaiseSkill"/>, <see cref="IActionApi.RaiseAttribute"/>, <see cref="IActionApi.RaiseVital"/>), one rank and one action at a time,
/// waiting for the server's answer (the stat's private update, or ACE's told refusal, or the action's timeout) before the next. A small runner a station or a loop calls: <see cref="Begin(IGameSessionView, IActionApi)"/>, <see cref="Tick"/> until <see cref="Done"/>, then
/// <see cref="Fields"/> (<c>spent_xp=&lt;sum&gt; raises=&lt;n&gt;</c>).
/// </summary>
/// <remarks>
/// <para><b>The order.</b> The planner (<see cref="MageSpendingPlanner.Compute"/>) turns an amount of experience into target ranks, listed in its order: the four magic skills, Focus and Self, Mana Conversion, Max Mana, then the
/// pool of the rest. The rule computes the plan for <i>all</i> the experience the character has earned (<c>TotalExperience</c>: what is spent on stats plus the unassigned pool; ACE spends only the available part), so the
/// plan is the character as the planner would have built it from nothing. Walking the plan in its order, the first stat whose ranks are under its target is the one raised next, by one rank, for exactly that rank's
/// cost (the rank's cumulative XpTable entry less the experience already spent on it). The price list is the stat's own class (a specialised skill's list prices a specialised skill). A stat whose next rank the pool does not
/// cover is passed over, named once (<see cref="Unaffordable"/>, <see cref="FirstUnaffordable"/>), and the walk goes on down the order to the first stat the pool does cover (M5 item 3 fix F3, review 3-R7: the spec's "whenever
/// the unassigned pool covers the next rank" is read per stat, so one dear rank never keeps a cheaper planned rank unbought). When no planned stat's next rank is covered the run ends with
/// <see cref="XpSpendEnd.PoolCoversNothing"/> and <see cref="PoolNote"/> names the pool and the cheapest next rank; the pool grows until one is covered.</para>
/// <para><b>ACE's rules this rests on</b> (all ACEmulator/ACE@95ef426d, <c>Source/ACE.Server/WorldObjects</c>). The raise actions: <c>Player_Attributes.cs</c> <c>HandleActionRaiseAttribute</c> 13-58 (an amount over the available
/// experience is dropped, 21-26; a refused spend sends "Your attempt to raise {attribute} has failed.", 29-33), <c>Player_Vitals.cs</c> <c>HandleActionRaiseVital</c> 20-64 (an amount over the available experience sends the same
/// line, 28-39), <c>Player_Skills.cs</c> <c>HandleActionRaiseSkill</c> 21-62 (a skill that is not trained or specialised, or an amount over the available experience, is dropped with a log line only, 25-35: no refusal
/// reaches the client). Each spend (<c>SpendAttributeXp</c> 75-107, <c>SpendVitalXp</c> 67-99, <c>SpendSkillXp</c> 69-110): a stat at its maximum rank is refused (80, 72, 81), an amount over the experience left to the
/// maximum rank is dropped whole (86-91, 78-83, 87-92), the experience comes off the pool by <c>Player_Xp.cs</c> <c>SpendXP</c> 352-363 (which also sends the AvailableExperience update, the first answer to a raise), then
/// the experience spent is added (102, 94, 103) and the ranks are recomputed from the experience spent in total (105, 97, 106: <c>CalcAttributeRank</c>, <c>CalcVitalRank</c>, <c>CalcSkillRank</c>). So a rank's price is
/// the XpTable's cumulative difference, ACE takes any amount up to the maximum rank (one rank at a time is this rule's choice), and the ranks follow the experience spent, not the number of raises.</para>
/// <para><b>Never off the plan.</b> Only a stat the plan lists with ranks above the character's is raised. A stat the planner does not raise is never touched. A skill the plan lists but the character holds untrained
/// (or does not have), and a stat at its rank cap, are skipped and counted (<see cref="Skipped"/>, <see cref="FirstSkip"/>): ACE tells no refusal for an untrained skill's raise, so it is not sent.</para>
/// <para><b>Bounds</b> (Lifestone's own numbers, not retail's): <see cref="MaxRaisesPerCall"/> raises and <see cref="MaxSecondsPerCall"/> seconds of the session clock a call. The time bound stops new sends; a raise in flight
/// finishes first (the Action API times it out itself).</para>
/// <para><b>Source.</b> The raises are <see cref="ActionSource.Automation"/>: the kill switch cancels them and dry run logs them without sending (<see cref="XpSpendEnd.DryRun"/>).</para>
/// <para><b>Not a failure:</b> a pool that covers no planned next rank ends the run with <c>spent_xp=0 raises=0</c> (or the sum of what it did cover) and no <see cref="Failure"/>. A refusal ends it with <see cref="Failure"/> naming the stat, the outcome and ACE's line.</para>
/// </remarks>
public sealed class XpSpendRule
{
    /// <summary>The log category.</summary>
    public const string LogCategory = "xp";

    /// <summary>The raises one call may send. Lifestone's own bound: a long-neglected pool would otherwise be spent in one unbroken run.</summary>
    public const int MaxRaisesPerCall = 60;

    /// <summary>The seconds, on the session clock, after which one call sends no further raise. Lifestone's own bound.</summary>
    public const double MaxSecondsPerCall = 120;

    private readonly XpTableData _table;
    private readonly ILogSink? _log;
    private IGameSessionView? _view;
    private IActionApi? _actions;
    private SpendingPlan? _plan;
    private readonly HashSet<(MageStatKind Kind, uint Id)> _skipped = [];
    private readonly HashSet<(MageStatKind Kind, uint Id)> _unaffordable = [];
    private (string Name, int Ranks, ulong Cost)? _cheapest;
    private long _pool;
    private ActionHandle? _pending;
    private StatTarget? _pendingTarget;
    private ulong _pendingCost;
    private double _start;
    private bool _begun;

    /// <summary>A rule over the XpTable.</summary>
    /// <param name="table">The dats' XpTable (the planner's and the prices').</param>
    /// <param name="log">The log, or null.</param>
    /// <param name="maxRaises">The raises one call may send (default <see cref="MaxRaisesPerCall"/>).</param>
    /// <param name="maxSeconds">The time bound of one call (default <see cref="MaxSecondsPerCall"/>).</param>
    /// <param name="followLevel">M5 7 F3 (D87): plan with the shares of the character's level (<see cref="MageTemplate.SharesAt"/>) and leave out the skills of <see cref="MageTemplate.TrainedLater"/> it has not trained (the soak mage at level 80 has no Item Enchantment); false (the default): the D19 split over every skill.</param>
    public XpSpendRule(XpTableData table, ILogSink? log = null, int maxRaises = MaxRaisesPerCall, double maxSeconds = MaxSecondsPerCall, bool followLevel = false)
    {
        _followLevel = followLevel;
        ArgumentNullException.ThrowIfNull(table);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRaises, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSeconds);
        _table = table;
        _log = log;
        MaxRaises = maxRaises;
        MaxSeconds = maxSeconds;
    }

    /// <summary>The raises one call may send.</summary>
    public int MaxRaises { get; }

    /// <summary>The time bound of one call, in session-clock seconds.</summary>
    public double MaxSeconds { get; }

    /// <summary>True once the run has ended (<see cref="End"/> says why); false before <c>Begin</c> and while it runs.</summary>
    public bool Done { get; private set; }

    /// <summary>Why the run ended, or <see cref="XpSpendEnd.None"/> while it runs.</summary>
    public XpSpendEnd End { get; private set; }

    /// <summary>The experience spent by the raises this call confirmed.</summary>
    public long SpentXp { get; private set; }

    /// <summary>The raises this call confirmed.</summary>
    public int Raises { get; private set; }

    /// <summary>The stats the plan lists above the character's ranks that were skipped (untrained, missing, at the rank cap, or priced inconsistently).</summary>
    public int Skipped => _skipped.Count;

    /// <summary>The first skip, named, or null.</summary>
    public string? FirstSkip { get; private set; }

    /// <summary>The stats the plan raises whose next rank the pool did not cover, each named once this call and passed over for the next stat of the order (not skips: a later call may cover them).</summary>
    public int Unaffordable => _unaffordable.Count;

    /// <summary>The first stat the pool did not cover, as its log line names it ("... does not cover the next rank of X (a -> b, n)"), or null.</summary>
    public string? FirstUnaffordable { get; private set; }

    /// <summary>
    /// When the run ended <see cref="XpSpendEnd.PoolCoversNothing"/>: the unassigned experience and the price of the cheapest next rank of the stats the plan still raises
    /// (<c>xp_pool=&lt;n&gt; next_rank_xp=&lt;n&gt; (&lt;stat&gt; a -&gt; b)</c>), so a station can say why <c>spent_xp</c> is 0 (review 3-R7); null otherwise.
    /// </summary>
    public string? PoolNote => End == XpSpendEnd.PoolCoversNothing && _cheapest is { } c
        ? string.Create(CultureInfo.InvariantCulture, $"xp_pool={_pool} next_rank_xp={c.Cost} ({c.Name} {c.Ranks} -> {c.Ranks + 1})")
        : null;

    /// <summary>The named reason the run could not go on (a refused raise, an unread character), or null; a pool that covers nothing is not a failure.</summary>
    public string? Failure { get; private set; }

    /// <summary>The plan this call spends toward (for the earned experience at <c>Begin</c>), or null before it.</summary>
    public SpendingPlan? Plan => _plan;

    /// <summary>The verdict fields: <c>spent_xp=&lt;sum&gt; raises=&lt;n&gt;</c>.</summary>
    public string Fields => string.Create(CultureInfo.InvariantCulture, $"spent_xp={SpentXp} raises={Raises}");

    /// <summary>Begins a call over the session, sending through its own Action API.</summary>
    /// <param name="view">The session view (the read model and the clock).</param>
    public void Begin(IGameSessionView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Begin(view, view.Actions);
    }

    /// <summary>Begins a call: the fields are zeroed, the plan is made for the experience earned so far, and the next <see cref="Tick"/> sends the first raise the pool covers.</summary>
    /// <param name="view">The session view (the read model and the clock).</param>
    /// <param name="actions">The Action API the raises go through.</param>
    public void Begin(IGameSessionView view, IActionApi actions)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(actions);
        _view = view;
        _actions = actions;
        _pending = null;
        _pendingTarget = null;
        _start = view.Clock.Now;
        _begun = true;
        Done = false;
        End = XpSpendEnd.None;
        SpentXp = 0;
        Raises = 0;
        _skipped.Clear();
        _unaffordable.Clear();
        _cheapest = null;
        _pool = 0;
        FirstUnaffordable = null;
        FirstSkip = null;
        Failure = null;
        var q = view.State.Self.Qualities;
        _plan = q.IsKnown ? PlanFor(q) : null;
    }

    /// <summary>
    /// The plan for the character's experience (M5 7 F3, D87): the shares of its level (<see cref="MageTemplate.SharesAt"/>) over a plan that leaves out the skills of <see cref="MageTemplate.TrainedLater"/> the character has not trained, so
    /// the share of a skill the build's credits could not pay for goes to the stats that can be raised instead of being skipped.
    /// </summary>
    private SpendingPlan PlanFor(PlayerQualities q) => _followLevel
        ? MageSpendingPlanner.Compute(
            _table,
            (ulong)Math.Max(0, q.TotalExperience),
            [.. MageTemplate.TrainedLater.Where(id => !q.TryGetSkill(id, out var skill) || skill.AdvancementClass < HeadlessCharacter.TrainedSkill)],
            MageTemplate.SharesAt(q.Level),
            q.TryGetSkill(MageTemplate.MeleeDefense, out var melee) && melee.AdvancementClass >= HeadlessCharacter.TrainedSkill ? MageBuild.Gate126 : MageBuild.Traditional)
        : MageSpendingPlanner.Compute(_table, (ulong)Math.Max(0, q.TotalExperience));

    private readonly bool _followLevel;

    /// <summary>Advances the run by one step; true once it is <see cref="Done"/>. Call it every frame while it runs.</summary>
    public bool Tick()
    {
        if (!_begun || Done)
        {
            return Done;
        }

        var q = _view!.State.Self.Qualities;
        if (!q.IsKnown)
        {
            return Finish(XpSpendEnd.Unread, "the character's description has not arrived: its stats and experience are unknown");
        }

        if (_pending is { } handle)
        {
            if (!handle.IsCompleted)
            {
                return false;
            }

            var outcome = handle.Outcome!.Value;
            var target = _pendingTarget!;
            _pending = null;
            if (outcome.Kind == ActionOutcomeKind.DryRun)
            {
                return Finish(XpSpendEnd.DryRun, null);
            }

            if (!outcome.Succeeded)
            {
                return Finish(XpSpendEnd.Refused, string.Create(CultureInfo.InvariantCulture, $"the raise of {target.Name} for {_pendingCost:N0} experience ended {outcome.Kind}{(outcome.Code != 0 ? $" (0x{outcome.Code:X4})" : string.Empty)}: {outcome.Detail}"));
            }

            SpentXp += (long)_pendingCost;
            Raises++;
        }

        _plan ??= PlanFor(q);
        if (Raises >= MaxRaises)
        {
            return Finish(XpSpendEnd.RaiseCap, null);
        }

        var now = _view.Clock.Now;
        if (now - _start > MaxSeconds)
        {
            return Finish(XpSpendEnd.TimeBound, null);
        }

        switch (Next(q, out var next, out var ranks, out var cost))
        {
            case Pick.PlanReached:
                return Finish(XpSpendEnd.PlanReached, null);
            case Pick.PoolCoversNothing:
                _pool = q.AvailableExperience;
                return Finish(XpSpendEnd.PoolCoversNothing, null);
        }

        _log?.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"xp: raise {next.Name} {ranks} -> {ranks + 1} for {cost:N0} of {q.AvailableExperience:N0} unassigned"));
        _pendingTarget = next;
        _pendingCost = cost;
        _pending = next.Kind switch
        {
            MageStatKind.Attribute => _actions!.RaiseAttribute(next.Id, (uint)cost, ActionSource.Automation),
            MageStatKind.Vital => _actions!.RaiseVital(next.Id, (uint)cost, ActionSource.Automation),
            _ => _actions!.RaiseSkill(next.Id, (uint)cost, ActionSource.Automation),
        };
        return false;
    }

    /// <summary>What <see cref="Next"/> found.</summary>
    private enum Pick
    {
        /// <summary>A stat the plan still raises, whose next rank the pool covers.</summary>
        Found,

        /// <summary>Stats remain under their target but the pool covers none of their next ranks.</summary>
        PoolCoversNothing,

        /// <summary>No stat that can be raised is under its target.</summary>
        PlanReached,
    }

    /// <summary>
    /// The first stat of the plan, in its order, whose ranks are under its target, that can be raised and whose next rank the pool covers: that rank's price. Skips (and counts) a stat that cannot be raised, and names (once)
    /// a stat the pool does not cover, going on down the order. The cheapest next rank of the stats not covered is kept for <see cref="PoolNote"/>.
    /// </summary>
    private Pick Next(PlayerQualities q, out StatTarget target, out int ranks, out ulong cost)
    {
        var pool = q.AvailableExperience;
        var under = 0;
        _cheapest = null;
        foreach (var t in _plan!.Targets)
        {
            if (!Read(q, t, out ranks, out var spent, out var list, out var why))
            {
                if (ranks < t.Ranks)
                {
                    Skip(t, why);
                }

                continue;
            }

            if (ranks >= t.Ranks)
            {
                continue;
            }

            if (ranks + 1 >= list!.Count)
            {
                Skip(t, "it is at its rank cap");
                continue;
            }

            var goal = list[ranks + 1];
            if (spent >= goal)
            {
                Skip(t, string.Create(CultureInfo.InvariantCulture, $"it shows {ranks} ranks but {spent:N0} spent, at or over the {goal:N0} of rank {ranks + 1} (the read model and the XpTable disagree)"));
                continue;
            }

            var price = goal - spent;
            if (pool < (long)price)
            {
                under++;
                NameUnaffordable(t, ranks, price, pool);
                if (_cheapest is not { } c || price < c.Cost)
                {
                    _cheapest = (t.Name, ranks, price);
                }

                continue;
            }

            target = t;
            cost = price;
            return Pick.Found;
        }

        target = null!;
        ranks = 0;
        cost = 0;
        return under > 0 ? Pick.PoolCoversNothing : Pick.PlanReached;
    }

    /// <summary>Names (once a call) a stat whose next rank the pool does not cover; the walk goes on to the next stat of the order.</summary>
    private void NameUnaffordable(StatTarget t, int ranks, ulong price, long pool)
    {
        if (!_unaffordable.Add((t.Kind, t.Id)))
        {
            return;
        }

        var text = string.Create(CultureInfo.InvariantCulture, $"xp: {pool:N0} unassigned does not cover the next rank of {t.Name} ({ranks} -> {ranks + 1}, {price:N0}); the next stat of the order is tried");
        FirstUnaffordable ??= text;
        _log?.Info(LogCategory, text);
    }

    /// <summary>Reads the stat's ranks, experience spent and price list. False (with <paramref name="why"/>) for a stat the character lacks or holds untrained.</summary>
    private bool Read(PlayerQualities q, StatTarget t, out int ranks, out uint spent, out IReadOnlyList<uint>? list, out string why)
    {
        (ranks, spent, list, why) = (0, 0, null, string.Empty);
        switch (t.Kind)
        {
            case MageStatKind.Attribute:
                if (!q.TryGetAttribute(t.Id, out var attribute))
                {
                    why = "the attribute is not in the character's description";
                    return false;
                }

                (ranks, spent, list) = ((int)attribute.Ranks, attribute.Experience, _table.AttributeXpList);
                return true;
            case MageStatKind.Vital:
                if (!q.TryGetVital(t.Id, out var vital))
                {
                    why = "the vital is not in the character's description";
                    return false;
                }

                (ranks, spent, list) = ((int)vital.Ranks, vital.Experience, _table.VitalXpList);
                return true;
            default:
                if (!q.TryGetSkill(t.Id, out var skill))
                {
                    why = "the skill is not in the character's description";
                    return false;
                }

                if (skill.AdvancementClass < SkillRules.TrainedClass)
                {
                    why = "the skill is untrained (ACE tells no refusal for it, so no raise is sent)";
                    return false;
                }

                (ranks, spent, list) = (skill.Ranks, skill.Experience, skill.AdvancementClass >= SkillRules.SpecializedClass ? _table.SpecializedSkillXpList : _table.TrainedSkillXpList);
                return true;
        }
    }

    private void Skip(StatTarget t, string why)
    {
        if (_skipped.Add((t.Kind, t.Id)) && FirstSkip is null)
        {
            FirstSkip = t.Name + ": " + why;
            _log?.Warn(LogCategory, "xp: skipped " + FirstSkip);
        }
    }

    private bool Finish(XpSpendEnd end, string? failure)
    {
        End = end;
        Done = true;
        Failure = failure;
        var text = string.Create(CultureInfo.InvariantCulture, $"xp: ended {end} ({Fields}{(Skipped > 0 ? $", {Skipped} skipped" : string.Empty)}{(PoolNote is { } note ? ", " + note : string.Empty)}){(failure is null ? string.Empty : ": " + failure)}");
        if (failure is null)
        {
            _log?.Info(LogCategory, text);
        }
        else
        {
            _log?.Warn(LogCategory, text);
        }

        return true;
    }
}

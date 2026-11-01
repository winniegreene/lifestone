using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// <see cref="XpSpendRule"/> on the real Action API over a scripted ACE (M5 item 3 m4; spec DoD 6): the raises follow the planner's order one rank and one action at a time, a pool that covers nothing is
/// <c>spent_xp=0</c> and no failure, a refusal is named and ends the run, a skill the planner does not raise is never touched, the bounds hold, and the fields read the sum. The XpTable is the synthetic linear one
/// (<see cref="ScriptedMage.SyntheticTable"/>): attributes cost 110 a rank, vitals 73, a trained skill 58, a specialised skill 23, so the prices are exact and the tests do not need the dats.
/// </summary>
public sealed class XpSpendRuleTests
{
    private static readonly XpTableData Table = ScriptedMage.SyntheticTable();
    private static byte _sequence = 50;

    /// <summary>A session view over an action harness (what a station holds).</summary>
    private sealed class View(ActionHarness h) : IGameSessionView
    {
        public GameState State => h.State;

        public IEventBus Events => h.R.Bus;

        public IActionApi Actions => h.Api;

        public PlayerTools Tools => h.Api.Tools;

        public SessionClock Clock => h.R.Clock;

        public ActionLog ActionLog => h.ActionLog;

        public LinkStats Link => default;
    }

    /// <summary>The scripted ACE: it holds the stats' ranks and spent experience, answers each raise the Action API sent as ACE does (the available experience first, then the stat's private update), or stays silent or refuses.</summary>
    private sealed class Server
    {
        private readonly ActionHarness _h;
        private readonly Dictionary<(MageStatKind Kind, uint Id), (uint Ranks, uint Spent, uint Class)> _stats = [];
        private int _seen;

        public Server(ActionHarness h, ulong total, long available, Func<StatTarget, (int Ranks, bool Untrained)>? start = null)
        {
            _h = h;
            Available = available;
            Plan = MageSpendingPlanner.Compute(Table, total);
            var attributes = new List<AttributeDesc>();
            var vitals = new List<VitalDesc>();
            var skills = new List<SkillDesc>();
            foreach (var t in Plan.Targets)
            {
                var (ranks, untrained) = start?.Invoke(t) ?? (0, false);
                var spent = ranks == 0 ? 0u : t.XpList(Table)[ranks];
                var cls = t.Kind != MageStatKind.Skill ? 0u : untrained ? 1u : t.Specialized ? 3u : 2u;
                _stats[(t.Kind, t.Id)] = ((uint)ranks, spent, cls);
                switch (t.Kind)
                {
                    case MageStatKind.Attribute:
                        attributes.Add(new AttributeDesc(t.Id, (uint)ranks, 100, spent));
                        break;
                    case MageStatKind.Vital:
                        vitals.Add(new VitalDesc(t.Id, (uint)ranks, 0, spent, 100));
                        break;
                    default:
                        skills.Add(new SkillDesc(t.Id, (ushort)ranks, 1, cls, spent, 0, 0, 0.0));
                        break;
                }
            }

            // A skill the plan does not raise, trained: Healing (20). It must never be raised.
            skills.Add(new SkillDesc(Healing, 0, 1, 2, 0, 0, 0, 0.0));
            _stats[(MageStatKind.Skill, Healing)] = (0, 0, 2);
            h.R.Reducer.OnPlayerDescription(
                h.R.Context,
                new GameEventHeader(ActionHarness.Amy, 1, GameEventTypes.PlayerDescription),
                new PlayerDescriptionMsg
                {
                    Int64Properties = System.Collections.Immutable.ImmutableDictionary<uint, long>.Empty
                        .Add(PlayerQualities.TotalExperienceProperty, (long)total)
                        .Add(PlayerQualities.AvailableExperienceProperty, available),
                    Attributes = [.. attributes],
                    Vitals = [.. vitals],
                    Skills = [.. skills],
                });
            h.Tick();
        }

        public const uint Healing = 20;

        public SpendingPlan Plan { get; }

        public long Available { get; private set; }

        /// <summary>Every raise ACE took, in order: the stat and the experience spent on it.</summary>
        public List<(MageStatKind Kind, uint Id, uint Xp)> Raised { get; } = [];

        /// <summary>True: ACE tells no answer (a skill's raise is never refused aloud; an over-spend is dropped).</summary>
        public bool Silent { get; set; }

        /// <summary>True: ACE tells its refusal line for an attribute or a vital instead of raising it.</summary>
        public bool Refuse { get; set; }

        public uint RanksOf(MageStatKind kind, uint id) => _stats[(kind, id)].Ranks;

        public void Pump()
        {
            var sends = _h.Net.Sends;
            while (_seen < sends.Count)
            {
                var send = sends[_seen++];
                var kind = send.Kind switch
                {
                    FakeSendKind.RaiseAttribute => MageStatKind.Attribute,
                    FakeSendKind.RaiseVital => MageStatKind.Vital,
                    FakeSendKind.RaiseSkill => MageStatKind.Skill,
                    _ => (MageStatKind?)null,
                };
                if (kind is null || Silent)
                {
                    continue;
                }

                var id = (uint)send.Guid;
                var xp = (uint)send.Amount;
                if (Refuse && kind != MageStatKind.Skill)
                {
                    var name = Plan.Targets.First(t => t.Kind == kind && t.Id == id).Name;
                    _h.ServerMessage($"Your attempt to raise {name} has failed.");
                    continue;
                }

                Raised.Add((kind.Value, id, xp));
                var (ranks, spent, cls) = _stats[(kind.Value, id)];
                spent += xp;
                var list = ListOf(kind.Value, cls);
                ranks = 0;
                while (ranks + 1 < list.Count && list[(int)ranks + 1] <= spent)
                {
                    ranks++;
                }

                _stats[(kind.Value, id)] = (ranks, spent, cls);
                Available -= xp;
                Update(PrivateUpdateKind.PropertyInt64, PlayerQualities.AvailableExperienceProperty, Available);
                switch (kind)
                {
                    case MageStatKind.Attribute:
                        Update(PrivateUpdateKind.Attribute, id, attribute: new AttributeDesc(id, ranks, 100, spent));
                        break;
                    case MageStatKind.Vital:
                        Update(PrivateUpdateKind.Vital, id, vital: new VitalDesc(id, ranks, 0, spent, 100));
                        break;
                    default:
                        Update(PrivateUpdateKind.Skill, id, skill: new SkillDesc(id, (ushort)ranks, 1, cls, spent, 0, 0, 0.0));
                        break;
                }
            }
        }

        private static IReadOnlyList<uint> ListOf(MageStatKind kind, uint cls) => kind switch
        {
            MageStatKind.Attribute => Table.AttributeXpList,
            MageStatKind.Vital => Table.VitalXpList,
            _ => cls >= 3 ? Table.SpecializedSkillXpList : Table.TrainedSkillXpList,
        };

        private void Update(PrivateUpdateKind kind, uint key, long value = 0, AttributeDesc attribute = default, VitalDesc vital = default, SkillDesc skill = default) =>
            _h.R.Reducer.OnPrivateUpdate(_h.R.Context, new PrivateUpdateMsg(kind, ++_sequence, key, value, attribute, vital, skill));
    }

    private sealed class Rig
    {
        public Rig(ulong total, long available, Func<StatTarget, (int Ranks, bool Untrained)>? start = null, int maxRaises = 100_000, double maxSeconds = 100_000)
        {
            H = new ActionHarness();
            H.Present();
            S = new Server(H, total, available, start);
            View = new View(H);
            Rule = new XpSpendRule(Table, H.R.Log, maxRaises, maxSeconds);
        }

        public ActionHarness H { get; }

        public Server S { get; }

        public View View { get; }

        public XpSpendRule Rule { get; }

        /// <summary>One frame of a station: the rule steps, the Action API sends, the scripted ACE answers, the Action API completes.</summary>
        public bool Frame(double dt = 0.1)
        {
            var done = Rule.Tick();
            H.Tick(dt);
            S.Pump();
            H.Tick();
            return done;
        }

        public void Run(int frames = 20_000, double dt = 0.1)
        {
            for (var i = 0; i < frames && !Frame(dt); i++)
            {
            }
        }
    }

    [Fact]
    public void The_raises_follow_the_planners_order_one_rank_and_one_action_at_a_time()
    {
        var rig = new Rig(total: 6_000, available: 6_000);
        var plan = rig.S.Plan;
        Assert.True(plan.Spent > 0);
        var expected = new List<(MageStatKind Kind, uint Id)>();
        foreach (var t in plan.Targets)
        {
            for (var rank = 0; rank < t.Ranks; rank++)
            {
                expected.Add((t.Kind, t.Id));
            }
        }

        Assert.True(expected.Count > 20, "a pool big enough for a real run");
        rig.Rule.Begin(rig.View);
        var maxInFlight = 0;
        for (var i = 0; i < 20_000 && !rig.Rule.Done; i++)
        {
            rig.Frame();
            maxInFlight = Math.Max(maxInFlight, rig.H.Net.Sends.Count(s => s.Kind is FakeSendKind.RaiseAttribute or FakeSendKind.RaiseVital or FakeSendKind.RaiseSkill) - rig.S.Raised.Count);
        }

        Assert.True(rig.Rule.Done);
        Assert.Equal(XpSpendEnd.PlanReached, rig.Rule.End);
        Assert.Null(rig.Rule.Failure);
        Assert.Equal(expected, rig.S.Raised.Select(r => (r.Kind, r.Id)).ToList()); // the plan's order, a rank at a time
        Assert.Equal(expected.Count, rig.Rule.Raises);
        Assert.Equal((long)plan.Spent, rig.Rule.SpentXp);
        Assert.Equal(6_000 - (long)plan.Spent, rig.H.State.Self.Qualities.AvailableExperience);
        Assert.Equal($"spent_xp={plan.Spent} raises={expected.Count}", rig.Rule.Fields);
        Assert.Equal(MageStatKind.Skill, rig.S.Raised[0].Kind);
        Assert.Equal(MageTemplate.WarMagic, rig.S.Raised[0].Id); // the first target of the order
        Assert.Equal(23u, rig.S.Raised[0].Xp); // a specialised skill's first rank is priced by the specialised list
        Assert.True(maxInFlight <= 1, "one action in flight at a time");
        foreach (var t in plan.Targets)
        {
            Assert.Equal((uint)t.Ranks, rig.S.RanksOf(t.Kind, t.Id));
        }
    }

    [Fact]
    public void A_pool_that_covers_nothing_is_spent_0_and_not_a_failure()
    {
        var rig = new Rig(total: 6_000, available: 20); // War Magic's first rank costs 23
        rig.Rule.Begin(rig.View, rig.H.Api);
        rig.Run();

        Assert.True(rig.Rule.Done);
        Assert.Equal(XpSpendEnd.PoolCoversNothing, rig.Rule.End);
        Assert.Null(rig.Rule.Failure);
        Assert.Equal("spent_xp=0 raises=0", rig.Rule.Fields);
        Assert.DoesNotContain(rig.H.Net.Sends, s => s.Kind is FakeSendKind.RaiseAttribute or FakeSendKind.RaiseVital or FakeSendKind.RaiseSkill);
        Assert.Contains(rig.H.Log.Lines, l => l.Contains("does not cover the next rank of War Magic", StringComparison.Ordinal));
        Assert.Equal("xp_pool=20 next_rank_xp=23 (War Magic 0 -> 1)", rig.Rule.PoolNote); // review 3-R7: the station can say why spent_xp is 0
        Assert.Contains(rig.H.Log.Lines, l => l.Contains("xp_pool=20 next_rank_xp=23", StringComparison.Ordinal));
        Assert.True(rig.Rule.Unaffordable > 1, "every planned stat the pool does not cover is passed over, not only the first");
    }

    [Fact]
    public void A_pool_that_covers_some_ranks_buys_them_and_ends_when_no_planned_rank_is_covered()
    {
        var rig = new Rig(total: 6_000, available: 23 + 23 + 20); // two War Magic ranks and 20 over: the third costs 23, and nothing else in the plan costs under 58
        rig.Rule.Begin(rig.View);
        rig.Run();

        Assert.Equal(XpSpendEnd.PoolCoversNothing, rig.Rule.End);
        Assert.Null(rig.Rule.Failure);
        Assert.Equal("spent_xp=46 raises=2", rig.Rule.Fields);
        Assert.All(rig.S.Raised, r => Assert.Equal((MageStatKind.Skill, MageTemplate.WarMagic), (r.Kind, r.Id)));
        Assert.Equal(20, rig.H.State.Self.Qualities.AvailableExperience);
        Assert.Equal("xp_pool=20 next_rank_xp=23 (War Magic 2 -> 3)", rig.Rule.PoolNote);
    }

    [Fact]
    public void A_stat_the_pool_cannot_cover_is_named_once_and_passed_over_for_the_next_stat_of_the_order_it_does_cover()
    {
        // Review 3-R7: Focus (an attribute, 110 a rank) and Mana Conversion (a trained skill, 58 a rank) are each one rank under the plan; the pool is 70. The order names Focus first: 110 is not covered,
        // so Focus is passed over (named once) and Mana Conversion is raised; the 12 left cover neither, and the run ends with the pool covering nothing, not as a failure.
        var plan = MageSpendingPlanner.Compute(Table, 6_000);
        var focus = plan.Targets.First(t => t.Kind == MageStatKind.Attribute && t.Id == MageTemplate.Focus);
        var conversion = plan.Targets.First(t => t.Kind == MageStatKind.Skill && t.Id == MageTemplate.ManaConversion);
        Assert.True(focus.Ranks >= 1 && conversion.Ranks >= 1, "both stats have a rank to buy");
        var order = plan.Targets.ToList();
        Assert.True(order.IndexOf(focus) < order.IndexOf(conversion), "Focus comes first in the planner's order");

        var rig = new Rig(total: 6_000, available: 70, start: t => ((t.Kind, t.Id) == (MageStatKind.Attribute, MageTemplate.Focus) || (t.Kind, t.Id) == (MageStatKind.Skill, MageTemplate.ManaConversion)) ? (t.Ranks - 1, false) : (t.Ranks, false));
        rig.Rule.Begin(rig.View);
        rig.Run();

        Assert.Equal(XpSpendEnd.PoolCoversNothing, rig.Rule.End);
        Assert.Null(rig.Rule.Failure);
        Assert.Equal("spent_xp=58 raises=1", rig.Rule.Fields);
        Assert.Equal([(MageStatKind.Skill, MageTemplate.ManaConversion)], rig.S.Raised.Select(r => (r.Kind, r.Id)).ToList());
        Assert.Equal(1, rig.Rule.Unaffordable);
        Assert.Contains("does not cover the next rank of Focus", rig.Rule.FirstUnaffordable, StringComparison.Ordinal);
        Assert.Equal(1, rig.H.Log.Lines.Count(l => l.Contains("does not cover the next rank of Focus", StringComparison.Ordinal))); // named once, not once a frame
        Assert.Equal($"xp_pool=12 next_rank_xp=110 (Focus {focus.Ranks - 1} -> {focus.Ranks})", rig.Rule.PoolNote);
        Assert.Equal(0, rig.Rule.Skipped); // an unaffordable rank is not a skip: the pool may cover it on a later call
    }

    [Fact]
    public void A_skill_the_plan_does_not_raise_is_never_raised_and_a_planned_stat_with_no_ranks_is_not_either()
    {
        var rig = new Rig(total: 6_000, available: 6_000);
        rig.Rule.Begin(rig.View);
        rig.Run();

        var planned = rig.S.Plan.Targets.Where(t => t.Ranks > 0).Select(t => (t.Kind, t.Id)).ToHashSet();
        Assert.NotEmpty(planned);
        Assert.DoesNotContain(rig.S.Raised, r => r.Kind == MageStatKind.Skill && r.Id == Server.Healing); // trained, held, not in the plan
        Assert.All(rig.S.Raised, r => Assert.Contains((r.Kind, r.Id), planned));
        Assert.Equal(0u, rig.S.RanksOf(MageStatKind.Skill, Server.Healing));
    }

    [Fact]
    public void A_stat_already_at_its_planned_rank_is_left_alone_and_the_next_one_is_raised()
    {
        // The character already holds every skill at its planned ranks: the first raise is the first attribute of the order (Focus).
        var plan = MageSpendingPlanner.Compute(Table, 6_000);
        var rig = new Rig(total: 6_000, available: 6_000, start: t => t.Kind == MageStatKind.Skill ? (t.Ranks, false) : (0, false));
        rig.Rule.Begin(rig.View);
        rig.Run();

        Assert.Contains(plan.Targets, t => t.Kind == MageStatKind.Skill && t.Ranks > 0);
        Assert.DoesNotContain(rig.S.Raised, r => r.Kind == MageStatKind.Skill);
        Assert.Equal((MageStatKind.Attribute, MageTemplate.Focus), (rig.S.Raised[0].Kind, rig.S.Raised[0].Id));
        Assert.Equal(110u, rig.S.Raised[0].Xp); // an attribute's first rank
    }

    [Fact]
    public void A_refusal_is_named_ends_the_run_and_is_not_retried()
    {
        var rig = new Rig(total: 6_000, available: 6_000, start: t => t.Kind == MageStatKind.Skill ? (t.Ranks, false) : (0, false));
        rig.S.Refuse = true;
        rig.Rule.Begin(rig.View);
        rig.Run();

        Assert.Equal(XpSpendEnd.Refused, rig.Rule.End);
        Assert.NotNull(rig.Rule.Failure);
        Assert.Contains("raise of Focus for 110 experience", rig.Rule.Failure, StringComparison.Ordinal);
        Assert.Contains("ServerError", rig.Rule.Failure, StringComparison.Ordinal);
        Assert.Contains("Your attempt to raise Focus has failed.", rig.Rule.Failure, StringComparison.Ordinal);
        Assert.Equal("spent_xp=0 raises=0", rig.Rule.Fields);
        Assert.Single(rig.H.Net.Sends, s => s.Kind == FakeSendKind.RaiseAttribute); // one send, never resent
        Assert.Contains(rig.H.Log.Lines, l => l.Contains("xp: ended Refused", StringComparison.Ordinal));
    }

    [Fact]
    public void A_raise_ACE_never_answers_times_out_and_is_named_as_such()
    {
        var rig = new Rig(total: 6_000, available: 6_000);
        rig.S.Silent = true; // ACE tells no refusal for a skill: it drops the raise
        rig.Rule.Begin(rig.View);
        rig.Run(frames: 200, dt: 0.1);

        Assert.Equal(XpSpendEnd.Refused, rig.Rule.End);
        Assert.Contains("TimedOut", rig.Rule.Failure, StringComparison.Ordinal);
        Assert.Contains("War Magic", rig.Rule.Failure, StringComparison.Ordinal);
        Assert.Equal("spent_xp=0 raises=0", rig.Rule.Fields);
    }

    [Fact]
    public void The_raise_cap_bounds_a_call_and_another_call_goes_on()
    {
        var rig = new Rig(total: 6_000, available: 6_000, maxRaises: 3);
        rig.Rule.Begin(rig.View);
        rig.Run();

        Assert.Equal(XpSpendEnd.RaiseCap, rig.Rule.End);
        Assert.Null(rig.Rule.Failure);
        Assert.Equal(3, rig.Rule.Raises);
        Assert.Equal("spent_xp=69 raises=3", rig.Rule.Fields); // three specialised War Magic ranks

        rig.Rule.Begin(rig.View);
        rig.Run();
        Assert.Equal(XpSpendEnd.RaiseCap, rig.Rule.End);
        Assert.Equal(3, rig.Rule.Raises);
        Assert.Equal(6, rig.S.Raised.Count);
        Assert.Equal(XpSpendRule.MaxRaisesPerCall, new XpSpendRule(Table).MaxRaises);
        Assert.Equal(XpSpendRule.MaxSecondsPerCall, new XpSpendRule(Table).MaxSeconds);
    }

    [Fact]
    public void The_time_bound_sends_no_further_raise_after_it()
    {
        var rig = new Rig(total: 6_000, available: 6_000, maxSeconds: 1.0);
        rig.Rule.Begin(rig.View);
        rig.Run(frames: 500, dt: 0.1);

        Assert.Equal(XpSpendEnd.TimeBound, rig.Rule.End);
        Assert.Null(rig.Rule.Failure);
        Assert.InRange(rig.Rule.Raises, 1, 12);
        var raisesAtEnd = rig.Rule.Raises;
        rig.Frame();
        rig.Frame();
        Assert.Equal(raisesAtEnd, rig.S.Raised.Count);
    }

    [Fact]
    public void A_skill_the_character_holds_untrained_is_skipped_counted_and_never_sent()
    {
        var rig = new Rig(total: 6_000, available: 6_000, start: t => t.Id == MageTemplate.ManaConversion ? (0, true) : (0, false));
        rig.Rule.Begin(rig.View);
        rig.Run();

        Assert.True(rig.S.Plan.Targets.First(t => t.Id == MageTemplate.ManaConversion).Ranks > 0); // the synthetic table prices the specialised War Magic cheapest, so the other magic skills have no ranks; Mana Conversion's own share does
        Assert.Equal(1, rig.Rule.Skipped);
        Assert.StartsWith("Mana Conversion: the skill is untrained", rig.Rule.FirstSkip, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.S.Raised, r => r.Kind == MageStatKind.Skill && r.Id == MageTemplate.ManaConversion);
        Assert.Contains(rig.S.Raised, r => r.Kind == MageStatKind.Vital && r.Id == MageTemplate.MaxMana); // the rest of the order goes on
    }

    [Fact]
    public void A_character_whose_description_has_not_arrived_is_a_named_stop_with_nothing_sent()
    {
        var h = new ActionHarness();
        h.Present(); // no PlayerDescription
        var rule = new XpSpendRule(Table, h.R.Log);
        rule.Begin(new View(h));

        Assert.True(rule.Tick());
        Assert.Equal(XpSpendEnd.Unread, rule.End);
        Assert.Contains("description has not arrived", rule.Failure, StringComparison.Ordinal);
        Assert.Equal("spent_xp=0 raises=0", rule.Fields);
        Assert.DoesNotContain(h.Net.Sends, s => s.Kind is FakeSendKind.RaiseAttribute or FakeSendKind.RaiseVital or FakeSendKind.RaiseSkill);
    }

    [Fact]
    public void The_pool_note_exists_only_for_a_run_that_ended_with_the_pool_covering_nothing()
    {
        Assert.Null(new XpSpendRule(Table).PoolNote);
        var rig = new Rig(total: 6_000, available: 6_000);
        rig.Rule.Begin(rig.View);
        rig.Frame();
        Assert.Null(rig.Rule.PoolNote); // running
        rig.Run();
        Assert.Equal(XpSpendEnd.PlanReached, rig.Rule.End);
        Assert.Null(rig.Rule.PoolNote); // the plan is reached: nothing is left that a bigger pool would buy
        Assert.Equal(0, rig.Rule.Unaffordable);
        Assert.Null(rig.Rule.FirstUnaffordable);
    }

    [Fact]
    public void A_second_call_names_the_stats_it_cannot_cover_again_and_counts_from_zero()
    {
        var rig = new Rig(total: 6_000, available: 20);
        rig.Rule.Begin(rig.View);
        rig.Run();
        var first = rig.Rule.Unaffordable;
        Assert.True(first > 1);
        Assert.Equal(1, rig.H.Log.Lines.Count(l => l.Contains("does not cover the next rank of War Magic", StringComparison.Ordinal)));

        rig.Rule.Begin(rig.View);
        Assert.Equal(0, rig.Rule.Unaffordable);
        Assert.Null(rig.Rule.FirstUnaffordable);
        rig.Run();
        Assert.Equal(first, rig.Rule.Unaffordable);
        Assert.Equal(2, rig.H.Log.Lines.Count(l => l.Contains("does not cover the next rank of War Magic", StringComparison.Ordinal))); // once a call, not once a frame
        Assert.Equal("spent_xp=0 raises=0", rig.Rule.Fields);
    }

    [Fact]
    public void A_begun_rule_that_is_done_stays_done_and_a_rule_never_begun_is_not()
    {
        var never = new XpSpendRule(Table);
        Assert.False(never.Tick());
        Assert.False(never.Done);
        Assert.Equal("spent_xp=0 raises=0", never.Fields);

        var rig = new Rig(total: 6_000, available: 20);
        rig.Rule.Begin(rig.View);
        rig.Run();
        Assert.True(rig.Rule.Tick());
        Assert.True(rig.Rule.Tick());
        Assert.Equal(XpSpendEnd.PoolCoversNothing, rig.Rule.End);
    }
}

/// <summary>
/// The forced trip's arithmetic against the retail XpTable (M5 item 3 fix F3, review 3-R7): a mage built to <see cref="MageSpendingPlanner"/>'s plan for level 126's experience, plus the experience of three den kills (about
/// 30,000 each, spec section 5.5), holds an unassigned pool far under the cheapest next rank of the plan, and the plan for the larger total is the same plan. So the gate's trip ends <c>spent_xp=0</c> by arithmetic, not by a
/// rule that stops early; the rule's skip (above) only matters once a pool covers one planned rank.
/// </summary>
public sealed class XpSpendRuleDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const ulong KillXp = 30_000UL;
    private const int Kills = 3;

    /// <summary>The six creation-trained skills start at 5 ranks / 526 XP each, free: about 3,156 XP more stays unspent than the plan from nothing leaves (<see cref="MageTemplate.XpRemainder"/>).</summary>
    private const long CreationBonus = 3_156;

    [RequiresDatsFact]
    public void A_mage_built_to_the_plan_plus_three_kills_covers_no_planned_rank_so_the_gates_forced_trip_spends_nothing()
    {
        var table = XpTableData.Load(fixture.Dats);
        var built = MageSpendingPlanner.Compute(table, MageTemplate.Level126Xp);
        var earned = MageTemplate.Level126Xp + Kills * KillXp;
        var plan = MageSpendingPlanner.Compute(table, earned);
        var pool = (long)MageTemplate.Remainder(table) + CreationBonus + (long)(Kills * KillXp);

        ulong cheapest = ulong.MaxValue;
        var cheapestName = string.Empty;
        for (var i = 0; i < plan.Targets.Count; i++)
        {
            var target = plan.Targets[i];
            Assert.Equal(built.Targets[i].Ranks, target.Ranks); // the plan for three kills more experience is the same plan: no stat is under its target
            var list = target.XpList(table);
            if (target.Ranks + 1 < list.Count && list[target.Ranks + 1] - list[target.Ranks] is var price && price < cheapest)
            {
                (cheapest, cheapestName) = (price, target.Name);
            }
        }

        var killsNeeded = (int)Math.Ceiling(((double)cheapest - (MageTemplate.Remainder(table) + CreationBonus)) / KillXp);
        output.WriteLine($"pool after {Kills} kills {pool:N0}; cheapest next planned rank {cheapest:N0} ({cheapestName}); kills for it {killsNeeded}; plan spent {plan.Spent:N0} of {earned:N0}");
        Assert.Equal((ulong)MageTemplate.XpSpent, plan.Spent);
        Assert.True((ulong)pool < cheapest, $"the pool {pool:N0} would cover the cheapest next rank {cheapest:N0} ({cheapestName})");
        Assert.Equal("Melee Defense", cheapestName);
        Assert.Equal(2_306_880UL, cheapest);
        Assert.Equal(45, killsNeeded); // about 45 kills at 30,000 before one planned rank is covered: the autopilot lane's 30 minutes, not the gate's three kills
    }
}

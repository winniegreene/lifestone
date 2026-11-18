using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Threat;

/// <summary>
/// M6 16 E2a (docs/automation-engine.md E2; m6-plan.md D139): the threat board. One picture a session of who attacks her and who is near, fed once from the event bus, answering queries whose parameters are the differences
/// between today's detectors. Each detector's question is one query here, and the query answers as the detector does in the states both can see; where two detectors disagree, the two queries do, and the state is pinned.
/// </summary>
public sealed class ThreatBoardTests
{
    private const string Guard = "Tusker Guard";

    /// <summary>The outer loop's watch and the board's answer to its question, compared by value: the same verdict, the same attack clock, the same creature at the same distance.</summary>
    private static void SameAsOuterLoop(ThreatRig t, HostileWatch watch, string state)
    {
        var said = watch.InReach(t.Now, out _);
        var board = t.AskInReach();
        Assert.True(said == board.Any, $"{state}: the watch says {said}, the board {board}");
        if (!said)
        {
            return;
        }

        if (t.Now - watch.LastAttackAt < HostileWatch.AttackQuietSeconds)
        {
            Assert.Equal((ThreatAnswerKind.Attacked, watch.LastAttackAt, watch.LastAttacker), (board.Kind, board.At, board.Name));
            return;
        }

        Assert.True(watch.Nearest(out var guid, out var name, out var metres), state);
        Assert.Equal((ThreatAnswerKind.Standing, guid, name, metres), (board.Kind, board.Guid, board.Name, board.Metres));
    }

    /// <summary>The travel legs' read and the board's answer to its question, compared by value: as many attackers present, the nearest at the same distance.</summary>
    private static void SameAsTravelLeg(ThreatRig t, AttackWatch watch, string state)
    {
        var present = t.Present(watch);
        var found = HostileReach.TryNearest(t.View, ThreatRig.LegReach, watch.HeardWithin(ThreatRig.LegSeconds), out var guid, out _, out var metres);
        var board = t.AskOnHer();
        Assert.True(present.Count == board.Count && found == board.Any, $"{state}: the leg reads {present.Count} present, the board {board}");
        if (found)
        {
            Assert.Equal((guid, metres), (board.Guid, board.Metres));
            Assert.Equal(present.Min(p => p.Metres), board.Metres);
        }
    }

    [Fact]
    public void The_board_is_the_sessions_and_one_however_often_it_is_asked_for()
    {
        var t = new ThreatRig();
        var board = ThreatBoard.For(t.View);
        Assert.Same(board, ThreatBoard.For(t.View));
        Assert.Same(board, ThreatBoard.For(new FightManaTestsView(t))); // another view of the same session's state: the same board
        Assert.NotSame(board, new ThreatRig().Board); // another session has its own
        Assert.Equal(t.Now, board.HearingSince);
        Assert.Equal((0, 0L, 0L), (board.AttackerCount, board.Hits, board.Attacks));
    }

    /// <summary>A second view over the rig's session (a host hands scenarios views of its own).</summary>
    private sealed class FightManaTestsView(ThreatRig t) : IGameSessionView
    {
        public GameState State => t.View.State;

        public IEventBus Events => t.View.Events;

        public IActionApi Actions => t.View.Actions;

        public PlayerTools Tools => t.View.Tools;

        public SessionClock Clock => t.View.Clock;

        public ActionLog ActionLog => t.View.ActionLog;

        public Lifestone.Net.Diagnostics.LinkStats Link => default;
    }

    [Fact]
    public void The_outer_loops_question_is_one_query_and_answers_as_its_watch_does()
    {
        var t = new ThreatRig();
        _ = t.Board;
        using var watch = t.OuterWatch();
        SameAsOuterLoop(t, watch, "an empty floor");
        Assert.False(t.AskInReach().Any);

        var far = t.Creature(Guard, t.Listed, 22);
        SameAsOuterLoop(t, watch, "a target 22 m away, outside the 20 m reach");
        Assert.False(t.AskInReach().Any);

        t.Creature("Stranger", ThreatRig.Unlisted, 6);
        SameAsOuterLoop(t, watch, "a creature off the ground's list 6 m away");
        Assert.False(t.AskInReach().Any);

        var near = t.Creature(Guard, t.Listed, 12);
        var nearer = t.Creature(Guard, t.Listed, 6);
        SameAsOuterLoop(t, watch, "two targets within the reach");
        Assert.Equal((ThreatAnswerKind.Standing, nearer, 2), (t.AskInReach().Kind, t.AskInReach().Guid, t.AskInReach().Count));

        t.Gone(nearer);
        SameAsOuterLoop(t, watch, "the nearer one gone");
        Assert.Equal(near, t.AskInReach().Guid);
        t.Gone(near);

        t.Evade(Guard);
        SameAsOuterLoop(t, watch, "an evaded swing: an attack heard, no hit");
        Assert.False(t.AskInReach().Any);

        t.Magic("You resist the spell cast by Tusker Guard");
        t.Magic("Tusker Guard casts Drain Stamina Other II and drains 9 points of your stamina.");
        SameAsOuterLoop(t, watch, "a resisted spell and a stamina drain");
        Assert.False(t.AskInReach().Any);

        t.Wait(1);
        t.Hit(Guard, 12);
        t.Wait(4);
        SameAsOuterLoop(t, watch, "a hit 4 s ago");
        Assert.Equal((ThreatAnswerKind.Attacked, Guard), (t.AskInReach().Kind, t.AskInReach().Name));

        t.Wait(5.9);
        SameAsOuterLoop(t, watch, "a hit 9.9 s ago");
        Assert.True(t.AskInReach().Any);
        t.Wait(0.2);
        SameAsOuterLoop(t, watch, "a hit 10.1 s ago");
        Assert.False(t.AskInReach().Any);

        t.Magic("Tusker Guard scorches you for 15 points with Flame Bolt III.");
        SameAsOuterLoop(t, watch, "a spell's damage");
        Assert.True(t.AskInReach().Any);
        t.Wait(11);
        t.Magic("Dark Wisp casts Harm Other II and drains 11 points of your health.");
        SameAsOuterLoop(t, watch, "health a drain took");
        Assert.Equal("Dark Wisp", t.AskInReach().Name);
        t.Wait(11);
        t.Magic("You lose 7 points of health due to Dark Wisp casting Drain Health Other II on you");
        SameAsOuterLoop(t, watch, "health a transfer took");
        Assert.True(t.AskInReach().Any);
        t.Wait(11);
        t.Hit(string.Empty, 3);
        SameAsOuterLoop(t, watch, "a damage notice that names no one");
        Assert.Equal((ThreatAnswerKind.Attacked, string.Empty), (t.AskInReach().Kind, t.AskInReach().Name));
        Assert.True(t.View.State.Objects.Contains(far));
    }

    [Fact]
    public void The_travel_legs_question_is_one_query_and_answers_as_its_watch_and_reach_do()
    {
        var t = new ThreatRig();
        _ = t.Board;
        using var watch = t.LegWatch();
        var near = t.Creature(Guard, t.Listed, 5);
        t.Creature(Guard, t.Listed, 30);
        t.Creature(Guard, t.Listed, 45); // beyond the 40 m
        t.Creature("Stranger", ThreatRig.Unlisted, 10); // never attacks
        t.Creature(Guard, t.Listed, 8, flags: 0); // of the name, and not one that may be attacked
        SameAsTravelLeg(t, watch, "creatures near and no attack heard");
        Assert.False(t.AskOnHer().Any);

        t.Evade(Guard); // an attack that took nothing is an attack heard: the leg's watch counts it
        SameAsTravelLeg(t, watch, "an evaded swing");
        Assert.Equal((ThreatAnswerKind.Standing, near, 2), (t.AskOnHer().Kind, t.AskOnHer().Guid, t.AskOnHer().Count));
        Assert.Equal(t.Now, t.AskOnHer().At);

        t.Wait(9.9);
        SameAsTravelLeg(t, watch, "heard 9.9 s ago");
        Assert.True(t.AskOnHer().Any);
        t.Wait(0.2);
        SameAsTravelLeg(t, watch, "heard 10.1 s ago");
        Assert.False(t.AskOnHer().Any);

        t.Magic("You resist the spell cast by Stranger");
        SameAsTravelLeg(t, watch, "a resisted spell from another kind");
        Assert.Equal("Stranger", t.AskOnHer().Name);
        t.Magic("Tusker Guard scorches you for 15 points with Flame Bolt III.");
        SameAsTravelLeg(t, watch, "two kinds heard");
        Assert.Equal((near, 3), (t.AskOnHer().Guid, t.AskOnHer().Count));

        t.Gone(near);
        SameAsTravelLeg(t, watch, "the nearest gone");
        Assert.Equal("Stranger", t.AskOnHer().Name);

        // the asker's own reads: where she stands as it reads her, and the creatures it has set aside
        var here = t.View.State.Self.Position!.Value;
        var stranger = t.AskOnHer().Guid;
        var aside = t.Board.Ask(t.View, ThreatQuery.OnHer(ThreatRig.LegReach, ThreatRig.LegSeconds, here) with { Skip = g => g == stranger }, t.Now);
        Assert.True(HostileReach.TryNearest(t.View, ThreatRig.LegReach, watch.HeardWithin(ThreatRig.LegSeconds), out var guid, out _, out var metres, g => g == stranger, here));
        Assert.Equal((guid, metres, 1), (aside.Guid, aside.Metres, aside.Count));
    }

    [Fact]
    public void A_swing_she_evaded_from_22_metres_is_an_attacker_to_the_travel_leg_and_nothing_to_the_outer_loop()
    {
        var t = new ThreatRig();
        _ = t.Board;
        using var outer = t.OuterWatch();
        using var leg = t.LegWatch();
        t.Creature(Guard, t.Listed, 22);
        t.Evade(Guard);
        t.Wait(5);

        Assert.False(outer.InReach(t.Now, out _)); // no hit within 10 s (an evade is none), no target within 20 m
        Assert.Single(t.Present(leg)); // heard within 10 s, alive within 40 m
        Assert.False(t.AskInReach().Any);
        Assert.Equal(1, t.AskOnHer().Count);
    }

    [Fact]
    public void A_target_15_metres_away_that_never_attacked_is_in_reach_to_the_outer_loop_and_nothing_to_the_travel_leg()
    {
        var t = new ThreatRig();
        _ = t.Board;
        using var outer = t.OuterWatch();
        using var leg = t.LegWatch();
        var guard = t.Creature(Guard, t.Listed, 15);

        Assert.True(outer.InReach(t.Now, out _));
        Assert.Empty(t.Present(leg));
        Assert.Equal((ThreatAnswerKind.Standing, guard), (t.AskInReach().Kind, t.AskInReach().Guid));
        Assert.False(t.AskOnHer().Any);
        Assert.Equal(guard, t.Board.Ask(t.View, ThreatQuery.AwakeNear(VitalsKeeper.QuietMetres), t.Now).Guid); // and awake within the rest's 24 m
    }

    [Fact]
    public void A_creature_off_the_grounds_list_5_metres_away_is_awake_near_to_the_rests_and_nothing_to_the_outer_loop()
    {
        var t = new ThreatRig();
        _ = t.Board;
        using var outer = t.OuterWatch();
        var stranger = t.Creature("Stranger", ThreatRig.Unlisted, 5);
        t.Creature("Vendor", ThreatRig.Unlisted, 2, flags: 0); // a town NPC is no reason

        Assert.False(outer.InReach(t.Now, out _));
        Assert.True(t.View.State.Objects.NearestAttackableCreature(t.View.State.Self.Position!.Value, VitalsKeeper.QuietMetres, t.View.State.Self.Guid, out var awake, t.View.Live));
        var board = t.Board.Ask(t.View, ThreatQuery.AwakeNear(VitalsKeeper.QuietMetres), t.Now);
        Assert.Equal((stranger, stranger, 1), (awake, board.Guid, board.Count));
        Assert.False(t.AskInReach().Any);
    }

    [Fact]
    public void A_hit_from_before_a_watch_was_built_is_on_the_board_and_not_on_the_watch()
    {
        var t = new ThreatRig();
        _ = t.Board; // the session's: it heard the hit
        t.Creature(Guard, t.Listed, 30);
        t.Hit(Guard, 12);
        t.Wait(5);
        using var outer = t.OuterWatch(); // a scenario rebuilt at a rung move builds its watch anew
        using var leg = t.LegWatch();

        Assert.Equal(double.NegativeInfinity, outer.LastAttackAt); // the watch's own hearing missed it
        Assert.True(outer.InReach(t.Now, out _)); // M6 16 Q2: the watch asks the board, which holds it
        Assert.Empty(t.Present(leg));
        Assert.Equal((ThreatAnswerKind.Attacked, Guard), (t.AskInReach().Kind, t.AskInReach().Name));
        Assert.Equal(1, t.AskOnHer().Count);

        // a query that takes the watch's own start gives the watch's answer: the difference is the history alone
        var since = t.Now;
        Assert.False(t.Board.Ask(t.View, ThreatQuery.InReach(ThreatRig.OuterReach, HostileWatch.AttackQuietSeconds, t.Targets) with { Since = since }, t.Now).Any);
        Assert.False(t.Board.Ask(t.View, ThreatQuery.OnHer(ThreatRig.LegReach, ThreatRig.LegSeconds) with { Since = since }, t.Now).Any);
    }

    [Fact]
    public void A_lull_is_no_hit_from_anyone_inside_the_window_and_names_the_last_one()
    {
        var t = new ThreatRig();
        var board = t.Board;
        Assert.Equal(new ThreatLull(true, string.Empty, double.NegativeInfinity), board.Lull(t.Now, 10));

        t.Hit(Guard, 12);
        var at = t.Now;
        t.Wait(4);
        Assert.Equal(new ThreatLull(false, Guard, at), board.Lull(t.Now, 10));
        t.Evade("Dark Wisp"); // an attack that took nothing breaks no lull of hits; it breaks one of attacks
        t.Wait(7);
        Assert.Equal(new ThreatLull(true, Guard, at), board.Lull(t.Now, 10));
        Assert.False(board.Lull(t.Now, 10, ThreatHeard.Attacks).Quiet);
        Assert.Equal("Dark Wisp", board.Lull(t.Now, 10, ThreatHeard.Attacks).LastBy);

        // where the outer loop's lull tests differ: a target standing within 20 m that has not hit her is no lull to them
        using var outer = t.OuterWatch();
        t.Creature(Guard, t.Listed, 6);
        Assert.True(outer.InReach(t.Now, out _));
        Assert.True(board.Lull(t.Now, 10).Quiet);
    }

    [Fact]
    public void A_swing_order_names_the_attackers_guid_and_its_death_or_removal_forgets_it()
    {
        var t = new ThreatRig();
        var board = t.Board;
        var one = t.Creature(Guard, t.Listed, 3);
        var two = t.Creature(Guard, t.Listed, 4);
        t.Swing(two);
        var guard = board.Find(Guard)!;
        Assert.Equal((two, t.Now, 1, t.Listed, "seen"), (guard.Guid, guard.GuidAt, guard.Swings, guard.Wcid, guard.WcidSource));
        Assert.Equal((0, double.NegativeInfinity), (guard.Attacks, guard.LastAttackAt)); // a swing order is no attack heard: no detector hears it as one
        Assert.False(t.AskOnHer().Any);

        t.Hit(Guard, 9); // the notice that follows names the name alone: the guid stands
        Assert.Equal((two, 1, 1), (guard.Guid, guard.Hits, guard.Attacks));
        t.DeadMotion(one); // another of the name died
        Assert.Equal(two, guard.Guid);
        t.DeadMotion(two);
        Assert.Equal(0u, guard.Guid);

        t.Swing(one);
        Assert.Equal(one, guard.Guid);
        t.Gone(one);
        Assert.Equal(0u, guard.Guid);
        t.Swing(0x80009999); // an object the table does not hold
        Assert.Equal((0u, 2), (guard.Guid, guard.Swings));
    }

    [Fact]
    public void The_board_holds_each_attackers_times_hits_and_damage_and_a_window_of_its_hits()
    {
        var t = new ThreatRig();
        var board = t.Board;
        t.Hit(Guard, 10);
        var first = t.Now;
        t.Wait(3);
        t.Hit(Guard, 25);
        t.Evade(Guard);
        t.Magic("Dark Wisp casts Harm Other II and drains 11 points of your health.");
        t.Magic("Dark Wisp cast Exhaustion Other II on you"); // an enchantment: counted for a kind held, no attack
        t.Magic("Sho cast Strength Self on you"); // a buff from another: no kind of the board
        t.Wait(8);

        var guard = board.Find(Guard)!;
        Assert.Equal((first, 2, 3, 35.0, 25.0, "evasion notice"), (guard.FirstAt, guard.Hits, guard.Attacks, guard.Damage, guard.BiggestHit, guard.LastHow));
        var wisp = board.Find("Dark Wisp")!;
        Assert.Equal((1, 1, 11.0, 1, "spell drain"), (wisp.Hits, wisp.Attacks, wisp.Damage, wisp.Enchants, wisp.LastHow));
        Assert.Null(board.Find("Sho"));
        Assert.Equal((2, 3L, 4L, "Dark Wisp", "Dark Wisp"), (board.AttackerCount, board.Hits, board.Attacks, board.LastHitBy, board.LastAttackBy));
        Assert.Same(guard, board.AttackerAt(0));

        Assert.Equal(new ThreatWindow(3, 46, 25), board.Window(null, t.Now, 12)); // everyone, the whole 11 s
        Assert.Equal(new ThreatWindow(2, 36, 25), board.Window(null, t.Now, 9)); // the first hit, 11 s ago, is out
        Assert.Equal(new ThreatWindow(1, 25, 25), board.Window(Guard, t.Now, 9));
        Assert.Equal(new ThreatWindow(1, 11, 11), board.Window("Dark Wisp", t.Now, 9));
        Assert.Equal(default, board.Window("Sho", t.Now, 9));
    }

    [Fact]
    public void An_attackers_weenie_class_is_the_one_seen_else_the_bestiarys_one_kind_of_the_name_else_unknown_by_name()
    {
        var t = new ThreatRig();
        var board = t.Board;
        var guard = t.Creature(Guard, 4242, 5);
        t.Hit(Guard);
        Assert.Equal((4242u, "seen"), (board.WcidOf(Guard), board.Find(Guard)!.WcidSource));
        t.Gone(guard);
        Assert.Equal(4242u, board.WcidOf(Guard)); // read when it attacked: kept

        var unique = Bestiary.All.Select(k => k.Identity.Name).GroupBy(n => n, StringComparer.Ordinal).First(g => g.Count() == 1 && g.Key.Length > 0).Key;
        var wcid = Bestiary.All.Single(k => k.Identity.Name == unique).Wcid;
        Assert.Equal((wcid, 1), (Bestiary.WcidNamed(unique, out var kinds), kinds));
        t.Hit(unique); // none of it in view: only its name is known
        Assert.Equal((0u, "unread"), (board.Find(unique)!.Wcid, board.Find(unique)!.WcidSource));
        Assert.Equal((wcid, "bestiary"), (board.WcidOf(unique), board.Find(unique)!.WcidSource));

        t.Hit("No Such Creature");
        Assert.Equal(0u, board.WcidOf("No Such Creature"));
        Assert.Equal("unknown: no creature of the name is in view and the bestiary holds none", board.Find("No Such Creature")!.WcidSource);
        Assert.Equal(0u, board.WcidOf("Never Heard"));

        var shared = Bestiary.All.Select(k => k.Identity.Name).GroupBy(n => n, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (shared is not null)
        {
            Assert.Null(Bestiary.WcidNamed(shared, out var several)); // a name several kinds bear is no identity
            Assert.True(several > 1);
            t.Hit(shared);
            Assert.Equal(0u, board.WcidOf(shared));
            Assert.Equal("unknown: no creature of the name is in view and the bestiary holds several kinds of it", board.Find(shared)!.WcidSource);
        }
    }

    [Fact]
    public void Her_death_and_her_kills_are_on_the_board()
    {
        var t = new ThreatRig();
        var board = t.Board;
        Assert.Equal((0, 0L, double.NegativeInfinity, double.NegativeInfinity), (board.Deaths, board.Kills, board.LastDeathAt, board.LastKillAt));
        t.Wait(2);
        t.Rig.R.H.R.Bus.Publish(new PlayerKilledSomething("Tusker Guard is shattered by your assault!"));
        Assert.Equal((1L, t.Now, "Tusker Guard is shattered by your assault!"), (board.Kills, board.LastKillAt, board.LastKillText));
        t.Wait(2);
        t.Rig.R.H.R.Bus.Publish(new PlayerDied("You have been killed by a Tusker Guard", 0x80001234, DeathNoticeSource.PlayerKilledBroadcast));
        Assert.Equal((1, t.Now, 0x80001234u), (board.Deaths, board.LastDeathAt, board.LastKiller));
    }

    [Fact]
    public void The_board_keeps_at_most_its_bound_of_names_and_forgets_the_one_heard_longest_ago()
    {
        var t = new ThreatRig();
        var board = t.Board;
        for (var i = 0; i < ThreatBoard.MaxAttackers + 2; i++)
        {
            t.Hit("Kind " + i.ToString(System.Globalization.CultureInfo.InvariantCulture), 1);
            t.Wait(0.1);
        }

        Assert.Equal(ThreatBoard.MaxAttackers, board.AttackerCount);
        Assert.Null(board.Find("Kind 0"));
        Assert.Null(board.Find("Kind 1"));
        Assert.NotNull(board.Find("Kind 2"));
        Assert.Equal(ThreatBoard.MaxAttackers + 2, board.Window(null, t.Now, 60).Hits); // the forgotten names' hits stay in the ring for the window of everyone
    }
    [Fact]
    public void M6_16_E3_0_the_gather_lists_every_creature_that_counts_and_answers_as_the_ask_does()
    {
        var t = new ThreatRig();
        _ = t.Board;
        var near = t.Creature(Guard, t.Listed, 5);
        var far = t.Creature(Guard, t.Listed, 30);
        t.Creature(Guard, t.Listed, 45); // beyond the 40 m
        t.Creature("Stranger", ThreatRig.Unlisted, 10); // never attacks
        t.Creature(Guard, t.Listed, 8, flags: 0); // of the name, and not one that may be attacked
        var query = ThreatQuery.OnHer(ThreatRig.LegReach, ThreatRig.LegSeconds);
        var list = new List<ThreatStander> { new(1, "stale", 1, 1, 1) };

        Assert.Equal(t.AskOnHer(), t.Board.Gather(t.View, in query, t.Now, list)); // nobody heard: the same nothing
        Assert.Empty(list); // cleared

        t.Evade(Guard);
        var answer = t.Board.Gather(t.View, in query, t.Now, list);

        Assert.Equal(t.AskOnHer(), answer);
        Assert.Equal(answer.Count, list.Count);
        Assert.Equal([near, far], list.Select(s => s.Guid).Order());
        var first = list.Single(s => s.Guid == near);
        Assert.Equal((Guard, t.Listed, answer.Metres, t.Now), (first.Name, first.Wcid, first.Metres, first.HeardAt));

        var skipped = query with { Skip = guid => guid == near }; // the asker's own set aside (a fight's killed)
        t.Board.Gather(t.View, in skipped, t.Now, list);
        Assert.Equal(far, Assert.Single(list).Guid);

        var alone = ThreatQuery.InReach(ThreatRig.OuterReach, 10, t.Targets); // an attack answers this one alone: no body is read
        t.Hit(Guard);
        Assert.Equal(ThreatAnswerKind.Attacked, t.Board.Gather(t.View, in alone, t.Now, list).Kind);
        Assert.Empty(list);
    }

    // ------------------------------------------------------------------------------------------------ M6 16 Eperf: the board walks the table's creatures

    /// <summary>What a query answered before M6 16 Eperf: one walk of every object of the table, in the table's order. The board's answer from its kept list of creatures must be this one, field for field.</summary>
    private static ThreatAnswer WalkOfTheWholeTable(ThreatRig t, IGameSessionView view, in ThreatQuery query, double now)
    {
        var board = t.Board;
        if (query.AttackAlone && query.QuietSeconds > 0)
        {
            var (at, by) = query.Heard == ThreatHeard.Hits ? (board.LastHitAt, board.LastHitBy) : (board.LastAttackAt, board.LastAttackBy);
            if (at >= query.Since && now - at < query.QuietSeconds)
            {
                return new ThreatAnswer(ThreatAnswerKind.Attacked, 0, by, double.NaN, at, 0);
            }
        }

        var none = new ThreatAnswer(ThreatAnswerKind.None, 0, string.Empty, double.NaN, double.NegativeInfinity, 0);
        var state = view.State;
        if (query.Standing == ThreatStanding.None || query.ReachMetres <= 0 || (query.Here ?? state.Self.Position) is not { } here)
        {
            return none;
        }

        var origin = here.ToGlobal();
        WorldObject? nearest = null;
        var (metres, heardAt, count) = (double.MaxValue, double.NegativeInfinity, 0);
        foreach (var o in state.Objects.All)
        {
            if (o.Position is null || (o.ItemType & 0x10) == 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord })
            {
                continue;
            }

            var heard = double.NegativeInfinity;
            if (query.Standing == ThreatStanding.Targets)
            {
                if (query.Targets is null || !query.Targets.Contains(o.Weenie.WeenieClassId))
                {
                    continue;
                }
            }
            else
            {
                if (o.IsSelf || o.Guid == state.Self.Guid || (o.DescriptionFlags & WorldObjectTable.AttackableFlag) == 0 || (o.DescriptionFlags & WorldObjectTable.PlayerFlag) != 0)
                {
                    continue;
                }

                if (query.Standing == ThreatStanding.Attackers)
                {
                    if (board.Find(o.Name) is not { } who)
                    {
                        continue;
                    }

                    heard = query.Heard == ThreatHeard.Hits ? who.LastHitAt : who.LastAttackAt;
                    if (!(heard >= query.Since && now - heard <= query.QuietSeconds))
                    {
                        continue;
                    }
                }
            }

            if ((query.Skip is { } skip && skip(o.Guid)) || view.Live.At(o) is not { } p)
            {
                continue;
            }

            var d = p.ToGlobal().DistanceTo(origin);
            if (d > query.ReachMetres)
            {
                continue;
            }

            count++;
            if (d < metres)
            {
                (nearest, metres, heardAt) = (o, d, heard);
            }
        }

        return nearest is null ? none : new ThreatAnswer(ThreatAnswerKind.Standing, nearest.Guid, nearest.Name ?? "creature", metres, heardAt, count);
    }

    /// <summary>
    /// M6 16 Eperf: the board answers from the table's creatures it keeps, and the list follows the table at once: within one clock, a creature that comes, goes, comes again by an update, or is described anew in place
    /// (the same instance again) as a creature or as none is in or out of the very next answer, and every answer is the one a walk of the whole table gives, for each shape of query the lanes send.
    /// </summary>
    [Fact]
    public void The_boards_answers_from_its_kept_creatures_are_a_walk_of_the_whole_tables_at_every_change_of_the_table()
    {
        var t = new ThreatRig();
        var view = t.View;
        var board = t.Board;
        var objects = view.State.Objects;
        var h = t.Rig.R.H.R;
        var here = view.State.Self.Position!.Value;
        var skipped = 0u;
        Func<uint, bool> skip = g => g == skipped;
        ThreatQuery[] queries =
        [
            ThreatQuery.InReach(ThreatRig.OuterReach, HostileWatch.AttackQuietSeconds, t.Targets),
            ThreatQuery.InReach(ThreatRig.OuterReach, HostileWatch.AttackQuietSeconds, t.Targets) with { AttackAlone = false },
            ThreatQuery.OnHer(ThreatRig.LegReach, ThreatRig.LegSeconds, here),
            new ThreatQuery(HostileReach.DefaultMetres, HostileWatch.AttackQuietSeconds, ThreatHeard.Hits, ThreatStanding.Attackers) { Skip = skip },
            new ThreatQuery(double.PositiveInfinity, 30, ThreatHeard.Attacks, ThreatStanding.Attackers) { Skip = skip },
            ThreatQuery.AwakeNear(24),
            new ThreatQuery(12, 0, ThreatHeard.Hits, ThreatStanding.Targets) { Targets = t.Targets, Here = here, Skip = skip },
        ];
        void Same(string state)
        {
            for (var q = 0; q < queries.Length; q++)
            {
                Assert.True(WalkOfTheWholeTable(t, view, in queries[q], t.Now) == board.Ask(view, in queries[q], t.Now), $"{state}, query {q}: the whole table gives {WalkOfTheWholeTable(t, view, in queries[q], t.Now)}, the board {board.Ask(view, in queries[q], t.Now)}");
            }
        }

        Same("an empty room");
        var version = objects.Version;
        var near = t.Creature(Guard, t.Listed, 6);
        var twin = t.Creature(Guard, t.Listed, 6); // two at one distance: the nearest is the one the table's own order meets first
        var far = t.Creature(Guard, t.Listed, 30);
        var stranger = t.Creature("Stranger", ThreatRig.Unlisted, 9);
        Assert.True(objects.Version > version, "a new object moves the table's version");
        Same("four creatures came");
        Assert.Equal(2, board.Ask(view, in queries[1], t.Now).Count);

        t.Hit(Guard, 9);
        t.Evade("Stranger");
        Same("two kinds heard, within the clock of the last answer");
        Assert.Equal(4, board.Ask(view, in queries[2], t.Now).Count);
        skipped = twin;
        Same("one set aside");
        skipped = 0;

        version = objects.Version;
        t.Gone(near);
        Assert.True(objects.Version > version, "a removed object moves the table's version");
        Same("the nearest left");
        Assert.Equal(twin, board.Ask(view, in queries[2], t.Now).Guid);

        // a thing on the floor that is no creature, then the same guid again by an update that makes it one (a new object replaces it)
        const uint Thing = 0x80007F01;
        h.Reducer.OnCreateObject(h.Context, ReducerHarness.Create(Thing, ReducerHarness.Stamps(), ReducerHarness.Position(here.CellId, here.Origin.X + 3, here.Origin.Y, here.Origin.Z), name: Guard), isUpdate: false);
        Same("a thing with a creature's name lies near");
        Assert.Equal(twin, board.Ask(view, in queries[2], t.Now).Guid);
        var asCreature = ReducerHarness.Create(Thing, ReducerHarness.Stamps(), ReducerHarness.Position(here.CellId, here.Origin.X + 3, here.Origin.Y, here.Origin.Z), name: Guard);
        asCreature = asCreature with { Weenie = asCreature.Weenie with { ItemType = 0x10, WeenieClassId = t.Listed, ObjectDescriptionFlags = WorldObjectTable.AttackableFlag } };
        h.Reducer.OnCreateObject(h.Context, asCreature, isUpdate: true);
        Same("the thing is a creature now (an update)");
        Assert.Equal(Thing, board.Ask(view, in queries[2], t.Now).Guid);

        // the same instance described again in place, as no creature and then as a creature: the object stays the table's, its description is another
        version = objects.Version;
        Assert.True(objects.TryGet(Thing, out var before));
        h.Reducer.OnCreateObject(h.Context, asCreature with { Weenie = asCreature.Weenie with { ItemType = 0x80 } }, isUpdate: false);
        Assert.True(objects.TryGet(Thing, out var after) && ReferenceEquals(before, after), "the same instance again is described in place");
        Assert.True(objects.Version > version, "a new description moves the table's version");
        Same("described in place as no creature");
        Assert.Equal(twin, board.Ask(view, in queries[2], t.Now).Guid);
        h.Reducer.OnCreateObject(h.Context, asCreature, isUpdate: false);
        Same("described in place as a creature again");
        Assert.Equal(Thing, board.Ask(view, in queries[2], t.Now).Guid);

        // what changes inside an object moves no version and is read from the object at each answer
        version = objects.Version;
        Assert.Equal(2, board.Ask(view, in queries[1], t.Now).Count); // the twin and the thing; the far one stands at 30 m
        h.Reducer.OnUpdatePosition(h.Context, new UpdatePositionMsg(far, PositionFlags.Grounded, ReducerHarness.Position(here.CellId, here.Origin.X + 5, here.Origin.Y, here.Origin.Z), null, null, 1, 50, 1, 1));
        Assert.Equal(version, objects.Version);
        Same("the far one came near (its position)");
        Assert.Equal(3, board.Ask(view, in queries[1], t.Now).Count);
        t.Gone(Thing);
        t.Gone(twin);
        t.Gone(stranger);
        t.Gone(far);
        Same("the room is empty again");
        Assert.False(board.Ask(view, in queries[2], t.Now).Any);
    }
}

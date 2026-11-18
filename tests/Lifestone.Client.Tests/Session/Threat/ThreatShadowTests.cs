using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Threat;

/// <summary>
/// M6 16 E2a (docs/automation-engine.md section 3, "shadow first"): the threat board in shadow beside the two detectors asked most. Each detector decides exactly as it did; the board is asked the same question beside it; a
/// difference is one line when it appears and when it changes, never every frame; the counts are kept for the session.
/// </summary>
public sealed class ThreatShadowTests
{
    private const string Guard = "Tusker Guard";
    private const string Category = "autopilot";

    private static List<string> ShadowLines(ThreatRig t) => [.. t.Rig.Log.Lines.Where(l => l.Contains(ThreatShadowLane.Token, StringComparison.Ordinal))];

    [Fact]
    public void A_lane_counts_every_ask_and_asks_for_a_line_only_when_a_difference_appears_changes_or_ends()
    {
        var lane = new ThreatShadowLane("outer-loop");
        Assert.Equal(ThreatShadowChange.None, lane.Note(1.0, true, 0, 0));
        Assert.Equal(ThreatShadowChange.Differs, lane.Note(1.1, false, 0, 1)); // a difference appears: one line
        Assert.Equal("threat-shadow: outer-loop: it said A, the board says B", lane.DiffersLine("it said A", "B"));
        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(ThreatShadowChange.None, lane.Note(1.2 + (i * 0.1), false, 0, 1)); // it stands: no line, however long
        }

        Assert.Equal(ThreatShadowChange.Differs, lane.Note(7.0, false, 0, 2)); // the board's answer changes: one line
        Assert.Equal(ThreatShadowChange.AgreesAgain, lane.Note(7.5, true, 0, 0)); // it ends: one line
        Assert.Equal("threat-shadow: outer-loop: agrees again after 6.4 s (52 ask(s) differed)", lane.AgreesLine());
        Assert.Equal(ThreatShadowChange.None, lane.Note(7.6, true, 0, 0));
        Assert.Equal((3L, 52L, 3L, 0L), (lane.Agree, lane.Differ, lane.Lines, lane.Unwritten));
        Assert.Equal("threat_shadow_agree=3 threat_shadow_differ=52", lane.Fields(default));
        Assert.Equal("threat_shadow_agree=1 threat_shadow_differ=0", lane.Fields(new ThreatShadowMark(2, 52)));
    }

    [Fact]
    public void A_lane_writes_at_most_one_line_a_second_and_names_the_changes_it_did_not_write()
    {
        var lane = new ThreatShadowLane("travel-leg");
        Assert.Equal(ThreatShadowChange.Differs, lane.Note(10.0, false, 1, 2));
        Assert.Equal(ThreatShadowChange.None, lane.Note(10.1, false, 1, 3)); // a new pair inside the second: not written yet
        Assert.Equal(ThreatShadowChange.None, lane.Note(10.2, false, 1, 4)); // and replaced before its line: lost, counted
        Assert.Equal(ThreatShadowChange.None, lane.Note(10.9, false, 1, 4));
        Assert.Equal(ThreatShadowChange.Differs, lane.Note(11.0, false, 1, 4)); // the pair that still stands when the second is over is written
        Assert.Equal("threat-shadow: travel-leg: X, the board says Y (+1 change(s) inside 1 s not written)", lane.DiffersLine("X", "Y"));
        Assert.Equal("threat-shadow: travel-leg: X, the board says Y", lane.DiffersLine("X", "Y")); // named once

        Assert.Equal(ThreatShadowChange.AgreesAgain, lane.Note(11.1, true, 0, 0));
        Assert.Equal(ThreatShadowChange.None, lane.Note(11.2, false, 1, 2)); // a flap inside the second after a line
        Assert.Equal(ThreatShadowChange.None, lane.Note(11.3, true, 0, 0)); // over before its line: no line for its end either
        Assert.Equal((2L, 6L, 3L, 2L), (lane.Agree, lane.Differ, lane.Lines, lane.Unwritten));
    }

    [Fact]
    public void A_lane_compares_once_a_frame_and_a_shadow_that_throws_turns_itself_off_loudly()
    {
        var lane = new ThreatShadowLane("outer-loop");
        Assert.True(lane.Due(3.0));
        lane.Note(3.0, true, 0, 0);
        Assert.False(lane.Due(3.0)); // asked again in the same frame: the state has not changed
        Assert.True(lane.Due(3.1));

        var log = new RecordingLogSink();
        lane.Fail(new InvalidOperationException("boom"), log, Category);
        lane.Fail(new InvalidOperationException("again"), log, Category);
        var line = Assert.Single(log.Entries);
        Assert.Equal(LogSeverity.Error, line.Severity);
        Assert.Contains("threat-shadow: outer-loop: TODO(threat-shadow-failed): the shadow threw InvalidOperationException: boom; it is off for this session (threat_shadow_agree=1 threat_shadow_differ=0 so far); the detector decides as ever", line.Message, StringComparison.Ordinal);
        Assert.True(lane.Off);
        Assert.False(lane.Due(3.2));
    }

    /// <summary>The outer loop's rule as the watch's own hearing answered it before M6 16 Q2: a hit it heard inside its quiet, else the nearest listed creature within its reach.</summary>
    private static (bool Answer, string What) OwnRule(HostileWatch watch, double now)
    {
        if (now - watch.LastAttackAt < HostileWatch.AttackQuietSeconds)
        {
            return (true, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{watch.LastAttacker} attacked {now - watch.LastAttackAt:0.#} s ago"));
        }

        return watch.Nearest(out var guid, out var name, out var metres)
            ? (true, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{name} 0x{guid:X8} at {metres:0.#} m (reach {watch.ReachMetres:0.#} m)"))
            : (false, string.Empty);
    }

    private static void SameAsItsOwnRule(HostileWatch watch, double now, string state)
    {
        var answer = watch.InReach(now, out var what);
        var (own, ownWhat) = OwnRule(watch, now);
        Assert.True(answer == own && what == ownWhat, $"{state}: the board's answer {answer} '{what}', the watch's own rule {own} '{ownWhat}'");
    }

    /// <summary>M6 16 Q2: the watch asks the board; for a watch that heard everything the board heard, nothing differs from its own rule.</summary>
    [Fact]
    public void The_outer_loops_watch_asks_the_board_and_answers_as_its_own_rule_over_a_fight()
    {
        var t = new ThreatRig();
        using var watch = t.OuterWatch();
        SameAsItsOwnRule(watch, t.Now, "an empty floor");
        var near = t.Creature(Guard, t.Listed, 6);
        t.Creature(Guard, t.Listed, 6); // a tie: the first met is named by both
        t.Creature("Stranger", ThreatRig.Unlisted, 2); // not a target of the ground
        t.Wait(0.5);
        SameAsItsOwnRule(watch, t.Now, "a target 6 m away");
        t.Hit(Guard, 12);
        t.Wait(0.5);
        SameAsItsOwnRule(watch, t.Now, "hit");
        t.Evade(Guard);
        t.Magic("Tusker Guard scorches you for 15 points with Flame Bolt III.");
        t.Wait(0.5);
        SameAsItsOwnRule(watch, t.Now, "a spell's damage");
        t.Gone(near);
        t.Wait(4);
        SameAsItsOwnRule(watch, t.Now, "the target gone, the hit 4 s old");
        t.Wait(7);
        SameAsItsOwnRule(watch, t.Now, "quiet, one target still at 6 m");
        Assert.Empty(ShadowLines(t)); // M6 16 Q2: the lane outer-loop is gone
    }

    /// <summary>M6 16 Q2: what counts as an attack, the quiet's edge and the reach's edge are the watch's own.</summary>
    /// <summary>
    /// M6 16 Q2: the outer loop's readers of "was she attacked, when, by whom" (the keeper's rule and its fight-back's quiet, the retreat's attackers, the survival layer's attacked and its attackers' name, the task fight-back,
    /// the audit) read <see cref="HostileWatch.LastHitAt"/> and <see cref="HostileWatch.LastHitBy"/>. On the remembered hit: before, each read the watch's own hearing and said not attacked while <see cref="HostileWatch.InReach"/>
    /// said attacked; after, each says attacked, by the name and at the time InReach's words give.
    /// </summary>
    [Fact]
    public void A_remembered_hit_is_attacked_for_every_reader_of_the_last_hit_as_InReach_says()
    {
        var t = new ThreatRig();
        _ = t.Board;
        t.Creature(Guard, t.Listed, 30); // outside the reach: only the hit answers
        t.Hit(Guard, 12);
        t.Wait(4);
        using var watch = t.OuterWatch();
        var quiet = HostileWatch.AttackQuietSeconds;

        Assert.False(t.Now - watch.LastAttackAt < quiet); // before: the readers' own clock said not attacked
        Assert.Equal(string.Empty, watch.LastAttacker);
        Assert.True(watch.InReach(t.Now, out var what));
        Assert.True(t.Now - watch.LastHitAt < quiet); // after: attacked, as InReach
        Assert.Equal(Guard, watch.LastHitBy);
        Assert.Equal(what, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{watch.LastHitBy} attacked {t.Now - watch.LastHitAt:0.#} s ago")); // the task fight-back's and the audit's words
        Assert.Equal(4, t.Now - Math.Max(watch.LastHitAt, t.Now - 8), 6); // the keeper's and the task's quiet: from the later of the hit and their start

        t.Wait(6.1);
        Assert.False(t.Now - watch.LastHitAt < quiet); // the quiet ends for every reader when InReach's does
        Assert.False(watch.InReach(t.Now, out _));
    }

    /// <summary>M6 16 Q2: on a plain state (the watch built before every hit), the last hit the readers read is exactly the watch's own: when and by whom, through hits, evades, spells and an unnamed hit.</summary>
    [Fact]
    public void On_a_plain_state_the_last_hit_the_readers_read_is_the_watchs_own()
    {
        var t = new ThreatRig();
        using var watch = t.OuterWatch();
        void Same(string state) => Assert.True(
            watch.LastHitAt.Equals(watch.LastAttackAt) && watch.LastHitBy == watch.LastAttacker,
            $"{state}: the board {watch.LastHitBy} at {watch.LastHitAt}, the watch {watch.LastAttacker} at {watch.LastAttackAt}");

        Same("nothing heard");
        t.Hit(Guard, 7);
        t.Wait(1);
        Same("a hit");
        t.Evade("Stranger"); // an attack, no hit: neither moves
        t.Magic("You resist the spell cast by Stranger");
        t.Wait(1);
        Same("an evade and a resist");
        t.Magic("Stranger scorches you for 15 points with Flame Bolt III.");
        t.Wait(1);
        Same("a spell's damage");
        t.Hit(string.Empty, 3);
        Same("an unnamed hit");
        t.Wait(12);
        Same("the quiet over");
    }

    [Fact]
    public void An_evade_a_resist_an_unnamed_hit_and_the_edges_of_the_quiet_and_the_reach_answer_as_the_watchs_own_rule()
    {
        var t = new ThreatRig();
        using var watch = t.OuterWatch();
        t.Creature(Guard, t.Listed, (float)ThreatRig.OuterReach + 0.5f); // just outside the reach
        t.Evade(Guard); // an attack heard, no hit: it does not count
        t.Magic("You resist the spell cast by Tusker Guard");
        SameAsItsOwnRule(watch, t.Now, "an evade and a resist");
        Assert.False(watch.InReach(t.Now, out _));

        t.Hit(string.Empty, 4); // a hit whose notice names no attacker
        SameAsItsOwnRule(watch, t.Now, "an unnamed hit");
        Assert.True(watch.InReach(t.Now, out var what));
        Assert.Equal(" attacked 0 s ago", what);
        var at = t.Now;
        t.Wait(HostileWatch.AttackQuietSeconds - 0.01);
        SameAsItsOwnRule(watch, t.Now, "just inside the quiet");
        SameAsItsOwnRule(watch, at + HostileWatch.AttackQuietSeconds, "exactly at the quiet's end");
        Assert.False(watch.InReach(at + HostileWatch.AttackQuietSeconds + 0.01, out _));

        t.Creature(Guard, t.Listed, (float)ThreatRig.OuterReach); // exactly at the reach
        t.Wait(1);
        SameAsItsOwnRule(watch, t.Now, "a target exactly at the reach");
    }

    /// <summary>M6 16 Q2: the one change of answer. A watch built after a hit (a rung move rebuilds the scenario and its watch) heard nothing; the board did, and the watch now knows the hit for its quiet.</summary>
    [Fact]
    public void A_watch_built_after_a_hit_knows_the_hit_for_its_quiet()
    {
        var t = new ThreatRig();
        _ = t.Board; // the session's board heard the fight before the rung move
        t.Wait(20);
        t.Hit(Guard, 12);
        t.Wait(4);
        using var watch = t.OuterWatch(); // the scenario is rebuilt: its watch and its hit history are new

        Assert.Equal(double.NegativeInfinity, watch.LastAttackAt); // before: its own hearing began empty, and its own rule said no hostile
        Assert.False(OwnRule(watch, t.Now).Answer);
        Assert.True(watch.InReach(t.Now, out var what)); // after: the board's hit is known
        Assert.Equal("Tusker Guard attacked 4 s ago", what);

        t.Wait(5.9);
        Assert.True(watch.InReach(t.Now, out _)); // 9.9 s: still inside the quiet
        t.Wait(0.2);
        Assert.False(watch.InReach(t.Now, out _)); // 10.1 s: the quiet is over, for the board as for the watch
        Assert.Empty(ShadowLines(t));
    }

    /// <summary>M6 16 Q2: a watch that is the session's first asker makes the board, so the board hears from the watch's start at the latest; a second watch of the session answers as the first.</summary>
    [Fact]
    public void The_first_watch_of_a_session_makes_the_board_and_two_watches_of_one_session_answer_alike()
    {
        var t = new ThreatRig();
        using var first = t.OuterWatch(); // no board before it
        t.Hit(Guard, 5);
        t.Wait(1);
        SameAsItsOwnRule(first, t.Now, "the first watch, its hit");
        using var second = t.OuterWatch();
        Assert.True(second.InReach(t.Now, out var what));
        Assert.True(first.InReach(t.Now, out var firstWhat));
        Assert.Equal(firstWhat, what);
    }

    /// <summary>M6 16 Q2: the travel leg's attackers present, as the leg now asks the board (its own question, the object table's order), next to the old read through its own watch.</summary>
    private static List<PresentAttacker> BoardPresent(ThreatRig t)
    {
        var standers = new List<ThreatStander>();
        t.Board.Gather(t.View, ThreatQuery.OnHer(ThreatRig.LegReach, ThreatRig.LegSeconds), t.Now, standers);
        return [.. standers.Select(p => new PresentAttacker(p.Guid, p.Wcid, p.Name, p.Metres))];
    }

    private static void SamePresent(ThreatRig t, AttackWatch watch, string state) =>
        Assert.True(t.Present(watch).SequenceEqual(BoardPresent(t)), $"{state}: the old read [{string.Join(", ", t.Present(watch))}], the board [{string.Join(", ", BoardPresent(t))}]");

    /// <summary>The travel leg's old pick and lull read (<c>TravelLeg.Attacker</c> with its skip, <c>TravelLeg.OnHer</c> without): the nearest named by its own watch, through <see cref="HostileReach.TryNearest"/>.</summary>
    private static (bool Found, uint Guid, string Name, double Metres) OldNearest(ThreatRig t, AttackWatch watch, Func<uint, bool>? skip)
    {
        var found = HostileReach.TryNearest(t.View, ThreatRig.LegReach, watch.HeardWithin(ThreatRig.LegSeconds), out var guid, out var name, out var metres, skip);
        return (found, guid, name, metres);
    }

    /// <summary>M6 16 Q2b: the same reads as the leg now asks them of the board (<c>TravelLeg.NearestOnHer</c>: the leg's question, its skip, the later of two at one distance).</summary>
    private static (bool Found, uint Guid, string Name, double Metres) BoardNearest(ThreatRig t, Func<uint, bool>? skip)
    {
        var a = t.Board.Ask(t.View, ThreatQuery.OnHer(ThreatRig.LegReach, ThreatRig.LegSeconds) with { Skip = skip, TiesToLast = true }, t.Now);
        return a.Kind == ThreatAnswerKind.Standing ? (true, a.Guid, a.Name, a.Metres) : (false, 0u, string.Empty, double.NaN);
    }

    private static void SameNearest(ThreatRig t, AttackWatch watch, Func<uint, bool>? skip, string state)
    {
        var (old, board) = (OldNearest(t, watch, skip), BoardNearest(t, skip));
        Assert.True(old.Found == board.Found && old.Guid == board.Guid && old.Name == board.Name && (!old.Found || old.Metres == board.Metres), $"{state}: the old read {old}, the board {board}");
    }

    /// <summary>
    /// M6 16 Q2 and Q2b: over a walk under attack the travel leg's three reads off the board are its old reads: the attackers present, the fight-back's pick (one creature set aside) and the lull test, through a tie of two at one
    /// distance (the later in the table, as before), an evade, a resisted spell, the nearest gone and the window over.
    /// </summary>
    [Fact]
    public void The_travel_legs_present_pick_and_lull_off_the_board_are_the_old_reads_over_a_walk_under_attack()
    {
        var t = new ThreatRig();
        using var watch = t.LegWatch();
        var skipped = 0u;
        bool Skip(uint guid) => guid == skipped;
        var asks = 0;
        void Frame(string state)
        {
            asks++;
            SamePresent(t, watch, state);
            SameNearest(t, watch, Skip, state + " (the pick)");
            SameNearest(t, watch, null, state + " (the lull)");
        }

        Frame("nobody");
        var near = t.Creature(Guard, t.Listed, 5);
        var twin = t.Creature(Guard, t.Listed, 5); // a tie: the later in the table is named, as the old read named it
        t.Creature(Guard, t.Listed, 30);
        t.Creature(Guard, t.Listed, (float)ThreatRig.LegReach + 1); // past the reach
        var stranger = t.Creature("Stranger", ThreatRig.Unlisted, 10);
        t.Wait(0.5);
        Frame("creatures, none heard");
        t.Evade(Guard); // an evade is an attack heard: it counts
        t.Wait(0.5);
        Frame("an evade");
        Assert.Equal(twin, BoardNearest(t, null).Guid);
        skipped = twin;
        Frame("the twin set aside");
        Assert.Equal(near, BoardNearest(t, Skip).Guid);
        t.Magic("You resist the spell cast by Stranger");
        t.Wait(0.5);
        Frame("a resisted spell");
        t.Gone(near);
        t.Gone(twin);
        t.Wait(5);
        Frame("the nearest gone");
        Assert.Equal(stranger, BoardNearest(t, Skip).Guid); // its resisted spell was heard: nearer than the far guard
        t.Wait(6);
        Frame("the window over");
        Assert.False(BoardNearest(t, null).Found);
        Assert.Equal(7, asks);
    }

    /// <summary>M6 16 Q2b: the one change of answer for the lull test. A leg whose watch began after an attack found nobody on her (a lull); the board knows the attacker for its window, and then reads as the watch does.</summary>
    [Fact]
    public void A_leg_whose_watch_began_after_an_attack_now_finds_the_attacker_on_her_and_calls_no_lull()
    {
        var t = new ThreatRig();
        _ = t.Board;
        var guard = t.Creature(Guard, t.Listed, 8);
        t.Wait(30);
        t.Evade(Guard); // on the way out of the fight, 3 s before the leg begins
        t.Wait(3);
        using var watch = t.LegWatch();

        Assert.False(OldNearest(t, watch, null).Found); // before: the leg's own read, nothing heard since its watch began: a lull
        Assert.Equal((true, guard, Guard), (BoardNearest(t, null).Found, BoardNearest(t, null).Guid, BoardNearest(t, null).Name)); // after: the board's read knows it
        Assert.Equal(guard, Assert.Single(BoardPresent(t)).Guid); // as the walk's present read (Q2) does

        t.Wait(7.1); // 10.1 s after the evade: the window is over for the board too
        Assert.False(BoardNearest(t, null).Found);
        t.Hit(Guard, 9); // it hits her again: the leg's watch hears it too
        SameNearest(t, watch, null, "heard by both");
    }

    /// <summary>M6 16 Q2b: the one change of answer for the fight-back's pick. A leg whose watch began after an attack picked nobody to fight; the board knows the attacker for its window, the ones set aside still set aside.</summary>
    [Fact]
    public void A_leg_whose_watch_began_after_an_attack_now_picks_the_attacker_to_fight_and_still_skips_the_ones_set_aside()
    {
        var t = new ThreatRig();
        _ = t.Board;
        var left = t.Creature(Guard, t.Listed, 4);
        var guard = t.Creature(Guard, t.Listed, 8);
        bool Skip(uint guid) => guid == left;
        t.Hit(Guard, 6); // in the portal space, 4 s before the leg begins
        t.Wait(4);
        using var watch = t.LegWatch();

        Assert.False(OldNearest(t, watch, Skip).Found); // before: the fight-back found no attacker
        Assert.Equal((true, guard), (BoardNearest(t, Skip).Found, BoardNearest(t, Skip).Guid)); // after: the nearest one not set aside
        Assert.Equal(left, BoardNearest(t, null).Guid); // the one set aside is still on her for the lull

        t.Wait(6.1); // 10.1 s after the hit: the window is over
        Assert.False(BoardNearest(t, Skip).Found);
        t.Evade(Guard); // heard by both now
        SameNearest(t, watch, Skip, "heard by both");
    }
}

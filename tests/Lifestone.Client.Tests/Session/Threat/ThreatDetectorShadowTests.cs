using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Threat;

/// <summary>
/// M6 16 E2b (docs/automation-engine.md E2 and section 3, "shadow first"): one detector beside the threat board (<see cref="ThreatDetectorShadow"/>) and an owner's share of the lanes (<see cref="ThreatShadowShare"/>).
/// The detector's answer is its owner's; the board is asked the detector's question as one query; the pair is compared on whether anything answered, on the creature when both name one, and on the count when the
/// detector counts; a difference is one line when it appears, changes or ends. Then the questions of the detectors that read a watch of their own, each as one query that answers as the detector does over scripted states.
/// </summary>
public sealed class ThreatDetectorShadowTests
{
    private const string Guard = "Tusker Guard";
    private const string Category = "vitals";

    private static ThreatDetectorShadow Shadow(ThreatRig t, string detector, List<string> lines, bool hearsSince = false) =>
        new(t.View, detector, lines.Add, t.Rig.Log, Category, hearsSince);

    [Fact]
    public void A_detector_that_names_its_creature_agrees_with_the_board_and_a_difference_is_one_line_that_names_both()
    {
        var t = new ThreatRig();
        var lines = new List<string>();
        var shadow = Shadow(t, "keeper-awake", lines);
        var stranger = t.Creature("Stranger", ThreatRig.Unlisted, 5);
        var guard = t.Creature(Guard, t.Listed, 12);
        var awake = ThreatQuery.AwakeNear(VitalsKeeper.QuietMetres);

        shadow.Answered(t.Now, in awake, true, stranger); // the nearest that may be attacked: the board's answer too
        shadow.Answered(t.Now, in awake, false); // asked again in the same frame: compared once
        Assert.Equal((1L, 0L), (shadow.Lane.Agree, shadow.Lane.Differ));
        Assert.Empty(lines);

        t.Wait(0.25);
        shadow.Answered(t.Now, in awake, false);
        Assert.Equal("threat-shadow: keeper-awake: none, the board says Stranger 0x80007000 at 5 m (2 within 24 m)", Assert.Single(lines));
        for (var i = 0; i < 20; i++)
        {
            t.Wait(0.25);
            shadow.Answered(t.Now, in awake, false); // it stands: no line, however long
        }

        Assert.Single(lines);
        shadow.Answered(t.Now + 0.1, in awake, true, guard, 12); // it names another creature than the board's nearest: a new pair, one line
        Assert.Equal(2, lines.Count);
        Assert.Equal("threat-shadow: keeper-awake: Tusker Guard 0x80007001 at 12 m, the board says Stranger 0x80007000 at 5 m (2 within 24 m)", lines[1]);

        shadow.Answered(t.Now + 0.2, in awake, true, stranger);
        Assert.Equal(3, lines.Count);
        Assert.StartsWith("threat-shadow: keeper-awake: agrees again after ", lines[2], StringComparison.Ordinal);
        Assert.Equal((2L, 22L, 3L), (shadow.Lane.Agree, shadow.Lane.Differ, shadow.Lane.Lines));
        Assert.Same(shadow.Lane, t.Board.Lane("keeper-awake")); // the lane is the session's
    }

    [Fact]
    public void A_detector_is_compared_on_its_count_only_when_it_counts_and_on_its_creature_only_when_it_names_the_boards()
    {
        var t = new ThreatRig();
        _ = t.Board;
        var lines = new List<string>();
        var near = t.Creature(Guard, t.Listed, 5);
        var far = t.Creature(Guard, t.Listed, 30);
        t.Evade(Guard);
        var onHer = ThreatQuery.OnHer(ThreatRig.LegReach, ThreatRig.LegSeconds);
        Assert.Equal((near, 2), (t.AskOnHer().Guid, t.AskOnHer().Count));

        var counts = Shadow(t, "counts", lines);
        counts.Answered(t.Now, in onHer, true, near, 5, count: 2);
        counts.Answered(t.Now + 0.1, in onHer, true, near, 5, count: 1); // one fewer than the board counts
        Assert.Equal((1L, 1L), (counts.Lane.Agree, counts.Lane.Differ));
        Assert.Equal("threat-shadow: counts: Tusker Guard 0x80007000 at 5 m (1 counted), the board says Tusker Guard 0x80007000 at 5 m (2 within 40 m), its name heard attacking 0.1 s ago", Assert.Single(lines));

        var yesNo = Shadow(t, "yes-no", lines);
        yesNo.Answered(t.Now, in onHer, true); // it names none and does not count: "is one near" alone
        yesNo.Answered(t.Now + 0.1, in onHer, true, said: "a hit was taken 0.3 s ago");
        Assert.Equal((2L, 0L), (yesNo.Lane.Agree, yesNo.Lane.Differ));

        var path = Shadow(t, "path", lines);
        path.Answered(t.Now, in onHer, true, far, 2.5, sameCreature: false); // the nearest to a path, not to her: named in the words, not compared
        Assert.Equal((1L, 0L), (path.Lane.Agree, path.Lane.Differ));
        path.Answered(t.Now + 0.1, in onHer, false);
        Assert.Equal(2, lines.Count);

        // an attack that answers alone reads no body: neither a creature nor a count is compared
        t.Hit(Guard, 9);
        var inReach = ThreatQuery.InReach(ThreatRig.OuterReach, HostileWatch.AttackQuietSeconds, t.Targets);
        var alone = Shadow(t, "alone", lines);
        alone.Answered(t.Now, in inReach, true, far, 30, count: 7);
        Assert.Equal((1L, 0L), (alone.Lane.Agree, alone.Lane.Differ));
    }

    [Fact]
    public void The_words_of_a_difference_say_what_the_board_answered_with_and_what_it_looked_for()
    {
        var t = new ThreatRig();
        _ = t.Board;
        var lines = new List<string>();
        t.Creature("Vendor", ThreatRig.Unlisted, 3, flags: 0); // may not be attacked
        var none = Shadow(t, "a", lines, hearsSince: true);
        none.Answered(t.Now + 1, ThreatQuery.OnHer(40, 10), true, said: "'Vendor' is on her");
        Assert.Equal("threat-shadow: a: 'Vendor' is on her, the board says none (no creature that may be attacked, of a name heard attacking her within 10 s, stands alive within 40 m; no attack heard this session)", lines[^1]);

        Shadow(t, "b", lines).Answered(t.Now + 1, ThreatQuery.AwakeNear(24), true);
        Assert.Equal("threat-shadow: b: one or more, the board says none (no creature that may be attacked stands alive within 24 m)", lines[^1]);

        Shadow(t, "c", lines).Answered(t.Now + 1, new ThreatQuery(20, 0, ThreatHeard.Hits, ThreatStanding.Targets) { Targets = t.Targets }, true, count: 3);
        Assert.Equal("threat-shadow: c: one or more (3 counted), the board says none (no target of the ground stands alive within 20 m)", lines[^1]);

        t.Hit(Guard, 9);
        t.Wait(2);
        Shadow(t, "d", lines, hearsSince: true).Answered(t.Now + 1, ThreatQuery.InReach(20, 10, t.Targets), false);
        Assert.Equal("threat-shadow: d: none (its watch hears since 1 s), the board says Tusker Guard attacked 3 s ago, 2 s before the detector began", lines[^1]);

        Shadow(t, "e", lines).Answered(t.Now + 1, new ThreatQuery(double.PositiveInfinity, 20, ThreatHeard.Attacks, ThreatStanding.Attackers), true);
        Assert.Equal("threat-shadow: e: one or more, the board says none (no creature that may be attacked, of a name heard attacking her within 20 s, stands alive within any distance; the last attack heard: Tusker Guard 3 s ago)", lines[^1]);

        t.Wait(20);
        Shadow(t, "f", lines).Answered(t.Now, ThreatQuery.InReach(20, 10, t.Targets), true, said: "a hit was taken 9.9 s ago");
        Assert.Equal("threat-shadow: f: a hit was taken 9.9 s ago, the board says none (no hit within 10 s; no target of the ground stands alive within 20 m; the last hit heard: Tusker Guard 22 s ago)", lines[^1]);
    }

    [Fact]
    public void A_shadow_that_throws_turns_its_lane_off_once_and_loudly_and_asks_no_more()
    {
        var t = new ThreatRig();
        var lines = new List<string>();
        var shadow = Shadow(t, "broken", lines);
        t.Creature(Guard, t.Listed, 5);
        var throws = ThreatQuery.AwakeNear(24) with { Skip = _ => throw new InvalidOperationException("boom") };

        shadow.Answered(t.Now, in throws, true);
        shadow.Answered(t.Now + 1, in throws, true);

        Assert.True(shadow.Lane.Off);
        Assert.Empty(lines);
        var loud = Assert.Single(t.Rig.Log.Entries, e => e.Message.Contains(ThreatShadowLane.Token, StringComparison.Ordinal));
        Assert.Equal(LogSeverity.Error, loud.Severity);
        Assert.Contains("threat-shadow: broken: TODO(threat-shadow-failed): the shadow threw InvalidOperationException: boom; it is off for this session", loud.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_owners_share_is_its_lanes_asks_since_its_marks_and_its_line_names_each_detector_asked()
    {
        var a = new ThreatShadowLane("fight-defence");
        var b = new ThreatShadowLane("fight-refresh");
        var c = new ThreatShadowLane("fight-awake");
        a.Note(1, true, 0, 0); // asked before the owner began: the session's, not the owner's
        var share = new ThreatShadowShare();
        share.Add(a);
        share.Add(b);
        share.Add(c);
        share.Add(a); // once
        Assert.Null(share.Line("fight")); // nothing asked since the marks: no line
        Assert.Equal("threat_shadow_agree=0 threat_shadow_differ=0", share.Fields());

        a.Note(2, true, 0, 0);
        a.Note(3, false, 0, 1);
        b.Note(3, true, 0, 0);
        Assert.Equal((2L, 1L), share.Counts);
        Assert.Equal("threat_shadow_agree=2 threat_shadow_differ=1", share.Fields());
        Assert.Equal("threat-shadow: fight: its asks: threat_shadow_agree=2 threat_shadow_differ=1 (agreed/differed by detector: fight-defence 1/1, fight-refresh 1/0)", share.Line("fight"));

        share.Remark(); // the owner wrote its line: the next one starts here
        Assert.Null(share.Line("fight"));
        c.Note(4, true, 0, 0);
        Assert.Equal("threat-shadow: fight: its asks: threat_shadow_agree=1 threat_shadow_differ=0 (agreed/differed by detector: fight-awake 1/0)", share.Line("fight"));
    }

    /// <summary>The retreat line's attackers (the creatures of a name the outer loop's watch heard hit her within its quiet, alive within the fight-back's reach), as the outer loop reads them.</summary>
    private static List<PresentAttacker> RetreatPresent(ThreatRig t, HostileWatch watch)
    {
        var present = new List<PresentAttacker>();
        HostileReach.Present(t.View, HuntScenario.DefendReachMetres, watch.HeardWithin(t.Now, HostileWatch.AttackQuietSeconds), present);
        return present;
    }

    [Fact]
    public void The_retreats_question_is_one_query_and_answers_as_the_outer_watchs_heard_names_and_the_reach_do()
    {
        var t = new ThreatRig();
        _ = t.Board;
        using var watch = t.OuterWatch();
        var query = new ThreatQuery(HuntScenario.DefendReachMetres, HostileWatch.AttackQuietSeconds, ThreatHeard.Hits, ThreatStanding.Attackers);
        void Same(string state)
        {
            var present = RetreatPresent(t, watch);
            var board = t.Board.Ask(t.View, in query, t.Now);
            Assert.True(present.Count == board.Count, $"{state}: the retreat reads {present.Count}, the board {board}");
            if (present.Count > 0)
            {
                Assert.Equal(present.Min(p => p.Metres), board.Metres);
            }
        }

        var near = t.Creature(Guard, t.Listed, 5);
        t.Creature(Guard, t.Listed, (float)HuntScenario.DefendReachMetres - 1);
        t.Creature(Guard, t.Listed, (float)HuntScenario.DefendReachMetres + 5); // beyond the reach
        t.Creature("Stranger", ThreatRig.Unlisted, 8);
        Same("creatures near and nothing heard");
        t.Evade(Guard);
        Same("an evaded swing is no hit: the watch's heard names are of hits");
        Assert.Equal(0, t.Board.Ask(t.View, in query, t.Now).Count);
        t.Hit(Guard, 9);
        Same("a hit");
        Assert.Equal((near, 2), (t.Board.Ask(t.View, in query, t.Now).Guid, t.Board.Ask(t.View, in query, t.Now).Count));
        t.Magic("Stranger scorches you for 15 points with Flame Bolt III.");
        Same("a spell's damage from another kind");
        Assert.Equal(3, t.Board.Ask(t.View, in query, t.Now).Count);
        t.Wait(9.9);
        Same("9.9 s on");
        t.Wait(0.2);
        Same("10.1 s on");
        Assert.Equal(0, t.Board.Ask(t.View, in query, t.Now).Count);
    }

    /// <summary>M6 16 Q2: the retreat line now reads its attackers off the board with this question; the one change of answer is a hit heard before the outer loop's watch was built (a rung move), known for its quiet.</summary>
    [Fact]
    public void The_retreats_question_knows_a_hit_heard_before_the_outer_watch_was_built_for_its_quiet()
    {
        var t = new ThreatRig();
        _ = t.Board;
        var near = t.Creature(Guard, t.Listed, 5);
        t.Hit(Guard, 9);
        t.Wait(4);
        using var watch = t.OuterWatch(); // the scenario is rebuilt: its watch heard nothing
        var query = new ThreatQuery(HuntScenario.DefendReachMetres, HostileWatch.AttackQuietSeconds, ThreatHeard.Hits, ThreatStanding.Attackers);
        var standers = new List<ThreatStander>();

        Assert.Empty(RetreatPresent(t, watch)); // before: the old read through the watch's heard names
        t.Board.Gather(t.View, in query, t.Now, standers);
        Assert.Equal(near, Assert.Single(standers).Guid); // after: the board's read knows it
        t.Wait(6.1); // 10.1 s after the hit
        t.Board.Gather(t.View, in query, t.Now, standers);
        Assert.Empty(standers);
    }

    [Fact]
    public void The_heal_stations_question_is_one_query_whose_window_is_the_time_it_has_listened()
    {
        var t = new ThreatRig();
        _ = t.Board;
        t.Creature(Guard, t.Listed, 12);
        t.Hit(Guard, 9); // before the pre-check began to listen: neither counts it
        t.Wait(2);
        var began = t.Now;
        using var watch = t.LegWatch(); // the pre-check's own watch: it keeps every name since it began
        void Same(string state)
        {
            var found = HostileReach.TryNearest(t.View, HostileReach.DefaultMetres, watch.Names, out var guid, out _, out _);
            var board = t.Board.Ask(t.View, ThreatQuery.OnHer(HostileReach.DefaultMetres, t.Now - began), t.Now);
            Assert.True(found == board.Any && guid == board.Guid, $"{state}: the pre-check finds 0x{guid:X8}, the board {board}");
        }

        t.Wait(3);
        Same("an attack from before the listen");
        Assert.False(t.Board.Ask(t.View, ThreatQuery.OnHer(HostileReach.DefaultMetres, t.Now - began), t.Now).Any);
        var stranger = t.Creature("Stranger", ThreatRig.Unlisted, 30);
        t.Magic("You resist the spell cast by Stranger");
        t.Wait(3);
        Same("a resisted spell inside the listen");
        t.Wait(60);
        Same("a minute on: the pre-check's watch forgets nothing");
        Assert.Equal(stranger, t.Board.Ask(t.View, ThreatQuery.OnHer(HostileReach.DefaultMetres, t.Now - began), t.Now).Guid);
    }
}

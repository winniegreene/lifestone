using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 14 (<c>m6-live-position</c>, the fix behind <c>TODO(m6-sticky-position)</c>): the fight takes a creature to stand where the session's <see cref="IGameSessionView.Live"/> says: where its body is drawn when the
/// session draws bodies and the body can be trusted, else where the server last reported it. A monster in melee that follows the mage sends no position (ACE's <c>Monster_Tick.cs:118-119</c>), so the report leaves it behind
/// a wall it has long walked round; the body is beside her. Over the same scripted ACE and walk as the other fight tests, with a scripted body source in the place of play mode's avatar set; nothing here needs the dats, a
/// server or the avatars.
/// </summary>
public sealed partial class FightScenarioTests
{
    /// <summary>The bodies a test draws: what play mode's avatar set answers through <see cref="ILiveBodies"/>.</summary>
    private sealed class FakeBodies : ILiveBodies
    {
        private readonly Dictionary<uint, LiveBody> _bodies = [];

        /// <summary>A placed, drawn body of <paramref name="guid"/> in <paramref name="cell"/> at the player's x and <paramref name="y"/>.</summary>
        public void Stand(uint guid, uint cell, float y, bool hidden = false) =>
            _bodies[guid] = new LiveBody(new CellFrame(cell, new AcFrame(new Vector3(PlayerX, y, 30), Quaternion.Identity)), InWorld: true, hidden, ServerPositionPending: false);

        public bool TryGetBody(uint guid, out LiveBody body) => _bodies.TryGetValue(guid, out body);
    }

    [Fact]
    public void An_attacker_the_servers_report_leaves_behind_a_wall_is_cast_at_where_its_body_stands_with_no_walk_and_no_struck_proof()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 25, "Virindi"); // the server's last report: 5 m off, behind a wall by the line test
        var bodies = new FakeBodies();
        bodies.Stand(TuskerGuid, OtherFloorCell, 45); // where the server reported it
        bodies.Stand(VirindiGuid, Den, 21.5f); // its body followed her: beside her, in her own cell
        r.Draw(bodies);
        var walk = new FakeWalk { BlockedCell = FloorCell, KeepBlockedCell = true, TicksPerLeg = 3, TraceCells = [Den, OtherFloorCell, FloorCell] };
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r);
        for (var i = 0; i < 400 && !virindi.Dead; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(virindi.Dead);
        Assert.Equal(VirindiGuid, r.CastTargets[0]);
        Assert.Equal(0, walk.Legs); // no leg: the line to where it stands is clear
        Assert.Equal(0, scenario.StruckProofs); // and the blow's proof was not needed
        Assert.Equal(0, LinesWith(r, "m6-struck-proof"));
        Assert.Equal(0, LinesWith(r, "self-defence: no attacker is in range with a clear line"));
        Assert.Equal(1, scenario.Defended);

        // The measure (report-only): both records, their metres apart and from the mage, the report's age, and which one the fight reads; once per creature in five seconds.
        Assert.Equal(1, LinesWith(r, "m6-live-position: Virindi 0x80002001 (attacker"));
        Assert.Equal(1, LinesWith(r, ") is drawn 3.5 m from where the server last reported it: body 0x01050375 [100.0 21.5 30.0] 1.5 m from the mage, report 0x01050303 [100.0 25.0 30.0] 5.0 m from the mage, sent "));
        Assert.Equal(1, LinesWith(r, " s ago; the mage at 0x01050375 [100.0 20.0 30.0]; the fight reads the body (report-only measure, gap 1 of this fight)"));
        Assert.Equal(1, scenario.LivePositionGaps);
        Assert.Equal(3.5, scenario.LivePositionGapMaxMetres, 3);
        Assert.Equal(0, scenario.LivePositionFarBlows); // the blow came from 1.5 m by the fight's own answer
        Assert.Contains(" live_position_gaps=1 live_position_gap_max_m=3.5 live_position_far_blows=0", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0, LinesWith(r, "[live-position]")); // no fall back: every creature asked about has a body
    }

    [Fact]
    public void M6_14_a_new_server_report_referees_between_the_drawn_body_and_the_report_before_it()
    {
        // Live 2026-11-12 14:12 to 14:20 UTC, the Cave: gaps of 5 to 11 m between the two records, and nothing said which was right.
        var r = new Rig(fight: new FightOptions { DefendForgetSeconds = 30 });
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 90, hitsToKill: 100);
        r.Add(VirindiGuid, Virindi, FloorCell, 25, "Virindi", hitsToKill: 100_000); // the server's last report: 5 m off
        var bodies = new FakeBodies();
        bodies.Stand(TuskerGuid, OtherFloorCell, 90);
        bodies.Stand(VirindiGuid, Den, 21.5f); // drawn beside her
        r.Draw(bodies);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk { BlockedCell = FloorCell, KeepBlockedCell = true, TicksPerLeg = 3 }));

        MeleeHit(r);
        for (var i = 0; i < 12; i++)
        {
            r.Frame(scenario); // three seconds with no report
        }

        Assert.Equal(0, scenario.LivePositionBodyRight + scenario.LivePositionReportRight);
        r.R.H.R.Reducer.OnUpdatePosition(r.R.H.R.Context, ReducerHarness.Move(VirindiGuid, 1, 2, 1, 1, cell: Den, x: PlayerX + 1.5f)); // the server speaks: 1.5 m beside her
        r.Frame(scenario);
        r.Frame(scenario);

        Assert.Equal((1, 0), (scenario.LivePositionBodyRight, scenario.LivePositionReportRight));
        Assert.Equal(1, LinesWith(r, "m6-live-position: referee: Virindi 0x80002001: a new server report came "));
        Assert.Equal(1, LinesWith(r, " s after the last: the drawn body was 2.1 m from it, the old report 5.2 m: the body was nearer (body right 1, old report right 0 in this fight; report-only)"));
        Assert.Contains(" live_position_body_right=1 live_position_report_right=0", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void M6_14_a_move_to_object_order_names_its_targets_place_and_the_fight_reads_it_as_the_servers_newest_word()
    {
        // The owner, 2026-11-12: use the information the server gives. A MoveToObject order carries "the location of the target" (ACE Network/Motion/MoveToObject.cs:10,16); the client kept the order and dropped the place.
        var r = new Rig(fight: new FightOptions { DefendForgetSeconds = 30 });
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 90, hitsToKill: 100);
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 25, "Virindi"); // the server's last report: 5 m off, behind a wall by the line test
        var bodies = new FakeBodies();
        bodies.Stand(TuskerGuid, OtherFloorCell, 90);
        bodies.Stand(VirindiGuid, FloorCell, 27); // drawn 2 m past its last report, farther still: the body knows no better
        r.Draw(bodies);
        var walk = new FakeWalk { BlockedCell = FloorCell, KeepBlockedCell = true, TicksPerLeg = 3 };
        var scenario = ToTheFight(r, r.Scenario(walk));
        var h = r.R.H.R;
        Assert.True(h.State.Objects.TryGet(VirindiGuid, out var o));
        Assert.Null(o!.OrderPosition);

        // An order aimed at it names its place: beside her, in her own cell.
        h.Reducer.OnUpdateMotion(h.Context, new UpdateMotionMsg(TuskerGuid, 1, 2, 1, false, new ServerMotionState
        {
            MovementType = MovementType.MoveToObject,
            MoveTo = new MoveToData(VirindiGuid, Den, new Vector3(PlayerX, 21.5f, 30), new MoveToParameters(0x1EE0F, 0.6f, 0.1f, 50f, 1.2f, 15f, 90f), 1.75f),
        }));

        Assert.Equal(Den, o.OrderPosition!.Value.CellId);
        Assert.Equal(new Vector3(PlayerX, 21.5f, 30), o.OrderPosition.Value.Origin);
        Assert.False(double.IsNaN(o.OrderPositionAt));
        Assert.Equal(FloorCell, o.Position!.Value.CellId); // the report is not rewritten: two words of the server's, each with its time

        MeleeHit(r);
        for (var i = 0; i < 12 && !virindi.Dead; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(virindi.Dead); // cast at where the order placed it: no wall from there
        Assert.Equal(0, walk.Legs);
        Assert.Equal(0, scenario.StruckProofs);
        Assert.True(LinesWith(r, "the fight reads the place a movement order named") >= 1);
    }

    [Fact]
    public void M6_14_an_order_whose_target_is_the_mover_or_unknown_or_names_no_cell_places_nothing()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        var h = r.R.H.R;
        var order = new MoveToParameters(0x1EE0F, 0.6f, 0.1f, 50f, 1.2f, 15f, 90f);
        h.Reducer.OnUpdateMotion(h.Context, new UpdateMotionMsg(TuskerGuid, 1, 2, 1, false, new ServerMotionState { MovementType = MovementType.MoveToObject, MoveTo = new MoveToData(TuskerGuid, Den, new Vector3(1, 2, 3), order, 1f) }));
        h.Reducer.OnUpdateMotion(h.Context, new UpdateMotionMsg(TuskerGuid, 1, 3, 1, false, new ServerMotionState { MovementType = MovementType.MoveToObject, MoveTo = new MoveToData(0x8000FFFF, Den, new Vector3(1, 2, 3), order, 1f) }));
        h.Reducer.OnUpdateMotion(h.Context, new UpdateMotionMsg(TuskerGuid, 1, 4, 1, false, new ServerMotionState { MovementType = MovementType.MoveToPosition, MoveTo = new MoveToData(null, Den, new Vector3(1, 2, 3), order, 1f) }));

        Assert.True(h.State.Objects.TryGet(TuskerGuid, out var o));
        Assert.Null(o!.OrderPosition);
        Assert.True(double.IsNaN(o.OrderPositionAt));
    }

    private static void Swing(Rig r, uint guid, ushort movement, ushort forward = 0x0062) =>
        r.R.H.R.Reducer.OnUpdateMotion(r.R.H.R.Context, new UpdateMotionMsg(guid, 1, movement, 1, false, new ServerMotionState { MovementType = MovementType.Interpreted, Stance = 0x003C, Forward = forward, StickyGuid = Lifestone.Client.Tests.Session.ActionHarness.Amy }));

    [Fact]
    public void M6_14_a_swing_order_names_the_attacker_by_its_guid_and_the_blow_notice_is_its_not_the_nearest_of_that_names()
    {
        // The wire, 2026-11-12: each melee swing carries its creature's guid and the mage as the object it is stuck to (ACE Monster_Melee.cs DoSwingMotion). The notices name a name alone.
        const uint Bystander = 0x80002002;
        var r = new Rig(fight: new FightOptions { DefendForgetSeconds = 30 });
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 90, hitsToKill: 100);
        var swinger = r.Add(VirindiGuid, Virindi, FloorCell, 27, "Virindi"); // by its record 7 m off, behind a wall by the line test
        var bystander = r.Add(Bystander, Virindi, Den, 22, "Virindi", hitsToKill: 100); // another of the same name, nearer, doing nothing
        var walk = new FakeWalk { BlockedCell = FloorCell, KeepBlockedCell = true, TicksPerLeg = 3 };
        var scenario = ToTheFight(r, r.Scenario(walk));

        Swing(r, VirindiGuid, 2);
        MeleeHit(r); // "Virindi hits you": by name
        for (var i = 0; i < 12 && !swinger.Dead; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal((1, 1), (scenario.SwingOrders, scenario.SwingBlows));
        Assert.Equal(1, LinesWith(r, "self-defence: Virindi 0x80002001 swings at the mage (the server's swing order names her as the object it is stuck to"));
        Assert.Equal(0, LinesWith(r, "self-defence: Virindi 0x80002002 attacked")); // the nearer one of that name was never taken for the attacker
        Assert.Equal(VirindiGuid, r.CastTargets[0]);
        Assert.True(swinger.Dead);
        Assert.False(bystander.Dead);
        Assert.Equal(0, walk.Legs); // stuck to her by the server's own order: cast at where she stands, whatever its record says
        Assert.Equal(1, scenario.StruckProofs);
    }

    [Fact]
    public void M6_14_a_swing_order_that_names_another_or_comes_from_a_killed_creature_makes_no_attacker()
    {
        var r = new Rig(fight: new FightOptions { DefendForgetSeconds = 30 });
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 90, hitsToKill: 100);
        r.Add(VirindiGuid, Virindi, FloorCell, 27, "Virindi", hitsToKill: 100);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk { BlockedCell = FloorCell, KeepBlockedCell = true }));

        r.R.H.R.Reducer.OnUpdateMotion(r.R.H.R.Context, new UpdateMotionMsg(VirindiGuid, 1, 2, 1, false, new ServerMotionState { MovementType = MovementType.Interpreted, Forward = 0x0062, StickyGuid = TuskerGuid })); // it swings at something else
        r.R.H.R.Reducer.OnUpdateMotion(r.R.H.R.Context, new UpdateMotionMsg(VirindiGuid, 1, 3, 1, false, new ServerMotionState { MovementType = MovementType.Interpreted, Forward = 0x0003 })); // no sticky object at all
        r.Frame(scenario);

        Assert.Equal(0, scenario.SwingOrders);
        Assert.Equal(0, scenario.AttackersNow);
    }

    [Fact]
    public void A_body_the_seam_refuses_leaves_the_fight_on_the_servers_report_named_and_the_struck_proof_still_answers()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 25, "Virindi");
        var bodies = new FakeBodies();
        bodies.Stand(TuskerGuid, OtherFloorCell, 45);
        bodies.Stand(VirindiGuid, Den, 21.5f, hidden: true); // a body the screen does not draw is no witness
        r.Draw(bodies);
        var walk = new FakeWalk { BlockedCell = FloorCell, KeepBlockedCell = true, TicksPerLeg = 3, TraceCells = [Den, OtherFloorCell, FloorCell] };
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r);
        for (var i = 0; i < 400 && !virindi.Dead; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(virindi.Dead);
        Assert.Equal(0, walk.Legs);
        Assert.Equal(1, scenario.StruckProofs); // judged by the report (behind the wall), so the blow's proof is what lets her cast, as before
        Assert.Equal(1, LinesWith(r, "it is cast at (m6-struck-proof, TODO(m6-sticky-position))"));
        Assert.True(LinesWith(r, "[live-position] Virindi 0x80002001 is taken to stand where the server last reported it, 0x01050303 [100 25 30], not where its body is drawn: its body is Hidden, the screen draws nothing there (m6-live-position;") >= 1);
        Assert.Equal(1, LinesWith(r, "the fight reads the server's report (Hidden) (report-only measure, gap 1 of this fight)")); // the measure still shows both records
        Assert.Equal(1, LinesWith(r, "m6-live-position: Virindi 0x80002001 struck the mage (damage notice) while the fight takes it to stand 5.0 m from her: body 0x01050375 [100.0 21.5 30.0] 1.5 m from the mage, report 0x01050303 [100.0 25.0 30.0] 5.0 m from the mage, sent "));
        Assert.Equal(1, scenario.LivePositionFarBlows);
    }

    [Fact]
    public void Without_bodies_the_fight_reads_the_servers_report_and_the_measure_says_nothing()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 25, "Virindi");
        var walk = new FakeWalk { BlockedCell = FloorCell, KeepBlockedCell = true, TicksPerLeg = 3, TraceCells = [Den, OtherFloorCell, FloorCell] };
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r);
        for (var i = 0; i < 400 && !virindi.Dead; i++)
        {
            r.Frame(scenario);
        }

        Assert.Same(LivePositions.ReportsOnly, r.View.Live);
        Assert.Equal(1, scenario.StruckProofs); // the headless host's answer, unchanged
        Assert.Equal(0, LinesWith(r, "live-position"));
        Assert.Equal(0, scenario.LivePositionGaps);
        Assert.DoesNotContain("live_position", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_pick_takes_the_creature_whose_body_is_nearest_and_the_hostile_watch_reads_the_body_too()
    {
        var r = new Rig();
        var far = r.Add(TuskerGuid, TuskerSlave, FloorCell, 30); // reported 10 m off; its body is 25 m off
        var near = r.Add(TuskerGuid + 1, TuskerSlave, FloorCell, 48); // reported 28 m off; its body is 9 m off
        var bodies = new FakeBodies();
        bodies.Stand(TuskerGuid, FloorCell, 45); // 15 m from its report: inside retail's 20 m for a dungeon cell, so the body answers
        bodies.Stand(TuskerGuid + 1, FloorCell, 29); // 19 m from its report
        using var reports = new HostileWatch(r.View, r.Fight.GroundProfile, 8);
        Assert.False(reports.Nearest(out _, out _, out _)); // by the reports, nothing within 8 m

        r.Draw(bodies);
        using var watch = new HostileWatch(r.View, r.Fight.GroundProfile, 12);
        Assert.True(watch.Nearest(out var guid, out _, out var metres));
        Assert.Equal(TuskerGuid + 1, guid);
        Assert.Equal(9, metres, 3);

        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));
        for (var i = 0; i < 400 && r.CastTargets.Count == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(TuskerGuid + 1, r.CastTargets[0]); // the nearest by where they stand, not by where they were reported
        Assert.False(far.Dead);
        _ = near;
    }
}

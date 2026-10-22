using System.Numerics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 1.2 s3: the stand-in's melee attack in the live order of cache/m4/1-2/capture.md C1 (<c>StandInWorld.Attack.cs</c>): the first swing with no 01B8, 01C0 then 01B1 with F750 and F755 on the target, 01A7 {0}; the repeat
/// by itself with 01B8 first; a lethal blow with 01C0, 01AD and no 01B1, the experience, the Dead motion, the corpse, and 01A7 {0x0036}; the approach of a target beyond reach; the cancel; the refusals; and the tusker that
/// fights back dying by melee. Nothing here is ACE: the offline stand-in is proved against the capture, the live loops prove ACE.
/// </summary>
public sealed class StandInMeleeTests
{
    private const double Tolerance = 0.06;

    private static readonly string[] FirstSwing = ["02E9", "F74C", "01C0", "01B1", "F750", "F755", "01A7"];

    [Fact]
    public void A_swing_in_reach_is_the_stamina_the_swing_the_health_and_the_hit_then_01A7_zero_at_the_captured_times()
    {
        using var rig = new StandInAttackRig();
        rig.MeleeKit(2.0);

        rig.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(2.2);

        Assert.Equal(FirstSwing, rig.Until("01A7")); // the first swing: no 01B8 before it, the 01C0 with each 01B1
        var timeline = rig.Cast.Timeline;
        var swing = timeline.First(e => e.Tag == "F74C").At;
        Assert.InRange(timeline.First(e => e.Tag == "01C0").At - swing, StandInWorld.MeleeBlowSeconds - Tolerance, StandInWorld.MeleeBlowSeconds + Tolerance);
        Assert.InRange(timeline.First(e => e.Tag == "01A7").At - swing, StandInWorld.MeleeSwingSeconds - Tolerance, StandInWorld.MeleeSwingSeconds + Tolerance);
        Assert.Equal([0u], rig.World.AttackDoneErrors);
        Assert.Equal(1, rig.World.Swings);
        Assert.True(rig.World.Attacking);
    }

    [Fact]
    public void One_send_repeats_by_itself_each_repeat_opens_with_01B8_and_the_client_sends_nothing_between_swings()
    {
        using var rig = new StandInAttackRig();
        rig.MeleeKit(2.0);
        var responder = rig.Cast.Session.Reducer.LocalResponder!;
        var sends = responder.ActionSends.Count;

        rig.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(8.0);

        // The selected tusker's 5 s heartbeat (M4 1.3 s0) is a 01C0 with no 01B1 or 01AD behind it: it is not part of the swing order judged here.
        var all = rig.Tags;
        var tags = all.Where((t, i) => t != "01C0" || (i + 1 < all.Length && all[i + 1] is "01B1" or "01AD")).ToArray();
        Assert.Equal(FirstSwing, tags[..FirstSwing.Length]);
        Assert.Equal(["01B8", "02E9", "F74C", "01C0", "01B1", "F750", "F755", "01A7"], tags[FirstSwing.Length..(FirstSwing.Length + 8)]);
        Assert.True(rig.World.Swings >= 3);
        Assert.All(rig.World.AttackDoneErrors, e => Assert.Equal(0u, e));
        Assert.Equal(sends + 1, responder.ActionSends.Count); // one send, many swings
        // the repeat starts the power bar's refill (the power, 0.5) after the 01A7
        var times = rig.Cast.Timeline;
        Assert.InRange(times.First(e => e.Tag == "01B8").At - times.First(e => e.Tag == "01A7").At, 0.5 - Tolerance, 0.5 + Tolerance);
    }

    [Fact]
    public void A_lethal_blow_is_01C0_and_01AD_with_no_01B1_the_experience_the_dead_motion_the_corpse_and_then_01A7_0x0036()
    {
        using var rig = new StandInAttackRig();
        rig.MeleeKit(2.0, health: 1);

        rig.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(3.0);

        Assert.Equal(["02E9", "F74C", "01C0", "01AD", "02CF", "02CF", "F74C", "F745", "F747", "01A7"], rig.Tags);
        Assert.Equal([(uint)Lifestone.Data.Motion.WeenieError.ActionCancelled], rig.World.AttackDoneErrors);
        Assert.False(rig.World.Attacking);
        Assert.False(rig.Cast.Session.State.Objects.Contains(StandInAttackRig.Target));
        Assert.Equal(1, rig.World.Kills);
        var times = rig.Cast.Timeline;
        Assert.InRange(times.First(e => e.Tag == "F745").At - times.First(e => e.Tag == "01AD").At, StandInWorld.CorpseSeconds - Tolerance, StandInWorld.CorpseSeconds + Tolerance);
        Assert.True(times.First(e => e.Tag == "01A7").At > times.First(e => e.Tag == "F745").At); // the sequence ends after the corpse
    }

    [Fact]
    public void A_target_beyond_reach_is_walked_to_with_the_own_move_to_and_positions_then_struck_without_a_01B8()
    {
        using var rig = new StandInAttackRig();
        rig.MeleeKit(12.0);
        var before = rig.Cast.Session.State.Self.Position!.Value.Frame.Origin;

        rig.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(5.0);

        var tags = rig.Tags;
        Assert.Equal("F74C", tags[0]); // the MoveToObject
        var swing = Array.IndexOf(tags, "02E9");
        Assert.True(swing > 5, "the walk takes several position updates before the first swing");
        Assert.All(tags[1..swing], t => Assert.Equal("F748", t));
        Assert.Equal(["02E9", "F74C", "01C0", "01B1"], tags[swing..(swing + 4)]);
        Assert.DoesNotContain("01B8", tags[..(swing + 4)]);
        var after = rig.Cast.Session.State.Self.Position!.Value.Frame.Origin;
        Assert.True(Vector3.Distance(before, after) > 6f, "the player walked toward the target");
        var times = rig.Cast.Timeline;
        Assert.InRange(times.First(e => e.Tag == "02E9").At - times.First(e => e.Tag == "F74C").At, 1.0, 4.0); // the live walk took 2.4 s
    }

    [Fact]
    public void The_own_swing_F74C_carries_the_attack_motion_in_Forward_as_ACE_does_so_the_attack_sequence_counts_it()
    {
        using var rig = new StandInAttackRig();
        rig.MeleeKit(2.0);

        rig.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(2.2);

        var motion = rig.Cast.Session.State.Objects.TryGet(rig.Cast.Session.State.Self.Guid, out var self) ? self.Motion : null;
        Assert.NotNull(motion);
        Assert.Equal(Lifestone.Net.Types.MovementType.Interpreted, motion.MovementType);
        Assert.True(motion.Forward is { } forward && AttackMotions.IsMeleeSwing(forward), "Forward holds the attack");
        Assert.Equal(StandInAttackRig.Target, motion.StickyGuid);
        Assert.Equal(1, rig.Cast.Session.Attack.Swings);
    }

    [Fact]
    public void A_teleport_after_a_melee_approach_moves_the_player_its_position_sequence_follows_the_approachs()
    {
        using var rig = new StandInAttackRig();
        rig.MeleeKit(12.0);
        rig.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(5.0); // the walk sent several F748s, advancing the position sequence past the teleport count
        var here = rig.Cast.Session.State.Self.Position!.Value;
        var elsewhere = new Vector3(here.Frame.Origin.X + 3f, here.Frame.Origin.Y - 7f, here.Frame.Origin.Z);
        var destination = new Lifestone.Client.Navigation.TeleportDestination("test", [], new Lifestone.Client.World.WorldLocation("test", new Lifestone.Client.World.CellPosition(here.CellId, elsewhere), true, 0.0), "test");

        rig.Net.SendCancelAttack();
        rig.Run(1.0);
        var stale = rig.Cast.Session.Reducer.Counters.Dropped;
        rig.Cast.Session.Tools.Place(destination, ActionSource.Host);
        rig.Run(2.0);

        var after = rig.Cast.Session.State.Self.Position!.Value.Frame.Origin;
        Assert.True(Vector3.Distance(after, elsewhere) < 0.5f, "the teleport's position was applied, not dropped as Stale");
        Assert.Equal(stale, rig.Cast.Session.Reducer.Counters.Dropped);
    }

    [Fact]
    public void A_cancel_during_a_swing_lets_it_finish_then_ends_the_sequence_and_a_cancel_between_swings_ends_it_at_once()
    {
        using var rig = new StandInAttackRig();
        rig.MeleeKit(2.0);
        rig.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(1.0); // the swing is in flight
        rig.Net.SendCancelAttack();
        rig.Run(3.0);

        Assert.Equal(FirstSwing, rig.Tags); // the swing finished (the blow landed), nothing repeats
        Assert.Equal([0x36u], rig.World.AttackDoneErrors);

        using var between = new StandInAttackRig();
        between.MeleeKit(2.0);
        between.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        between.Run(StandInWorld.MeleeSwingSeconds + between.World.DelaySeconds + 0.15); // 01A7 {0} at 1.9 s after the swing, the next swing waits for the refill
        Assert.Equal([0u], between.World.AttackDoneErrors);
        between.Net.SendCancelAttack();
        between.Run(3.0);
        Assert.Equal([0u, 0x36u], between.World.AttackDoneErrors);
        Assert.Equal(1, between.World.Swings);

        using var idle = new StandInAttackRig();
        idle.MeleeKit(2.0);
        idle.Net.SendCancelAttack(); // nothing to cancel: nothing is sent
        idle.Run(1.0);
        Assert.Empty(idle.Tags);
    }

    [Fact]
    public void Without_auto_repeat_one_swing_ends_the_sequence()
    {
        using var rig = new StandInAttackRig();
        rig.MeleeKit(2.0);
        rig.World.AutoRepeatAttacks = false;

        rig.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(6.0);

        Assert.Equal(1, rig.World.Swings);
        Assert.Equal([0x36u], rig.World.AttackDoneErrors);
        Assert.False(rig.World.Attacking);
    }

    [Fact]
    public void The_refusals_a_wrong_mode_a_missing_target_and_a_busy_player_end_with_01A7_and_a_busy_one_is_told_028A_first()
    {
        using var wrongMode = new StandInAttackRig();
        wrongMode.MeleeKit(2.0);
        wrongMode.Mode(CombatMode.NonCombat);
        wrongMode.Cast.Begin();
        wrongMode.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        wrongMode.Run(1.0);
        Assert.Equal(["01A7"], wrongMode.Tags);
        Assert.Equal([0x36u], wrongMode.World.AttackDoneErrors);

        using var missing = new StandInAttackRig();
        missing.MeleeKit(2.0);
        missing.Net.SendMeleeAttack(0x80009999, 2, 0.5f);
        missing.Run(1.0);
        Assert.Equal(["01A7"], missing.Tags);

        using var dead = new StandInAttackRig();
        dead.MeleeKit(2.0, health: 1);
        dead.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        dead.Run(3.0); // the blow killed it, the sequence ended and the creature is gone
        dead.Cast.Begin();
        dead.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        dead.Run(0.5);
        Assert.Equal(["01A7"], dead.Tags); // a dead target: 01A7 only

        using var busy = new StandInAttackRig();
        busy.MeleeKit(2.0);
        busy.Net.SendSuicide();
        busy.Run(0.5);
        busy.Cast.Begin();
        busy.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        busy.Run(0.5);
        Assert.Equal(["028A", "01A7"], busy.Tags);
        Assert.Equal(0x36u, busy.World.AttackDoneErrors[^1]);
    }

    [Fact]
    public void The_tusker_that_fights_back_dies_by_melee_and_stops_attacking()
    {
        using var rig = new StandInAttackRig();
        rig.MeleeKit(1.5, health: 60, attacks: true);
        rig.World.PlayerHitRate = 1.0;

        rig.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(25.0);

        Assert.Equal(1, rig.World.Kills);
        Assert.False(rig.Cast.Session.State.Objects.Contains(StandInAttackRig.Target));
        Assert.True(rig.World.TuskerHits + rig.World.TuskerEvades >= 1, "it fought back while it lived");
        var struck = rig.World.TuskerHits + rig.World.TuskerEvades;
        rig.Run(15.0);
        Assert.Equal(struck, rig.World.TuskerHits + rig.World.TuskerEvades); // dead: no more strikes
        Assert.Equal(0x36u, rig.World.AttackDoneErrors[^1]);
        Assert.Contains("01AD", rig.Tags);
    }
}

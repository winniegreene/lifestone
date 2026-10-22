using Lifestone.Client.Session;
using Lifestone.Client.Session.StandIn;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 1.3 s0: the stand-in pushes the selected creature's health like ACE: the selection is the guid of the last QueryHealth (0 clears it), one 01C0 per 5 s while its health is above 0, and the hit's 01C0 goes only to a
/// player that selected the struck creature, ahead of the 01B1 in the same tick (<c>W/Player_Tick.cs</c> 112-118, <c>Entity/DamageHistory.cs</c> 93).
/// </summary>
public sealed class StandInHealthPushTests
{
    private const double Tolerance = 0.06;

    private static int Beats(StandInAttackRig rig) => rig.Tags.Count(t => t == "01C0");

    [Fact]
    public void A_selected_creature_is_pushed_every_five_seconds_by_the_stand_ins_own_clock()
    {
        using var rig = new StandInAttackRig();
        rig.Place(2.0);
        rig.Cast.Begin();
        rig.Net.SendQueryHealth(StandInAttackRig.Target);
        rig.Run(0.5);
        Assert.Equal(StandInAttackRig.Target, rig.World.HealthSelection);
        var queried = rig.Cast.Session.Clock.Now - 0.5;

        rig.Run(11.0);

        var beats = rig.Cast.Timeline.Where(e => e.Tag == "01C0").Select(e => e.At).ToArray();
        Assert.Equal(2, beats.Length); // about 5 s and 10 s after the query (the answer itself is not a heartbeat)
        Assert.InRange(beats[0] - queried, StandInWorld.HealthHeartbeatSeconds - Tolerance, StandInWorld.HealthHeartbeatSeconds + 0.3);
        Assert.InRange(beats[1] - beats[0], StandInWorld.HealthHeartbeatSeconds - Tolerance, StandInWorld.HealthHeartbeatSeconds + Tolerance);
        Assert.Equal(1f, rig.Cast.Session.State.Objects.TryGet(StandInAttackRig.Target, out var o) ? o.Health!.Value : -1f, 3);
    }

    [Fact]
    public void Nothing_is_pushed_after_a_query_for_object_zero_or_with_nothing_selected()
    {
        using var rig = new StandInAttackRig();
        rig.Place(2.0);
        rig.Net.SendQueryHealth(StandInAttackRig.Target);
        rig.Run(0.5);
        rig.Net.SendQueryHealth(0);
        rig.Run(0.5);
        Assert.Equal(0u, rig.World.HealthSelection);
        rig.Cast.Begin();

        rig.Run(12.0);

        Assert.Equal(0, Beats(rig));

        using var none = new StandInAttackRig();
        none.Place(2.0);
        none.Cast.Begin();
        none.Run(12.0);
        Assert.Equal(0, Beats(none)); // never selected: never pushed
    }

    [Fact]
    public void A_newer_query_for_another_object_moves_the_selection_and_the_old_creature_is_no_longer_pushed()
    {
        using var rig = new StandInAttackRig();
        rig.Place(2.0);
        rig.Net.SendQueryHealth(StandInAttackRig.Target);
        rig.Run(0.5);
        rig.Net.SendQueryHealth(0x80000FFF); // not an object of the visit: clears the selection, no answer
        rig.Run(0.5);
        rig.Cast.Begin();

        rig.Run(12.0);

        Assert.Equal(0u, rig.World.HealthSelection);
        Assert.Equal(0, Beats(rig));
    }

    [Fact]
    public void A_dead_creature_is_not_pushed()
    {
        using var rig = new StandInAttackRig();
        rig.MeleeKit(2.0, health: 1);
        rig.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(3.0);
        Assert.Equal(1, rig.World.Kills);
        rig.Cast.Begin();

        rig.Run(12.0);

        Assert.Equal(0, Beats(rig));
    }

    [Fact]
    public void The_hits_health_push_goes_only_when_the_struck_creature_is_the_selection_and_rides_ahead_of_the_01B1_in_one_tick()
    {
        using var selected = new StandInAttackRig();
        selected.MeleeKit(2.0);
        selected.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        selected.Run(2.2);
        var tags = selected.Until("01A7");
        Assert.Equal(["02E9", "F74C", "01C0", "01B1", "F750", "F755", "01A7"], tags);
        var timeline = selected.Cast.Timeline;
        Assert.Equal(timeline.First(e => e.Tag == "01C0").At, timeline.First(e => e.Tag == "01B1").At, 6); // the same tick

        using var unselected = new StandInAttackRig();
        unselected.MeleeKit(2.0, select: false);
        unselected.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        unselected.Run(2.2);
        Assert.Equal(["02E9", "F74C", "01B1", "F750", "F755", "01A7"], unselected.Until("01A7"));
    }

    [Fact]
    public void A_lethal_hit_on_the_selection_pushes_01C0_before_the_01AD_and_an_unselected_kill_pushes_none()
    {
        using var selected = new StandInAttackRig();
        selected.MeleeKit(2.0, health: 1);
        selected.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        selected.Run(3.0);
        Assert.Equal(["02E9", "F74C", "01C0", "01AD"], selected.Tags[..4]);

        using var unselected = new StandInAttackRig();
        unselected.MeleeKit(2.0, health: 1, select: false);
        unselected.Net.SendMeleeAttack(StandInAttackRig.Target, 2, 0.5f);
        unselected.Run(3.0);
        Assert.Equal(["02E9", "F74C", "01AD"], unselected.Tags[..3]);
    }
}

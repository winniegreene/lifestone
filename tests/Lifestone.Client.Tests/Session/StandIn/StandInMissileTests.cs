using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 1.2 s3: the stand-in's missile attack in the live order of cache/m4/1-2/capture.md C3 (<c>StandInWorld.Attack.cs</c>): the aim, the launch tick (02E9, 02CE, 0197, 02CD, F750, the arrow's F745, F755 Launch, F74A), the hit
/// (01C0, 01B1, F750 and F755 on the target, F750 and F747 on the arrow), the Reload and 01A7 {0}; the repeat while arrows remain; the last arrow (02DA, 02CE, F74A, F750, F747, no 0024, then 02EB and NonCombat); a miss
/// (F7E0 and the arrow's removal); a target beyond the launcher's range (028A 0x0550, 01A7); a missing launcher or arrow; and the refusal to wield a bow while a mace is wielded (GE 00A0, capture.md C2).
/// </summary>
public sealed class StandInMissileTests
{
    private static readonly string[] Aim = ["F74C", "F749"];
    private static readonly string[] Launch = ["02E9", "02CE", "0197", "02CD", "F750", "F745", "F755", "F74A"];
    private static readonly string[] Hit = ["01C0", "01B1", "F750", "F755", "F750", "F747"];
    private static readonly string[] Over = ["F74C", "F749", "01A7"];

    [Fact]
    public void A_shot_is_the_aim_the_launch_tick_the_hit_then_the_reload_and_01A7_zero()
    {
        using var rig = new StandInAttackRig();
        rig.MissileKit(4.0);

        rig.Net.SendMissileAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(2.6);

        var expected = new[] { Aim, Launch, Hit, Over }.SelectMany(x => x).ToArray();
        Assert.Equal(expected, rig.Until("01A7"));
        Assert.Equal([0u], rig.World.AttackDoneErrors);
        var times = rig.Cast.Timeline;
        Assert.InRange(times.First(e => e.Tag == "02E9").At - times.First(e => e.Tag == "F74C").At, StandInWorld.MissileAimSeconds - 0.05, StandInWorld.MissileAimSeconds + 0.05);
        Assert.InRange(times.First(e => e.Tag == "01A7").At - times.First(e => e.Tag == "02E9").At, StandInWorld.MissileShotSeconds - 0.05, StandInWorld.MissileShotSeconds + 0.05);
        Assert.Equal(1, rig.World.ArrowsLaunched);
        var arrows = rig.World.Wielded.Values.Single(e => e.Name == "Arrow");
        Assert.Equal(4, arrows.Stack); // one of five left the stack
    }

    [Fact]
    public void One_send_gives_a_shot_per_arrow_and_the_last_arrow_takes_the_wield_off_tells_the_notice_and_ends_the_sequence()
    {
        using var rig = new StandInAttackRig();
        rig.MissileKit(4.0, arrows: 3);

        rig.Net.SendMissileAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(15.0);

        var tags = rig.Tags;
        Assert.Equal(3, rig.World.ArrowsLaunched);
        Assert.Equal(2, tags.Count(t => t == "01B8")); // the repeats open with 01B8
        var lastLaunch = Array.LastIndexOf(tags, "02DA");
        var last = new[] { "02E9", "02DA", "02CE", "F74A", "F750", "F747", "F745", "F755", "02EB", "02CD", "01A7" };
        Assert.Equal(last, tags[(lastLaunch - 1)..(lastLaunch - 1 + last.Length)]);
        Assert.DoesNotContain("0024", tags); // the last arrow leaves the wield without a pack removal
        Assert.Equal([0u, 0u, 0x36u], rig.World.AttackDoneErrors);
        Assert.Equal(CombatModes.NonCombat, rig.Cast.Session.State.Self.Qualities.CombatMode);
        Assert.DoesNotContain(rig.World.Wielded.Values, e => e.Name == "Arrow");
        Assert.False(rig.World.Attacking);
    }

    [Fact]
    public void An_arrow_whose_target_is_gone_hits_the_environment_with_the_line_and_is_removed()
    {
        using var rig = new StandInAttackRig();
        rig.MissileKit(30.0);

        rig.Net.SendMissileAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(StandInWorld.MissileAimSeconds + rig.World.DelaySeconds + 0.1); // launched, in flight (0.5 s at 60 m/s)
        rig.World.RemoveObject(StandInAttackRig.Target);
        rig.Run(1.0);

        var tags = rig.Tags;
        Assert.Contains("F7E0", tags);
        Assert.DoesNotContain("01B1", tags);
        Assert.Contains(rig.Cast.Chat, l => l.Text == "Your missile attack hit the environment.");
        Assert.True(Array.IndexOf(tags, "F747", Array.IndexOf(tags, "F7E0")) > 0, "the arrow is removed after the line");
    }

    [Fact]
    public void A_target_beyond_the_launchers_range_is_told_028A_0x0550_then_01A7_and_nothing_is_launched()
    {
        using var rig = new StandInAttackRig();
        rig.MissileKit(60.0);

        rig.Net.SendMissileAttack(StandInAttackRig.Target, 2, 0.5f);
        rig.Run(2.0);

        Assert.Equal(["028A", "01A7"], rig.Tags);
        Assert.Equal(0, rig.World.ArrowsLaunched);
        Assert.Equal([0x36u], rig.World.AttackDoneErrors);
        Assert.Contains(rig.Cast.Chat, l => l.ChatType == StandInWorld.MissileOutOfRange);
    }

    [Fact]
    public void No_launcher_no_ammunition_a_wrong_mode_and_a_missing_target_end_with_01A7_alone()
    {
        // M4 3.1 g: the combat-mode guard refuses a missile mode with no launcher and a melee mode with a bow, so these two rows send the attack from peace (the setups are consistent; ACE answers the same 01A7).
        using var noBow = new StandInAttackRig();
        noBow.Place(4.0);
        noBow.Cast.Begin();
        noBow.Net.SendMissileAttack(StandInAttackRig.Target, 2, 0.5f);
        noBow.Run(1.0);
        Assert.Equal(["01A7"], noBow.Tags);

        using var noArrows = new StandInAttackRig();
        noArrows.Wield(noArrows.Spawn(StandInAttackRig.Bow), EquipMask.MissileWeapon);
        noArrows.Mode(CombatMode.Missile);
        noArrows.Place(4.0);
        noArrows.Cast.Begin();
        noArrows.Net.SendMissileAttack(StandInAttackRig.Target, 2, 0.5f);
        noArrows.Run(1.0);
        Assert.Equal(["01A7"], noArrows.Tags);

        using var wrongMode = new StandInAttackRig();
        wrongMode.Wield(wrongMode.Spawn(StandInAttackRig.Bow), EquipMask.MissileWeapon);
        wrongMode.Wield(wrongMode.Spawn(StandInAttackRig.Arrows, 5), EquipMask.MissileAmmo);
        wrongMode.Place(4.0);
        wrongMode.World.PlayerHitRate = 1.0;
        wrongMode.Select(StandInAttackRig.Target);
        wrongMode.Cast.Begin();
        wrongMode.Net.SendMissileAttack(StandInAttackRig.Target, 2, 0.5f);
        wrongMode.Run(1.0);
        Assert.Equal(["01A7"], wrongMode.Tags);

        using var missing = new StandInAttackRig();
        missing.MissileKit(4.0);
        missing.Net.SendMissileAttack(0x80009999, 2, 0.5f);
        missing.Run(1.0);
        Assert.Equal(["01A7"], missing.Tags);
        Assert.Equal(0, missing.World.ArrowsLaunched);
    }

    [Fact]
    public void Wielding_the_bow_while_the_mace_is_wielded_is_refused_with_GE_00A0_and_works_once_the_mace_is_in_the_pack()
    {
        using var rig = new StandInAttackRig();
        var mace = rig.Spawn(StandInAttackRig.Mace);
        var bow = rig.Spawn(StandInAttackRig.Bow);
        rig.Wield(mace, EquipMask.MeleeWeapon);
        rig.World.AnswerLog.Clear();
        rig.Cast.Begin();

        rig.Wield(bow, EquipMask.MissileWeapon);

        Assert.Contains("00A0", rig.World.AnswerLog);
        Assert.DoesNotContain("0023", rig.World.AnswerLog);
        Assert.DoesNotContain(bow, rig.World.Wielded.Keys);

        var put = rig.Cast.Session.Actions.MoveToPack(mace, 0, 0, ActionSource.Host);
        rig.Run(2.0);
        Assert.True(put.IsCompleted, put.Summary);
        rig.World.AnswerLog.Clear();
        rig.Wield(bow, EquipMask.MissileWeapon);
        Assert.Contains("0023", rig.World.AnswerLog);
        Assert.Contains(bow, rig.World.Wielded.Keys);
    }
}

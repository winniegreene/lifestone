using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 1.2 s3, cut down by M4 4.1 t2 (D23, D26): the stand-in's missile attack (<c>StandInWorld.Attack.cs</c>). The stand-in holds no creature, so a missile attack ends as ACE ends one at a missing target: 01A7 with ActionCancelled and
/// nothing else, after the refusals ACE makes first (the wrong combat mode, <c>Player_Missile.cs</c> 44-55; a missing launcher or its ammunition, 84-87; the target, 97-101). The shots, the arrows, the hit and the kill were deleted with the
/// creature (proved live by the hunt leg and by the scripted tracker tests). The refusal to wield a bow while a mace is wielded stays (GE 00A0, capture.md C3).
/// </summary>
public sealed class StandInMissileTests
{
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

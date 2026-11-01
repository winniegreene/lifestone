using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// M5 item 4 h2 (<c>m5-hunt-host-sends</c>): the hunt's attack, cancel, corpse and weapon-swap sends are <see cref="ActionSource.Automation"/>, so the kill switch (which withdraws and refuses automation) stops a melee or a missile swing.
/// </summary>
public sealed partial class HuntScenarioTests
{
    private static readonly ActionKind[] FightKinds = [ActionKind.Attack, ActionKind.MissileAttack, ActionKind.CancelAttack, ActionKind.OpenContainer, ActionKind.CloseContainer, ActionKind.Wield, ActionKind.MoveToPack, ActionKind.MergeStack, ActionKind.SetCombatMode];

    private static List<ActionLogEntry> LogRows(HuntScriptRig rig, params ActionKind[] kinds)
    {
        var rows = new List<ActionLogEntry>();
        for (var i = 0; i < rig.Session.ActionLog.Count; i++)
        {
            if (kinds.Contains(rig.Session.ActionLog[i].Kind))
            {
                rows.Add(rig.Session.ActionLog[i]);
            }
        }

        return rows;
    }

    [Theory]
    [InlineData(HuntWeapon.Melee)]
    [InlineData(HuntWeapon.Missile)]
    public void The_swings_the_cancels_and_the_corpse_opens_are_automation_actions_in_the_log_M5_4_h2(HuntWeapon weapon)
    {
        using var rig = Belt12(Hunt(weapon, kills: 1));
        rig.Blow = (500, 500);

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, rig.Scenario.Failure + rig.Tail());
        var rows = LogRows(rig, FightKinds);
        Assert.Contains(rows, r => r.Kind == (weapon == HuntWeapon.Melee ? ActionKind.Attack : ActionKind.MissileAttack));
        Assert.Contains(rows, r => r.Kind == ActionKind.OpenContainer);
        Assert.All(rows, r => Assert.Equal(ActionSource.Automation, r.Source));
    }

    [Theory]
    [InlineData(HuntWeapon.Melee)]
    [InlineData(HuntWeapon.Missile)]
    public void The_kill_switch_stops_the_swing_withdraws_a_queued_one_and_the_hunt_sends_nothing_more_M5_4_h2(HuntWeapon weapon)
    {
        using var rig = HuntScriptRig.Create(Hunt(weapon, kills: 5));
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12, health: 1_000_000); // a creature that outlasts the test
        Assert.True(rig.RunUntil(() => rig.Swings + rig.Shots >= 1), "the hunt swings at the creature" + rig.Tail());

        // A second swing from the same source, queued behind the one in flight (the attack lane holds one at a time).
        var queued = weapon == HuntWeapon.Melee
            ? rig.Session.Actions.Attack(FirstCreature, AttackHeight.Medium, 0.5f, ActionSource.Automation)
            : rig.Session.Actions.MissileAttack(FirstCreature, AttackHeight.Medium, 0.5f, ActionSource.Automation);
        var swings = rig.Swings + rig.Shots;

        rig.Session.Actions.KillSwitch = true;

        Assert.True(queued.IsCompleted, "the queued swing is withdrawn or refused at once");
        Assert.Contains(queued.Outcome!.Value.Kind, new[] { ActionOutcomeKind.Cancelled, ActionOutcomeKind.Refused });
        rig.Run(60);
        Assert.InRange(rig.Swings + rig.Shots, swings, swings + 1); // the swing in flight finishes; the hunt cancels the sequence (a stop goes through the switch) and not one more is sent
        Assert.Contains(LogRows(rig, ActionKind.Attack, ActionKind.MissileAttack), r => r.Source == ActionSource.Automation && r.Outcome.Kind is ActionOutcomeKind.Cancelled or ActionOutcomeKind.Refused); // the withdrawn swing is in the audit log
        Assert.Contains("Blocked why=kill-switch", rig.Scenario.Failure, StringComparison.Ordinal);
        Assert.False(rig.Session.Attack.Active, "the repeating sequence was cancelled");
        Assert.NotEqual(ScenarioStatus.Succeeded, rig.Status); // the hunt did not finish its kills
        rig.Session.Actions.KillSwitch = false;
    }
}

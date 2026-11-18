using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Contracts;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 tactics (live ladder56 2026-11-17 08:33:02.349 UTC: the self-defence rule left a hurt Cursed Wisp for a fresh one that had harmed her): over the scripted ACE in the den, the fight weighs the attacker against the target
/// she is on (FightScenario.TargetSwitch.cs) and logs it. Nothing here needs the dats or a server; the rule's own numbers are the live log's, in <c>TacticsTests</c>.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint MasterGuid = 0x80002002;
    private const uint GuardGuid = 0x80001002;

    private static void MasterBolt(Rig r, string name = "Virindi Master") => r.R.Raw(AceWireWriters.ServerMessage(name + " scorches you for 31 points with Flame Bolt.", 7));

    [Fact]
    public void M6_14_a_hurt_target_is_finished_before_a_fresh_attacker_of_the_ground_and_the_attacker_is_next()
    {
        var r = new Rig(fight: new FightOptions { Kills = FightOptions.UnlimitedKills, DefendForgetSeconds = 60 }); // the fight goes on past the first kill
        var slave = r.Add(TuskerGuid, TuskerSlave, FloorCell, PlayerY + 3, hitsToKill: 2); // the profile's target, nearest
        var guard = r.Add(GuardGuid, TuskerGuard, FloorCell, PlayerY + 8, "Tusker Guard", hitsToKill: 100);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));
        for (var i = 0; i < 200 && slave.Hits == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(1, slave.Hits); // half its health left
        MeleeHit(r, "Tusker Guard");
        for (var i = 0; i < 200 && !slave.Dead; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(slave.Dead);
        Assert.False(guard.Dead);
        Assert.All(r.CastTargets, t => Assert.Equal(TuskerGuid, t)); // no cast left the slave for the guard while it lived
        Assert.Equal(1, LinesWith(r, "target switch: keep 'Tusker Slave' 0x80001001 (200 left, "));
        Assert.Equal(1, LinesWith(r, "over 'Tusker Guard' 0x80001002 ("));
        Assert.Equal(1, LinesWith(r, "finishing the current target costs her no more health (M6 14 tactics)"));
        Assert.True(scenario.SwitchesWeighed >= 1);
        Assert.Equal(scenario.SwitchesWeighed, scenario.SwitchesKept);

        for (var i = 0; i < 40 && !r.CastTargets.Contains(GuardGuid); i++)
        {
            r.Frame(scenario);
        }

        Assert.Contains(GuardGuid, r.CastTargets); // the attacker is next
        Assert.Equal(1, LinesWith(r, "target 0x80001002 (Tusker Guard), was 0x80001001: self-defence: it attacked the player"));
    }

    [Fact]
    public void M6_14_an_attacker_her_bolts_cannot_be_read_to_kill_does_not_take_her_off_a_target_she_can()
    {
        // M6 16 E1b: a creature of a class the bestiary does not hold (no creature weenie of the world database's table): its health is known nowhere, so its kill seconds are never; the hurt slave is finished first, as the line
        // says. Until E1b this was a Virindi Master, whose health the den's list does not give: the bestiary gives it now (the test below).
        var r = new Rig(fight: new FightOptions { Kills = FightOptions.UnlimitedKills, DefendForgetSeconds = 60 });
        var slave = AttackedOnTheSlaveBy(r, NoWeenie, "Unheard Thing");

        Assert.True(slave.Dead);
        Assert.All(r.CastTargets, t => Assert.Equal(TuskerGuid, t));
        Assert.Equal(1, LinesWith(r, "her bolts do nothing to the attacker: the current target, which she can kill, is finished (M6 14 tactics)"));
        Assert.Equal(1, LinesWith(r, "TODO(engine-bestiary-unknown-kind): Unheard Thing (class 999999999) is no kind of the bestiary"));
    }

    [Fact]
    public void M6_16_E1b_an_avoided_attacker_of_the_den_is_weighed_on_its_real_health_and_Magic_Defense()
    {
        // The den lists a Virindi Master as avoided, with no health and no Magic Defense: the fight read 0 and 0, so its kill seconds were never and the rule never weighed it. The bestiary gives 145 health and Magic Defense 191
        // (the world database's creature table): both orders are weighed, and here the hurt slave (200 of its 400 left) still goes first, on the numbers.
        var r = new Rig(fight: new FightOptions { Kills = FightOptions.UnlimitedKills, DefendForgetSeconds = 60 });
        var slave = AttackedOnTheSlaveBy(r, 237, "Virindi Master");

        Assert.True(slave.Dead);
        Assert.All(r.CastTargets, t => Assert.Equal(TuskerGuid, t));
        Assert.Equal(0, LinesWith(r, "her bolts do nothing to the attacker"));
        Assert.Equal(1, LinesWith(r, "target facts: Virindi Master (class 237): maximum health 145 (the world database's creature table), Magic Defense 191, from the bestiary"));
        Assert.Equal(1, LinesWith(r, "target switch: keep 'Tusker Slave' 0x80001001 (200 left, "));
        Assert.Equal(1, LinesWith(r, " over 'Virindi Master' 0x80002002 (145 left, "));
        Assert.Equal(1, LinesWith(r, "finishing the current target costs her no more health (M6 14 tactics)"));
        Assert.Contains(r.Log.Lines, l => l.Contains("target switch: keep", StringComparison.Ordinal) && !l.Contains("dies in never", StringComparison.Ordinal) && !l.Contains("the losses cannot be compared", StringComparison.Ordinal));
    }

    /// <summary>The den's slave is bolted once, then a creature of <paramref name="wcid"/> 8 m off bolts her, and the fight runs until the slave is dead.</summary>
    private static Creature AttackedOnTheSlaveBy(Rig r, uint wcid, string name)
    {
        var slave = r.Add(TuskerGuid, TuskerSlave, FloorCell, PlayerY + 3, hitsToKill: 2);
        r.Add(MasterGuid, wcid, FloorCell, PlayerY + 8, name, hitsToKill: 100);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));
        for (var i = 0; i < 200 && slave.Hits == 0; i++)
        {
            r.Frame(scenario);
        }

        MasterBolt(r, name);
        for (var i = 0; i < 200 && !slave.Dead; i++)
        {
            r.Frame(scenario);
        }

        return slave;
    }

    [Fact]
    public void M6_14_with_no_target_in_hand_the_attacker_is_taken_at_once_and_nothing_is_weighed()
    {
        var r = new Rig();
        var master = r.Add(MasterGuid, 237, FloorCell, PlayerY + 8, "Virindi Master", hitsToKill: 100);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));
        MasterBolt(r);
        for (var i = 0; i < 40 && r.CastTargets.Count == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal([MasterGuid], r.CastTargets.Distinct());
        Assert.Equal(0, scenario.SwitchesWeighed);
        Assert.Equal(0, LinesWith(r, "target switch:"));
        Assert.False(master.Dead);
    }
}

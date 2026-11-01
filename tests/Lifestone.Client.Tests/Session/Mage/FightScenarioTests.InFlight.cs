using Lifestone.Client.Session.Scenarios;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 item 7, D124 (S1b, the owner: the 0x042C gone at its source). Soak round 12's 44 casts at a creature that died during the wind-up all followed the mage's own earlier cast, confirmed 37-322 ms before: a projectile still flying
/// killed the creature the new cast was aimed at. The pick now leaves out a creature that the projectiles in flight are expected to kill (their mean hit against its live health), takes the next one, logs and counts it, and holds
/// rather than walking when nothing else is in reach.
/// </summary>
public sealed partial class FightScenarioTests
{
    [Fact]
    public void A_target_the_bolt_in_flight_will_kill_is_left_out_of_the_next_pick_and_the_next_tusker_is_cast_at_instead()
    {
        var r = new Rig { HitDelay = 1.0, StaleDamageLineOnce = true };
        var dying = r.Add(0x80001001, TuskerSlave, 0x01050303, 40, hitsToKill: 10);
        dying.Hits = 9; // 10 % of a Slave's 400: the live model's health from the server's answer, under one Flame Bolt
        r.Add(0x80001002, TuskerSlave, 0x01050303, 48);
        var walk = new FakeWalk();
        var scenario = r.Scenario(walk);

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(dying.Dead); // the bolt in flight killed it (the fight's one kill)
        Assert.Equal(0x80001001u, r.CastTargets[0]);
        Assert.Equal(0x80001002u, r.CastTargets[1]); // the stale line sent the fight back to the pick while the bolt flew: the next tusker, not the doomed one
        Assert.DoesNotContain(0x80001001u, r.CastTargets.Skip(1));
        Assert.Equal(1, scenario.DoomedSkips);
        Assert.Equal(0, scenario.Tally.TargetDiedMidCast);
        Assert.Contains(" doomed_skips=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(1, LinesWith(r, "pick: 0x80001001 (Tusker Slave) is left out: the projectiles in flight are expected to deal"));
        Assert.Contains(r.Log.Lines, l => l.Contains("in flight: ", StringComparison.Ordinal) && l.Contains(" at 0x80001001, mean hit ", StringComparison.Ordinal));
        Assert.Equal(0, walk.Legs);
    }

    [Fact]
    public void With_only_the_doomed_target_in_reach_the_fight_holds_for_the_bolt_instead_of_casting_again_or_walking()
    {
        var r = new Rig { HitDelay = 1.0, StaleDamageLineOnce = true };
        var dying = r.Add(0x80001001, TuskerSlave, 0x01050303, 40, hitsToKill: 10);
        dying.Hits = 9;
        r.Add(0x80001002, TuskerSlave, 0x01050303, 80); // 60 m: out of the bolt's reach
        var walk = new FakeWalk { TicksPerLeg = 2 };
        var scenario = r.Scenario(walk);

        for (var i = 0; i < 200 && !dying.Dead; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(dying.Dead); // the bolt in flight killed it
        Assert.Equal([0x80001001u], r.CastTargets); // one cast: no second one at the doomed tusker
        Assert.Equal(0, walk.Legs); // and no walk while the bolt flew
        Assert.Equal(1, scenario.DoomedSkips);
    }

    [Fact]
    public void A_target_the_bolt_in_flight_leaves_alive_is_not_left_out()
    {
        var r = new Rig { HitDelay = 1.0, StaleDamageLineOnce = true };
        r.Add(0x80001001, TuskerSlave, 0x01050303, 40, hitsToKill: 20); // full health: 400 is far over one Flame Bolt's mean, and a few hits leave it so
        var scenario = r.Scenario(new FakeWalk());

        for (var i = 0; i < 40; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(r.CastTargets.Count >= 2); // cast again at it while the first bolt flew (the stale line), as before
        Assert.Equal(0, scenario.DoomedSkips);
        Assert.Equal(0, LinesWith(r, "is left out: the projectiles in flight"));
    }
}

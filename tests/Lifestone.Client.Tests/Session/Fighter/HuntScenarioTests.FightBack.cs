using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// M5 7 P7u (the owner's decision under D148; D88: always fight back an attacker), over the scripted rig: the item-end gate's Archer ended its forest hunt ("the sequence is over", its 2 kills done) while a Gold Phyntos Wasp, a
/// Shadow Child and a Brutish Monouga attacked it, and the harm-heal station after it ran under fire (the Brawler's belt hunt left a Drudge Prowler the same way). Now the hunt's end checks for an attacker first: a creature heard
/// attacking within <see cref="HuntScenario.DefendHeardSeconds"/>, alive and within <see cref="HuntScenario.DefendReachMetres"/> is fought back, with the hunt's own weapon and mode, until none is left; then the hunt ends, logged.
/// The wasp of these rows is not of the ground's table (wcid 1): only the fight-back ever targets it.
/// </summary>
public sealed partial class HuntScenarioTests
{
    private const uint Wasp = 0x80000E01;
    private const string WaspName = "Gold Phyntos Wasp";
    private const uint WaspWcid = 1; // not in the ground's table: never a pick of the hunt's own

    [Theory]
    [InlineData(HuntWeapon.Melee)]
    [InlineData(HuntWeapon.Missile)]
    public void The_quota_met_while_a_creature_attacks_the_hunt_does_not_end_fights_it_back_with_its_own_weapon_then_ends_with_the_line_P7u(HuntWeapon weapon)
    {
        using var rig = Belt12(Hunt(weapon, kills: 1));
        Assert.True(rig.RunUntil(() => rig.Scenario.Target == FirstCreature), rig.Scenario.Failure + rig.Tail());
        rig.PlaceCreature(Wasp, WaspName, 6, health: 60, wcid: WaspWcid, eastMetres: 4); // it joins the fight on the hunt's last target, about 7 m off
        rig.AttacksPlayer(Wasp);

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail(60));
        Assert.Equal(2, scenario.Tally.Kills); // the quota's kill and the wasp
        Assert.Equal(1, scenario.FoughtBack);
        Assert.True(rig.Logged("self-defence: heard 'Gold Phyntos Wasp' attack the player (evasion notice)"), rig.Tail());
        var quota = rig.LineAt("fight: the kill notice came (1 of 1)");
        var held = rig.LineAt("hunt: the kills are done (1 of 1) but 'Gold Phyntos Wasp' 0x80000E01 attacked the player");
        var attack = rig.LineAt($"at '{WaspName}' 0x80000E01");
        var ended = rig.LineAt("hunt: fought back 1 attacker(s) before ending ('Gold Phyntos Wasp' 0x80000E01); 1 kill(s) in the fight-back, 2 of 1 in all");
        var done = rig.LineAt("hunt done: 2 kill(s)");
        Assert.True(quota >= 0 && quota < held && held < attack && attack < ended && ended < done, $"quota {quota}, held {held}, attack {attack}, ended {ended}, done {done}" + rig.Tail(60));
        Assert.Contains(weapon == HuntWeapon.Missile ? "fight: MissileAttack" : "fight: Attack", rig.Log.Lines[attack], StringComparison.Ordinal);

        // The hunt's own attack, in the mode of its weapon: the wasp took the weapon's attack and never the other.
        var (used, other) = weapon == HuntWeapon.Missile ? (GameActionTypes.TargetedMissileAttack, GameActionTypes.TargetedMeleeAttack) : (GameActionTypes.TargetedMeleeAttack, GameActionTypes.TargetedMissileAttack);
        Assert.True(rig.LastSend(used, Wasp) >= 0, "the wasp was attacked with the hunt's weapon");
        Assert.Equal(-1, rig.LastSend(other, Wasp));
        Assert.False(rig.Logged("hunt: the end check:"), "the end check that found no attacker is not the line of this run");
    }

    [Fact]
    public void With_no_attacker_the_hunt_ends_as_before_and_its_end_check_is_logged_P7u()
    {
        using var rig = Belt12(Hunt(kills: 1));

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(0, scenario.FoughtBack);
        Assert.Equal(0, scenario.DefendedBetweenKills);
        var check = rig.LineAt("hunt: the end check: 1 of 1 kill(s) and no creature heard attacking the player in the last 10 s stands within 40 m; the hunt ends");
        Assert.True(check >= 0 && check < rig.LineAt("hunt done: 1 kill(s)"), rig.Tail());
        Assert.False(rig.Logged("hunt: fought back"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_attacker_beyond_the_reach_or_no_longer_attacking_does_not_hold_the_end_P7u(bool beyond)
    {
        // Beyond: it attacks all along, but from 50 m east (past the 40 m reach). Quiet: it attacked for half a second as the fight began, and the 200-health target took longer than 10 s to fall.
        using var rig = HuntScriptRig.Create(Hunt(kills: 1));
        rig.OnBelt = (r, n) => r.PlaceCreature(FirstCreature, Belt, 12, health: beyond ? 40u : 200u);
        Assert.True(rig.RunUntil(() => rig.Scenario.Target == FirstCreature), rig.Scenario.Failure + rig.Tail());
        rig.PlaceCreature(Wasp, WaspName, beyond ? 0 : 6, health: 60, wcid: WaspWcid, eastMetres: beyond ? 50 : 4);
        rig.AttacksPlayer(Wasp, forSeconds: beyond ? double.PositiveInfinity : 0.5);

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail(60));
        Assert.True(rig.Logged("self-defence: heard 'Gold Phyntos Wasp' attack the player"), "the attack was heard" + rig.Tail());
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(0, scenario.FoughtBack);
        Assert.True(rig.Logged("hunt: the end check: 1 of 1 kill(s) and no creature heard attacking"), rig.Tail());
        Assert.Equal(-1, rig.LastSend(GameActionTypes.TargetedMeleeAttack, Wasp));
    }

    [Fact]
    public void The_time_limit_still_ends_a_fight_back_that_outlasts_it_and_names_it_P7u()
    {
        using var rig = Belt12(Hunt(kills: 1, seconds: 60));
        Assert.True(rig.RunUntil(() => rig.Scenario.Target == FirstCreature), rig.Scenario.Failure + rig.Tail());
        rig.PlaceCreature(Wasp, WaspName, 6, health: 1_000_000, wcid: WaspWcid, eastMetres: 4); // it never falls
        rig.AttacksPlayer(Wasp);

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.StartsWith("time: 60 s of hunting passed while fighting back 'Gold Phyntos Wasp' 0x80000E01 (self-defence, D88; 1 attacker(s)) with 1 of 1 kill(s)", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(1, scenario.FoughtBack);
        Assert.True(rig.LastSend(GameActionTypes.TargetedMeleeAttack, Wasp) >= 0);
        Assert.True(scenario.Returned, "the wind-down still ran");
        Assert.False(rig.Logged("hunt: fought back"));
    }

    [Fact]
    public void Between_kills_an_attacker_in_reach_is_fought_back_before_the_grounds_next_target_P7u()
    {
        using var rig = Belt12(Hunt(kills: 3), numbered: true); // one listed creature per arrival
        Assert.True(rig.RunUntil(() => rig.Scenario.Tally.Kills >= 1), rig.Scenario.Failure + rig.Tail());
        rig.PlaceCreature(Wasp, WaspName, 6, health: 60, wcid: WaspWcid, eastMetres: 4);
        rig.AttacksPlayer(Wasp);

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail(60));
        Assert.Equal(1, scenario.DefendedBetweenKills);
        var first = rig.LineAt("acquire: target");
        var defend = rig.LineAt("acquire: 'Gold Phyntos Wasp' 0x80000E01 attacked the player");
        var second = rig.LineAt($"acquire: target '{Belt}'", defend + 1);
        Assert.True(first < defend && defend < second, $"first target {first}, the wasp {defend}, the next target {second}" + rig.Tail(60));
        Assert.Contains("fought back before the ground's targets (D88; 1 of 3 kill(s))", rig.Log.Lines[defend], StringComparison.Ordinal);
        Assert.Equal(3, scenario.Tally.Kills);
        Assert.Equal(0, scenario.FoughtBack); // nothing attacked at the end
    }
}

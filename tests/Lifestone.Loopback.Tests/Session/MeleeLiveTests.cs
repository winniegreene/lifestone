using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M4 1.2 s4b, the melee attack against the bundled ACE. WRITTEN AND COMPILED BY THE SLICE, RUN BY THE LEAD (it needs the backend with the Lifestone server mod ready and Holtburg Hunter built by
/// <c>--scenario=fighter-build</c>). On the test account the headless runner enters Holtburg Hunter and runs <see cref="HuntScenario"/> with the Mace (<c>--hunt-weapon=melee</c>, one kill), recording the session to
/// <c>cache/m4/1-2/live/</c>.
/// </summary>
/// <remarks>
/// <para><b>What it proves.</b> That <c>IActionApi.Attack</c> (GameAction 0x0008) drives a real ACE: ACE walks the player to the target and swings (the attack sequence counts <c>swings&gt;=1</c>, which is the own swing
/// F74C of <see cref="AttackSequence"/>), the blows are read from the combat notices (<c>damage_dealt&gt;0</c> from 0x01B1: the killing blow has none, so a kill without earlier hits fails), the 0x01AD names a kill
/// (<c>kills&gt;=1</c>), the hunt ends the sequence and returns to Holtburg (<c>returned=yes</c>), and the Mace is wielded at the end (<c>Restored</c>: the fighter-build scenario reads <c>wielded=331</c> again). It prints
/// <c>hunt fighter= kills= swings= shots= hits= misses= damage_dealt= ... returned=</c> and the scenario's detail. The kill levels Holtburg Brawler (on test-melee; M4 P4: it was the Hunter's test; owner decision D1); no other character is touched.</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-melee")]
[LoopbackCharacter("Holtburg Brawler")]
[LiveSharedSession]
public sealed class MeleeLiveTests(ITestOutputHelper output)
{
    [LoopbackFact]
    public void The_fighter_kills_a_creature_with_its_kit_weapon_and_returns_to_Holtburg()
    {
        var run = HuntLiveRun.Run(output, "melee", new HuntOptions { Kills = 1, Weapon = HuntWeapon.Melee }, character: FighterTemplate.Melee.Name);
        var tally = run.Scenario.Tally;

        Assert.True(run.Result.Ok, run.Result.Failure);
        Assert.Equal(0, run.Result.Errors);
        Assert.Null(run.Scenario.Failure);
        Assert.Equal("done", run.Scenario.Stage);
        Assert.Equal("ok", run.Scenario.Fighter);
        Assert.True(tally.Kills >= 1, "kills " + tally.Kills);
        Assert.True(tally.Swings >= 1, "swings " + tally.Swings + " (the own swing F74C of the attack sequence)");
        // P5a r3: the level-55 Brawler kills a belt creature with one blow, which ACE reports by the kill notice only (Player_Combat.cs 157 @95ef426d): one_blow_kills stands for the damage line then.
        Assert.True(tally.DamageDealt > 0 || run.Scenario.OneBlowKills >= 1, "damage_dealt " + tally.DamageDealt + " one_blow_kills " + run.Scenario.OneBlowKills + " (the killing blow has no 0x01B1 line)");
        Assert.True(run.Scenario.Returned, "returned=no (the run did not end in Holtburg's landblock)");
        Assert.True(run.Scenario.Restored, "the kit's weapon " + FighterTemplate.Melee.WieldedWeaponWcid + " was not wielded at the end");
    }
}

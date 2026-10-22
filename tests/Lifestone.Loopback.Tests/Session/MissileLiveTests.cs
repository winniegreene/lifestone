using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M4 1.2 s4b, the missile attack against the bundled ACE. WRITTEN AND COMPILED BY THE SLICE, RUN BY THE LEAD (it needs the backend with the Lifestone server mod ready and Holtburg Hunter built by
/// <c>--scenario=fighter-build</c>, with the Shortbow and the Arrows in its pack). On the test account the headless runner enters Holtburg Hunter and runs <see cref="HuntScenario"/> with the bow
/// (<c>--hunt-weapon=missile</c>, one kill), recording the session to <c>cache/m4/1-2/live/</c>.
/// </summary>
/// <remarks>
/// <para><b>What it proves.</b> The weapon swap of capture C2 (the Mace to the pack, the Shortbow and then the Arrows wielded, in that order, which ACE needs) and that <c>IActionApi.MissileAttack</c> (GameAction 0x000A)
/// drives a real ACE: arrows are launched (<c>shots&gt;=1</c>, each fall of the wielded ammo's stack in 0x0197 or an arrow's F745), at least one lands (<c>hits&gt;=1</c> from 0x01B1), and the hunt returns to Holtburg
/// (<c>returned=yes</c>) with the swap undone: the bow and the arrows are back in the pack and the Mace is wielded (<c>Restored</c>, which the scenario sets only after it found both in the pack and the Mace wielded). It
/// prints the same lines as <see cref="MeleeLiveTests"/>. The shots burn arrows of the kit's stack; the kill levels Holtburg Hunter (owner decision D1).</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
public sealed class MissileLiveTests(ITestOutputHelper output)
{
    [LoopbackFact]
    public void The_fighter_shoots_a_creature_with_the_Shortbow_and_restores_the_Mace()
    {
        var run = HuntLiveRun.Run(output, "missile", new HuntOptions { Kills = 1, Weapon = HuntWeapon.Missile });
        var tally = run.Scenario.Tally;

        Assert.True(run.Result.Ok, run.Result.Failure);
        Assert.Equal(0, run.Result.Errors);
        Assert.Null(run.Scenario.Failure);
        Assert.Equal("done", run.Scenario.Stage);
        Assert.True(tally.Shots >= 1, "shots " + tally.Shots);
        Assert.True(tally.Hits >= 1, "hits " + tally.Hits + " (0x01B1 lines the player dealt)");
        Assert.True(run.Scenario.Returned, "returned=no (the run did not end in Holtburg's landblock)");
        Assert.True(run.Scenario.Restored, "the bow and the arrows are not back in the pack with the Mace wielded");
    }
}

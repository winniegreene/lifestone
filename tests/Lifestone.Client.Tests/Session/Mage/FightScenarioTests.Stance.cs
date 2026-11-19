using Lifestone.Client.Session.Scenarios;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 sprint (m6-stance-after-read; run 6, 2026-11-19 21:14:45 to 21:19 UTC): ACE put her in peace for an on-the-spot scroll read (Scroll.ActOnUse sets NonCombat), and every later bolt was refused by the stance guard and decided
/// again the next frame: 5,016 refusals in four minutes while a Drudge Skulker hit her. A cast refused for the stance now asks the magic stance, the casts wait for its answer, and the fight goes on. Over the same scripted ACE as
/// the other fight tests.
/// </summary>
public sealed partial class FightScenarioTests
{
    [Fact]
    public void A_cast_refused_for_the_stance_asks_the_magic_stance_once_and_the_fight_is_won_M6_sprint()
    {
        var r = new Rig(fight: LootRetryFight(kills: 1)) { Corpses = true };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        var scenario = r.Scenario(new FakeWalk());
        var status = ScenarioStatus.Running;
        for (var i = 0; i < 4000 && status == ScenarioStatus.Running && !r.ModesSent.Contains(8); i++)
        {
            status = r.Frame(scenario);
        }

        Assert.Equal(ScenarioStatus.Running, status);
        r.R.CombatModeIs(1); // ACE's NonCombat, as a scroll read sets it
        var asked = r.ModesSent.Count;

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario, 120));
        Assert.Contains(8, r.ModesSent.Skip(asked)); // the magic stance asked again
        Assert.InRange(LinesWith(r, "the player is not in the magic combat mode"), 1, 2); // the refusal that called the ask, never a loop
        Assert.Equal(1, LinesWith(r, "act: the magic stance is asked (ask 1 of this fight)"));
        Assert.Equal(1, LinesWith(r, "stance: check after: Confirmed"));
        Assert.Equal(1, scenario.StanceAsks);
    }
}

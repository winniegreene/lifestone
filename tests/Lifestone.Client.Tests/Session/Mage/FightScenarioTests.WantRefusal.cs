using Lifestone.Client.Session.Scenarios;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 5 F1 (review 5-R4): a hunt-until whose only item the server refuses at the pick-up (the tusk's 20 h gate) can never be met; the fight ends by name at its safe point, its verdict says <c>until=refused</c>, instead of hunting to the limit.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint RefusedTusk = 31_001;

    [Fact]
    public void A_refused_pick_up_of_the_until_item_ends_the_fight_with_until_refused()
    {
        var r = new Rig(fight: new FightOptions { Kills = 5, Seconds = 600, Loot = true, LootThresholds = RigLoot, LootAggroMetres = 0, Want = [RefusedTusk], Until = new FightUntil(RefusedTusk, 1, null) }) { Corpses = true, MoveToSeconds = 3 };
        const uint tuskGuid = 0x80007101;
        r.CorpseItems.Add((tuskGuid, RefusedTusk, 0x80, 0, 100, "Tusker Tusk"));
        r.RefusePicks.Add(tuskGuid);
        r.Add(0x80001001, TuskerSlave, Den, 30);
        r.Add(0x80001002, TuskerSlave, Den, 34);
        var scenario = r.Scenario(new FakeWalk());

        var status = r.Run(scenario);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains(r.Log.Lines, l => l.Contains("loot: want 31001 refused by the server: You have solved this quest too recently!", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("fight: until: refused: 31001 (You have solved this quest too recently!)", StringComparison.Ordinal));
        Assert.EndsWith(" until=refused", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(r.Picked); // no second try on the second corpse: the wcid is no longer wanted
        Assert.True(scenario.Tally.Kills < 5);
    }
}

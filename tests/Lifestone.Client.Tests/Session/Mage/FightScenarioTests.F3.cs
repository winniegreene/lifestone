using Lifestone.Client.Session.Scenarios;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>M6 6 F3 (<c>m6-resume-loot-queue</c>): the fight exports its kill/corpse pairs not yet looted, and a fresh fight queues a stopped run's pairs that are still in view and ours.</summary>
public sealed partial class FightScenarioTests
{
    [Fact]
    public void The_fight_exports_the_corpses_found_and_not_yet_looted_and_none_once_looted_M6_6_F3()
    {
        var r = new Rig(fight: LootRetryFight()) { Corpses = true };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        r.Add(SlaveB, TuskerSlave, Den, 34, hitsToKill: 100_000); // never dies: corpse A stays put off beside it
        var scenario = r.Scenario(new FakeWalk());
        Assert.Empty(scenario.KillCorpses);

        for (var i = 0; i < 400 && LinesWith(r, "is put off:") == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal([new KillCorpse(CorpseA, "Tusker Slave")], scenario.KillCorpses);

        var done = new Rig(fight: LootRetryFight(kills: 1)) { Corpses = true };
        done.Add(SlaveA, TuskerSlave, Den, 30);
        var looting = done.Scenario(new FakeWalk());
        for (var i = 0; i < 400 && (done.Opened.Count < 1 || looting.LootBusy); i++)
        {
            done.Frame(looting);
        }

        Assert.Equal([CorpseA], done.Opened);
        Assert.Empty(looting.KillCorpses);
    }

    [Fact]
    public void A_resumed_fight_queues_the_saved_corpses_in_view_and_ours_and_drops_a_stale_one_by_name_M6_6_F3()
    {
        var r = new Rig(fight: LootRetryFight()) { Corpses = true };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        r.Add(SlaveB, TuskerSlave, Den, 34, hitsToKill: 100_000);
        var first = r.Scenario(new FakeWalk());
        for (var i = 0; i < 400 && LinesWith(r, "is put off:") == 0; i++)
        {
            r.Frame(first);
        }

        var saved = first.KillCorpses;
        var resumed = r.Scenario(new FakeWalk());
        var queued = resumed.SeedLoot(r.View, [.. saved, new KillCorpse(0x80009999, "Tusker Guard"), new KillCorpse(CorpseA + 0, "Tusker Guard")]);

        Assert.Equal(1, queued);
        Assert.Equal(saved, resumed.KillCorpses);
        Assert.Equal(1, LinesWith(r, "the resumed run's corpse 0x80009999 of 'Tusker Guard' is dropped (not in view)"));
        Assert.Equal(1, LinesWith(r, "is dropped (it is 'Corpse of Tusker Slave' now, not the corpse of 'Tusker Guard')"));
        Assert.Equal(0, resumed.SeedLoot(r.View, saved)); // the corpse is already queued: not twice
        Assert.Equal(1, LinesWith(r, "is dropped (already queued or looted)"));
    }
}

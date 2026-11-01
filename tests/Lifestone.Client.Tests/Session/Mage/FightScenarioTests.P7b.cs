using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 item 7, slice P7b (D139 rows g, i, j), over the scripted ACE: the corpse of every creature the mage killed is queued, not only the target's (i); a later kill no longer takes an earlier put-off kill's corpse, and a put-off corpse
/// lost from view is walked back to and looted when the server sends it again, or given up for a named reason (j); a walk stuck for <see cref="FightScenario.WalkStuckRungsGiveUp"/> rungs is given up, its cell and creature rested,
/// and the fight picks again (g). Each test fails before the slice's change.
/// </summary>
public sealed partial class FightScenarioTests
{
    [Theory]
    [InlineData("You bring Tusker Slave to a fiery end!", "Tusker Slave", true)]
    [InlineData("You bring Tusker Slave to a fiery end!", "Tusker", false)]
    [InlineData("Tusker Slave's seared corpse smolders before you!", "Tusker Slave", true)]
    [InlineData("Blistered by lightning, Tusker Guard falls!", "Tusker Guard", true)]
    [InlineData("You killed Virindi Servant!", "Virindi Servant", true)]
    [InlineData("You killed Virindi Servant!", "Virindi", false)]
    [InlineData("You flatten Tusker Slave's body with the force of your assault!", "Tusker Slave", true)]
    public void A_kill_notice_names_its_victim_as_whole_words_M5_7_P7b_i(string text, string name, bool named) =>
        Assert.Equal(named, FightScenario.NoticeNames(text, name));

    [Fact]
    public void A_neighbour_the_targets_ring_killed_has_its_corpse_queued_and_opened_M5_7_P7b_D139_i()
    {
        var r = new Rig(fight: LootRetryFight(kills: 2) with { LootAggroMetres = 0 }) { Corpses = true };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        var neighbour = r.Add(SlaveB, TuskerSlave, Den, 33, hitsToKill: 100); // never killed by a cast of its own
        r.Splash[SlaveA] = neighbour; // the target's last hit (the ring) also kills the neighbour
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario)); // two kills: the fight ends once both corpses are done

        Assert.True(neighbour.Dead);
        Assert.Equal(2, scenario.Tally.Kills);
        Assert.Equal([CorpseA, CorpseB], r.Opened); // before P7b only the target's corpse was queued: the neighbour's lay unopened
        Assert.Equal(1, LinesWith(r, "loot: splash kill: the kill notice \"You bring Tusker Slave to a fiery end!\""));
        Assert.Equal(1, LinesWith(r, "loot: the splash kill of 'Tusker Slave' (its corpse 0x80001102) waits for the loot step"));
        Assert.Equal(0, LinesWith(r, "met no fresh corpse"));
        Assert.Equal(0, scenario.LootCorpsesLeft);
    }

    [Fact]
    public void A_later_kill_waits_for_its_own_corpse_and_leaves_the_put_off_corpse_to_its_kill_M5_7_P7b_D139_j()
    {
        // Round 13 (21 of 24 "gone from the world"): the second kill was settled by the health at 0 before its corpse appeared, took the nearest corpse by name (the first kill's, put off), and the first kill's retry then found
        // its corpse handled; the second kill's own corpse was never opened.
        var r = new Rig(fight: LootRetryFight(kills: 3)) { Corpses = true, DeathDelay = 4, CorpseAtDeletion = true, HealthZeroAtKill = true };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        r.Add(SlaveB, TuskerSlave, Den, 34); // 4 m from the first corpse: it is put off while this one lives
        var scenario = r.Scenario(new FakeWalk());

        for (var i = 0; i < 800 && (r.Opened.Count < 2 || scenario.LootBusy); i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(1, LinesWith(r, "is put off: Tusker Slave 0x80001002 stands"));
        Assert.Equal([CorpseB, CorpseA], r.Opened); // each kill its own corpse
        Assert.Equal(0, LinesWith(r, "gone from the world"));
        Assert.Contains(" loot_left_decayed=0 ", scenario.LootFields(), StringComparison.Ordinal);
        Assert.Equal(0, scenario.LootCorpsesLeft);
    }

    [Fact]
    public void A_put_off_corpse_lost_from_view_is_walked_back_to_and_looted_when_it_is_sent_again_M5_7_P7b_D139_j()
    {
        var r = new Rig(fight: LootRetryFight(kills: 3)) { Corpses = true };
        var a = r.Add(SlaveA, TuskerSlave, Den, 30);
        r.Add(SlaveB, TuskerSlave, Den, 34, hitsToKill: 6);
        var walk = new FakeWalk { TicksPerLeg = 40 };
        var scenario = r.Scenario(walk);

        for (var i = 0; i < 400 && LinesWith(r, "is put off:") == 0; i++)
        {
            r.Frame(scenario);
        }

        r.R.Raw(AceWireWriters.ObjectDelete(CorpseA, 1)); // the client's cull: 25 s out of view (the mage chased the next tusker off)
        var resent = false;
        for (var i = 0; i < 600 && !r.Opened.Contains(CorpseA); i++)
        {
            r.Frame(scenario);
            if (walk.Spots.Count > 0 && !resent)
            {
                resent = true;
                r.LeaveCorpse(a); // back in view on the way: ACE sends it again
            }
        }

        Assert.Equal(1, LinesWith(r, "left the object table before it could rot: lost from view"));
        Assert.Single(walk.Spots);
        Assert.Equal(Den, walk.Spots[0].CellId);
        Assert.Equal(1, LinesWith(r, "walking: purpose=loot (the corpse 0x80001101 of 'Tusker Slave'"));
        Assert.Equal(1, LinesWith(r, "loot: the walk back: the corpse 0x80001101 is sent again"));
        Assert.Contains(CorpseA, r.Opened); // before P7b: "given up, left to decay: it is gone from the world"
        Assert.Equal(0, LinesWith(r, "gone from the world"));
        Assert.Contains(" loot_left_decayed=0 ", scenario.LootFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_walk_back_that_ends_short_of_the_spot_gives_the_corpse_up_for_a_named_reason_M5_7_P7b_D139_j()
    {
        var r = new Rig(fight: LootRetryFight(kills: 3)) { Corpses = true };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        r.Add(SlaveB, TuskerSlave, Den, 34, hitsToKill: 6);
        var walk = new FakeWalk { TicksPerLeg = 4 }; // the fake walk moves no one: the leg ends where the mage stands, 10 m from the spot
        var scenario = r.Scenario(walk);

        for (var i = 0; i < 400 && LinesWith(r, "is put off:") == 0; i++)
        {
            r.Frame(scenario);
        }

        r.R.Raw(AceWireWriters.ObjectDelete(CorpseA, 1));
        for (var i = 0; i < 600 && LinesWith(r, "the walk back ended") == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.Single(walk.Spots);
        Assert.Equal(1, LinesWith(r, "is given up, left to decay: the walk back ended 10 m from where it lay, beyond 5 m"));
        Assert.DoesNotContain(CorpseA, r.Opened);
        Assert.Equal(0, LinesWith(r, "gone from the world"));
        Assert.Contains(" loot_left_decayed=1 ", scenario.LootFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_walk_stuck_four_rungs_toward_a_seen_tusker_is_given_up_its_cell_and_creature_rested_and_the_fight_picks_again_M5_7_P7b_D139_g()
    {
        var r = new Rig();
        var far = r.Add(TuskerGuid, TuskerSlave, FloorCell, 80); // 60 m, range 30: only a walk reaches it
        var tour = new RecordingTour { Purpose = TourPurpose.Seen, PurposeGuid = TuskerGuid };
        var walk = new FakeWalk { Tour = tour, TicksPerLeg = 400, StuckLegs = 1 }; // the first leg sticks and would walk on for 100 s
        var scenario = ToTheFight(r, r.Scenario(walk));

        for (var i = 0; i < 40; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(1, LinesWith(r, $"walk: given up: {FightScenario.WalkStuckRungsGiveUp} stuck rungs in a row on the leg to 0x01050320 (purpose seen: a living Tusker Slave 0x80001001)"));
        Assert.Equal(1, LinesWith(r, "the cell 0x01050320 rests 120 s and the creature 0x80001001 is not walked toward for 120 s; the fight picks again"));
        Assert.Equal([0x01050320u], tour.Failed);
        Assert.Equal([TuskerGuid], tour.Avoided);
        Assert.Equal(2, walk.Legs); // a new leg (the tour's next offer) began at once; before P7b the stuck leg ran on
        Assert.Equal(1, walk.Abandons);
        Assert.False(far.Dead);
        Assert.Equal(4, FightScenario.WalkStuckRungsGiveUp);
    }
}

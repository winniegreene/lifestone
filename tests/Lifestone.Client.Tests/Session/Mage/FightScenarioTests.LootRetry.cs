using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 item 7, the loot retry: a corpse is put off only when a creature is attacking the mage now or a living target creature stands within 6 m of it; it stays queued, is retried when the fight has no target and nothing is in the way, in the order of the
/// kills, and is given up (counted as left to decay) 270 s after the kill or when it is gone from the world. Over the same scripted ACE as the other loot tests; the corpses here are empty (the loot rule is not what is tested).
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint SlaveA = 0x80001001;
    private const uint SlaveB = 0x80001002;
    private const uint CorpseA = SlaveA + Rig.CorpseOffset;
    private const uint CorpseB = SlaveB + Rig.CorpseOffset;

    private static FightOptions LootRetryFight(int kills = 3, double defendForget = 20) => new() { Kills = kills, Seconds = 1000, Loot = true, LootThresholds = RigLoot, DefendForgetSeconds = defendForget };

    /// <summary>One frame like <see cref="Rig.Frame"/>, with a hook between the scripted ACE's answers and the scenario's step (a server message that arrives in the same tick as the kill).</summary>
    private static void FrameWith(Rig r, FightScenario scenario, Action between)
    {
        r.R.Tick(0.25);
        r.Pump();
        r.R.Tick();
        between();
        scenario.Step(r.View, r.Now);
    }

    [Fact]
    public void The_loot_aggro_reach_defaults_to_six_metres_not_the_death_trips_twenty()
    {
        Assert.Equal(6, FightOptions.DefaultLootAggroMetres);
        Assert.Equal(6, new FightOptions().LootAggroMetres);
        Assert.Equal(20, DeathOptions.DefaultTripAggroMetres); // the death trip's own reach is left alone
        Assert.DoesNotContain("loot_aggro_m", new FightOptions { Loot = true }.ToString(), StringComparison.Ordinal);
        Assert.Contains("loot_aggro_m=20", new FightOptions { Loot = true, LootAggroMetres = 20 }.ToString(), StringComparison.Ordinal);
        Assert.Equal(270, FightOptions.LootRetryWindowSeconds);
    }

    [Fact]
    public void A_corpse_put_off_by_a_tusker_beside_it_stays_queued_and_is_looted_after_the_next_one_when_the_fight_has_no_target()
    {
        var r = new Rig(fight: LootRetryFight()) { Corpses = true };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        r.Add(SlaveB, TuskerSlave, Den, 34); // 4 m from the first corpse: inside the 6 m reach
        var scenario = r.Scenario(new FakeWalk());

        for (var i = 0; i < 400 && (r.Opened.Count < 2 || scenario.LootBusy); i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal([CorpseB, CorpseA], r.Opened); // the second kill's corpse first (nothing beside it), the first kill's once the fight had no target
        Assert.Equal(1, LinesWith(r, "is put off: Tusker Slave 0x80001002 stands"));
        Assert.Equal(1, LinesWith(r, "0x80001101 of 'Tusker Slave' put off "));
        Assert.Contains(r.Log.Lines, l => l.Contains("is retried: no target in reach and nothing in the way", StringComparison.Ordinal));
        Assert.Equal(0, scenario.LootCorpsesLeft);
        Assert.Contains(" loot_skipped_hostile=1 ", scenario.LootFields(), StringComparison.Ordinal);
        Assert.EndsWith(" corpses_opened=2 loot_left_aggro=1 loot_left_decayed=0 worth_a_look=0 stack_merges=0 loot_busy_waits=0 loot_left_busy=0", scenario.LootFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_corpse_put_off_that_nothing_clears_is_given_up_after_270_seconds_counted_as_left_to_decay()
    {
        var r = new Rig(fight: LootRetryFight()) { Corpses = true };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        r.Add(SlaveB, TuskerSlave, Den, 34, hitsToKill: 100_000); // never dies: the fight never has "no target", and it stays 4 m from the corpse
        var scenario = r.Scenario(new FakeWalk());
        var start = r.Now;

        while (r.Now < start + 250)
        {
            r.Frame(scenario);
        }

        Assert.Empty(r.Opened);
        Assert.Contains(" loot_left_aggro=1 loot_left_decayed=0 worth_a_look=0", scenario.LootFields(), StringComparison.Ordinal);
        Assert.Equal(0, scenario.LootCorpsesLeft);

        while (r.Now < start + 300)
        {
            r.Frame(scenario);
        }

        Assert.Empty(r.Opened);
        Assert.Equal(1, scenario.LootCorpsesLeft);
        Assert.Contains(" corpses_opened=0 loot_left_aggro=1 loot_left_decayed=1 worth_a_look=0", scenario.LootFields(), StringComparison.Ordinal);
        Assert.Equal(1, LinesWith(r, "left to decay: its retry window of 270 s from the kill has passed"));
    }

    [Fact]
    public void A_corpse_put_off_that_leaves_the_table_inside_its_window_is_kept_as_lost_from_view_not_given_up_M5_7_P7b_D139_j()
    {
        // Before P7b (D139 j) such a corpse was given up at once ("gone from the world"). A monster corpse rots 300 s after the kill (WorldObject_Decay.cs:18), so one that leaves the table inside the 270 s window was lost from view
        // (the client's 25 s cull; ACE sends no delete): it stays queued with its spot, and here (the neighbour never dies, so the fight never has no target) its window passes.
        var r = new Rig(fight: LootRetryFight()) { Corpses = true };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        r.Add(SlaveB, TuskerSlave, Den, 34, hitsToKill: 100_000);
        var scenario = r.Scenario(new FakeWalk());
        var start = r.Now;

        for (var i = 0; i < 400 && LinesWith(r, "is put off:") == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(1, LinesWith(r, "is put off:"));
        r.R.Raw(AceWireWriters.ObjectDelete(CorpseA, 1)); // the client stopped holding it
        r.Frame(scenario);

        Assert.Equal(0, scenario.LootCorpsesLeft);
        Assert.Contains(" loot_left_aggro=1 loot_left_decayed=0 worth_a_look=0", scenario.LootFields(), StringComparison.Ordinal);
        Assert.Equal(1, LinesWith(r, "loot: the corpse 0x80001101 of 'Tusker Slave' (killed "));
        Assert.Equal(1, LinesWith(r, "left the object table before it could rot: lost from view"));
        Assert.Equal(0, LinesWith(r, "gone from the world"));

        while (r.Now < start + 300)
        {
            r.Frame(scenario);
        }

        Assert.Equal(1, scenario.LootCorpsesLeft);
        Assert.Equal(1, LinesWith(r, "left to decay: its retry window of 270 s from the kill has passed (lost from view"));
        Assert.Empty(r.Opened);
    }

    [Fact]
    public void A_creature_attacking_the_mage_puts_a_corpse_off_until_it_has_been_quiet_and_then_the_corpse_is_looted()
    {
        var r = new Rig(fight: LootRetryFight(kills: 3, defendForget: 5)) { Corpses = true };
        var a = r.Add(SlaveA, TuskerSlave, Den, 30);
        r.Add(0x80002001, Virindi, Den, 60, "Virindi", hitsToKill: 100_000); // 30 m from the corpse, 40 m from the player: not beside the corpse, out of the bolt's reach, avoided by the profile
        var scenario = r.Scenario(new FakeWalk());
        var hit = false;
        var attackAt = double.NaN;

        for (var i = 0; i < 160 && (r.Opened.Count == 0 || scenario.LootBusy); i++)
        {
            FrameWith(r, scenario, () =>
            {
                if (a.Dead && !hit)
                {
                    hit = true;
                    attackAt = r.Now;
                    MeleeHit(r); // the Virindi hits the mage in the tick the tusker dies
                }
            });
            if (hit && r.Now < attackAt + 4.5)
            {
                Assert.Empty(r.Opened); // put off while the attack is fresh
            }
        }

        Assert.True(hit);
        Assert.Contains(r.Log.Lines, l => l.Contains("is put off: Virindi 0x80002001 attacked the mage", StringComparison.Ordinal));
        Assert.Equal([CorpseA], r.Opened);
        Assert.True(r.Now >= attackAt + 5, "the corpse waited for the attacker to be forgotten"); // DefendForgetSeconds 5
        Assert.Equal(0, scenario.LootCorpsesLeft);
        Assert.EndsWith(" corpses_opened=1 loot_left_aggro=1 loot_left_decayed=0 worth_a_look=0 stack_merges=0 loot_busy_waits=0 loot_left_busy=0", scenario.LootFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_attack_while_ACEs_walk_to_the_corpse_is_in_hand_puts_the_corpse_back_and_the_fight_goes_first()
    {
        var r = new Rig(fight: LootRetryFight(kills: 3, defendForget: 5)) { Corpses = true, MoveToSeconds = 3 };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        r.Add(0x80002001, Virindi, Den, 60, "Virindi", hitsToKill: 100_000);
        var scenario = r.Scenario(new FakeWalk());

        for (var i = 0; i < 100 && r.Opened.Count == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal([CorpseA], r.Opened); // the Use is sent, ACE's walk is under way
        MeleeHit(r);
        r.Frame(scenario);

        Assert.Contains(r.Log.Lines, l => l.Contains("is put off: Virindi 0x80002001 attacked the mage", StringComparison.Ordinal) && l.Contains("while the corpse was being reached or read", StringComparison.Ordinal) && l.Contains("the fight goes first", StringComparison.Ordinal));
        Assert.False(scenario.LootBusy);
        Assert.Contains(" loot_skipped_hostile=1 ", scenario.LootFields(), StringComparison.Ordinal);
        Assert.Contains(" loot_left_aggro=1 ", scenario.LootFields(), StringComparison.Ordinal);
        Assert.Equal(0, scenario.LootCorpsesLeft); // still queued, not left

        for (var i = 0; i < 160 && (scenario.LootBusy || !scenario.LootFields().Contains("looted=1", StringComparison.Ordinal)); i++)
        {
            r.Frame(scenario);
        }

        // No movement controller is bound in this rig, so no key could end the walk (M6 14: named, m6-loot-walk-cut-refused; with one bound the walk ends, FightScenarioTests.LootWalkCut.cs). The rig's ACE finished its walk and
        // opened the corpse after the put-off, so the retry found it already open (nothing was sent again) and looted it.
        Assert.Equal(1, LinesWith(r, "can not be ended: no movement controller is bound, so no key can be pressed"));
        Assert.Equal(1, LinesWith(r, "(m6-loot-walk-cut-refused)"));
        Assert.Equal(0, scenario.LootWalkCuts);
        Assert.Equal(1, scenario.LootWalkCutsUnseen);
        Assert.Contains(r.Log.Lines, l => l.Contains("put off ", StringComparison.Ordinal) && l.Contains("is retried: no target in reach and nothing in the way", StringComparison.Ordinal));
        Assert.Equal([CorpseA], r.Closed);
        Assert.EndsWith(" corpses_opened=1 loot_left_aggro=1 loot_left_decayed=0 worth_a_look=0 stack_merges=0 loot_busy_waits=0 loot_left_busy=0", scenario.LootFields(), StringComparison.Ordinal);
        Assert.Equal(0, scenario.LootCorpsesLeft);
    }

    [Fact]
    public void With_the_aggro_reach_at_zero_neither_a_tusker_nor_an_attacker_puts_a_corpse_off()
    {
        var r = new Rig(fight: LootRetryFight(kills: 1) with { LootAggroMetres = 0 }) { Corpses = true };
        r.Add(SlaveA, TuskerSlave, Den, 30);
        r.Add(SlaveB, TuskerSlave, Den, 34, hitsToKill: 100_000);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal([CorpseA], r.Opened);
        Assert.EndsWith(" corpses_opened=1 loot_left_aggro=0 loot_left_decayed=0 worth_a_look=0 stack_merges=0 loot_busy_waits=0 loot_left_busy=0", scenario.LootFields(), StringComparison.Ordinal);
    }
}

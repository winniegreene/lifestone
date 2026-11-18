using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 (live 2026-11-12, 06:42 to 07:53 UTC: five times she stood 8 to 16 s under a tusker's hits with full mana before her bolt went). A Use of a corpse beyond reach lets ACE walk her to it and is answered only when the
/// walk ends; the Action API never has a Use and a cast out together, so the bolt the fight asked at the attacker waited for the walk. Here: the walk is ended with a movement key the moment the corpse is put off, and the bolt
/// goes in a normal cast time; a key that ends nothing is named and the cast-wait line says what the cast waits behind; and ACE's walk does not start while a hostile is in reach of her. Over the scripted ACE of the other
/// loot tests, with a movement controller bound that records the keys (<c>Rig.BindKeys</c>).
/// </summary>
public sealed partial class FightScenarioTests
{
    /// <summary>The walk's length in the replays: the live walks took 6.9 to 7.9 s when they arrived.</summary>
    private const double LiveWalkSeconds = 8;

    /// <summary>The frames until the scripted ACE walks her to the first corpse (the Use is sent and unanswered).</summary>
    private static void ToTheWalk(Rig r, FightScenario scenario)
    {
        for (var i = 0; i < 200 && r.Opened.Count == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal([CorpseA], r.Opened);
        Assert.True(r.Walking, "the Use is out and ACE walks her to the corpse");
    }

    [Fact]
    public void A_tusker_that_attacks_during_ACEs_walk_to_a_corpse_ends_the_walk_with_a_key_and_the_bolt_goes_in_a_normal_cast_time_M6_14()
    {
        var r = new Rig(fight: LootRetryFight()) { Corpses = true, MoveToSeconds = LiveWalkSeconds };
        var keys = r.BindKeys();
        r.Add(SlaveA, TuskerSlave, Den, 30); // its corpse lies 10 m from her: beyond reach, so the Use lets ACE walk
        var scenario = r.Scenario(new FakeWalk());
        ToTheWalk(r, scenario);
        Assert.Empty(keys.Commands);

        r.Add(SlaveB, TuskerSlave, Den, 22); // a tusker comes up to her, 2 m off
        MeleeHit(r, "Tusker Slave");
        var attackAt = r.Now;
        var casts = r.CastTargets.Count;
        var landed = LinesWith(r, "fight bolt: landed");
        for (var i = 0; i < 80 && LinesWith(r, "fight bolt: landed") == landed; i++)
        {
            r.Frame(scenario);
        }

        // The walk had 8 s to go; the bolt at the attacker is out and answered in the time of a cast (the rig's is 0.5 s, plus the frames of the key, the Use's answer and the send).
        Assert.True(LinesWith(r, "fight bolt: landed") > landed, "the bolt landed");
        Assert.True(r.Now - attackAt <= 2.0, $"the bolt landed {r.Now - attackAt:0.00} s after the attack");
        Assert.Equal(SlaveB, r.CastTargets[casts]);
        Assert.Equal([(MoveIntents.EndServerMoveCommand, true), (MoveIntents.EndServerMoveCommand, false)], keys.Commands); // one key, pressed and released
        Assert.False(r.Walking);
        Assert.Equal(1, scenario.LootWalkCuts);
        Assert.Equal(0, scenario.LootWalkCutsUnseen);
        Assert.Equal(1, LinesWith(r, "is put off: Tusker Slave 0x80001002 attacked the mage"));
        var cut = Assert.Single(r.Log.Lines, l => l.Contains("is ended as a key ends it (the corpse is put off)", StringComparison.Ordinal));
        Assert.Contains("ACE's walk to the corpse 0x80001101 of 'Tusker Slave'", cut, StringComparison.Ordinal);
        Assert.Contains("server walks her: yes", cut, StringComparison.Ordinal); // the check before
        Assert.Contains("(m6-loot-walk-cut)", cut, StringComparison.Ordinal);
        var ended = Assert.Single(r.Log.Lines, l => l.Contains("ACE's walk to the corpse 0x80001101 of 'Tusker Slave' ended: the Use #", StringComparison.Ordinal));
        Assert.Contains("ServerError", ended, StringComparison.Ordinal); // a UseDone 0 with no contents: the walk did not arrive
        Assert.Contains("the body's own MoveTo ended: cancelled (0x0036 ActionCancelled); server walks her: no; the fight casts now", ended, StringComparison.Ordinal); // the check after
        var lines = r.Log.Lines.ToList();
        Assert.True(lines.IndexOf(ended) < lines.FindIndex(l => l.Contains("fight bolt: try 1", StringComparison.Ordinal) && l.Contains("at 0x80001002", StringComparison.Ordinal)), "the bolt is asked after the walk's end is read");
        Assert.Equal(0, LinesWith(r, "(m6-fight-idle)"));
        Assert.Equal(0, scenario.CastWaitLines);

        // The corpse stayed queued: the attacker dies, its own corpse is looted, and the first corpse is retried when the fight has no target.
        for (var i = 0; i < 400 && (scenario.LootBusy || !scenario.LootFields().Contains("looted=2", StringComparison.Ordinal)); i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal([CorpseA, CorpseB, CorpseA], r.Opened);
        Assert.Equal(0, scenario.LootCorpsesLeft);
        Assert.Equal(1, scenario.LootWalkCuts);
    }

    [Fact]
    public void A_key_that_ends_nothing_is_named_the_fight_casts_anyway_and_the_cast_wait_line_says_what_the_cast_waits_behind_M6_14()
    {
        var r = new Rig(fight: LootRetryFight()) { Corpses = true, MoveToSeconds = LiveWalkSeconds, KeysIgnored = true };
        r.BindKeys();
        r.Add(SlaveA, TuskerSlave, Den, 30);
        var scenario = r.Scenario(new FakeWalk());
        ToTheWalk(r, scenario);

        r.Add(SlaveB, TuskerSlave, Den, 22);
        MeleeHit(r, "Tusker Slave");
        var attackAt = r.Now;
        var landed = LinesWith(r, "fight bolt: landed");
        for (var i = 0; i < 80 && LinesWith(r, "fight bolt: landed") == landed; i++)
        {
            r.Frame(scenario);
            if (i % 8 == 7)
            {
                MeleeHit(r, "Tusker Slave"); // it goes on hitting her
            }
        }

        var late = Assert.Single(r.Log.Lines, l => l.Contains("(m6-loot-walk-cut-late)", StringComparison.Ordinal));
        Assert.Contains("was not seen to end within 1 s of the key", late, StringComparison.Ordinal);
        Assert.Contains("is still unanswered; server walks her: yes", late, StringComparison.Ordinal);
        Assert.Contains("the fight casts anyway", late, StringComparison.Ordinal);
        Assert.Equal(1, scenario.LootWalkCuts);
        Assert.Equal(1, scenario.LootWalkCutsUnseen);

        // The cast was asked a second after the attack and waited in the Action API behind the Use: the line names the Use by number and kind, and says the cast was never sent.
        Assert.True(scenario.CastWaitLines >= 1, $"cast wait lines {scenario.CastWaitLines}");
        var wait = r.Log.Lines.First(l => l.Contains("(m6-cast-wait)", StringComparison.Ordinal));
        Assert.Contains("cast wait: an attacker is on her and the cast #", wait, StringComparison.Ordinal);
        Assert.Contains("not sent yet: it waits its turn in the Action API", wait, StringComparison.Ordinal);
        Assert.Contains(" OpenContainer sent, unanswered ", wait, StringComparison.Ordinal);
        Assert.Contains(" CastSpell not sent, waiting ", wait, StringComparison.Ordinal);
        Assert.Contains("server walks her: yes", wait, StringComparison.Ordinal);
        Assert.Contains("motion: the rig's ACE walks her", wait, StringComparison.Ordinal);

        // The walk ended on its own (it arrived): the end is logged, and only then did the bolt go.
        var ended = Assert.Single(r.Log.Lines, l => l.Contains("ACE's walk to the corpse 0x80001101 of 'Tusker Slave' ended: the Use #", StringComparison.Ordinal));
        Assert.Contains("Confirmed", ended, StringComparison.Ordinal);
        Assert.Contains("the walk had arrived", ended, StringComparison.Ordinal);
        Assert.True(r.Now - attackAt >= LiveWalkSeconds - 1, $"the bolt landed {r.Now - attackAt:0.00} s after the attack: it waited for the walk");
    }

    [Fact]
    public void ACEs_walk_to_a_corpse_does_not_start_while_the_fight_has_a_target_in_sight_M6_14()
    {
        // Each live walk began in the frame a tusker came into range and line ("sight: the walk is dropped; ... is in range with a clear line", then the Use of a corpse 18 m the other way).
        var r = new Rig(fight: LootRetryFight()) { Corpses = true, MoveToSeconds = 1 };
        var a = r.Add(SlaveA, TuskerSlave, Den, 30); // its corpse: 10 m from her, a walk longer than the 6 m aggro reach
        var b = r.Add(SlaveB, TuskerSlave, Den, 45); // 25 m from her, in the bolt's range with a clear line; 15 m from the corpse, so the corpse's own aggro rule says nothing
        var scenario = r.Scenario(new FakeWalk());

        for (var i = 0; i < 400 && !b.Dead; i++)
        {
            r.Frame(scenario);
            Assert.Empty(r.Opened); // no Use, so no walk, while the second tusker lives
        }

        Assert.True(a.Dead && b.Dead);
        var held = Assert.Single(r.Log.Lines, l => l.Contains("(m6-loot-walk-waits)", StringComparison.Ordinal));
        Assert.Contains("is put off: Tusker Slave 0x80001002 is in range of the mage with a clear line and the corpse is 10 m away, a walk longer than the aggro reach (6 m, --fight-loot-aggro-m): the fight has a target, so ACE's walk does not start", held, StringComparison.Ordinal);
        Assert.Contains("it stays queued (1 put off)", held, StringComparison.Ordinal);
        Assert.Equal(1, scenario.LootWalkWaits);

        for (var i = 0; i < 200 && (scenario.LootBusy || r.Opened.Count == 0); i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal([CorpseA], r.Opened); // retried once the fight had no target (the second corpse lies beyond the loot radius)
        Assert.Contains(r.Log.Lines, l => l.Contains("0x80001101 of 'Tusker Slave' put off ", StringComparison.Ordinal) && l.Contains("is retried: no target in reach and nothing in the way", StringComparison.Ordinal));
        Assert.Contains(" loot_left_aggro=1 ", scenario.LootFields(), StringComparison.Ordinal);
        Assert.Equal(0, scenario.LootWalkCuts);
    }

    [Fact]
    public void A_tusker_within_the_aggro_reach_of_the_mage_holds_even_a_short_walk_to_a_corpse_M6_14()
    {
        var r = new Rig(fight: LootRetryFight()) { Corpses = true, MoveToSeconds = 1 };
        var a = r.Add(SlaveA, TuskerSlave, Den, 24); // 4 m from her: the nearest, killed first; its corpse is a short walk
        var b = r.Add(SlaveB, TuskerSlave, Den, 15, hitsToKill: 6); // 5 m behind her and 9 m from the corpse: outside the corpse's aggro reach, inside hers
        var scenario = r.Scenario(new FakeWalk());

        for (var i = 0; i < 400 && !b.Dead; i++)
        {
            r.Frame(scenario);
            Assert.DoesNotContain(CorpseA, r.Opened);
        }

        Assert.True(a.Dead && b.Dead);
        var held = Assert.Single(r.Log.Lines, l => l.Contains("(m6-loot-walk-waits)", StringComparison.Ordinal));
        Assert.Contains("is put off: Tusker Slave 0x80001002 stands 5 m from the mage (aggro reach 6 m, --fight-loot-aggro-m) and the corpse is 4 m away: ACE's walk to it does not start with a hostile in reach", held, StringComparison.Ordinal);
        Assert.Equal(1, scenario.LootWalkWaits);

        for (var i = 0; i < 300 && (scenario.LootBusy || !scenario.LootFields().Contains("looted=2", StringComparison.Ordinal)); i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal([CorpseB, CorpseA], r.Opened);
        Assert.Equal(0, scenario.LootCorpsesLeft);
    }

    [Fact]
    public void A_corpse_within_reach_of_her_hand_is_opened_with_a_target_in_sight_as_before_M6_14()
    {
        // The rule is about ACE's walk: a corpse she can open from where she stands needs none.
        var r = new Rig(fight: LootRetryFight()) { Corpses = true };
        var a = r.Add(SlaveA, TuskerSlave, Den, 22); // 2 m from her: within reach
        var b = r.Add(SlaveB, TuskerSlave, Den, 45, hitsToKill: 100_000); // in sight for the whole test, 23 m from the corpse
        var scenario = r.Scenario(new FakeWalk());

        for (var i = 0; i < 200 && r.Opened.Count == 0; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(a.Dead);
        Assert.False(b.Dead);
        Assert.Equal([CorpseA], r.Opened);
        Assert.Equal(0, scenario.LootWalkWaits);
        Assert.Equal(0, LinesWith(r, "(m6-loot-walk-waits)"));
    }
}

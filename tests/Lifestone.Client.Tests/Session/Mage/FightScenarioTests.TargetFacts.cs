using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 16 E1b (docs/automation-engine.md stage E1, m6-plan.md D139; the owner, 2026-11-15: a creature's numbers are known data, never assumed): the fight's one read of a target's maximum health and Magic Defense
/// (<c>FightScenario.TargetStatsOf</c>) knew only its ground's target list; any other creature (an attacker that wandered in, the den's avoided kinds, an outdoor ground's creatures before their appraisal) read health 0 and
/// Magic Defense 0, and the emergency then took 400 health. Here: the read order (the ground's row, then the class's appraisal, then the bestiary for the health; the row, then the bestiary for the Magic Defense), the one line a
/// class, the loud stub for a class the bestiary does not hold, and a fight in which an attacker outside the list is finished by its real health. The numbers are the tracked tables' (the world database's creature table through
/// <see cref="Bestiary"/>). Nothing here needs the dats or a server.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint DrudgeLurker = 1608; // no row of the den's list: the bestiary's 117 health, Magic Defense 113 (the world database's creature table)
    private const string Lurker = "Drudge Lurker";
    private const uint ShrineStatue = 22641; // the Temple's avoided statue: its row gives max_health 370 and no magic_defence; the bestiary's Magic Defense is 76
    private const uint AurochCow = 181; // the belt names it among its creatures; a WeenieType.Cow, a row of the tables from M6 16 E1e on: health 80, Magic Defense 28 (fight-sim creature 181)
    private const uint CowGuid = 0x80003005;
    private const uint OddClass = 999_999_998; // no weenie of the world database: a creature the rig places with it stands for a kind the bestiary does not hold
    private const uint NoWeenie = 999_999_999;

    [Fact]
    public void E1b_the_fights_read_takes_the_grounds_row_first_and_the_bestiary_for_what_the_row_or_the_list_does_not_give()
    {
        var r = new Rig();
        var scenario = r.Scenario(new FakeWalk());
        var den = r.Fight.GroundProfile;
        var slave = den.Targets.Single(t => t.Wcid == TuskerSlave);
        Assert.Equal((400, 219), (slave.MaxHealth, slave.MagicDefence));
        var virindi = den.Targets.Single(t => t.Wcid == Virindi);
        Assert.Equal((0, 0), (virindi.MaxHealth, virindi.MagicDefence)); // an avoided kind: listed with neither number
        Assert.DoesNotContain(den.Targets, t => t.Wcid == DrudgeLurker);

        // the ground's row says both: the row, and the bestiary is not asked
        Assert.Equal((400, 219), scenario.TargetFacts(TuskerSlave));
        Assert.Equal(0, LinesWith(r, "target facts:"));

        // a row with neither number, and a class with no row: the bestiary's (before: health 0, taken as 400 by the emergency, and Magic Defense 0)
        Assert.Equal((125, 171), scenario.TargetFacts(Virindi));
        Assert.Equal((117, 113), scenario.TargetFacts(DrudgeLurker));
        Assert.Equal((125, 171), (Bestiary.Find(Virindi)!.Vitals.MaxHealth, Bestiary.Find(Virindi)!.Defence.MagicDefence));
        Assert.Equal((117, 113), (Bestiary.Find(DrudgeLurker)!.Vitals.MaxHealth, Bestiary.Find(DrudgeLurker)!.Defence.MagicDefence));

        // one line a class, however often it is read
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal((125, 171), scenario.TargetFacts(Virindi));
            Assert.Equal((117, 113), scenario.TargetFacts(DrudgeLurker));
        }

        Assert.Equal(2, LinesWith(r, "target facts:"));
        Assert.Equal(1, LinesWith(r, $"target facts: Virindi Servant (class 23): maximum health 125 (the world database's creature table), Magic Defense 171, from the bestiary; the ground {den.Key} lists it with no max_health and no magic_defence"));
        Assert.Equal(1, LinesWith(r, $"target facts: Drudge Lurker (class 1608): maximum health 117 (the world database's creature table), Magic Defense 113, from the bestiary; the ground {den.Key} does not list it"));
        Assert.Equal(0, LinesWith(r, "TODO(engine-bestiary-unknown-kind)"));

        // class 0 is no creature (the policy asks it of a ground with no target and no pick): no read and no line
        Assert.Equal((0, 0), scenario.TargetFacts(0));
        Assert.Equal(2, LinesWith(r, "target facts:"));
    }

    [Fact]
    public void E1b_a_row_that_gives_the_health_and_no_Magic_Defense_keeps_its_health_and_reads_the_defence_in_the_bestiary()
    {
        var temple = GroundCatalog.Get("tusker-temple-20-40");
        var statue = temple.Targets.Single(t => t.Wcid == ShrineStatue);
        Assert.Equal((370, 0), (statue.MaxHealth, statue.MagicDefence));
        var r = new Rig(fight: new FightOptions { Ground = temple.Key, GroundForTest = temple });
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal((370, 76), scenario.TargetFacts(ShrineStatue));
        Assert.Equal(76, Bestiary.Find(ShrineStatue)!.Defence.MagicDefence);
        Assert.Equal(1, LinesWith(r, $"target facts: Tusker Shrine Statue (class 22641): maximum health 370 (the world database's creature table), Magic Defense 76, from the bestiary; the ground {temple.Key} lists it with no magic_defence (its row's max_health 370 stands)"));

        var minion = temple.Targets.First(t => t.Role == GroundRole.Target);
        Assert.Equal((minion.MaxHealth, minion.MagicDefence), scenario.TargetFacts(minion.Wcid)); // a row with both, as before
        Assert.Equal(1, LinesWith(r, "target facts:"));
    }

    [Fact]
    public void E1b_the_bestiarys_health_stands_until_the_class_is_appraised_and_then_the_appraisal_does()
    {
        var r = BeltRig(level: 5);
        r.CreatureProfiles[ScoutGuid] = new AceCreatureProfile(60, 60); // the scripted server's word: not the tracked table's 43, so the order shows
        r.Add(ScoutGuid, BanderlingScout, BeltCell, 45, "Banderling Scout", hitsToKill: 3);
        var scenario = r.Scenario(new FakeWalk());
        Assert.Empty(BeltGround.Targets);

        Assert.Equal((43, 27), scenario.TargetFacts(BanderlingScout)); // nothing appraised yet: the bestiary's (before: 0 and 0, the picker unscored)
        Assert.Equal(1, LinesWith(r, $"target facts: Banderling Scout (class 6): maximum health 43 (the world database's creature table), Magic Defense 27, from the bestiary; the ground {Belt} does not list it"));

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Single(r.Identified, g => g == ScoutGuid); // the class is still appraised once
        Assert.Equal(1, LinesWith(r, "pick: Banderling Scout (class 6) has a maximum health of 60 by its appraisal"));
        Assert.Equal((60, 27), scenario.TargetFacts(BanderlingScout)); // the health the appraisal's, the Magic Defense the bestiary's still
        Assert.Equal(1, LinesWith(r, "TODO(engine-bestiary-health-differs): Banderling Scout (class 6): its appraisal gives a maximum health of 60, the bestiary 43; the appraisal stands for this fight"));
        Assert.Equal(0, LinesWith(r, "TODO(f10-picker-data)")); // scored from the first decision
        Assert.Equal(1, LinesWith(r, "target facts:"));
    }

    [Fact]
    public void E1b_a_class_the_bestiary_does_not_hold_keeps_the_old_answer_and_is_named_once_by_name()
    {
        // the belt with one of its creatures, the Auroch Cow, given a weenie class the bestiary does not hold (the cattle themselves are kinds of the bestiary from M6 16 E1e on)
        Assert.Null(Bestiary.Find(OddClass));
        var outdoor = BeltGround.Outdoor!;
        Assert.Contains(outdoor.Creatures, c => c.Wcid == AurochCow);
        var belt = BeltGround with { Outdoor = outdoor with { Creatures = [.. outdoor.Creatures.Select(c => c.Wcid == AurochCow ? c with { Wcid = OddClass, Name = "Odd Thing" } : c)] } };
        var r = new Rig(fight: new FightOptions { Ground = Belt, GroundForTest = belt, TargetMargin = RungMargin }, characterLevel: 5);
        PlayerTo(r, BeltCell);
        r.Add(CowGuid, OddClass, BeltCell, 45, "Odd Thing", hitsToKill: 3); // the rig answers its appraisal with no creature profile
        var scenario = r.Scenario(new FakeWalk());
        Assert.Equal((0, 0), scenario.TargetFacts(OddClass));
        Assert.Equal(0, LinesWith(r, "TODO(engine-bestiary-unknown-kind)")); // before the fight sees the world the class cannot be named: no line yet
        ToTheFight(r, scenario);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal((0, 0), scenario.TargetFacts(OddClass)); // the old answer: no health, Magic Defense 0
            Assert.Equal((0, 0), scenario.TargetFacts(NoWeenie));
        }

        Assert.Equal(2, LinesWith(r, "TODO(engine-bestiary-unknown-kind)"));
        Assert.Equal(1, LinesWith(r, $"TODO(engine-bestiary-unknown-kind): Odd Thing (class 999999998) is no kind of the bestiary (no creature weenie of the pinned world database's table) and the ground {Belt} does not list it"));
        Assert.Equal(1, LinesWith(r, "TODO(engine-bestiary-unknown-kind): a creature (class 999999999) is no kind of the bestiary"));
        Assert.Equal(0, LinesWith(r, "target facts:"));

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));
        Assert.Equal(1, LinesWith(r, "TODO(engine-bestiary-unknown-kind): Odd Thing (class 999999998)")); // the fight's own reads add no second line
        Assert.Equal(1, LinesWith(r, "with no maximum health; this class stays unscored (the bolt), named once")); // the appraisal's line as before: nothing else knows its health
    }

    /// <summary>
    /// M6 16 E1e: the belt's cattle were the one case of a creature a ground profile names and the bestiary did not hold (their weenie type is Cow, and the tables took Creature alone). They are rows now: the fight
    /// reads an Auroch Cow's health and Magic Defense from the bestiary, as it reads any creature outside its ground's list, and the loud stub is not written.
    /// </summary>
    [Fact]
    public void E1e_the_belts_cattle_read_their_facts_from_the_bestiary_as_any_creature_outside_the_list()
    {
        Assert.Contains(BeltGround.Outdoor!.Creatures, c => c.Wcid == AurochCow);
        Assert.DoesNotContain(BeltGround.Targets, t => t.Wcid == AurochCow);
        var r = BeltRig(level: 5);
        r.Add(CowGuid, AurochCow, BeltCell, 45, "Auroch Cow", hitsToKill: 3);
        var scenario = r.Scenario(new FakeWalk());
        ToTheFight(r, scenario);

        Assert.Equal((80, 28), scenario.TargetFacts(AurochCow));
        Assert.Equal((80, 28), scenario.TargetFacts(AurochCow));

        Assert.Equal(1, LinesWith(r, $"target facts: Auroch Cow (class 181): maximum health 80 (the world database's creature table), Magic Defense 28, from the bestiary; the ground {Belt} does not list it"));
        Assert.Equal(0, LinesWith(r, "TODO(engine-bestiary-unknown-kind)"));
    }

    [Fact]
    public void E1b_the_read_allocates_nothing_after_a_classs_first()
    {
        var r = new Rig();
        var scenario = r.Scenario(new FakeWalk());
        AllocationAssert.Nothing(() =>
        {
            _ = scenario.TargetFacts(TuskerSlave);
            _ = scenario.TargetFacts(Virindi);
            _ = scenario.TargetFacts(DrudgeLurker);
            _ = scenario.TargetFacts(NoWeenie);
            _ = scenario.TargetFacts(0);
        });
    }

    [Fact]
    public void E1b_every_creature_a_ground_profile_names_outside_its_list_has_its_facts_in_the_bestiary_the_belts_cattle_too()
    {
        // The proof by numbers: every approach hostile and every outdoor creature of the 19 profiles that its ground's target list does not give both numbers for, and every listed row that lacks one. Each reads a maximum health
        // above 0 and a Magic Defense above 0 from the bestiary. Until M6 16 E1e three did not: the belt's cattle, whose weenie type (Cow) the tables did not take; they are rows now, so none is left.
        var unknown = new SortedSet<uint>();
        var read = new HashSet<(string Ground, uint Wcid)>();
        foreach (var g in GroundCatalog.All)
        {
            var listed = g.Targets.Where(t => t.MaxHealth > 0 && t.MagicDefence > 0).Select(t => t.Wcid).ToHashSet();
            var named = g.Targets.Select(t => t.Wcid).Concat((g.Approach?.Hostiles ?? []).Select(h => h.Wcid)).Concat((g.Outdoor?.Creatures ?? []).Select(c => c.Wcid)).Where(w => !listed.Contains(w)).Distinct();
            foreach (var wcid in named)
            {
                if (Bestiary.Find(wcid) is not { } kind)
                {
                    unknown.Add(wcid);
                    continue;
                }

                read.Add((g.Key, wcid));
                Assert.True(kind.Vitals.MaxHealth > 0, $"{g.Key}: {kind.Identity.Name} ({wcid}) has no maximum health in the bestiary");
                Assert.True(kind.Defence.MagicDefence > 0, $"{g.Key}: {kind.Identity.Name} ({wcid}) has no Magic Defense in the bestiary");
            }
        }

        Assert.Empty(unknown); // Auroch Bull 20, Auroch Cow 181 and Auroch Yearling 182 were the three, before the tables took WeenieType.Cow
        Assert.True(read.Count >= 100, $"{read.Count} creatures of the grounds read their facts from the bestiary (the report's table)");
    }

    [Fact]
    public void E1b_an_attacker_outside_the_grounds_list_is_finished_by_its_real_health_where_400_was_assumed()
    {
        // A Drudge Lurker in the den (no row of its list) attacks her; the server says it is at 90 %: 105 of its 117 (the bestiary's), where the emergency took 360 of an assumed 400 and the finishing bolt's own read had no
        // maximum health at all. One bolt of the rig's (expected 170) finishes 105: the bolt goes, not the drain or the heal.
        var (first, r, scenario) = UnderTheLineAgainst(DrudgeLurker, Lurker, 0.90f);

        Assert.True(FightManaTests.Rig.IsBolt(first), $"the bolt, not spell {first}");
        Assert.Equal(1, scenario.EmergencyFinishes);
        Assert.Equal(0, scenario.EmergencyDrains);
        Assert.Contains(r.Log.Lines, l => l.Contains("emergency: finishing bolt: ", StringComparison.Ordinal) && l.Contains($"on {Lurker} ", StringComparison.Ordinal) && l.Contains(": 105 left, expected 170 ", StringComparison.Ordinal) && l.Contains("the bolt kills the only attacker and the mage lives through the cast", StringComparison.Ordinal));
        Assert.Single(r.Log.Lines, l => l.Contains($"target facts: {Lurker} (class 1608): maximum health 117 (the world database's creature table), Magic Defense 113, from the bestiary", StringComparison.Ordinal));
        Assert.Equal((117, 113), scenario.TargetFacts(DrudgeLurker));

        // the control: the same blow, the same 90 %, on a creature that does hold 400 (the den's Tusker Slave, by its row): 360 left, one bolt does not finish it and the heal stands, which is what an assumed 400 chooses
        var (other, slaves, control) = UnderTheLineAgainst(TuskerSlave, Slave, 0.90f);
        Assert.False(FightManaTests.Rig.IsBolt(other));
        Assert.Equal(0, control.EmergencyFinishes);
        Assert.Contains(slaves.Log.Lines, l => l.Contains("emergency: finishing bolt: ", StringComparison.Ordinal) && l.Contains(": 360 left, ", StringComparison.Ordinal) && l.Contains("one bolt is not expected to finish it; the heal stands", StringComparison.Ordinal));
    }

    /// <summary>One creature of <paramref name="wcid"/> attacks her in the den and is fought back; the server says its health is <paramref name="share"/>; her health falls under the emergency line and it hits again. The first cast after that.</summary>
    private static (uint First, FightManaTests.Rig Rig, FightScenario Scenario) UnderTheLineAgainst(uint wcid, string name, float share)
    {
        var r = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true) { HitsToKill = 400, TuskerWcid = wcid, TuskerName = name };
        r.Caster.HealthPerCast = 18;
        r.Caster.ManaRegenPerSecond = 20;
        var guid = r.AddTusker(40);
        var spells = new List<uint>();
        r.Caster.TargetedCastHook = (spell, _) =>
        {
            spells.Add(spell);
            return false;
        };
        var scenario = r.Scenario();
        r.Run(scenario, 20, 0.1, () => scenario.Stage == "fight");
        for (var i = 0; i < 40 && !r.BoltAnswers.Any(); i++)
        {
            HitBy(r, scenario, name, 3, 0.5); // it attacks: self-defence fights it back, listed or not
        }

        Assert.True(r.BoltAnswers.Any(), $"the fight bolts the {name} that attacks her");
        var max = MaxHealthOf(r);
        TuskerHealth(r, guid, share); // the push
        r.Caster.SetHealth(max * 40 / 100); // under the line, a long way from the next hit
        var from = spells.Count;
        HitBy(r, scenario, name, 3, 0);
        return (FirstAfter(r, scenario, spells, from), r, scenario);
    }

    private static void HitBy(FightManaTests.Rig r, FightScenario scenario, string name, uint damage, double thenSeconds)
    {
        r.R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, name, 4, 0.1, damage, 4, false, 0));
        r.Run(scenario, thenSeconds, 0.1);
    }
}

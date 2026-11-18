using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Vitals;
using Xunit;

namespace Lifestone.Client.Tests.Session.Creatures;

/// <summary>
/// M6 16 E1a, the bestiary's facade (docs/automation-engine.md section 2.1 and stage E1; m6-plan.md D139): a creature kind's facts were read through five doors (<see cref="CreatureTable"/>, <see cref="CreatureResists"/>,
/// <see cref="CreatureSkills"/>, <see cref="CreatureFacts"/> and <see cref="CreatureAwareness"/>). <see cref="Bestiary"/> is the one door over them, with no change of behaviour or data: these tests compare the old path with the
/// new one while both exist, for every weenie the ground profiles name and for every row of the world database's table, and pin why each reader moved onto the facade reads the same value as before. The dats are not needed.
/// M6 16 E1d: the record's fields the behaviour table fills are <see cref="CreatureBehaviourTests"/>'; here they are beside the old values, which stay as they were.
/// </summary>
public sealed class BestiaryTests
{
    private const uint Golem = 23082; // Nubilous Golem: War Magic 50, specialized (DerivedIncomingTests)
    private const uint NoWeenie = 999_999_999;

    /// <summary>Every weenie class the ground profiles name as a target or as an approach hostile.</summary>
    private static List<uint> ProfileWcids() =>
        [.. GroundCatalog.All.SelectMany(g => g.Targets.Select(t => t.Wcid).Concat((g.Approach?.Hostiles ?? []).Select(h => h.Wcid))).Distinct().Order()];

    [Fact]
    public void Every_weenie_the_19_ground_profiles_name_reads_through_the_facade_as_the_creature_facts_gave_it()
    {
        Assert.True(GroundCatalog.All.Count >= 19, $"{GroundCatalog.All.Count} ground profiles");
        var wcids = ProfileWcids();
        Assert.True(wcids.Count >= 40, $"the profiles name {wcids.Count} target and approach weenies (47 on 2026-11-17)");

        var profileFirst = 0;
        foreach (var wcid in wcids)
        {
            var fact = CreatureFacts.All.Find(wcid); // the old path: a profile's facts first, then the table's
            var kind = Bestiary.Find(wcid);
            Assert.NotNull(fact); // none is outside the world database
            Assert.NotNull(kind);
            AssertSameFacts(fact, kind);

            // the order: where a profile records the kind's attack, the facade's facts are the profile's, never the table's
            if (CreatureFacts.All.Recorded(wcid) is { } recorded)
            {
                profileFirst++;
                Assert.Equal(recorded.Where, kind.Identity.Where);
                Assert.True(recorded.Where.EndsWith(":targets", StringComparison.Ordinal) || recorded.Where.EndsWith(":approach", StringComparison.Ordinal), recorded.Where);
                Assert.Same(recorded.Hitter, kind.Offence.Hitter);
            }
            else
            {
                Assert.StartsWith("the world database's creature table", kind.Identity.Where, StringComparison.Ordinal);
            }
        }

        Assert.Equal(CreatureFacts.All.Count, profileFirst); // every kind the profiles record facts for was compared
    }

    [Fact]
    public void Every_row_of_the_world_database_s_table_reads_through_the_facade_as_before_and_the_facade_holds_no_other_kind()
    {
        // the two key sets are one: every weenie a profile records facts for is a row of the table, so Find is null exactly where CreatureTable.Find was (the quest catalog's carrier check, the attack times' table check)
        Assert.All(CreatureFacts.All.Wcids, wcid => Assert.NotNull(CreatureTable.Find(wcid)));
        Assert.Equal(CreatureTable.Count, Bestiary.Count);
        Assert.Null(Bestiary.Find(NoWeenie));
        Assert.Null(CreatureFacts.All.Find(NoWeenie));

        foreach (var row in CreatureTable.All)
        {
            var kind = Bestiary.Find(row.Wcid);
            Assert.NotNull(kind);
            Assert.Equal(row.Wcid, kind.Wcid);
            AssertSameFacts(CreatureFacts.All.Find(row.Wcid)!, kind);

            // the readers moved off CreatureTable.Find read the same values: the name (the want-loot carrier, the quest carrier, the attack times), the maximum health (the travel leg's life strike), the Magic Defense (the vitals
            // guard, the fire dance, the life strike) and the timing facts (the attack times); a profile's name and max_health, read first, are the table's own for every kind
            Assert.Equal(row.Name, kind.Identity.Name);
            Assert.Equal(row.Level, kind.Identity.Level);
            Assert.Equal(row.MaxHealth, kind.Vitals.MaxHealth);
            Assert.Equal(row.MagicDefence, kind.Defence.MagicDefence);
            Assert.Same(row.Timing, kind.Timing);
            Assert.Same(row.Attack, kind.Offence.TableAttack);
            Assert.Same(row.Timing!.Spells, kind.Offence.Spells);
        }
    }

    [Fact]
    public void The_resistances_the_skills_the_drain_and_the_awareness_read_through_the_facade_as_their_sources_gave_them()
    {
        foreach (var kind in Bestiary.All)
        {
            var wcid = kind.Wcid;
            Assert.Equal(CreatureResists.Records(wcid), kind.Defence.Resists is not null);
            for (var i = 0; i < CreatureResists.Elements.Count; i++)
            {
                var element = CreatureResists.Elements[i];
                Assert.Equal(CreatureResists.Of(wcid, element), Bestiary.ResistOf(wcid, element));
                Assert.Equal(CreatureResists.Of(wcid, element), Math.Max(0.0, kind.Defence.Resists?[i] ?? 1.0)); // the record holds the stated value; the read gives ACE's floor at 0 (one weenie states -0.2: M6 16 E1e)
            }

            Assert.Equal(DrainResist.RecordedOf(wcid), kind.Defence.HealthDrainResist);
            Assert.Equal(CreatureAwareness.MetresOf(wcid), Bestiary.AwarenessMetres(wcid));
            Assert.Equal((CreatureAwareness.MetresOf(wcid), CreatureAwareness.IsPinned(wcid)), (kind.Awareness.AwarenessMetres, kind.Awareness.Pinned));
            foreach (var skill in kind.Offence.Skills)
            {
                Assert.True(CreatureSkills.TryRow(wcid, skill.Skill, out var init, out var ranks, out var sac));
                Assert.Equal((skill.Init, skill.Ranks, skill.Sac), (init, ranks, sac));
            }
        }

        // a weenie outside the record takes ACE's defaults, as before
        Assert.Equal(1.0, Bestiary.ResistOf(NoWeenie, "fire"));
        Assert.Equal(CreatureResists.Text(NoWeenie), Bestiary.ResistsText(NoWeenie));
        Assert.Equal(CreatureResists.Text(Golem), Bestiary.ResistsText(Golem));
        Assert.Equal(CreatureAwareness.DefaultMetres, Bestiary.AwarenessMetres(NoWeenie));
        Assert.Throws<ArgumentException>(() => Bestiary.ResistOf(Golem, "nether"));

        // the golem's skills (DerivedIncomingTests pins the rows): War Magic 50, specialized; no Unarmed Combat row, so ACE's Light Weapons; a skill's value is a join with the dats' SkillTable and null without it
        var golem = Bestiary.Find(Golem)!;
        Assert.Contains(new CreatureSkillRow(CreatureSkills.WarMagic, 50, 0, 3), golem.Offence.Skills);
        Assert.Equal(CreatureSkills.AttackSkillId(Golem, 0, ranged: false), Bestiary.AttackSkillId(Golem, 0, ranged: false));
        Assert.Equal(CreatureSkills.LightWeapons, Bestiary.AttackSkillId(Golem, 0, ranged: false));
        Assert.Null(Bestiary.SkillOf(Golem, CreatureSkills.WarMagic, golem.Timing!.Attributes, null));
    }

    [Fact]
    public void The_weenies_of_the_resists_the_skills_and_the_awareness_are_rows_of_the_table_or_named_with_why()
    {
        Assert.DoesNotContain(CreatureResists.Wcids, w => CreatureTable.Find(w) is null);
        Assert.DoesNotContain(CreatureSkills.Wcids, w => CreatureTable.Find(w) is null);

        // The awareness pins are the hunt grounds' creatures (the Low B belt's and the low north forest's tables). Three of the belt's were no row of creatures.json while its rows were weenie.type 10 alone: the belt's
        // cattle, which the world database types WeenieType.Cow (weenie.type 15) and ACE builds as a creature. M6 16 E1e took that type into the tables (TODO(engine-bestiary-cattle) closed): every pin is a row now, the
        // facade holds the cattle's record, and their pin equals the table's own range.
        var cattle = new Dictionary<uint, string> { [20] = "Auroch Bull", [181] = "Auroch Cow", [182] = "Auroch Yearling" };
        Assert.DoesNotContain(CreatureAwareness.Wcids, w => CreatureTable.Find(w) is null);
        Assert.All(cattle, c =>
        {
            var kind = Bestiary.Find(c.Key);
            Assert.NotNull(kind);
            Assert.Equal(c.Value, kind.Identity.Name);
            Assert.True(CreatureAwareness.IsPinned(c.Key));
            Assert.Equal(22f, Bestiary.AwarenessMetres(c.Key));
            Assert.Equal(22.0, kind.Awareness.TableVisualMetres);
            Assert.Equal(22f, Bestiary.OwnAwarenessMetres(c.Key));
        });

        // the belt's profile names them among its outdoor creatures, which carry no attack facts: their facts are the table's now, where there were none
        var belt = GroundCatalog.All.Single(g => g.Key == "low-b-belt");
        Assert.All(cattle, c => Assert.Contains(belt.Outdoor!.Creatures, o => o.Wcid == c.Key && o.Name == c.Value));
        Assert.All(cattle.Keys, wcid => Assert.StartsWith("the world database's creature table", CreatureFacts.All.Find(wcid)!.Where, StringComparison.Ordinal));
    }

    /// <summary>
    /// M6 16 E1e: the cattle as the server builds them (<c>tools/fight-sim.ps1 creature 20</c>, <c>181</c>, <c>182</c>, <c>14</c> and <c>34611</c>, 2026-11-17, once the tool's lookup took WeenieType.Cow): the record's
    /// health, stamina, mana, Run, Magic Defense, ranges, tolerance and experience are ACE's own objects'.
    /// </summary>
    [Theory]
    [InlineData(20u, "Auroch Bull", 85, 330, 30, 60, 28, 22.0, 64, 420L)]
    [InlineData(181u, "Auroch Cow", 80, 320, 30, 55, 28, 22.0, 64, 445L)]
    [InlineData(182u, "Auroch Yearling", 55, 290, 20, 45, 16, 22.0, 64, 108L)]
    [InlineData(14u, "Cow", 20, 30, 20, 30, 29, 50.0, 64, 1000L)]
    [InlineData(34611u, "Very Mad Cow", 10500, 11000, 11000, 1040, 586, 25.0, 0, 17_500_000L)]
    public void The_cattle_are_kinds_of_the_bestiary_with_the_server_s_numbers(uint wcid, string name, int health, int stamina, int mana, int run, int magicDefence, double sees, int tolerance, long xp)
    {
        var kind = Bestiary.Find(wcid);
        Assert.NotNull(kind);
        Assert.Equal(name, kind.Identity.Name);
        Assert.Equal((health, stamina, mana), (kind.Vitals.MaxHealth, kind.Vitals.MaxStamina, kind.Vitals.MaxMana));
        Assert.Equal("the world database's creature table", kind.Vitals.MaxHealthSource);
        Assert.Equal(run, kind.Movement.TableRunSkill);
        Assert.Equal(magicDefence, kind.Defence.MagicDefence);
        Assert.Equal((sees, sees), (kind.Awareness.TableVisualMetres, kind.Awareness.TableAuralMetres));
        Assert.Equal(tolerance, kind.Awareness.Tolerance);
        Assert.Equal(xp, kind.Reward.Xp);
        Assert.True(KindNumbers.Of(wcid).Complete);
    }

    [Fact]
    public void The_tables_hold_the_17_cattle_beside_the_7831_creatures_and_no_vendor_pet_or_game_piece()
    {
        Assert.Equal(7848, CreatureTable.Count); // 7,831 of weenie.type 10 and 17 of weenie.type 15 (bestiary --check, 2026-11-17)
        Assert.Equal(7848, CreatureBehaviour.Count);
        Assert.Equal(7848, Bestiary.Count);
        uint[] cattle = [14, 20, 181, 182, 618, 1605, 1606, 1607, 5755, 8128, 11981, 12185, 23623, 29359, 29504, 34611, 72867];
        Assert.All(cattle, wcid => Assert.NotNull(Bestiary.Find(wcid)));
        Assert.All(cattle, wcid => Assert.NotNull(CreatureResists.RowOf(wcid))); // each states a resistance
        Assert.All(cattle, wcid => Assert.NotEmpty(CreatureSkills.RowsOf(wcid)));

        // one of them states a resistance under 0: the Master of the Herd's ResistFire is -0.2. ACE floors the modifier at 0 where it deals damage (SpellProjectile.cs:486, DamageEvent.cs:341): the simulator's flame
        // bolts take 0 from it (fight-sim matchup 11981). The row keeps the stated value; the read every decision asks gives the floor.
        Assert.Equal(-0.2, CreatureResists.RowOf(11981)![3]);
        Assert.Equal(-0.2, Bestiary.Find(11981)!.Defence.Resists![3]);
        Assert.Equal(0.0, Bestiary.ResistOf(11981, "fire"));
        Assert.Equal(0.8, Bestiary.ResistOf(11981, "cold"));
        Assert.Contains("fire 0 ", Bestiary.ResistsText(11981), StringComparison.Ordinal);
        Assert.DoesNotContain(CreatureResists.Wcids.Where(w => w != 11981), w => CreatureResists.RowOf(w)!.Any(v => v < 0));

        // left out, each a weenie type ACE builds as a Creature subclass (tools/fight-sim/BestiaryRows.cs, AceIds.LeftOut): the lowest weenie class of the Vendors, the GamePieces, the Pets and the CombatPets (the bestiary command's scope line, 2026-11-17)
        Assert.All(new uint[] { 400, 14342, 33918, 48879 }, wcid => Assert.Null(Bestiary.Find(wcid)));
    }

    [Fact]
    public void The_record_groups_a_kind_s_facts_as_the_design_names_them()
    {
        var hitter = new LadderHitter("Test Tusker", 7, 60, 0.5, 200);
        var ranged = new GroundRanged(40, 0.4, 0x2, 180, 30);
        var fact = new CreatureFact(7, "Test Tusker", 55, 400, hitter, 0x4, 3, "test-ground:approach") { RunSkill = 150, MotionTable = 0x09000001, Scale = 1.2, Ranged = ranged };
        var spell = new CreatureSpell(81, "Flame Bolt IV", 2.1, 0x10, 30, 20, 1, 0, 0, -1, -1);
        var timing = new CreatureTimingFacts(0x09000001, 0x30000001, 1.5, null, 120, false, 0, 0, 0, 0, 0, 0, [spell]) { Attributes = new CreatureAttributes(200, 150, 120, 110, 90, 80) };
        var attack = new CreatureAttack("its hardest body part", new LadderHitter("Test Tusker", 7, 45, 0.5, 200), 0x4);
        var row = new CreatureRow(7, "Test Tusker", 55, 400, 0.5, 0x4, 133) { Attack = attack, Timing = timing };
        double[] resists = [1, 1, 1, 0.5, 1.5, 1, 1];
        CreatureSkillRow[] skills = [new(CreatureSkills.WarMagic, 50, 0, 3)];

        var kind = Bestiary.Of(fact, 400, "test-ground:approach max_health", row, resists, skills, 0.5, 25f, awarenessPinned: true);

        Assert.Equal(7u, kind.Wcid);
        Assert.Equal(new CreatureIdentity(7, "Test Tusker", 55, "test-ground:approach"), kind.Identity);
        Assert.Equal(new CreatureVitals(400, "test-ground:approach max_health"), kind.Vitals);
        Assert.Equal(new CreatureDefence(133, resists, 0.5), kind.Defence);
        Assert.Equal(new CreatureOffence(hitter, 0x4, 3, ranged, attack, timing.Spells, skills), kind.Offence); // the profile's hitter, with the table's own attack beside it
        Assert.Same(timing, kind.Timing);
        Assert.Equal(new CreatureMovement(150, 0x09000001, 1.2), kind.Movement);
        Assert.Equal(new CreatureSenses(25f, true), kind.Awareness);
        Assert.Equal(new CreatureReward(null), kind.Reward);
        Assert.Equal(default, kind.Body);

        // M6 16 E1d: with the behaviour table's row the fields no other source holds are filled, beside the values above, which stay: the profile's pace, the pinned awareness
        var behaviour = new CreatureBehaviourRow(7)
        {
            CreatureType = 8, Setup = 0x020010A4, Scale = 1.5, VisualAwarenessMetres = 30, Tolerance = 64, TargetingTactic = 9, FriendType = 8, StaminaStart = 900, ManaStart = 10, HealthRate = 10, StaminaRate = 4,
            ManaRate = 2, UsesMana = false, ResistStaminaDrain = 0.75, ResistManaDrain = 0.5, RunStart = 70, RunSac = 3, Xp = 125_000,
        };
        var filled = Bestiary.Of(fact, 400, "test-ground:approach max_health", row, resists, skills, 0.5, 25f, awarenessPinned: true, behaviour);

        Assert.Equal(kind.Identity, filled.Identity);
        Assert.Equal(kind.Offence, filled.Offence);
        Assert.Equal(new CreatureVitals(400, "test-ground:approach max_health") { MaxStamina = 900 + 150, MaxMana = 10 + 80, HealthRate = 10, StaminaRate = 4, ManaRate = 2, UsesMana = false, HeartbeatSeconds = 5, HealthPerSecond = 2, StaminaPerSecond = 0.8, ManaPerSecond = 0.4 }, filled.Vitals); // M6 16 E1e: the rates a second, over ACE's 5 s heartbeat
        Assert.Equal(new CreatureDefence(133, resists, 0.5) { StaminaDrainResist = 0.75, ManaDrainResist = 0.5 }, filled.Defence);
        Assert.Equal(new CreatureMovement(150, 0x09000001, 1.2) { TableRunSkill = 70 + 120 }, filled.Movement); // the profile's Run 150 and scale 1.2 stay; the table's Run 190 is beside them
        Assert.Equal(new CreatureSenses(25f, true) { TableVisualMetres = 30, TableAuralMetres = 30, Tolerance = 64, TargetingTactic = 9, FriendType = 8 }, filled.Awareness); // the pinned 25 m stays; the table's 30 m is beside it
        Assert.Equal(new CreatureReward(125_000), filled.Reward);
        Assert.Equal(new CreatureBody(8, 0x020010A4, 1.5), filled.Body);
    }

    [Fact]
    public void A_field_no_source_holds_is_absent_never_a_guess()
    {
        // a kind the table does not hold: nothing of the table's is made up
        var fact = new CreatureFact(8, "Profile Only", 0, 0, new LadderHitter("Profile Only", 8, 10, 0.5, 60), 0, 1, "test-ground:targets");
        var kind = Bestiary.Of(fact, null, "TODO(m6-creature-health): weenie 8", null, null, [], 1.0, CreatureAwareness.DefaultMetres, awarenessPinned: false);

        Assert.Null(kind.Vitals.MaxHealth);
        Assert.Null(kind.Defence.MagicDefence);
        Assert.Null(kind.Defence.Resists);
        Assert.Null(kind.Offence.TableAttack);
        Assert.Null(kind.Offence.Ranged);
        Assert.Empty(kind.Offence.Spells);
        Assert.Empty(kind.Offence.Skills);
        Assert.Null(kind.Timing);
        Assert.Equal(default, kind.Movement); // no pace, and no Run of the table's: TODO(engine-bestiary-run) is the pace reader's move
        Assert.False(kind.Awareness.Pinned); // ACE's default, named: TODO(engine-bestiary-awareness) is the awareness reader's move

        // M6 16 E1d: the fields the behaviour table fills are null for a kind that table does not hold, never ACE's default passed off as the kind's own
        Assert.Null(kind.Vitals.MaxStamina);
        Assert.Null(kind.Vitals.MaxMana);
        Assert.Null(kind.Vitals.HealthRate);
        Assert.Null(kind.Vitals.StaminaRate);
        Assert.Null(kind.Vitals.ManaRate);
        Assert.Null(kind.Vitals.HeartbeatSeconds);
        Assert.Null(kind.Vitals.HealthPerSecond);
        Assert.Null(kind.Vitals.UsesMana);
        Assert.Null(kind.Defence.StaminaDrainResist);
        Assert.Null(kind.Defence.ManaDrainResist);
        Assert.Equal(new CreatureSenses(CreatureAwareness.DefaultMetres, false), kind.Awareness); // no range of the table's, no tolerance, no friend or foe
        Assert.Null(kind.Reward.Xp);
        Assert.Equal(default, kind.Body);

        // a behaviour row without the creature table's row: what the row states alone is there; the stamina, the mana and the Run skill need the table's attributes and stay null
        var half = Bestiary.Of(fact, null, "TODO(m6-creature-health): weenie 8", null, null, [], 1.0, CreatureAwareness.DefaultMetres, awarenessPinned: false, new CreatureBehaviourRow(8) { Xp = 30 });
        Assert.Equal(30, half.Reward.Xp);
        Assert.Equal(18.0, half.Awareness.TableVisualMetres);
        Assert.Null(half.Vitals.MaxStamina);
        Assert.Null(half.Vitals.MaxMana);
        Assert.Null(half.Movement.TableRunSkill);
    }

    [Fact]
    public void A_lookup_allocates_nothing()
    {
        Assert.NotNull(Bestiary.Find(Golem)); // the tables are loaded once, here
        AllocationAssert.Nothing(static () =>
        {
            _ = Bestiary.Find(Golem)!.Defence.MagicDefence;
            _ = Bestiary.Find(NoWeenie);
            _ = Bestiary.AwarenessMetres(Golem);
            _ = Bestiary.ResistOf(Golem, "fire");
        });
    }

    /// <summary>The facade's record carries exactly what the creature facts gave: the fact's fields, and the maximum health in the facts' own order with its source.</summary>
    private static void AssertSameFacts(CreatureFact fact, CreatureCapabilities kind)
    {
        Assert.Equal(new CreatureIdentity(fact.Wcid, fact.Name, fact.Level, fact.Where), kind.Identity);
        Assert.Same(fact.Hitter, kind.Offence.Hitter);
        Assert.Equal(fact.DamageTypes, kind.Offence.DamageTypes);
        Assert.Equal(fact.Pack, kind.Offence.Pack);
        Assert.Same(fact.Ranged, kind.Offence.Ranged);
        Assert.Equal(new CreatureMovement(fact.RunSkill, fact.MotionTable, fact.Scale), kind.Movement with { TableRunSkill = null }); // the profile's pace, as before; the table's Run is beside it

        var maxHealth = CreatureFacts.All.MaxHealthOf(fact.Wcid, out var source);
        Assert.Equal(maxHealth, kind.Vitals.MaxHealth);
        Assert.Equal(source, kind.Vitals.MaxHealthSource);
        if (fact.MaxHealth > 0)
        {
            Assert.Equal(fact.MaxHealth, kind.Vitals.MaxHealth); // a recorded max_health is the one read
        }
    }
}

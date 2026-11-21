using System.Globalization;
using System.Text.RegularExpressions;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Repository;
using Lifestone.Data.Tests;
using Lifestone.FightSim;
using Xunit;
using Xunit.Abstractions;
using Bestiary = Lifestone.Client.Session.Creatures.Bestiary;

namespace Lifestone.Client.Tests.Session.Creatures;

/// <summary>
/// M6 16 E1d, the bestiary's fourth table (docs/automation-engine.md section 2.1 and stage E1; docs/fight-sim.md, "The bestiary"): <c>creature-behaviour.json</c> holds, for every creature weenie of the world database,
/// the behaviour facts the first three tables do not: its senses and temper, its stamina and mana and their rates, its Run skill, its experience and its body. <see cref="CreatureBehaviour"/> reads it and the facade fills
/// its record from it (<see cref="Bestiary"/>). These tests prove the reader against the server's own objects (<c>tools\fight-sim.ps1 creature</c>, ACE's code), prove that no reader of the automation changed, and
/// hold the comparison the next slices need: the table's values against the pinned awareness ranges and against what the ground profiles state.
/// </summary>
public sealed class CreatureBehaviourTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    private static TableFile BehaviourFile => BestiaryTables.Files.Single(f => f.FileName == "creature-behaviour.json");

    /// <summary>Every weenie class the ground profiles name as a target or as an approach hostile (the E1a tests' own list).</summary>
    private static List<uint> ProfileWcids() =>
        [.. GroundCatalog.All.SelectMany(g => g.Targets.Select(t => t.Wcid).Concat((g.Approach?.Hostiles ?? []).Select(h => h.Wcid))).Distinct().Order()];

    [Fact]
    public void The_behaviour_table_holds_one_row_for_every_creature_weenie_of_the_core_table_and_no_other()
    {
        Assert.Equal(CreatureTable.Count, CreatureBehaviour.Count);
        Assert.True(CreatureBehaviour.Count >= 7848, $"{CreatureBehaviour.Count} rows (7,848 on 2026-11-17 with the cattle, bestiary --check)");
        Assert.All(CreatureTable.All, row => Assert.NotNull(CreatureBehaviour.Find(row.Wcid)));
        Assert.Null(CreatureBehaviour.Find(999_999_999));

        // the generator's column list and the reader's are one list
        Assert.Equal(BestiaryTables.Behaviour.Select(c => c.Name), CreatureBehaviour.Columns);
    }

    [Fact]
    public void A_creature_the_world_states_nothing_of_reads_as_ACE_gives_it_from_the_generator_s_own_row()
    {
        // The generator's row of a creature with no property at all (every column's default, each ACE's own: tools/fight-sim/BestiaryTables.cs cites the file and line), written by the generator's writer and read by the
        // client's reader: the two ends agree on what "absent" is, with no number typed twice.
        var world = new WorldFacts();
        world.WeenieTypes[8] = AceIds.Creature;
        var text = BestiaryWriter.Write(BehaviourFile, world, BehaviourFile.StatedSources!);
        Assert.EndsWith("\"behaviour\":[\n[8,-1,0,1,18,-1,0,0,-1,-1,0,0,0,0,0,0,0,5,1,1,1,0,0,1,0]\n]}\n", text, StringComparison.Ordinal);

        var row = Assert.Single(CreatureBehaviour.Parse(text)).Value;

        Assert.Equal(new CreatureBehaviourRow(8), row); // the record's own defaults are ACE's
        Assert.Null(row.CreatureType); // ACE holds none (WorldObject_Properties.cs:2231)
        Assert.Equal(0u, row.Setup);
        Assert.Equal(1.0, row.Scale); // ObjScale ?? 1.0f (Monster_Navigation.cs:334)
        Assert.Equal(18.0, row.VisualAwarenessMetres); // VisualAwarenessRange_Default (Monster_Awareness.cs:377)
        Assert.Equal(CreatureAwareness.DefaultMetres, row.VisualAwarenessMetres); // the same default the pinned ranges fall back to
        Assert.Null(row.StatedAuralAwarenessMetres);
        Assert.Equal(18.0, row.AuralAwarenessMetres); // it hears as far as it sees (Monster_Awareness.cs:421)
        Assert.Equal((0, 0), (row.Tolerance, row.TargetingTactic));
        Assert.Null(row.FriendType);
        Assert.Null(row.FoeType);
        Assert.Equal((0.0, 0.0, 0.0), (row.HealthRate, row.StaminaRate, row.ManaRate)); // no regeneration (CreatureVital.cs:38-44)
        Assert.Equal(5.0, row.HeartbeatSeconds); // HeartbeatInterval 5 where the weenie states none (WorldObject_Tick.cs:47-48)
        Assert.True(row.UsesMana); // AiUsesMana ?? true (Monster_Magic.cs:23)
        Assert.Equal((1.0, 1.0), (row.ResistStaminaDrain, row.ResistManaDrain));
        Assert.Equal((0, 0, 1), (row.RunStart, row.RunRanks, row.RunSac)); // an untrained, empty Run (Creature_Skills.cs:27-35)
        Assert.Equal(0L, row.Xp); // XpOverride ?? 0 (Creature_Death.cs:193)
    }

    [Fact]
    public void A_row_reads_each_column_at_its_place()
    {
        const string Columns = "\"columns\":[\"wcid\",\"creature_type\",\"setup\",\"scale\",\"visual_awareness\",\"aural_awareness\",\"tolerance\",\"targeting_tactic\",\"friend_type\",\"foe_type\",\"stamina_start\",\"stamina_ranks\","
            + "\"mana_start\",\"mana_ranks\",\"health_rate\",\"stamina_rate\",\"mana_rate\",\"heartbeat_interval\",\"uses_mana\",\"resist_stamina_drain\",\"resist_mana_drain\",\"run_start\",\"run_ranks\",\"run_sac\",\"xp\"]";
        static string Doc(string columns, params string[] rows) => "{\"sources\":{}," + columns + ",\"behaviour\":[" + string.Join(",", rows) + "]}";

        var row = Assert.Single(CreatureBehaviour.Parse(Doc(Columns, "[7,3,33556445,0.95,10,16,64,5,12,20,20,2,30,4,0.067,5,-1.5,2.5,0,0.75,0.5,40,6,3,1000]"))).Value;

        Assert.Equal(
            new CreatureBehaviourRow(7)
            {
                CreatureType = 3, Setup = 33556445, Scale = 0.95, VisualAwarenessMetres = 10, StatedAuralAwarenessMetres = 16, Tolerance = 64, TargetingTactic = 5, FriendType = 12, FoeType = 20,
                StaminaStart = 20, StaminaRanks = 2, ManaStart = 30, ManaRanks = 4, HealthRate = 0.067, StaminaRate = 5, ManaRate = -1.5, HeartbeatSeconds = 2.5, UsesMana = false, ResistStaminaDrain = 0.75, ResistManaDrain = 0.5,
                RunStart = 40, RunRanks = 6, RunSac = 3, Xp = 1000,
            },
            row);
        Assert.Equal(16.0, row.AuralAwarenessMetres); // its own range, not the visual one

        // a table whose columns are not the reader's, a row of another length, a second row for a weenie and a value the world database cannot state are refused, never read by position
        Assert.Throws<InvalidDataException>(() => CreatureBehaviour.Parse(Doc(Columns.Replace("\"xp\"", "\"experience\"", StringComparison.Ordinal))));
        Assert.Throws<InvalidDataException>(() => CreatureBehaviour.Parse(Doc(Columns, "[7,3,33556445,0.95,10,16,64,5,12,20,20,2,30,4,0.067,5,1,5,0,0.75,0.5,40,6,3]")));
        Assert.Throws<InvalidDataException>(() => CreatureBehaviour.Parse(Doc(Columns, "[7,3,33556445,0.95,10,16,64,5,12,20,20,2,30,4,0.067,5,-1.5,0,0.75,0.5,40,6,3,1000]"))); // a row of the table before the heartbeat's column
        const string Plain = "[8,-1,0,1,18,-1,0,0,-1,-1,0,0,0,0,0,0,0,5,1,1,1,0,0,1,0]";
        Assert.Throws<InvalidDataException>(() => CreatureBehaviour.Parse(Doc(Columns, Plain, Plain)));
        Assert.Throws<InvalidDataException>(() => CreatureBehaviour.Parse(Doc(Columns, "[8,-1,0,1,18,-1,0,0,-1,-1,-1,0,0,0,0,0,0,5,1,1,1,0,0,1,0]"))); // a stamina start of -1
        Assert.Throws<InvalidDataException>(() => CreatureBehaviour.Parse(Doc(Columns, "[8,-1,0,1,18,-1,0,0,-1,-1,0,0,0,0,0,0,0,5,1,1,1,0,0,4,0]"))); // an advancement class of 4
        Assert.Throws<InvalidDataException>(() => CreatureBehaviour.Parse(Doc(Columns, "[8,-1,0,0,18,-1,0,0,-1,-1,0,0,0,0,0,0,0,5,1,1,1,0,0,1,0]"))); // a scale of 0
    }

    [Fact]
    public void The_stamina_the_mana_and_the_Run_skill_are_ACE_s_formulas_over_the_row_and_the_attributes()
    {
        var attributes = new CreatureAttributes(Strength: 70, Endurance: 60, Quickness: 110, Coordination: 90, Focus: 15, Self: 15);
        var row = new CreatureBehaviourRow(7) { StaminaStart = 20, StaminaRanks = 3, ManaStart = 5, ManaRanks = 1, RunStart = 40, RunRanks = 2, RunSac = 3 };

        Assert.Equal(20 + 3 + 60, row.MaxStaminaOf(attributes)); // start + ranks + Endurance
        Assert.Equal(5 + 1 + 15, row.MaxManaOf(attributes)); // start + ranks + Self
        Assert.Equal(40 + 2 + 110, row.RunSkillOf(attributes)); // start + ranks + Quickness, specialized
        Assert.Equal(152, (row with { RunSac = 2 }).RunSkillOf(attributes)); // trained
        Assert.Equal(152, (row with { RunSac = 1 }).RunSkillOf(attributes)); // untrained: the dats' Run is usable untrained (RunMinLevel 1)
        Assert.Equal(42, (row with { RunSac = 0 }).RunSkillOf(attributes)); // inactive: its start and ranks alone (CreatureSkill.IsUsable)

        // An attribute is never under 1 in ACE's formulas (CreatureAttribute.cs:149-160), an unstated one included, and a vital's maximum never under 1 (CreatureVital.cs:168-171).
        var none = default(CreatureAttributes);
        var empty = new CreatureBehaviourRow(8);
        Assert.Equal((1, 1, 1), (empty.MaxStaminaOf(none), empty.MaxManaOf(none), empty.RunSkillOf(none)));
        Assert.Equal(0, (empty with { RunSac = 0 }).RunSkillOf(none));
        Assert.Equal(1u, CreatureBehaviourRow.AttributeOf(none, 2));
        Assert.Equal(9u, CreatureBehaviourRow.AttributeOf(new CreatureAttributes(0, 9, 0, 0, 0, 0), 2));
        Assert.Equal(60u, CreatureBehaviourRow.AttributeOf(attributes, 2));
    }

    /// <summary>
    /// The server's own objects, built by ACE's code from the world database: the lines <c>pwsh -File tools\fight-sim.ps1 creature WCID</c> printed on 2026-11-17 ("stamina", "mana", "Run", "scale", "sees a player at",
    /// "hears its kind call at", "tolerance", "experience ... a kill"). Each kind is a branch of the reader: a specialized Run, a trained one, an untrained one with no row, a vital's ranks, a creature with no attribute
    /// row at all, the one creature with a hearing range of its own, a tolerance.
    /// </summary>
    public static TheoryData<uint, string, int, int, int, double, double, double, int, long> ServerBuilt() => new()
    {
        { 7, "Drudge Skulker", 80, 15, 150, 0.95, 10, 10, 0, 1_000 },
        { 22053, "Assailer", 1220, 60, 270, 1.5, 25, 25, 0, 125_000 },
        { 22747, "Reaper", 810, 200, 335, 2.3, 24, 24, 0, 125_000 },
        { 23082, "Nubilous Golem", 390, 350, 90, 1, 17, 17, 0, 30_000 },
        { 7126, "Cursed Wisp", 350, 310, 300, 1.3, 30, 30, 0, 30_000 },
        { 22511, "Tusker Minion", 110, 100, 220, 1, 15, 15, 0, 7_000 },
        { 3, "Olthoi Worker", 345, 20, 150, 1, 20, 20, 0, 10_000 },              // Run trained
        { 23, "Virindi Servant", 150, 550, 230, 1, 16, 16, 0, 7_000 },
        { 5, "Laigus Lugian", 300, 50, 100, 1, 16, 16, 64, 3_500 },              // it attacks only after it is attacked
        { 16, "Undead", 55, 115, 20, 1, 18, 18, 0, 1_000 },                      // no Run row: untrained, its Quickness alone
        { 185, "Bandit", 55, 55, 55, 1, 30, 30, 0, 30 },                         // no stamina, mana or Run row
        { 187, "Bandit Lord", 320, 60, 200, 1, 30, 30, 0, 21_490 },
        { 35268, "Spectral Dread", 5500, 5490, 500, 1, 18, 18, 0, 2_500_000 },   // 5,000 ranks of stamina and of mana
        { 32400, "Run Warden of Forgetfulness", 1, 1, 1, 0.5, 18, 18, 0, 78 },   // no attribute row: each attribute is 1
        { 45196, "Blinded Apparition", 3420, 420, 300, 1.1, 0.1, 16, 0, 2_000_000 }, // it sees 0.1 m and hears 16 m
    };

    [Theory]
    [MemberData(nameof(ServerBuilt))]
    public void The_record_holds_what_the_server_builds(uint wcid, string name, int stamina, int mana, int run, double scale, double sees, double hears, int tolerance, long xp)
    {
        var kind = Bestiary.Find(wcid);

        Assert.NotNull(kind);
        Assert.Equal(name, kind.Identity.Name);
        Assert.Equal((stamina, mana), (kind.Vitals.MaxStamina, kind.Vitals.MaxMana));
        Assert.Equal(run, kind.Movement.TableRunSkill);
        Assert.Equal(scale, kind.Body.Scale);
        Assert.Equal((sees, hears), (kind.Awareness.TableVisualMetres, kind.Awareness.TableAuralMetres));
        Assert.Equal(tolerance, kind.Awareness.Tolerance);
        Assert.Equal(xp, kind.Reward.Xp);
    }

    [Fact]
    public void Every_kind_of_the_bestiary_has_the_behaviour_fields_and_they_are_its_row_s()
    {
        Assert.Equal(CreatureBehaviour.Count, Bestiary.Count);
        foreach (var kind in Bestiary.All)
        {
            var row = CreatureBehaviour.Find(kind.Wcid);
            Assert.NotNull(row);
            var attributes = kind.Timing!.Attributes;
            Assert.Equal((row.MaxStaminaOf(attributes), row.MaxManaOf(attributes)), (kind.Vitals.MaxStamina, kind.Vitals.MaxMana));
            Assert.Equal((row.HealthRate, row.StaminaRate, row.ManaRate, row.UsesMana), (kind.Vitals.HealthRate, kind.Vitals.StaminaRate, kind.Vitals.ManaRate, kind.Vitals.UsesMana));
            Assert.Equal((row.HeartbeatSeconds, row.HealthPerSecond, row.StaminaPerSecond, row.ManaPerSecond), (kind.Vitals.HeartbeatSeconds, kind.Vitals.HealthPerSecond, kind.Vitals.StaminaPerSecond, kind.Vitals.ManaPerSecond));
            Assert.Equal((row.ResistStaminaDrain, row.ResistManaDrain), (kind.Defence.StaminaDrainResist, kind.Defence.ManaDrainResist));
            Assert.Equal(row.RunSkillOf(attributes), kind.Movement.TableRunSkill);
            Assert.Equal((row.VisualAwarenessMetres, row.AuralAwarenessMetres), (kind.Awareness.TableVisualMetres, kind.Awareness.TableAuralMetres));
            Assert.Equal((row.Tolerance, row.TargetingTactic), (kind.Awareness.Tolerance, kind.Awareness.TargetingTactic));
            Assert.Equal((row.FriendType, row.FoeType), (kind.Awareness.FriendType, kind.Awareness.FoeType));
            Assert.Equal(row.Xp, kind.Reward.Xp);
            Assert.Equal(new CreatureBody(row.CreatureType, row.Setup, row.Scale), kind.Body);
            Assert.True(kind.Vitals.MaxStamina >= 1 && kind.Vitals.MaxMana >= 1 && kind.Movement.TableRunSkill >= 0, $"weenie {kind.Wcid}");
        }

        // one spot check of the columns a creature sheet does not print (the table's row of weenie 22053, bestiary --weenie 22053, 2026-11-17): a Tusker (CreatureType 8) that wakes Tuskers
        var assailer = Bestiary.Find(22053)!;
        Assert.Equal(new CreatureBody(8, 33556836, 1.5), assailer.Body); // Setup 0x02000964
        Assert.Equal((9, 8, null), (assailer.Awareness.TargetingTactic, assailer.Awareness.FriendType, assailer.Awareness.FoeType));
        Assert.Equal((10.0, 4.0, 2.0, true), (assailer.Vitals.HealthRate, assailer.Vitals.StaminaRate, assailer.Vitals.ManaRate, assailer.Vitals.UsesMana));
        Assert.Equal((0.75, 0.75), (assailer.Defence.StaminaDrainResist, assailer.Defence.ManaDrainResist));
        Assert.False(Bestiary.Find(23)!.Vitals.UsesMana); // a Virindi Servant's casts spend no mana (AiUsesMana 0)
    }

    [Fact]
    public void No_reader_but_the_walker_s_own_awareness_range_reads_the_new_values_and_what_every_reader_decides_by_is_unchanged()
    {
        // What the automation reads of a kind is what it read before the table: the awareness is the pinned range or ACE's default, whatever the table's own range is; the pace is the ground profile's alone.
        foreach (var kind in Bestiary.All)
        {
            Assert.Equal(CreatureAwareness.MetresOf(kind.Wcid), kind.Awareness.AwarenessMetres);
            Assert.Equal(CreatureAwareness.MetresOf(kind.Wcid), Bestiary.AwarenessMetres(kind.Wcid));
            Assert.Equal(CreatureAwareness.IsPinned(kind.Wcid), kind.Awareness.Pinned);
            var fact = CreatureFacts.All.Find(kind.Wcid)!;
            Assert.Equal((fact.RunSkill, fact.MotionTable, fact.Scale), (kind.Movement.RunSkill, kind.Movement.MotionTable, kind.Movement.Scale));
        }

        Assert.Equal(22f, Bestiary.AwarenessMetres(20)); // the cattle's pin answers, as before they were rows (M6 16 E1e)
        Assert.Equal(CreatureAwareness.DefaultMetres, Bestiary.AwarenessMetres(7126)); // a Cursed Wisp is still avoided at 18 m by default, though the table says it sees 30 m: the walk keeps the own range only under --walk-awareness=own (M6 16 E1f)
        Assert.Equal(30.0, Bestiary.Find(7126)!.Awareness.TableVisualMetres);

        // The new values are read by nothing under src/ but the facade's own files: a slice that moves a reader onto one takes its name out of this pattern. A guard by the fields' names, as a file would write a
        // read of them; the proof that the old values stand is the loop above.
        var fields = new Regex(
            @"\.Awareness\.(Table\w+|Tolerance|TargetingTactic|FriendType|FoeType)\b|TableRunSkill\b|TableVisualMetres\b|TableAuralMetres\b|\.Reward\.Xp\b|\.Body\.CreatureType\b|CreatureBody\b"
            + @"|Bestiary\.Find\([^)]*\)[!?]*\.(Body|Reward)\b|\.Vitals\.(MaxStamina|MaxMana|HealthRate|StaminaRate|ManaRate|UsesMana|HeartbeatSeconds|HealthPerSecond|StaminaPerSecond|ManaPerSecond)\b|\.Defence\.ManaDrainResist\b|CreatureBehaviour(Row)?\b",
            RegexOptions.CultureInvariant);
        string[] own = ["src/Lifestone.Client/Session/Bestiary/Bestiary.cs", "src/Lifestone.Client/Session/Bestiary/CreatureCapabilities.cs", "src/Lifestone.Client/Session/Grounds/CreatureBehaviour.cs"];
        var readers = CheckoutFiles.CSharpUnder("src/")
            .Where(path => !own.Contains(path))
            .Where(path => CheckoutFiles.Lines(CheckoutFiles.ReadText(path)).Any(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && fields.IsMatch(line)))
            .ToList();
        Assert.Empty(readers);

        // M6 16 E1f, the pattern narrowed for exactly one value: a kind's own awareness range is read outside the facade, through the facade's Bestiary.OwnAwarenessMetres (the table's range times the server's scale, the pin
        // or ACE's default for a kind the tables do not hold), and by the walker's creature feed alone. By default the feed only reports what that range would change (WalkAwareness, awareness-shadow:); it walks by it under
        // --walk-awareness=own. Every other reader keeps Bestiary.AwarenessMetres, which the loop above holds to the pinned range.
        var ownRange = new Regex(@"\bOwnAwarenessMetres\b", RegexOptions.CultureInvariant);
        var ownReaders = CheckoutFiles.CSharpUnder("src/")
            .Where(path => !own.Contains(path))
            .Where(path => CheckoutFiles.Lines(CheckoutFiles.ReadText(path)).Any(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && ownRange.IsMatch(line)))
            .ToList();
        Assert.Equal(["src/Lifestone.Client/Movement/Wayfinding/HostileCreatureFeed.cs"], ownReaders);
        Assert.Equal(30f, Bestiary.OwnAwarenessMetres(7126)); // the Cursed Wisp's own 30 m, beside the 18 m above
        Assert.Equal(22f, Bestiary.OwnAwarenessMetres(20)); // the cattle's own range is the table's 22 m, which their pin states (M6 16 E1e)

        // M6 undrainable fills, the pattern narrowed for a second value: a kind's stamina-drain resistance is read outside the facade by DrainResist.StaminaOf alone, which the fight's stamina drain prices and picks its source by
        // (an Armored Tusker's 0: no Drain Stamina Other on it; VitalsPolicyUndrainableTests). Nothing else reads it.
        var staminaResist = new Regex(@"\.Defence\.StaminaDrainResist\b", RegexOptions.CultureInvariant);
        var staminaReaders = CheckoutFiles.CSharpUnder("src/")
            .Where(path => !own.Contains(path))
            .Where(path => CheckoutFiles.Lines(CheckoutFiles.ReadText(path)).Any(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && staminaResist.IsMatch(line)))
            .ToList();
        Assert.Equal(["src/Lifestone.Client/Session/Vitals/DrainResist.cs"], staminaReaders);
    }

    /// <summary>
    /// M6 16 E1e, the heartbeat (closes <c>TODO(engine-bestiary-heartbeat)</c>): the three regeneration rates are vital points a heartbeat, and the table now holds the heartbeat's seconds (HeartbeatInterval, ACE's 5
    /// where the weenie states none: WorldObject_Tick.cs:47-48 @95ef426d), so the record gives each rate a second. The intervals pinned are the server's own objects' (<c>tools/fight-sim.ps1 creature</c>, 2026-11-17:
    /// "(health regeneration 0.05 every 2 s)" for weenie 5595, "0.85 every 10 s" for 24495, "0.15 every 480 s" for 72277, "75 every 2 s" for 21383, "-0.4 every 1 s" for 36766, no heartbeat for 32966,
    /// "0.5 every 5 s" for 1608). No caller of the automation reads them yet.
    /// </summary>
    [Theory]
    [InlineData(1608u, 5.0, 0.5, 0.1)] // Drudge Lurker: half a point a 5 s heartbeat
    [InlineData(5595u, 2.0, 0.067, 0.0335)] // Drudge Skulker: a 2 s heartbeat
    [InlineData(24495u, 10.0, 0.9, 0.09)] // Gotrok Juggernaut: a 10 s heartbeat
    [InlineData(72277u, 480.0, 0.16, 0.16 / 480)] // Jester Brazier Stopgap: a heartbeat every 8 minutes
    [InlineData(21383u, 2.0, 75.0, 37.5)] // Gaerlan: 75 health every 2 s
    [InlineData(36766u, 1.0, -0.45, -0.45)] // Asheron: a rate under 0 takes health away
    [InlineData(32966u, 0.0, 1.0, 0.0)] // Stone of Yanshi: an interval of 0 is no heartbeat (WorldObject_Tick.cs:56-68): its health never comes back, whatever its rate
    [InlineData(181u, 5.0, 0.1, 0.02)] // Auroch Cow: states no interval
    public void A_kind_s_rates_are_vital_points_a_heartbeat_and_the_record_gives_them_a_second(uint wcid, double heartbeat, double healthRate, double healthPerSecond)
    {
        var row = CreatureBehaviour.Find(wcid);
        Assert.NotNull(row);
        Assert.Equal((heartbeat, healthRate), (row.HeartbeatSeconds, row.HealthRate));
        Assert.Equal(healthPerSecond, row.HealthPerSecond, 12);
        Assert.Equal(heartbeat > 0 ? row.StaminaRate / heartbeat : 0, row.StaminaPerSecond, 12);
        Assert.Equal(heartbeat > 0 ? row.ManaRate / heartbeat : 0, row.ManaPerSecond, 12);

        var vitals = Bestiary.Find(wcid)!.Vitals;
        Assert.Equal((row.HeartbeatSeconds, row.HealthPerSecond, row.StaminaPerSecond, row.ManaPerSecond), (vitals.HeartbeatSeconds, vitals.HealthPerSecond, vitals.StaminaPerSecond, vitals.ManaPerSecond));
    }

    [Fact]
    public void Most_kinds_beat_every_5_s_and_139_state_another_interval()
    {
        var byInterval = CreatureBehaviour.All.GroupBy(row => row.HeartbeatSeconds).ToDictionary(g => g.Key, g => g.Count());
        output.WriteLine(string.Join(", ", byInterval.OrderBy(i => i.Key).Select(i => string.Create(C, $"{i.Key} s: {i.Value}"))));

        Assert.Equal(7709, byInterval[5.0]);
        Assert.Equal(139, CreatureBehaviour.All.Count(row => row.HeartbeatSeconds != 5.0));
        Assert.Equal((38, 36, 21), (byInterval[2.0], byInterval[10.0], byInterval[3.0]));
        Assert.Equal((0.0, 480.0), (byInterval.Keys.Min(), byInterval.Keys.Max()));
        Assert.Equal([32966u], CreatureBehaviour.All.Where(row => row.HeartbeatSeconds <= 0).Select(row => row.Wcid)); // the one kind with no heartbeat
        Assert.Equal([36766u, 36963u, 45705u, 72636u], CreatureBehaviour.All.Where(row => row.HealthRate < 0).Select(row => row.Wcid).Order()); // the four whose health drains
        Assert.All(CreatureBehaviour.All, row => Assert.True(row.StaminaRate >= 0 && row.ManaRate >= 0, $"weenie {row.Wcid}"));

        // the reader's rule: a rate over the interval, 0 with no heartbeat
        var row = new CreatureBehaviourRow(8) { HealthRate = 3, StaminaRate = 6, ManaRate = 1.5, HeartbeatSeconds = 2 };
        Assert.Equal((1.5, 3.0, 0.75), (row.HealthPerSecond, row.StaminaPerSecond, row.ManaPerSecond));
        Assert.Equal((0.0, 0.0), ((row with { HeartbeatSeconds = 0 }).HealthPerSecond, (row with { HeartbeatSeconds = -1 }).ManaPerSecond));
        Assert.Equal(0.6, (row with { HeartbeatSeconds = 5 }).HealthPerSecond, 12);
    }

    [Fact]
    public void The_pinned_awareness_ranges_are_the_table_s_own_the_cattle_s_too()
    {
        // The comparison E1's next slice needs before the awareness reader moves: all 74 pins against the table.
        var pinned = CreatureAwareness.Wcids.Order().ToList();
        Assert.Equal(74, pinned.Count);

        var differing = new List<string>();
        var outside = new List<uint>();
        foreach (var wcid in pinned)
        {
            if (CreatureBehaviour.Find(wcid) is not { } row)
            {
                outside.Add(wcid);
            }
            else if (row.VisualAwarenessMetres != CreatureAwareness.MetresOf(wcid))
            {
                differing.Add(string.Create(C, $"weenie {wcid}: pinned {CreatureAwareness.MetresOf(wcid)} m, the table {row.VisualAwarenessMetres} m"));
            }
        }

        output.WriteLine($"pinned {pinned.Count}; equal to the table {pinned.Count - outside.Count - differing.Count}; differing {differing.Count}; not in the table {outside.Count} ({string.Join(", ", outside)})");
        Assert.Empty(differing); // all 74 are the table's value to the metre
        Assert.Empty(outside);

        // Three were outside the table until M6 16 E1e: the Low B belt's cattle, Auroch Bull 20, Auroch Cow 181 and Auroch Yearling 182. The world database types them WeenieType.Cow (weenie.type 15), not Creature
        // (10): ACE's Cow is a Creature with its own class (WorldObjects/Cow.cs:7), and the tables took weenie.type 10 only, as the first SELECTs did. The generator takes both types now
        // (TODO(engine-bestiary-cattle) closed): their rows read VisualAwarenessRange 22, the pinned value, a Tolerance of 64 (they attack only after they are attacked) and experience 420, 445 and 108.
        uint[] cattle = [20, 181, 182];
        Assert.All(cattle, wcid => Assert.Equal(22f, CreatureAwareness.MetresOf(wcid)));
        Assert.All(cattle, wcid => Assert.Equal((22.0, 64), (CreatureBehaviour.Find(wcid)!.VisualAwarenessMetres, CreatureBehaviour.Find(wcid)!.Tolerance)));
        Assert.Equal([420L, 445L, 108L], cattle.Select(wcid => CreatureBehaviour.Find(wcid)!.Xp));
    }

    [Fact]
    public void Of_the_54_kinds_the_ground_profiles_name_12_are_pinned_and_40_of_the_other_42_are_avoided_at_ACE_s_default_instead_of_their_own_range()
    {
        var named = ProfileWcids();
        var pinned = named.Where(CreatureAwareness.IsPinned).ToList();
        var unpinned = named.Where(w => !CreatureAwareness.IsPinned(w)).ToList();
        var changes = unpinned.Select(w => (Wcid: w, Kind: Bestiary.Find(w)!)).Where(k => k.Kind.Awareness.TableVisualMetres != k.Kind.Awareness.AwarenessMetres)
            .Select(k => (k.Wcid, k.Kind.Identity.Name, Today: (double)k.Kind.Awareness.AwarenessMetres, Table: k.Kind.Awareness.TableVisualMetres!.Value)).OrderByDescending(c => Math.Abs(c.Table - c.Today)).ThenBy(c => c.Wcid).ToList();
        foreach (var c in changes)
        {
            output.WriteLine(string.Create(C, $"weenie {c.Wcid} {c.Name}: read today {c.Today} m (ACE's default), its own range {c.Table} m ({c.Table - c.Today:+0.#;-0.#} m)"));
        }

        Assert.Equal((54, 12, 42), (named.Count, pinned.Count, unpinned.Count)); // M6 redoubt: the Holtburg Redoubt's five drudges: 7, 192, 193 and 940 are pinned, the Drudge Slinker 1468 is not; M6 retreat: the Tusker Retreat names the two Tusker Kin, 22509 and 22510
        Assert.All(pinned, w => Assert.Equal((double)CreatureAwareness.MetresOf(w), Bestiary.Find(w)!.Awareness.TableVisualMetres)); // a pinned kind would not change
        Assert.All(unpinned, w => Assert.Equal(CreatureAwareness.DefaultMetres, Bestiary.Find(w)!.Awareness.AwarenessMetres)); // an unpinned kind is read at 18 m today

        // 40 of the 42 would change; the other two see 18 m by their own rows
        Assert.Equal(40, changes.Count);
        Assert.Equal(2, unpinned.Count - changes.Count);

        // the largest: a Cursed Wisp sees 30 m, 12 m past the 18 m the walker keeps off it; twelve kinds see 25 m; the Tusker Minions, Worshippers and Kin see 15 m, 3 m short of it
        Assert.Equal((7126u, 30.0), (changes[0].Wcid, changes[0].Table));
        Assert.Equal([1629u, 7184u, 10810u, 11540u, 11541u, 16916u, 22053u, 22591u, 22593u, 22599u, 22611u, 27800u], changes.Where(c => c.Table == 25).Select(c => c.Wcid));
        Assert.Equal([11u, 236u, 1628u, 22585u, 22589u, 22597u, 22605u, 22746u, 22747u], changes.Where(c => c.Table == 24).Select(c => c.Wcid));
        Assert.Equal([4244u, 22506u], changes.Where(c => c.Table == 22).Select(c => c.Wcid));
        Assert.Equal([7607u, 22519u, 22520u, 22595u, 22600u, 22601u], changes.Where(c => c.Table == 20).Select(c => c.Wcid));
        Assert.Equal([199u, 23082u], changes.Where(c => c.Table == 17).Select(c => c.Wcid));
        Assert.Equal([23u], changes.Where(c => c.Table == 16).Select(c => c.Wcid));
        Assert.Equal([22509u, 22510u, 22511u, 22512u, 22515u, 22516u], changes.Where(c => c.Table == 15).Select(c => c.Wcid)); // M6 retreat: the Tusker Kin see 15 m too
        Assert.Equal(30, changes.Count(c => c.Table > c.Today)); // 30 see farther than they are avoided at
        Assert.Equal([1468u], changes.Where(c => c.Table == 10).Select(c => c.Wcid)); // M6 redoubt: the Redoubt's Drudge Slinker 1468 sees 10 m (its row) and is avoided at 18 m
        Assert.Equal(10, changes.Count(c => c.Table < c.Today)); // 10 see less far (M6 retreat: and the two Tusker Kin)
    }

    [Fact]
    public void Every_experience_pace_and_awareness_a_ground_profile_states_is_the_table_s_value()
    {
        // The comparison E1's next slices need before the profiles' hand fields go: each value a profile states of a kind against the bestiary's. A difference would be listed here by ground, weenie and both values.
        var differing = new List<string>();
        var (xp, pace, awareness) = (0, 0, 0);
        foreach (var ground in GroundCatalog.All)
        {
            foreach (var target in ground.Targets.Where(t => t.Xp is not null))
            {
                xp++;
                Compare(ground.Key + ":targets", target.Wcid, "xp", target.Xp!.Value, Bestiary.Find(target.Wcid)!.Reward.Xp);
            }

            foreach (var hostile in ground.Approach?.Hostiles ?? [])
            {
                var kind = Bestiary.Find(hostile.Wcid)!;
                if (hostile.Xp is { } stated)
                {
                    xp++;
                    Compare(ground.Key + ":approach", hostile.Wcid, "xp", stated, kind.Reward.Xp);
                }

                if (hostile.RunSkill is { } run)
                {
                    pace++;
                    Compare(ground.Key + ":approach", hostile.Wcid, "pace.run_skill", run, kind.Movement.TableRunSkill);
                    Compare(ground.Key + ":approach", hostile.Wcid, "pace.scale", hostile.Scale, kind.Body.Scale);
                    Compare(ground.Key + ":approach", hostile.Wcid, "pace.motion_table", hostile.MotionTable ?? 0u, kind.Timing?.MotionTable);
                }

                if (hostile.AwarenessMetres is { } metres)
                {
                    awareness++;
                    Compare(ground.Key + ":approach", hostile.Wcid, "awareness_m", metres, kind.Awareness.TableVisualMetres);
                }
            }
        }

        output.WriteLine($"stated by the profiles: {xp} xp, {pace} pace, {awareness} awareness_m; differing from the table: {differing.Count}");
        foreach (var line in differing)
        {
            output.WriteLine(line);
        }

        Assert.Empty(differing);
        Assert.Equal((28, 14, 19), (xp, pace, awareness)); // 2026-11-17: the Armory, the Barracks, the Holding and the Quarters state them

        void Compare<T>(string where, uint wcid, string field, T profile, T? table)
            where T : struct
        {
            if (table is not { } value || !EqualityComparer<T>.Default.Equals(profile, value))
            {
                differing.Add(string.Create(C, $"{where} weenie {wcid} {field}: the profile {profile}, the table {(table is { } t ? t.ToString() : "none")}"));
            }
        }
    }

    [Fact]
    public void A_read_of_the_behaviour_fields_allocates_nothing()
    {
        Assert.NotNull(Bestiary.Find(7)); // the tables are loaded once, here
        AllocationAssert.Nothing(static () =>
        {
            var kind = Bestiary.Find(7)!;
            _ = kind.Vitals.MaxStamina;
            _ = kind.Awareness.TableVisualMetres;
            _ = kind.Movement.TableRunSkill;
            _ = kind.Body.Scale;
            _ = CreatureBehaviour.Find(7)!.AuralAwarenessMetres;
            _ = CreatureBehaviour.Find(999_999_999);
        });
    }

    [RequiresDatsFact]
    public void The_dats_give_Run_as_Quickness_usable_untrained_and_stamina_and_mana_as_Endurance_and_Self()
    {
        var run = Lifestone.Data.CharacterCreation.SkillTableData.Load(fixture.Dats).Skills.Single(s => s.Id == 24);
        Assert.Equal(CreaturePace.RunFormula, run.Formula);
        Assert.Equal(CreatureBehaviourRow.RunMinLevel, (int)run.MinLevel);

        // ACE reads the vital formulas from the dats' SecondaryAttributeTable, 0x0E000003 (ACE.DatLoader/FileTypes/SecondaryAttributeTable.cs:11 @95ef426d)
        Assert.True(fixture.Dats.TryGetPortal<DatReaderWriter.DBObjs.VitalTable>(0x0E000003u, out var vitals));
        static Lifestone.Data.CharacterCreation.SkillTableFormula Of(DatReaderWriter.Types.SkillFormula f) => new(f.AdditiveBonus, f.Attribute1Multiplier, f.Attribute2Multiplier, f.Divisor, (uint)f.Attribute1, (uint)f.Attribute2);
        Assert.Equal(CreatureBehaviourRow.MaxStaminaFormula, Of(vitals.Stamina));
        Assert.Equal(CreatureBehaviourRow.MaxManaFormula, Of(vitals.Mana));
        Assert.Equal(GroundDraft.MaxHealthFormula, Of(vitals.Health));
    }
}

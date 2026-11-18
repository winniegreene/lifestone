using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Lifestone.FightSim;

namespace Lifestone.Client.Tests.Tools;

/// <summary>
/// M6 item 16 E1c, the bestiary's generator (docs/fight-sim.md, "The bestiary"; docs/automation-engine.md section 2.1): <c>tools/fight-sim bestiary</c> writes the tracked creature tables again from the world
/// database (three when it was written; four with E1d's creature-behaviour.json, whose reader's tests are <c>CreatureBehaviourTests</c>). Its column lists, its picks, its writer and its check are plain C# with no ACE type (<c>tools/fight-sim/Bestiary{Model,Tables,Rows,Writer,Check}.cs</c>, compiled into this project too), so they are
/// proved here on made-up rows and against the tracked tables' own text, with no database, no dats and no ACE build. What needs the database, the generator's parity itself, is
/// <see cref="BestiaryParityRunTests"/> (<c>Tier=Tool</c>, lead-run).
/// </summary>
public sealed class BestiaryGeneratorTests
{
    private const int Level = 25;
    private const int Damage = 44;
    private const int DamageMod = 63;

    private static TableFile File(string name) => BestiaryTables.Files.Single(f => f.FileName == name);

    private static string Tracked(string name) => System.IO.File.ReadAllText(RepositoryFiles.PathOf([.. BestiaryTables.TrackedFolder.Split('/'), name]));

    private static WorldFacts World(params uint[] creatures)
    {
        var world = new WorldFacts();
        foreach (var wcid in creatures)
        {
            world.WeenieTypes[wcid] = AceIds.Creature;
        }

        return world;
    }

    private static string Row(IReadOnlyList<Column> columns, WorldFacts world, uint wcid, params string[] names)
    {
        var entry = new Entry(world, wcid);
        return string.Join(",", names.Select(n => BestiaryWriter.Text(Bestiary.Value(columns.Single(c => c.Name == n), entry))));
    }

    public static TheoryData<string> Files() => [.. BestiaryTables.Files.Select(f => f.FileName)];

    [Fact]
    public void The_column_lists_name_the_columns_the_tracked_tables_name()
    {
        using var creatures = JsonDocument.Parse(Tracked("creatures.json"));
        using var resists = JsonDocument.Parse(Tracked("creature-resists.json"));
        static string[] Names(JsonElement list) => [.. list.EnumerateArray().Select(n => n.GetString()!)];

        Assert.Equal(Names(creatures.RootElement.GetProperty("columns")), BestiaryTables.Creatures.Select(c => c.Name));
        Assert.Equal(Names(creatures.RootElement.GetProperty("spell_columns")), BestiaryTables.Spells.Select(c => c.Name));
        Assert.Equal(Names(resists.RootElement.GetProperty("columns")), BestiaryTables.Resists.Select(c => c.Name));
        using var behaviour = JsonDocument.Parse(Tracked("creature-behaviour.json"));
        Assert.Equal(Names(behaviour.RootElement.GetProperty("columns")), BestiaryTables.Behaviour.Select(c => c.Name));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void The_writer_prints_a_tracked_table_s_own_rows_back_byte_for_byte(string name)
    {
        // The tracked table's rows, read as JSON and handed to the writer with the table's own sources header: the layout (which member is on which line, where a list closes), the number text and the string
        // escapes are the tracked file's, to the byte. No database: the rows are the file's.
        var file = File(name);
        var tracked = Tracked(name);
        using var json = JsonDocument.Parse(tracked);
        static Cell CellOf(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()!,
            JsonValueKind.Array => Cell.List([.. value.EnumerateArray().Select(CellOf)]),
            JsonValueKind.Number when value.TryGetInt64(out var whole) => whole,
            JsonValueKind.Number => value.GetDouble(),
            _ => throw new InvalidOperationException($"a table holds a {value.ValueKind}"),
        };
        var rows = file.Members.Select(m => m.Shape == Shape.Names
            ? []
            : (IReadOnlyList<Cell[]>)[.. json.RootElement.GetProperty(m.Key).EnumerateArray().Select(r => r.EnumerateArray().Select(CellOf).ToArray())]).ToList();
        var counts = new Dictionary<string, int>();

        var written = BestiaryWriter.Write(file, BestiaryWriter.SourcesOf(tracked), rows, counts);

        Assert.True(written == tracked, $"{name}: the writer's text differs from the tracked table: {BestiaryCheck.Compare(file, Encoding.UTF8.GetBytes(written), Encoding.UTF8.GetBytes(tracked)).Lines[0]}");
        Assert.All(file.Members.Where(m => m.Shape != Shape.Names), m => Assert.True(counts[m.Key] > 300, $"{name}: {m.Key} has {counts[m.Key]} rows"));
        string[] members = ["sources", .. file.Members.Select(m => m.Key)];
        Assert.Equal(members, json.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void A_creature_s_row_reads_each_column_at_its_source_and_prints_the_column_s_default_where_the_world_has_no_row()
    {
        var world = World(7, 8);
        world.Strings[(7, 1)] = "Drudge\tSkulker\nthe \"Sly\"";
        world.Numbers[(Source.Int, 7, Level)] = 8;
        world.Ranked[(Source.Vital, 7, 1)] = new Ranked(12, 30);
        world.Ranked[(Source.Attribute, 7, 2)] = new Ranked(60, 5);
        world.Floats[(7, 125)] = 0.75;
        world.Ranked[(Source.Skill, 7, 15)] = new Ranked(5, 0, 3);
        world.Numbers[(Source.DataId, 7, 2)] = 150994952;
        world.Numbers[(Source.Bool, 7, 7)] = 1;
        world.Floats[(7, 34)] = 2.29;
        world.Numbers[(Source.Int, 7, 2)] = 99;
        world.Numbers[(Source.Int, 7, 1)] = 98;

        // A property of the same number in another table is another property: PropertyInt 1 (98) is not PropertyString 1 or the vital 1, PropertyInt 2 (99) not the attribute 2 or PropertyDataId 2.
        Assert.Equal("7,\"Drudge Skulker the \\\"Sly\\\"\",8,12,30,65,0.75,5,0,3,150994952,2.29,1",
            Row(BestiaryTables.Creatures, world, 7, "wcid", "name", "level", "health_start", "health_ranks", "endurance", "resist_health_drain", "defence_start", "defence_ranks", "defence_sac", "motion_table", "powerup_time", "human_magic"));

        // The creature the world states nothing of: every column's default, as the SELECT's COALESCE gave it.
        var row = Assert.Single(new WeenieRows(Scope.Creatures, BestiaryTables.Creatures).Rows(World(8)));
        // name, level; the vital, the attribute, the drain resistance (ACE's 1), no damage type; the 30 columns from defence_start to quickness; human_magic 0; the counts 0; an empty book.
        Assert.Equal("[8,\"\",0,-1,-1,-1,1,0," + string.Join(",", Enumerable.Repeat("-1", 30)) + ",0,-1,-1,-1,0,0,-1,[]]", BestiaryWriter.Text(Cell.List(row)));
        Assert.Equal(BestiaryTables.Creatures.Count, row.Length);
    }

    [Fact]
    public void The_weapon_is_the_wielded_melee_or_thrown_weapon_that_hits_hardest_and_the_earliest_among_equals()
    {
        var world = World(7);
        world.WeenieTypes[301] = AceIds.MeleeWeapon;   // Damage 10 x DamageMod 1 (absent) = 10
        world.WeenieTypes[302] = AceIds.Missile;       // Damage 4 x 3 = 12: the hardest
        world.WeenieTypes[303] = AceIds.MeleeWeapon;   // Damage 12 x 1 = 12: equal, but a later row
        world.WeenieTypes[304] = AceIds.MeleeWeapon;   // Damage 50, but carried (Contain), not wielded
        world.WeenieTypes[305] = 2;                    // wielded clothing: no weapon
        world.Numbers[(Source.Int, 301, Damage)] = 10;
        world.Numbers[(Source.Int, 302, Damage)] = 4;
        world.Floats[(302, DamageMod)] = 3;
        world.Numbers[(Source.Int, 303, Damage)] = 12;
        world.Numbers[(Source.Int, 304, Damage)] = 50;
        world.Numbers[(Source.Int, 302, 45)] = 2;
        world.Numbers[(Source.Int, 302, 46)] = 8;
        world.Floats[(302, 22)] = 0.5;

        // The create list is read in id order whatever the order the rows came in; 399 is a class the world does not hold (the SELECT's inner join drops it).
        world.CreateLists[7] = [new(13, AceIds.Wield, 303), new(10, AceIds.Wield, 305), new(11, AceIds.Wield | 8, 301), new(12, AceIds.Wield, 302), new(14, 1, 304), new(9, AceIds.Wield, 399)];

        var entry = new Entry(world, 7);
        Assert.Equal(302u, entry.Weapon);
        Assert.Null(entry.Launcher);
        Assert.Null(entry.Ammunition);
        Assert.Equal("302,4,4,3,0.5,2,-1,-1,8,-1,-1,-1,-1",
            Row(BestiaryTables.Creatures, world, 7, "weapon_wcid", "weapon_kind", "weapon_damage", "weapon_damage_mod", "weapon_variance", "weapon_dtype", "weapon_crit_multiplier", "weapon_crit_frequency",
                "weapon_combat_style", "weapon_attack_type", "launcher_wcid", "launcher_damage_mod", "ammo_damage"));
        Assert.Equal("2", Row(BestiaryTables.Creatures, world, 7, "melee_weapons")); // 301 and 303: wielded melee weapons; 304 is carried, 302 is thrown

        // With the equal weapon first in id order, it is the pick.
        world.CreateLists[7] = [new(11, AceIds.Wield, 303), new(12, AceIds.Wield, 302)];
        Assert.Equal(303u, new Entry(world, 7).Weapon);
    }

    [Fact]
    public void The_launcher_is_the_earliest_wielded_one_the_ammunition_the_hardest_of_the_whole_create_list_and_a_shield_is_counted_by_its_combat_use()
    {
        var world = World(7);
        world.WeenieTypes[401] = AceIds.MissileLauncher;
        world.WeenieTypes[402] = AceIds.MissileLauncher;
        world.WeenieTypes[501] = AceIds.Ammunition;
        world.WeenieTypes[502] = AceIds.Ammunition;
        world.WeenieTypes[601] = 1;
        world.Floats[(401, DamageMod)] = 2.35;
        world.Floats[(401, 136)] = 2.5;
        world.Numbers[(Source.Int, 401, 46)] = 16;
        world.Numbers[(Source.Int, 501, Damage)] = 6;
        world.Numbers[(Source.Int, 502, Damage)] = 9;
        world.Floats[(502, 22)] = 0.4;
        world.Numbers[(Source.Int, 502, 45)] = 2;
        world.Numbers[(Source.Int, 601, AceIds.CombatUse)] = AceIds.Shield;
        world.CreateLists[7] = [new(20, AceIds.Wield, 402), new(19, AceIds.Wield, 401), new(21, 1, 501), new(22, 1, 502), new(23, AceIds.Wield, 601), new(24, 1, 601)];

        Assert.Equal("-1,401,2.35,2.5,-1,16,9,0.4,2,0,1",
            Row(BestiaryTables.Creatures, world, 7, "weapon_wcid", "launcher_wcid", "launcher_damage_mod", "launcher_crit_multiplier", "launcher_crit_frequency", "launcher_combat_style", "ammo_damage", "ammo_variance",
                "ammo_dtype", "melee_weapons", "shields"));
    }

    [Fact]
    public void The_body_part_is_the_one_that_hits_hardest_and_the_damage_types_are_those_of_every_part_that_attacks()
    {
        var world = World(7, 8);
        world.BodyParts[7] =
        [
            new(Key: 0, DType: 4, DVal: 0, DVar: 0.1f),     // does not attack: neither the part nor a damage type
            new(Key: 5, DType: 1, DVal: 12, DVar: 0.75f),
            new(Key: 3, DType: 2, DVal: 12, DVar: 0.5f),    // the same d_Val, the smaller d_Var
            new(Key: 2, DType: 1024, DVal: 12, DVar: 0.5f), // the same again, the smaller key: the pick
            new(Key: 1, DType: 16, DVal: 3, DVar: 0f),
        ];

        Assert.Equal("12,0.5,1024,1043", Row(BestiaryTables.Creatures, world, 7, "part_dval", "part_dvar", "part_dtype", "damage_types"));
        Assert.Equal("-1,-1,-1,0", Row(BestiaryTables.Creatures, world, 8, "part_dval", "part_dvar", "part_dtype", "damage_types"));
    }

    [Fact]
    public void A_creature_s_spell_book_is_in_id_order_and_the_spell_table_has_a_row_for_each_spell_of_a_book_it_holds()
    {
        var world = World(7, 8);
        world.WeenieTypes[9] = 1; // not a creature: its book adds no spell row
        world.SpellBooks[7] = [new(31, 85, 2.03f), new(30, 4312, 2.0004f)];
        world.SpellBooks[8] = [new(40, 85, 2.5f), new(41, 99999, 2f)];
        world.SpellBooks[9] = [new(50, 1, 2f)];
        world.Spells[85] = new() { ["Name"] = "Flame\tBolt VI", ["EType"] = 16L, ["BaseIntensity"] = 84L, ["Variance"] = 84L, ["NumProjectiles"] = 1L };
        world.Spells[4312] = new() { ["Name"] = "Drain", ["Boost"] = -4L, ["DamageType"] = 128L, ["Source"] = 2L, ["TransferCap"] = 60L };
        world.Spells[1] = new() { ["Name"] = "Nobody's" };

        Assert.Equal("[[4312,2.0004],[85,2.03]]", Row(BestiaryTables.Creatures, world, 7, "spells"));

        // 99999 is in a book and not in the spell table: no row (the SELECT reads the spell table). By spell id; a NULL column is the column's default.
        var rows = new SpellRows(BestiaryTables.Spells).Rows(world).Select(r => BestiaryWriter.Text(Cell.List(r)));
        Assert.Equal(["[85,\"Flame Bolt VI\",16,84,84,1,0,0,-1,-1,-1]", "[4312,\"Drain\",-1,-1,-1,-1,-4,0,128,2,60]"], rows);
    }

    [Fact]
    public void A_resistance_row_is_written_only_for_a_creature_that_records_one_and_a_weapon_row_only_for_a_wielded_weapon_with_a_skill()
    {
        var world = World(7, 8);
        world.WeenieTypes[301] = AceIds.MeleeWeapon;
        world.WeenieTypes[302] = AceIds.MeleeWeapon;
        world.WeenieTypes[401] = AceIds.MissileLauncher;
        world.Floats[(7, 67)] = 1.42;
        world.Floats[(8, 125)] = 0.5;          // a float that is no resistance: creature 8 has no row
        world.Floats[(301, 64)] = 0.1;         // a weapon is no creature
        world.CreateLists[7] = [new(1, AceIds.Wield, 302), new(2, AceIds.Wield, 401)];
        world.CreateLists[8] = [new(3, AceIds.Wield, 301), new(4, AceIds.Wield, 401)];
        world.Numbers[(Source.Int, 401, 48)] = 47;
        world.Numbers[(Source.Int, 301, 48)] = 44;
        world.Numbers[(Source.Int, 7, 48)] = 45; // a creature's own WeaponSkill: the creature is no wielded weapon

        static string[] Text(RowSet set, WorldFacts w) => [.. set.Rows(w).Select(r => BestiaryWriter.Text(Cell.List(r)))];
        var resists = File("creature-resists.json").Members.Single(m => m.Key == "resists").Rows;
        var weapons = File("creature-skills.json").Members.Single(m => m.Key == "weapons").Rows;

        Assert.Equal(["[7,1,1,1,1.42,1,1,1]"], Text(resists, world));
        Assert.Equal(["[301,44]", "[401,47]"], Text(weapons, world)); // 302 is wielded and records no WeaponSkill; 401 is wielded by two and listed once
    }

    [Fact]
    public void A_skill_row_lists_the_creature_s_listed_skills_by_skill_id_each_with_its_start_its_ranks_and_its_class()
    {
        var world = World(7, 8, 9);
        world.Ranked[(Source.Skill, 7, 45)] = new Ranked(170, 0, 3);
        world.Ranked[(Source.Skill, 7, 11)] = new Ranked(20, 4, 2);
        world.Ranked[(Source.Skill, 7, 15)] = new Ranked(90, 0, 3);  // Magic Defense is creatures.json's, not this table's
        world.Ranked[(Source.Skill, 8, 6)] = new Ranked(50, 0, 3);   // Melee Defense only: no row
        world.Ranked[(Source.Attribute, 9, 1)] = new Ranked(50, 0);  // an attribute numbered as a skill is no skill

        var rows = new SkillRows(BestiaryTables.Skills).Rows(world).Select(r => BestiaryWriter.Text(Cell.List(r)));

        Assert.Equal(["[7,11,20,4,2,45,170,0,3]"], rows);
    }

    [Fact]
    public void Each_layout_puts_its_members_and_rows_on_the_lines_the_tracked_tables_have_them_on()
    {
        IReadOnlyList<Column> columns = [Col.Key("wcid"), Col.Int("level", "Level", Level, absent: 0)];
        var set = new WeenieRows(Scope.Creatures, columns);
        IReadOnlyList<Cell[]> two = [[1L, 5L], [2L, 0.25]];
        const string Sources = "{\"a\": \"b\"}";

        // creatures.json: every member on the first line, the last one's rows a line each, the file closed on the last row's line.
        var tight = new TableFile("t.json", MemberLines: false, [new("columns", Shape.Names, set), new("flat", Shape.Inline, set), new("rows", Shape.Tight, set)]);
        Assert.Equal("{\"sources\":{\"a\": \"b\"},\"columns\":[\"wcid\",\"level\"],\"flat\":[[1,5],[2,0.25]],\"rows\":[\n[1,5],\n[2,0.25]]}\n", BestiaryWriter.Write(tight, Sources, [[], two, two]));

        // The resistances and the skills: a member a line, a row a line, each list closed on a line of its own.
        var block = new TableFile("b.json", MemberLines: true, [new("columns", Shape.Names, set), new("rows", Shape.Block, set), new("more", Shape.Block, set)]);
        Assert.Equal("{\"sources\":{\"a\": \"b\"},\n\"columns\":[\"wcid\",\"level\"],\n\"rows\":[\n[1,5],\n[2,0.25]\n],\n\"more\":[\n]}\n", BestiaryWriter.Write(block, Sources, [[], two, []]));

        // From a world: the rows are the row set's, and the counts are reported by member.
        var world = World(2, 1);
        world.Numbers[(Source.Int, 1, Level)] = 5;
        var counts = new Dictionary<string, int>();
        Assert.Equal("{\"sources\":{},\"columns\":[\"wcid\",\"level\"],\"flat\":[[1,5],[2,0]],\"rows\":[\n[1,5],\n[2,0]]}\n", BestiaryWriter.Write(tight, world, "{}", counts));
        Assert.Equal(new Dictionary<string, int> { ["flat"] = 2, ["rows"] = 2 }, counts);
        Assert.Throws<ArgumentException>(() => BestiaryWriter.Write(tight, Sources, [two]));
    }

    [Theory]
    [InlineData(1.0, "1")]
    [InlineData(0.75, "0.75")]
    [InlineData(0.205, "0.205")]
    [InlineData(2.0004, "2.0004")]
    [InlineData(-30.0, "-30")]
    [InlineData(0.1 + 0.2, "0.30000000000000004")]
    public void A_double_is_printed_as_its_shortest_text_that_reads_back_equal(double value, string text)
    {
        Assert.Equal(text, BestiaryWriter.Text(value));
        Assert.Equal(value, double.Parse(BestiaryWriter.Text(value), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_single_is_printed_as_a_single_a_text_as_an_ASCII_JSON_string_and_a_number_JSON_cannot_hold_is_refused()
    {
        // A body part's d_Var and a spell book's probability are float columns: 0.45f is 0.44999998807907104 as a double, and prints as the database prints it.
        Assert.Equal("0.45", BestiaryWriter.Text(0.45f));
        Assert.Equal("2.03", BestiaryWriter.Text(2.03f));
        Assert.Equal("0.44999998807907104", BestiaryWriter.Text((double)0.45f));
        Assert.Equal("\"Bag labeled \\\"Black\\\"\"", BestiaryWriter.Text("Bag labeled \"Black\""));
        Assert.Equal("\"a\\\\b\\u0009c\\u00e9\"", BestiaryWriter.Text("a\\b\tcé"));
        Assert.Equal("[1,[2,\"x\"],[]]", BestiaryWriter.Text(Cell.List([1L, Cell.List([2L, "x"]), Cell.List([])])));
        Assert.Throws<InvalidOperationException>(() => BestiaryWriter.Text(double.NaN));
        Assert.Throws<InvalidOperationException>(() => BestiaryWriter.Text(float.PositiveInfinity));
    }

    [Fact]
    public void The_sources_header_is_carried_as_it_stands_whatever_braces_and_quotes_its_prose_holds()
    {
        const string Sources = "{\"how\": \"SELECT '}' AS a, \\\"x\\\" FROM t; {not a member}\", \"use\":{\"nested\":\"}\\\\\"}}";

        Assert.Equal(Sources, BestiaryWriter.SourcesOf("{\"sources\":" + Sources + ",\n\"rows\":[\n[1,\"}\"]\n]}\n"));
        Assert.Throws<ArgumentException>(() => BestiaryWriter.SourcesOf("{\"rows\":[]}"));
        Assert.Throws<ArgumentException>(() => BestiaryWriter.SourcesOf("{\"sources\":{\"how\": \"never closed\""));
        // the first three carry theirs: the prose is kept once, in the tracked table. The fourth (M6 16 E1d) states its own, and the tracked table's header is that statement to the byte.
        Assert.Equal(["creature-behaviour.json"], BestiaryTables.Files.Where(f => f.StatedSources is not null).Select(f => f.FileName));
        Assert.Equal(BestiaryTables.BehaviourSources, BestiaryWriter.SourcesOf(Tracked("creature-behaviour.json")));
    }

    [Fact]
    public void The_behaviour_table_s_header_states_what_was_read_and_every_column_s_default_with_its_place_in_ACE_s_source()
    {
        using var sources = JsonDocument.Parse(BestiaryTables.BehaviourSources);
        var members = sources.RootElement.EnumerateObject().ToDictionary(m => m.Name, m => m.Value.GetString()!);

        Assert.Equal(["behaviour", "columns", "computed", "use"], members.Keys);
        Assert.Contains("ACE-World-16PY-Patches v0.9.296", members["behaviour"], StringComparison.Ordinal); // the pinned world SQL (server/versions.json)
        Assert.Contains("pwsh -File tools/fight-sim.ps1 bestiary", members["behaviour"], StringComparison.Ordinal);
        Assert.Contains("Written on " + BestiaryTables.BehaviourRead + ".", members["behaviour"], StringComparison.Ordinal);
        Assert.Matches("^20[0-9]{2}-[01][0-9]-[0-3][0-9]$", BestiaryTables.BehaviourRead);

        // every column's own line is in the header, so a default and its citation are written once, in the column list; and every column but the key cites a file and a line of ACE's source
        Assert.All(BestiaryTables.Behaviour, c => Assert.Contains(c.Describe(), members["columns"], StringComparison.Ordinal));
        Assert.All(BestiaryTables.Behaviour.Skip(1), c => Assert.Matches(@"[A-Za-z_/]+\.cs:[0-9]+", c.Cite!));
        Assert.All(BestiaryTables.Files.Where(f => f.StatedSources is null).SelectMany(f => f.Members).SelectMany(m => m.Rows.Columns), c => Assert.Null(c.Cite)); // the first three tables' lines are as they were

        // the header is plain ASCII with no line break, as the tracked tables' are, and names nothing outside the repository
        Assert.DoesNotContain(BestiaryTables.BehaviourSources, ch => ch is < ' ' or > '~');
        Assert.DoesNotContain(":\\", BestiaryTables.BehaviourSources, StringComparison.Ordinal);
        Assert.Equal("{\"a\": \"b\", \"c\": \"d \\\"e\\\"\"}", BestiaryWriter.Sources(("a", "b"), ("c", "d \"e\"")));
    }

    [Fact]
    public void The_check_says_identical_for_the_same_bytes_and_names_the_first_byte_that_differs_when_only_the_text_does()
    {
        var file = File("creature-resists.json");
        var tracked = Encoding.UTF8.GetBytes("{\"sources\":{\"a\":\"b\"},\n\"columns\":[\"wcid\"],\n\"resists\":[\n[7,1,1,1,1.42,1,1,1]\n]}\n");

        var same = BestiaryCheck.Compare(file, [.. tracked], tracked);
        Assert.True(same.Same);
        Assert.Equal([$"creature-resists.json: identical (byte for byte, {tracked.Length} bytes)"], same.Lines);

        // The same table, laid out otherwise: 1.0 for 1, a space, the members in another order.
        var other = Encoding.UTF8.GetBytes("{\"sources\":{\"a\":\"b\"},\n\"resists\":[\n[7, 1.0,1,1,1.42,1,1,1]\n],\"columns\":[\"wcid\"]}\n");
        var spaced = BestiaryCheck.Compare(file, other, tracked);
        Assert.True(spaced.Same);
        Assert.Equal(
            [$"creature-resists.json: identical as JSON; the bytes differ: first at byte 23 (line 2), generated 'resists\":[\\n[7, 1.0,1,1,1', tracked 'columns\":[\"wcid\"],\\n\"resi'; {other.Length} bytes generated, {tracked.Length} tracked"],
            spaced.Lines);

        var broken = BestiaryCheck.Compare(file, Encoding.UTF8.GetBytes("{\"sources\":"), tracked);
        Assert.False(broken.Same);
        Assert.StartsWith("creature-resists.json: DIFFERENT: the generated table is not JSON", broken.Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void The_check_counts_the_weenie_classes_that_differ_names_the_fields_and_shows_the_first_ten_with_both_values()
    {
        var file = File("creature-resists.json");
        static byte[] Table(string sources, string columns, IEnumerable<string> rows) =>
            Encoding.UTF8.GetBytes("{\"sources\":" + sources + ",\n\"columns\":" + columns + ",\n\"resists\":[\n" + string.Join(",\n", rows) + "\n]}\n");
        const string Columns = "[\"wcid\",\"slash\",\"pierce\",\"bludgeon\",\"fire\",\"cold\",\"acid\",\"electric\"]";
        var tracked = Table("{\"resists\":\"how\",\"use\":\"why\"}", Columns, Enumerable.Range(1, 30).Select(i => $"[{i},1,1,1,0.5,1,1,1]"));
        var generated = Table("{\"resists\":\"how\",\"use\":\"another\"}", "[\"wcid\",\"slash\"]", Enumerable.Range(1, 31)
            .Where(i => i != 30)                                                                                   // 30: only tracked; 31: only generated
            .Select(i => i <= 12 ? $"[{i},1,1,1,0.75,1,1,1]" : i == 20 ? "[20,2,1,1,0.5,1,1,0]" : i == 21 ? "[21,1,1]" : $"[{i},1,1,1,0.5,1,1,1]"));

        var result = BestiaryCheck.Compare(file, generated, tracked);

        Assert.False(result.Same);
        Assert.Equal(
        [
            "creature-resists.json: DIFFERENT",
            "  sources: differs in use",
            "  columns: generated [\"wcid\",\"slash\"], tracked " + Columns,
            "  resists: 16 weenie classes differ of 30 generated and 30 tracked (14 with a different row, 1 only generated, 1 only tracked)",
            "    fields that differ: fire (12), cells in the row (1), electric (1), slash (1)",
            "    only generated, the first 1: 31",
            "    only tracked, the first 1: 30",
            .. Enumerable.Range(1, BestiaryCheck.Shown).Select(i => $"    weenie class {i}: fire: generated 0.75, tracked 0.5"),
        ], result.Lines);

        // A spell row is named a spell, and a row whose cells no column names (a skill row) by the cell's place.
        var skills = BestiaryCheck.Compare(File("creature-skills.json"),
            Encoding.UTF8.GetBytes("{\"sources\":{},\"skills\":[[7,45,170,0,3]],\"weapons\":[[301,44]]}"), Encoding.UTF8.GetBytes("{\"sources\":{},\"skills\":[[7,45,171,0,3]],\"weapons\":[[301,45]]}"));
        Assert.Equal(
        [
            "creature-skills.json: DIFFERENT",
            "  skills: 1 weenie classes differ of 1 generated and 1 tracked (1 with a different row, 0 only generated, 0 only tracked)",
            "    fields that differ: cell 2 (1)",
            "    weenie class 7: cell 2: generated 170, tracked 171",
            "  weapons: 1 weenie classes differ of 1 generated and 1 tracked (1 with a different row, 0 only generated, 0 only tracked)",
            "    fields that differ: weapon_skill (1)",
            "    weenie class 301: weapon_skill: generated 44, tracked 45",
        ], skills.Lines);
        var spells = BestiaryCheck.Compare(File("creatures.json"),
            Encoding.UTF8.GetBytes("{\"sources\":{},\"spells\":[[85,\"Flame Bolt VI\",16,84,84,1,0,0,-1,-1,-1]]}"), Encoding.UTF8.GetBytes("{\"sources\":{},\"spells\":[[85,\"Flame Bolt 6\",16,84,84,1,0,0,-1,-1,-1]]}"));
        Assert.Contains("    spell 85: name: generated \"Flame Bolt VI\", tracked \"Flame Bolt 6\"", spells.Lines);
    }

    [Fact]
    public void Every_column_names_its_source_once_a_property_by_one_name_and_one_number_and_the_reader_is_told_all_of_them()
    {
        var sets = BestiaryTables.Files.SelectMany(f => f.Members).Select(m => m.Rows).ToList();
        var columns = sets.SelectMany(s => s.Columns).ToList();

        Assert.All(sets, s => Assert.Equal(s.Columns.Count, s.Columns.Select(c => c.Name).Distinct().Count()));
        Assert.All(columns, c => Assert.Matches("^[a-z][a-z0-9_]*$", c.Name));

        // A property is named as ACE's enum names it, with its number there: one name a number and one number a name in each enum, so a typing slip in a new column shows before the run's own check against the ACE
        // build (BestiaryWorld.VerifyNames) does.
        var named = columns.Where(c => c.Enum != null).Select(c => (Enum: c.Enum!, c.Property, c.Id)).Concat(AceIds.Pinned.Select(p => (p.Enum, Property: p.Name, p.Id))).Distinct().ToList();
        Assert.All(named, n => Assert.True(n.Id > 0 && n.Property.Length > 0 && char.IsUpper(n.Property[0]), $"{n.Enum}.{n.Property} = {n.Id}"));
        Assert.Equal(named.Count, named.Select(n => (n.Enum, n.Property)).Distinct().Count());
        Assert.Equal(named.Count, named.Select(n => (n.Enum, n.Id)).Distinct().Count());
        Assert.All(columns.Where(c => c.Source == Source.Rule), c => Assert.NotNull(c.Rule));
        Assert.All(columns.Where(c => c.Source != Source.Rule), c => Assert.Null(c.Rule));

        // What the reader loads: every column's property and the three the picks read; nothing that is no property.
        var needs = Bestiary.Needs(BestiaryTables.Files);
        Assert.Equal(needs.Count, needs.Distinct().Count());
        Assert.All(columns.Where(c => c.Enum != null), c => Assert.Contains((c.Source, c.Id), needs));
        Assert.Contains((Source.Int, AceIds.Damage), needs);
        Assert.Contains((Source.Float, AceIds.DamageMod), needs);
        Assert.Contains((Source.Int, AceIds.CombatUse), needs);
        Assert.DoesNotContain(needs, n => n.Source is Source.Key or Source.WeenieType or Source.BodyPart or Source.SpellTable or Source.Rule);
        Assert.Equal(["Name", "EType", "BaseIntensity", "Variance", "NumProjectiles", "Boost", "BoostVariance", "DamageType", "Source", "TransferCap"], Bestiary.SpellNeeds(BestiaryTables.Files));
    }

    [Fact]
    public void A_column_s_line_states_its_table_its_property_by_number_and_name_and_its_default()
    {
        string Line(IReadOnlyList<Column> columns, string name) => columns.Single(c => c.Name == name).Describe();

        Assert.Equal("wcid: the row's id", Line(BestiaryTables.Creatures, "wcid"));
        Assert.Equal("level: weenie_properties_int 25 PropertyInt.Level; 0 when absent", Line(BestiaryTables.Creatures, "level"));
        Assert.Equal("health_ranks: weenie_properties_attribute_2nd 1 PropertyAttribute2nd.MaxHealth level_From_C_P; -1 when absent", Line(BestiaryTables.Creatures, "health_ranks"));
        Assert.Equal("endurance: weenie_properties_attribute 2 PropertyAttribute.Endurance init_Level + level_From_C_P; -1 when absent", Line(BestiaryTables.Creatures, "endurance"));
        Assert.Equal("defence_ranks: weenie_properties_skill 15 Skill.MagicDefense level_From_P_P; -1 when absent", Line(BestiaryTables.Creatures, "defence_ranks"));
        Assert.Equal("part_dvar: weenie_properties_body_part (the part that hits hardest) d_Var; -1 when absent", Line(BestiaryTables.Creatures, "part_dvar"));
        Assert.Equal("weapon_wcid: weenie.class_Id of the wielded weapon; -1 when absent", Line(BestiaryTables.Creatures, "weapon_wcid"));
        Assert.Equal("launcher_damage_mod: weenie_properties_float 63 PropertyFloat.DamageMod of the wielded launcher; -1 when absent", Line(BestiaryTables.Creatures, "launcher_damage_mod"));
        Assert.Equal("human_magic: weenie_properties_bool 7 PropertyBool.AiUseHumanMagicAnimations; 0 when absent", Line(BestiaryTables.Creatures, "human_magic"));
        Assert.Equal("wielded_treasure: weenie_properties_d_i_d 32 PropertyDataId.WieldedTreasureType; -1 when absent", Line(BestiaryTables.Creatures, "wielded_treasure"));
        Assert.Equal("shields: rule: how many wielded rows of its create list are a shield (PropertyInt.CombatUse is CombatUse.Shield)", Line(BestiaryTables.Creatures, "shields"));
        Assert.Equal("name: spell.Name; \"\" when absent", Line(BestiaryTables.Spells, "name"));
        Assert.Equal("fire: weenie_properties_float 67 PropertyFloat.ResistFire; 1 when absent", Line(BestiaryTables.Resists, "fire"));
        Assert.Equal("war_magic: weenie_properties_skill 34 Skill.WarMagic [skill, init_Level, level_From_P_P, s_a_c]", Line(BestiaryTables.Skills, "war_magic"));

        // M6 16 E1d: a behaviour column's default is ACE's own, and its line says where ACE's source states it
        Assert.Equal("visual_awareness: weenie_properties_float 31 PropertyFloat.VisualAwarenessRange; 18 when absent (VisualAwarenessRange_Default: Monster_Awareness.cs:377, read at 404)", Line(BestiaryTables.Behaviour, "visual_awareness"));
        Assert.Equal("aural_awareness: weenie_properties_float 32 PropertyFloat.AuralAwarenessRange; -1 when absent (ACE then hears as far as it sees: Monster_Awareness.cs:421)", Line(BestiaryTables.Behaviour, "aural_awareness"));
        Assert.Equal("uses_mana: weenie_properties_bool 6 PropertyBool.AiUsesMana; 1 when absent (Monster_Magic.cs:23)", Line(BestiaryTables.Behaviour, "uses_mana"));
        Assert.Equal("setup: weenie_properties_d_i_d 1 PropertyDataId.Setup; 0 when absent (WorldObject_Properties.cs:909)", Line(BestiaryTables.Behaviour, "setup"));
        Assert.Equal("mana_ranks: weenie_properties_attribute_2nd 5 PropertyAttribute2nd.MaxMana level_From_C_P; 0 when absent (Entity/CreatureVital.cs:29-33)", Line(BestiaryTables.Behaviour, "mana_ranks"));
        Assert.Equal("run_sac: weenie_properties_skill 24 Skill.Run s_a_c; 1 when absent (SkillAdvancementClass.Untrained: Creature_Skills.cs:31)", Line(BestiaryTables.Behaviour, "run_sac"));
        Assert.Equal("xp: weenie_properties_int 146 PropertyInt.XpOverride; 0 when absent (Creature_Death.cs:193)", Line(BestiaryTables.Behaviour, "xp"));
        Assert.Equal(
            "heartbeat_interval: weenie_properties_float 1 PropertyFloat.HeartbeatInterval; 5 when absent (WorldObject_Tick.cs:47-48; an interval of 0 or less never beats: 56-68)",
            Line(BestiaryTables.Behaviour, "heartbeat_interval"));
    }

    [Fact]
    public void A_behaviour_row_reads_each_column_at_its_source_and_a_creature_of_a_weenie_type_left_out_has_no_row()
    {
        var world = World(7, 8);
        world.WeenieTypes[20] = AceIds.Vendor; // a creature to ACE, and no row of any bestiary table (AceIds.LeftOut)
        world.Numbers[(Source.Int, 7, 2)] = 3;
        world.Numbers[(Source.DataId, 7, 1)] = 33556445;
        world.Floats[(7, 39)] = 0.95;
        world.Floats[(7, 31)] = 10;
        world.Floats[(7, 32)] = 16;
        world.Numbers[(Source.Int, 7, 67)] = 64;
        world.Numbers[(Source.Int, 7, 68)] = 5;
        world.Numbers[(Source.Int, 7, 72)] = 12;
        world.Numbers[(Source.Int, 7, 73)] = 20;
        world.Ranked[(Source.Vital, 7, 3)] = new Ranked(20, 2);
        world.Ranked[(Source.Vital, 7, 5)] = new Ranked(30, 4);
        world.Ranked[(Source.Vital, 7, 1)] = new Ranked(99, 9);   // MaxHealth is creatures.json's
        world.Floats[(7, 3)] = 0.067;
        world.Floats[(7, 4)] = 5;
        world.Floats[(7, 5)] = -1.5;
        world.Floats[(7, 1)] = 2.5;                                // PropertyFloat 1 is HeartbeatInterval
        world.Numbers[(Source.Bool, 7, 6)] = 0;
        world.Floats[(7, 72)] = 0.75;                              // PropertyFloat 72 is ResistStaminaDrain; PropertyInt 72 is FriendType
        world.Floats[(7, 74)] = 0.5;
        world.Ranked[(Source.Skill, 7, 24)] = new Ranked(40, 6, 3);
        world.Numbers[(Source.Int, 7, 146)] = 1000;
        world.Floats[(20, 31)] = 22;

        var rows = File("creature-behaviour.json").Members.Single(m => m.Key == "behaviour").Rows.Rows(world).Select(r => BestiaryWriter.Text(Cell.List(r)));

        // M6 item 16 E1e: the heartbeat's seconds after the three rates: weenie 7 states 2.5, weenie 8 none (ACE's 5)
        Assert.Equal(["[7,3,33556445,0.95,10,16,64,5,12,20,20,2,30,4,0.067,5,-1.5,2.5,0,0.75,0.5,40,6,3,1000]", "[8,-1,0,1,18,-1,0,0,-1,-1,0,0,0,0,0,0,0,5,1,1,1,0,0,1,0]"], rows);
    }

    /// <summary>
    /// M6 item 16 E1e: the tables' scope is one list. ACE builds six weenie types as a Creature or a subclass of it (WorldObjectFactory.cs:49-52, 57-58, 69-70, 127-130 @95ef426d); the tables take the Creature and the
    /// Cow, and the Vendor, the GamePiece, the Pet and the CombatPet are left out, each with why.
    /// </summary>
    [Fact]
    public void The_tables_take_the_creature_and_the_cow_and_leave_out_the_other_types_ACE_builds_as_a_creature_each_with_why()
    {
        Assert.Equal([AceIds.Creature, AceIds.Cow], AceIds.CreatureTypes);
        Assert.Equal([10, 15], AceIds.CreatureTypes);
        Assert.Equal([(12, "Vendor"), (61, "GamePiece"), (69, "Pet"), (71, "CombatPet")], AceIds.LeftOut.Select(l => (l.Type, l.Name)));
        Assert.All(AceIds.CreatureTypes, type => Assert.True(AceIds.IsCreature(type)));
        Assert.All(AceIds.LeftOut, l =>
        {
            Assert.False(AceIds.IsCreature(l.Type));
            Assert.True(l.Why.Length > 20, l.Name);
            Assert.Contains(("WeenieType", l.Name, l.Type), AceIds.Pinned); // each number is checked against the ACE build at every run
        });
        Assert.Contains(("WeenieType", "Cow", AceIds.Cow), AceIds.Pinned);

        var world = new WorldFacts();
        (uint Wcid, int Type)[] weenies = [(7, AceIds.Creature), (20, AceIds.Cow), (400, AceIds.Vendor), (14342, AceIds.GamePiece), (33918, AceIds.Pet), (48879, AceIds.CombatPet), (9, AceIds.MeleeWeapon)];
        foreach (var (wcid, type) in weenies)
        {
            world.WeenieTypes[wcid] = type;
            world.Floats[(wcid, 67)] = 0.5;                                   // each states a ResistFire
            world.Ranked[(Source.Skill, wcid, 34)] = new Ranked(100, 0, 3);   // and a War Magic row
        }

        Assert.Equal([7u, 20u], world.Creatures);
        foreach (var file in BestiaryTables.Files)
        {
            foreach (var member in file.Members.Where(m => m.Shape != Shape.Names && m.Rows is WeenieRows { Scope: Scope.Creatures } or SkillRows))
            {
                Assert.Equal([7L, 20L], member.Rows.Rows(world).Select(r => r[0].Whole));
            }
        }
    }

    [Fact]
    public void The_tool_writes_only_under_its_cache_folder_and_reads_the_world_without_a_save()
    {
        // The command's own text (it needs the ACE build to run, so it is read, not run): its one write is under cache/fight-sim/bestiary/ of the checkout, and no file of the tool saves to a database.
        var tool = RepositoryFiles.PathOf("tools", "fight-sim");
        var command = System.IO.File.ReadAllText(Path.Combine(tool, "BestiaryWorld.cs"));

        Assert.Contains("Path.Combine(checkout, \"cache\", \"fight-sim\", \"bestiary\")", command, StringComparison.Ordinal);
        Assert.Equal(1, command.Split("File.Write").Length - 1);
        Assert.Contains("File.WriteAllBytes(Path.Combine(folder, file.FileName), generated)", command, StringComparison.Ordinal);
        Assert.All(Directory.GetFiles(tool, "*.cs"), path =>
        {
            var code = string.Join('\n', System.IO.File.ReadLines(path).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            Assert.DoesNotContain("SaveChanges(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("ExecuteSql", code, StringComparison.Ordinal);
        });
    }
}

/// <summary>
/// The generator's parity, as a lead-run command (<c>[Trait("Tier", "Tool")]</c>: the default filters of <c>dotnet test</c>, slice-check and smoke leave it out). It runs
/// <c>pwsh -File tools\fight-sim.ps1 bestiary --check</c> in this checkout, which needs the ACE build and the local world database (read-only: ACE's own queries; it writes only under <c>cache\fight-sim\bestiary\</c>),
/// and proves that the tables written from the world database are the tracked ones (four with E1d's creature-behaviour.json):
/// <code>
/// dotnet test tests/Lifestone.Client.Tests --disable-build-servers --filter "FullyQualifiedName~BestiaryParityRunTests"
/// </code>
/// </summary>
public sealed class BestiaryParityRunTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    [Trait("Tier", "Tool")]
    public async Task The_generator_writes_the_tracked_tables_again_from_the_world_database()
    {
        var info = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RepositoryFiles.Root };
        foreach (var argument in new[] { "-NoProfile", "-File", RepositoryFiles.PathOf("tools", "fight-sim.ps1"), "bestiary", "--check" })
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var reading = process.StandardError.ReadToEndAsync();
        var report = await process.StandardOutput.ReadToEndAsync();
        var errors = await reading;
        await process.WaitForExitAsync();
        output.WriteLine(report);
        output.WriteLine(errors);

        Assert.True(process.ExitCode == 0, $"bestiary --check ended with {process.ExitCode}:\n{report}\n{errors}");
        Assert.All(BestiaryTables.Files, f => Assert.Contains(report.Split('\n'), l => l.StartsWith(f.FileName + ": identical", StringComparison.Ordinal)));
    }
}

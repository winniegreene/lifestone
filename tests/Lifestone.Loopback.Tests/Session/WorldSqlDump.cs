using System.Globalization;
using System.IO.Compression;
using System.Text;
using Lifestone.Client.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>One table of the world SQL dump as the reader keeps it: the columns in order and the rows it was asked to keep (numbers as long or double, text as string, NULL as null).</summary>
internal sealed class SqlTable(string name, string[] columns)
{
    private readonly Dictionary<string, int> _index = columns.Select((c, i) => (c, i)).ToDictionary(p => p.c, p => p.i, StringComparer.Ordinal);

    /// <summary>The table's name.</summary>
    public string Name => name;

    /// <summary>The columns, in the order of the CREATE TABLE statement.</summary>
    public string[] Columns => columns;

    /// <summary>The kept rows, each as wide as <see cref="Columns"/>.</summary>
    public List<object?[]> Rows { get; } = [];

    /// <summary>The position of <paramref name="column"/>, or -1.</summary>
    public int IndexOf(string column) => _index.GetValueOrDefault(column, -1);

    /// <summary>The kept rows as views.</summary>
    public IEnumerable<SqlRow> View() => Rows.Select(r => new SqlRow(this, r));
}

/// <summary>A view of one row by column name.</summary>
internal readonly struct SqlRow(SqlTable table, object?[] values)
{
    /// <summary>The raw values.</summary>
    public object?[] Values => values;

    /// <summary>The integer in <paramref name="column"/> (a double is truncated), or <paramref name="fallback"/> for NULL or a text value.</summary>
    public long L(string column, long fallback = 0) => At(column) switch { long l => l, double d => (long)d, _ => fallback };

    /// <summary>The number in <paramref name="column"/> as a double, or <paramref name="fallback"/>.</summary>
    public double F(string column, double fallback = 0) => At(column) switch { long l => l, double d => d, _ => fallback };

    /// <summary>The text in <paramref name="column"/>, or null.</summary>
    public string? S(string column) => At(column) as string;

    /// <summary>True when the value is NULL.</summary>
    public bool IsNull(string column) => At(column) is null;

    /// <summary>The raw value of <paramref name="column"/>.</summary>
    public object? Raw(string column) => At(column);

    private object? At(string column) => table.IndexOf(column) is var i and >= 0 ? values[i] : throw new KeyNotFoundException($"table {table.Name} has no column {column}");
}

/// <summary>
/// M5 item 0 (c4): a streaming reader of the pinned world SQL dump (<c>server/runtime/downloads/ace-world-v0.9.296/ACE-World-Database-v0.9.296.sql.zip</c>, the mysqldump of ACE's world database that the setup script loads).
/// READ-ONLY and OFFLINE: it parses the text of the dump, never loads it into a database and never connects to one, so it needs no backend. It keeps, per table, only the rows a caller's filter accepts (the dump is 155 MB).
/// </summary>
internal static class WorldSqlDump
{
    /// <summary>The pinned dump's path below a checkout.</summary>
    internal static readonly string[] ZipParts = ["server", "runtime", "downloads", "ace-world-v0.9.296", "ACE-World-Database-v0.9.296.sql.zip"];

    /// <summary>The dump's zip in the main checkout (a worktree has no <c>server/runtime</c>), or null when it is not there.</summary>
    internal static string? PinnedZip()
    {
        var path = Path.Combine([RepositoryFiles.MainCheckout, .. ZipParts]);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Reads the dump's one SQL entry from <paramref name="zipPath"/> and keeps the rows <paramref name="wanted"/> accepts.</summary>
    internal static Dictionary<string, SqlTable> ReadZip(string zipPath, IReadOnlyDictionary<string, Func<SqlRow, bool>> wanted)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"{zipPath} holds no .sql entry");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 20);
        return Read(reader, wanted);
    }

    /// <summary>Reads mysqldump text and keeps, for each table named in <paramref name="wanted"/>, the rows its filter accepts.</summary>
    internal static Dictionary<string, SqlTable> Read(TextReader reader, IReadOnlyDictionary<string, Func<SqlRow, bool>> wanted)
    {
        var tables = new Dictionary<string, SqlTable>(StringComparer.Ordinal);
        string? creating = null;
        List<string>? columns = null;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (creating is not null)
            {
                if (line.StartsWith("  `", StringComparison.Ordinal))
                {
                    columns!.Add(Between(line, 2, '`'));
                }
                else if (line.StartsWith(')'))
                {
                    tables[creating] = new SqlTable(creating, [.. columns!]);
                    creating = null;
                }

                continue;
            }

            if (line.StartsWith("CREATE TABLE `", StringComparison.Ordinal))
            {
                var name = Between(line, 13, '`');
                if (wanted.ContainsKey(name))
                {
                    creating = name;
                    columns = [];
                }
            }
            else if (line.StartsWith("INSERT INTO `", StringComparison.Ordinal))
            {
                var name = Between(line, 12, '`');
                if (tables.TryGetValue(name, out var table))
                {
                    ParseInsert(line, table, wanted[name]);
                }
            }
        }

        return tables;
    }

    /// <summary>The text between the backtick at <paramref name="open"/> and the next one.</summary>
    private static string Between(string line, int open, char close)
    {
        var start = line.IndexOf('`', open) + 1;
        return line[start..line.IndexOf(close, start)];
    }

    /// <summary>Parses one <c>INSERT INTO `t` [(`a`, `b`)] VALUES (...),(...);</c> line into <paramref name="table"/>.</summary>
    internal static void ParseInsert(string line, SqlTable table, Func<SqlRow, bool> keep)
    {
        var valuesAt = line.IndexOf(" VALUES ", StringComparison.Ordinal);
        if (valuesAt < 0)
        {
            throw new FormatException($"INSERT into {table.Name} has no VALUES");
        }

        int[]? map = null;
        var head = line.IndexOf('(');
        if (head >= 0 && head < valuesAt)
        {
            var names = new List<int>();
            var p = head;
            while (p < valuesAt)
            {
                var open = line.IndexOf('`', p);
                if (open < 0 || open >= valuesAt)
                {
                    break;
                }

                var close = line.IndexOf('`', open + 1);
                names.Add(table.IndexOf(line[(open + 1)..close]));
                p = close + 1;
            }

            map = [.. names];
        }

        var i = valuesAt + 8;
        var width = table.Columns.Length;
        var values = new List<object?>(width);
        while (i < line.Length)
        {
            var c = line[i];
            if (c == '(')
            {
                i++;
                values.Clear();
                while (true)
                {
                    values.Add(ParseValue(line, ref i));
                    if (line[i] == ',')
                    {
                        i++;
                        continue;
                    }

                    if (line[i] == ')')
                    {
                        i++;
                        break;
                    }

                    throw new FormatException($"table {table.Name}: unexpected '{line[i]}' at {i}");
                }

                var row = new object?[width];
                if (map is null)
                {
                    if (values.Count != width)
                    {
                        throw new FormatException($"table {table.Name}: a row has {values.Count} values, the table {width} columns");
                    }

                    for (var k = 0; k < width; k++)
                    {
                        row[k] = values[k];
                    }
                }
                else
                {
                    for (var k = 0; k < map.Length; k++)
                    {
                        if (map[k] >= 0)
                        {
                            row[map[k]] = values[k];
                        }
                    }
                }

                if (keep(new SqlRow(table, row)))
                {
                    table.Rows.Add(row);
                }
            }
            else if (c == ';')
            {
                break;
            }
            else
            {
                i++;
            }
        }
    }

    private static object? ParseValue(string line, ref int i)
    {
        var c = line[i];
        if (c == '\'')
        {
            var sb = new StringBuilder();
            i++;
            while (true)
            {
                var ch = line[i++];
                if (ch == '\\')
                {
                    var next = line[i++];
                    sb.Append(next switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '0' => '\0', 'Z' => '\u001a', _ => next });
                }
                else if (ch == '\'')
                {
                    if (i < line.Length && line[i] == '\'')
                    {
                        sb.Append('\'');
                        i++;
                    }
                    else
                    {
                        return sb.ToString();
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }
        }

        if (c == 'N')
        {
            i += 4;
            return null;
        }

        if (c == 'b' && line[i + 1] == '\'')
        {
            var close = line.IndexOf('\'', i + 2);
            var bits = line[(i + 2)..close];
            i = close + 1;
            return bits.Length == 0 ? 0L : Convert.ToInt64(bits, 2);
        }

        var start = i;
        while (i < line.Length && line[i] != ',' && line[i] != ')')
        {
            i++;
        }

        var text = line[start..i];
        if (text.Contains('.') || text.Contains('e') || text.Contains('E'))
        {
            return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        return long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }
}

/// <summary>A fact that needs the pinned world dump and the opt-in switch (<c>LIFESTONE_WORLD_SQL_FACTS=1</c>): skipped, with the reason, otherwise. Offline; no backend.</summary>
internal sealed class WorldSqlFactAttribute : FactAttribute
{
    public WorldSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LIFESTONE_WORLD_SQL_FACTS") != "1")
        {
            Skip = "set LIFESTONE_WORLD_SQL_FACTS=1 to parse the pinned world SQL dump (a 155 MB offline read, about a minute)";
        }
        else if (WorldSqlDump.PinnedZip() is null)
        {
            Skip = "the pinned world SQL dump is not under server/runtime/downloads of the main checkout";
        }
    }
}

/// <summary>The parser's own tests (a synthetic mysqldump excerpt: column lists, generated columns, escapes, NULL, bit values, floats).</summary>
public sealed class WorldSqlDumpTests
{
    private const string Excerpt = """
        CREATE TABLE `t` (
          `id` int(10) unsigned NOT NULL AUTO_INCREMENT,
          `landblock` int(5) GENERATED ALWAYS AS ((`obj_Cell_Id` >> 16)) VIRTUAL,
          `name` varchar(100) DEFAULT NULL,
          `x` float NOT NULL,
          `flag` bit(1) NOT NULL,
          PRIMARY KEY (`id`)
        ) ENGINE=InnoDB;
        INSERT INTO `t` (`id`, `name`, `x`, `flag`) VALUES (1,'Scrivener\'s ''pen''',-1.5,b'1'),(2,NULL,2e-3,b'0'),(3,'a,b)(c',4,b'0');
        INSERT INTO `other` VALUES (9,'skipped');
        """;

    [Fact]
    public void The_reader_parses_column_lists_escapes_nulls_bits_and_numbers_and_applies_the_filter()
    {
        var wanted = new Dictionary<string, Func<SqlRow, bool>> { ["t"] = r => r.L("id") != 3 };
        using var reader = new StringReader(Excerpt);
        var tables = WorldSqlDump.Read(reader, wanted);
        var table = Assert.Single(tables).Value;
        Assert.Equal(["id", "landblock", "name", "x", "flag"], table.Columns);
        Assert.Equal(2, table.Rows.Count);
        var first = table.View().First();
        Assert.Equal("Scrivener's 'pen'", first.S("name"));
        Assert.Equal(-1.5, first.F("x"));
        Assert.Equal(1, first.L("flag"));
        Assert.True(first.IsNull("landblock"));
        var second = table.View().Last();
        Assert.True(second.IsNull("name"));
        Assert.Equal(0.002, second.F("x"), 6);
    }

    [Fact]
    public void A_text_value_with_commas_and_brackets_is_one_value()
    {
        var wanted = new Dictionary<string, Func<SqlRow, bool>> { ["t"] = _ => true };
        using var reader = new StringReader(Excerpt);
        var table = Assert.Single(WorldSqlDump.Read(reader, wanted)).Value;
        Assert.Equal("a,b)(c", table.View().Last().S("name"));
        Assert.Equal(3, table.Rows.Count);
    }
}

/// <summary>
/// M5 item 0 (c4), reader <c>WorldSqlFacts</c>: parses the pinned world SQL dump offline and writes the den's, the tuskers', the two vendors' and the Scriveners' facts to
/// <c>cache/m5/0/world-sql-facts.md</c> of the running checkout (gitignored; derived from ACE's world data, not from the dats). About a minute; reads no database and sends nothing. Opt in with
/// <c>LIFESTONE_WORLD_SQL_FACTS=1</c>; <c>dotnet test tests/Lifestone.Loopback.Tests --filter "FullyQualifiedName~WorldSqlFactsRunTests"</c> (no backend needed, it is not a loopback test).
/// </summary>
public sealed class WorldSqlFactsRunTests(ITestOutputHelper output)
{
    /// <summary>PropertyInt keys (ACE.Entity/Enum/Properties/PropertyInt.cs @95ef426d): Level 25, Value 19, ItemType 1, Merchandise 74-76, MaxGeneratedObjects 81, InitGeneratedObjects 82, GeneratorType 100, GeneratorTimeType 142-144.</summary>
    private static readonly Dictionary<long, string> IntNames = new()
    {
        [1] = "ItemType", [19] = "Value", [25] = "Level", [74] = "MerchandiseItemTypes", [75] = "MerchandiseMinValue", [76] = "MerchandiseMaxValue", [77] = "DealMagicalItems",
        [81] = "MaxGeneratedObjects", [82] = "InitGeneratedObjects", [100] = "GeneratorType", [142] = "GeneratorTimeType", [143] = "GeneratorStartTime", [144] = "GeneratorEndTime",
    };

    /// <summary>PropertyFloat keys: BuyPrice 37, SellPrice 38, RegenerationInterval 41, RegenerationTimestamp 42.</summary>
    private static readonly Dictionary<long, string> FloatNames = new() { [37] = "BuyPrice", [38] = "SellPrice", [41] = "RegenerationInterval", [42] = "RegenerationTimestamp" };

    private const double SellRate = 1.35;

    [WorldSqlFact]
    public void The_pinned_dump_gives_the_den_the_tuskers_the_vendors_and_the_scriveners()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var markdown = Compose(WorldSqlDump.PinnedZip()!, output.WriteLine);
        var path = Path.Combine(RepositoryFiles.Root, "cache", "m5", "0", "world-sql-facts.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, markdown);
        output.WriteLine($"[measure] world-sql-facts written to cache/m5/0/world-sql-facts.md ({markdown.Length} characters)");
        Assert.Contains("## 1. The den's generators", markdown, StringComparison.Ordinal);
    }

    private static readonly uint[] Tuskers = [1628, 1629];
    private static readonly uint[] Vendors = [2304, 715];
    private static readonly uint[] DenLandblocks = [0x0105];

    internal static string Compose(string zipPath, Action<string> progress)
    {
        // Pass 1: names (every weenie's Name string), to find the Scriveners and the Lifestones.
        progress("pass 1: weenie names");
        var names = WorldSqlDump.ReadZip(zipPath, new Dictionary<string, Func<SqlRow, bool>> { ["weenie_properties_string"] = r => r.L("type") == 1 })["weenie_properties_string"];
        var nameOf = names.View().ToDictionary(r => (uint)r.L("object_Id"), r => r.S("value") ?? "?");
        string Name(long wcid) => nameOf.GetValueOrDefault((uint)wcid, "(no name)");
        var scrivener = nameOf.Where(p => IsScrivenerOfSchool(p.Value)).Select(p => p.Key).ToHashSet();
        var lifestones = nameOf.Where(p => p.Value is "Lifestone" or "Life Stone").Select(p => p.Key).ToHashSet();

        progress("pass 2: the tables");
        bool Den(SqlRow r) => DenLandblocks.Contains((uint)(r.L("obj_Cell_Id") >> 16));
        var wanted = new Dictionary<string, Func<SqlRow, bool>>
        {
            ["weenie"] = r => r.L("class_Id") is 3955 or 1628 or 1629 or 2304 or 715,
            ["weenie_properties_int"] = r => r.L("object_Id") is 3955 or 1628 or 1629 or 2304 or 715 || r.L("type") == 19,
            ["weenie_properties_float"] = r => r.L("object_Id") is 3955 or 1628 or 1629 or 2304 or 715,
            ["weenie_properties_d_i_d"] = r => r.L("object_Id") is 1628 or 1629,
            ["weenie_properties_generator"] = r => r.L("object_Id") == 3955,
            ["weenie_properties_create_list"] = r => r.L("object_Id") is 1628 or 1629 or 2304 or 715,
            ["treasure_death"] = _ => true,
            ["treasure_wielded"] = _ => true,
            ["landblock_instance"] = r => Den(r) || scrivener.Contains((uint)r.L("weenie_Class_Id")) || (lifestones.Contains((uint)r.L("weenie_Class_Id")) && r.L("obj_Cell_Id") >> 16 == 0xA9B4),
            ["landblock_instance_link"] = _ => true,
        };
        var t = WorldSqlDump.ReadZip(zipPath, wanted);

        var sb = new StringBuilder();
        sb.AppendLine("# World SQL facts (pinned dump ACE-World-Database-v0.9.296, parsed offline, read-only)");
        sb.AppendLine();
        sb.AppendLine("Derived from ACE's world database (not from the dats). Written by `WorldSqlFactsRunTests`. Keys: PropertyInt/Float/DataId of ACE @95ef426d.");

        // ---- 1. the den's generators
        sb.AppendLine().AppendLine("## 1. The den's generators (wcid 3955, landblock 0x0105)");
        sb.AppendLine($"weenie 3955: name `{Name(3955)}`, ints: {PropList(t["weenie_properties_int"], 3955, IntNames)}; floats: {PropList(t["weenie_properties_float"], 3955, FloatNames)}");
        sb.AppendLine().AppendLine("Generator profile rows of weenie 3955 (`weenie_properties_generator`): probability / wcid (name) / delay s / init / max / when / where");
        foreach (var r in t["weenie_properties_generator"].View())
        {
            sb.AppendLine($"- probability {r.F("probability"):0.###} | {r.L("weenie_Class_Id")} ({Name(r.L("weenie_Class_Id"))}) | delay {(r.IsNull("delay") ? "NULL" : r.F("delay").ToString("0.###", CultureInfo.InvariantCulture))} | init {r.L("init_Create")} | max {r.L("max_Create")} | when {r.L("when_Create")} | where {r.L("where_Create")}");
        }

        var instances = t["landblock_instance"].View().ToList();
        var byGuid = instances.ToDictionary(r => (uint)r.L("guid"));
        var links = t["landblock_instance_link"].View().Select(r => (Parent: (uint)r.L("parent_GUID"), Child: (uint)r.L("child_GUID"))).ToList();
        var generators = instances.Where(r => r.L("weenie_Class_Id") == 3955 && Den(r)).OrderBy(r => r.L("guid")).ToList();
        sb.AppendLine().AppendLine($"Instances of wcid 3955 in 0x0105: {generators.Count}.");
        foreach (var g in generators)
        {
            var guid = (uint)g.L("guid");
            var children = links.Where(l => l.Parent == guid).Select(l => l.Child).ToList();
            sb.AppendLine($"- generator 0x{guid:X8} cell 0x{g.L("obj_Cell_Id"):X8} at ({g.F("origin_X"):0.#}, {g.F("origin_Y"):0.#}, {g.F("origin_Z"):0.#}): {children.Count} `landblock_instance_link` rows (parent_GUID = this)");
            var known = children.Where(byGuid.ContainsKey).Select(c => byGuid[c]).ToList();
            sb.AppendLine($"  - children found in landblock_instance: {known.Count}; by wcid: {string.Join(", ", known.GroupBy(k => k.L("weenie_Class_Id")).OrderBy(x => x.Key).Select(x => $"{Name(x.Key)} {x.Key} x{x.Count()}"))}");
            sb.AppendLine($"  - by floor (origin_Z): {string.Join("; ", known.GroupBy(k => Math.Round(k.F("origin_Z"))).OrderByDescending(x => x.Key).Select(x => $"z {x.Key:0}: " + string.Join(", ", x.GroupBy(k => k.L("weenie_Class_Id")).OrderBy(w => w.Key).Select(w => $"{w.Key} x{w.Count()}"))))}");
            var arrivalFloor = known.Where(k => Math.Abs(k.F("origin_Z")) < 0.5 && k.L("weenie_Class_Id") is 1628 or 1629).ToList();
            sb.AppendLine($"  - tuskers on the arrival floor (z 0): {arrivalFloor.Count}, cells {string.Join(" ", arrivalFloor.Select(k => $"0x{k.L("obj_Cell_Id") & 0xFFFF:X4}").Distinct().OrderBy(x => x))}");
            sb.AppendLine($"  - link rows (first 3): {string.Join("; ", children.Take(3).Select(c => $"child 0x{c:X8}"))}");
        }

        // ---- 2. the tuskers
        sb.AppendLine().AppendLine("## 2. The tuskers (1628 Tusker Slave, 1629 Tusker Guard)");
        foreach (var wcid in Tuskers)
        {
            sb.AppendLine($"- {wcid} `{Name(wcid)}`: ints {PropList(t["weenie_properties_int"], wcid, IntNames)}");
            var did = t["weenie_properties_d_i_d"].View().Where(r => r.L("object_Id") == wcid).ToList();
            var death = did.Where(r => r.L("type") == 35).Select(r => r.L("value")).ToList();
            var wielded = did.Where(r => r.L("type") == 32).Select(r => r.L("value")).ToList();
            sb.AppendLine($"  - DeathTreasureType (DataId 35): {(death.Count == 0 ? "none" : string.Join(",", death))}; WieldedTreasureType (32): {(wielded.Count == 0 ? "none" : string.Join(",", wielded))}");
            foreach (var type in death)
            {
                foreach (var r in t["treasure_death"].View().Where(r => r.L("treasure_Type") == type))
                {
                    sb.AppendLine("  - treasure_death row: " + Row(t["treasure_death"], r, "last_Modified"));
                }
            }

            foreach (var type in wielded)
            {
                foreach (var r in t["treasure_wielded"].View().Where(r => r.L("treasure_Type") == type))
                {
                    sb.AppendLine("  - treasure_wielded row: " + Row(t["treasure_wielded"], r, "last_Modified"));
                }
            }

            var create = t["weenie_properties_create_list"].View().Where(r => r.L("object_Id") == wcid).ToList();
            sb.AppendLine($"  - create list (`weenie_properties_create_list`): {create.Count} rows");
            foreach (var r in create)
            {
                sb.AppendLine($"    - destination {r.L("destination_Type")} | {r.L("weenie_Class_Id")} ({Name(r.L("weenie_Class_Id"))}) | stack {r.L("stack_Size")} | palette {r.L("palette")} | shade {r.F("shade"):0.###} | bond {r.L("try_To_Bond")}");
            }
        }

        // ---- 3. the vendors
        sb.AppendLine().AppendLine($"## 3. The vendors' whole stock (price = max(1, ceil({SellRate} x Value - 0.1)) in float arithmetic, as VendorStock.PriceToBuy)");
        foreach (var wcid in Vendors)
        {
            sb.AppendLine().AppendLine($"### {wcid} {Name(wcid)}");
            sb.AppendLine($"- ints: {PropList(t["weenie_properties_int"], wcid, IntNames)}; floats: {PropList(t["weenie_properties_float"], wcid, FloatNames)}");
            var stock = t["weenie_properties_create_list"].View().Where(r => r.L("object_Id") == wcid).ToList();
            sb.AppendLine($"- create-list rows: {stock.Count} (destination 4 = Shop)");
            sb.AppendLine("| destination | wcid | name | Value | price at 1.35 | stack |");
            sb.AppendLine("|---|---|---|---|---|---|");
            var valueOf = t["weenie_properties_int"].View().Where(r => r.L("type") == 19).GroupBy(r => (uint)r.L("object_Id")).ToDictionary(g => g.Key, g => g.First().L("value"));
            foreach (var r in stock.OrderBy(r => r.L("destination_Type")).ThenBy(r => r.L("weenie_Class_Id")))
            {
                var item = (uint)r.L("weenie_Class_Id");
                var has = valueOf.TryGetValue(item, out var value);
                sb.AppendLine($"| {r.L("destination_Type")} | {item} | {Name(item)} | {(has ? value.ToString(CultureInfo.InvariantCulture) : "n/a")} | {(has ? Price(value).ToString(CultureInfo.InvariantCulture) : "n/a")} | {r.L("stack_Size")} |");
            }
        }

        // ---- 4. the Scriveners
        sb.AppendLine().AppendLine("## 4. The Scriveners of the four schools (distance on the ground plane, landblock grid 192 m, from Holtburg's lifestone)");
        var home = instances.Where(r => lifestones.Contains((uint)r.L("weenie_Class_Id")) && r.L("obj_Cell_Id") >> 16 == 0xA9B4).OrderBy(r => r.L("guid")).ToList();
        foreach (var h in home)
        {
            sb.AppendLine($"- Lifestone instance 0x{h.L("guid"):X8} in 0xA9B4: cell 0x{h.L("obj_Cell_Id"):X8} at ({h.F("origin_X"):0.#}, {h.F("origin_Y"):0.#}, {h.F("origin_Z"):0.#})");
        }

        if (home.Count == 0)
        {
            sb.AppendLine("- no Lifestone instance in 0xA9B4: distances not computed (TODO(m5-scrivener-distance))");
        }
        else
        {
            var origin = Global(home[0]);
            sb.AppendLine($"- distances measured from the first one (0x{home[0].L("guid"):X8})");
            foreach (var school in new[] { "War", "Creature", "Item", "Life" })
            {
                sb.AppendLine().AppendLine($"### {school} Magic");
                var rows = instances.Where(r => nameOf.TryGetValue((uint)r.L("weenie_Class_Id"), out var n) && IsScrivenerOfSchool(n) && SchoolOf(n).Contains(school, StringComparison.Ordinal))
                    .Select(r => (Row: r, Distance: Distance(origin, Global(r)))).OrderBy(x => x.Distance).ToList();
                foreach (var (r, distance) in rows.Take(8))
                {
                    var lb = r.L("obj_Cell_Id") >> 16;
                    sb.AppendLine($"- {Name(r.L("weenie_Class_Id"))} ({r.L("weenie_Class_Id")}): landblock 0x{lb:X4} cell 0x{r.L("obj_Cell_Id"):X8} at ({r.F("origin_X"):0.#}, {r.F("origin_Y"):0.#}, {r.F("origin_Z"):0.#}), {distance:0} m from the lifestone");
                }

                if (rows.Count == 0)
                {
                    sb.AppendLine("- none found");
                }
            }

            var warHere = instances.Where(r => nameOf.TryGetValue((uint)r.L("weenie_Class_Id"), out var n) && n.EndsWith("Scrivener of War Magic", StringComparison.Ordinal) && r.L("obj_Cell_Id") >> 16 == 0xA9B3).ToList();
            sb.AppendLine().AppendLine($"Scriveners of War Magic in landblock 0xA9B3: {warHere.Count}: {string.Join("; ", warHere.Select(r => $"{Name(r.L("weenie_Class_Id"))} cell 0x{r.L("obj_Cell_Id"):X8} ({r.F("origin_X"):0.#}, {r.F("origin_Y"):0.#}, {r.F("origin_Z"):0.#}), {Distance(origin, Global(r)):0} m"))}");
        }

        return sb.ToString();
    }

    /// <summary>The names the reader treats as a Scrivener of one school (any rank): "Scrivener of War Magic" and its Apprentice, Journeyman, Master and Grand Master forms, and the combined Item and War one.</summary>
    internal static bool IsScrivenerOfSchool(string name) => name.Contains("Scrivener of ", StringComparison.Ordinal) && name.EndsWith("Magic", StringComparison.Ordinal) && !name.StartsWith("Reflection", StringComparison.Ordinal);

    private static string SchoolOf(string name) => name[(name.IndexOf("Scrivener of ", StringComparison.Ordinal) + 13)..];

    private static (double X, double Y) Global(SqlRow r)
    {
        var cell = r.L("obj_Cell_Id");
        return ((cell >> 24 & 0xFF) * 192.0 + r.F("origin_X"), (cell >> 16 & 0xFF) * 192.0 + r.F("origin_Y"));
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    /// <summary>VendorStock.PriceToBuy's arithmetic: per created stack max(1, ceil(rate x value - 0.1)) in single precision.</summary>
    internal static int Price(long value) => Math.Max(1, (int)Math.Ceiling((float)SellRate * (float)value - 0.1f));

    private static string PropList(SqlTable table, long wcid, Dictionary<long, string> names)
    {
        var keys = table.View().Where(r => r.L("object_Id") == wcid).OrderBy(r => r.L("type"))
            .Select(r => $"{names.GetValueOrDefault(r.L("type"), "#" + r.L("type"))}={r.Raw("value")}");
        var text = string.Join(", ", keys);
        return text.Length == 0 ? "(none kept)" : text;
    }

    private static string Row(SqlTable table, SqlRow row, params string[] skip) =>
        string.Join(", ", table.Columns.Where(c => !skip.Contains(c)).Select(c => $"{c}={row.Values[table.IndexOf(c)] ?? "NULL"}"));
}

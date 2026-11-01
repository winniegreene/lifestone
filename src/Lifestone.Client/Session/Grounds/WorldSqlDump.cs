// M5 item 2 g2: moved from tests/Lifestone.Loopback.Tests/Session/WorldSqlDump.cs (M5 item 0, c4) so GroundDraft can read the pinned world SQL; the parser is unchanged.
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Lifestone.Client.Session.Grounds;

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
/// M5 item 0 (c4), moved into the client by M5 item 2 g2 for <see cref="GroundDraft"/>: a streaming reader of the pinned world SQL dump (<c>server/runtime/downloads/ace-world-v0.9.296/ACE-World-Database-v0.9.296.sql.zip</c>, the mysqldump of ACE's world database that the setup script loads).
/// READ-ONLY and OFFLINE: it parses the text of the dump, never loads it into a database and never connects to one, so it needs no backend. It keeps, per table, only the rows a caller's filter accepts (the dump is 155 MB).
/// </summary>
internal static class WorldSqlDump
{
    /// <summary>The pinned dump's path below a checkout (the main checkout: a worktree has no <c>server/runtime</c>); the host opens it (the client does no file I/O).</summary>
    internal static readonly string[] ZipParts = ["server", "runtime", "downloads", "ace-world-v0.9.296", "ACE-World-Database-v0.9.296.sql.zip"];

    /// <summary>Reads the dump's one SQL entry from the zip in <paramref name="zipStream"/> (left open; the host opened it) and keeps the rows <paramref name="wanted"/> accepts.</summary>
    internal static Dictionary<string, SqlTable> ReadZip(Stream zipStream, IReadOnlyDictionary<string, Func<SqlRow, bool>> wanted)
    {
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("the world SQL zip holds no .sql entry");
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

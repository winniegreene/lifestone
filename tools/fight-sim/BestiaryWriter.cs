// The bestiary's writer (BestiaryModel.cs): a table file's text in the layout the tracked file has, byte for byte. No ACE type; also compiled into Lifestone.Client.Tests.

using System.Globalization;
using System.Text;

namespace Lifestone.FightSim;

/// <summary>Writes a table file's text.</summary>
internal static class BestiaryWriter
{
    /// <summary>The file's text from the world's rows.</summary>
    /// <param name="file">The table file.</param>
    /// <param name="world">The world database's rows.</param>
    /// <param name="sources">The <c>sources</c> header: a JSON object's text (<see cref="TableFile.StatedSources"/>, else <see cref="SourcesOf"/> the tracked table).</param>
    /// <param name="counts">Receives how many rows each row member has, by the member's name.</param>
    public static string Write(TableFile file, WorldFacts world, string sources, IDictionary<string, int>? counts = null) =>
        Write(file, sources, [.. file.Members.Select(m => m.Shape == Shape.Names ? [] : (IReadOnlyList<Cell[]>)[.. m.Rows.Rows(world)])], counts);

    /// <summary>The file's text from rows already built: one list a member, in the members' order (a <see cref="Shape.Names"/> member's list is not read).</summary>
    public static string Write(TableFile file, string sources, IReadOnlyList<IReadOnlyList<Cell[]>> rows, IDictionary<string, int>? counts = null)
    {
        if (rows.Count != file.Members.Count)
        {
            throw new ArgumentException($"{file.FileName} has {file.Members.Count} members and was given the rows of {rows.Count}");
        }

        // Every line ends with a line feed alone, as the repository stores its text.
        var text = new StringBuilder();
        text.Append("{\"sources\":").Append(sources);
        for (var i = 0; i < file.Members.Count; i++)
        {
            var member = file.Members[i];
            text.Append(',');
            if (file.MemberLines)
            {
                text.Append('\n');
            }

            text.Append('"').Append(member.Key).Append("\":[");
            if (member.Shape == Shape.Names)
            {
                text.AppendJoin(',', member.Rows.Columns.Select(c => Text(c.Name))).Append(']');
                continue;
            }

            var lines = member.Shape != Shape.Inline;
            for (var r = 0; r < rows[i].Count; r++)
            {
                text.Append(r == 0 ? string.Empty : ",").Append(lines ? "\n" : string.Empty);
                AppendRow(text, rows[i][r]);
            }

            text.Append(member.Shape == Shape.Block || (lines && rows[i].Count == 0) ? "\n]" : "]");
            if (counts != null)
            {
                counts[member.Key] = rows[i].Count;
            }
        }

        return text.Append("}\n").ToString();
    }

    /// <summary>A cell's text.</summary>
    public static string Text(Cell cell)
    {
        var text = new StringBuilder();
        Append(text, cell);
        return text.ToString();
    }

    /// <summary>A <c>sources</c> header stated in code (<see cref="TableFile.StatedSources"/>): a JSON object of texts in the tracked tables' own spacing, <c>{"key": "text", "key": "text"}</c>, in plain ASCII.</summary>
    public static string Sources(params (string Key, string Text)[] members) => "{" + string.Join(", ", members.Select(m => Text(m.Key) + ": " + Text(m.Text))) + "}";

    /// <summary>
    /// The <c>sources</c> header of a table file's text, exactly as it stands there: the JSON object after the opening <c>{"sources":</c>, found by its braces (a brace inside a string does not count).
    /// </summary>
    public static string SourcesOf(string tableText)
    {
        const string Opening = "{\"sources\":";
        if (!tableText.StartsWith(Opening + "{", StringComparison.Ordinal))
        {
            throw new ArgumentException($"the table does not open with {Opening}{{ and an object: its sources header cannot be carried");
        }

        var (depth, inString) = (0, false);
        for (var i = Opening.Length; i < tableText.Length; i++)
        {
            var c = tableText[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }
            }
            else if (c == '"')
            {
                inString = true;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                return tableText[Opening.Length..(i + 1)];
            }
        }

        throw new ArgumentException("the table's sources header never closes");
    }

    private static void AppendRow(StringBuilder text, Cell[] row)
    {
        text.Append('[');
        for (var i = 0; i < row.Length; i++)
        {
            if (i > 0)
            {
                text.Append(',');
            }

            Append(text, row[i]);
        }

        text.Append(']');
    }

    private static void Append(StringBuilder text, Cell cell)
    {
        switch (cell.Kind)
        {
            case CellKind.Whole:
                text.Append(cell.Whole.ToString(CultureInfo.InvariantCulture));
                break;
            case CellKind.Double:
                text.Append(Finite(cell.Real).ToString("R", CultureInfo.InvariantCulture));
                break;
            case CellKind.Single:
                text.Append(((float)Finite(cell.Real)).ToString("R", CultureInfo.InvariantCulture));
                break;
            case CellKind.Text:
                AppendText(text, cell.Text ?? string.Empty);
                break;
            case CellKind.List:
                text.Append('[');
                for (var i = 0; i < cell.Items!.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Append(',');
                    }

                    Append(text, cell.Items[i]);
                }

                text.Append(']');
                break;
            default:
                throw new InvalidOperationException($"a cell of kind {cell.Kind} has no text");
        }
    }

    private static double Finite(double value) => double.IsFinite(value) ? value : throw new InvalidOperationException($"the world database holds {value.ToString(CultureInfo.InvariantCulture)}, which JSON cannot hold");

    /// <summary>A JSON string in plain ASCII: the quote and the backslash escaped, a control character and anything past ASCII as <c>\uXXXX</c> (the tracked tables are ASCII).</summary>
    private static void AppendText(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (var c in value)
        {
            if (c is '"' or '\\')
            {
                text.Append('\\').Append(c);
            }
            else if (c is < ' ' or > '~')
            {
                text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                text.Append(c);
            }
        }

        text.Append('"');
    }
}

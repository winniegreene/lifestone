// The bestiary's check (BestiaryModel.cs): a generated table against the tracked one. No ACE type; also compiled into Lifestone.Client.Tests.

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lifestone.FightSim;

/// <summary>What <see cref="BestiaryCheck.Compare"/> found.</summary>
/// <param name="Same">Whether the two hold the same table: identical bytes, or equal as JSON.</param>
/// <param name="Lines">The report: its first line is the verdict.</param>
internal sealed record CheckResult(bool Same, IReadOnlyList<string> Lines);

/// <summary>Compares a generated table file with the tracked one.</summary>
internal static class BestiaryCheck
{
    /// <summary>How many differing rows a member's report shows with both values.</summary>
    public const int Shown = 10;

    /// <summary>
    /// The verdict: <c>identical</c> byte for byte; else <c>identical as JSON</c> with where the bytes first differ; else, a member, how many rows differ (by the row's first cell: the weenie class, the spell), the
    /// fields that differ and the first ten with both values.
    /// </summary>
    public static CheckResult Compare(TableFile file, byte[] generated, byte[] tracked)
    {
        if (generated.AsSpan().SequenceEqual(tracked))
        {
            return new CheckResult(true, [string.Create(CultureInfo.InvariantCulture, $"{file.FileName}: identical (byte for byte, {tracked.Length} bytes)")]);
        }

        JsonDocument made;
        JsonDocument kept;
        try
        {
            made = JsonDocument.Parse(generated);
        }
        catch (JsonException e)
        {
            return new CheckResult(false, [$"{file.FileName}: DIFFERENT: the generated table is not JSON ({e.Message})"]);
        }

        using var ownsMade = made;
        try
        {
            kept = JsonDocument.Parse(tracked);
        }
        catch (JsonException e)
        {
            return new CheckResult(false, [$"{file.FileName}: DIFFERENT: the tracked table is not JSON ({e.Message})"]);
        }

        using var ownsKept = kept;
        if (Equal(made.RootElement, kept.RootElement))
        {
            return new CheckResult(true, [$"{file.FileName}: identical as JSON; the bytes differ: {FirstByteDifference(generated, tracked)}"]);
        }

        var lines = new List<string>();
        if (made.RootElement.ValueKind != JsonValueKind.Object || kept.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new CheckResult(false, [$"{file.FileName}: DIFFERENT: a table is a JSON object; the generated one is {made.RootElement.ValueKind}, the tracked one {kept.RootElement.ValueKind}"]);
        }

        var keys = made.RootElement.EnumerateObject().Select(p => p.Name).Concat(kept.RootElement.EnumerateObject().Select(p => p.Name)).Distinct(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var hasMade = made.RootElement.TryGetProperty(key, out var a);
            var hasKept = kept.RootElement.TryGetProperty(key, out var b);
            if (!hasMade || !hasKept)
            {
                lines.Add($"  {key}: only in the {(hasMade ? "generated" : "tracked")} table");
                continue;
            }

            if (Equal(a, b))
            {
                continue;
            }

            var member = file.Members.FirstOrDefault(m => m.Key == key);
            if (member is { Shape: not Shape.Names } && IsRows(a) && IsRows(b))
            {
                CompareRows(member, a, b, lines);
            }
            else if (a.ValueKind == JsonValueKind.Object && b.ValueKind == JsonValueKind.Object)
            {
                var names = a.EnumerateObject().Select(p => p.Name).Concat(b.EnumerateObject().Select(p => p.Name)).Distinct(StringComparer.Ordinal)
                    .Where(n => !a.TryGetProperty(n, out var x) || !b.TryGetProperty(n, out var y) || !Equal(x, y));
                lines.Add($"  {key}: differs in {string.Join(", ", names)}");
            }
            else
            {
                lines.Add($"  {key}: generated {Clip(a.GetRawText())}, tracked {Clip(b.GetRawText())}");
            }
        }

        lines.Insert(0, $"{file.FileName}: DIFFERENT");
        return new CheckResult(false, lines);
    }

    private static void CompareRows(Member member, JsonElement generated, JsonElement tracked, List<string> lines)
    {
        var name = member.Rows.KeyName;
        var made = Keyed(generated, out var madeRepeats);
        var kept = Keyed(tracked, out var keptRepeats);
        var onlyMade = made.Keys.Where(k => !kept.ContainsKey(k)).ToList();
        var onlyKept = kept.Keys.Where(k => !made.ContainsKey(k)).ToList();
        var fields = new Dictionary<string, int>(StringComparer.Ordinal);
        var shown = new List<string>();
        var changed = 0;
        foreach (var (key, a) in made)
        {
            if (!kept.TryGetValue(key, out var b) || Equal(a, b))
            {
                continue;
            }

            changed++;
            var (lengthA, lengthB) = (a.GetArrayLength(), b.GetArrayLength());
            var named = member.Rows.Fixed && lengthA == member.Rows.Columns.Count && lengthB == lengthA;
            var differing = new List<string>();
            if (lengthA != lengthB)
            {
                fields["cells in the row"] = fields.GetValueOrDefault("cells in the row") + 1;
                differing.Add(string.Create(CultureInfo.InvariantCulture, $"cells in the row: generated {lengthA}, tracked {lengthB}"));
            }

            for (var i = 0; i < Math.Min(lengthA, lengthB); i++)
            {
                if (!Equal(a[i], b[i]))
                {
                    var field = named ? member.Rows.Columns[i].Name : string.Create(CultureInfo.InvariantCulture, $"cell {i}");
                    fields[field] = fields.GetValueOrDefault(field) + 1;
                    differing.Add($"{field}: generated {Clip(a[i].GetRawText())}, tracked {Clip(b[i].GetRawText())}");
                }
            }

            if (shown.Count < Shown)
            {
                shown.Add($"    {name} {key}: {string.Join("; ", differing)}");
            }
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"  {member.Key}: {onlyMade.Count + onlyKept.Count + changed} {Plural(name)} differ of {made.Count} generated and {kept.Count} tracked ({changed} with a different row, {onlyMade.Count} only generated, {onlyKept.Count} only tracked)"));
        if (madeRepeats + keptRepeats > 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"    rows that repeat an earlier row's {name}, not compared: {madeRepeats} generated, {keptRepeats} tracked"));
        }

        if (fields.Count > 0)
        {
            lines.Add("    fields that differ: " + string.Join(", ", fields.OrderByDescending(f => f.Value).ThenBy(f => f.Key, StringComparer.Ordinal).Select(f => string.Create(CultureInfo.InvariantCulture, $"{f.Key} ({f.Value})"))));
        }

        if (onlyMade.Count > 0)
        {
            lines.Add($"    only generated, the first {Math.Min(Shown, onlyMade.Count)}: {string.Join(", ", onlyMade.Take(Shown))}");
        }

        if (onlyKept.Count > 0)
        {
            lines.Add($"    only tracked, the first {Math.Min(Shown, onlyKept.Count)}: {string.Join(", ", onlyKept.Take(Shown))}");
        }

        lines.AddRange(shown);
    }

    /// <summary>The rows by their first cell's text, in the table's order; a row that repeats an earlier key is counted and left out.</summary>
    private static Dictionary<string, JsonElement> Keyed(JsonElement rows, out int repeats)
    {
        repeats = 0;
        var keyed = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var row in rows.EnumerateArray())
        {
            if (!keyed.TryAdd(row[0].GetRawText(), row))
            {
                repeats++;
            }
        }

        return keyed;
    }

    private static bool IsRows(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array && element.EnumerateArray().All(r => r.ValueKind == JsonValueKind.Array && r.GetArrayLength() > 0);

    /// <summary>Whether two JSON values are equal: numbers by value (1 and 1.0 are equal), strings by their text, arrays in order, objects by member whatever the order.</summary>
    private static bool Equal(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Number:
                return a.GetRawText() == b.GetRawText() || (a.TryGetDecimal(out var x) && b.TryGetDecimal(out var y) ? x == y : a.GetDouble().Equals(b.GetDouble()));
            case JsonValueKind.String:
                return a.GetString() == b.GetString();
            case JsonValueKind.Array:
                if (a.GetArrayLength() != b.GetArrayLength())
                {
                    return false;
                }

                using (var left = a.EnumerateArray())
                using (var right = b.EnumerateArray())
                {
                    while (left.MoveNext() && right.MoveNext())
                    {
                        if (!Equal(left.Current, right.Current))
                        {
                            return false;
                        }
                    }
                }

                return true;
            case JsonValueKind.Object:
                var count = 0;
                foreach (var property in a.EnumerateObject())
                {
                    count++;
                    if (!b.TryGetProperty(property.Name, out var other) || !Equal(property.Value, other))
                    {
                        return false;
                    }
                }

                return count == b.EnumerateObject().Count();
            default:
                return true;
        }
    }

    private static string FirstByteDifference(byte[] generated, byte[] tracked)
    {
        var at = generated.AsSpan().CommonPrefixLength(tracked);
        var line = 1 + tracked.AsSpan(0, Math.Min(at, tracked.Length)).Count((byte)'\n');
        return string.Create(CultureInfo.InvariantCulture,
            $"first at byte {at} (line {line}), generated {Near(generated, at)}, tracked {Near(tracked, at)}; {generated.Length} bytes generated, {tracked.Length} tracked");
    }

    private static string Near(byte[] bytes, int at) =>
        at >= bytes.Length ? "(the end)" : "'" + Encoding.UTF8.GetString(bytes, at, Math.Min(24, bytes.Length - at)).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal) + "'";

    private static string Clip(string text) => text.Length <= 80 ? text : text[..77] + "...";

    private static string Plural(string name) => name.EndsWith("ss", StringComparison.Ordinal) ? name + "es" : name + "s";
}

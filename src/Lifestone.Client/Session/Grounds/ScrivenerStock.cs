using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;

namespace Lifestone.Client.Session.Grounds;

/// <summary>One scroll a catalog scrivener sells (<see cref="ScrivenerStock"/>): the spell, its name, its line and level (read from the name), the scroll's weenie, Value and burden, and the create-list rows that sell it.</summary>
/// <param name="SpellId">The spell id (the scroll's SpellDID).</param>
/// <param name="Name">The spell's name (the world's spell row).</param>
/// <param name="Line">The name less its numeral ("Frost Bolt"); the whole name for a spell with no numeral ("Lifestone Recall").</param>
/// <param name="Level">1 to 6 by the numeral, 0 with none.</param>
/// <param name="ScrollWcid">The scroll's weenie class id.</param>
/// <param name="ScrollValue">The scroll's Value (int 19); the price is a scrivener's <see cref="Scrivener.PriceOf"/> of it.</param>
/// <param name="Burden">The scroll's burden (int 5).</param>
/// <param name="Sellers">The create-list rows of the catalog's scriveners who sell it.</param>
public sealed record ScrivenerStockSpell(uint SpellId, string Name, string Line, int Level, uint ScrollWcid, int ScrollValue, int Burden, IReadOnlyList<ScrivenerSale> Sellers);

/// <summary>
/// M6 14 (the every-spell rule; the owner, 2026-11-16: she buys every spell, eventually, when affordable): every scroll the scrivener catalog's scriveners sell (<c>Grounds/scrivener-stock.json</c>, the world copy's create lists; the file's
/// <c>source</c> holds the SELECT). The catalog (<see cref="ScrivenerCatalog"/>) keeps the lines the spell plan reasons about with their pinned powers; this is the whole shop, so a ground's vendor view sells what the scrivener sells
/// (<see cref="ScrivenerCatalog.VendorsIn"/>) and the every-spell rule (<c>Mage.EverySpellPlan</c>) can learn any line. School and power are not here: they are the SpellTable's, read where the rule runs.
/// </summary>
public sealed class ScrivenerStock
{
    /// <summary>The embedded file's logical name (Lifestone.Client.csproj).</summary>
    internal const string ResourceName = "Lifestone.Client.Session.Grounds.scrivener-stock.json";

    private static readonly string[] Numerals = ["I", "II", "III", "IV", "V", "VI"];

    private static readonly Lazy<ScrivenerStock> Loaded = new(() =>
    {
        using var stream = typeof(ScrivenerStock).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"the scrivener stock is not embedded ({ResourceName}; Lifestone.Client.csproj)");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    });

    private readonly FrozenDictionary<uint, ScrivenerStockSpell> _byId;
    private readonly FrozenDictionary<uint, ScrivenerStockSpell> _byScroll;
    private readonly FrozenDictionary<string, IReadOnlyList<ScrivenerStockSpell>> _lines;

    private ScrivenerStock(IReadOnlyList<ScrivenerStockSpell> spells, string source)
    {
        Spells = spells;
        Source = source;
        _byId = spells.ToFrozenDictionary(s => s.SpellId);
        _byScroll = spells.GroupBy(s => s.ScrollWcid).ToFrozenDictionary(g => g.Key, g => g.First());
        LineNames = [.. spells.Select(s => s.Line).Distinct(StringComparer.Ordinal)];
        _lines = spells.GroupBy(s => s.Line, StringComparer.Ordinal).ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<ScrivenerStockSpell>)[.. g.OrderBy(s => s.Level)], StringComparer.Ordinal);
    }

    /// <summary>The embedded stock.</summary>
    public static ScrivenerStock Default => Loaded.Value;

    /// <summary>An empty stock (a test's catalog).</summary>
    public static ScrivenerStock Empty { get; } = new([], "none");

    /// <summary>A stock of <paramref name="spells"/> (a test's shop).</summary>
    /// <param name="spells">The spells.</param>
    /// <param name="source">Where they come from.</param>
    internal static ScrivenerStock Of(IEnumerable<ScrivenerStockSpell> spells, string source = "test") => new([.. spells], source);

    /// <summary>The spells, in the file's order (by spell id).</summary>
    public IReadOnlyList<ScrivenerStockSpell> Spells { get; }

    /// <summary>The line names, in the order of their first spell.</summary>
    public IReadOnlyList<string> LineNames { get; }

    /// <summary>Where the file comes from (its <c>source</c>).</summary>
    public string Source { get; }

    /// <summary>The spell of <paramref name="spellId"/>, or null.</summary>
    /// <param name="spellId">The spell id.</param>
    public ScrivenerStockSpell? Spell(uint spellId) => _byId.GetValueOrDefault(spellId);

    /// <summary>The spell whose scroll is <paramref name="scrollWcid"/>, or null.</summary>
    /// <param name="scrollWcid">The scroll's weenie class id.</param>
    public ScrivenerStockSpell? ByScroll(uint scrollWcid) => _byScroll.GetValueOrDefault(scrollWcid);

    /// <summary>The spells of the line <paramref name="line"/>, by level (I first), or empty.</summary>
    /// <param name="line">The line's name.</param>
    public IReadOnlyList<ScrivenerStockSpell> LineSpells(string line) => _lines.TryGetValue(line, out var spells) ? spells : [];

    /// <summary>The spells <paramref name="scrivenerWcid"/> sells, in the file's order.</summary>
    /// <param name="scrivenerWcid">The scrivener's weenie class id.</param>
    public IEnumerable<ScrivenerStockSpell> SoldBy(uint scrivenerWcid) => Spells.Where(s => s.Sellers.Any(r => r.VendorWcid == scrivenerWcid));

    /// <summary>A name's line and level: the name less a trailing numeral I to VI, and the numeral's number; the whole name and 0 with none.</summary>
    /// <param name="name">The spell's name.</param>
    public static (string Line, int Level) LineOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var space = name.LastIndexOf(' ');
        if (space > 0)
        {
            var tail = name.AsSpan(space + 1);
            for (var i = 0; i < Numerals.Length; i++)
            {
                if (tail.Equals(Numerals[i], StringComparison.Ordinal))
                {
                    return (name[..space], i + 1);
                }
            }
        }

        return (name, 0);
    }

    /// <summary>Reads a stock file; throws <see cref="ScrivenerCatalogException"/> on any error (the catalog's error, with the path).</summary>
    /// <param name="json">The file's text.</param>
    internal static ScrivenerStock Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException e)
        {
            throw new ScrivenerCatalogException("stock $", "is not JSON: " + e.Message);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var source = root.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String && s.GetString() is { Length: > 0 } text ? text : throw new ScrivenerCatalogException("stock $.source", "is missing (D28: every number has its row)");
            if (!root.TryGetProperty("spells", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                throw new ScrivenerCatalogException("stock $.spells", "is not an array");
            }

            var spells = new List<ScrivenerStockSpell>();
            var seen = new HashSet<uint>();
            var i = 0;
            foreach (var e in list.EnumerateArray())
            {
                var path = string.Create(CultureInfo.InvariantCulture, $"stock $.spells[{i++}]");
                uint U(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetUInt32(out var n) ? n : throw new ScrivenerCatalogException(path + "." + name, "is not a whole number from 0");
                var name = e.TryGetProperty("name", out var nv) && nv.GetString() is { Length: > 0 } nm ? nm : throw new ScrivenerCatalogException(path + ".name", "is missing");
                var sellers = new List<ScrivenerSale>();
                if (!e.TryGetProperty("sellers", out var sv) || sv.ValueKind != JsonValueKind.Array)
                {
                    throw new ScrivenerCatalogException(path + ".sellers", "is not an array");
                }

                foreach (var row in sv.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != 2 || !row[0].TryGetUInt32(out var vendor) || !row[1].TryGetInt32(out var createList) || createList <= 0)
                    {
                        throw new ScrivenerCatalogException(path + ".sellers", "a row is not [scrivener wcid, create-list id]");
                    }

                    sellers.Add(new ScrivenerSale(vendor, createList));
                }

                if (sellers.Count == 0)
                {
                    throw new ScrivenerCatalogException(path + ".sellers", "is empty (D28: a row cites its create-list id)");
                }

                var id = U("spell");
                if (!seen.Add(id))
                {
                    throw new ScrivenerCatalogException(path + ".spell", $"{id} is listed twice");
                }

                var (line, level) = LineOf(name);
                spells.Add(new ScrivenerStockSpell(id, name, line, level, U("scroll"), (int)U("value"), (int)U("burden"), sellers));
            }

            return new ScrivenerStock(spells, source);
        }
    }
}

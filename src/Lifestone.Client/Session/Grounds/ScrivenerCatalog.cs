using System.Collections.Frozen;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using DatReaderWriter.Enums;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Grounds;

/// <summary>What a scrivener line is: a war bolt, a debuff the fight casts, or a travel spell.</summary>
public enum ScrivenerLineKind
{
    /// <summary>A war bolt line of one element (I to VI).</summary>
    Bolt,

    /// <summary>A debuff line (an elemental vulnerability, Imperil Other, Vulnerability Other or Magic Yield Other; I to VI).</summary>
    Debuff,

    /// <summary>A travel spell (one spell, no level).</summary>
    Travel,

    /// <summary>M6 7 X (spells are the armor): the body armor line (Armor Self, I to VI): it cuts every physical hit.</summary>
    Armor,

    /// <summary>M6 7 X: a protection line of one damage type (its element, I to VI): it cuts the hits of that type.</summary>
    Protection,

    /// <summary>M6 7 AN (the owner, 2026-11-06): a regeneration line (Regeneration Self, Rejuvenation Self, Mana Renewal Self): read at the buff bar on the skill the bootstrap reaches, bought right after the two Life IVs and the bootstrap, before more Item scrolls.</summary>
    Recovery,

    /// <summary>M6 7 AD: the stamina-to-mana line (Stamina to Mana Self, I to VI): its level is the one whose press nets the most at the character's stamina, not the highest she can cast (<see cref="Mage.ScrivenerLearnPlan.DanceTarget"/>).</summary>
    Dance,

    /// <summary>M6 7 AK (the owner, 2026-11-06: buff everything, and bootstrap): a creature self buff (I to IV): its level is the highest at the ladder's <see cref="Ladder.PlanBuffChance"/> on the skill the bootstrap reaches (<see cref="Mage.BuffReach"/>).</summary>
    SelfBuff,

    /// <summary>M6 7 AK: an item armor line cast on each worn piece, by the same bar: Impenetrability and Bludgeon Bane, and from M6 14 (<c>m6-banes</c>) the six other banes (Piercing, Blade, Lightning, Flame, Frost, Acid), each I to VI. One level is bought first at home when the bar's level is sold only in another town (<see cref="Mage.ScrivenerLearnPlan"/>).</summary>
    ItemArmor,

    /// <summary>M6 7 AN: a line the fight keeps her alive and fed with (Drain Health Other, Drain Stamina Other, Heal Self): the catalog holds exactly the levels she buys, each bought once it is missing, at the combat bar; a higher level loses mana at her size or waits for a later decision.</summary>
    Sustain,

    /// <summary>
    /// M6 14 (<c>m6-life-through</c>): a Life Magic line the fight attacks with where no bolt reaches (Harm Other, I to VI): one level is bought, the highest at the ladder's <see cref="Ladder.PlanCastChance"/> on her
    /// standing skill that a scrivener of the ground's home town sells (<see cref="Mage.ScrivenerLearnPlan"/>); a higher level sold only off home is the Town Network's once the home's is in the book.
    /// </summary>
    Attack,

    /// <summary>
    /// M6 14 (<c>m6-caster</c>): an aura she casts on herself for the caster she wields (Spirit Drinker, Hermetic Link, Defender; I to VI): read at the buff bar, bought by the item armor lines' rule, and planned only while it adds
    /// to a caster she holds (<see cref="Mage.CasterGain"/>).
    /// </summary>
    CasterAura,

    /// <summary>
    /// M6 14 (the owner, 2026-11-14: at her level the potions go; with Mana Conversion trained, Revitalize Self then Stamina to Mana Self restores her mana): the dance's stamina line (Revitalize Self, I to VI). It stands right after
    /// <see cref="Dance"/>, and a level of it is planned only when one round of it and the best press nets mana on her live state (<see cref="Mage.ScrivenerLearnPlan.RevitalizeTarget"/>); otherwise it is named with the numbers and nothing is bought.
    /// </summary>
    Revitalize,
}

/// <summary>A line of the catalog: its name (the SpellTable's, less the numeral), its kind, its element (a bolt or an elemental vulnerability; null for the rest) and its school.</summary>
/// <param name="Name">The line's name ("Flame Bolt", "Fire Vulnerability Other", "Lifestone Recall").</param>
/// <param name="Kind">Bolt, debuff, travel, armor, protection, recovery, dance, revitalize, self buff, item armor, sustain, attack or caster aura.</param>
/// <param name="Element">The war element (<see cref="Ladder.Elements"/>), or null.</param>
/// <param name="School">The spell's school.</param>
/// <param name="NeedsSkill">M6 7 AK: a skill that must be trained before the line is worth learning (a school's mastery, the file's <c>needs_skill</c>), or null.</param>
/// <param name="FromLevel">
/// M6 14 (<c>m6-banes</c>): the character level from which the plan learns the line (the file's <c>from_level</c>), or null for a line learned as soon as its school is trained. The six other armour banes carry one: under it the
/// plan names the line and buys nothing (<see cref="Mage.LearnNeedKind.Later"/>), so the low rungs' coin and their pass at home are as before.
/// </param>
public sealed record ScrivenerLine(string Name, ScrivenerLineKind Kind, string? Element, MagicSchool School, uint? NeedsSkill = null, int? FromLevel = null);

/// <summary>One create-list row: a scrivener who sells the scroll (D28: every row cites its id).</summary>
/// <param name="VendorWcid">The scrivener's weenie class id.</param>
/// <param name="CreateListId">The row's id in the pinned world SQL's create list (destination 4, Shop).</param>
public sealed record ScrivenerSale(uint VendorWcid, int CreateListId);

/// <summary>A spell the catalog's scriveners sell as a scroll.</summary>
/// <param name="SpellId">The spell id (the scroll's int property 28, SpellDID).</param>
/// <param name="Name">The spell's name.</param>
/// <param name="Line">The line's name (<see cref="ScrivenerLine.Name"/>).</param>
/// <param name="Level">1 to 6, or 0 for a travel spell.</param>
/// <param name="Power">The spell's power (the SpellTable's, pinned by <c>ScrivenerCatalogTests</c>).</param>
/// <param name="ScrollWcid">The scroll's weenie class id.</param>
/// <param name="ScrollValue">The scroll's Value (int property 19).</param>
/// <param name="Sellers">The create-list rows of the catalog's scriveners who sell it.</param>
public sealed record ScrivenerSpell(uint SpellId, string Name, string Line, int Level, uint Power, uint ScrollWcid, int ScrollValue, IReadOnlyList<ScrivenerSale> Sellers);

/// <summary>Where one scrivener stands in a town (a landblock_instance row).</summary>
/// <param name="Town">The town's name (a profile's <c>home.town</c> matches it).</param>
/// <param name="Guid">The instance's guid.</param>
/// <param name="RowCell">The row's own cell (the low word may be 0).</param>
/// <param name="Cell">The cell the station walks to: the row's, or the outdoor cell under the point when the row's low word is 0 (<see cref="CellFromPoint"/>).</param>
/// <param name="Point">The landblock-local point.</param>
public sealed record ScrivenerPlace(string Town, uint Guid, uint RowCell, uint Cell, Vector3 Point)
{
    /// <summary>True when the row's cell had the low word 0 and <see cref="Cell"/> was resolved from the point: the station reads him live before the last metres (a check, not a guess).</summary>
    public bool CellFromPoint => RowCell != Cell;

    /// <summary>
    /// The place the walk to him stops at, when the catalog names one (an instance's optional <c>stop</c>): a clear floor in front of his counter, within his reach, that the walker's physics pre-check passes from the
    /// town's lifestone (M6 1, HoltburgScrivenerWalkDatTests: the floor behind a Holtburg counter is no floor for the walker, and the balcony of 20216 is reached from the street below); null walks to his own cell and point.
    /// </summary>
    public uint? StopCell { get; init; }

    /// <summary>The stop's point (<see cref="StopCell"/>'s), or null.</summary>
    public Vector3? StopPoint { get; init; }
}

/// <summary>A scrivener (a vendor of scrolls) with his sell rate and his places.</summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">The name.</param>
/// <param name="ClassName">The weenie class name.</param>
/// <param name="SellRate">His SellPrice (float property 38).</param>
/// <param name="Places">His instances in the catalog's towns.</param>
public sealed record Scrivener(uint Wcid, string Name, string ClassName, double SellRate, IReadOnlyList<ScrivenerPlace> Places)
{
    /// <summary>ACE's <c>Vendor.GetSellCost</c> (Vendor.cs 577-585 @95ef426d): max(1, ceil(rate x Value - 0.1)).</summary>
    /// <param name="value">The item's Value.</param>
    public int PriceOf(int value) => Math.Max(1, (int)Math.Ceiling((SellRate * value) - 0.1));
}

/// <summary>One scarab a seller lists (M6 item 1 d, D12, D28): the scarab's weenie class id, name and Value, and the create-list row that lists it.</summary>
/// <param name="ScarabWcid">The scarab's weenie class id (the Pyreal Scarab 690, the Platinum Scarab 8897).</param>
/// <param name="Name">The scarab's name.</param>
/// <param name="Value">Its Value (int property 19).</param>
/// <param name="CreateListId">The row's id in the pinned world SQL's create list (destination 4, Shop).</param>
public sealed record ScarabSale(uint ScarabWcid, string Name, int Value, int CreateListId);

/// <summary>M6 7 AU: a focus item a scrivener sells (Foci of Enchantment, Foci of Artifice): ACE casts its school on the foci formula once it is in the pack (<c>Player.HasFoci</c>, Player_Spells.cs:451-488 @95ef426d).</summary>
/// <param name="Wcid">The focus weenie class id.</param>
/// <param name="Name">The focus' name.</param>
/// <param name="School">The school it serves.</param>
/// <param name="Value">Its Value (PropertyInt 19).</param>
/// <param name="Burden">Its burden (PropertyInt 5).</param>
/// <param name="SellerWcids">The catalog scriveners who sell it.</param>
public sealed record ScrivenerFocus(uint Wcid, string Name, MagicSchool School, int Value, int Burden, IReadOnlyList<uint> SellerWcids);

/// <summary>
/// M6 item 1 d (D12, D40): an archmage who sells a carried-only scarab (the buy-trip's vendor), with his sell rate, the scarabs he lists and his places (as a scrivener's: town, guid, cell and point).
/// </summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">The name.</param>
/// <param name="SellRate">His SellPrice (float property 38).</param>
/// <param name="Sells">The scarabs he lists.</param>
/// <param name="Places">His instances in the catalog's towns.</param>
public sealed record ScarabSeller(uint Wcid, string Name, double SellRate, IReadOnlyList<ScarabSale> Sells, IReadOnlyList<ScrivenerPlace> Places)
{
    /// <summary>ACE's <c>Vendor.GetSellCost</c> (Vendor.cs 577-585 @95ef426d): max(1, ceil(rate x Value - 0.1)).</summary>
    /// <param name="value">The item's Value.</param>
    public int PriceOf(int value) => Math.Max(1, (int)Math.Ceiling((SellRate * value) - 0.1));

    /// <summary>The row of the scarab <paramref name="scarabWcid"/>, or null when he does not list it.</summary>
    /// <param name="scarabWcid">The scarab's weenie class id.</param>
    public ScarabSale? SaleOf(uint scarabWcid) => Sells.FirstOrDefault(s => s.ScarabWcid == scarabWcid);
}

/// <summary>A catalog error: the path and the problem.</summary>
public sealed class ScrivenerCatalogException(string path, string problem) : Exception($"scriveners.json {path}: {problem}")
{
    /// <summary>The JSON path of the field at fault.</summary>
    public string JsonPath { get; } = path;
}

/// <summary>
/// M6 item 1 c (D28, D40): the scrivener catalog, <c>Grounds/scriveners.json</c>; M6 item 1 d (D12) adds its <c>scarab_sellers</c> section, the archmages who sell the carried-only scarabs (<see cref="ScarabSellers"/>), the buy-trip's vendors (embedded as the profiles are), read once and immutable. Per scrivener his weenie class id, name, sell rate and places (town, guid, cell and point from
/// the pinned world SQL's landblock_instance rows); per spell its line, level, power, scroll and the create-list rows of the scriveners who sell it (<c>cache/m6/1/sel-scriveners.tsv</c>, <c>sel-scroll-rows.tsv</c>). A missing
/// or unknown field, a seller that is no catalog scrivener or a line that is not listed fails at load with its path. The home town's scriveners join a ground's vendor view at load (<see cref="VendorsIn"/>,
/// <see cref="HuntingGround.Scriveners"/>).
/// </summary>
/// <remarks>
/// The towns (D40, the spec section 5.5 corrected): Holtburg sells every school I to IV on foot (Apprentice I and II, Journeyman III, Scrivener IV), Sanamar I to IV, level V only the Master Scriveners of Plateau Village, Qalaba'r, Dryreach
/// and Westwatch, level VI only the Grand Master Scriveners (Fort Tethana, Stonehold, Candeth Keep, Wai Jhou, Kara, Ayan Baqur, Eastwatch). The prices are SellPrice 50 times the scroll's Value: Flame Bolt II 250, V 10,000, VI 50,000.
/// </remarks>
public sealed class ScrivenerCatalog
{
    /// <summary>The embedded file's logical name (Lifestone.Client.csproj).</summary>
    internal const string ResourceName = "Lifestone.Client.Session.Grounds.scriveners.json";

    private static readonly Lazy<ScrivenerCatalog> Loaded = new(() =>
    {
        using var stream = typeof(ScrivenerCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"the scrivener catalog is not embedded ({ResourceName}; Lifestone.Client.csproj)");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd()).WithStock(ScrivenerStock.Default); // M6 14 (the every-spell rule): the whole shop beside the plan's lines
    });

    private readonly FrozenDictionary<uint, ScrivenerSpell> _spells;
    private readonly FrozenDictionary<string, ScrivenerLine> _lines;
    private readonly FrozenDictionary<uint, Scrivener> _scriveners;

    private ScrivenerCatalog(IReadOnlyList<ScrivenerLine> lines, IReadOnlyList<Scrivener> scriveners, IReadOnlyList<ScrivenerSpell> spells, IReadOnlyDictionary<string, string> sources)
    {
        Lines = lines;
        Scriveners = scriveners;
        Spells = spells;
        Sources = sources;
        _spells = spells.ToFrozenDictionary(s => s.SpellId);
        _lines = lines.ToFrozenDictionary(l => l.Name, StringComparer.Ordinal);
        _scriveners = scriveners.ToFrozenDictionary(s => s.Wcid);
    }

    /// <summary>The embedded catalog.</summary>
    public static ScrivenerCatalog Default => Loaded.Value;

    /// <summary>The lines, in the file's order.</summary>
    public IReadOnlyList<ScrivenerLine> Lines { get; }

    /// <summary>The scriveners, by weenie class id order.</summary>
    public IReadOnlyList<Scrivener> Scriveners { get; }

    /// <summary>The spells, in the file's order.</summary>
    public IReadOnlyList<ScrivenerSpell> Spells { get; }

    /// <summary>Where every section comes from.</summary>
    public IReadOnlyDictionary<string, string> Sources { get; }

    /// <summary>M6 item 1 d (D12): the archmages who sell the carried-only scarabs, in the file's order (Zisaha of Fort Tethana first, D40).</summary>
    public IReadOnlyList<ScarabSeller> ScarabSellers { get; private init; } = [];

    /// <summary>M6 7 AU: the foci the scriveners sell, in the file's order (Enchantment first).</summary>
    public IReadOnlyList<ScrivenerFocus> Foci { get; private init; } = [];

    /// <summary>
    /// M6 14 (the every-spell rule): every scroll the catalog's scriveners sell (<see cref="ScrivenerStock"/>); the embedded catalog carries <see cref="ScrivenerStock.Default"/>, a parsed one (a test's) none. Its scrolls join each
    /// scrivener's wares in the vendor view (<see cref="VendorsIn"/>) after the catalog's own, so the resupply's buy path and its prices see the whole shop.
    /// </summary>
    public ScrivenerStock Stock { get; private init; } = ScrivenerStock.Empty;

    /// <summary>This catalog with <paramref name="stock"/> as its shop (<see cref="Stock"/>).</summary>
    /// <param name="stock">The stock.</param>
    internal ScrivenerCatalog WithStock(ScrivenerStock stock) => new(Lines, Scriveners, Spells, Sources) { ScarabSellers = ScarabSellers, Foci = Foci, Stock = stock ?? throw new ArgumentNullException(nameof(stock)) };

    /// <summary>M6 item 1 d: the sellers who list the scarab <paramref name="scarabWcid"/>, in the file's order.</summary>
    /// <param name="scarabWcid">The scarab's weenie class id.</param>
    public IReadOnlyList<ScarabSeller> ScarabSellersOf(uint scarabWcid) => [.. ScarabSellers.Where(s => s.SaleOf(scarabWcid) is not null)];

    /// <summary>The spell of <paramref name="spellId"/>, or null.</summary>
    /// <param name="spellId">The spell id.</param>
    public ScrivenerSpell? Spell(uint spellId) => _spells.GetValueOrDefault(spellId);

    /// <summary>The line named <paramref name="name"/>, or null.</summary>
    /// <param name="name">The line's name.</param>
    public ScrivenerLine? Line(string name) => _lines.GetValueOrDefault(name);

    /// <summary>The scrivener of <paramref name="wcid"/>, or null.</summary>
    /// <param name="wcid">The weenie class id.</param>
    public Scrivener? ScrivenerOf(uint wcid) => _scriveners.GetValueOrDefault(wcid);

    /// <summary>The spells of the line <paramref name="line"/>, by level (I first).</summary>
    /// <param name="line">The line's name.</param>
    public IReadOnlyList<ScrivenerSpell> LineSpells(string line) => [.. Spells.Where(s => string.Equals(s.Line, line, StringComparison.Ordinal)).OrderBy(s => s.Level)];

    /// <summary>The towns where some catalog scrivener sells <paramref name="spell"/>, in name order.</summary>
    /// <param name="spell">The spell.</param>
    public IReadOnlyList<string> TownsSelling(ScrivenerSpell spell)
    {
        ArgumentNullException.ThrowIfNull(spell);
        return [.. spell.Sellers.SelectMany(s => _scriveners[s.VendorWcid].Places).Select(p => p.Town).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The scriveners of <paramref name="town"/> as vendors of a ground's view (one per scrivener; his first place in the town): his own cell (resolved, <see cref="ScrivenerPlace.Cell"/>) as the stop, no stop point (unless the catalog names one, <see cref="ScrivenerPlace.StopCell"/>: the walk ends there; else it stops
    /// short of him and the open lets ACE's move-to walk the rest), mask 0 (he buys nothing here), and the scrolls he sells with ACE's price at his rate.
    /// </summary>
    /// <param name="town">The town's name (a profile's <c>home.town</c>).</param>
    public IReadOnlyList<GroundVendor> VendorsIn(string town)
    {
        var vendors = new List<GroundVendor>();
        foreach (var scrivener in Scriveners)
        {
            if (scrivener.Places.FirstOrDefault(p => string.Equals(p.Town, town, StringComparison.Ordinal)) is not { } place)
            {
                continue;
            }

            var sells = Spells.Where(s => s.Sellers.Any(r => r.VendorWcid == scrivener.Wcid)).Select(s => new VendorWare(s.ScrollWcid, s.Name, scrivener.PriceOf(s.ScrollValue))).ToList();
            sells.AddRange(Foci.Where(f => f.SellerWcids.Contains(scrivener.Wcid)).Select(f => new VendorWare(f.Wcid, f.Name, scrivener.PriceOf(f.Value)))); // M6 7 AU: the foci beside the scrolls
            var listed = sells.Select(w => w.Wcid).ToHashSet();
            sells.AddRange(Stock.SoldBy(scrivener.Wcid).Where(s => listed.Add(s.ScrollWcid)).Select(s => new VendorWare(s.ScrollWcid, s.Name, scrivener.PriceOf(s.ScrollValue)))); // M6 14 (the every-spell rule): the rest of his shop
            vendors.Add(new GroundVendor(scrivener.Name, scrivener.Wcid, place.Guid, place.Cell, place.Point, place.StopCell ?? place.Cell, place.StopPoint, 0, sells));
        }

        return vendors;
    }

    /// <summary>Reads a catalog; throws <see cref="ScrivenerCatalogException"/> on any error.</summary>
    /// <param name="json">The file's text.</param>
    internal static ScrivenerCatalog Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException e)
        {
            throw new ScrivenerCatalogException("$", "is not JSON: " + e.Message);
        }

        using (doc)
        {
            var top = new Fields(doc.RootElement, "$", ["sources", "lines", "scriveners", "spells", "scarab_sellers", "foci"]);
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in top.Object("sources").EnumerateObject())
            {
                sources[p.Name] = p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } text ? text : throw new ScrivenerCatalogException("$.sources." + p.Name, "is not a non-empty string");
            }

            foreach (var needed in new[] { "scriveners", "towns", "cells", "spells", "lines", "scarab_sellers" })
            {
                if (!sources.ContainsKey(needed))
                {
                    throw new ScrivenerCatalogException("$.sources." + needed, "is missing (D28: every number has its row)");
                }
            }

            var lines = top.List("lines", (e, p) =>
            {
                var f = new Fields(e, p, ["name", "kind", "element", "school"], optional: ["needs_skill", "from_level"]);
                var kind = f.Text("kind") switch
                {
                    "bolt" => ScrivenerLineKind.Bolt,
                    "debuff" => ScrivenerLineKind.Debuff,
                    "travel" => ScrivenerLineKind.Travel,
                    "armor" => ScrivenerLineKind.Armor,
                    "protection" => ScrivenerLineKind.Protection,
                    "recovery" => ScrivenerLineKind.Recovery,
                    "dance" => ScrivenerLineKind.Dance,
                    "self_buff" => ScrivenerLineKind.SelfBuff,
                    "item_armor" => ScrivenerLineKind.ItemArmor,
                    "sustain" => ScrivenerLineKind.Sustain,
                    "attack" => ScrivenerLineKind.Attack,
                    "caster_aura" => ScrivenerLineKind.CasterAura, // M6 14 (m6-caster)
                    "revitalize" => ScrivenerLineKind.Revitalize, // M6 14 (the owner, 2026-11-14: the stamina dance in the potions' place)
                    var other => throw new ScrivenerCatalogException(p + ".kind", $"'{other}' is not bolt, debuff, travel, armor, protection, recovery, dance, revitalize, self_buff, item_armor, sustain, attack or caster_aura"),
                };
                var element = f.OptionalText("element");
                if (element is not null && !Ladder.Elements.Contains(element))
                {
                    throw new ScrivenerCatalogException(p + ".element", $"'{element}' is not an element ({string.Join(", ", Ladder.Elements)})");
                }

                if (kind is ScrivenerLineKind.Bolt or ScrivenerLineKind.Protection && element is null)
                {
                    throw new ScrivenerCatalogException(p + ".element", $"is null for a {f.Text("kind")} line");
                }

                var school = Enum.TryParse<MagicSchool>(f.Text("school"), out var s) && Enum.IsDefined(s) ? s : throw new ScrivenerCatalogException(p + ".school", $"'{f.Text("school")}' is not a magic school");
                int? fromLevel = f.Has("from_level") ? f.Int("from_level") : null;
                if (fromLevel is < 1 or > 275)
                {
                    throw new ScrivenerCatalogException(p + ".from_level", $"{fromLevel} is not a character level (1 to 275)");
                }

                return new ScrivenerLine(f.Text("name"), kind, element, school, f.Has("needs_skill") ? (uint)f.Int("needs_skill") : null, fromLevel);
            });
            Unique(lines.Select(l => l.Name), "$.lines", "name");

            var scriveners = top.List("scriveners", (e, p) =>
            {
                var f = new Fields(e, p, ["wcid", "name", "class", "sell_rate", "instances"]);
                var places = f.List("instances", (ie, ip) =>
                {
                    var g = new Fields(ie, ip, ["town", "guid", "cell", "point"], optional: ["stop"]);
                    var row = g.Hex("cell");
                    var point = g.Point("point");
                    var place = new ScrivenerPlace(g.Text("town"), g.Hex("guid"), row, ResolveCell(row, point, ip), point);
                    if (g.Has("stop"))
                    {
                        var stop = new Fields(g.Object("stop"), ip + ".stop", ["cell", "point", "why"]);
                        place = place with { StopCell = stop.Hex("cell"), StopPoint = stop.Point("point") };
                        _ = stop.Text("why");
                    }

                    return place;
                });
                if (places.Count == 0)
                {
                    throw new ScrivenerCatalogException(p + ".instances", "is empty (a catalog scrivener stands somewhere)");
                }

                var rate = f.Number("sell_rate");
                return rate > 0 ? new Scrivener((uint)f.Int("wcid"), f.Text("name"), f.Text("class"), rate, places) : throw new ScrivenerCatalogException(p + ".sell_rate", "is not positive");
            });
            Unique(scriveners.Select(s => s.Wcid.ToString(CultureInfo.InvariantCulture)), "$.scriveners", "wcid");
            var known = scriveners.Select(s => s.Wcid).ToHashSet();
            var lineNames = lines.Select(l => l.Name).ToHashSet(StringComparer.Ordinal);

            var spells = top.List("spells", (e, p) =>
            {
                var f = new Fields(e, p, ["spell", "name", "line", "level", "power", "scroll_wcid", "scroll_value", "sellers"]);
                var line = f.Text("line");
                if (!lineNames.Contains(line))
                {
                    throw new ScrivenerCatalogException(p + ".line", $"'{line}' is not a line of $.lines");
                }

                var sellers = f.List("sellers", (se, sp) =>
                {
                    var g = new Fields(se, sp, ["wcid", "create_list"]);
                    var wcid = (uint)g.Int("wcid");
                    return known.Contains(wcid) ? new ScrivenerSale(wcid, g.Int("create_list")) : throw new ScrivenerCatalogException(sp + ".wcid", $"{wcid} is no scrivener of $.scriveners");
                });
                if (sellers.Count == 0)
                {
                    throw new ScrivenerCatalogException(p + ".sellers", "is empty (D28: a row cites its create-list id)");
                }

                var level = f.Int("level");
                return level is >= 0 and <= 6
                    ? new ScrivenerSpell((uint)f.Int("spell"), f.Text("name"), line, level, (uint)f.Int("power"), (uint)f.Int("scroll_wcid"), f.Int("scroll_value"), sellers)
                    : throw new ScrivenerCatalogException(p + ".level", $"{level} is not 0 to 6");
            });
            Unique(spells.Select(s => s.SpellId.ToString(CultureInfo.InvariantCulture)), "$.spells", "spell");
            var sellers = top.List("scarab_sellers", (e, p) =>
            {
                var f = new Fields(e, p, ["wcid", "name", "sell_rate", "sells", "instances"]);
                var sells = f.List("sells", (se, sp) =>
                {
                    var g = new Fields(se, sp, ["wcid", "name", "value", "create_list"]);
                    var value = g.Int("value");
                    var row = g.Int("create_list");
                    return value > 0 && row > 0 ? new ScarabSale((uint)g.Int("wcid"), g.Text("name"), value, row) : throw new ScrivenerCatalogException(sp, "value and create_list must be positive (D28: a row cites its create-list id)");
                });
                if (sells.Count == 0)
                {
                    throw new ScrivenerCatalogException(p + ".sells", "is empty (a scarab seller lists a scarab)");
                }

                var places = f.List("instances", (ie, ip) =>
                {
                    var g = new Fields(ie, ip, ["town", "guid", "cell", "point"]);
                    var row = g.Hex("cell");
                    var point = g.Point("point");
                    return new ScrivenerPlace(g.Text("town"), g.Hex("guid"), row, ResolveCell(row, point, ip), point);
                });
                if (places.Count == 0)
                {
                    throw new ScrivenerCatalogException(p + ".instances", "is empty (a catalog seller stands somewhere)");
                }

                var rate = f.Number("sell_rate");
                return rate > 0 ? new ScarabSeller((uint)f.Int("wcid"), f.Text("name"), rate, sells, places) : throw new ScrivenerCatalogException(p + ".sell_rate", "is not positive");
            });
            Unique(sellers.Select(s => s.Wcid.ToString(CultureInfo.InvariantCulture)), "$.scarab_sellers", "wcid");
            var foci = top.List("foci", (e, p) =>
            {
                var f = new Fields(e, p, ["wcid", "name", "school", "value", "burden", "sellers"]);
                var school = Enum.TryParse<MagicSchool>(f.Text("school"), out var sc) && Enum.IsDefined(sc) ? sc : throw new ScrivenerCatalogException(p + ".school", $"'{f.Text("school")}' is not a magic school");
                var sold = f.List("sellers", (se, sp) =>
                {
                    var g = new Fields(se, sp, ["vendor", "create_list"]);
                    var vendor = (uint)g.Int("vendor");
                    return g.Int("create_list") > 0 && scriveners.Any(x => x.Wcid == vendor) ? vendor : throw new ScrivenerCatalogException(sp, "names no catalog scrivener, or no create-list row (D28)");
                });
                return f.Int("value") > 0 && sold.Count > 0 ? new ScrivenerFocus((uint)f.Int("wcid"), f.Text("name"), school, f.Int("value"), f.Int("burden"), sold) : throw new ScrivenerCatalogException(p, "needs a value and a seller");
            });
            return new ScrivenerCatalog(lines, [.. scriveners.OrderBy(s => s.Wcid)], spells, sources.ToFrozenDictionary(StringComparer.Ordinal)) { ScarabSellers = sellers, Foci = foci };
        }
    }

    /// <summary>The row's cell, or (low word 0) the outdoor cell under the point (retail <c>LandDefs::get_outside_lcoord</c>, <see cref="LandDefs.TryGetOutsideLandCoord"/>).</summary>
    private static uint ResolveCell(uint row, Vector3 point, string path)
    {
        if ((row & 0xFFFFu) != 0)
        {
            return row;
        }

        return LandDefs.TryGetOutsideLandCoord(row | 0xFFFFu, point, out var x, out var y) && LandDefs.LandCoordToCellId(x, y) is var cell and not 0 && (cell >> 16) == (row >> 16)
            ? cell
            : throw new ScrivenerCatalogException(path + ".cell", $"0x{row:X8} has the low word 0 and its point ({point.X}, {point.Y}) resolves to no outdoor cell of the landblock");
    }

    private static void Unique(IEnumerable<string> keys, string path, string field)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (!seen.Add(key))
            {
                throw new ScrivenerCatalogException(path, $"{field} {key} is listed twice");
            }
        }
    }

    /// <summary>An object's fields: each must be present (null only where <see cref="OptionalText"/> reads it), and no other field may be.</summary>
    private sealed class Fields
    {
        private readonly JsonElement _e;
        private readonly string _path;

        public Fields(JsonElement e, string path, string[] names, string[]? optional = null)
        {
            optional ??= [];
            if (e.ValueKind != JsonValueKind.Object)
            {
                throw new ScrivenerCatalogException(path, "is not an object");
            }

            foreach (var p in e.EnumerateObject())
            {
                if (!names.Contains(p.Name, StringComparer.Ordinal) && !optional.Contains(p.Name, StringComparer.Ordinal))
                {
                    throw new ScrivenerCatalogException(path + "." + p.Name, "is not a field of this object");
                }
            }

            foreach (var name in names)
            {
                if (!e.TryGetProperty(name, out _))
                {
                    throw new ScrivenerCatalogException(path + "." + name, "is missing");
                }
            }

            (_e, _path) = (e, path);
        }

        private JsonElement Get(string name, JsonValueKind kind)
        {
            var v = _e.GetProperty(name);
            return v.ValueKind == kind ? v : throw new ScrivenerCatalogException(_path + "." + name, $"is {v.ValueKind}, not {kind}");
        }

        public JsonElement Object(string name) => Get(name, JsonValueKind.Object);

        public bool Has(string name) => _e.TryGetProperty(name, out _);

        public string Text(string name) => Get(name, JsonValueKind.String).GetString() is { Length: > 0 } s ? s : throw new ScrivenerCatalogException(_path + "." + name, "is empty");

        public string? OptionalText(string name) => _e.GetProperty(name).ValueKind == JsonValueKind.Null ? null : Text(name);

        public int Int(string name) => Get(name, JsonValueKind.Number).TryGetInt32(out var i) && i >= 0 ? i : throw new ScrivenerCatalogException(_path + "." + name, "is not a whole number from 0");

        public double Number(string name) => Get(name, JsonValueKind.Number).GetDouble();

        public uint Hex(string name)
        {
            var text = Text(name);
            return text.StartsWith("0x", StringComparison.Ordinal) && uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : throw new ScrivenerCatalogException(_path + "." + name, $"'{text}' is not a 0x hex number");
        }

        public Vector3 Point(string name)
        {
            var f = new Fields(Object(name), _path + "." + name, ["x", "y", "z"]);
            return new Vector3((float)f.Number("x"), (float)f.Number("y"), (float)f.Number("z"));
        }

        public IReadOnlyList<T> List<T>(string name, Func<JsonElement, string, T> read)
        {
            var list = new List<T>();
            var i = 0;
            foreach (var e in Get(name, JsonValueKind.Array).EnumerateArray())
            {
                list.Add(read(e, string.Create(CultureInfo.InvariantCulture, $"{_path}.{name}[{i++}]")));
            }

            return list.AsReadOnly();
        }
    }
}

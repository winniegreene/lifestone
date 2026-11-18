using System.Numerics;
using System.Text.Json.Nodes;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Grounds;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 item 1 c (D28, D40): the scrivener catalog (<c>Grounds/scriveners.json</c>). What these prove: it loads, every section has its source and every spell cites the create-list row of each scrivener who sells it; with the retail dats,
/// every scroll's spell is real in the SpellTable with the catalog's name, school and power; Holtburg sells every line I to IV on foot (Flame Bolt II's scroll 5998 at the Apprentice Scrivener 20216, row 78068, 250 pyreals at his
/// SellPrice 50; his row's cell 0xA9B30000 resolved to the outdoor cell under his point), level V only at Plateau Village, Qalaba'r, Dryreach and Westwatch, level VI only at the seven Grand Master towns; the home town's scriveners
/// join a ground's vendor view additively; a missing field fails at load with its path.
/// </summary>
public sealed class ScrivenerCatalogTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly string[] VTowns = ["Dryreach", "Plateau Village", "Qalaba'r", "Westwatch"];
    private static readonly string[] ViTowns = ["Ayan Baqur", "Candeth Keep", "Eastwatch", "Fort Tethana", "Kara", "Stonehold", "Wai Jhou"];

    private static ScrivenerCatalog C => ScrivenerCatalog.Default;

    [Fact]
    public void The_catalog_loads_with_its_sources_and_every_spell_cites_its_create_list_rows()
    {
        Assert.Equal(["cells", "foci", "lines", "scarab_sellers", "scriveners", "spells", "towns"], C.Sources.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(58, C.Lines.Count); // M6 14 (the stamina dance in the potions' place): Revitalize Self; M6 14 (m6-caster): Spirit Drinker, Hermetic Link and Defender; M6 14 (m6-banes): the six other armour banes; M6 14 (m6-life-through): Harm Other; M6 7 AN: Regeneration Self, Rejuvenation Self, Drain Health Other, Drain Stamina Other, Heal Self; // 7 bolt lines, 7 elemental vulnerabilities, Imperil, Vulnerability and Magic Yield Other, 7 travel spells, M6 7 X's Armor Self, Bludgeoning Protection Self and Mana Renewal Self, M6 7 AK's 12 self buffs and 2 item armor lines
        Assert.Equal((21 * 6) + 7 + (14 * 4) + 8 + 5 + 8 + 6 + (6 * 6) + 4 + (3 * 6) + 6, C.Spells.Count); // M6 14: Revitalize Self I to VI (6); M6 14 (m6-caster): the three caster auras I to VI (18); M6 14 (m6-banes): the six other banes I to VI (36) and Impenetrability and Bludgeon Bane V and VI (4); M6 14 (m6-life-through): Harm Other I to VI (6); M6 14: Heal Self and Drain Health Other III to VI (8); M6 7 AN: Regeneration and Rejuvenation I to IV, Drain Health I and II, Drain Stamina I, Heal Self I and II; // M6 7 AK's creature self lines stop at IV, TODO(m6-self-buff-lines-v-vi)
        Assert.All(C.Spells, s => Assert.NotEmpty(s.Sellers));
        Assert.All(C.Spells.SelectMany(s => s.Sellers), r => Assert.True(r.CreateListId > 0 && C.ScrivenerOf(r.VendorWcid) is not null));
        Assert.All(C.Scriveners, s => Assert.Equal(50, s.SellRate)); // float property 38 of every one
        foreach (var element in Ladder.Elements)
        {
            var line = Assert.Single(C.Lines, l => l.Kind == ScrivenerLineKind.Bolt && l.Element == element);
            Assert.Equal(Lifestone.Client.Session.Mage.SpellPlan.BoltLineNames[element], line.Name);
            Assert.Equal([1, 2, 3, 4, 5, 6], C.LineSpells(line.Name).Select(s => s.Level));
        }
    }

    [Fact]
    public void M6_14_Harm_Other_is_the_attack_line_I_to_VI_with_the_worlds_scrolls_and_sellers()
    {
        // The read-only world copy, 2026-11-12: SELECT object_Id FROM weenie_properties_d_i_d WHERE type=28 AND value=<spell>; weenie_properties_int type 19; weenie_properties_create_list WHERE destination_Type=4.
        var line = C.Line("Harm Other")!;
        Assert.Equal((ScrivenerLineKind.Attack, MagicSchool.LifeMagic, (string?)null), (line.Kind, line.School, line.Element));
        Assert.Same(line, Assert.Single(C.Lines, l => l.Kind == ScrivenerLineKind.Attack));
        var spells = C.LineSpells("Harm Other");
        Assert.Equal(Lifestone.Client.Session.Vitals.VitalsMeans.HarmOther.Take(6), spells.Select(s => s.SpellId)); // the ids the fight casts
        Assert.Equal([1, 2, 3, 4, 5, 6], spells.Select(s => s.Level));
        Assert.Equal(["Harm Other I", "Harm Other II", "Harm Other III", "Harm Other IV", "Harm Other V", "Harm Other VI"], spells.Select(s => s.Name));
        Assert.Equal([1554u, 2687u, 2688u, 2689u, 2690u, 2691u], spells.Select(s => s.ScrollWcid));
        Assert.Equal([1, 5, 20, 100, 200, 1000], spells.Select(s => s.ScrollValue));
        Assert.Equal([new ScrivenerSale(20217, 33575), new ScrivenerSale(30011, 79107)], spells[0].Sellers); // the Apprentice Scriveners of Life Magic
        Assert.Equal([new ScrivenerSale(20218, 33636), new ScrivenerSale(30012, 79168)], spells[1].Sellers);
        Assert.Equal([new ScrivenerSale(20221, 33816), new ScrivenerSale(30015, 79339)], spells[2].Sellers); // the Journeyman
        Assert.Equal([new ScrivenerSale(20222, 33876)], spells[3].Sellers); // the Scrivener of Life Magic
        Assert.Equal([new ScrivenerSale(20219, 33696), new ScrivenerSale(30013, 79225)], spells[4].Sellers); // the Master
        Assert.Equal([new ScrivenerSale(20220, 33756), new ScrivenerSale(30014, 79282)], spells[5].Sellers); // the Grand Master

        var outskirts = GroundCatalog.Get("holtburg-outskirts");
        Assert.Equal([50, 250, 1000, 5000], spells.Take(4).Select(s => outskirts.PriceOf(s.ScrollWcid))); // ceil(50 x Value - 0.1): Holtburg sells I to IV on foot
        Assert.Null(outskirts.VendorSelling(spells[4].ScrollWcid)); // V: the Master Scriveners' towns
    }

    [Fact]
    public void M6_14_Revitalize_Self_is_the_dance_stamina_line_I_to_VI_with_the_worlds_scrolls_and_sellers()
    {
        // The read-only world copy, 2026-11-15: SELECT ... FROM weenie_properties_d_i_d WHERE type=28 AND value IN (1177 to 1182, 2083, 4321), weenie_properties_int types 1 and 19, weenie_properties_create_list WHERE destination_Type=4 (scriveners.json sources.spells).
        var line = C.Line("Revitalize Self")!;
        Assert.Equal((ScrivenerLineKind.Revitalize, MagicSchool.LifeMagic, (string?)null), (line.Kind, line.School, line.Element));
        Assert.Same(line, Assert.Single(C.Lines, l => l.Kind == ScrivenerLineKind.Revitalize));
        var spells = C.LineSpells("Revitalize Self");
        Assert.Equal(Lifestone.Client.Session.Vitals.VitalsMeans.RevitalizeSelf.Take(6), spells.Select(s => s.SpellId)); // the ids the dance casts
        Assert.Equal(Lifestone.Client.Session.Mage.BuffStatMods.Revitalize.Take(6).Select(r => r.Id), spells.Select(s => s.SpellId)); // and the boost rows the plan weighs
        Assert.Equal([1, 2, 3, 4, 5, 6], spells.Select(s => s.Level));
        Assert.Equal(["Revitalize Self I", "Revitalize Self II", "Revitalize Self III", "Revitalize Self IV", "Revitalize Self V", "Revitalize Self VI"], spells.Select(s => s.Name));
        Assert.Equal([1785u, 2727u, 2728u, 2729u, 2730u, 2731u], spells.Select(s => s.ScrollWcid));
        Assert.Equal([1, 5, 20, 100, 200, 1000], spells.Select(s => s.ScrollValue));
        Assert.Equal([new ScrivenerSale(20217, 33581), new ScrivenerSale(30011, 79113)], spells[0].Sellers); // the Apprentice Scriveners of Life Magic
        Assert.Equal([new ScrivenerSale(20218, 33642), new ScrivenerSale(30012, 79174)], spells[1].Sellers);
        Assert.Equal([new ScrivenerSale(20221, 33822), new ScrivenerSale(30015, 79345)], spells[2].Sellers); // the Journeyman
        Assert.Equal([new ScrivenerSale(20222, 33882)], spells[3].Sellers); // the Scrivener of Life Magic (the Viamont 30016 and the ACE-added 49597 left out)
        Assert.Equal([new ScrivenerSale(20219, 33702), new ScrivenerSale(30013, 79231)], spells[4].Sellers); // the Master
        Assert.Equal([new ScrivenerSale(20220, 33762), new ScrivenerSale(30014, 79288)], spells[5].Sellers); // the Grand Master

        var holding = GroundCatalog.Get("tusker-holding"); // her rung at level 68: home Holtburg
        Assert.Equal([50, 250, 1000, 5000], spells.Take(4).Select(s => holding.PriceOf(s.ScrollWcid))); // ceil(50 x Value - 0.1): Holtburg sells I to IV on foot
        Assert.Null(holding.VendorSelling(spells[4].ScrollWcid));
        Assert.Equal(VTowns, C.TownsSelling(spells[4]));
        Assert.Equal(ViTowns, C.TownsSelling(spells[5])); // Stonehold among them, where the Town Network buys her VI
        Assert.Equal(50_000, C.ScrivenerOf(20220)!.PriceOf(spells[5].ScrollValue));
        Assert.Contains(C.ScrivenerOf(20220)!.Places, p => p.Town == "Stonehold");
    }

    [RequiresDatsFact]
    public void Every_scrolls_spell_is_real_in_the_SpellTable_with_the_catalogs_name_school_and_power()
    {
        var table = SpellTableData.Load(fixture.Dats).Spells;
        var wrong = new List<string>();
        foreach (var spell in C.Spells)
        {
            var school = C.Line(spell.Line)!.School;
            if (!table.TryGetValue(spell.SpellId, out var info))
            {
                wrong.Add($"{spell.SpellId} {spell.Name}: not in the SpellTable");
            }
            else if (info.Name != spell.Name || info.School != school || info.Power != spell.Power)
            {
                wrong.Add($"{spell.SpellId} {spell.Name}/{school}/{spell.Power}: the SpellTable says {info.Name}/{info.School}/{info.Power}");
            }
        }

        output.WriteLine(string.Join(Environment.NewLine, wrong));
        Assert.Empty(wrong);
    }

    [Fact]
    public void Holtburg_sells_every_line_I_to_IV_on_foot_and_Flame_Bolt_II_at_the_Apprentice_Scrivener_20216()
    {
        var two = C.Spell(81)!;
        Assert.Equal(("Flame Bolt II", "Flame Bolt", 2, 50u, 5998u, 5), (two.Name, two.Line, two.Level, two.Power, two.ScrollWcid, two.ScrollValue));
        Assert.Contains(new ScrivenerSale(20216, 78068), two.Sellers); // sel-scroll-rows.tsv
        var apprentice = C.ScrivenerOf(20216)!;
        Assert.Equal(250, apprentice.PriceOf(two.ScrollValue)); // ceil(50 x 5 - 0.1), ACE Vendor.GetSellCost
        Assert.Equal(50_000, apprentice.PriceOf(C.Spell(85)!.ScrollValue)); // Flame Bolt VI: Value 1,000
        var place = Assert.Single(apprentice.Places, p => p.Town == "Holtburg");
        Assert.Equal((0x7A9B300Du, 0xA9B30000u, 0xA9B3003Cu, true), (place.Guid, place.RowCell, place.Cell, place.CellFromPoint)); // the row's low word 0: the outdoor cell under (184.0, 79.8)
        Assert.False(C.ScrivenerOf(20215)!.Places.Single(p => p.Town == "Holtburg").CellFromPoint); // 0xA9B30101, his own row's cell
        foreach (var line in C.Lines.Where(l => l.Kind != ScrivenerLineKind.Travel))
        {
            foreach (var spell in C.LineSpells(line.Name).Where(s => s.Level <= 4))
            {
                Assert.Contains("Holtburg", C.TownsSelling(spell));
                Assert.Contains("Sanamar", C.TownsSelling(spell));
            }
        }

        Assert.All(C.Lines.Where(l => l.Kind == ScrivenerLineKind.Travel), l => Assert.Contains(C.LineSpells(l.Name).Single().Sellers, s => s.VendorWcid == 20213)); // the Journeyman Scrivener of Item Magic, Holtburg
        Assert.Contains(new ScrivenerSale(20213, 77913), C.Spell(47)!.Sellers); // Primary Portal Tie at the Journeyman Scrivener of Item Magic
    }

    [Fact]
    public void Level_V_is_sold_only_at_Plateau_Village_Qalabar_Dryreach_and_Westwatch()
    {
        foreach (var spell in C.Spells.Where(s => s.Level == 5))
        {
            Assert.Equal(VTowns, C.TownsSelling(spell));
        }

        Assert.Contains(new ScrivenerSale(20223, 33943), C.Spell(84)!.Sellers); // Flame Bolt V at the Master Scrivener of War Magic
    }

    [Fact]
    public void Level_VI_is_sold_only_at_the_Grand_Master_Scriveners()
    {
        foreach (var spell in C.Spells.Where(s => s.Level == 6))
        {
            Assert.Equal(ViTowns, C.TownsSelling(spell));
        }

        var six = C.Spell(85)!;
        Assert.Contains(new ScrivenerSale(20224, 33983), six.Sellers);
        Assert.Equal(0x25810175u, C.ScrivenerOf(20224)!.Places.Single(p => p.Town == "Fort Tethana").Cell);
        Assert.All(C.Spells.Where(s => s.Level == 6).SelectMany(s => s.Sellers), s => Assert.StartsWith("Grand Master Scrivener", C.ScrivenerOf(s.VendorWcid)!.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void The_home_towns_scriveners_join_the_grounds_vendor_view_additively()
    {
        var outskirts = GroundCatalog.Get("holtburg-outskirts");
        Assert.Same(outskirts.VendorOf(20216), outskirts.VendorSelling(5998)); // the buy path finds the scroll's vendor
        Assert.Equal(250, outskirts.PriceOf(5998));
        Assert.DoesNotContain(outskirts.Vendors, v => v.Wcid == 20216); // the profile's own list is unchanged
        Assert.All(outskirts.Scriveners, v => Assert.Equal(0u, v.BuysItemTypes)); // no sale goes to them
        Assert.Null(outskirts.VendorSelling(6001)); // Flame Bolt V: not sold in Holtburg

        var den = GroundCatalog.Get("black-spawn-den-aluvian");
        Assert.Equal([718u, 2304u, 715u, 719u, 716u, 714u, 20214u], den.Vendors.Select(v => v.Wcid)); // the den's profile vendors as before (M6 14 grocer: and Fispur Ansel the Grocer)
        Assert.Same(den.Vendors.Single(v => v.Wcid == 20214), den.VendorOf(20214));
        Assert.Contains(den.Scriveners, v => v.Wcid == 20216);

        var temple = GroundCatalog.Get("tusker-temple-fort-tethana");
        Assert.Equal(temple.VendorOf(20224), temple.VendorSelling(6002)); // Flame Bolt VI at Fort Tethana's Grand Master
        Assert.Equal(50_000, temple.PriceOf(6002));
        Assert.Null(temple.VendorSelling(5998));
    }

    /// <summary>M6 1 (climb 2): an instance's optional stop is the vendor's stop in the ground's view; HoltburgScrivenerWalkDatTests walks each one on the dats.</summary>
    [Fact]
    public void A_scriveners_stop_is_his_stop_cell_and_point_in_the_vendor_view()
    {
        var outskirts = GroundCatalog.Get("holtburg-outskirts");
        var life = outskirts.VendorOf(20217)!;
        Assert.Equal(0xA9B30102u, life.Cell);
        Assert.Equal(0xA9B30100u, life.StopCell); // the front room before his counter
        Assert.Equal(new Vector3(182.7f, 85.4f, 116.005f), life.StopPoint);
        var balcony = outskirts.VendorOf(20216)!;
        Assert.Equal(0xA9B3003Cu, balcony.StopCell); // his own cell: the street under his balcony is a point request too (ResupplyRun.RoomRequest)
        Assert.NotNull(balcony.StopPoint);
        Assert.Null(outskirts.VendorOf(20215)!.StopPoint); // a walk that stands keeps his own cell and point
        Assert.Equal(outskirts.VendorOf(20215)!.Cell, outskirts.VendorOf(20215)!.StopCell);
    }

    [Theory]
    [InlineData("spells", "power")]
    [InlineData("spells", "sellers")]
    [InlineData("scriveners", "instances")]
    [InlineData("lines", "school")]
    public void A_missing_field_fails_at_load_with_its_path(string section, string field)
    {
        var json = JsonNode.Parse(ResourceText())!;
        ((JsonObject)json[section]![0]!).Remove(field);

        var e = Assert.Throws<ScrivenerCatalogException>(() => ScrivenerCatalog.Parse(json.ToJsonString()));

        Assert.Equal($"$.{section}[0].{field}", e.JsonPath);
        Assert.Contains("is missing", e.Message, StringComparison.Ordinal);
    }

    private static string ResourceText()
    {
        using var stream = typeof(ScrivenerCatalog).Assembly.GetManifestResourceStream(ScrivenerCatalog.ResourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

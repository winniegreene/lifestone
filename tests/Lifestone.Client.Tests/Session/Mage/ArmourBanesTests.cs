using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Lifestone.ServerTools;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 14 (<c>m6-banes</c>, the owner's order of 2026-11-12: every armour protection on her armour before the Direlands, not only the tuskers' two). What these prove: the six other banes are lines of the climber's
/// profile alone, with the world's spell ids, cast right after Impenetrability and Bludgeon Bane; the catalog holds them I to VI with the world's scrolls, values and sellers (and Impenetrability and Bludgeon Bane to VI),
/// at the Master and Grand Master Scriveners of Item Magic for V and VI; the learn plan buys each of the eight at the highest level her standing Item Enchantment casts at the buff bar, the home's level IV first when
/// that level is sold only in another town; a pass casts all eight at her own guid and the armour memory keeps each on every worn piece; a cycle in the field wants one of the six only where the ground's targets deal
/// its damage type, so the fight's refresh recasts Impenetrability and Bludgeon Bane under fire as before and leaves the others to the pass at home. Over the scripted caster and the catalogs; nothing here needs a
/// server (the SpellTable pins are <c>ArmourBanesDatTests</c>).
/// </summary>
public sealed class ArmourBanesTests(ITestOutputHelper output)
{
    private const uint Life = MageTemplate.LifeMagic, Creature = MageTemplate.CreatureEnchantment, Item = MageTemplate.ItemEnchantment;
    private const string Cave = "tusker-cave";
    private const string OffHomeNote = "; the plan's level V is sold only at Dryreach, Plateau Village, Qalaba'r, Westwatch: the highest level Holtburg sells is bought first";

    private static readonly string[] Six = ["Piercing Bane", "Blade Bane", "Lightning Bane", "Flame Bane", "Frost Bane", "Acid Bane"];
    private static readonly string[] Eight = ["Impenetrability", "Bludgeon Bane", .. Six];
    private static readonly string[] VTowns = ["Dryreach", "Plateau Village", "Qalaba'r", "Westwatch"];
    private static readonly string[] ViTowns = ["Ayan Baqur", "Candeth Keep", "Eastwatch", "Fort Tethana", "Kara", "Stonehold", "Wai Jhou"];

    /// <summary>The world's rows (the read-only world copy, 2026-11-12: spell.id by name; the scroll of weenie_properties_d_i_d type 28; weenie_properties_create_list destination 4 of the catalog's scriveners): per line the spells I to VIII, the scrolls I to VI and the create-list rows of 20215, 30009, 20216, 30010, 20213, 30029, 20214, 20211, 30027, 20212, 30028.</summary>
    private static readonly (string Line, uint[] Spells, uint[] Scrolls, int[] Rows)[] World =
    [
        ("Piercing Bane", [1569, 1570, 1571, 1572, 1573, 1574, 2113, 4412], [1892, 2867, 2868, 2869, 2870, 2871], [77992, 79013, 78055, 79072, 77900, 80490, 77948, 77817, 80407, 77857, 80447]),
        ("Blade Bane", [37, 1558, 1559, 1560, 1561, 1562, 2094, 4393], [1552, 2772, 2773, 2774, 2775, 2776], [77968, 78989, 78031, 79048, 77876, 80466, 77924, 77793, 80383, 77833, 80423]),
        ("Lightning Bane", [1535, 1536, 1537, 1538, 1539, 1540, 2110, 4409], [1889, 2852, 2853, 2854, 2855, 2856], [77990, 79011, 78053, 79070, 77898, 80488, 77946, 77815, 80405, 77855, 80445]),
        ("Flame Bane", [1547, 1548, 1549, 1550, 1551, 1552, 2102, 4401], [1881, 2812, 2813, 2814, 2815, 2816], [77978, 78999, 78041, 79058, 77886, 80476, 77934, 77803, 80393, 77843, 80433]),
        ("Frost Bane", [1523, 1524, 1525, 1526, 1527, 1528, 2104, 4403], [1883, 2822, 2823, 2824, 2825, 2826], [77980, 79001, 78043, 79060, 77888, 80478, 77936, 77805, 80395, 77845, 80435]),
        ("Acid Bane", [1493, 1494, 1495, 1496, 1497, 1498, 2092, 4391], [1875, 2762, 2763, 2764, 2765, 2766], [77966, 78987, 78029, 79046, 77874, 80464, 77922, 77791, 80381, 77831, 80421]),
    ];

    private static Ladder L => LadderLoader.WarMage;

    private static ScrivenerCatalog C => ScrivenerCatalog.Default;

    // ------------------------------------------------------------------------------------------------ the lines

    [Fact]
    public void The_six_other_banes_are_lines_of_the_climbers_profile_alone_with_the_worlds_spells_cast_after_her_two()
    {
        Assert.Equal(Six, ArmourBanes.Names);
        Assert.Equal(Eight, ArmourBanes.AllArmourLines);
        foreach (var (name, spells, _, _) in World)
        {
            var line = Assert.Single(ArmourBanes.Lines, l => l.Name == name);
            Assert.Equal((name, MagicSchool.ItemEnchantment, ProfileRole.ItemBuffArmor, true, true), (line.DatRoot, line.School, line.Role, line.ByPlay, line.IsBuff));
            Assert.Equal(spells, line.Spells.Select(s => s.Id));
            Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], line.Spells.Select(s => s.Position));
            Assert.Equal([1u, 50u, 100u, 150u, 200u, 250u, 300u, 400u], line.Spells.Select(s => s.Power)); // Bludgeon Bane's own pattern (ArmourBanesDatTests pins the SpellTable)
            Assert.Equal([1800.0, 1800, 1800, 1800, 1800, 2700, 3600, 5400], line.Spells.Select(s => s.DurationSeconds));
            Assert.Equal([name + " I", name + " II", name + " III", name + " IV", name + " V", name + " VI"], line.Spells.Take(6).Select(s => s.Name));
            Assert.Equal(RefreshGroup.Protection, ArenaProfile.RefreshGroupOf(line));

            Assert.Same(line, BuffProfiles.Climber.LineNamed(name)); // hers
            Assert.Null(ArenaProfile.LineNamed(name)); // not BSD Mage's: his profile, his build and his kit are unchanged
            Assert.DoesNotContain(ArenaProfile.SpellsToLearn, id => spells.Contains(id));
            Assert.Null(ArenaProfile.Buffs.LineNamed(name));
        }

        Assert.Equal(["Archer's Bane", "Swordsman's Bane", "Astyrrian's Bane", "Inferno's Bane", "Gelidite's Bane", "Olthoi's Bane"], ArmourBanes.Lines.Select(l => l.Spells[6].Name));
        Assert.All(ArmourBanes.Lines, l => Assert.Equal("Incantation of " + l.Name, l.Spells[7].Name));

        // Her pass casts the eight armour lines together, in this order, before the attributes (the owner's order of M6 7 AK, with the six after her two).
        var order = BuffProfiles.Climber.BuffOrder.Select(l => l.Name).ToList();
        var at = order.IndexOf("Impenetrability");
        Assert.Equal([.. Eight, "Endurance Self"], order.Skip(at).Take(9));
        Assert.Equal(8, BuffProfiles.Climber.BuffOrder.Count(l => l.Role == ProfileRole.ItemBuffArmor));
        Assert.Equal(2, ArenaProfile.Buffs.BuffOrder.Count(l => l.Role == ProfileRole.ItemBuffArmor));
        Assert.Empty(BuffProfiles.Climber.SpellsToLearn.Intersect(World.SelectMany(w => w.Spells))); // learned by play: never granted
    }

    [Fact]
    public void In_the_field_one_of_the_six_is_kept_only_where_the_grounds_targets_deal_its_damage_and_her_two_as_before()
    {
        Assert.Equal([0x2u, 0x1u, 0x40u, 0x10u, 0x8u, 0x20u], Six.Select(ArmourBanes.DamageBitOf)); // ACE DamageType: Pierce, Slash, Electric, Fire, Cold, Acid
        Assert.All(new[] { "Impenetrability", "Bludgeon Bane", "Armor Self", "Focus Self", "Spirit Drinker" }, n => Assert.Equal(0u, ArmourBanes.DamageBitOf(n)));

        // The tusker grounds deal bludgeon alone: her two lines are the field's, the six are the pass at home's.
        var cave = ArmourBanes.FieldMask(GroundCatalog.Get(Cave));
        Assert.Equal(0x4u, cave);
        Assert.All(Six, n => Assert.False(ArmourBanes.KeptInField(n, cave), n));
        Assert.All(new[] { "Impenetrability", "Bludgeon Bane", "Armor Self", "Bludgeon Protection Self" }, n => Assert.True(ArmourBanes.KeptInField(n, cave), n));
        Assert.Equal(0x4u, ArmourBanes.FieldMask(GroundCatalog.Get("tusker-temple-20-40"))); // the Shrine Statue's 0x0048 is role avoid: not fought, not counted

        // The Crater's Cursed Wisp (0x0018) and Ember (0x0010) deal cold and fire: those two banes are kept there, the rest are not.
        var crater = ArmourBanes.FieldMask(GroundCatalog.Get("crater-caves"));
        Assert.Equal(0x1Cu, crater);
        Assert.Equal(["Flame Bane", "Frost Bane"], Six.Where(n => ArmourBanes.KeptInField(n, crater)));

        // A ground that names no damage type keeps none of the six (TODO(m6-outskirts-damage-types)) and her two as before.
        Assert.Equal(0u, ArmourBanes.FieldMask(GroundCatalog.Get("holtburg-outskirts")));
        Assert.All(Six, n => Assert.False(ArmourBanes.KeptInField(n, 0), n));
        Assert.True(ArmourBanes.KeptInField("Bludgeon Bane", 0));
        Assert.All(Six, n => Assert.True(ArmourBanes.KeptInField(n, 0x7F), n));
    }

    // ------------------------------------------------------------------------------------------------ the catalog

    [Fact]
    public void The_catalog_holds_the_six_banes_I_to_VI_with_the_worlds_scrolls_values_and_sellers()
    {
        Assert.Equal(Eight, C.Lines.Where(l => l.Kind == ScrivenerLineKind.ItemArmor).Select(l => l.Name)); // the plan's order: her two, then the six
        foreach (var (name, ids, scrolls, rows) in World)
        {
            var line = C.Line(name)!;
            Assert.Equal((ScrivenerLineKind.ItemArmor, MagicSchool.ItemEnchantment, (string?)null, (uint?)null), (line.Kind, line.School, line.Element, line.NeedsSkill));
            var spells = C.LineSpells(name);
            Assert.Equal(ids.Take(6), spells.Select(s => s.SpellId));
            Assert.Equal([1, 2, 3, 4, 5, 6], spells.Select(s => s.Level));
            Assert.Equal([1u, 50u, 100u, 150u, 200u, 250u], spells.Select(s => s.Power));
            Assert.Equal(ArmourBanes.Lines.Single(l => l.Name == name).Spells.Take(6).Select(s => s.Name), spells.Select(s => s.Name));
            Assert.Equal(scrolls, spells.Select(s => s.ScrollWcid));
            Assert.Equal([1, 5, 20, 100, 200, 1000], spells.Select(s => s.ScrollValue));
            Assert.Equal([new ScrivenerSale(20215, rows[0]), new ScrivenerSale(30009, rows[1])], spells[0].Sellers); // the Apprentice Scriveners of Item and War Magic (Holtburg, Sanamar)
            Assert.Equal([new ScrivenerSale(20216, rows[2]), new ScrivenerSale(30010, rows[3])], spells[1].Sellers);
            Assert.Equal([new ScrivenerSale(20213, rows[4]), new ScrivenerSale(30029, rows[5])], spells[2].Sellers); // the Journeyman Scriveners of Item Magic
            Assert.Equal([new ScrivenerSale(20214, rows[6])], spells[3].Sellers); // the Scrivener of Item Magic (the Viamont 30030 and the ACE-added 49599 left out, as the catalog's other rows)
            Assert.Equal([new ScrivenerSale(20211, rows[7]), new ScrivenerSale(30027, rows[8])], spells[4].Sellers); // the Master Scriveners of Item Magic
            Assert.Equal([new ScrivenerSale(20212, rows[9]), new ScrivenerSale(30028, rows[10])], spells[5].Sellers); // the Grand Masters
            Assert.Equal(VTowns, C.TownsSelling(spells[4]));
            Assert.Equal(ViTowns, C.TownsSelling(spells[5]));
        }

        // Her two lines reach VI in the catalog too (the same SELECTs): the Master's and the Grand Master's rows.
        Assert.Equal([(1485u, 2845u, 200), (1486u, 2846u, 1000)], C.LineSpells("Impenetrability").Skip(4).Select(s => (s.SpellId, s.ScrollWcid, s.ScrollValue)));
        Assert.Equal([new ScrivenerSale(20211, 77813), new ScrivenerSale(30027, 80403)], C.Spell(1485)!.Sellers);
        Assert.Equal([new ScrivenerSale(20212, 77853), new ScrivenerSale(30028, 80443)], C.Spell(1486)!.Sellers);
        Assert.Equal([(1515u, 2795u, 200), (1516u, 2796u, 1000)], C.LineSpells("Bludgeon Bane").Skip(4).Select(s => (s.SpellId, s.ScrollWcid, s.ScrollValue)));
        Assert.Equal([new ScrivenerSale(20211, 77798), new ScrivenerSale(30027, 80388)], C.Spell(1515)!.Sellers);
        Assert.Equal([new ScrivenerSale(20212, 77838), new ScrivenerSale(30028, 80428)], C.Spell(1516)!.Sellers);
    }

    [Fact]
    public void The_Master_and_Grand_Master_Scriveners_of_Item_Magic_stand_in_their_towns_and_Holtburg_sells_every_level_IV()
    {
        // landblock_instance rows of weenies 20211, 20212, 30027 and 30028 (the read-only world copy, 2026-11-12): guid and cell, beside the catalog's other Masters and Grand Masters.
        var master = C.ScrivenerOf(20211)!;
        Assert.Equal(("Master Scrivener of Item Magic", "scriveneritemdistant", 50.0), (master.Name, master.ClassName, master.SellRate));
        Assert.Equal([("Plateau Village", 0x749B6037u, 0x49B60137u), ("Qalaba'r", 0x7972204Au, 0x9722017Cu), ("Dryreach", 0x7DA75036u, 0xDA75016Du)], master.Places.Select(p => (p.Town, p.Guid, p.Cell)));
        Assert.Equal([("Westwatch", 0x723DA017u, 0x23DA0115u)], C.ScrivenerOf(30027)!.Places.Select(p => (p.Town, p.Guid, p.Cell)));
        var grand = C.ScrivenerOf(20212)!;
        Assert.Equal(("Grand Master Scrivener of Item Magic", "scriveneritemextreme"), (grand.Name, grand.ClassName));
        Assert.Equal(["Ayan Baqur", "Fort Tethana", "Candeth Keep", "Wai Jhou", "Stonehold", "Kara"], grand.Places.Select(p => p.Town));
        Assert.Equal([0x71134037u, 0x72581036u, 0x72B12021u, 0x73F31017u, 0x764D503Du, 0x7BA1702Cu], grand.Places.Select(p => p.Guid));
        var ayan = grand.Places[0];
        Assert.True(ayan.CellFromPoint && ayan.RowCell == 0x11340000u && ayan.Cell >> 16 == 0x1134u); // the row's low word 0: the outdoor cell under his point, read live before the last metres
        Assert.Equal([("Eastwatch", 0x749F0017u, 0x49F00013u)], C.ScrivenerOf(30028)!.Places.Select(p => (p.Town, p.Guid, p.Cell)));

        // The prices are ACE's GetSellCost, ceil(50 x Value - 0.1): IV 5,000 at Holtburg on foot, V 10,000, VI 50,000.
        var outskirts = GroundCatalog.Get("holtburg-outskirts");
        foreach (var name in Eight)
        {
            var spells = C.LineSpells(name);
            Assert.Equal([50, 250, 1000, 5000], spells.Take(4).Select(s => outskirts.PriceOf(s.ScrollWcid)));
            Assert.Equal(20214u, outskirts.VendorSelling(spells[3].ScrollWcid)!.Wcid);
            Assert.Null(outskirts.VendorSelling(spells[4].ScrollWcid));
            Assert.Equal((10_000, 50_000), (master.PriceOf(spells[4].ScrollValue), grand.PriceOf(spells[5].ScrollValue)));
        }
    }

    // ------------------------------------------------------------------------------------------------ the learn plan

    /// <summary>The climber of the bot's build (BotBuildTests' Winnie): Focus and Self 100, Item Enchantment trained with <paramref name="itemRanks"/> ranks. With the bot's level VII Focus, Willpower and mastery up her Item Enchantment stands at 110 + the ranks.</summary>
    private static PlayerQualities Winnie(int itemRanks, int level = 35)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Ints[PlayerQualities.LevelProperty] = level;
        q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 0, 100, 0);
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 0, 100, 0);
        q.Skills[MageTemplate.WarMagic] = new SkillDesc(MageTemplate.WarMagic, 0, 1, 3u, 0, 10u, 0, 0);
        q.Skills[Life] = new SkillDesc(Life, 0, 1, 3u, 0, 10u, 0, 0);
        q.Skills[Creature] = new SkillDesc(Creature, 0, 1, 1u, 0, 0u, 0, 0);
        q.Skills[Item] = new SkillDesc(Item, (ushort)itemRanks, 1, 2u, 0, 0u, 0, 0);
        return q;
    }

    private static Spellbook Book(int level, params string[] lines)
    {
        var book = new Spellbook { IsKnown = true };
        foreach (var line in lines)
        {
            book.Learn(C.LineSpells(line)[level - 1].SpellId);
        }

        return book;
    }

    private static IReadOnlyList<LearnNeed> Needs(PlayerQualities q, Spellbook? book = null, HuntingGround? ground = null) =>
        ScrivenerLearnPlan.Needs(ground ?? HomeOnlyCave, ScrivenerLearnPlan.RungOf(L, Cave, q.Level)!, L.PlanCastChance, q, book ?? new Spellbook { IsKnown = true }, C, L, BuffBotWire.Tusker);

    /// <summary>
    /// The Cave as a Holtburg ground whose trips buy no Gold and no Pyreal Scarab: its profile without the spell components of its town stock (M6 item 14, m6-goldscarab: the real Cave buys both at Uziz on the way out, so it reaches V
    /// and VI: <see cref="The_caves_town_stock_at_Uziz_puts_levels_V_and_VI_in_reach"/>). The Temple 20-40, whose way out passes no Town Network, is such a ground today.
    /// </summary>
    private static HuntingGround HomeOnlyCave
    {
        get
        {
            var cave = GroundCatalog.Get(Cave);
            return cave with { TownStock = [.. cave.TownStock.Where(l => !Lifestone.Data.Magic.ComponentWeenies.TryGetByWcid(l.Wcid, out _))] };
        }
    }

    /// <summary>The Cave with its Holtburg archmage also selling the scarabs named (no ground of today sells the Gold or the Pyreal Scarab at home: a ground that will is this one).</summary>
    private static HuntingGround CaveSelling(params (uint Wcid, string Name)[] scarabs)
    {
        var cave = HomeOnlyCave;
        return cave with { Vendors = [.. cave.Vendors.Select(v => v.Wcid == 2304 ? v with { Sells = [.. v.Sells, .. scarabs.Select(x => new VendorWare(x.Wcid, x.Name, 675))] } : v)] };
    }

    private static readonly (uint, string) Gold = (687, "Gold Scarab"), Pyreal = (690, "Pyreal Scarab");

    [Fact]
    public void On_a_Holtburg_ground_the_plan_buys_each_of_the_eight_at_IV_because_level_V_burns_a_Gold_Scarab_her_trips_do_not_sell()
    {
        // Item Enchantment stands at 230 with the buff bot's lines up (110 + 120 ranks): the buff bar 0.75 is the power + 16, so level V (power 200) at 216 and VI (250) at 266.
        var q = Winnie(itemRanks: 120);
        Assert.Equal(230u, BuffReach.Walk(q, L.PlanBuffChance, static (_, _) => false, bot: true)!.Item);
        var rung = ScrivenerLearnPlan.RungOf(L, Cave, 35)!;
        Assert.Equal(Eight, ScrivenerLearnPlan.LinesFor(rung, C).Where(l => l.Kind == ScrivenerLineKind.ItemArmor).Select(l => l.Name));

        // What each level burns on the foci formula is its scarab: Holtburg's archmage sells Copper and Silver to the Cave's home trips, no vendor of them the Gold or the Pyreal Scarab.
        var cave = HomeOnlyCave;
        Assert.Equal((4, "Gold Scarab 687"), (ScrivenerLearnPlan.ArmorLevelInReach(cave, 5, out var gold), gold));
        Assert.Equal((4, "Pyreal Scarab 690"), (ScrivenerLearnPlan.ArmorLevelInReach(cave, 6, out var pyreal), pyreal));
        Assert.Equal((4, string.Empty), (ScrivenerLearnPlan.ArmorLevelInReach(cave, 4, out var none), none));
        Assert.Equal(3, ScrivenerLearnPlan.ArmorLevelInReach(cave, 3, out _));
        Assert.Equal(2, ScrivenerLearnPlan.ArmorLevelInReach(GroundCatalog.Get("holtburg-outskirts"), 2, out _));
        Assert.Equal(0, ScrivenerLearnPlan.ArmorLevelInReach(cave with { Vendors = [], Scriveners = [] }, 5, out _)); // no scarab on sale at all: nothing is capped on a guess
        Assert.Equal(5, ScrivenerLearnPlan.ArmorLevelInReach(CaveSelling(Gold), 5, out _));
        Assert.Equal(5, ScrivenerLearnPlan.ArmorLevelInReach(CaveSelling(Gold), 6, out _));

        // So with a skill that casts V, the plan's level on the Cave is IV, sold at Holtburg, and the line says why by the scarab's name.
        var needs = Needs(q);
        foreach (var name in Eight)
        {
            var need = Assert.Single(needs, n => n.Line.Name == name); // one scroll a line, never one for every level
            output.WriteLine($"{name}: {need.Kind} {need.Spell?.Name}: {need.Why}");
            Assert.Equal((LearnNeedKind.Due, name + " IV", 4), (need.Kind, need.Spell!.Name, need.Target));
            Assert.Contains("ItemEnchantment skill 230", need.Why, StringComparison.Ordinal);
            Assert.Contains("plan chance 0.75: level V at the chance", need.Why, StringComparison.Ordinal);
            Assert.Contains("sold at Holtburg", need.Why, StringComparison.Ordinal);
            Assert.EndsWith("; level V burns a Gold Scarab 687 no vendor of this ground's trips sells, so she could not cast it: the plan's level is IV (TODO(m6-armor-scarab-reach))", need.Why, StringComparison.Ordinal);
        }

        // IV in the book: the plan is met; no level V scroll is fetched for a spell she could not cast.
        Assert.All(Eight, name => Assert.Equal((LearnNeedKind.Known, 4), (Needs(q, Book(4, Eight)).Single(n => n.Line.Name == name).Kind, Needs(q, Book(4, Eight)).Single(n => n.Line.Name == name).Target)));
        Assert.DoesNotContain(Needs(Winnie(itemRanks: 200), Book(4, Eight)), n => n.Line.Kind == ScrivenerLineKind.ItemArmor && n.Kind != LearnNeedKind.Known); // nor VI at 310
        Assert.All(Eight, name => Assert.Equal((LearnNeedKind.Due, 4), (Needs(q, Book(3, Eight)).Single(n => n.Line.Name == name).Kind, Needs(q, Book(3, Eight)).Single(n => n.Line.Name == name).Target))); // a lower level held never stands in
    }

    /// <summary>
    /// M6 item 14 (m6-goldscarab): the Cave's town stock buys the Gold and the Pyreal Scarab at Uziz on the way out, so both are in her trips' reach and the cap by the scarab is gone on the real profile: with IV in the book and a skill
    /// that casts V the plan's level is V, the Town Network's, and no line names <c>TODO(m6-armor-scarab-reach)</c>; a ground with no such town stock (the Temple 20-40) keeps the cap.
    /// </summary>
    [Fact]
    public void The_caves_town_stock_at_Uziz_puts_levels_V_and_VI_in_reach()
    {
        var cave = GroundCatalog.Get(Cave);
        Assert.Equal(["Gold Scarab", "Pyreal Scarab"], cave.TownStock.Where(l => l.Town == "Uziz" && !l.FeeOnly).Select(l => l.Name));
        Assert.Equal((5, string.Empty), (ScrivenerLearnPlan.ArmorLevelInReach(cave, 5, out var gold), gold));
        Assert.Equal((6, string.Empty), (ScrivenerLearnPlan.ArmorLevelInReach(cave, 6, out var pyreal), pyreal));
        Assert.Equal((4, "Gold Scarab 687"), (ScrivenerLearnPlan.ArmorLevelInReach(GroundCatalog.Get("tusker-temple-20-40"), 5, out var temple), temple)); // no town stock: its way out passes no Town Network

        var q = Winnie(itemRanks: 120); // Item Enchantment 230 standing: level V at the buff bar
        var first = Needs(q, ground: cave);
        var then = Needs(q, Book(4, Eight), cave);
        foreach (var name in Eight)
        {
            var bare = Assert.Single(first, n => n.Line.Name == name);
            Assert.Equal((LearnNeedKind.Due, name + " IV", 4), (bare.Kind, bare.Spell!.Name, bare.Target)); // the home's IV first, as any line whose level is sold off home
            Assert.EndsWith(OffHomeNote, bare.Why, StringComparison.Ordinal);
            var need = Assert.Single(then, n => n.Line.Name == name);
            Assert.Equal((LearnNeedKind.OffHome, name + " V", 5), (need.Kind, need.Spell!.Name, need.Target));
            Assert.DoesNotContain("m6-armor-scarab-reach", need.Why, StringComparison.Ordinal);
        }

        var six = Needs(Winnie(itemRanks: 156), Book(4, Eight), cave); // 266 standing casts VI
        Assert.All(Eight, name => Assert.Equal((LearnNeedKind.OffHome, name + " VI"), (six.Single(n => n.Line.Name == name).Kind, six.Single(n => n.Line.Name == name).Spell!.Name)));
    }

    [Fact]
    public void Where_the_Gold_Scarab_is_on_sale_level_V_is_the_plans_and_the_homes_IV_is_bought_first()
    {
        var q = Winnie(itemRanks: 120);
        var ground = CaveSelling(Gold);

        // An empty book: V is the plan's level and only the Master Scriveners sell it, so the home's IV is bought first: she is never without a line while Holtburg sells one.
        var needs = Needs(q, ground: ground);
        foreach (var name in Eight)
        {
            var need = Assert.Single(needs, n => n.Line.Name == name);
            Assert.Equal((LearnNeedKind.Due, name + " IV", 4), (need.Kind, need.Spell!.Name, need.Target));
            Assert.EndsWith(OffHomeNote, need.Why, StringComparison.Ordinal);
        }

        // With IV in the book the plan's own answer stands: V off home, the Town Network's (a trip that happens fetches it; it calls none by itself).
        var then = Needs(q, Book(4, Eight), ground);
        foreach (var name in Eight)
        {
            var need = Assert.Single(then, n => n.Line.Name == name);
            Assert.Equal((LearnNeedKind.OffHome, name + " V", 5), (need.Kind, need.Spell!.Name, need.Target));
            Assert.Equal(VTowns, need.Towns);
        }

        Assert.All(Eight, name => Assert.Equal(LearnNeedKind.Known, Needs(q, Book(5, Eight), ground).Single(n => n.Line.Name == name).Kind));

        // 266 standing casts VI: with the Pyreal Scarab on sale too it is the Grand Masters'; with the Gold alone the plan's level is V, by the scarab's name.
        var six = Needs(Winnie(itemRanks: 156), Book(4, Eight), CaveSelling(Gold, Pyreal));
        foreach (var name in Eight)
        {
            var need = Assert.Single(six, n => n.Line.Name == name);
            Assert.Equal((LearnNeedKind.OffHome, name + " VI"), (need.Kind, need.Spell!.Name));
            Assert.Equal(ViTowns, need.Towns);
        }

        var five = Needs(Winnie(itemRanks: 156), Book(4, Eight), ground);
        Assert.All(Eight, name => Assert.Equal((LearnNeedKind.OffHome, 5), (five.Single(n => n.Line.Name == name).Kind, five.Single(n => n.Line.Name == name).Target)));
        Assert.All(Eight, name => Assert.Contains("level VI burns a Pyreal Scarab 690 no vendor of this ground's trips sells", five.Single(n => n.Line.Name == name).Why, StringComparison.Ordinal));
        var bare = Needs(Winnie(itemRanks: 156), ground: CaveSelling(Gold, Pyreal));
        Assert.All(Eight, name => Assert.Equal((LearnNeedKind.Due, 4), (bare.Single(n => n.Line.Name == name).Kind, bare.Single(n => n.Line.Name == name).Target)));
        Assert.All(Eight, name => Assert.Contains("the plan's level VI is sold only at ", bare.Single(n => n.Line.Name == name).Why, StringComparison.Ordinal));
    }

    [Fact]
    public void Under_the_bar_for_V_the_plan_buys_IV_at_Holtburg_with_no_other_town_and_no_scarab_named()
    {
        // 215 standing: one under V's bar (216): IV is the plan's own level, sold at home.
        var four = Needs(Winnie(itemRanks: 105));
        foreach (var name in Eight)
        {
            var need = Assert.Single(four, n => n.Line.Name == name);
            Assert.Equal((LearnNeedKind.Due, name + " IV"), (need.Kind, need.Spell!.Name));
            Assert.Contains("sold at Holtburg", need.Why, StringComparison.Ordinal);
            Assert.DoesNotContain("is sold only at", need.Why, StringComparison.Ordinal);
            Assert.DoesNotContain("burns a", need.Why, StringComparison.Ordinal);
        }

        // 216 is V's bar and 265 is still V (seen where the Gold Scarab is on sale).
        Assert.All(Eight, name => Assert.Equal(5, Needs(Winnie(itemRanks: 106), Book(4, Eight), CaveSelling(Gold, Pyreal)).Single(n => n.Line.Name == name).Target));
        Assert.All(Eight, name => Assert.Equal(5, Needs(Winnie(itemRanks: 155), Book(4, Eight), CaveSelling(Gold, Pyreal)).Single(n => n.Line.Name == name).Target));

        // Item Enchantment untrained: no line of the eight is asked for.
        var untrained = Winnie(itemRanks: 0);
        untrained.Skills[Item] = new SkillDesc(Item, 0, 1, 1u, 0, 0u, 0, 0);
        Assert.All(Eight, name => Assert.Equal(LearnNeedKind.Untrained, Needs(untrained).Single(n => n.Line.Name == name).Kind));
    }

    [Fact]
    public void Under_level_32_the_six_are_named_and_nothing_of_them_is_bought_and_her_two_are_planned_as_before()
    {
        // The catalog's from_level: 32, the first level of the Temple 20-40's rung, whose band ended at the level 40 the owner's order names. Under it the low rungs' coin order and pass are as before.
        // M6 14 (the owner, 2026-11-13): that rung left the ladder; 32 stands (the catalog's data, the lead's or the owner's to move); D135: it is inside the Abode's band, 25 to 41.
        Assert.All(Six, n => Assert.Equal(32, C.Line(n)!.FromLevel));
        Assert.All(C.Lines.Where(l => !Six.Contains(l.Name)), l => Assert.Null(l.FromLevel));
        Assert.Equal(("abode", 25, 41), (L.RungFor(32).Key, L.RungFor(32).Band.From, L.RungFor(32).Band.To));
        Assert.Equal(("cave", 21), (L.RungFor(24).Key, L.RungFor(24).Band.From));

        var early = Needs(Winnie(itemRanks: 120, level: 31));
        foreach (var name in Six)
        {
            var need = Assert.Single(early, n => n.Line.Name == name);
            Assert.Equal((LearnNeedKind.Later, 0), (need.Kind, need.Target));
            Assert.Null(need.Spell);
            Assert.Equal("planned from level 32 (scriveners.json from_level; she is level 31): no scroll of it is bought before", need.Why);
        }

        Assert.All(new[] { "Impenetrability", "Bludgeon Bane" }, name => Assert.Equal((LearnNeedKind.Due, 4), (early.Single(n => n.Line.Name == name).Kind, early.Single(n => n.Line.Name == name).Target)));
        Assert.DoesNotContain(Needs(Winnie(itemRanks: 120, level: 32)), n => n.Kind == LearnNeedKind.Later); // from 32 on: all eight

        // A wrong from_level fails at load with its path.
        var json = System.Text.Json.Nodes.JsonNode.Parse(ResourceText())!;
        json["lines"]!.AsArray().Single(l => (string?)l!["name"] == "Acid Bane")!["from_level"] = 0;
        Assert.Equal("$.lines[54].from_level", Assert.Throws<ScrivenerCatalogException>(() => ScrivenerCatalog.Parse(json.ToJsonString())).JsonPath);
    }

    private static string ResourceText()
    {
        using var stream = typeof(ScrivenerCatalog).Assembly.GetManifestResourceStream(ScrivenerCatalog.ResourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ------------------------------------------------------------------------------------------------ the pass

    private sealed class PassRig
    {
        public PassRig(int level, uint? field = null, uint mana = 400)
        {
            R = new WorldActionRig();
            Known = [.. Eight.Select(n => BuffProfiles.Climber.LineNamed(n)!.Spells[level - 1].Id)];
            Caster = new ScriptedCaster(R, mana, 200, lines: [.. ArenaProfile.Lines, .. ArmourBanes.Lines], known: Known) { ManaRegenPerSecond = 40 };
            Ledger = new ArmourBuffLedger();
            Ledger.MarkSeeded();
            Cycle = new BuffCycle(R.Api, R.H.State, Caster.Table, new BuffOptions { FieldDamageTypes = field }, R.H.Log, Ledger, null, BuffProfiles.Climber);
        }

        public WorldActionRig R { get; }

        public uint[] Known { get; }

        public ScriptedCaster Caster { get; }

        public ArmourBuffLedger Ledger { get; }

        public BuffCycle Cycle { get; }

        public double Now => R.H.R.Clock.Now;

        public IEnumerable<uint> Landed => Caster.Answered.Where(a => !a.Fizzled).Select(a => a.Spell).Where(id => Known.Contains(id));

        public BuffStatus Run(double maxSeconds = 600)
        {
            var end = Now + maxSeconds;
            var status = BuffStatus.Running;
            while (status == BuffStatus.Running && Now < end)
            {
                R.Tick(0.25);
                Caster.Pump();
                R.Tick();
                status = Cycle.Step(Now);
            }

            return status;
        }

        public int Logged(string text) => R.H.Log.Lines.Count(l => l.Contains(text, StringComparison.Ordinal));
    }

    [Fact]
    public void A_pass_at_home_casts_all_eight_armour_lines_at_her_own_guid_and_the_armour_memory_keeps_each_on_every_piece()
    {
        var t = new PassRig(level: 4);
        Assert.Equal(8, t.Cycle.Wanted);
        Assert.Equal(Eight, t.Cycle.LineOrder);
        Assert.Equal(Eight, t.Cycle.RebuffDue(t.Now, 300)); // nothing is on yet: every line is due

        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        var pass = t.Cycle.Result!;
        output.WriteLine(pass.Fields);
        Assert.Null(pass.Failure);
        Assert.Equal((8, 8, 8, 8), (pass.Wanted, pass.Active, pass.Cast, pass.ArmourCast));
        Assert.Empty(pass.Misses);
        Assert.Equal("Impen:IV,BludB:IV,PierceB:IV,BladeB:IV,LightB:IV,FlameB:IV,FrostB:IV,AcidB:IV", pass.Levels);
        Assert.Equal(t.Known, t.Landed); // one cast a line, in the pass's order, each at the caster (the scripted caster asserts the target is the player)

        // Each line is on every worn piece, ending 1,800 s after its cast: the level IV duration.
        var worn = t.R.H.State.Self.Inventory.Equipped.Where(e => t.R.H.State.Objects.TryGet(e.Guid, out var o) && (o.ItemType & 0x6) != 0).Select(e => e.Guid).ToList();
        Assert.Equal(ScriptedCaster.PieceNames.Length, worn.Count);
        for (var i = 0; i < Eight.Length; i++)
        {
            foreach (var piece in worn)
            {
                Assert.True(t.Ledger.TryGet(piece, Eight[i], out var entry), Eight[i]);
                Assert.Equal(t.Known[i], entry.SpellId);
                Assert.InRange(entry.ExpiresAt - t.Now, 1700, 1800);
            }

            Assert.Equal(4, t.Cycle.ActiveLevel(Eight[i], t.Now));
            Assert.Equal(1, t.Logged($"{Eight[i]}: {ScriptedCaster.PieceNames.Length} pieces enchanted, each ends 1800 s after the cast's UseDone"));
        }

        // Kept up as her two always were: none due while they stand, all eight due together under the margin, and a second pass casts nothing while they stand.
        Assert.Empty(t.Cycle.RebuffDue(t.Now, 300));
        Assert.Equal(Eight, t.Cycle.RebuffDue(t.Now + 1600, 300));
        var before = t.Landed.Count();
        Assert.True(t.Cycle.StartRebuff(t.Now, forced: false));
        Assert.Equal(BuffStatus.Done, t.Run());
        Assert.Equal(before, t.Landed.Count());
    }

    [Fact]
    public void A_cycle_in_the_field_wants_her_two_lines_and_the_banes_of_the_damage_dealt_there_and_leaves_the_rest_to_the_pass_at_home()
    {
        // The pass at home put all eight on; in a tusker ground (bludgeon, 0x4) the same cycle reads and keeps her two alone.
        var t = new PassRig(level: 4);
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        Assert.Equal(8, t.Cycle.Result!.ArmourCast);

        t.Cycle.FieldDamageTypes = 0x4;
        Assert.Equal(2, t.Cycle.Wanted);
        Assert.Equal(["Impenetrability", "Bludgeon Bane"], t.Cycle.LineOrder);
        var lines = new List<BuffCycle.LineState>();
        t.Cycle.ReadLines(t.Now, lines);
        Assert.Equal(["Impenetrability", "Bludgeon Bane"], lines.Select(l => l.Line.Name)); // what the fight's refresh reads: the six are neither recast nor counted as run out
        Assert.Equal(["Impenetrability", "Bludgeon Bane"], t.Cycle.RebuffDue(t.Now + 1600, 300)); // and what calls a refresh
        Assert.Equal(["Impenetrability", "Bludgeon Bane"], t.Cycle.RebuffDue(t.Now + 4000, 300)); // lapsed: the six still call nothing
        Assert.False(t.Cycle.StartRebuffLine(t.Now, "Piercing Bane")); // the fight's one-line pass refuses a line the field does not want
        Assert.Equal(1, t.Logged("Piercing Bane: Piercing Bane IV (1572) is in the spellbook; in the field (the ground's creatures deal damage types 0x4, not its 0x2) it is not cast and not kept up: the pass at home casts it (m6-banes)"));
        Assert.Equal(6, t.Logged("the pass at home casts it (m6-banes)")); // each of the six once, however often the lines are read

        // A ground whose targets deal piercing and electric damage too: those two banes are the field's as Bludgeon Bane is.
        t.Cycle.FieldDamageTypes = 0x4 | 0x2 | 0x40;
        Assert.Equal(["Impenetrability", "Bludgeon Bane", "Piercing Bane", "Lightning Bane"], t.Cycle.LineOrder);
        Assert.Equal(["Impenetrability", "Bludgeon Bane", "Piercing Bane", "Lightning Bane"], t.Cycle.RebuffDue(t.Now + 1600, 300));

        // Home again: all eight are read, and lapsed ones are due, so the pass before she leaves casts them.
        t.Cycle.FieldDamageTypes = null;
        Assert.Equal(Eight, t.Cycle.LineOrder);
        Assert.Equal(Eight, t.Cycle.RebuffDue(t.Now + 1600, 300));
    }

    [Fact]
    public void A_pass_in_the_ground_casts_her_two_lines_and_no_bane_of_damage_the_ground_does_not_deal()
    {
        var t = new PassRig(level: 4, field: ArmourBanes.FieldMask(GroundCatalog.Get(Cave)));
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        var pass = t.Cycle.Result!;
        Assert.Equal((2, 2, 2), (pass.Wanted, pass.Active, pass.ArmourCast));
        Assert.Equal("Impen:IV,BludB:IV", pass.Levels);
        Assert.Equal(t.Known.Take(2), t.Landed);
        Assert.Empty(pass.Misses); // not wanted there: no cast, no miss
    }

    [Fact]
    public void The_pass_casts_the_level_the_book_holds_and_the_bar_reaches_V_once_it_is_read()
    {
        // Level V of every line in the book (the Master Scriveners' scrolls): the scripted mage's Item Enchantment casts it at the buff bar, 1,800 s as IV.
        var t = new PassRig(level: 5, mana: 600);
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        var pass = t.Cycle.Result!;
        output.WriteLine(pass.Fields);
        Assert.Equal((8, 8, 8), (pass.Wanted, pass.Active, pass.ArmourCast));
        Assert.Equal("Impen:V,BludB:V,PierceB:V,BladeB:V,LightB:V,FlameB:V,FrostB:V,AcidB:V", pass.Levels);
    }

    [Fact]
    public void The_autopilots_pass_at_home_is_the_whole_pass_and_its_passes_in_the_ground_are_the_fields()
    {
        // The home pass (after the buff bot's stop, before she leaves) carries no field argument: every armour line the book holds. The upkeep's pass runs in the ground: the field's.
        Assert.False(Lifestone.Client.Session.Autopilot.AutopilotScenario.BuffSpec.Arguments.ContainsKey(JourneyStations.FieldArgument));
        Assert.True(Lifestone.Client.Session.Autopilot.AutopilotScenario.UpkeepSpec.Arguments.ContainsKey(JourneyStations.FieldArgument));
        Assert.True(Lifestone.Client.Session.Autopilot.AutopilotScenario.UpkeepSpec.Arguments.ContainsKey(JourneyStations.UnattendedArgument)); // as before

        // The station turns the argument into the damage types its ground's targets deal (the pass in the ground is AutopilotTests.AssertGroundPass's).
        var cave = GroundCatalog.Get(Cave);
        var field = new StationSpec("buff", StationKinds.Buff, null, 200, new Dictionary<string, string> { [JourneyStations.FieldArgument] = "yes" });
        Assert.Equal(0x4u, JourneyStations.FieldOptions(field, new BuffOptions(), cave).FieldDamageTypes);
        Assert.Equal(0x1Cu, JourneyStations.FieldOptions(field, new BuffOptions(), GroundCatalog.Get("crater-caves")).FieldDamageTypes);
        Assert.Equal(0u, JourneyStations.FieldOptions(field, new BuffOptions(), GroundCatalog.Get("holtburg-outskirts")).FieldDamageTypes); // a field with no damage type named: none of the six, never "home"
        Assert.Null(JourneyStations.FieldOptions(Lifestone.Client.Session.Autopilot.AutopilotScenario.BuffSpec, new BuffOptions(), cave).FieldDamageTypes);
        Assert.Null(new BuffOptions().FieldDamageTypes);
    }

    // ------------------------------------------------------------------------------------------------ the fight's refresh

    private static FightManaTests.Rig FightRig(double armourLeft, HuntingGround? ground = null)
    {
        var fight = new FightOptions { BuffProfile = BuffProfiles.Climber };
        if (ground is not null)
        {
            fight = fight with { GroundForTest = ground };
        }

        var r = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true, fight: fight, lines: [.. ArenaProfile.Lines, .. ArmourBanes.Lines]) { HitsToKill = 400 };
        r.Caster.ManaRegenPerSecond = 40;
        r.Caster.HealthPerCast = 20;
        r.AddTusker(12); // inside the reach, alive for the whole test: no lull comes
        foreach (var spell in ArmourBanes.Lines.Select(l => l.Spells[3]))
        {
            r.R.H.State.Self.Spellbook.Learn(spell.Id); // the six at IV, read from Holtburg's scrolls
        }

        FightF24Tests.Prime(r, l => l.Role == ProfileRole.ItemBuffArmor ? armourLeft : 1500);
        var ledger = ArmourBuffLedger.For(r.R.H.State);
        var equipped = r.R.H.State.Self.Inventory.Equipped;
        foreach (var bane in ArmourBanes.Lines)
        {
            for (var i = 0; i < equipped.Count; i++)
            {
                ledger.Record(equipped[i].Guid, bane.Name, bane.Spells[3].Id, r.Now, armourLeft); // the pass at home cast all eight together: they come due together
            }
        }

        r.R.Tick();
        return r;
    }

    private static List<string> ArmourCasts(FightManaTests.Rig r) =>
        [.. r.Caster.Answered.Select(a => BuffProfiles.Climber.BuffOrder.FirstOrDefault(l => l.Role == ProfileRole.ItemBuffArmor && l.Spells.Any(s => s.Id == a.Spell))?.Name).OfType<string>()];

    [Fact]
    public void Under_fire_the_fight_recasts_Impenetrability_and_Bludgeon_Bane_under_the_hard_floor_and_none_of_the_six()
    {
        // All eight were cast together at home and have 25 s left, under the 30 s hard floor, with a tusker in reach. The den's targets name no damage type; a tusker ground names bludgeon: either way the six are not the field's.
        var r = FightRig(armourLeft: 25);
        var scenario = r.Scenario();
        scenario.RequestBuffRefresh(300, 20, "a line under the 300 s margin");
        r.Run(scenario, 60, 0.1); // well past the six lines' end

        foreach (var line in r.Log.Lines.Where(l => l.Contains("refresh", StringComparison.Ordinal) || l.Contains("m6-banes", StringComparison.Ordinal)).Take(40))
        {
            output.WriteLine(line);
        }

        Assert.Equal(["Impenetrability", "Bludgeon Bane"], ArmourCasts(r));
        Assert.Contains(r.Log.Lines, l => l.Contains("buff refresh: hard: Impenetrability has 2", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("buff refresh: hard: Bludgeon Bane has ", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Log.Lines, l => l.Contains("buff refresh:", StringComparison.Ordinal) && ArmourBanes.Names.Any(n => l.Contains(n, StringComparison.Ordinal))); // no recast, no "EXPIRED under fire" for a line that is the home pass's
        Assert.Equal(0, scenario.BuffsExpired);
        Assert.Equal(6, r.Log.Lines.Count(l => l.Contains("the pass at home casts it (m6-banes)", StringComparison.Ordinal)));
        Assert.True(r.BoltAnswers.Any(), "the fight went on casting at the tusker");
        Assert.Null(scenario.StopReason);
    }

    [Fact]
    public void Where_the_grounds_targets_deal_piercing_damage_the_fight_keeps_Piercing_Bane_up_as_it_keeps_Bludgeon_Bane()
    {
        var den = GroundCatalog.Get(FightOptions.DefaultGround);
        var pierce = den with { Targets = [.. den.Targets.Select(t => t.Role == GroundRole.Target ? t with { DamageTypes = 0x4 | 0x2 } : t)] };
        Assert.Equal(0x6u, ArmourBanes.FieldMask(pierce));
        var r = FightRig(armourLeft: 25, pierce);
        var scenario = r.Scenario();
        scenario.RequestBuffRefresh(300, 20, "a line under the 300 s margin");
        r.Run(scenario, 60, 0.1);

        Assert.Equal(["Impenetrability", "Bludgeon Bane", "Piercing Bane"], ArmourCasts(r)); // the protections' order is the pass's own
        Assert.Contains(r.Log.Lines, l => l.Contains("buff refresh: hard: Piercing Bane has ", StringComparison.Ordinal));
        Assert.Equal(5, r.Log.Lines.Count(l => l.Contains("the pass at home casts it (m6-banes)", StringComparison.Ordinal)));
        Assert.Equal(0, scenario.BuffsExpired);
    }
}

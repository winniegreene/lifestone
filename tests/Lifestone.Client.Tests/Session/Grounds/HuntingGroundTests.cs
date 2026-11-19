using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using System.Text.Json.Nodes;
using Lifestone.Client.Interiors;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// The hunting-ground profile (M5 item 2 g1; DoD 4, D22, D43): the den's profile against today's values, written here as literal data (item 2 g2b removed the den's constants from the code, so these literals are the
/// only record of them); since item 2 g2 the den is the whole den (D39): its storeys, no-go cells, slots
/// and tour are the draft's (GroundDraft) plus the hand-set fields, and the z 0 storey still holds today's floor; a profile with a missing, unknown or out-of-range field cannot load.
/// </summary>
public sealed class HuntingGroundTests
{
    private const string DenKey = "black-spawn-den-aluvian";
    private const string DenFile = DenKey + ".json";

    /// <summary>Today's floor (BlackSpawnDen.FloorLowWords at m5/2 8e6bdea1), as literal low words of landblock 0x0105.</summary>
    private static readonly ushort[] FloorLiteral =
    [
        0x0375,
        0x02E1, 0x02E2, 0x0303, 0x030D, 0x0310, 0x0320, 0x0325, 0x0338, 0x033F, 0x036F, 0x0377, 0x0383,
        0x02D7, 0x02D8, 0x02D9, 0x02DA, 0x02DB, 0x02E0, 0x02E3, 0x02E4, 0x02E5, 0x02E6, 0x02E7, 0x02E8, 0x02E9,
        0x02EA, 0x02EB, 0x02EC, 0x02ED, 0x02EE, 0x02EF, 0x02F0, 0x02F1, 0x02F2, 0x02F7, 0x02F8, 0x02F9, 0x02FA,
        0x02FB, 0x02FC, 0x02FD, 0x02FE, 0x02FF, 0x0300, 0x0301, 0x0302, 0x0304, 0x0305, 0x0306, 0x0307, 0x0308,
        0x030E, 0x030F, 0x0311, 0x0312, 0x0313, 0x0314, 0x0315, 0x0316, 0x0317, 0x0318, 0x0319, 0x031A, 0x031B,
        0x031C, 0x031D, 0x031E, 0x031F, 0x0321, 0x0322, 0x0323, 0x0324, 0x0326, 0x0327, 0x0328, 0x0329, 0x032A,
        0x032B, 0x032C, 0x032D, 0x032E, 0x032F, 0x0330, 0x0331, 0x0332, 0x0333, 0x0334, 0x0335, 0x0336, 0x0337,
        0x0339, 0x033A, 0x033B, 0x033C, 0x033D, 0x033E, 0x0340, 0x0344, 0x0345, 0x0346, 0x0347, 0x0348, 0x0349,
        0x034A, 0x034B, 0x034C, 0x034D, 0x034E, 0x034F, 0x0350, 0x0351, 0x0352, 0x0353, 0x0354, 0x0355, 0x0356,
        0x0357, 0x0358, 0x0359, 0x035A, 0x035B, 0x035D, 0x035E, 0x035F, 0x0360, 0x0361, 0x0362, 0x0363, 0x0364,
        0x0365, 0x0366, 0x0367, 0x0368, 0x0369, 0x036A, 0x036B, 0x036C, 0x036D, 0x036E, 0x0370, 0x0371, 0x0372,
        0x0373, 0x0374, 0x0376, 0x0378, 0x0379, 0x037A, 0x037B, 0x037C, 0x037D, 0x037E, 0x037F, 0x0380, 0x0381,
        0x0382, 0x0384,
    ];

    /// <summary>Today's tour (GroundWalk.Tour), literal.</summary>
    private static readonly ushort[] TourLiteral = [0x0383, 0x0377, 0x036F, 0x033F, 0x0338, 0x0320, 0x0303, 0x0310, 0x030D, 0x0325, 0x02E2, 0x02E1];

    private static HuntingGround Den => GroundCatalog.Get(DenKey);

    /// <summary>Today's no-go low words (BlackSpawnDen.IsExcluded), literal: the two wing ranges, the four outliers and the exit portal's room.</summary>
    private static IEnumerable<uint> NoGoLiteral() =>
        Range(0x0100, 0x0121).Concat(Range(0x016A, 0x018B)).Concat(new uint[] { 0x0244, 0x02D3, 0x02D5, 0x02D6 }).Concat(Range(0x0341, 0x0343)).Append(0x035Cu);

    private static IEnumerable<uint> Range(uint from, uint to)
    {
        for (var w = from; w <= to; w++)
        {
            yield return w;
        }
    }

    [Fact]
    public void The_den_loads_by_key_as_a_dungeon_of_landblock_0105_with_every_section_sourced()
    {
        var den = Den;
        Assert.Equal(DenKey, den.Key);
        Assert.Equal("Black Spawn Den (Aluvian)", den.Name);
        Assert.Equal(GroundKind.Dungeon, den.Kind);
        Assert.Equal([(ushort)0x0105], den.Landblocks);
        Assert.Contains(den, GroundCatalog.All);
        Assert.Equal(HuntingGround.SectionNames.Order(), den.Sources.Keys.Order());
        Assert.All(den.Sources.Values, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        Assert.Null(den.Outdoor);
    }

    [Fact]
    public void The_storeys_are_the_draft_s_six_z_bands_and_z0_holds_every_one_of_today_s_158_floor_cells()
    {
        var den = Den;
        Assert.Equal(["z 0", "z -6", "z -12", "z -18", "z -24", "z -30"], den.Storeys.Select(s => s.Name));
        Assert.Equal([0f, -6f, -12f, -18f, -24f, -30f], den.Storeys.Select(s => s.ZMin));
        Assert.All(den.Storeys, s => Assert.Equal(s.ZMin, s.ZMax)); // a band is one origin z
        Assert.Equal([161, 96, 25, 149, 117, 19], den.Storeys.Select(s => s.Cells.Count)); // 567 of the 653 cells (M5 item 2 g2, GroundDraft.Cells)
        Assert.Equal(567, den.Storeys.Sum(s => s.Cells.Count));
        foreach (var low in FloorLiteral)
        {
            Assert.True(den.Storeys[0].Cells.Contains(0x01050000u | low), $"today's floor cell 0x{low:X4} is in storey z 0");
            Assert.True(den.IsWalkable(0x01050000u | low), $"today's floor cell 0x{low:X4} is walkable");
        }

        Assert.All(FloorLiteral, low => Assert.Equal(0, den.StoreyOf(0x01050000u | low)));
        Assert.Equal(-1, den.StoreyOf(0xA9B40019));
        Assert.Equal(-1, den.StoreyOf(0x010501D8)); // an unreached pocket
    }

    /// <summary>D39's no-go cells, literal: the wing by its geometry (52), the outliers (4), the exits (0x0279; 0x0341-0x0343, 0x035C) and the 13 cells they cut off.</summary>
    private static IEnumerable<uint> D39Literal() =>
        Range(0x0100, 0x010D).Concat(Range(0x010F, 0x0117)).Concat(Range(0x0119, 0x0121)).Concat(Range(0x0138, 0x013D)).Concat(Range(0x0168, 0x0170)).Concat(Range(0x0187, 0x018B))
            .Concat(new uint[] { 0x0244, 0x02D3, 0x02D5, 0x02D6, 0x0279 }).Concat(Range(0x0341, 0x0343)).Append(0x035Cu)
            .Concat(new uint[] { 0x01DC, 0x0240, 0x026A, 0x026B, 0x026C }).Concat(Range(0x0273, 0x0278)).Concat(new uint[] { 0x027B, 0x02D4 });

    [Fact]
    public void The_no_go_cells_are_D39_s_and_today_s_exclusions_are_no_go_or_dropped_by_D39_by_name()
    {
        var den = Den;
        Assert.Equal(D39Literal().Select(w => 0x01050000u | w).Order(), den.NoGo.Cells);
        Assert.Equal(74, den.NoGo.Count);
        Assert.Equal(52, D39Literal().Count(w => w is >= 0x0100 and <= 0x018B));

        // D39 drops these of today's exclusions: the south z -24 area, which today's ranges swallowed (its guard cells 0x0118, 0x0171-0x0176 and 0x0182, and the rest of 0x0171-0x0186),
        // and the wing's south doorstep 0x010E at z -30 (outside the wing by geometry; no slot).
        uint[] dropped = [0x010E, 0x0118, .. Range(0x0171, 0x0186)];
        Assert.Equal(dropped, NoGoLiteral().Where(w => !den.IsNoGo(0x01050000u | w)).Order());
        foreach (var low in dropped)
        {
            var cell = 0x01050000u | low;
            Assert.True(den.IsWalkable(cell), $"0x{low:X4} is walkable under D39");
            Assert.Equal(low == 0x010E ? 5 : 4, den.StoreyOf(cell));
        }

        foreach (var low in NoGoLiteral().Except(dropped))
        {
            Assert.True(den.IsNoGo(0x01050000u | low), $"today's exclusion 0x{low:X4} stays no-go");
        }

        for (uint low = 0; low <= 0xFFFF; low++)
        {
            var cell = 0x01050000u | low;
            Assert.True(den.IsWalkable(cell) == (den.StoreyOf(cell) >= 0 && !den.IsNoGo(cell)), $"walkable 0x{cell:X8}");
            if (FloorLiteral.Contains((ushort)low))
            {
                Assert.True(den.IsWalkable(cell), $"today's floor 0x{cell:X8} is walkable");
            }
        }

        Assert.Equal(499, den.Walkable.Count); // 567 storey cells less the 68 no-go cells among them (the wing's five floorless ramp cells and 0x0342 are in no storey)
        Assert.False(den.IsWalkable(0x01060375));
        Assert.False(den.IsNoGo(0x01060100));
    }

    [Fact]
    public void The_arrival_is_today_s_arrival_and_the_places_row()
    {
        var arrival = Den.Arrival;
        Assert.Equal(0x01050375u, arrival.Cell);
        Assert.Equal(new Vector3(100f, -210f, 0f), arrival.Origin);
        Assert.Equal(0.707107f, arrival.HeadingW);
        Assert.Equal(-0.707107f, arrival.HeadingZ);
        Assert.Equal(new CellFrame(0x01050375u, new AcFrame(new Vector3(100f, -210f, 0f), new Quaternion(0f, 0f, -0.707107f, 0.707107f))), arrival.Frame);
        Assert.Equal(InteriorLocations.BlackSpawnDenAluvian.CellId, arrival.Cell);
        Assert.Equal(InteriorLocations.BlackSpawnDenAluvian.Location.Position.Local, arrival.Origin);
    }

    [Fact]
    public void The_targets_are_the_tuskers_and_the_wing_s_creatures_are_avoided()
    {
        var den = Den;
        Assert.Equal(
            [(1628u, "Tusker Slave", 80), (1629u, "Tusker Guard", 80)],
            den.Targets.Where(t => t.Role == GroundRole.Target).Select(t => (t.Wcid, t.Name, t.Level)));
        Assert.Equal(
            [(23u, "Virindi Servant", 40), (237u, "Virindi Master", 50), (1609u, "Drudge Stalker", 50), (1610u, "Drudge Ravener", 80)],
            den.Targets.Where(t => t.Role == GroundRole.Avoid).Select(t => (t.Wcid, t.Name, t.Level)));
        Assert.Equal([1628u, 1629u], den.Targets.Where(t => t.Role == GroundRole.Target).Select(t => t.Wcid)); // the tuskers (the former ArenaProfile.TargetWcids)
        Assert.Equal(GroundRole.Target, den.RoleOf(1628));
        Assert.Equal(GroundRole.Avoid, den.RoleOf(1610));
        Assert.Null(den.RoleOf(3955));
        Assert.Equal(new LevelBand(80, 80), den.LevelBand);
    }

    /// <summary>The den's first-draft tour (M5 item 2 g2, GroundDraft.FirstTour), literal: the 57 cells of the 78 target slots, storey by storey.</summary>
    private static readonly ushort[] DraftTourLiteral =
    [
        0x0383, 0x0377, 0x036F, 0x0338, 0x0320, 0x0303, 0x030D, 0x0310, 0x0325, 0x02E1, 0x02E2, 0x033F,
        0x0281, 0x0280, 0x02A0, 0x02A1, 0x02BE, 0x02D1, 0x02D2, 0x02B6, 0x0263,
        0x0243, 0x024C, 0x0245, 0x023A,
        0x0191, 0x0192, 0x0193, 0x01C0, 0x01C3, 0x01AB, 0x0196, 0x01BA, 0x01F0, 0x0202, 0x01EE, 0x0221, 0x020B, 0x022C, 0x0218, 0x0219,
        0x0182, 0x0176, 0x0157, 0x0159, 0x0127, 0x0125, 0x0150, 0x0124, 0x0123, 0x0146, 0x0172, 0x0171, 0x0122, 0x0173, 0x0174, 0x0118,
    ];

    [Fact]
    public void The_tour_is_the_draft_s_storey_by_storey_and_its_z0_leg_is_today_s_twelve_spawn_cells()
    {
        var den = Den;
        Assert.Equal(DraftTourLiteral.Select(w => 0x01050000u | w), den.Tour);
        Assert.Equal(TourLiteral.Order(), DraftTourLiteral.Take(12).Order());
        Assert.Equal([12, 9, 4, 16, 16], den.Tour.GroupBy(den.StoreyOf).Select(g => g.Count()));
        Assert.Equal(Enumerable.Range(0, 5), den.Tour.Select(den.StoreyOf).Distinct()); // down, storey by storey
        Assert.All(den.Tour, c => Assert.True(den.IsWalkable(c)));
    }

    [Fact]
    public void The_portal_the_home_and_the_vendors_are_the_measured_values()
    {
        var den = Den;
        Assert.Equal(new GroundPortal(0x71B90000, 0x1B90002C, 47, 48, 7242, new Vector3(122.404f, 87.43f, 29.937f), 0x1B90002C, new Vector3(122.40f, 84.43f, 30.01f)), den.Portal);
        Assert.Equal(new GroundHome("Holtburg", 0xA9B40019, new Vector3(84f, 7.7f, 94f)), den.Home);

        Assert.Equal([718u, 2304u, 715u, 719u, 716u, 714u, 20214u], den.Vendors.Select(v => v.Wcid)); // M5 7 F13: Ecutha the Tailor, the hooded robe's vendor, last; M5 7 F28: Monyra the Jeweler last; M5 7 P7j: the Scrivener of Item Magic, the learn list's; M6 14 grocer: Fispur Ansel the Grocer, the Empty Flask's buyer, after Monyra
        var scrivener = den.VendorOf(20214)!;
        Assert.Equal((0x7A9B301Bu, 0xA9B3003Cu, 0xA9B3003Cu, 0u), (scrivener.Guid, scrivener.Cell, scrivener.StopCell, scrivener.BuysItemTypes)); // outdoors, south of Holtburg; she buys nothing here (no trip sells to her)
        Assert.Equal([(28005u, "Aura of Spirit Drinker Self IV", 5000)], scrivener.Sells.Select(w => (w.Wcid, w.Name, w.Price))); // ACE: ceil(SellPrice 50 x Value 100 - 0.1)
        Assert.Equal([new LearnLine(3256, "Aura of Spirit Drinker Self IV", 28005, 20214)], den.Learn);
        var renald = den.VendorOf(718)!;
        Assert.Equal(0xA9B4013Fu, renald.StopCell);
        Assert.Equal(0xA9B40141u, renald.Cell);
        Assert.Equal(0x480467A7u, renald.BuysItemTypes);
        Assert.Equal([new VendorWare(56, "Leather Gauntlets", 1485), new VendorWare(136, "Pack", 88)], renald.Sells); // M5 7 F13 (D87): the kit's gloves; M6 8 D: the side pack a short layout buys, ceil(1.35 x 65 - 0.1)
        var cindrue = den.VendorOf(2304)!;
        Assert.Equal(0x000CB000u, cindrue.BuysItemTypes);
        Assert.True(cindrue.Buys(0x1000)); // SpellComponents
        Assert.False(cindrue.Buys(0x1)); // MeleeWeapon
        Assert.Equal([new VendorWare(20631, "Prismatic Taper", 30), new VendorWare(686, "Copper Scarab", 135), new VendorWare(688, "Silver Scarab", 338), new VendorWare(691, "Lead Scarab", 14), new VendorWare(2472, "Wand", 270), new VendorWare(20630, "Trade Note (250,000)", 287500),
            // M6 sprint (2026-11-18): the five components the learn step named short (wares only, rows 21033, 20980, 20982, 21003 and 21007 of her create list), then Lifestone Recall's other four on the account formula (rows 21029, 21017, 21041 and 21044)
            new VendorWare(746, "Hazel Talisman", 7), new VendorWare(767, "Comfrey", 14), new VendorWare(769, "Dragonsblood", 14), new VendorWare(788, "Powdered Malachite", 7), new VendorWare(792, "Powdered Turquoise", 7),
            new VendorWare(750, "Rowan Talisman", 7), new VendorWare(762, "Turpeth", 7), new VendorWare(1647, "Indigo Taper", 34), new VendorWare(1652, "White Taper", 34)], cindrue.Sells); // M5 7 G1: the largest trade note, which the sell trip buys with the coin above the purse's coin (1.15 x 250,000) // the taper, the travel spells' Copper Scarab (M5 3 m1), then the kit's wand (M5 3 m0; her robe 6071 left the kit, M5 7 F13)
        var ecutha = den.VendorOf(719)!;
        Assert.Equal([new VendorWare(5851, "Faran Robe with Hood", 68)], ecutha.Sells); // M5 7 F13 (D87): 1.35 x Value 50, ceil(67.4)
        Assert.Equal((0xA9B4012Au, 0xA9B4012Au), (ecutha.Cell, ecutha.StopCell));
        var thelnoth = den.VendorOf(715)!;
        Assert.Equal(0x00044080u, thelnoth.BuysItemTypes);
        Assert.Equal(0xA9B40111u, thelnoth.StopCell);
        Assert.Equal([new VendorWare(379, "Mana Potion", 230)], thelnoth.Sells);
        Assert.Null(den.VendorOf(712));
    }

    [Fact]
    public void The_dens_loot_setting_is_a_floor_of_ten_and_the_Jeweler_buys_the_jewellery_and_gems_the_floor_keeps_M5_7_F28()
    {
        var den = Den;
        // M5 7 F29 (D96): the owner's rule: a payout of 4,000, 60 Value per burden unit, no item over 150 burden, a trip every two buff cycles, an early one under five free slots, the load line at 85%
        Assert.Equal(new GroundLoot(60) { MinPayout = 4000, MaxItemBurden = 150, TripEveryBuffCycles = 2, MinFreeSlots = 5, LoadLinePercent = 85, KeepAside = den.Loot!.KeepAside }, den.Loot);
        Assert.Equal(3.0, den.Loot.KeepAside!.MinWarBonusPercent); // M5 7 F30 (the owner): the wands at +3 percent and the cantrip pieces kept aside and never sold
        Assert.Equal(["Major", "Epic", "Legendary"], den.Loot.KeepAside.Tiers);
        Assert.Equal(["War Magic Aptitude", "Life Magic Aptitude", "Creature Enchantment Aptitude", "Item Enchantment Aptitude", "Focus", "Willpower", "Mana Conversion Prowess", "Arcane Prowess", "Magic Resistance", "Invulnerability", "Impregnability"], den.Loot.KeepAside.Cantrips);
        Assert.All(den.Vendors.Where(v => v.BuysItemTypes != 0), v => Assert.Equal(0.9, v.BuyPrice)); // the world SQL: every Holtburg vendor of the profile pays 0.9 (float property 37)
        Assert.Equal(den.Vendors.Count(v => v.BuysItemTypes != 0), den.LootBuyers.Count); // M5 7 P7j: the scrivener (mask 0) buys nothing here
        Assert.Equal(1.0, GroundVendor.DefaultBuyPrice);
        Assert.Equal(GroundLoot.DefaultMinValuePerBurden, 1.0);
        Assert.Null(GroundCatalog.Get("low-b-belt").Loot); // a ground without the section: today's floor

        var monyra = den.VendorOf(716)!; // the pinned world SQL: weenie 716 holtburgjeweler, int property 74 = 264200
        Assert.Equal("Monyra the Jeweler", monyra.Name);
        Assert.Equal(0x7A9B4020u, monyra.Guid); // landblock_instance guid 2056994848
        Assert.Equal(0x00040808u, monyra.BuysItemTypes);
        Assert.True(monyra.Buys(0x8) && monyra.Buys(0x800)); // Jewelry, Gem
        Assert.False(monyra.Buys(0x2)); // not armour
        Assert.Empty(monyra.Sells); // a loot buyer only
        Assert.Equal((0xA9B40117u, 0xA9B40117u), (monyra.Cell, monyra.StopCell)); // the entry hall that is Cindrue's stop cell
        Assert.Equal(0x480467A7u | 0x000CB000u | 0x00044080u | 0x00040004u | 0x00040808u | 0x00446220u, den.SellableItemTypes); // the vendors' masks together (M6 14 grocer: the Grocer's adds CraftCookingBase 0x400000, the Empty Flask's type)
        Assert.NotEqual(0u, den.SellableItemTypes & 0x8); // jewellery sells
        Assert.NotEqual(0u, den.SellableItemTypes & 0x800); // gems sell
    }

    [Fact]
    public void Without_the_Jeweler_the_dens_vendors_buy_neither_jewellery_nor_gems_and_the_loot_section_is_strict_M5_7_F28()
    {
        var none = FullDenCopy();
        var vendors = (JsonArray)none["vendors"]!;
        vendors.RemoveAt(4); // Monyra
        var without = GroundCatalog.Parse(DenFile, none.ToJsonString());
        Assert.Equal(0u, without.SellableItemTypes & (0x8 | 0x800)); // the finding that made the Jeweler a profile vendor: nobody else takes them

        var low = FullDenCopy();
        ((JsonObject)low["loot"]!)["min_value_per_burden"] = -1;
        Assert.Equal("$.loot.min_value_per_burden", Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, low.ToJsonString())).JsonPath);

        foreach (var field in new[] { "min_payout", "max_item_burden", "trip_every_buff_cycles", "min_free_slots", "load_line_percent" }) // M5 7 F29: each is optional and strict
        {
            var bad = FullDenCopy();
            ((JsonObject)bad["loot"]!)[field] = -1;
            Assert.Equal("$.loot." + field, Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, bad.ToJsonString())).JsonPath);
            var gone = FullDenCopy();
            ((JsonObject)gone["loot"]!).Remove(field);
            Assert.NotNull(GroundCatalog.Parse(DenFile, gone.ToJsonString()).Loot); // absent: off, not an error
        }

        var rate = FullDenCopy();
        ((JsonObject)((JsonArray)rate["vendors"]!)[0]!)["buy_price"] = 0;
        Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, rate.ToJsonString())); // a vendor that pays nothing is not a buyer
        var noRate = FullDenCopy();
        ((JsonObject)((JsonArray)noRate["vendors"]!)[0]!).Remove("buy_price");
        Assert.Equal(GroundVendor.DefaultBuyPrice, GroundCatalog.Parse(DenFile, noRate.ToJsonString()).Vendors[0].BuyPrice);

        var unknown = FullDenCopy();
        ((JsonObject)unknown["loot"]!)["keep_weapons"] = true;
        Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, unknown.ToJsonString()));

        var absent = FullDenCopy();
        ((JsonObject)absent).Remove("loot");
        Assert.Null(GroundCatalog.Parse(DenFile, absent.ToJsonString()).Loot); // the section is optional
    }

    [Fact]
    public void The_loot_sections_keep_aside_block_is_optional_and_strict_M5_7_F30()
    {
        var gone = FullDenCopy();
        ((JsonObject)gone["loot"]!).Remove("keep_aside");
        Assert.Null(GroundCatalog.Parse(DenFile, gone.ToJsonString()).Loot!.KeepAside); // absent: nothing kept aside, not an error

        void Rejects(string path, Action<JsonObject> spoil)
        {
            var bad = FullDenCopy();
            spoil((JsonObject)((JsonObject)bad["loot"]!)["keep_aside"]!);
            Assert.Equal("$.loot.keep_aside" + path, Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, bad.ToJsonString())).JsonPath);
        }

        Rejects(".min_war_bonus_percent", b => b["min_war_bonus_percent"] = 0); // a floor of nothing would keep every caster
        Rejects(".min_war_bonus_percent", b => b["min_war_bonus_percent"] = -3);
        Rejects(".min_war_bonus_percent", b => b["min_war_bonus_percent"] = "3");
        Rejects(".min_war_bonus_percent", b => b.Remove("min_war_bonus_percent")); // each field is required
        Rejects(".cantrips", b => b.Remove("cantrips"));
        Rejects(".cantrips", b => b["cantrips"] = new JsonArray());
        Rejects(".cantrips[1]", b => b["cantrips"] = new JsonArray("Focus", "focus")); // a repeat, whatever its case
        Rejects(".cantrips[0]", b => b["cantrips"] = new JsonArray(" "));
        Rejects(".cantrip_tiers", b => b["cantrip_tiers"] = new JsonArray());
        Rejects(".cantrip_tiers[0]", b => b["cantrip_tiers"] = new JsonArray("Mighty")); // Minor, Major, Epic or Legendary
        Rejects(".cantrip_tiers[1]", b => b["cantrip_tiers"] = new JsonArray("Major", "Major"));
        Rejects(".keep_everything", b => b["keep_everything"] = true); // an unknown field

        var ok = GroundCatalog.Parse(DenFile, FullDenCopy().ToJsonString()).Loot!.KeepAside!;
        Assert.Contains("Major Focus", ok.SpellNames());
        Assert.Contains("Legendary Invulnerability", ok.SpellNames());
        Assert.Equal(3 * 11, ok.SpellNames().Count());
    }

    [Fact]
    public void A_vendor_s_stop_point_is_a_known_optional_field_a_front_room_vendor_has_one_and_a_vendor_in_his_own_stop_cell_has_none()
    {
        var den = Den;
        Assert.Equal(new Vector3(152.3f, 132.5f, 66f), den.VendorOf(2304)!.StopPoint); // Archmage Cindrue: her entry hall 0xA9B40117, the clear floor nearest her that the planner reaches
        Assert.Equal(0xA9B40117u, den.VendorOf(2304)!.StopCell);
        Assert.Equal(new Vector3(133.9f, 15.7f, 94f), den.VendorOf(718)!.StopPoint); // Renald: his stand on the floor of his front room, inside his 5 m use radius (M5 7 P7p; D120 put it at (133.5, 15.5), 5.04 m out: the corner nearest him is full of barrels), his own point lies in the back room
        Assert.Null(den.VendorOf(715)!.StopPoint); // Thelnoth stops in his own cell: absent means his own point
        Assert.All(den.Vendors, v => Assert.True(v.StopCell == v.Cell || v.StopPoint is not null, v.Name)); // a stop room in front of the vendor always names the point to stand at

        var absent = FullDenCopy();
        ((JsonObject)absent["vendors"]![1]!).Remove("stop_point");
        Assert.Null(GroundCatalog.Parse(DenFile, absent.ToJsonString()).VendorOf(2304)!.StopPoint); // the same profile without the field loads, with none

        var present = FullDenCopy();
        ((JsonObject)present["vendors"]![2]!)["stop_point"] = JsonNode.Parse("""{ "x": 30.5, "y": 159, "z": 66 }""");
        Assert.Equal(new Vector3(30.5f, 159f, 66f), GroundCatalog.Parse(DenFile, present.ToJsonString()).VendorOf(715)!.StopPoint); // and with it on a vendor that had none

        var short1 = FullDenCopy();
        ((JsonObject)short1["vendors"]![2]!)["stop_point"] = JsonNode.Parse("""{ "x": 30.5, "z": 66 }""");
        Assert.Equal("$.vendors[2].stop_point.y", Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, short1.ToJsonString())).JsonPath); // strict when present

        var nothing = FullDenCopy();
        ((JsonObject)nothing["vendors"]![2]!)["stop_point"] = null;
        Assert.Equal("$.vendors[2].stop_point", Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, nothing.ToJsonString())).JsonPath); // a null is not "absent"
    }

    [Fact]
    public void A_target_may_name_an_opener_the_den_s_Slave_does_and_the_Guard_does_not_and_the_loader_is_strict_M5_7_F8()
    {
        var den = GroundCatalog.Get(DenKey);
        Assert.Equal("Fire Vulnerability Other", den.OpenerOf(1628)); // Tusker Slave
        Assert.Null(den.OpenerOf(1629)); // Tusker Guard: bolts only
        Assert.Null(den.OpenerOf(23)); // an avoided Virindi Servant
        Assert.Null(den.OpenerOf(999_999)); // not listed
        Assert.All(GroundCatalog.All.SelectMany(g => g.Targets).Where(t => t.Opener is not null), t => Assert.NotNull(ArenaProfile.ExtraNamed(t.Opener!))); // every opener of every profile names a spell line the profile holds

        var empty = FullDenCopy();
        ((JsonObject)empty["targets"]![0]!)["opener"] = "";
        Assert.Equal("$.targets[0].opener", Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, empty.ToJsonString())).JsonPath);

        var nothing = FullDenCopy();
        ((JsonObject)nothing["targets"]![0]!)["opener"] = null;
        Assert.Equal("$.targets[0].opener", Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, nothing.ToJsonString())).JsonPath); // a null is not "absent"

        var avoided = FullDenCopy();
        ((JsonObject)avoided["targets"]![2]!)["opener"] = "Fire Vulnerability Other";
        Assert.Equal("$.targets[2].opener", Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, avoided.ToJsonString())).JsonPath); // only a target is fought

        var none = FullDenCopy();
        ((JsonObject)none["targets"]![0]!).Remove("opener");
        Assert.Null(GroundCatalog.Parse(DenFile, none.ToJsonString()).OpenerOf(1628)); // the same profile without the field loads, with none
    }

    [Fact]
    public void The_spell_plan_is_D41_and_the_components_are_the_war_line_s()
    {
        var den = Den;
        Assert.Equal([new SpellPlanRow(1, 99, "Flame Bolt", 6), new SpellPlanRow(100, 275, "Flame Bolt", 7)], den.SpellPlan);
        Assert.Equal(6, den.PlanFor(80)!.SpellLevel);
        Assert.Equal(6, den.PlanFor(99)!.SpellLevel);
        Assert.Equal(7, den.PlanFor(100)!.SpellLevel);
        Assert.Equal(7, den.PlanFor(127)!.SpellLevel);
        Assert.Null(den.PlanFor(276));
        Assert.Equal(ArenaProfile.WarLineName, den.SpellPlan[0].Family);
        Assert.Equal(
            [
                new GroundComponent(690, "Pyreal Scarab", 6, 1),
                new GroundComponent(20631, "Prismatic Taper", 6, 4),
                new GroundComponent(8897, "Platinum Scarab", 7, 1),
                new GroundComponent(20631, "Prismatic Taper", 7, 4),
            ],
            den.Components);
    }

    [Fact]
    public void The_three_generators_are_900_s_on_a_60_s_tick_their_117_slots_marked_by_role_and_there_are_no_hold_spots()
    {
        var den = Den;
        Assert.Equal(
            [
                new GeneratorModel(0x70105022, 0x01050154, 900, 60),
                new GeneratorModel(0x7010507A, 0x01050382, 900, 60),
                new GeneratorModel(0x7010507B, 0x01050382, 900, 60),
            ],
            den.Respawn.Generators);
        Assert.Equal(900, den.Respawn.GeneratorOf(0x7010507B)!.DelaySeconds);
        Assert.Null(den.Respawn.GeneratorOf(0x70105023));
        Assert.Empty(den.HoldSpots);

        // The slots (GroundDraft.World over the pinned world SQL v0.9.296): every link child of the three generators.
        var slots = den.Respawn.Slots;
        Assert.Equal(117, slots.Count);
        Assert.Equal([35, 1, 81], den.Respawn.Generators.Select(g => slots.Count(s => s.Generator == g.Guid)));
        var hunted = slots.Where(s => den.RoleOf(s.Wcid) == GroundRole.Target && den.IsWalkable(s.Cell)).ToArray();
        Assert.Equal(78, hunted.Length); // D39: 19 Slaves and 59 Guards
        Assert.Equal(19, hunted.Count(s => s.Wcid == 1628));
        Assert.Equal(59, hunted.Count(s => s.Wcid == 1629));
        Assert.Equal(
            [0x010D, 0x02D3, 0x02D5, 0x02D5, 0x02D6], // the guards D39 leaves standing: the wing's and the outliers' cells
            slots.Where(s => den.RoleOf(s.Wcid) == GroundRole.Target && den.IsNoGo(s.Cell)).Select(s => (int)(s.Cell & 0xFFFF)).Order());
        var avoided = slots.Where(s => den.RoleOf(s.Wcid) == GroundRole.Avoid).ToArray();
        Assert.Equal([(23u, 8), (237u, 2), (1609u, 8), (1610u, 16)], avoided.GroupBy(s => s.Wcid).OrderBy(g => g.Key).Select(g => (g.Key, g.Count())));
        Assert.All(slots, s => Assert.NotNull(den.RoleOf(s.Wcid)));
        Assert.Equal(den.Tour.Order(), hunted.Select(s => s.Cell).Distinct().Order());
    }

    [Fact]
    public void The_walk_is_today_s_doorway_lean_and_leg_rules()
    {
        var walk = Den.Walk;
        Assert.Equal(new WalkModel(0.3f, 1.0f, 0.25, 90), walk);
    }

    [Fact]
    public void A_ground_of_two_landblocks_two_more_storeys_and_generators_of_different_delays_loads()
    {
        var copy = FullDenCopy();
        copy["landblocks"] = JsonNode.Parse("""["0x0105", "0x0106"]""");
        var storeys = (JsonArray)copy["storeys"]!;
        storeys.Add(JsonNode.Parse("""{ "name": "the pockets", "z_min": -18.5, "z_max": -17.5, "cells": ["0x010501D7-0x010501DA"] }"""));
        storeys.Add(JsonNode.Parse("""{ "name": "the next landblock", "z_min": -12.5, "z_max": -11.5, "cells": ["0x01060100", "0x01060101"] }"""));
        var generators = (JsonArray)copy["respawn"]!["generators"]!;
        generators.Add(JsonNode.Parse("""{ "guid": "0x70106001", "cell": "0x01060100", "delay_s": 180, "tick_s": 30 }"""));
        generators.Add(JsonNode.Parse("""{ "guid": "0x70106002", "cell": "0x01060101", "delay_s": 480, "tick_s": 120 }"""));
        ((JsonArray)copy["respawn"]!["slots"]!).Add(JsonNode.Parse("""{ "generator": "0x70106002", "cell": "0x01060101", "wcid": 7 }"""));
        ((JsonArray)copy["targets"]!).Add(JsonNode.Parse("""{ "wcid": 7, "name": "A seventh class", "level": 12, "role": "target" }"""));

        var ground = GroundCatalog.Parse(DenFile, copy.ToJsonString());
        Assert.Equal(8, ground.Storeys.Count);
        Assert.Equal(7, ground.StoreyOf(0x01060101));
        Assert.Equal(6, ground.StoreyOf(0x010501D8));
        Assert.True(ground.IsWalkable(0x01060100));
        Assert.True(ground.InLandblocks(0x01060001));
        Assert.Equal(180, ground.Respawn.GeneratorOf(0x70106001)!.DelaySeconds);
        Assert.Equal(120, ground.Respawn.GeneratorOf(0x70106002)!.TickSeconds);
        Assert.Equal(119, ground.Respawn.Slots.Count); // the den's 117, the copy's added one and this one
        Assert.Equal(GroundRole.Target, ground.RoleOf(7));
    }

    [Fact]
    public void The_den_s_supplies_are_Cindrue_s_taper_Thelnoth_s_potion_the_carried_scarabs_her_robe_and_wand_and_a_550000_pyreal_purse()
    {
        var den = Den;
        Assert.Equal(
            [
                new StockLine(20631, "Prismatic Taper", 1000, 200, 12),
                new StockLine(379, "Mana Potion", 30, 5, 0),
                new StockLine(690, "Pyreal Scarab", 400, 3, 3), // M5 7 F3 (D87): a day of VI casts at the soak's level 80; M5 7 F10: 300 -> 400 for the picker's extra VI casts (the opener, the drains), four stacks of 100
                new StockLine(8897, "Platinum Scarab", 100, 3, 3),
                new StockLine(686, "Copper Scarab", 20, 5, 2), // M5 3 m1: the travel spells' scarab on the foci formula
                new StockLine(7299, "Diamond Scarab", 6, 2, 2), // M5 7 F10 (the owner's components rule): the ring's two a cast, carried (F9: one burns about every 450 casts)
                new StockLine(691, "Lead Scarab", 100, 20, 2), // M5 7 F16 (D91): the emergency drain's level I scarab, bought on trips from Cindrue
                new StockLine(688, "Silver Scarab", 20, 2, 2), // M5 7 F25 (D93): the buff's level IV scarab, carried (Holtburg sells none)
                new StockLine(687, "Gold Scarab", 20, 2, 2), // M5 7 F25: the buff's level V scarab, carried
            ],
            den.Supplies.Stock);
        Assert.Equal([new KitLine(5851, "Faran Robe with Hood", KitSlot.Worn), new KitLine(56, "Leather Gauntlets", KitSlot.Worn), new KitLine(2472, "Wand", KitSlot.Wielded), new KitLine(136, "Pack", KitSlot.Pack, 2)], den.Supplies.Kit); // M5 7 F13 (D87): a hooded robe and gloves; M5 7 G3: the seven side packs a player can carry
        Assert.Equal(90, den.Supplies.BurdenTripPercent);
        Assert.Equal(40_000, den.Supplies.Purse.Coin);
        Assert.Equal([new PurseNote(20630, "Trade Note (250,000)", 250_000, 1), new PurseNote(2625, "Trade Note (10,000)", 10_000, 1), new PurseNote(2626, "Trade Note (50,000)", 50_000, 5)], den.Supplies.Purse.Notes); // M5 7 F10: 400,000 -> 550,000 for the picker's extra taper burn
        Assert.Equal(510_000, den.Supplies.Purse.NotesValue); // M5 7 F10: three more 50,000 notes
        Assert.Equal(550_000, den.Supplies.Purse.Total);
        Assert.True(den.Supplies.Purse.Total >= 20_700L * 26, "M5 7 F10: the purse pays for 26 hours at the picker's taper burn: 15,300 + 5,400 pyreals an hour"); // 538,200
        Assert.True(den.Supplies.Purse.Total >= 85L * 180 * 24, "M5 7 F3 (D87): the purse pays for a day of the soak's level 80: 85 pyreals a kill x 180 an hour x 24 h"); // 367,200
        Assert.True(den.Supplies.Purse.Total >= 45L * 224 * 24, "the purse pays for D38's day: 45 pyreals a kill x 224 an hour x 24 h"); // 241,920

        // The sold lines: the vendor and the price are the vendors' sells, the one source of a price. The scarabs are carried (Holtburg sells neither, D8): their trip line is their stop line.
        Assert.Equal((2304u, 30), (den.VendorSelling(20631)!.Wcid, den.PriceOf(20631)!.Value));
        Assert.Equal((715u, 230), (den.VendorSelling(379)!.Wcid, den.PriceOf(379)!.Value));
        Assert.Equal((719u, 68), (den.VendorSelling(5851)!.Wcid, den.PriceOf(5851)!.Value)); // M5 7 F13 (D87)
        Assert.Equal((718u, 1485), (den.VendorSelling(56)!.Wcid, den.PriceOf(56)!.Value));
        Assert.Null(den.VendorSelling(6071)); // the war apprentice robe left the kit
        Assert.Equal((2304u, 270), (den.VendorSelling(2472)!.Wcid, den.PriceOf(2472)!.Value));
        foreach (var carried in new uint[] { 690, 8897 })
        {
            Assert.True(den.IsCarried(carried), $"{carried} is carried");
            Assert.Null(den.VendorSelling(carried));
            Assert.Null(den.PriceOf(carried));
            Assert.Equal(3, den.Supplies.StockOf(carried)!.StopUnder); // D8: a stack under three stops the loop
            Assert.Equal(den.Supplies.StockOf(carried)!.StopUnder, den.Supplies.StockOf(carried)!.TripUnder); // no trip restocks it
            Assert.True(den.Supplies.StockOf(carried)!.Keep <= 400, "M5 7 F3 (D87), F10: at most four stacks of 100 (the build asks for each stack, GearKit.StackSizes)");
        }

        Assert.False(den.IsCarried(20631));
        Assert.Null(den.Supplies.StockOf(5851)); // a kit item is not a stock line
        Assert.Equal(KitSlot.Wielded, den.Supplies.KitOf(2472)!.Slot);
        Assert.Null(den.Supplies.KitOf(20631));
        Assert.Null(den.VendorSelling(12345));
    }

    [Fact]
    public void Tusker_Temple_s_supplies_buy_the_scarabs_from_Zisaha_the_potion_from_Yoshi_Deshi_and_keep_Zisaha_s_kit()
    {
        var temple = GroundCatalog.Get("tusker-temple-fort-tethana");
        Assert.Equal([20631u, 379u, 690u, 8897u, 686u], temple.Supplies.Stock.Select(l => l.Wcid));
        Assert.All(temple.Supplies.Stock, l => Assert.False(temple.IsCarried(l.Wcid), $"{l.Name} is bought at Fort Tethana"));
        Assert.Equal((2314u, 40), (temple.VendorSelling(20631)!.Wcid, temple.PriceOf(20631)!.Value));
        Assert.Equal((2317u, 306), (temple.VendorSelling(379)!.Wcid, temple.PriceOf(379)!.Value));
        Assert.Equal((2314u, 1800), (temple.VendorSelling(690)!.Wcid, temple.PriceOf(690)!.Value));
        Assert.Equal((2314u, 18000), (temple.VendorSelling(8897)!.Wcid, temple.PriceOf(8897)!.Value));
        Assert.Equal([new KitLine(5910, "Dho War Master Robe", KitSlot.Worn), new KitLine(2472, "Wand", KitSlot.Wielded)], temple.Supplies.Kit);
        Assert.Equal((2314u, 14400), (temple.VendorSelling(5910)!.Wcid, temple.PriceOf(5910)!.Value));
        Assert.Equal((2314u, 360), (temple.VendorSelling(2472)!.Wcid, temple.PriceOf(2472)!.Value));
        Assert.Equal(1_600_000, temple.Supplies.Purse.Total);
        Assert.Equal(50_000, temple.Supplies.Purse.Coin);
    }

    /// <summary>M6 8 D (m5-side-packs-use): each dungeon's home vendor that sells the side pack lists it at ACE's sell price, <c>max(1, ceil(rate x Value - 0.1))</c> with the Pack's Value 65 (world SQL v0.9.296, weenie 136) and the vendor's SellPrice (float 38): Renald 1.35, Yoshi Deshi 1.8.</summary>
    [Theory]
    [MemberData(nameof(DenRoomGraphTests.Dungeons), MemberType = typeof(DenRoomGraphTests))]
    public void M6_8_D_a_home_vendor_sells_the_side_pack_at_the_ceiling_of_its_rate(string key)
    {
        var ground = GroundCatalog.Get(key);
        var vendor = ground.VendorSelling(136);
        Assert.True(vendor is not null, $"{key}: a home vendor sells the Pack");
        Assert.Equal("Pack", vendor!.Sells.Single(w => w.Wcid == 136).Name);
        var rate = vendor.Wcid == 2317 ? 1.8 : 1.35;
        Assert.Equal((int)Math.Max(1, Math.Ceiling((rate * 65) - 0.1)), ground.PriceOf(136));
    }

    [Fact]
    public void The_outdoor_grounds_have_the_empty_supplies_section()
    {
        foreach (var key in new[] { "low-b-belt", "low-north-forest-mix" })
        {
            var ground = GroundCatalog.Get(key);
            Assert.Empty(ground.Supplies.Stock);
            Assert.Empty(ground.Supplies.Kit);
            Assert.Equal(0, ground.Supplies.Purse.Total);
            Assert.Empty(ground.Supplies.Purse.Notes);
            Assert.Contains("hunters shop at the journey stations", ground.Sources["supplies"], StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(DenRoomGraphTests.Dungeons), MemberType = typeof(DenRoomGraphTests))]
    public void Every_spell_plan_level_s_components_are_stock_lines_and_every_kit_line_resolves_in_each_dungeon(string key)
    {
        var ground = GroundCatalog.Get(key);
        Assert.NotEmpty(ground.SpellPlan);
        foreach (var row in ground.SpellPlan)
        {
            var burned = ground.Components.Where(c => c.SpellLevel == row.SpellLevel).ToList();
            Assert.NotEmpty(burned); // a row's level burns something
            foreach (var c in burned)
            {
                var line = ground.Supplies.StockOf(c.Wcid);
                if (line is null && ground.TownStockOf(c.Wcid) is { } town)
                {
                    Assert.Equal(c.Name, town.Name); // M6 item 14 (m6-goldscarab): a plan component bought in another town (the Gold and the Pyreal Scarab at Uziz) is the town stock's line, not the home walk's
                    continue;
                }

                Assert.True(line is not null, $"{key}: spell level {row.SpellLevel}'s component {c.Name} is a stock line");
                Assert.Equal(c.Name, line!.Name);
            }
        }

        // A kit line resolves to the vendor that sells it, at a price; a stock line to its vendor or is carried (and a carried line stops where a trip is due).
        long firstFill = 0;
        foreach (var kit in ground.Supplies.Kit)
        {
            // M6 8 D (m5-side-packs-use closed): a pack line is sold by a vendor of the profile like any other kit line, so the trip can buy it (Renald's Pack row: the pinned world SQL's CreateList row of weenie 136, 65 x 1.35).
            var vendor = ground.VendorSelling(kit.Wcid);
            Assert.True(vendor is not null, $"{key}: kit line {kit.Name} is sold by a vendor of the profile");
            Assert.Equal(kit.Name, vendor!.Sells.Single(w => w.Wcid == kit.Wcid).Name);
            firstFill += ground.PriceOf(kit.Wcid)!.Value;
        }

        foreach (var line in ground.Supplies.Stock)
        {
            if (ground.IsCarried(line.Wcid))
            {
                Assert.Equal(line.TripUnder, line.StopUnder);
            }
            else
            {
                Assert.True(ground.PriceOf(line.Wcid) > 0);
                firstFill += (long)line.Keep * ground.PriceOf(line.Wcid)!.Value;
            }
        }

        // M6 3 F1 (3-R1): a profile either lists a robe and a wand or lists no kit and is kit-less by rule (GearKit.HasNoKit: the character fights with what it holds); a half kit is a gap.
        if (ground.Supplies.Kit.Count == 0)
        {
            Assert.True(GearKit.HasNoKit(ground), key);
        }
        else
        {
            Assert.Contains(ground.Supplies.Kit, k => k.Slot == KitSlot.Worn);
            Assert.Contains(ground.Supplies.Kit, k => k.Slot == KitSlot.Wielded);
        }

        Assert.True(ground.Supplies.Purse.Coin > 0);
        Assert.True(ground.Supplies.Purse.Total >= firstFill, $"{key}: the purse ({ground.Supplies.Purse.Total}) pays for the first fill of every sold line and the kit ({firstFill})");
        Assert.All(ground.Supplies.Purse.Notes, n => Assert.True(n.Value >= 1000, "a purse note is a large note"));
    }

    [Fact]
    public void A_plan_level_with_no_stock_line_or_no_components_fails_at_load_naming_the_row_and_the_gap()
    {
        // A component of the plan's level 7 with no stock line.
        var noStock = FullDenCopy();
        ((JsonArray)noStock["supplies"]!["stock"]!).RemoveAt(3); // the Platinum Scarab, carried
        var gap = Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, noStock.ToJsonString()));
        Assert.Equal("$.spell_plan[1]", gap.JsonPath);
        Assert.Contains("TODO(ground-plan-gap)", gap.Message, StringComparison.Ordinal);
        Assert.Contains("8897", gap.Message, StringComparison.Ordinal);
        Assert.Contains("$.supplies.stock", gap.Message, StringComparison.Ordinal);

        // The same for level 6's Pyreal Scarab, the plan's first row.
        var noPyreal = FullDenCopy();
        ((JsonArray)noPyreal["supplies"]!["stock"]!).RemoveAt(2);
        Assert.Equal("$.spell_plan[0]", Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, noPyreal.ToJsonString())).JsonPath);

        // Any spell level (D48), not VI and VII alone: a row for level 5 whose component has a stock line loads; one whose component has none fails; one with no component at all fails.
        var five = FullDenCopy();
        ((JsonArray)five["spell_plan"]!).Add(JsonNode.Parse("""{ "min_level": 276, "max_level": 300, "family": "Flame Bolt", "spell_level": 5 }"""));
        var noComponents = Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, five.ToJsonString()));
        Assert.Equal("$.spell_plan[2]", noComponents.JsonPath);
        Assert.Contains("TODO(ground-plan-gap)", noComponents.Message, StringComparison.Ordinal);
        Assert.Contains("no component", noComponents.Message, StringComparison.Ordinal);

        ((JsonArray)five["components"]!).Add(JsonNode.Parse("""{ "wcid": 689, "name": "Iron Scarab", "spell_level": 5, "per_cast": 1 }"""));
        var noLine = Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, five.ToJsonString()));
        Assert.Equal("$.spell_plan[2]", noLine.JsonPath);
        Assert.Contains("689", noLine.Message, StringComparison.Ordinal);

        ((JsonArray)five["supplies"]!["stock"]!).Add(JsonNode.Parse("""{ "wcid": 689, "name": "Iron Scarab", "keep": 50, "trip_under": 10, "stop_under": 3 }"""));
        var loaded = GroundCatalog.Parse(DenFile, five.ToJsonString());
        Assert.Equal(5, loaded.PlanFor(276)!.SpellLevel);
        Assert.True(loaded.IsCarried(689)); // no vendor lists it: carried
    }

    [Fact]
    public void A_vendor_s_ware_is_a_supply_line_with_one_vendor_and_one_name_or_the_profile_fails_at_load()
    {
        var orphan = FullDenCopy();
        ((JsonArray)orphan["vendors"]![0]!["sells"]!).Add(JsonNode.Parse("""{ "wcid": 12345, "name": "A Gizmo", "price": 9 }"""));
        var e = Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, orphan.ToJsonString()));
        Assert.Equal("$.vendors[0].sells[2]", e.JsonPath); // Renald sells the gauntlets (M5 7 F13) at sells[0] and the Pack (M6 8 D) at sells[1]
        Assert.Contains("$.supplies.stock, $.supplies.kit, a note of $.supplies.purse or a scroll of $.learn", e.Message, StringComparison.Ordinal);

        var twice = FullDenCopy();
        ((JsonArray)twice["vendors"]![2]!["sells"]!).Add(JsonNode.Parse("""{ "wcid": 20631, "name": "Prismatic Taper", "price": 31 }"""));
        var twoVendors = Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, twice.ToJsonString()));
        Assert.Equal("$.vendors[2].sells[1].wcid", twoVendors.JsonPath);
        Assert.Contains("one home vendor", twoVendors.Message, StringComparison.Ordinal);

        var renamed = FullDenCopy();
        Set(renamed, "$.supplies.stock[0].name", JsonValue.Create("Taper"));
        var name = Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, renamed.ToJsonString()));
        Assert.Equal("$.supplies.stock[0].name", name.JsonPath);
        Assert.Contains("one source of a name", name.Message, StringComparison.Ordinal);

        var kitName = FullDenCopy();
        Set(kitName, "$.supplies.kit[1].name", JsonValue.Create("Staff"));
        Assert.Equal("$.supplies.kit[1].name", Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, kitName.ToJsonString())).JsonPath);

        var twiceStock = FullDenCopy();
        ((JsonArray)twiceStock["supplies"]!["stock"]!).Add(JsonNode.Parse("""{ "wcid": 379, "name": "Mana Potion", "keep": 1, "trip_under": 1, "stop_under": 1 }"""));
        Assert.Equal("$.supplies.stock", Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, twiceStock.ToJsonString())).JsonPath);
    }

    [Fact]
    public void A_draft_s_hand_set_supplies_section_is_refused_naming_the_field()
    {
        var draft = FullDenCopy();
        draft["supplies"] = GroundDraft.HandSet;
        var e = Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, draft.ToJsonString()));
        Assert.Equal("$.supplies", e.JsonPath);
        Assert.Contains("not an object", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_key_throws_naming_it()
    {
        var e = Assert.Throws<KeyNotFoundException>(() => GroundCatalog.Get("no-such-ground"));
        Assert.Contains("'no-such-ground'", e.Message, StringComparison.Ordinal);
        Assert.Contains(DenKey, e.Message, StringComparison.Ordinal);
    }

    /// <summary>Every required field, each removed from a copy (the den's, with one hold spot and one slot added, or an outdoor copy for the outdoor block's fields).</summary>
    public static TheoryData<string, bool> RequiredFields() => new()
    {
        { "$.key", false }, { "$.name", false }, { "$.kind", false }, { "$.sources", false },
        { "$.sources.landblocks", false }, { "$.sources.arrival", false }, { "$.sources.storeys", false }, { "$.sources.no_go", false }, { "$.sources.targets", false },
        { "$.sources.level_band", false }, { "$.sources.portal", false }, { "$.sources.home", false }, { "$.sources.vendors", false }, { "$.sources.spell_plan", false },
        { "$.sources.components", false }, { "$.sources.supplies", false }, { "$.sources.hold_spots", false }, { "$.sources.respawn", false }, { "$.sources.tour", false }, { "$.sources.outdoor", false },
        { "$.landblocks", false },
        { "$.arrival", false }, { "$.arrival.cell", false }, { "$.arrival.origin", false }, { "$.arrival.origin.x", false }, { "$.arrival.origin.y", false }, { "$.arrival.origin.z", false },
        { "$.arrival.heading", false }, { "$.arrival.heading.w", false }, { "$.arrival.heading.z", false },
        { "$.walk", false }, { "$.walk.doorway_lean", false }, { "$.walk.stop_within_m", false }, { "$.walk.stop_dwell_s", false }, { "$.walk.leg_s", false },
        { "$.sources.walk", false },
        { "$.storeys", false }, { "$.storeys[0].name", false }, { "$.storeys[0].z_min", false }, { "$.storeys[0].z_max", false }, { "$.storeys[0].cells", false },
        { "$.no_go", false },
        { "$.targets", false }, { "$.targets[0].wcid", false }, { "$.targets[0].name", false }, { "$.targets[0].level", false }, { "$.targets[0].role", false },
        { "$.level_band", false }, { "$.level_band.min", false }, { "$.level_band.max", false },
        { "$.portal", false }, { "$.portal.guid", false }, { "$.portal.cell", false }, { "$.portal.tie_spell", false }, { "$.portal.recall_spell", false },
        { "$.portal.wcid", false }, { "$.portal.origin", false }, { "$.portal.origin.x", false }, { "$.portal.front_cell", false }, { "$.portal.front_point", false }, { "$.portal.front_point.z", false },
        { "$.home", false }, { "$.home.town", false }, { "$.home.lifestone_cell", false }, { "$.home.lifestone_point", false },
        { "$.vendors", false }, { "$.vendors[0].name", false }, { "$.vendors[0].wcid", false }, { "$.vendors[0].guid", false }, { "$.vendors[0].cell", false },
        { "$.vendors[0].point", false }, { "$.vendors[0].stop_cell", false }, { "$.vendors[0].buys_item_types", false }, { "$.vendors[0].sells", false },
        { "$.vendors[1].sells[0].wcid", false }, { "$.vendors[1].sells[0].name", false }, { "$.vendors[1].sells[0].price", false },
        { "$.spell_plan", false }, { "$.spell_plan[0].min_level", false }, { "$.spell_plan[0].max_level", false }, { "$.spell_plan[0].family", false }, { "$.spell_plan[0].spell_level", false },
        { "$.components", false }, { "$.components[0].wcid", false }, { "$.components[0].name", false }, { "$.components[0].spell_level", false }, { "$.components[0].per_cast", false },
        { "$.supplies", false }, { "$.supplies.stock", false }, { "$.supplies.stock[0].wcid", false }, { "$.supplies.stock[0].name", false }, { "$.supplies.stock[0].keep", false },
        { "$.supplies.stock[0].trip_under", false }, { "$.supplies.stock[0].stop_under", false },
        { "$.supplies.kit", false }, { "$.supplies.kit[0].wcid", false }, { "$.supplies.kit[0].name", false }, { "$.supplies.kit[0].slot", false },
        { "$.supplies.burden_trip_percent", false }, { "$.supplies.purse", false }, { "$.supplies.purse.coin", false }, { "$.supplies.purse.notes", false },
        { "$.supplies.purse.notes[0].wcid", false }, { "$.supplies.purse.notes[0].name", false }, { "$.supplies.purse.notes[0].value", false }, { "$.supplies.purse.notes[0].count", false },
        { "$.hold_spots", false }, { "$.hold_spots[0].cell", false }, { "$.hold_spots[0].point", false },
        { "$.respawn", false }, { "$.respawn.generators", false }, { "$.respawn.generators[0].guid", false }, { "$.respawn.generators[0].cell", false },
        { "$.respawn.generators[0].delay_s", false }, { "$.respawn.generators[0].tick_s", false }, { "$.respawn.slots", false },
        { "$.respawn.slots[0].generator", false }, { "$.respawn.slots[0].cell", false }, { "$.respawn.slots[0].wcid", false },
        { "$.tour", false }, { "$.outdoor", false },
        { "$.outdoor.cells", true }, { "$.outdoor.cells[0].cell", true }, { "$.outdoor.cells[0].north", true }, { "$.outdoor.cells[0].east", true },
        { "$.outdoor.cells[0].terrain", true }, { "$.outdoor.cells[0].spot", true }, { "$.outdoor.creatures", true }, { "$.outdoor.creatures[0].wcid", true },
        { "$.outdoor.creatures[0].name", true }, { "$.outdoor.creatures[0].level", true }, { "$.outdoor.creatures[0].spawn_mass", true }, { "$.outdoor.acquire_radius_m", true }, { "$.outdoor.cell_cap", true },
        { "$.outdoor.expected_yield_per_spot", true },
    };

    [Theory]
    [MemberData(nameof(RequiredFields))]
    public void A_profile_missing_a_required_field_fails_at_load_naming_the_field(string path, bool outdoor)
    {
        var copy = outdoor ? OutdoorCopy() : FullDenCopy();
        Assert.NotNull(GroundCatalog.Parse(DenFile, copy.ToJsonString())); // the copy itself loads
        Remove(copy, path);
        var e = Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, copy.ToJsonString()));
        Assert.Equal(path, e.JsonPath);
        Assert.Equal(DenFile, e.File);
        Assert.Contains($"{DenFile}: {path} is missing", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("$")]
    [InlineData("$.arrival")]
    [InlineData("$.storeys[0]")]
    [InlineData("$.vendors[2].sells[0]")]
    [InlineData("$.vendors[1]")]
    [InlineData("$.sources")]
    [InlineData("$.respawn")]
    [InlineData("$.walk")]
    [InlineData("$.respawn.generators[0]")]
    [InlineData("$.supplies")]
    [InlineData("$.supplies.stock[0]")]
    [InlineData("$.supplies.kit[0]")]
    [InlineData("$.supplies.purse")]
    [InlineData("$.supplies.purse.notes[0]")]
    public void An_unknown_field_fails_at_load_naming_it(string at)
    {
        var copy = FullDenCopy();
        ((JsonObject)Node(copy, at)).Add("hazards", 1);
        var e = Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, copy.ToJsonString()));
        Assert.Equal($"{at}.hazards", e.JsonPath);
        Assert.Contains("unknown field", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("$.tour[0]", "\"0x01060383\"", "$.tour[0]", "outside the profile's landblocks")]
    [InlineData("$.storeys[0].cells[0]", "\"0xA9B40019\"", "$.storeys[0].cells[0]", "outside the profile's landblocks")]
    [InlineData("$.no_go[0]", "\"0x01060100-0x01060121\"", "$.no_go[0]", "outside the profile's landblocks")]
    [InlineData("$.respawn.generators[0].delay_s", "-1", "$.respawn.generators[0].delay_s", "out of range")]
    [InlineData("$.respawn.generators[1].tick_s", "-60", "$.respawn.generators[1].tick_s", "out of range")]
    [InlineData("$.respawn.slots[0].generator", "\"0x70105099\"", "$.respawn.slots[0].generator", "is not one of $.respawn.generators")]
    [InlineData("$.walk.doorway_lean", "1.5", "$.walk.doorway_lean", "out of range")]
    [InlineData("$.spell_plan[1].min_level", "99", "$.spell_plan[1]", "overlap")]
    [InlineData("$.level_band.min", "81", "$.level_band.min", "above max")]
    [InlineData("$.kind", "\"cave\"", "$.kind", "out of range")]
    [InlineData("$.key", "\"black-spawn-den\"", "$.key", "does not match the file name")]
    [InlineData("$.targets[0].role", "\"maybe\"", "$.targets[0].role", "out of range")]
    [InlineData("$.components[0].spell_level", "9", "$.components[0].spell_level", "out of range")]
    [InlineData("$.tour[0]", "\"0x01050100\"", "$.tour[0]", "not walkable")]
    [InlineData("$.arrival.cell", "\"0x0105\"", "$.arrival.cell", "outside the profile's landblocks")]
    [InlineData("$.vendors[0].guid", "718", "$.vendors[0].guid", "not a hex string")]
    [InlineData("$.outdoor", "{}", "$.outdoor.cells", "is missing")]
    [InlineData("$.portal", "null", null, null)]
    [InlineData("$.supplies.stock[0].wcid", "0", "$.supplies.stock[0].wcid", "out of range")]
    [InlineData("$.supplies.stock[0].name", "\"\"", "$.supplies.stock[0].name", "not a non-empty string")]
    [InlineData("$.supplies.stock[0].keep", "0", "$.supplies.stock[0].keep", "out of range")]
    [InlineData("$.supplies.stock[0].trip_under", "-1", "$.supplies.stock[0].trip_under", "out of range")]
    [InlineData("$.supplies.stock[0].trip_under", "1001", "$.supplies.stock[0].trip_under", "above keep")]
    [InlineData("$.supplies.stock[0].stop_under", "-1", "$.supplies.stock[0].stop_under", "out of range")]
    [InlineData("$.supplies.stock[0].stop_under", "201", "$.supplies.stock[0].stop_under", "above trip_under")]
    [InlineData("$.supplies.stock[0].keep", "\"many\"", "$.supplies.stock[0].keep", "not an integer")]
    [InlineData("$.supplies.kit[0].wcid", "0", "$.supplies.kit[0].wcid", "out of range")]
    [InlineData("$.supplies.kit[0].slot", "\"head\"", "$.supplies.kit[0].slot", "out of range")]
    [InlineData("$.supplies.burden_trip_percent", "0", "$.supplies.burden_trip_percent", "out of range")]
    [InlineData("$.supplies.burden_trip_percent", "201", "$.supplies.burden_trip_percent", "out of range")]
    [InlineData("$.supplies.purse.coin", "-1", "$.supplies.purse.coin", "out of range")]
    [InlineData("$.supplies.purse.notes[0].wcid", "0", "$.supplies.purse.notes[0].wcid", "out of range")]
    [InlineData("$.supplies.purse.notes[0].value", "0", "$.supplies.purse.notes[0].value", "out of range")]
    [InlineData("$.supplies.purse.notes[0].count", "0", "$.supplies.purse.notes[0].count", "out of range")]
    [InlineData("$.supplies", "[]", "$.supplies", "not an object")]
    public void An_out_of_range_value_fails_at_load_naming_the_field(string at, string json, string? failsAt, string? problem)
    {
        var copy = FullDenCopy();
        Set(copy, at, JsonNode.Parse(json));
        if (failsAt is null)
        {
            Assert.Null(GroundCatalog.Parse(DenFile, copy.ToJsonString()).Portal); // null is a value for the portal
            return;
        }

        var e = Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, copy.ToJsonString()));
        Assert.Equal(failsAt, e.JsonPath);
        Assert.Contains(problem!, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cell_in_two_storeys_and_an_outdoor_ground_without_its_block_fail()
    {
        var copy = FullDenCopy();
        var storeys = (JsonArray)copy["storeys"]!;
        storeys.Add(JsonNode.Parse("""{ "name": "again", "z_min": -2, "z_max": -1, "cells": ["0x01050375"] }"""));
        Assert.Equal($"$.storeys[{storeys.Count - 1}].cells", Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, copy.ToJsonString())).JsonPath);

        var outdoor = FullDenCopy();
        outdoor["kind"] = "outdoor";
        Assert.Equal("$.outdoor", Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, outdoor.ToJsonString())).JsonPath);

        var notJson = Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(DenFile, "{ \"key\": "));
        Assert.Equal("$", notJson.JsonPath);
    }

    [Fact]
    public void An_outdoor_copy_loads_its_cells_and_creatures()
    {
        var ground = GroundCatalog.Parse(DenFile, OutdoorCopy().ToJsonString());
        Assert.Equal(GroundKind.Outdoor, ground.Kind);
        var outdoor = ground.Outdoor!;
        Assert.Equal(new OutdoorCell(0x01050001, 1.5, -2.5, GroundTerrain.Dry, new Vector3(0f, 0f, 1f)), Assert.Single(outdoor.Cells));
        Assert.Equal(new GroundCreature(1628, "Tusker Slave", 80) { SpawnMass = 0.125 }, Assert.Single(outdoor.Creatures));
        Assert.Equal(40f, outdoor.AcquireRadiusMetres);
        Assert.Equal(33, outdoor.CellCap);
        Assert.Equal(0.25, outdoor.ExpectedYieldPerSpot);
    }

    [Fact]
    public void The_embedded_den_text_parses_to_the_catalog_s_den()
    {
        var text = GroundCatalog.ResourceText(DenFile);
        Assert.NotNull(text);
        var parsed = GroundCatalog.Parse(DenFile, text!);
        Assert.Equal(Den.Walkable.Cells, parsed.Walkable.Cells);
        Assert.Equal(Den.Tour, parsed.Tour);
    }

    /// <summary>The den's profile with one hold spot and one slot added, so their fields have a row to remove.</summary>
    private static JsonNode FullDenCopy()
    {
        var copy = JsonNode.Parse(GroundCatalog.ResourceText(DenFile)!)!;
        ((JsonArray)copy["hold_spots"]!).Add(JsonNode.Parse("""{ "cell": "0x01050375", "point": { "x": 100, "y": -210, "z": 0 } }"""));
        ((JsonArray)copy["respawn"]!["slots"]!).Add(JsonNode.Parse("""{ "generator": "0x7010507A", "cell": "0x01050382", "wcid": 1628 }"""));
        return copy;
    }

    /// <summary>The den's profile made an outdoor ground with one cell and one creature.</summary>
    private static JsonNode OutdoorCopy()
    {
        var copy = FullDenCopy();
        copy["kind"] = "outdoor";
        copy["outdoor"] = JsonNode.Parse(
            """
            {
              "cells": [{ "cell": "0x01050001", "north": 1.5, "east": -2.5, "terrain": "dry", "spot": { "x": 0, "y": 0, "z": 1 } }],
              "creatures": [{ "wcid": 1628, "name": "Tusker Slave", "level": 80, "spawn_mass": 0.125 }],
              "acquire_radius_m": 40,
              "cell_cap": 33,
              "expected_yield_per_spot": 0.25
            }
            """);
        return copy;
    }

    private static (JsonNode Parent, string? Name, int Index) Parent(JsonNode root, string path)
    {
        var steps = Steps(path);
        var node = root;
        for (var i = 0; i < steps.Count - 1; i++)
        {
            node = steps[i].Name is { } n ? node[n]! : node[steps[i].Index]!;
        }

        return (node, steps[^1].Name, steps[^1].Index);
    }

    private static JsonNode Node(JsonNode root, string path)
    {
        if (path == "$")
        {
            return root;
        }

        var (parent, name, index) = Parent(root, path);
        return name is not null ? parent[name]! : parent[index]!;
    }

    private static void Remove(JsonNode root, string path)
    {
        var (parent, name, _) = Parent(root, path);
        Assert.True(((JsonObject)parent).Remove(name!), path);
    }

    private static void Set(JsonNode root, string path, JsonNode? value)
    {
        var (parent, name, index) = Parent(root, path);
        if (name is not null)
        {
            parent[name] = value;
        }
        else
        {
            parent[index] = value;
        }
    }

    /// <summary>"$.a.b[0].c" as steps: a name, or an index.</summary>
    private static List<(string? Name, int Index)> Steps(string path)
    {
        var steps = new List<(string? Name, int Index)>();
        foreach (var part in path[2..].Split('.'))
        {
            var open = part.IndexOf('[', StringComparison.Ordinal);
            steps.Add((open < 0 ? part : part[..open], -1));
            if (open >= 0)
            {
                steps.Add((null, int.Parse(part[(open + 1)..^1], System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        return steps;
    }
}

/// <summary>The profile's per-frame lookups allocate nothing (measured alone, <see cref="AllocationMeasurementCollection"/>).</summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class HuntingGroundAllocationTests
{
    [Fact]
    public void The_cell_role_storey_and_plan_lookups_allocate_nothing()
    {
        var den = GroundCatalog.Get("black-spawn-den-aluvian");
        var sink = 0;
        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                sink += den.IsWalkable(0x01050375) ? 1 : 0;
                sink += den.IsNoGo(0x01050100) ? 1 : 0;
                sink += den.StoreyOf(0x01050383);
                sink += den.RoleOf(1628) == GroundRole.Target ? 1 : 0;
                sink += den.InLandblocks(0x01050001) ? 1 : 0;
                sink += den.PlanFor(80)!.SpellLevel;
                sink += den.VendorSelling(20631) is null ? 0 : 1;
                sink += den.PriceOf(20631) ?? 0;
                sink += den.IsCarried(8897) ? 1 : 0;
                sink += den.Supplies.StockOf(379)!.Keep + (den.Supplies.KitOf(2472)!.Slot == KitSlot.Wielded ? 1 : 0);
            }
        });
        Assert.True(sink > 0);
    }

    /// <summary>
    /// M6 item 3 p1 and p2: the seven spine tusker profiles against item 0's W0 SELECT rows (cache/m6/0/sel-spine.tsv for the slots, sel-portals.tsv for the gates, sel-creatures.tsv for the levels, and the pinned world SQL for the
    /// health and Magic Defense ACE computes: the creature's init skill plus (Focus + Self) / 7, which is why they are above the W0 sheets' bare 48, 56, 88, 97, 80, 100, 155, 200 and 230): the slots per weenie, the generator's delay, the entry gate,
    /// each target's level, health and Magic Defense and the kill-first Minion. A profile that drifts from the data fails here (D28).
    /// </summary>
    [Fact]
    public void The_seven_spine_tusker_profiles_hold_the_W0_counts_gates_and_target_stats()
    {
        // key, entry min, entry max, generator delay (s), slots per wcid, targets: wcid -> (level, health, magic defence)
        var burrow = new Dictionary<uint, int> { [236] = 37, [22589] = 5 };
        var lodge = new Dictionary<uint, int> { [11] = 53, [22597] = 4 };
        var cave = new Dictionary<uint, int> { [1627] = 77, [22585] = 4 };
        var temple = new Dictionary<uint, int> { [235] = 49, [22512] = 40, [1627] = 36, [22511] = 19, [22641] = 1 };
        var abode = new Dictionary<uint, int> { [22600] = 78, [22520] = 39, [22601] = 4 };
        var quarters = new Dictionary<uint, int> { [1628] = 75, [22605] = 4 };
        var holding = new Dictionary<uint, int> { [10810] = 136, [16916] = 44, [22599] = 4 };
        var cases = new (string Key, int? Min, int? Max, double Delay, Dictionary<uint, int> Slots, (uint Wcid, int Level, int Health, int MagicDefence)[] Targets)[]
        {
            ("tusker-burrow", null, null, 300, burrow, [(236, 15, 95, 54), (22589, 15, 95, 54)]),
            ("tusker-lodge", 5, null, 240, lodge, [(11, 20, 100, 63), (22597, 20, 100, 63)]),
            ("tusker-cave", 12, null, 300, cave, [(1627, 30, 110, 94), (22585, 30, 110, 94)]),
            ("tusker-temple-20-40", 20, 40, 300, temple, [(235, 30, 130, 103), (1627, 30, 110, 94), (22511, 40, 90, 109), (22512, 40, 90, 146)]),
            // M6 3 p2: the last three spine profiles (W0: 121 Redeemers L60 17,500 XP, gate 20; 79 Slaves L80 30,000 XP, gate 30; 184 Rampagers L100 80,000 XP, gate 60; each behind one 5-minute generator 7924). Health 250, 400, 435 are W0's;
            // the Magic Defense 171, 219 and 251 are ACE's current values (the W0 rows' 155, 200 and 230 plus (Focus + Self) / 7 = 16, 19 and 21).
            ("tusker-abode", 20, null, 300, abode, [(22520, 60, 250, 171), (22600, 60, 250, 171), (22601, 60, 250, 171)]),
            ("tusker-quarters", 30, null, 300, quarters, [(1628, 80, 400, 219), (22605, 80, 400, 219)]),
            ("tusker-holding", 60, null, 300, holding, [(10810, 100, 435, 251), (16916, 100, 435, 251), (22599, 100, 435, 251)]),
        };
        foreach (var (key, min, max, delay, slots, targets) in cases)
        {
            var ground = GroundCatalog.Get(key);
            Assert.Equal(slots.Values.Sum(), ground.Respawn.Slots.Count);
            var one = Assert.Single(ground.Respawn.Generators);
            Assert.Equal(delay, one.DelaySeconds);
            foreach (var (wcid, count) in slots)
            {
                Assert.True(ground.Respawn.Slots.Count(s => s.Wcid == wcid) == count, $"{key}: slots of wcid {wcid}");
            }

            Assert.NotNull(ground.Entry);
            Assert.Equal(min, ground.Entry.MinLevel);
            Assert.Equal(max, ground.Entry.MaxLevel);
            Assert.Equal(key == "tusker-temple-20-40", ground.Entry.Tieable); // every tusker portal but the Temple 20-40's carries NoRecall (PortalBitmask 0x31), the Temple 20-40's is 0x1
            Assert.Equal(min is null, ground.AdmitsLevel(1)); // a gate with a MinLevel refuses a level 1
            Assert.Equal(max is null, ground.AdmitsLevel(100)); // and one with a MaxLevel refuses a level 100
            Assert.True(ground.AdmitsLevel(max ?? Math.Max(40, min ?? 0)));
            foreach (var (wcid, level, health, magicDefence) in targets)
            {
                var t = Assert.Single(ground.Targets, x => x.Wcid == wcid);
                Assert.Equal((level, health, magicDefence), (t.Level, t.MaxHealth, t.MagicDefence));
                Assert.Equal(wcid == 22512, ground.IsKillFirst(wcid));
            }
        }

        Assert.Equal(GroundRole.Avoid, GroundCatalog.Get("tusker-temple-20-40").Targets.Single(t => t.Wcid == 22641).Role);
    }
}

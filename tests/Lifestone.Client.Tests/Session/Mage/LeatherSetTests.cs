using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 14 (the owner's order of 2026-11-12: leather in place of the plate): the leather set as the Pathwarden quest's file names it (<see cref="LeatherSet"/>: Holtburg's blacksmith, this server's ace_world rows of 2026-11-12), the rule
/// that buys it as one swap only when the purse pays for all of it over the trip's other needs (<see cref="LeatherSwap"/>, on the live first trip's numbers: a purse of 10,009 and 6,095 spent), what it weighs and what it is worth
/// against a Tusker Crimsonback beside the robe and the plate (<see cref="ArmorReport"/>).
/// </summary>
public sealed class LeatherSetTests
{
    private const uint Robe = 40439, LeatherGauntlets = 56, PathwardenGauntlets = 33606, LeatherBoots = 115, Wand = 12748;
    private const uint Cap = 46, Breastplate = 42, Girth = 63, Pauldrons = 89, Bracers = 38, Leggings = 84, StuddedGauntlets = 59, StuddedBoots = 116;

    private static LeatherSet Set => LeatherSet.Default!;

    /// <summary>What the fresh climber holds after her first trip: the robe and the floor's Leather Gauntlets worn, the boots she was created in kept in her pack.</summary>
    private static readonly uint[] WornFresh = [Robe, LeatherGauntlets, Wand];
    private static readonly uint[] HeldFresh = [Robe, LeatherGauntlets, Wand, LeatherBoots];

    [Fact]
    public void The_pathwarden_file_names_the_blacksmiths_set_over_the_whole_body_and_the_plate_is_off()
    {
        var set = Set;
        Assert.Equal("pathwarden", set.Quest);
        Assert.Equal([Cap, Breastplate, Girth, Pauldrons, Bracers, Leggings, StuddedGauntlets, StuddedBoots], set.Pieces.Select(p => p.Wcid));
        Assert.Equal(GearCoverage.Body, set.Coverage); // the robe's parts, and the hands
        Assert.Equal([140, 90, 90, 90, 90, 90, 130, 130], set.Pieces.Select(p => p.ArmourLevel));
        Assert.Equal([120, 675, 350, 350, 330, 900, 450, 690], set.Pieces.Select(p => p.Burden));
        Assert.Equal([92, 1813, 200, 138, 138, 263, 1375, 1563], set.Pieces.Select(p => p.Price)); // max(1, ceil(1.25 x Value - 0.1)): 73, 1450, 160, 110, 110, 210, 1100, 1250
        Assert.Equal([Robe], set.InsteadOf);

        // The fresh climber buys the first six: the gauntlets the floor bought and the boots she was created in fill the hands and the feet.
        Assert.Equal((2_644, 2_725), (set.Pieces.Take(6).Sum(p => p.Price), set.Pieces.Take(6).Sum(p => p.Burden)));
        Assert.Equal(StuddedGauntlets, set.PieceFilledBy(LeatherGauntlets)!.Wcid);
        Assert.Equal(StuddedGauntlets, set.PieceFilledBy(PathwardenGauntlets)!.Wcid);
        Assert.Equal(StuddedBoots, set.PieceFilledBy(LeatherBoots)!.Wcid);
        Assert.Null(set.PieceFilledBy(Robe));
        // The whole set on her: the six (2,725), the Leather Gauntlets 270 and the Leather Boots 420: 3,415 (the plate: 6,540).

        var smith = set.Vendor;
        Assert.Equal(("Sedor Wystan the Blacksmith", 712u, 0x7A9B4026u, 0xA9B40100u), (smith.Name, smith.Wcid, smith.Guid, smith.Cell));
        Assert.Equal((smith.Cell, (System.Numerics.Vector3?)null, 0.95, 0x40040103u), (smith.StopCell, smith.StopPoint, smith.BuyPrice, smith.BuysItemTypes));
        Assert.Equal(new System.Numerics.Vector3(87.48f, 131.04f, 66.005f), smith.Point);
        Assert.Equal(set.Pieces.Select(p => (p.Wcid, p.Price)), smith.Sells.Select(w => (w.Wcid, w.Price))); // his sells are the set's wares at the block's prices: the buy reads them

        // The plate form is off: nothing reads its block as the armour form, so the chest gives the robe and no stop at the bot runs before it; the data is kept.
        Assert.Null(ArmourSet.Of("pathwarden"));
        Assert.Equal(6_540, ArmourSet.OfBlock("pathwarden", ArmourSet.PlateOffBlock)!.Burden);
        Assert.Equal(BotFirstKind.NoSet, ArmourForm.BotFirst(ArmourSet.Of("pathwarden"), botApplies: true, rawStrength: 10, strength: 10).Kind);
        Assert.Equal([Robe, 33606u, 33605u, 41513u], QuestCatalog.Get("pathwarden").Steps.Where(s => s.Kind == QuestStepKind.PickUp).Select(s => s.Item!.Wcid!.Value)); // the chest's pieces as before
    }

    [Fact]
    public void A_malformed_block_fails_by_name_and_a_set_that_leaves_a_part_bare_is_refused()
    {
        Assert.Null(LeatherSet.Parse("""{ "key": "x" }"""));
        Assert.Null(LeatherSet.Of("academy"));

        const string Vendor = """ "vendor": { "name": "V", "wcid": 1, "guid": "0x7A9B4026", "cell": "0xA9B40100", "point": [1, 2, 3], "buys_item_types": "0x2", "buy_price": 0.9 } """;
        static string Piece(uint wcid, uint coverage, string also = "[]") => $$"""{ "wcid": {{wcid}}, "name": "P{{wcid}}", "coverage": {{coverage}}, "locations": 1, "armour_level": 90, "burden": 10, "price": 5, "also": {{also}} }""";
        static string File(string pieces, string vendor, string instead = """ "instead_of": [9] """) => $$"""{ "key": "x", "leather": { {{vendor}}, "pieces": [{{pieces}}], {{instead}} } }""";

        var whole = File(Piece(1, 0x1FF00), Vendor);
        Assert.Single(LeatherSet.Parse(whole)!.Pieces);

        foreach (var bad in new[]
        {
            File(Piece(1, 0x17F00), Vendor), // the hands bare: the swap would bare them
            File(Piece(1, 0x1FF00) + ", " + Piece(2, 0x8000), Vendor), // two pieces on the hands
            File(Piece(1, 0x17F00) + ", " + Piece(2, 0x8000, "[1]"), Vendor), // one weenie class for two parts
            File(Piece(1, 0x1FF08), Vendor), // an underwear bit
            File(Piece(1, 0x1FF00), Vendor, """ "instead_of": [] """), // nothing it replaces
            File(Piece(1, 0x1FF00), Vendor.Replace("\"0xA9B40100\"", "\"A9B40100\"", StringComparison.Ordinal)), // a cell not written 0x
            File(Piece(1, 0x1FF00), Vendor.Replace("[1, 2, 3]", "[1, 2]", StringComparison.Ordinal)),
            File(Piece(1, 0x1FF00), Vendor.Replace("0.9", "1.5", StringComparison.Ordinal)),
            File(string.Empty, Vendor),
        })
        {
            Assert.Contains("TODO(m6-leather-file)", Assert.Throws<FormatException>(() => LeatherSet.Parse(bad)).Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void On_the_live_first_trip_the_set_is_held_back_by_name_and_nothing_of_it_is_bought()
    {
        // 2026-11-12 00:08 UTC: the purse 10,009 after the sale, 86 wares for 6,095 (the Wand, the components, ten Mana Potions, seven scrolls, two Packs).
        var plan = LeatherSwap.Decide(Set, WornFresh, HeldFresh, purse: 10_009, planned: 6_095, waiting: 0, tripSpendPercent: 60);
        Assert.Equal((LeatherSwapKind.HeldBack, "purse", 2_644L, 2_348L), (plan.Kind, plan.Token, plan.Need, plan.Budget));
        Assert.Equal([Cap, Breastplate, Girth, Pauldrons, Bracers, Leggings], plan.Missing.Select(p => p.Wcid));
        Assert.Equal(
            "leather: held back: the 6 missing piece(s) of 8 (Metal Cap 92, Studded Leather Breastplate 1813, Studded Leather Girth 200, Studded Leather Pauldrons 138, Studded Leather Bracers 138, Studded Leather Leggings 263) cost 2644 at Sedor Wystan the Blacksmith; "
            + "the purse 10009 less this trip's other buys 6095 leaves 3914, and the trip's share of it (60%) is 2348, short by 296: none is bought and the robe stays on (a part of the set is never worn alone); it is due when a trip's purse reaches 10502 with these buys",
            plan.Why);
    }

    [Fact]
    public void It_is_due_on_the_first_trip_whose_purse_pays_for_all_of_it_after_the_other_buys_and_the_scrolls_put_off()
    {
        // A later trip: 9,000 in the purse, 2,176 of other buys (the live second trip's): 6,824 left, 4,094 for the set.
        var due = LeatherSwap.Decide(Set, WornFresh, HeldFresh, purse: 9_000, planned: 2_176, waiting: 0, tripSpendPercent: 60);
        Assert.Equal((LeatherSwapKind.Buy, "due", 2_644L, 4_094L), (due.Kind, due.Token, due.Need, due.Budget));
        Assert.Contains("bought on this trip, all of them, and the set goes on in the robe's place as one swap", due.Why, StringComparison.Ordinal);
        Assert.Equal(6, due.Missing.Count);

        // The scrolls the trip put off come first (her Impenetrability III, Bludgeon Bane III and Lifestone Tie at 1,000 each waited on the live third trip): the same purse with 3,000 of scrolls waiting holds the set back.
        var scrollsFirst = LeatherSwap.Decide(Set, WornFresh, HeldFresh, purse: 9_000, planned: 2_176, waiting: 3_000, tripSpendPercent: 60);
        Assert.Equal((LeatherSwapKind.HeldBack, 2_294L), (scrollsFirst.Kind, scrollsFirst.Budget));
        Assert.Contains("and the scrolls it put off 3000 leaves 3824", scrollsFirst.Why, StringComparison.Ordinal);

        // The edge: the share must reach the need, to the pyreal.
        Assert.Equal(LeatherSwapKind.Buy, LeatherSwap.Decide(Set, WornFresh, HeldFresh, 4_407, 0, 0, 60).Kind); // 60% of 4,407 = 2,644
        Assert.Equal(LeatherSwapKind.HeldBack, LeatherSwap.Decide(Set, WornFresh, HeldFresh, 4_406, 0, 0, 60).Kind);
    }

    [Fact]
    public void What_she_holds_is_not_bought_again_and_a_character_without_the_robe_is_not_asked()
    {
        // A piece bought on an earlier trip that could not reach the rest: only the others are missing.
        uint[] partly = [.. HeldFresh, Cap, Girth, Pauldrons, Bracers, Leggings];
        var one = LeatherSwap.Decide(Set, WornFresh, partly, 5_000, 0, 0, 60);
        Assert.Equal((LeatherSwapKind.Buy, 1_813L), (one.Kind, one.Need));
        Assert.Equal([Breastplate], one.Missing.Select(p => p.Wcid));

        // Every piece held: nothing to buy, the swap is made on this trip.
        uint[] all = [.. partly, Breastplate];
        var wear = LeatherSwap.Decide(Set, WornFresh, all, 0, 0, 0, 60);
        Assert.Equal((LeatherSwapKind.Wear, "wear", 0L), (wear.Kind, wear.Token, wear.Need));
        Assert.Empty(LeatherSwap.Missing(Set, all));

        // No gauntlets and no boots held (a climber who sold or dropped them): the blacksmith's own pair and boots are part of the buy.
        uint[] bare = [Robe, Wand];
        var eight = LeatherSwap.Decide(Set, bare, bare, 20_000, 0, 0, 60);
        Assert.Equal((LeatherSwapKind.Buy, 2_644L + 1_375 + 1_563, 8), (eight.Kind, eight.Need, eight.Missing.Count));

        // The den's mage wears a vendor robe, the climber after the swap wears the set: neither wears the Pathwarden Robe, so the rule asks nothing of them.
        Assert.False(LeatherSwap.Applies(Set, [5851u, Wand]));
        Assert.False(LeatherSwap.Applies(Set, all.Where(w => w != Robe)));
        Assert.Equal((LeatherSwapKind.NotHers, "not-hers"), (LeatherSwap.Decide(Set, [5851u], [5851u], 100_000, 0, 0, 60).Kind, LeatherSwap.Decide(Set, [5851u], [5851u], 100_000, 0, 0, 60).Token));
    }

    [Fact]
    public void A_part_another_worn_piece_covers_whole_needs_no_piece_of_the_set()
    {
        // Live 2026-11-12 02:10 UTC: a looted pair of Long Leather Gauntlets worn (no weenie class of the set), the Leather Gauntlets in the pack.
        const uint looted = 9_301;
        (uint Wcid, uint Coverage, uint Location)[] worn = [(Robe, 0x17F00, 0x7F01), (looted, 0x8000, 0x20), (Wand, 0, 0x01000000)];
        var covered = LeatherSwap.CoveredByOthers(Set, worn);
        Assert.Equal(0x8000u, covered); // the hands; the robe is the piece the set replaces, not another's cover
        Assert.True(LeatherSwap.Covers(covered, Set.PieceFilledBy(LeatherGauntlets)!));
        Assert.False(LeatherSwap.Covers(covered, Set.PieceFilledBy(LeatherBoots)!));
        Assert.False(LeatherSwap.Covers(0x100, Set.Pieces.Single(p => p.Wcid == Leggings))); // half of the legs is not the part
        Assert.Equal(0x8000u, LeatherSwap.CoveredByOthers(Set, [(looted, 0, 0x20)])); // a ClothingPriority not read: by its location
        Assert.Equal(0u, LeatherSwap.CoveredByOthers(Set, [(Robe, 0x17F00, 0x7F01), (LeatherGauntlets, 0x8000, 0x20)])); // a piece of the set is the set's, not another's

        // The hands covered and nothing else held: seven missing, the blacksmith's gauntlets not among them.
        uint[] held = [Robe, Wand, looted];
        Assert.DoesNotContain(LeatherSwap.Missing(Set, held, covered), p => p.Wcid == StuddedGauntlets);
        Assert.Equal(7, LeatherSwap.Missing(Set, held, covered).Count);
        Assert.Equal(8, LeatherSwap.Missing(Set, held).Count);

        // The live state after the undo: the six in the pack, the boots in the pack, the looted pair on: every part held or covered, nothing to buy, the swap is made.
        uint[] after = [Robe, Wand, looted, LeatherBoots, Cap, Breastplate, Girth, Pauldrons, Bracers, Leggings];
        var plan = LeatherSwap.Decide(Set, [Robe, Wand, looted], after, purse: 0, planned: 0, waiting: 0, tripSpendPercent: 60, covered);
        Assert.Equal((LeatherSwapKind.Wear, 0L), (plan.Kind, plan.Need));
        Assert.Contains("every one of the 8 parts is held or covered (another worn piece covers 0x8000)", plan.Why, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ what it is worth: robe, leather, plate

    private static List<ArmorPiece> RobeSet => [new("Pathwarden Robe", 0x17F00, 50, 1.0, 50), new("Leather Gauntlets", 0x8000, 130, 1.0, 0)];

    /// <summary>The set as the fresh climber wears it: the six bought pieces, the Leather Gauntlets and the Leather Boots (130 each, as the blacksmith's pair).</summary>
    private static List<ArmorPiece> LeatherWorn => [.. Set.Pieces.Select(p => new ArmorPiece(p.Name, p.Coverage, p.ArmourLevel, 1.0, 0))];

    private static List<ArmorPiece> PlateSet => [.. ArmourSet.OfBlock("pathwarden", ArmourSet.PlateOffBlock)!.Pieces.Select(p => new ArmorPiece(p.Name, p.Coverage, p.ArmourLevel, 1.0, 50))];

    [Theory]
    // Armor Self, Bludgeoning Protection, Impenetrability, Bludgeon Bane; the factor through the robe and gauntlets, the leather, the plate.
    [InlineData(0, 0, 0, 0, 0.397, 0.395, 0.282)] // nothing cast (after a death): the robe's own Impenetrability II makes it the leather's equal
    [InlineData(7, 7, 0, 0, 0.059, 0.059, 0.051)] // level 6: the bot's level VII lines, no item line of her own yet (Item Enchantment is trained at 6; the scrolls wait for coin)
    [InlineData(7, 7, 3, 3, 0.048, 0.042, 0.040)] // level 16: her Impenetrability III and Bludgeon Bane III: the leather 13% under the robe, the plate 17%
    [InlineData(7, 7, 4, 4, 0.042, 0.036, 0.034)] // level 26: at IV (Item Enchantment 231 in the long round of 2026-11-11): 13% and 17%
    public void The_leather_meets_a_bludgeon_swing_as_the_robe_does_until_she_casts_her_own_item_lines_and_then_nearly_as_the_plate(int armor, int protection, int impen, int bane, double robe, double leather, double plate)
    {
        Assert.Equal(robe, ArmorReport.Factor(RobeSet, armor, protection, impen, bane), 3);
        Assert.Equal(leather, ArmorReport.Factor(LeatherWorn, armor, protection, impen, bane), 3);
        Assert.Equal(plate, ArmorReport.Factor(PlateSet, armor, protection, impen, bane), 3);
        Assert.True(ArmorReport.Factor(LeatherWorn, armor, protection, impen, bane) <= ArmorReport.Factor(RobeSet, armor, protection, impen, bane)); // never worse than the robe
    }

    [Fact]
    public void A_Tusker_Crimsonbacks_hardest_swing_through_the_robe_the_leather_and_the_plate_at_levels_6_16_and_26()
    {
        // Tusker Crimsonback 22584: a hand swing is 104 at the most before armour, a critical one 208 (ArmourSetTests has the rows).
        const double most = 40 * (1 + ((200 - 55) * 0.011));
        (double Robe, double Leather, double Plate) Hit(int impen, int bane, double swing) =>
            (Math.Round(swing * ArmorReport.Factor(RobeSet, 7, 7, impen, bane), 1), Math.Round(swing * ArmorReport.Factor(LeatherWorn, 7, 7, impen, bane), 1), Math.Round(swing * ArmorReport.Factor(PlateSet, 7, 7, impen, bane), 1));

        Assert.Equal((6.2, 6.1, 5.2), Hit(0, 0, most)); // level 6
        Assert.Equal((5.0, 4.3, 4.1), Hit(3, 3, most)); // level 16
        Assert.Equal((4.3, 3.7, 3.6), Hit(4, 4, most)); // level 26
        Assert.Equal((10.0, 8.7, 8.3), Hit(3, 3, most * 2)); // a critical swing at level 16
    }

    // ------------------------------------------------------------------------------------------------ what it weighs

    [Theory]
    // The burden after the swap (the base pack 1,440, the trinket 60, the set 3,415) with a stock on top; the load at Strength 50 (the bot's line) and the Strength floor's ranks with and without the bot's line.
    [InlineData(644, "burden 5559 of the capacity 7500 (Strength 50): load 74%, run modifier 1.00", 0, 28)] // the outskirts' first restock
    [InlineData(4_050, "burden 8965 of the capacity 7500 (Strength 50): load 120%, run modifier 0.80 (OVER the line where the run slows)", 10, 50)] // a tusker ground's drinks
    public void The_load_with_the_leather_on_and_the_ranks_the_Strength_floor_buys_with_the_bots_line_and_without(int stock, string text, int ranksWithBot, int ranksWithout)
    {
        const int worn = 1_440 + 60 + 3_415;
        Assert.Equal(text, ArmourForm.LoadText(worn + stock, 50));
        var want = StrengthFloor.Want(worn + stock, 0, 100);
        Assert.Equal(ranksWithBot, StrengthFloor.Ranks(want - StandingStrength.BotPoints, MageTemplate.LowAttribute));
        Assert.Equal(ranksWithout, StrengthFloor.Ranks(want, MageTemplate.LowAttribute)); // the leather casts no Strength on her (the robe and the hauberk cast Strength Self II)
    }

    [Fact]
    public void The_plate_cost_three_times_the_leathers_ranks_at_the_tusker_stock()
    {
        // The same stock under the plate (1,440 + 60 + 6,540 + 4,050 = 12,090): 31 ranks with the bot's line; the leather 10.
        Assert.Equal(31, StrengthFloor.Ranks(StrengthFloor.Want(1_440 + 60 + 6_540 + 4_050, 0, 100) - StandingStrength.BotPoints, MageTemplate.LowAttribute));
        Assert.Equal("burden 8965 of the capacity 1500 (Strength 10): load 598%, run modifier 0.00 (OVER the line where the run slows)", ArmourForm.LoadText(1_440 + 60 + 3_415 + 4_050, 10)); // no bot, no ranks yet
    }

    [Fact]
    public void The_set_worn_leaves_no_part_bare_and_the_floor_asks_for_no_robe_and_no_gauntlets()
    {
        var worn = Set.Pieces.Select((p, i) => new WornPiece(0x90000000 + (uint)i, p.Name, 0x2, p.Locations, p.Coverage, null)).ToList();
        Assert.Equal(0u, GearFloor.BareParts(worn));
        Assert.Equal(0u, GearFloor.BareParts([.. Set.Pieces.Select((p, i) => new WornPiece(0x90000000 + (uint)i, p.Name, 0x2, p.Locations, 0, null))])); // read by their locations when a header carries no ClothingPriority
        Assert.NotNull(GearFloor.ChestPiece(worn)); // the breastplate: the floor's robe line is not due
        Assert.True(GearFloor.HandsCoveredBy(worn));
        Assert.Equal(0x400u, GearFloor.BareParts([.. worn.Where(w => w.Name != "Studded Leather Breastplate")])); // a set short of a piece is named
    }
}

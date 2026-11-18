using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 14 (the owner, 2026-11-11: armour instead of the robe): the Pathwarden chest's plate set as the quest file names it (<see cref="ArmourSet"/>), whether ACE lets a fresh climber lift it (<see cref="ArmourForm"/>), the Strength
/// that carries it beside her ranks (<see cref="StandingStrength"/>), what it is worth against a Tusker Crimsonback's swing (<see cref="ArmorReport"/>), and the two rules that keep it on: a replacement never bares an armoured part
/// (<see cref="SelfEquipJudge"/>) and a bare part is named (<see cref="GearFloor"/>). Every world number is this server's ace_world row (read-only SELECT, 2026-11-11; the quest file's <c>armour.sources</c>); the climber's burden is the
/// live climb's of 2026-11-11 (2,460 before the chest, the Academy Coat 600 and the Leather Boots 420 worn).
/// </summary>
public sealed class ArmourSetTests
{
    private const uint Hauberk = 33597, Leggings = 33601, Helm = 33607, Gauntlets = 33606, Sollerets = 33605, Robe = 40439, Trinket = 41513;
    private const int FreshBurden = 2_460, CoatAndBoots = 1_020;

    /// <summary>The plate set: kept in the quest file under <see cref="ArmourSet.PlateOffBlock"/> since the owner's order of 2026-11-12 (leather, <c>LeatherSetTests</c>); the rules below are the plate form's, kept for the block's return.</summary>
    private static ArmourSet Plate => ArmourSet.OfBlock("pathwarden", ArmourSet.PlateOffBlock)!;

    // ------------------------------------------------------------------------------------------------ the quest file's block

    [Fact]
    public void The_pathwarden_file_names_the_five_plate_pieces_in_lift_order_in_place_of_the_robe()
    {
        var set = Plate;
        Assert.Equal("pathwarden", set.Quest);
        Assert.Equal([Hauberk, Leggings, Helm, Gauntlets, Sollerets], set.Pieces.Select(p => p.Wcid));
        Assert.Equal([2500, 2000, 600, 900, 540], set.Pieces.Select(p => p.Burden));
        Assert.Equal(6_540, set.Burden);
        Assert.All(set.Pieces, p => Assert.Equal(120, p.ArmourLevel));
        Assert.Equal(GearCoverage.Body, set.Coverage); // every part of the body: the robe's 0x17F00 leaves the hands
        Assert.Equal([15, 0, 0, 0, 0], set.Pieces.Select(p => p.Strength)); // the hauberk's Strength Self II is why it is lifted first
        Assert.Equal([Robe], set.InsteadOf);
        Assert.Equal("Pathwarden Helm", set.PieceOf(Helm)!.Name);
        Assert.Null(set.PieceOf(Robe));
    }

    [Fact]
    public void The_armour_form_takes_the_set_then_the_trinket_and_never_the_robe()
    {
        var quest = QuestCatalog.Get("pathwarden");
        var steps = quest.Steps.Where(s => s.Kind == QuestStepKind.PickUp).Select(s => s.Item!).ToList();
        Assert.Equal([Robe, Gauntlets, Sollerets, Trinket], steps.Select(p => p.Wcid!.Value)); // the robe form's pieces: the file's steps, unchanged
        Assert.Equal([Hauberk, Leggings, Helm, Gauntlets, Sollerets, Trinket], Plate.PiecesWith(steps).Select(p => p.Wcid!.Value));
        Assert.Equal(Trinket, quest.Done.Item!.Wcid); // the done condition still reads the trinket
    }

    [Fact]
    public void A_file_with_no_block_has_no_set_and_a_malformed_block_fails_by_name()
    {
        Assert.Null(ArmourSet.Of("academy"));
        Assert.Null(ArmourSet.Parse("""{ "key": "x" }"""));

        static string File(string pieces, string tail = "") => $$"""{ "key": "x", "armour": { "pieces": [{{pieces}}]{{tail}} } }""";
        const string A = """{ "wcid": 1, "name": "A", "coverage": 1024, "armour_level": 100, "burden": 10, "strength": 0 }""";
        Assert.Single(ArmourSet.Parse(File(A))!.Pieces);
        Assert.Empty(ArmourSet.Parse(File(A))!.InsteadOf);

        foreach (var bad in new[]
        {
            File(string.Empty), // no piece
            File(A + ", " + A), // one piece twice (its coverage overlaps itself first)
            File(A + """, { "wcid": 2, "name": "B", "coverage": 1024, "armour_level": 100, "burden": 10, "strength": 0 }"""), // two pieces on the chest: ACE refuses the second
            File("""{ "wcid": 1, "name": "A", "coverage": 8, "armour_level": 100, "burden": 10, "strength": 0 }"""), // an underwear bit: not an armour piece
            File("""{ "wcid": 1, "name": "A", "coverage": 1024, "armour_level": 0, "burden": 10, "strength": 0 }"""), // no armour
            File("""{ "wcid": 1, "coverage": 1024, "armour_level": 100, "burden": 10, "strength": 0 }"""), // no name
            File(A, """, "instead_of": [0]"""),
            File(A, """, "instead_of": 5"""),
        })
        {
            Assert.Contains("TODO(m6-armour-file)", Assert.Throws<FormatException>(() => ArmourSet.Parse(bad)).Message, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------------------------------------ whether she can lift it, and what it weighs

    [Fact]
    public void A_fresh_climber_lifts_the_set_once_the_coat_and_boots_are_shed_and_the_load_is_said_over_the_line()
    {
        // Strength 10: capacity 1,500, ACE's limit 4,500. 2,460 - 1,020 + the hauberk 2,500 = 3,940: lifted; worn, its Strength Self II makes Strength 25 (3,750, limit 11,250) and the rest follows.
        var choice = ArmourForm.Choose(Plate, FreshBurden, CoatAndBoots, rawStrength: 10, strength: 10);
        Assert.Equal(ArmourFormKind.Armour, choice.Kind);
        Assert.Equal(FreshBurden - CoatAndBoots + 6_540, choice.BurdenAfter);
        Assert.Equal((25, 3_750), (choice.StrengthAfter, choice.CapacityAfter));
        Assert.Contains("burden 7980 of the capacity 3750 (Strength 25): load 213%, run modifier 0.00 (OVER the line where the run slows)", choice.Why, StringComparison.Ordinal);

        // The buff bot's Strength line on her (+40): Strength 50, capacity 7,500: the set and her pack are 106% of it.
        var withBot = ArmourForm.Choose(Plate, FreshBurden, CoatAndBoots, rawStrength: 10, strength: 50);
        Assert.Equal((ArmourFormKind.Armour, 50, 7_500), (withBot.Kind, withBot.StrengthAfter, withBot.CapacityAfter)); // one Strength line counts, the strongest: 50, not 65
        Assert.Contains("load 106%, run modifier 0.94 (OVER", withBot.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_first_piece_ACE_would_refuse_is_the_robe_form_by_name_and_no_part_of_the_set_is_taken()
    {
        // Nothing shed: 2,460 + 2,500 = 4,960 is over the 4,500 a Strength of 10 may lift (Player_Inventory.cs:53-76).
        var choice = ArmourForm.Choose(Plate, FreshBurden, 0, rawStrength: 10, strength: 10);
        Assert.Equal(ArmourFormKind.Robe, choice.Kind);
        Assert.Equal((FreshBurden, 10), (choice.BurdenAfter, choice.StrengthAfter));
        Assert.Contains("the Pathwarden Plate Hauberk (33597) cannot be lifted: burden 2460 + 2500 is over ACE's limit 4500 (3 x the capacity of Strength 10)", choice.Why, StringComparison.Ordinal);
        Assert.Contains("the robe form by name", choice.Why, StringComparison.Ordinal);

        // The order is the reason: the same pieces with the hauberk last stop at the gauntlets (1,440 + 2,000 + 600 + 900 = 4,940).
        var set = Plate;
        var hauberkLast = set with { Pieces = [.. set.Pieces.Skip(1), set.Pieces[0]] };
        var late = ArmourForm.Choose(hauberkLast, FreshBurden, CoatAndBoots, rawStrength: 10, strength: 10);
        Assert.Equal(ArmourFormKind.Robe, late.Kind);
        Assert.Contains("the Pathwarden Gauntlets (33606) cannot be lifted: burden 4040 + 900", late.Why, StringComparison.Ordinal);

        Assert.Equal(ArmourFormKind.Robe, ArmourForm.Choose(null, FreshBurden, 0, 10, 10).Kind); // a quest with no block: its own pieces
        Assert.Equal(3, ArmourForm.LiftLimit);
    }

    [Theory]
    [InlineData(1_500, 10, "burden 1500 of the capacity 1500 (Strength 10): load 100%, run modifier 1.00")] // exactly the capacity: the modifier is still 1
    [InlineData(8_040, 50, "burden 8040 of the capacity 7500 (Strength 50): load 107%, run modifier 0.93 (OVER the line where the run slows)")] // after the chest, the bot's line on
    [InlineData(8_040, 25, "burden 8040 of the capacity 3750 (Strength 25): load 214%, run modifier 0.00 (OVER the line where the run slows)")] // after the chest, the hauberk's own line alone
    [InlineData(2_344, 25, "burden 2344 of the capacity 3750 (Strength 25): load 63%, run modifier 1.00")] // the robe form's first spend, 2026-11-11
    public void The_load_line_says_the_burden_the_capacity_and_the_run_modifier(int burden, int strength, string text) =>
        Assert.Equal(text, ArmourForm.LoadText(burden, strength));

    // ------------------------------------------------------------------------------------------------ the first live run (2026-11-11 23:40 UTC): the shed, and the bot before the chest

    /// <summary>
    /// What the fresh climber held at the chest in the live run, as its log read it: the Leather Boots (wcid 115, burden 420, the feet 0x10000) worn, the Academy Coat (burden 600; weenie 13210, ClothingPriority 15360 = 0x3C00) in her
    /// PACK, never on her, the starter shirt and pants (underwear), the Training Wand and the trinket's place free; a burden of 2,460 in all.
    /// </summary>
    private static List<PathwardenWear.HeldPiece> FreshInventory =>
    [
        new(0xA1, 100, "Shirt", 0x68, 30, Worn: true),
        new(0xA2, 101, "Pants", 0x06, 135, Worn: true),
        new(0x80001217, 115, "Leather Boots", 0x10000, 420, Worn: true),
        new(0xA4, 12748, "Training Wand", 0, 50, Worn: true),
        new(0x80000A7F, 13210, "Academy Coat", 0x3C00, 600, Worn: false),
        new(0xA6, 33608, "Pathwarden Supply Key", 0, 10, Worn: false),
    ];

    [Fact]
    public void The_shed_before_the_first_lift_takes_the_coat_from_her_pack_as_well_as_the_boots_from_her_feet()
    {
        var set = Plate;
        HashSet<uint> quest = [.. set.Pieces.Select(p => p.Wcid), .. set.InsteadOf, Trinket];
        var shed = PathwardenWear.ShedFirst(FreshInventory, set.Coverage, quest);
        Assert.Equal([("Leather Boots", true), ("Academy Coat", false)], shed.Select(h => (h.Name, h.Worn))); // the worn one first: it is taken off, then dropped
        Assert.Equal(CoatAndBoots, shed.Sum(h => h.Burden));

        // The live run's shed read the worn pieces alone: the boots, 420. The forecast it logged, to the number, and what the whole shed gives.
        var live = ArmourForm.Choose(set, FreshBurden, 420, rawStrength: 10, strength: 10);
        Assert.Equal(ArmourFormKind.Robe, live.Kind);
        Assert.Contains("the Pathwarden Plate Hauberk (33597) cannot be lifted: burden 2040 + 2500 is over ACE's limit 4500 (3 x the capacity of Strength 10)", live.Why, StringComparison.Ordinal);
        Assert.Equal(ArmourFormKind.Armour, ArmourForm.Choose(set, FreshBurden, shed.Sum(h => h.Burden), rawStrength: 10, strength: 10).Kind); // 1,440 + 2,500 = 3,940 of 4,500

        // A quest piece is never shed, worn or carried (a robe of an earlier run is the wear's named blocker), nor a piece that covers no part of the set.
        List<PathwardenWear.HeldPiece> withRobe = [.. FreshInventory, new(0xB1, Robe, "Pathwarden Robe", 0x17F00, 200, Worn: false), new(0xB2, Gauntlets, "Pathwarden Gauntlets", 0x8000, 900, Worn: true)];
        Assert.Equal(["Leather Boots", "Academy Coat"], PathwardenWear.ShedFirst(withRobe, set.Coverage, quest).Select(h => h.Name));
        Assert.Empty(PathwardenWear.ShedFirst(FreshInventory, 0x4000, quest)); // a helm alone: nothing she holds covers the head
    }

    [Fact]
    public void The_bot_comes_before_the_chest_when_the_set_is_wanted_and_its_stop_applies()
    {
        var set = Plate;
        var stop = ArmourForm.BotFirst(set, botApplies: true, rawStrength: 10, strength: 10);
        Assert.Equal((BotFirstKind.Stop, "failed"), (stop.Kind, stop.Field)); // the field reads failed until the stop's own check after says ok
        Assert.Contains("the stop runs before the chest: Strength 10 (raw 10, the capacity 1500) is to hold the bot's line (+40: 50, the capacity 7500)", stop.Why, StringComparison.Ordinal);

        // The check after: her Strength holds the bot's line, or a stronger one; her own Strength Self II from a worn piece (+15) is not it.
        Assert.True(ArmourForm.BotStrengthUp(10, 50));
        Assert.True(ArmourForm.BotStrengthUp(18, 60));
        Assert.False(ArmourForm.BotStrengthUp(10, 25));
        Assert.False(ArmourForm.BotStrengthUp(10, 10));

        var up = ArmourForm.BotFirst(set, botApplies: true, rawStrength: 10, strength: 50);
        Assert.Equal((BotFirstKind.AlreadyUp, "up"), (up.Kind, up.Field));

        var off = ArmourForm.BotFirst(set, botApplies: false, rawStrength: 10, strength: 10);
        Assert.Equal((BotFirstKind.Off, "off"), (off.Kind, off.Field));
        Assert.Contains("the chest is opened on her own Strength 10 (the capacity 1500), the shed first", off.Why, StringComparison.Ordinal);

        var none = ArmourForm.BotFirst(null, botApplies: true, rawStrength: 10, strength: 10);
        Assert.Equal((BotFirstKind.NoSet, "n/a"), (none.Kind, none.Field));

        // With the bot's line on her nothing at the chest is marginal: the whole set and her whole pack, nothing shed, is a third of what she may lift, and she stands at 120% of her load, not over 200%.
        var lifted = ArmourForm.Choose(set, FreshBurden, 0, rawStrength: 10, strength: 50);
        Assert.Equal((ArmourFormKind.Armour, FreshBurden + 6_540, 7_500), (lifted.Kind, lifted.BurdenAfter, lifted.CapacityAfter));
        Assert.True(lifted.BurdenAfter < ArmourForm.LiftLimit * 7_500 / 2);
    }

    [Fact]
    public void The_headless_journey_tells_the_Pathwarden_station_whether_the_bots_stop_applies_by_the_autopilots_own_option()
    {
        Assert.True(HeadlessJourneyScenario.BotStopOf(JourneyPlans.Climber, [])!()); // on by default
        Assert.True(HeadlessJourneyScenario.BotStopOf(JourneyPlans.Climber, ["--autopilot-buff-bot=on"])!());
        Assert.False(HeadlessJourneyScenario.BotStopOf(JourneyPlans.Climber, ["--autopilot-buff-bot=off"])!());
        Assert.True(HeadlessJourneyScenario.BotStopOf(JourneyPlans.Climber with { Name = "another-climber", Character = "Another" }, [])!()); // by the plan's station, not by a name
        Assert.All(JourneyPlans.All.Where(p => !p.Stations.Any(s => s.Kind == StationKinds.Pathwarden)), p => Assert.Null(HeadlessJourneyScenario.BotStopOf(p, []))); // every other plan: as before
        Assert.Equal(300, JourneyPlans.Climber.Stations.Single(s => s.Kind == StationKinds.Pathwarden).ExpectedSeconds); // the stop's seventeen casts are in the station's time
    }

    [Theory]
    // After the set is worn with the bot's Strength up (1,440 + 6,540 + the trinket 60 = 8,040), the first restock on top; the Strength floor's ranks with the bot's line counted.
    [InlineData(0, "burden 8040 of the capacity 7500 (Strength 50): load 107%, run modifier 0.93 (OVER the line where the run slows)", 4)]
    [InlineData(644, "burden 8684 of the capacity 7500 (Strength 50): load 116%, run modifier 0.84 (OVER the line where the run slows)", 8)] // the outskirts' first restock as measured on 2026-11-11
    [InlineData(4_050, "burden 12090 of the capacity 7500 (Strength 50): load 161%, run modifier 0.39 (OVER the line where the run slows)", 31)] // a tusker ground's stock: the tinctures and potions
    public void The_load_after_the_first_restock_with_the_bots_Strength_and_the_ranks_the_floor_then_buys(int restock, string text, int ranks)
    {
        const int worn = FreshBurden - CoatAndBoots + 6_540 + 60;
        Assert.Equal(text, ArmourForm.LoadText(worn + restock, 50));
        Assert.Equal(ranks, StrengthFloor.Ranks(StrengthFloor.Want(worn + restock, 0, 100) - StandingStrength.BotPoints, MageTemplate.LowAttribute));
    }

    [Fact]
    public void The_same_tusker_stock_under_the_robe_cost_the_floor_as_many_ranks_so_the_set_is_carried_by_the_bots_line()
    {
        // The robe form: 1,440 + the robe 200 + the trinket 60 + a bought pair of gauntlets 270 + the stock 4,050 = 6,020, on the base Strength alone (the floor before this item): 41, 31 ranks. The set, with the bot's line counted: 31.
        Assert.Equal(31, StrengthFloor.Ranks(StrengthFloor.Want(1_440 + 200 + 60 + 270 + 4_050, 0, 100), MageTemplate.LowAttribute));
    }

    // ------------------------------------------------------------------------------------------------ the Strength that stands beside the ranks

    private static EnchantmentDesc Strength(ushort spell, float points, double duration) =>
        new(spell, 1, 1, 0, 50, 0, duration, 0x50000001, 0, 0, 0, 0x9001, 1, points, 0, EnchantmentBucket.Additive);

    [Fact]
    public void The_floor_counts_a_worn_pieces_lasting_Strength_line_and_the_bots_while_its_stop_applies_never_her_own_timed_cast()
    {
        Assert.Equal(40, StandingStrength.BotPoints); // the bot's Strength line: the Other spell 2086, the value of the Self spell beside it (2087, Might of the Lugians)
        var hauberkLine = Strength(1328, 15, -1); // Strength Self II from the worn hauberk: no end
        var ownCast = Strength(1329, 20, 1800) with { PowerLevel = 100 }; // her own Strength Self III: it lapses, so it is not counted

        Assert.Equal(0, StandingStrength.Points([], 10, botApplies: false, out _));
        Assert.Equal(0, StandingStrength.Points([ownCast], 10, botApplies: false, out _));
        Assert.Equal(15, StandingStrength.Points([hauberkLine, ownCast], 10, botApplies: false, out var why));
        Assert.Contains("the worn pieces' own lasting Strength line +15", why, StringComparison.Ordinal);
        Assert.Contains("the stop at the bot does not apply", why, StringComparison.Ordinal);

        // The bot's line counts whether or not it is in effect at the read (a death strips it until the stop): the stronger of the two, never their sum.
        Assert.Equal(40, StandingStrength.Points([hauberkLine], 10, botApplies: true, out why));
        Assert.Contains("the buff bot's line +40", why, StringComparison.Ordinal);
        Assert.Equal(40, StandingStrength.Points([], 10, botApplies: true, out _));
    }

    [Fact]
    public void With_the_standing_Strength_the_floor_of_the_armoured_climber_is_eight_ranks_not_forty_eight()
    {
        // The first spend after the chest: the robe form measured 2,344 (2026-11-11); the set in place of the robe and gauntlets-to-come: 2,344 - 200 + 6,540 = 8,684.
        const int burden = 2_344 - 200 + 6_540;
        var total = StrengthFloor.Want(burden, 0, 100);
        Assert.Equal(58, total); // 58 x 150 = 8,700
        Assert.Equal(48, StrengthFloor.Ranks(total, MageTemplate.LowAttribute)); // the floor on the base Strength alone
        Assert.Equal(8, StrengthFloor.Ranks(total - StandingStrength.BotPoints, MageTemplate.LowAttribute)); // with the bot's line counted
        Assert.Equal(33, StrengthFloor.Ranks(total - 15, MageTemplate.LowAttribute)); // with the hauberk's own line alone (the stop at the bot off): TODO(m6-armour-bot-off)
    }

    // ------------------------------------------------------------------------------------------------ what it is worth: a Tusker Crimsonback's swing

    private static readonly ArmorPiece RobePiece = new("Pathwarden Robe", 0x17F00, 50, 1.0, 50);

    private static ArmorPiece PlatePiece(uint wcid) => Plate.PieceOf(wcid) is { } p ? new ArmorPiece(p.Name, p.Coverage, p.ArmourLevel, 1.0, 50) : throw new InvalidOperationException();

    private static List<ArmorPiece> RobeSet => [RobePiece, PlatePiece(Gauntlets)]; // what she wore on 2026-11-11: the robe and the gauntlets

    private static List<ArmorPiece> PlateSet => [.. Plate.Pieces.Select(p => PlatePiece(p.Wcid))];

    [Fact]
    public void A_creatures_swings_fall_on_the_nine_parts_by_ACEs_height_roll()
    {
        var shares = ArmorReport.HitShares.ToDictionary(p => p.Part, p => p.Share);
        Assert.Equal(1.0, shares.Values.Sum(), 9);
        Assert.Equal(1.0 / 9, shares["head"], 9); // a third of the swings are high, a third of those the head
        Assert.Equal((1.0 / 9) + (1.0 / 18), shares["chest"], 9); // high and medium
        Assert.Equal(shares["chest"], shares["upper arm"], 9);
        Assert.Equal(1.0 / 18, shares["hand"], 9);
        Assert.Equal(1.0 / 6, shares["foot"], 9); // a third are low: the foot or the lower leg
        Assert.Equal(1.0 / 6, shares["lower leg"], 9);
        Assert.Equal(4.0 / 9, ArmorReport.HitShares.Where(p => (p.Coverage & Plate.PieceOf(Hauberk)!.Coverage) != 0).Sum(p => p.Share), 9); // the hauberk takes four swings in nine
    }

    [Theory]
    // Armor Self, Bludgeoning Protection, Impenetrability, Bludgeon Bane; the robe and gauntlets' factor, the plate's.
    [InlineData(0, 0, 0, 0, 0.393, 0.282)] // nothing cast (after a death): the pieces' own Impenetrability II alone: 28% less through the plate
    [InlineData(3, 3, 3, 3, 0.134, 0.103)] // her own level III lines: 23% less
    [InlineData(7, 7, 0, 0, 0.059, 0.051)] // the bot's Armor and Protection, no item line of her own yet: 14% less
    [InlineData(7, 7, 3, 3, 0.048, 0.040)] // the bot's lines and her Impenetrability III and Bludgeon Bane III: 17% less
    [InlineData(7, 7, 4, 4, 0.042, 0.034)] // and at IV: 17% less
    public void The_plate_set_lets_less_of_a_bludgeon_swing_through_than_the_robe_at_every_stage(int armor, int protection, int impen, int bane, double robe, double plate)
    {
        Assert.Equal(robe, ArmorReport.Factor(RobeSet, armor, protection, impen, bane), 3);
        Assert.Equal(plate, ArmorReport.Factor(PlateSet, armor, protection, impen, bane), 3);
        Assert.True(plate < robe);
    }

    [Fact]
    public void A_Tusker_Crimsonbacks_swing_through_the_robe_and_through_the_plate()
    {
        // Tusker Crimsonback 22584: Strength 200, its hand 40 and its foot 35 of bludgeon damage, variance 0.75 (weenie_properties_body_part and _attribute); ACE's attribute modifier 1 + (200 - 55) x 0.011 = 2.595
        // (SkillFormula.GetAttributeMod), so a hand swing is 26 to 104 before armour and a critical one 208 (twice the most, DamageEvent.cs:270-288 @95ef426d).
        const double most = 40 * (1 + ((200 - 55) * 0.011)), critical = most * 2;
        Assert.Equal(103.8, most, 1);

        double Robe(int a, int p, int i, int b) => ArmorReport.Factor(RobeSet, a, p, i, b);
        double Plate(int a, int p, int i, int b) => ArmorReport.Factor(PlateSet, a, p, i, b);

        // With nothing cast (the minutes after a death): the most 41 through the robe and 29 through the plate; a critical swing 82 and 58.
        Assert.Equal((41, 29), ((int)Math.Round(most * Robe(0, 0, 0, 0)), (int)Math.Round(most * Plate(0, 0, 0, 0))));
        Assert.Equal((82, 58), ((int)Math.Round(critical * Robe(0, 0, 0, 0)), (int)Math.Round(critical * Plate(0, 0, 0, 0))));

        // With the bot's Armor and Bludgeoning Protection VII and her own level III item lines: the most 5 and 4, a critical swing 10 and 8.
        Assert.Equal((5, 4), ((int)Math.Round(most * Robe(7, 7, 3, 3)), (int)Math.Round(most * Plate(7, 7, 3, 3))));
        Assert.Equal((10, 8), ((int)Math.Round(critical * Robe(7, 7, 3, 3)), (int)Math.Round(critical * Plate(7, 7, 3, 3))));
    }

    [Fact]
    public void A_set_short_of_a_piece_is_worse_on_that_part_than_the_robe_and_one_whole_body_piece_reads_as_the_robes_own_form()
    {
        var noHelm = PlateSet.Where(p => p.Coverage != 0x4000).ToList();
        Assert.Equal(1.0, ArmorReport.PartFactor(noHelm, 0x4000, 0, 0, 0, 0), 9); // a bare head meets the whole swing
        Assert.Equal(0.4, ArmorReport.PartFactor(RobeSet, 0x4000, 0, 0, 0, 0), 3); // the robe's 50 and its own Impenetrability II
        Assert.True(ArmorReport.Factor(noHelm, 0, 0, 0, 0) > ArmorReport.Factor(PlateSet, 0, 0, 0, 0));

        List<ArmorPiece> whole = [new("a robe over every part", GearCoverage.Body, ArmorReport.RobeArmor, 1.0, 0)];
        Assert.Equal(ArmorReport.Factor(4, 4, 3, 3), ArmorReport.Factor(whole, 4, 4, 3, 3), 9);
        Assert.Equal(ArmorReport.Factor(8, 8, 8, 8), ArmorReport.Factor(whole, 9, 9, 9, 9), 9); // a level past VIII reads as VIII
        Assert.Equal(0.028, ArmorReport.Factor(7, 7, 7, 7), 3); // level VII is in the table now (Armor 225, Protection 0.35, Impenetrability 220, Bane 1.7 capped at a protection of 2)
    }

    private static Appraisal Appraised(uint guid, int armorLevel, int burden, int mana, bool attuned, params uint[] spells) => new(
        new IdentifyResponseMsg(
            guid,
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable | IdentifyResponseFlags.SpellBook | IdentifyResponseFlags.ArmorProfile,
            true,
            new Dictionary<uint, int> { [5] = burden, [28] = armorLevel, [107] = mana, [108] = 1000, [114] = attuned ? 1 : 0 }.ToImmutableDictionary(),
            ImmutableDictionary<uint, long>.Empty,
            ImmutableDictionary<uint, bool>.Empty,
            ImmutableDictionary<uint, double>.Empty,
            ImmutableDictionary<uint, string>.Empty,
            ImmutableDictionary<uint, uint>.Empty,
            [.. spells],
            new AppraisalArmorProfile(1.3f, 1f, 1f, 0.4f, 0.4f, 0.6f, 1f, 0.4f),
            null,
            null,
            null,
            null,
            null,
            null,
            null),
        1,
        0);

    /// <summary>The pieces' ValidLocations (PropertyInt 9 of the same rows): the hauberk 7680, the leggings 24576, the helm 1, the gauntlets 32, the sollerets 256.</summary>
    private static uint LocationOf(uint wcid) => wcid switch { Hauberk => 0x1E00, Leggings => 0x6000, Helm => 0x1, Gauntlets => 0x20, _ => 0x100 };

    private static WornPiece WornPlate(uint wcid, int mana = 1000) =>
        Plate.PieceOf(wcid) is { } p ? new WornPiece(0x90000000 + wcid, p.Name, 0x2, LocationOf(wcid), p.Coverage, Appraised(0x90000000 + wcid, p.ArmourLevel, p.Burden, mana, attuned: true, 1328, 1482)) : throw new InvalidOperationException();

    [Fact]
    public void The_report_reads_the_worn_pieces_from_their_appraisals_and_names_what_is_unread_and_bare()
    {
        List<WornPiece> worn =
        [
            WornPlate(Hauberk),
            WornPlate(Leggings, mana: 0), // out of mana: its own Impenetrability II is off
            new(0x90000001, "Pathwarden Helm", 0x2, 0x1, 0x4000, null), // not appraised: counted unread, never guessed
            new(0x90000002, "Wand", 0x8000, 0x01000000, 0, null), // a caster: no part of the body
            new(0x90000003, "Pathwarden Trinket", 0x8, 0x04000000, 0, null),
        ];
        var read = ArmorReport.Read(worn, out var unread);
        Assert.Equal(1, unread);
        Assert.Equal([("Pathwarden Plate Hauberk", 120, 50), ("Pathwarden Plate Leggings", 120, 0)], read.Select(p => (p.Name, p.ArmorLevel, p.OwnImpenetrability)));
        Assert.All(read, p => Assert.Equal(1.0, p.BludgeonMod));

        Assert.Equal("armor_buffs=7:7:3:3 damage_factor=unread armor_pieces=2 armor_unread=1 bare=unread", ArmorReport.Line(read, unread, 7, 7, 3, 3)); // a piece not appraised: no factor on part of what she wears
        var line = ArmorReport.Line(read, 0, 7, 7, 3, 3);
        Assert.StartsWith("armor_buffs=7:7:3:3 damage_factor=0.", line, StringComparison.Ordinal);
        Assert.EndsWith(" armor_pieces=2 armor_unread=0 bare=0x1C000", line, StringComparison.Ordinal); // the head, the hands and the feet are not covered
        Assert.Equal("armor_buffs=7:7:3:3 damage_factor=0.040 armor_pieces=5 armor_unread=0 bare=0x0", ArmorReport.Line(PlateSet, 0, 7, 7, 3, 3));
        Assert.Equal("armor_buffs=4:4:3:3 damage_factor=0.107", ArmorReport.Line(4, 4, 3, 3)); // the robe's own line, as before
    }

    // ------------------------------------------------------------------------------------------------ kept on: the judge and the floor

    private sealed class Climber(int strength) : IGearSkills
    {
        public int Level => 10;

        public int AdvancementClass(uint skill) => 3;

        public int? Current(uint skill) => 100;

        public int? Base(uint skill) => 100;

        public int? Attribute(uint attribute) => attribute == 1 ? strength : 100;
    }

    private static GearCandidate Loot(uint guid, string name, uint locations, uint coverage, int armorLevel, int burden) =>
        new(guid, name, 0x2, locations, coverage, burden, Appraised(guid, armorLevel, burden, 0, attuned: false));

    [Fact]
    public void A_looted_piece_never_takes_off_a_worn_piece_that_armours_parts_it_does_not_cover()
    {
        var judge = new SelfEquipJudge([], 0x10, _ => null, [0x4u]); // a tusker ground: bludgeon
        var climber = new Climber(strength: 10); // the base Strength the score weighs a burden against: the hauberk's 2,500 is over its 1,500, so the hauberk scores nothing and any chest piece out-scores it
        var hauberk = WornPlate(Hauberk);

        // A breastplate covers the chest alone: it out-scores the hauberk, and wearing it would leave the abdomen and the arms with no armour.
        var breastplate = Loot(0x91000001, "Breastplate", 0x200, 0x400, 150, 400);
        var refused = judge.Judge(breastplate, [hauberk], climber);
        Assert.Equal(SelfEquipKind.None, refused.Kind);
        Assert.Contains("armours 0x3800 more of the body (armour level 120) which this one does not cover: not replaced", refused.Why, StringComparison.Ordinal);

        // A coat over the same four parts, plainly better and lighter, does replace it.
        var coat = Loot(0x91000002, "Coat", 0x1E00, 0x3C00, 300, 400);
        var replaced = judge.Judge(coat, [hauberk], climber);
        Assert.True(replaced.Kind == SelfEquipKind.Replace, replaced.Why);
        Assert.Equal(hauberk.Guid, replaced.Replaces);

        // A lighter coat of less armour out-scores the hauberk too (the score weighs the hauberk's 2,500 against the base Strength), but the hauberk is attuned: taken off it is still carried, so armour is not traded for a burden that is not saved.
        var weaker = judge.Judge(Loot(0x91000004, "Leather Coat", 0x1E00, 0x3C00, 100, 400), [hauberk], climber);
        Assert.Equal(SelfEquipKind.None, weaker.Kind);
        Assert.Contains("its armour 400 is under that piece's 680 and that piece is attuned", weaker.Why, StringComparison.Ordinal);

        // One part for one part is judged as before: better gauntlets replace the Pathwarden pair.
        Assert.Equal(SelfEquipKind.Replace, judge.Judge(Loot(0x91000003, "Gauntlets", 0x20, 0x8000, 300, 200), [WornPlate(Gauntlets)], climber).Kind);
    }

    [Fact]
    public void The_floor_reads_the_bare_parts_and_names_one_it_has_no_line_for()
    {
        Assert.Equal(0u, GearFloor.BareParts([.. Plate.Pieces.Select(p => WornPlate(p.Wcid))]));
        Assert.Equal(0x8000u, GearFloor.BareParts([new WornPiece(1, "Pathwarden Robe", 0x4, 0x7F01, 0x17F00, null)])); // the robe leaves the hands
        Assert.Equal(0xC000u, GearFloor.BareParts([new WornPiece(1, "a robe with no ClothingPriority read", 0x4, 0x7F00, 0, null)])); // read by its location: the body and the feet (0x7F00), so the head and the hands are bare
        Assert.Equal(0x13F00u, GearCoverage.OfLocation(0x7F00));
        Assert.Equal(0u, GearCoverage.OfLocation(0x01000000)); // a held slot covers no part
        Assert.Equal(0x4000u, GearCoverage.OfWorn(0x4000, 0x7F00)); // a ClothingPriority read wins
        Assert.Equal(GearCoverage.Body, GearFloor.BareParts([]));
        Assert.Equal("head, foot", GearFloor.PartNames(0x14000));
        Assert.Equal(string.Empty, GearFloor.PartNames(0));

        var ground = GroundCatalog.Get("holtburg-outskirts");
        var floor = GearFloor.FloorLevel(LadderLoader.WarMage);
        var plated = new GearFloorState(10, GearFloor.CastersWanted, true, "Pathwarden Plate Hauberk", 10_000);
        var whole = GearFloor.Plan(ground, plated, floor);
        Assert.Empty(whole.Lines); // the set on: no robe, no gauntlets
        Assert.Empty(whole.Notes);
        Assert.DoesNotContain("bare parts", whole.Why, StringComparison.Ordinal);

        var noHelm = GearFloor.Plan(ground, plated with { Bare = 0x4000 }, floor);
        Assert.Empty(noHelm.Lines);
        Assert.Contains(noHelm.Notes, n => n.StartsWith("gear floor: armour: the worn pieces leave 0x4000 of the body bare (head)", StringComparison.Ordinal) && n.Contains("TODO(m6-armour-floor-vendor)", StringComparison.Ordinal));
        Assert.Contains("bare parts 0x4000", noHelm.Why, StringComparison.Ordinal);

        Assert.DoesNotContain(GearFloor.Plan(ground, plated with { Bare = 0x8000, HandsCovered = false }, floor).Notes, n => n.Contains("gear floor: armour:", StringComparison.Ordinal)); // the hands are the gauntlets line's
        Assert.Empty(GearFloor.Plan(ground, plated with { Level = floor - 1, Bare = 0x4000 }, floor).Notes); // under the floor level: nothing asked
    }
}

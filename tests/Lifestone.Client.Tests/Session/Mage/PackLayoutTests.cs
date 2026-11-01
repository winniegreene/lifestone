using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 D121 (the owner, polish point 1): the packs, one job each. The main pack holds the loot to sell, the coins and the notes; the three side packs take the components, the kit and the review finds, chosen by their contents and else by
/// their slot order; the start-up audit's sort moves what it finds misplaced to its pack, counted per pack's free slots. The case is the database's BSD Mage of 2026-10-29: two Packs and a Sack, all empty, and 88 items in the main pack.
/// </summary>
public sealed class PackLayoutTests
{
    private const uint Me = 0x50000001;
    private const uint Pack1 = 0x80001001, Pack2 = 0x80001002, Sack = 0x80001003;

    private static readonly PackRules Rules = new(GroundCatalog.Get("black-spawn-den-aluvian"));

    private static readonly HashSet<uint> Aside = [0x90000009, 0x9000000A];

    private static bool KeptAside(uint guid) => Aside.Contains(guid);

    private static bool None(uint guid) => false;

    private static PackItem InMain(uint guid, string name, uint wcid, uint type) => new(guid, name, wcid, type, Me);

    /// <summary>The database's main pack, in short: a stack of each stock line, a mana stone, a note, coins, looted shoes, the spare wand and two finds worth a look.</summary>
    private static List<PackItem> Database() =>
    [
        InMain(0x90000001, "Pyreal Scarab", 690, 0x1000),
        InMain(0x90000002, "Prismatic Taper", 20631, 0x1000),
        InMain(0x90000003, "Mana Potion", 379, 0x20),
        InMain(0x90000004, "Greater Mana Stone", 2436, 0x80000),
        InMain(0x90000005, "Trade Note (250,000)", 20630, 0x40000),
        InMain(0x90000006, "Pyreal", 273, 0x40),
        InMain(0x90000007, "Shoes", 132, 0x4),
        InMain(0x90000008, "Wand", 2472, 0x8000),
        InMain(0x90000009, "Compass", 9_001, 0x8),
        InMain(0x9000000A, "Fire Sceptre", 9_002, 0x8000),
        InMain(0x9000000B, "Healing Kit", 628, 0x80),
    ];

    private static List<SidePack> EmptyPacks() => [new(Pack1, "Pack", 24, 0), new(Pack2, "Pack", 24, 0), new(Sack, "Sack", 24, 0)];

    [Fact]
    public void Empty_side_packs_take_the_jobs_in_their_slot_order_and_the_why_is_logged()
    {
        var layout = PackLayout.Assign(Me, 102, 88, EmptyPacks(), Database(), Rules, KeptAside, None);

        Assert.Equal(Pack1, layout.ContainerOf(PackJob.Components));
        Assert.Equal(Pack2, layout.ContainerOf(PackJob.Kit));
        Assert.Equal(Sack, layout.ContainerOf(PackJob.Review));
        Assert.Equal(Me, layout.ContainerOf(PackJob.Main));
        Assert.Equal("components='Pack' 0x80001001 (side slot 1); kit='Pack' 0x80001002 (side slot 2); review='Sack' 0x80001003 (side slot 3)", layout.Why);
        Assert.Equal("packs free: main=14/102 components=24/24 kit=24/24 review=24/24", layout.SlotsLine());
    }

    [Fact]
    public void A_pack_that_holds_a_jobs_items_keeps_that_job_whatever_its_slot()
    {
        var items = new List<PackItem> { new(0x90000001, "Pyreal Scarab", 690, 0x1000, Sack), new(0x90000004, "Greater Mana Stone", 2436, 0x80000, Pack1) };
        var packs = new List<SidePack> { new(Pack1, "Pack", 24, 1), new(Pack2, "Pack", 24, 0), new(Sack, "Sack", 24, 1) };

        var layout = PackLayout.Assign(Me, 102, 10, packs, items, Rules, KeptAside, None);

        Assert.Equal(Sack, layout.ContainerOf(PackJob.Components)); // the scarabs are there
        Assert.Equal(Pack1, layout.ContainerOf(PackJob.Kit)); // the stone is there
        Assert.Equal(Pack2, layout.ContainerOf(PackJob.Review)); // the pack left
        Assert.StartsWith("components='Sack' 0x80001003 (holds 1 item(s) of the job)", layout.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_audit_sort_moves_components_kit_and_finds_out_of_the_main_pack_and_leaves_the_loot_the_coin_and_the_notes()
    {
        var items = Database();
        var layout = PackLayout.Assign(Me, 102, 88, EmptyPacks(), items, Rules, KeptAside, None);

        var moves = layout.Sort(items, Rules, KeptAside, None);

        Assert.Equal(
            [
                (0x90000001u, PackJob.Components, Pack1), (0x90000002u, PackJob.Components, Pack1),
                (0x90000003u, PackJob.Kit, Pack2), (0x90000004u, PackJob.Kit, Pack2),
                (0x90000008u, PackJob.Kit, Pack2), // the spare kit wand
                (0x90000009u, PackJob.Review, Sack), (0x9000000Au, PackJob.Review, Sack),
                (0x9000000Bu, PackJob.Kit, Pack2),
            ],
            moves.Select(m => (m.Guid, m.To, m.Container)));
        Assert.All(moves, m => Assert.Equal(PackJob.Main, m.From));
        Assert.DoesNotContain(moves, m => m.Name is "Pyreal" or "Trade Note (250,000)" or "Shoes"); // the coin, the note and the loot stay
        Assert.Equal("packs: move 'Pyreal Scarab' 0x90000001 main -> components pack 0x80001001 (a spell component)", moves[0].ToString());
        Assert.Equal("packs: move 'Compass' 0x90000009 main -> review pack 0x80001003 (worth a look)", moves[5].ToString());
    }

    [Fact]
    public void A_full_pack_leaves_the_item_where_it_is_named_and_the_slots_are_counted_per_job()
    {
        var items = Database();
        var packs = new List<SidePack> { new(Pack1, "Pack", 24, 23), new(Pack2, "Pack", 24, 0), new(Sack, "Sack", 24, 24) };
        var layout = PackLayout.Assign(Me, 102, 88, packs, items, Rules, KeptAside, None);
        var stays = new List<string>();

        var moves = layout.Sort(items, Rules, KeptAside, None, stays);

        Assert.Equal(1, moves.Count(m => m.To == PackJob.Components)); // one free slot: the scarabs go, the tapers stay
        Assert.DoesNotContain(moves, m => m.To == PackJob.Review);
        Assert.Contains("packs: 'Prismatic Taper' 0x90000002 belongs in the components pack (a spell component) but it is full: stays in the main pack", stays);
        Assert.Equal(3, stays.Count);
    }

    [Fact]
    public void Gear_in_the_kit_pack_and_finds_in_the_review_pack_stay_and_misplaced_side_pack_items_move()
    {
        var items = new List<PackItem>
        {
            new(0x91000001, "Fire Sceptre", 9_002, 0x8000, Pack2), // rotation gear from an earlier run: stays in the kit pack
            new(0x91000002, "Gold Ring", 9_003, 0x8, Sack), // the owner's find: stays
            new(0x91000003, "Pyreal Scarab", 690, 0x1000, Sack), // a component in the review pack: moves
            new(0x91000004, "Bread", 9_004, 0x20, Pack1), // junk in the components pack: to the main pack
            new(0x91000005, "Prismatic Taper", 20631, 0x1000, Pack1), // the components pack's own
        };
        var packs = new List<SidePack> { new(Pack1, "Pack", 24, 2), new(Pack2, "Pack", 24, 1), new(Sack, "Sack", 24, 2) };
        var layout = PackLayout.Assign(Me, 102, 10, packs, items, Rules, None, None);

        var moves = layout.Sort(items, Rules, None, None);

        Assert.Equal([(0x91000003u, PackJob.Review, PackJob.Components), (0x91000004u, PackJob.Components, PackJob.Main)], moves.Select(m => (m.Guid, m.From, m.To)));
        Assert.Equal(Me, moves[1].Container);
    }

    [Fact]
    public void The_rules_name_each_job()
    {
        Assert.Equal(PackJob.Main, Rules.JobOf(InMain(1, "Pyreal", 273, 0x40), false, false));
        Assert.Equal(PackJob.Main, Rules.JobOf(InMain(1, "Trade Note (10,000)", 2625, 0x40000), false, false));
        Assert.Equal(PackJob.Components, Rules.JobOf(InMain(1, "Powdered Quartz", 9_005, 0x1000), false, false)); // any spell component
        Assert.Equal(PackJob.Kit, Rules.JobOf(InMain(1, "Lesser Mana Stone", 2434, 0x80000), false, false));
        Assert.Equal(PackJob.Kit, Rules.JobOf(InMain(1, "Old Robe", 9_006, 0x4), true, rotation: true)); // taken off by the self-equip: rotation gear
        Assert.Equal(PackJob.Review, Rules.JobOf(InMain(1, "Compass", 9_001, 0x8), keptAside: true, false));
        Assert.Equal(PackJob.Main, Rules.JobOf(InMain(1, "Compass", 9_001, 0x8), false, false));
    }

    [Fact]
    public void Fewer_than_three_side_packs_leave_the_last_jobs_to_the_main_pack_named()
    {
        var layout = PackLayout.Assign(Me, 102, 10, [new(Pack1, "Pack", 24, 0)], [], Rules, None, None);

        Assert.Equal(Pack1, layout.ContainerOf(PackJob.Components));
        Assert.Equal(Me, layout.ContainerOf(PackJob.Review));
        Assert.Contains("review=the main pack (no side pack left: TODO(m5-pack-short))", layout.Why, StringComparison.Ordinal);
        Assert.Equal("packs free: main=92/102 components=24/24 kit=main review=main", layout.SlotsLine());
    }

    [Fact]
    public void The_loot_plan_routes_each_kept_item_to_its_jobs_pack_and_a_full_pack_to_the_main_pack()
    {
        var rules = new MageLootRules(new MageLootThresholds { MinValuePerBurden = 1 });
        var layout = PackLayout.Assign(Me, 102, 101, [new(Pack1, "Pack", 24, 0), new(Pack2, "Pack", 24, 0), new(Sack, "Sack", 24, 24)], [], Rules, None, None);
        MageLootItem[] items =
        [
            new(1, "Pyreal Scarab", 0x1000, 500, 4, Wcid: 690),
            new(2, "Gold Ring", 0x8, 900, 10, KeepAside: "Major Focus", Wcid: 9_003), // the review pack is full: the main pack's last slot
            new(3, "Greater Mana Stone", 0x80000, 800, 50, Wcid: 2436),
            new(4, "Shirt", 0x4, 700, 50, Wcid: 130), // no main slot left: left
        ];

        var plan = rules.Plan("Corpse of Tusker Guard", "BSD Mage", items, burden: 0, capacity: 10_000, freeSlots: 1, layout, Rules);

        Assert.Equal([2u, 1u, 3u], plan.Kept);
        Assert.Equal(PackJob.Components, plan.JobOf(1));
        Assert.Equal(PackJob.Kit, plan.JobOf(3));
        Assert.Equal(PackJob.Main, plan.JobOf(2));
        Assert.Equal(MageLootVerdict.Slots, plan.VerdictOf(4));
        Assert.Equal("; to the components pack 1, the kit pack 1", plan.RouteSummary());
    }

    [Fact]
    public void M5_7_D139_d_the_empty_stones_beyond_the_keep_are_shed_the_charged_first_and_a_shed_stone_is_main_pack_loot_the_sort_never_moves_back()
    {
        // Round 13 (03:06 UTC): 3 charged and 23 empty stones held against the keep of 4 filled the kit pack.
        var stones = new List<HeldStone> { new(0x91, "Greater Mana Stone", 6668, 2.0), new(0x92, "Greater Mana Stone", 4000, 2.0), new(0x93, "Greater Mana Stone", 900, 2.0) };
        for (uint i = 0; i < 23; i++)
        {
            stones.Add(new HeldStone(0xA0 + i, i < 3 ? "Moderate Mana Stone" : "Greater Mana Stone", null, i < 3 ? 1.2 : 2.0));
        }

        var shed = ItemManaKeeper.Shed(stones, g => g == 0xA5, out var why);
        Assert.Equal(22, shed.Count);
        Assert.DoesNotContain(shed, s => s.Charged);
        Assert.DoesNotContain(shed, s => s.Guid == 0xA5); // the kept empty one: a Greater (efficiency 2) already in the kit pack
        Assert.Equal("keep 3 charged and 1 empty of 23 (the keep is 4 stones, the charged first); 22 empty to sell", why);
        Assert.Empty(ItemManaKeeper.Shed(stones.Take(4).ToList(), _ => false, out why));
        Assert.Equal("keep 3 charged and 1 empty of 1 (the keep is 4 stones, the charged first); none to sell", why);

        var marked = new HashSet<uint> { 0xA0 };
        var rules = new PackRules(GroundCatalog.Get("black-spawn-den-aluvian")) { Shed = marked.Contains };
        var inKit = new PackItem(0xA0, "Moderate Mana Stone", 27330, 0x80000, Pack2);
        Assert.Equal(PackJob.Main, rules.JobOf(inKit, false, false));
        Assert.Equal("an empty mana stone beyond the keep, to sell", rules.WhyOf(inKit, false, false));
        Assert.Equal(PackJob.Kit, rules.JobOf(inKit with { Guid = 0xA1 }, false, false));
        var layout = PackLayout.Assign(Me, 102, 10, EmptyPacks(), [inKit with { Container = Me }], rules, None, None);
        Assert.Empty(layout.Sort([inKit with { Container = Me }], rules, None, None)); // stays in the main pack for the sale
    }
}

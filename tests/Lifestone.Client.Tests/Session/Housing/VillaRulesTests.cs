using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Housing;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Travel;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Housing;

/// <summary>
/// M6 villa: the villa steps' pure rules (when a step may run, what it needs, what is missing): the writ run's need, hand-in plan and next fetch; the lockpick buy; the purchase (the account's opening, one house an
/// account, the level, the price line by line); the storage plan (attuned, worn, unheld, a chest's 26 items and one pack); the rent (the margin, a late rent, the rent short); and the facts the steps walk to.
/// </summary>
public sealed class VillaRulesTests
{
    private static readonly DateTimeOffset Opens = VillaFacts.ClimberAccountOpensVillasRetail;

    private static readonly PriceHeld FullPrice = new(2_000_000, 0, 5, 1);

    [Fact]
    public void The_writs_wanted_need_two_stones_each_less_the_stones_held_and_a_half_pair()
    {
        Assert.Equal((5, 10), Pick(WritRule.Need(0, 0, 5)));
        Assert.Equal((2, 3), Pick(WritRule.Need(3, 1, 5)));
        Assert.Equal((2, 2), Pick(WritRule.Need(3, 1, 5, halfPending: true)));
        Assert.Equal((0, 0), Pick(WritRule.Need(7, 0, 5)));
        Assert.Equal((2, 0), Pick(WritRule.Need(0, 6, 2))); // more stones than the writs need: nothing to fetch
        static (int, int) Pick((int WritsShort, int StonesToFetch, string Why) n) => (n.WritsShort, n.StonesToFetch);
    }

    [Fact]
    public void The_hand_in_gives_whole_pairs_and_keeps_a_lone_stone()
    {
        var none = WritRule.PlanHandIn([], false);
        Assert.Equal(("no-stones", false, 0), (none.Decision.Token, none.Decision.May, none.Gives.Count));

        var lone = WritRule.PlanHandIn([(0x10u, 1)], false);
        Assert.Equal(("one-stone-kept", false), (lone.Decision.Token, lone.Decision.May));

        var completes = WritRule.PlanHandIn([(0x10u, 1)], halfPending: true);
        Assert.Equal((true, 1), (completes.Decision.May, completes.Writs));
        Assert.Equal([0x10u], completes.Gives);

        var three = WritRule.PlanHandIn([(0x10u, 1), (0x11u, 1), (0x12u, 1)], false);
        Assert.Equal([0x10u, 0x11u], three.Gives);
        Assert.Equal(1, three.Writs);
        Assert.Contains("keep 1", three.Decision.Why, StringComparison.Ordinal);

        var stack = WritRule.PlanHandIn([(0x20u, 4), (0x21u, 1)], true);
        Assert.Equal([0x20u, 0x20u, 0x20u, 0x20u, 0x21u], stack.Gives); // 5 held + the half = 3 writs: every stone goes, one a give
        Assert.Equal(3, stack.Writs);
    }

    [Fact]
    public void The_next_fetch_is_the_first_due_cave_whose_route_the_dats_established()
    {
        var now = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var timers = new QuestTimers("villa-test");
        var first = WritRule.NextFetch(WritFacts.Caves, timers, now, out var why);
        Assert.Equal("minor-smoldering-stone", first!.QuestKey); // the Shivering and Stinging runs carry their walk TODOs
        Assert.DoesNotContain("TODO(m6-fight-element-line)", why, StringComparison.Ordinal); // closed by the element line (m6/14-atlan): the Smoldering file carries no step TODO now
        Assert.Contains("TODO(villa-shivering-igloo-walk)", why, StringComparison.Ordinal);
        Assert.Contains("TODO(villa-blackmire-portal-ground)", why, StringComparison.Ordinal);

        timers.Complete(QuestCatalog.Get("minor-smoldering-stone"), now.AddMinutes(-10), QuestTimers.HowItem);
        Assert.Equal("minor-sparking-stone", WritRule.NextFetch(WritFacts.Caves, timers, now, out _)!.QuestKey);

        timers.Complete(QuestCatalog.Get("minor-sparking-stone"), now.AddMinutes(-5), QuestTimers.HowItem);
        Assert.Null(WritRule.NextFetch(WritFacts.Caves, timers, now, out var waiting));
        Assert.Contains("minor-smoldering-stone due 2026-11-20T12:50:00Z", waiting, StringComparison.Ordinal);
        Assert.Equal("minor-smoldering-stone", WritRule.NextFetch(WritFacts.Caves, timers, now.AddMinutes(51), out _)!.QuestKey); // the hour passed
    }

    [Fact]
    public void The_lockpick_is_bought_only_when_none_is_held_and_the_coin_covers_it()
    {
        Assert.Equal(233, LockpickFacts.CatalogPrice); // ceil(1.55 x 150 - 0.1)
        Assert.Equal(("held", false), Pick(LockpickRule.Decide(1, 10_000, 233)));
        Assert.Equal(("purse-unread", false), Pick(LockpickRule.Decide(0, -1, 233)));
        var shortCoin = LockpickRule.Decide(0, 200, 233);
        Assert.Equal(("purse-short", false), Pick(shortCoin));
        Assert.Equal(["pyreals 33 short"], shortCoin.Missing);
        Assert.Equal(("due", true), Pick(LockpickRule.Decide(0, 233, 233)));
    }

    [Fact]
    public void M6_16_E5a2_the_lockpick_is_due_on_a_short_coin_when_the_notes_the_counter_can_change_cover_it()
    {
        // 100 coin and a 10,000 note: before the slice the rule read the coin alone and the step never started, though the Merchant's counter would have changed the note (VendorCounter).
        var onNote = LockpickRule.Decide(0, 100, 233, notes: 10_000);
        Assert.Equal(("due", true), Pick(onNote));
        Assert.Equal("no Crude Lockpick held; one at 233: the coin 100 is short by 133 and the trade notes held (10000) cover it, so a note is changed at the Merchant's counter", onNote.Why);

        // The judge says what it did with the notes when the caller gives its words.
        var said = LockpickRule.Decide(0, 100, 233, notes: 10_000, notesSay: "the notes are counted");
        Assert.EndsWith("so a note is changed at the Merchant's counter; the notes are counted", said.Why, StringComparison.Ordinal);

        // Notes that do not cover the rest: still short, by the rest.
        var stillShort = LockpickRule.Decide(0, 100, 233, notes: 100);
        Assert.Equal(("purse-short", false), Pick(stillShort));
        Assert.Equal("the coin 100 does not cover one Crude Lockpick at 233 (the trade notes held, 100, do not cover the rest)", stillShort.Why);
        Assert.Equal(["pyreals 33 short"], stillShort.Missing);

        // A vendor known to take no note: the caller counts none and says so; the coin alone judges.
        var coinOnly = LockpickRule.Decide(0, 100, 233, notes: 0, notesSay: "the Merchant's mask takes no trade note");
        Assert.Equal(("purse-short", false), Pick(coinOnly));
        Assert.Equal("the coin 100 does not cover one Crude Lockpick at 233; the Merchant's mask takes no trade note", coinOnly.Why);
        Assert.Equal(["pyreals 133 short"], coinOnly.Missing);

        // Unread stays unread whatever the notes; a coin that covers is as before, and so is a call with no notes.
        Assert.Equal(("purse-unread", false), Pick(LockpickRule.Decide(0, -1, 233, notes: 10_000)));
        Assert.Equal("no Crude Lockpick held; one at 233 within the coin 233", LockpickRule.Decide(0, 233, 233, notes: 10_000).Why);
        Assert.Equal("the coin 200 does not cover one Crude Lockpick at 233", LockpickRule.Decide(0, 200, 233).Why);
        Assert.Equal(("held", false), Pick(LockpickRule.Decide(1, 0, 233, notes: 10_000)));
    }

    [Fact]
    public void The_climbers_purchase_waits_for_no_account_age_by_default()
    {
        var day1 = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero); // the day after the climber's account was made
        Assert.Equal(("may", true), Pick(VillaPurchaseRule.Decide(day1, VillaFacts.ClimberAccountOpensVillas, VillaFacts.Block1401, HouseOwnership.None, 78, FullPrice)));
    }

    [Fact]
    public void Under_the_retail_flag_the_purchase_waits_for_the_accounts_fifteen_days_except_for_an_apartment()
    {
        var early = VillaPurchaseRule.Decide(Opens.AddSeconds(-1), Opens, VillaFacts.Block1401, HouseOwnership.None, 78, FullPrice);
        Assert.Equal(("too-early", false), Pick(early));
        Assert.Contains("2026-11-24 22:51:38Z", early.Why, StringComparison.Ordinal);
        Assert.Equal(("may", true), Pick(VillaPurchaseRule.Decide(Opens, Opens, VillaFacts.Block1401, HouseOwnership.None, 78, FullPrice)));

        var apartments = VillaFacts.Block1401 with { Key = "test-apartments", Type = HouseType.Apartment, Price = new HousePrice(10_000, 0, 0), MinLevel = 1 };
        Assert.Equal(("may", true), Pick(VillaPurchaseRule.Decide(Opens.AddDays(-10), Opens, apartments, HouseOwnership.None, 5, new PriceHeld(10_000, 0, 0, 0))));
    }

    [Fact]
    public void The_purchase_is_refused_unread_owned_low_or_short_with_every_short_line_named()
    {
        var at = Opens.AddDays(1);
        Assert.Equal("status-unread", VillaPurchaseRule.Decide(at, Opens, VillaFacts.Block1401, HouseOwnership.Unknown, 78, FullPrice).Token);
        Assert.Equal("owns-house", VillaPurchaseRule.Decide(at, Opens, VillaFacts.Block1401, HouseOwnership.Owned, 78, FullPrice).Token);
        Assert.Equal("below-level", VillaPurchaseRule.Decide(at, Opens, VillaFacts.Block1401, HouseOwnership.None, 34, FullPrice).Token);

        var shortAll = VillaPurchaseRule.Decide(at, Opens, VillaFacts.Block1401, HouseOwnership.None, 78, new PriceHeld(1_000_000, 500_000, 3, 0));
        Assert.Equal(("price-short", false), Pick(shortAll));
        Assert.Equal(3, shortAll.Missing.Count);
        Assert.StartsWith("pyreals 500,000 short", shortAll.Missing[0], StringComparison.Ordinal);
        Assert.StartsWith("Writ of Refuge 2 short", shortAll.Missing[1], StringComparison.Ordinal);
        Assert.StartsWith("Crude Lockpick 1 short", shortAll.Missing[2], StringComparison.Ordinal);

        var notes = new PriceHeld(250_000, 1_750_000, 5, 1); // trade notes pay the money line (ACE HousePayment.GetConsumeItems)
        Assert.Equal("may", VillaPurchaseRule.Decide(at, Opens, VillaFacts.Block1401, HouseOwnership.None, 78, notes).Token);
    }

    [Fact]
    public void The_storage_plan_keeps_out_attuned_worn_unheld_and_unread_items_and_fills_26_items_and_one_pack()
    {
        KeepItem Item(uint g, bool? attuned = false, bool pack = false, bool held = true, bool worn = false) => new(g, "item " + g, pack, attuned, held, worn);
        var asked = new[] { Item(1), Item(2, attuned: true), Item(3, attuned: null), Item(4, held: false), Item(5, worn: true), Item(6, pack: true), Item(7, pack: true), Item(8) };
        var (moves, refused) = StorageRule.Plan(asked, 0, 0);
        Assert.Equal([1u, 6u, 8u], moves.Select(m => m.Guid));
        Assert.Equal([(2u, "attuned"), (3u, "attuned-unread"), (4u, "not-held"), (5u, "worn"), (7u, "pack-slot-full")], refused.Select(r => (r.Item.Guid, r.Token)));

        var nearlyFull = StorageRule.Plan([Item(1), Item(2), Item(3)], VillaFacts.ChestItems - 1, VillaFacts.ChestPacks);
        Assert.Equal([1u], nearlyFull.Moves.Select(m => m.Guid));
        Assert.Equal(["chest-full", "chest-full"], nearlyFull.Refused.Select(r => r.Token));
    }

    [Fact]
    public void The_rent_is_due_inside_the_margin_paid_at_once_when_late_and_named_when_short()
    {
        const long Due = 1_800_000_000;
        var margin = TimeSpan.FromDays(3);
        (uint, string, long)[] owed = [(VillaFacts.PyrealWcid, "Pyreals", 100_000), (VillaFacts.WritWcid, "Writs of Refuge", 2)];
        var held = new PriceHeld(150_000, 0, 2, 0);
        Assert.Equal("status-unread", RentRule.Decide(HouseOwnership.Unknown, false, Due, Due - 10, margin, owed, held).Token);
        Assert.Equal("no-house", RentRule.Decide(HouseOwnership.None, false, Due, Due - 10, margin, owed, held).Token);
        Assert.Equal("paid", RentRule.Decide(HouseOwnership.Owned, true, Due, Due - 10, margin, [], held).Token);
        Assert.Equal(("not-yet", false), Pick(RentRule.Decide(HouseOwnership.Owned, false, Due, Due - (4 * 86400), margin, owed, held)));
        Assert.Equal(("due", true), Pick(RentRule.Decide(HouseOwnership.Owned, false, Due, Due - (2 * 86400), margin, owed, held)));
        Assert.Equal(("late", true), Pick(RentRule.Decide(HouseOwnership.Owned, false, Due, Due + 60, margin, owed, held)));

        var shortWrits = RentRule.Decide(HouseOwnership.Owned, false, Due, Due - 100, margin, owed, held with { Writs = 1 });
        Assert.Equal(("rent-short", false), Pick(shortWrits));
        Assert.Equal(["Writs of Refuge 1 short (1 of 2)"], shortWrits.Missing);
    }

    [Fact]
    public void The_facts_name_the_villa_block_the_stones_and_their_routes()
    {
        var block = VillaFacts.Block1401;
        Assert.Equal((HouseType.Villa, new HousePrice(2_000_000, 5, 1), 35), (block.Type, block.Price, block.MinLevel));
        Assert.All(block.Houses, h => Assert.Equal((VillaFacts.VillaGuardianWcid, 0x9DAFu), (h.Guardian.Wcid, h.Guardian.Cell >> 16)));
        Assert.All(block.Houses, h => Assert.Equal(2, h.Chests.Count));
        Assert.Same(block.Houses[0], VillaFacts.HouseAt(0x9DAF));
        Assert.Same(block.Houses[1], VillaFacts.HouseOf(block, 0x79DAF03A));

        Assert.Equal([6318u, 6319u, 6320u, 6321u], WritFacts.StoneWcids.Order());
        foreach (var cave in WritFacts.Caves)
        {
            var quest = QuestCatalog.Get(cave.QuestKey);
            Assert.Equal((cave.StoneWcid, 3600), (quest.Done.Item!.Wcid!.Value, quest.TimerSeconds!.Value));
            var drop = TravelRoutes.Default.Edge(cave.DropEdge);
            var place = TravelRoutes.Default.Places.Single(p => p.Key == drop.To);
            Assert.Contains((ushort)(cave.StoneCell >> 16), place.Landblocks); // the run from the drop ends in the drop's own place
            if (cave.RouteTodo is { } todo)
            {
                Assert.Contains(todo.Split(':')[0], quest.Steps[0].Todo, StringComparison.Ordinal); // the quest file's go step names the same walk
            }
        }
    }

    private static (string, bool) Pick(VillaDecision d) => (d.Token, d.May);
}

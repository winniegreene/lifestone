using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 16 E5c2 (docs/automation-engine.md E5, <c>TODO(engine-sale-copies)</c>): what the rule book's verdict reads outside a trip, proved against the trip itself. The two parts only a trip could say (the every-spell rule's scroll
/// keeps, the drinks its pack budget sells) are asked of the session's context (<see cref="SaleContext.For"/>) before a trip runs, and the trip that follows does what the context said.
/// </summary>
public sealed partial class ResupplyStationTests
{
    private static readonly Dictionary<uint, SpellInfo> NoSpellTable = [];

    /// <summary>The climber's visit of the pack room test (her live shape of 2026-11-14): the drinks she did not drink, the loot, the last visit's departure and the loot since.</summary>
    private static Home LiveShapeBeforeTheVisit(out uint elixirs, out uint oneTincture, out uint tinctures, out uint manaPotions, out uint loot)
    {
        var description = new PlayerDescriptionMsg
        {
            IntProperties = ImmutableDictionary<uint, int>.Empty.Add(PlayerQualities.LevelProperty, 66),
            Attributes = [new AttributeDesc(1, 0, 119, 0)],
        };
        var home = new Home(Barracks, 2_000_000, held: l => l.Wcid switch { GearKit.PrismaticTaperWcid => 333, FighterKit.PotionWcid or FighterKit.ManaPotionWcid => 0, _ => l.Keep }, description: description);
        home.Own("Potion of Healing", FighterKit.PotionWcid, MiscType, 33 * 170, stack: 33, maxStack: 100);
        manaPotions = home.Own("Mana Potion", FighterKit.ManaPotionWcid, MiscType, 100 * 170, stack: 100, maxStack: 100);
        oneTincture = home.Own("Health Tincture", HealthDrinks.TinctureWcid, MiscType, 500, stack: 1, maxStack: 100);
        tinctures = home.Own("Health Tincture", HealthDrinks.TinctureWcid, MiscType, 20 * 500, stack: 20, maxStack: 100);
        home.Own("Mana Tincture", ManaDrinks.TinctureWcid, MiscType, 29 * 500, stack: 29, maxStack: 100);
        elixirs = home.Own("Health Elixir", HealthElixirWcid, MiscType, 9 * 300, stack: 9, maxStack: 100);
        home.Own("Gold Scarab", 687, MiscType, 100, stack: 100, maxStack: 100);
        home.Own("Pyreal Scarab", 690, MiscType, 95, stack: 95, maxStack: 100);
        loot = home.Own("Chainmail Sleeves", 99_002, MiscType, 2_000);
        home.Vendor.R.PrivateInt(PlayerInventory.EncumbranceProperty, 17_776);
        var room = PackRoomMemory.For(home.Vendor.State);
        var departed = home.Ground.Supplies.Stock.ToDictionary(l => l.Wcid, l => l.Keep);
        departed[GearKit.PrismaticTaperWcid] = 850;
        departed[FighterKit.PotionWcid] = 33;
        departed[HealthDrinks.TinctureWcid] = 21;
        departed[ManaDrinks.TinctureWcid] = 29;
        departed[687] = 100;
        departed[690] = 95;
        room.Visited(-57 * 60, departed, new Dictionary<uint, int>());
        room.NoteLoot(leftItems: 338, leftValue: 2_112_938, leftBurden: 132_036, keptBurden: 3_735);
        return home;
    }

    private static WorldObject Held(GameState state, uint guid) => state.Objects.TryGet(guid, out var o) ? o : throw new InvalidOperationException($"0x{guid:X8} is not in the object table");

    [Fact]
    public void M6_16_E5c2_outside_a_trip_the_rule_book_reads_the_drink_stacks_the_trips_budget_sells_whole_and_the_trip_then_sells_them()
    {
        var home = LiveShapeBeforeTheVisit(out var elixirs, out var oneTincture, out var tinctures, out var manaPotions, out var loot);
        var state = home.Vendor.State;
        var room = PackRoomMemory.For(state);

        var book = SaleContext.For(state, home.Ground, "BSD Mage", NoSpellTable, home.Memory);
        SaleAnswer Of(uint guid) => SaleVerdict.Of(guid, Held(state, guid), false, book);

        // the budget the trip plans (AtNeeds: every drink at its need) sells 9 elixirs, 14 Health Tinctures and 89 Mana Potions: the whole stacks that fit are sale loot now; the rest is a split only a trip makes
        Assert.Equal(("drinks-surplus", PackAction.Sell), (Of(elixirs).Rule, Of(elixirs).Action));
        Assert.Equal(("drinks-surplus", PackAction.Sell), (Of(oneTincture).Rule, Of(oneTincture).Action));
        Assert.Equal(("drinks-in-use", PackAction.Keep), (Of(tinctures).Rule, Of(tinctures).Action)); // 13 more of its 20 are over: no stack of 13 exists until the trip splits it
        Assert.Equal(("drinks-in-use", PackAction.Keep), (Of(manaPotions).Rule, Of(manaPotions).Action)); // 89 of its 100
        Assert.Equal(PackAction.Sell, Of(loot).Action);
        Assert.True(SaleVerdict.WillSell(elixirs, Held(state, elixirs), false, book));

        // a read: nothing of the session's memory moved
        Assert.Equal((1, 338, null), (room.Visits, room.LoadLeftItems, room.DrinksDitched));
        Assert.Empty(home.Memory.KeptAside);

        // and the trip does what the context said: the two whole stacks are gone, the split stacks stay
        var run = home.Start(new ResupplyOptions(true, () => [], Memory: home.Memory));
        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run, 1_200));
        Assert.False(state.Self.Inventory.Contains(elixirs));
        Assert.False(state.Self.Inventory.Contains(oneTincture));
        Assert.True(state.Self.Inventory.Contains(tinctures));
        Assert.True(state.Self.Inventory.Contains(manaPotions));
        Assert.Equal(89 + 26 + 14 + 18 + 9, run.DrinksOverBudget);
    }

    [Fact]
    public void M6_16_E5c2_outside_a_trip_no_drink_is_surplus_before_the_sessions_first_visit_or_when_her_strength_is_not_read()
    {
        var unmeasured = new Home(Barracks, 2_000_000, description: new PlayerDescriptionMsg { Attributes = [new AttributeDesc(1, 0, 119, 0)] });
        var stack = unmeasured.Own("Health Elixir", HealthElixirWcid, MiscType, 9 * 300, stack: 9, maxStack: 100);
        unmeasured.Vendor.R.PrivateInt(PlayerInventory.EncumbranceProperty, 17_776);
        var state = unmeasured.Vendor.State;
        Assert.Equal("drinks-in-use", SaleVerdict.Of(stack, Held(state, stack), false, SaleContext.For(state, unmeasured.Ground, "BSD Mage", NoSpellTable, unmeasured.Memory)).Rule);

        var unread = new Home(Barracks, 2_000_000); // no Strength in the description: no budget on a guessed capacity
        var other = unread.Own("Health Elixir", HealthElixirWcid, MiscType, 9 * 300, stack: 9, maxStack: 100);
        PackRoomMemory.For(unread.Vendor.State).Visited(0, new Dictionary<uint, int>(), new Dictionary<uint, int>());
        PackRoomMemory.For(unread.Vendor.State).NoteLoot(leftItems: 338, leftValue: 2_112_938, leftBurden: 132_036, keptBurden: 3_735);
        Assert.Equal("drinks-in-use", SaleVerdict.Of(other, Held(unread.Vendor.State, other), false, SaleContext.For(unread.Vendor.State, unread.Ground, "BSD Mage", NoSpellTable, unread.Memory)).Rule);
    }

    [Fact]
    public void M6_16_E5c2_outside_a_trip_the_rule_book_keeps_a_scroll_the_every_spell_rule_keeps_in_the_trips_own_words_while_the_jobs_switch_is_on()
    {
        var home = OutskirtsHome(warRanks: 56);
        var spells = TwoWarLines();
        var acid = ScrivenerStock.Default.Spell(AcidStreamII)!;
        var learned = new List<uint>();
        var refused = new List<uint>();
        JourneyFacts? facts = null;
        var run = home.Start(new ResupplyOptions(Memory: home.Memory), spells, facts: f => (facts = f).EverySpell = true);
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 900, pump: EverySpellServer(home, learned, refused)));
        var state = home.Vendor.State;
        var scroll = state.Objects.All.Single(o => o.Weenie.WeenieClassId == acid.ScrollWcid); // ACE refused its read: the trip keeps it from the sale

        // the trip ran with no job, so it stated its own context to the session's sale desk: who she is, the SpellTable and its facts (the switch on)
        Assert.True(SaleDesk.For(state).JobStated);
        Assert.Same(facts, SaleDesk.For(state).Facts);
        var kept = SaleVerdict.Of(scroll.Guid, scroll, false, SaleContext.For(state, home.Ground, "BSD Mage", spells, home.Memory));
        Assert.Equal(("scrolls-kept", PackAction.Keep, run.ScrollKeepReason(scroll)), (kept.Rule, kept.Action, kept.Detail));
        Assert.Contains("the every-spell rule: ACE refused its read this session", kept.Detail, StringComparison.Ordinal);

        // a scroll the rule bought and has not read yet is kept by the same place
        var frost = ScrivenerStock.Default.LineSpells("Frost Bolt")[2];
        var bought = home.Own("Scroll of " + frost.Name, frost.ScrollWcid, ResupplyRun.WritableType, 1_000);
        EverySpellMemory.For(home.Memory).Bought.Add(frost.ScrollWcid);
        var held = SaleVerdict.Of(bought, Held(state, bought), false, SaleContext.For(state, home.Ground, "BSD Mage", spells, home.Memory));
        Assert.Equal(("scrolls-kept", "the every-spell rule bought it this session: read on the trip"), (held.Rule, held.Detail));

        facts!.EverySpell = false; // the switch is read at each ask: off, the rule keeps nothing outside a trip, as on a trip, and the scroll is the table's to sell
        var off = SaleVerdict.Of(scroll.Guid, scroll, false, SaleContext.For(state, home.Ground, "BSD Mage", spells, home.Memory));
        Assert.Equal(("scrolls", PackAction.Sell), (off.Rule, off.Action));
    }

    [Fact]
    public void M6_16_E5c2_the_junk_drop_drops_what_the_rule_book_drops_and_no_longer_a_valueless_stone_the_kit_keeps()
    {
        // An Oil of Rendering (no value, no rule needs it) and a mana stone of no value she has not appraised (the kit keeps a stone it has not read). Before this slice the junk pass asked the table alone, whose
        // no-value row stands before its mana stone row, and dropped both; the verdict's own row for the kit's stones stands first.
        var home = new Home(Den, 1_000);
        var oil = home.Own("Oil of Rendering", 12711, MiscType, 0);
        var stone = home.Own("Greater Mana Stone", 2436, 0x80000, 0);
        home.Own("Black Garnet", 2394, 0x800, 373); // something to sell: a trip with no walk ends before its junk pass
        var state = home.Vendor.State;
        Assert.Equal("value-zero-drop", PackRuleBook.Classify(new PackFacts(2436, "Greater Mana Stone", 0x80000, 0)).Verdict.Rule); // the table alone, as the pass asked it before
        var run = home.Start(new ResupplyOptions(true));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run, 4000));

        Assert.False(state.Self.Inventory.Contains(oil));
        Assert.True(state.Self.Inventory.Contains(stone));
        Assert.Equal(1, run.JunkDropped);
        Assert.True(home.Logged("pack rule value-zero-drop: drop 'Oil of Rendering'"));
        Assert.False(home.Logged("drop 'Greater Mana Stone'"));
        Assert.Equal(1, home.Lines.Count(l => l.Contains("pack rule mana-stones (ManaStones): keep 'Greater Mana Stone'", StringComparison.Ordinal))); // named once a trip by the row that decided
        Assert.Contains("junk_dropped=1", run.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain("sale_shadow", run.VerdictFields(), StringComparison.Ordinal); // the shadow of this copy went with its own rule
    }

    [Fact]
    public void M6_16_E5c2_the_drain_never_takes_a_piece_the_rule_book_keeps_though_none_of_the_old_filters_named_it()
    {
        // The due Robe, an empty stone, and two pieces of loot with mana: a Ring worth 2,343 and a sash of no value (a quest set's piece: the rule book keeps it, value-zero-gear, for the return to its NPC). Before
        // this slice the never-list read the sale's old filters, none of which names a piece of no value, and the sash was the cheapest capture there is (no payout forgone): the stone drained and destroyed it.
        var f = new RefillHome(robeMana: 97);
        var stone = f.Stone("Mana Stone", 2435, 0.6, 0.1);
        var ring = f.Loot("Ring", JewelryType, 1084, 2343);
        var sash = f.Loot("Pathwarden Sash", ClothingType, 900, 0);
        var run = f.Start();

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        Assert.Equal([(stone, ring, false), (stone, f.Robe, true)], f.Uses);
        Assert.True(f.Inventory.Contains(sash));
        Assert.True(f.Home.Logged("gear mana: refill: never drained: 'Pathwarden Sash' 0x" + Hex(sash) + " (900 mana): the pack rules keep it (value-zero-gear)"));
        Assert.Null(run.Failure);
    }
}

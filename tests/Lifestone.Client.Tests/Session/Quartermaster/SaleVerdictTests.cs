using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Quartermaster;

/// <summary>
/// M6 16 E5c: the one keep, sell or drop verdict (<see cref="SaleVerdict.Of"/>) over a hand-made context: every row of the table decides an item made for it, the rows stand in the trip's order (the sale's rows, the table, the
/// guards), and asking writes nothing. Whole trips over a mixed pack are in <c>ResupplyStationTests.SaleVerdict.cs</c>.
/// </summary>
public sealed class SaleVerdictTests
{
    private const uint Melee = 0x1, Armor = 0x2, Clothing = 0x4, Jewelry = 0x8, Money = 0x40, Misc = 0x80, Container = 0x200, Gem = 0x800, Writable = 0x2000, Caster = 0x8000, Note = 0x40000, Stone = 0x80000;
    private const uint MajorFocus = 2574;
    private const uint SilverPea = 8331, LeadScarab = 691, CopperScarab = 686, PinkTaper = 1649, LockpickLine = 515;

    /// <summary>The den's ground (its kit lists a robe, gauntlets, a wand and a pack; its purse three notes) with one stock line that is neither a component nor a drink, which no profile has today.</summary>
    private static readonly HuntingGround Den = WithStock(GroundCatalog.Get("black-spawn-den-aluvian"));

    private static HuntingGround WithStock(HuntingGround ground) =>
        ground with { Supplies = ground.Supplies with { Stock = [.. ground.Supplies.Stock, new StockLine(LockpickLine, "Superb Lockpick", 5, 2, 0)] } };

    /// <summary>What a test says of the trip; <see cref="Book"/> is the verdict's context over it.</summary>
    internal sealed class Trip
    {
        public Dictionary<uint, Appraisal> Appraisals { get; } = [];

        public Dictionary<uint, string> CasterNeeds { get; } = [];

        public HashSet<uint> StonesKept { get; } = [];

        public HashSet<uint> FloorCasters { get; } = [];

        public HashSet<uint> Surplus { get; } = [];

        public HashSet<uint> Leather { get; } = [];

        public Dictionary<uint, string> Scrolls { get; } = [];

        public SaleMemory Memory { get; } = new();

        public PackMemory Packs { get; } = new();

        /// <summary>Every focus held and a book that burns the Lead Scarab: a pea, a Copper Scarab and a Pink Taper are sold.</summary>
        public bool AllFoci { get; set; } = true;

        public SaleContext Book => new()
        {
            Ground = Den,
            Appraisal = g => Appraisals.GetValueOrDefault(g),
            KeepAside = new KeepAsideRule(KeepAsideRule.ClimberDefault),
            SpellName = id => id == MajorFocus ? "Major Focus" : null,
            Cantrips = UsefulCantripTests.Qwinnie,
            CasterNeed = g => CasterNeeds.GetValueOrDefault(g),
            StoneKept = StonesKept.Contains,
            FloorCaster = FloorCasters.Contains,
            QuestItems = ResupplyRun.QuestItemWcids,
            ScrollKeep = o => Scrolls.GetValueOrDefault(o.Guid),
            Memory = Memory,
            Packs = Packs,
            LeatherKeeps = Leather.Contains,
            DrinkSurplus = Surplus.Contains,
            Component = (_, o) => FociComponents.Judge(o.Weenie.WeenieClassId, o.ItemType, AllFoci, new Dictionary<uint, int> { [LeadScarab] = 1 }, Den.Supplies.StockOf(o.Weenie.WeenieClassId) is not null),
        };
    }

    internal static WorldObject Item(uint guid, uint wcid, string name, uint type, uint value, bool worn = false, uint flags = 0)
    {
        var o = LootRulesTests.Item(type, value, 10, guid, name, wcid, descriptionFlags: flags);
        return worn ? new WorldObject(o.Created with { Weenie = o.Created.Weenie with { Header = o.Created.Weenie.Header | WeenieHeaderFlags.Wielder, WielderId = 0x50000001 } }, isSelf: false, createdAt: 0) : o;
    }

    internal static Appraisal Appraised(uint guid, int attuned = 0, bool retained = false, params uint[] spells) => new(
        new IdentifyResponseMsg(
            guid,
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.BoolStatsTable | IdentifyResponseFlags.SpellBook,
            true,
            attuned > 0 ? ImmutableDictionary<uint, int>.Empty.Add(114, attuned) : ImmutableDictionary<uint, int>.Empty,
            ImmutableDictionary<uint, long>.Empty,
            retained ? ImmutableDictionary<uint, bool>.Empty.Add(91, true) : ImmutableDictionary<uint, bool>.Empty,
            ImmutableDictionary<uint, double>.Empty,
            ImmutableDictionary<uint, string>.Empty,
            ImmutableDictionary<uint, uint>.Empty,
            [.. spells],
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null),
        1,
        0);

    /// <summary>One item made for one row: how the trip stands, the item, whether it lies in a side pack, and the answer.</summary>
    private sealed record Case(string Rule, PackAction Action, SaleStep Step, Func<Trip, WorldObject> Make, bool InSidePack = false, string? Detail = null);

    private static readonly uint QuestWcid = ResupplyRun.QuestItemWcids.First();

    private static readonly Case[] Cases =
    [
        // part 1: the sale's rows, in their order
        new("side-pack-held", PackAction.Keep, SaleStep.SidePack, _ => Item(1, 9001, "Ruby", Gem, 500), InSidePack: true),
        new("gear-caster-kept", PackAction.Keep, SaleStep.KeptAside, t => { t.CasterNeeds[2] = "the floor's spare"; return Item(2, 2472, "Wand", Caster, 200); }, Detail: "her gear rules need it: the floor's spare"),
        new("kept-aside-match", PackAction.Keep, SaleStep.KeptAside, t => { t.Appraisals[3] = Appraised(3, spells: MajorFocus); return Item(3, 9003, "Ring of Focus", Jewelry, 700); }),
        new("quest-item", PackAction.Keep, SaleStep.KeptAside, _ => Item(4, QuestWcid, "Quest Thing", Misc, 100), Detail: "quest item: a hunt-until, expected or done item of a quest file"),
        new("scrolls-kept", PackAction.Keep, SaleStep.KeptAside, t => { t.Scrolls[5] = "her book can use it"; return Item(5, 9005, "Scroll of Infernae", Writable, 2000); }, Detail: "her book can use it"),
        new("gear-review", PackAction.Keep, SaleStep.KeptAside, t => { t.Packs.WearReview.Add(6); return Item(6, 9006, "Covenant Sollerets", Armor, 9000); }, Detail: "the wear pick keeps it for the next visit's review (pack rule gear-review)"),
        new("kept-aside-noted", PackAction.Keep, SaleStep.KeptAside, t => { t.Memory.KeptAside.Add(7); return Item(7, 9007, "Odd Find", Misc, 300); }, Detail: "noted by the loot step"),
        new("mana-stones", PackAction.Keep, SaleStep.StoneKept, t => { t.StonesKept.Add(8); return Item(8, 2436, "Greater Mana Stone", Stone, 5000); }),
        new("gear-rotation", PackAction.Keep, SaleStep.Rotation, t => { t.Packs.Rotation.Add(9); return Item(9, 9009, "Old Helm", Armor, 800); }),
        new("gear-leather-set", PackAction.Keep, SaleStep.Leather, t => { t.Leather.Add(115); return Item(10, 115, "Leather Boots", Clothing, 1100); }),
        new("drinks-surplus", PackAction.Sell, SaleStep.DrinkSurplus, t => { t.Surplus.Add(11); return Item(11, 27319, "Health Tincture", Misc, 6500); }),
        new("drinks-in-use", PackAction.Keep, SaleStep.DrinkKept, _ => Item(12, 27319, "Health Tincture", Misc, 6500)),
        new(FociComponents.RuleBurned, PackAction.Keep, SaleStep.ComponentKept, _ => Item(13, LeadScarab, "Lead Scarab", FociComponents.ComponentType, 10)),
        new(FociComponents.RuleStocked, PackAction.Keep, SaleStep.ComponentKept, _ => Item(14, CopperScarab, "Copper Scarab", FociComponents.ComponentType, 10)), // the den's stock lists it; her book does not burn it here
        new(FociComponents.RuleFociMissing, PackAction.Keep, SaleStep.ComponentKept, t => { t.AllFoci = false; return Item(15, SilverPea, "Silver Pea", FociComponents.ComponentType, 25000); }),
        new(FociComponents.RuleUnknown, PackAction.Keep, SaleStep.ComponentKept, _ => Item(16, 99_016, "Strange Component", FociComponents.ComponentType, 10)),
        new("dropped-before", PackAction.Keep, SaleStep.Dropped, t => { t.Memory.Dropped.Add(17); return Item(17, 9017, "Refused Twice", Misc, 40); }),
        new("supplies-stock", PackAction.Keep, SaleStep.Supplies, _ => Item(18, LockpickLine, "Superb Lockpick", 0x4000, 1200)),
        new("supplies-kit", PackAction.Keep, SaleStep.Supplies, _ => Item(19, 5851, "Faran Robe with Hood", Clothing, 900)),
        new("money-notes", PackAction.Keep, SaleStep.Supplies, _ => Item(20, 2625, "Trade Note (10,000)", Note, 10_000)),
        new("foci-held", PackAction.Keep, SaleStep.Supplies, _ => Item(21, 15271, "Foci of Strife", Misc, 500)),
        new("money-coin", PackAction.Keep, SaleStep.Supplies, _ => Item(22, 273, "Pyreal", Money, 25_000)),
        new("containers-in-use", PackAction.Keep, SaleStep.Supplies, _ => Item(23, 9023, "Sack", Container, 65)),

        // part 2: the table's rows
        new("gear-worn", PackAction.Keep, SaleStep.Unsellable, _ => Item(24, 9024, "Worn Shirt", Clothing, 100, worn: true), Detail: "worn"),
        new("travel-gem", PackAction.Keep, SaleStep.Table, _ => Item(25, 99_025, "Facility Hub Portal Gem", Gem, 10)),
        new(FociComponents.RulePea, PackAction.Sell, SaleStep.Table, _ => Item(26, SilverPea, "Silver Pea", FociComponents.ComponentType, 25000)),
        new(FociComponents.RuleScarabUnused, PackAction.Sell, SaleStep.Table, _ => Item(27, 689, "Iron Scarab", FociComponents.ComponentType, 10)),
        new(FociComponents.RuleAccountOnly, PackAction.Sell, SaleStep.Table, _ => Item(28, PinkTaper, "Pink Taper", FociComponents.ComponentType, 10)),
        new("self-buff-gem", PackAction.Keep, SaleStep.Unsellable, _ => Item(29, 31000, "Blackmoor's Favor", Gem, 0), Detail: "value 0"),
        new("value-zero-attuned", PackAction.Keep, SaleStep.Unsellable, t => { t.Appraisals[30] = Appraised(30, attuned: 1); return Item(30, 9030, "Bound Token", Misc, 0); }, Detail: "value 0"),
        new("value-zero-gear", PackAction.Keep, SaleStep.Unsellable, _ => Item(31, 9031, "Pathwarden Sash", Clothing, 0), Detail: "value 0"),
        new("value-zero-drop", PackAction.Drop, SaleStep.Unsellable, _ => Item(32, 12711, "Oil of Rendering", Misc, 0), Detail: "value 0"),
        new("healing-kits", PackAction.Sell, SaleStep.Table, _ => Item(33, 630, "Gifted Healing Kit", Misc, 120)),
        new("healing-kits", PackAction.Sell, SaleStep.Table, _ => Item(34, 629, "Adept Healing Kit", Misc, 50), InSidePack: true), // one of the three kinds the sale reads in a side pack
        new("essences", PackAction.Sell, SaleStep.Table, _ => Item(35, 49422, "Acid Spectre Essence (80)", Misc, 5000)),
        new("mana-stones-shed", PackAction.Sell, SaleStep.Table, _ => Item(36, 2436, "Greater Mana Stone", Stone, 5000)), // the keep does not hold it
        new("tinkering-tool", PackAction.Drop, SaleStep.Table, _ => Item(37, 20646, "Ust", 0x20000000, 10)),
        new("tinkering-salvage", PackAction.Sell, SaleStep.Table, _ => Item(38, 9038, "Salvaged Iron", 0x40000000, 30)),
        new("scrolls", PackAction.Sell, SaleStep.Table, _ => Item(39, 9039, "Scroll of Dual Wield Mastery Self VI", Writable, 1000)),
        new("gear-loot", PackAction.Sell, SaleStep.Table, _ => Item(40, 3828, "Flaming Khanjar", Melee, 2740)),
        new("gear-loot", PackAction.Sell, SaleStep.Table, _ => Item(41, 9041, "Sceptre", Caster, 5658)), // a caster her gear rules do not need: loot
        new("trade-goods", PackAction.Sell, SaleStep.Table, _ => Item(42, 2394, "Black Garnet", Gem, 373)),
        new("unknown", PackAction.Keep, SaleStep.Table, _ => Item(43, 99_043, "Strange Thing", 0x80000000, 50)),

        // part 3: the guards on a row that would sell
        new("value-zero-kept", PackAction.Keep, SaleStep.Unsellable, _ => Item(44, SilverPea, "Silver Pea", FociComponents.ComponentType, 0), Detail: "value 0"),
        new("attuned-kept", PackAction.Keep, SaleStep.Unsellable, t => { t.Appraisals[45] = Appraised(45, attuned: 1); return Item(45, 9045, "Attuned Cap", Clothing, 50); }, Detail: "attuned"),
        new("retained-kept", PackAction.Keep, SaleStep.Unsellable, t => { t.Appraisals[46] = Appraised(46, retained: true); return Item(46, 9046, "Retained Glove", Clothing, 40); }, Detail: "retained"),
        new("retained-kept", PackAction.Keep, SaleStep.Unsellable, _ => Item(47, 9047, "Retained Ring", Jewelry, 40, flags: VendorStock.RetainedFlag), Detail: "retained"),
        new("gear-caster-kept", PackAction.Keep, SaleStep.Unsellable, t => { t.FloorCasters.Add(48); return Item(48, 9048, "Odd Baton", Melee, 300); }, Detail: "floor caster"), // the last guard: a piece the caster verdicts keep that is no caster by type
        new("unsellable-class", PackAction.Keep, SaleStep.Unsellable, _ => Item(49, VendorStock.BreadWcid, "Bread", 0x20, 5), Detail: "unsellable class"), // M6 16 E5c2: ACE refuses the class whatever the mask
    ];

    public static TheoryData<int> CaseIndexes() => [.. Enumerable.Range(0, Cases.Length)];

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void Each_row_of_the_table_decides_the_item_made_for_it(int index)
    {
        var c = Cases[index];
        var trip = new Trip();
        var item = c.Make(trip);

        var answer = SaleVerdict.Of(item.Guid, item, c.InSidePack, trip.Book);

        Assert.Equal((c.Rule, c.Action, c.Step), (answer.Rule, answer.Action, answer.Step));
        if (c.Detail is not null)
        {
            Assert.Equal(c.Detail, answer.Detail);
        }

        var known = PackRuleBook.Rules.FirstOrDefault(r => r.Name == c.Rule);
        Assert.Equal(known?.Class ?? PackClass.Components, answer.Class); // a components row is the components rule's own
    }

    [Fact]
    public void The_cases_hold_an_item_for_every_row_of_the_table_but_the_spare_pack_the_sale_never_reaches()
    {
        string[] components = [FociComponents.RuleBurned, FociComponents.RuleStocked, FociComponents.RuleAccountOnly, FociComponents.RuleScarabUnused, FociComponents.RulePea, FociComponents.RuleFociMissing, FociComponents.RuleUnknown];
        var rows = PackRuleBook.Rules.Select(r => r.Name).Concat(components).Where(r => r != "containers-spare").Order(StringComparer.Ordinal);

        Assert.Equal(rows, Cases.Select(c => c.Rule).Distinct().Order(StringComparer.Ordinal));

        // the two rows the cases leave out, by name: the sale keeps every container before the table is asked (it passes no "spare" fact), and an unread spellbook keeps every component
        var trip = new Trip();
        Assert.Equal("containers-in-use", SaleVerdict.Of(60, Item(60, 9060, "Sack", Container, 65), false, trip.Book).Rule);
        Assert.Equal(PackAction.Sell, PackRuleBook.Of("containers-spare").Action);
        var unread = trip.Book with { Component = (_, o) => FociComponents.Judge(o.Weenie.WeenieClassId, o.ItemType, true, new Dictionary<uint, int>()) };
        var pea = SaleVerdict.Of(61, Item(61, SilverPea, "Silver Pea", FociComponents.ComponentType, 25000), false, unread);
        Assert.Equal((FociComponents.RuleBookUnread, SaleStep.ComponentKept), (pea.Rule, pea.Step));
    }

    [Fact]
    public void A_caster_asks_the_caster_rule_alone_of_the_kept_aside_rows()
    {
        var trip = new Trip();
        trip.Appraisals[70] = Appraised(70, spells: MajorFocus); // the keep-aside rule would match a ring with it
        trip.Memory.KeptAside.Add(70); // the loot step noted it
        trip.Packs.Rotation.Add(70); // and the self-equip took it off
        trip.Packs.WearReview.Add(70);

        var loot = SaleVerdict.Of(70, Item(70, 9070, "Fire Sceptre", Caster, 7519), false, trip.Book);
        trip.CasterNeeds[70] = "the best for fire";
        var kept = SaleVerdict.Of(70, Item(70, 9070, "Fire Sceptre", Caster, 7519), false, trip.Book);

        Assert.Equal(("gear-review", SaleStep.Table), (loot.Rule, loot.Step)); // none of the sale's keeps stand on a caster; the table's own review row does (the wear pick's memory)
        trip.Packs.WearReview.Remove(70);
        var sold = SaleVerdict.Of(70, Item(70, 9070, "Fire Sceptre", Caster, 7519), false, trip.Book with { CasterNeed = _ => null });
        Assert.Equal(("gear-loot", PackAction.Sell), (sold.Rule, sold.Action));
        Assert.Equal(("gear-caster-kept", "her gear rules need it: the best for fire"), (kept.Rule, kept.Detail));
    }

    [Fact]
    public void The_sales_rows_stand_before_the_table_and_the_guards_only_on_a_row_that_would_sell()
    {
        var trip = new Trip();

        // kept aside beats everything after it: a noted find that is also a stock line, a drink, dropped before
        trip.Memory.KeptAside.Add(80);
        trip.Memory.Dropped.Add(80);
        Assert.Equal("kept-aside-noted", SaleVerdict.Of(80, Item(80, 27319, "Health Tincture", Misc, 6500), false, trip.Book).Rule);

        // a surplus drink on which a guard stands is kept as a drink in use
        trip.Surplus.Add(81);
        trip.Appraisals[81] = Appraised(81, retained: true);
        var drink = SaleVerdict.Of(81, Item(81, 27319, "Health Tincture", Misc, 6500), false, trip.Book);
        Assert.Equal(("drinks-in-use", SaleStep.DrinkKept), (drink.Rule, drink.Step));

        // a component the rule keeps is its own row even when dropped before; one it sells that was dropped before is not offered
        trip.Memory.Dropped.Add(82);
        trip.Memory.Dropped.Add(83);
        Assert.Equal(FociComponents.RuleBurned, SaleVerdict.Of(82, Item(82, LeadScarab, "Lead Scarab", FociComponents.ComponentType, 10), false, trip.Book).Rule);
        var droppedPea = SaleVerdict.Of(83, Item(83, SilverPea, "Silver Pea", FociComponents.ComponentType, 25000), false, trip.Book);
        Assert.Equal(("dropped-before", true), (droppedPea.Rule, droppedPea.IsComponent));

        // a component the rule sells is not caught by the supplies rows (they ask only what is no component): one that is a kit line is sold by the components rule, one that is a container by type is kept by the table's own container row
        var kitPea = SaleVerdict.Of(84, Item(84, SilverPea, "Silver Pea", FociComponents.ComponentType, 25000), false, trip.Book with { Ground = Den with { Supplies = Den.Supplies with { Kit = [new KitLine(SilverPea, "Silver Pea", KitSlot.Pack)] } } });
        Assert.Equal((FociComponents.RulePea, PackAction.Sell), (kitPea.Rule, kitPea.Action));
        var boxPea = SaleVerdict.Of(84, Item(84, SilverPea, "Silver Pea", FociComponents.ComponentType | Container, 25000), false, trip.Book);
        Assert.Equal(("containers-in-use", SaleStep.Table), (boxPea.Rule, boxPea.Step));

        // an attuned piece on a keep or drop row keeps that row's name and action; the guard's word is the detail
        trip.Appraisals[85] = Appraised(85, attuned: 1);
        var tool = SaleVerdict.Of(85, Item(85, 20646, "Ust", 0x20000000, 10), false, trip.Book);
        Assert.Equal(("tinkering-tool", PackAction.Drop, SaleStep.Unsellable, "attuned"), (tool.Rule, tool.Action, tool.Step, tool.Detail));

        // the guards' order: worn, no value, attuned, retained, the gear rules' caster
        trip.Appraisals[86] = Appraised(86, attuned: 1, retained: true);
        trip.FloorCasters.Add(86);
        Assert.Equal("attuned", SaleVerdict.Unsellable(86, Item(86, 9086, "Cap", Clothing, 50), trip.Book));
        Assert.Equal("value 0", SaleVerdict.Unsellable(86, Item(86, 9086, "Cap", Clothing, 0), trip.Book));
        Assert.Equal("worn", SaleVerdict.Unsellable(86, Item(86, 9086, "Cap", Clothing, 0, worn: true), trip.Book));
        trip.Appraisals[87] = Appraised(87, retained: true);
        trip.FloorCasters.Add(87);
        Assert.Equal("retained", SaleVerdict.Unsellable(87, Item(87, 9087, "Cap", Clothing, 50), trip.Book));
    }

    [Fact]
    public void M6_16_E5c2_a_class_ACE_refuses_is_kept_by_its_own_guard_for_every_asker_but_the_trips_own_list()
    {
        var trip = new Trip();
        var bread = Item(110, VendorStock.BreadWcid, "Bread", 0x20, 5); // Food, inside the shopkeeper's mask: the table's row would sell it
        var arrows = Item(111, VendorStock.ArrowWcid, "Arrow", 0x100, 40);

        Assert.Equal(("unsellable-class", PackAction.Keep), (SaleVerdict.Of(110, bread, false, trip.Book).Rule, SaleVerdict.Of(110, bread, false, trip.Book).Action));
        Assert.Equal("unsellable-class", SaleVerdict.Of(111, arrows, false, trip.Book).Rule);
        Assert.False(SaleVerdict.WillSell(110, bread, false, trip.Book)); // no sale value: the trip trigger's estimate and the audit's gate

        // the town trip's own list offers them (Q11 keeps this: ACE refuses them, and two trips' refusals drop them, SaleMemory.Dropped)
        var trips = trip.Book with { OffersRefusedClasses = true };
        Assert.Equal(("trade-goods", PackAction.Sell), (SaleVerdict.Of(110, bread, false, trips).Rule, SaleVerdict.Of(110, bread, false, trips).Action));
        Assert.Equal(("gear-loot", PackAction.Sell), (SaleVerdict.Of(111, arrows, false, trips).Rule, SaleVerdict.Of(111, arrows, false, trips).Action));

        // the guard stands after retained and before the gear rules' caster
        trip.Appraisals[112] = Appraised(112, retained: true);
        Assert.Equal("retained", SaleVerdict.Unsellable(112, Item(112, VendorStock.BreadWcid, "Bread", 0x20, 5), trip.Book));
        trip.FloorCasters.Add(113);
        Assert.Equal("unsellable class", SaleVerdict.Unsellable(113, Item(113, VendorStock.BreadWcid, "Bread", 0x20, 5), trip.Book));
        Assert.True(VendorStock.UnsellableClass(VendorStock.BreadWcid) && VendorStock.UnsellableClass(VendorStock.ArrowWcid) && !VendorStock.UnsellableClass(2394));
    }

    [Fact]
    public void M6_16_E5c2_what_a_sale_will_sell_is_a_sell_verdict_on_a_type_a_buyer_of_the_ground_takes()
    {
        var trip = new Trip();
        var garnet = Item(120, 2394, "Black Garnet", Gem, 373);
        var kit = Item(121, 630, "Gifted Healing Kit", 0x10000000, 120); // a healing kit by name, of a type no vendor of the den buys

        Assert.True(SaleVerdict.WillSell(120, garnet, false, trip.Book));
        Assert.Equal(PackAction.Sell, SaleVerdict.Of(121, kit, false, trip.Book).Action); // the row sells it
        Assert.Equal(0u, kit.ItemType & Den.SellableItemTypes);
        Assert.False(SaleVerdict.WillSell(121, kit, false, trip.Book)); // and no buyer's mask takes it: the sale walks to no one for it
        trip.Packs.Rotation.Add(122);
        Assert.False(SaleVerdict.WillSell(122, Item(122, 9122, "Old Helm", Armor, 800), false, trip.Book)); // a keep is no sale
    }

    [Fact]
    public void A_stone_the_keep_does_not_hold_is_shed_and_sold_and_asking_writes_no_memory()
    {
        var trip = new Trip();
        trip.StonesKept.Add(90);
        var book = trip.Book;

        var kept = SaleVerdict.Of(90, Item(90, 2436, "Greater Mana Stone", Stone, 5000), false, book);
        var shed = SaleVerdict.Of(91, Item(91, 2436, "Greater Mana Stone", Stone, 5000), false, book);
        var noted = SaleVerdict.Of(92, Item(92, 9092, "Plain Ring", Jewelry, 500), false, book);
        var caster = SaleVerdict.Of(93, Item(93, 9093, "Sceptre", Caster, 200), false, book);

        Assert.Equal(("mana-stones", "mana-stones-shed", "gear-loot", "gear-loot"), (kept.Rule, shed.Rule, noted.Rule, caster.Rule));
        Assert.Empty(trip.Packs.Shed); // the trip notes a shed stone in the layout's memory; the verdict does not
        Assert.Empty(trip.Memory.KeptAside);
        Assert.Empty(trip.Memory.Dropped);
        Assert.Empty(trip.Packs.Rotation);
        Assert.Empty(trip.Packs.WearReview);
    }

    [Fact]
    public void The_three_kinds_the_sale_reads_in_a_side_pack_are_the_components_the_healing_kits_and_the_essences()
    {
        Assert.True(SaleVerdict.SoldFromSidePack(Item(100, SilverPea, "Silver Pea", FociComponents.ComponentType, 25000)));
        Assert.True(SaleVerdict.SoldFromSidePack(Item(101, 630, "Gifted Healing Kit", Misc, 120)));
        Assert.True(SaleVerdict.SoldFromSidePack(Item(102, 49422, "Acid Spectre Essence (80)", Misc, 5000)));
        Assert.False(SaleVerdict.SoldFromSidePack(Item(103, 2394, "Black Garnet", Gem, 373)));
        Assert.False(SaleVerdict.SoldFromSidePack(Item(104, 9104, "Old Helm", Armor, 800)));
    }
}

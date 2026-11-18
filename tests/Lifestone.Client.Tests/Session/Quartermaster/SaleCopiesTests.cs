using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Quartermaster;

/// <summary>
/// M6 16 E5c2 (docs/automation-engine.md E5, <c>TODO(engine-sale-copies)</c>): the copies of "may this be sold or dropped" that now ask the rule book's verdict (<see cref="SaleVerdict.Of"/>): the field's make-room rule and
/// the burden the Strength floor leaves out, the trip trigger's sale estimate, the start-up audit's sale gate. Each test states what the copy answers and what it answered before the slice. (M6 16 E5c wrote this file as the
/// sale shadow's tests: the copies decided by their own rules and a shadow named where the verdict differed; the shadow went with the copies' own rules.)
/// </summary>
public sealed class SaleCopiesTests
{
    private const uint Armor = 0x2, Misc = 0x80, Caster = 0x8000, Stone = 0x80000;
    private const uint Helm = 0x8E5C0301, Hauberk = 0x8E5C0302, Kit = 0x8E5C0303, Wand = 0x8E5C0304, Oil = 0x8E5C0305, ManaStone = 0x8E5C0306, Tincture = 0x8E5C0307, Garnet = 0x8E5C0308;

    private static readonly HuntingGround Den = GroundCatalog.Get("black-spawn-den-aluvian");
    private static readonly Dictionary<uint, SpellInfo> NoSpells = [];

    // ---------------------------------------------------------------------------------------------- the session's sale desk

    [Fact]
    public void M6_16_E5c2_the_sale_desk_holds_the_jobs_statement_a_station_states_only_when_none_stands_and_a_context_is_made_afresh_at_each_ask()
    {
        var state = new WorldActionRig().H.State;
        var desk = SaleDesk.For(state);
        Assert.Same(desk, SaleDesk.For(state));
        Assert.Equal((false, null, 0, null), (desk.JobStated, desk.Character, desk.Spells.Count, desk.Facts)); // not stated: an empty SpellTable, no facts

        var stations = new Dictionary<uint, SpellInfo> { [1] = WorldActionRig.Spell(1, DatReaderWriter.Enums.SpellType.Enchantment) };
        var stationFacts = new Lifestone.Client.Session.Scenarios.Journey.JourneyFacts();
        desk.StateIfNone("A Station's", stations, stationFacts);
        Assert.Equal((true, "A Station's"), (desk.JobStated, desk.Character));
        Assert.Same(stations, desk.Spells);

        desk.StateIfNone("Another Station's", NoSpells, new Lifestone.Client.Session.Scenarios.Journey.JourneyFacts()); // one stands: left as it is
        Assert.Equal("A Station's", desk.Character);
        Assert.Same(stationFacts, desk.Facts);

        var jobFacts = new Lifestone.Client.Session.Scenarios.Journey.JourneyFacts();
        desk.StateJob("The Job's", NoSpells, jobFacts); // the job's statement stands over a station's
        Assert.Equal("The Job's", desk.Character);
        Assert.Same(jobFacts, desk.Facts);

        var book = desk.BookOn(Den);
        Assert.Same(Den, book.Ground);
        Assert.NotSame(book, desk.BookOn(Den)); // every part reads the pack of the ask
    }

    [Fact]
    public void M6_16_E5c2_the_burden_the_strength_floor_leaves_out_is_the_fields_sell_loot_by_the_sessions_rules()
    {
        // A healing kit, a gem and a scroll no rule keeps, 10 burden each. Before this slice this read named no scroll rule and so kept every scroll: 20. The session's scroll rule reads no spell of the scroll: sell loot, 30.
        var kit = SaleVerdictTests.Item(0x8E5C0601, 630, "Gifted Healing Kit", Misc, 120);
        var garnet = SaleVerdictTests.Item(0x8E5C0602, 2394, "Black Garnet", 0x800, 373);
        var scroll = SaleVerdictTests.Item(0x8E5C0603, 9603, "Scroll of Dual Wield Mastery Self VI", 0x2000, 1_000);
        var boots = SaleVerdictTests.Item(0x8E5C0604, 9604, "Leather Boots", Armor, 900); // not appraised: the field keeps it, so the floor still carries it
        var state = PackOf(kit, garnet, scroll, boots);

        Assert.Equal(30, CantripRoom.SellLootBurden(state, Den, "BSD Mage", NoSpells));
        Assert.Equal([kit.Guid, garnet.Guid, scroll.Guid], CantripRoom.SellLoot(state, SaleContext.For(state, Den, "BSD Mage", NoSpells)).Select(l => l.Guid));
    }

    // ---------------------------------------------------------------------------------------------- the copies

    /// <summary>
    /// A main pack that holds what the copies read differently: a helm the self-equip took off (rotation), an unappraised hauberk, a healing kit, an unappraised wand, a value-0 oil, an unappraised mana stone, a Health Tincture
    /// (the den's stock lists none) and a gem.
    /// </summary>
    private static GameState Pack()
    {
        var r = new WorldActionRig();
        var items = new (uint Guid, uint Wcid, uint Type, uint Value, string Name)[]
        {
            (Helm, 97_311, Armor, 800, "Old Helm"),
            (Hauberk, 97_312, Armor, 3_000, "Diforsa Hauberk"),
            (Kit, 97_313, Misc, 120, "Gifted Healing Kit"),
            (Wand, 97_314, Caster, 5_000, "Plain Baton"),
            (Oil, 97_315, Misc, 0, "Oil of Rendering"),
            (ManaStone, 97_316, Stone, 5_000, "Greater Mana Stone"),
            (Tincture, 27319, Misc, 6_000, "Health Tincture"),
            (Garnet, 97_318, 0x800, 373, "Black Garnet"),
        };
        foreach (var i in items)
        {
            var o = LootRulesTests.Item(i.Type, i.Value, 50, i.Guid, i.Name, i.Wcid);
            r.H.R.Reducer.OnCreateObject(r.H.R.Context, o.Created with { Weenie = o.Created.Weenie with { Header = o.Created.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player } }, isUpdate: false);
        }

        r.H.Describe(new PlayerDescriptionMsg { Inventory = [.. items.Select(i => new ContentDesc(i.Guid, 0))] });
        r.H.State.Appraisals.Add(KeepAsideRuleTests.Answer(Helm), 0); // read, plain
        return r.H.State;
    }

    [Fact]
    public void M6_16_E5c2_the_fields_make_room_rule_is_the_rule_books_sell_loot_less_what_the_field_keeps_by_name()
    {
        PackMemory.Shared.Rotation.Add(Helm);
        try
        {
            var state = Pack();
            var book = SaleContext.For(state, Den, "BSD Mage", NoSpells);
            var census = new Dictionary<string, int>(StringComparer.Ordinal);

            var loot = CantripRoom.SellLoot(state, book, census);

            // the kit and the gem, as before this slice; the hauberk is the difference that stays, by name: the rule book sells it (the town appraises first), the field never drops a piece it has not appraised
            Assert.Equal([Kit, Garnet], loot.Select(l => l.Guid));
            Assert.Equal(("gear-loot", PackAction.Sell), (SaleVerdict.Of(Hauberk, Item(state, Hauberk), false, book).Rule, SaleVerdict.Of(Hauberk, Item(state, Hauberk), false, book).Action));
            Assert.Equal(CantripRoom.KeepUnappraised, CantripRoom.FieldKeep(Hauberk, Item(state, Hauberk), book));
            Assert.Equal(1, census[CantripRoom.KeepUnappraised]);
            Assert.Equal(1, census["pack rule gear-rotation (keep)"]); // the helm
            Assert.Equal(1, census["pack rule gear-caster-kept (keep)"]); // the wand: her gear rules keep a caster she has not read
            Assert.Equal(1, census["pack rule mana-stones (keep)"]);
            Assert.Equal(1, census["pack rule drinks-in-use (keep)"]);
            Assert.Equal(1, census["pack rule value-zero-drop (drop)"]); // the oil: the town's junk drop, never the field's
            Assert.Equal(2, census["sell loot"]);
        }
        finally
        {
            PackMemory.Shared.Rotation.Remove(Helm);
        }
    }

    private static WorldObject Item(GameState state, uint guid) => state.Objects.TryGet(guid, out var o) ? o : throw new InvalidOperationException("not held");

    /// <summary>A state whose main pack holds <paramref name="items"/> (their guids are the objects' own).</summary>
    private static GameState PackOf(params WorldObject[] items)
    {
        var r = new WorldActionRig();
        foreach (var o in items)
        {
            r.H.R.Reducer.OnCreateObject(r.H.R.Context, o.Created with { Weenie = o.Created.Weenie with { Header = o.Created.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player } }, isUpdate: false);
        }

        r.H.Describe(new PlayerDescriptionMsg { Inventory = [.. items.Select(o => new ContentDesc(o.Guid, 0))] });
        return r.H.State;
    }

    [Fact]
    public void M6_16_E5c2_the_field_keeps_an_unappraised_piece_a_useful_great_cantrip_of_any_family_and_every_drink_whatever_the_rule_book_sells()
    {
        const uint MajorFocus = 2574;
        var trip = new SaleVerdictTests.Trip();
        var boots = SaleVerdictTests.Item(0x8E5C0401, 9401, "Leather Boots", Armor, 900); // never appraised
        var wand = SaleVerdictTests.Item(0x8E5C0402, 9402, "Sceptre", Caster, 5_658); // a caster her gear rules do not need, never appraised
        var ring = SaleVerdictTests.Item(0x8E5C0403, 9403, "Ring of Focus", 0x8, 700); // Major Focus, on a ground whose keep-aside rule is none
        var sceptre = SaleVerdictTests.Item(0x8E5C0404, 9404, "Focus Sceptre", Caster, 3_000); // a caster with Major Focus her gear rules were not asked to keep
        var plain = SaleVerdictTests.Item(0x8E5C0405, 9405, "Plain Ring", 0x8, 500); // appraised, no cantrip: loot
        var tincture = SaleVerdictTests.Item(0x8E5C0406, 27319, "Health Tincture", Misc, 6_500); // a stack the pack budget would sell
        trip.Appraisals[ring.Guid] = SaleVerdictTests.Appraised(ring.Guid, spells: MajorFocus);
        trip.Appraisals[sceptre.Guid] = SaleVerdictTests.Appraised(sceptre.Guid, spells: MajorFocus);
        trip.Appraisals[plain.Guid] = SaleVerdictTests.Appraised(plain.Guid);
        trip.Surplus.Add(tincture.Guid);
        var book = trip.Book with { KeepAside = null };
        var state = PackOf(boots, wand, ring, sceptre, plain, tincture);
        var census = new Dictionary<string, int>(StringComparer.Ordinal);

        // the rule book sells all six (the town's sale appraises first, and its wear pick reads a cantrip piece before it); the field keeps five by its three named rules
        Assert.All(new[] { boots, wand, ring, sceptre, plain, tincture }, o => Assert.Equal(PackAction.Sell, SaleVerdict.Of(o.Guid, o, false, book).Action));
        Assert.Equal("drinks-surplus", SaleVerdict.Of(tincture.Guid, tincture, false, book).Rule);

        var loot = CantripRoom.SellLoot(state, book, census);

        Assert.Equal([plain.Guid], loot.Select(l => l.Guid));
        Assert.Equal(2, census[CantripRoom.KeepUnappraised]);
        Assert.Equal(2, census[CantripRoom.KeepGreatCantrip]);
        Assert.Equal(1, census[CantripRoom.KeepDrink]);
        Assert.Null(CantripRoom.FieldKeep(plain.Guid, plain, book));
    }

    [Fact]
    public void M6_16_E5c2_in_the_field_a_scroll_the_every_spell_rule_keeps_is_no_sell_loot_and_a_component_the_rule_book_sells_is()
    {
        const uint SilverPea = 8331, LeadScarab = 691;
        var trip = new SaleVerdictTests.Trip();
        var wanted = SaleVerdictTests.Item(0x8E5C0501, 9501, "Scroll of Acid Stream II", 0x2000, 1_000);
        var useless = SaleVerdictTests.Item(0x8E5C0502, 9502, "Scroll of Dual Wield Mastery Self VI", 0x2000, 1_000);
        var pea = SaleVerdictTests.Item(0x8E5C0503, SilverPea, "Silver Pea", FociComponents.ComponentType, 25_000);
        var scarab = SaleVerdictTests.Item(0x8E5C0504, LeadScarab, "Lead Scarab", FociComponents.ComponentType, 10); // her book burns it
        var bread = SaleVerdictTests.Item(0x8E5C0505, VendorStock.BreadWcid, "Bread", 0x20, 5);
        var refused = SaleVerdictTests.Item(0x8E5C0506, 2394, "Black Garnet", 0x800, 373);
        trip.Scrolls[wanted.Guid] = "the every-spell rule: ACE refused its read this session (skill-short): kept, never bought again";
        trip.Memory.Dropped.Add(refused.Guid); // its drop was tried before
        var state = PackOf(wanted, useless, pea, scarab, bread, refused);
        var census = new Dictionary<string, int>(StringComparer.Ordinal);

        var loot = CantripRoom.SellLoot(state, trip.Book, census);

        // Before this slice: the wanted scroll was sell loot (the fight's scroll hook knew the scroll rule alone), the pea was kept (every component was), the Bread was sell loot (a trade good) and so was the garnet whose
        // drop had been tried.
        Assert.Equal([(useless.Guid, "scrolls"), (pea.Guid, FociComponents.RulePea)], loot.Select(l => (l.Guid, l.Rule)));
        Assert.Equal(1, census["pack rule scrolls-kept (keep)"]);
        Assert.Equal(1, census["pack rule " + FociComponents.RuleBurned + " (keep)"]);
        Assert.Equal(1, census["pack rule unsellable-class (keep)"]);
        Assert.Equal(1, census["pack rule dropped-before (keep)"]);
    }

    [Fact]
    public void M6_16_E5c2_the_trip_triggers_sale_estimate_is_the_rule_books_and_a_check_reads_it_from_its_cache_while_the_pack_stands()
    {
        PackMemory.Shared.Rotation.Add(Helm);
        try
        {
            var state = Pack();
            var counters = new LogisticsCounters(Den);

            // Before this slice the estimate's own list counted 15,293: the helm 800 (rotation gear), the hauberk 3,000, the kit 120, the mana stone 5,000 (the kit keeps it), the Health Tincture 6,000 (a drink she uses) and
            // the gem 373. The sale keeps the helm, the stone and the tincture, so the rule book's estimate is the hauberk, the kit and the gem.
            Assert.Equal(3_000 + 120 + 373, counters.CarriedSaleValue(state));
            var rate = Den.LootBuyers.Max(b => b.Rate); // every buyer of the den pays one rate
            Assert.All(Den.LootBuyers, b => Assert.Equal(rate, b.Rate));
            Assert.Equal((long)Math.Floor(3_000 * rate) + (long)Math.Floor(120 * rate) + (long)Math.Floor(373 * rate), counters.CarriedSalePayout(state));
            Assert.Equal((3_493L, counters.CarriedSalePayout(state)), counters.SaleEstimate);

            // the pack stands: the estimate is read from its cache, the verdict is not asked, nothing is allocated
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
            {
                counters.CarriedSaleValue(state);
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

            // an input the stamps read changes (the rotation's memory): the estimate is worked out afresh
            PackMemory.Shared.Rotation.Remove(Helm);
            Assert.Equal(800 + 3_000 + 120 + 373, counters.CarriedSaleValue(state));
        }
        finally
        {
            PackMemory.Shared.Rotation.Remove(Helm);
        }
    }

    [Fact]
    public void M6_16_E5c2_the_start_up_audits_sale_gate_offers_what_the_sale_will_sell()
    {
        PackMemory.Shared.Rotation.Add(Helm);
        try
        {
            var state = Pack();

            // Before this slice the gate's own list offered six: Old Helm (rotation gear), Diforsa Hauberk, Gifted Healing Kit, Greater Mana Stone (the kit keeps it), Health Tincture (a drink she uses) and Black Garnet.
            Assert.Equal(["Diforsa Hauberk", "Gifted Healing Kit", "Black Garnet"], StartupAudit.SaleOffers(state, Den));

            PackMemory.Shared.Rotation.Remove(Helm); // the helm is loot again
            Assert.Equal(["Old Helm", "Diforsa Hauberk", "Gifted Healing Kit", "Black Garnet"], StartupAudit.SaleOffers(state, Den));
        }
        finally
        {
            PackMemory.Shared.Rotation.Remove(Helm);
        }
    }
}

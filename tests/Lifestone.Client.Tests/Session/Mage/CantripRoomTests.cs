using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 keep every great cantrip (the owner's ruling, 2026-11-15: asked whether she was looting and saving all major cantrips then a ruling that these must be kept). Live ladder27-29: from 01:46 UTC she saw 14 pieces with a Major cantrip she wants and took 2;
/// the other 12 were left over the load line while her pack held ordinary sell loot (10:08:39: Studded Leather Leggings with Major Life Magic Aptitude, burden 726, left at 17,664 of 17,850 while the same plan kept a Katar). A great-cantrip
/// piece outranks every piece of sell loot: the plan drops the sell loot she carries with the lowest value per burden first until the piece fits (<see cref="MageLootRules.Plan"/>, <see cref="CantripRoom"/>); the heavy rule never leaves
/// one; a wand kept for its War Magic bonus and a Minor-cantrip piece keep their present rule.
/// </summary>
public sealed class CantripRoomTests
{
    private const uint Armor = 0x2, Jewelry = 0x8, Caster = 0x8000, Melee = 0x1, Misc = 0x80, Writable = 0x2000;
    private const uint MajorLife = 2520, MajorFocus = 2574, MinorFocus = 2581, MajorHeavy = 2531, EpicWar = 4715, LegendaryWillpower = 6101; // M6 14 useful cantrips: the real spell ids (ACE's spell table)
    private const int Capacity = 17_850;

    private static readonly Dictionary<uint, SpellInfo> Spells = new()
    {
        [MajorLife] = Named(MajorLife, "Major Life Magic Aptitude"),
        [MajorFocus] = Named(MajorFocus, "Major Focus"),
        [MinorFocus] = Named(MinorFocus, "Minor Focus"),
        [MajorHeavy] = Named(MajorHeavy, "Major Heavy Weapon Aptitude"),
        [EpicWar] = Named(EpicWar, "Epic War Magic Aptitude"),
        [LegendaryWillpower] = Named(LegendaryWillpower, "Legendary Willpower"),
    };

    private static SpellInfo Named(uint id, string name) => WorldActionRig.Spell(id, SpellType.Enchantment) with { Name = name };

    /// <summary>The climber's loot rule (her keep-aside default, the load line at 100% of the capacity), with the per-item burden cap when given.</summary>
    private static MageLootRules Climber(int maxItemBurden = 0) =>
        new(new MageLootThresholds { LoadLinePercent = 100, MaxItemBurden = maxItemBurden, KeepAside = new KeepAsideRule(KeepAsideRule.ClimberDefault) }, Spells);

    private static WorldObject Find(uint guid, string name, uint type, uint value, ushort burden) => LootRulesTests.Item(type, value, burden, guid, name);

    /// <summary>A corpse's piece as the loot step reads it: the keep-aside match on its appraisal (its spells), and the self-equip's word when <paramref name="judged"/> is given.</summary>
    private static MageLootItem Piece(MageLootRules rules, uint guid, string name, uint type, uint value, ushort burden, double? war = null, SelfEquipKind? judged = null, params uint[] spells) =>
        rules.ItemFor(Find(guid, name, type, value, burden), KeepAsideRuleTests.Appraised(guid, war, spells), held: false, judged is { } kind ? c => new SelfEquipChoice(kind, c.Guid, c.Name, 0, 0, "no free slot: the worn robe covers its coverage") : null, UsefulCantripTests.Qwinnie);

    /// <summary>The sell loot she carries in the 10:08 case: two cheap heavy pieces, a platter and a gem.</summary>
    private static readonly CarriedLoot ChainmailLeggings = new(21, "Chainmail Leggings", 800, 400), PlatemailGauntlets = new(22, "Platemail Gauntlets", 900, 300), GoldPlatter = new(23, "Gold Platter", 3000, 300, "trade-goods"), Diamond = new(24, "Diamond", 5000, 10, "trade-goods");

    private static readonly List<CarriedLoot> Carried = [GoldPlatter, Diamond, PlatemailGauntlets, ChainmailLeggings]; // the main pack's order is not the drop order

    [Fact]
    public void The_10_08_case_drops_the_lowest_value_per_burden_sell_loot_until_the_leggings_fit_under_the_line_and_takes_them()
    {
        var rules = Climber();
        var tusk = new MageLootItem(11, "Sharp Tusker Slave Tusk", Misc, 0, 10);
        var katar = new MageLootItem(12, "Katar", Melee, 2782, 114);
        var leggings = Piece(rules, 13, "Studded Leather Leggings", Armor, 8753, 726, spells: MajorLife);
        var scroll = new MageLootItem(14, "Scroll of Boon of the Arrow Turner", Writable, 2000, 30);
        MageLootItem[] corpse = [tusk, katar, leggings, scroll];

        var before = rules.Plan("Corpse of Tusker Slave", "Winnie", corpse, burden: 17_664, Capacity, freeSlots: 65); // what ran live: no sell loot to drop
        var plan = rules.Plan("Corpse of Tusker Slave", "Winnie", corpse, burden: 17_664, Capacity, freeSlots: 65, sellLoot: Carried);

        Assert.True(leggings.GreatCantrip);
        Assert.Equal(MageLootVerdict.Load, before.VerdictOf(13)); // live: "left Studded Leather Leggings ... over the load line"
        Assert.Equal([14u, 12u], before.Kept); // and the Katar and the scroll kept

        // 17,664 + 726 is 540 over 17,850: the 400 of v/b 2 then the 300 of v/b 3 go, 700 freed; the platter (v/b 10) and the gem (v/b 500) stay
        Assert.Equal([21u, 22u], plan.Drops.Select(d => d.Loot.Guid));
        Assert.All(plan.Drops, d => Assert.Equal((13u, "Studded Leather Leggings"), (d.ForGuid, d.ForName)));
        Assert.Equal((700L, 1700L), (plan.DroppedBurden, plan.DroppedValue));
        Assert.Equal(MageLootVerdict.KeepAside, plan.VerdictOf(13));
        Assert.Equal([13u, 14u, 12u], plan.Kept); // the leggings first, then the scroll and the Katar fit in what is left: 17,664 - 700 + 726 + 30 + 114 = 17,834
        Assert.Equal(MageLootVerdict.ValuePerBurden, plan.VerdictOf(11));
        Assert.Equal([13u], plan.RoomMadeFor);
        Assert.Empty(plan.Shorts);
        Assert.Equal(
            ["loot: make room for the great cantrip 'Studded Leather Leggings' 0x0000000D (Major Life Magic Aptitude; value 8753, burden 726): burden 17664 of 17850 (load line 100% = 17850), 65 free slot(s): drop 2 sell loot item(s), burden 700, value 1700, the lowest value per burden first: 'Chainmail Leggings' 0x00000015 value 800 burden 400 v/b 2, 'Platemail Gauntlets' 0x00000016 value 900 burden 300 v/b 3"],
            plan.RoomLines());
        Assert.Contains("loot: worth a look: 'Studded Leather Leggings' 0x0000000D (Major Life Magic Aptitude); room made: 2 sell loot item(s) dropped first (burden 700, value 1700)", plan.WorthALookLines());
        Assert.Contains("loot: kept Studded Leather Leggings value 8753 burden 726 v/b 12.06 (floor 1, worth a look, a great cantrip: room made by dropping 2 sell loot item(s))", plan.DecisionLines());
        Assert.Contains(" room_drops=2 room_dropped_burden=700", plan.Summary(), StringComparison.Ordinal);
        Assert.Equal(
            "'Chainmail Leggings' 0x00000015 value 800 burden 400 v/b 2 (pack rule gear-loot: sell loot, the lowest value per burden she carries) to make room for the great cantrip 'Studded Leather Leggings' 0x0000000D",
            plan.Drops[0].Reason);
    }

    [Fact]
    public void When_dropping_all_her_sell_loot_cannot_make_room_nothing_is_dropped_and_the_piece_is_left_loudly_with_the_trip_due()
    {
        var rules = Climber();
        var breastplate = Piece(rules, 31, "Covenant Breastplate", Armor, 29_895, 2200, spells: MajorFocus);

        var load = rules.Plan("Corpse of Tusker Guard", "Winnie", [breastplate], burden: 17_840, Capacity, freeSlots: 65, sellLoot: [Diamond]);
        var slots = rules.Plan("Corpse of Tusker Guard", "Winnie", [breastplate], burden: 1_000, Capacity, freeSlots: 0, sellLoot: []);

        Assert.Empty(load.Drops);
        Assert.Equal(MageLootVerdict.Load, load.VerdictOf(31));
        var shortfall = Assert.Single(load.Shorts);
        Assert.Equal(MageLootVerdict.Load, shortfall.Verdict);
        Assert.Equal("dropping all 1 sell loot item(s) she carries (burden 10, value 5000) cannot make room: burden 17840 + 2200 against the load line 100% = 17850, 65 free slot(s) (the piece goes to the main pack)", shortfall.Why);
        Assert.Equal(
            ["loot: NO ROOM for the great cantrip 'Covenant Breastplate' 0x0000001F (Major Focus; burden 2200): dropping all 1 sell loot item(s) she carries (burden 10, value 5000) cannot make room: burden 17840 + 2200 against the load line 100% = 17850, 65 free slot(s) (the piece goes to the main pack); it is left on the corpse (over the load line) and the full-pack trip is due (m6-pack-room)"],
            load.RoomLines());
        Assert.Contains("loot: worth a look: 'Covenant Breastplate' 0x0000001F (Major Focus); left: over the load line, no room could be made (a great cantrip): the full-pack trip is due", load.WorthALookLines());
        Assert.Contains(" no_room=1", load.Summary(), StringComparison.Ordinal);
        Assert.Equal(MageLootVerdict.Slots, slots.VerdictOf(31));
        Assert.Equal(MageLootVerdict.Slots, Assert.Single(slots.Shorts).Verdict);

        // the full-pack trip is due at once: the pack room counts each as a keeper left (one left for the slots counts too)
        var room = new PackRoomMemory();
        room.NoteLoot(load);
        room.NoteLoot(slots);
        Assert.Equal(2, room.LoadLeftKeepers);
    }

    [Fact]
    public void A_full_main_pack_drops_the_one_lowest_value_per_burden_piece_for_the_slot()
    {
        var rules = Climber();
        var necklace = Piece(rules, 41, "Necklace", Jewelry, 5093, 45, spells: MajorFocus);

        var plan = rules.Plan("Corpse of Tusker Guard", "Winnie", [necklace], burden: 1_000, Capacity, freeSlots: 0, sellLoot: Carried);

        Assert.Equal([21u], plan.Drops.Select(d => d.Loot.Guid)); // the burden is fine: one drop frees the one slot
        Assert.Equal([41u], plan.Kept);
    }

    [Fact]
    public void A_heavy_Major_breastplate_that_is_no_upgrade_is_kept_and_room_made_for_it_while_a_heavy_wand_that_is_none_is_still_left()
    {
        var rules = Climber(maxItemBurden: 150);
        var breastplate = Piece(rules, 51, "Covenant Breastplate", Armor, 29_895, 2200, judged: SelfEquipKind.Review, spells: MajorFocus);
        var sceptre = Piece(rules, 52, "Fire Sceptre", Caster, 7519, 200, war: 1.05, judged: SelfEquipKind.Review);
        var hauberk = new CarriedLoot(25, "Diforsa Hauberk", 6000, 1000);

        var fits = rules.Plan("Corpse of Tusker Guard", "Winnie", [breastplate, sceptre], burden: 15_000, Capacity, freeSlots: 65, sellLoot: [.. Carried, hauberk]);
        var full = rules.Plan("Corpse of Tusker Guard", "Winnie", [breastplate, sceptre], burden: 17_000, Capacity, freeSlots: 65, sellLoot: [.. Carried, hauberk]);

        Assert.Equal(MageLootVerdict.KeepAside, fits.VerdictOf(51)); // D149 would have left it: heavy and no upgrade; the owner's rule keeps every great cantrip
        Assert.Empty(fits.Drops);
        Assert.Equal(MageLootVerdict.HeavyNotUpgrade, fits.VerdictOf(52)); // a wand kept for its War Magic bonus keeps its present rule
        Assert.Contains("loot: worth a look: 'Covenant Breastplate' 0x00000033 (Major Focus); heavy (burden 2200 over 150) and not an upgrade, but kept for its great cantrip (the owner, 2026-11-15): review: no free slot: the worn robe covers its coverage", fits.WorthALookLines());
        Assert.Contains("loot: worth a look but heavy and not an upgrade: left 'Fire Sceptre' 0x00000034 burden 200 (War Magic bonus +5%; review: no free slot: the worn robe covers its coverage)", fits.WorthALookLines());

        // 17,000 + 2,200 is 1,350 over: v/b 2 (400), 3 (300), then the hauberk's 6 (1,000); the platter (10) and the gem stay
        Assert.Equal([21u, 22u, 25u], full.Drops.Select(d => d.Loot.Guid));
        Assert.Equal([51u], full.Kept);
    }

    [Fact]
    public void A_wand_kept_for_its_war_bonus_and_a_Minor_cantrip_piece_keep_their_present_rule_and_the_great_pieces_go_first()
    {
        var rules = Climber();
        var wand = Piece(rules, 61, "Acid Baton", Caster, 10_000, 50, war: 1.03); // v/b 200: before, the best of the keep-aside finds
        var minor = Piece(rules, 62, "Minor Ring", Jewelry, 900, 15, spells: MinorFocus);
        var other = Piece(rules, 63, "Heavy Ring", Jewelry, 900, 15, spells: MajorHeavy);
        var epic = Piece(rules, 64, "Epic Ring", Jewelry, 900, 15, spells: EpicWar); // v/b 60
        var legendary = Piece(rules, 65, "Legendary Amulet", Jewelry, 900, 15, spells: LegendaryWillpower);

        var atTheLine = rules.Plan("Corpse of Tusker Guard", "Winnie", [wand, minor, other], burden: Capacity, Capacity, freeSlots: 65, sellLoot: Carried);
        var oneSlot = rules.Plan("Corpse of Tusker Guard", "Winnie", [wand, epic, legendary], burden: 1_000, Capacity, freeSlots: 1, sellLoot: Carried);

        Assert.Equal((false, true), (wand.GreatCantrip, wand.KeepAside is not null)); // a war bonus alone is no great cantrip
        Assert.Equal((false, null), (minor.GreatCantrip, minor.KeepAside)); // Minor: no tier of the rule; plain loot by its value per burden
        Assert.Equal((false, null), (other.GreatCantrip, other.KeepAside)); // M6 14 useful cantrips: a Major cantrip of a skill she neither holds nor plans (Heavy Weapons)
        Assert.True(epic.GreatCantrip && legendary.GreatCantrip);
        Assert.Empty(atTheLine.Shorts); // no great cantrip's room is made for any of the three
        Assert.Equal(MageLootVerdict.Load, atTheLine.VerdictOf(61)); // the wand keeps its present rule: left over the line, no room made for it

        // M6 14 hour load (the owner's ruling, 2026-11-16: she is not married to something just because she picked it up): the Minor ring and the other Major ring are plain sell loot of v/b 60: the first is swapped in for the chainmail (v/b 2),
        // whose 400 make room for the second too
        Assert.Equal([21u], atTheLine.Drops.Select(d => d.Loot.Guid));
        Assert.All(atTheLine.Drops, d => Assert.True(d.Swap && d.ForGuid == 62u));
        Assert.Equal([62u, 63u], atTheLine.Kept);

        Assert.Equal([64u, 65u], oneSlot.Kept); // the great pieces first: the Epic ring takes the one slot, room is made for the Legendary amulet
        Assert.Equal([21u], oneSlot.Drops.Select(d => d.Loot.Guid));
        Assert.All(oneSlot.Drops, d => Assert.Equal(65u, d.ForGuid));
        Assert.Equal(MageLootVerdict.Slots, oneSlot.VerdictOf(61)); // the wand is left for the slot, no room made for it

        Assert.True(KeepAsideRule.IsGreat("Major Focus") && KeepAsideRule.IsGreat("Epic Focus") && KeepAsideRule.IsGreat("Legendary Focus"));
        Assert.False(KeepAsideRule.IsGreat("Minor Focus") || KeepAsideRule.IsGreat("Majorette") || KeepAsideRule.IsGreat(null) || KeepAsideRule.IsGreat("Strength Self VI"));
    }

    [Fact]
    public void The_check_after_the_drops_takes_a_piece_that_still_does_not_fit_off_the_plan_and_the_looter_skips_it()
    {
        var rules = Climber();
        var leggings = Piece(rules, 13, "Studded Leather Leggings", Armor, 8753, 726, spells: MajorLife);
        var katar = new MageLootItem(12, "Katar", Melee, 2782, 114);
        var plan = rules.Plan("Corpse of Tusker Slave", "Winnie", [katar, leggings], burden: 17_664, Capacity, freeSlots: 65, sellLoot: Carried);
        Assert.Equal([13u, 12u], plan.Kept);

        Assert.Empty(plan.Refit(burden: 16_964, Capacity, freeSlots: 67)); // the drops made what the plan said: nothing is taken off
        Assert.Equal([13u], plan.Refit(burden: 17_664, Capacity, freeSlots: 65, apply: false)); // a drop refused: the leggings no longer fit (the Katar alone would)
        Assert.Equal([13u, 12u], plan.Kept); // a dry read takes nothing off

        Assert.Equal([13u], plan.Refit(burden: 17_664, Capacity, freeSlots: 65));
        Assert.Equal([12u], plan.Kept);
        Assert.Equal(MageLootVerdict.Load, plan.VerdictOf(13));
        Assert.Equal("over the load line when read again (burden 17664 + 726 against 100% of 17850)", plan.WithdrawnWhy(13));
        Assert.Equal(LootDecision.Burden, plan.Decide(Find(13, "Studded Leather Leggings", Armor, 8753, 726), new LootContext(Burden: 17_664, Capacity: Capacity, Inventory: null)));
        Assert.Equal(LootDecision.Take, plan.Decide(Find(12, "Katar", Melee, 2782, 114), new LootContext(Burden: 17_664, Capacity: Capacity, Inventory: null)));
        Assert.Equal((2782L, 114L), (plan.KeptValue, plan.KeptBurden));
        Assert.Contains("loot: worth a look: 'Studded Leather Leggings' 0x0000000D (Major Life Magic Aptitude); room made: 2 sell loot item(s) dropped first (burden 700, value 1700); left: over the load line; over the load line when read again (burden 17664 + 726 against 100% of 17850)", plan.WorthALookLines());
    }

    // ------------------------------------------------------------------------------------------------ what she may drop: the pack rules' sell loot, never a keep

    /// <summary>
    /// M6 16 E5c2: the verdict's context a test's make-room read asks: the session's (<see cref="SaleContext.For"/>) over the test's SpellTable, with the test's keep-aside rule and, when given, its own scroll, caster and
    /// stone rules named over it (as a fight names its own: <see cref="CantripRoom.FieldBook"/>).
    /// </summary>
    internal static SaleContext Book(GameState state, HuntingGround ground, KeepAsideRule? keepAside, IReadOnlyDictionary<uint, SpellInfo> spells, Func<WorldObject, string?>? scroll = null, Func<uint, string?>? casters = null, Func<uint, bool>? stones = null)
    {
        var book = SaleContext.For(state, ground, null, spells) with { KeepAside = keepAside };
        if (scroll is not null)
        {
            book = book with { ScrollKeep = scroll };
        }

        if (casters is not null)
        {
            book = book with { CasterNeed = casters, FloorCaster = g => casters(g) is not null };
        }

        return stones is null ? book : book with { StoneKept = stones };
    }

    private const uint Hauberk = 0x8C0C0001, Ruby = 0x8C0C0002, Girth = 0x8C0C0003, Boots = 0x8C0C0004, Taper = 0x8C0C0005, Potion = 0x8C0C0006, Favor = 0x8C0C0007, Ware = 0x8C0C0008, Baton = 0x8C0C0009, Ring = 0x8C0C000A, Ewer = 0x8C0C000B, Scroll = 0x8C0C000C;

    [Fact]
    public void SellLoot_reads_only_the_appraised_sell_loot_of_her_main_pack_and_never_a_supply_a_keep_or_a_cantrip_piece()
    {
        var ground = GroundCatalog.Get("tusker-barracks");
        var stock = ground.Supplies.Stock[0].Wcid;
        var r = new WorldActionRig();
        var items = new (uint Guid, uint Wcid, uint Type, uint Value, ushort Burden, string Name)[]
        {
            (Hauberk, 9101, Armor, 3000, 1000, "Platemail Hauberk"), // appraised, no spells: sell loot (gear-loot)
            (Ruby, 9102, 0x800, 5000, 10, "Ruby"), // a gem: trade-goods
            (Girth, 9103, Armor, 4000, 400, "Studded Leather Girth"), // Major Heavy Weapon Aptitude: M6 14 useful cantrips: a skill she neither holds nor plans, so sell loot
            (Boots, 9104, Armor, 900, 300, "Leather Boots"), // never appraised: never dropped blind
            (Taper, 20631, 0x1000, 50, 5, "Prismatic Taper"), // a component
            (Potion, FighterKit.ManaPotionWcid, 0x20, 200, 5, "Mana Potion"), // a drink she uses
            (Favor, 31000, 0x800, 1000, 5, "Blackmoor's Favor"), // the self-buff gem (the owner, 2026-11-15)
            (Ware, stock, 0x80, 100, 5, "Stock Ware"), // a stock line of the ground
            (Baton, 9105, Caster, 1000, 50, "Plain Baton"), // a caster: her gear rules need it (the only one she holds)
            (Ring, 9106, Jewelry, 700, 15, "Ring of Focus"), // Major Focus: the keep-aside rule's
            (Ewer, 9107, Misc, 400, 60, "Silver Ewer"), // misc, no appraisal needed: trade-goods
            (Scroll, 9108, Writable, 200, 30, "Scroll of Frost Bolt II"), // a scroll no rule keeps (the scroll rule reads no spell of it; M6 16 E5c2: asked without a rule, this read kept every scroll): sell loot
        };
        foreach (var i in items)
        {
            var o = LootRulesTests.Item(i.Type, i.Value, i.Burden, i.Guid, i.Name, i.Wcid);
            r.H.R.Reducer.OnCreateObject(r.H.R.Context, o.Created with { Weenie = o.Created.Weenie with { Header = o.Created.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player } }, isUpdate: false);
        }

        r.H.Describe(new PlayerDescriptionMsg { Inventory = [.. items.Select(i => new ContentDesc(i.Guid, 0))] });
        r.PrivateInt(PlayerInventory.EncumbranceProperty, 17_000);
        var state = r.H.State;
        state.Appraisals.Add(KeepAsideRuleTests.Answer(Hauberk), 0);
        state.Appraisals.Add(KeepAsideRuleTests.Answer(Girth, null, MajorHeavy), 0);
        state.Appraisals.Add(KeepAsideRuleTests.Answer(Baton), 0);
        state.Appraisals.Add(KeepAsideRuleTests.Answer(Ring, null, MajorFocus), 0);
        string? Name(uint id) => Spells.TryGetValue(id, out var s) ? s.Name : null;

        var census = new Dictionary<string, int>(StringComparer.Ordinal);
        var loot = CantripRoom.SellLoot(state, Book(state, ground, new KeepAsideRule(KeepAsideRule.ClimberDefault), Spells), census);

        // The review pack holds only the pieces the owner wants to see (the owner, 2026-11-16): a Major, Epic or Legendary cantrip of any family, read from the appraisal; a caster, plain gear or an item not
        // appraised stays in the main pack.
        Assert.True(CantripRoom.IsGreatPiece(state, Ring, Name)); // Major Focus
        Assert.False(CantripRoom.IsGreatPiece(state, Girth, Spells)); // M6 14 useful cantrips (the owner, 2026-11-16): Major Heavy Weapon Aptitude raises a skill she neither holds nor plans: not a great piece
        Assert.False(CantripRoom.IsGreatPiece(state, Baton, Name)); // a caster with no cantrip
        Assert.False(CantripRoom.IsGreatPiece(state, Hauberk, Name)); // plain gear
        Assert.False(CantripRoom.IsGreatPiece(state, Ruby, Name)); // not appraised

        Assert.Equal([(Hauberk, "gear-loot"), (Ruby, "trade-goods"), (Girth, "gear-loot"), (Ewer, "trade-goods"), (Scroll, "scrolls")], loot.Select(l => (l.Guid, l.Rule))); // the girth is sell loot now
        Assert.Equal(1, census[CantripRoom.KeepUnappraised]); // the boots: the field's own keep, over the rule book's gear-loot
        Assert.Equal(1, census["pack rule kept-aside-match (keep)"]); // the ring
        Assert.Equal(1, census["pack rule gear-caster-kept (keep)"]); // the baton
        Assert.Equal(1, census["pack rule drinks-in-use (keep)"]);
        Assert.Equal(7, census.Where(c => c.Key != "sell loot").Sum(c => c.Value)); // the taper and the stock ware are the components rule's keeps
        Assert.Equal(1, census["pack rule self-buff-gem (keep)"]);
        Assert.Equal((3000L, 1000L), (loot[0].Value, loot[0].Burden));
        var fill = CantripRoom.PackFill(state, loot);
        Assert.Contains("sell loot she may drop 5 item(s) burden 1500; kept aside 0 piece(s) burden 0; supplies, kit and the other keeps 7 item(s) burden 385; the heaviest she may not drop: 'Leather Boots' 300, 'Plain Baton' 50, 'Ring of Focus' 15, 'Blackmoor's Favor' 5, 'Mana Potion' 5", fill, StringComparison.Ordinal);
        Assert.StartsWith("burden 17000 of ", fill, StringComparison.Ordinal);
    }

    private const uint Essence = 0x8C0C0021, Kit = 0x8C0C0022, Useless = 0x8C0C0023, Readable = 0x8C0C0024, NoGain = 0x8C0C0025, Review = 0x8C0C0026;

    /// <summary>
    /// M6 14 (live ladder42 19:51:19 UTC: 0 of 102 slots free at 108% burden under the line 136%, every find refused "no swap could make room"): of the loot she held, 33 scrolls her book cannot use (a 1,000 or 2,000 Value each), 16
    /// essences (4,000 to 5,000) and 3 healing kits were held out of the swap: the scrolls by a blanket keep, the others because only gear and trade goods counted. Every item the pack rules sell is sell loot now; a scroll is kept only when
    /// the scroll rule keeps it; a wear upgrade the station judged "no gain" carries no keep (sell loot), one kept for the next visit's review does; the census names each item's reason for the log.
    /// </summary>
    [Fact]
    public void Every_item_the_pack_rules_sell_is_sell_loot_and_a_scroll_only_when_the_scroll_rule_leaves_it()
    {
        var ground = GroundCatalog.Get("tusker-quarters");
        var r = new WorldActionRig();
        var items = new (uint Guid, uint Wcid, uint Type, uint Value, ushort Burden, string Name)[]
        {
            (Essence, 9201, Misc, 4000, 50, "Frost Wisp Essence (50)"), // pack rule essences: sold to Renald
            (Kit, 630, Misc, 120, 50, "Gifted Healing Kit"), // pack rule healing-kits
            (Useless, 9203, Writable, 1000, 30, "Scroll of Dual Wield Mastery Self VI"), // her book cannot use it: the scroll rule leaves it
            (Readable, 9204, Writable, 2000, 30, "Scroll of Infernae"), // the scroll rule keeps it to be read
            (NoGain, 9205, Armor, 12119, 361, "Sollerets"), // a loot-time wear upgrade the station's wear pick judged "no gain": no keep
            (Review, 9206, Armor, 9000, 300, "Covenant Sollerets"), // kept for the next visit's review
        };
        foreach (var i in items)
        {
            var o = LootRulesTests.Item(i.Type, i.Value, i.Burden, i.Guid, i.Name, i.Wcid);
            r.H.R.Reducer.OnCreateObject(r.H.R.Context, o.Created with { Weenie = o.Created.Weenie with { Header = o.Created.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player } }, isUpdate: false);
        }

        r.H.Describe(new PlayerDescriptionMsg { Inventory = [.. items.Select(i => new ContentDesc(i.Guid, 0))] });
        var state = r.H.State;
        state.Appraisals.Add(KeepAsideRuleTests.Answer(NoGain), 0);
        state.Appraisals.Add(KeepAsideRuleTests.Answer(Review), 0);
        PackMemory.Shared.WearReview.Add(Review);
        try
        {
            var census = new Dictionary<string, int>(StringComparer.Ordinal);

            var loot = CantripRoom.SellLoot(state, Book(state, ground, new KeepAsideRule(KeepAsideRule.ClimberDefault), Spells, o => o.Guid == Readable ? "the scroll rule: readable" : null), census);
            var sessions = CantripRoom.SellLoot(state, Book(state, ground, new KeepAsideRule(KeepAsideRule.ClimberDefault), Spells));

            Assert.Equal([(Essence, "essences"), (Kit, "healing-kits"), (Useless, "scrolls"), (NoGain, "gear-loot")], loot.Select(l => (l.Guid, l.Rule)));
            Assert.Equal("sell loot 4, pack rule gear-review (keep) 1, pack rule scrolls-kept (keep) 1", CantripRoom.CensusLine(census)); // M6 16 E5c2: a keep is named by the rule book's row
            Assert.Contains(sessions, l => l.Guid == Useless); // M6 16 E5c2: a read that names no scroll rule asks the session's (it kept every scroll before): the scroll rule reads no spell her book could use in either scroll
        }
        finally
        {
            PackMemory.Shared.WearReview.Remove(Review);
        }
    }
}

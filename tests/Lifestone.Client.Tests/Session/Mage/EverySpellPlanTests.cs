using DatReaderWriter.Enums;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Housing;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.ServerTools;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 (the owner, 2026-11-16: with the money she has, she buys every spell, eventually when affordable): the every-spell rule (<see cref="EverySpellPlan"/>) over the catalog's scriveners and a shop of real stock rows
/// (<c>scrivener-stock.json</c>: Frost Bolt IV 2943 at the Scrivener of War Magic 20226, Value 100, so 5,000 pyreals; the VI at the Grand Masters 20224 and 30018). Proved: the order (the gates, her bootstrap, damage, then buffs and
/// protections, then the rest; the bot's lines in their own place, M6 14 own buffs); the reserve's parts from the ground's data; the purse above the reserve (a scroll waits, a cheaper one after it is still bought, a reserve over the purse buys nothing); a batch per vendor at home and off
/// home; a known target, a known lower level and the plan's lines; a refused read never bought again and a held scroll read for nothing; the stock file and the option.
/// </summary>
public sealed class EverySpellPlanTests
{
    /// <summary>The levels' powers of a I to VI line (the SpellTable's pattern for these lines: 1, 50, 100, 150, 200, 250).</summary>
    private static readonly uint[] Powers = [1, 50, 100, 150, 200, 250];

    private static readonly Dictionary<string, (MagicSchool School, bool Beneficial)> Lines = new(StringComparer.Ordinal)
    {
        ["Frost Bolt"] = (MagicSchool.WarMagic, false),
        ["Harm Other"] = (MagicSchool.LifeMagic, false),
        ["Flame Streak"] = (MagicSchool.WarMagic, false),
        ["Armor Self"] = (MagicSchool.LifeMagic, true),
        ["Strength Other"] = (MagicSchool.CreatureEnchantment, true),
        ["Bafflement Other"] = (MagicSchool.CreatureEnchantment, false),
        ["Focus Self"] = (MagicSchool.CreatureEnchantment, true),
    };

    /// <summary>The catalog with a shop of the test's lines only (real rows of the stock file).</summary>
    private static ScrivenerCatalog Catalog { get; } = ScrivenerCatalog.Default.WithStock(ScrivenerStock.Of(ScrivenerStock.Default.Spells.Where(s => Lines.ContainsKey(s.Line))));

    private static readonly HashSet<uint> Holtburg = [.. ScrivenerCatalog.Default.Scriveners.Where(s => s.Places.Any(p => p.Town == "Holtburg")).Select(s => s.Wcid)];

    private static EverySpellFact? Fact(uint id)
    {
        if (Catalog.Stock.Spell(id) is not { } spell || !Lines.TryGetValue(spell.Line, out var line))
        {
            return id == 1785 ? new EverySpellFact("Cassius' Ring of Fire", MagicSchool.WarMagic, 300, false) : null;
        }

        return new EverySpellFact(spell.Name, line.School, Powers[spell.Level - 1], line.Beneficial);
    }

    /// <summary>A climber of skill <paramref name="skill"/> in every school (IV at the combat bar from 193, IV at the buff bar from 166), the book empty, a purse of 1,000,000 and no reserve.</summary>
    private static EverySpellInput Input(uint skill = 193, long purse = 1_000_000, Func<uint, bool>? knows = null) => new()
    {
        Knows = knows ?? (_ => false),
        Fact = Fact,
        Skill = _ => skill,
        Catalog = Catalog,
        AtHome = Holtburg.Contains,
        Purse = purse,
        Covered = line => line == "Focus Self",
        BootstrapLines = EverySpellPlan.BootstrapLines(Catalog, _ => true),
    };

    private static EverySpellPick PickOf(EverySpellResult result, string line) => result.Picks.Single(p => p.Line == line);

    [Fact]
    public void The_order_is_her_bootstrap_then_the_damage_lines_then_her_buffs_and_protections_then_the_rest_the_bots_lines_in_their_own_place()
    {
        var result = EverySpellPlan.Plan(Input());

        Assert.Equal(["Focus Self IV", "Frost Bolt IV", "Harm Other IV", "Flame Streak IV", "Armor Self IV", "Bafflement Other IV", "Strength Other IV"], result.Buys.Select(p => p.Spell!.Name));
        Assert.Equal([EverySpellTier.Bootstrap, EverySpellTier.Damage, EverySpellTier.Damage, EverySpellTier.Damage, EverySpellTier.Buff, EverySpellTier.Rest, EverySpellTier.Rest], result.Buys.Select(p => p.Tier));
        Assert.All(result.Buys, p => Assert.Equal(5_000, p.Price)); // Value 100 x SellPrice 50 (Vendor.GetSellCost)
        Assert.Contains("the combat bar 0.95", PickOf(result, "Frost Bolt").Why, StringComparison.Ordinal);
        Assert.Contains("the buff bar 0.75", PickOf(result, "Armor Self").Why, StringComparison.Ordinal);
        Assert.Contains(EverySpellPlan.CoveredNote, PickOf(result, "Focus Self").Why, StringComparison.Ordinal); // M6 14 own buffs: the bot's cover moves it nowhere
        Assert.Equal(EverySpellVerdict.Unsold, PickOf(result, "Cassius' Ring of Fire").Verdict); // the picker's ring: its scroll is on no vendor's create list
        Assert.Equal(7, result.Wanted);
    }

    [Fact]
    public void The_highest_level_at_the_bar_is_the_one_bought_and_no_lower_level_is_asked_first()
    {
        var war = EverySpellPlan.Plan(Input(skill: 243)); // V (power 200) at 0.95 from 243; VI needs 293

        Assert.Equal("Frost Bolt V", PickOf(war, "Frost Bolt").Spell!.Name);
        Assert.Single(war.Picks, p => p.Line == "Frost Bolt"); // ACE's CanReadScroll asks no lower level: one scroll a line
        Assert.Equal(EverySpellVerdict.NoLevel, PickOf(EverySpellPlan.Plan(Input(skill: 40)), "Frost Bolt").Verdict); // I (power 1) needs 44 at 0.95
        Assert.Equal(EverySpellVerdict.Untrained, PickOf(EverySpellPlan.Plan(Input() with { Skill = s => s == MagicSchool.CreatureEnchantment ? null : 193 }), "Bafflement Other").Verdict);
    }

    [Fact]
    public void The_purse_above_the_reserve_pays_in_the_order_a_dear_scroll_waits_and_a_cheaper_one_after_it_is_still_bought()
    {
        var reserve = new EverySpellReserve(100_000, 0, 0, 0, "test");
        var input = Input(purse: 112_000) with { Reserve = reserve, Skill = s => s == MagicSchool.CreatureEnchantment ? 50u : 193u }; // 12,000 above the reserve; Creature 50: level I (50 pyreals) of its lines

        var result = EverySpellPlan.Plan(input);

        Assert.Equal(12_000, result.Spendable);
        Assert.Equal(["Focus Self I", "Frost Bolt IV", "Harm Other IV", "Bafflement Other I", "Strength Other I"], result.Buys.Select(p => p.Spell!.Name));
        Assert.Equal(10_150, result.Spent);
        Assert.Equal(EverySpellVerdict.Purse, PickOf(result, "Flame Streak").Verdict);
        Assert.Contains("waits: 5000 is over the 1950 the purse 112000 holds above the reserve 100000", PickOf(result, "Flame Streak").Why, StringComparison.Ordinal);

        var none = EverySpellPlan.Plan(input with { Reserve = reserve with { Supplies = 200_000 } }); // a reserve over the purse spends nothing
        Assert.Empty(none.Buys);
        Assert.Equal(0, none.Spendable);
        Assert.Equal(none.Wanted, none.Count(EverySpellVerdict.Purse));
    }

    [Fact]
    public void The_reserve_is_her_supplies_at_their_keeps_the_bots_fee_the_villa_and_the_planned_needs()
    {
        var ground = GroundCatalog.Get("tusker-holding");
        long supplies = 0;
        foreach (var line in ground.Supplies.Stock.Where(l => l.Wcid != BuffBotWire.FeeWcid))
        {
            supplies += (long)(ground.PriceOf(line.Wcid) ?? 0) * line.Keep;
        }

        supplies += ground.TownStock.Where(l => l.Wcid != BuffBotWire.FeeWcid).Sum(l => (long)l.Price * l.Keep);
        var fee = ground.TownStock.Concat([]).Where(l => l.Wcid == BuffBotWire.FeeWcid).Select(l => (long)l.Price * l.Keep).DefaultIfEmpty((long)(ground.PriceOf(BuffBotWire.FeeWcid) ?? 0) * (ground.Supplies.StockOf(BuffBotWire.FeeWcid)?.Keep ?? 0)).Max();
        Assert.True(fee > 0, "D138: every ground carries the fee's line");

        var full = EverySpellPlan.ReserveOf(ground, 79, botOn: true, villaOn: true, HouseOwnership.None, 30_000, "two scrolls");
        Assert.Equal((supplies, fee, VillaFacts.PricePyreals + VillaFacts.RentPyreals, 30_000L), (full.Supplies, full.BuffBot, full.Villa, full.Planned));
        Assert.Equal(supplies + fee + 2_100_000 + 30_000, full.Total);
        Assert.Contains("the villa 2100000 (the price 2000000 and the first rent 100000", full.Why, StringComparison.Ordinal);

        Assert.Equal(VillaFacts.RentPyreals, EverySpellPlan.ReserveOf(ground, 79, true, true, HouseOwnership.Owned, 0, "none").Villa); // owned: the next rent
        Assert.Equal(0, EverySpellPlan.ReserveOf(ground, 79, true, villaOn: false, HouseOwnership.None, 0, "none").Villa);
        Assert.Equal(0, EverySpellPlan.ReserveOf(ground, BuffBotWire.FeeLevel - 1, true, false, HouseOwnership.None, 0, "none").BuffBot); // under the fee's level a round is free
        Assert.Equal(0, EverySpellPlan.ReserveOf(ground, 79, botOn: false, false, HouseOwnership.None, 0, "none").BuffBot);
    }

    [Fact]
    public void The_buys_are_batched_by_vendor_at_home_and_by_the_sellers_off_home()
    {
        var home = EverySpellPlan.Plan(Input() with { Covered = _ => false });
        var batches = home.Batches.ToDictionary(b => b.Key);

        Assert.Equal(["Frost Bolt IV", "Flame Streak IV"], batches["20226"].Picks.Select(p => p.Spell!.Name)); // the Scrivener of War Magic
        Assert.Equal(["Harm Other IV", "Armor Self IV"], batches["20222"].Picks.Select(p => p.Spell!.Name)); // the Scrivener of Life Magic
        Assert.Equal(["Focus Self IV", "Bafflement Other IV", "Strength Other IV"], batches["20210"].Picks.Select(p => p.Spell!.Name)); // the Scrivener of Creature Magic: her buff (not the bot's here) before the rest
        Assert.Equal(10_000, batches["20226"].Cost);
        Assert.All(home.Batches, b => Assert.True(b.Home));

        var town = EverySpellPlan.Plan(Input(skill: 293)); // VI at 0.95: the Grand Masters' only
        var war = town.Batches.Single(b => b.Key == "20224+30018");
        Assert.False(war.Home);
        Assert.Equal(["Frost Bolt VI", "Flame Streak VI"], war.Picks.Select(p => p.Spell!.Name));
        Assert.Equal(100_000, war.Cost);
        Assert.Contains("sold only off home (", war.Picks[0].Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_target_the_book_holds_is_known_a_lower_level_held_still_buys_and_a_plan_line_keeps_the_plans_answer()
    {
        var frostIV = Catalog.Stock.LineSpells("Frost Bolt")[3].SpellId;
        var frostIII = Catalog.Stock.LineSpells("Frost Bolt")[2].SpellId;
        Assert.Equal(EverySpellVerdict.Known, PickOf(EverySpellPlan.Plan(Input(knows: id => id == frostIV)), "Frost Bolt").Verdict);
        Assert.Equal(EverySpellVerdict.Buy, PickOf(EverySpellPlan.Plan(Input(knows: id => id == frostIII)), "Frost Bolt").Verdict);

        var plan = new Dictionary<string, EverySpellPlanLine>(StringComparer.Ordinal)
        {
            ["Harm Other"] = new(LearnNeedKind.Due, ScrivenerLineKind.Attack, 4), // the plan buys it on this trip
            ["Frost Bolt"] = new(LearnNeedKind.Known, ScrivenerLineKind.Bolt, 2), // the plan's level is held: the rule may go higher
            ["Focus Self"] = new(LearnNeedKind.BotCovers, ScrivenerLineKind.SelfBuff, 0), // the plan calls no trip for it: the rule buys her own, in her bootstrap's place
        };
        var result = EverySpellPlan.Plan(Input() with { PlanLines = plan });
        Assert.Equal(EverySpellVerdict.PlanLine, PickOf(result, "Harm Other").Verdict);
        Assert.Equal(EverySpellVerdict.Buy, PickOf(result, "Frost Bolt").Verdict);
        Assert.Equal((EverySpellVerdict.Buy, EverySpellTier.Bootstrap), (PickOf(result, "Focus Self").Verdict, PickOf(result, "Focus Self").Tier));
        Assert.Equal(EverySpellVerdict.PlanLine, PickOf(EverySpellPlan.Plan(Input() with { OnTrip = new HashSet<uint> { frostIV } }), "Frost Bolt").Verdict); // the trip reads it already
    }

    [Fact]
    public void A_scroll_she_could_not_read_is_never_bought_again_and_a_held_scroll_is_read_for_nothing()
    {
        var frost = Catalog.Stock.LineSpells("Frost Bolt")[3];
        var streak = Catalog.Stock.LineSpells("Flame Streak")[3];
        var result = EverySpellPlan.Plan(Input() with { RefusedBefore = id => id == frost.SpellId, Held = w => w == streak.ScrollWcid });

        var refused = PickOf(result, "Frost Bolt");
        Assert.Equal(EverySpellVerdict.RefusedBefore, refused.Verdict);
        Assert.Contains("never bought again", refused.Why, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Buys, p => p.Spell!.SpellId == frost.SpellId);
        var held = PickOf(result, "Flame Streak");
        Assert.Equal((EverySpellVerdict.ReadHeld, 0), (held.Verdict, held.Price));
        Assert.Equal(25_000, result.Spent); // five scrolls of IV; the refused and the held cost nothing

        var memory = new EverySpellMemory();
        memory.Unreadable[frost.SpellId] = "skill-short";
        Assert.Equal("off", memory.Marker(false));
        Assert.Equal("on,unread,learned:0,refused:1", memory.Marker(true));
        memory.Last = new EverySpellMarker(result.Wanted, result.Buys.Count, 0, 0, 0);
        Assert.Equal("on,wanted:6,buy:5,learned:0,refused:1,reserve:0,next:none", memory.Marker(true));
    }

    [Fact]
    public void The_room_on_a_trip_holds_the_scrolls_over_it_for_the_next()
    {
        var result = EverySpellPlan.Plan(Input() with { Room = 2 });

        Assert.Equal(["Focus Self IV", "Frost Bolt IV"], result.Buys.Select(p => p.Spell!.Name));
        Assert.Equal(5, result.Count(EverySpellVerdict.Room));
        Assert.Contains("waits for the next trip", PickOf(result, "Flame Streak").Why, StringComparison.Ordinal);
        Assert.Single(EverySpellPlan.Plan(Input() with { BurdenRoom = 59 }).Buys); // a scroll's burden is 30 (int 5)
    }

    [Fact]
    public void The_stock_is_every_scroll_of_the_catalogs_scriveners_and_their_vendor_view_sells_it()
    {
        var stock = ScrivenerStock.Default;
        Assert.Equal(1527, stock.Spells.Count); // the file's source: 2,783 create-list rows, 1,527 spells
        Assert.All(ScrivenerCatalog.Default.Spells, s => Assert.Equal((s.ScrollWcid, s.ScrollValue, s.Level), (stock.Spell(s.SpellId)!.ScrollWcid, stock.Spell(s.SpellId)!.ScrollValue, stock.Spell(s.SpellId)!.Level)));
        Assert.Equal("Aura of Spirit Drinker Self", EverySpellPlan.StockLine(ScrivenerCatalog.Default, "Spirit Drinker")); // the catalog's short name, the shop's own
        Assert.Contains("Aura of Spirit Drinker Self", EverySpellPlan.BuffLines(ScrivenerCatalog.Default, []));
        Assert.Equal(("Flame Volley", 3), ScrivenerStock.LineOf("Flame Volley III"));
        Assert.Equal(("Lifestone Recall", 0), ScrivenerStock.LineOf("Lifestone Recall"));
        Assert.Equal(["Flame Streak", "Flame Volley", "Flame Blast"], EverySpellPlan.WarShapeLines); // the arc is left out, as the picker leaves it

        var streak = stock.LineSpells("Flame Streak").Single(s => s.Level == 4);
        var war = ScrivenerCatalog.Default.VendorsIn("Holtburg").Single(v => v.Wcid == 20226);
        Assert.Equal(5_000, war.Sells.Single(w => w.Wcid == streak.ScrollWcid).Price); // a scroll no plan line names, in the vendor view at his price
        Assert.Equal(war.Sells.Count, war.Sells.Select(w => w.Wcid).Distinct().Count());
    }

    [Fact]
    public void The_option_is_on_by_default_and_off_by_its_word()
    {
        static AutopilotOptions Parse(params string[] args) => AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(args));

        Assert.True(new AutopilotOptions().EverySpell);
        Assert.True(Parse().EverySpell);
        Assert.False(Parse("--autopilot-every-spell=off").EverySpell);
        Assert.Contains(" every_spell=off", Parse("--autopilot-every-spell=off").ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("every_spell", Parse().ToString(), StringComparison.Ordinal);
        Assert.NotEmpty(Parse("--autopilot-every-spell=maybe").Errors);
        Assert.True(OptionCatalog.Registry.IsKnown("--autopilot-every-spell"));
    }
    // ------------------------------------------------------------------------------------------------ M6 14 gate lines

    [Fact]
    public void M6_14_the_gates_armour_lines_are_the_armour_lines_the_ladders_gates_read_and_no_mastery()
    {
        var lines = EverySpellPlan.GateArmourLines(ScrivenerCatalog.Default, LadderLoader.WarMage);

        Assert.Equal(["Armor Self", "Bludgeoning Protection Self", "Impenetrability", "Bludgeon Bane"], lines); // the Burrow's gate first: the order the gates read them
        Assert.DoesNotContain("Focus Self", lines); // a gate line, but no armour line: its own tier
        Assert.DoesNotContain(EverySpellPlan.StockLine(ScrivenerCatalog.Default, "War Magic Mastery Self"), lines);
        Assert.Equal(lines.Count, lines.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void M6_14_a_gates_armour_line_the_bot_casts_for_her_is_bought_first_not_last()
    {
        // ladder52: Armor Self VI waited "last" behind every other line while the bot cast it; the bot's round fell short at 04:55:00 and every gate read shut on it
        var input = Input() with { Covered = line => line is "Focus Self" or "Armor Self", GateLines = ["Armor Self"] };
        var result = EverySpellPlan.Plan(input);

        Assert.Equal("Armor Self IV", result.Buys[0].Spell!.Name);
        Assert.Equal(EverySpellTier.Gate, result.Buys[0].Tier);
        Assert.Contains("a gate's armour line: first (the buff bot casts it for her now, and a gate reads her book)", PickOf(result, "Armor Self").Why, StringComparison.Ordinal);
        Assert.Equal(EverySpellTier.Bootstrap, PickOf(result, "Focus Self").Tier); // M6 14 own buffs: a covered bootstrap line comes right after the gates
        Assert.Equal(["Armor Self IV", "Focus Self IV"], result.Buys.Take(2).Select(p => p.Spell!.Name));

        // the room of a trip goes to the gate's line first
        var room = EverySpellPlan.Plan(input with { Room = 1 });
        Assert.Equal(["Armor Self IV"], room.Buys.Select(p => p.Spell!.Name));
        Assert.Equal(EverySpellVerdict.Room, PickOf(room, "Frost Bolt").Verdict);

        // when her coin allows: the purse above the reserve still pays in the order, the gate's line first
        var purse = EverySpellPlan.Plan(input with { Purse = 5_000 });
        Assert.Equal(["Armor Self IV"], purse.Buys.Select(p => p.Spell!.Name));
        Assert.Equal(EverySpellVerdict.Purse, PickOf(purse, "Frost Bolt").Verdict);
        Assert.Equal(EverySpellVerdict.Purse, PickOf(EverySpellPlan.Plan(input with { Purse = 4_999 }), "Armor Self").Verdict); // never past the purse
    }

    // ------------------------------------------------------------------------------------------------ M6 14 own buffs

    [Fact]
    public void M6_14_own_buffs_with_the_bot_covering_Focus_Self_she_still_buys_her_own_level_right_after_the_gates_as_her_coin_allows()
    {
        // live ladder57 (2026-11-17): Focus Self VI waited "last" behind the rest of the shop while the bot cast it; her book held no level, and when the bot's boons lapsed her War Magic fell from 378 to 326.
        // Skill 193 is what the bot's lines give (IV at the buff bar); on her own bootstrap Creature Enchantment stands at 116, which casts III at 0.75 (power 100 + 16).
        var input = Input() with { Covered = line => line is "Focus Self" or "Armor Self", GateLines = ["Armor Self"], OwnSkill = s => s == MagicSchool.CreatureEnchantment ? 116u : 193u };
        var result = EverySpellPlan.Plan(input);

        var focus = PickOf(result, "Focus Self");
        Assert.Equal((EverySpellVerdict.Buy, EverySpellTier.Bootstrap, "Focus Self III"), (focus.Verdict, focus.Tier, focus.Spell!.Name)); // the level she casts with no bot about
        Assert.Equal(["Armor Self IV", "Focus Self III", "Frost Bolt IV"], result.Buys.Take(3).Select(p => p.Spell!.Name)); // the gate's line, then her bootstrap ahead of every other line
        Assert.Contains("CreatureEnchantment skill 116 (standing on her own bootstrap, the bot's lines not counted), the buff bar 0.75", focus.Why, StringComparison.Ordinal);
        Assert.Contains("her bootstrap (Focus, Willpower, her schools' masteries): right after the gates", focus.Why, StringComparison.Ordinal);
        Assert.Contains("; " + EverySpellPlan.CoveredNote, focus.Why, StringComparison.Ordinal); // the owner's direction, named on the pick
        Assert.Equal("IV", PickOf(result, "Bafflement Other").Spell!.Name.Split(' ')[^1]); // a line of the rest keeps the plan's standing read

        // as her coin allows: the purse above the reserve pays the gate's line and her Focus, and the next line waits
        var paid = EverySpellPlan.Plan(input with { Purse = PickOf(result, "Armor Self").Price + focus.Price });
        Assert.Equal(["Armor Self IV", "Focus Self III"], paid.Buys.Select(p => p.Spell!.Name));
        Assert.Equal(EverySpellVerdict.Purse, PickOf(paid, "Frost Bolt").Verdict);

        // once her book holds the level she casts, it is known: nothing is bought again
        Assert.Equal(EverySpellVerdict.Known, PickOf(EverySpellPlan.Plan(input with { Knows = id => id == focus.Spell.SpellId }), "Focus Self").Verdict);
    }

    [Fact]
    public void M6_14_own_buffs_her_bootstrap_is_Focus_Willpower_and_her_trained_masteries_and_the_defence_and_armour_lines_lead_her_buffs()
    {
        var catalog = ScrivenerCatalog.Default;
        Assert.Equal(
            ["Focus Self", "Willpower Self", "Creature Enchantment Mastery Self", "War Magic Mastery Self", "Item Enchantment Mastery Self", "Life Magic Mastery Self", "Mana Conversion Mastery Self"],
            EverySpellPlan.BootstrapLines(catalog, _ => true));
        Assert.Equal(
            ["Focus Self", "Willpower Self", "Creature Enchantment Mastery Self", "War Magic Mastery Self"],
            EverySpellPlan.BootstrapLines(catalog, id => id is MageTemplate.CreatureEnchantment or MageTemplate.WarMagic)); // Item, Life and Mana Conversion untrained: their masteries are not hers
        Assert.All(EverySpellPlan.BootstrapLines(catalog, _ => true), l => Assert.NotEmpty(catalog.Stock.LineSpells(l))); // the shop sells each by that name

        var buffs = EverySpellPlan.BuffLines(catalog, []).ToList();
        var strength = buffs.IndexOf(EverySpellPlan.StockLine(catalog, "Strength Self"));
        Assert.True(strength > 0);
        Assert.All(new[] { "Invulnerability Self", "Impregnability Self", "Magic Resistance Self", "Armor Self" }, l => Assert.InRange(buffs.IndexOf(EverySpellPlan.StockLine(catalog, l)), 0, strength - 1)); // the defence and armour lines before the rest of her buffs
    }
}

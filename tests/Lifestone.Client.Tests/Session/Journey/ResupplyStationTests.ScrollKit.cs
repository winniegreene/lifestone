using DatReaderWriter.Enums;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// The owner's rule of 2026-11-08 (checking for components is part of purchasing the spell scrolls): the components of a scroll the climber buys are in the trip's plan beside it, read from the foci formula when she holds the school's focus and from the account's full formula when not,
/// two passes' worth. The owner's rule of 2026-11-09 (D116) took the gate out: the scroll waits only for its own price; buying the components is a required step of the same trip, and what it cannot buy is named and kept on a standing shopping list the next trip buys first.
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const uint SilverScarab = 688;
    private const uint CopperScarab = 686;
    private const uint HawthornComponent = 15; // an herb no den vendor sells

    private static readonly uint ItemFocus = LevelFloor.FocusOf(MagicSchool.ItemEnchantment);

    private static uint ComponentOf(uint wcid) => ComponentWeenies.TryGetByWcid(wcid, out var w) ? w.ComponentId : throw new InvalidOperationException("no component of weenie " + wcid);

    private static SpellInfo ScrollSpell(params uint[] formula) => WorldActionRig.Spell(SpiritDrinkerIV, SpellType.Enchantment) with { Name = "Aura of Spirit Drinker Self IV", School = MagicSchool.ItemEnchantment, Power = 150, Formula = formula, FormulaVersion = 1 };

    private static Home Climber(int coins, Func<StockLine, int>? held = null, bool focus = true, bool notes = false)
    {
        var home = new Home(Den, coins, held: held, notes: notes, description: LearnMage(itemRanks: 200));
        home.Vendor.H.State.Self.Name = MageTemplate.ClimberName;
        if (focus)
        {
            home.Own("Foci of Artifice", ItemFocus, MiscType, 1);
        }

        return home;
    }

    private static Dictionary<uint, SpellInfo> Table(SpellInfo spell) => new() { [spell.Id] = spell };

    [Fact]
    public void The_foci_formula_is_read_with_the_focus_the_full_formula_without_it_and_each_for_two_passes()
    {
        var ground = GroundCatalog.Get(Den);
        var spell = ScrollSpell(ComponentOf(SilverScarab), 63, HawthornComponent, 63, 34, 46, 63, 55);

        var foci = ScrollSupply.Plan(spell, new Dictionary<uint, int>(), holdsFocus: true, "test", ground);
        var full = ScrollSupply.Plan(spell, new Dictionary<uint, int>(), holdsFocus: false, "test", ground);

        Assert.Equal("foci", foci.Formula);
        Assert.Equal(GearKit.FociFormulaCounts(spell.Formula).Select(kv => (Wcid(kv.Key), 2 * kv.Value)).OrderBy(x => x), foci.Components.Select(c => (c.Wcid, c.Want)).OrderBy(x => x));
        Assert.Equal("full", full.Formula);
        Assert.Equal(AccountFormula.Counts(spell, false, "test").Select(kv => (Wcid(kv.Key), 2 * kv.Value)).OrderBy(x => x), full.Components.Select(c => (c.Wcid, c.Want)).OrderBy(x => x));
        Assert.NotEqual(foci.Components.Select(c => c.Wcid).OrderBy(w => w), full.Components.Select(c => c.Wcid).OrderBy(w => w));

        static uint Wcid(uint component) => ComponentWeenies.TryGetByComponent(component, out var w) ? w.Wcid : throw new InvalidOperationException("component " + component);
    }

    [Fact]
    public void With_no_focus_and_no_account_name_the_components_are_a_named_unknown_not_a_guess()
    {
        var plan = ScrollSupply.Plan(ScrollSpell(ComponentOf(SilverScarab)), new Dictionary<uint, int>(), holdsFocus: false, null, GroundCatalog.Get(Den));

        Assert.StartsWith("TODO(m6-scroll-components-account)", plan.Unknown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scroll_she_buys_has_its_components_in_the_same_trips_plan_and_two_passes_are_in_the_pack_before_the_read()
    {
        var home = Climber(2_000_000, held: l => l.Wcid == CopperScarab ? 0 : l.Keep);
        var uses = new List<uint>();
        var spell = ScrollSpell(ComponentOf(CopperScarab));

        var run = home.Start(new ResupplyOptions(Memory: new SaleMemory(), AccountName: "test"), Table(spell));
        var status = home.Stations.Run(run, 600, pump: ScrollServer(home, teach: true, uses));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains(run.BuyList, b => b.Line.Wcid == SpiritDrinkerScroll);
        Assert.True(run.BuyList.Single(b => b.Line.Wcid == CopperScarab).Count >= 2); // the one scarab, two passes at least
        Assert.Equal(1, run.Learned);
        Assert.True(home.Logged("learn: components: Aura of Spirit Drinker Self IV (3256): check before: the foci formula, 2 passes: Copper Scarab 686 want 2 held 0"));
        Assert.True(home.Logged("learn: components: Aura of Spirit Drinker Self IV (3256): check after: the pack holds 2 passes of the foci formula"));
    }

    [Fact]
    public void A_scroll_whose_component_no_vendor_of_the_trip_sells_is_bought_and_read_and_leaves_a_shopping_line()
    {
        var home = Climber(2_000_000, focus: false);
        var uses = new List<uint>();
        var ground = home.Ground;
        var spell = ScrollSpell(ComponentOf(SilverScarab), 63, HawthornComponent, 63, 34, 46, 63, 55);
        var unsold = ScrollSupply.Plan(spell, new Dictionary<uint, int>(), holdsFocus: false, "test", ground).Unsold;
        Assert.NotEmpty(unsold);

        var memory = new SaleMemory();
        memory.FociRefused.Add(ItemFocus); // no focus on this trip either: the full formula stands

        var run = home.Start(new ResupplyOptions(Memory: memory, AccountName: "test"), Table(spell));
        var status = home.Stations.Run(run, 600, pump: ScrollServer(home, teach: true, uses));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains(run.BuyList, b => b.Line.Wcid == SpiritDrinkerScroll);
        Assert.Single(uses);
        Assert.Equal(1, run.Learned);
        Assert.DoesNotContain(run.LearnRefusals, r => r.Contains("no-components", StringComparison.Ordinal));
        Assert.True(memory.ComponentShopping.ContainsKey(unsold[0].Wcid));
        Assert.Equal(unsold[0].Want, memory.ComponentShopping[unsold[0].Wcid].Want);
        Assert.True(home.Logged($"learn: components: Aura of Spirit Drinker Self IV (3256): check before: no vendor of {ground.Home.Town} sells {unsold[0].Name} {unsold[0].Wcid}"));
        Assert.True(home.Logged($"learn: components: check after: TODO(m6-scroll-components-short): {unsold[0].Name} {unsold[0].Wcid} short {unsold[0].Want}"));
    }

    /// <summary>
    /// M6 item 14 (m6-goldscarab; live 2026-11-12: the list held Gold Scarab x10 and Pyreal Scarab x4, named <c>TODO(m6-scroll-components-short)</c> on every trip): a component the ground buys in another town is no gap. Each line that
    /// names it says where and when it is bought (the stop on the way out), the shopping list keeps it only until the pack holds it, and the next trip takes it off the list without buying anything: the list and the town stock never buy
    /// the same ware twice.
    /// </summary>
    [Fact]
    public void A_scrolls_component_the_town_stock_buys_is_named_by_its_stop_not_as_a_gap_and_its_shopping_line_comes_off_once_the_pack_holds_it()
    {
        const uint gold = 687;
        var den = GroundCatalog.Get(Den);
        var line = GroundCatalog.Get("tusker-cave").TownStock.Single(l => l.Wcid == gold);
        var ground = den with { Supplies = den.Supplies with { Stock = [.. den.Supplies.Stock.Where(l => l.Wcid != gold)] }, TownStock = [line] };
        var home = new Home(Den, 2_000_000, description: LearnMage(itemRanks: 200), ground: ground);
        home.Vendor.H.State.Self.Name = MageTemplate.ClimberName;
        home.Own("Foci of Artifice", ItemFocus, MiscType, 1);
        var uses = new List<uint>();
        var memory = new SaleMemory();
        var spell = ScrollSpell(ComponentOf(gold)); // the foci formula: one Gold Scarab and its Prismatic Tapers, which Holtburg sells

        var run = home.Start(new ResupplyOptions(Memory: memory, AccountName: "test"), Table(spell));
        var status = home.Stations.Run(run, 600, pump: ScrollServer(home, teach: true, uses));

        Assert.True(status == ScenarioStatus.Succeeded, run.VerdictFields() + " | " + string.Join(" | ", home.Lines.Where(l => l.Contains("components", StringComparison.Ordinal))));
        Assert.Equal(1, run.Learned);
        Assert.Equal(2, memory.ComponentShopping[gold].Want);
        const string stop = ": the town stock buys it at Archmage Sammam al-Ashiz in Uziz, at the stop on the way out (to 100 when under 40)";
        Assert.True(home.Logged("learn: components: Aura of Spirit Drinker Self IV (3256): check before: no vendor of Holtburg sells Gold Scarab 687 (want 2, held 0): the scroll is bought all the same; the line goes on the shopping list" + stop));
        Assert.True(home.Logged("learn: components: check after: Gold Scarab 687 short 2 (held 0 of 2): no vendor of Holtburg sells it; the shopping list keeps it until the pack holds it" + stop));
        Assert.True(home.Logged("learn: components: Aura of Spirit Drinker Self IV (3256): check after: short of Gold Scarab 687 (held 0 of 2); the scroll is read all the same" + stop));
        Assert.DoesNotContain(home.Lines, l => l.Contains("TODO(m6-scroll-components-short)", StringComparison.Ordinal));
        Assert.DoesNotContain(run.BuyList, b => b.Line.Wcid == gold); // the home walk buys none: no vendor of it sells one

        // The stop at Uziz has filled the pack: the next trip takes the line off the list and buys nothing of it.
        home.Own("Gold Scarab", gold, MiscType, 1, 100);
        var again = home.Start(new ResupplyOptions(Memory: memory, AccountName: "test"), Table(spell));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(again, 600, pump: ScrollServer(home, teach: true, uses)));
        Assert.False(memory.ComponentShopping.ContainsKey(gold));
        Assert.True(home.Logged("learn: components: shopping list: Gold Scarab 687: filled (held 100 of 2)"));
        Assert.DoesNotContain(again.BuyList, b => b.Line.Wcid == gold);
    }

    [Fact]
    public void A_named_unknown_stays_loud_and_does_not_stop_the_buy()
    {
        var home = Climber(2_000_000, focus: false);
        var uses = new List<uint>();
        var memory = new SaleMemory();
        memory.FociRefused.Add(ItemFocus); // no focus: the full formula, and no account name to read it by

        var run = home.Start(new ResupplyOptions(Memory: memory), Table(ScrollSpell(ComponentOf(SilverScarab))));
        var status = home.Stations.Run(run, 600, pump: ScrollServer(home, teach: true, uses));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Single(uses);
        Assert.Equal(1, run.Learned);
        Assert.True(home.Logged("learn: components: Aura of Spirit Drinker Self IV (3256): check before: TODO(m6-scroll-components-account)"));
    }

    /// <summary>
    /// D123 (the owner: sell first, then buy). Long4's trip 3: the plan on a 729-coin purse put the scrolls off, the sale brought in 20,646, and a second trip 14 s later bought them for 6,528. Now the sale comes first and the
    /// stock is planned from the purse after it: the scroll the thin purse would have held is bought, and read, on the same trip.
    /// </summary>
    [Fact]
    public void A_thin_purse_sells_first_and_buys_the_scroll_it_would_have_put_off_on_the_same_trip()
    {
        var home = Climber(729, focus: true);
        var loot = home.Own("Shirt", 130, ClothingType, 22_940); // floor(0.9 x 22,940 + 0.1) = 20,646, trip 3's sales
        var uses = new List<uint>();

        var run = home.Start(new ResupplyOptions(Loot: true, Kept: () => [loot], AccountName: "test"), Table(ScrollSpell(ComponentOf(CopperScarab))));
        var status = home.Stations.Run(run, 600, pump: ScrollServer(home, teach: true, uses));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(20_646, run.Earned);
        Assert.Single(uses); // the scroll bought and read on this trip
        Assert.Equal(1, run.Learned);
        Assert.True(run.Visits[0].Sell, "the sale stop comes first");
        Assert.Empty(run.Visits[0].Buys);
        Assert.True(home.Logged("resupply: sell first (D123): the stock is planned after the sale, from the purse it leaves (now 729)"));
        Assert.True(home.Logged("resupply: sold first (D123): sold 1 item(s) for 20646; the purse now 21375"));
        Assert.False(home.Logged("waits: its scroll costs"));
    }

    [Fact]
    public void The_next_trip_buys_a_shopping_line_ahead_of_a_new_scroll()
    {
        // The stop line takes two Silver Scarabs (676), the shopping list four more to its six (1,352): 5,800 leaves 3,772, under the scroll's 5,000.
        var home = Climber(5_800, held: l => l.Wcid == SilverScarab ? 0 : l.Keep);
        var uses = new List<uint>();
        var silver = ComponentOf(SilverScarab);
        var memory = new SaleMemory();
        memory.ComponentShopping[SilverScarab] = new ComponentShoppingLine("Silver Scarab", 6, "an earlier trip's purse ran out");

        var run = home.Start(new ResupplyOptions(Memory: memory, AccountName: "test"), Table(ScrollSpell(silver, silver)));
        var status = home.Stations.Run(run, 600, pump: ScrollServer(home, teach: true, uses));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(run.BuyList.Single(b => b.Line.Wcid == SilverScarab).Count >= 6); // the line first; with the scroll waiting, the rest of the purse tops the stock line up to its keep
        Assert.DoesNotContain(run.BuyList, b => b.Line.Wcid == SpiritDrinkerScroll);
        Assert.Empty(uses);
        Assert.False(memory.ComponentShopping.ContainsKey(SilverScarab)); // the pack holds them: the line is off the list
        Assert.True(home.Logged("learn: components: shopping list: Silver Scarab 688: buy 6 at"));
        Assert.True(home.Logged("learn: components: shopping list: Silver Scarab 688: filled"));
        Assert.True(home.Logged("learn: Aura of Spirit Drinker Self IV (3256): waits: its scroll costs 5000 and this trip's budget"));
    }

    [Fact]
    public void M6_16_E5a2_a_scroll_the_budget_cannot_pay_is_held_on_what_the_counter_can_pay_with_not_the_coin_alone()
    {
        // The trip above with the same purse held as 800 coin and a 5,000 note: the scroll waits, and its hold is released by the coin and the notes together (the counters read the same number), so the threshold is
        // over this trip's whole purse, 5,800. Before the slice it was over the coin alone (801 or the budget's line), which the note's face never released and a sale's coin did though the purse had not grown past the trip's.
        var home = Climber(800, held: l => l.Wcid == SilverScarab ? 0 : l.Keep);
        home.Own("Trade Note (5,000)", 2624, Lifestone.Client.Session.Contracts.VendorStock.PromissoryNoteType, 5_000);
        home.Vendor.Tick();
        var silver = ComponentOf(SilverScarab);
        var memory = new SaleMemory();
        memory.ComponentShopping[SilverScarab] = new ComponentShoppingLine("Silver Scarab", 6, "an earlier trip's purse ran out");

        var run = home.Start(new ResupplyOptions(Memory: memory, AccountName: "test"), Table(ScrollSpell(silver, silver)));
        Assert.Equal(5_800, Lifestone.Client.Session.Quartermaster.PurseView.CounterCanPayUnreadAsZero(home.Vendor.State));
        var status = home.Stations.Run(run, 600, pump: ScrollServer(home, teach: true, []));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.DoesNotContain(run.BuyList, b => b.Line.Wcid == SpiritDrinkerScroll);
        var waits = Assert.Single(home.Lines, l => l.Contains("learn: Aura of Spirit Drinker Self IV (3256): waits: its scroll costs 5000 and this trip's budget", StringComparison.Ordinal));
        var percent = home.Ground.Supplies.Purse.TripSpendPercent;
        var threshold = Math.Max((int)Math.Ceiling(5_000 * 100.0 / percent), 5_801);
        Assert.Contains($"its trigger is held until the coin reaches {threshold} or the level passes ", waits, StringComparison.Ordinal); // the line's words as they were
        Assert.True(home.Logged($"learn: Aura of Spirit Drinker Self IV (3256): the trade notes held (5000) count with the coin toward the {threshold}: the hold is released by what the scrivener's counter can pay with, 5800 now"));
    }

    [Fact]
    public void A_shopping_line_no_vendor_of_the_trip_sells_stays_on_the_list_and_does_not_hold_the_scroll()
    {
        const uint unsold = 9_999_001; // a weenie no vendor of the ground sells
        var home = Climber(2_000_000, focus: true);
        var uses = new List<uint>();
        var memory = new SaleMemory();
        memory.ComponentShopping[unsold] = new ComponentShoppingLine("Unsold Herb", 2, "no vendor");

        var run = home.Start(new ResupplyOptions(Memory: memory, AccountName: "test"), Table(ScrollSpell(ComponentOf(CopperScarab))));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: ScrollServer(home, teach: true, uses)));

        Assert.Single(uses);
        Assert.True(memory.ComponentShopping.ContainsKey(unsold));
        Assert.DoesNotContain(run.BuyList, b => b.Line.Wcid == unsold);
        Assert.True(home.Logged("learn: components: shopping list: Unsold Herb 9999001: short 2 (held 0 of 2): no vendor of"));
    }

    [Fact]
    public void A_shopping_line_comes_off_once_no_spell_that_wanted_it_still_burns_it()
    {
        // The focus is held, so the spell reads the foci formula, which burns the scarab and tapers and no herb: the herb left over from the full formula is off the list.
        var home = Climber(2_000_000, focus: true);
        var spell = ScrollSpell(ComponentOf(CopperScarab), 63, HawthornComponent, 63);
        var herb = Wcid(HawthornComponent);
        var memory = new SaleMemory();
        memory.ComponentShopping[herb] = new ComponentShoppingLine("Hawthorn", 2, "no vendor", [spell.Id]);

        var run = home.Start(new ResupplyOptions(Memory: memory, AccountName: "test"), Table(spell));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: ScrollServer(home, teach: true, [])));

        Assert.False(memory.ComponentShopping.ContainsKey(herb));
        Assert.True(home.Logged($"learn: components: shopping list: Hawthorn {herb}: off the list"));

        static uint Wcid(uint component) => ComponentWeenies.TryGetByComponent(component, out var w) ? w.Wcid : throw new InvalidOperationException("component " + component);
    }

    [Fact]
    public void A_shopping_line_a_spell_still_burns_stays_on_the_list()
    {
        var home = Climber(2_000_000, focus: false);
        var spell = ScrollSpell(ComponentOf(SilverScarab), 63, HawthornComponent, 63, 34, 46, 63, 55);
        var memory = new SaleMemory();
        memory.FociRefused.Add(ItemFocus); // no focus: the full formula still burns what no vendor sells
        var unsold = ScrollSupply.Plan(spell, new Dictionary<uint, int>(), holdsFocus: false, "test", home.Ground).Unsold[0];
        memory.ComponentShopping[unsold.Wcid] = new ComponentShoppingLine(unsold.Name, unsold.Want, "no vendor", [spell.Id]);

        var run = home.Start(new ResupplyOptions(Memory: memory, AccountName: "test"), Table(spell));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: ScrollServer(home, teach: true, [])));

        Assert.True(memory.ComponentShopping.ContainsKey(unsold.Wcid));
        Assert.Contains(spell.Id, memory.ComponentShopping[unsold.Wcid].Spells!);
    }

    [Fact]
    public void The_required_step_buys_what_the_walks_list_left_short_straight_after_the_scrolls()
    {
        var home = Climber(2_000_000, held: l => l.Wcid == CopperScarab ? 0 : l.Keep);
        var uses = new List<uint>();
        var memory = new SaleMemory();
        memory.BoughtUnseen[CopperScarab] = 1_000; // the plan holds the line out once, so the walk's list buys none: the step after the scrolls must
        var spell = ScrollSpell(ComponentOf(CopperScarab));

        var run = home.Start(new ResupplyOptions(Memory: memory, AccountName: "test"), Table(spell));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: ScrollServer(home, teach: true, uses)));

        Assert.DoesNotContain(run.BuyList, b => b.Line.Wcid == CopperScarab);
        Assert.Single(uses);
        Assert.Empty(memory.ComponentShopping);
        Assert.True(home.Logged("learn: components: check before: short after the walk: Copper Scarab 686 x2: bought now, straight after the scrolls"));
        Assert.True(home.Logged("resupply: bought 2 Copper Scarab at"));
        Assert.True(home.Logged("learn: components: Aura of Spirit Drinker Self IV (3256): check after: the pack holds 2 passes of the foci formula"));
    }

    [Fact]
    public void A_focus_bought_on_the_same_trip_puts_the_scroll_on_the_foci_formula()
    {
        var home = Climber(2_000_000, focus: false); // the plan's Foci of Artifice is first in the list; without it the herb below would stop the scroll
        var spell = ScrollSpell(ComponentOf(SilverScarab), 63, HawthornComponent, 63, 34, 46, 63, 55);

        var run = home.Start(new ResupplyOptions(Memory: new SaleMemory(), AccountName: "test"), Table(spell));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: ScrollServer(home, teach: true, [])));

        Assert.Contains(run.BuyList, b => b.Line.Wcid == ItemFocus);
        Assert.Contains(run.BuyList, b => b.Line.Wcid == SpiritDrinkerScroll);
        Assert.True(home.Logged("learn: components: Aura of Spirit Drinker Self IV (3256): check before: the foci formula"));
    }

    [Fact]
    public void A_character_the_rule_does_not_bind_buys_the_scroll_as_before_with_no_components_counted()
    {
        var home = new Home(Den, 2_000_000, description: LearnMage(itemRanks: 200));
        var uses = new List<uint>();

        var run = home.Start(new ResupplyOptions(Memory: new SaleMemory()), Table(ScrollSpell(ComponentOf(SilverScarab))));
        Assert.Equal(ScenarioStatus.Succeeded, home.Stations.Run(run, 600, pump: ScrollServer(home, teach: true, uses)));

        Assert.Null(run.BuyList.Single(b => b.Line.Wcid == SpiritDrinkerScroll).Components);
        Assert.DoesNotContain(home.Lines, l => l.Contains("learn: components:", StringComparison.Ordinal));
    }

    [Fact]
    public void Coin_that_covers_the_scroll_but_not_its_components_buys_and_reads_the_scroll_and_lists_the_shortfall()
    {
        // The stop line takes two Silver Scarabs first (676): 5,800 leaves 5,124: the scroll's 5,000 fits and is bought, and the 124 left cannot pay the two more scarabs its two passes want.
        var home = Climber(5_800, held: l => l.Wcid == SilverScarab ? 0 : l.Keep);
        var uses = new List<uint>();
        var silver = ComponentOf(SilverScarab);
        var memory = new SaleMemory();

        var run = home.Start(new ResupplyOptions(Memory: memory, AccountName: "test"), Table(ScrollSpell(silver, silver)));
        var status = home.Stations.Run(run, 600, pump: ScrollServer(home, teach: true, uses));

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Contains(run.BuyList, b => b.Line.Wcid == SpiritDrinkerScroll);
        Assert.Single(uses);
        Assert.Equal(1, run.Learned);
        Assert.Equal(4, memory.ComponentShopping[SilverScarab].Want);
        Assert.True(home.Logged("learn: components: check after: TODO(m6-scroll-components-short): Silver Scarab 688 short 2 (held 2 of 4): the purse ran out"));
    }
}

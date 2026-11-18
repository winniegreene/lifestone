using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 2 c (m6-atlan-mana-recharge; m5-stone-short; the mage half of m4-gear-mana-reactivate): the held caster's own mana kept by rule. The setting is the wand of the Atlan chain, weenie 46128 Minor Smoldering Atlan Wand (M6 8 B2b, the pinned world SQL: <c>SELECT type, value FROM weenie_properties_int WHERE object_Id = 46128 AND type IN (107, 108)</c>: ItemCurMana 500, ItemMaxMana 500; <c>SELECT value FROM weenie_properties_float WHERE object_Id = 46128 AND type = 5</c>: ManaRate -0.025 a second, 20,000 s, about 5.6 h, the M6 spec's DoD 4 rounds it to 5.5 h); the Training Wand has no item mana. The wares are Archmage Cindrue's (wcid 2304): the Mana Charges 4612 to 4615 (100, 175, 350, 500 mana) and the empty Minor and Lesser Mana Stones (<c>cache/m6/2/sel-stones.tsv</c>).
/// </summary>
public sealed class CasterManaRuleTests
{
    private const uint Atlan = 0xA0000001;
    private const uint Training = 0xA0000002;
    private const uint SpareGuid = 0xA0000003;
    private const double AtlanRate = -0.025; // weenie 46128's ManaRate (float 5), the class comment's row

    private static readonly ManaOffer[] Cindrue =
    [
        new(2304, "Archmage Cindrue", 27331, 338), new(2304, "Archmage Cindrue", 2434, 675),
        new(2304, "Archmage Cindrue", 4612, 473), new(2304, "Archmage Cindrue", 4613, 878), new(2304, "Archmage Cindrue", 4614, 2025), new(2304, "Archmage Cindrue", 4615, 3375),
    ];

    private static readonly HeldStone HighCharge = new(30, "High Mana Charge", 500, 1);
    private static readonly HeldStone TinyCharge = new(31, "Tiny Mana Charge", 100, 1);
    private static readonly HeldStone EmptyStone = new(32, "Lesser Mana Stone", null, 0.25);

    private static WornMana AtlanAt(int mana) => new(Atlan, "Minor Smoldering Atlan Wand", mana, 500, AtlanRate);

    private static CasterManaInput Input(WornMana? held, bool dropped = false, IReadOnlyList<WornMana>? spares = null, IReadOnlyList<ManaOffer>? offers = null) =>
        new(held, dropped, spares ?? [], offers ?? Cindrue);

    [Fact]
    public void The_Atlan_wands_500_mana_at_its_rate_last_twenty_thousand_seconds()
    {
        Assert.Equal(20_000.0, AtlanAt(500).SecondsLeft, 6);
        var full = CasterManaRule.Decide(Input(AtlanAt(500)), [HighCharge]);

        Assert.Equal(CasterManaAct.None, full.Act);
        Assert.Equal(20_000.0 / 3600, full.Hours, 6);
        Assert.Contains("over the 2 h horizon", full.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refill_is_due_at_the_horizon_and_not_a_point_before()
    {
        // 7,200 s at 0.025 a second is 180 points: 181 last 7,240 s, 179 last 7,160 s
        Assert.Equal(CasterManaAct.None, CasterManaRule.Decide(Input(AtlanAt(181)), [HighCharge]).Act);
        var due = CasterManaRule.Decide(Input(AtlanAt(179)), [HighCharge]);

        Assert.Equal(CasterManaAct.RefillDue, due.Act);
        Assert.Equal(30u, due.Stone);
        Assert.Equal(Atlan, due.Caster);
    }

    [Fact]
    public void Well_before_the_horizon_nothing_is_due_whatever_is_held()
    {
        Assert.Equal(CasterManaAct.None, CasterManaRule.Decide(Input(AtlanAt(400)), []).Act); // 4.4 h left: nothing to buy either
        Assert.Equal(CasterManaAct.None, CasterManaRule.Decide(Input(AtlanAt(250)), [EmptyStone]).Act); // half of the maximum is 2.78 h: still over the horizon
    }

    [Fact]
    public void The_refill_uses_the_least_charged_stone_that_covers_the_deficit_else_the_fullest()
    {
        var covers = CasterManaRule.Decide(Input(AtlanAt(100)), [TinyCharge, HighCharge]); // the deficit is 400
        Assert.Equal(30u, covers.Stone);

        var fullest = CasterManaRule.Decide(Input(AtlanAt(100)), [TinyCharge, new HeldStone(33, "Small Mana Charge", 175, 1)]);
        Assert.Equal(33u, fullest.Stone); // none covers 400: the fullest
    }

    [Fact]
    public void An_empty_stone_is_no_charge_so_a_caster_due_with_only_empty_stones_wants_one()
    {
        var want = CasterManaRule.Decide(Input(AtlanAt(100)), [EmptyStone]); // the deficit is 400

        Assert.Equal(CasterManaAct.WantStone, want.Act);
        Assert.Equal(4615u, want.WantWcid); // the High Mana Charge, 500, is the smallest of the four that covers 400
    }

    [Fact]
    public void The_want_names_the_smallest_charge_that_covers_the_deficit_at_the_vendor_who_sells_it()
    {
        var want = CasterManaRule.Decide(Input(AtlanAt(160)), []); // 1.78 h left, the deficit is 340

        Assert.Equal(CasterManaAct.WantStone, want.Act);
        Assert.Equal(4614u, want.WantWcid); // Moderate Mana Charge: 350 covers 340
        Assert.Equal(2304u, want.WantVendor);
        Assert.Equal(1, want.WantCount);
        Assert.Null(want.Todo);
        Assert.Contains("want 1 x wcid 4614 (350 mana each, 340 needed) from Archmage Cindrue (2304) at 2025", want.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Out_of_mana_with_a_charged_spare_swaps_to_the_spare()
    {
        var spare = new WornMana(SpareGuid, "Spare Wand", 200, 200, -0.01);

        var swap = CasterManaRule.Decide(Input(AtlanAt(0), spares: [new WornMana(0x11, "Empty Wand", 0, 200, -0.01), spare]), [HighCharge]);

        Assert.Equal(CasterManaAct.SwapToSpare, swap.Act);
        Assert.Equal(SpareGuid, swap.Spare);
        Assert.Contains("'Spare Wand' holds 200/200", swap.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Out_of_mana_with_no_spare_uses_a_charged_stone_then_the_caster_is_wielded_again()
    {
        var refill = CasterManaRule.Decide(Input(AtlanAt(0)), [HighCharge]);
        Assert.Equal(CasterManaAct.RefillDue, refill.Act);
        Assert.Contains("then wield it again", refill.Reason, StringComparison.Ordinal);

        // the refill poured 500 in, ACE removed the spells at 0 (Player_Tick.cs:681-699) and a stone does not restore them: only the wield does (Player_Inventory.cs:313-315)
        var rewield = CasterManaRule.Decide(Input(AtlanAt(500), dropped: true), []);
        Assert.Equal(CasterManaAct.Rewield, rewield.Act);
        Assert.Equal(Atlan, rewield.Caster);
        Assert.Contains("Player_Inventory.cs:313-315", rewield.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Out_of_mana_with_nothing_charged_held_wants_a_charge_for_the_whole_maximum()
    {
        var want = CasterManaRule.Decide(Input(AtlanAt(0), spares: [new WornMana(0x11, "Empty Wand", 0, 200, -0.01)]), [EmptyStone]);

        Assert.Equal(CasterManaAct.WantStone, want.Act);
        Assert.Equal(4615u, want.WantWcid); // the High Mana Charge's 500 is the Atlan's maximum
        Assert.Equal(1, want.WantCount);
        Assert.Contains("out of mana, no charged spare and no charged stone is held", want.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_deficit_over_every_charge_on_offer_wants_as_many_of_the_largest_as_it_needs()
    {
        ManaOffer[] small = [new(2304, "Archmage Cindrue", 4612, 473), new(2304, "Archmage Cindrue", 4613, 878)];

        var want = CasterManaRule.Decide(Input(AtlanAt(0), offers: small), []);

        Assert.Equal(4613u, want.WantWcid);
        Assert.Equal(3, want.WantCount); // 500 needed, 175 each
    }

    [Fact]
    public void The_Training_Wand_has_no_item_mana_so_nothing_is_kept()
    {
        var training = new WornMana(Training, "Training Wand", 0, 0, 0);

        var none = CasterManaRule.Decide(Input(training, dropped: true), []);

        Assert.Equal(CasterManaAct.None, none.Act);
        Assert.Contains("no item mana", none.Reason, StringComparison.Ordinal);
        Assert.Equal(CasterManaAct.None, CasterManaRule.Decide(Input(null), [HighCharge]).Act);
    }

    [Fact]
    public void A_caster_with_a_maximum_but_no_rate_is_never_drained_so_it_is_left_alone()
    {
        var noRate = new WornMana(Atlan, "Odd Wand", 40, 500, 0);

        var none = CasterManaRule.Decide(Input(noRate), [HighCharge]);

        Assert.Equal(CasterManaAct.None, none.Act);
        Assert.Contains("no ManaRate", none.Reason, StringComparison.Ordinal);
        Assert.StartsWith("caster mana: Odd Wand 40/500 inf h -> None (", none.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_charge_on_offer_the_want_is_a_loud_named_TODO_and_names_no_ware()
    {
        ManaOffer[] stonesOnly = [new(2304, "Archmage Cindrue", 27331, 338), new(2304, "Archmage Cindrue", 2434, 675)]; // sold empty: no charge

        var want = CasterManaRule.Decide(Input(AtlanAt(100), offers: stonesOnly), []);

        Assert.Equal(CasterManaAct.WantStone, want.Act);
        Assert.Equal(0u, want.WantWcid);
        Assert.Equal("TODO(m6-no-stone-vendor)", want.Todo);
        Assert.Contains("TODO(m6-no-stone-vendor): no profile vendor sells a Mana Charge", want.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_decision_prints_as_the_caster_mana_log_line()
    {
        var line = CasterManaRule.Decide(Input(AtlanAt(179)), [HighCharge]).ToString();

        Assert.Equal("caster mana: Minor Smoldering Atlan Wand 179/500 1.99h -> RefillDue (1.99 h left, under the 2 h horizon; the stone 'High Mana Charge' (500) on the player)", line);
    }
}

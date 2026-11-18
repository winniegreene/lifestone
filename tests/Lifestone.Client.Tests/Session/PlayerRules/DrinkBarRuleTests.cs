using Lifestone.Client.Session.PlayerRules;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.PlayerRules;

/// <summary>
/// The player's rule for drinks as a pure function (M6 item 15): the listed kinds and their slots, the slot a kind with no shortcut goes on (the one it was last on, its own, the first free one to the right, none on a full
/// bar), and the slot that holds a kind.
/// </summary>
public sealed class DrinkBarRuleTests
{
    [Fact]
    public void Every_drink_the_automation_uses_has_its_own_slot_on_the_row_the_player_sees()
    {
        uint[] wanted = [VitalsMeans.HealthDraughtWcid, 377, DrinkBarRule.HealthTinctureWcid, 379, VitalsMeans.ManaDraughtWcid, DrinkBarRule.ManaTinctureWcid, 378, DrinkBarRule.StaminaTinctureWcid];
        Assert.Equal(wanted, DrinkBarRule.Kinds.Select(k => k.Wcid));
        Assert.Equal<uint>([2457, 377, 27319, 379, 2460, 27322, 378, 27326], wanted); // the weenie class ids, as the order names the tinctures and the draught
        Assert.Equal(Enumerable.Range(0, 8), DrinkBarRule.Kinds.Select(k => k.Slot)); // the keys 1 to 8, each kind its own
        Assert.All(DrinkBarRule.Kinds, k => Assert.Equal(k.Slot, DrinkBarRule.PreferredSlot(k.Wcid)));
        Assert.All(DrinkBarRule.Kinds, k => Assert.InRange(k.Slot, 0, Lifestone.Client.RetailUi.Toolbar.GameHotbar.VisibleSlots - 1));
        Assert.Equal(-1, DrinkBarRule.PreferredSlot(628)); // a healing kit is no drink
        Assert.Equal(["Health Draught", "Potion of Healing", "Health Tincture", "Mana Potion", "Mana Draught", "Mana Tincture", "Stamina Potion", "Stamina Tincture"], DrinkBarRule.Kinds.Select(k => k.Name));
    }

    [Fact]
    public void A_kind_goes_on_its_own_slot_when_that_is_free()
    {
        var rig = new ShortcutRig();
        Assert.Equal(1, DrinkBarRule.SlotFor(rig.Bar, 377, remembered: -1));
        Assert.Equal(3, DrinkBarRule.SlotFor(rig.Bar, 379, remembered: -1));
        Assert.Equal(7, DrinkBarRule.SlotFor(rig.Bar, DrinkBarRule.StaminaTinctureWcid, remembered: -1));
    }

    [Fact]
    public void A_slot_something_else_holds_is_never_taken_the_kind_goes_to_the_first_free_slot_on_its_right()
    {
        var rig = new ShortcutRig((1, ShortcutRig.Kit), (2, ShortcutRig.Dagger));
        Assert.Equal(3, DrinkBarRule.SlotFor(rig.Bar, 377, remembered: -1)); // 1 and 2 are the person's: the potion goes on 3
        Assert.Equal(4, DrinkBarRule.SlotFor(rig.Bar, 377, remembered: -1, reserved: new HashSet<int> { 3 })); // and not on a slot another add waits for
    }

    [Fact]
    public void A_kind_that_ran_out_goes_back_to_the_slot_it_was_last_on()
    {
        var rig = new ShortcutRig();
        Assert.Equal(11, DrinkBarRule.SlotFor(rig.Bar, 377, remembered: 11)); // the slot the session remembers wins over the table's
        var taken = new ShortcutRig((11, ShortcutRig.Dagger));
        Assert.Equal(1, DrinkBarRule.SlotFor(taken.Bar, 377, remembered: 11)); // unless something else holds it now
    }

    [Fact]
    public void A_drink_the_table_does_not_list_goes_after_the_listed_slots_and_a_full_bar_is_no_slot()
    {
        var rig = new ShortcutRig();
        Assert.Equal(8, DrinkBarRule.SlotFor(rig.Bar, 5003, remembered: -1));

        var full = new ShortcutRig([.. Enumerable.Range(0, 18).Select(i => ((uint)i, 0x80000C00 + (uint)i))]);
        Assert.Equal(-1, DrinkBarRule.SlotFor(full.Bar, 377, remembered: -1));
        Assert.Equal(-1, DrinkBarRule.SlotFor(full.Bar, 5003, remembered: 4));
    }

    [Fact]
    public void The_slot_of_a_kind_is_the_first_slot_whose_held_item_is_of_that_kind()
    {
        var rig = new ShortcutRig((5, ShortcutRig.Potion), (6, ShortcutRig.Kit), (7, 0x80000C55)); // slot 7 names an object the player does not hold
        var state = rig.R.H.State;
        Assert.Equal(5, DrinkBarRule.SlotOfKind(state, 377));
        Assert.Equal(-1, DrinkBarRule.SlotOfKind(state, 379));
        Assert.Equal(377u, DrinkBarRule.KindOf(state, ShortcutRig.Potion));
        Assert.Equal(0u, DrinkBarRule.KindOf(state, 0x80000C55));
        Assert.Equal(ShortcutRig.Potion, DrinkBarRule.FirstHeld(state, 377));
        Assert.Equal(0u, DrinkBarRule.FirstHeld(state, 379));
        Assert.Equal("Potion of Healing", DrinkBarRule.NameOf(state, ShortcutRig.Potion));
        Assert.Equal("item 0x80000C55", DrinkBarRule.NameOf(state, 0x80000C55));
    }
}

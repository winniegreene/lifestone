using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 3.2 h1: <c>AddShortcut</c> (GameAction 0x019C), <c>RemoveShortcut</c> (0x019D) and <c>UseShortcut</c> over the rules of <c>gmToolbarUI</c> (11.6096): an item already on the bar moves (<c>AddShortcut</c> 0x004BE630 after
/// <c>RemoveShortcut</c> 0x004BE0E0), a slot that is occupied hands its occupant to the first empty slot to the right (<c>GetFirstEmptyShortcutToTheRightOf</c> 0x004BE1F0), what is not held and a pyreal stack are refused
/// (<c>IsShortcutEligible</c> 0x004BDE60, <c>IsCoinstack</c> 0x004BDEE0). ACE answers nothing, so every add and remove completes as Sent with the model changed as it sends; a use routes a kit, a potion and anything else
/// (<c>UseShortcut</c> 0x004BDFE0). The wire sequence is read as one string: A = AddShortcut slot:object, R = RemoveShortcut slot.
/// </summary>
public sealed class ShortcutActionTests
{
    // ---- AddShortcut ----

    [Fact]
    public void Adding_an_item_to_an_empty_slot_sends_0x019C_and_fills_the_slot_at_once()
    {
        var rig = new ShortcutRig();
        var handle = rig.Api.AddShortcut(3, ShortcutRig.Kit, ActionSource.Ui);
        var outcome = rig.Done(handle);

        Assert.Equal(ActionOutcomeKind.Sent, outcome.Kind);
        Assert.True(outcome.Succeeded);
        Assert.Equal(ActionKind.AddShortcut, handle.Kind);
        var sent = Assert.Single(rig.Adds);
        Assert.Equal((3, ShortcutRig.Kit, 0u), (sent.Index, sent.Guid, sent.Amount));
        Assert.Equal(ShortcutRig.Kit, rig.Bar[3].ObjectId);
        Assert.Equal(new ShortcutsChanged(ShortcutChange.Added, 3, ShortcutRig.Kit, 0), rig.Changes[^1]);
        Assert.Empty(rig.Removes);
    }

    [Fact]
    public void An_item_already_on_the_bar_moves_with_its_old_slot_removed_first()
    {
        var rig = new ShortcutRig((0, ShortcutRig.Kit));
        var outcome = rig.Done(rig.Api.AddShortcut(5, ShortcutRig.Kit, ActionSource.Ui));

        Assert.Equal(ActionOutcomeKind.Sent, outcome.Kind);
        Assert.Contains("moved from slot 0", outcome.Detail, StringComparison.Ordinal);
        Assert.Equal("R0,A5:kit", rig.Wire());
        Assert.True(rig.Bar[0].IsEmpty);
        Assert.Equal(ShortcutRig.Kit, rig.Bar[5].ObjectId);
        Assert.Equal(1, rig.Bar.Count);
    }

    [Fact]
    public void An_occupied_slot_hands_its_occupant_to_the_first_empty_slot_to_the_right()
    {
        var rig = new ShortcutRig((0, ShortcutRig.Kit), (1, ShortcutRig.Potion));
        var outcome = rig.Done(rig.Api.AddShortcut(0, ShortcutRig.Dagger, ActionSource.Ui));

        Assert.Equal(ActionOutcomeKind.Sent, outcome.Kind);
        Assert.Contains("the occupant moved to slot 2", outcome.Detail, StringComparison.Ordinal);
        Assert.Equal("R0,A0:dagger,A2:kit", rig.Wire());
        Assert.Equal(ShortcutRig.Dagger, rig.Bar[0].ObjectId);
        Assert.Equal(ShortcutRig.Potion, rig.Bar[1].ObjectId);
        Assert.Equal(ShortcutRig.Kit, rig.Bar[2].ObjectId);
    }

    [Fact]
    public void A_move_onto_a_neighbour_takes_the_slot_it_just_left_for_the_occupant()
    {
        var rig = new ShortcutRig((16, ShortcutRig.Potion), (17, ShortcutRig.Kit));
        rig.Done(rig.Api.AddShortcut(16, ShortcutRig.Kit, ActionSource.Ui));

        // The kit leaves 17, the potion on 16 is cleared, the kit lands on 16, and the potion goes to the first empty slot to the right: the 17 the kit left.
        Assert.Equal("R17,R16,A16:kit,A17:potion", rig.Wire());
        Assert.Equal(ShortcutRig.Kit, rig.Bar[16].ObjectId);
        Assert.Equal(ShortcutRig.Potion, rig.Bar[17].ObjectId);
    }

    [Fact]
    public void The_search_to_the_right_wraps_to_the_start_of_the_bar()
    {
        var rig = new ShortcutRig((17, ShortcutRig.Kit));
        rig.Done(rig.Api.AddShortcut(17, ShortcutRig.Dagger, ActionSource.Ui));

        Assert.Equal("R17,A17:dagger,A0:kit", rig.Wire());
        Assert.Equal(ShortcutRig.Kit, rig.Bar[0].ObjectId);
    }

    [Fact]
    public void With_no_free_slot_the_occupant_leaves_the_bar()
    {
        // 17 rows of objects the player does not hold, and the kit on the last slot.
        var rows = Enumerable.Range(0, 17).Select(i => ((uint)i, 0x80009000u + (uint)i)).Append((17u, ShortcutRig.Kit)).ToArray();
        var rig = new ShortcutRig(rows);
        var outcome = rig.Done(rig.Api.AddShortcut(0, ShortcutRig.Dagger, ActionSource.Ui));

        Assert.Equal(ActionOutcomeKind.Sent, outcome.Kind);
        Assert.Contains("the occupant left the bar", outcome.Detail, StringComparison.Ordinal);
        Assert.Equal("R0,A0:dagger", rig.Wire());
        Assert.Equal(ShortcutRig.Dagger, rig.Bar[0].ObjectId);
        Assert.Equal(-1, rig.Bar.SlotOf(0x80009000));
        Assert.Equal(18, rig.Bar.Count);
    }

    [Fact]
    public void The_item_already_on_the_slot_changes_nothing_and_sends_nothing()
    {
        var rig = new ShortcutRig((4, ShortcutRig.Potion));
        var outcome = rig.Done(rig.Api.AddShortcut(4, ShortcutRig.Potion, ActionSource.Ui));

        Assert.Equal(ActionOutcomeKind.Sent, outcome.Kind);
        Assert.Contains("already", outcome.Detail, StringComparison.Ordinal);
        Assert.Equal(string.Empty, rig.Wire());
    }

    [Fact]
    public void The_automatic_slot_is_the_first_empty_one_and_is_refused_when_the_item_is_on_the_bar_or_the_bar_is_full()
    {
        var rig = new ShortcutRig((0, ShortcutRig.Kit), (1, ShortcutRig.Potion));
        rig.Done(rig.Api.AddShortcut(ShortcutBar.AutoSlot, ShortcutRig.Dagger, ActionSource.Ui));
        Assert.Equal("A2:dagger", rig.Wire());

        var twice = rig.Done(rig.Api.AddShortcut(ShortcutBar.AutoSlot, ShortcutRig.Dagger, ActionSource.Ui));
        Assert.Equal(ActionOutcomeKind.Refused, twice.Kind);
        Assert.Contains("already a shortcut", twice.Detail, StringComparison.Ordinal);

        var rows = Enumerable.Range(0, 18).Select(i => ((uint)i, 0x80009000u + (uint)i)).ToArray();
        var full = new ShortcutRig(rows);
        var none = full.Done(full.Api.AddShortcut(ShortcutBar.AutoSlot, ShortcutRig.Kit, ActionSource.Ui));
        Assert.Equal(ActionOutcomeKind.Refused, none.Kind);
        Assert.Contains("no free shortcut slots", none.Detail, StringComparison.Ordinal);
        Assert.Equal(string.Empty, full.Wire());
    }

    [Theory]
    [InlineData(0u, 3, "no item")]
    [InlineData(ShortcutRig.Kit, 18, "0 to 17")]
    [InlineData(ShortcutRig.Kit, -2, "0 to 17")]
    [InlineData(ShortcutRig.Bow, 3, "not carrying")]
    [InlineData(ShortcutRig.Pyreals, 3, "pyreals")]
    public void An_ineligible_add_is_a_named_refusal_that_sends_nothing(uint item, int slot, string reason)
    {
        var rig = new ShortcutRig();
        var outcome = rig.Done(rig.Api.AddShortcut(slot, item, ActionSource.Ui));

        Assert.Equal(ActionOutcomeKind.Refused, outcome.Kind);
        Assert.False(outcome.Succeeded);
        Assert.Contains(reason, outcome.Detail, StringComparison.Ordinal);
        Assert.Equal(string.Empty, rig.Wire());
        Assert.Equal(0, rig.Bar.Count);
    }

    [Fact]
    public void The_part_of_eligibility_retail_does_not_name_is_logged_once_per_session()
    {
        var rig = new ShortcutRig();
        rig.Done(rig.Api.AddShortcut(0, ShortcutRig.Kit, ActionSource.Ui));
        rig.Done(rig.Api.AddShortcut(1, ShortcutRig.Potion, ActionSource.Ui));

        Assert.Single(rig.R.H.Log.Lines, l => l.Contains("TODO(m4-shortcut-eligible-flags)", StringComparison.Ordinal));
    }

    // ---- RemoveShortcut ----

    [Fact]
    public void Removing_a_slot_sends_0x019D_and_empties_it_at_once()
    {
        var rig = new ShortcutRig((3, ShortcutRig.Kit), (4, ShortcutRig.Potion));
        var handle = rig.Api.RemoveShortcut(3, ActionSource.Ui);
        var outcome = rig.Done(handle);

        Assert.Equal(ActionOutcomeKind.Sent, outcome.Kind);
        Assert.Equal(ActionKind.RemoveShortcut, handle.Kind);
        Assert.Equal(3, Assert.Single(rig.Removes).Index);
        Assert.True(rig.Bar[3].IsEmpty);
        Assert.Equal(ShortcutRig.Potion, rig.Bar[4].ObjectId);
        Assert.Equal(new ShortcutsChanged(ShortcutChange.Removed, 3, ShortcutRig.Kit, 0), rig.Changes[^1]);
    }

    [Theory]
    [InlineData(7, "nothing is on")]
    [InlineData(18, "0 to 17")]
    [InlineData(-1, "0 to 17")]
    public void Removing_an_empty_or_unknown_slot_is_a_named_refusal_that_sends_nothing(int slot, string reason)
    {
        var rig = new ShortcutRig((3, ShortcutRig.Kit));
        var outcome = rig.Done(rig.Api.RemoveShortcut(slot, ActionSource.Ui));

        Assert.Equal(ActionOutcomeKind.Refused, outcome.Kind);
        Assert.Contains(reason, outcome.Detail, StringComparison.Ordinal);
        Assert.Empty(rig.Removes);
        Assert.Equal(1, rig.Bar.Count);
    }

    // ---- UseShortcut ----

    [Fact]
    public void A_healing_kit_on_a_slot_is_used_on_the_player_with_UseWithTarget()
    {
        var rig = new ShortcutRig((0, ShortcutRig.Kit));
        var handle = rig.Api.UseShortcut(0, ActionSource.Ui);
        rig.R.Tick();

        Assert.IsType<HealHandle>(handle);
        Assert.Equal(ActionKind.UseWithTarget, handle.Kind);
        var sent = Assert.Single(rig.R.H.Net.Of(FakeSendKind.UseWithTarget));
        Assert.Equal(ShortcutRig.Kit, sent.Guid);
        Assert.Equal(ShortcutRig.Player.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), sent.Target);
        Assert.Empty(rig.R.H.Net.Of(FakeSendKind.Use));
    }

    [Fact]
    public void A_potion_on_a_slot_is_drunk_and_the_handle_is_the_drinks()
    {
        var rig = new ShortcutRig((1, ShortcutRig.Potion));
        var handle = rig.Api.UseShortcut(1, ActionSource.Ui);
        rig.R.Tick();

        Assert.IsType<HealHandle>(handle);
        Assert.Equal(ActionKind.Drink, handle.Kind);
        Assert.Equal(ShortcutRig.Potion, Assert.Single(rig.R.H.Net.Of(FakeSendKind.Use)).Guid);
        Assert.Empty(rig.R.H.Net.Of(FakeSendKind.UseWithTarget));
    }

    [Theory]
    [InlineData(ShortcutRig.Scroll)]
    [InlineData(ShortcutRig.Other)]
    public void Any_other_item_on_a_slot_is_used_with_plain_Use(uint item)
    {
        var rig = new ShortcutRig((2, item));
        var handle = rig.Api.UseShortcut(2, ActionSource.Ui);
        rig.R.Tick();

        Assert.Equal(ActionKind.Use, handle.Kind);
        Assert.Equal(item, Assert.Single(rig.R.H.Net.Of(FakeSendKind.Use)).Guid);
    }

    [Fact]
    public void The_use_s_outcome_is_the_underlying_actions_own()
    {
        var rig = new ShortcutRig((2, ShortcutRig.Dagger));
        var handle = rig.Api.UseShortcut(2, ActionSource.Ui);
        rig.R.Tick();
        Assert.Null(handle.Outcome);

        rig.R.UseDone();
        rig.R.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(18)]
    [InlineData(-1)]
    public void An_empty_or_unknown_slot_is_refused_at_once_with_no_send(int slot)
    {
        var rig = new ShortcutRig((2, ShortcutRig.Dagger));
        var handle = rig.Api.UseShortcut(slot, ActionSource.Ui);

        Assert.Equal(ActionKind.UseShortcut, handle.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, handle.Outcome!.Value.Kind);
        rig.R.Tick();
        Assert.Empty(rig.R.H.Net.Of(FakeSendKind.Use));
        Assert.Empty(rig.R.H.Net.Of(FakeSendKind.UseWithTarget));
    }

    [Fact]
    public void A_slot_whose_item_the_player_does_not_hold_is_refused_at_once()
    {
        var rig = new ShortcutRig((6, 0x80009999));
        var handle = rig.Api.UseShortcut(6, ActionSource.Ui);

        Assert.Equal(ActionKind.UseShortcut, handle.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, handle.Outcome!.Value.Kind);
        Assert.Contains("not carried", handle.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.True(rig.Bar.IsMissing(6));
        rig.R.Tick();
        Assert.Empty(rig.R.H.Net.Of(FakeSendKind.Use));
    }

    [Fact]
    public void A_spell_shortcut_answers_not_built_with_its_named_TODO()
    {
        var rig = new ShortcutRig();
        rig.R.H.Describe(new PlayerDescriptionMsg { Shortcuts = [new ShortcutDesc(8, 0, 0x0001_0035)] });
        var handle = rig.Api.UseShortcut(8, ActionSource.Ui);

        Assert.Equal(ActionOutcomeKind.NotImplemented, handle.Outcome!.Value.Kind);
        Assert.Contains("TODO(m4-shortcut-spell)", handle.Outcome.Value.Detail, StringComparison.Ordinal);
        rig.R.Tick();
        Assert.Empty(rig.R.H.Net.Of(FakeSendKind.Use));
    }

    [Theory]
    [InlineData(628u, "Handy Healing Kit", 0x80u, "Kit")]
    [InlineData(629u, "Adept Healing Kit", 0x80u, "Kit")]
    [InlineData(9999u, "Master Healing Kit", 0x80u, "Kit")]
    [InlineData(377u, "Potion of Healing", 0x80u, "Drink")]
    [InlineData(2457u, "Health Draught", 0x80u, "Drink")]
    [InlineData(9998u, "Strange Brew", 0x20u, "Drink")]
    [InlineData(9997u, "Potion of Rare Insight", 0x80u, "Drink")]
    [InlineData(5001u, "Rusty Dagger", 0x1u, "Use")]
    public void The_routing_tells_a_kit_a_potion_and_the_rest_by_class_item_type_and_name(uint wcid, string name, uint itemType, string expected)
    {
        var rig = new ShortcutRig();
        rig.Item(0x80000C01, wcid, name, itemType);
        Assert.True(rig.R.H.State.Objects.TryGet(0x80000C01, out var o));
        Assert.Equal(expected, ShortcutRouting.Of(o).ToString());
    }
}

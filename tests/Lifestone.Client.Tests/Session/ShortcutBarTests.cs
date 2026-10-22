using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 3.2 h1: the shortcut bar read model (<see cref="ShortcutBar"/>): 18 slots (<c>gmToolbarUI::InitShortcutArray</c>, 11.6096 0x004BE9E0) filled from the description's list at login
/// (<c>gmToolbarUI::UpdateFromPlayerDesc</c>, 0x004C04A0), an object the player does not hold stays and reads missing, and the model is cleared when the player leaves the world.
/// </summary>
public sealed class ShortcutBarTests
{
    [Fact]
    public void The_bar_has_18_empty_slots_and_is_unknown_until_the_description_arrives()
    {
        var r = new ReducerHarness();
        var bar = r.State.Self.Shortcuts;

        Assert.Equal(18, ShortcutBar.SlotCount);
        Assert.Equal(18, bar.Slots.Length);
        Assert.All(bar.Slots, s => Assert.True(s.IsEmpty));
        Assert.False(bar.IsKnown);
        Assert.Equal(0, bar.Count);
        Assert.Equal(0, bar.FirstEmpty());
    }

    [Fact]
    public void The_description_s_list_fills_the_slots_by_index_and_publishes_one_event()
    {
        var rig = new ShortcutRig((0, ShortcutRig.Kit), (1, ShortcutRig.Potion), (17, ShortcutRig.Dagger));
        var bar = rig.Bar;

        Assert.True(bar.IsKnown);
        Assert.Equal(ShortcutRig.Kit, bar[0].ObjectId);
        Assert.Equal(ShortcutRig.Potion, bar[1].ObjectId);
        Assert.Equal(ShortcutRig.Dagger, bar[17].ObjectId);
        Assert.Equal(3, bar.Count);
        Assert.Equal(17, bar.SlotOf(ShortcutRig.Dagger));
        Assert.Equal(-1, bar.SlotOf(ShortcutRig.Bow));
        Assert.Equal(2, bar.FirstEmpty());
        var changed = Assert.Single(rig.Changes);
        Assert.Equal(new ShortcutsChanged(ShortcutChange.Description, -1, 0, 0), changed);
    }

    [Fact]
    public void A_row_for_an_object_the_player_does_not_hold_stays_and_reads_missing()
    {
        var rig = new ShortcutRig((0, ShortcutRig.Kit), (3, 0x80009999));
        var bar = rig.Bar;

        Assert.Equal(0x80009999u, bar[3].ObjectId);
        Assert.False(bar.IsMissing(0));
        Assert.True(bar.IsMissing(3));
        Assert.False(bar.IsMissing(4)); // empty
        Assert.False(bar.IsMissing(99));
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("TODO(m4-shortcut-missing-at-login)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_row_past_the_18_slots_is_ignored_and_logged_and_a_slot_named_twice_keeps_the_last()
    {
        var rig = new ShortcutRig((2, ShortcutRig.Kit), (2, ShortcutRig.Potion), (18, ShortcutRig.Dagger), (400, ShortcutRig.Other));

        Assert.Equal(ShortcutRig.Potion, rig.Bar[2].ObjectId);
        Assert.Equal(1, rig.Bar.Count);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("names a slot the bar does not have", StringComparison.Ordinal));
    }

    [Fact]
    public void A_spell_only_row_is_kept_as_a_spell_and_never_reads_missing()
    {
        var r = new ReducerHarness();
        r.Reducer.OnPlayerDescription(r.Context, new GameEventHeader(ReducerHarness.PlayerGuid, 1, GameEventTypes.PlayerDescription), new PlayerDescriptionMsg { Shortcuts = [new ShortcutDesc(4, 0, 0x0001_0035)] });

        var slot = r.State.Self.Shortcuts[4];
        Assert.True(slot.IsSpell);
        Assert.False(slot.IsItem);
        Assert.Equal((ushort)0x35, slot.Spell);
        Assert.False(r.State.Self.Shortcuts.IsMissing(4));
    }

    [Fact]
    public void A_second_description_replaces_the_bar_and_leaving_the_world_clears_it()
    {
        var rig = new ShortcutRig((0, ShortcutRig.Kit));
        rig.R.H.Describe(new PlayerDescriptionMsg { Shortcuts = [new ShortcutDesc(5, ShortcutRig.Potion, 0)] });

        Assert.True(rig.Bar[0].IsEmpty);
        Assert.Equal(ShortcutRig.Potion, rig.Bar[5].ObjectId);

        rig.R.H.R.Reducer.OnSessionEvent(Lifestone.Net.Session.SessionEvent.LoggedOff(rig.R.H.R.Time.GetTimestamp(), Lifestone.Net.Session.SessionPhase.CharacterSelect));
        Assert.Equal(0, rig.Bar.Count);
        Assert.False(rig.Bar.IsKnown);
    }

    [Fact]
    public void The_first_empty_slot_to_the_right_wraps_and_ends_at_the_slot_itself()
    {
        var rig = new ShortcutRig((0, ShortcutRig.Kit), (1, ShortcutRig.Potion), (2, ShortcutRig.Dagger), (3, ShortcutRig.Pyreals));
        var bar = rig.Bar;

        Assert.Equal(4, bar.FirstEmptyToTheRightOf(1)); // 0x004BE1F0: from the next slot on
        Assert.Equal(4, bar.FirstEmptyToTheRightOf(17)); // wraps to the start, 0 to 3 are taken, so 4
        Assert.Equal(4, bar.FirstEmptyToTheRightOf(-1));

        rig.R.H.Describe(new PlayerDescriptionMsg { Shortcuts = [.. Enumerable.Range(0, 18).Select(i => new ShortcutDesc((uint)i, ShortcutRig.Kit + (uint)i, 0))] });
        Assert.Equal(-1, rig.Bar.FirstEmptyToTheRightOf(7));
        Assert.Equal(-1, rig.Bar.FirstEmpty());
    }
}

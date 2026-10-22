using System.Collections.Immutable;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 3.2 s3: the shortcut sends (capture.md C4): AddShortCut 0x019C and RemoveShortCut 0x019D are answered with nothing, and the stand-in's recorded description carries the list afterwards (a later description in the same visit, or a replayed
/// login), listed with spell 0 as ACE's relog showed.
/// </summary>
public sealed class StandInShortcutTests
{
    private static PlayerDescriptionMsg Base() => new() { Inventory = [] };

    [Fact]
    public void An_add_changes_the_list_and_answers_nothing_and_a_description_sent_after_it_carries_the_entries_by_index()
    {
        using var rig = new StandInVendorRig();
        rig.Fresh();

        rig.Net.SendAddShortcut(1, 0x80000AF4, 0);
        rig.Net.SendAddShortcut(0, 0x80001097, 0);
        rig.Run(3.0);

        Assert.Empty(rig.Tags);
        Assert.Empty(rig.Done);
        Assert.Equal([new ShortcutDesc(0, 0x80001097, 0), new ShortcutDesc(1, 0x80000AF4, 0)], rig.World.Shortcuts.ToArray());
        Assert.Equal([new ShortcutDesc(0, 0x80001097, 0), new ShortcutDesc(1, 0x80000AF4, 0)], rig.World.WithVendorState(Base()).Shortcuts.ToArray());
    }

    [Fact]
    public void An_add_at_an_index_in_use_replaces_it_and_a_remove_takes_an_entry_out_and_a_remove_of_an_empty_slot_is_nothing()
    {
        using var rig = new StandInVendorRig();
        rig.Net.SendAddShortcut(0, 0x80001097, 0);
        rig.Net.SendAddShortcut(0, 0x80000AF4, 0);
        rig.Net.SendAddShortcut(5, 0x80000BB1, 0);
        rig.Net.SendRemoveShortcut(5);
        rig.Net.SendRemoveShortcut(9);
        rig.Run(2.0);

        Assert.Equal([new ShortcutDesc(0, 0x80000AF4, 0)], rig.World.WithVendorState(Base()).Shortcuts.ToArray());
        Assert.Empty(rig.Tags);
        rig.Net.SendRemoveShortcut(0);
        Assert.Empty(rig.World.WithVendorState(Base()).Shortcuts.ToArray());
    }

    [Fact]
    public void A_spell_word_is_dropped_as_ACE_drops_it_and_logged_once()
    {
        using var rig = new StandInVendorRig();

        rig.Net.SendAddShortcut(2, 0x80000AF4, 0x00010064);

        Assert.Equal([new ShortcutDesc(2, 0x80000AF4, 0)], rig.World.Shortcuts.ToArray());
        Assert.Contains("TODO(m4-standin-shortcut-spell)", string.Join('\n', rig.Cast.Log.Entries.Select(e => e.Line)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_recorded_description_with_a_bar_is_the_list_until_the_first_change_then_the_stand_ins_list_replaces_it()
    {
        var recorded = RecordedDescription.Of("x.lsrec", new PlayerDescriptionMsg { Inventory = [], Shortcuts = [new ShortcutDesc(5, 0xAAA, 0), new ShortcutDesc(6, 0xBBB, 0)] }, 0x50000077, []);
        using var rig = new StandInVendorRig(recorded);

        Assert.Equal([new ShortcutDesc(5, 0xAAA, 0), new ShortcutDesc(6, 0xBBB, 0)], rig.World.Shortcuts.ToArray());
        rig.Net.SendRemoveShortcut(5);
        rig.Net.SendAddShortcut(2, 0xCCC, 0);

        Assert.Equal([new ShortcutDesc(2, 0xCCC, 0), new ShortcutDesc(6, 0xBBB, 0)], rig.World.WithVendorState(Base()).Shortcuts.ToArray());
    }

    [Fact]
    public void A_description_that_the_stand_in_has_not_changed_keeps_the_list_it_was_given()
    {
        var list = new StandInShortcutList();
        var given = new PlayerDescriptionMsg { Inventory = [], Shortcuts = ImmutableArray.Create(new ShortcutDesc(3, 0xDDD, 0)) };

        Assert.Same(given, list.Apply(given));
        Assert.False(list.Changed);
    }
}

using Lifestone.Client.Session.Contracts;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 3.2 h1, OpenAC #219: an item that leaves the player's holdings leaves the shortcut bar, and RemoveShortcut (GameAction 0x019D) is sent once for its slot, because ACE never drops the row itself
/// (<c>Player_Character.cs</c> 245-262) and the row came back at the next login naming an item the player no longer had. Retail's <c>gmToolbarUI::RecvNotice_ServerSaysMoveItem</c> (11.6096 0x004BFF40) removes the slot with the
/// broadcast on. Sold or given (GameMessage 0x0024, or GameEvent 0x0022 into a container that is not the player's), dropped (0x019A), used up (0x0024 after a use) and deleted (F747) leave; a move inside the player's own packs
/// (0x0022 into the main pack or a side pack) and a wield keep the row.
/// </summary>
public sealed class ShortcutInventoryTests
{
    private const uint Chest = 0x70000001;

    [Fact]
    public void An_item_removed_by_0024_leaves_the_bar_and_RemoveShortcut_is_sent_once()
    {
        var rig = new ShortcutRig((0, ShortcutRig.Kit), (1, ShortcutRig.Potion));
        rig.Gone(ShortcutRig.Potion); // sold (ACE's TryRemoveFromInventoryWithNetworking)

        Assert.True(rig.Bar[1].IsEmpty);
        Assert.Equal(ShortcutRig.Kit, rig.Bar[0].ObjectId);
        Assert.Equal(new ShortcutsChanged(ShortcutChange.ItemLeft, 1, ShortcutRig.Potion, 0), rig.Changes[^1]);
        Assert.Empty(rig.Removes); // sent by the next update, not inside the message

        rig.R.Tick();
        rig.R.Tick();
        Assert.Equal(1, Assert.Single(rig.Removes).Index);
        Assert.Empty(rig.Adds);
    }

    [Fact]
    public void An_eviction_and_an_add_for_the_same_slot_in_one_update_send_019D_before_019C()
    {
        // 3.2-R2: the last potion on slot 0 is drunk (0024) and the same frame adds the kit to slot 0; the removal must not follow the add and delete the new row on the server.
        var rig = new ShortcutRig((0, ShortcutRig.Potion));
        rig.Gone(ShortcutRig.Potion);
        var add = rig.Api.AddShortcut(0, ShortcutRig.Kit, ActionSource.Ui);
        rig.R.Tick();
        rig.R.Tick();

        Assert.Equal("R0,A0:kit", rig.Wire());
        Assert.Equal(ShortcutRig.Kit, rig.Bar[0].ObjectId);
        Assert.NotNull(add.Outcome);
    }

    [Fact]
    public void A_dropped_item_leaves_the_bar_through_0x019A()
    {
        var rig = new ShortcutRig((4, ShortcutRig.Dagger));
        rig.Dropped(ShortcutRig.Dagger);
        rig.R.Tick();

        Assert.True(rig.Bar[4].IsEmpty);
        Assert.Equal(4, Assert.Single(rig.Removes).Index);
    }

    [Fact]
    public void An_item_given_into_a_container_that_is_not_the_player_s_leaves_the_bar_through_0x0022()
    {
        var rig = new ShortcutRig((9, ShortcutRig.Other));
        rig.Into(ShortcutRig.Other, Chest);
        rig.R.Tick();

        Assert.True(rig.Bar[9].IsEmpty);
        Assert.Equal(9, Assert.Single(rig.Removes).Index);
    }

    [Fact]
    public void An_item_used_up_leaves_the_bar_when_its_0024_arrives()
    {
        var rig = new ShortcutRig((1, ShortcutRig.Potion));
        var use = rig.Api.UseShortcut(1, ActionSource.Ui);
        rig.R.Tick();
        Assert.Empty(rig.Removes);

        rig.Gone(ShortcutRig.Potion); // the last potion: ACE's 0024
        rig.R.UseDone();
        rig.R.Tick();

        Assert.True(rig.Bar[1].IsEmpty);
        Assert.Equal(1, Assert.Single(rig.Removes).Index);
        Assert.NotNull(use.Outcome); // the drink still ended on its own terms
    }

    [Fact]
    public void A_deleted_held_object_leaves_the_bar_through_F747()
    {
        var rig = new ShortcutRig((7, ShortcutRig.Scroll));
        rig.R.Raw(AceWireWriters.ObjectDelete(ShortcutRig.Scroll, 1));
        rig.R.Tick();

        Assert.True(rig.Bar[7].IsEmpty);
        Assert.Equal(7, Assert.Single(rig.Removes).Index);
    }

    [Fact]
    public void A_move_inside_the_player_s_own_packs_keeps_the_row_and_sends_nothing()
    {
        var rig = new ShortcutRig((0, ShortcutRig.Kit), (1, ShortcutRig.Potion));
        rig.Into(ShortcutRig.Kit, ShortcutRig.Player, 3); // another place in the main pack
        rig.Into(ShortcutRig.Potion, ShortcutRig.SidePack, 0); // into a side pack
        rig.R.Tick();

        Assert.Equal(ShortcutRig.Kit, rig.Bar[0].ObjectId);
        Assert.Equal(ShortcutRig.Potion, rig.Bar[1].ObjectId);
        Assert.False(rig.Bar.IsMissing(0));
        Assert.False(rig.Bar.IsMissing(1));
        Assert.Empty(rig.Removes);
    }

    [Fact]
    public void Wielding_an_item_keeps_the_row()
    {
        var rig = new ShortcutRig((2, ShortcutRig.Dagger));
        rig.R.Raw(AceM3Writers.WieldObject(ShortcutRig.Player, 900, ShortcutRig.Dagger, 0x00100000));
        rig.R.Tick();

        Assert.Equal(ShortcutRig.Dagger, rig.Bar[2].ObjectId);
        Assert.False(rig.Bar.IsMissing(2));
        Assert.Empty(rig.Removes);
    }

    [Fact]
    public void An_item_that_was_not_on_the_bar_sends_nothing_and_a_row_for_another_item_stays()
    {
        var rig = new ShortcutRig((0, ShortcutRig.Kit));
        rig.Gone(ShortcutRig.Other);
        rig.R.Tick();

        Assert.Equal(ShortcutRig.Kit, rig.Bar[0].ObjectId);
        Assert.Empty(rig.Removes);
        Assert.DoesNotContain(rig.Changes, e => e.Change == ShortcutChange.ItemLeft);
    }

    [Fact]
    public void An_item_that_leaves_twice_sends_once_and_a_row_for_an_object_never_held_is_left_alone()
    {
        var rig = new ShortcutRig((0, ShortcutRig.Kit), (5, 0x80009999));
        rig.Gone(ShortcutRig.Kit);
        rig.Gone(ShortcutRig.Kit);
        rig.R.Tick();

        Assert.True(rig.Bar[0].IsEmpty);
        Assert.Equal(0x80009999u, rig.Bar[5].ObjectId);
        Assert.Equal(0, Assert.Single(rig.Removes).Index);
    }

    [Fact]
    public void A_RemoveShortcut_the_session_cannot_take_is_logged_and_the_row_stays_off_the_model()
    {
        var rig = new ShortcutRig((0, ShortcutRig.Kit));
        rig.R.H.Net.Throw = new InvalidOperationException("not in the world");
        rig.Gone(ShortcutRig.Kit);
        rig.R.Tick();

        Assert.True(rig.Bar[0].IsEmpty);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("RemoveShortcut could not be sent", StringComparison.Ordinal));
    }
}

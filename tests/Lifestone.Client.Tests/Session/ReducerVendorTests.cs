using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 3.2 s1: the vendor model, fed message by message in the order ACE really sends (cache/m4/3-2/capture.md C1, C2): a Use is answered by UseDone 0 and, about a second later, the 0062; a buy and a sale are each answered by a refreshed
/// 0062; a second vendor replaces the first. Retail's close has no message, so the model closes the vendor by a named rule (<c>TODO(m4-vendor-close-rule)</c>): the player more than the UseRadius plus 1 m away, the vendor deleted, the world left.
/// </summary>
public sealed class ReducerVendorTests
{
    [Fact]
    public void The_capture_order_UseDone_then_the_0062_opens_the_vendor_with_one_VendorOpened()
    {
        var rig = new VendorRig();

        rig.UseDone(); // C1: UseDone 0 at +833 ms
        Assert.Null(rig.State.Vendor);
        Assert.Empty(rig.Events.OfType<VendorOpened>());

        rig.Tick(1.04);
        rig.ThelnothStock(); // the 0062, 1.04 s after the UseDone

        var opened = Assert.Single(rig.Events.OfType<VendorOpened>());
        Assert.Equal(VendorRig.Thelnoth, opened.Vendor);
        Assert.Empty(rig.Events.OfType<VendorStockChanged>());
        Assert.Empty(rig.Events.OfType<VendorClosed>());
        Assert.Equal(VendorRig.Thelnoth, rig.State.Vendor!.Vendor);
        Assert.Equal(4, rig.State.Vendor.Entries.Length);
    }

    [Fact]
    public void A_refreshed_0062_after_a_buy_is_VendorStockChanged_with_a_new_stock_and_the_old_one_untouched()
    {
        var rig = new VendorRig();
        rig.ThelnothStock();
        var first = rig.State.Vendor!;
        rig.Events.Clear();

        // C1 (buy 1 kit): ..., the refreshed 0062 at +12 ms.
        rig.Stock(VendorRig.Thelnoth, VendorRig.ThelnothMask, [VendorRig.Row(-1, VendorRig.KitRow, "Handy Healing Kit", 628, VendorRig.MiscType, 10)]);

        var second = rig.State.Vendor!;
        Assert.NotSame(first, second);
        Assert.True(second.Revision > first.Revision);
        Assert.Equal(4, first.Entries.Length); // the instance a caller holds never changes
        Assert.Single(second.Entries);
        var changed = Assert.Single(rig.Events.OfType<VendorStockChanged>());
        Assert.Equal(VendorRig.Thelnoth, changed.Vendor);
        Assert.Empty(rig.Events.OfType<VendorOpened>());
    }

    [Fact]
    public void A_second_Use_on_the_open_vendor_is_a_refresh_not_a_second_open()
    {
        var rig = new VendorRig();
        rig.ThelnothStock();
        rig.Events.Clear();

        rig.ThelnothStock();

        Assert.Single(rig.Events.OfType<VendorStockChanged>());
        Assert.Empty(rig.Events.OfType<VendorOpened>());
    }

    [Fact]
    public void A_second_vendor_closes_the_first_then_opens_itself()
    {
        var rig = new VendorRig();
        rig.ThelnothStock();
        rig.Events.Clear();

        // C2: Renald's 0062 (mask 0x480467A7, 53 rows in the capture) while Thelnoth's is the open one.
        rig.Stock(VendorRig.Renald, VendorRig.RenaldMask, [VendorRig.Row(-1, 0x80000500, "Leather Cap", 100, VendorRig.ArmorType, 20)]);

        Assert.Equal(VendorRig.Renald, rig.State.Vendor!.Vendor);
        Assert.Equal(VendorRig.RenaldMask, rig.State.Vendor.Mask);
        var closed = rig.Events.OfType<VendorClosed>().ToList();
        var opened = rig.Events.OfType<VendorOpened>().ToList();
        Assert.Equal(VendorRig.Thelnoth, Assert.Single(closed).Vendor);
        Assert.Equal(VendorRig.Renald, Assert.Single(opened).Vendor);
        Assert.True(rig.Events.IndexOf(closed[0]) < rig.Events.IndexOf(opened[0]), "the first closes before the second opens");
    }

    [Fact]
    public void The_vendor_closes_when_the_player_moves_beyond_the_use_radius_plus_the_margin()
    {
        var rig = new VendorRig();
        rig.ThelnothStock();
        rig.Events.Clear();

        rig.WalkTo(15.5f, 2); // 3.5 m from Thelnoth at x 12: UseRadius 3 + 1 m margin is 4
        Assert.NotNull(rig.State.Vendor);
        Assert.Empty(rig.Events.OfType<VendorClosed>());

        rig.WalkTo(16.5f, 3); // 4.5 m
        Assert.Null(rig.State.Vendor);
        var closed = Assert.Single(rig.Events.OfType<VendorClosed>());
        Assert.Equal(VendorRig.Thelnoth, closed.Vendor);
    }

    [Fact]
    public void A_vendor_whose_object_is_unknown_never_closes_by_distance()
    {
        var rig = new VendorRig();
        rig.Stock(VendorRig.Renald, VendorRig.RenaldMask, []); // Renald's object was never created in this world

        rig.WalkTo(60, 2);

        Assert.Equal(VendorRig.Renald, rig.State.Vendor!.Vendor);
        Assert.Empty(rig.Events.OfType<VendorClosed>());
    }

    [Fact]
    public void The_vendor_closes_when_its_object_is_deleted()
    {
        var rig = new VendorRig();
        rig.ThelnothStock();
        rig.Events.Clear();

        rig.H.R.Reducer.OnDeleteObject(rig.H.R.Context, new DeleteObjectMsg(VendorRig.Thelnoth, 1));

        Assert.Null(rig.State.Vendor);
        Assert.Equal(VendorRig.Thelnoth, Assert.Single(rig.Events.OfType<VendorClosed>()).Vendor);
    }

    [Fact]
    public void Leaving_the_world_closes_the_vendor()
    {
        var rig = new VendorRig();
        rig.ThelnothStock();
        rig.Events.Clear();

        rig.H.R.Reducer.OnSessionEvent(SessionEvent.LoggedOff(rig.H.R.Time.GetTimestamp(), SessionPhase.CharacterSelect));

        Assert.Null(rig.State.Vendor);
        Assert.Equal(VendorRig.Thelnoth, Assert.Single(rig.Events.OfType<VendorClosed>()).Vendor);
    }

    [Fact]
    public void Each_0062_raises_the_revision_whichever_vendor_sent_it()
    {
        var rig = new VendorRig();
        rig.ThelnothStock();
        var a = rig.State.Vendor!.Revision;
        rig.Stock(VendorRig.Renald, VendorRig.RenaldMask, []);
        var b = rig.State.Vendor!.Revision;
        rig.ThelnothStock();
        var c = rig.State.Vendor!.Revision;

        Assert.True(a < b && b < c);
    }
}

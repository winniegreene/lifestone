using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;

namespace Lifestone.Client.Tests.Session.Quartermaster;

/// <summary>
/// M6 item 16, slice Q10b: the quartermaster's free-slot reads (<see cref="PackView"/>) keep the two meanings they had as two copies. What these prove: both read the pack's capacity less its items on an empty pack;
/// an item held in the main pack takes one of each; a side pack in the main pack is no main-pack item for <see cref="PackView.MainPackFree"/> and, with no capacity sent for it, makes
/// <see cref="PackView.AllPacksLessFociFree"/> unknown (null) as it was; and a main pack held past its capacity is floored at 0 by the main-pack read and not floored by the all-packs read, as before.
/// </summary>
public sealed class PackViewTests
{
    [Fact]
    public void An_empty_pack_is_the_capacity_for_both_reads_and_each_item_takes_one()
    {
        var rig = new PurseViewTests.PurseRig();
        Assert.Equal((GearKit.PackCapacity, GearKit.PackCapacity), (PackView.MainPackFree(rig.State), PackView.AllPacksLessFociFree(rig.State)));

        rig.Hold("Loot", 9001, 0x1, 10);
        rig.Hold("Loot", 9002, 0x1, 10);

        Assert.Equal((GearKit.PackCapacity - 2, GearKit.PackCapacity - 2), (PackView.MainPackFree(rig.State), PackView.AllPacksLessFociFree(rig.State)));
    }

    [Fact]
    public void A_side_pack_with_no_capacity_sent_leaves_the_main_read_and_makes_the_all_packs_read_unknown()
    {
        var rig = new PurseViewTests.PurseRig();
        var pack = rig.SidePack();
        rig.Hold("Loot", 9001, 0x1, 10, container: pack);

        Assert.Equal(GearKit.PackCapacity, PackView.MainPackFree(rig.State)); // a side pack and what lies in it are not main-pack slots
        Assert.Null(PackView.AllPacksLessFociFree(rig.State)); // a side pack whose capacity is not known
    }

    [Fact]
    public void A_main_pack_held_past_its_capacity_is_floored_by_the_main_read_only()
    {
        var rig = new PurseViewTests.PurseRig();
        for (uint i = 0; i < GearKit.PackCapacity + 1; i++)
        {
            rig.Hold("Loot", 9000 + i, 0x1, 10);
        }

        Assert.Equal(0, PackView.MainPackFree(rig.State));
        Assert.Equal(-1, PackView.AllPacksLessFociFree(rig.State));
    }
}

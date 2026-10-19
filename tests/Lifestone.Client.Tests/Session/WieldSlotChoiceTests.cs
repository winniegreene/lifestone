using Lifestone.Client.Session.Contracts;
using Xunit;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The location a double-click wield asks for (<see cref="WieldSlotChoice"/>, retail's <c>CPlayerSystem::AutoWield</c> 11.6096 0x005617D0): a ring or bracelet takes the first free side (the right first), and an
/// item whose mask is not such a pair goes out whole.
/// </summary>
public sealed class WieldSlotChoiceTests
{
    private const uint BothRings = (uint)(EquipMask.FingerWearLeft | EquipMask.FingerWearRight);
    private const uint BothWrists = (uint)(EquipMask.WristWearLeft | EquipMask.WristWearRight);

    private static PlayerInventory Wearing(params EquipMask[] locations)
    {
        var inventory = new PlayerInventory();
        var equipped = new List<EquippedItem>();
        var guid = 0x80000200u;
        foreach (var location in locations)
        {
            equipped.Add(new EquippedItem(guid++, (uint)location, 0));
        }

        inventory.Replace([], equipped, true);
        return inventory;
    }

    [Fact]
    public void A_ring_with_both_slots_free_goes_to_the_right()
    {
        Assert.Equal((uint)EquipMask.FingerWearRight, WieldSlotChoice.Choose(Wearing(), BothRings));
    }

    [Fact]
    public void A_ring_goes_to_the_free_slot_when_one_is_taken()
    {
        Assert.Equal((uint)EquipMask.FingerWearLeft, WieldSlotChoice.Choose(Wearing(EquipMask.FingerWearRight), BothRings));
        Assert.Equal((uint)EquipMask.FingerWearRight, WieldSlotChoice.Choose(Wearing(EquipMask.FingerWearLeft), BothRings));
    }

    [Fact]
    public void A_ring_with_both_slots_taken_asks_for_the_preferred_slot_so_the_server_swaps_it()
    {
        Assert.Equal((uint)EquipMask.FingerWearRight, WieldSlotChoice.Choose(Wearing(EquipMask.FingerWearLeft, EquipMask.FingerWearRight), BothRings));
    }

    [Fact]
    public void A_bracelet_follows_the_same_rule_and_is_not_confused_with_the_rings()
    {
        Assert.Equal((uint)EquipMask.WristWearLeft, WieldSlotChoice.Choose(Wearing(EquipMask.WristWearRight, EquipMask.FingerWearLeft), BothWrists));
        Assert.Equal((uint)EquipMask.WristWearRight, WieldSlotChoice.Choose(Wearing(EquipMask.FingerWearRight), BothWrists));
    }

    [Fact]
    public void Other_masks_go_out_whole()
    {
        var coverage = (uint)(EquipMask.ChestArmor | EquipMask.AbdomenArmor | EquipMask.UpperArmArmor);
        Assert.Equal(coverage, WieldSlotChoice.Choose(Wearing(EquipMask.ChestArmor), coverage));
        Assert.Equal((uint)EquipMask.Held, WieldSlotChoice.Choose(Wearing(), (uint)EquipMask.Held));
        Assert.Equal((uint)EquipMask.FingerWearLeft, WieldSlotChoice.Choose(Wearing(), (uint)EquipMask.FingerWearLeft)); // a single-sided ring is already one slot
    }
}

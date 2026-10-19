namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The location a double-click wield asks for. Retail's <c>CPlayerSystem::AutoWield</c> (11.6096 0x005617D0) tests the item's valid locations against its
/// <c>inventoryMask</c> (the locations already occupied) and asks for the first free one; for a ring or a bracelet it takes the preferred side (the right, as
/// the double click's slot side is) and, when that is taken and the second choice is allowed, the other side. When both are taken it blocks on the preferred side,
/// so the request names that one slot and the server swaps it. Every other mask (armour coverage, weapons) goes out whole, as ACE stores the mask as given
/// (<c>Player_Inventory.cs</c> 1651-1730). The retail double click passes no second choice; the general auto-wield path passes one, and the
/// brief for this fix asks for the free-slot rule, so both sides are tried (<c>TODO(m3-wield-second-choice)</c> if the double click proves to differ live).
/// </summary>
public static class WieldSlotChoice
{
    private const uint Wrists = (uint)(EquipMask.WristWearLeft | EquipMask.WristWearRight);
    private const uint Fingers = (uint)(EquipMask.FingerWearLeft | EquipMask.FingerWearRight);

    /// <summary>The mask to send for an item whose valid locations are <paramref name="valid"/>, given what <paramref name="inventory"/> has equipped.</summary>
    public static uint Choose(PlayerInventory inventory, uint valid)
    {
        valid = Pair(inventory, valid, EquipMask.WristWearRight, EquipMask.WristWearLeft, Wrists);
        return Pair(inventory, valid, EquipMask.FingerWearRight, EquipMask.FingerWearLeft, Fingers);
    }

    private static uint Pair(PlayerInventory inventory, uint valid, EquipMask preferred, EquipMask other, uint both)
    {
        if ((valid & both) != both)
        {
            return valid;
        }

        var rest = valid & ~both;
        if (inventory.EquippedAt((uint)preferred) is null)
        {
            return rest | (uint)preferred;
        }

        return rest | (inventory.EquippedAt((uint)other) is null ? (uint)other : (uint)preferred);
    }
}

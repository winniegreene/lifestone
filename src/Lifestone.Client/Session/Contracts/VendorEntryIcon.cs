namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The icon fields of a shop row (M4 3.2 s4b): what the vendor window's cell needs to draw the row, read from the row's WeenieDesc in the contract layer (the retail UI never names the wire types), as
/// <see cref="WorldObjectIcon.ItemFields"/> does for an object in the world.
/// </summary>
public static class VendorEntryIcon
{
    /// <summary>The icon, underlay, overlay, UI effects, stack size and plural name of <paramref name="entry"/>; <see cref="ItemIconFields.StackSize"/> is the stack of the row's object (1 for an unlimited row's single item).</summary>
    public static ItemIconFields ItemFields(this VendorStockEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var d = entry.Desc;
        return new ItemIconFields(Did(d.IconId), Did(d.IconUnderlayId), Did(d.IconOverlayId), d.UiEffects ?? 0, (int)entry.StackSize, d.ValidLocations ?? 0, d.PluralName);
    }

    private static uint Did(uint? id) => id is { } value && (value & 0x00FFFFFFu) != 0 ? value : 0;
}

/// <summary>What the vendor window's sell rule reads of an object the player carries (M4 3.2 s4b), in the contract layer: the retail UI never names the wire types.</summary>
public static class WorldObjectSaleFacts
{
    /// <summary>The Value of the whole stack (0 when the object has none).</summary>
    public static uint StackValue(this WorldObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Weenie.Value ?? 0;
    }

    /// <summary>The stack size (1 for an object that is not a stack).</summary>
    public static uint StackSizeOr1(this WorldObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Weenie.StackSize is { } s and > 0 ? s : 1u;
    }

    /// <summary>True when the object glows as magical (UiEffects bit 0).</summary>
    public static bool IsMagical(this WorldObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return ((item.Weenie.UiEffects ?? 0) & 1) != 0;
    }
}

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The CreateObject WeenieDesc fields a UI cell draws an item from (M3 2.2 b1): the retail UI may not name <c>Lifestone.Net</c> types, so the contract hands them over as plain values. A DID of 0 or of a bare type id (a packed 0
/// reads as 0x06000000) is "none" here.
/// </summary>
/// <param name="IconId">The icon (a RenderSurface DID), or 0.</param>
/// <param name="UnderlayId">The custom icon underlay, or 0.</param>
/// <param name="OverlayId">The custom icon overlay, or 0.</param>
/// <param name="UiEffects">The UI effects word (Magical is bit 0), or 0.</param>
/// <param name="StackSize">The stack size, or 1 for an object that does not stack.</param>
/// <param name="ValidLocations">The <c>EquipMask</c> bits the item can be worn or wielded at, or 0 for an item that cannot be.</param>
/// <param name="PluralName">The plural name the description sent, or null.</param>
/// <param name="ItemsCapacity">The number of items it can hold (a container's), or 0.</param>
/// <param name="ContainersCapacity">The number of containers it can hold, or 0.</param>
/// <param name="Structure">The remaining structure (uses or durability), or 0.</param>
/// <param name="MaxStructure">The full structure, or 0 for an object without one.</param>
public readonly record struct ItemIconFields(uint IconId, uint UnderlayId, uint OverlayId, uint UiEffects, int StackSize, uint ValidLocations, string? PluralName, int ItemsCapacity = 0, int ContainersCapacity = 0, int Structure = 0, int MaxStructure = 0);

/// <summary>The item fields of a <see cref="WorldObject"/> for the retail UI's item cells (additive; <see cref="WorldObject"/> itself is unchanged).</summary>
public static class WorldObjectIcon
{
    /// <summary>The icon, stack and location fields of <paramref name="obj"/>'s CreateObject, as <see cref="ItemIconFields"/>.</summary>
    public static ItemIconFields ItemFields(this WorldObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var weenie = obj.Weenie;
        return new ItemIconFields(
            Did(weenie.IconId),
            Did(weenie.IconUnderlayId),
            Did(weenie.IconOverlayId),
            weenie.UiEffects ?? 0,
            weenie.StackSize ?? 1,
            weenie.ValidLocations ?? 0,
            weenie.PluralName,
            weenie.ItemsCapacity ?? 0,
            weenie.ContainersCapacity ?? 0,
            weenie.Structure ?? 0,
            weenie.MaxStructure ?? 0);
    }

    private static uint Did(uint? id) => id is { } value && (value & 0x00FFFFFFu) != 0 ? value : 0;
}

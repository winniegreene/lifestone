namespace Lifestone.Net.Messages;

// The inventory, wield and public-quality messages (M3 1.2 s0). InventoryCodecs holds the parsers and the layouts. Lane
// s2 applies them to the object table, the pack and the equipment.

/// <summary>GM 0x0024 InventoryRemoveObject: an item left the player's inventory (a pickup by someone else, a burnt component).</summary>
/// <param name="Guid">The item.</param>
public readonly record struct InventoryRemoveObjectMsg(uint Guid);

/// <summary>GM 0x0197 SetStackSize: a stack's new size and value.</summary>
/// <param name="Sequence">The u8 update sequence (ACE's property-int sequence for StackSize).</param>
/// <param name="Guid">The stack.</param>
/// <param name="StackSize">Its new size.</param>
/// <param name="Value">Its new value.</param>
public readonly record struct SetStackSizeMsg(byte Sequence, uint Guid, uint StackSize, uint Value);

/// <summary>GM 0x02CE PublicUpdatePropertyInt: an int property of any object changed (a wielded location, a stack's use count).</summary>
/// <param name="Sequence">The u8 update sequence.</param>
/// <param name="Guid">The object.</param>
/// <param name="Property">The property id (retail's PropertyInt).</param>
/// <param name="Value">The new value.</param>
public readonly record struct PublicUpdatePropertyIntMsg(byte Sequence, uint Guid, uint Property, int Value);

/// <summary>GM 0x02DA PublicUpdateInstanceId: an instance-id property of any object changed (its container, its wielder).</summary>
/// <param name="Sequence">The u8 update sequence.</param>
/// <param name="Guid">The object.</param>
/// <param name="Property">The property id (retail's PropertyInstanceId: 2 Container, 3 Wielder, ...).</param>
/// <param name="Value">The GUID it now names (0 for none).</param>
public readonly record struct PublicUpdateInstanceIdMsg(byte Sequence, uint Guid, uint Property, uint Value);

/// <summary>GameEvent 0x0022 InventoryPutObjInContainer: the server confirms an item is now in a container.</summary>
/// <param name="Item">The item.</param>
/// <param name="Container">The container (the player or a pack).</param>
/// <param name="Placement">The slot in the container (ACE's <c>PlacementPosition</c>).</param>
/// <param name="ContainerType">ACE's <c>ContainerType</c> of the item (0 not a container, 1 container, 2 foci).</param>
public readonly record struct InventoryPutObjInContainerEvent(uint Item, uint Container, uint Placement, uint ContainerType);

/// <summary>GameEvent 0x0023 WieldObject: the server confirms an item is wielded.</summary>
/// <param name="Object">The item.</param>
/// <param name="Location">The <c>EquipMask</c> it now occupies.</param>
public readonly record struct WieldObjectEvent(uint Object, uint Location);

/// <summary>GameEvent 0x019A InventoryPutObjectIn3D: the server confirms an item was dropped to the ground.</summary>
/// <param name="Object">The item.</param>
public readonly record struct InventoryPutObjectIn3DEvent(uint Object);

/// <summary>GameEvent 0x00A0 InventoryServerSaysFailed: an inventory action was refused.</summary>
/// <param name="Item">The item the action was about.</param>
/// <param name="Error">The WeenieError (0 when ACE gives none).</param>
public readonly record struct InventoryServerSaysFailedEvent(uint Item, uint Error);

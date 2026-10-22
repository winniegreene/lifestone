using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.State;

/// <summary>
/// The inventory, wield and public-quality messages (M3 1.2 s2): the player's <see cref="PlayerInventory"/> (<see cref="SelfState.Inventory"/>) from the description's lists and from
/// GameEvents 0x0022, 0x0023, 0x019A and 0x00A0 and GameMessages 0x0024, 0x0197, 0x02CE and 0x02DA, with an item's <see cref="WorldObject"/> kept in step.
/// </summary>
/// <remarks>
/// <para>The writers are ACE's (<c>Player_Inventory.cs</c>: <c>TryCreateInInventoryWithNetworking</c> 90-114, <c>TryConsumeFromInventoryWithNetworking</c> 116-140,
/// <c>TryRemoveFromInventoryWithNetworking</c> 215-230, <c>TryEquipObjectWithNetworking</c> 297-306, <c>TryDequipObjectWithNetworking</c> 400-405, the put at 1359 and the drop at 1441).
/// Membership follows 0022, 0023, 019A and 0024. ACE's wield sends 02DA Wielder 0 and 02CE CurrentWieldedLocation 0 before 0023 names the location, and its unwield sends the same two
/// zeros before the 0022 into the pack, so 02DA and 02CE only update the item's qualities (its world object, and the model's per-item ints); 0023 and 0022 then set the container, wielder
/// and wielded location they mean.</para>
/// <para>Retail's handlers (11.6096, 11.4186 in brackets): <c>ACCWeenieObject::ServerSaysContainID</c> 0x0058CC70 [0x0058BE40] (0022), <c>ACCWeenieObject::ServerSaysSetStackSize</c> 0x0058DA70 [0x0058CC40]
/// (0197), <c>CM_Qualities::DispatchUI_UpdateInt</c> 0x006B0000 [0x006AF130] and <c>DispatchUI_UpdateInstanceID</c> 0x006AFFB0 [0x006AF0E0] (02CE, 02DA; their per-object sequence is
/// <c>WTimeStamper::UpdateTS</c> 0x006B3CB0), <c>CPlayerSystem::RecvNotice_ServerSaysMoveItem</c> 0x00564000 [0x00563260] (019A), <c>CPlayerSystem::RecvNotice_ServerSaysAttemptFailed</c>
/// 0x0055F1B0 [0x0055E490] (00A0). Not found in the symbol table: the handlers of 0024 and 0023 (<c>TODO(retail-handler-0024)</c>, <c>TODO(retail-handler-0023)</c>, unchanged from s0).</para>
/// </remarks>
internal sealed partial class GameStateReducer
{
    private const uint ContainerProperty = 2;
    private const uint WielderProperty = 3;
    private const uint InstanceTag = 0x10000;
    private const uint StackTag = 0x20000;
    private const uint ItemTypeContainer = 0x200;

    private readonly HashSet<uint> _inventoryNotedProperties = [];
    private bool _heldDeleteNoted;
    private bool _adoptNoted;

    /// <summary>
    /// The PlayerDescription's <c>Inventory</c> (the main pack, in wire order) and <c>Equipped</c> lists, applied after the qualities (the one call from <c>OnPlayerDescription</c>). A description
    /// whose gameplay options precede the lists carries none (<see cref="PlayerDescriptionMsg.InventoryUndecoded"/>): the model is then known but incomplete.
    /// </summary>
    private void ApplyDescriptionInventory(PlayerDescriptionMsg message)
    {
        var self = _state.Self;
        var inventory = self.Inventory;
        inventory.Bind(self.Qualities);
        inventory.Owner = self.Guid;
        inventory.Replace(
            message.Inventory.Select(c => new InventoryEntry(c.Guid, c.ContainerType)),
            message.Equipped.Select(e => new EquippedItem(e.Guid, e.Location, e.Priority)),
            !message.InventoryUndecoded);
        Changed();
        Publish(new InventoryChanged(InventoryChangeKind.Reset, 0));
    }

    /// <summary>
    /// GM 0x0024: the item is gone from the player's holdings (a component burnt, an item consumed or given). The model forgets it and leaves its world object (it clears the object's container and
    /// wielder; whether retail destroys the object is <c>TODO(retail-handler-0024)</c>). An item the player does not hold is only logged.
    /// </summary>
    public void OnInventoryRemoveObject(in InboundContext context, in InventoryRemoveObjectMsg message) =>
        Leave(message.Guid, InventoryChangeKind.Removed, MessageOpcodes.InventoryRemoveObject);

    /// <summary>GM 0x0197: a stack's size and value. Kept for a held item and written to its world object; a stale sequence is dropped.</summary>
    public void OnSetStackSize(in InboundContext context, in SetStackSizeMsg message)
    {
        var inventory = _state.Self.Inventory;
        if (!inventory.AcceptStamp(message.Guid, StackTag, message.Sequence))
        {
            ReportDrop(MessageOpcodes.SetStackSize, message.Guid, DropReason.Stale);
            return;
        }

        var known = _state.Objects.TryGet(message.Guid, out var item);
        var previousStack = known ? item!.Weenie.StackSize : null;
        var stackSize = message.StackSize;
        var stackValue = message.Value;
        if (known)
        {
            Mirror(item!, w => w with
            {
                StackSize = (ushort)Math.Min(stackSize, ushort.MaxValue),
                Value = stackValue,
                Header = w.Header | WeenieHeaderFlags.StackSize | WeenieHeaderFlags.Value,
            });
        }

        if (!inventory.Contains(message.Guid))
        {
            NoteNotHeld(MessageOpcodes.SetStackSize, message.Guid, known);
            return;
        }

        inventory.SetStack(message.Guid, message.StackSize, message.Value);
        Changed();
        Publish(new InventoryChanged(InventoryChangeKind.StackChanged, message.Guid, StackSize: message.StackSize, Value: message.Value));
        NoteAmmoStack(message.Guid, previousStack, message.StackSize);
    }

    /// <summary>
    /// A launch (M4 1.2 s1): in the launch tick ACE sends the wielded ammo's 0x0197 with the stack one smaller (capture.md C3: 02CE, 0197, 02CD, then F750/F745/F755/F74A), so a fall of the stack of the item at
    /// <see cref="HeldAmmo.Location"/> in missile combat mode is one shot of the <see cref="Attack"/> sequence. A stack that did not fall (a merge, a pick-up) or one with no known earlier size is not a shot.
    /// TODO(m4-last-arrow-launch): the launch that uses the last arrow may delete the stack instead of sending 0x0197; not captured, so it is not counted (M4 1.3 s2: the missile attack tracker logs the TODO once per run when the arrow's F745 is taken as the launch with no 0x0197).
    /// </summary>
    private void NoteAmmoStack(uint guid, ushort? previous, uint now)
    {
        if (previous is not { } before || now >= before || _state.Self.Qualities.CombatMode != CombatModes.Missile || _state.Self.Inventory.EquippedAt(HeldAmmo.Location) != guid)
        {
            return;
        }

        WireAttack();
        PublishAttack(_attack.OnShot());
    }

    /// <summary>
    /// GM 0x02CE: an int quality of an object (retail <c>CM_Qualities::DispatchUI_UpdateInt</c>; per-object sequence). The fields of the object's weenie description that are these qualities are kept in step
    /// (container-less ones such as CurrentWieldedLocation 10, StackSize 12, Value 19, Burden 5); a held item also keeps the value in the model (<see cref="PlayerInventory.TryGetInt"/>) and raises
    /// <see cref="InventoryChangeKind.PropertyChanged"/>. The player's own object takes it into its qualities.
    /// </summary>
    public void OnPublicUpdatePropertyInt(in InboundContext context, in PublicUpdatePropertyIntMsg message)
    {
        var self = _state.Self;
        if (!self.Inventory.AcceptStamp(message.Guid, message.Property, message.Sequence))
        {
            ReportDrop(MessageOpcodes.PublicUpdatePropertyInt, message.Guid, DropReason.Stale);
            return;
        }

        if (self.Guid != 0 && message.Guid == self.Guid)
        {
            self.Qualities.Ints[message.Property] = message.Value;
            Changed();
            Publish(new QualitiesChanged(QualityChange.Int, message.Property));
            return;
        }

        var known = _state.Objects.TryGet(message.Guid, out var item);
        if (known)
        {
            MirrorInt(item!, message.Property, message.Value);
        }

        if (!self.Inventory.Contains(message.Guid))
        {
            NoteNotHeld(MessageOpcodes.PublicUpdatePropertyInt, message.Guid, known);
            return;
        }

        self.Inventory.SetInt(message.Guid, message.Property, message.Value);
        Changed();
        Publish(new InventoryChanged(InventoryChangeKind.PropertyChanged, message.Guid, Property: message.Property, PropertyValue: message.Value));
    }

    /// <summary>
    /// GM 0x02DA: an instance-id quality (retail <c>CM_Qualities::DispatchUI_UpdateInstanceID</c>; per-object sequence). Container (2) and Wielder (3) are written to the object's weenie description
    /// (0 clears them); membership of the model follows 0022, 0023, 019A and 0024 (see the class remarks). Any other instance property has nothing to update and no consumer, and is logged once per property.
    /// The Value and ItemType a loot rule needs are fields of the F745's weenie description (<see cref="WorldObject.Weenie"/> Value, <see cref="WorldObject.ItemType"/>), not instance properties:
    /// <c>m3-inventory-instance-properties</c> is closed (M4 2.1 s1).
    /// </summary>
    public void OnPublicUpdateInstanceId(in InboundContext context, in PublicUpdateInstanceIdMsg message)
    {
        if (!_state.Self.Inventory.AcceptStamp(message.Guid, InstanceTag | message.Property, message.Sequence))
        {
            ReportDrop(MessageOpcodes.PublicUpdateInstanceId, message.Guid, DropReason.Stale);
            return;
        }

        var known = _state.Objects.TryGet(message.Guid, out var item);
        uint? value = message.Value == 0 ? null : message.Value;
        switch (message.Property)
        {
            case ContainerProperty when known:
                Mirror(item!, w => w with { ContainerId = value, Header = Flag(w.Header, WeenieHeaderFlags.Container, value is not null) });
                break;
            case WielderProperty when known:
                Mirror(item!, w => w with { WielderId = value, Header = Flag(w.Header, WeenieHeaderFlags.Wielder, value is not null) });
                break;
            case ContainerProperty or WielderProperty:
                NoteNotHeld(MessageOpcodes.PublicUpdateInstanceId, message.Guid, known);
                break;
            default:
                if (_inventoryNotedProperties.Add(message.Property))
                {
                    _log.Info(
                        SessionLog.Category,
                        "a PublicUpdateInstanceId for an instance property the reducer does not apply (no consumer; Value and ItemType come from the weenie description); first seen",
                        new LogField("property", message.Property),
                        new LogField("guid", message.Guid));
                }

                break;
        }
    }

    /// <summary>
    /// GE 0x0022 (retail <c>ACCWeenieObject::ServerSaysContainID</c>): the item is in <c>Container</c> at <c>Placement</c>. A held item moves (an equipped one is unwielded); an item the player did not
    /// hold is added. A container that is not the player or one of the player's side packs is a pack that is the player's only by its own CreateObject (its ContainerId is the player's): it is added
    /// as a side pack; any other container (a chest) takes the item out of the player's hands (<see cref="InventoryChangeKind.Removed"/>).
    /// </summary>
    public void OnInventoryPutObjInContainer(in InboundContext context, in GameEventHeader header, in InventoryPutObjInContainerEvent message)
    {
        var self = _state.Self;
        var inventory = self.Inventory;
        inventory.Owner = self.Guid;
        MoveInViewedContainer(message.Item, message.Container, (int)Math.Min(message.Placement, int.MaxValue));
        if (!inventory.IsKnownContainer(message.Container) && self.Guid != 0
            && _state.Objects.TryGet(message.Container, out var pack) && pack.ContainerId == self.Guid)
        {
            inventory.AddKnownSidePack(new InventoryEntry(message.Container, ContainerTypeOf(pack)), int.MaxValue);
        }

        if (!inventory.IsKnownContainer(message.Container))
        {
            Leave(message.Item, InventoryChangeKind.Removed, GameEventTypes.InventoryPutObjInContainer, message.Container);
            return;
        }

        var container = message.Container;
        var from = inventory.PutInContainer(message.Item, message.ContainerType, container, (int)Math.Min(message.Placement, int.MaxValue));
        var now = inventory.Find(message.Item);
        if (message.ContainerType != 0 && container == self.Guid)
        {
            // A pack that just arrived: its 0196 came before this 0022 and its F745 follows (capture C1).
            ApplyPendingPackView(message.Item);
        }

        if (_state.Objects.TryGet(message.Item, out var item))
        {
            Mirror(item, w => w with
            {
                ContainerId = container,
                WielderId = null,
                CurrentlyWieldedLocation = null,
                Header = (w.Header | WeenieHeaderFlags.Container) & ~(WeenieHeaderFlags.Wielder | WeenieHeaderFlags.CurrentlyWieldedLocation),
            });
        }

        var kind = from.Kind switch
        {
            InventoryPlaceKind.None => InventoryChangeKind.Added,
            InventoryPlaceKind.Equipped => InventoryChangeKind.Unwielded,
            _ => InventoryChangeKind.Moved,
        };
        Changed();
        Publish(new InventoryChanged(kind, message.Item, message.Container, now.Placement, 0, from.Container, from.Placement, from.Location));
        PublishSidePackChanged(message.Container);
        if (from.Kind == InventoryPlaceKind.InSidePack)
        {
            PublishSidePackChanged(from.Container);
        }
    }

    /// <summary>
    /// GE 0x0023 (<c>TODO(retail-handler-0023)</c>): the item is wielded at <c>Location</c> (an <c>EquipMask</c>). It leaves the pack it was in (or its old equipped slot) for the equipped list; its
    /// world object gets the player as wielder and the location, and no container.
    /// </summary>
    public void OnWieldObject(in InboundContext context, in GameEventHeader header, in WieldObjectEvent message)
    {
        var self = _state.Self;
        var inventory = self.Inventory;
        inventory.Owner = self.Guid;
        var location = message.Location;
        var known = _state.Objects.TryGet(message.Object, out var item);
        var from = inventory.Equip(message.Object, message.Location, known ? item!.Weenie.Priority ?? 0 : 0);
        if (known)
        {
            Mirror(item!, w => w with
            {
                ContainerId = null,
                WielderId = self.Guid == 0 ? null : self.Guid,
                CurrentlyWieldedLocation = location,
                Header = (w.Header & ~WeenieHeaderFlags.Container) | WeenieHeaderFlags.Wielder | WeenieHeaderFlags.CurrentlyWieldedLocation,
            });
        }

        Changed();
        Publish(new InventoryChanged(InventoryChangeKind.Wielded, message.Object, Location: message.Location, FromContainer: from.Container, FromPlacement: from.Placement, FromLocation: from.Location));
    }

    /// <summary>GE 0x019A (retail <c>CPlayerSystem::RecvNotice_ServerSaysMoveItem</c>): the item was dropped to the ground; it leaves the pack and the equipped list (ACE's position update follows).</summary>
    public void OnInventoryPutObjectIn3D(in InboundContext context, in GameEventHeader header, in InventoryPutObjectIn3DEvent message) =>
        Leave(message.Object, InventoryChangeKind.Dropped, GameEventTypes.InventoryPutObjectIn3D);

    /// <summary>
    /// GE 0x00A0 (retail <c>CPlayerSystem::RecvNotice_ServerSaysAttemptFailed</c>): the server refused an inventory action. No state changes; <see cref="InventoryFailed"/> carries the item and the
    /// WeenieError for a tracker.
    /// </summary>
    public void OnInventoryServerSaysFailed(in InboundContext context, in GameEventHeader header, in InventoryServerSaysFailedEvent message)
    {
        if (_log.IsEnabled(LogSeverity.Debug))
        {
            _log.Debug(SessionLog.Category, "inventory action refused", new LogField("item", message.Item), new LogField("error", message.Error));
        }

        Publish(new InventoryFailed(message.Item, message.Error));
    }

    /// <summary>
    /// A world object whose CreateObject says the player holds it (its container is the player or a known side pack, or the player wields it) and which the model does not list yet: ACE sends no 0022 for
    /// the contents of a side pack at login, only each item's CreateObject. Call it from the create path once the object is in the table (the tolerance when no 0196 named the item, M4 2.1 s1: <c>m3-inventory-sidepack-contents</c> is closed by <c>OnViewContents</c>; <c>CreateNew</c> calls it
    /// during the arrival only, so a live grant is published once, as Added, by its own 0022: M3 1.2 fix R3). The slot is unknown, so the item goes last; a later 0022 sets it.
    /// </summary>
    internal void AdoptHeldObject(WorldObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var self = _state.Self;
        var inventory = self.Inventory;
        if (!inventory.IsKnown || self.Guid == 0 || inventory.Contains(item.Guid))
        {
            return;
        }

        inventory.Owner = self.Guid;
        if (item.WielderId == self.Guid && item.Weenie.CurrentlyWieldedLocation is { } location and not 0)
        {
            inventory.Equip(item.Guid, location, item.Weenie.Priority ?? 0);
            Changed();
            Publish(new InventoryChanged(InventoryChangeKind.Wielded, item.Guid, Location: location));
        }
        else if (item.ContainerId is { } container and not 0 && inventory.IsKnownContainer(container))
        {
            if (container != self.Guid && !_adoptNoted)
            {
                _adoptNoted = true;
                _log.Info(SessionLog.Category, "a side-pack item joined the inventory model from its own CreateObject: no 0196 ViewContents named it (the tolerance); first seen", new LogField("guid", item.Guid), new LogField("pack", container));
            }

            inventory.PutInContainer(item.Guid, ContainerTypeOf(item), container, int.MaxValue);
            Changed();
            Publish(new InventoryChanged(InventoryChangeKind.Added, item.Guid, container, inventory.Find(item.Guid).Placement));
        }
    }

    /// <summary>
    /// F747 ObjectDelete for an item the player holds: it leaves the model too. ACE's death drop of a WIELDED item is <c>TryDequipObjectWithNetworking(ToCorpseOnDeath)</c> (<c>Player_Inventory.cs</c> 397-441, called from
    /// <c>Player_Death.cs</c> 548 and 565): 02DA Wielder 0, 02CE CurrentWieldedLocation 0, F74A PickupEvent, the unwield sound and F747 DeleteObject, and no 0024 (a pack item gets 02DA Container 0 and 0024 instead,
    /// <c>TryRemoveFromInventoryWithNetworking</c> 217-226). Without this the equipped list kept the guid of an object that no longer existed, and a re-gear counted it as an item with no world object (live, M3 3.2).
    /// <para><b>Retail.</b> The client takes an F747 through <c>CM_Physics::DispatchSB_DeleteObject</c> (11.6096 0x006AD600) to <c>SmartBox::HandleDeleteObject</c> (0x00451EE0: refuses the player's own guid, queues a blob for an
    /// object it does not know yet, compares the instance timestamp), then <c>ACCObjectMaint::DeleteObject</c> (0x00558330: tells the plugin interface and marks a corpse deleted) and <c>CObjectMaint::DeleteObject(id)</c>
    /// (0x00508FA0), which destroys the physics object (out of the world, out of its parent) and the weenie object, whatever container or slot held it. Lifestone's model forgets the held entry with it, as an object that no longer
    /// exists must be. <c>TODO(m3-inventory-delete-retail)</c>: whether the weenie's destruction hook (the virtual call <c>CObjectMaint::DeleteObject</c> makes before it removes the weenie from its tables) also takes the item
    /// out of retail's placement list was not read; the first such release is logged with the name, so a live round shows when it mattered.</para>
    /// </summary>
    private void ReleaseDeletedHeld(uint guid)
    {
        var inventory = _state.Self.Inventory;
        if (!inventory.IsKnown || !inventory.Contains(guid))
        {
            return;
        }

        if (!_heldDeleteNoted)
        {
            _heldDeleteNoted = true;
            _log.Info(SessionLog.Category, "TODO(m3-inventory-delete-retail): a held object was deleted by its F747 and the held entry released; retail's placement-list handling of it is not read; first seen", new LogField("guid", guid));
        }

        Leave(guid, InventoryChangeKind.Removed, MessageOpcodes.ObjectDelete);
    }

    /// <summary>An item leaves the player's holdings (0024, 019A, or a 0022 into a container that is not the player's).</summary>
    private void Leave(uint guid, InventoryChangeKind kind, uint code, uint into = 0)
    {
        var self = _state.Self;
        var inventory = self.Inventory;
        inventory.Owner = self.Guid;
        var from = inventory.Release(guid);
        EvictShortcuts(guid); // OpenAC #219: the item left, so its shortcut row leaves with it (M4 3.2 h1)
        var known = _state.Objects.TryGet(guid, out var item);
        if (known)
        {
            Mirror(item!, w => w with
            {
                ContainerId = into == 0 ? null : into,
                WielderId = null,
                CurrentlyWieldedLocation = null,
                Header = Flag(w.Header & ~(WeenieHeaderFlags.Wielder | WeenieHeaderFlags.CurrentlyWieldedLocation), WeenieHeaderFlags.Container, into != 0),
            });
        }

        if (!from.Held)
        {
            NoteNotHeld(code, guid, known);
            return;
        }

        Changed();
        Publish(new InventoryChanged(kind, guid, into, -1, 0, from.Container, from.Placement, from.Location));
        if (from.Kind == InventoryPlaceKind.InSidePack)
        {
            PublishSidePackChanged(from.Container);
        }
        else if (from.Kind == InventoryPlaceKind.SidePack)
        {
            _state.Containers.Forget(guid);
        }
    }

    /// <summary>ACE's <c>ContainerType</c> of an object by its ItemType: 1 for a container, else 0 (a foci cannot be told from its CreateObject: <c>TODO(m3-inventory-foci)</c>).</summary>
    private static uint ContainerTypeOf(WorldObject item) => (item.ItemType & ItemTypeContainer) != 0 ? 1u : 0u;

    /// <summary>A message about an object the player does not hold: nothing to apply to the model (logged at DEBUG).</summary>
    private void NoteNotHeld(uint code, uint guid, bool known)
    {
        if (_log.IsEnabled(LogSeverity.Debug))
        {
            _log.Debug(SessionLog.Category, "inventory message for an item the player does not hold", new LogField("code", code), new LogField("guid", guid), new LogField("inTable", known));
        }
    }

    /// <summary>Replaces the object's last description with one whose weenie description <paramref name="edit"/> changed.</summary>
    private static void Mirror(WorldObject item, Func<WeenieDesc, WeenieDesc> edit) =>
        item.Created = item.Created with { Weenie = edit(item.Created.Weenie) };

    private static WeenieHeaderFlags Flag(WeenieHeaderFlags header, WeenieHeaderFlags flag, bool on) => on ? header | flag : header & ~flag;

    /// <summary>
    /// The weenie description fields that are int qualities (ACE <c>PropertyInt</c>): ClothingPriority 4, EncumbranceVal 5 (Burden), ItemsCapacity 6, ContainersCapacity 7, ValidLocations 9,
    /// CurrentWieldedLocation 10 (0 clears it), MaxStackSize 11, StackSize 12, UiEffects 18, Value 19, MaxStructure 91, Structure 92. Any other int has no field and is not mirrored.
    /// </summary>
    private static void MirrorInt(WorldObject item, uint property, int value)
    {
        var u = unchecked((uint)value);
        var s = (ushort)Math.Clamp(value, 0, ushort.MaxValue);
        var b = (byte)Math.Clamp(value, 0, byte.MaxValue);
        switch (property)
        {
            case 4:
                Mirror(item, w => w with { Priority = u, Header = w.Header | WeenieHeaderFlags.Priority });
                break;
            case 5:
                Mirror(item, w => w with { Burden = s, Header = w.Header | WeenieHeaderFlags.Burden });
                break;
            case 6:
                Mirror(item, w => w with { ItemsCapacity = b, Header = w.Header | WeenieHeaderFlags.ItemsCapacity });
                break;
            case 7:
                Mirror(item, w => w with { ContainersCapacity = b, Header = w.Header | WeenieHeaderFlags.ContainersCapacity });
                break;
            case 9:
                Mirror(item, w => w with { ValidLocations = u, Header = w.Header | WeenieHeaderFlags.ValidLocations });
                break;
            case 10:
                Mirror(item, w => w with { CurrentlyWieldedLocation = value == 0 ? null : u, Header = Flag(w.Header, WeenieHeaderFlags.CurrentlyWieldedLocation, value != 0) });
                break;
            case 11:
                Mirror(item, w => w with { MaxStackSize = s, Header = w.Header | WeenieHeaderFlags.MaxStackSize });
                break;
            case 12:
                Mirror(item, w => w with { StackSize = s, Header = w.Header | WeenieHeaderFlags.StackSize });
                break;
            case 18:
                Mirror(item, w => w with { UiEffects = u, Header = w.Header | WeenieHeaderFlags.UiEffects });
                break;
            case 19:
                Mirror(item, w => w with { Value = u, Header = w.Header | WeenieHeaderFlags.Value });
                break;
            case 91:
                Mirror(item, w => w with { MaxStructure = s, Header = w.Header | WeenieHeaderFlags.MaxStructure });
                break;
            case 92:
                Mirror(item, w => w with { Structure = s, Header = w.Header | WeenieHeaderFlags.Structure });
                break;
        }
    }
}

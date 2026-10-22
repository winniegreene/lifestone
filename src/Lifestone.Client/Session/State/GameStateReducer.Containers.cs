using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.State;

/// <summary>
/// The container model (M4 2.1 s1): GameEvents 0x0196 ViewContents and 0x0052 CloseGroundContainer, the side packs' contents and the one viewed ground container (a corpse or chest).
/// </summary>
/// <remarks>
/// <para><b>The order ACE really sends</b> (capture.md C1 to C4; where it and the spec disagree, the capture wins). The 0196 names guids whose F745 has not arrived: at login one 0196 per side pack in the first
/// event bundle, before the F746 and before every F745; on a pack's arrival a 0196 {pack, 0 entries} before the pack's 0022 and F745; for a corpse the 0196, the UseDone, then the F745 of each item (the stale
/// objects of an earlier open are resent). A view therefore holds the guids and resolves each as its F745 lands (<see cref="NoteObjectArrived"/>), publishing
/// <see cref="ContainerContentsChanged"/> whenever the set or its resolution changes. A close is a 0052 (with or without a UseDone in the tick); ACE sends no F747 for the items of a closed container, so their
/// objects stay in the world table and a re-open's 0196 replaces the entry list.</para>
/// <para>Where ACE's writers are: <c>Container.cs</c> 744-809 (the open: an earlier open container is closed first, so a second open replaces the first), 558-570 (the insert and shift of
/// <c>PlacementPosition</c>, which the model's index lists do without a message), 853-875 (a chest's close after half its Off animation). Retail's handler of 0196 (the container UI's contents
/// request reply) is not located in the 11.6096 symbol table: <c>TODO(m4-retail-handler-0196)</c>.</para>
/// <para>Which container a 0196 is for: the player's own id is ignored; one of the player's side packs takes the entries as its contents (<see cref="PlayerInventory.ItemsIn"/>), the foci type kept; any other
/// container with a known object is the viewed ground container; a guid with no object yet is held until its 0022 (a pack that joins the inventory) or its F745 decides.</para>
/// </remarks>
internal sealed partial class GameStateReducer
{
    /// <summary>GE 0x0196: the contents of a container, in slot order. See the class remarks.</summary>
    public void OnViewContents(in InboundContext context, in GameEventHeader header, in ViewContentsEvent message)
    {
        var self = _state.Self;
        var inventory = self.Inventory;
        inventory.Owner = self.Guid;
        var container = message.Container;
        var entries = new InventoryEntry[message.Entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i] = new InventoryEntry(message.Entries[i].Guid, message.Entries[i].ContainerType);
        }

        if (self.Guid != 0 && container == self.Guid)
        {
            if (_log.IsEnabled(LogSeverity.Debug))
            {
                _log.Debug(SessionLog.Category, "a ViewContents for the player's own pack is ignored (the description names the main pack)", new LogField("entries", entries.Length));
            }

            return;
        }

        if (inventory.IsKnownContainer(container))
        {
            ApplySidePackView(container, entries);
            return;
        }

        if (_state.Objects.TryGet(container, out var known))
        {
            if (self.Guid != 0 && known.ContainerId == self.Guid)
            {
                // The pack's F745 came first and the model does not list it yet: it joins as a side pack now.
                inventory.AddKnownSidePack(new InventoryEntry(container, ContainerTypeOf(known)), int.MaxValue);
                Changed();
                Publish(new InventoryChanged(InventoryChangeKind.Added, container, self.Guid, inventory.Find(container).Placement));
                ApplySidePackView(container, entries);
                return;
            }

            OpenGround(container, entries);
            return;
        }

        _state.Containers.Pending[container] = entries;
    }

    /// <summary>GE 0x0052: the viewed ground container closed (the server's answer to a Use on it or to the client's 0x0195). The stale item objects stay in the world table.</summary>
    public void OnCloseGroundContainer(in InboundContext context, in GameEventHeader header, in CloseGroundContainerEvent message)
    {
        var views = _state.Containers;
        views.Pending.Remove(message.Container);
        if (views.Viewed is { } viewed && viewed.Guid == message.Container)
        {
            views.Viewed = null;
            Changed();
            Publish(new ContainerClosed(message.Container));
            return;
        }

        if (_log.IsEnabled(LogSeverity.Debug))
        {
            _log.Debug(SessionLog.Category, "a CloseGroundContainer for a container that is not viewed", new LogField("container", message.Container));
        }
    }

    private void ApplySidePackView(uint pack, InventoryEntry[] entries)
    {
        var inventory = _state.Self.Inventory;
        if (!inventory.SetContents(pack, entries))
        {
            return;
        }

        _state.Containers.Pending.Remove(pack);
        _state.Containers.MarkNamed(pack);
        Changed();
        Publish(new ContainerContentsChanged(pack));
    }

    /// <summary>A pack just joined the inventory by a 0022: a 0196 that was held for it names its contents now.</summary>
    private void ApplyPendingPackView(uint pack)
    {
        if (_state.Containers.Pending.TryGetValue(pack, out var entries) && _state.Self.Inventory.IsKnownContainer(pack))
        {
            ApplySidePackView(pack, entries);
        }
    }

    private void OpenGround(uint container, InventoryEntry[] entries)
    {
        var views = _state.Containers;
        views.Pending.Remove(container);
        var wasOpen = false;
        if (views.Viewed is { } earlier)
        {
            if (earlier.Guid == container)
            {
                wasOpen = true;
            }
            else
            {
                // ACE closes the earlier container before it opens the next (Container.cs 744-809): its 0052 may not have been seen.
                views.Viewed = null;
                Publish(new ContainerClosed(earlier.Guid));
            }
        }

        var view = new ContainerView(container, isGround: true, contentsNamed: true);
        foreach (var entry in entries)
        {
            view.Add(new ContainerViewEntry(entry.Guid, entry.ContainerType, Resolved: false));
        }

        views.Viewed = view;
        Changed();
        if (wasOpen)
        {
            Publish(new ContainerContentsChanged(container));
        }
        else
        {
            Publish(new ContainerOpened(container));
        }
    }

    /// <summary>
    /// An F745 (new or refreshed) landed for <paramref name="guid"/>: a 0196 that waited for the object is decided (a pack of the player's, or a ground container), and an entry of the viewed container or of a side
    /// pack is resolved.
    /// </summary>
    private void NoteObjectArrived(uint guid)
    {
        var views = _state.Containers;
        if (!_state.Objects.TryGet(guid, out var obj))
        {
            return;
        }

        if (views.Pending.Remove(guid, out var entries))
        {
            var self = _state.Self;
            if (self.Guid != 0 && obj.ContainerId == self.Guid)
            {
                if (!self.Inventory.IsKnownContainer(guid))
                {
                    self.Inventory.AddKnownSidePack(new InventoryEntry(guid, ContainerTypeOf(obj)), int.MaxValue);
                    Changed();
                    Publish(new InventoryChanged(InventoryChangeKind.Added, guid, self.Guid, self.Inventory.Find(guid).Placement));
                }

                ApplySidePackView(guid, entries);
            }
            else
            {
                OpenGround(guid, entries);
            }
        }

        if (views.Viewed is { } viewed && viewed.Resolve(guid))
        {
            Changed();
            Publish(new ContainerContentsChanged(viewed.Guid));
        }

        var place = _state.Self.Inventory.Find(guid);
        if (place.Kind == InventoryPlaceKind.InSidePack)
        {
            PublishSidePackChanged(place.Container);
        }
    }

    /// <summary>A side pack's set or resolution changed.</summary>
    private void PublishSidePackChanged(uint container)
    {
        if (container != 0 && container != _state.Self.Inventory.Owner && _state.Self.Inventory.IsKnownContainer(container))
        {
            Publish(new ContainerContentsChanged(container));
        }
    }

    /// <summary>
    /// A 0022 or a leave moved <paramref name="item"/> into <paramref name="container"/> (-1 placement: out of the player's holdings into a container the model does not know). The viewed ground container loses
    /// the entry when the item went elsewhere (a pick-up from a corpse) and gains it when the item went in (a put into a chest), at the slot the server named, later entries shifting by one without a message
    /// (<c>Container.cs</c> 558-570).
    /// </summary>
    private void MoveInViewedContainer(uint item, uint container, int placement)
    {
        if (_state.Containers.Viewed is not { } viewed)
        {
            return;
        }

        var removed = container != viewed.Guid && viewed.Remove(item);
        var added = false;
        if (container == viewed.Guid)
        {
            viewed.Remove(item);
            viewed.Add(new ContainerViewEntry(item, ContainerTypeOfKnown(item), _state.Objects.TryGet(item, out _)), placement);
            added = true;
        }

        if (removed || added)
        {
            Changed();
            Publish(new ContainerContentsChanged(viewed.Guid));
        }
    }

    private uint ContainerTypeOfKnown(uint guid) => _state.Objects.TryGet(guid, out var o) ? ContainerTypeOf(o) : 0u;

    /// <summary>An F747 for <paramref name="guid"/>: the viewed container itself closes, a viewed entry leaves, a side pack's pending view is dropped.</summary>
    private void ForgetInViews(uint guid)
    {
        var views = _state.Containers;
        views.Pending.Remove(guid);
        if (views.Viewed is not { } viewed)
        {
            return;
        }

        if (viewed.Guid == guid)
        {
            views.Viewed = null;
            Changed();
            Publish(new ContainerClosed(guid));
        }
        else if (viewed.Remove(guid))
        {
            Changed();
            Publish(new ContainerContentsChanged(viewed.Guid));
        }
    }
}

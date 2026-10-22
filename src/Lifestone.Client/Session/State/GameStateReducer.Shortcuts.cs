using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.State;

/// <summary>
/// The shortcut bar (M4 3.2 h1): <see cref="SelfState.Shortcuts"/> from the PlayerDescription's list, the client's own AddShortcut and RemoveShortcut, and the removal of a row whose item leaves the player's holdings.
/// </summary>
/// <remarks>
/// <para>Retail (11.6096): <c>gmToolbarUI::UpdateFromPlayerDesc</c> 0x004C04A0 empties the toolbar and adds every row of the player module's list that names an object (18 slots, no check that the object is held);
/// <c>gmToolbarUI::AddShortcut</c> 0x004BE630 and <c>RemoveShortcut</c> 0x004BE0E0 change a slot and, when asked to broadcast, send the action and change the player module's list; <c>gmToolbarUI::RecvNotice_ServerSaysMoveItem</c>
/// 0x004BFF40 removes the slot (with a broadcast) when its item moved and the player no longer owns it. ACE never removes a row itself (<c>Player_Character.cs</c> 245-262), which is OpenAC #219: an item that left stayed on the bar for good.</para>
/// <para>The sends are not made here: the reducer only changes the model and publishes <see cref="ShortcutsChanged"/>; the Action API sends 019D once for each <see cref="ShortcutChange.ItemLeft"/>.</para>
/// </remarks>
internal sealed partial class GameStateReducer
{
    private bool _shortcutIgnoredNoted;
    private bool _shortcutMissingNoted;

    /// <summary>The description's shortcut list (the one call from <c>OnPlayerDescription</c>, after the inventory is applied so a missing object can be told).</summary>
    private void ApplyDescriptionShortcuts(PlayerDescriptionMsg message)
    {
        var self = _state.Self;
        var bar = self.Shortcuts;
        var ignored = bar.ApplyDescription(message);
        if (ignored > 0 && !_shortcutIgnoredNoted)
        {
            _shortcutIgnoredNoted = true;
            _log.Warn(SessionLog.Category, "the description's shortcut list names a slot the bar does not have (the bar has 18); the row is ignored", new LogField("rows", ignored));
        }

        if (!_shortcutMissingNoted && self.Inventory.IsKnown && self.Inventory.ListsComplete)
        {
            for (var slot = 0; slot < ShortcutBar.SlotCount; slot++)
            {
                if (bar.IsMissing(slot))
                {
                    _shortcutMissingNoted = true;
                    _log.Info(
                        SessionLog.Category,
                        "TODO(m4-shortcut-missing-at-login): a shortcut names an object the player does not hold (yet: a side pack's contents may follow); the row stays and reads missing; whether retail drops it at login is unread",
                        new LogField("slot", slot),
                        new LogField("object", bar[slot].ObjectId));
                    break;
                }
            }
        }

        Changed();
        Publish(new ShortcutsChanged(ShortcutChange.Description, -1, 0, 0));
    }

    /// <summary>The client sent AddShortcut: the slot holds the object at once (the player module's list changes as retail's <c>AddShortcut</c> does). Publishes <see cref="ShortcutChange.Added"/>.</summary>
    internal void ApplyShortcutAdded(int slot, uint objectId)
    {
        var bar = _state.Self.Shortcuts;
        if (!ShortcutBar.InRange(slot))
        {
            return;
        }

        bar.Set(slot, new ShortcutSlot(objectId, 0));
        Changed();
        Publish(new ShortcutsChanged(ShortcutChange.Added, slot, objectId, 0));
    }

    /// <summary>The client sent RemoveShortcut: the slot is empty at once. Publishes <see cref="ShortcutChange.Removed"/> when the slot held something.</summary>
    internal void ApplyShortcutRemoved(int slot)
    {
        var bar = _state.Self.Shortcuts;
        var was = bar[slot];
        if (was.IsEmpty)
        {
            return;
        }

        bar.ClearSlot(slot);
        Changed();
        Publish(new ShortcutsChanged(ShortcutChange.Removed, slot, was.ObjectId, was.SpellId));
    }

    /// <summary>
    /// An object left the player's holdings (0024, 019A, a 0022 into a container that is not the player's, an F747 of a held object): its row leaves the bar (OpenAC #219; <c>gmToolbarUI::RecvNotice_ServerSaysMoveItem</c>, 0x004BFF40),
    /// whether or not the model held it (the message says it is gone). Publishes <see cref="ShortcutChange.ItemLeft"/> for each slot it was on (the Action API sends the 019D). A move inside the player's own packs does not come here.
    /// </summary>
    private void EvictShortcuts(uint guid)
    {
        var bar = _state.Self.Shortcuts;
        for (var slot = bar.SlotOf(guid); slot >= 0; slot = bar.SlotOf(guid))
        {
            bar.ClearSlot(slot);
            Changed();
            Publish(new ShortcutsChanged(ShortcutChange.ItemLeft, slot, guid, 0));
        }
    }
}

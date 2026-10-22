using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions;

/// <summary>
/// How <see cref="IActionApi.UseShortcut"/> tells what a shortcut's object is (M4 3.2 h1). Retail's <c>gmToolbarUI::UseShortcut</c> (11.6096 0x004BDFE0) hands the slot's object to <c>ItemHolder::UseObject</c>, which reads the weenie's own use rules; the
/// client here has the CreateObject's weenie description, which carries no WeenieType and no BoosterEnum (ACE's Healer is WeenieType 28, a potion is Food with a BoosterEnum), so the kit and the potion are told by weenie class and name.
/// <c>TODO(m4-shortcut-classify)</c>: the sets are the vendors' stock (facts.md: Handy 628 and Adept 629 kits; Potions of Healing 377, Stamina 378, Mana 379, the Health Draught 2457, the Mana Draught 2460, the Stamina Tincture 27326), the item type Food (0x20) and the
/// names "... Healing Kit" and "Potion of ..."; an item of another class that is a kit or a potion is used with plain Use until the appraisal model (3.2 s2) can read its WeenieType.
/// </summary>
internal static class ShortcutRouting
{
    /// <summary>ACE <c>ItemType.Food</c>.</summary>
    internal const uint ItemTypeFood = 0x20;

    private static readonly HashSet<uint> KitWcids = [628, 629];
    private static readonly HashSet<uint> DrinkWcids = [377, 378, 379, 2457, 2460, 27326];

    /// <summary>What a shortcut does for this object.</summary>
    internal enum Route
    {
        /// <summary>A healing kit: <see cref="IActionApi.UseWithTarget"/> on the player.</summary>
        Kit,

        /// <summary>A potion or food: <see cref="IActionApi.Drink"/>.</summary>
        Drink,

        /// <summary>Anything else: <see cref="IActionApi.Use"/>.</summary>
        Use,
    }

    /// <summary>The route for <paramref name="item"/>.</summary>
    internal static Route Of(WorldObject item)
    {
        var weenie = item.Weenie;
        if (KitWcids.Contains(weenie.WeenieClassId) || weenie.Name.EndsWith(" Healing Kit", StringComparison.OrdinalIgnoreCase))
        {
            return Route.Kit;
        }

        if (DrinkWcids.Contains(weenie.WeenieClassId) || (weenie.ItemType & ItemTypeFood) != 0 || weenie.Name.StartsWith("Potion of ", StringComparison.OrdinalIgnoreCase))
        {
            return Route.Drink;
        }

        return Route.Use;
    }
}

/// <summary>
/// <see cref="IActionApi.AddShortcut"/> (M4 3.2 h1): GameAction 0x019C {slot, item, spell 0}, as retail's <c>CM_Character::Event_AddShortCut</c> sends it (11.6096 0x006A1CD0) from <c>gmToolbarUI::AddShortcut</c> (0x004BE630) and ACE's
/// <c>Player.HandleActionAddShortcut</c> reads it (<c>Player_Character.cs</c> 245-262). ACE answers nothing and keeps the row for the next login, so the action completes as <see cref="ActionOutcomeKind.Sent"/> and the model changes as it sends.
/// <para>The order of the sends, as <c>CreateShortcutToItem</c> (0x004BE750) and the drop handler do: an item already on the bar leaves its old slot first (RemoveShortcut), an occupant of the target slot is removed (RemoveShortcut) before the new row is added, then the
/// occupant is added again at the first empty slot to the right of the target (<c>GetFirstEmptyShortcutToTheRightOf</c>, 0x004BE1F0; with none free it stays off the bar). The item already on the target slot changes nothing and sends nothing.</para>
/// <para>The slot <see cref="ShortcutBar.AutoSlot"/> is the first empty one; an item already on the bar is then refused ("There is already a shortcut to the ..."), as retail's no-index path does.</para>
/// <para><c>TODO(m4-shortcut-eligible-flags)</c>: <c>gmToolbarUI::IsShortcutEligible</c> (0x004BDE60) also tests two bits of the weenie (the description's stuck flag, 0x4, and a virtual call that answers for objects that cannot be carried) whose meaning the decompile does not name;
/// the rule built here is the readable part: the player holds the item, it is not a pyreal stack (<c>ACCWeenieObject::IsCoinstack</c>, 0x004BDEE0: the coin weenie, wcid 273; where retail applies it is unread), and it is not the vendor's stock (which the player does not hold).</para>
/// </summary>
internal sealed class AddShortcutTracker(ActionHandle handle, int slot, uint item) : ActionTracker(handle, ActionLane.Immediate, double.PositiveInfinity)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x019C AddShortcut slot=" + slot + " item=" + ActionText.Hex(item);

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api)
    {
        if (WorldTrackers.NotInWorld(api) is { } notInWorld)
        {
            return notInWorld;
        }

        var state = api.State;
        if (!state.Self.Inventory.Contains(item))
        {
            return "You are not carrying the item (IsShortcutEligible: only an item the player holds)";
        }

        if (state.Objects.TryGet(item, out var o) && o.Weenie.WeenieClassId == DeathDropModel.CoinWcid)
        {
            return "You cannot make a shortcut to a stack of pyreals (ACCWeenieObject::IsCoinstack)";
        }

        var bar = state.Self.Shortcuts;
        if (slot == ShortcutBar.AutoSlot)
        {
            if (bar.SlotOf(item) >= 0)
            {
                return "There is already a shortcut to the item";
            }

            if (bar.FirstEmpty() < 0)
            {
                return "There are no free shortcut slots";
            }
        }

        return null;
    }

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        var bar = api.State.Self.Shortcuts;
        var target = slot == ShortcutBar.AutoSlot ? bar.FirstEmpty() : slot;
        if (!ShortcutBar.InRange(target))
        {
            return new ActionOutcome(ActionOutcomeKind.Refused, 0, "There are no free shortcut slots");
        }

        api.NoteShortcutEligibilityGap();
        if (bar[target].ObjectId == item)
        {
            return new ActionOutcome(ActionOutcomeKind.Sent, 0, "the item is on slot " + target + " already; nothing sent");
        }

        var detail = new System.Text.StringBuilder("slot " + target.ToString(CultureInfo.InvariantCulture));
        var from = bar.SlotOf(item);
        if (from >= 0)
        {
            session!.SendRemoveShortcut((uint)from);
            api.Reducer.ApplyShortcutRemoved(from);
            detail.Append("; moved from slot ").Append(from);
        }

        var occupant = bar[target];
        if (!occupant.IsEmpty)
        {
            session!.SendRemoveShortcut((uint)target);
            api.Reducer.ApplyShortcutRemoved(target);
        }

        session!.SendAddShortcut((uint)target, item, 0);
        api.Reducer.ApplyShortcutAdded(target, item);
        if (occupant.IsItem)
        {
            var to = bar.FirstEmptyToTheRightOf(target);
            if (to >= 0)
            {
                session.SendAddShortcut((uint)to, occupant.ObjectId, 0);
                api.Reducer.ApplyShortcutAdded(to, occupant.ObjectId);
                detail.Append("; the occupant moved to slot ").Append(to);
            }
            else
            {
                detail.Append("; the occupant left the bar (no free slot)");
            }
        }

        return new ActionOutcome(ActionOutcomeKind.Sent, 0, detail.ToString());
    }
}

/// <summary>
/// <see cref="IActionApi.RemoveShortcut"/> (M4 3.2 h1): GameAction 0x019D {slot}, as retail's <c>CM_Character::Event_RemoveShortCut</c> sends it (11.6096 0x006A25E0, from <c>gmToolbarUI::RemoveShortcutInSlotNum</c> 0x004BE180) and ACE's
/// <c>Player.HandleActionRemoveShortcut</c> reads it. ACE answers nothing, so the action completes as <see cref="ActionOutcomeKind.Sent"/> and the model changes as it sends. An empty slot sends nothing (retail's <c>RemoveShortcutInSlotNum</c> finds no object).
/// </summary>
internal sealed class RemoveShortcutTracker(ActionHandle handle, int slot) : ActionTracker(handle, ActionLane.Immediate, double.PositiveInfinity)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x019D RemoveShortcut slot=" + slot;

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) =>
        WorldTrackers.NotInWorld(api) ?? (api.State.Self.Shortcuts[slot].IsEmpty ? "nothing is on shortcut slot " + slot.ToString(CultureInfo.InvariantCulture) : null);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        var was = api.State.Self.Shortcuts[slot];
        session!.SendRemoveShortcut((uint)slot);
        api.Reducer.ApplyShortcutRemoved(slot);
        return new ActionOutcome(ActionOutcomeKind.Sent, 0, "slot " + slot.ToString(CultureInfo.InvariantCulture) + " (object " + ActionText.Hex(was.ObjectId) + ")");
    }
}

/// <summary>
/// The handle of a <see cref="IActionApi.UseShortcut"/> that was refused or is not built: it is decided at the call (the early outcome of <see cref="ActionApi.Accept"/>) and never queued, so it sends nothing.
/// </summary>
internal sealed class UseShortcutTracker(ActionHandle handle) : ActionTracker(handle, ActionLane.Immediate, double.PositiveInfinity)
{
    /// <inheritdoc/>
    internal override string WouldSend => "nothing (a refused shortcut use)";

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api) =>
        throw new InvalidOperationException("TODO(state): a refused shortcut use was queued");
}

/// <summary>
/// OpenAC #219 (M4 3.2 h1): the shortcut rows whose item left the player's holdings (<see cref="ShortcutChange.ItemLeft"/>), waiting for their one RemoveShortcut (GameAction 0x019D). ACE never removes the row itself (<c>Player_Character.cs</c> 245-262),
/// so without the send the row came back at the next login naming an item the player no longer had. Retail sends it from <c>gmToolbarUI::RecvNotice_ServerSaysMoveItem</c> (11.6096 0x004BFF40) through <c>RemoveShortcutInSlotNum</c> with the broadcast on.
/// The Action API flushes the list once per update, after the tick's messages: the row left the model when the message was applied, so a second message for the same item finds no row and sends nothing.
/// </summary>
internal sealed class ShortcutEvictions
{
    private readonly List<(int Slot, uint Object)> _pending = [];

    /// <summary>The bus subscription: a row that left with its item is queued for its send.</summary>
    internal void OnChanged(in ShortcutsChanged e)
    {
        if (e.Change == ShortcutChange.ItemLeft)
        {
            _pending.Add((e.Slot, e.ObjectId));
        }
    }

    /// <summary>Sends the queued removals on the network session, or on the offline stand-in's when its world answers. A session that cannot take them is logged once per removal and the row stays off the model.</summary>
    internal void Flush(ActionApi api)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var session = api.Link is { } link ? link.Session : api.Reducer.LocalResponder is { World: not null } responder ? responder.Session : null;
        foreach (var (slot, item) in _pending)
        {
            if (session is null)
            {
                api.Log.Info(SessionLog.Category, "shortcut slot " + slot.ToString(CultureInfo.InvariantCulture) + ": its item left the inventory; no network session to send RemoveShortcut on (offline)", new LogField("object", item));
                continue;
            }

            try
            {
                session.SendRemoveShortcut((uint)slot);
                api.Log.Info(SessionLog.Category, "shortcut slot " + slot.ToString(CultureInfo.InvariantCulture) + ": its item left the inventory; RemoveShortcut sent (OpenAC #219)", new LogField("object", item));
            }
            catch (Exception e) when (e is InvalidOperationException or NotSupportedException or ObjectDisposedException)
            {
                api.Log.Warn(SessionLog.Category, "shortcut slot " + slot.ToString(CultureInfo.InvariantCulture) + ": its item left the inventory but RemoveShortcut could not be sent: " + e.Message, new LogField("object", item));
            }
        }

        _pending.Clear();
    }

    /// <summary>Drops the queue (the session was reset).</summary>
    internal void Clear() => _pending.Clear();
}

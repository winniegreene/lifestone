// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Creature_Equipment.cs (TryEquipObjectWithBroadcasting, 354-369: a wielded item with a ParentLocation is
// announced by a ParentEvent) and Source/ACE.Server/Network/GameMessages/Messages/GameMessageParentEvent.cs (AGPL-3.0)
// [with changes: the stand-in has no weenie ParentLocation (TODO(m3-standin-weenies)), so the location comes from the equip mask].
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.StandIn;

// M3 4.1 fix A (review finding R6): a wield of a held item sends the parent event, as ACE does, so the avatar set gets the item as the player's child and the first-person
// fade is read against the real wand. Closes TODO(m3-standin-wield-parent).
internal sealed partial class StandInWorld
{
    /// <summary>The parent location of a wielded weapon or caster in the hand (<c>ParentLocation.RightHand</c>).</summary>
    private const int RightHand = 1;

    /// <summary>The parent location of a wielded shield (<c>ParentLocation.Shield</c>).</summary>
    private const int ShieldLocation = 3;

    /// <summary>
    /// The ParentEvent (F749) ACE broadcasts when an item with a ParentLocation is equipped: it comes before the wielder zeros and the wield event (the broadcast is queued inside
    /// <c>TryEquipObjectWithBroadcasting</c>, the player's own sends after it). Its instance stamp is the player's current one and its position stamp the child's next one
    /// (<c>GameMessageParentEvent</c>). Worn armour has no parent location and sends none.
    /// </summary>
    private void ParentEventNow(IAcSessionSink sink, uint item, uint mask)
    {
        var location = (mask & (uint)EquipMask.Shield) != 0 ? ShieldLocation
            : (mask & (uint)(EquipMask.MeleeWeapon | EquipMask.MissileWeapon | EquipMask.Held)) != 0 ? RightHand
            : 0;
        if (location == 0)
        {
            return;
        }

        var self = _reducer.State.Self.Guid;
        if (!_reducer.State.Objects.TryGet(self, out var player) || !_reducer.State.Objects.TryGet(item, out var child))
        {
            return;
        }

        AnswerLog.Add("F749");
        var childPosition = unchecked((ushort)(child.Stamps.Position + 1));
        sink.OnParentEvent(Context(sink), new ParentEventMsg(self, item, location, 0, player.Stamps.Instance, childPosition));
    }
}

using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

// M3 4.2 p6 follow-up: the player wears what it wields. ACE broadcasts an ObjDescEvent (F625) with the whole description when worn clothing or armour changes (Creature_Equipment.cs 365 after the parent event,
// 438 after a dequip); the stand-in does the same through the host's dresser (it has the dats). Retail applies it with SmartBox::HandleObjDescEvent (11.6096 0x00453380).
internal sealed partial class StandInWorld
{
    private const uint ObjDescEventTag = 0xF625;

    /// <summary>The ObjDescEvents sent (the tests read it).</summary>
    internal int ObjDescEvents { get; private set; }

    /// <summary>
    /// Sends the player's ObjDescEvent for what it wears now, when a piece worn on the body is in <paramref name="piece"/> or has just left; no dresser: the look stays
    /// (<c>TODO(m3-standin-objdesc-on-wield)</c>, logged once).
    /// </summary>
    private void SendLook(IAcSessionSink sink, StandInWorn? piece)
    {
        if (piece is null || !IsBodyWear(piece.Value))
        {
            return;
        }

        if (_visit.Dresser is not { } dresser)
        {
            Placeholder("m3-standin-objdesc-on-wield", "ACE broadcasts an ObjDescEvent (F625) when worn clothing or armour changes (Creature_Equipment.cs 365, 438); this host gave the stand-in no dresser (no ClothingTables), so the player keeps the look it was created with");
            return;
        }

        var self = _reducer.State.Self.Guid;
        if (!_reducer.State.Objects.TryGet(self, out var player))
        {
            return;
        }

        var worn = new List<StandInWorn>();
        foreach (var entry in _wielded.Values)
        {
            if (entry.Worn is { } w && IsBodyWear(w))
            {
                worn.Add(w);
            }
        }

        ObjDescEvents++;
        AnswerLog.Add("F625");
        sink.OnObjDescEvent(Context(sink), new ObjDescEventMsg(self, StandInHumanLook.FromObjDesc(dresser.Player(worn)), player.Stamps.Instance, unchecked((ushort)(player.Stamps.ObjDesc + 1))));
    }

    /// <summary>A piece that covers the body (armour or clothing); a held caster has a ClothingBase of its own art but is not worn (ACE counts only the Clothing, Armor and Cloak locations).</summary>
    private static bool IsBodyWear(StandInWorn piece) => piece.ClothingBase != 0 && piece.ItemSetup != WandSetup;
}

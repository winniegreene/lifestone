using Lifestone.Net.Packets;

namespace Lifestone.Net.Messages;

/// <summary>
/// The shortcut-bar game actions (M4 3.2 s0), each F7B1 {sequence, type} then 32-bit little-endian fields, written from ACE's
/// readers (ACE 95ef426d; not a port, and OpenAC's <c>ShortcutStore.cs</c> is runtime code and was not read):
/// <list type="bullet">
/// <item>0x019C AddShortCut {u32 index, u32 objectId, u16 spellId, u16 layer}: <c>GameActionAddShortcut.cs:5-14</c> calls
/// <c>ReadShortcut</c> (<c>Network/Structure/Shortcut.cs:62-71</c>): <c>ReadUInt32</c> (the index), <c>ReadUInt32</c> (the object) and
/// <c>ReadLayeredSpell</c> (<c>LayeredSpell.cs:30-37</c>: <c>ReadUInt16</c> spell, <c>ReadUInt16</c> layer).</item>
/// <item>0x019D RemoveShortCut {u32 index}: <c>GameActionRemoveShortcut.cs:19-28</c> reads <c>ReadUInt32</c>.</item>
/// </list>
/// ACE answers neither: <c>Player.HandleActionAddShortcut</c> and <c>HandleActionRemoveShortcut</c> (<c>Player_Character.cs:245-262</c>)
/// store or drop the row (the spell is not stored: <c>AddOrUpdateShortcut(index, objectId)</c>), and the list comes back in the next
/// login's player description (option flag 0x1). The index is 0-based on this wire (<c>CharacterExtensions.cs:460-466</c> stores
/// index + 1 and <c>Shortcut.cs:54</c> subtracts one on the way back). Retail's senders are <c>CM_Character::Event_AddShortCut</c> (11.4186 0x006A0EB0, 11.6096 0x006A1CD0) and
/// <c>CM_Character::Event_RemoveShortCut</c> (11.4186 0x006A17C0, 11.6096 0x006A25E0); the toolbar functions that call them
/// are <c>gmToolbarUI::AddShortcut</c> (11.6096 0x004BE630) and <c>RemoveShortcut</c> (0x004BE0E0). Their bodies were not read.
/// </summary>
internal static class ShortcutActionCodecs
{
    /// <summary>
    /// Writes F7B1 {<paramref name="sequence"/>, 0x019C, index, objectId, spell word}. The spell word is the layered spell as
    /// the description lists it (<c>ShortcutDesc.SpellId</c>): the spell id in its low 16 bits and the layer in its high 16
    /// (little-endian that is the u16 spell then the u16 layer); 0 for an item shortcut.
    /// </summary>
    public static void WriteAddShortcut(PacketWriter writer, uint sequence, uint index, uint objectId, uint layeredSpell)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.AddShortCut);
        writer.WriteUInt32(index);
        writer.WriteUInt32(objectId);
        writer.WriteUInt16((ushort)(layeredSpell & 0xFFFF));
        writer.WriteUInt16((ushort)(layeredSpell >> 16));
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0x019D, index}.</summary>
    public static void WriteRemoveShortcut(PacketWriter writer, uint sequence, uint index)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.RemoveShortCut);
        writer.WriteUInt32(index);
    }
}

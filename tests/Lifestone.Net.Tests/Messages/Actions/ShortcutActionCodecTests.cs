using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Session;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.Messages.Actions;

/// <summary>
/// The shortcut-bar game actions (M4 3.2 s0) against hand-built bytes in ACE's reader order: 0x019C AddShortCut
/// (<c>GameActionAddShortcut.cs</c> through <c>ReadShortcut</c>, <c>Shortcut.cs:62-71</c>: u32 index, u32 object, then a
/// LayeredSpell, <c>ReadUInt16</c> spell and <c>ReadUInt16</c> layer) and 0x019D RemoveShortCut (<c>GameActionRemoveShortcut.cs</c>:
/// u32 index), behind the F7B1 envelope. Written from ACE's reader, not ported.
/// </summary>
public sealed class ShortcutActionCodecTests
{
    private const string Envelope = "B1F70000";

    [Fact]
    public void AddShortcut_for_an_item_is_index_object_and_a_zero_spell()
    {
        var body = Build(w => ShortcutActionCodecs.WriteAddShortcut(w, 12, 3, 0x80000042, 0));

        Assert.Equal(Convert.FromHexString(Envelope + "0C000000" + "9C010000" + "03000000" + "42000080" + "0000" + "0000"), body);
    }

    [Fact]
    public void AddShortcut_writes_the_layered_spell_as_a_u16_spell_then_a_u16_layer()
    {
        // The description's word for spell 0x0D5A at layer 2 is 0x00020D5A; on this wire it is 5A0D then 0200.
        var body = Build(w => ShortcutActionCodecs.WriteAddShortcut(w, 12, 17, 0x80000042, 0x00020D5A));

        Assert.Equal(Convert.FromHexString(Envelope + "0C000000" + "9C010000" + "11000000" + "42000080" + "5A0D" + "0200"), body);
        Assert.Equal(24, body.Length);
    }

    [Fact]
    public void RemoveShortcut_is_the_index()
    {
        var body = Build(w => ShortcutActionCodecs.WriteRemoveShortcut(w, 13, 3));

        Assert.Equal(Convert.FromHexString(Envelope + "0D000000" + "9D010000" + "03000000"), body);
    }

    [Fact]
    public void The_opcodes_are_the_ones_ace_registers()
    {
        Assert.Equal([0x019Cu, 0x019Du], [GameActionTypes.AddShortCut, GameActionTypes.RemoveShortCut]);
    }

    [Fact]
    public void The_session_sends_both_on_the_weenie_queue_and_needs_the_world()
    {
        using var closed = new SessionRig();
        Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(() => closed.Session.SendAddShortcut(0, 1, 0)).Message, StringComparison.Ordinal);
        Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(() => closed.Session.SendRemoveShortcut(0)).Message, StringComparison.Ordinal);

        using var rig = new SessionRig();
        rig.ToWorld();
        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        rig.Session.SendAddShortcut(3, 0x80000042, 0);
        rig.Session.SendRemoveShortcut(3);
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        var actions = rig.Tap.Messages
            .Where(m => m.Direction == MessageDirection.Outbound && BitConverter.ToUInt32(m.Body) == MessageOpcodes.GameAction)
            .ToList();
        Assert.Equal(3, actions.Count);
        Assert.All(actions, a => Assert.Equal(MessageQueue.Weenie, a.Queue));
        Assert.Equal(new uint[] { 0x00A1, 0x019C, 0x019D }, actions.Select(a => BitConverter.ToUInt32(a.Body, 8)));
        Assert.Equal(Convert.FromHexString(Envelope + "02000000" + "9C010000" + "03000000" + "42000080" + "00000000"), actions[1].Body);
        Assert.Equal(Convert.FromHexString(Envelope + "03000000" + "9D010000" + "03000000"), actions[2].Body);
    }

    private static byte[] Build(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return writer.ToArray();
    }
}

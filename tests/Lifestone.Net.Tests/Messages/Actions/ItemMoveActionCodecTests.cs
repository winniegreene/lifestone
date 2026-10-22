using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Session;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.Messages.Actions;

/// <summary>
/// The item-move game actions (M4 2.1 s0) against hand-built bytes in ACE's reader order (<c>GameAction*.cs</c> in
/// <c>ACE.Server/Network/GameAction/Actions/</c>): 0x0195 NoLongerViewingContents (u32), 0x001B DropItem (u32), 0x0055
/// StackableSplitToContainer (u32, u32, i32 placement, i32 amount), 0x0056 StackableSplitTo3D (u32, i32), 0x019B
/// StackableSplitToWield (u32, i32 read as EquipMask, i32) and 0x00CD GiveObjectRequest (u32, u32, i32), behind the F7B1
/// envelope {opcode, sequence, type} every send uses. ACE's readers check nothing; the handlers refuse an amount of 0 or less.
/// </summary>
public sealed class ItemMoveActionCodecTests
{
    private const string Envelope = "B1F70000";

    [Fact]
    public void NoLongerViewingContents_is_envelope_and_the_container()
    {
        var body = Build(w => ItemMoveActionCodecs.WriteNoLongerViewingContents(w, 3, 0x7A9B4016));

        Assert.Equal(Convert.FromHexString(Envelope + "03000000" + "95010000" + "16409B7A"), body);
    }

    [Fact]
    public void DropItem_is_envelope_and_the_item()
    {
        var body = Build(w => ItemMoveActionCodecs.WriteDropItem(w, 4, 0x80000042));

        Assert.Equal(Convert.FromHexString(Envelope + "04000000" + "1B000000" + "42000080"), body);
    }

    [Fact]
    public void SplitToContainer_is_stack_container_placement_amount()
    {
        var body = Build(w => ItemMoveActionCodecs.WriteSplitToContainer(w, 5, 0x80000100, 0x50000001, 2, 30));

        Assert.Equal(Convert.FromHexString(Envelope + "05000000" + "55000000" + "00010080" + "01000050" + "02000000" + "1E000000"), body);
    }

    [Fact]
    public void SplitTo3D_is_stack_then_amount()
    {
        var body = Build(w => ItemMoveActionCodecs.WriteSplitTo3D(w, 6, 0x80000100, 10));

        Assert.Equal(Convert.FromHexString(Envelope + "06000000" + "56000000" + "00010080" + "0A000000"), body);
    }

    [Fact]
    public void SplitToWield_is_stack_equip_mask_amount()
    {
        // Any EquipMask: the four bytes are as given (ACE reads them with ReadInt32 and casts).
        var body = Build(w => ItemMoveActionCodecs.WriteSplitToWield(w, 7, 0x80000100, 0x00800000, 25));

        Assert.Equal(Convert.FromHexString(Envelope + "07000000" + "9B010000" + "00010080" + "00008000" + "19000000"), body);
    }

    [Fact]
    public void GiveObject_is_target_item_amount()
    {
        var body = Build(w => ItemMoveActionCodecs.WriteGiveObject(w, 8, 0x80000200, 0x80000100, 1));

        Assert.Equal(Convert.FromHexString(Envelope + "08000000" + "CD000000" + "00020080" + "00010080" + "01000000"), body);
    }

    [Fact]
    public void An_amount_is_signed_on_the_wire_and_sent_as_given_because_ace_checks_it()
    {
        var split = Build(w => ItemMoveActionCodecs.WriteSplitTo3D(w, 1, 1, -1));
        var give = Build(w => ItemMoveActionCodecs.WriteGiveObject(w, 1, 1, 2, 0));

        Assert.Equal(-1, BitConverter.ToInt32(split, 16));
        Assert.Equal(0, BitConverter.ToInt32(give, 20));
    }

    [Fact]
    public void The_opcodes_are_the_ones_ace_registers()
    {
        Assert.Equal(
            [0x0195u, 0x001Bu, 0x0055u, 0x0056u, 0x019Bu, 0x00CDu],
            [GameActionTypes.NoLongerViewingContents, GameActionTypes.DropItem, GameActionTypes.StackableSplitToContainer,
             GameActionTypes.StackableSplitTo3D, GameActionTypes.StackableSplitToWield, GameActionTypes.GiveObjectRequest]);
    }

    [Fact]
    public void The_session_sends_the_six_on_the_weenie_queue_with_consecutive_sequences()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        rig.Session.SendNoLongerViewingContents(0x7A9B4016);
        rig.Session.SendDropItem(0x80000042);
        rig.Session.SendSplitToContainer(0x80000100, 0x50000001, 2, 30);
        rig.Session.SendSplitTo3D(0x80000100, 10);
        rig.Session.SendSplitToWield(0x80000100, 0x00800000, 25);
        rig.Session.SendGiveObject(0x80000200, 0x80000100, 1);
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        var actions = rig.Tap.Messages
            .Where(m => m.Direction == MessageDirection.Outbound && BitConverter.ToUInt32(m.Body) == MessageOpcodes.GameAction)
            .ToList();
        Assert.Equal(7, actions.Count);
        Assert.All(actions, a => Assert.Equal(MessageQueue.Weenie, a.Queue));
        Assert.Equal(new uint[] { 0x00A1, 0x0195, 0x001B, 0x0055, 0x0056, 0x019B, 0x00CD }, actions.Select(a => BitConverter.ToUInt32(a.Body, 8)));
        Assert.Equal(new uint[] { 1, 2, 3, 4, 5, 6, 7 }, actions.Select(a => BitConverter.ToUInt32(a.Body, 4)));
        Assert.Equal(Convert.FromHexString(Envelope + "04000000" + "55000000" + "00010080" + "01000050" + "02000000" + "1E000000"), actions[3].Body);
        Assert.Equal(Convert.FromHexString(Envelope + "07000000" + "CD000000" + "00020080" + "00010080" + "01000000"), actions[6].Body);
    }

    [Fact]
    public void The_sends_need_the_world()
    {
        using var rig = new SessionRig();

        Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(() => rig.Session.SendNoLongerViewingContents(1)).Message, StringComparison.Ordinal);
        Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(() => rig.Session.SendDropItem(1)).Message, StringComparison.Ordinal);
        Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(() => rig.Session.SendSplitToContainer(1, 2, 0, 1)).Message, StringComparison.Ordinal);
        Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(() => rig.Session.SendSplitTo3D(1, 1)).Message, StringComparison.Ordinal);
        Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(() => rig.Session.SendSplitToWield(1, 2, 1)).Message, StringComparison.Ordinal);
        Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(() => rig.Session.SendGiveObject(1, 2, 1)).Message, StringComparison.Ordinal);
    }

    private static byte[] Build(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return writer.ToArray();
    }
}

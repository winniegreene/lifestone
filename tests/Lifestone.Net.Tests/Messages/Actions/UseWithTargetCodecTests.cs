using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Session;
using Lifestone.Net.Tests.Support;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.Messages.Actions;

/// <summary>
/// GameAction 0x0035 UseWithTarget (M4 3.1 s0) against hand-built bytes in ACE's reader order (<c>GameActionUseWithTarget.cs:10-15</c>: a u32 <c>sourceObjectGuid</c>,
/// the item, then a u32 <c>targetObjectGuid</c>), behind the F7B1 envelope {opcode, sequence, type} every send uses. Retail's sender is
/// <c>CM_Inventory::Event_UseWithTargetEvent</c> (11.4186 0x006AC480, 11.6096 0x006AD3E0). It is written from ACE's reader, not ported from OpenAC.
/// </summary>
public sealed class UseWithTargetCodecTests
{
    [Fact]
    public void UseWithTarget_is_envelope_item_then_target_in_twenty_bytes()
    {
        // F7B1, sequence 7, type 0x0035, item (a kit) 0x80000042, target (the player) 0x50000001.
        var body = Build(w => ObjectActionCodecs.WriteUseWithTarget(w, 7, 0x80000042, 0x50000001));

        Assert.Equal(20, body.Length);
        Assert.Equal(Convert.FromHexString("B1F70000" + "07000000" + "35000000" + "42000080" + "01000050"), body);
    }

    [Fact]
    public void The_item_is_first_and_the_target_second()
    {
        var body = Build(w => ObjectActionCodecs.WriteUseWithTarget(w, 1, 0x11111111, 0x22222222));
        var swapped = Build(w => ObjectActionCodecs.WriteUseWithTarget(w, 1, 0x22222222, 0x11111111));

        Assert.Equal(0x11111111u, BitConverter.ToUInt32(body, 12));
        Assert.Equal(0x22222222u, BitConverter.ToUInt32(body, 16));
        Assert.NotEqual(body, swapped);
    }

    [Fact]
    public void Ace_reads_the_item_as_the_source_and_the_target_and_nothing_after()
    {
        var body = Build(w => ObjectActionCodecs.WriteUseWithTarget(w, 9, 0x80000123, 0x50000002));

        Assert.Equal((9u, 0x0035u, 0x80000123u, 0x50000002u, 0), AceClientReaders.ReadUseWithTarget(body));
    }

    [Fact]
    public void The_opcode_is_the_retail_senders_value_and_is_not_use()
    {
        // CM_Inventory::Event_UseWithTargetEvent puts 53 in the type word; Event_UseEvent puts 54.
        Assert.Equal(0x0035u, GameActionTypes.UseWithTarget);
        Assert.NotEqual(GameActionTypes.Use, GameActionTypes.UseWithTarget);
    }

    [Fact]
    public void The_session_sends_it_on_the_weenie_queue_and_the_server_reads_both_guids()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        var ticket = rig.Session.SendUseWithTarget(0x80000042, SessionRig.HeroGuid);
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        var actions = rig.Tap.Messages
            .Where(m => m.Direction == MessageDirection.Outbound && BitConverter.ToUInt32(m.Body) == MessageOpcodes.GameAction)
            .ToList();
        Assert.Equal(2, actions.Count);
        Assert.All(actions, a => Assert.Equal(MessageQueue.Weenie, a.Queue));
        Assert.Equal(new uint[] { 0x00A1, 0x0035 }, actions.Select(a => BitConverter.ToUInt32(a.Body, 8)));
        Assert.Equal(new uint[] { 1, 2 }, actions.Select(a => BitConverter.ToUInt32(a.Body, 4)));
        Assert.Equal(ticket.MessageSequence, actions[1].Sequence);
        Assert.Equal(Convert.FromHexString("B1F70000" + "02000000" + "35000000" + "42000080" + "01000050"), actions[1].Body);

        var handled = rig.ServerSession.HandledMessages.Where(m => BitConverter.ToUInt32(m) == MessageOpcodes.GameAction).ToList();
        Assert.Equal((2u, 0x0035u, 0x80000042u, SessionRig.HeroGuid, 0), AceClientReaders.ReadUseWithTarget(handled[1]));
    }

    [Fact]
    public void The_send_needs_the_world()
    {
        using var rig = new SessionRig();

        Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(() => rig.Session.SendUseWithTarget(1, 2)).Message, StringComparison.Ordinal);
    }

    private static byte[] Build(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return writer.ToArray();
    }
}

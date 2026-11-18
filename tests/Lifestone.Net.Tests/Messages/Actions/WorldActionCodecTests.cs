using System.Buffers.Binary;
using System.Numerics;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Support;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.Messages.Actions;

/// <summary>
/// The world game actions (m2-plan §3.3 N7) in retail's layouts, read back by ACE's reader mirror
/// (<see cref="AceClientReaders.ReadMoveToState"/> and its neighbours, ACE's handlers re-expressed): the docs/protocol.md
/// §10 MoveToState vector byte for byte; <b>defect 1</b> (each RawMotionState action is 8 bytes, so with one action the
/// message is 80 bytes and ACE still finds cell 0xA9B40019, where OpenAC's 4-byte actions made it read 0x42A80000); the
/// 56-byte Jump; AutonomousPosition; Use, QueryHealth, Talk and Tell.
/// </summary>
public sealed class WorldActionCodecTests
{
    /// <summary>docs/protocol.md §10: action sequence 1; hold key Run, WalkForward, forward hold key Run; the §10 position;
    /// sequences 1, 0, 0, 0; contact.</summary>
    private static readonly byte[] MoveToStateVector = Convert.FromHexString(
        "B1F70000010000001CF600000D000000020000000500004502000000" +
        "1900B4A90000A8423333E3400000BC42F4357F3F00000000000000002AAFA0BD" +
        "0100000000000000" + "01000000");

    private static readonly AcPosition Holtburg = new(0xA9B40019, new Vector3(84f, 7.1f, 94f), new Quaternion(0f, 0f, -0.0784591f, 0.996917f));

    private static readonly RawMotionStateWire Running = RawMotionStateWire.Default with { CurrentHoldKey = 2, ForwardCommand = 0x45000005, ForwardHoldKey = 2 };

    [Fact]
    public void MoveToState_matches_the_golden_vector_byte_for_byte()
    {
        var body = Build(w => MovementActionCodecs.WriteMoveToState(w, 1, Running, Holtburg, new ObjSeqs(1, 0, 0, 0), contact: true, standingLongJump: false));

        Assert.Equal(72, body.Length);
        Assert.Equal(MoveToStateVector, body);
    }

    [Fact]
    public void MoveToState_with_one_action_is_80_bytes_and_ace_finds_the_cell()
    {
        var withAction = Running with { Actions = [new RawMotionAction(0x13000087, 5, Autonomous: true, 1f)] };
        var body = Build(w => MovementActionCodecs.WriteMoveToState(w, 2, withAction, Holtburg, new ObjSeqs(1, 2, 3, 4), contact: true, standingLongJump: true));

        Assert.Equal(80, body.Length);
        var read = AceClientReaders.ReadMoveToState(body);
        Assert.Equal(0xA9B40019u, read.Position.Cell);
        Assert.Equal((84f, 7.1f, 94f), (read.Position.X, read.Position.Y, read.Position.Z));
        Assert.Equal((0.996917f, 0f, 0f, -0.0784591f), (read.Position.Qw, read.Position.Qx, read.Position.Qy, read.Position.Qz));
        Assert.Equal(((ushort)1, (ushort)2, (ushort)3, (ushort)4), read.Sequences);
        Assert.Equal(3, read.ContactLongJump);
        Assert.Equal(0, read.Unread);
        var action = Assert.Single(read.State.Commands);
        Assert.Equal(((ushort)0x0087, (ushort)(5 | 0x8000), 1f), action);
        Assert.Equal((2u, 0x45000005u, 2u), (read.State.CurrentHoldKey!.Value, read.State.ForwardCommand!.Value, read.State.ForwardHoldKey!.Value));
        Assert.Equal(2u, read.ActionSequence);
    }

    [Fact]
    public void Four_byte_actions_are_what_made_ace_read_cell_0x42A80000()
    {
        // The counterfactual of defect 1: the same message with the action's speed left out (OpenAC's packer).
        var withAction = Running with { Actions = [new RawMotionAction(0x13000087, 5, Autonomous: true, 1f)] };
        var good = Build(w => MovementActionCodecs.WriteMoveToState(w, 2, withAction, Holtburg, default, true, false));
        var actionEnd = 12 + 4 + 12 + 8;
        byte[] defective = [.. good[..(actionEnd - 4)], .. good[actionEnd..]];

        Assert.Equal(76, defective.Length);
        Assert.Throws<EndOfStreamException>(() => AceClientReaders.ReadMoveToState(defective));
        var cellAt = actionEnd;  // where ACE reads the cell: 4 bytes into the position
        Assert.Equal(0x42A80000u, BinaryPrimitives.ReadUInt32LittleEndian(defective.AsSpan(cellAt)));
    }

    [Fact]
    public void Every_RawMotionState_field_goes_out_when_it_differs_from_its_default()
    {
        var state = new RawMotionStateWire(2, 0x8000003C, 0x45000006, 2, 0.5f, 0x6500000F, 2, 1.25f, 0x6500000D, 2, 1.5f, []);
        var body = Build(w => MovementActionCodecs.WriteMoveToState(w, 9, state, Holtburg, default, contact: false, standingLongJump: false));

        var read = AceClientReaders.ReadMoveToState(body);
        Assert.Equal(0x7FFu, read.State.PackedFlags);
        Assert.Equal(
            new AceClientReaders.AceRawMotionState(0x7FF, 2, 0x8000003C, 0x45000006, 2, 0.5f, 0x6500000F, 2, 1.25f, 0x6500000D, 2, 1.5f, read.State.Commands),
            read.State);
        Assert.Equal(0, read.ContactLongJump);
        Assert.Equal(0, read.Unread);
    }

    [Fact]
    public void A_default_RawMotionState_is_its_flags_word_alone()
    {
        var body = Build(w => MovementActionCodecs.WriteMoveToState(w, 1, RawMotionStateWire.Default, Holtburg, default, true, false));
        Assert.Equal(12 + 4 + 32 + 8 + 1 + 3, body.Length);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12)));
    }

    [Fact]
    public void Thirty_two_actions_are_refused_before_anything_is_written()
    {
        var actions = Enumerable.Range(0, 32).Select(i => new RawMotionAction(0x13000087, (ushort)i, false, 1f)).ToArray();
        var writer = new PacketWriter();
        Assert.Throws<ArgumentException>(() => MovementActionCodecs.WriteMoveToState(writer, 1, Running with { Actions = [.. actions] }, Holtburg, default, true, false));
        Assert.Equal(0, writer.Position);

        var thirtyOne = Build(w => MovementActionCodecs.WriteMoveToState(w, 1, Running with { Actions = [.. actions[..31]] }, Holtburg, default, true, false));
        Assert.Equal(31, AceClientReaders.ReadMoveToState(thirtyOne).State.Commands.Count);
    }

    [Fact]
    public void AutonomousPosition_is_the_position_the_sequences_and_the_contact_byte()
    {
        var body = Build(w => MovementActionCodecs.WriteAutonomousPosition(w, 3, Holtburg, new ObjSeqs(5, 6, 7, 8), contact: true));

        Assert.Equal(12 + 32 + 8 + 1 + 3, body.Length);
        var read = AceClientReaders.ReadAutonomousPosition(body);
        Assert.Equal((3u, 0xA9B40019u, true, 0), (read.ActionSequence, read.Position.Cell, read.Contact, read.Unread));
        Assert.Equal(((ushort)5, (ushort)6, (ushort)7, (ushort)8), read.Sequences);

        var airborne = Build(w => MovementActionCodecs.WriteAutonomousPosition(w, 3, Holtburg, default, contact: false));
        Assert.False(AceClientReaders.ReadAutonomousPosition(airborne).Contact);
    }

    [Fact]
    public void Jump_is_56_bytes_after_the_action_type_and_ace_reads_its_extent_and_velocity()
    {
        var body = Build(w => MovementActionCodecs.WriteJump(w, 4, 0.75f, new Vector3(1f, 2f, 9.5f), Holtburg, new ObjSeqs(1, 2, 3, 4)));

        Assert.Equal(12 + 56, body.Length);
        Assert.Equal(56, MovementActionCodecs.JumpPayloadSize);
        var read = AceClientReaders.ReadJump(body);
        Assert.Equal((0.75f, (1f, 2f, 9.5f)), (read.Extent, read.Velocity));
        Assert.Equal(56 - 32, read.Unread);  // ACE reads 32 bytes and leaves the rest

        // Retail's order: the position follows the velocity, then the four sequences.
        Assert.Equal(0xA9B40019u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12 + 16)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(12 + 16 + 32)));
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(12 + 16 + 32 + 6)));
    }

    [Fact]
    public void Use_and_QueryHealth_carry_one_guid()
    {
        var use = Build(w => ObjectActionCodecs.WriteUse(w, 5, 0x7A9B4002));
        Assert.Equal((5u, 0x0036u, 0x7A9B4002u, 0), AceClientReaders.ReadObjectAction(use));
        Assert.Equal(16, use.Length);

        var query = Build(w => ObjectActionCodecs.WriteQueryHealth(w, 6, 0x80000042));
        Assert.Equal((6u, 0x01BFu, 0x80000042u, 0), AceClientReaders.ReadObjectAction(query));
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("@lifestone status")]
    [InlineData("")]
    public void Talk_is_one_string_ace_reads_back(string text)
    {
        var body = Build(w => ChatActionCodecs.WriteTalk(w, 7, text));
        Assert.Equal((7u, text, 0), AceClientReaders.ReadTalk(body));
    }

    [Fact]
    public void Talk_may_carry_windows_1252_but_not_what_it_lacks()
    {
        var body = Build(w => ChatActionCodecs.WriteTalk(w, 1, "café"));
        Assert.Equal(new byte[] { 4, 0, (byte)'c', (byte)'a', (byte)'f', 0xE9, 0, 0 }, body[12..]);

        var writer = new PacketWriter();
        Assert.Throws<ArgumentException>(() => ChatActionCodecs.WriteTalk(writer, 1, "日本"));
        Assert.Equal(0, writer.Position);
    }

    [Fact]
    public void Tell_sends_the_message_then_the_target()
    {
        var body = Build(w => ChatActionCodecs.WriteTell(w, 8, "Friend", "meet at the lifestone"));
        Assert.Equal((8u, "meet at the lifestone", "Friend", 0), AceClientReaders.ReadTell(body));
    }

    [Theory]
    [InlineData("Friénd", "hi")]
    [InlineData("Friend", "héllo")]
    [InlineData("", "hi")]
    public void Tell_refuses_a_non_ascii_or_empty_field_before_writing(string target, string text)
    {
        var writer = new PacketWriter();
        Assert.Throws<ArgumentException>(() => ChatActionCodecs.WriteTell(writer, 1, target, text));
        Assert.Equal(0, writer.Position);
    }

    [Fact]
    public void TalkDirect_sends_the_message_then_the_target_id_as_ACE_reads_them()
    {
        // F7B1, the sequence, 0x0032, then String16L "tusker" (2 + 6 bytes: no padding) and the id.
        var body = Build(w => ChatActionCodecs.WriteTalkDirect(w, 9, 0x7A9B4F14, "tusker"));
        Assert.Equal(
            new byte[] { 0xB1, 0xF7, 0, 0, 9, 0, 0, 0, 0x32, 0, 0, 0, 6, 0, (byte)'t', (byte)'u', (byte)'s', (byte)'k', (byte)'e', (byte)'r', 0x14, 0x4F, 0x9B, 0x7A },
            body);
        Assert.Equal((9u, "tusker", 0x7A9B4F14u, 0), AceClientReaders.ReadTalkDirect(body));

        // "help" is 2 + 4 bytes, padded to 8 before the id.
        var padded = Build(w => ChatActionCodecs.WriteTalkDirect(w, 10, 0x7A9B4F14, "help"));
        Assert.Equal(new byte[] { 4, 0, (byte)'h', (byte)'e', (byte)'l', (byte)'p', 0, 0, 0x14, 0x4F, 0x9B, 0x7A }, padded[12..]);
        Assert.Equal((10u, "help", 0x7A9B4F14u, 0), AceClientReaders.ReadTalkDirect(padded));
    }

    [Theory]
    [InlineData(0u, "hi")]
    [InlineData(0x7A9B4F14u, "h\u00E9llo")]
    public void TalkDirect_refuses_no_target_or_a_non_ascii_message_before_writing(uint target, string text)
    {
        var writer = new PacketWriter();
        Assert.Throws<ArgumentException>(() => ChatActionCodecs.WriteTalkDirect(writer, 1, target, text));
        Assert.Equal(0, writer.Position);
    }

    [Fact]
    public void SetInscription_sends_the_item_then_the_text_as_ACE_reads_them_D137()
    {
        // ACE GameActionSetInscription: ReadUInt32 (the item), then ReadString16L (the text: a u16 length, the bytes, padded to 4).
        var body = Build(w => ChatActionCodecs.WriteSetInscription(w, 9, 0x80000A07, "For the Den"));
        Assert.Equal(0xF7B1u, BitConverter.ToUInt32(body, 0));
        Assert.Equal(9u, BitConverter.ToUInt32(body, 4));
        Assert.Equal(0x00BFu, BitConverter.ToUInt32(body, 8));
        Assert.Equal(0x80000A07u, BitConverter.ToUInt32(body, 12));
        Assert.Equal(11, BitConverter.ToUInt16(body, 16));
        Assert.Equal("For the Den", System.Text.Encoding.ASCII.GetString(body, 18, 11));
        Assert.Equal(0, (body.Length - 16) % 4);
        Assert.Equal(GameActionTypes.SetInscription, 0x00BFu);

        var cleared = Build(w => ChatActionCodecs.WriteSetInscription(w, 10, 0x80000A07, string.Empty));
        Assert.Equal(0, BitConverter.ToUInt16(cleared, 16)); // an empty text clears the inscription
    }

    private static byte[] Build(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return writer.ToArray();
    }
}

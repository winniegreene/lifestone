using System.Buffers.Binary;
using System.Numerics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages.World;

/// <summary>
/// F74C UpdateMotion and the MovementData body it shares with CreateObject (m2-plan §3.3 N7), against ACE's writers:
/// the header, every interpreted-state field, the 8-byte commands, the MoveTo and TurnTo bodies, and <b>defect 2</b>:
/// with StickToObject and an odd number of u16 fields the sticky GUID follows 2 bytes of pad, as ACE's
/// <c>InterpretedMotionState.Write</c> and retail's <c>InterpretedMotionState::UnPack</c> align.
/// </summary>
public sealed class UpdateMotionCodecTests
{
    [Fact]
    public void The_header_and_a_running_state_round_trip()
    {
        var movement = new AceMovementData { Forward = 0x0007, ForwardSpeed = 2.85f };
        var body = AceWireWriters.UpdateMotion(0x50000001, 3, 41, 7, isAutonomous: true, movement);

        Assert.True(UpdateMotionCodec.TryParse(body, out var update));
        Assert.Equal((0x50000001u, (ushort)3, (ushort)41, (ushort)7, true), (update.Guid, update.InstanceSeq, update.MovementSeq, update.ServerControlSeq, update.IsAutonomous));
        var motion = update.Motion;
        Assert.Equal(MovementType.Interpreted, motion.MovementType);
        Assert.Equal(MotionFlags.None, motion.MotionFlags);
        Assert.Equal((ushort)0x003D, motion.Stance);
        Assert.Equal((ushort)0x0007, motion.Forward);
        Assert.Equal(2.85f, motion.ForwardSpeed);
        Assert.Null(motion.Style);
        Assert.Null(motion.Sidestep);
        Assert.Null(motion.Turn);
        Assert.Null(motion.SidestepSpeed);
        Assert.Null(motion.TurnSpeed);
        Assert.Empty(motion.Commands);
        Assert.Null(motion.StickyGuid);
        ObjectEventCodecTests.AssertExact(body, static b => UpdateMotionCodec.TryParse(b, out _));
    }

    [Fact]
    public void Every_interpreted_field_and_command_round_trips()
    {
        var movement = new AceMovementData
        {
            MotionFlags = 0x2,
            Stance = 0x003C,
            Style = 0x003C,
            Forward = 0x0005,
            Sidestep = 0x000F,
            Turn = 0x000D,
            ForwardSpeed = -0.65f,
            SidestepSpeed = 1.248f,
            TurnSpeed = 1.5f,
            Commands = [new AceMotionItem(0x0087, 12, Autonomous: true, 1f), new AceMotionItem(0x011B, 0x7FFF, Autonomous: false, 0.5f)],
        };
        var body = AceWireWriters.UpdateMotion(0x80000005, 1, 2, 3, isAutonomous: false, movement);

        Assert.True(UpdateMotionCodec.TryParse(body, out var update));
        var motion = update.Motion;
        Assert.False(update.IsAutonomous);
        Assert.Equal(MotionFlags.StandingLongJump, motion.MotionFlags);
        Assert.Equal((0x003C, 0x003C, 0x0005, 0x000F, 0x000D), ((int)motion.Stance, (int)motion.Style!, (int)motion.Forward!, (int)motion.Sidestep!, (int)motion.Turn!));
        Assert.Equal((-0.65f, 1.248f, 1.5f), (motion.ForwardSpeed!.Value, motion.SidestepSpeed!.Value, motion.TurnSpeed!.Value));
        Assert.Equal(new[] { new MotionItem(0x0087, 12, true, 1f), new MotionItem(0x011B, 0x7FFF, false, 0.5f) }, motion.Commands);
        ObjectEventCodecs_AssertExact(body);
    }

    /// <summary>
    /// Defect 2. The u16 fields present decide the pad: an odd count leaves the state 2 bytes short of a multiple of 4,
    /// so ACE writes 2 pad bytes before the sticky GUID, and the GUID must be read after them.
    /// </summary>
    [Theory]
    [InlineData(1, 2)]  // forward only: odd, 2 pad bytes
    [InlineData(2, 0)]  // style and forward: even, no pad
    [InlineData(3, 2)]  // style, forward and turn: odd
    [InlineData(4, 0)]  // all four: even
    [InlineData(0, 0)]  // none
    public void The_sticky_guid_is_read_after_the_align(int u16Fields, int expectedPad)
    {
        var movement = new AceMovementData
        {
            MotionFlags = 0x1,
            Style = u16Fields >= 2 ? (ushort)0x003D : null,
            Forward = u16Fields >= 1 ? (ushort)0x0007 : null,
            Turn = u16Fields >= 3 ? (ushort)0x000D : null,
            Sidestep = u16Fields >= 4 ? (ushort)0x000F : null,
            StickyGuid = 0x80007777,
        };
        var body = AceWireWriters.UpdateMotion(0x80006666, 0, 1, 2, isAutonomous: false, movement);

        // Header 16 bytes, then type, flags, stance (4), the state word (4), the u16 fields, the pad, the GUID.
        Assert.Equal(16 + 4 + 4 + (2 * u16Fields) + expectedPad + 4, body.Length);
        Assert.Equal(0x80007777u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(body.Length - 4)));
        Assert.True(UpdateMotionCodec.TryParse(body, out var update));
        Assert.Equal(0x80007777u, update.Motion.StickyGuid);
        Assert.Equal(MotionFlags.StickToObject, update.Motion.MotionFlags);
        ObjectEventCodecs_AssertExact(body);
    }

    /// <summary>
    /// The layout OpenAC's UpdateMotionTests (lines 583-600 at 1ee5a08) asserted, where the sticky GUID follows a single
    /// u16 field with no pad, is not what ACE writes: here it is refused, where OpenAC read the GUID 2 bytes early.
    /// </summary>
    [Fact]
    public void The_unpadded_layout_defect_2_read_is_refused()
    {
        var body = new byte[4 + 4 + 2 + 6 + 4 + 4 + 2 + 4];
        var p = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(p), 0xF74Cu); p += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(p), 0x80006666u); p += 4;
        p += 2 + 6;                       // instance, movement and server-control sequences, autonomous byte, pad
        body[p++] = 0;                    // interpreted
        body[p++] = 0x1;                  // StickToObject
        p += 2;                           // stance 0
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(p), 0x2u); p += 4;   // forward only
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(p), 0x0007); p += 2;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(p), 0x80007777u);    // the GUID with no pad before it

        Assert.False(UpdateMotionCodec.TryParse(body, out _));

        // The same state as ACE writes it parses, with the GUID 2 bytes later.
        var ace = AceWireWriters.UpdateMotion(0x80006666, 0, 0, 0, false, new AceMovementData { MotionFlags = 0x1, Stance = 0, Forward = 0x0007, StickyGuid = 0x80007777 });
        Assert.Equal(body.Length + 2, ace.Length);
        Assert.True(UpdateMotionCodec.TryParse(ace, out var update));
        Assert.Equal(0x80007777u, update.Motion.StickyGuid);
    }

    [Fact]
    public void Without_StickToObject_nothing_follows_the_align()
    {
        var body = AceWireWriters.UpdateMotion(1, 0, 0, 0, false, new AceMovementData { Forward = 0x0007 });
        Assert.Equal(16 + 4 + 4 + 2 + 2, body.Length);
        Assert.True(UpdateMotionCodec.TryParse(body, out var update));
        Assert.Null(update.Motion.StickyGuid);
    }

    [Fact]
    public void The_command_count_is_retails_five_bits()
    {
        // Bit 12 set above the count (a word ACE never writes): retail masks it off and reads 1 command.
        var body = AceWireWriters.UpdateMotion(1, 0, 0, 0, false, new AceMovementData { Commands = [new AceMotionItem(0x0087, 1, false)] });
        var word = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(20));
        Assert.Equal(1u << 7, word);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(20), word | (1u << 12));
        Assert.True(UpdateMotionCodec.TryParse(body, out var update));
        Assert.Single(update.Motion.Commands);
    }

    [Fact]
    public void MoveToObject_keeps_every_parameter()
    {
        var movement = new AceMovementData
        {
            MovementType = 6,
            Stance = 0x003C,
            TargetGuid = 0x80001111,
            OriginCell = 0xA9B4001A,
            OriginX = 10.5f,
            OriginY = 20.25f,
            OriginZ = 30f,
            ParameterFlags = 0x0001EE0F,
            DistanceToObject = 0.6f,
            MinDistance = 0.1f,
            FailDistance = 50f,
            Speed = 1.25f,
            WalkRunThreshold = 15f,
            DesiredHeading = 123f,
            RunRate = 2.75f,
        };
        var body = AceWireWriters.UpdateMotion(0x50000001, 1, 2, 3, false, movement);

        Assert.True(UpdateMotionCodec.TryParse(body, out var update));
        var motion = update.Motion;
        Assert.Equal(MovementType.MoveToObject, motion.MovementType);
        Assert.Equal(
            new MoveToData(0x80001111, 0xA9B4001A, new Vector3(10.5f, 20.25f, 30f), new MoveToParameters(0x0001EE0F, 0.6f, 0.1f, 50f, 1.25f, 15f, 123f), 2.75f),
            motion.MoveTo);
        Assert.Null(motion.TurnTo);
        Assert.Empty(motion.Commands);
        ObjectEventCodecs_AssertExact(body);
    }

    [Fact]
    public void MoveToPosition_has_no_target()
    {
        var movement = new AceMovementData { MovementType = 7, OriginCell = 0x01020304, OriginX = 1, ParameterFlags = 2, RunRate = 1.5f };
        var body = AceWireWriters.UpdateMotion(1, 0, 0, 0, false, movement);
        Assert.True(UpdateMotionCodec.TryParse(body, out var update));
        Assert.Equal(MovementType.MoveToPosition, update.Motion.MovementType);
        Assert.Null(update.Motion.MoveTo!.Value.TargetGuid);
        Assert.Equal(0x01020304u, update.Motion.MoveTo.Value.OriginCellId);
        Assert.Equal(1.5f, update.Motion.MoveTo.Value.RunRate);
        ObjectEventCodecs_AssertExact(body);
    }

    [Fact]
    public void TurnToObject_and_TurnToHeading_round_trip()
    {
        var toObject = AceWireWriters.UpdateMotion(1, 0, 0, 0, false,
            new AceMovementData { MovementType = 8, TargetGuid = 0x80002222, ObjectHeading = 90f, ParameterFlags = 0x10, Speed = 1.5f, DesiredHeading = 45f });
        Assert.True(UpdateMotionCodec.TryParse(toObject, out var a));
        Assert.Equal(new TurnToData(0x80002222, 90f, new TurnToParameters(0x10, 1.5f, 45f)), a.Motion.TurnTo);
        Assert.Equal(MovementType.TurnToObject, a.Motion.MovementType);
        ObjectEventCodecs_AssertExact(toObject);

        var toHeading = AceWireWriters.UpdateMotion(1, 0, 0, 0, false,
            new AceMovementData { MovementType = 9, ParameterFlags = 0x20, Speed = 1f, DesiredHeading = 270f });
        Assert.True(UpdateMotionCodec.TryParse(toHeading, out var b));
        Assert.Equal(new TurnToData(null, null, new TurnToParameters(0x20, 1f, 270f)), b.Motion.TurnTo);
        ObjectEventCodecs_AssertExact(toHeading);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(255)]
    public void A_movement_type_retail_does_not_unpack_is_refused(byte type)
    {
        var body = AceWireWriters.UpdateMotion(1, 0, 0, 0, false, new AceMovementData());
        body[16] = type;
        Assert.False(UpdateMotionCodec.TryParse(body, out _));
    }

    private static void ObjectEventCodecs_AssertExact(byte[] body) =>
        ObjectEventCodecTests.AssertExact(body, static b => UpdateMotionCodec.TryParse(b, out _));
}

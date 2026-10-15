// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/MoveToState.cs, AutonomousPosition.cs and
// JumpAction.cs (MIT; Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: one internal static class,
// MovementActionCodecs, whose writers fill a PacketWriter through GameActionEnvelope with the session's action sequence
// instead of returning new arrays; positions and sequences are the N0 records AcPosition and ObjSeqs (the rotation goes
// out exactly as given); the RawMotionState is RawMotionStateCodec's (defect 1 fixed there) and is validated before
// anything is written; AutonomousPosition's contact is a bool.

using System.Numerics;
using Lifestone.Net.Packets;
using Lifestone.Net.Types;

namespace Lifestone.Net.Messages;

/// <summary>
/// The movement game actions (docs/protocol.md §5.4), in retail's layouts:
/// <list type="bullet">
/// <item>0xF61C MoveToState: RawMotionState, Position (32 bytes), the four sequences, u8 (bit 0 contact, bit 1 standing
/// long jump), Align (<c>MoveToStatePack::Pack</c>, 11.4186 0x005168F0, 11.6096 0x005173F0; sent by
/// <c>CM_Movement::Event_MoveToState</c>, 11.4186 0x006AFC00, 11.6096 0x006B0AD0);</item>
/// <item>0xF753 AutonomousPosition: Position, the four sequences, u8 contact, Align (<c>AutonomousPositionPack::Pack</c>,
/// 11.4186 0x00516AF0, 11.6096 0x005175F0; <c>CM_Movement::Event_AutonomousPosition</c>, 11.4186 0x006AF790, 11.6096
/// 0x006B0660);</item>
/// <item>0xF61B Jump: f32 extent, 3 × f32 velocity, Position, the four sequences, Align: 56 bytes after the action type
/// (<c>JumpPack::Pack</c>, 11.4186 0x00516D10, 11.6096 0x00517810; <c>CM_Movement::Event_Jump</c>, 11.4186 0x006AFA70,
/// 11.6096 0x006B0940). ACE reads only 32 of them and uses the extent and velocity; a 24-byte jump makes it throw.</item>
/// </list>
/// The four sequences echo the player's latest instance, server-control, teleport and force-position values (ACE reads
/// them but does not check them, §6).
/// </summary>
internal static class MovementActionCodecs
{
    /// <summary>The Jump payload after the action type: extent, velocity, position, sequences (no pad needed).</summary>
    public const int JumpPayloadSize = 4 + 12 + PositionCodec.Size + 8;

    /// <summary>MoveToState's contact bit.</summary>
    public const byte ContactBit = 0x1;

    /// <summary>MoveToState's standing-long-jump bit.</summary>
    public const byte StandingLongJumpBit = 0x2;

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0xF61C, MoveToState}.</summary>
    /// <exception cref="ArgumentException">The state has more than 31 actions; nothing is written.</exception>
    public static void WriteMoveToState(PacketWriter writer, uint sequence, in RawMotionStateWire state, in AcPosition position, in ObjSeqs sequences, bool contact, bool standingLongJump)
    {
        RawMotionStateCodec.Validate(state);
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.MoveToState);
        RawMotionStateCodec.Write(writer, state);
        PositionCodec.Write(writer, position);
        PositionCodec.WriteSequences(writer, sequences);
        writer.WriteByte((byte)((contact ? ContactBit : 0) | (standingLongJump ? StandingLongJumpBit : 0)));
        writer.AlignTo4();
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0xF753, AutonomousPosition}.</summary>
    public static void WriteAutonomousPosition(PacketWriter writer, uint sequence, in AcPosition position, in ObjSeqs sequences, bool contact)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.AutonomousPosition);
        PositionCodec.Write(writer, position);
        PositionCodec.WriteSequences(writer, sequences);
        writer.WriteByte(contact ? (byte)1 : (byte)0);
        writer.AlignTo4();
    }

    /// <summary>Writes F7B1 {<paramref name="sequence"/>, 0xF61B, Jump}, the 56-byte retail <c>JumpPack</c>.</summary>
    public static void WriteJump(PacketWriter writer, uint sequence, float extent, Vector3 velocity, in AcPosition position, in ObjSeqs sequences)
    {
        GameActionEnvelope.WriteHeader(writer, sequence, GameActionTypes.Jump);
        writer.WriteFloat(extent);
        PositionCodec.WriteVector3(writer, velocity);
        PositionCodec.Write(writer, position);
        PositionCodec.WriteSequences(writer, sequences);
        writer.AlignTo4();
    }
}

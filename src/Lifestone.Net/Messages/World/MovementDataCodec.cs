// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/CreateObject.cs and
// src/AcDream.Core.Net/Messages/UpdateMotion.cs (MIT; Copyright (c) 2026 Erik Nihlén and OpenAC contributors)
// with changes: the movement body both files read (CreateObject.TryParseMovementData, the body half of
// UpdateMotion.TryParse, TryParseMoveToPayload and TryParseTurnToPayload) as one internal static MovementDataCodec over
// N2's WireReader that returns false instead of catching; defect 2 fixed (docs/protocol.md §8.2): the interpreted state
// ends with Align before the sticky GUID, as ACE's InterpretedMotionState.Write and retail's
// InterpretedMotionState::UnPack align, where OpenAC read the GUID 2 bytes early whenever the u16 fields were odd; the
// command count is retail's 5 bits; a body cut short is refused instead of returned half-read; an unknown movement type
// is refused instead of returned empty; MoveTo keeps every parameter, and the result is the N0 record ServerMotionState.

using System.Collections.Immutable;
using Lifestone.Net.Types;
using Lifestone.Net.Wire;

namespace Lifestone.Net.Messages;

/// <summary>
/// The body of a server MovementData, without its header (docs/protocol.md §5.3): u8 movement type, u8 motion flags,
/// u16 stance, then by type an interpreted motion state (0), a MoveTo (6, 7) or a TurnTo (8, 9). F74C UpdateMotion
/// carries it after its sequence header; F745 CreateObject inside its PhysicsDesc, length-prefixed.
/// </summary>
/// <remarks>
/// <para>Retail reads it in <c>MovementManager::unpack_movement</c> (11.4186 0x00524440, 11.6096 0x00525040): the
/// interpreted state with <c>InterpretedMotionState::UnPack</c> (11.4186 0x0051F400, 11.6096 0x0051FF10), which ends by
/// aligning to 4, then the sticky GUID when motion flag 0x1 is set; the MoveTo and TurnTo parameters with
/// <c>MovementParameters::UnPackNet</c> (11.4186 0x0052AC50, 11.6096 0x0052B860). An unknown type makes retail return
/// without moving the object; here it makes the message malformed, so it is reported.</para>
/// <para><b>Defect 2.</b> The interpreted state is u32 flags | count &lt;&lt; 7, up to four u16 commands, up to three f32
/// speeds and 8-byte commands, so it ends 2 bytes short of a multiple of 4 whenever an odd number of the u16 fields is
/// present; ACE (<c>InterpretedMotionState.Write</c>, then <c>MovementInvalid.Write</c>) and retail both pad before the
/// sticky GUID. Alignment is measured from the reader's start: the message's opcode for UpdateMotion; for CreateObject the
/// movement buffer, which retail's <c>PhysicsDesc::UnPack</c> (11.4186 0x0051DDD0, 11.6096 0x0051E8E0) copies into its own
/// allocation and ACE writes at a 4-byte boundary of the message, so the two agree.</para>
/// <para>The motion commands stay u16 low words (map them with <c>Lifestone.Data.Animation.MotionCommandTable</c>). The
/// command count is bits 7-11 of the flags word, as retail reads it (<c>(flags &gt;&gt; 7) &amp; 0x1F</c>).</para>
/// </remarks>
internal static class MovementDataCodec
{
    /// <summary>Interpreted-state flag: the style (u16) is present.</summary>
    public const uint StyleFlag = 0x1;

    /// <summary>Interpreted-state flag: the forward command (u16) is present.</summary>
    public const uint ForwardFlag = 0x2;

    /// <summary>Interpreted-state flag: the forward speed (f32) is present.</summary>
    public const uint ForwardSpeedFlag = 0x4;

    /// <summary>Interpreted-state flag: the sidestep command (u16) is present.</summary>
    public const uint SidestepFlag = 0x8;

    /// <summary>Interpreted-state flag: the sidestep speed (f32) is present.</summary>
    public const uint SidestepSpeedFlag = 0x10;

    /// <summary>Interpreted-state flag: the turn command (u16) is present.</summary>
    public const uint TurnFlag = 0x20;

    /// <summary>Interpreted-state flag: the turn speed (f32) is present.</summary>
    public const uint TurnSpeedFlag = 0x40;

    /// <summary>Where the command count starts in the flags word.</summary>
    public const int CommandCountShift = 7;

    /// <summary>The command count's mask after the shift (retail reads 5 bits).</summary>
    public const uint CommandCountMask = 0x1F;

    /// <summary>The size of one command: u16 command, u16 stamp word, f32 speed.</summary>
    public const int MotionItemSize = 8;

    /// <summary>Reads a header-less MovementData body; the reader must be positioned at its movement-type byte.</summary>
    public static bool TryRead(ref WireReader reader, out ServerMotionState motion)
    {
        motion = null!;
        if (!reader.TryReadU8(out var type) || !reader.TryReadU8(out var flags) || !reader.TryReadU16(out var stance))
        {
            return false;
        }

        var motionFlags = (MotionFlags)flags;
        switch ((MovementType)type)
        {
            case MovementType.Interpreted:
                return TryReadInterpreted(ref reader, motionFlags, stance, out motion);

            case MovementType.MoveToObject or MovementType.MoveToPosition:
                uint? target = null;
                if ((MovementType)type == MovementType.MoveToObject)
                {
                    if (!reader.TryReadU32(out var moveTarget))
                    {
                        return false;
                    }

                    target = moveTarget;
                }

                if (!PositionCodec.TryReadOrigin(ref reader, out var cell, out var origin)
                    || !TryReadMoveToParameters(ref reader, out var moveTo)
                    || !reader.TryReadF32(out var runRate))
                {
                    return false;
                }

                motion = new ServerMotionState
                {
                    MovementType = (MovementType)type,
                    MotionFlags = motionFlags,
                    Stance = stance,
                    MoveTo = new MoveToData(target, cell, origin, moveTo, runRate),
                };
                return true;

            case MovementType.TurnToObject or MovementType.TurnToHeading:
                uint? turnTarget = null;
                float? heading = null;
                if ((MovementType)type == MovementType.TurnToObject)
                {
                    if (!reader.TryReadU32(out var turnGuid) || !reader.TryReadF32(out var objectHeading))
                    {
                        return false;
                    }

                    turnTarget = turnGuid;
                    heading = objectHeading;
                }

                if (!reader.TryReadU32(out var turnFlags) || !reader.TryReadF32(out var speed) || !reader.TryReadF32(out var desired))
                {
                    return false;
                }

                motion = new ServerMotionState
                {
                    MovementType = (MovementType)type,
                    MotionFlags = motionFlags,
                    Stance = stance,
                    TurnTo = new TurnToData(turnTarget, heading, new TurnToParameters(turnFlags, speed, desired)),
                };
                return true;

            default:
                return false;
        }
    }

    private static bool TryReadInterpreted(ref WireReader reader, MotionFlags motionFlags, ushort stance, out ServerMotionState motion)
    {
        motion = null!;
        if (!reader.TryReadU32(out var word))
        {
            return false;
        }

        ushort? style = null, forward = null, sidestep = null, turn = null;
        float? forwardSpeed = null, sidestepSpeed = null, turnSpeed = null;
        if (!TryReadU16If(ref reader, word, StyleFlag, ref style)
            || !TryReadU16If(ref reader, word, ForwardFlag, ref forward)
            || !TryReadU16If(ref reader, word, SidestepFlag, ref sidestep)
            || !TryReadU16If(ref reader, word, TurnFlag, ref turn)
            || !TryReadF32If(ref reader, word, ForwardSpeedFlag, ref forwardSpeed)
            || !TryReadF32If(ref reader, word, SidestepSpeedFlag, ref sidestepSpeed)
            || !TryReadF32If(ref reader, word, TurnSpeedFlag, ref turnSpeed))
        {
            return false;
        }

        var count = (int)((word >> CommandCountShift) & CommandCountMask);
        var commands = ImmutableArray<MotionItem>.Empty;
        if (count > 0)
        {
            if (reader.Remaining < count * MotionItemSize)
            {
                return false;
            }

            var builder = ImmutableArray.CreateBuilder<MotionItem>(count);
            for (var i = 0; i < count; i++)
            {
                if (!TryReadMotionItem(ref reader, out var item))
                {
                    return false;
                }

                builder.Add(item);
            }

            commands = builder.MoveToImmutable();
        }

        // Defect 2: the state is padded to 4 before the sticky GUID (InterpretedMotionState::UnPack's closing align).
        if (!reader.TryAlign())
        {
            return false;
        }

        uint? sticky = null;
        if ((motionFlags & MotionFlags.StickToObject) != 0)
        {
            if (!reader.TryReadU32(out var guid))
            {
                return false;
            }

            sticky = guid;
        }

        motion = new ServerMotionState
        {
            MovementType = MovementType.Interpreted,
            MotionFlags = motionFlags,
            Stance = stance,
            Style = style,
            Forward = forward,
            Sidestep = sidestep,
            Turn = turn,
            ForwardSpeed = forwardSpeed,
            SidestepSpeed = sidestepSpeed,
            TurnSpeed = turnSpeed,
            Commands = commands,
            StickyGuid = sticky,
        };
        return true;
    }

    /// <summary>One command: u16 command low word, u16 stamp (low 15 bits) with the autonomous bit 15, f32 speed.</summary>
    private static bool TryReadMotionItem(ref WireReader reader, out MotionItem item)
    {
        if (!reader.TryReadU16(out var command) || !reader.TryReadU16(out var stampWord) || !reader.TryReadF32(out var speed))
        {
            item = default;
            return false;
        }

        item = new MotionItem(command, (ushort)(stampWord & 0x7FFF), (stampWord & 0x8000) != 0, speed);
        return true;
    }

    /// <summary>The 28-byte MoveTo parameters (<c>MovementParameters::UnPackNet</c> for types 6 and 7).</summary>
    private static bool TryReadMoveToParameters(ref WireReader reader, out MoveToParameters parameters)
    {
        if (!reader.TryReadU32(out var flags)
            || !reader.TryReadF32(out var distance)
            || !reader.TryReadF32(out var min)
            || !reader.TryReadF32(out var fail)
            || !reader.TryReadF32(out var speed)
            || !reader.TryReadF32(out var walkRun)
            || !reader.TryReadF32(out var heading))
        {
            parameters = default;
            return false;
        }

        parameters = new MoveToParameters(flags, distance, min, fail, speed, walkRun, heading);
        return true;
    }

    private static bool TryReadU16If(ref WireReader reader, uint word, uint flag, ref ushort? value)
    {
        if ((word & flag) == 0)
        {
            return true;
        }

        if (!reader.TryReadU16(out var read))
        {
            return false;
        }

        value = read;
        return true;
    }

    private static bool TryReadF32If(ref WireReader reader, uint word, uint flag, ref float? value)
    {
        if ((word & flag) == 0)
        {
            return true;
        }

        if (!reader.TryReadF32(out var read))
        {
            return false;
        }

        value = read;
        return true;
    }
}

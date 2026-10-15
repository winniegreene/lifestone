// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Messages/RawMotionStatePacker.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: an internal static RawMotionStateCodec that writes
// the N0 record RawMotionStateWire (OpenAC's AcDream.Core.Physics.RawMotionState) into a PacketWriter; defect 1 fixed
// (docs/protocol.md §8.2): each action is 8 bytes, {u16 command low word, u16 stamp | autonomous << 15, f32 speed},
// where OpenAC left out the speed and ACE then read the position from the wrong offset; more actions than the 5-bit count
// holds are refused instead of overflowing into the flags.

using System.Collections.Immutable;
using Lifestone.Net.Packets;
using Lifestone.Net.Types;

namespace Lifestone.Net.Messages;

/// <summary>
/// RawMotionState, the client's input state at the head of a MoveToState (docs/protocol.md §5.4): u32 flags (11 bits)
/// | action count &lt;&lt; 11, then each field whose value differs from its retail default (bit order: current hold
/// key, current style, forward command, forward hold key, forward speed, sidestep command, sidestep hold key, sidestep
/// speed, turn command, turn hold key, turn speed; commands are full u32 ids), then the actions, 8 bytes each. Retail
/// packs it with <c>RawMotionState::Pack</c> (11.4186 0x0051ED10, 11.6096 0x0051F820), comparing each field with
/// the defaults <c>RawMotionState::RawMotionState</c> sets (11.4186 0x0051E7F0, 11.6096 0x0051F300;
/// <see cref="RawMotionStateWire.Default"/>) and keeping 5 bits of count.
/// </summary>
internal static class RawMotionStateCodec
{
    /// <summary>The most actions the 5-bit count can carry.</summary>
    public const int MaxActions = 31;

    /// <summary>Where the action count starts in the flags word.</summary>
    public const int ActionCountShift = 11;

    private const uint CurrentHoldKeyFlag = 0x001;
    private const uint CurrentStyleFlag = 0x002;
    private const uint ForwardCommandFlag = 0x004;
    private const uint ForwardHoldKeyFlag = 0x008;
    private const uint ForwardSpeedFlag = 0x010;
    private const uint SidestepCommandFlag = 0x020;
    private const uint SidestepHoldKeyFlag = 0x040;
    private const uint SidestepSpeedFlag = 0x080;
    private const uint TurnCommandFlag = 0x100;
    private const uint TurnHoldKeyFlag = 0x200;
    private const uint TurnSpeedFlag = 0x400;

    /// <summary>Throws unless <paramref name="state"/> can be packed (at most <see cref="MaxActions"/> actions).</summary>
    /// <exception cref="ArgumentException">More than 31 actions.</exception>
    public static void Validate(in RawMotionStateWire state)
    {
        var actions = state.Actions.IsDefault ? 0 : state.Actions.Length;
        if (actions > MaxActions)
        {
            throw new ArgumentException($"a RawMotionState carries at most {MaxActions} actions (a 5-bit count); this one has {actions}.", nameof(state));
        }
    }

    /// <summary>Writes <paramref name="state"/>; call <see cref="Validate"/> first.</summary>
    public static void Write(PacketWriter writer, in RawMotionStateWire state)
    {
        var flags = Flags(state);
        var actions = state.Actions.IsDefault ? ImmutableArray<RawMotionAction>.Empty : state.Actions;
        writer.WriteUInt32(flags | ((uint)actions.Length << ActionCountShift));
        if ((flags & CurrentHoldKeyFlag) != 0) writer.WriteUInt32(state.CurrentHoldKey);
        if ((flags & CurrentStyleFlag) != 0) writer.WriteUInt32(state.CurrentStyle);
        if ((flags & ForwardCommandFlag) != 0) writer.WriteUInt32(state.ForwardCommand);
        if ((flags & ForwardHoldKeyFlag) != 0) writer.WriteUInt32(state.ForwardHoldKey);
        if ((flags & ForwardSpeedFlag) != 0) writer.WriteFloat(state.ForwardSpeed);
        if ((flags & SidestepCommandFlag) != 0) writer.WriteUInt32(state.SidestepCommand);
        if ((flags & SidestepHoldKeyFlag) != 0) writer.WriteUInt32(state.SidestepHoldKey);
        if ((flags & SidestepSpeedFlag) != 0) writer.WriteFloat(state.SidestepSpeed);
        if ((flags & TurnCommandFlag) != 0) writer.WriteUInt32(state.TurnCommand);
        if ((flags & TurnHoldKeyFlag) != 0) writer.WriteUInt32(state.TurnHoldKey);
        if ((flags & TurnSpeedFlag) != 0) writer.WriteFloat(state.TurnSpeed);

        foreach (var action in actions)
        {
            // Defect 1: the speed follows the stamp word, 8 bytes per action.
            writer.WriteUInt16((ushort)action.Command);
            writer.WriteUInt16((ushort)((action.Stamp & 0x7FFF) | (action.Autonomous ? 0x8000 : 0)));
            writer.WriteFloat(action.Speed);
        }
    }

    /// <summary>The fields that differ from retail's defaults. Speeds compare by their bits, as retail does.</summary>
    private static uint Flags(in RawMotionStateWire state)
    {
        var d = RawMotionStateWire.Default;
        uint flags = 0;
        if (state.CurrentHoldKey != d.CurrentHoldKey) flags |= CurrentHoldKeyFlag;
        if (state.CurrentStyle != d.CurrentStyle) flags |= CurrentStyleFlag;
        if (state.ForwardCommand != d.ForwardCommand) flags |= ForwardCommandFlag;
        if (state.ForwardHoldKey != d.ForwardHoldKey) flags |= ForwardHoldKeyFlag;
        if (!SameBits(state.ForwardSpeed, d.ForwardSpeed)) flags |= ForwardSpeedFlag;
        if (state.SidestepCommand != d.SidestepCommand) flags |= SidestepCommandFlag;
        if (state.SidestepHoldKey != d.SidestepHoldKey) flags |= SidestepHoldKeyFlag;
        if (!SameBits(state.SidestepSpeed, d.SidestepSpeed)) flags |= SidestepSpeedFlag;
        if (state.TurnCommand != d.TurnCommand) flags |= TurnCommandFlag;
        if (state.TurnHoldKey != d.TurnHoldKey) flags |= TurnHoldKeyFlag;
        if (!SameBits(state.TurnSpeed, d.TurnSpeed)) flags |= TurnSpeedFlag;
        return flags;
    }

    private static bool SameBits(float a, float b) => BitConverter.SingleToUInt32Bits(a) == BitConverter.SingleToUInt32Bits(b);
}

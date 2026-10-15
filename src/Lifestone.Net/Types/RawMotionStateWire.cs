using System.Collections.Immutable;

namespace Lifestone.Net.Types;

/// <summary>
/// The RawMotionState a MoveToState (F7B1/F61C) carries, as the wire wants it (docs/protocol.md §5.4; retail
/// <c>RawMotionState::Pack</c>, 11.4186 0x0051ED10, 11.6096 0x0051F820). Commands are <b>full u32</b> 11.6096 ids here
/// (for example WalkForward 0x45000005); each field is written only when it differs from its retail default
/// (<see cref="Default"/>), and actions are 8 bytes each.
/// </summary>
/// <remarks>
/// <para>Named <c>RawMotionStateWire</c> so it never collides with <c>Lifestone.Data.Motion.RawMotionState</c>, the
/// behaviour-carrying class the motion interpreter keeps; the state track maps one to the other (hold keys as their
/// u32 values). Lifestone.Net references no other Lifestone project.</para>
/// <para>Start from <see cref="Default"/> and use <c>with</c>: <c>default(RawMotionStateWire)</c> is all zeros, which is
/// not the retail default (the current hold key would be Invalid rather than None).</para>
/// </remarks>
/// <param name="CurrentHoldKey">Flag 0x1; default None (1). Running is WalkForward plus hold key Run (2).</param>
/// <param name="CurrentStyle">Flag 0x2; default NonCombat (0x8000003D).</param>
/// <param name="ForwardCommand">Flag 0x4; default Ready (0x41000003).</param>
/// <param name="ForwardHoldKey">Flag 0x8; default Invalid (0).</param>
/// <param name="ForwardSpeed">Flag 0x10; default 1.</param>
/// <param name="SidestepCommand">Flag 0x20; default 0.</param>
/// <param name="SidestepHoldKey">Flag 0x40; default Invalid (0).</param>
/// <param name="SidestepSpeed">Flag 0x80; default 1.</param>
/// <param name="TurnCommand">Flag 0x100; default 0.</param>
/// <param name="TurnHoldKey">Flag 0x200; default Invalid (0).</param>
/// <param name="TurnSpeed">Flag 0x400; default 1.</param>
/// <param name="Actions">The queued actions, oldest first (ACE accepts them only for soul emotes at speed 1).</param>
public readonly record struct RawMotionStateWire(
    uint CurrentHoldKey,
    uint CurrentStyle,
    uint ForwardCommand,
    uint ForwardHoldKey,
    float ForwardSpeed,
    uint SidestepCommand,
    uint SidestepHoldKey,
    float SidestepSpeed,
    uint TurnCommand,
    uint TurnHoldKey,
    float TurnSpeed,
    ImmutableArray<RawMotionAction> Actions)
{
    /// <summary>
    /// Retail's defaults (<c>RawMotionState::RawMotionState</c>, 11.4186 0x0051E7F0, 11.6096 0x0051F300, as ACE's
    /// RawMotionState.cs:100-115 reproduces them): what an all-default MoveToState leaves out.
    /// </summary>
    public static RawMotionStateWire Default { get; } = new(
        CurrentHoldKey: 1,
        CurrentStyle: 0x8000003D,
        ForwardCommand: 0x41000003,
        ForwardHoldKey: 0,
        ForwardSpeed: 1f,
        SidestepCommand: 0,
        SidestepHoldKey: 0,
        SidestepSpeed: 1f,
        TurnCommand: 0,
        TurnHoldKey: 0,
        TurnSpeed: 1f,
        Actions: []);
}

/// <summary>
/// One action of a <see cref="RawMotionStateWire"/>: 8 bytes on the wire, {u16 command low word, u16 stamp &amp; 0x7FFF
/// | autonomous &lt;&lt; 15, f32 speed} (defect 1 of OpenAC's packer, which wrote 4, is fixed by N7).
/// </summary>
/// <param name="Command">The full u32 command (the codec writes its low word).</param>
/// <param name="Stamp">The 15-bit action stamp.</param>
/// <param name="Autonomous">True when the client issued it (bit 15).</param>
/// <param name="Speed">The speed multiplier.</param>
public readonly record struct RawMotionAction(uint Command, ushort Stamp, bool Autonomous, float Speed);

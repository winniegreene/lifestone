// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Animation/ActionNode.cs (AGPL-3.0)
// with changes: data and defaults only; an immutable record struct, and the stamp is the 15-bit server action stamp
// held in a ushort (ACE keeps an int).
namespace Lifestone.Data.Motion;

/// <summary>
/// One queued action of a motion state (retail <c>ActionNode</c>): an action command such as a gesture or an attack,
/// its speed, the action stamp it was sent with and whether the client issued it on its own (autonomous) rather than
/// the server.
/// </summary>
/// <param name="Action">The action motion command (a <c>MotionCommands</c> value with the action bit set).</param>
/// <param name="Speed">Animation speed multiplier (1 = normal).</param>
/// <param name="Stamp">
/// The 15-bit server action stamp: the packed wire word masked with <see cref="World.Sequence16.ActionStampMask"/>
/// (0x7FFF), since bit 15 of that word is <paramref name="Autonomous"/>. Compare stamps with
/// <see cref="World.Sequence16.IsNewerAction"/> (retail <c>CMotionInterp::move_to_interpreted_state</c>'s 15-bit rule),
/// never with the 16-bit <see cref="World.Sequence16.IsNewer"/>.
/// </param>
/// <param name="Autonomous">
/// True when the client initiated the action (retail <c>autonomous</c>; bit 15 of the packed wire word).
/// </param>
public readonly record struct MotionActionNode(uint Action, float Speed, ushort Stamp, bool Autonomous);

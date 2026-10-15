namespace Lifestone.Net.Types;

/// <summary>
/// The four 16-bit sequences the client echoes after its position in MoveToState, AutonomousPosition and Jump
/// (docs/protocol.md §5.4): the latest values the server sent for the player's own object. ACE reads them but never
/// checks them (§6); retail sends them (<c>MoveToStatePack::Pack</c>, <c>AutonomousPositionPack::Pack</c>,
/// <c>JumpPack::Pack</c>).
/// </summary>
/// <param name="Instance">The object's instance sequence.</param>
/// <param name="ServerControl">The server-control sequence (from UpdateMotion).</param>
/// <param name="Teleport">The teleport sequence (from PlayerTeleport and UpdatePosition).</param>
/// <param name="ForcePosition">The force-position sequence (from UpdatePosition).</param>
public readonly record struct ObjSeqs(ushort Instance, ushort ServerControl, ushort Teleport, ushort ForcePosition);

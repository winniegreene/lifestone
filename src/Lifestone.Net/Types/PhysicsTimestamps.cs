namespace Lifestone.Net.Types;

/// <summary>
/// The nine u16 sequences at the end of a CreateObject PhysicsDesc, in wire order (docs/protocol.md §5.3). The reducer
/// compares later updates against them with <c>Lifestone.Data.World.Sequence16</c>.
/// </summary>
/// <param name="Position">Position sequence (UpdatePosition).</param>
/// <param name="Movement">Movement sequence (UpdateMotion).</param>
/// <param name="State">State sequence (SetState).</param>
/// <param name="Vector">Vector sequence (VectorUpdate).</param>
/// <param name="Teleport">Teleport sequence (PlayerTeleport, UpdatePosition).</param>
/// <param name="ServerControl">Server-control sequence (UpdateMotion).</param>
/// <param name="ForcePosition">Force-position sequence (UpdatePosition).</param>
/// <param name="ObjDesc">Visual-description sequence (ObjDescEvent).</param>
/// <param name="Instance">Instance sequence: a change means the object was re-created.</param>
public readonly record struct PhysicsTimestamps(
    ushort Position,
    ushort Movement,
    ushort State,
    ushort Vector,
    ushort Teleport,
    ushort ServerControl,
    ushort ForcePosition,
    ushort ObjDesc,
    ushort Instance);
